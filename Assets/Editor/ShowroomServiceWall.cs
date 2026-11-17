using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

internal static class ShowroomServiceWall
{
    const string ScenePath="Assets/Scenes/Car Show room.unity";
    const string Atlas="Assets/Texture/Showroom Atlases";
    const string Folder="Assets/Prefeb/Showroom";
    const string Trigger="Showroom service wall.request";
    const string Report="Showroom Service Wall Verification.txt";

    [InitializeOnLoadMethod]
    static void Schedule()
    {
        EditorApplication.delayCall+=()=>
        {
            if(!File.Exists(Trigger))return;
            File.Delete(Trigger);
            try{if(File.Exists("Showroom ceiling finish.request")){File.Delete("Showroom ceiling finish.request");FinishCeiling();}else Apply();}
            catch(Exception e){File.AppendAllText(Report,e+"\n");Debug.LogException(e);}
        };
    }
    sealed class Geometry
    {
        readonly List<Vector3> v=new();
        readonly List<Vector3> normals=new();
        readonly List<Vector2> uv=new();
        readonly List<int> triangles=new();
        public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Rect tile,bool flatColor=false)
        {
            int i=v.Count;
            v.AddRange(new[]{a,b,c,d});
            Vector3 n=Vector3.Cross(b-a,c-a).normalized;
            normals.AddRange(new[]{n,n,n,n});
            uv.AddRange(flatColor?new[]{tile.center,tile.center,tile.center,tile.center}:
                new[]{new Vector2(tile.xMin,tile.yMin),new Vector2(tile.xMax,tile.yMin),
                      new Vector2(tile.xMax,tile.yMax),new Vector2(tile.xMin,tile.yMax)});
            triangles.AddRange(new[]{i,i+1,i+2,i,i+2,i+3});
        }
        public void Box(Vector3 p,Vector3 size,Rect r,bool flatColor=false,Quaternion? rotation=null,bool omitTop=false)
        {
            var h=size*.5f;
            Vector3 At(float x,float y,float z)=>p+(rotation??Quaternion.identity)*new Vector3(x*h.x,y*h.y,z*h.z);
            var a=At(-1,-1,-1);var b=At(1,-1,-1);var c=At(1,1,-1);var d=At(-1,1,-1);
            var A=At(-1,-1,1);var B=At(1,-1,1);var C=At(1,1,1);var D=At(-1,1,1);
            Quad(a,d,c,b,r,flatColor);Quad(A,B,C,D,r,flatColor);Quad(a,A,D,d,r,flatColor);
            Quad(b,c,C,B,r,flatColor);if(!omitTop)Quad(d,D,C,c,r,flatColor);Quad(a,b,B,A,r,flatColor);
        }
        public void Tube(Vector3 p,float outer,float inner,float depth,Rect r)
        {
            const int N=12;
            Vector3 P(int i,float radius,float z)=>p+new Vector3(Mathf.Cos(i*Mathf.PI*2/N)*radius,Mathf.Sin(i*Mathf.PI*2/N)*radius,z);
            for(int i=0;i<N;i++)
            {
                int j=i+1;
                Quad(P(i,outer,depth),P(j,outer,depth),P(j,inner,depth),P(i,inner,depth),r,true);
                Quad(P(j,outer,0),P(i,outer,0),P(i,outer,depth),P(j,outer,depth),r,true);
            }
        }
        public Mesh Build(string name)
        {
            var mesh=new Mesh{name=name};
            mesh.SetVertices(v);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);
            mesh.RecalculateBounds();
            return mesh;
        }
        public void CeilingDisc(Vector3 p,float inner,float outer,Rect bezel,Rect lens)
        {
            const int n=8;
            Vector3 Point(int i,float r)=>p+new Vector3(Mathf.Cos(i*Mathf.PI*2/n)*r,0,Mathf.Sin(i*Mathf.PI*2/n)*r);
            for(int i=0;i<n;i++)Quad(Point(i,outer),Point(i+1,outer),Point(i+1,inner),Point(i,inner),bezel,true);
            int start=v.Count;
            v.Add(p);normals.Add(Vector3.down);uv.Add(lens.center);
            for(int i=0;i<n;i++){v.Add(Point(i,inner));normals.Add(Vector3.down);uv.Add(lens.center);}
            for(int i=0;i<n;i++)triangles.AddRange(new[]{start,start+1+i,start+1+(i+1)%n});
        }
        public Mesh Save(string folder,string name)
        {
            var m=Build(name);Unwrapping.GenerateSecondaryUVSet(m);
            string path=folder+"/"+name+".asset";
            var prev=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(prev){EditorUtility.CopySerialized(m,prev);UnityEngine.Object.DestroyImmediate(m);return prev;}
            AssetDatabase.CreateAsset(m,path);return m;
        }
    }
    static Mesh Rack(string folder,Dictionary<string,Rect> tile)
    {
        var g=new Geometry();
        Rect marble=tile["Black marble"],dark=tile["Charcoal powder coat"],brass=tile["Brushed brass"],
             light=tile["Amber light strip"],wood=tile["Walnut"],paper=tile["Warm ivory"];
        // Shallow wall-mounted case. Each shelf has a slim warm edge and space for the display pieces.
        g.Box(new Vector3(0,1.06f,.027f),new Vector3(1.16f,2.12f,.054f),marble);
        foreach(float x in new[]{-.558f,.558f})
        {
            g.Box(new Vector3(x,1.06f,.165f),new Vector3(.061f,2.12f,.32f),dark,true);
            g.Box(new Vector3(x,1.06f,.330f),new Vector3(.026f,2.10f,.021f),brass,true);
        }
        foreach(float h in new[]{.032f,.70f,1.39f,2.088f})
        {
            g.Box(new Vector3(0,h,.174f),new Vector3(1.10f,.061f,.32f),wood);
            g.Box(new Vector3(0,h+.010f,.341f),new Vector3(1.085f,.017f,.014f),light,true);
        }
        // Trophy silhouette, an alloy wheel and a few books, kept in the rack mesh.
        g.Box(new Vector3(-.25f,1.455f,.217f),new Vector3(.22f,.047f,.16f),brass,true);
        g.Box(new Vector3(-.25f,1.56f,.217f),new Vector3(.067f,.19f,.068f),brass,true);
        g.Box(new Vector3(-.25f,1.69f,.217f),new Vector3(.20f,.132f,.17f),brass,true);
        foreach(float a in new[]{-1f,1f})
            g.Box(new Vector3(-.25f+a*.115f,1.705f,.215f),new Vector3(.026f,.112f,.036f),brass,true,Quaternion.Euler(0,0,a*21));
        g.Tube(new Vector3(.25f,1.62f,.175f),.158f,.125f,.060f,dark);
        g.Tube(new Vector3(.25f,1.62f,.238f),.128f,.099f,.014f,brass);
        for(int i=0;i<5;i++)
        {
            float t=i*72f;
            Vector3 radial=Quaternion.Euler(0,0,t)*Vector3.up;
            g.Box(new Vector3(.25f,1.62f,.25f)+radial*.056f,new Vector3(.019f,.104f,.012f),brass,true,Quaternion.Euler(0,0,t));
        }
        g.Box(new Vector3(0,.777f,.199f),new Vector3(.57f,.094f,.22f),dark,true);
        g.Box(new Vector3(0,.837f,.209f),new Vector3(.45f,.031f,.19f),brass,true);
        for(int i=0;i<5;i++)
            g.Box(new Vector3(-.34f+i*.13f,.19f+(i%3)*.025f,.205f),
                new Vector3(.10f,.26f+(i%3)*.05f,.20f),i%3==0?brass:i%3==1?paper:dark,true,
                Quaternion.Euler(0,0,i==4?-9f:0));
        return g.Save(folder,"Service Display Rack");
    }
    static Mesh Print(string folder,Dictionary<string,Rect> tile,int art)
    {
        var g=new Geometry();Rect dark=tile["Charcoal powder coat"],brass=tile["Brushed brass"],light=tile["Amber light strip"];
        float w=2.64f,h=1.32f;
        g.Box(new Vector3(0,0,.036f),new Vector3(w,h,.073f),dark,true);
        foreach(float x in new[]{-w*.5f,w*.5f})
        {
            g.Box(new Vector3(x,0,.075f),new Vector3(.040f,h+.04f,.042f),brass,true);
            g.Box(new Vector3(x,0,.098f),new Vector3(.014f,h-.02f,.008f),light,true);
        }
        foreach(float y in new[]{-h*.5f,h*.5f})
        {
            g.Box(new Vector3(0,y,.075f),new Vector3(w,.040f,.042f),brass,true);
            g.Box(new Vector3(0,y,.098f),new Vector3(w-.035f,.014f,.008f),light,true);
        }
        // The wall-facing front uses the same image regions as the upstairs gallery.
        g.Quad(new Vector3(-1.292f,-.637f,.079f),new Vector3(1.292f,-.637f,.079f),
               new Vector3(1.292f,.637f,.079f),new Vector3(-1.292f,.637f,.079f),tile["Neon Car Print "+art]);
        return g.Save(folder,"Service Wall Car Artwork "+art);
    }
    static GameObject Template(string name,Mesh mesh,Material mat)
    {
        string path=Folder+"/"+name+".prefab";
        var existing=AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if(existing)return existing;
        var obj=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));
        obj.GetComponent<MeshFilter>().sharedMesh=mesh;
        var r=obj.GetComponent<MeshRenderer>();r.sharedMaterial=mat;
        r.receiveGI=ReceiveGI.Lightmaps;
        r.motionVectorGenerationMode=MotionVectorGenerationMode.ForceNoMotion;
        GameObjectUtility.SetStaticEditorFlags(obj,StaticEditorFlags.ContributeGI|StaticEditorFlags.BatchingStatic);
        var result=PrefabUtility.SaveAsPrefabAsset(obj,path);
        UnityEngine.Object.DestroyImmediate(obj);
        if(!result)throw new IOException("Could not save "+path);
        return result;
    }
    [MenuItem("Tools/Showroom/Furnish Customer Services Wall")]
    static void Apply()
    {
        var scene=SceneManager.GetActiveScene();
        if(scene.path!=ScenePath||Lightmapping.isRunning||EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Open the showroom in Edit Mode and wait for any bake.");
        var room=scene.GetRootGameObjects().Single(o=>o.name=="Show Room"&&o.transform.Find("Ceiling"));
        const string backup="Showroom before service wall.unity.backup";
        if(!File.Exists(backup))File.Copy(ScenePath,backup);
        var existing=room.GetComponentsInChildren<Transform>(true).ToDictionary(t=>t,t=>t.localToWorldMatrix);
        var renderers=room.GetComponentsInChildren<MeshRenderer>(true);
        float front=Mathf.Sign(room.transform.InverseTransformPoint(renderers.Single(r=>r.name=="Entrance Frame").bounds.center).z);
        float right=Mathf.Sign(room.transform.InverseTransformPoint(renderers.Single(r=>r.name=="Ramp").bounds.center).x);
        var tiles=File.ReadAllLines(Atlas+"/Atlas Layout.csv").Skip(1).Select(line=>line.Split(',')).Where(row=>row[0]=="Props")
            .ToDictionary(row=>row[1],row=>new Rect(int.Parse(row[2])/2048f,int.Parse(row[3])/2048f,int.Parse(row[4])/2048f,int.Parse(row[5])/2048f));
        var mat=AssetDatabase.LoadAssetAtPath<Material>(Atlas+"/Showroom Props.mat");
        if(!mat)throw new InvalidOperationException("The shared props atlas material is missing.");
        const string meshFolder=Folder+"/Service Wall Meshes";
        if(!AssetDatabase.IsValidFolder(meshFolder))AssetDatabase.CreateFolder(Folder,"Service Wall Meshes");
        var rack=Rack(meshFolder,tiles);
        int[] art={1,2,4,5};
        var prints=art.ToDictionary(i=>i,i=>Print(meshFolder,tiles,i));
        var rackPrefab=Template("PF_Showroom_DisplayRack",rack,mat);
        var artPrefab=Template("PF_Showroom_WallCarArtwork",prints[1],mat);
        var parent=room.transform.Find("Customer Services Wall Display");
        if(!parent)
        {
            parent=new GameObject("Customer Services Wall Display").transform;
            parent.SetParent(room.transform,false);
        }
        Quaternion rotation=Quaternion.LookRotation(new Vector3(0,0,front),Vector3.up);
        GameObject Place(string name,GameObject prefab,Mesh mesh,float x,float y,float rear)
        {
            var existingProp=parent.Find(name);
            var g=existingProp?existingProp.gameObject:(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);
            g.name=name;g.transform.localPosition=new Vector3(x*right,y,-rear*front);g.transform.localRotation=rotation;
            g.transform.localScale=Vector3.one;
            var f=g.GetComponent<MeshFilter>();
            f.sharedMesh=mesh;
            PrefabUtility.RecordPrefabInstancePropertyModifications(g);
            PrefabUtility.RecordPrefabInstancePropertyModifications(g.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(f);
            return g;
        }
        var placed=new List<GameObject>
        {
            Place("Display Rack Left",rackPrefab,rack,-3.29f,.76f,13.93f),
            Place("Display Rack Right",rackPrefab,rack,3.29f,.76f,13.93f)
        };
        float[] xs={-10.56f,-5.65f,5.65f,10.56f};
        for(int i=0;i<xs.Length;i++)placed.Add(Place("Wall Car Artwork "+(i+1).ToString("00"),artPrefab,prints[art[i]],xs[i],2.15f,13.925f));
        if(placed.Count!=6||placed.Any(o=>!PrefabUtility.IsPartOfPrefabInstance(o)))throw new InvalidOperationException("The wall displays must be prefab instances.");
        if(existing.Any(q=>q.Key.localToWorldMatrix!=q.Value))throw new InvalidOperationException("An existing object moved.");
        int tris=placed.Sum(o=>(int)o.GetComponent<MeshFilter>().sharedMesh.GetIndexCount(0)/3);
        File.WriteAllText(Report,$"Rack prefab instances: 2\nWall artwork prefab instances: 4\nShared material: Showroom Props\nAdded geometry: {tris} triangles\nExisting placements preserved: {existing.Count}\n");
        EditorSceneManager.MarkSceneDirty(scene);AssetDatabase.SaveAssets();
        if(!EditorSceneManager.SaveScene(scene))throw new IOException("Could not save the scene.");
        Capture(room.transform,front,"Customer Services Wall Preview.png");
        Lightmapping.bakeCompleted+=Done;
        if(!Lightmapping.BakeAsync()){Lightmapping.bakeCompleted-=Done;throw new InvalidOperationException("Unity did not start the bake.");}
        void Done()
        {
            Lightmapping.bakeCompleted-=Done;
            EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            File.AppendAllText(Report,$"Baked lightmaps: {LightmapSettings.lightmaps.Length}\n");
            Capture(room.transform,front,"Customer Services Wall Baked Preview.png");
        }
    }
    [MenuItem("Tools/Showroom/Detail Mezzanine Ceiling")]
    static void FinishCeiling()
    {
        var scene=SceneManager.GetActiveScene();
        if(scene.path!=ScenePath||Lightmapping.isRunning||EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Open the showroom in Edit Mode and finish any active bake.");
        var room=scene.GetRootGameObjects().Single(o=>o.name=="Show Room"&&o.transform.Find("Ceiling"));
        var list=room.GetComponentsInChildren<MeshRenderer>(true);
        var mezz=list.Single(r=>r.name=="Mezzanine");
        var filter=mezz.GetComponent<MeshFilter>();
        float front=Mathf.Sign(room.transform.InverseTransformPoint(list.Single(r=>r.name=="Entrance Frame").bounds.center).z);
        float right=Mathf.Sign(room.transform.InverseTransformPoint(list.Single(r=>r.name=="Ramp").bounds.center).x);
        var state=room.GetComponentsInChildren<Transform>(true).ToDictionary(t=>t,t=>t.localToWorldMatrix);
        var existingMaterial=mezz.sharedMaterial;
        var textures=existingMaterial.GetTexturePropertyNames().Select(existingMaterial.GetTexture).ToArray();
        var tiles=File.ReadAllLines(Atlas+"/Atlas Layout.csv").Skip(1).Select(line=>line.Split(',')).Where(row=>row[0]=="Structure")
            .ToDictionary(row=>row[1],row=>new Rect(int.Parse(row[2])/2048f,int.Parse(row[3])/2048f,int.Parse(row[4])/2048f,int.Parse(row[5])/2048f));
        var g=new Geometry();
        Rect walnut=tiles["Walnut"],dark=tiles["Charcoal powder coat"],gold=tiles["Brushed brass"],
             warm=tiles["Amber light strip"],white=tiles["Warm spotlight diffuser"];
        Vector3 P(float x,float y,float depth)=>new Vector3(x*right,y,-front*depth);
        void Box(float x,float y,float z,float w,float h,float l,Rect mat,bool flat=true)
        {
            var p=P(x,y,z);
            if(mat.Equals(warm)||mat.Equals(dark))
            {
                p.y-=h*.5f;
                g.Quad(p+new Vector3(-w*.5f,0,-l*.5f),p+new Vector3(w*.5f,0,-l*.5f),
                       p+new Vector3(w*.5f,0,l*.5f),p+new Vector3(-w*.5f,0,l*.5f),mat,flat);
            }
            else g.Box(p,new Vector3(w,h,l),mat,flat,omitTop:true);
        }
        void Border(float x,float z,float width,float length,float gauge,float height,float y,Rect mat)
        {
            foreach(float sign in new[]{-1f,1f})
            {
                Box(x,y,z+sign*(length-gauge)*.5f,width,height,gauge,mat);
                Box(x+sign*(width-gauge)*.5f,y,z,gauge,height,length-2*gauge,mat);
            }
        }
        void Cassette(float x,float z,float width,float length,int slats,int lamps)
        {
            Box(x,3.461f,z,width-.08f,.018f,length-.08f,dark);
            Border(x,z,width,length,.112f,.053f,3.432f,gold);
            Border(x,z,width-.188f,length-.188f,.023f,.018f,3.402f,warm);
            float inset=.22f;
            for(int i=0;i<slats;i++)
            {
                float px=Mathf.Lerp(x-width*.5f+inset,x+width*.5f-inset,i/(float)(slats-1));
                Box(px,3.426f,z,.072f,.049f,length-.42f,walnut,false);
            }
            for(int i=0;i<lamps;i++)
            {
                float px=x+(i-(lamps-1)*.5f)*(width*.27f);
                float pz=z+(i%2==0?-.28f:.28f);
                Box(px,3.391f,pz,.055f,.014f,length*.47f,warm);
            }
        }
        Cassette(0,8.48f,8.85f,3.28f,22,3);
        Cassette(-10.05f,9.90f,2.23f,3.35f,7,1);
        Cassette(10.05f,9.90f,2.23f,3.35f,7,1);
        // Perimeter return and small recessed fittings, all below the existing soffit.
        Box(0,3.444f,5.03f,13.25f,.019f,.041f,warm);
        Box(0,3.444f,13.65f,26.95f,.019f,.041f,warm);
        foreach(float x in new[]{-13.30f,13.30f})
            Box(x,3.444f,10.76f,.041f,.019f,5.78f,warm);
        foreach(float x in new[]{-11.8f,-7.0f,-5.45f,-2.65f,0f,2.65f,5.45f,7f,11.8f})
        {
            float z=Mathf.Abs(x)>7.5f?8.11f:6.0f;
            if(Mathf.Abs(x)>7.5f||Mathf.Abs(x)<6.6f)
                g.CeilingDisc(P(x,3.439f,z),.055f,.084f,gold,white);
            g.CeilingDisc(P(x,3.439f,12.65f),.055f,.084f,gold,white);
        }
        var src=ShowroomAtlasSetup.LoadSourceMesh(Atlas+"/Meshes/Mezzanine Branded.asset");
        if(!src||src.subMeshCount!=1||mezz.sharedMaterials.Length!=1)
            throw new InvalidOperationException("The mezzanine needs its single existing atlas material.");
        var detail=g.Build("Temporary soffit geometry");
        int added=(int)detail.GetIndexCount(0)/3;
        var combined=new Mesh{name="Mezzanine | Optimized Soffit"};
        combined.CombineMeshes(new[]{
            new CombineInstance{mesh=src,transform=Matrix4x4.identity},
            new CombineInstance{mesh=detail,transform=filter.transform.worldToLocalMatrix*room.transform.localToWorldMatrix}
        },true,true,false);
        UnityEngine.Object.DestroyImmediate(detail);
        Unwrapping.GenerateSecondaryUVSet(combined);
        combined.RecalculateBounds();
        string path=Atlas+"/Meshes/Mezzanine Optimized Soffit.asset";
        var previous=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(previous){EditorUtility.CopySerialized(combined,previous);UnityEngine.Object.DestroyImmediate(combined);combined=previous;}
        else AssetDatabase.CreateAsset(combined,path);
        filter.sharedMesh=combined;
        if(existingMaterial!=mezz.sharedMaterial||!textures.SequenceEqual(existingMaterial.GetTexturePropertyNames().Select(existingMaterial.GetTexture))||
           state.Any(q=>q.Key.localToWorldMatrix!=q.Value))throw new InvalidOperationException("Existing material references or placements changed.");
        File.WriteAllText("Showroom Ceiling Verification.txt",$"One mezzanine mesh and one existing material.\nSoffit triangles: 1440 before, {added} after\nPreserved existing transforms: {state.Count}\nNo added textures or materials.\n");
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        Capture(room.transform,front,"Mezzanine Ceiling Preview.png",true);
        Lightmapping.bakeCompleted+=Done;
        if(!Lightmapping.BakeAsync()){Lightmapping.bakeCompleted-=Done;throw new InvalidOperationException("Unity did not start the bake.");}
        void Done()
        {
            Lightmapping.bakeCompleted-=Done;EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            File.AppendAllText("Showroom Ceiling Verification.txt",$"Baked lightmaps: {LightmapSettings.lightmaps.Length}\n");
            Capture(room.transform,front,"Mezzanine Optimized Ceiling Preview.png",true);
            File.AppendAllText("Showroom Ceiling Verification.txt","Final ceiling preview captured.\n");
        }
    }
    static void Capture(Transform root,float front,string name,bool ceiling=false)
    {
        var g=new GameObject("Temporary service wall preview"){hideFlags=HideFlags.HideAndDontSave};
        var c=g.AddComponent<Camera>();c.enabled=false;c.allowHDR=true;c.fieldOfView=58;
        var p=root.TransformPoint(new Vector3(0,2.95f,front*.1f));
        var aim=root.TransformPoint(new Vector3(0,1.97f,-front*13.8f));
        if(ceiling){p=root.TransformPoint(new Vector3(0,1.15f,front*1.65f));aim=root.TransformPoint(new Vector3(0,3.12f,-front*8.1f));c.fieldOfView=67;}
        g.transform.SetPositionAndRotation(p,Quaternion.LookRotation(aim-p,root.up));
        var data=c.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        var rt=new RenderTexture(1440,730,24);var tex=new Texture2D(1440,730,TextureFormat.RGB24,false);
        var old=RenderTexture.active;
        try
        {
            RenderPipeline.SubmitRenderRequest(c,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
            RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1440,730),0,0);tex.Apply();
            File.WriteAllBytes(name,tex.EncodeToPNG());File.AppendAllText(Report,"Preview: "+name+"\n");
        }
        finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);UnityEngine.Object.DestroyImmediate(g);}
    }
}


