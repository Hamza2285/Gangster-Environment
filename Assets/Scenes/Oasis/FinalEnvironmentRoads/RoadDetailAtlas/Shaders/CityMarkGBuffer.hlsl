#include "Assets/Scenes/Oasis/RoadAtlas/Shaders/TextureOnlyRoadDetails.hlsl"
TEXTURE2D(_MarkAtlas); SAMPLER(sampler_MarkAtlas);

float CitySegment(float2 p,float2 a,float2 b){float2 v=b-a;return 1-smoothstep(.065,.085,length(p-a-v*saturate(dot(p-a,v)/dot(v,v))));}
float CityDigit(float2 p,int digit){float a=CitySegment(p,float2(-.22,.4),float2(.22,.4)),b=CitySegment(p,float2(.25,.36),float2(.25,.04)),c=CitySegment(p,float2(.25,-.04),float2(.25,-.36)),d=CitySegment(p,float2(-.22,-.4),float2(.22,-.4)),e=CitySegment(p,float2(-.25,-.04),float2(-.25,-.36)),f=CitySegment(p,float2(-.25,.04),float2(-.25,.36)),g=CitySegment(p,float2(-.22,0),float2(.22,0));return saturate(digit==0?a+b+c+d+e+f:digit==5?a+f+g+c+d:a+f+g+e+c+d);}

void ApplyCityMarks(float4 markUV,float3 worldPosition,inout SurfaceData surfaceData,half coverage){if(markUV.z< -2000&&markUV.z> -2200&&markUV.w< -5.5){if(markUV.x>2.5){half kind=markUV.x;half3 finish=kind<3.5?half3(.025,.18,.72):kind<4.5?half3(.025,.07,.25):kind<5.5?half3(.62,.67,.72):kind<6.5?half3(.16,.36,.52):half3(.015,.72,1);surfaceData.albedo=finish;surfaceData.metallic=kind>5.5&&kind<6.5?.06:.12;surfaceData.smoothness=kind>5.5&&kind<6.5?.82:.48;surfaceData.emission=kind>6.5?finish*1.6*lerp(.55,1,saturate(_GangstarNight)):finish*markUV.y*.20;surfaceData.occlusion=1;surfaceData.normalTS=half3(0,0,1);return;}surfaceData.emission+=surfaceData.albedo*markUV.y*lerp(.35,1,saturate(_GangstarNight));surfaceData.normalTS=half3(0,0,1);surfaceData.occlusion=1;if(markUV.x>.5&&markUV.x<1.5){surfaceData.metallic=.85;surfaceData.smoothness=.78;}else if(markUV.x>1.5){surfaceData.metallic=0;surfaceData.smoothness=.42;float grain=frac(sin(dot(floor(worldPosition.xz*40),float2(17.1,93.7)))*4375.5);surfaceData.albedo*=.94+grain*.12;}else{surfaceData.metallic=.05;surfaceData.smoothness=.36;}return;} if(_ParkingSize.w>0){markUV=float4((worldPosition.z-_ParkingBay.z)/_ParkingSize.x+.5,(worldPosition.x-_ParkingBay.x)/_ParkingSize.y+.5,6,-1);if(abs(worldPosition.y-_ParkingBay.y)>.1)markUV.x=100;}
float2 paintDx=ddx(markUV.xy),paintDy=ddy(markUV.xy);float2 gradientScale=float2(.954,.954);bool stamp=markUV.w<0;float tile=stamp?round(markUV.z):floor(markUV.x/32.0);float2 local=stamp?markUV.xy:float2(saturate(markUV.x-tile*32.0-16.0),frac(markUV.y));bool inside=all(local>=0)&&all(local<=1);local=lerp(float2(.023,.023),float2(.977,.977),local);if(tile>20.5){gradientScale*=float2(.166016,.664062);tile=1;local=float2(.03418,.16992)+saturate(local)*float2(.166016,.664062);}else if(tile>16.5){gradientScale*=.5;float q=tile-17;tile=14;local=float2(fmod(q,2)*.5,q<2?.5:0)+saturate(local)*.5;}else if(tile>15.5){gradientScale*=float2(.35,.13);tile=7;local=float2(.075,.77)+saturate(local)*float2(.35,.13);}
// Atlas pixels are top-left indexed; clamp inside each padded cell to avoid mip bleeding.
float2 atlasUV=(float2(fmod(tile,4.0),3.0-floor(tile/4.0))+local)/4.0;
half4 paint=SAMPLE_TEXTURE2D_GRAD(_MarkAtlas,sampler_MarkAtlas,atlasUV,paintDx*gradientScale*.25,paintDy*gradientScale*.25);if(stamp&&markUV.z>=17&&markUV.z<=21)paint.a=saturate(paint.a*1.22);if(tile<.5&&paint.r>.6&&paint.g<.35&&paint.b<.35){float colour=fmod(round(markUV.w)*13,3);paint.rgb=colour<.5?half3(.8,.12,.1):colour<1.5?half3(.12,.35,.85):half3(.1,.62,.3);}
 bool speedArea=(abs(local.x-.342)<.070&&abs(local.y-.326)<.070)||(abs(local.x-.664)<.070&&abs(local.y-.674)<.070);
 if(!stamp&&tile<.5&&speedArea){bool sixty=local.x>.5;float2 p=(local-(sixty?float2(.664,.674):float2(.342,.326)))/.058;if(sixty)p=-p;float ring=1-smoothstep(.065,.085,abs(length(p)-.83));float2 d=p/ .85;float digits=max(CityDigit((d-float2(-.33,0))*float2(1.8,1),5),CityDigit((d-float2(.33,0))*float2(1.8,1),0));paint.rgb=lerp(half3(.85,.13,.1),half3(.92,.92,.88),digits);paint.a=max(ring,digits);if(floor(markUV.y)!=floor(round(markUV.w)*.5))paint.a=0;}
if(!stamp&&markUV.z < -823.5&&tile<.5&&speedArea)paint.a=0;
if(stamp&&(!inside||markUV.w < -4.0))paint.a=0;
 paint.a*=coverage;surfaceData.emission=lerp(surfaceData.emission,half3(0,0,0),paint.a);surfaceData.albedo=lerp(surfaceData.albedo,paint.rgb,paint.a);surfaceData.metallic=lerp(surfaceData.metallic,0,paint.a);surfaceData.smoothness=lerp(surfaceData.smoothness,.22,paint.a);
}
#ifndef UNIVERSAL_LIT_GBUFFER_PASS_INCLUDED
#define UNIVERSAL_LIT_GBUFFER_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"
#if defined(LOD_FADE_CROSSFADE)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

