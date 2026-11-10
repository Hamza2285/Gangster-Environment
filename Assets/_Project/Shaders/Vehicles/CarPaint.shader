// Mobile URP car paint: deep base colour, glossy clear coat with Fresnel reflection, subtle metallic flake, tight highlight.
// The cars use one atlas for paint, trim, interior and lamps, so _PaintMask (FinalCarPaint, editor) limits the paint layers to
// the painted pixels; everything else is plain diffuse with a faint rough sheen.
//
// Colour space: Fresnel, reflections and highlights are added in linear whichever colour space the project uses. In Gamma the
// inputs are squared and the result square-rooted (cheap sRGB approximation). Diffuse light is summed before that conversion, so
// an unpainted part is exactly as bright as its URP Lit neighbours. In Linear the conversions compile away.
//
// Reflections: URP's glossy environment path, so baked reflection probes along the streets (CarReflectionProbes, a day and a
// night set switched by ReflectionProbeDayNight) reflect the city; blending and box projection compile in when the pipeline asset
// enables them (Forward), Forward+ always blends. The ambient sky gradient stays as the fallback: it is scaled down by the global
// _CarPaintProbeBlend that ReflectionProbeDayNight sets while its probes are live, and is at full strength in scenes without them.
//
// Colour variants: _PaintColor replaces the atlas paint colour on the masked pixels (keeping the atlas' own shading through its
// brightness relative to _PaintRefLuma). One material per model and colour (FinalCarPaint), so the SRP Batcher keeps batching.
//
// Tune on CarPaint_Tuning.mat, then run Tools > OpenworldCrime > Vehicles > Apply Final Car Paint.
Shader "OpenworldCrime/Car Paint"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map (atlas)", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1,1,1,1)
        [NoScaleOffset] _PaintMask("Paint Mask (R, 1 = painted body)", 2D) = "white" {}
        [Toggle(_NORMALMAP)] _UseNormalMap("Use Normal Map", Float) = 0
        [NoScaleOffset][Normal] _BumpMap("Normal Map", 2D) = "bump" {}

        [Header(Colour Variant)]
        _PaintColor("Paint Colour", Color) = (1,1,1,1)
        _PaintRecolor("Use Paint Colour (0 = atlas colour)", Range(0, 1)) = 0
        _PaintRefLuma("Atlas Paint Brightness (set by the bake)", Range(0.01, 1)) = 0.5

        [Header(Base Coat)]
        _Saturation("Colour Saturation", Range(0, 2)) = 1.25
        _SunResponse("Direct Light On Paint", Range(0, 1.5)) = 1
        _LightCompress("Light Compression (deeper by day, brighter by night)", Range(0, 2)) = 0.2
        _ShadeLift("Ambient On Paint (0 = same as scene)", Range(-0.8, 2)) = 0.3
        _ColorDepth("Depth (darker at glancing angles)", Range(0, 1)) = 0.2
        _BaseSheen("Metallic Sheen Strength", Range(0, 2)) = 0.35
        _BaseSheenRoughness("Metallic Sheen Roughness", Range(0.15, 0.7)) = 0.38

        [Header(Clear Coat)]
        _CoatF0("Reflectance Facing Camera", Range(0.02, 0.25)) = 0.06
        _CoatFresnelMax("Reflectance At Glancing Angles (lower = more paint colour on the sides)", Range(0.3, 1)) = 0.6
        _CoatRoughness("Coat Roughness", Range(0.02, 0.3)) = 0.06
        _ReflectionStrength("Reflection Strength", Range(0, 3)) = 0.9
        _HorizonStrength("Sky Gradient Reflection (day/night aware)", Range(0, 3)) = 1.2
        _ReflectionTint("Reflection Tinted By Paint", Range(0, 1)) = 0.4
        _ReflectionSaturation("Reflection Saturation (lower = cleaner clear coat)", Range(0, 1)) = 0.3
        _HorizonHeight("Horizon Line Height (higher = line lower on the doors)", Range(0, 0.8)) = 0.3
        _HorizonWithProbes("Sky Gradient Kept Where Probes Reflect", Range(0, 1)) = 0.25
        _SpecularStrength("Highlight Strength", Range(0, 3)) = 1.6

        [Header(Metallic Flake)]
        [NoScaleOffset] _FlakeMap("Flake Map (RG tilt, B amount)", 2D) = "gray" {}
        _FlakeScale("Flake Tiling", Range(1, 64)) = 16
        _FlakeStrength("Flake Strength", Range(0, 3)) = 0.5
        _FlakeSpread("Flake Tilt", Range(0, 0.5)) = 0.14

        [Header(Unpainted Parts)]
        _TrimRoughness("Trim Roughness", Range(0.2, 1)) = 0.55
        _TrimF0("Trim Reflectance", Range(0, 0.1)) = 0.01

        // Set per renderer by VehicleHealth on a destroyed car: strips the paint layers so it looks burnt.
        [HideInInspector] _Burnt("Burnt", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // All passes share this block, as the SRP Batcher requires.
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Saturation;
            half _SunResponse;
            half _LightCompress;
            half _ShadeLift;
            half _ColorDepth;
            half _BaseSheen;
            half _BaseSheenRoughness;
            half _CoatF0;
            half _CoatFresnelMax;
            half _CoatRoughness;
            half _ReflectionStrength;
            half _HorizonStrength;
            half _HorizonHeight;
            half _HorizonWithProbes;
            half4 _PaintColor;
            half _PaintRecolor;
            half _PaintRefLuma;
            half _ReflectionTint;
            half _ReflectionSaturation;
            half _SpecularStrength;
            float _FlakeScale;
            half _FlakeStrength;
            half _FlakeSpread;
            half _TrimRoughness;
            half _TrimF0;
            half _Burnt;
        CBUFFER_END

        // Only here for the URP pass includes below, which reference them under _ALPHATEST_ON (never enabled).
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma shader_feature_local _NORMALMAP
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS _FORWARD_PLUS
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            #if defined(UNITY_COLORSPACE_GAMMA)
                #define TO_WORK(c) ((c) * (c))
                #define FROM_WORK(c) sqrt(max((c), 0.0))
            #else
                #define TO_WORK(c) (c)
                #define FROM_WORK(c) (c)
            #endif

            // Global (not per material): 1 while baked street probes are live (ReflectionProbeDayNight), 0 elsewhere.
            half _CarPaintProbeBlend;

            TEXTURE2D(_PaintMask); SAMPLER(sampler_PaintMask);
            TEXTURE2D(_FlakeMap);  SAMPLER(sampler_FlakeMap);
            #if defined(_NORMALMAP)
                TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            #endif

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS    : TEXCOORD2;
                #if defined(_NORMALMAP)
                    half4 tangentWS : TEXCOORD3;
                #endif
                half3 ambient     : TEXCOORD4;
                half fogFactor    : TEXCOORD5;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normal = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.normalWS = normal.normalWS;
                #if defined(_NORMALMAP)
                    output.tangentWS = half4(normal.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                #endif
                output.ambient = max(SampleSH(normal.normalWS), half3(0, 0, 0));
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            // URP's minimal Cook-Torrance. Float on purpose: a tight highlight bands at mobile half precision.
            float SpecularLobe(float nDotH, float lDotH, float roughness)
            {
                float r2 = roughness * roughness;
                float d = nDotH * nDotH * (r2 - 1.0) + 1.00001;
                return min(r2 / (d * d * max(0.1, lDotH * lDotH) * (roughness * 4.0 + 2.0)), 60.0);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 atlas = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half mask = SAMPLE_TEXTURE2D(_PaintMask, sampler_PaintMask, input.uv).r * (1.0 - _Burnt);
                half3 atlasColor = atlas.rgb * _BaseColor.rgb;
                // Variant colour on the paint, keeping the atlas' panel shading and highlights (its brightness against the average paint).
                half shade = min(dot(atlasColor, half3(0.2126, 0.7152, 0.0722)) / _PaintRefLuma, 1.3);
                atlasColor = lerp(atlasColor, _PaintColor.rgb * shade, mask * _PaintRecolor);
                half3 albedo = TO_WORK(atlasColor);

                half3 normalWS = input.normalWS;
                #if defined(_NORMALMAP)
                    half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv));
                    float3 bitangent = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
                    normalWS = TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangent, input.normalWS));
                #endif
                normalWS = normalize(normalWS);

                float3 viewWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half nDotV = saturate(dot(normalWS, viewWS));

                // Base coat: richer colour, darkening toward glancing angles where the coat reflection takes over.
                half luma = dot(albedo, half3(0.2126, 0.7152, 0.0722));
                half3 paintColor = max(lerp(luma.xxx, albedo, _Saturation), half3(0, 0, 0));
                paintColor *= lerp(1.0 - _ColorDepth, 1.0, nDotV);
                half3 diffuseColor = lerp(albedo, paintColor, mask);

                // Coat Fresnel; unpainted parts get a faint rough sheen from the same terms.
                half f0 = lerp(_TrimF0, _CoatF0, mask);
                half oneMinusNdotV = 1.0 - nDotV;
                half fresnelPower = oneMinusNdotV * oneMinusNdotV;
                fresnelPower = fresnelPower * fresnelPower * oneMinusNdotV;
                // Capped below 1 on the paint: at glancing angles the coat would otherwise replace the base colour with whatever it
                // reflects (mostly dark street and walls), which reads as murky paint on the sides of every car.
                half fresnel = f0 + (lerp(0.25, _CoatFresnelMax, mask) - f0) * fresnelPower;
                half coatRoughness = lerp(_TrimRoughness, _CoatRoughness, mask);

                half3 diffuseLight = input.ambient * (1.0 + _ShadeLift * mask);

                // Reflection: scene cubemap plus a sky gradient with a horizon line built from the ambient colours the
                // day/night system already drives, so it needs no extra texture and follows the time of day.
                half3 reflectWS = reflect(-viewWS, normalWS);
                half3 reflection = TO_WORK(GlossyEnvironmentReflection(reflectWS, input.positionWS, coatRoughness, 1.0, GetNormalizedScreenSpaceUV(input.positionCS)));
                half gradientHeight = reflectWS.y + _HorizonHeight;
                half band = smoothstep(0.0, 0.03, gradientHeight) * (1.0 - smoothstep(0.05, 0.28, gradientHeight));
                half upperSky = smoothstep(0.2, 0.65, gradientHeight);
                half3 gradient = TO_WORK(unity_AmbientGround.rgb) * 0.5 * (1.0 - smoothstep(0.0, 0.03, gradientHeight))
                    + TO_WORK(unity_AmbientEquator.rgb) * band + TO_WORK(unity_AmbientSky.rgb) * upperSky * 0.7;
                half gradientStrength = _HorizonStrength * lerp(1.0, _HorizonWithProbes, _CarPaintProbeBlend);
                reflection = reflection * lerp(1.0, _ReflectionStrength, mask) + gradient * gradientStrength * mask;
                // Partly desaturated on the paint: a clear coat over dark paint otherwise picks up the brick and sodium tones of the
                // city as a brown cast instead of reading as clean gloss.
                reflection = lerp(dot(reflection, half3(0.2126, 0.7152, 0.0722)).xxx, reflection, lerp(1.0, _ReflectionSaturation, mask));
                // Partly tinted by the base coat: pure white reflections over a dark paint read as a milky veil at this contrast.
                half3 paintTint = paintColor / max(max(paintColor.r, max(paintColor.g, paintColor.b)), 0.001);
                half3 specular = reflection * fresnel * lerp(half3(1, 1, 1), paintTint, _ReflectionTint * mask);

                // Flake tilt; its mip chain averages to no tilt at distance, so far cars do not shimmer.
                half3 flake = SAMPLE_TEXTURE2D(_FlakeMap, sampler_FlakeMap, input.uv * _FlakeScale).rgb;
                half3 flakeNormal = normalize(normalWS + (flake - 0.5) * 2.0 * _FlakeSpread);

                // Main light: diffuse, broad metallic sheen, flake sparkle and the tight coat highlight.
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 mainLightColor = mainLight.color * mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                half3 mainLightWork = TO_WORK(mainLightColor);
                float3 halfDir = SafeNormalize(mainLight.direction + viewWS);
                half nDotL = saturate(dot(normalWS, mainLight.direction));
                float nDotH = saturate(dot(normalWS, halfDir));
                float lDotH = saturate(dot(mainLight.direction, halfDir));
                float flakeNdotH = saturate(dot(flakeNormal, halfDir));
                diffuseLight += mainLightColor * nDotL * lerp(1.0, _SunResponse, mask);
                half3 lit = paintColor * _BaseSheen * SpecularLobe(nDotH, lDotH, _BaseSheenRoughness) * 0.15;
                lit += (paintColor + 0.03) * _FlakeStrength * flake.b * SpecularLobe(flakeNdotH, lDotH, 0.22) * 0.12;
                lit += _CoatF0 * _SpecularStrength * SpecularLobe(nDotH, lDotH, _CoatRoughness);
                specular += lit * mask * mainLightWork * nDotL;

                // Other lights (street lamps, headlights): diffuse and coat highlight only.
                #if defined(_ADDITIONAL_LIGHTS) || defined(_FORWARD_PLUS)
                    // The Forward+ light loop macro reads a local named inputData.
                    InputData inputData = (InputData)0;
                    inputData.positionWS = input.positionWS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                    uint lightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(lightCount)
                        Light light = GetAdditionalLight(lightIndex, input.positionWS);
                        half3 lightColor = light.color * light.distanceAttenuation;
                        float3 h = SafeNormalize(light.direction + viewWS);
                        half lightNdotL = saturate(dot(normalWS, light.direction));
                        diffuseLight += lightColor * lightNdotL;
                        specular += mask * _CoatF0 * _SpecularStrength * SpecularLobe(saturate(dot(normalWS, h)), saturate(dot(light.direction, h)), _CoatRoughness) * TO_WORK(lightColor) * lightNdotL;
                    LIGHT_LOOP_END
                #endif

                // Soft limit around a mid light level: paint keeps its depth in strong sun and stays readable in dim light.
                half3 paintLight = diffuseLight * (1.0 + _LightCompress * 0.6) / (1.0 + _LightCompress * diffuseLight);
                half3 color = diffuseColor * TO_WORK(lerp(diffuseLight, paintLight, mask)) * (1.0 - fresnel * mask) + specular;
                half3 output = FROM_WORK(color);
                output = MixFog(output, input.fogFactor);
                return half4(output, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Simple Lit"
}
