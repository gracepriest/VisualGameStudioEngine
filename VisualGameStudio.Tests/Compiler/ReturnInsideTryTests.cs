using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A <c>Return</c> inside a <c>Try</c> or <c>Catch</c> must run the Finally it leaves, on the C++
/// backend as in .NET.
///
/// <para><b>MEASURED on master</b> (<c>c30d52e5</c>): <c>Try : If n = 1 Then Return 10 : Finally :
/// Print "finally"</c> returned 10 and printed nothing — the Finally was SKIPPED, with a green build.
/// C# and JavaScript were right. A C++ <c>return</c> out of a try block runs no handler, and the
/// Finally was copied only onto the normal and the exceptional exits. A Return now carries a copy
/// of every Finally it leaves, innermost first — the mechanism a goto out of a Try already uses
/// (ExitInsideTryTests) — and takes its value BEFORE they run.</para>
/// </summary>
[TestFixture]
public class ReturnInsideTryTests
{
    internal const string Program = @"
Function InBody(n As Integer) As Integer
    Try
        If n = 1 Then
            Return 10
        End If
        Console.WriteLine(""body tail"")
    Finally
        Console.WriteLine(""finally "" & n)
    End Try
    Return 20
End Function

Sub SubReturn(n As Integer)
    Try
        Console.WriteLine(""sub body"")
        If n = 1 Then Return
        Console.WriteLine(""sub tail"")
    Finally
        Console.WriteLine(""sub finally"")
    End Try
    Console.WriteLine(""after sub try"")
End Sub

Function InCatch() As String
    Try
        Throw New InvalidOperationException(""boom"")
    Catch ex As InvalidOperationException
        Console.WriteLine(""caught "" & ex.Message)
        Return ""from catch""
    Finally
        Console.WriteLine(""catch finally"")
    End Try
    Return ""not reached""
End Function

Function Nested() As Integer
    Try
        Try
            Console.WriteLine(""inner body"")
            Return 7
        Finally
            Console.WriteLine(""inner finally"")
        End Try
    Finally
        Console.WriteLine(""outer finally"")
    End Try
    Return 0
End Function

Function Snapshot() As Integer
    Dim x As Integer = 1
    Try
        Return x
    Finally
        x = 99
        Console.WriteLine(""finally set "" & x)
    End Try
End Function

Function InLoop() As Integer
    Try
        For i As Integer = 1 To 5
            If i = 3 Then
                Return i
            End If
            Console.WriteLine(""loop "" & i)
        Next
    Finally
        Console.WriteLine(""loop finally"")
    End Try
    Return -1
End Function

Function InSelect(n As Integer) As String
    Try
        Select Case n
            Case 1
                Return ""one""
            Case Else
                Console.WriteLine(""select other"")
        End Select
    Finally
        Console.WriteLine(""select finally"")
    End Try
    Return ""after""
End Function

Function FinallyWithIf(n As Integer) As Integer
    Try
        Return n * 2
    Finally
        If n > 5 Then
            Console.WriteLine(""fin big"")
        Else
            Console.WriteLine(""fin small"")
        End If
    End Try
End Function

Function NoFinally(n As Integer) As Integer
    Try
        If n = 1 Then Return 100
    Catch ex As Exception
        Console.WriteLine(""caught"")
    End Try
    Return 200
End Function

Class Box
    Public Function Take() As Integer
        Try
            Return 5
        Finally
            Console.WriteLine(""method finally"")
        End Try
    End Function
End Class

Sub Main()
    Console.WriteLine(InBody(1))
    Console.WriteLine(InBody(2))
    SubReturn(1)
    SubReturn(2)
    Console.WriteLine(InCatch())
    Console.WriteLine(Nested())
    Console.WriteLine(Snapshot())
    Console.WriteLine(InLoop())
    Console.WriteLine(InSelect(1))
    Console.WriteLine(InSelect(2))
    Console.WriteLine(FinallyWithIf(9))
    Console.WriteLine(FinallyWithIf(1))
    Console.WriteLine(NoFinally(1))
    Console.WriteLine(NoFinally(2))
    Dim b As New Box()
    Console.WriteLine(b.Take())
End Sub
";

