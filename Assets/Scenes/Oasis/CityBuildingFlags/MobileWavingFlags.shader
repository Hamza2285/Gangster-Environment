Shader "City/Mobile Atlas Waving Flags"{
Properties{_Atlas("Building atlas",2D)="white"{} _Wind("Wind strength",Range(0,1))=.24 [HideInInspector]_PreviewTime("Preview time",Float)=-1}
SubShader{Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+6"} Cull Off ZWrite On
HLSLINCLUDE
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
TEXTURE2D(_Atlas);SAMPLER(sampler_Atlas);
CBUFFER_START(UnityPerMaterial)
float _Wind,_PreviewTime;
CBUFFER_END
struct A{float4 p:POSITION;float3 n:NORMAL;float2 uv:TEXCOORD0;float4 c:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID};
struct V{float4 p:SV_POSITION;float3 n:TEXCOORD0;float2 uv:TEXCOORD1;float4 c:COLOR;float3 world:TEXCOORD2;float fog:TEXCOORD3;UNITY_VERTEX_INPUT_INSTANCE_ID};
V Vert(A i){V o;UNITY_SETUP_INSTANCE_ID(i);UNITY_TRANSFER_INSTANCE_ID(i,o);float3 p=i.p.xyz;float3 n=i.n;if(i.c.a>.5){float phase=dot(GetObjectToWorldMatrix()._m03_m23,float2(.13,.09))+i.c.g*9;float time=_PreviewTime>=0?_PreviewTime:_Time.y;float wave=time*2.1+phase+i.c.b*4+p.y*2.4;float bend=sin(wave)*.78+sin(wave*1.73+p.z*3.1)*.22;p.x+=bend*_Wind*i.c.r;p.z+=sin(wave*.83)*_Wind*.25*i.c.r;n=normalize(n+float3(0,cos(wave)*_Wind*i.c.r*.5,cos(wave)*_Wind*i.c.r));}o.world=TransformObjectToWorld(p);o.p=TransformWorldToHClip(o.world);o.n=TransformObjectToWorldNormal(n);o.uv=i.uv;o.c=i.c;o.fog=ComputeFogFactor(o.p.z);return o;}
half4 Frag(V i):SV_Target{UNITY_SETUP_INSTANCE_ID(i);half3 n=normalize(i.n);Light light=GetMainLight(TransformWorldToShadowCoord(i.world));half3 lightColor=max(.25,SampleSH(n)+light.color*(.25+.65*abs(dot(n,light.direction)))*light.shadowAttenuation);half3 albedo=i.c.a>.5?SAMPLE_TEXTURE2D(_Atlas,sampler_Atlas,i.uv).rgb:i.c.rgb;return half4(MixFog(albedo*lightColor,i.fog),1);}
half4 Depth(V i):SV_Target{return 0;}half4 Norm(V i):SV_Target{return half4(normalize(i.n),0);}
ENDHLSL
Pass{Name "Forward" Tags{"LightMode"="UniversalForward"} HLSLPROGRAM
#pragma target 3.0
#pragma vertex Vert
#pragma fragment Frag
#pragma multi_compile_instancing
#pragma multi_compile_fog
#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
ENDHLSL}
Pass{Name "DepthOnly" Tags{"LightMode"="DepthOnly"} ColorMask 0 HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Depth
#pragma multi_compile_instancing
ENDHLSL}
Pass{Name "DepthNormals" Tags{"LightMode"="DepthNormals"} HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Norm
#pragma multi_compile_instancing
ENDHLSL}
}}