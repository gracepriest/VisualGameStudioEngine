using System;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Runtime correctness for the ONE headless function of raylib textures Batch 3d:
/// <c>Image GenImageText(int width, int height, const char* text)</c>. It copies the text bytes into a
/// <c>PIXELFORMAT_UNCOMPRESSED_GRAYSCALE</c> buffer — no font, no GL context — so it runs headless via
/// P/Invoke (unlike the other 8 Batch 3d fns, which need a live window and are verified by the
/// <c>TestVbDLL --textures3d</c> smoke scene). The correctness-critical property is the by-VALUE
/// <see cref="RImage"/> return marshaling: a layout/packing slip surfaces as a corrupted width/height or
/// a wrong pixel format. Self-contained local [DllImport] with local struct mirror; self-skips when the
/// engine DLL isn't staged with these exports (stale DLL → EntryPointNotFound → refresh IDE\ first).
/// </summary>
[Category("Integration")]
[TestFixture]
public class RaylibGenImageTextTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;

    [StructLayout(LayoutKind.Sequential)] private struct RImage { public IntPtr data; public int width, height, mipmaps, format; }

    private const int PIXELFORMAT_UNCOMPRESSED_GRAYSCALE = 1;

    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern RImage Framework_GenImageText(int width, int height, string text);
    [DllImport(DLL, CallingConvention = CC)] private static extern int Framework_GetPixelDataSize(int width, int height, int format);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadImage(RImage image);

    private static T Guard<T>(Func<T> call)
    {
        try { return call(); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); throw; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates GenImageText Batch 3d export; refresh IDE\\ first."); throw; }
    }

    [Test]
    public void GenImageText_produces_a_grayscale_image_of_the_requested_dims()
    {
        var img = Guard(() => Framework_GenImageText(16, 16, "AB"));
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(img.width, Is.EqualTo(16), "requested width must round-trip by-value");
                Assert.That(img.height, Is.EqualTo(16), "requested height must round-trip by-value");
                Assert.That(img.mipmaps, Is.GreaterThanOrEqualTo(1), "a valid image has at least one mip level");
                Assert.That(img.data, Is.Not.EqualTo(IntPtr.Zero), "GenImageText must allocate a pixel buffer");
                Assert.That(img.format, Is.EqualTo(PIXELFORMAT_UNCOMPRESSED_GRAYSCALE), "GenImageText yields an 8-bit grayscale image");
            });
        }
        finally { Framework_UnloadImage(img); }
    }

    [Test]
    public void GetPixelDataSize_agrees_with_16x16_grayscale()
    {
        // 16*16*1 byte per grayscale pixel = 256 bytes; sanity-checks the format constant against the shipped 3a helper.
        Assert.That(Guard(() => Framework_GetPixelDataSize(16, 16, PIXELFORMAT_UNCOMPRESSED_GRAYSCALE)), Is.EqualTo(256));
    }
}
