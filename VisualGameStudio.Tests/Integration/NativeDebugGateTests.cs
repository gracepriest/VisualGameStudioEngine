using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Serialization;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Integration;

/// <summary>
/// Phase 4 Task 1 — the Step-0 GATE: can lldb-dap bind a breakpoint on a <c>.bas</c> line
/// through the <c>#line</c> directives Task 0 put in the generated C++ (MSVC <c>/Zi</c> → PDB),
/// stop there, and step to the NEXT <c>.bas</c> statement?
///
/// <para>
/// This fixture predates the productized <c>DapSession</c> — it hand-rolls a minimal raw-stdio
/// DAP driver (<see cref="RawDapClient"/>) from the proven transport shapes in
/// <c>DebugService</c> (the BOM-less <see cref="ProcessStartInfo"/> block and the Latin1
/// Content-Length framing reader, both copied verbatim — they each carry a hard-won fix).
/// The handshake ORDER here is deliberately different from <c>DebugService</c>'s own (which
/// awaits the <c>launch</c> response before <c>configurationDone</c> — wrong for lldb-dap):
/// initialize → launch-in-flight → <c>initialized</c> event → setBreakpoints →
/// configurationDone → launch response. This ordering is the REFERENCE IMPLEMENTATION the
/// later productization task copies.
/// </para>
///
/// <para>
/// The temp project is a real MIXED project (spec §7): multi-TU emission and the
/// generated-header boundary must be under the gate — a mixed-only failure (breakpoints
/// binding in a single TU but not across split <c>.g.cpp</c> files) must not produce a
/// false PASS at the decision checkpoint.
/// </para>
///
/// <para>
/// Skips (never fails) when the pinned winlibs lldb-dap or a C++ toolchain is absent.
/// ⚠ lldb-dap discovery is an EXISTENCE check only — spawning <c>lldb-dap --version</c>
/// parks on stdin and hangs forever.
/// </para>
/// </summary>
[TestFixture]
[NonParallelizable] // spawns lldb-dap + a native debuggee; also runs a real MSVC build
[Category("NativeDebugGate")]
public class NativeDebugGateTests
{
    /// <summary>
    /// lldb-dap 19.1.7 (winlibs mingw64 build, self-contained: liblldb.dll +
    /// libpython3.9.dll beside it). Deliberately OFF PATH — probed at this literal path.
    /// </summary>
    private const string LldbDapPath = @"C:\winlibs\mingw64\bin\lldb-dap.exe";

    /// <summary>Per-response ceiling. Generous by design — never an expectation.</summary>
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Ceiling on launch completion and on each stop (breakpoint / step).</summary>
    private static readonly TimeSpan LaunchStopTimeout = TimeSpan.FromSeconds(60);

    // ------------------------------------------------------------------
    // Temp project fixture — a real MIXED project (Logic.bas + main.cpp)
    // ------------------------------------------------------------------

    private sealed class TempMixedNativeProject : IDisposable
    {
        public string RootDir { get; }
        public string ProjectFile { get; }
        public string LogicBasFile { get; }
        public string MainCppFile { get; }

        /// <summary>1-based line of <c>Dim total ...</c> — the breakpoint target.</summary>
        public const int BreakpointLine = 2;

        /// <summary>1-based line of <c>Dim doubled ...</c> — where one <c>next</c> must land.</summary>
        public const int StepTargetLine = 3;

        // NOTE: line numbers are load-bearing (the breakpoint and step-target
        // assertions pin BreakpointLine/StepTargetLine against this exact text).
        private const string LogicBas =
@"Function AddNumbers(a As Integer, b As Integer) As Integer
    Dim total As Integer = a + b
    Dim doubled As Integer = total * 2
    Return doubled
End Function
";

        private const string MainCpp =
@"#include ""Logic.g.h""
int main() {
    return AddNumbers(40, 2) == 84 ? 0 : 1;
}
";

