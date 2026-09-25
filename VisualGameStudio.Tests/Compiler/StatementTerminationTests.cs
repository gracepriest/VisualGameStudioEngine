using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// VB's BC30205, "End of statement expected". A block used to go straight on to the next statement
/// after each one, so anything left on the line was parsed as ANOTHER statement and a bare
/// expression there was silently dropped. MEASURED on master e7df955 — every one of these compiled
/// clean on every backend: <c>Dim x As Integer = 1 y</c> (x = 1), <c>y = 1 2</c>,
/// <c>Console.WriteLine(y) 7</c>, <c>If y &gt; 1 Then y = 2 y</c>, <c>y += 1 y</c>. It is also what
/// made <c>1E40</c> (then lexed as <c>1</c> <c>E40</c>) compile to x = 1, and it let
/// <c>New Integer() {1, 2}</c>, whose initializer the parser did not read then, compile and then
/// crash at run time on JavaScript.
///
/// <para>⚠ Form-designer Task 24a taught the parser that initializer (<c>New T() { … }</c>), so the
/// bare shape is now WELL-FORMED (see <c>TypedArrayLiteralTests</c>) and is pinned below as such;
/// what stays an error is a token left over AFTER the initializer.</para>
/// </summary>
public class StatementTerminationTests
{
    private static string[] ParseErrors(string body)
    {
        var source = "Sub Main()\n    Dim y As Integer = 5\n" + body + "\nEnd Sub\n";
        var parser = new Parser(new Lexer(source).Tokenize());
        parser.Parse();
        return parser.Errors.Select(e => e.Message).ToArray();
    }

    [TestCase("    Dim x As Integer = 1 y", "y")]
    [TestCase("    Dim x As Integer = 1 2", "2")]
    [TestCase("    Dim x As Integer = y y", "y")]
    [TestCase("    y = 1 y", "y")]
    [TestCase("    y = 1 2", "2")]
    [TestCase("    y += 1 y", "y")]
    [TestCase("    Console.WriteLine(y) 7", "7")]
    [TestCase("    If y > 1 Then y = 2 y", "y")]
    [TestCase("    Dim x = New Integer() {1, 2} y", "y")]
    public void TrailingTokens_AreAnError(string line, string found) =>
        Assert.That(ParseErrors(line), Has.Some.Contains($"End of statement expected, found '{found}'"));

    /// <summary>A multi-line lambda body is a block too.</summary>
    [Test]
    public void TrailingTokens_InAMultiLineLambdaBody_AreAnError() =>
        Assert.That(ParseErrors("    Dim f = Sub()\n        y = 1 z\n    End Sub"),
            Has.Some.Contains("End of statement expected, found 'z'"));

    /// <summary>What must keep parsing: the check is at the END of a statement, nowhere else.</summary>
    [TestCase("    Dim x As Integer = 1 : y = 2", TestName = "ColonSeparatesStatements")]
    [TestCase("    If y > 1 Then\n        y = 2\n    Else\n        y = 3\n    End If", TestName = "MultiLineIf")]
    [TestCase("    If y > 1 Then y = 2", TestName = "SingleLineIf")]
    [TestCase("    Dim f = Sub(v As Integer)\n        y = v\n    End Sub\n    f(3)", TestName = "MultiLineSubLambda")]
    [TestCase("    Dim g = Function(v As Integer) v * 2", TestName = "SingleLineFunctionLambda")]
    [TestCase("    Dim h = Sub(v As Integer) y = v", TestName = "SingleLineSubLambda")]
    [TestCase("    Run(Sub() y = 1)", TestName = "SingleLineSubLambda_AsAnArgument")]
    [TestCase("    For i As Integer = 1 To 3\n        y += i\n    Next i", TestName = "ForNextWithVariable")]
    [TestCase("    Do\n        y += 1\n    Loop While y < 10", TestName = "DoLoopWhile")]
    [TestCase("    Try\n        y = 1\n    Catch ex As Exception\n        y = 2\n    End Try", TestName = "TryCatch")]
    [TestCase("    Dim a() As Integer = {1, 2, 3}", TestName = "ArrayLiteral")]
    [TestCase("    Dim x = New Integer() {1, 2}", TestName = "TypedArrayCreationWithInitializer")]
    [TestCase("    Dim z As Integer = y _\n        + 1", TestName = "LineContinuation")]
    public void WellFormedStatements_StillParse(string body) =>
        Assert.That(ParseErrors(body), Is.Empty);
}
