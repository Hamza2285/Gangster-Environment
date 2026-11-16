using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class CityMirageLacesAndRailFit
{
    const string Request = "Temp/CityMirageLacesAndRailFit.request";
    const string Status = "Temp/CityMirageLacesAndRailFit.txt";
    const string Shop = "Assets/Prefeb/Cloth Shop/Cloth Shop Environment.prefab";
    const string Asset = "Assets/Texture/City Mirage/Crisscross White Sneaker Laces.asset";
    [Serializable] class Shape { public Vector3[] vertices; public int[] triangles; }
    [Serializable] class Data { public Shape shoe; }

    [InitializeOnLoadMethod] static void Queue()
    {
        if (!Application.isBatchMode && File.Exists(Request)) EditorApplication.delayCall += Run;
    }

    [MenuItem("Tools/City Mirage/Fit Shirts to Rails and Add White Laces")]
    public static void Run()
    {
        try
        {
            File.WriteAllText(Status, Apply());
            if (File.Exists(Request)) File.Delete(Request);
        }
        catch (Exception e) { File.WriteAllText(Status, e.ToString()); Debug.LogException(e); }
    }

    static string Apply()
    {
        var root = PrefabUtility.LoadPrefabContents(Shop);
        try
        {
            var rs = root.GetComponentsInChildren<MeshRenderer>(true);
            var old = rs.Where(r => !r.enabled && (r.name.StartsWith("Hanger | shoulder and hook") ||
                r.name.StartsWith("Wall bay | bent hanger"))).ToList();
            var shirts = root.GetComponentsInChildren<LODGroup>(true)
                .Where(g => g.name.StartsWith("025 Hanging Shirt |") && g.transform.Find("Hanger")).ToArray();
            var shoes = rs.Where(r => r.enabled && r.name == "Sale table | display sneaker").ToArray();
            if (old.Count != 68 || shirts.Length != 68 || shoes.Length != 8)
                throw new InvalidOperationException($"Expected 68 rails, 68 shirts, 8 shoes. Found {old.Count}/{shirts.Length}/{shoes.Length}.");
            var source = JsonUtility.FromJson<Data>(File.ReadAllText("Assets/Editor/CityMirageSuppliedExtras.json")).shoe;
            if (shoes.Any(r => r.GetComponent<MeshFilter>().sharedMesh.vertexCount < source.vertices.Length))
                throw new InvalidOperationException("Unexpected sneaker mesh.");
            if (AssetDatabase.LoadMainAssetAtPath(Asset)) throw new InvalidOperationException("White lace meshes already exist.");
            foreach (var shirt in shirts)
            {
                var hook = shirt.transform.Find("Hanger").GetComponent<MeshRenderer>().bounds;
                var anchor = old.OrderBy(r => (r.bounds.center - hook.center).sqrMagnitude).First();
                old.Remove(anchor);
                float lift = anchor.bounds.max.y - hook.max.y;
                if (Mathf.Abs(lift) > .24f) throw new InvalidOperationException("Rail match is too far from " + shirt.name);
                shirt.transform.position += Vector3.up * lift;
                PrefabUtility.RecordPrefabInstancePropertyModifications(shirt.transform);
            }

            bool first = true;
            foreach (var shoe in shoes)
            {
                var filter = shoe.GetComponent<MeshFilter>();
                var original = filter.sharedMesh;
                var mesh = AddLaces(original, source, shoe.transform);
                if (first) { AssetDatabase.CreateAsset(mesh, Asset); first = false; }
                else AssetDatabase.AddObjectToAsset(mesh, Asset);
                filter.sharedMesh = mesh;
                PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
            }
            AssetDatabase.SaveAssets();
            PrefabUtility.SaveAsPrefabAsset(root, Shop, out bool saved);
            if (!saved) throw new IOException("Could not save shop prefab.");
            return "DONE: 8 sneakers use white crisscross atlas laces, and all 68 linked shirts have their hooks aligned to their rail positions.";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static Mesh AddLaces(Mesh original, Shape source, Transform shoe)
    {
        var basePositions = original.vertices;
        var positions = basePositions.Take(source.vertices.Length).ToList();
        var normals = original.normals.Take(source.vertices.Length).ToList();
        var uv = original.uv.Take(source.vertices.Length).ToList();
        var triangles = source.triangles.ToList();
        var x = source.vertices.Select(v => v.x).ToArray();
        float center = (x.Min()+x.Max())*.5f;
        float whiteX = uv[0].x > 1.5625f ? 1.594f : 1.5315f;
        for (int lace = 0; lace < 8; lace++)
        {
            int start = positions.Count;
            float halfWidth = .026f - (lace/2)*.0012f;
            for (int segment = 0; segment <= 8; segment++)
            {
                float t = segment/8f;
                float sx = center + Mathf.Lerp(-halfWidth, halfWidth, t);
                float sy = -.038f - (lace/2)*.016f + (t-.5f)*.016f*(lace%2==0 ? 1 : -1);
                for (int edge=0; edge<2; edge++)
                {
                    var point = TopAt(sx,sy+(edge-.5f)*.006f,source,basePositions);
                    positions.Add(point + shoe.InverseTransformVector(Vector3.up*(lace%2==0?.003f:.0042f)));
                    normals.Add(shoe.InverseTransformDirection(Vector3.up));
                    uv.Add(new Vector2(whiteX+(t-.5f)*.003f,.393f+edge*.003f));
                }
                if (segment == 0) continue;
                int a = start + (segment-1)*2;
                triangles.AddRange(new[]{a,a+2,a+1,a+1,a+2,a+3,a,a+1,a+2,a+1,a+3,a+2});
            }
        }
        var mesh = new Mesh { name = original.name + " | white laces", indexFormat = IndexFormat.UInt16 };
        mesh.SetVertices(positions);
        mesh.SetNormals(normals);
        mesh.SetUVs(0,uv);
        mesh.SetTriangles(triangles,0);
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    static Vector3 TopAt(float x,float y,Shape source,Vector3[] positions)
    {
        float highest = float.NegativeInfinity;
        Vector3 result = default;
        for (int i=0;i<source.triangles.Length;i+=3)
        {
            int a=source.triangles[i],b=source.triangles[i+1],c=source.triangles[i+2];
            var p=source.vertices[a]; var q=source.vertices[b]-p; var r=source.vertices[c]-p;
            float det=q.x*r.y-q.y*r.x;
            if (Mathf.Abs(det)<1e-9f) continue;
            float u=((x-p.x)*r.y-(y-p.y)*r.x)/det;
            float v=(q.x*(y-p.y)-q.y*(x-p.x))/det;
            if (u<0 || v<0 || u+v>1) continue;
            float z=p.z+u*q.z+v*r.z;
            if (z<=highest) continue;
            highest=z;
            result=positions[a]*(1-u-v)+positions[b]*u+positions[c]*v;
        }
        if (float.IsNegativeInfinity(highest)) throw new InvalidOperationException("Lace sample fell outside sneaker.");
        return result;
    }
}


