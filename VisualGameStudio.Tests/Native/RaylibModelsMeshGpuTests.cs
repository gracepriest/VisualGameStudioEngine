using System;
using System.IO;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Runtime correctness for the GPU-bound mesh fns under a live GL context — [Category("Integration")] + [NonParallelizable]
/// (owns the single global raylib window). The 11 GenMesh* generators call UploadMesh internally, so they need a real GL
/// context; UploadMesh/UpdateMeshBuffer/GenMeshTangents/ExportMesh(AsCode)/UnloadMesh are exercised here too. Real oracles
/// where cheap: GenMeshCube(2,2,2) -> GetMeshBoundingBox is exactly [-1,1]^3; GenMeshTangents flips the tangents pointer
/// from null to non-null (proves the ByRef write-back); UploadMesh fills mesh.vboId (proves the Mesh* mutate-in-place);
/// ExportMesh(AsCode) return true and the files appear on disk. Allocator discipline: GenMesh* meshes were RL_MALLOC'd
/// inside the DLL so UnloadMesh (RL_FREE) matches; the ONE hand-built mesh uses Marshal.AllocHGlobal (a different heap), so
/// it is NEVER passed to UnloadMesh — we FreeHGlobal our own pointers instead. Self-skips headless / on a stale DLL.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class RaylibModelsMeshGpuTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;
    private const uint FLAG_WINDOW_HIDDEN = 0x00000080;

    [StructLayout(LayoutKind.Sequential)] private struct Vector3 { public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; } }
    [StructLayout(LayoutKind.Sequential)] private struct BoundingBox { public Vector3 min, max; }
    [StructLayout(LayoutKind.Sequential)] private struct Image { public IntPtr data; public int width, height, mipmaps, format; }

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

    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern void Framework_InitWindow(int width, int height, string title);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_CloseWindow();
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsWindowReady();
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetWindowState(uint flags);

    [DllImport(DLL, CallingConvention = CC)] private static extern Mesh Framework_GenMeshPoly(int sides, float radius);
    [DllImport(DLL, CallingConvention = CC)] private static extern Mesh Framework_GenMeshPlane(float width, float length, int resX, int resZ);
    [DllImport(DLL, CallingConvention = CC)] private static extern Mesh Framework_GenMeshCube(float width, float height, float length);
    [DllImport(DLL, CallingConvention = CC)] private static extern Mesh Framework_GenMeshSphere(float radius, int rings, int slices);
    [DllImport(DLL, CallingConvention = CC)] private static extern Mesh Framework_GenMeshHemiSphere(float radius, int rings, int slices);
    [DllImport(DLL, CallingConvention = CC)] private static extern Mesh Framework_GenMeshCylinder(float radius, float height, int slices);
    [DllImport(DLL, CallingConvention = CC)] private static extern Mesh Framework_GenMeshCone(float radius, float height, int slices);
    [DllImport(DLL, CallingConvention = CC)] private static extern Mesh Framework_GenMeshTorus(float radius, float size, int radSeg, int sides);
    [DllImport(DLL, CallingConvention = CC)] private static extern Mesh Framework_GenMeshKnot(float radius, float size, int radSeg, int sides);
    [DllImport(DLL, CallingConvention = CC)] private static extern Mesh Framework_GenMeshHeightmap(Image heightmap, Vector3 size);
    [DllImport(DLL, CallingConvention = CC)] private static extern Mesh Framework_GenMeshCubicmap(Image cubicmap, Vector3 cubeSize);

    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UploadMesh(ref Mesh mesh, [MarshalAs(UnmanagedType.I1)] bool dynamic);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UpdateMeshBuffer(Mesh mesh, int index, IntPtr data, int dataSize, int offset);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadMesh(Mesh mesh);
    [DllImport(DLL, CallingConvention = CC)] private static extern BoundingBox Framework_GetMeshBoundingBox(Mesh mesh);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_GenMeshTangents(ref Mesh mesh);
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_ExportMesh(Mesh mesh, string fileName);
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_ExportMeshAsCode(Mesh mesh, string fileName);

    [Test]
    public void Mesh_generators_and_management_execute_under_a_window()
    {
        try { Framework_InitWindow(320, 240, "vgs_models_mesh_test"); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); return; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates the mesh exports; refresh IDE\\ first."); return; }

        if (!Framework_IsWindowReady())
        {
            try { Framework_CloseWindow(); } catch { /* nothing to tear down */ }
            Assert.Ignore("No GL window could be created in this environment (headless).");
            return;
        }

        var tmpObj = Path.Combine(Path.GetTempPath(), "vgs_mesh_export_" + Guid.NewGuid().ToString("N") + ".obj");
        var tmpCode = Path.Combine(Path.GetTempPath(), "vgs_mesh_export_" + Guid.NewGuid().ToString("N") + ".h");
        IntPtr imgData = IntPtr.Zero, handVerts = IntPtr.Zero, updVerts = IntPtr.Zero;

        try
        {
            Framework_SetWindowState(FLAG_WINDOW_HIDDEN);

            // First mesh export exercised — a pre-batch DLL surfaces the missing entry point here. Assert.Ignore throws
            // through the outer finally (single CloseWindow); do NOT close here too (a second rlglClose() with no live GL
            // context can AV).
            Mesh cube;
            try { cube = Framework_GenMeshCube(2f, 2f, 2f); }
            catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates the mesh exports; refresh IDE\\ first."); return; }

            Assert.That(cube.vertexCount, Is.GreaterThan(0), "GenMeshCube must produce vertices");
            Assert.That(cube.triangleCount, Is.GreaterThan(0), "GenMeshCube must produce triangles");

            // Oracle: a 2x2x2 cube centered at the origin has bounding box exactly [-1,1] on each axis.
            var box = Framework_GetMeshBoundingBox(cube);
            Assert.Multiple(() =>
            {
                Assert.That(box.min.x, Is.EqualTo(-1f).Within(1e-3f));
                Assert.That(box.min.y, Is.EqualTo(-1f).Within(1e-3f));
                Assert.That(box.min.z, Is.EqualTo(-1f).Within(1e-3f));
                Assert.That(box.max.x, Is.EqualTo(1f).Within(1e-3f));
                Assert.That(box.max.y, Is.EqualTo(1f).Within(1e-3f));
                Assert.That(box.max.z, Is.EqualTo(1f).Within(1e-3f));
            });

            // GenMeshTangents: null -> non-null tangents pointer (proves the Mesh* write-back).
            Assert.That(cube.tangents, Is.EqualTo(IntPtr.Zero), "GenMeshCube leaves tangents unset");
            Framework_GenMeshTangents(ref cube);
            Assert.That(cube.tangents, Is.Not.EqualTo(IntPtr.Zero), "GenMeshTangents must allocate the tangents array");

            // Export to disk (pure CPU): both return true and the files appear.
            Assert.Multiple(() =>
            {
                Assert.That(Framework_ExportMesh(cube, tmpObj), Is.True, "ExportMesh should succeed");
                Assert.That(File.Exists(tmpObj), Is.True, "ExportMesh should write the .obj");
                Assert.That(Framework_ExportMeshAsCode(cube, tmpCode), Is.True, "ExportMeshAsCode should succeed");
                Assert.That(File.Exists(tmpCode), Is.True, "ExportMeshAsCode should write the .h");
            });

            Framework_UnloadMesh(cube);  // raylib-allocated -> RL_FREE matches

            // The other generators: each produces geometry and unloads cleanly.
            GenAndUnload("GenMeshPoly", () => Framework_GenMeshPoly(6, 1f));
            GenAndUnload("GenMeshPlane", () => Framework_GenMeshPlane(2f, 2f, 2, 2));
            GenAndUnload("GenMeshSphere", () => Framework_GenMeshSphere(1f, 8, 8));
            GenAndUnload("GenMeshHemiSphere", () => Framework_GenMeshHemiSphere(1f, 8, 8));
            GenAndUnload("GenMeshCylinder", () => Framework_GenMeshCylinder(1f, 2f, 8));
            GenAndUnload("GenMeshCone", () => Framework_GenMeshCone(1f, 2f, 8));
            GenAndUnload("GenMeshTorus", () => Framework_GenMeshTorus(0.3f, 1f, 8, 8));
            GenAndUnload("GenMeshKnot", () => Framework_GenMeshKnot(0.3f, 1f, 8, 8));

            // Heightmap / cubicmap take an Image BY VALUE (the highest-risk marshaling in this file) -> prove real geometry,
            // not just no-throw. A 4x4 black/white checkerboard (format 7 RGBA) guarantees a full heightmap grid AND
            // cubicmap walls, so a garbled Image (misread width/height/data/format -> empty or degenerate mesh) fails the
            // vertexCount oracle instead of passing green.
            imgData = Marshal.AllocHGlobal(4 * 4 * 4);
            var pixels = new byte[4 * 4 * 4];
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                {
                    byte v = (byte)(((x + y) % 2 == 0) ? 255 : 0);
                    int o = (y * 4 + x) * 4;
                    pixels[o] = v; pixels[o + 1] = v; pixels[o + 2] = v; pixels[o + 3] = 255;
                }
            Marshal.Copy(pixels, 0, imgData, pixels.Length);
            var img = new Image { data = imgData, width = 4, height = 4, mipmaps = 1, format = 7 };

            Mesh hm = default, cm = default;
            Assert.DoesNotThrow(() => hm = Framework_GenMeshHeightmap(img, new Vector3(2f, 1f, 2f)),
                "GenMeshHeightmap must marshal the Image-by-value + Vector3 and execute");
            Assert.That(hm.vertexCount, Is.GreaterThan(0), "GenMeshHeightmap must produce geometry (Image marshaled correctly)");
            Framework_UnloadMesh(hm);

            Assert.DoesNotThrow(() => cm = Framework_GenMeshCubicmap(img, new Vector3(1f, 1f, 1f)),
                "GenMeshCubicmap must marshal the Image-by-value + Vector3 and execute");
            Assert.That(cm.vertexCount, Is.GreaterThan(0), "GenMeshCubicmap must produce geometry (Image marshaled correctly)");
            Framework_UnloadMesh(cm);

            // UploadMesh + UpdateMeshBuffer on a HAND-BUILT mesh (AllocHGlobal heap — never UnloadMesh'd here).
            handVerts = Marshal.AllocHGlobal(9 * sizeof(float));
            Marshal.Copy(new[] { -1f, -1f, 0f, 1f, -1f, 0f, 0f, 1f, 0f }, 0, handVerts, 9);
            var hand = new Mesh { vertexCount = 3, triangleCount = 1, vertices = handVerts };
            Framework_UploadMesh(ref hand, false);
            Assert.That(hand.vboId, Is.Not.EqualTo(IntPtr.Zero), "UploadMesh must allocate the vboId array and write it back");

            updVerts = Marshal.AllocHGlobal(9 * sizeof(float));
            Marshal.Copy(new[] { -2f, -2f, 0f, 2f, -2f, 0f, 0f, 2f, 0f }, 0, updVerts, 9);
            Assert.DoesNotThrow(() => Framework_UpdateMeshBuffer(hand, 0, updVerts, 9 * sizeof(float), 0),
                "UpdateMeshBuffer must marshal the mesh + raw data pointer and update VBO 0");
            // NOTE: 'hand' is intentionally not UnloadMesh'd — its vertices are AllocHGlobal'd (foreign heap); CloseWindow
            // reclaims the GL buffers, and we free our own pointers below.
        }
        finally
        {
            Framework_CloseWindow();
            if (imgData != IntPtr.Zero) Marshal.FreeHGlobal(imgData);
            if (handVerts != IntPtr.Zero) Marshal.FreeHGlobal(handVerts);
            if (updVerts != IntPtr.Zero) Marshal.FreeHGlobal(updVerts);
            try { if (File.Exists(tmpObj)) File.Delete(tmpObj); } catch { /* best effort */ }
            try { if (File.Exists(tmpCode)) File.Delete(tmpCode); } catch { /* best effort */ }
        }
    }

    private static void GenAndUnload(string name, Func<Mesh> gen)
    {
        Mesh m = default;
        Assert.DoesNotThrow(() => m = gen(), $"{name} must marshal its scalars and execute against real GL");
        Assert.That(m.vertexCount, Is.GreaterThan(0), $"{name} must produce vertices");
        Framework_UnloadMesh(m);
    }
}
