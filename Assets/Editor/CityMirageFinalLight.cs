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
public static class CityMirageFinalLight
{
    const string ScenePath="Assets/Scenes/Cloth Shop.unity";
    const string Folder="Assets/Texture/City Mirage/";
    const string Request="Temp/CityMirageFinalLight.request";
    const string Status="Temp/CityMirageFinalLight.txt";
    const string MeshPath=Folder+"Reference Detail Meshes.asset";
    static bool first;
    [InitializeOnLoadMethod] static void Schedule()
    {
        if(!Application.isBatchMode && File.Exists(Request)) EditorApplication.delayCall+=Run;
    }
    [MenuItem("Tools/City Mirage/Apply Reference Lighting and Detail")]
    public static void Run()
    {
        try { Apply(); File.WriteAllText(Status,"DONE"); File.Delete(Request); }
        catch(Exception e){File.WriteAllText(Status,e.ToString());Debug.LogException(e);}
    }
    static void Apply()
    {
        var scene=SceneManager.GetSceneByPath(ScenePath);
        if(scene.IsValid() && scene.isDirty)throw new InvalidOperationException("Save pending Cloth Shop changes first.");
        if(!scene.IsValid()||!scene.isLoaded)scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
        if(scene.GetRootGameObjects().Any(o=>o.name=="Reference | display and surface polish"))return;
        var rs=scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<MeshRenderer>(true)).ToArray();
        var tables=rs.Where(r=>r.name.StartsWith("Sale Table")||r.name.StartsWith("Display Table")).ToArray();
        var panels=rs.Where(r=>r.name.StartsWith("Sale panel housing")).ToArray();
        var bays=rs.Where(r=>System.Text.RegularExpressions.Regex.IsMatch(r.name,@"^Wall Bay 0[1-4]$")).ToArray();
        var stand=rs.Single(r=>r.name.StartsWith("New arrivals stand"));
        var sneaker=rs.First(r=>r.name.StartsWith("Mannequin 01 | Sneaker"));
        var lights=scene.GetRootGameObjects().Single(o=>o.name=="City Mirage | Lighting");
        if(tables.Length==0||panels.Length==0||bays.Length!=4)throw new InvalidOperationException($"Scene has {tables.Length} tables, {panels.Length} sale signs and {bays.Length} wall displays.");
        var previous=SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(scene);
        var root=new GameObject("Reference | display and surface polish");
        var lampGroup=new GameObject("Reference | bright retail light");
        lampGroup.transform.SetParent(lights.transform,false);
        first=true;
        if(AssetDatabase.LoadMainAssetAtPath(MeshPath)!=null)AssetDatabase.DeleteAsset(MeshPath);
        var dark=MaterialFor("Reference - satin charcoal",new Color(.13f,.105f,.12f),.27f,.58f);
        var frame=MaterialFor("Reference - matte black stand",new Color(.035f,.030f,.041f),.22f,.43f);
        var side=MaterialFor("Reference - honey wood insert",new Color(.61f,.32f,.12f),0,.47f);
        var ceramic=MaterialFor("Reference - satin ceramic",new Color(.79f,.73f,.65f),0,.56f);
        var white=MaterialFor("Reference - sneaker ivory",new Color(.89f,.85f,.79f),0,.37f);
        var black=MaterialFor("Reference - sneaker charcoal",new Color(.13f,.10f,.13f),0,.44f);
        var gold=MaterialFor("Reference - gold sign trim",new Color(1f,.64f,.075f),.28f,.55f);
        var art=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/City Mirage/"+"Branded display panels.mat");
        var saleMat=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/City Mirage/"+"Reference - retail sign faces.mat");
        if(saleMat==null)
        {
            saleMat=new Material(art);
            AssetDatabase.CreateAsset(saleMat,"Assets/Materials/City Mirage/"+"Reference - retail sign faces.mat");
        }
        saleMat.SetColor("_EmissionColor",Color.white*1.46f);
        EditorUtility.SetDirty(saleMat);
        var amber=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/City Mirage/"+"Amber display trim.mat");
        foreach(var r in rs.Where(r=>r.name=="Architecture baseboard"))
        {
            var b=r.bounds;
            bool runsX=b.size.x>b.size.z;
            float inward=runsX?-Mathf.Sign(b.center.z):-Mathf.Sign(b.center.x);
            var at=b.center+Vector3.up*(b.extents.y+.011f)+(runsX?Vector3.forward:Vector3.right)*inward*.028f;
            var size=runsX?new Vector3(b.size.x-.15f,.016f,.025f):new Vector3(.025f,.016f,b.size.z-.15f);
            Box(root.transform,"Skirting | warm LED channel",at,size,amber);
        }
        foreach(var r in rs.Where(r=>r.name.StartsWith("Hanger")||r.name.Contains("bent hanger")||r.name.Contains("hanger hook")))
            r.sharedMaterial=gold;
        foreach(var r in rs.Where(r=>r.name.StartsWith("Rack | welded")))
        {
            r.sharedMaterial=frame;
            var b=r.bounds;
            bool runsX=b.size.x>b.size.z;
            foreach(float edge in new[]{-1f,1f})
            {
                var at=b.center+(runsX?Vector3.forward*b.extents.z:Vector3.right*b.extents.x)*edge;
                at.y=b.min.y+.049f;
                var size=runsX?new Vector3(b.size.x-.08f,.014f,.022f):new Vector3(.022f,.014f,b.size.z-.08f);
                Box(root.transform,"Rack | warm base trim",at,size,amber);
            }
        }
        foreach(var r in tables)
        {
            r.sharedMaterial=dark;
            var b=r.bounds;
            foreach(float e in new[]{-1f,1f})
            {
                Box(root.transform,"Table | warm plinth strip",new Vector3(b.center.x,b.min.y+.073f,b.center.z+e*(b.extents.z-.048f)),new Vector3(b.size.x-.17f,.017f,.022f),amber);
                Box(root.transform,"Table | warm plinth return",new Vector3(b.center.x+e*(b.extents.x-.054f),b.min.y+.073f,b.center.z),new Vector3(.022f,.017f,b.size.z-.13f),amber);
            }
            foreach(float direction in new[]{-1f,1f})
            {
                var p=new Vector3(b.center.x+direction*(b.extents.x-.143f),b.min.y+.455f,b.center.z);
                Box(root.transform,"Table | inset side panel",p,new Vector3(.022f,.58f,.89f),side);
            }
            // The rear display table stays clear of shoes.
            if(!r.name.Contains("03"))
            {
                for(int j=0;j<4;j++)
                {
                    var o=new GameObject("Sale table | display sneaker");
                    o.transform.SetParent(root.transform,false);
                    o.transform.position=new Vector3(b.center.x+(j-1.5f)*.19f,b.max.y+.003f,b.max.z-.16f);
                    o.transform.rotation=sneaker.transform.rotation;
                    o.transform.localScale=sneaker.transform.lossyScale*1.19f;
                    o.AddComponent<MeshFilter>().sharedMesh=sneaker.GetComponent<MeshFilter>().sharedMesh;
                    o.AddComponent<MeshRenderer>().sharedMaterial=j<2?white:black;
                }
            }
        }
        foreach(var r in panels)
        {
            var b=r.bounds;
            r.sharedMaterial=frame;
            var mesh=Panel("Sale | correctly oriented 50 percent artwork",b.size.x*.972f,b.size.y*.91f,b.extents.z+.004f,9);
            ObjectFor(root.transform,mesh,saleMat,b.center);
        }
        var sb=stand.bounds;
        stand.enabled=false;
        var board=new Vector3(sb.center.x,sb.min.y+1.16f,sb.center.z);
        Box(root.transform,"Arrival A-frame | board casing",board,new Vector3(.73f,.83f,.061f),frame);
        Box(root.transform,"Arrival A-frame | gold outer trim",board,new Vector3(.705f,.802f,.067f),gold);
        ObjectFor(root.transform,Panel("Arrival | readable NEW ARRIVAL artwork",.663f,.751f,.037f,10),saleMat,board);
        foreach(float sideX in new[]{-1f,1f})
        {
            float x=sb.center.x+sideX*.31f;
            Bar(root.transform,"Arrival A-frame | tubular upright",new Vector3(x,sb.min.y,sb.center.z+.30f),new Vector3(x,sb.min.y+1.56f,sb.center.z),.042f,frame);
            Bar(root.transform,"Arrival A-frame | tubular rear leg",new Vector3(x,sb.min.y,sb.center.z-.30f),new Vector3(x,sb.min.y+1.52f,sb.center.z),.042f,frame);
            Bar(root.transform,"Arrival A-frame | side runner",new Vector3(x,sb.min.y+.024f,sb.center.z-.32f),new Vector3(x,sb.min.y+.024f,sb.center.z+.32f),.043f,frame);
        }
        foreach(float z in new[]{sb.center.z-.30f,sb.center.z+.30f})
            Box(root.transform,"Arrival A-frame | lower crossbar",new Vector3(sb.center.x,sb.min.y+.024f,z),new Vector3(.66f,.044f,.044f),frame);

