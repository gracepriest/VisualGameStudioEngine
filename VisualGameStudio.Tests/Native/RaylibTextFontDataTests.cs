using System;
using System.IO;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Runtime oracles for the rtext font-data sub-batch. LoadFontData/GenImageFontAtlas/UnloadFontData are all CPU (stb_truetype
/// rasterization + rectangle packing — no GL), but the meaningful path needs real font-file bytes, so it is split:
///   • Headless (fast subset): UnloadFontData(IntPtr.Zero, 0) — glyphCount 0 skips the per-glyph UnloadImage loop and
///     RL_FREE(NULL) is a no-op, so a null/empty array unloads safely; plus GlyphInfo is 40 bytes (anchors the PtrToStructure
///     read below: 4 int + embedded Image 24 B).
///   • [Category("Integration")]: the real pipeline against a system TrueType font (self-skips if absent). LoadFontData with
///     codepoints=null/count=0 defaults to the 95 ASCII glyphs from codepoint 32 (space); GenImageFontAtlas packs them into a
///     CPU atlas Image and writes the packed Rectangle* out through glyphRecs. This pins the whole binding end-to-end: the
///     Byte()/Integer() inputs, the raylib-owned GlyphInfo* return (read back: glyph 0 == space, sane advance), the by-value
///     Image return, and the ByRef out-pointer. Ownership: glyphRecs is caller-owned -> Framework_MemFree; the atlas ->
///     Framework_UnloadImage; the glyphs -> Framework_UnloadFontData (never MemFree'd — different owner).
/// Self-skips via Assert.Ignore when the DLL is absent or predates the exports.
/// </summary>
[TestFixture]
public class RaylibTextFontDataTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;
    private const int FONT_DEFAULT = 0;

    [StructLayout(LayoutKind.Sequential)] private struct Image { public IntPtr data; public int width, height, mipmaps, format; }
    [StructLayout(LayoutKind.Sequential)] private struct Rectangle { public float x, y, width, height; }
    [StructLayout(LayoutKind.Sequential)] private struct GlyphInfo { public int value, offsetX, offsetY, advanceX; public Image image; }

    [DllImport(DLL, CallingConvention = CC)] private static extern IntPtr Framework_LoadFontData(byte[] fileData, int dataSize, int fontSize, int[] codepoints, int codepointCount, int type);
    [DllImport(DLL, CallingConvention = CC)] private static extern Image Framework_GenImageFontAtlas(IntPtr glyphs, ref IntPtr glyphRecs, int glyphCount, int fontSize, int padding, int packMethod);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadFontData(IntPtr glyphs, int glyphCount);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadImage(Image image);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_MemFree(IntPtr ptr);

    [Test]
    public void GlyphInfo_struct_is_40_bytes_on_x64()
        => Assert.That(Marshal.SizeOf<GlyphInfo>(), Is.EqualTo(40), "GlyphInfo = 4 int (16) + embedded Image (24)");

    [Test]
    public void UnloadFontData_on_a_null_array_is_safe()
    {
        // glyphCount 0 -> the per-glyph UnloadImage loop never runs; RL_FREE(NULL) is a no-op.
        try { Framework_UnloadFontData(IntPtr.Zero, 0); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); return; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates the font-data exports; refresh IDE\\ first."); return; }
        Assert.Pass("UnloadFontData on a null/empty array did not crash");
    }

    [Test]
    [Category("Integration")]
    public void Font_data_pipeline_rasterizes_a_real_ttf()
    {
        // A system TrueType font gives real bytes without shipping an asset. CPU-only pipeline (no window needed).
        string ttf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
        if (!File.Exists(ttf)) { Assert.Ignore($"no system font at {ttf}"); return; }
        byte[] fontBytes = File.ReadAllBytes(ttf);

        const int GLYPH_COUNT = 95;   // LoadFontData defaults to 95 ASCII glyphs (codepoints 32..126) when codepoints==null
        IntPtr glyphs = IntPtr.Zero, recs = IntPtr.Zero;
        Image atlas = default;
        bool haveGlyphs = false, haveRecs = false, haveAtlas = false;

        try
        {
            try { glyphs = Framework_LoadFontData(fontBytes, fontBytes.Length, 32, null, 0, FONT_DEFAULT); }
            catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); return; }
            catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates the font-data exports; refresh IDE\\ first."); return; }
            haveGlyphs = glyphs != IntPtr.Zero;
            Assert.That(glyphs, Is.Not.EqualTo(IntPtr.Zero), "LoadFontData must return a glyph array for a real TTF");

            // Read back the first default glyph: codepoint 32 (space), with a non-negative advance — pins the GlyphInfo ABI
            // and that the raylib-owned array marshals through IntPtr correctly.
            var g0 = Marshal.PtrToStructure<GlyphInfo>(glyphs);
            Assert.That(g0.value, Is.EqualTo(32), "first default glyph is space (codepoint 32)");
            Assert.That(g0.advanceX, Is.GreaterThanOrEqualTo(0), "space glyph has a non-negative advance");

            atlas = Framework_GenImageFontAtlas(glyphs, ref recs, GLYPH_COUNT, 32, 4, 0);
            haveAtlas = atlas.data != IntPtr.Zero;
            haveRecs = recs != IntPtr.Zero;
            Assert.Multiple(() =>
            {
                Assert.That(atlas.width, Is.GreaterThan(0), "atlas image has a positive width");
                Assert.That(atlas.height, Is.GreaterThan(0), "atlas image has a positive height");
                Assert.That(recs, Is.Not.EqualTo(IntPtr.Zero), "GenImageFontAtlas writes the packed Rectangle* out through glyphRecs");
            });

            var r0 = Marshal.PtrToStructure<Rectangle>(recs);
            Assert.That(r0.width, Is.GreaterThanOrEqualTo(0f), "the first packed glyph rectangle is well-formed");
        }
        finally
        {
            if (haveRecs) Framework_MemFree(recs);           // glyphRecs is caller-owned (raylib RL_MALLOC'd)
            if (haveAtlas) Framework_UnloadImage(atlas);      // the CPU atlas image
            if (haveGlyphs) Framework_UnloadFontData(glyphs, GLYPH_COUNT);  // the glyph array (NOT MemFree'd — its own owner)
        }
    }
}
