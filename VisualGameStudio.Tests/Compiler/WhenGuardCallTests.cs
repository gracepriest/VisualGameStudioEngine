using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.SemanticAnalysis;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A <c>Select Case</c> <c>When</c> guard is built with emission suppressed and rendered INLINE by
/// every backend. Four defects, all MEASURED on master 72b18a7:
/// <code>
///   C++  When IsBig(n) / b.Over(n) / Len(s) = …   "'t10' was not declared in this scope", after
///        When b.Limit = n / arr(2) = n            "Compilation successful": RenderInline had no
///                                                 arm for a call, a method call, a member read
///                                                 or an element read, so each rendered as the
///                                                 temp its (never-emitted) statement would have
///                                                 assigned.
///   C++  When "k" &amp; n = "k7"                  compiled and silently took Case Else: the
///                                                 guard spelled `&amp;` as a bare `+`, which on a
///                                                 string literal is pointer arithmetic.
///   All  When a AndAlso b  (OrElse too)           a green build that STOPPED at the Select: the
///                                                 short-circuit lowering built blocks with
///                                                 nothing emitted into them and left the builder
///                                                 on the orphan merge block, so every statement
///                                                 after the Select Case was unreachable.
///   All  a Case's patterns were never analyzed    so a guard's calls were untyped, `arr(2)` was
///                                                 built as a CALL to the local (C# CS1955, JS
///                                                 "arr is not a function"), a misspelled name
///                                                 compiled green, and `Case n When n > 0` could
///                                                 not see `n`.
/// </code>
/// The C++ guard now renders each node through its statement form's OWN expression builder
/// (calls, method calls, member reads, element reads, operators, casts), refusing by name what an
/// expression cannot hold. MSIL's inline renderer short-circuits AndAlso/OrElse itself; ilasm is
/// Windows-only, so its run test SKIPS here and the IL shape is pinned instead.
/// </summary>
[TestFixture]
public class WhenGuardCallTests
{
    /// <summary>Calls, nested calls, a method call on a call result, a property getter, a field, a builtin, a keyword static.</summary>
    internal const string CallsProgram = @"
Function IsBig(n As Integer) As Boolean
    Return n > 100
End Function

Function Twice(n As Integer) As Integer
    Return n * 2
End Function

Class Box
    Public Limit As Integer
    Private _scale As Integer
    Public Property Scale As Integer
        Get
            Return _scale
        End Get
        Set(value As Integer)
            _scale = value
        End Set
    End Property
    Public Function Over(n As Integer) As Boolean
        Return n > Limit
    End Function
End Class

Function MakeBox(limit As Integer) As Box
    Dim b As New Box()
    b.Limit = limit
    Return b
End Function

Function Classify(n As Integer, b As Box, s As String) As String
    Select Case n
        Case Is > 0 When IsBig(Twice(n))
            Return ""big""
        Case Is > 0 When b.Over(n) AndAlso n <> 9
            Return ""over""
        Case Is > 0 When MakeBox(1).Over(n)
            Return ""made""
        Case Is > 0 When b.Scale = n
            Return ""scale""
        Case Is > 0 When b.Limit = n
            Return ""limit""
        Case Is < 0 When Len(s) = -n
            Return ""len""
        Case Is < 0 When String.IsNullOrEmpty(s)
            Return ""empty""
        Case Else
            Return ""else""
    End Select
End Function

Sub Main()
    Dim b As New Box()
    b.Limit = 10
    b.Scale = 1
    Console.WriteLine(Classify(60, b, """"))
    Console.WriteLine(Classify(20, b, """"))
    Console.WriteLine(Classify(9, b, """"))
    Console.WriteLine(Classify(1, b, """"))
    b.Limit = 1
    b.Scale = 3
    Console.WriteLine(Classify(1, b, """"))
    Console.WriteLine(Classify(-3, b, ""abc""))
    Console.WriteLine(Classify(-3, b, """"))
    Console.WriteLine(Classify(-3, b, ""x""))
    Console.WriteLine(""done"")
End Sub
";

    internal const string CallsExpected = "big\nover\nmade\nscale\nlimit\nlen\nempty\nelse\ndone";

    /// <summary>
    /// AndAlso/OrElse in a guard: the right side must NOT run when the left decides (a
    /// divide by zero; a side-effecting call), and the statements AFTER the Select must run.
    /// </summary>
    internal const string ShortCircuitProgram = @"
Function Noisy(tag As String) As Boolean
    Console.WriteLine(""ran "" & tag)
    Return True
End Function

Sub Main()
    For Each d As Integer In New Integer() {0, 2, 5}
        Select Case d
            Case Is >= 0 When d <> 0 AndAlso 10 \ d > 3
                Console.WriteLine(""hi "" & d)
            Case Is >= 0 When d = 0 OrElse Noisy(""or"")
                Console.WriteLine(""or "" & d)
            Case Else
                Console.WriteLine(""else "" & d)
        End Select
        Select Case d
            Case Is >= 0 When d > 100 AndAlso Noisy(""and"")
                Console.WriteLine(""never"")
            Case Else
                Console.WriteLine(""skip "" & d)
        End Select
    Next
    Console.WriteLine(""after"")
End Sub
";

    internal const string ShortCircuitExpected = "or 0\nskip 0\nhi 2\nskip 2\nran or\nor 5\nskip 5\nafter";

    /// <summary>Concatenation, a delegate call, ToString, Not, and an array element.</summary>
    internal const string ShapesProgram = @"
Sub Main()
    Dim arr() As Integer = New Integer() {1, 2, 3}
    Dim twice As Func(Of Integer, Integer) = Function(x As Integer) x * 2
    For Each n As Integer In New Integer() {3, 7, 8, 9}
        Select Case n
            Case Is > 0 When ""k"" & n = ""k7""
                Console.WriteLine(""concat "" & n)
            Case Is > 0 When twice(n) = 16
                Console.WriteLine(""lambda "" & n)
            Case Is > 0 When n.ToString() = ""9""
                Console.WriteLine(""tostring "" & n)
            Case Is > 0 When Not (arr(2) <> n)
                Console.WriteLine(""elem "" & n)
            Case Else
                Console.WriteLine(""else "" & n)
        End Select
    Next
End Sub
";

    internal const string ShapesExpected = "elem 3\nconcat 7\nlambda 8\ntostring 9";

    /// <summary>Casts through the statement form's own lowering: CStr of a Double, a Double `\`, a Decimal.</summary>
    internal const string CastProgram = @"
Sub Main()
    Dim price As Decimal = CType(2.5, Decimal)
    For Each y As Double In New Double() {8.0, 9.5, 3.0}
        Select Case y
            Case Is > 0 When CStr(y) = ""9.5""
                Console.WriteLine(""str "" & y)
            Case Is > 0 When y \ 2 = 4
                Console.WriteLine(""intdiv "" & y)
            Case Is > 0 When CType(y, Decimal) > price
                Console.WriteLine(""dec "" & y)
            Case Else
                Console.WriteLine(""else "" & y)
        End Select
    Next
End Sub
";

    internal const string CastExpected = "intdiv 8\nstr 9.5\ndec 3";

    /// <summary>A binding pattern: `n` is the Select subject, visible to its guard and body.</summary>
    internal const string BindingProgram = @"
Sub Main()
    Dim y As Integer = 5
    Select Case y
        Case m When m > 3
            Console.WriteLine(""bound "" & m)
        Case Else
            Console.WriteLine(""unbound"")
    End Select
End Sub
";

    // ---------------------------------------------------------------------------------------

    [Test]
    public void Cpp_RendersEachGuardCall_Inline()
    {
        var cpp = CppGeneratedCode.WithoutBclRuntime(BclE2E.CompileToCppOptimized(CallsProgram));
        Assert.Multiple(() =>
        {
            Assert.That(cpp, Does.Contain("IsBig(Twice(n))"), "a nested call, inline");
            Assert.That(cpp, Does.Contain("b->Over(n)"), "a method call, inline");
            Assert.That(cpp, Does.Contain("MakeBox(1)->Over(n)"), "a method call on a call result, `->` for a class");
            Assert.That(cpp, Does.Contain("b->get_Scale()"), "a Get property, through its getter");
            Assert.That(cpp, Does.Contain("BasicLang::Prim::String_IsNullOrEmpty(s)"), "a keyword static");
        });
    }

    [Test]
    public void Cpp_GuardConcatenation_IsStringConcatenation_NotPointerArithmetic()
    {
        var cpp = CppGeneratedCode.WithoutBclRuntime(BclE2E.CompileToCppOptimized(ShapesProgram));
        Assert.That(cpp, Does.Not.Contain("(\"k\" + n)"), cpp);
    }

    /// <summary>A ByRef argument that is not a variable needs a named local, which an expression has nowhere to put.</summary>
    [Test]
    public void Cpp_GuardCall_WithANonVariableByRefArgument_IsRefusedByName()
    {
        const string program = @"
Function Bump(ByRef x As Integer) As Integer
    x = x + 1
    Return x
End Function
Sub Main()
    Dim n As Integer = 1
    Select Case n
        Case Is > 0 When Bump(n + 1) > 2
            Console.WriteLine(""yes"")
    End Select
End Sub
";
        var ex = Assert.Throws<CppCapabilityException>(() => BclE2E.CompileToCppOptimized(program));
        Assert.That(string.Join("\n", ex!.Diagnostics),
            Does.Contain("'When' guard").And.Contain("Bump"));
    }

    /// <summary>The guard used to be skipped by the analyzer, so an undeclared name compiled.</summary>
    [Test]
    public void Analyzer_ReportsAnUndeclaredName_InAGuard()
    {
        var errors = AnalyzerErrors(@"
Sub Main()
    Dim n As Integer = 1
    Select Case n
        Case Is > 0 When nope > 0
            Console.WriteLine(""yes"")
    End Select
End Sub
");
        Assert.That(errors, Has.Some.Contains("nope"), string.Join("\n", errors));
    }

    [Test]
    public void Analyzer_BindsABindingPattern_ToTheSubjectType()
        => Assert.That(AnalyzerErrors(BindingProgram), Is.Empty);

    /// <summary>
    /// Analyzing the patterns woke a range visitor that had never run, whose "bounds should be
    /// numeric" warning fired on `Case "a" To "z"` — valid VB. Warnings share the Errors list.
    /// </summary>
    [Test]
    public void Analyzer_AcceptsAStringRange_WithoutAWarning()
        => Assert.That(AnalyzerErrors(@"
Sub Main()
    Dim s As String = ""m""
    Select Case s
        Case ""a"" To ""z""
            Console.WriteLine(""letter"")
    End Select
End Sub
"), Is.Empty);

    /// <summary>
    /// AndAlso/OrElse in a guard with no call in it — MSIL refuses a guard CALL (its inline
    /// renderer has no IRCall arm: a clean refusal, the MSIL form of the C++ defect, not fixed here).
    /// </summary>
    internal const string MsilShortCircuitProgram = @"
Sub Main()
    For Each d As Integer In New Integer() {0, 2, 5}
        Select Case d
            Case Is >= 0 When d <> 0 AndAlso 10 \ d > 3
                Console.WriteLine(""hi "" & d)
            Case Is >= 0 When d = 0 OrElse d > 4
                Console.WriteLine(""or "" & d)
            Case Else
                Console.WriteLine(""else "" & d)
        End Select
    Next
    Console.WriteLine(""after"")
End Sub
";

    internal const string MsilShortCircuitExpected = "or 0\nhi 2\nor 5\nafter";

    /// <summary>
    /// The IL of a guard's AndAlso branches past its right operand. The generic binary arm would
    /// load both and `and` them, dividing by zero on `d = 0`.
    /// </summary>
    [Test]
    public void Msil_GuardAndAlso_BranchesPastTheRightOperand()
    {
        var il = MsilHarness.CompileToIl(MsilShortCircuitProgram);
        var division = il.IndexOf("\n    div", System.StringComparison.Ordinal);
        Assert.That(division, Is.GreaterThan(0), il);
        Assert.That(il.Substring(0, division), Does.Contain("brfalse case_test_"),
            "the guard's left operand must branch away before the division is loaded:\n" + il);
    }

    private static string[] AnalyzerErrors(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty);
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        return analyzer.Errors.Select(e => e.Message).ToArray();
    }
}

/// <summary>The guard programs, run on each backend against .NET's own output.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class WhenGuardCallRunTests
{
    private static readonly string[] Programs = { "Calls", "ShortCircuit", "Shapes" };

    private static (string Program, string Expected) Get(string name) => name switch
    {
        "Calls" => (WhenGuardCallTests.CallsProgram, WhenGuardCallTests.CallsExpected),
        "ShortCircuit" => (WhenGuardCallTests.ShortCircuitProgram, WhenGuardCallTests.ShortCircuitExpected),
        "Shapes" => (WhenGuardCallTests.ShapesProgram, WhenGuardCallTests.ShapesExpected),
        _ => throw new System.ArgumentException(name),
    };

    [TestCaseSource(nameof(Programs))]
    public void CSharp_Runs(string name)
    {
        var (program, expected) = Get(name);
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo(expected));
    }

    [TestCaseSource(nameof(Programs))]
    public void Cpp_Runs(string name)
    {
        var (program, expected) = Get(name);
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program))), Is.EqualTo(expected));
    }

    [TestCaseSource(nameof(Programs))]
    public void Cpp_Aggressive_Runs(string name)
    {
        var (program, expected) = Get(name);
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(program))), Is.EqualTo(expected));
    }

