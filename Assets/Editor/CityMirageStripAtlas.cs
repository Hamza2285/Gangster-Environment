using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
public static class CityMirageStripAtlas
{
    const string Prefab="Assets/Prefeb/Cloth Shop/Cloth Shop Environment.prefab";
    const string Textures="Assets/Texture/City Mirage/";
    const string Request="Temp/CityMirageStripAtlas.request";
    const string Status="Temp/CityMirageStripAtlas.txt";
    [InitializeOnLoadMethod] static void Schedule()
    {
        if(!Application.isBatchMode&&File.Exists(Request))EditorApplication.delayCall+=Run;
    }
    [MenuItem("Tools/City Mirage/Pack Rack and Skirting Emission")]
    public static void Run()
    {
        try{File.WriteAllText(Status,Apply());File.Delete(Request);}
        catch(Exception e){File.WriteAllText(Status,e.ToString());Debug.LogException(e);}
    }
    static string Apply()
    {
        var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/City Mirage/Props Floor Walls Atlas.mat");
        if(!material||ShaderUtil.ShaderHasError(material.shader))throw new InvalidOperationException("Shared atlas shader failed import.");
        string path=Textures+"Props Floor Walls Emission.png";
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        if(!importer)throw new IOException("Emission texture not imported.");
        importer.sRGBTexture=false;importer.wrapMode=TextureWrapMode.Clamp;importer.mipmapEnabled=true;
        importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
        var root=PrefabUtility.LoadPrefabContents(Prefab);
        try
        {
            var strips=root.GetComponentsInChildren<MeshRenderer>(true).Where(r=>
                r.name=="Rack | warm base trim"||r.name=="Skirting | warm LED channel"||
                r.name=="Entrance - warm skirting and jamb LEDs").ToArray();
            if(strips.Length<10)throw new InvalidOperationException($"Only {strips.Length} rack/skirting renderers found.");
            material.SetTexture("_EmissionMap",AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            material.SetColor("_EmissionColor",new Color(8,8,8,1));
            EditorUtility.SetDirty(material);
            var meshes=new Dictionary<(Mesh,bool),Mesh>();
            string meshPath=Textures+"Rack Skirting Emission UVs.asset";
            if(strips.All(r=>r.sharedMaterial==material))return "DONE (already packed)";
            if(AssetDatabase.LoadMainAssetAtPath(meshPath))AssetDatabase.DeleteAsset(meshPath);
            foreach(var renderer in strips)
            {
                var filter=renderer.GetComponent<MeshFilter>();
                var old=filter.sharedMesh;
                bool entrance=renderer.name.StartsWith("Entrance -");
                var key=(old,entrance);
                if(!meshes.TryGetValue(key,out var mesh))
                {
                    mesh=UnityEngine.Object.Instantiate(old);
                    mesh.name=old.name+" | shared LED atlas cell";
                    mesh.uv=Enumerable.Repeat(new Vector2(.4375f,entrance ? .125f : .375f),mesh.vertexCount).ToArray();
                    if(meshes.Count==0)AssetDatabase.CreateAsset(mesh,meshPath);
                    else AssetDatabase.AddObjectToAsset(mesh,meshPath);
                    meshes.Add(key,mesh);
                }
                filter.sharedMesh=mesh;
                renderer.sharedMaterials=Enumerable.Repeat(material,mesh.subMeshCount).ToArray();
                if(PrefabUtility.IsPartOfPrefabInstance(filter))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
            }
            AssetDatabase.SaveAssets();
            PrefabUtility.SaveAsPrefabAsset(root,Prefab,out var saved);
            if(!saved)throw new IOException("Could not save environment prefab.");
            return $"DONE: {strips.Length} rack and skirting renderers use the shared Props Floor Walls material and emission map. Light components and bloom unchanged.";
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }
}

