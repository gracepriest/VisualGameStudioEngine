using System.Collections.Concurrent;
using System.Diagnostics;
using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Serialization;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Integration;

/// <summary>
/// Phase 4 Task 14 — the native debug e2e ring: real MSVC build, real lldb-dap, FULL
/// product path. Where <see cref="NativeDebugGateTests"/> (Task 1) proved the outcomes
/// with a hand-rolled raw DAP driver, this fixture proves the same outcomes through the
/// productized stack a user's F5 actually rides:
/// <c>DebugService</c> + <c>DebugAdapterRegistry</c> (built exactly as
/// <c>ServiceConfiguration</c> builds it) + <c>LldbDapLocator</c> + <c>DapSession</c>'s
/// spec-correct handshake + real adapter-assigned threadIds on the wire.
///
/// <para>
/// Three rings: (a) breakpoint → step → locals → watch → cpp_throw → clean shutdown on
/// user C++ (MSVC PDB route); (b) a <c>.bas</c> breakpoint through Task 0's <c>#line</c>
/// directives, OS-native path form (Task 1's gate verdict: backslash bound immediately);
/// (c) the DWARF route — a g++-built exe debugged through the same product session path,
/// deliberately bypassing the product BUILD (which would probe MSVC first).
/// </para>
///
/// <para>
/// Line-pin provenance for the <c>.bas</c> step target: <c>Return hits * 10</c> lowers to
/// TWO mapped statements in the generated C++ (<c>t0 = hits * 10;</c> under
/// <c>#line 9</c>, then <c>return t0;</c> falling on the <c>End Function</c> line, 10) —
/// verified against the real generated <c>Logic.g.cpp</c>. So step-over from line 9 lands
/// on line 10, still in <c>.bas</c> coordinates.
/// </para>
///
/// <para>
/// Skips (never fails) when no C++ toolchain or no lldb-dap is discoverable — on the
/// primary dev machine both resolve, so these tests run live there.
/// ⚠ lldb-dap discovery is an EXISTENCE check only — spawning <c>lldb-dap --version</c>
/// parks on stdin and hangs forever.
/// </para>
/// </summary>
[TestFixture]
[NonParallelizable] // spawns lldb-dap + native debuggees; also runs real MSVC/g++ builds
[Category("NativeDebugE2E")]
public class NativeDebugE2ETests
{
    /// <summary>winlibs g++ for the DWARF leg — probed at this literal path, deliberately off PATH.</summary>
    private const string GppPath = @"C:\winlibs\mingw64\bin\g++.exe";

    /// <summary>Per-request ceiling. Generous by design — never an expectation.</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Ceiling on each stop (breakpoint / step / exception).</summary>
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Ceiling on session start (adapter spawn + handshake + launch).</summary>
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(90);

    // ------------------------------------------------------------------
    // Temp project fixture — a real MIXED project (Logic.bas + main.cpp),
    // ClangdE2ETests XML shape: no <Compile> items, sources discovered.
    // ------------------------------------------------------------------

    private sealed class TempNativeDebugProject : IDisposable
    {
        public string RootDir { get; }
        public string ProjectFile { get; }
        public string LogicBasFile { get; }
        public string MainCppFile { get; }

        /// <summary>1-based main.cpp line of <c>int local = x * 3;</c> — breakpoint A.</summary>
        public const int CppBreakpointLine = 5;

        /// <summary>1-based main.cpp line of <c>return local;</c> — where step-over lands.</summary>
        public const int CppStepTargetLine = 6;

        /// <summary>1-based main.cpp line of the <c>throw</c> — the cpp_throw target.</summary>
        public const int CppThrowLine = 13;

        /// <summary>1-based Logic.bas line of <c>Return hits * 10</c> — the .bas breakpoint.</summary>
        public const int BasBreakpointLine = 9;

        /// <summary>
        /// 1-based Logic.bas line where step-over from <see cref="BasBreakpointLine"/> lands:
        /// <c>End Function</c> — the generated <c>return t0;</c> statement's mapping (see the
        /// fixture doc comment for the generated-code provenance).
        /// </summary>
        public const int BasStepTargetLine = 10;

        // Unique-ish debuggee name so the zero-orphans Process.GetProcessesByName probe
        // can never collide with an unrelated process.
        private const string ProjectName = "NativeDbgE2E";

        /// <summary>The CalculateScore shape from ClangdE2ETests. Line 9 = Return hits * 10.</summary>
        private const string LogicBas =
@"Class Player
    Public Name As String
    Function Tag() As String
        Return Name
    End Function
End Class

Function CalculateScore(hits As Integer) As Integer
    Return hits * 10
End Function
";

        // NOTE: line numbers are load-bearing (breakpoint, step and throw assertions pin
        // the Cpp*Line constants against this exact text).
        private const string MainCpp =
@"#include ""Logic.g.h""
#include <stdexcept>

static int triple(int x) {
    int local = x * 3;                       // line 5  <- breakpoint A
    return local;                            // line 6  <- step-over lands here
}

int main() {
    int score = CalculateScore(5);           // line 10
    int t = triple(score);                   // line 11
    if (t == 150) {
        throw std::runtime_error(""boom"");  // line 13 <- cpp_throw target
    }
    return 0;
}
";

