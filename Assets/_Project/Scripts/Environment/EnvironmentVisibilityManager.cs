using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OpenworldCrime.Environment
{
    /// <summary>
    /// Distance-based visibility for the static environment (lives under ///Manager///, one per scene, not a Singleton).
    /// - At start every MeshRenderer under the configured roots is put into a distance class by its size (small prop, medium
    ///   object) or by its root (vegetation); large objects (buildings, roads) are never touched and only use the camera far
    ///   plane. Renderers that already have a LODGroup are left to it.
    /// - Each class is one <see cref="CullingGroup"/> (bounding spheres measured from the player): Unity computes the distance
    ///   bands in its culling job and reports only changes, so there is no per-object Update and no per-frame loop here.
    /// - Hidden renderers use <see cref="Renderer.forceRenderingOff"/>, so systems that switch renderer.enabled keep working.
    /// - Each class has a hide band (Hysteresis metres past its distance) so objects on the edge do not flicker.
    /// - A broken <see cref="BreakableProp"/> is always visible until it is put back (it may fly into another band).
    /// Distances come from the graphics profiles through <see cref="SetDistances"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class EnvironmentVisibilityManager : MonoBehaviour
    {
        public enum RootCategory
        {
            /// <summary>Small or medium class by renderer size; large renderers are left alone.</summary>
            BySize,
            /// <summary>Every renderer under this root uses the vegetation distance.</summary>
            Vegetation
        }

        [Serializable]
        public struct VisibilityRoot
        {
            public Transform root;
            public RootCategory category;
        }

        private enum DistanceClass { Small = 0, Medium = 1, Vegetation = 2 }

        private class ClassGroup
        {
            public CullingGroup Group;
            public BoundingSphere[] Spheres;
            public Renderer[] Renderers;
            public bool[] Exempt;
            public int Count;
            public float Distance;
            public readonly float[] Bands = new float[2];
        }

        [Header("References")]
        [Tooltip("Distances are measured from this transform (the player root; it follows the player into vehicles).")]
        [SerializeField] private Transform distanceReference;
        [Tooltip("Gameplay cameras (on foot and vehicle). The first active one drives the culling groups.")]
        [SerializeField] private Camera[] cameras = new Camera[0];
        [SerializeField] private VisibilityRoot[] roots = new VisibilityRoot[0];

        [Header("Classes")]
        [Tooltip("Renderers up to this bounds diagonal (m) are small props.")]
        [SerializeField, Min(0.1f)] private float smallMaxSize = 6f;
        [Tooltip("Renderers up to this bounds diagonal (m) are medium objects; larger ones are never hidden by distance.")]
        [SerializeField, Min(0.1f)] private float mediumMaxSize = 25f;
        [Tooltip("Small props (signs, bollards, lamps...) cast real-time shadows. Off saves thousands of shadow draws for a tiny visual loss.")]
        [SerializeField] private bool smallPropsCastShadows;

        [Header("Distances (set by the graphics profile)")]
        [SerializeField, Min(10f)] private float smallPropDistance = 140f;
        [SerializeField, Min(10f)] private float mediumPropDistance = 300f;
        [SerializeField, Min(10f)] private float vegetationDistance = 350f;
        [Tooltip("Objects are hidden this many metres past their distance and shown again inside it.")]
        [SerializeField, Min(0f)] private float hysteresis = 10f;
        [Tooltip("Seconds between checks that the culling groups follow the active camera (on foot / vehicle switch).")]
        [SerializeField, Min(0.1f)] private float cameraCheckInterval = 0.5f;

        private readonly ClassGroup[] groups = new ClassGroup[3];
        private readonly Dictionary<Renderer, (int group, int index)> lookup = new Dictionary<Renderer, (int, int)>();
        private readonly List<Renderer> rendererBuffer = new List<Renderer>(8);
        private Camera activeCamera;
        private Coroutine cameraRoutine;

        public int SmallCount => groups[0] != null ? groups[0].Count : 0;
        public int MediumCount => groups[1] != null ? groups[1].Count : 0;
        public int VegetationCount => groups[2] != null ? groups[2].Count : 0;

        private void Awake()
        {
            if (distanceReference == null) Debug.LogWarning($"[{nameof(EnvironmentVisibilityManager)}] No distance reference on '{name}'; distances are measured from the active camera.", this);
            if (cameras.Length == 0) Debug.LogWarning($"[{nameof(EnvironmentVisibilityManager)}] No cameras assigned on '{name}'; nothing is hidden by distance.", this);
        }

        private void OnEnable()
        {
            Build();
            BreakableProp.OnBroken += HandleBroken;
            BreakableProp.OnPropReset += HandlePropReset;
            cameraRoutine = StartCoroutine(CameraRoutine());
        }

        private void OnDisable()
        {
            BreakableProp.OnBroken -= HandleBroken;
            BreakableProp.OnPropReset -= HandlePropReset;
            if (cameraRoutine != null) StopCoroutine(cameraRoutine);
            cameraRoutine = null;
            Release();
        }

        /// <summary>Hide distances (m) for small props, medium objects and vegetation (graphics profiles).</summary>
        public void SetDistances(float small, float medium, float vegetation)
        {
            smallPropDistance = Mathf.Max(10f, small);
            mediumPropDistance = Mathf.Max(10f, medium);
            vegetationDistance = Mathf.Max(10f, vegetation);
            ApplyBands();
        }

        private void Build()
        {
            var lists = new List<Renderer>[3] { new List<Renderer>(), new List<Renderer>(), new List<Renderer>() };
            for (int r = 0; r < roots.Length; r++)
            {
                if (roots[r].root == null) continue;
                foreach (MeshRenderer renderer in roots[r].root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (lookup.ContainsKey(renderer) || renderer.GetComponentInParent<LODGroup>() != null) continue;
                    float size = renderer.bounds.size.magnitude;
                    DistanceClass distanceClass;
                    if (roots[r].category == RootCategory.Vegetation) distanceClass = DistanceClass.Vegetation;
                    else if (size <= smallMaxSize)
                    {
                        distanceClass = DistanceClass.Small;
                        if (!smallPropsCastShadows) renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    }
                    else if (size <= mediumMaxSize) distanceClass = DistanceClass.Medium;
                    else continue;
                    lookup[renderer] = ((int)distanceClass, lists[(int)distanceClass].Count);
                    lists[(int)distanceClass].Add(renderer);
                }
            }

            for (int g = 0; g < groups.Length; g++)
            {
                List<Renderer> list = lists[g];
                var group = new ClassGroup
                {
                    Renderers = list.ToArray(),
                    Spheres = new BoundingSphere[Mathf.Max(1, list.Count)],
                    Exempt = new bool[list.Count],
                    Count = list.Count
                };
                for (int i = 0; i < list.Count; i++)
                {
                    Bounds b = list[i].bounds;
                    group.Spheres[i] = new BoundingSphere(b.center, b.extents.magnitude);
                }
                group.Group = new CullingGroup();
                group.Group.SetBoundingSpheres(group.Spheres);
                group.Group.SetBoundingSphereCount(group.Count);
                int captured = g;
                group.Group.onStateChanged = e => HandleStateChanged(captured, e);
                groups[g] = group;
            }
            ApplyBands();
            UpdateCamera();
        }

        private void Release()
        {
            for (int g = 0; g < groups.Length; g++)
            {
                ClassGroup group = groups[g];
                if (group == null) continue;
                group.Group.onStateChanged = null;
                group.Group.Dispose();
                for (int i = 0; i < group.Count; i++)
                    if (group.Renderers[i] != null) group.Renderers[i].forceRenderingOff = false;
                groups[g] = null;
            }
            lookup.Clear();
            activeCamera = null;
        }

        private void ApplyBands()
        {
            SetBands(0, smallPropDistance);
            SetBands(1, mediumPropDistance);
            SetBands(2, vegetationDistance);
        }

        // Band 0: inside the distance (shown). Band 1: hysteresis ring (keeps its state). Band 2: beyond (hidden).
        private void SetBands(int g, float distance)
        {
            ClassGroup group = groups[g];
            if (group == null) return;
            group.Distance = distance;
            group.Bands[0] = distance;
            group.Bands[1] = distance + hysteresis;
            group.Group.SetBoundingDistances(group.Bands);
        }

        private IEnumerator CameraRoutine()
        {
            var wait = new WaitForSeconds(cameraCheckInterval);
            while (true)
            {
                yield return wait;
                if (activeCamera == null || !activeCamera.isActiveAndEnabled) UpdateCamera();
            }
        }

        private void UpdateCamera()
        {
            Camera found = null;
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null && cameras[i].isActiveAndEnabled)
                {
                    found = cameras[i];
                    break;
                }
            }
            if (found == null || found == activeCamera) return;
            activeCamera = found;
            Transform reference = distanceReference != null ? distanceReference : found.transform;
            for (int g = 0; g < groups.Length; g++)
            {
                if (groups[g] == null) continue;
                groups[g].Group.targetCamera = found;
                groups[g].Group.SetDistanceReferencePoint(reference);
            }
        }

        private void HandleStateChanged(int g, CullingGroupEvent e)
        {
            ClassGroup group = groups[g];
            if (group == null || e.index >= group.Count || group.Exempt[e.index]) return;
            Renderer renderer = group.Renderers[e.index];
            if (renderer == null) return;
            if (e.currentDistance == 0) renderer.forceRenderingOff = false;
            else if (e.currentDistance >= 2) renderer.forceRenderingOff = true;
        }

        private void HandleBroken(BreakableProp prop) => SetExempt(prop, true);

        private void HandlePropReset(BreakableProp prop) => SetExempt(prop, false);

        private void SetExempt(BreakableProp prop, bool exempt)
        {
            if (prop == null) return;
            prop.GetComponentsInChildren(true, rendererBuffer);
            for (int i = 0; i < rendererBuffer.Count; i++)
            {
                Renderer renderer = rendererBuffer[i];
                if (!lookup.TryGetValue(renderer, out (int group, int index) entry)) continue;
                ClassGroup group = groups[entry.group];
                if (group == null) continue;
                group.Exempt[entry.index] = exempt;
                // Broken: always drawn. Put back: the current band decides again.
                renderer.forceRenderingOff = !exempt && group.Group.GetDistance(entry.index) >= 2;
            }
        }
    }
}
