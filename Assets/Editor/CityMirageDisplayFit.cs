using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class CityMirageDisplayFit
{
    const string Prefab = "Assets/Prefeb/Cloth Shop/Cloth Shop Environment.prefab";
    const string Folder = "Assets/Texture/City Mirage/";
    const string Request = "Temp/CityMirageDisplayFit.request";
    [InitializeOnLoadMethod] static void Schedule()
    {
        if (!Application.isBatchMode && File.Exists(Request)) EditorApplication.delayCall += Run;
    }

    [MenuItem("Tools/City Mirage/Fit Hangers and Color Sneakers")]
    public static void Run()
    {
        var root = PrefabUtility.LoadPrefabContents(Prefab);
        try
        {
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            var hangers = renderers.Where(r => r.name.StartsWith("Hanger | shoulder and hook") || r.name.StartsWith("Wall bay | bent hanger")).ToArray();
            var shoes = renderers.Where(r => r.name == "Sale table | display sneaker").ToArray();
            var shirts = root.GetComponentsInChildren<LODGroup>(true).Where(g => g.name.StartsWith("025 Hanging Shirt |"))
                .Select(g => g.GetLODs()[0].renderers[0].bounds).ToArray();
            if (hangers.Length != 68 || shoes.Length != 8 || shirts.Length != 68)
                throw new InvalidOperationException($"Unexpected display counts: {hangers.Length}/{shoes.Length}/{shirts.Length}.");
            if (renderers.SelectMany(r => r.sharedMaterials).Distinct().Count() != 3)
                throw new InvalidOperationException("Expected three shared materials.");
            const string meshAsset = Folder + "Fitted 025 Hangers and Blue Red Shoes.asset";
            if (AssetDatabase.LoadMainAssetAtPath(meshAsset)) throw new InvalidOperationException("Fitted mesh asset already exists.");
            var source = JsonUtility.FromJson<Models>(File.ReadAllText("Assets/Editor/CityMirageSuppliedExtras.json"));
            if (source.shoe.vertices.Length != shoes[0].GetComponent<MeshFilter>().sharedMesh.vertexCount)
                throw new InvalidOperationException("Unexpected shoe vertex layout.");
            var atlasPath = Folder + "Clothes Shared Atlas Base Color.png";
            var atlas = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!atlas.LoadImage(File.ReadAllBytes(atlasPath)) || atlas.width != 4096 || atlas.height != 2048)
                throw new InvalidOperationException("Expected 4096 × 2048 Clothes Atlas.");
            Paint(atlas, false);
            Paint(atlas, true);
            var matched = shirts.ToList();
            bool first = true;
            foreach (var r in hangers.Concat(shoes))
            {
                var filter = r.GetComponent<MeshFilter>();
                var mesh = UnityEngine.Object.Instantiate(filter.sharedMesh);
                if (shoes.Contains(r))
                {
                    bool red = Array.IndexOf(shoes.OrderBy(s => s.bounds.center.x).ToArray(), r) % 4 >= 2;
                    var min = source.shoe.vertices.Aggregate(Vector3.Min);
                    var max = source.shoe.vertices.Aggregate(Vector3.Max);
                    mesh.name = red ? "Ch42 | red sneaker" : "Ch42 | blue sneaker";
                    mesh.uv = source.shoe.vertices.Select(p => new Vector2(
                        1.5f + (red ? .0625f : 0f) + .004f + .0545f * Mathf.InverseLerp(min.y,max.y,p.y),
                        .381f + .113f * Mathf.InverseLerp(min.z,max.z,p.z))).ToArray();
                }
                else
                {
                    var b = r.bounds;
                    var shirt = matched.OrderBy(s => (new Vector3(s.center.x,s.max.y,s.center.z)-b.center).sqrMagnitude).First();
                    matched.Remove(shirt);
                    bool alongX = shirt.size.x > shirt.size.z;
                    var width = alongX ? Vector3.right : Vector3.forward;
                    var depth = alongX ? Vector3.forward : Vector3.right;
                    float halfWidth = Vector3.Dot(shirt.extents,width)*.66f;
                    float halfDepth = Vector3.Dot(shirt.extents,depth)*.38f;
                    float hookBase = Mathf.Lerp(b.min.y,b.max.y,.63f);
                    mesh.name = "025 | hanger fitted inside collar";
                    mesh.vertices = mesh.vertices.Select(v =>
                    {
                        var world = r.transform.TransformPoint(v);
                        float blend = 1f-Mathf.SmoothStep(0,1,Mathf.InverseLerp(hookBase,b.max.y,world.y));
                        float x = Mathf.Clamp(Vector3.Dot(world-b.center,width)*.60f,-halfWidth,halfWidth);
                        float z = Mathf.Clamp(Vector3.Dot(world-b.center,depth)*.35f,-halfDepth,halfDepth);
                        var fitted = new Vector3(shirt.center.x, Mathf.Min(world.y,shirt.max.y-.012f),shirt.center.z) + width*x + depth*z;
                        return r.transform.InverseTransformPoint(Vector3.Lerp(world,fitted,blend));
                    }).ToArray();
                    mesh.RecalculateNormals();
                }
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                if (first) { AssetDatabase.CreateAsset(mesh,meshAsset); first = false; }
                else AssetDatabase.AddObjectToAsset(mesh,meshAsset);
                filter.sharedMesh = mesh;
                PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
            }
            AssetDatabase.SaveAssets();
            PrefabUtility.SaveAsPrefabAsset(root,Prefab,out bool saved);
            if (!saved) throw new IOException("Could not save display fit.");
            File.WriteAllBytes(atlasPath,atlas.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(atlas);
            AssetDatabase.ImportAsset(atlasPath,ImportAssetOptions.ForceUpdate);
            File.WriteAllText("Temp/CityMirageDisplayFit.txt","DONE: 68 fitted hangers and 8 sneaker wraps in the existing Clothes Atlas.");
            if (File.Exists(Request)) File.Delete(Request);
        }
        catch (Exception e) { File.WriteAllText("Temp/CityMirageDisplayFit.txt",e.ToString()); Debug.LogException(e); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    [Serializable] class Shape { public Vector3[] vertices; }
    [Serializable] class Models { public Shape shoe; }

    static void Paint(Texture2D atlas, bool red)
    {
        int x0 = red ? 2304 : 2048;
        const int width = 256, height = 256;
        var pixels = new Color32[width*height];
        var main = red ? new Color(.69f,.085f,.12f) : new Color(.06f,.29f,.72f);
        var rubber = new Color(.88f,.89f,.89f);
        var tread = new Color(.08f,.088f,.10f);
        for (int y=0;y<height;y++)
        for (int x=0;x<width;x++)
        {
            float u = Mathf.Clamp01((x-16.384f)/223.232f);
            float v = Mathf.Clamp01((y-12.288f)/231.424f);
            float band = Mathf.Abs(v-(.29f+.34f*u));
            float branch = Mathf.Abs(v-(.73f-.70f*u));
            bool accent = u>.19f && u<.78f && (band<.052f || branch<.045f);
            bool seam = Mathf.Abs(v-(.24f+.10f*Mathf.Cos(u*7)))<.007f || Mathf.Abs(u-.79f+.08f*v)<.009f;
            float weave = .94f+.055f*Mathf.Sin(x*1.43f)*Mathf.Sin(y*1.68f);
            var color = v<.055f ? tread : v<.19f ? rubber : accent ? rubber*.91f : main*weave;
            if (seam && v>.20f && !accent) color *= (x+y)%9<4 ? .57f : .83f;
            color.a = 1;
            pixels[y*width+x] = color;
        }
        atlas.SetPixels32(x0,768,width,height,pixels);
    }
}

