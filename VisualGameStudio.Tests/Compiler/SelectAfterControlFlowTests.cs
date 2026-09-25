using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A <c>Select Case</c> that FOLLOWS a structured construct, or ENDS an If arm, must survive the C#
/// backend.
///
/// <para><b>MEASURED on master</b>: the C# backend dropped the Select — its case bodies AND every
/// statement after it — in all eight positions below, with a green build, while C++ and JavaScript
/// printed the right thing. Even a plain Sub lost it: <c>If … End If</c> then <c>Select Case n</c>
/// compiled to the If alone. The C# emitter reconstructs structure from the CFG, and each of its
/// continuation sites (an If's merge block, the end block of a For/While/Do, a Try and a For Each)
/// and each If arm followed only a conditional or plain branch, never an IRSwitch — the same gap
/// SelectInsideTryTests found in the Try regions. Two helpers now carry one terminator dispatch.</para>
///
/// <para>Pinned elsewhere: code after an <c>ElseIf</c> chain (ElseIfChainContinuationTests) and an
/// <c>If</c> inside a <c>Case</c> body (IfInsideCaseTests), and two Selects in one function on
/// JavaScript (SiblingSelectTests — each Select here lives in its own Sub because that once failed).</para>
/// </summary>
[TestFixture]
public class SelectAfterControlFlowTests
{
    internal const string Program = @"
Sub AfterIf(n As Integer)
    If n = 1 Then
        Console.WriteLine(""if taken"")
    End If
    Select Case n
        Case 1
            Console.WriteLine(""after-if one"")
        Case Else
            Console.WriteLine(""after-if other"")
    End Select
    Console.WriteLine(""after-if end"")
End Sub

Sub AfterIfElse(n As Integer)
    If n = 1 Then
        Console.WriteLine(""then"")
    Else
        Console.WriteLine(""else"")
    End If
    Select Case n
        Case 1
            Console.WriteLine(""after-ifelse one"")
        Case Else
            Console.WriteLine(""after-ifelse other"")
    End Select
End Sub

Sub InThen(n As Integer)
    If n > 0 Then
        Console.WriteLine(""then start"")
        Select Case n
            Case 1
                Console.WriteLine(""in-then one"")
            Case Else
                Console.WriteLine(""in-then other"")
        End Select
    End If
    Console.WriteLine(""in-then end"")
End Sub

Sub InElse(n As Integer)
    If n > 5 Then
        Console.WriteLine(""big"")
    Else
        Select Case n
            Case 1
                Console.WriteLine(""in-else one"")
            Case Else
                Console.WriteLine(""in-else other"")
        End Select
    End If
    Console.WriteLine(""in-else end"")
End Sub

Sub AfterFor(n As Integer)
    For i As Integer = 1 To 2
        Console.WriteLine(""for "" & i)
    Next
    Select Case n
        Case 1
            Console.WriteLine(""after-for one"")
        Case Else
            Console.WriteLine(""after-for other"")
    End Select
End Sub

Sub AfterWhile(n As Integer)
    Dim i As Integer = 0
    While i < 2
        i = i + 1
    End While
    Select Case n
        Case 1
            Console.WriteLine(""after-while one"")
        Case Else
            Console.WriteLine(""after-while other"")
    End Select
End Sub

Sub AfterTry(n As Integer)
    Try
        Console.WriteLine(""try body"")
    Catch ex As Exception
        Console.WriteLine(""caught"")
    End Try
    Select Case n
        Case 1
            Console.WriteLine(""after-try one"")
        Case Else
            Console.WriteLine(""after-try other"")
    End Select
End Sub

Sub AfterForEach(n As Integer)
    Dim xs As New List(Of Integer)
    xs.Add(5)
    For Each x As Integer In xs
        Console.WriteLine(""each "" & x)
    Next
    Select Case n
        Case 1
            Console.WriteLine(""after-each one"")
        Case Else
            Console.WriteLine(""after-each other"")
    End Select
End Sub

Sub InLoopBody()
    For i As Integer = 1 To 3
        If i = 2 Then
            Console.WriteLine(""loop if "" & i)
        End If
        Select Case i
            Case 1
                Console.WriteLine(""loop sel one"")
            Case Else
                Console.WriteLine(""loop sel "" & i)
        End Select
    Next
    Console.WriteLine(""loop end"")
End Sub

Sub FinallyIfThenSelect(n As Integer)
    Try
        Console.WriteLine(""body"")
    Finally
        If n = 7 Then
            Console.WriteLine(""fin seven"")
        Else
            Console.WriteLine(""fin other"")
        End If
        Select Case n
            Case 7
                Console.WriteLine(""fin case seven"")
            Case Else
                Console.WriteLine(""fin case else"")
        End Select
    End Try
End Sub

Sub Main()
    AfterIf(1)
    AfterIf(2)
    AfterIfElse(1)
    InThen(1)
    InThen(2)
    InElse(1)
    InElse(9)
    AfterFor(1)
    AfterWhile(2)
    AfterTry(1)
    AfterForEach(2)
    InLoopBody()
    FinallyIfThenSelect(7)
End Sub
";

