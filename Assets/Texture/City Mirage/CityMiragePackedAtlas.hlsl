#ifndef CITY_MIRAGE_PACKED_ATLAS_INCLUDED
#define CITY_MIRAGE_PACKED_ATLAS_INCLUDED
float2 CityMiragePackedUV(float2 uv, float2 tile)
{
    float2 inset = 2.0 * _BaseMap_TexelSize.xy;
    return (clamp(uv, inset, 1.0 - inset) + tile) * .5;
}
half4 CityMiragePackedSample(float2 uv, float2 tile)
{
    return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, CityMiragePackedUV(uv,tile));
}
half3 CityMiragePackedColor(half3 rgb)
{
    #ifndef UNITY_COLORSPACE_GAMMA
    half3 low = rgb / 12.92h;
    half3 high = pow((rgb + .055h) / 1.055h, 2.4h);
    return lerp(low, high, step(.04045h, rgb));
    #else
    return rgb;
    #endif
}
half3 CityMiragePackedEmission(float2 uv)
{
    return CityMiragePackedColor(CityMiragePackedSample(uv,float2(1,0)).rgb);
}
inline void CityMiragePackedSurface(float2 uv, out SurfaceData data)
{
    data = (SurfaceData)0;
    half4 base = CityMiragePackedSample(uv,float2(0,1));
    half4 surface = CityMiragePackedSample(uv,float2(0,0));
    half2 normal = (CityMiragePackedSample(uv,float2(1,1)).rg * 2.0h - 1.0h) * _BumpScale;
    data.albedo = CityMiragePackedColor(base.rgb) * _BaseColor.rgb;
    data.alpha = Alpha(base.a,_BaseColor,_Cutoff);
    data.normalTS = half3(normal,sqrt(saturate(1.0h-dot(normal,normal))));
    data.metallic = surface.r;
    data.smoothness = surface.a * _Smoothness;
    #if defined(_OCCLUSIONMAP)
    data.occlusion = lerp(1.0h,surface.g,_OcclusionStrength);
    #else
    data.occlusion = 1.0h;
    #endif
}
#endif
