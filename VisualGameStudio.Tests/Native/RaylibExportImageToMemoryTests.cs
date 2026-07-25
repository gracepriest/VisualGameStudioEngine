using System;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Runtime correctness for the file-I/O straggler <c>ExportImageToMemory</c> (+ its <c>MemFree</c> release
/// primitive). The function encodes an in-RAM <see cref="RImage"/> to a container format via stb_image_write —
/// no font, no GL context — so it runs headless via P/Invoke. The correctness-critical property is that the
/// returned <see cref="IntPtr"/> points at a REAL encoded stream (verified by container magic bytes), the
/// <c>ByRef fileSize</c> out-param round-trips a positive length, and the buffer frees cleanly through
/// <c>Framework_MemFree</c> (raylib's allocator; a mismatched free is UB). Self-contained local [DllImport]
/// with local struct mirror; self-skips when the engine DLL isn't staged with these exports (stale DLL →
/// EntryPointNotFound → refresh IDE\ first, and confirm Passed not Skipped).
/// </summary>
[Category("Integration")]
[TestFixture]
public class RaylibExportImageToMemoryTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;

    [StructLayout(LayoutKind.Sequential)] private struct RImage { public IntPtr data; public int width, height, mipmaps, format; }

    [DllImport(DLL, CallingConvention = CC)] private static extern RImage Framework_GenImageColor(int width, int height, byte r, byte g, byte b, byte a);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern IntPtr Framework_ExportImageToMemory(RImage image, string fileType, ref int fileSize);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_MemFree(IntPtr ptr);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadImage(RImage image);

    private static T Guard<T>(Func<T> call)
    {
        try { return call(); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); throw; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates ExportImageToMemory/MemFree exports; refresh IDE\\ first."); throw; }
    }

    // PNG container magic — proves ExportImageToMemory produced a genuine encoded stream.
    private static readonly byte[] PngMagic = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static byte[] ExportAndCopy(RImage img, string fileType, out int size)
    {
        int fileSize = 0;
        IntPtr p = Guard(() => Framework_ExportImageToMemory(img, fileType, ref fileSize));
        try
        {
            Assert.That(p, Is.Not.EqualTo(IntPtr.Zero), $"{fileType} export must return a non-null buffer");
            Assert.That(fileSize, Is.GreaterThan(0), $"{fileType} export must report a positive byte length");
            var bytes = new byte[fileSize];
            Marshal.Copy(p, bytes, 0, fileSize);
            size = fileSize;
            return bytes;
        }
        finally { Framework_MemFree(p); }
    }

    [Test]
    public void ExportImageToMemory_png_yields_a_real_png_stream()
    {
        var img = Guard(() => Framework_GenImageColor(32, 24, 200, 100, 50, 255));
        try
        {
            var bytes = ExportAndCopy(img, ".png", out var size);
            Assert.That(size, Is.GreaterThanOrEqualTo(PngMagic.Length));
            Assert.That(bytes[..PngMagic.Length], Is.EqualTo(PngMagic), "encoded bytes must open with the PNG signature");
        }
        finally { Framework_UnloadImage(img); }
    }

    [Test]
    public void ExportImageToMemory_is_PNG_only_and_returns_null_for_other_formats()
    {
        // raylib 5.5's ExportImageToMemory wires up ONLY the stb PNG to-memory writer; a non-PNG container
        // name returns NULL with a zero fileSize (the file-based ExportImage supports more formats, but the
        // in-memory variant is PNG-only). This is faithful passthrough of raylib's intended behavior — NOT a
        // bug to fix (contrast the genuine TextInsert upstream bug we did correct). Documenting the contract
        // keeps a future maintainer from mistaking the null for a marshaling fault.
        var img = Guard(() => Framework_GenImageColor(32, 24, 10, 20, 30, 255));
        try
        {
            int fileSize = 123; // sentinel — must be overwritten to 0 by the engine
            IntPtr p = Guard(() => Framework_ExportImageToMemory(img, ".bmp", ref fileSize));
            Assert.Multiple(() =>
            {
                Assert.That(p, Is.EqualTo(IntPtr.Zero), "non-PNG memory export is unsupported by raylib → null buffer");
                Assert.That(fileSize, Is.EqualTo(0), "an unsupported format must report zero length");
            });
            // Nothing was allocated, so there is nothing to Framework_MemFree.
        }
        finally { Framework_UnloadImage(img); }
    }
}
