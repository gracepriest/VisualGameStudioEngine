using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

[TestFixture]
public class TerminalServiceTests
{
    private TerminalService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new TerminalService();
    }

    [TearDown]
    public void TearDown()
    {
        _service?.Dispose();
    }

    #region Session Management Tests

    [Test]
    public void Sessions_Initially_ReturnsEmptyList()
    {
        Assert.That(_service.Sessions, Is.Empty);
    }

    [Test]
    public void ActiveSession_Initially_ReturnsNull()
    {
        Assert.That(_service.ActiveSession, Is.Null);
    }

    [Test]
    public void CreateSession_WithDefaultOptions_CreatesSession()
    {
        var session = _service.CreateSession();

        Assert.That(session, Is.Not.Null);
        Assert.That(session.Id, Is.Not.Empty);
        Assert.That(session.Name, Does.StartWith("Terminal"));
        Assert.That(session.WorkingDirectory, Is.Not.Empty);
    }

    [Test]
    public void CreateSession_WithCustomName_UsesCustomName()
    {
        var options = new TerminalOptions { Name = "My Terminal" };
        var session = _service.CreateSession(options);

        Assert.That(session.Name, Is.EqualTo("My Terminal"));
    }

    [Test]
    public void CreateSession_WithWorkingDirectory_UsesWorkingDirectory()
    {
        var workDir = Environment.CurrentDirectory;
        var options = new TerminalOptions { WorkingDirectory = workDir };
        var session = _service.CreateSession(options);

        Assert.That(session.WorkingDirectory, Is.EqualTo(workDir));
    }

    [Test]
    public void CreateSession_AddsToSessionsList()
    {
        var session = _service.CreateSession();

        Assert.That(_service.Sessions, Has.Count.EqualTo(1));
        Assert.That(_service.Sessions[0].Id, Is.EqualTo(session.Id));
    }

    [Test]
    public void CreateSession_FirstSession_BecomesActiveSession()
    {
        var session = _service.CreateSession();

        Assert.That(_service.ActiveSession, Is.Not.Null);
        Assert.That(_service.ActiveSession!.Id, Is.EqualTo(session.Id));
    }

    [Test]
    public void CreateSession_SecondSession_DoesNotChangeActiveSession()
    {
        var firstSession = _service.CreateSession();
        var secondSession = _service.CreateSession();

        Assert.That(_service.ActiveSession!.Id, Is.EqualTo(firstSession.Id));
        Assert.That(_service.Sessions, Has.Count.EqualTo(2));
    }

    [Test]
    public void CreateSession_RaisesSessionCreatedEvent()
    {
        TerminalSession? createdSession = null;
        _service.SessionCreated += (s, e) => createdSession = e.Session;

        var session = _service.CreateSession();

        Assert.That(createdSession, Is.Not.Null);
        Assert.That(createdSession!.Id, Is.EqualTo(session.Id));
    }

    [Test]
    public void CloseSession_RemovesFromSessionsList()
    {
        var session = _service.CreateSession();
        _service.CloseSession(session.Id);

        Assert.That(_service.Sessions, Is.Empty);
    }

    [Test]
    public void CloseSession_RaisesSessionClosedEvent()
    {
        var session = _service.CreateSession();
        TerminalSession? closedSession = null;
        _service.SessionClosed += (s, e) => closedSession = e.Session;

        _service.CloseSession(session.Id);

        Assert.That(closedSession, Is.Not.Null);
        Assert.That(closedSession!.Id, Is.EqualTo(session.Id));
    }

    [Test]
    public void CloseSession_ActiveSession_SetsNewActiveSession()
    {
        var first = _service.CreateSession();
        var second = _service.CreateSession();
        _service.SetActiveSession(first.Id);

        _service.CloseSession(first.Id);

        Assert.That(_service.ActiveSession, Is.Not.Null);
        Assert.That(_service.ActiveSession!.Id, Is.EqualTo(second.Id));
    }

    [Test]
    public void CloseSession_LastSession_SetsActiveSessionToNull()
    {
        var session = _service.CreateSession();
        _service.CloseSession(session.Id);

        Assert.That(_service.ActiveSession, Is.Null);
    }

    [Test]
    public void CloseSession_NonExistentSession_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => _service.CloseSession("nonexistent"));
    }

    [Test]
    public void SetActiveSession_ValidSession_ChangesActiveSession()
    {
        var first = _service.CreateSession();
        var second = _service.CreateSession();

        _service.SetActiveSession(second.Id);

        Assert.That(_service.ActiveSession!.Id, Is.EqualTo(second.Id));
    }

    [Test]
    public void SetActiveSession_RaisesActiveSessionChangedEvent()
    {
        var first = _service.CreateSession();
        var second = _service.CreateSession();
        TerminalSession? changedSession = null;
        _service.ActiveSessionChanged += (s, e) => changedSession = e.Session;

        _service.SetActiveSession(second.Id);

        Assert.That(changedSession, Is.Not.Null);
        Assert.That(changedSession!.Id, Is.EqualTo(second.Id));
    }

    [Test]
    public void SetActiveSession_NonExistentSession_DoesNotChangeActiveSession()
    {
        var session = _service.CreateSession();
        _service.SetActiveSession("nonexistent");

        Assert.That(_service.ActiveSession!.Id, Is.EqualTo(session.Id));
    }

    #endregion

    #region History Tests

    [Test]
    public void GetHistory_NewSession_ReturnsEmptyList()
    {
        var session = _service.CreateSession();
        var history = _service.GetHistory(session.Id);

        Assert.That(history, Is.Empty);
    }

    [Test]
    public void GetHistory_NonExistentSession_ReturnsEmptyList()
    {
        var history = _service.GetHistory("nonexistent");

        Assert.That(history, Is.Empty);
    }

    [Test]
    public void Clear_ClearsSessionHistory()
    {
        var session = _service.CreateSession();
        // Send some input to generate history
        _service.SendInput(session.Id, "test");

        _service.Clear(session.Id);

        var history = _service.GetHistory(session.Id);
        Assert.That(history, Is.Empty);
    }

    [Test]
    public void Clear_WithNullSessionId_ClearsActiveSessionHistory()
    {
        var session = _service.CreateSession();
        _service.SendInput("test");

        _service.Clear();

        var history = _service.GetHistory(session.Id);
        Assert.That(history, Is.Empty);
    }

    #endregion

    #region Input Tests

    [Test]
    public void SendInput_ToActiveSession_SendsToActiveSession()
    {
        var session = _service.CreateSession();
        bool outputReceived = false;
        _service.OutputReceived += (s, e) =>
        {
            if (e.SessionId == session.Id && e.Output.Type == TerminalOutputType.Input)
            {
                outputReceived = true;
            }
        };

        _service.SendInput("test");

        // Give a small delay for async output
        Thread.Sleep(100);
        Assert.That(outputReceived, Is.True);
    }

    [Test]
    public void SendInput_NoActiveSession_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => _service.SendInput("test"));
    }

    [Test]
    public void SendInput_ToSpecificSession_SendsToThatSession()
    {
        var first = _service.CreateSession();
        var second = _service.CreateSession();
        string? receivedSessionId = null;
        _service.OutputReceived += (s, e) =>
        {
            if (e.Output.Type == TerminalOutputType.Input)
            {
                receivedSessionId = e.SessionId;
            }
        };

        _service.SendInput(second.Id, "test");

        Thread.Sleep(100);
        Assert.That(receivedSessionId, Is.EqualTo(second.Id));
    }

    [Test]
    public void SendInput_NonExistentSession_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => _service.SendInput("nonexistent", "test"));
    }

    #endregion

    #region ExecuteCommandAsync Tests

    [Test]
    public async Task ExecuteCommandAsync_SimpleCommand_ReturnsResult()
    {
        var result = await _service.ExecuteCommandAsync("echo hello");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Command, Is.EqualTo("echo hello"));
        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.StandardOutput, Does.Contain("hello"));
    }

    [Test]
    public async Task ExecuteCommandAsync_WithWorkingDirectory_UsesWorkingDirectory()
    {
        var workDir = Environment.CurrentDirectory;
        var result = await _service.ExecuteCommandAsync("cd", workDir);

        Assert.That(result.ExitCode, Is.EqualTo(0));
    }

    [Test]
    public async Task ExecuteCommandAsync_FailingCommand_ReturnsNonZeroExitCode()
    {
        // Use a command that will fail
        var result = await _service.ExecuteCommandAsync("exit 1");

        Assert.That(result.ExitCode, Is.Not.EqualTo(0));
        Assert.That(result.Success, Is.False);
    }

    [Test]
    public async Task ExecuteCommandAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _service.ExecuteCommandAsync("ping localhost -n 10", cancellationToken: cts.Token));
    }

    [Test]
    public async Task ExecuteCommandAsync_RecordsDuration()
    {
        var result = await _service.ExecuteCommandAsync("echo test");

        Assert.That(result.Duration, Is.GreaterThan(TimeSpan.Zero));
        Assert.That(result.StartTime, Is.GreaterThan(DateTime.MinValue));
    }

    #endregion

    #region ExecuteInBackground Tests

    [Test]
    public void ExecuteInBackground_CreatesNewSession()
    {
        var session = _service.ExecuteInBackground("echo background");

        Assert.That(session, Is.Not.Null);
        Assert.That(_service.Sessions, Has.Count.EqualTo(1));
    }

    [Test]
    public void ExecuteInBackground_WithWorkingDirectory_UsesWorkingDirectory()
    {
        var workDir = Environment.CurrentDirectory;
        var session = _service.ExecuteInBackground("echo test", workDir);

        Assert.That(session.WorkingDirectory, Is.EqualTo(workDir));
    }

    [Test]
    public void ExecuteInBackground_SessionName_ContainsCommandPrefix()
    {
        var session = _service.ExecuteInBackground("echo hello world");

        Assert.That(session.Name, Does.StartWith("Command:"));
    }

    #endregion

    #region Dispose Tests

    [Test]
    public void Dispose_ClosesAllSessions()
    {
        _service.CreateSession();
        _service.CreateSession();
        _service.CreateSession();

        _service.Dispose();

        Assert.That(_service.Sessions, Is.Empty);
    }

    [Test]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        _service.CreateSession();

        Assert.DoesNotThrow(() =>
        {
            _service.Dispose();
            _service.Dispose();
        });
    }

    #endregion

    #region TerminalOptions Tests

    [Test]
    public void TerminalOptions_Defaults_AreCorrect()
    {
        var options = new TerminalOptions();

        Assert.That(options.Shell, Is.Null);
        Assert.That(options.WorkingDirectory, Is.Null);
        Assert.That(options.EnvironmentVariables, Is.Null);
        Assert.That(options.Name, Is.Null);
        Assert.That(options.UseSystemShell, Is.True);
        Assert.That(options.MaxHistoryLines, Is.EqualTo(10000));
    }

    [Test]
    public void TerminalOptions_CanSetEnvironmentVariables()
    {
        var options = new TerminalOptions
        {
            EnvironmentVariables = new Dictionary<string, string>
            {
                { "MY_VAR", "my_value" }
            }
        };

        Assert.That(options.EnvironmentVariables, Has.Count.EqualTo(1));
        Assert.That(options.EnvironmentVariables["MY_VAR"], Is.EqualTo("my_value"));
    }

    #endregion

    #region TerminalSession Tests

    [Test]
    public void TerminalSession_Defaults_AreCorrect()
    {
        var session = new TerminalSession();

        Assert.That(session.Id, Is.Not.Empty);
        Assert.That(session.Name, Is.EqualTo("Terminal"));
        Assert.That(session.Shell, Is.Empty);
        Assert.That(session.WorkingDirectory, Is.Empty);
        Assert.That(session.IsRunning, Is.False);
        Assert.That(session.CreatedAt, Is.GreaterThan(DateTime.MinValue));
        Assert.That(session.CurrentCommand, Is.Null);
        Assert.That(session.LastExitCode, Is.Null);
    }

    [Test]
    public void TerminalSession_Id_IsUniqueGuid()
    {
        var session1 = new TerminalSession();
        var session2 = new TerminalSession();

        Assert.That(session1.Id, Is.Not.EqualTo(session2.Id));
        Assert.That(Guid.TryParse(session1.Id, out _), Is.True);
    }

    #endregion

    #region TerminalOutput Tests

    [Test]
    public void TerminalOutput_Defaults_AreCorrect()
    {
        var output = new TerminalOutput();

        Assert.That(output.Text, Is.Empty);
        Assert.That(output.Type, Is.EqualTo(TerminalOutputType.StandardOutput));
        Assert.That(output.Timestamp, Is.GreaterThan(DateTime.MinValue));
    }

    [Test]
    public void TerminalOutput_CanSetProperties()
    {
        var output = new TerminalOutput
        {
            Text = "Hello",
            Type = TerminalOutputType.StandardError,
            Timestamp = DateTime.Now
        };

        Assert.That(output.Text, Is.EqualTo("Hello"));
        Assert.That(output.Type, Is.EqualTo(TerminalOutputType.StandardError));
    }

    #endregion

    #region TerminalOutputType Tests

    [Test]
    public void TerminalOutputType_HasExpectedValues()
    {
        Assert.That(Enum.GetValues<TerminalOutputType>(), Has.Length.EqualTo(4));
        Assert.That((int)TerminalOutputType.StandardOutput, Is.EqualTo(0));
        Assert.That((int)TerminalOutputType.StandardError, Is.EqualTo(1));
        Assert.That((int)TerminalOutputType.Input, Is.EqualTo(2));
        Assert.That((int)TerminalOutputType.System, Is.EqualTo(3));
    }

    #endregion

    #region CommandResult Tests

    [Test]
    public void CommandResult_Success_IsTrueWhenExitCodeIsZero()
    {
        var result = new CommandResult { ExitCode = 0 };
        Assert.That(result.Success, Is.True);
    }

    [Test]
    public void CommandResult_Success_IsFalseWhenExitCodeIsNonZero()
    {
        var result = new CommandResult { ExitCode = 1 };
        Assert.That(result.Success, Is.False);
    }

    [Test]
    public void CommandResult_Defaults_AreCorrect()
    {
        var result = new CommandResult();

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.StandardOutput, Is.Empty);
        Assert.That(result.StandardError, Is.Empty);
        Assert.That(result.Command, Is.Empty);
        Assert.That(result.Duration, Is.EqualTo(TimeSpan.Zero));
    }

    #endregion

    #region Event Args Tests

    [Test]
    public void TerminalOutputEventArgs_StoresSessionIdAndOutput()
    {
        var output = new TerminalOutput { Text = "test" };
        var args = new TerminalOutputEventArgs("session1", output);

        Assert.That(args.SessionId, Is.EqualTo("session1"));
        Assert.That(args.Output, Is.SameAs(output));
    }

    [Test]
    public void TerminalSessionEventArgs_StoresSession()
    {
        var session = new TerminalSession { Name = "Test" };
        var args = new TerminalSessionEventArgs(session);

        Assert.That(args.Session, Is.SameAs(session));
    }

    [Test]
    public void CommandCompletedEventArgs_StoresSessionIdAndResult()
    {
        var result = new CommandResult { Command = "test" };
        var args = new CommandCompletedEventArgs("session1", result);

        Assert.That(args.SessionId, Is.EqualTo("session1"));
        Assert.That(args.Result, Is.SameAs(result));
    }

    #endregion

    #region Output Received Tests

    [Test]
    public void OutputReceived_RaisedWhenOutputReceived()
    {
        var session = _service.CreateSession();
        var outputs = new List<TerminalOutput>();
        _service.OutputReceived += (s, e) => outputs.Add(e.Output);

        _service.SendInput(session.Id, "echo test");

        // Wait for output
        Thread.Sleep(200);

        Assert.That(outputs, Is.Not.Empty);
        Assert.That(outputs.Any(o => o.Type == TerminalOutputType.Input), Is.True);
    }

    #endregion
}
