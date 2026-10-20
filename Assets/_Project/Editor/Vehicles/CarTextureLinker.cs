using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// The car FBX files do not embed their textures; they only reference PNG files by name (from the artist's original project).
    /// Unity's model importer links a texture automatically when a PNG with that name sits next to the FBX or in a "Textures"
    /// folder beside it (materialSearch = RecursiveUp). This tool re-imports the car models so newly copied PNGs are linked, then
    /// writes the still-missing files to Models/Vehicles/Textures/REQUIRED_TEXTURES.txt.
    /// Light materials need nothing extra: VehicleLights makes their texture the emission map, so lamps glow in their own pattern.
    /// Menu: Tools > OpenworldCrime > Vehicles > Link Car Textures.
    /// </summary>
    public static class CarTextureLinker
    {
        private const string ModelFolder = "Assets/_Project/Models/Vehicles";
        private const string TextureFolder = ModelFolder + "/Textures";
        private static readonly Regex PngName = new Regex(@"[A-Za-z0-9_\-\. ]{2,60}\.png", RegexOptions.IgnoreCase);

        [MenuItem("Tools/OpenworldCrime/Vehicles/Link Car Textures")]
        public static void LinkMenu() => Debug.Log(Link());

        public static string Link()
        {
            var models = AssetDatabase.FindAssets("t:Model", new[] { ModelFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase) && !p.Contains("Knife"))
                .ToList();

            var present = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ModelFolder }))
                present.Add(Path.GetFileName(AssetDatabase.GUIDToAssetPath(guid)));

            var required = new SortedSet<string>(System.StringComparer.OrdinalIgnoreCase);
            var perCar = new StringBuilder();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string path in models)
                {
                    var names = new SortedSet<string>(System.StringComparer.OrdinalIgnoreCase);
                    string raw = Encoding.ASCII.GetString(File.ReadAllBytes(path));
                    foreach (Match m in PngName.Matches(raw))
                    {
                        string n = m.Value.Trim();
                        int slash = n.LastIndexOfAny(new[] { '\\', '/' });
                        names.Add(slash >= 0 ? n.Substring(slash + 1) : n);
                    }
                    required.UnionWith(names);
                    var missing = names.Where(n => !present.Contains(n)).ToList();
                    perCar.AppendLine($"{Path.GetFileNameWithoutExtension(path)}: {names.Count - missing.Count}/{names.Count}" +
                                      (missing.Count > 0 ? "  missing: " + string.Join(", ", missing) : "  complete"));

                    var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                    importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                    importer.materialSearch = ModelImporterMaterialSearch.RecursiveUp;
                    importer.SaveAndReimport();
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }

            var stillMissing = required.Where(n => !present.Contains(n)).ToList();
            if (!AssetDatabase.IsValidFolder(TextureFolder)) AssetDatabase.CreateFolder(ModelFolder, "Textures");
            File.WriteAllText(TextureFolder + "/REQUIRED_TEXTURES.txt",
                "Copy the missing PNG files into this folder (or next to the FBX files), from the original project's Texture2D folder\n" +
                "(K:\\2026 Work\\September\\Projects\\Grand Theft Auto San Andreas\\Assets\\Texture2D\\),\n" +
                "then run Tools > OpenworldCrime > Vehicles > Link Car Textures.\n\n" +
                $"Present {required.Count - stillMissing.Count} / {required.Count}. Missing ({stillMissing.Count}):\n" +
                string.Join("\n", stillMissing) + "\n\nPer car:\n" + perCar);
            AssetDatabase.Refresh();
            // Parts whose texture just arrived go back to their real material; the rest keep the look-alike materials.
            string parts = CarMaterialSetup.ApplyAll();
            return $"[CarTextureLinker] textures present {required.Count - stillMissing.Count}/{required.Count}, missing {stillMissing.Count}.\n" + perCar + parts;
        }
    }
}
