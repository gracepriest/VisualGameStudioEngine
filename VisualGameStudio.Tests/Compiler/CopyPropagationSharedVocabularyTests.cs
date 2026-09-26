using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.SemanticAnalysis;
using VisualGameStudio.Tests.Msil;
using TypeInfo = BasicLang.Compiler.SemanticAnalysis.TypeInfo;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #146 — CopyPropagationPass becomes a CONSUMER of the shared kill vocabulary (ADR-0006 D1/
//  D3): OptimizationPass.NamesWrittenBy / IsCallVisible / ReadsCallVisible, the same answers CSE's
//  Invalidate, LICM's VariablesWrittenIn and IRVerifier already use. Before this it kept its OWN
//  private kill rules (an assignment target, an IRStore to a variable, a renamed value, a ByRef
//  argument of an IRCall/IRInstanceMethodCall) and had NO call arm at all: a copy fact for a
//  FIELD, a GLOBAL, or anything else the shared vocabulary already knew a call could reach
//  survived the call. MEASURED (BarePropertyLoweringTests.CP1, task #146's own flagship):
//  `K = 5 : Inc() : K + 1`, Inc bumping the field K, printed 6 for 16 on all four backends
//  including C# — C#'s inline-always policy re-emits the binop's text, but ConstantFolding had
//  already folded the STALE `5 + 1` before that text was ever written.
//
//  Every probe below is verbatim from the implementer's evidence (S/cp146/{probes,extra,ctl}/*.bas
//  — see this session's scratchpad), each value cross-checked against that probe's own .exp AND
//  against S/cp146/{probes,extra}/matrix-fix.txt / matrix-base.txt cell by cell before being
//  pinned here — matching the convention KillVocabularyExtensionsTests.cs and
//  BarePropertyLoweringTests.cs already established. `fmt()` in extra/probe.py joins stdout lines
//  with " | " for the matrix's one-line-per-cell format; every two/three-line expected string
//  below un-joins that back to the real newlines.
// ================================================================================================

internal static class CopyPropagationSharedVocabularyProbes
{
    /// <summary>
    /// CP2 — task #146's OWN closure-rule flagship, restated from the CopyPropagationPass class
    /// remarks: <c>Dim bump = Sub() p = p + 100 : a = p + q : bump() : l(0) = p + q</c>. Before
    /// #146 this printed 3,3 for 103,3 on C# and JavaScript (CopyPropagation's private rules had
    /// no closure awareness at all, so the fact <c>p -&gt; 1</c> survived <c>bump()</c> and
    /// ConstantFolding folded BOTH occurrences of <c>p + q</c> to the same stale constant).
    /// </summary>
    internal const string CP2 = """
        Sub Main()
            Dim p As Integer = 1
            Dim q As Integer = 2
            Dim l As New List(Of Integer)()
            l.Add(0)
            Dim bump = Sub() p = p + 100
            Dim a As Integer = p + q
            bump()
            l(0) = p + q
            Console.WriteLine(CStr(l(0)) & "," & CStr(a))
        End Sub
        """;
    internal const string CP2Expected = "103,3";

    /// <summary>CP5 — a class field, aliased through a local (<c>x = K</c>) that CopyPropagation
    /// then carries across <c>Inc()</c>. Before #146: <c>seed | 16,15</c> (both <c>x</c>'s copy
    /// fact AND the bare-field read stayed stale). Correct is <c>seed | 6,15</c>.</summary>
    internal const string CP5 = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class Box
            Public K As Integer

            Sub Inc()
                K = K + 10
            End Sub

            Sub Work()
                Dim x As Integer
                K = Seed(5)
                x = K
                Inc()
                Console.WriteLine(CStr(x + 1) & "," & CStr(K))
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work()
        End Sub
        """;
    internal const string CP5Expected = "seed\n6,15";

    /// <summary>CP6 — the same shape as CP5 with a MODULE-level global instead of a field. Before
    /// #146: <c>seed | 103,3,101</c> (x's fact survived Bump()). Correct is
    /// <c>seed | 3,3,101</c>.</summary>
    internal const string CP6 = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Dim G As Integer = 0

        Sub Bump()
            G = G + 100
        End Sub

        Sub Main()
            Dim x As Integer
            Dim y As Integer
            G = Seed(1)
            x = G
            y = x * 3
            Bump()
            Console.WriteLine(CStr(x + 2) & "," & CStr(y) & "," & CStr(G))
        End Sub
        """;
    internal const string CP6Expected = "seed\n3,3,101";

    /// <summary>CP7 — a ByRef PARAMETER aliased to the global it kills. Before #146:
    /// <c>6 | 105</c> (n's fact — recorded via a plain IRAssignment, no ByRef argument in sight
    /// from INSIDE Work — survived Touch(), which the private rules could not see writes the
    /// SAME storage through the alias). Correct is <c>106 | 105</c>. JavaScript refuses ByRef
    /// outright (BL7002) and is pinned separately below.</summary>
    internal const string CP7 = """
        Dim G As Integer = 0

        Sub Touch()
            G = G + 100
        End Sub

        Sub Work(ByRef n As Integer)
            n = 5
            Touch()
            Console.WriteLine(CStr(n + 1))
        End Sub

        Sub Main()
            Work(G)
            Console.WriteLine(CStr(G))
        End Sub
        """;
    internal const string CP7Expected = "106\n105";

