using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Control flow inside a <c>Try</c>, <c>Catch</c> or <c>Finally</c> must survive codegen. Three
/// separate defects, all MEASURED on master:
/// <code>
///   C#   Try : Select Case n ...            the whole Select vanished — an empty `try { }`, a
///                                           green build, a program that printed nothing. The
///                                           try/catch emitter followed an If or a branch out of
///                                           its first block, never an IRSwitch. A Finally
///                                           followed nothing, so an If there vanished too.
///   C++  Try : Select Case n ...            "jump to label 'switch0_end'": the case bodies were
///                                           emitted OUTSIDE the try braces (ControlFlowTargets
///                                           ignored PatternCases, where the parser puts every
///                                           Case value) and the switch jumped back in.
///   C++  Finally : Select Case n ...        compiled, then looped forever: the two Finally
///                                           copies fell out of their region instead of jumping,
///                                           and a switch's end block is not its last block.
///   All  Finally : If n = 7 ...             the analyzer never visited a Finally body: its
///                                           comparison was untyped ("cannot convert 'bool' to
///                                           'void*'" on C++) and an undeclared name there
///                                           compiled successfully.
/// </code>
/// </summary>
[TestFixture]
public class SelectInsideTryTests
{
    internal const string Program = @"
Sub InTry(n As Integer)
    Try
        Select Case n
            Case 7
                Console.WriteLine(""try seven"")
            Case 1 To 3
                Console.WriteLine(""try small"")
            Case Else
                Console.WriteLine(""try other"")
        End Select
        Console.WriteLine(""try after select"")
    Catch ex As Exception
        Console.WriteLine(""caught"")
    End Try
    Console.WriteLine(""after end try"")
End Sub

Sub InCatch(n As Integer)
    Try
        Throw New InvalidOperationException(""boom"")
    Catch ex As InvalidOperationException
        Select Case n
            Case 7
                Console.WriteLine(""catch seven"")
            Case Else
                Console.WriteLine(""catch other"")
        End Select
    End Try
End Sub

Sub InFinally(n As Integer)
    Try
        Console.WriteLine(""body"")
    Finally
        Select Case n
            Case 7
                Console.WriteLine(""finally seven"")
            Case Else
                Console.WriteLine(""finally other"")
        End Select
    End Try
End Sub

Sub IfInFinally(n As Integer)
    Try
        Console.WriteLine(""body2"")
    Finally
        If n = 7 Then
            Console.WriteLine(""finally if seven"")
        Else
            Console.WriteLine(""finally if other"")
        End If
    End Try
End Sub

Sub LoopInTry()
    Try
        For i As Integer = 1 To 3
            Select Case i
                Case 2
                    Console.WriteLine(""loop two"")
                Case Else
                    Console.WriteLine(""loop "" & i)
            End Select
        Next
    Catch ex As Exception
        Console.WriteLine(""caught"")
    End Try
End Sub

Sub Main()
    InTry(7)
    InTry(2)
    InTry(9)
    InCatch(7)
    InCatch(1)
    InFinally(7)
    InFinally(1)
    IfInFinally(7)
    IfInFinally(1)
    LoopInTry()
End Sub
";

    internal const string Expected =
        "try seven\ntry after select\nafter end try\n" +
        "try small\ntry after select\nafter end try\n" +
        "try other\ntry after select\nafter end try\n" +
        "catch seven\ncatch other\n" +
        "body\nfinally seven\nbody\nfinally other\n" +
        "body2\nfinally if seven\nbody2\nfinally if other\n" +
        "loop 1\nloop two\nloop 3";

    [Test]
    public void CSharp_ControlFlowInEveryTryRegion_Compiles()
    {
        var errors = ReturnCoercionTests.CompileEmittedCSharpForTest(Program);
        Assert.That(errors, Is.Empty, string.Join("\n", errors));
    }

