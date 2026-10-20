using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenworldCrime.Vehicles;
using UnityEditor;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Makes the car parts whose model material has no texture (the pack's PNGs were never supplied) look like real car parts:
    /// every such slot gets a shared look-alike material chosen by the material's name:
    ///   glass -> tinted transparent glass; logo/decal -> the car's own body paint (the decal blends into the panel instead of
    ///   showing as a white block); siren -> light-bar lens; lights -> lamp lens (VehicleLights still makes it glow at night);
    ///   wheel -> dark alloy + tyre; interior/seat -> dark interior; matte/trim -> satin black trim; engine -> dark metal;
    ///   secondary body colours -> clear-coat paint in the artist's own colour.
    /// Slots are recorded in CarMaterialFallback; every run first restores the originals, so a slot whose real texture has since
    /// been imported goes back to the real material. Run by Link Car Textures, Setup Car Paint and the vehicle build.
    /// Menu: Tools > OpenworldCrime > Vehicles > Apply Car Part Materials.
    /// </summary>
    public static class CarMaterialSetup
    {
        private const string Folder = "Assets/_Project/Materials/Vehicles/Parts";
        private const string PaintShader = "OpenworldCrime/Vehicle Paint";
        private static readonly string[] PrefabFolders = { "Assets/_Project/Prefabs/Vehicles/Drivable", "Assets/_Project/Prefabs/Vehicles/Traffic" };

        [MenuItem("Tools/OpenworldCrime/Vehicles/Apply Car Part Materials")]
        public static void ApplyAllMenu() => Debug.Log(ApplyAll());

        public static string ApplyAll()
        {
            EnsureFolder(Folder);
            var sb = new StringBuilder("[CarMaterialSetup]\n");
            foreach (string folder in PrefabFolders)
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string key = CarPaintSetup.KeyOf(Path.GetFileNameWithoutExtension(path));
                    if (key == null) continue;
                    sb.AppendLine($"  {Path.GetFileNameWithoutExtension(path)}: {ApplyToPrefab(path, key)}");
                }
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        private static string ApplyToPrefab(string path, string key)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var record = root.GetComponent<CarMaterialFallback>();
                int restored = 0;
                if (record != null)
                    foreach (var r in record.Records)
                    {
                        if (r.renderer == null || r.original == null) continue;
                        var shared = r.renderer.sharedMaterials;
                        if (r.materialIndex >= shared.Length || shared[r.materialIndex] != r.fallback) continue;
                        shared[r.materialIndex] = r.original;
                        r.renderer.sharedMaterials = shared;
                        restored++;
                    }

                var records = new List<CarMaterialFallback.Record>();
                Material livery = CarPaintSetup.LiveryPaintFor(key);
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer is ParticleSystemRenderer || renderer is SkinnedMeshRenderer) continue; // FX and seated dummies
                    var shared = renderer.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < shared.Length; i++)
                    {
                        Material original = shared[i];
                        if (!NeedsFallback(original)) continue;
                        Material fallback = FallbackFor(original, livery);
                        if (fallback == null) continue;
                        shared[i] = fallback;
                        changed = true;
                        records.Add(new CarMaterialFallback.Record { renderer = renderer, materialIndex = i, original = original, fallback = fallback });
                    }
                    if (changed) renderer.sharedMaterials = shared;
                }

                if (records.Count > 0)
                {
                    if (record == null) record = root.AddComponent<CarMaterialFallback>();
                    record.EditorSetRecords(records.ToArray());
                }
                else if (record != null) Object.DestroyImmediate(record);

                PrefabUtility.SaveAsPrefabAsset(root, path);
                return $"{records.Count} part slot(s) given look-alike materials" + (restored > 0 ? $", {restored} checked/restored" : "");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // Only the car model's own materials that came without a texture.
        private static bool NeedsFallback(Material m)
        {
            if (m == null || !AssetDatabase.GetAssetPath(m).EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) return false;
            return !m.HasProperty("_BaseMap") || m.GetTexture("_BaseMap") == null;
        }

        private static Material FallbackFor(Material original, Material livery)
        {
            string n = original.name.ToLowerInvariant();
            if (n.Contains("glass")) return Glass();
            if (n.Contains("logo") || n == "taxi") return livery != null ? livery : Lit("Trim_SatinBlack", new Color(0.05f, 0.05f, 0.055f), 0.1f, 0.5f);
            if (n.Contains("serena") || n.Contains("siren")) return Lit("Siren_Lens", new Color(0.55f, 0.58f, 0.66f), 0f, 0.9f);
            if (n.Contains("tail")) return Lit("Lamp_Lens_Red", new Color(0.55f, 0.05f, 0.05f), 0f, 0.92f);
            if (n.Contains("light")) return Lit("Lamp_Lens_Clear", new Color(0.78f, 0.79f, 0.8f), 0.1f, 0.94f);
            if (n.Contains("wheel")) return Lit("Wheel_DarkAlloy", new Color(0.13f, 0.13f, 0.14f), 0.55f, 0.55f);
            if (n.Contains("disk")) return Lit("Wheel_Chrome", new Color(0.75f, 0.76f, 0.78f), 1f, 0.85f);
            if (n.Contains("dvigatel") || n.Contains("engine")) return Lit("Engine_DarkMetal", new Color(0.22f, 0.22f, 0.23f), 0.8f, 0.55f);
            if (n.Contains("interior") || n.Contains("salon") || n.Contains("sidushka") || n.StartsWith("material__")) return Lit("Interior_Dark", new Color(0.1f, 0.1f, 0.105f), 0f, 0.28f);
            if (n.Contains("matte") || n.Contains("mate")) return Lit("Trim_SatinBlack", new Color(0.05f, 0.05f, 0.055f), 0.1f, 0.5f);
            if (n.Contains("body") || n.Contains("police_jeep"))
            {
                Color c = original.HasProperty("_BaseColor") ? original.GetColor("_BaseColor") : Color.white;
                return CarPaintSetup.MakePaint("Paint2_" + original.name, Shader.Find(PaintShader), c, 0.35f, 0.82f);
            }
            return null; // unknown: keep the model's own material
        }

        private static Material Glass()
        {
            Material m = LoadOrCreate("Glass_Tinted", Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", new Color(0.04f, 0.055f, 0.065f, 0.62f));
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", 0.96f);
            m.SetFloat("_Surface", 1f);           // transparent
            m.SetFloat("_Blend", 0f);             // alpha
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
            m.SetFloat("_ReceiveShadows", 1f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetShaderPassEnabled("ShadowCaster", false);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material Lit(string name, Color color, float metallic, float smoothness)
        {
            Material m = LoadOrCreate(name, Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material LoadOrCreate(string name, Shader shader)
        {
            string path = $"{Folder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            return m;
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
