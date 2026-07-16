using System.Collections.Concurrent;
using System.Diagnostics;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Serialization;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Integration;

/// <summary>
/// "IDE in anger" end-to-end integration tests.
///
/// These tests exercise the REAL service chain a user hits when working in the IDE:
///   open project (ProjectSerializer) → build (BuildService → BasicLang compiler →
///   dotnet build) → language services (LanguageService → `dotnet BasicLang.dll --lsp`
///   over stdio) → debug (DebugService → `dotnet BasicLang.dll --debug-adapter` DAP
///   over stdio → ICorDebug) — plus a smoke check that the shipped IDE executable
///   starts without crashing.
///
/// Every test provisions its own temp BasicLang project (async function + lambda +
/// cross-file call), uses generous-but-bounded timeouts, and guarantees that no
/// spawned process outlives the test.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("IdeInAnger")]
public class IdeInAngerTests
{
    // ------------------------------------------------------------------
    // Temp project fixture
    // ------------------------------------------------------------------

    /// <summary>
    /// Provisions a throw-away BasicLang project on disk:
    ///   E2EApp.blproj  – project file (CSharp backend, console exe)
    ///   Program.bas    – async function (Await), lambda, cross-file call
    ///   MathHelpers.bas– public function called from Program.bas
    ///   Broken.bas     – NOT in the project; used for LSP diagnostics
    /// </summary>
    private sealed class TempBasicLangProject : IDisposable
    {
        public string RootDir { get; }
        public string ProjectFile { get; }
        public string ProgramFile { get; }
        public string HelpersFile { get; }
        public string BrokenFile { get; }

        /// <summary>1-based line of `Dim answer As Integer = AddNumbers(40, 2)` — the
        /// first statement AFTER the Await (executes on the async continuation).</summary>
        public const int BreakpointLineAfterAwait = 6;

        /// <summary>1-based line of the `missingFunction` semantic error in Broken.bas.</summary>
        public const int BrokenErrorLine = 2;

        // NOTE: line numbers are load-bearing (breakpoints, diagnostics assertions).
        private const string ProgramSource =
@"Import MathHelpers

Async Function DoWorkAsync() As Task(Of Integer)
    Console.WriteLine(""before await"")
    Await Task.Delay(200)
    Dim answer As Integer = AddNumbers(40, 2)
    Console.WriteLine(""after await: "" & answer)
    Return answer
End Function

Sub Main()
    Console.WriteLine(""start"")
    Dim square As Func(Of Integer, Integer) = Function(x As Integer) x * x
    Console.WriteLine(""square = "" & square(6))
    Dim t As Task(Of Integer) = DoWorkAsync()
    Dim result As Object = t.Result
    Console.WriteLine(""result = "" & result)
End Sub
";

        private const string HelpersSource =
@"Public Function AddNumbers(a As Integer, b As Integer) As Integer
    Return a + b
End Function
";

        private const string BrokenSource =
@"Sub Main()
    Dim x As Integer = missingFunction(1)
End Sub
";

        private const string ProjectXml =
@"<?xml version=""1.0"" encoding=""utf-8""?>
<BasicLangProject Version=""1.0"">
  <PropertyGroup>
    <ProjectName>E2EApp</ProjectName>
    <OutputType>Exe</OutputType>
    <RootNamespace>E2EApp</RootNamespace>
    <TargetBackend>CSharp</TargetBackend>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include=""Program.bas"" />
    <Compile Include=""MathHelpers.bas"" />
  </ItemGroup>
</BasicLangProject>
";

