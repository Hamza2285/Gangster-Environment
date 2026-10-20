#ifndef UNIVERSAL_LIT_INPUT_INCLUDED
#define UNIVERSAL_LIT_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ParallaxMapping.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"

#if defined(_DETAIL_MULX2) || defined(_DETAIL_SCALED)
#define _DETAIL
#endif

// NOTE: Do not ifdef the properties here as SRP batcher can not handle different layouts.
CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST;
float4 _DetailAlbedoMap_ST;
half4 _BaseColor;
half4 _SpecColor;
half4 _EmissionColor;
half _Cutoff;
half _Smoothness;
half _Metallic;
half _BumpScale;
half _Parallax;
half _OcclusionStrength;
half _ClearCoatMask;
half _ClearCoatSmoothness;
half _DetailAlbedoMapScale;
half _DetailNormalMapScale;
half _Surface;
half _CityFlagAtlas; half _CityHospital; float4 _CityHospitalAtlasRect; float4 _CityClothAtlasRect; float4 _CityClothVariantsRect; float4 _CityWeaponAtlasRect; float4 _CityWeaponVariantsRect; float4 _CityWeaponStoneRect;
half _CityContrast, _CityMetallic, _CitySmoothnessLimit, _CityEmissionStrength, _CityRailingGlow, _CityRailingUseEmissionMap;
CBUFFER_END

// NOTE: Do not ifdef the properties for dots instancing, but ifdef the actual usage.
// Otherwise you might break CPU-side as property constant-buffer offsets change per variant.
// NOTE: Dots instancing is orthogonal to the constant buffer above.
#ifdef UNITY_DOTS_INSTANCING_ENABLED

UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
    UNITY_DOTS_INSTANCED_PROP(float4, _BaseColor)
    UNITY_DOTS_INSTANCED_PROP(float4, _SpecColor)
    UNITY_DOTS_INSTANCED_PROP(float4, _EmissionColor)
    UNITY_DOTS_INSTANCED_PROP(float , _Cutoff)
    UNITY_DOTS_INSTANCED_PROP(float , _Smoothness)
    UNITY_DOTS_INSTANCED_PROP(float , _Metallic)
    UNITY_DOTS_INSTANCED_PROP(float , _BumpScale)
    UNITY_DOTS_INSTANCED_PROP(float , _Parallax)
    UNITY_DOTS_INSTANCED_PROP(float , _OcclusionStrength)
    UNITY_DOTS_INSTANCED_PROP(float , _ClearCoatMask)
    UNITY_DOTS_INSTANCED_PROP(float , _ClearCoatSmoothness)
    UNITY_DOTS_INSTANCED_PROP(float , _DetailAlbedoMapScale)
    UNITY_DOTS_INSTANCED_PROP(float , _DetailNormalMapScale)
    UNITY_DOTS_INSTANCED_PROP(float , _Surface)
UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)

// Here, we want to avoid overriding a property like e.g. _BaseColor with something like this:
// #define _BaseColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _BaseColor0)
//
// It would be simpler, but it can cause the compiler to regenerate the property loading code for each use of _BaseColor.
//
// To avoid this, the property loads are cached in some static values at the beginning of the shader.
// The properties such as _BaseColor are then overridden so that it expand directly to the static value like this:
// #define _BaseColor unity_DOTS_Sampled_BaseColor
//
// This simple fix happened to improve GPU performances by ~10% on Meta Quest 2 with URP on some scenes.
static float4 unity_DOTS_Sampled_BaseColor;
static float4 unity_DOTS_Sampled_SpecColor;
static float4 unity_DOTS_Sampled_EmissionColor;
static float  unity_DOTS_Sampled_Cutoff;
static float  unity_DOTS_Sampled_Smoothness;
static float  unity_DOTS_Sampled_Metallic;
static float  unity_DOTS_Sampled_BumpScale;
static float  unity_DOTS_Sampled_Parallax;
static float  unity_DOTS_Sampled_OcclusionStrength;
static float  unity_DOTS_Sampled_ClearCoatMask;
static float  unity_DOTS_Sampled_ClearCoatSmoothness;
static float  unity_DOTS_Sampled_DetailAlbedoMapScale;
static float  unity_DOTS_Sampled_DetailNormalMapScale;
static float  unity_DOTS_Sampled_Surface;

