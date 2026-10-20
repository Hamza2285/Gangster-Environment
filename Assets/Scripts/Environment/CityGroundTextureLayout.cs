using System;using UnityEngine;
/// <summary>Texture placement only: no meshes, materials, colliders or textures are instantiated.</summary>
[ExecuteAlways,DisallowMultipleComponent] public sealed class CityGroundTextureLayout:MonoBehaviour{
[Serializable] public sealed class Plot{public Renderer renderer;public Vector4 rect,data,feature0,feature1,fuel0,fuel1;public Vector4[] patches=new Vector4[12],modes=new Vector4[12];public Vector4[] outline=new Vector4[48],facades=new Vector4[48];}
public Plot[] plots=Array.Empty<Plot>();MaterialPropertyBlock block;bool dirty=true;
void OnEnable(){dirty=true;Apply();}void OnValidate(){dirty=true;}void LateUpdate(){if(dirty)Apply();}
public void Apply(){dirty=false;if(block==null)block=new MaterialPropertyBlock();foreach(var p in plots){if(!p.renderer)continue;p.renderer.GetPropertyBlock(block);block.SetVector("_PlotRect",p.rect);block.SetVector("_GroundData",p.data);block.SetVector("_Feature0",p.feature0);block.SetVector("_Feature1",p.feature1);block.SetVector("_Fuel0",p.fuel0);block.SetVector("_Fuel1",p.fuel1);block.SetVectorArray("_PatchRect",p.patches);block.SetVectorArray("_PatchMode",p.modes);block.SetVectorArray("_GroundOutline",p.outline);block.SetVectorArray("_GroundFacades",p.facades);p.renderer.SetPropertyBlock(block);}}
}
