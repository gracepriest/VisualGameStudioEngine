using System.Text.RegularExpressions;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Two or more <c>Select Case</c> statements in ONE function on the JavaScript backend.
///
/// <para><b>MEASURED on master</b> (<c>f7609885</c>): the program did not run at all — node refused
/// it with "SyntaxError: Identifier '_sel0' has already been declared". A Select lowers to an
/// if/else chain over a <c>const _selN</c> subject temp, and N was the Select NESTING DEPTH,
/// decremented before the code after the Select was emitted, so every sibling Select declared
/// <c>_sel0</c> in the same scope. N is now a per-function count. C# and C++ were right.</para>
/// </summary>
[TestFixture]
public class SiblingSelectTests
{
    internal const string Program = @"
Sub Two(n As Integer)
    Select Case n
        Case 1
            Console.WriteLine(""first one"")
        Case Else
            Console.WriteLine(""first other"")
    End Select
    Select Case n * 2
        Case 2
            Console.WriteLine(""second two"")
        Case Else
            Console.WriteLine(""second other"")
    End Select
End Sub

Sub Three(n As Integer)
    Select Case n
        Case 1
            Console.WriteLine(""a1"")
    End Select
    Select Case n
        Case 1
            Console.WriteLine(""b1"")
    End Select
    Select Case n
        Case Is > 0
            Console.WriteLine(""c positive"")
    End Select
End Sub

Sub NestedThenSibling(n As Integer, m As Integer)
    Select Case n
        Case 1
            Select Case m
                Case 1
                    Console.WriteLine(""inner one"")
                Case Else
                    Console.WriteLine(""inner other"")
            End Select
        Case Else
            Console.WriteLine(""outer other"")
    End Select
    Select Case m
        Case 1
            Console.WriteLine(""after one"")
        Case Else
            Console.WriteLine(""after other"")
    End Select
End Sub

Sub InIfArms(n As Integer)
    If n > 0 Then
        Select Case n
            Case 1
                Console.WriteLine(""then one"")
        End Select
    Else
        Select Case n
            Case 0
                Console.WriteLine(""else zero"")
        End Select
    End If
    Select Case n
        Case 1
            Console.WriteLine(""tail one"")
        Case Else
            Console.WriteLine(""tail other"")
    End Select
End Sub

Sub InLoop()
    For i As Integer = 1 To 2
        Select Case i
            Case 1
                Console.WriteLine(""loop a1"")
        End Select
        Select Case i
            Case 2
                Console.WriteLine(""loop b2"")
        End Select
    Next
End Sub

Function Grade(n As Integer) As String
    Dim prefix As String = """"
    Select Case n
        Case Is >= 90
            prefix = ""A""
        Case Else
            prefix = ""B""
    End Select
    Select Case n Mod 10
        Case Is >= 5
            Return prefix & ""+""
        Case Else
            Return prefix
    End Select
End Function

Class Counter
    Public Sub Show(n As Integer)
        Select Case n
            Case 1
                Console.WriteLine(""method first"")
        End Select
        Select Case n
            Case 1
                Console.WriteLine(""method second"")
        End Select
    End Sub
End Class

Sub Main()
    Two(1)
    Two(5)
    Three(1)
    NestedThenSibling(1, 2)
    NestedThenSibling(3, 1)
    InIfArms(1)
    InIfArms(0)
    InLoop()
    Console.WriteLine(Grade(95))
    Console.WriteLine(Grade(81))
    Dim c As New Counter()
    c.Show(1)
End Sub
";

    internal const string Expected =
        "first one\nsecond two\nfirst other\nsecond other\n" +
        "a1\nb1\nc positive\n" +
        "inner other\nafter other\nouter other\nafter one\n" +
        "then one\ntail one\nelse zero\ntail other\n" +
        "loop a1\nloop b2\n" +
        "A+\nB\n" +
        "method first\nmethod second";

    /// <summary>No subject temp is declared twice in one emitted function.</summary>
    [TestCase("function Two(")]
    [TestCase("function Three(")]
    [TestCase("function NestedThenSibling(")]
    [TestCase("function InIfArms(")]
    [TestCase("function InLoop(")]
    [TestCase("function Grade(")]
    [TestCase("Show(n) {")]
    public void JavaScript_EachSelectSubject_IsDeclaredOnce(string header)
    {
        var js = JsTestSupport.CompileOptimized(Program).Replace("\r\n", "\n");
        var start = js.IndexOf(header, System.StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"no {header} in the emitted JS:\n{js}");
        var indent = js.LastIndexOf('\n', start) + 1;
        var close = "\n" + new string(' ', start - indent) + "}\n";
        var body = js.Substring(start, js.IndexOf(close, start, System.StringComparison.Ordinal) - start);

        var declared = Regex.Matches(body, @"const (_sel\d+) =");
        Assert.That(declared.Count, Is.GreaterThanOrEqualTo(2), $"expected two or more Selects:\n{body}");
        var names = new System.Collections.Generic.HashSet<string>();
        foreach (Match m in declared)
            Assert.That(names.Add(m.Groups[1].Value), Is.True, $"{m.Groups[1].Value} is declared twice:\n{body}");
    }

    /// <summary>The count restarts per function: every function's first Select is <c>_sel0</c>.</summary>
    [Test]
    public void JavaScript_SubjectNumbering_RestartsPerFunction()
    {
        var js = JsTestSupport.CompileOptimized(Program).Replace("\r\n", "\n");
        var three = js.IndexOf("function Three(", System.StringComparison.Ordinal);
        Assert.That(js.IndexOf("const _sel0 = ", three, System.StringComparison.Ordinal),
            Is.LessThan(js.IndexOf("const _sel1 = ", three, System.StringComparison.Ordinal)), js);
    }
}

/// <summary>The run half: C#, C++ and JavaScript through the optimizer, byte-identical.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class SiblingSelectRunTests
{
    private static string Norm(string s) => FourBackends.Norm(s);

    [Test]
    public void SiblingSelects_CSharp()
        => Assert.That(Norm(FourBackends.RunEmittedCSharp(SiblingSelectTests.Program)),
            Is.EqualTo(SiblingSelectTests.Expected));

    [Test]
    public void SiblingSelects_Cpp()
        => Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(SiblingSelectTests.Program))),
            Is.EqualTo(SiblingSelectTests.Expected));

    // CompileOptimized, NOT JavaScriptExecutionTests.RunJs — that one runs no optimizer.
    [Test]
    public void SiblingSelects_JavaScript()
        => Assert.That(Norm(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.CompileOptimized(SiblingSelectTests.Program))),
            Is.EqualTo(SiblingSelectTests.Expected));
}