void SetupDOTSLitMaterialPropertyCaches()
{
    unity_DOTS_Sampled_BaseColor            = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _BaseColor);
    unity_DOTS_Sampled_SpecColor            = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _SpecColor);
    unity_DOTS_Sampled_EmissionColor        = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _EmissionColor);
    unity_DOTS_Sampled_Cutoff               = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Cutoff);
    unity_DOTS_Sampled_Smoothness           = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Smoothness);
    unity_DOTS_Sampled_Metallic             = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Metallic);
    unity_DOTS_Sampled_BumpScale            = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _BumpScale);
    unity_DOTS_Sampled_Parallax             = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Parallax);
    unity_DOTS_Sampled_OcclusionStrength    = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _OcclusionStrength);
    unity_DOTS_Sampled_ClearCoatMask        = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _ClearCoatMask);
    unity_DOTS_Sampled_ClearCoatSmoothness  = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _ClearCoatSmoothness);
    unity_DOTS_Sampled_DetailAlbedoMapScale = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _DetailAlbedoMapScale);
    unity_DOTS_Sampled_DetailNormalMapScale = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _DetailNormalMapScale);
    unity_DOTS_Sampled_Surface              = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Surface);
}

#undef UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES
#define UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES() SetupDOTSLitMaterialPropertyCaches()

#define _BaseColor              unity_DOTS_Sampled_BaseColor
#define _SpecColor              unity_DOTS_Sampled_SpecColor
#define _EmissionColor          unity_DOTS_Sampled_EmissionColor
#define _Cutoff                 unity_DOTS_Sampled_Cutoff
#define _Smoothness             unity_DOTS_Sampled_Smoothness
#define _Metallic               unity_DOTS_Sampled_Metallic
#define _BumpScale              unity_DOTS_Sampled_BumpScale
#define _Parallax               unity_DOTS_Sampled_Parallax
#define _OcclusionStrength      unity_DOTS_Sampled_OcclusionStrength
#define _ClearCoatMask          unity_DOTS_Sampled_ClearCoatMask
#define _ClearCoatSmoothness    unity_DOTS_Sampled_ClearCoatSmoothness
#define _DetailAlbedoMapScale   unity_DOTS_Sampled_DetailAlbedoMapScale
#define _DetailNormalMapScale   unity_DOTS_Sampled_DetailNormalMapScale
#define _Surface                unity_DOTS_Sampled_Surface

#endif

TEXTURE2D(_ParallaxMap);        SAMPLER(sampler_ParallaxMap);
TEXTURE2D(_OcclusionMap);       SAMPLER(sampler_OcclusionMap);
TEXTURE2D(_DetailMask);         SAMPLER(sampler_DetailMask);
TEXTURE2D(_DetailAlbedoMap);    SAMPLER(sampler_DetailAlbedoMap);
TEXTURE2D(_DetailNormalMap);    SAMPLER(sampler_DetailNormalMap);
TEXTURE2D(_MetallicGlossMap);   SAMPLER(sampler_MetallicGlossMap);
TEXTURE2D(_SpecGlossMap);       SAMPLER(sampler_SpecGlossMap);
TEXTURE2D(_ClearCoatMap);       SAMPLER(sampler_ClearCoatMap);

#ifdef _SPECULAR_SETUP
    #define SAMPLE_METALLICSPECULAR(uv) SAMPLE_TEXTURE2D(_SpecGlossMap, sampler_SpecGlossMap, uv)
#else
    #define SAMPLE_METALLICSPECULAR(uv) SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uv)
#endif

half4 SampleMetallicSpecGloss(float2 uv, half albedoAlpha)
{
    half4 specGloss;

#ifdef _METALLICSPECGLOSSMAP
    specGloss = half4(SAMPLE_METALLICSPECULAR(uv));
    #ifdef _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
        specGloss.a = albedoAlpha * _Smoothness;
    #else
        specGloss.a *= _Smoothness;
    #endif
#else // _METALLICSPECGLOSSMAP
    #if _SPECULAR_SETUP
        specGloss.rgb = _SpecColor.rgb;
    #else
        specGloss.rgb = _Metallic.rrr;
    #endif

    #ifdef _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
        specGloss.a = albedoAlpha * _Smoothness;
    #else
        specGloss.a = _Smoothness;
    #endif
#endif

    return specGloss;
}

half SampleOcclusion(float2 uv)
{
    #ifdef _OCCLUSIONMAP
        half occ = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, uv).g;
        return LerpWhiteTo(occ, _OcclusionStrength);
    #else
        return half(1.0);
    #endif
}


