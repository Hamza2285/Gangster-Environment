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

public static class CityMirageReferenceCorrection
{
    const string ScenePath="Assets/Scenes/Cloth Shop.unity";
    const string Folder="Assets/Texture/City Mirage/";
    const string Request="Temp/CityMirageReferenceCorrection.request";
    const string Status="Temp/CityMirageReferenceCorrection.txt";
    [InitializeOnLoadMethod] static void Schedule()
    {
        if(!Application.isBatchMode&&File.Exists(Request))EditorApplication.update+=WhenSaved;
        if(!Application.isBatchMode&&File.Exists("Temp/CityMirageReview.request"))EditorApplication.delayCall+=Review;
    }
    [MenuItem("Tools/City Mirage/Capture Reference Review")]
    static void Review()
    {
        var scene=SceneManager.GetSceneByPath(ScenePath);
        if(!scene.IsValid()||!scene.isLoaded)return;
        File.Delete("Temp/CityMirageReview.request");
        Preview(scene);
    }
    static void WhenSaved()
    {
        if(EditorApplication.isCompiling||EditorApplication.isPlayingOrWillChangePlaymode)return;
        var scene=SceneManager.GetSceneByPath(ScenePath);
        if(scene.IsValid()&&scene.isDirty)return;
        EditorApplication.update-=WhenSaved;
        Run();
    }
    [MenuItem("Tools/City Mirage/Apply Reference Correction")]
    public static void Run()
    {
        try { File.WriteAllText(Status,Apply());File.Delete(Request); }
        catch(Exception e){File.WriteAllText(Status,e.ToString());Debug.LogException(e);}
    }
    static string Apply()
    {
        var scene=SceneManager.GetSceneByPath(ScenePath);
        if(scene.IsValid()&&scene.isDirty)throw new InvalidOperationException("Save pending Cloth Shop edits first.");
        if(!scene.IsValid()||!scene.isLoaded)scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
        var shader=AssetDatabase.LoadAssetAtPath<Shader>(Folder+"Retail Emissive.shader");
        if(!shader||ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Retail HDR emissive shader import failed.");
        var floorShader=AssetDatabase.LoadAssetAtPath<Shader>(Folder+"Shop Surface Atlas.shader");
        if(!floorShader||ShaderUtil.ShaderHasError(floorShader))throw new InvalidOperationException("Floor reflection shader import failed.");
        var lighting=scene.GetRootGameObjects().Single(o=>o.name=="City Mirage | Lighting");
        var shelves=lighting.GetComponentsInChildren<Light>(true).Where(l=>l.name.Contains("warm cloth wash")).ToArray();
        if(shelves.Length!=4)throw new InvalidOperationException("Expected four shelf washes.");
        var renderers=scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<MeshRenderer>(true)).ToArray();
        var clothes=renderers.Where(r=>r.enabled && (r.name.StartsWith("T-shirt |")||r.name.StartsWith("Hoodie |")) && r.GetComponent<MeshFilter>() && AssetDatabase.GetAssetPath(r.GetComponent<MeshFilter>().sharedMesh)==Folder+"Hanging Garments.asset").ToArray();
        var meshes=clothes.Select(r=>r.GetComponent<MeshFilter>().sharedMesh).Distinct().ToArray();
        if(clothes.Length<50)throw new InvalidOperationException($"Found only {clothes.Length} active Blender garments.");
        var old=SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(scene);
        foreach(var l in shelves)
        {
            var heading=l.transform.forward;
            var target=l.transform.position+heading*2.3f;
            l.transform.position-=heading*.53f;
            l.transform.LookAt(target);
            l.color=new Color(1,.90f,.78f);
            l.intensity=4.65f;
            l.range=5.4f;
            l.spotAngle=119f;
            l.innerSpotAngle=91f;
        }
        var originals=new HashSet<Mesh>();
        foreach(var r in clothes)
        {
            var mesh=r.GetComponent<MeshFilter>().sharedMesh;
            if(!originals.Add(mesh))continue;
            var coords=mesh.vertices;
            var b=mesh.bounds;
            var axes=Enumerable.Range(0,3).OrderByDescending(i=>Mathf.Abs(r.transform.TransformDirection(Unit(i)).y)).ToArray();
            int vertical=axes[0],wide=b.size[axes[1]]>=b.size[axes[2]]?axes[1]:axes[2];
            int depth=axes.Single(i=>i!=vertical&&i!=wide);
            bool up=r.transform.TransformDirection(Unit(vertical)).y>0;
            for(int i=0;i<coords.Length;i++)
            {
                var v=coords[i];
                float x=(v[wide]-b.center[wide])/b.extents[wide];
                float z=(v[vertical]-b.min[vertical])/b.size[vertical];
                if(!up)z=1-z;
                float d=(v[depth]-b.center[depth])/b.extents[depth];
                float fabric=Mathf.Sin(x*11f+z*2.2f)*.020f+Mathf.Sin(x*20f-z*4f)*.009f;
                float falloff=Mathf.SmoothStep(0,1,(.82f-Mathf.Abs(x))/.46f)*Mathf.Sin(Mathf.PI*Mathf.Clamp01(z))*.82f+.18f;
                float cuff=0;
                if(r.name.StartsWith("Hoodie")&&Mathf.Abs(x)>.52f)
                    cuff=.07f*Mathf.Exp(-Mathf.Pow((z-.17f)/.041f,2));
                else if(Mathf.Abs(x)>.68f&&z>.49f)
                    cuff=.053f*Mathf.Exp(-Mathf.Pow((Mathf.Abs(x)-.85f)/.066f,2));
                v[depth]+=Mathf.Sign(d)*b.size[depth]*(fabric*falloff+cuff)*Mathf.Clamp01(Mathf.Abs(d)*2);
                coords[i]=v;
            }
            mesh.vertices=coords;mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
        }
        RenderSettings.ambientLight=new Color(.69f,.62f,.55f);
        var profile=lighting.GetComponent<Volume>().sharedProfile;
        if(profile.TryGet<Bloom>(out var bloom))
        {
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(.67f);
            bloom.scatter.Override(.70f);
            EditorUtility.SetDirty(bloom);
        }
        AssetDatabase.SaveAssets();
        if(!EditorSceneManager.SaveScene(scene))throw new IOException("Could not save Cloth Shop.");
        if(old.IsValid()&&old.isLoaded&&old!=scene)SceneManager.SetActiveScene(old);
        EditorApplication.delayCall+=()=>Preview(scene);
        return $"DONE: {shelves.Length} softened shelf washes, {originals.Count} shared cloth variants refined, HDR shaders imported without errors.";
    }
    static Vector3 Unit(int i){var v=Vector3.zero;v[i]=1;return v;}
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
            source.transform.position=new Vector3(-8.3f,1.78f,5.1f);
            source.transform.LookAt(new Vector3(1.5f,2.04f,-3.7f));
            source.fieldOfView=65;
            var request=new UniversalRenderPipeline.SingleCameraRequest{destination=rt};
            if(!RenderPipeline.SupportsRenderRequest(source,request))return;
            RenderPipeline.SubmitRenderRequest(source,request);
            RenderTexture.active=rt;
            pixels=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
            pixels.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);
            pixels.Apply();
            File.WriteAllBytes(@"C:\Users\Gamebox\.codex\visualizations\2026\11\04\01a1a638-bfb7-7120-9a6a-ae943dd0056f\Shop-Reference-Correction-Unity.png",pixels.EncodeToPNG());
        }
        catch(Exception e){Debug.LogWarning("Reference correction preview could not be captured: "+e.Message);}
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


