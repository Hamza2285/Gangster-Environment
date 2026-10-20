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
  e.EnableInstancingField();
  foreach(Material m in e.targets){if(m.GetTexture("_BumpMap"))m.EnableKeyword("_NORMALMAP");else m.DisableKeyword("_NORMALMAP");m.EnableKeyword("_METALLICSPECGLOSSMAP");}
 }
}
