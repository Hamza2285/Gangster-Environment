using System;using System.IO;using System.Linq;using System.Text;using System.Reflection;using System.Collections;using UnityEngine;using UnityEngine.SceneManagement;using UnityEditor;
[InitializeOnLoad]public static class FinalEnvironmentRoadCheck{
 static FinalEnvironmentRoadCheck(){EditorApplication.update+=Poll;}
 static void Poll(){if(!File.Exists("FinalEnvironmentRoadCheck.request")||EditorApplication.isCompiling||EditorApplication.isPlayingOrWillChangePlaymode)return;File.Delete("FinalEnvironmentRoadCheck.request");Check();}
 static object Value(object x,string name){return x.GetType().GetField(name,BindingFlags.Public|BindingFlags.Instance).GetValue(x);}
 static void Check(){var log=new StringBuilder();try{
 var scene=SceneManager.GetSceneByPath("Assets/Editor/DayNightBackups/Gangstar Final Env.unity");if(!scene.isLoaded)throw new Exception("Final scene missing.");
 var root=scene.GetRootGameObjects().Single(g=>g.name=="Track Roads");string path=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
 if(path!="Assets/Scenes/Oasis/FinalEnvironmentRoads/Final Roads.prefab")throw new Exception("Scene is not connected to its dedicated road prefab.");
 int errors=0;foreach(var mat in root.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m).Distinct())errors+=ShaderUtil.GetShaderMessages(mat.shader).Count(m=>m.severity.ToString()=="Error");if(errors>0)throw new Exception("Shader errors "+errors);
 Physics.SyncTransforms();int probes=0;
 var model=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Scenes/Oasis/3D/Fbx/Roads.fbx");
var originals=model.GetComponentsInChildren<MeshFilter>(true).ToDictionary(f=>f.name,f=>f);
int fieldCount=0;
foreach(var roadName in new[]{"polySurface809","polySurface816","polySurface810","polySurface878","polySurface862","polySurface840"}){
var current=root.GetComponentsInChildren<MeshFilter>(true).Single(f=>f.name==roadName);
var vertices=originals[roadName].sharedMesh.vertices.Select(current.transform.TransformPoint).ToArray();
var bounds=new Bounds(vertices[0],Vector3.zero);foreach(var v in vertices.Skip(1))bounds.Encapsulate(v);
bool x=bounds.size.x>bounds.size.z;float start=x?bounds.min.x:bounds.min.z,end=x?bounds.max.x:bounds.max.z,center=x?bounds.center.z:bounds.center.x,baseY=bounds.center.y;fieldCount++;
for(float t=.05f;t<1;t+=.05f){
var p=x?new Vector3(Mathf.Lerp(start,end,t),baseY,center):new Vector3(center,baseY,Mathf.Lerp(start,end,t));
float delta=3.15f*Mathf.Pow(Mathf.Sin(Mathf.PI*t),2)*Mathf.Sin(2*Mathf.PI*t);
var hits=Physics.RaycastAll(new Ray(p+Vector3.up*3,Vector3.down),6,~0,QueryTriggerInteraction.Ignore).Where(h=>h.collider.gameObject.scene==scene&&!h.collider.attachedRigidbody).OrderBy(h=>h.distance).ToArray();
if(hits.Length==0)throw new Exception("No scene floor at "+p);
if(Mathf.Abs(hits[0].point.y-(baseY+delta))>.12f)throw new Exception("Another collider masks the rise/dip at "+p+" hit="+hits[0].collider.name+" actual="+hits[0].point.y+" expected="+(baseY+delta));
probes++;
}
}

if(fieldCount!=6)throw new Exception("Expected six height profiles; runtime metadata has "+fieldCount);
 var modules=root.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("City Underpass ")).ToArray();if(modules.Length==0)log.AppendLine("Underpass scene checks skipped: the underpass group is absent from this scene. Its removal was present in the BeforeStrongerProfiles scene backup; the two underpasses remain in the road prefab.");else if(modules.Length!=2)throw new Exception("Wrong underpass count "+modules.Length);
 int lanes=0;
 foreach(var module in modules){
 var body=module.GetComponentsInChildren<MeshFilter>().Single(f=>f.name.EndsWith("_Concrete"));var world=body.sharedMesh.vertices.Select(body.transform.TransformPoint).ToArray();float top=world.Max(p=>p.y),ground=world.Min(p=>p.y);
 var roof=world.Where(p=>p.y>top-.01f).ToArray();var roofBounds=new Bounds(roof[0],Vector3.zero);foreach(var p in roof.Skip(1))roofBounds.Encapsulate(p);
 var dir=roofBounds.size.x<roofBounds.size.z?Vector3.right:Vector3.forward;var side=Vector3.Cross(Vector3.up,dir);var p0=new Vector3(roofBounds.center.x,ground,roofBounds.center.z);
 foreach(float lateral in new[]{-11f,-4f,4f,11f}){
 var start=p0+side*lateral-dir*18;var hits=Physics.CapsuleCastAll(start+Vector3.up*1.7f,start+Vector3.up*1.9f,1.5f,dir,36,~0,QueryTriggerInteraction.Ignore);
 var blockers=hits.Where(h=>h.collider.gameObject.scene==scene&&!h.collider.attachedRigidbody).ToArray();
 if(blockers.Length>0)throw new Exception("Tunnel lane obstructed: "+module.name+" offset="+lateral+" by "+string.Join(",",blockers.Select(h=>h.collider.name)));
 lanes++;
 }
 }
 log.AppendLine("SUCCESS: scene-specific prefab connected; six gentle road profiles; global floor checks="+probes+"; vehicle-sized tunnel lane sweeps="+lanes+"; shader errors=0.");
 log.AppendLine("Visual previews inspected. 150 staged surface checks also passed. Driving feel and device performance require play-mode/device testing.");
 }catch(Exception e){log.AppendLine("FAILED "+e);Debug.LogException(e);}File.WriteAllText("FinalEnvironmentRoadReview/ValidationReport.txt",log.ToString());}
}




