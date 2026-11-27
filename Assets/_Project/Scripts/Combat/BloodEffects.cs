using System.Collections;
using System.Collections.Generic;
using Invector;
using OpenworldCrime.Player;
using OpenworldCrime.Vehicles;
using UnityEngine;

namespace OpenworldCrime.Combat
{
    /// <summary>
    /// Blood for hard falls and hits (scene system under ///Manager///, not a Singleton): a splash at the impact and, once the body
    /// has come to rest, a blood mark on the ground under it that fades out after a while. GTA-like and restrained: one splash and
    /// one mark per event, more only for a death.
    /// - The playable characters: hurt by a car, thrown off a bike / out through the windscreen, a bail-out, an explosion or a heavy
    ///   fall (their Invector health's damage event, by damage type and amount), and death.
    /// - Anybody a vehicle hits hard (<see cref="VehicleCharacterImpact.OnCharacterHit"/>): pedestrians, officers, the player.
    /// - A hard landing from a fall (<see cref="PlayerFreeFall.OnHardLanding"/>): a bigger burst (several splashes fanned out
    ///   over the ground, scaled by how hard) with a wet impact sound, and a splatter on the ground that spreads.
    /// Splashes and marks are pooled (created once at start); only the few marks currently fading are touched per frame.
    /// </summary>
    [DisallowMultipleComponent]
    public class BloodEffects : MonoBehaviour
    {
        [Header("Assets")]
        [Tooltip("Blood splash particle (Invector's defaultDamageEffect). Pooled copies; its self-destroy is removed on them.")]
        [SerializeField] private GameObject splashPrefab;
        [Tooltip("Transparent blood-splatter material for the marks on the ground.")]
        [SerializeField] private Material markMaterial;

        [Header("Who")]
        [Tooltip("Health of each playable character (both are in the scene; only the active one gets hurt).")]
        [SerializeField] private vHealthController[] players = new vHealthController[0];
        [Tooltip("Player damage types that bleed (Invector damageType).")]
        [SerializeField] private string[] playerDamageTypes = { "Vehicle", "VehicleBail", "WindshieldEjection", "Explosion", "FallDamage" };
        [Tooltip("Player damage below this does not bleed.")]
        [SerializeField, Min(0f)] private float minPlayerDamage = 8f;
        [Tooltip("A vehicle hit on anybody bleeds from this impact speed (m/s).")]
        [SerializeField, Min(0f)] private float minVehicleHitSpeed = 6f;

        [Header("Hard Landing (falls)")]
        [Tooltip("Splashes fanned out at a hard landing: at the least and at a lethal landing.")]
        [SerializeField] private Vector2Int landingSplashes = new Vector2Int(2, 4);
        [Tooltip("Ground splatter size (m) at the least and at a lethal landing (spreads out like a death pool).")]
        [SerializeField] private Vector2 landingMarkSize = new Vector2(2.2f, 3.8f);
        [Tooltip("Wet impact sounds, one picked at random (optional).")]
        [SerializeField] private AudioClip[] landingClips = new AudioClip[0];
        [SerializeField, Range(0f, 1f)] private float landingVolume = 0.9f;

        [Header("Marks On The Ground")]
        [SerializeField, Min(1)] private int splashPoolSize = 8;
        [SerializeField, Min(1)] private int markPoolSize = 16;
        [Tooltip("Mark size (m), random between the two, for a hit / a fall.")]
        [SerializeField] private Vector2 markSize = new Vector2(0.7f, 1.2f);
        [Tooltip("Mark size (m) a dead body's pool spreads to.")]
        [SerializeField, Min(0.1f)] private float deathPoolSize = 1.9f;
        [SerializeField, Min(0.1f)] private float deathPoolSpreadSeconds = 6f;
        [Tooltip("Seconds a mark stays before it fades, and the fade.")]
        [SerializeField, Min(1f)] private float markLifetime = 30f;
        [SerializeField, Min(0.1f)] private float markFadeSeconds = 4f;
        [SerializeField] private Color markColor = new Color(0.32f, 0.02f, 0.02f, 0.9f);
        [SerializeField] private LayerMask groundLayers = 1;
        [Tooltip("Height (m) of a mark above the ground collider: clears thin painted surfaces laid over it (airport apron, " +
                 "road decals) without floating visibly.")]
        [SerializeField, Range(0.005f, 0.15f)] private float markLift = 0.06f;
        [Tooltip("Seconds a falling body is given to come to rest before its mark is placed.")]
        [SerializeField, Min(0.1f)] private float maxSettleSeconds = 3f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private class Mark
        {
            public Transform Transform;
            public Renderer Renderer;
            public float BornAt;
            public bool Active;
        }

