using System;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Runtime correctness for the rmodels BASIC-3D-SHAPES sub-batch under a live GL context —
/// [Category("Integration")] + [NonParallelizable] (owns the single global raylib window). These 20 functions are void
/// immediate-mode draws (no return value, no queryable state — the only observable is pixels), so the correct correctness
/// bar is: every one MARSHALS its args (Vector3/Vector2/Ray by value, Color-as-4-bytes, the DrawTriangleStrip3D const
/// Vector3* array) and EXECUTES against real rlgl inside a BeginMode3D frame without an access violation. A wrong array
/// pointer or a wrong-sized struct would AV here; static TYPE correctness (Color->bytes, the array param, by-value structs)
/// is separately pinned by the parity guard's type-scan. Self-skips via Assert.Ignore when headless or when the DLL
/// predates the models-shapes exports.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class RaylibModelsShapesDrawTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;
    private const uint FLAG_WINDOW_HIDDEN = 0x00000080;

    [StructLayout(LayoutKind.Sequential)] private struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } }
    [StructLayout(LayoutKind.Sequential)] private struct Vector3 { public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; } }
    [StructLayout(LayoutKind.Sequential)] private struct Camera3D { public Vector3 position, target, up; public float fovy; public int projection; }
    [StructLayout(LayoutKind.Sequential)] private struct Ray { public Vector3 position, direction; public Ray(Vector3 p, Vector3 d) { position = p; direction = d; } }

    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern void Framework_InitWindow(int width, int height, string title);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_CloseWindow();
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsWindowReady();
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetWindowState(uint flags);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_BeginDrawing();
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_EndDrawing();
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ClearBackground(byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_BeginMode3D(Camera3D camera);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_EndMode3D();

    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DrawLine3D(Vector3 s, Vector3 e, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DrawPoint3D(Vector3 position, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DrawCircle3D(Vector3 center, float radius, Vector3 axis, float angle, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DrawTriangle3D(Vector3 v1, Vector3 v2, Vector3 v3, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DrawTriangleStrip3D(Vector3[] points, int pointCount, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DrawCube(Vector3 position, float w, float h, float l, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DrawCubeV(Vector3 position, Vector3 size, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DrawCubeWires(Vector3 position, float w, float h, float l, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DrawCubeWiresV(Vector3 position, Vector3 size, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DrawSphere(Vector3 centerPos, float radius, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DrawSphereEx(Vector3 centerPos, float radius, int rings, int slices, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DrawSphereWires(Vector3 centerPos, float radius, int rings, int slices, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DrawCylinder(Vector3 position, float rTop, float rBottom, float height, int slices, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DrawCylinderEx(Vector3 s, Vector3 e, float rStart, float rEnd, int sides, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DrawCylinderWires(Vector3 position, float rTop, float rBottom, float height, int slices, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DrawCylinderWiresEx(Vector3 s, Vector3 e, float rStart, float rEnd, int sides, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DrawCapsule(Vector3 s, Vector3 e, float radius, int slices, int rings, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DrawCapsuleWires(Vector3 s, Vector3 e, float radius, int slices, int rings, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DrawPlane(Vector3 centerPos, Vector2 size, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DrawRay(Ray ray, byte r, byte g, byte b, byte a);

    [Test]
    public void All_basic_3d_shape_draws_marshal_and_execute_under_a_window()
    {
        try { Framework_InitWindow(320, 240, "vgs_models_shapes_test"); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); return; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates the models-shapes exports; refresh IDE\\ first."); return; }

        if (!Framework_IsWindowReady())
        {
            try { Framework_CloseWindow(); } catch { /* nothing to tear down */ }
            Assert.Ignore("No GL window could be created in this environment (headless).");
            return;
        }

        try
        {
            Framework_SetWindowState(FLAG_WINDOW_HIDDEN);

            var cam = new Camera3D
            {
                position = new Vector3(0f, 10f, 10f),
                target = new Vector3(0f, 0f, 0f),
                up = new Vector3(0f, 1f, 0f),
                fovy = 45f,
                projection = 0, // CAMERA_PERSPECTIVE
            };

            // A non-degenerate triangle strip to exercise the const Vector3* array marshaling.
            var strip = new[]
            {
                new Vector3(-1f, 0f, 0f), new Vector3(1f, 0f, 0f),
                new Vector3(-1f, 1f, 0f), new Vector3(1f, 1f, 0f),
            };

            // RED-ish; exact color is irrelevant (nothing is read back), but real geometry makes rlgl process vertices.
            const byte R = 230, G = 41, B = 55, A = 255;

            // First models-shapes export exercised — a pre-batch DLL surfaces the missing entry point here. Assert.Ignore
            // throws through the outer finally, which performs the single CloseWindow (do NOT close here too — a second
            // CloseWindow re-runs rlglClose() with no live GL context and can AV).
            try { Framework_DrawCube(new Vector3(0, 0, 0), 1f, 1f, 1f, R, G, B, A); }
            catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates the models-shapes exports; refresh IDE\\ first."); return; }

            Assert.DoesNotThrow(() =>
            {
                Framework_BeginDrawing();
                Framework_ClearBackground(245, 245, 245, 255);
                Framework_BeginMode3D(cam);

                Framework_DrawLine3D(new Vector3(0, 0, 0), new Vector3(1, 1, 1), R, G, B, A);
                Framework_DrawPoint3D(new Vector3(0, 1, 0), R, G, B, A);
                Framework_DrawCircle3D(new Vector3(0, 0, 0), 1f, new Vector3(0, 1, 0), 0f, R, G, B, A);
                Framework_DrawTriangle3D(new Vector3(-1, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0), R, G, B, A);
                Framework_DrawTriangleStrip3D(strip, strip.Length, R, G, B, A);
                Framework_DrawCube(new Vector3(0, 0, 0), 1f, 1f, 1f, R, G, B, A);
                Framework_DrawCubeV(new Vector3(0, 0, 0), new Vector3(1, 1, 1), R, G, B, A);
                Framework_DrawCubeWires(new Vector3(0, 0, 0), 1f, 1f, 1f, R, G, B, A);
                Framework_DrawCubeWiresV(new Vector3(0, 0, 0), new Vector3(1, 1, 1), R, G, B, A);
                Framework_DrawSphere(new Vector3(0, 0, 0), 1f, R, G, B, A);
                Framework_DrawSphereEx(new Vector3(0, 0, 0), 1f, 8, 8, R, G, B, A);
                Framework_DrawSphereWires(new Vector3(0, 0, 0), 1f, 8, 8, R, G, B, A);
                Framework_DrawCylinder(new Vector3(0, 0, 0), 1f, 1f, 2f, 8, R, G, B, A);
                Framework_DrawCylinderEx(new Vector3(0, 0, 0), new Vector3(0, 2, 0), 1f, 1f, 8, R, G, B, A);
                Framework_DrawCylinderWires(new Vector3(0, 0, 0), 1f, 1f, 2f, 8, R, G, B, A);
                Framework_DrawCylinderWiresEx(new Vector3(0, 0, 0), new Vector3(0, 2, 0), 1f, 1f, 8, R, G, B, A);
                Framework_DrawCapsule(new Vector3(0, 0, 0), new Vector3(0, 2, 0), 0.5f, 8, 8, R, G, B, A);
                Framework_DrawCapsuleWires(new Vector3(0, 0, 0), new Vector3(0, 2, 0), 0.5f, 8, 8, R, G, B, A);
                Framework_DrawPlane(new Vector3(0, 0, 0), new Vector2(2, 2), R, G, B, A);
                Framework_DrawRay(new Ray(new Vector3(0, 0, 0), new Vector3(0, 1, 0)), R, G, B, A);

                Framework_EndMode3D();
                Framework_EndDrawing();
            }, "all 20 basic-3d-shape draws marshal their args and execute against real rlgl without an access violation");
        }
        finally
        {
            Framework_CloseWindow();
        }
    }
}
