using NUnit.Framework;
using BasicLang.Compiler.LSP;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace VisualGameStudio.Tests.LSP;

/// <summary>
/// Tests for completion trigger-context detection (audit findings [1] and [36])
/// and the VS Code filtering contract (finding [17]): the server returns the
/// FULL candidate set for the detected context and only RANKS by fuzzy score —
/// it never drops items; the client filters in place.
/// </summary>
[TestFixture]
public class CompletionContextTests
{
    private CompletionService _completionService = null!;

    [SetUp]
    public void SetUp()
    {
        _completionService = new CompletionService();
    }

    private static DocumentState CreateParsedState(string sourceCode, string fileName = "test.bas")
    {
        var uri = DocumentUri.From($"file:///{fileName}");
        var state = new DocumentState(uri, sourceCode);
        state.Parse();
        return state;
    }

    // ------------------------------------------------------------------
    // [1] Member-access dot check must run FIRST (before keyword contexts)
    // ------------------------------------------------------------------

    [Test]
    public void MemberAccess_OnLineContainingAs_ReturnsMembersOfReceiver()
    {
        // "Dim s As String = other." previously hijacked into AsType context
        // with FilterPrefix "String = other." -> empty list.
        var source = "Sub Main()\n" +
                     "    Dim other As String\n" +
                     "    Dim s As String = other.\n" +
                     "End Sub";
        var state = CreateParsedState(source);

        var line2 = "    Dim s As String = other.";
        var result = _completionService.GetCompletions(state, 2, line2.Length);

        Assert.That(result, Is.Not.Empty, "member completion after '.' on a Dim..As line must not be empty");
        Assert.That(result.Any(c => c.Label == "Trim"), Is.True, "expected String member 'Trim'");
        // Member list must be scoped: no keywords leaking in
        Assert.That(result.Any(c => c.Label == "Sub"), Is.False, "keywords must not leak into member completion");
    }

    [Test]
    public void MemberAccess_AfterNewExpression_ReturnsMemberContext()
    {
        // "Dim p = New Person()." must be member access on Person, not New context.
        var source = "Class Person\n" +
                     "    Public Name As String\n" +
                     "    Public Sub Greet()\n" +
                     "    End Sub\n" +
                     "End Class\n" +
                     "Sub Main()\n" +
                     "    Dim p = New Person().\n" +
                     "End Sub";
        var state = CreateParsedState(source);

        var line6 = "    Dim p = New Person().";
        var result = _completionService.GetCompletions(state, 6, line6.Length);

        Assert.That(result.Any(c => c.Label == "Greet" || c.Label == "Name"), Is.True,
            "expected members of Person after 'New Person().'");
    }

    [Test]
    public void GeneralContext_AfterParameterList_IsNotHijackedByAsKeyword()
    {
        // Cursor at end of "Sub Foo(x As Integer)" — the "As" inside the parameter
        // list must not force AsType context.
        var source = "Sub Foo(x As Integer)\nEnd Sub";
        var state = CreateParsedState(source);

        var line0 = "Sub Foo(x As Integer)";
        var result = _completionService.GetCompletions(state, 0, line0.Length);

        // General context: keywords and built-in functions present
        Assert.That(result.Any(c => c.Kind == CompletionItemKind.Keyword), Is.True);
        Assert.That(result.Any(c => c.Label == "PrintLine"), Is.True);
    }

    // ------------------------------------------------------------------
    // [36] Keyword contexts activate at the trigger point (trailing space)
    // ------------------------------------------------------------------

    [Test]
    public void AsContext_ImmediatelyAfterAsSpace_ReturnsTypesOnly()
    {
        var source = "Sub Main()\n    Dim x As \nEnd Sub";
        var state = CreateParsedState(source);

        // Cursor right after "Dim x As " (trailing space)
        var result = _completionService.GetCompletions(state, 1, "    Dim x As ".Length);

        Assert.That(result.Any(c => c.Label == "Integer"), Is.True, "expected type 'Integer' in As context");
        Assert.That(result.Any(c => c.Label == "String"), Is.True, "expected type 'String' in As context");
        // Types-only: no built-in function dump
        Assert.That(result.Any(c => c.Label == "PrintLine"), Is.False, "functions must not appear in As context");
        Assert.That(result.Any(c => c.Label == "Asc"), Is.False, "functions must not appear in As context");
    }

    [Test]
    public void NewContext_ImmediatelyAfterNewSpace_ReturnsInstantiableTypes()
    {
        var source = "Sub Main()\n    Dim x = New \nEnd Sub";
        var state = CreateParsedState(source);

        var result = _completionService.GetCompletions(state, 1, "    Dim x = New ".Length);

        Assert.That(result.Any(c => c.Label == "List"), Is.True, "expected 'List' in New context");
        Assert.That(result.Any(c => c.Label == "Random"), Is.True, "expected 'Random' in New context");
        Assert.That(result.Any(c => c.Label == "PrintLine"), Is.False, "functions must not appear in New context");
    }

    [Test]
    public void ImportContext_ImmediatelyAfterImportSpace_ReturnsModules()
    {
        _completionService.SetAvailableModules(new[] { "MathUtils" });
        var source = "Import \n";
        var state = CreateParsedState(source);

        var result = _completionService.GetCompletions(state, 0, "Import ".Length);

        Assert.That(result.Any(c => c.Label == "MathUtils"), Is.True, "expected available module in Import context");
        Assert.That(result.Any(c => c.Label == "PrintLine"), Is.False, "functions must not appear in Import context");
    }