    /// <summary>CP8 — <c>Me.K = 7</c> (IRFieldStore) redefining the SAME member a bare <c>K</c>
    /// read names. Before #146: 6 (K's bare-name fact survived its OWN qualified redefinition —
    /// CopyPropagation's private rules never named a member store's target at all). Correct is
    /// 8. Unaffected by which backend: not a call-visibility question, a plain same-block
    /// redefinition the private rules missed structurally.</summary>
    internal const string CP8 = """
        Class Box
            Public K As Integer

            Sub Work()
                K = 5
                Me.K = 7
                Console.WriteLine(CStr(K + 1))
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work()
        End Sub
        """;
    internal const string CP8Expected = "8";

    /// <summary>CP9 — the property-Setter half of the same idea: <c>Me.P = 10</c> lowers to an
    /// IRFieldStore (ADR-0007) whose Setter body writes the DIFFERENTLY-named field K.
    /// Before #146: 6. Correct is 21 (P's setter doubles its argument: K = 10 * 2 = 20, K + 1 =
    /// 21). C++ cannot build ANY Get/Set property (task #148 — not task #141 as an earlier
    /// analysis pass mislabeled it; #141 is the unrelated MyBase-Exception family) and is pinned
    /// separately below.</summary>
    internal const string CP9 = """
        Class Box
            Public K As Integer

            Property P As Integer
                Get
                    Return K
                End Get
                Set(value As Integer)
                    K = value * 2
                End Set
            End Property

            Sub Work()
                K = 5
                Me.P = 10
                Console.WriteLine(CStr(K + 1))
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work()
        End Sub
        """;
    internal const string CP9Expected = "21";

    /// <summary>CP10 — the ByRef arm's OWN over-kill control seen from the OTHER side: the write
    /// through the ByRef parameter <c>n</c> happens BEFORE the plain global write <c>G = 9</c>,
    /// so this is n's kill of ITS OWN just-recorded fact — <c>n = 5</c> must still see itself.
    /// Before #146: 6 (over-kill — the private rules invalidated the very record they had just
    /// created). Correct is 10.</summary>
    internal const string CP10 = """
        Dim G As Integer = 0

        Sub Work(ByRef n As Integer)
            n = 5
            G = 9
            Console.WriteLine(CStr(n + 1))
        End Sub

        Sub Main()
            Work(G)
        End Sub
        """;
    internal const string CP10Expected = "10";

    /// <summary>CP11 — the mirror of CP10: the plain global write happens FIRST, the ByRef write
    /// second, and the later read is of the GLOBAL (<c>G + 1</c>), which the ByRef write to
    /// <c>n</c> (aliased to <c>G</c>) must invalidate. Before #146: 6 (G's fact survived a write
    /// through its own ByRef alias — the private rules' ByRef arm only fired for a variable
    /// PASSED as a ByRef argument, never for the aliasing this shape exercises). Correct is
    /// 10.</summary>
    internal const string CP11 = """
        Dim G As Integer = 0

        Sub Work(ByRef n As Integer)
            G = 5
            n = 9
            Console.WriteLine(CStr(G + 1))
        End Sub

        Sub Main()
            Work(G)
        End Sub
        """;
    internal const string CP11Expected = "10";

    /// <summary>CPI_cpp/CPI_csharp/CPI_javascript — IRInlineCode is Universal (D1): raw
    /// target-language text can assign ANY variable, so a copy fact recorded before an inline
    /// block must not survive it. Each probe's inline block is written in, and can only build on,
    /// its OWN matching backend. Before #146: 3 (p's fact survived the inline write on every
    /// backend that could build it at all). Correct is 102.</summary>
    internal const string CPI_cpp = """
        Sub Main()
            Dim p As Integer
            p = 1
            cpp{ p = 100; }
            Console.WriteLine(CStr(p + 2))
        End Sub
        """;
    internal const string CPI_csharp = """
        Sub Main()
            Dim p As Integer
            p = 1
            csharp{ p = 100; }
            Console.WriteLine(CStr(p + 2))
        End Sub
        """;
    internal const string CPI_javascript = """
        Sub Main()
            Dim p As Integer
            p = 1
            javascript{ p = 100; }
            Console.WriteLine(CStr(p + 2))
        End Sub
        """;
    internal const string CPIExpected = "102";

    /// <summary>CPC — a STRUCTURAL control, not an execution probe: a plain DECLARED LOCAL's copy
    /// fact, which <c>Touch()</c> (writes only the global <c>G</c>) cannot reach either before or
    /// after #146. Must still fold to the literal 6 — this is the anti-vacuity partner to every
    /// probe above, proving the fix does not turn into "kill everything across every call".
    /// </summary>
    internal const string CPC = """
        Dim G As Integer = 0

        Sub Touch()
            G = G + 100
        End Sub

        Sub Main()
            Dim x As Integer
            x = 5
            Touch()
            Console.WriteLine(CStr(x + 1) & "," & CStr(G))
        End Sub
        """;

    /// <summary>CPK — the same anti-vacuity idea for the CONST exemption: a declared local's fact
    /// holding a Const GLOBAL must still propagate across a call (nothing, call included, can
    /// write a Const).</summary>
    internal const string CPK = """
        Const LIMIT As Integer = 7

        Sub Touch()
            Console.WriteLine("t")
        End Sub

        Sub Main()
            Dim x As Integer
            x = LIMIT
            Touch()
            Console.WriteLine(CStr(x + 1))
        End Sub
        """;
}

