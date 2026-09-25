using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.MSIL;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.SemanticAnalysis;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  ADR-0006 D3 — "which rule decides whether a call can write a value's operand?" One predicate,
//  the DECLARATIONS rule: OptimizationPass.IsCallVisible(IRVariable, IRFunction), plus the
//  (string, IRFunction) overload holding the rule itself. Visible unless the variable is Const, a
//  by-value parameter, or a declared local; a global, a ByRef parameter, and any UNDECLARED name
//  (the safe default for a kill vocabulary) are visible. CSE's ReadsCallVisible(IRValue,
//  IRFunction), IsCallVisibleDestination(IRValue, IRFunction), LICM's read check and IRVerifier's
//  call arm (now checking ALL of Guard(v), not only the destination) all delegate to it.
//
//  This file has four parts:
//   1. CallVisibilityQ3ExecutionTests  — Q3/Q3m (docs/superpowers/decisions 0006-brief.md's own
//      Q3 probe): a bare class field read, across a bare call and a `Me.`-qualified one.
//   2. CallVisibilityQ3nExecutionTests — Q3n: the NAMED-OPERAND arm ReadsCallVisible needed so CSE
//      and the widened verifier agree (`Dim a As Integer = (p + q) * 2` renames `(p+q)` `K`, an
//      undeclared name, through NamedAfterVariable — the same "renamed value" shape the verifier's
//      Guard walk already covered, and CSE's operand walk did not, before D3).
//   3. CallVisibilityHandBuiltIRTests  — hand-built IR against IRVerifier.CheckInvariantSPrime,
//      isolating the declarations rule from what CSE happens to decide (mirrors
//      IRVerifierHandBuiltIRTests' idiom in IRVerifierTests.cs, and
//      scratchpad/adr6/hand/Program.cs's own nine-case driver one-for-one for the cases D3 changed).
//   4. CallVisibilityDecisionTests     — the three CSE decision rows a bare pass.Run() pins
//      structurally: a bare field (undeclared) across a call does not merge; a declared local
//      across a call still merges; a For Each variable across a call does not merge — "precision
//      loss, not wrong" (task #121, IRBuilder leaves For Each/Catch/With variables out of
//      LocalVariables, so they read as undeclared and therefore call-visible).
// ================================================================================================

/// <summary>BASIC sources, verbatim from the implementer's evidence
/// (scratchpad/adr6/probes/Q3.bas, Q3m.bas, Q3n.bas — the Q3/Q3n probes 0006-brief.md's own Q1
/// table cites).</summary>
internal static class CallVisibilityShapes
{
    /// <summary>
    /// <c>Class Box</c> with a bare field <c>K</c>: <c>K = Seed(1) : a = K + q : Inc() :
    /// l(0) = K + q</c>. <c>Inc()</c> is a BARE call to another method on the same (implicit
    /// <c>Me</c>) instance. Before D3, the operand half of CSE's kill check used the retired flag
    /// rule (<c>IsGlobal &amp;&amp; !IsConst || IsByRef</c>), and a bare field read lowers to an
    /// <c>IRVariable</c> with NEITHER flag set — so CSE called <c>K</c> private and merged across
    /// <c>Inc()</c>. C++, JavaScript and MSIL printed <c>3,3</c> where <c>13,3</c> is correct; only
    /// C# was right, and only because it re-emits the operand expression as TEXT rather than
    /// honouring the merge (the same C#-is-vacuous-here trap <c>CseInvalidationAndKeyTests.cs</c>
    /// documents at length for the redefinition family).
    /// </summary>
    internal const string Q3 = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class Box
            Public K As Integer

            Sub Inc()
                K = K + 10
            End Sub

