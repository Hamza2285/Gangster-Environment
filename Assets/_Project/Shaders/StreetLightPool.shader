// Fake street lamp light pool on the ground (pool meshes of NightStreetLights). One unlit blended pass that brightens what is
// already drawn (dst * (1 + light)), so paving keeps its texture and colour; no lights, no textures.
// Strength and colour come from _OWCStreetPool (set with the night blend); the mesh renderers are off by day.
Shader "OpenworldCrime/Street Light Pool"
{
    Properties
    {
        _Softness ("Edge softness", Range(0.5, 4)) = 1.6
        _DepthBias ("Lift toward the camera (m)", Range(0, 1)) = 0.12
        _FadeStart ("Fade start (m)", Float) = 160
        _FadeEnd ("Fade end (m)", Float) = 320
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-150" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Blend DstColor One, Zero One
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Softness;
                float _DepthBias;
                float _FadeStart, _FadeEnd;
            CBUFFER_END
            half4 _OWCStreetPool; // rgb colour x strength

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half fade : TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 world = TransformObjectToWorld(i.positionOS.xyz);
                float3 toCamera = _WorldSpaceCameraPos - world;
                float distance = length(toCamera);
                world += toCamera / max(distance, 0.01) * min(_DepthBias * (1.0 + distance * 0.04), distance * 0.5);
                o.positionCS = TransformWorldToHClip(world);
                o.uv = i.uv;
                o.fade = saturate(1.0 - (distance - _FadeStart) / max(_FadeEnd - _FadeStart, 1.0));
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half r2 = saturate(dot(i.uv, i.uv));
                half f = pow(max(1.0h - r2, 0.0001h), _Softness) * i.fade;
                return half4(_OWCStreetPool.rgb * f, 0);
            }
            ENDHLSL
        }
    }
}
