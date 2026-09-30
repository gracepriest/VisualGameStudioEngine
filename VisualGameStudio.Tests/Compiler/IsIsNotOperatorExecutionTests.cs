using System.Linq;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using NUnit.Framework;
using VisualGameStudio.Tests.Native;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task #185 (fix commit <c>ffed9fc1</c>) — <c>Is</c> / <c>IsNot</c> RUNS correctly on all four
/// backends, on both the standard and aggressive optimizer pipelines. The architect's ruling is
/// ADR-0011 (<c>docs/superpowers/decisions/0011-is-isnot-reference-identity.md</c>). Expected
/// strings are taken verbatim from the implementer's measured probes (<c>S/t185/probes/*.exp</c>),
/// themselves the VB/C# answer — never from MSIL, which can diverge on its own.
///
/// <para>Front end (parse/refuse) and pure IR-level assertions are
/// <see cref="IsIsNotOperatorTests"/> (fast subset). This fixture is RUN-level only.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // several legs redirect Console.Out
public class IsIsNotOperatorExecutionTests
{
    // ============================================================================================
    // 1. The kind table (ruling's brief) + two-operand identity: assign Nothing and test, then set
    //    and test. Every row here measured RAN OK on all four backends, both pipelines
    //    (matrix-after.txt). [TestCase] source = probe file, [TestCase] label = probe name.
    // ============================================================================================

    // P01: parse positions, combined into one program (If/IsNot/Dim/argument/AndAlso/base call).
    private const string P01_Parse = """
        Public Class Foo
            Public V As Integer
        End Class

        Function IsMissing(x As Foo) As Boolean
            Return x Is Nothing
        End Function

        Function IsPresent(x As Foo, y As Foo) As Boolean
            Return x IsNot y
        End Function

        Sub Main()
            Dim a As Foo = Nothing
            If a Is Nothing Then
                Console.WriteLine("a1")
            End If
            If a IsNot Nothing Then
                Console.WriteLine("a2")
            End If
            Dim b = a Is Nothing
            Console.WriteLine(b)
            Dim c As Boolean = a IsNot Nothing
            Console.WriteLine(c)
            Console.WriteLine(IsMissing(a))
            Console.WriteLine(a Is Nothing)
            a = New Foo()
            a.V = 5
            If a IsNot Nothing AndAlso a.V > 0 Then
                Console.WriteLine("a3")
            End If
            Dim d As Foo = a
            Dim e As Boolean
            e = a Is d
            Console.WriteLine(e)
            e = a IsNot d
            Console.WriteLine(e)
            Console.WriteLine(IsMissing(a))
            Console.WriteLine(IsPresent(a, d))
            Console.WriteLine(IsPresent(a, New Foo()))
        End Sub
        """;
    private const string P01_Exp = "a1\nTrue\nFalse\nTrue\nTrue\na3\nTrue\nFalse\nFalse\nFalse\nTrue";

    private const string ClassInterfaceKinds = """
        Public Class Foo
            Public V As Integer
        End Class
        Public Interface IThing
            Sub Go()
        End Interface
        Public Class Thing
            Implements IThing
            Public Sub Go() Implements IThing.Go
            End Sub
        End Class
        """;

    private static string KindProbe(string decl, string typeName, string newExpr) => $"""
        {decl}
        Sub Main()
            Dim x As {typeName} = Nothing
            Console.WriteLine(x Is Nothing)
            Console.WriteLine(x IsNot Nothing)
            Console.WriteLine(Nothing Is x)
            x = {newExpr}
            Console.WriteLine(x Is Nothing)
            Console.WriteLine(x IsNot Nothing)
            Console.WriteLine(Nothing IsNot x)
        End Sub
        """;
    private const string KindExp = "True\nFalse\nTrue\nFalse\nTrue\nTrue";

