// Blends the day and night bake of one lightmap (BakedLightingSwitcher, during a day/night transition) into a render target.
// The lightmaps are RGBM encoded (Lightmap import, gamma colour space: value = rgb * alpha * 5), so the blend is done on the
// decoded light and re-encoded; at blend 0 / 1 the result decodes to exactly the day / night light.
Shader "Hidden/OpenworldCrime/LightmapBlend"
{
    Properties
    {
        _DayTex ("Day", 2D) = "black" {}
        _NightTex ("Night", 2D) = "black" {}
        _Blend ("Blend", Range(0, 1)) = 0
        _RGBMRange ("RGBM range", Float) = 5
        _Encoded ("RGBM encoded (1) / plain (0)", Float) = 1
    }
    SubShader
    {
        ZTest Always ZWrite Off Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _DayTex;
            sampler2D _NightTex;
            float _Blend;
            float _RGBMRange;
            float _Encoded;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 d = tex2D(_DayTex, i.uv);
                float4 n = tex2D(_NightTex, i.uv);
                if (_Encoded < 0.5) return lerp(d, n, _Blend);
                float3 light = lerp(d.rgb * d.a, n.rgb * n.a, _Blend) * _RGBMRange;
                // Re-encode: the smallest multiplier (8-bit steps) that fits the brightest channel.
                float m = saturate(max(max(light.r, light.g), max(light.b, 1e-6)) / _RGBMRange);
                m = ceil(m * 255.0) / 255.0;
                return float4(light / (max(m, 1.0 / 255.0) * _RGBMRange), m);
            }
            ENDCG
        }
    }
}
