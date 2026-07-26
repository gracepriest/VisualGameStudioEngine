using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Runtime correctness for rcore Batch core-C7 (raw raylib file-data I/O). Plain filesystem I/O — no window, no GL, no
/// device — so this fixture runs FULLY HEADLESS (no [Category("Integration")]) in the fast subset, against real temp
/// files it creates and deletes. The correctness-critical properties are the heap-buffer ownership contract (LoadFile*
/// hands back a raylib-malloc'd buffer released by the MATCHING Framework_UnloadFile*, never the caller's allocator)
/// and exact round-trips, cross-checked with .NET's own File API:
///
///  • SaveFileData -> LoadFileData round-trips arbitrary bytes (length via ByRef int; buffer freed by UnloadFileData).
///  • SaveFileText -> LoadFileText round-trips ASCII text (IntPtr return -> PtrToStringAnsi; freed by UnloadFileText).
///  • ExportDataAsCode writes a real C header (static unsigned char array) for the given bytes.
///
/// Self-contained local [DllImport]; self-skips (whole fixture) when the engine DLL isn't staged with the C7 exports.
/// Text uses no embedded newline so the round-trip is immune to any text-mode CRLF translation.
/// </summary>
[TestFixture]
public class RaylibFileDataIoTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;

    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern IntPtr Framework_LoadFileData(string fileName, ref int dataSize);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadFileData(IntPtr data);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_SaveFileData(string fileName, byte[] data, int dataSize);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_ExportDataAsCode(byte[] data, int dataSize, string fileName);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern IntPtr Framework_LoadFileText(string fileName);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadFileText(IntPtr text);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_SaveFileText(string fileName, string text);

    [OneTimeSetUp]
    public void ProbeEngine()
    {
        try { Framework_LoadFileText(Path.Combine(Path.GetTempPath(), "__vgs_c7_probe_nonexistent__")); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates rcore Batch core-C7 exports; refresh IDE\\ first."); }
    }

    private static string TempPath(string ext) =>
        Path.Combine(Path.GetTempPath(), "vgs_c7_" + Guid.NewGuid().ToString("N") + ext);

    [Test]
    public void SaveFileData_then_LoadFileData_round_trips_arbitrary_bytes()
    {
        var original = new byte[512];
        for (int i = 0; i < original.Length; i++) original[i] = (byte)(i & 0xFF);
        string path = TempPath(".bin");
        IntPtr loaded = IntPtr.Zero;
        try
        {
            Assert.That(Framework_SaveFileData(path, original, original.Length), Is.True, "SaveFileData reports success");
            Assert.That(File.Exists(path), Is.True, "the file exists on disk (.NET cross-check)");
            Assert.That(new FileInfo(path).Length, Is.EqualTo(original.Length), "on-disk length matches (.NET cross-check)");

            int size = 0;
            loaded = Framework_LoadFileData(path, ref size);
            Assert.That(loaded, Is.Not.EqualTo(IntPtr.Zero), "LoadFileData returns a non-null buffer");
            Assert.That(size, Is.EqualTo(original.Length), "LoadFileData reports the byte length via ByRef");
            var got = new byte[size];
            Marshal.Copy(loaded, got, 0, size);
            Assert.That(got, Is.EqualTo(original), "load(save(x)) == x");
        }
        finally
        {
            if (loaded != IntPtr.Zero) Framework_UnloadFileData(loaded); // raylib's own free, not Marshal.FreeHGlobal
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public void SaveFileText_then_LoadFileText_round_trips_ascii_text()
    {
        const string text = "Hello, raylib file text - no newline to dodge CRLF translation.";
        string path = TempPath(".txt");
        IntPtr loaded = IntPtr.Zero;
        try
        {
            Assert.That(Framework_SaveFileText(path, text), Is.True, "SaveFileText reports success");
            Assert.That(File.Exists(path), Is.True, "the file exists on disk (.NET cross-check)");
            Assert.That(File.ReadAllText(path), Is.EqualTo(text), "on-disk text matches (independent .NET witness; no newline => no CRLF issue)");

            loaded = Framework_LoadFileText(path);
            Assert.That(loaded, Is.Not.EqualTo(IntPtr.Zero), "LoadFileText returns a non-null buffer");
            string got = Marshal.PtrToStringAnsi(loaded);
            Assert.That(got, Is.EqualTo(text), "load(save(text)) == text");
        }
        finally
        {
            if (loaded != IntPtr.Zero) Framework_UnloadFileText(loaded);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public void ExportDataAsCode_writes_a_c_header_with_the_byte_array()
    {
        var data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        string path = TempPath(".h");
        try
        {
            Assert.That(Framework_ExportDataAsCode(data, data.Length, path), Is.True, "ExportDataAsCode reports success");
            Assert.That(File.Exists(path), Is.True, "the .h file exists on disk");
            string code = File.ReadAllText(path);
            Assert.Multiple(() =>
            {
                Assert.That(code, Does.Contain("static unsigned char"), "emits a C unsigned char array declaration");
                Assert.That(code, Does.Contain("_DATA_SIZE"), "emits raylib's size #define");
                // ⚠ raylib 5.5 formats bytes with %x (e.g. 0xde), NOT %02x (a leading byte 0x01 prints as "0x1").
                // Use bytes >= 0x10 so the two-hex-digit form is unambiguous.
                Assert.That(code, Does.Contain("0xde").IgnoreCase, "emits the data bytes as hex (0xde)");
                Assert.That(code, Does.Contain("0xef").IgnoreCase, "emits the data bytes as hex (0xef)");
            });
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
