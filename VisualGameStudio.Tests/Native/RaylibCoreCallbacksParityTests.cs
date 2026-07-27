using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Engine⇄wrapper sync invariant for the deferred CALLBACKS batch (5 rcore SetXxxCallback + 5 raudio AudioCallback
/// function-pointer setters). All headless — fast subset. Three checks:
///   1. Every_callback_export_is_bound_3_ways — genuine 3-way (framework.h export + framework.cpp forwarder + wrapper
///      import) for all 10, PLUS a raylib.h completeness cross-check that the rcore SetTraceLogCallback..SetSaveFileTextCallback
///      range is EXACTLY the 5 rcore names. (The 5 raudio callbacks are interleaved with non-callback fns in raylib.h, so
///      they get name-presence checks, not a contiguous range check.)
///   2. Wrapper_declares_6_UnmanagedFunctionPointer_delegates_and_binds_them — the whole point of this batch: each of the 6
///      raylib callback typedefs is mirrored by a &lt;UnmanagedFunctionPointer(Cdecl)&gt; delegate with the right signature
///      (IntPtr for const char*/char*/void*/va_list, I1 for the bool returns, ByRef Integer for the int* out), and every
///      binding takes the delegate type (not a bare IntPtr) so callers get a typed callback.
///   3. Callback_exports_are_each_declared_exactly_once — duplicate-export guard.
/// </summary>
[TestFixture]
public class RaylibCoreCallbacksParityTests
{
    private static readonly string[] RcoreCallbackNames =
    {
        "SetTraceLogCallback", "SetLoadFileDataCallback", "SetSaveFileDataCallback", "SetLoadFileTextCallback", "SetSaveFileTextCallback",
    };

    private static readonly string[] RaudioCallbackNames =
    {
        "SetAudioStreamCallback", "AttachAudioStreamProcessor", "DetachAudioStreamProcessor", "AttachAudioMixedProcessor", "DetachAudioMixedProcessor",
    };

    private static string[] AllCallbackNames => RcoreCallbackNames.Concat(RaudioCallbackNames).ToArray();

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        for (; d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "VisualGameStudioEngine.sln")))
                return d.FullName;
        throw new DirectoryNotFoundException("VisualGameStudioEngine.sln not found above " + AppContext.BaseDirectory);
    }

    [Test]
    public void Every_callback_export_is_bound_3_ways()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));
        var forwarder = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.cpp"));
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            foreach (var name in AllCallbackNames)
            {
                Assert.That(header.Contains($"Framework_{name}("), Is.True, $"framework.h missing export Framework_{name}");
                Assert.That(forwarder.Contains($"Framework_{name}("), Is.True, $"framework.cpp missing forwarder Framework_{name}");
                Assert.That(wrapper.Contains($"Framework_{name}("), Is.True, $"RaylibWrapper.vb missing import Framework_{name}");
            }

            var raylibHeader = File.ReadAllText(Path.Combine(root, "packages", "raylib.5.5.0", "build", "native", "include", "raylib.h"));
            var rcoreRange = ExtractRlapiRange(raylibHeader, "SetTraceLogCallback(", "SetSaveFileTextCallback(");
            Assert.That(rcoreRange, Is.EquivalentTo(RcoreCallbackNames),
                "raylib's SetTraceLogCallback..SetSaveFileTextCallback range must be exactly the 5 rcore callback setters");
        });
    }

    [Test]
    public void Wrapper_declares_6_UnmanagedFunctionPointer_delegates_and_binds_them()
    {
        var root = RepoRoot();
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            // The 6 delegate types, each Cdecl UnmanagedFunctionPointer with the correct signature.
            Assert.That(Regex.IsMatch(wrapper, @"<UnmanagedFunctionPointer\(CallingConvention\.Cdecl\)>\s*Public Delegate Sub TraceLogCallback\(logLevel As Integer, text As IntPtr, args As IntPtr\)"),
                Is.True, "TraceLogCallback: Cdecl Sub(Integer, IntPtr text, IntPtr va_list)");
            Assert.That(Regex.IsMatch(wrapper, @"<UnmanagedFunctionPointer\(CallingConvention\.Cdecl\)>\s*Public Delegate Function LoadFileDataCallback\(fileName As IntPtr, ByRef dataSize As Integer\) As IntPtr"),
                Is.True, "LoadFileDataCallback: Cdecl Function(IntPtr, ByRef Integer) As IntPtr");
            Assert.That(Regex.IsMatch(wrapper, @"<UnmanagedFunctionPointer\(CallingConvention\.Cdecl\)>\s*Public Delegate Function SaveFileDataCallback\(fileName As IntPtr, data As IntPtr, dataSize As Integer\) As <MarshalAs\(UnmanagedType\.I1\)> Boolean"),
                Is.True, "SaveFileDataCallback: Cdecl Function(IntPtr, IntPtr, Integer) As I1 Boolean");
            Assert.That(Regex.IsMatch(wrapper, @"<UnmanagedFunctionPointer\(CallingConvention\.Cdecl\)>\s*Public Delegate Function LoadFileTextCallback\(fileName As IntPtr\) As IntPtr"),
                Is.True, "LoadFileTextCallback: Cdecl Function(IntPtr) As IntPtr");
            Assert.That(Regex.IsMatch(wrapper, @"<UnmanagedFunctionPointer\(CallingConvention\.Cdecl\)>\s*Public Delegate Function SaveFileTextCallback\(fileName As IntPtr, text As IntPtr\) As <MarshalAs\(UnmanagedType\.I1\)> Boolean"),
                Is.True, "SaveFileTextCallback: Cdecl Function(IntPtr, IntPtr) As I1 Boolean");
            Assert.That(Regex.IsMatch(wrapper, @"<UnmanagedFunctionPointer\(CallingConvention\.Cdecl\)>\s*Public Delegate Sub AudioCallback\(bufferData As IntPtr, frames As UInteger\)"),
                Is.True, "AudioCallback: Cdecl Sub(IntPtr, UInteger)");

            // Every binding takes the delegate type (typed callback), not a bare IntPtr.
            Assert.That(wrapper.Contains("Framework_SetTraceLogCallback(callback As TraceLogCallback)"), Is.True, "SetTraceLogCallback takes TraceLogCallback");
            Assert.That(wrapper.Contains("Framework_SetLoadFileDataCallback(callback As LoadFileDataCallback)"), Is.True, "SetLoadFileDataCallback takes LoadFileDataCallback");
            Assert.That(wrapper.Contains("Framework_SetSaveFileDataCallback(callback As SaveFileDataCallback)"), Is.True, "SetSaveFileDataCallback takes SaveFileDataCallback");
            Assert.That(wrapper.Contains("Framework_SetLoadFileTextCallback(callback As LoadFileTextCallback)"), Is.True, "SetLoadFileTextCallback takes LoadFileTextCallback");
            Assert.That(wrapper.Contains("Framework_SetSaveFileTextCallback(callback As SaveFileTextCallback)"), Is.True, "SetSaveFileTextCallback takes SaveFileTextCallback");
            Assert.That(wrapper.Contains("Framework_SetAudioStreamCallback(stream As AudioStream, callback As AudioCallback)"), Is.True, "SetAudioStreamCallback: AudioStream by value + AudioCallback");
            Assert.That(wrapper.Contains("Framework_AttachAudioStreamProcessor(stream As AudioStream, processor As AudioCallback)"), Is.True, "AttachAudioStreamProcessor: AudioStream + AudioCallback");
            Assert.That(wrapper.Contains("Framework_DetachAudioStreamProcessor(stream As AudioStream, processor As AudioCallback)"), Is.True, "DetachAudioStreamProcessor: AudioStream + AudioCallback");
            Assert.That(wrapper.Contains("Framework_AttachAudioMixedProcessor(processor As AudioCallback)"), Is.True, "AttachAudioMixedProcessor: AudioCallback");
            Assert.That(wrapper.Contains("Framework_DetachAudioMixedProcessor(processor As AudioCallback)"), Is.True, "DetachAudioMixedProcessor: AudioCallback");
        });
    }

    [Test]
    public void Callback_exports_are_each_declared_exactly_once()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));

        Assert.Multiple(() =>
        {
            foreach (var name in AllCallbackNames)
            {
                int count = CountOccurrences(header, $"Framework_{name}(");
                Assert.That(count, Is.EqualTo(1), $"Framework_{name} should be declared exactly once (found {count})");
            }
        });
    }

    private static List<string> ExtractRlapiRange(string raylibHeader, string firstContains, string lastContains)
    {
        var lines = raylibHeader.Replace("\r\n", "\n").Split('\n');
        int start = Array.FindIndex(lines, l => l.Contains("RLAPI") && l.Contains(firstContains));
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"raylib.h start anchor '{firstContains}' not found");
        var names = new List<string>();
        var rx = new Regex(@"RLAPI\s+.*?(\w+)\s*\(");
        for (int i = start; i < lines.Length; i++)
        {
            if (lines[i].Contains("RLAPI"))
            {
                var m = rx.Match(lines[i]);
                if (m.Success) names.Add(m.Groups[1].Value);
            }
            if (lines[i].Contains(lastContains)) break;
        }
        return names;
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { count++; i += needle.Length; }
        return count;
    }
}
