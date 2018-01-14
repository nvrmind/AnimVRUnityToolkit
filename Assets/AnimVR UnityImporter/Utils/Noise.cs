using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class Noise {

    static float floor(float f)
    {
        return Mathf.Floor(f);
    }

    static Vector2 floor(Vector2 f)
    {
        return new Vector2(Mathf.Floor(f.x), Mathf.Floor(f.y));
    }

    static Vector3 floor(Vector3 f)
    {
        return new Vector3(Mathf.Floor(f.x), Mathf.Floor(f.y), Mathf.Floor(f.z));
    }

    static Vector4 floor(Vector4 f)
    {
        return new Vector4(Mathf.Floor(f.x), Mathf.Floor(f.y), Mathf.Floor(f.z), Mathf.Floor(f.w));
    }

    //
    // Description : Array and textureless GLSL 2D simplex noise function.
    //      Author : Ian McEwan, Ashima Arts.
    //  Maintainer : ijm
    //     Lastmod : 20110822 (ijm)
    //     License : Copyright (C) 2011 Ashima Arts. All rights reserved.
    //               Distributed under the MIT License. See LICENSE file.
    //               https://github.com/ashima/webgl-noise
    // 
    public static Vector4 mod289(Vector4 x)
    {
        return x - floor(x * (1.0f / 289.0f)) * 289.0f;
    }
    public static Vector3 mod289(Vector3 x)
    {
        return x - floor(x * (1.0f / 289.0f)) * 289.0f;
    }

    public static Vector2 mod289(Vector2 x)
    {
        return x - floor(x * (1.0f / 289.0f)) * 289.0f;
    }

    public static float mod289(float x)
    {
        return x - floor(x * (1.0f / 289.0f)) * 289.0f;
    }

    public static float permute(float x)
    {
        return mod289(((x * 34.0f) + 1.0f) * x);
    }

    /*public static Vector3 permute(Vector3 x)
    {
        return mod289(((x * 34.0f) + Vector3.one) * x);
    }

    public static Vector4 permute(Vector4 x)
    {
        return mod289(((x * 34.0f) + Vector4.one) * x);
    }

    public static Vector4 taylorInvSqrt(Vector4 r)
    {
        float val = 1.79284291400159f;
        return new Vector4(val, val, val, val) - 0.85373472095314f * r;
    }

    public static float taylorInvSqrt(float r)
    {
        return 1.79284291400159f - 0.85373472095314f * r;
    }

    public static Vector4 grad4(float j, Vector4 ip)
    {
        Vector4 ones = new Vector4(1.0f, 1.0f, 1.0f, -1.0f);
        Vector4 p, s;

        p.xyz = floor(frac(new Vector3(j, j, j) * ip.xyz) * 7.0) * ip.z - 1.0;
        p.w = 1.5 - dot(abs(p.xyz), ones.xyz);
        //s = p;//Vector4(lessThan(p, Vector4(0.0)));
        if (p.x < 0)
            s.x = 1;
        else
            s.x = 0;
        if (p.y < 0)
            s.y = 1;
        else
            s.y = 0;
        if (p.z < 0)
            s.z = 1;
        else
            s.z = 0;
        if (p.w < 0)
            s.w = 1;
        else
            s.w = 0;
        p.xyz = p.xyz + (s.xyz * 2.0 - 1.0) * s.www;

        return p;
    }

    public static float snoise(Vector2 v)
    {
        Vector4 C = new Vector4(0.211324865405187f,  // (3.0-sqrt(3.0))/6.0
                            0.366025403784439f,  // 0.5*(sqrt(3.0)-1.0)
                           -0.577350269189626f,  // -1.0 + 2.0 * C.x
                            0.024390243902439f); // 1.0 / 41.0
                                                // First corner
        Vector2 i = floor(v + dot(v, C.yy));
        Vector2 x0 = v - i + dot(i, C.xx);

        // Other corners
        Vector2 i1;
        //i1.x = step( x0.y, x0.x ); // x0.x > x0.y ? 1.0 : 0.0
        //i1.y = 1.0 - i1.x;
        i1 = (x0.x > x0.y) ? new Vector2(1.0f, 0.0f) : new Vector2(0.0f, 1.0f);
        // x0 = x0 - 0.0 + 0.0 * C.xx ;
        // x1 = x0 - i1 + 1.0 * C.xx ;
        // x2 = x0 - 1.0 + 2.0 * C.xx ;
        Vector4 x12 = x0.xyxy + C.xxzz;
        x12.xy -= i1;

        // Permutations
        i = mod289(i); // Avoid truncation effects in permutation
        Vector3 p = permute(permute(i.y + Vector3(0.0, i1.y, 1.0))
              + i.x + Vector3(0.0, i1.x, 1.0));

        Vector3 m = max(0.5 - Vector3(dot(x0, x0), dot(x12.xy, x12.xy), dot(x12.zw, x12.zw)), 0.0);
        m = m * m;
        m = m * m;

        // Gradients: 41 points uniformly over a line, mapped onto a diamond.
        // The ring size 17*17 = 289 is close to a multiple of 41 (41*7 = 287)

        Vector3 x = 2.0 * frac(p * C.www) - 1.0;
        Vector3 h = abs(x) - 0.5;
        Vector3 ox = floor(x + 0.5);
        Vector3 a0 = x - ox;

        // Normalise gradients implicitly by scaling m
        // Approximation of: m *= inversesqrt( a0*a0 + h*h );
        m *= 1.79284291400159 - 0.85373472095314 * (a0 * a0 + h * h);

        // Compute final noise value at P
        Vector3 g;
        g.x = a0.x * x0.x + h.x * x0.y;
        g.yz = a0.yz * x12.xz + h.yz * x12.yw;
        return 130.0 * dot(m, g);
    }*/

    internal static float Generate(float x, float y)
    {
        const float F2 = 0.366025403f; // F2 = 0.5*(sqrt(3.0)-1.0)
        const float G2 = 0.211324865f; // G2 = (3.0-Math.sqrt(3.0))/6.0

        float n0, n1, n2; // Noise contributions from the three corners

        // Skew the input space to determine which simplex cell we're in
        float s = (x + y) * F2; // Hairy factor for 2D
        float xs = x + s;
        float ys = y + s;
        int i = FastFloor(xs);
        int j = FastFloor(ys);

        float t = (float)(i + j) * G2;
        float X0 = i - t; // Unskew the cell origin back to (x,y) space
        float Y0 = j - t;
        float x0 = x - X0; // The x,y distances from the cell origin
        float y0 = y - Y0;

        // For the 2D case, the simplex shape is an equilateral triangle.
        // Determine which simplex we are in.
        int i1, j1; // Offsets for second (middle) corner of simplex in (i,j) coords
        if (x0 > y0) { i1 = 1; j1 = 0; } // lower triangle, XY order: (0,0)->(1,0)->(1,1)
        else { i1 = 0; j1 = 1; }      // upper triangle, YX order: (0,0)->(0,1)->(1,1)

        // A step of (1,0) in (i,j) means a step of (1-c,-c) in (x,y), and
        // a step of (0,1) in (i,j) means a step of (-c,1-c) in (x,y), where
        // c = (3-sqrt(3))/6

        float x1 = x0 - i1 + G2; // Offsets for middle corner in (x,y) unskewed coords
        float y1 = y0 - j1 + G2;
        float x2 = x0 - 1.0f + 2.0f * G2; // Offsets for last corner in (x,y) unskewed coords
        float y2 = y0 - 1.0f + 2.0f * G2;

        // Wrap the integer indices at 256, to avoid indexing perm[] out of bounds
        int ii = i % 256;
        int jj = j % 256;

        // Calculate the contribution from the three corners
        float t0 = 0.5f - x0 * x0 - y0 * y0;
        if (t0 < 0.0f) n0 = 0.0f;
        else
        {
            t0 *= t0;
            n0 = t0 * t0 * grad(perm[ii + perm[jj]], x0, y0);
        }

        float t1 = 0.5f - x1 * x1 - y1 * y1;
        if (t1 < 0.0f) n1 = 0.0f;
        else
        {
            t1 *= t1;
            n1 = t1 * t1 * grad(perm[ii + i1 + perm[jj + j1]], x1, y1);
        }

        float t2 = 0.5f - x2 * x2 - y2 * y2;
        if (t2 < 0.0f) n2 = 0.0f;
        else
        {
            t2 *= t2;
            n2 = t2 * t2 * grad(perm[ii + 1 + perm[jj + 1]], x2, y2);
        }

        // Add contributions from each corner to get the final noise value.
        // The result is scaled to return values in the interval [-1,1].
        return 40.0f * (n0 + n1 + n2); // TODO: The scale factor is preliminary!
    }

    private static int FastFloor(float x)
    {
        return (x > 0) ? ((int)x) : (((int)x) - 1);
    }

    private static readonly byte[] perm = new byte[]
{
            151,160,137,91,90,15,
            131,13,201,95,96,53,194,233,7,225,140,36,103,30,69,142,8,99,37,240,21,10,23,
            190, 6,148,247,120,234,75,0,26,197,62,94,252,219,203,117,35,11,32,57,177,33,
            88,237,149,56,87,174,20,125,136,171,168, 68,175,74,165,71,134,139,48,27,166,
            77,146,158,231,83,111,229,122,60,211,133,230,220,105,92,41,55,46,245,40,244,
            102,143,54, 65,25,63,161, 1,216,80,73,209,76,132,187,208, 89,18,169,200,196,
            135,130,116,188,159,86,164,100,109,198,173,186, 3,64,52,217,226,250,124,123,
            5,202,38,147,118,126,255,82,85,212,207,206,59,227,47,16,58,17,182,189,28,42,
            223,183,170,213,119,248,152, 2,44,154,163, 70,221,153,101,155,167, 43,172,9,
            129,22,39,253, 19,98,108,110,79,113,224,232,178,185, 112,104,218,246,97,228,
            251,34,242,193,238,210,144,12,191,179,162,241, 81,51,145,235,249,14,239,107,
            49,192,214, 31,181,199,106,157,184, 84,204,176,115,121,50,45,127, 4,150,254,
            138,236,205,93,222,114,67,29,24,72,243,141,128,195,78,66,215,61,156,180,
            151,160,137,91,90,15,
            131,13,201,95,96,53,194,233,7,225,140,36,103,30,69,142,8,99,37,240,21,10,23,
            190, 6,148,247,120,234,75,0,26,197,62,94,252,219,203,117,35,11,32,57,177,33,
            88,237,149,56,87,174,20,125,136,171,168, 68,175,74,165,71,134,139,48,27,166,
            77,146,158,231,83,111,229,122,60,211,133,230,220,105,92,41,55,46,245,40,244,
            102,143,54, 65,25,63,161, 1,216,80,73,209,76,132,187,208, 89,18,169,200,196,
            135,130,116,188,159,86,164,100,109,198,173,186, 3,64,52,217,226,250,124,123,
            5,202,38,147,118,126,255,82,85,212,207,206,59,227,47,16,58,17,182,189,28,42,
            223,183,170,213,119,248,152, 2,44,154,163, 70,221,153,101,155,167, 43,172,9,
            129,22,39,253, 19,98,108,110,79,113,224,232,178,185, 112,104,218,246,97,228,
            251,34,242,193,238,210,144,12,191,179,162,241, 81,51,145,235,249,14,239,107,
            49,192,214, 31,181,199,106,157,184, 84,204,176,115,121,50,45,127, 4,150,254,
            138,236,205,93,222,114,67,29,24,72,243,141,128,195,78,66,215,61,156,180
};

    private static float grad(int hash, float x)
    {
        int h = hash & 15;
        float grad = 1.0f + (h & 7);   // Gradient value 1.0, 2.0, ..., 8.0
        if ((h & 8) != 0) grad = -grad;         // Set a random sign for the gradient
        return (grad * x);           // Multiply the gradient with the distance
    }

    private static float grad(int hash, float x, float y)
    {
        int h = hash & 7;      // Convert low 3 bits of hash code
        float u = h < 4 ? x : y;  // into 8 simple gradient directions,
        float v = h < 4 ? y : x;  // and compute the dot product with (x,y).
        return ((h & 1) != 0 ? -u : u) + ((h & 2) != 0 ? -2.0f * v : 2.0f * v);
    }

    private static float grad(int hash, float x, float y, float z)
    {
        int h = hash & 15;     // Convert low 4 bits of hash code into 12 simple
        float u = h < 8 ? x : y; // gradient directions, and compute dot product.
        float v = h < 4 ? y : h == 12 || h == 14 ? x : z; // Fix repeats at h = 12 to 15
        return ((h & 1) != 0 ? -u : u) + ((h & 2) != 0 ? -v : v);
    }

    private static float grad(int hash, float x, float y, float z, float t)
    {
        int h = hash & 31;      // Convert low 5 bits of hash code into 32 simple
        float u = h < 24 ? x : y; // gradient directions, and compute dot product.
        float v = h < 16 ? y : z;
        float w = h < 8 ? z : t;
        return ((h & 1) != 0 ? -u : u) + ((h & 2) != 0 ? -v : v) + ((h & 4) != 0 ? -w : w);
    }
}
