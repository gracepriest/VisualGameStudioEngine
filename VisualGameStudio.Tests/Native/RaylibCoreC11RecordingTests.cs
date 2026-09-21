using System;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Runtime correctness for the RECORDING half of rcore Batch core-C11 under a live window — [Category("Integration")] +
/// [NonParallelizable] (owns the single global raylib window). SetAutomationEventList takes a POINTER that raylib RETAINS
/// for the whole recording session, so this test allocates the AutomationEventList in UNMANAGED memory (stable address),
/// hands raylib that address, records across a few frames, then reads the struct back — proving the retained-pointer
/// contract and the record lifecycle (SetAutomationEventList / SetAutomationEventBaseFrame / Start / Stop) marshal without a
/// stack/struct mismatch. Self-Ignores when headless or when the DLL predates the C11 exports.
///
/// <para>⛔ RECORDING IS NOT INPUT-ONLY — it reflects ATTACHED HARDWARE. This test used to assert a flat
/// <c>count == 0</c> ("no synthetic input → nothing recorded"). That is FALSE on any machine with a gamepad plugged in:
/// raylib's <c>RecordAutomationEvent</c> runs from <c>EndDrawing</c> and writes one
/// <c>INPUT_GAMEPAD_AXIS_MOTION</c> (type 13) event PER NON-TRIGGER AXIS PER FRAME for every connected pad — even when
/// every stick is dead centre and nobody has touched the device. Measured 2026-09-20 on this repo's raylib 5.5 engine:
/// plugging in an Xbox-compatible pad (6 axes; the 2 triggers rest at -1.0 and do not record, the 4 stick axes read
/// 0.0 and do) turned this row red with <c>count == 8</c> and NOTHING in the repo had changed. 8 = (3 frames − 1) × 4
/// axes: the FIRST frame records nothing because GLFW only raises its joystick-connect callback inside
/// <c>PollInputEvents()</c>, which <c>EndDrawing</c> calls AFTER it records.</para>
///
/// <para>⚠ That ordering is also why the gamepad probe below runs AFTER the frames are pumped, never before: straight
/// out of <c>InitWindow</c> raylib still reports <c>IsGamepadAvailable(0) == false</c> even with a pad attached, so a
/// precondition read at the top of the test would claim "no gamepad" and re-assert the very thing that is wrong.</para>
///
/// <para>So the empty-run claim is made ONLY when the run is genuinely device-free, and the assertions that carry this
/// test's actual purpose — the retained pointer, the in-place write, and Stop ending the session — are stated in a form
/// no attached hardware can move.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class RaylibCoreC11RecordingTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;
    private const uint FLAG_WINDOW_HIDDEN = 0x00000080;

    [StructLayout(LayoutKind.Sequential)]
    private struct AutomationEventList { public uint capacity; public uint count; public IntPtr events; }

    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern void Framework_InitWindow(int width, int height, string title);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_CloseWindow();
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsWindowReady();
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetWindowState(uint flags);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_BeginDrawing();
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_EndDrawing();

    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern AutomationEventList Framework_LoadAutomationEventList(string fileName);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadAutomationEventList(AutomationEventList list);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetAutomationEventList(IntPtr list);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetAutomationEventBaseFrame(int frame);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_StartAutomationEventRecording();
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_StopAutomationEventRecording();

    // The ambient-recording probe. String RETURNS come back as IntPtr + PtrToStringAnsi, never as LPStr String.
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsGamepadAvailable(int gamepad);
    [DllImport(DLL, CallingConvention = CC)] private static extern IntPtr Framework_GetGamepadName(int gamepad);

    [Test]
    public void Automation_recording_cycle_marshals_under_a_window()
    {
        try { Framework_InitWindow(320, 240, "vgs_c11_record_test"); }
        catch (DllNotFoundException) { Assert.Ignore(NativeEngineSkip.DllNotFound(DLL)); return; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates rcore Batch core-C11 exports; refresh IDE\\ first."); return; }

        if (!Framework_IsWindowReady())
        {
            try { Framework_CloseWindow(); } catch { /* nothing to tear down */ }
            Assert.Ignore("No GL window could be created in this environment (headless).");
            return;
        }

        Framework_SetWindowState(FLAG_WINDOW_HIDDEN); // keep the test window off-screen (matches the other integration fixtures)

        AutomationEventList list;
        try { list = Framework_LoadAutomationEventList(null); }
        catch (EntryPointNotFoundException) { try { Framework_CloseWindow(); } catch { } Assert.Ignore($"{DLL} predates rcore Batch core-C11 exports; refresh IDE\\ first."); return; }

        // raylib retains the pointer we pass to SetAutomationEventList, so the list must live at a STABLE address for the
        // whole session — allocate it in unmanaged memory rather than passing a managed/copied struct that would dangle.
        IntPtr listPtr = Marshal.AllocHGlobal(Marshal.SizeOf<AutomationEventList>());
        Marshal.StructureToPtr(list, listPtr, false);
        try
        {
            Assert.DoesNotThrow(() =>
            {
                Framework_SetAutomationEventList(listPtr);
                Framework_SetAutomationEventBaseFrame(0);
                Framework_StartAutomationEventRecording();
                for (int i = 0; i < 3; i++) { Framework_BeginDrawing(); Framework_EndDrawing(); }
                Framework_StopAutomationEventRecording();
            }, "the record cycle (Set/BaseFrame/Start/Stop) marshals the retained pointer under a live window");

            // Read the pinned struct back: capacity/events are unchanged and raylib wrote count in place.
            var after = Marshal.PtrToStructure<AutomationEventList>(listPtr);

            // Now — and only now, with frames already pumped (see the ⚠ note on the fixture) — ask what hardware raylib
            // actually sees. This is what decides whether an empty run was ever on the table.
            string? attachedPad = null;
            for (int g = 0; g < 4 && attachedPad is null; g++)   // raylib's MAX_GAMEPADS
                if (Framework_IsGamepadAvailable(g))
                    attachedPad = $"gamepad {g} '{Marshal.PtrToStringAnsi(Framework_GetGamepadName(g)) ?? "?"}'";

            // Stop must END the session, not merely stop throwing: three more frames after Stop must add NOTHING. This
            // holds with or without a pad attached, so it states the record lifecycle in a form hardware cannot move.
            for (int i = 0; i < 3; i++) { Framework_BeginDrawing(); Framework_EndDrawing(); }
            var afterStop = Marshal.PtrToStructure<AutomationEventList>(listPtr);

            Assert.Multiple(() =>
            {
                Assert.That(after.capacity, Is.EqualTo(list.capacity), "capacity preserved across the recording session");
                Assert.That(after.events, Is.EqualTo(list.events), "raylib recorded through the same events pointer");
                Assert.That(after.count, Is.LessThanOrEqualTo(list.capacity), "raylib never recorded past the capacity it was handed");
                Assert.That(afterStop.count, Is.EqualTo(after.count),
                    "StopAutomationEventRecording ends the session — frames drawn after it record nothing");

                if (attachedPad is null)
                    Assert.That(after.count, Is.EqualTo(0u), "no input device and no synthetic input → nothing recorded");
                else
                    Assert.That(after.count, Is.GreaterThan(0u),
                        $"{attachedPad} is attached, so raylib records its resting axis state every frame — and that raylib "
                        + "moved count from 0 through OUR unmanaged pointer is the retained-pointer contract proving itself");
            });
        }
        finally
        {
            // Clear the global recording flag first (in case the try body threw after Start but before Stop), then detach
            // raylib's retained pointer BEFORE freeing the pinned holder so raylib never holds a freed address.
            try { Framework_StopAutomationEventRecording(); } catch { /* ensure the recording flag is cleared */ }
            try { Framework_SetAutomationEventList(IntPtr.Zero); } catch { /* detach raylib's retained pointer */ }
            try { Marshal.FreeHGlobal(listPtr); } catch { /* free the pinned holder */ }
            try { Framework_UnloadAutomationEventList(list); } catch { /* free the raylib-owned events array */ }
            Framework_CloseWindow();
        }
    }
}
