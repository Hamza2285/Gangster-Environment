using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class CityMirageThreeAtlases
{
    const string Prefab="Assets/Prefeb/Cloth Shop/Cloth Shop Environment.prefab";
    const string Tex="Assets/Texture/City Mirage/";
    const string Mat="Assets/Materials/City Mirage/";
    const string Request="Temp/CityMirageThreeAtlases.request";
    const string Status="Temp/CityMirageThreeAtlases.txt";
    const string MeshAsset=Tex+"Final Three Atlas Meshes.asset";
    [InitializeOnLoadMethod] static void Schedule()
    {if(!Application.isBatchMode&&File.Exists(Request))EditorApplication.delayCall+=Run;}
    [MenuItem("Tools/City Mirage/Consolidate Interior Into Three Atlases")]
    public static void Run()
    {
        try{File.WriteAllText(Status,Apply());File.Delete(Request);}
        catch(Exception e){File.WriteAllText(Status,e.ToString());Debug.LogException(e);}
    }
    static string Apply()
    {
        var cloth=MaterialAt("Clothes Atlas");
        var props=MaterialAt("Props Floor Walls Atlas");
        var arch=MaterialAt("Architecture Ceiling Atlas");
        var shader=AssetDatabase.LoadAssetAtPath<Shader>(Tex+"Clothes Shared Atlas.shader");
        if(!cloth||!props||!arch||!shader||ShaderUtil.ShaderHasError(shader))
            throw new InvalidOperationException("One of the three shared atlas materials or shaders is missing.");
        var root=PrefabUtility.LoadPrefabContents(Prefab);
        var temporary=new List<Mesh>();
        try
        {
            var allowed=new[]{cloth,props,arch};
            var active=root.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
            var unknown=active.SelectMany(r=>r.sharedMaterials).Where(m=>!m||!allowed.Contains(m)&&Destination(m.name)<0)
                .Select(m=>m?m.name:"<missing>").Distinct().ToArray();
            if(unknown.Length>0)throw new InvalidOperationException("Unmapped active materials: "+string.Join(", ",unknown));
            var rs=active.Where(r=>r.sharedMaterials.Any(m=>!allowed.Contains(m))).ToArray();
            foreach(var r in rs)
            {
                if(!r.TryGetComponent<MeshFilter>(out var f)||!f.sharedMesh)
                    throw new InvalidOperationException("Unexpected non-mesh renderer: "+r.name);
                var destinations=r.sharedMaterials.Select(m=>allowed.Contains(m)?Array.IndexOf(allowed,m):Destination(m.name)).Distinct().ToArray();
                if(destinations.Length!=1||r.sharedMaterials.Length!=f.sharedMesh.subMeshCount)
                    throw new InvalidOperationException("Mixed atlas families on "+r.name);
            }
            var albedo=TextureAt("Base Color",false,4096);
            var normal=TextureAt("Normal",true,2048);
            var surface=TextureAt("Surface",false,4096,true);
            var emission=TextureAt("Emission",false,2048);
            cloth.shader=shader;
            cloth.SetTexture("_BaseMap",albedo);cloth.SetTexture("_BumpMap",normal);
            cloth.SetTexture("_MetallicGlossMap",surface);cloth.SetTexture("_EmissionMap",emission);
            cloth.SetFloat("_BumpScale",.72f);cloth.SetFloat("_Smoothness",1);
            cloth.EnableKeyword("_NORMALMAP");cloth.EnableKeyword("_METALLICSPECGLOSSMAP");
            EditorUtility.SetDirty(cloth);
            var cache=new Dictionary<(Mesh,string),Mesh>();
            bool first=!AssetDatabase.LoadMainAssetAtPath(MeshAsset);
            foreach(var r in rs)
            {
                var f=r.GetComponent<MeshFilter>();
                var source=f.sharedMesh;
                var materials=r.sharedMaterials;
                var key=(source,string.Join("|",materials.Select(m=>m.name)));
                if(!cache.TryGetValue(key,out var combined))
                {
                    var parts=new List<CombineInstance>();
                    for(int i=0;i<source.subMeshCount;i++)
                    {
                        var variant=UnityEngine.Object.Instantiate(source);
                        temporary.Add(variant);
                        var uv=source.uv;
                        if(uv.Length!=source.vertexCount)uv=new Vector2[source.vertexCount];
                        for(int j=0;j<uv.Length;j++)uv[j]=Remap(materials[i].name,uv[j]);
                        variant.uv=uv;
                        parts.Add(new CombineInstance{mesh=variant,subMeshIndex=i,transform=Matrix4x4.identity});
                    }
                    combined=new Mesh{name=source.name+" | final shared atlas",indexFormat=parts.Sum(p=>p.mesh.vertexCount)>65535?IndexFormat.UInt32:IndexFormat.UInt16};
                    combined.CombineMeshes(parts.ToArray(),true,true);
                    if(combined.GetIndexCount(0)!=Enumerable.Range(0,source.subMeshCount).Sum(i=>(long)source.GetIndexCount(i)))
                        throw new InvalidOperationException("Triangle count changed on "+r.name);
                    if(first){AssetDatabase.CreateAsset(combined,MeshAsset);first=false;}
                    else AssetDatabase.AddObjectToAsset(combined,MeshAsset);
                    cache.Add(key,combined);
                }
                f.sharedMesh=combined;
                r.sharedMaterials=new[]{allowed[Destination(materials[0].name)]};
                if(PrefabUtility.IsPartOfPrefabInstance(f))
                {PrefabUtility.RecordPrefabInstancePropertyModifications(f);PrefabUtility.RecordPrefabInstancePropertyModifications(r);}
            }
            var used=active.SelectMany(r=>r.sharedMaterials).Distinct().ToArray();
            if(used.Length!=3||used.Any(m=>!allowed.Contains(m)))throw new InvalidOperationException("The visible interior must use exactly three materials.");
            AssetDatabase.SaveAssets();
            PrefabUtility.SaveAsPrefabAsset(root,Prefab,out var saved);
            if(!saved)throw new IOException("Could not save consolidated environment prefab.");
            return $"DONE: {active.Length} visible renderers use exactly {used.Length} materials. {rs.Length} remaining renderers migrated. Lighting and bloom unchanged.";
        }
        finally
        {
            foreach(var m in temporary)UnityEngine.Object.DestroyImmediate(m);
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
    static Material MaterialAt(string n)=>AssetDatabase.LoadAssetAtPath<Material>(Mat+n+".mat");
    static Texture2D TextureAt(string name,bool normal,int max,bool linear=false)
    {
        string p=Tex+"Clothes Shared Atlas "+name+".png";
        var i=(TextureImporter)AssetImporter.GetAtPath(p);
        if(!i)throw new IOException("Missing "+name+" atlas.");
        i.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
        i.sRGBTexture=!normal&&!linear;
        i.wrapMode=TextureWrapMode.Clamp;i.mipmapEnabled=true;i.maxTextureSize=max;
        i.textureCompression=TextureImporterCompression.CompressedHQ;
        var settings=i.GetDefaultPlatformTextureSettings();settings.maxTextureSize=max;i.SetPlatformTextureSettings(settings);
        i.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
    }
    static int Destination(string n)
    {
        switch(n)
        {
            case "Reference - honey wood insert":case "Reference - gold sign trim":case "Display shoes - stitched atlas":
            case "Branded display panels":case "Checkout Apparel Displays":case "Reference - retail sign faces":return 0;
            case "Props Atlas":case "Floor and Walls PBR":
            case "Entrance - illuminated CLOTH SHOP marquee":case "Entrance - branded door panels":case "Entrance - charcoal frame":
            case "Entrance - bronze edge and handles":case "Entrance - warm skirting and jamb LEDs":case "Entrance - violet detector LEDs":return 1;
            case "Architecture Atlas":case "Banner - charcoal housing":case "Banner - violet tube":case "Banner - illuminated apparel artwork":
            case "Amber display trim":case "Reference - matte black stand":case "Reference - satin charcoal":case "Checkout - satin graphite":
            case "Cyan counter strip":case "Counter - cyan edge tube":case "Reference - satin ceramic":case "Checkout Planter Leaves":
            case "Ceiling - bright warm diffuser":case "Ceiling - brushed conduit steel":case "Ceiling - satin dark steel":
            case "Fixture - cyan icon tube":return 2;
            default:return -1;
        }
    }
    static Vector2 Cell(int i,float x=.5f,float y=.5f)=>new Vector2((i%4+.025f+x*.95f)/4,(i/4+.025f+y*.95f)/4);
    static Vector2 ArchSwatch(int i)
    {
        int col=i<3?i:i<6?i-3:(i-6)*2,row=i<3?0:i<6?2:1;
        return new Vector2((2+(col+.5f)/3)/4,(1+(row+.5f)/3)/4);
    }
    static Vector2 Remap(string n,Vector2 uv)
    {
        switch(n)
        {
            case "Reference - honey wood insert":return new Vector2(1.6875f,.375f);
            case "Reference - gold sign trim":return new Vector2(1.8125f,.375f);
            case "Display shoes - stitched atlas":return new Vector2(1.5f+uv.x*.125f,.375f+uv.y*.125f);
            case "Branded display panels":return new Vector2(2.5f+uv.x*.5f,uv.y);
            case "Reference - retail sign faces":return new Vector2(3.5f+uv.x*.5f,uv.y);
            case "Checkout Apparel Displays":return new Vector2(4.5f+uv.x*.5f,uv.y);
            case "Props Atlas":return uv*.5f;
            case "Floor and Walls PBR":return new Vector2(.5f+uv.x*.5f,uv.y*2);
            case "Entrance - illuminated CLOTH SHOP marquee":case "Entrance - branded door panels":return new Vector2(uv.x*.5f,.75f+uv.y*.25f);
            case "Entrance - charcoal frame":return new Vector2(.625f,.875f);
            case "Entrance - bronze edge and handles":return new Vector2(.625f,.625f);
            case "Entrance - warm skirting and jamb LEDs":return new Vector2(.4375f,.0625f);
            case "Entrance - violet detector LEDs":return new Vector2(.875f,.875f);
            case "Banner - charcoal housing":case "Ceiling - satin dark steel":return Cell(9);
            case "Ceiling - brushed conduit steel":return Cell(11);
            case "Ceiling - bright warm diffuser":return Cell(12);
            case "Banner - violet tube":return Cell(13);
            case "Banner - illuminated apparel artwork":return Cell(14,Mathf.Clamp01((uv.x-.003f)/.869f),Mathf.Clamp01((uv.y-.753f)/.243f));
            case "Fixture - cyan icon tube":return Cell(6);
            case "Reference - matte black stand":return ArchSwatch(0);
            case "Reference - satin charcoal":return ArchSwatch(1);
            case "Checkout - satin graphite":return ArchSwatch(2);
            case "Cyan counter strip":return ArchSwatch(3);
            case "Counter - cyan edge tube":return ArchSwatch(4);
            case "Amber display trim":return ArchSwatch(5);
            case "Reference - satin ceramic":return ArchSwatch(6);
            case "Checkout Planter Leaves":return ArchSwatch(7);
            default:return uv;
        }
    }
}
