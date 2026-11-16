#define InitializeStandardLitSurfaceData InitializeURPLitSurfaceData
#include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
#undef InitializeStandardLitSurfaceData
#include "CityMiragePackedAtlas.hlsl"

inline void InitializeStandardLitSurfaceData(float2 uv, out SurfaceData data)
{
    if (uv.y >= .5)
    {
        CityMiragePackedSurface(uv,data);
        half3 mask=CityMiragePackedEmission(uv);
        if (uv.x < .5 && uv.y > .9375)
        {
            half3 art=CityMiragePackedColor(CityMiragePackedSample(uv,float2(0,1)).rgb);
            data.albedo=0;data.specular=0;data.metallic=0;
            data.emission=art*.72h+mask*2.85h;
        }
        else if (uv.x < .5) data.emission=mask*1.9h;
        else if (uv.x > .75)
        {
            data.albedo=0;data.specular=0;data.metallic=0;
            data.emission=mask*8;
        }
        return;
    }
    uv.y *= 2;
    if (uv.x < .5)
    {
        float2 sampleUV=float2(uv.x,uv.y*.5);
        float wardrobeMirror=0;
        if(floor(uv.x*8)==2 && uv.y<.25)
        {
            float t=saturate((frac(uv.x*8)-.025)/.95)*2;
            wardrobeMirror=step(1,t);
            sampleUV.x=(2+.025+lerp(frac(t),1-frac(t),wardrobeMirror)*.95)/8;
        }
        CityMiragePackedSurface(sampleUV, data);
        data.normalTS.x*=1-2*wardrobeMirror;
        data.normalTS = normalize(half3(data.normalTS.xy * .42h, data.normalTS.z));
        data.occlusion = 1;
        // Reserved prop atlas cell 7 contains the rack and skirting LED mask.
        if (floor(uv.x*8)==3 && uv.y<.5)
        {
            data.albedo = 0;
            data.metallic = 0;
            data.specular = 0;
            data.emission = CityMiragePackedEmission(float2(uv.x,uv.y*.5)) * _EmissionColor.rgb;
        }
        return;
    }
    float x = (uv.x - .5) * 8;
    float slot = clamp(floor(x),0.0,2.0);
    float2 local = saturate((float2(frac(x), uv.y) - .025) / .95);
    float2 repeatCount = slot < .5 ? float2(12,8) : (slot < 1.5 ? float2(18,1) : float2(1,1));
    float2 tilePosition = min(local,.99999) * repeatCount;
    float2 mirror = step(1.0, fmod(floor(tilePosition), 2.0));
    float2 tiled = lerp(frac(tilePosition), 1-frac(tilePosition), mirror);
    tiled = lerp(float2(.002,.002),float2(.998,.998),tiled);
    float2 sampleUV = float2(.5 + (fmod(slot,2.0)+tiled.x)*.25,(floor(slot*.5)+tiled.y)*.5);
    CityMiragePackedSurface(float2(sampleUV.x,sampleUV.y*.5), data);
    data.normalTS.xy *= 1 - 2 * mirror;
    data.smoothness *= .94h;
}




