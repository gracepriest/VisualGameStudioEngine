using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Tests.Core;

[TestFixture]
public class DebugStateTests
{
    [Test]
    public void NotStarted_HasValue0()
    {
        Assert.That((int)DebugState.NotStarted, Is.EqualTo(0));
    }

    [Test]
    public void Initializing_HasValue1()
    {
        Assert.That((int)DebugState.Initializing, Is.EqualTo(1));
    }

    [Test]
    public void Running_HasValue2()
    {
        Assert.That((int)DebugState.Running, Is.EqualTo(2));
    }

    [Test]
    public void Paused_HasValue3()
    {
        Assert.That((int)DebugState.Paused, Is.EqualTo(3));
    }

    [Test]
    public void Stopped_HasValue4()
    {
        Assert.That((int)DebugState.Stopped, Is.EqualTo(4));
    }

    [Test]
    public void HasFiveValues()
    {
        var values = Enum.GetValues<DebugState>();
        Assert.That(values, Has.Length.EqualTo(5));
    }
}

[TestFixture]
public class StopReasonTests
{
    [Test]
    public void Step_HasValue0()
    {
        Assert.That((int)StopReason.Step, Is.EqualTo(0));
    }

    [Test]
    public void Breakpoint_HasValue1()
    {
        Assert.That((int)StopReason.Breakpoint, Is.EqualTo(1));
    }

    [Test]
    public void Exception_HasValue2()
    {
        Assert.That((int)StopReason.Exception, Is.EqualTo(2));
    }

    [Test]
    public void Pause_HasValue3()
    {
        Assert.That((int)StopReason.Pause, Is.EqualTo(3));
    }

    [Test]
    public void Entry_HasValue4()
    {
        Assert.That((int)StopReason.Entry, Is.EqualTo(4));
    }

    [Test]
    public void Goto_HasValue5()
    {
        Assert.That((int)StopReason.Goto, Is.EqualTo(5));
    }

    [Test]
    public void FunctionBreakpoint_HasValue6()
    {
        Assert.That((int)StopReason.FunctionBreakpoint, Is.EqualTo(6));
    }

    [Test]
    public void DataBreakpoint_HasValue7()
    {
        Assert.That((int)StopReason.DataBreakpoint, Is.EqualTo(7));
    }

    [Test]
    public void HasEightValues()
    {
        var values = Enum.GetValues<StopReason>();
        Assert.That(values, Has.Length.EqualTo(8));
    }
}

[TestFixture]
public class DebugStateChangedEventArgsTests
{
    [Test]
    public void DefaultValues_AreDefault()
    {
        var args = new DebugStateChangedEventArgs();

        Assert.That(args.OldState, Is.EqualTo(DebugState.NotStarted));
        Assert.That(args.NewState, Is.EqualTo(DebugState.NotStarted));
    }

    [Test]
    public void OldState_CanBeSetAndRetrieved()
    {
        var args = new DebugStateChangedEventArgs { OldState = DebugState.Running };

        Assert.That(args.OldState, Is.EqualTo(DebugState.Running));
    }

    [Test]
    public void NewState_CanBeSetAndRetrieved()
    {
        var args = new DebugStateChangedEventArgs { NewState = DebugState.Paused };

        Assert.That(args.NewState, Is.EqualTo(DebugState.Paused));
    }

    [Test]
    public void InheritsFromEventArgs()
    {
        var args = new DebugStateChangedEventArgs();

        Assert.That(args, Is.InstanceOf<EventArgs>());
    }
}

