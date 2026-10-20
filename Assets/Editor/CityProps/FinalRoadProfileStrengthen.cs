using System;using System.IO;using System.Linq;using System.Text;using System.Collections.Generic;using UnityEngine;using UnityEngine.SceneManagement;using UnityEditor;using UnityEditor.SceneManagement;
[InitializeOnLoad]public static class FinalRoadProfileStrengthen{
const string ScenePath="Assets/Editor/DayNightBackups/Gangstar Final Env.unity",Out="Assets/Scenes/Oasis/FinalEnvironmentRoads",PrefabPath=Out+"/Final Roads.prefab";
static readonly string[] Names={"polySurface809","polySurface816","polySurface810","polySurface878","polySurface862","polySurface840"};
static FinalRoadProfileStrengthen(){EditorApplication.update+=Poll;}
static void Poll(){if(!File.Exists("FinalRoadProfileStrengthen.request")||EditorApplication.isCompiling||EditorApplication.isPlayingOrWillChangePlaymode)return;try{File.Delete("FinalRoadProfileStrengthen.request");}catch(IOException){return;}Run();}
class Profile{public bool x;public float start,end,center,half,y;public Bounds bounds;}
class Change{public MeshFilter filter;public Mesh before,after;}
static List<Profile> profiles=new List<Profile>();
static float Long(Vector3 p,bool x){return x?p.x:p.z;}static float Side(Vector3 p,bool x){return x?p.z:p.x;}
static float Shape(Vector3 p,bool gate=true){float result=0;foreach(var f in profiles){
float t=(Long(p,f.x)-f.start)/(f.end-f.start);if(t<=0||t>=1||(gate&&Mathf.Abs(p.y-f.y)>1))continue;
float extra=Mathf.Abs(Side(p,f.x)-f.center)-f.half;if(extra>=7)continue;float weight=extra<=0?1:.5f+.5f*Mathf.Cos(Mathf.PI*extra/7);
result+=Mathf.Pow(Mathf.Sin(Mathf.PI*t),2)*Mathf.Sin(2*Mathf.PI*t)*weight;}return result;}
static float OriginalHeight(Vector3 p,Vector3[] verts,int[] ids){
float closest=float.PositiveInfinity,height=p.y-1.05f*Shape(p,false);
for(int i=0;i<ids.Length;i+=3){var a=verts[ids[i]];var b=verts[ids[i+1]];var c=verts[ids[i+2]];
if(p.x<Mathf.Min(a.x,b.x,c.x)-.002f||p.x>Mathf.Max(a.x,b.x,c.x)+.002f||p.z<Mathf.Min(a.z,b.z,c.z)-.002f||p.z>Mathf.Max(a.z,b.z,c.z)+.002f)continue;
float d=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);if(Mathf.Abs(d)<.000001f)continue;
float u=((b.z-c.z)*(p.x-c.x)+(c.x-b.x)*(p.z-c.z))/d,v=((c.z-a.z)*(p.x-c.x)+(a.x-c.x)*(p.z-c.z))/d;
if(u<-.001f||v<-.001f||u+v>1.001f)continue;float h=u*a.y+v*b.y+(1-u-v)*c.y;float diff=Mathf.Abs(h-p.y);
if(diff<closest){closest=diff;height=h;}}
return height;}
static void Capture(Scene scene,Vector3 p,Vector3 target,string name){
var g=new GameObject("Road Profile Preview"){hideFlags=HideFlags.HideAndDontSave};SceneManager.MoveGameObjectToScene(g,scene);var c=g.AddComponent<Camera>();c.enabled=false;c.fieldOfView=62;c.nearClipPlane=.2f;c.farClipPlane=4000;c.transform.SetPositionAndRotation(p,Quaternion.LookRotation(target-p));
var rt=RenderTexture.GetTemporary(1400,1000,24);var prev=RenderTexture.active;
try{c.targetTexture=rt;c.Render();RenderTexture.active=rt;var tex=new Texture2D(1400,1000,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1400,1000),0,0);tex.Apply();File.WriteAllBytes("FinalEnvironmentRoadReview/"+name+".png",tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);}
finally{c.targetTexture=null;RenderTexture.active=prev;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(g);}}
static void Run(){
var log=new StringBuilder();var changes=new List<Change>();CityDayNightController light=null;bool night=false,saved=false;
try{
var scene=SceneManager.GetSceneByPath(ScenePath);if(!scene.isLoaded)throw new Exception("Final Environment must be loaded.");
var root=scene.GetRootGameObjects().Single(g=>g.name=="Track Roads");
if(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root)!=PrefabPath)throw new Exception("Unexpected road prefab.");
var filters=root.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh&&f.sharedMesh.name.StartsWith("Gentle_")).ToArray();
if(filters.Length!=16)throw new Exception("Expected the original sixteen gentle road/pavement meshes; found "+filters.Length);
Directory.CreateDirectory("FinalEnvironmentRoadBackup");Directory.CreateDirectory("FinalEnvironmentRoadReview");
string stamp=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
if(!EditorSceneManager.SaveScene(scene,"FinalEnvironmentRoadBackup/BeforeStrongerProfiles-"+stamp+".unity",true))throw new Exception("Scene backup failed.");
File.Copy(PrefabPath,"FinalEnvironmentRoadBackup/BeforeStrongerProfiles-"+stamp+".prefab");
var model=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Scenes/Oasis/3D/Fbx/Roads.fbx");var sources=model.GetComponentsInChildren<MeshFilter>(true).ToDictionary(f=>f.name,f=>f);
profiles.Clear();foreach(var name in Names){
var f=filters.Single(v=>v.name==name);var original=sources[name].sharedMesh.vertices.Select(f.transform.TransformPoint).ToArray();var b=new Bounds(original[0],Vector3.zero);foreach(var v in original.Skip(1))b.Encapsulate(v);
bool x=b.size.x>b.size.z;profiles.Add(new Profile{x=x,start=Long(b.min,x),end=Long(b.max,x),center=Side(b.center,x),half=Mathf.Min(b.size.x,b.size.z)*.5f,y=b.center.y,bounds=b});
}
Directory.CreateDirectory(Out+"/MoreVisibleMeshes");AssetDatabase.Refresh();
foreach(var f in filters){
var original=sources[f.name].sharedMesh;var w=original.vertices.Select(f.transform.TransformPoint).ToArray();var ids=original.triangles;
var next=UnityEngine.Object.Instantiate(f.sharedMesh);next.name="Visible_"+f.name;var vertices=next.vertices;
for(int i=0;i<vertices.Length;i++){
var p=f.transform.TransformPoint(vertices[i]);var baseline=p;baseline.y=OriginalHeight(p,w,ids);
p.y+=(3.15f-1.05f)*Shape(baseline);vertices[i]=f.transform.InverseTransformPoint(p);}
next.vertices=vertices;next.RecalculateNormals();next.RecalculateTangents();next.RecalculateBounds();
string path=Out+"/MoreVisibleMeshes/"+f.name+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
if(existing){EditorUtility.CopySerialized(next,existing);UnityEngine.Object.DestroyImmediate(next);next=existing;EditorUtility.SetDirty(next);}else AssetDatabase.CreateAsset(next,path);
changes.Add(new Change{filter=f,before=f.sharedMesh,after=next});
}
foreach(var change in changes){change.filter.sharedMesh=change.after;foreach(var col in change.filter.GetComponents<MeshCollider>()){col.sharedMesh=null;col.sharedMesh=change.after;PrefabUtility.RecordPrefabInstancePropertyModifications(col);}PrefabUtility.RecordPrefabInstancePropertyModifications(change.filter);}
Physics.SyncTransforms();
// Verify actual whole-scene driving floors before saving the prefab.
int checks=0;foreach(var p in profiles)for(float t=.05f;t<1;t+=.05f){
var point=p.x?new Vector3(Mathf.Lerp(p.start,p.end,t),p.y,p.center):new Vector3(p.center,p.y,Mathf.Lerp(p.start,p.end,t));
float expected=point.y+3.15f*Shape(point);
var hits=Physics.RaycastAll(new Ray(point+Vector3.up*4,Vector3.down),8,~0,QueryTriggerInteraction.Ignore).Where(h=>h.collider.gameObject.scene==scene&&!h.collider.attachedRigidbody).OrderBy(h=>h.distance).ToArray();
if(hits.Length==0||Mathf.Abs(hits[0].point.y-expected)>.12f)throw new Exception("Scene collision masks the new profile at "+point+(hits.Length>0?" hit="+hits[0].collider.name:""));checks++;}
var prefab=PrefabUtility.LoadPrefabContents(PrefabPath);try{
foreach(var change in changes){var f=prefab.GetComponentsInChildren<MeshFilter>(true).Single(v=>v.name==change.filter.name);f.sharedMesh=change.after;foreach(var col in f.GetComponents<MeshCollider>())col.sharedMesh=change.after;}
PrefabUtility.SaveAsPrefabAsset(prefab,PrefabPath,out bool ok);if(!ok)throw new Exception("Prefab save failed");saved=true;
}finally{PrefabUtility.UnloadPrefabContents(prefab);}
int propsMoved=0;var propRoot=scene.GetRootGameObjects().FirstOrDefault(g=>g.name=="Roads Props");
if(propRoot)foreach(var prop in propRoot.GetComponentsInChildren<Renderer>(true).Select(r=>PrefabUtility.GetOutermostPrefabInstanceRoot(r.gameObject)).Where(g=>g).Distinct()){
var renderers=prop.GetComponentsInChildren<Renderer>(true);if(renderers.Length==0)continue;var b=renderers[0].bounds;foreach(var r in renderers.Skip(1))b.Encapsulate(r.bounds);
var p=new Vector3(b.center.x,b.min.y,b.center.z);p.y-=1.05f*Shape(p,false);float extra=2.10f*Shape(p);
if(Mathf.Abs(extra)<.002f)continue;prop.transform.position+=Vector3.up*extra;PrefabUtility.RecordPrefabInstancePropertyModifications(prop.transform);propsMoved++;}
AssetDatabase.SaveAssets();
light=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CityDayNightController>(true)).FirstOrDefault();night=light&&light.IsNight;if(light)light.ApplyDay();
var first=profiles[0];var dir=first.x?Vector3.right:Vector3.forward;var side=Vector3.Cross(Vector3.up,dir);
var eye=first.bounds.center; if(first.x)eye.x=Mathf.Lerp(first.start,first.end,.04f);else eye.z=Mathf.Lerp(first.start,first.end,.04f);
eye+=side*3;eye.y=first.y+3.15f*Shape(eye)+1.65f;
Capture(scene,eye,first.bounds.center+Vector3.up*.4f,"Gentle-Road");
Capture(scene,first.bounds.center-dir*100+side*12+Vector3.up*5,first.bounds.center,"Stronger-Road-Profiles");
if(light){if(night)light.ApplyNight();else light.ApplyDay();}
EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new Exception("Scene save failed.");
log.AppendLine("SUCCESS. Six road stretches now peak at approximately +2.05m and dip to -2.05m (previously +/-0.68m). Endpoints and tangent transitions unchanged.");
log.AppendLine("Mesh count, vertex count, road widths, materials, underpasses and realtime-light count preserved. Pavement reshaped; roadside props moved="+propsMoved+"; whole-scene floor checks="+checks+".");
}catch(Exception e){if(!saved)foreach(var change in changes){change.filter.sharedMesh=change.before;foreach(var col in change.filter.GetComponents<MeshCollider>())col.sharedMesh=change.before;}log.AppendLine("FAILED "+e+"; prefab saved="+saved);Debug.LogException(e);}
finally{if(light&&night)light.ApplyNight();File.WriteAllText("FinalEnvironmentRoadReview/StrongerProfilesReport.txt",log.ToString());}
}
}

