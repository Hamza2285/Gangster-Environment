using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class BeachShoreBuilder {
 const string Folder="Assets/Scenes/Oasis/BeachShoreline";
 const string ScenePath="Assets/Scenes/Oasis/Gangstar City.unity";
 const int MaskSize=2048;
 const float Distance=16;
 struct Edge {public int a,b,third,count;}
 class Target {public MeshFilter f;public MeshRenderer r;public Mesh original;public Material[] materials;public Vector3[] world;public List<Edge> high=new List<Edge>();public List<Edge> low=new List<Edge>();public Vector3[] weld;}
 static BeachShoreBuilder(){EditorApplication.delayCall+=Run;}
 static void Run(){
  if(!File.Exists("BeachShoreBuild.request"))return;
  File.Move("BeachShoreBuild.request","BeachShoreBuild.started-"+DateTime.UtcNow.Ticks);
  var log=new StringBuilder();var targets=new List<Target>();bool committed=false;
  bool oldAsync=ShaderUtil.allowAsyncCompilation;
  try {
   ShaderUtil.allowAsyncCompilation=false;
   var scene=SceneManager.GetSceneByPath(ScenePath);
   if(!scene.IsValid()||!scene.isLoaded)throw new Exception("Gangstar City scene is not open.");
   Directory.CreateDirectory("BeachReview");
   string backup="Assets/Editor/BeachBackups/BeforeShoreline-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity";
   Directory.CreateDirectory(Path.GetDirectoryName(backup));
   if(!EditorSceneManager.SaveScene(scene,backup,true))throw new Exception("Scene backup failed.");
   log.AppendLine("Current scene including unsaved edits backed up: "+backup);
   var filters=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshFilter>(true)).Where(f=>f.name=="Canal1"||f.name=="Outer_MEsh").ToArray();
   if(filters.Length!=2)throw new Exception("Expected exactly Canal1 and Outer_MEsh.");
   var bounds=new Bounds(filters[0].GetComponent<Renderer>().bounds.center,Vector3.zero);
   foreach(var f in filters){
    var t=new Target{f=f,r=f.GetComponent<MeshRenderer>(),original=f.sharedMesh,world=f.sharedMesh.vertices.Select(p=>f.transform.TransformPoint(p)).ToArray()};
    t.materials=t.r.sharedMaterials;targets.Add(t);bounds.Encapsulate(t.r.bounds);
    var lookup=new Dictionary<Vector3Int,int>();var welded=new List<Vector3>();var remap=new int[t.world.Length];
    for(int i=0;i<t.world.Length;i++){
     var p=t.world[i];var key=new Vector3Int(Mathf.RoundToInt(p.x*1000),Mathf.RoundToInt(p.y*1000),Mathf.RoundToInt(p.z*1000));
     if(!lookup.TryGetValue(key,out int index)){index=welded.Count;lookup.Add(key,index);welded.Add(p);}remap[i]=index;
    }
    t.weld=welded.ToArray();var edges=new Dictionary<long,Edge>();var tris=t.original.triangles;
    for(int i=0;i<tris.Length;i+=3)for(int k=0;k<3;k++){
     int a=remap[tris[i+k]],b=remap[tris[i+(k+1)%3]],third=remap[tris[i+(k+2)%3]];
     long key=((long)Math.Min(a,b)<<32)|(uint)Math.Max(a,b);
     if(edges.TryGetValue(key,out var edge)){edge.count++;edges[key]=edge;}else edges.Add(key,new Edge{a=a,b=b,third=third,count=1});
    }
    foreach(var e in edges.Values.Where(e=>e.count==1)){
     var a=t.weld[e.a];var b=t.weld[e.b];
     if(new Vector2(a.x-b.x,a.z-b.z).sqrMagnitude<0.0625f)continue;
     if(a.y>26.9f&&a.y<28.1f&&b.y>26.9f&&b.y<28.1f)t.high.Add(e);
     float waterEdge=f.name=="Canal1"?-21.5f:1.1f;
     if(a.y<waterEdge&&b.y<waterEdge)t.low.Add(e);
    }
    if(t.high.Count<5 || t.low.Count<5)throw new Exception("Shore edges could not be identified: "+f.name);
    log.AppendLine(f.name+": "+t.high.Count+" land-side edge segments; "+t.low.Count+" water-side segments.");
   }
   bounds.Expand(new Vector3(4,0,4));
   var min=new Vector2(bounds.min.x,bounds.min.z);var size=new Vector2(bounds.size.x,bounds.size.z);
   var dry=Enumerable.Repeat(Distance,MaskSize*MaskSize).ToArray();var wet=(float[])dry.Clone();
   foreach(var t in targets){foreach(var e in t.high)PaintDistance(dry,t.weld[e.a],t.weld[e.b],min,size);foreach(var e in t.low)PaintDistance(wet,t.weld[e.a],t.weld[e.b],min,size);}
   var tex=new Texture2D(MaskSize,MaskSize,TextureFormat.RGBA32,false,true);var pixels=new Color32[dry.Length];
   for(int i=0;i<pixels.Length;i++)pixels[i]=new Color32((byte)Mathf.RoundToInt(dry[i]/Distance*255),(byte)Mathf.RoundToInt(wet[i]/Distance*255),0,255);
   tex.SetPixels32(pixels);tex.Apply();string maskPath=Folder+"/Textures/Shore Shape.png";
   File.WriteAllBytes(maskPath,tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);
   AssetDatabase.Refresh();
   Configure(Folder+"/Textures/Promenade Tiles.jpg",true,1024,TextureWrapMode.Repeat);
   Configure(Folder+"/Textures/Beach Sand.png",true,1024,TextureWrapMode.Repeat);
   Configure(maskPath,false,MaskSize,TextureWrapMode.Clamp);
   var shader=AssetDatabase.LoadAssetAtPath<Shader>(Folder+"/Shaders/Beach Shoreline.shader");
   if(!shader||!shader.isSupported)throw new Exception("Shore shader is unsupported.");
   string materialPath=Folder+"/Beach Shoreline.mat";
   var mat=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
   if(!mat){mat=new Material(shader){name="Beach Shoreline"};AssetDatabase.CreateAsset(mat,materialPath);}
   mat.shader=shader;mat.shaderKeywords=new string[0];mat.enableInstancing=true;mat.renderQueue=2000;
   mat.SetTexture("_TileMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/Promenade Tiles.jpg"));
   mat.SetTexture("_SandMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/Beach Sand.png"));
   mat.SetTexture("_ShoreMask",AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath));
   mat.SetVector("_ShoreBounds",new Vector4(min.x,min.y,1/size.x,1/size.y));mat.SetFloat("_MaskDistance",Distance);
   mat.SetTextureScale("_BaseMap",Vector2.one);mat.SetTextureOffset("_BaseMap",Vector2.zero);
   mat.SetTextureScale("_TileMap",new Vector2(0.25f,0.25f));
   mat.SetTextureScale("_SandMap",new Vector2(0.16f,0.16f));
   var poses=new List<Tuple<Vector3,Quaternion>>();
   foreach(var t in targets){
    var choices=t.high.Where(e=>(t.weld[e.a]-t.weld[e.b]).sqrMagnitude>100).OrderByDescending(e=>(t.weld[e.a]-t.weld[e.b]).sqrMagnitude).ToArray();
    foreach(var e in choices.Take(2)){
     var mid=(t.weld[e.a]+t.weld[e.b])*0.5f;
     var inland=t.weld[e.third]-mid;inland.y=0;inland.Normalize();
     var look=mid+inland*12;var position=mid+inland*35+Vector3.up*24;
     poses.Add(Tuple.Create(position,Quaternion.LookRotation(look-position,Vector3.up)));
    }
   }
   for(int i=0;i<poses.Count;i++)Capture(poses[i],"Before-"+i);
   foreach(var t in targets){
    var clone=UnityEngine.Object.Instantiate(t.original);clone.name=t.f.name+"_BeachUV";
    clone.uv=t.world.Select(p=>new Vector2(p.x,p.z)).ToArray();
    string path=Folder+"/"+t.f.name+" Beach Mesh.asset";
    if(AssetDatabase.LoadAssetAtPath<Mesh>(path))throw new Exception("Shore mesh already exists; inspect it before rebuilding.");
    AssetDatabase.CreateAsset(clone,path);t.f.sharedMesh=clone;t.r.sharedMaterials=new[]{mat};
    PrefabUtility.RecordPrefabInstancePropertyModifications(t.f);PrefabUtility.RecordPrefabInstancePropertyModifications(t.r);
    if(!clone.vertices.SequenceEqual(t.original.vertices)||!clone.uv2.SequenceEqual(t.original.uv2)||!clone.triangles.SequenceEqual(t.original.triangles))
     throw new Exception("Geometry or lightmap UVs changed.");
   }
   AssetDatabase.SaveAssets();
   for(int i=0;i<poses.Count;i++)Capture(poses[i],"After-"+i);
   var errors=ShaderUtil.GetShaderMessages(shader).Where(m=>m.severity.ToString()=="Error").ToArray();
   if(errors.Length>0)throw new Exception(string.Join("\n",errors.Select(m=>m.message)));
   EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new Exception("Scene save failed.");
   committed=true;Selection.activeObject=mat;
   log.AppendLine("SUCCESS: Canal1 and Outer_MEsh use one editable URP beach material.");
   log.AppendLine("3 metre tiled edge, 1.5 metre blend into dry sand, darker wet sand along lower edges.");
   log.AppendLine("Three texture samples; no normal map, transparency, extra geometry or runtime scripts.");
   log.AppendLine("Android ASTC 4x4 configured. Mesh positions, triangles, normals, UV2 and colliders preserved.");
   File.WriteAllText("BeachReview/Views.json",JsonUtility.ToJson(new ViewData{positions=poses.Select(p=>p.Item1).ToArray(),rotations=poses.Select(p=>p.Item2).ToArray()}));
  }catch(Exception e){
   if(!committed)foreach(var t in targets){t.f.sharedMesh=t.original;t.r.sharedMaterials=t.materials;PrefabUtility.RecordPrefabInstancePropertyModifications(t.f);PrefabUtility.RecordPrefabInstancePropertyModifications(t.r);}
   log.AppendLine("FAILED: "+e);Debug.LogException(e);
  }finally{ShaderUtil.allowAsyncCompilation=oldAsync;File.WriteAllText("BeachShoreReport.txt",log.ToString());}
 }
 [Serializable]class ViewData{public Vector3[] positions;public Quaternion[] rotations;}
 static void PaintDistance(float[] field,Vector3 a3,Vector3 b3,Vector2 min,Vector2 size){
  var a=new Vector2(a3.x,a3.z);var b=new Vector2(b3.x,b3.z);var ab=b-a;float denominator=ab.sqrMagnitude;
  int x0=Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.x,b.x)-Distance-min.x)/size.x*MaskSize),0,MaskSize-1);
  int x1=Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(a.x,b.x)+Distance-min.x)/size.x*MaskSize),0,MaskSize-1);
  int y0=Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.y,b.y)-Distance-min.y)/size.y*MaskSize),0,MaskSize-1);
  int y1=Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(a.y,b.y)+Distance-min.y)/size.y*MaskSize),0,MaskSize-1);
  for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++){
   var p=min+new Vector2((x+0.5f)/MaskSize*size.x,(y+0.5f)/MaskSize*size.y);
   float f=Mathf.Clamp01(Vector2.Dot(p-a,ab)/denominator);
   float d=(p-a-ab*f).magnitude;int index=y*MaskSize+x;if(d<field[index])field[index]=d;
  }
 }
 static void Configure(string path,bool srgb,int size,TextureWrapMode wrap){
  var importer=(TextureImporter)AssetImporter.GetAtPath(path);
  importer.sRGBTexture=srgb;importer.mipmapEnabled=true;importer.isReadable=false;importer.wrapMode=wrap;
  importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=4;importer.alphaSource=TextureImporterAlphaSource.None;
  importer.maxTextureSize=size;importer.textureCompression=TextureImporterCompression.CompressedHQ;
  importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings{name="Android",overridden=true,maxTextureSize=size,format=TextureImporterFormat.ASTC_4x4,compressionQuality=100});
  importer.SaveAndReimport();
 }
 static void Capture(Tuple<Vector3,Quaternion> pose,string name){
  var go=new GameObject("Beach review camera"){hideFlags=HideFlags.HideAndDontSave};
  var camera=go.AddComponent<Camera>();camera.enabled=false;camera.fieldOfView=60;camera.farClipPlane=2500;camera.nearClipPlane=0.1f;camera.aspect=1.6f;
  camera.clearFlags=CameraClearFlags.Skybox;camera.transform.SetPositionAndRotation(pose.Item1,pose.Item2);
  var data=go.AddComponent<UniversalAdditionalCameraData>();data.renderPostProcessing=false;
  var rt=RenderTexture.GetTemporary(1280,800,24,RenderTextureFormat.ARGB32);var old=RenderTexture.active;
  try{
   camera.targetTexture=rt;camera.Render();camera.Render();RenderTexture.active=rt;
   var image=new Texture2D(1280,800,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,1280,800),0,0);image.Apply();
   File.WriteAllBytes("BeachReview/"+name+".png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
  }finally{camera.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(go);}
 }
}

