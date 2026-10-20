using System;using System.IO;using System.Linq;using System.Text;using System.Collections;using System.Collections.Generic;using System.Reflection;using UnityEngine;using UnityEngine.SceneManagement;using UnityEditor;using UnityEditor.SceneManagement;
[InitializeOnLoad]public static class FinalCanalJoinRepair{
const string ScenePath="Assets/Editor/DayNightBackups/Gangstar Final Env.unity",Out="Assets/Scenes/Oasis/FinalEnvironmentRoads",PrefabPath=Out+"/Final Roads.prefab";
static readonly string[] Names={"polySurface809","polySurface816","polySurface810","polySurface878","polySurface862","polySurface840"};
static Type helper=typeof(FinalPathwayRepair);static BindingFlags S=BindingFlags.NonPublic|BindingFlags.Static;
static FinalCanalJoinRepair(){EditorApplication.update+=Poll;}
static void Poll(){if(!File.Exists("FinalCanalJoinRepair.request")||EditorApplication.isCompiling||EditorApplication.isPlayingOrWillChangePlaymode)return;try{File.Delete("FinalCanalJoinRepair.request");}catch(IOException){return;}Run();}
class Profile{public bool x;public float a,b,c,half,y;}
class Change{public MeshFilter f;public Mesh old,next;public object raw;}
class Edge{public Vector3 a,b;public int count;}
static List<Profile> profiles=new List<Profile>();static Dictionary<Vector3Int,float> offsets=new Dictionary<Vector3Int,float>();
static object Call(string name,params object[] args){return helper.GetMethod(name,S).Invoke(null,args);}
static object Value(object o,string name){return o.GetType().GetField(name,BindingFlags.Public|BindingFlags.Instance).GetValue(o);}
static void Set(object o,string name,object value){o.GetType().GetField(name,BindingFlags.Public|BindingFlags.Instance).SetValue(o,value);}
static float Long(Vector3 p,bool x){return x?p.x:p.z;}static float Side(Vector3 p,bool x){return x?p.z:p.x;}
static Vector3Int Key(Vector3 p){return new Vector3Int(Mathf.RoundToInt(p.x*1000),Mathf.RoundToInt(p.y*1000),Mathf.RoundToInt(p.z*1000));}
static float ShoreDelta(Vector3 p){
var key=Key(p);if(offsets.TryGetValue(key,out float cached))return cached;float delta=0;bool nearGround=false;
foreach(var f in profiles){
float t=(Long(p,f.x)-f.a)/(f.b-f.a);if(t<=0||t>=1)continue;
float extra=Mathf.Abs(Side(p,f.x)-f.c)-f.half;if(extra>=7)continue;
float horizontal=extra<=0?1:.5f+.5f*Mathf.Cos(Mathf.PI*extra/7);
float vertical=p.y<f.y-1?Mathf.SmoothStep(0,1,Mathf.Clamp01((p.y-(f.y-8))/7)):p.y>f.y+1?Mathf.SmoothStep(1,0,Mathf.Clamp01(p.y-(f.y+1))):1;
delta+=3.15f*Mathf.Pow(Mathf.Sin(Mathf.PI*t),2)*Mathf.Sin(2*Mathf.PI*t)*horizontal*vertical;
if(Mathf.Abs(p.y-f.y)<=1)nearGround=true;
}
if(nearGround&&Mathf.Abs(delta)>.000001f){
var hook=helper.GetField("displacementOverride",S);var saved=hook.GetValue(null);hook.SetValue(null,null);
try{delta=(float)Call("Displacement",p);}finally{hook.SetValue(null,saved);}
}
offsets[key]=delta;return delta;
}
static bool Height(Vector3[] p,int[] ids,Vector3 point,out float y){var args=new object[]{p,ids,point,0f};bool ok=(bool)Call("Height",args);y=(float)args[3];return ok;}
static string Path(Transform t){return t.parent?Path(t.parent)+"/"+t.name:t.name;}
static void EdgeAdd(Dictionary<string,Edge> edges,Vector3 a,Vector3 b){
string ka=Key(a).ToString(),kb=Key(b).ToString();string key=string.CompareOrdinal(ka,kb)<0?ka+"|"+kb:kb+"|"+ka;
if(edges.TryGetValue(key,out var e))e.count++;else edges[key]=new Edge{a=a,b=b,count=1};
}
static void Run(){
var log=new StringBuilder();var changes=new List<Change>();MeshFilter water=null;Mesh oldWater=null;bool committed=false;CityDayNightController light=null;bool night=false;
try{
var scene=SceneManager.GetSceneByPath(ScenePath);if(!scene.isLoaded)throw new Exception("Final scene not loaded.");
var root=scene.GetRootGameObjects().Single(g=>g.name=="Track Roads");if(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root)!=PrefabPath)throw new Exception("Wrong road prefab.");
Directory.CreateDirectory("FinalEnvironmentRoadBackup");string stamp=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
if(!EditorSceneManager.SaveScene(scene,"FinalEnvironmentRoadBackup/BeforeCanalJoins-"+stamp+".unity",true))throw new Exception("Scene backup failed.");File.Copy(PrefabPath,"FinalEnvironmentRoadBackup/BeforeCanalJoins-"+stamp+".prefab");
var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshFilter>(true)).Where(f=>f.sharedMesh&&f.GetComponent<Renderer>()).ToArray();
var model=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Scenes/Oasis/3D/Fbx/Roads.fbx");var sources=model.GetComponentsInChildren<MeshFilter>(true).ToDictionary(f=>f.name,f=>f);
var sharedProfiles=(IList)helper.GetField("profiles",S).GetValue(null);sharedProfiles.Clear();profiles.Clear();offsets.Clear();
foreach(var name in Names){
var f=all.Single(v=>v.name==name);var raw=Call("Read",sources[name].sharedMesh);var p=((Vector3[])Value(raw,"p")).Select(f.transform.TransformPoint).ToArray();
var bounds=new Bounds(p[0],Vector3.zero);foreach(var v in p.Skip(1))bounds.Encapsulate(v);bool x=bounds.size.x>bounds.size.z;
var own=new Profile{x=x,a=Long(bounds.min,x),b=Long(bounds.max,x),c=Side(bounds.center,x),half=Mathf.Min(bounds.size.x,bounds.size.z)*.5f,y=bounds.center.y};profiles.Add(own);
var item=Activator.CreateInstance(sharedProfiles.GetType().GetGenericArguments()[0],true);Set(item,"x",x);Set(item,"a",own.a);Set(item,"b",own.b);Set(item,"c",own.c);Set(item,"half",own.half);Set(item,"y",own.y);sharedProfiles.Add(item);
}
var sharedFloors=(IList)helper.GetField("neighbors",S).GetValue(null);sharedFloors.Clear();
foreach(var f in all.Where(f=>f.sharedMesh.name.StartsWith("Visible_")||f.sharedMesh.name.StartsWith("Fitted_")).OrderBy(f=>f.sharedMesh.name.StartsWith("Fitted_")?0:1)){
if(!sources.TryGetValue(f.name,out var original))continue;var before=Call("Read",original.sharedMesh);var after=Call("Read",f.sharedMesh);
var floor=Activator.CreateInstance(sharedFloors.GetType().GetGenericArguments()[0],true);
Set(floor,"before",((Vector3[])Value(before,"p")).Select(f.transform.TransformPoint).ToArray());Set(floor,"after",((Vector3[])Value(after,"p")).Select(f.transform.TransformPoint).ToArray());
Set(floor,"original",Value(before,"ids"));Set(floor,"current",Value(after,"ids"));Set(floor,"bounds",f.GetComponent<Renderer>().bounds);sharedFloors.Add(floor);
}
var targets=all.Where(f=>f.name=="Canal1"||f.name=="Outer_MEsh").ToArray();if(targets.Length!=2)throw new Exception("Expected canal and outer shoreline meshes.");
if(targets.Any(f=>f.sharedMesh.name.StartsWith("Joined_")))throw new Exception("Shoreline joins already repaired; inspect before rerunning.");
int walls=0;var hook=helper.GetField("displacementOverride",S);hook.SetValue(null,(Func<Vector3,float>)ShoreDelta);
try{
foreach(var f in targets){
if(!f.transform.IsChildOf(root.transform))throw new Exception("Shore mesh is outside the road template: "+Path(f.transform));
var raw=Call("Read",f.sharedMesh);var args=new object[]{f,raw,0};var mesh=(Mesh)Call("Pathway",args);walls+=(int)args[2];
if(f.GetComponent<Renderer>().sharedMaterial.shader.name.Contains("Beach"))mesh.uv=mesh.vertices.Select(v=>f.transform.TransformPoint(v)).Select(p=>new Vector2(p.x,p.z)).ToArray();
mesh.RecalculateTangents();mesh=(Mesh)Call("Save",mesh,"Joined_"+f.name);
changes.Add(new Change{f=f,old=f.sharedMesh,next=mesh,raw=raw});log.AppendLine("SHORE "+Path(f.transform)+" original="+f.sharedMesh.name+" repaired vertices="+mesh.vertexCount);
}
}finally{hook.SetValue(null,null);}
var masksMethod=helper.GetMethod("Masks",S);var landType=masksMethod.GetParameters()[0].ParameterType;var land=(IList)Activator.CreateInstance(landType);var tupleType=landType.GetGenericArguments()[0];
foreach(var c in changes)land.Add(Activator.CreateInstance(tupleType,new object[]{c.f,c.raw}));
var masks=masksMethod.Invoke(null,new object[]{land});
water=all.Single(f=>f.name=="Water"&&f.GetComponent<Renderer>().sharedMaterial.shader.name.Contains("Water"));oldWater=water.sharedMesh;
var newWater=(Mesh)Call("DryWater",water,Call("Read",oldWater),masks);newWater=(Mesh)Call("Save",newWater,"Water Outside All Adjusted Shores");
int joinProbes=0;float maxError=0;
foreach(var c in changes){
var original=((Vector3[])Value(c.raw,"p")).Select(c.f.transform.TransformPoint).ToArray();var ids=(int[])Value(c.raw,"ids");
var after=c.next.vertices.Select(c.f.transform.TransformPoint).ToArray();var edges=new Dictionary<string,Edge>();
for(int i=0;i<ids.Length;i+=3){EdgeAdd(edges,original[ids[i]],original[ids[i+1]]);EdgeAdd(edges,original[ids[i+1]],original[ids[i+2]]);EdgeAdd(edges,original[ids[i+2]],original[ids[i]]);}
foreach(var edge in edges.Values.Where(e=>e.count==1&&e.a.y>26.9f&&e.a.y<28.1f&&e.b.y>26.9f&&e.b.y<28.1f&&Vector3.Distance(e.a,e.b)>.3f))for(int k=1;k<=3;k++){
var p=Vector3.Lerp(edge.a,edge.b,k*.25f);float delta=ShoreDelta(p);if(Mathf.Abs(delta)<.02f)continue;
if(!Height(original,ids,p,out float baseline)||Mathf.Abs(baseline-p.y)>.1f)continue;
var target=p+Vector3.up*delta;bool found=Height(after,c.next.triangles,target,out float h);
if(!found){var tangent=edge.b-edge.a;tangent.y=0;var inward=Vector3.Cross(Vector3.up,tangent.normalized);foreach(float sign in new[]{-1f,1f}){var candidate=p+inward*(sign*.02f);if(!Height(original,ids,candidate,out float floor)||Mathf.Abs(floor-p.y)>.1f)continue;candidate.y=floor;var expected=candidate+Vector3.up*ShoreDelta(candidate);if(Height(after,c.next.triangles,expected,out h)){target=expected;found=true;break;}}}
if(!found)throw new Exception("Shore edge has no fitted surface: "+c.f.name+" at "+p);
float error=Mathf.Abs(h-target.y);maxError=Mathf.Max(error,maxError);if(error>.12f)throw new Exception("Canal join mismatch "+c.f.name+" at "+p+" error="+error);joinProbes++;
}}
if(joinProbes==0)throw new Exception("No changed shoreline edge connections checked.");
foreach(var c in changes){c.f.sharedMesh=c.next;if(!c.f.GetComponent<MeshCollider>())c.f.gameObject.AddComponent<MeshCollider>();foreach(var col in c.f.GetComponents<MeshCollider>()){col.sharedMesh=null;col.sharedMesh=c.next;PrefabUtility.RecordPrefabInstancePropertyModifications(col);}PrefabUtility.RecordPrefabInstancePropertyModifications(c.f);}
water.sharedMesh=newWater;PrefabUtility.RecordPrefabInstancePropertyModifications(water);foreach(var col in water.GetComponents<MeshCollider>()){col.sharedMesh=newWater;PrefabUtility.RecordPrefabInstancePropertyModifications(col);}
Physics.SyncTransforms();
var prefab=PrefabUtility.LoadPrefabContents(PrefabPath);try{foreach(var c in changes){var f=prefab.GetComponentsInChildren<MeshFilter>(true).Single(v=>v.name==c.f.name);f.sharedMesh=c.next;if(!f.GetComponent<MeshCollider>())f.gameObject.AddComponent<MeshCollider>();foreach(var col in f.GetComponents<MeshCollider>())col.sharedMesh=c.next;}
PrefabUtility.SaveAsPrefabAsset(prefab,PrefabPath,out bool ok);if(!ok)throw new Exception("Shore prefab save failed.");committed=true;}finally{PrefabUtility.UnloadPrefabContents(prefab);}
AssetDatabase.SaveAssets();
light=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CityDayNightController>(true)).FirstOrDefault();night=light&&light.IsNight;if(light)light.ApplyDay();
var first=profiles[0];var direction=first.x?Vector3.right:Vector3.forward;var cross=Vector3.Cross(Vector3.up,direction);
var point=first.x?new Vector3(Mathf.Lerp(first.a,first.b,.7f),first.y,first.c):new Vector3(first.c,first.y,Mathf.Lerp(first.a,first.b,.7f));
Call("Capture",scene,point-cross*(first.half+14)-direction*30+Vector3.up*6,point-cross*(first.half+2)+direction*18,"Joined-Canal-Promenade");
var view=SceneView.lastActiveSceneView;if(view&&view.camera)Call("Capture",scene,view.camera.transform.position,view.camera.transform.position+view.camera.transform.forward*50,"Joined-Canal-Current-View");
if(light){if(night)light.ApplyNight();else light.ApplyDay();}EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new Exception("Scene save failed.");
log.AppendLine("SUCCESS. Canal1 and Outer_MEsh fitted to the approved road/pavement contours. Closed supporting edge segments="+walls+"; shoreline join probes="+joinProbes+"; maximum connection interpolation error="+maxError.ToString("F4")+"m.");
log.AppendLine("Road and repaired pathway meshes preserved. Existing shoreline textures and UV maps retained. Water exclusion extended only beneath affected shoreline land. Saved to the Final Roads prefab and Final Environment scene.");
}catch(Exception e){if(!committed){foreach(var c in changes){c.f.sharedMesh=c.old;foreach(var col in c.f.GetComponents<MeshCollider>())col.sharedMesh=c.old;}if(water&&oldWater)water.sharedMesh=oldWater;}log.AppendLine("FAILED "+e+"; committed="+committed);Debug.LogException(e);}
finally{helper.GetField("displacementOverride",S).SetValue(null,null);if(light&&night)light.ApplyNight();File.WriteAllText("FinalEnvironmentRoadReview/CanalJoinReport.txt",log.ToString());}
}
}


