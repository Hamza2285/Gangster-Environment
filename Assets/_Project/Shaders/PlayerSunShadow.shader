// The played character's sun shadow on the ground (see PlayerSunShadow.cs): the sun's-eye mask of the player, laid on the
// ground along the sunlight, multiplied over what is already drawn. Soft: a Poisson blur (on the mask's mips) that widens
// with the distance from the feet, so the contact is crisp and the head's shadow is soft, like a real penumbra.
Shader "OpenworldCrime/PlayerSunShadow"
{
    Properties
    {
        [NoScaleOffset] _ShadowMask ("Mask (set at runtime)", 2D) = "black" {}
        _ShadowColor ("Shadow colour (ground in full shade)", Color) = (0.42, 0.44, 0.56, 1)
        _SoftNear ("Blur at the feet (fraction of the frame)", Range(0, 0.05)) = 0.004
        _SoftFar ("Blur far from the feet", Range(0, 0.1)) = 0.03
        _FarFade ("Lighter far from the feet", Range(0, 1)) = 0.3
        _Strength ("Strength (set at runtime)", Range(0, 1)) = 0
        _FeetUV ("Feet in the mask (set at runtime)", Vector) = (0.5, 0.2, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-100" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend DstColor Zero
        ColorMask RGB
        ZWrite Off
        ZTest LEqual
        Cull Off
        Offset -1, -1

        Pass
        {
            Name "PlayerSunShadow"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_ShadowMask); SAMPLER(sampler_ShadowMask);
            float4 _ShadowMask_TexelSize;
            CBUFFER_START(UnityPerMaterial)
                half4 _ShadowColor;
                half _SoftNear;
                half _SoftFar;
                half _FarFade;
                half _Strength;
                float4 _FeetUV;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                return o;
            }

            static const float2 Taps[8] =
            {
                float2(-0.613, 0.617), float2(0.170, -0.040), float2(-0.299, -0.792), float2(0.645, 0.493),
                float2(-0.651, -0.072), float2(0.421, -0.718), float2(-0.054, 0.986), float2(0.958, -0.211)
            };

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv;
                half far = saturate(distance(uv, _FeetUV.xy) / 0.75);
                float radius = lerp(_SoftNear, _SoftFar, far);
                float lod = max(0, log2(radius * _ShadowMask_TexelSize.z) - 1.5);
                half m = SAMPLE_TEXTURE2D_LOD(_ShadowMask, sampler_ShadowMask, uv, lod).a;
                [unroll] for (int k = 0; k < 8; k++)
                    m += SAMPLE_TEXTURE2D_LOD(_ShadowMask, sampler_ShadowMask, uv + Taps[k] * radius, lod).a;
                m /= 9.0;
                float2 edge = saturate(min(uv, 1.0 - uv) * 25.0);
                half a = m * _Strength * (1.0 - _FarFade * far) * edge.x * edge.y;
                return half4(lerp(half3(1, 1, 1), _ShadowColor.rgb, a), 1);
            }
            ENDHLSL
        }
    }
}
