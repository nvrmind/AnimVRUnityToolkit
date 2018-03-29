#if UNITY_EDITOR || ANIM_RUNTIME_AVAILABLE
using System; 
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;

[ExecuteInEditMode]
public class TubeRenderer : MonoBehaviour
{
    [StructLayout(LayoutKind.Sequential, Pack=1, Size=96)]
    public struct PerLineData
    {
        public Color LineColor;

        public float LineLength;
        public int OneSided;
        public BrushType BrushType;
        public float IsPulsing;

        public Vector3 FacingDir;
        public int TextureIndex;

        public float TaperAmountShape;
        public float TaperAmountOpacity;
        public float EndTaperFactor;
        public float ConstantSize;

        public float LineCount;
        public float UseTextureObjectSpace;
        public float Highlight;
        public float WorldScale;

        public int FrameDataIndex;
        public Vector3 __pad;
    }

    public static PerLineData[] PerLineDataCPU;
    public static ComputeBuffer PerLineDataBuffer;
    public static Queue<int> Freelist;
    public static bool LineDataIsDirty = false;

    public static float  EndScale = 1;


    int bufferDataIndex = -1;

    public float _EndScale = 1;

    public void OnValidate()
    {
        if (EndScale != _EndScale) EndScale = _EndScale;
    }


    [Serializable]
    public struct TubeVertex
    {
        public Vector3 point ;
        public Vector2 extends ;
        public Color color;
        public Quaternion orientation;

        public TubeVertex(Vector3 pt, Vector2 e, Color c, Quaternion o)
        {
            point = pt;
            extends = e;
            color = c;
            orientation = o;
        }
    }


    [NonSerialized]
    public List<TubeVertex> vertices = new List<TubeVertex>();
    public Material Material { set { renderer.sharedMaterial = value; } }

    public bool drawing = true;
    private bool _update;
    public bool update {
        get { return _update; }
        set
        {
            _update = value;
            if (bufferDataIndex == -1) return;
            PerLineDataCPU[bufferDataIndex].EndTaperFactor = _update ? 0.1f : 1;
            LineDataIsDirty = true;
        }
    }

    [Space]
    [Header("Material Settings")]
    public BrushType brushType;
    public BrushMode brushMode;

    private Color _lineColor = Color.white;

    public Color LineColor
    {
        get { return _lineColor; }
        set
        {
            _lineColor = value;
            if (bufferDataIndex == -1) return;
            PerLineDataCPU[bufferDataIndex].LineColor = LineColor;
            LineDataIsDirty = true;
        }
    }

    public int FrameDataIndex
    {
        set
        {
            if (bufferDataIndex == -1) return;
            PerLineDataCPU[bufferDataIndex].FrameDataIndex = value;
            LineDataIsDirty = true;
        }
    }

    private float _highlight = 0;
    public float Highlight
    {
        get { return _highlight; }
        set
        {
            _highlight = value;
            if (bufferDataIndex == -1) return;
            PerLineDataCPU[bufferDataIndex].Highlight = _highlight;
            LineDataIsDirty = true;
        }
    }

    private bool _isPulsing;
    public bool IsPulsing
    {
        get { return _isPulsing; }
        set
        {
            _isPulsing = value;
            if (bufferDataIndex == -1) return;
            PerLineDataCPU[bufferDataIndex].IsPulsing = _isPulsing ? 1 : 0;
            LineDataIsDirty = true;
        }
    }

    public bool OneSided = false;
    public bool Flat = false;
    public bool ConstantSize = true;
    public bool MultiLine = false;
    public bool Web = false;
    public bool ObjectSpaceTexture = false;
    public int TextureIndex = -1;
    public bool taperShape = true;
    public bool taperOpacity = true;
    public Vector3 FacingDir = Vector3.zero;

    public float currentTotalLength = 0;

    public Transform frame;

    public Vector2 ExtendsMultiplier
    {
        get
        {

            Vector2 baseScale = new Vector2(1, Flat ? 0.3f : 1);

                return baseScale * .5f;
        }
    }

    private Mesh mesh;

