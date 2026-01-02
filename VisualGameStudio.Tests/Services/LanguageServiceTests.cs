using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

[TestFixture]
public class LanguageServiceTests
{
    private Mock<IOutputService> _mockOutputService = null!;
    private LanguageService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _mockOutputService = new Mock<IOutputService>();
        _service = new LanguageService(_mockOutputService.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _service.Dispose();
    }

    [Test]
    public void InitialState_IsNotConnected()
    {
        Assert.That(_service.IsConnected, Is.False);
    }

    [Test]
    public async Task StopAsync_WhenNotConnected_DoesNotThrow()
    {
        await _service.StopAsync();

        Assert.That(_service.IsConnected, Is.False);
    }

    [Test]
    public async Task OpenDocumentAsync_WhenNotConnected_DoesNotThrow()
    {
        await _service.OpenDocumentAsync("file:///test.bas", "Module Test\nEnd Module");

        Assert.Pass();
    }

    [Test]
    public async Task ChangeDocumentAsync_WhenNotConnected_DoesNotThrow()
    {
        await _service.ChangeDocumentAsync("file:///test.bas", "Module Test\nEnd Module", 2);

        Assert.Pass();
    }

    [Test]
    public async Task CloseDocumentAsync_WhenNotConnected_DoesNotThrow()
    {
        await _service.CloseDocumentAsync("file:///test.bas");

        Assert.Pass();
    }