/// <summary>
/// End-to-end: all four backends, both pipelines, verified against
/// <c>S/cp146/{probes,extra}/matrix-fix.txt</c> (this exact working tree, task #146 applied) AND
/// <c>matrix-base.txt</c> (before) cell by cell. The JavaScript "standard" leg ALWAYS goes through
/// <see cref="JavaScriptOptimizedExecutionTests.RunOptimized"/> (AddStandardPasses, which contains
/// CopyPropagationPass) — never <see cref="FourBackends.RunsOnEveryBackend"/> /
/// <see cref="JavaScriptExecutionTests.RunJs"/>, which runs NO optimizer and would print the
/// correct answer whether or not #146 is fixed, silently certifying nothing (the same trap CP1's
/// own doc comment names). The aggressive leg uses <see cref="FourBackends.RunAggressiveJs"/>,
/// which IS the aggressive pipeline and needs no such substitution.
/// </summary>
[TestFixture]
[Category("Integration")]   // compiles/runs C++, spawns node, assembles/runs IL
[NonParallelizable]         // the C# leg redirects Console.Out (see FourBackends)
public class CopyPropagationSharedVocabularyExecutionTests
{
    // ---- CP2: C# + JavaScript RIGHT; C++ KNOWN-WRONG (task #140); MSIL cannot build (task #155) ----

