Shader "Gangfire/Two Texture PBR" {
Properties {
_BaseMap("Colour atlas",2D)="white"{} _PackedMap("Normal RG Metallic B Smoothness A",2D)="white"{}
_BaseColor("Tint",Color)=(1,1,1,1) _EmissionColor("Emission",Color)=(0,0,0,0)
_AtlasRect("Atlas region",Vector)=(0,0,1,1) _Metallic("Metallic",Float)=0 _Smoothness("Smoothness",Float)=0.3 _BumpScale("Normal strength",Float)=1
_ClampUV("Clamp",Float)=0 _Cutout("Alpha clip",Float)=0 _Cutoff("Cutoff",Float)=0.5 _SDF("Font distance field",Float)=0 _Unlit("Unlit",Float)=0 _Surface("Transparent",Float)=0
[HideInInspector]_SrcBlend("Src",Float)=1 [HideInInspector]_DstBlend("Dst",Float)=0 [HideInInspector]_ZWrite("ZWrite",Float)=1 _Cull("Cull",Float)=2
}
SubShader {Tags{"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline"} LOD 200
HLSLINCLUDE
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);TEXTURE2D(_PackedMap);SAMPLER(sampler_PackedMap);
CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST,_BaseColor,_EmissionColor,_AtlasRect;float _Metallic,_Smoothness,_BumpScale,_ClampUV,_Cutout,_Cutoff,_SDF,_Unlit,_Surface;
CBUFFER_END
float2 AtlasUV(float2 uv){float2 p=uv*_BaseMap_ST.xy+_BaseMap_ST.zw;p=lerp(frac(p),saturate(p),_ClampUV);return _AtlasRect.xy+p*_AtlasRect.zw;}
half4 ReadBase(float2 uv){return SAMPLE_TEXTURE2D_GRAD(_BaseMap,sampler_BaseMap,AtlasUV(uv),ddx(uv)*_BaseMap_ST.xy*_AtlasRect.zw,ddy(uv)*_BaseMap_ST.xy*_AtlasRect.zw)*_BaseColor;}
half AlphaValue(half a){if(_SDF>.5)a=smoothstep(_Cutoff-fwidth(a),_Cutoff+fwidth(a),a);if(_Cutout>.5)clip(a-(_SDF>.5?.01:_Cutoff));return _Surface>.5||_SDF>.5?a:1;}
struct A{float4 positionOS:POSITION;float3 normalOS:NORMAL;float4 tangentOS:TANGENT;float2 uv:TEXCOORD0;float2 lm:TEXCOORD1;};
struct V{float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;half4 tangentWS:TEXCOORD2;float2 uv:TEXCOORD3;DECLARE_LIGHTMAP_OR_SH(lightmapUV,vertexSH,4);half fog:TEXCOORD6;};
V Vert(A a){V v=(V)0;VertexPositionInputs pos=GetVertexPositionInputs(a.positionOS.xyz);VertexNormalInputs nor=GetVertexNormalInputs(a.normalOS,a.tangentOS);v.positionCS=pos.positionCS;v.positionWS=pos.positionWS;v.normalWS=nor.normalWS;v.tangentWS=half4(nor.tangentWS,a.tangentOS.w*GetOddNegativeScale());v.uv=a.uv;OUTPUT_LIGHTMAP_UV(a.lm,unity_LightmapST,v.lightmapUV);OUTPUT_SH(v.normalWS,v.vertexSH);v.fog=ComputeFogFactor(v.positionCS.z);return v;}
half4 Frag(V v):SV_Target{half4 base=ReadBase(v.uv);half alpha=AlphaValue(base.a);if(_Unlit>.5)return half4(MixFog(base.rgb+base.rgb*_EmissionColor.rgb,v.fog),alpha);half4 data=SAMPLE_TEXTURE2D_GRAD(_PackedMap,sampler_PackedMap,AtlasUV(v.uv),ddx(v.uv)*_BaseMap_ST.xy*_AtlasRect.zw,ddy(v.uv)*_BaseMap_ST.xy*_AtlasRect.zw);half2 xy=(data.rg*2-1)*_BumpScale;half3 nts=half3(xy,sqrt(saturate(1-dot(xy,xy))));half3 normal=NormalizeNormalPerPixel(v.normalWS);half3 tangent=normalize(v.tangentWS.xyz);half3 bitangent=cross(normal,tangent)*v.tangentWS.w;normal=NormalizeNormalPerPixel(mul(nts,half3x3(tangent,bitangent,normal)));InputData input=(InputData)0;input.positionWS=v.positionWS;input.normalWS=normal;input.viewDirectionWS=GetWorldSpaceNormalizeViewDir(v.positionWS);input.shadowCoord=TransformWorldToShadowCoord(v.positionWS);input.fogCoord=v.fog;input.vertexLighting=VertexLighting(v.positionWS,normal);input.bakedGI=SAMPLE_GI(v.lightmapUV,v.vertexSH,normal);input.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(v.positionCS);input.shadowMask=SAMPLE_SHADOWMASK(v.lightmapUV);SurfaceData s=(SurfaceData)0;s.albedo=base.rgb;s.metallic=saturate(data.b*_Metallic);s.smoothness=saturate(data.a*_Smoothness);s.normalTS=nts;s.occlusion=1;s.alpha=alpha;s.emission=base.rgb*_EmissionColor.rgb;half4 color=UniversalFragmentPBR(input,s);color.rgb=MixFog(color.rgb,v.fog);return color;}
ENDHLSL
Pass{Name "ForwardLit" Tags{"LightMode"="UniversalForward"} Blend[_SrcBlend][_DstBlend] ZWrite[_ZWrite] Cull[_Cull]
HLSLPROGRAM
#pragma target 3.0
#pragma vertex Vert
#pragma fragment Frag
#pragma multi_compile _ LIGHTMAP_ON
#pragma multi_compile _ DIRLIGHTMAP_COMBINED
#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
#pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
#pragma multi_compile_fragment _ _SHADOWS_SOFT
#pragma multi_compile_fog
ENDHLSL
}
Pass{Name "ShadowCaster" Tags{"LightMode"="ShadowCaster"} ZWrite On ColorMask 0 Cull[_Cull]
HLSLPROGRAM
#pragma target 3.0
#pragma vertex SV
#pragma fragment SF
float3 _LightDirection;float3 _LightPosition;
struct SD{float4 p:SV_POSITION;float2 uv:TEXCOORD0;};
SD SV(A a){SD o;float3 pos=TransformObjectToWorld(a.positionOS.xyz);float3 norm=TransformObjectToWorldNormal(a.normalOS);o.p=TransformWorldToHClip(ApplyShadowBias(pos,norm,_LightDirection));o.uv=a.uv;return o;}
half4 SF(SD i):SV_Target{AlphaValue(ReadBase(i.uv).a);return 0;}
ENDHLSL
}
Pass{Name "DepthOnly" Tags{"LightMode"="DepthOnly"} ZWrite On ColorMask R Cull[_Cull]
HLSLPROGRAM
#pragma vertex Vert
#pragma fragment DepthFrag
half4 DepthFrag(V i):SV_Target{AlphaValue(ReadBase(i.uv).a);return 0;}
ENDHLSL
}
Pass{Name "Meta" Tags{"LightMode"="Meta"} Cull Off
HLSLPROGRAM
#pragma vertex MV
#pragma fragment MF
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"
struct MA{float4 p:POSITION;float2 uv:TEXCOORD0;float2 uv1:TEXCOORD1;float2 uv2:TEXCOORD2;};struct MD{float4 p:SV_POSITION;float2 uv:TEXCOORD0;};
MD MV(MA a){MD o;o.p=UnityMetaVertexPosition(a.p.xyz,a.uv1,a.uv2);o.uv=a.uv;return o;}
half4 MF(MD i):SV_Target{half4 base=ReadBase(i.uv);AlphaValue(base.a);MetaInput m=(MetaInput)0;m.Albedo=base.rgb;m.Emission=base.rgb*_EmissionColor.rgb;return MetaFragment(m);}
ENDHLSL
}
}
}

