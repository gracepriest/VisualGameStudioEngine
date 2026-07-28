using System;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Runtime correctness for rcore Batch core-C2 (raw raylib window/monitor query getters &amp; clipboard). Like C1 these
/// need a real GL window, so this fixture is [Category("Integration")] (excluded from the fast subset) and
/// [NonParallelizable] (it owns the single global raylib window). It InitWindow's at a known size, hides the window
/// immediately, then reads the render/monitor/DPI getters and round-trips the clipboard. The Vector2/Image by-value
/// returns are the real target here: a wrong struct-return ABI would surface as garbage/NaN fields or an
/// AccessViolation. If no display/GL context can be created (headless CI), the whole test self-skips via Assert.Ignore.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class RaylibWindowQueryClipboardTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;
    private const uint FLAG_WINDOW_HIDDEN = 0x00000080;

    [StructLayout(LayoutKind.Sequential)]
    private struct Vector2 { public float x; public float y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Image { public IntPtr data; public int width; public int height; public int mipmaps; public int format; }

    // Window lifecycle + already-bound oracles.
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern void Framework_InitWindow(int width, int height, string title);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_CloseWindow();
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsWindowReady();
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetWindowState(uint flags);
    [DllImport(DLL, CallingConvention = CC)] private static extern int Framework_GetScreenWidth();
    [DllImport(DLL, CallingConvention = CC)] private static extern int Framework_GetScreenHeight();
    [DllImport(DLL, CallingConvention = CC)] private static extern int Framework_GetMonitorCount();
    [DllImport(DLL, CallingConvention = CC)] private static extern int Framework_GetCurrentMonitor();
    [DllImport(DLL, CallingConvention = CC)] private static extern int Framework_GetMonitorWidth(int monitor);
    [DllImport(DLL, CallingConvention = CC)] private static extern int Framework_GetMonitorHeight(int monitor);

    // C2 under test.
    [DllImport(DLL, CallingConvention = CC)] private static extern int Framework_GetRenderWidth();
    [DllImport(DLL, CallingConvention = CC)] private static extern int Framework_GetRenderHeight();
    [DllImport(DLL, CallingConvention = CC)] private static extern Vector2 Framework_GetMonitorPosition(int monitor);
    [DllImport(DLL, CallingConvention = CC)] private static extern int Framework_GetMonitorPhysicalWidth(int monitor);
    [DllImport(DLL, CallingConvention = CC)] private static extern int Framework_GetMonitorPhysicalHeight(int monitor);
    [DllImport(DLL, CallingConvention = CC)] private static extern Vector2 Framework_GetWindowPosition();
    [DllImport(DLL, CallingConvention = CC)] private static extern Vector2 Framework_GetWindowScaleDPI();
    [DllImport(DLL, CallingConvention = CC)] private static extern IntPtr Framework_GetMonitorName(int monitor);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern void Framework_SetClipboardText(string text);
    [DllImport(DLL, CallingConvention = CC)] private static extern IntPtr Framework_GetClipboardText();
    [DllImport(DLL, CallingConvention = CC)] private static extern Image Framework_GetClipboardImage();
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_EnableEventWaiting();
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DisableEventWaiting();

    [Test]
    public void Raw_window_query_and_clipboard_roundtrip()
    {
        try { Framework_InitWindow(320, 240, "vgs_c2_query_test"); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); return; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates rcore Batch core-C2 exports; refresh IDE\\ first."); return; }

        if (!Framework_IsWindowReady())
        {
            try { Framework_CloseWindow(); } catch { /* nothing to tear down */ }
            Assert.Ignore("No GL window could be created in this environment (headless).");
            return;
        }

        try
        {
            Framework_SetWindowState(FLAG_WINDOW_HIDDEN); // hide immediately so the test shows at most a brief flash
            int monitor = Framework_GetCurrentMonitor();

            Assert.Multiple(() =>
            {
                // Render size >= screen size (equal at 1x DPI; larger under HiDPI). Also proves the render getters bind.
                Assert.That(Framework_GetRenderWidth(), Is.GreaterThanOrEqualTo(Framework_GetScreenWidth()), "render width >= screen width");
                Assert.That(Framework_GetRenderHeight(), Is.GreaterThanOrEqualTo(Framework_GetScreenHeight()), "render height >= screen height");

                // At least one monitor and the current index is in range.
                int count = Framework_GetMonitorCount();
                Assert.That(count, Is.GreaterThanOrEqualTo(1), "at least one connected monitor");
                Assert.That(monitor, Is.InRange(0, count - 1), "current monitor index in range");

                // Current monitor has a positive video-mode size; physical size is non-negative (0 if unreported by driver).
                Assert.That(Framework_GetMonitorWidth(monitor), Is.GreaterThan(0), "monitor width positive");
                Assert.That(Framework_GetMonitorHeight(monitor), Is.GreaterThan(0), "monitor height positive");
                Assert.That(Framework_GetMonitorPhysicalWidth(monitor), Is.GreaterThanOrEqualTo(0), "physical width non-negative");
                Assert.That(Framework_GetMonitorPhysicalHeight(monitor), Is.GreaterThanOrEqualTo(0), "physical height non-negative");

                // Vector2 by-value returns marshal correctly: DPI scale is >= 1 on both axes (garbage/NaN would fail).
                Vector2 dpi = Framework_GetWindowScaleDPI();
                Assert.That(dpi.x, Is.GreaterThanOrEqualTo(1.0f), "DPI scale x >= 1");
                Assert.That(dpi.y, Is.GreaterThanOrEqualTo(1.0f), "DPI scale y >= 1");

                // Monitor/window positions return finite coordinates (again exercising the Vector2 struct ABI).
                Vector2 mpos = Framework_GetMonitorPosition(monitor);
                Assert.That(float.IsFinite(mpos.x) && float.IsFinite(mpos.y), Is.True, "monitor position finite");
                Vector2 wpos = Framework_GetWindowPosition();
                Assert.That(float.IsFinite(wpos.x) && float.IsFinite(wpos.y), Is.True, "window position finite");

                // Monitor name: const char* -> IntPtr -> PtrToStringAnsi comes back non-null and non-empty.
                IntPtr namePtr = Framework_GetMonitorName(monitor);
                string name = namePtr == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(namePtr);
                Assert.That(name, Is.Not.Null.And.Not.Empty, "monitor name is a non-empty String");

                // Clipboard text round-trip (GLFW/OS-backed): Set then Get returns the same ASCII payload.
                const string payload = "vgs_c2_clipboard_roundtrip";
                Framework_SetClipboardText(payload);
                IntPtr clipPtr = Framework_GetClipboardText();
                string clip = clipPtr == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(clipPtr);
                Assert.That(clip, Is.EqualTo(payload), "clipboard text round-trips through Set/Get");

                // GetClipboardImage (Image by value) + the event-waiting toggles marshal without an ABI mismatch.
                Assert.DoesNotThrow(() =>
                {
                    Image img = Framework_GetClipboardImage();
                    _ = img.width; // touch the returned struct
                    Framework_EnableEventWaiting();
                    Framework_DisableEventWaiting();
                }, "GetClipboardImage / Enable/DisableEventWaiting marshal cleanly");
            });
        }
        finally
        {
            Framework_CloseWindow();
        }
    }
}
