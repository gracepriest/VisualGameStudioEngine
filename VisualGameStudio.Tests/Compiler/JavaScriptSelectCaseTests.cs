using System.Linq;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Plan task 14, second half — <c>Select Case</c>.
///
/// <para><b>It lowers to an if/else-if chain, never a JavaScript <c>switch</c>.</b> JS has no
/// <c>when</c> guard syntax at all, and five further pattern kinds — ranges
/// (<c>Case 1 To 10</c>), comparisons (<c>Case Is &gt; 5</c>), type, or-patterns and tuple
/// patterns — have no <c>case</c> equivalent either. Only plain constants could map, and only
/// when no pattern is present, so emitting a chain unconditionally is both simpler and the
/// only shape that is correct for every arm.</para>
///
/// <para><b>The trap that makes guards special.</b> IRBuilder emits a <c>When</c> guard's
/// instructions with emission SUPPRESSED, so the guard is a free-floating operand tree that
/// never appears in any block. Rendering it by name — the way every other value in this
/// backend is referenced — produces an undefined identifier and a ReferenceError from a green
/// build. Guards must be rendered INLINE.</para>
/// </summary>
[TestFixture]
[Category("Integration")]   // spawns node
public class JavaScriptSelectCaseTests
{
    private static string Pick(string value, string body) =>
        JavaScriptExecutionTests.RunJs(
            $"Sub Main()\nDim n As Integer\nn = {value}\n{body}\nEnd Sub");

    private const string Simple =
        "Select Case n\n" +
        "Case 1\nConsole.WriteLine(\"one\")\n" +
        "Case 2\nConsole.WriteLine(\"two\")\n" +
        "Case Else\nConsole.WriteLine(\"other\")\n" +
        "End Select";

    [TestCase("1", "one")]
    [TestCase("2", "two")]
    [TestCase("7", "other")]
    public void SelectCase_Constants(string value, string expected)
        => Assert.That(Pick(value, Simple), Is.EqualTo(expected));

    /// <summary>
    /// <c>Case 1, 2, 3</c> produces THREE entries in IRSwitch.Cases all pointing at ONE block.
    /// Emitting one arm per entry would duplicate the body three times.
    /// </summary>
    [TestCase("1", "low")]
    [TestCase("3", "low")]
    [TestCase("9", "high")]
    public void SelectCase_MultipleValuesShareOneArm(string value, string expected)
        => Assert.That(Pick(value,
            "Select Case n\n" +
            "Case 1, 2, 3\nConsole.WriteLine(\"low\")\n" +
            "Case Else\nConsole.WriteLine(\"high\")\n" +
            "End Select"), Is.EqualTo(expected));

    /// <summary>No Case Else: a value matching nothing must simply fall through.</summary>
    [Test]
    public void SelectCase_WithNoMatchAndNoElse_FallsThrough()
        => Assert.That(Pick("9",
            "Select Case n\nCase 1\nConsole.WriteLine(\"one\")\nEnd Select\n" +
            "Console.WriteLine(\"after\")"), Is.EqualTo("after"));

    /// <summary>Code after End Select runs exactly once, whichever arm was taken.</summary>
    [Test]
    public void SelectCase_ContinuesAfterTheEndBlock_Once()
        => Assert.That(Pick("1",
            "Select Case n\nCase 1\nConsole.WriteLine(\"one\")\nCase Else\nConsole.WriteLine(\"x\")\nEnd Select\n" +
            "Console.WriteLine(\"after\")"), Is.EqualTo("one\nafter"));

    // ---------------------------------------------------------------- patterns

    [TestCase("7", "big")]
    [TestCase("2", "small")]
    public void SelectCase_ComparisonPattern(string value, string expected)
        => Assert.That(Pick(value,
            "Select Case n\n" +
            "Case Is > 5\nConsole.WriteLine(\"big\")\n" +
            "Case Else\nConsole.WriteLine(\"small\")\n" +
            "End Select"), Is.EqualTo(expected));

    [TestCase("5", "in")]
    [TestCase("1", "in")]
    [TestCase("10", "in")]
    [TestCase("11", "out")]
    public void SelectCase_RangePatternIsInclusive(string value, string expected)
        => Assert.That(Pick(value,
            "Select Case n\n" +
            "Case 1 To 10\nConsole.WriteLine(\"in\")\n" +
            "Case Else\nConsole.WriteLine(\"out\")\n" +
            "End Select"), Is.EqualTo(expected));

