#ifndef CITY_TEXTURE_ONLY_ROAD_DETAILS
#define CITY_TEXTURE_ONLY_ROAD_DETAILS
float CitySurfaceHash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
#if defined(CITY_TEXTURE_ROAD_ENABLED)
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
// Reuse the artist's existing high-smoothness mask on Road 02 only.
// No new coverage, geometry, texture, transparent pass or reflection camera.
void CityExistingRoadWater(float3 world,float2 uv,inout SurfaceData s){
    float4 st;half4 tint,settings;RoadSettings(RoadSlice(uv),st,tint,settings);
    if(settings.w<.5||settings.w>1.5)return;
    half4 packed=RoadSurface(uv);
    half originalMask=saturate(packed.a/max(settings.y,.001));
    if(originalMask<.20)return;
    half wet=smoothstep(.38,.82,originalMask);
    // Keep opaque road paint readable while retaining its underlying wet sheen.
    half paint= smoothstep(.68,.9,min(s.albedo.r,min(s.albedo.g,s.albedo.b)));
    half body=wet*(1-paint*.65);
    half damp=smoothstep(.20,.55,originalMask);
    s.albedo*=1-damp*.10-body*.16;
    s.metallic=lerp(s.metallic,0,wet);
    s.smoothness=lerp(s.smoothness,lerp(.82,.965,wet),wet);
    // Very shallow, stationary surface ripples; fade fine detail before aliasing.
    float detail=1-smoothstep(.04,.18,length(fwidth(world.xz)));
    float2 ripple=float2(sin(world.x*3.7+world.z*2.1),cos(world.z*4.3-world.x*1.6))*.007*detail;
    s.normalTS=normalize(lerp(s.normalTS,half3(ripple.x,ripple.y,1),wet));
        s.emission*=1-wet*.85;
    half3 waterNormal=normalize(half3(ripple.x,1,ripple.y));
    half3 view=GetWorldSpaceNormalizeViewDir(world);
    half facing=1-saturate(dot(waterNormal,view));
    half fresnel=facing*facing*facing*facing*facing;
    // Reuse the existing environment probe: no planar/SSR pass or extra camera.
    half3 reflected=GlossyEnvironmentReflection(reflect(-view,waterNormal),.055h,1.0h);
    s.emission+=reflected*wet*(.08h+.52h*fresnel);
}

