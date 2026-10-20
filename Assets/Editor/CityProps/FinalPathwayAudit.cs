using System;using System.IO;using System.Linq;using System.Text;using UnityEngine;using UnityEngine.SceneManagement;using UnityEditor;
[InitializeOnLoad]public static class FinalPathwayAudit{
static FinalPathwayAudit(){EditorApplication.update+=Poll;}
static void Poll(){if(!File.Exists("FinalPathwayAudit.request")||EditorApplication.isCompiling||EditorApplication.isPlayingOrWillChangePlaymode)return;try{File.Delete("FinalPathwayAudit.request");}catch(IOException){return;}var log=new StringBuilder();try{
var scene=SceneManager.GetSceneByPath("Assets/Editor/DayNightBackups/Gangstar Final Env.unity");log.AppendLine("SCENE "+scene.path+" loaded="+scene.isLoaded+" dirty="+scene.isDirty);
var sv=SceneView.lastActiveSceneView;if(sv&&sv.camera)log.AppendLine("VIEW pos="+sv.camera.transform.position.ToString("F4")+" rotation="+sv.camera.transform.eulerAngles.ToString("F4")+" pivot="+sv.pivot.ToString("F4"));
foreach(var f in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshFilter>(true)).Where(f=>f.sharedMesh&&f.GetComponent<Renderer>())){
var r=f.GetComponent<Renderer>();string mats=string.Join("|",r.sharedMaterials.Select(m=>m?m.name+" ["+m.shader.name+"]":"null"));
if(f.name!="Canal1"&&f.name!="Outer_MEsh"&&!mats.ToLower().Contains("pathway")&&!mats.ToLower().Contains("water")&&!mats.ToLower().Contains("ocean"))continue;
log.AppendLine("SURFACE "+f.name+" path="+Path(f.transform)+" mesh="+f.sharedMesh.name+" vertices="+f.sharedMesh.vertexCount+" readable="+f.sharedMesh.isReadable+" center="+r.bounds.center.ToString("F3")+" size="+r.bounds.size.ToString("F3")+" materials="+mats+" meshAsset="+AssetDatabase.GetAssetPath(f.sharedMesh));
}
}catch(Exception e){log.AppendLine("FAILED "+e);}File.WriteAllText("FinalEnvironmentRoadReview/PathwayAudit.txt",log.ToString());}
static string Path(Transform t){return t.parent?Path(t.parent)+"/"+t.name:t.name;}
}

