using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class CityMirageDoorHardware
{
    const string Prefab="Assets/Prefeb/Cloth Shop/Cloth Shop Environment.prefab";
    const string Asset="Assets/Texture/City Mirage/Stockroom Door Hardware.asset";
    const string Request="Temp/CityMirageDoorHardware.request";
    const string Status="Temp/CityMirageDoorHardware.txt";
    [InitializeOnLoadMethod] static void Schedule()
    {if(!Application.isBatchMode&&File.Exists(Request))EditorApplication.delayCall+=Run;}
    [MenuItem("Tools/City Mirage/Refine Stockroom Door Handle")]
    public static void Run()
    {
        try{File.WriteAllText(Status,Apply());File.Delete(Request);}
        catch(Exception e){File.WriteAllText(Status,e.ToString());Debug.LogException(e);}
    }
    static string Apply()
    {
        var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/City Mirage/Architecture Ceiling Atlas.mat");
        if(!material)throw new InvalidOperationException("Architecture atlas material missing.");
        var root=PrefabUtility.LoadPrefabContents(Prefab);
        try
        {
            var r=root.GetComponentsInChildren<MeshRenderer>(true).Single(x=>x.name=="Stockroom | lever handle");
            var filter=r.GetComponent<MeshFilter>();
            if(AssetDatabase.GetAssetPath(filter.sharedMesh)==Asset)return "DONE (already updated)";
            var door=root.GetComponentsInChildren<MeshRenderer>(true).Single(x=>x.name=="Stockroom | door leaf with vision cutout").bounds;
            var b=r.bounds;
            var anchor=new Vector3(door.max.x+.004f,b.center.y,b.max.z-.031f);
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var tris=new List<int>();
            void Tube(Vector3[] path,float[] radii,int sides,int slot)
            {
                int start=vertices.Count;
                for(int i=0;i<path.Length;i++)
                {
                    Vector3 forward=(path[Mathf.Min(i+1,path.Length-1)]-path[Mathf.Max(i-1,0)]).normalized;
                    Vector3 side=Vector3.Cross(forward,Vector3.up).normalized;
                    Vector3 up=Vector3.Cross(forward,side);
                    for(int j=0;j<sides;j++)
                    {
                        float a=j*2*Mathf.PI/sides;
                        var point=path[i]+(side*Mathf.Cos(a)+up*Mathf.Sin(a))*radii[i];
                        vertices.Add(r.transform.InverseTransformPoint(anchor+point));
                        uv.Add(new Vector2((slot%4+.12f+.76f*j/sides)/4,(slot/4+.12f+.76f*i/(path.Length-1))/4));
                        if(i==0)continue;
                        int A=start+(i-1)*sides+j,B=start+(i-1)*sides+(j+1)%sides;
                        tris.AddRange(new[]{A,B,B+sides,A,B+sides,A+sides});
                    }
                }
                int tail=start+(path.Length-1)*sides;
                for(int i=1;i<sides-1;i++)
                    tris.AddRange(new[]{start,start+i+1,start+i,tail,tail+i,tail+i+1});
            }
            // Beveled escutcheon and pivot collar.
            Tube(new[]{new Vector3(.002f,0,0),new Vector3(.006f,0,0),new Vector3(.017f,0,0),new Vector3(.022f,0,0)},
                new[]{.044f,.049f,.049f,.042f},16,11);
            Tube(new[]{new Vector3(.021f,0,0),new Vector3(.035f,0,0),new Vector3(.044f,0,0)},new[]{.022f,.021f,.017f},12,9);
            // Soft return at the end of the lever keeps the silhouette believable.
            Tube(new[]{new Vector3(.034f,0,0),new Vector3(.051f,0,0),new Vector3(.064f,0,-.010f),
                       new Vector3(.069f,0,-.033f),new Vector3(.067f,-.003f,-.142f),
                       new Vector3(.058f,-.004f,-.163f),new Vector3(.047f,-.004f,-.169f)},
                new[]{.015f,.017f,.017f,.015f,.014f,.012f,.008f},12,11);
            foreach(float y in new[]{-.032f,.032f})
                Tube(new[]{new Vector3(.021f,y,0),new Vector3(.024f,y,0)},new[]{.004f,.004f},8,9);
            var mesh=new Mesh{name="Stockroom | brushed lever, rose and screws"};
            mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(tris,0);
            mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();
            if(AssetDatabase.LoadMainAssetAtPath(Asset))AssetDatabase.DeleteAsset(Asset);
            AssetDatabase.CreateAsset(mesh,Asset);
            filter.sharedMesh=mesh;r.sharedMaterial=material;
            AssetDatabase.SaveAssets();
            PrefabUtility.SaveAsPrefabAsset(root,Prefab,out var saved);
            if(!saved)throw new IOException("Could not save stockroom hardware.");
            return $"DONE: rounded lever, mounting rose and screws, one Architecture Atlas material, {tris.Count/3} triangles.";
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }
}
