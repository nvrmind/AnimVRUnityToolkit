// Upgrade NOTE: replaced '_World2Object' with 'unity_WorldToObject'

Shader "AnimVR/FuzzyLine" {
	Properties {
		_Color ("Material Color", Color) = (1,1,1,1)
		_Map ("Map", 2D) = "white" {}
		_SplatTex ("Splat Tex", 2D) = "white" {}

		_TessAmount ("Tesselation Amount", Float) = 1
		_LightBlending ("Lighting", Float) = 1
		_HermiteInterpolate ("Smooth Lines", Range(0,1)) = 1
		_UseVertexColors ("Use Vertex Colors", Range(0,1)) = 1
		_FadeOutDistStart ("Fade out Distance Start", Float) = 0.2
		_FadeOutDistEnd ("Fade out Distance End", Float) = 0.05
		_TaperDistStart ("Taper Distance at start of line", Float) = 0.1
		_TaperDistEnd ("Taper Distance at end of line", Float) = 0.1

		[HideInInspector] _ZWrite ("__zw", Float) = 1.0
	}



	CGINCLUDE
#pragma multi_compile __ FANCY_RENDER_ON
    #include "Tessellation.cginc"
    #include "CoverageTransparency.cginc"
    #include "noiseSimplex.cginc"
    #include "Lighting.cginc"
	#include "ColorCorrection.cginc"
#pragma enable_d3d11_debug_symbols

    struct PerLineDataS {
        float4 LineColor;
        
        float LineLength;
        int OneSided;
        int BrushType;
        float IsPulsing;
        
        float3 FacingDir;
        int TextureIndex;

        float TaperAmountShape;
        float TaperAmountOpacity;
        float EndTaperFactor;
        float ConstantSize;

        float LineCount;
        float UseTextureObjectSpace;
        float Highlight;
        float WorldScale;

		int FrameDataIndex;
		float3 __pad;
    };

    UNITY_DECLARE_TEX2DARRAY(BrushTextures);


    StructuredBuffer<PerLineDataS> PerLineData;

	struct PerFrameDataS {
		float Opacity;
		int ColorLookupTexture;
		uint FadeModeIn;
		uint FadeModeOut;
		
		float Fade;
		float FadeIn;
		float FadeOut;
		float __pad;
	};
    
	StructuredBuffer<PerFrameDataS> PerFrameData;

	float StageScale;
    float DoHighlighting;

    sampler2D _SplatTex;
    float4 _SplatTex_ST;

    sampler2D _MainTex;
    float4 _MainTex_ST;

    sampler2D _Map;
    float4 _Map_ST;

    float4 _Color;

    float _TessAmount;
    float _LightBlending;
    float _HermiteInterpolate;
    float _UseVertexColors;
    float _FadeOutDistStart;
    float _FadeOutDistEnd;

    float _TaperDistStart;
    float _TaperDistEnd;



    struct v2g {
        float4 vertex : SV_POSITION;
        float4 color : TEXCOORD1;
        float3 bitangent : TEXCOORD4;
        int dataIndex : TEXCOORD5;
        float3 tangent : TEXCOORD3;
        float3 normal : TEXCOORD2;
        float2 uv : TEXCOORD0;
    };

    struct g2f {
        COVERAGE_DATA
        float4 color : TEXCOORD1;
        float2 uv : TEXCOORD0;
        float2 splatUv : TEXCOORD2;
        float3 objPos : TEXCOORD3;
        int dataIndex : TEXCOORD5;
        float3 objNorm : TEXCOORD4;
    };


    float3 normalizeOrZero(float3 val) {
        float len = length(val);
        if(len < 0.000000001) return 0;
        return val/len;
    }
	

	void ApplyFade(float fade, uint fadeMode, float alongLine, int dataIndex, out float fadeOpacity, out float fadeWidth) {
		const int FadeOpacity = 1;
		const int FadeOpacityAlongLine = 2;
		const int FadeWidth = 4;
		const int FadeWidthAlongLine = 8;
		const int RandomOffset = 16;
		const int EndToStart = 32;

		fadeWidth = 1;
		fadeOpacity = 1;

		float alongFactor = 1;
		
		if (fadeMode & RandomOffset) {
			float r = rand(float2(dataIndex, 0)) * 0.4;
			fade = saturate(fade * (1.0 + r) - r);
		}

		if (fadeMode & EndToStart) {
			alongLine = 1.0 - alongLine;
		}

		float frameFade = fade * 1.2 - 0.2;
		alongFactor = saturate(smoothstep(frameFade + 0.2, frameFade, alongLine));

		
		if (fadeMode & FadeOpacity) {
			fadeOpacity = lerp(fade, alongFactor, fadeMode & FadeOpacityAlongLine);
		}

		if (fadeMode & FadeWidth) {
			fadeWidth = lerp(fade, alongFactor, fadeMode & FadeWidthAlongLine);
		}

		fadeWidth = saturate(fadeWidth);
		fadeOpacity = saturate(fadeOpacity);
	}

    v2g vert (appdata_full v) {
        
        v2g o;
        o.dataIndex = round(v.texcoord.x);
        PerLineDataS data = PerLineData[o.dataIndex];
		PerFrameDataS frame = PerFrameData[data.FrameDataIndex];

        o.vertex = v.vertex;
        o.color = v.color * data.LineColor;

		o.color.a *= frame.Opacity;


        o.uv = v.texcoord;
        o.uv.x = 0;


        float lineWidthScale = lerp(1, 1.0/ mul(float4(1, 0, 0, 0), unity_ObjectToWorld).x, data.ConstantSize);

        o.normal = v.normal * lineWidthScale;
        o.tangent = v.tangent.xyz;
        o.bitangent =  cross( normalizeOrZero(v.normal), normalizeOrZero(v.tangent.xyz) ) * v.tangent.w * lineWidthScale;
        float3 worldSpaceViewDir = WorldSpaceViewDir(v.vertex);
        float viewDist = length(worldSpaceViewDir);
        worldSpaceViewDir *= 1.0/viewDist;

        float3 worldFacingDir = mul(transpose(unity_WorldToObject), float4(data.FacingDir, 0));

        if(data.OneSided) o.color.a *=  max(0, dot(normalize(worldFacingDir), -worldSpaceViewDir));

		// Not drawing a line
		if (data.EndTaperFactor > 0.99) {
			float fadeInOpacity = 1;
			float fadeInWidth = 1;

			float fadeOutOpacity = 1;
			float fadeOutWidth = 1;

			float alongLine = (o.uv.y / data.LineLength);
			ApplyFade(frame.FadeIn, frame.FadeModeIn, alongLine, o.dataIndex, fadeInOpacity, fadeInWidth);
			ApplyFade(frame.FadeOut, frame.FadeModeOut, 1.0f - alongLine, o.dataIndex, fadeOutOpacity, fadeOutWidth);

			float fadeOpacity = fadeInOpacity * fadeOutOpacity;
			float fadeWidth = fadeInWidth * fadeOutWidth;

			o.color.a *= fadeOpacity;

			o.normal *= fadeWidth;
			o.bitangent *= fadeWidth;
		}

		
		if (frame.ColorLookupTexture != -1) {
			o.color.rgb = ApplyLut2D(frame.ColorLookupTexture, saturate(o.color.rgb));
		}



        return o;
    }

    struct HS_CONSTANT_OUTPUT
    {
        float edges[2] : SV_TessFactor;
    };


    // Returns true if triangle with given 3 world positions is outside of camera's view frustum.
    // cullEps is distance outside of frustum that is still considered to be inside (i.e. max displacement)
    bool UnityWorldViewFrustumCullLine (float3 wpos0, float3 wpos1, float cullEps)
    {    
        float4 planeTest;

        // left
        planeTest.x = (( UnityDistanceFromPlane(wpos0, unity_CameraWorldClipPlanes[0]) > -cullEps-0.2) ? 1.0f : 0.0f ) +
                      (( UnityDistanceFromPlane(wpos1, unity_CameraWorldClipPlanes[0]) > -cullEps-0.2) ? 1.0f : 0.0f );
        // right
        planeTest.y = (( UnityDistanceFromPlane(wpos0, unity_CameraWorldClipPlanes[1]) > -cullEps-0.75) ? 1.0f : 0.0f ) +
                      (( UnityDistanceFromPlane(wpos1, unity_CameraWorldClipPlanes[1]) > -cullEps-0.75) ? 1.0f : 0.0f );
        // top
        planeTest.z = (( UnityDistanceFromPlane(wpos0, unity_CameraWorldClipPlanes[2]) > -cullEps) ? 1.0f : 0.0f ) +
                      (( UnityDistanceFromPlane(wpos1, unity_CameraWorldClipPlanes[2]) > -cullEps) ? 1.0f : 0.0f );
        // bottom
        planeTest.w = (( UnityDistanceFromPlane(wpos0, unity_CameraWorldClipPlanes[3]) > -cullEps) ? 1.0f : 0.0f ) +
                      (( UnityDistanceFromPlane(wpos1, unity_CameraWorldClipPlanes[3]) > -cullEps) ? 1.0f : 0.0f );
    
        // has to pass all 4 plane tests to be visible
        return !all (planeTest);
    }


    
    float UnityCalcEdgeTessFactorLine (float3 wpos0, float3 wpos1, float edgeLen)
    {
        // distance to edge center
        float dist = distance (0.5 * (wpos0+wpos1), _WorldSpaceCameraPos);
        // length of the edge
        float len = distance(wpos0, wpos1);
        // edgeLen is approximate desired size in pixels
        float f = max(len * _ScreenParams.y / (edgeLen * dist), 1.0);
        return f;
    }

    float UnityEdgeLengthBasedTessCullLine (float4 v0, float4 v1, float edgeLength, float maxDisplacement)
    {
        float3 pos0 = mul(unity_ObjectToWorld,v0).xyz;
        float3 pos1 = mul(unity_ObjectToWorld,v1).xyz;
        float tess;

        if (UnityWorldViewFrustumCullLine(pos0, pos1, maxDisplacement))
        {
            tess = 0.0f;
        }
        else
        {
            tess = UnityCalcEdgeTessFactorLine(pos0, pos1, edgeLength);
        }
        return tess;
    }


    HS_CONSTANT_OUTPUT HSConst(InputPatch<v2g, 2> patch, uint patchID : SV_PrimitiveID)
    {
        HS_CONSTANT_OUTPUT output;

        PerLineDataS data = PerLineData[patch[0].dataIndex];

        float averageThickness =  max(length(mul(unity_ObjectToWorld, float4(patch[0].bitangent.xyz, 0))), 
                                      length(mul(unity_ObjectToWorld, float4(patch[1].bitangent.xyz, 0)))); 
        output.edges[0] = data.LineCount; // Detail factor
		output.edges[1] = data.BrushType == 2 ? 8 : UnityEdgeLengthBasedTessCullLine(patch[0].vertex, patch[1].vertex, _TessAmount * 0.2, averageThickness * 2); // Density factor

        return output;
    }

    [domain("isoline")]
    [partitioning("integer")]
    [outputtopology("line")]
    [outputcontrolpoints(2)]
    [patchconstantfunc("HSConst")]
    v2g hull(InputPatch<v2g, 2> ip, uint id : SV_OutputControlPointID)
    {
        return ip[id];
    }

    [domain("isoline")]
    v2g domain(HS_CONSTANT_OUTPUT input, OutputPatch<v2g, 2> op, float2 uv : SV_DomainLocation)
    {
        PerLineDataS data = PerLineData[op[0].dataIndex];
        float4 opPos0 = op[0].vertex;
        float4 opPos1 = op[1].vertex;
        float scaleFactor = 1;
        if(data.LineCount >= 2) {
            float firstDistFromStart = op[0].uv.y;
            float3 offsetStart = op[0].bitangent * snoise(float2(firstDistFromStart*20, uv.y*10)) + op[0].normal * snoise(float2(firstDistFromStart*20, uv.y*10)+200);
            offsetStart = offsetStart * (uv.y*uv.y+0.5);

            float secDistFromStart = op[1].uv.y;
            float3 offsetEnd = op[1].bitangent * snoise(float2(secDistFromStart*20, uv.y*10)) + op[1].normal * snoise(float2(secDistFromStart*20, uv.y*10)+200);
            offsetEnd = offsetEnd * (uv.y*uv.y+0.5);

            opPos0.xyz += offsetStart;
            opPos1.xyz += offsetEnd;

            scaleFactor = 1.0/(data.LineCount * 0.33);
        }

        v2g output;
        float t = uv.x;

		float t2 = t * t;
		float t3 = t2 * t;

		float3 position = (2.0*t3 - 3.0*t2 + 1.0) * opPos0.xyz
			+ (t3 - 2.0*t2 + t) * op[0].tangent
			+ (-2.0*t3 + 3.0*t2) * opPos1.xyz
			+ (t3 - t2) * op[1].tangent;

		float3 linPos = lerp(opPos0.xyz, opPos1.xyz, t);

		position = lerp(linPos, position, _HermiteInterpolate);

        output.dataIndex = op[0].dataIndex;

        output.vertex = float4(position, 1);
        output.normal = lerp(op[0].normal, op[1].normal, t) * scaleFactor;
        output.tangent = lerp(op[0].tangent, op[1].tangent, t);
        output.bitangent = lerp(op[0].bitangent, op[1].bitangent, t) * scaleFactor;
        output.color = lerp(op[0].color, op[1].color, t);
        output.uv = lerp(op[0].uv, op[1].uv, t);

        float lineLength = data.LineLength;

        float distanceFromStart = output.uv.y;
        float distanceFromEnd = lineLength - distanceFromStart;

        float taperDistStart = min(_TaperDistStart, 0.5 * lineLength);
        float taperDistEnd = min(_TaperDistEnd, 0.5 * lineLength);

        float taperFac = smoothstep(0, taperDistStart, distanceFromStart) * smoothstep(0, lerp(0, taperDistEnd, data.EndTaperFactor), distanceFromEnd);

        float taperFacShape = lerp(1, taperFac, data.TaperAmountShape);

        output.normal *= taperFacShape;
        output.bitangent *= taperFacShape;
        output.color.a *= lerp(1, taperFac, data.TaperAmountOpacity);

        output.color.a *= data.LineCount >= 2 ? 1.0/(uv.y*10+1) : 1; 
        
        return output;
    }

    void emitSplat(v2g IN[2], inout TriangleStream<g2f> tristream) {
        v2g v = IN[0];

        float xScale = length(v.bitangent);
        float yScale = length(v.normal);

        float scale =  ( 0.75 + rand(v.vertex.xy+0.75) * 0.5) * (xScale + yScale) * 2;

        float3 right = normalize(float3(rand(v.vertex.xy+0.4), rand(v.vertex.xy+0.13), rand(v.vertex.xy+0.8)) -0.5 );
        float3 up = normalize(float3(rand(v.vertex.xy+0.12), rand(v.vertex.xy+0.124), rand(v.vertex.xy+0.5807)) -0.5 );

        float3 forward = cross(right, up);

        right = cross(up, forward);

        right *= scale;
        up *= scale;

        float uvx = 0; //rand(v.vertex.xy+0.124124) < 0.5 ? 0.0 : 0.5;
        float uvy = 0; //rand(v.vertex.xy+0.456345) < 0.5 ? 0.0 : 0.5;

        float uvsize = 1;

        v.vertex.xyz += ( float3(rand(v.vertex.xy+0.1), rand(v.vertex.xz+4), rand(v.vertex.zy+1)) * 2 - 1) *  (xScale + yScale) * 0.5;

        float3 worldSpaceForward = normalize( mul(transpose(unity_WorldToObject), float4(forward, 0)));
        float3 viewDir = normalize(WorldSpaceViewDir(v.vertex));

        g2f o;
        o.dataIndex = v.dataIndex;
        o.color = v.color;
        o.color.a *= abs(dot(worldSpaceForward, -viewDir));
        o.uv = v.uv;
        o.objNorm = forward;
            
        v.vertex.xyz += -right * 0.5 + up * 0.5; 
        o.splatUv = float2(uvx, uvy);
        o.objPos = v.vertex.xyz;
        o.pos = UnityObjectToClipPos(v.vertex);
		o.screenPos = ComputeScreenPos(o.pos);
        tristream.Append(o);

        v.vertex.xyz += right; 
        o.objPos = v.vertex.xyz;
        o.pos = UnityObjectToClipPos(v.vertex);
		o.screenPos = ComputeScreenPos(o.pos);
		o.splatUv = float2(uvx+uvsize, uvy);
        tristream.Append(o);

        v.vertex.xyz += -right - up; 
        o.objPos = v.vertex.xyz;
        o.pos = UnityObjectToClipPos(v.vertex);
		o.screenPos = ComputeScreenPos(o.pos);
		o.splatUv = float2(uvx, uvy+uvsize);
        tristream.Append(o);

        v.vertex.xyz += right; 
        o.objPos = v.vertex.xyz;
        o.pos = UnityObjectToClipPos(v.vertex);
		o.screenPos = ComputeScreenPos(o.pos);
		o.splatUv = float2(uvx+uvsize, uvy+uvsize);
        tristream.Append(o);

        tristream.RestartStrip();
    }
                    

    void emitCube(v2g IN[2], inout TriangleStream<g2f> tristream) {
        float3 startPos = IN[0].vertex.xyz;
        float3 rightStart = IN[0].bitangent;
        float3 upStart = IN[0].normal;

        float3 endPos = IN[1].vertex.xyz;
        float3 rightEnd = IN[1].bitangent;
        float3 upEnd = IN[1].normal;
        float3 startVerts[4] = {
            startPos - rightStart + upStart,
            startPos + rightStart + upStart,
            startPos + rightStart - upStart,
            startPos - rightStart - upStart,
        };

        float3 endVerts[4] = {
            endPos - rightEnd + upEnd,
            endPos + rightEnd + upEnd,
            endPos + rightEnd - upEnd,
            endPos - rightEnd - upEnd,
        };
        
        
        v2g v = IN[0];

        for(int i = 0; i <= 4; i++) {
            int index = i == 4 ? 0 : i;
            index = 3 - index;

            {
                g2f o;
                o.dataIndex = IN[1].dataIndex;
                o.splatUv = 0;
                o.color = IN[1].color;
                o.uv = IN[1].uv;
                o.uv.x = fmod(index,2);
                v.vertex.xyz = endVerts[index];
                o.objNorm = v.vertex.xyz-endPos;

                o.objPos = v.vertex.xyz;
                TRANSFER_COVERAGE_DATA_VERT(v, o);
                tristream.Append(o);
            }

            {
                g2f o;
                o.dataIndex = IN[0].dataIndex;
                o.splatUv = 0;
                o.color = IN[0].color;
                o.uv = IN[0].uv;
                o.uv.x = fmod(index,2);
                v.vertex.xyz = startVerts[index];
                o.objNorm = v.vertex.xyz-startPos;

                o.objPos = v.vertex.xyz;
                TRANSFER_COVERAGE_DATA_VERT(v, o);
                tristream.Append(o);
            }
        }
    }

    void emitRibbon(v2g IN[2], inout TriangleStream<g2f> tristream) {
        float3 startPos = IN[0].vertex.xyz;
        float3 rightStart = IN[0].bitangent;

        float3 endPos = IN[1].vertex.xyz;
        float3 rightEnd = IN[1].bitangent;
        float3 startVerts[2] = {
            startPos - rightStart,
            startPos + rightStart
        };

        float3 endVerts[2] = {
            endPos - rightEnd,
            endPos + rightEnd,
        };
        
        
        v2g v = IN[0];

        for(int i = 0; i <= 2; i++) {
            int index = i == 2 ? 0 : i;
            index = 1 - index;

            {
                g2f o;
                o.dataIndex = IN[1].dataIndex;
                o.splatUv = 0;
                o.color = IN[1].color;
                o.uv = IN[1].uv;
                o.uv.x = index;
                v.vertex.xyz = endVerts[index];
                o.objNorm = v.vertex.xyz-endPos;

                o.objPos = v.vertex.xyz;
                TRANSFER_COVERAGE_DATA_VERT(v, o);
                tristream.Append(o);
            }

            {
                g2f o;
                o.dataIndex = IN[0].dataIndex;
                o.splatUv = 0;
                o.color = IN[0].color;
                o.uv = IN[0].uv;
                o.uv.x = index;
                v.vertex.xyz = startVerts[index];
                o.objNorm = v.vertex.xyz-startPos;

                o.objPos = v.vertex.xyz;
                TRANSFER_COVERAGE_DATA_VERT(v, o);
                tristream.Append(o);
            }
        }
    }

    void emitSphere(v2g IN[2], inout TriangleStream<g2f> tristream) {
        float3 startPos = IN[0].vertex.xyz;
        float3 rightStart = IN[0].bitangent;
        float3 upStart = IN[0].normal;


        float3 startVerts[8] = {
            startPos + rightStart * 1 +			     upStart * 0,
            startPos + rightStart * 0.707106781 +    upStart * 0.707106781,
            startPos + rightStart * 0			 +	 upStart * 1,
            startPos + rightStart * -0.707106781 +	 upStart * 0.707106781,
            startPos + rightStart * -1 +			 upStart * 0,
            startPos + rightStart * -0.707106781 +	 upStart * -0.707106781,
            startPos + rightStart * -0 +			 upStart * -1,
            startPos + rightStart * 0.707106781 +	 upStart * -0.707106781,
        };

        float3 endPos = IN[1].vertex.xyz;
        float3 rightEnd = IN[1].bitangent;
        float3 upEnd = IN[1].normal;
        float3 endVerts[8] = {
            endPos + rightEnd * 1 +			     upEnd * 0,
            endPos + rightEnd * 0.707106781 +    upEnd * 0.707106781,
            endPos + rightEnd * 0			 +	 upEnd * 1,
            endPos + rightEnd * -0.707106781 +	 upEnd * 0.707106781,
            endPos + rightEnd * -1 +			 upEnd * 0,
            endPos + rightEnd * -0.707106781 +	 upEnd * -0.707106781,
            endPos + rightEnd * -0 +			 upEnd * -1,
            endPos + rightEnd * 0.707106781 +	 upEnd * -0.707106781,
        };

        v2g v = IN[0];

        for(int i = 0; i <= 8; i++) {
            int index = i == 8 ? 0 : i;
            
            {
                g2f o;
                o.dataIndex = IN[1].dataIndex;
                o.splatUv = 0;
                o.color = IN[1].color;
                o.uv = IN[1].uv;
                o.uv.x = fmod(index * 0.25,1);
                v.vertex.xyz = endVerts[index];
                o.objNorm = v.vertex.xyz-endPos;
                o.objPos = v.vertex.xyz;
                TRANSFER_COVERAGE_DATA_VERT(v, o);
                tristream.Append(o);
            }

            {
                g2f o;
                o.dataIndex = IN[0].dataIndex;
                o.splatUv = 0;
                o.color = IN[0].color;
                o.uv = IN[0].uv;
                o.uv.x = fmod(index * 0.25,1);
                v.vertex.xyz = startVerts[index];
                o.objNorm = v.vertex.xyz-startPos;
                o.objPos = v.vertex.xyz;
                TRANSFER_COVERAGE_DATA_VERT(v, o);
                tristream.Append(o);
            }
        }
    }

    void emitLine(v2g IN[2], inout TriangleStream<g2f> tristream) {
        v2g v0 = IN[0];
        v2g v1 = IN[1];

        float4 t0 =  UnityObjectToClipPos(v0.vertex + float4(v0.tangent, 0));
        float4 t1 =  UnityObjectToClipPos(v1.vertex + float4(v1.tangent, 0));

        float3 v0Obj = v0.vertex.xyz;
        float3 v1Obj = v1.vertex.xyz;
        v0.vertex =  UnityObjectToClipPos(v0.vertex);
        v1.vertex =  UnityObjectToClipPos(v1.vertex);

        float3 v0dir = normalize(t0.xyz/t0.w - v0.vertex.xyz/v0.vertex.w);
        float4 vnorm0 = float4(cross(v0dir, float3(0, 0, -_ProjectionParams.x)), 0) * length(v0.bitangent) * 3;

        float3 v1dir = normalize(t1.xyz/t1.w - v1.vertex.xyz/v1.vertex.w);
        float4 vnorm1 = float4(cross(v1dir, float3(0, 0, -_ProjectionParams.x)), 0) * length(v1.bitangent) * 3;

        g2f o;
        o.dataIndex = v0.dataIndex;
        o.splatUv = 0;

        o.color = v0.color;
        o.uv = v0.uv;
        o.objPos = v0Obj;
        o.objNorm = v0.normal;
        
        o.pos = v0.vertex - vnorm0 * v0.vertex.w;
		o.screenPos = ComputeScreenPos(o.pos);
        tristream.Append(o);

        o.pos = v0.vertex + vnorm0 * v0.vertex.w;
		o.screenPos = ComputeScreenPos(o.pos);
        tristream.Append(o);

        o.color = v1.color;
        o.uv = v1.uv;
        o.objPos = v1Obj;
        o.objNorm = v1.normal;

        o.pos = v1.vertex - vnorm1 * v1.vertex.w;
		o.screenPos = ComputeScreenPos(o.pos);
        tristream.Append(o);

        o.pos = v1.vertex + vnorm1 * v1.vertex.w;
		o.screenPos = ComputeScreenPos(o.pos);
        tristream.Append(o);
    }

    [maxvertexcount(18)]
    void geom(line v2g IN[2], inout TriangleStream<g2f> tristream) {

        PerLineDataS data = PerLineData[IN[0].dataIndex];

        if(data.BrushType == 0) {
            emitSphere(IN, tristream);
        } else if (data.BrushType == 1) {
            emitCube(IN, tristream);
        } else if (data.BrushType == 2) {
            emitSplat(IN, tristream);
        } else if (data.BrushType == 3) {
            emitLine(IN, tristream);
        } else if (data.BrushType == 4) {
            emitRibbon(IN, tristream);
        }
    }

    float3 desaturate(float3 val, float fac) {
        float grayscale = 0.3 * val.r + 0.59 * val.g + 0.11 * val.b;
        return lerp(val, grayscale, fac);
    }

    float4 frag(g2f i) : SV_Target {

        PerLineDataS data = PerLineData[i.dataIndex];
        float4 vertColor = lerp(float4(1,1,1, i.color.a), i.color, _UseVertexColors);
        float4 texSample = 1;

		[branch]
        if(data.TextureIndex == 1) {
            float lineLength = data.LineLength;
            float distanceFromStart = i.uv.y;
            float distanceFromEnd = lineLength - distanceFromStart;

            float taperDistStart = min(_TaperDistStart, 0.5 * lineLength);
            float taperDistEnd = min(_TaperDistEnd, 0.5 * lineLength);

            float startFactor = smoothstep(0, -0.5*taperDistStart, distanceFromStart-taperDistStart);
            float endFactor = smoothstep(0, -0.5*taperDistEnd, distanceFromEnd-taperDistEnd) * data.EndTaperFactor;
            float middleFactor = 1.0 - startFactor - endFactor;

            i.uv += float2(0, 0.001);

            float2 endUV = float2(0,lineLength+0.002) - i.uv;

            float4 textureSampleStart = UNITY_SAMPLE_TEX2DARRAY(BrushTextures, float3(i.uv * float2(1.0/3,1.0/_TaperDistStart), data.TextureIndex)); 
            float4 textureSampleEnd = UNITY_SAMPLE_TEX2DARRAY(BrushTextures, float3(endUV * float2(-1.0/3,1.0/_TaperDistEnd) + float2(2.0/3,0), data.TextureIndex)); 

            float2 middleUV = i.uv * float2(1.0/3,3) + float2(1.0/3,0);
            float alongLine = middleUV.y;
            float blendFactor = 1.0f - 2 * abs(fmod(alongLine, 1) - 0.5);

            float4 textureSampleMiddle = UNITY_SAMPLE_TEX2DARRAY(BrushTextures, float3(middleUV, data.TextureIndex)) * blendFactor +
                                             UNITY_SAMPLE_TEX2DARRAY(BrushTextures, float3( float2(-1, 1) * middleUV+float2(0,0.5), data.TextureIndex)) * (1.0 - blendFactor); 

            texSample = textureSampleStart * startFactor + 
                        textureSampleMiddle * middleFactor + 
                        textureSampleEnd * endFactor;
        }


        float4 c = texSample * float4(pow( vertColor.rgb, 2.2), vertColor.a) * _Color;

        if(data.BrushType == 2) c *= tex2D (_SplatTex, i.splatUv);


		[branch]
        if(data.UseTextureObjectSpace > 0.5) {
			float3 objNorm = normalize( i.objNorm );

			half3 blend = abs(objNorm);
            // make sure the weights sum up to 1 (divide by sum of x+y+z)
            blend /= dot(blend,1.0);
			half3 objSpacePos = i.objPos * 2;
            // read the three texture projections, for x,y,z axes
            fixed4 diff_x = UNITY_SAMPLE_TEX2DARRAY(BrushTextures, float3(objSpacePos.yz, data.TextureIndex));
            fixed4 diff_y = UNITY_SAMPLE_TEX2DARRAY(BrushTextures, float3(objSpacePos.xz, data.TextureIndex));
            fixed4 diff_z = UNITY_SAMPLE_TEX2DARRAY(BrushTextures, float3(objSpacePos.xy, data.TextureIndex));
            // blend the textures based on weights
			fixed4 diff = diff_x * blend.x + diff_y * blend.y + diff_z * blend.z;

			c *= diff;
        }


		CoverageFragmentInfo f;
		TRANSFER_COVERAGE_DATA_FRAG(i, f);
		f.id = i.dataIndex;

#if FANCY_RENDER_ON
		float3 worldNorm = UnityObjectToWorldNormal(i.objNorm);
		float3 worldSpaceViewDir = normalize(WorldSpaceViewDir(float4(i.objPos, 1)));
		float edge =  saturate(dot(worldSpaceViewDir, worldNorm));
		c.a *= edge * edge; // lerp(0, c.a, (1.0 - blueNoise(i.screenPos.xy / i.screenPos.w * _ScreenParams.xy)) * edge + edge);
		c.rgb -= blueNoise(int3(f.screenPos.x, f.screenPos.y, i.dataIndex)) * c.rgb * 0.3;

		half nl = max(0, dot(worldNorm, _WorldSpaceLightPos0.xyz));
		fixed3 lighting = nl * _LightColor0.rgb;
		c.rgb *= lighting;
#endif

        float highlightFactor = saturate((1-DoHighlighting) + data.Highlight);
        c.a = lerp(c.a * 0.55, c.a, highlightFactor);

		if (data.IsPulsing > 0.5) c.a *= 0.7 + sin(_Time.y*10) * 0.2;

        c.rgb = desaturate(saturate(c.rgb), highlightFactor * 0.5 * DoHighlighting);

        return ApplyCoverage(c, f);
    }

    ENDCG
    		
	SubShader {  
	    Tags { "Queue"="Geometry" "RenderType"="Opaque" "DisableBatching" = "True"  }
		Pass {
            Tags { "Queue"="Geometry" "RenderType"="Opaque" "DisableBatching" = "True"  }
            LOD 200
            Cull Back
            Blend Off
            ZWrite [_ZWrite]
			AlphaToMask On
                
            CGPROGRAM
            #pragma target 5.0
            #pragma vertex vert
            #pragma hull hull
            #pragma domain domain
            #pragma geometry geom
            #pragma fragment frag
            ENDCG
		}
		
        Pass {
            Name "ShadowCaster"
            Tags { "Queue"="Geometry" "RenderType"="Opaque" "DisableBatching" = "True" "LightMode" = "ShadowCaster" }
			
			AlphaToMask On
            Fog { Mode Off }
            ZWrite On ZTest Less Cull Off
            Offset 1, 1
                   
            CGPROGRAM
            #pragma multi_compile_shadowcaster
            #pragma fragmentoption ARB_precision_hint_fastest
            #pragma target 5.0
            #pragma vertex vert
            #pragma hull hull
            #pragma domain domain
            #pragma geometry geom
            #pragma fragment frag_shadow
            
            float4 frag_shadow( g2f i ) : COLOR
            {
                return 1;
            }
            ENDCG
        }
	}
}