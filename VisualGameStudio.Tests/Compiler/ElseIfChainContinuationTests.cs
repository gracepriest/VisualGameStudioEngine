using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// The statements AFTER an <c>If … ElseIf … End If</c> chain must run on every path — above all
/// when the FIRST branch is taken.
///
/// <para><b>MEASURED on master</b>: the C# AND JavaScript backends emitted the code after the chain
/// inside the chain's outer <c>else</c>, so <c>Chain(1)</c> printed "chain one" and never "chain end",
/// with a green build; C++ (goto-based) was right. On JavaScript a For loop whose body ended with a
/// chain lost its increment on the Then path and never terminated. An ElseIf is lowered as a nested
/// conditional (<c>ifN.elseifK.then</c> / <c>.else</c>) that branches to the OUTER If's <c>ifN.end</c>,
/// and both emitters reconstruct structure from block names — so the nested conditional found the
/// same merge block and, being processed first, emitted it inside the outer Else. Only the outermost
/// If now emits a merge block it shares.</para>
/// </summary>
[TestFixture]
public class ElseIfChainContinuationTests
{
    internal const string Program = @"
Sub Chain(n As Integer)
    If n = 1 Then
        Console.WriteLine(""chain one"")
    ElseIf n = 2 Then
        Console.WriteLine(""chain two"")
    Else
        Console.WriteLine(""chain other"")
    End If
    Console.WriteLine(""chain end"")
End Sub

Sub NoElse(n As Integer)
    If n = 1 Then
        Console.WriteLine(""ne one"")
    ElseIf n = 2 Then
        Console.WriteLine(""ne two"")
    ElseIf n = 3 Then
        Console.WriteLine(""ne three"")
    End If
    Console.WriteLine(""ne end"")
End Sub

Sub Nested(n As Integer, m As Integer)
    If n = 1 Then
        If m = 1 Then
            Console.WriteLine(""inner one"")
        ElseIf m = 2 Then
            Console.WriteLine(""inner two"")
        End If
        Console.WriteLine(""after inner"")
    ElseIf n = 2 Then
        Console.WriteLine(""outer two"")
    End If
    Console.WriteLine(""nested end"")
End Sub

Sub InLoop()
    For i As Integer = 1 To 3
        If i = 1 Then
            Console.WriteLine(""loop one"")
        ElseIf i = 2 Then
            Console.WriteLine(""loop two"")
        Else
            Console.WriteLine(""loop other"")
        End If
        Console.WriteLine(""loop tail "" & i)
    Next
    Console.WriteLine(""loop end"")
End Sub

Sub ThenSelect(n As Integer)
    If n = 1 Then
        Console.WriteLine(""ts one"")
    ElseIf n = 2 Then
        Console.WriteLine(""ts two"")
    End If
    Select Case n
        Case 1
            Console.WriteLine(""ts case one"")
        Case Else
            Console.WriteLine(""ts case other"")
    End Select
End Sub

Function Classify(n As Integer) As String
    Dim label As String = ""?""
    If n < 0 Then
        Return ""negative""
    ElseIf n = 0 Then
        label = ""zero""
    Else
        label = ""positive""
    End If
    Return label & ""!""
End Function

Sub InTry(n As Integer)
    Try
        If n = 1 Then
            Console.WriteLine(""try one"")
        ElseIf n = 2 Then
            Console.WriteLine(""try two"")
        End If
        Console.WriteLine(""try tail"")
    Catch ex As Exception
        Console.WriteLine(""caught"")
    End Try
    Console.WriteLine(""try end"")
End Sub

Sub Main()
    Chain(1)
    Chain(2)
    Chain(3)
    NoElse(1)
    NoElse(3)
    NoElse(4)
    Nested(1, 2)
    Nested(1, 3)
    Nested(2, 1)
    InLoop()
    ThenSelect(1)
    ThenSelect(3)
    Console.WriteLine(Classify(-1))
    Console.WriteLine(Classify(0))
    Console.WriteLine(Classify(5))
    InTry(1)
    InTry(3)
End Sub
";

