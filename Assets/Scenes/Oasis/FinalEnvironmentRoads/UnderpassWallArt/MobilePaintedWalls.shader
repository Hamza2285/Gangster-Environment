Shader "City/Mobile Painted Underpass Walls" {
Properties{_BaseMap("Existing concrete",2D)="white"{} _BaseColor("Concrete tint",Color)=(1,1,1,1) _Artwork("Existing artwork atlas",2D)="white"{} [HideInInspector]_Art0("Art 0",Vector)=(0,0,1,1) [HideInInspector]_Art1("Art 1",Vector)=(0,0,1,1) [HideInInspector]_Art2("Art 2",Vector)=(0,0,1,1) [HideInInspector]_Art3("Art 3",Vector)=(0,0,1,1) [HideInInspector]_Art4("Art 4",Vector)=(0,0,1,1) [HideInInspector]_Art5("Art 5",Vector)=(0,0,1,1) [HideInInspector]_Art6("Art 6",Vector)=(0,0,1,1) [HideInInspector]_Art7("Art 7",Vector)=(0,0,1,1) [HideInInspector]_Art8("Art 8",Vector)=(0,0,1,1) [HideInInspector]_Art9("Art 9",Vector)=(0,0,1,1) [HideInInspector]_Art10("Art 10",Vector)=(0,0,1,1) [HideInInspector]_Art11("Art 11",Vector)=(0,0,1,1) [HideInInspector]_Art12("Art 12",Vector)=(0,0,1,1) [HideInInspector]_Art13("Art 13",Vector)=(0,0,1,1)}
SubShader{Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"} Cull Back ZWrite On
HLSLINCLUDE
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);TEXTURE2D(_Artwork);SAMPLER(sampler_Artwork);
CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST,_BaseColor;
float4 _Art0,_Art1,_Art2,_Art3,_Art4,_Art5,_Art6,_Art7,_Art8,_Art9,_Art10,_Art11,_Art12,_Art13;
CBUFFER_END
struct A{float4 p:POSITION;float3 n:NORMAL;float2 uv:TEXCOORD0;float4 art:TEXCOORD2;float2 fit:TEXCOORD3;UNITY_VERTEX_INPUT_INSTANCE_ID};
struct V{float4 p:SV_POSITION;float3 n:TEXCOORD0;float2 uv:TEXCOORD1;float4 art:TEXCOORD2;float3 world:TEXCOORD3;float fog:TEXCOORD4;float2 fit:TEXCOORD5;UNITY_VERTEX_INPUT_INSTANCE_ID};
V Vert(A i){V o;UNITY_SETUP_INSTANCE_ID(i);UNITY_TRANSFER_INSTANCE_ID(i,o);o.world=TransformObjectToWorld(i.p.xyz);o.p=TransformWorldToHClip(o.world);o.n=TransformObjectToWorldNormal(i.n);o.uv=TRANSFORM_TEX(i.uv,_BaseMap);o.art=i.art;o.fit=i.fit;o.fog=ComputeFogFactor(o.p.z);return o;}
float4 Rect(float id){if(id<.5)return _Art0;if(id<1.5)return _Art1;if(id<2.5)return _Art2;if(id<3.5)return _Art3;if(id<4.5)return _Art4;if(id<5.5)return _Art5;if(id<6.5)return _Art6;if(id<7.5)return _Art7;if(id<8.5)return _Art8;if(id<9.5)return _Art9;if(id<10.5)return _Art10;if(id<11.5)return _Art11;if(id<12.5)return _Art12;return _Art13;}
half4 Frag(V i):SV_Target{
UNITY_SETUP_INSTANCE_ID(i);
half3 concrete=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,float2(.252,.752)+frac(i.uv)*.246).rgb*_BaseColor.rgb;
concrete=lerp(_BaseColor.rgb,concrete,saturate(i.art.z));half3 color=concrete;
if(i.art.z>.001){
float slot=floor(i.art.x);float id=fmod(slot+i.art.w,14);float4 rect=Rect(id);
float artworkHeight=max(.2,i.fit.x);float width=min(10.7,rect.z/rect.w*artworkHeight);
float2 q=float2((frac(i.art.x)-.5)*12/width+.5,i.art.y/artworkHeight);
float2 safe=saturate(q);half3 art=SAMPLE_TEXTURE2D(_Artwork,sampler_Artwork,rect.xy+safe*rect.zw).rgb;
float noise=frac(sin(dot(floor(i.world.xz*17+i.world.y*23),float2(12.9898,78.233)))*43758.5453);
float edge=min(min(q.x,1-q.x),min(q.y,1-q.y));
float fade=smoothstep(.003,.035,edge+(noise-.5)*.012);
float grain=dot(concrete,half3(.299,.587,.114));
float ink=smoothstep(.018,.15,max(art.r,max(art.g,art.b)));
float weather=lerp(.91,1,smoothstep(.12,.45,noise));
float mask=fade*saturate(i.art.z)*lerp(.38,.98,ink)*weather;
art*=lerp(.88,1.12,saturate(grain*1.4));
color=lerp(concrete,art,mask);
}
half3 n=normalize(i.n);Light light=GetMainLight(TransformWorldToShadowCoord(i.world));
half3 illumination=SampleSH(n)+light.color*saturate(dot(n,light.direction))*light.shadowAttenuation*light.distanceAttenuation;
color*=max(.16,illumination);
return half4(MixFog(color,i.fog),1);
}
half4 Depth(V i):SV_Target{return 0;}half4 Norm(V i):SV_Target{return half4(normalize(i.n),0);}
ENDHLSL
Pass{Name "Forward" Tags{"LightMode"="UniversalForward"}
HLSLPROGRAM
#pragma target 3.0
#pragma vertex Vert
#pragma fragment Frag
#pragma multi_compile_fog
#pragma multi_compile_instancing
#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
#pragma multi_compile_fragment _ _SHADOWS_SOFT
ENDHLSL}
Pass{Name "DepthOnly" Tags{"LightMode"="DepthOnly"} ColorMask 0
HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Depth
#pragma multi_compile_instancing
ENDHLSL}
Pass{Name "DepthNormals" Tags{"LightMode"="DepthNormals"}
HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Norm
#pragma multi_compile_instancing
ENDHLSL}
UsePass "Universal Render Pipeline/Lit/ShadowCaster"
}}