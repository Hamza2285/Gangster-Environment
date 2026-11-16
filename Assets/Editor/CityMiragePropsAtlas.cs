using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class CityMiragePropsAtlas
{
    const string Prefab="Assets/Prefeb/Cloth Shop/Cloth Shop Environment.prefab";
    const string Texture="Assets/Texture/City Mirage/";
    const string Materials="Assets/Materials/City Mirage/";
    const string MeshPath=Texture+"Props Floor Walls UV Meshes.asset";
    const string Request="Temp/CityMiragePropsAtlas.request";
    const string Status="Temp/CityMiragePropsAtlas.txt";
    [InitializeOnLoadMethod] static void Schedule()
    {
        if(!Application.isBatchMode&&File.Exists(Request))EditorApplication.delayCall+=Run;
    }
    [MenuItem("Tools/City Mirage/Share Props Floor Walls Atlas")]
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
            var props=AssetDatabase.LoadAssetAtPath<Material>(Materials+"Props Atlas.mat");
            var room=AssetDatabase.LoadAssetAtPath<Material>(Materials+"Floor and Walls PBR.mat");
            var shared=AssetDatabase.LoadAssetAtPath<Material>(Materials+"Props Floor Walls Atlas.mat");
            if(!props||!room)throw new InvalidOperationException("Source materials missing.");
            var shader=AssetDatabase.LoadAssetAtPath<Shader>(Texture+"Props Floor Walls Atlas.shader");
            if(!shader||ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Shared atlas shader failed to import.");
            var candidates=root.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r=>r.sharedMaterials.Any(m=>m==props||m==room)).ToArray();
            if(candidates.Length==0&&shared)return "DONE (already consolidated)";
            if(candidates.Length<100)throw new InvalidOperationException($"Only {candidates.Length} atlas users found.");
            foreach(var r in candidates)
            {
                var mats=r.sharedMaterials;
                if(!mats.All(m=>m==mats[0])||!r.TryGetComponent<MeshFilter>(out var f)||!f.sharedMesh||
                    f.sharedMesh.uv.Length!=f.sharedMesh.vertexCount)
                    throw new InvalidOperationException("Unexpected UV or material layout on "+r.name);
            }
            var albedo=LoadTexture("Props Floor Walls Base Color.png");
            var normal=LoadTexture("Props Floor Walls Normal.png");
            var mask=LoadTexture("Props Floor Walls Surface.png");
            if(!shared)
            {
                shared=new Material(shader);
                AssetDatabase.CreateAsset(shared,Materials+"Props Floor Walls Atlas.mat");
            }
            shared.SetTexture("_BaseMap",albedo);
            shared.SetTexture("_BumpMap",normal);
            shared.SetTexture("_MetallicGlossMap",mask);
            shared.SetTexture("_OcclusionMap",mask);
            shared.SetFloat("_BumpScale",1);
            shared.SetFloat("_Smoothness",1);
            shared.SetFloat("_OcclusionStrength",.65f);
            shared.EnableKeyword("_NORMALMAP");
            shared.EnableKeyword("_METALLICSPECGLOSSMAP");
            shared.EnableKeyword("_OCCLUSIONMAP");
            EditorUtility.SetDirty(shared);
            if(AssetDatabase.LoadMainAssetAtPath(MeshPath))AssetDatabase.DeleteAsset(MeshPath);
            var variants=new Dictionary<(Mesh,bool),Mesh>();
            int roomCount=0;
            foreach(var renderer in candidates)
            {
                var f=renderer.GetComponent<MeshFilter>();
                bool architecture=renderer.sharedMaterial==room;
                if(architecture)roomCount++;
                var key=(f.sharedMesh,architecture);
                if(!variants.TryGetValue(key,out var mapped))
                {
                    mapped=UnityEngine.Object.Instantiate(f.sharedMesh);
                    mapped.name=f.sharedMesh.name+" | shared prop and room atlas";
                    var uv=mapped.uv;
                    for(int i=0;i<uv.Length;i++)
                        uv[i]=architecture?new Vector2(.5f+uv[i].x*.5f,uv[i].y*4):new Vector2(uv[i].x*.5f,uv[i].y);
                    mapped.uv=uv;
                    if(variants.Count==0)AssetDatabase.CreateAsset(mapped,MeshPath);
                    else AssetDatabase.AddObjectToAsset(mapped,MeshPath);
                    variants.Add(key,mapped);
                }
                f.sharedMesh=mapped;
                renderer.sharedMaterials=Enumerable.Repeat(shared,mapped.subMeshCount).ToArray();
                if(PrefabUtility.IsPartOfPrefabInstance(f))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(f);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
            }
            AssetDatabase.SaveAssets();
            PrefabUtility.SaveAsPrefabAsset(root,Prefab,out var saved);
            if(!saved)throw new IOException("Could not save the shared-atlas prefab.");
            return $"DONE: {candidates.Length-roomCount} prop and {roomCount} floor/wall renderers, {variants.Count} shared UV meshes, one PBR material. Lighting and emissive materials unchanged.";
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }
    static Texture2D LoadTexture(string n)
    {
        var t=AssetDatabase.LoadAssetAtPath<Texture2D>(Texture+n);
        if(!t)throw new IOException("Missing packed map "+n);
        return t;
    }
}
