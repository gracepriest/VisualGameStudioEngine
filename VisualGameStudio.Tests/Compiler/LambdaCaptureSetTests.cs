using System;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #122 (ADR-0006 D1's Obligation), committed 22f18284 — "Narrow the closure rule to the
//  lambda capture set". D1's INTERIM closure rule made EVERY local and by-value parameter
//  call-visible in a function that creates a lambda; this narrows it to the locals a lambda of
//  that function actually CAPTURES:
//    - OptimizationPass.LambdaCapturesOf(lambda) reads a lambda's capture set off its own IR:
//      every name mentioned (operands, When guards, NamesWrittenBy names, a V_addr slot's V),
//      plus the sets of lambdas nested inside it (transitive), minus its own PARAMETERS (exact
//      spelling) — but NOT minus its own locals (deliberate: a lambda body can use the creator's
//      n before its own Dim n, and the C# backend never declares a lambda's locals at all).
//      Null when a name cannot be enumerated (IRInlineCode) or a nested lambda was not recorded.
//    - IRBuilder.Visit(LambdaExpressionNode) calls it once the lambda's body (and every lambda
//      nested in it) is built, filling IRFunction.CapturedVariables on the lambda and
//      LambdaCapturedNames/LambdaCaptureSources on the CREATOR.
//    - OptimizationPass.IsCallVisible asks IsLambdaCaptured, compared ignoring case. FALLBACK to
//      D1's interim rule (every local) when the creator's set is null, or when it references a
//      __lambda_N its LambdaCaptureSources does not list.
//  Measured (implementer, S/t122): 492/492 probe cells and 1056/1056 corpus cells behaviourally
//  identical to master, 0 verifier fires — this is a pure precision gain, never a value change.
//
//  Four parts, in this file:
//   1. LambdaCaptureSetIrLevelTests    — the set itself, from source through the real front end
//      (Lexer/Parser/SemanticAnalyzer/IRBuilder — JsTestSupport.BuildModule), asserting directly
//      on OptimizationPass.IsCallVisible / IsLambdaCaptured / LambdaCapturesOf. Fast — no
//      optimizer pipeline, no process spawned.
//   2. LambdaCaptureSetFallbackTests   — the three fallback shapes reachable from real source
//      (an opaque lambda, an unaccounted-for one alongside a properly recorded one, and a nested
//      opaque lambda). The canonical HAND-BUILT fallback pin (a function referencing __lambda_0
//      with LambdaCapturedNames left null entirely) already exists —
//      CopyPropagationSharedVocabularyTests.U7_ClosureRule_MakesADeclaredLocalFact_CallVisible and
//      KillVocabularyExtensionArmTests.ClosureRule_MakesAByValueParameter_CallVisible_
//      WhenTheFunctionContainsALambda (KillVocabularyExtensionsTests.cs) — cited, not duplicated.
//   3. LambdaCaptureSetPrecisionStructuralTests — the precision GAIN itself, on the built IR
//      after the standard pipeline (K10: b = m + q folds to a literal even after bump()) and
//      after LICM (N7: an uncaptured local's shift hoists out of a loop that calls a lambda,
//      while the captured one's stays in). Fast — matches LicmKillVocabularyStructuralTests'
//      own convention of a bare pass Run rather than a full OptimizationPipeline (whose
//      ModificationCount after a fixed point is always zero).
//   4. LambdaCaptureSetExecutionTests  — real compiled-and-run JavaScript (the AGGRESSIVE
//      pipeline: FourBackends.RunAggressiveJs). C++ is task #140 (backend capture-by-copy), MSIL
//      is task #155 (no lowering for the delegate type a Sub() lambda gets typed as), and C#
//      inlines a single-use lambda body at its call site regardless of the closure rule — so
//      JavaScript is the only backend where #122's precision gain, or its absence, is
//      OBSERVABLE at run time. Every probe here is verbatim from S/t122 (probes/, probes2/,
//      probes4/), each cross-checked against its own .exp and against a fresh run of
//      probes/probe.py on this exact working tree before being pinned.
//
//  Mutation-proved (S/t122/mut/mutate.py's own ten mutants, applied to a scratch tree of THIS
//  working tree, one production DLL swapped into an isolated reflection harness — never this
//  repo's test binary, never this repo's source): every mutant flips at least one assertion
//  below, reported in the hand-back. See each test's own doc comment for which.
// ================================================================================================

/// <summary>Probe sources, verbatim from the implementer's scratch probes (S/t122/probes,
/// probes2, probes4) — never hand-rolled, so each is exactly what was measured.</summary>
internal static class LambdaCaptureSetProbes
{
    /// <summary>K1 — the base shape: n captured by reference, bump() between two reads.</summary>
    internal const string K1 = """
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

    internal const string K1Expected = "3,103";

    /// <summary>K8 — a NESTED lambda: outer creates inner, inner writes n. n must be call-visible
    /// in Main (two levels out), and outer's OWN capture set must carry it too (the transitive
    /// step LambdaCapturesOf takes for a lambda created inside another).</summary>
    internal const string K8 = """
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

    internal const string K8Expected = "3,103";

