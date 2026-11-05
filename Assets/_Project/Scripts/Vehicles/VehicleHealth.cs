using System;
using System.Collections;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>Damage stage of a car.</summary>
    public enum VehicleDamageStage { Healthy, Smoking, Burning, Destroyed }

    /// <summary>
    /// Health of a car (drivable and traffic alike; lives on the root next to the Rigidbody). Bullets reach it through
    /// <see cref="VehicleHitZone"/>, crashes through the collision callback. Stages: smoke when low, then fire (which keeps burning
    /// the car down), then the explosion (<see cref="VehicleExplosion"/>). A destroyed car swaps to its destroyed model if it has
    /// one, otherwise its materials are darkened (per renderer, no material copies); it can no longer be driven or entered and is
    /// cleaned up once off camera (<see cref="OnWreckCleanup"/>: the owner decides what that means, traffic returns to the iTS pool).
    /// Resets itself when re-enabled (iTS pooling, pre-warmed drivables). Nothing runs per frame: coroutines only while burning or wrecked.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class VehicleHealth : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private VehicleDamageSettings settings;
        [Tooltip("Destroyed version of this car (visual only). Empty = the car's own model is darkened and kept as the wreck.")]
        [SerializeField] private GameObject destroyedModelPrefab;
        [Tooltip("Where smoke and fire sit (the engine). Empty = the root.")]
        [SerializeField] private Transform effectAnchor;
        [Tooltip("Lights, exhausts and the like, switched off on the wreck.")]
        [SerializeField] private GameObject[] disableWhenWrecked = new GameObject[0];

        /// <summary>Raised once when a car blows up (any car). Used by the player's side to get out of it.</summary>
        public static event Action<VehicleHealth> OnAnyDestroyed;
        /// <summary>Raised when the stage changes (never for the reset of a pooled car).</summary>
        public event Action<VehicleDamageStage> OnStageChanged;
        /// <summary>Raised once when this car blows up.</summary>
        public event Action OnDestroyedWreck;
        /// <summary>Raised when the wreck should go: off camera and old enough. Without a listener the object is removed.</summary>
        public event Action OnWreckCleanup;

        public float CurrentHealth { get; private set; }
        public VehicleDamageStage Stage { get; private set; }
        public bool IsDestroyed => Stage == VehicleDamageStage.Destroyed;
        public float MaxHealth => settings != null ? settings.maxHealth : 1f;
        public VehicleDamageSettings Settings => settings;

        private Rigidbody body;
        private AudioSource explosionAudio;
        private Renderer[] renderers = new Renderer[0];
        private bool[] rendererWasEnabled = new bool[0];
        private GameObject smokeInstance;
        private GameObject fireInstance;
        private GameObject destroyedInstance;
        private Coroutine burnRoutine;
        private Coroutine wreckRoutine;
        private float lastCrashTime = -100f;
        private bool darkened;
        private bool valid;
        private MaterialPropertyBlock block;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            valid = settings != null;
            if (!valid)
            {
                Debug.LogError($"[{nameof(VehicleHealth)}] No Vehicle Damage Settings on '{name}'. It cannot be damaged.", this);
                return;
            }
            renderers = GetComponentsInChildren<Renderer>(true);
            rendererWasEnabled = new bool[renderers.Length];
        }

        // iTS re-enables pooled cars and the takeover manager re-enables pre-warmed ones: each use starts as a fresh car.
        private void OnEnable()
        {
            if (!valid) return;
            ResetState();
        }

        private void OnDisable()
        {
            burnRoutine = null;
            wreckRoutine = null;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!valid || IsDestroyed || ((1 << collision.gameObject.layer) & settings.crashLayers) == 0) return;
            if (Time.time - lastCrashTime < settings.crashCooldown || collision.contactCount == 0) return;

            // The speed lost along the contact normal: scraping along a wall or a kerb does not count.
            float speed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, collision.GetContact(0).normal));
            if (speed < settings.crashMinimumSpeed) return;
            lastCrashTime = Time.time;
            ApplyDamage((speed - settings.crashMinimumSpeed) * settings.crashDamagePerSpeed, collision.transform);
        }

        /// <summary>Takes damage (bullets, crashes, other explosions).</summary>
        public void ApplyDamage(float amount, Transform sender)
        {
            if (!valid || IsDestroyed || amount <= 0f) return;
            CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
            UpdateStage();
        }

        /// <summary>Carries a taken-over car's damage to the drivable car that replaces it.</summary>
        public void CopyDamageFrom(VehicleHealth other)
        {
            if (!valid || other == null || !other.valid || other.IsDestroyed) return;
            CurrentHealth = Mathf.Min(CurrentHealth, other.CurrentHealth);
            UpdateStage();
        }

        /// <summary>Pushes the car away from an explosion.</summary>
        public void Shove(Vector3 center, float speed)
        {
            if (body == null || body.isKinematic || speed <= 0f) return;
            Vector3 direction = transform.position - center;
            direction.y = 0f;
            direction = (direction.sqrMagnitude > 0.01f ? direction.normalized : transform.forward) + Vector3.up * 0.5f;
            body.AddForce(direction.normalized * speed, ForceMode.VelocityChange);
            body.AddTorque(UnityEngine.Random.onUnitSphere * speed * 0.2f, ForceMode.VelocityChange);
        }

        private void UpdateStage()
        {
            float share = CurrentHealth / settings.maxHealth;
            if (CurrentHealth <= 0f)
            {
                Explode();
                return;
            }
            VehicleDamageStage wanted = share <= settings.fireAtHealth ? VehicleDamageStage.Burning
                : share <= settings.smokeAtHealth ? VehicleDamageStage.Smoking : VehicleDamageStage.Healthy;
            if (wanted <= Stage) return;
            SetStage(wanted);
        }

        private void SetStage(VehicleDamageStage stage)
        {
            Stage = stage;
            if (stage == VehicleDamageStage.Smoking) EnsureEffect(ref smokeInstance, settings.smokeEffect);
            if (stage == VehicleDamageStage.Burning)
            {
                EnsureEffect(ref smokeInstance, settings.smokeEffect);
                EnsureEffect(ref fireInstance, settings.fireEffect);
                if (burnRoutine == null && isActiveAndEnabled) burnRoutine = StartCoroutine(BurnRoutine());
            }
            OnStageChanged?.Invoke(stage);
        }

        private IEnumerator BurnRoutine()
        {
            const float interval = 0.25f;
            var wait = new WaitForSeconds(interval);
            while (!IsDestroyed)
            {
                yield return wait;
                ApplyDamage(settings.burnDamagePerSecond * interval, transform);
            }
            burnRoutine = null;
        }

        private void Explode()
        {
            Stage = VehicleDamageStage.Destroyed;
            Vector3 center = effectAnchor != null ? effectAnchor.position : body.worldCenterOfMass;

            if (settings.explosionEffect != null)
            {
                GameObject fx = Instantiate(settings.explosionEffect, center, Quaternion.identity);
                Destroy(fx, settings.explosionEffectLifetime);
            }
            if (settings.explosionSound != null) AudioSource.PlayClipAtPoint(settings.explosionSound, center);

            ShowWreck();
            EnsureEffect(ref smokeInstance, settings.smokeEffect);
            if (fireInstance != null) Destroy(fireInstance);
            fireInstance = null;

            // The player gets out first (OnAnyDestroyed), so the blast below finds him on foot and only the inside damage counts.
            OnStageChanged?.Invoke(Stage);
            OnDestroyedWreck?.Invoke();
            OnAnyDestroyed?.Invoke(this);

            Shove(center + UnityEngine.Random.insideUnitSphere, settings.explosionCarSpeed);
            if (isActiveAndEnabled)
            {
                // A frame later, so a chain of cars never recurses inside one physics callback.
                StartCoroutine(DetonateNextFrame(center));
                wreckRoutine = StartCoroutine(WreckRoutine());
            }
        }

        private IEnumerator DetonateNextFrame(Vector3 center)
        {
            yield return null;
            VehicleExplosion.Detonate(center, settings, this);
        }

        private void ShowWreck()
        {
            for (int i = 0; i < disableWhenWrecked.Length; i++)
            {
                if (disableWhenWrecked[i] != null) disableWhenWrecked[i].SetActive(false);
            }

            if (destroyedModelPrefab != null)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] == null || renderers[i] is ParticleSystemRenderer) continue;
                    rendererWasEnabled[i] = renderers[i].enabled;
                    renderers[i].enabled = false;
                }
                destroyedInstance = Instantiate(destroyedModelPrefab, transform);
                destroyedInstance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                // The destroyed models are dented but keep their paint: burnt the same way as the other wrecks.
                foreach (Renderer part in destroyedInstance.GetComponentsInChildren<Renderer>(true)) TintBurnt(part);
                return;
            }
            SetDarkened(true);
        }

        // Per-material block: keeps each material's own colour and alpha, tinted down to burnt metal.
        private void SetDarkened(bool on)
        {
            if (on == darkened) return;
            darkened = on;
            if (block == null) block = new MaterialPropertyBlock();
            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer renderer = renderers[r];
                if (renderer == null || renderer is ParticleSystemRenderer) continue;
                if (!on)
                {
                    renderer.SetPropertyBlock(null);
                    for (int m = 0; m < renderer.sharedMaterials.Length; m++) renderer.SetPropertyBlock(null, m);
                    continue;
                }
                TintBurnt(renderer);
            }
        }

        private void TintBurnt(Renderer renderer)
        {
            if (block == null) block = new MaterialPropertyBlock();
            Material[] materials = renderer.sharedMaterials;
            for (int m = 0; m < materials.Length; m++)
            {
                if (materials[m] == null || !materials[m].HasProperty(BaseColorId)) continue;
                Color color = materials[m].GetColor(BaseColorId);
                color.r *= settings.burntTint.r;
                color.g *= settings.burntTint.g;
                color.b *= settings.burntTint.b;
                renderer.GetPropertyBlock(block, m);
                block.SetColor(BaseColorId, color);
                renderer.SetPropertyBlock(block, m);
            }
        }

        // Low-frequency: the smoke dies down, and the wreck goes once it is old enough, off camera and not right next to the player.
        private IEnumerator WreckRoutine()
        {
            var wait = new WaitForSeconds(1f);
            float age = 0f;
            while (true)
            {
                yield return wait;
                age += 1f;
                if (smokeInstance != null && age >= settings.wreckSmokeTime)
                {
                    Destroy(smokeInstance);
                    smokeInstance = null;
                }
                if (age >= settings.wreckMinimumTime && IsOutOfSight(transform.position, settings.wreckKeepDistance)) break;
            }
            wreckRoutine = null;
            if (OnWreckCleanup != null) OnWreckCleanup.Invoke();
            else Destroy(gameObject);
        }

        /// <summary>True when the point is off the main camera's screen and at least keepDistance (m) from it.</summary>
        public static bool IsOutOfSight(Vector3 point, float keepDistance)
        {
            Camera camera = Camera.main;
            if (camera == null) return true;
            if ((camera.transform.position - point).sqrMagnitude < keepDistance * keepDistance) return false;
            Vector3 viewport = camera.WorldToViewportPoint(point);
            const float margin = 0.2f;
            return viewport.z <= 0f || viewport.x < -margin || viewport.x > 1f + margin || viewport.y < -margin || viewport.y > 1f + margin;
        }

        private void EnsureEffect(ref GameObject instance, GameObject prefab)
        {
            if (instance != null || prefab == null) return;
            Transform anchor = effectAnchor != null ? effectAnchor : transform;
            instance = Instantiate(prefab, anchor.position, anchor.rotation, anchor);
        }

        private void ResetState()
        {
            CurrentHealth = settings.maxHealth;
            bool wasWrecked = Stage == VehicleDamageStage.Destroyed;
            Stage = VehicleDamageStage.Healthy;
            lastCrashTime = -100f;
            if (smokeInstance != null) Destroy(smokeInstance);
            if (fireInstance != null) Destroy(fireInstance);
            smokeInstance = null;
            fireInstance = null;
            for (int i = 0; wasWrecked && i < disableWhenWrecked.Length; i++)
            {
                if (disableWhenWrecked[i] != null) disableWhenWrecked[i].SetActive(true);
            }
            if (destroyedInstance != null)
            {
                Destroy(destroyedInstance);
                destroyedInstance = null;
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] != null && !(renderers[i] is ParticleSystemRenderer)) renderers[i].enabled = rendererWasEnabled[i];
                }
            }
            SetDarkened(false);
        }
    }
}