// Returns clear coat parameters
// .x/.r == mask
// .y/.g == smoothness
half2 SampleClearCoat(float2 uv)
{
#if defined(_CLEARCOAT) || defined(_CLEARCOATMAP)
    half2 clearCoatMaskSmoothness = half2(_ClearCoatMask, _ClearCoatSmoothness);

#if defined(_CLEARCOATMAP)
    clearCoatMaskSmoothness *= SAMPLE_TEXTURE2D(_ClearCoatMap, sampler_ClearCoatMap, uv).rg;
#endif

    return clearCoatMaskSmoothness;
#else
    return half2(0.0, 1.0);
#endif  // _CLEARCOAT
}

void ApplyPerPixelDisplacement(half3 viewDirTS, inout float2 uv)
{
#if defined(_PARALLAXMAP)
    uv += ParallaxMapping(TEXTURE2D_ARGS(_ParallaxMap, sampler_ParallaxMap), viewDirTS, _Parallax, uv);
#endif
}

// Used for scaling detail albedo. Main features:
// - Depending if detailAlbedo brightens or darkens, scale magnifies effect.
// - No effect is applied if detailAlbedo is 0.5.
half3 ScaleDetailAlbedo(half3 detailAlbedo, half scale)
{
    // detailAlbedo = detailAlbedo * 2.0h - 1.0h;
    // detailAlbedo *= _DetailAlbedoMapScale;
    // detailAlbedo = detailAlbedo * 0.5h + 0.5h;
    // return detailAlbedo * 2.0f;

    // A bit more optimized
    return half(2.0) * detailAlbedo * scale - scale + half(1.0);
}

half3 ApplyDetailAlbedo(float2 detailUv, half3 albedo, half detailMask)
{
#if defined(_DETAIL)
    half3 detailAlbedo = SAMPLE_TEXTURE2D(_DetailAlbedoMap, sampler_DetailAlbedoMap, detailUv).rgb;

    // In order to have same performance as builtin, we do scaling only if scale is not 1.0 (Scaled version has 6 additional instructions)
#if defined(_DETAIL_SCALED)
    detailAlbedo = ScaleDetailAlbedo(detailAlbedo, _DetailAlbedoMapScale);
#else
    detailAlbedo = half(2.0) * detailAlbedo;
#endif

    return albedo * LerpWhiteTo(detailAlbedo, detailMask);
#else
    return albedo;
#endif
}

half3 ApplyDetailNormal(float2 detailUv, half3 normalTS, half detailMask)
{
#if defined(_DETAIL)
#if BUMP_SCALE_NOT_SUPPORTED
    half3 detailNormalTS = UnpackNormal(SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, detailUv));
#else
    half3 detailNormalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, detailUv), _DetailNormalMapScale);
#endif

    // With UNITY_NO_DXT5nm unpacked vector is not normalized for BlendNormalRNM
    // For visual consistancy we going to do in all cases
    detailNormalTS = normalize(detailNormalTS);

    return lerp(normalTS, BlendNormalRNM(normalTS, detailNormalTS), detailMask); // todo: detailMask should lerp the angle of the quaternion rotation, not the normals
#else
    return normalTS;
#endif
}

