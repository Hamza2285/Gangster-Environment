using System;using System.IO;using System.Linq;using System.Text;using System.Collections.Generic;using UnityEngine;using UnityEngine.Rendering;using UnityEngine.SceneManagement;using UnityEditor;using UnityEditor.SceneManagement;using Unity.Collections;
[InitializeOnLoad]public static class FinalPathwayRepair{
const string ScenePath="Assets/Editor/DayNightBackups/Gangstar Final Env.unity",Out="Assets/Scenes/Oasis/FinalEnvironmentRoads",PrefabPath=Out+"/Final Roads.prefab";
static readonly string[] RoadNames={"polySurface809","polySurface816","polySurface810","polySurface878","polySurface862","polySurface840"};
static FinalPathwayRepair(){EditorApplication.update+=Poll;}
static void Poll(){if(!File.Exists("FinalPathwayRepair.request")||EditorApplication.isCompiling||EditorApplication.isPlayingOrWillChangePlaymode)return;try{File.Delete("FinalPathwayRepair.request");}catch(IOException){return;}Run();}
class Data{public Vector3[] p;public Vector2[] uv,uv1;public int[] ids;public bool second;}
class Profile{public bool x;public float a,b,c,half,y;}
struct V{public Vector3 p;public Vector2 uv,uv1;public V(Vector3 p,Vector2 uv,Vector2 uv1){this.p=p;this.uv=uv;this.uv1=uv1;}}
class Floor{public Vector3[] before,after;public int[] original,current;public Bounds bounds;}
class Change{public MeshFilter f;public Mesh before,after;}
class Mask{public Vector3 a,b,c;public float minX,maxX,minZ,maxZ;}
class Boundary{public V a,b;public int count;}
static List<Profile> profiles=new List<Profile>();static List<Floor> neighbors=new List<Floor>();
static Data Read(Mesh mesh){
if(mesh.subMeshCount!=1)throw new Exception("Mixed submesh needs separate handling: "+mesh.name);
using(var snapshot=MeshUtility.AcquireReadOnlyMeshData(mesh)){
var d=snapshot[0];var result=new Data();using(var a=new NativeArray<Vector3>(d.vertexCount,Allocator.Temp)){d.GetVertices(a);result.p=a.ToArray();}
using(var a=new NativeArray<Vector2>(d.vertexCount,Allocator.Temp)){d.GetUVs(0,a);result.uv=a.ToArray();}
result.second=d.HasVertexAttribute(VertexAttribute.TexCoord1);if(result.second){using(var a=new NativeArray<Vector2>(d.vertexCount,Allocator.Temp)){d.GetUVs(1,a);result.uv1=a.ToArray();}}else result.uv1=new Vector2[d.vertexCount];
using(var a=new NativeArray<int>(d.GetSubMesh(0).indexCount,Allocator.Temp)){d.GetIndices(a,0);result.ids=a.ToArray();}return result;}}
static float Long(Vector3 p,bool x){return x?p.x:p.z;}static float Side(Vector3 p,bool x){return x?p.z:p.x;}
static bool Affected(Bounds b){return profiles.Any(f=>Long(b.max,f.x)>f.a&&Long(b.min,f.x)<f.b&&Side(b.max,f.x)>f.c-f.half-7&&Side(b.min,f.x)<f.c+f.half+7&&b.min.y<f.y+1&&b.max.y>f.y-1);}
static float Analytic(Vector3 p){float d=0;foreach(var f in profiles){float t=(Long(p,f.x)-f.a)/(f.b-f.a);if(t<=0||t>=1||Mathf.Abs(p.y-f.y)>1)continue;
float extra=Mathf.Abs(Side(p,f.x)-f.c)-f.half;if(extra>=7)continue;float weight=extra<=0?1:.5f+.5f*Mathf.Cos(Mathf.PI*extra/7);
d+=3.15f*Mathf.Pow(Mathf.Sin(Mathf.PI*t),2)*Mathf.Sin(2*Mathf.PI*t)*weight;}return d;}
static bool Height(Vector3[] p,int[] ids,Vector3 point,out float y){
float closest=float.PositiveInfinity;y=0;
for(int i=0;i<ids.Length;i+=3){var a=p[ids[i]];var b=p[ids[i+1]];var c=p[ids[i+2]];
if(point.x<Mathf.Min(a.x,b.x,c.x)-.003f||point.x>Mathf.Max(a.x,b.x,c.x)+.003f||point.z<Mathf.Min(a.z,b.z,c.z)-.003f||point.z>Mathf.Max(a.z,b.z,c.z)+.003f)continue;
float d=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);if(Mathf.Abs(d)<.000001f)continue;
float u=((b.z-c.z)*(point.x-c.x)+(c.x-b.x)*(point.z-c.z))/d,v=((c.z-a.z)*(point.x-c.x)+(a.x-c.x)*(point.z-c.z))/d;
if(u<-.0002f||v<-.0002f||u+v>1.0002f)continue;float h=u*a.y+v*b.y+(1-u-v)*c.y,delta=Mathf.Abs(h-point.y);
if(delta<closest){closest=delta;y=h;}}return closest<float.PositiveInfinity;}
static Func<Vector3,float> displacementOverride;
static float Displacement(Vector3 p){
if(displacementOverride!=null)return displacementOverride(p);
float analytic=Analytic(p);if(Mathf.Abs(analytic)<.000001f)return 0;
foreach(var f in neighbors){
if(p.x<f.bounds.min.x-.003f||p.x>f.bounds.max.x+.003f||p.z<f.bounds.min.z-.003f||p.z>f.bounds.max.z+.003f)continue;
if(!Height(f.before,f.original,p,out float old)||Mathf.Abs(old-p.y)>.35f)continue;
var target=p;target.y=old+analytic;
if(Height(f.after,f.current,target,out float height)&&Mathf.Abs(height-target.y)<.4f)return height-old;
}return analytic;}
static V Lerp(V a,V b,float t){return new V(Vector3.Lerp(a.p,b.p,t),Vector2.Lerp(a.uv,b.uv,t),Vector2.Lerp(a.uv1,b.uv1,t));}
static List<V> Half(List<V> poly,Func<V,float> distance,bool inside){
var output=new List<V>();if(poly.Count<3)return output;var a=poly[poly.Count-1];float da=distance(a);bool ina=inside?da>=0:da<=0;
foreach(var b in poly){float db=distance(b);bool inb=inside?db>=0:db<=0;if(ina!=inb)output.Add(Lerp(a,b,da/(da-db)));if(inb)output.Add(b);a=b;da=db;ina=inb;}return output;}
static List<V> Axis(List<V> poly,bool x,float cut,bool above){return Half(poly,v=>Long(v.p,x)-cut,above);}
static List<List<V>> Split(List<List<V>> pieces,bool x,float cut){
var next=new List<List<V>>();foreach(var poly in pieces){if(poly.Min(v=>Long(v.p,x))<cut-.00001f&&poly.Max(v=>Long(v.p,x))>cut+.00001f){
var a=Axis(poly,x,cut,true);var b=Axis(poly,x,cut,false);if(a.Count>2)next.Add(a);if(b.Count>2)next.Add(b);}else next.Add(poly);}return next;}
static string Key(Vector3 p){return Mathf.RoundToInt(p.x*1000)+","+Mathf.RoundToInt(p.y*1000)+","+Mathf.RoundToInt(p.z*1000);}
static void Edge(Dictionary<string,Boundary> edges,V a,V b){
string ka=Key(a.p),kb=Key(b.p),key=string.CompareOrdinal(ka,kb)<0?ka+"|"+kb:kb+"|"+ka;
if(edges.TryGetValue(key,out var edge))edge.count++;else edges[key]=new Boundary{a=a,b=b,count=1};}
static Mesh BuildMesh(string name,List<V> triangles,Transform transform,bool uv1){
var positions=new List<Vector3>();var mapping=new List<Vector2>();var secondary=new List<Vector2>();var ids=new List<int>();
for(int i=0;i<triangles.Count;i+=3){var a=triangles[i];var b=triangles[i+1];var c=triangles[i+2];if(Vector3.Cross(b.p-a.p,c.p-a.p).sqrMagnitude<.00000001f)continue;
foreach(var v in new[]{a,b,c}){ids.Add(positions.Count);positions.Add(transform.InverseTransformPoint(v.p));mapping.Add(v.uv);secondary.Add(v.uv1);}}
var m=new Mesh{name=name};if(positions.Count>65535)m.indexFormat=IndexFormat.UInt32;m.SetVertices(positions);m.SetUVs(0,mapping);if(uv1)m.SetUVs(1,secondary);m.SetTriangles(ids,0);m.RecalculateNormals();m.RecalculateTangents();m.RecalculateBounds();return m;}
static void Fan(List<V> poly,List<V> output){for(int i=1;i<poly.Count-1;i++){output.Add(poly[0]);output.Add(poly[i]);output.Add(poly[i+1]);}}
static Mesh Pathway(MeshFilter f,Data data,out int foundations){
var source=data.p.Select(f.transform.TransformPoint).ToArray();var baseTriangles=new List<V>();
for(int i=0;i<data.ids.Length;i+=3){
var start=new List<V>();for(int j=0;j<3;j++){int k=data.ids[i+j];start.Add(new V(source[k],data.uv[k],data.uv1[k]));}
var parts=new List<List<V>>{start};
foreach(var field in profiles){
if(start.Max(v=>Long(v.p,field.x))<field.a||start.Min(v=>Long(v.p,field.x))>field.b||start.Max(v=>Side(v.p,field.x))<field.c-field.half-7||start.Min(v=>Side(v.p,field.x))>field.c+field.half+7||start.Min(v=>v.p.y)>field.y+1||start.Max(v=>v.p.y)<field.y-1)continue;
int n=Mathf.CeilToInt((field.b-field.a)/4);for(int k=0;k<=n;k++)parts=Split(parts,field.x,Mathf.Lerp(field.a,field.b,(float)k/n));
foreach(float cut in new[]{field.c-field.half-7,field.c-field.half-3.5f,field.c-field.half,field.c+field.half,field.c+field.half+3.5f,field.c+field.half+7})parts=Split(parts,!field.x,cut);
}foreach(var part in parts)Fan(part,baseTriangles);}
var edges=new Dictionary<string,Boundary>();
for(int i=0;i<baseTriangles.Count;i+=3){var a=baseTriangles[i];var b=baseTriangles[i+1];var c=baseTriangles[i+2];
if(Vector3.Cross(b.p-a.p,c.p-a.p).normalized.y>.8f){Edge(edges,a,b);Edge(edges,b,c);Edge(edges,c,a);}}
var output=baseTriangles.Select(v=>new V(v.p+Vector3.up*Displacement(v.p),v.uv,v.uv1)).ToList();foundations=0;
foreach(var edge in edges.Values.Where(e=>e.count==1)){
float da=Displacement(edge.a.p),db=Displacement(edge.b.p);if(Mathf.Max(Mathf.Abs(da),Mathf.Abs(db))<.025f||Vector3.Distance(edge.a.p,edge.b.p)<.03f)continue;
var a=edge.a;var b=edge.b;var topA=new V(a.p+Vector3.up*da,a.uv,a.uv1);var topB=new V(b.p+Vector3.up*db,b.uv,b.uv1);
var bottomA=topA;bottomA.p.y=Mathf.Min(a.p.y,topA.p.y)-.35f;bottomA.uv.y-=.35f+Mathf.Abs(da);
var bottomB=topB;bottomB.p.y=Mathf.Min(b.p.y,topB.p.y)-.35f;bottomB.uv.y-=.35f+Mathf.Abs(db);
output.AddRange(new[]{topA,bottomA,bottomB,topA,bottomB,topB});foundations++;
}return BuildMesh("Pathway_"+f.name,output,f.transform,data.second);}
static List<Mask> Masks(List<Tuple<MeshFilter,Data>> land){
var masks=new List<Mask>();
foreach(var entry in land){var f=entry.Item1;var data=entry.Item2;var w=data.p.Select(f.transform.TransformPoint).ToArray();
for(int i=0;i<data.ids.Length;i+=3){var a=w[data.ids[i]];var b=w[data.ids[i+1]];var c=w[data.ids[i+2]];
if(Mathf.Abs(Vector3.Cross(b-a,c-a).normalized.y)<.8f)continue;
foreach(var field in profiles){
if(Mathf.Min(a.y,b.y,c.y)<field.y-1||Mathf.Max(a.y,b.y,c.y)>field.y+1)continue;
var poly=new List<V>{new V(a,Vector2.zero,Vector2.zero),new V(b,Vector2.zero,Vector2.zero),new V(c,Vector2.zero,Vector2.zero)};
poly=Axis(poly,field.x,(field.a+field.b)*.5f,true);poly=Axis(poly,field.x,field.b,false);poly=Axis(poly,!field.x,field.c-field.half-7,true);poly=Axis(poly,!field.x,field.c+field.half+7,false);
for(int k=1;k<poly.Count-1;k++){
var x=poly[0].p;var y=poly[k].p;var z=poly[k+1].p;float cross=(y.x-x.x)*(z.z-x.z)-(y.z-x.z)*(z.x-x.x);if(Mathf.Abs(cross)<.0001f)continue;if(cross<0){var swap=y;y=z;z=swap;}
masks.Add(new Mask{a=x,b=y,c=z,minX=Mathf.Min(x.x,y.x,z.x),maxX=Mathf.Max(x.x,y.x,z.x),minZ=Mathf.Min(x.z,y.z,z.z),maxZ=Mathf.Max(x.z,y.z,z.z)});
}}}}return masks;}
static bool PlanarValid(List<V> polygon){if(polygon.Count<3)return false;double area=0;for(int i=0;i<polygon.Count;i++){var a=polygon[i].p;var b=polygon[(i+1)%polygon.Count].p;area+=(double)a.x*b.z-(double)b.x*a.z;}return Math.Abs(area)>.0001;}
static List<List<V>> Subtract(List<V> poly,Mask mask){
if(poly.Max(v=>v.p.x)<mask.minX||poly.Min(v=>v.p.x)>mask.maxX||poly.Max(v=>v.p.z)<mask.minZ||poly.Min(v=>v.p.z)>mask.maxZ)return new List<List<V>>{poly};
var output=new List<List<V>>();var inside=poly;var corners=new[]{mask.a,mask.b,mask.c};
for(int i=0;i<3&&inside.Count>2;i++){var a=corners[i];var b=corners[(i+1)%3];Func<V,float> distance=v=>(b.x-a.x)*(v.p.z-a.z)-(b.z-a.z)*(v.p.x-a.x);
var outside=Half(inside,distance,false);if(PlanarValid(outside))output.Add(outside);inside=Half(inside,distance,true);if(!PlanarValid(inside))break;}
return output;}
static Mesh DryWater(MeshFilter f,Data data,List<Mask> masks){
var world=data.p.Select(f.transform.TransformPoint).ToArray();var result=new List<V>();
for(int i=0;i<data.ids.Length;i+=3){var poly=new List<V>();for(int j=0;j<3;j++){int k=data.ids[i+j];poly.Add(new V(world[k],data.uv[k],data.uv1[k]));}
var pieces=new List<List<V>>{poly};foreach(var mask in masks){var next=new List<List<V>>();foreach(var p in pieces)next.AddRange(Subtract(p,mask));pieces=next;if(pieces.Count>40000)throw new Exception("Water clipping exceeded safe complexity.");}
foreach(var piece in pieces)Fan(piece,result);}
return BuildMesh("City Dry Land Water",result,f.transform,data.second);}
static Mesh Save(Mesh m,string name){string path=Out+"/PathwayContours/"+name+".asset";m.name=name;var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(old){EditorUtility.CopySerialized(m,old);UnityEngine.Object.DestroyImmediate(m);EditorUtility.SetDirty(old);return old;}AssetDatabase.CreateAsset(m,path);return m;}
static void Capture(Scene scene,Vector3 p,Vector3 target,string name){
var go=new GameObject("Pathway Review Camera"){hideFlags=HideFlags.HideAndDontSave};SceneManager.MoveGameObjectToScene(go,scene);var camera=go.AddComponent<Camera>();camera.enabled=false;camera.fieldOfView=62;camera.nearClipPlane=.2f;camera.farClipPlane=4000;camera.transform.SetPositionAndRotation(p,Quaternion.LookRotation(target-p));
var rt=RenderTexture.GetTemporary(1400,1000,24);var previous=RenderTexture.active;try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var tex=new Texture2D(1400,1000,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1400,1000),0,0);tex.Apply();File.WriteAllBytes("FinalEnvironmentRoadReview/"+name+".png",tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);}finally{camera.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(go);}}
static void Run(){
var log=new StringBuilder();var changes=new List<Change>();MeshFilter water=null;Mesh waterBefore=null;CityDayNightController lighting=null;bool night=false,committed=false;
try{
var scene=SceneManager.GetSceneByPath(ScenePath);if(!scene.isLoaded)throw new Exception("Final Environment is not loaded.");var root=scene.GetRootGameObjects().Single(g=>g.name=="Track Roads");
if(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root)!=PrefabPath)throw new Exception("Wrong scene road prefab.");
Directory.CreateDirectory("FinalEnvironmentRoadBackup");Directory.CreateDirectory("FinalEnvironmentRoadReview");string stamp=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
if(!EditorSceneManager.SaveScene(scene,"FinalEnvironmentRoadBackup/BeforePathwayFit-"+stamp+".unity",true))throw new Exception("Current scene backup failed.");File.Copy(PrefabPath,"FinalEnvironmentRoadBackup/BeforePathwayFit-"+stamp+".prefab");
var model=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Scenes/Oasis/3D/Fbx/Roads.fbx");var sources=model.GetComponentsInChildren<MeshFilter>(true).ToDictionary(f=>f.name,f=>f);
profiles.Clear();neighbors.Clear();var filters=root.GetComponentsInChildren<MeshFilter>(true);
foreach(string name in RoadNames){var f=filters.Single(v=>v.name==name);var d=Read(sources[name].sharedMesh);var w=d.p.Select(f.transform.TransformPoint).ToArray();var b=new Bounds(w[0],Vector3.zero);foreach(var p in w.Skip(1))b.Encapsulate(p);bool x=b.size.x>b.size.z;
profiles.Add(new Profile{x=x,a=Long(b.min,x),b=Long(b.max,x),c=Side(b.center,x),half=Mathf.Min(b.size.x,b.size.z)*.5f,y=b.center.y});}
foreach(var f in filters.Where(f=>f.sharedMesh&&f.sharedMesh.name.StartsWith("Visible_"))){var original=Read(sources[f.name].sharedMesh);var current=Read(f.sharedMesh);
neighbors.Add(new Floor{before=original.p.Select(f.transform.TransformPoint).ToArray(),after=current.p.Select(f.transform.TransformPoint).ToArray(),original=original.ids,current=current.ids,bounds=f.GetComponent<Renderer>().bounds});}
Directory.CreateDirectory(Out+"/PathwayContours");AssetDatabase.Refresh();
var pathwayCandidates=filters.Where(f=>f.sharedMesh&&f.GetComponent<Renderer>()&&f.GetComponent<Renderer>().sharedMaterials.Any(m=>m&&m.name=="Pathways")&&Affected(f.GetComponent<Renderer>().bounds)).ToArray();
if(pathwayCandidates.Any(f=>f.sharedMesh.name.StartsWith("Fitted_")))throw new Exception("Pathways have already been fitted.");
int foundations=0;var land=new List<Tuple<MeshFilter,Data>>();
foreach(var f in filters.Where(f=>f.sharedMesh&&f.GetComponent<Renderer>()&&Affected(f.GetComponent<Renderer>().bounds)&&f.GetComponent<Renderer>().sharedMaterials.Any(m=>m&&(m.name=="Road Atlas"||m.name=="Pathways")))){
var source=sources.TryGetValue(f.name,out var sourceFilter)?sourceFilter.sharedMesh:f.sharedMesh;land.Add(Tuple.Create(f,Read(source)));}
foreach(var f in pathwayCandidates){var data=Read(f.sharedMesh);var mesh=Pathway(f,data,out int walls);foundations+=walls;mesh=Save(mesh,"Fitted_"+f.name);changes.Add(new Change{f=f,before=f.sharedMesh,after=mesh});}
if(changes.Count==0)throw new Exception("No pathway surfaces found.");
water=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshFilter>(true)).Single(f=>f.name=="Water"&&f.GetComponent<Renderer>().sharedMaterial.shader.name.Contains("Water"));waterBefore=water.sharedMesh;
var waterData=Read(waterBefore);var masks=Masks(land);var dry=Save(DryWater(water,waterData,masks),"Water Outside Adjusted Land");
var dryData=Read(dry);var dryWorld=dryData.p.Select(water.transform.TransformPoint).ToArray();int dryProbes=0;
foreach(var field in profiles)for(float t=.55f;t<.96f;t+=.04f){var p=field.x?new Vector3(Mathf.Lerp(field.a,field.b,t),field.y,field.c):new Vector3(field.c,field.y,Mathf.Lerp(field.a,field.b,t));if(Height(dryWorld,dryData.ids,p,out _))throw new Exception("Water remains beneath an adjusted road dip.");dryProbes++;}
foreach(var change in changes){change.f.sharedMesh=change.after;if(!change.f.GetComponent<MeshCollider>())change.f.gameObject.AddComponent<MeshCollider>();foreach(var col in change.f.GetComponents<MeshCollider>()){col.sharedMesh=null;col.sharedMesh=change.after;PrefabUtility.RecordPrefabInstancePropertyModifications(col);}PrefabUtility.RecordPrefabInstancePropertyModifications(change.f);}
water.sharedMesh=dry;PrefabUtility.RecordPrefabInstancePropertyModifications(water);foreach(var col in water.GetComponents<MeshCollider>()){col.sharedMesh=dry;PrefabUtility.RecordPrefabInstancePropertyModifications(col);}
Physics.SyncTransforms();int floorProbes=0;
foreach(var change in changes){var d=Read(change.after);var world=d.p.Select(change.f.transform.TransformPoint).ToArray();var collider=change.f.GetComponent<MeshCollider>();
for(int i=0;i<d.ids.Length;i+=Mathf.Max(3,d.ids.Length/16/3*3)){var a=world[d.ids[i]];var b=world[d.ids[i+1]];var c=world[d.ids[i+2]];if(Vector3.Cross(b-a,c-a).normalized.y<.8f)continue;var p=(a+b+c)/3;if(!collider.Raycast(new Ray(p+Vector3.up,Vector3.down),out _,2))throw new Exception("Pathway collision mismatch "+change.f.name);floorProbes++;}}
var prefab=PrefabUtility.LoadPrefabContents(PrefabPath);try{foreach(var change in changes){var f=prefab.GetComponentsInChildren<MeshFilter>(true).Single(v=>v.name==change.f.name);f.sharedMesh=change.after;if(!f.GetComponent<MeshCollider>())f.gameObject.AddComponent<MeshCollider>();foreach(var col in f.GetComponents<MeshCollider>())col.sharedMesh=change.after;}
PrefabUtility.SaveAsPrefabAsset(prefab,PrefabPath,out bool ok);if(!ok)throw new Exception("Pathway prefab save failed.");committed=true;}finally{PrefabUtility.UnloadPrefabContents(prefab);}
AssetDatabase.SaveAssets();
lighting=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CityDayNightController>(true)).FirstOrDefault();night=lighting&&lighting.IsNight;if(lighting)lighting.ApplyDay();
for(int i=0;i<3;i++){var field=profiles[i==2?4:i];var dir=field.x?Vector3.right:Vector3.forward;var side=Vector3.Cross(Vector3.up,dir);
var p=field.x?new Vector3(Mathf.Lerp(field.a,field.b,.65f),field.y,field.c):new Vector3(field.c,field.y,Mathf.Lerp(field.a,field.b,.65f));
var eye=p+side*(field.half-3)-dir*22+Vector3.up*6;Capture(scene,eye,p+side*(field.half+2)+dir*18,"Fitted-Pathway-"+(i+1));}
if(lighting){if(night)lighting.ApplyNight();else lighting.ApplyDay();}EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new Exception("Scene save failed.");
log.AppendLine("SUCCESS. Pathway meshes fitted="+changes.Count+"; closed foundation edge segments="+foundations+"; pathway collider probes="+floorProbes+"; dry road water checks="+dryProbes+"; footprint masks="+masks.Count+".");
log.AppendLine("Approved road contour meshes unchanged. Existing pathway materials and UV channels preserved. Water geometry clipped only under affected ground footprints; surrounding water level and material unchanged. Saved to Final Roads prefab and Final Environment scene.");
}catch(Exception e){if(!committed){foreach(var change in changes){change.f.sharedMesh=change.before;foreach(var col in change.f.GetComponents<MeshCollider>())col.sharedMesh=change.before;}if(water&&waterBefore)water.sharedMesh=waterBefore;}log.AppendLine("FAILED "+e+"; committed="+committed);Debug.LogException(e);}
finally{if(lighting&&night)lighting.ApplyNight();File.WriteAllText("FinalEnvironmentRoadReview/PathwayRepairReport.txt",log.ToString());}
}
}