    [TestCaseSource(nameof(Programs))]
    public void JavaScript_Runs(string name)
    {
        var (program, expected) = Get(name);
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.Compile(program))), Is.EqualTo(expected));
    }

    [TestCaseSource(nameof(Programs))]
    public void JavaScript_Aggressive_Runs(string name)
    {
        var (program, expected) = Get(name);
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.CompileAggressive(program))), Is.EqualTo(expected));
    }

    /// <summary>Windows-only (ilasm): skips on Linux.</summary>
    [Test]
    public void Msil_ShortCircuit_Runs() =>
        Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(WhenGuardCallTests.MsilShortCircuitProgram)),
            Is.EqualTo(WhenGuardCallTests.MsilShortCircuitExpected));

    /// <summary>The MSIL program's expected text is .NET's own, measured through the C# leg.</summary>
    [Test]
    public void MsilProgram_CSharp_Runs() =>
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(WhenGuardCallTests.MsilShortCircuitProgram)),
            Is.EqualTo(WhenGuardCallTests.MsilShortCircuitExpected));

    /// <summary>JavaScript has no Decimal (BL7004), so the cast program runs on C# and C++.</summary>
    [Test]
    public void Casts_CSharpAndCpp_Run()
    {
        // Not Assert.Multiple: CompileRun's no-compiler Assert.Ignore would FAIL inside one.
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(WhenGuardCallTests.CastProgram)),
            Is.EqualTo(WhenGuardCallTests.CastExpected));
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(WhenGuardCallTests.CastProgram))),
            Is.EqualTo(WhenGuardCallTests.CastExpected));
    }

    /// <summary>C++ still refuses a binding pattern (CppSelectCaseTests); C# and JavaScript run it.</summary>
    [Test]
    public void Binding_CSharpAndJavaScript_Run()
    {
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(WhenGuardCallTests.BindingProgram)), Is.EqualTo("bound 5"));
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.Compile(WhenGuardCallTests.BindingProgram))),
            Is.EqualTo("bound 5"));
    }
}
