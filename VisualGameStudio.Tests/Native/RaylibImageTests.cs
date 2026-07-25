using System;
using System.IO;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Runtime correctness for the raylib textures Batch 3b Image-in-RAM bindings (load/generate/query) —
/// all GL-free, so fully unit-testable via P/Invoke. Self-contained local [DllImport] with local struct
/// mirrors; self-skips when the engine DLL isn't staged with these exports. The FIRST test
/// (<see cref="GenImageColor_and_LoadImageColors_prove_Image_byvalue_and_callerbuffer"/>) is the
/// go/no-go proof that a 24-byte Image struct marshals correctly as a by-value RETURN and that the
/// caller-buffer LoadImageColors pixel path works end-to-end — if it fails, surface it loudly before
/// trusting anything else in this file.
/// </summary>
[Category("Integration")]
[TestFixture]
public class RaylibImageTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;

    [StructLayout(LayoutKind.Sequential)] private struct RColor { public byte r, g, b, a; }
    [StructLayout(LayoutKind.Sequential)] private struct RImage { public IntPtr data; public int width, height, mipmaps, format; }
    [StructLayout(LayoutKind.Sequential)] private struct RRect { public float x, y, width, height; }

    private const int PIXELFORMAT_UNCOMPRESSED_R8G8B8A8 = 7;

    // ---- Load / export to disk ----
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern RImage Framework_LoadImageRaw(string fileName, int width, int height, int format, int headerSize);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern RImage Framework_LoadImageAnim(string fileName, ref int frames);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern RImage Framework_LoadImageAnimFromMemory(string fileType, byte[] fileData, int dataSize, ref int frames);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern RImage Framework_LoadImageFromMemory(string fileType, byte[] fileData, int dataSize);
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsImageValid(RImage image);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_ExportImage(RImage image, string fileName);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_ExportImageAsCode(RImage image, string fileName);

    // ---- Generation ----
    [DllImport(DLL, CallingConvention = CC)] private static extern RImage Framework_GenImageColor(int width, int height, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern RImage Framework_GenImageGradientLinear(int width, int height, int direction, byte startR, byte startG, byte startB, byte startA, byte endR, byte endG, byte endB, byte endA);
    [DllImport(DLL, CallingConvention = CC)] private static extern RImage Framework_GenImageGradientRadial(int width, int height, float density, byte innerR, byte innerG, byte innerB, byte innerA, byte outerR, byte outerG, byte outerB, byte outerA);
    [DllImport(DLL, CallingConvention = CC)] private static extern RImage Framework_GenImageGradientSquare(int width, int height, float density, byte innerR, byte innerG, byte innerB, byte innerA, byte outerR, byte outerG, byte outerB, byte outerA);
    [DllImport(DLL, CallingConvention = CC)] private static extern RImage Framework_GenImageChecked(int width, int height, int checksX, int checksY, byte col1R, byte col1G, byte col1B, byte col1A, byte col2R, byte col2G, byte col2B, byte col2A);
    [DllImport(DLL, CallingConvention = CC)] private static extern RImage Framework_GenImageWhiteNoise(int width, int height, float factor);
    [DllImport(DLL, CallingConvention = CC)] private static extern RImage Framework_GenImagePerlinNoise(int width, int height, int offsetX, int offsetY, float scale);
    [DllImport(DLL, CallingConvention = CC)] private static extern RImage Framework_GenImageCellular(int width, int height, int tileSize);

    // ---- Non-mutating manipulation / query / palette ----
    [DllImport(DLL, CallingConvention = CC)] private static extern RImage Framework_ImageCopy(RImage image);
    [DllImport(DLL, CallingConvention = CC)] private static extern RImage Framework_ImageFromImage(RImage image, RRect rec);
    [DllImport(DLL, CallingConvention = CC)] private static extern RImage Framework_ImageFromChannel(RImage image, int selectedChannel);
    [DllImport(DLL, CallingConvention = CC)] private static extern RRect Framework_GetImageAlphaBorder(RImage image, float threshold);
    [DllImport(DLL, CallingConvention = CC)] private static extern RColor Framework_GetImageColor(RImage image, int x, int y);
    [DllImport(DLL, CallingConvention = CC)] private static extern int Framework_LoadImageColors(RImage image, [In, Out] RColor[] outColors);
    [DllImport(DLL, CallingConvention = CC)] private static extern int Framework_LoadImagePalette(RImage image, int maxPaletteSize, [In, Out] RColor[] outColors);

    // ---- Lifecycle ----
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadImage(RImage image);

    private static T Guard<T>(Func<T> call)
    {
        try { return call(); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); throw; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates Image Batch 3b exports; refresh IDE\\ first."); throw; }
    }

    private static void AssertColor(RColor c, byte r, byte g, byte b, byte a)
        => Assert.That((c.r, c.g, c.b, c.a), Is.EqualTo(((byte)r, (byte)g, (byte)b, (byte)a)));

    [Test] public void GenImageColor_and_LoadImageColors_prove_Image_byvalue_and_callerbuffer()
    {
        // GO/NO-GO: a 24-byte Image returned BY VALUE must marshal field-for-field, and the
        // caller-buffer LoadImageColors pixel path must work end-to-end.
        var img = Guard(() => Framework_GenImageColor(3, 2, 10, 20, 30, 255));
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(Framework_IsImageValid(img), Is.True);
                Assert.That(img.width, Is.EqualTo(3));
                Assert.That(img.height, Is.EqualTo(2));
            });

            var buf = new RColor[6];
            var n = Framework_LoadImageColors(img, buf);
            Assert.That(n, Is.EqualTo(6));
            Assert.Multiple(() =>
            {
                foreach (var c in buf) AssertColor(c, 10, 20, 30, 255);
            });
        }
        finally { Framework_UnloadImage(img); }
    }

    [Test] public void GetImageColor_reads_the_expected_pixel()
    {
        var img = Guard(() => Framework_GenImageColor(3, 2, 10, 20, 30, 255));
        try { AssertColor(Framework_GetImageColor(img, 1, 1), 10, 20, 30, 255); }
        finally { Framework_UnloadImage(img); }
    }

    [Test] public void WhiteNoise_extremes_are_deterministic()
    {
        var black = Guard(() => Framework_GenImageWhiteNoise(4, 4, 0f));
        try { AssertColor(Framework_GetImageColor(black, 0, 0), 0, 0, 0, 255); }
        finally { Framework_UnloadImage(black); }

        var white = Framework_GenImageWhiteNoise(4, 4, 1f);
        try { AssertColor(Framework_GetImageColor(white, 0, 0), 255, 255, 255, 255); }
        finally { Framework_UnloadImage(white); }
    }

    [Test] public void GenImageChecked_alternates_between_the_two_colors()
    {
        var img = Guard(() => Framework_GenImageChecked(2, 2, 1, 1, 255, 0, 0, 255, 0, 0, 255, 255));
        try
        {
            AssertColor(Framework_GetImageColor(img, 0, 0), 255, 0, 0, 255);
            AssertColor(Framework_GetImageColor(img, 1, 0), 0, 0, 255, 255);
        }
        finally { Framework_UnloadImage(img); }
    }

    [Test] public void ImageCopy_ImageFromImage_ImageFromChannel()
    {
        var img = Guard(() => Framework_GenImageColor(3, 2, 10, 20, 30, 255));
        RImage copy = default, cropped = default, channel = default;
        try
        {
            copy = Framework_ImageCopy(img);
            Assert.Multiple(() =>
            {
                Assert.That(Framework_IsImageValid(copy), Is.True);
                Assert.That((copy.width, copy.height), Is.EqualTo((img.width, img.height)));
            });
            AssertColor(Framework_GetImageColor(copy, 1, 1), 10, 20, 30, 255);

            cropped = Framework_ImageFromImage(img, new RRect { x = 0, y = 0, width = 2, height = 1 });
            Assert.Multiple(() =>
            {
                Assert.That(cropped.width, Is.EqualTo(2));
                Assert.That(cropped.height, Is.EqualTo(1));
            });

            channel = Framework_ImageFromChannel(img, 0);
            Assert.Multiple(() =>
            {
                Assert.That(Framework_IsImageValid(channel), Is.True);
                Assert.That((channel.width, channel.height), Is.EqualTo((img.width, img.height)));
            });
        }
        finally
        {
            Framework_UnloadImage(img);
            Framework_UnloadImage(copy);
            Framework_UnloadImage(cropped);
            Framework_UnloadImage(channel);
        }
    }

    [Test] public void GetImageAlphaBorder_on_fully_opaque_image_returns_full_rect()
    {
        var img = Guard(() => Framework_GenImageColor(4, 4, 9, 9, 9, 255));
        try
        {
            var rect = Framework_GetImageAlphaBorder(img, 0f);
            Assert.That((rect.x, rect.y, rect.width, rect.height), Is.EqualTo((0f, 0f, 4f, 4f)));
        }
        finally { Framework_UnloadImage(img); }
    }

    [Test] public void LoadImagePalette_single_color_image_returns_one_entry()
    {
        var img = Guard(() => Framework_GenImageColor(4, 4, 7, 8, 9, 255));
        try
        {
            var buf16 = new RColor[16];
            var count = Framework_LoadImagePalette(img, 16, buf16);
            Assert.That(count, Is.EqualTo(1));
            AssertColor(buf16[0], 7, 8, 9, 255);
        }
        finally { Framework_UnloadImage(img); }
    }

    [Test] public void IsImageValid_false_for_zeroed_image()
        => Assert.That(Guard(() => Framework_IsImageValid(default)), Is.False);

    [Test] public void Gradient_and_noise_generators_are_plausible()
    {
        var linear = Guard(() => Framework_GenImageGradientLinear(4, 4, 0, 255, 0, 0, 255, 0, 0, 255, 255));
        try
        {
            Assert.That(Framework_IsImageValid(linear), Is.True);
            Assert.That((linear.width, linear.height), Is.EqualTo((4, 4)));
        }
        finally { Framework_UnloadImage(linear); }

        var radial = Framework_GenImageGradientRadial(4, 4, 0.5f, 255, 0, 0, 255, 0, 0, 255, 255);
        try
        {
            Assert.That(Framework_IsImageValid(radial), Is.True);
            Assert.That((radial.width, radial.height), Is.EqualTo((4, 4)));
        }
        finally { Framework_UnloadImage(radial); }

        var square = Framework_GenImageGradientSquare(4, 4, 0.5f, 255, 0, 0, 255, 0, 0, 255, 255);
        try
        {
            Assert.That(Framework_IsImageValid(square), Is.True);
            Assert.That((square.width, square.height), Is.EqualTo((4, 4)));
        }
        finally { Framework_UnloadImage(square); }

        var perlin = Framework_GenImagePerlinNoise(4, 4, 0, 0, 1f);
        try
        {
            Assert.That(Framework_IsImageValid(perlin), Is.True);
            Assert.That((perlin.width, perlin.height), Is.EqualTo((4, 4)));
        }
        finally { Framework_UnloadImage(perlin); }

        var cellular = Framework_GenImageCellular(8, 8, 4);
        try
        {
            Assert.That(Framework_IsImageValid(cellular), Is.True);
            Assert.That((cellular.width, cellular.height), Is.EqualTo((8, 8)));
        }
        finally { Framework_UnloadImage(cellular); }
    }

    [Test] public void File_roundtrip_ExportImage_LoadImageFromMemory_LoadImageAnim_LoadImageAnimFromMemory()
    {
        var tmpPng = Path.Combine(Path.GetTempPath(), "vgs3b_" + Guid.NewGuid().ToString("N") + ".png");
        RImage img2 = default, fromMem = default, anim = default, animM = default;
        try
        {
            img2 = Guard(() => Framework_GenImageColor(2, 2, 50, 60, 70, 255));
            Assert.That(Framework_ExportImage(img2, tmpPng), Is.True);

            var bytes = File.ReadAllBytes(tmpPng);

            fromMem = Framework_LoadImageFromMemory(".png", bytes, bytes.Length);
            Assert.That(Framework_IsImageValid(fromMem), Is.True);
            Assert.That((fromMem.width, fromMem.height), Is.EqualTo((2, 2)));
            AssertColor(Framework_GetImageColor(fromMem, 0, 0), 50, 60, 70, 255);

            int frames = 0;
            anim = Framework_LoadImageAnim(tmpPng, ref frames);
            Assert.That(frames, Is.EqualTo(1));
            Assert.That(Framework_IsImageValid(anim), Is.True);

            int framesM = 0;
            animM = Framework_LoadImageAnimFromMemory(".png", bytes, bytes.Length, ref framesM);
            Assert.That(framesM, Is.EqualTo(1));
            Assert.That(Framework_IsImageValid(animM), Is.True);
        }
        finally
        {
            Framework_UnloadImage(img2);
            Framework_UnloadImage(fromMem);
            Framework_UnloadImage(anim);
            Framework_UnloadImage(animM);
            if (File.Exists(tmpPng)) File.Delete(tmpPng);
        }
    }

    [Test] public void LoadImageRaw_reads_raw_pixel_bytes()
    {
        var tmpRaw = Path.Combine(Path.GetTempPath(), "vgs3b_" + Guid.NewGuid().ToString("N") + ".raw");
        RImage img = default;
        try
        {
            // 2x2 image, every pixel {50,60,70,255} = 4 pixels * 4 bytes = 16 bytes.
            var pixel = new byte[] { 50, 60, 70, 255 };
            var raw = new byte[16];
            for (var i = 0; i < 4; i++) Array.Copy(pixel, 0, raw, i * 4, 4);
            File.WriteAllBytes(tmpRaw, raw);

            img = Guard(() => Framework_LoadImageRaw(tmpRaw, 2, 2, PIXELFORMAT_UNCOMPRESSED_R8G8B8A8, 0));
            Assert.That(Framework_IsImageValid(img), Is.True);
            AssertColor(Framework_GetImageColor(img, 0, 0), 50, 60, 70, 255);
        }
        finally
        {
            Framework_UnloadImage(img);
            if (File.Exists(tmpRaw)) File.Delete(tmpRaw);
        }
    }

    [Test] public void ExportImageAsCode_writes_a_file()
    {
        var tmpH = Path.Combine(Path.GetTempPath(), "vgs3b_" + Guid.NewGuid().ToString("N") + ".h");
        var img = Guard(() => Framework_GenImageColor(2, 2, 50, 60, 70, 255));
        try
        {
            Assert.That(Framework_ExportImageAsCode(img, tmpH), Is.True);
            Assert.That(File.Exists(tmpH), Is.True);
        }
        finally
        {
            Framework_UnloadImage(img);
            if (File.Exists(tmpH)) File.Delete(tmpH);
        }
    }
}
