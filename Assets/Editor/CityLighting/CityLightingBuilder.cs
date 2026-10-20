using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;

[InitializeOnLoad] public static class CityLightingBuilder {
 const string Folder="Assets/Scenes/Oasis/DayNight";
 static CityLightingBuilder(){EditorApplication.delayCall+=Run;}
 static void Run(){
  if(!File.Exists("CityLightingBuild.request"))return;
  File.Move("CityLightingBuild.request","CityLightingBuild.started-"+DateTime.UtcNow.Ticks);
  var report=new StringBuilder();CityDayNightController controller=null;bool async=ShaderUtil.allowAsyncCompilation;
  try{
   ShaderUtil.allowAsyncCompilation=false;
   var scene=SceneManager.GetSceneByPath("Assets/Scenes/Oasis/Gangstar City.unity");
   if(!scene.isLoaded)throw new Exception("Open the Gangstar City scene first.");
   Directory.CreateDirectory(Folder);Directory.CreateDirectory("DayNightReview");Directory.CreateDirectory("Assets/Editor/DayNightBackups");
   string backup="Assets/Editor/DayNightBackups/BeforeDayNight-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".unity";
   EditorSceneManager.SaveScene(scene,backup,true);
   var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
   if(all.Any(t=>t.GetComponent<CityDayNightController>()))throw new Exception("A day/night controller already exists; use the review helper.");
   var sun=all.Select(t=>t.GetComponent<Light>()).First(l=>l&&l.type==LightType.Directional);
   var meta=all.Select(t=>t.GetComponent<SceneMetaData>()).FirstOrDefault(m=>m);
   var preset=ScriptableObject.CreateInstance<CityDayNightPresets>();preset.name="City Day Night Presets";
   preset.day=CaptureDay(sun);
   string original=JsonUtility.ToJson(preset.day);
   preset.night=JsonUtility.FromJson<CityLightingLook>(original);
   AssetDatabase.CreateAsset(preset,Folder+"/City Day Night Presets.asset");
   var night=preset.night;
   var skyShader=Shader.Find("Gangstar City/Mobile Night Sky");if(!skyShader)throw new Exception("Night sky shader not imported.");
   var sky=new Material(skyShader){name="City Night Sky"};
   var rotation=Quaternion.Euler(38,-32,0);var moon=rotation*Vector3.back;
   sky.SetVector("_MoonDirection",moon);AssetDatabase.CreateAsset(sky,Folder+"/Night Sky.mat");
   night.skybox=sky;night.lightRotation=rotation.eulerAngles;night.lightColor=new Color(.63f,.76f,1);
   night.lightIntensity=.28f;night.indirectIntensity=1;night.useColorTemperature=false;night.shadows=LightShadows.Hard;night.shadowStrength=.72f;
   night.ambientMode=AmbientMode.Trilight;night.ambientSky=new Color(.11f,.16f,.26f);
   night.ambientEquator=new Color(.055f,.075f,.12f);night.ambientGround=new Color(.018f,.022f,.034f);night.ambientIntensity=1;
   night.nightAmount=1;night.windowBrightness=1.8f;night.windowColor=new Color(1,.74f,.42f);
   night.fog=false;night.fogColor=new Color(.065f,.1f,.18f);night.reflectionIntensity=.65f;
   night.reflection=CreateReflection();night.reflectionMode=DefaultReflectionMode.Custom;
   var profile=ScriptableObject.CreateInstance<VolumeProfile>();profile.name="Night Effects";
   AssetDatabase.CreateAsset(profile,Folder+"/Night Effects.asset");
   var bloom=profile.Add<Bloom>(true);bloom.intensity.Override(.22f);bloom.threshold.Override(1.1f);bloom.scatter.Override(.55f);bloom.highQualityFiltering.Override(false);bloom.maxIterations.Override(4);
   var colors=profile.Add<ColorAdjustments>(true);colors.postExposure.Override(0);colors.contrast.Override(8);colors.saturation.Override(-6);
   var fog=profile.Add<OasisFogVolumeComponent>(true);fog.Density.Override(.0015f);fog.StartDistance.Override(45);
   fog.HeightRange.Override(new Vector2(0,45));fog.Tint.Override(new Color(.06f,.105f,.20f));fog.SunScatteringIntensity.Override(0);
   foreach(var component in profile.components)AssetDatabase.AddObjectToAsset(component,profile);
   night.effects=profile;
   var go=new GameObject("City Day Night");SceneManager.MoveGameObjectToScene(go,scene);if(meta&&meta.Root)go.transform.SetParent(meta.Root.transform,false);
   controller=go.AddComponent<CityDayNightController>();controller.presets=preset;controller.sun=sun;controller.sceneMetadata=meta;controller.originalMetadataFog=meta&&meta.FogEnabled;
   controller.sunFlares=sun.GetComponents<Behaviour>().Where(b=>b.GetType().Name.Contains("LensFlare")).ToArray();
   controller.dayFlareEnabled=controller.sunFlares.Select(b=>b.enabled).ToArray();
   var fx=new GameObject("Night Effects");fx.transform.SetParent(go.transform,false);controller.nightEffects=fx.AddComponent<Volume>();controller.nightEffects.isGlobal=true;controller.nightEffects.priority=100;controller.nightEffects.weight=0;
   var lamps=all.Where(t=>t.name.StartsWith("Street Light",StringComparison.OrdinalIgnoreCase)&&t.GetComponent<MeshRenderer>()).ToArray();
   var positions=lamps.Select(t=>t.GetComponent<MeshRenderer>().bounds).Where(b=>b.size.y>1).Select(b=>new Vector3(b.center.x,b.max.y-.35f,b.center.z)).ToArray();
   controller.streetLightPositions=positions;
   controller.streetLights=new Light[4];
   for(int i=0;i<4;i++){var lg=new GameObject("Nearby Street Light "+(i+1));lg.transform.SetParent(go.transform,false);var l=lg.AddComponent<Light>();l.type=LightType.Point;l.shadows=LightShadows.None;l.renderMode=LightRenderMode.ForcePixel;l.enabled=false;controller.streetLights[i]=l;}
   MakeControls(controller);
   AssetDatabase.SaveAssets();
   controller.ApplyDay();
   var poses=Views(scene);
   for(int i=0;i<poses.Length;i++)Capture(controller,poses[i],"Day-"+i);
   controller.ApplyNight();
   for(int i=0;i<poses.Length;i++)Capture(controller,poses[i],"Night-"+i);
   if(controller.streetLights.Count(l=>l.enabled)>4)throw new Exception("Light budget exceeded.");
   controller.ApplyDay();
   var restored=CaptureDay(sun); if(Quaternion.Angle(Quaternion.Euler(restored.lightRotation),Quaternion.Euler(preset.day.lightRotation))>0.001f)throw new Exception("Day rotation mismatch."); restored.lightRotation=preset.day.lightRotation; if(JsonUtility.ToJson(restored)!=original)throw new Exception("Day settings mismatch.");
   var shaders=new[]{skyShader,Shader.Find("Gangstar City/City Detail Mobile"),Shader.Find("Gangstar City/Road Atlas Lit")};
   foreach(var sh in shaders){if(!sh||!sh.isSupported)throw new Exception("Shader unsupported: "+sh);var errors=ShaderUtil.GetShaderMessages(sh).Where(m=>m.severity.ToString()=="Error").ToArray();if(errors.Length>0)throw new Exception(string.Join("\n",errors.Select(e=>e.message)));}
   EditorUtility.SetDirty(preset);EditorUtility.SetDirty(controller);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   report.AppendLine("SUCCESS: Day / Night / Day roundtrip preserved captured lighting.");
   report.AppendLine("Scene saved in DAY. Preset: "+AssetDatabase.GetAssetPath(preset));
   report.AppendLine("Directional lights reused: 1. Street light pool: 4, shadowless; candidate positions: "+positions.Length);
   report.AppendLine("No baked lightmaps/probes: "+LightmapSettings.lightmaps.Length+"/"+(LightmapSettings.lightProbes?LightmapSettings.lightProbes.count:0));
   report.AppendLine("Night uses one static reflection cubemap; no realtime reflection capture. Windows reuse existing packed texture sample.");
   report.AppendLine("Scene backup: "+backup);
   Selection.activeObject=preset;
  }catch(Exception e){report.AppendLine("FAILED: "+e);Debug.LogException(e);if(controller)controller.ApplyDay();}
  finally{ShaderUtil.allowAsyncCompilation=async;File.WriteAllText("CityLightingReport.txt",report.ToString());}
 }
 public static CityLightingLook CaptureDay(Light sun){
  return new CityLightingLook{
   skybox=RenderSettings.skybox,reflectionMode=RenderSettings.defaultReflectionMode,reflection=RenderSettings.customReflectionTexture as Cubemap,
   reflectionIntensity=RenderSettings.reflectionIntensity,reflectionBounces=RenderSettings.reflectionBounces,
   ambientMode=RenderSettings.ambientMode,ambientSky=RenderSettings.ambientSkyColor,ambientEquator=RenderSettings.ambientEquatorColor,ambientGround=RenderSettings.ambientGroundColor,ambientIntensity=RenderSettings.ambientIntensity,
   fog=RenderSettings.fog,fogMode=RenderSettings.fogMode,fogColor=RenderSettings.fogColor,fogDensity=RenderSettings.fogDensity,fogStart=RenderSettings.fogStartDistance,fogEnd=RenderSettings.fogEndDistance,
   lightRotation=sun.transform.eulerAngles,lightColor=sun.color,lightIntensity=sun.intensity,indirectIntensity=sun.bounceIntensity,lightEnabled=sun.enabled,useColorTemperature=sun.useColorTemperature,colorTemperature=sun.colorTemperature,shadows=sun.shadows,shadowStrength=sun.shadowStrength};
 }
 static Cubemap CreateReflection(){
  const int n=64;var cube=new Cubemap(n,TextureFormat.RGBAHalf,true){name="Static Night Reflection",wrapMode=TextureWrapMode.Clamp};
  var horizon=new Color(.065f,.12f,.22f).linear;var zenith=new Color(.008f,.018f,.055f).linear;
  for(int f=0;f<6;f++){var pixels=new Color[n*n];
   for(int y=0;y<n;y++)for(int x=0;x<n;x++){
    float u=2*(x+.5f)/n-1,v=2*(y+.5f)/n-1;
    Vector3 d;
    switch(f){case 0:d=new Vector3(1,-v,-u);break;case 1:d=new Vector3(-1,-v,u);break;case 2:d=new Vector3(u,1,v);break;case 3:d=new Vector3(u,-1,-v);break;case 4:d=new Vector3(u,-v,1);break;default:d=new Vector3(-u,-v,-1);break;}
    d.Normalize();pixels[y*n+x]=Color.Lerp(horizon,zenith,Mathf.Clamp01(d.y*1.7f));
   }cube.SetPixels(pixels,(CubemapFace)f);
  }
  cube.Apply(true,true);AssetDatabase.CreateAsset(cube,Folder+"/Night Reflection.asset");return cube;
 }
 static void MakeControls(CityDayNightController c){
  var root=new GameObject("Day Night Buttons",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));root.transform.SetParent(c.transform,false);
  var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=100;
  var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);scaler.matchWidthOrHeight=.5f;
  c.controls=root;
  for(int i=0;i<2;i++){
   var go=new GameObject(i==0?"Day":"Night",typeof(RectTransform),typeof(Image),typeof(Button));go.transform.SetParent(root.transform,false);
   var rect=go.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=new Vector2(1,1);rect.pivot=new Vector2(1,1);rect.sizeDelta=new Vector2(116,44);rect.anchoredPosition=new Vector2(-20-i*124,-20);
   go.GetComponent<Image>().color=new Color(.045f,.065f,.11f,.9f);
   var label=new GameObject("Label",typeof(RectTransform),typeof(Text));label.transform.SetParent(go.transform,false);var tr=label.GetComponent<RectTransform>();tr.anchorMin=Vector2.zero;tr.anchorMax=Vector2.one;tr.offsetMin=tr.offsetMax=Vector2.zero;
   var text=label.GetComponent<Text>();text.text=i==0?"DAY":"NIGHT";text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");text.fontSize=20;text.alignment=TextAnchor.MiddleCenter;text.color=Color.white;text.raycastTarget=false;
   if(i==0)UnityEventTools.AddPersistentListener(go.GetComponent<Button>().onClick,c.ApplyDay);else UnityEventTools.AddPersistentListener(go.GetComponent<Button>().onClick,c.ApplyNight);
  }
 }
 public static Tuple<Vector3,Quaternion>[] Views(Scene scene){
  var m=AssetDatabase.LoadAssetAtPath<Material>("Assets/Scenes/Oasis/3D/Fbx/Materials/Building.mat");
  var r=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>(true)).Where(x=>x.enabled&&x.gameObject.activeInHierarchy&&x.sharedMaterials.Contains(m)&&x.bounds.size.y>8&&x.bounds.size.magnitude<350).OrderByDescending(x=>x.bounds.size.y).First();
  var b=r.bounds;var p=b.center+new Vector3(.8f,.15f,-1).normalized*Mathf.Max(30,b.size.magnitude*1.1f);
  var street=new Vector3(-60,31.5f,-110);var direction=Quaternion.LookRotation(new Vector3(0,.07f,1));
  return new[]{Tuple.Create(p,Quaternion.LookRotation(b.center-p)),Tuple.Create(street,direction)};
 }
 public static void Capture(CityDayNightController controller,Tuple<Vector3,Quaternion> pose,string name){
  var go=new GameObject("Lighting Preview Camera"){hideFlags=HideFlags.HideAndDontSave};var camera=go.AddComponent<Camera>();
  go.transform.SetPositionAndRotation(pose.Item1,pose.Item2);camera.enabled=false;camera.fieldOfView=60;camera.farClipPlane=3500;camera.nearClipPlane=.3f;camera.allowHDR=true;camera.clearFlags=CameraClearFlags.Skybox;
  var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;data.volumeLayerMask=1;data.volumeTrigger=go.transform;data.antialiasing=AntialiasingMode.FastApproximateAntialiasing;
  var rt=RenderTexture.GetTemporary(1280,720,24,RenderTextureFormat.ARGB32);var old=RenderTexture.active;
  try{
   camera.targetTexture=rt;controller.RefreshStreetLights(camera);
   for(int i=0;i<3;i++){VolumeManager.instance.Update(camera.transform,1);camera.Render();}
   RenderTexture.active=rt;var image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes("DayNightReview/"+name+".png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
  }finally{camera.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(go);}
 }
}

