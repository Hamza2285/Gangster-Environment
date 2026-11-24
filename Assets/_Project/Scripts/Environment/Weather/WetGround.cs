using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenworldCrime.Environment
{
    /// <summary>
    /// Wet ground around the camera while the world is wet (<see cref="WeatherManager.Wetness"/>): the city's ground materials are
    /// Hamza's / third-party (not changed), so a grid mesh centred under the camera, fitted to the ground by raycasts, is drawn
    /// over them with OpenworldCrime/WetGround (darker asphalt, a sky-coloured sheen, rippling puddles), fading out toward its
    /// edge. The heights are refreshed a few rays per frame (Rays Per Frame) when the camera has moved on; the mesh vertices are
    /// rewritten once per refresh. Off (renderer disabled, no work) while the world is dry or an interior is active.
    /// Cost while wet: one draw call, a few raycasts per frame, one small mesh upload per refresh.
    /// </summary>
    [DisallowMultipleComponent]
    public class WetGround : MonoBehaviour
    {
        [SerializeField] private WeatherManager weather;
        [SerializeField] private Material material;
        [Tooltip("Size (m) of the wet area around the camera.")]
        [SerializeField, Min(10f)] private float size = 70f;
        [SerializeField, Range(8, 48)] private int resolution = 28;
        [SerializeField, Min(1)] private int raysPerFrame = 60;
        [Tooltip("The grid follows the camera in steps of this many metres (re-fitted then).")]
        [SerializeField, Min(1f)] private float snapStep = 6f;
        [Tooltip("World geometry the ground mesh lies on (not people, vehicles or triggers).")]
        [SerializeField] private LayerMask groundLayers = 1;
        [SerializeField, Min(0f)] private float lift = 0.03f;
        [Tooltip("Rays start this far above the camera: low enough to pass under bridges, awnings and roofs overhead (their tops would " +
                 "otherwise be fitted and the grid would stretch from them down to the street).")]
        [SerializeField, Min(0.5f)] private float rayStartAbove = 3f;

        private static readonly int WetnessId = Shader.PropertyToID("_Wetness");
        private Mesh mesh;
        private Vector3[] vertices;
        private Color[] colors;
        private MeshRenderer meshRenderer;
        private Material instance;
        private Vector3 center;
        private bool hasCenter;
        private int fitCursor = -1;
        private Vector3 pendingCenter;
        private float appliedWetness = -1f;
        private Transform cam;

        private void Awake()
        {
            if (weather == null || material == null) { Debug.LogError($"[{nameof(WetGround)}] Missing weather manager or material on '{name}'.", this); enabled = false; return; }
            instance = new Material(material) { name = material.name + " (Runtime)" };
            BuildMesh();
            var go = new GameObject("Wet Ground Mesh");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = instance;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            meshRenderer.enabled = false;
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (instance != null) Destroy(instance);
        }

        private void LateUpdate()
        {
            float wet = weather.Wetness;
            bool show = wet > 0.005f && SceneManager.GetActiveScene() == gameObject.scene;
            if (!show)
            {
                if (meshRenderer.enabled) meshRenderer.enabled = false;
                hasCenter = false;
                fitCursor = -1;
                return;
            }
            if (cam == null || !cam.gameObject.activeInHierarchy) { Camera c = Camera.main; cam = c != null ? c.transform : null; }
            if (cam == null) return;

            Vector3 p = cam.position;
            Vector3 snapped = new Vector3(Mathf.Round(p.x / snapStep) * snapStep, p.y, Mathf.Round(p.z / snapStep) * snapStep);
            if (fitCursor < 0 && (!hasCenter || (new Vector2(snapped.x - center.x, snapped.z - center.z)).sqrMagnitude > 0.01f || Mathf.Abs(p.y - center.y) > 6f))
            {
                pendingCenter = snapped;
                fitCursor = 0;
            }
            if (fitCursor >= 0) Fit();

            if (!Mathf.Approximately(wet, appliedWetness))
            {
                appliedWetness = wet;
                instance.SetFloat(WetnessId, wet);
            }
            if (hasCenter && !meshRenderer.enabled) meshRenderer.enabled = true;
        }

        // A slice of the grid's ground heights per frame; when complete the mesh is updated and moved under the camera.
        private void Fit()
        {
            int n = resolution + 1;
            int total = n * n;
            int end = Mathf.Min(total, fitCursor + raysPerFrame);
            float half = size * 0.5f, step = size / resolution;
            for (int i = fitCursor; i < end; i++)
            {
                int x = i % n, z = i / n;
                float lx = -half + x * step, lz = -half + z * step;
                Vector3 origin = new Vector3(pendingCenter.x + lx, pendingCenter.y + rayStartAbove, pendingCenter.z + lz);
                float y;
                float edge = Mathf.Clamp01(1f - new Vector2(lx, lz).magnitude / half);
                if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 40f, groundLayers, QueryTriggerInteraction.Ignore) && hit.normal.y > 0.6f)
                    y = hit.point.y - pendingCenter.y + lift;
                else { y = -50f; edge = 0f; } // no ground (water, roof edge): hidden
                vertices[i] = new Vector3(lx, y, lz);
                colors[i] = new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, edge * 1.6f));
            }
            fitCursor = end;
            if (fitCursor < total) return;
            fitCursor = -1;
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.RecalculateBounds();
            center = pendingCenter;
            hasCenter = true;
            meshRenderer.transform.SetPositionAndRotation(center, Quaternion.identity);
        }

        private void BuildMesh()
        {
            int n = resolution + 1;
            vertices = new Vector3[n * n];
            colors = new Color[n * n];
            var tris = new int[resolution * resolution * 6];
            int k = 0;
            for (int z = 0; z < resolution; z++)
                for (int x = 0; x < resolution; x++)
                {
                    int a = z * n + x, b = a + n;
                    tris[k++] = a; tris[k++] = b; tris[k++] = a + 1;
                    tris[k++] = a + 1; tris[k++] = b; tris[k++] = b + 1;
                }
            mesh = new Mesh { name = "Wet Ground" };
            mesh.MarkDynamic();
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.triangles = tris;
        }

#if UNITY_EDITOR
        public void EditorSetup(WeatherManager manager, Material wetMaterial, LayerMask ground)
        {
            weather = manager;
            material = wetMaterial;
            groundLayers = ground;
        }
#endif
    }
}