    private const string P10_Two = """
        Public Class Base
            Public V As Integer
        End Class
        Public Class Derived
            Inherits Base
        End Class
        Public Interface IThing
            Sub Go()
        End Interface
        Public Class Thing
            Implements IThing
            Public Sub Go() Implements IThing.Go
            End Sub
        End Class

        Sub Main()
            Dim a As Derived = New Derived()
            Dim b As Derived = New Derived()
            Console.WriteLine(a Is a)
            Console.WriteLine(a Is b)
            Dim c As Derived = a
            Console.WriteLine(a Is c)
            Dim bs As Base = a
            Console.WriteLine(bs Is a)
            Console.WriteLine(a Is bs)
            Console.WriteLine(bs Is b)
            Dim t As Thing = New Thing()
            Dim it As IThing = t
            Console.WriteLine(it Is t)
            Console.WriteLine(t IsNot it)
            Dim t2 As Thing = New Thing()
            Console.WriteLine(it Is t2)
            Console.WriteLine(a IsNot b)
            Console.WriteLine(a IsNot c)
            Console.WriteLine(bs IsNot b)
        End Sub
        """;
    private const string P10_Exp = "True\nFalse\nTrue\nTrue\nTrue\nFalse\nTrue\nFalse\nFalse\nTrue\nFalse\nTrue";

    private const string E2_Guard = """
        Public Class Foo
        End Class

        Sub Classify(n As Integer, f As Foo)
            Select Case n
                Case Is > 0 When f Is Nothing
                    Console.WriteLine("pos-null")
                Case Is > 0 When f IsNot Nothing
                    Console.WriteLine("pos-set")
                Case Else
                    Console.WriteLine("other")
            End Select
        End Sub

        Sub Main()
            Classify(1, Nothing)
            Classify(1, New Foo())
            Classify(0, Nothing)
        End Sub
        """;
    private const string E2_Exp = "pos-null\npos-set\nother";

    private const string E4_Loop = """
        Public Class Foo
            Public V As Integer
        End Class

        Sub Main()
            Dim a As Foo = New Foo()
            Dim b As Foo = Nothing
            Dim hits As Integer = 0
            For i As Integer = 1 To 5
                If a Is b Then
                    hits = hits + 100
                End If
                If b Is Nothing Then
                    hits = hits + 1
                End If
                If i = 3 Then
                    b = a
                End If
            Next
            Console.WriteLine(hits)
            Dim c As Foo = a
            Dim r1 As Boolean = a Is c
            c = New Foo()
            Dim r2 As Boolean = a Is c
            Console.WriteLine(r1)
            Console.WriteLine(r2)
        End Sub
        """;
    private const string E4_Exp = "203\nTrue\nFalse";

    private const string E6_Collections = """
        Sub Main()
            Dim l1 As List(Of Integer) = New List(Of Integer)()
            Dim l2 As List(Of Integer) = New List(Of Integer)()
            Dim l3 As List(Of Integer) = l1
            Console.WriteLine(l1 Is l2)
            Console.WriteLine(l1 Is l3)
            Dim a1 As Integer() = New Integer() {1}
            Dim a2 As Integer() = New Integer() {1}
            Dim a3 As Integer() = a1
            Console.WriteLine(a1 Is a2)
            Console.WriteLine(a1 Is a3)
            Console.WriteLine(a1 IsNot a2)
        End Sub
        """;
    private const string E6_Exp = "False\nTrue\nFalse\nTrue\nTrue";

    private const string E9_Casing = """
        Public Class Foo
        End Class

        Sub Main()
            Dim f As Foo = Nothing
            Console.WriteLine(f is Nothing)
            Console.WriteLine(f ISNOT Nothing)
            f = New Foo()
            Console.WriteLine(f isnot nothing)
        End Sub
        """;
    private const string E9_Exp = "True\nFalse\nTrue";

    [TestCase(P01_Parse, P01_Exp, "P01 parse positions")]
    [TestCase(P10_Two, P10_Exp, "P10 two-operand: same/distinct/alias/base-derived/interface")]
    [TestCase(E2_Guard, E2_Exp, "E2 When-guard identity")]
    [TestCase(E4_Loop, E4_Exp, "E4 identity inside a loop")]
    [TestCase(E6_Collections, E6_Exp, "E6 List(Of T) and array identity")]
    [TestCase(E9_Casing, E9_Exp, "E9 case-insensitive Is/IsNot")]
    public void Probe_RunsOnEveryBackend_BothPipelines(string source, string expected, string label)
    {
        TestContext.Out.WriteLine($"[{label}] standard pipeline");
        FourBackends.RunsOnEveryBackend(source, expected);
        TestContext.Out.WriteLine($"[{label}] aggressive pipeline");
        FourBackends.RunsOnEveryBackendAggressive(source, expected);
    }

