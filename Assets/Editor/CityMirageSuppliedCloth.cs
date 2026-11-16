using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
public static class CityMirageSuppliedCloth
{
    const string Prefab="Assets/Prefeb/Cloth Shop/Cloth Shop Environment.prefab";
    const string Folder="Assets/Prefeb/Cloth Shop/Reusable/";
    const string Asset="Assets/Texture/City Mirage/Supplied Shirt Atlas Meshes.asset";
    const string Request="Temp/CityMirageSuppliedCloth.request";
    const string Status="Temp/CityMirageSuppliedCloth.txt";
    [Serializable] class Shape {public Vector3[] vertices,normals;public Vector2[] uv;public int[] triangles;}
    [Serializable] class Models {public Shape hanging,folded,hangingLow,foldedLow;}
    static bool first;
    [InitializeOnLoadMethod] static void Schedule()
    {if(!Application.isBatchMode&&File.Exists(Request))EditorApplication.delayCall+=Run;}
    [MenuItem("Tools/City Mirage/Use Supplied Shirt Prefabs")]
    public static void Run()
    {
        try{File.WriteAllText(Status,Apply());File.Delete(Request);}
        catch(Exception e){File.WriteAllText(Status,e.ToString());Debug.LogException(e);}
    }
    static string Apply()
    {
        var root=PrefabUtility.LoadPrefabContents(Prefab);
        try
        {
            var cloth=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/City Mirage/Clothes Atlas.mat");
            if(!cloth)throw new InvalidOperationException("Clothes Atlas not found.");
            var renderers=root.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
            var hanging=renderers.Where(r=>r.name.StartsWith("T-shirt |")||r.name.StartsWith("Button-down |")||
                r.name.StartsWith("Casual shirt |")||r.name.StartsWith("Hoodie |")).ToArray();
            var folded=renderers.Where(r=>r.name.StartsWith("Folded garment |")||r.name.StartsWith("Folded apparel | stack")).ToArray();
            if(hanging.Length==0&&folded.Length==0&&AssetDatabase.LoadMainAssetAtPath(Asset))return "DONE (already applied)";
            if(hanging.Length<60||folded.Length<100)throw new InvalidOperationException($"Unexpected placements: {hanging.Length} hanging, {folded.Length} folded.");
            foreach(var r in hanging.Concat(folded))
                if(r.transform.childCount!=0||r.GetComponents<Component>().Any(c=>!(c is Transform||c is MeshFilter||c is MeshRenderer)))
                    throw new InvalidOperationException("Unexpected components on "+r.name);
            var models=JsonUtility.FromJson<Models>(File.ReadAllText("Assets/Editor/CityMirageSuppliedCloth.json"));
            if(models?.hangingLow?.vertices==null||models.foldedLow?.vertices==null)throw new InvalidOperationException("Source shirt data missing.");
            if(AssetDatabase.LoadMainAssetAtPath(Asset))throw new InvalidOperationException("An existing generated mesh asset needs inspection.");
            first=true;
            var hangingMeshes=Variants(models.hanging,models.hangingLow,true);
            var foldedMeshes=Variants(models.folded,models.foldedLow,false);
            var hp=MakePrefab("025 Hanging Shirt",hangingMeshes[0],cloth,.65f);
            var fp=MakePrefab("Folded Company Shirt",foldedMeshes[0],cloth,.065f);
            var useH=Replace(hanging,hp,hangingMeshes,true,root);
            var useF=Replace(folded,fp,foldedMeshes,false,root);
            var used=root.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy)
                .SelectMany(r=>r.sharedMaterials).Distinct().Count();
            if(used!=3)throw new InvalidOperationException($"Expected 3 materials after replacement, got {used}.");
            AssetDatabase.SaveAssets();
            PrefabUtility.SaveAsPrefabAsset(root,Prefab,out var saved);
            if(!saved)throw new IOException("Could not save the supplied shirt prefab instances.");
            return $"DONE: {useH} copies of one 025 Hanging Shirt prefab; {useF} copies of one Folded Company Shirt prefab. Six shared atlas color meshes and one distance LOD per type. Three materials verified. LOD0/LOD1 triangles: hanging {models.hanging.triangles.Length/3}/{models.hangingLow.triangles.Length/3}; folded {models.folded.triangles.Length/3}/{models.foldedLow.triangles.Length/3}.";
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }
    static Vector3 Convert(Vector3 v)=>new Vector3(v.x,v.z,-v.y);
    static Mesh[][] Variants(Shape high,Shape low,bool hanging)
    {
        var points=high.vertices.Select(Convert).ToArray();
        var b=new Bounds(points[0],Vector3.zero);
        foreach(var p in points)b.Encapsulate(p);
        var pivot=new Vector3(b.center.x,hanging?b.max.y:b.min.y,b.center.z);
        var min=high.uv.Aggregate(Vector2.Min);var max=high.uv.Aggregate(Vector2.Max);
        var result=new Mesh[6][];
        for(int color=0;color<6;color++)
        {
            result[color]=new[]{MakeMesh(high,pivot,b.size.y,min,max,color,hanging,0),
                MakeMesh(low,pivot,b.size.y,min,max,color,hanging,1)};
        }
        return result;
    }
    static Mesh MakeMesh(Shape shape,Vector3 pivot,float h,Vector2 min,Vector2 max,int color,bool hanging,int lod)
    {
        var m=new Mesh{name=(hanging?"025 hanging shirt":"Folded Company Shirt")+$" | color {color+1} LOD{lod}"};
        m.vertices=shape.vertices.Select(v=>(Convert(v)-pivot)/h).ToArray();
        m.normals=shape.normals.Select(Convert).ToArray();
        m.triangles=shape.triangles;
        m.uv=shape.uv.Select(v=>
        {
            var uv=new Vector2(Mathf.Clamp01((v.x-min.x)/Mathf.Max(.001f,max.x-min.x)),
                Mathf.Clamp01((v.y-min.y)/Mathf.Max(.001f,max.y-min.y)));
            return new Vector2(1.625f+(color+.035f+uv.x*.93f)/16f,.008f+uv.y*.235f);
        }).ToArray();
        m.RecalculateTangents();m.RecalculateBounds();
        if(first){AssetDatabase.CreateAsset(m,Asset);first=false;}else AssetDatabase.AddObjectToAsset(m,Asset);
        return m;
    }
    static GameObject MakePrefab(string name,Mesh[] meshes,Material material,float height)
    {
        var temp=new GameObject(name);
        try
        {
            temp.transform.localScale=Vector3.one*height;
            var rs=new Renderer[2];
            for(int i=0;i<2;i++)
            {
                var child=new GameObject("LOD"+i);
                child.transform.SetParent(temp.transform,false);
                child.AddComponent<MeshFilter>().sharedMesh=meshes[i];
                var r=child.AddComponent<MeshRenderer>();
                r.sharedMaterial=material;rs[i]=r;
            }
            var lod=temp.AddComponent<LODGroup>();
            lod.SetLODs(new[]{new LOD(.16f,new[]{rs[0]}),new LOD(.008f,new[]{rs[1]})});
            lod.RecalculateBounds();
            var prefab=PrefabUtility.SaveAsPrefabAsset(temp,Folder+name+".prefab",out bool saved);
            if(!saved||!prefab)throw new IOException("Could not save source prefab "+name);
            return prefab;
        }
        finally{UnityEngine.Object.DestroyImmediate(temp);}
    }
    static int Replace(MeshRenderer[] renderers,GameObject source,Mesh[][] variants,bool hanging,GameObject environment)
    {
        int count=0;
        foreach(var r in renderers.OrderBy(r=>r.name,StringComparer.Ordinal).ThenBy(r=>r.bounds.center.x).ThenBy(r=>r.bounds.center.y))
        {
            var old=r.gameObject;
            var b=r.bounds;
            var parent=old.transform.parent;
            int sibling=old.transform.GetSiblingIndex();
            bool side=old.name.Contains("wall")&&Mathf.Abs(b.center.x)>8;
            var facing=side?Quaternion.Euler(0,-90,0):Quaternion.identity;
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(source,environment.scene);
            instance.transform.SetPositionAndRotation(new Vector3(b.center.x,hanging?b.max.y:b.min.y,b.center.z),facing);
            instance.transform.localScale=Vector3.one*b.size.y;
            instance.transform.SetParent(parent,true);
            instance.transform.SetSiblingIndex(sibling);
            instance.name=source.name+" | "+(++count).ToString("000");
            int color=(count*5+count/5)%6;
            for(int lod=0;lod<2;lod++)
            {
                var filter=instance.transform.GetChild(lod).GetComponent<MeshFilter>();
                filter.sharedMesh=variants[color][lod];
                PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
            if(PrefabUtility.GetCorrespondingObjectFromSource(instance)!=source)
                throw new InvalidOperationException("Prefab link missing for "+instance.name);
            UnityEngine.Object.DestroyImmediate(old);
        }
        return count;
    }
}
