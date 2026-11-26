using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Glossier, richer car paint on the existing OpenworldCrime/Car Paint shader (no shader change, so its cost stays the same):
    /// - writes the clear-coat / colour look below onto CarPaint_Tuning.mat and copies it (look floats only, as Sync Car Paint
    ///   Tuning does) to every Car Paint material of the final cars and the taxi, skipping the protected local files;
    /// - rebuilds the colour variants from FinalCarPaint's (richer) palette and rewires the prefabs (FinalCarPaint.WirePrefabs).
    /// Re-runnable. Menu: Tools > OpenworldCrime > Vehicles > Car Polish > Apply Paint Polish.
    /// </summary>
    public static class CarPaintPolish
    {
        private const string ShaderName = "OpenworldCrime/Car Paint";
        private const string TuningPath = "Assets/_Project/Materials/Vehicles/FinalCars/CarPaint_Tuning.mat";
        private static readonly string[] MaterialFolders = { "Assets/_Project/Materials/Vehicles/FinalCars", "Assets/_Project/Materials/Vehicles/Taxi" };

        // Local edits of other people / other tasks: never written by this tool. Talenter_MidnightBlue / Khodge_2_MidnightBlue and
        // the rest of the old MidnightBlue variants are replaced by the new NightBlue variants instead.
        private static readonly string[] ProtectedNames = { "Dodge Charger" };
        private const string ProtectedSuffix = "_MidnightBlue";

        // Clear coat: tighter (roughness 0.12 -> 0.045) and 2.5x the probe reflection at grazing angles, tinted toward the paint so
        // it reads as gloss rather than a milky veil, crisp sun highlight; base coat: more saturated with a deeper flop.
        // Same shader instructions as before (only uniforms change). Tuned against before/after shots in daylight.
        private static readonly (string property, float value)[] Look =
        {
            ("_Saturation", 1.5f),
            ("_ColorDepth", 0.32f),
            ("_ShadeLift", 0.2f),
            ("_LightCompress", 0.3f),
            ("_BaseSheen", 0.45f),
            ("_CoatF0", 0.055f),
            ("_CoatFresnelMax", 0.66f),
            ("_CoatRoughness", 0.045f),
            ("_ReflectionStrength", 1f),
            ("_HorizonStrength", 0.75f),
            ("_ReflectionTint", 0.45f),
            ("_ReflectionSaturation", 0.5f),
            ("_HorizonWithProbes", 0.3f),
            ("_SpecularStrength", 2f),
            ("_FlakeStrength", 0.6f),
        };

        private static readonly HashSet<string> PerMaterialFloats = new HashSet<string> { "_PaintRecolor", "_PaintRefLuma", "_Burnt", "_UseNormalMap" };

        [MenuItem("Tools/OpenworldCrime/Vehicles/Car Polish/Apply Paint Polish")]
        public static void ApplyMenu() => Debug.Log(Apply());

        public static bool IsProtected(Material material) =>
            material != null && (material.name.EndsWith(ProtectedSuffix) || System.Array.IndexOf(ProtectedNames, material.name) >= 0);

        public static string Apply()
        {
            Shader shader = Shader.Find(ShaderName);
            var tuning = AssetDatabase.LoadAssetAtPath<Material>(TuningPath);
            if (shader == null || tuning == null) return "[CarPaintPolish] Car Paint shader or CarPaint_Tuning.mat not found";

            var sb = new StringBuilder("[CarPaintPolish]\n");
            foreach ((string property, float value) in Look)
            {
                sb.Append($"  {property} {tuning.GetFloat(property):F3} -> {value:F3}\n");
                tuning.SetFloat(property, value);
            }
            EditorUtility.SetDirty(tuning);

            int synced = 0, skipped = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", MaterialFolders))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material == null || material == tuning || material.shader != shader) continue;
                if (IsProtected(material)) { skipped++; continue; }
                for (int i = 0; i < shader.GetPropertyCount(); i++)
                {
                    UnityEngine.Rendering.ShaderPropertyType type = shader.GetPropertyType(i);
                    if (type != UnityEngine.Rendering.ShaderPropertyType.Float && type != UnityEngine.Rendering.ShaderPropertyType.Range) continue;
                    string property = shader.GetPropertyName(i);
                    if (PerMaterialFloats.Contains(property)) continue;
                    material.SetFloat(property, tuning.GetFloat(property));
                }
                EditorUtility.SetDirty(material);
                synced++;
            }
            AssetDatabase.SaveAssets();
            sb.AppendLine($"  look copied to {synced} paint materials ({skipped} protected left alone)");

            // Colour variants follow their body material (tuning above) with FinalCarPaint's palette.
            sb.AppendLine(FinalCarPaint.WritePaletteWeights());
            sb.Append(FinalCarPaint.WirePrefabs());
            return sb.ToString();
        }
    }
}
