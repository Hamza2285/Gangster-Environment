using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class CityMirageEntrance
{
    const string Folder="Assets/Texture/City Mirage/";
    const string ScenePath="Assets/Scenes/Cloth Shop.unity";
    const string Request="Temp/CityMirageEntrance.request";
    const string Status="Temp/CityMirageEntrance.txt";
    static Mesh cube;
    static Mesh cylinder;
    static readonly Dictionary<Material,List<CombineInstance>> parts=new Dictionary<Material,List<CombineInstance>>();
    [InitializeOnLoadMethod] static void Schedule()
    {
        if(!Application.isBatchMode && File.Exists(Request)) EditorApplication.delayCall+=Run;
    }
    [MenuItem("Tools/City Mirage/Apply Entrance and Night Lighting")]
    public static void Run()
    {
        try { Apply(); File.WriteAllText(Status,"DONE"); File.Delete(Request); }
        catch(Exception e) { File.WriteAllText(Status,e.ToString()); Debug.LogException(e); }
    }
    static void Apply()
    {
        var scene=SceneManager.GetSceneByPath(ScenePath);
        if(scene.IsValid() && scene.isDirty) throw new InvalidOperationException("Save pending Cloth Shop changes before running the entrance pass.");
        if(!scene.IsValid() || !scene.isLoaded) scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
        if(scene.GetRootGameObjects().Any(o=>o.name=="Entrance | branded double doors")) return;
        var renderers=scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<MeshRenderer>(true)).ToArray();
        var sign=renderers.Single(r=>r.name.StartsWith("Entrance sign housing"));
        var detectors=renderers.Where(r=>r.name.StartsWith("Entrance | anti-theft antenna")).OrderBy(r=>r.bounds.center.x).ToArray();
        if(detectors.Length!=2) throw new InvalidOperationException($"Expected two detectors, found {detectors.Length}.");
        var importer=(TextureImporter)AssetImporter.GetAtPath(Folder+"Entrance Branding Atlas.png");
        if(importer==null)throw new InvalidOperationException("Entrance branding atlas has not been imported.");
        importer.wrapMode=TextureWrapMode.Clamp;
        importer.mipmapEnabled=true;
        importer.maxTextureSize=2048;
        importer.SaveAndReimport();
        var atlas=AssetDatabase.LoadAssetAtPath<Texture2D>(importer.assetPath);
        var prior=SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(scene);
        var root=new GameObject("Entrance | branded double doors");
        var lighting=scene.GetRootGameObjects().Single(o=>o.name=="City Mirage | Lighting");
        var lightRoot=new GameObject("Entrance | night accents");
        lightRoot.transform.SetParent(lighting.transform,false);
        var dark=Mat("Entrance - charcoal frame",new Color(.105f,.078f,.10f),.35f,.39f);
        var bevel=Mat("Entrance - bronze edge and handles",new Color(.42f,.29f,.21f),.68f,.53f);
        var amber=Mat("Entrance - warm skirting and jamb LEDs",new Color(1,.62f,.25f),0,.5f,3.1f);
        var violet=Mat("Entrance - violet detector LEDs",new Color(.76f,.15f,1),0,.48f,3.65f);
        var face=Mat("Entrance - branded door panels",Color.white,0,.37f);
        face.SetTexture("_BaseMap",atlas);
        var marquee=Mat("Entrance - illuminated CLOTH SHOP marquee",Color.white,0,.43f,1.8f);
        marquee.SetTexture("_BaseMap",atlas);
        marquee.SetTexture("_EmissionMap",atlas);
        EditorUtility.SetDirty(face);
        EditorUtility.SetDirty(marquee);
        var primitive=GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube=primitive.GetComponent<MeshFilter>().sharedMesh;
        UnityEngine.Object.DestroyImmediate(primitive);
        primitive=GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cylinder=primitive.GetComponent<MeshFilter>().sharedMesh;
        UnityEngine.Object.DestroyImmediate(primitive);
        parts.Clear();
        float z=sign.bounds.center.z;
        float doorZ=z+.16f;
        float roomFace=doorZ-.072f;
        Box(dark,new Vector3(0,2.89f,z),new Vector3(4.35f,.54f,.35f));
        foreach(float side in new[]{-1f,1f})
        {
            float x=side*1.81f;
            Box(dark,new Vector3(x,1.31f,z+.047f),new Vector3(.48f,2.62f,.25f));
            Box(bevel,new Vector3(side*2.042f,1.30f,z-.091f),new Vector3(.022f,2.57f,.025f));
            Box(bevel,new Vector3(side*1.58f,1.30f,z-.091f),new Vector3(.021f,2.57f,.025f));
            Box(amber,new Vector3(x+side*.064f,1.27f,z-.096f),new Vector3(.025f,1.49f,.017f));
            Box(amber,new Vector3(x-side*.062f,1.39f,z-.096f),new Vector3(.022f,1.20f,.017f));
            Box(violet,new Vector3(x,.061f,z-.096f),new Vector3(.38f,.020f,.017f));
            for(int i=0;i<2;i++)
            {
                float xx=side*(2.12f+i*3.71f);
                Box(amber,new Vector3(xx+side*1.77f,.153f,6.726f),new Vector3(3.54f,.018f,.035f));
            }
        }
        Box(dark,new Vector3(0,2.54f,doorZ),new Vector3(3.11f,.19f,.27f));
        Box(bevel,new Vector3(0,.031f,doorZ),new Vector3(3.1f,.038f,.155f));
        for(int i=0;i<5;i++)
        {
            float x=(i-2)*.86f;
            Cylinder(dark,new Vector3(x,2.62f,z-.05f),.073f,.045f,Vector3.up);
            Cylinder(amber,new Vector3(x,2.592f,z-.05f),.056f,.014f,Vector3.up);
        }
        // Each leaf has its own transform so it can be animated later.
        string meshPath=Folder+"Entrance Feature Meshes.asset";
        if(AssetDatabase.LoadMainAssetAtPath(meshPath)!=null) AssetDatabase.DeleteAsset(meshPath);
        bool first=true;
        for(int i=0;i<2;i++)
        {
            float side=i==0?-1:1;
            var leaf=new GameObject(i==0?"Left door leaf":"Right door leaf");
            leaf.transform.SetParent(root.transform,false);
            leaf.transform.position=new Vector3(side*.775f,1.22f,doorZ);
            var door=ArtPanel($"Entrance door art {i+1}",1.44f,2.30f,.072f,new Rect(i*.25f+.003f,.006f,.244f,.738f));
            Attach(leaf,door,face,meshPath,ref first);
            foreach(float j in new[]{-1f,1f})
            {
                float x=side*.775f+j*.753f;
                Box(dark,new Vector3(x,1.218f,doorZ),new Vector3(.066f,2.35f,.15f));
            }
            Box(dark,new Vector3(side*.775f,.065f,doorZ),new Vector3(1.54f,.065f,.15f));
            Box(dark,new Vector3(side*.775f,2.37f,doorZ),new Vector3(1.54f,.069f,.15f));
            float handle=side*.153f;
            Cylinder(bevel,new Vector3(handle,1.158f,roomFace-.046f),.019f,.73f,Vector3.up);
            foreach(float y in new[]{.837f,1.479f})
                Cylinder(bevel,new Vector3(handle,y,roomFace-.021f),.020f,.063f,Vector3.forward);
        }
        var banner=new GameObject("Entrance | CLOTH SHOP banner faces");
        banner.transform.SetParent(root.transform,false);
        banner.transform.position=new Vector3(0,2.89f,z);
        Attach(banner,ArtPanel("CLOTH SHOP marquee art",4.19f,.435f,.179f,new Rect(.003f,.755f,.869f,.240f)),marquee,meshPath,ref first);
        foreach(var detector in detectors)
        {
            var b=detector.bounds;
            float x=b.center.x, zz=b.center.z;
            Box(bevel,new Vector3(x,b.max.y-.007f,zz),new Vector3(b.size.x+.016f,.028f,b.size.z+.016f));
            foreach(float s in new[]{-1f,1f})
            {
                float dz=zz+s*(b.extents.z+.012f);
                Box(dark,new Vector3(x,.80f,dz),new Vector3(.058f,.59f,.018f));
                Box(violet,new Vector3(x,.80f,dz+s*.013f),new Vector3(.018f,.48f,.013f));
                Box(violet,new Vector3(x,.069f,zz+s*.204f),new Vector3(.212f,.016f,.013f));
                Box(violet,new Vector3(x+s*.106f,.069f,zz),new Vector3(.013f,.016f,.414f));
            }
        }
        foreach(var pair in parts)
        {
            var mesh=new Mesh{name=pair.Key.name};
            mesh.CombineMeshes(pair.Value.ToArray(),true,true);
            var node=new GameObject(mesh.name);
            node.transform.SetParent(root.transform,false);
            Attach(node,mesh,pair.Key,meshPath,ref first);
        }
        parts.Clear();
        sign.enabled=false;
        foreach(var r in renderers.Where(r=>r.name.StartsWith("Entrance | detector sensor insert"))) r.enabled=false;
        Spot(lightRoot,"Entrance | warm left pool",new Vector3(-1.87f,2.59f,z-.31f),new Vector3(-2.22f,0,z-.30f),new Color(1,.72f,.44f),7,4.2f,72);
        Spot(lightRoot,"Entrance | warm right pool",new Vector3(1.87f,2.59f,z-.31f),new Vector3(2.22f,0,z-.30f),new Color(1,.72f,.44f),7,4.2f,72);
        Spot(lightRoot,"Entrance | violet floor spill",new Vector3(0,.69f,z-.88f),new Vector3(0,0,z-1.25f),new Color(.72f,.17f,1),2.2f,2.4f,111);
        foreach(var l in lighting.GetComponentsInChildren<Light>(true))
        {
            if(l.transform.parent==lightRoot.transform) continue;
            if(l.name=="Front warm wash" || l.name=="Center warm wash") l.intensity*=1.29f;
            else if(l.name.Contains("wall") || l.name.Contains("Mannequin")) l.intensity*=1.14f;
        }
        RenderSettings.ambientLight=new Color(.54f,.48f,.43f);
        var profile=lighting.GetComponent<Volume>().sharedProfile;
        if(profile.TryGet<Bloom>(out var bloom))
        {
            bloom.threshold.Override(1.08f);
            bloom.intensity.Override(.53f);
            bloom.scatter.Override(.63f);
            EditorUtility.SetDirty(bloom);
        }
        AssetDatabase.SaveAssets();
        if(!EditorSceneManager.SaveScene(scene))throw new IOException("Could not save Cloth Shop.");
        if(prior.IsValid() && prior.isLoaded && prior!=scene) SceneManager.SetActiveScene(prior);
    }
    static Mesh ArtPanel(string name,float w,float h,float depth,Rect uv)
    {
        var m=new Mesh{name=name};
        float x=w*.5f,y=h*.5f;
        m.vertices=new[]{new Vector3(-x,-y,-depth),new Vector3(-x,y,-depth),new Vector3(x,y,-depth),new Vector3(x,-y,-depth),new Vector3(x,-y,depth),new Vector3(x,y,depth),new Vector3(-x,y,depth),new Vector3(-x,-y,depth)};
        m.uv=Enumerable.Range(0,2).SelectMany(_=>new[]{new Vector2(uv.xMin,uv.yMin),new Vector2(uv.xMin,uv.yMax),new Vector2(uv.xMax,uv.yMax),new Vector2(uv.xMax,uv.yMin)}).ToArray();
        m.triangles=new[]{0,1,2,0,2,3,4,5,6,4,6,7};
        m.RecalculateNormals();
        return m;
    }
    static void Attach(GameObject o,Mesh m,Material mat,string path,ref bool first)
    {
        if(first){AssetDatabase.CreateAsset(m,path);first=false;}else AssetDatabase.AddObjectToAsset(m,path);
        o.AddComponent<MeshFilter>().sharedMesh=m;
        var r=o.AddComponent<MeshRenderer>();
        r.sharedMaterial=mat;
        r.shadowCastingMode=ShadowCastingMode.Off;
    }
    static Material Mat(string n,Color c,float metal,float smooth,float emit=0)
    {
        string path="Assets/Materials/City Mirage/"+n+".mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(m==null){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
        m.SetColor("_BaseColor",c);m.SetFloat("_Metallic",metal);m.SetFloat("_Smoothness",smooth);
        if(emit>0){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",c*emit);}
        EditorUtility.SetDirty(m);
        return m;
    }
    static void Add(Material m,Mesh mesh,Vector3 pos,Quaternion rot,Vector3 scale)
    {
        if(!parts.TryGetValue(m,out var list))parts[m]=list=new List<CombineInstance>();
        list.Add(new CombineInstance{mesh=mesh,transform=Matrix4x4.TRS(pos,rot,scale)});
    }
    static void Box(Material m,Vector3 p,Vector3 s)=>Add(m,cube,p,Quaternion.identity,s);
    static void Cylinder(Material m,Vector3 p,float r,float h,Vector3 a)=>Add(m,cylinder,p,Quaternion.FromToRotation(Vector3.up,a),new Vector3(r*2,h*.5f,r*2));
    static void Spot(GameObject parent,string name,Vector3 p,Vector3 aim,Color color,float intensity,float range,float angle)
    {
        var o=new GameObject(name);
        o.transform.SetParent(parent.transform,false);
        o.transform.position=p;
        o.transform.LookAt(aim);
        var l=o.AddComponent<Light>();
        l.type=LightType.Spot;l.color=color;l.intensity=intensity;l.range=range;l.spotAngle=angle;l.innerSpotAngle=angle*.57f;l.shadows=LightShadows.None;
    }
}