            Sub Work(q As Integer)
                Dim l As New List(Of Integer)()
                l.Add(0)
                K = Seed(1)
                Dim a As Integer = K + q
                Inc()
                l(0) = K + q
                Console.WriteLine(CStr(l(0)) & "," & CStr(a))
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work(Seed(2))
        End Sub
        """;

    /// <summary>Q3 with <c>Me.Inc()</c> in place of the bare <c>Inc()</c> — the receiver-qualified
    /// twin, so the call-visibility rule cannot be rescued by "only a BARE call is a call".</summary>
    internal const string Q3m = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class Box
            Public K As Integer

            Sub Inc()
                K = K + 10
            End Sub

            Sub Work(q As Integer)
                Dim l As New List(Of Integer)()
                l.Add(0)
                K = Seed(1)
                Dim a As Integer = K + q
                Me.Inc()
                l(0) = K + q
                Console.WriteLine(CStr(l(0)) & "," & CStr(a))
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work(Seed(2))
        End Sub
        """;

    /// <summary>
    /// The NAMED-OPERAND arm, isolated: <c>K = p + q</c> is a bare-field DESTINATION write (not
    /// this file's concern — that is D1's <c>IRFieldStore</c> gap), but <c>Dim a As Integer =
    /// (p + q) * 2</c> and <c>l(0) = K * 2</c> both read <c>K</c> as a NAMED OPERAND through a
    /// renamed <c>IRBinaryOp</c> (<c>NamedAfterVariable</c>) rather than a bare <c>IRVariable</c> —
    /// exactly the shape <c>ReadsCallVisible</c>'s pre-D3 body did not walk at all (it recursed
    /// into a binop's Left/Right, never asked whether an OPERAND instruction's own destination
    /// name was call-visible). Wrong on ALL FOUR backends, C# included, before this arm was added
    /// — see the ADR-0006 implementation note's item (c).
    /// </summary>
    internal const string Q3n = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class Box
            Public K As Integer

            Sub Inc()
                K = K + 10
            End Sub

