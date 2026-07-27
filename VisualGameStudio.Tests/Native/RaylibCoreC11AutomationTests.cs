using System;
using System.IO;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// HEADLESS correctness for rcore Batch core-C11 (automation events). List management (Load/Unload/Export) and PlayAutomationEvent
/// are pure CPU/file/global-state — no GL — so the struct marshaling gets a real oracle in the FAST SUBSET (the record/replay
/// cycle, which needs the input/frame system, lives in RaylibCoreC11RecordingTests). Proves:
///   - LoadAutomationEventList(NULL) returns an allocated empty list BY VALUE — capacity &gt; 0, count == 0, events != NULL
///     (a misaligned 16-byte {uint,uint,ptr} return would scramble these);
///   - ExportAutomationEventList writes a real text file and returns true (by-value param + I1);
///   - PlayAutomationEvent marshals the non-blittable 24-byte AutomationEvent (uint,uint,int[4]) BY VALUE — exercised with a
///     benign type-0 event (params all zero), a no-op in raylib so it is headless-safe;
///   - SetAutomationEventBaseFrame / UnloadAutomationEventList round-trip without throwing.
/// Self-Ignores via Assert.Ignore when the DLL isn't staged or predates the C11 exports.
/// </summary>
[TestFixture]
public class RaylibCoreC11AutomationTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;

    [StructLayout(LayoutKind.Sequential)]
    private struct AutomationEvent
    {
        public uint frame;
        public uint type;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public int[] prms; // int params[4]
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AutomationEventList { public uint capacity; public uint count; public IntPtr events; }

    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern AutomationEventList Framework_LoadAutomationEventList(string fileName);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadAutomationEventList(AutomationEventList list);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_ExportAutomationEventList(AutomationEventList list, string fileName);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetAutomationEventBaseFrame(int frame);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_PlayAutomationEvent(AutomationEvent evt);

    [Test]
    public void Automation_list_and_event_structs_marshal_headless()
    {
        AutomationEventList list;
        try { list = Framework_LoadAutomationEventList(null); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); return; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates rcore Batch core-C11 exports; refresh IDE\\ first."); return; }

        var export = Path.Combine(Path.GetTempPath(), $"vgs_c11_{Guid.NewGuid():N}.rae");
        try
        {
            Assert.Multiple(() =>
            {
                // The by-value AutomationEventList return: LoadAutomationEventList(NULL) allocates an empty list
                // (capacity = MAX_AUTOMATION_EVENTS). A misaligned 16-byte struct return would scramble these three fields.
                Assert.That(list.count, Is.EqualTo(0u), "a fresh list has 0 recorded events");
                Assert.That(list.capacity, Is.GreaterThan(0u), "a fresh list has a non-zero capacity (MAX_AUTOMATION_EVENTS)");
                Assert.That(list.capacity, Is.LessThan(1_000_000u), "capacity is a small count, not a misaligned pointer value");
                Assert.That(list.events, Is.Not.EqualTo(IntPtr.Zero), "the events array was allocated (raylib-owned pointer)");

                // By-value param + I1 return: export the empty list to a real text file.
                bool ok = Framework_ExportAutomationEventList(list, export);
                Assert.That(ok, Is.True, "ExportAutomationEventList returns true");
                Assert.That(File.Exists(export), Is.True, "the .rae text file was written");
                Assert.That(new FileInfo(export).Length, Is.GreaterThan(0L), "the exported file is non-empty");

                // The non-blittable 24-byte AutomationEvent by-value param. type 0 with zero params is a raylib no-op, so this
                // exercises the marshaling (uint,uint,int[4] = 24 B) without touching any window-bound input subsystem.
                Assert.DoesNotThrow(() => Framework_PlayAutomationEvent(new AutomationEvent { frame = 0u, type = 0u, prms = new int[4] }),
                    "PlayAutomationEvent marshals the non-blittable AutomationEvent by value");

                Assert.DoesNotThrow(() => Framework_SetAutomationEventBaseFrame(0), "SetAutomationEventBaseFrame round-trips");
            });
        }
        finally
        {
            try { Framework_UnloadAutomationEventList(list); } catch { /* frees the raylib-owned events array */ }
            try { if (File.Exists(export)) File.Delete(export); } catch { /* temp cleanup */ }
        }
    }
}
