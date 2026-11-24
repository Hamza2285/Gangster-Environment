using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenworldCrime.Interiors;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// The shop marker (<see cref="EntryMarker"/>): builds its prefab (ring, light column and arrow: meshes, textures and URP
    /// Unlit transparent materials under Assets/_Project/Art/Markers) and places one on the ground at every shop door in the
    /// city and at the spots inside the shops where the player stands to use something (clothes fitting spot, gun counter,
    /// shooting lanes, the way out). Gun shop / club doors had a plain column and ring: those are switched off for this one.
    /// Rerunnable: each interactable keeps a single marker. Menu: Tools > OpenworldCrime > Interiors > Place Entry Markers.
    /// </summary>
    public static class EntryMarkerBuilder
    {
        private const string ArtFolder = "Assets/_Project/Art/Markers";
        private const string PrefabPath = "Assets/_Project/Prefabs/Interiors/EntryMarker.prefab";
        private const string MarkerName = "Entry Marker";
        public static readonly Color Pink = new Color(1f, 0.32f, 0.78f, 1f);

        // The scenes that hold shops: the city and every interior.
        private static readonly string[] Scenes =
        {
            "Assets/_Project/Scenes/Gangstar_Gameplay.unity",
            "Assets/Scenes/Clothing Store Interior.unity",
            "Assets/_Project/Scenes/GunShop_Interior.unity",
            "Assets/_Project/Scenes/Club_Interior.unity",
            "Assets/Scenes/Car Showroom Interior.unity",
        };

        // Interactable types that get a marker, and its size (doors full size, spots inside a little smaller).
        private static readonly Dictionary<string, float> Sizes = new Dictionary<string, float>
        {
            { "ShopEntrance", 1f }, { "ClothingStoreEntrance", 1f }, { "ShowroomEntrance", 1f },
            { "ClothingStoreFittingSpot", 0.8f }, { "ShopCounter", 0.75f }, { "RangeLane", 0.7f },
            { "InteriorExitDoor", 0.8f }, { "ClothingStoreExit", 0.8f }, { "ShowroomExit", 0.8f },
        };

        [MenuItem("Tools/OpenworldCrime/Interiors/Place Entry Markers")]
        public static void Menu() => Debug.Log(PlaceEverywhere());

        /// <summary>Builds the prefab and places markers in every shop scene (each is opened, marked and saved).</summary>
        public static string PlaceEverywhere()
        {
            var log = new StringBuilder("[EntryMarkerBuilder]\n");
            GameObject prefab = BuildPrefab(log);
            string before = SceneManager.GetActiveScene().path;
            foreach (string path in Scenes)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) { log.AppendLine("  no scene " + path); continue; }
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                log.AppendLine($"  {Path.GetFileNameWithoutExtension(path)}: {PlaceInScene(prefab)}");
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            if (!string.IsNullOrEmpty(before)) EditorSceneManager.OpenScene(before, OpenSceneMode.Single);
            return log.ToString();
        }

        /// <summary>Rebuilds only the marker's art and prefab; placed markers (prefab instances) pick it up.</summary>
        public static string RebuildArt()
        {
            var log = new StringBuilder("[EntryMarkerBuilder]\n");
            BuildPrefab(log);
            return log.ToString();
        }

        public static string PlaceInScene(GameObject prefab)
        {
            int placed = 0, oldHidden = 0;
            var names = new List<string>();
            foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (behaviour == null || !Sizes.TryGetValue(behaviour.GetType().Name, out float size)) continue;
                Transform owner = behaviour.transform;

                // The old plain marker (column + bobbing ring) on gun shop and club doors.
                foreach (string old in new[] { "Marker Column", "Marker Ring", "Fitting Spot Ring" })
                {
                    var t = owner.Find(old);
                    if (t != null && t.gameObject.activeSelf) { t.gameObject.SetActive(false); oldHidden++; }
                }

                var existing = owner.Find(MarkerName);
                if (existing != null) Object.DestroyImmediate(existing.gameObject);
                if (!owner.gameObject.activeInHierarchy) continue;   // switched-off doors (not a shop any more) get none
                var marker = (GameObject)PrefabUtility.InstantiatePrefab(prefab, owner.gameObject.scene);
                marker.name = MarkerName;
                marker.transform.SetParent(owner, false);
                marker.transform.position = GroundBelow(behaviour);
                marker.transform.rotation = Quaternion.identity;
                Vector3 parentScale = owner.lossyScale;
                marker.transform.localScale = new Vector3(size / Mathf.Max(0.01f, parentScale.x), size / Mathf.Max(0.01f, parentScale.y), size / Mathf.Max(0.01f, parentScale.z));
                // Gun shop doors: the floating arrow becomes the gun shop's map icon.
                if (behaviour is ShopEntrance shop && shop.InteriorSceneName == "GunShop_Interior") UseGunIcon(marker.transform);
                placed++;
                names.Add(owner.name);
            }
            return $"{placed} markers ({oldHidden} old marker parts hidden){(names.Count > 0 ? ": " + string.Join(", ", names) : "")}";
        }

        private const string GunIconPath = "Assets/Game/UI/MapIcons/MapIcon_Gun.png";

        /// <summary>Swaps a placed marker's arrow for the gun shop icon (same billboard, a little bigger).</summary>
        public static bool UseGunIcon(Transform marker)
        {
            var arrow = marker.Find("Arrow");
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(GunIconPath);
            if (arrow == null || texture == null) return false;
            string path = ArtFolder + "/MarkerGunIcon.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(AssetDatabase.LoadAssetAtPath<Material>(ArtFolder + "/MarkerArrow.mat"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);   // the icon keeps its own neon colours
            EditorUtility.SetDirty(material);
            arrow.GetComponent<MeshRenderer>().sharedMaterial = material;
            arrow.localScale = new Vector3(1.05f, 1.05f, 1f);
            return true;
        }

        // The floor under the interactable's trigger (its centre), ignoring triggers and the player.
        private static Vector3 GroundBelow(MonoBehaviour behaviour)
        {
            Vector3 from = behaviour.transform.position;
            var col = behaviour.GetComponent<Collider>();
            if (col != null) from = col.bounds.center;
            var hits = Physics.RaycastAll(from + Vector3.up * 1.5f, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 ground = new Vector3(from.x, from.y - (col != null ? col.bounds.extents.y : 0f), from.z);
            foreach (var hit in hits)
            {
                if (hit.collider.transform.IsChildOf(behaviour.transform)) continue;
                if (hit.distance < best) { best = hit.distance; ground = hit.point; }
            }
            return ground + Vector3.up * 0.03f;
        }

        // ---------- The prefab ----------

        private static GameObject BuildPrefab(StringBuilder log)
        {
            EnsureFolder(ArtFolder);
            EnsureFolder("Assets/_Project/Prefabs/Interiors");
            Texture2D ringTex = SaveTexture("MarkerRing.png", RingTexture());
            Texture2D columnTex = SaveTexture("MarkerColumn.png", ColumnTexture());
            Texture2D arrowTex = SaveTexture("MarkerArrow.png", ArrowTexture());
            Material ringMat = SaveMaterial("MarkerRing.mat", ringTex, new Color(Pink.r, Pink.g, Pink.b, 0.95f));
            Material columnMat = SaveMaterial("MarkerColumn.mat", columnTex, new Color(Pink.r, Pink.g, Pink.b, 0.55f));
            Material arrowMat = SaveMaterial("MarkerArrow.mat", arrowTex, new Color(Pink.r, Pink.g, Pink.b, 1f));
            Mesh quad = SaveMesh("MarkerQuad.asset", QuadMesh());
            Mesh tube = SaveMesh("MarkerColumn.asset", TubeMesh(0.85f, 1.7f, 32));

            var root = new GameObject(MarkerName);
            var ring = Part("Ring", root.transform, quad, ringMat);
            ring.localScale = new Vector3(2.3f, 2.3f, 1f);
            ring.localPosition = new Vector3(0f, 0.02f, 0f);
            ring.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var column = Part("Column", root.transform, tube, columnMat);
            var arrow = Part("Arrow", root.transform, quad, arrowMat);
            arrow.localScale = new Vector3(0.75f, 0.75f, 1f);
            arrow.localPosition = new Vector3(0f, 2.15f, 0f);

            var marker = root.AddComponent<EntryMarker>();
            var so = new SerializedObject(marker);
            so.FindProperty("ring").objectReferenceValue = ring;
            so.FindProperty("column").objectReferenceValue = column;
            so.FindProperty("arrow").objectReferenceValue = arrow;
            so.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            log.AppendLine("  prefab " + PrefabPath);
            return prefab;
        }

        private static Transform Part(string name, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return go.transform;
        }

        // Soft glowing ring with a faint fill inside it.
        private static Texture2D RingTexture()
        {
            const int s = 256;
            var t = new Texture2D(s, s, TextureFormat.RGBA32, true);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(s * 0.5f, s * 0.5f)) / (s * 0.5f);
                    float ring = Mathf.Exp(-Mathf.Pow((d - 0.82f) / 0.05f, 2f));
                    float halo = Mathf.Exp(-Mathf.Pow((d - 0.82f) / 0.14f, 2f)) * 0.45f;
                    float inner = Mathf.Exp(-Mathf.Pow((d - 0.55f) / 0.03f, 2f)) * 0.5f;
                    float fill = d < 0.8f ? 0.14f * (1f - d) : 0f;
                    float a = Mathf.Clamp01(Mathf.Max(ring, halo) + inner + fill) * (d < 1f ? 1f : 0f);
                    float white = Mathf.Clamp01(ring * 0.6f);
                    t.SetPixel(x, y, new Color(Mathf.Lerp(1f, 1f, white), Mathf.Lerp(0.85f, 1f, white), Mathf.Lerp(0.95f, 1f, white), a));
                }
            t.Apply();
            return t;
        }

        // Vertical fade: strongest at the floor, gone at the top, with faint vertical streaks.
        private static Texture2D ColumnTexture()
        {
            const int w = 64, h = 128;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat };
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float v = (y + 0.5f) / h;
                    float fade = Mathf.Pow(1f - v, 1.6f);
                    float streak = 0.8f + 0.2f * Mathf.Sin(x / (float)w * Mathf.PI * 2f * 6f);
                    float baseGlow = Mathf.Exp(-v * 18f) * 0.6f;
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(fade * streak * 0.8f + baseGlow)));
                }
            t.Apply();
            return t;
        }

        // A thick downward chevron with a light outline.
        private static Texture2D ArrowTexture()
        {
            const int s = 128;
            var t = new Texture2D(s, s, TextureFormat.RGBA32, true);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    Vector2 p = new Vector2((x + 0.5f) / s - 0.5f, (y + 0.5f) / s - 0.5f);   // -0.5..0.5, y up
                    // Down-pointing chevron: distance band around the V  |x| = (y + 0.3)
                    float vDist = Mathf.Abs(Mathf.Abs(p.x) - (p.y + 0.28f)) / 1.41f;
                    bool inside = p.y > -0.3f && p.y < 0.32f && Mathf.Abs(p.x) < 0.46f;
                    float body = inside ? 1f - Smooth(0.07f, 0.09f, vDist) : 0f;
                    float edge = inside ? 1f - Smooth(0.09f, 0.13f, vDist) : 0f;
                    float a = Mathf.Max(body, edge * 0.55f);
                    float white = body > 0.5f ? 0.15f : 1f;   // pale outline, pink fill (tinted by the material)
                    t.SetPixel(x, y, new Color(1f, Mathf.Lerp(1f, 1f, white), 1f, a));
                }
            t.Apply();
            return t;
        }

        // Shader-style smoothstep: 0 below e0, 1 above e1, smooth between. (Mathf.SmoothStep interpolates its first two arguments.)
        private static float Smooth(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        // A unit quad in XY facing -Z (the side the arrow script turns toward the camera).
        private static Mesh QuadMesh()
        {
            var m = new Mesh { name = "MarkerQuad" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // An open tube (no caps), v = 0 at the floor, 1 at the top.
        private static Mesh TubeMesh(float radius, float height, int sides)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int i = 0; i <= sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                verts.Add(dir * radius);
                verts.Add(dir * radius + Vector3.up * height);
                uvs.Add(new Vector2(i / (float)sides, 0f));
                uvs.Add(new Vector2(i / (float)sides, 1f));
                if (i < sides)
                {
                    int b = i * 2;
                    tris.AddRange(new[] { b, b + 1, b + 3, b, b + 3, b + 2 });
                }
            }
            var m = new Mesh { name = "MarkerColumn" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private static Texture2D SaveTexture(string file, Texture2D texture)
        {
            string path = ArtFolder + "/" + file;
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency = true;
            importer.wrapMode = file.Contains("Column") ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // URP Unlit, transparent (alpha blend), both faces, no depth write: glows through itself without sorting artefacts.
        private static Material SaveMaterial(string file, Texture2D texture, Color colour)
        {
            string path = ArtFolder + "/" + file;
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_Cull", 0f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetOverrideTag("RenderType", "Transparent");
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Mesh SaveMesh(string file, Mesh mesh)
        {
            string path = ArtFolder + "/" + file;
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); return existing; }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
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