    /// <summary>
    /// The guard case. Its operand tree was never emitted into any block, so it has to be
    /// rendered inline — referencing it by name yields an undefined identifier.
    /// </summary>
    [TestCase("8", "guarded")]
    [TestCase("3", "plain")]
    public void SelectCase_WhenGuard(string value, string expected)
        => Assert.That(Pick(value,
            "Select Case n\n" +
            "Case Is > 0 When n > 5\nConsole.WriteLine(\"guarded\")\n" +
            "Case Else\nConsole.WriteLine(\"plain\")\n" +
            "End Select"), Is.EqualTo(expected));

    // ---------------------------------------------------------------- nesting

    [Test]
    public void SelectCase_InsideALoop_EvaluatesEachIteration()
        => Assert.That(JavaScriptExecutionTests.RunJs(
            "Sub Main()\n" +
            "For i As Integer = 1 To 3\n" +
            "Select Case i\n" +
            "Case 1\nConsole.WriteLine(\"a\")\n" +
            "Case 2\nConsole.WriteLine(\"b\")\n" +
            "Case Else\nConsole.WriteLine(\"c\")\n" +
            "End Select\n" +
            "Next\nEnd Sub"), Is.EqualTo("a\nb\nc"));

    // ------------------------------------------------- several Selects in one scope

    /// <summary>
    /// ⛔ The subject temp was named after the NESTING DEPTH, which dropped back before the code
    /// after a Select was emitted, so two Selects in a row both declared <c>const _sel0</c>: a
    /// SyntaxError that stopped the whole program loading. In a row, nested in sequence, inside
    /// a loop body and inside a function, with and without --optimize.
    /// </summary>
    private const string SeveralSelects = @"
Function Grade(n As Integer) As String
    Dim g As String = """"
    Select Case n
        Case Is >= 90 : g = ""A""
        Case Else : g = ""C""
    End Select
    Select Case g
        Case ""A"" : g = g & ""+""
    End Select
    Return g
End Function

Sub Main()
    Dim x As Integer = 5
    Dim log As String = """"
    Select Case x
        Case 5 : log = log & ""a""
    End Select
    Select Case x
        Case 5 : log = log & ""b""
    End Select
    Select Case x
        Case 5
            Select Case x * 2
                Case 10 : log = log & ""c""
            End Select
            Select Case x * 3
                Case 15 : log = log & ""d""
            End Select
    End Select
    For i As Integer = 1 To 2
        Select Case i
            Case 1 : log = log & ""e""
        End Select
        Select Case i
            Case 2 : log = log & ""f""
        End Select
    Next
    Console.WriteLine(log)
    Console.WriteLine(Grade(95) & Grade(10))
End Sub";

    [Test]
    public void SeveralSelects_InOneScope_Run()
        => Assert.That(JavaScriptExecutionTests.RunJs(SeveralSelects), Is.EqualTo("abcdef\nA+C"));

    [Test]
    public void SeveralSelects_InOneScope_Run_Optimized()
        => Assert.That(JavaScriptOptimizedExecutionTests.RunOptimized(SeveralSelects).Replace("\r\n", "\n").TrimEnd('\n'),
            Is.EqualTo("abcdef\nA+C"));
}

/// <summary>The codegen half of the `_sel0` fix: every Select subject is a distinct const.</summary>
[TestFixture]
public class JavaScriptSelectSubjectNameTests
{
    [Test]
    public void TwoSelectsInARow_DeclareDistinctSubjects()
    {
        var js = new BasicLang.Compiler.CodeGen.JavaScript.JavaScriptCodeGenerator().Generate(JsTestSupport.BuildModule(
            "Sub Main()\nDim x As Integer = 5\n" +
            "Select Case x\nCase 5\nConsole.WriteLine(1)\nEnd Select\n" +
            "Select Case x\nCase 5\nConsole.WriteLine(2)\nEnd Select\nEnd Sub"));
        var names = System.Text.RegularExpressions.Regex.Matches(js, @"const (_sel\d+) =")
            .Select(m => m.Groups[1].Value).ToList();
        Assert.That(names, Has.Count.EqualTo(2), js);
        Assert.That(names, Is.Unique, js);
    }
}