    /// <summary>K9 — the lambda's OWN parameter is spelled exactly like the creator's local (n).
    /// The lambda never touches the creator's n at all; only its own parameter. Precision: n must
    /// stay call-INvisible (private).</summary>
    internal const string K9 = """
        Sub Main()
            Dim n As Integer = 1
            Dim q As Integer = 2
            Dim show = Sub(n As Integer) Console.WriteLine(n)
            Dim a As Integer = n + q
            show(50)
            Dim b As Integer = n + q
            Console.WriteLine(CStr(a) & "," & CStr(b))
        End Sub
        """;

    /// <summary>K10 — m and q are NEVER touched by bump (only n is); the precision case the
    /// implementer measured by name: m + q now folds/merges across the bump() call.</summary>
    internal const string K10 = """
        Sub Main()
            Dim n As Integer = 1
            Dim m As Integer = 7
            Dim q As Integer = 2
            Dim bump = Sub() n = n + 100
            Dim a As Integer = m + q
            bump()
            Dim b As Integer = m + q
            Console.WriteLine(CStr(a) & "," & CStr(b) & "," & CStr(n))
        End Sub
        """;

    /// <summary>K11 — LICM: x is captured and written by bump(), called every iteration.</summary>
    internal const string K11 = """
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

    internal const string K11Expected = "30";

    /// <summary>K12 — a captured BY-VALUE PARAMETER (not a Dim'd local) of an enclosing Sub —
    /// the shape ADR-0006 D1's own doc comment measures by name.</summary>
    internal const string K12 = """
        Sub Work(p As Integer, q As Integer)
            Dim bump = Sub() p = p + 100
            Dim a As Integer = p + q
            bump()
            Dim b As Integer = p + q
            Console.WriteLine(CStr(a) & "," & CStr(b))
        End Sub

        Sub Main()
            Work(1, 2)
        End Sub
        """;

    internal const string K12Expected = "3,103";

    /// <summary>K14 — the CREATOR declares N (uppercase); the lambda's body writes n (lowercase).
    /// BasicLang resolves both to the SAME variable (case-insensitive), and IRBuilder records the
    /// write under whichever spelling the write itself uses — so IsLambdaCaptured's own compare
    /// must be case-INSENSITIVE for IsCallVisible("N", Main) to see it.</summary>
    internal const string K14 = """
        Sub Main()
            Dim N As Integer = 1
            Dim q As Integer = 2
            Dim bump = Sub() n = n + 100
            Dim a As Integer = N + q
            bump()
            Dim b As Integer = N + q
            Console.WriteLine(CStr(a) & "," & CStr(b))
        End Sub
        """;

    /// <summary>N1 — captured (n) and uncaptured (m) locals SIDE BY SIDE in the same function, the
    /// same bump() call between two reads of each.</summary>
    internal const string N1 = """
        Function Seed(v As Integer) As Integer
            Return v
        End Function

        Sub Main()
            Dim n As Integer = Seed(1)
            Dim m As Integer = Seed(7)
            Dim q As Integer = Seed(2)
            Dim bump = Sub() n = n + 100
            Dim a As Integer = n + q
            Dim c As Integer = m + q
            bump()
            Dim b As Integer = n + q
            Dim d As Integer = m + q
            Console.WriteLine(CStr(a) & "," & CStr(b) & "," & CStr(c) & "," & CStr(d))
        End Sub
        """;

    internal const string N1Expected = "3,103,9,9";

    /// <summary>N4b (pin g) — the lambda's OWN parameter is N (uppercase); its body writes n
    /// (lowercase). Under the CURRENT IR binding this is NOT the shadowing case K9 pins: N4b's
    /// parameter binds to the LAMBDA's own N, but the body's bare `n` resolves to the CREATOR's
    /// variable (task #169 is the open question of whether VB should instead bind it to the
    /// lambda's own N case-insensitively — not fixed here, and this pin is IR semantics, not a
    /// VB-binding claim). So the creator's n IS written, and must stay call-visible.</summary>
    internal const string N4b = """
        Function Seed(v As Integer) As Integer
            Return v
        End Function

        Sub Main()
            Dim n As Integer = Seed(1)
            Dim q As Integer = Seed(2)
            Dim setp = Sub(N As Integer) n = n + 100
            Dim a As Integer = n + q
            setp(5)
            Dim b As Integer = n + q
            Console.WriteLine(CStr(a) & "," & CStr(b))
        End Sub
        """;

    /// <summary>N5 — three by-value parameters; bump() touches only p. r sits alongside p,
    /// untouched — the "uncaptured by-value parameter" companion to K12/N4's captured one.</summary>
    internal const string N5 = """
        Function Seed(v As Integer) As Integer
            Return v
        End Function

        Sub Work(p As Integer, q As Integer, r As Integer)
            Dim bump = Sub() p = p + 100
            Dim a As Integer = p + q
            Dim c As Integer = r + q
            bump()
            Dim b As Integer = p + q
            Dim d As Integer = r + q
            Console.WriteLine(CStr(a) & "," & CStr(b) & "," & CStr(c) & "," & CStr(d))
        End Sub

        Sub Main()
            Work(Seed(1), Seed(2), Seed(7))
        End Sub
        """;

    /// <summary>N7 — LICM precision: x is captured (bump() writes it every iteration), y is
    /// never touched by any lambda. y's shift must hoist; x's must not.</summary>
    internal const string N7 = """
        Function Seed(v As Integer) As Integer
            Return v
        End Function

