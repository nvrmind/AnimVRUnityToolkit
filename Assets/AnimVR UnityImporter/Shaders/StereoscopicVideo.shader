Shader "AnimVR/Video"
{
	Properties
	{
		_MainTex("Diffuse Texture", 2D) = "white" {}
		_Color("Diffuse Color", Color) = (1, 1, 1, 1)
		_Gamma("Gamma", Float) = 2.2
		[Enum(None, 0, Side by Side, 1, Over Under, 2)] _Layout("3D Layout", Float) = 0
	}
	
	SubShader
	{
	CGINCLUDE
	#include "UnityCG.cginc"
	#include "Lighting.cginc"

	// compile shader into multiple variants, with and without shadows
	// (we don't care about any lightmaps yet, so skip these variants)
	// shadow helper functions and macros
	#include "AutoLight.cginc"
	#include "CoverageTransparency.cginc"
	sampler2D _MainTex;
	float4 _MainTex_TexelSize;
	half4 _MainTex_HDR;
	half4 _Color;
	int _Layout;
	float _Gamma;

	struct v2f
	{
		COVERAGE_DATA
			float2 uv : TEXCOORD0;
		UNITY_VERTEX_INPUT_INSTANCE_ID
			float4 layout3DScaleAndOffset : TEXCOORD2;
		UNITY_VERTEX_OUTPUT_STEREO
	};

	v2f vert(appdata_full v, uint id : SV_VertexID)
	{
		v2f o;
		UNITY_SETUP_INSTANCE_ID(v);
		UNITY_TRANSFER_INSTANCE_ID(v, o);
		UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
		TRANSFER_COVERAGE_DATA_VERT(v, o);

		o.uv = v.texcoord.xy;

		// Calculate constant scale and offset for 3D layouts
		if (_Layout == 0) // No 3D layout
			o.layout3DScaleAndOffset = float4(0, 0, 1, 1);
		else if (_Layout == 1) // Side-by-Side 3D layout
			o.layout3DScaleAndOffset = float4(unity_StereoEyeIndex, 0, 0.5, 1);
		else // Over-Under 3D layout
			o.layout3DScaleAndOffset = float4(0, 1 - unity_StereoEyeIndex, 1, 0.5);

		return o;
	}
	ENDCG
	Pass
	{
		Tags{ "LightMode" = "ForwardBase"  "Queue" = "Geometry" "RenderType" = "Opaque" }
		Blend Off
		Cull Off
		ZWrite On
		AlphaToMask On

		CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma target 5.0
#pragma multi_compile_instancing
#pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight

		fixed4 frag(v2f i) : SV_Target
		{
			UNITY_SETUP_INSTANCE_ID(i);

			float2 tc = i.uv;
			tc = (tc + i.layout3DScaleAndOffset.xy) * i.layout3DScaleAndOffset.zw;

			fixed4 col = _Color;
			col *= tex2D(_MainTex, tc);

			col.rgb = pow(col.rgb, 1.0/_Gamma);

			col = saturate(col);

			col.a = 1;

			CoverageFragmentInfo f;
			TRANSFER_COVERAGE_DATA_FRAG(i, f);
			f.id = 0;
			return ApplyCoverage(col, f);
		}
		ENDCG
	}

	Pass 
	{
		Name "ShadowCaster"
		Tags{ "Queue" = "Geometry" "RenderType" = "Opaque" "DisableBatching" = "True" "LightMode" = "ShadowCaster" }

		AlphaToMask On
		Fog{ Mode Off }
		ZWrite On ZTest Less Cull Off
		Offset 1, 1

		CGPROGRAM
#pragma multi_compile_shadowcaster
#pragma fragmentoption ARB_precision_hint_fastest
#pragma target 5.0
#pragma vertex vert
#pragma fragment frag_shadow
#pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight
		
		float4 frag_shadow(v2f i) : COLOR
		{
			return 1;
		}
		ENDCG
	}
	}
}