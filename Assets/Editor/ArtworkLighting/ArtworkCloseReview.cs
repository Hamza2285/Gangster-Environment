using System;
using System.IO;
using UnityEditor;
using UnityEngine;
public static class ArtworkCloseReview {
 [InitializeOnLoadMethod]static void Init(){EditorApplication.delayCall+=Run;}
 static void Run(){
 if(!File.Exists("ArtworkClose.request"))return;File.Delete("ArtworkClose.request");
 var c=UnityEngine.Object.FindObjectOfType<CityDayNightController>(true);
 var m=AssetDatabase.LoadAssetAtPath<Material>("Assets/Scenes/Oasis/3D/Fbx/Materials/Roads Props 01.mat");
 bool night=c.IsNight;float strength=m.GetFloat("_ArtworkBrightness");
 try{
 c.ApplyNight();
 var target=new Vector3(-146.75f,36,-30.71f);
 var pos=target+new Vector3(17,0,5);
 var pose=Tuple.Create(pos,Quaternion.LookRotation(target-pos));
 CityLightingBuilder.Capture(c,pose,"Artwork-Close");
 m.SetFloat("_ArtworkBrightness",0);CityLightingBuilder.Capture(c,pose,"Artwork-Close-Off");
 }finally{m.SetFloat("_ArtworkBrightness",strength);if(night)c.ApplyNight();else c.ApplyDay();}
 }
}