    [Test]
    public void CP2_StandardPipeline_CSharpAndJavaScript()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(CopyPropagationSharedVocabularyProbes.CP2)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP2Expected), "C#, standard");
            Assert.That(FourBackends.Norm(JavaScriptOptimizedExecutionTests.RunOptimized(CopyPropagationSharedVocabularyProbes.CP2)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP2Expected), "JavaScript, standard");
        });

    [Test]
    public void CP2_AggressivePipeline_CSharpAndJavaScript()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(CopyPropagationSharedVocabularyProbes.CP2)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP2Expected), "C#, aggressive");
            Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(CopyPropagationSharedVocabularyProbes.CP2)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP2Expected), "JavaScript, aggressive");
        });

    /// <summary>C++'s own lambda lowering captures BY COPY (<c>[=]</c>), not by reference — task
    /// #140, MEASURED present even with NO optimizer pass running at all (matches
    /// LicmKillVocabularyTests' L5 pin for the identical backend defect). Not a kill-vocabulary
    /// defect task #146 could ever have closed; pinned here so a regression (or a fix) is
    /// caught.</summary>
    [Test]
    public void CP2_Cpp_KnownWrong_PinnedForTask140_StandardPipeline()
        => Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(CopyPropagationSharedVocabularyProbes.CP2))),
            Is.EqualTo("3,3"),
            "task #140 (C++ backend capture-by-copy) — if this changed, re-measure before touching it.");

    [Test]
    public void CP2_Cpp_KnownWrong_PinnedForTask140_AggressivePipeline()
        => Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(CopyPropagationSharedVocabularyProbes.CP2))),
            Is.EqualTo("3,3"),
            "task #140, aggressive pipeline — same backend defect, unrelated to LICM or CopyPropagation.");

    /// <summary>MSIL has no lowering for the delegate type a <c>Sub()</c> lambda gets typed as —
    /// task #155 ("Reference to undefined class 'Action'", ilasm), a pre-existing gap this pass
    /// never touches. Matches LicmKillVocabularyTests' identical pin for the same MSIL gap on a
    /// different probe (there tracked as task #122).</summary>
    [Test]
    public void CP2_Msil_CannotBuild_PinnedForTask155_StandardPipeline()
    {
        var run = MsilHarness.Run(CopyPropagationSharedVocabularyProbes.CP2, aggressive: false);
        Assert.That(run.Outcome, Is.EqualTo(MsilHarness.MsilOutcome.AssembleFailed), run.Report);
        Assert.That(run.Detail, Does.Contain("Action"), "task #155 — MSIL has no lowering for the "
            + "delegate type a Sub() lambda gets typed as.");
    }

    [Test]
    public void CP2_Msil_CannotBuild_PinnedForTask155_AggressivePipeline()
    {
        var run = MsilHarness.Run(CopyPropagationSharedVocabularyProbes.CP2, aggressive: true);
        Assert.That(run.Outcome, Is.EqualTo(MsilHarness.MsilOutcome.AssembleFailed), run.Report);
        Assert.That(run.Detail, Does.Contain("Action"), "task #155, aggressive pipeline — same gap.");
    }

    // ---- CP5, CP6: all four backends agree, both pipelines --------------------------------------

    [Test]
    public void CP5_StandardPipeline_AllFourBackends()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(CopyPropagationSharedVocabularyProbes.CP5)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP5Expected), "C#, standard");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(CopyPropagationSharedVocabularyProbes.CP5))),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP5Expected), "C++, standard");
            Assert.That(FourBackends.Norm(JavaScriptOptimizedExecutionTests.RunOptimized(CopyPropagationSharedVocabularyProbes.CP5)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP5Expected), "JavaScript, standard");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(CopyPropagationSharedVocabularyProbes.CP5)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP5Expected), "MSIL, standard");
        });

    [Test]
    public void CP5_AggressivePipeline_AllFourBackends()
        => FourBackends.RunsOnEveryBackendAggressive(CopyPropagationSharedVocabularyProbes.CP5, CopyPropagationSharedVocabularyProbes.CP5Expected);

    [Test]
    public void CP6_StandardPipeline_AllFourBackends()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(CopyPropagationSharedVocabularyProbes.CP6)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP6Expected), "C#, standard");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(CopyPropagationSharedVocabularyProbes.CP6))),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP6Expected), "C++, standard");
            Assert.That(FourBackends.Norm(JavaScriptOptimizedExecutionTests.RunOptimized(CopyPropagationSharedVocabularyProbes.CP6)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP6Expected), "JavaScript, standard");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(CopyPropagationSharedVocabularyProbes.CP6)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP6Expected), "MSIL, standard");
        });

    [Test]
    public void CP6_AggressivePipeline_AllFourBackends()
        => FourBackends.RunsOnEveryBackendAggressive(CopyPropagationSharedVocabularyProbes.CP6, CopyPropagationSharedVocabularyProbes.CP6Expected);

    // ---- CP7, CP10, CP11: C#/C++/MSIL agree; JavaScript refuses ByRef outright (BL7002) ---------

    [Test]
    public void CP7_StandardPipeline_CSharpCppMsil()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(CopyPropagationSharedVocabularyProbes.CP7)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP7Expected), "C#, standard");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(CopyPropagationSharedVocabularyProbes.CP7))),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP7Expected), "C++, standard");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(CopyPropagationSharedVocabularyProbes.CP7)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP7Expected), "MSIL, standard");
        });

    [Test]
    public void CP7_AggressivePipeline_CSharpCppMsil()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(CopyPropagationSharedVocabularyProbes.CP7)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP7Expected), "C#, aggressive");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(CopyPropagationSharedVocabularyProbes.CP7))),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP7Expected), "C++, aggressive");
            Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(CopyPropagationSharedVocabularyProbes.CP7)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP7Expected), "MSIL, aggressive");
        });

    /// <summary>⛔ Structural, not a value assertion — matches
    /// <c>CseDestinationInvalidationTests.D4_JavaScript_RefusesByRef_BL7002</c>'s convention.
    /// JavaScript has no reference parameters at all and refuses <c>ByRef</c> at codegen
    /// regardless of what CopyPropagation decided.</summary>
    [Test]
    public void CP7_JavaScript_RefusesByRef_BL7002()
    {
        var module = JsTestSupport.BuildModule(CopyPropagationSharedVocabularyProbes.CP7);
        var ex = Assert.Throws<BasicLang.Compiler.CodeGen.ForeignFeatureException>(
            () => new BasicLang.Compiler.CodeGen.JavaScript.JavaScriptCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("BL7002"));
        Assert.That(ex.Message, Does.Contain("ByRef"));
    }

    [Test]
    public void CP10_StandardPipeline_CSharpCppMsil()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(CopyPropagationSharedVocabularyProbes.CP10)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP10Expected), "C#, standard");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(CopyPropagationSharedVocabularyProbes.CP10))),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP10Expected), "C++, standard");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(CopyPropagationSharedVocabularyProbes.CP10)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP10Expected), "MSIL, standard");
        });

    [Test]
    public void CP10_AggressivePipeline_CSharpCppMsil()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(CopyPropagationSharedVocabularyProbes.CP10)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP10Expected), "C#, aggressive");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(CopyPropagationSharedVocabularyProbes.CP10))),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP10Expected), "C++, aggressive");
            Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(CopyPropagationSharedVocabularyProbes.CP10)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP10Expected), "MSIL, aggressive");
        });

    [Test]
    public void CP10_JavaScript_RefusesByRef_BL7002()
    {
        var module = JsTestSupport.BuildModule(CopyPropagationSharedVocabularyProbes.CP10);
        var ex = Assert.Throws<BasicLang.Compiler.CodeGen.ForeignFeatureException>(
            () => new BasicLang.Compiler.CodeGen.JavaScript.JavaScriptCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("BL7002"));
        Assert.That(ex.Message, Does.Contain("ByRef"));
    }

    [Test]
    public void CP11_StandardPipeline_CSharpCppMsil()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(CopyPropagationSharedVocabularyProbes.CP11)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP11Expected), "C#, standard");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(CopyPropagationSharedVocabularyProbes.CP11))),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP11Expected), "C++, standard");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(CopyPropagationSharedVocabularyProbes.CP11)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP11Expected), "MSIL, standard");
        });

    [Test]
    public void CP11_AggressivePipeline_CSharpCppMsil()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(CopyPropagationSharedVocabularyProbes.CP11)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP11Expected), "C#, aggressive");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(CopyPropagationSharedVocabularyProbes.CP11))),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP11Expected), "C++, aggressive");
            Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(CopyPropagationSharedVocabularyProbes.CP11)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP11Expected), "MSIL, aggressive");
        });

    [Test]
    public void CP11_JavaScript_RefusesByRef_BL7002()
    {
        var module = JsTestSupport.BuildModule(CopyPropagationSharedVocabularyProbes.CP11);
        var ex = Assert.Throws<BasicLang.Compiler.CodeGen.ForeignFeatureException>(
            () => new BasicLang.Compiler.CodeGen.JavaScript.JavaScriptCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("BL7002"));
        Assert.That(ex.Message, Does.Contain("ByRef"));
    }

    // ---- CP8: all four backends, both pipelines --------------------------------------------------

    [Test]
    public void CP8_StandardPipeline_AllFourBackends()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(CopyPropagationSharedVocabularyProbes.CP8)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP8Expected), "C#, standard");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(CopyPropagationSharedVocabularyProbes.CP8))),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP8Expected), "C++, standard");
            Assert.That(FourBackends.Norm(JavaScriptOptimizedExecutionTests.RunOptimized(CopyPropagationSharedVocabularyProbes.CP8)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP8Expected), "JavaScript, standard");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(CopyPropagationSharedVocabularyProbes.CP8)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP8Expected), "MSIL, standard");
        });

    [Test]
    public void CP8_AggressivePipeline_AllFourBackends()
        => FourBackends.RunsOnEveryBackendAggressive(CopyPropagationSharedVocabularyProbes.CP8, CopyPropagationSharedVocabularyProbes.CP8Expected);

    // ---- CP9: C#/JavaScript/MSIL; C++ cannot build ANY Get/Set property (task #148) --------------

    [Test]
    public void CP9_StandardPipeline_CSharpJavaScriptMsil()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(CopyPropagationSharedVocabularyProbes.CP9)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP9Expected), "C#, standard");
            Assert.That(FourBackends.Norm(JavaScriptOptimizedExecutionTests.RunOptimized(CopyPropagationSharedVocabularyProbes.CP9)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP9Expected), "JavaScript, standard");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(CopyPropagationSharedVocabularyProbes.CP9)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP9Expected), "MSIL, standard");
        });

    [Test]
    public void CP9_AggressivePipeline_CSharpJavaScriptMsil()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(CopyPropagationSharedVocabularyProbes.CP9)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP9Expected), "C#, aggressive");
            Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(CopyPropagationSharedVocabularyProbes.CP9)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP9Expected), "JavaScript, aggressive");
            Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(CopyPropagationSharedVocabularyProbes.CP9)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP9Expected), "MSIL, aggressive");
        });

    /// <summary>task #148 ("no member named 'P' in 'Box'") — NOT task #141 as an earlier pass
    /// mislabeled it; #141 is the unrelated MyBase/Exception family (ADR-0006 D1's own
    /// implementation note). Matches <c>BarePropertyLoweringTests.P4_Cpp_StillDoesNotBuild_Task148</c>'s
    /// pattern exactly — same underlying gap, different probe.</summary>
    [Test]
    public void CP9_Cpp_CannotBuild_PinnedForTask148_StandardPipeline()
    {
        var ex = Assert.Throws<AssertionException>(
            () => BclE2E.CompileRun(BclE2E.CompileToCppOptimized(CopyPropagationSharedVocabularyProbes.CP9)));
        Assert.That(ex!.Message, Does.Contain("C++ compilation failed"),
            "expected a COMPILE failure (task #148) — if this now builds, re-measure before "
            + "widening this pin.");
    }

    [Test]
    public void CP9_Cpp_CannotBuild_PinnedForTask148_AggressivePipeline()
    {
        var ex = Assert.Throws<AssertionException>(
            () => BclE2E.CompileRun(BclE2E.CompileToCppAggressive(CopyPropagationSharedVocabularyProbes.CP9)));
        Assert.That(ex!.Message, Does.Contain("C++ compilation failed"),
            "expected a COMPILE failure (task #148), aggressive pipeline — same gap.");
    }

    // ---- CPI_cpp / CPI_csharp / CPI_javascript: IRInlineCode is Universal, each on its own backend

    [Test]
    public void CPI_cpp_StandardAndAggressivePipelines()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(CopyPropagationSharedVocabularyProbes.CPI_cpp))),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CPIExpected), "C++, standard");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(CopyPropagationSharedVocabularyProbes.CPI_cpp))),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CPIExpected), "C++, aggressive");
        });

    [Test]
    public void CPI_csharp_StandardAndAggressivePipelines()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(CopyPropagationSharedVocabularyProbes.CPI_csharp)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CPIExpected), "C#, standard");
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(CopyPropagationSharedVocabularyProbes.CPI_csharp)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CPIExpected), "C#, aggressive");
        });

    [Test]
    public void CPI_javascript_StandardAndAggressivePipelines()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(JavaScriptOptimizedExecutionTests.RunOptimized(CopyPropagationSharedVocabularyProbes.CPI_javascript)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CPIExpected), "JavaScript, standard");
            Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(CopyPropagationSharedVocabularyProbes.CPI_javascript)),
                Is.EqualTo(CopyPropagationSharedVocabularyProbes.CPIExpected), "JavaScript, aggressive");
        });

    // ---- One CLI entry-point leg for C#, one for MSIL (CLAUDE.md: "test both entry points") ------
    //      Matches CseDestinationInvalidationTests.RunThroughEntryPoint's convention: BasicCompiler
    //      with OptimizeAggressive=true is what the CLI's --optimize and a Release .blproj build
    //      both request (Compiler.cs). CP2 for C# (the flagship closure shape); CP6 for MSIL (CP2
    //      itself cannot build there — task #155 — so a probe that DOES build stands in).

    [Test]
    public void CP2_TheCliSingleFileEntryPoint_CSharp()
        => Assert.That(RunCliCSharp(CopyPropagationSharedVocabularyProbes.CP2, asProject: false),
            Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP2Expected),
            "BasicCompiler.CompileFile (the CLI single-file entry point) printed the wrong answer.");

    [Test]
    public void CP2_TheProjectBuildEntryPoint_CSharp()
        => Assert.That(RunCliCSharp(CopyPropagationSharedVocabularyProbes.CP2, asProject: true),
            Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP2Expected),
            "BasicCompiler.CompileProjectFiles (the IDE build's entry point) printed the wrong answer.");

    [Test]
    public void CP6_TheCliSingleFileEntryPoint_Msil()
        => Assert.That(RunCliMsil(CopyPropagationSharedVocabularyProbes.CP6),
            Is.EqualTo(CopyPropagationSharedVocabularyProbes.CP6Expected),
            "BasicCompiler.CompileFile's CombinedIR, run through the MSIL backend, printed the wrong answer.");

    private static string RunCliCSharp(string source, bool asProject)
    {
        var dir = Path.Combine(Path.GetTempPath(), "BasicLang_CP146Cli_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "prog.bas");
            File.WriteAllText(file, source);

            var compiler = new BasicCompiler(new CompilerOptions { OptimizeAggressive = true });
            var result = asProject ? compiler.CompileProjectFiles(new[] { file }) : compiler.CompileFile(file);

            Assert.That(result.HasErrors, Is.False, string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            Assert.That(result.CombinedIR, Is.Not.Null, "the entry point produced no combined IR");

            return FourBackends.Norm(FourBackends.RunEmittedCSharpText(
                new BasicLang.Compiler.CodeGen.CSharp.ImprovedCSharpCodeGenerator().Generate(result.CombinedIR)));
        }
        finally { try { Directory.Delete(dir, true); } catch { /* temp */ } }
    }

    private static string RunCliMsil(string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "BasicLang_CP146CliMsil_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "prog.bas");
            File.WriteAllText(file, source);

            var compiler = new BasicCompiler(new CompilerOptions { OptimizeAggressive = true });
            var result = compiler.CompileFile(file);

            Assert.That(result.HasErrors, Is.False, string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            Assert.That(result.CombinedIR, Is.Not.Null, "the entry point produced no combined IR");

            var il = new BasicLang.Compiler.CodeGen.MSIL.MSILCodeGenerator().Generate(result.CombinedIR);
            return FourBackends.Norm(MsilHarness.RunIlExpectingSuccess(il));
        }
        finally { try { Directory.Delete(dir, true); } catch { /* temp */ } }
    }
}

