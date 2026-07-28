using System;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Runtime correctness for the GPU-bound material fns under a live GL context — [Category("Integration")] + [NonParallelizable]
/// (owns the single global raylib window). LoadMaterialDefault creates the default shader + a MAX_MATERIAL_MAPS map array on the
/// GPU; SetMaterialTexture / DrawMesh / DrawMeshInstanced all run through rlgl. Real oracles where cheap:
///   • LoadMaterialDefault yields a valid Material (shader.id > 0, maps != NULL, params length 4) — the positive path the
///     headless suite can't reach.
///   • SetMaterialTexture(&mat, DIFFUSE, tex) writes through the maps pointer: reading maps[0] back as a MaterialMap shows the
///     texture id we set — this pins the MaterialMap ABI (offset of texture) AND the maps offset inside Material by value.
///   • SetModelMeshMaterial(&model, 0, 0) round-trips the ByRef Model and leaves meshMaterial[0] == 0.
///   • DrawMesh + DrawMeshInstanced marshal a Material by value (and a Matrix[] instance array) and execute inside a BeginMode3D
///     frame without an access violation.
/// Ownership: the texture is handed to `mat` via SetMaterialTexture, so UnloadMaterial(mat) frees it — it is NOT separately
/// UnloadTexture'd (that would double-free). LoadModelFromMesh takes modelCube -> freed via UnloadModel; drawCube is ours ->
/// UnloadMesh. Self-skips headless / on a stale DLL.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class RaylibModelsMaterialsGpuTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;
    private const uint FLAG_WINDOW_HIDDEN = 0x00000080;
    private const int MATERIAL_MAP_DIFFUSE = 0;

    [StructLayout(LayoutKind.Sequential)] private struct Vector3 { public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; } }
    [StructLayout(LayoutKind.Sequential)] private struct Camera3D { public Vector3 position, target, up; public float fovy; public int projection; }
    [StructLayout(LayoutKind.Sequential)] private struct Color { public byte r, g, b, a; }
    [StructLayout(LayoutKind.Sequential)] private struct Image { public IntPtr data; public int width, height, mipmaps, format; }
    [StructLayout(LayoutKind.Sequential)] private struct Texture2D { public uint id; public int width, height, mipmaps, format; }
    [StructLayout(LayoutKind.Sequential)] private struct Shader { public uint id; public IntPtr locs; }
    [StructLayout(LayoutKind.Sequential)] private struct Matrix { public float m0, m4, m8, m12, m1, m5, m9, m13, m2, m6, m10, m14, m3, m7, m11, m15; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MaterialMap { public Texture2D texture; public Color color; public float value; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Material
    {
        public Shader shader;
        public IntPtr maps;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] @params;
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
    [DllImport(DLL, CallingConvention = CC)] private static extern Model Framework_LoadModelFromMesh(Mesh mesh);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadModel(Model model);

    [DllImport(DLL, CallingConvention = CC)] private static extern Image Framework_GenImageColor(int width, int height, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern Texture2D Framework_LoadTextureFromImage(Image image);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadImage(Image image);

    [DllImport(DLL, CallingConvention = CC)] private static extern Material Framework_LoadMaterialDefault();
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsMaterialValid(Material material);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadMaterial(Material material);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetMaterialTexture(ref Material material, int mapType, Texture2D texture);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetModelMeshMaterial(ref Model model, int meshId, int materialId);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DrawMesh(Mesh mesh, Material material, Matrix transform);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DrawMeshInstanced(Mesh mesh, Material material, Matrix[] transforms, int instances);

    private static Matrix Identity() => new Matrix { m0 = 1f, m5 = 1f, m10 = 1f, m15 = 1f };

    [Test]
    public void Material_load_set_and_draw_execute_under_a_window()
    {
        try { Framework_InitWindow(320, 240, "vgs_models_materials_test"); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); return; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates the material exports; refresh IDE\\ first."); return; }

        if (!Framework_IsWindowReady())
        {
            try { Framework_CloseWindow(); } catch { /* nothing to tear down */ }
            Assert.Ignore("No GL window could be created in this environment (headless).");
            return;
        }

        Material mat = default;
        Mesh drawCube = default;
        Model model = default;
        Image img = default;
        bool haveMat = false, haveDrawCube = false, haveModel = false, haveImg = false;

        try
        {
            Framework_SetWindowState(FLAG_WINDOW_HIDDEN);

            // A pre-batch DLL surfaces the missing entry point on the first material call (do NOT CloseWindow in the catch;
            // the outer finally does the single close).
            try { mat = Framework_LoadMaterialDefault(); haveMat = true; }
            catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates the material exports; refresh IDE\\ first."); return; }

            Assert.Multiple(() =>
            {
                Assert.That(Framework_IsMaterialValid(mat), Is.True, "the default material must be valid");
                Assert.That(mat.shader.id, Is.GreaterThan(0u), "default material carries the default shader");
                Assert.That(mat.maps, Is.Not.EqualTo(IntPtr.Zero), "default material allocates its maps array");
                Assert.That(mat.@params, Is.Not.Null.And.Length.EqualTo(4), "params marshals back as an inline float[4]");
            });

            // SetMaterialTexture writes through the maps pointer; read maps[MATERIAL_MAP_DIFFUSE] back to confirm + pin the
            // MaterialMap ABI. The texture is now owned by `mat` (freed by UnloadMaterial) — it is NOT UnloadTexture'd below.
            img = Framework_GenImageColor(4, 4, 120, 200, 80, 255); haveImg = true;
            var tex = Framework_LoadTextureFromImage(img);
            Framework_SetMaterialTexture(ref mat, MATERIAL_MAP_DIFFUSE, tex);
            var diffuse = Marshal.PtrToStructure<MaterialMap>(mat.maps);
            Assert.That(diffuse.texture.id, Is.EqualTo(tex.id), "SetMaterialTexture must place the texture in the DIFFUSE map slot");

            // SetModelMeshMaterial writes materialId through the ByRef Model's meshMaterial[] pointer. The default model's
            // meshMaterial[0] is already calloc-zeroed, and 0 is the only legal materialId for a 1-material model — so to give
            // the assertion teeth we first poison the slot with a sentinel (7): only a binding that actually reaches the native
            // array and writes will reset it to 0 (a no-op / broken ByRef leaves 7).
            var modelCube = Framework_GenMeshCube(1f, 1f, 1f);
            model = Framework_LoadModelFromMesh(modelCube); haveModel = true;  // TAKES modelCube
            Marshal.WriteInt32(model.meshMaterial, 0, 7);
            Framework_SetModelMeshMaterial(ref model, 0, 0);
            Assert.That(Marshal.ReadInt32(model.meshMaterial, 0), Is.EqualTo(0), "SetModelMeshMaterial must write mesh 0's material index back to 0");

            // Draw a real mesh with the material, and an instanced batch, inside a 3D frame.
            drawCube = Framework_GenMeshCube(2f, 2f, 2f); haveDrawCube = true;
            var transforms = new[]
            {
                Identity(),
                new Matrix { m0 = 1f, m5 = 1f, m10 = 1f, m15 = 1f, m12 = 3f },  // translate +x (m12 is column-3 row-0)
            };
            var cam = new Camera3D
            {
                position = new Vector3(0f, 10f, 10f),
                target = new Vector3(0f, 0f, 0f),
                up = new Vector3(0f, 1f, 0f),
                fovy = 45f,
                projection = 0,
            };

            Assert.DoesNotThrow(() =>
            {
                Framework_BeginDrawing();
                Framework_ClearBackground(245, 245, 245, 255);
                Framework_BeginMode3D(cam);

                Framework_DrawMesh(drawCube, mat, Identity());
                Framework_DrawMeshInstanced(drawCube, mat, transforms, transforms.Length);

                Framework_EndMode3D();
                Framework_EndDrawing();
            }, "DrawMesh/DrawMeshInstanced marshal a Material by value (+ a Matrix[] instance array) and execute against real rlgl without an access violation");
        }
        finally
        {
            if (haveModel) Framework_UnloadModel(model);      // frees modelCube taken by LoadModelFromMesh
            if (haveDrawCube) Framework_UnloadMesh(drawCube);  // ours
            if (haveMat) Framework_UnloadMaterial(mat);        // also frees the texture placed in its DIFFUSE map
            if (haveImg) Framework_UnloadImage(img);           // CPU-side image, separate from the texture
            Framework_CloseWindow();
        }
    }
}
