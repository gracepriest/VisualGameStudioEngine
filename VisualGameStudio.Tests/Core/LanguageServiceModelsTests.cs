using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.Tests.Core;

[TestFixture]
public class DiagnosticsEventArgsTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
    {
        var args = new DiagnosticsEventArgs();

        Assert.That(args.Uri, Is.EqualTo(""));
        Assert.That(args.Diagnostics, Is.Empty);
    }

    [Test]
    public void Uri_CanBeSetAndRetrieved()
    {
        var args = new DiagnosticsEventArgs { Uri = "file:///path/to/file.bas" };

        Assert.That(args.Uri, Is.EqualTo("file:///path/to/file.bas"));
    }

    [Test]
    public void Diagnostics_CanBeSetAndRetrieved()
    {
        var diagnostics = new List<DiagnosticItem>
        {
            new() { Id = "BC001", Message = "Error 1" },
            new() { Id = "BC002", Message = "Error 2" }
        };
        var args = new DiagnosticsEventArgs { Diagnostics = diagnostics };

        Assert.That(args.Diagnostics, Has.Count.EqualTo(2));
    }
}

[TestFixture]
public class LanguageServiceCompletionItemTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
    {
        var item = new CompletionItem();

        Assert.That(item.Label, Is.EqualTo(""));
        Assert.That(item.Detail, Is.Null);
        Assert.That(item.Documentation, Is.Null);
        Assert.That((int)item.Kind, Is.EqualTo(0)); // Default enum value, not a named value
        Assert.That(item.InsertText, Is.Null);
        Assert.That(item.FilterText, Is.Null);
        Assert.That(item.SortText, Is.Null);
    }

    [Test]
    public void AllProperties_CanBeSet()
    {
        var item = new CompletionItem
        {
            Label = "WriteLine",
            Detail = "Sub WriteLine(message As String)",
            Documentation = "Writes a line to the console",
            Kind = CompletionItemKind.Method,
            InsertText = "WriteLine($0)",
            FilterText = "writeline",
            SortText = "0001_WriteLine"
        };

        Assert.That(item.Label, Is.EqualTo("WriteLine"));
        Assert.That(item.Detail, Is.EqualTo("Sub WriteLine(message As String)"));
        Assert.That(item.Documentation, Is.EqualTo("Writes a line to the console"));
        Assert.That(item.Kind, Is.EqualTo(CompletionItemKind.Method));
        Assert.That(item.InsertText, Is.EqualTo("WriteLine($0)"));
        Assert.That(item.FilterText, Is.EqualTo("writeline"));
        Assert.That(item.SortText, Is.EqualTo("0001_WriteLine"));
    }
}

[TestFixture]
public class LanguageServiceCompletionItemKindTests
{
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
    [TestCase(CompletionItemKind.TypeParameter, 25)]
    public void CompletionItemKind_HasCorrectValue(CompletionItemKind kind, int expectedValue)
    {
        Assert.That((int)kind, Is.EqualTo(expectedValue));
    }

    [Test]
    public void HasTwentyFiveValues()
    {
        var values = Enum.GetValues<CompletionItemKind>();
        Assert.That(values, Has.Length.EqualTo(25));
    }
}

[TestFixture]
public class HoverInfoTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
    {
        var hover = new HoverInfo();

        Assert.That(hover.Contents, Is.EqualTo(""));
        Assert.That(hover.StartLine, Is.EqualTo(0));
        Assert.That(hover.StartColumn, Is.EqualTo(0));
        Assert.That(hover.EndLine, Is.EqualTo(0));
        Assert.That(hover.EndColumn, Is.EqualTo(0));
    }

    [Test]
    public void AllProperties_CanBeSet()
    {
        var hover = new HoverInfo
        {
            Contents = "**Dim x As Integer**\n\nA local variable.",
            StartLine = 10,
            StartColumn = 5,
            EndLine = 10,
            EndColumn = 6
        };

        Assert.That(hover.Contents, Does.Contain("Dim x"));
        Assert.That(hover.StartLine, Is.EqualTo(10));
        Assert.That(hover.StartColumn, Is.EqualTo(5));
        Assert.That(hover.EndLine, Is.EqualTo(10));
        Assert.That(hover.EndColumn, Is.EqualTo(6));
    }
}

[TestFixture]
public class LocationInfoTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
    {
        var location = new LocationInfo();

        Assert.That(location.Uri, Is.EqualTo(""));
        Assert.That(location.Line, Is.EqualTo(0));
        Assert.That(location.Column, Is.EqualTo(0));
        Assert.That(location.EndLine, Is.EqualTo(0));
        Assert.That(location.EndColumn, Is.EqualTo(0));
    }

    [Test]
    public void AllProperties_CanBeSet()
    {
        var location = new LocationInfo
        {
            Uri = "file:///path/to/file.bas",
            Line = 10,
            Column = 5,
            EndLine = 15,
            EndColumn = 10
        };

        Assert.That(location.Uri, Is.EqualTo("file:///path/to/file.bas"));
        Assert.That(location.Line, Is.EqualTo(10));
        Assert.That(location.Column, Is.EqualTo(5));
        Assert.That(location.EndLine, Is.EqualTo(15));
        Assert.That(location.EndColumn, Is.EqualTo(10));
    }
}