/// <summary>
/// Structural controls (standard pipeline, IR inspected directly — no backend, no process, not
/// Integration): the anti-vacuity partners to every execution probe above. Both shapes were
/// ALREADY correct before #146 (a call-invisible local's fact, and a Const-valued fact, were never
/// touched by CopyPropagation's OLD private rules either — neither is call-visible under the NEW
/// shared vocabulary), so these pin that the fix does not overreach into "kill everything across
/// every call". Verified against both <c>S/cp146/dll-fix</c> and <c>S/cp146/dll-base</c> via the
/// implementer's own <c>tool ir … std</c> dump: byte-identical output on both trees.
/// </summary>
[TestFixture]
public class CopyPropagationSharedVocabularyStructuralTests
{
    private static IRFunction MainOf(IRModule module) => module.Functions.Single(f => f.Name == "Main");

    private static IRModule BuildStandard(string source)
    {
        var module = JsTestSupport.BuildModule(source, sourceFilePath: "prog.bas");
        var pipeline = new OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.Run(module);
        return module;
    }

    /// <summary>CPC — <c>x = 5 : Touch() : CStr(x + 1)</c>, x a plain DECLARED LOCAL Touch()
    /// (which writes only the global G) cannot reach. CopyPropagation's fact <c>x -&gt; 5</c> must
    /// survive the call and ConstantFolding must fold <c>x + 1</c> down to the literal 6.
    /// <c>OptimizationPass.IsCallVisible(x, Main)</c> is False: x is a declared local, not Const,
    /// not Global, and Main contains no lambda.</summary>
    [Test]
    public void CPC_DeclaredLocalFact_StillFoldsTo6AcrossACall()
    {
        var module = BuildStandard(CopyPropagationSharedVocabularyProbes.CPC);
        var block = MainOf(module).Blocks.Single();

        var six = block.Instructions.OfType<IRConstant>()
            .Any(c => c.Value is int i && i == 6);

        Assert.That(six, Is.True,
            "expected x + 1 to survive CopyPropagation and be folded to the literal 6 by "
            + "ConstantFolding — a call-invisible declared local's fact must not be killed by an "
            + "unrelated call, however the shared vocabulary's call arm is implemented.");
    }

