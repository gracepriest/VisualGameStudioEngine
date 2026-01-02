using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Integration;

[TestFixture]
public class EventAggregatorIntegrationTests
{
    private EventAggregator _eventAggregator = null!;

    [SetUp]
    public void SetUp()
    {
        _eventAggregator = new EventAggregator();
    }

    [Test]
    public void FileOpenedEvent_CanPublishAndSubscribe()
    {
        FileOpenedEvent? receivedEvent = null;
        _eventAggregator.Subscribe<FileOpenedEvent>(e => receivedEvent = e);

        _eventAggregator.Publish(new FileOpenedEvent(@"C:\Projects\test.bas", "Module Test\nEnd Module"));

        Assert.That(receivedEvent, Is.Not.Null);
        Assert.That(receivedEvent!.FilePath, Is.EqualTo(@"C:\Projects\test.bas"));
    }

    [Test]
    public void FileSavedEvent_CanPublishAndSubscribe()
    {
        FileSavedEvent? receivedEvent = null;
        _eventAggregator.Subscribe<FileSavedEvent>(e => receivedEvent = e);

        _eventAggregator.Publish(new FileSavedEvent(@"C:\Projects\test.bas"));

        Assert.That(receivedEvent, Is.Not.Null);
        Assert.That(receivedEvent!.FilePath, Is.EqualTo(@"C:\Projects\test.bas"));
    }

    [Test]
    public void FileClosedEvent_CanPublishAndSubscribe()
    {
        FileClosedEvent? receivedEvent = null;
        _eventAggregator.Subscribe<FileClosedEvent>(e => receivedEvent = e);

        _eventAggregator.Publish(new FileClosedEvent(@"C:\Projects\test.bas"));

        Assert.That(receivedEvent, Is.Not.Null);
        Assert.That(receivedEvent!.FilePath, Is.EqualTo(@"C:\Projects\test.bas"));
    }

    [Test]
    public void ProjectOpenedEvent_CanPublishAndSubscribe()
    {
        ProjectOpenedEvent? receivedEvent = null;
        _eventAggregator.Subscribe<ProjectOpenedEvent>(e => receivedEvent = e);

        var project = new BasicLangProject { Name = "TestProject" };
        _eventAggregator.Publish(new ProjectOpenedEvent(project));

        Assert.That(receivedEvent, Is.Not.Null);
        Assert.That(receivedEvent!.Project.Name, Is.EqualTo("TestProject"));
    }

    [Test]
    public void ProjectClosedEvent_CanPublishAndSubscribe()
    {
        ProjectClosedEvent? receivedEvent = null;
        var project = new BasicLangProject { Name = "TestProject" };
        _eventAggregator.Subscribe<ProjectClosedEvent>(e => receivedEvent = e);

        _eventAggregator.Publish(new ProjectClosedEvent(project));

        Assert.That(receivedEvent, Is.Not.Null);
    }

    [Test]
    public void BuildCompletedEvent_CanPublishAndSubscribe()
    {
        BuildCompletedEvent? receivedEvent = null;
        _eventAggregator.Subscribe<BuildCompletedEvent>(e => receivedEvent = e);

        var result = new BuildResult { Success = true };
        _eventAggregator.Publish(new BuildCompletedEvent(result));

        Assert.That(receivedEvent, Is.Not.Null);
        Assert.That(receivedEvent!.Result.Success, Is.True);
    }

    [Test]
    public void ThemeChangedEvent_CanPublishAndSubscribe()
    {
        ThemeChangedEvent? receivedEvent = null;
        _eventAggregator.Subscribe<ThemeChangedEvent>(e => receivedEvent = e);

        _eventAggregator.Publish(new ThemeChangedEvent("Dark"));

        Assert.That(receivedEvent, Is.Not.Null);
        Assert.That(receivedEvent!.ThemeName, Is.EqualTo("Dark"));
    }

    [Test]
    public void MultipleSubscribers_AllReceiveEvent()
    {
        var received1 = false;
        var received2 = false;
        var received3 = false;

        _eventAggregator.Subscribe<FileOpenedEvent>(_ => received1 = true);
        _eventAggregator.Subscribe<FileOpenedEvent>(_ => received2 = true);
        _eventAggregator.Subscribe<FileOpenedEvent>(_ => received3 = true);

        _eventAggregator.Publish(new FileOpenedEvent("test.bas", ""));

        Assert.That(received1, Is.True);
        Assert.That(received2, Is.True);
        Assert.That(received3, Is.True);
    }