        private const string ProjectXml =
@"<BasicLangProject Version=""1.0"">
  <PropertyGroup>
    <ProjectName>" + ProjectName + @"</ProjectName>
    <OutputType>Exe</OutputType>
    <Language>Cpp</Language>
    <TargetBackend>Cpp</TargetBackend>
  </PropertyGroup>
</BasicLangProject>
";

        public TempNativeDebugProject()
        {
            RootDir = Path.Combine(Path.GetTempPath(), "vgs-nde2e-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootDir);

            ProjectFile = Path.Combine(RootDir, ProjectName + ".blproj");
            LogicBasFile = Path.Combine(RootDir, "Logic.bas");
            MainCppFile = Path.Combine(RootDir, "main.cpp");

            File.WriteAllText(ProjectFile, ProjectXml);
            File.WriteAllText(LogicBasFile, LogicBas);
            File.WriteAllText(MainCppFile, MainCpp);
        }

        public void Dispose()
        {
            // Best-effort cleanup with retries — the built exe/pdb can stay locked
            // for a moment after the debuggee/adapter process is killed.
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (Directory.Exists(RootDir))
                        Directory.Delete(RootDir, recursive: true);
                    return;
                }
                catch
                {
                    Thread.Sleep(250);
                }
            }
        }
    }

    /// <summary>
    /// The DWARF-route fixture: one self-contained dwarf.cpp (no generated headers, no
    /// product build), compiled DIRECTLY with winlibs g++ by the test.
    /// </summary>
    private sealed class TempDwarfFixture : IDisposable
    {
        public string RootDir { get; }
        public string DwarfCppFile { get; }
        public string ExePath { get; }

        /// <summary>1-based dwarf.cpp line of <c>int local = x * 3;</c> — the breakpoint.</summary>
        public const int BreakpointLine = 2;

        /// <summary>1-based dwarf.cpp line of <c>return local;</c> — where step-over lands.</summary>
        public const int StepTargetLine = 3;

        // NOTE: line numbers are load-bearing (the breakpoint and step-target
        // assertions pin BreakpointLine/StepTargetLine against this exact text).
        private const string DwarfCpp =
@"static int triple(int x) {
    int local = x * 3;    // line 2  <- breakpoint
    return local;         // line 3  <- step-over lands here
}

