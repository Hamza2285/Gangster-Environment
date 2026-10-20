Shader "City/Mobile Garden Utility Atlas"{
Properties{_GardenPebbles("Seamless garden gravel",2D)="white"{} _RoadBaseArray("Existing road surfaces",2DArray)=""{} _GrassDetail("Existing grass detail",2D)="white"{} _Atlas("Shared PNG atlas",2D)="white"{} _Cutoff("Cutout",Range(0,1))=.32 _Wind("Wind strength",Float)=.07}
SubShader{Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest"} Cull Off ZWrite On
HLSLINCLUDE
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
TEXTURE2D_ARRAY(_RoadBaseArray);SAMPLER(sampler_RoadBaseArray);TEXTURE2D(_GardenPebbles);SAMPLER(sampler_GardenPebbles);TEXTURE2D(_Atlas);SAMPLER(sampler_Atlas);TEXTURE2D(_GrassDetail);SAMPLER(sampler_GrassDetail);
CBUFFER_START(UnityPerMaterial)
float _Cutoff,_Wind;
CBUFFER_END
float _CityGardenPreviewTime,_CityGardenPreviewEnabled;
float4 AtlasRect(float tile){
if(tile<.5)return float4(.019,.773,.227,.216);
if(tile<1.5)return float4(.270,.770,.219,.215);
if(tile<2.5)return float4(.498,.803,.259,.155);
if(tile<3.5)return float4(.784,.784,.194,.192);
if(tile<4.5)return float4(.012,.531,.238,.232);
if(tile<5.5)return float4(.254,.530,.246,.243);
if(tile<6.5)return float4(.498,.529,.258,.242);
if(tile<7.5)return float4(.756,.519,.243,.239);
if(tile<8.5)return float4(.010,.312,.242,.176);
if(tile<9.5)return float4(.255,.282,.243,.247);
if(tile<10.5)return float4(.505,.284,.249,.244);
if(tile<11.5)return float4(.7735,.3134,.197,.1954);
if(tile<12.5)return float4(.0223,.0463,.2161,.2225);
if(tile<13.5)return float4(.273,.040,.211,.233);
if(tile<14.5)return float4(.551,.0128,.144,.2648);
return float4(.755,.0144,.226,.264);
}
struct A{float4 p:POSITION;float3 n:NORMAL;float2 uv:TEXCOORD0;float2 info:TEXCOORD1;float2 plot:TEXCOORD3;float2 host:TEXCOORD4;float4 color:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID};
struct V{float4 p:SV_POSITION;float3 n:TEXCOORD0;float2 uv:TEXCOORD1;float2 info:TEXCOORD2;float4 color:TEXCOORD3;float2 fogY:TEXCOORD4;float4 rect:TEXCOORD5;float2 grassUV:TEXCOORD6;float4 plot:TEXCOORD7;UNITY_VERTEX_INPUT_INSTANCE_ID};
float TimeValue(){return lerp(_Time.y,_CityGardenPreviewTime,_CityGardenPreviewEnabled);}
V Vert(A i){V o;UNITY_SETUP_INSTANCE_ID(i);UNITY_TRANSFER_INSTANCE_ID(i,o);float3 w=TransformObjectToWorld(i.p.xyz);float time=TimeValue();float phase=i.color.g*6.283+w.x*.17+w.z*.11;if(i.info.x<19.5)w.xz+=float2(sin(time*1.4+phase),cos(time*1.1+phase))*(i.color.r*i.color.r*_Wind);if(i.color.r<-.5){float life=frac(time*.72+i.color.g);w.xz+=i.plot*life;w.y+=1.9*life-3.1*life*life;}o.p=TransformWorldToHClip(w);o.n=TransformObjectToWorldNormal(i.n);o.grassUV=w.xz*.25;o.plot=float4(i.plot,i.host);o.uv=i.uv;float tile=i.color.a<.999?floor(i.color.a*16):i.info.x;o.rect=AtlasRect(tile);o.info=float2(tile,tile>12.5&&tile<13.5?1:tile>13.5&&tile<14.5?3:tile>14.5?2:0);o.color=i.color;o.fogY=float2(ComputeFogFactor(o.p.z),w.y);return o;}

half4 GardenSurface(V i){if(any(i.plot.xy<1))return SAMPLE_TEXTURE2D(_Atlas,sampler_Atlas,i.rect.xy+lerp(.01,.99,i.uv)*i.rect.zw);
float2 size=max(i.plot.xy,float2(1,1)),p=(i.uv-.5)*size;
float diagonal=min(abs(p.x*size.y-p.y*size.x),abs(p.x*size.y+p.y*size.x))/length(size);
float width=clamp(min(size.x,size.y)*.058,2.3,4.5);
float border=min(min(i.uv.x,1-i.uv.x)*size.x,min(i.uv.y,1-i.uv.y)*size.y);
float borderWidth=clamp(min(size.x,size.y)*.032,1.4,2.4);
float radius=min(size.x,size.y)*.15,dist=length(p);
float path=max(1-smoothstep(width*.5-.045,width*.5+.045,diagonal),1-smoothstep(borderWidth-.045,borderWidth+.045,border));
path=max(path,1-smoothstep(radius-.045,radius+.045,dist));
float2 direction=normalize(float2(size.x,p.x*p.y>=0?size.y:-size.y));float2 stoneUV=float2(dot(p,direction),dot(p,float2(-direction.y,direction.x)))*2;float ringZone=1-smoothstep(radius+.03,radius+.12,dist);float2 ringUV=float2(atan2(p.y,p.x)*radius*2,dist*2);stoneUV=lerp(stoneUV,ringUV,ringZone);
stoneUV.x+=floor(stoneUV.y)*.5;
float2 cell=frac(stoneUV);
float4 rect=AtlasRect(12);
half3 stone=SAMPLE_TEXTURE2D_GRAD(_Atlas,sampler_Atlas,rect.xy+lerp(.01,.99,cell)*rect.zw,ddx(stoneUV)*rect.zw*.98,ddy(stoneUV)*rect.zw*.98).rgb;
float seam=min(min(cell.x,1-cell.x),min(cell.y,1-cell.y));
float seamAA=max(fwidth(seam),.004);stone*=lerp(.63,1,smoothstep(.012-seamAA,.012+seamAA,seam));stone*=half3(1.03,.98,.90);
float plaza=1-smoothstep(radius*.77-.04,radius*.77+.04,dist);
float circular=1-smoothstep(radius-.05,radius+.05,dist);float band=frac(dist/.58);half3 blocks=lerp(stone, dot(stone,half3(.3,.59,.11))*half3(.62,.66,.69),.82);float edging=step(.82,dist/radius);blocks=lerp(blocks,stone*.87,edging);float variation=frac(sin(floor(stoneUV.x)*12.9898+floor(stoneUV.y)*78.233)*43758.5453);blocks*=.89+variation*.20;half3 gravel=saturate((SAMPLE_TEXTURE2D(_GardenPebbles,sampler_GardenPebbles,i.grassUV*1.3333).rgb-.45)*1.14+.45)*.94;stone=lerp(gravel,blocks,circular);
half3 grass=SAMPLE_TEXTURE2D(_GrassDetail,sampler_GrassDetail,i.grassUV).rgb*.95;
return half4(lerp(grass,stone,path),1);
}

float3 PixelWorld(V i){return float3(i.grassUV.x*4,i.fogY.y,i.grassUV.y*4);}
half FoliageFacing(V i){if(i.info.x<3.5||i.info.x>10.5)return 1;return smoothstep(.16,.68,abs(dot(normalize(i.n),normalize(_WorldSpaceCameraPos-PixelWorld(i)))));}
half GardenPlantMask(V i){if(i.info.x<3.5||i.info.x>8.5||any(i.plot.xy<1))return 1;float2 size=i.plot.xy,p=(i.plot.zw-.5)*size;float diagonal=min(abs(p.x*size.y-p.y*size.x),abs(p.x*size.y+p.y*size.x))/length(size);float border=min(min(i.plot.z,1-i.plot.z)*size.x,min(i.plot.w,1-i.plot.w)*size.y);float clearance=min(min(diagonal-clamp(min(size.x,size.y)*.058,2.3,4.5)*.5,border-clamp(min(size.x,size.y)*.032,1.4,2.4)),length(p)-min(size.x,size.y)*.15);return smoothstep(.16,.24,clearance);}

float WaterHash(float2 p){float3 q=frac(float3(p.x,p.y,p.x)*.1031);q+=dot(q,q.yzx+33.33);return frac((q.x+q.y)*q.z);}
float WaterNoise(float2 p){float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(WaterHash(a),WaterHash(a+float2(1,0)),f.x),lerp(WaterHash(a+float2(0,1)),WaterHash(a+1),f.x),f.y);}

half FlowCoverage(V i){if(i.info.x<14.5)return 1;if(i.color.g<0){float pattern=fmod(floor(i.p.x)+floor(i.p.y)*2,4);return pattern<1?0:1;}float time=TimeValue();float u=i.uv.x*3+sin(i.uv.x*17+i.grassUV.x*1.7+i.grassUV.y*.9)*.13+sin(i.uv.y*21-time*5)*.025;float strand=1-smoothstep(.14,.34,abs(frac(u)-.5));float drops=step(.91,frac(sin(floor(i.uv.x*35)*12.9898+floor(i.uv.y*30+time*8)*78.233)*43758.5453));return max(strand,drops*.65);}
half3 FountainWater(V i,half3 baseColor,Light light){float time=TimeValue();float2 w=PixelWorld(i).xz;float2 d=w-i.plot.zw;float distance=length(d);float radius=max(i.plot.x,.2);float wave1=sin(w.x*4+w.y*2-time*1.8),wave2=cos(w.y*5-w.x*1.6+time*1.3);half3 normal=normalize(half3(wave1*.13,1,wave2*.13));half3 view=normalize(_WorldSpaceCameraPos-PixelWorld(i));half3 halfway=normalize(view+light.direction);half shine=pow(saturate(dot(normal,halfway)),72)*light.shadowAttenuation;half fresnel=pow(1-saturate(dot(view,normal)),4);half3 water=lerp(half3(.025,.19,.24),half3(.055,.42,.49),saturate(dot(baseColor,half3(.3,.59,.11))*1.3));water=lerp(water,baseColor*.65,.35);water+=shine*light.color*.95+fresnel*half3(.15,.23,.25);float agitation=sin(w.x*13+time*2.2)*cos(w.y*17-time*1.7);float ring=exp(-abs(distance-i.plot.y+agitation*.085)*5.5);float edge=exp(-abs(distance-radius)*11)*.35;float ripples=pow(saturate(sin(distance*24-time*4+sin(w.x*3+w.y*2)*.55)),12)*exp(-abs(distance-i.plot.y)*2.8);float flecks=smoothstep(.43,.76,WaterNoise(w*18+float2(time*.7,-time*.9)));float foam=saturate((ring+edge)*(.12+flecks*.60)+ripples*.16);float2 fine=frac(i.uv*2+float2(-time*.012,time*.016));half3 detail=SAMPLE_TEXTURE2D(_Atlas,sampler_Atlas,i.rect.xy+lerp(.01,.99,fine)*i.rect.zw).rgb;water*=.88+dot(detail,half3(.3,.59,.11))*.3;water+=pow(saturate(agitation),18)*shine*.22;return lerp(water,half3(.88,.97,.96),foam);}

half3 FountainStone(V i){float3 w=PixelWorld(i);float2 d=w.xz-i.plot.zw;float radius=length(d);float theta=atan2(d.y,d.x);float2 mapped=float2(theta*max(radius,.35)*1.6,w.y*3.2);if(abs(i.n.y)>.6)mapped.y=radius*2.1;mapped.x+=floor(mapped.y)*.5;float2 cell=frac(mapped);float4 rect=AtlasRect(12);half3 stone=SAMPLE_TEXTURE2D_GRAD(_Atlas,sampler_Atlas,rect.xy+lerp(.04,.96,cell)*rect.zw,clamp(ddx(mapped),-.2,.2)*rect.zw*.92,clamp(ddy(mapped),-.2,.2)*rect.zw*.92).rgb;float joint=min(min(cell.x,1-cell.x),min(cell.y,1-cell.y));stone*=lerp(.55,1,smoothstep(.008,.032,joint));float variation=frac(sin(floor(mapped.x)*12.9898+floor(mapped.y)*78.233)*43758.5453);stone*=half3(.80,.75,.65)*(.86+variation*.20);float damp=exp(-abs(w.y-i.plot.y)*4);return stone*(1-damp*.20);}

// Sports geometry reuses this existing material and its existing turf / stone textures.
// Only new meshes with UV1 tile codes >=20 enter this branch.
void SportsClip(V i){if(i.info.x>23.5&&i.info.x<25.5){float2 q=i.uv*(i.info.x<24.5?5:7);float2 p=i.info.x<24.5?float2(q.x+q.y,q.x-q.y):q;float2 f=abs(frac(p+.5)-.5);float wireDistance=min(f.x,f.y);clip(.045-wireDistance);}}
half4 SportsSurface(V i){SportsClip(i);half3 c=i.color.rgb;
if(i.info.x<20.5){half3 turf=SAMPLE_TEXTURE2D(_GrassDetail,sampler_GrassDetail,i.uv*.55).rgb;half gray=dot(turf,half3(.3,.59,.11));c*=lerp(.55,1.35,gray);c*=.94+.12*WaterNoise(i.uv*35);}
else if(i.info.x<23.5){float4 rect=AtlasRect(12);float2 tex=frac(i.uv*.60);half3 stone=SAMPLE_TEXTURE2D(_Atlas,sampler_Atlas,rect.xy+lerp(.04,.96,tex)*rect.zw).rgb;half gray=dot(stone,half3(.3,.59,.11));c*=lerp(.76,1.12,gray);float stain=WaterNoise(i.uv*.34);c*=.94+.10*stain;}
if(i.info.x>25.5){float4 leatherRect=AtlasRect(12);float2 ballUV=frac(i.uv*float2(35,18));half3 grain=SAMPLE_TEXTURE2D(_Atlas,sampler_Atlas,leatherRect.xy+lerp(.04,.96,ballUV)*leatherRect.zw).rgb;c*=.90+.16*dot(grain,half3(.3,.59,.11));if(i.info.x>26.5){float lon=i.uv.x*6.2831853,lat=i.uv.y*3.14159265;float3 sp=float3(sin(lat)*cos(lon),cos(lat),sin(lat)*sin(lon));float seamDistance=min(min(abs(sp.x),abs(sp.y)),abs(abs(sp.z)-(.63-.30*sp.y*sp.y)));float seamAA=max(fwidth(seamDistance),.002);float seam=1-smoothstep(.013-seamAA,.019+seamAA,seamDistance);float2 pebbleUV=i.uv*float2(104,52);pebbleUV.x+=fmod(floor(pebbleUV.y),2)*.5;float pebble=1-smoothstep(.18,.43,length(frac(pebbleUV)-.5));float grainVisibility=1-smoothstep(.28,.85,max(fwidth(pebbleUV.x),fwidth(pebbleUV.y)));c*=lerp(1,.86+.24*pebble,grainVisibility);c=lerp(c,half3(.018,.014,.010),seam);}}
Light l=GetMainLight(TransformWorldToShadowCoord(PixelWorld(i)));half3 n=normalize(i.n);half3 lighting=max(.32,SampleSH(n)*.65+l.color*(.32+.65*saturate(dot(n,l.direction)))*l.shadowAttenuation);half3 lit=c*lighting;if(i.info.x>25.5){half3 view=normalize(_WorldSpaceCameraPos-PixelWorld(i));half shine=pow(saturate(dot(n,normalize(view+l.direction))),30)*.12;lit+=shine*l.color*l.shadowAttenuation;}return half4(MixFog(lit,i.fogY.x),1);}
half4 Frag(V i):SV_Target{if(i.info.x>19.5)return SportsSurface(i);UNITY_SETUP_INSTANCE_ID(i);float time=TimeValue();float2 local=i.uv;float2 dx=ddx(local)*i.rect.zw*.98,dy=ddy(local)*i.rect.zw*.98;
if(i.info.y>.5&&i.info.y<1.5)local=frac(local+float2(time*.018,time*.012)+sin(local.yx*12+time)*.015);
else if(i.info.y>1.5){local.y=frac(local.y+time*(i.info.y>2.5?-.35:.28));}
float2 uv=i.rect.xy+lerp(.01,.99,local)*i.rect.zw;
half4 c;if(i.info.x>10.5&&i.info.x<11.5)c=GardenSurface(i);else c=SAMPLE_TEXTURE2D_GRAD(_Atlas,sampler_Atlas,uv,dx,dy);if(i.info.y>1.5){float2 outlineUV=i.rect.xy+lerp(.01,.99,i.uv)*i.rect.zw;half4 outline=SAMPLE_TEXTURE2D_GRAD(_Atlas,sampler_Atlas,outlineUV,dx,dy);c.rgb=lerp(outline.rgb,c.rgb,c.a);c.a=outline.a;}if(i.info.x>11.5&&i.info.x<12.5&&i.plot.x<-.5)c=half4(FountainStone(i),1);c.a*=FoliageFacing(i)*GardenPlantMask(i)*FlowCoverage(i);clip(c.a-_Cutoff);if(i.info.x<3.5){c.rgb=lerp(dot(c.rgb,half3(.3,.59,.11)),c.rgb,.72)*.92;float slice=floor(i.plot.z/32);float2 hostUV=float2(i.plot.z-slice*32-16,i.plot.w);half3 surface=SAMPLE_TEXTURE2D_ARRAY(_RoadBaseArray,sampler_RoadBaseArray,hostUV,slice).rgb;float edge=min(min(i.uv.x,1-i.uv.x),min(i.uv.y,1-i.uv.y));float rim=i.info.x<1.5?smoothstep(.78,.99,length((i.uv-.5)*2)):1-smoothstep(.015,.12,edge);float wear=(.5+.5*sin(i.uv.x*73+i.uv.y*91+i.color.g*17));float blend=rim*(.72+.20*wear);c.rgb=lerp(c.rgb,surface*.87,blend);}Light light=GetMainLight(TransformWorldToShadowCoord(PixelWorld(i)));half3 n=normalize(i.n+(i.info.x>3.5&&i.info.x<10.5?float3(0,.7,0):float3(0,0,0)));half3 illumination=max(.38,SampleSH(n)*.65+light.color*(.45+.45*abs(dot(n,light.direction)))*light.shadowAttenuation);c.rgb*=illumination*i.color.b;
if(i.info.x>12.5&&i.info.x<13.5)c.rgb=FountainWater(i,c.rgb,light);if(i.info.y>1.5)c.rgb=lerp(c.rgb,half3(.65,.84,.87),.10)*(.88+.12*sin(time*3+local.y*21));
c.rgb=MixFog(c.rgb,i.fogY.x);return half4(c.rgb,1);}

void ClipOutline(V i){if(i.info.x>19.5){SportsClip(i);return;}if(i.info.x>10.5&&i.info.x<11.5)return;float2 uv=i.rect.xy+lerp(.01,.99,i.uv)*i.rect.zw;half a=SAMPLE_TEXTURE2D(_Atlas,sampler_Atlas,uv).a;clip(a*FoliageFacing(i)*GardenPlantMask(i)*FlowCoverage(i)-_Cutoff);}
half4 SportsShadow(V i):SV_Target{clip(i.info.x-25.5);return 0;}
half4 DepthFrag(V i):SV_Target{ClipOutline(i);return 0;}
half4 NormFrag(V i):SV_Target{ClipOutline(i);return half4(normalize(i.n),0);}
ENDHLSL

Pass{Name "Forward" Tags{"LightMode"="UniversalForward"}
HLSLPROGRAM
#pragma target 3.5
#pragma vertex Vert
#pragma fragment Frag
#pragma multi_compile_fog
#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
#pragma multi_compile_fragment _ _SHADOWS_SOFT
#pragma multi_compile_instancing
ENDHLSL
}
Pass{Name "SportsBallShadow" Tags{"LightMode"="ShadowCaster"} ColorMask 0 ZWrite On ZTest LEqual Cull Back
HLSLPROGRAM
#pragma target 3.5
#pragma vertex Vert
#pragma fragment SportsShadow
#pragma multi_compile_instancing
ENDHLSL
}
Pass{Name "DepthOnly" Tags{"LightMode"="DepthOnly"} ColorMask 0 ZWrite On ZTest LEqual
HLSLPROGRAM
#pragma target 3.5
#pragma vertex Vert
#pragma fragment DepthFrag
#pragma multi_compile_instancing
ENDHLSL
}
Pass{Name "DepthNormals" Tags{"LightMode"="DepthNormals"} ZWrite On ZTest LEqual
HLSLPROGRAM
#pragma target 3.5
#pragma vertex Vert
#pragma fragment NormFrag
#pragma multi_compile_instancing
ENDHLSL
}
}}









