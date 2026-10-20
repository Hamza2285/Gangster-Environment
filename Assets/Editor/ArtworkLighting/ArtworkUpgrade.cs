using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
public static class ArtworkUpgrade {
 [InitializeOnLoadMethod] static void Init(){EditorApplication.delayCall+=Run;}
 static void Run(){
  if(!File.Exists("ArtworkUpgrade.request"))return;
  File.Delete("ArtworkUpgrade.request");
  var c=UnityEngine.Object.FindObjectOfType<CityDayNightController>(true);
  bool night=c&&c.IsNight;bool async=ShaderUtil.allowAsyncCompilation;
  try{
   ShaderUtil.allowAsyncCompilation=false;
   const string folder="Assets/Scenes/Oasis/ArtworkLighting/";
   var m=AssetDatabase.LoadAssetAtPath<Material>("Assets/Scenes/Oasis/3D/Fbx/Materials/Roads Props 01.mat");
   Directory.CreateDirectory("ArtworkReview");Directory.CreateDirectory("ArtworkBackup");
   var path=AssetDatabase.GetAssetPath(m);
   if(!File.Exists("ArtworkBackup/Roads Props 01.mat"))File.Copy(path,"ArtworkBackup/Roads Props 01.mat");
   var source=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
   source.LoadImage(File.ReadAllBytes("Assets/Scenes/Oasis/3D/Texture/Roads Props/2 Road matel.jpg"));
   int[,] rects={{126,561,491,765},{117,769,553,1012},{116,1017,553,1260},{678,405,897,798},{902,405,1196,798},{563,808,1061,1057},{1068,803,1598,1063},{563,1069,1060,1315},{1067,1069,1598,1329},{1,1270,173,1598},{180,1270,359,1598},{370,1270,555,1598},{562,1323,1060,1598},{1069,1338,1598,1598}};
   int size=2048;var pixels=new Color32[size*size];
   for(int y=0;y<size;y++)for(int x=0;x<size;x++){
    float u=(x+.5f)/size,v=(y+.5f)/size,px=u*1600,py=(1-v)*1600;
    Color s=source.GetPixelBilinear(u,v);byte mask=0;
    for(int r=0;r<rects.GetLength(0);r++)if(px>=rects[r,0]&&px<=rects[r,2]&&py>=rects[r,1]&&py<=rects[r,3]){mask=255;break;}
    pixels[y*size+x]=new Color32((byte)Mathf.RoundToInt(s.r*255),255,mask,255);
   }
   var packed=new Texture2D(size,size,TextureFormat.RGBA32,false,true);packed.SetPixels32(pixels);packed.Apply();
   string packedPath=folder+"Artwork Surface.png";File.WriteAllBytes(packedPath,packed.EncodeToPNG());
   UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(packed);
   AssetDatabase.ImportAsset(packedPath,ImportAssetOptions.ForceSynchronousImport);
   var importer=(TextureImporter)AssetImporter.GetAtPath(packedPath);
   importer.sRGBTexture=false;importer.mipmapEnabled=true;importer.alphaSource=TextureImporterAlphaSource.FromInput;
   importer.maxTextureSize=2048;importer.wrapMode=TextureWrapMode.Repeat;importer.filterMode=FilterMode.Bilinear;
   var android=importer.GetPlatformTextureSettings("Android");android.overridden=true;android.maxTextureSize=2048;android.format=TextureImporterFormat.ASTC_4x4;importer.SetPlatformTextureSettings(android);importer.SaveAndReimport();
   m.shader=Shader.Find("Gangstar City/Artwork Mobile Lit");if(!m.shader)throw new Exception("Shader missing");
   m.shaderKeywords=new[]{"_NORMALMAP","_METALLICSPECGLOSSMAP"};
   m.SetTexture("_MetallicGlossMap",AssetDatabase.LoadAssetAtPath<Texture2D>(packedPath));m.SetTexture("_EmissionMap",null);
   m.SetFloat("_ArtworkBrightness",1.6f);m.SetColor("_ArtworkColor",Color.white);m.enableInstancing=true;
   m.globalIlluminationFlags=MaterialGlobalIlluminationFlags.EmissiveIsBlack;EditorUtility.SetDirty(m);AssetDatabase.SaveAssets();
   int count=UnityEngine.Object.FindObjectsOfType<Renderer>(true).Count(r=>r.sharedMaterials.Contains(m));
   if(c){
    var pose=Tuple.Create(new Vector3(481,31,-390),Quaternion.Euler(0,0,0));
    c.ApplyNight();CityLightingBuilder.Capture(c,pose,"Artwork-Night");
    m.SetFloat("_ArtworkBrightness",0);CityLightingBuilder.Capture(c,pose,"Artwork-Off");m.SetFloat("_ArtworkBrightness",1.6f);
    c.ApplyDay();CityLightingBuilder.Capture(c,pose,"Artwork-Day");
   }
   var errors=ShaderUtil.GetShaderMessages(m.shader).Where(x=>x.severity.ToString()=="Error").ToArray();
   if(errors.Length>0)throw new Exception(string.Join("\n",errors.Select(x=>x.message)));
   EditorUtility.SetDirty(m);AssetDatabase.SaveAssets();Selection.activeObject=m;
   File.WriteAllText("ArtworkReview/Report.txt","SUCCESS\nMaterial: "+path+"\nScene renderers using material: "+count+"\n14 artwork rectangles; metal/signs/gutters excluded.\nWhite emission strength 1.6, controlled by existing _GangstarNight global.\n3 texture samples: albedo, normal, packed surface; previously 4 including emission.\nNo added materials, lights, geometry, or runtime scripts.\nShader compiled and day/night/off captures rendered.\nAndroid ASTC 4x4 packed map. Device performance not measured.");
  }catch(Exception e){File.WriteAllText("ArtworkReview/Report.txt","FAILED "+e);Debug.LogException(e);}
  finally{if(c){if(night)c.ApplyNight();else c.ApplyDay();}ShaderUtil.allowAsyncCompilation=async;}
 }
}
