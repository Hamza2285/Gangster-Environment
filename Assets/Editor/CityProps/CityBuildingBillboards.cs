using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class CityBuildingBillboards {
 const string Folder="Assets/Scenes/Oasis/BuildingBillboards";
 class Geo {public Renderer r;public Vector3[] v;public Vector2[] uv;public int[] t;}
 class Site{public Geo g;public Vector3 pos,normal;public float width,height,baseY;public bool wall;public int art;}
 static List<Vector3> verts;static List<Vector2> uvs;static List<int> indices;
 static Dictionary<Texture,Texture2D> raw=new Dictionary<Texture,Texture2D>();
 static List<Site> sites=new List<Site>();
 static Renderer[] obstacles;
 [InitializeOnLoadMethod]static void Init(){EditorApplication.delayCall+=Run;EditorApplication.delayCall+=Refine;EditorApplication.delayCall+=FinalizePlacement;}
 static bool Hit(Geo g,Vector3 origin,Vector3 dir,out Vector3 point,out Vector2 tex,out Vector3 normal){
  float best=float.MaxValue;point=Vector3.zero;tex=Vector2.zero;normal=Vector3.zero;
  for(int j=0;j<g.t.Length;j+=3){int ia=g.t[j],ib=g.t[j+1],ic=g.t[j+2];var a=g.v[ia];var e1=g.v[ib]-a;var e2=g.v[ic]-a;
   var p=Vector3.Cross(dir,e2);float det=Vector3.Dot(e1,p);if(Mathf.Abs(det)<.000001f)continue;
   float inv=1/det;var s=origin-a;float u=Vector3.Dot(s,p)*inv;if(u<0||u>1)continue;var q=Vector3.Cross(s,e1);float v=Vector3.Dot(dir,q)*inv;if(v<0||u+v>1)continue;
   float d=Vector3.Dot(e2,q)*inv;if(d<.01f||d>=best)continue;best=d;point=origin+dir*d;tex=g.uv[ia]*(1-u-v)+g.uv[ib]*u+g.uv[ic]*v;normal=Vector3.Cross(e1,e2).normalized;
  }return best<float.MaxValue;
 }
 static Texture2D Raw(Texture tex){if(!tex)return null;if(raw.ContainsKey(tex))return raw[tex];var t=new Texture2D(2,2,TextureFormat.RGBA32,false,true);t.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(tex)));raw[tex]=t;return t;}
 static Vector3 Cardinal(Vector3 d){return Mathf.Abs(d.x)>Mathf.Abs(d.z)?new Vector3(Mathf.Sign(d.x),0,0):new Vector3(0,0,Mathf.Sign(d.z));}
 static bool Clear(Geo owner,Vector3 pos,float width,Geo[] all){return !all.Any(g=>g!=owner&&g.r.bounds.Contains(pos));}
 static Site Roof(Geo g,Vector3[] lamps,Geo[] all){
  var b=g.r.bounds;var lamp=lamps.OrderBy(p=>(new Vector2(p.x-b.center.x,p.z-b.center.z)).sqrMagnitude).First();
  var n=Cardinal(lamp-b.center);var right=Vector3.Cross(Vector3.up,n);float span=Mathf.Abs(n.x)>.5?b.size.z:b.size.x;
  float w=Mathf.Min(16,span*.65f);if(w<11)return null;float h=w/1.78f;
  var p=b.center+n*(Mathf.Abs(n.x)>.5?b.extents.x:b.extents.z)*.52f;
  if(!Hit(g,new Vector3(p.x,b.max.y+10,p.z),Vector3.down,out var roof,out _,out var norm)||Mathf.Abs(norm.y)<.9||roof.y<b.max.y-5)return null;
  float min=roof.y,max=roof.y;
  foreach(float x in new[]{-.36f,.36f})foreach(float z in new[]{0f,-2f}){
   var foot=roof+right*x*w+n*z;
   if(!Hit(g,new Vector3(foot.x,b.max.y+10,foot.z),Vector3.down,out var hit,out _,out norm)||Mathf.Abs(norm.y)<.9)return null;
   min=Mathf.Min(min,hit.y);max=Mathf.Max(max,hit.y);
  }
  if(max-min>.35f)return null;
  var center=new Vector3(roof.x,max+1.8f+h*.5f,roof.z);
  if(!Clear(g,center,w,all))return null;
  return new Site{g=g,pos=center,normal=n,width=w,height=h,baseY=min,wall=false};
 }
 static Site Wall(Geo g,Vector3[] lamps,Geo[] all){
  var b=g.r.bounds;var mat=g.r.sharedMaterial;var baseTex=Raw(mat.GetTexture("_BaseMap"));var mask=Raw(mat.GetTexture("_CitySurfaceMap"));
  if(!baseTex)return null;
  foreach(var n in new[]{Vector3.forward,Vector3.back,Vector3.right,Vector3.left}){
   float w=10,h=7;float span=Mathf.Abs(n.x)>.5?b.size.z:b.size.x;if(span<15)continue;
   var center=new Vector3(b.center.x,b.max.y-6.5f,b.center.z);var right=Vector3.Cross(Vector3.up,n);
   if(!Hit(g,center+n*100,-n,out var first,out _,out var norm)||Mathf.Abs(Vector3.Dot(norm,n))<.99f)continue;
   center=first;
   if(obstacles!=null&&obstacles.Any(o=>o!=g.r&&Blocked(o,center+n*55,-n,54)))continue;
   bool valid=true;float sum=0,sum2=0;
   for(int yy=0;yy<7&&valid;yy++)for(int xx=0;xx<9;xx++){
    var p=center+right*((xx/8f-.5f)*(w+1))+Vector3.up*((yy/6f-.5f)*(h+1));
    if(!Hit(g,p+n*2,-n,out var q,out var uv,out norm)||Mathf.Abs(Vector3.Dot(q-center,n))>.08f||Mathf.Abs(Vector3.Dot(norm,n))<.99f){valid=false;break;}
    var col=baseTex.GetPixelBilinear(uv.x,uv.y);float lum=col.grayscale;sum+=lum;sum2+=lum*lum;
    if(mask&&mask.GetPixelBilinear(uv.x,uv.y).b>.04f){valid=false;break;}
   }
   float mean=sum/63f;if(!valid||Mathf.Sqrt(Mathf.Max(0,sum2/63f-mean*mean))>.1f||mean<.08f)continue;
   // Keep outward view clear and favor a road-facing wall.
   if(!Clear(g,center+n*5,w,all)||!lamps.Any(l=>Vector3.Dot(l-center,n)>5&&Vector3.Distance(l,center)<75))continue;
   var site=new Site{g=g,pos=center+n*.22f,normal=n,width=w,height=h,wall=true};
   if(obstacles!=null&&!FindRoad(site,UnityEngine.Object.FindObjectOfType<CityDayNightController>(true),out _))continue;
   return site;
  }return null;
 }
 static void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector2 ua,Vector2 ub,Vector2 uc,Vector2 ud){
  int k=verts.Count;verts.AddRange(new[]{a,b,c,d});uvs.AddRange(new[]{ua,ub,uc,ud});indices.AddRange(new[]{k,k+1,k+2,k,k+2,k+3});
 }
 static void Box(Vector3 center,Vector3 size){
  var p=new Vector3[8];for(int i=0;i<8;i++)p[i]=center+new Vector3((i&1)==0?-size.x/2:size.x/2,(i&2)==0?-size.y/2:size.y/2,(i&4)==0?-size.z/2:size.z/2);
  var uv=new Vector2(.56f,.95f);
  int[,] faces={{0,2,3,1},{4,5,7,6},{0,4,6,2},{1,3,7,5},{0,1,5,4},{2,6,7,3}};
  for(int i=0;i<6;i++)Quad(p[faces[i,0]],p[faces[i,1]],p[faces[i,2]],p[faces[i,3]],uv,uv,uv,uv);
 }
 static void Beam(Vector3 a,Vector3 b,float size){
  int start=verts.Count;Box(Vector3.zero,new Vector3(size,Vector3.Distance(a,b),size));var rot=Quaternion.FromToRotation(Vector3.up,(b-a).normalized);
  for(int i=start;i<verts.Count;i++)verts[i]=(a+b)*.5f+rot*verts[i];
 }
 static Mesh Build(Site s,int index){
  verts=new List<Vector3>();uvs=new List<Vector2>();indices=new List<int>();
  float w=s.width,h=s.height;Box(new Vector3(0,0,-.16f),new Vector3(w+.24f,h+.24f,.3f));
  int[,] rects={{128,562,489,763},{118,772,551,1008},{567,809,1057,1053},{1072,808,1591,1058},{567,1072,1056,1311},{1072,1072,1591,1326},{570,1328,1052,1592},{1073,1342,1590,1592}};
  int a=s.art%8;float l=rects[a,0]/1600f,r=rects[a,2]/1600f,t=1-rects[a,1]/1600f,b=1-rects[a,3]/1600f;
  // Front is +Z; display image with its natural orientation.
  Quad(new Vector3(-w/2,-h/2,.001f),new Vector3(w/2,-h/2,.001f),new Vector3(w/2,h/2,.001f),new Vector3(-w/2,h/2,.001f),new Vector2(r,b),new Vector2(l,b),new Vector2(l,t),new Vector2(r,t));
  if(!s.wall){
   float bottom=s.baseY-s.pos.y;
   foreach(float x in new[]{-w*.35f,0,w*.35f}){
    Beam(new Vector3(x,bottom,-.4f),new Vector3(x,h*.45f,-.4f),.17f);
    Beam(new Vector3(x,bottom,-2),new Vector3(x,h*.35f,-.4f),.13f);
    Box(new Vector3(x,bottom+.08f,-1.1f),new Vector3(.6f,.16f,2.5f));
   }
   Beam(new Vector3(-w*.35f,-h*.45f,-.5f),new Vector3(w*.35f,h*.4f,-.5f),.10f);
   Beam(new Vector3(w*.35f,-h*.45f,-.5f),new Vector3(-w*.35f,h*.4f,-.5f),.10f);
  }
  var mesh=new Mesh{name="Building Billboard "+(index+1)};mesh.SetVertices(verts);mesh.SetUVs(0,uvs);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();
  return mesh;
 }
 static Dictionary<Renderer,Geo> obstacleGeo=new Dictionary<Renderer,Geo>();
 static bool Blocked(Renderer r,Vector3 origin,Vector3 dir,float limit){
  if(!r.bounds.IntersectRay(new Ray(origin,dir),out var near)||near>limit)return false;
  if(!obstacleGeo.TryGetValue(r,out var g)){
   var f=r.GetComponent<MeshFilter>();if(!f||!f.sharedMesh)return false;
   var m=f.sharedMesh;g=new Geo{r=r,v=m.vertices.Select(v=>f.transform.TransformPoint(v)).ToArray(),uv=m.uv,t=m.triangles};obstacleGeo[r]=g;
  }
  return Hit(g,origin,dir,out var p,out _,out _)&&Vector3.Distance(p,origin)<limit;
 }
 static bool FindRoad(Site s,CityDayNightController c,out Vector3 road){
  var candidates=c.streetLightPositions.Select(l=>new Vector3(l.x,31.2f,l.z)+Vector3.Cross(Vector3.up,s.normal)*2.5f).Where(l=>Vector3.Dot(l-s.pos,s.normal)>30&&Vector3.Distance(l,s.pos)<200).OrderBy(l=>Mathf.Abs(Vector3.Dot(l-s.pos,s.normal)-65)+Vector3.Cross(l-s.pos,s.normal).magnitude*.5f);
  foreach(var p in candidates){
   var delta=s.pos-p;float len=delta.magnitude;var dir=delta/len;
   if(obstacles.Any(o=>o!=s.g.r&&Blocked(o,p,dir,len-.5f)))continue;
   if(Hit(s.g,p,dir,out var hit,out _,out _)&&Vector3.Distance(p,hit)<len-.5f)continue;
   road=p;return true;
  }road=Vector3.zero;return false;
 }
 static void Refine(){
  if(!File.Exists("BuildingBillboardsRefine.request"))return;File.Delete("BuildingBillboardsRefine.request");
  var c=UnityEngine.Object.FindObjectOfType<CityDayNightController>(true);bool night=c.IsNight;bool async=ShaderUtil.allowAsyncCompilation;
  try{
   var root=GameObject.Find("Building Billboards - Sparse");if(!root)throw new Exception("Group missing");
   obstacles=UnityEngine.Object.FindObjectsOfType<MeshRenderer>().Where(r=>r.gameObject.scene==c.gameObject.scene&&r.enabled&&r.sharedMaterial&&r.sharedMaterial.name.StartsWith("Building")).Cast<Renderer>().ToArray();
   var all=obstacles.Where(r=>r.GetComponent<MeshFilter>()&&r.bounds.size.y>14&&r.bounds.size.y<50&&r.bounds.size.x>18&&r.bounds.size.x<90&&r.bounds.size.z>15&&r.bounds.size.z<90).Select(r=>{var f=r.GetComponent<MeshFilter>();return new Geo{r=r,v=f.sharedMesh.vertices.Select(v=>f.transform.TransformPoint(v)).ToArray(),uv=f.sharedMesh.uv,t=f.sharedMesh.triangles};}).ToArray();
   sites.Clear();
   float[] aspect={361f/201,433f/236,490f/244,519f/250,489f/239,519f/254,482f/264,517f/250};
   for(int i=0;i<root.transform.childCount;i++){
    var tr=root.transform.GetChild(i);var mf=tr.GetComponent<MeshFilter>();var mesh=mf.sharedMesh;var v=mesh.vertices;
    var originalName=tr.name.Substring(tr.name.IndexOf("Billboard - ")+12);var g=all.FirstOrDefault(a=>a.r.name==originalName);
    if(g==null)throw new Exception("Building missing: "+originalName);
    var site=new Site{g=g,pos=tr.position,normal=tr.forward,width=mesh.bounds.size.x-.24f,height=Mathf.Abs(v[26].y-v[24].y),baseY=tr.position.y+mesh.bounds.min.y,wall=i>=6,art=i};
    if(i==2||i==6){
     Site replacement=null;
     foreach(var candidate in all.OrderBy(a=>Vector3.Distance(a.r.bounds.center,tr.position))){
      if(root.transform.Cast<Transform>().Any(other=>other!=tr&&Vector2.Distance(new Vector2(other.position.x,other.position.z),new Vector2(candidate.r.bounds.center.x,candidate.r.bounds.center.z))<190))continue;
      replacement=i==2?Roof(candidate,c.streetLightPositions,all):Wall(candidate,c.streetLightPositions,all);if(replacement!=null&&FindRoad(replacement,c,out _))break;replacement=null;
     }
     if(replacement==null)throw new Exception("No clear replacement for index "+i);
     site=replacement;site.art=i;
    }
    float newHeight=site.width/aspect[i];if(!site.wall)site.pos+=Vector3.up*(newHeight-site.height)*.5f;site.height=newHeight;
    tr.SetPositionAndRotation(site.pos,Quaternion.LookRotation(site.normal));tr.name=(i+1).ToString("00")+" "+(site.wall?"Wall":"Rooftop")+" Billboard - "+site.g.r.name;
    var replacementMesh=Build(site,i);mesh.Clear();mesh.vertices=replacementMesh.vertices;mesh.uv=replacementMesh.uv;mesh.triangles=replacementMesh.triangles;mesh.normals=replacementMesh.normals;mesh.tangents=replacementMesh.tangents;mesh.RecalculateBounds();mesh.UploadMeshData(false);UnityEngine.Object.DestroyImmediate(replacementMesh);EditorUtility.SetDirty(mesh);
    tr.GetComponent<LODGroup>().RecalculateBounds();sites.Add(site);
   }
   AssetDatabase.SaveAssets();ShaderUtil.allowAsyncCompilation=false;
   var csv=new List<string>{"index,type,building,x,y,z,width,height,triangles"};
   int total=0;
   for(int i=0;i<sites.Count;i++){
    var s=sites[i];int tris=root.transform.GetChild(i).GetComponent<MeshFilter>().sharedMesh.triangles.Length/3;total+=tris;
    csv.Add($"{i+1},{(s.wall?"Wall":"Rooftop")},{s.g.r.name},{s.pos.x},{s.pos.y},{s.pos.z},{s.width},{s.height},{tris}");
    var pos=s.pos+s.normal*s.width*2.4f+Vector3.up*2;
    c.ApplyDay();CityLightingBuilder.Capture(c,Tuple.Create(pos,Quaternion.LookRotation(s.pos-pos)),"BuildingBoard-Day-"+(i+1));
    c.ApplyNight();CityLightingBuilder.Capture(c,Tuple.Create(pos,Quaternion.LookRotation(s.pos-pos)),"BuildingBoard-Night-"+(i+1));
    var candidates=c.streetLightPositions.Select(l=>new Vector3(l.x,31.2f,l.z)+Vector3.Cross(Vector3.up,s.normal)*2.5f).Where(l=>Vector3.Dot(l-s.pos,s.normal)>30&&Vector3.Distance(l,s.pos)<200).OrderBy(l=>Mathf.Abs(Vector3.Dot(l-s.pos,s.normal)-65)+Vector3.Cross(l-s.pos,s.normal).magnitude*.5f).ToArray();
    Vector3 road=Vector3.zero;bool found=false;
    foreach(var candidate in candidates){
     var delta=s.pos-candidate;float length=delta.magnitude;var dir=delta/length;
     if(obstacles.Any(o=>o!=s.g.r&&Blocked(o,candidate,dir,length-.5f)))continue;
     if(Hit(s.g,candidate,dir,out var hit,out _,out _)&&Vector3.Distance(candidate,hit)<length-.5f)continue;
     road=candidate;found=true;break;
    }
    if(!found){road=s.pos+s.normal*65;road.y=31.2f;File.AppendAllText("BuildingBillboardReview/Visibility.txt","No clear street-lamp view for "+(i+1)+"\n");}
    road.y=31.2f;CityLightingBuilder.Capture(c,Tuple.Create(road,Quaternion.LookRotation(s.pos-road)),"BuildingBoard-Road-"+(i+1));
   }
   File.WriteAllLines("BuildingBillboardReview/Placements.csv",csv);
   File.WriteAllText("BuildingBillboardReview/VisibilityFinal.txt",string.Join("\n",sites.Select((s,i)=>(i+1)+": "+(FindRoad(s,c,out _)?"CLEAR ROAD VIEW":"CHECK REQUIRED"))));
   File.AppendAllText("BuildingBillboardReview/Report.txt","\nREFINED: corrected readable front UVs, preserved all eight image aspect ratios, moved obstructed wall board. Total "+total+" triangles across eight meshes. Street-view and close-up day/night captures rendered.");
   if(night)c.ApplyNight();else c.ApplyDay();EditorSceneManager.MarkSceneDirty(c.gameObject.scene);EditorSceneManager.SaveScene(c.gameObject.scene);
  }catch(Exception e){File.AppendAllText("BuildingBillboardReview/Report.txt","\nREFINE FAILED "+e);Debug.LogException(e);}
  finally{foreach(var t in raw.Values)UnityEngine.Object.DestroyImmediate(t);raw.Clear();if(night)c.ApplyNight();else c.ApplyDay();ShaderUtil.allowAsyncCompilation=async;}
 }
 static void FinalizePlacement(){
  if(!File.Exists("BuildingBillboardsFinal.request"))return;File.Delete("BuildingBillboardsFinal.request");
  var c=UnityEngine.Object.FindObjectOfType<CityDayNightController>(true);bool night=c.IsNight;bool async=ShaderUtil.allowAsyncCompilation;
  try{
   var root=GameObject.Find("Building Billboards - Sparse");
   var rejected=root.transform.Cast<Transform>().FirstOrDefault(t=>t.name.StartsWith("07 Wall"));
   if(rejected)UnityEngine.Object.DestroyImmediate(rejected.gameObject);
   obstacles=UnityEngine.Object.FindObjectsOfType<MeshRenderer>().Where(r=>r.gameObject.scene==c.gameObject.scene&&r.enabled&&r.sharedMaterial&&r.sharedMaterial.name.StartsWith("Building")).Cast<Renderer>().ToArray();
   ShaderUtil.allowAsyncCompilation=false;var rows=new List<string>{"index,type,building,x,y,z,triangles,clearRoadView"};int total=0;
   var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/Scenes/Oasis/3D/Fbx/Materials/Roads Props 01.mat");
   for(int i=0;i<root.transform.childCount;i++){
    var tr=root.transform.GetChild(i);string name=tr.name.Substring(tr.name.IndexOf("Billboard - ")+12);
    var renderer=obstacles.First(r=>r.name==name);var f=renderer.GetComponent<MeshFilter>();
    var g=new Geo{r=renderer,v=f.sharedMesh.vertices.Select(v=>f.transform.TransformPoint(v)).ToArray(),uv=f.sharedMesh.uv,t=f.sharedMesh.triangles};
    var mesh=tr.GetComponent<MeshFilter>().sharedMesh;
    var site=new Site{g=g,pos=tr.position,normal=tr.forward,width=mesh.bounds.size.x-.24f,wall=tr.name.Contains("Wall")};
    tr.name=(i+1).ToString("00")+" "+(site.wall?"Wall":"Rooftop")+" Billboard - "+name;
    bool clear=FindRoad(site,c,out var road);if(!clear)throw new Exception("No road view for "+tr.name);
    if(tr.GetComponent<MeshRenderer>().sharedMaterial!=material)throw new Exception("Material not shared");
    if(tr.GetComponent<LODGroup>().GetLODs()[0].screenRelativeTransitionHeight!=.025f)throw new Exception("LOD missing");
    int triangles=mesh.triangles.Length/3;total+=triangles;
    rows.Add($"{i+1},{(site.wall?"Wall":"Rooftop")},{name},{tr.position.x},{tr.position.y},{tr.position.z},{triangles},{clear}");
    c.ApplyNight();CityLightingBuilder.Capture(c,Tuple.Create(road,Quaternion.LookRotation(site.pos-road)),"BuildingBoard-Final-Night-"+(i+1));
    c.ApplyDay();CityLightingBuilder.Capture(c,Tuple.Create(road,Quaternion.LookRotation(site.pos-road)),"BuildingBoard-Final-Day-"+(i+1));
   }
   if(root.transform.childCount!=7||total!=890)throw new Exception("Geometry budget mismatch");
   if(night)c.ApplyNight();else c.ApplyDay();AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(c.gameObject.scene);EditorSceneManager.SaveScene(c.gameObject.scene);Selection.activeGameObject=root;
   File.WriteAllLines("BuildingBillboardReview/FinalPlacements.csv",rows);
   File.WriteAllText("BuildingBillboardReview/FinalReport.txt","SUCCESS: 7 billboards saved (6 rooftop, 1 wall). 890 triangles total, 7 renderers, shared existing Roads Props 01 material and atlas. No extra textures, materials, lights, colliders or per-frame scripts. LOD culling at 2.5% screen height, shadow casting disabled. All seven passed geometric visibility checks from road-side viewpoints; final Day and Night previews rendered. Rejected one obscured wall location. Android device FPS not measured.");
  }catch(Exception e){File.WriteAllText("BuildingBillboardReview/FinalReport.txt","FAILED "+e);Debug.LogException(e);}
  finally{ShaderUtil.allowAsyncCompilation=async;if(night)c.ApplyNight();else c.ApplyDay();}
 }
 static void Run(){
  if(!File.Exists("BuildingBillboards.request"))return;File.Delete("BuildingBillboards.request");
  Directory.CreateDirectory("BuildingBillboardReview");Directory.CreateDirectory(Folder);
  var c=UnityEngine.Object.FindObjectOfType<CityDayNightController>(true);bool night=c&&c.IsNight;bool async=ShaderUtil.allowAsyncCompilation;GameObject group=null;
  try{
   if(GameObject.Find("Building Billboards - Sparse"))throw new Exception("Billboard group already exists; no duplicates created.");
   int mats=AssetDatabase.FindAssets("t:Material").Length,lights=UnityEngine.Object.FindObjectsOfType<Light>(true).Length;
   EditorSceneManager.SaveScene(c.gameObject.scene,"Assets/Editor/PropsBackups/BeforeBuildingBillboards.unity",true);
   var all=UnityEngine.Object.FindObjectsOfType<MeshRenderer>().Where(r=>r.gameObject.scene==c.gameObject.scene&&r.enabled&&r.sharedMaterials.Length==1&&r.sharedMaterial&&r.sharedMaterial.name.StartsWith("Building")&&r.GetComponent<MeshFilter>()&&r.bounds.size.y>14&&r.bounds.size.y<50&&r.bounds.size.x>18&&r.bounds.size.x<90&&r.bounds.size.z>15&&r.bounds.size.z<90).Select(r=>{
    var f=r.GetComponent<MeshFilter>();var m=f.sharedMesh;return new Geo{r=r,v=m.vertices.Select(v=>f.transform.TransformPoint(v)).ToArray(),uv=m.uv,t=m.triangles};
   }).ToArray();
   var lamps=c.streetLightPositions;sites.Clear();
   var aims=new[]{new Vector3(-350,0,300),new Vector3(430,0,100),new Vector3(-330,0,-280),new Vector3(490,0,-330),new Vector3(-50,0,40),new Vector3(80,0,-530),new Vector3(290,0,390),new Vector3(-30,0,-280)};
   for(int i=0;i<8;i++){
    Site chosen=null;
    foreach(var g in all.OrderBy(g=>Vector2.Distance(new Vector2(g.r.bounds.center.x,g.r.bounds.center.z),new Vector2(aims[i].x,aims[i].z)))){
     if(sites.Any(s=>s.g==g||Vector2.Distance(new Vector2(s.pos.x,s.pos.z),new Vector2(g.r.bounds.center.x,g.r.bounds.center.z))<190))continue;
     chosen=i>=6?Wall(g,lamps,all):Roof(g,lamps,all);if(chosen!=null)break;
    }
    if(chosen==null)throw new Exception("No suitable site for board "+(i+1));
    chosen.art=i;sites.Add(chosen);
   }
   var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/Scenes/Oasis/3D/Fbx/Materials/Roads Props 01.mat");
   group=new GameObject("Building Billboards - Sparse");var parent=GameObject.Find("Roads Props");if(parent)group.transform.SetParent(parent.transform,true);
   var lines=new List<string>{"index,type,building,x,y,z,width,height,triangles"};
   for(int i=0;i<sites.Count;i++){
    var s=sites[i];var go=new GameObject((i+1).ToString("00")+" "+(s.wall?"Wall":"Rooftop")+" Billboard - "+s.g.r.name);
    go.transform.SetParent(group.transform);go.transform.SetPositionAndRotation(s.pos,Quaternion.LookRotation(s.normal));
    var mesh=Build(s,i);AssetDatabase.CreateAsset(mesh,Folder+"/Billboard "+(i+1)+".asset");
    go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=material;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=true;r.lightProbeUsage=LightProbeUsage.Off;
    GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccludeeStatic);
    var lod=go.AddComponent<LODGroup>();lod.fadeMode=LODFadeMode.None;lod.SetLODs(new[]{new LOD(.025f,new Renderer[]{r})});lod.RecalculateBounds();
    lines.Add($"{i+1},{(s.wall?"Wall":"Rooftop")},{s.g.r.name},{s.pos.x},{s.pos.y},{s.pos.z},{s.width},{s.height},{mesh.triangles.Length/3}");
   }
   AssetDatabase.SaveAssets();ShaderUtil.allowAsyncCompilation=false;
   for(int i=0;i<sites.Count;i++){
    var s=sites[i];var pos=s.pos+s.normal*(s.width*2.4f)+Vector3.up*2;
    c.ApplyDay();CityLightingBuilder.Capture(c,Tuple.Create(pos,Quaternion.LookRotation(s.pos-pos)),"BuildingBoard-Day-"+(i+1));
    c.ApplyNight();CityLightingBuilder.Capture(c,Tuple.Create(pos,Quaternion.LookRotation(s.pos-pos)),"BuildingBoard-Night-"+(i+1));
   }
   if(mats!=AssetDatabase.FindAssets("t:Material").Length||lights!=UnityEngine.Object.FindObjectsOfType<Light>(true).Length)throw new Exception("Material/light count changed");
   File.WriteAllLines("BuildingBillboardReview/Placements.csv",lines);
   File.WriteAllText("BuildingBillboardReview/Report.txt","SUCCESS: 8 billboards (6 rooftop, 2 wall), minimum 190m between additions. Existing atlas/material reused. 8 combined meshes/renderers, no lights or colliders. LOD culls below 2.5% screen height. Shadows off; existing buildings unchanged. Day/night captures saved. Review placement images before delivery.");
   if(night)c.ApplyNight();else c.ApplyDay();EditorSceneManager.MarkSceneDirty(c.gameObject.scene);EditorSceneManager.SaveScene(c.gameObject.scene);Selection.activeGameObject=group;
  }catch(Exception e){File.WriteAllText("BuildingBillboardReview/Report.txt","FAILED: "+e);Debug.LogException(e);}
  finally{foreach(var t in raw.Values)UnityEngine.Object.DestroyImmediate(t);raw.Clear();ShaderUtil.allowAsyncCompilation=async;if(c){if(night)c.ApplyNight();else c.ApplyDay();}}
 }
}