        // The ClangdE2ETests mixed shape: no <Compile> items — sources are discovered
        // from the project directory. Debug is the default build configuration, so the
        // generated C++ carries #line directives (Task 0) and MSVC compiles with /Zi.
        private const string ProjectXml =
@"<BasicLangProject Version=""1.0"">
  <PropertyGroup>
    <ProjectName>App</ProjectName>
    <OutputType>Exe</OutputType>
    <Language>Cpp</Language>
    <TargetBackend>Cpp</TargetBackend>
  </PropertyGroup>
</BasicLangProject>
";

        public TempMixedNativeProject()
        {
            RootDir = Path.Combine(Path.GetTempPath(), "vgs-dapgate-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootDir);

            ProjectFile = Path.Combine(RootDir, "App.blproj");
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
    /// Thread-safe IOutputService that records everything, so test failures can
    /// include the real build output instead of a bare assertion message.
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
    // Minimal raw-stdio DAP driver — private to this fixture by design.
    // Transport shapes are copied VERBATIM from DebugService (BOM-less stdin/
    // stdout ProcessStartInfo; Latin1 byte-exact Content-Length framing); the
    // request/event plumbing is a plain seq counter + TCS tables.
    // ------------------------------------------------------------------

    private sealed class RawDapClient : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly Process _process;
        private readonly StreamWriter _writer;
        private readonly StreamReader _reader;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly object _lock = new();
        private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pendingRequests = new();
        private readonly List<(string[] Names, TaskCompletionSource<JsonElement> Tcs)> _eventWaiters = new();
        private readonly List<JsonElement> _eventBacklog = new();
        private readonly ConcurrentQueue<string> _transcript = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _readTask;
        private int _requestSeq;

        public int ProcessId => _process.Id;

        /// <summary>Every DAP message both ways plus adapter stderr — the failure dump.</summary>
        public string Transcript => string.Join(Environment.NewLine, _transcript);

        public RawDapClient(string adapterPath)
        {
            // ProcessStartInfo copied from DebugService.StartDebuggingAsync
            // (minus WorkingDirectory plumbing — lldb-dap needs none).
            var startInfo = new ProcessStartInfo
            {
                FileName = adapterPath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                // MUST be BOM-less: accessing Process.StandardInput sets AutoFlush=true,
                // which flushes the wrapper StreamWriter and writes the encoding preamble.
                // With Encoding.UTF8 (BOM) that injects EF BB BF into the adapter's stdin,
                // corrupting the first Content-Length header — the adapter never replies.
                StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            };

            _process = new Process { StartInfo = startInfo };
            _process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) _transcript.Enqueue("[stderr] " + e.Data);
            };

            _process.Start();
            _process.BeginErrorReadLine();

            _writer = new StreamWriter(_process.StandardInput.BaseStream, new UTF8Encoding(false)) { AutoFlush = false };
            // Latin1 maps every byte 1:1 to a char, so Content-Length (a BYTE count)
            // can be honoured exactly; the body is re-decoded as UTF-8 afterwards.
            // A UTF-8 StreamReader here would over-read whenever a message contains
            // multi-byte characters, corrupting the framing of subsequent messages.
            _reader = new StreamReader(_process.StandardOutput.BaseStream, Encoding.Latin1);

            _readTask = Task.Run(() => ReadLoopAsync(_cts.Token));
        }

        /// <summary>
        /// Send a request; the returned task completes with the matching response.
        /// No internal timeout — callers bound every await with WithTimeout.
        /// </summary>
        public Task<JsonElement> SendRequestAsync(string command, object arguments)
        {
            var seq = Interlocked.Increment(ref _requestSeq);
            var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_lock) { _pendingRequests[seq] = tcs; }
            PreObserve(tcs.Task);
            return SendAndAwaitAsync(seq, command, arguments, tcs);
        }