[TestFixture]
public class DocumentSymbolTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
    {
        var symbol = new DocumentSymbol();

        Assert.That(symbol.Name, Is.EqualTo(""));
        Assert.That(symbol.Detail, Is.Null);
        Assert.That((int)symbol.Kind, Is.EqualTo(0)); // Default enum value, not a named value
        Assert.That(symbol.Line, Is.EqualTo(0));
        Assert.That(symbol.Column, Is.EqualTo(0));
        Assert.That(symbol.EndLine, Is.EqualTo(0));
        Assert.That(symbol.EndColumn, Is.EqualTo(0));
        Assert.That(symbol.Children, Is.Empty);
    }

    [Test]
    public void AllProperties_CanBeSet()
    {
        var symbol = new DocumentSymbol
        {
            Name = "MyClass",
            Detail = "Public Class",
            Kind = SymbolKind.Class,
            Line = 1,
            Column = 0,
            EndLine = 50,
            EndColumn = 9
        };

        Assert.That(symbol.Name, Is.EqualTo("MyClass"));
        Assert.That(symbol.Detail, Is.EqualTo("Public Class"));
        Assert.That(symbol.Kind, Is.EqualTo(SymbolKind.Class));
    }

    [Test]
    public void Children_CanBeNested()
    {
        var classSymbol = new DocumentSymbol
        {
            Name = "MyClass",
            Kind = SymbolKind.Class
        };

        classSymbol.Children.Add(new DocumentSymbol
        {
            Name = "MyMethod",
            Kind = SymbolKind.Method
        });

        classSymbol.Children.Add(new DocumentSymbol
        {
            Name = "MyProperty",
            Kind = SymbolKind.Property
        });

        Assert.That(classSymbol.Children, Has.Count.EqualTo(2));
        Assert.That(classSymbol.Children[0].Name, Is.EqualTo("MyMethod"));
        Assert.That(classSymbol.Children[1].Name, Is.EqualTo("MyProperty"));
    }
}

[TestFixture]
public class SymbolKindTests
{
    [TestCase(SymbolKind.File, 1)]
    [TestCase(SymbolKind.Module, 2)]
    [TestCase(SymbolKind.Namespace, 3)]
    [TestCase(SymbolKind.Class, 5)]
    [TestCase(SymbolKind.Method, 6)]
    [TestCase(SymbolKind.Property, 7)]
    [TestCase(SymbolKind.Field, 8)]
    [TestCase(SymbolKind.Constructor, 9)]
    [TestCase(SymbolKind.Enum, 10)]
    [TestCase(SymbolKind.Interface, 11)]
    [TestCase(SymbolKind.Function, 12)]
    [TestCase(SymbolKind.Variable, 13)]
    [TestCase(SymbolKind.Constant, 14)]
    [TestCase(SymbolKind.TypeParameter, 26)]
    public void SymbolKind_HasCorrectValue(SymbolKind kind, int expectedValue)
    {
        Assert.That((int)kind, Is.EqualTo(expectedValue));
    }

    [Test]
    public void HasTwentySixValues()
    {
        var values = Enum.GetValues<SymbolKind>();
        Assert.That(values, Has.Length.EqualTo(26));
    }
}

[TestFixture]
public class SignatureHelpTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
    {
        var help = new SignatureHelp();

        Assert.That(help.Signatures, Is.Empty);
        Assert.That(help.ActiveSignature, Is.EqualTo(0));
        Assert.That(help.ActiveParameter, Is.EqualTo(0));
    }

    [Test]
    public void AllProperties_CanBeSet()
    {
        var help = new SignatureHelp
        {
            ActiveSignature = 1,
            ActiveParameter = 2
        };

        help.Signatures.Add(new SignatureInfo { Label = "Sub Method(a As Integer)" });
        help.Signatures.Add(new SignatureInfo { Label = "Sub Method(a As Integer, b As String)" });

        Assert.That(help.Signatures, Has.Count.EqualTo(2));
        Assert.That(help.ActiveSignature, Is.EqualTo(1));
        Assert.That(help.ActiveParameter, Is.EqualTo(2));
    }
}

[TestFixture]
public class SignatureInfoTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
    {
        var info = new SignatureInfo();

        Assert.That(info.Label, Is.EqualTo(""));
        Assert.That(info.Documentation, Is.Null);
        Assert.That(info.Parameters, Is.Empty);
    }

    [Test]
    public void AllProperties_CanBeSet()
    {
        var info = new SignatureInfo
        {
            Label = "Sub WriteLine(message As String)",
            Documentation = "Writes a line to the console"
        };

        info.Parameters.Add(new ParameterInfo
        {
            Label = "message",
            Documentation = "The message to write"
        });

        Assert.That(info.Label, Is.EqualTo("Sub WriteLine(message As String)"));
        Assert.That(info.Documentation, Is.EqualTo("Writes a line to the console"));
        Assert.That(info.Parameters, Has.Count.EqualTo(1));
    }
}

[TestFixture]
public class ParameterInfoTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
    {
        var param = new ParameterInfo();

        Assert.That(param.Label, Is.EqualTo(""));
        Assert.That(param.Documentation, Is.Null);
    }

    [Test]
    public void AllProperties_CanBeSet()
    {
        var param = new ParameterInfo
        {
            Label = "message As String",
            Documentation = "The message to display"
        };

        Assert.That(param.Label, Is.EqualTo("message As String"));
        Assert.That(param.Documentation, Is.EqualTo("The message to display"));
    }
}