        private readonly List<ParticleSystem[]> splashes = new List<ParticleSystem[]>();
        private readonly List<Transform> splashRoots = new List<Transform>();
        private readonly List<Mark> marks = new List<Mark>();
        private readonly List<UnityEngine.Events.UnityAction<vDamage>> damageHandlers = new List<UnityEngine.Events.UnityAction<vDamage>>();
        private readonly List<UnityEngine.Events.UnityAction<GameObject>> deathHandlers = new List<UnityEngine.Events.UnityAction<GameObject>>();
        private MaterialPropertyBlock block;
        private Transform poolRoot;
        private int nextSplash;
        private int nextMark;
        private float lastVehicleBlood;

        private void Awake()
        {
            if (splashPrefab == null || markMaterial == null)
                Debug.LogWarning($"[{nameof(BloodEffects)}] Splash prefab or mark material missing on '{name}'; no blood.", this);
            block = new MaterialPropertyBlock();
            poolRoot = new GameObject("Blood Pool").transform;
            if (splashPrefab != null)
            {
                for (int i = 0; i < splashPoolSize; i++)
                {
                    GameObject s = Instantiate(splashPrefab, poolRoot);
                    foreach (var d in s.GetComponentsInChildren<vDestroyGameObject>(true)) Destroy(d);
                    splashRoots.Add(s.transform);
                    splashes.Add(s.GetComponentsInChildren<ParticleSystem>(true));
                }
            }
            if (markMaterial != null)
            {
                for (int i = 0; i < markPoolSize; i++)
                {
                    GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    quad.name = "Blood Mark";
                    Destroy(quad.GetComponent<Collider>());
                    quad.transform.SetParent(poolRoot, false);
                    var r = quad.GetComponent<Renderer>();
                    r.sharedMaterial = markMaterial;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    quad.SetActive(false);
                    marks.Add(new Mark { Transform = quad.transform, Renderer = r });
                }
            }
        }

        private void OnEnable()
        {
            VehicleCharacterImpact.OnCharacterHit += HandleVehicleHit;
            PlayerFreeFall.OnHardLanding += HandleHardLanding;
            damageHandlers.Clear();
            deathHandlers.Clear();
            for (int i = 0; i < players.Length; i++)
            {
                vHealthController player = players[i];
                if (player == null) { damageHandlers.Add(null); deathHandlers.Add(null); continue; }
                UnityEngine.Events.UnityAction<vDamage> onDamage = d => HandlePlayerDamage(player, d);
                UnityEngine.Events.UnityAction<GameObject> onDead = _ => HandlePlayerDied(player);
                player.onReceiveDamage.AddListener(onDamage);
                player.onDead.AddListener(onDead);
                damageHandlers.Add(onDamage);
                deathHandlers.Add(onDead);
            }
        }

        private void OnDisable()
        {
            VehicleCharacterImpact.OnCharacterHit -= HandleVehicleHit;
            PlayerFreeFall.OnHardLanding -= HandleHardLanding;
            for (int i = 0; i < players.Length && i < damageHandlers.Count; i++)
            {
                if (players[i] == null || damageHandlers[i] == null) continue;
                players[i].onReceiveDamage.RemoveListener(damageHandlers[i]);
                players[i].onDead.RemoveListener(deathHandlers[i]);
            }
            damageHandlers.Clear();
            deathHandlers.Clear();
        }

        // Only marks that are fading are touched (a handful at most).
        private void Update()
        {
            float now = Time.time;
            for (int i = 0; i < marks.Count; i++)
            {
                Mark m = marks[i];
                if (!m.Active) continue;
                float age = now - m.BornAt;
                if (age < markLifetime) continue;
                float k = 1f - (age - markLifetime) / markFadeSeconds;
                if (k <= 0f)
                {
                    m.Active = false;
                    m.Transform.gameObject.SetActive(false);
                    continue;
                }
                SetAlpha(m, markColor.a * k);
            }
        }

        /// <summary>A splash at a point, spraying along a direction.</summary>
        public void Splash(Vector3 point, Vector3 direction)
        {
            if (splashRoots.Count == 0) return;
            int i = nextSplash;
            nextSplash = (nextSplash + 1) % splashRoots.Count;
            if (direction.sqrMagnitude < 0.001f) direction = Vector3.up;
            splashRoots[i].SetPositionAndRotation(point, Quaternion.LookRotation(direction.normalized));
            ParticleSystem[] systems = splashes[i];
            for (int s = 0; s < systems.Length; s++)
            {
                systems[s].Clear(true);
                systems[s].Play(true);
            }
        }

