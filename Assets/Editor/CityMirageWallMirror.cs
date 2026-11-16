using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CityMirageWallMirror
{
    const string Shop = "Assets/Prefeb/Cloth Shop/Cloth Shop Environment.prefab";
    const string Folder = "Assets/Texture/City Mirage/";
    const string Request = "Temp/CityMirageWallMirror.request";
    const string Status = "Temp/CityMirageWallMirror.txt";
    const int X = 3790, Y = 1032, W = 276, H = 448;

    [InitializeOnLoadMethod] static void Queue()
    {
        if (!Application.isBatchMode && File.Exists(Request)) EditorApplication.delayCall += Run;
    }

    [MenuItem("Tools/City Mirage/Add Display Wall Mirror")]
    public static void Run()
    {
        try
        {
            File.WriteAllText(Status,Apply());
            if (File.Exists(Request)) File.Delete(Request);
        }
        catch(Exception e) { File.WriteAllText(Status,e.ToString()); Debug.LogException(e); }
    }

    static string Apply()
    {
        var root = PrefabUtility.LoadPrefabContents(Shop);
        try
        {
            if (root.transform.Find("Display wall | illuminated mirror")) return "DONE: mirror already present.";
            var rs = root.GetComponentsInChildren<MeshRenderer>(true);
            var left = rs.Single(r=>r.name=="Wall Bay 02 | textured charcoal lining").bounds;
            var right = rs.Single(r=>r.name=="Wall Bay 01 | textured charcoal lining").bounds;
            if (left.center.x > right.center.x) (left,right)=(right,left);
            float gap = right.min.x-left.max.x;
            float width = Mathf.Min(1.36f,gap-.16f);
            const float height = 2.45f;
            if (width < .62f) throw new InvalidOperationException($"Insufficient rack clearance ({gap:F2} m).");
            var center = new Vector3((left.max.x+right.min.x)*.5f,1.48f,(left.center.z+right.center.z)*.5f+.02f);
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/City Mirage/Clothes Atlas.mat");
            if (!material) throw new InvalidOperationException("Existing Clothes Atlas material missing.");

            var capture = Capture(root,center);
            var basePath = Folder+"Clothes Shared Atlas Base Color.png";
            var emissionPath = Folder+"Clothes Shared Atlas Emission.png";
            var baseBytes = Patch(basePath,capture,new Color(1f,.79f,.31f));
            var emissionBytes = Patch(emissionPath,null,new Color(1f,.66f,.17f));
            var mirror = new GameObject("Display wall | illuminated mirror");
            mirror.transform.SetParent(root.transform,false);
            mirror.transform.position = center;
            var mesh = MakeMesh(width,height);
            const string meshPath = Folder+"Rounded Display Wall Mirror.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (existing) { EditorUtility.CopySerialized(mesh,existing); UnityEngine.Object.DestroyImmediate(mesh); mesh=existing; }
            else AssetDatabase.CreateAsset(mesh,meshPath);
            mirror.AddComponent<MeshFilter>().sharedMesh = mesh;
            mirror.AddComponent<MeshRenderer>().sharedMaterial = material;
            AssetDatabase.SaveAssets();
            PrefabUtility.SaveAsPrefabAsset(root,Shop,out bool saved);
            if (!saved) throw new IOException("Could not save the wall mirror.");
            File.WriteAllBytes(basePath,baseBytes);
            File.WriteAllBytes(emissionPath,emissionBytes);
            AssetDatabase.ImportAsset(basePath,ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(emissionPath,ImportAssetOptions.ForceUpdate);
            return $"DONE: rounded {width:F2} × {height:F2} m mirror between wall bays 01 and 02, with warm atlas trim and a baked shop reflection.";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static Color[] Capture(GameObject root,Vector3 center)
    {
        var obj = new GameObject("Temporary mirror camera");
        SceneManager.MoveGameObjectToScene(obj,root.scene);
        var cam = obj.AddComponent<Camera>();
        cam.enabled = false;
        cam.transform.SetPositionAndRotation(center+Vector3.forward*.09f,Quaternion.LookRotation(new Vector3(.16f,-.06f,1f)));
        cam.fieldOfView=66f;
        cam.nearClipPlane=.08f;
        cam.farClipPlane=32f;
        var rt=RenderTexture.GetTemporary(W,H,24,RenderTextureFormat.ARGB32);
        var previous=RenderTexture.active;
        var pixels=new Texture2D(W,H,TextureFormat.RGBA32,false);
        try
        {
            cam.targetTexture=rt;
            cam.Render();
            RenderTexture.active=rt;
            pixels.ReadPixels(new Rect(0,0,W,H),0,0);
            var data=pixels.GetPixels();
            if (data.Max(c=>c.grayscale)-data.Min(c=>c.grayscale)<.12f)
                throw new InvalidOperationException("The mirror camera did not capture the shop.");
            return data;
        }
        finally
        {
            RenderTexture.active=previous;
            cam.targetTexture=null;
            RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.DestroyImmediate(pixels);
            UnityEngine.Object.DestroyImmediate(obj);
        }
    }

    static byte[] Patch(string path,Color[] capture,Color gold)
    {
        var t=new Texture2D(2,2,TextureFormat.RGBA32,false);
        try
        {
            if (!t.LoadImage(File.ReadAllBytes(path)) || t.width!=4096 || t.height!=2048)
                throw new InvalidOperationException("Unexpected Clothes Atlas size: "+path);
            if(capture!=null) t.SetPixels(X,Y,W,H,capture);
            t.SetPixels(4072,1040,16,16,Enumerable.Repeat(gold,256).ToArray());
            return t.EncodeToPNG();
        }
        finally { UnityEngine.Object.DestroyImmediate(t); }
    }

    static Mesh MakeMesh(float width,float height)
    {
        const int count=48;
        var vertices=new List<Vector3>{new Vector3(0,0,.002f)};
        var uv=new List<Vector2>{MirrorUV(.5f,.5f)};
        var triangles=new List<int>();
        for(int i=0;i<count;i++)
        {
            var inner=Round(i,width-.07f,height-.07f,.24f);
            vertices.Add(new Vector3(inner.x,inner.y,.002f));
            uv.Add(MirrorUV(inner.x/(width-.07f)+.5f,inner.y/(height-.07f)+.5f));
        }
        for(int i=0;i<count;i++)
        {
            int j=(i+1)%count;
            triangles.AddRange(new[]{0,i+1,j+1,0,j+1,i+1});
            var a=Round(i,width,height,.275f);
            var b=Round(j,width,height,.275f);
            var c=Round(i,width-.07f,height-.07f,.24f);
            var d=Round(j,width-.07f,height-.07f,.24f);
            int n=vertices.Count;
            vertices.AddRange(new[]{new Vector3(a.x,a.y,.018f),new Vector3(b.x,b.y,.018f),new Vector3(c.x,c.y,.018f),new Vector3(d.x,d.y,.018f)});
            for(int k=0;k<4;k++) uv.Add(new Vector2(3f+4080f/4096f,1048f/2048f));
            triangles.AddRange(new[]{n,n+1,n+2,n+2,n+1,n+3,n,n+2,n+1,n+2,n+3,n+1});
        }
        var mesh=new Mesh{name="Rounded illuminated wall mirror"};
        mesh.SetVertices(vertices);
        mesh.SetUVs(0,uv);
        mesh.SetTriangles(triangles,0);
        mesh.SetNormals(Enumerable.Repeat(Vector3.forward,vertices.Count).ToList());
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    static Vector2 MirrorUV(float x,float y) => new Vector2(4f+(X+3+(1-x)*(W-6))/4096f,(Y+3+y*(H-6))/2048f);

    static Vector2 Round(int index,float w,float h,float radius)
    {
        int corner=index/12;
        float angle=(corner*90+(index%12)*90f/11f)*Mathf.Deg2Rad;
        return new Vector2((corner==0||corner==3?1:-1)*(w*.5f-radius)+Mathf.Cos(angle)*radius,
            (corner<2?1:-1)*(h*.5f-radius)+Mathf.Sin(angle)*radius);
    }
}
