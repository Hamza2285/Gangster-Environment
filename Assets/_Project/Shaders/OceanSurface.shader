// One water surface for the whole ocean (the city's water and the open sea out to the horizon), so nothing can show a seam:
// - colour and see-through-ness of the city's water (OasisWater_Mat) inside the city's water square, easing into an opaque,
//   deeper open sea outside it (an opaque far sea never shows "nothing" through itself, which made bands and edges);
// - two scrolling normal maps in world space (no UV seams anywhere), fading out with distance (no shimmer far out);
// - the scene's existing light, ambient, environment reflection (sky) and fog, only read: the look follows the city's day and
//   night presets by itself. No depth texture, no refraction, no reflection probes, one draw call;
// - out to the horizon without touching any camera's far plane: water beyond it is drawn just inside it, on the same view ray
//   (same place on screen), while its shading uses the real distance; far out it turns see-through so the real sky meets it.
Shader "OpenworldCrime/OceanSurface"
{
    Properties
    {
        [Header(Colour)]
        _ShallowColor ("City water colour", Color) = (0.173, 0.344, 0.353, 1)
        _DeepColor ("Open sea colour", Color) = (0.06, 0.15, 0.19, 1)
        _CityAlpha ("City water opacity", Range(0, 1)) = 0.6
        _CityCenter ("City water centre (xz) and half size (zw)", Vector) = (199, -164, 1017, 1017)
        _CityBlend ("Blend width past the city square (m)", Float) = 400
        [Header(Waves)]
        [NoScaleOffset] _NormalMap ("Normal map", 2D) = "bump" {}
        _NormalTiling1 ("Tiling 1 (per m)", Float) = 0.004
        _NormalTiling2 ("Tiling 2 (per m)", Float) = 0.01
        _NormalStrength ("Strength", Range(0, 2)) = 0.35
        _NormalSpeed ("Scroll speed", Float) = 0.007
        _NormalFadeDistance ("Waves fade out by (m from camera)", Float) = 2500
        [Header(Reflection)]
        _Smoothness ("Smoothness", Range(0, 1)) = 0.92
        _ReflectionStrength ("Sky reflection", Range(0, 1.5)) = 0.9
        _FresnelPower ("Fresnel power", Range(1, 8)) = 4
        _SpecularStrength ("Sun / moon glint", Range(0, 4)) = 1.2
        [Header(Horizon)]
        _HorizonStart ("Blend into the horizon sky from (m)", Float) = 350
        _HorizonEnd ("Fully horizon sky at (m)", Float) = 4000
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            Name "OceanSurface"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half _CityAlpha;
                float4 _CityCenter;
                float _CityBlend;
                float _NormalTiling1;
                float _NormalTiling2;
                half _NormalStrength;
                float _NormalSpeed;
                float _NormalFadeDistance;
                half _Smoothness;
                half _ReflectionStrength;
                half _FresnelPower;
                half _SpecularStrength;
                float _HorizonStart;
                float _HorizonEnd;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half fog : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                float3 cam = GetCameraPositionWS();
                float3 ray = world - cam;
                float len = length(ray);
                float maxDistance = _ProjectionParams.z * 0.985;
                float3 drawn = len > maxDistance ? cam + ray * (maxDistance / len) : world;
                o.positionCS = TransformWorldToHClip(drawn);
                o.positionWS = world; // shading, waves and fog use the real place
                o.fog = ComputeFogFactor(TransformWorldToHClip(world).z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 viewVec = GetCameraPositionWS() - i.positionWS;
                float dist = length(viewVec);
                half3 v = viewVec / max(dist, 1e-3);

                // Waves: two world-space normal samples scrolling against each other, faded with distance.
                float t = _Time.y * _NormalSpeed;
                float2 xz = i.positionWS.xz;
                half3 n1 = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, xz * _NormalTiling1 + float2(t, t * 0.7)));
                half3 n2 = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, xz * _NormalTiling2 - float2(t * 0.8, t * 1.1)));
                half strength = _NormalStrength * saturate(1.0h - dist / _NormalFadeDistance);
                half2 slope = (n1.xy + n2.xy) * strength;
                half3 n = normalize(half3(slope.x, 1.0h, slope.y));

                // City water inside its square, open sea outside (a soft band, no edge).
                float2 d = abs(xz - _CityCenter.xy) - _CityCenter.zw;
                float outside = saturate(max(d.x, d.y) / max(_CityBlend, 1.0));
                half3 waterColour = lerp(_ShallowColor.rgb, _DeepColor.rgb, outside);
                half alpha = lerp(_CityAlpha, 1.0h, outside);

                // Scene light, ambient and sky reflection (whatever the day / night presets set).
                Light mainLight = GetMainLight();
                half ndl = saturate(dot(n, mainLight.direction));
                half3 ambient = SampleSH(n);
                half3 diffuse = waterColour * (ambient + mainLight.color * ndl * 0.25h);
                half3 r = reflect(-v, n);
                half roughness = 1.0h - _Smoothness;
                half3 sky = GlossyEnvironmentReflection(r, roughness * roughness, 1.0h) * _ReflectionStrength;
                half fresnel = 0.04h + 0.96h * pow(1.0h - saturate(dot(n, v)), _FresnelPower);
                half3 h = normalize(mainLight.direction + v);
                half spec = pow(saturate(dot(n, h)), 600.0h * _Smoothness + 20.0h) * _SpecularStrength;
                half3 colour = lerp(diffuse, sky, fresnel) + mainLight.color * spec;
                alpha = saturate(lerp(alpha, 1.0h, fresnel) + spec);

                // The scene's fog first (the sea fades like the city around it).
                colour = MixFog(colour, i.fog);
                // Far out the sea becomes see-through, so the real sky (whatever the day / night sky shows there) is what
                // meets the horizon: no colour guess, no line.
                half toHorizon = smoothstep(_HorizonStart, _HorizonEnd, dist);
                alpha *= 1.0h - toHorizon;
                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }
}
