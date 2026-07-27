using System;
using System.IO;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Runtime correctness for the GPU-bound model fns under a live GL context — [Category("Integration")] + [NonParallelizable]
/// (owns the single global raylib window). LoadModel/LoadModelFromMesh upload to the GPU and the draws run through rlgl, so
/// they need a real context. Real oracles where cheap: LoadModelFromMesh(GenMeshCube(2,2,2)) yields a valid 1-mesh/1-material
/// Model whose GetModelBoundingBox is exactly [-1,1]^3; LoadModel round-trips an exported .obj into a valid model. The 10
/// draws (model + wires + points + bounding box + 3 billboards) execute inside a BeginMode3D frame without an access
/// violation. Ownership: LoadModelFromMesh takes the Mesh, so that cube is freed via UnloadModel, never UnloadMesh; the cube
/// exported to disk is separately RL_MALLOC'd -> UnloadMesh. Self-skips headless / on a stale DLL.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class RaylibModelsModelGpuTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;
    private const uint FLAG_WINDOW_HIDDEN = 0x00000080;

    [StructLayout(LayoutKind.Sequential)] private struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } }
    [StructLayout(LayoutKind.Sequential)] private struct Vector3 { public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; } }
    [StructLayout(LayoutKind.Sequential)] private struct Rectangle { public float x, y, width, height; public Rectangle(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; } }
    [StructLayout(LayoutKind.Sequential)] private struct Camera3D { public Vector3 position, target, up; public float fovy; public int projection; }
    [StructLayout(LayoutKind.Sequential)] private struct BoundingBox { public Vector3 min, max; }
    [StructLayout(LayoutKind.Sequential)] private struct Image { public IntPtr data; public int width, height, mipmaps, format; }
    [StructLayout(LayoutKind.Sequential)] private struct Texture2D { public uint id; public int width, height, mipmaps, format; }

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

    [StructLayout(LayoutKind.Sequential)]
    private struct Matrix { public float m0, m4, m8, m12, m1, m5, m9, m13, m2, m6, m10, m14, m3, m7, m11, m15; }

    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern void Framework_InitWindow(int width, int height, string title);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_CloseWindow();
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsWindowReady();
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetWindowState(uint flags);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_BeginDrawing();
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_EndDrawing();
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ClearBackground(byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_BeginMode3D(Camera3D camera);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_EndMode3D();

    [DllImport(DLL, CallingConvention = CC)] private static extern Mesh Framework_GenMeshCube(float width, float height, float length);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadMesh(Mesh mesh);
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_ExportMesh(Mesh mesh, string fileName);

    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern Model Framework_LoadModel(string fileName);
    [DllImport(DLL, CallingConvention = CC)] private static extern Model Framework_LoadModelFromMesh(Mesh mesh);
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsModelValid(Model model);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadModel(Model model);
    [DllImport(DLL, CallingConvention = CC)] private static extern BoundingBox Framework_GetModelBoundingBox(Model model);

    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DrawModel(Model model, Vector3 position, float scale, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DrawModelEx(Model model, Vector3 position, Vector3 axis, float angle, Vector3 scale, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DrawModelWires(Model model, Vector3 position, float scale, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DrawModelWiresEx(Model model, Vector3 position, Vector3 axis, float angle, Vector3 scale, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DrawModelPoints(Model model, Vector3 position, float scale, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DrawModelPointsEx(Model model, Vector3 position, Vector3 axis, float angle, Vector3 scale, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DrawBoundingBox(BoundingBox box, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DrawBillboard(Camera3D camera, Texture2D texture, Vector3 position, float scale, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DrawBillboardRec(Camera3D camera, Texture2D texture, Rectangle source, Vector3 position, Vector2 size, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DrawBillboardPro(Camera3D camera, Texture2D texture, Rectangle source, Vector3 position, Vector3 up, Vector2 size, Vector2 origin, float rotation, byte r, byte g, byte b, byte a);

    [DllImport(DLL, CallingConvention = CC)] private static extern Image Framework_GenImageColor(int width, int height, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern Texture2D Framework_LoadTextureFromImage(Image image);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadTexture(Texture2D texture);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadImage(Image image);

    [Test]
    public void Model_load_query_and_draw_execute_under_a_window()
    {
        try { Framework_InitWindow(320, 240, "vgs_models_model_test"); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); return; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates the model exports; refresh IDE\\ first."); return; }

        if (!Framework_IsWindowReady())
        {
            try { Framework_CloseWindow(); } catch { /* nothing to tear down */ }
            Assert.Ignore("No GL window could be created in this environment (headless).");
            return;
        }

        var tmpObj = Path.Combine(Path.GetTempPath(), "vgs_model_" + Guid.NewGuid().ToString("N") + ".obj");
        Model model = default, fileModel = default;
        Mesh cube2 = default;
        Texture2D tex = default; Image img = default;
        bool haveModel = false, haveFileModel = false, haveCube2 = false, haveTex = false, haveImg = false;

        try
        {
            Framework_SetWindowState(FLAG_WINDOW_HIDDEN);

            var cube = Framework_GenMeshCube(2f, 2f, 2f);  // from the mesh batch (already shipped)

            // First MODEL export exercised — a pre-batch DLL surfaces the missing entry point here (do NOT CloseWindow in the
            // catch; the outer finally does the single close). LoadModelFromMesh TAKES the cube -> freed via UnloadModel only.
            try { model = Framework_LoadModelFromMesh(cube); haveModel = true; }
            catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates the model exports; refresh IDE\\ first."); return; }

            Assert.Multiple(() =>
            {
                Assert.That(model.meshCount, Is.GreaterThan(0), "LoadModelFromMesh must attach the mesh");
                Assert.That(model.materialCount, Is.GreaterThan(0), "LoadModelFromMesh must attach a default material");
                Assert.That(Framework_IsModelValid(model), Is.True, "the loaded model must be valid");
            });

            // Oracle: the cube's model bounding box is exactly [-1,1]^3.
            var box = Framework_GetModelBoundingBox(model);
            Assert.Multiple(() =>
            {
                Assert.That(box.min.x, Is.EqualTo(-1f).Within(1e-3f));
                Assert.That(box.min.y, Is.EqualTo(-1f).Within(1e-3f));
                Assert.That(box.min.z, Is.EqualTo(-1f).Within(1e-3f));
                Assert.That(box.max.x, Is.EqualTo(1f).Within(1e-3f));
                Assert.That(box.max.y, Is.EqualTo(1f).Within(1e-3f));
                Assert.That(box.max.z, Is.EqualTo(1f).Within(1e-3f));
            });

            // LoadModel from a file: export a fresh cube to .obj, load it, confirm validity. The exported cube is separately
            // owned -> UnloadMesh in finally (a mid-test assert failure below must not leak it).
            cube2 = Framework_GenMeshCube(1f, 1f, 1f); haveCube2 = true;
            if (Framework_ExportMesh(cube2, tmpObj) && File.Exists(tmpObj))
            {
                fileModel = Framework_LoadModel(tmpObj); haveFileModel = true;
                Assert.That(Framework_IsModelValid(fileModel), Is.True, "a model loaded from the exported .obj must be valid");
                Assert.That(fileModel.meshCount, Is.GreaterThan(0), "the loaded .obj model must have a mesh");
            }

            // A texture for the billboards.
            img = Framework_GenImageColor(4, 4, 255, 255, 255, 255); haveImg = true;
            tex = Framework_LoadTextureFromImage(img); haveTex = true;
            var src = new Rectangle(0, 0, tex.width, tex.height);

            var cam = new Camera3D
            {
                position = new Vector3(0f, 10f, 10f),
                target = new Vector3(0f, 0f, 0f),
                up = new Vector3(0f, 1f, 0f),
                fovy = 45f,
                projection = 0,
            };
            const byte R = 230, G = 41, B = 55, A = 255;
            var pos = new Vector3(0, 0, 0);
            var axis = new Vector3(0, 1, 0);
            var scale3 = new Vector3(1, 1, 1);

            Assert.DoesNotThrow(() =>
            {
                Framework_BeginDrawing();
                Framework_ClearBackground(245, 245, 245, 255);
                Framework_BeginMode3D(cam);

                Framework_DrawModel(model, pos, 1f, R, G, B, A);
                Framework_DrawModelEx(model, pos, axis, 30f, scale3, R, G, B, A);
                Framework_DrawModelWires(model, pos, 1f, R, G, B, A);
                Framework_DrawModelWiresEx(model, pos, axis, 30f, scale3, R, G, B, A);
                Framework_DrawModelPoints(model, pos, 1f, R, G, B, A);
                Framework_DrawModelPointsEx(model, pos, axis, 30f, scale3, R, G, B, A);
                Framework_DrawBoundingBox(box, R, G, B, A);
                Framework_DrawBillboard(cam, tex, new Vector3(2, 0, 0), 1f, R, G, B, A);
                Framework_DrawBillboardRec(cam, tex, src, new Vector3(-2, 0, 0), new Vector2(1, 1), R, G, B, A);
                Framework_DrawBillboardPro(cam, tex, src, new Vector3(0, 0, 2), new Vector3(0, 1, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), 15f, R, G, B, A);

                Framework_EndMode3D();
                Framework_EndDrawing();
            }, "all 10 model/billboard draws marshal their args and execute against real rlgl without an access violation");
        }
        finally
        {
            if (haveTex) Framework_UnloadTexture(tex);
            if (haveImg) Framework_UnloadImage(img);
            if (haveFileModel) Framework_UnloadModel(fileModel);
            if (haveCube2) Framework_UnloadMesh(cube2);  // separately owned (ExportMesh does not take it)
            if (haveModel) Framework_UnloadModel(model);  // frees the cube taken by LoadModelFromMesh
            Framework_CloseWindow();
            try { if (File.Exists(tmpObj)) File.Delete(tmpObj); } catch { /* best effort */ }
        }
    }
}