        public TempBasicLangProject(bool broken = false)
        {
            RootDir = Path.Combine(Path.GetTempPath(), "vgs-e2e-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootDir);

            ProjectFile = Path.Combine(RootDir, "E2EApp.blproj");
            ProgramFile = Path.Combine(RootDir, "Program.bas");
            HelpersFile = Path.Combine(RootDir, "MathHelpers.bas");
            BrokenFile = Path.Combine(RootDir, "Broken.bas");

            File.WriteAllText(ProjectFile, ProjectXml);
            // A "broken" project swaps in the semantically-invalid source for Program.bas
            File.WriteAllText(ProgramFile, broken ? BrokenSource : ProgramSource);
            File.WriteAllText(HelpersFile, HelpersSource);
            File.WriteAllText(BrokenFile, BrokenSource);
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
    /// Thread-safe IOutputService that records everything, so test failures can
    /// include the real build/LSP/DAP output instead of a bare assertion message.
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

    /// <summary>Builds the temp project through the real BuildService and returns the result.</summary>
    private static async Task<(BuildResult Result, RecordingOutputService Output)> BuildProjectAsync(
        TempBasicLangProject fixture)
    {
        var output = new RecordingOutputService();
        var buildService = new BuildService(output);
        var serializer = new ProjectSerializer();

        var project = await serializer.LoadAsync(fixture.ProjectFile);
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var result = await buildService.BuildProjectAsync(project, cts.Token);
        return (result, output);
    }

    // ------------------------------------------------------------------
    // (a) BUILD — open project → BuildService → exe on disk / diagnostics
    // ------------------------------------------------------------------

    [Test]
    public async Task Build_AsyncLambdaCrossFileProject_ProducesRunnableExe()
    {
        using var fixture = new TempBasicLangProject();

        var (result, output) = await BuildProjectAsync(fixture);

        Assert.That(result.Success, Is.True,
            "Build failed.\nDiagnostics:\n" +
            string.Join("\n", result.Diagnostics.Select(d => $"{d.FilePath}({d.Line},{d.Column}): {d.Severity} {d.Id}: {d.Message}")) +
            "\n\nBuild output:\n" + output.Dump());

        Assert.That(result.ExecutablePath, Is.Not.Null.And.Not.Empty,
            "Build succeeded but no executable path was reported.\n" + output.Dump());
        Assert.That(File.Exists(result.ExecutablePath), Is.True,
            $"Reported executable does not exist: {result.ExecutablePath}");

        // Run the produced exe end-to-end: async continuation, lambda and the
        // cross-file call must all actually execute.
        var psi = new ProcessStartInfo(result.ExecutablePath!)
        {
            WorkingDirectory = Path.GetDirectoryName(result.ExecutablePath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi)!;
        try
        {
            var stdout = await WithTimeout(proc.StandardOutput.ReadToEndAsync(), TimeSpan.FromSeconds(30), "program stdout");
            var stderr = await proc.StandardError.ReadToEndAsync();
            Assert.That(proc.WaitForExit(30_000), Is.True, "Built program did not exit within 30s");

            Assert.That(stdout, Does.Contain("square = 36"), $"lambda did not run.\nstdout:\n{stdout}\nstderr:\n{stderr}");
            Assert.That(stdout, Does.Contain("after await: 42"), $"async continuation / cross-file call did not run.\nstdout:\n{stdout}\nstderr:\n{stderr}");
            Assert.That(stdout, Does.Contain("result = 42"), $"Task result was wrong.\nstdout:\n{stdout}\nstderr:\n{stderr}");
            Assert.That(proc.ExitCode, Is.EqualTo(0), $"Program exited with {proc.ExitCode}.\nstderr:\n{stderr}");
        }
        finally
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
        }
    }

    [Test]
    public async Task Build_SemanticError_FailsWithFileLineColumnDiagnostic()
    {
        using var fixture = new TempBasicLangProject(broken: true);

        var (result, output) = await BuildProjectAsync(fixture);

        Assert.That(result.Success, Is.False, "Build of semantically-broken project unexpectedly succeeded.\n" + output.Dump());
        Assert.That(result.ErrorCount, Is.GreaterThan(0), "Failed build reported no error diagnostics.\n" + output.Dump());

        var error = result.Errors.FirstOrDefault(e =>
            e.FilePath != null &&
            e.FilePath.EndsWith("Program.bas", StringComparison.OrdinalIgnoreCase));
        Assert.That(error, Is.Not.Null,
            "No error diagnostic carried the source file path.\nDiagnostics:\n" +
            string.Join("\n", result.Diagnostics.Select(d => $"{d.FilePath}({d.Line},{d.Column}): {d.Severity} {d.Id}: {d.Message}")));

        Assert.That(error!.Line, Is.EqualTo(TempBasicLangProject.BrokenErrorLine),
            $"Diagnostic line mismatch: {error.FilePath}({error.Line},{error.Column}): {error.Message}");
        Assert.That(error.Column, Is.GreaterThan(0),
            $"Diagnostic column missing: {error.FilePath}({error.Line},{error.Column}): {error.Message}");
    }

    // ------------------------------------------------------------------
    // (b) LSP — start server → didOpen → diagnostics / hover / completion → stop
    // ------------------------------------------------------------------

    [Test]
    public async Task Lsp_RoundTrip_DiagnosticsHoverCompletion_AndServerExitsOnStop()
    {
        using var fixture = new TempBasicLangProject();
        var output = new RecordingOutputService();
        var lsp = new LanguageService(output);

        try
        {
            using (var startCts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            {
                await lsp.StartAsync(startCts.Token);
            }
            Assert.That(lsp.IsConnected, Is.True, "LanguageService failed to connect.\n" + output.Dump());
            Assert.That(lsp.ServerProcessId, Is.Not.Null, "Server process id was not recorded");
            Assert.That(ProcessIsAlive(lsp.ServerProcessId!.Value), Is.True, "LSP server process is not running after StartAsync");

            // --- capabilities: the initialize result must be CAPTURED, not discarded.
            // These pair with the hover/completion round-trips further down: what the
            // real server ADVERTISES here has to match what it demonstrably DOES there.
            // (Unit tests cover the parser; only a live handshake proves we parse what
            // the actual --lsp server sends, which returns providers as objects.)
            Assert.That(lsp.Capabilities, Is.Not.Null, "initialize result was not captured.\n" + output.Dump());
            Assert.Multiple(() =>
            {
                Assert.That(lsp.Capabilities!.HasHoverProvider, Is.True,
                    "server must advertise hover — the hover round-trip below proves it provides it");
                Assert.That(lsp.Capabilities!.HasCompletionProvider, Is.True,
                    "server must advertise completion — the completion round-trip below proves it provides it");
                Assert.That(lsp.Capabilities!.PositionEncoding, Is.EqualTo("utf-16"),
                    "the real server must leave us on utf-16; this client's column math is utf-16 only");
            });

            // --- diagnostics: open a file with a semantic error, expect a published error
            var diagnosticsTcs = new TaskCompletionSource<DiagnosticsEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            lsp.DiagnosticsReceived += (_, e) =>
            {
                if (e.Uri.EndsWith("Broken.bas", StringComparison.OrdinalIgnoreCase) && e.Diagnostics.Count > 0)
                    diagnosticsTcs.TrySetResult(e);
            };

            await lsp.OpenDocumentAsync(fixture.BrokenFile, File.ReadAllText(fixture.BrokenFile));
            var diags = await WithTimeout(diagnosticsTcs.Task, TimeSpan.FromSeconds(30), "publishDiagnostics for Broken.bas");
            Assert.That(diags.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), Is.True,
                "Expected at least one error diagnostic for Broken.bas, got: " +
                string.Join("; ", diags.Diagnostics.Select(d => $"{d.Severity} {d.Message}")));
            Assert.That(diags.Diagnostics.First(d => d.Severity == DiagnosticSeverity.Error).Line,
                Is.EqualTo(TempBasicLangProject.BrokenErrorLine), "diagnostic line number");

            // --- hover: over the DoWorkAsync() call site in Main (line 15, col 33).
            // (Cross-file symbols like AddNumbers currently return null hover — the LSP
            // server analyzes each open document in isolation; tracked as a known gap.)
            await lsp.OpenDocumentAsync(fixture.ProgramFile, File.ReadAllText(fixture.ProgramFile));

            // The server analyzes the document asynchronously after didOpen, so retry
            // briefly (an interactive user gets the same effect from hover debounce).
            HoverInfo? hover = null;
            var hoverDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (hover == null && DateTime.UtcNow < hoverDeadline)
            {
                using var hoverCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                hover = await lsp.GetHoverAsync(fixture.ProgramFile, line: 15, column: 33, hoverCts.Token);
                if (hover == null) await Task.Delay(500);
            }
            Assert.That(hover, Is.Not.Null, "hover over 'DoWorkAsync' call returned null (30s of retries).\n" + output.Dump());
            Assert.That(hover!.Contents, Does.Contain("DoWorkAsync"),
                $"hover contents did not describe the function: '{hover.Contents}'");

            // --- completion: inside Main's body
            using (var complCts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            {
                var completions = await lsp.GetCompletionsAsync(fixture.ProgramFile, line: 12, column: 5, complCts.Token);
                Assert.That(completions, Is.Not.Empty, "completion returned no items.\n" + output.Dump());
            }

            // --- shutdown: server process must actually exit (Wave-4 orphan fix)
            var serverPid = lsp.ServerProcessId!.Value;
            await WithTimeout(lsp.StopAsync(), TimeSpan.FromSeconds(15), "LanguageService.StopAsync");
            Assert.That(lsp.IsConnected, Is.False);

            // Capabilities were populated by the live handshake above; once disconnected
            // they must read null, or callers deciding "can I ask for hover?" get a yes
            // from a server that is gone. StopAsync deliberately does NOT clear a field
            // (CleanupConnection is not on this path) — the property derives from
            // IsConnected precisely so every teardown path gets this for free.
            Assert.That(lsp.Capabilities, Is.Null,
                "capabilities must not remain readable after disconnect");

            AssertProcessExits(serverPid, TimeSpan.FromSeconds(10), "LSP server process");
        }
        finally
        {
            lsp.Dispose();
            if (lsp.ServerProcessId is int pid && ProcessIsAlive(pid))
            {
                try { Process.GetProcessById(pid).Kill(entireProcessTree: true); } catch { }
            }
        }
    }

    // ------------------------------------------------------------------
    // (c) DEBUG — build exe → breakpoint after Await → stack/locals → run to end
    // ------------------------------------------------------------------

    [Test]
    public async Task Debug_BreakpointAfterAwait_StackAndLocals_ThenCleanShutdown()
    {
        using var fixture = new TempBasicLangProject();

        var (buildResult, buildOutput) = await BuildProjectAsync(fixture);
        Assert.That(buildResult.Success, Is.True, "Prerequisite build failed.\n" + buildOutput.Dump());
        Assert.That(buildResult.ExecutablePath, Is.Not.Null, "Prerequisite build produced no exe");

        var output = new RecordingOutputService();
        var debug = new DebugService(output);

        var stoppedTcs = new TaskCompletionSource<StoppedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        debug.Stopped += (_, e) => stoppedTcs.TrySetResult(e);

        var stateStoppedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        debug.StateChanged += (_, e) =>
        {
            if (e.NewState == DebugState.Stopped)
                stateStoppedTcs.TrySetResult(true);
        };

        try
        {
            var config = new DebugConfiguration
            {
                Program = buildResult.ExecutablePath!,
                WorkingDirectory = Path.GetDirectoryName(buildResult.ExecutablePath!)!
            };
            var breakpoints = new Dictionary<string, IEnumerable<SourceBreakpoint>>
            {
                [fixture.ProgramFile] = new[]
                {
                    new SourceBreakpoint { Line = TempBasicLangProject.BreakpointLineAfterAwait }
                }
            };

            using var startCts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var started = await debug.StartDebuggingAsync(config, breakpoints, startCts.Token);
            Assert.That(started, Is.True, "StartDebuggingAsync returned false.\n" + output.Dump());

            // Wait for the breakpoint on the line AFTER the Await (continuation thread).
            var stopped = await WithTimeout(stoppedTcs.Task, TimeSpan.FromSeconds(60), "stopped event at breakpoint");
            Assert.That(stopped.Reason, Is.EqualTo(StopReason.Breakpoint),
                $"Expected breakpoint stop, got {stopped.Reason} ({stopped.Description}).\n" + output.Dump());
            Assert.That(debug.State, Is.EqualTo(DebugState.Paused));

            // Stack trace must show the .bas source location, not generated C#.
            var frames = await WithTimeout(debug.GetStackTraceAsync(stopped.ThreadId), TimeSpan.FromSeconds(30), "stackTrace");
            Assert.That(frames, Is.Not.Empty, "stack trace was empty.\n" + output.Dump());
            var basFrame = frames.FirstOrDefault(f =>
                f.FilePath != null && f.FilePath.EndsWith("Program.bas", StringComparison.OrdinalIgnoreCase));
            Assert.That(basFrame, Is.Not.Null,
                "No stack frame mapped to Program.bas. Frames:\n" +
                string.Join("\n", frames.Select(f => $"  {f.Name} @ {f.FilePath}:{f.Line}")));
            Assert.That(basFrame!.Line, Is.EqualTo(TempBasicLangProject.BreakpointLineAfterAwait),
                $"Stopped at wrong line. Frame: {basFrame.Name} @ {basFrame.FilePath}:{basFrame.Line}");

            // Locals must include the demangled user variable name (async state
            // machines hoist locals as '<answer>5__N' fields — the inspector must
            // present the plain name).
            var scopes = await WithTimeout(debug.GetScopesAsync(basFrame.Id), TimeSpan.FromSeconds(30), "scopes");
            Assert.That(scopes, Is.Not.Empty, "no scopes returned for the paused frame");

            var allVariables = new List<VariableInfo>();
            foreach (var scope in scopes)
            {
                var vars = await WithTimeout(debug.GetVariablesAsync(scope.VariablesReference), TimeSpan.FromSeconds(30), $"variables for scope '{scope.Name}'");
                allVariables.AddRange(vars);
            }

            var varDump = string.Join("\n", allVariables.Select(v => $"  {v.Name} = {v.Value} ({v.Type})"));
            Assert.That(allVariables.Any(v => v.Name == "answer"), Is.True,
                "Expected demangled local 'answer' in the async frame. Variables:\n" + varDump);
            Assert.That(allVariables.Any(v => v.Name.Contains('<') || v.Name.Contains("__")), Is.False,
                "Compiler-mangled variable names leaked into the variables view:\n" + varDump);

            // Continue → program runs to completion → terminated → Stopped state.
            await debug.ContinueAsync();
            await WithTimeout(stateStoppedTcs.Task, TimeSpan.FromSeconds(60), "debug session termination after continue");
            Assert.That(debug.State, Is.EqualTo(DebugState.Stopped));

            // The adapter process (and its debuggee child) must be gone.
            Assert.That(debug.AdapterProcessId, Is.Not.Null);
            AssertProcessExits(debug.AdapterProcessId!.Value, TimeSpan.FromSeconds(10), "debug adapter process");
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
        }
    }

    // ------------------------------------------------------------------
    // (d) IDE LAUNCH SMOKE — the shipped exe must survive startup
    // ------------------------------------------------------------------

    [Test]
    public void IdeExecutable_LaunchSmoke_SurvivesStartup()
    {
        // Skip when there is no interactive desktop (services / session 0).
        if (!Environment.UserInteractive || Process.GetCurrentProcess().SessionId == 0)
        {
            Assert.Ignore("No interactive desktop session available — skipping IDE launch smoke test.");
        }

        // Test assembly lives at <root>\VisualGameStudio.Tests\bin\<cfg>\net8.0\
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var idePath = Path.Combine(root, "IDE", "VisualGameStudio.exe");
        if (!File.Exists(idePath))
        {
            Assert.Ignore($"IDE executable not found at {idePath} — skipping smoke test.");
        }

        var psi = new ProcessStartInfo(idePath)
        {
            WorkingDirectory = Path.Combine(root, "IDE"),
            UseShellExecute = false,
            CreateNoWindow = false
        };

        Process? proc = null;
        try
        {
            proc = Process.Start(psi);
            Assert.That(proc, Is.Not.Null, "Process.Start returned null for the IDE exe");

            // Watch it for ~10 seconds: a startup crash exits within this window.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                if (proc!.HasExited)
                {
                    Assert.Fail($"IDE process exited during startup with exit code {proc.ExitCode}.");
                }
                Thread.Sleep(500);
            }

            Assert.That(proc!.HasExited, Is.False, "IDE process crashed on startup");
        }
        finally
        {
            if (proc != null)
            {
                try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
                try { proc.WaitForExit(10_000); } catch { }
                proc.Dispose();
            }
        }
    }
}
