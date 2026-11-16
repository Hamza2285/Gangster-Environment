using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
public static class CityMirageEntrance4K
{
    const string Prefab="Assets/Prefeb/Cloth Shop/Cloth Shop Environment.prefab";
    const string Tex="Assets/Texture/City Mirage/";
    const string Asset=Tex+"Props Floor Walls Entrance 4K UVs.asset";
    const string Request="Temp/CityMirageEntrance4K.request";
    const string Status="Temp/CityMirageEntrance4K.txt";
    [InitializeOnLoadMethod] static void Schedule()
    {if(!Application.isBatchMode&&File.Exists(Request))EditorApplication.delayCall+=Run;}
    [MenuItem("Tools/City Mirage/Pack Entrance Into 4K Atlas")]
    public static void Run()
    {
        try{File.WriteAllText(Status,Apply());File.Delete(Request);}
        catch(Exception e){File.WriteAllText(Status,e.ToString());Debug.LogException(e);}
    }
    static string Apply()
    {
        var mat=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/City Mirage/Props Floor Walls Atlas.mat");
        if(!mat||ShaderUtil.ShaderHasError(mat.shader))throw new InvalidOperationException("4K shader import failed.");
        var path=Tex+"Props Floor Walls Emission.png";
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        if(!importer)throw new IOException("Emission map missing.");
        importer.maxTextureSize=4096;
        importer.sRGBTexture=false;
        importer.textureCompression=TextureImporterCompression.CompressedHQ;
        var settings=importer.GetDefaultPlatformTextureSettings();
        settings.maxTextureSize=4096;
        importer.SetPlatformTextureSettings(settings);
        importer.SaveAndReimport();
        var emission=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        var baseMap=mat.GetTexture("_BaseMap");
        if(baseMap.width!=4096||baseMap.height!=4096||emission.width!=4096||emission.height!=4096)
            throw new InvalidOperationException($"4K import failed: Base {baseMap.width}x{baseMap.height}; emission {emission.width}x{emission.height}.");
        var root=PrefabUtility.LoadPrefabContents(Prefab);
        try
        {
            var rs=root.GetComponentsInChildren<MeshRenderer>(true);
            var prior=rs.Where(r=>r.sharedMaterial==mat&&r.TryGetComponent<MeshFilter>(out var f)&&
                f.sharedMesh&&AssetDatabase.GetAssetPath(f.sharedMesh)!=Asset).ToArray();
            if(prior.Length==0&&AssetDatabase.LoadMainAssetAtPath(Asset))return "DONE (already applied)";
            var doors=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Entrance | branded double doors");
            var gate=rs.Where(r=>r.enabled&&r.gameObject.activeSelf&&
                (r.name=="Entrance marquee | one connected mesh"||r.transform.IsChildOf(doors))&&r.sharedMaterials.Any(m=>m!=mat)).ToArray();
            if(prior.Length<100||gate.Length<3)throw new InvalidOperationException($"Unexpected renderer count: {prior.Length}, {gate.Length}.");
            foreach(var r in gate)
                if(r.sharedMaterials.Any(m=>Slot(m)<0))throw new InvalidOperationException("Unmapped gate material on "+r.name);
            bool first=true;
            var variants=new Dictionary<(Mesh,bool),Mesh>();
            foreach(var r in prior)
            {
                var f=r.GetComponent<MeshFilter>();
                bool entry=r.name=="Entrance - warm skirting and jamb LEDs";
                var key=(f.sharedMesh,entry);
                if(!variants.TryGetValue(key,out var m))
                {
                    m=UnityEngine.Object.Instantiate(f.sharedMesh);
                    m.name=f.sharedMesh.name+" | 4K atlas";
                    m.uv=m.uv.Select(v=>entry?new Vector2(.4375f,.0625f):new Vector2(v.x,v.y*.5f)).ToArray();
                    Save(m,ref first);variants.Add(key,m);
                }
                Bind(f,r,m,mat);
            }
            foreach(var r in gate)
            {
                var f=r.GetComponent<MeshFilter>();var source=f.sharedMesh;
                var parts=new List<CombineInstance>();var tmp=new List<Mesh>();
                try
                {
                    for(int i=0;i<source.subMeshCount;i++)
                    {
                        int slot=Slot(r.sharedMaterials[i]);
                        var m=UnityEngine.Object.Instantiate(source);tmp.Add(m);
                        var uv=new Vector2[source.vertexCount];var old=source.uv;
                        for(int j=0;j<uv.Length;j++)
                        {
                            var v=old.Length==uv.Length?old[j]:Vector2.one*.5f;
                            uv[j]=slot==0?new Vector2(v.x*.5f,.75f+v.y*.25f):
                                slot==1?new Vector2(.625f,.875f):slot==2?new Vector2(.875f,.875f):
                                slot==3?new Vector2(.4375f,.0625f):new Vector2(.625f,.625f);
                        }
                        m.uv=uv;
                        parts.Add(new CombineInstance{mesh=m,subMeshIndex=i,transform=Matrix4x4.identity});
                    }
                    var mesh=new Mesh{name=source.name+" | 4K gate atlas",
                        indexFormat=parts.Sum(p=>p.mesh.vertexCount)>65535?IndexFormat.UInt32:IndexFormat.UInt16};
                    mesh.CombineMeshes(parts.ToArray(),true,true);
                    Save(mesh,ref first);Bind(f,r,mesh,mat);
                }
                finally{foreach(var m in tmp)UnityEngine.Object.DestroyImmediate(m);}
            }
            mat.SetTexture("_EmissionMap",emission);mat.SetColor("_EmissionColor",new Color(8,8,8,1));
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            PrefabUtility.SaveAsPrefabAsset(root,Prefab,out var saved);
            if(!saved)throw new IOException("4K prefab save failed.");
            return $"DONE: {gate.Length} entrance and {prior.Length} existing renderers on shared 4K atlas. Emission packed; lighting unchanged.";
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }
    static int Slot(Material m)
    {
        string n=m?m.name:"";
        if(n.Contains("branded door")||n.Contains("marquee"))return 0;
        if(n.Contains("charcoal frame"))return 1;
        if(n.Contains("violet"))return 2;
        if(n.Contains("warm skirting"))return 3;
        if(n.Contains("bronze"))return 4;
        return -1;
    }
    static void Save(Mesh m,ref bool first)
    {if(first){AssetDatabase.CreateAsset(m,Asset);first=false;}else AssetDatabase.AddObjectToAsset(m,Asset);}
    static void Bind(MeshFilter f,MeshRenderer r,Mesh m,Material mat)
    {
        f.sharedMesh=m;r.sharedMaterials=Enumerable.Repeat(mat,m.subMeshCount).ToArray();
        if(PrefabUtility.IsPartOfPrefabInstance(f))
        {PrefabUtility.RecordPrefabInstancePropertyModifications(f);PrefabUtility.RecordPrefabInstancePropertyModifications(r);}
    }
}
