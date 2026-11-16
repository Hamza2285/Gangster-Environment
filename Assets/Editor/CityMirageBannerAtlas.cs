using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class CityMirageBannerAtlas
{
    const string Prefab="Assets/Prefeb/Cloth Shop/Cloth Shop Environment.prefab";
    const string Path="Assets/Texture/City Mirage/Upper Banner Architecture UVs.asset";
    const string Request="Temp/CityMirageBannerAtlas.request";
    const string Status="Temp/CityMirageBannerAtlas.txt";
    static readonly string[] Names={"Banner - charcoal housing","Banner - violet tube","Banner - illuminated apparel artwork"};
    [InitializeOnLoadMethod] static void Schedule()
    {if(!Application.isBatchMode&&File.Exists(Request))EditorApplication.delayCall+=Run;}
    [MenuItem("Tools/City Mirage/Pack Banner Materials Into Architecture Atlas")]
    public static void Run()
    {
        try{File.WriteAllText(Status,Apply());File.Delete(Request);}
        catch(Exception e){File.WriteAllText(Status,e.ToString());Debug.LogException(e);}
    }
    static string Apply()
    {
        var atlas=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/City Mirage/Architecture Ceiling Atlas.mat");
        if(!atlas||!atlas.GetTexture("_EmissionMap"))throw new InvalidOperationException("Architecture Atlas or emission map missing.");
        var root=PrefabUtility.LoadPrefabContents(Prefab);
        var temporary=new List<Mesh>();
        try
        {
            var signs=root.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r=>r.enabled&&r.name.Contains("CLOTH SHOP lightbox")&&r.name.Contains("one connected mesh")&&
                    r.sharedMaterials.Any(m=>m&&Names.Contains(m.name))).ToArray();
            if(signs.Length==0)
            {
                if(AssetDatabase.LoadMainAssetAtPath(Path))return "DONE (already packed)";
                throw new InvalidOperationException("No active upper banner with these materials found.");
            }
            foreach(var r in signs)
                if(r.sharedMaterials.Any(m=>!m||!Names.Contains(m.name)))throw new InvalidOperationException("Unexpected slot on "+r.name);
            bool first=true;
            long triangles=0;
            foreach(var r in signs)
            {
                var f=r.GetComponent<MeshFilter>();
                var source=f.sharedMesh;
                var inputs=new List<CombineInstance>();
                long expected=0;
                for(int i=0;i<source.subMeshCount;i++)
                {
                    int slot=new[]{9,13,14}[Array.IndexOf(Names,r.sharedMaterials[i].name)];
                    var m=UnityEngine.Object.Instantiate(source);
                    temporary.Add(m);
                    var uv=source.uv;
                    for(int j=0;j<uv.Length;j++)
                    {
                        float x=slot==14?Mathf.Clamp01((uv[j].x-.003f)/.869f):.5f;
                        float y=slot==14?Mathf.Clamp01((uv[j].y-.753f)/.243f):.5f;
                        uv[j]=new Vector2((slot%4+.025f+x*.95f)/4,(slot/4+.025f+y*.95f)/4);
                    }
                    m.uv=uv;
                    expected+=source.GetIndexCount(i)/3;
                    inputs.Add(new CombineInstance{mesh=m,subMeshIndex=i,transform=Matrix4x4.identity});
                }
                var mesh=new Mesh{name=source.name+" | one Architecture material",indexFormat=IndexFormat.UInt16};
                mesh.CombineMeshes(inputs.ToArray(),true,true);
                if(mesh.subMeshCount!=1||mesh.GetIndexCount(0)/3!=expected)
                    throw new InvalidOperationException("Banner topology changed unexpectedly.");
                if(first){AssetDatabase.CreateAsset(mesh,Path);first=false;}else AssetDatabase.AddObjectToAsset(mesh,Path);
                f.sharedMesh=mesh;
                r.sharedMaterials=new[]{atlas};
                if(PrefabUtility.IsPartOfPrefabInstance(f))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(f);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(r);
                }
                triangles+=expected;
            }
            AssetDatabase.SaveAssets();
            PrefabUtility.SaveAsPrefabAsset(root,Prefab,out var saved);
            if(!saved)throw new IOException("Could not save banner atlas assignments.");
            return $"DONE: {signs.Length} upper banner(s), one material slot each, {triangles} triangles retained. Artwork and violet glow use Architecture atlas maps.";
        }
        finally
        {
            foreach(var mesh in temporary)UnityEngine.Object.DestroyImmediate(mesh);
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
