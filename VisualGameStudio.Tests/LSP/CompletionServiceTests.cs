using NUnit.Framework;
using BasicLang.Compiler.LSP;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace VisualGameStudio.Tests.LSP;

[TestFixture]
public class CompletionServiceTests
{
    private CompletionService _completionService = null!;

    [SetUp]
    public void SetUp()
    {
        _completionService = new CompletionService();
    }

    [Test]
    public void GetCompletions_WithNullState_ReturnsKeywordsAndFunctions()
    {
        // Even with null state, the service returns keywords and built-in functions
        var result = _completionService.GetCompletions(null!, 0, 0);

        Assert.That(result, Is.Not.Empty);
        Assert.That(result.Any(c => c.Kind == CompletionItemKind.Keyword), Is.True);
        Assert.That(result.Any(c => c.Kind == CompletionItemKind.Function), Is.True);
    }

    [Test]
    public void GetCompletions_WithEmptyState_ReturnsKeywordsAndFunctions()
    {
        var state = CreateDocumentState("");

        var result = _completionService.GetCompletions(state, 0, 0);

        Assert.That(result, Is.Not.Empty);
        Assert.That(result.Any(c => c.Kind == CompletionItemKind.Keyword), Is.True);
        Assert.That(result.Any(c => c.Kind == CompletionItemKind.Function), Is.True);
    }

    [Test]
    public void GetCompletions_ContainsSubKeyword()
    {
        var state = CreateDocumentState("");

        var result = _completionService.GetCompletions(state, 0, 0);

        var subCompletion = result.FirstOrDefault(c => c.Label == "Sub");
        Assert.That(subCompletion, Is.Not.Null);
        Assert.That(subCompletion!.Kind, Is.EqualTo(CompletionItemKind.Keyword));
        Assert.That(subCompletion.InsertTextFormat, Is.EqualTo(InsertTextFormat.Snippet));
    }

    [Test]
    public void GetCompletions_ContainsFunctionKeyword()
    {
        var state = CreateDocumentState("");

        var result = _completionService.GetCompletions(state, 0, 0);

        var funcCompletion = result.FirstOrDefault(c => c.Label == "Function");
        Assert.That(funcCompletion, Is.Not.Null);
        Assert.That(funcCompletion!.Kind, Is.EqualTo(CompletionItemKind.Keyword));
    }

    [Test]
    public void GetCompletions_ContainsIfKeyword()
    {
        var state = CreateDocumentState("");

        var result = _completionService.GetCompletions(state, 0, 0);

        var ifCompletion = result.FirstOrDefault(c => c.Label == "If");
        Assert.That(ifCompletion, Is.Not.Null);
        Assert.That(ifCompletion!.InsertText, Does.Contain("If"));
        Assert.That(ifCompletion.InsertText, Does.Contain("End If"));
    }

    [Test]
    public void GetCompletions_ContainsForLoopKeyword()
    {
        var state = CreateDocumentState("");

        var result = _completionService.GetCompletions(state, 0, 0);

        var forCompletion = result.FirstOrDefault(c => c.Label == "For");
        Assert.That(forCompletion, Is.Not.Null);
        Assert.That(forCompletion!.InsertText, Does.Contain("For"));
        Assert.That(forCompletion.InsertText, Does.Contain("Next"));
    }

    [Test]
    public void GetCompletions_ContainsPrintLineFunction()
    {
        var state = CreateDocumentState("");

        var result = _completionService.GetCompletions(state, 0, 0);

        var printCompletion = result.FirstOrDefault(c => c.Label == "PrintLine");
        Assert.That(printCompletion, Is.Not.Null);
        Assert.That(printCompletion!.Kind, Is.EqualTo(CompletionItemKind.Function));
    }

    [Test]
    public void GetCompletions_ContainsReadLineFunction()
    {
        var state = CreateDocumentState("");

        var result = _completionService.GetCompletions(state, 0, 0);

        var readCompletion = result.FirstOrDefault(c => c.Label == "ReadLine");
        Assert.That(readCompletion, Is.Not.Null);
        Assert.That(readCompletion!.Kind, Is.EqualTo(CompletionItemKind.Function));
    }

    [Test]
    public void GetCompletions_ContainsStringFunctions()
    {
        var state = CreateDocumentState("");

        var result = _completionService.GetCompletions(state, 0, 0);

        var expectedFunctions = new[] { "Len", "Left", "Right", "Mid", "UCase", "LCase", "Trim" };
        foreach (var funcName in expectedFunctions)
        {
            var completion = result.FirstOrDefault(c => c.Label == funcName);
            Assert.That(completion, Is.Not.Null, $"Expected function {funcName} not found");
            Assert.That(completion!.Kind, Is.EqualTo(CompletionItemKind.Function));
        }
    }

