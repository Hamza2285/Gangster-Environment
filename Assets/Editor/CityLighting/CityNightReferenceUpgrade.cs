using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
[InitializeOnLoad] public static class CityNightReferenceUpgrade {
 static CityNightReferenceUpgrade(){EditorApplication.delayCall+=Run;}
 static void Run(){
  if(!File.Exists("CityNightReference.request"))return;
  File.Move("CityNightReference.request","CityNightReference.started-"+DateTime.UtcNow.Ticks);
  var log=new StringBuilder();var c=UnityEngine.Object.FindObjectOfType<CityDayNightController>(true);
  bool async=ShaderUtil.allowAsyncCompilation;
  try{
   if(!c)throw new Exception("Scene controller missing");
   ShaderUtil.allowAsyncCompilation=false;
   Directory.CreateDirectory("NightReferenceReview");
   Directory.CreateDirectory("Assets/Editor/DayNightBackups");
   EditorSceneManager.SaveScene(c.gameObject.scene,"Assets/Editor/DayNightBackups/BeforeNightReference-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".unity",true);
   var poses=CityLightingBuilder.Views(c.gameObject.scene);
   c.ApplyDay();CityLightingBuilder.Capture(c,poses[0],"Reference-Day-Before");
   var names=new[]{"Building","Building 1","Building 2","Bus Station","Cafe","Casino"};
   foreach(var name in names){
    var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/Scenes/Oasis/3D/Fbx/Materials/"+name+".mat");
    var sources=CityMaterialUpgrade.Sources(material);
    var path=AssetDatabase.GetAssetPath(material.GetTexture("_CitySurfaceMap"));
    File.Copy(path,"NightReferenceBackup/"+name+" Surface.png",true);
    sources.windows=CityWindowMaskAuthor.Create(material);CityMaterialUpgrade.Pack(material,sources);
    Overlay(material,sources.windows);
    log.AppendLine(name+": dedicated window mask packed into existing surface texture B channel.");
   }
   var n=c.presets.night;
   n.windowLightsEnabled=true;n.windowBrightness=3.2f;n.windowWarmth=.65f;n.litWindowAmount=.64f;n.windowColor=Color.white;n.sceneBrightness=1;
   n.lightColor=new Color(.86f,.89f,1);n.lightIntensity=.72f;
   n.ambientSky=new Color(.38f,.42f,.51f);n.ambientEquator=new Color(.27f,.285f,.33f);n.ambientGround=new Color(.12f,.125f,.145f);
   n.reflectionIntensity=.85f;n.roadEmission=.06f;
   n.skybox.SetColor("_Horizon",new Color(.14f,.18f,.25f));n.skybox.SetColor("_Zenith",new Color(.025f,.042f,.078f));EditorUtility.SetDirty(n.skybox);
   if(n.effects.TryGet<Bloom>(out var bloom)){bloom.intensity.Override(.3f);bloom.threshold.Override(1.15f);EditorUtility.SetDirty(bloom);}
   if(n.effects.TryGet<ColorAdjustments>(out var colors)){colors.postExposure.Override(.35f);colors.contrast.Override(6);colors.saturation.Override(-3);EditorUtility.SetDirty(colors);}
   if(n.effects.TryGet<OasisFogVolumeComponent>(out var fog)){fog.Density.Override(.001f);fog.Tint.Override(new Color(.11f,.14f,.19f));EditorUtility.SetDirty(fog);}
   c.presets.streetLightRange=27;c.presets.streetLightIntensity=24;c.presets.streetLightColor=new Color(1,.85f,.64f);
   BuildBulbs(c);EditorUtility.SetDirty(c.presets);EditorUtility.SetDirty(c);AssetDatabase.SaveAssets();
   c.ApplyDay();CityLightingBuilder.Capture(c,poses[0],"Reference-Day-After");
   c.ApplyNight();
   for(int i=0;i<poses.Length;i++)CityLightingBuilder.Capture(c,poses[i],"Reference-Night-"+i);
   n.windowLightsEnabled=false;c.ApplyNight();CityLightingBuilder.Capture(c,poses[1],"Reference-Windows-Off");
   if(Shader.GetGlobalFloat("_CityNightWindows")!=0)throw new Exception("Window off control failed");
   n.windowLightsEnabled=true;n.windowBrightness=1;c.ApplyNight();CityLightingBuilder.Capture(c,poses[1],"Reference-Windows-Dim");
   n.windowBrightness=3.2f;c.ApplyNight();
   if(Shader.GetGlobalFloat("_CityNightWindows")!=3.2f)throw new Exception("Window brightness control failed");
   foreach(var name in new[]{"Gangstar City/City Detail Mobile","Gangstar City/Mobile Night Sky","Gangstar City/Night Lamp Bulbs"}){
    var shader=Shader.Find(name);if(!shader)throw new Exception("Missing shader "+name);
    var errors=ShaderUtil.GetShaderMessages(shader).Where(x=>x.severity.ToString()=="Error").ToArray();if(errors.Length>0)throw new Exception(string.Join("\n",errors.Select(e=>e.message)));
   }
   c.ApplyDay();if(c.nightLampBulbs.enabled)throw new Exception("Lamp bulbs should be hidden during Day");
   var restored=CityLightingBuilder.CaptureDay(c.sun);var expected=c.presets.day;
   if(Quaternion.Angle(Quaternion.Euler(restored.lightRotation),Quaternion.Euler(expected.lightRotation))>.001f)throw new Exception("Day rotation mismatch");
   // Compare the actual original daylight fields, ignoring independent new UI controls.
   if(restored.lightColor!=expected.lightColor||restored.lightIntensity!=expected.lightIntensity*expected.sceneBrightness||restored.ambientSky!=expected.ambientSky*expected.sceneBrightness||restored.ambientEquator!=expected.ambientEquator*expected.sceneBrightness||restored.skybox!=expected.skybox||restored.fog!=expected.fog)throw new Exception("Day restoration failed");
   EditorUtility.SetDirty(c.presets);EditorUtility.SetDirty(c);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(c.gameObject.scene);EditorSceneManager.SaveScene(c.gameObject.scene);Selection.activeObject=c.presets;
   log.AppendLine("SUCCESS: window on/off and brightness checks passed; shaders compiled; Day restored. Night lights capped at four. Dedicated masks and bulb mesh add no per-window lights.");
  }catch(Exception e){log.AppendLine("FAILED: "+e);Debug.LogException(e);}
  finally{if(c)c.ApplyDay();ShaderUtil.allowAsyncCompilation=async;File.WriteAllText("CityNightReferenceReport.txt",log.ToString());}
 }
 static void Overlay(Material material,Texture2D mask){
  var color=new Texture2D(2,2);color.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap"))));
  var raw=new Texture2D(2,2);raw.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(mask)));
  var output=new Texture2D(1024,1024,TextureFormat.RGB24,false);
  for(int y=0;y<1024;y++)for(int x=0;x<1024;x++){float u=(x+.5f)/1024,v=(y+.5f)/1024;var c=color.GetPixelBilinear(u,v);float m=raw.GetPixelBilinear(u,v).r;output.SetPixel(x,y,m>.02f?Color.Lerp(c,new Color(1,.18f,0),.75f):c);}
  output.Apply();File.WriteAllBytes("NightReferenceReview/"+material.name+" Mask Overlay.png",output.EncodeToPNG());
  UnityEngine.Object.DestroyImmediate(color);UnityEngine.Object.DestroyImmediate(raw);UnityEngine.Object.DestroyImmediate(output);
 }
 static void BuildBulbs(CityDayNightController c){
  if(c.nightLampBulbs)return;
  var lamps=c.gameObject.scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshFilter>(true)).Where(f=>f.name.StartsWith("Street Light",StringComparison.OrdinalIgnoreCase)&&f.sharedMesh).ToArray();
  var positions=new List<Vector3>();
  foreach(var f in lamps){
   var r=f.GetComponent<Renderer>();if(!r||r.bounds.size.y<1)continue;
   // The far end of the lamp arm in the upper 20% of its bounds.
   Vector3 point=r.bounds.center;float best=-1;var vertices=f.sharedMesh.vertices;
   foreach(var vertex in vertices){
    var p=f.transform.TransformPoint(vertex);if(p.y<r.bounds.max.y-r.bounds.size.y*.2f)continue;
    var delta=p-f.transform.position;float distance=delta.x*delta.x+delta.z*delta.z;
    if(distance>best){best=distance;point=p;}
   }
   positions.Add(point+Vector3.down*.12f);
  }
  c.streetLightPositions=positions.Select(p=>p+Vector3.down*.3f).ToArray();
  var verticesOut=new List<Vector3>();var triangles=new List<int>();
  Vector3[] unit={Vector3.up,Vector3.down,Vector3.left,Vector3.right,Vector3.forward,Vector3.back};
  int[] faces={0,2,4,0,4,3,0,3,5,0,5,2,1,4,2,1,3,4,1,5,3,1,2,5};
  foreach(var pos in positions){int start=verticesOut.Count;foreach(var p in unit)verticesOut.Add(pos+Vector3.Scale(p,new Vector3(.22f,.075f,.22f)));foreach(int index in faces)triangles.Add(start+index);}
  var mesh=new Mesh{name="City Lamp Bulbs"};mesh.SetVertices(verticesOut);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();
  AssetDatabase.CreateAsset(mesh,"Assets/Scenes/Oasis/DayNight/City Lamp Bulbs.asset");
  var material=new Material(Shader.Find("Gangstar City/Night Lamp Bulbs")){name="Night Lamp Glow"};material.SetColor("_Color",new Color(8,6.3f,4.2f,1));AssetDatabase.CreateAsset(material,"Assets/Scenes/Oasis/DayNight/Night Lamp Glow.mat");
  var go=new GameObject("Night Lamp Bulbs");go.transform.SetParent(c.transform,false);
  // Mesh vertices were generated in world space.
  go.transform.position=Vector3.zero;go.transform.rotation=Quaternion.identity;go.transform.localScale=Vector3.one;
  go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;renderer.lightProbeUsage=LightProbeUsage.Off;renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;renderer.enabled=false;c.nightLampBulbs=renderer;
 }
}

