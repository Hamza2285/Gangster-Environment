using System;
using OpenworldCrime.Vehicles;
using UnityEngine;

namespace OpenworldCrime.Environment
{
    /// <summary>
    /// In-game clock and automatic day/night cycle (lives under ///Manager///, not a Singleton).
    /// It owns the time of day and moves the sun along an east-to-west arc; the look (light, ambient, fog, sky, window and
    /// road glow, street lights) is blended between the existing day and night presets by <see cref="CityDayNightController"/>.
    /// Updates are throttled (a few times a second): lighting changes slowly, so per-frame work is not needed.
    /// </summary>
    [DisallowMultipleComponent]
    public class TimeOfDayManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CityDayNightController cityLighting;

        [Header("Clock")]
        [Tooltip("Hour the game starts at (0-24).")]
        [SerializeField, Range(0f, 24f)] private float startHour = 9f;
        [Tooltip("Real minutes for one full in-game day.")]
        [SerializeField, Min(0.5f)] private float dayLengthMinutes = 24f;
        [SerializeField] private bool autoAdvance = true;
        [Tooltip("Seconds between lighting updates.")]
        [SerializeField, Min(0.02f)] private float updateInterval = 0.2f;

        [Header("Sun")]
        [SerializeField, Range(3f, 9f)] private float sunrise = 6f;
        [SerializeField, Range(15f, 21f)] private float sunset = 19f;
        [Tooltip("Hours of twilight on each side of sunrise / sunset over which day blends into night.")]
        [SerializeField, Range(0.25f, 2f)] private float twilightHours = 0.8f;
        [SerializeField, Range(10f, 85f)] private float noonElevation = 58f;
        [Tooltip("Compass direction (deg) the sun faces at noon; the arc runs 90 deg either side of it.")]
        [SerializeField] private float noonYaw = 340f;
        [Tooltip("Direction of the moon light at night (pitch, yaw).")]
        [SerializeField] private Vector2 moonRotation = new Vector2(38f, 328f);

        [Header("Jump targets (Day / Night buttons)")]
        [SerializeField, Range(0f, 24f)] private float dayHour = 12f;
        [SerializeField, Range(0f, 24f)] private float nightHour = 22f;

        /// <summary>Hour of day, 0-24.</summary>
        public float Hour { get; private set; }
        /// <summary>0 = full night, 1 = full day.</summary>
        public float Daylight { get; private set; }
        public bool IsNight => Daylight < 0.45f;

        /// <summary>Raised when night falls (true) or day breaks (false).</summary>
        public event Action<bool> OnNightChanged;

        private float nextUpdate;
        private bool lastNight;
        private bool started;

        private void Awake()
        {
            if (cityLighting == null) Debug.LogError($"[{nameof(TimeOfDayManager)}] No CityDayNightController assigned on '{name}'.", this);
            Hour = Mathf.Repeat(startHour, 24f);
        }

        private void Start()
        {
            started = true;
            lastNight = !ComputeNight(Hour);
            Refresh();
        }

        private void Update()
        {
            if (autoAdvance) Hour = Mathf.Repeat(Hour + Time.deltaTime * 24f / (dayLengthMinutes * 60f), 24f);
            if (Time.unscaledTime < nextUpdate) return;
            nextUpdate = Time.unscaledTime + updateInterval;
            Refresh();
        }

        /// <summary>Sets the clock (0-24) and applies the look immediately.</summary>
        public void SetHour(float hour)
        {
            Hour = Mathf.Repeat(hour, 24f);
            if (started) Refresh();
        }

        public void JumpToDay() => SetHour(dayHour);
        public void JumpToNight() => SetHour(nightHour);
        public void SetPaused(bool paused) => autoAdvance = !paused;
        public void SetDayLength(float minutes) => dayLengthMinutes = Mathf.Max(0.5f, minutes);

        private bool ComputeNight(float hour) => DaylightAt(hour) < 0.45f;

        private float DaylightAt(float hour)
        {
            float up = Mathf.InverseLerp(sunrise - twilightHours, sunrise + twilightHours, hour);
            float down = 1f - Mathf.InverseLerp(sunset - twilightHours, sunset + twilightHours, hour);
            return Mathf.SmoothStep(0f, 1f, Mathf.Min(up, down));
        }

        private void Refresh()
        {
            Daylight = DaylightAt(Hour);
            float dayProgress = Mathf.InverseLerp(sunrise, sunset, Hour);                // 0 sunrise .. 1 sunset
            float elevation = Mathf.Sin(dayProgress * Mathf.PI) * noonElevation;
            float noon = Mathf.Clamp01(Mathf.Sin(dayProgress * Mathf.PI));
            Quaternion sunRot = Quaternion.Euler(Mathf.Max(elevation, 4f), noonYaw - 90f + 180f * dayProgress, 0f);
            Quaternion moonRot = Quaternion.Euler(moonRotation.x, moonRotation.y, 0f);
            // Through twilight the low sun hands over to the moon while the light is dim, so the swap is not visible.
            Quaternion light = Quaternion.Slerp(moonRot, sunRot, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.6f, Daylight)));
            if (cityLighting != null) cityLighting.ApplyBlend(Daylight, light, Daylight > 0.5f ? noon : 0f);

            bool night = IsNight;
            if (night != lastNight)
            {
                lastNight = night;
                VehicleLights.SetNightMode(night);
                OnNightChanged?.Invoke(night);
            }
        }
    }
}
