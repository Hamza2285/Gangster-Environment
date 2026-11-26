using OpenworldCrime.Player;
using UnityEngine;

namespace OpenworldCrime.Environment
{
    /// <summary>
    /// Water splashes, ripples and their sounds for the whole city. Three world-space particle systems (spray drops, foam puffs,
    /// flat ripple rings, texture-free OpenworldCrime/WeatherParticle materials) are shared by every splash: a splash is a few
    /// ParticleSystem.Emit calls at the point (the systems are the pool - no objects are spawned), sized by the impact speed:
    /// a step off a ledge gives a few drops and one ring, a fall from a roof or a car going in a tall spray and a wide foam patch.
    /// Sounds play on a few pooled 3D AudioSources, round robin.
    /// Listens to <see cref="PlayerSwimController"/>'s static events (entering the water, swim strokes, climbing out, the player's
    /// vehicle going in); <see cref="Splash"/> can be called by anything else. Nothing runs per frame.
    /// Lives under ///Manager/// next to <see cref="WaterManager"/> (not a Singleton).
    /// </summary>
    [DisallowMultipleComponent]
    public class WaterSplashFx : MonoBehaviour
    {
        [Header("Particles (world space)")]
        [SerializeField] private ParticleSystem spray;
        [SerializeField] private ParticleSystem foam;
        [SerializeField] private ParticleSystem ripples;

        [Header("Splash Size")]
        [Tooltip("Impact speed (m/s) of the smallest splash (a step in) ...")]
        [SerializeField, Min(0f)] private float smallSpeed = 1.5f;
        [Tooltip("... and of the biggest (a long fall, a car).")]
        [SerializeField, Min(1f)] private float bigSpeed = 18f;
        [SerializeField] private Vector2Int sprayCount = new Vector2Int(6, 60);
        [Tooltip("Speed multiplier of the spray drops, small to big splash.")]
        [SerializeField] private Vector2 spraySpeed = new Vector2(1.6f, 12f);
        [SerializeField] private Vector2Int foamCount = new Vector2Int(1, 7);
        [SerializeField] private Vector2 foamSize = new Vector2(0.6f, 1.7f);
        [Tooltip("Ring size (m) at the end of its life, small to big splash.")]
        [SerializeField] private Vector2 rippleSize = new Vector2(2.2f, 7f);
        [Tooltip("Vehicles are heavier: their impact speed is multiplied by this.")]
        [SerializeField, Min(0f)] private float vehicleSpeedScale = 1.6f;

        [Header("Swimming")]
        [SerializeField, Min(0f)] private float strokeRippleSize = 1.8f;
        [SerializeField, Min(0)] private int strokeSprayCount = 2;

        [Header("Audio")]
        [SerializeField] private AudioClip splashClip;
        [SerializeField] private AudioClip climbOutClip;
        [Tooltip("Pooled 3D sources, used round robin.")]
        [SerializeField] private AudioSource[] voices = new AudioSource[0];
        [SerializeField, Range(0f, 1f)] private float splashVolume = 0.9f;
        [SerializeField, Range(0f, 1f)] private float climbOutVolume = 0.6f;

        private int nextVoice;

        private void Awake()
        {
            if (spray == null || foam == null || ripples == null)
                Debug.LogWarning($"[{nameof(WaterSplashFx)}] Particle systems not assigned on '{name}': splashes are partly missing.", this);
        }

        private void OnEnable()
        {
            PlayerSwimController.OnWaterEntered += HandleWaterEntered;
            PlayerSwimController.OnSwimStroke += HandleStroke;
            PlayerSwimController.OnClimbedOut += HandleClimbedOut;
            PlayerSwimController.OnVehicleEnteredWater += HandleVehicleEntered;
        }