    [Test]
    public void Unsubscribe_StopsReceivingEvents()
    {
        var receiveCount = 0;
        var subscription = _eventAggregator.Subscribe<FileOpenedEvent>(_ => receiveCount++);

        _eventAggregator.Publish(new FileOpenedEvent("test1.bas", ""));
        subscription.Dispose();
        _eventAggregator.Publish(new FileOpenedEvent("test2.bas", ""));

        Assert.That(receiveCount, Is.EqualTo(1));
    }

    [Test]
    public void DifferentEventTypes_IndependentSubscriptions()
    {
        var fileOpenedReceived = false;
        var fileSavedReceived = false;

        _eventAggregator.Subscribe<FileOpenedEvent>(_ => fileOpenedReceived = true);
        _eventAggregator.Subscribe<FileSavedEvent>(_ => fileSavedReceived = true);

        _eventAggregator.Publish(new FileOpenedEvent("test.bas", ""));

        Assert.That(fileOpenedReceived, Is.True);
        Assert.That(fileSavedReceived, Is.False);
    }
}

[TestFixture]
public class ServiceIntegrationTests
{
    private Mock<IOutputService> _mockOutputService = null!;

    [SetUp]
    public void SetUp()
    {
        _mockOutputService = new Mock<IOutputService>();
    }

    [Test]
    public void BookmarkService_WorksIndependently()
    {
        var bookmarkService = new BookmarkService();
        var filePath = Path.GetFullPath("test.bas");

        bookmarkService.ToggleBookmark(filePath, 10);
        bookmarkService.ToggleBookmark(filePath, 20);
        bookmarkService.ToggleBookmark(filePath, 30);

        var bookmarks = bookmarkService.GetBookmarks(filePath);
        Assert.That(bookmarks, Has.Count.EqualTo(3));

        var next = bookmarkService.GetNextBookmark(filePath, 15);
        Assert.That(next, Is.Not.Null);
        Assert.That(next!.Line, Is.EqualTo(20));
    }

    [Test]
    public void DebugService_InitializesCorrectly()
    {
        var debugService = new DebugService(_mockOutputService.Object);

        Assert.That(debugService.State, Is.EqualTo(DebugState.NotStarted));
        Assert.That(debugService.IsDebugging, Is.False);

        debugService.Dispose();
    }

    [Test]
    public void LanguageService_InitializesCorrectly()
    {
        var languageService = new LanguageService(_mockOutputService.Object);

        Assert.That(languageService.IsConnected, Is.False);

        languageService.Dispose();
    }

    [Test]
    public void BuildService_InitializesCorrectly()
    {
        var buildService = new BuildService(_mockOutputService.Object);

        Assert.That(buildService.IsBuilding, Is.False);
        Assert.That(buildService.CurrentConfiguration.Name, Is.EqualTo("Debug"));
    }
}

[TestFixture]
public class DiagnosticsWorkflowTests
{
    [Test]
    public void DiagnosticItem_CanRepresentErrors()
    {
        var diagnostics = new List<DiagnosticItem>
        {
            new()
            {
                Id = "BL0001",
                Message = "Undefined variable 'x'",
                FilePath = @"C:\Projects\main.bas",
                Line = 10,
                Column = 5,
                Severity = DiagnosticSeverity.Error
            },
            new()
            {
                Id = "BL0002",
                Message = "Unused variable 'y'",
                FilePath = @"C:\Projects\main.bas",
                Line = 15,
                Column = 5,
                Severity = DiagnosticSeverity.Warning
            }
        };

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        var warnings = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning).ToList();

        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.That(warnings, Has.Count.EqualTo(1));
    }

    [Test]
    public void BuildResult_AggregatesDiagnostics()
    {
        var result = new BuildResult { Success = false };
        result.Diagnostics.Add(new DiagnosticItem { Severity = DiagnosticSeverity.Error, Message = "Error 1" });
        result.Diagnostics.Add(new DiagnosticItem { Severity = DiagnosticSeverity.Error, Message = "Error 2" });
        result.Diagnostics.Add(new DiagnosticItem { Severity = DiagnosticSeverity.Warning, Message = "Warning 1" });

        Assert.That(result.ErrorCount, Is.EqualTo(2));
        Assert.That(result.WarningCount, Is.EqualTo(1));
        Assert.That(result.Errors.Count(), Is.EqualTo(2));
        Assert.That(result.Warnings.Count(), Is.EqualTo(1));
    }
}