    private static readonly string[][] Kinds =
    {
        new[] { ClassInterfaceKinds, "Foo", "New Foo()", "class (P02)" },
        new[] { ClassInterfaceKinds, "IThing", "New Thing()", "interface (P03)" },
        new[] { "", "List(Of Integer)", "New List(Of Integer)()", "List(Of T) (P04)" },
        new[] { "", "Action", "Sub() Console.Write(\"\")", "Action (P05)" },
        new[] { "", "Func(Of Integer)", "Function() 1", "Func(Of T) (P06)" },
        new[] { "", "String", "\"hi\"", "String (P07)" },
        new[] { "", "Integer()", "New Integer() {1, 2}", "array (P08)" },
    };

    [TestCaseSource(nameof(KindCases))]
    public void Kind_AssignNothingThenSet_RunsOnEveryBackend_BothPipelines(string decl, string typeName, string newExpr, string label)
    {
        var source = KindProbe(decl, typeName, newExpr);
        TestContext.Out.WriteLine($"[{label}] standard pipeline");
        FourBackends.RunsOnEveryBackend(source, KindExp);
        TestContext.Out.WriteLine($"[{label}] aggressive pipeline");
        FourBackends.RunsOnEveryBackendAggressive(source, KindExp);
    }

    private static System.Collections.Generic.IEnumerable<TestCaseData> KindCases() =>
        Kinds.Select(k => new TestCaseData(k[0], k[1], k[2], k[3]));