    [Test]
    public void GetCompletions_ContainsMathFunctions()
    {
        var state = CreateDocumentState("");

        var result = _completionService.GetCompletions(state, 0, 0);

        var expectedFunctions = new[] { "Abs", "Sqrt", "Pow", "Sin", "Cos", "Tan", "Log", "Floor", "Ceiling", "Round" };
        foreach (var funcName in expectedFunctions)
        {
            var completion = result.FirstOrDefault(c => c.Label == funcName);
            Assert.That(completion, Is.Not.Null, $"Expected function {funcName} not found");
            Assert.That(completion!.Kind, Is.EqualTo(CompletionItemKind.Function));
        }
    }

    [Test]
    public void GetCompletions_ContainsTypeConversionFunctions()
    {
        var state = CreateDocumentState("");

        var result = _completionService.GetCompletions(state, 0, 0);

        var expectedFunctions = new[] { "CInt", "CLng", "CDbl", "CStr", "CBool" };
        foreach (var funcName in expectedFunctions)
        {
            var completion = result.FirstOrDefault(c => c.Label == funcName);
            Assert.That(completion, Is.Not.Null, $"Expected function {funcName} not found");
            Assert.That(completion!.Kind, Is.EqualTo(CompletionItemKind.Function));
        }
    }

    [Test]
    public void GetCompletions_ContainsBuiltInTypes()
    {
        var state = CreateDocumentState("");

        var result = _completionService.GetCompletions(state, 0, 0);

        var expectedTypes = new[] { "Integer", "Long", "Double", "String", "Boolean", "Object" };
        foreach (var typeName in expectedTypes)
        {
            // Find the type completion (which should have Class kind)
            var completion = result.FirstOrDefault(c => c.Label == typeName && c.Kind == CompletionItemKind.Class);
            Assert.That(completion, Is.Not.Null, $"Expected type {typeName} with Class kind not found");
        }
    }

    [Test]
    public void GetCompletions_ContainsCollectionFunctions()
    {
        var state = CreateDocumentState("");

        var result = _completionService.GetCompletions(state, 0, 0);

        var expectedFunctions = new[] { "CreateList", "ListAdd", "ListGet", "CreateDictionary", "DictSet", "DictGet" };
        foreach (var funcName in expectedFunctions)
        {
            var completion = result.FirstOrDefault(c => c.Label == funcName);
            Assert.That(completion, Is.Not.Null, $"Expected function {funcName} not found");
        }
    }

    [Test]
    public void GetCompletions_ContainsLinqStyleFunctions()
    {
        var state = CreateDocumentState("");

        var result = _completionService.GetCompletions(state, 0, 0);

        var expectedFunctions = new[] { "Where", "Select", "OrderBy", "FirstOrDefault", "ToList", "ToArray" };
        foreach (var funcName in expectedFunctions)
        {
            var completion = result.FirstOrDefault(c => c.Label == funcName);
            Assert.That(completion, Is.Not.Null, $"Expected function {funcName} not found");
        }
    }

    [Test]
    public void GetCompletions_ContainsSimpleKeywords()
    {
        var state = CreateDocumentState("");

        var result = _completionService.GetCompletions(state, 0, 0);

        var expectedKeywords = new[] { "And", "Or", "Not", "True", "False", "Nothing", "Public", "Private" };
        foreach (var keyword in expectedKeywords)
        {
            var completion = result.FirstOrDefault(c => c.Label == keyword);
            Assert.That(completion, Is.Not.Null, $"Expected keyword {keyword} not found");
            Assert.That(completion!.Kind, Is.EqualTo(CompletionItemKind.Keyword));
        }
    }

    [Test]
    public void GetCompletions_FunctionSnippetsHaveParameters()
    {
        var state = CreateDocumentState("");

        var result = _completionService.GetCompletions(state, 0, 0);

        var printCompletion = result.FirstOrDefault(c => c.Label == "PrintLine");
        Assert.That(printCompletion, Is.Not.Null);
        Assert.That(printCompletion!.InsertText, Does.Contain("${1:"));
        Assert.That(printCompletion.InsertTextFormat, Is.EqualTo(InsertTextFormat.Snippet));
    }

    [Test]
    public void GetCompletions_KeywordSnippetsHavePlaceholders()
    {
        var state = CreateDocumentState("");

        var result = _completionService.GetCompletions(state, 0, 0);

        var subCompletion = result.FirstOrDefault(c => c.Label == "Sub");
        Assert.That(subCompletion, Is.Not.Null);
        Assert.That(subCompletion!.InsertText, Does.Contain("${1:"));
        Assert.That(subCompletion.InsertText, Does.Contain("$0"));
    }

