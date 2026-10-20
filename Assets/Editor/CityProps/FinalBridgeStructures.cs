using System;using System.IO;using System.Linq;using System.Text;using System.Collections.Generic;using System.Reflection;using UnityEngine;using UnityEngine.SceneManagement;using UnityEngine.Rendering;using UnityEditor;using UnityEditor.SceneManagement;
[InitializeOnLoad]public static class FinalBridgeStructures{
const string ScenePath="Assets/Editor/DayNightBackups/Gangstar Final Env.unity",Out="Assets/Scenes/Oasis/FinalEnvironmentRoads/BridgeStructures",PrefabPath="Assets/Scenes/Oasis/FinalEnvironmentRoads/Final Roads.prefab";
static FinalBridgeStructures(){EditorApplication.update+=Poll;}
static void Poll(){if(!File.Exists("FinalBridgeStructures.request")||EditorApplication.isCompiling||EditorApplication.isPlayingOrWillChangePlaymode)return;string mode;try{mode=File.ReadAllText("FinalBridgeStructures.request").Trim();File.Delete("FinalBridgeStructures.request");}catch(IOException){return;}if(mode=="audit")Audit();else if(mode=="preview")Preview();else if(mode=="repeated")Repeated();else if(mode=="inspect")InspectRepeated();else Build();}
class Row{public Vector3 p;public float width,s;}
class Curve{public string name;public MeshFilter f;public List<Row> rows=new List<Row>();public Vector3[] world;public int[] ids;public float width,length;
public Vector3 Point(float distance){distance=Mathf.Clamp(distance,0,length);int i=1;while(i<rows.Count-1&&rows[i].s<distance)i++;var a=rows[i-1];var b=rows[i];return Vector3.Lerp(a.p,b.p,Mathf.InverseLerp(a.s,b.s,distance));}
public Vector3 Direction(float distance){var d=Point(distance+2)-Point(distance-2);d.y=0;return d.normalized;}
public Vector3 Side(float distance){return Vector3.Cross(Vector3.up,Direction(distance)).normalized;}}
class Floor{public Vector3[] p;public int[] ids;public Bounds b;}
class Buffer{public List<Vector3> p=new List<Vector3>();public List<Vector2> uv=new List<Vector2>();public List<int> ids=new List<int>();
public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector2 ua,Vector2 ub,Vector2 uc,Vector2 ud){int n=p.Count;p.AddRange(new[]{a,b,c,d});uv.AddRange(new[]{ua,ub,uc,ud});ids.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});}
public void Triangle(Vector3 a,Vector3 b,Vector3 c){int n=p.Count;p.AddRange(new[]{a,b,c});uv.AddRange(new[]{Vector2.zero,Vector2.right,Vector2.up});ids.AddRange(new[]{n,n+1,n+2});}
public void Box(Vector3 center,Vector3 size,Quaternion q){
var r=q*Vector3.right*size.x*.5f;var up=q*Vector3.up*size.y*.5f;var f=q*Vector3.forward*size.z*.5f;
var lo=new[]{center-up-r-f,center-up+r-f,center-up+r+f,center-up-r+f};var hi=lo.Select(v=>v+up*2).ToArray();
for(int i=0;i<4;i++){int j=(i+1)%4;float width=Vector3.Distance(lo[i],lo[j])/3,height=size.y/3;Quad(lo[j],lo[i],hi[i],hi[j],new Vector2(width,0),Vector2.zero,new Vector2(0,height),new Vector2(width,height));}
Quad(lo[0],lo[1],lo[2],lo[3],Vector2.zero,Vector2.right,Vector2.one,Vector2.up);Quad(hi[3],hi[2],hi[1],hi[0],Vector2.zero,Vector2.right,Vector2.one,Vector2.up);}
public void Sweep(List<Vector3> centers,float width,float thickness){
var rings=new List<Vector3[]>();foreach(int i in Enumerable.Range(0,centers.Count)){
var tangent=(centers[Mathf.Min(centers.Count-1,i+1)]-centers[Mathf.Max(0,i-1)]).normalized;
var side=Vector3.Cross(Vector3.up,tangent).normalized;if(side.sqrMagnitude<.1f)side=Vector3.right;var up=Vector3.Cross(tangent,side).normalized;
rings.Add(new[]{centers[i]-side*width/2-up*thickness/2,centers[i]+side*width/2-up*thickness/2,centers[i]+side*width/2+up*thickness/2,centers[i]-side*width/2+up*thickness/2});}
float distance=0;for(int i=1;i<rings.Count;i++){float next=distance+Vector3.Distance(centers[i],centers[i-1]);for(int k=0;k<4;k++){int j=(k+1)%4;Quad(rings[i-1][k],rings[i-1][j],rings[i][j],rings[i][k],new Vector2(k,distance/3),new Vector2(k+1,distance/3),new Vector2(k+1,next/3),new Vector2(k,next/3));}distance=next;}
var a=rings[0];var b=rings[rings.Count-1];Quad(a[3],a[2],a[1],a[0],Vector2.zero,Vector2.right,Vector2.one,Vector2.up);Quad(b[0],b[1],b[2],b[3],Vector2.zero,Vector2.right,Vector2.one,Vector2.up);}
public void Tube(Vector3 a,Vector3 b,float radius,int sides=5){
var d=(b-a).normalized;var side=Vector3.Cross(Mathf.Abs(d.y)>.95f?Vector3.forward:Vector3.up,d).normalized;var up=Vector3.Cross(d,side).normalized;
var ra=new Vector3[sides];var rb=new Vector3[sides];for(int i=0;i<sides;i++){float angle=2*Mathf.PI*i/sides;var v=(side*Mathf.Cos(angle)+up*Mathf.Sin(angle))*radius;ra[i]=a+v;rb[i]=b+v;}
for(int i=0;i<sides;i++){int j=(i+1)%sides;Quad(ra[i],ra[j],rb[j],rb[i],Vector2.zero,Vector2.right,new Vector2(1,Vector3.Distance(a,b)),new Vector2(0,Vector3.Distance(a,b)));}
for(int i=1;i<sides-1;i++){Triangle(ra[0],ra[i+1],ra[i]);Triangle(rb[0],rb[i],rb[i+1]);}}
}
class Cable{public Vector3 a,b;public float radius;public Curve curve;}
static List<Floor> floors=new List<Floor>();static List<Cable> cables=new List<Cable>();
static object Read(Mesh mesh){return typeof(FinalPathwayRepair).GetMethod("Read",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{mesh});}
static T Value<T>(object data,string name){return (T)data.GetType().GetField(name,BindingFlags.Public|BindingFlags.Instance).GetValue(data);}
static Curve GetCurve(GameObject root,string name){
var f=root.GetComponentsInChildren<MeshFilter>(true).Single(v=>v.name==name);var data=Read(f.sharedMesh);var p=Value<Vector3[]>(data,"p").Select(f.transform.TransformPoint).ToArray();var uv=Value<Vector2[]>(data,"uv");
var c=new Curve{name=name,f=f,world=p,ids=Value<int[]>(data,"ids")};
float u0=uv.Min(v=>v.x),u1=uv.Max(v=>v.x);
foreach(var group in Enumerable.Range(0,uv.Length).GroupBy(i=>Mathf.RoundToInt(uv[i].y*1000)).OrderBy(g=>g.Key)){
int a=group.OrderBy(i=>uv[i].x).First(),b=group.OrderByDescending(i=>uv[i].x).First();
float w=Vector3.Distance(p[a],p[b]);if(uv[b].x-uv[a].x<(u1-u0)*.8f||w<20||w>38)continue;var center=(p[a]+p[b])/2;if(c.rows.Count>0&&Vector3.Distance(center,c.rows.Last().p)<.02f)continue;
c.rows.Add(new Row{p=center,width=w});}
if(c.rows.Count<8)throw new Exception("Could not resolve bridge cross-sections: "+name+" rows="+c.rows.Count);
for(int i=1;i<c.rows.Count;i++){float d=Vector3.Distance(c.rows[i].p,c.rows[i-1].p);if(d>75)throw new Exception("Discontinuous centerline on "+name);c.rows[i].s=c.rows[i-1].s+d;}
c.length=c.rows.Last().s;c.width=c.rows.Select(r=>r.width).OrderBy(w=>w).ElementAt(c.rows.Count/2);return c;}
static bool Height(Vector3[] p,int[] ids,Vector3 point,out float y){var args=new object[]{p,ids,point,0f};bool ok=(bool)typeof(FinalPathwayRepair).GetMethod("Height",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);y=(float)args[3];return ok;}
static Vector3 Deck(Curve c,float s,float sign,float extra=1.65f,float above=0){
var p=c.Point(s)+c.Side(s)*sign*(c.width/2+extra);float best=1.2f,height=p.y;
foreach(var f in floors){if(p.x<f.b.min.x-.01f||p.x>f.b.max.x+.01f||p.z<f.b.min.z-.01f||p.z>f.b.max.z+.01f||p.y<f.b.min.y-1.2f||p.y>f.b.max.y+1.2f)continue;
if(Height(f.p,f.ids,p,out float y)&&Mathf.Abs(y-p.y)<best){best=Mathf.Abs(y-p.y);height=y;}}
p.y=height+above;return p;}
static void ColliderBox(Transform root,Vector3 p,Vector3 size,Quaternion rotation,string name){
var go=new GameObject(name);go.transform.SetParent(root,false);go.transform.SetPositionAndRotation(p,rotation);var col=go.AddComponent<BoxCollider>();var scale=go.transform.lossyScale;col.size=new Vector3(size.x/Mathf.Abs(scale.x),size.y/Mathf.Abs(scale.y),size.z/Mathf.Abs(scale.z));}
static void CableTube(Buffer metal,Curve c,Vector3 a,Vector3 b,float radius,bool record){metal.Tube(a,b,radius);if(record)cables.Add(new Cable{a=a,b=b,radius=radius,curve=c});}
static void TieBeams(Buffer metal,Curve c,float start,float end,int segments){
for(int side=-1;side<=1;side+=2){var points=Enumerable.Range(0,segments+1).Select(i=>Deck(c,Mathf.Lerp(start,end,(float)i/segments),side,1.65f,.32f)).ToList();metal.Sweep(points,.35f,.38f);}}
static void Stayed(Curve c,Buffer concrete,Buffer metal,bool low,Transform colliders){
float middle=c.length*.53f,span=Mathf.Min(220,c.length*.5f),start=middle-span/2,end=middle+span/2;TieBeams(metal,c,start,end,low?14:28);
int perHalf=low?6:11;float height=40;
for(int sign=-1;sign<=1;sign+=2){
var foot=Deck(c,middle,sign);var rotation=Quaternion.LookRotation(c.Direction(middle));
concrete.Box(foot+Vector3.up*.32f,new Vector3(2.45f,.64f,3.2f),rotation);
concrete.Box(foot+Vector3.up*(height/2+.64f),new Vector3(2.1f,height,2.6f),rotation);
concrete.Box(foot+Vector3.up*(height+.9f),new Vector3(2.35f,.5f,2.85f),rotation);
if(colliders)ColliderBox(colliders,foot+Vector3.up*(height/2+.32f),new Vector3(2.45f,height+.64f,3.2f),rotation,"Tower collision");
foreach(int direction in new[]{-1,1})for(int i=1;i<=perHalf;i++){
float s=middle+direction*(span*.5f)*((float)i/perHalf);var anchor=Deck(c,s,sign,2.2f,1.6f);var top=foot+Vector3.up*(height*.88f+.64f+height*.10f*((float)i/perHalf));
CableTube(metal,c,top,anchor,.11f,!low);metal.Box(anchor-Vector3.up*.32f,new Vector3(.42f,.64f,.65f),Quaternion.LookRotation(c.Direction(s)));}}
}
static Vector3 ArchPoint(Curve c,float start,float span,float t,float sign,float rise){
var p=Deck(c,start+span*t,sign,1.65f,.65f);p.y+=rise*Mathf.Sqrt(Mathf.Max(0,1-Mathf.Pow(2*t-1,2)));return p;}
static void Arch(Curve c,Buffer concrete,Buffer metal,bool low,bool bases,Transform colliders,float water){
float span=Mathf.Min(bases?126:174,c.length*.75f),start=(c.length-span)/2,end=start+span,rise=bases?28:37;int segments=low?20:40;TieBeams(metal,c,start,end,low?12:24);
for(int sign=-1;sign<=1;sign+=2){
var points=new List<Vector3>();for(int i=0;i<=segments;i++){float angle=Mathf.PI-Mathf.PI*i/segments;float t=(1+Mathf.Cos(angle))*.5f;points.Add(ArchPoint(c,start,span,t,sign,rise));}
metal.Sweep(points,1.15f,bases?1.25f:1.5f);
int hangs=low?7:14;for(int i=1;i<=hangs;i++){
float t=(float)i/(hangs+1);float s=start+span*t;var bottom=Deck(c,s,sign,1.65f,.8f);var top=ArchPoint(c,start,span,t,sign,rise);
CableTube(metal,c,top,bottom,.085f,!low);}
foreach(float s in new[]{start,end}){
var foot=Deck(c,s,sign);var rotation=Quaternion.LookRotation(c.Direction(s));concrete.Box(foot+Vector3.up*.3f,new Vector3(1.9f,.6f,3.0f),rotation);
if(colliders)ColliderBox(colliders,foot+Vector3.up*.65f,new Vector3(1.9f,1.3f,3),rotation,"Arch foot collision");
if(bases){
float bottom=water-5,top=foot.y-.15f;if(top<=bottom)throw new Exception("Invalid water support height.");
concrete.Box(new Vector3(foot.x,(bottom+top)/2,foot.z),new Vector3(2.8f,top-bottom,3.4f),rotation);
concrete.Box(new Vector3(foot.x,bottom+.35f,foot.z),new Vector3(4.7f,.7f,5.2f),rotation);
if(colliders)ColliderBox(colliders,new Vector3(foot.x,(bottom+top)/2,foot.z),new Vector3(4.7f,top-bottom,5.2f),rotation,"Water pier collision");
}}}
foreach(float t in new[]{.23f,.5f,.77f}){var a=ArchPoint(c,start,span,t,-1,rise);var b=ArchPoint(c,start,span,t,1,rise);metal.Box((a+b)/2,new Vector3(.65f,.8f,Vector3.Distance(a,b)),Quaternion.LookRotation(b-a));}
if(bases)foreach(float s in new[]{start,end}){var p=c.Point(s);var size=new Vector3(c.width+6,1.1f,3.5f);concrete.Box(p-Vector3.up*.82f,size,Quaternion.LookRotation(c.Direction(s)));if(colliders)ColliderBox(colliders,p-Vector3.up*.82f,size,Quaternion.LookRotation(c.Direction(s)),"Pier cap collision");}
}
static Mesh Mesh(Buffer b,Transform coordinates,string name){
if(b.p.Any(p=>float.IsNaN(p.x)||float.IsNaN(p.y)||float.IsInfinity(p.x)))throw new Exception("Non-finite bridge geometry.");
var mesh=new Mesh{name=name};if(b.p.Count>65535)mesh.indexFormat=IndexFormat.UInt32;mesh.SetVertices(b.p.Select(coordinates.InverseTransformPoint).ToList());mesh.SetUVs(0,b.uv);mesh.SetTriangles(b.ids,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();
string path=Out+"/"+name+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(old){EditorUtility.CopySerialized(mesh,old);UnityEngine.Object.DestroyImmediate(mesh);EditorUtility.SetDirty(old);return old;}AssetDatabase.CreateAsset(mesh,path);return mesh;}
static Renderer Part(Transform parent,Mesh mesh,Material material,bool shadow,string name){
var go=new GameObject(name);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=material;r.shadowCastingMode=shadow?ShadowCastingMode.On:ShadowCastingMode.Off;r.receiveShadows=true;GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccludeeStatic);return r;}
static void Capture(Scene scene,Vector3 position,Vector3 target,string name,bool top=false,float size=400){
var go=new GameObject("Bridge Review Camera"){hideFlags=HideFlags.HideAndDontSave};SceneManager.MoveGameObjectToScene(go,scene);var c=go.AddComponent<Camera>();c.enabled=false;c.nearClipPlane=.2f;c.farClipPlane=5000;c.fieldOfView=60;c.orthographic=top;c.orthographicSize=size;c.transform.SetPositionAndRotation(position,top?Quaternion.LookRotation(Vector3.down,Vector3.right):Quaternion.LookRotation(target-position));
var rt=RenderTexture.GetTemporary(1600,1100,24);var previous=RenderTexture.active;try{c.targetTexture=rt;c.Render();RenderTexture.active=rt;var tex=new Texture2D(1600,1100,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1600,1100),0,0);tex.Apply();File.WriteAllBytes("FinalEnvironmentRoadReview/"+name+".png",tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);}
finally{c.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(go);}}
static void Audit(){
var log=new StringBuilder();var labels=new List<GameObject>();try{
var scene=SceneManager.GetSceneByPath(ScenePath);var root=scene.GetRootGameObjects().Single(g=>g.name=="Track Roads");
foreach(string name in new[]{"polySurface539","polySurface537","polySurface538","polySurface536","polySurface965"}){
var c=GetCurve(root,name);log.AppendLine(name+" rows="+c.rows.Count+" length="+c.length+" width="+c.width+" start="+c.Point(0)+" middle="+c.Point(c.length/2)+" end="+c.Point(c.length));
var go=new GameObject("Temporary bridge label"){hideFlags=HideFlags.HideAndDontSave};SceneManager.MoveGameObjectToScene(go,scene);labels.Add(go);var t=go.AddComponent<TextMesh>();t.text=name.Replace("polySurface","");t.characterSize=8;t.fontSize=50;t.anchor=TextAnchor.MiddleCenter;t.alignment=TextAlignment.Center;t.color=Color.cyan;go.transform.SetPositionAndRotation(c.Point(c.length/2)+Vector3.up*180,Quaternion.LookRotation(Vector3.down,Vector3.right));
}
Capture(scene,new Vector3(70,1200,-250),Vector3.zero,"Bridge-Location-Audit",true,430);
Capture(scene,new Vector3(40,700,528),Vector3.zero,"Small-Bridge-Location-Audit",true,140);
}catch(Exception e){log.AppendLine("FAILED "+e);Debug.LogException(e);}finally{foreach(var g in labels)UnityEngine.Object.DestroyImmediate(g);File.WriteAllText("FinalEnvironmentRoadReview/BridgeAudit.txt",log.ToString());}}
static void Preview(){
var scene=SceneManager.GetSceneByPath(ScenePath);var root=scene.GetRootGameObjects().Single(g=>g.name=="Track Roads");var c=GetCurve(root,"polySurface538");float s=c.length*.53f;var point=c.Point(s);
Capture(scene,point+c.Side(s)*140-c.Direction(s)*70+Vector3.up*75,point+Vector3.up*20,"Bridge-1-Structure");
}
static void Repeated(){
GameObject group=null;bool saved=false;var log=new StringBuilder();try{
var scene=SceneManager.GetSceneByPath(ScenePath);if(!scene.isLoaded)throw new Exception("Final scene not loaded.");var root=scene.GetRootGameObjects().Single(g=>g.name=="Track Roads");var c=GetCurve(root,"polySurface539");
const string name="Bridge 04 - Repeated Arches With Water Bases - polySurface539";
if(root.GetComponentsInChildren<Transform>(true).Any(t=>t.name==name))throw new Exception("Repeated bridge already exists.");
string stamp=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");EditorSceneManager.SaveScene(scene,"FinalEnvironmentRoadBackup/BeforeRepeatedArches-"+stamp+".unity",true);File.Copy(PrefabPath,"FinalEnvironmentRoadBackup/BeforeRepeatedArches-"+stamp+".prefab");
var metal=AssetDatabase.LoadAssetAtPath<Material>("Assets/Scenes/Oasis/DrivingLayout/Bridge Guardrail.mat");var concrete=AssetDatabase.LoadAssetAtPath<Material>("Assets/Scenes/Oasis/FinalEnvironmentRoads/Tunnel Concrete.mat");
float water=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshFilter>(true)).Single(f=>f.name=="Water").GetComponent<Renderer>().bounds.center.y;
floors.Clear();cables.Clear();foreach(var f in root.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh&&f.GetComponent<Renderer>()&&f.GetComponent<Renderer>().sharedMaterials.Any(m=>m&&(m.name=="Road Atlas"||m.name=="Pathways")))){var data=Read(f.sharedMesh);floors.Add(new Floor{p=Value<Vector3[]>(data,"p").Select(f.transform.TransformPoint).ToArray(),ids=Value<int[]>(data,"ids"),b=f.GetComponent<Renderer>().bounds});}
var parents=root.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Bridge Superstructures").ToArray();var parent=parents.Single(t=>PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject));foreach(var duplicate in parents.Where(t=>!PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject))){if(duplicate.GetComponentsInChildren<MeshFilter>(true).All(f=>parent.GetComponentsInChildren<MeshFilter>(true).Any(o=>o.sharedMesh==f.sharedMesh)))UnityEngine.Object.DestroyImmediate(duplicate.gameObject);else throw new Exception("Unexpected differing bridge duplicate.");}group=new GameObject(name);group.transform.SetParent(parent,false);var physics=new GameObject("Simple structure collisions");physics.transform.SetParent(group.transform,false);var levels=new List<Renderer[]>();int high=0,low=0;
for(int level=0;level<2;level++){
var solid=new Buffer();var steel=new Buffer();int segments=level==0?28:14;float[] spans={75,144,75};float start=(c.length-310)/2;
for(int arch=0;arch<3;arch++){float span=spans[arch],rise=arch==1?32:16;
TieBeams(steel,c,start,start+span,level==0?12:6);
for(int sign=-1;sign<=1;sign+=2){var points=new List<Vector3>();for(int j=0;j<=segments;j++){float angle=Mathf.PI-Mathf.PI*j/segments,t=(1+Mathf.Cos(angle))*.5f;points.Add(ArchPoint(c,start,span,t,sign,rise));}steel.Sweep(points,.85f,1.05f);
int hangs=level==0?(arch==1?9:5):(arch==1?5:3);for(int j=1;j<=hangs;j++){float t=(float)j/(hangs+1);CableTube(steel,c,ArchPoint(c,start,span,t,sign,rise),Deck(c,start+span*t,sign,1.65f,.8f),.07f,level==0);}
foreach(float s in new[]{start,start+span}){var foot=Deck(c,s,sign);var q=Quaternion.LookRotation(c.Direction(s));solid.Box(foot+Vector3.up*.3f,new Vector3(1.6f,.6f,2.6f),q);float bottom=water-5,top=foot.y-.15f;solid.Box(new Vector3(foot.x,(bottom+top)/2,foot.z),new Vector3(2.6f,top-bottom,3.2f),q);solid.Box(new Vector3(foot.x,bottom+.35f,foot.z),new Vector3(4.5f,.7f,4.8f),q);
if(level==0){ColliderBox(physics.transform,foot+Vector3.up*.6f,new Vector3(1.6f,1.2f,2.6f),q,"Arch foot collision");ColliderBox(physics.transform,new Vector3(foot.x,(bottom+top)/2,foot.z),new Vector3(4.5f,top-bottom,4.8f),q,"Water pier collision");}}}
foreach(float s in new[]{start,start+span}){var p=c.Point(s);var size=new Vector3(c.width+6,1.1f,3.5f);solid.Box(p-Vector3.up*.82f,size,Quaternion.LookRotation(c.Direction(s)));if(level==0)ColliderBox(physics.transform,p-Vector3.up*.82f,size,Quaternion.LookRotation(c.Direction(s)),"Pier cap collision");}
start+=span+8;}
var lod=new GameObject("LOD "+level);lod.transform.SetParent(group.transform,false);var sm=Mesh(solid,root.transform,"Bridge_539_Concrete_LOD"+level);var mm=Mesh(steel,root.transform,"Bridge_539_Metal_LOD"+level);levels.Add(new[]{Part(lod.transform,sm,concrete,true,"Concrete water bases"),Part(lod.transform,mm,metal,false,"Repeated arches and hangers")});if(level==0)high=(solid.ids.Count+steel.ids.Count)/3;else low=(solid.ids.Count+steel.ids.Count)/3;}
var l=group.AddComponent<LODGroup>();l.SetLODs(new[]{new LOD(.24f,levels[0]),new LOD(.018f,levels[1])});l.RecalculateBounds();Physics.SyncTransforms();int probes=0;
for(float s=6;s<c.length-6;s+=6)foreach(float offset in new[]{-10.5f,-3.5f,3.5f,10.5f}){var p=c.Point(s)+c.Side(s)*offset;var next=c.Point(s+4)+c.Side(s+4)*offset;if(Height(c.world,c.ids,p,out float floorY))p.y=floorY;if(Height(c.world,c.ids,next,out float nextY))next.y=nextY;var movement=next-p;var blocking=Physics.CapsuleCastAll(p+Vector3.up*1.7f,p+Vector3.up*2,1.5f,movement.normalized,movement.magnitude,~0,QueryTriggerInteraction.Ignore).FirstOrDefault(h=>h.collider.transform.IsChildOf(group.transform));if(blocking.collider)throw new Exception("Traffic blocked at "+p+" by "+blocking.collider.name+" position="+blocking.collider.transform.position+" bounds="+blocking.collider.bounds);probes++;}
foreach(var cable in cables)for(int i=1;i<24;i++){var p=Vector3.Lerp(cable.a,cable.b,i/24f);if(Height(c.world,c.ids,p,out float y)&&p.y-y<3.4f)throw new Exception("Hanger clearance failed.");}
if(group.GetComponentsInChildren<Light>(true).Length!=0||high>5000||low>=high)throw new Exception("Mobile constraints failed.");
var prefab=PrefabUtility.LoadPrefabContents(PrefabPath);try{var p=prefab.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Bridge Superstructures");var mistaken=p.Cast<Transform>().FirstOrDefault(t=>t.name=="Bridge 03 - Repeated Arches With Water Bases - polySurface965");if(mistaken){UnityEngine.Object.DestroyImmediate(mistaken.gameObject);RestoreCanalBridge(root,p,metal,concrete,water);}foreach(var old in p.Cast<Transform>().Where(t=>t.name=="Bridge 04 - Repeated Arches With Water Bases - polySurface536").ToArray())UnityEngine.Object.DestroyImmediate(old.gameObject);var copy=UnityEngine.Object.Instantiate(group,p,false);copy.name=name;PrefabUtility.SaveAsPrefabAsset(prefab,PrefabPath,out bool ok);if(!ok)throw new Exception("Prefab save failed.");saved=true;}finally{PrefabUtility.UnloadPrefabContents(prefab);}
AssetDatabase.SaveAssets();UnityEngine.Object.DestroyImmediate(group);group=null;if(root.GetComponentsInChildren<Transform>(true).Count(t=>t.name==name)!=1)throw new Exception("Expected one inherited bridge instance.");EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new Exception("Scene save failed.");var point=c.Point(c.length/2);Capture(scene,point-c.Direction(c.length/2)*95+c.Side(c.length/2)*70+Vector3.up*55,point+Vector3.up*12,"Bridge-4-Repeated-Arches");Capture(scene,point+c.Side(c.length/2)*90+Vector3.up*5,point-Vector3.up*3,"Bridge-4-Water-Bases");
log.AppendLine("SUCCESS: polySurface539; three arch spans per side (small, large, small); twelve submerged piers with footings; six under-deck cap beams; saved scene and road prefab. High triangles="+high+"; distant triangles="+low+"; vehicle checks="+probes+"; new lights=0; new textures=0; existing materials=2. Device performance unmeasured.");
}catch(Exception e){if(group&&!saved)UnityEngine.Object.DestroyImmediate(group);log.AppendLine("FAILED "+e);Debug.LogException(e);}finally{File.WriteAllText("FinalEnvironmentRoadReview/RepeatedArchesReport.txt",log.ToString());}}
static void RestoreCanalBridge(GameObject root,Transform parent,Material metal,Material concrete,float water){
var c=GetCurve(root,"polySurface965");var bridge=new GameObject("Bridge 03 - Canal Arch With Water Bases - polySurface965");bridge.transform.SetParent(parent,false);var collision=new GameObject("Simple structure collisions");collision.transform.SetParent(bridge.transform,false);var levels=new List<Renderer[]>();
for(int level=0;level<2;level++){var a=new Buffer();var b=new Buffer();Arch(c,a,b,level==1,true,level==0?collision.transform:null,water);var lod=new GameObject("LOD "+level);lod.transform.SetParent(bridge.transform,false);levels.Add(new[]{Part(lod.transform,AssetDatabase.LoadAssetAtPath<Mesh>(Out+"/Bridge_3_Concrete_LOD"+level+".asset"),concrete,true,"Concrete"),Part(lod.transform,AssetDatabase.LoadAssetAtPath<Mesh>(Out+"/Bridge_3_Metal_LOD"+level+".asset"),metal,false,"Metal and cables")});}
var l=bridge.AddComponent<LODGroup>();l.SetLODs(new[]{new LOD(.24f,levels[0]),new LOD(.018f,levels[1])});l.RecalculateBounds();}
static void InspectRepeated(){var scene=SceneManager.GetSceneByPath(ScenePath);var root=scene.GetRootGameObjects().Single(g=>g.name=="Track Roads");var log=new StringBuilder();foreach(var t in root.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Bridge Superstructures"||t.name.StartsWith("Bridge 03 - Repeated"))){log.AppendLine(t.name+" active="+t.gameObject.activeSelf+" hierarchy="+t.gameObject.activeInHierarchy+" pos="+t.position+" scale="+t.lossyScale);foreach(var r in t.GetComponentsInChildren<Renderer>(true))log.AppendLine(r.name+" enabled="+r.enabled+" bound="+r.bounds+" active="+r.gameObject.activeInHierarchy);foreach(var l in t.GetComponentsInChildren<LODGroup>(true))log.AppendLine("LOD size="+l.size+" local reference="+l.localReferencePoint+" renderers="+string.Join(",",l.GetLODs().SelectMany(v=>v.renderers).Select(r=>r?r.name:"NULL")));}var curve=GetCurve(root,"polySurface539");var point=curve.Point(curve.length/2);Capture(scene,point-curve.Direction(curve.length/2)*95+curve.Side(curve.length/2)*70+Vector3.up*55,point+Vector3.up*12,"Bridge-4-Repeated-Arches");Capture(scene,point+curve.Side(curve.length/2)*90+Vector3.up*5,point-Vector3.up*3,"Bridge-4-Water-Bases");File.WriteAllText("FinalEnvironmentRoadReview/RepeatedInspection.txt",log.ToString());}
static void Build(){
var log=new StringBuilder();GameObject group=null;CityDayNightController light=null;bool night=false,saved=false;try{
var scene=SceneManager.GetSceneByPath(ScenePath);if(!scene.isLoaded)throw new Exception("Final Environment scene must be loaded.");var root=scene.GetRootGameObjects().Single(g=>g.name=="Track Roads");
if(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root)!=PrefabPath)throw new Exception("Wrong road prefab.");
if(root.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="Bridge Superstructures"))throw new Exception("Bridge structures already exist; inspect before rebuilding.");
Directory.CreateDirectory(Out);Directory.CreateDirectory("FinalEnvironmentRoadReview");Directory.CreateDirectory("FinalEnvironmentRoadBackup");string stamp=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
if(!EditorSceneManager.SaveScene(scene,"FinalEnvironmentRoadBackup/BeforeBridgeStructures-"+stamp+".unity",true))throw new Exception("Scene backup failed.");File.Copy(PrefabPath,"FinalEnvironmentRoadBackup/BeforeBridgeStructures-"+stamp+".prefab");AssetDatabase.Refresh();
var metal=AssetDatabase.LoadAssetAtPath<Material>("Assets/Scenes/Oasis/DrivingLayout/Bridge Guardrail.mat");var concrete=AssetDatabase.LoadAssetAtPath<Material>("Assets/Scenes/Oasis/FinalEnvironmentRoads/Tunnel Concrete.mat");
if(!metal||!concrete)throw new Exception("Existing structural materials missing.");if(metal.IsKeywordEnabled("_EMISSION")||concrete.IsKeywordEnabled("_EMISSION"))throw new Exception("Structural material unexpectedly emits light.");
var curves=new[]{GetCurve(root,"polySurface538"),GetCurve(root,"polySurface537"),GetCurve(root,"polySurface965")};floors.Clear();cables.Clear();
foreach(var f in root.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh&&f.GetComponent<Renderer>()&&f.GetComponent<Renderer>().sharedMaterials.Any(m=>m&&(m.name=="Road Atlas"||m.name=="Pathways")))){
var data=Read(f.sharedMesh);floors.Add(new Floor{p=Value<Vector3[]>(data,"p").Select(f.transform.TransformPoint).ToArray(),ids=Value<int[]>(data,"ids"),b=f.GetComponent<Renderer>().bounds});}
var waterObject=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshFilter>(true)).Single(f=>f.name=="Water");float water=waterObject.GetComponent<Renderer>().bounds.center.y;
group=new GameObject("Bridge Superstructures");SceneManager.MoveGameObjectToScene(group,scene);group.transform.SetParent(root.transform,false);
int hiTriangles=0,lowTriangles=0;var bridgeRoots=new List<GameObject>();
for(int index=0;index<3;index++){
var curve=curves[index];string name=index==0?"Bridge 01 - Cable Stayed - polySurface538":index==1?"Bridge 02 - Twin Arch - polySurface537":"Bridge 03 - Canal Arch With Water Bases - polySurface965";
var bridge=new GameObject(name);bridge.transform.SetParent(group.transform,false);bridgeRoots.Add(bridge);var physics=new GameObject("Simple structure collisions");physics.transform.SetParent(bridge.transform,false);
var levels=new List<Renderer[]>();
for(int level=0;level<2;level++){
var solid=new Buffer();var steel=new Buffer();var lod=new GameObject("LOD "+level);lod.transform.SetParent(bridge.transform,false);
if(index==0)Stayed(curve,solid,steel,level==1,level==0?physics.transform:null);else Arch(curve,solid,steel,level==1,index==2,level==0?physics.transform:null,water);
var sm=Mesh(solid,root.transform,"Bridge_"+(index+1)+"_Concrete_LOD"+level);var mm=Mesh(steel,root.transform,"Bridge_"+(index+1)+"_Metal_LOD"+level);
int tris=solid.ids.Count/3+steel.ids.Count/3;if(level==0)hiTriangles+=tris;else lowTriangles+=tris;
levels.Add(new[]{Part(lod.transform,sm,concrete,true,"Concrete"),Part(lod.transform,mm,metal,false,"Metal and cables")});
}
var lodGroup=bridge.AddComponent<LODGroup>();lodGroup.SetLODs(new[]{new LOD(.24f,levels[0]),new LOD(.018f,levels[1])});lodGroup.RecalculateBounds();
log.AppendLine(name+"; curve length="+curve.length.ToString("F2")+"m; source deck width="+curve.width.ToString("F2")+"m; water foundations="+(index==2?4:0));
}
if(hiTriangles>10000||lowTriangles>=hiTriangles)throw new Exception("Bridge triangle budget exceeded.");
int cableChecks=0;foreach(var cable in cables)for(int i=1;i<24;i++){
var point=Vector3.Lerp(cable.a,cable.b,(float)i/24);
if(Height(cable.curve.world,cable.curve.ids,point,out float y)&&point.y-y<3.25f+cable.radius)throw new Exception("Cable intrudes into vehicle clearance on "+cable.curve.name+" at "+point);
cableChecks++;
}
Physics.SyncTransforms();int trafficChecks=0;
foreach(var c in curves)for(float s=12;s<c.length-12;s+=12)foreach(float offset in new[]{-10.5f,-3.5f,3.5f,10.5f}){
var p=c.Point(s)+c.Side(s)*offset;var hit=Physics.CapsuleCastAll(p+Vector3.up*1.7f,p+Vector3.up*2.0f,1.5f,c.Direction(s),4,~0,QueryTriggerInteraction.Ignore).FirstOrDefault(h=>h.collider.transform.IsChildOf(group.transform));
if(hit.collider)throw new Exception("Structure blocks traffic on "+c.name+" at "+p+" by "+hit.collider.name);trafficChecks++;}
if(group.GetComponentsInChildren<Light>(true).Length!=0)throw new Exception("Unexpected added light.");
var prefab=PrefabUtility.LoadPrefabContents(PrefabPath);try{var copy=UnityEngine.Object.Instantiate(group,prefab.transform,false);copy.name=group.name;PrefabUtility.SaveAsPrefabAsset(prefab,PrefabPath,out bool ok);if(!ok)throw new Exception("Bridge prefab save failed.");saved=true;}finally{PrefabUtility.UnloadPrefabContents(prefab);}
AssetDatabase.SaveAssets();light=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CityDayNightController>(true)).FirstOrDefault();night=light&&light.IsNight;if(light)light.ApplyDay();
for(int i=0;i<3;i++){var c=curves[i];var point=c.Point(c.length*(i==0?.53f:.5f));var dir=c.Direction(c.length/2);var side=c.Side(c.length/2);
Capture(scene,point-dir*(i==2?95:150)-side*(i==2?65:95)+Vector3.up*(i==2?45:72),point+Vector3.up*(i==2?15:23),"Bridge-"+(i+1)+"-Structure");
}
Capture(scene,new Vector3(70,1000,-250),Vector3.zero,"Bridge-Structures-Overview",true,430);
if(light){if(night)light.ApplyNight();else light.ApplyDay();}EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new Exception("Scene save failed.");
log.AppendLine("SUCCESS. Three different bridge groups saved within Final Roads prefab. Full detail triangles="+hiTriangles+"; distant detail triangles="+lowTriangles+"; active renderers at one LOD=6; reused material assets=2; new texture assets=0; new lights=0.");
log.AppendLine("Decorative cable clearance probes="+cableChecks+"; vehicle lane sweeps="+trafficChecks+"; simple box colliders="+group.GetComponentsInChildren<BoxCollider>(true).Length+". Original road geometry and source materials preserved. Actual mobile device timing unmeasured.");
}catch(Exception e){if(group&&!saved)UnityEngine.Object.DestroyImmediate(group);log.AppendLine("FAILED "+e+"; prefab saved="+saved);Debug.LogException(e);}
finally{if(light&&night)light.ApplyNight();File.WriteAllText("FinalEnvironmentRoadReview/BridgeStructuresReport.txt",log.ToString());}
}
}











