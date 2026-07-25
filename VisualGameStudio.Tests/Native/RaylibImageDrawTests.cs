using System;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Runtime correctness for the raylib textures Batch 3c-ii Image software (CPU) drawing functions.
/// Every function is a <c>void ImageDrawXxx(Image*, …)</c> passthrough bound in VB as
/// <c>ByRef dst As Image</c>. These draws mutate the pixel buffer IN PLACE (no realloc), so — unlike the
/// 3c-i mutators — the proof is not ByRef dim write-back but the PIXEL STATE, read back via
/// <c>Framework_GetImageColor(dst, x, y)</c> (returns a Color by value). Everything is GL-free, so this
/// runs headless via P/Invoke. Self-contained local [DllImport] + struct mirrors; self-skips (Guard) when
/// the engine DLL isn't staged with these exports.
///
/// Load-bearing checks: <see cref="ImageDraw_blits_by_value_src"/> proves the by-VALUE <c>Image</c> src
/// blit; <see cref="ImageDrawPixelV_sets_pixel_by_value_vector"/> and
/// <see cref="ImageDrawRectangleRec_fills_by_value_rectangle"/> prove Vector2/Rectangle-by-value; the
/// TriangleFan/Strip tests prove the <c>Vector2[]</c> array marshaling.
///
/// ⚠ Rasterizer coverage of an arbitrary pixel isn't guaranteed for the triangle/fan/strip fills, so those
/// assert a small neighbourhood around the shape's geometric centre (via <see cref="AnyNear"/>), on a
/// TRANSPARENT background so "was drawn" == alpha != 0 (an opaque-black background has a==255 everywhere).
/// </summary>
[Category("Integration")]
[TestFixture]
public class RaylibImageDrawTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;

    [StructLayout(LayoutKind.Sequential)] private struct RColor { public byte r, g, b, a; }
    [StructLayout(LayoutKind.Sequential)] private struct RImage { public IntPtr data; public int width, height, mipmaps, format; }
    [StructLayout(LayoutKind.Sequential)] private struct RRect { public float x, y, width, height; }
    [StructLayout(LayoutKind.Sequential)] private struct RVector2 { public float x, y; }

    // ---- Reused Batch 3b entry points (build / read / free images) ----
    [DllImport(DLL, CallingConvention = CC)] private static extern RImage Framework_GenImageColor(int width, int height, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern RColor Framework_GetImageColor(RImage image, int x, int y);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadImage(RImage image);

    // ---- Batch 3c-ii software draws — first arg ByRef; Vector2/Rectangle by value; Color as u8 r,g,b,a ----
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageClearBackground(ref RImage dst, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageDrawPixel(ref RImage dst, int posX, int posY, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageDrawPixelV(ref RImage dst, RVector2 position, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageDrawLine(ref RImage dst, int startPosX, int startPosY, int endPosX, int endPosY, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageDrawLineV(ref RImage dst, RVector2 start, RVector2 end, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageDrawLineEx(ref RImage dst, RVector2 start, RVector2 end, int thick, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageDrawCircle(ref RImage dst, int centerX, int centerY, int radius, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageDrawCircleV(ref RImage dst, RVector2 center, int radius, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageDrawCircleLines(ref RImage dst, int centerX, int centerY, int radius, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageDrawCircleLinesV(ref RImage dst, RVector2 center, int radius, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageDrawRectangle(ref RImage dst, int posX, int posY, int width, int height, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageDrawRectangleV(ref RImage dst, RVector2 position, RVector2 size, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageDrawRectangleRec(ref RImage dst, RRect rec, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageDrawRectangleLines(ref RImage dst, RRect rec, int thick, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageDrawTriangle(ref RImage dst, RVector2 v1, RVector2 v2, RVector2 v3, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageDrawTriangleEx(ref RImage dst, RVector2 v1, RVector2 v2, RVector2 v3, byte c1R, byte c1G, byte c1B, byte c1A, byte c2R, byte c2G, byte c2B, byte c2A, byte c3R, byte c3G, byte c3B, byte c3A);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageDrawTriangleLines(ref RImage dst, RVector2 v1, RVector2 v2, RVector2 v3, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageDrawTriangleFan(ref RImage dst, RVector2[] points, int pointCount, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageDrawTriangleStrip(ref RImage dst, RVector2[] points, int pointCount, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ImageDraw(ref RImage dst, RImage src, RRect srcRec, RRect dstRec, byte r, byte g, byte b, byte a);

    private static T Guard<T>(Func<T> call)
    {
        try { return call(); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); throw; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates Image software-draw Batch 3c-ii exports; refresh IDE\\ first."); throw; }
    }

    private static RVector2 V(float x, float y) => new RVector2 { x = x, y = y };

    private static void AssertColor(RImage img, int x, int y, byte r, byte g, byte b, byte a, string because)
    {
        var c = Framework_GetImageColor(img, x, y);
        Assert.That((c.r, c.g, c.b, c.a), Is.EqualTo((r, g, b, a)), $"{because} @ ({x},{y})");
    }

    /// <summary>Scan a (2*radius+1)² neighbourhood around (cx,cy) for ANY pixel matching <paramref name="pred"/>.</summary>
    private static bool AnyNear(RImage img, int cx, int cy, int radius, Func<RColor, bool> pred)
    {
        for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if (x >= 0 && y >= 0 && x < img.width && y < img.height && pred(Framework_GetImageColor(img, x, y)))
                    return true;
            }
        return false;
    }

    private static bool IsWhite(RColor c) => c.r == 255 && c.g == 255 && c.b == 255 && c.a == 255;
    private static bool IsSet(RColor c) => c.a != 0; // vs a fully-transparent background

    // black opaque background; the deterministic axis-aligned draws are checked exactly against it.
    private static RImage Black(int w, int h) => Framework_GenImageColor(w, h, 0, 0, 0, 255);
    // transparent background; a drawn pixel becomes a!=0 regardless of its rasterised colour.
    private static RImage Blank(int w, int h) => Framework_GenImageColor(w, h, 0, 0, 0, 0);

    // ---------------------------------------------------------------------------------------------
    // Deterministic single-pixel fills (opaque-black background, exact colour asserts)
    // ---------------------------------------------------------------------------------------------

    [Test]
    public void ImageClearBackground_fills_every_pixel()
    {
        var img = Guard(() => Black(4, 4));
        try
        {
            Framework_ImageClearBackground(ref img, 255, 0, 0, 255);
            Assert.Multiple(() =>
            {
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                        AssertColor(img, x, y, 255, 0, 0, 255, "ClearBackground must paint all 16 pixels red");
            });
        }
        finally { Framework_UnloadImage(img); }
    }

    [Test]
    public void ImageDrawPixel_sets_target_and_leaves_neighbour()
    {
        var img = Guard(() => Black(4, 4));
        try
        {
            Framework_ImageDrawPixel(ref img, 1, 1, 0, 0, 255, 255);
            AssertColor(img, 1, 1, 0, 0, 255, 255, "drawn pixel must be blue");
            AssertColor(img, 0, 0, 0, 0, 0, 255, "untouched pixel must stay black");
        }
        finally { Framework_UnloadImage(img); }
    }

    [Test]
    public void ImageDrawPixelV_sets_pixel_by_value_vector()
    {
        var img = Guard(() => Black(4, 4));
        try
        {
            Framework_ImageDrawPixelV(ref img, V(2, 2), 0, 255, 0, 255);
            AssertColor(img, 2, 2, 0, 255, 0, 255, "DrawPixelV must set (2,2) green (proves Vector2 by value)");
        }
        finally { Framework_UnloadImage(img); }
    }

    [Test]
    public void ImageDrawRectangle_fills_region()
    {
        var img = Guard(() => Black(4, 4));
        try
        {
            Framework_ImageDrawRectangle(ref img, 0, 0, 2, 2, 255, 255, 255, 255);
            AssertColor(img, 0, 0, 255, 255, 255, 255, "rect corner must be white");
            AssertColor(img, 1, 1, 255, 255, 255, 255, "rect interior must be white");
            AssertColor(img, 3, 3, 0, 0, 0, 255, "pixel outside the 2x2 rect must stay black");
        }
        finally { Framework_UnloadImage(img); }
    }

    [Test]
    public void ImageDrawRectangleRec_fills_by_value_rectangle()
    {
        var img = Guard(() => Black(4, 4));
        try
        {
            Framework_ImageDrawRectangleRec(ref img, new RRect { x = 1, y = 1, width = 2, height = 2 }, 255, 255, 255, 255);
            AssertColor(img, 1, 1, 255, 255, 255, 255, "RectangleRec must fill (1,1) white (proves Rectangle by value)");
            AssertColor(img, 0, 0, 0, 0, 0, 255, "pixel outside the rec must stay black");
        }
        finally { Framework_UnloadImage(img); }
    }

    [Test]
    public void ImageDrawLine_paints_a_horizontal_run()
    {
        var img = Guard(() => Black(4, 4));
        try
        {
            // Horizontal line (0,0)->(3,0). Start + interior prove the segment rendered with the right
            // colour and coordinates. NOTE: raylib 5.5's ImageDrawLine is endpoint-EXCLUSIVE — it paints
            // (0,0),(1,0),(2,0) but NOT the (3,0) endpoint (faithful passthrough; every exact-pixel/colour
            // assert elsewhere in this fixture passes, so this is raylib's own convention, not a slip).
            Framework_ImageDrawLine(ref img, 0, 0, 3, 0, 255, 255, 255, 255);
            AssertColor(img, 0, 0, 255, 255, 255, 255, "line start must be white");
            AssertColor(img, 2, 0, 255, 255, 255, 255, "line interior must be white");
        }
        finally { Framework_UnloadImage(img); }
    }

    [Test]
    public void ImageDrawCircle_fills_its_centre()
    {
        var img = Guard(() => Black(8, 8));
        try
        {
            Framework_ImageDrawCircle(ref img, 4, 4, 3, 255, 255, 255, 255);
            AssertColor(img, 4, 4, 255, 255, 255, 255, "filled circle centre must be white");
        }
        finally { Framework_UnloadImage(img); }
    }

    // ---------------------------------------------------------------------------------------------
    // Load-bearing: by-value Image src blit
    // ---------------------------------------------------------------------------------------------

    [Test]
    public void ImageDraw_blits_by_value_src()
    {
        var src = Guard(() => Framework_GenImageColor(2, 2, 255, 0, 0, 255)); // solid red 2x2
        var dst = Black(4, 4);
        try
        {
            Framework_ImageDraw(ref dst, src,
                new RRect { x = 0, y = 0, width = 2, height = 2 },
                new RRect { x = 0, y = 0, width = 2, height = 2 },
                255, 255, 255, 255); // white tint = passthrough
            AssertColor(dst, 0, 0, 255, 0, 0, 255, "blitted src pixel must be red (proves by-value Image src)");
            AssertColor(dst, 1, 1, 255, 0, 0, 255, "blitted src pixel must be red");
            AssertColor(dst, 3, 3, 0, 0, 0, 255, "pixel outside the 2x2 blit must stay black");
        }
        finally
        {
            Framework_UnloadImage(dst);
            Framework_UnloadImage(src);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Triangle fills — rasteriser coverage: assert the geometric centre region on a transparent bg
    // ---------------------------------------------------------------------------------------------

    [Test]
    public void ImageDrawTriangle_fills_centroid()
    {
        var img = Guard(() => Blank(8, 8));
        try
        {
            // verts {1,1},{6,1},{3,6} → centroid ≈ (3.3, 2.7) ≈ pixel (3,3)
            Framework_ImageDrawTriangle(ref img, V(1, 1), V(6, 1), V(3, 6), 255, 255, 255, 255);
            Assert.That(AnyNear(img, 3, 3, 1, IsWhite), Is.True, "triangle centroid region must be filled white");
        }
        finally { Framework_UnloadImage(img); }
    }

    [Test]
    public void ImageDrawTriangleEx_fills_centroid_with_interpolated_colour()
    {
        var img = Guard(() => Blank(8, 8));
        try
        {
            // same verts, per-vertex red/green/blue → interior is a non-background (opaque) blend
            Framework_ImageDrawTriangleEx(ref img, V(1, 1), V(6, 1), V(3, 6),
                255, 0, 0, 255, /*v1 red*/
                0, 255, 0, 255, /*v2 green*/
                0, 0, 255, 255 /*v3 blue*/);
            Assert.That(AnyNear(img, 3, 3, 1, IsSet), Is.True, "TriangleEx centroid must be non-background (a != 0)");
        }
        finally { Framework_UnloadImage(img); }
    }

    [Test]
    public void ImageDrawTriangleFan_marshals_array_and_fills()
    {
        var img = Guard(() => Blank(8, 8));
        try
        {
            var pts = new[] { V(1, 1), V(6, 1), V(6, 6), V(1, 6) }; // convex quad
            Framework_ImageDrawTriangleFan(ref img, pts, 4, 255, 255, 255, 255);
            Assert.That(AnyNear(img, 3, 3, 1, IsWhite), Is.True, "TriangleFan quad centre must be filled (proves Vector2[] marshaling)");
        }
        finally { Framework_UnloadImage(img); }
    }

    [Test]
    public void ImageDrawTriangleStrip_marshals_array_and_fills()
    {
        var img = Guard(() => Blank(8, 8));
        try
        {
            var pts = new[] { V(1, 1), V(6, 1), V(1, 6), V(6, 6) }; // strip order over the same quad
            Framework_ImageDrawTriangleStrip(ref img, pts, 4, 255, 255, 255, 255);
            Assert.That(AnyNear(img, 3, 3, 1, IsWhite), Is.True, "TriangleStrip quad centre must be filled (proves Vector2[] marshaling)");
        }
        finally { Framework_UnloadImage(img); }
    }

    // ---------------------------------------------------------------------------------------------
    // Secondary Vector2/Rectangle overloads + outline draws: run + one concrete pixel each
    // ---------------------------------------------------------------------------------------------

    [Test]
    public void Vector_overloads_draw_pixels()
    {
        // DrawLineV — horizontal along y=0
        var lineV = Guard(() => Black(4, 4));
        try
        {
            Framework_ImageDrawLineV(ref lineV, V(0, 0), V(3, 0), 255, 255, 255, 255);
            AssertColor(lineV, 0, 0, 255, 255, 255, 255, "DrawLineV start must be white");
        }
        finally { Framework_UnloadImage(lineV); }

        // DrawLineEx — thick horizontal; rasterised band, scan near the midpoint
        var lineEx = Framework_GenImageColor(8, 8, 0, 0, 0, 255);
        try
        {
            Framework_ImageDrawLineEx(ref lineEx, V(0, 0), V(7, 0), 1, 255, 255, 255, 255);
            Assert.That(AnyNear(lineEx, 4, 0, 1, IsWhite), Is.True, "DrawLineEx must paint the line band");
        }
        finally { Framework_UnloadImage(lineEx); }

        // DrawCircleV — filled, centre exact
        var circV = Framework_GenImageColor(8, 8, 0, 0, 0, 255);
        try
        {
            Framework_ImageDrawCircleV(ref circV, V(4, 4), 3, 255, 255, 255, 255);
            AssertColor(circV, 4, 4, 255, 255, 255, 255, "DrawCircleV centre must be white");
        }
        finally { Framework_UnloadImage(circV); }

        // DrawRectangleV — filled 2x2 at origin
        var rectV = Framework_GenImageColor(4, 4, 0, 0, 0, 255);
        try
        {
            Framework_ImageDrawRectangleV(ref rectV, V(0, 0), V(2, 2), 255, 255, 255, 255);
            AssertColor(rectV, 0, 0, 255, 255, 255, 255, "DrawRectangleV corner must be white");
            AssertColor(rectV, 1, 1, 255, 255, 255, 255, "DrawRectangleV interior must be white");
        }
        finally { Framework_UnloadImage(rectV); }
    }

    [Test]
    public void Outline_draws_paint_their_edges()
    {
        // DrawCircleLines — outline; centre NOT filled, rim at (1,4)/(7,4) is
        var circLines = Guard(() => Black(8, 8));
        try
        {
            Framework_ImageDrawCircleLines(ref circLines, 4, 4, 3, 255, 255, 255, 255);
            Assert.That(AnyNear(circLines, 7, 4, 1, IsWhite) || AnyNear(circLines, 1, 4, 1, IsWhite), Is.True,
                "DrawCircleLines must paint the rim");
        }
        finally { Framework_UnloadImage(circLines); }

        // DrawCircleLinesV — outline via Vector2 centre
        var circLinesV = Framework_GenImageColor(8, 8, 0, 0, 0, 255);
        try
        {
            Framework_ImageDrawCircleLinesV(ref circLinesV, V(4, 4), 3, 255, 255, 255, 255);
            Assert.That(AnyNear(circLinesV, 7, 4, 1, IsWhite) || AnyNear(circLinesV, 1, 4, 1, IsWhite), Is.True,
                "DrawCircleLinesV must paint the rim");
        }
        finally { Framework_UnloadImage(circLinesV); }

        // DrawRectangleLines — border of the whole image; top-left corner is on the border
        var rectLines = Framework_GenImageColor(4, 4, 0, 0, 0, 255);
        try
        {
            Framework_ImageDrawRectangleLines(ref rectLines, new RRect { x = 0, y = 0, width = 4, height = 4 }, 1, 255, 255, 255, 255);
            Assert.That(AnyNear(rectLines, 0, 0, 1, IsWhite), Is.True, "DrawRectangleLines must paint the border");
        }
        finally { Framework_UnloadImage(rectLines); }

        // DrawTriangleLines — outline; top edge from (1,1) to (6,1) at y=1
        var triLines = Framework_GenImageColor(8, 8, 0, 0, 0, 255);
        try
        {
            Framework_ImageDrawTriangleLines(ref triLines, V(1, 1), V(6, 1), V(3, 6), 255, 255, 255, 255);
            Assert.That(AnyNear(triLines, 3, 1, 1, IsWhite), Is.True, "DrawTriangleLines must paint the top edge");
        }
        finally { Framework_UnloadImage(triLines); }
    }
}
