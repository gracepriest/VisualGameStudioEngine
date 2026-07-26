using System;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Runtime correctness for the WINDOW-DEPENDENT half of rcore Batch core-C6 — the manual frame-control calls and the two
/// input stragglers, which need a live GL/GLFW context. [Category("Integration")] + [NonParallelizable] (owns the single
/// global raylib window). InitWindow's at a known size, hides it, then reads GetTouchPosition (a Vector2 by-value return)
/// and ABI-smokes PollInputEvents/SwapScreenBuffer/SetGamepadVibration under DoesNotThrow (a wrong P/Invoke signature
/// would corrupt the stack → AccessViolation). WaitTime is exercised here too: its busy-wait needs InitWindow's timer, so
/// it cannot run headlessly. Self-skips via Assert.Ignore when no display/GL context exists. The headless C6 half (RNG,
/// MemAlloc, TraceLog, SetConfigFlags) is covered by RaylibCoreC6RandomMiscTests.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class RaylibCoreC6FrameControlTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;
    private const uint FLAG_WINDOW_HIDDEN = 0x00000080;

    [StructLayout(LayoutKind.Sequential)]
    private struct Vector2 { public float x; public float y; }

    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern void Framework_InitWindow(int width, int height, string title);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_CloseWindow();
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsWindowReady();
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetWindowState(uint flags);

    // C6 window-dependent functions under test.
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SwapScreenBuffer();
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_PollInputEvents();
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetGamepadVibration(int gamepad, float leftMotor, float rightMotor, float duration);
    [DllImport(DLL, CallingConvention = CC)] private static extern Vector2 Framework_GetTouchPosition(int index);
    // WaitTime busy-waits on GetTime(); only safe once InitWindow has initialized the timer base (hence tested here).
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_WaitTime(double seconds);

    [Test]
    public void Frame_control_and_input_stragglers_marshal_under_a_window()
    {
        try { Framework_InitWindow(320, 240, "vgs_c6_frame_test"); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); return; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates rcore Batch core-C6 exports; refresh IDE\\ first."); return; }

        if (!Framework_IsWindowReady())
        {
            try { Framework_CloseWindow(); } catch { /* nothing to tear down */ }
            Assert.Ignore("No GL window could be created in this environment (headless).");
            return;
        }

        try
        {
            Framework_SetWindowState(FLAG_WINDOW_HIDDEN); // hide immediately so the test shows at most a brief flash

            Assert.Multiple(() =>
            {
                // GetTouchPosition returns a finite Vector2 (0,0 with no active touch) — exercises the by-value struct ABI.
                Vector2 t = Framework_GetTouchPosition(0);
                Assert.That(float.IsFinite(t.x) && float.IsFinite(t.y), Is.True, "GetTouchPosition returns a finite Vector2");

                // The void frame-control calls + gamepad vibration marshal without an ABI mismatch (a wrong signature
                // would corrupt the stack → AccessViolation). SetGamepadVibration on an absent gamepad 0 is a no-op.
                Assert.DoesNotThrow(() =>
                {
                    Framework_PollInputEvents();
                    Framework_SwapScreenBuffer();
                    Framework_SetGamepadVibration(0, 0.0f, 0.0f, 0.0f);
                    // WaitTime with the timer live (InitWindow ran) returns after ~1 ms; also exercises the Double param.
                    Framework_WaitTime(0.001);
                }, "PollInputEvents / SwapScreenBuffer / SetGamepadVibration / WaitTime marshal cleanly under a window");
            });
        }
        finally
        {
            Framework_CloseWindow();
        }
    }
}
