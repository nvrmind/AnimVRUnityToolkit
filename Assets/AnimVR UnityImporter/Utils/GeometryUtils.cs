using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class GeometryUtils {
    public static Vector3 Barycentric(Vector3 p, Vector3 a, Vector3 b, Vector3 c) {
        Vector3 v0 = b - a, v1 = c - a, v2 = p - a;
        float d00 = Vector3.Dot(v0, v0);
        float d01 = Vector3.Dot(v0, v1);
        float d11 = Vector3.Dot(v1, v1);
        float d20 = Vector3.Dot(v2, v0);
        float d21 = Vector3.Dot(v2, v1);
        float denom = d00 * d11 - d01 * d01;
        Vector3 uvw;
        uvw.y = (d11 * d20 - d01 * d21) / denom;
        uvw.z = (d00 * d21 - d01 * d20) / denom;

        uvw.y = Mathf.Clamp01(uvw.y);
        uvw.z = Mathf.Clamp01(uvw.z);
        uvw.x = 1.0f - uvw.y - uvw.z;

        uvw.y = Mathf.Clamp01(uvw.y);
        uvw.x = Mathf.Clamp01(uvw.x);
        uvw.z = 1.0f - uvw.y - uvw.x;

        uvw.z = Mathf.Clamp01(uvw.z);
        uvw.x = Mathf.Clamp01(uvw.x);
        uvw.y = 1.0f - uvw.z - uvw.x;

        return uvw;
    }

    public static Vector3 ProjectPointOnTriangle(Vector3 point, Vector3 a, Vector3 b, Vector3 c) {
        point = a + Vector3.ProjectOnPlane(point - a, Vector3.Cross(b - a, c - a));
        Vector3 barycentric = Barycentric(point, a, b, c);
        return a * barycentric.x + b * barycentric.y + c * barycentric.z;
    }

    public static float DistancePointLine(Vector3 point, Vector3 lineStart, Vector3 lineEnd) {
        return Vector3.Magnitude(ProjectPointLine(point, lineStart, lineEnd) - point);
    }
    public static Vector3 ProjectPointLine(Vector3 point, Vector3 lineStart, Vector3 lineEnd) {
        Vector3 rhs = point - lineStart;
        Vector3 vector2 = lineEnd - lineStart;
        float magnitude = vector2.magnitude;
        Vector3 lhs = vector2;
        if (magnitude > 1E-06f) {
            lhs = (Vector3)(lhs / magnitude);
        }
        float num2 = Mathf.Clamp(Vector3.Dot(lhs, rhs), 0f, magnitude);
        return (lineStart + ((Vector3)(lhs * num2)));
    }

    public static Vector3 ProjectPointOnCircle(Vector3 point, Vector3 center, Vector3 up, float radius) {
        var rel = point - center;
        rel = Vector3.ProjectOnPlane(rel, up);

        return center + rel.normalized * radius;
    }

    public static IEnumerable<Vector3> PointsOnSphere(int u, int v, float radius) {
        for (int i = 0; i < u; i++) {
            float phi = Mathf.Asin(2 * (i + 0.5f) / u - 1.0f) * Mathf.PI;
            float sin_phi = Mathf.Sin(phi);
            float cos_phi = Mathf.Cos(phi);
            for (int j = 0; j < v; j++) {
                float theta = (j + 0.5f) / v * Mathf.PI * 2; // Mathf.Asin(2 * (j + 0.5f) / v - 1.0f) * Mathf.PI;
                float sin_theta = Mathf.Sin(theta);
                float cos_theta = Mathf.Cos(theta);

                yield return new Vector3(sin_phi * cos_theta, sin_phi * sin_theta, cos_phi) * radius;
            }
        }
    }

    public static IEnumerable<Vector3> PointsOnSphere(int samples, float radius) {
        float offset = 2.0f / samples;
        float increment = Mathf.PI * (3.0f - Mathf.Sqrt(5.0f));

        for (int i = 0; i < samples; i++) {
            float y = ((i * offset) - 1) + (offset / 2);
            float r = Mathf.Sqrt(1 - y * y);

            float phi = (i % samples) * increment;

            float x = Mathf.Cos(phi) * r;
            float z = Mathf.Sin(phi) * r;

            yield return new Vector3(x, y, z) * radius;
        }
    }

    public static bool PointInCollider(this Collider col, Vector3 pos) {

        if (!col.bounds.Contains(pos)) return false;

        Vector3 far = col.bounds.center + col.bounds.extents * 4;

        Ray r = new Ray(pos, far - pos);
        RaycastHit hitInfo;

        float distToFar = Vector3.Distance(r.origin, far);

        int iterations = 0;
        while(distToFar > 0.00001f && iterations < 101) {
            if (!col.Raycast(r, out hitInfo, distToFar))
                break;

            if (hitInfo.distance < 0.00001f)
                break;

            iterations++;
            r.origin = hitInfo.point + r.direction * 0.000001f;
            distToFar -= hitInfo.distance;
        }

        if (iterations == 101)
            Debug.LogWarning("Weird thing in PointInCollider");

        return iterations % 2 == 0;
    }

    private static float _copysign(float sizeval, float signval) {
        return Mathf.Sign(signval) == 1 ? Mathf.Abs(sizeval) : -Mathf.Abs(sizeval);
    }

    public static Quaternion AnimGetRotation(this Matrix4x4 matrix) {
        Quaternion q = new Quaternion();
        q.w = Mathf.Sqrt(Mathf.Max(0, 1 + matrix.m00 + matrix.m11 + matrix.m22)) / 2;
        q.x = Mathf.Sqrt(Mathf.Max(0, 1 + matrix.m00 - matrix.m11 - matrix.m22)) / 2;
        q.y = Mathf.Sqrt(Mathf.Max(0, 1 - matrix.m00 + matrix.m11 - matrix.m22)) / 2;
        q.z = Mathf.Sqrt(Mathf.Max(0, 1 - matrix.m00 - matrix.m11 + matrix.m22)) / 2;
        q.x = _copysign(q.x, matrix.m21 - matrix.m12);
        q.y = _copysign(q.y, matrix.m02 - matrix.m20);
        q.z = _copysign(q.z, matrix.m10 - matrix.m01);
        return q;
    }

    public static Vector3 AnimGetPosition(this Matrix4x4 matrix) {
        var x = matrix.m03;
        var y = matrix.m13;
        var z = matrix.m23;

        return new Vector3(x, y, z);
    }

    public static Vector3 AnimGetScale(this Matrix4x4 m) {
        var x = Mathf.Sqrt(m.m00 * m.m00 + m.m01 * m.m01 + m.m02 * m.m02);
        var y = Mathf.Sqrt(m.m10 * m.m10 + m.m11 * m.m11 + m.m12 * m.m12);
        var z = Mathf.Sqrt(m.m20 * m.m20 + m.m21 * m.m21 + m.m22 * m.m22);

        return new Vector3(x, y, z);
    }
}
