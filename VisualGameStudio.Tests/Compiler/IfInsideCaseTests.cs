using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// An <c>If</c> (or a nested <c>Select</c>) inside a <c>Case</c> body must survive the C# backend,
/// together with every statement after it in that case.
///
/// <para><b>MEASURED on master</b> (<c>e7df9552</c>): a <c>Case 1</c> holding
/// <c>If m = 1 … Else … End If</c> and a WriteLine compiled to <c>case 1: break;</c> — the If and the
/// rest of the case body were DROPPED with a green build, in a <c>Case</c> and in the
/// <c>Case Else</c> alike. C++ and JavaScript were right. A case section's first block ends in the
/// If's conditional branch, and the case-section emitter followed only a Return, a loop exit or a
/// plain branch; everything else fell through to a bare <c>break;</c>. A nested <c>Select</c>
/// (an IRSwitch terminator) was lost the same way.</para>
/// </summary>
[TestFixture]
public class IfInsideCaseTests
{
    internal const string Program = @"
Sub IfElseInCase(n As Integer, m As Integer)
    Select Case n
        Case 1
            If m = 1 Then
                Console.WriteLine(""one-one"")
            Else
                Console.WriteLine(""one-other"")
            End If
            Console.WriteLine(""case one tail"")
        Case 2
            If m > 0 Then
                Console.WriteLine(""two-pos"")
            End If
            Console.WriteLine(""case two tail"")
        Case Else
            Console.WriteLine(""else"")
    End Select
    Console.WriteLine(""ifelse end"")
End Sub

Sub ChainInCase(n As Integer, m As Integer)
    Select Case n
        Case 1
            If m = 1 Then
                Console.WriteLine(""chain a"")
            ElseIf m = 2 Then
                Console.WriteLine(""chain b"")
            Else
                Console.WriteLine(""chain c"")
            End If
            Console.WriteLine(""chain tail"")
        Case Else
            Console.WriteLine(""chain else"")
    End Select
End Sub

Sub IfInCaseElse(n As Integer)
    Select Case n
        Case 1
            Console.WriteLine(""ce one"")
        Case Else
            If n > 10 Then
                Console.WriteLine(""ce big"")
            Else
                Console.WriteLine(""ce small"")
            End If
            Console.WriteLine(""ce tail"")
    End Select
End Sub

Sub NestedSelect(n As Integer, m As Integer)
    Select Case n
        Case 1
            Select Case m
                Case 1
                    Console.WriteLine(""inner one"")
                Case Else
                    Console.WriteLine(""inner other"")
            End Select
            Console.WriteLine(""outer one tail"")
        Case Else
            Console.WriteLine(""outer else"")
    End Select
    Console.WriteLine(""nested end"")
End Sub

Sub PatternCase(n As Integer)
    Select Case n
        Case Is > 5
            If n = 9 Then
                Console.WriteLine(""pattern nine"")
            End If
            Console.WriteLine(""pattern big"")
        Case Else
            Console.WriteLine(""pattern small"")
    End Select
End Sub

Function Describe(n As Integer) As String
    Select Case n
        Case 1
            If n > 0 Then
                Return ""one positive""
            End If
            Return ""unreachable""
        Case Else
            Return ""other""
    End Select
End Function

Sub InLoop()
    For i As Integer = 1 To 4
        Select Case i
            Case 2
                If i = 2 Then
                    Console.WriteLine(""loop two"")
                End If
                Console.WriteLine(""loop case tail"")
            Case 3
                If i = 3 Then
                    Exit For
                End If
                Console.WriteLine(""not reached"")
            Case Else
                Console.WriteLine(""loop "" & i)
        End Select
    Next
    Console.WriteLine(""loop end"")
End Sub

Sub Main()
    IfElseInCase(1, 1)
    IfElseInCase(1, 2)
    IfElseInCase(2, 1)
    IfElseInCase(2, 0)
    IfElseInCase(3, 0)
    ChainInCase(1, 2)
    ChainInCase(1, 5)
    ChainInCase(4, 0)
    IfInCaseElse(1)
    IfInCaseElse(20)
    IfInCaseElse(3)
    NestedSelect(1, 1)
    NestedSelect(1, 2)
    NestedSelect(2, 0)
    PatternCase(9)
    PatternCase(7)
    PatternCase(1)
    Console.WriteLine(Describe(1))
    Console.WriteLine(Describe(2))
    InLoop()
End Sub
";

