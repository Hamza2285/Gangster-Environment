using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class CityMirageDistinctArtwork
{
    const string Shop = "Assets/Prefeb/Cloth Shop/Cloth Shop Environment.prefab";
    const string Asset = "Assets/Texture/City Mirage/Distinct Framed Apparel Meshes.asset";
    const string Request = "Temp/CityMirageDistinctArtwork.request";
    const string Status = "Temp/CityMirageDistinctArtwork.txt";

    [InitializeOnLoadMethod] static void Queue()
    {
        if (!Application.isBatchMode && File.Exists(Request)) EditorApplication.delayCall += Run;
    }

    [MenuItem("Tools/City Mirage/Assign Distinct Apparel Posters")]
    public static void Run()
    {
        var root = PrefabUtility.LoadPrefabContents(Shop);
        try
        {
            var names = new[]{"Wall artwork | right of mirror","Wall artwork | side panel"};
            var filters = names.Select(n => root.transform.Find(n)?.GetComponent<MeshFilter>()).ToArray();
            if (filters.Any(f => !f)) throw new InvalidOperationException("Artwork placements missing.");
            var source = filters[0].sharedMesh;
            var originalUV = source.uv;
            var face = Enumerable.Range(0,originalUV.Length).Where(i => originalUV[i].x > 1.877f &&
                originalUV[i].x < 1.925f && originalUV[i].y > .57f && originalUV[i].y < .72f).ToArray();
            var existing = AssetDatabase.LoadAllAssetsAtPath(Asset).OfType<Mesh>().ToArray();
            if (existing.Length == 0 && face.Length != 4) throw new InvalidOperationException("Expected four artwork corners.");
            var styles = new[]{"Black and red streetwear","Gold lion streetwear"};
            var tiles = new[]{2,5};
            for (int i=0;i<filters.Length;i++)
            {
                var mesh = existing.Length == 2 ? existing.Single(m => m.name.EndsWith(styles[i])) : null;
                if (!mesh)
                {
                    mesh = UnityEngine.Object.Instantiate(source);
                    mesh.name = "Framed Apparel | "+styles[i];
                    var uv = (Vector2[])originalUV.Clone();
                    var low = face.Select(j=>uv[j]).Aggregate(Vector2.Min);
                    var high = face.Select(j=>uv[j]).Aggregate(Vector2.Max);
                    foreach (int j in face)
                    {
                        var q = uv[j];
                        uv[j] = new Vector2(1.625f+(tiles[i]+.03f+.94f*Mathf.InverseLerp(low.x,high.x,q.x))/16f,
                            .013f+.225f*Mathf.InverseLerp(low.y,high.y,q.y));
                    }
                    mesh.uv = uv;
                    mesh.RecalculateTangents();
                    if (i==0) AssetDatabase.CreateAsset(mesh,Asset);
                    else AssetDatabase.AddObjectToAsset(mesh,Asset);
                }
                filters[i].sharedMesh = mesh;
                PrefabUtility.RecordPrefabInstancePropertyModifications(filters[i]);
            }
            AssetDatabase.SaveAssets();
            PrefabUtility.SaveAsPrefabAsset(root,Shop,out bool saved);
            if (!saved) throw new IOException("Could not save distinct artwork.");
            File.WriteAllText(Status,"DONE: left hoodie poster, right black/red streetwear, side gold lion. Shared Clothes Atlas.");
            if (File.Exists(Request)) File.Delete(Request);
        }
        catch (Exception e) { File.WriteAllText(Status,e.ToString()); Debug.LogException(e); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
