using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.IR.Optimization;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  ADR-0005 D2 — Invariant S widened to S′: CSE guards a shared value's named DESTINATION, not
//  only its operands. A merge re-points a later duplicate's consumers at the surviving
//  instruction, and C++/JavaScript/MSIL materialise that instruction and read it back BY NAME —
//  so the value goes stale the moment its OWN destination is written, exactly as it does when an
//  operand is written. Task #125's C4 pins (StatementOperandUndeclaredTempFixTests) are the
//  measured regression this closes for the shape whose shared operands are both plain, replicable
//  locals — the one case ADR-0004 D2's replicability gate could not reach, because the gate only
//  ever excludes a NON-replicable OPERAND, never a stale DESTINATION.
//
//  ⛔ THIS FILE IS PART A OF TWO, COMMITTED SEPARATELY FROM THE VERIFIER (Part B, in
//  <c>IRVerifierTests.cs</c>). Do not merge their tests into one file — the two repair different
//  halves of the same ADR and are reviewed/reverted independently.
//
//  Everything here exercises <c>CommonSubexpressionEliminationPass.Candidate</c>'s new
//  <c>Destination</c> field and <c>OptimizationPass.IsCallVisibleDestination</c>, both in
//  <c>BasicLang/IROptimizer.cs</c>. See that file's doc comments for the exact kill vocabulary
//  (<c>NamesWrittenBy</c>) and its KNOWN GAPS list — the task #133 pins at the foot of this file
//  are exactly those gaps, as programs.
// ================================================================================================

/// <summary>
/// The shapes, as measured against the fixed tree in <c>scratchpad/f111g</c> (probe programs
/// <c>C4/C4b/C4n/D1/A3/A4/D4/K1</c>, each with a <c>.exp</c> file this class's expected strings
/// were taken from verbatim). Every shape seeds its shared operands from a side-effecting
/// <c>Seed</c> call so a merge that evaluates the wrong number of times is visible in the
/// transcript, not only in the final value — the same discriminator
/// <c>StatementOperandUndeclaredTempFixTests</c> and <c>Family111MaterialisationBehaviourTests</c>
/// use throughout.
/// </summary>
internal static class CseDestinationShapes
{
    /// <summary>
    /// ⭐ THE REPORTED SHAPE, restated. <c>a</c> and <c>l(0)</c> both compute <c>p + q</c>; CSE
    /// merges them onto one shared <c>IRBinaryOp</c> named <c>a</c> (its Destination); <c>a</c> is
    /// then reassigned (<c>a = Seed(0)</c>) BETWEEN the two occurrences. Before this fix, a
    /// backend that reads the merged value back by name — C++, JavaScript, MSIL — read <c>a</c>
    /// AFTER the reassignment and printed <c>0,0</c>; C# escaped it only because inline-always
    /// re-emits <c>p + q</c> as TEXT rather than trusting the name. Correct is <c>3,0</c> on every
    /// backend: <c>StatementOperandUndeclaredTempFixTests</c>' <c>CppStillReadsTheCseMergedVariable_
    /// PinnedForTask125</c> family (renamed in this change) pins that flip directly; this file adds
    /// the four-backend + both-entry-point coverage that pin did not carry.
    /// </summary>
    internal const string C4 = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Sub Main()
            Dim p As Integer = Seed(1)
            Dim q As Integer = Seed(2)
            Dim l As New List(Of Integer)()
            l.Add(0)
            Dim a As Integer = p + q
            a = Seed(0)
            l(0) = p + q
            Console.WriteLine(CStr(l(0)) & "," & CStr(a))
        End Sub
        """;

    internal const string C4Expected = "seed\nseed\nseed\n3,0";

    /// <summary>
    /// C4 with the reassignment made from a LITERAL (<c>a = 0</c>) instead of a call. Kept as its
    /// own shape because a literal reassignment goes through <c>IRAssignment</c> the same way
    /// <c>Candidate</c>'s destination kill does, but produces one FEWER "seed" line — a fixture
    /// that only ever saw the call-based reassignment could not distinguish "the kill fires on
    /// literal writes too" from "the kill only ever fires because it happens to coincide with a
    /// call".
    /// </summary>
    internal const string C4b = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Sub Main()
            Dim p As Integer = Seed(1)
            Dim q As Integer = Seed(2)
            Dim l As New List(Of Integer)()
            l.Add(0)
            Dim a As Integer = p + q
            a = 0
            l(0) = p + q
            Console.WriteLine(CStr(l(0)) & "," & CStr(a))
        End Sub
        """;

