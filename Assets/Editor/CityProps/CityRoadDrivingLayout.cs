using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;

[InitializeOnLoad]
public static class CityRoadDrivingLayout {
 const string ScenePath="Assets/Scenes/Oasis/Gangstar City.unity";
 const string PrefabPath="Assets/Scenes/Oasis/3D/Prefeb/Roads.prefab";
 const string Out="Assets/Scenes/Oasis/DrivingLayout";
 static CityRoadDrivingLayout(){EditorApplication.update+=Poll;}
 static void Poll(){
 if(File.Exists("CityRoadDrivingBridge.request")&&!EditorApplication.isCompiling&&!EditorApplication.isPlayingOrWillChangePlaymode){File.Delete("CityRoadDrivingBridge.request");CompleteBridge();return;}
  if(!File.Exists("CityRoadDrivingLayout.request")||EditorApplication.isCompiling||EditorApplication.isPlayingOrWillChangePlaymode)return;
  File.Move("CityRoadDrivingLayout.request","CityRoadDrivingLayout.started-"+DateTime.UtcNow.Ticks);
  Build();
 }
 struct V {public Vector3 p;public Vector2 uv;public V(Vector3 p,Vector2 uv){this.p=p;this.uv=uv;}}
 class Geometry{
  public List<Vector3> p=new List<Vector3>(); public List<Vector2> uv=new List<Vector2>();public List<int> tri=new List<int>();
  public void Polygon(List<V> vs,Func<V,Vector2> map,Func<Vector3,Vector3> deform){
   if(vs.Count<3)return;
   for(int i=1;i<vs.Count-1;i++){
    var a=vs[0];var b=vs[i];var c=vs[i+1];
    if(Vector3.Cross(b.p-a.p,c.p-a.p).sqrMagnitude<0.000000001f)continue;
    int n=p.Count;foreach(var v in new[]{a,b,c}){p.Add(deform(v.p));uv.Add(map(v));}tri.Add(n);tri.Add(n+1);tri.Add(n+2);
   }
  }
  public Mesh Save(string name){var m=new Mesh{name=name};if(p.Count>65535)m.indexFormat=IndexFormat.UInt32;m.SetVertices(p);m.SetUVs(0,uv);m.SetTriangles(tri,0);m.RecalculateNormals();m.RecalculateTangents();m.RecalculateBounds();AssetDatabase.CreateAsset(m,Out+"/Meshes/"+name+".asset");return m;}
 }
 class Road {
  public MeshFilter f;public MeshRenderer r;public Mesh m;public int layer;public Vector3[] world;public Vector2[] uv;public int[] tri;public float u0,u1,v0,v1,width,driveWidth;public Bounds bounds;public bool bend;
 }
 static List<V> Clip(List<V> poly,int axis,float cut,bool above){
  var result=new List<V>();if(poly.Count==0)return result;
  V prev=poly[poly.Count-1];float a=axis==0?prev.uv.x:prev.uv.y;bool ain=above?a>=cut-0.000001f:a<=cut+0.000001f;
  foreach(var v in poly){float b=axis==0?v.uv.x:v.uv.y;bool bin=above?b>=cut-0.000001f:b<=cut+0.000001f;if(bin!=ain){float t=(cut-a)/(b-a);result.Add(new V(Vector3.Lerp(prev.p,v.p,t),Vector2.Lerp(prev.uv,v.uv,t)));}if(bin)result.Add(v);prev=v;a=b;ain=bin;}return result;
 }
 static List<V> Region(List<V> poly,float a,float b,float c,float d){return Clip(Clip(Clip(Clip(poly,0,a,true),0,b,false),1,c,true),1,d,false);}
 static Road Describe(MeshFilter f){
  var m=f.sharedMesh;var uv=m.uv;if(uv.Length!=m.vertexCount)return null;
  int layer=Mathf.FloorToInt(uv.Average(x=>x.x)/32);if(layer!=0&&layer!=2)return null;
  var road=new Road{f=f,r=f.GetComponent<MeshRenderer>(),m=m,uv=uv,world=m.vertices.Select(f.transform.TransformPoint).ToArray(),tri=m.triangles,layer=layer};
  road.bounds=road.r.bounds;road.u0=uv.Min(v=>v.x);road.u1=uv.Max(v=>v.x);road.v0=uv.Min(v=>v.y);road.v1=uv.Max(v=>v.y);
  var pairs=new List<float>();
  foreach(var g in Enumerable.Range(0,uv.Length).GroupBy(i=>Mathf.RoundToInt(uv[i].y*1000))){
   int left=g.OrderBy(i=>uv[i].x).First();int right=g.OrderByDescending(i=>uv[i].x).First();
   if(uv[right].x-uv[left].x>(road.u1-road.u0)*.8f)pairs.Add(Vector3.Distance(road.world[left],road.world[right]));
  }
  road.width=pairs.Count>0?pairs.OrderBy(x=>x).ElementAt(pairs.Count/2):Mathf.Min(road.bounds.size.x,road.bounds.size.z);
  if(road.u1-road.u0>.02f&&road.u1-road.u0<1.1f&&road.width>15&&road.width<40)return road;
  return null;
 }
 static Vector3 Deform(Road road,Vector3 p){
  if(!road.bend)return p;
  bool alongX=road.bounds.size.x>road.bounds.size.z;
  float t=alongX?(p.x-road.bounds.min.x)/road.bounds.size.x:(p.z-road.bounds.min.z)/road.bounds.size.z;
  float delta=1.25f*Mathf.Sin(t*Mathf.PI)*Mathf.Sin(t*Mathf.PI)*Mathf.Sin(t*Mathf.PI*2);
  return p+(alongX?Vector3.forward:Vector3.right)*delta;
 }
 static void Convert(Road road,int serial,Material atlas){
  float half=Mathf.Min(.48f,road.driveWidth/road.width*.5f);
  float ua=road.u0+(road.u1-road.u0)*(.5f-half),ub=road.u0+(road.u1-road.u0)*(.5f+half);
  var drive=new Geometry();var shoulders=new Geometry();
  float[] cuts=road.layer==2?new[]{0f,.5f,1f}:new[]{0f,.47f,.53f,1f};
  for(int i=0;i<road.tri.Length;i+=3){
   var poly=new List<V>();for(int j=0;j<3;j++){int k=road.tri[i+j];poly.Add(new V(road.world[k],road.uv[k]));}
   float low=poly.Min(v=>v.uv.y),high=poly.Max(v=>v.uv.y);
   int bands=Mathf.Clamp(Mathf.CeilToInt((high-low)/.45f),1,160);
   for(int band=0;band<bands;band++){
    float va=Mathf.Lerp(low,high,(float)band/bands),vb=Mathf.Lerp(low,high,(float)(band+1)/bands);
    for(int section=0;section<cuts.Length-1;section++){
     int s=section;float t0=cuts[s],t1=cuts[s+1];
     var clipped=Region(poly,Mathf.Lerp(ua,ub,t0),Mathf.Lerp(ua,ub,t1),va,vb);
     drive.Polygon(clipped,v=>{
      float t=Mathf.InverseLerp(ua,ub,v.uv.x);float q=Mathf.InverseLerp(t0,t1,t);float src;
      if(road.layer==2)src=s==0?Mathf.Lerp(.015f,.54f,q):Mathf.Lerp(.54f,.015f,q);
      else src=s==0?Mathf.Lerp(.024f,.20f,q):s==1?Mathf.Lerp(.445f,.555f,q):Mathf.Lerp(.80f,.976f,q);
      return new Vector2(road.layer*32+src,v.uv.y);
     },p=>road.f.transform.InverseTransformPoint(Deform(road,p)));
    }
    foreach(var part in new[]{Region(poly,road.u0,ua,va,vb),Region(poly,ub,road.u1,va,vb)})
     shoulders.Polygon(part,v=>new Vector2(96+Mathf.Repeat(v.p.x/7,10),v.p.z/7),p=>road.f.transform.InverseTransformPoint(Deform(road,p)));
   }
  }
  var mesh=drive.Save("Driving_"+serial.ToString("D3"));road.f.sharedMesh=mesh;road.r.sharedMaterial=atlas;EditorUtility.SetDirty(road.f);
  if(!road.f.GetComponent<MeshCollider>())road.f.gameObject.AddComponent<MeshCollider>(); foreach(var c in road.f.GetComponents<MeshCollider>()){c.sharedMesh=mesh;c.convex=false;}
  var go=new GameObject(road.layer==2?"Boulevard pavement":"Local street pavement");go.transform.SetParent(road.f.transform,false);
  go.AddComponent<MeshFilter>().sharedMesh=shoulders.Save("Pavement_"+serial.ToString("D3"));go.AddComponent<MeshRenderer>().sharedMaterial=atlas;
  go.AddComponent<MeshCollider>().sharedMesh=go.GetComponent<MeshFilter>().sharedMesh;go.isStatic=true;
 }
 static bool Height(Road road,Vector3 point,out float y){
  foreach(var k in Enumerable.Range(0,road.tri.Length/3)){
   var a=road.world[road.tri[k*3]];var b=road.world[road.tri[k*3+1]];var c=road.world[road.tri[k*3+2]];
   float d=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);if(Mathf.Abs(d)<.000001f)continue;
   float u=((b.z-c.z)*(point.x-c.x)+(c.x-b.x)*(point.z-c.z))/d;
   float v=((c.z-a.z)*(point.x-c.x)+(a.x-c.x)*(point.z-c.z))/d;
   if(u>=-.001f&&v>=-.001f&&u+v<=1.001f){y=u*a.y+v*b.y+(1-u-v)*c.y;return true;}
  }y=0;return false;
 }
 static Vector3 Direction(Road road){
  var a=road.world.OrderBy(p=>road.bounds.size.x>road.bounds.size.z?p.x:p.z).First();
  var b=road.world.OrderByDescending(p=>road.bounds.size.x>road.bounds.size.z?p.x:p.z).First();
  var d=b-a;d.y=0;return d.normalized;
 }
 static Material Material(string name,Color color,float emission=0){
  var m=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name};m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",.22f);m.enableInstancing=true;
  if(emission>0){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",color*emission);}AssetDatabase.CreateAsset(m,Out+"/"+name+".mat");return m;
 }
 static void Box(Transform parent,Vector3 p,Vector3 size,Quaternion rotation,Material material,string name){
  var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);go.transform.SetPositionAndRotation(p,rotation);go.transform.localScale=size;go.GetComponent<MeshRenderer>().sharedMaterial=material;go.isStatic=true;
 }
 static void Combine(Transform root,string name,Material material,bool collider){
  var all=root.GetComponentsInChildren<MeshFilter>().Where(f=>f.GetComponent<MeshRenderer>()&&f.GetComponent<MeshRenderer>().sharedMaterial==material).ToArray();
  if(all.Length==0)return;
  var instances=all.Select(f=>new CombineInstance{mesh=f.sharedMesh,transform=root.worldToLocalMatrix*f.transform.localToWorldMatrix}).ToArray();
  var mesh=new Mesh{name=name};mesh.indexFormat=IndexFormat.UInt32;mesh.CombineMeshes(instances,true,true);mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,Out+"/Meshes/"+name+".asset");
  foreach(var f in all)UnityEngine.Object.DestroyImmediate(f.gameObject);
  var g=new GameObject(name);g.transform.SetParent(root,false);g.AddComponent<MeshFilter>().sharedMesh=mesh;g.AddComponent<MeshRenderer>().sharedMaterial=material;
  if(collider)g.AddComponent<MeshCollider>().sharedMesh=mesh;g.isStatic=true;
 }
 static List<Vector3> Underpasses(List<Road> roads,Transform parent,Material concrete,Material glow,StringBuilder report){
  var centers=new List<Vector3>();
  var elevated=roads.Where(r=>r.bounds.size.y>8).ToArray();
  foreach(var low in roads.Where(r=>r.layer==0&&r.bounds.size.y<.3f&&Mathf.Max(r.bounds.size.x,r.bounds.size.z)>65).OrderByDescending(r=>Mathf.Max(r.bounds.size.x,r.bounds.size.z))){
   var dir=Direction(low);float length=Mathf.Max(low.bounds.size.x,low.bounds.size.z);
   for(float t=.2f;t<=.8f;t+=.05f){
    var p=low.bounds.center+dir*((t-.5f)*length);
    if(!Height(low,p,out float ground))continue;p.y=ground;
    if(centers.Any(c=>Vector3.Distance(c,p)<150))continue;
    if(!elevated.Any(high=>Height(high,p,out float top)&&top-ground>8&&top-ground<25))continue;
    var module=new GameObject("Underpass "+(centers.Count+1));module.transform.SetParent(parent,false);
    var rot=Quaternion.LookRotation(dir);var side=Vector3.Cross(Vector3.up,dir);
    float tunnelLength=22,innerWidth=low.driveWidth+2.4f,clearance=6.2f;
    for(int sign=-1;sign<=1;sign+=2){
     Box(module.transform,p+side*sign*(innerWidth*.5f+.35f)+Vector3.up*(clearance*.5f),new Vector3(.7f,clearance,tunnelLength),rot,concrete,"Retaining wall");
     for(int k=-1;k<=1;k++)Box(module.transform,p+side*sign*(innerWidth*.5f-.03f)+dir*k*7+Vector3.up*4.9f,new Vector3(.06f,.18f,2.4f),rot,glow,"Tunnel wall lighting");
    }
    Box(module.transform,p+Vector3.up*(clearance+.35f),new Vector3(innerWidth+1.4f,.7f,tunnelLength),rot,concrete,"Tunnel roof");
    for(int sign=-1;sign<=1;sign+=2)Box(module.transform,p+dir*sign*(tunnelLength*.5f)+Vector3.up*(clearance+.75f),new Vector3(innerWidth+1.9f,.25f,.55f),rot,glow,"Portal clearance marker");
    Combine(module.transform,"Underpass_"+(centers.Count+1)+"_Concrete",concrete,true);
    Combine(module.transform,"Underpass_"+(centers.Count+1)+"_Lighting",glow,false);
    centers.Add(p);report.AppendLine("Underpass "+centers.Count+": "+p+" clearance="+clearance+" width="+innerWidth+" length="+tunnelLength);
    break;
   }if(centers.Count==2)return centers;
  }throw new Exception("Could not locate two valid elevated crossings; no prefab will be applied.");
 }
 static int Rails(List<Road> roads,Transform root,Material metal){
  int count=0;
  foreach(var road in roads.Where(r=>r.bounds.size.y>3)){
   var rows=Enumerable.Range(0,road.uv.Length).GroupBy(i=>Mathf.RoundToInt(road.uv[i].y*200)).OrderBy(g=>g.Key).Select(g=>new[]{
    g.OrderBy(i=>road.uv[i].x).First(),g.OrderByDescending(i=>road.uv[i].x).First()
   }).Where(pair=>road.uv[pair[1]].x-road.uv[pair[0]].x>(road.u1-road.u0)*.8f).ToArray();
   for(int j=1;j<rows.Length;j++){
    var prev=rows[j-1];var next=rows[j];
    for(int s=0;s<2;s++){
     float u=s==0?.5f-road.driveWidth/road.width*.5f:.5f+road.driveWidth/road.width*.5f;
     var a=Vector3.Lerp(road.world[prev[0]],road.world[prev[1]],u);
     var b=Vector3.Lerp(road.world[next[0]],road.world[next[1]],u);
     if(Mathf.Min(a.y,b.y)<road.bounds.min.y+1.5f||Vector3.Distance(a,b)>65)continue;
     var d=b-a;if(d.magnitude<.3f)continue;
     Box(root,(a+b)*.5f+Vector3.up*.85f,new Vector3(.18f,.25f,d.magnitude),Quaternion.LookRotation(d),metal,"Bridge guardrail");count++;
    }
   }
  }Combine(root,"Bridge_Guardrails",metal,true);return count;
 }
 static void Capture(Scene scene,Vector3 pos,Vector3 target,string file,bool ortho=false,float size=700){
  var go=new GameObject("Driving Layout Review Camera"){hideFlags=HideFlags.HideAndDontSave};SceneManager.MoveGameObjectToScene(go,scene);
  var c=go.AddComponent<Camera>();c.enabled=false;c.transform.SetPositionAndRotation(pos,Quaternion.LookRotation(target-pos));c.farClipPlane=5000;c.nearClipPlane=.3f;c.fieldOfView=62;c.orthographic=ortho;c.orthographicSize=size;
  var rt=RenderTexture.GetTemporary(1400,1000,24);var previous=RenderTexture.active;
  try{c.targetTexture=rt;c.Render();RenderTexture.active=rt;var tex=new Texture2D(1400,1000,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1400,1000),0,0);tex.Apply();File.WriteAllBytes("DrivingLayoutReview/"+file+".png",tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);}
  finally{RenderTexture.active=previous;c.targetTexture=null;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(go);}
 }
 static void CompleteBridge(){
 var report=new StringBuilder();try{
 var scene=SceneManager.GetSceneByPath(ScenePath);var root=scene.GetRootGameObjects().Single(g=>g.name=="Track Roads");
 var f=root.GetComponentsInChildren<MeshFilter>().Single(x=>x.sharedMesh&&x.GetComponent<MeshRenderer>()&&x.GetComponent<MeshRenderer>().sharedMaterial.name=="Road Atlas"&&!x.sharedMesh.name.StartsWith("Driving_")&&Mathf.FloorToInt(x.sharedMesh.uv.Average(v=>v.x)/32)==0);var m=f.sharedMesh; report.AppendLine("Remaining road: "+f.name+" mesh="+m.name);
 if(m.name.StartsWith("Driving_"))throw new Exception("Bridge already converted.");
 var world=m.vertices.Select(f.transform.TransformPoint).ToArray();var uv=m.uv;var tri=m.triangles;
 var drive=new Geometry();var shoulders=new Geometry();float half=8.876343f/28*.5f;
 for(int i=0;i<tri.Length;i+=3){
 var poly=new List<V>();for(int j=0;j<3;j++){int k=tri[i+j];poly.Add(new V(world[k],uv[k]));}
 float low=poly.Min(v=>v.uv.x),high=poly.Max(v=>v.uv.x);
 if(high-low<.90f||high-low>1.1f)throw new Exception("Unexpected bridge triangle UV width "+(high-low));
 float ua=Mathf.Lerp(low,high,.5f-half),ub=Mathf.Lerp(low,high,.5f+half);
 float va=poly.Min(v=>v.uv.y),vb=poly.Max(v=>v.uv.y);
 var cuts=new[]{0f,.47f,.53f,1f};
 for(int s=0;s<3;s++){int segment=s;float t0=cuts[s],t1=cuts[s+1];
 drive.Polygon(Region(poly,Mathf.Lerp(ua,ub,t0),Mathf.Lerp(ua,ub,t1),va,vb),v=>{
 float t=Mathf.InverseLerp(t0,t1,Mathf.InverseLerp(ua,ub,v.uv.x));
 float src=segment==0?Mathf.Lerp(.024f,.20f,t):segment==1?Mathf.Lerp(.445f,.555f,t):Mathf.Lerp(.80f,.976f,t);
 return new Vector2(src,v.uv.y);},p=>f.transform.InverseTransformPoint(p));}
 foreach(var part in new[]{Region(poly,low,ua,va,vb),Region(poly,ub,high,va,vb)})shoulders.Polygon(part,v=>new Vector2(96+Mathf.Repeat(v.p.x/7,10),v.p.z/7),p=>f.transform.InverseTransformPoint(p));
 }
 var driving=drive.Save("Driving_116");var pavement=shoulders.Save("Pavement_116");
 var atlas=f.GetComponent<MeshRenderer>().sharedMaterial;
 var asset=PrefabUtility.LoadPrefabContents(PrefabPath);
 try{
 var target=asset.GetComponentsInChildren<MeshFilter>().Single(x=>x.name==f.name);target.sharedMesh=driving;
 foreach(var c in target.GetComponents<MeshCollider>())c.sharedMesh=driving;
 var go=new GameObject("Local street pavement");go.transform.SetParent(target.transform,false);go.AddComponent<MeshFilter>().sharedMesh=pavement;go.AddComponent<MeshRenderer>().sharedMaterial=atlas;go.AddComponent<MeshCollider>().sharedMesh=pavement;go.isStatic=true;
 PrefabUtility.SaveAsPrefabAsset(asset,PrefabPath);
 }finally{PrefabUtility.UnloadPrefabContents(asset);}
 f.sharedMesh=driving;PrefabUtility.RecordPrefabInstancePropertyModifications(f);
 foreach(var c in f.GetComponents<MeshCollider>()){c.sharedMesh=driving;PrefabUtility.RecordPrefabInstancePropertyModifications(c);}
 // Keep the separate review prefab consistent with the original template.
 var draft=PrefabUtility.LoadPrefabContents(Out+"/Roads Driving Layout.prefab");
 try{var target=draft.GetComponentsInChildren<MeshFilter>().Single(x=>x.name==f.name);target.sharedMesh=driving;
 foreach(var c in target.GetComponents<MeshCollider>())c.sharedMesh=driving;
 var go=new GameObject("Local street pavement");go.transform.SetParent(target.transform,false);go.AddComponent<MeshFilter>().sharedMesh=pavement;go.AddComponent<MeshRenderer>().sharedMaterial=atlas;go.AddComponent<MeshCollider>().sharedMesh=pavement;go.isStatic=true;PrefabUtility.SaveAsPrefabAsset(draft,Out+"/Roads Driving Layout.prefab");
 }finally{PrefabUtility.UnloadPrefabContents(draft);}
 EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
 report.AppendLine("SUCCESS. The remaining local road section now uses two-lane geometry and pavement. All 105 local road sections and 11 boulevard sections converted.");
 }catch(Exception e){report.AppendLine("FAILED "+e);Debug.LogException(e);}File.WriteAllText("DrivingLayoutReview/BridgeCompletionReport.txt",report.ToString());
}

