using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// The final car pack (Assets/FinalCars) comes with Built-in pipeline materials (Standard / Autodesk Interactive), which do
    /// not render in URP. This converts them, without touching the originals, into URP materials under
    /// Assets/_Project/Materials/Vehicles/FinalCars/[car]/ (same names, so the light and paint setup can still recognise them):
    /// - glass -> one shared OpenworldCrime/Vehicle Glass material (tint + fresnel reflections);
    /// - lamps (taillights / Light_*) and trim atlases with an emission mask -> URP Lit with that emission map, emission off
    ///   (VehicleLights switches it on at night / when braking);
    /// - the taxi checker decal -> URP Lit alpha clip;
    /// - body paint materials -> handled by CarPaintSetup (palette colour over the body texture);
    /// - everything else -> URP Lit with albedo, normal map, colour, metallic and smoothness carried over.
    /// </summary>
    public static class FinalCarMaterials
    {
        public const string SourceFolder = "Assets/FinalCars";
        private const string TargetFolder = "Assets/_Project/Materials/Vehicles/FinalCars";
        private const string GlassPath = "Assets/_Project/Materials/Vehicles/FinalCars/Glass_Car.mat";

        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>Converts every material of the given car folders (re-runnable) and returns source -> URP material.</summary>
        public static Dictionary<Material, Material> ConvertAll(IEnumerable<string> carFolders)
        {
            var map = new Dictionary<Material, Material>();
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            Material glass = GetGlass();
            foreach (string folder in carFolders)
            {
                string car = Path.GetFileNameWithoutExtension(folder).Replace(".fbm", "");
                string target = $"{TargetFolder}/{car}";
                EnsureFolder(target);
                foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
                {
                    var src = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                    if (src == null) continue;
                    map[src] = IsGlass(src) ? glass : Convert(src, $"{target}/{src.name}.mat", lit);
                }
            }
            AssetDatabase.SaveAssets();
            return map;
        }

        public static bool IsGlass(Material m)
        {
            string n = m.name.ToLowerInvariant();
            return n.Contains("glass") && !n.Contains("light");
        }

        private static Material Convert(Material src, string path, Shader lit)
        {
            var dst = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (dst == null)
            {
                dst = new Material(lit) { name = src.name };
                AssetDatabase.CreateAsset(dst, path);
            }
            dst.shader = lit;
            Texture albedo = src.HasProperty(MainTex) ? src.GetTexture(MainTex) : null;
            Color color = src.HasProperty(ColorId) ? src.GetColor(ColorId) : UnityEngine.Color.white;
            color.a = 1f;
            dst.SetTexture("_BaseMap", albedo);
            dst.SetColor("_BaseColor", color);
            Texture normal = src.HasProperty("_BumpMap") ? src.GetTexture("_BumpMap") : null;
            dst.SetTexture("_BumpMap", normal);
            if (normal != null) dst.EnableKeyword("_NORMALMAP"); else dst.DisableKeyword("_NORMALMAP");
            dst.SetFloat("_Metallic", src.HasProperty("_Metallic") ? Mathf.Min(src.GetFloat("_Metallic"), 0.6f) : 0f);
            dst.SetFloat("_Smoothness", src.HasProperty("_Glossiness") ? Mathf.Clamp(src.GetFloat("_Glossiness"), 0.2f, 0.8f) : 0.45f);

            // Emission: the mask / lamp texture is kept as the emission map, but switched off here; VehicleLights turns it on.
            Texture emission = src.HasProperty("_EmissionMap") ? src.GetTexture("_EmissionMap") : null;
            bool dashboard = src.name.ToLowerInvariant().Contains("dashboard");
            dst.SetTexture("_EmissionMap", emission);
            if (emission != null && dashboard)
            {
                // Instrument panels glow softly all the time.
                dst.SetColor("_EmissionColor", UnityEngine.Color.white * 0.6f);
                dst.EnableKeyword("_EMISSION");
                dst.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            else
            {
                dst.SetColor("_EmissionColor", UnityEngine.Color.black);
                dst.DisableKeyword("_EMISSION");
            }

            // Cut-out decals (taxi checker): Built-in "Cutout" rendering mode = 1.
            bool cutout = src.HasProperty("_Mode") && Mathf.Approximately(src.GetFloat("_Mode"), 1f);
            dst.SetFloat("_AlphaClip", cutout ? 1f : 0f);
            dst.SetFloat("_Cutoff", 0.5f);
            if (cutout) dst.EnableKeyword("_ALPHATEST_ON"); else dst.DisableKeyword("_ALPHATEST_ON");
            dst.renderQueue = cutout ? (int)UnityEngine.Rendering.RenderQueue.AlphaTest : -1;
            dst.SetFloat("_Surface", 0f);
            dst.enableInstancing = true;
            EditorUtility.SetDirty(dst);
            return dst;
        }

        private static Material GetGlass()
        {
            EnsureFolder(TargetFolder);
            var m = AssetDatabase.LoadAssetAtPath<Material>(GlassPath);
            Shader shader = Shader.Find("OpenworldCrime/Vehicle Glass");
            if (m == null)
            {
                m = new Material(shader) { name = "Glass_Car" };
                AssetDatabase.CreateAsset(m, GlassPath);
                m.SetColor("_BaseColor", new Color(0.05f, 0.065f, 0.075f, 0.5f));
                m.SetFloat("_Reflection", 1f);
                m.SetFloat("_FresnelPower", 4f);
                m.SetFloat("_MinReflection", 0.1f);
                m.SetFloat("_Smoothness", 0.97f);
                m.SetFloat("_SpecularStrength", 1.5f);
            }
            m.shader = shader;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
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
