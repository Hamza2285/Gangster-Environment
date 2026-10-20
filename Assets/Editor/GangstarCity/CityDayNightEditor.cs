using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
[CustomEditor(typeof(CityDayNightController))]
public sealed class CityDayNightControllerEditor:Editor {
 public override void OnInspectorGUI(){
  var c=(CityDayNightController)target;
  EditorGUILayout.LabelField("CITY LIGHTING",EditorStyles.boldLabel);
  EditorGUILayout.HelpBox("Choose Day or Night. Open the preset file below to tune either look. Day preserves the captured scene lighting.",MessageType.Info);
  Buttons(c);
  DrawDefaultInspector();
 }
 public static void Buttons(CityDayNightController c){
  EditorGUILayout.BeginHorizontal();
  if(GUILayout.Button("DAY",GUILayout.Height(38)))Switch(c,false);
  if(GUILayout.Button("NIGHT",GUILayout.Height(38)))Switch(c,true);
  EditorGUILayout.EndHorizontal();
  EditorGUILayout.LabelField("Current look: "+(c.IsNight?"Night":"Day"));
 }
 static void Switch(CityDayNightController c,bool night){
  Undo.RecordObject(c,"Change city lighting");if(c.sun){Undo.RecordObject(c.sun,"Change city lighting");Undo.RecordObject(c.sun.transform,"Change city lighting");}
  c.Apply(night);EditorUtility.SetDirty(c);
  if(!Application.isPlaying)EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
  SceneView.RepaintAll();
 }
}
[CustomEditor(typeof(CityDayNightPresets))]
public sealed class CityDayNightPresetsEditor:Editor {
 public override void OnInspectorGUI(){
  var c=Object.FindAnyObjectByType<CityDayNightController>(FindObjectsInactive.Include);
  if(c&&c.presets==target)CityDayNightControllerEditor.Buttons(c);
  var night=serializedObject.FindProperty("night");
  var day=serializedObject.FindProperty("day");
  serializedObject.Update();
  EditorGUILayout.Space();EditorGUILayout.LabelField("QUICK LIGHTING CONTROLS",EditorStyles.boldLabel);
  EditorGUILayout.PropertyField(day.FindPropertyRelative("sceneBrightness"),new GUIContent("Day Brightness"));
  EditorGUILayout.PropertyField(night.FindPropertyRelative("sceneBrightness"),new GUIContent("Night Brightness"));
  EditorGUILayout.PropertyField(night.FindPropertyRelative("windowLightsEnabled"),new GUIContent("Window Lights On"));
  EditorGUILayout.PropertyField(night.FindPropertyRelative("windowBrightness"),new GUIContent("Window Brightness"));
  EditorGUILayout.PropertyField(night.FindPropertyRelative("windowWarmth"),new GUIContent("Window Warmth"));
  EditorGUILayout.PropertyField(night.FindPropertyRelative("windowColorMix"),new GUIContent("Mixed Window Colors"));
  EditorGUILayout.PropertyField(night.FindPropertyRelative("accentBrightness"),new GUIContent("Pink / Blue Accent Brightness"));
  EditorGUILayout.PropertyField(night.FindPropertyRelative("venueBrightness"),new GUIContent("Shops / Casino / Terminal Brightness"));
  EditorGUILayout.PropertyField(night.FindPropertyRelative("litWindowAmount"),new GUIContent("Amount of Lit Windows"));
  EditorGUILayout.PropertyField(night.FindPropertyRelative("whiteWindowColor"),new GUIContent("White Window Color"));
  EditorGUILayout.PropertyField(night.FindPropertyRelative("blueWindowColor"),new GUIContent("Blue Window Color"));
  EditorGUILayout.PropertyField(night.FindPropertyRelative("blueAccentColor"),new GUIContent("Blue Accent Color"));
  EditorGUILayout.PropertyField(night.FindPropertyRelative("pinkAccentColor"),new GUIContent("Pink Accent Color"));
  EditorGUILayout.HelpBox("Window light only affects the dedicated glass masks. Warmth goes from cool white (0) to warm amber (1). Set brightness to 0 or switch Window Lights On off to remove window illumination.",MessageType.Info);
  if(serializedObject.ApplyModifiedProperties()&&c&&c.presets==target){c.Apply(c.IsNight);SceneView.RepaintAll();}
  EditorGUILayout.Space();
  EditorGUI.BeginChangeCheck();DrawDefaultInspector();
  if(EditorGUI.EndChangeCheck()&&c&&c.presets==target)c.Apply(c.IsNight);
 }
 [MenuItem("Gangstar City/Lighting/Day")]static void Day(){var c=Object.FindAnyObjectByType<CityDayNightController>(FindObjectsInactive.Include);if(c)c.ApplyDay();SceneView.RepaintAll();}
 [MenuItem("Gangstar City/Lighting/Night")]static void Night(){var c=Object.FindAnyObjectByType<CityDayNightController>(FindObjectsInactive.Include);if(c)c.ApplyNight();SceneView.RepaintAll();}
 [MenuItem("Gangstar City/Lighting/Open Presets")]static void Open(){Selection.activeObject=AssetDatabase.LoadAssetAtPath<CityDayNightPresets>("Assets/Scenes/Oasis/DayNight/City Day Night Presets.asset");}
}




