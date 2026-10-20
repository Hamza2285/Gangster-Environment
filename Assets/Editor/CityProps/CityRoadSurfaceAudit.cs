using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
[InitializeOnLoad]public static class CityRoadSurfaceAudit{
 static CityRoadSurfaceAudit(){EditorApplication.delayCall+=Run;}
 static void Run(){if(!File.Exists("CityRoadSurface.request"))return;File.Delete("CityRoadSurface.request");
 var log=new StringBuilder();var fs=UnityEngine.Object.FindObjectsOfType<MeshFilter>().Where(f=>f.GetComponent<Renderer>()&&f.GetComponent<Renderer>().sharedMaterial&&f.GetComponent<Renderer>().sharedMaterial.name=="Road Atlas").ToArray();
 foreach(var g in fs.GroupBy(f=>Mathf.FloorToInt(f.sharedMesh.uv.Average(v=>v.x)/32))){log.AppendLine("LAYER "+g.Key+" count="+g.Count());foreach(var f in g.Where(f=>f.GetComponent<Renderer>().bounds.center.x>145).Take(12))log.AppendLine(f.name+" "+f.sharedMesh.name+" "+f.GetComponent<Renderer>().bounds);}
 var signals=UnityEngine.Object.FindObjectsOfType<MeshRenderer>().Where(r=>PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(r.gameObject).EndsWith("/Signal.prefab")).ToArray();
 foreach(var r in signals.Where(r=>r.transform.position.x>-270&&r.transform.position.x<-190&&r.transform.position.z>10&&r.transform.position.z<105))log.AppendLine("SIGNAL "+r.name+" "+r.transform.position+" rot "+r.transform.eulerAngles+" bounds "+r.bounds);
 File.WriteAllText("PropPlacementReview/SurfaceAudit.txt",log.ToString());
 CityPropAudit.Capture(new Vector3(460,230,-300),Quaternion.Euler(55,0,0),100,"East-Before-Aerial",false);
 CityPropAudit.Capture(new Vector3(481,31,-390),Quaternion.Euler(0,0,0),100,"East-Before-Street",false);
 }}

