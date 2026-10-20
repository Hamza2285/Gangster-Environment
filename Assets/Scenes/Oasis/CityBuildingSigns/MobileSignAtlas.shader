Shader "City/Mobile Shared Sign Atlas"{
Properties{_Atlas("Single billboard atlas",2D)="black"{} _NightGlow("Night glow",Range(0,3))=1.7}
SubShader{Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+5"} Cull Off ZWrite On
HLSLINCLUDE
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
TEXTURE2D(_Atlas);SAMPLER(sampler_Atlas);
CBUFFER_START(UnityPerMaterial)
float _NightGlow;
CBUFFER_END
float _GangstarNight;
struct A{float4 p:POSITION;float3 n:NORMAL;float2 uv:TEXCOORD0;float4 color:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID};
struct V{float4 p:SV_POSITION;float3 n:TEXCOORD0;float2 uv:TEXCOORD1;float4 color:COLOR;float fog:TEXCOORD2;UNITY_VERTEX_INPUT_INSTANCE_ID};
V Vert(A i){V o;UNITY_SETUP_INSTANCE_ID(i);UNITY_TRANSFER_INSTANCE_ID(i,o);o.p=TransformObjectToHClip(i.p.xyz);o.n=TransformObjectToWorldNormal(i.n);o.uv=i.uv;o.color=i.color;o.fog=ComputeFogFactor(o.p.z);return o;}
void WallArtClip(half3 c){half peak=max(c.r,max(c.g,c.b));half low=min(c.r,min(c.g,c.b));clip(peak-.30h);clip(max(peak-low-.10h,peak-.72h));}
half4 Frag(V i):SV_Target{UNITY_SETUP_INSTANCE_ID(i);half3 c;if(i.color.a<-.5){c=SAMPLE_TEXTURE2D(_Atlas,sampler_Atlas,i.uv).rgb;WallArtClip(c);c*=i.color.rgb*(1.05+saturate(_GangstarNight)*_NightGlow*.80);}else if(i.color.a<.25){Light l=GetMainLight();c=i.color.rgb*max(.30,SampleSH(normalize(i.n))+.5*abs(dot(normalize(i.n),l.direction))*l.color);}else if(i.color.a>.75)c=i.color.rgb*(1+saturate(_GangstarNight)*_NightGlow);else{c=SAMPLE_TEXTURE2D(_Atlas,sampler_Atlas,i.uv).rgb*i.color.rgb;c*=.94+saturate(_GangstarNight)*_NightGlow*.65;}return half4(MixFog(c,i.fog),1);}
half4 Depth(V i):SV_Target{if(i.color.a<-.5)WallArtClip(SAMPLE_TEXTURE2D(_Atlas,sampler_Atlas,i.uv).rgb);return 0;} half4 Norm(V i):SV_Target{if(i.color.a<-.5)WallArtClip(SAMPLE_TEXTURE2D(_Atlas,sampler_Atlas,i.uv).rgb);return half4(normalize(i.n),0);}
ENDHLSL
Pass{Name "Forward" Tags{"LightMode"="UniversalForward"}
HLSLPROGRAM
#pragma target 3.0
#pragma vertex Vert
#pragma fragment Frag
#pragma multi_compile_fog
#pragma multi_compile_instancing
ENDHLSL
}
Pass{Name "DepthOnly" Tags{"LightMode"="DepthOnly"} ColorMask 0
HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Depth
#pragma multi_compile_instancing
ENDHLSL
}
Pass{Name "DepthNormals" Tags{"LightMode"="DepthNormals"} ZWrite On HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Norm
#pragma multi_compile_instancing
ENDHLSL
}
}}