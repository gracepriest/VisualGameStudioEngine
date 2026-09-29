using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.MSIL;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.SemanticAnalysis;
using NUnit.Framework;
using VisualGameStudio.Tests.Compiler;

namespace VisualGameStudio.Tests.Msil;

// =====================================================================================
//  Task #155 / ADR-0010 — MSIL closure conversion (ClosureLowering).
//
//  Probe sources are VERBATIM from the implementer's measurement corpus
//  (scratchpad t155/probes/L*.bas, t155/edge/R*.bas) except where a doc comment below says a
//  probe was written for this fixture specifically (the private-Me case D2's contract asks
//  for, and the by-value-parameter capture no L-probe exercises). Every expected value in this
//  file was independently re-measured against this working tree (CLI, both pipelines, and —
//  where noted — the C#/JavaScript oracle) before being pinned; none is copied from MSIL's own
//  output (CLAUDE.md's rule for this backend).
//
//  C++ is NOT a third oracle here for most rows. Per the implementer's own byte-compare and
//  matrix (S/t155/probes/matrix-after.txt), the C++ backend's OWN lambda lowering is pre-
//  existing-known-wrong for a WRITE capture (task #140, capture by copy — measured with no
//  optimizer pass running at all) and does not build at all for a loop/For-Each capture — both
//  unrelated to #155 and untouched by it. Where all four backends genuinely agree
//  (L1/L2/L3/L8/L9 — no write-back through a capture, no per-iteration semantics),
//  FourBackends.RunsOnEveryBackend[Aggressive] is used so one assertion covers all four; where
//  they do not, this file follows the house pattern already established by
//  CopyPropagationSharedVocabularyTests/LicmKillVocabularyTests: C#-and-JavaScript-and-MSIL in
//  one Assert.Multiple, with C++'s pre-existing gap left to the fixtures that already pin it.
//  L7 drops the C# leg too — measured, pre-existing, unrelated to MSIL: C# fails this exact
//  shape with CS0103 ("the name 'inner' does not exist") on every pipeline, before and after
//  #155.
// =====================================================================================

/// <summary>Every BASIC source this file runs, named to match the implementer's probe letters.</summary>
internal static class ClosureLoweringProbes
{
    // ---- All four backends agree (no write-back through a capture, no per-iteration rule) ----

    internal const string L1 = """
        Sub Main()
            Dim f = Sub() Console.WriteLine("hi")
            f()
            f()
        End Sub
        """;
    internal const string L1Expected = "hi\nhi";

    internal const string L2 = """
        Sub Main()
            Dim sq = Function(x As Integer) x * x
            Console.WriteLine(sq(5))
        End Sub
        """;
    internal const string L2Expected = "25";

    internal const string L3 = """
        Sub Main()
            Dim k As Integer = 3
            Dim add = Function(x As Integer) x + k
            Console.WriteLine(add(5))
        End Sub
        """;
    internal const string L3Expected = "8";

    internal const string L8 = """
        Class Box
            Public K As Integer = 5

            Function Scaled() As Integer
                Dim f = Function(m As Integer) K * m
                Return f(3)
            End Function
        End Class

        Sub Main()
            Dim b As New Box()
            Console.WriteLine(b.Scaled())
        End Sub
        """;
    internal const string L8Expected = "15";

    internal const string L9 = """
        Sub Hello()
            Console.WriteLine("hello")
        End Sub

        Sub Main()
            Dim a As Action = AddressOf Hello
            a()
        End Sub
        """;
    internal const string L9Expected = "hello";

    // ---- C#, JavaScript and MSIL agree; C++ is task #140 (capture by copy), unrelated -------

    internal const string L4 = """
        Sub Main()
            Dim n As Integer = 1
            Dim q As Integer = 2
            Dim bump = Sub() n = n + 100
            Dim a As Integer = n + q
            bump()
            Dim b As Integer = n + q
            Console.WriteLine(CStr(a) & "," & CStr(b))
        End Sub
        """;
    internal const string L4Expected = "3,103";

