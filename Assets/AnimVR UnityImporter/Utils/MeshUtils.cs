#if UNITY_EDITOR || ANIM_RUNTIME_AVAILABLE
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class MeshUtils
{
    #region Simplification
    private const float CURVATURE_TOLERANCE = 0.1f;
    private const float DISTANCE_TOLERANCE_FACTOR = 1.5f;
    private const float COLOR_TOLERANCE = 0.04f;
    private const float LIGHT_TOLERANCE = 0.04f;

    static float CURR_CURVATURE_TOLERANCE, CURR_DISTANCE_TOLERANCE_FACTOR, CURR_COLOR_TOLERANCE, CURR_LIGHT_TOLERANCE;

    public static float Distance(Color a, Color b)
    {
        float rDiff = a.r - b.r;
        float gDiff = a.g - b.g;
        float bDiff = a.b - b.b;
        float aDiff = a.a - b.a;
        return Mathf.Sqrt(rDiff * rDiff + gDiff * gDiff + bDiff * bDiff + aDiff * aDiff);
    }
    
    public static void SimplifyStage(StageData stage, float factor = 1)
    {
        float f;
        SimplifyStage(stage, factor, out f);
    }

    public static void SimplifyStage(StageData stage, float factor, out float reduceFactor)
    {
        CURR_CURVATURE_TOLERANCE = CURVATURE_TOLERANCE * factor;
        CURR_DISTANCE_TOLERANCE_FACTOR = DISTANCE_TOLERANCE_FACTOR * factor;
        CURR_COLOR_TOLERANCE = COLOR_TOLERANCE * factor;
        CURR_LIGHT_TOLERANCE = LIGHT_TOLERANCE * factor;

        int prevCount = 0, newCount = 0;
        foreach (var symbol in stage.Symbols)
        {
            SimplifyPlayable(symbol, ref prevCount, ref newCount);
        }

        reduceFactor = ((float) newCount) / prevCount;
    }
    
    
    public static void SimplifyPlayable(PlayableData playable, ref int prevCount, ref int newCount)
    {
        if (playable is SymbolData) SimplifyPlayable(playable as SymbolData, ref prevCount, ref newCount);
        else if (playable is TimeLineData) SimplifyPlayable(playable as TimeLineData, ref prevCount, ref newCount);
    }
    
    public static void SimplifyPlayable(SymbolData playable, ref int prevCount, ref int newCount)
    {
        foreach (var p in playable.Playables)
        {
            SimplifyPlayable(p, ref prevCount, ref newCount);
        }
    }

    public static void SimplifyPlayable(TimeLineData playable, ref int prevCount, ref int newCount)
    {
        foreach (var frame in playable.Frames)
        {
            if(frame.isInstance) continue;

            foreach (var line in frame.Lines)
            {
                SimplifyLineData(line, ref prevCount, ref newCount);
            }
        }
    }
    
    
    public static void SimplifyLineData(LineData line, ref int prevCount, ref int newCount)
    {
        prevCount += line.Points.Count;
        if (line.Points.Count < 3) return;

        List<int> pointsToKeep = new List<int>();
        
        Vector3 prevDir = (line.Points[1].V3 - line.Points[0].V3).normalized;
        Color prevColor = line.colors[0];
        float prevLight = line.light[0];
        
        pointsToKeep.Add(0);
        
        double curveCos = Mathf.Cos(CURR_CURVATURE_TOLERANCE);

        for (int i = 1; i < line.Points.Count-1; i++) {

            var curr = line.Points[i].V3;

            int nextIndex = (i + 1);
            var next = line.Points[nextIndex].V3;
		
            Vector3 dir = next - curr;
            float length = dir.magnitude;

            Vector3 norm = dir * 1.0f/length;

            bool outsideLengthTolerance = length > line.widths[i] * CURR_DISTANCE_TOLERANCE_FACTOR;
            bool outsideCurvatureTolerance = Vector3.Dot(norm, prevDir) < curveCos;

            var currCol = line.colors[i];
            var currLight = line.light[i];

            bool outsideColorTolerance = Distance(currCol, prevColor) > CURR_COLOR_TOLERANCE;

            bool outsideLightTolerance = Mathf.Abs(currLight - prevLight) > CURR_LIGHT_TOLERANCE;
            
            if (length < 0.0000001f || outsideCurvatureTolerance || outsideLengthTolerance || outsideColorTolerance || outsideLightTolerance) {
                pointsToKeep.Add(i);
                prevDir = norm;
                prevColor = currCol;
                prevLight = currLight;
            }
        }
        
        pointsToKeep.Add(line.Points.Count-1);

        newCount += pointsToKeep.Count;

        List<float> widths = new List<float>(pointsToKeep.Count);
        List<SerializableVector3> points = new List<SerializableVector3>(pointsToKeep.Count);
        List<SerializableQuaternion> rotations = new List<SerializableQuaternion>(pointsToKeep.Count);
        List<SerializableColor> colors = new List<SerializableColor>(pointsToKeep.Count);
        List<float> light = new List<float>(pointsToKeep.Count);

        for (int i = 0; i < pointsToKeep.Count; i++)
        {
            int id = pointsToKeep[i];
            widths.Add(line.widths[id]);
            points.Add(line.Points[id]);
            rotations.Add(line.rotations[id]);
            colors.Add(line.colors[id]);
            light.Add(line.light[id]);
        }

        line.widths = widths;
        line.Points = points;
        line.rotations = rotations;
        line.colors = colors;
        line.light = light;
        
    }
    #endregion

    #region Geometry Generation

    private struct Vertex
    {
        public Vector3 vertex;
        public Vector4 tangent;
        public Vector3 normal;
        public Vector2 uv;
        public Color color;
    }

    private struct v2g
    {
        public Vector3 vertex;
        public Vector3 tangent;
        public Vector3 bitangent;
        public Vector3 normal;
        public Vector2 uv;
        public Color color;
    }

    struct g2f
    {
        public Color color;
        public Vector2 uv;
        public Vector2 splatUv;
        public Vector3 objPos;
        public Vector3 objNorm;
	};

    static float _TaperDistStart = 0.1f;
    static float _TaperDistEnd = 0.1f;
    static float _EndTaperFactor = 1;

    static int _BrushType;
    static float _LineCount;
    static float _LineLength;
    static float _TaperAmountShape;
    static float _TaperAmountOpacity;
    //static float _ConstantSize;

    public static void GeneratePositionData(LineData data, List<Vector3> positions, List<int> indices, List<Vector4> colors)
    {
        if (data.Points.Count < 2) return;

        List<TubeRenderer.TubeVertex> tubeVertices = new List<TubeRenderer.TubeVertex>(data.Points.Count);

        for (int i = 0; i < data.Points.Count; i++)
        {
            TubeRenderer.AddPoint(data.Points[i].V3, data.rotations[i].Q, data.widths[i], data.colors[i].C, data.light[i], tubeVertices);
        }

        int additionalVertices = data.brushType == BrushType.Sphere ? 8 : 2;
        int actualVertexCount = data.Points.Count + additionalVertices;

        List<Vector3> meshVertices = new List<Vector3>(Enumerable.Repeat(default(Vector3), actualVertexCount));
        List<Vector4> meshTangents = new List<Vector4>(Enumerable.Repeat(default(Vector4), actualVertexCount));
        List<Vector3> meshNormals = new List<Vector3>(Enumerable.Repeat(default(Vector3), actualVertexCount));
        List<Vector2> meshUvs = new List<Vector2>(Enumerable.Repeat(default(Vector2), actualVertexCount));
        List<Color> meshColors = new List<Color>(Enumerable.Repeat(default(Color), actualVertexCount));

        int[] meshIndices = null;

        TubeRenderer.GenerateMesh(tubeVertices, 0, data.isFlat, data.isWeb, data.brushType, new Vector2(1, data.isFlat ? 0.3f : 1) * 0.5f,
            meshVertices, meshTangents, meshNormals, meshUvs, meshColors, ref meshIndices, ref _LineLength);

        _BrushType = (int)data.brushType;
        _TaperAmountShape = data.taperShape ? 1 : 0;
        _TaperAmountOpacity = data.taperOpacity ? 1 : 0;
        _LineCount = data.multiLine ? 12f : 1.0f;

        List<g2f> tristream = new List<g2f>(meshIndices.Length * 3);

        for (int l = 0; l < 1; l++)
        {
            for (int i = 0; i < meshIndices.Length / 2; i++)
            {
                int i1 = meshIndices[i * 2 + 0];
                int i2 = meshIndices[i * 2 + 1];

                v2g v1 = vert(new Vertex() { vertex = meshVertices[i1], color = meshColors[i1], normal = meshNormals[i1], tangent = meshTangents[i1], uv = meshUvs[i1] });
                v2g v2 = vert(new Vertex() { vertex = meshVertices[i2], color = meshColors[i2], normal = meshNormals[i2], tangent = meshTangents[i2], uv = meshUvs[i2] });

                v2g[] op = new v2g[2]
                {
                v1, v2
                };


                v2g[] IN = new v2g[2]
                {
                v1, domain(op, new Vector2(0, l/_LineCount), _LineCount)
                };

                for (int u = 1; u <= 2; u++)
                {
                    IN[0] = IN[1];
                    IN[1] = domain(op, new Vector2(((float)u) / 2, l / _LineCount), _LineCount);
                    geom(IN, tristream);
                }
            }



            if (tristream.Count < 3)
            {
                continue;
            }

            int strip0 = positions.Count;
            int strip1 = positions.Count + 1;
            int strip2 = positions.Count + 2;

            var pos = tristream[0].objPos;
            data.transform.ApplyTo(ref pos);
            positions.Add(pos);
            colors.Add(tristream[0].color);

            pos = tristream[1].objPos;
            data.transform.ApplyTo(ref pos);
            positions.Add(pos);
            colors.Add(tristream[1].color);

            bool flip = _BrushType != 0;

            for (int i = 2; i < tristream.Count; i++)
            {
                if (flip)
                {
                    indices.Add(strip2);
                    indices.Add(strip1);
                    indices.Add(strip0);
                }
                else
                {
                    indices.Add(strip0);
                    indices.Add(strip1);
                    indices.Add(strip2);
                }

                flip = !flip;

                strip0 = strip1;
                strip1 = strip2;
                strip2++;


                pos = tristream[i].objPos;
                data.transform.ApplyTo(ref pos);

                positions.Add(pos);
                colors.Add(tristream[i].color);
            }
        }
    }

    private static float length(Vector3 v) { return v.magnitude; }
    private static Vector3 normalize(Vector3 v) { return v.normalized; }
    private static Vector3 cross(Vector3 a, Vector3 b) { return Vector3.Cross(a,b); }
    private static float frac(float v) { return v - (int)v; }

    private static float rand(Vector2 v)
    {
        return  frac(Mathf.Sin(Vector2.Dot(v, new Vector2(12.9898f, 78.233f))) * 43758.5453f);
    }

    private static void emitSplat(v2g[] IN, List<g2f> tristream)
    {
        v2g v = IN[0];

        float xScale = length(v.bitangent);
        float yScale = length(v.normal);

        Vector2 vxy = new Vector2(v.vertex.x, v.vertex.y);
        Vector2 vxz = new Vector2(v.vertex.x, v.vertex.z);
        Vector2 vzy = new Vector2(v.vertex.z, v.vertex.y);

        float scale = (0.75f + rand(vxy + Vector2.one*0.75f) * 0.5f) * (xScale + yScale) * 2;

        Vector3 right = normalize(new Vector3(rand(vxy + Vector2.one * 0.4f), rand(vxy + Vector2.one * 0.13f), rand(vxy + Vector2.one * 0.8f)) - Vector3.one * 0.5f);
        Vector3 up = normalize(new Vector3(rand(vxy + Vector2.one * 0.12f), rand(vxy + Vector2.one * 0.124f), rand(vxy + Vector2.one * 0.5807f)) - Vector3.one*0.5f);

        Vector3 forward = cross(right, up);

        right = cross(up, forward);

        right *= scale;
        up *= scale;

        float uvx = 0; //rand(vxy+0.124124) < 0.5 ? 0.0 : 0.5;
        float uvy = 0; //rand(vxy+0.456345) < 0.5 ? 0.0 : 0.5;

        float uvsize = 1;

        v.vertex += (new Vector3(rand(vxy + Vector2.one*0.1f), rand(vxz + Vector2.one * 4), rand(vzy + Vector2.one * 1)) * 2 - Vector3.one * 1) * (xScale + yScale) * 0.5f;

        g2f o;
        o.color = v.color;
        o.uv = v.uv;
        o.objNorm = forward;

        v.vertex += -right * 0.5f + up * 0.5f;
        o.splatUv = new Vector2(uvx, uvy);
        o.objPos = v.vertex;
        tristream.Add(o);

        v.vertex += right;
        o.objPos = v.vertex;
        o.splatUv = new Vector2(uvx + uvsize, uvy);
        tristream.Add(o);

        v.vertex += -right - up;
        o.objPos = v.vertex;
        o.splatUv = new Vector2(uvx, uvy + uvsize);
        tristream.Add(o);

        v.vertex += right;
        o.objPos = v.vertex;
        o.splatUv = new Vector2(uvx + uvsize, uvy + uvsize);
        tristream.Add(o);
    }


    private static void emitCube(v2g[] IN, List<g2f> tristream)
    {
        Vector3 startPos = IN[0].vertex;
        Vector3 rightStart = IN[0].bitangent;
        Vector3 upStart = IN[0].normal;

        Vector3 endPos = IN[1].vertex;
        Vector3 rightEnd = IN[1].bitangent;
        Vector3 upEnd = IN[1].normal;
        Vector3[] startVerts = new Vector3[] {
            startPos - rightStart + upStart,
            startPos + rightStart + upStart,
            startPos + rightStart - upStart,
            startPos - rightStart - upStart,
        };

        Vector3[] endVerts = new Vector3[] {
            endPos - rightEnd + upEnd,
            endPos + rightEnd + upEnd,
            endPos + rightEnd - upEnd,
            endPos - rightEnd - upEnd,
        };

        v2g v = IN[0];

        for (int i = 0; i <= 4; i++)
        {
            int index = i == 4 ? 0 : i;

            {
                g2f o;
                o.splatUv = Vector2.zero;
                o.color = IN[1].color;
                o.uv = IN[1].uv;
                o.uv.y = index * 0.25f;
                v.vertex = endVerts[index];
                o.objNorm = v.vertex - endPos;
                o.objPos = v.vertex;
                tristream.Add(o);
            }

            {
                g2f o;
                o.splatUv = Vector2.zero;
                o.color = IN[0].color;
                o.uv = IN[0].uv;
                o.uv.y = index * 0.25f;
                v.vertex = startVerts[index];
                o.objNorm = v.vertex - startPos;
                o.objPos = v.vertex;
                tristream.Add(o);
            }
        }
    }

    private static void emitSphere(v2g[] IN, List<g2f> tristream)
    {
        Vector3 startPos = IN[0].vertex;
        Vector3 rightStart = IN[0].bitangent;
        Vector3 upStart = IN[0].normal;

        Vector3 endPos = IN[1].vertex;
        Vector3 rightEnd = IN[1].bitangent;
        Vector3 upEnd = IN[1].normal;
        Vector3[] startVerts = new Vector3[] {
            startPos + rightStart * 1 +              upStart * 0,
            startPos + rightStart * 0.707106781f +    upStart * 0.707106781f,
            startPos + rightStart * 0            +   upStart * 1,
            startPos + rightStart * -0.707106781f +   upStart * 0.707106781f,
            startPos + rightStart * -1 +             upStart * 0,
            startPos + rightStart * -0.707106781f +   upStart * -0.707106781f,
            startPos + rightStart * -0 +             upStart * -1,
            startPos + rightStart * 0.707106781f +    upStart * -0.707106781f,
        };

        Vector3[] endVerts = {
            endPos + rightEnd * 1 +              upEnd * 0,
            endPos + rightEnd * 0.707106781f +    upEnd * 0.707106781f,
            endPos + rightEnd * 0            +   upEnd * 1,
            endPos + rightEnd * -0.707106781f +   upEnd * 0.707106781f,
            endPos + rightEnd * -1 +             upEnd * 0,
            endPos + rightEnd * -0.707106781f +   upEnd * -0.707106781f,
            endPos + rightEnd * -0 +             upEnd * -1,
            endPos + rightEnd * 0.707106781f +    upEnd * -0.707106781f,
        };


        v2g v = IN[0];

        for (int i = 0; i <= 8; i++)
        {
            int index = i == 8 ? 0 : i;

            {
                g2f o;
                o.splatUv = Vector2.zero;
                o.color = IN[1].color;
                o.uv = IN[1].uv;
                o.uv.y = index * 0.2f;
                v.vertex = endVerts[index];
                o.objNorm = v.vertex - endPos;
                o.objPos = v.vertex;
                tristream.Add(o);
            }

            {
                g2f o;
                o.splatUv = Vector2.zero;
                o.color = IN[0].color;
                o.uv = IN[0].uv;
                o.uv.y = index * 0.2f;
                v.vertex = startVerts[index];
                o.objNorm = v.vertex - startPos;
                o.objPos = v.vertex;
                tristream.Add(o);
            }
        }
    }

    private static void geom(v2g[] IN, List<g2f> tristream)
    {
        if (_BrushType == 0)
        {
            emitSphere(IN, tristream);
        }
        else if (_BrushType == 1)
        {
            emitCube(IN, tristream);
        }
        else if (_BrushType == 2)
        {
            emitSplat(IN, tristream);
        }
        /*else if (_BrushType == 3)
        {
            emitLine(IN, tristream);
        }*/
        else
        {
            emitCube(IN, tristream);
        }
    }

    private static Vector3 normalizeOrZero(Vector3 val)
    {
        float len = val.magnitude;
        if (len < 0.00001) return Vector3.zero;
        return val / len;
    }

    private static v2g vert(Vertex v)
    {
        v2g o = new v2g();
        o.vertex = v.vertex;
        o.color = v.color;
        o.uv = v.uv;

        float lineWidthScale = 1; // lerp(1, 1.0 / mul(float4(1, 0, 0, 0), unity_ObjectToWorld).x, _ConstantSize);

        o.normal = v.normal * lineWidthScale;
        o.tangent = new Vector3(v.tangent.x, v.tangent.y, v.tangent.z);
        o.bitangent = Vector3.Cross(normalizeOrZero(v.normal), normalizeOrZero(o.tangent)) * v.tangent.w * lineWidthScale;

        return o;
    }

    public static float smoothstep(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp((x - edge0) / (edge1 - edge0), 0.0f, 1.0f);
        if (float.IsNaN(t)) t = 0;
        return t * t * (3.0f - 2.0f * t);
    }

    private static v2g domain(v2g[] op, Vector2 uv, float _LineCount)
    {
        Vector3 opPos0 = op[0].vertex;
        Vector3 opPos1 = op[1].vertex;
        float scaleFactor = 1;
        if (_LineCount >= 2)
        {
            float firstDistFromStart = op[0].uv.y;
            Vector3 offsetStart = op[0].bitangent * Noise.Generate(firstDistFromStart * 20, uv.y * 10) + op[0].normal * Noise.Generate(firstDistFromStart * 20 + 200, uv.y * 10 + 200);
            offsetStart = offsetStart * (uv.y * uv.y + 0.5f);

            float secDistFromStart = op[1].uv.y;
            Vector3 offsetEnd = op[1].bitangent * Noise.Generate(secDistFromStart * 20, uv.y * 10) + op[1].normal * Noise.Generate(secDistFromStart * 20 + 200, uv.y * 10 + 200);
            offsetEnd = offsetEnd * (uv.y * uv.y + 0.5f);

            opPos0 += offsetStart;
            opPos1 += offsetEnd;

            scaleFactor = 1.0f / (_LineCount * 0.33f);
        }

        v2g output;
        float t = uv.x;

        float t2 = t * t;
        float t3 = t2 * t;

        Vector3 position = (2.0f * t3 - 3.0f * t2 + 1.0f) * opPos0
                        + (t3 - 2.0f * t2 + t) * op[0].tangent
                        + (-2.0f * t3 + 3.0f * t2) * opPos1
                        + (t3 - t2) * op[1].tangent;


        output.vertex = position;
        output.normal =     Vector3.Lerp(op[0].normal, op[1].normal, t) * scaleFactor;
        output.tangent =    Vector3.Lerp(op[0].tangent, op[1].tangent, t);
        output.bitangent =  Vector3.Lerp(op[0].bitangent, op[1].bitangent, t) * scaleFactor;
        output.color =      Color.Lerp(op[0].color, op[1].color, t);
        output.uv =         Vector3.Lerp(op[0].uv, op[1].uv, t);


        float distanceFromStart = output.uv.y;
        float distanceFromEnd = _LineLength - distanceFromStart;

        float taperDistStart = Mathf.Min(_TaperDistStart, 0.5f * _LineLength);
        float taperDistEnd = Mathf.Min(_TaperDistEnd, 0.5f * _LineLength);

        float taperFac = smoothstep(0, taperDistStart, distanceFromStart) * smoothstep(0, Mathf.Lerp(0, taperDistEnd, _EndTaperFactor), distanceFromEnd);

        float taperFacShape = Mathf.Lerp(1, taperFac, _TaperAmountShape);

        output.normal *= taperFacShape;
        output.bitangent *= taperFacShape;
        output.color.a *= Mathf.Lerp(1, taperFac, _TaperAmountOpacity);
        output.color.a *= _LineCount >= 2 ? 1.0f / (uv.y * 10 + 1) : 1;

        return output;
    }
#endregion

    public static Mesh MeshFromData(MeshData data)
    {
        var mesh = new Mesh();
        mesh.vertices = data.vertices.Select((v) => v.V3).ToArray();

        if (data.normals != null)
        {
            mesh.normals = data.normals.Select((v) => v.V3).ToArray();
        }

        if (data.uvs != null && data.uvs.Length == mesh.vertices.Length)
        {
            mesh.uv = data.uvs.Select((v) => v.V2).ToArray();
        }

        if (data.colors != null && data.colors.Length == mesh.vertices.Length)
        {
            mesh.colors = data.colors.Select((v) => v.C).ToArray();
        }

        mesh.triangles = data.triangles;
        mesh.RecalculateBounds();

        if (data.normals == null)
        {
            mesh.RecalculateNormals();
        }

        return mesh;
    }

    public static Material MaterialFromData(MaterialData data, Material baseMat)
    {
        Material mat = new Material(baseMat);

        mat.SetColor("_Color", data.Diffuse.C);
        mat.SetColor("_SpecColor", data.Specular.C);
        mat.SetColor("_EmissionColor", data.Emissive.C);
        mat.SetFloat("_Unlit", data.ShaderType == MeshShaderType.Unlit ? 1 : 0);
        mat.SetColor("_TintColor", Color.white);
        mat.SetFloat("_Gamma", data.ColorSpace == ColorSpace.Linear ? 1 : 2.2f);

        if (data.DiffuseTex != null)
        {
            Texture2D diffuseTex = new Texture2D(1, 1);
            diffuseTex.LoadImage(data.DiffuseTex);
            diffuseTex.Apply();

            mat.mainTexture = diffuseTex;
        }

        return mat;
    }
}
#endif