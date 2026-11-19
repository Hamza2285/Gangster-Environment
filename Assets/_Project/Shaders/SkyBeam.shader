// Searchlight beam into the night sky (rooftop searchlights). Additive, double-sided, no depth texture needed:
// bright along the beam's core (faces looking at the camera) and soft at its silhouette, fading out along its length and
// right at the lamp. Per-renderer brightness comes from _Intensity (MaterialPropertyBlock), so beams fade in and out at
// dusk and dawn without touching the material asset.
Shader "OpenworldCrime/SkyBeam"
{
    Properties
    {
        _Color ("Color", Color) = (0.78, 0.84, 1, 1)
        _Intensity ("Intensity", Float) = 1
        _LengthFade ("Length fade (power)", Float) = 1.4
        _EdgeSoftness ("Edge softness (power)", Float) = 1.6
        _StartFade ("Fade-in at the lamp (share of length)", Float) = 0.02
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "SkyBeam"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity;
                half _LengthFade;
                half _EdgeSoftness;
                half _StartFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.viewWS = GetWorldSpaceViewDir(p.positionWS);
                o.uv = input.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half facing = saturate(abs(dot(normalize(i.normalWS), normalize(i.viewWS))));
                half edge = pow(facing, _EdgeSoftness);
                half along = pow(saturate(1.0h - i.uv.y), _LengthFade);
                half start = saturate(i.uv.y / max(_StartFade, 0.001h));
                return half4(_Color.rgb * (_Intensity * edge * along * start), 1.0h);
            }
            ENDHLSL
        }
    }
}