    private bool _useTaper;
    public bool useTaper
    {
        get
        {
            if (brushMode == BrushMode.Line)
                return false;
            else return _useTaper;
        }
        set { _useTaper = value; }
    }


    new public MeshRenderer renderer;
    private MeshFilter meshFilter;

    public void RenderTo(Camera cam)
    {
        Graphics.DrawMesh(mesh, transform.localToWorldMatrix, renderer.sharedMaterial, gameObject.layer, cam);
    }

    public void UpdateSettings()
    {
        if (bufferDataIndex == -1) return;

        PerLineDataCPU[bufferDataIndex].LineColor = LineColor;
        PerLineDataCPU[bufferDataIndex].LineLength = currentTotalLength;
        PerLineDataCPU[bufferDataIndex].BrushType = brushType;
        PerLineDataCPU[bufferDataIndex].TextureIndex = TextureIndex+1;

        PerLineDataCPU[bufferDataIndex].IsPulsing                = IsPulsing ? 1 : 0;
        PerLineDataCPU[bufferDataIndex].OneSided                 = OneSided ? 1 : 0;
        PerLineDataCPU[bufferDataIndex].TaperAmountShape         = taperShape ? 1 : 0;
        PerLineDataCPU[bufferDataIndex].TaperAmountOpacity       = taperOpacity ? 1 : 0;
        PerLineDataCPU[bufferDataIndex].LineCount                = MultiLine ? 12f : 1.0f;
        PerLineDataCPU[bufferDataIndex].ConstantSize             = ConstantSize ? 1 : 0;
        PerLineDataCPU[bufferDataIndex].UseTextureObjectSpace    = ObjectSpaceTexture ? 1 : 0;
        PerLineDataCPU[bufferDataIndex].FacingDir                = FacingDir;
        LineDataIsDirty = true;
    }

    private void Awake()
    {
        useTaper = true;

        renderer = gameObject.GetComponent<MeshRenderer>();
        meshFilter = gameObject.GetComponent<MeshFilter>();

        if (PerLineDataBuffer == null)
        {
            PerLineDataCPU = new PerLineData[2048];
            PerLineDataBuffer = new ComputeBuffer(PerLineDataCPU.Length, Marshal.SizeOf(typeof(PerLineData)), ComputeBufferType.Default);
            Freelist = new Queue<int>();
            for (int i = 0; i < PerLineDataCPU.Length; i++) Freelist.Enqueue(i);
        }

        if (Freelist.Count == 0)
        {
            var oldData = PerLineDataCPU.DeepCopy();
            PerLineDataCPU = new PerLineData[PerLineDataCPU.Length * 2];
            Array.Copy(oldData, PerLineDataCPU, oldData.Length);
            PerLineDataBuffer.Release();
            PerLineDataBuffer = new ComputeBuffer(PerLineDataCPU.Length, Marshal.SizeOf(typeof(PerLineData)), ComputeBufferType.Default);

            for (int i = oldData.Length; i < PerLineDataCPU.Length; i++) Freelist.Enqueue(i);
        }

        bufferDataIndex = Freelist.Dequeue();
        UpdateSettings();

        if (!renderer)
        {
            renderer = gameObject.AddComponent<MeshRenderer>();
            meshFilter = gameObject.AddComponent<MeshFilter>();
        }
        else
        {
            RebuildMesh();
        }
    }

    private void OnDestroy()
    {
        if(mesh)
        {
            if(Application.isEditor && !Application.isPlaying)
            {
                DestroyImmediate(mesh);
            }
            else
            {
                Destroy(mesh);
            }
        }

        Freelist.Enqueue(bufferDataIndex);
        bufferDataIndex = -1;
    }

    public void SetBrushOptions(LineData LineData)
    {
        brushType = LineData.brushType;
        brushMode = LineData.brushMode;
        OneSided = LineData.isOneSided;
        Flat = LineData.isFlat;
        taperOpacity = LineData.taperOpacity;
        taperShape = LineData.taperShape;
        ConstantSize = LineData.constantWidth;
        MultiLine = LineData.multiLine;
        Web = LineData.isWeb;
        ObjectSpaceTexture = LineData.isObjectSpaceTex;
        TextureIndex = LineData.textureIndex;
        FacingDir = LineData.cameraOrientations.Count > 0 ? LineData.cameraOrientations[0].Q * Vector3.forward : Vector3.forward;
    }

