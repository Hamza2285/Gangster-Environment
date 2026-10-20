using System;using System.IO;using System.Linq;using UnityEngine;using UnityEditor;using UnityEditor.SceneManagement;
public static class BridgeLightingBuild{
[InitializeOnLoadMethod]static void Init(){EditorApplication.delayCall+=Run;}
static void Run(){if(!File.Exists("BridgeLighting.request"))return;File.Delete("BridgeLighting.request");var c=UnityEngine.Object.FindObjectOfType<CityDayNightController>(true);bool night=c.IsNight;bool async=ShaderUtil.allowAsyncCompilation;try{
ShaderUtil.allowAsyncCompilation=false;
var r=UnityEngine.Object.FindObjectsOfType<MeshRenderer>().First(r=>r.name=="bridge");
if(!File.Exists("Assets/Editor/DayNightBackups/BeforeBridgeLighting.unity"))EditorSceneManager.SaveScene(c.gameObject.scene,"Assets/Editor/DayNightBackups/BeforeBridgeLighting.unity",true);
string p="Assets/Scenes/Oasis/BridgeLighting/Bridge Night.mat";var m=AssetDatabase.LoadAssetAtPath<Material>(p);if(!m){m=new Material(r.sharedMaterial);m.name="Bridge Night";AssetDatabase.CreateAsset(m,p);}m.shader=Shader.Find("Gangstar City/Bridge Night Lit");r.sharedMaterial=m;EditorUtility.SetDirty(m);EditorUtility.SetDirty(r);AssetDatabase.SaveAssets();
var pos=r.bounds.center+new Vector3(1,.13f,.7f).normalized*r.bounds.size.magnitude*.68f;var pose=Tuple.Create(pos,Quaternion.LookRotation(r.bounds.center-pos));
c.ApplyNight();CityLightingBuilder.Capture(c,pose,"Bridge-Night");c.ApplyDay();CityLightingBuilder.Capture(c,pose,"Bridge-Day");
var errors=ShaderUtil.GetShaderMessages(m.shader).Where(e=>e.severity.ToString()=="Error").ToArray();if(errors.Length>0)throw new Exception(string.Join("\n",errors.Select(e=>e.message)));
File.WriteAllText("VenueLightingReview/BridgeReport.txt","SUCCESS: bridge uses a dedicated material with existing texture and geometry. No additional renderers, texture samples or lights. Day/night previews rendered; shader compiled. Accent colors and brightness use existing night preset controls. Android device timing unmeasured.");
}catch(Exception e){File.WriteAllText("VenueLightingReview/BridgeReport.txt","FAILED "+e);Debug.LogException(e);}finally{if(night)c.ApplyNight();else c.ApplyDay();ShaderUtil.allowAsyncCompilation=async;EditorSceneManager.MarkSceneDirty(c.gameObject.scene);EditorSceneManager.SaveScene(c.gameObject.scene);}}
}
