#ifndef UNIVERSAL_INPUT_SURFACE_INCLUDED
#define UNIVERSAL_INPUT_SURFACE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceData.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);
float4 _BaseMap_TexelSize;
float4 _BaseMap_MipInfo;
TEXTURE2D(_BumpMap);
SAMPLER(sampler_BumpMap);
TEXTURE2D(_EmissionMap);
SAMPLER(sampler_EmissionMap);

///////////////////////////////////////////////////////////////////////////////
//                      Material Property Helpers                            //
///////////////////////////////////////////////////////////////////////////////
half Alpha(half albedoAlpha, half4 color, half cutoff)
{
#if !defined(_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A) && !defined(_GLOSSINESS_FROM_BASE_ALPHA)
    half alpha = albedoAlpha * color.a;
#else
    half alpha = color.a;
#endif

    alpha = AlphaDiscard(alpha, cutoff);

    return alpha;
}

TEXTURE2D_ARRAY(_RoadBaseArray); SAMPLER(sampler_RoadBaseArray);
TEXTURE2D_ARRAY(_RoadSurfaceArray); SAMPLER(sampler_RoadSurfaceArray);

// Five lossless imported texture slices. Integer U offsets carry the slice
// without consuming lightmap UV channels or adding per-renderer properties.
// Repeat sampling stays local to each slice at every mip level.
void RoadSettings(float slice, out float4 st, out half4 tint, out half4 surface) {
    if (slice < 0.5) { st=_Road0ST; tint=_Road0Tint; surface=half4(_Road0Metallic,_Road0Smoothness,_Road0Emission,_Road0SurfaceSlice); }
    else if (slice < 1.5) { st=_Road1ST; tint=_Road1Tint; surface=half4(_Road1Metallic,_Road1Smoothness,_Road1Emission,_Road1SurfaceSlice); }
    else if (slice < 2.5) { st=_Road2ST; tint=_Road2Tint; surface=half4(_Road2Metallic,_Road2Smoothness,_Road2Emission,_Road2SurfaceSlice); }
    else if (slice < 3.5) { st=_Road3ST; tint=_Road3Tint; surface=half4(_Road3Metallic,_Road3Smoothness,_Road3Emission,_Road3SurfaceSlice); }
    else { st=_Road4ST; tint=_Road4Tint; surface=half4(_Road4Metallic,_Road4Smoothness,_Road4Emission,_Road4SurfaceSlice); }
}
float RoadSlice(float2 uv) { return floor(uv.x / 32.0); }
float2 RoadUV(float2 uv) { return float2(uv.x - RoadSlice(uv) * 32.0 - 16.0, uv.y); }
half4 RoadColor(float2 uv) {
    float4 st; half4 tint, surface; RoadSettings(RoadSlice(uv),st,tint,surface);
    half4 color=SAMPLE_TEXTURE2D_ARRAY(_RoadBaseArray,sampler_RoadBaseArray,RoadUV(uv)*st.xy+st.zw,RoadSlice(uv));
    color.rgb*=tint.rgb; return color;
}
half4 RoadSurface(float2 uv) {
    float4 st; half4 tint, surface; RoadSettings(RoadSlice(uv),st,tint,surface);
    half4 map=SAMPLE_TEXTURE2D_ARRAY(_RoadSurfaceArray,sampler_RoadSurfaceArray,RoadUV(uv)*st.xy+st.zw,surface.w);
    map.r*=surface.x;map.a*=surface.y;return map;
}
half RoadEmission(float2 uv) { float4 st;half4 tint,surface;RoadSettings(RoadSlice(uv),st,tint,surface);return surface.z; }
half4 SampleAlbedoAlpha(float2 uv, TEXTURE2D_PARAM(albedoAlphaMap, sampler_albedoAlphaMap))
{
    return RoadColor(uv);
}

half3 SampleNormal(float2 uv, TEXTURE2D_PARAM(bumpMap, sampler_bumpMap), half scale = half(1.0))
{
#ifdef _NORMALMAP
    half4 n = SAMPLE_TEXTURE2D(bumpMap, sampler_bumpMap, uv);
    #if BUMP_SCALE_NOT_SUPPORTED
        return UnpackNormal(n);
    #else
        return UnpackNormalScale(n, scale);
    #endif
#else
    return half3(0.0h, 0.0h, 1.0h);
#endif
}

half3 SampleEmission(float2 uv, half3 emissionColor, TEXTURE2D_PARAM(emissionMap, sampler_emissionMap))
{
#ifndef _EMISSION
    return 0;
#else
    return RoadColor(uv).rgb * emissionColor;
#endif
}

#endif