    /// <summary>
    /// P09 (Object): the ONE kind-table row that does NOT run everywhere — <c>Object</c> has no
    /// C++ mapping at all, a PRE-EXISTING refusal (measured before this task: E8_object.bas fails
    /// identically). Pinned so a future fix is a deliberate, noticed change, not a silent one.
    /// </summary>
    [Test]
    public void P09_Object_Cpp_PinsTodaysPreExistingRefusal()
    {
        var source = KindProbe(ClassInterfaceKinds, "Object", "New Foo()");
        // CppCodeGenerator.Generate throws CppCapabilityException directly (code GENERATION never
        // reaches a compiler) — not the AssertionException a native-compile/run failure raises.
        var ex = Assert.Throws<CppCapabilityException>(() => BclE2E.CompileToCppOptimized(source));
        Assert.That(ex!.Message, Does.Contain("Object").And.Contain("no C++ mapping"),
            "the compile failure must still be Object's pre-existing 'no C++ mapping' refusal, " +
            "unrelated to Is/IsNot. A DIFFERENT failure here means this pin is stale.\n" + ex.Message);
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(source)), Is.EqualTo(KindExp), "C#");
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(source)), Is.EqualTo(KindExp), "JavaScript");
            Assert.That(FourBackends.Norm(Msil.MsilHarness.RunExpectingSuccess(source)), Is.EqualTo(KindExp), "MSIL");
        });
    }

    // ============================================================================================
    // 2. #189, now CLOSED: `Case Is Nothing` on a C++ String and on an array compile and print the
    //    .exp — the two rows that used to fail to compile on C++ (`x == nullptr` on a std::string /
    //    BasicLang::Array<T>). Probes C1, C2, and E7 (`Case Is Nothing` inside an Or pattern).
    // ============================================================================================

    private const string C1_CaseString = """
        Sub Check(label As String, x As String)
            Select Case x
                Case Is Nothing
                    Console.WriteLine(label & ":null")
                Case Else
                    Console.WriteLine(label & ":set")
            End Select
        End Sub

        Sub Main()
            Dim x As String = Nothing
            Check("a", x)
            x = "hi"
            Check("b", x)
        End Sub
        """;

    private const string C2_CaseArray = """
        Sub Check(label As String, x As Integer())
            Select Case x
                Case Is Nothing
                    Console.WriteLine(label & ":null")
                Case Else
                    Console.WriteLine(label & ":set")
            End Select
        End Sub

        Sub Main()
            Dim x As Integer() = Nothing
            Check("a", x)
            x = New Integer() {1, 2}
            Check("b", x)
        End Sub
        """;
    private const string CaseIsNothing_Exp = "a:null\nb:set";

    private const string E7_CaseOr = """
        Public Class Foo
        End Class

        Sub Check(s As String)
            Select Case s
                Case "x", Is Nothing
                    Console.WriteLine("x-or-null")
                Case Else
                    Console.WriteLine("other")
            End Select
        End Sub

        Sub Main()
            Check(Nothing)
            Check("x")
            Check("y")
        End Sub
        """;
    private const string E7_Exp = "x-or-null\nx-or-null\nother";

    [TestCase(C1_CaseString, CaseIsNothing_Exp, "C1 Case Is Nothing on a C++ String — #189, now closed")]
    [TestCase(C2_CaseArray, CaseIsNothing_Exp, "C2 Case Is Nothing on a C++ array — #189, now closed")]
    [TestCase(E7_CaseOr, E7_Exp, "E7 Case Is Nothing inside an Or pattern")]
    public void CaseIsNothing_189_RunsOnEveryBackend_BothPipelines(string source, string expected, string label)
    {
        TestContext.Out.WriteLine($"[{label}] standard pipeline");
        FourBackends.RunsOnEveryBackend(source, expected);
        TestContext.Out.WriteLine($"[{label}] aggressive pipeline");
        FourBackends.RunsOnEveryBackendAggressive(source, expected);
    }

    /// <summary>
    /// D3 (1)'s own text, directly: the SAME <c>EmitNullTest</c> helper answers both sites, keyed
    /// on the mapped spelling — a String/array `Case Is Nothing` must emit <c>.empty()</c>, never
    /// the bare <c>== nullptr</c> that failed to compile before #185.
    /// </summary>
    [Test]
    public void CaseIsNothing_Cpp_OnStringAndArray_EmitsEmptinessTest_NeverBareNullptr()
    {
        // Strip the spliced BCL runtime FIRST — it is full of its own unrelated `.empty()` calls
        // (String/List helpers), so a bare Does.Contain(".empty()") against the whole translation
        // unit passes vacuously even when the Case Is Nothing arm itself still emits `== nullptr`.
        var cppString = CppGeneratedCode.WithoutBclRuntime(BclE2E.CompileToCppOptimized(C1_CaseString));
        var cppArray = CppGeneratedCode.WithoutBclRuntime(BclE2E.CompileToCppOptimized(C2_CaseArray));
        Assert.Multiple(() =>
        {
            Assert.That(cppString, Does.Contain("(x).empty()"), "String Case Is Nothing must test emptiness\n" + cppString);
            Assert.That(cppString, Does.Not.Contain("x == nullptr"), "\n" + cppString);
            Assert.That(cppArray, Does.Contain("(x).empty()"), "array Case Is Nothing must test emptiness\n" + cppArray);
            Assert.That(cppArray, Does.Not.Contain("x == nullptr"), "\n" + cppArray);
        });
    }

    // ============================================================================================
    // 3. D3 (3) — the NAMED C++ divergence: "" Is Nothing / an empty array Is Nothing are True on
    //    C++ ONLY (no null state for either there), False everywhere else. Flips for arrays when
    //    #196 (Array<T> gets a real null state) lands — see ADR-0011's "Revisit if".
    // ============================================================================================

    private const string P12_Divergence = """
        Sub Main()
            Dim s As String = ""
            Console.WriteLine(s Is Nothing)
            Dim e As Integer() = New Integer() {}
            Console.WriteLine(e Is Nothing)
        End Sub
        """;

    [Test]
    public void CppStringAndArrayNothingIsEmptiness_DivergesFromDotNet()
    {
        // #196 note: when Array<T> gains a real null state, the C++ half of this pin (only) must
        // flip from True to False — the array row, not the String row (String stays value-held).
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(P12_Divergence)), Is.EqualTo("False\nFalse"), "C#");
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(P12_Divergence)), Is.EqualTo("False\nFalse"), "JavaScript");
            Assert.That(FourBackends.Norm(Msil.MsilHarness.RunExpectingSuccess(P12_Divergence)), Is.EqualTo("False\nFalse"), "MSIL");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(P12_Divergence))), Is.EqualTo("True\nTrue"),
                "C++ — the divergence: no null state for either String or array there");
        });
    }

    // ============================================================================================
    // 4. D4 (1) — the operator-overload invariant. There is no user `Operator =` yet (#198), so
    //    this is pinned through C# ONLY with `System.Version` (`Operator =` from the BCL, not
    //    user-declared): reference identity must be False on two distinct, value-equal instances,
    //    proving emission never reaches a value-equality operator. The four-backend version of
    //    this invariant (a USER Operator=) waits on #198.
    //    ⚠ #198 has since landed for C# and C++: UserOperatorExecutionTests pins a user
    //    `Operator =` answering `a = b` True while `a Is b` stays False. JavaScript refuses user
    //    operators (BL7006), so the proof is two backends, not four.
    // ============================================================================================

    private const string P11_Overload = """
        Using System

        Sub Main()
            Dim v1 As Version = New Version(1, 2)
            Dim v2 As Version = New Version(1, 2)
            Console.WriteLine(v1 = v2)
            Console.WriteLine(v1 Is v2)
            Console.WriteLine(v1 IsNot v2)
            Dim v3 As Version = v1
            Console.WriteLine(v1 Is v3)
            Dim v4 As Version = Nothing
            Console.WriteLine(v4 Is Nothing)
        End Sub
        """;

    [Test]
    public void P11_SystemVersion_IsFalse_WhereEqualsIsTrue_OnCSharp()
    {
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(P11_Overload)),
            Is.EqualTo("True\nFalse\nTrue\nTrue\nTrue"),
            "`v1 = v2` (Version.op_Equality, value-equal) must be True while `v1 Is v2` (two " +
            "distinct instances) is False — emission never reaches op_Equality for `Is`. " +
            "P11 on cpp/javascript/msil is excluded: System.Version has no mapping on any of " +
            "them (measured, matrix-after.txt) — unrelated to Is/IsNot.");
    }

    // ============================================================================================
    // 5. E1 — `Me Is other` / `other IsNot Me` / `Me Is Nothing`, through the C++ `.get()` spelling
    //    (mutant: "the C++ .get() for Me removed" — without it `Me == other` compares a raw
    //    `this` pointer against a `shared_ptr`, which does not compile).
    // ============================================================================================

    private const string E1_Me = """
        Public Class Node
            Public Function Same(other As Node) As Boolean
                Return Me Is other
            End Function
            Public Function Differs(other As Node) As Boolean
                Return other IsNot Me
            End Function
            Public Function Missing() As Boolean
                Return Me Is Nothing
            End Function
        End Class

        Sub Main()
            Dim a As Node = New Node()
            Dim b As Node = New Node()
            Console.WriteLine(a.Same(a))
            Console.WriteLine(a.Same(b))
            Console.WriteLine(a.Differs(b))
            Console.WriteLine(a.Differs(a))
            Console.WriteLine(a.Missing())
        End Sub
        """;
    private const string E1_Exp = "True\nFalse\nTrue\nFalse\nFalse";

    [Test]
    public void E1_MeIdentity_RunsOnEveryBackend_BothPipelines()
    {
        FourBackends.RunsOnEveryBackend(E1_Me, E1_Exp);
        FourBackends.RunsOnEveryBackendAggressive(E1_Me, E1_Exp);
    }

    [Test]
    public void E1_MeIdentity_Cpp_UsesGetOnTheOtherOperand()
    {
        // A bare Does.Contain(".get()") is vacuous: the spliced BasicLang::NetRef runtime defines
        // its OWN get() (`get() const { return ...ref_.get(); }`), present regardless of this
        // program's own content. Check the SPECIFIC call this mutant targets instead — measured
        // (unmutated): `Same` emits `t0 = (this == (other).get());`.
        var cpp = BclE2E.CompileToCppOptimized(E1_Me);
        Assert.That(cpp, Does.Contain("(other).get()"),
            "mutant target: `Me Is other` must compare `other.get()` against the raw `this` " +
            "pointer — without `.get()` the shared_ptr/this comparison does not compile.\n" + cpp);
    }

    // ============================================================================================
    // 6. E10/E11 — lambda references. E10: a Delegate-typed VARIABLE holding a lambda, tested
    //    against Nothing before and after assignment. E11: a lambda LITERAL, INLINE — the shape
    //    that exercises the JS-parentheses / C++ std::function-wrap mutant (a bare lambda
    //    expression spliced straight into `=== null` / `== nullptr` does not parse/compile).
    // ============================================================================================

    private const string E10_LambdaDirect = """
        Sub Main()
            Dim x As Action = Nothing
            Console.WriteLine(x Is Nothing)
            x = Sub() Console.Write("")
            Console.WriteLine(x Is Nothing)
            Console.WriteLine(x IsNot Nothing)
            x()
        End Sub
        """;
    private const string E10_Exp = "True\nFalse\nTrue";

    private const string E11_LambdaLiteral = """
        Sub Main()
            Console.WriteLine((Function() 1) Is Nothing)
            Console.WriteLine((Function() 1) IsNot Nothing)
        End Sub
        """;
    private const string E11_Exp = "False\nTrue";

    [TestCase(E10_LambdaDirect, E10_Exp, "E10 lambda stored in a Delegate variable")]
    [TestCase(E11_LambdaLiteral, E11_Exp, "E11 lambda LITERAL, inline")]
    public void LambdaIdentity_RunsOnEveryBackend_BothPipelines(string source, string expected, string label)
    {
        TestContext.Out.WriteLine($"[{label}] standard pipeline");
        FourBackends.RunsOnEveryBackend(source, expected);
        TestContext.Out.WriteLine($"[{label}] aggressive pipeline");
        FourBackends.RunsOnEveryBackendAggressive(source, expected);
    }

    /// <summary>Mutant: "the JS parentheses ... wrap on a lambda literal removed" — a bare arrow
    /// function spliced into `=== null` is a JavaScript SyntaxError (the arrow body swallows what
    /// follows), so the lambda expression must be parenthesised in the emitted null test.</summary>
    [Test]
    public void E11_LambdaLiteral_JavaScript_ParenthesisesTheLambdaBeforeTheNullTest()
    {
        var js = JsTestSupport.CompileOptimized(E11_LambdaLiteral);
        // Correct: `(() => {...}) === null` — the lambda's closing brace is followed by its OWN
        // wrapping `)` before ` === null`. Mutant (the wrap removed): `() => {...} === null` — the
        // brace runs straight into `===` with no closing paren, which is a SyntaxError at runtime
        // (measured: `Unexpected token '==='`). Checked on text, not by running Node, because the
        // mutant's output is exactly the invalid program this wrap exists to prevent.
        Assert.That(js, Does.Contain("}) === null"),
            "the lambda literal must be wrapped in its OWN parentheses before the null test — " +
            "mutant target: the JS parentheses wrap removed.\n" + js);
    }

    /// <summary>Mutant: "the ... C++ std::function wrap on a lambda literal removed" — the raw
    /// closure expression has no `== nullptr`; it must be wrapped `std::function(...)` first.</summary>
    [Test]
    public void E11_LambdaLiteral_Cpp_WrapsTheLambdaInStdFunctionBeforeTheNullTest()
    {
        var cpp = BclE2E.CompileToCppOptimized(E11_LambdaLiteral);
        Assert.That(cpp, Does.Contain("std::function("),
            "the lambda literal must be wrapped in std::function(...) before the nullptr test — " +
            "mutant target: the C++ std::function wrap removed.\n" + cpp);
    }

    // ============================================================================================
    // 7. P13 — folding, at RUN level (the fold itself is pinned at the IR level in
    //    IsIsNotOperatorTests.D5_1_Optimizer_Folds_...). Confirms the folded constant still PRINTS
    //    right on every backend.
    // ============================================================================================

    private const string P13_Fold = """
        Sub Main()
            Console.WriteLine(Nothing Is Nothing)
            Console.WriteLine(Nothing IsNot Nothing)
        End Sub
        """;
    private const string P13_Exp = "True\nFalse";

    [Test]
    public void P13_NothingIsNothing_Folds_AndPrintsRight_OnEveryBackend_BothPipelines()
    {
        FourBackends.RunsOnEveryBackend(P13_Fold, P13_Exp);
        FourBackends.RunsOnEveryBackendAggressive(P13_Fold, P13_Exp);
    }

    // ============================================================================================
    // 8. The C++ `NetRef` null test: `!x`, never `== nullptr` (mutant: "NetRef's !x replaced by
    //    == nullptr"). An ordinary unresolvable .NET reference type (not NativeOwned) crosses as
    //    BasicLang::NetRef — matching HANDOFF.md's #173 note for `Dim s As Stream = Nothing`.
    // ============================================================================================

    private const string NetRefNothingTest = """
        Using System.IO

        Sub Check(s As Stream)
            Console.WriteLine(s Is Nothing)
        End Sub

        Sub Main()
            Check(Nothing)
        End Sub
        """;

    [Test]
    public void NetRefIdentity_Cpp_UsesLogicalNot_NeverEqualsNullptr()
    {
        var cpp = BclE2E.CompileToCppOptimized(NetRefNothingTest);
        Assert.Multiple(() =>
        {
            Assert.That(cpp, Does.Match(@"!\s*\(\s*s\s*\)"),
                "mutant target: NetRef's null test must be `!x`, never `x == nullptr` (NetRef has " +
                "no `==` at all).\n" + cpp);
            Assert.That(cpp, Does.Not.Contain("s == nullptr"), "\n" + cpp);
        });
    }

    // ============================================================================================
    // 9. Pre-existing gaps this task does NOT touch — each pinned with the reason and, where one
    //    exists, a no-Is control that fails identically.
    // ============================================================================================

    private const string E3_Lambda = """
        Public Class Foo
        End Class

        Sub Main()
            Dim a As Foo = Nothing
            Dim probe As Func(Of Boolean) = Function() a Is Nothing
            Console.WriteLine(probe())
            a = New Foo()
            Console.WriteLine(probe())
            Dim b As Foo = a
            Dim same As Func(Of Foo, Boolean) = Function(y As Foo) y Is b
            Console.WriteLine(same(a))
            Console.WriteLine(same(New Foo()))
        End Sub
        """;
    private const string E3_Exp = "True\nFalse\nTrue\nFalse";

    private const string E3b_LambdaControl = """
        Sub Main()
            Dim a As Integer = 0
            Dim probe As Func(Of Boolean) = Function() a = 0
            Console.WriteLine(probe())
            a = 1
            Console.WriteLine(probe())
        End Sub
        """;
    private const string E3b_Exp = "True\nFalse";

    [Test]
    public void E3_LambdaCapturedIdentity_CSharpJavaScriptMsil_RunOk()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(E3_Lambda)), Is.EqualTo(E3_Exp), "C#");
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(E3_Lambda)), Is.EqualTo(E3_Exp), "JavaScript");
            Assert.That(FourBackends.Norm(Msil.MsilHarness.RunExpectingSuccess(E3_Lambda)), Is.EqualTo(E3_Exp), "MSIL");
        });
    }

    /// <summary>
    /// ⭐ MOVED PIN (ADR-0016 D3/W2, task #170). E3 on C++ is a PRE-EXISTING lambda-capture
    /// defect, task #140 — measured against E3b, the no-`Is`-at-all control (a captured Integer
    /// mutated after the lambda is created), which USED TO fail IDENTICALLY: both silently printed
    /// stale captures ("True | True" instead of "True | False"). Not caused by, or fixed by,
    /// #185. #170's capability check now REFUSES BOTH by name instead (arm (b): <c>Main</c> writes
    /// the captured variable — <c>a</c> in each — at a point reachable from the lambda-creation
    /// instruction). Pinned so a #140 fix is a deliberate, noticed change here too.
    /// </summary>
    [Test]
    public void E3_LambdaCapturedIdentity_Cpp_RefusedByName_PinnedForTask140_WithItsControl()
    {
        Assert.Multiple(() =>
        {
            var e3 = Assert.Throws<CppCapabilityException>(() => BclE2E.CompileToCppOptimized(E3_Lambda));
            Assert.That(e3!.Message, Does.Contain("captures 'a' of 'Main'").And.Contain("#140"),
                "E3 (task #140 flips this to running) — re-measure before touching.\n" + e3.Message);

            var e3b = Assert.Throws<CppCapabilityException>(() => BclE2E.CompileToCppOptimized(E3b_LambdaControl));
            Assert.That(e3b!.Message, Does.Contain("captures 'a' of 'Main'").And.Contain("#140"),
                "E3b control — the SAME capture-write shape with no Is/IsNot at all.\n" + e3b.Message);
        });
    }

    /// <summary>
    /// `Integer?` on C++, MSIL and JS — #193, pre-existing and unrelated to #185 (the nullable
    /// type itself has no lowering on any of the three: "undeclared identifier 'Integer'" on
    /// C++, "Reference to undefined class 'Integer'" on MSIL, BL7007 on JavaScript). C# runs.
    /// </summary>
    [Test]
    public void E5_NullableIdentity_CSharp_RunsOk_OthersExcluded_Against193()
    {
        const string e5 = """
            Sub Main()
                Dim n As Integer? = Nothing
                Console.WriteLine(n Is Nothing)
                Console.WriteLine(n IsNot Nothing)
            End Sub
            """;
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(e5)), Is.EqualTo("True\nFalse"), "C#");

        var cppEx = Assert.Throws<AssertionException>(() => BclE2E.CompileRun(BclE2E.CompileToCppOptimized(e5)));
        Assert.That(cppEx!.Message, Does.Contain("Integer"), "#193, unchanged by #185\n" + cppEx.Message);
    }
}
