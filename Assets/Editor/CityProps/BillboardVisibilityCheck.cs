using System;using System.IO;using System.Linq;using UnityEngine;using UnityEditor;using UnityEditor.SceneManagement;
public static class BillboardVisibilityCheck{
[InitializeOnLoadMethod]static void Init(){EditorApplication.delayCall+=Run;}
static void Run(){
if(!File.Exists("BillboardVisibility.request"))return;File.Delete("BillboardVisibility.request");
Directory.CreateDirectory("BuildingBillboardReview");
try{
var c=UnityEngine.Object.FindObjectOfType<CityDayNightController>(true);
var root=UnityEngine.Object.FindObjectsOfType<Transform>(true).FirstOrDefault(t=>t.name=="Building Billboards - Sparse"&&t.gameObject.scene==c.gameObject.scene);
if(!root)throw new Exception("No billboard group in the open scene");
var lines=new System.Collections.Generic.List<string>{"Scene: "+c.gameObject.scene.path,"Children: "+root.childCount};
foreach(Transform tr in root){
var r=tr.GetComponent<MeshRenderer>();var m=tr.GetComponent<MeshFilter>();var lod=tr.GetComponent<LODGroup>();
lines.Add(tr.name+" active="+tr.gameObject.activeInHierarchy+" renderer="+r.enabled+" forcedOff="+r.forceRenderingOff+" mesh="+(m.sharedMesh?m.sharedMesh.vertexCount:0)+" bounds="+r.bounds+" material="+r.sharedMaterial.name+" lod="+lod.GetLODs()[0].screenRelativeTransitionHeight+" hidden="+SceneVisibilityManager.instance.IsHidden(tr.gameObject));
}
File.WriteAllLines("BuildingBillboardReview/VisibilityCurrent.txt",lines);
var target=root.GetChild(3);var pos=target.position+target.forward*55+Vector3.up*3;
CityLightingBuilder.Capture(c,Tuple.Create(pos,Quaternion.LookRotation(target.position-pos)),"Billboard-Current");
}catch(Exception e){File.WriteAllText("BuildingBillboardReview/VisibilityCurrent.txt",e.ToString());Debug.LogException(e);}
}}
