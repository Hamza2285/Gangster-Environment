Shader "Gangstar City/Mobile Night Sky"
{
 Properties {
  _DayCube("Existing day sky",Cube)="grey" {}
  _DayTint("Day tint",Color)=(.5,.5,.5,1)
  _DayExposure("Day exposure",Float)=1
  _DayRotation("Day rotation",Float)=0
  _Horizon("Horizon",Color)=(.065,.12,.22,1)
  _Zenith("Zenith",Color)=(.008,.018,.055,1)
  _MoonDirection("Moon direction",Vector)=(.3,.5,.8,0)
  _MoonColor("Moon glow",Color)=(1.8,1.9,2.2,1)
 }
 SubShader {
  Tags {"Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline"}
  Cull Off ZWrite Off
  Pass {
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"
   CBUFFER_START(UnityPerMaterial)
    half4 _Horizon,_Zenith,_MoonDirection,_MoonColor,_DayTint,_DayCube_HDR;
    float _DayExposure,_DayRotation;
   CBUFFER_END
   TEXTURECUBE(_DayCube);SAMPLER(sampler_DayCube);float _CitySkyBlend;
   struct A{float4 positionOS:POSITION;};struct V{float4 positionCS:SV_POSITION;float3 direction:TEXCOORD0;};
   V vert(A i){V o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.direction=i.positionOS.xyz;return o;}
   half4 frag(V i):SV_Target{
    float3 d=normalize(i.direction);
    half3 color=lerp(_Horizon.rgb,_Zenith.rgb,saturate(d.y*1.7));
    float m=dot(d,normalize(_MoonDirection.xyz));
    float edge=max(fwidth(m),.000008);
    float disc=smoothstep(.99965-edge,.99965+edge,m);
    float halo=pow(saturate(m),180)*.025;
    color+=_MoonColor.rgb*(disc+halo);
    float3 cell=floor(d*450);float star=frac(sin(dot(cell,float3(12.9898,78.233,45.164)))*43758.5453);
    color+=step(.9991,star)*smoothstep(.05,.35,d.y)*.2;
        float angle=-radians(_DayRotation);float sn,cs;sincos(angle,sn,cs);float3 rd=float3(cs*d.x-sn*d.z,d.y,sn*d.x+cs*d.z);
    half4 encoded=SAMPLE_TEXTURECUBE(_DayCube,sampler_DayCube,rd);
    half3 day=DecodeHDREnvironment(encoded,_DayCube_HDR)*_DayTint.rgb*_DayExposure;
#ifdef UNITY_COLORSPACE_GAMMA
    day*=2;
#else
    day*=4.5947938;
#endif
    float t=saturate(_CitySkyBlend);
    half3 dusk=lerp(half3(.34,.19,.24),half3(.075,.105,.24),saturate(d.y*2));
        float phase=saturate(t<.5?t*2:(t-.5)*2);
    phase=phase*phase*phase*(phase*(phase*6-15)+10);
    half3 sky=t<.5?lerp(day,dusk,phase):lerp(dusk,color,phase);
    return half4(sky,1);
   }
   ENDHLSL
  }
 }
}