    internal const string C4bExpected = "seed\nseed\n3,0";

    /// <summary>
    /// C4 with BOTH occurrences NAMED — <c>Dim b As Integer = p + q</c> instead of an
    /// <c>IRArrayStore</c> — rather than one named and one a store operand. This is the shape
    /// that isolates <c>Candidate.Destination</c> from the four EmitOperand-era statement-operand
    /// sites entirely: neither occurrence is an array/indexer store, a <c>Yield</c> or a
    /// <c>For Each</c> collection, so nothing here could be rescued by
    /// <c>StatementOperandUndeclaredTempFixTests</c>' unconditional-<c>EmitExpression</c> fix even
    /// on C#. A merge is the ONLY way <c>b</c> could ever see the value CSE decided on.
    /// </summary>
    internal const string C4n = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Sub Main()
            Dim p As Integer = Seed(1)
            Dim q As Integer = Seed(2)
            Dim a As Integer = p + q
            a = Seed(0)
            Dim b As Integer = p + q
            Console.WriteLine(CStr(b) & "," & CStr(a))
        End Sub
        """;

    internal const string C4nExpected = "seed\nseed\nseed\n3,0";

    /// <summary>
    /// C4's exact body, run TWICE by wrapping it in a two-iteration counted <c>For</c> loop — the
    /// destination write and both uses all sit in the SAME block on every iteration, so this is
    /// the control that the destination guard is not a one-shot, per-function fix: it must apply
    /// on each re-entry to the block, not just the first.
    /// </summary>
    internal const string D1 = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Sub Main()
            Dim p As Integer = Seed(1)
            Dim q As Integer = Seed(2)
            Dim l As New List(Of Integer)()
            l.Add(0)
            For i As Integer = 1 To 2
                Dim a As Integer = p + q
                a = Seed(0)
                l(0) = p + q
                Console.WriteLine(CStr(l(0)) & "," & CStr(a))
            Next
        End Sub
        """;

    internal const string D1Expected = "seed\nseed\nseed\n3,0\nseed\n3,0";

    /// <summary>
    /// The destination is a MODULE-level global (<c>g</c>), written not by an assignment in this
    /// block but by a CALL, <c>ZeroG()</c>, whose body writes <c>g</c> — invisible as an
    /// <c>IRAssignment</c> to this function entirely. Only <c>IsCallVisibleDestination</c>'s
    /// "global → call-visible" arm can see this; mutant (a4) removing it from
    /// <c>Candidate.ReadsCallVisibleStorage</c> leaves this shape unguarded (see the mutant-kill
    /// section below).
    /// </summary>
    internal const string A3 = """
        Dim g As Integer

        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Sub ZeroG()
            g = 0
        End Sub

        Sub Main()
            Dim p As Integer = Seed(1)
            Dim q As Integer = Seed(2)
            Dim l As New List(Of Integer)()
            l.Add(0)
            g = p + q
            ZeroG()
            l(0) = p + q
            Console.WriteLine(CStr(l(0)) & "," & CStr(g))
        End Sub
        """;

    internal const string A3Expected = "seed\nseed\n3,0";

    /// <summary>
    /// Same call-visible-destination question, but the destination is a CLASS FIELD (<c>K</c>)
    /// written by an INSTANCE METHOD call (<c>Zero()</c>, called bare, resolving to <c>Me.Zero()</c>)
    /// rather than a module global by a free function. <c>IsCallVisibleDestination</c>'s fallback
    /// arm ("not declared as a parameter or local → call-visible") is what catches this, since
    /// <c>K</c> is neither.
    /// </summary>
    internal const string A4 = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class Box
            Public K As Integer

            Sub Zero()
                K = 0
            End Sub

