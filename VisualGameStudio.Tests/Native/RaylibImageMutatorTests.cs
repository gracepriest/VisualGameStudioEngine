using System;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Runtime correctness for the raylib textures Batch 3c-i in-place Image mutators. Every mutator is a
/// <c>void ImageXxx(Image*, …)</c> passthrough, bound in VB as <c>ByRef img As Image</c> — so the ONE
/// correctness-critical property is ByRef write-back: a resize/crop/rotate must propagate the new
/// dimensions (and reallocated pixel buffer) to the managed caller. A by-value slip would silently
/// discard the mutation. All mutators are GL-free, so this runs headless via P/Invoke. Self-contained
/// local [DllImport] with local struct mirrors; self-skips when the engine DLL isn't staged with these
/// exports. The FIRST test (<see cref="ByRef_propagation_ImageResizeNN_updates_dims_and_pixel_count"/>)
/// is the go/no-go ByRef proof — if it fails, surface it loudly before trusting anything else here.
///
/// ⚠ LoadImageColors is a CALLER-BUFFER wrapper (Batch 3b contract): size the RColor[] to the CURRENT
/// img.width*img.height, read back AFTER the mutator (a resize changes the required size).
/// </summary>
[Category("Integration")]
[TestFixture]
public class RaylibImageMutatorTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;

    [StructLayout(LayoutKind.Sequential)] private struct RColor { public byte r, g, b, a; }
    [StructLayout(LayoutKind.Sequential)] private struct RImage { public IntPtr data; public int width, height, mipmaps, format; }
    [StructLayout(LayoutKind.Sequential)] private struct RRect { public float x, y, width, height; }

    private const int PIXELFORMAT_UNCOMPRESSED_GRAYSCALE = 1;
    private const int PIXELFORMAT_UNCOMPRESSED_R8G8B8A8 = 7;

    // ---- Reused Batch 3b entry points (build / read / free images) ----
    [DllImport(DLL, CallingConvention = CC)] private static extern RImage Framework_GenImageColor(int width, int height, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern RImage Framework_GenImageChecked(int width, int height, int checksX, int checksY, byte col1R, byte col1G, byte col1B, byte col1A, byte col2R, byte col2G, byte col2B, byte col2A);
    [DllImport(DLL, CallingConvention = CC)] private static extern int Framework_LoadImageColors(RImage image, [In, Out] RColor[] outColors);
    [DllImport(DLL, CallingConvention = CC)] private static extern RColor Framework_GetImageColor(RImage image, int x, int y);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadImage(RImage image);

    // ---- Batch 3c-i mutators — first arg ByRef (blittable → in-place write-back) ----
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageFormat(ref RImage img, int newFormat);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageToPOT(ref RImage img, byte fillR, byte fillG, byte fillB, byte fillA);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageCrop(ref RImage img, RRect crop);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageAlphaCrop(ref RImage img, float threshold);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageAlphaClear(ref RImage img, byte r, byte g, byte b, byte a, float threshold);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageAlphaMask(ref RImage img, RImage alphaMask);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageAlphaPremultiply(ref RImage img);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageBlurGaussian(ref RImage img, int blurSize);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageKernelConvolution(ref RImage img, float[] kernel, int kernelSize);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageResizeNN(ref RImage img, int newWidth, int newHeight);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageResizeCanvas(ref RImage img, int newWidth, int newHeight, int offsetX, int offsetY, byte fillR, byte fillG, byte fillB, byte fillA);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageMipmaps(ref RImage img);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageDither(ref RImage img, int rBpp, int gBpp, int bBpp, int aBpp);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageFlipHorizontal(ref RImage img);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageRotate(ref RImage img, int degrees);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageRotateCW(ref RImage img);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageRotateCCW(ref RImage img);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageColorTint(ref RImage img, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageColorGrayscale(ref RImage img);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageColorContrast(ref RImage img, float contrast);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageColorBrightness(ref RImage img, int brightness);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageColorReplace(ref RImage img, byte colorR, byte colorG, byte colorB, byte colorA, byte replaceR, byte replaceG, byte replaceB, byte replaceA);

    private static T Guard<T>(Func<T> call)
    {
        try { return call(); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); throw; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates Image mutator Batch 3c-i exports; refresh IDE\\ first."); throw; }
    }

    /// <summary>Read every pixel back as RGBA. Buffer sized to the CURRENT dims (post-mutator).</summary>
    private static RColor[] Colors(RImage img)
    {
        var buf = new RColor[img.width * img.height];
        var n = Framework_LoadImageColors(img, buf);
        Assert.That(n, Is.EqualTo(img.width * img.height), "LoadImageColors returned an unexpected pixel count");
        return buf;
    }

    // ---------------------------------------------------------------------------------------------
    // ByRef write-back (the go/no-go proof)
    // ---------------------------------------------------------------------------------------------

    [Test]
    public void ByRef_propagation_ImageResizeNN_updates_dims_and_pixel_count()
    {
        // GO/NO-GO: a resize reallocates the pixel buffer and changes width/height. Those MUST propagate
        // back to the managed struct (proves ByRef, not by-value). Then the caller-buffer LoadImageColors
        // must be sized to the NEW 6*4=24 pixels — read the dims back from the ref struct to size it.
        var img = Guard(() => Framework_GenImageColor(3, 2, 200, 0, 0, 255));
        try
        {
            Assert.That((img.width, img.height), Is.EqualTo((3, 2)));

            Framework_ImageResizeNN(ref img, 6, 4);
            Assert.Multiple(() =>
            {
                Assert.That(img.width, Is.EqualTo(6), "ImageResizeNN width did not propagate ByRef");
                Assert.That(img.height, Is.EqualTo(4), "ImageResizeNN height did not propagate ByRef");
            });

            var buf = new RColor[img.width * img.height]; // 24
            var n = Framework_LoadImageColors(img, buf);
            Assert.That(n, Is.EqualTo(24));
        }
        finally { Framework_UnloadImage(img); }
    }

    [Test]
    public void ImageCrop_shrinks_dims_ByRef()
    {
        var img = Guard(() => Framework_GenImageColor(4, 4, 10, 20, 30, 255));
        try
        {
            Framework_ImageCrop(ref img, new RRect { x = 0, y = 0, width = 2, height = 2 });
            Assert.Multiple(() =>
            {
                Assert.That(img.width, Is.EqualTo(2));
                Assert.That(img.height, Is.EqualTo(2));
            });
            // Buffer must be sized to the cropped 2*2=4, not the original 16.
            Assert.That(Colors(img), Has.Length.EqualTo(4));
        }
        finally { Framework_UnloadImage(img); }
    }

    // ---------------------------------------------------------------------------------------------
    // Color mutators
    // ---------------------------------------------------------------------------------------------

    [Test]
    public void ImageColorGrayscale_equalizes_channels()
    {
        var img = Guard(() => Framework_GenImageColor(2, 2, 255, 0, 0, 255));
        try
        {
            Framework_ImageColorGrayscale(ref img);
            Assert.Multiple(() =>
            {
                foreach (var c in Colors(img))
                    Assert.That((c.r, c.g), Is.EqualTo((c.b, c.b)), "grayscale pixel channels must be equal");
            });
        }
        finally { Framework_UnloadImage(img); }
    }

    [Test]
    public void ImageColorReplace_swaps_red_for_blue()
    {
        var img = Guard(() => Framework_GenImageColor(2, 2, 255, 0, 0, 255));
        try
        {
            Framework_ImageColorReplace(ref img, 255, 0, 0, 255, /*replace*/ 0, 0, 255, 255);
            Assert.Multiple(() =>
            {
                foreach (var c in Colors(img))
                    Assert.That((c.r, c.g, c.b, c.a), Is.EqualTo(((byte)0, (byte)0, (byte)255, (byte)255)));
            });
        }
        finally { Framework_UnloadImage(img); }
    }

    [Test]
    public void ImageColorTint_half_white_halves_channel()
    {
        var img = Guard(() => Framework_GenImageColor(2, 2, 255, 255, 255, 255));
        try
        {
            Framework_ImageColorTint(ref img, 128, 128, 128, 255);
            var c = Colors(img)[0];
            Assert.That((int)c.r, Is.EqualTo(128).Within(2), "white tinted by ~50% grey should be ~128");
        }
        finally { Framework_UnloadImage(img); }
    }

    [Test]
    public void ImageColorBrightness_brightens_and_Contrast_zero_is_noop()
    {
        var bright = Guard(() => Framework_GenImageColor(2, 2, 100, 100, 100, 255));
        try
        {
            var before = Framework_GetImageColor(bright, 0, 0).r;
            Framework_ImageColorBrightness(ref bright, 60);
            var after = Colors(bright)[0].r;
            Assert.That((int)after, Is.GreaterThan(before), "+60 brightness must raise the channel value");
        }
        finally { Framework_UnloadImage(bright); }

        var contrast = Framework_GenImageColor(2, 2, 100, 100, 100, 255);
        try
        {
            Framework_ImageColorContrast(ref contrast, 0f);
            Assert.That((int)Colors(contrast)[0].r, Is.EqualTo(100).Within(2), "contrast 0 must leave pixels unchanged");
        }
        finally { Framework_UnloadImage(contrast); }
    }

    // ---------------------------------------------------------------------------------------------
    // Geometric mutators
    // ---------------------------------------------------------------------------------------------

    [Test]
    public void ImageFlipHorizontal_swaps_the_two_pixels()
    {
        // 2x1: pixel(0,0)=col1=black, pixel(1,0)=col2=white (checksX=checksY=1).
        var img = Guard(() => Framework_GenImageChecked(2, 1, 1, 1, 0, 0, 0, 255, 255, 255, 255, 255));
        try
        {
            var before = Colors(img);
            Assert.That((before[0].r, before[1].r), Is.EqualTo(((byte)0, (byte)255)), "precondition: black then white");

            Framework_ImageFlipHorizontal(ref img);
            var after = Colors(img);
            Assert.That((after[0].r, after[1].r), Is.EqualTo(((byte)255, (byte)0)), "flip must swap the two columns");
        }
        finally { Framework_UnloadImage(img); }
    }

    [Test]
    public void ImageRotateCW_and_ImageRotate90_swap_dims()
    {
        var cw = Guard(() => Framework_GenImageColor(2, 1, 10, 20, 30, 255));
        try
        {
            Framework_ImageRotateCW(ref cw);
            Assert.Multiple(() =>
            {
                Assert.That(cw.width, Is.EqualTo(1));
                Assert.That(cw.height, Is.EqualTo(2));
            });
        }
        finally { Framework_UnloadImage(cw); }

        var rot = Framework_GenImageColor(2, 1, 10, 20, 30, 255);
        try
        {
            Framework_ImageRotate(ref rot, 90);
            Assert.Multiple(() =>
            {
                Assert.That(rot.width, Is.EqualTo(1));
                Assert.That(rot.height, Is.EqualTo(2));
            });
        }
        finally { Framework_UnloadImage(rot); }
    }

    [Test]
    public void ImageRotateCCW_swaps_dims()
    {
        var img = Guard(() => Framework_GenImageColor(2, 1, 10, 20, 30, 255));
        try
        {
            Framework_ImageRotateCCW(ref img);
            Assert.Multiple(() =>
            {
                Assert.That(img.width, Is.EqualTo(1));
                Assert.That(img.height, Is.EqualTo(2));
            });
        }
        finally { Framework_UnloadImage(img); }
    }

    // ---------------------------------------------------------------------------------------------
    // Format / structure mutators
    // ---------------------------------------------------------------------------------------------

    [Test]
    public void ImageFormat_to_grayscale_sets_format_and_keeps_pixel_count()
    {
        var img = Guard(() => Framework_GenImageColor(4, 4, 50, 60, 70, 255));
        try
        {
            Framework_ImageFormat(ref img, PIXELFORMAT_UNCOMPRESSED_GRAYSCALE);
            Assert.That(img.format, Is.EqualTo(PIXELFORMAT_UNCOMPRESSED_GRAYSCALE), "format must propagate ByRef");
            // LoadImageColors normalizes any format back to RGBA → still width*height entries.
            Assert.That(Colors(img), Has.Length.EqualTo(16));
        }
        finally { Framework_UnloadImage(img); }
    }

    [Test]
    public void ImageMipmaps_generates_extra_levels()
    {
        var img = Guard(() => Framework_GenImageColor(4, 4, 50, 60, 70, 255));
        try
        {
            Assert.That(img.mipmaps, Is.EqualTo(1), "precondition: single mip level");
            Framework_ImageMipmaps(ref img);
            Assert.That(img.mipmaps, Is.GreaterThan(1), "4x4 must gain mip levels (4x4→2x2→1x1)");
        }
        finally { Framework_UnloadImage(img); }
    }

    [Test]
    public void ImageToPOT_rounds_dims_up_to_power_of_two()
    {
        var img = Guard(() => Framework_GenImageColor(3, 3, 50, 60, 70, 255));
        try
        {
            Framework_ImageToPOT(ref img, 0, 0, 0, 255);
            Assert.Multiple(() =>
            {
                Assert.That(img.width, Is.EqualTo(4), "3 → next power of two = 4");
                Assert.That(img.height, Is.EqualTo(4));
                Assert.That(IsPowerOfTwo(img.width), Is.True);
                Assert.That(IsPowerOfTwo(img.height), Is.True);
            });
        }
        finally { Framework_UnloadImage(img); }
    }

    private static bool IsPowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;

    // ---------------------------------------------------------------------------------------------
    // Secondary-Image-by-value + array-marshaling paths
    // ---------------------------------------------------------------------------------------------

    [Test]
    public void ImageAlphaMask_applies_mask_grey_as_alpha()
    {
        // Proves the by-VALUE secondary Image argument path. Mask grey ~128 → image alpha ~128.
        var img = Guard(() => Framework_GenImageColor(4, 4, 200, 200, 200, 255));
        var mask = Framework_GenImageColor(4, 4, 128, 128, 128, 255);
        try
        {
            Framework_ImageAlphaMask(ref img, mask);
            var a = Colors(img)[0].a;
            Assert.That((int)a, Is.EqualTo(128).Within(4), "masked alpha must follow the mask's grey value");
        }
        finally
        {
            Framework_UnloadImage(img);
            Framework_UnloadImage(mask);
        }
    }

    [Test]
    public void ImageKernelConvolution_identity_leaves_solid_fill_unchanged()
    {
        // Solid fill + identity 3x3 kernel → no edge/rounding drift. Proves the Single()[] marshals.
        var img = Guard(() => Framework_GenImageColor(4, 4, 100, 100, 100, 255));
        try
        {
            var identity = new float[] { 0, 0, 0, 0, 1, 0, 0, 0, 0 };
            Framework_ImageKernelConvolution(ref img, identity, identity.Length);
            Assert.Multiple(() =>
            {
                foreach (var c in Colors(img))
                    Assert.That((int)c.r, Is.EqualTo(100).Within(3), "identity kernel must preserve a solid fill");
            });
        }
        finally { Framework_UnloadImage(img); }
    }

    // ---------------------------------------------------------------------------------------------
    // Remaining mutators: run without crashing, leave plausible state
    // ---------------------------------------------------------------------------------------------

    [Test]
    public void ImageBlurGaussian_runs_and_preserves_dims()
    {
        var img = Guard(() => Framework_GenImageColor(4, 4, 120, 120, 120, 255));
        try
        {
            Framework_ImageBlurGaussian(ref img, 2);
            Assert.That((img.width, img.height), Is.EqualTo((4, 4)));
            Assert.That(Colors(img), Has.Length.EqualTo(16));
        }
        finally { Framework_UnloadImage(img); }
    }

    [Test]
    public void ImageDither_runs_and_preserves_dims()
    {
        var img = Guard(() => Framework_GenImageColor(4, 4, 120, 130, 140, 255));
        try
        {
            Framework_ImageDither(ref img, 5, 6, 5, 0);
            Assert.That((img.width, img.height), Is.EqualTo((4, 4)));
            // Dither repacks to R5G6B5; LoadImageColors still normalizes back to 16 RGBA entries.
            Assert.That(Colors(img), Has.Length.EqualTo(16));
        }
        finally { Framework_UnloadImage(img); }
    }

    [Test]
    public void ImageResizeCanvas_grows_dims_ByRef()
    {
        var img = Guard(() => Framework_GenImageColor(4, 4, 90, 90, 90, 255));
        try
        {
            Framework_ImageResizeCanvas(ref img, 6, 6, 1, 1, 0, 0, 0, 255);
            Assert.Multiple(() =>
            {
                Assert.That(img.width, Is.EqualTo(6));
                Assert.That(img.height, Is.EqualTo(6));
            });
            Assert.That(Colors(img), Has.Length.EqualTo(36));
        }
        finally { Framework_UnloadImage(img); }
    }

    [Test]
    public void ImageAlpha_crop_clear_premultiply_run()
    {
        var crop = Guard(() => Framework_GenImageColor(4, 4, 30, 40, 50, 255));
        try
        {
            // Fully opaque → threshold 0 crops nothing → dims unchanged, still valid.
            Framework_ImageAlphaCrop(ref crop, 0f);
            Assert.That((crop.width, crop.height), Is.EqualTo((4, 4)));
            Assert.That(Colors(crop), Has.Length.EqualTo(16));
        }
        finally { Framework_UnloadImage(crop); }

        var clear = Framework_GenImageColor(4, 4, 30, 40, 50, 255);
        try
        {
            Framework_ImageAlphaClear(ref clear, 0, 0, 0, 0, 0f);
            Assert.That(Colors(clear), Has.Length.EqualTo(16));
        }
        finally { Framework_UnloadImage(clear); }

        var premul = Framework_GenImageColor(4, 4, 200, 100, 50, 128);
        try
        {
            Framework_ImageAlphaPremultiply(ref premul);
            Assert.That(Colors(premul), Has.Length.EqualTo(16));
        }
        finally { Framework_UnloadImage(premul); }
    }
}