        /// <summary>A blood mark on the ground under a point (nothing if there is no ground within reach).</summary>
        public void MarkGround(Vector3 above, float size)
        {
            if (marks.Count == 0 || !Physics.Raycast(above + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 3f, groundLayers, QueryTriggerInteraction.Ignore)) return;
            Mark m = marks[nextMark];
            nextMark = (nextMark + 1) % marks.Count;
            m.Transform.gameObject.SetActive(true);
            m.Transform.SetPositionAndRotation(hit.point + hit.normal * markLift,
                Quaternion.LookRotation(-hit.normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));
            m.Transform.localScale = Vector3.one * size;
            m.BornAt = Time.time;
            m.Active = true;
            SetAlpha(m, markColor.a);
            if (size > markSize.y) StartCoroutine(Spread(m, size));
        }

        private IEnumerator Spread(Mark m, float size)
        {
            float born = m.BornAt;
            for (float t = 0f; t < deathPoolSpreadSeconds && m.Active && m.BornAt == born; t += Time.deltaTime)
            {
                m.Transform.localScale = Vector3.one * Mathf.Lerp(size * 0.35f, size, Mathf.SmoothStep(0f, 1f, t / deathPoolSpreadSeconds));
                yield return null;
            }
        }

        private void SetAlpha(Mark m, float alpha)
        {
            Color c = markColor;
            c.a = alpha;
            m.Renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, c);
            m.Renderer.SetPropertyBlock(block);
        }

        private void HandleVehicleHit(VehicleImpact impact)
        {
            // The car driving into the person, not the person running into a standing car.
            if (impact.ClosingSpeed < minVehicleHitSpeed || Time.time - lastVehicleBlood < 0.25f) return;
            lastVehicleBlood = Time.time;
            Splash(impact.Point, impact.VehicleVelocity + Vector3.up * 2f);
            StartCoroutine(MarkLater(impact.Point + impact.VehicleVelocity * 0.15f, 1f, Random.Range(markSize.x, markSize.y)));
        }

        // Several pooled splashes fanned out low over the ground (the pool is sized for it), a wet thud, and a splatter mark
        // right at the impact that spreads out.
        private void HandleHardLanding(PlayerFreeFall faller, Vector3 point, float severity)
        {
            if (faller == null || !faller.gameObject.activeInHierarchy) return;
            int count = Mathf.RoundToInt(Mathf.Lerp(landingSplashes.x, landingSplashes.y, severity));
            float turn = Random.Range(0f, 360f);
            for (int i = 0; i < count; i++)
            {
                float angle = turn + i * 360f / Mathf.Max(1, count);
                Vector3 outward = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Splash(point + outward * 0.15f, outward + Vector3.up * Random.Range(0.6f, 1.2f));
            }
            if (landingClips != null && landingClips.Length > 0)
            {
                AudioClip clip = landingClips[Random.Range(0, landingClips.Length)];
                if (clip != null) AudioSource.PlayClipAtPoint(clip, point, landingVolume);
            }
            MarkGround(point, Mathf.Lerp(landingMarkSize.x, landingMarkSize.y, severity));
        }

        private IEnumerator MarkLater(Vector3 point, float delay, float size)
        {
            yield return new WaitForSeconds(delay);
            MarkGround(point, size);
        }

        private void HandlePlayerDamage(vHealthController player, vDamage damage)
        {
            if (damage == null || damage.damageValue < minPlayerDamage || !player.gameObject.activeInHierarchy) return;
            if (System.Array.IndexOf(playerDamageTypes, damage.damageType) < 0) return;
            Transform hips = Hips(player);
            Vector3 point = damage.hitPosition != Vector3.zero ? damage.hitPosition : hips.position;
            Splash(point, damage.force.sqrMagnitude > 0.01f ? damage.force : Vector3.up);
            StartCoroutine(MarkWhenSettled(hips, Random.Range(markSize.x, markSize.y)));
        }

        private void HandlePlayerDied(vHealthController player)
        {
            if (!player.gameObject.activeInHierarchy) return;
            StartCoroutine(MarkWhenSettled(Hips(player), deathPoolSize));
        }

        // Once the body lies still (or after Max Settle Seconds), the mark goes under the hips.
        private IEnumerator MarkWhenSettled(Transform hips, float size)
        {
            yield return new WaitForSeconds(0.5f);
            Vector3 last = hips.position;
            for (float t = 0.5f; t < maxSettleSeconds; t += 0.25f)
            {
                yield return new WaitForSeconds(0.25f);
                if (hips == null) yield break;
                if ((hips.position - last).sqrMagnitude < 0.0025f) break;
                last = hips.position;
            }
            if (hips != null) MarkGround(hips.position, size);
        }

        private static Transform Hips(vHealthController player)
        {
            Animator animator = player.GetComponent<Animator>();
            Transform hips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
            return hips != null ? hips : player.transform;
        }
    }
}
