using System;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Runtime correctness for the WINDOW half of rcore Batch core-C9 (dropped files). Dropped-file state is a window concept,
/// so this fixture is [Category("Integration")] + [NonParallelizable] (it owns the single global raylib window). With no
/// real drag-and-drop possible in a headless test the values are trivially empty, so this is an ABI-smoke: it proves
/// IsFileDropped and the FilePathList by-value round-trip (LoadDroppedFiles -> UnloadDroppedFiles) marshal without a stack/
/// struct mismatch under a live window. Self-skips via Assert.Ignore when no display/GL context exists. The real-oracle
/// directory-listing half lives in RaylibCoreC9DirectoryTests.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class RaylibCoreC9DroppedFilesTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;
    private const uint FLAG_WINDOW_HIDDEN = 0x00000080;

    [StructLayout(LayoutKind.Sequential)]
    private struct FilePathList { public uint capacity; public uint count; public IntPtr paths; }

    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern void Framework_InitWindow(int width, int height, string title);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_CloseWindow();
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsWindowReady();
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetWindowState(uint flags);
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsFileDropped();
    [DllImport(DLL, CallingConvention = CC)] private static extern FilePathList Framework_LoadDroppedFiles();
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadDroppedFiles(FilePathList files);

    [Test]
    public void Dropped_file_queries_marshal_under_a_window()
    {
        try { Framework_InitWindow(320, 240, "vgs_c9_drop_test"); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); return; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates rcore Batch core-C9 exports; refresh IDE\\ first."); return; }

        if (!Framework_IsWindowReady())
        {
            try { Framework_CloseWindow(); } catch { /* nothing to tear down */ }
            Assert.Ignore("No GL window could be created in this environment (headless).");
            return;
        }

        try
        {
            Framework_SetWindowState(FLAG_WINDOW_HIDDEN);
            Assert.Multiple(() =>
            {
                // No drag-and-drop happened in an automated test.
                Assert.That(Framework_IsFileDropped(), Is.False, "no file dropped in a headless test run");

                // The FilePathList by-value return + pass-back to Unload marshals cleanly (an ABI mismatch would corrupt
                // the stack → AccessViolation). With no drop the list is empty.
                Assert.DoesNotThrow(() =>
                {
                    FilePathList list = Framework_LoadDroppedFiles();
                    Assert.That(list.count, Is.EqualTo(0u), "no dropped files → empty FilePathList");
                    Framework_UnloadDroppedFiles(list);
                }, "LoadDroppedFiles / UnloadDroppedFiles round-trip the FilePathList by value under a window");
            });
        }
        finally
        {
            Framework_CloseWindow();
        }
    }
}
