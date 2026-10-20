Shader "City/Mobile Beach Furniture Atlas" {
Properties{_BaseMap("Shared port atlas",2D)="white"{} _Tint("Tint",Color)=(1,1,1,1)}
SubShader{Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"} Cull Back ZWrite On
HLSLINCLUDE
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
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
half4 Frag(V i):SV_Target{UNITY_SETUP_INSTANCE_ID(i);half3 albedo=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb*i.color.rgb*_Tint.rgb;half3 n=normalize(i.n);Light light=GetMainLight(TransformWorldToShadowCoord(i.world));half3 lit=albedo*(max(.22,SampleSH(n))+saturate(dot(n,light.direction))*light.color*light.shadowAttenuation);lit+=light.color*pow(saturate(dot(n,normalize(light.direction+GetWorldSpaceNormalizeViewDir(i.world)))),32)*i.tile.z*.7;if(i.tile.w>.5){half3 view=GetWorldSpaceNormalizeViewDir(i.world);half3 reflected=GlossyEnvironmentReflection(reflect(-view,n),i.world,.24,1);half fresnel=pow(1-saturate(dot(n,view)),5);half highlight=pow(saturate(dot(n,normalize(light.direction+view))),72);lit=albedo*(.20+max(.10,SampleSH(n))*.18)+reflected*lerp(albedo,half3(1,1,1),fresnel)*.85+light.color*highlight*light.shadowAttenuation*1.6;}lit+=albedo*i.tile.y;return half4(MixFog(lit,i.fog),1);}half4 Depth(V i):SV_Target{return 0;}half4 Norm(V i):SV_Target{return half4(normalize(i.n),0);}
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





