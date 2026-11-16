#define InitializeStandardLitSurfaceData InitializeURPLitSurfaceData
#include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
#undef InitializeStandardLitSurfaceData
#include "CityMiragePackedAtlas.hlsl"
inline void InitializeStandardLitSurfaceData(float2 uv, out SurfaceData data)
{
    if (uv.x <= 1)
    {
        CityMiragePackedSurface(float2(uv.x*.5,uv.y),data);
        return;
    }
    float mode=floor(uv.x);
    float2 atlasUV=float2(frac(uv.x),uv.y);
    CityMiragePackedSurface(atlasUV,data);
    if (mode >= 2)
    {
        half3 glow=CityMiragePackedEmission(atlasUV);
        if(mode>=4)
        {
            data.emission=data.albedo*.72h+glow*2.05h;
            data.albedo=0;data.specular=0;data.metallic=0;
        }
        else data.emission=glow*(mode>=3?1.46h:.95h);
    }
}