        private async Task<JsonElement> SendAndAwaitAsync(int seq, string command, object arguments, TaskCompletionSource<JsonElement> tcs)
        {
            var request = new { seq, type = "request", command, arguments };
            await SendMessageAsync(request);
            return await tcs.Task;
        }

        private async Task SendMessageAsync(object message)
        {
            var json = JsonSerializer.Serialize(message, JsonOptions);
            var content = Encoding.UTF8.GetBytes(json);   // Content-Length is a BYTE count
            var header = $"Content-Length: {content.Length}\r\n\r\n";

            await _writeLock.WaitAsync();
            try
            {
                _transcript.Enqueue("--> " + json);
                await _writer.WriteAsync(header);
                await _writer.WriteAsync(json);
                await _writer.FlushAsync();
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// Completes with the first event whose name is in <paramref name="names"/> —
        /// consuming the backlog first, so an event that raced ahead of the wait
        /// (e.g. `initialized` arriving while the launch send was in flight) is never missed.
        /// </summary>
        public Task<JsonElement> WaitForEventAsync(params string[] names)
        {
            lock (_lock)
            {
                for (int i = 0; i < _eventBacklog.Count; i++)
                {
                    if (names.Contains(EventName(_eventBacklog[i])))
                    {
                        var evt = _eventBacklog[i];
                        _eventBacklog.RemoveAt(i);
                        return Task.FromResult(evt);
                    }
                }

                var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
                PreObserve(tcs.Task);
                _eventWaiters.Add((names, tcs));
                return tcs.Task;
            }
        }

        /// <summary>Remove and return all backlogged events with the given name.</summary>
        public List<JsonElement> DrainEvents(string name)
        {
            lock (_lock)
            {
                var matches = _eventBacklog.Where(e => EventName(e) == name).ToList();
                _eventBacklog.RemoveAll(e => EventName(e) == name);
                return matches;
            }
        }

        public static string? EventName(JsonElement message) =>
            message.TryGetProperty("event", out var e) ? e.GetString() : null;

        /// <summary>
        /// A test can bail (timeout Assert.Fail) before awaiting a request/event task;
        /// if that task later faults on adapter EOF it must not surface as an
        /// UnobservedTaskException — observe eagerly.
        /// </summary>
        private static void PreObserve(Task task) =>
            _ = task.ContinueWith(static t => _ = t.Exception,
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

        private async Task ReadLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var message = await ReadMessageAsync(cancellationToken);
                    if (message == null) break;   // EOF — the adapter exited
                    Dispatch(message.Value);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _transcript.Enqueue("[read-loop] " + ex.Message);
            }
            finally
            {
                FailAllPending("lldb-dap closed the DAP stream before this request/event completed");
            }
        }

        private void Dispatch(JsonElement message)
        {
            _transcript.Enqueue("<-- " + message);

            var type = message.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (type == "response")
            {
                var reqSeq = message.TryGetProperty("request_seq", out var rs) ? rs.GetInt32() : 0;
                TaskCompletionSource<JsonElement>? tcs = null;
                lock (_lock)
                {
                    if (_pendingRequests.Remove(reqSeq, out var found)) tcs = found;
                }
                tcs?.TrySetResult(message);
            }
            else if (type == "event")
            {
                lock (_lock)
                {
                    var name = EventName(message);
                    var idx = _eventWaiters.FindIndex(w => w.Names.Contains(name));
                    if (idx >= 0)
                    {
                        var waiter = _eventWaiters[idx];
                        _eventWaiters.RemoveAt(idx);
                        waiter.Tcs.TrySetResult(message);
                    }
                    else
                    {
                        _eventBacklog.Add(message);
                    }
                }
            }
        }

        private void FailAllPending(string why)
        {
            List<TaskCompletionSource<JsonElement>> all;
            lock (_lock)
            {
                all = _pendingRequests.Values.Concat(_eventWaiters.Select(w => w.Tcs)).ToList();
                _pendingRequests.Clear();
                _eventWaiters.Clear();
            }
            foreach (var tcs in all) tcs.TrySetException(new IOException(why));
        }

        // Content-Length framing reader copied from DebugService.ReadMessageAsync.
        private async Task<JsonElement?> ReadMessageAsync(CancellationToken cancellationToken)
        {
            int contentLength = 0;
            while (true)
            {
                var line = await _reader.ReadLineAsync(cancellationToken);
                if (line == null) return null;
                if (line.Length == 0) break;

                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(line.Substring(15).Trim(), out contentLength))
                        contentLength = 0;
                }
            }

