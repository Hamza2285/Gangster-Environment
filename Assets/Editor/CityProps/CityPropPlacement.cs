using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;

[InitializeOnLoad]public static class CityPropPlacement {
 class Tri{public Vector3 a,b,c;public int layer;public float minX,maxX,minZ,maxZ;}
 class Edge{public Vector3 a,b;public int count;}
 class Spot{public Vector3 edge,outward,point;}
 static readonly List<Tri> triangles=new List<Tri>();
 static readonly Dictionary<Vector2Int,List<Tri>> cells=new Dictionary<Vector2Int,List<Tri>>();
 static readonly List<GameObject> added=new List<GameObject>();
 static readonly Dictionary<string,List<Vector3>> locations=new Dictionary<string,List<Vector3>>();
 static readonly Dictionary<string,Transform> groups=new Dictionary<string,Transform>();
 static MeshRenderer[] original;static Transform root;static StringBuilder report;
 const string Prefabs="Assets/Scenes/Oasis/3D/Prefeb/";
 static CityPropPlacement(){EditorApplication.delayCall+=Run;}
 static void Run(){
  if(!File.Exists("CityPropPlacement.request"))return;
  File.Move("CityPropPlacement.request","CityPropPlacement.started-"+DateTime.UtcNow.Ticks);
  report=new StringBuilder();CityDayNightController lighting=null;bool wasNight=false;
  try{
   triangles.Clear();cells.Clear();added.Clear();locations.Clear();groups.Clear();
   var scene=SceneManager.GetSceneByPath("Assets/Scenes/Oasis/Gangstar City.unity");if(!scene.isLoaded)throw new Exception("City scene not loaded");
   original=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>(true)).Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
   if(scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Any(t=>t.name=="Canal Side - Added Props"))throw new Exception("Added-props group already exists; inspect it before rerunning.");
   var originalTransforms=original.ToDictionary(r=>r.transform,t=>t.transform.localToWorldMatrix);
   lighting=UnityEngine.Object.FindObjectOfType<CityDayNightController>(true);wasNight=lighting&&lighting.IsNight;
   string stamp=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
   Directory.CreateDirectory("Assets/Editor/PropsBackups");
   string backup="Assets/Editor/PropsBackups/BeforeProps-"+stamp+".unity";EditorSceneManager.SaveScene(scene,backup,true);
   report.AppendLine("Backup: "+backup);
   var parent=scene.GetRootGameObjects().FirstOrDefault(g=>g.name=="Roads Props");
   var group=new GameObject("Canal Side - Added Props");SceneManager.MoveGameObjectToScene(group,scene);if(parent)group.transform.SetParent(parent.transform,false);root=group.transform;
   Undo.RegisterCreatedObjectUndo(group,"Extend canal-side street props");
   var roadMeshes=original.Where(r=>r.sharedMaterial&&r.sharedMaterial.name=="Road Atlas").Select(r=>r.GetComponent<MeshFilter>()).Where(f=>f&&f.sharedMesh).ToArray();
   var edges=new Dictionary<string,Edge>();var junctions=new List<Vector3>();
   foreach(var f in roadMeshes){
    var mesh=f.sharedMesh;var uv=mesh.uv;int layer=Mathf.FloorToInt(uv.Average(v=>v.x)/32);
    var verts=mesh.vertices.Select(v=>f.transform.TransformPoint(v)).ToArray();var indices=mesh.triangles;
    if(layer==1)junctions.Add(f.GetComponent<Renderer>().bounds.center);
    for(int i=0;i<indices.Length;i+=3){
     var a=verts[indices[i]];var b=verts[indices[i+1]];var c=verts[indices[i+2]];
     var normal=Vector3.Cross(b-a,c-a).normalized;if(Mathf.Abs(normal.y)<.8f)continue;
     var tri=new Tri{a=a,b=b,c=c,layer=layer,minX=Mathf.Min(a.x,b.x,c.x),maxX=Mathf.Max(a.x,b.x,c.x),minZ=Mathf.Min(a.z,b.z,c.z),maxZ=Mathf.Max(a.z,b.z,c.z)};triangles.Add(tri);
     for(int x=Mathf.FloorToInt(tri.minX/25);x<=Mathf.FloorToInt(tri.maxX/25);x++)for(int z=Mathf.FloorToInt(tri.minZ/25);z<=Mathf.FloorToInt(tri.maxZ/25);z++){
      var key=new Vector2Int(x,z);if(!cells.TryGetValue(key,out var list)){list=new List<Tri>();cells[key]=list;}list.Add(tri);
     }
     if(layer==0||layer==2){AddEdge(edges,a,b);AddEdge(edges,b,c);AddEdge(edges,c,a);}
    }
   }
   var spots=new List<Spot>();
   foreach(var e in edges.Values.Where(e=>e.count==1)){
    float length=Vector3.Distance(e.a,e.b);if(length<8)continue;
    var tangent=(e.b-e.a).normalized;var normal=new Vector3(-tangent.z,0,tangent.x).normalized;
    int count=Mathf.Max(1,Mathf.FloorToInt(length/32));
    for(int i=0;i<count;i++){
     var mid=Vector3.Lerp(e.a,e.b,(i+.5f)/count);if(mid.x<145||mid.x>850||mid.z>555||mid.z< -620||mid.y<26||mid.y>45)continue;
     foreach(int sign in new[]{1,-1}){
      var outward=normal*sign;var p=mid+outward*1.4f;
      if(!Surface(mid-outward*.8f,mid.y,0,2,out _)||!Surface(p,mid.y,3,3,out float y))continue;
      if(Mathf.Abs(y-mid.y)>1)continue;p.y=y;
      if(junctions.Any(j=>Horizontal(p,j)<24))continue;
      spots.Add(new Spot{edge=mid,outward=outward,point=p});break;
     }
    }
   }
   spots=spots.OrderBy(s=>s.point.z).ThenBy(s=>s.point.x).ToList();
   report.AppendLine("Pavement candidates across canal: "+spots.Count);
   int serial=0;
   foreach(var spot in spots){
    var lamp=Place("Street Light 1.prefab",spot.point,-spot.outward,30,.5f,true);
    // Benches and bins are spaced more sparsely than lamps, following the finished side.
    if(serial%2==0){
     var tangent=Vector3.Cross(Vector3.up,spot.outward);
     var bench=Place("Bench.prefab",spot.edge+spot.outward*1.9f+tangent*7,-spot.outward,48,1.3f,false);
     if(bench)Place("Trash .prefab",bench.transform.position+tangent*3.5f,-spot.outward,28,.65f,false);
    }
    if(serial%5==0)Place("Speed limit 1.prefab",spot.point+Vector3.Cross(Vector3.up,spot.outward)*6,-spot.outward,95,.5f,false);
    if(serial%7==0)Place("Road Billboards.prefab",spot.point+Vector3.Cross(Vector3.up,spot.outward)*11,-spot.outward,85,.8f,false);
    serial++;
   }
   // Copy the user's three-signal arrangement only into unpopulated junctions.
   var templateCenter=junctions.OrderBy(j=>Horizontal(j,new Vector3(-236,27.7f,60))).First();
   var signalTemplates=original.Where(r=>PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(r.gameObject)==Prefabs+"Signal.prefab"&&Horizontal(r.transform.position,templateCenter)<29).ToArray();
   foreach(var junction in junctions.Where(j=>j.x>145&&j.z<555&&j.z> -600)){
    if(original.Any(r=>PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(r.gameObject)==Prefabs+"Signal.prefab"&&Horizontal(r.transform.position,junction)<32))continue;
    foreach(var source in signalTemplates){
     var p=source.transform.position+(junction-templateCenter);
     PlaceExact("Signal.prefab",p,source.transform.rotation,source,14,.55f,true);
    }
   }
   // Verify every new base lies on pavement, with no changes to the original transforms.
   foreach(var go in added)if(!Surface(go.transform.position,go.transform.position.y,3,3,out _))throw new Exception("Prop is not on pavement: "+go.name);
   foreach(var kv in originalTransforms)if(kv.Key.localToWorldMatrix!=kv.Value)throw new Exception("An existing transform changed");
   var newLamps=added.Where(g=>g.name.StartsWith("Street Light 1")).ToArray();
   if(lighting&&newLamps.Length>0)AppendNightBulbs(lighting,newLamps,stamp);
   if(lighting)lighting.ApplyDay();
   CityPropAudit.Capture(new Vector3(481,31,-390),Quaternion.Euler(0,0,0),100,"East-After-Street",false);
   CityPropAudit.Capture(new Vector3(460,230,-300),Quaternion.Euler(55,0,0),100,"East-After-Aerial",false);
   CityPropAudit.Capture(new Vector3(279,31,93),Quaternion.Euler(0,0,0),100,"Canal-After-Street",false);
   if(lighting){lighting.ApplyNight();var pose=Tuple.Create(new Vector3(481,31,-390),Quaternion.Euler(0,0,0));CityLightingBuilder.Capture(lighting,pose,"Props-Night");lighting.Apply(wasNight);}
   foreach(var g in added.GroupBy(g=>PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(g)))report.AppendLine(g.Count()+" "+Path.GetFileNameWithoutExtension(g.Key));
   report.AppendLine("SUCCESS: "+added.Count+" new prefab instances. Existing props retained; all new bases on pavement. No new per-prop scripts, materials or realtime lights.");
   EditorSceneManager.MarkSceneDirty(scene);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);Selection.activeGameObject=group;
   File.WriteAllText("PropPlacementReview/AddedProps.csv","Name,X,Y,Z\n"+string.Join("\n",added.Select(g=>g.name+","+g.transform.position.x+","+g.transform.position.y+","+g.transform.position.z)));
  }catch(Exception e){report.AppendLine("FAILED "+e);Debug.LogException(e);}
  finally{if(lighting)lighting.Apply(wasNight);File.WriteAllText("CityPropPlacementReport.txt",report.ToString());}
 }
 static void AddEdge(Dictionary<string,Edge> edges,Vector3 a,Vector3 b){
  string aa=Key(a),bb=Key(b);string key=string.CompareOrdinal(aa,bb)<0?aa+"|"+bb:bb+"|"+aa;
  if(edges.TryGetValue(key,out var edge))edge.count++;else edges[key]=new Edge{a=a,b=b,count=1};
 }
 static string Key(Vector3 p){return Mathf.RoundToInt(p.x*100)+","+Mathf.RoundToInt(p.y*100)+","+Mathf.RoundToInt(p.z*100);}
 static float Horizontal(Vector3 a,Vector3 b){float x=a.x-b.x,z=a.z-b.z;return Mathf.Sqrt(x*x+z*z);}
 static bool Surface(Vector3 point,float expected,int layerA,int layerB,out float height){
  height=0;float closest=2;
  if(!cells.TryGetValue(new Vector2Int(Mathf.FloorToInt(point.x/25),Mathf.FloorToInt(point.z/25)),out var list))return false;
  bool found=false;
  foreach(var t in list){
   if((t.layer!=layerA&&t.layer!=layerB)||point.x<t.minX-.01f||point.x>t.maxX+.01f||point.z<t.minZ-.01f||point.z>t.maxZ+.01f)continue;
   float ax=t.b.x-t.a.x,az=t.b.z-t.a.z,bx=t.c.x-t.a.x,bz=t.c.z-t.a.z,px=point.x-t.a.x,pz=point.z-t.a.z;
   float det=ax*bz-az*bx;if(Mathf.Abs(det)<.00001f)continue;
   float u=(px*bz-pz*bx)/det,v=(ax*pz-az*px)/det;
   if(u<-.0001f||v<-.0001f||u+v>1.0001f)continue;
   float y=t.a.y+u*(t.b.y-t.a.y)+v*(t.c.y-t.a.y);float delta=Mathf.Abs(y-expected);
   if(delta<closest){height=y;closest=delta;found=true;}
  }return found;
 }
 static Vector3 RoadDirection(Vector3 p){
  for(float dist=1;dist<=20;dist+=1)for(int i=0;i<16;i++){var d=Quaternion.Euler(0,i*22.5f,0)*Vector3.forward;if(Surface(p+d*dist,p.y,0,2,out _))return d;}
  return Vector3.right;
 }
 static GameObject Place(string prefab,Vector3 point,Vector3 roadDirection,float spacing,float radius,bool tall){
  var source=original.FirstOrDefault(r=>PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(r.gameObject)==Prefabs+prefab&&r.transform.position.x< -100);
  if(!source)return null;
  var reference=RoadDirection(source.transform.position);
  float yaw=Vector3.SignedAngle(reference,roadDirection,Vector3.up);
  return PlaceExact(prefab,point,Quaternion.AngleAxis(yaw,Vector3.up)*source.transform.rotation,source,spacing,radius,tall);
 }
 static GameObject PlaceExact(string prefab,Vector3 point,Quaternion rotation,MeshRenderer source,float spacing,float radius,bool tall){
  if(!Surface(point,point.y,3,3,out float y))return null;point.y=y;
  if(!locations.TryGetValue(prefab,out var existing)){
   existing=original.Where(r=>PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(r.gameObject)==Prefabs+prefab).Select(r=>r.transform.position).ToList();locations[prefab]=existing;
  }
  if(existing.Any(p=>Horizontal(p,point)<spacing))return null;
  foreach(var offset in new[]{Vector3.left*radius,Vector3.right*radius,Vector3.forward*radius,Vector3.back*radius})
   if(!Surface(point+offset,y,3,3,out float yy)||Mathf.Abs(yy-y)>.18f)return null;
  // Test the occupied pole/seat footprint, not an overhanging lamp arm.
  var occupied=new Bounds(point+Vector3.up*(tall?3:1),new Vector3(radius*2,tall?6:2,radius*2));
  foreach(var r in original){
   var b=r.bounds;if(b.size.y<.4f||b.size.x>230||b.size.z>230||r.name=="Night Lamp Bulbs")continue;
   if(b.Intersects(occupied))return null;
  }
  foreach(var go in added)if(Horizontal(go.transform.position,point)<radius+1.5f)return null;
  string label=Path.GetFileNameWithoutExtension(prefab);
  if(!groups.TryGetValue(label,out var parent)){var g=new GameObject(label);g.transform.SetParent(root,false);parent=g.transform;groups[label]=parent;}
  var asset=AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs+prefab);
  var instance=(GameObject)PrefabUtility.InstantiatePrefab(asset,parent);
  instance.name=label+" - Canal "+(existing.Count+1).ToString("D3");
  instance.transform.SetPositionAndRotation(point,rotation);instance.transform.localScale=source.transform.lossyScale;
  var renderer=instance.GetComponent<MeshRenderer>();if(renderer)renderer.sharedMaterials=source.sharedMaterials;
  GameObjectUtility.SetStaticEditorFlags(instance,GameObjectUtility.GetStaticEditorFlags(source.gameObject));
  // Preserve model pivot-to-ground offset while grounding the actual visible base.
  if(renderer){float bottom=renderer.bounds.min.y;instance.transform.position+=Vector3.up*(y-bottom+.01f);}
  existing.Add(point);added.Add(instance);return instance;
 }
 static void AppendNightBulbs(CityDayNightController lighting,GameObject[] lamps,string stamp){
  if(!lighting.nightLampBulbs)return;
  var filter=lighting.nightLampBulbs.GetComponent<MeshFilter>();var mesh=filter.sharedMesh;
  string path=AssetDatabase.GetAssetPath(mesh);AssetDatabase.CopyAsset(path,"Assets/Editor/PropsBackups/LampBulbs-"+stamp+".asset");
  var vertices=mesh.vertices.ToList();var indices=mesh.triangles.ToList();var positions=lighting.streetLightPositions.ToList();
  Vector3[] unit={Vector3.up,Vector3.down,Vector3.left,Vector3.right,Vector3.forward,Vector3.back};
  int[] faces={0,2,4,0,4,3,0,3,5,0,5,2,1,4,2,1,3,4,1,5,3,1,2,5};
  foreach(var go in lamps){
   var r=go.GetComponent<Renderer>();var f=go.GetComponent<MeshFilter>();Vector3 p=r.bounds.center;float best=-1;
   foreach(var v in f.sharedMesh.vertices){var w=go.transform.TransformPoint(v);if(w.y<r.bounds.max.y-r.bounds.size.y*.2f)continue;var d=w-go.transform.position;float length=d.x*d.x+d.z*d.z;if(length>best){best=length;p=w;}}
   p+=Vector3.down*.12f;positions.Add(p+Vector3.down*.3f);int start=vertices.Count;
   foreach(var v in unit)vertices.Add(filter.transform.InverseTransformPoint(p+Vector3.Scale(v,new Vector3(.22f,.075f,.22f))));
   foreach(int index in faces)indices.Add(start+index);
  }
  mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
  lighting.streetLightPositions=positions.ToArray();EditorUtility.SetDirty(lighting);
  report.AppendLine("New lamps added to existing night system; realtime light pool remains "+lighting.streetLights.Length+".");
 }
}

