using System;using System.IO;using System.Linq;using UnityEngine;using UnityEditor;using UnityEditor.SceneManagement;
public static class VenueLightingBuild{
const int N=1024;
static Color32[] pixels;static Texture2D albedo;
static Texture2D Read(string p){var t=new Texture2D(2,2,TextureFormat.RGBA32,false,true);t.LoadImage(File.ReadAllBytes(p));return t;}
static void Fill(float x,float y,float w,float h,float strength,bool glassOnly=false){
int l=Mathf.Max(0,Mathf.CeilToInt(x/1600*N)),r=Mathf.Min(N,Mathf.FloorToInt((x+w)/1600*N));
int top=Mathf.Max(0,Mathf.CeilToInt(y/1600*N)),bottom=Mathf.Min(N,Mathf.FloorToInt((y+h)/1600*N));
for(int yy=top;yy<bottom;yy++)for(int xx=l;xx<r;xx++){
var color=albedo.GetPixelBilinear((xx+.5f)/N,1-(yy+.5f)/N);
if(glassOnly&&!(color.b>color.r*1.035f&&color.b>color.g*.98f))continue;
int i=(N-1-yy)*N+xx;
pixels[i]=new Color32(0,0,32,(byte)Mathf.RoundToInt(strength*255));
}
}
static void ColorFill(float x,float y,float w,float h,byte code){Fill(x,y,w,h,1);for(int yy=Mathf.CeilToInt(y/1600*N);yy<Mathf.FloorToInt((y+h)/1600*N);yy++)for(int xx=Mathf.CeilToInt(x/1600*N);xx<Mathf.FloorToInt((x+w)/1600*N);xx++)pixels[(N-1-yy)*N+xx]=new Color32(0,0,code,255);}
[InitializeOnLoadMethod]static void Init(){EditorApplication.delayCall+=Run;}
static void Run(){
if(!File.Exists("VenueLighting.request"))return;File.Delete("VenueLighting.request");
Directory.CreateDirectory("VenueLightingReview");Directory.CreateDirectory("VenueLightingBackup");
var c=UnityEngine.Object.FindObjectOfType<CityDayNightController>(true);bool night=c.IsNight;bool async=ShaderUtil.allowAsyncCompilation;
try{
ShaderUtil.allowAsyncCompilation=false;int lights=UnityEngine.Object.FindObjectsOfType<Light>(true).Length;int mats=AssetDatabase.FindAssets("t:Material").Length;
if(!File.Exists("Assets/Editor/DayNightBackups/BeforeVenueLighting.unity"))EditorSceneManager.SaveScene(c.gameObject.scene,"Assets/Editor/DayNightBackups/BeforeVenueLighting.unity",true);
foreach(var name in new[]{"Building 1","Casino","Bus Station"}){
var m=AssetDatabase.LoadAssetAtPath<Material>("Assets/Scenes/Oasis/3D/Fbx/Materials/"+name+".mat");
string mp=AssetDatabase.GetAssetPath(m);if(!File.Exists("VenueLightingBackup/"+name+".mat"))File.Copy(mp,"VenueLightingBackup/"+name+".mat");
var old=m.GetTexture("_CityNightMap");pixels=new Color32[N*N];
if(old){var p=AssetDatabase.GetAssetPath(old);if(!File.Exists("VenueLightingBackup/"+name+" Night.png"))File.Copy(p,"VenueLightingBackup/"+name+" Night.png");var t=Read(p);pixels=t.GetPixels32();UnityEngine.Object.DestroyImmediate(t);}
albedo=Read(AssetDatabase.GetAssetPath(m.GetTexture("_BaseMap")));
if(name=="Building 1"){
// Supermarket atlas: retail glass, posters and selected colored frontage only.
Fill(1129,1317,33,111,.65f);Fill(1561,1393,35,110,.65f);Fill(801,1322,30,65,.65f);
Fill(804,804,46,306,.15f);Fill(805,1110,47,119,.15f);
ColorFill(986,1375,8,52,0);Fill(807,873,34,13,1);Fill(807,1039,34,15,1);Fill(807,1127,34,17,1);Fill(807,1165,34,14,1);
}
if(name=="Casino"){
// Blue glass within patterned facade; silver relief stays non-emissive.
Fill(77,805,1518,315,.32f,true);Fill(3,1130,1592,311,.32f,true);
Fill(786,1510,299,82,.65f);
Fill(16,672,1574,23,.16f);Fill(18,743,1570,10,.18f);ColorFill(18,672,1572,4,255);ColorFill(18,741,1572,4,85);
}
if(name=="Bus Station"){
// Gentle fill on the blue frontage, with brighter ground-level glass.
Fill(930,266,662,330,.48f,true);Fill(910,607,680,326,.48f,true);
Fill(667,945,923,360,.48f,true);Fill(672,1319,918,267,.72f,true);
Fill(240,1184,294,398,.7f,true);
Fill(724,41,863,41,.22f);Fill(0,0,285,1598,.17f);Fill(407,839,133,313,.3f);
}
UnityEngine.Object.DestroyImmediate(albedo);
string path="Assets/Scenes/Oasis/CityDetail/Night/"+name+" Night.png";
var texture=new Texture2D(N,N,TextureFormat.RGBA32,false,true);texture.SetPixels32(pixels);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);
AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.sRGBTexture=false;importer.mipmapEnabled=true;importer.maxTextureSize=N;importer.alphaSource=TextureImporterAlphaSource.FromInput;
importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings{name="Android",overridden=true,maxTextureSize=N,format=TextureImporterFormat.ASTC_4x4});importer.SaveAndReimport();
m.SetTexture("_CityNightMap",AssetDatabase.LoadAssetAtPath<Texture2D>(path));CityMaterialUpgrade.Keywords(m);EditorUtility.SetDirty(m);
}
var shop=UnityEngine.Object.FindObjectsOfType<MeshRenderer>().First(r=>r.name=="building"&&r.gameObject.scene==c.gameObject.scene).GetComponent<MeshFilter>();
string signMeshPath="Assets/Scenes/Oasis/CityDetail/Supermarket Sign Mesh.asset";
var signMesh=AssetDatabase.LoadAssetAtPath<Mesh>(signMeshPath);
if(!signMesh){signMesh=UnityEngine.Object.Instantiate(shop.sharedMesh);signMesh.name="Supermarket Sign Mesh";var v=signMesh.vertices;var uv=signMesh.uv;int changed=0;for(int i=0;i<v.Length;i++){var w=shop.transform.TransformPoint(v[i]);if(w.y>35.1f&&uv[i].x*1600>986&&uv[i].x*1600<994&&uv[i].y*1600>175&&uv[i].y*1600<224){uv[i].x+=2;changed++;}}signMesh.uv=uv;AssetDatabase.CreateAsset(signMesh,signMeshPath);File.WriteAllText("VenueLightingReview/SignMesh.txt","Tagged sign vertices: "+changed);}
var signUv=signMesh.uv;var signV=signMesh.vertices;for(int i=0;i<signUv.Length;i++){if(signUv[i].x>=2)signUv[i].x-=2;if(shop.transform.TransformPoint(signV[i]).y>34&&signUv[i].x*1600>986&&signUv[i].x*1600<994&&signUv[i].y*1600>165&&signUv[i].y*1600<230)signUv[i].x+=2;}signMesh.uv=signUv;EditorUtility.SetDirty(signMesh);shop.sharedMesh=signMesh;EditorUtility.SetDirty(shop);
c.presets.night.venueBrightness=2.4f;EditorUtility.SetDirty(c.presets);AssetDatabase.SaveAssets();
c.ApplyNight();
foreach(var name in new[]{"building","Casino","polySurface248_phong3_0"}){
var r=UnityEngine.Object.FindObjectsOfType<MeshRenderer>().First(r=>r.name==name&&r.gameObject.scene==c.gameObject.scene);var b=r.bounds;
var dir=name=="Casino"?new Vector3(0,.12f,1):name=="building"?new Vector3(-1,.08f,-.08f):new Vector3(-.85f,.04f,.65f);
var pos=name=="polySurface248_phong3_0"?new Vector3(-79,39,-590):b.center+dir.normalized*b.size.magnitude*.8f;
var pose=Tuple.Create(pos,Quaternion.LookRotation(b.center-pos));
CityLightingBuilder.Capture(c,pose,"Venue-After-"+name);
c.ApplyDay();CityLightingBuilder.Capture(c,pose,"Venue-Day-"+name);c.ApplyNight();
}
var shader=Shader.Find("Gangstar City/City Detail Mobile");var errors=ShaderUtil.GetShaderMessages(shader).Where(e=>e.severity.ToString()=="Error").ToArray();if(errors.Length>0)throw new Exception(string.Join("\n",errors.Select(e=>e.message)));
if(mats!=AssetDatabase.FindAssets("t:Material").Length||lights!=UnityEngine.Object.FindObjectsOfType<Light>(true).Length)throw new Exception("Material/light budget changed");
if(night)c.ApplyNight();else c.ApplyDay();EditorUtility.SetDirty(c);EditorSceneManager.MarkSceneDirty(c.gameObject.scene);EditorSceneManager.SaveScene(c.gameObject.scene);Selection.activeObject=c.presets;
File.WriteAllText("VenueLightingReview/Report.txt","SUCCESS: supermarket retail glass/posters, casino blue glazing and terminal blue front panels receive selective night fill. Existing lighting maps reused; Bus Station adds one 1024 ASTC4x4 map (~1.33 MiB with mipmaps). Material and light counts unchanged. Shader compiled and three night/day views rendered. Existing Day/Night state restored. Brightness control added to presets. Android device performance unmeasured.");
}catch(Exception e){File.WriteAllText("VenueLightingReview/Report.txt","FAILED "+e);Debug.LogException(e);}
finally{if(night)c.ApplyNight();else c.ApplyDay();ShaderUtil.allowAsyncCompilation=async;}
}}





