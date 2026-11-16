using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CityMiragePrefabs
{
    const string ScenePath="Assets/Scenes/Cloth Shop.unity";
    const string Folder="Assets/Prefeb/Cloth Shop";
    const string Request="Temp/CityMiragePrefabs.request";
    const string Status="Temp/CityMiragePrefabs.txt";
    const string Garments="Assets/Texture/City Mirage/Hanging Garments.asset";
    [InitializeOnLoadMethod] static void Schedule()
    {
        if(!Application.isBatchMode&&File.Exists(Request))EditorApplication.update+=WhenReady;
    }
    static void WhenReady()
    {
        if(EditorApplication.isCompiling||EditorApplication.isPlayingOrWillChangePlaymode)return;
        var scene=SceneManager.GetSceneByPath(ScenePath);
        if(scene.IsValid()&&scene.isDirty)return;
        EditorApplication.update-=WhenReady;
        Run();
    }
    [MenuItem("Tools/City Mirage/Build Reusable Shop Prefabs")]
    public static void Run()
    {
        try{File.WriteAllText(Status,Build());File.Delete(Request);}
        catch(Exception e){File.WriteAllText(Status,e.ToString());Debug.LogException(e);}
    }
    static string Build()
    {
        var scene=SceneManager.GetSceneByPath(ScenePath);
        if(scene.IsValid()&&scene.isDirty)throw new InvalidOperationException("Save Cloth Shop first.");
        if(!scene.IsValid()||!scene.isLoaded)scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
        if(scene.GetRootGameObjects().Any(o=>o.name=="Cloth Shop Environment"))return "DONE (already applied)";
        var objects=scene.GetRootGameObjects();
        var filters=objects.SelectMany(o=>o.GetComponentsInChildren<MeshFilter>(true))
            .Where(f=>f.gameObject.activeInHierarchy && f.sharedMesh && f.TryGetComponent<MeshRenderer>(out var r)&&r.enabled
                && !PrefabUtility.IsPartOfPrefabInstance(f) && f.transform.childCount==0).ToArray();
        var garmentFilters=filters.Where(f=>AssetDatabase.GetAssetPath(f.sharedMesh)==Garments).ToArray();
        if(garmentFilters.Length<50)throw new InvalidOperationException($"Expected shared hanging garments, found {garmentFilters.Length}.");
        if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/Prefeb","Cloth Shop");
        string pieces=Folder+"/Reusable";
        if(!AssetDatabase.IsValidFolder(pieces))AssetDatabase.CreateFolder(Folder,"Reusable");
        var before=filters.ToDictionary(f=>f.gameObject,o=>(o.sharedMesh,o.GetComponent<MeshRenderer>().sharedMaterials,o.transform.localToWorldMatrix));
        var prior=SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(scene);
        DetailGarments(garmentFilters);
        var groups=filters.GroupBy(f=>Family(f.name)).Where(g=>g.Count()>1).OrderBy(g=>g.Key).ToArray();
        int instances=0, assets=0;
        var settings=new ConvertToPrefabInstanceSettings
        {
            objectMatchMode=ObjectMatchMode.ByHierarchy,
            changeRootNameToAssetName=false,
            recordPropertyOverridesOfMatches=true,
            componentsNotMatchedBecomesOverride=true,
            gameObjectsNotMatchedBecomesOverride=true
        };
        foreach(var group in groups)
        {
            var members=group.Select(f=>f.gameObject).ToArray();
            var name=Regex.Replace(group.Key,@"[^\w .-]+"," ").Trim();
            string path=AssetDatabase.GenerateUniqueAssetPath(pieces+"/"+name+".prefab");
            var prefab=PrefabUtility.SaveAsPrefabAsset(members[0],path,out var success);
            if(!success||!prefab)throw new IOException("Could not save "+path);
            PrefabUtility.ConvertToPrefabInstances(members,prefab,settings,InteractionMode.AutomatedAction);
            foreach(var go in members)
            {
                if(!PrefabUtility.IsAnyPrefabInstanceRoot(go))throw new InvalidOperationException("Prefab conversion failed for "+go.name);
                var old=before[go];
                if(go.GetComponent<MeshFilter>().sharedMesh!=old.sharedMesh||
                    !go.GetComponent<MeshRenderer>().sharedMaterials.SequenceEqual(old.sharedMaterials)||
                    !NearlySame(go.transform.localToWorldMatrix,old.localToWorldMatrix))
                    throw new InvalidOperationException("Mesh, material, or placement changed for "+go.name);
            }
            assets++;instances+=members.Length;
        }
        var environment=new GameObject("Cloth Shop Environment");
        foreach(var root in objects)
        {
            if(root.GetComponent<Camera>()||root.GetComponent<Light>()||root.name=="Global Volume")continue;
            if(root.GetComponentInChildren<MeshRenderer>(true)||root.name=="City Mirage | Lighting")
                root.transform.SetParent(environment.transform,true);
        }
        var environmentPath=Folder+"/Cloth Shop Environment.prefab";
        var saved=PrefabUtility.SaveAsPrefabAssetAndConnect(environment,environmentPath,InteractionMode.AutomatedAction,out bool ok);
        if(!ok||!saved||!PrefabUtility.IsAnyPrefabInstanceRoot(environment))throw new IOException("Environment prefab save failed.");
        AssetDatabase.SaveAssets();
        if(!EditorSceneManager.SaveScene(scene))throw new IOException("Scene save failed.");
        if(prior.IsValid()&&prior.isLoaded&&prior!=scene)SceneManager.SetActiveScene(prior);
        return $"DONE: {assets} reusable prefab assets, {instances} scene instances, one connected environment prefab. {garmentFilters.Length} garments refined.";
    }
    static string Family(string name)
    {
        name=Regex.Replace(name,@"\.\d{3}$","");
        if(name.StartsWith("T-shirt |"))return "Hanging T-Shirt";
        if(name.StartsWith("Hoodie |"))return "Hanging Hoodie";
        if(name.StartsWith("Folded garment |")||name.StartsWith("Folded apparel | stack"))return "Folded Garment";
        if(Regex.IsMatch(name,@"^Wall Bay 0[1-4]$"))return "Wall Display Bay";
        if(name.StartsWith("Sale Table")||name.StartsWith("Display Table"))return "Merchandise Display Table";
        return name;
    }
    static bool NearlySame(Matrix4x4 a,Matrix4x4 b)
    {
        for(int i=0;i<16;i++)if(Mathf.Abs(a[i]-b[i])>.0002f)return false;
        return true;
    }
    static Vector3 Axis(int axis){var a=Vector3.zero;a[axis]=1;return a;}
    static void DetailGarments(MeshFilter[] filters)
    {
        var seen=new HashSet<Mesh>();
        foreach(var f in filters)
        {
            var m=f.sharedMesh;
            if(!seen.Add(m))continue;
            var b=m.bounds;
            var axes=Enumerable.Range(0,3).OrderByDescending(i=>Mathf.Abs(f.transform.TransformDirection(Axis(i)).y)).ToArray();
            int vertical=axes[0],wide=b.size[axes[1]]>b.size[axes[2]]?axes[1]:axes[2];
            int depth=axes.Single(i=>i!=vertical&&i!=wide);
            bool up=f.transform.TransformDirection(Axis(vertical)).y>0;
            bool hoodie=f.name.StartsWith("Hoodie");
            var verts=m.vertices;
            for(int i=0;i<verts.Length;i++)
            {
                var v=verts[i];
                float x=(v[wide]-b.center[wide])/b.extents[wide];
                float z=Mathf.Clamp01((v[vertical]-b.min[vertical])/b.size[vertical]);
                if(!up)z=1-z;
                float profile=(v[depth]-b.center[depth])/b.extents[depth];
                float body=Mathf.Clamp01((.72f-Mathf.Abs(x))/.25f);
                float crease=Mathf.Sin(12*x+2.3f*z)*.014f+Mathf.Sin(22*x-3.1f*z)*.008f;
                float hem=.023f*Mathf.Exp(-Mathf.Pow((z-.055f)/.025f,2))*body;
                float arm=Mathf.Clamp01((Mathf.Abs(x)-.62f)/.24f);
                float cuff=hoodie?.039f*arm*Mathf.Exp(-Mathf.Pow((z-.18f)/.041f,2)):
                    .033f*arm*Mathf.Exp(-Mathf.Pow((Mathf.Abs(x)-.88f)/.055f,2));
                v[depth]+=Mathf.Sign(profile)*b.size[depth]*(crease*body+hem+cuff)*Mathf.Clamp01(Mathf.Abs(profile)*2.2f);
                verts[i]=v;
            }
            m.vertices=verts;m.RecalculateNormals();m.RecalculateTangents();m.RecalculateBounds();
            EditorUtility.SetDirty(m);
        }
    }
}
