using System;using System.IO;using System.Linq;using UnityEngine;using UnityEditor;
public static class VenueAudit{
[InitializeOnLoadMethod]static void Init(){EditorApplication.delayCall+=Run;}
static void Run(){if(!File.Exists("VenueAudit.request"))return;File.Delete("VenueAudit.request");
Directory.CreateDirectory("VenueLightingReview");
var c=UnityEngine.Object.FindObjectOfType<CityDayNightController>(true);
var targets=new[]{"bridge","Bridge_Fence"};
foreach(var name in targets){
var r=UnityEngine.Object.FindObjectsOfType<MeshRenderer>().FirstOrDefault(r=>r.name==name&&r.gameObject.scene==c.gameObject.scene);if(!r)continue;
var b=r.bounds;var direction=name=="Casino"?new Vector3(0,.15f,-1):new Vector3(1,.15f,.1f);
var pos=b.center+direction.normalized*b.size.magnitude*.9f;
CityLightingBuilder.Capture(c,Tuple.Create(pos,Quaternion.LookRotation(b.center-pos)),"Venue-Before-"+name);
if(name=="building"){
var mf=r.GetComponent<MeshFilter>();var m=mf.sharedMesh;var v=m.vertices;var tris=m.triangles;var uv=m.uv;var lines=new System.Collections.Generic.List<string>();
for(int i=0;i<tris.Length;i+=3){
var a=mf.transform.TransformPoint(v[tris[i]]);var bb=mf.transform.TransformPoint(v[tris[i+1]]);var d=mf.transform.TransformPoint(v[tris[i+2]]);
var n=Vector3.Cross(bb-a,d-a).normalized;if(Mathf.Abs(n.y)<.25f&&(a.y+bb.y+d.y)/3>35.2f)lines.Add(uv[tris[i]]*1600+" "+uv[tris[i+1]]*1600+" "+uv[tris[i+2]]*1600);
}
File.WriteAllLines("VenueLightingReview/ShopUV.txt",lines.Distinct());
}
}
}}


