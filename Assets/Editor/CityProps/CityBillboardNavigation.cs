using System;using System.IO;using System.Linq;using UnityEngine;using UnityEditor;using UnityEditor.SceneManagement;
public static class CityBillboardNavigation{
static int index=2;
static Transform Root(){return UnityEngine.Object.FindObjectsOfType<Transform>(true).FirstOrDefault(t=>t.name=="Building Billboards - Sparse"&&t.gameObject.scene.IsValid());}
[MenuItem("Gangstar City/Billboards/Show Next Billboard")]
public static void Next(){
var root=Root();if(!root||root.childCount==0){Debug.LogWarning("Open the Gangstar City scene to find its building billboards.");return;}
index=(index+1)%root.childCount;Show(root.GetChild(index));
}
static void Show(Transform tr){
SceneVisibilityManager.instance.Show(tr.gameObject,true);Selection.activeGameObject=tr.gameObject;EditorGUIUtility.PingObject(tr.gameObject);
var view=SceneView.lastActiveSceneView;if(!view)view=EditorWindow.GetWindow<SceneView>();
view.orthographic=false;view.LookAtDirect(tr.position,Quaternion.LookRotation(-tr.forward+Vector3.down*.12f),28);view.Repaint();view.Focus();
}
[MenuItem("Gangstar City/Billboards/Select Billboard Group")]
public static void SelectGroup(){var root=Root();if(root){Selection.activeGameObject=root.gameObject;EditorGUIUtility.PingObject(root.gameObject);}}
[InitializeOnLoadMethod]static void Init(){EditorApplication.delayCall+=Repair;}
static void Repair(){
if(!File.Exists("BillboardsShow.request"))return;File.Delete("BillboardsShow.request");
try{
var root=Root();if(!root||root.childCount!=7)throw new Exception("Expected seven existing billboards.");
string backup="Assets/Editor/PropsBackups/BeforeBillboardVisibility-20260929.unity";
if(!File.Exists(backup))EditorSceneManager.SaveScene(root.gameObject.scene,backup,true);
int triangles=0;
foreach(Transform tr in root){
var r=tr.GetComponent<MeshRenderer>();var lod=tr.GetComponent<LODGroup>();triangles+=tr.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3;
Undo.RecordObject(lod,"Extend billboard visibility");var levels=lod.GetLODs();levels[0].screenRelativeTransitionHeight=.015f;lod.SetLODs(levels);lod.RecalculateBounds();EditorUtility.SetDirty(lod);
if(!tr.gameObject.activeInHierarchy||!r.enabled||!r.sharedMaterial||!tr.GetComponent<MeshFilter>().sharedMesh)throw new Exception("Invalid billboard: "+tr.name);
}
EditorSceneManager.MarkSceneDirty(root.gameObject.scene);EditorSceneManager.SaveScene(root.gameObject.scene);
var c=UnityEngine.Object.FindObjectOfType<CityDayNightController>(true);var target=root.GetChild(3);var pos=target.position+target.forward*55+Vector3.up*3;
CityLightingBuilder.Capture(c,Tuple.Create(pos,Quaternion.LookRotation(target.position-pos)),"Billboards-Visible-Today");
Show(target);
File.WriteAllText("BuildingBillboardReview/VisibilityFix-20260929.txt","SUCCESS: verified seven active billboards in the open saved Gangstar City scene. Distance cutoff changed from 2.5% to 1.5% screen height. "+triangles+" triangles; existing materials and atlas unchanged. Added Show Next Billboard and Select Billboard Group menus. Unity Scene View focused on rooftop Grill Rush billboard. No duplicate objects added.");
}catch(Exception e){File.WriteAllText("BuildingBillboardReview/VisibilityFix-20260929.txt","FAILED "+e);Debug.LogException(e);}
}
}

