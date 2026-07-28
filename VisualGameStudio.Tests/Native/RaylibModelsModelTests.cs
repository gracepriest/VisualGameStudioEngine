using System;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Headless ABI oracles for the model fns that can run without GL — fast subset, no window. GetModelBoundingBox only walks
/// model.meshes on the CPU. IsModelValid has NO null-guard on its meshCount-bounded meshes[i] deref loop (raylib 5.5), so only
/// a zeroed Model (meshCount 0, the loop never runs) is headless-safe — a null/garbage meshes pointer with count > 0
/// access-violates; the positive (true) path derefs each mesh and is GPU-only. We hand-build a Model (and, for the bbox, a
/// Mesh it points at) in unmanaged memory and pass it BY VALUE. Because meshCount lives at offset 64 and meshes at 72 — AFTER the
/// embedded 64-byte Matrix transform — these oracles transitively pin the Matrix size and every downstream offset: a wrong
/// Model layout reads garbage counts/pointers and fails deterministically (or access-violates). The GPU-bound load/draw fns
/// live in RaylibModelsModelGpuTests (Integration). Self-skips via Assert.Ignore when the DLL is absent or predates the exports.
/// </summary>
[TestFixture]
public class RaylibModelsModelTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;

    [StructLayout(LayoutKind.Sequential)] private struct Vector3 { public float x, y, z; }
    [StructLayout(LayoutKind.Sequential)] private struct BoundingBox { public Vector3 min, max; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Matrix
    {
        public float m0, m4, m8, m12, m1, m5, m9, m13, m2, m6, m10, m14, m3, m7, m11, m15;
        public static Matrix Identity() => new Matrix { m0 = 1f, m5 = 1f, m10 = 1f, m15 = 1f };
    }

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

    [StructLayout(LayoutKind.Sequential)]
    private struct Model
    {
        public Matrix transform;
        public int meshCount, materialCount;
        public IntPtr meshes, materials, meshMaterial;
        public int boneCount;
        public IntPtr bones, bindPose;
    }

    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsModelValid(Model model);
    [DllImport(DLL, CallingConvention = CC)] private static extern BoundingBox Framework_GetModelBoundingBox(Model model);

    [Test]
    public void Model_struct_is_120_bytes_on_x64()
    {
        Assert.That(Marshal.SizeOf<Model>(), Is.EqualTo(120), "Model must be 120 bytes (Matrix 64 + 2 int + 3 ptr + int + 2 ptr, 8-aligned)");
    }

    [Test]
    public void IsModelValid_rejects_a_zeroed_model()
    {
        // raylib 5.5's IsModelValid iterates `for (i = 0; i < meshCount; i++) IsMeshValid(meshes[i])` WITHOUT a guarding
        // null/count short-circuit, so ANY Model with meshCount > 0 derefs meshes[i] — a null or dummy pointer access-violates.
        // The only guaranteed-safe headless case is a zeroed Model (meshCount == 0 -> the deref loop never runs -> returns
        // false). It still exercises Model-by-value marshaling + the I1 return + the post-Matrix field reads. The positive
        // (true) path needs a real GPU-loaded model and is covered in RaylibModelsModelGpuTests.
        bool valid;
        try { valid = Framework_IsModelValid(new Model()); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); return; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates the model exports; refresh IDE\\ first."); return; }
        Assert.That(valid, Is.False, "a zeroed Model (meshCount 0) is invalid");
    }

    [Test]
    public void GetModelBoundingBox_walks_a_hand_built_models_single_mesh()
    {
        // Build a Mesh (3 vertices, known AABB) in unmanaged memory, point a Model's meshes[0] at it, pass the Model by value.
        // min=(-3,-1,-2), max=(2,4,3).
        IntPtr verts = Marshal.AllocHGlobal(9 * sizeof(float));
        Marshal.Copy(new[] { -3f, -1f, -2f,  2f, 4f, 1f,  0f, 0f, 3f }, 0, verts, 9);
        IntPtr meshPtr = Marshal.AllocHGlobal(Marshal.SizeOf<Mesh>());
        try
        {
            Marshal.StructureToPtr(new Mesh { vertexCount = 3, vertices = verts }, meshPtr, false);
            var model = new Model { transform = Matrix.Identity(), meshCount = 1, meshes = meshPtr };

            BoundingBox box;
            try { box = Framework_GetModelBoundingBox(model); }
            catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); return; }
            catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates the model exports; refresh IDE\\ first."); return; }

            Assert.Multiple(() =>
            {
                Assert.That(box.min.x, Is.EqualTo(-3f).Within(1e-4f));
                Assert.That(box.min.y, Is.EqualTo(-1f).Within(1e-4f));
                Assert.That(box.min.z, Is.EqualTo(-2f).Within(1e-4f));
                Assert.That(box.max.x, Is.EqualTo(2f).Within(1e-4f));
                Assert.That(box.max.y, Is.EqualTo(4f).Within(1e-4f));
                Assert.That(box.max.z, Is.EqualTo(3f).Within(1e-4f));
            });
        }
        finally
        {
            Marshal.DestroyStructure<Mesh>(meshPtr);
            Marshal.FreeHGlobal(meshPtr);
            Marshal.FreeHGlobal(verts);
        }
    }
}