    internal const string Expected =
        "one-one\ncase one tail\nifelse end\none-other\ncase one tail\nifelse end\n" +
        "two-pos\ncase two tail\nifelse end\ncase two tail\nifelse end\nelse\nifelse end\n" +
        "chain b\nchain tail\nchain c\nchain tail\nchain else\n" +
        "ce one\nce big\nce tail\nce small\nce tail\n" +
        "inner one\nouter one tail\nnested end\ninner other\nouter one tail\nnested end\nouter else\nnested end\n" +
        "pattern nine\npattern big\npattern big\npattern small\n" +
        "one positive\nother\n" +
        "loop 1\nloop two\nloop case tail\nloop end";

    /// <summary>Every statement of a case body that holds an If or a Select is emitted.</summary>
    [TestCase("IfElseInCase", "\"one-other\"")]
    [TestCase("IfElseInCase", "\"case one tail\"")]
    [TestCase("IfElseInCase", "\"two-pos\"")]
    [TestCase("IfElseInCase", "\"case two tail\"")]
    [TestCase("ChainInCase", "\"chain b\"")]
    [TestCase("ChainInCase", "\"chain tail\"")]
    [TestCase("IfInCaseElse", "\"ce big\"")]
    [TestCase("IfInCaseElse", "\"ce tail\"")]
    [TestCase("NestedSelect", "\"inner other\"")]
    [TestCase("NestedSelect", "\"outer one tail\"")]
    [TestCase("PatternCase", "\"pattern nine\"")]
    [TestCase("InLoop", "\"loop case tail\"")]
    public void CSharp_CaseBodyStatement_IsEmittedOnce(string sub, string literal)
    {
        var body = SubBody(ReturnCoercionTests.EmitCSharpForTest(Program), sub);
        var first = body.IndexOf(literal, System.StringComparison.Ordinal);
        Assert.That(first, Is.GreaterThan(0), $"{literal} was dropped from {sub}:\n{body}");
        Assert.That(body.IndexOf(literal, first + 1, System.StringComparison.Ordinal), Is.EqualTo(-1),
            $"{literal} is emitted twice in {sub}:\n{body}");
    }

    /// <summary>The case's tail follows the If inside the SAME case section, before its break.</summary>
    [Test]
    public void CSharp_CaseTail_IsInsideItsCaseSection()
    {
        var body = SubBody(ReturnCoercionTests.EmitCSharpForTest(Program), "IfElseInCase");
        var case1 = body.IndexOf("case 1:", System.StringComparison.Ordinal);
        var tail = body.IndexOf("\"case one tail\"", System.StringComparison.Ordinal);
        var case2 = body.IndexOf("case 2:", System.StringComparison.Ordinal);
        Assert.That(case1, Is.GreaterThan(0), body);
        Assert.That(tail, Is.GreaterThan(case1).And.LessThan(case2),
            "\"case one tail\" must be emitted inside case 1:\n" + body);
        Assert.That(body.IndexOf("\"ifelse end\"", System.StringComparison.Ordinal),
            Is.GreaterThan(body.IndexOf("default:", System.StringComparison.Ordinal)),
            "the code after the Select must follow the switch:\n" + body);
    }

    private static string SubBody(string cs, string sub)
    {
        cs = cs.Replace("\r\n", "\n");
        var start = cs.IndexOf($"public static void {sub}(", System.StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"no {sub} in the emitted C#");
        var end = cs.IndexOf("\n        }\n", start, System.StringComparison.Ordinal);
        return cs.Substring(start, end - start);
    }
}

/// <summary>The run half: C#, C++ and JavaScript through the optimizer, byte-identical.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class IfInsideCaseRunTests
{
    private static string Norm(string s) => FourBackends.Norm(s);

    [Test]
    public void IfInsideCase_CSharp()
        => Assert.That(Norm(FourBackends.RunEmittedCSharp(IfInsideCaseTests.Program)),
            Is.EqualTo(IfInsideCaseTests.Expected));

    [Test]
    public void IfInsideCase_Cpp()
        => Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(IfInsideCaseTests.Program))),
            Is.EqualTo(IfInsideCaseTests.Expected));

    // CompileOptimized, NOT JavaScriptExecutionTests.RunJs — that one runs no optimizer.
    [Test]
    public void IfInsideCase_JavaScript()
        => Assert.That(Norm(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.CompileOptimized(IfInsideCaseTests.Program))),
            Is.EqualTo(IfInsideCaseTests.Expected));
}
