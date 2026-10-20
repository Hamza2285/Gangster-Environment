using System.IO;
using OpenworldCrime.Vehicles;
using UnityEditor;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Prepares the iTS traffic cars for random paints (<see cref="TrafficCarPaint"/>). The car models have their paint baked
    /// into one albedo atlas together with interior, trim and lights, so for every model this tool:
    /// 1. keys the body-paint pixels of the atlas by colour and writes a new texture whose paint areas are neutral 0.5 grey
    ///    (keeping the panel shading) with the paint mask in alpha; all other pixels keep their exact original look;
    /// 2. creates a "paint-ready" copy of the body material (URP Lit, detail x2 with the mask) using that texture;
    /// 3. adds a TrafficCarPaint to the traffic prefab and wires palette, body renderer, slot and paint-ready material.
    /// Source textures, source materials and the iTS core are never modified. Re-running overwrites only the files it made
    /// (GUIDs kept) and never overwrites the palette asset, so designer edits to the paints survive.
    /// </summary>
    public static class TrafficPaintBaker
    {
        private const string OutputFolder = "Assets/_Project/Materials/Vehicles/TrafficPaint";
        private const string PaletteFolder = "Assets/_Project/Data/Vehicles";
        private const string PalettePath = PaletteFolder + "/TrafficPaintPalette.asset";
        private const string NeutralDetailPath = OutputFolder + "/TrafficPaint_NeutralDetail.png";
        private const string BodyRendererPath = "Center/Model/MergedBody";

        private struct Entry
        {
            public string prefab;       // traffic prefab
            public string material;     // original body material (on MergedBody)
            public bool whiteKey;       // body is white/neutral in the atlas
            public float hue;           // body hue in degrees (coloured keys)
            public float hueTolerance;  // degrees
            public float minSaturation;
            public float keyValue;      // HSV value of a fully lit body pixel

            public Entry(string prefab, string material, float hue, float hueTolerance, float minSaturation, float keyValue)
            {
                this.prefab = prefab; this.material = material; whiteKey = false;
                this.hue = hue; this.hueTolerance = hueTolerance; this.minSaturation = minSaturation; this.keyValue = keyValue;
            }

            public static Entry White(string prefab, string material, float keyValue)
            {
                return new Entry(prefab, material, 0f, 0f, 0f, keyValue) { whiteKey = true };
            }
        }

        private const string P = "Assets/_Project/Prefabs/Vehicles/Traffic/";
        private const string M = "Assets/_Project/Materials/Vehicles/NewCars/";

        // Body key colours measured from each atlas (dominant paint colour).
        private static readonly Entry[] Entries =
        {
            new Entry(P + "Traffic_AudiEtron.prefab", M + "Audi e-tron GT_URP.mat", 120f, 25f, 0.10f, 0.35f),
            new Entry(P + "Traffic_Challenger.prefab", M + "Challenger_URP.mat", 40f, 8f, 0.60f, 0.97f),
            new Entry(P + "Traffic_Chiron.prefab", M + "BugattiChiron_URP.mat", 196f, 12f, 0.60f, 0.72f),
            new Entry(P + "Traffic_BMW7.prefab", M + "BMW 7 series G12 2020_URP.mat", 200f, 14f, 0.20f, 0.53f),
            new Entry(P + "Traffic_AudiSQ7.prefab", M + "Audi SQ7_URP.mat", 224f, 10f, 0.60f, 0.72f),
            Entry.White(P + "Traffic_GWagon.prefab", M + "U Wagor_URP.mat", 0.91f),
            Entry.White(P + "Traffic_Khodge.prefab", M + "Khodge 2_URP.mat", 0.97f),
            new Entry(P + "Traffic_LandSover.prefab", M + "Land Sover_URP.mat", 222f, 14f, 0.50f, 0.47f),
            new Entry(P + "Traffic_Talenter.prefab", M + "Talenter_URP.mat", 28f, 8f, 0.60f, 0.97f),
            new Entry(P + "Traffic_WMB.prefab", M + "WMB_URP.mat", 5f, 9f, 0.60f, 0.97f),
        };

        [MenuItem("Tools/OpenworldCrime/Traffic/Bake Paint-Ready Materials")]
        public static void BakeAll()
        {
            EnsureFolder(OutputFolder);
            EnsureFolder(PaletteFolder);
            TrafficPaintPalette palette = EnsurePalette();
            Texture2D neutralDetail = EnsureNeutralDetail();

            foreach (Entry e in Entries)
            {
                var source = AssetDatabase.LoadAssetAtPath<Material>(e.material);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(e.prefab);
                if (source == null || prefab == null)
                {
                    Debug.LogError($"[TrafficPaintBaker] Missing material '{e.material}' or prefab '{e.prefab}'. Skipped.");
                    continue;
                }
                Material paintReady = BakeMaterial(e, source, neutralDetail, out float coverage);
                if (paintReady == null) continue;
                bool wired = WirePrefab(e, source, paintReady, palette);
                Debug.Log($"[TrafficPaintBaker] {prefab.name}: paint mask {coverage:P0} of atlas, prefab wired = {wired}.");
            }
            AssetDatabase.SaveAssets();
        }

        private static Material BakeMaterial(Entry e, Material source, Texture2D neutralDetail, out float coverage)
        {
            coverage = 0f;
            var baseMap = source.GetTexture("_BaseMap") as Texture2D;
            string texPath = baseMap != null ? AssetDatabase.GetAssetPath(baseMap) : null;
            if (string.IsNullOrEmpty(texPath) || !File.Exists(texPath))
            {
                Debug.LogError($"[TrafficPaintBaker] '{source.name}' has no readable _BaseMap file. Skipped.", source);
                return null;
            }

            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            src.LoadImage(File.ReadAllBytes(texPath));
            Color tint = source.GetColor("_BaseColor");
            Color[] px = src.GetPixels();
            int masked = 0;
            for (int i = 0; i < px.Length; i++)
            {
                Color c = px[i];
                Color.RGBToHSV(c, out float h, out float s, out float v);
                float mask = e.whiteKey ? WhiteMask(s, v) : HueMask(e, h * 360f, s, v);
                // Project colour space is Gamma, so _BaseColor multiplies the texture value directly: baking the original
                // tint into non-paint pixels keeps them identical once the paint-ready material uses a white _BaseColor.
                Color original = new Color(c.r * tint.r, c.g * tint.g, c.b * tint.b, 1f);
                float grey = Mathf.Clamp01(0.5f * v / e.keyValue);
                Color neutral = new Color(grey, grey, grey, 1f);
                Color o = Color.Lerp(original, neutral, mask);
                o.a = mask;
                px[i] = o;
                if (mask > 0.5f) masked++;
            }
            coverage = (float)masked / px.Length;
            src.SetPixels(px);
            src.Apply(false);

            string baseName = Sanitize(source.name.Replace("_URP", "")) + "_PaintReady";
            string outTex = $"{OutputFolder}/{baseName}.png";
            File.WriteAllBytes(outTex, src.EncodeToPNG());
            Object.DestroyImmediate(src);
            AssetDatabase.ImportAsset(outTex, ImportAssetOptions.ForceUpdate);
            var srcImporter = AssetImporter.GetAtPath(texPath) as TextureImporter;
            if (AssetImporter.GetAtPath(outTex) is TextureImporter imp)
            {
                imp.textureType = TextureImporterType.Default;
                imp.sRGBTexture = true;
                imp.alphaSource = TextureImporterAlphaSource.FromInput;
                imp.alphaIsTransparency = false;
                imp.mipmapEnabled = true;
                if (srcImporter != null) imp.maxTextureSize = srcImporter.maxTextureSize;
                imp.SaveAndReimport();
            }
            var bakedTex = AssetDatabase.LoadAssetAtPath<Texture2D>(outTex);

            var mat = new Material(source) { name = baseName };
            mat.SetTexture("_BaseMap", bakedTex);
            mat.SetTexture("_MainTex", bakedTex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetColor("_Color", Color.white);
            mat.SetTexture("_DetailMask", bakedTex);
            // A neutral (0.5) detail texture keeps URP's material GUI from dropping the _DETAIL_MULX2 keyword; at runtime
            // TrafficCarPaint replaces it with the paint colour.
            mat.SetTexture("_DetailAlbedoMap", neutralDetail);
            mat.SetTextureScale("_DetailAlbedoMap", Vector2.one);
            mat.SetTextureOffset("_DetailAlbedoMap", Vector2.zero);
            mat.SetFloat("_DetailAlbedoMapScale", 1f);
            mat.EnableKeyword("_DETAIL_MULX2");
            mat.DisableKeyword("_DETAIL_SCALED");
            // Production car paint (mask-limited clear coat, paint/trim metallic and smoothness) when the shader is in the project;
            // TrafficCarPaint sets its _PaintColor instead of the detail texture.
            Shader paintShader = Shader.Find("OpenworldCrime/Vehicle Paint");
            if (paintShader != null)
            {
                float metallic = mat.GetFloat("_Metallic"), smoothness = mat.GetFloat("_Smoothness");
                Texture normal = mat.GetTexture("_BumpMap");
                mat.shader = paintShader;
                mat.shaderKeywords = new string[0];
                mat.SetTexture("_BaseMap", bakedTex);
                mat.SetTexture("_BumpMap", normal);
                mat.SetFloat("_Metallic", metallic);
                mat.SetFloat("_Smoothness", smoothness);
                mat.SetColor("_PaintColor", new Color(0.5f, 0.5f, 0.5f, 1f));
            }

            string outMat = $"{OutputFolder}/{baseName}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(outMat);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mat, existing);
                existing.name = baseName;
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(mat);
                return existing;
            }
            AssetDatabase.CreateAsset(mat, outMat);
            return mat;
        }

        private static float HueMask(Entry e, float hueDeg, float s, float v)
        {
            float d = Mathf.Abs(hueDeg - e.hue);
            if (d > 180f) d = 360f - d;
            float mh = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(e.hueTolerance, e.hueTolerance * 1.5f, d));
            float ms = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(e.minSaturation * 0.6f, e.minSaturation, s));
            float vMin = e.keyValue * 0.35f;
            float mv = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(vMin * 0.6f, vMin, v));
            return mh * ms * mv;
        }

        private static float WhiteMask(float s, float v)
        {
            float ms = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.08f, 0.16f, s));
            float mv = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 0.65f, v));
            return ms * mv;
        }

        private static bool WirePrefab(Entry e, Material source, Material paintReady, TrafficPaintPalette palette)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(e.prefab);
            try
            {
                Transform body = root.transform.Find(BodyRendererPath);
                Renderer renderer = body != null ? body.GetComponent<Renderer>() : null;
                if (renderer == null)
                {
                    Debug.LogError($"[TrafficPaintBaker] '{e.prefab}' has no renderer at '{BodyRendererPath}'. Not wired.");
                    return false;
                }
                int slot = System.Array.IndexOf(renderer.sharedMaterials, source);
                if (slot < 0)
                {
                    Debug.LogError($"[TrafficPaintBaker] '{source.name}' is not on '{e.prefab}/{BodyRendererPath}'. Not wired.");
                    return false;
                }
                var paint = root.GetComponent<TrafficCarPaint>();
                if (paint == null) paint = root.AddComponent<TrafficCarPaint>();
                var so = new SerializedObject(paint);
                so.FindProperty("palette").objectReferenceValue = palette;
                so.FindProperty("bodyRenderer").objectReferenceValue = renderer;
                so.FindProperty("paintSlot").intValue = slot;
                so.FindProperty("paintReadyMaterial").objectReferenceValue = paintReady;
                so.FindProperty("adapter").objectReferenceValue = root.GetComponent<TrafficVehicleAdapter>();
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, e.prefab);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static Texture2D EnsureNeutralDetail()
        {
            if (!File.Exists(NeutralDetailPath))
            {
                var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                var px = new Color32[16];
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(128, 128, 128, 255);
                t.SetPixels32(px);
                File.WriteAllBytes(NeutralDetailPath, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(NeutralDetailPath, ImportAssetOptions.ForceUpdate);
                if (AssetImporter.GetAtPath(NeutralDetailPath) is TextureImporter imp)
                {
                    imp.mipmapEnabled = false;
                    imp.textureCompression = TextureImporterCompression.Uncompressed;
                    imp.SaveAndReimport();
                }
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(NeutralDetailPath);
        }

        // Created once with realistic default paints; never overwritten afterwards.
        private static TrafficPaintPalette EnsurePalette()
        {
            var palette = AssetDatabase.LoadAssetAtPath<TrafficPaintPalette>(PalettePath);
            if (palette != null) return palette;

            palette = ScriptableObject.CreateInstance<TrafficPaintPalette>();
            AssetDatabase.CreateAsset(palette, PalettePath);
            (string name, Color color, float metallic, float smoothness)[] paints =
            {
                ("Polar White",           new Color(0.92f, 0.92f, 0.90f), 0.10f, 0.80f),
                ("Pearl White Metallic",  new Color(0.88f, 0.86f, 0.80f), 0.45f, 0.88f),
                ("Silver Metallic",       new Color(0.72f, 0.73f, 0.75f), 0.65f, 0.85f),
                ("Gunmetal Grey",         new Color(0.36f, 0.38f, 0.41f), 0.60f, 0.82f),
                ("Nardo Grey (flat)",     new Color(0.52f, 0.53f, 0.53f), 0.05f, 0.50f),
                ("Graphite Metallic",     new Color(0.20f, 0.21f, 0.22f), 0.55f, 0.80f),
                ("Jet Black",             new Color(0.06f, 0.06f, 0.07f), 0.25f, 0.90f),
                ("Midnight Blue",         new Color(0.08f, 0.12f, 0.30f), 0.55f, 0.85f),
                ("Royal Blue",            new Color(0.12f, 0.26f, 0.64f), 0.45f, 0.85f),
                ("Sky Blue",              new Color(0.45f, 0.64f, 0.82f), 0.35f, 0.80f),
                ("Deep Teal",             new Color(0.05f, 0.38f, 0.40f), 0.55f, 0.82f),
                ("British Racing Green",  new Color(0.06f, 0.25f, 0.14f), 0.45f, 0.84f),
                ("Sage Green (satin)",    new Color(0.44f, 0.48f, 0.36f), 0.15f, 0.60f),
                ("Racing Red",            new Color(0.78f, 0.07f, 0.06f), 0.20f, 0.86f),
                ("Candy Apple Red",       new Color(0.55f, 0.03f, 0.05f), 0.65f, 0.90f),
                ("Burgundy Metallic",     new Color(0.32f, 0.05f, 0.09f), 0.55f, 0.85f),
                ("Sunset Orange",         new Color(0.92f, 0.40f, 0.07f), 0.30f, 0.85f),
                ("Taxi Yellow",           new Color(0.96f, 0.76f, 0.12f), 0.10f, 0.80f),
                ("Champagne Gold",        new Color(0.75f, 0.65f, 0.46f), 0.65f, 0.85f),
                ("Bronze Metallic",       new Color(0.50f, 0.31f, 0.17f), 0.65f, 0.82f),
                ("Chocolate Brown",       new Color(0.25f, 0.15f, 0.09f), 0.45f, 0.80f),
                ("Sand Beige",            new Color(0.77f, 0.70f, 0.56f), 0.10f, 0.70f),
                ("Plum Purple",           new Color(0.30f, 0.12f, 0.40f), 0.55f, 0.85f),
                ("Mint Green",            new Color(0.58f, 0.78f, 0.66f), 0.25f, 0.80f),
            };
            var so = new SerializedObject(palette);
            SerializedProperty list = so.FindProperty("paints");
            list.arraySize = paints.Length;
            for (int i = 0; i < paints.Length; i++)
            {
                SerializedProperty p = list.GetArrayElementAtIndex(i);
                p.FindPropertyRelative("name").stringValue = paints[i].name;
                p.FindPropertyRelative("color").colorValue = paints[i].color;
                p.FindPropertyRelative("metallic").floatValue = paints[i].metallic;
                p.FindPropertyRelative("smoothness").floatValue = paints[i].smoothness;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(palette);
            return palette;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static string Sanitize(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(' ', '_').Replace('.', '_');
        }
    }
}
