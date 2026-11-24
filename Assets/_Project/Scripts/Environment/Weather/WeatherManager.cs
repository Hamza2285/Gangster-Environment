using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenworldCrime.Environment
{
    public enum WeatherQuality { Low, High }

    /// <summary>
    /// Random rain with lightning for the city (global scene manager under ///Manager///, not a Singleton; others listen to
    /// <see cref="OnIntensityChanged"/> or read <see cref="Intensity"/> / <see cref="Wetness"/>).
    /// - Spells: after a quiet start, every Check Interval there is a Rain Chance of a spell of random strength and length; it
    ///   ramps in and out over Ramp Seconds (smooth), then a cooldown. Day and night alike; nothing while an interior is the
    ///   active scene (rain, sound and overlay fade out there). Debug: Force Rain, and the component's context menu / editor buttons.
    /// - Overcast overlay on top of the day/night look (CityDayNightController and CityAtmosphere keep owning the base values):
    ///   sun intensity, ambient and fog are scaled toward an overcast look by the rain intensity. The base is whatever those
    ///   systems last wrote (detected by comparing with what this wrote last frame), so when the rain clears the exact base
    ///   values are written back. Lightning pulses ride on the same overlay (and so are restored exactly too).
    /// - Rain visuals: a camera-following particle system (stretched billboards, no collisions) and splashes near the camera,
    ///   their rates scaled by the intensity and capped by <see cref="Quality"/>; clouds thicken and darken (SkyClouds).
    /// - Lightning during heavy rain: 2-3 quick pulses of sky / cloud / ambient / sun light, sometimes a distant bolt, and
    ///   thunder after a delay. Sound: a rain loop following the intensity.
    /// Per-frame work: a few compares by day without rain; a handful of property writes while it rains.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(900)] // after the day/night controller, the sky cycle and CityAtmosphere wrote their values
    public class WeatherManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CityDayNightController dayNight;
        [SerializeField] private SkyClouds clouds;
        [SerializeField] private ParticleSystem rain;
        [SerializeField] private ParticleSystem splashes;
        [SerializeField] private AudioSource rainLoop;
        [SerializeField] private AudioSource thunder;
        [SerializeField] private AudioClip[] thunderClips = new AudioClip[0];
        [SerializeField] private Renderer bolt;
        [Tooltip("World geometry splashes land on (not people, vehicles or triggers).")]
        [SerializeField] private LayerMask groundLayers = 1;

        [Header("Spells")]
        [Tooltip("No rain during the first seconds of a session.")]
        [SerializeField, Min(0f)] private float quietStartSeconds = 240f;
        [SerializeField, Min(5f)] private float checkInterval = 60f;
        [SerializeField, Range(0f, 1f)] private float rainChance = 0.22f;
        [SerializeField] private Vector2 spellSeconds = new Vector2(90f, 240f);
        [SerializeField] private Vector2 spellStrength = new Vector2(0.35f, 1f);
        [SerializeField, Min(1f)] private float rampSeconds = 25f;
        [SerializeField, Min(0f)] private float cooldownSeconds = 240f;
        [Tooltip("Debug: rain now (at Debug Strength), ignoring the schedule.")]
        [SerializeField] private bool forceRain;
        [SerializeField, Range(0f, 1f)] private float debugStrength = 1f;

        [Header("Quality (does not touch URP / quality settings)")]
        [SerializeField] private WeatherQuality quality = WeatherQuality.High;
        [SerializeField] private int maxRainParticlesHigh = 1600, maxRainParticlesLow = 600;
        [SerializeField] private float rainRateHigh = 1500f, rainRateLow = 550f;
        [SerializeField] private float splashRateHigh = 110f;

        [Header("Overcast overlay at full rain")]
        [SerializeField, Range(0f, 1f)] private float sunDim = 0.72f;
        [SerializeField, Range(0f, 1f)] private float shadowSoften = 0.65f;
        [Tooltip("Rain fog brightness as a share of the base fog brightness.")]
        [SerializeField, Range(0f, 1f)] private float fogDarken = 0.45f;
        [SerializeField, Range(0f, 1f)] private float ambientDim = 0.28f;
        [Tooltip("Fog distances are scaled by (1 - this).")]
        [SerializeField, Range(0f, 0.9f)] private float fogCloser = 0.45f;
        [SerializeField, Range(0f, 1f)] private float fogGrey = 0.6f;

        [Header("Lightning")]
        [SerializeField, Range(0f, 1f)] private float lightningFromIntensity = 0.6f;
        [SerializeField] private Vector2 lightningInterval = new Vector2(7f, 22f);
        [SerializeField, Range(0f, 1f)] private float boltChance = 0.6f;
        [SerializeField] private Vector2 thunderDelay = new Vector2(0.6f, 3f);
        [SerializeField, Min(0f)] private float flashAmbient = 0.9f;
        [SerializeField, Min(0f)] private float flashSun = 1.6f;

        [Header("Wetness")]
        [Tooltip("Seconds for the world to get fully wet in full rain, and to dry once it stops.")]
        [SerializeField, Min(1f)] private float wetSeconds = 20f;
        [SerializeField, Min(1f)] private float drySeconds = 90f;

        /// <summary>Rain intensity now, 0-1 (smoothly ramped).</summary>
        public float Intensity { get; private set; }
        /// <summary>How wet the world is, 0-1 (follows the rain, dries slowly).</summary>
        public float Wetness { get; private set; }
        /// <summary>Lightning flash brightness now, 0-1.</summary>
        public float Flash { get; private set; }
        public WeatherQuality Quality => quality;
        public bool IsRaining => target > 0f;
        /// <summary>Raised when the intensity changes noticeably (for listeners that do not read it per frame).</summary>
        public event Action<float> OnIntensityChanged;

        private static readonly int FlashId = Shader.PropertyToID("_WeatherFlash");

        private float target;          // intensity the current spell aims for (0 when dry)
        private float spellEnds, nextCheck, startedAt;
        private float lastRaised = -1f;
        private bool cityActive = true;
        private float nextLightning;
        private float flashStart = -1f, flashLength;
        private int flashPulses;
        private float thunderAt = -1f;
        private float boltUntil = -1f;
        private Transform cam;
        private float groundY, nextGroundCheck;

        // Overlay bookkeeping: base = what the other systems wrote; written = what this wrote last.
        private struct Values { public float sun, shadow; public Color sky, equator, ground; public float ambientIntensity; public Color fog; public float fogStart, fogEnd; }
        private Values baseValues, written;
        private bool overlayActive;
        private Light sun;

        private void Awake()
        {
            if (dayNight == null) { Debug.LogError($"[{nameof(WeatherManager)}] No CityDayNightController on '{name}'.", this); enabled = false; return; }
            startedAt = Time.unscaledTime;
            nextCheck = startedAt + quietStartSeconds;
            ApplyQuality();
            SetRainRate(0f);
            if (bolt != null) bolt.enabled = false;
            if (rainLoop != null) { rainLoop.loop = true; rainLoop.volume = 0f; }
            Shader.SetGlobalFloat(FlashId, 0f);
        }

        private void OnDisable()
        {
            RestoreOverlay();
            Shader.SetGlobalFloat(FlashId, 0f);
            if (rainLoop != null) rainLoop.Stop();
            if (bolt != null) bolt.enabled = false;
        }

        // ------------------------------------------------------------------ public controls (debug / missions)

        /// <summary>Starts a rain spell now (strength 0-1, length in seconds).</summary>
        public void StartRain(float strength, float seconds)
        {
            target = Mathf.Clamp01(strength);
            spellEnds = Time.unscaledTime + Mathf.Max(rampSeconds, seconds);
        }

        /// <summary>Lets the current spell clear (ramps out).</summary>
        public void StopRain()
        {
            target = 0f;
            forceRain = false;
            nextCheck = Time.unscaledTime + cooldownSeconds;
        }

        /// <summary>A lightning strike now (if it is raining).</summary>
        public void LightningNow() => StartLightning();

        public void SetQuality(WeatherQuality value)
        {
            quality = value;
            ApplyQuality();
        }

        [ContextMenu("Debug/Start Heavy Rain")] private void DebugHeavy() => StartRain(1f, 180f);
        [ContextMenu("Debug/Start Light Rain")] private void DebugLight() => StartRain(0.35f, 180f);
        [ContextMenu("Debug/Stop Rain")] private void DebugStop() => StopRain();
        [ContextMenu("Debug/Lightning")] private void DebugLightning() => LightningNow();

        // ------------------------------------------------------------------ per frame

        private void LateUpdate()
        {
            float now = Time.unscaledTime;
            cityActive = SceneManager.GetActiveScene() == gameObject.scene;
            Schedule(now);

            float goal = cityActive ? (forceRain ? debugStrength : target) : 0f;
            float rate = cityActive ? 1f / rampSeconds : 1f; // into an interior: gone within a second
            Intensity = Mathf.MoveTowards(Intensity, goal, rate * Time.unscaledDeltaTime);
            Wetness = Intensity > Wetness
                ? Mathf.MoveTowards(Wetness, Intensity, Time.unscaledDeltaTime / wetSeconds)
                : Mathf.MoveTowards(Wetness, Intensity, Time.unscaledDeltaTime / drySeconds);
            if (!cityActive) Wetness = Mathf.MoveTowards(Wetness, 0f, Time.unscaledDeltaTime);

            if (Mathf.Abs(Intensity - lastRaised) > 0.01f || (Intensity == 0f && lastRaised != 0f))
            {
                lastRaised = Intensity;
                SetRainRate(Intensity);
                if (clouds != null) clouds.SetWeather(Intensity);
                OnIntensityChanged?.Invoke(Intensity);
            }

            // Systems re-enabled by something else (or stopped while their emitter was off) resume on their own.
            if (Intensity > 0.001f && rain != null && !rain.isPlaying) rain.Play();
            if (Intensity > 0.001f && splashes != null && quality == WeatherQuality.High && !splashes.isPlaying) splashes.Play();
            UpdateLightning(now);
            UpdateAudio();
            FollowCamera();
            if (Intensity > 0f || Flash > 0f) ApplyOverlay();
            else if (overlayActive) RestoreOverlay();
        }

        private void Schedule(float now)
        {
            if (forceRain) return;
            if (target > 0f)
            {
                if (now >= spellEnds) { target = 0f; nextCheck = now + rampSeconds + cooldownSeconds; }
                return;
            }
            if (now < nextCheck) return;
            nextCheck = now + checkInterval;
            if (UnityEngine.Random.value < rainChance)
                StartRain(UnityEngine.Random.Range(spellStrength.x, spellStrength.y), UnityEngine.Random.Range(spellSeconds.x, spellSeconds.y));
        }

        private void ApplyQuality()
        {
            if (rain != null)
            {
                var main = rain.main;
                main.maxParticles = quality == WeatherQuality.High ? maxRainParticlesHigh : maxRainParticlesLow;
            }
            if (splashes != null)
            {
                var em = splashes.emission;
                em.enabled = quality == WeatherQuality.High;
            }
            lastRaised = -1f;
        }

        private void SetRainRate(float intensity)
        {
            bool on = intensity > 0.001f;
            if (rain != null)
            {
                var em = rain.emission;
                em.rateOverTime = intensity * (quality == WeatherQuality.High ? rainRateHigh : rainRateLow);
                if (on && !rain.isPlaying) rain.Play();
                else if (!on && rain.isPlaying && rain.particleCount == 0) rain.Stop();
            }
            if (splashes != null)
            {
                var em = splashes.emission;
                em.rateOverTime = quality == WeatherQuality.High ? Mathf.Clamp01(intensity * 1.3f - 0.15f) * splashRateHigh : 0f;
                if (on && quality == WeatherQuality.High && !splashes.isPlaying) splashes.Play();
                else if (!on && splashes.isPlaying) splashes.Stop();
            }
        }

        // The rain volume is a box above and around the camera (world simulation: drops do not move with the camera).
        private void FollowCamera()
        {
            if (Intensity <= 0f && (rain == null || rain.particleCount == 0)) return;
            if (cam == null || !cam.gameObject.activeInHierarchy) { Camera c = Camera.main; cam = c != null ? c.transform : null; }
            if (cam == null) return;
            Vector3 flatForward = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
            if (rain != null) rain.transform.position = cam.position + Vector3.up * 14f + flatForward.normalized * 6f;
            if (splashes != null)
            {
                // Splashes lie on the ground ahead of the camera (ground height refreshed a few times a second).
                Vector3 at = cam.position + flatForward.normalized * 5f;
                if (Time.unscaledTime >= nextGroundCheck)
                {
                    nextGroundCheck = Time.unscaledTime + 0.2f;
                    if (Physics.Raycast(at + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 30f, groundLayers, QueryTriggerInteraction.Ignore)) groundY = hit.point.y;
                    else groundY = cam.position.y - 1.6f;
                }
                splashes.transform.position = new Vector3(at.x, groundY + 0.04f, at.z);
            }
        }

        // ------------------------------------------------------------------ overlay (exactly restorable)

        private Values Read()
        {
            return new Values
            {
                sun = sun != null ? sun.intensity : 0f, shadow = sun != null ? sun.shadowStrength : 0f,
                sky = RenderSettings.ambientSkyColor, equator = RenderSettings.ambientEquatorColor, ground = RenderSettings.ambientGroundColor,
                ambientIntensity = RenderSettings.ambientIntensity,
                fog = RenderSettings.fogColor, fogStart = RenderSettings.fogStartDistance, fogEnd = RenderSettings.fogEndDistance
            };
        }

        private void ApplyOverlay()
        {
            if (sun == null) sun = dayNight.sun;
            Values now = Read();
            if (!overlayActive) { baseValues = now; overlayActive = true; }
            else
            {
                // A value that is not what this wrote was set by its owner since: that is the new base.
                if (now.sun != written.sun) baseValues.sun = now.sun;
                if (now.shadow != written.shadow) baseValues.shadow = now.shadow;
                if (now.sky != written.sky) baseValues.sky = now.sky;
                if (now.equator != written.equator) baseValues.equator = now.equator;
                if (now.ground != written.ground) baseValues.ground = now.ground;
                if (now.ambientIntensity != written.ambientIntensity) baseValues.ambientIntensity = now.ambientIntensity;
                if (now.fog != written.fog) baseValues.fog = now.fog;
                if (now.fogStart != written.fogStart) baseValues.fogStart = now.fogStart;
                if (now.fogEnd != written.fogEnd) baseValues.fogEnd = now.fogEnd;
            }
            float w = Intensity, f = Flash;
            float ambientScale = 1f - ambientDim * w;
            Color ambientAdd = new Color(0.55f, 0.6f, 0.75f) * (flashAmbient * f);
            Values v = baseValues;
            v.sun = baseValues.sun * (1f - sunDim * w) + flashSun * f;
            v.shadow = baseValues.shadow * (1f - shadowSoften * w);
            v.sky = baseValues.sky * ambientScale + ambientAdd;
            v.equator = baseValues.equator * ambientScale + ambientAdd * 0.8f;
            v.ground = baseValues.ground * ambientScale + ambientAdd * 0.5f;
            v.ambientIntensity = baseValues.ambientIntensity * ambientScale + f * 0.6f;
            float luma = baseValues.fog.r * 0.3f + baseValues.fog.g * 0.59f + baseValues.fog.b * 0.11f;
            float grey = luma * Mathf.Lerp(1f, fogDarken, w);
            v.fog = Color.Lerp(baseValues.fog, new Color(grey, grey * 1.04f, grey * 1.1f), fogGrey * w) + ambientAdd * 0.3f;
            v.fogStart = baseValues.fogStart * (1f - fogCloser * w);
            v.fogEnd = baseValues.fogEnd * (1f - fogCloser * w);
            Write(v);
            Shader.SetGlobalFloat(FlashId, f * 0.5f);
        }

        private void RestoreOverlay()
        {
            if (!overlayActive) return;
            Values now = Read();
            // Only values still as this wrote them are put back (anything its owner changed since stays).
            Values v = baseValues;
            if (now.sun != written.sun) v.sun = now.sun;
            if (now.shadow != written.shadow) v.shadow = now.shadow;
            if (now.sky != written.sky) v.sky = now.sky;
            if (now.equator != written.equator) v.equator = now.equator;
            if (now.ground != written.ground) v.ground = now.ground;
            if (now.ambientIntensity != written.ambientIntensity) v.ambientIntensity = now.ambientIntensity;
            if (now.fog != written.fog) v.fog = now.fog;
            if (now.fogStart != written.fogStart) v.fogStart = now.fogStart;
            if (now.fogEnd != written.fogEnd) v.fogEnd = now.fogEnd;
            Write(v);
            overlayActive = false;
            Shader.SetGlobalFloat(FlashId, 0f);
        }

        private void Write(Values v)
        {
            if (sun != null) { sun.intensity = v.sun; sun.shadowStrength = v.shadow; }
            RenderSettings.ambientSkyColor = v.sky;
            RenderSettings.ambientEquatorColor = v.equator;
            RenderSettings.ambientGroundColor = v.ground;
            RenderSettings.ambientIntensity = v.ambientIntensity;
            RenderSettings.fogColor = v.fog;
            RenderSettings.fogStartDistance = v.fogStart;
            RenderSettings.fogEndDistance = v.fogEnd;
            written = Read(); // as stored (colour / float precision), for the exact compare next frame
        }

        // ------------------------------------------------------------------ lightning and sound

        private void UpdateLightning(float now)
        {
            if (Intensity >= lightningFromIntensity && cityActive)
            {
                if (nextLightning <= 0f) nextLightning = now + UnityEngine.Random.Range(lightningInterval.x, lightningInterval.y) * 0.5f;
                else if (now >= nextLightning) StartLightning();
            }
            else nextLightning = 0f;

            if (flashStart >= 0f)
            {
                // A few quick pulses, each a sharp rise and a softer fall.
                float t = now - flashStart;
                float pulse = t * flashPulses / flashLength;
                int index = Mathf.FloorToInt(pulse);
                float frac = pulse - index;
                float amp = index == 0 ? 1f : index == 1 ? 0.55f : 0.8f;
                Flash = index < flashPulses ? amp * (frac < 0.15f ? frac / 0.15f : 1f - (frac - 0.15f) / 0.85f) : 0f;
                if (t >= flashLength) { flashStart = -1f; Flash = 0f; }
                if (clouds != null) clouds.SetFlash(Flash);
            }
            if (bolt != null && bolt.enabled)
            {
                bolt.material.SetFloat("_Intensity", now < boltUntil ? (2f + Mathf.Sin(now * 90f)) * Flash : 0f);
                if (now >= boltUntil) bolt.enabled = false;
            }
            if (thunderAt >= 0f && now >= thunderAt)
            {
                thunderAt = -1f;
                if (thunder != null && thunderClips.Length > 0 && cityActive)
                {
                    thunder.pitch = UnityEngine.Random.Range(0.32f, 0.48f);
                    thunder.PlayOneShot(thunderClips[UnityEngine.Random.Range(0, thunderClips.Length)], UnityEngine.Random.Range(0.6f, 1f));
                }
            }
        }

        private void StartLightning()
        {
            float now = Time.unscaledTime;
            nextLightning = now + UnityEngine.Random.Range(lightningInterval.x, lightningInterval.y);
            if (Intensity <= 0.05f) return;
            flashStart = now;
            flashPulses = UnityEngine.Random.Range(2, 4);
            flashLength = UnityEngine.Random.Range(0.35f, 0.6f);
            float delay = UnityEngine.Random.Range(thunderDelay.x, thunderDelay.y);
            thunderAt = now + delay;
            if (bolt != null && cam != null && UnityEngine.Random.value < boltChance) ShowBolt();
        }

        private void ShowBolt()
        {
            Camera c = cam.GetComponent<Camera>();
            float far = c != null ? c.farClipPlane * 0.75f : 600f;
            float yaw = UnityEngine.Random.Range(-60f, 60f) + cam.eulerAngles.y;
            Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 pos = cam.position + dir * far + Vector3.up * UnityEngine.Random.Range(110f, 170f);
            Transform t = bolt.transform;
            t.position = pos;
            t.rotation = Quaternion.LookRotation(pos - cam.position) * Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(-12f, 12f));
            float h = UnityEngine.Random.Range(220f, 300f);
            t.localScale = new Vector3(h * 0.25f * (UnityEngine.Random.value < 0.5f ? -1f : 1f), h, 1f);
            bolt.enabled = true;
            boltUntil = Time.unscaledTime + UnityEngine.Random.Range(0.12f, 0.25f);
        }

        private void UpdateAudio()
        {
            if (rainLoop == null) return;
            float vol = Intensity * 0.75f;
            if (vol > 0.001f && !rainLoop.isPlaying) rainLoop.Play();
            rainLoop.volume = vol;
            if (vol <= 0.001f && rainLoop.isPlaying) rainLoop.Stop();
        }

#if UNITY_EDITOR
        public void EditorSetup(CityDayNightController controller, SkyClouds sky, ParticleSystem rainSystem, ParticleSystem splashSystem,
            AudioSource loop, AudioSource thunderSource, AudioClip[] thunderSounds, Renderer boltRenderer)
        {
            dayNight = controller; clouds = sky; rain = rainSystem; splashes = splashSystem;
            rainLoop = loop; thunder = thunderSource; thunderClips = thunderSounds; bolt = boltRenderer;
            groundLayers = 1; // Default: the city's ground (vehicles are on RCC / Vehicle layers, invisible blockers on StopMove)
        }
#endif
    }
}
