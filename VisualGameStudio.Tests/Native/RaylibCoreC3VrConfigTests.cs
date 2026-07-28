using System;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// HEADLESS marshaling canary for the hard half of rcore Batch core-C3: the VR-simulator structs. VrDeviceInfo and
/// VrStereoConfig both carry NESTED FIXED-SIZE arrays (&lt;MarshalAs(ByValArray, SizeConst)&gt;), which makes them
/// non-blittable — and LoadVrStereoConfig RETURNS the 304-byte VrStereoConfig BY VALUE. If .NET could not marshal a
/// non-blittable struct return, the very first call would throw MarshalDirectiveException at stub-build time; so this test
/// is the canary for the whole approach and it runs in the FAST SUBSET (no window). raylib gates the config computation on
/// rlGetVersion()==RL_OPENGL_33, so with no GL context the returned config is ZEROED — we therefore assert only the
/// STRUCTURE (every ByValArray came back allocated at its SizeConst length) and that the by-value param/return round-trip
/// does not throw. The meaningful VALUES are the job of RaylibCoreC3DrawModeTests under a live window.
/// </summary>
[TestFixture]
public class RaylibCoreC3VrConfigTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;

    [StructLayout(LayoutKind.Sequential)]
    private struct Matrix { public float m0, m4, m8, m12, m1, m5, m9, m13, m2, m6, m10, m14, m3, m7, m11, m15; }

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

    [DllImport(DLL, CallingConvention = CC)] private static extern VrStereoConfig Framework_LoadVrStereoConfig(VrDeviceInfo device);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadVrStereoConfig(VrStereoConfig config);

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

    [Test]
    public void LoadVrStereoConfig_nonblittable_return_marshals_structurally()
    {
        var device = MakeDevice();

        VrStereoConfig config = default;
        try { config = Framework_LoadVrStereoConfig(device); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); return; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates rcore Batch core-C3 exports; refresh IDE\\ first."); return; }

        // Reaching here already proves the non-blittable VrStereoConfig by-value RETURN (Matrix[2] + Matrix[2] + 6x float[2])
        // built a marshalling stub and executed — a MarshalDirectiveException would have thrown above. Now confirm every
        // fixed-size array came back allocated at its SizeConst length (values are zero headless — no GL context).
        Assert.Multiple(() =>
        {
            Assert.That(config.projection, Is.Not.Null, "projection array marshalled");
            Assert.That(config.projection.Length, Is.EqualTo(2), "projection is Matrix[2]");
            Assert.That(config.viewOffset, Is.Not.Null, "viewOffset array marshalled");
            Assert.That(config.viewOffset.Length, Is.EqualTo(2), "viewOffset is Matrix[2]");
            Assert.That(config.leftLensCenter?.Length, Is.EqualTo(2), "leftLensCenter is float[2]");
            Assert.That(config.rightLensCenter?.Length, Is.EqualTo(2), "rightLensCenter is float[2]");
            Assert.That(config.leftScreenCenter?.Length, Is.EqualTo(2), "leftScreenCenter is float[2]");
            Assert.That(config.rightScreenCenter?.Length, Is.EqualTo(2), "rightScreenCenter is float[2]");
            Assert.That(config.scale?.Length, Is.EqualTo(2), "scale is float[2]");
            Assert.That(config.scaleIn?.Length, Is.EqualTo(2), "scaleIn is float[2]");
        });

        // The non-blittable VrStereoConfig by-value PARAM marshals back into the engine without a stack/struct mismatch.
        Assert.DoesNotThrow(() => Framework_UnloadVrStereoConfig(config),
            "UnloadVrStereoConfig round-trips the non-blittable VrStereoConfig by value");
    }
}