    [Test]
    public void CSharp_SelectInsideTry_IsEmittedInsideTheTry()
    {
        var cs = ReturnCoercionTests.EmitCSharpForTest(Program);
        var body = FunctionBody(cs, "public static void InTry(int n)", "\n        }\n");
        var tryAt = body.IndexOf("try", System.StringComparison.Ordinal);
        var switchAt = body.IndexOf("switch (", System.StringComparison.Ordinal);
        var catchAt = body.IndexOf("catch (", System.StringComparison.Ordinal);
        Assert.That(switchAt, Is.GreaterThan(tryAt), "the Select was dropped:\n" + body);
        Assert.That(switchAt, Is.LessThan(catchAt), "the Select is not inside the try:\n" + body);
        Assert.That(body.IndexOf("\"after end try\"", System.StringComparison.Ordinal), Is.GreaterThan(catchAt),
            "code after End Try must not be pulled inside the try:\n" + body);
    }

    [Test]
    public void Cpp_SelectCaseBodies_AreEmittedInsideTheTryBraces()
    {
        var cpp = CppGeneratedCode.WithoutBclRuntime(BclE2E.CompileToCppOptimized(Program));
        var body = FunctionBody(cpp, "void InTry(int32_t n)\n{", "\n}");
        var catchAt = body.IndexOf("catch (", System.StringComparison.Ordinal);
        Assert.That(catchAt, Is.GreaterThan(0), body);
        foreach (var text in new[] { "\"try seven\"", "\"try small\"", "\"try other\"" })
            Assert.That(body.IndexOf(text, System.StringComparison.Ordinal), Is.InRange(0, catchAt),
                $"{text} is emitted outside the try braces — the switch would goto into the try:\n{body}");
    }

    /// <summary>The Finally body is analyzed: a mistake in it is now reported, not compiled.</summary>
    [Test]
    public void FinallyBody_IsSemanticallyAnalyzed()
    {
        var source = "Sub Main()\n    Try\n        Console.WriteLine(\"a\")\n    Finally\n" +
                     "        Console.WriteLine(notDeclaredAnywhere)\n    End Try\nEnd Sub\n";
        var analyzer = new SemanticAnalyzer();
        var ok = analyzer.Analyze(new Parser(new Lexer(source).Tokenize()).Parse());
        Assert.That(ok, Is.False, "an undeclared name inside a Finally compiled successfully");
        Assert.That(analyzer.Errors.Any(e => e.Message.Contains("notDeclaredAnywhere")), Is.True,
            string.Join("; ", analyzer.Errors.Select(e => e.Message)));
    }

    /// <summary>
    /// The text from <paramref name="signature"/> to the first <paramref name="closing"/> after it:
    /// "\n}" for a C++ free function, "\n        }\n" for a C# method (class-member indent).
    /// </summary>
    private static string FunctionBody(string code, string signature, string closing)
    {
        var norm = code.Replace("\r\n", "\n");
        var start = norm.IndexOf(signature, System.StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"no '{signature}' in:\n{norm}");
        var end = norm.IndexOf(closing, start, System.StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(start), $"no end of '{signature}'");
        return norm.Substring(start, end - start);
    }
}

/// <summary>
/// The run half of <see cref="SelectInsideTryTests"/>: the program through the optimizer on C#
/// (in-process Roslyn), C++ and JavaScript. JavaScript was already correct on master and serves
/// as the cross-check; C# printed nothing for the Select and Finally-If arms, and C++ did not
/// compile (then, part-fixed, looped forever in the Finally).
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class SelectInsideTryRunTests
{
    private static string Norm(string s) => FourBackends.Norm(s);

    [Test]
    public void ControlFlowInEveryTryRegion_Runs_CSharp()
        => Assert.That(Norm(FourBackends.RunEmittedCSharp(SelectInsideTryTests.Program)),
            Is.EqualTo(SelectInsideTryTests.Expected));

    [Test]
    public void ControlFlowInEveryTryRegion_Runs_Cpp()
        => Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(SelectInsideTryTests.Program))),
            Is.EqualTo(SelectInsideTryTests.Expected));

    // CompileOptimized, NOT JavaScriptExecutionTests.RunJs — that one runs no optimizer.
    [Test]
    public void ControlFlowInEveryTryRegion_Runs_JavaScript()
        => Assert.That(Norm(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.CompileOptimized(SelectInsideTryTests.Program))),
            Is.EqualTo(SelectInsideTryTests.Expected));
}
