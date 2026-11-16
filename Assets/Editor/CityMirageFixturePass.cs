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
public static class CityMirageFixturePass
{
    const string ScenePath="Assets/Scenes/Cloth Shop.unity";
    const string Folder="Assets/Texture/City Mirage/";
    const string Request="Temp/CityMirageFixturePass.request";
    const string Status="Temp/CityMirageFixturePass.txt";
    [InitializeOnLoadMethod]
    static void Schedule()
    {
        if (!Application.isBatchMode && File.Exists(Request)) EditorApplication.delayCall += Run;
    }
    [MenuItem("Tools/City Mirage/Apply Environment Lighting")]
    public static void Run()
    {
        try { Apply(); File.WriteAllText(Status,"DONE"); File.Delete(Request); }
        catch (Exception e) { File.WriteAllText(Status,e.ToString()); Debug.LogException(e); if(Application.isBatchMode)throw; }
    }
    static void Apply()
    {
        var scene=SceneManager.GetSceneByPath(ScenePath);
        bool recovering=File.Exists(Status) && File.ReadAllText(Status).Contains("MissingComponentException");
        if(scene.IsValid() && scene.isDirty && !recovering) throw new InvalidOperationException("Save the current Cloth Shop changes first.");
        if(!scene.IsValid() || !scene.isLoaded) scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
        var previous=SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(scene);
        var lights=scene.GetRootGameObjects().Single(o=>o.name=="City Mirage | Lighting");
        var partial=lights.transform.Find("Reference | fixture detail");
        if(partial && !recovering) return;
        if(partial) UnityEngine.Object.DestroyImmediate(partial.gameObject);
        var renderers=scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<MeshRenderer>(true)).ToArray();
        var pendants=renderers.Where(r=>r.name.StartsWith("Ceiling | suspended rectangular fixture")).ToArray();
        var spots=renderers.Where(r=>r.name.StartsWith("Ceiling | track spotlight housing")).ToArray();
        if(pendants.Length!=15 || spots.Length!=7) throw new InvalidOperationException($"Expected 15 ceiling panels and 7 spot housings, found {pendants.Length}, {spots.Length}.");
        var detail=new GameObject("Reference | fixture detail");
        detail.transform.SetParent(lights.transform,false);
        var dark=MaterialFor("Fixture | charcoal powder coat",new Color(.054f,.049f,.053f),.26f,.37f);
        var metal=MaterialFor("Fixture | satin reflector",new Color(.26f,.24f,.22f),.65f,.55f);
        var lens=MaterialFor("Fixture | warm opal lens",new Color(1f,.84f,.63f),0,.48f,2.7f);
        var violet=MaterialFor("Fixture | violet sign tube",new Color(.60f,.12f,1f),0,.45f,3.1f);
        var cyan=MaterialFor("Fixture | cyan icon tube",new Color(.08f,.62f,1f),0,.48f,3.1f);
        var batches=new Dictionary<Material,List<CombineInstance>>();
        foreach(var p in pendants)
        {
            var b=p.bounds;
            var c=b.center;
            float x=b.extents.x,z=b.extents.z;
            var bottom=b.min.y-.019f;
            foreach(var side in new[]{-1f,1f})
            {
                Box(batches,dark,new Vector3(c.x+side*(x-.010f),bottom,c.z),new Vector3(.024f,.037f,b.size.z));
                Box(batches,dark,new Vector3(c.x,bottom,c.z+side*(z-.012f)),new Vector3(b.size.x,.037f,.030f));
                Box(batches,metal,new Vector3(c.x,b.max.y+.035f,c.z+side*b.size.z*.39f),new Vector3(.10f,.07f,.10f));
            }
            Box(batches,lens,new Vector3(c.x,bottom-.006f,c.z),new Vector3(b.size.x-.047f,.016f,b.size.z-.065f));
        }
        foreach(var p in spots)
        {
            var b=p.bounds;
            var down=new Vector3(0,-.88f,-.47f).normalized;
            var tip=b.center+down*.123f;
            Cylinder(batches,dark,tip,.071f,.043f,down);
            Cylinder(batches,metal,tip+down*.022f,.062f,.014f,down);
            Cylinder(batches,lens,tip+down*.029f,.049f,.008f,down);
            Box(batches,dark,new Vector3(b.center.x,b.max.y+.017f,b.max.z-.042f),new Vector3(.13f,.041f,.095f));
        }
        var sign=renderers.Single(r=>r.name=="Hanging City Mirage sign").bounds;
        foreach(float z in new[]{sign.min.z-.009f,sign.max.z+.009f})
        {
            float x=sign.extents.x-.026f, y=sign.extents.y-.026f;
            var c=new Vector3(sign.center.x,sign.center.y,z);
            Outline(batches,violet,c,new[]{new Vector2(-x,-y),new Vector2(x,-y),new Vector2(x,y),new Vector2(-x,y)},.016f);
        }
        var counter=renderers.Single(r=>r.name.StartsWith("Checkout | welded counter")).bounds;
        var iconCenter=new Vector3(counter.min.x-1.18f,2.33f,-5.82f);
        iconCenter.x=-9.69f;
        var shirt=new[]{new Vector2(-.28f,.21f),new Vector2(-.14f,.30f),new Vector2(-.08f,.24f),new Vector2(.08f,.24f),new Vector2(.14f,.30f),new Vector2(.28f,.21f),new Vector2(.36f,.06f),new Vector2(.24f,-.01f),new Vector2(.17f,.04f),new Vector2(.17f,-.29f),new Vector2(-.17f,-.29f),new Vector2(-.17f,.04f),new Vector2(-.24f,-.01f),new Vector2(-.36f,.06f)};
        for(int i=0;i<shirt.Length;i++)
        {
            var a=shirt[i];var b=shirt[(i+1)%shirt.Length];
            Bar(batches,cyan,iconCenter+new Vector3(0,a.y,a.x),iconCenter+new Vector3(0,b.y,b.x),.018f);
        }
        SaveBatches(batches,detail.transform);