TEXTURE2D(_CitySurfaceMap); SAMPLER(sampler_CitySurfaceMap);
half _GangstarNight, _CityNightWindows, _CityWindowCoverage; half4 _CityNightColor;
TEXTURE2D(_CityNightMap); SAMPLER(sampler_CityNightMap);
half _CityWindowMix, _CityAccentBrightness, _CityVenueBrightness;
half4 _CityWhiteWindowColor, _CityBlueWindowColor, _CityBlueAccentColor, _CityPinkAccentColor;
inline void InitializeStandardLitSurfaceData(float2 uv, out SurfaceData s)
{
    half weaponStone = (_CityHospital > .5 && uv.x >= 223.0) ? 1.0h : 0.0h;
    half stoneNosing = weaponStone > .5 && uv.y > 63.0 ? 1.0h : 0.0h;
    if (weaponStone > .5) uv = frac(uv - float2(224.0,stoneNosing > .5 ? 64.0 : 0.0));
    half weaponVariant = (_CityHospital > .5 && uv.x >= 159.0) ? 1.0h : 0.0h;
    half weaponFacade = (_CityHospital > .5 && uv.x >= 127.0) ? 1.0h : 0.0h;
    half clothFacade = (_CityHospital > .5 && uv.x >= 63.0) ? 1.0h : 0.0h;
    half clothVariant = (_CityHospital > .5 && uv.x >= 95.0 && uv.x < 127.0) ? 1.0h : 0.0h;
    if (clothFacade > .5) uv.x -= weaponFacade > .5 ? (weaponVariant > .5 ? 160.0 : 128.0) : (clothVariant > .5 ? 96.0 : 64.0);
    // Non-front hospital walls use a continuous architectural floor coordinate; wrap only inside the window island.
    half hospitalCladding = (_CityHospital > .5 && uv.x > 31.0) ? 1.0h : 0.0h;
    if (hospitalCladding > .5) { uv = float2(.146 + .005 * frac(uv.x - 32.0), .740 + .240 * frac(uv.y)); }
    else if (_CityHospital > .5 && uv.x > 15.0) { uv.x -= 16.0; uv.y = .704 + .286 * frac(uv.y); }
    // Shared bus shelter palette and light strips: no extra texture or material.
    half busWarm = step(16.0,uv.x); uv.x -= busWarm * 16.0;
    half busSurface = step(12.0,uv.x); uv.x -= busSurface * 12.0;
    half busAccent = step(8.0,uv.x); uv.x -= busAccent * 8.0;
    half coffeeWindow = step(4.0,uv.x); uv.x -= coffeeWindow * 4.0;
    half signGeometry = step(2.0,uv.x); uv.x -= signGeometry * 2.0;
    float2 baseUV = uv;
    if (_CityHospital > .5) { float4 tile = weaponStone > .5 ? _CityWeaponStoneRect : weaponFacade > .5 ? (weaponVariant > .5 ? _CityWeaponVariantsRect : _CityWeaponAtlasRect) : (clothFacade > .5 ? (clothVariant > .5 ? _CityClothVariantsRect : _CityClothAtlasRect) : _CityHospitalAtlasRect); baseUV = uv * tile.xy + tile.zw; }
    half4 base = SampleAlbedoAlpha(baseUV, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap));
    // Flag tiles occupy previously white pixels; building surfaces keep their original white sample.
    float2 reservedUV = frac(uv);
    if (_CityFlagAtlas > .5 && reservedUV.x > .250 && reservedUV.x < .625 && reservedUV.y > .1875 && reservedUV.y < .250) base = half4(1,1,1,1);
    // Existing amber post inserts only; no outline emission.
    float2 railingUV=frac(uv);
    half railingMask=0;
    half3 railingColor=half3(1,.22,.025);
    if (_CityRailingGlow > .001 && railingUV.y > .025 && railingUV.y < .135) {
        half post=(railingUV.x < .085 || (railingUV.x > .475 && railingUV.x < .565) || railingUV.x > .945) ? 1.0h : 0.0h;
        railingMask=post*smoothstep(.10h,.30h,base.r-base.b)*smoothstep(.08h,.30h,base.g-base.b);
        railingColor=railingUV.x < .25 ? half3(1,.22,.025) : (railingUV.x < .75 ? half3(1,.025,.32) : half3(1,.015,.01));
        if(_CityRailingUseEmissionMap > .5) railingMask*=SAMPLE_TEXTURE2D(_EmissionMap,sampler_EmissionMap,uv).r;
    }
    s = (SurfaceData)0;
    s.alpha = Alpha(base.a, _BaseColor, _Cutoff);
    s.albedo = max(0.0h, (base.rgb * _BaseColor.rgb - 0.18h) * _CityContrast + 0.18h);
#ifdef _CITY_SURFACE_MAP
    half4 packed = SAMPLE_TEXTURE2D(_CitySurfaceMap, sampler_CitySurfaceMap, uv);
    s.metallic = saturate(packed.r * _CityMetallic);
    s.smoothness = min(_CitySmoothnessLimit, packed.a * _Smoothness);
    s.occlusion = lerp(1.0h, packed.g, _OcclusionStrength);
#else
    s.metallic = _Metallic;
    s.smoothness = min(_CitySmoothnessLimit, _Smoothness);
    s.occlusion = 1.0h;
#endif
    s.specular = half3(0,0,0);
    s.normalTS = SampleNormal(uv, TEXTURE2D_ARGS(_BumpMap,sampler_BumpMap),_BumpScale);
#ifdef _CITY_SURFACE_MAP
    half windowMask = saturate(packed.b * 4.0h) * saturate((packed.b + _CityWindowCoverage - 1.0h) * 5.0h);
        half3 windowColor = _CityNightColor.rgb;
    half3 accent = 0;
    half venue = 0; half signLight = 0;
