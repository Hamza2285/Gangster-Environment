using System;using System.IO;using System.Linq;using System.Text;using UnityEngine;using UnityEditor;using UnityEngine.SceneManagement;
[InitializeOnLoad] public static class FinalRoadSceneAudit {
static FinalRoadSceneAudit(){EditorApplication.update+=Poll;}
static void Poll(){if(!File.Exists("FinalRoadSceneAudit.request")||EditorApplication.isCompiling||EditorApplication.isPlayingOrWillChangePlaymode)return;File.Delete("FinalRoadSceneAudit.request");var log=new StringBuilder();try{
for(int i=0;i<SceneManager.sceneCount;i++){var s=SceneManager.GetSceneAt(i);log.AppendLine("LOADED "+s.path+" dirty="+s.isDirty);}
var scene=SceneManager.GetSceneByPath("Assets/Editor/DayNightBackups/Gangstar Final Env.unity");if(!scene.isLoaded)throw new Exception("Final scene is not loaded.");
var root=scene.GetRootGameObjects().Single(g=>g.name=="Track Roads");log.AppendLine("PREFAB "+PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root));
foreach(var t in root.GetComponentsInChildren<Transform>(true).Where(t=>t.name.Contains("Underpass")))log.AppendLine("UNDERPASS "+t.name+" active="+t.gameObject.activeInHierarchy);
foreach(var f in root.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh&&f.GetComponent<MeshRenderer>())){
var r=f.GetComponent<MeshRenderer>();if(!r.sharedMaterial||r.sharedMaterial.name!="Road Atlas")continue;var uv=f.sharedMesh.uv;
log.AppendLine("ROAD "+f.name+" mesh="+f.sharedMesh.name+" layer="+Mathf.FloorToInt(uv.Average(v=>v.x)/32)+" center="+r.bounds.center.ToString("F3")+" size="+r.bounds.size.ToString("F3")+" vertices="+f.sharedMesh.vertexCount+" colliders="+f.GetComponents<MeshCollider>().Length);}
}catch(Exception e){log.AppendLine("FAILED "+e);}File.WriteAllText("FinalRoadSceneAudit.txt",log.ToString());}}