    internal const string L5 = """
        Sub Twice(f As Action)
            f()
            f()
        End Sub

        Function Apply(g As Func(Of Integer, Integer), v As Integer) As Integer
            Return g(v)
        End Function

        Sub Main()
            Dim n As Integer = 5
            Twice(Sub() n = n + 1)
            Console.WriteLine(n)
            Console.WriteLine(Apply(Function(x As Integer) x + n, 5))
        End Sub
        """;
    internal const string L5Expected = "7\n12";

    internal const string L11 = """
        Sub Main()
            Dim x As Integer = 3
            Dim s As Integer = 0
            Dim bump = Sub() x = x + 1
            For i As Integer = 1 To 3
                bump()
                s = s + x * 2
            Next
            Console.WriteLine(s)
        End Sub
        """;
    internal const string L11Expected = "30";

    /// <summary>NOT one of the implementer's L-probes: no L1..L16 shape captures a BY-VALUE
    /// PARAMETER (L3/L4/L11's captured names are all creator LOCALS). D4 specifically requires
    /// a by-value parameter to be copied into the environment at entry, with every later read
    /// AND write going through it — this is the shape that proves it. Re-measured against C#
    /// (native closures capture the parameter directly) and JavaScript before being pinned:
    /// both print 7, matching MSIL.</summary>
    internal const string ByValParamCapture = """
        Sub Work(n As Integer)
            Dim f = Sub() n = n + 1
            f()
            f()
            Console.WriteLine(n)
        End Sub

        Sub Main()
            Work(5)
        End Sub
        """;
    internal const string ByValParamCaptureExpected = "7";

    /// <summary>NOT one of the implementer's L-probes: L8 reaches a PUBLIC field through Me, so
    /// it says nothing about D6/the implementer's nested-environment note — "measured: a
    /// TOP-LEVEL class reading another class's Private field dies with FieldAccessException; a
    /// NESTED class may" (ADR-0010). This is the PRIVATE-field version of L8's exact shape, the
    /// reason <c>IRClass.EnclosingClass</c> exists at all. Re-measured: C# and JavaScript agree
    /// on 15 (same as L8 — a lambda has the same access to its own class's members either way);
    /// MSIL must too, and only WITH nesting.</summary>
    internal const string PrivateFieldThroughMe = """
        Class Box
            Private K As Integer = 5

            Function Scaled() As Integer
                Dim f = Function(m As Integer) K * m
                Return f(3)
            End Function
        End Class

        Sub Main()
            Dim b As New Box()
            Console.WriteLine(b.Scaled())
        End Sub
        """;
    internal const string PrivateFieldThroughMeExpected = "15";

    // ---- JavaScript and MSIL agree; C# ALSO fails this shape (CS0103, pre-existing, unrelated) --

    /// <summary>C# fails this exact source with <c>CS0103: The name 'inner' does not exist in
    /// the current context</c> on EVERY pipeline, measured before AND after #155
    /// (matrix-master.txt / matrix-after.txt) — a pre-existing front-end/C#-backend gap in
    /// naming a doubly-nested lambda's own inner lambda, unrelated to MSIL. JavaScript is the
    /// only working oracle for this row.</summary>
    internal const string L7 = """
        Sub Main()
            Dim n As Integer = 1
            Dim q As Integer = 2
            Dim outer = Sub()
                            Dim inner = Sub() n = n + 100
                            inner()
                        End Sub
            Dim a As Integer = n + q
            outer()
            Dim b As Integer = n + q
            Console.WriteLine(CStr(a) & "," & CStr(b))
        End Sub
        """;
    internal const string L7Expected = "3,103";

    // ---- Loop / For-Each captures. C#, JavaScript and MSIL agree; C++ COMPILE-FAILS these ----
    // ---- entirely (measured, pre-existing, unrelated — matrix-after.txt) --------------------

    internal const string L13 = """
        Sub Main()
            Dim fs As New List(Of Action)()
            Dim l As New List(Of Integer)()
            l.Add(1)
            l.Add(2)
            l.Add(3)
            For Each x As Integer In l
                fs.Add(Sub() Console.WriteLine(x))
            Next
            For Each f As Action In fs
                f()
            Next
        End Sub
        """;
    internal const string L13Expected = "1\n2\n3";

