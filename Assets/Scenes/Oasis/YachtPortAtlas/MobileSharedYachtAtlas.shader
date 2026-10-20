Shader "City/Mobile Shared Yacht Port Atlas" {
Properties{_Atlas("Shared port atlas",2D)="white"{} _Tint("Tint",Color)=(1,1,1,1)}
SubShader{Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"} Cull Back ZWrite On
HLSLINCLUDE
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
TEXTURE2D(_Atlas);SAMPLER(sampler_Atlas);
CBUFFER_START(UnityPerMaterial)
half4 _Tint;
CBUFFER_END
struct A{float4 p:POSITION;float3 n:NORMAL;float2 uv:TEXCOORD0;float4 tile:TEXCOORD1;half4 color:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID};
struct V{float4 p:SV_POSITION;float3 n:TEXCOORD0;float2 uv:TEXCOORD1;float4 tile:TEXCOORD2;half4 color:COLOR;float3 world:TEXCOORD3;float fog:TEXCOORD4;UNITY_VERTEX_INPUT_INSTANCE_ID};
float3 _LightDirection;
V Vert(A i){V o;UNITY_SETUP_INSTANCE_ID(i);UNITY_TRANSFER_INSTANCE_ID(i,o);o.world=TransformObjectToWorld(i.p.xyz);o.p=TransformWorldToHClip(o.world);o.n=TransformObjectToWorldNormal(i.n);o.uv=i.uv;o.tile=i.tile;o.color=i.color;o.fog=ComputeFogFactor(o.p.z);return o;}
V ShadowVert(A i){V o=Vert(i);o.p=TransformWorldToHClip(ApplyShadowBias(o.world,normalize(o.n),_LightDirection));
#if UNITY_REVERSED_Z
o.p.z=min(o.p.z,UNITY_NEAR_CLIP_VALUE*o.p.w);
#else
o.p.z=max(o.p.z,UNITY_NEAR_CLIP_VALUE*o.p.w);
#endif
return o;}
half4 Frag(V i):SV_Target{UNITY_SETUP_INSTANCE_ID(i);half3 albedo=i.color.rgb;float tile=round(i.tile.x);if(i.tile.z>.5&&tile==0)tile=1;if(tile>=0){float2 local=lerp(.018,.982,(i.tile.w>.5?saturate(i.uv):frac(i.uv)));if(tile==8)local=clamp(frac(i.uv),.001,.999);float2 atlasUV=(float2(fmod(tile,4),2-floor(tile/4))+local)*float2(.25,.333333);albedo*=SAMPLE_TEXTURE2D_GRAD(_Atlas,sampler_Atlas,atlasUV,ddx(i.uv)*float2(.241,.321333),ddy(i.uv)*float2(.241,.321333)).rgb;}half3 n=normalize(i.n);Light light=GetMainLight(TransformWorldToShadowCoord(i.world));half3 lit=albedo*_Tint.rgb*(max(.22,SampleSH(n))+saturate(dot(n,light.direction))*light.color*light.shadowAttenuation);if(tile==7){half fresnel=pow(1-saturate(dot(n,normalize(GetWorldSpaceViewDir(i.world)))),3);lit=lerp(lit,half3(.27,.40,.46),.18+fresnel*.35);}lit+=albedo*i.tile.y;return half4(MixFog(lit,i.fog),1);}half4 Depth(V i):SV_Target{return 0;}half4 Norm(V i):SV_Target{return half4(normalize(i.n),0);}
ENDHLSL
Pass{Name "Forward" Tags{"LightMode"="UniversalForward"}
HLSLPROGRAM
#pragma target 3.0
#pragma vertex Vert
#pragma fragment Frag
#pragma multi_compile_instancing
#pragma multi_compile_fog
#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
#pragma multi_compile_fragment _ _SHADOWS_SOFT
ENDHLSL
}
Pass{Name "DepthOnly" Tags{"LightMode"="DepthOnly"} ColorMask 0
HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Depth
#pragma multi_compile_instancing
ENDHLSL
}
Pass{Name "DepthNormals" Tags{"LightMode"="DepthNormals"}
HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Norm
#pragma multi_compile_instancing
ENDHLSL
}
Pass{Name "ShadowCaster" Tags{"LightMode"="ShadowCaster"} ColorMask 0
HLSLPROGRAM
#pragma vertex ShadowVert
#pragma fragment Depth
#pragma multi_compile_instancing
ENDHLSL
}
}}

