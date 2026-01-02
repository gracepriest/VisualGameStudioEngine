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
