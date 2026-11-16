using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class CityMirageMockup
{
    const string ScenePath="Assets/Scenes/Cloth Shop.unity";
    const string Folder="Assets/Texture/City Mirage/";
    const string Request="Temp/CityMirageMockup.request";
    const string Status="Temp/CityMirageMockup.txt";
    const string Asset=Folder+"Hanging Garments.asset";
    [Serializable] class Shape { public Vector3[] vertices; public int[] triangles; }
    [Serializable] class Shapes { public Shape tee; public Shape hoodie; }
    [InitializeOnLoadMethod] static void Schedule()
    {
        if(!Application.isBatchMode&&File.Exists(Request))EditorApplication.update+=ApplyWhenSaved;
    }
    static void ApplyWhenSaved()
    {
        if(EditorApplication.isCompiling||EditorApplication.isPlayingOrWillChangePlaymode)return;
        var shop=SceneManager.GetSceneByPath(ScenePath);
        if(shop.IsValid()&&shop.isDirty)return;
        EditorApplication.update-=ApplyWhenSaved;
        Run();
    }
    [MenuItem("Tools/City Mirage/Apply After Mockup Look")]
    public static void Run()
    {
        try{File.WriteAllText(Status,Apply());File.Delete(Request);}
        catch(Exception e){File.WriteAllText(Status,e.ToString());Debug.LogException(e);}
    }
    static string Apply()
    {
        var scene=SceneManager.GetSceneByPath(ScenePath);
        if(scene.IsValid()&&scene.isDirty)throw new InvalidOperationException("Save pending Cloth Shop edits first.");
        if(!scene.IsValid()||!scene.isLoaded)scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
        var root=scene.GetRootGameObjects().Single(o=>o.name=="City Mirage | Lighting");
        if(root.transform.Find("After mockup | warm aisle fill"))return "DONE (already applied)";
        var renderers=scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<MeshRenderer>(true)).ToArray();
        var shirts=renderers.Where(r=>r.enabled&&(r.name.StartsWith("T-shirt |")||r.name.StartsWith("Hoodie |"))).ToArray();
        if(shirts.Length<50)throw new InvalidOperationException($"Expected hanging garments, found {shirts.Length}.");
        var data=JsonUtility.FromJson<Shapes>(File.ReadAllText("Assets/Editor/CityMirageGarmentShapes.json"));
        if(data?.tee?.vertices==null||data?.hoodie?.vertices==null||data.tee.vertices.Length<150||data.hoodie.vertices.Length<150)throw new InvalidOperationException("Blender garment mesh data is incomplete.");
        var floor=MaterialAt("Floor and Walls PBR");
        var counter=renderers.Single(r=>r.name.StartsWith("Checkout | welded counter"));
        var ceiling=MaterialAt("Ceiling - bright warm diffuser");
        var artwork=MaterialAt("Banner - illuminated apparel artwork");
        var neon=MaterialAt("Banner - violet tube");
        if(!floor||!ceiling||!artwork||!neon)throw new InvalidOperationException("Expected the existing floor, pendant and banner materials.");
        if(AssetDatabase.LoadMainAssetAtPath(Asset)!=null)AssetDatabase.DeleteAsset(Asset);
        var variants=new Dictionary<(Mesh,bool,int,int,int),Mesh>();
        int previousTriangles=0,updatedTriangles=0;
        foreach(var shirt in shirts)
        {
            var filter=shirt.GetComponent<MeshFilter>();
            var old=filter.sharedMesh;
            previousTriangles+=(int)old.GetIndexCount(0)/3;
            var sample=old.uv[0]*4;
            int tile=Mathf.Clamp(Mathf.FloorToInt(sample.x),0,3)+4*Mathf.Clamp(Mathf.FloorToInt(sample.y),0,3);
            var axis=Enumerable.Range(0,3).OrderByDescending(i=>Mathf.Abs(shirt.transform.TransformDirection(Axis(i)).y)).ToArray();
            int up=axis[0],wide=old.bounds.size[axis[1]]>=old.bounds.size[axis[2]]?axis[1]:axis[2];
            bool hoodie=shirt.name.StartsWith("Hoodie");
            int direction=shirt.transform.TransformDirection(Axis(up)).y>0?1:-1;
            var key=(old,hoodie,tile,up,direction);
            if(!variants.TryGetValue(key,out var newMesh))
            {
                newMesh=Garment(hoodie?data.hoodie:data.tee,old.bounds,up,wide,direction,tile,hoodie);
                if(variants.Count==0)AssetDatabase.CreateAsset(newMesh,Asset);else AssetDatabase.AddObjectToAsset(newMesh,Asset);
                variants.Add(key,newMesh);
            }
            updatedTriangles+=(int)newMesh.GetIndexCount(0)/3;
            filter.sharedMesh=newMesh;
        }
        var prior=SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(scene);
        var lightRoot=new GameObject("After mockup | warm aisle fill");
        lightRoot.transform.SetParent(root.transform,false);
        foreach(var l in root.GetComponentsInChildren<Light>(true))
        {
            if(l.name.Contains("warm cloth wash"))
            {
                l.color=new Color(1f,.90f,.77f);
                l.intensity*=.72f;
                l.spotAngle=115;
                l.innerSpotAngle=77;
                l.range=Mathf.Max(l.range,4.25f);
            }
            else if(l.name=="Center warm wash"||l.name=="Front warm wash")
            {
                l.color=new Color(1f,.90f,.77f);
                l.intensity*=1.16f;
                l.spotAngle=117;
                l.innerSpotAngle=76;
            }
        }
        Fill(lightRoot.transform,"Foreground rack and counter fill",new Vector3(-4.1f,3.12f,3.15f),new Vector3(-4.0f,.35f,1.85f),4.1f);
        Fill(lightRoot.transform,"Soft side aisle fill",new Vector3(4.8f,3.12f,2.7f),new Vector3(4.25f,.35f,1.9f),3.8f);
        RenderSettings.ambientMode=AmbientMode.Flat;
        RenderSettings.ambientLight=new Color(.64f,.58f,.52f);
        Emit(ceiling,new Color(4.25f,3.78f,3.08f));
        Emit(artwork,new Color(3.1f,3.1f,3.1f));
        Emit(neon,new Color(3.9f,.87f,5.7f));
        floor.SetFloat("_Smoothness",.94f);
        EditorUtility.SetDirty(floor);
        var satin=MaterialAt("Checkout - satin graphite");
        if(!satin)
        {
            satin=new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(satin,"Assets/Materials/City Mirage/"+"Checkout - satin graphite.mat");
        }
        satin.SetColor("_BaseColor",new Color(.14f,.126f,.133f));
        satin.SetFloat("_Metallic",.17f);
        satin.SetFloat("_Smoothness",.57f);
        EditorUtility.SetDirty(satin);
        counter.sharedMaterial=satin;
        var vol=root.GetComponent<Volume>();
        if(vol.sharedProfile.TryGet<Bloom>(out var bloom))
        {
            bloom.threshold.Override(1.12f);
            bloom.intensity.Override(.71f);
            bloom.scatter.Override(.68f);
            EditorUtility.SetDirty(bloom);
        }
        AssetDatabase.SaveAssets();
        if(!EditorSceneManager.SaveScene(scene))throw new IOException("Could not save Cloth Shop scene.");
        if(prior.IsValid()&&prior.isLoaded&&prior!=scene)SceneManager.SetActiveScene(prior);
        EditorApplication.delayCall+=()=>Preview(scene);
        return $"DONE: {shirts.Length} garments, {variants.Count} shared mesh variants, {previousTriangles} to {updatedTriangles} rendered garment triangles. Two shadowless fill lights.";
    }
    static Material MaterialAt(string n)=>AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/City Mirage/"+n+".mat");
    static void Emit(Material m,Color color)
    {
        m.SetColor("_EmissionColor",color);m.globalIlluminationFlags=MaterialGlobalIlluminationFlags.None;
        m.EnableKeyword("_EMISSION");EditorUtility.SetDirty(m);
    }
    static Vector3 Axis(int i){var a=Vector3.zero;a[i]=1;return a;}
    static Mesh Garment(Shape s,Bounds target,int up,int width,int vertical,int tile,bool hoodie)
    {
        var b=new Bounds(s.vertices[0],Vector3.zero);
        foreach(var v in s.vertices)b.Encapsulate(v);
        var p=Axis(width);var q=Axis(up)*vertical;var r=Vector3.Cross(q,p);
        // Blender X/Y/Z maps to width/depth/up while retaining triangle winding.
        var depthAxis=Enumerable.Range(0,3).Single(i=>i!=up&&i!=width);
        float depth=Mathf.Min(target.size[depthAxis],target.size[width]*.245f);
        var verts=new Vector3[s.vertices.Length];var uv=new Vector2[verts.Length];
        for(int i=0;i<verts.Length;i++)
        {
            var v=s.vertices[i];
            float x=(v.x-b.min.x)/b.size.x, y=(v.y-b.min.y)/b.size.y,z=(v.z-b.min.z)/b.size.z;
            verts[i]=target.center+p*((x-.5f)*target.size[width])+q*((z-.5f)*target.size[up])+r*((y-.5f)*depth);
            uv[i]=new Vector2((tile%4+.025f+x*.95f)/4,(tile/4+.025f+z*.95f)/4);
        }
        var mesh=new Mesh{name=(hoodie?"Blender hoodie":"Blender T-shirt")+" | atlas "+tile};
        mesh.vertices=verts;mesh.triangles=s.triangles;mesh.uv=uv;
        mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();
        return mesh;
    }
    static void Fill(Transform root,string name,Vector3 p,Vector3 target,float intensity)
    {
        var o=new GameObject(name);o.transform.SetParent(root,false);o.transform.position=p;o.transform.LookAt(target);
        var l=o.AddComponent<Light>();l.type=LightType.Spot;l.color=new Color(1,.89f,.75f);
        l.intensity=intensity;l.range=6.1f;l.spotAngle=112;l.innerSpotAngle=74;l.shadows=LightShadows.None;
    }
    static void Preview(Scene scene)
    {
        var source=scene.GetRootGameObjects().Select(o=>o.GetComponent<Camera>()).FirstOrDefault(c=>c);
        if(!source)return;
        var rt=new RenderTexture(1450,850,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
        var oldActive=RenderTexture.active;
        var pos=source.transform.position;
        var rot=source.transform.rotation;
        var fov=source.fieldOfView;
        Texture2D pixels=null;
        try
        {
            source.transform.position=new Vector3(-5.6f,1.84f,4.7f);
            source.transform.LookAt(new Vector3(1.95f,2.1f,-4.0f));
            source.fieldOfView=65;
            var request=new UniversalRenderPipeline.SingleCameraRequest{destination=rt};
            if(!RenderPipeline.SupportsRenderRequest(source,request))return;
            RenderPipeline.SubmitRenderRequest(source,request);
            RenderTexture.active=rt;
            pixels=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
            pixels.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);
            pixels.Apply();
            File.WriteAllBytes(@"C:\Users\Gamebox\.codex\visualizations\2026\11\04\01a1a638-bfb7-7120-9a6a-ae943dd0056f\Shop-After-Mockup-Unity.png",pixels.EncodeToPNG());
        }
        catch(Exception e){Debug.LogWarning("Shop preview could not be captured: "+e.Message);}
        finally
        {
            source.transform.SetPositionAndRotation(pos,rot);
            source.fieldOfView=fov;
            RenderTexture.active=oldActive;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            if(pixels)UnityEngine.Object.DestroyImmediate(pixels);
        }
    }
}


