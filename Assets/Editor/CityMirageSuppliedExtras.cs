using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class CityMirageSuppliedExtras
{
    const string Prefab = "Assets/Prefeb/Cloth Shop/Cloth Shop Environment.prefab";
    const string Asset = "Assets/Texture/City Mirage/Supplied Hanger and Shoe Meshes.asset";
    const string Request = "Temp/CityMirageSuppliedExtras.request";
    [Serializable] class Shape { public Vector3[] vertices, normals; public Vector2[] uv; public int[] triangles; }
    [Serializable] class Models { public Shape hanger, shoe; }

    [InitializeOnLoadMethod] static void Schedule()
    {
        if (!Application.isBatchMode && File.Exists(Request)) EditorApplication.delayCall += Run;
    }

    [MenuItem("Tools/City Mirage/Apply Supplied Hangers and Shoes")]
    public static void Run()
    {
        try
        {
            File.WriteAllText("Temp/CityMirageSuppliedExtras.txt", Apply());
            File.Delete(Request);
        }
        catch (Exception e)
        {
            File.WriteAllText("Temp/CityMirageSuppliedExtras.txt", e.ToString());
            Debug.LogException(e);
        }
    }

    static string Apply()
    {
        var root = PrefabUtility.LoadPrefabContents(Prefab);
        try
        {
            var data = JsonUtility.FromJson<Models>(File.ReadAllText("Assets/Editor/CityMirageSuppliedExtras.json"));
            var active = root.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            var hangers = active.Where(r => r.name.StartsWith("Hanger | shoulder and hook") || r.name.StartsWith("Wall bay | bent hanger")).ToArray();
            var shoes = active.Where(r => r.name == "Sale table | display sneaker").ToArray();
            if (hangers.Length < 60 || shoes.Length != 8) throw new InvalidOperationException($"Unexpected placement count: {hangers.Length} hangers, {shoes.Length} shoes.");
            var cloth = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/City Mirage/Clothes Atlas.mat");
            if (!cloth) throw new InvalidOperationException("Clothes Atlas missing.");
            if (AssetDatabase.LoadMainAssetAtPath(Asset)) throw new InvalidOperationException("Generated asset already exists.");
            bool first = true;
            foreach (var r in hangers.Concat(shoes))
            {
                bool shoe = shoes.Contains(r);
                var filter = r.GetComponent<MeshFilter>();
                var shape = shoe ? data.shoe : data.hanger;
                var b = r.bounds;
                var min = shape.vertices.Aggregate(Vector3.Min);
                var max = shape.vertices.Aggregate(Vector3.Max);
                var umin = shape.uv.Aggregate(Vector2.Min);
                var umax = shape.uv.Aggregate(Vector2.Max);
                bool side = !shoe && b.size.z > b.size.x;
                bool dark = shoe && filter.sharedMesh.uv[0].x >= 1.5625f;
                var xAxis = side ? Vector3.forward : Vector3.right;
                var yAxis = Vector3.Cross(Vector3.up, xAxis);
                float width = side ? b.size.z : b.size.x;
                float depth = side ? b.size.x : b.size.z;
                var mesh = new Mesh { name = shoe ? "Ch42 Sneakers | fitted atlas" : "025 | fitted hanger" };
                mesh.vertices = shape.vertices.Select(p =>
                {
                    float x = Mathf.InverseLerp(min.x, max.x, p.x);
                    float y = Mathf.InverseLerp(min.y, max.y, p.y);
                    float z = Mathf.InverseLerp(min.z, max.z, p.z);
                    return r.transform.InverseTransformPoint(b.center + xAxis * ((x-.5f)*width)
                        + yAxis * ((y-.5f)*depth) + Vector3.up * ((z-.5f)*b.size.y));
                }).ToArray();
                mesh.normals = shape.normals.Select(n => r.transform.InverseTransformDirection(
                    xAxis * (n.x*(max.x-min.x)/width) + yAxis * (n.y*(max.y-min.y)/depth)
                    + Vector3.up * (n.z*(max.z-min.z)/b.size.y)).normalized).ToArray();
                mesh.uv = shape.uv.Select((uv, i) =>
                {
                    float u = Mathf.InverseLerp(umin.x, umax.x, uv.x);
                    float v = Mathf.InverseLerp(umin.y, umax.y, uv.y);
                    if (!shoe) return new Vector2(1.804f+u*.017f,.368f+v*.014f);
                    float height = Mathf.InverseLerp(min.z, max.z, shape.vertices[i].z);
                    float panel = Mathf.SmoothStep(0,1,Mathf.InverseLerp(.17f,.24f,height));
                    return new Vector2(Mathf.Lerp(1.518f,dark?1.582f:1.515f,panel)+(u-.5f)*.004f,
                        Mathf.Lerp(.385f,.481f,panel)+(v-.5f)*.008f);
                }).ToArray();
                mesh.triangles = shape.triangles;
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                if (first) { AssetDatabase.CreateAsset(mesh, Asset); first = false; }
                else AssetDatabase.AddObjectToAsset(mesh, Asset);
                filter.sharedMesh = mesh;
                r.sharedMaterial = cloth;
                PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
                PrefabUtility.RecordPrefabInstancePropertyModifications(r);
            }
            if (active.SelectMany(r => r.sharedMaterials).Distinct().Count() != 3)
                throw new InvalidOperationException("Expected three materials.");
            AssetDatabase.SaveAssets();
            PrefabUtility.SaveAsPrefabAsset(root, Prefab, out bool saved);
            if (!saved) throw new IOException("Environment save failed.");
            return $"DONE: {hangers.Length} supplied 025 hangers, {shoes.Length} supplied Ch42 sneakers. Three materials.";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
