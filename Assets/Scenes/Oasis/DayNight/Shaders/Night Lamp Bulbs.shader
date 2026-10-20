Shader "Gangstar City/Night Lamp Bulbs" {
 Properties {[HDR] _Color("Light Color",Color)=(8,6.3,4.2,1)}
 SubShader {
  Tags {"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline"}
  Pass {
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
   half4 _Color;
   CBUFFER_END
   struct A{float4 positionOS:POSITION;};struct V{float4 positionCS:SV_POSITION;};
   V vert(A i){V o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);return o;}
   half4 frag(V i):SV_Target{return _Color;}
   ENDHLSL
  }
 }
}

