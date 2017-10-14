#pragma shader_feature  _COVERAGE_SEED_EYE
#pragma shader_feature  _COVERAGE_OLD

uniform int _FrameIndex;
uniform sampler2D _CoverageBlueNoise;

#include "UnityCG.cginc"

#define COVERAGE_DATA \
	float4 pos : SV_POSITION; \
	float4 screenPos :TEXCOORD6; \


struct CoverageFragmentInfo {
	COVERAGE_DATA
};
	 
#define TRANSFER_COVERAGE_DATA_VERT(v, o) \
	o.pos = UnityObjectToClipPos(v.vertex); \
	o.screenPos = ComputeScreenPos(o.pos); 

#define TRANSFER_COVERAGE_DATA_FRAG(f, o) \
	o.pos = f.pos; \
	o.screenPos = f.screenPos; 

float blueNoise(float2 uv) {
	float3 BlueNoise= tex2D(_CoverageBlueNoise, uv/256).rgb;
	return BlueNoise.x;
}

float rand(float2 co) {
	return frac(sin(dot(co.xy ,float2(12.9898,78.233))) * 43758.5453);
}

float InterleavedGradientNoise( float2 uv ) {
	const float3 magic = float3( 0.06711056, 0.00583715, 52.9829189 );
	return frac(magic.z * frac(dot(uv, magic.xy)));
}
 
uint BarrelShift8Bit(uint In, uint shift) {
	uint b = In | (In << 8);
	return (b >> shift) & 0xff;
}
 
uint ComputeAlpha2CoverageNoDither(float InAlpha, int InMSAASampleCount) {
	return (0xff00 >> uint(InAlpha * InMSAASampleCount + 0.5f)) & 0xff;
}
 
int ComputeAlpha2CoverageDither(float InAlpha, float2 SVPos, uint frameID){
	int MSAASampleCount = 8;
			
	float RandNorm = InterleavedGradientNoise(SVPos + float2(0, frameID % 4) * 0.7 );
	float ditherOffset = (RandNorm - 0.5);
	const float DitherRange = 1.0f / MSAASampleCount;
	float AlphaWithDither = clamp(InAlpha + ditherOffset * DitherRange, 0.0, 1.0);
	uint shift = uint(RandNorm *  6.99999f);
	return int(BarrelShift8Bit(ComputeAlpha2CoverageNoDither(AlphaWithDither, MSAASampleCount), shift));
}

float4 ApplyCoverage(float4 c, CoverageFragmentInfo i, inout uint coverage : SV_Coverage) {
	#if _COVERAGE_OLD
	float2 screenUv = i.pos.xy * _ScreenParams.xy;
	#else
	float2 screenUv = i.screenPos.xy / i.screenPos.w * _ScreenParams.xy;
	#endif
			     
	#if _COVERAGE_SEED_EYE
	int index = unity_CameraProjection[0][2] < 0 ? _FrameIndex : (8 - _FrameIndex);
	#else
	int index = _FrameIndex;
	#endif
	coverage = ComputeAlpha2CoverageDither(c.a, screenUv, index);
			
	return  float4( c.rgb, 1.0 );
}