int main() {
    int t = triple(5);
    return t == 15 ? 0 : 1;
}
";

        public TempDwarfFixture()
        {
            RootDir = Path.Combine(Path.GetTempPath(), "vgs-dwarf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootDir);

            DwarfCppFile = Path.Combine(RootDir, "dwarf.cpp");
            ExePath = Path.Combine(RootDir, "dwarf.exe");
            File.WriteAllText(DwarfCppFile, DwarfCpp);
        }

        public void Dispose()
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (Directory.Exists(RootDir))
                        Directory.Delete(RootDir, recursive: true);
                    return;
                }
                catch
                {
                    Thread.Sleep(250);
                }
            }
        }
    }

    /// <summary>
    /// Thread-safe IOutputService that records everything, so test failures can
    /// include the real build/DAP output instead of a bare assertion message.
    /// </summary>
    private sealed class RecordingOutputService : IOutputService
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public string Dump() => string.Join(Environment.NewLine, _lines);

        public void WriteLine(string message, OutputCategory category = OutputCategory.General) => _lines.Enqueue(message);
        public void Write(string message, OutputCategory category = OutputCategory.General) => _lines.Enqueue(message);
        public void WriteError(string message, OutputCategory category = OutputCategory.General) => _lines.Enqueue("ERROR: " + message);
        public void Clear(OutputCategory category) { }
        public void ClearAll() { }
        public void Activate(OutputCategory category) { }
        public IReadOnlyList<string> GetMessages(OutputCategory category) => _lines.ToArray();
        public event EventHandler<OutputEventArgs>? OutputReceived { add { } remove { } }
        public IOutputChannel CreateChannel(string name) => throw new NotSupportedException();
        public IOutputChannel? GetChannel(string name) => null;
        public IReadOnlyList<IOutputChannel> Channels => Array.Empty<IOutputChannel>();
        public IOutputChannel? ActiveChannel { get; set; }
        public event EventHandler<string>? ChannelCreated { add { } remove { } }
        public event EventHandler<IOutputChannel?>? ActiveChannelChanged { add { } remove { } }
        public void ShowOutput() { }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static bool ProcessIsAlive(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException)
        {
            return false; // process gone
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void AssertProcessExits(int pid, TimeSpan timeout, string what)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!ProcessIsAlive(pid)) return;
            Thread.Sleep(200);
        }
        // Leave no orphan behind even when the assertion is about to fail.
        try { Process.GetProcessById(pid).Kill(entireProcessTree: true); } catch { }
        Assert.Fail($"{what} (pid {pid}) was still running {timeout.TotalSeconds:F0}s after shutdown — orphaned process.");
    }

    /// <summary>
    /// The zero-orphans contract: no process with the debuggee's name may survive
    /// shutdown. Polled — disconnect(terminateDebuggee) tears the tree down
    /// asynchronously — and any leftover is killed before the assert fails.
    /// </summary>
    private static void AssertNoProcessesNamed(string name, TimeSpan timeout, RecordingOutputService output)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var probes = Process.GetProcessesByName(name);
            var anyAlive = probes.Length > 0;
            foreach (var p in probes) p.Dispose();
            if (!anyAlive) return;
            Thread.Sleep(200);
        }

        var leftover = Process.GetProcessesByName(name);
        var pids = string.Join(", ", leftover.Select(p => p.Id));
        foreach (var p in leftover)
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            p.Dispose();
        }
        Assert.Fail($"Debuggee process(es) named '{name}' still running (pids {pids}) after shutdown — orphaned debuggee.\n" + output.Dump());
    }

    /// <summary>Finally-block half of the zero-orphans contract: kill-tree by debuggee name.</summary>
    private static void KillTreeByName(string name)
    {
        foreach (var p in Process.GetProcessesByName(name))
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            p.Dispose();
        }
    }

    private static async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout, string what)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        if (completed != task)
            Assert.Fail($"Timed out after {timeout.TotalSeconds:F0}s waiting for: {what}");
        return await task;
    }

    private static async Task WithTimeout(Task task, TimeSpan timeout, string what)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        if (completed != task)
            Assert.Fail($"Timed out after {timeout.TotalSeconds:F0}s waiting for: {what}");
        await task;
    }

    /// <summary>
    /// Await the next stopped event — failing fast (with the full recorded output) when
    /// the session DIES instead of stopping, so a debuggee that ran past a never-bound
    /// breakpoint reports as exactly that rather than as a 60s timeout.
    /// </summary>
    private static async Task<StoppedEventArgs> AwaitStopAsync(
        Task<StoppedEventArgs> stoppedTask, Task sessionDead, string what, RecordingOutputService output)
    {
        var completed = await Task.WhenAny(stoppedTask, sessionDead, Task.Delay(StopTimeout));
        if (completed == sessionDead)
            Assert.Fail($"Debug session ended instead of stopping for: {what}.\n" + output.Dump());
        if (completed != stoppedTask)
            Assert.Fail($"Timed out after {StopTimeout.TotalSeconds:F0}s waiting for: {what}.\n" + output.Dump());
        return await stoppedTask;
    }

    private static string DumpFrames(IReadOnlyList<StackFrameInfo> frames) =>
        string.Join("\n", frames.Select(f => $"  {f.Name} @ {f.FilePath}:{f.Line}"));

    private static string DumpVariables(IReadOnlyList<VariableInfo> variables) =>
        string.Join("\n", variables.Select(v => $"  {v.Name} = {v.Value} ({v.Type})"));

    private static string DumpBreakpointEvents(IEnumerable<BreakpointsChangedEventArgs> events) =>
        string.Join("\n", events.Select(e =>
            $"  {e.FilePath}: " + string.Join("; ", e.Breakpoints.Select(b => $"line {b.Line} verified={b.Verified} ({b.Message})"))));

    /// <summary>
    /// Discovery gates. ⚠ EXISTENCE checks only — `lldb-dap --version` parks on stdin
    /// and hangs forever; never spawn it to probe.
    /// </summary>
    private static void RequireNativeDebugPrereqs()
    {
        if (CppToolchain.Find() == null)
            Assert.Ignore("No C++ toolchain found (clang++/g++ on PATH, or MSVC via vswhere) — cannot build the native debuggee.");
        if (LldbDapLocator.Resolve(null) == null)
            Assert.Ignore("lldb-dap not found by the product locator chain — the native debug e2e needs it installed.");
    }

    /// <summary>
    /// The registry, built exactly as <c>ServiceConfiguration</c> builds it: built-ins
    /// through the same public Register door, managed descriptor over the compiler-path
    /// locator, lldb-dap descriptor over the locator with no settings override (launch
    /// commands resolve at session start, per the descriptor contract).
    /// </summary>
    private static DebugAdapterRegistry BuildRegistryLikeServiceConfiguration()
    {
        var registry = new DebugAdapterRegistry();
        registry.Register(DebugAdapterDescriptor.BasicLangManaged(DebugService.ResolveCompilerPath));
        registry.Register(DebugAdapterDescriptor.LldbDap(() => LldbDapLocator.Locate(null)));
        return registry;
    }

    /// <summary>Builds the temp project through the real BuildService and returns the result.</summary>
    private static async Task<(BuildResult Result, RecordingOutputService Output)> BuildProjectAsync(
        TempNativeDebugProject fixture)
    {
        var output = new RecordingOutputService();
        var buildService = new BuildService(output);
        var serializer = new ProjectSerializer();

        var project = await serializer.LoadAsync(fixture.ProjectFile);
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var result = await buildService.BuildProjectAsync(project, cts.Token);
        return (result, output);
    }

    private static void AssertBuildSucceeded(BuildResult buildResult, RecordingOutputService buildOutput)
    {
        Assert.That(buildResult.Success, Is.True,
            "Prerequisite native build failed.\nDiagnostics:\n" +
            string.Join("\n", buildResult.Diagnostics.Select(d => $"{d.FilePath}({d.Line},{d.Column}): {d.Severity} {d.Id}: {d.Message}")) +
            "\n\nBuild output:\n" + buildOutput.Dump());
        Assert.That(buildResult.ExecutablePath, Is.Not.Null.And.Not.Empty,
            "Build succeeded but no executable path was reported.\n" + buildOutput.Dump());
        Assert.That(File.Exists(buildResult.ExecutablePath), Is.True,
            $"Reported executable does not exist: {buildResult.ExecutablePath}");
    }

    // ------------------------------------------------------------------
    // (a) The main e2e — full product path on the MSVC/PDB route
    // ------------------------------------------------------------------

    [Test]
    public async Task NativeDebug_CppBreakpoint_Steps_Locals_Watch_CppThrow_CleanShutdown()
    {
        RequireNativeDebugPrereqs();

        using var fixture = new TempNativeDebugProject();

        // ---- 1. Real MSVC build (Debug default → #line directives + /Zi PDB) ----
        var (buildResult, buildOutput) = await BuildProjectAsync(fixture);
        AssertBuildSucceeded(buildResult, buildOutput);
        var debuggeeName = Path.GetFileNameWithoutExtension(buildResult.ExecutablePath!);

        // ---- 2. The product session: DebugService over the ServiceConfiguration registry ----
        var output = new RecordingOutputService();
        var debug = new DebugService(output, BuildRegistryLikeServiceConfiguration());

        var stoppedTcs = new TaskCompletionSource<StoppedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        debug.Stopped += (_, e) => stoppedTcs.TrySetResult(e);

        var sessionDeadTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        debug.StateChanged += (_, e) =>
        {
            if (e.NewState == DebugState.Stopped)
                sessionDeadTcs.TrySetResult(true);
        };

        var breakpointEvents = new ConcurrentQueue<BreakpointsChangedEventArgs>();
        debug.BreakpointsChanged += (_, e) => breakpointEvents.Enqueue(e);

        try
        {
            var config = new DebugConfiguration
            {
                Program = buildResult.ExecutablePath!,
                WorkingDirectory = fixture.RootDir,
                AdapterId = DebugAdapterDescriptor.LldbDapId
            };
            var breakpoints = new Dictionary<string, IEnumerable<SourceBreakpoint>>
            {
                [fixture.MainCppFile] = new[]
                {
                    new SourceBreakpoint { Line = TempNativeDebugProject.CppBreakpointLine }
                }
            };

            // ---- 3. Start → stopped(breakpoint) with a verified line-5 breakpoint ----
            using var startCts = new CancellationTokenSource(StartTimeout);
            var started = await debug.StartDebuggingAsync(config, breakpoints, startCts.Token);
            Assert.That(started, Is.True, "StartDebuggingAsync returned false.\n" + output.Dump());

            var stopped = await AwaitStopAsync(stoppedTcs.Task, sessionDeadTcs.Task,
                "stopped event at the main.cpp breakpoint", output);
            Assert.That(stopped.Reason, Is.EqualTo(StopReason.Breakpoint),
                $"Expected breakpoint stop, got {stopped.Reason} ({stopped.Description}; text: {stopped.Text}).\n" + output.Dump());
            Assert.That(debug.State, Is.EqualTo(DebugState.Paused));

            Assert.That(breakpointEvents.Any(e => e.Breakpoints.Any(
                    b => b.Verified && b.Line == TempNativeDebugProject.CppBreakpointLine)), Is.True,
                "No BreakpointsChanged event delivered a Verified==true breakpoint at line " +
                $"{TempNativeDebugProject.CppBreakpointLine}. Events:\n" + DumpBreakpointEvents(breakpointEvents) +
                "\n\n" + output.Dump());

            // ---- 4. Stack: top frame is main.cpp line 5 ----
            var frames = await WithTimeout(debug.GetStackTraceAsync(stopped.ThreadId), RequestTimeout, "stackTrace at breakpoint");
            Assert.That(frames, Is.Not.Empty, "stack trace was empty.\n" + output.Dump());
            Assert.That(frames[0].FilePath, Does.EndWith("main.cpp").IgnoreCase,
                "Top frame at the breakpoint is not main.cpp. Frames:\n" + DumpFrames(frames) + "\n\n" + output.Dump());
            Assert.That(frames[0].Line, Is.EqualTo(TempNativeDebugProject.CppBreakpointLine),
                $"Stopped in main.cpp but at line {frames[0].Line}. Frames:\n" + DumpFrames(frames));

            // ---- 5. Step over → line 6, on the REAL threadId the adapter assigned ----
            stoppedTcs = new TaskCompletionSource<StoppedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            await debug.StepOverAsync();
            var stepStopped = await AwaitStopAsync(stoppedTcs.Task, sessionDeadTcs.Task,
                "stopped event after step-over", output);
            Assert.That(stepStopped.Reason, Is.EqualTo(StopReason.Step),
                $"Expected step stop, got {stepStopped.Reason} ({stepStopped.Description}).\n" + output.Dump());

            var stepFrames = await WithTimeout(debug.GetStackTraceAsync(stepStopped.ThreadId), RequestTimeout, "stackTrace after step");
            Assert.That(stepFrames, Is.Not.Empty, "stack trace after step was empty.\n" + output.Dump());
            Assert.That(stepFrames[0].FilePath, Does.EndWith("main.cpp").IgnoreCase,
                "Step landed outside main.cpp. Frames:\n" + DumpFrames(stepFrames) + "\n\n" + output.Dump());
            Assert.That(stepFrames[0].Line, Is.EqualTo(TempNativeDebugProject.CppStepTargetLine),
                $"Step landed at line {stepFrames[0].Line}, expected {TempNativeDebugProject.CppStepTargetLine}. Frames:\n" +
                DumpFrames(stepFrames));

            // ---- 6. Locals: real C++ names (no demangling expectations) ----
            var scopes = await WithTimeout(debug.GetScopesAsync(stepFrames[0].Id), RequestTimeout, "scopes");
            Assert.That(scopes, Is.Not.Empty, "no scopes returned for the paused frame.\n" + output.Dump());

            var allVariables = new List<VariableInfo>();
            foreach (var scope in scopes)
            {
                var vars = await WithTimeout(debug.GetVariablesAsync(scope.VariablesReference),
                    RequestTimeout, $"variables for scope '{scope.Name}'");
                allVariables.AddRange(vars);
            }
            Assert.That(allVariables.Any(v => v.Name == "local"), Is.True,
                "Expected local variable 'local' in the frame. Variables:\n" + DumpVariables(allVariables));
            Assert.That(allVariables.Any(v => v.Name == "x"), Is.True,
                "Expected parameter 'x' in the frame. Variables:\n" + DumpVariables(allVariables));

            // ---- 7. Watch: x == 50 (CalculateScore(5) == 50 flowed into triple) ----
            var eval = await WithTimeout(debug.EvaluateAsync("x", stepFrames[0].Id), RequestTimeout, "evaluate x");
            Assert.That(eval.Result, Is.EqualTo("50"),
                $"evaluate('x') returned '{eval.Result}' (type {eval.Type}), expected '50'.\n" + output.Dump());

            // ---- 8. cpp_throw: arm the filter, continue, stop at the throw ----
            await WithTimeout(debug.SetExceptionBreakpointsAsync(new[] { "cpp_throw" }),
                RequestTimeout, "setExceptionBreakpoints cpp_throw");

            stoppedTcs = new TaskCompletionSource<StoppedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            await debug.ContinueAsync();
            var throwStopped = await AwaitStopAsync(stoppedTcs.Task, sessionDeadTcs.Task,
                "stopped event at the C++ throw (cpp_throw filter)", output);
            // A vocabulary difference here on lldb 19 (reason not mapping to Exception) is a
            // REPORT-worthy finding — the description/text carry the raw adapter wording.
            Assert.That(throwStopped.Reason, Is.EqualTo(StopReason.Exception),
                $"Expected exception stop at the throw, got {throwStopped.Reason} " +
                $"(description: '{throwStopped.Description}'; text: '{throwStopped.Text}').\n" + output.Dump());

            var throwFrames = await WithTimeout(debug.GetStackTraceAsync(throwStopped.ThreadId), RequestTimeout, "stackTrace at throw");
            Assert.That(throwFrames.Any(f =>
                    f.FilePath != null &&
                    f.FilePath.EndsWith("main.cpp", StringComparison.OrdinalIgnoreCase) &&
                    f.Line == TempNativeDebugProject.CppThrowLine), Is.True,
                $"No frame at the exception stop maps to main.cpp:{TempNativeDebugProject.CppThrowLine} (the throw). Frames:\n" +
                DumpFrames(throwFrames) + "\n\n" + output.Dump());

            // ---- 9. Clean shutdown: disconnect(terminateDebuggee) → zero orphans ----
            await WithTimeout(debug.StopDebuggingAsync(), TimeSpan.FromSeconds(30), "StopDebuggingAsync");
            Assert.That(debug.AdapterProcessId, Is.Not.Null);
            AssertProcessExits(debug.AdapterProcessId!.Value, TimeSpan.FromSeconds(10), "lldb-dap adapter process");
            AssertNoProcessesNamed(debuggeeName, TimeSpan.FromSeconds(10), output);
        }
        finally
        {
            try
            {
                await WithTimeout(debug.StopDebuggingAsync(), TimeSpan.FromSeconds(15), "StopDebuggingAsync cleanup");
            }
            catch { }
            debug.Dispose();
            if (debug.AdapterProcessId is int pid && ProcessIsAlive(pid))
            {
                try { Process.GetProcessById(pid).Kill(entireProcessTree: true); } catch { }
            }
            KillTreeByName(debuggeeName);
        }
    }

    // ------------------------------------------------------------------
    // (b) The .bas ring — #line-mapped breakpoint through the product stack
    // ------------------------------------------------------------------

    [Test]
    public async Task NativeDebug_BasBreakpoint_BindsStopsAndSteps()
    {
        RequireNativeDebugPrereqs();

        using var fixture = new TempNativeDebugProject();

        var (buildResult, buildOutput) = await BuildProjectAsync(fixture);
        AssertBuildSucceeded(buildResult, buildOutput);
        var debuggeeName = Path.GetFileNameWithoutExtension(buildResult.ExecutablePath!);

        var output = new RecordingOutputService();
        var debug = new DebugService(output, BuildRegistryLikeServiceConfiguration());

        var stoppedTcs = new TaskCompletionSource<StoppedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        debug.Stopped += (_, e) => stoppedTcs.TrySetResult(e);

        var sessionDeadTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        debug.StateChanged += (_, e) =>
        {
            if (e.NewState == DebugState.Stopped)
                sessionDeadTcs.TrySetResult(true);
        };

        var breakpointEvents = new ConcurrentQueue<BreakpointsChangedEventArgs>();
        debug.BreakpointsChanged += (_, e) => breakpointEvents.Enqueue(e);

        try
        {
            var config = new DebugConfiguration
            {
                Program = buildResult.ExecutablePath!,
                WorkingDirectory = fixture.RootDir,
                AdapterId = DebugAdapterDescriptor.LldbDapId
            };
            // Task 1's recorded path-form verdict: the OS-NATIVE BACKSLASH form bound
            // immediately — send the path exactly as the OS spells it.
            var breakpoints = new Dictionary<string, IEnumerable<SourceBreakpoint>>
            {
                [fixture.LogicBasFile] = new[]
                {
                    new SourceBreakpoint { Line = TempNativeDebugProject.BasBreakpointLine }
                }
            };

            using var startCts = new CancellationTokenSource(StartTimeout);
            var started = await debug.StartDebuggingAsync(config, breakpoints, startCts.Token);
            Assert.That(started, Is.True, "StartDebuggingAsync returned false.\n" + output.Dump());

            var stopped = await AwaitStopAsync(stoppedTcs.Task, sessionDeadTcs.Task,
                "stopped event at the Logic.bas breakpoint", output);
            Assert.That(stopped.Reason, Is.EqualTo(StopReason.Breakpoint),
                $"Expected breakpoint stop, got {stopped.Reason} ({stopped.Description}; text: {stopped.Text}).\n" + output.Dump());

            // Bind-verified, on the OS-native path form.
            Assert.That(breakpointEvents.Any(e => e.Breakpoints.Any(
                    b => b.Verified && b.Line == TempNativeDebugProject.BasBreakpointLine)), Is.True,
                "No BreakpointsChanged event delivered a Verified==true breakpoint at Logic.bas line " +
                $"{TempNativeDebugProject.BasBreakpointLine}. Events:\n" + DumpBreakpointEvents(breakpointEvents) +
                "\n\n" + output.Dump());

            // Top frame presents as Logic.bas at the .bas line — not generated glue.
            var frames = await WithTimeout(debug.GetStackTraceAsync(stopped.ThreadId), RequestTimeout, "stackTrace at .bas breakpoint");
            Assert.That(frames, Is.Not.Empty, "stack trace was empty.\n" + output.Dump());
            Assert.That(frames[0].FilePath, Does.EndWith("Logic.bas").IgnoreCase,
                "Top frame at the .bas breakpoint is not Logic.bas. Frames:\n" + DumpFrames(frames) + "\n\n" + output.Dump());
            Assert.That(frames[0].Line, Is.EqualTo(TempNativeDebugProject.BasBreakpointLine),
                $"Stopped in Logic.bas but at line {frames[0].Line}. Frames:\n" + DumpFrames(frames));

            // Step-next stays in .bas coordinates: `Return hits * 10` lowers to a store
            // (line 9) plus `return t0;` on the End Function line (10) — the step lands
            // there, never in generated glue or main.cpp.
            stoppedTcs = new TaskCompletionSource<StoppedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            await debug.StepOverAsync();
            var stepStopped = await AwaitStopAsync(stoppedTcs.Task, sessionDeadTcs.Task,
                "stopped event after step-over in Logic.bas", output);
            Assert.That(stepStopped.Reason, Is.EqualTo(StopReason.Step),
                $"Expected step stop, got {stepStopped.Reason} ({stepStopped.Description}).\n" + output.Dump());

            var stepFrames = await WithTimeout(debug.GetStackTraceAsync(stepStopped.ThreadId), RequestTimeout, "stackTrace after .bas step");
            Assert.That(stepFrames, Is.Not.Empty, "stack trace after step was empty.\n" + output.Dump());
            Assert.That(stepFrames[0].FilePath, Does.EndWith("Logic.bas").IgnoreCase,
                "Step left .bas coordinates (generated glue or main.cpp). Frames:\n" + DumpFrames(stepFrames) + "\n\n" + output.Dump());
            Assert.That(stepFrames[0].Line, Is.EqualTo(TempNativeDebugProject.BasStepTargetLine),
                $"Step landed in Logic.bas but at line {stepFrames[0].Line}, expected {TempNativeDebugProject.BasStepTargetLine}. Frames:\n" +
                DumpFrames(stepFrames));

            // Clean shutdown — same zero-orphans contract as the main ring.
            await WithTimeout(debug.StopDebuggingAsync(), TimeSpan.FromSeconds(30), "StopDebuggingAsync");
            Assert.That(debug.AdapterProcessId, Is.Not.Null);
            AssertProcessExits(debug.AdapterProcessId!.Value, TimeSpan.FromSeconds(10), "lldb-dap adapter process");
            AssertNoProcessesNamed(debuggeeName, TimeSpan.FromSeconds(10), output);
        }
        finally
        {
            try
            {
                await WithTimeout(debug.StopDebuggingAsync(), TimeSpan.FromSeconds(15), "StopDebuggingAsync cleanup");
            }
            catch { }
            debug.Dispose();
            if (debug.AdapterProcessId is int pid && ProcessIsAlive(pid))
            {
                try { Process.GetProcessById(pid).Kill(entireProcessTree: true); } catch { }
            }
            KillTreeByName(debuggeeName);
        }
    }

    // ------------------------------------------------------------------
    // (c) The DWARF route — g++-built exe, product debug path only
    // ------------------------------------------------------------------

    [Test]
    public async Task NativeDebug_DwarfRoute_GppBuild_BreakpointStepAndLocals()
    {
        // This leg deliberately bypasses the product BUILD path (which would probe MSVC
        // first): the claim under test is the DEBUG route — registry → lldb-dap
        // descriptor → DapSession reading DWARF. Clean machines skip.
        if (!File.Exists(GppPath))
            Assert.Ignore($"g++ not found at {GppPath} — the DWARF route needs the winlibs toolchain.");
        if (LldbDapLocator.Resolve(null) == null)
            Assert.Ignore("lldb-dap not found by the product locator chain — the native debug e2e needs it installed.");

        using var fixture = new TempDwarfFixture();

        // ---- 1. Compile directly with g++ (DWARF, no optimization) ----
        // -gdwarf-4 is LOAD-BEARING: winlibs g++ 14.2's default -g emits DWARF5, and
        // winlibs lldb 19.1.7 resolves ZERO breakpoint locations against it (probed via
        // `lldb --batch`: every path form — native, forward-slash, bare filename — binds
        // 0 locations on the DWARF5 build, and binds immediately on the DWARF4 build).
        // The claim under test is the product DEBUG route reading DWARF, not lldb-19's
        // DWARF5 parser gap; the pinned 22.x adapter zip is where that gap closes.
        var gppPsi = new ProcessStartInfo(GppPath, "-g -gdwarf-4 -O0 -o dwarf.exe dwarf.cpp")
        {
            WorkingDirectory = fixture.RootDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using (var gpp = Process.Start(gppPsi)!)
        {
            try
            {
                // Drain both pipes from the start — a compile failure chatty enough to
                // fill the pipe buffer must not deadlock the WaitForExit below.
                var gppStderrTask = gpp.StandardError.ReadToEndAsync();
                var gppStdoutTask = gpp.StandardOutput.ReadToEndAsync();
                if (!gpp.WaitForExit(60_000))
                {
                    try { gpp.Kill(entireProcessTree: true); } catch { }
                    Assert.Fail("g++ did not finish compiling dwarf.cpp within 60s.");
                }
                var gppStderr = await gppStderrTask;
                await gppStdoutTask;
                Assert.That(gpp.ExitCode, Is.EqualTo(0), $"g++ failed (exit {gpp.ExitCode}).\nstderr:\n{gppStderr}");
            }
            finally
            {
                try { if (!gpp.HasExited) gpp.Kill(entireProcessTree: true); } catch { }
            }
        }
        Assert.That(File.Exists(fixture.ExePath), Is.True, $"g++ reported success but {fixture.ExePath} does not exist.");

        // ---- 2. The product session path, exactly as the main ring ----
        var output = new RecordingOutputService();
        var debug = new DebugService(output, BuildRegistryLikeServiceConfiguration());

        var stoppedTcs = new TaskCompletionSource<StoppedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        debug.Stopped += (_, e) => stoppedTcs.TrySetResult(e);

        var sessionDeadTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        debug.StateChanged += (_, e) =>
        {
            if (e.NewState == DebugState.Stopped)
                sessionDeadTcs.TrySetResult(true);
        };

        try
        {
            var config = new DebugConfiguration
            {
                Program = fixture.ExePath,
                WorkingDirectory = fixture.RootDir,
                AdapterId = DebugAdapterDescriptor.LldbDapId
            };
            var breakpoints = new Dictionary<string, IEnumerable<SourceBreakpoint>>
            {
                [fixture.DwarfCppFile] = new[]
                {
                    new SourceBreakpoint { Line = TempDwarfFixture.BreakpointLine }
                }
            };

            using var startCts = new CancellationTokenSource(StartTimeout);
            var started = await debug.StartDebuggingAsync(config, breakpoints, startCts.Token);
            Assert.That(started, Is.True, "StartDebuggingAsync returned false.\n" + output.Dump());

            var stopped = await AwaitStopAsync(stoppedTcs.Task, sessionDeadTcs.Task,
                "stopped event at the dwarf.cpp breakpoint", output);
            Assert.That(stopped.Reason, Is.EqualTo(StopReason.Breakpoint),
                $"Expected breakpoint stop, got {stopped.Reason} ({stopped.Description}; text: {stopped.Text}).\n" + output.Dump());

            var frames = await WithTimeout(debug.GetStackTraceAsync(stopped.ThreadId), RequestTimeout, "stackTrace at DWARF breakpoint");
            Assert.That(frames, Is.Not.Empty, "stack trace was empty.\n" + output.Dump());
            Assert.That(frames[0].FilePath, Does.EndWith("dwarf.cpp").IgnoreCase,
                "Top frame at the breakpoint is not dwarf.cpp. Frames:\n" + DumpFrames(frames) + "\n\n" + output.Dump());
            Assert.That(frames[0].Line, Is.EqualTo(TempDwarfFixture.BreakpointLine),
                $"Stopped in dwarf.cpp but at line {frames[0].Line}. Frames:\n" + DumpFrames(frames));

            stoppedTcs = new TaskCompletionSource<StoppedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            await debug.StepOverAsync();
            var stepStopped = await AwaitStopAsync(stoppedTcs.Task, sessionDeadTcs.Task,
                "stopped event after step-over in dwarf.cpp", output);
            Assert.That(stepStopped.Reason, Is.EqualTo(StopReason.Step),
                $"Expected step stop, got {stepStopped.Reason} ({stepStopped.Description}).\n" + output.Dump());

            var stepFrames = await WithTimeout(debug.GetStackTraceAsync(stepStopped.ThreadId), RequestTimeout, "stackTrace after DWARF step");
            Assert.That(stepFrames, Is.Not.Empty, "stack trace after step was empty.\n" + output.Dump());
            Assert.That(stepFrames[0].FilePath, Does.EndWith("dwarf.cpp").IgnoreCase,
                "Step landed outside dwarf.cpp. Frames:\n" + DumpFrames(stepFrames) + "\n\n" + output.Dump());
            Assert.That(stepFrames[0].Line, Is.EqualTo(TempDwarfFixture.StepTargetLine),
                $"Step landed at line {stepFrames[0].Line}, expected {TempDwarfFixture.StepTargetLine}. Frames:\n" +
                DumpFrames(stepFrames));

            // Locals through DWARF.
            var scopes = await WithTimeout(debug.GetScopesAsync(stepFrames[0].Id), RequestTimeout, "scopes");
            Assert.That(scopes, Is.Not.Empty, "no scopes returned for the paused frame.\n" + output.Dump());

            var allVariables = new List<VariableInfo>();
            foreach (var scope in scopes)
            {
                var vars = await WithTimeout(debug.GetVariablesAsync(scope.VariablesReference),
                    RequestTimeout, $"variables for scope '{scope.Name}'");
                allVariables.AddRange(vars);
            }
            Assert.That(allVariables.Any(v => v.Name == "local"), Is.True,
                "Expected local variable 'local' in the frame (DWARF). Variables:\n" + DumpVariables(allVariables));
            Assert.That(allVariables.Any(v => v.Name == "x"), Is.True,
                "Expected parameter 'x' in the frame (DWARF). Variables:\n" + DumpVariables(allVariables));

            // Clean shutdown — same zero-orphans contract as the other rings.
            await WithTimeout(debug.StopDebuggingAsync(), TimeSpan.FromSeconds(30), "StopDebuggingAsync");
            Assert.That(debug.AdapterProcessId, Is.Not.Null);
            AssertProcessExits(debug.AdapterProcessId!.Value, TimeSpan.FromSeconds(10), "lldb-dap adapter process");
            AssertNoProcessesNamed("dwarf", TimeSpan.FromSeconds(10), output);
        }
        finally
        {
            try
            {
                await WithTimeout(debug.StopDebuggingAsync(), TimeSpan.FromSeconds(15), "StopDebuggingAsync cleanup");
            }
            catch { }
            debug.Dispose();
            if (debug.AdapterProcessId is int pid && ProcessIsAlive(pid))
            {
                try { Process.GetProcessById(pid).Kill(entireProcessTree: true); } catch { }
            }
            KillTreeByName("dwarf");
        }
    }
}
