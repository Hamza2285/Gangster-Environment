using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class CityMirageTrackGlow
{
    const string Prefab="Assets/Prefeb/Cloth Shop/Cloth Shop Environment.prefab";
    const string Request="Temp/CityMirageTrackGlow.request";
    const string Status="Temp/CityMirageTrackGlow.txt";
    [InitializeOnLoadMethod] static void Schedule()
    {if(!Application.isBatchMode&&File.Exists(Request))EditorApplication.delayCall+=Run;}
    [MenuItem("Tools/City Mirage/Show Track Spotlight Glow")]
    public static void Run()
    {
        try{File.WriteAllText(Status,Apply());File.Delete(Request);}
        catch(Exception e){File.WriteAllText(Status,e.ToString());Debug.LogException(e);}
    }
    static string Apply()
    {
        var root=PrefabUtility.LoadPrefabContents(Prefab);
        Mesh lens=null,combined=null;
        try
        {
            var node=root.GetComponentsInChildren<MeshFilter>(true).Single(f=>f.name=="Ceiling Structure | combined mesh");
            var existing=node.sharedMesh;
            var material=node.GetComponent<MeshRenderer>().sharedMaterial;
            if(!material||material.name!="Architecture Ceiling Atlas"||!material.GetTexture("_EmissionMap"))
                throw new InvalidOperationException("Ceiling Architecture Emission is missing.");
            if(existing.name.EndsWith("| track lens glow"))return "DONE (already applied)";
            var points=new List<Vector3>();var uv=new List<Vector2>();var tris=new List<int>();
            var dir=new Vector3(0,-.88f,-.47f).normalized;
            var tangent=Vector3.right;
            var bitangent=Vector3.Cross(dir,tangent).normalized;
            const int sides=12;
            foreach(float x in new[]{6.8f,4.5f,2.2f,-.4f,-2.8f,-5.3f,-7.4f})
            {
                int start=points.Count;
                Vector3 center=new Vector3(x,3.365f,-5.565f);
                points.Add(node.transform.InverseTransformPoint(center));
                uv.Add(new Vector2(.125f,.875f));
                for(int i=0;i<sides;i++)
                {
                    float a=i*2*Mathf.PI/sides;
                    points.Add(node.transform.InverseTransformPoint(center+.048f*(Mathf.Cos(a)*tangent+Mathf.Sin(a)*bitangent)));
                    uv.Add(new Vector2(.125f,.875f));
                    tris.AddRange(new[]{start,start+1+i,start+1+(i+1)%sides});
                }
            }
            lens=new Mesh{name="Track spotlight lenses"};
            lens.SetVertices(points);lens.SetUVs(0,uv);lens.SetTriangles(tris,0);lens.RecalculateNormals();
            lens.tangents=Enumerable.Repeat(new Vector4(1,0,0,1),lens.vertexCount).ToArray();
            combined=new Mesh{name=existing.name+" | track lens glow",
                indexFormat=existing.vertexCount+lens.vertexCount>65535?IndexFormat.UInt32:IndexFormat.UInt16};
            combined.CombineMeshes(new[]{new CombineInstance{mesh=existing,transform=Matrix4x4.identity},
                new CombineInstance{mesh=lens,transform=Matrix4x4.identity}},true,true);
            combined.RecalculateBounds();
            EditorUtility.CopySerialized(combined,existing);
            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssets();
            return "DONE: 7 warm track lenses, 84 added triangles, one existing ceiling material and Architecture Emission. No Light or Volume edits.";
        }
        finally
        {
            if(lens)UnityEngine.Object.DestroyImmediate(lens);
            if(combined)UnityEngine.Object.DestroyImmediate(combined);
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
