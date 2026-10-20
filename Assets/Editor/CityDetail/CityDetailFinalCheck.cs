using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
[InitializeOnLoad]
public static class CityDetailFinalCheck {
 static CityDetailFinalCheck(){EditorApplication.delayCall+=Run;}
 static void Run(){
  if(!File.Exists("CityDetailFinal.request"))return;
  File.Move("CityDetailFinal.request","CityDetailFinal.started-"+DateTime.UtcNow.Ticks);
  var names=new[]{"258","Building","Building 1","Building 2","Bus Station","Cafe","Casino","Grass"};
  var materials=names.Select(n=>AssetDatabase.LoadAssetAtPath<Material>("Assets/Scenes/Oasis/3D/Fbx/Materials/"+n+".mat")).ToArray();
  bool asyncBefore=ShaderUtil.allowAsyncCompilation;int count=AssetDatabase.FindAssets("t:Material").Length;
  try{
   ShaderUtil.allowAsyncCompilation=false;
   foreach(var m in materials){
    var text=File.ReadAllText("CityDetailBackup/"+m.name+".mat");
    foreach(var p in new[]{"_Cull","_Cutoff"}){
     var match=Regex.Match(text,@"- "+p+@": ([0-9.]+)");
     if(!match.Success)throw new Exception("Backup property absent: "+p);
     m.SetFloat(p,float.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture));
    }
    var texture=m.GetTexture("_BaseMap") as Texture2D;
    if(texture){
     var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture));var android=importer.GetPlatformTextureSettings("Android");
     android.maxTextureSize=Mathf.Min(4096,Mathf.Max(texture.width,texture.height));importer.SetPlatformTextureSettings(android);importer.SaveAndReimport();
    }
    CityMaterialUpgrade.Keywords(m);EditorUtility.SetDirty(m);
   }
   AssetDatabase.SaveAssets();
   var scene=SceneManager.GetSceneByPath("Assets/Scenes/Oasis/Gangstar City.unity");
   var renderers=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>(true)).ToArray();
   int i=0;
   foreach(var m in materials.Skip(1).Take(6)){
    var candidates=renderers.Where(r=>r.enabled&&r.gameObject.activeInHierarchy&&r.sharedMaterials.Contains(m)&&r.bounds.size.y>8&&r.bounds.size.magnitude<350).OrderByDescending(r=>r.bounds.size.y).ToArray();
    if(candidates.Length==0)candidates=renderers.Where(r=>r.enabled&&r.gameObject.activeInHierarchy&&r.sharedMaterials.Contains(m)).OrderByDescending(r=>r.bounds.size.magnitude).ToArray();
    var bounds=candidates[0].bounds;var pos=bounds.center+new Vector3(0.8f,0.15f,-1).normalized*Mathf.Max(30,bounds.size.magnitude*1.1f);
    typeof(CityMaterialUpgrade).GetMethod("Capture",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{Tuple.Create(pos,Quaternion.LookRotation(bounds.center-pos,Vector3.up)),"After-"+i++});
   }
   var errors=ShaderUtil.GetShaderMessages(materials[1].shader).Where(x=>x.severity.ToString()=="Error").ToArray();
   if(errors.Length>0)throw new Exception(string.Join("\n",errors.Select(e=>e.message)));
   if(AssetDatabase.FindAssets("t:Material").Length!=count)throw new Exception("Material count changed.");
   File.AppendAllText("CityDetailReport.txt","\nFINAL CHECK PASSED: Original culling and cutoff restored from backups; Android color atlas resolution retained up to 4096. Six views rerendered. Material count unchanged.\n");
  }catch(Exception e){File.AppendAllText("CityDetailReport.txt","\nFINAL CHECK FAILED: "+e);Debug.LogException(e);}
  finally{ShaderUtil.allowAsyncCompilation=asyncBefore;}
 }
}

