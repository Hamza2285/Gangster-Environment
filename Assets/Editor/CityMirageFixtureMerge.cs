using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class CityMirageFixtureMerge
{
    const string Prefab="Assets/Prefeb/Cloth Shop/Cloth Shop Environment.prefab";
    const string Request="Temp/CityMirageFixtureMerge.request";
    const string Status="Temp/CityMirageFixtureMerge.txt";
    [InitializeOnLoadMethod] static void Schedule()
    {
        if(!Application.isBatchMode&&File.Exists(Request))EditorApplication.delayCall+=Run;
    }
    [MenuItem("Tools/City Mirage/Merge Reference Fixtures Into Ceiling")]
    public static void Run()
    {
        try{File.WriteAllText(Status,Apply());File.Delete(Request);}
        catch(Exception e){File.WriteAllText(Status,e.ToString());Debug.LogException(e);}
    }
    static string Apply()
    {
        var root=PrefabUtility.LoadPrefabContents(Prefab);
        var copies=new List<Mesh>();
        try
        {
            var all=root.GetComponentsInChildren<Transform>(true);
            var ceiling=all.Single(t=>t.name=="Ceiling Structure | combined mesh");
            var group=all.FirstOrDefault(t=>t.name=="Reference | fixture detail");
            if(!group)return "DONE (fixtures already merged)";
            var renderers=group.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r=>r.enabled&&r.gameObject.activeSelf&&r.TryGetComponent<MeshFilter>(out var f)&&f.sharedMesh).ToArray();
            if(renderers.Length==0)throw new InvalidOperationException("No active fixture renderers found.");
            var filter=ceiling.GetComponent<MeshFilter>();
            var original=filter.sharedMesh;
            var material=ceiling.GetComponent<MeshRenderer>().sharedMaterial;
            var emission=material.GetTexture("_EmissionMap");
            if(!emission||emission.name!="Architecture Emission")throw new InvalidOperationException("Architecture emission atlas missing.");
            long triangles=original.GetIndexCount(0)/3;
            var combine=new List<CombineInstance>{new CombineInstance{mesh=original,transform=Matrix4x4.identity}};
            foreach(var renderer in renderers)
            {
                var f=renderer.GetComponent<MeshFilter>();
                var mesh=f.sharedMesh;
                for(int i=0;i<mesh.subMeshCount;i++)
                {
                    var name=renderer.sharedMaterials[i].name;
                    int slot=name.Contains("cyan")?6:name.Contains("violet")?13:name.Contains("lens")?12:name.Contains("reflector")?11:name.Contains("charcoal")?9:-1;
                    if(slot<0)throw new InvalidOperationException("Unmapped fixture material: "+name);
                    var copy=UnityEngine.Object.Instantiate(mesh);
                    copies.Add(copy);
                    var uv=new Vector2((slot%4+.5f)/4,(slot/4+.5f)/4);
                    copy.uv=Enumerable.Repeat(uv,copy.vertexCount).ToArray();
                    combine.Add(new CombineInstance{mesh=copy,subMeshIndex=i,transform=ceiling.worldToLocalMatrix*f.transform.localToWorldMatrix});
                    triangles+=mesh.GetIndexCount(i)/3;
                }
            }
            var combined=new Mesh{name=original.name,indexFormat=IndexFormat.UInt32};
            copies.Add(combined);
            combined.CombineMeshes(combine.ToArray(),true,true);
            if(combined.subMeshCount!=1||combined.GetIndexCount(0)/3!=triangles)throw new InvalidOperationException("Combined topology mismatch.");
            combined.RecalculateBounds();
            // The existing mesh asset is kept so its Unity references stay valid.
            if(combined.vertexCount<=65535)
            {
                var small=new Mesh{name=combined.name};
                copies.Add(small);
                small.vertices=combined.vertices;small.normals=combined.normals;small.tangents=combined.tangents;
                small.uv=combined.uv;small.triangles=combined.triangles;small.bounds=combined.bounds;
                combined=small;
            }
            EditorUtility.CopySerialized(combined,original);
            EditorUtility.SetDirty(original);
            foreach(var r in renderers)
            {
                if(r.transform.childCount==0&&r.GetComponents<Component>().All(c=>c is Transform||c is MeshFilter||c is MeshRenderer))
                    UnityEngine.Object.DestroyImmediate(r.gameObject);
                else r.enabled=false;
            }
            if(group.GetComponentsInChildren<Light>(true).Length==0&&
                group.GetComponentsInChildren<Component>(true).All(c=>c is Transform||c is MeshFilter||c is MeshRenderer)&&
                group.GetComponentsInChildren<MeshRenderer>(true).All(r=>!r.enabled))
                UnityEngine.Object.DestroyImmediate(group.gameObject);
            AssetDatabase.SaveAssets();
            PrefabUtility.SaveAsPrefabAsset(root,Prefab,out var saved);
            if(!saved)throw new IOException("Environment prefab save failed.");
            return $"DONE: {renderers.Length} fixture renderer(s) merged. One ceiling mesh, one submesh, one Architecture Atlas material, {triangles} triangles. Architecture Emission assigned.";
        }
        finally
        {
            foreach(var mesh in copies)UnityEngine.Object.DestroyImmediate(mesh);
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
