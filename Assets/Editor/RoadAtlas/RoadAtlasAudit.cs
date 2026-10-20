using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
[InitializeOnLoad]
public static class RoadAtlasAudit {
 static RoadAtlasAudit() { EditorApplication.delayCall += Run; }
 [MenuItem("Tools/Road Atlas/Audit")]
 public static void Run() {
  if (File.Exists("RoadAtlasAudit.txt") || EditorApplication.isPlayingOrWillChangePlaymode) return;
  try {
   var b = new StringBuilder();
   for(int s=0;s<SceneManager.sceneCount;s++) {var sc=SceneManager.GetSceneAt(s); b.AppendLine("SCENE "+sc.path+" dirty="+sc.isDirty);}
   var scene=SceneManager.GetSceneByPath("Assets/Scenes/Oasis/Gangstar City.unity");
   if(!scene.IsValid() || !scene.isLoaded) throw new Exception("Target scene is not loaded");
   var roads=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="Track Roads");
   foreach(var g in roads.GetComponentsInChildren<MeshRenderer>(true).GroupBy(r=>string.Join("|",r.sharedMaterials.Select(m=>m?AssetDatabase.GetAssetPath(m):"NULL")))) {
    b.AppendLine("MATERIALS "+g.Key+" renderers="+g.Count());
    foreach(var m in g.First().sharedMaterials) if(m) { b.AppendLine(" shader="+m.shader.name+" keywords="+string.Join(",",m.shaderKeywords)); foreach(var p in new[]{"_BaseMap","_MetallicGlossMap","_BumpMap","_EmissionMap"}) {var t=m.GetTexture(p); b.AppendLine(" "+p+"="+(t?AssetDatabase.GetAssetPath(t)+" "+t.width+"x"+t.height:"null"));} }
    var uv=g.SelectMany(r=>r.GetComponent<MeshFilter>().sharedMesh.uv).ToArray();
    b.AppendLine(" UV "+uv.Min(v=>v.x)+","+uv.Min(v=>v.y)+" -> "+uv.Max(v=>v.x)+","+uv.Max(v=>v.y));
    b.AppendLine(" vertices="+g.Sum(r=>r.GetComponent<MeshFilter>().sharedMesh.vertexCount)+" submeshes="+g.Sum(r=>r.GetComponent<MeshFilter>().sharedMesh.subMeshCount));
   }
   File.WriteAllText("RoadAtlasAudit.txt",b.ToString()); Debug.Log("Road atlas audit complete");
  } catch(Exception e) {File.WriteAllText("RoadAtlasAudit.txt",e.ToString()); Debug.LogException(e);}
 }
}