    public static void GenerateMesh(List<TubeVertex> vertices, int dataIndex, bool Flat, bool Web, BrushType brushType, Vector2 ExtendsMultiplier, 
        List<Vector3> meshVertices, List<Vector4> meshTangents,
        List<Vector3> meshNormals, List<Vector2> uvs, List<Color> colors, ref int[] indices, ref float length)
    {
        int additionalVertices = brushType == BrushType.Sphere ? 8 : 2;
        int actualVertexCount = vertices.Count + additionalVertices;


        List<int> tris = new List<int>();

        float currentTotalLength = 0;

        Action<int, TubeVertex, TubeVertex, TubeVertex> addPointFunc = (int i, TubeVertex v, TubeVertex prev, TubeVertex next) =>
        {
            var prevPoint = prev.point;
            var nextPoint = next.point;

            float dist = Vector3.Distance(prevPoint, v.point);
            currentTotalLength += dist;

            if (float.IsNaN(v.point.x) || float.IsNaN(v.point.z) || float.IsNaN(v.point.z))
                return;

            meshVertices[i] = v.point;
            if (Flat)
                meshNormals[i] = v.orientation * Vector3.up * Mathf.Clamp(ExtendsMultiplier.y * v.extends.y, 0, .005f);
            else
                meshNormals[i] = v.orientation * Vector3.up * ExtendsMultiplier.y * v.extends.y;
            // Tangents for hermite interpolation
            Vector3 dir = 0.5f * (nextPoint - prevPoint);
            // Width of the line
            meshTangents[i] = new Vector4(dir.x, dir.y, dir.z, v.extends.x * ExtendsMultiplier.x) ;

            uvs[i] = new Vector2(dataIndex, currentTotalLength);
            colors[i] = v.color;

            if (i < actualVertexCount - 1)
            {
                tris.Add(i);
                tris.Add(i + 1);
            }

            if (Web && i < actualVertexCount - 7)
            {
                tris.Add(i);
                tris.Add(i + 7);
            }
        };

        if (brushType == BrushType.Sphere)
        {
            TubeVertex firstAddVert = vertices[0];
            var startPoint = vertices[0].point;
            firstAddVert.point -= firstAddVert.orientation * Vector3.forward * firstAddVert.extends.x * 0.5f;
            var endPoint = firstAddVert.point;

            Vector2 smallExtends = brushType == BrushType.Sphere ? firstAddVert.extends * 0.001f : firstAddVert.extends;
            var prevExtends = firstAddVert.extends;

            Func<float, float> t = (float v) => v;
            Func<float, float> height = (float v) => Mathf.Sqrt(1.0f - (1.0f - v) * (1.0f - v));

            {
                firstAddVert.extends = firstAddVert.extends * 0.001f;

                var lastVert = firstAddVert;
                float interp = ((float)1.0f) / (0.5f * additionalVertices + 1);


                firstAddVert.extends = Vector2.Lerp(smallExtends, prevExtends, height(t(interp)));
                firstAddVert.point = Vector3.Lerp(endPoint, startPoint, t(interp));
                var thisVert = firstAddVert;

                addPointFunc(0, lastVert, lastVert, thisVert);


                interp = ((float)2.0f) / (0.5f * additionalVertices + 1);
                firstAddVert.extends = Vector2.Lerp(smallExtends, prevExtends, height(t(interp)));
                firstAddVert.point = Vector3.Lerp(endPoint, startPoint, t(interp));

                var nextVert = firstAddVert;

                for (int i = 1; i <= additionalVertices / 2; i++)
                {
                    addPointFunc(i, thisVert, lastVert, nextVert);
                    lastVert = thisVert;
                    thisVert = nextVert;

                    interp = ((float)i + 2) / (0.5f * additionalVertices + 1);
                    nextVert.extends = Vector2.Lerp(smallExtends, prevExtends, height(t(interp)));
                    nextVert.point = Vector3.Lerp(endPoint, startPoint, t(interp));
                }

                firstAddVert = lastVert;
            }

            TubeVertex lastAddVert = vertices[vertices.Count - 1];
            startPoint = lastAddVert.point;
            lastAddVert.point += lastAddVert.orientation * Vector3.forward * lastAddVert.extends.x * 0.5f;
            endPoint = lastAddVert.point;
            prevExtends = lastAddVert.extends;

            smallExtends = brushType == BrushType.Sphere ? lastAddVert.extends * 0.001f : lastAddVert.extends;

            lastAddVert.extends = Vector2.Lerp(prevExtends, smallExtends, 1.0f / (0.5f * additionalVertices));
            lastAddVert.point = Vector3.Lerp(startPoint, endPoint, 1.0f / (0.5f * additionalVertices));

            int firstIndex = additionalVertices / 2;
            for (int i = 1; i < vertices.Count - 1; i++)
            {
                addPointFunc(firstIndex + i, vertices[i], i == 1 ? firstAddVert : vertices[i - 1], i == vertices.Count - 2 ? lastAddVert : vertices[i + 1]);
            }


            {
                float interp = 0.0f / (0.5f * additionalVertices + 1);

                var lastVert = vertices[vertices.Count - 2];
                lastAddVert.extends = Vector2.Lerp(prevExtends, smallExtends, 1.0f - height(1.0f - t(interp)));
                lastAddVert.point = Vector3.Lerp(startPoint, endPoint, t(interp));

                var thisVert = lastAddVert;

                interp = 1.0f / (0.5f * additionalVertices + 1);
                lastAddVert.extends = Vector2.Lerp(prevExtends, smallExtends, 1.0f - height(1.0f - t(interp)));
                lastAddVert.point = Vector3.Lerp(startPoint, endPoint, t(interp));
                var nextVert = lastAddVert;

                for (int i = actualVertexCount - additionalVertices / 2 - 1; i < actualVertexCount - 1; i++)
                {
                    addPointFunc(i, thisVert, lastVert, nextVert);
                    interp = 1.0f - ((float)actualVertexCount - i - 3) / (0.5f * additionalVertices + 1);
                    lastVert = thisVert;
                    thisVert = nextVert;
                    nextVert.extends = Vector2.Lerp(prevExtends, smallExtends, 1.0f - height(1.0f - t(interp)));
                    nextVert.point = Vector3.Lerp(startPoint, endPoint, t(interp));
                }

                lastAddVert.extends = vertices[vertices.Count - 1].extends * 0.001f;
                lastAddVert.point = endPoint;
                addPointFunc(actualVertexCount - 1, lastAddVert, lastVert, lastAddVert);
            }
        }
        else
        {
            var firstPoint = vertices[0];
            firstPoint.point -= firstPoint.orientation * Vector3.forward * firstPoint.extends.x * 0.5f;

            var nextPoint = firstPoint;
            firstPoint.extends *= 0.00001f;

            addPointFunc(0, firstPoint, firstPoint, nextPoint);

            addPointFunc(1, nextPoint, firstPoint, vertices[1]);

            var lastPoint = vertices[vertices.Count - 1];
            lastPoint.point += lastPoint.orientation * Vector3.forward * lastPoint.extends.x * 0.5f;

            var closingPoint = lastPoint;
            closingPoint.extends *= 0.00001f;

            var lastAddedPoint = nextPoint;

            for (int i = 1; i < vertices.Count - 1; i++)
            {
                addPointFunc(i + 1, vertices[i], lastAddedPoint, i == vertices.Count - 2 ? lastPoint : vertices[i + 1]);
                lastAddedPoint = vertices[i];
            }

            addPointFunc(vertices.Count  , lastPoint, lastAddedPoint, closingPoint);
            addPointFunc(vertices.Count+1, closingPoint, lastPoint, closingPoint);

        }


        indices = tris.ToArray();
        length = currentTotalLength;
    }

