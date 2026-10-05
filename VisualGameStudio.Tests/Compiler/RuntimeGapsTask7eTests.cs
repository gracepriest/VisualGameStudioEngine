using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Portable-controls Task 7e: programs that compiled clean and then failed or answered wrong at RUN time, measured by
/// the Task 7c/7d reviewers (probes in scratchpad <c>probes7c\</c>, <c>rv7d\</c>), all on the baseline too. Each row
/// RUNS on C#, JavaScript and C++ (MSIL is out of scope).
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class RuntimeGapsTask7eTests
{
    private static void RunsOnCsJsCpp(string program, string expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo(expected), "C#");
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(program)), Is.EqualTo(expected), "JavaScript");
        });
        // Outside Assert.Multiple: CompileRun IGNORES when there is no C++ compiler.
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program))), Is.EqualTo(expected), "C++");
    }

    /// <summary>⛔ A NESTED class: JavaScript died at load, <c>ReferenceError: OuterInner is not defined</c> (p04_nested).</summary>
    [Test]
    public void ANestedClass_IsConstructedAndCalled() => RunsOnCsJsCpp("""
        Class Outer
            Public Shared Function Sh() As Integer
                Return 100
            End Function
            Public Class Inner
                Private v As Integer = 5
                Public Function Own() As Integer
                    Return v
                End Function
                Public Function Run() As Integer
                    Return Me.Own() + Own() + Outer.Sh()
                End Function
            End Class
        End Class
        Module Program
            Sub Main()
                Dim i As New Outer.Inner()
                Console.WriteLine(i.Run())
            End Sub
        End Module
        """, "110");

    /// <summary>⛔ VB's FUNCTION-NAME return variable — <c>SFact = n * SFact(n - 1)</c>, <c>SumTo = SumTo + i</c> — in a Shared
    /// method, an instance method and a module function (p11_recursion: ReferenceError / wrong result on JavaScript).</summary>
    [Test]
    public void TheFunctionNameReturnVariable_AccumulatesAndRecurses() => RunsOnCsJsCpp("""
        Class C
            Public Shared Function SFact(n As Integer) As Integer
                If n <= 1 Then
                    SFact = 1
                Else
                    SFact = n * SFact(n - 1)
                End If
            End Function
            Public Function SumTo(n As Integer) As Integer
                For i As Integer = 1 To n
                    SumTo = SumTo + i
                Next
            End Function
        End Class
        Module Program
            Function Total(n As Integer) As Integer
                For i As Integer = 1 To n
                    Total = Total + i
                Next
            End Function
            Sub Main()
                Console.WriteLine(C.SFact(4))
                Console.WriteLine(New C().SumTo(4))
                Console.WriteLine(Total(3))
            End Sub
        End Module
        """, "24\n10\n6");

    /// <summary>⛔ <c>Name(0)</c> on a String PROPERTY is VB's default <c>Chars(0)</c> — "a" (p18_prop_paren: C# CS1955,
    /// JavaScript a wrong result); a String LOCAL indexes the same way. ⚠ The property is set in the constructor: an
    /// auto-property INITIALIZER (<c>Property Name As String = "abc"</c>) does not parse — a separate parser gap.</summary>
    [Test]
    public void IndexingAStringProperty_IsItsCharacter() => RunsOnCsJsCpp("""
        Class C
            Private _n As String
            Public Sub New()
                _n = "abc"
            End Sub
            Public Property Name As String
                Get
                    Return _n
                End Get
                Set(v As String)
                    _n = v
                End Set
            End Property
            Public ReadOnly Property Size As Integer
                Get
                    Return 4
                End Get
            End Property
            Public Function Go() As String
                Dim s As String = "xyz"
                Return Name() & Size().ToString() & Name(0) & s(2)
            End Function
        End Class
        Module Program
            Sub Main()
                Console.WriteLine(New C().Go())
            End Sub
        End Module
        """, "abc4az");

    /// <summary>⛔ <c>Exit Function</c> in a Function that never assigns its name returns the type's DEFAULT (VB): 0, and
    /// Nothing for a String (printed as empty). It returned NOTHING at all from a Function before Task 7e.</summary>
    [Test]
    public void ExitFunction_WithoutAnAssignment_ReturnsTheDefault() => RunsOnCsJsCpp("""
        Module Program
            Function F(b As Boolean) As Integer
                If b Then Exit Function
                Return 7
            End Function
            Function G(b As Boolean) As String
                If b Then Exit Function
                Return "seven"
            End Function
            Sub Main()
                Console.WriteLine(F(True))
                Console.WriteLine(F(False))
                Console.WriteLine("[" & G(True) & "]")
                Console.WriteLine(G(False))
            End Sub
        End Module
        """, "0\n7\n[]\nseven");

    /// <summary>⛔ <c>p.Name(1)</c> — a String property reached THROUGH A MEMBER — is Chars(1) too; it was still emitted as a
    /// call (C# CS1955, JavaScript TypeError). Piece 2's controls (<c>txt.Text(0)</c>) need it.</summary>
    [Test]
    public void IndexingAStringMember_ThroughAReceiver_IsItsCharacter() => RunsOnCsJsCpp("""
        Class P
            Public Name As String
            Private _t As String
            Public Sub New()
                Name = "abc"
                _t = "xyz"
            End Sub
            Public Property Text As String
                Get
                    Return _t
                End Get
                Set(v As String)
                    _t = v
                End Set
            End Property
        End Class
        Module Program
            Sub Main()
                Dim p As New P()
                Console.WriteLine(p.Name(1) & p.Text(0))
            End Sub
        End Module
        """, "bx");

    /// <summary>⛔ <c>s(i)</c> past the end throws, as VB's Chars does (C# threw; JavaScript answered an empty string).</summary>
    [Test]
    public void IndexingAStringPastItsEnd_Throws_OnJavaScript()
    {
        var outcome = JavaScriptExecutionTests.RunNodeScriptForOutcome(JsTestSupport.Compile("""
            Module Program
                Sub Main()
                    Dim s As String = "ab"
                    Console.WriteLine(s(5))
                End Sub
            End Module
            """));
        Assert.That(outcome.ExitCode, Is.Not.Zero, "an out-of-range Chars index must throw, not print an empty line");
    }

    /// <summary>
    /// The same out-of-range index throws on EVERY backend, and the throw is an ordinary catchable exception — C#'s
    /// string indexer, JavaScript's checked index, C++'s <c>.at()</c> (review of e2502c0b: only JavaScript was tested).
    /// A plain member read past the end too, through a receiver.
    /// </summary>
    [Test]
    public void IndexingAStringPastItsEnd_ThrowsACatchableException_OnEveryBackend() => RunsOnCsJsCpp("""
        Class P
            Public Name As String = "ab"
        End Class
        Module Program
            Sub Main()
                Dim s As String = "ab"
                Try
                    Console.WriteLine(s(5))
                    Console.WriteLine("no throw")
                Catch ex As Exception
                    Console.WriteLine("threw")
                End Try
                Dim p As New P()
                Try
                    Console.WriteLine(p.Name(-1))
                    Console.WriteLine("no throw")
                Catch ex As Exception
                    Console.WriteLine("threw")
                End Try
            End Sub
        End Module
        """, "threw\nthrew");

    /// <summary>⛔ C++ emitted bare <c>Beep()</c> / <c>FileCopy(…)</c> / <c>FileLen(…)</c> calls to names nothing defines — a late
    /// native-compile failure. Run on C++ and C# (JavaScript has no file system: those are refused there by design).</summary>
    [Test]
    public void BeepFileCopyAndFileLen_RunOnCppAndCSharp()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "bl7e-" + Path.GetRandomFileName())).FullName;
        try
        {
            var source = Path.Combine(dir, "src.txt");
            var copy = Path.Combine(dir, "copy.txt");
            File.WriteAllText(source, "hello");
            var program = $"Module Program\n Sub Main()\n  Beep()\n  FileCopy(\"{source}\", \"{copy}\")\n  Console.WriteLine(FileLen(\"{copy}\"))\n End Sub\nEnd Module\n";
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo("5"), "C#");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program))), Is.EqualTo("5"), "C++");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>⛔ C++ lowered <c>Left("hello", 2)</c> to <c>"hello".substr(0, 2)</c> — a const char[] has no substr.</summary>
    [Test]
    public void StringBuiltIns_OnALiteral() => RunsOnCsJsCpp("""
        Module Program
            Sub Main()
                Console.WriteLine(Left("hello", 2))
                Console.WriteLine(Right("hello", 3))
                Console.WriteLine(Mid("hello", 2, 2))
                Console.WriteLine(Len("hello"))
                Console.WriteLine(InStr("hello", "l"))
            End Sub
        End Module
        """, "he\nllo\nel\n5\n3");
}
