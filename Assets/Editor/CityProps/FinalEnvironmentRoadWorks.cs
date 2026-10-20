using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;

[InitializeOnLoad] public static class FinalEnvironmentRoadWorks {
 const string ScenePath="Assets/Editor/DayNightBackups/Gangstar Final Env.unity";
 const string Out="Assets/Scenes/Oasis/FinalEnvironmentRoads";
 const string PrefabPath=Out+"/Final Roads.prefab";
 static FinalEnvironmentRoadWorks(){EditorApplication.update+=Poll;}
 static void Poll(){if(!File.Exists("FinalEnvironmentRoadWorks.request")||EditorApplication.isCompiling||EditorApplication.isPlayingOrWillChangePlaymode)return;
 File.Move("FinalEnvironmentRoadWorks.request","FinalEnvironmentRoadWorks.started-"+DateTime.UtcNow.Ticks);Build();}
 class Road {public MeshFilter f;public Mesh mesh;public Vector3[] vertices;public int[] indices;public Bounds bounds;public int layer;public Vector3 dir;public float width;}
 class Field {public Road road;public bool x;public float start,end,center,half,baseY,amplitude;}
 struct Vertex {public Vector3 p;public Vector2 uv;public Vertex(Vector3 p,Vector2 uv){this.p=p;this.uv=uv;}}
 class Portal {public Vector3 p,dir;public float width,length,clearance;public GameObject go;}
 class MeshChange {public string name;public Mesh before,after;public int samples;}
 static List<Field> fields=new List<Field>();
 static float Coord(Vector3 p,bool x){return x?p.x:p.z;}
 static float Short(Vector3 p,bool x){return x?p.z:p.x;}
 static float Offset(Vector3 p){
 float result=0;
 foreach(var f in fields){
 float t=(Coord(p,f.x)-f.start)/(f.end-f.start);if(t<=0||t>=1||Mathf.Abs(p.y-f.baseY)>1)continue;
 float lateral=Mathf.Abs(Short(p,f.x)-f.center)-f.half;if(lateral>=7)continue;
 float weight=lateral<=0?1:.5f+.5f*Mathf.Cos(Mathf.PI*lateral/7);
 result+=f.amplitude*Mathf.Pow(Mathf.Sin(Mathf.PI*t),2)*Mathf.Sin(2*Mathf.PI*t)*weight;
 }return result;
 }
 static Road Describe(MeshFilter f){
 var mesh=f.sharedMesh;if(!mesh||!f.GetComponent<MeshRenderer>())return null;var r=f.GetComponent<MeshRenderer>();
 if(!r.sharedMaterial||r.sharedMaterial.name!="Road Atlas")return null;
 var road=new Road{f=f,mesh=mesh,vertices=mesh.vertices.Select(f.transform.TransformPoint).ToArray(),indices=mesh.triangles,bounds=r.bounds};
 road.layer=Mathf.FloorToInt(mesh.uv.Average(v=>v.x)/32);
 bool x=road.bounds.size.x>road.bounds.size.z;road.width=Mathf.Min(road.bounds.size.x,road.bounds.size.z);road.dir=x?Vector3.right:Vector3.forward;
 return road;
 }
 static bool Surface(Road r,Vector3 p,out float height){
 for(int i=0;i<r.indices.Length;i+=3){
 var a=r.vertices[r.indices[i]];var b=r.vertices[r.indices[i+1]];var c=r.vertices[r.indices[i+2]];
 float d=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);if(Mathf.Abs(d)<.000001f)continue;
 float u=((b.z-c.z)*(p.x-c.x)+(c.x-b.x)*(p.z-c.z))/d,v=((c.z-a.z)*(p.x-c.x)+(a.x-c.x)*(p.z-c.z))/d;
 if(u>=-.0005f&&v>=-.0005f&&u+v<=1.0005f){height=u*a.y+v*b.y+(1-u-v)*c.y;return true;}
 }height=0;return false;
 }
 static List<Portal> Locate(List<Road> roads){
 var result=new List<Portal>();var upper=roads.Where(r=>(r.layer==0||r.layer==2)&&r.bounds.size.y>8).ToArray();
 foreach(var low in roads.Where(r=>r.layer==0&&r.bounds.size.y<.12f&&r.width>24&&r.width<34&&Mathf.Max(r.bounds.size.x,r.bounds.size.z)>70).OrderByDescending(r=>Mathf.Max(r.bounds.size.x,r.bounds.size.z))){
 float length=Mathf.Max(low.bounds.size.x,low.bounds.size.z);
 for(float t=.2f;t<=.8f;t+=.04f){
 var p=low.bounds.center+low.dir*((t-.5f)*length);if(!Surface(low,p,out float y))continue;p.y=y;
 if(result.Any(v=>Vector3.Distance(v.p,p)<150))continue;
 var high=upper.FirstOrDefault(r=>Surface(r,p,out float top)&&top-y>9&&top-y<24);
 if(high==null)continue;
 // Require a clear ground-level road for the full portal length.
 if(!Surface(low,p-low.dir*14,out float a)||!Surface(low,p+low.dir*14,out float b)||Mathf.Abs(a-y)>.1f||Mathf.Abs(b-y)>.1f)continue;
 result.Add(new Portal{p=p,dir=low.dir,width=low.width+2.0f,length=24,clearance=6.2f});break;
 }if(result.Count==2)return result;
 }throw new Exception("Two safe underpass crossings were not found.");
 }
 static List<Vertex> Clip(List<Vertex> polygon,bool x,float value,bool above){
 var output=new List<Vertex>();if(polygon.Count==0)return output;
 var prev=polygon[polygon.Count-1];float a=Coord(prev.p,x);bool insideA=above?a>=value-.00001f:a<=value+.00001f;
 foreach(var v in polygon){
 float b=Coord(v.p,x);bool insideB=above?b>=value-.00001f:b<=value+.00001f;
 if(insideA!=insideB){float t=(value-a)/(b-a);output.Add(new Vertex(Vector3.Lerp(prev.p,v.p,t),Vector2.Lerp(prev.uv,v.uv,t)));}
 if(insideB)output.Add(v);prev=v;a=b;insideA=insideB;
 }return output;
 }
 static Mesh SaveMesh(Mesh m,string name){
 string path=Out+"/Meshes/"+name+".asset";m.name=name;var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
 if(existing){EditorUtility.CopySerialized(m,existing);UnityEngine.Object.DestroyImmediate(m);EditorUtility.SetDirty(existing);return existing;}
 AssetDatabase.CreateAsset(m,path);return m;
 }
 static Mesh Rebuild(MeshFilter f){
 var original=f.sharedMesh;var uv=original.uv;var w=original.vertices.Select(f.transform.TransformPoint).ToArray();var indices=original.triangles;
 var position=new List<Vector3>();var mapping=new List<Vector2>();var triangles=new List<int>();
 for(int i=0;i<indices.Length;i+=3){
 var initial=new List<Vertex>();for(int j=0;j<3;j++)initial.Add(new Vertex(w[indices[i+j]],uv[indices[i+j]]));
 var pieces=new List<List<Vertex>>{initial};
 foreach(var field in fields){
 float longMin=initial.Min(v=>Coord(v.p,field.x)),longMax=initial.Max(v=>Coord(v.p,field.x));
 float shortMin=initial.Min(v=>Short(v.p,field.x)),shortMax=initial.Max(v=>Short(v.p,field.x));
 float yMin=initial.Min(v=>v.p.y),yMax=initial.Max(v=>v.p.y);
 if(longMax<field.start||longMin>field.end||shortMax<field.center-field.half-7||shortMin>field.center+field.half+7||yMin>field.baseY+1||yMax<field.baseY-1)continue;
 var cuts=new List<float>();
 int n=Mathf.CeilToInt((field.end-field.start)/4);
 for(int k=0;k<=n;k++)cuts.Add(Mathf.Lerp(field.start,field.end,(float)k/n));
 foreach(float cut in cuts){
 var split=new List<List<Vertex>>();
 foreach(var piece in pieces){
 if(piece.Min(v=>Coord(v.p,field.x))<cut-.00001f&&piece.Max(v=>Coord(v.p,field.x))>cut+.00001f){
 var a=Clip(piece,field.x,cut,false);var b=Clip(piece,field.x,cut,true);if(a.Count>2)split.Add(a);if(b.Count>2)split.Add(b);
 }else split.Add(piece);
 }pieces=split;
 }
 // Sidewalk transitions are also sampled, so large pavement polygons cannot stay flat at the road edge.
 foreach(float cut in new[]{field.center-field.half-7,field.center-field.half-3.5f,field.center-field.half,field.center+field.half,field.center+field.half+3.5f,field.center+field.half+7}){
 var split=new List<List<Vertex>>();foreach(var piece in pieces){
 if(piece.Min(v=>Short(v.p,field.x))<cut-.00001f&&piece.Max(v=>Short(v.p,field.x))>cut+.00001f){
 var a=Clip(piece,!field.x,cut,false);var b=Clip(piece,!field.x,cut,true);if(a.Count>2)split.Add(a);if(b.Count>2)split.Add(b);
 }else split.Add(piece);
 }pieces=split;
 }
 }
 foreach(var polygon in pieces)for(int k=1;k<polygon.Count-1;k++){
 var a=polygon[0];var b=polygon[k];var c=polygon[k+1];if(Vector3.Cross(b.p-a.p,c.p-a.p).sqrMagnitude<.000000001f)continue;
 int n=position.Count;foreach(var v in new[]{a,b,c}){var p=v.p+Vector3.up*Offset(v.p);position.Add(f.transform.InverseTransformPoint(p));mapping.Add(v.uv);}
 triangles.Add(n);triangles.Add(n+1);triangles.Add(n+2);
 }
 }
 var mesh=new Mesh();if(position.Count>65535)mesh.indexFormat=IndexFormat.UInt32;mesh.SetVertices(position);mesh.SetUVs(0,mapping);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();return mesh;
 }
 static bool Affected(MeshFilter f){
 var b=f.GetComponent<Renderer>().bounds;
 return fields.Any(v=>Coord(b.max,v.x)>v.start&&Coord(b.min,v.x)<v.end&&Short(b.max,v.x)>v.center-v.half-7&&Short(b.min,v.x)<v.center+v.half+7&&b.min.y<v.baseY+1&&b.max.y>v.baseY-1);
 }
 static Material GetMaterial(string name,Color color,bool glow=false){
 string path=Out+"/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m)return m;
 m=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name};m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",.18f);m.enableInstancing=true;
 if(glow){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",color*1.4f);}else{
 var paving=AssetDatabase.LoadAssetAtPath<Material>("Assets/Scenes/Oasis/3D/Fbx/Materials/Road 04.mat");if(paving)m.SetTexture("_BaseMap",paving.GetTexture("_BaseMap"));
 m.SetTextureScale("_BaseMap",new Vector2(4,2));
 }
 AssetDatabase.CreateAsset(m,path);return m;
 }
 static void Box(Transform parent,Vector3 p,Vector3 size,Quaternion rotation,Material material){
 var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.transform.SetParent(parent,false);go.transform.SetPositionAndRotation(p,rotation);go.transform.localScale=size;go.GetComponent<MeshRenderer>().sharedMaterial=material;go.isStatic=true;
 }
 static void Combine(Transform root,Material material,string name,bool collider){
 var fs=root.GetComponentsInChildren<MeshFilter>().Where(f=>f.GetComponent<MeshRenderer>().sharedMaterial==material).ToArray();
 var combined=new Mesh();if(fs.Sum(f=>f.sharedMesh.vertexCount)>65535)combined.indexFormat=IndexFormat.UInt32;
 combined.CombineMeshes(fs.Select(f=>new CombineInstance{mesh=f.sharedMesh,transform=root.worldToLocalMatrix*f.transform.localToWorldMatrix}).ToArray(),true,true);
 combined=SaveMesh(combined,name);foreach(var f in fs)UnityEngine.Object.DestroyImmediate(f.gameObject);
 var go=new GameObject(name);go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=combined;go.AddComponent<MeshRenderer>().sharedMaterial=material;
 if(collider)go.AddComponent<MeshCollider>().sharedMesh=combined;go.isStatic=true;
 }
 static void MakePortal(Portal portal,Transform parent,int index,Material concrete,Material trim,Material glow){
 var go=new GameObject("City Underpass "+index);go.transform.SetParent(parent,false);portal.go=go;
 var p=portal.p;var dir=portal.dir;var side=Vector3.Cross(Vector3.up,dir);var rot=Quaternion.LookRotation(dir);float w=portal.width,h=portal.clearance,l=portal.length;
 for(int sign=-1;sign<=1;sign+=2){
 Box(go.transform,p+side*sign*(w/2+.45f)+Vector3.up*(h/2),new Vector3(.9f,h,l),rot,concrete);
 Box(go.transform,p+side*sign*(w/2+.3f)+Vector3.up*.3f,new Vector3(.6f,.6f,l),rot,trim);
 for(int end=-1;end<=1;end+=2){
 Box(go.transform,p+side*sign*(w/2+.3f)+dir*end*(l/2)+Vector3.up*h/2,new Vector3(1.25f,h,.9f),rot,trim);
 // Flared wing walls give the tunnel mouths a smooth approach.
 Box(go.transform,p+side*sign*(w/2+1.25f)+dir*end*(l/2+2.3f)+Vector3.up*1.35f,new Vector3(.7f,2.7f,5.0f),Quaternion.LookRotation(dir+side*sign*end*.32f),concrete);
 }
 for(int k=-1;k<=1;k++)Box(go.transform,p+side*sign*(w/2-.06f)+dir*k*7+Vector3.up*4.8f,new Vector3(.08f,.2f,2.2f),rot,glow);
 }
 Box(go.transform,p+Vector3.up*(h+.45f),new Vector3(w+1.8f,.9f,l),rot,concrete);
 for(int end=-1;end<=1;end+=2){
 Box(go.transform,p+dir*end*l/2+Vector3.up*(h+.65f),new Vector3(w+2.5f,1.1f,1.0f),rot,trim);
 Box(go.transform,p+dir*end*(l/2+.52f)+Vector3.up*(h+.55f),new Vector3(w+1.3f,.2f,.07f),rot,glow);
 }
 Combine(go.transform,concrete,"Underpass_"+index+"_Concrete",true);Combine(go.transform,trim,"Underpass_"+index+"_Trim",true);Combine(go.transform,glow,"Underpass_"+index+"_Markers",false);
 }
 static void Capture(Scene scene,Vector3 p,Vector3 target,string filename,bool ortho=false,float size=70){
 var go=new GameObject("Final Environment Review Camera"){hideFlags=HideFlags.HideAndDontSave};SceneManager.MoveGameObjectToScene(go,scene);
 var c=go.AddComponent<Camera>();c.enabled=false;c.transform.SetPositionAndRotation(p,Quaternion.LookRotation(target-p));c.nearClipPlane=.2f;c.farClipPlane=5000;c.fieldOfView=62;c.orthographic=ortho;c.orthographicSize=size;
 var rt=RenderTexture.GetTemporary(1400,1000,24);var previous=RenderTexture.active;
 try{c.targetTexture=rt;c.Render();RenderTexture.active=rt;var tex=new Texture2D(1400,1000,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1400,1000),0,0);tex.Apply();File.WriteAllBytes("FinalEnvironmentRoadReview/"+filename+".png",tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);}
 finally{c.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(go);}
 }
 static void Build(){
 var log=new StringBuilder();GameObject stage=null;CityDayNightController lighting=null;bool night=false,applied=false;
 try{
 var scene=SceneManager.GetSceneByPath(ScenePath);if(!scene.isLoaded)throw new Exception("The confirmed Final Environment scene must be loaded.");
 var original=scene.GetRootGameObjects().Single(g=>g.name=="Track Roads");
 if(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath))throw new Exception("Final road prefab already exists; inspect before rebuilding.");
 Directory.CreateDirectory("FinalEnvironmentRoadReview");Directory.CreateDirectory("FinalEnvironmentRoadBackup");
 if(!File.Exists("FinalEnvironmentRoadBackup/Gangstar Final Env.saved.unity"))File.Copy(ScenePath,"FinalEnvironmentRoadBackup/Gangstar Final Env.saved.unity");
 if(!File.Exists("FinalEnvironmentRoadBackup/Gangstar Final Env.current.unity")&&!EditorSceneManager.SaveScene(scene,"FinalEnvironmentRoadBackup/Gangstar Final Env.current.unity",true))throw new Exception("Current scene backup failed.");
 Directory.CreateDirectory(Out+"/Meshes");AssetDatabase.Refresh();
 stage=UnityEngine.Object.Instantiate(original);stage.name="Final Road Staging";SceneManager.MoveGameObjectToScene(stage,scene);if(PrefabUtility.IsPartOfPrefabInstance(stage))PrefabUtility.UnpackPrefabInstance(stage,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
 if(stage.GetComponentsInChildren<Transform>(true).Any(t=>t.name.StartsWith("City Underpass ")||t.name=="Underpass 1"))throw new Exception("Underpasses already exist; inspect rather than duplicate them.");
 var roads=stage.GetComponentsInChildren<MeshFilter>(true).Select(Describe).Where(r=>r!=null).ToList();var portals=Locate(roads);fields.Clear();
 foreach(var road in roads.Where(r=>(r.layer==0||r.layer==2)&&r.bounds.size.y<.12f&&r.width>26&&r.width<34&&Mathf.Max(r.bounds.size.x,r.bounds.size.z)>125).OrderByDescending(r=>Mathf.Max(r.bounds.size.x,r.bounds.size.z))){
 bool x=road.bounds.size.x>road.bounds.size.z;
 if(fields.Any(f=>Vector3.Distance(f.road.bounds.center,road.bounds.center)<140))continue;
 if(portals.Any(v=>Coord(v.p,x)>Coord(road.bounds.min,x)-18&&Coord(v.p,x)<Coord(road.bounds.max,x)+18&&Mathf.Abs(Short(v.p,x)-Short(road.bounds.center,x))<road.width*.5f+20))continue;
 if(road.bounds.size.y>.12f)continue;
 var field=new Field{road=road,x=x,start=Coord(road.bounds.min,x),end=Coord(road.bounds.max,x),center=Short(road.bounds.center,x),half=road.width/2,baseY=road.bounds.center.y,amplitude=1.05f};
 fields.Add(field);if(fields.Count==6)break;
 }
 if(fields.Count<6)throw new Exception("Only "+fields.Count+" suitable gentle road stretches found.");
 foreach(var f in fields)log.AppendLine("Gentle rise/dip "+f.road.f.name+" center="+f.road.bounds.center+" length="+(f.end-f.start)+" peak displacement approximately +/-0.68m; endpoints fixed.");
 var changes=new List<MeshChange>();
 foreach(var road in roads.Where(r=>r.layer!=1&&Affected(r.f))){
 var changed=Rebuild(road.f);var saved=SaveMesh(changed,"Gentle_"+road.f.name);changes.Add(new MeshChange{name=road.f.name,before=road.mesh,after=saved});
 road.f.sharedMesh=saved;if(!road.f.GetComponent<MeshCollider>())road.f.gameObject.AddComponent<MeshCollider>();foreach(var c in road.f.GetComponents<MeshCollider>()){c.sharedMesh=null;c.sharedMesh=saved;}
 }
 var details=new GameObject("Final Environment Underpasses");details.transform.SetParent(stage.transform,false);
 var concrete=GetMaterial("Tunnel Concrete",new Color(.66f,.66f,.65f));
 var trim=GetMaterial("Portal Stone",new Color(.38f,.39f,.4f));
 var glow=GetMaterial("Tunnel Reflectors",new Color(1,.75f,.24f),true);
 for(int i=0;i<portals.Count;i++){MakePortal(portals[i],details.transform,i+1,concrete,trim,glow);log.AppendLine("Underpass "+(i+1)+" center="+portals[i].p+" clearance="+portals[i].clearance+" inner width="+portals[i].width);}
 // Validate the staged geometry before committing the scene.
 int probes=0;
 foreach(var field in fields){
 var collider=field.road.f.GetComponent<MeshCollider>();if(!collider)throw new Exception("Selected road has no collider.");
 for(float t=.02f;t<.99f;t+=.04f){
 var p=field.road.bounds.center; if(field.x)p.x=Mathf.Lerp(field.start,field.end,t);else p.z=Mathf.Lerp(field.start,field.end,t);
 if(!Surface(field.road,p,out float y))continue;p.y=y;float expected=p.y+Offset(p);
 if(!collider.Raycast(new Ray(p+Vector3.up*3,Vector3.down),out var hit,6)||Mathf.Abs(hit.point.y-expected)>.1f)throw new Exception("Road collision height mismatch on "+field.road.f.name);
 probes++;
 }
 }
 foreach(var portal in portals)foreach(var col in portal.go.GetComponentsInChildren<MeshCollider>()){
 if(col.Raycast(new Ray(portal.p-portal.dir*18+Vector3.up*1.5f,portal.dir),out var hit,36))throw new Exception("Underpass driving passage obstructed.");
 }
 foreach(var change in changes){
 var f=original.GetComponentsInChildren<MeshFilter>(true).Single(v=>v.name==change.name);f.sharedMesh=change.after;if(!f.GetComponent<MeshCollider>())f.gameObject.AddComponent<MeshCollider>();
 foreach(var col in f.GetComponents<MeshCollider>()){col.sharedMesh=null;col.sharedMesh=change.after;PrefabUtility.RecordPrefabInstancePropertyModifications(col);}
 PrefabUtility.RecordPrefabInstancePropertyModifications(f);
 }
 var copy=UnityEngine.Object.Instantiate(details,original.transform,false);copy.name=details.name;
 UnityEngine.Object.DestroyImmediate(stage);stage=null;
 var prefab=PrefabUtility.SaveAsPrefabAssetAndConnect(original,PrefabPath,InteractionMode.AutomatedAction,out bool ok);
 if(!ok||!prefab)throw new Exception("Scene-specific road prefab save failed.");applied=true;
 int aligned=0;var propRoot=scene.GetRootGameObjects().FirstOrDefault(g=>g.name=="Roads Props");
 if(propRoot){
 var props=propRoot.GetComponentsInChildren<MeshRenderer>(true).Select(r=>PrefabUtility.GetOutermostPrefabInstanceRoot(r.gameObject)).Where(g=>g).Distinct().ToArray();
 foreach(var prop in props){
 var renderers=prop.GetComponentsInChildren<Renderer>(true);if(renderers.Length==0)continue;
 var bounds=renderers[0].bounds;foreach(var r in renderers.Skip(1))bounds.Encapsulate(r.bounds);
 var p=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);float delta=Offset(p);
 if(Mathf.Abs(delta)<.002f)continue;prop.transform.position+=Vector3.up*delta;PrefabUtility.RecordPrefabInstancePropertyModifications(prop.transform);aligned++;
 }
 }
 AssetDatabase.SaveAssets();Physics.SyncTransforms();
 lighting=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CityDayNightController>(true)).FirstOrDefault();night=lighting&&lighting.IsNight;if(lighting)lighting.ApplyDay();
 for(int i=0;i<portals.Count;i++)Capture(scene,portals[i].p-portals[i].dir*34+Vector3.up*3.5f,portals[i].p+Vector3.up*2.8f,"Underpass-"+(i+1));
 var first=fields.First();var direction=first.x?Vector3.right:Vector3.forward;var lateral=Vector3.Cross(Vector3.up,direction);
 Capture(scene,first.road.bounds.center-direction*70+lateral*4+Vector3.up*2,first.road.bounds.center+Vector3.up*.3f,"Gentle-Road");
 Capture(scene,new Vector3(130,1500,-90),new Vector3(130,28,-90),"Overview",true,850);
 if(lighting){if(night)lighting.ApplyNight();else lighting.ApplyDay();}
 EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new Exception("Scene save failed.");
 log.AppendLine("SUCCESS: two underpasses; six gentle rise-and-dip road stretches; mesh assets changed="+changes.Count+"; roadside props aligned="+aligned+"; road collider probes="+probes+".");
 log.AppendLine("Scene-specific prefab="+PrefabPath+". Existing road textures and widths retained. Shared materials and static tunnel meshes; no extra realtime lights. Play-mode driving and device frame timing remain untested.");
 }catch(Exception e){log.AppendLine("FAILED "+e+"; applied="+applied);Debug.LogException(e);}
 finally{if(stage)UnityEngine.Object.DestroyImmediate(stage);if(lighting&&night)lighting.ApplyNight();File.WriteAllText("FinalEnvironmentRoadReview/BuildReport.txt",log.ToString());}
 }
}


