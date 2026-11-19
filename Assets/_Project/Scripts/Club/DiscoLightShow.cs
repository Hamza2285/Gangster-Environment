using System;
using UnityEngine;

namespace OpenworldCrime.Club
{
    /// <summary>
    /// The club's animated lighting, driven from one Update for the whole room: sweeping light fixtures (beam meshes and a
    /// few realtime spot lights), the spinning disco ball, colour-cycling beam materials and neon pulsing on the beat.
    /// The room's ambience itself is baked; nothing here touches the lightmaps.
    ///
    /// Materials are animated through runtime copies created in Awake (assigned to the listed renderers), so the material
    /// assets are never modified in play mode.
    /// </summary>
    [DisallowMultipleComponent]
    public class DiscoLightShow : MonoBehaviour
    {
        [Serializable]
        public class SweepingFixture
        {
            public Transform target;
            [Tooltip("Half-angle of the yaw sweep around world up (degrees).")]
            public float amplitude = 25f;
            [Tooltip("Sweeps per second.")]
            public float frequency = 0.15f;
            public float phase;
            [Tooltip("Extra tilt sweep around the fixture's own right axis (degrees).")]
            public float tiltAmplitude;
            [NonSerialized] public Quaternion baseRotation;
        }

        [Serializable]
        public class CycledMaterial
        {
            public Material source;
            public Renderer[] renderers = new Renderer[0];
            [Tooltip("Animate _BaseColor through the palette (beams).")]
            public bool cycleColour = true;
            [Tooltip("Pulse _EmissionColor on the beat (neon).")]
            public bool pulseEmission;
            [Tooltip("Offset along the palette (0-1) so materials do not share one colour.")]
            public float paletteOffset;
            [NonSerialized] public Material instance;
            [NonSerialized] public Color baseColour;
            [NonSerialized] public Color baseEmission;
        }

        [Header("Rhythm")]
        [SerializeField, Min(30f)] private float beatsPerMinute = 124f;
        [Tooltip("Seconds for one trip through the colour palette.")]
        [SerializeField, Min(0.5f)] private float paletteCycleSeconds = 12f;
        [SerializeField] private Gradient palette = DefaultPalette();

        [Header("Moving Fixtures")]
        [SerializeField] private SweepingFixture[] fixtures = new SweepingFixture[0];
        [Tooltip("Realtime spot lights that sweep with their own fixture entry above and take the palette colour.")]
        [SerializeField] private Light[] colouredLights = new Light[0];

        [Header("Disco Ball")]
        [SerializeField] private Transform[] spinners = new Transform[0];
        [SerializeField] private float spinDegreesPerSecond = 25f;

        [Header("Materials")]
        [SerializeField] private CycledMaterial[] materials = new CycledMaterial[0];
        [Tooltip("Emission multiplier range for pulsing neon (low, high).")]
        [SerializeField] private Vector2 pulseRange = new Vector2(0.75f, 1.25f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private void Awake()
        {
            for (int i = 0; i < fixtures.Length; i++)
                if (fixtures[i].target != null) fixtures[i].baseRotation = fixtures[i].target.localRotation;

            foreach (CycledMaterial entry in materials)
            {
                if (entry.source == null) continue;
                entry.instance = new Material(entry.source) { name = entry.source.name + " (Runtime)" };
                entry.baseColour = entry.source.HasProperty(BaseColorId) ? entry.source.GetColor(BaseColorId) : Color.white;
                entry.baseEmission = entry.source.HasProperty(EmissionColorId) ? entry.source.GetColor(EmissionColorId) : Color.black;
                foreach (Renderer r in entry.renderers)
                {
                    if (r == null) continue;
                    Material[] shared = r.sharedMaterials;
                    bool changed = false;
                    for (int m = 0; m < shared.Length; m++)
                    {
                        if (shared[m] != entry.source) continue;
                        shared[m] = entry.instance;
                        changed = true;
                    }
                    if (changed) r.sharedMaterials = shared;
                }
            }
        }

        private void OnDestroy()
        {
            foreach (CycledMaterial entry in materials)
                if (entry.instance != null) Destroy(entry.instance);
        }

        // Per-frame visual animation of the whole rig (one Update for the room).
        private void Update()
        {
            float time = Time.time;
            float paletteTime = time / paletteCycleSeconds;

            for (int i = 0; i < fixtures.Length; i++)
            {
                SweepingFixture f = fixtures[i];
                if (f.target == null) continue;
                float angle = 2f * Mathf.PI * (f.frequency * time + f.phase);
                Quaternion yaw = Quaternion.AngleAxis(Mathf.Sin(angle) * f.amplitude, Vector3.up);
                Quaternion tilt = Quaternion.AngleAxis(Mathf.Sin(angle * 0.7f) * f.tiltAmplitude, Vector3.right);
                // Yaw about the parent's up (world up for these fixtures), tilt about the fixture's own axis.
                f.target.localRotation = yaw * f.baseRotation * tilt;
            }

            for (int i = 0; i < colouredLights.Length; i++)
            {
                Light l = colouredLights[i];
                if (l != null) l.color = palette.Evaluate(Mathf.Repeat(paletteTime + i / (float)Mathf.Max(1, colouredLights.Length), 1f));
            }

            float spin = spinDegreesPerSecond * Time.deltaTime;
            for (int i = 0; i < spinners.Length; i++)
                if (spinners[i] != null) spinners[i].Rotate(Vector3.up, spin, Space.World);

            // Sharp attack, soft decay on every beat.
            float beat = Mathf.Repeat(time * beatsPerMinute / 60f, 1f);
            float pulse = Mathf.Lerp(pulseRange.x, pulseRange.y, Mathf.Pow(1f - beat, 3f));
            foreach (CycledMaterial entry in materials)
            {
                if (entry.instance == null) continue;
                if (entry.cycleColour)
                {
                    Color c = palette.Evaluate(Mathf.Repeat(paletteTime + entry.paletteOffset, 1f));
                    c.a = entry.baseColour.a;
                    entry.instance.SetColor(BaseColorId, c);
                }
                if (entry.pulseEmission) entry.instance.SetColor(EmissionColorId, entry.baseEmission * pulse);
            }
        }

        private static Gradient DefaultPalette()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.15f, 0.85f), 0f),
                    new GradientColorKey(new Color(0.55f, 0.2f, 1f), 0.25f),
                    new GradientColorKey(new Color(0.1f, 0.85f, 1f), 0.5f),
                    new GradientColorKey(new Color(0.95f, 0.3f, 0.55f), 0.75f),
                    new GradientColorKey(new Color(1f, 0.15f, 0.85f), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }
    }
}
