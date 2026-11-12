using System;
using System.Collections.Generic;
using UnityEngine;
[DisallowMultipleComponent]
public sealed class GangfirePrefabLightmaps : MonoBehaviour
{
 [Serializable] public struct Entry{public Renderer renderer;public int map;public Vector4 scaleOffset;}
 public Texture2D[] colorMaps,directionalMaps,shadowMasks;
 public Entry[] renderers;
 public LightmapsMode mode=LightmapsMode.NonDirectional;
 void Awake(){Apply();}
 [ContextMenu("Apply saved prefab lightmaps")]
 public void Apply(){if(colorMaps==null||colorMaps.Length==0)return;var current=new List<LightmapData>(LightmapSettings.lightmaps);var indices=new int[colorMaps.Length];bool empty=current.Count==0;
 for(int i=0;i<colorMaps.Length;i++){var directional=directionalMaps!=null&&i<directionalMaps.Length?directionalMaps[i]:null;var mask=shadowMasks!=null&&i<shadowMasks.Length?shadowMasks[i]:null;int found=current.FindIndex(m=>m.lightmapColor==colorMaps[i]&&m.lightmapDir==directional&&m.shadowMask==mask);if(found<0){found=current.Count;current.Add(new LightmapData{lightmapColor=colorMaps[i],lightmapDir=directional,shadowMask=mask});}indices[i]=found;}
 if(empty)LightmapSettings.lightmapsMode=mode;LightmapSettings.lightmaps=current.ToArray();if(renderers==null)return;foreach(var e in renderers)if(e.renderer!=null&&e.map>=0&&e.map<indices.Length){e.renderer.lightmapIndex=indices[e.map];e.renderer.lightmapScaleOffset=e.scaleOffset;}}
}
