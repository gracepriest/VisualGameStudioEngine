using System.Collections.Concurrent;
using System.Text.Json;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

[TestFixture]
public class DebugServiceTests
{
    private Mock<IOutputService> _mockOutputService = null!;
    private DebugService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _mockOutputService = new Mock<IOutputService>();
        _service = new DebugService(_mockOutputService.Object);
    }

    [TearDown]
    public void TearDown()
    {
        // Fast dispose - returns immediately when service was never started
        _service?.Dispose();
    }

    [Test]
    public void InitialState_IsNotStarted()
    {
        Assert.That(_service.State, Is.EqualTo(DebugState.NotStarted));
    }

    [Test]
    public void IsDebugging_WhenNotStarted_ReturnsFalse()
    {
        Assert.That(_service.IsDebugging, Is.False);
    }

    [Test]
    public async Task StopDebuggingAsync_WhenNotStarted_DoesNotThrow()
    {
        await _service.StopDebuggingAsync();

        Assert.That(_service.State, Is.EqualTo(DebugState.Stopped));
    }

    [Test]
    public async Task ContinueAsync_WhenNotPaused_DoesNothing()
    {
        await _service.ContinueAsync();

        Assert.That(_service.State, Is.EqualTo(DebugState.NotStarted));
    }

    [Test]
    public async Task StepOverAsync_WhenNotPaused_DoesNothing()
    {
        await _service.StepOverAsync();

        Assert.That(_service.State, Is.EqualTo(DebugState.NotStarted));
    }

    [Test]
    public async Task StepIntoAsync_WhenNotPaused_DoesNothing()
    {
        await _service.StepIntoAsync();

        Assert.That(_service.State, Is.EqualTo(DebugState.NotStarted));
    }

    [Test]
    public async Task StepOutAsync_WhenNotPaused_DoesNothing()
    {
        await _service.StepOutAsync();

        Assert.That(_service.State, Is.EqualTo(DebugState.NotStarted));
    }

    [Test]
    public async Task PauseAsync_WhenNotRunning_DoesNothing()
    {
        await _service.PauseAsync();

        Assert.That(_service.State, Is.EqualTo(DebugState.NotStarted));
    }

    [Test]
    public async Task RunToCursorAsync_WhenNotPaused_DoesNothing()
    {
        await _service.RunToCursorAsync("/path/to/file.bas", 10);

        Assert.That(_service.State, Is.EqualTo(DebugState.NotStarted));
    }

    [Test]
    public async Task SetNextStatementAsync_WhenNotPaused_ReturnsFalse()
    {
        var result = await _service.SetNextStatementAsync("/path/to/file.bas", 10);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task SetExceptionBreakpointsAsync_WhenNotDebugging_DoesNotThrow()
    {
        await _service.SetExceptionBreakpointsAsync(new[] { "all" });

        Assert.Pass();
    }

    [Test]
    public async Task GetStackTraceAsync_WhenNotDebugging_ReturnsEmpty()
    {
        var result = await _service.GetStackTraceAsync();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetScopesAsync_WhenNotDebugging_ReturnsEmpty()
    {
        var result = await _service.GetScopesAsync(0);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetVariablesAsync_WhenNotDebugging_ReturnsEmpty()
    {
        var result = await _service.GetVariablesAsync(0);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task EvaluateAsync_WhenNotDebugging_ReturnsError()
    {
        var result = await _service.EvaluateAsync("x + 1");

        Assert.That(result.Result, Does.StartWith("Error:"));
    }

    [Test]
    public async Task SetBreakpointsAsync_WhenNotDebugging_ReturnsEmpty()
    {
        var breakpoints = new[] { new SourceBreakpoint { Line = 10 } };
        var result = await _service.SetBreakpointsAsync("/path/to/file.bas", breakpoints);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task SetFunctionBreakpointsAsync_WhenNotDebugging_ReturnsEmpty()
    {
        var breakpoints = new[] { new FunctionBreakpoint { Name = "Main" } };
        var result = await _service.SetFunctionBreakpointsAsync(breakpoints);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void StateChanged_Event_CanBeSubscribed()
    {
        _service.StateChanged += (s, e) => { };
        Assert.Pass();
    }

    [Test]
    public void Stopped_Event_CanBeSubscribed()
    {
        _service.Stopped += (s, e) => { };
        Assert.Pass();
    }

    [Test]
    public void OutputReceived_Event_CanBeSubscribed()
    {
        _service.OutputReceived += (s, e) => { };
        Assert.Pass();
    }

    [Test]
    public void BreakpointsChanged_Event_CanBeSubscribed()
    {
        _service.BreakpointsChanged += (s, e) => { };
        Assert.Pass();
    }

    [Test]
    public void Dispose_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => _service.Dispose());
    }

    [Test]
    public void Dispose_MultipleTimes_DoesNotThrow()
    {
        Assert.DoesNotThrow(() =>
        {
            _service.Dispose();
            _service.Dispose();
        });
    }
}

/// <summary>
/// Phase 4 Task 5 — real threadIds end to end. The Step-0 gate observed lldb-dap
/// stopping on threadId 6908, so every execution-control request that hardcodes
/// threadId = 1 is a landmine on the native path. These tests drive the REAL
/// DebugService over the sessionFactory seam against the scripted fake and assert
/// the WIRE value the adapter received — not internal state. Every await is
/// budget-bounded.
/// </summary>
[TestFixture]
public class DebugServiceThreadIdTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    private RecordingOutputService _output = null!;
    private FakeDapAdapter _fake = null!;
    private DapSession _session = null!;
    private DebugService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _output = new RecordingOutputService();
        _fake = FakeDapAdapter.ManagedShaped();
        _session = new DapSession(_fake.SessionReads, _fake.SessionWrites, _output);
        _service = new DebugService(_output, _ => _session);
    }

    [TearDown]
    public void TearDown()
    {
        _service.Dispose();
        _session.Dispose();
        _fake.Dispose();
    }

    [Test]
    public async Task StoppedThreadId_FlowsIntoContinue()
    {
        await StartAndStopOnThreadAsync(7);

        await WithTimeout(_service.ContinueAsync(), "the continue round-trip");

        Assert.That(WireThreadId("continue"), Is.EqualTo(7),
            "continue must carry the stopped event's threadId, not a hardcoded 1");
    }

    [Test]
    public async Task StoppedThreadId_FlowsIntoStepPauseAndGoto()
    {
        await StartAndStopOnThreadAsync(7);

        // next — the fake answers immediately and StepOverAsync awaits that response.
        await WithTimeout(_service.StepOverAsync(), "the next (step over) round-trip");
        Assert.That(WireThreadId("next"), Is.EqualTo(7),
            "next must carry the stopped event's threadId");

        // A step leaves the service Running; stepIn needs Paused again.
        await EmitStoppedAndWaitAsync(7);
        await WithTimeout(_service.StepIntoAsync(), "the stepIn round-trip");
        Assert.That(WireThreadId("stepIn"), Is.EqualTo(7),
            "stepIn must carry the stopped event's threadId");

        // pause needs Running — exactly where the step just left us.
        Assert.That(_service.State, Is.EqualTo(DebugState.Running),
            "precondition: the step should have left the service Running");
        await WithTimeout(_service.PauseAsync(), "the pause round-trip");
        Assert.That(WireThreadId("pause"), Is.EqualTo(7),
            "pause must carry the stopped event's threadId");

        // goto needs Paused, and gotoTargets needs a real targets body — scripted one-shot.
        await EmitStoppedAndWaitAsync(7);
        _fake.RespondToNextRequestWithBody("gotoTargets",
            new { targets = new object[] { new { id = 42, label = "line 5", line = 5 } } });
        var jumped = await WithTimeout(_service.SetNextStatementAsync("C:\\fake\\main.bas", 5),
            "the gotoTargets/goto round-trip");
        Assert.That(jumped, Is.True, "SetNextStatementAsync failed:\n" + _output.Dump());
        Assert.That(WireThreadId("goto"), Is.EqualTo(7),
            "goto must carry the stopped event's threadId");
    }

    [Test]
    public async Task StackTrace_DefaultsToTheStoppedThread()
    {
        await StartAndStopOnThreadAsync(7);

        await WithTimeout(_service.GetStackTraceAsync(), "the stackTrace round-trip");

        Assert.That(WireThreadId("stackTrace"), Is.EqualTo(7),
            "a default GetStackTraceAsync() must ask for the stopped thread, not thread 1");
    }

    [Test]
    public async Task StackTrace_ExplicitThreadIdWins()
    {
        await StartAndStopOnThreadAsync(7);

        await WithTimeout(_service.GetStackTraceAsync(3), "the stackTrace round-trip");

        Assert.That(WireThreadId("stackTrace"), Is.EqualTo(3),
            "an explicit threadId must always win over the stopped-thread default");
    }

    [Test]
    public async Task NewSession_ResetsTheStoppedThreadId()
    {
        // DebugService is a DI singleton outliving sessions: after session 1 stops on
        // thread 7, session 2's PRE-FIRST-STOP pause must carry 1 again — a stale 7
        // would ask the new adapter to pause a thread it never had (and the managed
        // adapter echoes the requested id back, yielding an empty call stack).
        var output = new RecordingOutputService();
        using var fake1 = FakeDapAdapter.ManagedShaped();
        using var fake2 = FakeDapAdapter.ManagedShaped();
        var session1 = new DapSession(fake1.SessionReads, fake1.SessionWrites, output);
        var session2 = new DapSession(fake2.SessionReads, fake2.SessionWrites, output);
        var sessions = new Queue<DapSession>(new[] { session1, session2 });
        var service = new DebugService(output, _ => sessions.Dequeue());

        try
        {
            var config = new DebugConfiguration
            {
                Program = "FakeApp.exe",
                WorkingDirectory = Path.GetTempPath()
            };

            // Session 1: handshake, stop on thread 7, tear down.
            var started1 = await WithTimeout(service.StartDebuggingAsync(config),
                "the session 1 handshake");
            Assert.That(started1, Is.True, "session 1 handshake failed:\n" + output.Dump());
            await EmitStoppedAndWaitAsync(service, fake1, 7);
            await WithTimeout(service.StopDebuggingAsync(), "stopping session 1");

            // Session 2: handshake only — deliberately NO stopped event before the pause.
            var started2 = await WithTimeout(service.StartDebuggingAsync(config),
                "the session 2 handshake");
            Assert.That(started2, Is.True, "session 2 handshake failed:\n" + output.Dump());
            Assert.That(service.State, Is.EqualTo(DebugState.Running));

            await WithTimeout(service.PauseAsync(), "the pre-first-stop pause round-trip");

            Assert.That(WireThreadId(fake2, "pause"), Is.EqualTo(1),
                "a new session's pre-first-stop pause must carry 1, not the previous session's stopped thread");
        }
        finally
        {
            service.Dispose();
            session1.Dispose();
            session2.Dispose();
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>Handshake to Running, then stop on the given thread.</summary>
    private async Task StartAndStopOnThreadAsync(int threadId)
    {
        var started = await WithTimeout(_service.StartDebuggingAsync(new DebugConfiguration
        {
            Program = "FakeApp.exe",
            WorkingDirectory = Path.GetTempPath()
        }), "the StartDebuggingAsync handshake against the managed-shaped fake");
        Assert.That(started, Is.True, "handshake failed:\n" + _output.Dump());

        await EmitStoppedAndWaitAsync(threadId);
    }

    /// <summary>Emit a stopped event and wait until the service raised Stopped for it.</summary>
    private Task EmitStoppedAndWaitAsync(int threadId)
        => EmitStoppedAndWaitAsync(_service, _fake, threadId);

    private static async Task EmitStoppedAndWaitAsync(DebugService service, FakeDapAdapter fake, int threadId)
    {
        var stopped = new TaskCompletionSource<StoppedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? _, StoppedEventArgs e) => stopped.TrySetResult(e);
        service.Stopped += Handler;
        try
        {
            fake.EmitEvent("stopped", new { reason = "breakpoint", threadId });
            var args = await WithTimeout(stopped.Task, $"the Stopped event for threadId {threadId}");
            Assert.That(args.ThreadId, Is.EqualTo(threadId),
                "the stopped event's threadId was not parsed off the event body");
            Assert.That(service.State, Is.EqualTo(DebugState.Paused),
                "the stopped event did not pause the service");
        }
        finally
        {
            service.Stopped -= Handler;
        }
    }

    /// <summary>The threadId the adapter actually received on the wire for a command.</summary>
    private int WireThreadId(string command)
        => WireThreadId(_fake, command);

    private static int WireThreadId(FakeDapAdapter fake, string command)
    {
        var matches = fake.Received.Where(r => r.Command == command).ToArray();
        Assert.That(matches, Is.Not.Empty,
            $"the adapter never received a '{command}' request; it saw: " +
            string.Join(", ", fake.Received.Select(r => r.Command)));

        var args = matches.Last().Arguments;
        Assert.That(args.ValueKind, Is.EqualTo(JsonValueKind.Object),
            $"'{command}' carried no arguments object");
        Assert.That(args.TryGetProperty("threadId", out var tid), Is.True,
            $"'{command}' arguments carried no threadId: {args.GetRawText()}");
        return tid.GetInt32();
    }

    private static async Task<T> WithTimeout<T>(Task<T> task, string what)
    {
        var completed = await Task.WhenAny(task, Task.Delay(Budget));
        if (completed != task)
            Assert.Fail($"Timed out after {Budget.TotalSeconds:F0}s waiting for: {what}");
        return await task;
    }

    private static async Task WithTimeout(Task task, string what)
    {
        var completed = await Task.WhenAny(task, Task.Delay(Budget));
        if (completed != task)
            Assert.Fail($"Timed out after {Budget.TotalSeconds:F0}s waiting for: {what}");
        await task;
    }

    /// <summary>
    /// Thread-safe IOutputService that records everything, so test failures can include
    /// the real DAP output. Duplicated per suite convention (the siblings in
    /// DapSessionTests/IdeInAngerTests are private).
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
}

[TestFixture]
public class DebugConfigurationTests
{
    [Test]
    public void DefaultConfiguration_HasEmptyValues()
    {
        var config = new DebugConfiguration();

        Assert.That(config.Program, Is.EqualTo(""));
        Assert.That(config.WorkingDirectory, Is.EqualTo(""));
        Assert.That(config.Arguments, Is.Empty);
        Assert.That(config.Environment, Is.Empty);
        Assert.That(config.StopOnEntry, Is.False);
    }

    [Test]
    public void Configuration_CanSetAllProperties()
    {
        var config = new DebugConfiguration
        {
            Program = "/path/to/app.exe",
            WorkingDirectory = "/path/to/project",
            Arguments = new[] { "--arg1", "--arg2" },
            StopOnEntry = true
        };
        config.Environment["DEBUG"] = "true";

        Assert.That(config.Program, Is.EqualTo("/path/to/app.exe"));
        Assert.That(config.WorkingDirectory, Is.EqualTo("/path/to/project"));
        Assert.That(config.Arguments, Has.Length.EqualTo(2));
        Assert.That(config.Environment["DEBUG"], Is.EqualTo("true"));
        Assert.That(config.StopOnEntry, Is.True);
    }
}

[TestFixture]
public class SourceBreakpointTests
{
    [Test]
    public void DefaultBreakpoint_HasDefaultValues()
    {
        var bp = new SourceBreakpoint();

        Assert.That(bp.Line, Is.EqualTo(0));
        Assert.That(bp.Column, Is.Null);
        Assert.That(bp.Condition, Is.Null);
        Assert.That(bp.HitCondition, Is.Null);
        Assert.That(bp.LogMessage, Is.Null);
    }

    [Test]
    public void Breakpoint_CanSetAllProperties()
    {
        var bp = new SourceBreakpoint
        {
            Line = 42,
            Column = 5,
            Condition = "x > 10",
            HitCondition = ">= 3",
            LogMessage = "Hit breakpoint at {x}"
        };

        Assert.That(bp.Line, Is.EqualTo(42));
        Assert.That(bp.Column, Is.EqualTo(5));
        Assert.That(bp.Condition, Is.EqualTo("x > 10"));
        Assert.That(bp.HitCondition, Is.EqualTo(">= 3"));
        Assert.That(bp.LogMessage, Is.EqualTo("Hit breakpoint at {x}"));
    }
}

[TestFixture]
public class FunctionBreakpointTests
{
    [Test]
    public void DefaultBreakpoint_HasDefaultValues()
    {
        var bp = new FunctionBreakpoint();

        Assert.That(bp.Name, Is.EqualTo(""));
        Assert.That(bp.Condition, Is.Null);
        Assert.That(bp.HitCondition, Is.Null);
    }

    [Test]
    public void Breakpoint_CanSetAllProperties()
    {
        var bp = new FunctionBreakpoint
        {
            Name = "MyModule.MyFunction",
            Condition = "param1 > 0",
            HitCondition = ">= 5"
        };

        Assert.That(bp.Name, Is.EqualTo("MyModule.MyFunction"));
        Assert.That(bp.Condition, Is.EqualTo("param1 > 0"));
        Assert.That(bp.HitCondition, Is.EqualTo(">= 5"));
    }
}

[TestFixture]
public class ExceptionFilterOptionTests
{
    [Test]
    public void DefaultOption_HasDefaultValues()
    {
        var option = new ExceptionFilterOption();

        Assert.That(option.FilterId, Is.EqualTo(""));
        Assert.That(option.Condition, Is.Null);
    }

    [Test]
    public void Option_CanSetAllProperties()
    {
        var option = new ExceptionFilterOption
        {
            FilterId = "System.NullReferenceException",
            Condition = "true"
        };

        Assert.That(option.FilterId, Is.EqualTo("System.NullReferenceException"));
        Assert.That(option.Condition, Is.EqualTo("true"));
    }
}

[TestFixture]
public class BreakpointInfoTests
{
    [Test]
    public void DefaultInfo_HasDefaultValues()
    {
        var info = new BreakpointInfo();

        Assert.That(info.Id, Is.EqualTo(0));
        Assert.That(info.Verified, Is.False);
        Assert.That(info.Message, Is.Null);
        Assert.That(info.Line, Is.EqualTo(0));
        Assert.That(info.Column, Is.Null);
        Assert.That(info.EndLine, Is.Null);
        Assert.That(info.EndColumn, Is.Null);
    }

    [Test]
    public void Info_CanSetAllProperties()
    {
        var info = new BreakpointInfo
        {
            Id = 1,
            Verified = true,
            Message = "Breakpoint bound",
            Line = 42,
            Column = 5,
            EndLine = 42,
            EndColumn = 15
        };

        Assert.That(info.Id, Is.EqualTo(1));
        Assert.That(info.Verified, Is.True);
        Assert.That(info.Message, Is.EqualTo("Breakpoint bound"));
        Assert.That(info.Line, Is.EqualTo(42));
        Assert.That(info.Column, Is.EqualTo(5));
        Assert.That(info.EndLine, Is.EqualTo(42));
        Assert.That(info.EndColumn, Is.EqualTo(15));
    }
}

[TestFixture]
public class StackFrameInfoTests
{
    [Test]
    public void DefaultInfo_HasDefaultValues()
    {
        var info = new StackFrameInfo();

        Assert.That(info.Id, Is.EqualTo(0));
        Assert.That(info.Name, Is.EqualTo(""));
        Assert.That(info.FilePath, Is.Null);
        Assert.That(info.Line, Is.EqualTo(0));
        Assert.That(info.Column, Is.EqualTo(0));
        Assert.That(info.EndLine, Is.Null);
        Assert.That(info.EndColumn, Is.Null);
        Assert.That(info.ModuleName, Is.Null);
    }

    [Test]
    public void Info_CanSetAllProperties()
    {
        var info = new StackFrameInfo
        {
            Id = 1,
            Name = "Main",
            FilePath = "/path/to/file.bas",
            Line = 10,
            Column = 1,
            EndLine = 50,
            EndColumn = 1,
            ModuleName = "Program"
        };

        Assert.That(info.Id, Is.EqualTo(1));
        Assert.That(info.Name, Is.EqualTo("Main"));
        Assert.That(info.FilePath, Is.EqualTo("/path/to/file.bas"));
        Assert.That(info.Line, Is.EqualTo(10));
        Assert.That(info.Column, Is.EqualTo(1));
        Assert.That(info.EndLine, Is.EqualTo(50));
        Assert.That(info.EndColumn, Is.EqualTo(1));
        Assert.That(info.ModuleName, Is.EqualTo("Program"));
    }
}

[TestFixture]
public class ScopeInfoTests
{
    [Test]
    public void DefaultInfo_HasDefaultValues()
    {
        var info = new ScopeInfo();

        Assert.That(info.Name, Is.EqualTo(""));
        Assert.That(info.VariablesReference, Is.EqualTo(0));
        Assert.That(info.Expensive, Is.False);
    }

    [Test]
    public void Info_CanSetAllProperties()
    {
        var info = new ScopeInfo
        {
            Name = "Locals",
            VariablesReference = 100,
            Expensive = true
        };

        Assert.That(info.Name, Is.EqualTo("Locals"));
        Assert.That(info.VariablesReference, Is.EqualTo(100));
        Assert.That(info.Expensive, Is.True);
    }
}

[TestFixture]
public class VariableInfoTests
{
    [Test]
    public void DefaultInfo_HasDefaultValues()
    {
        var info = new VariableInfo();

        Assert.That(info.Name, Is.EqualTo(""));
        Assert.That(info.Value, Is.EqualTo(""));
        Assert.That(info.Type, Is.Null);
        Assert.That(info.VariablesReference, Is.EqualTo(0));
    }

    [Test]
    public void Info_CanSetAllProperties()
    {
        var info = new VariableInfo
        {
            Name = "counter",
            Value = "42",
            Type = "Integer",
            VariablesReference = 0
        };

        Assert.That(info.Name, Is.EqualTo("counter"));
        Assert.That(info.Value, Is.EqualTo("42"));
        Assert.That(info.Type, Is.EqualTo("Integer"));
    }

    [Test]
    public void ComplexType_HasVariablesReference()
    {
        var info = new VariableInfo
        {
            Name = "myArray",
            Value = "Integer[5]",
            Type = "Integer[]",
            VariablesReference = 123
        };

        Assert.That(info.VariablesReference, Is.EqualTo(123));
    }
}

[TestFixture]
public class EvaluateResultTests
{
    [Test]
    public void DefaultResult_HasDefaultValues()
    {
        var result = new EvaluateResult();

        Assert.That(result.Result, Is.EqualTo(""));
        Assert.That(result.Type, Is.Null);
        Assert.That(result.VariablesReference, Is.EqualTo(0));
    }

    [Test]
    public void Result_CanSetAllProperties()
    {
        var result = new EvaluateResult
        {
            Result = "Hello, World!",
            Type = "String",
            VariablesReference = 0
        };

        Assert.That(result.Result, Is.EqualTo("Hello, World!"));
        Assert.That(result.Type, Is.EqualTo("String"));
    }

    [Test]
    public void ComplexResult_HasVariablesReference()
    {
        var result = new EvaluateResult
        {
            Result = "{...}",
            Type = "MyClass",
            VariablesReference = 456
        };

        Assert.That(result.VariablesReference, Is.EqualTo(456));
    }
}
