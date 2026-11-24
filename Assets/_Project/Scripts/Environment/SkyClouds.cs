using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenworldCrime.Environment
{
    /// <summary>
    /// Fake sky clouds (scene system, separate and toggleable: disable this component to remove them; not a Singleton): a
    /// low-poly dome generated at start, kept centred on the main camera just inside its far plane, drawn with
    /// OpenworldCrime/SkyClouds (scrolling noise, soft edges fading to the horizon, behind all solid geometry). Tinted from the
    /// time of day: from <see cref="SkyCycleController"/>'s morning / noon / evening / night weights when it is present, else
    /// from the day/night controller's night blend; lit toward the sun by day and the moon by night (the scene's main light).
    /// Works on top of whichever sky is active. One draw call; the material is a runtime copy (the asset is never changed).
    /// Only shown while the city scene is the active scene (not in interiors).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(700)]
    public class SkyClouds : MonoBehaviour
    {
        [System.Serializable]
        public class Look
        {
            public Color lit = Color.white;
            public Color shade = new Color(0.66f, 0.7f, 0.8f);
            public Color sunGlow = new Color(1f, 0.92f, 0.8f);
            [Range(0f, 1f)] public float opacity = 0.9f;
            [Range(0f, 1f)] public float coverage = 0.5f;
        }

        [Header("References")]
        [SerializeField] private Material cloudMaterial;
        [SerializeField] private SkyCycleController skyCycle;
        [SerializeField] private CityDayNightController dayNight;

        [Header("Dome")]
        [Tooltip("Dome radius as a share of the camera's far plane.")]
        [SerializeField, Range(0.5f, 0.98f)] private float radiusShare = 0.9f;
        [SerializeField, Range(8, 64)] private int segments = 32;
        [SerializeField, Range(3, 16)] private int rings = 8;

        [Header("Looks (blended by the time of day)")]
        [SerializeField] private Look morning = new Look { lit = new Color(1f, 0.93f, 0.86f), shade = new Color(0.62f, 0.64f, 0.76f), sunGlow = new Color(1f, 0.8f, 0.6f), opacity = 0.85f, coverage = 0.48f };
        [SerializeField] private Look noon = new Look { lit = new Color(1f, 1f, 1f), shade = new Color(0.7f, 0.74f, 0.82f), sunGlow = new Color(1f, 0.95f, 0.85f), opacity = 0.9f, coverage = 0.5f };
        [SerializeField] private Look evening = new Look { lit = new Color(1f, 0.64f, 0.42f), shade = new Color(0.46f, 0.3f, 0.44f), sunGlow = new Color(1f, 0.55f, 0.3f), opacity = 0.9f, coverage = 0.52f };
        [SerializeField] private Look night = new Look { lit = new Color(0.22f, 0.25f, 0.36f), shade = new Color(0.07f, 0.08f, 0.13f), sunGlow = new Color(0.35f, 0.42f, 0.6f), opacity = 0.75f, coverage = 0.5f };
        [Tooltip("Seconds between look updates (the time of day changes slowly).")]
        [SerializeField, Min(0.02f)] private float lookInterval = 0.2f;

        private static readonly int LitId = Shader.PropertyToID("_CloudLit");
        private static readonly int ShadeId = Shader.PropertyToID("_CloudShade");
        private static readonly int GlowId = Shader.PropertyToID("_SunGlow");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        private static readonly int CoverageId = Shader.PropertyToID("_Coverage");
        private static readonly int SunDirId = Shader.PropertyToID("_SunDir");
        private static readonly int OvercastId = Shader.PropertyToID("_Overcast");

        private Transform dome;
        private MeshRenderer domeRenderer;
        private Mesh mesh;
        private Material instance;
        private Camera cam;
        private float nextLook;
        private float rain;   // 0-1: clouds thicken and darken (WeatherManager)
        private float flash;  // 0-1: lightning

        /// <summary>Rain amount 0-1 (WeatherManager): more, darker, denser clouds.</summary>
        public void SetWeather(float amount) { rain = Mathf.Clamp01(amount); nextLook = 0f; }

        /// <summary>Lightning brightness 0-1 (applied at once).</summary>
        public void SetFlash(float amount)
        {
            if (Mathf.Approximately(flash, amount)) return;
            flash = Mathf.Clamp01(amount);
            if (instance != null) UpdateLook();
        }

        private void Awake()
        {
            if (cloudMaterial == null)
            {
                Debug.LogError($"[{nameof(SkyClouds)}] No cloud material on '{name}'.", this);
                enabled = false;
                return;
            }
            instance = new Material(cloudMaterial) { name = cloudMaterial.name + " (Runtime)" };
            mesh = BuildDome(segments, rings);
            var go = new GameObject("Cloud Dome");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            domeRenderer = go.AddComponent<MeshRenderer>();
            domeRenderer.sharedMaterial = instance;
            domeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            domeRenderer.receiveShadows = false;
            domeRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            domeRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            domeRenderer.allowOcclusionWhenDynamic = false;
            dome = go.transform;
        }

        private void OnEnable()
        {
            if (domeRenderer != null) domeRenderer.enabled = true;
            nextLook = 0f;
        }

        private void OnDisable()
        {
            if (domeRenderer != null) domeRenderer.enabled = false;
        }

        private void OnDestroy()
        {
            if (instance != null) Destroy(instance);
            if (mesh != null) Destroy(mesh);
        }

        // After the cameras moved this frame: keep the dome on the camera, just inside its far plane.
        private void LateUpdate()
        {
            bool cityActive = SceneManager.GetActiveScene() == gameObject.scene;
            if (domeRenderer.enabled != cityActive) domeRenderer.enabled = cityActive;
            if (!cityActive) return;
            if (cam == null || !cam.isActiveAndEnabled) cam = Camera.main;
            if (cam == null) return;
            dome.SetPositionAndRotation(cam.transform.position, Quaternion.identity);
            float r = cam.farClipPlane * radiusShare;
            dome.localScale = new Vector3(r, r, r);
            if (Time.unscaledTime < nextLook) return;
            nextLook = Time.unscaledTime + lookInterval;
            UpdateLook();
        }

        private void UpdateLook()
        {
            Vector4 w;
            if (skyCycle != null && skyCycle.isActiveAndEnabled) w = skyCycle.Weights;
            else
            {
                float n = dayNight != null ? dayNight.NightBlend : 0f;
                // Without the sky cycle: day -> evening glow -> night along the night blend.
                float eve = Mathf.Clamp01(1f - Mathf.Abs(n - 0.45f) / 0.35f);
                w = new Vector4(0f, Mathf.Clamp01(1f - n - eve), eve, Mathf.Clamp01(n - eve * 0.5f));
            }
            float sum = Mathf.Max(0.0001f, w.x + w.y + w.z + w.w);
            w /= sum;
            Color lit = morning.lit * w.x + noon.lit * w.y + evening.lit * w.z + night.lit * w.w;
            Color shade = morning.shade * w.x + noon.shade * w.y + evening.shade * w.z + night.shade * w.w;
            Color glow = morning.sunGlow * w.x + noon.sunGlow * w.y + evening.sunGlow * w.z + night.sunGlow * w.w;
            float opacity = morning.opacity * w.x + noon.opacity * w.y + evening.opacity * w.z + night.opacity * w.w;
            float coverage = morning.coverage * w.x + noon.coverage * w.y + evening.coverage * w.z + night.coverage * w.w;
            // Rain: overcast (more cover, opaque, grey and darker), lightning lights them from inside.
            Color storm = new Color(0.42f, 0.45f, 0.5f) * Mathf.Lerp(1f, 0.32f, w.w);
            lit = Color.Lerp(lit, storm, rain * 0.75f) + new Color(0.75f, 0.8f, 1f) * (flash * 1.2f);
            shade = Color.Lerp(shade, storm * 0.55f, rain * 0.8f) + new Color(0.5f, 0.55f, 0.75f) * flash;
            glow *= 1f - rain * 0.7f;
            opacity = Mathf.Lerp(opacity, 1f, rain);
            coverage = Mathf.Lerp(coverage, 0.97f, rain);
            instance.SetColor(LitId, lit);
            instance.SetColor(ShadeId, shade);
            instance.SetColor(GlowId, glow);
            instance.SetFloat(OpacityId, opacity);
            instance.SetFloat(CoverageId, coverage);
            instance.SetFloat(OvercastId, rain * 0.85f);
            Light main = RenderSettings.sun;
            Vector3 toLight = main != null ? -main.transform.forward : Vector3.up;
            if (toLight.y < 0.05f) toLight = new Vector3(toLight.x, 0.05f, toLight.z).normalized; // a low sun still lights the clouds
            instance.SetVector(SunDirId, toLight);
        }

        // Upper hemisphere plus a short skirt below the horizon (hidden by the horizon fade), unit radius.
        private static Mesh BuildDome(int seg, int ring)
        {
            int rowsAbove = ring, rows = rowsAbove + 2;
            var verts = new Vector3[(rows + 1) * (seg + 1)];
            for (int r = 0; r <= rows; r++)
            {
                // r = 0: top; r = rowsAbove: horizon; below: the skirt.
                float elev = r <= rowsAbove ? Mathf.Lerp(90f, 0f, (float)r / rowsAbove) : -6f * (r - rowsAbove);
                float e = elev * Mathf.Deg2Rad;
                for (int s = 0; s <= seg; s++)
                {
                    float a = (float)s / seg * Mathf.PI * 2f;
                    verts[r * (seg + 1) + s] = new Vector3(Mathf.Cos(e) * Mathf.Cos(a), Mathf.Sin(e), Mathf.Cos(e) * Mathf.Sin(a));
                }
            }
            var tris = new int[rows * seg * 6];
            int k = 0;
            for (int r = 0; r < rows; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a = r * (seg + 1) + s, b = a + seg + 1;
                    tris[k++] = a; tris[k++] = a + 1; tris[k++] = b;
                    tris[k++] = a + 1; tris[k++] = b + 1; tris[k++] = b;
                }
            var m = new Mesh { name = "Cloud Dome" };
            m.vertices = verts;
            m.triangles = tris;
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            return m;
        }

#if UNITY_EDITOR
        public void EditorSetup(Material material, SkyCycleController cycle, CityDayNightController controller)
        {
            cloudMaterial = material;
            skyCycle = cycle;
            dayNight = controller;
        }
#endif
    }
}
