using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Enlarges the shooting range section of the GANGFIRE instance without touching the shop room or the prefab asset.
    /// <para>
    /// The range is remapped by one warp (<see cref="Warp"/>, GANGFIRE prefab space): west of the shell's north-wall end
    /// (<see cref="KinkX"/>) and south of the north wall (<see cref="NorthWallZ"/>) the floor plan grows by
    /// <see cref="RangeScale"/>; heights are piecewise so the shooting bench lands at <see cref="BenchHeight"/> and the
    /// ceiling at <see cref="RangeScale"/> x its old height. The corridor mouth (x &gt;= KinkX, z &gt;= NorthWallZ) is
    /// unchanged in plan, so the corridor, its return patches and the floor join keep lining up.
    /// </para>
    /// Large meshes (shell, vents, glow / route markings) get warped copies (generated assets, MeshFilter override);
    /// everything else in the range gets a transform override around its bounds centre. Human-scale props (lockers, the
    /// shelf rack, guns) keep their size and are only moved.
    /// </summary>
    public static class GunShopRangeScaler
    {
        /// <summary>Plan scale of the range and its ceiling-height factor (relative to the 1.25 shop scale).</summary>
        public const float RangeScale = 1.6f;
        /// <summary>Shooting bench top in world metres (the package bench was knee height).</summary>
        public const float BenchHeight = 1.05f;
        /// <summary>Lightmap scale of the enlarged range renderers (keeps the bake near one 1024 atlas).</summary>
        private const float RangeLightmapScale = 0.7f;

        // Landmarks in GANGFIRE prefab space (measured from Interior_Shell).
        public const float KinkX = 4.37f;       // shell north wall ends here; the corridor mouth strip east of it keeps its width
        public const float NorthWallZ = -0.43f; // range north wall (shared with the corridor end)
        public const float BenchX = 1.07f;      // shooter-side edge of the bench line
        private const float FloorY = 0.03f;
        private const float BenchTopY = 0.47f;
        private const float CeilingY = 1.82f;
        private const float PartitionX = 2.97f; // cage partition the lockers / rack / wall guns hang on

        private const string MeshFolder = "Assets/_Project/Art/Interiors/RangeScaled";

        private static readonly string[] JoinObjects = { "AlignedCorridor", "CorridorWallEndPatch", "SingleThreshold", "PF_Corridor_Range_FlushFloorJoin" };
        private static readonly string[] KeepSizeNames = { "Prop_Locker", "Prop_Shelf_Rack", "PF_Fitted_Storage_Undershelf_And_BaseLighting" };

        public static float BenchTopTarget => BenchHeight / GunShopWorldBuilder.ShopScale;
        public static float CeilingTarget => CeilingY * RangeScale;
        private static float LowSlope => (BenchTopTarget - FloorY) / (BenchTopY - FloorY);
        private static float HighSlope => (CeilingTarget - BenchTopTarget) / (CeilingY - BenchTopY);
        /// <summary>Uniform scale given to range props / lights that follow the room (between the plan and height factors).</summary>
        public static float PropScale => (RangeScale + HighSlope) * 0.5f;

        public static float WarpX(float x) => x >= KinkX ? x : KinkX + (x - KinkX) * RangeScale;
        public static float WarpZ(float z) => z >= NorthWallZ ? z : NorthWallZ + (z - NorthWallZ) * RangeScale;
        public static float WarpY(float y)
        {
            if (y <= FloorY) return y;
            if (y <= BenchTopY) return FloorY + (y - FloorY) * LowSlope;
            return BenchTopTarget + (y - BenchTopY) * HighSlope;
        }

        /// <summary>Maps an original GANGFIRE prefab-space point into the enlarged range.</summary>
        public static Vector3 Warp(Vector3 p) => new Vector3(WarpX(p.x), WarpY(p.y), WarpZ(p.z));

        /// <summary>Maps original prefab-space bounds into the enlarged range.</summary>
        public static Bounds Warp(Bounds b)
        {
            var result = new Bounds();
            result.SetMinMax(Warp(b.min), Warp(b.max));
            return result;
        }

        private static Vector3 Slopes(Vector3 p) => new Vector3(
            p.x < KinkX ? RangeScale : 1f,
            p.y <= FloorY ? 1f : p.y <= BenchTopY ? LowSlope : HighSlope,
            p.z < NorthWallZ ? RangeScale : 1f);

        private static bool IsRangeBounds(Bounds b) => b.max.z < NorthWallZ + 0.13f;

        // ---------------------------------------------------------------- apply

        public static string Apply(GameObject shop)
        {
            var log = new StringBuilder();
            Transform shopT = shop.transform;
            var content = new Dictionary<Transform, Bounds>();
            var rangeLights = new List<Light>();
            foreach (Light l in shop.GetComponentsInChildren<Light>(true))
                if (l.gameObject.activeInHierarchy && l.type != LightType.Directional && ToPrefab(shopT, l.transform.position).z < NorthWallZ && !IsKeepSize(l.transform, shopT)) rangeLights.Add(l);

            // 1) Meshes that are too large or straddle the warp seams: warped copies.
            var warpedMeshes = new HashSet<Transform>();
            foreach (MeshRenderer r in shop.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r.gameObject.activeInHierarchy || IsJoin(r.transform, shopT) || IsKeepSize(r.transform, shopT)) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                Bounds b = ToPrefab(shopT, r.bounds);
                if (b.min.z > NorthWallZ - 0.3f) continue; // not (mostly) range
                bool large = b.size.x > 2.5f || b.size.z > 2.5f;
                bool seam = (b.min.x < KinkX - 0.05f && b.max.x > KinkX + 0.05f) || b.max.z > NorthWallZ + 0.05f;
                if (!large && !seam) continue;
                if (r.transform.childCount > 0) continue; // only leaf renderers (the shell, vents, strips)
                WarpMesh(mf, shopT);
                SetLightmapScale(r);
                warpedMeshes.Add(r.transform);
            }
            log.AppendLine($"  range: {warpedMeshes.Count} meshes warped");

            // 2) Human-scale props: moved, not scaled.
            int kept = MoveKeepSizeProps(shopT);
            log.AppendLine($"  range: {kept} props kept at human size");

            // 3) Everything else in the range follows the room.
            int units = 0;
            foreach (Transform child in shopT) units += WarpSubtree(child, shopT, warpedMeshes);
            log.AppendLine($"  range: {units} objects moved / scaled x{PropScale:0.00}");

            // 4) Baked light falloff does not follow the transform scale.
            float f = PropScale;
            foreach (Light l in rangeLights)
            {
                if (l.type == LightType.Rectangle || l.type == LightType.Disc) l.areaSize *= f;
                else
                {
                    l.range *= f;
                    l.intensity *= f * f;
                }
            }
            log.AppendLine($"  range: {rangeLights.Count} lights re-scaled");

            FitJoin(shopT);
            return log.ToString();
        }

        // Every transform below the shop root whose whole subtree lies in the range is one unit; mixed subtrees recurse.
        private static int WarpSubtree(Transform t, Transform shopT, HashSet<Transform> warpedMeshes)
        {
            if (IsJoin(t, shopT) || IsKeepSize(t, shopT) || IsGunGroup(t)) return 0;
            bool any, all, special;
            Bounds b = SubtreeBounds(t, shopT, warpedMeshes, out any, out all, out special);
            if (!any) return 0;
            if (all && !special)
            {
                WarpTransform(t, shopT, b.center, PropScale);
                foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true)) SetLightmapScale(r);
                return 1;
            }
            int count = 0;
            for (int i = 0; i < t.childCount; i++) count += WarpSubtree(t.GetChild(i), shopT, warpedMeshes);
            return count;
        }

        private static Bounds SubtreeBounds(Transform t, Transform shopT, HashSet<Transform> warpedMeshes, out bool any, out bool all, out bool special)
        {
            any = false; all = true; special = false;
            var b = new Bounds();
            bool has = false;
            foreach (Transform x in t.GetComponentsInChildren<Transform>(true))
            {
                if (warpedMeshes.Contains(x) || IsJoin(x, shopT) || IsKeepSize(x, shopT) || IsGunGroup(x)) { special = true; continue; }
                var r = x.GetComponent<Renderer>();
                var l = x.GetComponent<Light>();
                var p = x.GetComponent<ReflectionProbe>();
                if ((r == null && l == null && p == null) || !x.gameObject.activeInHierarchy) continue;
                Bounds own = r != null ? ToPrefab(shopT, r.bounds) : new Bounds(ToPrefab(shopT, x.position), Vector3.zero);
                bool inRange = IsRangeBounds(own);
                if (inRange) any = true; else all = false;
                if (!has) { b = own; has = true; } else b.Encapsulate(own);
            }
            if (!any) all = false;
            return b;
        }

        private static void WarpTransform(Transform t, Transform shopT, Vector3 prefabAnchor, float scale)
        {
            Vector3 pos = ToPrefab(shopT, t.position);
            Vector3 newPos = Warp(prefabAnchor) + (pos - prefabAnchor) * scale;
            t.position = shopT.TransformPoint(newPos);
            t.localScale *= scale;
            PrefabUtility.RecordPrefabInstancePropertyModifications(t);
        }

        private static void Translate(Transform t, Transform shopT, Vector3 prefabOffset)
        {
            t.position = shopT.TransformPoint(ToPrefab(shopT, t.position) + prefabOffset);
            PrefabUtility.RecordPrefabInstancePropertyModifications(t);
        }

        // Lockers, the shelf rack (with its fitted LEDs) and the guns on the partition move as one cluster along the
        // partition; the guns on the bench ride up with the bench top.
        private static int MoveKeepSizeProps(Transform shopT)
        {
            var cluster = new List<Transform>();
            var benchGuns = new List<Transform>();
            foreach (Transform t in shopT.GetComponentsInChildren<Transform>(true))
            {
                bool keep = IsKeepSizeRoot(t) || IsGunGroup(t);
                if (!keep || !t.gameObject.activeInHierarchy || IsKeepSize(t.parent, shopT)) continue; // moved with its root
                Bounds b;
                if (!TryRendererBounds(t, shopT, out b)) b = new Bounds(ToPrefab(shopT, t.position), Vector3.zero);
                if (!IsRangeBounds(b)) continue;
                if (b.center.x > PartitionX - 0.35f) cluster.Add(t); else benchGuns.Add(t);
            }

            if (cluster.Count > 0)
            {
                float minZ = float.MaxValue, maxZ = float.MinValue;
                foreach (Transform t in cluster)
                {
                    Bounds b;
                    if (!TryRendererBounds(t, shopT, out b)) b = new Bounds(ToPrefab(shopT, t.position), Vector3.zero);
                    minZ = Mathf.Min(minZ, b.min.z); maxZ = Mathf.Max(maxZ, b.max.z);
                }
                float cz = (minZ + maxZ) * 0.5f;
                var offset = new Vector3(WarpX(PartitionX) - PartitionX, 0f, WarpZ(cz) - cz);
                foreach (Transform t in cluster) Translate(t, shopT, offset);
            }
            foreach (Transform t in benchGuns)
            {
                Bounds b;
                if (!TryRendererBounds(t, shopT, out b)) continue;
                float y = b.min.y < BenchTopY + 0.1f ? BenchTopY : b.center.y;
                var offset = new Vector3(WarpX(b.center.x) - b.center.x, WarpY(y) - y, WarpZ(b.center.z) - b.center.z);
                Translate(t, shopT, offset);
            }
            return cluster.Count + benchGuns.Count;
        }

        // The corridor mouth keeps its plan; only the return patches beside it and the header above it must now reach the
        // taller range ceiling.
        private static void FitJoin(Transform shopT)
        {
            float top = CeilingTarget + 0.02f;
            Transform patches = shopT.Find("CorridorWallEndPatch");
            if (patches != null)
                foreach (Transform p in patches)
                    if (p.name.Contains("plaster")) StretchY(p, shopT, -1f, top);
            Transform closure = shopT.Find("SingleThreshold/Ceiling height transition closure");
            if (closure != null) StretchY(closure, shopT, 2.14f, top); // header from the corridor ceiling up to the range ceiling
        }

        // Re-fits a box-like renderer between two prefab-space heights (keeps its x / z). minY < 0 keeps the bottom.
        private static void StretchY(Transform t, Transform shopT, float minY, float maxY)
        {
            var r = t.GetComponent<Renderer>();
            if (r == null) return;
            Bounds b = ToPrefab(shopT, r.bounds);
            if (minY < 0f) minY = b.min.y;
            float k = (maxY - minY) / Mathf.Max(0.001f, b.size.y);
            Vector3 up = t.InverseTransformDirection(Vector3.up);
            Vector3 s = t.localScale;
            // Scale the local axis that points up.
            if (Mathf.Abs(up.x) > 0.7f) s.x *= k; else if (Mathf.Abs(up.z) > 0.7f) s.z *= k; else s.y *= k;
            t.localScale = s;
            Bounds nb = ToPrefab(shopT, r.bounds);
            t.position += shopT.TransformVector(new Vector3(0f, minY - nb.min.y, 0f));
            PrefabUtility.RecordPrefabInstancePropertyModifications(t);
        }

        // ---------------------------------------------------------------- mesh warp

        // Triangles with a vertex south of the north wall are warped; the rest of the mesh (e.g. shell pieces under the
        // shop) is copied unchanged. UVs, lightmap UVs and sub-meshes are preserved.
        private static void WarpMesh(MeshFilter mf, Transform shopT)
        {
            Mesh src = mf.sharedMesh;
            Matrix4x4 toPrefab = shopT.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            Matrix4x4 fromPrefab = toPrefab.inverse;

            Vector3[] v = src.vertices;
            Vector3[] n = src.normals;
            Vector4[] tan = src.tangents;
            Color[] col = src.colors;
            var uvs = new List<Vector4>[4];
            for (int c = 0; c < 4; c++)
            {
                var list = new List<Vector4>();
                src.GetUVs(c, list);
                uvs[c] = list.Count == v.Length ? list : null;
            }

            var outV = new List<Vector3>();
            var outN = new List<Vector3>();
            var outT = new List<Vector4>();
            var outC = new List<Color>();
            var outUv = new List<Vector4>[4];
            for (int c = 0; c < 4; c++) outUv[c] = uvs[c] != null ? new List<Vector4>() : null;
            var plainMap = new Dictionary<int, int>();
            var warpMap = new Dictionary<int, int>();

            System.Func<int, bool, int> emit = (i, warp) =>
            {
                Dictionary<int, int> map = warp ? warpMap : plainMap;
                int existing;
                if (map.TryGetValue(i, out existing)) return existing;
                Vector3 p = toPrefab.MultiplyPoint3x4(v[i]);
                if (warp)
                {
                    Vector3 slope = Slopes(p);
                    outV.Add(fromPrefab.MultiplyPoint3x4(Warp(p)));
                    if (n.Length == v.Length)
                    {
                        Vector3 pn = toPrefab.MultiplyVector(n[i]);
                        pn = new Vector3(pn.x / slope.x, pn.y / slope.y, pn.z / slope.z);
                        outN.Add(fromPrefab.MultiplyVector(pn).normalized);
                    }
                    if (tan.Length == v.Length)
                    {
                        Vector3 pt = toPrefab.MultiplyVector(tan[i]);
                        pt = Vector3.Scale(pt, slope);
                        Vector3 lt = fromPrefab.MultiplyVector(pt).normalized;
                        outT.Add(new Vector4(lt.x, lt.y, lt.z, tan[i].w));
                    }
                }
                else
                {
                    outV.Add(v[i]);
                    if (n.Length == v.Length) outN.Add(n[i]);
                    if (tan.Length == v.Length) outT.Add(tan[i]);
                }
                if (col.Length == v.Length) outC.Add(col[i]);
                for (int c = 0; c < 4; c++) if (outUv[c] != null) outUv[c].Add(uvs[c][i]);
                int index = outV.Count - 1;
                map[i] = index;
                return index;
            };

            var subs = new List<int>[src.subMeshCount];
            for (int s = 0; s < src.subMeshCount; s++)
            {
                int[] ids = src.GetTriangles(s);
                var list = new List<int>(ids.Length);
                for (int i = 0; i < ids.Length; i += 3)
                {
                    float za = toPrefab.MultiplyPoint3x4(v[ids[i]]).z, zb = toPrefab.MultiplyPoint3x4(v[ids[i + 1]]).z, zc = toPrefab.MultiplyPoint3x4(v[ids[i + 2]]).z;
                    float minZ = Mathf.Min(za, Mathf.Min(zb, zc)), maxZ = Mathf.Max(za, Mathf.Max(zb, zc));
                    // Reaches into the range, or lies on the north wall plane itself (the wall faces).
                    bool warp = minZ < NorthWallZ - 0.005f || maxZ < NorthWallZ + 0.05f;
                    list.Add(emit(ids[i], warp)); list.Add(emit(ids[i + 1], warp)); list.Add(emit(ids[i + 2], warp));
                }
                subs[s] = list;
            }

            // Refill the existing asset in place on a rebuild (keeps its GUID; CopySerialized would leave the GPU copy stale).
            EnsureFolder(MeshFolder);
            string path = MeshFolder + "/" + Sanitize(AnimationUtility.CalculateTransformPath(mf.transform, shopT)) + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool isNew = mesh == null;
            if (isNew) mesh = new Mesh();
            mesh.Clear();
            mesh.name = src.name + "_RangeScaled";
            mesh.indexFormat = outV.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(outV);
            if (outN.Count == outV.Count) mesh.SetNormals(outN);
            if (outT.Count == outV.Count) mesh.SetTangents(outT);
            if (outC.Count == outV.Count) mesh.SetColors(outC);
            for (int c = 0; c < 4; c++) if (outUv[c] != null) mesh.SetUVs(c, outUv[c]);
            mesh.subMeshCount = subs.Length;
            for (int s = 0; s < subs.Length; s++) mesh.SetTriangles(subs[s], s, false);
            mesh.RecalculateBounds();
            if (isNew) AssetDatabase.CreateAsset(mesh, path);
            else EditorUtility.SetDirty(mesh);
            mf.sharedMesh = mesh;
            PrefabUtility.RecordPrefabInstancePropertyModifications(mf);
        }

        // ---------------------------------------------------------------- helpers

        private static void SetLightmapScale(Renderer r)
        {
            var so = new SerializedObject(r);
            SerializedProperty p = so.FindProperty("m_ScaleInLightmap");
            if (p == null) return;
            p.floatValue = RangeLightmapScale;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static bool IsJoin(Transform t, Transform shopT)
        {
            Transform top = TopChild(t, shopT);
            if (top == null) return false;
            for (int i = 0; i < JoinObjects.Length; i++) if (top.name == JoinObjects[i]) return true;
            return false;
        }

        private static bool IsKeepSizeRoot(Transform t)
        {
            for (int i = 0; i < KeepSizeNames.Length; i++) if (t.name.StartsWith(KeepSizeNames[i])) return true;
            return false;
        }

        private static bool IsKeepSize(Transform t, Transform shopT)
        {
            for (Transform x = t; x != null && x != shopT; x = x.parent)
                if (IsKeepSizeRoot(x) || IsGunGroup(x)) return true;
            return false;
        }

        // Each gun model is a direct child of the package's "Guns" group.
        private static bool IsGunGroup(Transform t) => t.parent != null && t.parent.name == "Guns";

        private static Transform TopChild(Transform t, Transform shopT)
        {
            Transform x = t;
            while (x != null && x.parent != shopT) x = x.parent;
            return x;
        }

        private static bool TryRendererBounds(Transform t, Transform shopT, out Bounds b)
        {
            b = new Bounds();
            bool has = false;
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
            {
                Bounds own = ToPrefab(shopT, r.bounds);
                if (!has) { b = own; has = true; } else b.Encapsulate(own);
            }
            return has;
        }

        private static Vector3 ToPrefab(Transform shopT, Vector3 world) => shopT.InverseTransformPoint(world);

        private static Bounds ToPrefab(Transform shopT, Bounds world)
        {
            var b = new Bounds();
            b.SetMinMax(shopT.InverseTransformPoint(world.min), shopT.InverseTransformPoint(world.max));
            return b;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        private static string Sanitize(string path)
        {
            var sb = new StringBuilder();
            foreach (char c in path) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.ToString();
        }
    }
}
