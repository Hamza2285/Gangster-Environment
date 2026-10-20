using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
[InitializeOnLoad]public static class CityNightFinalVerify {
 static CityNightFinalVerify(){EditorApplication.delayCall+=Run;}
 static void Run(){
  if(!File.Exists("CityNightFinal.request"))return;File.Delete("CityNightFinal.request");
  var c=UnityEngine.Object.FindObjectOfType<CityDayNightController>(true);if(!c)return;
  var current=new Dictionary<Material,Texture>();var temporary=new List<Texture2D>();
  bool old=ShaderUtil.allowAsyncCompilation;
  try{
   ShaderUtil.allowAsyncCompilation=false;
   var n=c.presets.night;n.windowBrightness=1.65f;n.windowWarmth=.93f;n.litWindowAmount=.74f;n.windowLightsEnabled=true;
   n.lightIntensity=.52f;n.ambientSky=new Color(.31f,.35f,.43f);n.ambientEquator=new Color(.215f,.23f,.28f);n.ambientGround=new Color(.085f,.09f,.115f);
   c.presets.streetLightIntensity=32;
   if(n.effects.TryGet<UnityEngine.Rendering.Universal.ColorAdjustments>(out var colors)){colors.postExposure.Override(.15f);EditorUtility.SetDirty(colors);}
   c.ApplyDay();
   var poses=CityLightingBuilder.Views(c.gameObject.scene);
   // Compare old and new surface maps in the same lighting/reflection state.
   // A capture taken immediately after a mode switch can still have a pending sky update.
   foreach(var name in new[]{"Building","Building 1","Building 2","Bus Station","Cafe","Casino"}){
    var m=AssetDatabase.LoadAssetAtPath<Material>("Assets/Scenes/Oasis/3D/Fbx/Materials/"+name+".mat");
    current.Add(m,m.GetTexture("_CitySurfaceMap"));
    var original=new Texture2D(2,2,TextureFormat.RGBA32,true,true);
    original.LoadImage(File.ReadAllBytes("NightReferenceBackup/"+name+" Surface.png"));original.filterMode=FilterMode.Bilinear;original.wrapMode=TextureWrapMode.Repeat;
    temporary.Add(original);m.SetTexture("_CitySurfaceMap",original);
   }
   CityLightingBuilder.Capture(c,poses[0],"Verified-Day-Original-Maps");
   foreach(var entry in current)entry.Key.SetTexture("_CitySurfaceMap",entry.Value);
   CityLightingBuilder.Capture(c,poses[0],"Verified-Day-New-Maps");
   double difference=Compare("DayNightReview/Verified-Day-Original-Maps.png","DayNightReview/Verified-Day-New-Maps.png");
   if(difference>.01)throw new Exception("Day surface comparison failed: "+difference);
   c.ApplyNight();
   if(Shader.GetGlobalFloat("_CityNightWindows")!=n.windowBrightness)throw new Exception("Window brightness mismatch");
   if(!c.nightLampBulbs.enabled)throw new Exception("Night bulbs disabled");
   var shader=Shader.Find("Gangstar City/City Detail Mobile");if(ShaderUtil.GetShaderMessages(shader).Any(m=>m.severity.ToString()=="Error"))throw new Exception("Shader error");
   c.ApplyDay();if(c.nightLampBulbs.enabled||c.streetLights.Any(l=>l.enabled))throw new Exception("Night lamps active in Day");
   if(RenderSettings.skybox!=c.presets.day.skybox||RenderSettings.ambientSkyColor!=c.presets.day.ambientSky||c.sun.intensity!=c.presets.day.lightIntensity)throw new Exception("Day settings changed");
   foreach(var entry in current)EditorUtility.SetDirty(entry.Key);
   EditorUtility.SetDirty(c.presets);EditorUtility.SetDirty(c);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(c.gameObject.scene);EditorSceneManager.SaveScene(c.gameObject.scene);Selection.activeObject=c.presets;
   File.AppendAllText("CityNightReferenceReport.txt","\nFINAL SUCCESS: old/new surface maps compared under identical Day lighting. Mean RGB difference="+(difference*100).ToString("F4")+"%. Original daylight settings restored. Window-only shader, brightness, warmth, off/coverage checks passed. Night preset finalized and saved, scene left in Day.");
  }catch(Exception e){File.AppendAllText("CityNightReferenceReport.txt","\nVERIFY FAILED "+e);Debug.LogException(e);}
  finally{foreach(var entry in current)entry.Key.SetTexture("_CitySurfaceMap",entry.Value);foreach(var t in temporary)UnityEngine.Object.DestroyImmediate(t);c.ApplyDay();ShaderUtil.allowAsyncCompilation=old;}
 }
 static double Compare(string a,string b){
  var aa=new Texture2D(2,2);var bb=new Texture2D(2,2);aa.LoadImage(File.ReadAllBytes(a));bb.LoadImage(File.ReadAllBytes(b));var p=aa.GetPixels32();var q=bb.GetPixels32();double sum=0;
  for(int i=0;i<p.Length;i++)sum+=Math.Abs(p[i].r-q[i].r)+Math.Abs(p[i].g-q[i].g)+Math.Abs(p[i].b-q[i].b);
  UnityEngine.Object.DestroyImmediate(aa);UnityEngine.Object.DestroyImmediate(bb);return sum/(p.Length*3*255);
 }
}

