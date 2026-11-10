using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenworldCrime.Vehicles;
using UnityEditor;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Makes the final cars' own lamps glow through their materials' emission, switched per car by <see cref="VehicleLights"/>.
    /// The car models carry their lamps in two ways:
    /// - lamp materials (Audi e-tron / SQ7, Challenger, Chiron, BMW, police) holding front and rear lamps together, most with an
    ///   emission mask from the source pack (restored here where the URP conversion dropped it);
    /// - single-atlas bodies (G Wagon, Khodge 2, Land Sover, Talenter, WMB) whose lamps are painted into the body texture.
    /// Front and rear lamps have to switch separately (headlights / tail + brake), so for every body mesh this builds a copy
    /// (Assets/_Project/Meshes/Vehicles/Lamps) whose lamp triangles sit in their own submeshes: one for the front lamps, one for
    /// the rear lamps. Lamp-material triangles are split by position (front / rear third of the car); atlas lamps are found
    /// on the car's nose and tail by texture colour (light, unpainted texels at the front corners; red texels at the back) and
    /// get a shared "[body]_Lamps" URP Lit material on the same atlas. Vertex data is unchanged and every existing submesh keeps
    /// its index, so paint variant slots, damage deformation and the door hinge keep working.
    /// Then each prefab gets <see cref="VehicleLights"/> (front submeshes = Headlight, rear = Tail And Brake), its state sources
    /// (iTS AI or RCC, health for the wreck) and, on drivable cars, one shadowless spot light for the player's headlights.
    /// Emission stays off on the assets; VehicleLights swaps in shared lit variants at runtime (SRP Batcher friendly).
    /// Re-runnable: split meshes remember their source mesh (importer user data) and are rebuilt from it.
    /// Menu: Tools > OpenworldCrime > Vehicles > Build Car Lamps (emission). FinalCarBuilder runs it after a rebuild.
    /// </summary>
    public static class VehicleLampBuilder
    {
        private const string MeshFolder = "Assets/_Project/Meshes/Vehicles/Lamps";
        private const string MaterialFolder = "Assets/_Project/Materials/Vehicles/FinalCars/Lamps";
        private const string SourceMaterialFolder = "Assets/Material";
        private static readonly string[] PrefabFolders = { "Assets/_Project/Prefabs/Vehicles/Drivable", "Assets/_Project/Prefabs/Vehicles/Traffic" };

        // Share of the car's length at each end where lamps are looked for.
        private const float LampMaterialZone = 0.34f;
        private const float AtlasZone = 0.17f;
        private const float MaxAtlasLampHeight = 0.24f;

        private static readonly Dictionary<Texture, Pixels> pixelCache = new Dictionary<Texture, Pixels>();
        private static readonly Dictionary<string, Mesh> splitCache = new Dictionary<string, Mesh>();

        private sealed class Pixels
        {
            public Color32[] Data;
            public int Width, Height;

            public Color Sample(Vector2 uv)
            {
                int x = Mathf.Clamp((int)(Mathf.Repeat(uv.x, 1f) * Width), 0, Width - 1);
                int y = Mathf.Clamp((int)(Mathf.Repeat(uv.y, 1f) * Height), 0, Height - 1);
                return Data[y * Width + x];
            }
        }

        private sealed class Split
        {
            public Renderer Renderer;
            public readonly List<int> Head = new List<int>(); // submesh indices of front lamps
            public readonly List<int> Tail = new List<int>(); // submesh indices of rear lamps
        }

        [MenuItem("Tools/OpenworldCrime/Vehicles/Build Car Lamps (emission)")]
        public static void BuildMenu() => Debug.Log(BuildAll());

        /// <summary>Splits the lamps of every final car prefab and wires <see cref="VehicleLights"/>. Returns a per-prefab report.</summary>
        public static string BuildAll()
        {
            EnsureFolder(MeshFolder);
            EnsureFolder(MaterialFolder);
            pixelCache.Clear();
            splitCache.Clear();
            var sb = new StringBuilder("[VehicleLampBuilder]\n");
            try
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", PrefabFolders))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string file = Path.GetFileNameWithoutExtension(path);
                    if (!file.StartsWith("Drivable_") && !file.StartsWith("Traffic_")) continue;
                    if (Path.GetDirectoryName(path).Replace('\\', '/') != PrefabFolders[0] && Path.GetDirectoryName(path).Replace('\\', '/') != PrefabFolders[1]) continue;
                    GameObject root = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        string line = BuildPrefab(root);
                        if (line == null) continue;
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        sb.AppendLine("  " + line);
                    }
                    catch (System.Exception e)
                    {
                        sb.AppendLine($"  {file}: FAILED {e.Message}\n{e.StackTrace}");
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                }
            }
            finally
            {
                foreach (Pixels p in pixelCache.Values) p.Data = null;
                pixelCache.Clear();
                AssetDatabase.SaveAssets();
            }
            return sb.ToString();
        }

        private static string BuildPrefab(GameObject root)
        {
            var rcc = root.GetComponent<RCC_CarControllerV3>();
            var ai = root.GetComponent<TSTrafficAI>();
            if (rcc == null && ai == null) return null;
            Transform rt = root.transform;

            List<MeshRenderer> bodyRenderers = BodyRenderers(rt);
            if (bodyRenderers.Count == 0) return $"{root.name}: no body renderers";
            // Earlier runs: back to the source meshes first, so the bounds and the split start from the original.
            foreach (MeshRenderer r in bodyRenderers) RestoreSource(r);

            Bounds body = BodyBounds(rt, bodyRenderers);
            bool hasLampMaterials = false;
            foreach (MeshRenderer r in bodyRenderers)
                foreach (Material m in r.sharedMaterials)
                    if (IsLampMaterial(m)) hasLampMaterials = true;

            var splits = new List<Split>();
            int headTris = 0, tailTris = 0;
            var headPoints = new List<Vector3>();
            foreach (MeshRenderer r in bodyRenderers)
            {
                Split split = SplitRenderer(r, rt, body, hasLampMaterials, ref headTris, ref tailTris, headPoints);
                if (split.Head.Count > 0 || split.Tail.Count > 0) splits.Add(split);
            }

            VehicleLights lights = root.GetComponent<VehicleLights>();
            if (lights == null) lights = root.AddComponent<VehicleLights>();
            var so = new SerializedObject(lights);
            var slots = new List<(Renderer r, int index, VehicleLightRole role)>();
            foreach (Split s in splits)
            {
                foreach (int index in s.Head) slots.Add((s.Renderer, index, VehicleLightRole.Headlight));
                foreach (int index in s.Tail) slots.Add((s.Renderer, index, VehicleLightRole.TailAndBrake));
            }
            SerializedProperty slotsProp = so.FindProperty("slots");
            slotsProp.arraySize = slots.Count;
            for (int i = 0; i < slots.Count; i++)
            {
                SerializedProperty e = slotsProp.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("renderer").objectReferenceValue = slots[i].r;
                e.FindPropertyRelative("materialIndex").intValue = slots[i].index;
                e.FindPropertyRelative("role").enumValueIndex = (int)slots[i].role;
            }
            so.FindProperty("combinedShowsBrake").boolValue = false;
            so.FindProperty("trafficAI").objectReferenceValue = ai;
            so.FindProperty("rccController").objectReferenceValue = rcc;
            so.FindProperty("drivableAdapter").objectReferenceValue = root.GetComponent<RCCVehicleAdapter>();
            so.FindProperty("health").objectReferenceValue = root.GetComponent<VehicleHealth>();

            // The player's headlights: one shadowless spot light (drivable cars only; VehicleLights keeps it off on every other car).
            var realtime = new List<Light>();
            Transform oldSpot = rt.Find("HeadlightSpot");
            if (rcc != null)
            {
                Light spot = oldSpot != null ? oldSpot.GetComponent<Light>() : null;
                if (spot == null)
                {
                    var go = new GameObject("HeadlightSpot");
                    go.transform.SetParent(rt, false);
                    spot = go.AddComponent<Light>();
                }
                Vector3 lamp = new Vector3(0f, Mathf.Clamp(body.size.y * 0.42f, 0.55f, 1.0f), body.max.z - 0.15f);
                if (headPoints.Count > 0)
                {
                    float y = 0f;
                    foreach (Vector3 p in headPoints) y += p.y;
                    lamp.y = y / headPoints.Count;
                }
                spot.transform.localPosition = lamp;
                spot.transform.localRotation = Quaternion.Euler(7f, 0f, 0f);
                spot.type = LightType.Spot;
                spot.color = new Color(1f, 0.95f, 0.86f);
                spot.intensity = 6f;
                spot.range = 32f;
                spot.spotAngle = 80f;
                spot.innerSpotAngle = 45f;
                spot.shadows = LightShadows.None;
                spot.renderMode = LightRenderMode.ForcePixel;
                spot.lightmapBakeType = LightmapBakeType.Realtime;
                spot.enabled = false;
                realtime.Add(spot);
            }
            else if (oldSpot != null) Object.DestroyImmediate(oldSpot.gameObject);
            SetArray(so.FindProperty("realtimeHeadlights"), realtime);
            so.ApplyModifiedPropertiesWithoutUndo();

            return $"{root.name}: {splits.Count} renderers, {headTris} front / {tailTris} rear lamp triangles" +
                   (hasLampMaterials ? " (lamp materials)" : " (atlas)") + (rcc != null ? ", headlight spot" : "");
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Renderers and bounds
        // ---------------------------------------------------------------------------------------------------------------

        private static List<MeshRenderer> BodyRenderers(Transform rt)
        {
            var list = new List<MeshRenderer>();
            foreach (MeshRenderer r in rt.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (IsExcluded(r.transform, rt)) continue;
                var filter = r.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                list.Add(r);
            }
            return list;
        }

        private static bool IsExcluded(Transform t, Transform root)
        {
            for (Transform p = t; p != null && p != root; p = p.parent)
            {
                string n = p.name.ToLowerInvariant();
                if (n.Contains("wheel") || n.Contains("tire") || n.Contains("driverseat") || n.Contains("driver") || n == "lights"
                    || n.Contains("brakelight") || n.Contains("halolight") || n.Contains("exhaust") || n.Contains("siren")) return true;
            }
            return false;
        }

        private static Bounds BodyBounds(Transform rt, List<MeshRenderer> renderers)
        {
            bool init = false;
            var b = new Bounds();
            foreach (MeshRenderer r in renderers)
            {
                Mesh mesh = r.GetComponent<MeshFilter>().sharedMesh;
                Bounds local = mesh.bounds;
                Vector3 c = local.center, e = local.extents;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = c + Vector3.Scale(e, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = rt.InverseTransformPoint(r.transform.TransformPoint(corner));
                    if (!init) { b = new Bounds(p, Vector3.zero); init = true; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Materials
        // ---------------------------------------------------------------------------------------------------------------

        private static bool IsLampMaterial(Material m)
        {
            if (m == null) return false;
            string n = m.name.ToLowerInvariant();
            if (!n.Contains("light")) return false;
            // iTS brake glow sprites, lamp housings / lenses, the police light bar's flash quads.
            return !(n.Contains("brakelights") || n.Contains("plastic") || n.Contains("glass") || n.Contains("flash") || n.Contains("bulb"));
        }

        private static bool IsAtlasCandidate(Material m)
        {
            if (m == null) return false;
            string n = m.name.ToLowerInvariant();
            if (n.Contains("glass") || n.Contains("wind") || n.Contains("window") || n.Contains("wheel") || n.Contains("brake")
                || n.Contains("plate") || n.Contains("mirror") || n.Contains("interior") || n.Contains("rubber")) return false;
            return m.renderQueue < 2500 && m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null;
        }

        /// <summary>
        /// Lamp materials keep their source pack's emission mask (the URP conversion dropped it where the source had emission at
        /// black), with emission switched off on the asset: VehicleLights turns it on per car.
        /// </summary>
        private static void PrepareLampMaterial(Material m)
        {
            if (m == null || AssetDatabase.GetAssetPath(m).StartsWith("Assets/_Project/") == false) return;
            if (m.HasProperty("_EmissionMap") && m.GetTexture("_EmissionMap") == null)
            {
                Material source = AssetDatabase.LoadAssetAtPath<Material>($"{SourceMaterialFolder}/{m.name}.mat");
                Texture mask = source != null && source.HasProperty("_EmissionMap") ? source.GetTexture("_EmissionMap") : null;
                if (mask != null) m.SetTexture("_EmissionMap", mask);
            }
            m.SetColor("_EmissionColor", Color.black);
            m.DisableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            EditorUtility.SetDirty(m);
        }

        // "[atlas material]_Lamps": URP Lit on the same atlas, unpainted, emission off (the atlas itself is the emission map).
        private static Material AtlasLampMaterial(Material body)
        {
            string path = $"{MaterialFolder}/{Sanitize(body.name)}_Lamps.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = body.name + "_Lamps" };
                AssetDatabase.CreateAsset(m, path);
            }
            Texture atlas = body.GetTexture("_BaseMap");
            m.SetTexture("_BaseMap", atlas);
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_EmissionMap", atlas);
            m.SetColor("_EmissionColor", Color.black);
            m.DisableKeyword("_EMISSION");
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", 0.85f);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Mesh split
        // ---------------------------------------------------------------------------------------------------------------

        private static Split SplitRenderer(MeshRenderer r, Transform rt, Bounds body, bool lampMaterials, ref int headTris, ref int tailTris, List<Vector3> headPoints)
        {
            var result = new Split { Renderer = r };
            MeshFilter filter = r.GetComponent<MeshFilter>();
            Mesh mesh = filter.sharedMesh;
            Material[] mats = r.sharedMaterials;
            int subCount = Mathf.Min(mesh.subMeshCount, mats.Length);
            Vector3[] verts = mesh.vertices;
            Vector2[] uvs = mesh.uv;
            Matrix4x4 toRoot = rt.worldToLocalMatrix * r.transform.localToWorldMatrix;
            float length = body.size.z;

            var keep = new List<int>[subCount];
            var head = new List<int>[subCount];
            var tail = new List<int>[subCount];
            Material atlasLamp = null;
            bool any = false;
            for (int s = 0; s < subCount; s++)
            {
                keep[s] = new List<int>();
                head[s] = new List<int>();
                tail[s] = new List<int>();
                Material m = mats[s];
                bool lamp = IsLampMaterial(m);
                bool atlas = !lampMaterials && IsAtlasCandidate(m) && uvs != null && uvs.Length == verts.Length;
                int[] tris = mesh.GetTriangles(s);
                if (!lamp && !atlas)
                {
                    keep[s].AddRange(tris);
                    continue;
                }
                if (lamp) PrepareLampMaterial(m);
                Pixels albedo = atlas ? Read(m.GetTexture("_BaseMap")) : null;
                Pixels paintMask = atlas && m.HasProperty("_PaintMask") ? Read(m.GetTexture("_PaintMask")) : null;
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    Vector3 a = toRoot.MultiplyPoint3x4(verts[tris[t]]);
                    Vector3 b = toRoot.MultiplyPoint3x4(verts[tris[t + 1]]);
                    Vector3 c = toRoot.MultiplyPoint3x4(verts[tris[t + 2]]);
                    Vector3 centre = (a + b + c) / 3f;
                    int side = 0; // 1 front, -1 rear
                    if (lamp)
                    {
                        if (centre.z > body.max.z - length * LampMaterialZone) side = 1;
                        else if (centre.z < body.min.z + length * LampMaterialZone) side = -1;
                    }
                    else
                    {
                        Vector3 normal = Vector3.Cross(b - a, c - a);
                        if (normal.sqrMagnitude > 1e-12f)
                        {
                            normal.Normalize();
                            // Lamps are small: tall parts in front of them (bull bars, grille slats) are not lamps.
                            float tall = Mathf.Max(a.y, Mathf.Max(b.y, c.y)) - Mathf.Min(a.y, Mathf.Min(b.y, c.y));
                            if (tall < MaxAtlasLampHeight)
                                side = AtlasLampSide(centre, normal, body, albedo, paintMask, uvs[tris[t]], uvs[tris[t + 1]], uvs[tris[t + 2]]);
                        }
                    }
                    List<int> target = side > 0 ? head[s] : side < 0 ? tail[s] : keep[s];
                    target.Add(tris[t]); target.Add(tris[t + 1]); target.Add(tris[t + 2]);
                    if (side > 0) headPoints.Add(centre);
                }
                if (head[s].Count > 0 || tail[s].Count > 0)
                {
                    any = true;
                    if (atlas && atlasLamp == null) atlasLamp = AtlasLampMaterial(m);
                }
            }
            if (!any) return result;

            // Layout: existing submeshes keep their index (with the lamp triangles taken out), then front lamps, then rear lamps,
            // one pair per lamp material (atlas lamps share one material).
            var newMats = new List<Material>(mats);
            var subs = new List<List<int>>();
            for (int s = 0; s < subCount; s++) subs.Add(keep[s]);
            List<int> headIndices = result.Head;
            List<int> tailIndices = result.Tail;
            if (atlasLamp != null)
            {
                var h = new List<int>(); var tl = new List<int>();
                for (int s = 0; s < subCount; s++) if (!IsLampMaterial(mats[s])) { h.AddRange(head[s]); tl.AddRange(tail[s]); }
                if (h.Count > 0) { headIndices.Add(subs.Count); subs.Add(h); newMats.Add(atlasLamp); }
                if (tl.Count > 0) { tailIndices.Add(subs.Count); subs.Add(tl); newMats.Add(atlasLamp); }
            }
            for (int s = 0; s < subCount; s++)
            {
                if (!IsLampMaterial(mats[s])) continue;
                if (head[s].Count > 0) { headIndices.Add(subs.Count); subs.Add(head[s]); newMats.Add(mats[s]); }
                if (tail[s].Count > 0) { tailIndices.Add(subs.Count); subs.Add(tail[s]); newMats.Add(mats[s]); }
            }
            for (int i = 0; i < headIndices.Count; i++) headTris += subs[headIndices[i]].Count / 3;
            for (int i = 0; i < tailIndices.Count; i++) tailTris += subs[tailIndices[i]].Count / 3;

            // Same source mesh split the same way (drivable and traffic twin) = one shared asset.
            string signature = Signature(subs);
            string cacheKey = AssetDatabase.GetAssetPath(mesh) + "|" + mesh.name + "|" + signature;
            if (!splitCache.TryGetValue(cacheKey, out Mesh split))
            {
                split = SaveSplitMesh(mesh, subs, signature);
                splitCache[cacheKey] = split;
            }
            filter.sharedMesh = split;
            r.sharedMaterials = newMats.ToArray();
            return result;
        }

        // Atlas lamps: on the nose or tail, facing out, between bumper and bonnet height, on unpainted texels that look like a
        // lamp - at the front corners anything unpainted that is not black trim (lamp housings are grey glass with light or
        // coloured elements; the centre is grille and plate), at the back red.
        private static int AtlasLampSide(Vector3 centre, Vector3 normal, Bounds body, Pixels albedo, Pixels paint, Vector2 uvA, Vector2 uvB, Vector2 uvC)
        {
            float length = body.size.z;
            bool front = centre.z > body.max.z - length * AtlasZone && normal.z > 0.1f;
            bool rear = centre.z < body.min.z + length * AtlasZone && normal.z < -0.3f;
            if (!front && !rear) return 0;
            float height = (centre.y - body.min.y) / Mathf.Max(0.1f, body.size.y);
            if (front && (height < 0.3f || height > 0.72f)) return 0;
            if (rear && (height < 0.22f || height > 0.8f)) return 0;
            if (front && Mathf.Abs(centre.x - body.center.x) < body.extents.x * 0.45f) return 0;
            if (albedo == null) return 0;

            int pass = 0;
            Vector2 uvM = (uvA + uvB + uvC) / 3f;
            Vector2[] samples = { uvM, (uvA + uvM) * 0.5f, (uvB + uvM) * 0.5f, (uvC + uvM) * 0.5f };
            foreach (Vector2 uv in samples)
            {
                if (paint != null && paint.Sample(uv).r > 0.4f) continue;
                Color c = albedo.Sample(uv);
                float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                float saturation = max > 0.001f ? (max - min) / max : 0f;
                if (front && max > 0.16f && (saturation < 0.35f || c.r > c.b)) pass++;
                else if (rear && c.r > 0.18f && c.r > c.g * 1.6f && c.r > c.b * 1.6f) pass++;
            }
            // Red lamp texels are often thin strips on dark trim: half the samples are enough at the back.
            return front ? (pass >= 3 ? 1 : 0) : (pass >= 2 ? -1 : 0);
        }

        // Short content hash of a submesh layout (FNV-1a over the index lists).
        private static string Signature(List<List<int>> subs)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (List<int> sub in subs)
                {
                    hash = (hash ^ (uint)sub.Count) * 16777619;
                    foreach (int i in sub) hash = (hash ^ (uint)i) * 16777619;
                }
                return hash.ToString("X8");
            }
        }

        private static Mesh SaveSplitMesh(Mesh source, List<List<int>> subs, string signature)
        {
            Mesh split = Object.Instantiate(source);
            split.name = source.name + "_Lamps";
            split.subMeshCount = subs.Count;
            for (int s = 0; s < subs.Count; s++) split.SetTriangles(subs[s], s, false);
            split.RecalculateBounds();
            split.UploadMeshData(false);

            string sourcePath = AssetDatabase.GetAssetPath(source);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId);
            string path = $"{MeshFolder}/{Sanitize(Path.GetFileNameWithoutExtension(sourcePath))}_{Sanitize(source.name)}_{signature}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(split, existing);
                Object.DestroyImmediate(split);
                split = existing;
                EditorUtility.SetDirty(split);
            }
            else AssetDatabase.CreateAsset(split, path);
            AssetImporter importer = AssetImporter.GetAtPath(path);
            if (importer != null)
            {
                importer.userData = $"source={guid}:{localId}";
                importer.SaveAndReimport();
                split = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            }
            return split;
        }

        // A renderer showing a split mesh from an earlier run gets its source mesh and source materials back.
        private static void RestoreSource(MeshRenderer r)
        {
            MeshFilter filter = r.GetComponent<MeshFilter>();
            Mesh mesh = filter.sharedMesh;
            string path = AssetDatabase.GetAssetPath(mesh);
            if (!path.StartsWith(MeshFolder + "/")) return;
            AssetImporter importer = AssetImporter.GetAtPath(path);
            string data = importer != null ? importer.userData : null;
            if (string.IsNullOrEmpty(data) || !data.StartsWith("source=")) return;
            string[] parts = data.Substring(7).Split(':');
            if (parts.Length != 2 || !long.TryParse(parts[1], out long localId)) return;
            string sourcePath = AssetDatabase.GUIDToAssetPath(parts[0]);
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(sourcePath))
            {
                if (!(o is Mesh source)) continue;
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string _, out long id) || id != localId) continue;
                Material[] mats = r.sharedMaterials;
                int count = Mathf.Min(source.subMeshCount, mats.Length);
                var restored = new Material[count];
                for (int i = 0; i < count; i++) restored[i] = mats[i];
                filter.sharedMesh = source;
                r.sharedMaterials = restored;
                return;
            }
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------------------------------------------------

        private static Pixels Read(Texture texture)
        {
            if (texture == null) return null;
            if (pixelCache.TryGetValue(texture, out Pixels cached)) return cached;
            int w = Mathf.Min(texture.width, 1024), h = Mathf.Min(texture.height, 1024);
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(texture, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            var pixels = new Pixels { Data = copy.GetPixels32(), Width = w, Height = h };
            Object.DestroyImmediate(copy);
            pixelCache[texture] = pixels;
            return pixels;
        }

        private static void SetArray(SerializedProperty prop, List<Light> items)
        {
            prop.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }

        private static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Replace(' ', '_');
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