    [Test]
    public void GetCompletions_ContainsClassKeyword()
    {
        var state = CreateDocumentState("");

        var result = _completionService.GetCompletions(state, 0, 0);

        var classCompletion = result.FirstOrDefault(c => c.Label == "Class");
        Assert.That(classCompletion, Is.Not.Null);
        Assert.That(classCompletion!.InsertText, Does.Contain("Class"));
        Assert.That(classCompletion.InsertText, Does.Contain("End Class"));
    }

    [Test]
    public void GetCompletions_ContainsTryCatchSnippet()
    {
        var state = CreateDocumentState("");

        var result = _completionService.GetCompletions(state, 0, 0);

        var tryCompletion = result.FirstOrDefault(c => c.Label == "Try");
        Assert.That(tryCompletion, Is.Not.Null);
        Assert.That(tryCompletion!.InsertText, Does.Contain("Try"));
        Assert.That(tryCompletion.InsertText, Does.Contain("Catch"));
        Assert.That(tryCompletion.InsertText, Does.Contain("End Try"));
    }

    [Test]
    public void GetCompletions_ContainsWhileLoopKeyword()
    {
        var state = CreateDocumentState("");

        var result = _completionService.GetCompletions(state, 0, 0);

        var whileCompletion = result.FirstOrDefault(c => c.Label == "While");
        Assert.That(whileCompletion, Is.Not.Null);
        Assert.That(whileCompletion!.InsertText, Does.Contain("While"));
        Assert.That(whileCompletion.InsertText, Does.Contain("Wend"));
    }

    [Test]
    public void GetCompletions_ContainsSelectCaseKeyword()
    {
        var state = CreateDocumentState("");

        var result = _completionService.GetCompletions(state, 0, 0);

        var selectCompletion = result.FirstOrDefault(c => c.Label == "Select Case");
        Assert.That(selectCompletion, Is.Not.Null);
        Assert.That(selectCompletion!.InsertText, Does.Contain("Select Case"));
        Assert.That(selectCompletion.InsertText, Does.Contain("Case"));
        Assert.That(selectCompletion.InsertText, Does.Contain("End Select"));
    }

    [Test]
    public void GetCompletions_ContainsPropertyKeyword()
    {
        var state = CreateDocumentState("");

        var result = _completionService.GetCompletions(state, 0, 0);

        var propCompletion = result.FirstOrDefault(c => c.Label == "Property");
        Assert.That(propCompletion, Is.Not.Null);
        Assert.That(propCompletion!.InsertText, Does.Contain("Property"));
        Assert.That(propCompletion.InsertText, Does.Contain("Get"));
        Assert.That(propCompletion.InsertText, Does.Contain("Set"));
        Assert.That(propCompletion.InsertText, Does.Contain("End Property"));
    }

    [Test]
    public void GetCompletions_FunctionsHaveReturnTypeInDetail()
    {
        var state = CreateDocumentState("");

        var result = _completionService.GetCompletions(state, 0, 0);

        var lenCompletion = result.FirstOrDefault(c => c.Label == "Len");
        Assert.That(lenCompletion, Is.Not.Null);
        Assert.That(lenCompletion!.Detail, Does.Contain("Integer"));
    }

    [Test]
    public void GetCompletions_NoNullLabels()
    {
        var state = CreateDocumentState("");

        var result = _completionService.GetCompletions(state, 0, 0);

        foreach (var completion in result)
        {
            Assert.That(completion.Label, Is.Not.Null.And.Not.Empty);
        }
    }

    [Test]
    public void GetCompletions_NoNullInsertText()
    {
        var state = CreateDocumentState("");

        var result = _completionService.GetCompletions(state, 0, 0);

        foreach (var completion in result)
        {
            Assert.That(completion.InsertText, Is.Not.Null);
        }
    }

    [Test]
    public void GetCompletions_CountIsReasonable()
    {
        var state = CreateDocumentState("");

        var result = _completionService.GetCompletions(state, 0, 0);

        // Should have a reasonable number of completions (keywords + functions + types)
        Assert.That(result.Count, Is.GreaterThan(50));
        Assert.That(result.Count, Is.LessThan(500));
    }

    private DocumentState CreateDocumentState(string sourceCode)
    {
        // Create a dummy URI for testing
        var uri = DocumentUri.From("file:///test.bas");
        return new DocumentState(uri, sourceCode);
    }
}