[TestFixture]
public class StoppedEventArgsTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
    {
        var args = new StoppedEventArgs();

        Assert.That(args.Reason, Is.EqualTo(StopReason.Step));
        Assert.That(args.ThreadId, Is.EqualTo(0));
        Assert.That(args.Description, Is.Null);
        Assert.That(args.Text, Is.Null);
        Assert.That(args.AllThreadsStopped, Is.False);
    }

    [Test]
    public void Reason_CanBeSetAndRetrieved()
    {
        var args = new StoppedEventArgs { Reason = StopReason.Breakpoint };

        Assert.That(args.Reason, Is.EqualTo(StopReason.Breakpoint));
    }

    [Test]
    public void ThreadId_CanBeSetAndRetrieved()
    {
        var args = new StoppedEventArgs { ThreadId = 42 };

        Assert.That(args.ThreadId, Is.EqualTo(42));
    }

    [Test]
    public void Description_CanBeSetAndRetrieved()
    {
        var args = new StoppedEventArgs { Description = "Hit breakpoint at line 10" };

        Assert.That(args.Description, Is.EqualTo("Hit breakpoint at line 10"));
    }

    [Test]
    public void Text_CanBeSetAndRetrieved()
    {
        var args = new StoppedEventArgs { Text = "Additional info" };

        Assert.That(args.Text, Is.EqualTo("Additional info"));
    }

    [Test]
    public void AllThreadsStopped_CanBeSetToTrue()
    {
        var args = new StoppedEventArgs { AllThreadsStopped = true };

        Assert.That(args.AllThreadsStopped, Is.True);
    }
}

[TestFixture]
public class DebugOutputEventArgsTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
    {
        var args = new DebugOutputEventArgs();

        Assert.That(args.Category, Is.EqualTo("console"));
        Assert.That(args.Output, Is.EqualTo(""));
    }

    [Test]
    public void Category_CanBeSetAndRetrieved()
    {
        var args = new DebugOutputEventArgs { Category = "stderr" };

        Assert.That(args.Category, Is.EqualTo("stderr"));
    }

    [Test]
    public void Output_CanBeSetAndRetrieved()
    {
        var args = new DebugOutputEventArgs { Output = "Hello, World!" };

        Assert.That(args.Output, Is.EqualTo("Hello, World!"));
    }
}

[TestFixture]
public class BreakpointsChangedEventArgsTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
    {
        var args = new BreakpointsChangedEventArgs();

        Assert.That(args.FilePath, Is.EqualTo(""));
        Assert.That(args.Breakpoints, Is.Empty);
    }

    [Test]
    public void FilePath_CanBeSetAndRetrieved()
    {
        var args = new BreakpointsChangedEventArgs { FilePath = "/path/to/file.bas" };

        Assert.That(args.FilePath, Is.EqualTo("/path/to/file.bas"));
    }

    [Test]
    public void Breakpoints_CanBeSetAndRetrieved()
    {
        var breakpoints = new List<BreakpointInfo>
        {
            new() { Id = 1, Line = 10, Verified = true }
        };
        var args = new BreakpointsChangedEventArgs { Breakpoints = breakpoints };

        Assert.That(args.Breakpoints, Has.Count.EqualTo(1));
    }
}

[TestFixture]
public class DebugConfigurationTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
    {
        var config = new DebugConfiguration();

        Assert.That(config.Program, Is.EqualTo(""));
        Assert.That(config.WorkingDirectory, Is.EqualTo(""));
        Assert.That(config.Arguments, Is.Empty);
        Assert.That(config.Environment, Is.Empty);
        Assert.That(config.StopOnEntry, Is.False);
    }

    [Test]
    public void Program_CanBeSetAndRetrieved()
    {
        var config = new DebugConfiguration { Program = "/path/to/app.exe" };

        Assert.That(config.Program, Is.EqualTo("/path/to/app.exe"));
    }

    [Test]
    public void WorkingDirectory_CanBeSetAndRetrieved()
    {
        var config = new DebugConfiguration { WorkingDirectory = "/path/to/project" };

        Assert.That(config.WorkingDirectory, Is.EqualTo("/path/to/project"));
    }

    [Test]
    public void Arguments_CanBeSetAndRetrieved()
    {
        var config = new DebugConfiguration { Arguments = new[] { "--verbose", "--port", "8080" } };

        Assert.That(config.Arguments, Has.Length.EqualTo(3));
        Assert.That(config.Arguments[0], Is.EqualTo("--verbose"));
    }

    [Test]
    public void Environment_CanAddVariables()
    {
        var config = new DebugConfiguration();
        config.Environment["PATH"] = "/custom/path";
        config.Environment["DEBUG"] = "true";

        Assert.That(config.Environment, Has.Count.EqualTo(2));
    }

    [Test]
    public void StopOnEntry_CanBeSetToTrue()
    {
        var config = new DebugConfiguration { StopOnEntry = true };

        Assert.That(config.StopOnEntry, Is.True);
    }
}

