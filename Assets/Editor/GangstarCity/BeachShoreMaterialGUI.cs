using UnityEditor;
using UnityEngine;
public class BeachShoreMaterialGUI : ShaderGUI {
 public override void OnGUI(MaterialEditor editor,MaterialProperty[] properties) {
  EditorGUILayout.LabelField("Beach Shoreline",EditorStyles.boldLabel);
  EditorGUILayout.Space();
  var tile=FindProperty("_TileMap",properties);editor.TextureProperty(tile,"Tiles - Original Texture");editor.TextureScaleOffsetProperty(tile);
  var sand=FindProperty("_SandMap",properties);editor.TextureProperty(sand,"Beach Sand - Original Texture");editor.TextureScaleOffsetProperty(sand);
  EditorGUILayout.Space();EditorGUILayout.LabelField("Beach Colors",EditorStyles.boldLabel);
  foreach(var p in new[]{"_TileTint","_SandTint","_WetTint"})editor.ColorProperty(FindProperty(p,properties),FindProperty(p,properties).displayName);
  EditorGUILayout.Space();EditorGUILayout.LabelField("Shore Transition",EditorStyles.boldLabel);
  foreach(var p in new[]{"_TileWidth","_BlendWidth","_EdgeSoftness","_WetWidth","_WetFade"})editor.RangeProperty(FindProperty(p,properties),FindProperty(p,properties).displayName);
  EditorGUILayout.Space();EditorGUILayout.LabelField("Surface Finish",EditorStyles.boldLabel);
  foreach(var p in new[]{"_TileSmoothness","_SandSmoothness","_WetSmoothness"})editor.RangeProperty(FindProperty(p,properties),FindProperty(p,properties).displayName);
 }
}