[TestFixture]
public class BreakpointWorkflowTests
{
    [Test]
    public void SourceBreakpoint_CanHaveCondition()
    {
        var bp = new SourceBreakpoint
        {
            Line = 10,
            Condition = "x > 5"
        };

        Assert.That(bp.Line, Is.EqualTo(10));
        Assert.That(bp.Condition, Is.EqualTo("x > 5"));
    }

    [Test]
    public void SourceBreakpoint_CanHaveHitCondition()
    {
        var bp = new SourceBreakpoint
        {
            Line = 10,
            HitCondition = ">= 3"
        };

        Assert.That(bp.HitCondition, Is.EqualTo(">= 3"));
    }

    [Test]
    public void SourceBreakpoint_CanHaveLogMessage()
    {
        var bp = new SourceBreakpoint
        {
            Line = 10,
            LogMessage = "Value of x: {x}"
        };

        Assert.That(bp.LogMessage, Is.EqualTo("Value of x: {x}"));
    }

    [Test]
    public void BreakpointInfo_TracksVerification()
    {
        var unverified = new BreakpointInfo { Id = 1, Verified = false, Line = 10 };
        var verified = new BreakpointInfo { Id = 2, Verified = true, Line = 20 };

        Assert.That(unverified.Verified, Is.False);
        Assert.That(verified.Verified, Is.True);
    }

    [Test]
    public void FunctionBreakpoint_CanTargetSpecificFunction()
    {
        var bp = new FunctionBreakpoint
        {
            Name = "MyModule.Calculate",
            Condition = "input > 0"
        };

        Assert.That(bp.Name, Is.EqualTo("MyModule.Calculate"));
        Assert.That(bp.Condition, Is.EqualTo("input > 0"));
    }
}

[TestFixture]
public class DebugSessionWorkflowTests
{
    [Test]
    public void DebugConfiguration_CanBeFullyConfigured()
    {
        var config = new DebugConfiguration
        {
            Program = @"C:\Projects\bin\Debug\MyApp.exe",
            WorkingDirectory = @"C:\Projects",
            Arguments = new[] { "--debug", "--verbose" },
            StopOnEntry = true
        };
        config.Environment["DEBUG_MODE"] = "true";
        config.Environment["LOG_LEVEL"] = "verbose";

        Assert.That(config.Program, Is.EqualTo(@"C:\Projects\bin\Debug\MyApp.exe"));
        Assert.That(config.Arguments, Has.Length.EqualTo(2));
        Assert.That(config.Environment, Has.Count.EqualTo(2));
        Assert.That(config.StopOnEntry, Is.True);
    }

    [Test]
    public void StackFrameInfo_RepresentsCallStack()
    {
        var frames = new List<StackFrameInfo>
        {
            new() { Id = 0, Name = "Calculate", FilePath = @"C:\main.bas", Line = 42 },
            new() { Id = 1, Name = "ProcessData", FilePath = @"C:\helpers.bas", Line = 100 },
            new() { Id = 2, Name = "Main", FilePath = @"C:\main.bas", Line = 10 }
        };

        Assert.That(frames, Has.Count.EqualTo(3));
        Assert.That(frames[0].Name, Is.EqualTo("Calculate"));
        Assert.That(frames[2].Name, Is.EqualTo("Main"));
    }

    [Test]
    public void VariableInfo_RepresentsDebuggerVariables()
    {
        var variables = new List<VariableInfo>
        {
            new() { Name = "counter", Value = "42", Type = "Integer" },
            new() { Name = "message", Value = "Hello", Type = "String" },
            new() { Name = "myArray", Value = "Integer[5]", Type = "Integer[]", VariablesReference = 100 }
        };

        Assert.That(variables, Has.Count.EqualTo(3));
        Assert.That(variables[0].Value, Is.EqualTo("42"));
        Assert.That(variables[2].VariablesReference, Is.EqualTo(100)); // Has children
    }

    [Test]
    public void ScopeInfo_RepresentsVariableScopes()
    {
        var scopes = new List<ScopeInfo>
        {
            new() { Name = "Locals", VariablesReference = 1 },
            new() { Name = "Globals", VariablesReference = 2 },
            new() { Name = "Arguments", VariablesReference = 3 }
        };

        Assert.That(scopes, Has.Count.EqualTo(3));
        Assert.That(scopes[0].Name, Is.EqualTo("Locals"));
    }
}

