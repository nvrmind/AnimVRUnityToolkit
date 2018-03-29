#pragma shader_feature  _COVERAGE_SEED_EYE
#pragma shader_feature  _COVERAGE_OLD

uniform int _FrameIndex;
UNITY_DECLARE_TEX2DARRAY(_CoverageBlueNoise);

#include "UnityCG.cginc"

#define COVERAGE_DATA \
	float4 pos : SV_POSITION; \
	float4 screenPos : TEXCOORD7; \

struct CoverageFragmentInfo {
	int id;
	COVERAGE_DATA
};

#define TRANSFER_COVERAGE_DATA_VERT(v, o) \
	o.pos = UnityObjectToClipPos(v.vertex); \
	o.screenPos = ComputeScreenPos(o.pos); \

#if UNITY_SINGLE_PASS_STEREO

#define TRANSFER_COVERAGE_DATA_FRAG(i, f) \
	f.pos = i.pos; \
    f.screenPos = i.screenPos; \
	f.screenPos.xy /= f.screenPos.w; \
	float4 scaleOffset = unity_StereoScaleOffset[unity_StereoEyeIndex]; \
	f.screenPos.xy = (f.screenPos.xy - scaleOffset.zw) / scaleOffset.xy; \
	f.screenPos.xy *= _ScreenParams.xy \

#else

#define TRANSFER_COVERAGE_DATA_FRAG(i, f) \
	f.pos = i.pos; \
	f.screenPos = i.screenPos; \
	f.screenPos.xy /= f.screenPos.w; \
	f.screenPos.xy *= _ScreenParams.xy

#endif



float blueNoise(int3 coords) {
	return _CoverageBlueNoise.Load( int4(coords & 63, 0) ).a;
}

float rand(float2 co) {
	return frac(sin(dot(co.xy ,float2(12.9898,78.233))) * 43758.5453);
}

float checkerBoard(float2 co) {
	return 0.5 * min(uint(co.x) % 2 + uint(co.y) % 2, 1);
}


float4 ApplyCoverage(float4 c, CoverageFragmentInfo i) { //, inout uint coverage : SV_Coverage) {
			     
	uint index = unity_CameraProjection[0][2] < 0 ? _FrameIndex + 10 : _FrameIndex;
	
	const int MSAASampleCount = 8;
	const float DitherRange = 1.0 / MSAASampleCount;



	float RandNorm = blueNoise( int3( int2(i.screenPos.x, i.screenPos.y), _FrameIndex + i.id) );
	float ditherOffset = (RandNorm - 0.5);
	float AlphaWithDither = saturate(c.a + 0.99 * ditherOffset * DitherRange);



	return float4( c.rgb, AlphaWithDither);
}