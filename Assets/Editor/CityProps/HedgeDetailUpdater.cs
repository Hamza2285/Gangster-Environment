using System;using System.IO;using System.Linq;using System.Reflection;using UnityEngine;using UnityEditor;using UnityEngine.SceneManagement;
[InitializeOnLoad]public static partial class HedgeDetailUpdater{static HedgeDetailUpdater(){EditorApplication.update+=Poll;}static void Poll(){if(EditorApplication.isCompiling||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists("HedgeDetail.request"))return;string mode=File.ReadAllText("HedgeDetail.request").Trim();File.Delete("HedgeDetail.request");try{if(mode=="restore-green")RestoreOriginalGreen();else if(mode=="atlas-tune")AtlasTune();else if(mode=="atlas-close")AtlasCloseCapture();else if(mode=="atlas-apply")AtlasApply();else if(mode=="atlas-audit")AuditAtlasSpace();else if(mode=="mediannatural")MedianNaturalFoliage();else if(mode=="palmtrunk")PalmThicken();else if(mode=="medianround")RoundMedianCaps();else if(mode=="medianbuild")MedianBuild();else if(mode=="medianaudit")MedianAudit();else if(mode=="clarity")ClarityPreview();else if(mode=="leafatlas")ApplyLeafAtlas();else if(mode=="repair")Repair();else if(mode=="build")Build();else if(mode=="capture")CaptureHedge();else Audit();}catch(Exception e){File.WriteAllText("FinalEnvironmentRoadReview/HedgeDetail.txt",e.ToString());Debug.LogException(e);}}
static object Read(Mesh mesh)=>typeof(FinalPNGCityDetails).GetMethod("Read",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{mesh});static T F<T>(object d,string f)=>(T)d.GetType().GetField(f).GetValue(d);
static void Audit(){var sc=SceneManager.GetActiveScene();string log="SCENE="+sc.path+"\n";foreach(string name in new[]{"green","green1","green_base1"}){string path="Assets/Scenes/Oasis/3D/Prefeb/"+name+".prefab";var a=AssetDatabase.LoadAssetAtPath<GameObject>(path);var f=a.GetComponent<MeshFilter>();var mesh=f.sharedMesh;var data=Read(mesh);var p=F<Vector3[]>(data,"p").Select(v=>a.transform.localRotation*v).ToArray();var uv=F<Vector2[][]>(data,"uv")[0];log+=path+" mesh="+mesh.name+" vertex="+p.Length+" triangles="+mesh.GetIndexCount(0)/3+" size="+(new Vector3(p.Max(v=>v.x)-p.Min(v=>v.x),p.Max(v=>v.y)-p.Min(v=>v.y),p.Max(v=>v.z)-p.Min(v=>v.z)))+" min="+new Vector3(p.Min(v=>v.x),p.Min(v=>v.y),p.Min(v=>v.z))+" max="+new Vector3(p.Max(v=>v.x),p.Max(v=>v.y),p.Max(v=>v.z))+" UV="+uv.Min(v=>v.x)+","+uv.Max(v=>v.x)+" / "+uv.Min(v=>v.y)+","+uv.Max(v=>v.y)+"\n";}
foreach(var f in sc.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshFilter>(true)).Where(f=>(f.name=="green"||f.name.StartsWith("green (")||f.name=="green1"||f.name.StartsWith("green1 (")||(f.sharedMesh&&f.sharedMesh.name.StartsWith("green")))).Take(20))log+=f.name+" pos="+f.transform.position+" bounds="+f.GetComponent<Renderer>().bounds+" source="+PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(f.gameObject)+" mesh="+AssetDatabase.GetAssetPath(f.sharedMesh)+" lod="+(f.GetComponentInParent<LODGroup>()?f.GetComponentInParent<LODGroup>().name:"none")+" meshName="+f.sharedMesh.name+" parent="+f.transform.parent?.name+"\n";File.WriteAllText("FinalEnvironmentRoadReview/HedgeAudit.txt",log);}}

































