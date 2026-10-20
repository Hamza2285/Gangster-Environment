using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEditor;
[InitializeOnLoad] public static class CityLightingAudit{
 static CityLightingAudit(){EditorApplication.delayCall+=Run;}
 static void Run(){if(!File.Exists("CityLightingAudit.request"))return;File.Delete("CityLightingAudit.request");var b=new StringBuilder();var s=SceneManager.GetSceneByPath("Assets/Scenes/Oasis/Gangstar City.unity");
 var all=s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
 b.AppendLine("Lightmaps="+LightmapSettings.lightmaps.Length+" probes="+(LightmapSettings.lightProbes?LightmapSettings.lightProbes.count:0));
 b.AppendLine("Sky="+AssetDatabase.GetAssetPath(RenderSettings.skybox)+" Reflection="+AssetDatabase.GetAssetPath(RenderSettings.customReflectionTexture)+" mode="+RenderSettings.defaultReflectionMode+" ambient="+RenderSettings.ambientMode);
 foreach(var l in all.Select(t=>t.GetComponent<Light>()).Where(x=>x))b.AppendLine("LIGHT "+l.name+" "+l.type+" enabled="+l.enabled+" active="+l.gameObject.activeInHierarchy+" intensity="+l.intensity+" at="+l.transform.position+" shadows="+l.shadows);
 foreach(var v in all.Select(t=>t.GetComponent<Volume>()).Where(x=>x))b.AppendLine("VOLUME "+v.name+" active="+v.gameObject.activeInHierarchy+" weight="+v.weight+" priority="+v.priority+" global="+v.isGlobal+" layer="+v.gameObject.layer+" profile="+AssetDatabase.GetAssetPath(v.sharedProfile));
 foreach(var c in all.Select(t=>t.GetComponent<Camera>()).Where(x=>x))b.AppendLine("CAM "+c.name+" active="+c.gameObject.activeInHierarchy+" "+c.transform.position+" rot="+c.transform.eulerAngles);
 foreach(var h in all.Select(t=>t.GetComponent<HDRFogOverride>()).Where(x=>x))b.AppendLine("HDRFOG "+h.name+" enabled="+h.enabled+" color="+h.FogColor);
 foreach(var m in all.Select(t=>t.GetComponent<SceneMetaData>()).Where(x=>x))b.AppendLine("META "+m.name+" root="+m.Root+" sky="+AssetDatabase.GetAssetPath(m.skybox)+" refl="+AssetDatabase.GetAssetPath(m.reflection));
 foreach(var t in all.Where(t=>System.Text.RegularExpressions.Regex.IsMatch(t.name,"lamp|street.?light|light.?pole|eventsystem",System.Text.RegularExpressions.RegexOptions.IgnoreCase)).Take(100))b.AppendLine("LAMP "+t.name+" at="+t.position);
 File.WriteAllText("CityLightingAudit.txt",b.ToString());}}

