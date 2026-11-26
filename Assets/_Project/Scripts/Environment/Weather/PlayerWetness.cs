using System.Collections.Generic;
using UnityEngine;

namespace OpenworldCrime.Environment
{
    /// <summary>
    /// The played character gets wet in the rain and dries afterwards (Jason / Mia; follows whichever is active). Wetness rises
    /// with <see cref="WeatherManager.Intensity"/> while on foot (not while sheltered in a vehicle) and falls slowly once out of
    /// the rain. Look: each material slot of the character's skinned meshes gets a property block (per material index, so every
    /// slot keeps its own colour) with a darker base colour and a higher smoothness - wet cloth and skin - and a few drops fall
    /// off the body (one small pooled particle system, re-parented on character switch). Materials on disk are never changed;
    /// at zero wetness the blocks are removed (back to SRP batching).
    /// Swimming soaks the character at once (PlayerSwimController); he drips and dries from there once out of the water.
    /// Updates only when the wetness changed by a step; nothing per frame while dry.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerWetness : MonoBehaviour
    {
        [SerializeField] private WeatherManager weather;
        [SerializeField] private ParticleSystem drips;
        [SerializeField, Min(1f)] private float wetSeconds = 25f;
        [SerializeField, Min(1f)] private float drySeconds = 120f;
        [SerializeField, Range(0f, 1f)] private float darken = 0.28f;
        [SerializeField, Range(0f, 1f)] private float wetSmoothness = 0.78f;
        [SerializeField, Min(0f)] private float maxDripRate = 9f;
        [SerializeField, Range(0.005f, 0.1f)] private float applyStep = 0.02f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

        private struct Slot { public Renderer renderer; public int index; public Color baseColor; public float smoothness; }

        private readonly List<Slot> slots = new List<Slot>();
        private readonly List<Material> buffer = new List<Material>(8);
        private MaterialPropertyBlock block;
        private PlayableCharacter player;
        private PlayerVehicleInteractionState vehicleState;
        private float wetness, applied = -1f;
        private float nextLookup;
        private bool swimming;

        /// <summary>How wet the played character is, 0-1.</summary>
        public float Wetness => wetness;

        private void Awake()
        {
            if (weather == null) { Debug.LogError($"[{nameof(PlayerWetness)}] No WeatherManager on '{name}'.", this); enabled = false; return; }
            block = new MaterialPropertyBlock();
        }

        private void OnEnable() => OpenworldCrime.Player.PlayerSwimController.OnPlayerSwimmingChanged += HandleSwimming;

        private void OnDisable()
        {
            OpenworldCrime.Player.PlayerSwimController.OnPlayerSwimmingChanged -= HandleSwimming;
            swimming = false;
            Clear();
            applied = -1f;
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime >= nextLookup && (player == null || !player.gameObject.activeInHierarchy))
            {
                nextLookup = Time.unscaledTime + 0.5f;
                FindPlayer();
            }
            if (player == null) return;

            bool sheltered = vehicleState.InVehicle;
            float rain = sheltered ? 0f : weather.Intensity;
            wetness = rain > wetness
                ? Mathf.MoveTowards(wetness, rain, Time.deltaTime / wetSeconds)
                : Mathf.MoveTowards(wetness, rain, Time.deltaTime / drySeconds);
            if (swimming) wetness = 1f;

            if (drips != null)
            {
                var em = drips.emission;
                float rate = sheltered || swimming ? 0f : wetness * maxDripRate;
                em.rateOverTime = rate;
                if (rate > 0.01f && !drips.isPlaying) drips.Play();
                else if (rate <= 0.01f && drips.isPlaying) drips.Stop();
            }

            if (Mathf.Abs(wetness - applied) < applyStep && !(wetness == 0f && applied > 0f)) return;
            applied = wetness;
            Apply(wetness);
        }

        private void Apply(float w)
        {
            if (w <= 0.001f) { Clear(); return; }
            float k = w * w * (3f - 2f * w);
            foreach (Slot s in slots)
            {
                if (s.renderer == null) continue;
                s.renderer.GetPropertyBlock(block, s.index);
                block.SetColor(BaseColorId, s.baseColor * (1f - darken * k));
                block.SetFloat(SmoothnessId, Mathf.Lerp(s.smoothness, Mathf.Max(s.smoothness, wetSmoothness), k));
                s.renderer.SetPropertyBlock(block, s.index);
            }
        }

        private void Clear()
        {
            foreach (Slot s in slots) if (s.renderer != null) s.renderer.SetPropertyBlock(null, s.index);
        }

        private void FindPlayer()
        {
            Clear();
            slots.Clear();
            player = null;
            foreach (PlayableCharacter c in PlayableCharacter.All)
                if (c != null && c.gameObject.activeInHierarchy) { player = c; break; }
            if (player == null) return;
            vehicleState = new PlayerVehicleInteractionState(player.GetComponent<OpenworldCrime.Player.PlayerVehicleInteraction>());
            foreach (SkinnedMeshRenderer r in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                r.GetSharedMaterials(buffer);
                for (int i = 0; i < buffer.Count; i++)
                {
                    Material m = buffer[i];
                    if (m == null || !m.HasProperty(BaseColorId) || !m.HasProperty(SmoothnessId)) continue;
                    slots.Add(new Slot { renderer = r, index = i, baseColor = m.GetColor(BaseColorId), smoothness = m.GetFloat(SmoothnessId) });
                }
            }
            if (drips != null)
            {
                var animator = player.GetComponent<Animator>();
                Transform anchor = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Chest) : player.transform;
                drips.transform.SetParent(anchor != null ? anchor : player.transform, false);
                drips.transform.localPosition = Vector3.zero;
            }
            applied = -1f;
        }

        private void HandleSwimming(bool isSwimming) => swimming = isSwimming;

        // Small wrapper so the vehicle check is a field read (no lookup per frame).
        private readonly struct PlayerVehicleInteractionState
        {
            private readonly OpenworldCrime.Player.PlayerVehicleInteraction interaction;
            public PlayerVehicleInteractionState(OpenworldCrime.Player.PlayerVehicleInteraction i) { interaction = i; }
            public bool InVehicle => interaction != null && interaction.State != OpenworldCrime.Player.VehicleInteractionState.OnFoot;
        }

#if UNITY_EDITOR
        public void EditorSetup(WeatherManager manager, ParticleSystem dripSystem)
        {
            weather = manager;
            drips = dripSystem;
        }
#endif
    }
}
