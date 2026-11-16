using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class CityMirageCeilingAtlas
{
    const string Prefab="Assets/Prefeb/Cloth Shop/Cloth Shop Environment.prefab";
    const string Textures="Assets/Texture/City Mirage/";
    const string Materials="Assets/Materials/City Mirage/";
    const string MeshPath=Textures+"Ceiling Structure Combined.asset";
    const string Request="Temp/CityMirageCeilingAtlas.request";
    const string Status="Temp/CityMirageCeilingAtlas.txt";
    [InitializeOnLoadMethod] static void Schedule()
    {
        if(!Application.isBatchMode&&File.Exists(Request))EditorApplication.delayCall+=Run;
    }
    [MenuItem("Tools/City Mirage/Combine Ceiling and Atlas")]
    public static void Run()
    {
        try{File.WriteAllText(Status,Apply());File.Delete(Request);}
        catch(Exception e){File.WriteAllText(Status,e.ToString());Debug.LogException(e);}
    }
    static string Apply()
    {
        var root=PrefabUtility.LoadPrefabContents(Prefab);
        var temporary=new List<Mesh>();
        try
        {
            var group=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Ceiling Structure");
            if(group.Find("Ceiling Structure | combined mesh"))return "DONE (already combined)";
            var sources=group.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r=>r.enabled&&r.gameObject.activeSelf&&r.GetComponent<MeshFilter>()&&r.GetComponent<MeshFilter>().sharedMesh).ToArray();
            if(sources.Length<30)throw new InvalidOperationException($"Expected grouped ceiling meshes, found {sources.Length}.");
            var shader=AssetDatabase.LoadAssetAtPath<Shader>(Textures+"Architecture Ceiling Atlas.shader");
            if(!shader||ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Ceiling atlas shader import failed.");
            var entries=new List<CombineInstance>();
            long triangles=0;
            foreach(var r in sources)
            {
                var f=r.GetComponent<MeshFilter>();
                var source=f.sharedMesh;
                var mats=r.sharedMaterials;
                if(mats.Length!=source.subMeshCount)throw new InvalidOperationException("Invalid material slots on "+r.name);
                for(int i=0;i<source.subMeshCount;i++)
                {
                    int slot=Slot(r,mats[i]);
                    var m=UnityEngine.Object.Instantiate(source);
                    temporary.Add(m);
                    var old=source.uv;
                    var uv=new Vector2[source.vertexCount];
                    for(int j=0;j<uv.Length;j++)
                    {
                        var coord=old.Length==uv.Length?old[j]:Vector2.one*.5f;
                        float x=Mathf.Repeat(coord.x,1),y=Mathf.Repeat(coord.y,1);
                        if(slot==8)
                        {
                            x=Mathf.Clamp01((coord.x*4-.025f)/.95f);
                            y=Mathf.Clamp01((coord.y*4-1.025f)/.95f);
                        }
                        if(slot==14)
                        {
                            x=Mathf.Clamp01((coord.x-.003f)/.869f);
                            y=Mathf.Clamp01((coord.y-.753f)/.243f);
                        }
                        uv[j]=new Vector2((slot%4+.025f+x*.95f)/4,(slot/4+.025f+y*.95f)/4);
                    }
                    m.uv=uv;
                    triangles+=source.GetIndexCount(i)/3;
                    entries.Add(new CombineInstance{mesh=m,subMeshIndex=i,transform=group.worldToLocalMatrix*f.transform.localToWorldMatrix});
                }
            }
            var combined=new Mesh{name="Ceiling Structure | Architecture Atlas",indexFormat=IndexFormat.UInt32};
            combined.CombineMeshes(entries.ToArray(),true,true);
            if(combined.subMeshCount!=1||combined.GetIndexCount(0)/3!=triangles)
                throw new InvalidOperationException("Combined mesh does not preserve the source triangle count.");
            combined.RecalculateTangents();
            combined.RecalculateBounds();
            if(combined.vertexCount<=65535)combined=ConvertTo16Bit(combined);
            var material=AssetDatabase.LoadAssetAtPath<Material>(Materials+"Architecture Ceiling Atlas.mat");
            if(!material)
            {
                material=new Material(shader);
                AssetDatabase.CreateAsset(material,Materials+"Architecture Ceiling Atlas.mat");
            }
            material.SetTexture("_BaseMap",TextureAt("Architecture Atlas.png"));
            material.SetTexture("_BumpMap",TextureAt("Architecture Normal.png"));
            material.SetTexture("_MetallicGlossMap",TextureAt("Architecture Surface.png"));
            material.SetTexture("_EmissionMap",TextureAt("Architecture Emission.png"));
            material.SetFloat("_Smoothness",1);
            material.SetFloat("_BumpScale",.72f);
            material.SetColor("_EmissionColor",new Color(4.1f,4.1f,4.1f,1));
            material.EnableKeyword("_NORMALMAP");
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            EditorUtility.SetDirty(material);
            if(AssetDatabase.LoadMainAssetAtPath(MeshPath))AssetDatabase.DeleteAsset(MeshPath);
            AssetDatabase.CreateAsset(combined,MeshPath);
            var node=new GameObject("Ceiling Structure | combined mesh");
            node.transform.SetParent(group,false);
            node.AddComponent<MeshFilter>().sharedMesh=combined;
            var renderer=node.AddComponent<MeshRenderer>();
            renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;
            foreach(var r in sources)
            {
                if(r.transform.childCount==0&&r.GetComponents<Component>().All(c=>c is Transform||c is MeshFilter||c is MeshRenderer))
                    UnityEngine.Object.DestroyImmediate(r.gameObject);
                else r.enabled=false;
            }
            if(group.GetComponentsInChildren<MeshRenderer>(true).Count(r=>r.enabled)!=1)
                throw new InvalidOperationException("Ceiling must have exactly one active mesh renderer.");
            int lights=group.GetComponentsInChildren<Light>(true).Length;
            AssetDatabase.SaveAssets();
            PrefabUtility.SaveAsPrefabAsset(root,Prefab,out var saved);
            if(!saved)throw new IOException("Could not save the combined ceiling prefab.");
            return $"DONE: {sources.Length} renderers → 1 mesh, 1 submesh, 1 material; {triangles} triangles; {lights} Unity lights retained.";
        }
        finally
        {
            foreach(var m in temporary)UnityEngine.Object.DestroyImmediate(m);
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
    static Mesh ConvertTo16Bit(Mesh source)
    {
        var m=new Mesh{name=source.name};
        m.vertices=source.vertices;
        m.normals=source.normals;
        m.tangents=source.tangents;
        m.uv=source.uv;
        m.triangles=source.triangles;
        m.bounds=source.bounds;
        UnityEngine.Object.DestroyImmediate(source);
        return m;
    }
    static Texture2D TextureAt(string name)
    {
        var t=AssetDatabase.LoadAssetAtPath<Texture2D>(Textures+name);
        if(!t)throw new InvalidOperationException("Missing atlas map: "+name);
        return t;
    }
    static int Slot(MeshRenderer r,Material m)
    {
        if(!m)throw new InvalidOperationException("Missing ceiling material on "+r.name);
        string n=m.name;
        if(n.Contains("fine mineral")||r.name=="Ceiling deck")return 8;
        if(n.Contains("illuminated apparel")||n.Contains("illuminated CLOTH SHOP"))return 14;
        if(n.Contains("violet"))return 13;
        if(n.Contains("diffuser")||n.Contains("opal lens")||n.Contains("warm lens"))return 12;
        if(n.Contains("conduit")||n.Contains("reflector")||r.name.Contains("conduit"))return 11;
        return 9;
    }
}