            Sub Work(p As Integer, q As Integer)
                Dim l As New List(Of Integer)()
                l.Add(0)
                K = p + q
                Zero()
                l(0) = p + q
                Console.WriteLine(CStr(l(0)) & "," & CStr(K))
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work(Seed(1), Seed(2))
        End Sub
        """;

    internal const string A4Expected = "seed\nseed\n3,0";

    /// <summary>
    /// The destination is written through a <c>ByRef</c> ARGUMENT (<c>Bump(a)</c>), not a call
    /// that reaches storage the callee already knows about. <c>NamesWrittenBy</c>'s ByRef-argument
    /// arm (shared between CSE and <c>IRVerifier</c> per ADR-0005 D2) is what kills this one.
    /// ⛔ JavaScript refuses <c>ByRef</c> outright (BL7002) and is excluded from the value
    /// assertion — see the dedicated JS-refusal test below, matching
    /// <c>CseInvalidationAndKeyTests.CseBackends.ByRefShape</c> / <c>Family111...G7_JavaScript_
    /// RefusesByRef_BL7002</c>'s convention.
    /// </summary>
    internal const string D4 = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Sub Bump(ByRef n As Integer)
            n = 0
        End Sub

        Sub Main()
            Dim p As Integer = Seed(1)
            Dim q As Integer = Seed(2)
            Dim l As New List(Of Integer)()
            l.Add(0)
            Dim a As Integer = p + q
            Bump(a)
            l(0) = p + q
            Console.WriteLine(CStr(l(0)) & "," & CStr(a))
        End Sub
        """;

    internal const string D4Expected = "seed\nseed\n3,0";

