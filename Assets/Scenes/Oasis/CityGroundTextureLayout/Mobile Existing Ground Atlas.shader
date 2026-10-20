Shader "City/Mobile Existing Ground Atlas" {
Properties{_BaseMap("Existing single ground atlas",2D)="white"{} [HideInInspector]_PlotRect("Plot",Vector)=(0,0,0,0) [HideInInspector]_GroundData("Layout",Vector)=(0,0,0,0) [HideInInspector]_Feature0("Feature",Vector)=(0,0,0,0) [HideInInspector]_Feature1("Access",Vector)=(0,0,0,0)}
SubShader{Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+3"} Cull Back ZWrite On
HLSLINCLUDE
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
CBUFFER_START(UnityPerMaterial)
float4 _PlotRect,_GroundData,_Feature0,_Feature1,_Fuel0,_Fuel1;float4 _GroundOutline[48],_GroundFacades[48];float4 _PatchRect[12],_PatchMode[12];
CBUFFER_END
struct A{float4 p:POSITION;float3 n:NORMAL;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
struct V{float4 p:SV_POSITION;float3 w:TEXCOORD0;float3 n:TEXCOORD1;float2 uv:TEXCOORD2;float fog:TEXCOORD3;UNITY_VERTEX_INPUT_INSTANCE_ID};
V Vert(A i){V o;UNITY_SETUP_INSTANCE_ID(i);UNITY_TRANSFER_INSTANCE_ID(i,o);o.w=TransformObjectToWorld(i.p.xyz);o.p=TransformWorldToHClip(o.w);o.n=TransformObjectToWorldNormal(i.n);o.uv=i.uv;o.fog=ComputeFogFactor(o.p.z);return o;}
float BoxSDF(float2 p,float2 halfsize){float2 q=abs(p)-halfsize;return length(max(q,0))+min(max(q.x,q.y),0);}
float RoundedBox(float2 p,float2 halfsize,float radius){return BoxSDF(p,max(halfsize-radius,.05))-radius;}
float Ellipse(float2 p,float2 radius){return (length(p/max(radius,.1))-1)*min(radius.x,radius.y);}
float Arrow(float2 p){float shaft=BoxSDF(p-float2(0,-.35),float2(.17,.65));float head=max(abs(p.x)*.9+p.y*.9-.70,-p.y);return min(shaft,head);}
float SegmentDistance(float2 p,float2 a,float2 b){float2 d=b-a;return length(p-a-d*saturate(dot(p-a,d)/dot(d,d)));}
float GroundSegmentDistanceSquared(float2 p,float2 a,float2 b){float2 d=b-a;float2 q=p-a-d*saturate(dot(p-a,d)/max(dot(d,d),.0001));return dot(q,q);}
float FuelTurnArrow(float2 p){float shaft=min(SegmentDistance(p,float2(0,2.3),float2(0,.2)),SegmentDistance(p,float2(0,.2),float2(1.5,-1.3)))-.22;float2 dir=float2(.707107,-.707107),q=p-float2(1.55,-1.35);float2 h=float2(dot(q,float2(.707107,.707107)),dot(q,dir)+.72);float head=max(abs(h.x)*.9+h.y*.9-.85,-h.y);return min(shaft,head);}
half3 Fetch(float tile,float2 uv){float2 rect=float2(fmod(tile,2),1-floor(tile/2))*.5;float2 dx=clamp(ddx(uv)*.488,-.007,.007),dy=clamp(ddy(uv)*.488,-.007,.007);return SAMPLE_TEXTURE2D_GRAD(_BaseMap,sampler_BaseMap,rect+(.012+frac(uv)*.976)*.5,dx,dy).rgb;}
// Reuse the unjointed interior of an existing atlas stone for close surface grain.
float GroundStoneGrain(float2 p){float2 st=float2(.045,.035)+frac(p*float2(.73,1.63))*float2(.19,.085);return dot(Fetch(0,st),half3(.299,.587,.114));}
float GroundHash(float2 p){float3 q=frac(float3(p.xyx)*.1031);q+=dot(q,q.yzx+33.33);return frac((q.x+q.y)*q.z);}
float GroundMottle(float2 p){float2 b=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(GroundHash(b),GroundHash(b+float2(1,0)),f.x),lerp(GroundHash(b+float2(0,1)),GroundHash(b+1),f.x),f.y);}
half4 Frag(V i):SV_Target{UNITY_SETUP_INSTANCE_ID(i);float2 p=i.w.xz;float tile=0;half3 tint=1;float2 uv=i.uv;float active=step(.5,_GroundData.x)*step(.70,normalize(i.n).y);float paint=0;half3 paintColor=half3(.90,.89,.82);bool smoothConcrete=false;bool fuelSurface=false;float detailHeight=0;
if(active>.5){float2 d=p-_PlotRect.xy;uv=p*.42;float variant=frac(_GroundData.y*.6180339);tint=lerp(half3(.97,.98,.99),half3(1.05,1.015,.96),variant);
[loop]for(int k=0;k<12;k++){if(k>=_GroundData.z)break;float2 q=p-_PatchRect[k].xy;float sd=_PatchMode[k].z>.5?RoundedBox(q,_PatchRect[k].zw,.65):Ellipse(q,_PatchRect[k].zw);if(sd<.16){tile=sd<0?_PatchMode[k].x:0;tint=sd<0?half3(1,1,1):half3(.24,.26,.27);}}
float edge=min(_PlotRect.z-abs(d.x),_PlotRect.w-abs(d.y));if(edge<1.8){tile=0;tint=half3(.97,.98,.99);}if(edge>1.80&&edge<1.98){tile=0;tint=half3(.30,.32,.33);}
if(_GroundData.x>1.5&&_GroundData.x<2.5){float dist=length(p-_Feature0.xy);if(dist<_Feature0.z+2.0){tile=0;tint=1;}if(abs(dist-(_Feature0.z+1.8))<.14){tile=0;tint=half3(.24,.26,.27);}if(abs(p.x-_Feature0.x)<3.0){tile=0;tint=1;}if(abs(abs(p.x-_Feature0.x)-3.05)<.14&&dist>_Feature0.z+1.9){tile=0;tint=half3(.25,.27,.28);}}
if(_GroundData.x>3.5&&_GroundData.x<4.5){float u=p.y-_Feature0.y,v=p.x-_Feature0.x;if(v>0&&v<_Feature1.x-_Feature0.x&&abs(u)<64){tile=0;paint=0;smoothConcrete=false;float2 panelCell=floor(float2(u/8,v/7));float panel=frac(dot(panelCell,float2(.75487766,.56984029)));tint=lerp(half3(.88,.91,.94),half3(.96,.975,.985),panel);
// A clear facade walkway, entrance axis and emergency route, all on existing ground.
if(v<4||abs(u)<9){tint=half3(1.10,1.11,1.10);}if(abs(abs(u)-9.5)<.40){tint=half3(.39,.42,.46);}if(abs(abs(u)-9.05)<.10){paint=1;paintColor=half3(.20,.39,.53);}
float emergency=p.y-_Feature0.z;if(abs(emergency)<5.5){tint=half3(1.05,1.06,1.04);}if(abs(abs(emergency)-5.65)<.18){paint=1;paintColor=half3(.48,.21,.23);}
float border=min(min(v-2,_Feature1.x-p.x-2),62-abs(u));if(abs(border)<.36&&abs(u)>10){paint=0;tint=half3(.39,.42,.45);}for(int h=0;h<2;h++){float2 q=p-_PatchRect[h].xy;float sd=BoxSDF(q,_PatchRect[h].zw);if(sd<.45){paint=0;tile=sd<0?3:0;tint=sd<0?half3(.80,.96,.84):half3(.39,.42,.44);if(BoxSDF(q,float2(1.05,1.8))<0){tile=1;tint=1;}}}
// Existing furniture and public sidewalk remain untouched.

}}
if(_GroundData.x>4.5&&_GroundData.x<5.5){float2 cp=float2(p.y-_Feature0.y,p.x-_Feature0.x);float radius=length(cp);tile=0;paint=0;smoothConcrete=false;float stoneField=frac(dot(floor(float2(cp.x/9,cp.y/7)),float2(.75487766,.56984029)));tint=lerp(half3(.95,.94,.90),half3(1.025,1.01,.96),stoneField);
if(p.x>_Feature1.x){ // Front approach and fountain medallion.
float outer=min(radius-10.0,abs(cp.x)-5.2),inner=min(radius-8.0,abs(cp.x)-3.3);if(outer<0)tint=half3(.30,.32,.33);if(inner<0)tint=half3(1.10,1.08,1.02);if(abs(outer)<.12||abs(inner)<.10){paint=1;paintColor=half3(.54,.40,.20);}
if(abs(cp.y)<.22&&radius>10.1){tint=half3(.36,.37,.36);}if(abs(abs(cp.y)-.32)<.065&&radius>10.1){paint=1;paintColor=half3(.54,.40,.20);}
}else{float rearU=p.y+495.70;float rearX=p.x+435.39;if(rearX<0&&abs(rearU)<3.3)tint=half3(1.10,1.08,1.02);if(rearX<0&&abs(abs(rearU)-3.8)<.48)tint=half3(.33,.35,.35);if(rearX<0&&abs(abs(rearU)-4.35)<.09){paint=1;paintColor=half3(.54,.40,.20);}float buildingBand=BoxSDF(p-float2(-410.87,-495.70),float2(24.52,33.86));if(abs(buildingBand-3.2)<.09){paint=1;paintColor=half3(.54,.40,.20);}}
float perimeter=min(min(p.x-(_PlotRect.x-_PlotRect.z)-2,_PlotRect.x+_PlotRect.z-p.x-2),_PlotRect.w-abs(p.y-_PlotRect.y)-2);if(perimeter>-.9&&perimeter<.25&&abs(cp.x)>5.5){tint=half3(.33,.35,.35);paint=0;}if(abs(perimeter-.32)<.09&&abs(cp.x)>5.5){paint=1;paintColor=half3(.54,.40,.20);}
for(int h=0;h<12;h++){if(h>=_GroundData.z)break;float2 q=p-_PatchRect[h].xy;float sd=BoxSDF(q,_PatchRect[h].zw);if(sd<.35){paint=0;tile=sd<0?3:0;tint=sd<0?half3(.82,.96,.84):half3(.34,.37,.36);}}
}
if(_GroundData.x>5.5&&_GroundData.x<7.5){tile=0;paint=0;smoothConcrete=false;float field=frac(dot(floor(p/8),float2(.75487766,.56984029)));tint=lerp(half3(.73,.735,.71),half3(.81,.815,.78),field);float boundary=10000,facade=10000;[loop]for(int e=0;e<48;e++){if(e>=_GroundData.w)break;boundary=min(boundary,GroundSegmentDistanceSquared(p,_GroundOutline[e].xy,_GroundOutline[e].zw));}[loop]for(int f=0;f<48;f++){if(f>=_Feature0.w)break;facade=min(facade,GroundSegmentDistanceSquared(p,_GroundFacades[f].xy,_GroundFacades[f].zw));}boundary=sqrt(boundary);facade=sqrt(facade);
if(facade<2.5||boundary<1.0)tint=half3(1.32,1.30,1.22);if(abs(boundary-1.3)<.32)tint=half3(.24,.25,.25);
// Link the outer promenade to existing storefronts without interrupting circulation.
float2 pathDir=_Feature1.zw-_Feature1.xy;float pathLength=length(pathDir);pathDir/=max(pathLength,.001);float2 pathRel=p-(_Feature1.xy+_Feature1.zw)*.5;float pathMask=BoxSDF(float2(dot(pathRel,pathDir),dot(pathRel,float2(-pathDir.y,pathDir.x))),float2(pathLength*.5,1.6));if(pathLength>.1&&pathMask<0)tint=half3(1.32,1.30,1.22);for(int k=0;k<12;k++){if(k>=_GroundData.z)break;float2 q=abs(p-_PatchRect[k].xy);float frame=BoxSDF(p-_PatchRect[k].xy,_PatchRect[k].zw+1.8);if(_PlotRect.w>60&&abs(frame)<.65&&boundary>1.7&&facade>2.5)tint=half3(1.26,1.25,1.18);}
for(int h=0;h<12;h++){if(h>=_GroundData.z)break;float2 q=p-_PatchRect[h].xy;float sd=BoxSDF(q,_PatchRect[h].zw);if(sd<.30&&boundary>2.1&&facade>2.5){tile=sd<0?3:0;tint=sd<0?half3(.82,.96,.84):half3(.35,.38,.38);}}
}
if((_GroundData.x>2.5&&_GroundData.x<3.5)||(_GroundData.x>6.5&&_GroundData.x<7.5)){float4 fuelInfo=_GroundData.x>6.5?_Fuel0:_Feature0;float4 fuelFacing=_GroundData.x>6.5?_Fuel1:_Feature1;float2 normal=fuelFacing.xy;float2 side=float2(-normal.y,normal.x);float2 rel=p-fuelInfo.xy;float u=dot(rel,side),v=dot(rel,normal);
// Both mirrored station sites share the same pump-local surface layout.
float zone=RoundedBox(float2(u,v-5),float2(26.0+saturate((v+2)/25)*5.0,22.0),1.0);if(zone<0){fuelSurface=true;paint=0;tile=2;tint=1;if(v<-9.5){tile=0;tint=1;}
float apron=RoundedBox(float2(u,v+.5),float2(max(fuelInfo.w+8.0,18.0),5.0),.55);if(apron<0){tile=0;tint=1;smoothConcrete=true;}if(abs(apron)<.10){paint=1;paintColor=half3(.96,.74,.16);}
float arrowV=normal.x>0?12:17;float a0=FuelTurnArrow(float2(u+22,v-arrowV)*.88);float a1=FuelTurnArrow(float2(-(u-22),-(v-arrowV))*.88);if(min(a0,a1)<0&&v>7){paint=1;paintColor=half3(.86,.85,.80);}
// Short side edge markings keep the open forecourt readable without boxing it in.
if(abs(abs(u)-(26.0+saturate((v+2)/25)*5.0)+.75)<.075&&v>-8&&v<12){paint=1;paintColor=half3(.82,.81,.77);}}}
uv=p*(tile>2.5?.55:tile>1.5?.32:tile>.5?.58:.42);
}
half3 c=Fetch(tile,uv)*tint;
if(active>.5&&fuelSurface&&tile>1.5&&tile<2.5){half grey=dot(c,half3(.299,.587,.114));float broad=sin(p.x*.73+sin(p.y*.39))*sin(p.y*.61+p.x*.17);c=half3(.215,.214,.209)+(grey-.20)*.85+broad*.008;}
if(active>.5&&_GroundData.x>3.5&&_GroundData.x<4.5&&tile<.5&&p.x>_Feature0.x&&p.x<_Feature1.x&&abs(p.y-_Feature0.y)<64){float2 hp=float2(p.y-_Feature0.y,p.x-_Feature0.x);bool mainWalk=abs(hp.x)<9;float2 sz=mainWalk?float2(2,2.5):float2(1.8,1.2);float row=floor(hp.y/sz.y);float2 st=float2(hp.x+(mainWalk?0:fmod(abs(row),2)*sz.x*.5),hp.y)/sz;float2 cell=floor(st);float stone=frac(dot(cell,float2(.75487766,.56984029)));half grain=dot(c,half3(.299,.587,.114));c=half3(.535,.565,.585)*tint+(grain-.4)*.14*tint;c*=.978+stone*.044;float2 edgeSlab=min(frac(st),1-frac(st))*sz;float seamDist=min(edgeSlab.x,edgeSlab.y);float aa=max(fwidth(seamDist),.002);float jointCoverage=1-smoothstep(.012-aa,.012+aa,seamDist);c*=1-jointCoverage*.30;detailHeight-=jointCoverage*.0018;float bevel=(1-smoothstep(.030-aa,.030+aa,seamDist))*(1-jointCoverage);c+=bevel*.012*tint;}
if(active>.5&&_GroundData.x>4.5&&_GroundData.x<5.5&&tile<.5){float2 cp=float2(p.y-_Feature0.y,p.x-_Feature0.x);float radius=length(cp);float2 sz=float2(2.1,1.4);float2 st=cp/sz;st.x+=fmod(abs(floor(st.y)),2)*.5;float2 edgeStone=min(frac(st),1-frac(st))*sz;float seam=min(edgeStone.x,edgeStone.y);if(radius>6.5&&radius<10.3&&abs(cp.x)>3.3){float radialSeam=abs(frac(atan2(cp.y,cp.x)*3.8197186)-.5)*radius*.261799;seam=min(abs(radius-9.0),radialSeam);}float aa=max(fwidth(seam),.002);float jointCoverage=1-smoothstep(.013-aa,.013+aa,seam);float stone=frac(dot(floor(st),float2(.75487766,.56984029)));half grain=dot(c,half3(.299,.587,.114));c=half3(.52,.515,.49)*tint+(grain-.4)*.12*tint;c*=.98+stone*.04;c*=1-jointCoverage*.28;detailHeight-=jointCoverage*.0018;}
if(active>.5&&_GroundData.x>5.5&&_GroundData.x<7.5&&!fuelSurface&&tile<.5){float2 sz=float2(1.4,.8);float2 st=p/sz;st.x+=fmod(abs(floor(st.y)),2)*.5;float2 er=min(frac(st),1-frac(st))*sz;float seam=min(er.x,er.y);float aa=max(fwidth(seam),.002);float joint=1-smoothstep(.012-aa,.012+aa,seam);half grain=dot(c,half3(.299,.587,.114));c=half3(.56,.55,.52)*tint+(grain-.4)*.16*tint;float stoneTone=frac(dot(floor(st),float2(.75487766,.56984029)));c*=.963+stoneTone*.074;c*=1-joint*.30;float bevel=(1-smoothstep(.032-aa,.032+aa,seam))*(1-joint);c+=bevel*.018*tint;detailHeight-=joint*.002;detailHeight+=bevel*.00065;
// Charcoal bands use individual small stone units rather than an unbroken colour stripe.
if(dot(tint,half3(.299,.587,.114))<.43){float2 bs=p/float2(.65,.65);float2 be=min(frac(bs),1-frac(bs))*.65;float ds=min(be.x,be.y);float baa=max(fwidth(ds),.002);float grout=1-smoothstep(.010-baa,.010+baa,ds);c*=1-grout*.22;float chipTone=frac(dot(floor(bs),float2(.56984029,.75487766)));c*=.94+chipTone*.12;detailHeight-=grout*.0012;}}
if(smoothConcrete){half grain=dot(Fetch(0,p*.8),half3(.299,.587,.114));c=half3(.46,.445,.405)+(grain-.45)*.14;float2 seam=abs(frac(p/4)-.5);float joint=step(.493,max(seam.x,seam.y));c*=1-joint*.25;}
if(active>.5&&tile<1.5){float closeFade=1-smoothstep(18,55,distance(_WorldSpaceCameraPos,i.w));float grain=GroundStoneGrain(p);float mottle=GroundMottle(p*.67)-.5;float mineral=grain-.48;float darkStone=1-saturate(dot(tint,half3(.299,.587,.114))/.65);c*=1+mineral*(.90+darkStone*.35)+mottle*.10;float pore=GroundHash(floor(p*90));float poreFade=1-saturate(max(fwidth(p.x),fwidth(p.y))*90);float pit=step(.975,pore)*poreFade;c*=1-pit*.18;detailHeight+=(mineral*.0025+mottle*.001)*closeFade;paintColor*=.78+grain*.48;float worn=GroundMottle(p*5.3);paintColor*=lerp(.94,1.02,worn);}
c=lerp(c,paintColor,paint);Light light=GetMainLight(TransformWorldToShadowCoord(i.w));half3 n=normalize(i.n);
if(active>.5&&tile<1.5){float2 dx=ddx(p),dy=ddy(p);float det=dx.x*dy.y-dx.y*dy.x;float dhx=ddx(detailHeight),dhy=ddy(detailHeight);float safeDet=abs(det)>.00000001?det:1;float2 slope=float2(dhx*dy.y-dhy*dx.y,dhy*dx.x-dhx*dy.x)/safeDet;slope=clamp(slope,-.16,.16);n=normalize(n+float3(-slope.x,0,-slope.y));}half3 illumination=max(.24,SampleSH(n)*.72+light.color*(.25+.68*saturate(dot(n,light.direction)))*light.shadowAttenuation);if(active>.5&&_GroundData.x>5.5&&_GroundData.x<7.5){half neutralLight=dot(illumination,half3(.299,.587,.114));illumination=lerp(illumination,neutralLight.xxx,.70);}return half4(MixFog(c*illumination,i.fog),1);}
half4 Depth(V i):SV_Target{return 0;}half4 Norm(V i):SV_Target{return half4(normalize(i.n),0);}
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
Pass{Name "DepthOnly" Tags{"LightMode"="DepthOnly"} ColorMask 0
HLSLPROGRAM
#pragma target 3.5
#pragma vertex Vert
#pragma fragment Depth
#pragma multi_compile_instancing
ENDHLSL
}
Pass{Name "DepthNormals" Tags{"LightMode"="DepthNormals"}
HLSLPROGRAM
#pragma target 3.5
#pragma vertex Vert
#pragma fragment Norm
#pragma multi_compile_instancing
ENDHLSL
}
}}
