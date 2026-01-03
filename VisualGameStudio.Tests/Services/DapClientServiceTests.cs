using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

[TestFixture]
public class DapClientServiceTests
{
    private DapClientService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new DapClientService();
    }

    [TearDown]
    public void TearDown()
    {
        _service?.Dispose();
    }

    #region State Tests

    [Test]
    public void State_Initially_IsDisconnected()
    {
        Assert.That(_service.State, Is.EqualTo(DapConnectionState.Disconnected));
    }

    [Test]
    public void Capabilities_Initially_IsNull()
    {
        Assert.That(_service.Capabilities, Is.Null);
    }

    [Test]
    public void IsStopped_Initially_IsFalse()
    {
        Assert.That(_service.IsStopped, Is.False);
    }

    [Test]
    public void Threads_Initially_IsEmpty()
    {
        Assert.That(_service.Threads, Is.Empty);
    }

    #endregion

    #region StartAdapterAsync Tests

    [Test]
    public async Task StartAdapterAsync_NonExistentPath_ReturnsFalse()
    {
        var result = await _service.StartAdapterAsync("nonexistent.exe", null);
        Assert.That(result, Is.False);
        Assert.That(_service.State, Is.EqualTo(DapConnectionState.Error));
    }

    [Test]
    public async Task StartAdapterAsync_RaisesStateChangedEvent()
    {
        var stateChanges = new List<DapConnectionState>();
        _service.StateChanged += (s, e) => stateChanges.Add(e.NewState);

        await _service.StartAdapterAsync("nonexistent.exe", null);

        Assert.That(stateChanges, Does.Contain(DapConnectionState.Connecting));
    }

    #endregion

    #region ConnectAsync Tests

    [Test]
    public async Task ConnectAsync_InvalidHost_ReturnsFalse()
    {
        var result = await _service.ConnectAsync("invalid-host-12345", 9999);
        Assert.That(result, Is.False);
    }

    #endregion

    #region DisconnectAsync Tests

    [Test]
    public async Task DisconnectAsync_WhenDisconnected_DoesNotThrow()
    {
        await _service.DisconnectAsync();
        Assert.That(_service.State, Is.EqualTo(DapConnectionState.Disconnected));
    }

    #endregion

    #region Dispose Tests

    [Test]
    public void Dispose_MultipleDispose_DoesNotThrow()
    {
        Assert.DoesNotThrow(() =>
        {
            _service.Dispose();
            _service.Dispose();
        });
    }

    #endregion

    #region DAP Types Tests

    [Test]
    public void DapConnectionState_HasExpectedValues()
    {
        var values = Enum.GetValues<DapConnectionState>();
        Assert.That(values, Does.Contain(DapConnectionState.Disconnected));
        Assert.That(values, Does.Contain(DapConnectionState.Connecting));
        Assert.That(values, Does.Contain(DapConnectionState.Initializing));
        Assert.That(values, Does.Contain(DapConnectionState.Ready));
        Assert.That(values, Does.Contain(DapConnectionState.Running));
        Assert.That(values, Does.Contain(DapConnectionState.Paused));
        Assert.That(values, Does.Contain(DapConnectionState.Error));
    }

    [Test]
    public void DapCapabilities_Defaults_AreCorrect()
    {
        var caps = new DapCapabilities();
        Assert.That(caps.SupportsConfigurationDoneRequest, Is.False);
        Assert.That(caps.SupportsFunctionBreakpoints, Is.False);
        Assert.That(caps.SupportsConditionalBreakpoints, Is.False);
        Assert.That(caps.SupportsEvaluateForHovers, Is.False);
    }

    [Test]
    public void LaunchRequest_Defaults_AreCorrect()
    {
        var request = new LaunchRequest();
        Assert.That(request.Program, Is.Empty);
        Assert.That(request.StopOnEntry, Is.False);
        Assert.That(request.NoDebug, Is.False);
    }

    [Test]
    public void AttachRequest_Defaults_AreCorrect()
    {
        var request = new AttachRequest();
        Assert.That(request.ProcessId, Is.Null);
        Assert.That(request.ProcessName, Is.Null);
    }

    [Test]
    public void DapBreakpoint_Defaults_AreCorrect()
    {
        var bp = new DapBreakpoint();
        Assert.That(bp.Id, Is.Null);
        Assert.That(bp.Verified, Is.False);
        Assert.That(bp.Line, Is.Null);
    }

    [Test]
    public void DapSourceBreakpoint_Defaults_AreCorrect()
    {
        var bp = new DapSourceBreakpoint();
        Assert.That(bp.Line, Is.EqualTo(0));
        Assert.That(bp.Column, Is.Null);
        Assert.That(bp.Condition, Is.Null);
    }

    [Test]
    public void DapFunctionBreakpoint_Defaults_AreCorrect()
    {
        var bp = new DapFunctionBreakpoint();
        Assert.That(bp.Name, Is.Empty);
        Assert.That(bp.Condition, Is.Null);
        Assert.That(bp.HitCondition, Is.Null);
    }

    [Test]
    public void DapThread_Defaults_AreCorrect()
    {
        var thread = new DapThread();
        Assert.That(thread.Id, Is.EqualTo(0));
        Assert.That(thread.Name, Is.Empty);
    }

    [Test]
    public void DapStackFrame_Defaults_AreCorrect()
    {
        var frame = new DapStackFrame();
        Assert.That(frame.Id, Is.EqualTo(0));
        Assert.That(frame.Name, Is.Empty);
        Assert.That(frame.Line, Is.EqualTo(0));
    }

    [Test]
    public void DapScope_Defaults_AreCorrect()
    {
        var scope = new DapScope();
        Assert.That(scope.Name, Is.Empty);
        Assert.That(scope.VariablesReference, Is.EqualTo(0));
        Assert.That(scope.Expensive, Is.False);
    }

    [Test]
    public void DapVariable_Defaults_AreCorrect()
    {
        var variable = new DapVariable();
        Assert.That(variable.Name, Is.Empty);
        Assert.That(variable.Value, Is.Empty);
        Assert.That(variable.VariablesReference, Is.EqualTo(0));
    }

    [Test]
    public void DapSource_Defaults_AreCorrect()
    {
        var source = new DapSource();
        Assert.That(source.Name, Is.Null);
        Assert.That(source.Path, Is.Null);
        Assert.That(source.SourceReference, Is.Null);
    }

    [Test]
    public void DapEvaluateResult_Defaults_AreCorrect()
    {
        var result = new DapEvaluateResult();
        Assert.That(result.Result, Is.Empty);
        Assert.That(result.Type, Is.Null);
        Assert.That(result.VariablesReference, Is.EqualTo(0));
    }

    [Test]
    public void StackTraceResult_Defaults_AreCorrect()
    {
        var result = new StackTraceResult();
        Assert.That(result.StackFrames, Is.Empty);
        Assert.That(result.TotalFrames, Is.Null);
    }

    [Test]
    public void SetVariableResult_Defaults_AreCorrect()
    {
        var result = new SetVariableResult();
        Assert.That(result.Value, Is.Empty);
        Assert.That(result.Type, Is.Null);
    }

    [Test]
    public void CompletionTarget_Defaults_AreCorrect()
    {
        var target = new CompletionTarget();
        Assert.That(target.Label, Is.Empty);
        Assert.That(target.Text, Is.Null);
    }

    [Test]
    public void DapModule_Defaults_AreCorrect()
    {
        var module = new DapModule();
        Assert.That(module.Id, Is.Empty);
        Assert.That(module.Name, Is.Empty);
        Assert.That(module.Path, Is.Null);
    }

    #endregion

    #region Event Args Tests

    [Test]
    public void DapStateChangedEventArgs_StoresValues()
    {
        var args = new DapStateChangedEventArgs(DapConnectionState.Disconnected, DapConnectionState.Connecting, "test error");
        Assert.That(args.OldState, Is.EqualTo(DapConnectionState.Disconnected));
        Assert.That(args.NewState, Is.EqualTo(DapConnectionState.Connecting));
        Assert.That(args.Error, Is.EqualTo("test error"));
    }

    [Test]
    public void DapStoppedEventArgs_StoresValues()
    {
        var args = new DapStoppedEventArgs("breakpoint", "hit breakpoint", 1, allThreadsStopped: true);
        Assert.That(args.Reason, Is.EqualTo("breakpoint"));
        Assert.That(args.Description, Is.EqualTo("hit breakpoint"));
        Assert.That(args.ThreadId, Is.EqualTo(1));
        Assert.That(args.AllThreadsStopped, Is.True);
    }

    [Test]
    public void ContinuedEventArgs_StoresValues()
    {
        var args = new ContinuedEventArgs(5, true);
        Assert.That(args.ThreadId, Is.EqualTo(5));
        Assert.That(args.AllThreadsContinued, Is.True);
    }

    [Test]
    public void DapOutputEventArgs_StoresValues()
    {
        var args = new DapOutputEventArgs("console", "test output");
        Assert.That(args.Category, Is.EqualTo("console"));
        Assert.That(args.Output, Is.EqualTo("test output"));
    }

    [Test]
    public void TerminatedEventArgs_StoresValues()
    {
        var args = new TerminatedEventArgs(true);
        Assert.That(args.Restart, Is.True);
    }

    [Test]
    public void ThreadEventArgs_StoresValues()
    {
        var args = new ThreadEventArgs("started", 10);
        Assert.That(args.Reason, Is.EqualTo("started"));
        Assert.That(args.ThreadId, Is.EqualTo(10));
    }

    [Test]
    public void BreakpointEventArgs_StoresValues()
    {
        var bp = new DapBreakpoint { Id = 1, Verified = true };
        var args = new BreakpointEventArgs("changed", bp);
        Assert.That(args.Reason, Is.EqualTo("changed"));
        Assert.That(args.Breakpoint.Id, Is.EqualTo(1));
        Assert.That(args.Breakpoint.Verified, Is.True);
    }

    [Test]
    public void ModuleEventArgs_StoresValues()
    {
        var module = new DapModule { Id = "1", Name = "test.dll" };
        var args = new ModuleEventArgs("new", module);
        Assert.That(args.Reason, Is.EqualTo("new"));
        Assert.That(args.Module.Name, Is.EqualTo("test.dll"));
    }

    #endregion

    #region DapException Tests

    [Test]
    public void DapException_StoresMessage()
    {
        var ex = new DapException("Test error message");
        Assert.That(ex.Message, Is.EqualTo("Test error message"));
    }

    #endregion

    #region Configuration Property Tests

    [Test]
    public void LaunchRequest_CanSetProperties()
    {
        var request = new LaunchRequest
        {
            Program = "/path/to/program",
            Args = new List<string> { "arg1", "arg2" },
            Cwd = "/working/dir",
            Env = new Dictionary<string, string> { ["KEY"] = "value" },
            StopOnEntry = true,
            NoDebug = true
        };

        Assert.That(request.Program, Is.EqualTo("/path/to/program"));
        Assert.That(request.Args, Has.Count.EqualTo(2));
        Assert.That(request.Cwd, Is.EqualTo("/working/dir"));
        Assert.That(request.Env, Has.Count.EqualTo(1));
        Assert.That(request.StopOnEntry, Is.True);
        Assert.That(request.NoDebug, Is.True);
    }

    [Test]
    public void AttachRequest_CanSetProperties()
    {
        var request = new AttachRequest
        {
            ProcessId = 12345,
            ProcessName = "process.exe"
        };

        Assert.That(request.ProcessId, Is.EqualTo(12345));
        Assert.That(request.ProcessName, Is.EqualTo("process.exe"));
    }

    [Test]
    public void DapSourceBreakpoint_CanSetProperties()
    {
        var bp = new DapSourceBreakpoint
        {
            Line = 42,
            Column = 10,
            Condition = "x > 5",
            HitCondition = "3",
            LogMessage = "hit breakpoint"
        };

        Assert.That(bp.Line, Is.EqualTo(42));
        Assert.That(bp.Column, Is.EqualTo(10));
        Assert.That(bp.Condition, Is.EqualTo("x > 5"));
        Assert.That(bp.HitCondition, Is.EqualTo("3"));
        Assert.That(bp.LogMessage, Is.EqualTo("hit breakpoint"));
    }

    [Test]
    public void ExceptionBreakpointsFilter_Defaults_AreCorrect()
    {
        var filter = new ExceptionBreakpointsFilter();
        Assert.That(filter.Filter, Is.Empty);
        Assert.That(filter.Label, Is.Empty);
        Assert.That(filter.Default, Is.False);
    }

    #endregion
}