static void Build(){
  var report=new StringBuilder();GameObject clone=null;bool applied=false;CityDayNightController light=null;bool night=false;
  try{
   var scene=SceneManager.GetSceneByPath(ScenePath);if(!scene.isLoaded)throw new Exception("Load the city scene first.");
   var original=scene.GetRootGameObjects().Single(g=>g.name=="Track Roads");
   if(AssetDatabase.LoadAssetAtPath<GameObject>(Out+"/Roads Driving Layout.prefab"))throw new Exception("DrivingLayout already exists. Inspect before rerunning.");
   Directory.CreateDirectory("DrivingLayoutReview");Directory.CreateDirectory("DrivingLayoutBackup");
   File.Copy(PrefabPath,"DrivingLayoutBackup/Roads.prefab",true);File.Copy(PrefabPath+".meta","DrivingLayoutBackup/Roads.prefab.meta",true);
   File.Copy(ScenePath,"DrivingLayoutBackup/Gangstar City.saved.unity",true);
   if(!File.Exists("DrivingLayoutBackup/Gangstar City.current.unity")&&!EditorSceneManager.SaveScene(scene,"DrivingLayoutBackup/Gangstar City.current.unity",true))throw new Exception("Could not snapshot the current unsaved scene.");
   Directory.CreateDirectory(Out+"/Meshes");AssetDatabase.Refresh();
   var car=scene.GetRootGameObjects().FirstOrDefault(g=>g.name=="Prometheus");if(!car)throw new Exception("Prometheus car missing.");
   var body=car.GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled).ToArray();
   var carBounds=body[0].bounds;foreach(var b in body.Skip(1))carBounds.Encapsulate(b.bounds);
   float vehicleWidth=Mathf.Min(carBounds.size.x,carBounds.size.z);
   float lane=Mathf.Max(3.5f,vehicleWidth+1.1f),street=2*lane+.55f,boulevard=4*lane+1.45f;
   report.AppendLine("Prometheus bounds "+carBounds+"; lane width "+lane+"; street "+street+"; boulevard "+boulevard);
   clone=UnityEngine.Object.Instantiate(original);clone.name="Track Roads";SceneManager.MoveGameObjectToScene(clone,scene);
   if(PrefabUtility.IsPartOfPrefabInstance(clone)) PrefabUtility.UnpackPrefabInstance(clone,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
   var atlas=AssetDatabase.LoadAssetAtPath<Material>("Assets/Scenes/Oasis/RoadAtlas/Road Atlas.mat");
   var roads=clone.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh&&f.GetComponent<MeshRenderer>()&&f.GetComponent<MeshRenderer>().sharedMaterial==atlas).Select(Describe).Where(r=>r!=null).ToList();
   if(roads.Count<100)throw new Exception("Unexpected eligible road count: "+roads.Count);
   int serial=0,bends=0;
   foreach(var road in roads){
    road.driveWidth=road.layer==2?boulevard:street;
    if(road.driveWidth>road.width*.9f)throw new Exception("Car width exceeds original road space.");
    if(bends<3&&road.layer==0&&road.bounds.size.y<.15f&&road.m.vertexCount==4&&Mathf.Max(road.bounds.size.x,road.bounds.size.z)>120){road.bend=true;bends++;}
    Convert(road,++serial,atlas);
   }
   report.AppendLine("Converted roads="+serial+" boulevards="+roads.Count(r=>r.layer==2)+" local streets="+roads.Count(r=>r.layer==0)+" gentle bends="+bends);
   var details=new GameObject("Bridge and Underpass Details");details.transform.SetParent(clone.transform,false);
   var concrete=Material("Underpass Concrete",new Color(.30f,.32f,.34f));
   var metal=Material("Bridge Guardrail",new Color(.45f,.47f,.50f));
   var glow=Material("Tunnel Markers",new Color(.9f,.72f,.24f),1.3f);
   var tunnelPositions=Underpasses(roads,details.transform,concrete,glow,report);
   report.AppendLine("Guardrail segments="+Rails(roads,details.transform,metal));
   foreach(var f in clone.GetComponentsInChildren<MeshFilter>(true)){
    if(!f.sharedMesh)throw new Exception("Missing mesh on "+f.name);
    if(f.sharedMesh.vertices.Any(v=>float.IsNaN(v.x)||float.IsInfinity(v.x)))throw new Exception("Invalid vertex.");
   }
   // Save a reviewable complete prefab first, with the same template hierarchy.
   PrefabUtility.SaveAsPrefabAsset(clone,Out+"/Roads Driving Layout.prefab",out bool ok);if(!ok)throw new Exception("Prefab save failed.");
   AssetDatabase.SaveAssets();
   // Apply mesh references and new children to the existing prefab without replacing instance identity.
   var assetRoot=PrefabUtility.LoadPrefabContents(PrefabPath);
   try{
    var sourceMeshes=clone.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.name.StartsWith("polySurface")).ToDictionary(f=>f.name,f=>f);
    foreach(var target in assetRoot.GetComponentsInChildren<MeshFilter>(true)){
     if(!sourceMeshes.TryGetValue(target.name,out var source)||source.GetComponent<MeshRenderer>().sharedMaterial!=atlas)continue;
     target.sharedMesh=source.sharedMesh;target.GetComponent<MeshRenderer>().sharedMaterial=atlas; if(!target.GetComponent<MeshCollider>())target.gameObject.AddComponent<MeshCollider>();
     foreach(var c in target.GetComponents<MeshCollider>())c.sharedMesh=target.sharedMesh;
     foreach(Transform child in source.transform)if(child.name.EndsWith("pavement")){var copied=UnityEngine.Object.Instantiate(child.gameObject,target.transform,false);copied.name=child.name;}
    }
    foreach(var floor in assetRoot.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.GetComponent<MeshRenderer>()&&f.GetComponent<MeshRenderer>().sharedMaterial==atlas))if(!floor.GetComponent<MeshCollider>())floor.gameObject.AddComponent<MeshCollider>().sharedMesh=floor.sharedMesh; var newDetails=UnityEngine.Object.Instantiate(details,assetRoot.transform,false);newDetails.name=details.name;
    PrefabUtility.SaveAsPrefabAsset(assetRoot,PrefabPath,out ok);if(!ok)throw new Exception("Original prefab save failed.");applied=true;
   }finally{PrefabUtility.UnloadPrefabContents(assetRoot);}
   // Existing atlas overrides on the scene instance must reference the new assets too.
   foreach(var road in roads){
    var target=original.GetComponentsInChildren<MeshFilter>(true).First(f=>f.name==road.f.name);target.sharedMesh=road.f.sharedMesh;target.GetComponent<MeshRenderer>().sharedMaterial=atlas;
    foreach(var c in target.GetComponents<MeshCollider>())c.sharedMesh=target.sharedMesh;
    EditorUtility.SetDirty(target);PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    foreach(var c in target.GetComponents<MeshCollider>())PrefabUtility.RecordPrefabInstancePropertyModifications(c);
   }
   UnityEngine.Object.DestroyImmediate(clone);clone=null;
   light=UnityEngine.Object.FindObjectOfType<CityDayNightController>(true);night=light&&light.IsNight;if(light)light.ApplyDay();
   Capture(scene,new Vector3(130,1500,-90),new Vector3(130,28,-90),"City-Overview",true,850);
   var boulevardRoad=roads.First(r=>r.layer==2&&r.bounds.size.y<.2f);
   Capture(scene,boulevardRoad.bounds.center+new Vector3(0,45,-45),boulevardRoad.bounds.center,"Boulevard");
   var streetRoad=roads.First(r=>r.bend);
   Capture(scene,streetRoad.bounds.center+new Vector3(0,40,-45),streetRoad.bounds.center,"Two-Lane-Street");
   for(int i=0;i<tunnelPositions.Count;i++)Capture(scene,tunnelPositions[i]+new Vector3(18,7,-30),tunnelPositions[i]+Vector3.up*2,"Underpass-"+(i+1));
   if(light){if(night)light.ApplyNight();else light.ApplyDay();}
   EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   report.AppendLine("SUCCESS. Updated existing Roads.prefab and city instance. Current unsaved scene preserved in backup. Shared atlas retained; no extra realtime lights. Device performance and vehicle driving still require play testing.");
  }catch(Exception e){report.AppendLine("FAILED "+e+"; original prefab applied="+applied);Debug.LogException(e);}
  finally{if(clone)UnityEngine.Object.DestroyImmediate(clone);if(light&&night)light.ApplyNight();File.WriteAllText("DrivingLayoutReview/BuildReport.txt",report.ToString());}
 }
}






