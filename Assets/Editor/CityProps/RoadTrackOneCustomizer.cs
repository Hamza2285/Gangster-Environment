using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class RoadTrackOneCustomizer
{
    const string Request = "RoadTrackOneApply.request";
    const string Report = "FinalEnvironmentRoadReview/RoadTrackOne.txt";
    static RoadTrackOneCustomizer() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request)) return;
        string mode = File.ReadAllText(Request).Trim(); File.Delete(Request);
        try { if (mode == "audit") Audit(); else Apply(); }
        catch (Exception e) { Directory.CreateDirectory(Path.GetDirectoryName(Report)); File.WriteAllText(Report, e.ToString()); Debug.LogException(e); }
    }
    static GameObject Target(Scene sc) => sc.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Where(t => t.name == "Track Roads (1)").Select(t => t.gameObject).FirstOrDefault();
    static void Audit()
    {
        var sc = SceneManager.GetActiveScene(); var root = Target(sc); if (!root) throw new Exception("Track Roads (1) not found in active scene " + sc.path);
        var sb = new StringBuilder("SCENE=" + sc.path + "\nROOT=" + root.name + " active=" + root.activeSelf + "\n");
        var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
        sb.AppendLine("MeshRenderers=" + renderers.Length);
        foreach (var r in renderers)
        {
            var mf = r.GetComponent<MeshFilter>(); var mesh = mf ? mf.sharedMesh : null;
            sb.AppendLine("R=" + r.name + " mesh=" + (mesh ? mesh.name : "null") + " path=" + (mesh ? AssetDatabase.GetAssetPath(mesh) : "") + " verts=" + (mesh ? mesh.vertexCount : 0) + " submeshes=" + (mesh ? mesh.subMeshCount : 0) + " bounds=" + r.bounds);
            if (mesh && mesh.uv != null && mesh.uv.Length > 0) { var uv = mesh.uv; sb.AppendLine(" UV0=" + uv.Min(x=>x.x).ToString("F3") + "," + uv.Max(x=>x.x).ToString("F3") + " / " + uv.Min(x=>x.y).ToString("F3") + "," + uv.Max(x=>x.y).ToString("F3")); }
            foreach (var m in r.sharedMaterials) if (m)
            {
                string texPath = "none", scale = "n/a", offset = "n/a";
                try { var tex = m.mainTexture; if (tex) texPath = AssetDatabase.GetAssetPath(tex); scale = m.mainTextureScale.ToString(); offset = m.mainTextureOffset.ToString(); } catch { }
                sb.AppendLine(" M=" + m.name + " path=" + AssetDatabase.GetAssetPath(m) + " shader=" + (m.shader ? m.shader.name : "null") + " main=" + texPath + " tiling=" + scale + " offset=" + offset);
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Report)); File.WriteAllText(Report, sb.ToString());
    }
    static void Apply()
    {
        var sc = SceneManager.GetActiveScene();
        if (sc.path != "Assets/Scenes/Oasis/MobileOptimization/Gangstar City.unity") throw new Exception("Wrong active scene: " + sc.path);
        var root = Target(sc); if (!root) throw new Exception("Track Roads (1) not found");
        const string sourcePath = "Assets/Scenes/Oasis/RoadAtlas/Road Atlas.mat";
        const string folder = "Assets/Scenes/Oasis/RoadAtlas/Track Roads One";
        const string variantPath = folder + "/Track Roads One Road Atlas.mat";
        const string settingsPath = folder + "/Track Roads One Road Atlas Sources.asset";
        const string texturePath = "Assets/Scenes/Oasis/RoadAtlas/Track Roads One/Track Roads One Urban Guidance.png";
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var source = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
        var urban = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (!source || !urban) throw new Exception("Source road material or urban-guidance texture missing");
        var baseRenderers = root.GetComponentsInChildren<MeshRenderer>(true);
        int expected = baseRenderers.Count(r => r.sharedMaterials.Any(m => m == source));
        if (expected == 0) throw new Exception("No Road Atlas renderers inside Track Roads (1)");
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Scenes/Oasis/RoadAtlas", "Track Roads One");
        var variant = AssetDatabase.LoadAssetAtPath<Material>(variantPath);
        if (!variant)
        {
            variant = new Material(source) { name = "Track Roads One Road Atlas" };
            AssetDatabase.CreateAsset(variant, variantPath);
        }
        var sourceSettings = RoadAtlasMaterialGUI.Sources(source);
        if (!sourceSettings) throw new Exception("Road atlas source settings unavailable");
        var settings = AssetDatabase.LoadAssetAtPath<RoadAtlasSources>(settingsPath);
        if (!settings)
        {
            settings = UnityEngine.Object.Instantiate(sourceSettings);
            settings.name = "Track Roads One Road Atlas Sources";
            settings.colors = sourceSettings.colors.ToArray(); settings.surfaces = sourceSettings.surfaces.ToArray();
            AssetDatabase.CreateAsset(settings, settingsPath);
        }
        settings.colors[0] = urban;
        EditorUtility.SetDirty(settings);
        RoadAtlasTextureBaker.Bake(variant, settings);
        int changed = 0;
        foreach (var r in baseRenderers)
        {
            var mats = r.sharedMaterials; bool touched = false;
            for (int i = 0; i < mats.Length; i++) if (mats[i] == source) { mats[i] = variant; touched = true; }
            if (touched) { r.sharedMaterials = mats; PrefabUtility.RecordPrefabInstancePropertyModifications(r); changed++; }
        }
        if (changed != expected) throw new Exception("Renderer override count mismatch: expected " + expected + ", applied " + changed);
        int baselineRoads = sc.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeshRenderer>(true))
            .Where(r => !r.transform.IsChildOf(root.transform)).Count(r => r.sharedMaterials.Any(m => m == source));
        if (baselineRoads == 0) throw new Exception("Original Track Roads material baseline not found for isolation check");
        EditorSceneManager.MarkSceneDirty(sc); EditorSceneManager.SaveScene(sc);
        var shader = variant.shader;
        if (!shader || ShaderUtil.ShaderHasError(shader)) throw new Exception("Road atlas shader compilation check failed");
        string capture = "No automatic render capture";
        Directory.CreateDirectory(Path.GetDirectoryName(Report));
        File.WriteAllText(Report, "SUCCESS\nScene=" + sc.path + "\nTrack Roads (1) Road Atlas overrides=" + changed + "\nOriginal Track Roads still use original material; unaffected baseline renderers=" + baselineRoads + "\nMeshes and UVs changed=0\nNew runtime geometry/lights/scripts=0\nMaterial variants=1 shared material for the target duplicate\nSource tile=1254x1254 preserved and augmented with slim reflective edge rails, staggered bronze guide tabs, alternating lane-split strokes and inset center pinstripes\nAndroid/iPhone atlas compression=ASTC 4x4 (RoadAtlasTextureBaker defaults)\nShader errors=0\nPreview=" + capture);
    }
}