    /// <summary>Nested DECLARING For Eachs, both variables captured (D2 amended's contract):
    /// each lambda must see its OWN (outer, inner) pair, which only holds if the iteration
    /// environment is chained (D5) rather than shared or flattened.</summary>
    internal const string L13b = """
        Sub Main()
            Dim fs As New List(Of Action)()
            Dim outer As New List(Of Integer)()
            outer.Add(1)
            outer.Add(2)
            Dim inner As New List(Of Integer)()
            inner.Add(10)
            inner.Add(20)
            For Each a As Integer In outer
                For Each b As Integer In inner
                    fs.Add(Sub() Console.WriteLine(CStr(a) & "," & CStr(b)))
                Next
            Next
            For Each f As Action In fs
                f()
            Next
        End Sub
        """;
    internal const string L13bExpected = "1,10\n1,20\n2,10\n2,20";

    /// <summary>A NON-declaring For Each (#168's hidden <c>__foreach_N</c> plus the assignment)
    /// over an existing <c>x</c> — D2 amended says this gets NO iteration environment, so every
    /// lambda shares x at FUNCTION level and sees its FINAL value (3), unlike L13's fresh-per-
    /// iteration 1/2/3.</summary>
    internal const string L13c = """
        Sub Main()
            Dim fs As New List(Of Action)()
            Dim l As New List(Of Integer)()
            l.Add(1)
            l.Add(2)
            l.Add(3)
            Dim x As Integer = 0
            For Each x In l
                fs.Add(Sub() Console.WriteLine(x))
            Next
            For Each f As Action In fs
                f()
            Next
        End Sub
        """;
    internal const string L13cExpected = "3\n3\n3";

    /// <summary><c>For i = ... To ...</c> is ONE function-level binding (D2 amended, the VB
    /// rule) — every lambda sees the loop's FINAL value, unlike L13's declaring For Each.</summary>
    internal const string L14 = """
        Sub Main()
            Dim fs As New List(Of Action)()
            For i As Integer = 1 To 3
                fs.Add(Sub() Console.WriteLine(i))
            Next
            For Each f As Action In fs
                f()
            Next
        End Sub
        """;
    internal const string L14Expected = "4\n4\n4";

    /// <summary>⚠ STALE HEADER, KEPT FOR HISTORY — see the class remarks and
    /// <c>L15_LoopBodyDimIsPerIteration_*</c> below. This used to be bound at FUNCTION level
    /// (C#/JS 6|6|6, ADR-0010's own D2 amendment, "the pass must not reset or copy-forward
    /// <c>y</c>"), a recorded, deliberate, all-backend divergence from VB's 1|3|6.
    /// <b>ADR-0014 D5 SUPERSEDES that clause</b> (task #172, fix commit <c>4cdf2dd1</c> on top of
    /// <c>ef1e949b</c>): a captured loop-body <c>Dim</c> is now per-iteration with copy-forward on
    /// every backend, so this shape prints VB's own 1|3|6 — same as
    /// <c>PerIterationLoopBodyDimProbes.L2</c> in
    /// <c>VisualGameStudio.Tests.Compiler.PerIterationLoopBodyDimTests.cs</c>, which this probe is
    /// byte-identical to modulo the <c>Sub</c>/<c>Function</c> capture kind. <c>L15Expected</c> is
    /// kept as the name every existing caller uses; its VALUE is now VB's, not the old oracle's.</summary>
    internal const string L15 = """
        Sub Main()
            Dim fs As New List(Of Action)()
            For i As Integer = 1 To 3
                Dim y As Integer
                y = y + i
                fs.Add(Sub() Console.WriteLine(y))
            Next
            For Each f As Action In fs
                f()
            Next
        End Sub
        """;
    internal const string L15Expected = "1\n3\n6";

