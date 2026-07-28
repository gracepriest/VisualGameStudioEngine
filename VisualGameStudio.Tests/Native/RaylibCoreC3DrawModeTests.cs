using System;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Runtime correctness for rcore Batch core-C3 (drawing modes + VR simulator) under a live GL context —
/// [Category("Integration")] + [NonParallelizable] (owns the single global raylib window). Two halves:
///   1. The VR-value ORACLE: with a real context raylib actually computes the stereo config, so LoadVrStereoConfig now
///      returns MEANINGFUL data. viewOffset is a per-eye MatrixTranslate, so its diagonal is 1 and its m12 field holds
///      -/+ interpupillaryDistance*0.5. Asserting that proves, in one shot, (a) VrDeviceInfo marshalled IN (the IPD was
///      used), (b) the nested Matrix[2] marshalled OUT with correct element+field offsets, and (c) the scrambled Matrix
///      field order (m12 at field index 3). This is the strong end-to-end proof the headless canary can't give.
///   2. The draw-mode calls: BeginMode3D (Camera3D by value), BeginBlendMode/BeginScissorMode (ints), and
///      BeginVrStereoMode (VrStereoConfig by value) run inside BeginDrawing/EndDrawing under DoesNotThrow.
/// Self-skips via Assert.Ignore when headless or when the DLL predates the C3 exports.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class RaylibCoreC3DrawModeTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;
    private const uint FLAG_WINDOW_HIDDEN = 0x00000080;
    private const int BLEND_ADDITIVE = 1;

    [StructLayout(LayoutKind.Sequential)] private struct Vector3 { public float x, y, z; }
    [StructLayout(LayoutKind.Sequential)] private struct Camera3D { public Vector3 position, target, up; public float fovy; public int projection; }
    [StructLayout(LayoutKind.Sequential)] private struct Matrix { public float m0, m4, m8, m12, m1, m5, m9, m13, m2, m6, m10, m14, m3, m7, m11, m15; }

    [StructLayout(LayoutKind.Sequential)]
    private struct VrDeviceInfo
    {
        public int hResolution, vResolution;
        public float hScreenSize, vScreenSize, eyeToScreenDistance, lensSeparationDistance, interpupillaryDistance;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] lensDistortionValues;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] chromaAbCorrection;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VrStereoConfig
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)] public Matrix[] projection;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)] public Matrix[] viewOffset;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)] public float[] leftLensCenter;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)] public float[] rightLensCenter;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)] public float[] leftScreenCenter;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)] public float[] rightScreenCenter;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)] public float[] scale;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)] public float[] scaleIn;
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
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_BeginBlendMode(int mode);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_EndBlendMode();
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_BeginScissorMode(int x, int y, int width, int height);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_EndScissorMode();
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_BeginVrStereoMode(VrStereoConfig config);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_EndVrStereoMode();
    [DllImport(DLL, CallingConvention = CC)] private static extern VrStereoConfig Framework_LoadVrStereoConfig(VrDeviceInfo device);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadVrStereoConfig(VrStereoConfig config);

    [Test]
    public void Vr_config_values_and_draw_modes_marshal_under_a_window()
    {
        try { Framework_InitWindow(320, 240, "vgs_c3_drawmode_test"); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); return; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates rcore Batch core-C3 exports; refresh IDE\\ first."); return; }

        if (!Framework_IsWindowReady())
        {
            try { Framework_CloseWindow(); } catch { /* nothing to tear down */ }
            Assert.Ignore("No GL window could be created in this environment (headless).");
            return;
        }

        try
        {
            Framework_SetWindowState(FLAG_WINDOW_HIDDEN);

            var device = MakeDevice();

            // First C3 export exercised — a pre-C3 DLL (InitWindow is pre-existing) surfaces the missing entry point here.
            VrStereoConfig config;
            try { config = Framework_LoadVrStereoConfig(device); }
            catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates rcore Batch core-C3 exports; refresh IDE\\ first."); return; }

            float ipdHalf = device.interpupillaryDistance * 0.5f;
            try
            {
                Assert.Multiple(() =>
                {
                    // With a real GL context raylib computed the config -> projection is a non-zero perspective matrix.
                    Assert.That(SumAbs(config.projection[0]), Is.GreaterThan(0f), "projection[0] is a computed (non-zero) matrix");
                    // viewOffset is a per-eye MatrixTranslate: identity diagonal + the IPD offset in the m12 (X-translate) field.
                    Assert.That(config.viewOffset[0].m0, Is.EqualTo(1f).Within(1e-4f), "viewOffset[0] diagonal m0 == 1 (translation matrix)");
                    Assert.That(config.viewOffset[0].m5, Is.EqualTo(1f).Within(1e-4f), "viewOffset[0] diagonal m5 == 1");
                    Assert.That(config.viewOffset[0].m10, Is.EqualTo(1f).Within(1e-4f), "viewOffset[0] diagonal m10 == 1");
                    Assert.That(config.viewOffset[0].m15, Is.EqualTo(1f).Within(1e-4f), "viewOffset[0] diagonal m15 == 1");
                    // The two eyes carry opposite +/- IPD/2 in the m12 (X-translate) field — raylib 5.5's convention is
                    // eye 0 = +IPD/2, eye 1 = -IPD/2. The EXACT +/-0.035 (= 0.07*0.5) proves IPD marshalled IN, the nested
                    // Matrix[2] marshalled OUT, and the scrambled Matrix field order (m12 is field index 3).
                    Assert.That(config.viewOffset[0].m12, Is.EqualTo(ipdHalf).Within(1e-3f), "eye 0 viewOffset m12 == +IPD/2");
                    Assert.That(config.viewOffset[1].m12, Is.EqualTo(-ipdHalf).Within(1e-3f), "eye 1 viewOffset m12 == -IPD/2");
                    // Distortion scale is positive; the two lens centers differ (real per-eye computation).
                    Assert.That(config.scale[0], Is.GreaterThan(0f), "distortion scale[0] > 0");
                    Assert.That(config.leftLensCenter[0], Is.Not.EqualTo(config.rightLensCenter[0]), "left/right lens centers differ");
                });

                var cam = new Camera3D
                {
                    position = new Vector3 { x = 0f, y = 10f, z = 10f },
                    target = new Vector3 { x = 0f, y = 0f, z = 0f },
                    up = new Vector3 { x = 0f, y = 1f, z = 0f },
                    fovy = 45f,
                    projection = 0, // CAMERA_PERSPECTIVE
                };

                // Every C3 mode call marshals its by-value struct/int args under a live context (Camera3D, VrStereoConfig).
                Assert.DoesNotThrow(() =>
                {
                    Framework_BeginDrawing();
                    Framework_ClearBackground(245, 245, 245, 255);

                    Framework_BeginBlendMode(BLEND_ADDITIVE);
                    Framework_EndBlendMode();

                    Framework_BeginScissorMode(0, 0, 160, 120);
                    Framework_EndScissorMode();

                    Framework_BeginMode3D(cam);
                    Framework_EndMode3D();

                    Framework_BeginVrStereoMode(config);
                    Framework_BeginMode3D(cam);
                    Framework_EndMode3D();
                    Framework_EndVrStereoMode();

                    Framework_EndDrawing();
                }, "C3 draw-mode + VR calls marshal Camera3D/VrStereoConfig by value under a live GL context");
            }
            finally
            {
                try { Framework_UnloadVrStereoConfig(config); } catch { /* no-op unload */ }
            }
        }
        finally
        {
            Framework_CloseWindow();
        }
    }

    // Standard raylib VR-simulator device parameters (core_vr_simulator example).
    private static VrDeviceInfo MakeDevice() => new VrDeviceInfo
    {
        hResolution = 2160,
        vResolution = 1200,
        hScreenSize = 0.133793f,
        vScreenSize = 0.0669f,
        eyeToScreenDistance = 0.041f,
        lensSeparationDistance = 0.07f,
        interpupillaryDistance = 0.07f,
        lensDistortionValues = new float[] { 1.0f, 0.22f, 0.24f, 0.0f },
        chromaAbCorrection = new float[] { 0.996f, -0.004f, 1.014f, 0.0f },
    };

    private static float SumAbs(Matrix m) =>
        Math.Abs(m.m0) + Math.Abs(m.m4) + Math.Abs(m.m8) + Math.Abs(m.m12) +
        Math.Abs(m.m1) + Math.Abs(m.m5) + Math.Abs(m.m9) + Math.Abs(m.m13) +
        Math.Abs(m.m2) + Math.Abs(m.m6) + Math.Abs(m.m10) + Math.Abs(m.m14) +
        Math.Abs(m.m3) + Math.Abs(m.m7) + Math.Abs(m.m11) + Math.Abs(m.m15);
}
