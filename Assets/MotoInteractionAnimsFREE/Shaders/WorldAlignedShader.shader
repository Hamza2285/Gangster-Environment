Shader "Custom/WorldAlignedTriplanar"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Tiling ("Tiling", Float) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows

        sampler2D _MainTex;
        float _Tiling;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
        };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float3 blend = abs(IN.worldNormal);
            blend = blend / (blend.x + blend.y + blend.z);

            float2 xUV = IN.worldPos.yz * _Tiling;
            float2 yUV = IN.worldPos.xz * _Tiling;
            float2 zUV = IN.worldPos.xy * _Tiling;

            fixed4 texX = tex2D(_MainTex, xUV);
            fixed4 texY = tex2D(_MainTex, yUV);
            fixed4 texZ = tex2D(_MainTex, zUV);

            fixed4 finalColor = texX * blend.x + texY * blend.y + texZ * blend.z;

            o.Albedo = finalColor.rgb;
            o.Alpha = finalColor.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
