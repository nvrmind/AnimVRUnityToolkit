#include "UnityCG.cginc"

float3 _Lut2D_Params;
UNITY_DECLARE_TEX2DARRAY(_ColorLUTs);

#define FLT_EPSILON     1.192092896e-07 // Smallest positive number, such that 1.0 + FLT_EPSILON != 1.0

float3 PositivePow(float3 base, float3 power)
{
	return pow(max(abs(base), float3(FLT_EPSILON, FLT_EPSILON, FLT_EPSILON)), power);
}

half3 SRGBToLinear(half3 c)
{
	half3 linearRGBLo = c / 12.92;
	half3 linearRGBHi = PositivePow((c + 0.055) / 1.055, half3(2.4, 2.4, 2.4));
	half3 linearRGB = (c <= 0.04045) ? linearRGBLo : linearRGBHi;
	return linearRGB;
}

half3 LinearToSRGB(half3 c)
{
	half3 sRGBLo = c * 12.92;
	half3 sRGBHi = (PositivePow(c, half3(1.0 / 2.4, 1.0 / 2.4, 1.0 / 2.4)) * 1.055) - 0.055;
	half3 sRGB = (c <= 0.0031308) ? sRGBLo : sRGBHi;
	return sRGB;
}

//
// 2D LUT grading
// scaleOffset = (1 / lut_width, 1 / lut_height, lut_height - 1)
//
half3 ApplyLut2D(int index, float3 uvw)
{
	// Strip format where `height = sqrt(width)`
	uvw.z *= _Lut2D_Params.z;
	half shift = floor(uvw.z);
	uvw.xy = uvw.xy * _Lut2D_Params.z * _Lut2D_Params.xy + _Lut2D_Params.xy * 0.5;
	uvw.x += shift * _Lut2D_Params.y;

	float3 samplePos = float3(uvw.xy, index);

	uvw.xyz = lerp(
		UNITY_SAMPLE_TEX2DARRAY_LOD(_ColorLUTs, samplePos, 0).rgb,
		UNITY_SAMPLE_TEX2DARRAY_LOD(_ColorLUTs, samplePos + float3(_Lut2D_Params.y, 0.0, 0.0), 0).rgb,
		uvw.z - shift
	);

	return LinearToSRGB(uvw);
}
