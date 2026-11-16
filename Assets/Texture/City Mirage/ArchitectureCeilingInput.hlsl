#define InitializeStandardLitSurfaceData InitializeURPLitSurfaceData
#include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
#undef InitializeStandardLitSurfaceData
#include "CityMiragePackedAtlas.hlsl"

inline void InitializeStandardLitSurfaceData(float2 uv, out SurfaceData data)
{
    float2 cell = floor(uv * 4);
    bool concrete = cell.x == 0 && cell.y == 2;
    float2 mirror = 0;
    float2 source = uv;
    if (concrete)
    {
        float2 p = saturate((frac(uv * 4) - .025) / .95) * float2(6,5);
        mirror = step(1.0, fmod(floor(p), 2.0));
        float2 t = lerp(frac(p), 1-frac(p), mirror);
        source = (cell + .012 + t*.976) * .25;
    }
    CityMiragePackedSurface(source, data);
    data.normalTS.xy *= 1.0 - 2.0 * mirror;
    data.emission = CityMiragePackedEmission(uv) * _EmissionColor.rgb;
    bool counterTube = cell.x == 2 && cell.y == 1 && frac(uv.y * 4) > .67;
    if (counterTube)
    {
        data.albedo = 0;
        data.specular = 0;
        data.metallic = 0;
        data.emission *= 2;
    }
}