            Sub Work(p As Integer, q As Integer)
                Dim l As New List(Of Integer)()
                l.Add(0)
                K = p + q
                Dim a As Integer = (p + q) * 2
                Inc()
                l(0) = K * 2
                Console.WriteLine(CStr(l(0)) & "," & CStr(a))
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work(Seed(1), Seed(2))
        End Sub
        """;

    internal const string Expected13_3 = "seed\nseed\n13,3";
    internal const string ExpectedQ3n = "seed\nseed\n26,6";
}

/// <summary>
/// Q3 / Q3m — ADR-0006 D3's Contract: <c>13,3</c> on all four backends, standard AND aggressive
/// pipelines, plus one leg through an actual shipping ENTRY POINT (CLAUDE.md: "test both entry
/// points" — <c>FourBackends</c>'s helpers already run through the optimizer, but not through
/// <c>BasicCompiler</c> itself).
///
/// <para>⚠ <c>FourBackends.RunsOnEveryBackend</c>'s JavaScript leg
/// (<c>JavaScriptExecutionTests.RunJs</c> → <c>JsTestSupport.Compile</c>) runs NO OPTIMIZER AT
/// ALL — MEASURED, see <c>JsTestSupport.Compile</c>'s own doc comment. CSE never runs, so there is
/// no merge to get wrong in the first place and that leg is VACUOUS for this defect, same as C#
/// is vacuous for the CSE redefinition family. <c>RunsOnEveryBackendAggressive</c>'s JavaScript
/// leg (<c>JsTestSupport.CompileAggressive</c> → <c>AddAggressivePasses</c>, which calls
/// <c>AddStandardPasses</c> first) DOES run CSE and is load-bearing. Asserted on both pipelines
/// anyway, matching every other fixture in this suite that keeps a vacuous leg for agreement.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class CallVisibilityQ3ExecutionTests
{
    [Test]
    public void Q3_StandardPipeline_AllFourBackendsAgree()
        => FourBackends.RunsOnEveryBackend(CallVisibilityShapes.Q3, CallVisibilityShapes.Expected13_3);

    [Test]
    public void Q3_AggressivePipeline_AllFourBackendsAgree()
        => FourBackends.RunsOnEveryBackendAggressive(CallVisibilityShapes.Q3, CallVisibilityShapes.Expected13_3);

    [Test]
    public void Q3m_StandardPipeline_AllFourBackendsAgree()
        => FourBackends.RunsOnEveryBackend(CallVisibilityShapes.Q3m, CallVisibilityShapes.Expected13_3);

    [Test]
    public void Q3m_AggressivePipeline_AllFourBackendsAgree()
        => FourBackends.RunsOnEveryBackendAggressive(CallVisibilityShapes.Q3m, CallVisibilityShapes.Expected13_3);

    /// <summary>
    /// The CLI single-file entry point (<c>BasicCompiler.CompileFile</c>) with DEFAULT
    /// (non-aggressive) <c>CompilerOptions</c> — the literal DEFAULT pipeline ADR-0006 D3's
    /// Obligations name ("ordinary class code is wrong today"; CSE is in
    /// <c>OptimizationPipeline.AddStandardPasses</c>, which <c>BasicCompiler</c> runs
    /// UNCONDITIONALLY whether or not <c>--optimize</c> is passed). Every <c>FourBackends</c> leg
    /// above goes through the in-fixture <c>BclE2E</c>/<c>JsTestSupport</c> helpers, which call
    /// <c>IRBuilder</c> directly — CLAUDE.md's "test both entry points" rule for exactly this
    /// reason: a fix seen only through the non-CLI helper can still break via the CLI or the IDE
    /// build. MSIL is the leg (skips cleanly without <c>ilasm</c>, matching every MSIL leg in this
    /// suite — Windows-only prerequisite, not a fail).
    /// </summary>
    [Test]
    public void Q3_CliDefaultPipeline_Msil_PrintsTheCorrectValue()
    {
        var dir = Path.Combine(Path.GetTempPath(), "BasicLang_CallVisibilityQ3Cli_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "prog.bas");
            File.WriteAllText(file, CallVisibilityShapes.Q3);

            // Default CompilerOptions: OptimizeAggressive = false, which BasicCompiler maps to
            // AddStandardPasses — the DEFAULT pipeline, not --optimize's aggressive one.
            var compiler = new BasicCompiler(new CompilerOptions());
            var result = compiler.CompileFile(file);

            Assert.That(result.HasErrors, Is.False, string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            Assert.That(result.CombinedIR, Is.Not.Null, "the entry point produced no combined IR");

            var il = new MSILCodeGenerator().Generate(result.CombinedIR);
            Assert.That(FourBackends.Norm(MsilHarness.RunIlExpectingSuccess(il)),
                Is.EqualTo(CallVisibilityShapes.Expected13_3),
                "via the CLI's own DEFAULT (non-aggressive) entry point — BasicCompiler.CompileFile, "
                + "not the in-fixture IRBuilder helper");
        }
        finally { try { Directory.Delete(dir, true); } catch { /* temp */ } }
    }
}

/// <summary>
/// Q3n — the NAMED-OPERAND arm of <c>ReadsCallVisible</c>. C#, C++ and MSIL must print
/// <c>26,6</c>; JavaScript is a KNOWN, SEPARATE gap (ADR-0006 D1's Obligations list the
/// <c>Me.K</c>/JavaScript <c>ReferenceError</c> as one of three follow-up briefs D3 does not
/// cover) and is pinned as a crash, not silently accepted.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class CallVisibilityQ3nExecutionTests
{
    /// <summary>
    /// ⛔ JavaScript EXCLUDED — see <see cref="Q3n_JavaScript_KnownGap_ReferenceErrorOnMeK"/>.
    /// MEASURED, standard pipeline, all three entry points (CLI, CLI <c>--optimize</c>, Release
    /// <c>.blproj</c>): C#/C++/MSIL all print <c>26,6</c> — the implementer's evidence
    /// (<c>scratchpad/adr6/probes/matrix-final.txt</c>) plus an independent re-measurement against
    /// this exact working tree.
    /// </summary>
    [Test]
    public void Q3n_StandardPipeline_CSharpCppMsilAgree()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(CallVisibilityShapes.Q3n))),
                Is.EqualTo(CallVisibilityShapes.ExpectedQ3n), "C++ — load-bearing");
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(CallVisibilityShapes.Q3n)),
                Is.EqualTo(CallVisibilityShapes.ExpectedQ3n), "C# — load-bearing (unlike the CSE "
                + "redefinition family, C# is NOT vacuous here: Q3n's defect is the missing "
                + "NAMED-OPERAND arm, and before it was added C# was wrong too — see matrix-final.txt)");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(CallVisibilityShapes.Q3n)),
                Is.EqualTo(CallVisibilityShapes.ExpectedQ3n), "MSIL — load-bearing");
        });

    /// <summary>
    /// ⛔ KNOWN, SEPARATE GAP — ADR-0006 D1's Obligations name it explicitly: "JavaScript
    /// <c>Me.K</c> ReferenceError ... three separate briefs, not this one." <c>Me.K</c> (an
    /// <c>IRFieldStore</c>-shaped write the shared kill vocabulary does not yet name — D1's
    /// concern, not D3's) lowers to a bare <c>K</c> reference the emitted JS never declares, so
    /// Node throws before printing anything past the two "seed" lines. MEASURED against this exact
    /// working tree: <c>ReferenceError: K is not defined</c>, on the CLI's default JS target.
    /// Pinned as a CRASH — not "wrong value", not silently skipped — so a future fix to the gap
    /// this cites changes this test loudly instead of leaving it accidentally green.
    /// </summary>
    [Test]
    public void Q3n_JavaScript_KnownGap_ReferenceErrorOnMeK()
    {
        var js = JsTestSupport.CompileOptimized(CallVisibilityShapes.Q3n);
        var (exitCode, _, stderr) = RunNodeAllowingFailure(js);

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.Not.Zero,
                "Q3n was expected to CRASH on JavaScript (ADR-0006 D1's Me.K gap). If this now "
                + "exits 0, the gap may be closed — re-measure and update CallVisibilityShapes."
                + "ExpectedQ3n's JS leg, or delete this pin, rather than widening it.");
            Assert.That(stderr, Does.Contain("ReferenceError: K is not defined"),
                "expected the specific, MEASURED failure mode — a different error means the gap "
                + "moved, not that this pin is still describing it correctly");
        });
    }

    /// <summary>
    /// Runs already-generated JS under Node and returns (exit code, stdout, stderr) instead of
    /// hard-asserting success — <c>JavaScriptExecutionTests.RunNodeScript</c> asserts exit code
    /// ZERO unconditionally, which is exactly wrong for pinning a KNOWN crash. Otherwise mirrors
    /// its process handling verbatim (async reads before <c>WaitForExit</c>, then a 30s kill) for
    /// the same wedged-Node reason documented there.
    /// </summary>
    private static (int ExitCode, string Stdout, string Stderr) RunNodeAllowingFailure(string js)
    {
        var node = BasicLang.Runtime.NodeLocator.Find();
        if (node == null)
            Assert.Ignore("Node.js not found — the JS execution tier cannot run on this machine.");

        var dir = Path.Combine(Path.GetTempPath(), "BasicLang_Q3nJsCrash_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "program.mjs");
            File.WriteAllText(file, js);

            var psi = new ProcessStartInfo(node!)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add(file);

            using var p = Process.Start(psi)!;
            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();

            if (!p.WaitForExit(30000))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* best effort */ }
                Assert.Fail($"node did not exit within 30s.\n--- generated JS ---\n{js}");
            }

            return (p.ExitCode, stdoutTask.GetAwaiter().GetResult().Trim(), stderrTask.GetAwaiter().GetResult());
        }
        finally { try { Directory.Delete(dir, true); } catch { /* best effort */ } }
    }
}

/// <summary>
/// Hand-built IR against <see cref="IRVerifier.CheckInvariantSPrime"/> — the only way to isolate
/// the declarations rule from what CSE happens to decide, mirroring
/// <c>IRVerifierHandBuiltIRTests</c>'s idiom in <c>IRVerifierTests.cs</c> (same
/// <c>Fixture</c>/<c>Use</c>/<c>AssertVerdict</c> shape) and porting
/// <c>scratchpad/adr6/hand/Program.cs</c>'s nine-case driver one-for-one for the cases D3 changed.
///
/// <para>Every shape shares ONE value <c>t0 = K + p</c> (or, for the destination/named-operand
/// cases, a value spelled to test exactly that arm) used TWICE with a plain, argument-less
/// <c>Seed()</c> call between the two uses — the minimal shape that makes Invariant S′'s "shared
/// value, a write to Guard(v) between definition and a use" fire IF AND ONLY IF the operand (or
/// destination) is call-visible. <c>p</c>/<c>q</c> are declared by-value parameters throughout,
/// serving as the "definitely private" control operand.</para>
///
/// <para>MEASURED against this exact working tree (a fresh, isolated rebuild of
/// <c>BasicLang.dll</c>, re-verified by md5 against the applied patch): (a), (h) and (i) FIRE;
/// (b), (c) and (d) PASS — exactly the Contract below states.</para>
/// </summary>
[TestFixture]
public class CallVisibilityHandBuiltIRTests
{
    private static readonly TypeInfo IntType = new TypeInfo("Integer", TypeKind.Primitive);

    private sealed class Fixture
    {
        public IRModule Module;
        public IRFunction Function;
        public BasicBlock Entry;
        public IRVariable P;
        public IRVariable Q;
    }

    private static Fixture NewFixture()
    {
        var module = new IRModule("M");
        var function = new IRFunction("Main", IntType);
        module.Functions.Add(function);
        var p = new IRVariable("p", IntType) { IsParameter = true };
        var q = new IRVariable("q", IntType) { IsParameter = true };
        function.Parameters.Add(p);
        function.Parameters.Add(q);
        var entry = function.CreateBlock("entry");
        return new Fixture { Module = module, Function = function, Entry = entry, P = p, Q = q };
    }

    /// <summary>A call whose only role is to READ <paramref name="x"/> — the generic "some later
    /// instruction uses this value" site every shape below needs at least twice to make the value
    /// shared.</summary>
    private static IRCall Use(IRValue x)
    {
        var call = new IRCall("", "Show", IntType);
        call.Arguments.Add(x);
        return call;
    }

    private static void AssertVerdict(IRModule module, bool expectViolation, bool? expectDestination = null, string expectVariable = null)
    {
        var violations = IRVerifier.CheckInvariantSPrime(module);
        if (!expectViolation)
        {
            Assert.That(violations, Is.Empty,
                "expected NO violation; got: " + string.Join(" | ", violations.Select(v => v.ToString())));
            return;
        }

        Assert.That(violations, Is.Not.Empty, "expected a violation and got none");
        Assert.Multiple(() =>
        {
            if (expectDestination.HasValue)
                Assert.That(violations[0].IsDestination, Is.EqualTo(expectDestination.Value),
                    "IsDestination polarity is wrong — got: " + violations[0]);
            if (expectVariable != null)
                Assert.That(violations[0].Variable, Is.EqualTo(expectVariable), "wrong guarded variable — got: " + violations[0]);
        });
    }

    // ---- (a) MUST FIRE: an undeclared bare field, an anonymous temp, a call between two uses ----

    /// <summary>
    /// ⭐⭐ THE D3 KILL. <c>K</c> is a plain <c>IRVariable</c> named on neither
    /// <c>Function.Parameters</c> nor <c>Function.LocalVariables</c> — exactly how a class field
    /// read BARE lowers (the shape Q3 measures as a program). Before D3 the operand half asked the
    /// FLAG rule (<c>IsGlobal &amp;&amp; !IsConst || IsByRef</c>) and <c>K</c> has none of those
    /// flags set, so it was called PRIVATE and this shape PASSED at HEAD before D3 — the exact
    /// certification gap the ADR's Contract closes.
    /// </summary>
    [Test]
    public void UndeclaredBareFieldOperand_AnonymousTemp_SharedAcrossACall_Fires()
    {
        var f = NewFixture();
        var K = new IRVariable("K", IntType); // undeclared: no Parameters/LocalVariables entry
        var v = new IRBinaryOp("t0", BinaryOpKind.Add, K, f.P, IntType);
        f.Entry.Instructions.Add(v);
        f.Entry.Instructions.Add(Use(v));
        f.Entry.Instructions.Add(new IRCall("", "Seed", IntType));
        f.Entry.Instructions.Add(Use(v));
        f.Entry.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectViolation: true, expectDestination: false, expectVariable: "K");
    }

    // ---- (b), (c), (d): the controls — each swaps K's declaration and MUST PASS -----------------

    /// <summary>(b) K a DECLARED LOCAL of the function. A declared local is private under the
    /// declarations rule, so the call between the two uses does not guard it.</summary>
    [Test]
    public void DeclaredLocalOperand_SharedAcrossACall_Passes()
    {
        var f = NewFixture();
        var K = new IRVariable("K", IntType);
        f.Function.LocalVariables.Add(K);
        var v = new IRBinaryOp("t0", BinaryOpKind.Add, K, f.P, IntType);
        f.Entry.Instructions.Add(v);
        f.Entry.Instructions.Add(Use(v));
        f.Entry.Instructions.Add(new IRCall("", "Seed", IntType));
        f.Entry.Instructions.Add(Use(v));
        f.Entry.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectViolation: false);
    }

    /// <summary>(c) K a BY-VALUE PARAMETER of the function. Private under the declarations rule,
    /// same as a declared local.</summary>
    [Test]
    public void ByValueParameterOperand_SharedAcrossACall_Passes()
    {
        var f = NewFixture();
        var K = new IRVariable("K", IntType) { IsParameter = true };
        f.Function.Parameters.Add(K);
        var v = new IRBinaryOp("t0", BinaryOpKind.Add, K, f.P, IntType);
        f.Entry.Instructions.Add(v);
        f.Entry.Instructions.Add(Use(v));
        f.Entry.Instructions.Add(new IRCall("", "Seed", IntType));
        f.Entry.Instructions.Add(Use(v));
        f.Entry.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectViolation: false);
    }

    /// <summary>(d) K a Const GLOBAL — never declared as a local or parameter at all, but the
    /// <c>IsConst</c> flag itself is exempt regardless of declaration, the same exemption that
    /// keeps CSE worth having across the repo's sample games.</summary>
    [Test]
    public void ConstGlobalOperand_SharedAcrossACall_Passes()
    {
        var f = NewFixture();
        var K = new IRVariable("K", IntType) { IsGlobal = true, IsConst = true };
        var v = new IRBinaryOp("t0", BinaryOpKind.Add, K, f.P, IntType);
        f.Entry.Instructions.Add(v);
        f.Entry.Instructions.Add(Use(v));
        f.Entry.Instructions.Add(new IRCall("", "Seed", IntType));
        f.Entry.Instructions.Add(Use(v));
        f.Entry.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectViolation: false);
    }

    // ---- (h) MUST FIRE: a temp-SPELLED NamedAfterVariable destination is still a variable --------

    /// <summary>
    /// ⭐ A value renamed to <c>t1</c> — spelled exactly like a compiler temp — with
    /// <c>NamedAfterVariable = true</c>: <c>IRBuilder.SeparateTempsFromUserNames</c> renames the
    /// COMPILER's temp, never the user's variable, so a renamed value spelled <c>t1</c> IS a
    /// user field/variable named <c>t1</c>. Before D3's implementer choice (ADR note item (c):
    /// "temp-spelled names no longer exempt"), the destination arm's spelling test
    /// (<c>IsTempDestination</c>) alone would have called this private by SHAPE. It must fire the
    /// same as any other undeclared destination.
    /// </summary>
    [Test]
    public void NamedAfterVariableDestination_SpelledLikeATemp_SharedAcrossACall_Fires()
    {
        var f = NewFixture();
        var v = new IRBinaryOp("t1", BinaryOpKind.Add, f.P, f.Q, IntType) { NamedAfterVariable = true };
        f.Entry.Instructions.Add(v);
        f.Entry.Instructions.Add(new IRCall("", "Seed", IntType));
        f.Entry.Instructions.Add(Use(v));
        f.Entry.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectViolation: true, expectDestination: true, expectVariable: "t1");
    }

    // ---- (i) MUST FIRE: an OPERAND that is itself a value renamed to an undeclared name ----------

    /// <summary>
    /// ⭐⭐ THE NAMED-OPERAND ARM, directly — the same arm Q3n measures as a program. <c>Tot2</c>
    /// is a renamed <c>IRBinaryOp</c> (an undeclared name — the class-field-write shape, e.g.
    /// <c>K = p + q</c>), and <c>t2 = Tot2 * 2</c> reads it as an OPERAND, not by walking
    /// <c>Tot2</c>'s own Left/Right (that would miss <c>Seed()</c> writing whatever storage
    /// <c>Tot2</c> names). <c>t2</c> itself is anonymous and shared (two uses, a call between).
    /// Before the named-operand arm existed, <c>ReadsCallVisible</c> recursed into a binop
    /// operand's structure but never asked whether an OPERAND INSTRUCTION's own destination name
    /// was call-visible — this shape PASSED at HEAD before D3, and MEASURED, Q3n was wrong on all
    /// four backends, C# included, for exactly this reason.
    /// </summary>
    [Test]
    public void OperandRenamedToAnUndeclaredName_SharedAcrossACall_Fires()
    {
        var f = NewFixture();
        var a = new IRBinaryOp("Tot2", BinaryOpKind.Add, f.P, f.Q, IntType) { NamedAfterVariable = true };
        var v = new IRBinaryOp("t2", BinaryOpKind.Mul, a, new IRConstant(2, IntType), IntType);
        f.Entry.Instructions.Add(a);
        f.Entry.Instructions.Add(v);
        f.Entry.Instructions.Add(Use(v));
        f.Entry.Instructions.Add(new IRCall("", "Seed", IntType));
        f.Entry.Instructions.Add(Use(v));
        f.Entry.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectViolation: true, expectDestination: false, expectVariable: "Tot2");
    }
}

/// <summary>
/// The CSE DECISION, not the answer: three shapes, a SINGLE bare <c>pass.Run()</c>
/// (<c>ModificationCount</c>), matching <c>CseInvalidationDecisionTests</c>' idiom in
/// <c>CseInvalidationAndKeyTests.cs</c> — structural, because over-killing (a lost merge)
/// produces no wrong VALUE on any backend and needs a count, not a printed answer, to see it.
///
/// <para>⚠ <c>ModificationCount</c> read from a SINGLE <c>pass.Run</c>, never after
/// <c>OptimizationPipeline.Run</c> (always 0 there — the pipeline iterates to a fixed point and
/// the last round of a converging pass reports no change no matter what it did).</para>
///
/// <para>MEASURED against this exact working tree (an isolated, dependency-complete rebuild of
/// <c>BasicLang.dll</c>, re-verified by md5 against the applied patch, front end clean — no parse
/// or semantic errors — on all three shapes below).</para>
/// </summary>
[TestFixture]
public class CallVisibilityDecisionTests
{
    private static int Merges(string program)
    {
        var module = JsTestSupport.BuildModule(program, sourceFilePath: "prog.bas");
        var pass = new CommonSubexpressionEliminationPass();
        pass.Run(module);
        return pass.ModificationCount;
    }

    /// <summary>
    /// A bare class field <c>K</c>, read across a bare call to another method on the same
    /// instance — the program form of the hand-built (a) case above. Undeclared (never a
    /// parameter or local of <c>Work</c>), so call-visible under the declarations rule: the merge
    /// must NOT happen. Before D3 this shape MERGED (the flag rule called a bare field private),
    /// which is exactly Q3's defect as a structural count rather than a printed value.
    /// </summary>
    [Test]
    public void BareFieldOperand_AcrossACall_DoesNotMerge()
    {
        const string program = """
            Class Box
                Public K As Integer
                Sub Inc()
                    K = K + 10
                End Sub
                Sub Work(q As Integer)
                    Dim a As Integer = K + q
                    Inc()
                    Dim b As Integer = K + q
                    Console.WriteLine(CStr(a) & "," & CStr(b))
                End Sub
            End Class

