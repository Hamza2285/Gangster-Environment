// City sky through the day: morning, noon, evening and night blended by weights set from SkyCycleController
// (_CycleWeights = morning, noon, evening, night; they sum to 1). Daytime uses the cloud cubemap tinted per phase with a
// horizon glow toward the sun; night is a gradient with moon and stars. One pass, no textures besides the cubemap.
Shader "OpenworldCrime/Sky Cycle"
{
    Properties
    {
        [Header(Daytime clouds)]
        [NoScaleOffset] _DayCube ("Cloud cubemap", Cube) = "grey" {}
        _CubeRotation ("Cubemap rotation", Range(0, 360)) = 127
        _CubeExposure ("Cubemap exposure", Float) = 0.95

        [Header(Morning)]
        _MorningTint ("Morning tint", Color) = (0.95, 0.78, 0.78, 1)
        _MorningZenith ("Morning zenith", Color) = (0.33, 0.45, 0.70, 1)
        _MorningGlow ("Morning horizon glow", Color) = (1.0, 0.55, 0.35, 1)

        [Header(Noon)]
        _NoonTint ("Noon tint", Color) = (0.80, 0.98, 1.25, 1)
        _NoonZenith ("Noon zenith", Color) = (0.22, 0.45, 0.85, 1)
        _NoonGlow ("Noon horizon haze", Color) = (0.75, 0.85, 1.0, 1)

        [Header(Evening)]
        _EveningTint ("Evening tint", Color) = (1.05, 0.72, 0.62, 1)
        _EveningZenith ("Evening zenith", Color) = (0.25, 0.20, 0.42, 1)
        _EveningGlow ("Evening horizon glow", Color) = (1.0, 0.42, 0.20, 1)

        [Header(Night)]
        _NightHorizon ("Night horizon", Color) = (0.14, 0.19, 0.28, 1)
        _NightZenith ("Night zenith", Color) = (0.05, 0.085, 0.17, 1)
        _MoonColor ("Moon glow", Color) = (3.3, 3.3, 3.3, 1)
        _StarBrightness ("Star brightness", Range(0, 1)) = 0.2

        [Header(Sun)]
        _SunColor ("Sun disc", Color) = (4, 3.4, 2.6, 1)
        _SunSize ("Sun size", Range(0.995, 0.9999)) = 0.9993
        _CloudMix ("Cloud vs gradient (day)", Range(0, 1)) = 0.8

        // Set at runtime by SkyCycleController.
        [HideInInspector] _CycleWeights ("Weights", Vector) = (0, 1, 0, 0)
        [HideInInspector] _SunDirection ("Sun direction", Vector) = (0, 0.7, 0.7, 0)
        [HideInInspector] _MoonDirection ("Moon direction", Vector) = (0.3, 0.5, 0.8, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DayCube_HDR;
                float _CubeRotation, _CubeExposure;
                half4 _MorningTint, _MorningZenith, _MorningGlow;
                half4 _NoonTint, _NoonZenith, _NoonGlow;
                half4 _EveningTint, _EveningZenith, _EveningGlow;
                half4 _NightHorizon, _NightZenith, _MoonColor;
                half _StarBrightness;
                half4 _SunColor;
                float _SunSize;
                half _CloudMix;
                float4 _CycleWeights, _SunDirection, _MoonDirection;
            CBUFFER_END
            TEXTURECUBE(_DayCube); SAMPLER(sampler_DayCube);

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.direction = i.positionOS.xyz;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.direction);
                float4 w = _CycleWeights;
                float dayWeight = saturate(w.x + w.y + w.z);
                float up = saturate(d.y);

                // Daytime: tinted cloud cubemap over a phase gradient, glow on the horizon toward the sun.
                float angle = -radians(_CubeRotation); float sn, cs; sincos(angle, sn, cs);
                float3 rd = float3(cs * d.x - sn * d.z, d.y, sn * d.x + cs * d.z);
                half3 cube = DecodeHDREnvironment(SAMPLE_TEXTURECUBE(_DayCube, sampler_DayCube, rd), _DayCube_HDR) * _CubeExposure;
                #ifdef UNITY_COLORSPACE_GAMMA
                    cube *= 2;
                #else
                    cube *= 4.5947938;
                #endif
                half3 tint = (_MorningTint.rgb * w.x + _NoonTint.rgb * w.y + _EveningTint.rgb * w.z) / max(dayWeight, 1e-4);
                half3 zenith = (_MorningZenith.rgb * w.x + _NoonZenith.rgb * w.y + _EveningZenith.rgb * w.z) / max(dayWeight, 1e-4);
                half3 glowColor = (_MorningGlow.rgb * w.x + _NoonGlow.rgb * w.y * 0.35 + _EveningGlow.rgb * w.z) / max(dayWeight, 1e-4);
                half3 gradient = lerp(glowColor, zenith, smoothstep(0.0, 0.55, up));
                half3 day = lerp(gradient, cube * tint, _CloudMix);

                float3 sun = normalize(_SunDirection.xyz);
                float towardSun = pow(saturate(dot(normalize(d.xz + float2(1e-4, 0)), normalize(sun.xz + float2(1e-4, 0)))), 6);
                float horizonBand = exp2(-abs(d.y - max(sun.y, 0.0)) * 9);
                float lowSun = 1 - saturate(sun.y * 2.5); // the glow is strongest while the sun is low
                day += glowColor * horizonBand * (0.25 + towardSun * 0.75) * (0.35 + lowSun * 0.65) * 0.6;
                float sunEdge = max(fwidth(dot(d, sun)), 1e-5);
                float disc = smoothstep(_SunSize - sunEdge, _SunSize + sunEdge, dot(d, sun));
                day += _SunColor.rgb * disc * smoothstep(-0.03, 0.03, d.y);

                // Night: gradient, moon, stars.
                half3 night = lerp(_NightHorizon.rgb, _NightZenith.rgb, saturate(d.y * 1.7));
                float m = dot(d, normalize(_MoonDirection.xyz));
                float moonEdge = max(fwidth(m), 8e-6);
                night += _MoonColor.rgb * (smoothstep(0.99965 - moonEdge, 0.99965 + moonEdge, m) + pow(saturate(m), 180) * 0.025);
                float3 cell = floor(d * 450);
                float star = frac(sin(dot(cell, float3(12.9898, 78.233, 45.164))) * 43758.5453);
                night += step(0.9991, star) * smoothstep(0.05, 0.35, d.y) * _StarBrightness;

                half3 sky = day * dayWeight + night * saturate(w.w);
                return half4(sky, 1);
            }
            ENDHLSL
        }
    }
}