void CityTextureRoadDetails(float3 world,float2 uv,inout SurfaceData s){
half3 a=s.albedo;half chroma=max(a.r,max(a.g,a.b))-min(a.r,min(a.g,a.b));half safe=(1-smoothstep(.065,.20,chroma))*smoothstep(.025,.07,a.r)*(1-smoothstep(.78,.92,a.r));
safe*=1-step(a.r*1.25,a.g)*step(a.b*.95,a.g);float2 cell=floor(world.xz/13);float seed=CitySurfaceHash(cell);float2 delta=world.xz-(cell+.5)*13-(float2(CitySurfaceHash(cell+19),CitySurfaceHash(cell+43))-.5)*5;float angle=seed*6.283185;float2 q=float2(delta.x*cos(angle)-delta.y*sin(angle),delta.x*sin(angle)+delta.y*cos(angle));float2 size=float2(.65+CitySurfaceHash(cell+7)*1.4,.9+CitySurfaceHash(cell+31)*1.7);float aa=max(.009,length(fwidth(world.xz))*.5);float ragged=sin(q.y*5+seed*17)*.06+sin(q.x*8)*.035;float edge=max(abs(q.x)-size.x,abs(q.y)-size.y)+ragged;float patch=(1-smoothstep(-aa,aa,edge))*step(.66,seed);s.albedo*=lerp(1,.46+seed*.08,patch*safe);
float crackD=abs(q.x*.35+sin(q.y*2.1+seed*12)*.25+sin(q.y*6)*.04);float crack=(1-smoothstep(.009,.009+aa,crackD))*(1-smoothstep(2.1,3.4,abs(q.y)))*step(.79,CitySurfaceHash(cell+83));s.albedo*=1-crack*safe*.30;
// Close-range detail uses stable world coordinates and fades tiny grains before they alias.
float nearGrain=1-smoothstep(.025,.11,length(fwidth(world.xz)));float aggregate=CitySurfaceHash(floor(world.xz*18));float patchVariation=sin(q.x*2.7+seed*9)*sin(q.y*3.1+seed*4);s.albedo*=1+patch*safe*(patchVariation*.075+(aggregate-.5)*.16*nearGrain);
float sealedEdge=(1-smoothstep(.022,.022+aa,abs(edge+.018)))*step(.66,seed)*safe;s.albedo*=1-sealedEdge*.30;
float edgeChips=(1-smoothstep(.10,.10+aa,abs(edge)))*step(.78,CitySurfaceHash(floor(world.xz*7)+cell))*step(.66,seed)*safe;s.albedo*=1-edgeChips*.16*nearGrain;
float2 repairQ=q-float2(size.x*.22,-size.y*.18);float2 repairSize=size*float2(.56,.48);float innerEdge=max(abs(repairQ.x)-repairSize.x,abs(repairQ.y)-repairSize.y)+sin(repairQ.y*8+seed)*.025;float layered=(1-smoothstep(-aa,aa,innerEdge))*patch*step(.86,seed)*safe;s.albedo*=1-layered*.12;
float branchDistance=abs(q.y*.5-q.x*.7+.18*sin(q.x*4+seed*11));float branches=(1-smoothstep(.009,.009+aa,branchDistance))*(1-smoothstep(.55,1.25,abs(q.x-.25)))*step(.83,CitySurfaceHash(cell+83))*safe;s.albedo*=1-branches*.23;
s.smoothness=lerp(s.smoothness,.12+seed*.075,patch*safe);
// Secondary repaired-asphalt wear: restrained and stable, restricted by the original asphalt mask.
float stone=CitySurfaceHash(floor(world.xz*27)+41);float speckles=smoothstep(.91,.985,stone)*nearGrain*patch*safe;s.albedo*=1+speckles*.12;
float tarSeal=(1-smoothstep(.018,.018+aa,crackD))*(1-smoothstep(2.0,3.2,abs(q.y)))*step(.86,CitySurfaceHash(cell+83))*safe;s.albedo*=1-tarSeal*.16;
float wornLip=(1-smoothstep(.07,.07+aa,abs(edge+.07)))*patch*safe*(.6+.4*sin(q.y*11+seed*27));s.albedo*=1+wornLip*.10;
float scuff=exp(-dot(q-float2(.23,.35),q-float2(.23,.35))*float2(.6,1.9).x)*step(.83,seed)*patch*safe;s.albedo*=1-scuff*.065;
float2 smallQ=q-float2(-size.x*.28,size.y*.22);float smallAngle=seed*3.5;smallQ=float2(smallQ.x*cos(smallAngle)-smallQ.y*sin(smallAngle),smallQ.x*sin(smallAngle)+smallQ.y*cos(smallAngle));float oval=length(smallQ/float2(.30+seed*.25,.22+seed*.16));float smallRepair=(1-smoothstep(.88,1.0,oval))*patch*step(.91,seed)*safe;s.albedo*=1-smallRepair*.13;
float2 roadLocal=RoadUV(uv);float tireU=abs(frac(roadLocal.x*4)-.34);float streak=(1-smoothstep(.016,.03,tireU))*smoothstep(.55,.85,CitySurfaceHash(floor(world.xz/22)))*(1-smoothstep(3.0,5.0,abs(q.y)));s.albedo*=1-streak*safe*.09;
// Flat cover graphics are shaded directly on existing asphalt, with no mesh or new texture.
float2 largeCell=floor(world.xz/29);float coverSeed=CitySurfaceHash(largeCell+231);float2 cq=world.xz-(largeCell+.5)*29-(float2(CitySurfaceHash(largeCell+71),CitySurfaceHash(largeCell+113))-.5)*15;float radius=length(cq);float cover=(1-smoothstep(.34,.34+aa,radius))*step(.82,coverSeed)*safe;float rim=1-smoothstep(.013,.013+aa,abs(radius-.305));float grooves=(1-smoothstep(.013,.013+aa,abs(frac(cq.x*12)-.5)/12))+(1-smoothstep(.013,.013+aa,abs(frac(cq.y*12)-.5)/12));half3 coverColor=lerp(half3(.13,.14,.15),half3(.055,.06,.065),saturate(grooves*.55));coverColor=lerp(coverColor,half3(.23,.24,.25),rim);s.albedo=lerp(s.albedo,coverColor,cover);s.metallic=lerp(s.metallic,.55,cover);s.smoothness=lerp(s.smoothness,.33,cover);
float longCell=floor(roadLocal.y*40/34);float drainSeed=CitySurfaceHash(float2(longCell,floor(world.x/50)+floor(world.z/50)));float longDelta=roadLocal.y*40-(longCell+.2+drainSeed*.6)*34;float u=frac(roadLocal.x);float sideDistance=min(abs(u-.055),abs(u-.945));float grate=(1-smoothstep(.010,.014,sideDistance))*(1-smoothstep(.34,.40,abs(longDelta)))*step(.32,drainSeed)*safe*step(RoadSlice(uv),.5);float bars=step(.44,frac(longDelta*10));s.albedo=lerp(s.albedo,lerp(half3(.045,.05,.055),half3(.21,.22,.23),bars),grate);s.metallic=lerp(s.metallic,.50,grate);s.smoothness=lerp(s.smoothness,.30,grate);
CityExistingRoadWater(world,uv,s);
}
#else
void CityTextureRoadDetails(float3 world,float2 uv,inout SurfaceData s){} 
#endif
#endif