// TODO: Currently we support viewDirTS caclulated in vertex shader and in fragments shader.
// As both solutions have their advantages and disadvantages (etc. shader target 2.0 has only 8 interpolators).
// We need to find out if we can stick to one solution, which we needs testing.
// So keeping this until I get manaul QA pass.
#if defined(_PARALLAXMAP) && (SHADER_TARGET >= 30)
#define REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR
#endif

#if (defined(_NORMALMAP) || (defined(_PARALLAXMAP) && !defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR))) || defined(_DETAIL)
#define REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR
#endif

// keep this file in sync with LitForwardPass.hlsl

struct Attributes
{
    float4 positionOS   : POSITION;
    float3 normalOS     : NORMAL;
    float4 tangentOS    : TANGENT;
    float2 texcoord     : TEXCOORD0;
    float4 markUV : TEXCOORD3;
float4 siteUV : TEXCOORD4;
    float2 staticLightmapUV   : TEXCOORD1;
    float2 dynamicLightmapUV  : TEXCOORD2;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float2 uv                       : TEXCOORD0;
    float4 markUV : TEXCOORD10;
float4 siteUV : TEXCOORD11;

#if defined(REQUIRES_WORLD_SPACE_POS_INTERPOLATOR)
    float3 positionWS               : TEXCOORD1;
#endif

    half3 normalWS                  : TEXCOORD2;
#if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR)
    half4 tangentWS                 : TEXCOORD3;    // xyz: tangent, w: sign
#endif
#ifdef _ADDITIONAL_LIGHTS_VERTEX
    half3 vertexLighting            : TEXCOORD4;    // xyz: vertex lighting
#endif

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    float4 shadowCoord              : TEXCOORD5;
#endif

#if defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
    half3 viewDirTS                 : TEXCOORD6;
#endif

    DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 7);
#ifdef DYNAMICLIGHTMAP_ON
    float2  dynamicLightmapUV       : TEXCOORD8; // Dynamic lightmap UVs
#endif

    float4 positionCS               : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

void InitializeInputData(Varyings input, half3 normalTS, out InputData inputData)
{
    inputData = (InputData)0;

    #if defined(REQUIRES_WORLD_SPACE_POS_INTERPOLATOR)
        inputData.positionWS = input.positionWS;
    #endif

    inputData.positionCS = input.positionCS;
    half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
    #if defined(_NORMALMAP) || defined(_DETAIL)
        float sgn = input.tangentWS.w;      // should be either +1 or -1
        float3 bitangent = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
        inputData.normalWS = TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangent.xyz, input.normalWS.xyz));
    #else
        inputData.normalWS = input.normalWS;
    #endif

    inputData.normalWS = NormalizeNormalPerPixel(inputData.normalWS);
    inputData.viewDirectionWS = viewDirWS;

    #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
        inputData.shadowCoord = input.shadowCoord;
    #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
        inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);
    #else
        inputData.shadowCoord = float4(0, 0, 0, 0);
    #endif

    inputData.fogCoord = 0.0; // we don't apply fog in the guffer pass

    #ifdef _ADDITIONAL_LIGHTS_VERTEX
        inputData.vertexLighting = input.vertexLighting.xyz;
    #else
        inputData.vertexLighting = half3(0, 0, 0);
    #endif

