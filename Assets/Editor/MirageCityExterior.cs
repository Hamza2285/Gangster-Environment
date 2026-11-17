using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

internal static class MirageCityExterior
{
    const string Folder="Assets/Texture/Showroom Exterior";
    const string Trigger="Showroom exterior.request";
    const string Report="Showroom Exterior Verification.txt";
    [InitializeOnLoadMethod]
    static void Schedule()
    {
        EditorApplication.delayCall += () =>
        {
            if (!File.Exists(Trigger)) return;
            File.Delete(Trigger);
            try { Apply(); }
            catch(Exception ex) { File.WriteAllText(Report,ex.ToString()); Debug.LogException(ex); }
        };
    }
    static Texture2D Map(string file,bool alpha,bool sky=false)
    {
        string path=Folder+"/"+file+".png";
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
        var imp=(TextureImporter)AssetImporter.GetAtPath(path);
        imp.sRGBTexture=true; imp.alphaSource=alpha?TextureImporterAlphaSource.FromInput:TextureImporterAlphaSource.None;
        imp.alphaIsTransparency=alpha; imp.mipMapsPreserveCoverage=alpha; imp.alphaTestReferenceValue=.5f;
        imp.mipmapEnabled=true; imp.filterMode=FilterMode.Trilinear; imp.anisoLevel=4;
        imp.wrapMode=TextureWrapMode.Clamp; if(sky) imp.wrapModeU=TextureWrapMode.Repeat;
        imp.maxTextureSize=2048; imp.textureCompression=TextureImporterCompression.CompressedHQ;
        foreach(var platform in new[]{"Android","iPhone"})
        {
            var settings=imp.GetPlatformTextureSettings(platform);
            settings.overridden=true; settings.maxTextureSize=2048; settings.format=TextureImporterFormat.ASTC_6x6;
            imp.SetPlatformTextureSettings(settings);
        }
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
    static Material MaterialAsset(string name,Shader shader)
    {
        string path=Folder+"/"+name+".mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!m) { m=new Material(shader){name=name}; AssetDatabase.CreateAsset(m,path); }
        return m;
    }
    sealed class Batch
    {
        readonly List<Vector3> vertices=new();
        readonly List<Vector2> uv=new();
        readonly List<int> triangles=new();
        public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Rect r)
        {
            int i=vertices.Count;
            vertices.AddRange(new[]{a,b,c,d});
            uv.AddRange(new[]{new Vector2(r.xMin,r.yMin),new Vector2(r.xMax,r.yMin),new Vector2(r.xMax,r.yMax),new Vector2(r.xMin,r.yMax)});
            triangles.AddRange(new[]{i,i+1,i+2,i,i+2,i+3});
        }
        public void Box(Vector3 p,Vector3 size,Rect tile)
        {
            var e=size*.5f;
            var a=p+new Vector3(-e.x,-e.y,-e.z); var b=p+new Vector3(e.x,-e.y,-e.z);
            var c=p+new Vector3(e.x,e.y,-e.z); var d=p+new Vector3(-e.x,e.y,-e.z);
            var delta=Vector3.forward*size.z;
            Quad(a,b,c,d,tile); Quad(b,b+delta,c+delta,c,tile); Quad(b+delta,a+delta,d+delta,c+delta,tile);
            Quad(a+delta,a,d,d+delta,tile); Quad(d,c,c+delta,d+delta,tile);
        }
        public Mesh Save(string name)
        {
            var m=new Mesh{name=name}; m.SetVertices(vertices);m.SetUVs(0,uv);m.SetTriangles(triangles,0);m.RecalculateBounds();
            string path=Folder+"/"+name+".asset";
            var current=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(current){EditorUtility.CopySerialized(m,current);UnityEngine.Object.DestroyImmediate(m);return current;}
            AssetDatabase.CreateAsset(m,path);return m;
        }
    }
    static Rect Surface(int i)=>new Rect((i*256+5)/2048f,1029/2048f,246/2048f,246/2048f);
    static Rect Facade(int i)=>new Rect((i*512+10)/2048f,10/2048f,492/2048f,960/2048f);
    static void Flat(Batch b,float x0,float x1,float z0,float z1,float y,Rect r)
    {
        b.Quad(new Vector3(x0,y,z0),new Vector3(x1,y,z0),new Vector3(x1,y,z1),new Vector3(x0,y,z1),r);
    }
    [MenuItem("Tools/Showroom/Build Blue Hour Exterior")]
    static void Apply()
    {
        var s=SceneManager.GetActiveScene();
        if(s.path!="Assets/Scenes/Car Show room.unity"||EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Open the showroom scene in edit mode first.");
        var model=s.GetRootGameObjects().Single(g=>PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(g)=="Assets/Model/Show Room.fbx" || (g.name=="Show Room" && g.transform.Find("Ceiling")));
        var all=model.GetComponentsInChildren<Transform>(true);
        var transforms=all.ToDictionary(t=>t.GetInstanceID(),t=>t.localToWorldMatrix);
        var lights=s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Light>(true)).ToDictionary(l=>l.GetInstanceID(),l=>(l.intensity,l.color,l.lightmapBakeType,l.transform.localToWorldMatrix));
        var ambient=(RenderSettings.ambientMode,RenderSettings.ambientSkyColor,RenderSettings.ambientEquatorColor,RenderSettings.ambientGroundColor,RenderSettings.ambientIntensity,RenderSettings.reflectionIntensity);
        var lightmaps=LightmapSettings.lightmaps.Select(l=>l.lightmapColor).ToArray();
        if(!File.Exists("Showroom before exterior.unity.backup"))File.Copy(s.path,"Showroom before exterior.unity.backup");
        var floor=model.GetComponentsInChildren<MeshRenderer>().Single(r=>r.name=="Floor").bounds;
        var window=model.GetComponentsInChildren<MeshRenderer>().Single(r=>r.name=="Window").bounds;
        var entrance=model.GetComponentsInChildren<MeshRenderer>().Single(r=>r.name=="Entrance Frame").bounds;
        float x0=floor.min.x,x1=floor.max.x,z0=floor.min.z,z1=floor.max.z,y=floor.max.y-.039f;
        var road=new Batch(); var facades=new Batch(); var lightsMesh=new Batch();
        float side=2.6f, width=7.0f, far=3.0f, outer=side+width+far;
        void Ring(float inner,float edge,float height,int surface,float step)
        {
            void Strip(float xa,float xb,float za,float zb,bool horizontal)
            {
                int nx=horizontal?Mathf.CeilToInt((xb-xa)/step):1;
                int nz=horizontal?1:Mathf.CeilToInt((zb-za)/step);
                for(int i=0;i<nx;i++)for(int j=0;j<nz;j++)
                    Flat(road,Mathf.Lerp(xa,xb,i/(float)nx),Mathf.Lerp(xa,xb,(i+1f)/nx),
                        Mathf.Lerp(za,zb,j/(float)nz),Mathf.Lerp(za,zb,(j+1f)/nz),height,Surface(surface));
            }
            Strip(x0-edge,x1+edge,z0-edge,z0-inner,true);
            Strip(x0-edge,x1+edge,z1+inner,z1+edge,true);
            Strip(x0-edge,x0-inner,z0-inner,z1+inner,false);
            Strip(x1+inner,x1+edge,z0-inner,z1+inner,false);
        }
        Ring(0,side,y,1,2.3f);
        Ring(side,side+width,y-.09f,0,5);
        Ring(side+width,outer,y-.025f,1,2.3f);
        void Curb(float n,float top)
        {
            road.Box(new Vector3((x0+x1)/2,top-.05f,z0-n),new Vector3(x1-x0+2*n,.1f,.15f),Surface(2));
            road.Box(new Vector3((x0+x1)/2,top-.05f,z1+n),new Vector3(x1-x0+2*n,.1f,.15f),Surface(2));
            road.Box(new Vector3(x0-n,top-.05f,(z0+z1)/2),new Vector3(.15f,.1f,z1-z0+2*n),Surface(2));
            road.Box(new Vector3(x1+n,top-.05f,(z0+z1)/2),new Vector3(.15f,.1f,z1-z0+2*n),Surface(2));
        }
        Curb(side,y);Curb(side+width,y-.025f);
        float center=side+width*.5f, paint=y-.086f;
        for(float x=x0-5;x<x1+5;x+=4.7f)
        {
            Flat(road,x,x+2.4f,z0-center-.055f,z0-center+.055f,paint,Surface(3));
            Flat(road,x,x+2.4f,z1+center-.055f,z1+center+.055f,paint,Surface(3));
        }
        for(float z=z0-5;z<z1+5;z+=4.7f)
        {
            Flat(road,x0-center-.055f,x0-center+.055f,z,z+2.4f,paint,Surface(3));
            Flat(road,x1+center-.055f,x1+center+.055f,z,z+2.4f,paint,Surface(3));
        }
        float front=Mathf.Sign(entrance.center.z-floor.center.z);
        float glassSide=Mathf.Sign(window.center.x-floor.center.x);
        for(int i=0;i<8;i++)
        {
            float offset=side+.3f+i*.81f;
            float z=front>0?z1+offset:z0-offset;
            Flat(road,entrance.center.x-1.4f,entrance.center.x+1.4f,z-.19f,z+.19f,paint,Surface(5));
        }
        for(int face=0;face<4;face++)
        {
            bool alongX=face<2;
            float start=(alongX?x0:z0)-outer;
            float end=(alongX?x1:z1)+outer;
            float edge=face==0?z0-outer:face==1?z1+outer:face==2?x0-outer:x1+outer;
            float sign=face%2==0?-1:1;
            int count=Mathf.CeilToInt((end-start)/13f);
            for(int i=0;i<count;i++)
            {
                float t0=Mathf.Lerp(start,end,i/(float)count)+.19f;
                float t1=Mathf.Lerp(start,end,(i+1f)/count)-.19f;
                float e=edge+sign*((i%2)*1.2f);
                float h=12.8f+(i%3)*1.75f;
                Vector3 Point(float t,float z)=>alongX?new Vector3(t,z,e):new Vector3(e,z,t);
                facades.Quad(Point(t0,y-.026f),Point(t1,y-.026f),Point(t1,y+h),Point(t0,y+h),Facade((i+face)%4));
            }
            for(int i=1;i<=3;i++)
            {
                float t=Mathf.Lerp(start+outer,end-outer,i/4f);
                float p=edge-sign*(outer-side-.24f);
                Vector3 pos=alongX?new Vector3(t,y,p):new Vector3(p,y,t);
                if(alongX&&Mathf.Abs(pos.z-entrance.center.z)<3&&Mathf.Abs(t-entrance.center.x)<3)continue;
                var outward=alongX?Vector3.forward*sign:Vector3.right*sign;
                lightsMesh.Box(pos+Vector3.up*1.95f,new Vector3(.105f,3.9f,.105f),Surface(4));
                lightsMesh.Box(pos+Vector3.up*.12f,new Vector3(.25f,.24f,.25f),Surface(6));
                lightsMesh.Box(pos+Vector3.up*3.86f+outward*.34f,alongX?new Vector3(.12f,.11f,.74f):new Vector3(.74f,.11f,.12f),Surface(4));
                lightsMesh.Box(pos+Vector3.up*3.79f+outward*.63f,new Vector3(.25f,.055f,.22f),Surface(7));
            }
        }
        AssetDatabase.ImportAsset(Folder+"/Exterior Atlas.shader",ImportAssetOptions.ForceSynchronousImport);
        var mat=MaterialAsset("City Exterior Atlas",AssetDatabase.LoadAssetAtPath<Shader>(Folder+"/Exterior Atlas.shader"));
        mat.SetTexture("_BaseMap",Map("City Exterior Atlas",true));
        mat.SetTexture("_EmissionMap",Map("City Exterior Emission",false));
        var sky=MaterialAsset("Mirage Blue Hour Skybox",Shader.Find("Skybox/Panoramic"));
        sky.SetTexture("_MainTex",Map("Mirage Blue Hour Sky",false,true));sky.SetFloat("_Exposure",1);
        var root=s.GetRootGameObjects().FirstOrDefault(g=>g.name=="Mirage City Exterior") ?? new GameObject("Mirage City Exterior");
        void Assign(string name,Batch batch)
        {
            var t=root.transform.Find(name);
            var g=t?t.gameObject:new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));
            if(!t)g.transform.SetParent(root.transform,false);
            g.GetComponent<MeshFilter>().sharedMesh=batch.Save(name);
            var renderer=g.GetComponent<MeshRenderer>();
            renderer.sharedMaterial=mat;renderer.shadowCastingMode=ShadowCastingMode.Off;
            renderer.receiveShadows=false;renderer.lightProbeUsage=LightProbeUsage.Off;renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
        }
        Assign("Exterior Street and Pavement",road);
        Assign("Exterior Facade Cutouts",facades);
        Assign("Exterior Streetlight Props",lightsMesh);
        RenderSettings.skybox=sky;
        if(ambient!=(RenderSettings.ambientMode,RenderSettings.ambientSkyColor,RenderSettings.ambientEquatorColor,RenderSettings.ambientGroundColor,RenderSettings.ambientIntensity,RenderSettings.reflectionIntensity) ||
           !lightmaps.SequenceEqual(LightmapSettings.lightmaps.Select(l=>l.lightmapColor)) ||
           all.Any(t=>t.localToWorldMatrix!=transforms[t.GetInstanceID()]) ||
           s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Light>(true)).Any(l=>!lights.TryGetValue(l.GetInstanceID(),out var prev)||prev!=(l.intensity,l.color,l.lightmapBakeType,l.transform.localToWorldMatrix)))
            throw new InvalidOperationException("Existing lighting or placement changed.");
        EditorUtility.SetDirty(mat);EditorUtility.SetDirty(sky);
        EditorSceneManager.MarkSceneDirty(s);AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(s);
        long tris=root.GetComponentsInChildren<MeshFilter>().Sum(f=>(long)f.sharedMesh.GetIndexCount(0)/3);
        File.WriteAllText(Report,$"Exterior triangles: {tris}\nExterior renderers: 3\nOne atlas UV layout with color and emission maps; one skybox.\nInterior transforms unchanged: {all.Length}\nInterior lights unchanged: {lights.Count}\nExisting baked lightmaps preserved: {lightmaps.Length}\n");
        var look= new Vector3(floor.center.x+glassSide*6.5f,y+3.6f,floor.center.z-front*3.7f);
        var at=new Vector3(floor.center.x+glassSide*1.8f,y+4.25f,entrance.center.z-front*2.8f);
        Capture(at,look,"Showroom City Exterior Preview.png");
                Capture(new Vector3(floor.center.x+glassSide*(floor.extents.x+8.4f),y+3.7f,
          front>0?z1+6.5f:z0-6.5f),
          new Vector3(floor.center.x+glassSide*(floor.extents.x*.75f),y+2.7f,floor.center.z),"Showroom City Street Preview.png");
    }
    static void Capture(Vector3 position,Vector3 target,string file)
    {
        var obj=new GameObject("Temporary city preview"){hideFlags=HideFlags.HideAndDontSave};
        var cam=obj.AddComponent<Camera>();cam.enabled=false;cam.allowHDR=true;cam.fieldOfView=65;
        obj.transform.SetPositionAndRotation(position,Quaternion.LookRotation(target-position));
        var data=cam.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;
        data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        var rt=new RenderTexture(1360,800,24);var img=new Texture2D(1360,800,TextureFormat.RGB24,false);
        var old=RenderTexture.active;
        try
        {
            RenderPipeline.SubmitRenderRequest(cam,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
            RenderTexture.active=rt;img.ReadPixels(new Rect(0,0,1360,800),0,0);img.Apply();
            File.WriteAllBytes(file,img.EncodeToPNG());
        }
        finally { RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(img);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(obj); }
    }
}



