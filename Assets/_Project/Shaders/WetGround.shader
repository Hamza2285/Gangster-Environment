// Wet ground around the player (WetGround.cs): a ground-fitted mesh drawn over the city's own ground (whose materials are
// not ours and are not changed). One alpha-premultiplied pass: it darkens what is under it (wet asphalt absorbs light) and adds
// a glossy sheen - the sky / ambient colour, stronger at grazing angles (Fresnel) - plus puddles from a world-space noise mask
// that reflect more and ripple. Fades out toward the mesh edge (vertex alpha) and with the wetness. Unlit: a few ALU and two
// texture samples per pixel of nearby ground.
Shader "OpenworldCrime/WetGround"
{
    Properties
    {
        _NoiseTex ("Noise (R puddle shapes, G ripple)", 2D) = "gray" {}
        _Wetness ("Wetness (set at runtime)", Range(0, 1)) = 0
        _Darken ("Darkening when fully wet", Range(0, 1)) = 0.32
        _Sheen ("Sheen strength", Range(0, 2)) = 0.55
        _PuddleScale ("Puddle scale (per m)", Float) = 0.045
        _PuddleCoverage ("Puddle coverage", Range(0, 1)) = 0.35
        _SheenTint ("Sheen tint", Color) = (0.75, 0.82, 0.95, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-200" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Offset -2, -2
        Cull Back

        Pass
        {
            Name "WetGround"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
                half _Wetness, _Darken, _Sheen, _PuddleCoverage;
                float _PuddleScale;
                half4 _SheenTint;
            CBUFFER_END
            float _WeatherFlash;

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 world : TEXCOORD0; half fade : TEXCOORD1; };
            static const half MinUpNormal = 0.82h; // flatter than ~35 degrees only (see frag)

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.world = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.world);
                o.fade = v.color.a;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // Grid triangles that bridge a height jump (a kerb-to-roof, deck-to-street or canopy edge) are steep sheets
                // standing in the air: drop them. The face normal comes from the screen derivatives (one per triangle).
                float3 faceNormal = normalize(cross(ddy(i.world), ddx(i.world)));
                clip(abs(faceNormal.y) - MinUpNormal);
                half wet = _Wetness * i.fade;
                if (wet <= 0.001h) return 0;
                float2 p = i.world.xz * _PuddleScale;
                half shapes = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, p).r;
                half puddle = smoothstep(1.0h - _PuddleCoverage, 1.0h - _PuddleCoverage + 0.08h, shapes) * saturate(_Wetness * 1.5h - 0.3h);
                half ripple = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, i.world.xz * 0.9 + _Time.y * float2(0.13, 0.21)).g;

                float3 view = normalize(_WorldSpaceCameraPos - i.world);
                half fresnel = pow(1.0h - saturate(view.y), 4.0h);
                half3 sky = saturate(SampleSH(half3(0, 1, 0)) * 1.4h) + _WeatherFlash.xxx;
                half reflectAmount = (fresnel * 0.8h + 0.12h) * _Sheen * (1.0h + puddle * 1.6h) * (0.85h + ripple * 0.3h);
                half3 sheen = sky * _SheenTint.rgb * reflectAmount;

                half darken = _Darken * (1.0h + puddle * 0.4h);
                // Premultiplied: colour added, then what is below kept at (1 - alpha).
                return half4(sheen * wet, saturate(darken * wet));
            }
            ENDHLSL
        }
    }
}
