using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenworldCrime.Vehicles;
using UnityEditor;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Puts the body materials of the final cars on the OpenworldCrime/Car Paint shader.
    /// - The cars share one atlas between paint, trim, interior and lamps, so a paint mask is keyed from each atlas by colour
    ///   (the atlases themselves are not modified).
    /// - Shared look settings live on CarPaint_Tuning.mat; applying copies them to every body material, keeping each car's own
    ///   texture, colour and normal map.
    /// - Colour variants: per model a handful of realistic colours (Variants below), each a material asset next to the body
    ///   material (PaintVariants folder) that only changes _PaintColor, so cars of one model and colour still share one material
    ///   and the SRP Batcher keeps batching. <see cref="WirePrefabs"/> puts a <see cref="VehiclePaintVariants"/> on every final
    ///   drivable and traffic prefab with the slots that show the body material.
    /// FinalCarBuilder calls <see cref="ApplyIfBody"/> and <see cref="WirePrefabs"/>, so rebuilding the cars keeps paint and colours.
    /// Wreck materials are not listed here: they stay on URP Lit. Menu: Tools > OpenworldCrime > Vehicles > Apply Final Car Paint.
    /// </summary>
    public static class FinalCarPaint
    {
        private const string ShaderName = "OpenworldCrime/Car Paint";
        private const string MaterialFolder = "Assets/_Project/Materials/Vehicles/FinalCars";
        private const string MaskFolder = MaterialFolder + "/PaintMasks";
        private const string TuningPath = MaterialFolder + "/CarPaint_Tuning.mat";
        private const string FlakePath = MaskFolder + "/CarPaint_Flake.png";
        private const string VariantFolder = MaterialFolder + "/PaintVariants";
        private static readonly string[] PrefabFolders = { "Assets/_Project/Prefabs/Vehicles/Drivable", "Assets/_Project/Prefabs/Vehicles/Traffic" };

        // Gamma-space colours (the project is in Gamma). Dark and neutral ones dominate, as on real streets. The darkest ones are
        // kept just off pure black so a dark car still shows its body shape under street lights instead of reading as a hole.
        private static readonly Color Black = new Color(0.075f, 0.075f, 0.085f);
        private static readonly Color White = new Color(0.86f, 0.87f, 0.87f);
        private static readonly Color Silver = new Color(0.66f, 0.67f, 0.69f);
        private static readonly Color Gunmetal = new Color(0.32f, 0.33f, 0.35f);
        private static readonly Color MidnightBlue = new Color(0.08f, 0.14f, 0.33f);
        private static readonly Color DeepRed = new Color(0.58f, 0.05f, 0.06f);
        private static readonly Color RaceRed = new Color(0.78f, 0.06f, 0.05f);
        private static readonly Color Yellow = new Color(0.95f, 0.72f, 0.06f);
        private static readonly Color ElectricBlue = new Color(0.08f, 0.30f, 0.75f);
        private static readonly Color BritishGreen = new Color(0.07f, 0.28f, 0.15f);
        private static readonly Color Orange = new Color(0.95f, 0.38f, 0.05f);

        // Colours besides the model's own (variant 0). The Bugatti's dark second tone is not in its mask, so it stays: its colours
        // are ones that go with black.
        private static readonly Dictionary<string, (string name, Color color)[]> Variants = new Dictionary<string, (string, Color)[]>
        {
            { "Audi e-tron GT", new[] { ("Black", Black), ("White", White), ("Silver", Silver), ("Gunmetal", Gunmetal), ("MidnightBlue", MidnightBlue), ("DeepRed", DeepRed), ("RaceRed", RaceRed) } },
            { "Challenger", new[] { ("Black", Black), ("White", White), ("Silver", Silver), ("RaceRed", RaceRed), ("Yellow", Yellow), ("ElectricBlue", ElectricBlue), ("Gunmetal", Gunmetal) } },
            { "BugattiChiron", new[] { ("Black", Black), ("White", White), ("Silver", Silver), ("RaceRed", RaceRed), ("Yellow", Yellow), ("BritishGreen", BritishGreen), ("Orange", Orange) } },
            { "BMW 7 series G12 2020", new[] { ("Black", Black), ("White", White), ("Silver", Silver), ("Gunmetal", Gunmetal), ("MidnightBlue", MidnightBlue), ("DeepRed", DeepRed), ("BritishGreen", BritishGreen) } },
            { "Audi SQ7", new[] { ("Black", Black), ("White", White), ("Silver", Silver), ("Gunmetal", Gunmetal), ("MidnightBlue", MidnightBlue), ("DeepRed", DeepRed), ("BritishGreen", BritishGreen) } },
            { "U Wagor", new[] { ("Black", Black), ("Silver", Silver), ("Gunmetal", Gunmetal), ("MidnightBlue", MidnightBlue), ("DeepRed", DeepRed), ("BritishGreen", BritishGreen), ("Yellow", Yellow) } },
            { "Khodge 2", new[] { ("Black", Black), ("Silver", Silver), ("Gunmetal", Gunmetal), ("RaceRed", RaceRed), ("Yellow", Yellow), ("ElectricBlue", ElectricBlue), ("Orange", Orange) } },
            { "Land Sover", new[] { ("Black", Black), ("White", White), ("Silver", Silver), ("Gunmetal", Gunmetal), ("DeepRed", DeepRed), ("BritishGreen", BritishGreen), ("Yellow", Yellow) } },
            { "Talenter", new[] { ("Black", Black), ("White", White), ("Silver", Silver), ("Gunmetal", Gunmetal), ("MidnightBlue", MidnightBlue), ("DeepRed", DeepRed), ("ElectricBlue", ElectricBlue) } },
            { "WMB", new[] { ("Black", Black), ("White", White), ("Silver", Silver), ("Gunmetal", Gunmetal), ("MidnightBlue", MidnightBlue), ("Yellow", Yellow), ("ElectricBlue", ElectricBlue) } },
        };
        // Colour group of each model's own paint (variant 0), measured from the atlases, so the weights in VehiclePaintSettings
        // apply to it as well (an orange Challenger is as rare as any other orange car).
        private static readonly Dictionary<string, string> OriginalColour = new Dictionary<string, string>
        {
            { "Audi e-tron GT", "BritishGreen" },
            { "Challenger", "Orange" },
            { "BugattiChiron", "ElectricBlue" },
            { "BMW 7 series G12 2020", "Silver" },
            { "Audi SQ7", "ElectricBlue" },
            { "U Wagor", "Gunmetal" },
            { "Khodge 2", "RaceRed" },
            { "Land Sover", "MidnightBlue" },
            { "Talenter", "Orange" },
            { "WMB", "DeepRed" },
        };
        private const string PaintSettingsPath = "Assets/_Project/Data/Vehicles/VehiclePaintSettings.asset";
        private const int MaskSize = 512;
        private const int FlakeSize = 128;

        private struct Key
        {
            public bool White;
            public float Hue, HueTolerance, MinSaturation, MinValue;

            public static Key Coloured(float hue, float tolerance, float minSaturation, float minValue) =>
                new Key { Hue = hue, HueTolerance = tolerance, MinSaturation = minSaturation, MinValue = minValue };

            public static Key Neutral(float minValue) => new Key { White = true, MinValue = minValue };
        }

        // Body material -> colour of its paint in the atlas (measured from each atlas).
        private static readonly Dictionary<string, Key> Bodies = new Dictionary<string, Key>
        {
            { "Audi e-tron GT", Key.Coloured(120f, 25f, 0.10f, 0.12f) },
            { "Challenger", Key.Coloured(40f, 8f, 0.60f, 0.34f) },
            { "BugattiChiron", Key.Coloured(196f, 12f, 0.60f, 0.25f) },
            { "BMW 7 series G12 2020", Key.Coloured(200f, 14f, 0.20f, 0.18f) },
            { "Audi SQ7", Key.Coloured(224f, 10f, 0.60f, 0.25f) },
            { "U Wagor", Key.Neutral(0.6f) },
            { "Khodge 2", Key.Neutral(0.6f) },
            { "Land Sover", Key.Coloured(222f, 14f, 0.50f, 0.16f) },
            { "Talenter", Key.Coloured(28f, 8f, 0.60f, 0.34f) },
            { "WMB", Key.Coloured(5f, 9f, 0.60f, 0.34f) },
        };

        public static bool IsBody(string materialName) => Bodies.ContainsKey(materialName);

        [MenuItem("Tools/OpenworldCrime/Vehicles/Apply Final Car Paint")]
        public static void ApplyAllMenu() => Debug.Log(ApplyAll());

        /// <summary>
        /// Copies only the shared look settings (the float values) of CarPaint_Tuning.mat to every body and colour variant
        /// material: no mask bake, no prefab changes, so tuning the paint is quick. Per-material values (atlas brightness,
        /// colour variant switch, burnt state, normal map toggle) and all colours and textures are kept.
        /// </summary>
        [MenuItem("Tools/OpenworldCrime/Vehicles/Sync Car Paint Tuning (look only)")]
        public static void SyncTuningMenu() => Debug.Log(SyncTuning());

        private static readonly HashSet<string> PerMaterialFloats = new HashSet<string> { "_PaintRecolor", "_PaintRefLuma", "_Burnt", "_UseNormalMap" };

        public static string SyncTuning()
        {
            Shader shader = Shader.Find(ShaderName);
            var tuning = AssetDatabase.LoadAssetAtPath<Material>(TuningPath);
            if (shader == null || tuning == null) return "[FinalCarPaint] shader or CarPaint_Tuning.mat not found";
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialFolder }))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material == null || material == tuning || material.shader != shader) continue;
                for (int i = 0; i < shader.GetPropertyCount(); i++)
                {
                    UnityEngine.Rendering.ShaderPropertyType type = shader.GetPropertyType(i);
                    if (type != UnityEngine.Rendering.ShaderPropertyType.Float && type != UnityEngine.Rendering.ShaderPropertyType.Range) continue;
                    string property = shader.GetPropertyName(i);
                    if (PerMaterialFloats.Contains(property)) continue;
                    material.SetFloat(property, tuning.GetFloat(property));
                }
                EditorUtility.SetDirty(material);
                count++;
            }
            AssetDatabase.SaveAssets();
            return $"[FinalCarPaint] look settings of CarPaint_Tuning copied to {count} car paint materials";
        }

        public static string ApplyAll()
        {
            var sb = new StringBuilder("[FinalCarPaint]\n");
            foreach (string name in Bodies.Keys)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/{name}.mat");
                if (material == null)
                {
                    sb.AppendLine($"  {name}: material not found");
                    continue;
                }
                sb.AppendLine($"  {name}: {Apply(material)}");
            }
            AssetDatabase.SaveAssets();
            sb.Append(WirePrefabs());
            return sb.ToString();
        }

        /// <summary>Called by FinalCarBuilder after a material was (re)made as URP Lit.</summary>
        public static void ApplyIfBody(Material material)
        {
            if (material != null && IsBody(material.name)) Apply(material);
        }

        private static string Apply(Material material)
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null) return "shader not found: " + ShaderName;

            Texture baseMap = material.GetTexture("_BaseMap");
            Texture normal = material.HasProperty("_BumpMap") ? material.GetTexture("_BumpMap") : null;
            Color color = material.GetColor("_BaseColor");
            Vector2 tiling = material.GetTextureScale("_BaseMap"), offset = material.GetTextureOffset("_BaseMap");

            string log = BakeMask(material.name, baseMap, color, out Texture2D mask, out float paintLuma);
            if (mask == null) return log;

            Material tuning = GetTuning(shader);
            material.shader = shader;
            material.CopyPropertiesFromMaterial(tuning);
            material.shaderKeywords = new string[0];
            material.SetTexture("_BaseMap", baseMap);
            material.SetTextureScale("_BaseMap", tiling);
            material.SetTextureOffset("_BaseMap", offset);
            material.SetColor("_BaseColor", color);
            material.SetTexture("_PaintMask", mask);
            material.SetTexture("_FlakeMap", GetFlake());
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_UseNormalMap", normal != null ? 1f : 0f);
            if (normal != null) material.EnableKeyword("_NORMALMAP");
            material.SetFloat("_PaintRefLuma", paintLuma);
            material.SetFloat("_PaintRecolor", 0f);
            material.renderQueue = -1;
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            EditorUtility.SetDirty(material);
            int variants = MakeVariants(material).Count - 1;
            return $"{log}, paint brightness {paintLuma:F2}, {variants} colour variants";
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Colour variants
        // ---------------------------------------------------------------------------------------------------------------

        /// <summary>The body material followed by its colour variants (created or refreshed in place, so their GUIDs stay).</summary>
        private static List<Material> MakeVariants(Material body)
        {
            var list = new List<Material> { body };
            if (!Variants.TryGetValue(body.name, out (string name, Color color)[] colours)) return list;
            EnsureFolder(VariantFolder);
            foreach ((string colourName, Color colour) in colours)
            {
                string path = $"{VariantFolder}/{Sanitize(body.name)}_{colourName}.mat";
                var variant = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (variant == null)
                {
                    variant = new Material(body);
                    AssetDatabase.CreateAsset(variant, path);
                }
                else
                {
                    variant.shader = body.shader;
                    variant.CopyPropertiesFromMaterial(body);
                    variant.shaderKeywords = body.shaderKeywords;
                }
                variant.name = Path.GetFileNameWithoutExtension(path);
                variant.SetColor("_PaintColor", colour);
                variant.SetFloat("_PaintRecolor", 1f);
                variant.renderQueue = body.renderQueue;
                variant.enableInstancing = body.enableInstancing;
                variant.globalIlluminationFlags = body.globalIlluminationFlags;
                EditorUtility.SetDirty(variant);
                list.Add(variant);
            }
            return list;
        }

        /// <summary>Colour names in variant order: the original paint's group, then the variants.</summary>
        private static string[] ColourNames(Material body, int count)
        {
            var names = new string[count];
            names[0] = OriginalColour.TryGetValue(body.name, out string original) ? original : "Original";
            if (Variants.TryGetValue(body.name, out (string name, Color color)[] colours))
            {
                for (int i = 1; i < count && i - 1 < colours.Length; i++) names[i] = colours[i - 1].name;
            }
            return names;
        }

        private static VehiclePaintSettings GetPaintSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<VehiclePaintSettings>(PaintSettingsPath);
            if (settings != null) return settings;
            settings = ScriptableObject.CreateInstance<VehiclePaintSettings>();
            AssetDatabase.CreateAsset(settings, PaintSettingsPath);
            return settings;
        }

        /// <summary>
        /// Puts a <see cref="VehiclePaintVariants"/> on every final drivable and traffic prefab whose renderers show a body
        /// material, listing those slots and the body's variants. Traffic prefabs re-roll their colour on every spawn.
        /// </summary>
        public static string WirePrefabs()
        {
            var bodies = new Dictionary<Material, List<Material>>();
            foreach (string name in Bodies.Keys)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/{name}.mat");
                if (material != null) bodies[material] = MakeVariants(material);
            }
            VehiclePaintSettings paintSettings = GetPaintSettings();
            AssetDatabase.SaveAssets();

            int wired = 0;
            var sb = new StringBuilder();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", PrefabFolders))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var slots = new List<VehiclePaintVariants.Slot>();
                    List<Material> variants = null;
                    foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        Material[] materials = r.sharedMaterials;
                        for (int m = 0; m < materials.Length; m++)
                        {
                            Material body = materials[m] != null ? FindBody(materials[m], bodies) : null;
                            if (body == null) continue;
                            if (variants == null) variants = bodies[body];
                            else if (variants[0] != body) continue;
                            // A prefab saved showing a variant goes back to the body material (variant 0) here.
                            if (materials[m] != body)
                            {
                                materials[m] = body;
                                r.sharedMaterials = materials;
                            }
                            slots.Add(new VehiclePaintVariants.Slot { renderer = r, materialIndex = m });
                        }
                    }
                    var existing = root.GetComponent<VehiclePaintVariants>();
                    if (variants == null || slots.Count == 0)
                    {
                        if (existing != null)
                        {
                            Object.DestroyImmediate(existing, true);
                            PrefabUtility.SaveAsPrefabAsset(root, path);
                        }
                        continue;
                    }
                    if (existing == null) existing = root.AddComponent<VehiclePaintVariants>();
                    existing.EditorSetup(slots.ToArray(), variants.ToArray(), ColourNames(variants[0], variants.Count), paintSettings,
                        root.GetComponent<TrafficVehicleAdapter>() != null);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    wired++;
                    sb.AppendLine($"  {Path.GetFileNameWithoutExtension(path)}: {variants[0].name}, {slots.Count} slot(s), {variants.Count} colours");
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            return $"[FinalCarPaint] colour variants on {wired} prefabs\n{sb}";
        }

        private static Material FindBody(Material material, Dictionary<Material, List<Material>> bodies)
        {
            foreach (KeyValuePair<Material, List<Material>> pair in bodies)
            {
                if (pair.Value.Contains(material)) return pair.Key;
            }
            return null;
        }

        private static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Replace(' ', '_');
        }

        private static Material GetTuning(Shader shader)
        {
            var tuning = AssetDatabase.LoadAssetAtPath<Material>(TuningPath);
            if (tuning != null) return tuning;
            tuning = new Material(shader) { name = "CarPaint_Tuning" };
            AssetDatabase.CreateAsset(tuning, TuningPath);
            return tuning;
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Paint mask
        // ---------------------------------------------------------------------------------------------------------------
        private static string BakeMask(string materialName, Texture atlas, Color tint, out Texture2D result, out float paintLuma)
        {
            result = null;
            paintLuma = 0.5f;
            string atlasPath = atlas != null ? AssetDatabase.GetAssetPath(atlas) : null;
            if (string.IsNullOrEmpty(atlasPath) || !File.Exists(atlasPath)) return "atlas file not readable, left unchanged";

            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            source.LoadImage(File.ReadAllBytes(atlasPath));
            float[] mask = KeyMask(source, Bodies[materialName], out int width, out int height);
            Color32[] pixels = source.GetPixels32();
            Object.DestroyImmediate(source);

            RemoveIslands(mask, width, height, width * height / 2000);
            paintLuma = PaintLuma(pixels, mask, tint);
            float coverage = WriteMask(mask, width, height, GetMaskPath(materialName));
            result = AssetDatabase.LoadAssetAtPath<Texture2D>(GetMaskPath(materialName));
            return $"paint mask covers {coverage:P0} of the atlas";
        }

        // Mean brightness of the painted pixels (atlas x base colour, gamma space, as the shader sees them): a variant colour is
        // scaled by each pixel's brightness against this, so the atlas' panel shading carries over to every colour.
        private static float PaintLuma(Color32[] pixels, float[] mask, Color tint)
        {
            double sum = 0, weight = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (mask[i] < 0.5f) continue;
                Color c = pixels[i];
                sum += mask[i] * (0.2126f * c.r * tint.r + 0.7152f * c.g * tint.g + 0.0722f * c.b * tint.b);
                weight += mask[i];
            }
            return weight > 0 ? Mathf.Clamp((float)(sum / weight), 0.02f, 1f) : 0.5f;
        }

        private static string GetMaskPath(string materialName)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) materialName = materialName.Replace(c, '_');
            return $"{MaskFolder}/{materialName}_PaintMask.png";
        }

        private static float[] KeyMask(Texture2D source, Key key, out int width, out int height)
        {
            width = source.width;
            height = source.height;
            Color32[] pixels = source.GetPixels32();
            var mask = new float[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                Color.RGBToHSV(pixels[i], out float h, out float s, out float v);
                mask[i] = key.White ? NeutralMask(key, s, v) : ColouredMask(key, h * 360f, s, v);
            }
            return mask;
        }

        private static float ColouredMask(Key key, float hue, float s, float v)
        {
            float d = Mathf.Abs(hue - key.Hue);
            if (d > 180f) d = 360f - d;
            float inHue = 1f - Mathf.InverseLerp(key.HueTolerance, key.HueTolerance * 1.5f, d);
            float inSaturation = Mathf.InverseLerp(key.MinSaturation * 0.6f, key.MinSaturation, s);
            float inValue = Mathf.InverseLerp(key.MinValue * 0.6f, key.MinValue, v);
            return Mathf.SmoothStep(0f, 1f, inHue * inSaturation * inValue);
        }

        private static float NeutralMask(Key key, float s, float v)
        {
            float neutral = 1f - Mathf.InverseLerp(0.08f, 0.16f, s);
            float bright = Mathf.InverseLerp(key.MinValue * 0.85f, key.MinValue, v);
            return Mathf.SmoothStep(0f, 1f, neutral * bright);
        }

        // Atlas details that happen to share the paint colour (dials, buttons, lamp edges) are small: only large areas are paint.
        private static void RemoveIslands(float[] mask, int width, int height, int minPixels)
        {
            var visited = new bool[mask.Length];
            var stack = new Stack<int>();
            var island = new List<int>();
            for (int start = 0; start < mask.Length; start++)
            {
                if (visited[start] || mask[start] < 0.5f) continue;
                island.Clear();
                stack.Push(start);
                visited[start] = true;
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    island.Add(i);
                    int x = i % width;
                    if (x > 0) Visit(i - 1, mask, visited, stack);
                    if (x < width - 1) Visit(i + 1, mask, visited, stack);
                    if (i >= width) Visit(i - width, mask, visited, stack);
                    if (i < mask.Length - width) Visit(i + width, mask, visited, stack);
                }
                if (island.Count >= minPixels) continue;
                for (int j = 0; j < island.Count; j++) mask[island[j]] = 0f;
            }
        }

        private static void Visit(int i, float[] mask, bool[] visited, Stack<int> stack)
        {
            if (visited[i] || mask[i] < 0.5f) return;
            visited[i] = true;
            stack.Push(i);
        }

        private static float WriteMask(float[] mask, int width, int height, string path)
        {
            EnsureFolder(MaskFolder);
            var output = new Texture2D(width, height, TextureFormat.RGB24, false);
            var pixels = new Color32[mask.Length];
            int painted = 0;
            for (int i = 0; i < mask.Length; i++)
            {
                byte value = (byte)(Mathf.Clamp01(mask[i]) * 255f);
                pixels[i] = new Color32(value, value, value, 255);
                if (value > 127) painted++;
            }
            output.SetPixels32(pixels);
            output.Apply(false);
            File.WriteAllBytes(path, output.EncodeToPNG());
            Object.DestroyImmediate(output);
            ImportData(path, MaskSize, TextureWrapMode.Clamp, FilterMode.Bilinear);
            return (float)painted / mask.Length;
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Flake map
        // ---------------------------------------------------------------------------------------------------------------
        private static Texture2D GetFlake()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(FlakePath);
            if (existing != null) return existing;

            EnsureFolder(MaskFolder);
            // Fixed seed: a re-run writes the same file. Random tilt in RG averages to no tilt in the mips; sparse bright flakes in B.
            var random = new System.Random(20261105);
            var pixels = new Color32[FlakeSize * FlakeSize];
            for (int i = 0; i < pixels.Length; i++)
            {
                byte amount = (byte)(random.NextDouble() < 0.35 ? random.Next(120, 256) : random.Next(0, 50));
                pixels[i] = new Color32((byte)random.Next(0, 256), (byte)random.Next(0, 256), amount, 255);
            }
            var output = new Texture2D(FlakeSize, FlakeSize, TextureFormat.RGB24, false);
            output.SetPixels32(pixels);
            output.Apply(false);
            File.WriteAllBytes(FlakePath, output.EncodeToPNG());
            Object.DestroyImmediate(output);
            ImportData(FlakePath, FlakeSize, TextureWrapMode.Repeat, FilterMode.Bilinear);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(FlakePath);
        }

        // Data textures: no sRGB curve, mipmaps on, compressed for the target platform.
        private static void ImportData(string path, int maxSize, TextureWrapMode wrap, FilterMode filter)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = maxSize;
            importer.wrapMode = wrap;
            importer.filterMode = filter;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
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
