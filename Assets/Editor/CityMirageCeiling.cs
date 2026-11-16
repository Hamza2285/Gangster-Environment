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

public static class CityMirageCeiling
{
    const string ScenePath="Assets/Scenes/Cloth Shop.unity";
    const string Folder="Assets/Texture/City Mirage/";
    const string Request="Temp/CityMirageCeiling.request";
    const string Status="Temp/CityMirageCeiling.txt";
    const string MeshPath=Folder+"Ceiling and Sign Detail.asset";
    static readonly Dictionary<Material,List<CombineInstance>> batches=new Dictionary<Material,List<CombineInstance>>();
    static Mesh cube;
    static bool first;
    [InitializeOnLoadMethod] static void Schedule()
    {
        if(!Application.isBatchMode&&File.Exists(Request))EditorApplication.delayCall+=Run;
    }
    [MenuItem("Tools/City Mirage/Apply Ceiling and Sign Detail")]
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
        if(scene.GetRootGameObjects().Any(o=>o.name=="Reference | ceiling and branded signs"))return;
        var rs=scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<MeshRenderer>(true)).ToArray();
        var ceiling=rs.Single(r=>r.name=="Ceiling deck");
        var sign=rs.Single(r=>r.name=="Hanging City Mirage sign");
        var counter=rs.Single(r=>r.name.StartsWith("Checkout | welded counter"));
        var lights=scene.GetRootGameObjects().Single(o=>o.name=="City Mirage | Lighting");
        var branding=Texture("Entrance Branding Atlas.png",false,false,2048);
        var concrete=Texture("Ceiling Concrete Base Color.png",false,true,1024);
        var normal=Texture("Ceiling Concrete Normal.png",true,true,1024);
        var shoeAtlas=Texture("Display Shoes Atlas.png",false,false,512);
        var original=SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(scene);
        var root=new GameObject("Reference | ceiling and branded signs");
        var lightRoot=new GameObject("Ceiling | ambient bounce accents");
        lightRoot.transform.SetParent(lights.transform,false);
        if(AssetDatabase.LoadMainAssetAtPath(MeshPath)!=null)AssetDatabase.DeleteAsset(MeshPath);
        first=true;
        batches.Clear();
        var temp=GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube=temp.GetComponent<MeshFilter>().sharedMesh;
        UnityEngine.Object.DestroyImmediate(temp);

        var stone=Mat("Ceiling - fine mineral plaster",Color.white,0,.36f);
        stone.SetTexture("_BaseMap",concrete);
        stone.SetTextureScale("_BaseMap",new Vector2(24,20));
        stone.SetTextureOffset("_BaseMap",new Vector2(0,-5));
        stone.SetTexture("_BumpMap",normal);
        stone.SetFloat("_BumpScale",.72f);
        stone.EnableKeyword("_NORMALMAP");
        EditorUtility.SetDirty(stone);
        ceiling.sharedMaterial=stone;
        var beam=Mat("Ceiling - satin dark steel",new Color(.14f,.115f,.13f),.46f,.57f);
        var pipe=Mat("Ceiling - brushed conduit steel",new Color(.29f,.24f,.22f),.62f,.57f);
        var diffuser=Mat("Ceiling - bright warm diffuser",new Color(1f,.91f,.77f),0,.41f,5.2f);
        var rim=Mat("Ceiling - warm lens rim",new Color(1f,.62f,.31f),0,.47f,2.4f);
        var violet=Mat("Banner - violet tube",new Color(.68f,.15f,1f),0,.44f,4.25f);
        var cyan=Mat("Counter - cyan edge tube",new Color(.06f,.65f,1),0,.43f,3.9f);
        var casing=Mat("Banner - charcoal housing",new Color(.066f,.045f,.083f),.26f,.46f);
        var artwork=Mat("Banner - illuminated apparel artwork",Color.white,0,.36f,1.65f);
        artwork.SetTexture("_BaseMap",branding);
        artwork.SetTexture("_EmissionMap",branding);
        EditorUtility.SetDirty(artwork);

        foreach(var r in rs)
        {
            if(r.name.StartsWith("Ceiling | cross beam")||r.name.StartsWith("Ceiling | longitudinal rail"))r.sharedMaterial=beam;
            if(r.name.Contains("conduit"))r.sharedMaterial=pipe;
            if(r.name.StartsWith("Ceiling | fixture diffuser"))r.sharedMaterial=diffuser;
            if(r.name=="Fixture | warm opal lens")r.sharedMaterial=diffuser;
            if(r.name.StartsWith("Ceiling | suspended rectangular fixture"))
            {
                var b=r.bounds;
                bool longZ=b.size.z>b.size.x;
                var axis=longZ?Vector3.forward:Vector3.right;
                var cross=longZ?Vector3.right:Vector3.forward;
                float length=longZ?b.size.z:b.size.x,width=longZ?b.size.x:b.size.z;
                var center=new Vector3(b.center.x,b.min.y-.031f,b.center.z);
                foreach(float side in new[]{-1f,1f})
                {
                    var p=center+cross*side*(width*.5f-.038f);
                    Bar(rim,p-axis*(length*.5f-.035f),p+axis*(length*.5f-.035f),.009f);
                }
            }
            if(r.name=="Fixture | violet sign tube")r.enabled=false;
        }
        sign.enabled=false;
        var sb=sign.bounds;
        var centerSign=sb.center;
        Banner(root.transform,"Ceiling | CLOTH SHOP lightbox",centerSign,Quaternion.identity,sb.size.x+.12f,sb.size.y+.09f,.15f,casing,violet,artwork);
        // Match the wall signage in the reference without changing the 70% offer art.
        var screen=rs.Single(r=>r.name.StartsWith("Side wall screen housing"));
        var wb=screen.bounds;
        screen.enabled=false;
        Banner(root.transform,"Wall | CLOTH SHOP lightbox",wb.center,Quaternion.Euler(0,90,0),wb.size.z,wb.size.y,.15f,casing,violet,artwork);

        var cb=counter.bounds;
        var end=cb.min.z-.017f;
        for(int i=0;i<22;i++)
        {
            var x=Mathf.Lerp(cb.min.x+.065f,cb.max.x-.065f,i/21f);
            Box(casing,new Vector3(x,.57f,end),new Vector3(.027f,.74f,.024f));
        }
        foreach(float y in new[]{.183f,.952f})
            Bar(cyan,new Vector3(cb.min.x+.014f,y,end-.015f),new Vector3(cb.max.x-.014f,y,end-.015f),.019f);
        var icon=new[]{new Vector2(-.19f,.14f),new Vector2(-.08f,.19f),new Vector2(-.034f,.14f),new Vector2(.034f,.14f),new Vector2(.08f,.19f),new Vector2(.19f,.14f),new Vector2(.24f,.039f),new Vector2(.16f,-.008f),new Vector2(.12f,.027f),new Vector2(.12f,-.18f),new Vector2(-.12f,-.18f),new Vector2(-.12f,.027f),new Vector2(-.16f,-.008f),new Vector2(-.24f,.039f)};
        for(int i=0;i<icon.Length;i++)
            Bar(cyan,new Vector3(cb.center.x+icon[i].x,.56f+icon[i].y,end-.029f),new Vector3(cb.center.x+icon[(i+1)%icon.Length].x,.56f+icon[(i+1)%icon.Length].y,end-.029f),.013f);

        var shoes=Mat("Display shoes - stitched atlas",Color.white,0,.41f);
        shoes.SetTexture("_BaseMap",shoeAtlas);
        EditorUtility.SetDirty(shoes);
        var variants=new Dictionary<(Mesh,bool),Mesh>();
        foreach(var r in rs.Where(r=>r.name=="Sale table | display sneaker"))
        {
            var f=r.GetComponent<MeshFilter>();
            var source=f.sharedMesh;
            bool black=r.sharedMaterial.name.Contains("charcoal");
            if(!variants.TryGetValue((source,black),out var mesh))
            {
                mesh=UnityEngine.Object.Instantiate(source);
                mesh.name=black?"Display shoe | dark stitched atlas":"Display shoe | cream stitched atlas";
                var bounds=source.bounds;
                var v=source.vertices;
                var uv=new Vector2[v.Length];
                for(int i=0;i<v.Length;i++)
                {
                    var p=f.transform.TransformVector(v[i]-bounds.center);
                    var q=f.transform.TransformVector(bounds.extents);
                    float u=Mathf.Clamp01(.5f+p.z/(Mathf.Abs(q.z)*2+.00001f));
                    float h=Mathf.Clamp01(.5f+p.y/(Mathf.Abs(q.y)*2+.00001f));
                    uv[i]=new Vector2((black ? .5f : 0)+.015f+u*.47f,.025f+h*.95f);
                }
                mesh.uv=uv;
                SaveMesh(mesh);
                variants.Add((source,black),mesh);
            }
            f.sharedMesh=mesh;
            r.sharedMaterial=shoes;
        }
        foreach(var pair in batches)
        {
            var mesh=new Mesh{name=pair.Key.name+" | combined detail"};
            mesh.CombineMeshes(pair.Value.ToArray(),true,true);
            ObjectFor(root.transform,mesh,pair.Key,Vector3.zero,Quaternion.identity);
        }
        batches.Clear();
        Bounce(lightRoot.transform,"Ceiling | warm beam bounce",new Vector3(4.9f,2.54f,-3.6f),new Vector3(4.9f,3.85f,-3.6f),new Color(1,.67f,.42f),3.0f,3.0f);
        Bounce(lightRoot.transform,"Ceiling | violet banner bounce",centerSign+Vector3.up*.14f,new Vector3(centerSign.x,3.9f,centerSign.z),new Color(.63f,.22f,1),2.3f,2.8f);
        if(lights.GetComponent<Volume>().sharedProfile.TryGet<Bloom>(out var bloom))
        {
            bloom.threshold.Override(1.07f);
            bloom.intensity.Override(.56f);
            bloom.scatter.Override(.65f);
            EditorUtility.SetDirty(bloom);
        }
        AssetDatabase.SaveAssets();
        if(!EditorSceneManager.SaveScene(scene))throw new IOException("Unity could not save Cloth Shop.");
        if(original.IsValid()&&original.isLoaded&&original!=scene)SceneManager.SetActiveScene(original);
        int meshCount=root.GetComponentsInChildren<MeshFilter>().Length;
        long triangleCount=root.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.GetIndexCount(0)/3L);
        File.WriteAllText("Temp/CityMirageCeilingMetrics.txt",$"New detail renderers: {meshCount}; triangles: {triangleCount}; added local lights: 2.");
        Debug.Log("Ceiling, banners, counter trim and shoe atlas saved.");
        EditorApplication.delayCall += () => Preview(scene);
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
            File.WriteAllBytes(@"C:\Users\Gamebox\.codex\visualizations\2026\11\04\01a1a638-bfb7-7120-9a6a-ae943dd0056f\Shop-Ceiling-Unity.png",pixels.EncodeToPNG());
        }
        catch(Exception e){Debug.LogWarning("Ceiling preview could not be captured: "+e.Message);}
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
    static Texture2D Texture(string name,bool normal,bool repeat,int max)
    {
        var path=Folder+name;
        var i=(TextureImporter)AssetImporter.GetAtPath(path);
        if(i==null)throw new InvalidOperationException("Texture missing: "+name);
        i.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
        i.mipmapEnabled=true;i.wrapMode=repeat?TextureWrapMode.Repeat:TextureWrapMode.Clamp;i.maxTextureSize=max;
        i.textureCompression=TextureImporterCompression.Compressed;i.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
    static Material Mat(string name,Color c,float metal,float gloss,float emit=0)
    {
        var path="Assets/Materials/City Mirage/"+name+".mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(m==null){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
        m.SetColor("_BaseColor",c);m.SetFloat("_Metallic",metal);m.SetFloat("_Smoothness",gloss);
        if(emit>0){m.SetColor("_EmissionColor",c*emit);m.EnableKeyword("_EMISSION");}
        EditorUtility.SetDirty(m);return m;
    }
    static void Banner(Transform root,string name,Vector3 c,Quaternion q,float w,float h,float d,Material body,Material trim,Material art)
    {
        float x=w*.5f,y=h*.5f,cut=Mathf.Min(.20f,y*.55f);
        var profile=new[]{new Vector2(-x+cut,-y),new Vector2(x-cut,-y),new Vector2(x,-y+cut),new Vector2(x,y-cut),new Vector2(x-cut,y),new Vector2(-x+cut,y),new Vector2(-x,y-cut),new Vector2(-x,-y+cut)};
        var shell=new Mesh{name=name+" | chamfered enclosure"};
        var verts=profile.Select(p=>new Vector3(p.x,p.y,-d*.5f)).Concat(profile.Select(p=>new Vector3(p.x,p.y,d*.5f))).ToArray();
        var tri=new List<int>();
        for(int i=0;i<8;i++){int j=(i+1)%8;tri.AddRange(new[]{i,j,j+8,i,j+8,i+8});}
        shell.vertices=verts;shell.SetTriangles(tri,0);shell.RecalculateNormals();
        ObjectFor(root,shell,body,c,q);
        foreach(float side in new[]{-1f,1f})
        {
            float z=side*(d*.5f+.007f);
            var face=new Mesh{name=name+" | readable face"};
            face.vertices=profile.Select(p=>new Vector3(p.x,p.y,z)).ToArray();
            face.uv=profile.Select(p=>new Vector2(.007f+(.5f-side*p.x/w)*.86f,.758f+(.5f+p.y/h)*.232f)).ToArray();
            var indices=new List<int>();
            for(int i=1;i<7;i++)indices.AddRange(side>0?new[]{0,i,i+1}:new[]{0,i+1,i});
            face.SetTriangles(indices,0);face.RecalculateNormals();
            ObjectFor(root,face,art,c,q);
            for(int i=0;i<8;i++)
            {
                var a=profile[i];var b=profile[(i+1)%8];
                Bar(trim,c+q*new Vector3(a.x,a.y,z),c+q*new Vector3(b.x,b.y,z),.017f);
            }
        }
    }
    static void SaveMesh(Mesh m)
    {
        if(first){AssetDatabase.CreateAsset(m,MeshPath);first=false;}else AssetDatabase.AddObjectToAsset(m,MeshPath);
    }
    static void ObjectFor(Transform root,Mesh mesh,Material m,Vector3 p,Quaternion q)
    {
        SaveMesh(mesh);
        var o=new GameObject(mesh.name);o.transform.SetParent(root,false);o.transform.SetPositionAndRotation(p,q);
        o.AddComponent<MeshFilter>().sharedMesh=mesh;
        var r=o.AddComponent<MeshRenderer>();r.sharedMaterial=m;r.shadowCastingMode=ShadowCastingMode.Off;
    }
    static void Add(Material m,Vector3 p,Quaternion r,Vector3 scale)
    {
        if(!batches.TryGetValue(m,out var list))batches[m]=list=new List<CombineInstance>();
        list.Add(new CombineInstance{mesh=cube,transform=Matrix4x4.TRS(p,r,scale)});
    }
    static void Box(Material m,Vector3 p,Vector3 size)=>Add(m,p,Quaternion.identity,size);
    static void Bar(Material m,Vector3 a,Vector3 b,float w)=>Add(m,(a+b)*.5f,Quaternion.FromToRotation(Vector3.up,(b-a).normalized),new Vector3(w,(b-a).magnitude+w*.45f,w));
    static void Bounce(Transform parent,string name,Vector3 p,Vector3 target,Color color,float intensity,float range)
    {
        var o=new GameObject(name);o.transform.SetParent(parent,false);o.transform.position=p;o.transform.LookAt(target);
        var l=o.AddComponent<Light>();l.type=LightType.Spot;l.color=color;l.intensity=intensity;l.range=range;l.spotAngle=116;l.innerSpotAngle=62;l.shadows=LightShadows.None;
    }
}