    /// <summary>A <c>Catch</c> variable captured and invoked after the <c>Try</c> — L16 itself
    /// (<c>Dim f As Action = Nothing</c>) used to fail the front end for an unrelated reason
    /// ("Cannot assign value of type 'Object' to variable of type 'Action'"), so L16b (no
    /// initializer) was the implementer's re-probe and the one the contract names. ⚠ STALE AS OF
    /// #173 (fix commit c0b457d9): <c>Nothing</c> now converts to any reference type at every
    /// site, so L16 itself compiles — C#, JavaScript and MSIL print "boom", same as L16b.
    /// ⛔ <b>#189 DONE (fix commit 381b95ff):</b> C++ used to still fail here — the captured
    /// <c>Catch</c> variable's <c>ex.Message</c> lowered to <c>ex-&gt;Message</c>, a shared_ptr
    /// field access, against an exception representation C++ holds by VALUE. C++ now takes the
    /// captured variable by init-capture and reads it through the same <c>.what()</c> spelling the
    /// Catch clause's own body uses, so L16b (and L16's exact shape, N7 in
    /// <c>NothingConversionExecutionTests</c>) now runs on all four backends.</summary>
    internal const string L16b = """
        Sub Main()
            Dim f As Action
            Try
                Throw New Exception("boom")
            Catch ex As Exception
                f = Sub() Console.WriteLine(ex.Message)
            End Try
            f()
        End Sub
        """;
    internal const string L16bExpected = "boom";

    // ---- D8: invocation and AddressOf ---------------------------------------------------------

    /// <summary>Nothing in this program declares <c>Ghost</c> — not a module procedure, not a
    /// class member, not a delegate-typed variable. The front end does not itself validate that
    /// a called name resolves to anything (measured: this compiles past semantic analysis), so
    /// MSIL's own D8 check is the only thing standing between this and a MissingMethodException
    /// at run time.</summary>
    internal const string D8UndefinedCallee = """
        Sub Main()
            Ghost()
        End Sub
        """;

    /// <summary>A local variable named exactly like a module Sub, holding a delegate — VB's
    /// (and C#'s) rule is that the variable SHADOWS the procedure, so <c>Bump()</c> must invoke
    /// the DELEGATE, not the Sub. Re-measured against C# (whose own local-shadows-static-method
    /// scoping gives the same answer natively): both print "delegate".</summary>
    internal const string D8DelegateShadowsProcedure = """
        Sub Bump()
            Console.WriteLine("proc")
        End Sub

        Sub Main()
            Dim Bump As Action = Sub() Console.WriteLine("delegate")
            Bump()
        End Sub
        """;
    internal const string D8DelegateShadowsProcedureExpected = "delegate";

    // ---- D7: delegate types and the measured arity cap ---------------------------------------

    /// <summary>The measured cap itself, both directions in one program: Action`8 runs, Func`9
    /// runs. (Action`9 / Func`10 are the REFUSED direction — see the refusal fixture.)</summary>
    internal const string D7ArityAtCap = """
        Sub Main()
            Dim f As Action(Of Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer) = Sub(a As Integer, b As Integer, c As Integer, d As Integer, e As Integer, g As Integer, h As Integer, i As Integer) Console.WriteLine(a + i)
            f(1, 2, 3, 4, 5, 6, 7, 8)
            Dim g2 As Func(Of Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer) = Function(a As Integer, b As Integer, c As Integer, d As Integer, e As Integer, g As Integer, h As Integer, i As Integer) a * i
            Console.WriteLine(g2(2, 2, 3, 4, 5, 6, 7, 8))
        End Sub
        """;
    internal const string D7ArityAtCapExpected = "9\n16";

