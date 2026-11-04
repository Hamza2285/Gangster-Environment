using UnityEngine;
using UnityEditor;
public sealed class ArtworkMaterialGUI:ShaderGUI {
 public override void OnGUI(MaterialEditor e,MaterialProperty[] p){
  EditorGUILayout.LabelField("ARTWORK NIGHT LIGHTING",EditorStyles.boldLabel);
  EditorGUILayout.HelpBox("Day/Night presets switch the artwork glow automatically. White preserves the artwork colors. Set brightness to zero to turn it off.",MessageType.Info);
  e.ShaderProperty(FindProperty("_ArtworkBrightness",p),"Night Artwork Brightness");
  e.ShaderProperty(FindProperty("_ArtworkColor",p),"Artwork Light Color");
  e.TexturePropertySingleLine(new GUIContent("Atlas"),FindProperty("_BaseMap",p),FindProperty("_BaseColor",p));
  e.TexturePropertySingleLine(new GUIContent("Normal Map"),FindProperty("_BumpMap",p),FindProperty("_BumpScale",p));
  e.TexturePropertySingleLine(new GUIContent("Packed Surface (R Metal / B Artwork / A Smoothness)"),FindProperty("_MetallicGlossMap",p));
  e.ShaderProperty(FindProperty("_Smoothness",p),"Smoothness");
  EditorGUILayout.Space();EditorGUILayout.LabelField("EMISSION MAP",EditorStyles.boldLabel);
  e.ShaderProperty(FindProperty("_UseEmissionMap",p),"Use Emission Map");
  e.TexturePropertySingleLine(new GUIContent("Emission Map / HDR Color"),FindProperty("_EmissionMap",p),FindProperty("_EmissionColor",p));
  e.ShaderProperty(FindProperty("_EmissionStrength",p),"Emission Brightness");
  e.ShaderProperty(FindProperty("_EmissionNightOnly",p),"Night Only");
  EditorGUILayout.HelpBox("Black pixels stay unlit. The map adds selective glow while preserving the existing artwork lighting. Brightness 0 turns emission off. No realtime lights are added.",MessageType.Info);
  e.EnableInstancingField();
  foreach(Material m in e.targets){if(m.GetFloat("_UseEmissionMap")>.5f&&m.GetTexture("_EmissionMap"))m.EnableKeyword("_ARTWORK_EMISSION_MAP");else m.DisableKeyword("_ARTWORK_EMISSION_MAP");if(m.GetTexture("_BumpMap"))m.EnableKeyword("_NORMALMAP");else m.DisableKeyword("_NORMALMAP");m.EnableKeyword("_METALLICSPECGLOSSMAP");}
 }
}


