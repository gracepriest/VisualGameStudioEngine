using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>Exit For</c> / <c>Exit Do</c> / <c>Exit While</c> inside a <c>Try</c> or <c>Catch</c> on the
/// C++ backend.
///
/// <para><b>MEASURED on master</b>: "jump to label 'for0_end'" — the program did not compile.
/// <c>ComputeInlineRegion</c> follows every branch out of the try body, so the Exit's target — the
/// ENCLOSING loop's end block, and everything after the loop including its <c>return</c> — was
/// pulled INSIDE the try braces, and the loop's normal exit then jumped into the try block. A block
/// created before the region's entry now belongs to the enclosing construct and stays out.</para>
///
/// <para>Two defects surfaced once it compiled, both fixed here and pinned below: the §11.1
/// ladder's copy of a Catch body named the exit target with its <c>_nex</c> suffix ("label used but
/// not defined"), and a goto out of a Try WITH A FINALLY skipped the Finally — a goto runs no
/// handler, so the exit now carries its own copy of every Finally it leaves, innermost first.</para>
/// </summary>
[TestFixture]
public class ExitInsideTryTests
{
    internal const string Program = @"
Sub ExitInTry()
    For i As Integer = 1 To 5
        Try
            Console.WriteLine(""try "" & i)
            Exit For
        Catch ex As Exception
            Console.WriteLine(""caught"")
        End Try
    Next
    Console.WriteLine(""after loop 1"")
End Sub

Sub ExitInCatch()
    For i As Integer = 1 To 5
        Try
            Throw New InvalidOperationException(""x"")
        Catch ex As InvalidOperationException
            Console.WriteLine(""catch "" & i)
            Exit For
        End Try
    Next
    Console.WriteLine(""after loop 2"")
End Sub

Sub ExitDoInTry()
    Dim i As Integer = 0
    Do While i < 5
        i = i + 1
        Try
            Console.WriteLine(""do "" & i)
            Exit Do
        Catch ex As Exception
            Console.WriteLine(""caught"")
        End Try
    Loop
    Console.WriteLine(""after do"")
End Sub

Sub ExitWhileInCatch()
    Dim i As Integer = 0
    While i < 5
        i = i + 1
        Try
            Throw New InvalidOperationException(""x"")
        Catch ex As InvalidOperationException
            Console.WriteLine(""while catch "" & i)
            Exit While
        End Try
    End While
    Console.WriteLine(""after while"")
End Sub

Sub LoopInsideTry()
    Try
        For i As Integer = 1 To 5
            If i = 3 Then Exit For
            Console.WriteLine(""inner "" & i)
        Next
        Console.WriteLine(""still in try"")
    Catch ex As Exception
        Console.WriteLine(""caught"")
    End Try
    Console.WriteLine(""after try"")
End Sub

Sub ExitWithFinally()
    For i As Integer = 1 To 5
        Try
            Console.WriteLine(""body "" & i)
            If i = 2 Then Exit For
        Finally
            Console.WriteLine(""finally "" & i)
        End Try
    Next
    Console.WriteLine(""after loop f"")
End Sub

Sub LeavesBoth()
    For i As Integer = 1 To 3
        Try
            Try
                Console.WriteLine(""inner body "" & i)
                Exit For
            Finally
                Console.WriteLine(""inner finally"")
            End Try
        Finally
            Console.WriteLine(""outer finally"")
        End Try
    Next
    Console.WriteLine(""after both"")
End Sub

Sub LeavesInnerOnly()
    Try
        For i As Integer = 1 To 3
            Try
                Console.WriteLine(""body "" & i)
                If i = 2 Then Exit For
            Finally
                Console.WriteLine(""inner finally "" & i)
            End Try
        Next
        Console.WriteLine(""still in outer try"")
    Finally
        Console.WriteLine(""outer finally"")
    End Try
End Sub

Sub ExitFromCatchWithFinally()
    For i As Integer = 1 To 3
        Try
            Throw New InvalidOperationException(""x"")
        Catch ex As InvalidOperationException
            Console.WriteLine(""catch "" & i)
            Exit For
        Finally
            Console.WriteLine(""finally after catch"")
        End Try
    Next
    Console.WriteLine(""after catch loop"")
End Sub

Sub FinallyWithSelect(n As Integer)
    For i As Integer = 1 To 3
        Try
            Console.WriteLine(""fs body "" & i)
            Exit For
        Finally
            Select Case n
                Case 7
                    Console.WriteLine(""fin seven"")
                Case Else
                    Console.WriteLine(""fin other"")
            End Select
        End Try
    Next
    Console.WriteLine(""after fs"")
End Sub

Sub Main()
    ExitInTry()
    ExitInCatch()
    ExitDoInTry()
    ExitWhileInCatch()
    LoopInsideTry()
    ExitWithFinally()
    LeavesBoth()
    LeavesInnerOnly()
    ExitFromCatchWithFinally()
    FinallyWithSelect(7)
    FinallyWithSelect(1)
End Sub
";

    internal const string Expected =
        "try 1\nafter loop 1\n" +
        "catch 1\nafter loop 2\n" +
        "do 1\nafter do\n" +
        "while catch 1\nafter while\n" +
        "inner 1\ninner 2\nstill in try\nafter try\n" +
        "body 1\nfinally 1\nbody 2\nfinally 2\nafter loop f\n" +
        "inner body 1\ninner finally\nouter finally\nafter both\n" +
        "body 1\ninner finally 1\nbody 2\ninner finally 2\nstill in outer try\nouter finally\n" +
        "catch 1\nfinally after catch\nafter catch loop\n" +
        "fs body 1\nfin seven\nafter fs\n" +
        "fs body 1\nfin other\nafter fs";

    /// <summary>The code after the loop must not be emitted inside the try braces.</summary>
    [Test]
    public void Cpp_ExitTarget_IsNotPulledInsideTheTry()
    {
        var cpp = CppGeneratedCode.WithoutBclRuntime(BclE2E.CompileToCppOptimized(Program)).Replace("\r\n", "\n");
        var start = cpp.IndexOf("void ExitInTry()\n{", System.StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        var body = cpp.Substring(start, cpp.IndexOf("\n}", start, System.StringComparison.Ordinal) - start);
        var firstCatch = body.IndexOf("catch (", System.StringComparison.Ordinal);
        var afterLoop = body.IndexOf("\"after loop 1\"", System.StringComparison.Ordinal);
        Assert.That(firstCatch, Is.GreaterThan(0), body);
        Assert.That(afterLoop, Is.GreaterThan(firstCatch),
            "the enclosing loop's end block was pulled inside the try braces:\n" + body);
    }
}

/// <summary>The run half: C# (real .NET), C++ and JavaScript, all through the optimizer.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class ExitInsideTryRunTests
{
    private static string Norm(string s) => FourBackends.Norm(s);

    [Test]
    public void ExitInsideTry_CSharpReference()
        => Assert.That(Norm(FourBackends.RunEmittedCSharp(ExitInsideTryTests.Program)), Is.EqualTo(ExitInsideTryTests.Expected));

    [Test]
    public void ExitInsideTry_Cpp()
        => Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(ExitInsideTryTests.Program))),
            Is.EqualTo(ExitInsideTryTests.Expected),
            "a missing 'finally' line means an exit skipped the Finally it left");

    // CompileOptimized, NOT JavaScriptExecutionTests.RunJs — that one runs no optimizer.
    [Test]
    public void ExitInsideTry_JavaScript()
        => Assert.That(Norm(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.CompileOptimized(ExitInsideTryTests.Program))),
            Is.EqualTo(ExitInsideTryTests.Expected));
}