[TestFixture]
public class DocumentStateTests
{
    [Test]
    public void Constructor_SetsPropertiesCorrectly()
    {
        var uri = DocumentUri.From("file:///test.bas");
        var content = "Dim x As Integer";

        var state = new DocumentState(uri, content);

        Assert.That(state.Uri, Is.EqualTo(uri));
        Assert.That(state.Content, Is.EqualTo(content));
        Assert.That(state.SourceCode, Is.EqualTo(content));
        Assert.That(state.Lines, Is.Not.Null);
        Assert.That(state.Diagnostics, Is.Not.Null);
    }

    [Test]
    public void Constructor_SetsLinesFromContent()
    {
        var uri = DocumentUri.From("file:///test.bas");
        var content = "Line 1\nLine 2\nLine 3";

        var state = new DocumentState(uri, content);

        Assert.That(state.Lines, Has.Length.EqualTo(3));
        Assert.That(state.Lines[0], Is.EqualTo("Line 1"));
        Assert.That(state.Lines[1], Is.EqualTo("Line 2"));
        Assert.That(state.Lines[2], Is.EqualTo("Line 3"));
    }

    [Test]
    public void Constructor_ComputesContentHash()
    {
        var uri = DocumentUri.From("file:///test.bas");
        var content = "Dim x As Integer";

        var state = new DocumentState(uri, content);

        Assert.That(state.ContentHash, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void Constructor_EmptyContent_CreatesEmptyLines()
    {
        var uri = DocumentUri.From("file:///test.bas");
        var content = "";

        var state = new DocumentState(uri, content);

        Assert.That(state.Lines, Has.Length.EqualTo(1));
        Assert.That(state.Lines[0], Is.EqualTo(""));
    }

    [Test]
    public void Constructor_InitializesEmptyDiagnostics()
    {
        var uri = DocumentUri.From("file:///test.bas");
        var content = "Dim x As Integer";

        var state = new DocumentState(uri, content);

        Assert.That(state.Diagnostics, Is.Empty);
    }

    [Test]
    public void SourceCode_IsAliasForContent()
    {
        var uri = DocumentUri.From("file:///test.bas");
        var content = "Function Test() As Integer\nReturn 42\nEnd Function";

        var state = new DocumentState(uri, content);

        Assert.That(state.SourceCode, Is.EqualTo(state.Content));
    }

    [Test]
    public void SameContent_ProducesSameHash()
    {
        var uri1 = DocumentUri.From("file:///test1.bas");
        var uri2 = DocumentUri.From("file:///test2.bas");
        var content = "Dim x As Integer";

        var state1 = new DocumentState(uri1, content);
        var state2 = new DocumentState(uri2, content);

        Assert.That(state1.ContentHash, Is.EqualTo(state2.ContentHash));
    }

    [Test]
    public void DifferentContent_ProducesDifferentHash()
    {
        var uri = DocumentUri.From("file:///test.bas");
        var content1 = "Dim x As Integer";
        var content2 = "Dim y As String";

        var state1 = new DocumentState(uri, content1);
        var state2 = new DocumentState(uri, content2);

        Assert.That(state1.ContentHash, Is.Not.EqualTo(state2.ContentHash));
    }
}

[TestFixture]
public class DocumentManagerTests
{
    private DocumentManager _documentManager = null!;

    [SetUp]
    public void SetUp()
    {
        _documentManager = new DocumentManager();
    }

    [Test]
    public void UpdateDocument_AddsNewDocument()
    {
        var uri = DocumentUri.From("file:///test.bas");
        var content = "Dim x As Integer";

        var state = _documentManager.UpdateDocument(uri, content);

        Assert.That(state, Is.Not.Null);
        Assert.That(state.Content, Is.EqualTo(content));
    }

    [Test]
    public void GetDocument_ReturnsExistingDocument()
    {
        var uri = DocumentUri.From("file:///test.bas");
        var content = "Dim x As Integer";
        _documentManager.UpdateDocument(uri, content);

        var state = _documentManager.GetDocument(uri);

        Assert.That(state, Is.Not.Null);
        Assert.That(state!.Content, Is.EqualTo(content));
    }

    [Test]
    public void GetDocument_NonExistent_ReturnsNull()
    {
        var uri = DocumentUri.From("file:///nonexistent.bas");

        var state = _documentManager.GetDocument(uri);

        Assert.That(state, Is.Null);
    }

    [Test]
    public void UpdateDocument_SameContent_ReturnsCachedState()
    {
        var uri = DocumentUri.From("file:///test.bas");
        var content = "Dim x As Integer";
        var state1 = _documentManager.UpdateDocument(uri, content);

        var state2 = _documentManager.UpdateDocument(uri, content);

        Assert.That(state2.ContentHash, Is.EqualTo(state1.ContentHash));
    }

    [Test]
    public void Constructor_InitializesTypeRegistry()
    {
        Assert.That(_documentManager.TypeRegistry, Is.Not.Null);
    }
}
