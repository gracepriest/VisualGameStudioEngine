using System;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// HEADLESS correctness for the rgestures batch. raylib's gesture state lives in a zero-initialized static struct that only
/// PollInputEvents (under a window, with real touch input) mutates, and the getters are pure reads with no GL — so headless
/// they return well-defined defaults, and the ABI can be checked in the FAST SUBSET. This proves the marshaling: the two
/// Vector2-BY-VALUE returns come back as a valid (0,0) struct (a misaligned Vector2 return would corrupt the stack, not
/// yield clean zeros), IsGestureDetected is a real I1 bool, GetGestureDetected is Integer (GESTURE_NONE = 0), the float
/// getters are Single, and SetGesturesEnabled accepts a UInteger flag word. Value-level gesture behavior needs real touch
/// input (not automatable headlessly); the Vector2-by-value ABI itself is already proven non-trivially by Batch C2.
/// Self-Ignores when the DLL isn't staged or predates the rgestures exports.
/// </summary>
[TestFixture]
public class RaylibGesturesTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;

    // raylib Gesture flags (subset).
    private const uint GESTURE_NONE = 0;
    private const uint GESTURE_TAP = 1;
    private const uint GESTURE_DRAG = 8;
    private const uint GESTURE_ALL = 0x3FF; // all 10 gesture bits (TAP..PINCH_OUT)

    [StructLayout(LayoutKind.Sequential)] private struct Vector2 { public float x, y; }

    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetGesturesEnabled(uint flags);
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsGestureDetected(uint gesture);
    [DllImport(DLL, CallingConvention = CC)] private static extern int Framework_GetGestureDetected();
    [DllImport(DLL, CallingConvention = CC)] private static extern float Framework_GetGestureHoldDuration();
    [DllImport(DLL, CallingConvention = CC)] private static extern Vector2 Framework_GetGestureDragVector();
    [DllImport(DLL, CallingConvention = CC)] private static extern float Framework_GetGestureDragAngle();
    [DllImport(DLL, CallingConvention = CC)] private static extern Vector2 Framework_GetGesturePinchVector();
    [DllImport(DLL, CallingConvention = CC)] private static extern float Framework_GetGesturePinchAngle();

    [Test]
    public void Gesture_getters_return_well_formed_defaults_headless()
    {
        try { Framework_SetGesturesEnabled(GESTURE_ALL); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); return; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates the rgestures exports; refresh IDE\\ first."); return; }

        Vector2 drag = Framework_GetGestureDragVector();
        Vector2 pinch = Framework_GetGesturePinchVector();

        Assert.Multiple(() =>
        {
            // No touch input headless -> GESTURE_NONE and zero-valued getters. SetGesturesEnabled did not throw above.
            Assert.That(Framework_IsGestureDetected(GESTURE_TAP), Is.False, "no gesture is detected without input");
            Assert.That(Framework_IsGestureDetected(GESTURE_DRAG), Is.False, "no drag gesture without input");
            Assert.That(Framework_GetGestureDetected(), Is.EqualTo((int)GESTURE_NONE), "latest gesture is GESTURE_NONE (0)");
            Assert.That(Framework_GetGestureHoldDuration(), Is.EqualTo(0f), "hold duration is 0 with no active hold");
            Assert.That(Framework_GetGestureDragAngle(), Is.EqualTo(0f), "drag angle is 0");
            Assert.That(Framework_GetGesturePinchAngle(), Is.EqualTo(0f), "pinch angle is 0");

            // The Vector2-by-value returns marshal as a clean (0,0) struct (a wrong ABI would give garbage/NaN, not zeros).
            Assert.That(drag.x, Is.EqualTo(0f), "drag vector x is 0");
            Assert.That(drag.y, Is.EqualTo(0f), "drag vector y is 0");
            Assert.That(pinch.x, Is.EqualTo(0f), "pinch vector x is 0");
            Assert.That(pinch.y, Is.EqualTo(0f), "pinch vector y is 0");
        });
    }
}
