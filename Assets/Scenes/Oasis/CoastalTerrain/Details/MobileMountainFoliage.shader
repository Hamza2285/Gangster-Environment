Shader "City/Mobile Mountain Foliage" {
Properties {
_Atlas("Foliage atlas",2D)="white"{} _Tint("Tint",Color)=(1,1,1,1) _Cutoff("Alpha cutoff",Range(0,1))=.35
_BaseMap("Shadow map",2D)="white"{} _BaseColor("Shadow tint",Color)=(1,1,1,1) _Cull("Cull",Float)=0
_WindStrength("Gentle sway metres",Float)=.16 _WindSpeed("Wind speed",Float)=1.1 _PreviewTime("Preview time; negative is live",Float)=-1
}
SubShader { Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest"} Cull Off ZWrite On
HLSLINCLUDE
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
TEXTURE2D(_Atlas); SAMPLER(sampler_Atlas);
CBUFFER_START(UnityPerMaterial)
half4 _Tint; half _Cutoff; float4 _BaseMap_ST; half4 _BaseColor; float _Cull; float _WindStrength; float _WindSpeed; float _PreviewTime;
CBUFFER_END
float3 _LightDirection;
struct A {float4 p:POSITION; float3 n:NORMAL; float2 uv:TEXCOORD0; float4 wind:TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID};
struct V {float4 p:SV_POSITION; float3 n:TEXCOORD0; float2 uv:TEXCOORD1; float fog:TEXCOORD2; float3 world:TEXCOORD3; UNITY_VERTEX_INPUT_INSTANCE_ID};
float3 WindPosition(A i) {
float3 p=TransformObjectToWorld(i.p.xyz);
float3 anchor=TransformObjectToWorld(float3(i.wind.y,0,i.wind.z));
float phase=dot(anchor.xz,float2(.137,.219));
float t=(_PreviewTime<0?_Time.y:_PreviewTime)*_WindSpeed;
float weight=i.wind.x*i.wind.x;
float sway=sin(t+phase)+.23*sin(t*2.17+phase*1.7);
float flutter=sin(t*3.1+phase+i.wind.x*3)*.12;
p.xz+=float2(.88,.47)*((sway+flutter)*weight*_WindStrength);
return p;
}
V Vert(A i) {V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i,o); o.world=WindPosition(i); o.p=TransformWorldToHClip(o.world); o.n=TransformObjectToWorldNormal(i.n); o.uv=i.uv; o.fog=ComputeFogFactor(o.p.z); return o;}
V ShadowVert(A i) {V o=Vert(i); o.p=TransformWorldToHClip(ApplyShadowBias(o.world,normalize(o.n),_LightDirection));
#if UNITY_REVERSED_Z
o.p.z=min(o.p.z,UNITY_NEAR_CLIP_VALUE*o.p.w);
#else
o.p.z=max(o.p.z,UNITY_NEAR_CLIP_VALUE*o.p.w);
#endif
return o;}
half4 Sample(V i) {half4 c=SAMPLE_TEXTURE2D(_Atlas,sampler_Atlas,i.uv);clip(c.a-_Cutoff);return c;}
half4 Frag(V i):SV_Target {UNITY_SETUP_INSTANCE_ID(i);half4 c=Sample(i);half3 n=normalize(i.n);Light l=GetMainLight(TransformWorldToShadowCoord(i.world));half wrapped=.3+.7*abs(dot(n,l.direction));half3 light=max(.32,SampleSH(n)+wrapped*l.color*lerp(.35,1,l.shadowAttenuation));return half4(MixFog(c.rgb*_Tint.rgb*light,i.fog),1);}
half4 Depth(V i):SV_Target {Sample(i);return 0;}
half4 Norm(V i):SV_Target {Sample(i);return half4(normalize(i.n),0);}
ENDHLSL
Pass {Name "Forward" Tags {"LightMode"="UniversalForward"}
HLSLPROGRAM
#pragma target 3.0
#pragma vertex Vert
#pragma fragment Frag
#pragma multi_compile_fog
#pragma multi_compile_instancing
#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
#pragma multi_compile_fragment _ _SHADOWS_SOFT
ENDHLSL
}
Pass {Name "DepthOnly" Tags {"LightMode"="DepthOnly"} ColorMask 0
HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Depth
#pragma multi_compile_instancing
ENDHLSL
}
Pass {Name "DepthNormals" Tags {"LightMode"="DepthNormals"}
HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Norm
#pragma multi_compile_instancing
ENDHLSL
}
Pass {Name "ShadowCaster" Tags {"LightMode"="ShadowCaster"} ColorMask 0
HLSLPROGRAM
#pragma vertex ShadowVert
#pragma fragment Depth
#pragma multi_compile_instancing
ENDHLSL
}
}}
