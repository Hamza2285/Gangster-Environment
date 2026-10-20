using System;using System.IO;using UnityEngine;using UnityEditor;
public static class NightTargetAudit{
[InitializeOnLoadMethod]static void Init(){EditorApplication.delayCall+=Run;}
static void Run(){if(!File.Exists("NightTarget.request"))return;File.Delete("NightTarget.request");
var shop=GameObject.Find("Circus_Circus_0").GetComponentInChildren<MeshFilter>();
var mesh=shop.sharedMesh;var verts=mesh.vertices;var uv=mesh.uv;var tris=mesh.triangles;
File.WriteAllText("RestaurantUV.txt","");
for(int i=0;i<tris.Length;i+=3){
var a=shop.transform.TransformPoint(verts[tris[i]]);var b=shop.transform.TransformPoint(verts[tris[i+1]]);var d=shop.transform.TransformPoint(verts[tris[i+2]]);
var norm=Vector3.Cross(b-a,d-a).normalized;
if(Mathf.Abs(norm.y)<.25f&&(a.y+b.y+d.y)/3<35)
File.AppendAllText("RestaurantUV.txt",uv[tris[i]]*1600+" "+uv[tris[i+1]]*1600+" "+uv[tris[i+2]]*1600+"\n");
}
var ctl=UnityEngine.Object.FindObjectOfType<CityDayNightController>(true);
foreach(var name in new[]{"Plane001","building 1","Building","Casino","Circus_Circus_0"}){
var obj=GameObject.Find(name);if(!obj)continue;var rr=obj.GetComponentInChildren<Renderer>();if(!rr)continue;
var b=rr.bounds;var pos=b.center+new Vector3(.4f,.15f,-1).normalized*b.size.magnitude*.85f;
CityLightingBuilder.Capture(ctl,Tuple.Create(pos,Quaternion.LookRotation(b.center-pos)),"Identify-"+name);
}
var v=SceneView.lastActiveSceneView;var c=UnityEngine.Object.FindObjectOfType<CityDayNightController>(true);
if(v&&v.camera){var cam=v.camera;File.WriteAllText("NightTarget.txt","Camera "+cam.transform.position+" rotation "+cam.transform.eulerAngles);
CityLightingBuilder.Capture(c,Tuple.Create(cam.transform.position,cam.transform.rotation),"Current-Target");
var ray=new Ray(cam.transform.position,cam.transform.forward);
foreach(var r in UnityEngine.Object.FindObjectsOfType<MeshRenderer>()){float distance;if(r.bounds.IntersectRay(ray,out distance))File.AppendAllText("NightTarget.txt","\n"+r.name+" "+distance+" "+r.bounds+" "+string.Join(",",Array.ConvertAll(r.sharedMaterials,m=>m?m.name:"null")));}}
}}


