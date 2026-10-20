using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using UnityEditor;
[InitializeOnLoad]public static class CityPropAudit {
 [Serializable]public class Item {public string id,name,path,prefab,materials;public Vector3 position,rotation,scale,center,size;}
 [Serializable]public class Data {public Item[] items;}
 static CityPropAudit(){EditorApplication.delayCall+=Run;}
 static string Path(Transform t){return t.parent?Path(t.parent)+"/"+t.name:t.name;}
 static void Run(){
  if(!File.Exists("CityPropAudit.request"))return;File.Delete("CityPropAudit.request");
  try{
   Directory.CreateDirectory("PropPlacementReview");
   var scene=SceneManager.GetSceneByPath("Assets/Scenes/Oasis/Gangstar City.unity");
   var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>(true)).Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
   var items=all.Select(r=>new Item{id=GlobalObjectId.GetGlobalObjectIdSlow(r.gameObject).ToString(),name=r.name,path=Path(r.transform),prefab=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(r.gameObject),materials=string.Join("|",r.sharedMaterials.Select(m=>m?m.name:"")),position=r.transform.position,rotation=r.transform.eulerAngles,scale=r.transform.lossyScale,center=r.bounds.center,size=r.bounds.size}).ToArray();
   File.WriteAllText("PropPlacementReview/SceneObjects.json",JsonUtility.ToJson(new Data{items=items}));
   var b=new StringBuilder();foreach(var g in items.GroupBy(i=>i.prefab).OrderByDescending(g=>g.Count()))b.AppendLine(g.Count()+" "+g.Key);
   File.WriteAllText("PropPlacementReview/PrefabCounts.txt",b.ToString());
   var light=UnityEngine.Object.FindObjectOfType<CityDayNightController>(true);bool night=light&&light.IsNight;if(light)light.ApplyDay();
   Capture(new Vector3(130,1500,-90),Quaternion.Euler(90,0,0),850,"Overview",true);
   if(night)light.ApplyNight();
   File.WriteAllText("CityPropAuditReport.txt","SUCCESS: "+items.Length+" renderers exported.");
  }catch(Exception e){File.WriteAllText("CityPropAuditReport.txt",e.ToString());Debug.LogException(e);}
 }
 public static void Capture(Vector3 position,Quaternion rotation,float size,string name,bool ortho){
  var go=new GameObject("Prop Preview Camera"){hideFlags=HideFlags.HideAndDontSave};var c=go.AddComponent<Camera>();c.enabled=false;c.transform.SetPositionAndRotation(position,rotation);c.orthographic=ortho;c.orthographicSize=size;c.fieldOfView=65;c.farClipPlane=4000;c.nearClipPlane=.3f;c.clearFlags=CameraClearFlags.Skybox;
  c.GetUniversalAdditionalCameraData().renderPostProcessing=false;
  var rt=RenderTexture.GetTemporary(1600,1600,24,RenderTextureFormat.ARGB32);var old=RenderTexture.active;
  try{c.targetTexture=rt;c.Render();RenderTexture.active=rt;var image=new Texture2D(1600,1600,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1600,1600),0,0);image.Apply();File.WriteAllBytes("PropPlacementReview/"+name+".png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);}
  finally{c.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(go);}
 }
}