        Sub Main()
            Dim x As Integer = Seed(3)
            Dim y As Integer = Seed(5)
            Dim s As Integer = 0
            Dim t As Integer = 0
            Dim bump = Sub() x = x + 1
            For i As Integer = 1 To 3
                bump()
                s = s + x * 2
                t = t + y * 2
            Next
            Console.WriteLine(CStr(s) & "," & CStr(t))
        End Sub
        """;

    /// <summary>N8_javascript (fallback i) — bump's ENTIRE body is a `javascript{ }` inline-code
    /// block: IRInlineCode's names cannot be enumerated, so LambdaCapturesOf(bump) is null and
    /// Main — which references only this one lambda — never gets a recorded set at all
    /// (LambdaCapturedNames/LambdaCaptureSources both stay null). Fallback: every local, including
    /// the untouched q, must stay call-visible.</summary>
    internal const string N8Javascript = """
        Function Seed(v As Integer) As Integer
            Return v
        End Function

        Sub Main()
            Dim n As Integer = Seed(1)
            Dim m As Integer = Seed(7)
            Dim q As Integer = Seed(2)
            Dim bump = Sub()
                           javascript{ n = n + 100; }
                       End Sub
            Dim a As Integer = n + q
            Dim c As Integer = m + q
            bump()
            Dim b As Integer = n + q
            Dim d As Integer = m + q
            Console.WriteLine(CStr(a) & "," & CStr(b) & "," & CStr(c) & "," & CStr(d))
        End Sub
        """;

    internal const string N8JavascriptExpected = "3,103,9,9";

    /// <summary>N8m (fallback d2) — TWO lambdas: "other" is a normal, fully-recorded lambda
    /// (captures {m}); "bump" is opaque (javascript{ } inline code, captures null). Main
    /// references BOTH. Because "other" recorded successfully, Main.LambdaCaptureSources is
    /// NON-null — but it does not list "bump", so IsLambdaCaptured's "every referenced lambda
    /// accounted for" check must still trip the fallback for the WHOLE function, not just for
    /// names "other" happens to have recorded. q (touched by nothing) is the discriminating name:
    /// it is not in the recorded {m}, so only the fallback (not the recorded set) can make it
    /// call-visible.</summary>
    internal const string N8m = """
        Function Seed(v As Integer) As Integer
            Return v
        End Function

        Sub Main()
            Dim n As Integer = Seed(1)
            Dim m As Integer = Seed(7)
            Dim q As Integer = Seed(2)
            Dim other = Sub() Console.WriteLine(CStr(m))
            Dim bump = Sub()
                           javascript{ n = n + 100; }
                       End Sub
            Dim a As Integer = n + q
            Dim c As Integer = m + q
            bump()
            other()
            Dim b As Integer = n + q
            Dim d As Integer = m + q
            Console.WriteLine(CStr(a) & "," & CStr(b) & "," & CStr(c) & "," & CStr(d))
        End Sub
        """;

    internal const string N8mExpected = "7\n3,103,9,9";

    /// <summary>N8n (fallback j) — the OPAQUE lambda is NESTED two levels down (Main creates
    /// outer, outer calls Run(nested-inline-lambda)). Nested's captures are null, so outer's own
    /// LambdaCapturesOf must ALSO return null (its own "recorded?" check on the lambda names ITS
    /// IR references), which in turn leaves Main's set null too — the unresolved-ness must
    /// propagate up through every level, not just the innermost one.</summary>
    internal const string N8n = """
        Function Seed(v As Integer) As Integer
            Return v
        End Function

        Sub Run(f As Action)
            f()
        End Sub

        Sub Main()
            Dim n As Integer = Seed(1)
            Dim m As Integer = Seed(7)
            Dim q As Integer = Seed(2)
            Dim outer = Sub()
                            Run(Sub()
                                    javascript{ n = n + 100; }
                                End Sub)
                        End Sub
            Dim a As Integer = n + q
            Dim c As Integer = m + q
            outer()
            Dim b As Integer = n + q
            Dim d As Integer = m + q
            Console.WriteLine(CStr(a) & "," & CStr(b) & "," & CStr(c) & "," & CStr(d))
        End Sub
        """;

    internal const string N8nExpected = "3,103,9,9";

    /// <summary>N9 (pin h) — the lambda WRITES the creator's n on its FIRST line, then declares
    /// its OWN local also spelled n. The write happens before the lambda's own Dim takes effect,
    /// so it names the CREATOR's variable; the lambda's own locals are deliberately NOT
    /// subtracted from the capture set (C# never declares a lambda's locals at all — a lambda
    /// local spelled like a creator local IS the creator's variable in emitted C#), so n must
    /// stay captured.</summary>
    internal const string N9 = """
        Function Seed(v As Integer) As Integer
            Return v
        End Function