    List<Vector3> meshVertices = new List<Vector3>();
    List<Vector4> meshTangents = new List<Vector4>();
    List<Vector3> meshNormals = new List<Vector3>();
    List<Vector2> uvs = new List<Vector2>();
    List<Color> colors = new List<Color>();

    public void GenerateMesh(ref Mesh result)
    {
        if (result == null)
        {
            result = new Mesh();
            result.hideFlags = HideFlags.HideAndDontSave;
            result.MarkDynamic();
        }

        if (vertices.Count < 2)
        {
            result.Clear();
            return;
        }

        int additionalVertices = this.brushType == BrushType.Sphere ? 8 : 2;
        int actualVertexCount = vertices.Count + additionalVertices;

        if(actualVertexCount != meshVertices.Count)
        {
            if(actualVertexCount < meshVertices.Count)
            {
                int toRemove = meshVertices.Count - actualVertexCount;
                meshVertices.RemoveRange(actualVertexCount, toRemove);
                meshTangents.RemoveRange(actualVertexCount, toRemove);
                meshNormals.RemoveRange(actualVertexCount, toRemove);
                uvs.RemoveRange(actualVertexCount, toRemove);
                colors.RemoveRange(actualVertexCount, toRemove);
            }
            else
            {
                int toAdd = actualVertexCount- meshVertices.Count;
                meshVertices.AddRange(Enumerable.Repeat(default(Vector3), toAdd));
                meshTangents.AddRange(Enumerable.Repeat(default(Vector4), toAdd));
                meshNormals.AddRange(Enumerable.Repeat(default(Vector3), toAdd));
                uvs.AddRange(Enumerable.Repeat(default(Vector2), toAdd));
                colors.AddRange(Enumerable.Repeat(default(Color), toAdd));
            }
        }

        int[] indices = null;

        TubeRenderer.GenerateMesh(vertices, bufferDataIndex, Flat, Web, brushType, ExtendsMultiplier, 
            meshVertices, meshTangents, meshNormals, uvs, colors, ref indices, ref currentTotalLength);

        result.Clear();
        result.SetVertices(meshVertices);
        result.SetTangents(meshTangents);
        result.SetNormals(meshNormals);
        result.SetUVs(0,uvs);
        result.SetColors(colors);
        result.SetIndices(indices, MeshTopology.Lines, 0, true);
        result.RecalculateBounds();
        result.UploadMeshData(false);
    }