    internal const string Expected =
        "chain one\nchain end\nchain two\nchain end\nchain other\nchain end\n" +
        "ne one\nne end\nne three\nne end\nne end\n" +
        "inner two\nafter inner\nnested end\nafter inner\nnested end\nouter two\nnested end\n" +
        "loop one\nloop tail 1\nloop two\nloop tail 2\nloop other\nloop tail 3\nloop end\n" +
        "ts one\nts case one\nts case other\n" +
        "negative\nzero!\npositive!\n" +
        "try one\ntry tail\ntry end\ntry tail\ntry end";

    /// <summary>
    /// The line after a chain is emitted ONCE, at the depth of the chain itself — not inside one of
    /// its arms. <paramref name="indent"/> is the statement's column: 12 is a Sub's top level, 16 one
    /// block deeper (inside the enclosing Then arm or the Try).
    /// </summary>
    [TestCase("Chain", "\"chain end\"", 12)]
    [TestCase("NoElse", "\"ne end\"", 12)]
    [TestCase("Nested", "\"nested end\"", 12)]
    [TestCase("Nested", "\"after inner\"", 16)]
    [TestCase("InTry", "\"try tail\"", 16)]
    public void CSharp_CodeAfterChain_IsEmittedOnceAtTheChainsDepth(string sub, string literal, int indent)
    {
        var cs = ReturnCoercionTests.EmitCSharpForTest(Program).Replace("\r\n", "\n");
        var start = cs.IndexOf($"public static void {sub}(", System.StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"no {sub} in the emitted C#");
        var end = cs.IndexOf("\n        }\n", start, System.StringComparison.Ordinal);
        var body = cs.Substring(start, end - start);
        var first = body.IndexOf(literal, System.StringComparison.Ordinal);
        Assert.That(first, Is.GreaterThan(0), $"{literal} is missing from {sub}:\n{body}");
        Assert.That(body.IndexOf(literal, first + 1, System.StringComparison.Ordinal), Is.EqualTo(-1),
            $"{literal} is emitted twice in {sub}:\n{body}");
        var line = body.Substring(body.LastIndexOf('\n', first) + 1);
        Assert.That(line, Does.StartWith(new string(' ', indent) + "Console.WriteLine(" + literal),
            $"{literal} must sit at column {indent} in {sub}, not inside an arm of the chain:\n{body}");
    }
    /// <summary>The same placement on JavaScript: a function's top-level statement sits at column 4.</summary>
    [Test]
    public void JavaScript_ChainEnd_IsAtTheFunctionsTopLevel()
    {
        var js = JsTestSupport.CompileOptimized(Program).Replace("\r\n", "\n");
        var start = js.IndexOf("function Chain(n) {", System.StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), "no Chain in the emitted JS");
        var body = js.Substring(start, js.IndexOf("\n}\n", start, System.StringComparison.Ordinal) - start);
        Assert.That(body, Does.Contain("\n    console.log(\"chain end\");"),
            "\"chain end\" must sit at the function's top level, not inside the outer else:\n" + body);
    }
}

/// <summary>The run half: C#, C++ and JavaScript through the optimizer, byte-identical.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class ElseIfChainContinuationRunTests
{
    private static string Norm(string s) => FourBackends.Norm(s);

    [Test]
    public void ElseIfChain_CSharp()
        => Assert.That(Norm(FourBackends.RunEmittedCSharp(ElseIfChainContinuationTests.Program)),
            Is.EqualTo(ElseIfChainContinuationTests.Expected));

    [Test]
    public void ElseIfChain_Cpp()
        => Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(ElseIfChainContinuationTests.Program))),
            Is.EqualTo(ElseIfChainContinuationTests.Expected));

    // CompileOptimized, NOT JavaScriptExecutionTests.RunJs — that one runs no optimizer.
    [Test]
    public void ElseIfChain_JavaScript()
        => Assert.That(Norm(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.CompileOptimized(ElseIfChainContinuationTests.Program))),
            Is.EqualTo(ElseIfChainContinuationTests.Expected));
}
