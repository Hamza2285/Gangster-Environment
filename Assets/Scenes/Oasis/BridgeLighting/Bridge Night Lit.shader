Shader "Gangstar City/Bridge Night Lit" {
Properties { [MainTexture] _BaseMap("Existing Bridge Texture",2D)="white"{} [MainColor] _BaseColor("Tint",Color)=(1,1,1,1) }
SubShader { Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
Pass { Name "MobileLED" Tags { "LightMode"="SRPDefaultUnlit" } Cull Off ZWrite On
HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Frag
#pragma target 2.0
#pragma multi_compile_fog
#pragma multi_compile_instancing
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST;half4 _BaseColor;
CBUFFER_END
half _GangstarNight;
struct A {float4 p:POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
struct V {float4 p:SV_POSITION;half3 color:TEXCOORD0;half fog:TEXCOORD1;UNITY_VERTEX_OUTPUT_STEREO};
V Vert(A a){V o=(V)0;UNITY_SETUP_INSTANCE_ID(a);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.p=TransformObjectToHClip(a.p.xyz);o.color=a.uv.x<65?half3(.005,.075,1):(a.uv.x<66?half3(1,.19,.012):half3(.25,.55,1));o.fog=ComputeFogFactor(o.p.z);return o;}
half4 Frag(V i):SV_Target {UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);return half4(MixFog(i.color*lerp(.12h,3.2h,saturate(_GangstarNight)),i.fog),1);}
ENDHLSL
}
}
}
