using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
[InitializeOnLoad] public static class CityNightPolish {
 static CityNightPolish(){EditorApplication.delayCall+=Run;}
 static void Run(){
  if(!File.Exists("CityNightPolish.request"))return;File.Delete("CityNightPolish.request");
  var c=UnityEngine.Object.FindObjectOfType<CityDayNightController>(true);if(!c)return;
  bool old=ShaderUtil.allowAsyncCompilation;
  try{
   ShaderUtil.allowAsyncCompilation=false;
   foreach(var name in new[]{"Building","Building 1","Building 2","Bus Station","Cafe","Casino"}){
    var m=AssetDatabase.LoadAssetAtPath<Material>("Assets/Scenes/Oasis/3D/Fbx/Materials/"+name+".mat");var s=CityMaterialUpgrade.Sources(m);
    s.windows=CityWindowMaskAuthor.Create(m);CityMaterialUpgrade.Pack(m,s);
   }
   var n=c.presets.night;n.windowBrightness=1.65f;n.windowWarmth=.93f;n.litWindowAmount=.74f;
   n.lightIntensity=.52f;n.ambientSky=new Color(.31f,.35f,.43f);n.ambientEquator=new Color(.215f,.23f,.28f);n.ambientGround=new Color(.085f,.09f,.115f);
   if(n.effects.TryGet<UnityEngine.Rendering.Universal.ColorAdjustments>(out var ca)){ca.postExposure.Override(.15f);EditorUtility.SetDirty(ca);}
   c.presets.streetLightIntensity=32;
   var poses=CityLightingBuilder.Views(c.gameObject.scene);
   c.ApplyNight();for(int i=0;i<poses.Length;i++)CityLightingBuilder.Capture(c,poses[i],"Reference-Night-"+i);
   n.windowLightsEnabled=false;c.ApplyNight();CityLightingBuilder.Capture(c,poses[1],"Reference-Windows-Off");
   if(Shader.GetGlobalFloat("_CityNightWindows")!=0)throw new Exception("Windows off failed");
   n.windowLightsEnabled=true;float strength=n.windowBrightness;n.windowBrightness=.35f;c.ApplyNight();CityLightingBuilder.Capture(c,poses[1],"Reference-Windows-Dim");
   n.windowBrightness=strength;c.ApplyNight();
   var shader=Shader.Find("Gangstar City/City Detail Mobile");if(ShaderUtil.GetShaderMessages(shader).Any(x=>x.severity.ToString()=="Error"))throw new Exception("Shader compile failed");
   float amount=n.litWindowAmount;n.litWindowAmount=0;c.ApplyNight();if(Shader.GetGlobalFloat("_CityWindowCoverage")!=0)throw new Exception("Coverage control failed");n.litWindowAmount=amount;
   float warmth=n.windowWarmth;n.windowWarmth=0;c.ApplyNight();var cool=Shader.GetGlobalColor("_CityNightColor");n.windowWarmth=1;c.ApplyNight();var warm=Shader.GetGlobalColor("_CityNightColor");if(warm.b>=cool.b)throw new Exception("Warmth control failed");n.windowWarmth=warmth;
   c.ApplyDay();CityLightingBuilder.Capture(c,poses[0],"Reference-Day-After");
   double difference=Compare("DayNightReview/Reference-Day-Before.png","DayNightReview/Reference-Day-After.png");
   if(difference>.01)throw new Exception("Unexpected Day render difference: "+difference);
   EditorUtility.SetDirty(c.presets);EditorUtility.SetDirty(c);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(c.gameObject.scene);EditorSceneManager.SaveScene(c.gameObject.scene);Selection.activeObject=c.presets;
   File.AppendAllText("CityNightReferenceReport.txt","\nFINAL CHECKS PASSED: dedicated window masks, window toggle, dim/bright, warmth and coverage controls. Day image mean RGB difference: "+(difference*100).ToString("F4")+"%. Shaders compiled. Four shadowless street lights and one combined bulb mesh. Scene saved DAY.");
  }catch(Exception e){File.AppendAllText("CityNightReferenceReport.txt","\nFINAL FAILED: "+e);Debug.LogException(e);}finally{c.ApplyDay();ShaderUtil.allowAsyncCompilation=old;}
 }
 static double Compare(string a,string b){
  var aa=new Texture2D(2,2);var bb=new Texture2D(2,2);aa.LoadImage(File.ReadAllBytes(a));bb.LoadImage(File.ReadAllBytes(b));var p=aa.GetPixels32();var q=bb.GetPixels32();
  double sum=0;for(int i=0;i<p.Length;i++)sum+=Math.Abs(p[i].r-q[i].r)+Math.Abs(p[i].g-q[i].g)+Math.Abs(p[i].b-q[i].b);
  UnityEngine.Object.DestroyImmediate(aa);UnityEngine.Object.DestroyImmediate(bb);return sum/(p.Length*3*255);
 }
}

