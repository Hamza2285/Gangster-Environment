using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenworldCrime.Environment
{
    /// <summary>
    /// City sky through the day: a clock runs morning, noon, evening and night and blends the sky material
    /// ("OpenworldCrime/Sky Cycle") between the four looks smoothly. The sky's look (tints, glows, night colours) lives on
    /// the material; this component only feeds it the blend weights and the sun / moon directions, through a runtime copy
    /// (the material asset is never changed in play mode).
    ///
    /// Lighting stays with <see cref="CityDayNightController"/> (Hamza's, untouched), driven continuously from the clock: a night
    /// factor (0 day - 1 night) ramps smoothly through dusk and dawn and is handed to its SetManualBlend, which blends sun,
    /// ambient, fog, sky, window / road glow and volumes between its exact day and night presets (endpoints unchanged). Every
    /// other night system (baked lighting, reflections, car and lamp lights) follows that blend. This replaces firing the
    /// controller's own 40 s transitions at fixed hours, which ran against the clock (a short day reversed them half way).
    /// Its Day / Night buttons still work: they start its transition, which this takes over by moving the clock forward
    /// through dusk / dawn to the button's hour over Button Travel Seconds, eased, so the sky and the lights stay in step.
    /// The controller's blends put its own skybox back, so the sky is re-asserted after it each frame (LateUpdate,
    /// reference compare only). Nothing happens while another scene (an interior) is the active scene.
    /// Lives under ///Manager/// (not a Singleton). Clock work is throttled.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(500)] // after CityDayNightController.Update (it writes the skybox during transitions)
    public class SkyCycleController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CityDayNightController dayNight;
        [Tooltip("Sky material using the 'OpenworldCrime/Sky Cycle' shader (all look settings are on it).")]
        [SerializeField] private Material skyMaterial;

        [Header("Clock")]
        [SerializeField, Range(0f, 24f)] private float startHour = 8f;
        [Tooltip("Real minutes for one full in-game day.")]
        [SerializeField, Min(1f)] private float dayLengthMinutes = 16f;
        [SerializeField] private bool autoAdvance = true;
        [Tooltip("Seconds between sky updates (the sky changes slowly).")]
        [SerializeField, Min(0.02f)] private float updateInterval = 0.1f;

        [Header("Phases (hours, each blends smoothly into the next)")]
        [SerializeField] private float dawnStart = 5f;
        [SerializeField] private float morningFull = 6.5f;
        [SerializeField] private float morningEnd = 9f;
        [SerializeField] private float noonFull = 11f;
        [SerializeField] private float noonEnd = 15.5f;
        [SerializeField] private float eveningFull = 17.5f;
        [SerializeField] private float eveningEnd = 19f;
        [SerializeField] private float nightFull = 20.5f;

        [Header("Lighting (CityDayNightController night blend)")]
        [Tooltip("Dusk: the lights blend from day to night between these hours.")]
        [SerializeField] private float duskStartHour = 18.6f;
        [SerializeField] private float duskEndHour = 20.4f;
        [Tooltip("Dawn: the lights blend from night to day between these hours.")]
        [SerializeField] private float dawnStartHour = 4.8f;
        [SerializeField] private float dawnEndHour = 6.6f;
        [Tooltip("Night factor across the dusk (0-1 of the dusk hours -> 0 day - 1 night; the dawn is its mirror). Shallow in the middle, where the controller's sun sets / rises, so the sunset is not over in a moment.")]
        [SerializeField] private AnimationCurve rampCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0f), new Keyframe(0.25f, 0.3f), new Keyframe(0.75f, 0.6f), new Keyframe(1f, 1f, 0f, 0f));
        [Tooltip("Where the Day / Night buttons move the clock, and how long the move takes (s).")]
        [SerializeField] private float buttonDayHour = 9f;
        [SerializeField] private float buttonNightHour = 21.5f;
        [SerializeField, Min(1f)] private float buttonTravelSeconds = 16f;

        [Header("Sun path")]
        [SerializeField] private float sunriseHour = 6f;
        [SerializeField] private float sunsetHour = 19.3f;
        [SerializeField, Range(10f, 85f)] private float noonElevation = 55f;
        [Tooltip("Compass direction (deg) of the sun at noon; it rises 90 deg to one side and sets 90 deg to the other.")]
        [SerializeField] private float noonYaw = 148f;

        /// <summary>Hour of day, 0-24.</summary>
        public float Hour { get; private set; }
        /// <summary>Blend weights: morning, noon, evening, night (sum 1).</summary>
        public Vector4 Weights { get; private set; }
        /// <summary>Night factor handed to the lighting (0 day - 1 night), from the clock.</summary>
        public float NightFactor { get; private set; }
        /// <summary>Raised when the hour is moved by a jump (SetHour).</summary>
        public event Action<float> OnHourJumped;

        private static readonly int WeightsId = Shader.PropertyToID("_CycleWeights");
        private static readonly int SunId = Shader.PropertyToID("_SunDirection");
        private static readonly int MoonId = Shader.PropertyToID("_MoonDirection");

        private Material skyInstance;
        private float nextUpdate;
        private float appliedFactor = -1f;
        private bool valid;
        // Button travel: the clock moves forward from travelFrom by travelSpan hours over buttonTravelSeconds.
        private bool travelling;
        private float travelFrom, travelSpan, travelStart;

        private void Awake()
        {
            valid = dayNight != null && skyMaterial != null;
            if (!valid)
            {
                Debug.LogError($"[{nameof(SkyCycleController)}] Missing CityDayNightController or sky material on '{name}'.", this);
                enabled = false;
                return;
            }
            skyInstance = new Material(skyMaterial) { name = skyMaterial.name + " (Runtime)" };
            Hour = Mathf.Repeat(startHour, 24f);
        }

        private void Start()
        {
            if (!valid) return;
            appliedFactor = -1f;
            Refresh(true);
        }

        private void OnDestroy()
        {
            if (skyInstance != null) Destroy(skyInstance);
        }

        /// <summary>Moves the clock (lights follow with the controller's transition).</summary>
        public void SetHour(float hour)
        {
            travelling = false;
            Hour = Mathf.Repeat(hour, 24f);
            OnHourJumped?.Invoke(Hour);
            Refresh(true);
        }

        /// <summary>Moves the clock forward to an hour over a few seconds (dusk / dawn play out on the way).</summary>
        public void TravelToHour(float hour, float seconds)
        {
            travelFrom = Hour;
            travelSpan = Mathf.Repeat(hour - Hour, 24f);
            travelStart = Time.unscaledTime;
            travelSeconds = Mathf.Max(1f, seconds);
            travelling = travelSpan > 0.001f;
            // Most of the time goes to the dusk / dawn on the way (where the light changes); the hours before and after pass quickly.
            float rampStart = Mathf.Repeat(hour - Hour, 24f) > 0f && IsNightHour(hour) ? duskStartHour : dawnStartHour;
            float rampLength = IsNightHour(hour) ? duskEndHour - duskStartHour : dawnEndHour - dawnStartHour;
            rampFrom = Mathf.Repeat(rampStart - travelFrom, 24f);
            rampTo = Mathf.Min(rampFrom + rampLength, travelSpan);
            if (rampFrom >= travelSpan) rampFrom = rampTo = -1f; // no dusk / dawn on the way
        }

        private float travelSeconds = 14f;
        private float rampFrom = -1f, rampTo = -1f; // hours into the travel where the dusk / dawn lies

        private bool IsNightHour(float h) => h >= duskEndHour || h < dawnStartHour;

        // Travel progress (0-1 of the time) to hours travelled: 15 % of the time up to the ramp, 75 % through it (eased), 10 % after.
        private float TravelHours(float k)
        {
            if (rampFrom < 0f) return travelSpan * Smoother(k);
            const float pre = 0.15f, ramp = 0.75f;
            float preShare = rampFrom > 0.01f ? pre : 0f;
            float rampShare = ramp + (pre - preShare);
            if (k < preShare) return rampFrom * Smoother(k / preShare);
            if (k < preShare + rampShare) return Mathf.Lerp(rampFrom, rampTo, (k - preShare) / rampShare);
            float post = travelSpan - rampTo;
            return rampTo + post * Smoother((k - preShare - rampShare) / Mathf.Max(0.0001f, 1f - preShare - rampShare));
        }

        private void Update()
        {
            // The Day / Night buttons start the controller's own transition: take it over and move the clock instead.
            if (dayNight.IsTransitioning && !travelling) TravelToHour(dayNight.IsNight ? buttonNightHour : buttonDayHour, buttonTravelSeconds);

            if (travelling)
            {
                float k = Mathf.Clamp01((Time.unscaledTime - travelStart) / travelSeconds);
                Hour = Mathf.Repeat(travelFrom + TravelHours(k), 24f);
                if (k >= 1f) travelling = false;
            }
            else if (autoAdvance) Hour = Mathf.Repeat(Hour + Time.deltaTime * 24f / (dayLengthMinutes * 60f), 24f);

            // Lighting every frame while it changes (dusk / dawn / travel; only a compare otherwise); the sky at its own rate.
            UpdateLighting(false);
            if (Time.unscaledTime < nextUpdate) return;
            nextUpdate = Time.unscaledTime + updateInterval;
            Refresh(false);
        }

        // Every frame, but only a reference compare unless the controller replaced the sky (during its transitions).
        private void LateUpdate()
        {
            if (SceneManager.GetActiveScene() == gameObject.scene && RenderSettings.skybox != skyInstance) RenderSettings.skybox = skyInstance;
        }

        private void Refresh(bool force)
        {
            UpdateLighting(force);
            Weights = PhaseWeights(Hour);
            skyInstance.SetVector(WeightsId, Weights);
            skyInstance.SetVector(SunId, SunDirection(Hour));
            skyInstance.SetVector(MoonId, MoonDirection());
        }

        // The controller's blend follows the clock; only sent when it changed (the endpoints apply the presets exactly).
        private void UpdateLighting(bool force)
        {
            if (!force && SceneManager.GetActiveScene() != gameObject.scene) return;
            float f = NightFactorAt(Hour);
            NightFactor = f;
            if (!force && Mathf.Abs(f - appliedFactor) < 0.0004f && !dayNight.IsTransitioning) return;
            appliedFactor = f;
            dayNight.SetManualBlend(f);
        }

        private float NightFactorAt(float h)
        {
            if (h >= duskStartHour && h < duskEndHour) return Ramp(Mathf.InverseLerp(duskStartHour, duskEndHour, h));
            if (h >= dawnStartHour && h < dawnEndHour) return 1f - Ramp(Mathf.InverseLerp(dawnStartHour, dawnEndHour, h));
            return h >= duskEndHour || h < dawnStartHour ? 1f : 0f;
        }

        // Night -> morning -> noon -> evening -> night, smootherstep between neighbouring keys.
        private Vector4 PhaseWeights(float h)
        {
            var night = new Vector4(0f, 0f, 0f, 1f);
            var morning = new Vector4(1f, 0f, 0f, 0f);
            var noon = new Vector4(0f, 1f, 0f, 0f);
            var evening = new Vector4(0f, 0f, 1f, 0f);
            if (h < dawnStart) return night;
            if (h < morningFull) return Vector4.Lerp(night, morning, Smoother(Mathf.InverseLerp(dawnStart, morningFull, h)));
            if (h < morningEnd) return morning;
            if (h < noonFull) return Vector4.Lerp(morning, noon, Smoother(Mathf.InverseLerp(morningEnd, noonFull, h)));
            if (h < noonEnd) return noon;
            if (h < eveningFull) return Vector4.Lerp(noon, evening, Smoother(Mathf.InverseLerp(noonEnd, eveningFull, h)));
            if (h < eveningEnd) return evening;
            if (h < nightFull) return Vector4.Lerp(evening, night, Smoother(Mathf.InverseLerp(eveningEnd, nightFull, h)));
            return night;
        }

        // East-to-west arc: 0 at sunrise, 1 at sunset; below the horizon outside the day.
        private Vector4 SunDirection(float h)
        {
            float t = (h - sunriseHour) / (sunsetHour - sunriseHour);
            float elevation = Mathf.Sin(Mathf.Clamp(t, -0.15f, 1.15f) * Mathf.PI) * noonElevation;
            float yaw = noonYaw + Mathf.Lerp(-90f, 90f, Mathf.Clamp(t, -0.15f, 1.15f));
            Vector3 dir = Quaternion.Euler(-elevation, yaw, 0f) * Vector3.forward;
            return new Vector4(dir.x, dir.y, dir.z, 0f);
        }

        private Vector4 MoonDirection()
        {
            Vector3 moonLight = dayNight.presets != null ? dayNight.presets.night.lightRotation : new Vector3(38f, 328f, 0f);
            Vector3 dir = -(Quaternion.Euler(moonLight) * Vector3.forward);
            return new Vector4(dir.x, dir.y, dir.z, 0f);
        }

        private float Ramp(float x) => Mathf.Clamp01(rampCurve != null && rampCurve.length > 1 ? rampCurve.Evaluate(Mathf.Clamp01(x)) : Smoother(x));

        private static float Smoother(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * x * (x * (x * 6f - 15f) + 10f);
        }
    }
}