[TestFixture]
public class CompletionWorkflowTests
{
    [Test]
    public void CompletionItem_SupportsMultipleKinds()
    {
        var items = new List<CompletionItem>
        {
            new() { Label = "Print", Kind = CompletionItemKind.Function },
            new() { Label = "counter", Kind = CompletionItemKind.Variable },
            new() { Label = "MyClass", Kind = CompletionItemKind.Class },
            new() { Label = "If", Kind = CompletionItemKind.Keyword },
            new() { Label = "For Each Template", Kind = CompletionItemKind.Snippet }
        };

        Assert.That(items.Count(i => i.Kind == CompletionItemKind.Function), Is.EqualTo(1));
        Assert.That(items.Count(i => i.Kind == CompletionItemKind.Variable), Is.EqualTo(1));
        Assert.That(items.Count(i => i.Kind == CompletionItemKind.Class), Is.EqualTo(1));
        Assert.That(items.Count(i => i.Kind == CompletionItemKind.Keyword), Is.EqualTo(1));
        Assert.That(items.Count(i => i.Kind == CompletionItemKind.Snippet), Is.EqualTo(1));
    }

    [Test]
    public void CompletionItem_CanHaveDocumentation()
    {
        var item = new CompletionItem
        {
            Label = "Console.WriteLine",
            Detail = "Sub WriteLine(value As String)",
            Documentation = "Writes the specified string value to the console, followed by the current line terminator.",
            InsertText = "Console.WriteLine($1)",
            Kind = CompletionItemKind.Method
        };

        Assert.That(item.Documentation, Is.Not.Null);
        Assert.That(item.InsertText, Does.Contain("$1")); // Snippet placeholder
    }

    [Test]
    public void SignatureHelp_SupportsOverloads()
    {
        var help = new SignatureHelp { ActiveSignature = 1, ActiveParameter = 0 };

        help.Signatures.Add(new SignatureInfo
        {
            Label = "Sub Print(text As String)"
        });
        help.Signatures.Add(new SignatureInfo
        {
            Label = "Sub Print(format As String, ParamArray args() As Object)"
        });
        help.Signatures[0].Parameters.Add(new ParameterInfo { Label = "text As String" });
        help.Signatures[1].Parameters.Add(new ParameterInfo { Label = "format As String" });
        help.Signatures[1].Parameters.Add(new ParameterInfo { Label = "args() As Object" });

        Assert.That(help.Signatures, Has.Count.EqualTo(2));
        Assert.That(help.ActiveSignature, Is.EqualTo(1));
        Assert.That(help.Signatures[1].Parameters, Has.Count.EqualTo(2));
    }
}

[TestFixture]
public class DocumentSymbolWorkflowTests
{
    [Test]
    public void DocumentSymbol_SupportsHierarchy()
    {
        var moduleSymbol = new DocumentSymbol
        {
            Name = "MainModule",
            Kind = SymbolKind.Module,
            Line = 1
        };

        var mainSub = new DocumentSymbol
        {
            Name = "Main",
            Kind = SymbolKind.Function,
            Line = 5
        };

        var helperSub = new DocumentSymbol
        {
            Name = "CalculateTotal",
            Kind = SymbolKind.Function,
            Line = 20
        };

        var counterVar = new DocumentSymbol
        {
            Name = "counter",
            Kind = SymbolKind.Variable,
            Line = 3
        };

        moduleSymbol.Children.Add(counterVar);
        moduleSymbol.Children.Add(mainSub);
        moduleSymbol.Children.Add(helperSub);

        Assert.That(moduleSymbol.Children, Has.Count.EqualTo(3));
        Assert.That(moduleSymbol.Children[0].Kind, Is.EqualTo(SymbolKind.Variable));
        Assert.That(moduleSymbol.Children[1].Kind, Is.EqualTo(SymbolKind.Function));
    }

    [Test]
    public void LocationInfo_SupportsGoToDefinition()
    {
        var definition = new LocationInfo
        {
            Uri = @"C:\Projects\helpers.bas",
            Line = 42,
            Column = 5,
            EndLine = 42,
            EndColumn = 20
        };

        Assert.That(definition.Uri, Is.EqualTo(@"C:\Projects\helpers.bas"));
        Assert.That(definition.Line, Is.EqualTo(42));
    }

    [Test]
    public void HoverInfo_SupportsRichContent()
    {
        var hover = new HoverInfo
        {
            Contents = "```basiclang\nDim counter As Integer\n```\n\nA counter variable for the loop.",
            StartLine = 10,
            StartColumn = 5,
            EndLine = 10,
            EndColumn = 12
        };

        Assert.That(hover.Contents, Does.Contain("counter"));
        Assert.That(hover.Contents, Does.Contain("Integer"));
    }
}
