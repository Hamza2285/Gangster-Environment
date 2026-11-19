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
    /// Lighting stays with <see cref="CityDayNightController"/> (Hamza's, untouched): at dusk and dawn this calls its
    /// ApplyNight / ApplyDay, which run its smooth light transition (and with it the baked day / night swap of
    /// <see cref="BakedLightingSwitcher"/>). Its Day / Night buttons still work: they move the clock to day or night.
    /// The controller's transitions put its own skybox back, so the sky is re-asserted after it each frame (LateUpdate,
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

        [Header("Lighting switch (CityDayNightController)")]
        [Tooltip("Hour the light transition to night starts (it takes the controller's transition time).")]
        [SerializeField] private float lightsToNightHour = 19.2f;
        [Tooltip("Hour the light transition to day starts.")]
        [SerializeField] private float lightsToDayHour = 5.2f;
        [Tooltip("Where the Day / Night buttons move the clock.")]
        [SerializeField] private float buttonDayHour = 9f;
        [SerializeField] private float buttonNightHour = 21.5f;

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
        /// <summary>Raised when the hour is moved by a jump (buttons / SetHour).</summary>
        public event Action<float> OnHourJumped;

        private static readonly int WeightsId = Shader.PropertyToID("_CycleWeights");
        private static readonly int SunId = Shader.PropertyToID("_SunDirection");
        private static readonly int MoonId = Shader.PropertyToID("_MoonDirection");

        private Material skyInstance;
        private float nextUpdate;
        private bool lightsNight;
        private bool valid;

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
            lightsNight = WantsNightLights(Hour);
            dayNight.Apply(lightsNight); // start at the clock's state without a transition
            Refresh();
        }

        private void OnDestroy()
        {
            if (skyInstance != null) Destroy(skyInstance);
        }

        /// <summary>Moves the clock (lights follow with the controller's transition).</summary>
        public void SetHour(float hour)
        {
            Hour = Mathf.Repeat(hour, 24f);
            OnHourJumped?.Invoke(Hour);
            Refresh();
        }

        private void Update()
        {
            if (autoAdvance) Hour = Mathf.Repeat(Hour + Time.deltaTime * 24f / (dayLengthMinutes * 60f), 24f);
            if (Time.unscaledTime < nextUpdate) return;
            nextUpdate = Time.unscaledTime + updateInterval;
            Refresh();
        }

        // Every frame, but only a reference compare unless the controller replaced the sky (during its transitions).
        private void LateUpdate()
        {
            if (SceneManager.GetActiveScene() == gameObject.scene && RenderSettings.skybox != skyInstance) RenderSettings.skybox = skyInstance;
        }

        private void Refresh()
        {
            // The Day / Night buttons (controller) move the clock.
            if (dayNight.IsNight != lightsNight)
            {
                lightsNight = dayNight.IsNight;
                if (lightsNight != WantsNightLights(Hour)) Hour = lightsNight ? buttonNightHour : buttonDayHour;
            }
            // The clock switches the lights at dusk and dawn.
            bool wantNight = WantsNightLights(Hour);
            if (wantNight != lightsNight)
            {
                lightsNight = wantNight;
                if (wantNight) dayNight.ApplyNight();
                else dayNight.ApplyDay();
            }

            Weights = PhaseWeights(Hour);
            skyInstance.SetVector(WeightsId, Weights);
            skyInstance.SetVector(SunId, SunDirection(Hour));
            skyInstance.SetVector(MoonId, MoonDirection());
        }

        private bool WantsNightLights(float hour) => hour >= lightsToNightHour || hour < lightsToDayHour;

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

        private static float Smoother(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * x * (x * (x * 6f - 15f) + 10f);
        }
    }
}