    internal const string Expected =
        "finally 1\n10\nbody tail\nfinally 2\n20\n" +
        "sub body\nsub finally\nsub body\nsub tail\nsub finally\nafter sub try\n" +
        "caught boom\ncatch finally\nfrom catch\n" +
        "inner body\ninner finally\nouter finally\n7\n" +
        "finally set 99\n1\n" +
        "loop 1\nloop 2\nloop finally\n3\n" +
        "select finally\none\nselect other\nselect finally\nafter\n" +
        "fin big\n18\nfin small\n2\n" +
        "100\n200\n" +
        "method finally\n5";

    /// <summary>Every return out of a Try with a Finally follows a copy of that Finally.</summary>
    [TestCase("int32_t InBody(", "\"finally \"")]
    [TestCase("void SubReturn(", "\"sub finally\"")]
    [TestCase("InCatch(", "\"catch finally\"")]
    [TestCase("int32_t Nested(", "\"outer finally\"")]
    [TestCase("int32_t InLoop(", "\"loop finally\"")]
    [TestCase("InSelect(", "\"select finally\"")]
    public void Cpp_EarlyReturn_IsPrecededByItsFinally(string header, string finallyLiteral)
    {
        var body = FunctionBody(header);
        var firstReturn = System.Text.RegularExpressions.Regex.Match(body, @"\breturn[ ;]").Index;
        Assert.That(firstReturn, Is.GreaterThan(0), body);
        var finallyBefore = body.LastIndexOf(finallyLiteral, firstReturn, System.StringComparison.Ordinal);
        Assert.That(finallyBefore, Is.GreaterThan(0),
            $"the first return in {header} runs no copy of its Finally:\n{body}");
    }

    /// <summary>The returned value is taken before the Finally runs — it may reassign it.</summary>
    [Test]
    public void Cpp_ReturnValue_IsTakenBeforeTheFinally()
    {
        var body = FunctionBody("int32_t Snapshot(");
        var saved = body.IndexOf("auto __blReturn", System.StringComparison.Ordinal);
        var assign = body.IndexOf("= 99", System.StringComparison.Ordinal);
        Assert.That(saved, Is.GreaterThan(0), body);
        Assert.That(assign, Is.GreaterThan(saved), "the value must be saved before the Finally assigns it:\n" + body);
    }

    /// <summary>A Try without a Finally keeps the plain return.</summary>
    [Test]
    public void Cpp_ReturnWithoutFinally_IsUnchanged()
        => Assert.That(FunctionBody("int32_t NoFinally("), Does.Not.Contain("__blReturn"));

    private static string FunctionBody(string header)
    {
        var cpp = CppGeneratedCode.WithoutBclRuntime(BclE2E.CompileToCppOptimized(Program)).Replace("\r\n", "\n");
        // Skip forward declarations (a line ending in ';'): the definition is the one with a body.
        var start = cpp.IndexOf(header, System.StringComparison.Ordinal);
        while (start >= 0 && cpp.Substring(start, cpp.IndexOf('\n', start) - start).TrimEnd().EndsWith(";"))
            start = cpp.IndexOf(header, start + 1, System.StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"no {header} in the emitted C++:\n{cpp}");
        var end = cpp.IndexOf("\n}\n", start, System.StringComparison.Ordinal);
        return cpp.Substring(start, end - start);
    }
}

/// <summary>The run half: C# (real .NET), C++ and JavaScript, all through the optimizer.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class ReturnInsideTryRunTests
{
    private static string Norm(string s) => FourBackends.Norm(s);

    [Test]
    public void ReturnInsideTry_CSharpReference()
        => Assert.That(Norm(FourBackends.RunEmittedCSharp(ReturnInsideTryTests.Program)),
            Is.EqualTo(ReturnInsideTryTests.Expected));

    [Test]
    public void ReturnInsideTry_Cpp()
        => Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(ReturnInsideTryTests.Program))),
            Is.EqualTo(ReturnInsideTryTests.Expected),
            "a missing 'finally' line means a Return skipped the Finally it left");

    // CompileOptimized, NOT JavaScriptExecutionTests.RunJs — that one runs no optimizer.
    [Test]
    public void ReturnInsideTry_JavaScript()
        => Assert.That(Norm(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.CompileOptimized(ReturnInsideTryTests.Program))),
            Is.EqualTo(ReturnInsideTryTests.Expected));
}