#ifdef _CITY_NIGHT_MAP
    half4 nightStyle = SAMPLE_TEXTURE2D(_CityNightMap, sampler_CityNightMap, uv);
    // RGB stores two window weights (white and blue); warm windows remain linked to Warmth.
    half3 palette = lerp(windowColor, _CityWhiteWindowColor.rgb, nightStyle.r);
    palette = lerp(palette, _CityBlueWindowColor.rgb, nightStyle.g);
    windowColor = lerp(windowColor, palette, _CityWindowMix);
    windowMask *= nightStyle.a;
    // Blue channel blends cyan/blue to pink for narrow architectural accents.
    accent = lerp(_CityBlueAccentColor.rgb,_CityPinkAccentColor.rgb,saturate((nightStyle.b - .34h) * 1.52h)) * nightStyle.a * saturate((nightStyle.b - .23h) * 10.0h) * _CityAccentBrightness;
    signLight = step(.16h, nightStyle.b) * (1.0h - step(.22h, nightStyle.b));
    venue = step(.04h, nightStyle.b) * (1.0h - step(.22h, nightStyle.b)) * nightStyle.a * _CityVenueBrightness;
#endif
    s.emission = _GangstarNight * (windowMask * _CityNightWindows * windowColor * lerp(half3(1,1,1), base.rgb, 0.35h) + accent + base.rgb * venue + signGeometry * half3(1.0h,.78h,.44h) * _CityVenueBrightness);
#endif
#ifdef _EMISSION
    // The source materials use albedo as emission. Reuse that sample.
    s.emission += base.rgb * _EmissionColor.rgb * _CityEmissionStrength * (1.0h - _GangstarNight);
#endif
    // Coffee-only smooth dielectric glass: existing environment probes provide view-dependent reflections over the baked interior.
    if (coffeeWindow > .5) { s.normalTS=half3(0,0,1); s.metallic=0; s.specular=half3(.04h,.04h,.04h); s.smoothness=.82h; s.occlusion=1; s.albedo*=.96h; s.emission=base.rgb*lerp(.04h,.65h,saturate(_GangstarNight)); }
    if (_CityHospital > .5) { half warm = smoothstep(.03h,.18h,base.r-base.b)*smoothstep(.18h,.55h,base.g); half brand=max(smoothstep(.10h,.30h,base.b-base.r),smoothstep(.15h,.40h,base.r-base.g)); half led=smoothstep(.78h,.95h,dot(base.rgb,half3(.3,.59,.11))); s.normalTS=half3(0,0,1); s.metallic=.02h; s.smoothness=.46h; s.occlusion=1; s.emission=base.rgb*(.025h+saturate(_GangstarNight)*(.48h*warm+.85h*brand+.65h*led));  if (hospitalCladding > .5) { s.metallic=0; s.smoothness=.17h; s.emission=half3(0,0,0); } }

    // Painted shop fascia only: boost cyan LEDs without lighting clothing or other buildings.
    if (clothFacade > .5 && clothVariant < .5 && uv.y > .71) { half blueLED = smoothstep(.12h,.40h,base.b-base.r)*smoothstep(.20h,.60h,base.g)*smoothstep(.35h,.75h,base.b); s.emission += base.rgb * blueLED * lerp(.45h,2.4h,saturate(_GangstarNight)); }
    if (weaponFacade > .5 && weaponVariant < .5 && uv.y > .70) { half amberLED = smoothstep(.18h,.45h,base.r-base.b)*smoothstep(.35h,.70h,base.g); s.emission += base.rgb * amberLED * lerp(.3h,1.8h,saturate(_GangstarNight)); }
    if (weaponStone > .5) { s.albedo *= stoneNosing > .5 ? .34h : .72h; s.metallic=0; s.smoothness=.18h; s.occlusion=1; s.normalTS=half3(0,0,1); s.emission=half3(0,0,0); }
    if (_CityRailingGlow > .001 && railingMask > 0) { s.albedo=lerp(s.albedo,railingColor,railingMask*.75h); s.emission+=railingColor*railingMask*_CityRailingGlow*lerp(.28h,1.0h,saturate(_GangstarNight)); }
    if(busSurface>.5h||busAccent>.5h||busWarm>.5h){s.normalTS=half3(0,0,1);s.metallic=.12h;s.smoothness=.32h;s.occlusion=1;s.emission=base.rgb*(busAccent>.5h?lerp(.65h,2.4h,saturate(_GangstarNight)):.02h);if(busWarm>.5h)s.emission=base.rgb*half3(1,.78,.48)*lerp(.03h,.40h,saturate(_GangstarNight));}
}

#endif // UNIVERSAL_INPUT_SURFACE_PBR_INCLUDED









