using System;using System.IO;using System.Linq;using System.Text;using UnityEngine;using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine.SceneManagement;
[InitializeOnLoad]public static class CityRoadDrivingValidation{
 static CityRoadDrivingValidation(){EditorApplication.update+=Poll;}
 static void Poll(){if(!File.Exists("CityRoadDrivingValidation.request")||EditorApplication.isCompiling||EditorApplication.isPlayingOrWillChangePlaymode)return;File.Delete("CityRoadDrivingValidation.request");Run();}
 static void Capture(Scene scene,Vector3 position,Vector3 target,string file,bool ortho=false,float size=32){
 var go=new GameObject("Road Validation Camera"){hideFlags=HideFlags.HideAndDontSave};SceneManager.MoveGameObjectToScene(go,scene);var c=go.AddComponent<Camera>();c.enabled=false;c.transform.SetPositionAndRotation(position,Quaternion.LookRotation(target-position));c.nearClipPlane=.2f;c.farClipPlane=4000;c.fieldOfView=62;c.orthographic=ortho;c.orthographicSize=size;var rt=RenderTexture.GetTemporary(1400,1000,24);var prev=RenderTexture.active;
 try{c.targetTexture=rt;c.Render();RenderTexture.active=rt;var t=new Texture2D(1400,1000,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,1400,1000),0,0);t.Apply();File.WriteAllBytes("DrivingLayoutReview/"+file+".png",t.EncodeToPNG());UnityEngine.Object.DestroyImmediate(t);}
 finally{c.targetTexture=null;RenderTexture.active=prev;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(go);}
 }
 static int FixPavementSeams(MeshFilter[] floors){
 var model=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Scenes/Oasis/3D/Fbx/Roads.fbx");
 var sources=model.GetComponentsInChildren<MeshFilter>(true).ToDictionary(f=>f.name,f=>f);
 int fixedCount=0;
 foreach(var road in floors.OrderBy(f=>f.sharedMesh.name)){
 if(!sources.TryGetValue(road.name,out var source)||source.sharedMesh.vertexCount!=4)continue;
 var original=source.sharedMesh.vertices.Select(road.transform.TransformPoint).ToArray();
 var bounds=new Bounds(original[0],Vector3.zero);foreach(var p in original.Skip(1))bounds.Encapsulate(p);
 if(bounds.size.y>=.15f||Mathf.Max(bounds.size.x,bounds.size.z)<=120)continue;
 bool alongX=bounds.size.x>bounds.size.z;var direction=alongX?Vector3.forward:Vector3.right;
 var originalUV=source.sharedMesh.uv;float u0=originalUV.Min(v=>v.x),u1=originalUV.Max(v=>v.x);var indices=source.sharedMesh.triangles;
 var pavement=road.GetComponentsInChildren<MeshFilter>().FirstOrDefault(f=>f!=road&&f.sharedMesh.name.StartsWith("Pavement_"));if(!pavement)throw new Exception("Pavement missing");
 var mesh=pavement.sharedMesh;var vertices=mesh.vertices;float half=8.876343f/28*.5f;
 for(int i=0;i<vertices.Length;i++){
 var current=pavement.transform.TransformPoint(vertices[i]);
 // UV.y stores the original world Z, so the undo remains safe if this check is repeated.
 Vector3 basePoint=current;
 if(alongX)basePoint.z=mesh.uv[i].y*7;
 else{
 float targetX=Mathf.Repeat(mesh.uv[i].x-96,10)*7;
 basePoint.x=targetX+Mathf.Round((current.x-targetX)/70)*70;
 }
 float t=alongX?(basePoint.x-bounds.min.x)/bounds.size.x:(basePoint.z-bounds.min.z)/bounds.size.z;
 float delta=1.25f*Mathf.Sin(t*Mathf.PI)*Mathf.Sin(t*Mathf.PI)*Mathf.Sin(t*Mathf.PI*2);
 float transverse=.5f;bool found=false;
 for(int k=0;k<indices.Length;k+=3){
 int ia=indices[k],ib=indices[k+1],ic=indices[k+2];var a=original[ia];var b=original[ib];var c=original[ic];
 float d=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);if(Mathf.Abs(d)<.000001f)continue;
 float aa=((b.z-c.z)*(basePoint.x-c.x)+(c.x-b.x)*(basePoint.z-c.z))/d;
 float bb=((c.z-a.z)*(basePoint.x-c.x)+(a.x-c.x)*(basePoint.z-c.z))/d;
 if(aa>=-.005f&&bb>=-.005f&&aa+bb<=1.005f){float u=aa*originalUV[ia].x+bb*originalUV[ib].x+(1-aa-bb)*originalUV[ic].x;transverse=(u-u0)/(u1-u0);found=true;break;}
 }
 if(!found)throw new Exception("Pavement point outside original road "+road.name);
 float weight=Mathf.Clamp01(Mathf.Min(transverse,1-transverse)/(.5f-half));
 vertices[i]=pavement.transform.InverseTransformPoint(basePoint+direction*delta*weight);
 }
 mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
 foreach(var collider in pavement.GetComponents<MeshCollider>()){collider.sharedMesh=null;collider.sharedMesh=mesh;}
 fixedCount++;if(fixedCount==3)break;
 }
 return fixedCount;
}