[TestFixture]
public class SourceBreakpointTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
    {
        var bp = new SourceBreakpoint();

        Assert.That(bp.Line, Is.EqualTo(0));
        Assert.That(bp.Column, Is.Null);
        Assert.That(bp.Condition, Is.Null);
        Assert.That(bp.HitCondition, Is.Null);
        Assert.That(bp.LogMessage, Is.Null);
    }

    [Test]
    public void Line_CanBeSetAndRetrieved()
    {
        var bp = new SourceBreakpoint { Line = 42 };

        Assert.That(bp.Line, Is.EqualTo(42));
    }

    [Test]
    public void Column_CanBeSetAndRetrieved()
    {
        var bp = new SourceBreakpoint { Column = 10 };

        Assert.That(bp.Column, Is.EqualTo(10));
    }

    [Test]
    public void Condition_CanBeSetAndRetrieved()
    {
        var bp = new SourceBreakpoint { Condition = "x > 5" };

        Assert.That(bp.Condition, Is.EqualTo("x > 5"));
    }

    [Test]
    public void HitCondition_CanBeSetAndRetrieved()
    {
        var bp = new SourceBreakpoint { HitCondition = ">= 10" };

        Assert.That(bp.HitCondition, Is.EqualTo(">= 10"));
    }

    [Test]
    public void LogMessage_CanBeSetAndRetrieved()
    {
        var bp = new SourceBreakpoint { LogMessage = "Value of x: {x}" };

        Assert.That(bp.LogMessage, Is.EqualTo("Value of x: {x}"));
    }
}

[TestFixture]
public class BreakpointInfoTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
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
    public void AllProperties_CanBeSet()
    {
        var info = new BreakpointInfo
        {
            Id = 1,
            Verified = true,
            Message = "Breakpoint set",
            Line = 10,
            Column = 5,
            EndLine = 10,
            EndColumn = 20
        };

        Assert.That(info.Id, Is.EqualTo(1));
        Assert.That(info.Verified, Is.True);
        Assert.That(info.Message, Is.EqualTo("Breakpoint set"));
        Assert.That(info.Line, Is.EqualTo(10));
        Assert.That(info.Column, Is.EqualTo(5));
        Assert.That(info.EndLine, Is.EqualTo(10));
        Assert.That(info.EndColumn, Is.EqualTo(20));
    }
}

[TestFixture]
public class StackFrameInfoTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
    {
        var frame = new StackFrameInfo();

        Assert.That(frame.Id, Is.EqualTo(0));
        Assert.That(frame.Name, Is.EqualTo(""));
        Assert.That(frame.FilePath, Is.Null);
        Assert.That(frame.Line, Is.EqualTo(0));
        Assert.That(frame.Column, Is.EqualTo(0));
        Assert.That(frame.EndLine, Is.Null);
        Assert.That(frame.EndColumn, Is.Null);
        Assert.That(frame.ModuleName, Is.Null);
    }

    [Test]
    public void AllProperties_CanBeSet()
    {
        var frame = new StackFrameInfo
        {
            Id = 1,
            Name = "Main",
            FilePath = "/path/to/file.bas",
            Line = 10,
            Column = 5,
            EndLine = 15,
            EndColumn = 1,
            ModuleName = "Program"
        };

        Assert.That(frame.Id, Is.EqualTo(1));
        Assert.That(frame.Name, Is.EqualTo("Main"));
        Assert.That(frame.FilePath, Is.EqualTo("/path/to/file.bas"));
        Assert.That(frame.Line, Is.EqualTo(10));
        Assert.That(frame.Column, Is.EqualTo(5));
        Assert.That(frame.EndLine, Is.EqualTo(15));
        Assert.That(frame.EndColumn, Is.EqualTo(1));
        Assert.That(frame.ModuleName, Is.EqualTo("Program"));
    }
}

