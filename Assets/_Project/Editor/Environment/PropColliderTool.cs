using System.Collections.Generic;
using OpenworldCrime.Environment;
using UnityEditor;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Fits cheap primitive colliders to environment props from their mesh vertices (in the collider host's local space,
    /// so rotated, scaled and mirrored objects work):
    /// - "pole" props (tall, with a small footprint near the ground: signs, lamps, poles, tree trunks) get a capsule around the
    ///   lower footprint (or a box when that footprint is long and flat), optionally plus a box around the top part (arm / head);
    /// - everything else gets one box around the mesh;
    /// - flat details (decals, ground planes) and tiny objects get nothing.
    /// Menu items work on the selection; the static methods are used for scripted scene setup. All changes are undoable.
    /// </summary>
    public static class PropColliderTool
    {
        public const float MinHeight = 0.15f;
        public const float MinDiagonal = 0.3f;
        private const float SliceFraction = 0.3f;

        [MenuItem("Tools/OpenworldCrime/Environment/Fit Prop Colliders (Selection)")]
        private static void FitSelection()
        {
            int added = 0;
            foreach (GameObject root in Selection.gameObjects)
                foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                    if (renderer.GetComponent<Collider>() == null) added += FitColliders(renderer.transform, new[] { renderer }, false, 0f);
            Debug.Log($"[{nameof(PropColliderTool)}] Added {added} colliders.");
        }

        [MenuItem("Tools/OpenworldCrime/Environment/Replace Small Mesh Colliders (Selection)")]
        private static void ReplaceSmallMeshCollidersInSelection()
        {
            int replaced = 0;
            foreach (GameObject root in Selection.gameObjects) replaced += ReplaceSmallMeshColliders(root.transform, 6f);
            Debug.Log($"[{nameof(PropColliderTool)}] Replaced {replaced} small mesh colliders with primitives.");
        }

        [MenuItem("Tools/OpenworldCrime/Environment/Make Selected Props Breakable")]
        private static void MakeSelectionBreakable()
        {
            int made = 0;
            foreach (GameObject go in Selection.gameObjects) if (MakeBreakable(go.transform) != null) made++;
            Debug.Log($"[{nameof(PropColliderTool)}] {made} props are breakable.");
        }

        /// <summary>Replaces non-convex MeshColliders of objects smaller than maxDiagonal (m) with fitted primitives.</summary>
        public static int ReplaceSmallMeshColliders(Transform root, float maxDiagonal)
        {
            int replaced = 0;
            foreach (MeshCollider meshCollider in root.GetComponentsInChildren<MeshCollider>(true))
            {
                MeshRenderer renderer = meshCollider.GetComponent<MeshRenderer>();
                if (meshCollider.convex || renderer == null || renderer.bounds.size.magnitude >= maxDiagonal) continue;
                Undo.DestroyObjectImmediate(meshCollider);
                if (FitColliders(renderer.transform, new[] { renderer }, false, 0f) > 0) replaced++;
            }
            return replaced;
        }

        /// <summary>
        /// Puts fitted colliders on host (from host's and its children's mesh renderers, top box included), adds a
        /// <see cref="BreakableProp"/> and takes the moving renderers out of static batching. Returns null if not possible.
        /// </summary>
        public static BreakableProp MakeBreakable(Transform host)
        {
            var existing = host.GetComponent<BreakableProp>();
            if (existing != null) return existing;
            foreach (Collider c in host.GetComponentsInChildren<Collider>(true))
            {
                if (c is MeshCollider mesh && !mesh.convex)
                {
                    Debug.LogWarning($"[{nameof(PropColliderTool)}] '{host.name}' has a non-convex MeshCollider; not made breakable.", host);
                    return null;
                }
            }

            if (host.GetComponents<Collider>().Length == 0)
            {
                var renderers = host.GetComponentsInChildren<MeshRenderer>(true);
                if (FitColliders(host, renderers, true, 0f) == 0) return null;
            }

            foreach (Transform t in host.GetComponentsInChildren<Transform>(true))
            {
                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(t.gameObject);
                StaticEditorFlags moving = flags & ~(StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.NavigationStatic);
                if (moving != flags)
                {
                    Undo.RecordObject(t.gameObject, "Make Breakable");
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, moving);
                }
            }

            var prop = Undo.AddComponent<BreakableProp>(host.gameObject);
            prop.EditorSetColliders(host.GetComponentsInChildren<Collider>(true));
            EditorUtility.SetDirty(prop);
            return prop;
        }

        /// <summary>
        /// Adds fitted primitive colliders to host from the given renderers' vertices. maxPoleHeight > 0 limits a pole capsule
        /// (e.g. tree trunks). Returns the number of colliders added.
        /// </summary>
        public static int FitColliders(Transform host, IList<MeshRenderer> renderers, bool topBox, float maxPoleHeight)
        {
            return FitColliders(host, renderers, topBox, maxPoleHeight, false, 0f, SliceFraction);
        }

        /// <summary>
        /// As above; forcePole always fits a pole (tree trunks), and the pole footprint is measured between bandFrom and
        /// bandTo (fractions of the height above the base), e.g. 0.1-0.2 to skip a tree's root flare.
        /// </summary>
        public static int FitColliders(Transform host, IList<MeshRenderer> renderers, bool topBox, float maxPoleHeight,
                                       bool forcePole, float bandFrom, float bandTo)
        {
            var points = new List<Vector3>(512);
            Matrix4x4 toHost = host.worldToLocalMatrix;
            foreach (MeshRenderer renderer in renderers)
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                Matrix4x4 m = toHost * renderer.transform.localToWorldMatrix;
                foreach (Vector3 v in filter.sharedMesh.vertices) points.Add(m.MultiplyPoint3x4(v));
            }
            if (points.Count == 0) return 0;

            Vector3 scale = Abs(host.lossyScale);
            Vector3 localUp = host.InverseTransformDirection(Vector3.up);
            int axis = Dominant(localUp);
            int a1 = (axis + 1) % 3, a2 = (axis + 2) % 3;

            Bounds all = BoundsOf(points, null, 0f, 0f, axis);
            Vector3 worldSize = Vector3.Scale(all.size, scale);
            if (worldSize[axis] < MinHeight || worldSize.magnitude < MinDiagonal) return 0;

            float h = all.size[axis];
            // A mirrored host flips the axis: the base is then at the max side.
            bool flipped = Vector3.Dot(localUp, AxisVector(axis)) < 0f;
            float baseLevel = flipped ? all.max[axis] : all.min[axis];
            float sign = flipped ? -1f : 1f;
            Bounds lower = BoundsOf(points, all, Mathf.Min(baseLevel + sign * h * bandFrom, baseLevel + sign * h * bandTo),
                                    Mathf.Max(baseLevel + sign * h * bandFrom, baseLevel + sign * h * bandTo), axis);
            Vector3 lowerWorld = Vector3.Scale(lower.size, scale);
            float footprintArea = lowerWorld[a1] * lowerWorld[a2];
            float boundsArea = worldSize[a1] * worldSize[a2];
            bool smallFootprint = footprintArea <= 0.35f * boundsArea && Mathf.Max(lowerWorld[a1], lowerWorld[a2]) <= 1.2f;
            bool pole = forcePole || (worldSize[axis] >= 1.5f && smallFootprint);

            if (!pole)
            {
                // Tall structures on legs (billboards): block only the reachable lower part around the footprint.
                const float ReachHeight = 4.5f;
                if (worldSize[axis] > ReachHeight && footprintArea < 0.6f * boundsArea)
                {
                    float reach = ReachHeight / Mathf.Max(scale[axis], 1e-4f);
                    Vector3 c = lower.center;
                    c[axis] = baseLevel + sign * reach * 0.5f;
                    Vector3 s = lower.size;
                    s[axis] = reach;
                    AddBox(host, new Bounds(c, s));
                    return 1;
                }
                AddBox(host, all);
                return 1;
            }

            float poleHeight = h;
            if (maxPoleHeight > 0f) poleHeight = Mathf.Min(h, maxPoleHeight / Mathf.Max(scale[axis], 1e-4f));
            Vector3 center = lower.center;
            center[axis] = baseLevel + sign * poleHeight * 0.5f;

            float wide = Mathf.Max(lowerWorld[a1], lowerWorld[a2]);
            float narrow = Mathf.Max(Mathf.Min(lowerWorld[a1], lowerWorld[a2]), 0.01f);
            int added = 1;
            if (wide / narrow > 2.5f && wide > 0.3f)
            {
                Vector3 size = lower.size;
                size[axis] = poleHeight;
                AddBox(host, new Bounds(center, size));
            }
            else
            {
                var capsule = Undo.AddComponent<CapsuleCollider>(host.gameObject);
                capsule.direction = axis;
                capsule.center = center;
                float radialScale = Mathf.Max(scale[a1], scale[a2], 1e-4f);
                capsule.radius = Mathf.Clamp(wide * 0.5f, 0.06f, 0.6f) / radialScale;
                capsule.height = Mathf.Max(poleHeight, capsule.radius * 2f);
            }

            if (topBox)
            {
                float topLevel = flipped ? all.min[axis] : all.max[axis];
                Bounds top = BoundsOf(points, all, Mathf.Min(topLevel, topLevel - sign * h * SliceFraction),
                                      Mathf.Max(topLevel, topLevel - sign * h * SliceFraction), axis);
                Vector3 topWorld = Vector3.Scale(top.size, scale);
                if (topWorld[a1] > lowerWorld[a1] + 0.3f || topWorld[a2] > lowerWorld[a2] + 0.3f)
                {
                    AddBox(host, top);
                    added++;
                }
            }
            return added;
        }

        private static void AddBox(Transform host, Bounds b)
        {
            var box = Undo.AddComponent<BoxCollider>(host.gameObject);
            box.center = b.center;
            box.size = Vector3.Max(b.size, Vector3.one * 0.02f);
        }

        // Bounds of all points (filter == null) or of the points whose axis coordinate lies in [from, to].
        private static Bounds BoundsOf(List<Vector3> points, Bounds? filter, float from, float to, int axis)
        {
            bool any = false;
            var b = new Bounds();
            foreach (Vector3 p in points)
            {
                if (filter.HasValue && (p[axis] < from || p[axis] > to)) continue;
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                else b.Encapsulate(p);
            }
            return any ? b : (filter ?? new Bounds());
        }

        private static int Dominant(Vector3 v)
        {
            v = Abs(v);
            return v.x >= v.y && v.x >= v.z ? 0 : (v.y >= v.z ? 1 : 2);
        }

        private static Vector3 AxisVector(int axis) => axis == 0 ? Vector3.right : (axis == 1 ? Vector3.up : Vector3.forward);

        private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    }
}
