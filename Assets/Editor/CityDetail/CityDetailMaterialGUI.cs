using UnityEditor;
using UnityEngine;
public class CityDetailMaterialGUI : ShaderGUI {
 public override void OnGUI(MaterialEditor editor,MaterialProperty[] props) {
  var m=(Material)editor.target;
  EditorGUILayout.LabelField("City Detail · Mobile",EditorStyles.boldLabel);
  editor.TexturePropertySingleLine(new GUIContent("Color / Tint"),FindProperty("_BaseMap",props),FindProperty("_BaseColor",props));
  editor.TextureScaleOffsetProperty(FindProperty("_BaseMap",props));
  editor.TexturePropertySingleLine(new GUIContent("Normal Detail"),FindProperty("_BumpMap",props),FindProperty("_BumpScale",props));
  editor.RangeProperty(FindProperty("_CityContrast",props),"Texture Contrast");
  EditorGUILayout.Space();EditorGUILayout.LabelField("Surface Detail",EditorStyles.boldLabel);
  if(editor.targets.Length==1){
   var data=CityMaterialUpgrade.Sources(m);
   if(data){
    EditorGUI.BeginChangeCheck();
    var metal=(Texture2D)EditorGUILayout.ObjectField("Metallic Map",data.metallic,typeof(Texture2D),false);
    var rough=(Texture2D)EditorGUILayout.ObjectField("Roughness Map",data.roughness,typeof(Texture2D),false);
    var ao=(Texture2D)EditorGUILayout.ObjectField("Occlusion Map",data.occlusion,typeof(Texture2D),false);
    var windows=(Texture2D)EditorGUILayout.ObjectField("Window Lights Mask",data.windows,typeof(Texture2D),false);
    if(EditorGUI.EndChangeCheck()){
     Undo.RecordObject(data,"Change building surface maps");data.metallic=metal;data.roughness=rough;data.occlusion=ao;data.windows=windows;EditorUtility.SetDirty(data);
     EditorApplication.delayCall+=()=>{if(m&&data)CityMaterialUpgrade.Pack(m,data);};
    }
   }
  }
  editor.RangeProperty(FindProperty("_CityMetallic",props),"Metallic Strength");
  editor.RangeProperty(FindProperty("_Smoothness",props),"Smoothness Strength");
  editor.RangeProperty(FindProperty("_CitySmoothnessLimit",props),"Maximum Shininess");
  editor.RangeProperty(FindProperty("_OcclusionStrength",props),"Occlusion Strength");
  editor.ShaderProperty(FindProperty("_EnvironmentReflections",props),"Environment Reflections");
  editor.TexturePropertySingleLine(new GUIContent("Night Window Colors / Accents"),FindProperty("_CityNightMap",props));
  EditorGUILayout.Space();EditorGUILayout.LabelField("Day Emission",EditorStyles.boldLabel);
  EditorGUILayout.HelpBox("Night light is restricted to the Window Lights Mask. Brightness, warmth and lit windows are controlled in City Day Night Presets.",MessageType.Info);
  editor.ColorProperty(FindProperty("_EmissionColor",props),"Emission Tint");
  editor.RangeProperty(FindProperty("_CityEmissionStrength",props),"Emission Strength");
  EditorGUILayout.Space();EditorGUILayout.LabelField("Railing Post Emission",EditorStyles.boldLabel);
  editor.RangeProperty(FindProperty("_CityRailingGlow",props),"Post Glow Strength");
  editor.ShaderProperty(FindProperty("_CityRailingUseEmissionMap",props),"Use Custom Emission Mask");
  editor.TexturePropertySingleLine(new GUIContent("Post Emission Map (Red Channel)"),FindProperty("_EmissionMap",props));
  EditorGUILayout.HelpBox("Only the amber post inserts emit orange, pink and red. Optional mask: white emits, black does not.",MessageType.Info);
  EditorGUILayout.Space();EditorGUILayout.LabelField("Cutout and Sides",EditorStyles.boldLabel);
  editor.ShaderProperty(FindProperty("_AlphaClip",props),"Alpha Clipping");
  if(m.GetFloat("_AlphaClip")>0.5f)editor.RangeProperty(FindProperty("_Cutoff",props),"Cutout Threshold");
  editor.ShaderProperty(FindProperty("_Cull",props),"Render Face");
  foreach(Material target in editor.targets)CityMaterialUpgrade.Keywords(target);
 }
}