    /// <summary>A user <c>Delegate Sub D()</c> with NO parameters — the shape whose
    /// <c>BeginInvoke(, class ...)</c> stray leading comma made ilasm refuse EVERY parameterless
    /// user delegate before this fix, unrelated to #155's lambda work but fixed in the same
    /// commit. <c>CType</c> is used here as belt-and-braces — this fixture is about the
    /// parameterless <c>BeginInvoke</c> comma, not target-typing.
    ///
    /// <para>⚠ STALE AS OF #187, CORRECTED: this used to say the front end typed every
    /// lambda/AddressOf expression as 'Action'/'Func' and refused to assign one to a nominal
    /// Delegate slot directly (measured pre-#187: <c>Dim f As D = AddressOf Hi</c> and
    /// <c>Take(AddressOf Hi)</c> both failed with "cannot convert from 'Action' to 'D'"), with
    /// <c>CType</c> as the front end's only escape hatch. #187 target-types a lambda or
    /// <c>AddressOf</c> to a user <c>Delegate</c> directly (<c>SemanticAnalyzer.ConvertToUserDelegate</c>),
    /// so <c>Dim f As D = AddressOf Hi</c> now compiles without <c>CType</c> — the escape hatch
    /// is no longer needed for this shape, only kept here unchanged since it still exercises the
    /// same IL bug this fixture is pinning.</para></summary>
    internal const string D7UserDelegateParameterless = """
        Delegate Sub D()

        Sub Hi()
            Console.WriteLine("hi")
        End Sub

        Sub Main()
            Dim f As D = CType(AddressOf Hi, D)
            f()
        End Sub
        """;
    internal const string D7UserDelegateParameterlessExpected = "hi";

    /// <summary>Every probe the contract table names, MSIL entry point aside — used by the
    /// verifier sweep, which cares about "does lowering this raise a verifier failure", not
    /// about backend agreement.</summary>
    internal static IEnumerable<(string Name, string Source)> AllContractSources()
    {
        yield return ("L1", L1);
        yield return ("L2", L2);
        yield return ("L3", L3);
        yield return ("L4", L4);
        yield return ("L5", L5);
        yield return ("L7", L7);
        yield return ("L8", L8);
        yield return ("L9", L9);
        yield return ("L11", L11);
        yield return ("L13", L13);
        yield return ("L13b", L13b);
        yield return ("L13c", L13c);
        yield return ("L14", L14);
        yield return ("L15", L15);
        yield return ("L16b", L16b);
        yield return ("ByValParamCapture", ByValParamCapture);
        yield return ("PrivateFieldThroughMe", PrivateFieldThroughMe);
    }
}

// =====================================================================================
//  (a) CONTRACT — every row through both pipelines. [Category("Integration")]: every leg here
//  spawns a process (ilasm+dotnet, node, or both) or compiles/runs C++.
// =====================================================================================

