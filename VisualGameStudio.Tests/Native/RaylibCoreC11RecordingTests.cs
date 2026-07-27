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
/// stack/struct mismatch. With no synthetic input nothing is recorded (count stays 0), which is the expected empty-run
/// result. Self-Ignores when headless or when the DLL predates the C11 exports.
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

    [Test]
    public void Automation_recording_cycle_marshals_under_a_window()
    {
        try { Framework_InitWindow(320, 240, "vgs_c11_record_test"); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); return; }
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

            // Read the pinned struct back: capacity/events are unchanged and, with no synthetic input, nothing was recorded.
            var after = Marshal.PtrToStructure<AutomationEventList>(listPtr);
            Assert.Multiple(() =>
            {
                Assert.That(after.capacity, Is.EqualTo(list.capacity), "capacity preserved across the recording session");
                Assert.That(after.events, Is.EqualTo(list.events), "raylib recorded through the same events pointer");
                Assert.That(after.count, Is.EqualTo(0u), "no synthetic input → nothing recorded");
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