        SetSpot(lights,"Front warm wash",new Vector3(0,3.15f,3.77f),new Vector3(.2f,.10f,2.5f),new Color(1,.85f,.71f),8.5f,7.6f,101);
        SetSpot(lights,"Center warm wash",new Vector3(.2f,3.17f,-.08f),new Vector3(0,.1f,-.35f),new Color(1,.86f,.74f),10f,8.1f,105);
        SetSpot(lights,"Back wall amber",new Vector3(3.10f,3.36f,-4.92f),new Vector3(3.08f,1.62f,-6.40f),new Color(1,.76f,.50f),8,5.0f,94);
        SetSpot(lights,"Back wall | second display pool",new Vector3(-.52f,3.36f,-4.97f),new Vector3(-.26f,1.55f,-6.40f),new Color(1,.77f,.52f),7.6f,5.1f,90);
        SetSpot(lights,"Left display | soft amber fill",new Vector3(8.13f,3.43f,-1.08f),new Vector3(9.31f,1.52f,-1.1f),new Color(1,.73f,.48f),7.5f,6.5f,106);
        SetSpot(lights,"Mannequin feature",new Vector3(-6.7f,3.48f,-4.41f),new Vector3(-6.75f,1.2f,-5.66f),new Color(1,.84f,.64f),8.2f,5.3f,71);
        SetSpot(lights,"Checkout cyan accent",new Vector3(counter.max.x+.29f,.75f,counter.center.z),new Vector3(counter.max.x+1.10f,0,counter.center.z),new Color(.12f,.50f,1),4.3f,3.7f,105);
        SetSpot(lights,"Checkout | warm task light",new Vector3(counter.center.x+.1f,3.35f,counter.center.z-.15f),new Vector3(counter.center.x,1.05f,counter.center.z),new Color(1,.88f,.73f),7,4.8f,88);
        RenderSettings.ambientMode=AmbientMode.Flat;
        RenderSettings.ambientLight=new Color(.47f,.41f,.37f);
        var profile=lights.GetComponent<Volume>().sharedProfile;
        if(profile.TryGet<Bloom>(out var bloom))
        {
            bloom.threshold.Override(1.08f);
            bloom.intensity.Override(.46f);
            bloom.scatter.Override(.63f);
            EditorUtility.SetDirty(bloom);
        }
        AssetDatabase.SaveAssets();
        if(!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save Cloth Shop.");
        if(previous.IsValid() && previous.isLoaded && previous!=scene) SceneManager.SetActiveScene(previous);
        Debug.Log("Fixture and environment lighting detail saved.");
    }