    /// <summary>
    /// ⭐⭐ THE CONTROL THAT KEEPS EVERY SHAPE ABOVE HONEST: <c>a</c>'s destination is never
    /// reassigned anywhere in the block, so the merge is LEGAL and must still happen — a single
    /// <c>Add</c> instruction shared by both <c>a</c> and <c>l(0)</c>. See
    /// <see cref="CseDestinationDecisionTests"/> for why this can only be asserted from a bare
    /// <c>pass.Run</c>, never through the pipeline.
    /// </summary>
    internal const string K1 = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Sub Main()
            Dim p As Integer = Seed(1)
            Dim q As Integer = Seed(2)
            Dim l As New List(Of Integer)()
            l.Add(0)
            Dim a As Integer = p + q
            l(0) = p + q
            Console.WriteLine(CStr(l(0)) & "," & CStr(a))
        End Sub
        """;
}

/// <summary>
/// C4/C4b/C4n/D1/A3/A4: four backends × both pipelines × both compiler entry points. All six
/// shapes are measured CORRECT on every leg at the fixed tree (<c>matrix-final.txt</c>,
/// <c>scratchpad/f111g</c>). D4 has its own fixture below (<see cref="DestinationInvalidation_D4_ByRefExecutionTests"/>)
/// because JavaScript must be excluded from its value assertion.
///
/// <para>⚠ Per <c>BothCompilerEntryPoints_*</c>'s established convention (<c>StatementOperandUndeclaredTempFixTests</c>,
/// <c>Family111MaterialisationBehaviourTests</c>), the two entry-point legs render through C# only
/// — the risk they test is "does <c>CompileFile</c>/<c>CompileProjectFiles</c> even route through
/// the same optimizer pipeline as the in-process helpers", not a second value oracle per backend.
/// <c>OptimizeAggressive = true</c> is used for both, matching what the CLI's <c>--optimize</c> and
/// a Release <c>.blproj</c> build request — CSE is a STANDARD pass and runs under either.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class CseDestinationInvalidationExecutionTests
{
    private static string Source(string shapeName) =>
        (string)typeof(CseDestinationShapes)
            .GetField(shapeName, BindingFlags.NonPublic | BindingFlags.Static)!
            .GetRawConstantValue()!;

    [TestCase(nameof(CseDestinationShapes.C4), nameof(CseDestinationShapes.C4Expected), TestName = "C4_StandardPipeline_AllFourBackendsAgree")]
    [TestCase(nameof(CseDestinationShapes.C4b), nameof(CseDestinationShapes.C4bExpected), TestName = "C4b_LiteralReassignment_StandardPipeline_AllFourBackendsAgree")]
    [TestCase(nameof(CseDestinationShapes.C4n), nameof(CseDestinationShapes.C4nExpected), TestName = "C4n_BothOccurrencesNamed_StandardPipeline_AllFourBackendsAgree")]
    [TestCase(nameof(CseDestinationShapes.D1), nameof(CseDestinationShapes.D1Expected), TestName = "D1_ReassignedInsideALoopBody_StandardPipeline_AllFourBackendsAgree")]
    [TestCase(nameof(CseDestinationShapes.A3), nameof(CseDestinationShapes.A3Expected), TestName = "A3_GlobalDestinationWrittenInACallee_StandardPipeline_AllFourBackendsAgree")]
    [TestCase(nameof(CseDestinationShapes.A4), nameof(CseDestinationShapes.A4Expected), TestName = "A4_ClassFieldDestinationWrittenByAnInstanceCall_StandardPipeline_AllFourBackendsAgree")]
    public void StandardPipeline(string shapeName, string expectedName)
        => FourBackends.RunsOnEveryBackend(Source(shapeName), Source(expectedName));

    [TestCase(nameof(CseDestinationShapes.C4), nameof(CseDestinationShapes.C4Expected), TestName = "C4_AggressivePipeline_AllFourBackendsAgree")]
    [TestCase(nameof(CseDestinationShapes.C4b), nameof(CseDestinationShapes.C4bExpected), TestName = "C4b_LiteralReassignment_AggressivePipeline_AllFourBackendsAgree")]
    [TestCase(nameof(CseDestinationShapes.C4n), nameof(CseDestinationShapes.C4nExpected), TestName = "C4n_BothOccurrencesNamed_AggressivePipeline_AllFourBackendsAgree")]
    [TestCase(nameof(CseDestinationShapes.D1), nameof(CseDestinationShapes.D1Expected), TestName = "D1_ReassignedInsideALoopBody_AggressivePipeline_AllFourBackendsAgree")]
    [TestCase(nameof(CseDestinationShapes.A3), nameof(CseDestinationShapes.A3Expected), TestName = "A3_GlobalDestinationWrittenInACallee_AggressivePipeline_AllFourBackendsAgree")]
    [TestCase(nameof(CseDestinationShapes.A4), nameof(CseDestinationShapes.A4Expected), TestName = "A4_ClassFieldDestinationWrittenByAnInstanceCall_AggressivePipeline_AllFourBackendsAgree")]
    public void AggressivePipeline(string shapeName, string expectedName)
        => FourBackends.RunsOnEveryBackendAggressive(Source(shapeName), Source(expectedName));

    [TestCase(nameof(CseDestinationShapes.C4), nameof(CseDestinationShapes.C4Expected), TestName = "C4_TheCliSingleFileEntryPoint")]
    [TestCase(nameof(CseDestinationShapes.C4b), nameof(CseDestinationShapes.C4bExpected), TestName = "C4b_TheCliSingleFileEntryPoint")]
    [TestCase(nameof(CseDestinationShapes.C4n), nameof(CseDestinationShapes.C4nExpected), TestName = "C4n_TheCliSingleFileEntryPoint")]
    [TestCase(nameof(CseDestinationShapes.D1), nameof(CseDestinationShapes.D1Expected), TestName = "D1_TheCliSingleFileEntryPoint")]
    [TestCase(nameof(CseDestinationShapes.A3), nameof(CseDestinationShapes.A3Expected), TestName = "A3_TheCliSingleFileEntryPoint")]
    [TestCase(nameof(CseDestinationShapes.A4), nameof(CseDestinationShapes.A4Expected), TestName = "A4_TheCliSingleFileEntryPoint")]
    public void TheCliSingleFileEntryPoint(string shapeName, string expectedName)
        => Assert.That(RunThroughEntryPoint(Source(shapeName), asProject: false), Is.EqualTo(Source(expectedName)),
            "BasicCompiler.CompileFile printed the wrong answer.");

    [TestCase(nameof(CseDestinationShapes.C4), nameof(CseDestinationShapes.C4Expected), TestName = "C4_TheProjectBuildEntryPoint")]
    [TestCase(nameof(CseDestinationShapes.C4b), nameof(CseDestinationShapes.C4bExpected), TestName = "C4b_TheProjectBuildEntryPoint")]
    [TestCase(nameof(CseDestinationShapes.C4n), nameof(CseDestinationShapes.C4nExpected), TestName = "C4n_TheProjectBuildEntryPoint")]
    [TestCase(nameof(CseDestinationShapes.D1), nameof(CseDestinationShapes.D1Expected), TestName = "D1_TheProjectBuildEntryPoint")]
    [TestCase(nameof(CseDestinationShapes.A3), nameof(CseDestinationShapes.A3Expected), TestName = "A3_TheProjectBuildEntryPoint")]
    [TestCase(nameof(CseDestinationShapes.A4), nameof(CseDestinationShapes.A4Expected), TestName = "A4_TheProjectBuildEntryPoint")]
    public void TheProjectBuildEntryPoint(string shapeName, string expectedName)
        => Assert.That(RunThroughEntryPoint(Source(shapeName), asProject: true), Is.EqualTo(Source(expectedName)),
            "BasicCompiler.CompileProjectFiles printed the wrong answer.");

    /// <summary>
    /// The CLI single-file (<c>CompileFile</c>) / project-build (<c>CompileProjectFiles</c>)
    /// entry points, exactly as <c>StatementOperandUndeclaredTempFixTests.BothCompilerEntryPoints_*</c>
    /// / <c>Family111MaterialisationBehaviourTests.RunThroughEntryPoint</c> exercise them.
    /// </summary>
    private static string RunThroughEntryPoint(string source, bool asProject)
    {
        var dir = Path.Combine(Path.GetTempPath(),
            "BasicLang_CseDestinationEntry_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "prog.bas");
            File.WriteAllText(file, source);

            var compiler = new BasicCompiler(new CompilerOptions { OptimizeAggressive = true });
            var result = asProject ? compiler.CompileProjectFiles(new[] { file }) : compiler.CompileFile(file);

            Assert.That(result.HasErrors, Is.False,
                string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            Assert.That(result.CombinedIR, Is.Not.Null, "the entry point produced no combined IR");

            return FourBackends.Norm(FourBackends.RunEmittedCSharpText(
                new BasicLang.Compiler.CodeGen.CSharp.ImprovedCSharpCodeGenerator().Generate(result.CombinedIR)));
        }
        finally { try { Directory.Delete(dir, true); } catch { /* temp */ } }
    }
}

/// <summary>
/// D4 — the <c>ByRef</c> destination shape. JavaScript refuses <c>ByRef</c> structurally (BL7002)
/// and is excluded from the value assertion on every leg; C++, MSIL and C# are load-bearing.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class DestinationInvalidation_D4_ByRefExecutionTests
{
    [Test]
    public void D4_StandardPipeline_CppMsilCSharpAgree()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(CseDestinationShapes.D4))),
                Is.EqualTo(CseDestinationShapes.D4Expected), "C++");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(CseDestinationShapes.D4)),
                Is.EqualTo(CseDestinationShapes.D4Expected), "MSIL");
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(CseDestinationShapes.D4)),
                Is.EqualTo(CseDestinationShapes.D4Expected), "C#");
        });

    [Test]
    public void D4_AggressivePipeline_CppMsilCSharpAgree()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(CseDestinationShapes.D4))),
                Is.EqualTo(CseDestinationShapes.D4Expected), "C++");
            Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(CseDestinationShapes.D4)),
                Is.EqualTo(CseDestinationShapes.D4Expected), "MSIL");
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(CseDestinationShapes.D4)),
                Is.EqualTo(CseDestinationShapes.D4Expected), "C#");
        });

    /// <summary>⛔ Structural, not a value assertion — JavaScript has no reference parameters at
    /// all and refuses ByRef at codegen regardless of what CSE decided. Matches
    /// <c>Family111MaterialisationBehaviourTests.G7_JavaScript_RefusesByRef_BL7002</c>'s pattern.</summary>
    [Test]
    public void D4_JavaScript_RefusesByRef_BL7002()
    {
        var module = JsTestSupport.BuildModule(CseDestinationShapes.D4);
        var ex = Assert.Throws<BasicLang.Compiler.CodeGen.ForeignFeatureException>(
            () => new BasicLang.Compiler.CodeGen.JavaScript.JavaScriptCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("BL7002"));
        Assert.That(ex.Message, Does.Contain("ByRef"));
    }

    [Test]
    public void D4_TheCliSingleFileEntryPoint()
        => Assert.That(RunThroughEntryPoint(CseDestinationShapes.D4, asProject: false), Is.EqualTo(CseDestinationShapes.D4Expected),
            "BasicCompiler.CompileFile printed the wrong answer.");

    [Test]
    public void D4_TheProjectBuildEntryPoint()
        => Assert.That(RunThroughEntryPoint(CseDestinationShapes.D4, asProject: true), Is.EqualTo(CseDestinationShapes.D4Expected),
            "BasicCompiler.CompileProjectFiles printed the wrong answer.");

    private static string RunThroughEntryPoint(string source, bool asProject)
    {
        var dir = Path.Combine(Path.GetTempPath(),
            "BasicLang_CseDestinationD4Entry_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "prog.bas");
            File.WriteAllText(file, source);

            var compiler = new BasicCompiler(new CompilerOptions { OptimizeAggressive = true });
            var result = asProject ? compiler.CompileProjectFiles(new[] { file }) : compiler.CompileFile(file);

            Assert.That(result.HasErrors, Is.False,
                string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            Assert.That(result.CombinedIR, Is.Not.Null, "the entry point produced no combined IR");

            return FourBackends.Norm(FourBackends.RunEmittedCSharpText(
                new BasicLang.Compiler.CodeGen.CSharp.ImprovedCSharpCodeGenerator().Generate(result.CombinedIR)));
        }
        finally { try { Directory.Delete(dir, true); } catch { /* temp */ } }
    }
}

/// <summary>
/// K1 — the "must still merge" control, asserted the ONLY way it safely can be: a single, bare
/// <c>pass.Run</c>. See <c>CseInvalidationAndKeyTests.CseInvalidationDecisionTests</c>'s own
/// docstring for the same trap, restated here because it is easy to get wrong on a fresh file:
/// <c>ModificationCount</c> read AFTER <c>OptimizationPipeline.Run</c> is ALWAYS 0 — the pipeline
/// iterates to a fixed point, and the last run of any pass is by definition the one that changed
/// nothing. Reading it there would make this assertion pass whether or not the destination guard
/// over-kills.
/// </summary>
[TestFixture]
public class CseDestinationDecisionTests
{
    /// <summary>
    /// ⭐ THE CONTROL. <c>a</c>'s destination is never reassigned anywhere in the block, so the
    /// merge stays legal and CSE must still make it — exactly ONE modification. A destination
    /// guard that killed on ANYTHING touching the name (rather than only a WRITE to it) would
    /// silently zero this out with no wrong answer on any backend to notice by, the same
    /// non-vacuity concern <c>CseInvalidationDecisionTests</c>' "MUST STILL MERGE" rows exist for.
    /// </summary>
    [Test]
    public void K1_DestinationNeverReassigned_StillMergesIntoOneBinop_FromASingleBarePassRun()
    {
        var module = JsTestSupport.BuildModule(CseDestinationShapes.K1, sourceFilePath: "prog.bas");
        var pass = new CommonSubexpressionEliminationPass();
        pass.Run(module);

        Assert.That(pass.ModificationCount, Is.EqualTo(1),
            "K1: CSE must still merge `a`/`l(0)`'s shared `p + q` into one binop when the "
            + "destination is never reassigned. Read from a SINGLE pass.Run, never through "
            + "OptimizationPipeline — after the pipeline, ModificationCount always reads 0 "
            + "(the pipeline iterates to a fixed point; the last run of any pass changed nothing).");
    }
}

// ================================================================================================
//  KNOWN-WRONG PINS — task #133. Each is a MEASURED gap in NamesWrittenBy's kill vocabulary
//  (see BasicLang/IROptimizer.cs, NamesWrittenBy's own "KNOWN GAPS" doc comment): a write CSE's
//  destination guard cannot see because it never appears as an IRAssignment target, an IRStore
//  address, a rename or a ByRef argument in the block that reads the stale value. Per this repo's
//  pinned-broken convention (LoopPassesDisabledTests / InductionVariableDisabledTests /
//  StatementOperandUndeclaredTempFixTests' task-125 pins): these fail LOUDLY, not silently, the
//  day task #133 closes one of them — update or delete the pin then, do not widen it.
//
//  ⚠ Every backend/shape combination below is exactly what scratchpad/f111g/matrix-final.txt
//  measured on this tree. A backend is EXCLUDED from a shape's pin only when it fails for an
//  unrelated, pre-existing reason (a compile error nothing here caused, or BL7002's structural
//  ByRef refusal) rather than printing a wrong VALUE — matching this suite's convention of never
//  asserting "wrong" against a leg that cannot even run the program.
// ================================================================================================

[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class CseDestinationKnownGapsTask133Tests
{
    /// <summary>
    /// A1 — the destination is written inside a LAMBDA's body (<c>Dim clr = Sub() a = 0 : clr()</c>)
    /// rather than by a plain call. Correct is <c>seed\nseed\n3,0</c>.
    ///
    /// <para>⛔ C++ and MSIL are EXCLUDED — both fail to compile this program for reasons that have
    /// nothing to do with CSE: C++ ("cannot assign to a variable captured by copy in a non-mutable
    /// lambda") cannot lower a mutating capture at all, and MSIL ("Reference to undefined class
    /// 'Action'") has no lowering for the delegate type a <c>Sub()</c> lambda gets typed as. Both
    /// are pre-existing, unrelated gaps.</para>
    ///
    /// <para>C# and JavaScript DO run it, and print DIFFERENT wrong answers — task #133 covers
    /// both as separately measured pins, not one shared value.</para>
    /// </summary>
    [Test]
    public void A1_LambdaCapturedDestination_CSharp_PinnedForTask133()
        => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(A1)), Is.EqualTo("seed\nseed\n3,3"),
            "task #133 — if this changed, C#'s handling of a destination written inside a lambda "
            + "capture may have changed (for better or worse); re-measure and update or delete "
            + "this pin, do not just widen it. C++ and MSIL are excluded — both fail to compile "
            + "this shape for unrelated, pre-existing reasons.");

    [Test]
    public void A1_LambdaCapturedDestination_JavaScript_PinnedForTask133()
        => Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(A1)), Is.EqualTo("seed\nseed\n0,0"),
            "task #133 — same reasoning as the C# pin above; JavaScript prints a DIFFERENT wrong "
            + "answer, both are pinned separately.");

    private const string A1 = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Sub Main()
            Dim p As Integer = Seed(1)
            Dim q As Integer = Seed(2)
            Dim l As New List(Of Integer)()
            l.Add(0)
            Dim a As Integer = p + q
            Dim clr = Sub() a = 0
            clr()
            l(0) = p + q
            Console.WriteLine(CStr(l(0)) & "," & CStr(a))
        End Sub
        """;

