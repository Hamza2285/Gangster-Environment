Shader "Gangstar City/Bridge Night Lit" {
Properties { [MainTexture] _BaseMap("Existing Bridge Texture",2D)="white"{} [MainColor] _BaseColor("Tint",Color)=(1,1,1,1) }
SubShader { Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" } Cull Off ZWrite On
HLSLINCLUDE
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST;half4 _BaseColor;
CBUFFER_END
half _GangstarNight;
struct A {float4 p:POSITION;float3 n:NORMAL;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
struct V {float4 p:SV_POSITION;half3 color:TEXCOORD0;half fog:TEXCOORD1;half3 n:TEXCOORD2;UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO};
V Vert(A a){V o=(V)0;UNITY_SETUP_INSTANCE_ID(a);UNITY_TRANSFER_INSTANCE_ID(a,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.p=TransformObjectToHClip(a.p.xyz);o.n=TransformObjectToWorldNormal(a.n);o.color=a.uv.x<65?half3(.025,.45,1):(a.uv.x<66?half3(1,.22,.025):(a.uv.x<67?half3(.45,.70,1):half3(1,.055,.45)));o.fog=ComputeFogFactor(o.p.z);return o;}
half4 Frag(V i):SV_Target {UNITY_SETUP_INSTANCE_ID(i);UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);return half4(MixFog(i.color*(3.2h*saturate(_GangstarNight)),i.fog),1);}
half4 Depth(V i):SV_Target{return 0;}
half4 Norm(V i):SV_Target{return half4(normalize(i.n),0);}
ENDHLSL
Pass { Name "MobileLED" Tags { "LightMode"="UniversalForward" }
HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Frag
#pragma target 3.0
#pragma multi_compile_fog
#pragma multi_compile_instancing
ENDHLSL
}
Pass { Name "DepthOnly" Tags { "LightMode"="DepthOnly" } ColorMask 0
HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Depth
#pragma multi_compile_instancing
ENDHLSL
}
Pass { Name "DepthNormals" Tags { "LightMode"="DepthNormals" }
HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Norm
#pragma multi_compile_instancing
ENDHLSL
}
}
}





