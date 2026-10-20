using System.Collections.Generic;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Visible, limited impact dents for the custom vehicle models (traffic and drivable), using the same idea as RCC's mesh
    /// damage (vertices within a radius of the contact are pushed in with a falloff and clamped to a maximum displacement).
    /// RCC's own deformation cannot be used here: it lives privately inside RCC_CarControllerV3 (traffic cars have no RCC at all)
    /// and, when enabled on the custom drivables, it auto-collects every MeshFilter (the seated driver meshes, read-only wheel
    /// and door meshes). Original RCC vehicles keep RCC's damage.
    /// - Only the assigned body meshes deform (wheels, doors, glass parts, the driver are never touched). Colliders are not
    ///   rebuilt.
    /// - Shared/imported mesh assets are never modified: the first dent swaps in a runtime copy for this vehicle only, which is
    ///   destroyed again on <see cref="ResetDamage"/> (also called when the vehicle is disabled - iTS pooling - or destroyed).
    /// - Each impact moves a vertex at most Max Dent Per Impact, and in total at most Max Total Displacement, so one crash
    ///   never wrecks the shape. Normals are recomputed only where the mesh was dented; untouched areas keep their authored
    ///   shading.
    /// - Impacts are remembered (vehicle-local) so a taken-over traffic car hands its dents to its drivable counterpart.
    /// Lives on the vehicle root (next to the Rigidbody). Costs nothing until a qualifying impact happens.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class VehicleDamageDeformer : MonoBehaviour
    {
        [Header("Meshes")]
        [Tooltip("Body meshes that may dent (readable meshes). Never add wheels or the driver.")]
        [SerializeField] private MeshFilter[] deformableMeshes = new MeshFilter[0];

        [Header("Impact")]
        [Tooltip("Layers that dent the vehicle (world, other vehicles). Characters never dent it.")]
        [SerializeField] private LayerMask impactLayers = (1 << 0) | (1 << 13) | (1 << 19) | (1 << 20);
        [Tooltip("Impacts slower than this (m/s, along the contact normal) leave no dent.")]
        [SerializeField, Min(0f)] private float minImpactSpeed = 4f;
        [Tooltip("Share of the impact speed that counts for contacts along the vehicle's up axis (landings, kerbs).")]
        [SerializeField, Range(0f, 1f)] private float verticalContactScale = 0.3f;
        [Tooltip("Dent depth (m) of an impact right at the minimum speed.")]
        [SerializeField, Min(0f)] private float minDentDepth = 0.015f;
        [Tooltip("Extra dent depth (m) per m/s of impact speed above the minimum.")]
        [SerializeField, Min(0f)] private float depthPerSpeed = 0.012f;
        [Tooltip("Radius (m) around the contact point that is pushed in (falls off towards the edge).")]
        [SerializeField, Min(0.05f)] private float dentRadius = 0.55f;

        [Header("Limits")]
        [Tooltip("Max distance (m) a vertex can move in a single impact.")]
        [SerializeField, Min(0f)] private float maxDentPerImpact = 0.08f;
        [Tooltip("Max distance (m) a vertex can ever move from its original position.")]
        [SerializeField, Min(0f)] private float maxTotalDisplacement = 0.16f;
        [Tooltip("Contact points averaged into the single dent of a collision.")]
        [SerializeField, Range(1, 8)] private int maxContactsPerImpact = 4;
        [Tooltip("Seconds after a dent during which weaker contacts are ignored (sustained contact).")]
        [SerializeField, Min(0f)] private float cooldown = 0.25f;
        [Tooltip("Impacts remembered for handing dents over to a drivable counterpart.")]
        [SerializeField, Range(0, 32)] private int maxStoredImpacts = 16;

        [Header("Takeover (traffic cars only)")]
        [Tooltip("Optional: this traffic car's adapter, so its dents carry over to the drivable car that replaces it.")]
        [SerializeField] private TrafficVehicleAdapter adapter;

        /// <summary>An impact in vehicle-root space: where, which way the panel was pushed, how deep.</summary>
        public struct Impact
        {
            public Vector3 LocalPoint;
            public Vector3 LocalDirection;
            public float Depth;
        }

        private class MeshState
        {
            public MeshFilter Filter;
            public Mesh Original;
            public Mesh Runtime;
            public Vector3[] OriginalVertices;
            public Vector3[] OriginalNormals;
            public Vector3[] Vertices;
            public bool[] Dented;
            public readonly List<Vector3> Normals = new List<Vector3>();
        }

        private static readonly ContactPoint[] contactBuffer = new ContactPoint[8];

        private readonly List<Impact> impacts = new List<Impact>();
        private MeshState[] states;
        private Rigidbody body;
        private float lastDentTime = -100f;
        private float lastDentSpeed;

        /// <summary>True while at least one dent is applied.</summary>
        public bool IsDamaged { get; private set; }
        public IReadOnlyList<Impact> Impacts => impacts;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            var valid = new List<MeshState>();
            foreach (MeshFilter filter in deformableMeshes)
            {
                if (filter == null || filter.sharedMesh == null) continue;
                if (!filter.sharedMesh.isReadable)
                {
                    Debug.LogWarning($"[{nameof(VehicleDamageDeformer)}] Mesh '{filter.sharedMesh.name}' on '{name}' is not readable; it will not dent.", this);
                    continue;
                }
                valid.Add(new MeshState { Filter = filter, Original = filter.sharedMesh });
            }
            states = valid.ToArray();
            if (states.Length == 0)
                Debug.LogWarning($"[{nameof(VehicleDamageDeformer)}] '{name}' has no readable deformable meshes; it will not dent.", this);
        }

        private void OnEnable()
        {
            lastDentTime = -100f;
            lastDentSpeed = 0f;
            if (adapter != null) TrafficVehicleAdapter.OnTakenOver += HandleTakenOver;
        }

        // iTS recycles traffic cars with SetActive(false): every reuse starts undamaged.
        private void OnDisable()
        {
            if (adapter != null) TrafficVehicleAdapter.OnTakenOver -= HandleTakenOver;
            ResetDamage();
        }

        private void OnDestroy()
        {
            ResetDamage();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (states == null || states.Length == 0 || collision.contactCount == 0) return;
            // The hit collider's own layer (a traffic car's root is on "Traffic AI", its body collider on "Vehicle").
            if ((impactLayers.value & (1 << collision.collider.gameObject.layer)) == 0) return;

            float speed = VehicleCollisionAudio.GetImpactSpeed(collision, transform.up, verticalContactScale);
            if (speed < minImpactSpeed) return;
            if (Time.time - lastDentTime < cooldown && speed <= lastDentSpeed) return;
            lastDentTime = Time.time;
            lastDentSpeed = speed;

            // One dent per collision at the centre of its contacts, so several contact points never stack past the limit.
            int count = Mathf.Min(collision.GetContacts(contactBuffer), maxContactsPerImpact);
            Vector3 point = Vector3.zero;
            Vector3 normal = Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                point += contactBuffer[i].point;
                // ContactPoint.normal points towards this vehicle's collider: the direction the panel is pushed.
                normal += contactBuffer[i].normal;
            }
            if (count == 0 || normal.sqrMagnitude < 0.0001f) return;

            AddImpact(new Impact
            {
                LocalPoint = transform.InverseTransformPoint(point / count),
                LocalDirection = transform.InverseTransformDirection(normal.normalized),
                Depth = Mathf.Min(minDentDepth + (speed - minImpactSpeed) * depthPerSpeed, maxDentPerImpact)
            }, true);
        }

        /// <summary>Applies dents another vehicle received (same model, same root pose), e.g. after a traffic takeover.</summary>
        public void ApplyImpacts(IReadOnlyList<Impact> source)
        {
            if (source == null || states == null) return;
            for (int i = 0; i < source.Count; i++) AddImpact(source[i], i == source.Count - 1);
        }

        /// <summary>Restores the original meshes and frees the runtime copies (repair, respawn, pooling).</summary>
        public void ResetDamage()
        {
            impacts.Clear();
            IsDamaged = false;
            if (states == null) return;
            foreach (MeshState state in states)
            {
                if (state.Runtime == null) continue;
                if (state.Filter != null) state.Filter.sharedMesh = state.Original;
                Destroy(state.Runtime);
                state.Runtime = null;
            }
        }

        private void AddImpact(Impact impact, bool updateMesh)
        {
            if (maxStoredImpacts > 0)
            {
                if (impacts.Count >= maxStoredImpacts) impacts.RemoveAt(0);
                impacts.Add(impact);
            }

            Vector3 worldPoint = transform.TransformPoint(impact.LocalPoint);
            Vector3 worldDirection = transform.TransformDirection(impact.LocalDirection).normalized;
            foreach (MeshState state in states)
            {
                if (state.Filter == null) continue;
                Dent(state, worldPoint, worldDirection, impact.Depth);
                if (updateMesh) UpdateMesh(state);
            }
        }

        private void Dent(MeshState state, Vector3 worldPoint, Vector3 worldDirection, float depth)
        {
            Transform t = state.Filter.transform;
            float scale = Mathf.Max(0.0001f, t.lossyScale.x);
            Vector3 point = t.InverseTransformPoint(worldPoint);
            Vector3 push = t.InverseTransformDirection(worldDirection) * (depth / scale);
            float radius = dentRadius / scale;
            float radiusSqr = radius * radius;
            float maxTotal = maxTotalDisplacement / scale;

            // Cheap reject before touching (or copying) the mesh.
            Bounds bounds = state.Original.bounds;
            bounds.Expand(radius * 2f);
            if (!bounds.Contains(point)) return;

            EnsureRuntimeMesh(state);
            Vector3[] vertices = state.Vertices;
            Vector3[] original = state.OriginalVertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 offset = vertices[i] - point;
                float distSqr = offset.sqrMagnitude;
                if (distSqr >= radiusSqr) continue;

                float falloff = 1f - Mathf.Sqrt(distSqr) / radius;
                Vector3 moved = vertices[i] + push * (falloff * falloff);
                Vector3 total = moved - original[i];
                if (total.sqrMagnitude > maxTotal * maxTotal) moved = original[i] + total.normalized * maxTotal;
                vertices[i] = moved;
                state.Dented[i] = true;
                IsDamaged = true;
            }
        }

        private void EnsureRuntimeMesh(MeshState state)
        {
            if (state.Runtime != null) return;
            if (state.OriginalVertices == null)
            {
                state.OriginalVertices = state.Original.vertices;
                state.OriginalNormals = state.Original.normals;
            }
            state.Runtime = Instantiate(state.Original);
            state.Runtime.name = state.Original.name + " (dented)";
            state.Runtime.hideFlags = HideFlags.DontSave;
            state.Runtime.MarkDynamic();
            state.Vertices = (Vector3[])state.OriginalVertices.Clone();
            if (state.Dented == null || state.Dented.Length != state.Vertices.Length) state.Dented = new bool[state.Vertices.Length];
            else System.Array.Clear(state.Dented, 0, state.Dented.Length);
            state.Filter.sharedMesh = state.Runtime;
        }

        private static void UpdateMesh(MeshState state)
        {
            if (state.Runtime == null) return;
            Mesh mesh = state.Runtime;
            mesh.vertices = state.Vertices;
            mesh.RecalculateBounds();
            if (state.OriginalNormals == null || state.OriginalNormals.Length != state.Vertices.Length) return;
            // Recompute shading only where dented; the rest keeps its authored normals (no new seams across the car).
            mesh.RecalculateNormals();
            mesh.GetNormals(state.Normals);
            for (int i = 0; i < state.Normals.Count; i++)
            {
                if (!state.Dented[i]) state.Normals[i] = state.OriginalNormals[i];
            }
            mesh.SetNormals(state.Normals);
        }

        private void HandleTakenOver(TrafficVehicleAdapter traffic, RCCVehicleAdapter drivable)
        {
            if (traffic != adapter || drivable == null || impacts.Count == 0) return;
            VehicleDamageDeformer target = drivable.GetComponent<VehicleDamageDeformer>();
            if (target != null && target != this) target.ApplyImpacts(impacts);
        }
    }
}
