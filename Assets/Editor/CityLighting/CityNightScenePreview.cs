using UnityEngine;
using UnityEditor;
[InitializeOnLoad]public static class CityNightScenePreview {
 static double next;
 static CityNightScenePreview(){EditorApplication.update+=Tick;}
 static void Tick(){
  if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.timeSinceStartup<next)return;
  next=EditorApplication.timeSinceStartup+.5;
  var view=SceneView.lastActiveSceneView;if(!view)return;
  var c=Object.FindObjectOfType<CityDayNightController>();
  if(c&&c.IsNight){c.RefreshStreetLights(view.camera);view.Repaint();}
 }
}

