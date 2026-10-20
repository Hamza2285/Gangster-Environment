using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
[InitializeOnLoad]public static class CityLightingReview{
 static CityLightingReview(){EditorApplication.delayCall+=Run;}
 static void Run(){
  if(!File.Exists("CityLightingReview.request"))return;File.Delete("CityLightingReview.request");
  var c=UnityEngine.Object.FindObjectOfType<CityDayNightController>(true);if(!c){File.WriteAllText("CityLightingReport.txt","Controller missing");return;}
  bool old=ShaderUtil.allowAsyncCompilation;try{
   ShaderUtil.allowAsyncCompilation=false;
   var n=c.presets.night;n.ambientSky=new Color(.22f,.29f,.42f);n.ambientEquator=new Color(.13f,.17f,.25f);n.ambientGround=new Color(.05f,.06f,.09f);n.lightIntensity=.42f;n.windowBrightness=1.25f;
   n.skybox.SetColor("_Horizon",new Color(.16f,.23f,.35f));n.skybox.SetColor("_Zenith",new Color(.025f,.055f,.12f));EditorUtility.SetDirty(n.skybox);
   c.ApplyDay();
   File.WriteAllText("DayNightReview/ExpectedDay.json",JsonUtility.ToJson(c.presets.day,true));
   File.WriteAllText("DayNightReview/ActualDay.json",JsonUtility.ToJson(CityLightingBuilder.CaptureDay(c.sun),true));
   var poses=CityLightingBuilder.Views(c.gameObject.scene);
   for(int i=0;i<poses.Length;i++)CityLightingBuilder.Capture(c,poses[i],"Day-"+i);
   c.ApplyNight();
   for(int i=0;i<poses.Length;i++)CityLightingBuilder.Capture(c,poses[i],"Night-"+i);
   c.ApplyDay();
   var restored=CityLightingBuilder.CaptureDay(c.sun);var expected=c.presets.day;
   if(Quaternion.Angle(Quaternion.Euler(restored.lightRotation),Quaternion.Euler(expected.lightRotation))>.001f)throw new Exception("Day rotation mismatch");
   restored.lightRotation=expected.lightRotation;
   if(JsonUtility.ToJson(restored)!=JsonUtility.ToJson(expected))throw new Exception("Day lighting did not restore");
   var buttons=c.controls.GetComponentsInChildren<UnityEngine.UI.Button>();
   if(buttons.Length!=2)throw new Exception("Expected two buttons");
   foreach(var button in buttons){
    if(button.onClick.GetPersistentEventCount()!=1||button.onClick.GetPersistentTarget(0)!=c)throw new Exception("Button is not connected");
    var state=button.onClick.GetPersistentListenerState(0);
    try{button.onClick.SetPersistentListenerState(0,UnityEngine.Events.UnityEventCallState.EditorAndRuntime);button.onClick.Invoke();if(c.IsNight!=(button.name=="Night"))throw new Exception(button.name+" button failed");}
    finally{button.onClick.SetPersistentListenerState(0,state);}
   }
   c.ApplyDay();
   if(c.streetLightPositions.Length==0||c.streetLights.Length!=4||c.streetLights.Any(l=>l.shadows!=LightShadows.None))throw new Exception("Streetlight budget/configuration invalid");
   if(c.streetLights.Any(l=>l.enabled))throw new Exception("Street lights should be off during Day");
   var cameraObject=new GameObject("Light Budget Check");var cam=cameraObject.AddComponent<Camera>();cam.enabled=false;cam.transform.position=c.streetLightPositions[0];
   try{c.ApplyNight();c.RefreshStreetLights(cam);if(c.streetLights.Count(l=>l.enabled)<1||c.streetLights.Count(l=>l.enabled)>4)throw new Exception("Night light pool selection failed");}
   finally{c.ApplyDay();UnityEngine.Object.DestroyImmediate(cameraObject);}
   foreach(var name in new[]{"Gangstar City/Mobile Night Sky","Gangstar City/City Detail Mobile","Gangstar City/Road Atlas Lit"}){
    var shader=Shader.Find(name);var errs=ShaderUtil.GetShaderMessages(shader).Where(m=>m.severity.ToString()=="Error").ToArray();if(errs.Length>0)throw new Exception(name+": "+string.Join("\n",errs.Select(m=>m.message)));
   }
   EditorUtility.SetDirty(c);EditorUtility.SetDirty(c.presets);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(c.gameObject.scene);EditorSceneManager.SaveScene(c.gameObject.scene);
   Selection.activeObject=c.presets;
   File.WriteAllText("CityLightingReport.txt","SUCCESS: Day/Night/Day lighting restoration passed (rotation tolerance 0.001 degree); both UI button callbacks passed; all three shaders compiled without errors. Scene saved DAY. Street lights: maximum 4, all shadowless, all disabled in Day; "+c.streetLightPositions.Length+" candidate positions. Windows reuse existing surface-map sample; no extra building material instances or texture lookups. Night uses static reflection cubemap. Android device performance not measured.");
  }catch(Exception e){File.WriteAllText("CityLightingReport.txt","FAILED "+e);Debug.LogException(e);}finally{c.ApplyDay();ShaderUtil.allowAsyncCompilation=old;}
 }
}