    /// <summary>CPK — <c>x = LIMIT : Touch() : CStr(x + 1)</c>, x's fact holding a Const GLOBAL.
    /// <c>OptimizationPass.ReadsCallVisible(LIMIT, Main)</c> is False (the Const exemption — a
    /// call cannot change a Const, independent of the declarations rule that governs every other
    /// global), so the fact must survive and be substituted in: the add's left operand becomes
    /// the LIMIT variable itself, still carrying its Const flag.</summary>
    [Test]
    public void CPK_ConstGlobalFact_StillPropagatesAcrossACall()
    {
        var module = BuildStandard(CopyPropagationSharedVocabularyProbes.CPK);
        var block = MainOf(module).Blocks.Single();

        var add = block.Instructions.OfType<IRBinaryOp>()
            .FirstOrDefault(b => b.Operation == BinaryOpKind.Add);

        Assert.That(add, Is.Not.Null,
            "expected an Add instruction computing x + 1 — if LIMIT is now folded to a literal at "
            + "IR-build time, re-measure before widening this pin.");
        Assert.That(add!.Left, Is.InstanceOf<IRVariable>(),
            "the fact x -> LIMIT must have been SUBSTITUTED into the add, not left as x");

        var left = (IRVariable)add.Left;
        Assert.That(left.Name, Is.EqualTo("LIMIT"), "the substituted operand must be LIMIT");
        Assert.That(left.IsConst, Is.True, "the propagated operand must still carry its Const flag");
    }
}

