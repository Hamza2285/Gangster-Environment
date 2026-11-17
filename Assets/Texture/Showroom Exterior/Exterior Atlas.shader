Shader "Showroom/Exterior Atlas"
{
 Properties {
  _BaseMap ("Exterior atlas", 2D) = "white" {}
  _EmissionMap ("Exterior emission mask", 2D) = "black" {}
  _Cutoff ("Cutout threshold", Range(0,1)) = 0.5
 }
 SubShader {
  Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
  Cull Off
  ZWrite On
  Pass {
   Name "Exterior"
   Tags { "LightMode"="UniversalForward" }
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
   TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
   CBUFFER_START(UnityPerMaterial)
   float _Cutoff;
   CBUFFER_END
   struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
   struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
   Varyings Vert(Attributes input) {
      Varyings output;
      output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
      output.uv = input.uv;
      return output;
   }
   half4 Frag(Varyings input) : SV_Target {
      half4 color = SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv);
      clip(color.a - _Cutoff);
      half3 glow = SAMPLE_TEXTURE2D(_EmissionMap,sampler_EmissionMap,input.uv).rgb;
      return half4(color.rgb + glow*.55h,1);
   }
   ENDHLSL
  }
 }
}
