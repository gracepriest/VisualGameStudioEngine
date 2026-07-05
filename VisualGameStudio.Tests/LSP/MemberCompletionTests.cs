using NUnit.Framework;
using BasicLang.Compiler.LSP;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace VisualGameStudio.Tests.LSP;

/// <summary>
/// Tests for member-access completion coverage: Me/MyBase (finding [6]),
/// .NET member coverage via preloaded core types (finding [15]),
/// method-chain receivers (finding [5]) and For Each element types
/// (finding [19]).
/// </summary>
[TestFixture]
public class MemberCompletionTests
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
    // [6] Me. / MyBase. member completion
    // ------------------------------------------------------------------

    private const string InheritanceSource =
        "Class Animal\n" +                    // line 0
        "    Public Name As String\n" +       // line 1
        "    Public Sub Speak()\n" +          // line 2
        "    End Sub\n" +                     // line 3
        "End Class\n" +                       // line 4
        "Class Dog\n" +                       // line 5
        "    Inherits Animal\n" +             // line 6
        "    Public Breed As String\n" +      // line 7
        "    Public Sub Fetch()\n" +          // line 8
        "    End Sub\n" +                     // line 9
        "    Public Sub Test()\n" +           // line 10
        "        Me.\n" +                     // line 11
        "    End Sub\n" +                     // line 12
        "End Class";                          // line 13

    [Test]
    public void MeDot_InsideClass_ListsOwnMembers()
    {
        var state = CreateParsedState(InheritanceSource);

        var result = _completionService.GetCompletions(state, 11, "        Me.".Length);

        Assert.That(result, Is.Not.Empty, "'Me.' must not return an empty list");
        Assert.That(result.Any(c => c.Label == "Breed"), Is.True, "expected own field 'Breed'");
        Assert.That(result.Any(c => c.Label == "Fetch"), Is.True, "expected own method 'Fetch'");
    }

    [Test]
    public void MeDot_InsideClass_IncludesInheritedMembers()
    {
        var state = CreateParsedState(InheritanceSource);

        var result = _completionService.GetCompletions(state, 11, "        Me.".Length);

        Assert.That(result.Any(c => c.Label == "Name"), Is.True, "expected inherited field 'Name'");
        Assert.That(result.Any(c => c.Label == "Speak"), Is.True, "expected inherited method 'Speak'");
    }

    [Test]
    public void MyBaseDot_InsideDerivedClass_ListsBaseMembersOnly()
    {
        var source = InheritanceSource.Replace("        Me.\n", "        MyBase.\n");
        var state = CreateParsedState(source);

        var result = _completionService.GetCompletions(state, 11, "        MyBase.".Length);

        Assert.That(result.Any(c => c.Label == "Speak"), Is.True, "expected base method 'Speak'");
        Assert.That(result.Any(c => c.Label == "Name"), Is.True, "expected base field 'Name'");
        Assert.That(result.Any(c => c.Label == "Breed"), Is.False, "own members must not appear after 'MyBase.'");
        Assert.That(result.Any(c => c.Label == "Fetch"), Is.False, "own members must not appear after 'MyBase.'");
    }

    [Test]
    public void MeDot_MembersAreScopedToClass()
    {
        var state = CreateParsedState(InheritanceSource);

        var result = _completionService.GetCompletions(state, 11, "        Me.".Length);

        Assert.That(result.Any(c => c.Label == "PrintLine"), Is.False, "built-ins must not leak into 'Me.'");
        Assert.That(result.Any(c => c.Kind == CompletionItemKind.Keyword), Is.False, "keywords must not leak into 'Me.'");
    }

    // ------------------------------------------------------------------
    // [15] .NET member coverage: preloaded core types, static/instance split
    // ------------------------------------------------------------------

    private static DocumentState CreateManagedState(string sourceCode, string fileName = "test.bas")
    {
        var manager = new DocumentManager();
        var uri = DocumentUri.From($"file:///{fileName}");
        return manager.UpdateDocument(uri, sourceCode);
    }

    [Test]
    public void ConsoleDot_ReturnsFullStaticMemberSet()
    {
        var source = "Sub Main()\n    Console.\nEnd Sub";
        var state = CreateManagedState(source);

        var result = _completionService.GetCompletions(state, 1, "    Console.".Length);

        Assert.That(result.Any(c => c.Label == "WriteLine"), Is.True, "expected Console.WriteLine");
        Assert.That(result.Any(c => c.Label == "BackgroundColor"), Is.True,
            "expected the full reflected member set (BackgroundColor), not the 5-entry fallback table");
        Assert.That(result.Any(c => c.Label == "ForegroundColor"), Is.True);
    }

    [Test]
    public void StringVariableDot_ReturnsFullInstanceMemberSet()
    {
        var source = "Sub Main()\n    Dim s As String\n    s.\nEnd Sub";
        var state = CreateManagedState(source);

        var result = _completionService.GetCompletions(state, 2, "    s.".Length);

        Assert.That(result.Any(c => c.Label == "PadLeft"), Is.True, "expected instance method PadLeft");
        Assert.That(result.Any(c => c.Label == "ToCharArray"), Is.True, "expected instance method ToCharArray");
        Assert.That(result.Any(c => c.Label == "Length"), Is.True, "expected instance property Length");
    }

    [Test]
    public void StringVariableDot_ExcludesStaticMembers()
    {
        var source = "Sub Main()\n    Dim s As String\n    s.\nEnd Sub";
        var state = CreateManagedState(source);

        var result = _completionService.GetCompletions(state, 2, "    s.".Length);

        Assert.That(result.Any(c => c.Label == "IsNullOrEmpty"), Is.False,
            "static members must not be offered on an instance receiver");
        Assert.That(result.Any(c => c.Label == "Concat"), Is.False,
            "static members must not be offered on an instance receiver");
    }

    [Test]
    public void StringTypeDot_ReturnsStaticsAndExcludesInstanceMembers()
    {
        var source = "Sub Main()\n    String.\nEnd Sub";
        var state = CreateManagedState(source);

        var result = _completionService.GetCompletions(state, 1, "    String.".Length);

        Assert.That(result.Any(c => c.Label == "IsNullOrEmpty"), Is.True, "expected static String.IsNullOrEmpty");
        Assert.That(result.Any(c => c.Label == "PadLeft"), Is.False,
            "instance members must not be offered on a type receiver");
    }

    [Test]
    public void GeneralCompletions_IncludeConsoleTypeName()
    {
        var source = "Sub Main()\n    Con\nEnd Sub";
        var state = CreateManagedState(source);

        var result = _completionService.GetCompletions(state, 1, "    Con".Length);

        Assert.That(result.Any(c => c.Label == "Console"), Is.True,
            "the type name 'Console' must be completable so the receiver can even be typed");
    }

    [Test]
    public void VariableNamedLikeType_StillGetsInstanceMembers()
    {
        // A variable whose name collides (case-insensitively) with a type name
        // must resolve as a variable, not as the type receiver.
        var source = "Sub Main()\n    Dim random As Random\n    random.\nEnd Sub";
        var state = CreateManagedState(source);

        var result = _completionService.GetCompletions(state, 2, "    random.".Length);

        Assert.That(result.Any(c => c.Label == "Next"), Is.True, "expected instance method Random.Next");
    }

    // ------------------------------------------------------------------
    // [5] Method-chain receivers resolved left-to-right
    // ------------------------------------------------------------------

    [Test]
    public void MethodChain_SingleHop_ReturnsMembersOfReturnType()
    {
        var source = "Sub Main()\n    Dim s As String\n    s.Trim().\nEnd Sub";
        var state = CreateManagedState(source);

        var result = _completionService.GetCompletions(state, 2, "    s.Trim().".Length);

        Assert.That(result, Is.Not.Empty, "'s.Trim().' must resolve through the method return type");
        Assert.That(result.Any(c => c.Label == "Length"), Is.True, "expected String member 'Length'");
        Assert.That(result.Any(c => c.Label == "ToUpper"), Is.True, "expected String member 'ToUpper'");
    }

    [Test]
    public void MethodChain_TwoHops_ReturnsMembersOfFinalType()
    {
        var source = "Sub Main()\n    Dim s As String\n    s.Trim().ToUpper().\nEnd Sub";
        var state = CreateManagedState(source);

        var result = _completionService.GetCompletions(state, 2, "    s.Trim().ToUpper().".Length);

        Assert.That(result.Any(c => c.Label == "Substring"), Is.True, "expected String member after two chained hops");
    }

    [Test]
    public void FunctionCallReceiver_ReturnsMembersOfReturnType()
    {
        var source = "Function GetName() As String\n" +
                     "    Return \"x\"\n" +
                     "End Function\n" +
                     "Sub Main()\n" +
                     "    GetName().\n" +
                     "End Sub";
        var state = CreateManagedState(source);

        var result = _completionService.GetCompletions(state, 4, "    GetName().".Length);

        Assert.That(result.Any(c => c.Label == "ToUpper"), Is.True,
            "expected String members for the return type of GetName()");
    }

    // ------------------------------------------------------------------
    // [19] For Each loop variable typed from the collection element type
    // ------------------------------------------------------------------

    [Test]
    public void ForEachVariable_FromGenericList_GetsElementTypeMembers()
    {
        var source = "Sub Main()\n" +
                     "    Dim names As List(Of String)\n" +
                     "    For Each n In names\n" +
                     "        n.\n" +
                     "    Next\n" +
                     "End Sub";
        var state = CreateManagedState(source);

        var result = _completionService.GetCompletions(state, 3, "        n.".Length);

        Assert.That(result.Any(c => c.Label == "ToUpper"), Is.True,
            "for-each variable over List(Of String) must complete String members");
    }

    [Test]
    public void ForEachVariable_WithExplicitType_UsesDeclaredType()
    {
        var source = "Sub Main()\n" +
                     "    Dim nums As List(Of Integer)\n" +
                     "    For Each n As Integer In nums\n" +
                     "        n.\n" +
                     "    Next\n" +
                     "End Sub";
        var state = CreateManagedState(source);

        var result = _completionService.GetCompletions(state, 3, "        n.".Length);

        Assert.That(result.Any(c => c.Label == "ToString" || c.Label == "CompareTo"), Is.True,
            "explicitly typed for-each variable must complete its declared type's members");
    }
}