    public void RebuildMesh()
    {
        if (bufferDataIndex == -1) return;
        GenerateMesh(ref mesh);
        meshFilter.sharedMesh = mesh;
        UpdateSettings();
    }

    static Quaternion fitRotation(Vector3 forward, Quaternion rot)
    {
        Vector3 up = rot * Vector3.up;

        return Quaternion.LookRotation(forward, up);
    }

    public void UpdatePoint(int index, Vector3 pos, Quaternion rot, float thickness, Color col, float light)
    {
        if(index < 0 || index > vertices.Count - 1) return;

        Quaternion o;
        Vector3 forward = pos - vertices[index - 1 < 0 ? index : index - 1].point;
        if (vertices.Count == 0 ||forward.magnitude < 0.001)
        {
            o = vertices[index - 1 < 0 ? index : index - 1].orientation;
        } else {
            o = fitRotation(forward, rot);
        }
        var vert = vertices[index];
        vert.point = pos;
        vert.extends.x = thickness;
        vert.extends.y = thickness;
        vert.color = ApplyLight(col, light);
        vert.orientation = o;
        vertices[index] = vert;
        UpdateOrientation(index - 1);
    }

    public void UpdatePoint(int index, Color col, float light)
    {
        var vert = vertices[index];
        vert.color = ApplyLight(col, light);
        vertices[index] = vert;
    }
    
    public void UpdatePoint(int index, float width)
    {
        var vert = vertices[index];
        vert.extends = Vector2.one * width;
        vertices[index] = vert;
    }