            if (contentLength == 0) return null;

            // Read content — contentLength is a BYTE count. The reader uses Latin1
            // (1 byte == 1 char), so reading contentLength chars reads exactly the
            // message body; re-decode those bytes as UTF-8 to get the real JSON.
            var buffer = new char[contentLength];
            var read = 0;
            while (read < contentLength)
            {
                var chunk = await _reader.ReadAsync(buffer.AsMemory(read, contentLength - read), cancellationToken);
                if (chunk == 0) return null;
                read += chunk;
            }

            var bytes = Encoding.Latin1.GetBytes(buffer);
            var json = Encoding.UTF8.GetString(bytes);
            return JsonSerializer.Deserialize<JsonElement>(json, JsonOptions);
        }

        public void Dispose()
        {
            try { _cts.Cancel(); } catch { }
            try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch { }
            try { _readTask.Wait(500); } catch { }
            try { _writer.Dispose(); } catch { }
            try { _reader.Dispose(); } catch { }
            try { _process.Dispose(); } catch { }
            _cts.Dispose();
        }
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

    /// <summary>Builds the temp project through the real BuildService and returns the result.</summary>
    private static async Task<(BuildResult Result, RecordingOutputService Output)> BuildProjectAsync(
        TempMixedNativeProject fixture)
    {
        var output = new RecordingOutputService();
        var buildService = new BuildService(output);
        var serializer = new ProjectSerializer();

        var project = await serializer.LoadAsync(fixture.ProjectFile);
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var result = await buildService.BuildProjectAsync(project, cts.Token);
        return (result, output);
    }

    private static void AssertDapSuccess(JsonElement response, string command, RawDapClient dap)
    {
        var ok = response.TryGetProperty("success", out var s) && s.GetBoolean();
        Assert.That(ok, Is.True,
            $"DAP '{command}' request failed: {response}\n\nRaw DAP traffic:\n{dap.Transcript}");
    }

    private static string StopReason(JsonElement stoppedEvent) =>
        stoppedEvent.TryGetProperty("body", out var b) && b.TryGetProperty("reason", out var r)
            ? r.GetString() ?? "" : "";

    private static async Task<(bool Verified, int? Line)> SetBasBreakpointAsync(
        RawDapClient dap, string sourcePath, int line, string formLabel)
    {
        var response = await WithTimeout(dap.SendRequestAsync("setBreakpoints", new
        {
            source = new { path = sourcePath },
            breakpoints = new[] { new { line } }
        }), ResponseTimeout, $"setBreakpoints response ({formLabel} path form)");
        AssertDapSuccess(response, "setBreakpoints", dap);

        var bps = response.GetProperty("body").GetProperty("breakpoints");
        Assert.That(bps.GetArrayLength(), Is.EqualTo(1),
            $"setBreakpoints ({formLabel}) returned {bps.GetArrayLength()} breakpoints for 1 requested.\n\nRaw DAP traffic:\n{dap.Transcript}");

        var bp = bps[0];
        var verified = bp.TryGetProperty("verified", out var v) && v.GetBoolean();
        int? boundLine = bp.TryGetProperty("line", out var bl) && bl.ValueKind == JsonValueKind.Number
            ? bl.GetInt32() : null;
        return (verified, boundLine);
    }

    private static async Task<(string Path, int Line, string Name)> TopFrameAsync(
        RawDapClient dap, long threadId, string what)
    {
        var response = await WithTimeout(
            dap.SendRequestAsync("stackTrace", new { threadId, startFrame = 0, levels = 20 }),
            ResponseTimeout, $"stackTrace response ({what})");
        AssertDapSuccess(response, "stackTrace", dap);

        var frames = response.GetProperty("body").GetProperty("stackFrames");
        Assert.That(frames.GetArrayLength(), Is.GreaterThan(0),
            $"stackTrace ({what}) returned no frames.\n\nRaw DAP traffic:\n{dap.Transcript}");

        var top = frames[0];
        var name = top.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        var path = top.TryGetProperty("source", out var src)
                   && src.ValueKind == JsonValueKind.Object
                   && src.TryGetProperty("path", out var p)
            ? p.GetString() ?? "" : "";
        var line = top.TryGetProperty("line", out var l) && l.ValueKind == JsonValueKind.Number
            ? l.GetInt32() : -1;
        return (path, line, name);
    }

    // ------------------------------------------------------------------
    // THE GATE
    // ------------------------------------------------------------------

    [Test]
    public async Task BasBreakpoint_Binds_Stops_And_StepsToTheNextBasStatement()
    {
        // Discovery gates. ⚠ EXISTENCE checks only — `lldb-dap --version` parks on
        // stdin and hangs forever; never spawn it to probe.
        if (!File.Exists(LldbDapPath))
            Assert.Ignore($"lldb-dap not found at {LldbDapPath} — the native debug gate needs the pinned winlibs adapter.");
        if (CppToolchain.Find() == null)
            Assert.Ignore("No C++ toolchain found (clang++/g++ on PATH, or MSVC via vswhere) — cannot build the native debuggee.");

        using var fixture = new TempMixedNativeProject();

        // ---- 1. Build (Debug default → Task 0's #line directives + /Zi PDB) ----
        var (buildResult, buildOutput) = await BuildProjectAsync(fixture);
        Assert.That(buildResult.Success, Is.True,
            "Prerequisite native build failed.\nDiagnostics:\n" +
            string.Join("\n", buildResult.Diagnostics.Select(d => $"{d.FilePath}({d.Line},{d.Column}): {d.Severity} {d.Id}: {d.Message}")) +
            "\n\nBuild output:\n" + buildOutput.Dump());
        Assert.That(buildResult.ExecutablePath, Is.Not.Null.And.Not.Empty,
            "Build succeeded but no executable path was reported.\n" + buildOutput.Dump());
        Assert.That(File.Exists(buildResult.ExecutablePath), Is.True,
            $"Reported executable does not exist: {buildResult.ExecutablePath}");

        // ---- 2. Raw lldb-dap session ----
        using var dap = new RawDapClient(LldbDapPath);
        var adapterPid = dap.ProcessId;
        var winningPathForm = "none — never verified";

        try
        {
            try
            {
                // Handshake, in the CORRECT order for lldb-dap (the reference the
                // productization task copies): initialize → response …
                var initResponse = await WithTimeout(dap.SendRequestAsync("initialize", new
                {
                    // Client args copied from DebugService.StartDebuggingAsync.
                    clientID = "visualgamestudio",
                    clientName = "Visual Game Studio",
                    adapterID = "basiclang",
                    pathFormat = "path",
                    linesStartAt1 = true,
                    columnsStartAt1 = true,
                    supportsVariableType = true,
                    supportsVariablePaging = false,
                    supportsRunInTerminalRequest = false
                }), ResponseTimeout, "initialize response");
                AssertDapSuccess(initResponse, "initialize", dap);

                // … → launch IN FLIGHT (lldb-dap does not complete it until
                // configurationDone) → the `initialized` EVENT …
                var launchTask = dap.SendRequestAsync("launch", new
                {
                    program = buildResult.ExecutablePath!,
                    cwd = fixture.RootDir,
                    stopOnEntry = false
                });

                await WithTimeout(dap.WaitForEventAsync("initialized"), ResponseTimeout, "initialized event");

                // ---- 3. setBreakpoints + the path-form probe (feeds Tasks 9/14) ----
                // The PDB records the #line spelling C:/forward/slash/Logic.bas; lldb
                // usually normalizes separators on Windows, but this is exactly the
                // 19-vs-22 kind of quirk the spec warns about — probe both forms.
                var nativePath = fixture.LogicBasFile;
                var (verified, boundLine) = await SetBasBreakpointAsync(
                    dap, nativePath, TempMixedNativeProject.BreakpointLine, "backslash");
                if (verified)
                {
                    winningPathForm = "backslash (OS-native)";
                }
                else
                {
                    var forwardPath = nativePath.Replace('\\', '/');
                    (verified, boundLine) = await SetBasBreakpointAsync(
                        dap, forwardPath, TempMixedNativeProject.BreakpointLine, "forward-slash");
                    if (verified) winningPathForm = "forward-slash (#line spelling)";
                }

                if (verified && boundLine is int bl)
                {
                    Assert.That(bl, Is.EqualTo(TempMixedNativeProject.BreakpointLine),
                        $"Breakpoint verified but was moved to line {bl} (requested {TempMixedNativeProject.BreakpointLine})." +
                        "\n\nRaw DAP traffic:\n" + dap.Transcript);
                }
                // Not-verified-yet is NOT a bind failure at this point: lldb-dap may leave
                // breakpoints unverified until the target launches and then upgrade them
                // via `breakpoint` events — proceed through configurationDone/launch and
                // let the stop (or its absence) deliver the verdict.

                // ---- 4. configurationDone → launch response → stopped(breakpoint) ----
                var cdResponse = await WithTimeout(dap.SendRequestAsync("configurationDone", new { }),
                    ResponseTimeout, "configurationDone response");
                AssertDapSuccess(cdResponse, "configurationDone", dap);

                var launchResponse = await WithTimeout(launchTask, LaunchStopTimeout, "launch response");
                AssertDapSuccess(launchResponse, "launch", dap);

                var stopEvent = await WithTimeout(dap.WaitForEventAsync("stopped", "terminated", "exited"),
                    LaunchStopTimeout, "stopped event at the .bas breakpoint");
                Assert.That(RawDapClient.EventName(stopEvent), Is.EqualTo("stopped"),
                    $"The debuggee ended without ever stopping — the .bas breakpoint never bound " +
                    $"(neither path form; last probe state: verified={verified}).\n\nRaw DAP traffic:\n{dap.Transcript}");
                Assert.That(StopReason(stopEvent), Is.EqualTo("breakpoint"),
                    $"Stopped, but not for the breakpoint (reason '{StopReason(stopEvent)}').\n\nRaw DAP traffic:\n{dap.Transcript}");

                // lldb-dap reports REAL OS thread ids — never assume threadId=1.
                var threadId = stopEvent.GetProperty("body").GetProperty("threadId").GetInt64();

                if (!verified)
                {
                    // The deferred-verification leg: the stop proves the bind; record how
                    // it was upgraded for the checkpoint report.
                    var upgrades = dap.DrainEvents("breakpoint");
                    winningPathForm = upgrades.Count > 0
                        ? "deferred — unverified in both setBreakpoints responses, upgraded via 'breakpoint' event(s) after launch"
                        : "deferred — unverified in both setBreakpoints responses, yet the stop occurred (no 'breakpoint' event seen)";
                }

                // ---- 5. Stack: the stop must present as Logic.bas, not generated glue ----
                var stopFrame = await TopFrameAsync(dap, threadId, "at the breakpoint");
                Assert.That(stopFrame.Path, Does.EndWith("Logic.bas").IgnoreCase,
                    $"Top frame at the breakpoint is not the .bas source: '{stopFrame.Name}' @ {stopFrame.Path}:{stopFrame.Line}" +
                    "\n\nRaw DAP traffic:\n" + dap.Transcript);
                Assert.That(stopFrame.Line, Is.EqualTo(TempMixedNativeProject.BreakpointLine),
                    $"Stopped in Logic.bas but at line {stopFrame.Line}, expected {TempMixedNativeProject.BreakpointLine}." +
                    "\n\nRaw DAP traffic:\n" + dap.Transcript);

                // ---- 6. next → the NEXT .bas statement (not glue, not main.cpp) ----
                var nextResponse = await WithTimeout(dap.SendRequestAsync("next", new { threadId }),
                    ResponseTimeout, "next response");
                AssertDapSuccess(nextResponse, "next", dap);

                var stepEvent = await WithTimeout(dap.WaitForEventAsync("stopped", "terminated", "exited"),
                    LaunchStopTimeout, "stopped event after next");
                Assert.That(RawDapClient.EventName(stepEvent), Is.EqualTo("stopped"),
                    $"The debuggee ended during the step instead of stopping.\n\nRaw DAP traffic:\n{dap.Transcript}");
                Assert.That(StopReason(stepEvent), Is.EqualTo("step"),
                    $"Expected a step stop, got reason '{StopReason(stepEvent)}'.\n\nRaw DAP traffic:\n{dap.Transcript}");

                var stepFrame = await TopFrameAsync(dap, threadId, "after next");
                Assert.That(stepFrame.Path, Does.EndWith("Logic.bas").IgnoreCase,
                    $"Step landed outside the .bas source (generated glue or main.cpp): " +
                    $"'{stepFrame.Name}' @ {stepFrame.Path}:{stepFrame.Line}\n\nRaw DAP traffic:\n" + dap.Transcript);
                Assert.That(stepFrame.Line, Is.EqualTo(TempMixedNativeProject.StepTargetLine),
                    $"Step landed in Logic.bas but at line {stepFrame.Line}, expected {TempMixedNativeProject.StepTargetLine}." +
                    "\n\nRaw DAP traffic:\n" + dap.Transcript);

                // ---- 7. disconnect — terminate the debuggee; the adapter must exit ----
                var disconnectTask = dap.SendRequestAsync("disconnect", new { terminateDebuggee = true });
                // No assertion on the reply — some adapters exit before replying; the
                // process-exit poll is the real contract.
                await Task.WhenAny(disconnectTask, Task.Delay(TimeSpan.FromSeconds(10)));
                AssertProcessExits(adapterPid, TimeSpan.FromSeconds(10), "lldb-dap process");

                // Checkpoint evidence (shows in the test output on PASS).
                TestContext.Out.WriteLine("STEP-0 GATE: PASS");
                TestContext.Out.WriteLine($"  breakpoint path form: {winningPathForm}");
                TestContext.Out.WriteLine($"  stop:  '{stopFrame.Name}' @ {stopFrame.Path}:{stopFrame.Line} (reason breakpoint)");
                TestContext.Out.WriteLine($"  step:  '{stepFrame.Name}' @ {stepFrame.Path}:{stepFrame.Line} (reason step)");
            }
            catch (Exception ex) when (ex is not ResultStateException)
            {
                // Any transport/protocol surprise must still surface the raw traffic.
                Assert.Fail("Unexpected exception during the raw DAP exchange: " + ex +
                            "\n\nRaw DAP traffic:\n" + dap.Transcript);
            }
        }
        finally
        {
            // Kill-tree regardless of outcome — no lldb-dap (or debuggee) may outlive the test.
            if (ProcessIsAlive(adapterPid))
            {
                try { Process.GetProcessById(adapterPid).Kill(entireProcessTree: true); } catch { }
            }
        }
    }
}