    static Material MaterialFor(string name,Color color,float metal,float smooth,float emission=0)
    {
        string path="Assets/Materials/City Mirage/"+name.Replace(" | "," - ")+".mat";
        var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(mat==null)
        {
            mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat,path);
        }
        mat.SetColor("_BaseColor",color);
        mat.SetFloat("_Metallic",metal);
        mat.SetFloat("_Smoothness",smooth);
        if(emission>0)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor",color*emission);
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }
    static Mesh cube;
    static Mesh cylinder;
    static Mesh Primitive(PrimitiveType type)
    {
        var go=GameObject.CreatePrimitive(type);
        var mesh=go.GetComponent<MeshFilter>().sharedMesh;
        UnityEngine.Object.DestroyImmediate(go);
        return mesh;
    }
    static void Add(Dictionary<Material,List<CombineInstance>> batches,Material material,Mesh mesh,Vector3 p,Quaternion q,Vector3 scale)
    {
        if(!batches.TryGetValue(material,out var batch)) batches[material]=batch=new List<CombineInstance>();
        batch.Add(new CombineInstance{mesh=mesh,transform=Matrix4x4.TRS(p,q,scale)});
    }
    static void Box(Dictionary<Material,List<CombineInstance>> b,Material m,Vector3 p,Vector3 size)
    {
        if(cube==null) cube=Primitive(PrimitiveType.Cube);
        Add(b,m,cube,p,Quaternion.identity,size);
    }
    static void Cylinder(Dictionary<Material,List<CombineInstance>> b,Material m,Vector3 p,float radius,float height,Vector3 axis)
    {
        if(cylinder==null) cylinder=Primitive(PrimitiveType.Cylinder);
        Add(b,m,cylinder,p,Quaternion.FromToRotation(Vector3.up,axis),new Vector3(radius*2,height*.5f,radius*2));
    }
    static void Bar(Dictionary<Material,List<CombineInstance>> b,Material m,Vector3 from,Vector3 to,float width)
    {
        if(cube==null) cube=Primitive(PrimitiveType.Cube);
        var delta=to-from;
        Add(b,m,cube,(from+to)*.5f,Quaternion.FromToRotation(Vector3.up,delta.normalized),new Vector3(width,delta.magnitude+width*.6f,width));
    }
    static void Outline(Dictionary<Material,List<CombineInstance>> b,Material m,Vector3 center,Vector2[] path,float width)
    {
        for(int i=0;i<path.Length;i++) Bar(b,m,center+(Vector3)path[i],center+(Vector3)path[(i+1)%path.Length],width);
    }
    static void SaveBatches(Dictionary<Material,List<CombineInstance>> b,Transform parent)
    {
        string path=Folder+"Environment Fixture Detail.asset";
        if(AssetDatabase.LoadMainAssetAtPath(path)!=null) AssetDatabase.DeleteAsset(path);
        bool first=true;
        foreach(var pair in b)
        {
            var mesh=new Mesh{name=pair.Key.name};
            mesh.CombineMeshes(pair.Value.ToArray(),true,true);
            if(first){AssetDatabase.CreateAsset(mesh,path);first=false;}
            else AssetDatabase.AddObjectToAsset(mesh,path);
            var go=new GameObject(mesh.name);
            go.transform.SetParent(parent,false);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var r=go.AddComponent<MeshRenderer>();
            r.sharedMaterial=pair.Key;
            r.shadowCastingMode=ShadowCastingMode.Off;
        }
    }
    static void SetSpot(GameObject root,string name,Vector3 p,Vector3 aim,Color color,float intensity,float range,float angle)
    {
        var node=root.transform.Find(name);
        if(!node)
        {
            node=new GameObject(name).transform;
            node.SetParent(root.transform,false);
        }
        node.position=p;
        node.LookAt(aim);
        if(!node.TryGetComponent<Light>(out var light)) light=node.gameObject.AddComponent<Light>();
        light.type=LightType.Spot;
        light.color=color;
        light.intensity=intensity;
        light.range=range;
        light.spotAngle=angle;
        light.innerSpotAngle=angle*.57f;
        light.shadows=LightShadows.None;
    }
}

