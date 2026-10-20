Shader "City/Mobile Gangster Neon Decals" {
Properties { _Atlas("36-design shared atlas",2D)="white"{} _NightGlow("Night glow",Range(0,4))=1.7 _Cutoff("Alpha cutoff",Range(0,1))=.16 }
SubShader { Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest+5" } Cull Back ZWrite On
HLSLINCLUDE
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
TEXTURE2D(_Atlas); SAMPLER(sampler_Atlas);
CBUFFER_START(UnityPerMaterial)
float _NightGlow;float _Cutoff;
CBUFFER_END
float _GangstarNight;
struct A { float4 p:POSITION;float3 n:NORMAL;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID };
struct V { float4 p:SV_POSITION;float2 uv:TEXCOORD0;float3 n:TEXCOORD1;float fog:TEXCOORD2;UNITY_VERTEX_INPUT_INSTANCE_ID };
V Vert(A i){V o;UNITY_SETUP_INSTANCE_ID(i);UNITY_TRANSFER_INSTANCE_ID(i,o);o.p=TransformObjectToHClip(i.p.xyz);o.uv=i.uv;o.n=TransformObjectToWorldNormal(i.n);o.fog=ComputeFogFactor(o.p.z);return o;}
half4 SampleArt(V i){half4 c=SAMPLE_TEXTURE2D(_Atlas,sampler_Atlas,i.uv);clip(c.a-_Cutoff);return c;}
half4 Frag(V i):SV_Target {UNITY_SETUP_INSTANCE_ID(i);half4 c=SampleArt(i);half3 rgb=c.rgb*(1.05+saturate(_GangstarNight)*_NightGlow);return half4(MixFog(rgb,i.fog),1);}
half4 Depth(V i):SV_Target{UNITY_SETUP_INSTANCE_ID(i);SampleArt(i);return 0;}
half4 Norm(V i):SV_Target{UNITY_SETUP_INSTANCE_ID(i);SampleArt(i);return half4(normalize(i.n),0);}
ENDHLSL
Pass { Name "Forward" Tags{"LightMode"="UniversalForward"}
HLSLPROGRAM
#pragma target 3.0
#pragma vertex Vert
#pragma fragment Frag
#pragma multi_compile_instancing
#pragma multi_compile_fog
ENDHLSL
}
Pass { Name "DepthOnly" Tags{"LightMode"="DepthOnly"} ColorMask 0
HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Depth
#pragma multi_compile_instancing
ENDHLSL
}
Pass { Name "DepthNormals" Tags{"LightMode"="DepthNormals"}
HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Norm
#pragma multi_compile_instancing
ENDHLSL
}
} }
