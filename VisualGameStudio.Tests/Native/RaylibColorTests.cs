using System;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Runtime correctness for the raylib textures Batch 3a color/pixel bindings — all GL-free, so
/// fully unit-testable via P/Invoke. Self-contained local [DllImport] with local struct mirrors;
/// self-skips when the engine DLL isn't staged with these exports. The FIRST test
/// (<see cref="GetColor_proves_Color_struct_return"/>) is the go/no-go proof that a 4-byte Color
/// struct marshals correctly as a by-value RETURN — if it fails, the design falls back to a packed
/// uint return (spec §6) before Batch 3b.
/// </summary>
[Category("Integration")]
[TestFixture]
public class RaylibColorTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;

    [StructLayout(LayoutKind.Sequential)] private struct RColor { public byte r, g, b, a; }
    [StructLayout(LayoutKind.Sequential)] private struct RVector3 { public float x, y, z; }
    [StructLayout(LayoutKind.Sequential)] private struct RVector4 { public float x, y, z, w; }

    [DllImport(DLL, CallingConvention = CC)] private static extern int Framework_ColorToInt(byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern RColor Framework_GetColor(uint hexValue);
    [DllImport(DLL, CallingConvention = CC)] private static extern RVector4 Framework_ColorNormalize(byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern RColor Framework_ColorFromNormalized(RVector4 normalized);
    [DllImport(DLL, CallingConvention = CC)] private static extern RVector3 Framework_ColorToHSV(byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern RColor Framework_ColorFromHSV(float h, float s, float v);
    [DllImport(DLL, CallingConvention = CC)] private static extern RColor Framework_Fade(byte r, byte g, byte b, byte a, float alpha);
    [DllImport(DLL, CallingConvention = CC)] private static extern RColor Framework_ColorAlpha(byte r, byte g, byte b, byte a, float alpha);
    [DllImport(DLL, CallingConvention = CC)] private static extern RColor Framework_ColorTint(byte r, byte g, byte b, byte a, byte tr, byte tg, byte tb, byte ta);
    [DllImport(DLL, CallingConvention = CC)] private static extern RColor Framework_ColorBrightness(byte r, byte g, byte b, byte a, float factor);
    [DllImport(DLL, CallingConvention = CC)] private static extern RColor Framework_ColorContrast(byte r, byte g, byte b, byte a, float contrast);
    [DllImport(DLL, CallingConvention = CC)] private static extern RColor Framework_ColorLerp(byte r, byte g, byte b, byte a, byte r2, byte g2, byte b2, byte a2, float factor);
    [DllImport(DLL, CallingConvention = CC)] private static extern RColor Framework_ColorAlphaBlend(byte dr, byte dg, byte db, byte da, byte sr, byte sg, byte sb, byte sa, byte tr, byte tg, byte tb, byte ta);
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_ColorIsEqual(byte r, byte g, byte b, byte a, byte r2, byte g2, byte b2, byte a2);
    [DllImport(DLL, CallingConvention = CC)] private static extern int Framework_GetPixelDataSize(int width, int height, int format);
    [DllImport(DLL, CallingConvention = CC)] private static extern RColor Framework_GetPixelColor(IntPtr srcPtr, int format);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetPixelColor(IntPtr dstPtr, byte r, byte g, byte b, byte a, int format);

    private const int PIXELFORMAT_UNCOMPRESSED_R8G8B8A8 = 7;

    private static T Guard<T>(Func<T> call)
    {
        try { return call(); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); throw; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates color Batch 3a exports; refresh IDE\\ first."); throw; }
    }

    [Test] public void GetColor_proves_Color_struct_return()
    {
        // GO/NO-GO: a 4-byte Color returned BY VALUE must marshal field-for-field.
        var c = Guard(() => Framework_GetColor(0xFF0000FFu));
        Assert.Multiple(() =>
        {
            Assert.That(c.r, Is.EqualTo(255)); Assert.That(c.g, Is.EqualTo(0));
            Assert.That(c.b, Is.EqualTo(0));   Assert.That(c.a, Is.EqualTo(255));
        });
    }

    [Test] public void ColorToInt_packs_RRGGBBAA()
        => Assert.That(Guard(() => Framework_ColorToInt(255, 0, 0, 255)), Is.EqualTo(unchecked((int)0xFF0000FF)));

    [Test] public void Normalize_and_FromNormalized_roundtrip()
    {
        var v = Guard(() => Framework_ColorNormalize(255, 128, 0, 255));
        Assert.Multiple(() =>
        {
            Assert.That(v.x, Is.EqualTo(1f).Within(1e-3)); Assert.That(v.y, Is.EqualTo(128f / 255f).Within(1e-3));
            Assert.That(v.z, Is.EqualTo(0f).Within(1e-3)); Assert.That(v.w, Is.EqualTo(1f).Within(1e-3));
        });
        var c = Guard(() => Framework_ColorFromNormalized(new RVector4 { x = 1, y = 0, z = 0, w = 1 }));
        Assert.That((c.r, c.g, c.b, c.a), Is.EqualTo(((byte)255, (byte)0, (byte)0, (byte)255)));
    }

    [Test] public void HSV_roundtrip()
    {
        var hsv = Guard(() => Framework_ColorToHSV(255, 0, 0, 255)); // red: hue 0 deg, sat 1, val 1
        Assert.Multiple(() =>
        {
            Assert.That(hsv.x, Is.EqualTo(0f).Within(1e-2)); // hue in DEGREES [0..360]
            Assert.That(hsv.y, Is.EqualTo(1f).Within(1e-2));
            Assert.That(hsv.z, Is.EqualTo(1f).Within(1e-2));
        });
        var c = Guard(() => Framework_ColorFromHSV(0f, 1f, 1f));
        Assert.That((c.r, c.g, c.b), Is.EqualTo(((byte)255, (byte)0, (byte)0)));
    }

    [Test] public void Fade_and_ColorAlpha_set_alpha()
    {
        Assert.That(Guard(() => Framework_Fade(255, 255, 255, 255, 0.5f)).a, Is.EqualTo(127));
        Assert.That(Guard(() => Framework_ColorAlpha(255, 0, 0, 255, 0.5f)).a, Is.EqualTo(127));
    }

    [Test] public void Tint_Lerp_Brightness_Contrast()
    {
        Assert.That(Guard(() => Framework_ColorTint(255, 255, 255, 255, 128, 128, 128, 255)).r, Is.EqualTo(128));
        Assert.That(Guard(() => Framework_ColorLerp(0, 0, 0, 255, 255, 255, 255, 255, 0.5f)).r, Is.InRange(126, 129));
        Assert.That(Guard(() => Framework_ColorBrightness(100, 100, 100, 255, 0f)).r, Is.EqualTo(100)); // factor 0 = unchanged
        Assert.That(Guard(() => Framework_ColorContrast(128, 128, 128, 255, 0f)).r, Is.EqualTo(128));   // contrast 0 = unchanged
    }

    [Test] public void AlphaBlend_opaque_src_stays_opaque()
        => Assert.That(Guard(() => Framework_ColorAlphaBlend(0, 0, 0, 255, 255, 0, 0, 255, 255, 255, 255, 255)).a, Is.EqualTo(255));

    [Test] public void ColorIsEqual_true_and_false()
    {
        Assert.That(Guard(() => Framework_ColorIsEqual(255, 0, 0, 255, 255, 0, 0, 255)), Is.True);
        Assert.That(Guard(() => Framework_ColorIsEqual(255, 0, 0, 255, 255, 0, 0, 254)), Is.False);
    }

    [Test] public void GetPixelDataSize_rgba() => Assert.That(Guard(() => Framework_GetPixelDataSize(4, 4, PIXELFORMAT_UNCOMPRESSED_R8G8B8A8)), Is.EqualTo(64));

    [Test] public void SetPixelColor_then_GetPixelColor_roundtrip_via_voidptr()
    {
        var p = Marshal.AllocHGlobal(4);
        try
        {
            Framework_SetPixelColor(p, 10, 20, 30, 40, PIXELFORMAT_UNCOMPRESSED_R8G8B8A8);
            var c = Framework_GetPixelColor(p, PIXELFORMAT_UNCOMPRESSED_R8G8B8A8);
            Assert.That((c.r, c.g, c.b, c.a), Is.EqualTo(((byte)10, (byte)20, (byte)30, (byte)40)));
        }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged."); }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates exports."); }
        finally { Marshal.FreeHGlobal(p); }
    }
}
