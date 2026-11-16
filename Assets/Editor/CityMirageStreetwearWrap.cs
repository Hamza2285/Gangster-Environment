using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class CityMirageStreetwearWrap
{
    const string Shop = "Assets/Prefeb/Cloth Shop/Cloth Shop Environment.prefab";
    const string Meshes = "Assets/Texture/City Mirage/Supplied Shirt Atlas Meshes.asset";
    const string Request = "Temp/CityMirageStreetwearWrap.request";
    const string Status = "Temp/CityMirageStreetwearWrap.txt";

    [InitializeOnLoadMethod] static void Queue()
    {
        if (!Application.isBatchMode && File.Exists(Request)) EditorApplication.delayCall += Run;
    }

    [MenuItem("Tools/City Mirage/Wrap Streetwear by Display")]
    public static void Run()
    {
        try
        {
            File.WriteAllText(Status,Apply());
            if (File.Exists(Request)) File.Delete(Request);
        }
        catch(Exception e) { File.WriteAllText(Status,e.ToString()); Debug.LogException(e); }
    }

    static string Apply()
    {
        var root = PrefabUtility.LoadPrefabContents(Shop);
        try
        {
            var all = AssetDatabase.LoadAllAssetsAtPath(Meshes).OfType<Mesh>().ToDictionary(m => m.name);
            var shirts = root.GetComponentsInChildren<LODGroup>(true)
                .Where(g => g.name.StartsWith("025 Hanging Shirt |") || g.name.StartsWith("Folded Company Shirt |")).ToArray();
            if (shirts.Length != 202 || all.Count != 24)
                throw new InvalidOperationException($"Unexpected asset layout: {shirts.Length} shirts, {all.Count} variant meshes.");
            var displays = shirts.GroupBy(g => (g.transform.parent,
                band: Mathf.RoundToInt(g.transform.position.y/.25f),
                hanging: g.name.StartsWith("025 Hanging Shirt |"))).ToArray();
            var names = new[]{"Folded Company Shirt","025 hanging shirt"};
            const string firstMesh = "025 hanging shirt | color 1 LOD0";
            if (!all.ContainsKey(firstMesh))
            {
                var main = AssetDatabase.LoadMainAssetAtPath(Meshes) as Mesh;
                if (!main) throw new InvalidOperationException("Missing first shirt mesh.");
                all.Add(firstMesh,main);
            }
            foreach (var family in names)
            for (int color=1;color<=6;color++)
            for (int lod=0;lod<2;lod++)
                if (!all.ContainsKey($"{family} | color {color} LOD{lod}"))
                    throw new InvalidOperationException($"Missing variant {family}, {color}, LOD{lod}.");
            foreach (var family in names)
            for (int color=0;color<6;color++)
            {
                var high = all[$"{family} | color {color+1} LOD0"];
                var b = high.bounds;
                bool hanging = family == names[1];
                for (int lod=0;lod<2;lod++)
                {
                    var mesh = all[$"{family} | color {color+1} LOD{lod}"];
                    mesh.uv = mesh.vertices.Select(p =>
                    {
                        float u = Mathf.InverseLerp(b.min.x,b.max.x,p.x);
                        float v = hanging ? Mathf.InverseLerp(b.min.y,b.max.y,p.y) : Mathf.InverseLerp(b.min.z,b.max.z,p.z);
                        float depth = hanging ? Mathf.InverseLerp(b.min.z,b.max.z,p.z) : Mathf.InverseLerp(b.min.y,b.max.y,p.y);
                        u = Mathf.Clamp01(.045f + u*.91f + (depth-.5f)*.035f);
                        return new Vector2(1.625f+(color+.035f+u*.93f)/16f,.008f+(.035f+v*.93f)*.235f);
                    }).ToArray();
                    mesh.RecalculateTangents();
                    EditorUtility.SetDirty(mesh);
                }
            }
            int display = 0;
            foreach (var row in displays.OrderBy(r=>r.Key.parent?.name).ThenBy(r=>r.Key.band))
            {
                int item = 0;
                foreach (var g in row.OrderBy(s=>s.transform.position.x).ThenBy(s=>s.transform.position.z).ThenBy(s=>s.transform.position.y))
                {
                    int color = (display + item++ * 5) % 6 + 1;
                    string family = names[row.Key.hanging?1:0];
                    for (int lod=0;lod<2;lod++)
                    {
                        var filter = g.transform.Find("LOD"+lod).GetComponent<MeshFilter>();
                        filter.sharedMesh = all[$"{family} | color {color} LOD{lod}"];
                        PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
                    }
                }
                display++;
            }
            AssetDatabase.SaveAssets();
            PrefabUtility.SaveAsPrefabAsset(root,Shop,out bool saved);
            if (!saved) throw new IOException("Could not save the shop display.");
            return $"DONE: {shirts.Length} linked shirts, {display} display groups, six existing-atlas wraps. Geometry and materials retained.";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}

