using System;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Runtime correctness for rcore Batch core-C1 (raw raylib window state &amp; control). Unlike the pure-data batches
/// (C5/C7/C8/C10) these need a real GL window, so this fixture is [Category("Integration")] (excluded from the fast
/// subset) and [NonParallelizable] (it owns the single global raylib window and must not run alongside other device
/// tests). It InitWindow's at a known size, hides the window immediately, round-trips the state flags through
/// Set/Clear/IsWindowState, reads the native handle, cross-checks the screen size via the already-bound getters, then
/// exercises the remaining void/bool setters purely to prove their P/Invoke signatures marshal without an ABI mismatch
/// (a wrong signature would corrupt the stack and raise an AccessViolation here). If no display/GL context can be
/// created (headless CI), the whole test self-skips via Assert.Ignore rather than failing.
///
/// SetWindowIcon(Image) / SetWindowIcons(Image*, int) are covered by the parity guard only — their by-value Image
/// marshaling is already exercised by the rtextures fixtures, and constructing a valid RGBA icon here adds no coverage.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class RaylibWindowStateControlTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;

    // raylib ConfigFlags (raylib.h) used for the Set/Clear/IsWindowState round-trip.
    private const uint FLAG_WINDOW_RESIZABLE = 0x00000004; // non-visible attribute — safe to toggle in a test
    private const uint FLAG_WINDOW_HIDDEN    = 0x00000080;

    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern void Framework_InitWindow(int width, int height, string title);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_CloseWindow();
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_WindowShouldClose();
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsWindowReady();
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsWindowFullscreen();
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsWindowHidden();
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsWindowMinimized();
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsWindowMaximized();
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsWindowFocused();
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsWindowResized();
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsWindowState(uint flag);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetWindowState(uint flags);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ClearWindowState(uint flags);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_MaximizeWindow();
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_MinimizeWindow();
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_RestoreWindow();
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetWindowPosition(int x, int y);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetWindowMonitor(int monitor);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetWindowMaxSize(int width, int height);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetWindowOpacity(float opacity);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetWindowFocused();
    [DllImport(DLL, CallingConvention = CC)] private static extern IntPtr Framework_GetWindowHandle();
    // Already-bound C2 getters used purely as read-back oracles for the size assertions.
    [DllImport(DLL, CallingConvention = CC)] private static extern int Framework_GetScreenWidth();
    [DllImport(DLL, CallingConvention = CC)] private static extern int Framework_GetScreenHeight();

    [Test]
    public void Raw_window_lifecycle_state_and_control_roundtrip()
    {
        try { Framework_InitWindow(320, 240, "vgs_c1_window_test"); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); return; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates rcore Batch core-C1 exports; refresh IDE\\ first."); return; }

        if (!Framework_IsWindowReady())
        {
            // No display / GL context here (headless). Nothing to exercise; that's not a binding failure.
            try { Framework_CloseWindow(); } catch { /* nothing to tear down */ }
            Assert.Ignore("No GL window could be created in this environment (headless).");
            return;
        }

        try
        {
            Framework_SetWindowState(FLAG_WINDOW_HIDDEN); // hide immediately so the test shows at most a brief flash

            Assert.Multiple(() =>
            {
                Assert.That(Framework_IsWindowReady(), Is.True, "window is ready after InitWindow");
                Assert.That(Framework_IsWindowFullscreen(), Is.False, "created windowed, not fullscreen");

                // Hidden flag round-trips through Set -> Is.
                Assert.That(Framework_IsWindowHidden(), Is.True, "SetWindowState(HIDDEN) -> IsWindowHidden");
                Assert.That(Framework_IsWindowState(FLAG_WINDOW_HIDDEN), Is.True, "IsWindowState reflects HIDDEN");

                // A non-visible attribute round-trips cleanly: set then clear RESIZABLE.
                Framework_SetWindowState(FLAG_WINDOW_RESIZABLE);
                Assert.That(Framework_IsWindowState(FLAG_WINDOW_RESIZABLE), Is.True, "SetWindowState set RESIZABLE");
                Framework_ClearWindowState(FLAG_WINDOW_RESIZABLE);
                Assert.That(Framework_IsWindowState(FLAG_WINDOW_RESIZABLE), Is.False, "ClearWindowState cleared RESIZABLE");

                // Native OS handle is a real pointer.
                Assert.That(Framework_GetWindowHandle(), Is.Not.EqualTo(IntPtr.Zero), "GetWindowHandle returns a native handle");

                // Screen size reflects the requested dimensions (oracle via already-bound getters).
                Assert.That(Framework_GetScreenWidth(), Is.EqualTo(320), "GetScreenWidth reflects InitWindow width");
                Assert.That(Framework_GetScreenHeight(), Is.EqualTo(240), "GetScreenHeight reflects InitWindow height");

                // No ESC / close request immediately after init.
                Assert.That(Framework_WindowShouldClose(), Is.False, "WindowShouldClose false right after init");

                // The remaining setters/queries have no synchronous, WM-independent read-back, so we exercise them only to
                // prove the P/Invoke signatures marshal without corrupting the stack (a wrong ABI => AccessViolation here).
                Assert.DoesNotThrow(() =>
                {
                    Framework_MaximizeWindow();
                    Framework_RestoreWindow();
                    Framework_MinimizeWindow();
                    Framework_RestoreWindow();
                    Framework_SetWindowPosition(64, 64);
                    Framework_SetWindowMonitor(0);
                    Framework_SetWindowMaxSize(1920, 1080);
                    Framework_SetWindowOpacity(1.0f);
                    Framework_SetWindowFocused();
                    _ = Framework_IsWindowMinimized();
                    _ = Framework_IsWindowMaximized();
                    _ = Framework_IsWindowFocused();
                    _ = Framework_IsWindowResized();
                }, "all void/bool C1 setters marshal without an ABI mismatch");
            });
        }
        finally
        {
            Framework_CloseWindow();
        }
    }
}