    [Test]
    public async Task GetCompletionsAsync_WhenNotConnected_ReturnsEmpty()
    {
        var result = await _service.GetCompletionsAsync("file:///test.bas", 1, 1);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetHoverAsync_WhenNotConnected_ReturnsNull()
    {
        var result = await _service.GetHoverAsync("file:///test.bas", 1, 1);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetDefinitionAsync_WhenNotConnected_ReturnsNull()
    {
        var result = await _service.GetDefinitionAsync("file:///test.bas", 1, 1);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task FindReferencesAsync_WhenNotConnected_ReturnsEmpty()
    {
        var result = await _service.FindReferencesAsync("file:///test.bas", 1, 1);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetDocumentSymbolsAsync_WhenNotConnected_ReturnsEmpty()
    {
        var result = await _service.GetDocumentSymbolsAsync("file:///test.bas");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetSignatureHelpAsync_WhenNotConnected_ReturnsNull()
    {
        var result = await _service.GetSignatureHelpAsync("file:///test.bas", 1, 10);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void ConnectionChanged_Event_CanBeSubscribed()
    {
        var connectionChanged = false;
        _service.ConnectionChanged += (s, e) => connectionChanged = true;

        Assert.Pass();
    }

    [Test]
    public void DiagnosticsReceived_Event_CanBeSubscribed()
    {
        var diagnosticsReceived = false;
        _service.DiagnosticsReceived += (s, e) => diagnosticsReceived = true;

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
public class CompletionItemTests
{
    [Test]
    public void DefaultItem_HasDefaultValues()
    {
        var item = new CompletionItem();

        Assert.That(item.Label, Is.EqualTo(""));
        Assert.That(item.Detail, Is.Null);
        Assert.That(item.Documentation, Is.Null);
        Assert.That((int)item.Kind, Is.EqualTo(0));
        Assert.That(item.InsertText, Is.Null);
        Assert.That(item.FilterText, Is.Null);
        Assert.That(item.SortText, Is.Null);
    }

    [Test]
    public void Item_CanSetAllProperties()
    {
        var item = new CompletionItem
        {
            Label = "MyFunction",
            Detail = "Sub MyFunction()",
            Documentation = "This is a custom function",
            Kind = CompletionItemKind.Function,
            InsertText = "MyFunction()",
            FilterText = "myfunction",
            SortText = "0MyFunction"
        };

        Assert.That(item.Label, Is.EqualTo("MyFunction"));
        Assert.That(item.Detail, Is.EqualTo("Sub MyFunction()"));
        Assert.That(item.Documentation, Is.EqualTo("This is a custom function"));
        Assert.That(item.Kind, Is.EqualTo(CompletionItemKind.Function));
        Assert.That(item.InsertText, Is.EqualTo("MyFunction()"));
        Assert.That(item.FilterText, Is.EqualTo("myfunction"));
        Assert.That(item.SortText, Is.EqualTo("0MyFunction"));
    }

    [TestCase(CompletionItemKind.Text, 1)]
    [TestCase(CompletionItemKind.Method, 2)]
    [TestCase(CompletionItemKind.Function, 3)]
    [TestCase(CompletionItemKind.Constructor, 4)]
    [TestCase(CompletionItemKind.Field, 5)]
    [TestCase(CompletionItemKind.Variable, 6)]
    [TestCase(CompletionItemKind.Class, 7)]
    [TestCase(CompletionItemKind.Interface, 8)]
    [TestCase(CompletionItemKind.Module, 9)]
    [TestCase(CompletionItemKind.Property, 10)]
    [TestCase(CompletionItemKind.Keyword, 14)]
    [TestCase(CompletionItemKind.Snippet, 15)]
    public void CompletionItemKind_HasCorrectValue(CompletionItemKind kind, int expectedValue)
    {
        Assert.That((int)kind, Is.EqualTo(expectedValue));
    }
}

[TestFixture]
public class HoverInfoTests
{
    [Test]
    public void DefaultInfo_HasDefaultValues()
    {
        var info = new HoverInfo();

        Assert.That(info.Contents, Is.EqualTo(""));
        Assert.That(info.StartLine, Is.EqualTo(0));
        Assert.That(info.StartColumn, Is.EqualTo(0));
        Assert.That(info.EndLine, Is.EqualTo(0));
        Assert.That(info.EndColumn, Is.EqualTo(0));
    }

    [Test]
    public void Info_CanSetAllProperties()
    {
        var info = new HoverInfo
        {
            Contents = "Dim x As Integer",
            StartLine = 10,
            StartColumn = 5,
            EndLine = 10,
            EndColumn = 20
        };

        Assert.That(info.Contents, Is.EqualTo("Dim x As Integer"));
        Assert.That(info.StartLine, Is.EqualTo(10));
        Assert.That(info.StartColumn, Is.EqualTo(5));
        Assert.That(info.EndLine, Is.EqualTo(10));
        Assert.That(info.EndColumn, Is.EqualTo(20));
    }
}

[TestFixture]
public class LocationInfoTests
{
    [Test]
    public void DefaultInfo_HasDefaultValues()
    {
        var info = new LocationInfo();

        Assert.That(info.Uri, Is.EqualTo(""));
        Assert.That(info.Line, Is.EqualTo(0));
        Assert.That(info.Column, Is.EqualTo(0));
        Assert.That(info.EndLine, Is.EqualTo(0));
        Assert.That(info.EndColumn, Is.EqualTo(0));
    }

    [Test]
    public void Info_CanSetAllProperties()
    {
        var info = new LocationInfo
        {
            Uri = @"C:\Projects\test.bas",
            Line = 42,
            Column = 10,
            EndLine = 42,
            EndColumn = 25
        };

        Assert.That(info.Uri, Is.EqualTo(@"C:\Projects\test.bas"));
        Assert.That(info.Line, Is.EqualTo(42));
        Assert.That(info.Column, Is.EqualTo(10));
        Assert.That(info.EndLine, Is.EqualTo(42));
        Assert.That(info.EndColumn, Is.EqualTo(25));
    }
}

[TestFixture]
public class DocumentSymbolTests
{
    [Test]
    public void DefaultSymbol_HasDefaultValues()
    {
        var symbol = new DocumentSymbol();

        Assert.That(symbol.Name, Is.EqualTo(""));
        Assert.That(symbol.Detail, Is.Null);
        Assert.That((int)symbol.Kind, Is.EqualTo(0));
        Assert.That(symbol.Line, Is.EqualTo(0));
        Assert.That(symbol.Column, Is.EqualTo(0));
        Assert.That(symbol.EndLine, Is.EqualTo(0));
        Assert.That(symbol.EndColumn, Is.EqualTo(0));
        Assert.That(symbol.Children, Is.Empty);
    }

    [Test]
    public void Symbol_CanSetAllProperties()
    {
        var symbol = new DocumentSymbol
        {
            Name = "MainModule",
            Detail = "Public Module",
            Kind = SymbolKind.Module,
            Line = 1,
            Column = 1,
            EndLine = 100,
            EndColumn = 1
        };

        Assert.That(symbol.Name, Is.EqualTo("MainModule"));
        Assert.That(symbol.Detail, Is.EqualTo("Public Module"));
        Assert.That(symbol.Kind, Is.EqualTo(SymbolKind.Module));
        Assert.That(symbol.Line, Is.EqualTo(1));
        Assert.That(symbol.Column, Is.EqualTo(1));
        Assert.That(symbol.EndLine, Is.EqualTo(100));
        Assert.That(symbol.EndColumn, Is.EqualTo(1));
    }

    [Test]
    public void Symbol_CanHaveChildren()
    {
        var parent = new DocumentSymbol { Name = "MainModule", Kind = SymbolKind.Module };
        parent.Children.Add(new DocumentSymbol { Name = "Main", Kind = SymbolKind.Function });
        parent.Children.Add(new DocumentSymbol { Name = "counter", Kind = SymbolKind.Variable });

        Assert.That(parent.Children, Has.Count.EqualTo(2));
        Assert.That(parent.Children[0].Name, Is.EqualTo("Main"));
        Assert.That(parent.Children[1].Name, Is.EqualTo("counter"));
    }

    [TestCase(SymbolKind.File, 1)]
    [TestCase(SymbolKind.Module, 2)]
    [TestCase(SymbolKind.Namespace, 3)]
    [TestCase(SymbolKind.Class, 5)]
    [TestCase(SymbolKind.Method, 6)]
    [TestCase(SymbolKind.Property, 7)]
    [TestCase(SymbolKind.Field, 8)]
    [TestCase(SymbolKind.Constructor, 9)]
    [TestCase(SymbolKind.Function, 12)]
    [TestCase(SymbolKind.Variable, 13)]
    [TestCase(SymbolKind.Constant, 14)]
    public void SymbolKind_HasCorrectValue(SymbolKind kind, int expectedValue)
    {
        Assert.That((int)kind, Is.EqualTo(expectedValue));
    }
}

[TestFixture]
public class SignatureHelpTests
{
    [Test]
    public void DefaultHelp_HasDefaultValues()
    {
        var help = new SignatureHelp();

        Assert.That(help.Signatures, Is.Empty);
        Assert.That(help.ActiveSignature, Is.EqualTo(0));
        Assert.That(help.ActiveParameter, Is.EqualTo(0));
    }

    [Test]
    public void Help_CanSetAllProperties()
    {
        var help = new SignatureHelp
        {
            ActiveSignature = 1,
            ActiveParameter = 2
        };
        help.Signatures.Add(new SignatureInfo { Label = "Sub Print(text As String)" });

        Assert.That(help.Signatures, Has.Count.EqualTo(1));
        Assert.That(help.ActiveSignature, Is.EqualTo(1));
        Assert.That(help.ActiveParameter, Is.EqualTo(2));
    }
}

[TestFixture]
public class SignatureInfoTests
{
    [Test]
    public void DefaultInfo_HasDefaultValues()
    {
        var info = new SignatureInfo();

        Assert.That(info.Label, Is.EqualTo(""));
        Assert.That(info.Documentation, Is.Null);
        Assert.That(info.Parameters, Is.Empty);
    }

    [Test]
    public void Info_CanSetAllProperties()
    {
        var info = new SignatureInfo
        {
            Label = "Sub Print(text As String)",
            Documentation = "Prints text to the console"
        };
        info.Parameters.Add(new ParameterInfo { Label = "text", Documentation = "The text to print" });

        Assert.That(info.Label, Is.EqualTo("Sub Print(text As String)"));
        Assert.That(info.Documentation, Is.EqualTo("Prints text to the console"));
        Assert.That(info.Parameters, Has.Count.EqualTo(1));
    }
}

[TestFixture]
public class ParameterInfoTests
{
    [Test]
    public void DefaultInfo_HasDefaultValues()
    {
        var info = new ParameterInfo();

        Assert.That(info.Label, Is.EqualTo(""));
        Assert.That(info.Documentation, Is.Null);
    }

    [Test]
    public void Info_CanSetAllProperties()
    {
        var info = new ParameterInfo
        {
            Label = "text As String",
            Documentation = "The text to print"
        };

        Assert.That(info.Label, Is.EqualTo("text As String"));
        Assert.That(info.Documentation, Is.EqualTo("The text to print"));
    }
}

[TestFixture]
public class DiagnosticsEventArgsTests
{
    [Test]
    public void DefaultArgs_HasDefaultValues()
    {
        var args = new DiagnosticsEventArgs();

        Assert.That(args.Uri, Is.EqualTo(""));
        Assert.That(args.Diagnostics, Is.Empty);
    }

    [Test]
    public void Args_CanSetAllProperties()
    {
        var args = new DiagnosticsEventArgs
        {
            Uri = @"C:\Projects\test.bas",
            Diagnostics = new List<DiagnosticItem>
            {
                new() { Message = "Undefined variable 'x'", Line = 10, Column = 5 }
            }
        };

        Assert.That(args.Uri, Is.EqualTo(@"C:\Projects\test.bas"));
        Assert.That(args.Diagnostics, Has.Count.EqualTo(1));
    }

    [Test]
    public void InheritsFromEventArgs()
    {
        var args = new DiagnosticsEventArgs();

        Assert.That(args, Is.InstanceOf<EventArgs>());
    }
}
