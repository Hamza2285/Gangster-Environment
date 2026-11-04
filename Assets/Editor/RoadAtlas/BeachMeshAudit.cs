using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
[InitializeOnLoad]
public static class BeachMeshAudit {
 [Serializable] public class Data {public string name;public Vector3[] vertices; public Vector3[] normals;public Vector2[] uv;public int[] triangles;}
 static BeachMeshAudit(){EditorApplication.delayCall+=Run;}
 static void Run(){
  if(File.Exists("BeachMeshAudit.txt"))return;
  var log=new StringBuilder();
  try {
   Directory.CreateDirectory("BeachReview");
   var scene=SceneManager.GetSceneByPath("Assets/Scenes/Oasis/Gangstar City.unity");
   log.AppendLine("Scene loaded="+scene.isLoaded+", dirty="+scene.isDirty);
   var meshes=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshFilter>(true)).Where(f=>f.name=="Canal1"||f.name=="Outer_MEsh").ToArray();
   foreach(var f in meshes){
    var m=f.sharedMesh;var r=f.GetComponent<MeshRenderer>();
    var vertices=m.vertices.Select(v=>f.transform.TransformPoint(v)).ToArray();
    var normals=m.normals.Select(n=>f.transform.TransformDirection(n)).ToArray();
    log.AppendLine(f.name+" mesh="+AssetDatabase.GetAssetPath(m)+" vertices="+m.vertexCount+" triangles="+m.triangles.Length/3+" bounds="+r.bounds);
    log.AppendLine("materials="+string.Join(",",r.sharedMaterials.Select(mat=>AssetDatabase.GetAssetPath(mat))));
    log.AppendLine("UV: "+m.uv.Min(v=>v.x)+","+m.uv.Min(v=>v.y)+" -> "+m.uv.Max(v=>v.x)+","+m.uv.Max(v=>v.y));
    log.AppendLine("Y levels: "+string.Join("; ",vertices.GroupBy(v=>Mathf.Round(v.y*10)/10).OrderBy(g=>g.Key).Select(g=>g.Key+":"+g.Count())));
    File.WriteAllText("BeachReview/"+f.name+".json",JsonUtility.ToJson(new Data{name=f.name,vertices=vertices,normals=normals,uv=m.uv,triangles=m.triangles}));
   }
   if(meshes.Length!=2)throw new Exception("Expected two exact target meshes.");
  }catch(Exception e){log.AppendLine(e.ToString());}
  File.WriteAllText("BeachMeshAudit.txt",log.ToString());
 }
}