    public static Color ApplyLight(Color val, float light)
    {
        Color result;
        if(light < 0.5f)
        {
            Color currentHsv = new Color();
            Color.RGBToHSV(val, out currentHsv.r, out currentHsv.g, out currentHsv.b);

            currentHsv.g = Mathf.Lerp(currentHsv.g, currentHsv.g * 0.7f, 1.0f - light * 2);
            currentHsv.b = Mathf.Lerp(currentHsv.b, 0, 1.0f - light * 2);
            result = Color.HSVToRGB(currentHsv.r, currentHsv.g, currentHsv.b);
        }
        else
        {
            Color currentHsv = new Color();
            Color.RGBToHSV(val, out currentHsv.r, out currentHsv.g, out currentHsv.b);

            currentHsv.g = Mathf.Lerp(currentHsv.g, currentHsv.g * 0.7f, light * 2 - 1.0f);
            currentHsv.b = Mathf.Lerp(currentHsv.b, 1, light * 2 - 1.0f);
            result = Color.HSVToRGB(currentHsv.r, currentHsv.g, currentHsv.b);
        }
        result.a = val.a;
        return result;
    }

    public void UpdatePoint(int index, Vector3 pos)
    {
        var vert = vertices[index];
        vert.point = pos;
        vertices[index] = vert;
        UpdatePoint(index);
        UpdateOrientation(index);
        //UpdatePoint(index-1);
        //UpdatePoint(index);
        //UpdatePoint(index+1);
    }

    public void UpdatePoint(int index)
    {
        if(index < 0 || index > vertices.Count - 1) return;

        UpdatePoint(index, vertices[index].point, vertices[index].orientation, vertices[index].extends.x, vertices[index].color, 0.5f);
    }

    public void AddPoint(Vector3 pos, Quaternion rot, float thickness, Color col, float light)
    {
        Quaternion o;
        if (vertices.Count == 0 || (pos - vertices.Last().point).magnitude < 0.00001)
        {
            o = vertices.Count == 0 ? rot : vertices[vertices.Count - 1].orientation;
        } else {
            o = fitRotation(pos - vertices.Last().point, rot);
        }
        vertices.Add(new TubeVertex(pos, new Vector2(thickness, thickness), ApplyLight(col, light), o));
        UpdateOrientation(vertices.Count - 2);
    }

    public static void AddPoint(Vector3 pos, Quaternion rot, float thickness, Color col, float light, List<TubeVertex> vertices)
    {
        if (float.IsNaN(thickness)) thickness = 0;

        Quaternion o;
        if (vertices.Count == 0 || (pos - vertices.Last().point).magnitude < 0.00001)
        {
            o = vertices.Count == 0 ? rot : vertices[vertices.Count - 1].orientation;
        }
        else
        {
            o = fitRotation(pos - vertices.Last().point, rot);
        }
        vertices.Add(new TubeVertex(pos, new Vector2(thickness, thickness), ApplyLight(col, light), o));
        UpdateOrientation(vertices.Count - 2, vertices);
    }

    private void UpdateOrientation(int index)
    {
        if (index < 0 || index >= vertices.Count || vertices.Count == 1) return;
        var nextRot = index < vertices.Count - 1 ? vertices[index + 1].orientation : vertices[index-1].orientation;
        var preRot = index > 0 ? vertices[index - 1].orientation : vertices[index+1].orientation;

        var vert = vertices[index];
        vert.orientation = Quaternion.Slerp(preRot, nextRot, 0.5f);
        vertices[index] = vert;
    }

    private static void UpdateOrientation(int index, List<TubeVertex> vertices)
    {
        if (index < 0 || index >= vertices.Count || vertices.Count == 1) return;
        var nextRot = index < vertices.Count - 1 ? vertices[index + 1].orientation : vertices[index - 1].orientation;
        var preRot = index > 0 ? vertices[index - 1].orientation : vertices[index + 1].orientation;

        var vert = vertices[index];
        vert.orientation = Quaternion.Slerp(preRot, nextRot, 0.5f);
        vertices[index] = vert;
    }
}
#endif