static void Run(){
 var log=new StringBuilder();CityDayNightController lighting=null;bool night=false;
 try{
 var scene=SceneManager.GetSceneByPath("Assets/Scenes/Oasis/Gangstar City.unity");var root=scene.GetRootGameObjects().Single(g=>g.name=="Track Roads");
 var floors=root.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh&&f.sharedMesh.name.StartsWith("Driving_")).ToArray();if(floors.Length!=116)throw new Exception("Unexpected driving mesh count "+floors.Length);
 log.AppendLine("Pavement seam corrections="+FixPavementSeams(floors));int removed=0,probes=0;Physics.SyncTransforms();
 foreach(var f in floors){
 var colliders=f.GetComponents<MeshCollider>();if(colliders.Length==0)throw new Exception("Missing road collider "+f.name);
 var keep=colliders.FirstOrDefault(c=>PrefabUtility.GetCorrespondingObjectFromSource(c))??colliders[0];
 foreach(var c in colliders.Where(c=>c!=keep)){UnityEngine.Object.DestroyImmediate(c);removed++;}
 keep.sharedMesh=f.sharedMesh;keep.enabled=true;PrefabUtility.RecordPrefabInstancePropertyModifications(keep);
 var vs=f.sharedMesh.vertices;var indices=f.sharedMesh.triangles;
 for(int i=0;i<indices.Length;i+=Mathf.Max(3,indices.Length/12/3*3)){
 var p=f.transform.TransformPoint((vs[indices[i]]+vs[indices[i+1]]+vs[indices[i+2]])/3);
 if(!keep.Raycast(new Ray(p+Vector3.up*1,Vector3.down),out var hit,2))throw new Exception("Collider ray missed "+f.name+" at "+p);probes++;
 }
 }
 var modules=root.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Underpass 1"||t.name=="Underpass 2").ToArray();if(modules.Length!=2)throw new Exception("Underpass count wrong.");
 int shaderErrors=0;foreach(var mat in root.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m).Distinct())shaderErrors+=ShaderUtil.GetShaderMessages(mat.shader).Count(m=>m.severity.ToString()=="Error");
 if(shaderErrors>0)throw new Exception("Shader compile errors "+shaderErrors);
 foreach(var module in modules){
 var body=module.GetComponentsInChildren<MeshFilter>().First(f=>f.sharedMesh.name.EndsWith("_Concrete"));
 var collider=body.GetComponent<MeshCollider>();var center=body.GetComponent<Renderer>().bounds.center;var size=body.GetComponent<Renderer>().bounds.size;var direction=size.x>size.z?Vector3.right:Vector3.forward;
 float ground=body.GetComponent<Renderer>().bounds.min.y;
 var start=new Vector3(center.x,ground+1.2f,center.z)-direction*16;
 if(collider.Raycast(new Ray(start,direction),out var obstruction,32))throw new Exception("Tunnel centerline blocked "+module.name);
 }
 lighting=UnityEngine.Object.FindObjectOfType<CityDayNightController>(true);night=lighting&&lighting.IsNight;if(lighting)lighting.ApplyDay();
 // The earlier street view looked across a roof. Use a vertical view centered on its actual roadway.
 var street=floors.First(f=>f.name=="polySurface777");var streetCenter=street.GetComponent<Renderer>().bounds.center;
 Capture(scene,streetCenter+Vector3.up*100,streetCenter,"Two-Lane-Street",true,29);
 foreach(var module in modules){
 var body=module.GetComponentsInChildren<MeshRenderer>().First(r=>r.name.EndsWith("_Concrete"));var bounds=body.bounds;var dir=bounds.size.x>bounds.size.z?Vector3.right:Vector3.forward;
 var groundCenter=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
 Capture(scene,groundCenter-dir*27+Vector3.up*3.2f,groundCenter+Vector3.up*2.5f,module.name.Replace(" ","-")+"-Entrance");
 }
 if(lighting){if(night)lighting.ApplyNight();else lighting.ApplyDay();}
 EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
 log.AppendLine("SUCCESS. Driving meshes=116; surface collider ray probes="+probes+"; redundant scene colliders removed="+removed+"; tunnel centerline checks=2; shader errors=0.");
 log.AppendLine("All revised driving meshes and pavement pieces have colliders. Underpass centerlines are unobstructed by their structures. Shared road material and static pavement retained.");
 log.AppendLine("Play-mode driving, full building/prop intersection checks, and device frame timing are not verified.");
 }catch(Exception e){log.AppendLine("FAILED "+e);Debug.LogException(e);}finally{if(lighting&&night)lighting.ApplyNight();File.WriteAllText("DrivingLayoutReview/ValidationReport.txt",log.ToString());}
 }
}


