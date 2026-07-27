using System;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Headless ABI oracles for the two PURE-MATH mesh fns (GetMeshBoundingBox, GetRayCollisionMesh) — fast subset, no GL.
/// We hand-build a Mesh in unmanaged memory (a raw vertex array + counts) and pass it BY VALUE, which exercises the whole
/// Mesh struct layout (vertexCount@0, triangleCount@4, vertices pointer@8) plus the BoundingBox/RayCollision by-value
/// returns and the Matrix-transform path. A wrong struct offset or size would read garbage and fail these deterministic
/// oracles (or access-violate), so this is real correctness, not an ABI smoke test. The GPU-bound generators/upload/export
/// live in RaylibModelsMeshGpuTests (Integration). Self-skips via Assert.Ignore when the DLL predates the mesh exports.
/// </summary>
[TestFixture]
public class RaylibModelsMeshTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;

    [StructLayout(LayoutKind.Sequential)] private struct Vector3 { public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; } }
    [StructLayout(LayoutKind.Sequential)] private struct BoundingBox { public Vector3 min, max; }
    [StructLayout(LayoutKind.Sequential)] private struct RayCollision { [MarshalAs(UnmanagedType.I1)] public bool hit; public float distance; public Vector3 point, normal; }
    [StructLayout(LayoutKind.Sequential)] private struct Ray { public Vector3 position, direction; public Ray(Vector3 p, Vector3 d) { position = p; direction = d; } }

    // raylib Matrix — 16 floats in raylib's scrambled declaration order (m0,m4,m8,m12,m1,...), matching Utiliy.vb.
    [StructLayout(LayoutKind.Sequential)]
    private struct Matrix
    {
        public float m0, m4, m8, m12, m1, m5, m9, m13, m2, m6, m10, m14, m3, m7, m11, m15;
        public static Matrix Identity()
        {
            return new Matrix { m0 = 1f, m5 = 1f, m10 = 1f, m15 = 1f };
        }
        public static Matrix TranslateZ(float z)
        {
            // Column-major translation lives in m12/m13/m14; here only the z component (m14) is non-zero.
            var t = Identity();
            t.m14 = z;
            return t;
        }
    }

    // Mirror of Utiliy.vb Mesh — 17 blittable fields (ints + native pointers as IntPtr). SizeOf must equal 120 on x64.
    [StructLayout(LayoutKind.Sequential)]
    private struct Mesh
    {
        public int vertexCount, triangleCount;
        public IntPtr vertices, texcoords, texcoords2, normals, tangents, colors, indices;
        public IntPtr animVertices, animNormals, boneIds, boneWeights, boneMatrices;
        public int boneCount;
        public uint vaoId;
        public IntPtr vboId;
    }

    [DllImport(DLL, CallingConvention = CC)] private static extern BoundingBox Framework_GetMeshBoundingBox(Mesh mesh);
    [DllImport(DLL, CallingConvention = CC)] private static extern RayCollision Framework_GetRayCollisionMesh(Ray ray, Mesh mesh, Matrix transform);

    // Allocate an unmanaged float[] and return the pointer (caller frees). vertices are XYZ triples.
    private static IntPtr AllocVerts(float[] xyz)
    {
        IntPtr p = Marshal.AllocHGlobal(xyz.Length * sizeof(float));
        Marshal.Copy(xyz, 0, p, xyz.Length);
        return p;
    }

    [Test]
    public void Mesh_struct_is_120_bytes_on_x64()
    {
        // Pins the field layout: a missing/extra field or a wrong-width field would change the size and desync the ABI.
        Assert.That(Marshal.SizeOf<Mesh>(), Is.EqualTo(120), "Mesh must be 120 bytes (2 int + 12 ptr + int + uint + ptr, 8-aligned)");
    }

    [Test]
    public void GetMeshBoundingBox_computes_min_max_over_hand_built_vertices()
    {
        // Three vertices spanning a known AABB: min=(-2,-1,0), max=(2,3,5).
        IntPtr verts = AllocVerts(new[] { -2f, -1f, 0f,  2f, 3f, 0f,  0f, 0f, 5f });
        try
        {
            var mesh = new Mesh { vertexCount = 3, vertices = verts };

            BoundingBox box;
            try { box = Framework_GetMeshBoundingBox(mesh); }
            catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); return; }
            catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates the mesh exports; refresh IDE\\ first."); return; }

            Assert.Multiple(() =>
            {
                Assert.That(box.min.x, Is.EqualTo(-2f).Within(1e-4f));
                Assert.That(box.min.y, Is.EqualTo(-1f).Within(1e-4f));
                Assert.That(box.min.z, Is.EqualTo(0f).Within(1e-4f));
                Assert.That(box.max.x, Is.EqualTo(2f).Within(1e-4f));
                Assert.That(box.max.y, Is.EqualTo(3f).Within(1e-4f));
                Assert.That(box.max.z, Is.EqualTo(5f).Within(1e-4f));
            });
        }
        finally { Marshal.FreeHGlobal(verts); }
    }

    [Test]
    public void GetRayCollisionMesh_hits_a_hand_built_triangle_and_honors_the_transform()
    {
        // One CCW triangle in the z=0 plane straddling the origin; non-indexed (triangleCount=1, indices=NULL).
        IntPtr verts = AllocVerts(new[] { -1f, -1f, 0f,  1f, -1f, 0f,  0f, 1f, 0f });
        try
        {
            var mesh = new Mesh { vertexCount = 3, triangleCount = 1, vertices = verts };
            var ray = new Ray(new Vector3(0f, 0f, -5f), new Vector3(0f, 0f, 1f));  // +z toward the triangle

            RayCollision idHit;
            try { idHit = Framework_GetRayCollisionMesh(ray, mesh, Matrix.Identity()); }
            catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); return; }
            catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates the mesh exports; refresh IDE\\ first."); return; }

            // Identity transform: hit the triangle at z=0, distance 5, at the origin.
            Assert.Multiple(() =>
            {
                Assert.That(idHit.hit, Is.True, "ray should hit the triangle under identity transform");
                Assert.That(idHit.distance, Is.EqualTo(5f).Within(1e-3f), "distance origin->plane is 5");
                Assert.That(idHit.point.x, Is.EqualTo(0f).Within(1e-3f));
                Assert.That(idHit.point.y, Is.EqualTo(0f).Within(1e-3f));
                Assert.That(idHit.point.z, Is.EqualTo(0f).Within(1e-3f));
                Assert.That(Math.Abs(idHit.normal.z), Is.EqualTo(1f).Within(1e-3f), "surface normal is axis-z");
            });

            // Translate the mesh +2 in z: the triangle moves to z=2, so the SAME ray now hits at distance 7. This proves the
            // Matrix is actually marshaled (in raylib's scrambled field order) and applied — an ignored/scrambled matrix
            // would still report distance 5.
            var movedHit = Framework_GetRayCollisionMesh(ray, mesh, Matrix.TranslateZ(2f));
            Assert.Multiple(() =>
            {
                Assert.That(movedHit.hit, Is.True, "ray should still hit after a +z translation");
                Assert.That(movedHit.distance, Is.EqualTo(7f).Within(1e-3f), "translated triangle sits 2 further along +z");
                Assert.That(movedHit.point.z, Is.EqualTo(2f).Within(1e-3f), "hit point moves to z=2");
            });

            // Miss: an x-offset ray passes outside the triangle -> no hit.
            var missRay = new Ray(new Vector3(5f, 0f, -5f), new Vector3(0f, 0f, 1f));
            var miss = Framework_GetRayCollisionMesh(missRay, mesh, Matrix.Identity());
            Assert.That(miss.hit, Is.False, "an x=5 ray misses a triangle spanning x in [-1,1]");
        }
        finally { Marshal.FreeHGlobal(verts); }
    }
}
