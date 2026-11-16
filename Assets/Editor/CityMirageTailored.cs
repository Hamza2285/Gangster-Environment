using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class CityMirageTailored
{
    const string Prefab="Assets/Prefeb/Cloth Shop/Cloth Shop Environment.prefab";
    const string ScenePath="Assets/Scenes/Cloth Shop.unity";
    const string Asset="Assets/Texture/City Mirage/Tailored Hanging Clothes Refined.asset";
    const string Request="Temp/CityMirageTailored.request";
    const string Status="Temp/CityMirageTailored.txt";
    [Serializable] class Shape {public Vector3[] vertices;public int[] triangles;public int[] kinds;}
    [Serializable] class Shapes {public Shape buttonDown;public Shape casual;public Shape tee;}
    static bool first;
    [InitializeOnLoadMethod] static void Schedule()
    {if(!Application.isBatchMode&&File.Exists(Request))EditorApplication.delayCall+=Run;
        if(!Application.isBatchMode&&File.Exists("Temp/CityMirageTailoredReview.request"))EditorApplication.delayCall+=Review;
        if(!Application.isBatchMode&&File.Exists("Temp/CityMirageDrapeReview.request"))EditorApplication.delayCall+=DrapeReview;}
    [MenuItem("Tools/City Mirage/Apply Tailored Shirts and Blue Track Glow")]
    public static void Run()
    {
        try{File.WriteAllText(Status,Apply());File.Delete(Request);}
        catch(Exception e){File.WriteAllText(Status,e.ToString());Debug.LogException(e);}
    }
    static string Apply()
    {
        var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/City Mirage/Clothes Atlas.mat");
        var data=JsonUtility.FromJson<Shapes>(File.ReadAllText("Assets/Editor/CityMirageTailoredShapes.json"));
        if(!material||data?.buttonDown?.vertices==null||data.casual?.vertices==null||data.tee?.vertices==null)
            throw new InvalidOperationException("Missing tailored garments or Clothes Atlas.");
        var root=PrefabUtility.LoadPrefabContents(Prefab);
        Vector3 previewCenter=Vector3.zero;
        try
        {
            var rs=root.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
            var shirts=rs.Where(r=>r.name.StartsWith("T-shirt |")||r.name.StartsWith("Hoodie |")||r.name.StartsWith("Casual shirt |")||r.name.StartsWith("Button-down |")).ToArray();
            var hangers=rs.Where(r=>r.name.StartsWith("Wall bay | bent hanger")||r.name.StartsWith("Hanger | shoulder and hook")).ToList();
            if(shirts.Length!=68||hangers.Count!=68)throw new InvalidOperationException($"Expected 68 shirts and hangers. Found {shirts.Length}/{hangers.Count}.");
            var ceiling=rs.Single(r=>r.name=="Ceiling Structure | combined mesh").GetComponent<MeshFilter>().sharedMesh;
            var lensUV=ceiling.uv;
            bool blueAlready=ceiling.name.Contains("| blue lens");
            if(!blueAlready)
            {
                if(!ceiling.name.Contains("track lens glow")||lensUV.Length<91||
                    lensUV.Skip(lensUV.Length-91).Any(v=>Vector2.Distance(v,new Vector2(.125f,.875f))>.0001f))
                    throw new InvalidOperationException("Track lens UVs could not be identified.");
            }
            if(shirts.All(r=>AssetDatabase.GetAssetPath(r.GetComponent<MeshFilter>().sharedMesh)==Asset)&&blueAlready)
                return "DONE (already applied)";
            var pairs=new List<(MeshRenderer cloth,MeshRenderer hanger,bool wall)>();
            foreach(var r in shirts.OrderBy(r=>Mathf.Abs(r.bounds.center.z-1.5f)).ThenBy(r=>r.bounds.center.x))
            {
                bool wall=r.name.Contains("| wall");
                var h=hangers.Where(x=>x.name.StartsWith("Wall bay")==wall).OrderBy(x=>Gap(r.bounds,x.bounds)).FirstOrDefault();
                if(!h||Gap(r.bounds,h.bounds)>.12f)throw new InvalidOperationException("Could not match hanger: "+r.name);
                hangers.Remove(h);pairs.Add((r,h,wall));
            }
            first=!AssetDatabase.LoadMainAssetAtPath(Asset);
            var variants=new Dictionary<(Quaternion,int,int,int),Mesh>();
            var wooden=new Dictionary<(Mesh,Quaternion),Mesh>();
            int count=0;
            foreach(var pair in pairs)
            {
                var (r,h,wall)=pair;
                int style=count%3;
                int palette=style*2+(count/3)%2;
                bool sideWall=wall&&Mathf.Abs(h.bounds.center.x)>8;
                int direction=sideWall?1:0;
                var front=sideWall?Vector3.left:Vector3.forward;
                var f=r.GetComponent<MeshFilter>();
                var key=(r.transform.rotation,style,palette,direction);
                if(!variants.TryGetValue(key,out var mesh))
                {
                    mesh=MakeGarment(r,new[]{data.buttonDown,data.casual,data.tee}[style],style,palette,front);
                    Save(mesh);variants.Add(key,mesh);
                }
                var b=h.bounds;
                float shoulder=b.min.y+b.size.y*.51f;
                r.transform.position=new Vector3(b.center.x,shoulder-.018f,b.center.z)+front*.009f;
                string n=new[]{"Button-down","Casual shirt","T-shirt"}[style]+" | "+(wall?"wall":"rack")+" "+(count+1).ToString("00");
                r.gameObject.name=n;
                Bind(r,mesh,material);
                if(PrefabUtility.IsPartOfPrefabInstance(r.transform))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(r.transform);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(r.gameObject);
                }
                var hf=h.GetComponent<MeshFilter>();
                var old=hf.sharedMesh;
                var hkey=(old,h.transform.rotation);
                if(!wooden.TryGetValue(hkey,out var fitted))
                {
                    fitted=old.name.StartsWith("Wooden shoulder hanger")?old:WoodHanger(h,old);if(fitted!=old)Save(fitted);wooden.Add(hkey,fitted);
                }
                Bind(h,fitted,material);
                if(!wall&&previewCenter==Vector3.zero)previewCenter=b.center;
                count++;
            }
            if(!blueAlready)
            {
                for(int i=lensUV.Length-91;i<lensUV.Length;i++)lensUV[i]=new Vector2(.625f,.4025f);
                ceiling.uv=lensUV;ceiling.name+=" | blue lens";EditorUtility.SetDirty(ceiling);
            }
            var used=rs.SelectMany(r=>r.sharedMaterials).Distinct().Count();
            if(used!=3)throw new InvalidOperationException("The visible material count changed.");
            AssetDatabase.SaveAssets();
            PrefabUtility.SaveAsPrefabAsset(root,Prefab,out var saved);
            if(!saved)throw new IOException("Could not save tailored display prefab.");
            var point=previewCenter;
            EditorApplication.delayCall+=Review;
            return $"DONE: {count} fitted sleeved garments in 3 styles, {variants.Count} shared variants, wooden hanger arms and metal hooks, 7 blue track lenses on existing Architecture Emission. Exactly 3 materials.";
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }
    [MenuItem("Tools/City Mirage/Apply Three-Shirt Drape Test")]
    static void DrapeReview()
    {
        const string status="Temp/CityMirageDrapeReview.txt";
        const string request="Temp/CityMirageDrapeReview.request";
        const string asset="Assets/Texture/City Mirage/Three Shirt Drape Test V3.asset";
        GameObject root=null;
        try
        {
            root=PrefabUtility.LoadPrefabContents(Prefab);
            var rs=root.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
            var rack=rs.Where(r=>r.name.StartsWith("Rack | welded")).OrderBy(r=>Mathf.Abs(r.bounds.center.z-1.5f))
                .ThenByDescending(r=>r.bounds.center.x).First();
            var b=rack.bounds;
            var candidates=rs.Where(r=>r.name.Contains("| rack")&&
                Mathf.Abs(r.bounds.center.x-b.center.x)<.42f&&Mathf.Abs(r.bounds.center.z-b.center.z)<b.extents.z+.14f)
                .OrderByDescending(r=>r.bounds.center.z).Take(3).ToArray();
            if(candidates.Length!=3)throw new InvalidOperationException("Could not isolate three rack garments.");
            if(candidates.All(r=>AssetDatabase.GetAssetPath(r.GetComponent<MeshFilter>().sharedMesh)==asset))
            {
                File.WriteAllText(status,"DONE (three garments already fitted)");
                File.Delete(request);
                EditorApplication.delayCall+=Review;
                return;
            }
            var data=JsonUtility.FromJson<Shapes>(File.ReadAllText("Assets/Editor/CityMirageDrapeReviewShapes.json"));
            var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/City Mirage/Clothes Atlas.mat");
            if(!material||data?.buttonDown?.vertices==null)throw new InvalidOperationException("Missing test meshes or atlas.");
            if(AssetDatabase.LoadMainAssetAtPath(asset))throw new InvalidOperationException("Existing test mesh asset requires inspection.");
            for(int i=0;i<3;i++)
            {
                var r=candidates[i];
                var mesh=MakeGarment(r,new[]{data.buttonDown,data.casual,data.tee}[i],i,i*2,Vector3.forward);
                if(i==0)AssetDatabase.CreateAsset(mesh,asset);else AssetDatabase.AddObjectToAsset(mesh,asset);
                r.transform.position+=new Vector3(0,.024f,.019f);
                if(PrefabUtility.IsPartOfPrefabInstance(r.transform))PrefabUtility.RecordPrefabInstancePropertyModifications(r.transform);
                r.gameObject.name=new[]{"Button-down","Casual shirt","T-shirt"}[i]+" | rack drape test";
                Bind(r,mesh,material);
                if(PrefabUtility.IsPartOfPrefabInstance(r.gameObject))PrefabUtility.RecordPrefabInstancePropertyModifications(r.gameObject);
            }
            AssetDatabase.SaveAssets();
            PrefabUtility.SaveAsPrefabAsset(root,Prefab,out var saved);
            if(!saved)throw new IOException("Could not save the three-shirt test.");
            File.WriteAllText(status,"DONE: one button-down, casual shirt and T-shirt on the review rack. Existing atlas retained.");
            File.Delete(request);
            EditorApplication.delayCall+=Review;
        }
        catch(Exception e){File.WriteAllText(status,e.ToString());Debug.LogException(e);}
        finally{if(root)PrefabUtility.UnloadPrefabContents(root);}
    }
    static float Gap(Bounds a,Bounds b)
    {
        var d=a.center-b.center;
        return d.x*d.x+d.z*d.z+Mathf.Pow(a.max.y-b.min.y-b.size.y*.51f,2)*.25f;
    }
    static void Save(Mesh m)
    {if(first){AssetDatabase.CreateAsset(m,Asset);first=false;}else AssetDatabase.AddObjectToAsset(m,Asset);}
    static void Bind(MeshRenderer r,Mesh mesh,Material mat)
    {
        var f=r.GetComponent<MeshFilter>();
        f.sharedMesh=mesh;r.sharedMaterials=new[]{mat};
        if(PrefabUtility.IsPartOfPrefabInstance(f))
        {PrefabUtility.RecordPrefabInstancePropertyModifications(f);PrefabUtility.RecordPrefabInstancePropertyModifications(r);}
    }
    static Mesh MakeGarment(MeshRenderer r,Shape shape,int style,int palette,Vector3 front)
    {
        var min=shape.vertices.Aggregate(Vector3.Min);
        var max=shape.vertices.Aggregate(Vector3.Max);
        var v=new Vector3[shape.vertices.Length];var uv=new Vector2[v.Length];
        var width=Vector3.Cross(Vector3.up,front).normalized;
        float h=new[]{.73f,.65f,.62f}[style];
        float w=new[]{.65f,.65f,.67f}[style];
        for(int i=0;i<v.Length;i++)
        {
            var p=shape.vertices[i];
            float x=(p.x-min.x)/(max.x-min.x),z=(p.z-min.z)/(max.z-min.z);
            v[i]=r.transform.InverseTransformVector(width*((x-.5f)*w)+Vector3.up*((z-1)*h)-front*(p.y*.87f));
            uv[i]=shape.kinds[i]==2?new Vector2(1.518f,.3125f):
                new Vector2(1.625f+(palette+.035f+x*.93f)/16f,.008f+z*.235f);
        }
        var mesh=new Mesh{name=new[]{"Tailored long-sleeve button-down","Casual short-sleeve collared shirt","Crew-neck T-shirt"}[style]+" | color "+palette};
        mesh.vertices=v;mesh.uv=uv;mesh.triangles=shape.triangles;
        mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();
        return mesh;
    }
    static Mesh WoodHanger(MeshRenderer r,Mesh old)
    {
        var mesh=UnityEngine.Object.Instantiate(old);
        mesh.name="Wooden shoulder hanger | gold hook";
        var b=r.bounds;
        var v=mesh.vertices;
        var uv=new Vector2[v.Length];
        bool alongX=b.size.x>b.size.z;
        for(int i=0;i<v.Length;i++)
        {
            var p=r.transform.TransformPoint(v[i]);
            float h=(p.y-b.min.y)/b.size.y;
            bool hook=h>.57f;
            if(!hook)
            {
                if(alongX)p.z=b.center.z+(p.z-b.center.z)*1.17f;
                else p.x=b.center.x+(p.x-b.center.x)*1.17f;
                v[i]=r.transform.InverseTransformPoint(p);
            }
            uv[i]=hook?new Vector2(1.8125f,.375f):new Vector2(1.6875f,.375f);
        }
        mesh.vertices=v;mesh.uv=uv;mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }
    [MenuItem("Tools/City Mirage/Capture Tailored Rack Review")]
    public static void Review()
    {
        try
        {
            var scene=SceneManager.GetSceneByPath(ScenePath);
            bool opened=!scene.IsValid()||!scene.isLoaded;
            if(opened)scene=UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath,UnityEditor.SceneManagement.OpenSceneMode.Additive);
            try
            {
                var rs=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<MeshRenderer>(true))
                    .Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
                var rack=rs.Where(r=>r.name.StartsWith("Rack | welded")).OrderBy(r=>Mathf.Abs(r.bounds.center.z-1.5f))
                    .ThenByDescending(r=>r.bounds.center.x).First();
                var b=rack.bounds;
                Preview(new Vector3(b.center.x,b.max.y,b.center.z),scene);
            }
            finally{if(opened)UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene,true);}
            File.Delete("Temp/CityMirageTailoredReview.request");
        }
        catch(Exception e){File.WriteAllText("Temp/CityMirageTailoredReview.txt",e.ToString());Debug.LogException(e);}
    }
    static void Preview(Vector3 point,Scene target=default)
    {
        var scene=target.IsValid()?target:SceneManager.GetSceneByPath(ScenePath);
        if(!scene.IsValid()||!scene.isLoaded)throw new InvalidOperationException("Cloth Shop scene is not loaded.");
        var cam=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).FirstOrDefault(c=>c.CompareTag("MainCamera"));
        if(!cam)throw new InvalidOperationException("Main Camera not found.");
        var pos=cam.transform.position;var rot=cam.transform.rotation;var fov=cam.fieldOfView;
        var old=RenderTexture.active;
        var rt=new RenderTexture(1350,900,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
        Texture2D pixels=null;
        try
        {
            cam.transform.position=point+new Vector3(1.55f,.42f,2.9f);
            cam.transform.LookAt(point+new Vector3(0,-.64f,0));
            cam.fieldOfView=45;
            var request=new UniversalRenderPipeline.SingleCameraRequest{destination=rt};
            if(!RenderPipeline.SupportsRenderRequest(cam,request))throw new InvalidOperationException("The active render pipeline cannot capture this camera.");
            RenderPipeline.SubmitRenderRequest(cam,request);
            RenderTexture.active=rt;
            pixels=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
            pixels.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);pixels.Apply();
            File.WriteAllBytes(@"C:\Users\Gamebox\.codex\visualizations\2026\11\04\01a1a638-bfb7-7120-9a6a-ae943dd0056f\Tailored-Clothes-Unity.png",pixels.EncodeToPNG());
            File.WriteAllText("Temp/CityMirageTailoredReview.txt","DONE: rack camera render saved.");
        }
        catch(Exception e){File.WriteAllText("Temp/CityMirageTailoredReview.txt",e.ToString());Debug.LogWarning(e.Message);}
        finally
        {
            cam.transform.SetPositionAndRotation(pos,rot);cam.fieldOfView=fov;
            RenderTexture.active=old;rt.Release();UnityEngine.Object.DestroyImmediate(rt);
            if(pixels)UnityEngine.Object.DestroyImmediate(pixels);
        }
    }
}