/// <summary>
/// Hand-built IR, fast tier (no front end, no backend — pure <c>CopyPropagationPass.Run</c>).
/// Ported verbatim from the implementer's evidence (<c>S/cp146/unit/Program.cs</c>), each case
/// isolating exactly ONE arm of <c>CopyPropagationPass.Invalidate</c> /
/// <c>OptimizationPass.NamesWrittenBy</c> / <c>IsCallVisible</c> / <c>ReadsCallVisible</c>.
///
/// <para>Each case runs one instruction stream ending in a compare against the probe value and
/// reports whether the compare's LEFT operand is still the SAME REFERENCE (the fact was correctly
/// INVALIDATED — no substitution happened) or was REPLACED (the fact survived and its value was
/// substituted in). VERIFIED both directions against the implementer's own dll-fix/dll-base
/// snapshots (S/cp146/{dll-fix,dll-base}): base FAILS U4, U5, U6, U7 and U10 (each prints
/// "PROPAGATED" where "KEPT" — i.e. correctly invalidated — is right); the fix PASSES all
/// ten.</para>
/// </summary>
[TestFixture]
public class CopyPropagationSharedVocabularyUnitTests
{
    private static readonly TypeInfo I = new("Integer", TypeKind.Primitive);
    private static readonly TypeInfo B = new("Boolean", TypeKind.Primitive);
    private static readonly TypeInfo V = new("Void", TypeKind.Primitive);

    /// <summary>Builds one function, runs <paramref name="build"/> to append instructions and
    /// return the probe value, appends a compare of that probe against 42, runs
    /// <see cref="CopyPropagationPass"/> once, and reports whether the compare's left operand is
    /// still the SAME reference as the probe (true = KEPT/invalidated) or was substituted (false =
    /// PROPAGATED).</summary>
    private static bool Kept(Func<IRFunction, BasicBlock, IRValue> build, Action<IRFunction> declare = null)
    {
        var fn = new IRFunction("f", B);
        var block = fn.CreateBlock("entry");
        declare?.Invoke(fn);
        var probe = build(fn, block);
        var cmp = new IRCompare("tc", CompareKind.Eq, probe, new IRConstant(42, I), B);
        block.Instructions.Add(cmp);

        var module = new IRModule("m");
        module.Functions.Add(fn);
        new CopyPropagationPass().Run(module);

        return ReferenceEquals(cmp.Left, probe);
    }

    [Test]
    public void U1_RenamedRedefinition_NotACall_StillInvalidates()
    {
        var x = new IRVariable("x", I);
        var kept = Kept((f, b) =>
        {
            b.Instructions.Add(new IRAssignment(x, new IRConstant(5, I)));
            // x += 1 with no IRAssignment — a RENAMED IRBinaryOp writing x directly.
            b.Instructions.Add(new IRBinaryOp("x", BinaryOpKind.Add, x, new IRConstant(1, I), I) { NamedAfterVariable = true });
            return x;
        }, f => f.LocalVariables.Add(x));

        Assert.That(kept, Is.True, "a renamed-value redefinition of x (no IRAssignment, no call "
            + "involved) must invalidate the earlier fact through the plain name-kill arm.");
    }

    [Test]
    public void U2_ByRefArgument_Declared_OnlyByRefArmCanKill()
    {
        var n = new IRVariable("n", I);
        var kept = Kept((f, b) =>
        {
            b.Instructions.Add(new IRAssignment(n, new IRConstant(0, I)));
            var call = new IRCall("t0", "SetIt", V);
            call.Arguments.Add(n);
            call.ByRefArguments.Add(true);
            b.Instructions.Add(call);
            return n;
        }, f => f.LocalVariables.Add(n));

        Assert.That(kept, Is.True, "n passed BY REF must invalidate its fact — the ByRef-names arm, "
            + "not the call-visibility arm (n is a plain declared local, otherwise private).");
    }

    [Test]
    public void U3_ByValArgument_Declared_OverKillControl_StillPropagates()
    {
        var n = new IRVariable("n", I);
        var kept = Kept((f, b) =>
        {
            b.Instructions.Add(new IRAssignment(n, new IRConstant(0, I)));
            var call = new IRCall("t0", "Observe", V);
            call.Arguments.Add(n);
            call.ByRefArguments.Add(false); // BY VALUE — the callee cannot write n
            b.Instructions.Add(call);
            return n;
        }, f => f.LocalVariables.Add(n));

        Assert.That(kept, Is.False, "the anti-vacuity partner of U2: a BY-VALUE argument, n "
            + "otherwise a private declared local, must still propagate — a kill that fired on "
            + "every call argument regardless of ByRef would pass U2 while silently pessimizing "
            + "every call in the program.");
    }

    [Test]
    public void U4_KeyHalf_UndeclaredField_KilledAcrossACall()
    {
        var K = new IRVariable("K", I);
        var kept = Kept((f, b) =>
        {
            b.Instructions.Add(new IRAssignment(K, new IRConstant(5, I)));
            b.Instructions.Add(new IRCall("t0", "Inc", V));
            return K;
        }); // K is NOT declared — a field read bare.

        Assert.That(kept, Is.True, "an UNDECLARED name (a bare field read) defaults to call-"
            + "visible, so its own fact's KEY must be killed by the call — the call-visibility "
            + "arm on the fact's variable.");
    }