    /// <summary>
    /// A2b — TWO <c>ByRef</c> parameters (<c>n</c>, <c>m</c>) ALIASED to the SAME argument
    /// (<c>Work(v, v, ...)</c>): the write is to <c>m = 0</c>, but through the alias it also
    /// changes what <c>n</c> — the destination CSE is guarding — names. <c>NamesWrittenBy</c> kills
    /// on the LITERAL name written (<c>m</c>); it has no notion of argument aliasing. Correct is
    /// <c>seed\nseed\n3,0</c>; C++ and MSIL print <c>seed\nseed\n0,0</c>.
    ///
    /// <para>⛔ JavaScript is EXCLUDED — BL7002, it refuses <c>ByRef</c> outright and cannot run
    /// this program at all. C# is NOT pinned — measured correct (inline-always is, once again,
    /// vacuous for this whole family).</para>
    /// </summary>
    [Test]
    public void A2b_AliasedByRefParameters_Cpp_PinnedForTask133()
        => Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(A2b))), Is.EqualTo("seed\nseed\n0,0"),
            "task #133 — aliased ByRef parameters (Work(v, v, ...)); if this changed, re-measure "
            + "and update or delete this pin, do not just widen it");

    [Test]
    public void A2b_AliasedByRefParameters_Msil_PinnedForTask133()
        => Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(A2b)), Is.EqualTo("seed\nseed\n0,0"),
            "task #133 — same reasoning as the C++ pin above");

    private const string A2b = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Sub Work(ByRef n As Integer, ByRef m As Integer, p As Integer, q As Integer)
            Dim l As New List(Of Integer)()
            l.Add(0)
            n = p + q
            m = 0
            l(0) = p + q
            Console.WriteLine(CStr(l(0)) & "," & CStr(n))
        End Sub

        Sub Main()
            Dim v As Integer = 5
            Work(v, v, Seed(1), Seed(2))
        End Sub
        """;

    /// <summary>
    /// A5b — the destination is a class field, written through an EXPLICIT <c>Me.K = 0</c> rather
    /// than a bare <c>K = 0</c> (contrast A4, which is a bare write through a CALL and IS caught).
    /// This is the destination-side twin of the field-store gap <c>NamesWrittenBy</c>'s own doc
    /// comment names. Correct is <c>seed\nseed\n3,0</c>; C++ and MSIL print
    /// <c>seed\nseed\n0,0</c>.
    ///
    /// <para>⛔ JavaScript is EXCLUDED for an UNRELATED reason — measured <c>ReferenceError: K is
    /// not defined</c>, a pre-existing JS field-access lowering gap for a bare-named field read
    /// inside its own class (<c>CStr(K)</c> rather than <c>CStr(Me.K)</c>), not this family's to
    /// fix. C# is not pinned — vacuous, as everywhere in this family.</para>
    /// </summary>
    [Test]
    public void A5b_MeFieldStore_Cpp_PinnedForTask133()
        => Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(A5b))), Is.EqualTo("seed\nseed\n0,0"),
            "task #133 — an explicit Me.K = 0 field store; if this changed, re-measure and update "
            + "or delete this pin, do not just widen it");

    [Test]
    public void A5b_MeFieldStore_Msil_PinnedForTask133()
        => Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(A5b)), Is.EqualTo("seed\nseed\n0,0"),
            "task #133 — same reasoning as the C++ pin above");

    private const string A5b = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class Box
            Public K As Integer

            Sub Work(p As Integer, q As Integer)
                Dim l As New List(Of Integer)()
                l.Add(0)
                K = p + q
                Me.K = 0
                l(0) = p + q
                Console.WriteLine(CStr(l(0)) & "," & CStr(K))
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work(Seed(1), Seed(2))
        End Sub
        """;

    /// <summary>
    /// A6 — the OPERAND side of the SAME field-store gap A5b pins on the destination side
    /// (<c>NamesWrittenBy</c>'s doc comment lists both as one gap, "an IRFieldStore through Me. to
    /// a member the function also names bare"). Here the shared expression is <c>K + q</c>, with
    /// <c>K</c> read BARE as an OPERAND and later written through <c>Me.K = 10</c>. Correct is
    /// <c>12,3</c> (<c>a</c> keeps the value computed before the write; <c>l(0)</c> must recompute
    /// with the new <c>K</c>); the defect merges the two occurrences and prints <c>3,3</c> on
    /// C++/JavaScript/MSIL — MEASURED (<c>matrix-final.txt</c>).
    ///
    /// <para>⚠ C# is NOT pinned wrong here — MEASURED CORRECT (<c>12,3</c>), same as every other
    /// shape in this family: inline-always re-emits <c>K + q</c> as TEXT at each occurrence rather
    /// than trusting a merged name, which happens to be right regardless of whether CSE merged the
    /// two <c>K + q</c> instructions. (This distinguishes A6 from the destination-side shapes A5b/
    /// A2b/A1 above, where reading a NAMED, reassigned destination by value is what goes wrong —
    /// there is no equivalent "named destination" here for inline-always to be rescued or tripped
    /// up by; it only ever re-emits the expression.)</para>
    /// </summary>
    [Test]
    public void A6_OperandSideFieldStoreTwin_PinnedForTask133()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(A6)), Is.EqualTo("seed\nseed\n12,3"),
                "C# — MEASURED CORRECT, vacuous for this family as everywhere else; asserted for "
                + "agreement, not as a task #133 pin. If this regressed to '3,3', that is a NEW "
                + "defect on C#, not a widening of this pin.");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(A6))), Is.EqualTo("seed\nseed\n3,3"),
                "task #133 (C++) — a bare K operand read, aliased by a later Me.K write; if this "
                + "changed, re-measure and update or delete this pin, do not just widen it");
            Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(A6)), Is.EqualTo("seed\nseed\n3,3"),
                "task #133 (JavaScript) — same reasoning");
            Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(A6)), Is.EqualTo("seed\nseed\n3,3"),
                "task #133 (MSIL) — same reasoning");
        });

    private const string A6 = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class Box
            Public K As Integer

            Sub Work(q As Integer)
                Dim l As New List(Of Integer)()
                l.Add(0)
                K = Seed(1)
                Dim a As Integer = K + q
                Me.K = 10
                l(0) = K + q
                Console.WriteLine(CStr(l(0)) & "," & CStr(a))
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work(Seed(2))
        End Sub
        """;
}
