// Fake clouds on a camera-following dome (SkyClouds.cs). The view direction is projected onto a flat cloud layer (so clouds
// shrink toward the horizon like a real ceiling of cloud), two scrolling samples of a tileable noise texture make the shapes,
// they fade out toward the horizon, and are lit by a lit / shade colour pair plus a glow toward the sun (or the moon), all
// fed by the component from the time of day. Unlit, no fog, ZWrite off and depth tested after the opaques: anything solid is
// always in front. Cost: two texture samples and a few ALU per sky pixel.
Shader "OpenworldCrime/SkyClouds"
{
    Properties
    {
        _NoiseTex ("Noise (R big shapes, G detail)", 2D) = "gray" {}
        _Scale ("Layer scale", Float) = 0.45
        _DetailScale ("Detail scale", Float) = 2.6
        _Coverage ("Coverage", Range(0, 1)) = 0.5
        _Softness ("Edge softness", Range(0.01, 0.5)) = 0.18
        _Opacity ("Opacity", Range(0, 1)) = 0.9
        _Wind ("Wind (xy big, zw detail) per second", Vector) = (0.004, 0.0015, 0.007, 0.003)
        _HorizonFade ("Horizon fade (start, end elevation)", Vector) = (0.02, 0.28, 0, 0)
        _CloudLit ("Lit colour", Color) = (1, 1, 1, 1)
        _CloudShade ("Shade colour", Color) = (0.65, 0.7, 0.8, 1)
        _SunGlow ("Sun glow colour", Color) = (1, 0.9, 0.75, 1)
        _SunDir ("Sun direction", Vector) = (0, 1, 0, 0)
        _Overcast ("Overcast layer (rain)", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-450" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Skybox" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            Name "Clouds"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
                float _Scale, _DetailScale, _Coverage, _Softness, _Opacity;
                float4 _Wind, _HorizonFade;
                half4 _CloudLit, _CloudShade, _SunGlow;
                float4 _SunDir;
                half _Overcast;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz; // dome centred on the camera, unit sphere scaled: the direction is the position
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float up = d.y;
                half horizon = smoothstep(_HorizonFade.x, _HorizonFade.y, up);
                if (horizon <= 0.001h) return 0;
                // Flat cloud layer: the farther toward the horizon, the farther away (and smaller) the clouds.
                float2 p = d.xz / (up + 0.08) * _Scale;
                float t = _Time.y;
                half big = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, p + _Wind.xy * t).r;
                half detail = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, p * _DetailScale + _Wind.zw * t).g;
                half n = big * 0.7h + detail * 0.3h;
                half threshold = 1.0h - _Coverage;
                half cloud = smoothstep(threshold - _Softness, threshold + _Softness, n);
                cloud = max(cloud, _Overcast); // rain: a closed grey ceiling under the shapes
                // Thicker parts are darker underneath; the sun side glows.
                half thickness = saturate((n - threshold) * 2.2h);
                half3 col = lerp(_CloudLit.rgb, _CloudShade.rgb, thickness);
                half sun = saturate(dot(d, normalize(_SunDir.xyz)));
                col += _SunGlow.rgb * (pow(sun, 6.0h) * 0.6h + pow(sun, 40.0h) * 0.8h) * (1.0h - thickness * 0.6h);
                return half4(col, cloud * horizon * _Opacity);
            }
            ENDHLSL
        }
    }
}
