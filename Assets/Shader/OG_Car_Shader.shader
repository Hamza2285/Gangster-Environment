Shader "OG_Car_Shader" {
	Properties {
		_Color ("Color", Vector) = (1,1,1,1)
		_MainTex ("Albedo", 2D) = "white" {}
		_UnityTex ("Unity Map", 2D) = "white" {}
		_MaskColor ("Mask Color", Vector) = (1,1,1,1)
		_Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
		_Glossiness ("Smoothness", Range(0, 1)) = 0.5
		_GlossMapScale ("Smoothness Scale", Range(0, 1)) = 1
		[Gamma] _Metallic ("Metallic", Range(0, 1)) = 0
		[Toggle(_USE_COLOR)] _UseColorMask ("Use Color Mask", Float) = 1
		[ToggleOff] _SpecularHighlights ("Specular Highlights", Float) = 1
		[ToggleOff] _GlossyReflections ("Glossy Reflections", Float) = 1
		_BumpScale ("Scale", Float) = 1
		_BumpMap ("Normal Map", 2D) = "bump" {}
		[Toggle(_USE_DIRT)] _UseDirt ("Use Dirt", Float) = 0
		_DirtColor ("Dirt Color", Vector) = (0.43,0.39,0.24,1)
		_Contrast ("Contrast", Float) = 0
		_DecalTex ("Decals", 2D) = "white" {}
		_DecalColor ("Decal Color", Vector) = (1,1,1,1)
		_HoloTex ("Holo (R)", 2D) = "white" {}
		_EmissionColor ("Color", Vector) = (0,0,0,1)
		_EmissionMap ("Emission", 2D) = "white" {}
		[Enum(UV0,0,UV1,1)] _UVSec ("UV Set for secondary textures", Float) = 0
		[Toggle(_USE_CLIP_THRESHOLD)] _UseClipThreshold ("Use Clip Threshold", Float) = 0
		_ThresholdOffset ("Threshold Offset", Float) = 0.35
		_ThresholdRange ("Threshold Range", Float) = 0.4
		[Toggle(_USE_IRIDESCENT)] _UseIridescent ("Use iridescent", Float) = 0
		_FresnelPower ("Power", Float) = 1
		_SecondColorPos ("Border of Second Color", Range(0.01, 0.99)) = 0.5
		_ThirdColorPos ("Border of Third Color", Range(0.01, 0.99)) = 0.5
		_FirstColor ("First Color", Vector) = (1,1,1,1)
		_SecondColor ("Second Color", Vector) = (1,1,1,1)
		_ThirdColor ("Third Color", Vector) = (1,1,1,1)
		[HideInInspector] _Mode ("__mode", Float) = 0
		[HideInInspector] _SrcBlend ("__src", Float) = 1
		[HideInInspector] _DstBlend ("__dst", Float) = 0
		[HideInInspector] _ZWrite ("__zw", Float) = 1
	}
	//DummyShaderTextExporter
	SubShader{
		Tags { "RenderType"="Opaque" }
		LOD 200

		Pass
		{
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			float4x4 unity_ObjectToWorld;
			float4x4 unity_MatrixVP;
			float4 _MainTex_ST;

			struct Vertex_Stage_Input
			{
				float4 pos : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct Vertex_Stage_Output
			{
				float2 uv : TEXCOORD0;
				float4 pos : SV_POSITION;
			};

			Vertex_Stage_Output vert(Vertex_Stage_Input input)
			{
				Vertex_Stage_Output output;
				output.uv = (input.uv.xy * _MainTex_ST.xy) + _MainTex_ST.zw;
				output.pos = mul(unity_MatrixVP, mul(unity_ObjectToWorld, input.pos));
				return output;
			}

			Texture2D<float4> _MainTex;
			SamplerState sampler_MainTex;

			struct Fragment_Stage_Input
			{
				float2 uv : TEXCOORD0;
			};

			float4 frag(Fragment_Stage_Input input) : SV_TARGET
			{
				return _MainTex.Sample(sampler_MainTex, input.uv.xy);
			}

			ENDHLSL
		}
	}
	Fallback "VertexLit"
}