using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>What a light material slot lights up for.</summary>
    public enum VehicleLightRole
    {
        /// <summary>Front lamps only: on with the headlights.</summary>
        Headlight,
        /// <summary>Dedicated brake lamps: on while braking.</summary>
        Brake,
        /// <summary>Rear lamps that are tail lights and brake lights: dim with the headlights, bright while braking.</summary>
        TailAndBrake,
        /// <summary>Reversing lamps: on while reversing.</summary>
        Reverse,
        /// <summary>
        /// One material holding front and rear lamps (most custom car models): on with the headlights; optionally dim while braking
        /// (only where no separate brake geometry exists, e.g. drivable cars).
        /// </summary>
        Combined
    }

    /// <summary>
    /// Drives a vehicle's existing light materials (no extra emission geometry): each authored slot (renderer + material index +
    /// role) is switched between shared runtime variants of its own material (emission off / dim / full), so it stays SRP Batcher
    /// friendly and no per-car material copies are made. Only light slots are touched: the paint slot that TrafficCarPaint swaps
    /// is read back each time and never overwritten. Renderer enabling (TrafficViewRadius) is left alone.
    /// State sources (all optional, set by Tools > OpenworldCrime > Vehicles > Build Car Lamps (emission)):
    /// - iTS traffic: brake from <see cref="TSTrafficAI.OnUpdateAI"/> (event, no polling), or <see cref="ReportBrake"/> while a
    ///   script drives the car (panic flight, held for a carjack);
    /// - RCC drivable: brake / reverse / headlight switch (RCC's low beam key) read from RCC_CarControllerV3 (cheap compare per
    ///   frame while the engine runs, a few times a second when parked; only changes swap). Lamps only light with the engine
    ///   running, so parked empty cars stay dark; starting the engine at night switches the headlights on;
    /// - night: the global <see cref="SetNightLevel"/> (VehicleNightLightsSync follows the day/night blend). Each car has its own
    ///   switch-on point in the dusk (Night Switch Range), so the street lights up car by car rather than all at once, and its
    ///   lamps ramp up / down over Lamp Ramp Seconds in a few steps (still shared material variants, one per step);
    /// - wreck: <see cref="VehicleHealth.OnDestroyedWreck"/> puts every lamp out until the car is reused.
    /// Real-time Light components: everything in Other Realtime Lights is switched off for good; Realtime Headlights are only on for
    /// the vehicle the player drives, while the headlights are on and within Realtime Light Distance of the camera.
    /// </summary>
    [DisallowMultipleComponent]
    public class VehicleLights : MonoBehaviour
    {
        [Serializable]
        public struct LightSlot
        {
            public Renderer renderer;
            [Min(0)] public int materialIndex;
            public VehicleLightRole role;
        }

        private enum Level { Off = 0, Dim = 1, Full = 2 }

        [Header("Light Material Slots")]
        [SerializeField] private LightSlot[] slots = new LightSlot[0];
        [Tooltip("Combined front/rear light materials glow dimly while braking. Off for traffic cars (their brake glow is shown by iTS).")]
        [SerializeField] private bool combinedShowsBrake;

        [Header("Look")]
        [Tooltip("Emission strength (HDR multiplier) of a fully lit lamp.")]
        [SerializeField, Min(0f)] private float fullIntensity = 4f;
        [Tooltip("Share of Full Intensity used for tail lights / dim lamps.")]
        [SerializeField, Range(0f, 1f)] private float dimShare = 0.3f;

        [Header("Night")]
        [Tooltip("Range of the dusk night level (0 day - 1 night) in which this car switches its lights on; a random point per car.")]
        [SerializeField] private Vector2 nightSwitchRange = new Vector2(0.22f, 0.55f);
        [Tooltip("Seconds the lamps take to come up / go out (in Lamp Ramp Steps).")]
        [SerializeField, Min(0f)] private float lampRampSeconds = 0.6f;
        [SerializeField, Range(1, 8)] private int lampRampSteps = 4;

        [Header("State Sources (optional)")]
        [SerializeField] private TSTrafficAI trafficAI;
        [SerializeField] private RCC_CarControllerV3 rccController;
        [Tooltip("Used to know whether the player drives this vehicle (real-time headlights).")]
        [SerializeField] private RCCVehicleAdapter drivableAdapter;
        [Tooltip("Every lamp goes out when the car is wrecked.")]
        [SerializeField] private VehicleHealth health;
        [Tooltip("iTS brake input above which the brake lights are on.")]
        [SerializeField, Range(0f, 1f)] private float brakeThreshold = 0.05f;

        [Header("Real-time Lights")]
        [Tooltip("Spot lights of the headlights. Only on for the player's vehicle, with the headlights on, near the camera.")]
        [SerializeField] private Light[] realtimeHeadlights = new Light[0];
        [Tooltip("Every other Light component on the vehicle (brake, reverse, indicator, exhaust flame...): switched off for good.")]
        [SerializeField] private Light[] otherRealtimeLights = new Light[0];
        [Tooltip("Real-time headlights are switched off beyond this distance (m) from the camera. Exposed for the graphics profiles.")]
        [SerializeField, Min(0f)] private float realtimeLightDistance = 60f;

        /// <summary>Global night switch (true from the middle of the dusk); cars decide individually from <see cref="NightLevel"/>.</summary>
        public static bool NightMode { get; private set; }
        /// <summary>Raised when <see cref="NightMode"/> changes.</summary>
        public static event Action<bool> OnNightModeChanged;
        /// <summary>Night level, 0 day - 1 night (the day/night blend).</summary>
        public static float NightLevel { get; private set; }
        private static event Action<float> OnNightLevelChanged;

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        // Runtime-only shared variants: one per (source material, role tint, level). Bounded by light materials x 3.
        private static readonly Dictionary<(Material, VehicleLightRole, Level, float), Material> variants = new Dictionary<(Material, VehicleLightRole, Level, float), Material>();

        private readonly List<Material> materialBuffer = new List<Material>(8);
        private Material[] sourceMaterials = new Material[0];
        private Level[] appliedLevels = new Level[0];
        private bool headlightsOn;
        private bool braking;
        private bool reversing;
        private bool realtimeOn;
        private bool engineRunning;
        private bool wrecked;
        private Transform cameraTransform;
        private const float ParkedPollInterval = 0.3f;
        private float nightSwitch;
        private bool carNight;
        private float lampScale = 1f;
        private Coroutine rampRoutine;

        /// <summary>Sets the global night state (raises <see cref="OnNightModeChanged"/>). Called by the time-of-day system.</summary>
        public static void SetNightMode(bool night) => SetNightLevel(night ? 1f : 0f);

        /// <summary>Sets the night level (0 day - 1 night): every car switches at its own point of the dusk.</summary>
        public static void SetNightLevel(float level)
        {
            level = Mathf.Clamp01(level);
            if (Mathf.Abs(level - NightLevel) < 0.002f && level > 0f && level < 1f) return;
            NightLevel = level;
            OnNightLevelChanged?.Invoke(level);
            bool night = NightMode ? level > 0.3f : level >= 0.4f;
            if (night == NightMode) return;
            NightMode = night;
            OnNightModeChanged?.Invoke(night);
        }

        public bool HeadlightsOn => headlightsOn && !wrecked;
        public bool IsBraking => braking && !wrecked;

        /// <summary>Overrides the real-time headlight distance of this vehicle.</summary>
        public void SetRealtimeLightDistance(float distance) => realtimeLightDistance = Mathf.Max(0f, distance);

        /// <summary>
        /// Real-time headlight distance for every vehicle, including ones spawned later (graphics profiles). Negative = each
        /// vehicle's own Realtime Light Distance; 0 = real-time headlights off for every vehicle.
        /// </summary>
        public static float GlobalRealtimeLightDistance { get; private set; } = -1f;

        /// <summary>Sets <see cref="GlobalRealtimeLightDistance"/>.</summary>
        public static void SetGlobalRealtimeLightDistance(float distance) => GlobalRealtimeLightDistance = distance;

        private float RealtimeLightDistance => GlobalRealtimeLightDistance >= 0f ? GlobalRealtimeLightDistance : realtimeLightDistance;

        private void Awake()
        {
            sourceMaterials = new Material[slots.Length];
            appliedLevels = new Level[slots.Length];
            nightSwitch = UnityEngine.Random.Range(nightSwitchRange.x, nightSwitchRange.y);
            for (int i = 0; i < slots.Length; i++)
            {
                LightSlot slot = slots[i];
                appliedLevels[i] = (Level)(-1);
                if (slot.renderer == null) continue;
                slot.renderer.GetSharedMaterials(materialBuffer);
                if (slot.materialIndex < materialBuffer.Count) sourceMaterials[i] = materialBuffer[slot.materialIndex];
                else Debug.LogWarning($"[{nameof(VehicleLights)}] '{name}': slot {i} material index {slot.materialIndex} is out of range on '{slot.renderer.name}'.", this);
            }
            for (int i = 0; i < otherRealtimeLights.Length; i++)
            {
                Light l = otherRealtimeLights[i];
                if (l == null) continue;
                l.enabled = false;
                // RCC_Light switches its Light on in Start; with the component off it never does (RCC source untouched).
                var rccLight = l.GetComponent<RCC_Light>();
                if (rccLight != null) rccLight.enabled = false;
            }
            SetRealtimeHeadlights(false);
        }

        private void OnEnable()
        {
            OnNightLevelChanged += HandleNightLevelChanged;
            if (trafficAI != null) trafficAI.OnUpdateAI += HandleTrafficUpdate;
            if (health != null) health.OnDestroyedWreck += HandleWrecked;
            braking = false;
            reversing = false;
            wrecked = health != null && health.IsDestroyed;
            carNight = NightLevel >= nightSwitch;
            lampScale = 1f;
            rampRoutine = null;
            if (rccController != null && carNight) rccController.lowBeamHeadLightsOn = true;
            engineRunning = rccController != null && rccController.engineRunning;
            headlightsOn = rccController != null ? rccController.lowBeamHeadLightsOn && engineRunning : carNight;
            Apply(true);
            if (rccController != null) StartCoroutine(PollRccRoutine());
        }

        private void OnDisable()
        {
            OnNightLevelChanged -= HandleNightLevelChanged;
            if (trafficAI != null) trafficAI.OnUpdateAI -= HandleTrafficUpdate;
            if (health != null) health.OnDestroyedWreck -= HandleWrecked;
            SetRealtimeHeadlights(false);
        }

        // Only RCC vehicles poll (a few bool compares per frame while the engine runs, a few times a second when parked; swaps only
        // on change); traffic cars are purely event driven.
        private System.Collections.IEnumerator PollRccRoutine()
        {
            var parkedWait = new WaitForSeconds(ParkedPollInterval);
            while (true)
            {
                bool engine = rccController.engineRunning;
                // Engine started at night (the player got in): headlights on, as the night switch does for running cars.
                if (engine && !engineRunning && carNight) rccController.lowBeamHeadLightsOn = true;
                engineRunning = engine;
                bool brake = engine && rccController.brakeInput >= 0.1f;
                bool reverse = engine && rccController.direction == -1 && Mathf.Abs(rccController.speed) > 0.5f;
                bool lit = engine && rccController.lowBeamHeadLightsOn;
                bool changed = brake != braking || reverse != reversing || lit != headlightsOn;
                braking = brake;
                reversing = reverse;
                headlightsOn = lit;
                if (changed) Apply(false);
                UpdateRealtimeHeadlights();
                if (engine) yield return null;
                else yield return parkedWait;
            }
        }

        /// <summary>
        /// Brake input from a script that drives a traffic car without the iTS AI (panic flight, a car held for a carjack or a
        /// bail-out). Same threshold as the iTS brake.
        /// </summary>
        public void ReportBrake(float brake)
        {
            bool wasBraking = braking;
            braking = brake > brakeThreshold;
            if (braking != wasBraking) Apply(false);
        }

        private void HandleWrecked()
        {
            wrecked = true;
            Apply(false);
            SetRealtimeHeadlights(false);
        }

        private void HandleTrafficUpdate(float steering, float brake, float throttle, bool upsideDown)
        {
            bool wasBraking = braking;
            braking = brake > brakeThreshold;
            if (braking != wasBraking) Apply(false);
        }

        // This car's own switch point (with a little hysteresis), then a short ramp of its lamps.
        private void HandleNightLevelChanged(float level)
        {
            bool night = carNight ? level > nightSwitch - 0.08f : level >= nightSwitch;
            if (night == carNight) return;
            carNight = night;
            if (rccController != null)
            {
                rccController.lowBeamHeadLightsOn = night;
                headlightsOn = night && rccController.engineRunning;
            }
            else headlightsOn = night;
            if (night && lampRampSeconds > 0f && isActiveAndEnabled)
            {
                if (rampRoutine != null) StopCoroutine(rampRoutine);
                rampRoutine = StartCoroutine(RampLamps());
            }
            else Apply(false);
        }

        // Lamps come up in a few steps (one shared variant per step): a quick warm-up instead of a pop.
        private System.Collections.IEnumerator RampLamps()
        {
            float stepTime = lampRampSeconds / lampRampSteps;
            var wait = new WaitForSeconds(stepTime);
            for (int step = 1; step <= lampRampSteps; step++)
            {
                lampScale = (float)step / lampRampSteps;
                Apply(true);
                if (step < lampRampSteps) yield return wait;
            }
            lampScale = 1f;
            rampRoutine = null;
        }

        private void Apply(bool force)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                Material source = sourceMaterials[i];
                Renderer r = slots[i].renderer;
                if (source == null || r == null) continue;
                Level level = GetLevel(slots[i].role);
                if (!force && level == appliedLevels[i]) continue;
                appliedLevels[i] = level;

                // Read back the renderer's current array so another system's slot change (e.g. paint) is kept.
                r.GetSharedMaterials(materialBuffer);
                int index = slots[i].materialIndex;
                if (index >= materialBuffer.Count) continue;
                materialBuffer[index] = GetVariant(source, slots[i].role, level);
                r.SetSharedMaterials(materialBuffer);
            }
        }

        private Level GetLevel(VehicleLightRole role)
        {
            if (wrecked) return Level.Off;
            switch (role)
            {
                case VehicleLightRole.Headlight: return headlightsOn ? Level.Full : Level.Off;
                case VehicleLightRole.Brake: return braking ? Level.Full : Level.Off;
                case VehicleLightRole.TailAndBrake: return braking ? Level.Full : headlightsOn ? Level.Dim : Level.Off;
                case VehicleLightRole.Reverse: return reversing ? Level.Full : Level.Off;
                default: return headlightsOn ? Level.Full : combinedShowsBrake && braking ? Level.Dim : Level.Off;
            }
        }

        private Material GetVariant(Material source, VehicleLightRole role, Level level)
        {
            float ramp = level == Level.Off ? 1f : Mathf.Clamp01(lampScale);
            var key = (source, role, level, fullIntensity * (level == Level.Dim ? dimShare : 1f) * ramp);
            if (variants.TryGetValue(key, out Material variant) && variant != null) return variant;

            variant = new Material(source)
            {
                name = $"{source.name} ({role} {level})",
                hideFlags = HideFlags.DontSave
            };
            if (level == Level.Off)
            {
                variant.SetColor(EmissionColorId, Color.black);
                variant.DisableKeyword("_EMISSION");
            }
            else
            {
                // The lamp glows in its own texture colours (white front, red rear) unless the artist already set an emission map.
                if (variant.HasProperty(EmissionMapId) && variant.GetTexture(EmissionMapId) == null && variant.HasProperty(BaseMapId))
                    variant.SetTexture(EmissionMapId, variant.GetTexture(BaseMapId));
                variant.SetColor(EmissionColorId, GetTint(role) * key.Item4);
                variant.EnableKeyword("_EMISSION");
                variant.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            variants[key] = variant;
            return variant;
        }

        private static Color GetTint(VehicleLightRole role)
        {
            switch (role)
            {
                case VehicleLightRole.Brake:
                case VehicleLightRole.TailAndBrake: return new Color(1f, 0.15f, 0.1f);
                case VehicleLightRole.Headlight: return new Color(1f, 0.95f, 0.85f);
                default: return Color.white;
            }
        }

        private void UpdateRealtimeHeadlights()
        {
            if (realtimeHeadlights.Length == 0) return;
            float maxDistance = RealtimeLightDistance;
            // A global 0 switches real-time headlights off; a vehicle's own 0 means no distance limit (original behaviour).
            bool wanted = headlightsOn && !wrecked && drivableAdapter != null && drivableAdapter.IsControlled && GlobalRealtimeLightDistance != 0f;
            if (wanted && maxDistance > 0f)
            {
                if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
                Camera active = drivableAdapter.GetActiveCamera();
                Transform cam = active != null ? active.transform : cameraTransform;
                wanted = cam == null || (cam.position - transform.position).sqrMagnitude <= maxDistance * maxDistance;
            }
            // Applied every poll (a bool compare per light): RCC_Light switches its Light on in its own Start.
            if (wanted != realtimeOn || (realtimeHeadlights[0] != null && realtimeHeadlights[0].enabled != wanted)) SetRealtimeHeadlights(wanted);
        }

        private void SetRealtimeHeadlights(bool on)
        {
            realtimeOn = on;
            for (int i = 0; i < realtimeHeadlights.Length; i++)
            {
                if (realtimeHeadlights[i] != null) realtimeHeadlights[i].enabled = on;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            foreach (Material m in variants.Values) if (m != null) Destroy(m);
            variants.Clear();
            NightMode = false;
            NightLevel = 0f;
            OnNightModeChanged = null;
            OnNightLevelChanged = null;
            GlobalRealtimeLightDistance = -1f;
        }
    }
}