[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg (FourBackends) redirects Console.Out
public class ClosureLoweringContractTests
{
    // ---- Shared per-backend-set assertions, mirroring FourBackends.RunsOnEveryBackend's shape ----

    private static void CSharpJsMsilStandard(string source, string expected) => Assert.Multiple(() =>
    {
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(source)), Is.EqualTo(expected), "C#, standard");
        Assert.That(FourBackends.Norm(JavaScriptOptimizedExecutionTests.RunOptimized(source)), Is.EqualTo(expected), "JavaScript, standard");
        Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(source)), Is.EqualTo(expected), "MSIL, standard");
    });

    private static void CSharpJsMsilAggressive(string source, string expected) => Assert.Multiple(() =>
    {
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(source)), Is.EqualTo(expected), "C#, aggressive");
        Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(source)), Is.EqualTo(expected), "JavaScript, aggressive");
        Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(source)), Is.EqualTo(expected), "MSIL, aggressive");
    });

    private static void JsMsilStandard(string source, string expected) => Assert.Multiple(() =>
    {
        Assert.That(FourBackends.Norm(JavaScriptOptimizedExecutionTests.RunOptimized(source)), Is.EqualTo(expected), "JavaScript, standard");
        Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(source)), Is.EqualTo(expected), "MSIL, standard");
    });

    private static void JsMsilAggressive(string source, string expected) => Assert.Multiple(() =>
    {
        Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(source)), Is.EqualTo(expected), "JavaScript, aggressive");
        Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(source)), Is.EqualTo(expected), "MSIL, aggressive");
    });

    // ---- L1/L2/L3/L8/L9: all four backends agree ---------------------------------------------

    [Test]
    public void L1_StandardPipeline_AllFourBackendsAgree()
        => FourBackends.RunsOnEveryBackend(ClosureLoweringProbes.L1, ClosureLoweringProbes.L1Expected);

    [Test]
    public void L1_AggressivePipeline_AllFourBackendsAgree()
        => FourBackends.RunsOnEveryBackendAggressive(ClosureLoweringProbes.L1, ClosureLoweringProbes.L1Expected);

    [Test]
    public void L2_StandardPipeline_AllFourBackendsAgree()
        => FourBackends.RunsOnEveryBackend(ClosureLoweringProbes.L2, ClosureLoweringProbes.L2Expected);

    [Test]
    public void L2_AggressivePipeline_AllFourBackendsAgree()
        => FourBackends.RunsOnEveryBackendAggressive(ClosureLoweringProbes.L2, ClosureLoweringProbes.L2Expected);

    [Test]
    public void L3_StandardPipeline_AllFourBackendsAgree()
        => FourBackends.RunsOnEveryBackend(ClosureLoweringProbes.L3, ClosureLoweringProbes.L3Expected);

    [Test]
    public void L3_AggressivePipeline_AllFourBackendsAgree()
        => FourBackends.RunsOnEveryBackendAggressive(ClosureLoweringProbes.L3, ClosureLoweringProbes.L3Expected);

    /// <summary>A lambda in a CLASS method reading a field through <c>Me</c> — the shape D6's
    /// environment-captures-Me decision exists for. The field is PUBLIC here, so this alone
    /// does not exercise the nested-environment note; see PrivateFieldThroughMe below for that.</summary>
    [Test]
    public void L8_StandardPipeline_AllFourBackendsAgree()
        => FourBackends.RunsOnEveryBackend(ClosureLoweringProbes.L8, ClosureLoweringProbes.L8Expected);

    [Test]
    public void L8_AggressivePipeline_AllFourBackendsAgree()
        => FourBackends.RunsOnEveryBackendAggressive(ClosureLoweringProbes.L8, ClosureLoweringProbes.L8Expected);

    [Test]
    public void L9_AddressOf_StandardPipeline_AllFourBackendsAgree()
        => FourBackends.RunsOnEveryBackend(ClosureLoweringProbes.L9, ClosureLoweringProbes.L9Expected);

    [Test]
    public void L9_AddressOf_AggressivePipeline_AllFourBackendsAgree()
        => FourBackends.RunsOnEveryBackendAggressive(ClosureLoweringProbes.L9, ClosureLoweringProbes.L9Expected);

    // ---- L4/L5/L11/ByValParamCapture: C#, JS, MSIL agree; C++ is task #140 (unrelated) --------

    [Test]
    public void L4_WriteCapture_StandardPipeline_CSharpJavaScriptMsilAgree()
        => CSharpJsMsilStandard(ClosureLoweringProbes.L4, ClosureLoweringProbes.L4Expected);

    [Test]
    public void L4_WriteCapture_AggressivePipeline_CSharpJavaScriptMsilAgree()
        => CSharpJsMsilAggressive(ClosureLoweringProbes.L4, ClosureLoweringProbes.L4Expected);

    [Test]
    public void L5_LambdaPassedAsActionAndFunc_StandardPipeline_CSharpJavaScriptMsilAgree()
        => CSharpJsMsilStandard(ClosureLoweringProbes.L5, ClosureLoweringProbes.L5Expected);

    [Test]
    public void L5_LambdaPassedAsActionAndFunc_AggressivePipeline_CSharpJavaScriptMsilAgree()
        => CSharpJsMsilAggressive(ClosureLoweringProbes.L5, ClosureLoweringProbes.L5Expected);

    [Test]
    public void L11_LambdaCalledInLoop_StandardPipeline_CSharpJavaScriptMsilAgree()
        => CSharpJsMsilStandard(ClosureLoweringProbes.L11, ClosureLoweringProbes.L11Expected);

    [Test]
    public void L11_LambdaCalledInLoop_AggressivePipeline_CSharpJavaScriptMsilAgree()
        => CSharpJsMsilAggressive(ClosureLoweringProbes.L11, ClosureLoweringProbes.L11Expected);

    /// <summary>D4: a captured BY-VALUE PARAMETER, copied into the environment at entry, with
    /// every later read and write going through it. No L1..L16 probe exercises this — see the
    /// probe's own doc comment.</summary>
    [Test]
    public void ByValParamCapture_StandardPipeline_CSharpJavaScriptMsilAgree()
        => CSharpJsMsilStandard(ClosureLoweringProbes.ByValParamCapture, ClosureLoweringProbes.ByValParamCaptureExpected);

    [Test]
    public void ByValParamCapture_AggressivePipeline_CSharpJavaScriptMsilAgree()
        => CSharpJsMsilAggressive(ClosureLoweringProbes.ByValParamCapture, ClosureLoweringProbes.ByValParamCaptureExpected);

    /// <summary>D6/the implementer's nested-environment note: a lambda reaching a PRIVATE field
    /// through Me. Independently re-measured with ilasm+dotnet on this working tree: a
    /// TOP-LEVEL environment class reading Box's private K dies with FieldAccessException; only
    /// nesting the environment inside Box (IRClass.EnclosingClass) lets this print 15.</summary>
    [Test]
    public void PrivateFieldThroughMe_StandardPipeline_CSharpJavaScriptMsilAgree()
        => CSharpJsMsilStandard(ClosureLoweringProbes.PrivateFieldThroughMe, ClosureLoweringProbes.PrivateFieldThroughMeExpected);

    [Test]
    public void PrivateFieldThroughMe_AggressivePipeline_CSharpJavaScriptMsilAgree()
        => CSharpJsMsilAggressive(ClosureLoweringProbes.PrivateFieldThroughMe, ClosureLoweringProbes.PrivateFieldThroughMeExpected);

    // ---- L7: JavaScript and MSIL agree; C# fails this exact shape, pre-existing, unrelated ---

    [Test]
    public void L7_NestedLambdaTwoLevelsOut_StandardPipeline_JavaScriptAndMsilAgree()
        => JsMsilStandard(ClosureLoweringProbes.L7, ClosureLoweringProbes.L7Expected);

    [Test]
    public void L7_NestedLambdaTwoLevelsOut_AggressivePipeline_JavaScriptAndMsilAgree()
        => JsMsilAggressive(ClosureLoweringProbes.L7, ClosureLoweringProbes.L7Expected);

    // ---- L13/L13b/L13c/L14/L15: C#, JS, MSIL agree; C++ COMPILE-FAILS all of these entirely ----
    // ---- (pre-existing, unrelated to #155) ------------------------------------------------------
    // ---- L16b: was in that same group; #189 DONE (fix commit 381b95ff) fixed C++'s captured- ---
    // ---- Catch-variable rendering, so L16b now runs on all four backends (see its own doc). ----

    [Test]
    public void L13_DeclaringForEachCapture_StandardPipeline_CSharpJavaScriptMsilAgree()
        => CSharpJsMsilStandard(ClosureLoweringProbes.L13, ClosureLoweringProbes.L13Expected);

    [Test]
    public void L13_DeclaringForEachCapture_AggressivePipeline_CSharpJavaScriptMsilAgree()
        => CSharpJsMsilAggressive(ClosureLoweringProbes.L13, ClosureLoweringProbes.L13Expected);

    [Test]
    public void L13b_NestedDeclaringForEachs_StandardPipeline_CSharpJavaScriptMsilAgree()
        => CSharpJsMsilStandard(ClosureLoweringProbes.L13b, ClosureLoweringProbes.L13bExpected);

    [Test]
    public void L13b_NestedDeclaringForEachs_AggressivePipeline_CSharpJavaScriptMsilAgree()
        => CSharpJsMsilAggressive(ClosureLoweringProbes.L13b, ClosureLoweringProbes.L13bExpected);

    [Test]
    public void L13c_NonDeclaringForEach_StandardPipeline_CSharpJavaScriptMsilAgree()
        => CSharpJsMsilStandard(ClosureLoweringProbes.L13c, ClosureLoweringProbes.L13cExpected);

    [Test]
    public void L13c_NonDeclaringForEach_AggressivePipeline_CSharpJavaScriptMsilAgree()
        => CSharpJsMsilAggressive(ClosureLoweringProbes.L13c, ClosureLoweringProbes.L13cExpected);

    [Test]
    public void L14_ForNextSharedBinding_StandardPipeline_CSharpJavaScriptMsilAgree()
        => CSharpJsMsilStandard(ClosureLoweringProbes.L14, ClosureLoweringProbes.L14Expected);

    [Test]
    public void L14_ForNextSharedBinding_AggressivePipeline_CSharpJavaScriptMsilAgree()
        => CSharpJsMsilAggressive(ClosureLoweringProbes.L14, ClosureLoweringProbes.L14Expected);

    /// <summary>RENAMED from <c>L15_LoopBodyDimFunctionLevel_…</c> and its expectation MOVED from
    /// 6|6|6 to VB's own 1|3|6 — ADR-0014 D5 supersedes ADR-0010 D2's L15 clause (task #172, fix
    /// commit <c>4cdf2dd1</c> on <c>ef1e949b</c>): a loop-body <c>Dim</c> a lambda captures is now
    /// per-iteration with copy-forward on every backend, C# and JavaScript included. See
    /// <c>VisualGameStudio.Tests.Compiler.PerIterationLoopBodyDimTests</c> for the full ADR-0014
    /// coverage (L1-L7, Exit/re-entry, the optimizer, the IR facts); this pair stays here only
    /// because it is this fixture's own L13-L16b closure-lowering family and its MSIL leg matters
    /// to ADR-0010's own contract.</summary>
    [Test]
    public void L15_LoopBodyDimIsPerIteration_StandardPipeline_CSharpJavaScriptMsilAgree()
        => CSharpJsMsilStandard(ClosureLoweringProbes.L15, ClosureLoweringProbes.L15Expected);

    [Test]
    public void L15_LoopBodyDimIsPerIteration_AggressivePipeline_CSharpJavaScriptMsilAgree()
        => CSharpJsMsilAggressive(ClosureLoweringProbes.L15, ClosureLoweringProbes.L15Expected);

    [Test]
    public void L16b_CatchVariableCapture_StandardPipeline_AllFourBackendsAgree()
        => FourBackends.RunsOnEveryBackend(ClosureLoweringProbes.L16b, ClosureLoweringProbes.L16bExpected);

    [Test]
    public void L16b_CatchVariableCapture_AggressivePipeline_AllFourBackendsAgree()
        => FourBackends.RunsOnEveryBackendAggressive(ClosureLoweringProbes.L16b, ClosureLoweringProbes.L16bExpected);

    // ---- (c) D8: invocation ---------------------------------------------------------------

    /// <summary>The RUNNING half of D8's shadow rule (the throw half is in the non-Integration
    /// refusal fixture, since it needs no ilasm). Matches C#'s own local-shadows-static-method
    /// scoping natively.</summary>
    [Test]
    public void D8_DelegateVariable_ShadowsModuleProcedure_InvokesDelegate()
        => Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(ClosureLoweringProbes.D8DelegateShadowsProcedure)),
            Is.EqualTo(ClosureLoweringProbes.D8DelegateShadowsProcedureExpected));

    // ---- (d) D7: delegate types and the measured arity cap --------------------------------

    [Test]
    public void D7_ActionEightAndFuncNine_AtTheMeasuredCap_Run()
        => Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(ClosureLoweringProbes.D7ArityAtCap)),
            Is.EqualTo(ClosureLoweringProbes.D7ArityAtCapExpected));

    /// <summary>Covers the BeginInvoke stray-comma fix: before it, NO parameterless user
    /// <c>Delegate</c> ever assembled on MSIL.</summary>
    [Test]
    public void D7_ParameterlessUserDelegate_AssemblesAndRuns()
        => Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(ClosureLoweringProbes.D7UserDelegateParameterless)),
            Is.EqualTo(ClosureLoweringProbes.D7UserDelegateParameterlessExpected));
}