#if defined(DYNAMICLIGHTMAP_ON)
    inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.dynamicLightmapUV, input.vertexSH, inputData.normalWS);
#else
    inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, inputData.normalWS);
#endif

    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
}

///////////////////////////////////////////////////////////////////////////////
//                  Vertex and Fragment functions                            //
///////////////////////////////////////////////////////////////////////////////

// Used in Standard (Physically Based) shader
Varyings LitGBufferPassVertex(Attributes input)
{
    Varyings output = (Varyings)0;
    output.markUV = input.markUV; output.siteUV = input.siteUV;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);

    // normalWS and tangentWS already normalize.
    // this is required to avoid skewing the direction during interpolation
    // also required for per-vertex lighting and SH evaluation
    VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);

    output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);

    // already normalized from normal transform to WS.
    output.normalWS = normalInput.normalWS;

    #if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR) || defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
        real sign = input.tangentOS.w * GetOddNegativeScale();
        half4 tangentWS = half4(normalInput.tangentWS.xyz, sign);
    #endif

    #if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR)
        output.tangentWS = tangentWS;
    #endif

    #if defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
        half3 viewDirWS = GetWorldSpaceNormalizeViewDir(vertexInput.positionWS);
        half3 viewDirTS = GetViewDirectionTangentSpace(tangentWS, output.normalWS, viewDirWS);
        output.viewDirTS = viewDirTS;
    #endif

    OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
#ifdef DYNAMICLIGHTMAP_ON
    output.dynamicLightmapUV = input.dynamicLightmapUV.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
#endif
    OUTPUT_SH(output.normalWS.xyz, output.vertexSH);

    #ifdef _ADDITIONAL_LIGHTS_VERTEX
        half3 vertexLight = VertexLighting(vertexInput.positionWS, normalInput.normalWS);
        output.vertexLighting = vertexLight;
    #endif

    #if defined(REQUIRES_WORLD_SPACE_POS_INTERPOLATOR)
        output.positionWS = vertexInput.positionWS;
    #endif

    #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
        output.shadowCoord = GetShadowCoord(vertexInput);
    #endif

    output.positionCS = vertexInput.positionCS;

    return output;
}

// Used in Standard (Physically Based) shader
FragmentOutput LitGBufferPassFragment(Varyings input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

#if defined(_PARALLAXMAP)
    #if defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
        half3 viewDirTS = input.viewDirTS;
    #else
        half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
        half3 viewDirTS = GetViewDirectionTangentSpace(input.tangentWS, input.normalWS, viewDirWS);
    #endif
    ApplyPerPixelDisplacement(viewDirTS, input.uv);
#endif

    SurfaceData surfaceData;
    InitializeStandardLitSurfaceData(input.uv, surfaceData);
    half mainCoverage=(input.siteUV.w<-.5&&all(input.siteUV.xy>=0)&&all(input.siteUV.xy<=1))?0:1;ApplyCityMarks(input.markUV,input.positionWS,surfaceData,mainCoverage);if(input.siteUV.w<-.5)ApplyCityMarks(input.siteUV,input.positionWS,surfaceData,1);CityTextureRoadDetails(input.positionWS,input.uv,surfaceData);

#ifdef LOD_FADE_CROSSFADE
    LODFadeCrossFade(input.positionCS);
#endif

    InputData inputData;
    InitializeInputData(input, surfaceData.normalTS, inputData);
    SETUP_DEBUG_TEXTURE_DATA_FOR_TEX(inputData, input.uv, _BaseMap);

#ifdef _DBUFFER
    ApplyDecalToSurfaceData(input.positionCS, surfaceData, inputData);
#endif

    // Stripped down version of UniversalFragmentPBR().

    // in LitForwardPass GlobalIllumination (and temporarily LightingPhysicallyBased) are called inside UniversalFragmentPBR
    // in Deferred rendering we store the sum of these values (and of emission as well) in the GBuffer
    BRDFData brdfData;
    InitializeBRDFData(surfaceData.albedo, surfaceData.metallic, surfaceData.specular, surfaceData.smoothness, surfaceData.alpha, brdfData);

    Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, inputData.shadowMask);
    MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI, inputData.shadowMask);
    half3 color = GlobalIllumination(brdfData, inputData.bakedGI, surfaceData.occlusion, inputData.positionWS, inputData.normalWS, inputData.viewDirectionWS);

    return BRDFDataToGbuffer(brdfData, inputData, surfaceData.smoothness, surfaceData.emission + color, surfaceData.occlusion);
}

#endif






























