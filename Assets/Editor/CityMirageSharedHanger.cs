using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class CityMirageSharedHanger
{
    const string Shop = "Assets/Prefeb/Cloth Shop/Cloth Shop Environment.prefab";
    const string Shirt = "Assets/Prefeb/Cloth Shop/Reusable/025 Hanging Shirt.prefab";
    const string Request = "Temp/CityMirageSharedHanger.request";
    const string Status = "Temp/CityMirageSharedHanger.txt";
    const string MeshPath = "Assets/Texture/City Mirage/025 Shared Shirt Hanger.asset";

    [Serializable] class Shape { public Vector3[] vertices, normals; public Vector2[] uv; public int[] triangles; }
    [Serializable] class Source { public Shape hanger, hanging; }

    [InitializeOnLoadMethod] static void Schedule()
    {
        if (!Application.isBatchMode && File.Exists(Request)) EditorApplication.delayCall += Run;
    }

    [MenuItem("Tools/City Mirage/Attach Hanger to Shared Shirt")]
    public static void Run()
    {
        try
        {
            File.WriteAllText(Status, Apply());
            if (File.Exists(Request)) File.Delete(Request);
        }
        catch (Exception e) { File.WriteAllText(Status,e.ToString()); Debug.LogException(e); }
    }

    static string Apply()
    {
        var shop = PrefabUtility.LoadPrefabContents(Shop);
        var shirt = PrefabUtility.LoadPrefabContents(Shirt);
        try
        {
            var instances = shop.GetComponentsInChildren<LODGroup>(true)
                .Where(g => AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(g.gameObject)) == Shirt).ToArray();
            var old = shop.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy &&
                (r.name.StartsWith("Hanger | shoulder and hook") || r.name.StartsWith("Wall bay | bent hanger"))).ToArray();
            if (instances.Length != 68 || (old.Length != 68 && old.Length != 0))
                throw new InvalidOperationException($"Unexpected scene layout: {instances.Length} shirts and {old.Length} separate hangers.");
            var child = shirt.transform.Find("Hanger");
            if (!child)
            {
                var cloth = JsonUtility.FromJson<Source>(File.ReadAllText("Assets/Editor/CityMirageSuppliedCloth.json")).hanging;
                var hanger = JsonUtility.FromJson<Source>(File.ReadAllText("Assets/Editor/CityMirageSuppliedExtras.json")).hanger;
                var min = cloth.vertices.Aggregate(Vector3.Min);
                var max = cloth.vertices.Aggregate(Vector3.Max);
                var pivot = new Vector3((min.x+max.x)*.5f,(min.y+max.y)*.5f,max.z);
                float height = max.z-min.z;
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
                if (!mesh)
                {
                    mesh = new Mesh { name = "025 shared hanger" };
                    mesh.vertices = hanger.vertices.Select(p => Convert(p-pivot)/height).ToArray();
                    mesh.normals = hanger.normals.Select(Convert).ToArray();
                    mesh.triangles = hanger.triangles;
                    var uvMin = hanger.uv.Aggregate(Vector2.Min);
                    var uvMax = hanger.uv.Aggregate(Vector2.Max);
                    mesh.uv = hanger.uv.Select(u => new Vector2(1.804f+.017f*Mathf.InverseLerp(uvMin.x,uvMax.x,u.x),
                        .368f+.014f*Mathf.InverseLerp(uvMin.y,uvMax.y,u.y))).ToArray();
                    mesh.RecalculateTangents();
                    mesh.RecalculateBounds();
                    AssetDatabase.CreateAsset(mesh,MeshPath);
                }
                child = new GameObject("Hanger").transform;
                child.SetParent(shirt.transform,false);
                child.GetOrAddMesh(mesh);
                var lod = shirt.GetComponent<LODGroup>();
                var levels = lod.GetLODs();
                var renderer = child.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = levels[0].renderers[0].sharedMaterial;
                for (int i=0;i<levels.Length;i++) levels[i].renderers = levels[i].renderers.Append(renderer).ToArray();
                lod.SetLODs(levels);
                lod.RecalculateBounds();
                PrefabUtility.SaveAsPrefabAsset(shirt,Shirt,out bool savedShirt);
                if (!savedShirt) throw new IOException("Could not save shirt prefab.");
            }
            foreach (var renderer in old)
            {
                renderer.enabled = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            if (old.Length > 0)
            {
                PrefabUtility.SaveAsPrefabAsset(shop,Shop,out bool savedShop);
                if (!savedShop) throw new IOException("Could not save shop prefab.");
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(shirt); PrefabUtility.UnloadPrefabContents(shop); }
        var verify = PrefabUtility.LoadPrefabContents(Shop);
        try
        {
            int linked = verify.GetComponentsInChildren<LODGroup>(true).Count(g =>
                AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(g.gameObject)) == Shirt &&
                g.transform.Find("Hanger") && g.transform.Find("Hanger").GetComponent<MeshRenderer>().enabled);
            if (linked != 68) throw new InvalidOperationException($"Expected 68 inherited hangers, found {linked}.");
            return $"DONE: {linked} linked shirts inherit the editable Hanger child from the shared source prefab.";
        }
        finally { PrefabUtility.UnloadPrefabContents(verify); }
    }

    static Vector3 Convert(Vector3 p) => new Vector3(p.x,p.z,-p.y);

    static void GetOrAddMesh(this Transform child, Mesh mesh)
    {
        child.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        child.gameObject.AddComponent<MeshRenderer>();
    }
}