            Sub Main()
                Dim bx As New Box()
                bx.Work(5)
            End Sub
            """;

        Assert.That(Merges(program), Is.Zero,
            "K is a bare field, undeclared in Work's own Parameters/LocalVariables — call-visible "
            + "under the declarations rule, so the merge across Inc() must not happen");
    }

    /// <summary>
    /// ⭐ THE CONTROL that keeps the row above non-vacuous — same shape, but the operand is a
    /// DECLARED local (<c>Dim p</c>/<c>Dim q</c> in <c>Main</c>) and the intervening call
    /// (<c>Show(0)</c>) neither reads nor writes either name. A declared local is private, so the
    /// merge must STILL happen — proving the row above is really about declaration, not about a
    /// pass that never merges anything once any call intervenes.
    /// </summary>
    [Test]
    public void DeclaredLocalOperand_AcrossACall_StillMerges()
    {
        const string program = """
            Function Seed(v As Integer) As Integer
                Console.WriteLine("seed")
                Return v
            End Function

            Sub Show(v As Integer)
                Console.WriteLine("S=" & CStr(v))
            End Sub

            Sub Main()
                Dim p As Integer = Seed(1)
                Dim q As Integer = Seed(2)
                Dim a As Integer = p + q
                Show(0)
                Dim b As Integer = p + q
                Console.WriteLine(CStr(a) & "," & CStr(b))
            End Sub
            """;

        Assert.That(Merges(program), Is.EqualTo(1),
            "p and q are DECLARED locals of Main, private under the declarations rule — the "
            + "unrelated call Show(0) must not kill this merge. Zero is NOT a safe failure here: "
            + "over-killing silently deletes the optimization with no wrong value on any backend.");
    }

    /// <summary>
    /// ⚠ PRECISION LOSS, NOT WRONG — task #121. <c>x</c> is a <c>For Each</c> loop variable, which
    /// <c>IRBuilder</c> leaves OUT of <c>LocalVariables</c> (the ASSUMPTION D3's Obligations name:
    /// "IRFunction's declared-locals set is complete" does not hold for For Each/Catch/With
    /// variables). So <c>x</c> reads as UNDECLARED and therefore call-visible, and the intervening
    /// <c>Console.WriteLine(a)</c> kills the merge every iteration — a real optimization loss, but
    /// the printed answer (<c>3,7</c>) is still correct either way, since every backend just
    /// recomputes <c>x + q</c> instead of sharing it. Tracked on task #121, not this ADR: an
    /// omission here only costs a merge, per the Obligations' own "never prints wrong."
    /// </summary>
    [Test]
    public void ForEachVariableOperand_AcrossACall_DoesNotMerge_PrecisionLossNotWrong()
    {
        const string program = """
            Function Seed(v As Integer) As Integer
                Console.WriteLine("seed")
                Return v
            End Function

            Sub Main()
                Dim q As Integer = Seed(2)
                Dim items As New List(Of Integer)()
                items.Add(1)
                items.Add(5)
                Dim l As New List(Of Integer)()
                For Each x As Integer In items
                    Dim a As Integer = x + q
                    Console.WriteLine(CStr(a))
                    l.Add(x + q)
                Next
                Console.WriteLine(CStr(l(0)) & "," & CStr(l(1)))
            End Sub
            """;

        Assert.That(Merges(program), Is.Zero,
            "task #121 — x is a For Each variable, which IRBuilder omits from LocalVariables, so "
            + "it reads as undeclared and therefore call-visible under the declarations rule. This "
            + "is a LOST merge (precision loss), not a miscompile: the program still prints the "
            + "correct 3,7 either way. If this ever becomes 1, task #121 may be closed — re-measure "
            + "before changing the pin, do not just widen it.");
    }
}
