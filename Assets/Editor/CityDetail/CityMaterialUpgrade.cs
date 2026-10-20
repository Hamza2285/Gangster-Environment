using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class CityMaterialUpgrade {
 const string Folder="Assets/Scenes/Oasis/CityDetail";
 const string Materials="Assets/Scenes/Oasis/3D/Fbx/Materials/";
 static readonly string[] Names={"258","Building","Building 1","Building 2","Bus Station","Cafe","Casino","Grass"};
 static CityMaterialUpgrade(){EditorApplication.delayCall+=Run;}
 public static CitySurfaceSources Sources(Material m){
  string id=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m));
  return AssetDatabase.LoadAssetAtPath<CitySurfaceSources>("Assets/Editor/CityDetailSources/"+id+".asset");
 }
 public static void Keywords(Material m){
  SetKeyword(m,"_NORMALMAP",m.GetTexture("_BumpMap"));
  SetKeyword(m,"_CITY_SURFACE_MAP",m.GetTexture("_CitySurfaceMap"));
  SetKeyword(m,"_CITY_NIGHT_MAP",m.GetTexture("_CityNightMap"));
  SetKeyword(m,"_EMISSION",m.GetFloat("_CityEmissionStrength")>0);
  bool clip=m.GetFloat("_AlphaClip")>0.5f;
  SetKeyword(m,"_ALPHATEST_ON",clip);m.renderQueue=clip?2450:2000;
  SetKeyword(m,"_ENVIRONMENTREFLECTIONS_OFF",m.GetFloat("_EnvironmentReflections")==0);
  m.SetOverrideTag("RenderType",clip?"TransparentCutout":"Opaque");
 }
 static void SetKeyword(Material m,string key,bool enabled){if(enabled)m.EnableKeyword(key);else m.DisableKeyword(key);}
 static void Run(){
  if(!File.Exists("CityDetailBuild.request"))return;
  File.Move("CityDetailBuild.request","CityDetailBuild.started-"+DateTime.UtcNow.Ticks);
  var log=new StringBuilder();var snapshots=new Dictionary<Material,Material>();bool complete=false;
  bool oldAsync=ShaderUtil.allowAsyncCompilation;
  try {
   ShaderUtil.allowAsyncCompilation=false;
   Directory.CreateDirectory("CityDetailReview");Directory.CreateDirectory("CityDetailBackup");
   Directory.CreateDirectory("Assets/Editor/CityDetailSources");Directory.CreateDirectory(Folder+"/Packed");
   var materials=Names.Select(n=>AssetDatabase.LoadAssetAtPath<Material>(Materials+n+".mat")).ToArray();
   if(materials.Any(m=>!m))throw new Exception("An expected material is missing.");
   var ids=materials.Select(m=>AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m))).ToArray();
   int materialCount=AssetDatabase.FindAssets("t:Material").Length;
   var scene=SceneManager.GetSceneByPath("Assets/Scenes/Oasis/Gangstar City.unity");
   var renderers=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>(true)).ToArray();
   var poses=new List<Tuple<Vector3,Quaternion>>();
   foreach(var m in materials){
    string path=AssetDatabase.GetAssetPath(m);
    if(!File.Exists("CityDetailBackup/"+m.name+".mat")){File.Copy(path,"CityDetailBackup/"+m.name+".mat");File.Copy(path+".meta","CityDetailBackup/"+m.name+".mat.meta");}
    snapshots.Add(m,new Material(m));
    log.AppendLine(m.name+": "+m.shader.name+"; users="+renderers.Count(r=>r.sharedMaterials.Contains(m)));
    if(m.name=="258"||m.name=="Grass")continue;
    var candidates=renderers.Where(r=>r.enabled&&r.gameObject.activeInHierarchy&&r.sharedMaterials.Contains(m)&&r.bounds.size.y>8&&r.bounds.size.magnitude<350).OrderByDescending(r=>r.bounds.size.y).ToArray();
    if(candidates.Length==0)candidates=renderers.Where(r=>r.enabled&&r.gameObject.activeInHierarchy&&r.sharedMaterials.Contains(m)).OrderByDescending(r=>r.bounds.size.magnitude).ToArray();
    if(candidates.Length==0){log.AppendLine("No active renderer to capture: "+m.name);continue;}
    var b=candidates[0].bounds;var pos=b.center+new Vector3(0.8f,0.15f,-1).normalized*Mathf.Max(30,b.size.magnitude*1.1f);
    poses.Add(Tuple.Create(pos,Quaternion.LookRotation(b.center-pos,Vector3.up)));
   }
   for(int i=0;i<poses.Count;i++)Capture(poses[i],"Before-"+i);
   var shader=AssetDatabase.LoadAssetAtPath<Shader>(Folder+"/Shaders/City Detail Mobile.shader");
   if(!shader||!shader.isSupported)throw new Exception("City shader is unsupported.");
   foreach(var m in materials){
    var old=snapshots[m];bool legacy=old.shader.name.Contains("Autodesk");
    var color=(Texture2D)old.GetTexture(legacy?"_MainTex":"_BaseMap");
    var normal=old.GetTexture("_BumpMap");
    var source=ScriptableObject.CreateInstance<CitySurfaceSources>();
    if(legacy){source.metallic=old.GetTexture("_MetallicGlossMap") as Texture2D;source.roughness=old.GetTexture("_SpecGlossMap") as Texture2D;}
    string sourcePath="Assets/Editor/CityDetailSources/"+AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m))+".asset";
    AssetDatabase.CreateAsset(source,sourcePath);
    m.shader=shader;m.shaderKeywords=new string[0];
    m.SetTexture("_BaseMap",color);m.SetTexture("_BumpMap",normal);
    m.SetColor("_BaseColor",old.GetColor(legacy?"_Color":"_BaseColor"));
    var scale=legacy?(Vector2)old.GetVector("_UvTiling"):old.GetTextureScale("_BaseMap");
    var offset=legacy?(Vector2)old.GetVector("_UvOffset"):old.GetTextureOffset("_BaseMap");
    m.SetTextureScale("_BaseMap",scale);m.SetTextureOffset("_BaseMap",offset);
    m.SetFloat("_AlphaClip",legacy?0:old.GetFloat("_AlphaClip"));
    m.SetFloat("_Cull",old.HasProperty("_Cull")?old.GetFloat("_Cull"):2);m.SetFloat("_Cutoff",old.HasProperty("_Cutoff")?old.GetFloat("_Cutoff"):0.5f);
    m.SetFloat("_Surface",0);m.SetFloat("_ZWrite",1);m.SetFloat("_SrcBlend",1);m.SetFloat("_DstBlend",0);
    m.SetFloat("_WorkflowMode",1);m.SetFloat("_Metallic",0);
    m.SetFloat("_CityMetallic",0.8f);m.SetFloat("_Smoothness",legacy?0.9f:0.18f);
    m.SetFloat("_CitySmoothnessLimit",0.72f);m.SetFloat("_BumpScale",normal?1.1f:1);
    m.SetFloat("_CityContrast",m.name=="Grass"?1:1.06f);
    m.SetColor("_EmissionColor",old.GetColor("_EmissionColor"));
    m.SetFloat("_CityEmissionStrength",m.name=="Grass"?0:0.02f);
    m.SetFloat("_EnvironmentReflections",m.name=="Grass"?0:1);
    foreach(var p in new[]{"_MainTex","_EmissionMap","_MetallicGlossMap","_SpecGlossMap","_OcclusionMap","_ParallaxMap","_DetailAlbedoMap","_DetailNormalMap","_DetailMask"})
     if(m.HasProperty(p))m.SetTexture(p,null);
    Pack(m,source);Keywords(m);m.enableInstancing=true;EditorUtility.SetDirty(m);
    if(color)ConfigureTexture(AssetDatabase.GetAssetPath(color));
    if(normal)ConfigureTexture(AssetDatabase.GetAssetPath(normal));
    log.AppendLine(" -> "+m.name+": color "+(color?color.width+"x"+color.height:"none")+", normal="+(bool)normal+", surface maps="+(source.metallic||source.roughness)+", emission strength="+m.GetFloat("_CityEmissionStrength"));
   }
   AssetDatabase.SaveAssets();
   for(int i=0;i<poses.Count;i++)Capture(poses[i],"After-"+i);
   var errors=ShaderUtil.GetShaderMessages(shader).Where(m=>m.severity.ToString()=="Error").ToArray();
   if(errors.Length>0)throw new Exception(string.Join("\n",errors.Select(m=>m.message)));
   if(materialCount!=AssetDatabase.FindAssets("t:Material").Length)throw new Exception("Material count changed.");
   for(int i=0;i<materials.Length;i++)if(ids[i]!=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(materials[i])))throw new Exception("Material identity changed.");
   log.AppendLine("SUCCESS: 8 existing material assets updated in place. Material count and GUIDs unchanged.");
   log.AppendLine("Buildings: base color + normal + one packed metallic/occlusion/smoothness sample. Albedo reused for emission.");
   log.AppendLine("No normal-map work for materials without a normal texture. Cutout and culling settings preserved.");
   log.AppendLine("Android ASTC 4x4 configured; shader compiled and rendered in the Windows editor. Device FPS not measured.");
   complete=true;Selection.activeObject=materials[1];
  }catch(Exception e){
   if(!complete)foreach(var pair in snapshots)EditorUtility.CopySerialized(pair.Value,pair.Key);
   AssetDatabase.SaveAssets();log.AppendLine("FAILED: "+e);Debug.LogException(e);
  }finally{
   foreach(var copy in snapshots.Values)UnityEngine.Object.DestroyImmediate(copy);
   ShaderUtil.allowAsyncCompilation=oldAsync;File.WriteAllText("CityDetailReport.txt",log.ToString());
  }
 }
 public static void Pack(Material material,CitySurfaceSources sources){
  string path=Folder+"/Packed/"+material.name+" Surface.png";
  if(!sources.metallic&&!sources.roughness&&!sources.occlusion&&!sources.windows){material.SetTexture("_CitySurfaceMap",null);Keywords(material);return;}
  var metal=ReadRaw(sources.metallic);var rough=ReadRaw(sources.roughness);var ao=ReadRaw(sources.occlusion);var windows=ReadRaw(sources.windows);
  const int size=1024;
  var packed=new Texture2D(size,size,TextureFormat.RGBA32,false,true);
  try{
   var pixels=new Color32[size*size];
   for(int y=0;y<size;y++)for(int x=0;x<size;x++){
    float u=(x+0.5f)/size,v=(y+0.5f)/size;
    float mm=metal?metal.GetPixelBilinear(u,v).r:0;
    float rr=rough?rough.GetPixelBilinear(u,v).r:0.65f;
    float oo=ao?ao.GetPixelBilinear(u,v).r:1;
    pixels[y*size+x]=new Color(mm,oo,windows?windows.GetPixelBilinear(u,v).r:0,1-rr);
   }
   packed.SetPixels32(pixels);packed.Apply(false);File.WriteAllBytes(path,packed.EncodeToPNG());
  }finally{UnityEngine.Object.DestroyImmediate(packed);if(metal)UnityEngine.Object.DestroyImmediate(metal);if(rough)UnityEngine.Object.DestroyImmediate(rough);if(ao)UnityEngine.Object.DestroyImmediate(ao);if(windows)UnityEngine.Object.DestroyImmediate(windows);}
  AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
  var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.sRGBTexture=false;importer.alphaSource=TextureImporterAlphaSource.FromInput;importer.maxTextureSize=size;
  importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.mipmapEnabled=true;importer.isReadable=false;
  importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings{name="Android",overridden=true,maxTextureSize=size,format=TextureImporterFormat.ASTC_4x4,compressionQuality=100});importer.SaveAndReimport();
  material.SetTexture("_CitySurfaceMap",AssetDatabase.LoadAssetAtPath<Texture2D>(path));Keywords(material);
  EditorUtility.SetDirty(material);EditorUtility.SetDirty(sources);AssetDatabase.SaveAssets();
 }
 static Texture2D ReadRaw(Texture2D source){
  if(!source)return null;
  var image=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
  if(!image.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(source)),false)){UnityEngine.Object.DestroyImmediate(image);throw new Exception("Surface maps must be PNG or JPEG textures.");}
  return image;
 }
 static void ConfigureTexture(string path){
  var importer=AssetImporter.GetAtPath(path) as TextureImporter;if(!importer)return;
  importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=4;importer.mipmapEnabled=true;
  var android=importer.GetPlatformTextureSettings("Android");android.overridden=true;android.format=TextureImporterFormat.ASTC_4x4;var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);android.maxTextureSize=texture?Mathf.Min(4096,Mathf.Max(texture.width,texture.height)):2048;android.compressionQuality=100;
  importer.SetPlatformTextureSettings(android);importer.SaveAndReimport();
 }
 static void Capture(Tuple<Vector3,Quaternion> pose,string name){
  var method=typeof(GangstarRoadAtlasBuilder).GetMethod("Capture",BindingFlags.NonPublic|BindingFlags.Static);
  var warm=(Texture2D)method.Invoke(null,new object[]{pose,"CityDetailReview/Warmup.png"});UnityEngine.Object.DestroyImmediate(warm);
  var image=(Texture2D)method.Invoke(null,new object[]{pose,"CityDetailReview/"+name+".png"});UnityEngine.Object.DestroyImmediate(image);
 }
}


