// Motion blur at the sides of the screen when driving near top speed (SpeedSideBlur.cs): a few samples along the line from the
// screen centre, only near the left and right edges - the car and the road ahead stay sharp. One full-screen pass, six texture
// samples, and the pass is switched off completely below speed.
Shader "OpenworldCrime/Speed Side Blur"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always
        Pass
        {
            Name "SpeedSideBlur"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _SpeedSideBlur; // 0..1

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float2 fromCentre = uv - float2(0.5, 0.55);
                half side = smoothstep(0.24h, 0.5h, (half)abs(fromCentre.x));
                float2 stepUv = -fromCentre * (0.016 * side * _SpeedSideBlur);
                half4 c = 0;
                for (int i = 0; i < 6; i++) c += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + stepUv * i, 0);
                return c / 6.0h;
            }
            ENDHLSL
        }
    }
}
