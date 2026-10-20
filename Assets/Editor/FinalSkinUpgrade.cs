using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;
using System.Linq;
[InitializeOnLoad] public static class FinalSkinUpgrade {
const string P="Assets/Girl Player/Realistic Skin/";
static double next;
static FinalSkinUpgrade(){EditorApplication.update+=Tick;}
static void Tick(){if(EditorApplication.timeSinceStartup<next||EditorApplication.isCompiling||EditorApplication.isUpdating)return;next=EditorApplication.timeSinceStartup+4;if(!File.Exists(P+"Skin Normal.png")||File.Exists(P+"Applied.txt"))return;try{Run();}catch(System.Exception e){File.WriteAllText(P+"Error.txt",e.ToString());next=double.MaxValue;}}
static void Run(){
AssetDatabase.Refresh();
foreach(var f in new[]{"Skin Base Color.png","Skin Normal.png"}){var t=AssetImporter.GetAtPath(P+f) as TextureImporter;if(t==null)return;t.maxTextureSize=4096;t.mipmapEnabled=true;t.textureType=f.Contains("Normal")?TextureImporterType.NormalMap:TextureImporterType.Default;t.textureCompression=TextureImporterCompression.CompressedHQ;t.SaveAndReimport();}
var roots=Enumerable.Range(0,UnityEngine.SceneManagement.SceneManager.sceneCount).SelectMany(i=>UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).GetRootGameObjects()).ToArray();
var root=roots.SelectMany(x=>x.GetComponentsInChildren<Transform>(true)).FirstOrDefault(x=>AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(x.gameObject))=="Assets/Girl Player/Final.fbx"&&x.name=="Final");
if(root==null){File.WriteAllText(P+"Waiting.txt","Final root not found in loaded scene");return;}
EditorSceneManager.SaveScene(root.gameObject.scene,P+"Girl Player Before Skin.unity",true);
var mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));mat.name="Final Realistic Skin";mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(P+"Skin Base Color.png"));mat.SetColor("_BaseColor",Color.white);mat.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(P+"Skin Normal.png"));mat.SetFloat("_BumpScale",.65f);mat.SetFloat("_Metallic",0);mat.SetFloat("_Smoothness",.32f);mat.EnableKeyword("_NORMALMAP");AssetDatabase.CreateAsset(mat,P+"Final Realistic Skin.mat");
int count=0;string audit="";foreach(var r in root.GetComponentsInChildren<Renderer>(true)){audit+=r.name+": "+string.Join(",",r.sharedMaterials.Select(m=>m?m.name:"null"))+"\n";if(new[]{"Body","Body002","Object001"}.Contains(r.name)){Undo.RecordObject(r,"Upgrade Final skin");r.sharedMaterials=r.sharedMaterials.Select(m=>mat).ToArray();EditorUtility.SetDirty(r);PrefabUtility.RecordPrefabInstancePropertyModifications(r);count++;}}
if(count!=3)throw new System.Exception("Expected 3 skin renderers; found "+count);
EditorSceneManager.MarkSceneDirty(root.gameObject.scene);EditorSceneManager.SaveScene(root.gameObject.scene);AssetDatabase.SaveAssets();File.WriteAllText(P+"Applied.txt","Updated "+count+" skin renderers. Original scene backed up.\n"+audit);
}
}