    [Test]
    public void AsNewContext_ReturnsInstantiableTypes()
    {
        // VB idiom: Dim x As New T — the New context wins at the cursor
        var source = "Sub Main()\n    Dim x As New \nEnd Sub";
        var state = CreateParsedState(source);

        var result = _completionService.GetCompletions(state, 1, "    Dim x As New ".Length);

        Assert.That(result.Any(c => c.Label == "List"), Is.True, "expected 'List' in As New context");
        Assert.That(result.Any(c => c.Label == "PrintLine"), Is.False);
    }

    // ------------------------------------------------------------------
    // [17] Contract: fuzzy filter ranks but never drops; FilterText set
    // ------------------------------------------------------------------

    [Test]
    public void TypedPrefix_DoesNotDropNonMatchingItems()
    {
        var source = "Sub Main()\n    Con\nEnd Sub";
        var state = CreateParsedState(source);

        var result = _completionService.GetCompletions(state, 1, "    Con".Length);

        // Matching item present
        Assert.That(result.Any(c => c.Label == "Const"), Is.True);
        // Non-matching items must NOT be dropped (client filters)
        Assert.That(result.Any(c => c.Label == "PrintLine"), Is.True,
            "items with fuzzy score 0 must be kept — the client filters in place");
    }

    [Test]
    public void TypedPrefix_RanksMatchesBeforeNonMatches()
    {
        var source = "Sub Main()\n    Con\nEnd Sub";
        var state = CreateParsedState(source);

        var result = _completionService.GetCompletions(state, 1, "    Con".Length);

        var constItem = result.FirstOrDefault(c => c.Label == "Const");
        var printItem = result.FirstOrDefault(c => c.Label == "PrintLine");
        Assert.That(constItem, Is.Not.Null);
        Assert.That(printItem, Is.Not.Null);
        Assert.That(constItem!.SortText, Is.Not.Null);
        Assert.That(printItem!.SortText, Is.Not.Null);
        Assert.That(string.CompareOrdinal(constItem.SortText, printItem.SortText), Is.LessThan(0),
            "prefix match 'Const' must sort before non-match 'PrintLine'");
    }

    [Test]
    public void AsContextWithPartialPrefix_KeepsFullTypeList()
    {
        var source = "Sub Main()\n    Dim x As Str\nEnd Sub";
        var state = CreateParsedState(source);

        var result = _completionService.GetCompletions(state, 1, "    Dim x As Str".Length);

        var stringItem = result.FirstOrDefault(c => c.Label == "String");
        var integerItem = result.FirstOrDefault(c => c.Label == "Integer");
        Assert.That(stringItem, Is.Not.Null, "matching type must be present");
        Assert.That(integerItem, Is.Not.Null, "non-matching types must be kept (client filters)");
        Assert.That(string.CompareOrdinal(stringItem!.SortText, integerItem!.SortText), Is.LessThan(0),
            "'String' must rank before 'Integer' for prefix 'Str'");
    }

    [Test]
    public void AllItems_HaveFilterText()
    {
        var source = "Sub Main()\nEnd Sub";
        var state = CreateParsedState(source);

        var result = _completionService.GetCompletions(state, 1, 0);

        Assert.That(result, Is.Not.Empty);
        foreach (var item in result)
        {
            Assert.That(item.FilterText, Is.Not.Null.And.Not.Empty,
                $"item '{item.Label}' must carry FilterText");
        }
    }

    [Test]
    public void MultiWordLabels_HaveCleanIdentifierFilterText()
    {
        var state = CreateParsedState("");

        var result = _completionService.GetCompletions(state, 0, 0);

        var doWhile = result.FirstOrDefault(c => c.Label == "Do While");
        Assert.That(doWhile, Is.Not.Null);
        Assert.That(doWhile!.FilterText, Is.EqualTo("Do"),
            "multi-word label filters on its leading word");

        var ifElse = result.FirstOrDefault(c => c.Label == "If...Else");
        Assert.That(ifElse, Is.Not.Null);
        Assert.That(ifElse!.FilterText, Is.EqualTo("If"),
            "punctuated label filters on its leading identifier token");
    }

    [Test]
    public void MemberAccess_WithPartialMemberPrefix_KeepsAllMembers()
    {
        var source = "Sub Main()\n" +
                     "    Dim s As String\n" +
                     "    s.Tr\n" +
                     "End Sub";
        var state = CreateParsedState(source);

        var result = _completionService.GetCompletions(state, 2, "    s.Tr".Length);

        var trim = result.FirstOrDefault(c => c.Label == "Trim");
        Assert.That(trim, Is.Not.Null, "'Trim' must be present");
        // Non-matching members are kept (client filters)
        var length = result.FirstOrDefault(c => c.Label == "Length");
        Assert.That(length, Is.Not.Null, "'Length' must be kept even though it doesn't match 'Tr'");
        Assert.That(string.CompareOrdinal(trim!.SortText, length!.SortText), Is.LessThan(0),
            "'Trim' must rank before 'Length' for prefix 'Tr'");
    }
}