        var pot=SmoothPot();
        SaveMesh(pot);
        foreach(var r in rs.Where(r=>r.name.StartsWith("Planter")&&!r.name.Contains("foliage")))
        {
            var b=r.bounds;
            r.enabled=false;
            var o=new GameObject("Planter | smooth ceramic");
            o.transform.SetParent(root.transform,false);
            o.transform.position=new Vector3(b.center.x,b.min.y,b.center.z);
            o.transform.localScale=b.size;
            o.AddComponent<MeshFilter>().sharedMesh=pot;
            o.AddComponent<MeshRenderer>().sharedMaterial=ceramic;
        }
        var mannequinMeshes=rs.Where(r=>r.name.StartsWith("Mannequin 0")).Select(r=>r.GetComponent<MeshFilter>()).ToArray();
        var smoothMeshes=new Dictionary<Mesh,Mesh>();
        foreach(var f in mannequinMeshes)
        {
            var original=f.sharedMesh;
            if(!smoothMeshes.TryGetValue(original,out var refined))
            {
                refined=UnityEngine.Object.Instantiate(original);
                refined.name=original.name+" | smooth surface normals";
                var vertices=refined.vertices;
                var normals=refined.normals;
                if(normals.Length==vertices.Length)
                {
                    var groups=new Dictionary<Vector3Int,List<int>>();
                    for(int i=0;i<vertices.Length;i++)
                    {
                        var key=Vector3Int.RoundToInt(vertices[i]*10000);
                        if(!groups.TryGetValue(key,out var list))groups[key]=list=new List<int>();
                        list.Add(i);
                    }
                    var result=(Vector3[])normals.Clone();
                    foreach(var list in groups.Values)
                    foreach(int i in list)
                    {
                        var n=Vector3.zero;
                        foreach(int j in list)
                            if(Vector3.Dot(normals[i],normals[j])>.60f)n+=normals[j];
                        if(n.sqrMagnitude>.001f)result[i]=n.normalized;
                    }
                    refined.normals=result;
                }
                SaveMesh(refined);
                smoothMeshes.Add(original,refined);
            }
            f.sharedMesh=refined;
        }
        var existing=lights.GetComponentsInChildren<Light>(true);
        foreach(var l in existing)
        {
            if(l.name=="Front warm wash"||l.name=="Center warm wash"){l.intensity*=1.23f;l.color=new Color(1,.91f,.79f);}
            if(l.name=="Mannequin feature"){l.intensity*=1.25f;l.spotAngle=82;}
            if(l.name=="Checkout | warm task light"){l.intensity*=1.26f;l.spotAngle=100;}
            if(l.name=="Back wall amber"||l.name=="Back wall | second display pool"||l.name=="Left display | soft amber fill")
                l.enabled=false;
        }
        foreach(var bay in bays)
        {
            var b=bay.bounds;
            var forward=b.size.x>b.size.z?Vector3.forward:Vector3.left;
            var aim=new Vector3(b.center.x,1.42f,b.center.z)+forward*.13f;
            Spot(lampGroup.transform,bay.name+" | warm cloth wash",new Vector3(b.center.x,2.96f,b.center.z)+forward*1.09f,aim,new Color(1,.84f,.66f),8.3f,4.0f,101);
        }
        var counter=rs.Single(r=>r.name.StartsWith("Checkout | welded counter"));
        var cb=counter.bounds;
        var magenta=scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<Light>(true)).FirstOrDefault(l=>l.name=="Checkout | magenta display spill");
        if(magenta)
        {
            magenta.transform.position=new Vector3(cb.max.x+.28f,.74f,cb.center.z);
            magenta.transform.LookAt(new Vector3(cb.max.x+1.45f,0,cb.center.z));
            magenta.intensity=2.2f;
            magenta.range=4.0f;
            magenta.spotAngle=117;
            magenta.innerSpotAngle=62;
        }
        foreach(var table in tables.OrderBy(r=>r.bounds.center.z).Take(2))
        {
            var b=table.bounds;
            var p=new Vector3(b.center.x,.23f,b.max.z+.10f);
            Spot(lampGroup.transform,table.name+" | warm floor spill",p,p+new Vector3(0,-.23f,.25f),new Color(1,.59f,.26f),1.2f,1.55f,116);
        }
        RenderSettings.ambientMode=AmbientMode.Flat;
        RenderSettings.ambientLight=new Color(.63f,.57f,.51f);
        var profile=lights.GetComponent<Volume>().sharedProfile;
        if(profile.TryGet<Bloom>(out var bloom))
        {
            bloom.threshold.Override(1.04f);
            bloom.intensity.Override(.49f);
            bloom.scatter.Override(.66f);
            EditorUtility.SetDirty(bloom);
        }
        var counterArt=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/City Mirage/"+"Checkout Apparel Displays.mat");
        if(counterArt){counterArt.SetColor("_EmissionColor",Color.white*2.05f);EditorUtility.SetDirty(counterArt);}
        AssetDatabase.SaveAssets();
        if(!EditorSceneManager.SaveScene(scene))throw new IOException("Could not save Cloth Shop.");
        if(previous.IsValid()&&previous.isLoaded&&previous!=scene)SceneManager.SetActiveScene(previous);
    }
    static Material MaterialFor(string name,Color color,float metal,float smooth)
    {
        string path="Assets/Materials/City Mirage/"+name+".mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(m==null){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
        m.SetColor("_BaseColor",color);m.SetFloat("_Metallic",metal);m.SetFloat("_Smoothness",smooth);
        EditorUtility.SetDirty(m);return m;
    }
    static void Box(Transform parent,string name,Vector3 p,Vector3 size,Material mat)
    {
        var o=GameObject.CreatePrimitive(PrimitiveType.Cube);
        o.name=name;o.transform.SetParent(parent,false);o.transform.position=p;o.transform.localScale=size;
        UnityEngine.Object.DestroyImmediate(o.GetComponent<Collider>());
        o.GetComponent<MeshRenderer>().sharedMaterial=mat;
    }
    static void Bar(Transform parent,string name,Vector3 a,Vector3 b,float width,Material mat)
    {
        Box(parent,name,(a+b)*.5f,new Vector3(width,(b-a).magnitude,width),mat);
        parent.GetChild(parent.childCount-1).rotation=Quaternion.FromToRotation(Vector3.up,(b-a).normalized);
    }
    static Mesh Panel(string name,float w,float h,float z,int slot)
    {
        float x=w*.5f,y=h*.5f, u0=(slot%4+.034f)/4,u1=(slot%4+.966f)/4,v0=(slot/4+.034f)/4,v1=(slot/4+.966f)/4;
        var m=new Mesh{name=name};
        m.vertices=new[]{new Vector3(-x,-y,-z),new Vector3(-x,y,-z),new Vector3(x,y,-z),new Vector3(x,-y,-z),new Vector3(x,-y,z),new Vector3(x,y,z),new Vector3(-x,y,z),new Vector3(-x,-y,z)};
        m.uv=Enumerable.Range(0,2).SelectMany(_=>new[]{new Vector2(u0,v0),new Vector2(u0,v1),new Vector2(u1,v1),new Vector2(u1,v0)}).ToArray();
        m.triangles=new[]{0,1,2,0,2,3,4,5,6,4,6,7};m.RecalculateNormals();
        return m;
    }
    static void SaveMesh(Mesh m)
    {
        if(first){AssetDatabase.CreateAsset(m,MeshPath);first=false;}else AssetDatabase.AddObjectToAsset(m,MeshPath);
    }
    static void ObjectFor(Transform parent,Mesh m,Material mat,Vector3 position)
    {
        SaveMesh(m);
        var o=new GameObject(m.name);o.transform.SetParent(parent,false);o.transform.position=position;
        o.AddComponent<MeshFilter>().sharedMesh=m;
        var r=o.AddComponent<MeshRenderer>();r.sharedMaterial=mat;r.shadowCastingMode=ShadowCastingMode.Off;
    }
    static Mesh SmoothPot()
    {
        const int n=24;
        var rings=new[]{new Vector2(.33f,0),new Vector2(.365f,.042f),new Vector2(.474f,.92f),new Vector2(.50f,.965f),new Vector2(.485f,1),new Vector2(.426f,1),new Vector2(.418f,.948f),new Vector2(.322f,.115f)};
        var v=new List<Vector3>();var t=new List<int>();
        for(int j=0;j<rings.Length;j++)
        for(int i=0;i<n;i++)
        {
            float a=i*Mathf.PI*2/n;
            v.Add(new Vector3(Mathf.Cos(a)*rings[j].x,rings[j].y,Mathf.Sin(a)*rings[j].x));
            if(j==rings.Length-1)continue;
            int k=j*n+i,next=j*n+(i+1)%n;
            t.AddRange(new[]{k,k+n,next,next,k+n,next+n});
        }
        for(int i=1;i<n-1;i++){t.AddRange(new[]{0,i,i+1});int k=(rings.Length-1)*n;t.AddRange(new[]{k,k+i+1,k+i});}
        var m=new Mesh{name="Smooth 24-sided ceramic planter"};
        m.SetVertices(v);m.SetTriangles(t,0);m.RecalculateNormals();m.RecalculateBounds();return m;
    }
    static void Spot(Transform parent,string name,Vector3 p,Vector3 aim,Color c,float energy,float range,float angle)
    {
        var o=new GameObject(name);o.transform.SetParent(parent,false);o.transform.position=p;o.transform.LookAt(aim);
        var l=o.AddComponent<Light>();l.type=LightType.Spot;l.color=c;l.intensity=energy;l.range=range;l.spotAngle=angle;l.innerSpotAngle=angle*.58f;l.shadows=LightShadows.None;
    }
}

