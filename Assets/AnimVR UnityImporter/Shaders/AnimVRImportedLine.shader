Shader "AnimVR/ImportedLine"
{
    Properties
    {
        _MainTex ("Diffuse Texture", 2D) = "white" {}
		_Color("Diffuse Color", Color) = (1, 1, 1, 1)
		_TintColor("Tint", Color) = (1, 1, 1, 1)
		_OnlyTint("OnlyTint", Float) = 0
		_EmissionColor("Emissive Color", Color) = (0, 0, 0, 1)
		_SpecColor("Specular Color", Color) = (0, 0, 0, 1)
		_Unlit("Is Unlit", Range(0, 1)) = 0
		_Gamma("Gamma Value", Float) = 1

		[PerRendererData] _FadeIn("FadeIn", Float) = 1
		[PerRendererData] _FadeOut("FadeOut", Float) = 1
		[PerRendererData] _FadeModeIn("FadeModeIn",   Int) = 0
		[PerRendererData] _FadeModeOut("FadeModeOut", Int) = 0
		[HideInInspector] _ZWrite("__zw", Float) = 1.0
	}
		SubShader
		{


			Pass
			{
				Tags {"LightMode" = "ForwardBase"  "Queue" = "Geometry-3" "RenderType" = "Opaque"  }
				Blend Off
				Cull Off
				ZWrite[_ZWrite]
				AlphaToMask On

				CGPROGRAM
				#pragma vertex vert
				#pragma fragment frag
				#pragma target 5.0
				#pragma multi_compile_instancing
				#include "UnityCG.cginc"
				#include "Lighting.cginc"

			// compile shader into multiple variants, with and without shadows
			// (we don't care about any lightmaps yet, so skip these variants)
			#pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight
			// shadow helper functions and macros
			#include "AutoLight.cginc"
			#include "CoverageTransparency.cginc"

			float _Gamma;
			float _OnlyTint;

			float _FadeIn, _FadeOut;
			int _FadeModeIn, _FadeModeOut;

            struct v2f
            {
				COVERAGE_DATA
                float2 uv : TEXCOORD0;
                fixed4 diff : COLOR0;
				float3 norm : TEXCOORD1;
                fixed3 ambient : COLOR1;
				float3 objPos : TEXCOORD2;
				UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            v2f vert (appdata_full v, uint id : SV_VertexID)
            {
                v2f o;
				UNITY_SETUP_INSTANCE_ID (v);
                UNITY_TRANSFER_INSTANCE_ID (v, o);

                o.uv = v.texcoord.xy;
                o.norm = UnityObjectToWorldNormal(v.normal);
                o.diff =  v.color;

				float alongLine = v.texcoord.x;
				float fadeOpacity, fadeWidth;
				ComputeFadeValues(alongLine, _FadeIn, _FadeModeIn, _FadeOut, _FadeModeOut, 0, fadeOpacity, fadeWidth);

				v.vertex.xyz = v.vertex.xyz - v.normal.xyz * (1.0 - fadeWidth);
				o.diff.a *= fadeOpacity;

				if (fadeWidth < 0.00001) o.diff.a = 0;

				TRANSFER_COVERAGE_DATA_VERT(v, o);

                o.ambient = ShadeSH9(half4(o.norm,1));
				o.objPos = v.vertex;
                return o;
            }

            sampler2D _MainTex;
			
            //UNITY_SHADER_NO_UPGRADE
#if UNITY_VERSION >= 201730
			UNITY_INSTANCING_BUFFER_START (InstanceProperties)
                UNITY_DEFINE_INSTANCED_PROP (float4, _TintColor)
#define _TintColor_arr InstanceProperties
            UNITY_INSTANCING_BUFFER_END(InstanceProperties)
#else
            UNITY_INSTANCING_CBUFFER_START(InstanceProperties)
                UNITY_DEFINE_INSTANCED_PROP(float4, _TintColor)
            UNITY_INSTANCING_CBUFFER_END
#endif

			fixed4 _Color;
			fixed4 _EmissionColor;
			float _Unlit;
			
            fixed4 frag (v2f i) : SV_Target
            {
				UNITY_SETUP_INSTANCE_ID (i);

                fixed4 col = _Color * i.diff;
                col.rgb = pow(col.rgb, _Gamma);
                col *= tex2D(_MainTex, i.uv);

                // compute shadow attenuation (1.0 = fully lit, 0.0 = fully shadowed)
                // darken light's illumination with shadow, keep ambient intact
                half nl = max(0, dot(i.norm, _WorldSpaceLightPos0.xyz));
                fixed3 lighting = nl * _LightColor0.rgb  + i.ambient;
                col.rgb *= lerp(lighting, 1, _Unlit);
				col.rgb += _EmissionColor.rgb;

#if UNITY_VERSION >= 201730
				col *= UNITY_ACCESS_INSTANCED_PROP (_TintColor_arr, _TintColor);
                col = lerp(col, UNITY_ACCESS_INSTANCED_PROP(_TintColor_arr, _TintColor), _OnlyTint);
#else
                col *= UNITY_ACCESS_INSTANCED_PROP(_TintColor);
                col = lerp(col, UNITY_ACCESS_INSTANCED_PROP(_TintColor), _OnlyTint);
#endif
                col = saturate(col);

				CoverageFragmentInfo f;
				TRANSFER_COVERAGE_DATA_FRAG(i, f);
				f.id = 0;
				return ApplyCoverage(col, f);
            }
            ENDCG
        }

        // shadow casting support
        	Pass 
	{
		Name "ShadowCaster"
		Tags { "LightMode" = "ShadowCaster" }
		
		ZWrite On ZTest LEqual Cull Off

		CGPROGRAM
		#pragma vertex vert
		#pragma fragment frag
		#pragma target 2.0
		#pragma multi_compile_shadowcaster
		#include "UnityCG.cginc"

		struct v2f { 
			V2F_SHADOW_CASTER;
		};

		v2f vert( appdata_base v )
		{
			v2f o;
			TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
			return o;
		}

		float4 frag( v2f i ) : SV_Target
		{
			SHADOW_CASTER_FRAGMENT(i)
		}
		ENDCG
	}	
    }
}