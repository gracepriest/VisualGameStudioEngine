using System;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Runtime correctness for the HEADLESS half of rcore Batch core-C6 — the random-number generator, the raylib heap
/// allocator, and the trace/config helpers. None of these need a GL window (raylib's RNG and rmem are independent of
/// InitWindow), so this fixture runs in the fast subset with real oracles: seed determinism, inclusive range, no-repeat
/// sequences, and a write→read round-trip through the raw MemAlloc pointer (which would AccessViolation if the allocation
/// were bogus). The window-dependent C6 functions (SwapScreenBuffer/PollInputEvents/SetGamepadVibration/GetTouchPosition
/// and WaitTime — whose busy-wait needs InitWindow's timer) live in RaylibCoreC6FrameControlTests; OpenURL is never
/// invoked (it would launch a browser).
/// </summary>
[TestFixture]
public class RaylibCoreC6RandomMiscTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;

    // raylib TraceLogLevel values (raylib.h).
    private const int LOG_INFO = 3;
    private const int LOG_WARNING = 4;

    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetRandomSeed(uint seed);
    [DllImport(DLL, CallingConvention = CC)] private static extern int Framework_GetRandomValue(int min, int max);
    [DllImport(DLL, CallingConvention = CC)] private static extern IntPtr Framework_LoadRandomSequence(uint count, int min, int max);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadRandomSequence(IntPtr sequence);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetConfigFlags(uint flags);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern void Framework_TraceLog(int logLevel, string text);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetTraceLogLevel(int logLevel);
    [DllImport(DLL, CallingConvention = CC)] private static extern IntPtr Framework_MemAlloc(uint size);
    [DllImport(DLL, CallingConvention = CC)] private static extern IntPtr Framework_MemRealloc(IntPtr ptr, uint size);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_MemFree(IntPtr ptr);
    // NOTE: Framework_WaitTime is intentionally NOT declared/exercised here — raylib's WaitTime busy-waits on GetTime(),
    // whose time base is only initialized by InitWindow(). Called headlessly, GetTime() stays frozen at 0 and WaitTime
    // spins forever (1 core, unkillable). It is exercised under a live window in RaylibCoreC6FrameControlTests instead.

    // Probe that also validates the DLL is staged and carries the C6 exports.
    private static bool C6Available()
    {
        try { Framework_SetRandomSeed(1u); return true; }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    private static void SkipIfUnavailable()
    {
        if (!C6Available()) Assert.Ignore($"{DLL} not staged next to the test binary or predates rcore C6 exports; refresh IDE\\ first.");
    }

    [Test]
    public void SetRandomSeed_makes_GetRandomValue_deterministic()
    {
        SkipIfUnavailable();
        Framework_SetRandomSeed(1234u);
        var a = Enumerable.Range(0, 8).Select(_ => Framework_GetRandomValue(0, 1_000_000)).ToArray();
        Framework_SetRandomSeed(1234u);
        var b = Enumerable.Range(0, 8).Select(_ => Framework_GetRandomValue(0, 1_000_000)).ToArray();

        Assert.That(b, Is.EqualTo(a), "same seed reproduces the same GetRandomValue sequence");
        Assert.That(a.Distinct().Count(), Is.GreaterThan(1), "the RNG actually varies (not a constant)");
    }

    [Test]
    public void GetRandomValue_stays_within_the_inclusive_range()
    {
        SkipIfUnavailable();
        Framework_SetRandomSeed(99u);
        bool hitMin = false, hitMax = false;
        for (int i = 0; i < 2000; i++)
        {
            int v = Framework_GetRandomValue(-5, 5);
            Assert.That(v, Is.InRange(-5, 5), "GetRandomValue stays within [min,max]");
            hitMin |= v == -5; hitMax |= v == 5;
        }
        Assert.That(hitMin && hitMax, Is.True, "both inclusive bounds are reachable over many draws");
    }

    [Test]
    public void LoadRandomSequence_returns_distinct_in_range_values()
    {
        SkipIfUnavailable();
        const int count = 10;
        IntPtr ptr = Framework_LoadRandomSequence(count, 0, 99);
        Assert.That(ptr, Is.Not.EqualTo(IntPtr.Zero), "LoadRandomSequence allocated a sequence");
        try
        {
            var seq = new int[count];
            Marshal.Copy(ptr, seq, 0, count);
            Assert.That(seq, Is.All.InRange(0, 99), "all values within [0,99]");
            Assert.That(seq.Distinct().Count(), Is.EqualTo(count), "no repeated values (raylib guarantees a permutation)");
        }
        finally { Framework_UnloadRandomSequence(ptr); }
    }

    [Test]
    public void MemAlloc_realloc_free_roundtrip_is_writable()
    {
        SkipIfUnavailable();
        IntPtr p = Framework_MemAlloc(64u);
        Assert.That(p, Is.Not.EqualTo(IntPtr.Zero), "MemAlloc returned a non-null buffer");

        // Writing/reading through the raw pointer proves the allocation is real memory (a bogus pointer AccessViolations).
        Assert.DoesNotThrow(() => Marshal.WriteInt32(p, 0, 0x1234ABCD), "MemAlloc buffer is writable");
        Assert.That(Marshal.ReadInt32(p, 0), Is.EqualTo(0x1234ABCD), "written value reads back");

        IntPtr q = Framework_MemRealloc(p, 256u);
        Assert.That(q, Is.Not.EqualTo(IntPtr.Zero), "MemRealloc returned a non-null buffer");
        Assert.That(Marshal.ReadInt32(q, 0), Is.EqualTo(0x1234ABCD), "MemRealloc preserved the existing bytes");
        Framework_MemFree(q);
    }

    [Test]
    public void TraceLog_setloglevel_and_config_marshal_cleanly()
    {
        SkipIfUnavailable();
        Assert.DoesNotThrow(() =>
        {
            Framework_SetTraceLogLevel(LOG_INFO);
            // The text contains printf specifiers on purpose: the engine forwarder routes it through "%s", so "%s %d %x"
            // is printed literally. If it were passed as the format string directly, these specifiers would read varargs
            // off the stack and AccessViolation here — so a clean return proves the "%s" routing.
            Framework_TraceLog(LOG_WARNING, "vgs_c6 trace literal %s %d %x end");
            Framework_SetConfigFlags(0u); // no-op flags; does NOT create a window
        }, "TraceLog/SetTraceLogLevel/SetConfigFlags marshal without an ABI mismatch");
    }
}
