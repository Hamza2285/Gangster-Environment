using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
[InitializeOnLoad]public static class CityJunctionDetails {
 struct T{public Vector3 a,b,c;}
 static readonly List<T> ground=new List<T>();
 static CityJunctionDetails(){EditorApplication.delayCall+=Run;}
 static bool Ground(Vector3 p,out float height){
  height=p.y;float best=1;bool found=false;
  foreach(var t in ground){
   float ax=t.b.x-t.a.x,az=t.b.z-t.a.z,bx=t.c.x-t.a.x,bz=t.c.z-t.a.z,px=p.x-t.a.x,pz=p.z-t.a.z;
   float det=ax*bz-az*bx;if(Mathf.Abs(det)<.0001f)continue;float u=(px*bz-pz*bx)/det,v=(ax*pz-az*px)/det;
   if(u<0||v<0||u+v>1)continue;float y=t.a.y+u*(t.b.y-t.a.y)+v*(t.c.y-t.a.y),delta=Mathf.Abs(y-p.y);
   if(delta<best){height=y;best=delta;found=true;}
  }return found;
 }
 static void Run(){
  if(!File.Exists("CityJunctionDetails.request"))return;File.Delete("CityJunctionDetails.request");
  try{
   var parent=GameObject.Find("Roads Props/Canal Side - Added Props");if(!parent)throw new Exception("Added props group missing");
   if(parent.transform.Find("Junction Details"))throw new Exception("Junction details already placed");
   var all=parent.scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>(true)).ToList();
   foreach(var r in all.Where(r=>r.sharedMaterial&&r.sharedMaterial.name=="Road Atlas")){
    var f=r.GetComponent<MeshFilter>();if(!f||Mathf.FloorToInt(f.sharedMesh.uv.Average(v=>v.x)/32)!=3)continue;
    var v=f.sharedMesh.vertices.Select(x=>f.transform.TransformPoint(x)).ToArray();var tri=f.sharedMesh.triangles;
    for(int i=0;i<tri.Length;i+=3)if(Mathf.Abs(Vector3.Cross(v[tri[i+1]]-v[tri[i]],v[tri[i+2]]-v[tri[i]]).normalized.y)>.95f)ground.Add(new T{a=v[tri[i]],b=v[tri[i+1]],c=v[tri[i+2]]});
   }
   var sourceSignals=all.Where(r=>r.name=="Signal (120)"||r.name=="Signal (121)"||r.name=="Signal (122)").ToArray();
   var targets=parent.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name.StartsWith("Signal - Canal")).ToArray();
   var types=new[]{"Hyp.prefab","hyp 1.prefab","Trash .prefab","Gen_const_bollard_02_LOD5_lambert62_001.prefab","metal street props low poly.008.prefab","metal street props low poly.005.prefab","metal street props low poly.011.prefab"};
   var group=new GameObject("Junction Details");group.transform.SetParent(parent.transform,false);int count=0;
   foreach(var target in targets){
    var source=sourceSignals.OrderBy(s=>Quaternion.Angle(s.transform.rotation,target.transform.rotation)).First();
    var delta=target.transform.position-source.transform.position;
    foreach(var reference in all.Where(r=>types.Any(t=>PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(r.gameObject).EndsWith("/"+t))&&Vector3.Distance(r.transform.position,source.transform.position)<10).ToArray()){
     var p=reference.transform.position+delta;if(!Ground(p,out float y))continue;p.y=y;
     if(!Ground(p+Vector3.right*.25f,out _)||!Ground(p-Vector3.right*.25f,out _)||!Ground(p+Vector3.forward*.25f,out _)||!Ground(p-Vector3.forward*.25f,out _))continue;
     string path=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(reference.gameObject);
     if(all.Any(r=>PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(r.gameObject)==path&&Vector3.Distance(r.transform.position,p)<1.4f))continue;
     var instance=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path),group.transform);
     instance.name=Path.GetFileNameWithoutExtension(path)+" - Canal Junction "+(++count);
     instance.transform.SetPositionAndRotation(p,reference.transform.rotation);instance.transform.localScale=reference.transform.lossyScale;
     var renderer=instance.GetComponent<MeshRenderer>();renderer.sharedMaterials=reference.sharedMaterials;
     instance.transform.position+=Vector3.up*(y-renderer.bounds.min.y+.01f);
     GameObjectUtility.SetStaticEditorFlags(instance,GameObjectUtility.GetStaticEditorFlags(reference.gameObject));all.Add(renderer);
    }
   }
   CityPropAudit.Capture(new Vector3(482,31,-492),Quaternion.Euler(0,0,0),100,"Junction-After",false);
   EditorSceneManager.MarkSceneDirty(parent.scene);EditorSceneManager.SaveScene(parent.scene);Selection.activeGameObject=parent;
   File.AppendAllText("CityPropPlacementReport.txt","\nJunction finishing: "+count+" matching bins, bollards and signal-side accessories added. Final prefab count: "+parent.GetComponentsInChildren<MeshRenderer>().Length+".");
  }catch(Exception e){File.AppendAllText("CityPropPlacementReport.txt","\nJunction detail error: "+e);Debug.LogException(e);}
 }
}

