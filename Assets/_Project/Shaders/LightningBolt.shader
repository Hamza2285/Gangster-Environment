// A lightning bolt far away in the sky (WeatherManager): an additive camera-facing quad with a generated bolt texture,
// shown for a fraction of a second. Depth tested, so buildings in front hide it.
Shader "OpenworldCrime/LightningBolt"
{
    Properties
    {
        _MainTex ("Bolt", 2D) = "black" {}
        _Color ("Colour", Color) = (0.85, 0.9, 1, 1)
        _Intensity ("Intensity (set at runtime)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-440" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            Name "Bolt"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes v) { Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.uv = v.uv; return o; }
            half4 frag(Varyings i) : SV_Target { return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).r * _Color * _Intensity; }
            ENDHLSL
        }
    }
}