    [Test]
    public void U5_ValueHalf_DeclaredLocalReadingUndeclaredName_KilledAcrossACall()
    {
        var x = new IRVariable("x", I);
        var K = new IRVariable("K", I); // undeclared
        var kept = Kept((f, b) =>
        {
            b.Instructions.Add(new IRAssignment(x, K));
            b.Instructions.Add(new IRCall("t0", "Inc", V));
            return x;
        }, f => f.LocalVariables.Add(x));

        Assert.That(kept, Is.True, "x is a declared (private) local, but its RECORDED VALUE reads "
            + "the undeclared (call-visible) K — the call-visibility arm on the fact's VALUE "
            + "(ReadsCallVisible), not its key.");
    }

    [Test]
    public void U6_InlineCode_IsUniversal_KillsADeclaredLocalFact()
    {
        var x = new IRVariable("x", I);
        var kept = Kept((f, b) =>
        {
            b.Instructions.Add(new IRAssignment(x, new IRConstant(1, I)));
            b.Instructions.Add(new IRInlineCode("csharp", "x = 100;"));
            return x;
        }, f => f.LocalVariables.Add(x));

        Assert.That(kept, Is.True, "IRInlineCode is Universal (raw target-language text can "
            + "assign ANY variable) — every fact must die, including a private declared local's.");
    }

    /// <summary>
    /// Since task #122 (ADR-0006 D1's Obligation, DISCHARGED), <c>IsCallVisible</c> asks whether
    /// <c>x</c> is in the function's RECORDED capture set — but this <c>IRFunction</c> is
    /// hand-built, so nothing ever recorded one (<c>LambdaCapturedNames</c>/
    /// <c>LambdaCaptureSources</c> both stay null). That is exactly the FALLBACK case: a function
    /// referencing <c>__lambda_0</c> with no recorded set falls all the way back to D1's original
    /// "every local" rule, so <c>x</c> — never captured by anything real here, since there is no
    /// real lambda body at all — still ends up call-visible. This is the canonical hand-built pin
    /// for that fallback; <c>LambdaCaptureSetFallbackTests</c> (LambdaCaptureSetTests.cs) covers
    /// the same fallback reached from REAL front-end source instead.
    /// </summary>
    [Test]
    public void U7_ClosureRule_MakesADeclaredLocalFact_CallVisible()
    {
        var x = new IRVariable("x", I);
        var bump = new IRVariable("bump", I);
        var kept = Kept((f, b) =>
        {
            // A reference to the IRBuilder lambda spelling, marking this function as one that
            // "contains a lambda" — but with NO recorded capture set (hand-built IR), so
            // IsCallVisible falls back to ADR-0006 D1's original "every local" rule.
            b.Instructions.Add(new IRAssignment(bump, new IRVariable("__lambda_0", I)));
            b.Instructions.Add(new IRAssignment(x, new IRConstant(1, I)));
            b.Instructions.Add(new IRCall("t0", "bump", V));
            return x;
        }, f => { f.LocalVariables.Add(x); f.LocalVariables.Add(bump); });

        Assert.That(kept, Is.True, "the function references __lambda_0 but has no RECORDED "
            + "capture set (hand-built IR) -- IsCallVisible falls back to D1's original rule, "
            + "every local call-visible, so a subsequent call must invalidate x's fact.");
    }

    [Test]
    public void U8_ConstGlobalValue_Exempt_StillPropagates()
    {
        var x = new IRVariable("x", I);
        var kept = Kept((f, b) =>
        {
            b.Instructions.Add(new IRAssignment(x, new IRVariable("LIMIT", I) { IsGlobal = true, IsConst = true }));
            b.Instructions.Add(new IRCall("t0", "Touch", V));
            return x;
        }, f => f.LocalVariables.Add(x));

        Assert.That(kept, Is.False, "x's fact records a Const GLOBAL — nothing, call included, "
            + "can write a Const, so the fact must survive and propagate even though the call "
            + "arm ran.");
    }

    [Test]
    public void U9_LocalConstantValue_KeyHalfOverKillControl_StillPropagates()
    {
        var x = new IRVariable("x", I);
        var kept = Kept((f, b) =>
        {
            b.Instructions.Add(new IRAssignment(x, new IRConstant(5, I)));
            b.Instructions.Add(new IRCall("t0", "Touch", V));
            return x;
        }, f => f.LocalVariables.Add(x));

        Assert.That(kept, Is.False, "the key half's own over-kill control: x is a declared "
            + "(private) local and its value is a bare literal — neither half of the call arm "
            + "should fire, so the fact must survive a plain call.");
    }

    [Test]
    public void U10_MemberStore_NamesItsMember_KillsABareFieldFact()
    {
        var K = new IRVariable("K", I); // undeclared — a field
        var kept = Kept((f, b) =>
        {
            b.Instructions.Add(new IRAssignment(K, new IRConstant(5, I)));
            // Me.K = 7 — IRFieldStore names its member "K" directly.
            b.Instructions.Add(new IRFieldStore(new IRVariable("Me", I), "K", new IRConstant(7, I)));
            return K;
        });

        Assert.That(kept, Is.True, "Me.K = 7 (IRFieldStore) must invalidate the earlier bare-name "
            + "fact for K through the member-store name-kill arm, independent of the call arm.");
    }
}