        Sub Main()
            Dim n As Integer = Seed(1)
            Dim q As Integer = Seed(2)
            Dim bump = Sub()
                           n = n + 100
                           Dim n As Integer = 5
                           Console.WriteLine(n)
                       End Sub
            Dim a As Integer = n + q
            bump()
            Dim b As Integer = n + q
            Console.WriteLine(CStr(a) & "," & CStr(b))
        End Sub
        """;
}

/// <summary>
/// Part 1 — the capture set itself, read straight off IR built from real source through the
/// normal front end (no optimizer pipeline run). Every assertion here is a direct call to
/// <c>OptimizationPass.IsCallVisible</c> / <c>IsLambdaCaptured</c> / <c>LambdaCapturesOf</c>, so a
/// wrong answer here is a wrong answer in EVERY consumer (CSE, CopyPropagation, LICM, the
/// verifier's Guard(v) arm) without needing to run any of them.
/// </summary>
[TestFixture]
public class LambdaCaptureSetIrLevelTests
{
    private static IRFunction Fn(IRModule module, string name) =>
        module.Functions.First(f => f.Name == name && !f.IsExternal);

    /// <summary>K10 — n IS captured (bump writes it); m and q are NOT (bump never touches
    /// either).
    /// <para>⛔ MUTANT Me_always_empty (the final name-compare inside IsLambdaCaptured always
    /// returns false) kills the FIRST assertion: n's own recorded capture set contains "n", but
    /// the mutated compare never reports a match, so IsCallVisible("n", Main) wrongly becomes
    /// false — UNSOUND (a call could still write n through the lambda).</para>
    /// </summary>
    [Test]
    public void K10_NIsCaptured_MAndQAreNot()
    {
        var module = JsTestSupport.BuildModule(LambdaCaptureSetProbes.K10, sourceFilePath: "prog.bas");
        var main = Fn(module, "Main");

        Assert.Multiple(() =>
        {
            Assert.That(OptimizationPass.IsCallVisible("n", main), Is.True,
                "n is written inside bump() -- a call to bump() must be able to invalidate it.");
            Assert.That(OptimizationPass.IsCallVisible("m", main), Is.False,
                "m is never touched by any lambda in Main -- it must stay private.");
            Assert.That(OptimizationPass.IsCallVisible("q", main), Is.False,
                "q is never touched by any lambda in Main -- it must stay private.");
        });
    }

    /// <summary>K8 — nested lambdas: inner writes n, outer merely calls inner. n must be
    /// call-visible in MAIN (two levels out), and outer's OWN recorded capture set must carry it
    /// too (the transitive step).
    /// <para>⛔ MUTANT Ma_no_transitivity (skip merging a nested lambda's captures into its own
    /// creator) kills the first assertion: outer's capture set would be empty (outer's own IR
    /// never mentions n directly, only inner's), so Main would never learn n is captured and
    /// IsCallVisible("n", Main) wrongly becomes false — UNSOUND.</para>
    /// </summary>
    [Test]
    public void K8_NestedLambda_CaptureTwoLevelsOut_CarriedThroughTheIntermediate()
    {
        var module = JsTestSupport.BuildModule(LambdaCaptureSetProbes.K8, sourceFilePath: "prog.bas");
        var main = Fn(module, "Main");
        var outer = Fn(module, "__lambda_0"); // IRBuilder names outer before building its body (see
                                               // IRBuilder.Visit(LambdaExpressionNode)'s own comment
                                               // on why a dedicated counter is grabbed up front),
                                               // so outer gets a LOWER number than the nested inner.

        Assert.Multiple(() =>
        {
            Assert.That(OptimizationPass.IsCallVisible("n", main), Is.True,
                "inner writes n; outer calls inner -- n must be call-visible in Main too.");
            Assert.That(outer.LambdaCapturedNames, Is.Not.Null.And.Contains("n"),
                "outer's OWN recorded capture set must carry n, merged transitively from inner's.");
        });
    }

    /// <summary>K12 — a captured BY-VALUE PARAMETER (not a Dim'd local).
    /// <para>⛔ MUTANT Mb_params_excluded (IsCallVisible's parameter arm drops the
    /// IsLambdaCaptured call entirely, `return parameter.IsByRef;`) kills this test: a by-value,
    /// non-ByRef parameter can never be call-visible under the mutant no matter what a lambda
    /// does to it — UNSOUND (K12's own P.exp is 3,103; the mutant's own matrix run print 3,3).</para>
    /// </summary>
    [Test]
    public void K12_CapturedByValueParameter_IsCallVisible()
    {
        var module = JsTestSupport.BuildModule(LambdaCaptureSetProbes.K12, sourceFilePath: "prog.bas");
        var work = Fn(module, "Work");

        Assert.That(OptimizationPass.IsCallVisible("p", work), Is.True,
            "p is a by-value parameter written inside bump() -- a call to bump() must be able to "
            + "invalidate it, the same as a Dim'd local.");
    }

    /// <summary>N5 — the PRECISION companion to K12: p is captured, r (another by-value
    /// parameter) is not.</summary>
    [Test]
    public void N5_CapturedParameter_AlongsideAnUncapturedOne()
    {
        var module = JsTestSupport.BuildModule(LambdaCaptureSetProbes.N5, sourceFilePath: "prog.bas");
        var work = Fn(module, "Work");

        Assert.Multiple(() =>
        {
            Assert.That(OptimizationPass.IsCallVisible("p", work), Is.True, "p is written inside bump().");
            Assert.That(OptimizationPass.IsCallVisible("r", work), Is.False,
                "r sits alongside p but no lambda ever touches it -- it must stay private.");
        });
    }

    /// <summary>K14 — the creator declares N (uppercase); the lambda's body writes n (lowercase).
    /// <para>⛔ MUTANT Mc_case_sensitive (the final name compare inside IsLambdaCaptured uses
    /// Ordinal instead of OrdinalIgnoreCase) kills this test: the recorded capture carries
    /// whichever spelling the WRITE uses ("n"), and an exact-case compare against the DECLARED
    /// spelling ("N") never matches -- IsCallVisible("N", Main) wrongly becomes false. MEASURED:
    /// K14's own C# leg prints 3,3 under this mutant, 3,103 without it.</para>
    /// </summary>
    [Test]
    public void K14_CreatorDeclaresUppercase_LambdaWritesLowercase_StillCallVisible()
    {
        var module = JsTestSupport.BuildModule(LambdaCaptureSetProbes.K14, sourceFilePath: "prog.bas");
        var main = Fn(module, "Main");

        Assert.That(OptimizationPass.IsCallVisible("N", main), Is.True,
            "BasicLang is case-insensitive -- N (declared) and n (written inside the lambda) are "
            + "the SAME variable, and the capture-set compare must see that.");
    }

    /// <summary>K9 — the lambda's OWN parameter shadows the creator's local, EXACT spelling. The
    /// lambda never touches the creator's n; only its own parameter. Precision only: this narrows
    /// "every local" to "captured locals", and does not change any program's OUTPUT (n is never
    /// actually read after the call in K9's own probe).
    /// <para>⛔ MUTANT Mf_no_param_subtraction (skip removing the lambda's own parameter names
    /// from its capture set) kills this test: without the subtraction, "n" (the parameter
    /// mention) stays in the capture set and IsCallVisible("n", Main) wrongly becomes True. This
    /// mutant produced ZERO probe-matrix differences at the implementer's own measurement (the
    /// K9 program never reads n again after show(50), so no OUTPUT changes) -- only this
    /// IR-level assertion can see it, exactly as the brief requires.</para>
    /// </summary>
    [Test]
    public void K9_LambdaParameterShadowsCreatorLocal_ExactSpelling_StaysPrivate()
    {
        var module = JsTestSupport.BuildModule(LambdaCaptureSetProbes.K9, sourceFilePath: "prog.bas");
        var main = Fn(module, "Main");

        Assert.That(OptimizationPass.IsCallVisible("n", main), Is.False,
            "show's own parameter n shadows the creator's n by EXACT spelling -- the lambda's body "
            + "reads only ITS OWN n, never the creator's, so the creator's n must stay private.");
    }

    /// <summary>N4b (pin g) — the lambda's OWN parameter is N (uppercase); its body writes the
    /// bare name n (lowercase). Under the CURRENT IR binding this is the creator's n (task #169
    /// is the open question of whether VB should instead bind a case-insensitive match to the
    /// lambda's OWN parameter — an IR-BINDING question, not this capture-set rule; #169 is not
    /// fixed here and this pin is IR semantics under today's binding, not a claim about what VB
    /// SHOULD do). So the creator's n genuinely is captured and must stay call-visible.
    /// <para>⛔ MUTANT Mg_param_subtraction_ignorecase (the parameter-name subtraction compares
    /// case-INSENSITIVELY instead of exactly) kills this test: it would remove "n" from the
    /// capture set too (since "N" case-insensitively matches "n"), even though the write is to
    /// the CREATOR's variable, not the parameter -- IsCallVisible("n", Main) wrongly becomes
    /// False, UNSOUND. MEASURED: N4b's own JS leg prints 3,3 under this mutant, 3,103 without
    /// it.</para>
    /// </summary>
    [Test]
    public void N4b_LambdaParameterUppercaseN_BodyWritesLowercaseN_CreatorsNStaysCaptured()
    {
        var module = JsTestSupport.BuildModule(LambdaCaptureSetProbes.N4b, sourceFilePath: "prog.bas");
        var main = Fn(module, "Main");

        Assert.That(OptimizationPass.IsCallVisible("n", main), Is.True,
            "under the CURRENT IR binding the body's bare n resolves to the CREATOR's n (task "
            + "#169 tracks whether VB should instead bind it to the lambda's own parameter N) -- "
            + "so the creator's n is genuinely written and must stay call-visible.");
    }

    /// <summary>N9 (pin h) — the lambda writes the creator's n on its FIRST line, then declares
    /// its OWN local ALSO spelled n. Own locals are deliberately NOT subtracted from the capture
    /// set (a lambda body can use the creator's n before its own Dim n takes effect, and the C#
    /// backend never declares a lambda's locals at all -- a lambda-local n IS the creator's n in
    /// emitted C#).
    /// <para>⛔ MUTANT Mh_own_locals_subtracted (also remove the lambda's OWN LocalVariables from
    /// its capture set, the same way parameters are removed) kills this test: bump.LocalVariables
    /// contains its own "n", so the subtraction would remove the very capture the first-line
    /// write recorded -- IsCallVisible("n", Main) wrongly becomes False, UNSOUND. This mutant
    /// also produced ZERO probe-matrix differences at the implementer's own measurement (N9 never
    /// prints n again after bump()) -- only this IR-level assertion can see it.</para>
    /// </summary>
    [Test]
    public void N9_LambdaLocalShadowsCreatorLocal_OwnLocalsNotSubtracted_StaysCaptured()
    {
        var module = JsTestSupport.BuildModule(LambdaCaptureSetProbes.N9, sourceFilePath: "prog.bas");
        var main = Fn(module, "Main");

        Assert.That(OptimizationPass.IsCallVisible("n", main), Is.True,
            "the lambda's FIRST line writes the creator's n, before its own Dim n takes effect -- "
            + "own locals are deliberately not subtracted from the capture set, so n must stay "
            + "captured.");
    }

    /// <summary>K1 — CapturedVariables is no longer always empty (the dead loop over `_locals`,
    /// which nothing ever filled, is gone; it is now read straight off LambdaCapturesOf).</summary>
    [Test]
    public void K1_CapturedVariables_IsNoLongerAlwaysEmpty()
    {
        var module = JsTestSupport.BuildModule(LambdaCaptureSetProbes.K1, sourceFilePath: "prog.bas");
        var bump = Fn(module, "__lambda_0");

        Assert.That(bump.CapturedVariables, Is.Not.Null.And.Not.Empty);
        Assert.That(bump.CapturedVariables!.Select(c => c.name), Does.Contain("n"));
    }
}

/// <summary>
/// Part 2 — the three FALLBACK shapes reachable from real front-end source (an opaque lambda; an
/// opaque lambda alongside a properly-recorded one; a nested opaque lambda). The canonical
/// HAND-BUILT fallback pin (a function referencing __lambda_0 whose LambdaCapturedNames/
/// LambdaCaptureSources are left null entirely, because nothing ever recorded them) already
/// exists in the suite and is not duplicated here:
/// <list type="bullet">
/// <item><c>CopyPropagationSharedVocabularyTests.U7_ClosureRule_MakesADeclaredLocalFact_
/// CallVisible</c></item>
/// <item><c>KillVocabularyExtensionArmTests.ClosureRule_MakesAByValueParameter_CallVisible_
/// WhenTheFunctionContainsALambda</c> (KillVocabularyExtensionsTests.cs)</item>
/// </list>
/// Both are killed by MUTANT Md1_null_means_nothing (the top-of-IsLambdaCaptured null check
/// returns false instead of true) — and so, independently, are the two tests below whose ONLY
/// referenced lambda is itself opaque (N8_javascript, N8n): with a single unrecorded lambda,
/// the creator's OWN set never gets initialised at all, hitting the exact same top-level null
/// check as the hand-built pins above, from real source instead of hand-built IR.
/// </summary>
[TestFixture]
public class LambdaCaptureSetFallbackTests
{
    private static IRFunction Fn(IRModule module, string name) =>
        module.Functions.First(f => f.Name == name && !f.IsExternal);

    /// <summary>N8_javascript (fallback i) — bump's entire body is `javascript{ }` inline code:
    /// IRInlineCode's names cannot be enumerated, so LambdaCapturesOf(bump) is null, and Main
    /// (which references only this one lambda) never gets ANY recorded set.
    /// <para>⛔ MUTANT Mi_opaque_complete (the `if (writes.IsUniversal) return false;` guard
    /// inside LambdaCapturesOf's per-instruction walk is disabled) kills this test: bump's inline
    /// code would then be treated as a COMPLETE, fully-enumerated instruction contributing no
    /// names at all, so LambdaCapturesOf(bump) becomes an EMPTY (not null) dictionary; Main gets a
    /// real-looking but wrong recorded set, and IsCallVisible("q", Main) wrongly becomes False —
    /// UNSOUND (the inline code could write q too; BasicLang cannot see inside it at all).</para>
    /// <para>⛔ Also killed by MUTANT Md1_null_means_nothing (see this fixture's own header).</para>
    /// </summary>
    [Test]
    public void N8Javascript_OpaqueLambda_MainFallsBackForEveryName()
    {
        var module = JsTestSupport.BuildModule(LambdaCaptureSetProbes.N8Javascript, sourceFilePath: "prog.bas");
        var main = Fn(module, "Main");

        Assert.That(OptimizationPass.IsCallVisible("q", main), Is.True,
            "bump's body is opaque inline code -- BasicLang cannot see what it writes, so EVERY "
            + "local, including q (which nothing in this program actually touches), must fall "
            + "back to call-visible.");
    }

    /// <summary>N8m (fallback d2) — TWO lambdas: "other" is ordinary and fully recorded (captures
    /// {m}); "bump" is opaque (javascript{ } inline code, captures null). Main references BOTH.
    /// Because "other" recorded successfully, Main.LambdaCaptureSources is NON-null (it lists
    /// "other") -- so the top-level null check alone would NOT catch this; IsLambdaCaptured's
    /// SEPARATE "is every referenced lambda accounted for" loop is what must still trip the
    /// fallback for the whole function.
    /// <para>⛔ MUTANT Md2_unaccounted_ignored (`if (!recorded.Contains(lambda)) return true;`
    /// becomes `continue;`, so an unaccounted-for lambda reference is silently skipped) kills
    /// this test: with the check skipped, IsLambdaCaptured falls through to checking the
    /// (incomplete) recorded set directly -- {m} does not contain "q", so
    /// IsCallVisible("q", Main) wrongly becomes False -- UNSOUND (bump's opaque body could write
    /// q too). This is exactly why q, not m, is the discriminating name here: m would stay
    /// call-visible either way (it IS in the recorded set), so asserting on m alone would not
    /// kill this mutant.</para>
    /// <para>⛔ Also killed by MUTANT Mi_opaque_complete (bump's inline code would then
    /// contribute an empty-not-null capture set, which gets recorded as an accounted-for lambda
    /// with nothing in it, tripping the SAME q-goes-missing failure via a different route).</para>
    /// </summary>
    [Test]
    public void N8m_OneOpaqueLambdaAlongsideARecordedOne_MainFallsBackForEveryName()
    {
        var module = JsTestSupport.BuildModule(LambdaCaptureSetProbes.N8m, sourceFilePath: "prog.bas");
        var main = Fn(module, "Main");

        Assert.That(OptimizationPass.IsCallVisible("q", main), Is.True,
            "bump (opaque) is UNACCOUNTED for in Main's recorded set even though other's capture "
            + "of m WAS recorded -- one unaccounted lambda must fall the whole function back, not "
            + "just the names the recorded lambdas happen to have listed.");
    }

    /// <summary>N8n (fallback j) — the opaque lambda is NESTED two levels down: Main creates
    /// outer, outer calls Run(an inline-lambda argument). The innermost lambda's captures are
    /// null, so OUTER's own LambdaCapturesOf must ALSO return null (its own "is every lambda I
    /// reference accounted for" check, on outer's own IR), which leaves Main's set null too --
    /// the unresolved-ness must propagate through every level, not just the innermost one.
    /// <para>⛔ MUTANT Mj_nested_unchecked (the nested-lambda "recorded?" check inside
    /// LambdaCapturesOf is removed, `if (false) return null;`) kills this test: outer's own
    /// capture computation would then proceed as if fully known (outer's IR mentions no names of
    /// its own besides the call to Run and the nested-lambda reference, so it would compute an
    /// EMPTY, non-null capture set); Main would get outer recorded with nothing captured, and
    /// IsCallVisible("q", Main) wrongly becomes False -- UNSOUND.</para>
    /// <para>⛔ Also killed by MUTANT Md1_null_means_nothing and MUTANT Mi_opaque_complete (both
    /// reach the same q-goes-missing failure via a different route through this same shape).</para>
    /// </summary>
    [Test]
    public void N8n_NestedOpaqueLambda_UnresolvedPropagatesThroughEveryLevel()
    {
        var module = JsTestSupport.BuildModule(LambdaCaptureSetProbes.N8n, sourceFilePath: "prog.bas");
        var main = Fn(module, "Main");

        Assert.That(OptimizationPass.IsCallVisible("q", main), Is.True,
            "the innermost lambda (two levels down) is opaque -- outer's own capture set must "
            + "come back null too, and so must Main's, falling every local back to call-visible.");
    }
}

/// <summary>
/// Part 3 — the PRECISION gain itself, on the built IR (no process spawned): K10's b = m + q
/// folds to a literal even AFTER the bump() call (m and q are not in bump's capture set), and
/// N7's LICM hoists an uncaptured local's shift out of a loop that calls a lambda while the
/// captured one's stays put. Structural assertions on the pass output, matching
/// <c>LicmKillVocabularyStructuralTests</c>' own convention (a single bare pass Run after the
/// standard pipeline settles — never <c>pass.ModificationCount</c> after a full
/// <c>OptimizationPipeline.Run</c>, which iterates to a fixed point and so always reports zero on
/// its last round).
/// </summary>
[TestFixture]
public class LambdaCaptureSetPrecisionStructuralTests
{
    /// <summary>K10 — a = m + q (before bump()) always folds; the PRECISION gain is that
    /// b = m + q (AFTER bump()) folds too, because m and q survive the call as constants now that
    /// they are not in bump's capture set. Under D1's old interim rule (every local call-visible
    /// in a function that contains a lambda) CopyPropagation's constant facts for m and q would
    /// have been invalidated AT the bump() call, and b would still be an actual IRBinaryOp read
    /// of m + q -- not a folded literal.</summary>
    [Test]
    public void K10_BFoldsToALiteral_AcrossTheCall_BecauseMAndQAreNotCaptured()
    {
        var module = JsTestSupport.BuildModule(LambdaCaptureSetProbes.K10, sourceFilePath: "prog.bas");
        var pipeline = new OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.Run(module);

        var main = module.Functions.Single(f => f.Name == "Main" && !f.IsExternal);
        var entry = main.Blocks.Single(b => b.Name == "entry");
        IRConstant ValueOf(string name) =>
            entry.Instructions.OfType<IRAssignment>().LastOrDefault(a => a.Target?.Name == name)?.Value as IRConstant;

        Assert.Multiple(() =>
        {
            Assert.That(ValueOf("a")?.Value, Is.EqualTo(9), "a = m + q, BEFORE bump() -- folds regardless of captures.");
            Assert.That(ValueOf("b")?.Value, Is.EqualTo(9),
                "b = m + q, AFTER bump() -- only folds to a literal because m and q are NOT in "
                + "bump's capture set (task #122); under the old interim rule this would still be "
                + "an IRBinaryOp reading m and q, not a folded 9.");
        });
    }

    /// <summary>N7 — x is captured and written by bump(), called every iteration; y is never
    /// touched by any lambda. y's shift (StrengthReductionPass turns * 2 into &lt;&lt; 1, a
    /// standard pass that runs before LICM) must hoist into the preheader; x's must stay in the
    /// loop body.</summary>
    [Test]
    public void N7_LicmHoistsTheUncapturedShift_KeepsTheCapturedOneInTheLoop()
    {
        var module = JsTestSupport.BuildModule(LambdaCaptureSetProbes.N7, sourceFilePath: "prog.bas");
        var standard = new OptimizationPipeline();
        standard.AddStandardPasses();
        standard.Run(module);

        var main = module.Functions.Single(f => f.Name == "Main" && !f.IsExternal);
        var pass = new LoopInvariantCodeMotionPass();
        bool changed = pass.Run(module);

        var entry = main.Blocks.Single(b => b.Name == "entry");
        var body = main.Blocks.Single(b => b.Name == "for0.body");
        static bool ReferencesVariable(IRBinaryOp op, string name) =>
            (op.Left as IRVariable)?.Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true ||
            (op.Right as IRVariable)?.Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true;

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True, "y's shift is genuinely invariant (no lambda touches y) -- it must hoist.");
            Assert.That(pass.ModificationCount, Is.EqualTo(1));
            Assert.That(entry.Instructions.OfType<IRBinaryOp>().Any(op => ReferencesVariable(op, "y")), Is.True,
                "y's shift must be hoisted into the preheader (entry) -- y is never touched by bump().");
            Assert.That(body.Instructions.OfType<IRBinaryOp>().Any(op => ReferencesVariable(op, "x")), Is.True,
                "x's shift must stay in for0.body -- x is captured and written by bump(), called every iteration.");
        });
    }
}

/// <summary>
/// Part 4 — real compiled-and-run JavaScript, the AGGRESSIVE pipeline (FourBackends.RunAggressiveJs
/// -> JsTestSupport.CompileAggressive -> AggressivePipeline.Apply, the same pipeline --optimize
/// and a Release .blproj build both run). C++ is task #140 (the backend's own lambda lowering
/// captures BY COPY, wrong even with zero optimizer passes running); MSIL is task #155 (no
/// lowering for the delegate type a Sub() lambda gets typed as, "Reference to undefined class
/// 'Action'"); C# inlines a single-use lambda body at its call site regardless of the closure
/// rule, so none of the three can OBSERVE #122's precision gain (or its absence) at run time --
/// JavaScript is the judge, matching this suite's convention throughout the ADR-0006 D1 family.
/// Every expected string here is each probe's own .exp (S/t122/probes, probes2, probes4),
/// re-verified with a fresh run of probes/probe.py on this exact working tree before being
/// pinned.
/// </summary>
[TestFixture]
[Category("Integration")]
public class LambdaCaptureSetExecutionTests
{
    [Test]
    public void K1_JavaScript_Aggressive()
        => Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(LambdaCaptureSetProbes.K1)),
            Is.EqualTo(LambdaCaptureSetProbes.K1Expected));

    /// <summary>K8's C# leg fails to build for an UNRELATED, pre-existing reason (CS0103: "The
    /// name 'inner' does not exist in the current context" -- a multi-line nested-lambda-body
    /// scoping gap the implementer's brief already named, not touched by #122) -- excluded,
    /// matching this suite's convention of never asserting against a leg that cannot build.</summary>
    [Test]
    public void K8_JavaScript_Aggressive_NestedCaptureTwoLevelsOut()
        => Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(LambdaCaptureSetProbes.K8)),
            Is.EqualTo(LambdaCaptureSetProbes.K8Expected));

    [Test]
    public void K11_JavaScript_Aggressive_LicmLoopWithACapturedLocal()
        => Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(LambdaCaptureSetProbes.K11)),
            Is.EqualTo(LambdaCaptureSetProbes.K11Expected));

    [Test]
    public void K12_JavaScript_Aggressive_CapturedByValueParameter()
        => Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(LambdaCaptureSetProbes.K12)),
            Is.EqualTo(LambdaCaptureSetProbes.K12Expected));

    [Test]
    public void N1_JavaScript_Aggressive_CapturedAndUncapturedSideBySide()
        => Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(LambdaCaptureSetProbes.N1)),
            Is.EqualTo(LambdaCaptureSetProbes.N1Expected));

    /// <summary>N8m — C#/C++/MSIL all refuse to build this shape outright (bump's body is
    /// `javascript{ }` inline code, which only the JavaScript backend can lower at all) --
    /// excluded, matching every inline-code probe in this suite (KillVocabularyExtensionsTests'
    /// own IN_javascript/IN_cpp probes).</summary>
    [Test]
    public void N8m_JavaScript_Aggressive_OneOpaqueLambdaAlongsideARecordedOne()
        => Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(LambdaCaptureSetProbes.N8m)),
            Is.EqualTo(LambdaCaptureSetProbes.N8mExpected));

    /// <summary>N8n — same exclusion as N8m (the opaque lambda is nested, but still
    /// `javascript{ }` inline code only JavaScript can lower).</summary>
    [Test]
    public void N8n_JavaScript_Aggressive_NestedOpaqueLambda()
        => Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(LambdaCaptureSetProbes.N8n)),
            Is.EqualTo(LambdaCaptureSetProbes.N8nExpected));
}