    internal const string Expected =
        "if taken\nafter-if one\nafter-if end\nafter-if other\nafter-if end\n" +
        "then\nafter-ifelse one\n" +
        "then start\nin-then one\nin-then end\nthen start\nin-then other\nin-then end\n" +
        "in-else one\nin-else end\nbig\nin-else end\n" +
        "for 1\nfor 2\nafter-for one\n" +
        "after-while other\n" +
        "try body\nafter-try one\n" +
        "each 5\nafter-each other\n" +
        "loop sel one\nloop if 2\nloop sel 2\nloop sel 3\nloop end\n" +
        "body\nfin seven\nfin case seven";

    /// <summary>Every Sub with a Select must still contain a switch in the emitted C#.</summary>
    [TestCase("AfterIf")]
    [TestCase("AfterIfElse")]
    [TestCase("InThen")]
    [TestCase("InElse")]
    [TestCase("AfterFor")]
    [TestCase("AfterWhile")]
    [TestCase("AfterTry")]
    [TestCase("AfterForEach")]
    [TestCase("InLoopBody")]
    [TestCase("FinallyIfThenSelect")]
    public void CSharp_SelectIsEmitted(string sub)
    {
        var cs = ReturnCoercionTests.EmitCSharpForTest(Program).Replace("\r\n", "\n");
        var start = cs.IndexOf($"public static void {sub}(", System.StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"no {sub} in the emitted C#");
        var end = cs.IndexOf("\n        }\n", start, System.StringComparison.Ordinal);
        var body = cs.Substring(start, end - start);
        Assert.That(body, Does.Contain("switch ("), $"the Select in {sub} was dropped:\n{body}");
    }
}

/// <summary>The run half: C#, C++ and JavaScript through the optimizer, byte-identical.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class SelectAfterControlFlowRunTests
{
    private static string Norm(string s) => FourBackends.Norm(s);

    [Test]
    public void SelectAfterControlFlow_CSharp()
        => Assert.That(Norm(FourBackends.RunEmittedCSharp(SelectAfterControlFlowTests.Program)),
            Is.EqualTo(SelectAfterControlFlowTests.Expected));

    [Test]
    public void SelectAfterControlFlow_Cpp()
        => Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(SelectAfterControlFlowTests.Program))),
            Is.EqualTo(SelectAfterControlFlowTests.Expected));

    // CompileOptimized, NOT JavaScriptExecutionTests.RunJs — that one runs no optimizer.
    [Test]
    public void SelectAfterControlFlow_JavaScript()
        => Assert.That(Norm(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.CompileOptimized(SelectAfterControlFlowTests.Program))),
            Is.EqualTo(SelectAfterControlFlowTests.Expected));
}
