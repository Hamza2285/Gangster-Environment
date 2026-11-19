using UnityEngine;

namespace OpenworldCrime.Environment
{
    /// <summary>
    /// Hollywood-style searchlights on a rooftop: a few beams into the night sky, slowly sweeping and crossing each other.
    /// Night only: they fade in at dusk and out at dawn (Hamza's CityDayNightController, whatever drives it); by day the beams
    /// are switched off and nothing animates. Mobile cheap: no lights, one additive mesh per beam, brightness through a
    /// MaterialPropertyBlock (the shared material asset is never changed).
    /// </summary>
    [DisallowMultipleComponent]
    public class RooftopSkySearchlight : MonoBehaviour
    {
        [System.Serializable]
        public class Beam
        {
            public Transform pivot;
            public Renderer beam;
            [Tooltip("Lamp glass (emissive), brightened with the beam.")]
            public Renderer lens;
            [Tooltip("Tilt from vertical (degrees): middle, swing and speed.")]
            public float tilt = 22f, tiltSwing = 12f, tiltSpeed = 0.21f;
            [Tooltip("Turn (degrees): middle, swing and speed.")]
            public float yaw = 0f, yawSwing = 75f, yawSpeed = 0.13f;
            public float phase;
        }

        [SerializeField] private Beam[] beams = new Beam[0];
        [Tooltip("Day / night state. Empty = the scene's controller, found once at start.")]
        [SerializeField] private CityDayNightController dayNight;
        [SerializeField, Min(0f)] private float nightIntensity = 1f;
        [Tooltip("Seconds to fade in / out at dusk and dawn.")]
        [SerializeField, Min(0.1f)] private float fadeSeconds = 4f;
        [SerializeField] private Color lensColour = new Color(1f, 0.95f, 0.85f, 1f);
        [SerializeField, Min(0f)] private float lensGlow = 6f;

        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private MaterialPropertyBlock block;
        private float level = -1f;
        private float nextCheck;
        private bool night;

        private void Start()
        {
            if (dayNight == null) dayNight = FindFirstObjectByType<CityDayNightController>();
            block = new MaterialPropertyBlock();
            night = dayNight == null || dayNight.IsNight;
            SetLevel(night ? 1f : 0f);
        }

        private void Update()
        {
            if (Time.time >= nextCheck)
            {
                nextCheck = Time.time + 1f;
                night = dayNight == null || dayNight.IsNight;
            }
            float target = night ? 1f : 0f;
            if (!Mathf.Approximately(level, target)) SetLevel(Mathf.MoveTowards(level, target, Time.deltaTime / fadeSeconds));
            if (level <= 0f) return; // day: nothing to animate

            float t = Time.time;
            for (int i = 0; i < beams.Length; i++)
            {
                Beam b = beams[i];
                if (b.pivot == null) continue;
                float tilt = b.tilt + Mathf.Sin(t * b.tiltSpeed + b.phase) * b.tiltSwing;
                float yaw = b.yaw + Mathf.Sin(t * b.yawSpeed + b.phase * 1.7f) * b.yawSwing;
                b.pivot.localRotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(tilt, 0f, 0f);
            }
        }

        private void SetLevel(float value)
        {
            level = Mathf.Clamp01(value);
            bool on = level > 0f;
            for (int i = 0; i < beams.Length; i++)
            {
                Beam b = beams[i];
                if (b.beam != null)
                {
                    b.beam.enabled = on;
                    if (on)
                    {
                        b.beam.GetPropertyBlock(block);
                        block.SetFloat(IntensityId, nightIntensity * level);
                        b.beam.SetPropertyBlock(block);
                    }
                }
                if (b.lens != null)
                {
                    b.lens.GetPropertyBlock(block);
                    block.SetColor(EmissionId, lensColour * (lensGlow * level));
                    b.lens.SetPropertyBlock(block);
                }
            }
        }
    }
}
