using System.Collections.Generic;
using System.Text;
using OpenworldCrime.Vehicles;
using UnityEditor;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Car paint on the custom OpenworldCrime/Vehicle Paint shader:
    /// - creates the 16 shared colour materials and the CarPaintPalette;
    /// - civilian cars: their body paint slots (every LOD) get a CarPaintVariant (random colour per spawn, kept when stolen);
    /// - liveried cars (police, taxi, ambulance, bus): their body material is replaced by a fixed paint material in their own
    ///   colour, so they get the same clear-coat look but keep their livery colour.
    /// Re-runnable; VehicleFactory runs it after every build. Menu: Tools > OpenworldCrime > Vehicles > Setup Car Paint.
    /// </summary>
    public static class CarPaintSetup
    {
        private const string MaterialFolder = "Assets/_Project/Materials/Vehicles/Paint";
        private const string PalettePath = "Assets/_Project/Data/Vehicles/CarPaintPalette.asset";
        private const string ShaderName = "OpenworldCrime/Vehicle Paint";

        private struct Paint
        {
            public string Name; public Color Color; public float Metallic, Smoothness;
            public Paint(string name, Color color, float metallic, float smoothness) { Name = name; Color = color; Metallic = metallic; Smoothness = smoothness; }
        }

        private static readonly Paint[] Paints =
        {
            new Paint("PolarWhite",     new Color(0.92f, 0.92f, 0.90f), 0.10f, 0.80f),
            new Paint("SilverMetallic", new Color(0.70f, 0.72f, 0.74f), 0.75f, 0.82f),
            new Paint("GunmetalGrey",   new Color(0.30f, 0.32f, 0.34f), 0.65f, 0.78f),
            new Paint("JetBlack",       new Color(0.04f, 0.04f, 0.05f), 0.35f, 0.88f),
            new Paint("RacingRed",      new Color(0.74f, 0.05f, 0.05f), 0.25f, 0.85f),
            new Paint("CandyBurgundy",  new Color(0.38f, 0.04f, 0.09f), 0.60f, 0.85f),
            new Paint("SunsetOrange",   new Color(0.92f, 0.38f, 0.06f), 0.30f, 0.82f),
            new Paint("LimeGold",       new Color(0.80f, 0.70f, 0.18f), 0.55f, 0.80f),
            new Paint("BritishGreen",   new Color(0.05f, 0.23f, 0.12f), 0.45f, 0.84f),
            new Paint("MintGreen",      new Color(0.55f, 0.85f, 0.70f), 0.20f, 0.80f),
            new Paint("DeepTeal",       new Color(0.03f, 0.33f, 0.36f), 0.55f, 0.84f),
            new Paint("SkyBlue",        new Color(0.40f, 0.65f, 0.90f), 0.35f, 0.82f),
            new Paint("RoyalBlue",      new Color(0.08f, 0.20f, 0.70f), 0.50f, 0.85f),
            new Paint("MidnightBlue",   new Color(0.03f, 0.06f, 0.20f), 0.55f, 0.86f),
            new Paint("PlumPurple",     new Color(0.32f, 0.10f, 0.40f), 0.55f, 0.84f),
            new Paint("ChocolateBrown", new Color(0.26f, 0.15f, 0.08f), 0.40f, 0.80f),
        };

        // Per model: the body paint material names (civilian) or the livery body material + its fixed colour.
        // Final car pack: body paint material per model (the palette colour is painted over the body's own panel texture).
        private static readonly Dictionary<string, string[]> CivilianBody = new Dictionary<string, string[]>
        {
            { "BNWi8", new[] { "Body_07_Black" } },
            { "BugettaC", new[] { "Sport_08_Body" } },
            { "Cervo", new[] { "Body_D_1" } },
            { "Ferrari", new[] { "Body_Ferrari_2" } },
            { "FerraroEn", new[] { "Sport_09_Body" } },
            { "Hummer", new[] { "Body_Hummer" } },
            { "Jeep", new[] { "Body_red_d1" } },
            { "Roenigsegg", new[] { "Sport_13_Body" } },
            { "StandAsideCar1", new[] { "Body_D_1" } },
            { "StandAsideCar30", new[] { "Body_D_30" } },
            { "StandAsideCar", new[] { "Body_D_40" } },
        };

        private static readonly Dictionary<string, KeyValuePair<string, Color>[]> Livery = new Dictionary<string, KeyValuePair<string, Color>[]>
        {
            { "Taxi", new[] { new KeyValuePair<string, Color>("Body_Taxi", new Color(0.95f, 0.70f, 0.05f)) } },
        };

        // Each civilian model's own default colour (palette index) for the drivable car; traffic cars pick at random.
        private static readonly string[] CivilianOrder =
            { "BNWi8", "BugettaC", "Cervo", "Ferrari", "FerraroEn", "Hummer", "Jeep", "Roenigsegg", "StandAsideCar1", "StandAsideCar30", "StandAsideCar" };

        [MenuItem("Tools/OpenworldCrime/Vehicles/Setup Car Paint")]
        public static void ApplyAllMenu() => Debug.Log(ApplyAll());

        public static string ApplyAll()
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null) return "[CarPaintSetup] Shader not found: " + ShaderName;
            EnsureFolder(MaterialFolder);
            // The palette is the Paint_XX_* materials already in the Paint folder, used as they are (edit them to restyle a
            // colour, delete one to drop it). Only an empty folder is filled with the default colours.
            var found = new List<Material>();
            foreach (string guid in AssetDatabase.FindAssets("Paint_ t:Material", new[] { MaterialFolder }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileName(p).StartsWith("Paint_") && System.IO.Path.GetDirectoryName(p).Replace('\\', '/') == MaterialFolder)
                    found.Add(AssetDatabase.LoadAssetAtPath<Material>(p));
            }
            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            if (found.Count == 0)
                for (int i = 0; i < Paints.Length; i++) found.Add(MakePaint($"Paint_{i:00}_{Paints[i].Name}", shader, Paints[i].Color, Paints[i].Metallic, Paints[i].Smoothness));
            foreach (var m in found) if (m.shader != shader) { m.shader = shader; EditorUtility.SetDirty(m); }
            var mats = found.ToArray();
            var palette = AssetDatabase.LoadAssetAtPath<CarPaintPalette>(PalettePath);
            if (palette == null)
            {
                EnsureFolder(System.IO.Path.GetDirectoryName(PalettePath).Replace('\\', '/'));
                palette = ScriptableObject.CreateInstance<CarPaintPalette>();
                AssetDatabase.CreateAsset(palette, PalettePath);
            }
            palette.EditorSetPaints(mats);
            EditorUtility.SetDirty(palette);

            var sb = new StringBuilder($"[CarPaintSetup] {mats.Length} paints in the palette.\n");
            foreach (string folder in new[] { "Assets/_Project/Prefabs/Vehicles/Drivable", "Assets/_Project/Prefabs/Vehicles/Traffic" })
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string key = KeyOf(System.IO.Path.GetFileNameWithoutExtension(path));
                    if (key == null) continue;
                    sb.AppendLine("  " + System.IO.Path.GetFileNameWithoutExtension(path) + ": " + ApplyToPrefab(path, key, palette, shader));
                }
            AssetDatabase.SaveAssets();
            sb.AppendLine(CarMaterialSetup.ApplyAll());
            return sb.ToString();
        }

        /// <summary>The fixed livery paint of a liveried model (police, taxi, ambulance, bus), or null for civilian cars.</summary>
        internal static Material LiveryPaintFor(string key) =>
            Livery.ContainsKey(key) ? AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/Livery_{key}.mat") : null;

        internal static string KeyOf(string prefabName)
        {
            string n = prefabName.Replace("Drivable_", "").Replace("Traffic_", "");
            if (CivilianBody.ContainsKey(n) || Livery.ContainsKey(n)) return n;
            return null;
        }

        private static string ApplyToPrefab(string path, string key, CarPaintPalette palette, Shader shader)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                string log;
                if (Livery.TryGetValue(key, out var livery))
                {
                    int swapped = 0;
                    // A model moved from the random palette to a livery still holds palette paints: those slots become livery too.
                    var palettePaints = new HashSet<string>();
                    for (int i = 0; i < palette.Count; i++) palettePaints.Add(palette.Get(i).name);
                    foreach (var entry in livery)
                    {
                        Material fixedPaint = MakePaint($"Livery_{key}", shader, entry.Value, 0.15f, 0.80f);
                        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                        {
                            var shared = r.sharedMaterials; bool changed = false;
                            for (int i = 0; i < shared.Length; i++)
                                if (shared[i] != null && (shared[i].name == entry.Key || shared[i].name == fixedPaint.name || palettePaints.Contains(shared[i].name))) { shared[i] = fixedPaint; changed = true; swapped++; }
                            if (changed) r.sharedMaterials = shared;
                        }
                    }
                    var stale = root.GetComponent<CarPaintVariant>();
                    if (stale != null) Object.DestroyImmediate(stale);
                    log = $"livery paint on {swapped} slot(s)";
                }
                else
                {
                    var names = new HashSet<string>(CivilianBody[key]);
                    var paintNames = new HashSet<string>();
                    for (int i = 0; i < palette.Count; i++) paintNames.Add(palette.Get(i).name);
                    // A re-run finds palette paints in the slots: keep the body textures recorded the first time.
                    var variant = root.GetComponent<CarPaintVariant>();
                    var previous = new Dictionary<(Renderer, int), CarPaintVariant.PaintSlot>();
                    if (variant != null)
                    {
                        var so = new SerializedObject(variant);
                        var prop = so.FindProperty("slots");
                        for (int i = 0; i < prop.arraySize; i++)
                        {
                            var e = prop.GetArrayElementAtIndex(i);
                            var s = new CarPaintVariant.PaintSlot
                            {
                                renderer = e.FindPropertyRelative("renderer").objectReferenceValue as Renderer,
                                materialIndex = e.FindPropertyRelative("materialIndex").intValue,
                                bodyTexture = e.FindPropertyRelative("bodyTexture")?.objectReferenceValue as Texture,
                                bodyNormal = e.FindPropertyRelative("bodyNormal")?.objectReferenceValue as Texture,
                            };
                            if (s.renderer != null) previous[(s.renderer, s.materialIndex)] = s;
                        }
                    }
                    int defaultIndex = System.Array.IndexOf(CivilianOrder, key);
                    defaultIndex = palette.Count > 0 ? Mathf.Max(0, defaultIndex) % palette.Count : 0;
                    var slots = new List<CarPaintVariant.PaintSlot>();
                    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r is ParticleSystemRenderer || r is SkinnedMeshRenderer) continue;
                        var shared = r.sharedMaterials;
                        for (int i = 0; i < shared.Length; i++)
                        {
                            Material m = shared[i];
                            if (m == null) continue;
                            if (names.Contains(m.name))
                            {
                                slots.Add(new CarPaintVariant.PaintSlot
                                {
                                    renderer = r, materialIndex = i,
                                    bodyTexture = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null,
                                    bodyNormal = m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null,
                                });
                                shared[i] = palette.Get(defaultIndex);
                            }
                            else if (paintNames.Contains(m.name))
                            {
                                slots.Add(previous.TryGetValue((r, i), out var old) ? old : new CarPaintVariant.PaintSlot { renderer = r, materialIndex = i });
                                shared[i] = palette.Get(defaultIndex);
                            }
                        }
                        r.sharedMaterials = shared;
                    }
                    if (variant == null) variant = root.AddComponent<CarPaintVariant>();
                    // Traffic: random colour per spawn. Drivable: the model's own colour (a stolen car keeps the traffic colour).
                    bool traffic = root.GetComponent<TrafficVehicleAdapter>() != null;
                    variant.EditorSetup(palette, slots.ToArray(), traffic, defaultIndex);
                    log = $"{slots.Count} paint slot(s), " + (traffic ? $"random of {palette.Count}" : $"own colour {palette.Get(defaultIndex).name}");
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return log;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        internal static Material MakePaint(string name, Shader shader, Color color, float metallic, float smoothness)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetColor("_BaseColor", Color.white);
            mat.SetColor("_PaintColor", color * 0.5f);          // shader convention: x2 on 0.5 grey
            mat.SetFloat("_PaintMaskStrength", 0f);             // these bodies have no paint mask texture: the whole body is paint
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_ClearCoat", 1f);
            mat.SetFloat("_ClearCoatSmoothness", 0.92f);
            mat.SetFloat("_DirtAmount", 0.06f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