        private void OnDisable()
        {
            PlayerSwimController.OnWaterEntered -= HandleWaterEntered;
            PlayerSwimController.OnSwimStroke -= HandleStroke;
            PlayerSwimController.OnClimbedOut -= HandleClimbedOut;
            PlayerSwimController.OnVehicleEnteredWater -= HandleVehicleEntered;
        }

        /// <summary>A splash at a point on the water surface, sized by the impact speed (m/s), with its sound.</summary>
        public void Splash(Vector3 surfacePoint, float impactSpeed)
        {
            float k = Mathf.InverseLerp(smallSpeed, bigSpeed, impactSpeed);
            float shaped = k * k * (3f - 2f * k);
            if (spray != null)
            {
                var main = spray.main;
                main.startSpeedMultiplier = Mathf.Lerp(spraySpeed.x, spraySpeed.y, shaped);
                main.startSizeMultiplier = Mathf.Lerp(1f, 1.6f, shaped);
                Emit(spray, surfacePoint, Mathf.RoundToInt(Mathf.Lerp(sprayCount.x, sprayCount.y, shaped)));
            }
            if (foam != null)
            {
                var main = foam.main;
                main.startSizeMultiplier = Mathf.Lerp(foamSize.x, foamSize.y, shaped);
                main.startSpeedMultiplier = Mathf.Lerp(0.6f, 2.6f, shaped);
                Emit(foam, surfacePoint, Mathf.RoundToInt(Mathf.Lerp(foamCount.x, foamCount.y, shaped)));
            }
            Ripple(surfacePoint, Mathf.Lerp(rippleSize.x, rippleSize.y, shaped), k > 0.5f ? 2 : 1);
            Play(splashClip, surfacePoint, splashVolume * Mathf.Lerp(0.35f, 1f, k), Mathf.Lerp(1.25f, 0.9f, k));
        }

        private void HandleWaterEntered(Vector3 point, float speed) => Splash(point, speed);

        private void HandleVehicleEntered(Vector3 point, float speed) => Splash(point, speed * vehicleSpeedScale);

        // A swim stroke / wading step: one ring and a couple of drops, no sound.
        private void HandleStroke(Vector3 point, float intensity)
        {
            Ripple(point, strokeRippleSize * Mathf.Lerp(0.6f, 1f, intensity), 1);
            if (spray != null && strokeSprayCount > 0 && intensity > 0.3f)
            {
                var main = spray.main;
                main.startSpeedMultiplier = 1.2f;
                main.startSizeMultiplier = 0.8f;
                Emit(spray, point, strokeSprayCount);
            }
        }

        private void HandleClimbedOut(Vector3 point)
        {
            Ripple(point, rippleSize.x, 1);
            Play(climbOutClip, point, climbOutVolume, 1f);
        }

        private void Ripple(Vector3 point, float size, int count)
        {
            if (ripples == null) return;
            var main = ripples.main;
            main.startSizeMultiplier = size;
            Emit(ripples, point + Vector3.up * 0.03f, count);
        }

        private static void Emit(ParticleSystem system, Vector3 position, int count)
        {
            if (count <= 0) return;
            var p = new ParticleSystem.EmitParams { position = position, applyShapeToPosition = true };
            system.Emit(p, count);
        }

        private void Play(AudioClip clip, Vector3 position, float volume, float pitch)
        {
            if (clip == null || voices.Length == 0) return;
            AudioSource source = voices[nextVoice];
            nextVoice = (nextVoice + 1) % voices.Length;
            if (source == null) return;
            source.transform.position = position;
            source.pitch = pitch;
            source.PlayOneShot(clip, volume);
        }

#if UNITY_EDITOR
        public void EditorSetup(ParticleSystem spraySystem, ParticleSystem foamSystem, ParticleSystem rippleSystem,
            AudioClip splash, AudioClip climbOut, AudioSource[] sources)
        {
            spray = spraySystem;
            foam = foamSystem;
            ripples = rippleSystem;
            splashClip = splash;
            climbOutClip = climbOut;
            voices = sources;
        }
#endif
    }
}
