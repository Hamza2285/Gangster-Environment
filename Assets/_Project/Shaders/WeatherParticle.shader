// Weather particles (rain streaks, splashes, drips): no texture, the shape is made from the particle UVs, so there is no
// texture fetch and the fragment work is a few ALU. Alpha blended, unlit, tinted by the scene's ambient light (so rain is
// bright by day, dim at night) plus a flat tint. _Shape: 0 = streak (soft vertical line), 1 = ring (splash), 2 = soft dot.
Shader "OpenworldCrime/WeatherParticle"
{
    Properties
    {
        _Color ("Tint (alpha = opacity)", Color) = (0.75, 0.8, 0.9, 0.35)
        _Shape ("Shape (0 streak, 1 ring, 2 dot)", Float) = 0
        _AmbientShare ("Ambient lighting share", Range(0, 1)) = 0.8
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Weather"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Shape;
                half _AmbientShare;
            CBUFFER_END
            float _WeatherFlash; // lightning (WeatherManager)

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; half3 light : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv;
                half3 ambient = SampleSH(half3(0, 1, 0));
                o.light = lerp(half3(1, 1, 1), saturate(ambient * 1.6h + 0.08h), _AmbientShare) + _WeatherFlash.xxx;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 c = i.uv * 2.0 - 1.0;
                half a;
                if (_Shape < 0.5) a = saturate(1.0 - abs(c.x) * 1.15) * saturate(1.0 - c.y * c.y);       // streak
                else if (_Shape < 1.5) { half r = length(c); a = saturate(1.0 - abs(r - 0.7) * 5.0); }  // ring
                else a = saturate(1.0 - length(c)); a *= a;                                             // dot
                half4 col = _Color * i.color;
                col.rgb *= i.light;
                col.a *= a;
                return col;
            }
            ENDHLSL
        }
    }
}
