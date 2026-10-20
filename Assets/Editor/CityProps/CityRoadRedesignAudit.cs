using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
[InitializeOnLoad] public static class CityRoadRedesignAudit {
 static CityRoadRedesignAudit(){EditorApplication.update+=Poll;}
 static void Poll(){if(!File.Exists("CityRoadRedesignAudit.request")||EditorApplication.isCompiling||EditorApplication.isPlayingOrWillChangePlaymode)return;File.Delete("CityRoadRedesignAudit.request");try{
 var scene=SceneManager.GetSceneByPath("Assets/Scenes/Oasis/Gangstar City.unity");
 if(!scene.isLoaded)throw new Exception("City scene must be loaded.");
 var sb=new StringBuilder();sb.AppendLine("Scene="+scene.path+" dirty="+scene.isDirty);
 foreach(var root in scene.GetRootGameObjects())sb.AppendLine("ROOT "+root.name);
 var fs=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshFilter>(true)).Where(f=>f.sharedMesh&&f.GetComponent<Renderer>()).ToArray();
 foreach(var f in fs.Where(f=>f.GetComponent<Renderer>().sharedMaterial&&f.GetComponent<Renderer>().sharedMaterial.name=="Road Atlas"||f.name.ToLower().Contains("bridge"))){
 var m=f.sharedMesh;var r=f.GetComponent<Renderer>();var uv=m.uv;int layer=uv.Length>0?Mathf.FloorToInt(uv.Average(v=>v.x)/32):-1;
 sb.AppendLine("ROAD "+f.name+" layer="+layer+" center="+r.bounds.center.ToString("F3")+" size="+r.bounds.size.ToString("F3")+" vertices="+m.vertexCount+" prefab="+PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(f.gameObject));
 if(layer==0||layer==2)sb.AppendLine(" UV="+string.Join(";",uv.Take(12).Select(v=>v.ToString("F4")))+" WORLD="+string.Join(";",m.vertices.Take(12).Select(v=>f.transform.TransformPoint(v).ToString("F3"))));
 }
 foreach(var c in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MonoBehaviour>(true)).Where(c=>c&&(c.GetType().Name.Contains("RCC")||c.GetType().Name.Contains("CarController"))))sb.AppendLine("VEHICLE "+c.GetType().Name+" "+c.name+" scale="+c.transform.lossyScale);
 File.WriteAllText("CityRoadRedesignAudit.txt",sb.ToString());
 }catch(Exception e){File.WriteAllText("CityRoadRedesignAudit.txt","FAILED "+e);}}
}
