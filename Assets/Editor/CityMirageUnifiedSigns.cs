using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

public static class CityMirageUnifiedSigns
{
    const string ScenePath="Assets/Scenes/Cloth Shop.unity";
    const string Folder="Assets/Texture/City Mirage/";
    const string Request="Temp/CityMirageUnifiedSigns.request";
    const string Status="Temp/CityMirageUnifiedSigns.txt";
    const string Asset=Folder+"Unified Upper Signs.asset";
    static bool first;
    [InitializeOnLoadMethod] static void Schedule()
    {
        if(!Application.isBatchMode&&File.Exists(Request))EditorApplication.delayCall+=Run;
    }
    [MenuItem("Tools/City Mirage/Unify Upper Signboards")]
    public static void Run()
    {
        try{Apply();File.WriteAllText(Status,"DONE");File.Delete(Request);}
        catch(Exception e){File.WriteAllText(Status,e.ToString());Debug.LogException(e);}
    }
    static void Apply()
    {
        var scene=SceneManager.GetSceneByPath(ScenePath);
        if(scene.IsValid()&&scene.isDirty)throw new InvalidOperationException("Save pending Cloth Shop edits first.");
        if(!scene.IsValid()||!scene.isLoaded)scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
        if(scene.GetRootGameObjects().Any(o=>o.name=="Upper signs | unified meshes"))return;
        var rs=scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<MeshRenderer>(true)).ToArray();
        var groups=new[]{"Ceiling | CLOTH SHOP lightbox","Wall | CLOTH SHOP lightbox"};
        var pairs=groups.Select(prefix=>rs.Where(r=>r.name==prefix+" | readable face").ToArray()).ToArray();
        if(pairs.Any(p=>p.Length!=2))throw new InvalidOperationException("Expected two faces for each upper lightbox.");
        var entrance=rs.Single(r=>r.name=="Entrance | CLOTH SHOP banner faces");
        var previous=SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(scene);
        if(AssetDatabase.LoadMainAssetAtPath(Asset)!=null)AssetDatabase.DeleteAsset(Asset);
        first=true;
        var root=new GameObject("Upper signs | unified meshes");
        var casing=Mat("Banner - charcoal housing");
        var violet=Mat("Banner - violet tube");
        var artwork=Mat("Banner - illuminated apparel artwork");
        for(int i=0;i<groups.Length;i++)
        {
            var a=pairs[i][0];var b=pairs[i][1];
            var f=a.GetComponent<MeshFilter>();
            var local=f.sharedMesh.bounds;
            var p=a.transform.TransformPoint(local.center);
            var q=b.transform.TransformPoint(b.GetComponent<MeshFilter>().sharedMesh.bounds.center);
            var center=(p+q)*.5f;
            var depth=Vector3.Distance(p,q);
            var mesh=Lightbox(groups[i]+" | one connected mesh",local.size.x,local.size.y,depth,true);
            Put(root.transform,mesh,center,a.transform.rotation,new[]{casing,violet,artwork});
            foreach(var r in pairs[i])r.enabled=false;
            foreach(var r in rs.Where(r=>r.name==groups[i]+" | chamfered enclosure" ||
                r.GetComponent<MeshFilter>()?.sharedMesh?.name==groups[i]+" | chamfered enclosure"))r.enabled=false;
        }
        foreach(var r in rs.Where(r=>r.name=="Banner - violet tube | combined detail"))r.enabled=false;

