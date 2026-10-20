using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class CityMixedNightBuild{
 const int N=1024;
 static Color32[] pixels;static Color32[] mask;
 [InitializeOnLoadMethod]static void Init(){EditorApplication.delayCall+=Run;}
 static Texture2D Read(string path){var t=new Texture2D(2,2,TextureFormat.RGBA32,false,true);t.LoadImage(File.ReadAllBytes(path));return t;}
 static void Glass(float x,float y,float w,float h,int cols,int rows){
  for(int row=0;row<rows;row++)for(int col=0;col<cols;col++){
   float cw=w/cols,ch=h/rows;int left=Mathf.CeilToInt((x+col*cw+3)/1600*N),right=Mathf.FloorToInt((x+(col+1)*cw-3)/1600*N);
   int top=Mathf.CeilToInt((y+row*ch+3)/1600*N),bottom=Mathf.FloorToInt((y+(row+1)*ch-3)/1600*N);
   byte rank=(byte)(205+(row*13+col*7)%45);
   for(int yy=top;yy<bottom;yy++)for(int xx=left;xx<right;xx++)mask[(N-1-yy)*N+xx]=new Color32(rank,rank,rank,255);
  }
 }
 static void Accent(float x,float y,float w,float h,bool pink){
  int l=Mathf.Max(0,Mathf.FloorToInt((x-3)/1600*N)),r=Mathf.Min(N,Mathf.CeilToInt((x+w+3)/1600*N));
  int t=Mathf.Max(0,Mathf.FloorToInt((y-3)/1600*N)),b=Mathf.Min(N,Mathf.CeilToInt((y+h+3)/1600*N));
  for(int yy=t;yy<b;yy++)for(int xx=l;xx<r;xx++){
   float px=(xx+.5f)/N*1600,py=(yy+.5f)/N*1600;
   float dx=Mathf.Max(x-px,0,px-(x+w)),dy=Mathf.Max(y-py,0,py-(y+h));
   float glow=Mathf.Max(dx,dy);float a=glow<=0?1:Mathf.Max(0,.14f*(1-glow/3));
   int i=(N-1-yy)*N+xx;if(mask[i].r>0)continue;
   pixels[i]=new Color32(0,0,(byte)(pink?255:85),(byte)Mathf.RoundToInt(a*255));
  }
 }
 static Texture2D Save(string path,Color32[] data,bool android){
  var t=new Texture2D(N,N,TextureFormat.RGBA32,false,true);t.SetPixels32(data);t.Apply();File.WriteAllBytes(path,t.EncodeToPNG());UnityEngine.Object.DestroyImmediate(t);
  AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
  var imp=(TextureImporter)AssetImporter.GetAtPath(path);imp.sRGBTexture=false;imp.mipmapEnabled=android;imp.maxTextureSize=N;
  imp.alphaSource=TextureImporterAlphaSource.FromInput;imp.textureCompression=android?TextureImporterCompression.CompressedHQ:TextureImporterCompression.Uncompressed;
  if(android)imp.SetPlatformTextureSettings(new TextureImporterPlatformSettings{name="Android",overridden=true,maxTextureSize=N,format=TextureImporterFormat.ASTC_4x4});
  imp.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
 }
 static void Run(){
  if(!File.Exists("CityMixedNight.request"))return;File.Delete("CityMixedNight.request");
  var c=UnityEngine.Object.FindObjectOfType<CityDayNightController>(true);bool wasNight=c.IsNight;bool async=ShaderUtil.allowAsyncCompilation;
  Directory.CreateDirectory("MixedNightReview");Directory.CreateDirectory("MixedNightBackup");Directory.CreateDirectory("Assets/Scenes/Oasis/CityDetail/Night");
  try{
   ShaderUtil.allowAsyncCompilation=false;
   int materials=AssetDatabase.FindAssets("t:Material").Length;int lights=UnityEngine.Object.FindObjectsOfType<Light>(true).Length;
   if(!File.Exists("Assets/Editor/DayNightBackups/BeforeMixedNight.unity"))EditorSceneManager.SaveScene(c.gameObject.scene,"Assets/Editor/DayNightBackups/BeforeMixedNight.unity",true);
   string preset=AssetDatabase.GetAssetPath(c.presets);if(!File.Exists("MixedNightBackup/Presets.asset"))File.Copy(preset,"MixedNightBackup/Presets.asset");
   foreach(var name in new[]{"Building","Building 1","Building 2","Cafe","Casino"}){
    var m=AssetDatabase.LoadAssetAtPath<Material>("Assets/Scenes/Oasis/3D/Fbx/Materials/"+name+".mat");var src=CityMaterialUpgrade.Sources(m);
    string mp=AssetDatabase.GetAssetPath(m),wp=AssetDatabase.GetAssetPath(src.windows);
    if(!File.Exists("MixedNightBackup/"+name+".mat"))File.Copy(mp,"MixedNightBackup/"+name+".mat");
    if(!File.Exists("MixedNightBackup/"+name+" Windows.png"))File.Copy(wp,"MixedNightBackup/"+name+" Windows.png");
    var raw=Read(wp);mask=raw.GetPixels32();UnityEngine.Object.DestroyImmediate(raw);
    if(name=="Building"){
     // The Coffee Shop wraps this glass atlas region around its curved wall.
     Glass(986,482,191,315,4,8);Glass(1062,404,63,72,2,2);
     src.windows=Save(wp,mask,false);CityMaterialUpgrade.Pack(m,src);
    }
    pixels=new Color32[N*N];
    for(int i=0;i<pixels.Length;i++){
     int rank=mask[i].r;int bucket=(rank*73+19)%100;
     pixels[i]=new Color32((byte)(bucket>=58&&bucket<88?255:0),(byte)(bucket>=88?255:0),0,(byte)(rank>0?255:0));
     // Coffee Shop glass: warm and white, with no saturated blue interior.
     int x=i%N,y=N-1-i/N;float px=(x+.5f)/N*1600,py=(y+.5f)/N*1600;
     if(name=="Building"&&px>=980&&px<=1180&&py>=400&&py<=800)pixels[i]=new Color32((byte)(bucket>82?255:0),0,0,(byte)(rank>0?95+(rank%25):0));
    }
    if(name=="Building"){
     Accent(400,1311,474,3,false);Accent(876,1311,3,163,false);
     Accent(1000,1202,3,280,true);
    }
    if(name=="Building 1"){
     Accent(302,4,114,3,true);Accent(414,7,3,202,false);
     Accent(214,63,82,2,false);Accent(298,116,118,2,true);
    }
    if(name=="Building 2"){
     Accent(799,860,246,3,false);Accent(1070,806,3,298,true);
     Accent(1098,806,368,3,true);Accent(1341,856,3,216,false);
     Accent(805,1146,3,444,false);Accent(808,1145,175,3,true);
    }
    m.SetTexture("_CityNightMap",Save("Assets/Scenes/Oasis/CityDetail/Night/"+name+" Night.png",pixels,true));
    CityMaterialUpgrade.Keywords(m);EditorUtility.SetDirty(m);
   }
   c.presets.night.windowColorMix=1;c.presets.night.accentBrightness=1.6f;
   EditorUtility.SetDirty(c.presets);AssetDatabase.SaveAssets();
   c.ApplyNight();
   var poses=CityLightingBuilder.Views(c.gameObject.scene);
   for(int i=0;i<poses.Length;i++)CityLightingBuilder.Capture(c,poses[i],"Mixed-Night-"+i);
   var rr=GameObject.Find("Circus_Circus_0").GetComponentInChildren<Renderer>();var bb=rr.bounds;
   var pos=bb.center+new Vector3(.4f,.08f,-1).normalized*bb.size.magnitude*.78f;
   CityLightingBuilder.Capture(c,Tuple.Create(pos,Quaternion.LookRotation(bb.center-pos)),"Mixed-Restaurant");
   var view=SceneView.lastActiveSceneView;
   CityLightingBuilder.Capture(c,Tuple.Create(new Vector3(-84.78f,231.55f,418.32f),Quaternion.Euler(10.93f,179.85f,0)),"Mixed-Skyline");
   c.ApplyDay();CityLightingBuilder.Capture(c,poses[0],"Mixed-Day");
   var shader=Shader.Find("Gangstar City/City Detail Mobile");
   var errors=ShaderUtil.GetShaderMessages(shader).Where(e=>e.severity.ToString()=="Error").ToArray();
   if(errors.Length>0)throw new Exception(string.Join("\n",errors.Select(e=>e.message)));
   if(AssetDatabase.FindAssets("t:Material").Length!=materials)throw new Exception("Material count changed");
   if(UnityEngine.Object.FindObjectsOfType<Light>(true).Length!=lights||c.streetLights.Length!=4)throw new Exception("Light budget changed");
   File.WriteAllText("MixedNightReview/Report.txt","SUCCESS: shader compiled; mixed windows, skyline and restaurant rendered. Material count unchanged; light count unchanged; streetlight pool remains 4. One additional 1024 ASTC4x4 lighting sample on five materials; no runtime animation, added geometry or lights. Day gates all night emission to zero. Android device performance not measured.");
  }catch(Exception e){File.WriteAllText("MixedNightReview/Report.txt","FAILED "+e);Debug.LogException(e);}
  finally{if(wasNight)c.ApplyNight();else c.ApplyDay();ShaderUtil.allowAsyncCompilation=async;EditorUtility.SetDirty(c);EditorSceneManager.MarkSceneDirty(c.gameObject.scene);EditorSceneManager.SaveScene(c.gameObject.scene);Selection.activeObject=c.presets;}
 }
}