[TestFixture]
public class ScopeInfoTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
    {
        var scope = new ScopeInfo();

        Assert.That(scope.Name, Is.EqualTo(""));
        Assert.That(scope.VariablesReference, Is.EqualTo(0));
        Assert.That(scope.Expensive, Is.False);
    }

    [Test]
    public void AllProperties_CanBeSet()
    {
        var scope = new ScopeInfo
        {
            Name = "Locals",
            VariablesReference = 100,
            Expensive = true
        };

        Assert.That(scope.Name, Is.EqualTo("Locals"));
        Assert.That(scope.VariablesReference, Is.EqualTo(100));
        Assert.That(scope.Expensive, Is.True);
    }
}

[TestFixture]
public class VariableInfoTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
    {
        var variable = new VariableInfo();

        Assert.That(variable.Name, Is.EqualTo(""));
        Assert.That(variable.Value, Is.EqualTo(""));
        Assert.That(variable.Type, Is.Null);
        Assert.That(variable.VariablesReference, Is.EqualTo(0));
    }

    [Test]
    public void AllProperties_CanBeSet()
    {
        var variable = new VariableInfo
        {
            Name = "counter",
            Value = "42",
            Type = "Integer",
            VariablesReference = 0
        };

        Assert.That(variable.Name, Is.EqualTo("counter"));
        Assert.That(variable.Value, Is.EqualTo("42"));
        Assert.That(variable.Type, Is.EqualTo("Integer"));
    }

    [Test]
    public void VariablesReference_NonZeroForComplexTypes()
    {
        var variable = new VariableInfo
        {
            Name = "myObject",
            Value = "{...}",
            Type = "MyClass",
            VariablesReference = 123
        };

        Assert.That(variable.VariablesReference, Is.EqualTo(123));
    }
}

[TestFixture]
public class EvaluateResultTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
    {
        var result = new EvaluateResult();

        Assert.That(result.Result, Is.EqualTo(""));
        Assert.That(result.Type, Is.Null);
        Assert.That(result.VariablesReference, Is.EqualTo(0));
    }

    [Test]
    public void AllProperties_CanBeSet()
    {
        var result = new EvaluateResult
        {
            Result = "42",
            Type = "Integer",
            VariablesReference = 0
        };

        Assert.That(result.Result, Is.EqualTo("42"));
        Assert.That(result.Type, Is.EqualTo("Integer"));
    }
}

[TestFixture]
public class FunctionBreakpointTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
    {
        var bp = new FunctionBreakpoint();

        Assert.That(bp.Name, Is.EqualTo(""));
        Assert.That(bp.Condition, Is.Null);
        Assert.That(bp.HitCondition, Is.Null);
    }

    [Test]
    public void AllProperties_CanBeSet()
    {
        var bp = new FunctionBreakpoint
        {
            Name = "MyClass.MyMethod",
            Condition = "param > 0",
            HitCondition = ">= 5"
        };

        Assert.That(bp.Name, Is.EqualTo("MyClass.MyMethod"));
        Assert.That(bp.Condition, Is.EqualTo("param > 0"));
        Assert.That(bp.HitCondition, Is.EqualTo(">= 5"));
    }
}

[TestFixture]
public class FunctionBreakpointInfoTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
    {
        var info = new FunctionBreakpointInfo();

        Assert.That(info.Id, Is.EqualTo(0));
        Assert.That(info.Verified, Is.False);
        Assert.That(info.Message, Is.Null);
    }

    [Test]
    public void AllProperties_CanBeSet()
    {
        var info = new FunctionBreakpointInfo
        {
            Id = 1,
            Verified = true,
            Message = "Function breakpoint set"
        };

        Assert.That(info.Id, Is.EqualTo(1));
        Assert.That(info.Verified, Is.True);
        Assert.That(info.Message, Is.EqualTo("Function breakpoint set"));
    }
}

[TestFixture]
public class ExceptionFilterOptionTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
    {
        var option = new ExceptionFilterOption();

        Assert.That(option.FilterId, Is.EqualTo(""));
        Assert.That(option.Condition, Is.Null);
    }

    [Test]
    public void AllProperties_CanBeSet()
    {
        var option = new ExceptionFilterOption
        {
            FilterId = "NullReferenceException",
            Condition = "true"
        };

        Assert.That(option.FilterId, Is.EqualTo("NullReferenceException"));
        Assert.That(option.Condition, Is.EqualTo("true"));
    }
}