        var ef=entrance.GetComponent<MeshFilter>();
        var eb=ef.sharedMesh.bounds;
        var entranceCenter=entrance.transform.TransformPoint(eb.center);
        var existingFrame=rs.Single(r=>r.name=="Entrance - charcoal frame");
        RemoveMarqueeHousing(existingFrame,entranceCenter);
        Put(root.transform,Lightbox("Entrance marquee | one connected mesh",eb.size.x+.13f,eb.size.y+.08f,eb.size.z,false),
            entranceCenter,entrance.transform.rotation,new[]{existingFrame.sharedMaterial,violet,entrance.sharedMaterial});
        entrance.enabled=false;
        AssetDatabase.SaveAssets();
        if(!EditorSceneManager.SaveScene(scene))throw new IOException("Could not save Cloth Shop.");
        if(previous.IsValid()&&previous.isLoaded&&previous!=scene)SceneManager.SetActiveScene(previous);
    }
    static Material Mat(string name)=>AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/City Mirage/"+name+".mat");
    static void SaveMesh(Mesh mesh)
    {
        if(first){AssetDatabase.CreateAsset(mesh,Asset);first=false;}else AssetDatabase.AddObjectToAsset(mesh,Asset);
    }
    static void Put(Transform root,Mesh mesh,Vector3 position,Quaternion rotation,Material[] materials)
    {
        SaveMesh(mesh);
        var go=new GameObject(mesh.name);go.transform.SetParent(root,false);go.transform.SetPositionAndRotation(position,rotation);
        go.AddComponent<MeshFilter>().sharedMesh=mesh;
        var r=go.AddComponent<MeshRenderer>();r.sharedMaterials=materials;r.shadowCastingMode=ShadowCastingMode.Off;
    }
    static void RemoveMarqueeHousing(MeshRenderer renderer,Vector3 center)
    {
        var f=renderer.GetComponent<MeshFilter>();
        var source=f.sharedMesh;
        var verts=source.vertices.Select(v=>renderer.transform.TransformPoint(v)-center).ToArray();
        var triangles=source.triangles;
        bool Inside(int i)=>Mathf.Abs(verts[i].x)<2.24f&&Mathf.Abs(verts[i].y)<.275f&&Mathf.Abs(verts[i].z)<.19f;
        var keep=new List<int>();
        for(int i=0;i<triangles.Length;i+=3)
            if(!Inside(triangles[i])||!Inside(triangles[i+1])||!Inside(triangles[i+2]))
                keep.AddRange(new[]{triangles[i],triangles[i+1],triangles[i+2]});
        if(keep.Count==triangles.Length)return;
        var mesh=UnityEngine.Object.Instantiate(source);mesh.name="Entrance frame | marquee section removed";
        mesh.SetTriangles(keep,0);mesh.RecalculateBounds();SaveMesh(mesh);f.sharedMesh=mesh;
    }
    static Mesh Lightbox(string name,float width,float height,float depth,bool clipped)
    {
        var vertices=new List<Vector3>();var uv=new List<Vector2>();
        int n=clipped?8:4;
        float cut=clipped?Mathf.Min(.17f,height*.235f):0;
        void Ring(float inset,float z,bool reverse)
        {
            float x=width*.5f-inset,y=height*.5f-inset,t=Mathf.Max(.018f,cut-inset*.52f);
            var profile=clipped?new[]{new Vector2(-x+t,-y),new Vector2(x-t,-y),new Vector2(x,-y+t),new Vector2(x,y-t),
                new Vector2(x-t,y),new Vector2(-x+t,y),new Vector2(-x,y-t),new Vector2(-x,-y+t)}
                :new[]{new Vector2(-x,-y),new Vector2(x,-y),new Vector2(x,y),new Vector2(-x,y)};
            foreach(var p in profile)
            {
                vertices.Add(new Vector3(p.x,p.y,z));
                uv.Add(new Vector2(.007f+(.5f+(reverse?-p.x:p.x)/(width-2*inset))*.86f,.758f+(.5f+p.y/(height-2*inset))*.232f));
            }
        }
        float z=depth*.5f;
        Ring(.027f,-z-.002f,false);
        Ring(.010f,-z-.002f,false);
        Ring(0,-z+.010f,false);
        Ring(0,z-.010f,true);
        Ring(.010f,z+.002f,true);
        Ring(.027f,z+.002f,true);
        var tris=new[]{new List<int>(),new List<int>(),new List<int>()};
        for(int ring=0;ring<5;ring++)
        for(int i=0;i<n;i++)
        {
            int a=ring*n+i,b=ring*n+(i+1)%n,c=a+n,d=b+n;
            tris[ring==0||ring==4?1:0].AddRange(new[]{a,b,d,a,d,c});
        }
        for(int i=1;i<n-1;i++)
        {
            tris[2].AddRange(new[]{0,i+1,i});
            int a=5*n;tris[2].AddRange(new[]{a,a+i,a+i+1});
        }
        var mesh=new Mesh{name=name};
        mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.subMeshCount=3;
        for(int i=0;i<3;i++)mesh.SetTriangles(tris[i],i);
        mesh.RecalculateNormals();mesh.RecalculateBounds();
        return mesh;
    }
}
