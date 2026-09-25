using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.JavaScript;
using BasicLang.Compiler.CodeGen.MSIL;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.SemanticAnalysis;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

// =====================================================================================
//  ADR-0006 D2 (task #137) — the use count Invariant S′ checks is DYNAMIC, not static.
//  `BasicLang/IRVerifier.cs`: `CheckFunction` used to gate on
//  `useSites.Count > 1 || (destination != null && useSites.Count >= 1)` alone — a STATIC
//  count. An anonymous value with exactly ONE static use, sitting in a loop that does not
//  contain its definition, runs again before the definition does (the shape LICM's rewrite
//  leaves behind: a hoisted `x * 2` in the preheader, one use inside the loop body) and was
//  never checked at all. D2's ruling (Option A): a use counts as repeated when its block is
//  reachable from the definition and reaches itself without passing through the definition's
//  block again (`DefUsePaths.UseRepeats`); `region(v)` for a repeated use is the FULL body of
//  that loop, including the back edge and the use's own block after the use.
//
//  This file has three fixtures:
//    DynamicUseSPrimeHandBuiltIRTests            — hand-built IR, ported from the implementer's
//                                                   S/adr6-d2/harness/Program.cs shapes (a)-(h). Every
//                                                   FIRES verdict here is QUIET on the pre-D2
//                                                   verifier (measured in S/adr6-d2/hand-before.txt) —
//                                                   these are the shapes the static count missed.
//    DynamicUseSPrimeAggressivePipelineStructuralTests
//                                                 — L1-L7, compiled through the AGGRESSIVE
//                                                   pipeline, asserting CheckInvariantSPrime
//                                                   directly returns zero violations. No process
//                                                   spawned — fast subset.
//    DynamicUseSPrimeAggressivePipelineExecutionTests
//                                                 — the same L1-L7 probes, run for real on every
//                                                   backend the shape allows, through the shared
//                                                   AGGRESSIVE helpers (FourBackends and its per-
//                                                   backend legs) — never CompileOptimized /
//                                                   CompileToCppOptimized (STANDARD-only) or RunJs
//                                                   (no optimizer at all): see CLAUDE.md and
//                                                   docs/HANDOFF.md's notes on the two.
//
//  ⚠ THE TEST HOST ALREADY VERIFIES IN THROW MODE. `VisualGameStudio.Tests.csproj` sets the
//  `BasicLang.VerifyIR` runtime switch, which `IRVerifier.Resolve()` reads whenever nothing has
//  pinned `Mode` first (IRVerifierModeResolutionTests.InTheTestHost_ModeIsThrow pins this).  So
//  every compile below — hand-built or through source — already runs Invariant S′ in
//  IRVerifierMode.Throw: a violation throws IRVerificationException and the test that triggered
//  the compile fails with that exception, not with a wrong printed value. The structural fixture
//  additionally calls CheckInvariantSPrime directly, so "no violation" is asserted rather than
//  merely relied upon not to have thrown.
// =====================================================================================

/// <summary>BASIC source for the one probe this file needs beyond
/// <see cref="LicmKillVocabularyShapes"/> (which already has L1-L6, byte-identical modulo
/// indentation to <c>S/adr6-d1/probes/L1..L6.bas</c> — reused rather than duplicated, per
/// CLAUDE.md's "change shared source once" rule). L7 — CSE across a loop iteration merging a
/// destination-renamed value with a List-indexer store — is copied from
/// <c>S/adr6-d1/probes/L7.bas</c>.</summary>
internal static class DynamicUseSPrimeProbes
{
    internal const string L7 = """
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

    internal const string ExpectedL7 = "seed\nseed\nseed\n3,0\nseed\n3,0";
}

/// <summary>
/// Hand-built IR, ADR-0006 D2's contract shapes — ported one-for-one from the implementer's
/// scratch harness (<c>S/adr6-d2/harness/Program.cs</c>, task #137), ordered and named to match its
/// letters so a mismatch is traceable back to the measurement in
/// <c>S/adr6-d2/hand-final-full.txt</c>/<c>S/adr6-d2/hand-before.txt</c>. Every fixture, helper and shape below
/// builds the SAME six-block skeleton the harness's <c>Fx()</c> does: <c>p</c>, <c>q</c> declared
/// Integer locals, <c>c</c> a declared Boolean local used as every branch condition (never
/// actually assigned — only its identity as a guard variable matters to the CFG walk).
///
/// <para>Each shape's own doc comment states, in the harness's words, what it builds and why it
/// is expected to FIRE or stay QUIET — and, for the eight that flip from the pre-D2 verifier,
/// that the pre-D2 verifier was QUIET on it (measured, not asserted here: the pre-D2 code no
/// longer exists in the working tree to compile against).</para>
/// </summary>
[TestFixture]
public class DynamicUseSPrimeHandBuiltIRTests
{
    private static readonly TypeInfo IntType = new TypeInfo("Integer", TypeKind.Primitive);
    private static readonly TypeInfo BoolType = new TypeInfo("Boolean", TypeKind.Primitive);

    private sealed class Fixture
    {
        public IRModule Module;
        public IRFunction Function;
        public BasicBlock Entry;
        public IRVariable P;
        public IRVariable Q;
        public IRVariable C;
    }

    /// <summary>Matches the harness's <c>Fx()</c> exactly: <c>p</c>/<c>q</c>/<c>c</c> are plain
    /// declared locals (not parameters) — deliberately, since <c>OptimizationPass.IsCallVisible</c>
    /// treats a declared non-const, non-global local the same way whether or not it is a
    /// parameter (no lambda in any of these functions), so the choice does not change what is
    /// measured and keeping it identical to the harness keeps the port honest.</summary>
    private static Fixture NewFixture()
    {
        var module = new IRModule("M");
        var function = new IRFunction("Main", IntType);
        module.Functions.Add(function);
        var p = new IRVariable("p", IntType);
        var q = new IRVariable("q", IntType);
        var c = new IRVariable("c", BoolType);
        function.LocalVariables.Add(p);
        function.LocalVariables.Add(q);
        function.LocalVariables.Add(c);
        var entry = function.CreateBlock("entry");
        return new Fixture { Module = module, Function = function, Entry = entry, P = p, Q = q, C = c };
    }

    /// <summary>A call whose only role is to READ <paramref name="x"/> — every shape's use site.</summary>
    private static IRCall Use(IRValue x)
    {
        var call = new IRCall("", "Show", IntType);
        call.Arguments.Add(x);
        return call;
    }

    /// <summary>An assignment to <paramref name="x"/> — every shape's candidate write.</summary>
    private static IRAssignment Write(IRVariable x) => new IRAssignment(x, new IRConstant(5, IntType));

    /// <summary>
    /// Asserts QUIET, or FIRES with the ADR-0006 D2 fields that discriminate it from a pre-D2
    /// (static-count) violation: <see cref="InvariantViolation.UseRepeats"/> (always asserted when
    /// a violation is expected — that is the whole point of this fixture), plus
    /// <see cref="InvariantViolation.Variable"/>, <see cref="InvariantViolation.WriterBlock"/> and
    /// <see cref="InvariantViolation.UseBlock"/> where the caller supplies them.
    /// </summary>
    private static void AssertVerdict(IRModule module, bool expectFires, bool expectUseRepeats = true,
        string expectVariable = null, string expectWriterBlock = null, string expectUseBlock = null)
    {
        var violations = IRVerifier.CheckInvariantSPrime(module);
        if (!expectFires)
        {
            Assert.That(violations, Is.Empty,
                "expected QUIET; got: " + string.Join(" | ", violations.Select(v => v.ToString())));
            return;
        }

        Assert.That(violations, Is.Not.Empty, "expected FIRES and got QUIET");
        Assert.Multiple(() =>
        {
            Assert.That(violations[0].UseRepeats, Is.EqualTo(expectUseRepeats),
                "UseRepeats (ADR-0006 D2) — got: " + violations[0]);
            if (expectVariable != null)
                Assert.That(violations[0].Variable, Is.EqualTo(expectVariable), "wrong guarded variable — got: " + violations[0]);
            if (expectWriterBlock != null)
                Assert.That(violations[0].WriterBlock, Is.EqualTo(expectWriterBlock), "wrong writer block — got: " + violations[0]);
            if (expectUseBlock != null)
                Assert.That(violations[0].UseBlock, Is.EqualTo(expectUseBlock), "wrong use block — got: " + violations[0]);
        });
    }

    // ---- (a) preheader def, ONE use in the loop, Guard-name write AFTER the use, no destination.

    /// <summary>
    /// ⭐ THE M1 MUTANT KILL, and THE MESSAGE-TEXT PIN. <c>t0 = p + q</c> defined in the preheader
    /// (<c>entry</c>); its one use is a call inside <c>loop.body</c>; <c>p</c> is written AFTER
    /// that use, same block. Anonymous, one STATIC use — before D2 this was never checked at all
    /// (QUIET, measured in S/adr6-d2/hand-before.txt). After D2: the use's block is on a cycle that
    /// avoids the definition's block (the loop runs again without re-executing the preheader), so
    /// it is dynamically shared and the write is caught. Asserts the exact violation message —
    /// the "; the use in X repeats in a loop that does not contain the definition" clause is new
    /// surface this ADR adds to <see cref="InvariantViolation.ToString"/>.
    /// </summary>
    [Test]
    public void A_PreheaderDef_OneUseInLoop_WriteAfterUse_SameBlock_Fires()
    {
        var f = NewFixture();
        var head = f.Function.CreateBlock("loop.head");
        var body = f.Function.CreateBlock("loop.body");
        var exit = f.Function.CreateBlock("loop.exit");
        var v = new IRBinaryOp("t0", BinaryOpKind.Add, f.P, f.Q, IntType);
        f.Entry.Instructions.Add(v);
        f.Entry.Instructions.Add(new IRBranch(head));
        head.Instructions.Add(new IRConditionalBranch(f.C, body, exit));
        body.Instructions.Add(Use(v));
        body.Instructions.Add(Write(f.P));
        body.Instructions.Add(new IRBranch(head));
        exit.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectFires: true, expectVariable: "p", expectWriterBlock: "loop.body", expectUseBlock: "loop.body");

        var violations = IRVerifier.CheckInvariantSPrime(f.Module);
        Assert.That(violations[0].ToString(), Is.EqualTo(
            "Invariant S′ violated in Main: 't0' (IRBinaryOp, 1 use(s), defined in entry; the use in loop.body repeats "
            + "in a loop that does not contain the definition) — operand 'p' is written by IRAssignment in loop.body "
            + "before a use in loop.body."),
            "the new D2 clause in InvariantViolation.ToString() — pin the exact wording");
    }

    // ---- (a2) write in a separate latch block. ------------------------------------------------

    /// <summary>Same shape as (a), but the write sits in its own latch block reached only from
    /// <c>loop.body</c> — proves the region includes the WHOLE loop body, not just the use's own
    /// block. Also killed by M1, and — per the implementer's contract — NOT killed by M2 (region
    /// without the back-edge body) nor by the pre-existing
    /// <c>IRVerifierHandBuiltIRTests.DestinationWrittenLaterInALoopBody_ThatReReachesTheUse_Fails</c>,
    /// because <c>loop.latch</c> is reached via the FORWARD walk from the use's own block, which a
    /// region missing only the "back-edge body" (the use's own block after the use) still
    /// includes.</summary>
    [Test]
    public void A2_PreheaderDef_OneUseInBody_WriteInASeparateLatch_Fires()
    {
        var f = NewFixture();
        var head = f.Function.CreateBlock("loop.head");
        var body = f.Function.CreateBlock("loop.body");
        var latch = f.Function.CreateBlock("loop.latch");
        var exit = f.Function.CreateBlock("loop.exit");
        var v = new IRBinaryOp("t0", BinaryOpKind.Add, f.P, f.Q, IntType);
        f.Entry.Instructions.Add(v);
        f.Entry.Instructions.Add(new IRBranch(head));
        head.Instructions.Add(new IRConditionalBranch(f.C, body, exit));
        body.Instructions.Add(Use(v));
        body.Instructions.Add(new IRBranch(latch));
        latch.Instructions.Add(Write(f.Q));
        latch.Instructions.Add(new IRBranch(head));
        exit.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectFires: true, expectVariable: "q", expectWriterBlock: "loop.latch", expectUseBlock: "loop.body");
    }

    // ---- (a3) the use sits in the loop HEADER (a hoisted loop bound). -------------------------

    /// <summary>The use is a compare in <c>loop.head</c> (a hoisted loop bound: <c>i &lt;= t0</c>),
    /// the write is in <c>loop.body</c> — proves the region reaches a write reachable from the
    /// use's own block via the back edge, not merely "the use's block itself".</summary>
    [Test]
    public void A3_PreheaderDef_OneUseInLoopHeaderCompare_WriteInBody_Fires()
    {
        var f = NewFixture();
        var head = f.Function.CreateBlock("loop.head");
        var body = f.Function.CreateBlock("loop.body");
        var exit = f.Function.CreateBlock("loop.exit");
        var v = new IRBinaryOp("t0", BinaryOpKind.Add, f.P, f.Q, IntType);
        var i = new IRVariable("i", IntType);
        f.Function.LocalVariables.Add(i);
        f.Entry.Instructions.Add(v);
        f.Entry.Instructions.Add(new IRBranch(head));
        var cmp = new IRCompare("t1", CompareKind.Le, i, v, BoolType);
        head.Instructions.Add(cmp);
        head.Instructions.Add(new IRConditionalBranch(cmp, body, exit));
        body.Instructions.Add(Write(f.P));
        body.Instructions.Add(new IRBranch(head));
        exit.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectFires: true, expectVariable: "p", expectWriterBlock: "loop.body", expectUseBlock: "loop.head");
    }

    // ---- (a4) the write is a CALL over a bare (undeclared) field K. ---------------------------

    /// <summary>An undeclared name read bare (<c>K</c>: not in <c>LocalVariables</c>, so
    /// <c>OptimizationPass.IsCallVisible</c> falls to its "undeclared" default of call-visible) —
    /// a class field read with no receiver. The write is a bare call (<c>Inc()</c>) AFTER the use,
    /// hitting through the call-visible arm rather than a direct name match.</summary>
    [Test]
    public void A4_PreheaderDefOverBareFieldK_OneUseInLoop_CallAfterUse_Fires()
    {
        var f = NewFixture();
        var k = new IRVariable("K", IntType);
        var head = f.Function.CreateBlock("loop.head");
        var body = f.Function.CreateBlock("loop.body");
        var exit = f.Function.CreateBlock("loop.exit");
        var v = new IRBinaryOp("t0", BinaryOpKind.Add, k, f.Q, IntType);
        f.Entry.Instructions.Add(v);
        f.Entry.Instructions.Add(new IRBranch(head));
        head.Instructions.Add(new IRConditionalBranch(f.C, body, exit));
        body.Instructions.Add(Use(v));
        body.Instructions.Add(new IRCall("", "Inc", IntType));
        body.Instructions.Add(new IRBranch(head));
        exit.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectFires: true, expectVariable: "K", expectWriterBlock: "loop.body", expectUseBlock: "loop.body");
    }

    // ---- (a4g) same over a non-Const MODULE GLOBAL: not replicable, so outside Guard(v). -------

    /// <summary>Same shape as (a4), but over a module global <c>g</c> instead of a bare field.
    /// <c>g.IsGlobal &amp;&amp; !g.IsConst</c> makes it call-visible too — but
    /// <see cref="IRReplicability.IsReplicable"/> excludes a non-Const global outright (ADR-0005
    /// D2), so it never enters <c>Guard(v)</c> in the first place: QUIET regardless of the dynamic
    /// count. Distinguishes "outside Guard" from "inside Guard but not dynamically shared".</summary>
    [Test]
    public void A4g_PreheaderDefOverModuleGlobal_NonReplicable_OutsideGuard_Quiet()
    {
        var f = NewFixture();
        var g = new IRVariable("g", IntType) { IsGlobal = true };
        var head = f.Function.CreateBlock("loop.head");
        var body = f.Function.CreateBlock("loop.body");
        var exit = f.Function.CreateBlock("loop.exit");
        var v = new IRBinaryOp("t0", BinaryOpKind.Add, g, f.Q, IntType);
        f.Entry.Instructions.Add(v);
        f.Entry.Instructions.Add(new IRBranch(head));
        head.Instructions.Add(new IRConditionalBranch(f.C, body, exit));
        body.Instructions.Add(Use(v));
        body.Instructions.Add(new IRCall("", "Inc", IntType));
        body.Instructions.Add(new IRBranch(head));
        exit.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectFires: false);
    }

    // ---- (a10) a NAMED value: already checked before D2 (static rule) -- must fire before AND after.

    /// <summary>The ONE shape in this contract that FIRES both before and after D2: <c>a</c> has a
    /// named destination, so the STATIC rule alone (<c>destination != null &amp;&amp;
    /// useSites.Count &gt;= 1</c>, ADR-0005 D2) already made it shared — D2's dynamic count is not
    /// what catches this one. Proves D2 did not change behaviour for a value the static rule
    /// already covered, and — since the use is ALSO dynamically repeated here — that
    /// <see cref="InvariantViolation.UseRepeats"/> still reports true on a statically-shared value
    /// when its use happens to repeat too (the two rules are not mutually exclusive).</summary>
    [Test]
    public void A10_NamedValue_OneUseInLoop_WritePAfterUse_FiresBeforeAndAfterD2()
    {
        var f = NewFixture();
        var a = new IRVariable("a", IntType);
        f.Function.LocalVariables.Add(a);
        var head = f.Function.CreateBlock("loop.head");
        var body = f.Function.CreateBlock("loop.body");
        var exit = f.Function.CreateBlock("loop.exit");
        var v = new IRBinaryOp("a", BinaryOpKind.Add, f.P, f.Q, IntType) { NamedAfterVariable = true };
        f.Entry.Instructions.Add(v);
        f.Entry.Instructions.Add(new IRBranch(head));
        head.Instructions.Add(new IRConditionalBranch(f.C, body, exit));
        body.Instructions.Add(Use(v));
        body.Instructions.Add(Write(f.P));
        body.Instructions.Add(new IRBranch(head));
        exit.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectFires: true, expectVariable: "p", expectWriterBlock: "loop.body", expectUseBlock: "loop.body");
    }

    // ---- (c3) L1 made anonymous: def in loop BODY, use later in the SAME body, write between. --

    /// <summary>
    /// ⭐ THE M3a/M3b MUTANT KILL. <c>t0</c> is defined in <c>loop.head</c>; its one use is later
    /// in <c>loop.body</c>, with a write of <c>p</c> BETWEEN them (textually) — the correct L1-like
    /// shape, made anonymous. The loop CONTAINS the definition (every cycle back to the use passes
    /// through the definition's own block, <c>loop.head</c>), so the use is NOT dynamically
    /// repeated and the anonymous single-use exemption applies: QUIET. A mutant that treats a use
    /// in ANY loop as repeated regardless of whether the loop contains the definition (M3b), or
    /// that drops the dynamic gate altogether so every anonymous use is checked (M3a), turns this
    /// FIRES.
    /// </summary>
    [Test]
    public void C3_DefInLoopHeader_WriteThenUseInBody_AnonOneUseExemption_Quiet()
    {
        var f = NewFixture();
        var head = f.Function.CreateBlock("loop.head");
        var body = f.Function.CreateBlock("loop.body");
        var exit = f.Function.CreateBlock("loop.exit");
        var v = new IRBinaryOp("t0", BinaryOpKind.Add, f.P, f.Q, IntType);
        f.Entry.Instructions.Add(new IRBranch(head));
        head.Instructions.Add(v);
        head.Instructions.Add(new IRConditionalBranch(f.C, body, exit));
        body.Instructions.Add(Write(f.P));
        body.Instructions.Add(Use(v));
        body.Instructions.Add(new IRBranch(head));
        exit.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectFires: false);
    }

    // ---- (a5) nested: def in OUTER body, use in INNER loop, write after the use in the inner loop.

    /// <summary>Nested loops: the definition sits in the outer body (itself inside the outer
    /// loop), the one use is inside the inner loop, and the write follows the use inside the
    /// INNER loop's own body. The inner loop does not contain the definition (the definition is
    /// only reached once per outer iteration, in <c>outer.body</c>, before <c>inner.head</c>), so
    /// the inner-loop use is dynamically repeated.</summary>
    [Test]
    public void A5_DefInOuterBody_UseInInnerLoop_WriteAfterUseInInner_Fires()
    {
        var f = NewFixture();
        var oh = f.Function.CreateBlock("outer.head");
        var ob = f.Function.CreateBlock("outer.body");
        var ih = f.Function.CreateBlock("inner.head");
        var ib = f.Function.CreateBlock("inner.body");
        var ie = f.Function.CreateBlock("inner.end");
        var exit = f.Function.CreateBlock("exit");
        var v = new IRBinaryOp("t0", BinaryOpKind.Add, f.P, f.Q, IntType);
        f.Entry.Instructions.Add(new IRBranch(oh));
        oh.Instructions.Add(new IRConditionalBranch(f.C, ob, exit));
        ob.Instructions.Add(v);
        ob.Instructions.Add(new IRBranch(ih));
        ih.Instructions.Add(new IRConditionalBranch(f.C, ib, ie));
        ib.Instructions.Add(Use(v));
        ib.Instructions.Add(Write(f.P));
        ib.Instructions.Add(new IRBranch(ih));
        ie.Instructions.Add(new IRBranch(oh));
        exit.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectFires: true, expectVariable: "p", expectWriterBlock: "inner.body", expectUseBlock: "inner.body");
    }

    // ---- (a6) nested, write in the OUTER latch (past the inner loop): QUIET. -------------------

    /// <summary>Same nesting as (a5), but the write moves to <c>inner.end</c> (the outer latch,
    /// reached only AFTER the inner loop exits) — a path back to the use re-runs the definition
    /// (through <c>outer.head</c> -&gt; <c>outer.body</c>) before it can reach the write again, so
    /// the write is not dynamically before the NEXT execution of the use in the way D2's region
    /// captures. Also the mutant M3b discriminator: M3b's "any loop use repeated" over-widens the
    /// region to include the WHOLE outer loop and would catch this write too.</summary>
    [Test]
    public void A6_DefInOuterBody_UseInInnerLoop_WriteInOuterLatch_Quiet()
    {
        var f = NewFixture();
        var oh = f.Function.CreateBlock("outer.head");
        var ob = f.Function.CreateBlock("outer.body");
        var ih = f.Function.CreateBlock("inner.head");
        var ib = f.Function.CreateBlock("inner.body");
        var ie = f.Function.CreateBlock("inner.end");
        var exit = f.Function.CreateBlock("exit");
        var v = new IRBinaryOp("t0", BinaryOpKind.Add, f.P, f.Q, IntType);
        f.Entry.Instructions.Add(new IRBranch(oh));
        oh.Instructions.Add(new IRConditionalBranch(f.C, ob, exit));
        ob.Instructions.Add(v);
        ob.Instructions.Add(new IRBranch(ih));
        ih.Instructions.Add(new IRConditionalBranch(f.C, ib, ie));
        ib.Instructions.Add(Use(v));
        ib.Instructions.Add(new IRBranch(ih));
        ie.Instructions.Add(Write(f.P));
        ie.Instructions.Add(new IRBranch(oh));
        exit.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectFires: false);
    }

    // ---- (a7) def BEFORE both loops, use in inner loop, write in outer latch. ------------------

    /// <summary>The definition is in the preheader before BOTH loops (never re-executed at all,
    /// since it is outside the outer loop entirely); the use is in the inner loop; the write is in
    /// the outer latch <c>inner.end</c>. Unlike (a6), the definition being outside the outer loop
    /// means no path back to the use re-runs it — the write in the outer latch genuinely precedes
    /// the use's next execution.</summary>
    [Test]
    public void A7_DefBeforeBothLoops_UseInInner_WriteInOuterLatch_Fires()
    {
        var f = NewFixture();
        var oh = f.Function.CreateBlock("outer.head");
        var ob = f.Function.CreateBlock("outer.body");
        var ih = f.Function.CreateBlock("inner.head");
        var ib = f.Function.CreateBlock("inner.body");
        var ie = f.Function.CreateBlock("inner.end");
        var exit = f.Function.CreateBlock("exit");
        var v = new IRBinaryOp("t0", BinaryOpKind.Add, f.P, f.Q, IntType);
        f.Entry.Instructions.Add(v);
        f.Entry.Instructions.Add(new IRBranch(oh));
        oh.Instructions.Add(new IRConditionalBranch(f.C, ob, exit));
        ob.Instructions.Add(new IRBranch(ih));
        ih.Instructions.Add(new IRConditionalBranch(f.C, ib, ie));
        ib.Instructions.Add(Use(v));
        ib.Instructions.Add(new IRBranch(ih));
        ie.Instructions.Add(Write(f.P));
        ie.Instructions.Add(new IRBranch(oh));
        exit.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectFires: true, expectVariable: "p", expectWriterBlock: "inner.end", expectUseBlock: "inner.body");
    }

    // ---- (a8) exit-path write: QUIET — the region never includes a write past the loop exit. ---

    /// <summary>The write sits only on <c>loop.exit</c>, reached after the loop is done — never on
    /// any path that reaches the use again. A region that (wrongly) included exit-path writes
    /// would be too WIDE, per the implementer brief's "a fire on a correct program means the
    /// region wrongly includes exit-path writes" note; this shape is the direct check that it does
    /// not.</summary>
    [Test]
    public void A8_PreheaderDef_OneUseInLoop_WriteOnlyOnExitPath_Quiet()
    {
        var f = NewFixture();
        var head = f.Function.CreateBlock("loop.head");
        var body = f.Function.CreateBlock("loop.body");
        var exit = f.Function.CreateBlock("loop.exit");
        var v = new IRBinaryOp("t0", BinaryOpKind.Add, f.P, f.Q, IntType);
        f.Entry.Instructions.Add(v);
        f.Entry.Instructions.Add(new IRBranch(head));
        head.Instructions.Add(new IRConditionalBranch(f.C, body, exit));
        body.Instructions.Add(Use(v));
        body.Instructions.Add(new IRBranch(head));
        exit.Instructions.Add(Write(f.P));
        exit.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectFires: false);
    }

    // ---- (a9) a break path: writes THEN exits, never rejoins the use. QUIET. -------------------

    /// <summary>A conditional break out of the loop body: one arm writes <c>p</c> and exits
    /// (<c>loop.break</c> -&gt; <c>loop.exit</c>, never rejoining the loop), the other arm is the
    /// ordinary latch. The break-arm write is not on any path back to the use, so it must not be
    /// in the region — same "no exit-path write" property as (a8), on a path that leaves the loop
    /// from the middle rather than the header.</summary>
    [Test]
    public void A9_PreheaderDef_OneUseInLoop_WriteOnABreakPathThatExits_Quiet()
    {
        var f = NewFixture();
        var head = f.Function.CreateBlock("loop.head");
        var body = f.Function.CreateBlock("loop.body");
        var brk = f.Function.CreateBlock("loop.break");
        var latch = f.Function.CreateBlock("loop.latch");
        var exit = f.Function.CreateBlock("loop.exit");
        var v = new IRBinaryOp("t0", BinaryOpKind.Add, f.P, f.Q, IntType);
        f.Entry.Instructions.Add(v);
        f.Entry.Instructions.Add(new IRBranch(head));
        head.Instructions.Add(new IRConditionalBranch(f.C, body, exit));
        body.Instructions.Add(Use(v));
        body.Instructions.Add(new IRConditionalBranch(f.C, brk, latch));
        brk.Instructions.Add(Write(f.P));
        brk.Instructions.Add(new IRBranch(exit));
        latch.Instructions.Add(new IRBranch(head));
        exit.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectFires: false);
    }

    // ---- (b) = (a) without the write: QUIET (no candidate write at all). -----------------------

    [Test]
    public void B_PreheaderDef_OneUseInLoop_NoWrite_Quiet()
    {
        var f = NewFixture();
        var head = f.Function.CreateBlock("loop.head");
        var body = f.Function.CreateBlock("loop.body");
        var exit = f.Function.CreateBlock("loop.exit");
        var v = new IRBinaryOp("t0", BinaryOpKind.Add, f.P, f.Q, IntType);
        f.Entry.Instructions.Add(v);
        f.Entry.Instructions.Add(new IRBranch(head));
        head.Instructions.Add(new IRConditionalBranch(f.C, body, exit));
        body.Instructions.Add(Use(v));
        body.Instructions.Add(new IRBranch(head));
        exit.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectFires: false);
    }

    // ---- (b2) write of a NON-guard name in the loop, plus a call: QUIET (nothing GUARDED is hit).

    /// <summary>The loop writes <c>s</c> (never in <c>Guard(v)</c> — not an operand of <c>t0</c>,
    /// not its destination) and makes a bare call — the call-visible arm can only hit a name
    /// already IN the guard, and neither <c>p</c> nor <c>q</c> is call-visible here (both are
    /// plain declared locals, no lambda in the function), so the call hits nothing either.</summary>
    [Test]
    public void B2_PreheaderDefOverLocals_WriteOfNonGuardNamePlusACall_Quiet()
    {
        var f = NewFixture();
        var s = new IRVariable("s", IntType);
        f.Function.LocalVariables.Add(s);
        var head = f.Function.CreateBlock("loop.head");
        var body = f.Function.CreateBlock("loop.body");
        var exit = f.Function.CreateBlock("loop.exit");
        var v = new IRBinaryOp("t0", BinaryOpKind.Add, f.P, f.Q, IntType);
        f.Entry.Instructions.Add(v);
        f.Entry.Instructions.Add(new IRBranch(head));
        head.Instructions.Add(new IRConditionalBranch(f.C, body, exit));
        body.Instructions.Add(Use(v));
        body.Instructions.Add(Write(s));
        body.Instructions.Add(new IRCall("", "Inc", IntType));
        body.Instructions.Add(new IRBranch(head));
        exit.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectFires: false);
    }

    // ---- (c) def AND use in the SAME block, in the loop, write after use: the L1 shape. --------

    /// <summary>The correct L1-like shape in miniature: <c>t0</c> is defined and used in the same
    /// iteration (<c>loop.body</c>), the write of <c>p</c> follows both — every execution of the
    /// use sees the value from the SAME iteration's definition, so it is not dynamically
    /// repeated. The straight-line def-then-use-then-write case, just inside a loop.</summary>
    [Test]
    public void C_DefAndUseSameBlockInLoop_WriteAfterUse_Quiet()
    {
        var f = NewFixture();
        var head = f.Function.CreateBlock("loop.head");
        var body = f.Function.CreateBlock("loop.body");
        var exit = f.Function.CreateBlock("loop.exit");
        var v = new IRBinaryOp("t0", BinaryOpKind.Add, f.P, f.Q, IntType);
        f.Entry.Instructions.Add(new IRBranch(head));
        head.Instructions.Add(new IRConditionalBranch(f.C, body, exit));
        body.Instructions.Add(v);
        body.Instructions.Add(Use(v));
        body.Instructions.Add(Write(f.P));
        body.Instructions.Add(new IRBranch(head));
        exit.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectFires: false);
    }

    // ---- (c2) def in the loop HEADER, use in the body, write after the use: QUIET. -------------

    /// <summary>Def in <c>loop.head</c>, use in <c>loop.body</c>, write after the use in the same
    /// body — the loop contains the definition (every cycle back to the use passes through
    /// <c>loop.head</c>), so not dynamically repeated. Distinguishes from (a3), where the roles of
    /// def/use are swapped (def in the preheader, use in the header) and the loop does NOT contain
    /// the definition.</summary>
    [Test]
    public void C2_DefInLoopHeader_UseInBody_WriteAfterUse_Quiet()
    {
        var f = NewFixture();
        var head = f.Function.CreateBlock("loop.head");
        var body = f.Function.CreateBlock("loop.body");
        var exit = f.Function.CreateBlock("loop.exit");
        var v = new IRBinaryOp("t0", BinaryOpKind.Add, f.P, f.Q, IntType);
        f.Entry.Instructions.Add(new IRBranch(head));
        head.Instructions.Add(v);
        head.Instructions.Add(new IRConditionalBranch(f.C, body, exit));
        body.Instructions.Add(Use(v));
        body.Instructions.Add(Write(f.P));
        body.Instructions.Add(new IRBranch(head));
        exit.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectFires: false);
    }

    // ---- (d)/(d2)/(d3): def INSIDE the loop, on a branch the cycle CAN AVOID. -------------------
    //
    // ⚠⚠ THE IMPLEMENTED READING, NOT YET ARCHITECT-CONFIRMED (task #157). D2's rule reads
    // "repeated" off a CYCLE, not a natural loop: `head -> arm -> merge -> head` (skipping the
    // definition on `arm`) is a cycle that avoids the definition's block, so `t0`'s one use at
    // `merge` counts as repeated even though the definition IS lexically inside the same natural
    // loop. A natural-loop reading ("the loop contains the definition") would exempt it instead.
    // MEASURED (S/adr6-d2/hand-final-full.txt): both (d) and (d2) FIRE under the landed code. The ADR
    // doc's "## Implementation note (D2)" records this as the implemented interpretation pending
    // the architect's confirmation, per the implementer brief.

    /// <summary>(d): the write is AFTER the use, at the merge point. FIRES under the CYCLE
    /// reading — see the section note above and task #157.</summary>
    [Test]
    public void D_DefInLoopOnAnAvoidableBranch_UseAtMerge_WriteAfterUse_Fires_PendingTask157()
    {
        var f = NewFixture();
        var head = f.Function.CreateBlock("loop.head");
        var arm = f.Function.CreateBlock("loop.arm");
        var skip = f.Function.CreateBlock("loop.skip");
        var merge = f.Function.CreateBlock("loop.merge");
        var exit = f.Function.CreateBlock("loop.exit");
        var v = new IRBinaryOp("t0", BinaryOpKind.Add, f.P, f.Q, IntType);
        f.Entry.Instructions.Add(new IRBranch(head));
        head.Instructions.Add(new IRConditionalBranch(f.C, arm, skip));
        arm.Instructions.Add(v);
        arm.Instructions.Add(new IRBranch(merge));
        skip.Instructions.Add(new IRBranch(merge));
        merge.Instructions.Add(Use(v));
        merge.Instructions.Add(Write(f.P));
        merge.Instructions.Add(new IRConditionalBranch(f.C, head, exit));
        exit.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectFires: true, expectVariable: "p", expectWriterBlock: "loop.merge", expectUseBlock: "loop.merge");
    }

    /// <summary>(d2): same as (d), but the write is on the SKIP arm instead of after the use.
    /// FIRES under the CYCLE reading — same task #157 note as (d): the skip arm is on a path from
    /// the use's block back to itself that never re-executes the definition (which lives on the
    /// OTHER arm), so the write precedes the use's next execution on that path.</summary>
    [Test]
    public void D2_DefInLoopOnAnAvoidableBranch_WriteOnTheOtherArm_Fires_PendingTask157()
    {
        var f = NewFixture();
        var head = f.Function.CreateBlock("loop.head");
        var arm = f.Function.CreateBlock("loop.arm");
        var skip = f.Function.CreateBlock("loop.skip");
        var merge = f.Function.CreateBlock("loop.merge");
        var exit = f.Function.CreateBlock("loop.exit");
        var v = new IRBinaryOp("t0", BinaryOpKind.Add, f.P, f.Q, IntType);
        f.Entry.Instructions.Add(new IRBranch(head));
        head.Instructions.Add(new IRConditionalBranch(f.C, arm, skip));
        arm.Instructions.Add(v);
        arm.Instructions.Add(new IRBranch(merge));
        skip.Instructions.Add(Write(f.P));
        skip.Instructions.Add(new IRBranch(merge));
        merge.Instructions.Add(Use(v));
        merge.Instructions.Add(new IRConditionalBranch(f.C, head, exit));
        exit.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectFires: true, expectVariable: "p", expectWriterBlock: "loop.skip", expectUseBlock: "loop.merge");
    }

    /// <summary>(d3): same shape as (d)/(d2), no write anywhere — the negative control that proves
    /// (d)/(d2) fire on the WRITE, not merely on being on a cycle at all.</summary>
    [Test]
    public void D3_DefInLoopOnAnAvoidableBranch_NoWrite_Quiet()
    {
        var f = NewFixture();
        var head = f.Function.CreateBlock("loop.head");
        var arm = f.Function.CreateBlock("loop.arm");
        var skip = f.Function.CreateBlock("loop.skip");
        var merge = f.Function.CreateBlock("loop.merge");
        var exit = f.Function.CreateBlock("loop.exit");
        var v = new IRBinaryOp("t0", BinaryOpKind.Add, f.P, f.Q, IntType);
        f.Entry.Instructions.Add(new IRBranch(head));
        head.Instructions.Add(new IRConditionalBranch(f.C, arm, skip));
        arm.Instructions.Add(v);
        arm.Instructions.Add(new IRBranch(merge));
        skip.Instructions.Add(new IRBranch(merge));
        merge.Instructions.Add(Use(v));
        merge.Instructions.Add(new IRConditionalBranch(f.C, head, exit));
        exit.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectFires: false);
    }

    // ---- (e) straight line, one use, write between: still exempt (ADR-0005's literal "> 1"). ---

    /// <summary>No loop at all. Mirrors the pre-existing
    /// <see cref="IRVerifierHandBuiltIRTests.AnonymousTemp_OneUse_OperandWrittenInBetween_IsExempt"/>
    /// (which stays green, unmodified, under D2 — this is the same shape, kept here too so the
    /// M3a mutant discriminator list for THIS file is self-contained). Not on any cycle, so
    /// <c>UseRepeats</c> is trivially false and the anonymous single-use exemption applies.</summary>
    [Test]
    public void E_StraightLine_OneUse_WriteBetween_AnonymousExemption_Quiet()
    {
        var f = NewFixture();
        var v = new IRBinaryOp("t0", BinaryOpKind.Add, f.P, f.Q, IntType);
        f.Entry.Instructions.Add(v);
        f.Entry.Instructions.Add(Write(f.P));
        f.Entry.Instructions.Add(Use(v));
        f.Entry.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectFires: false);
    }

    // ---- (f) cross-block but ACYCLIC single use, write between: still exempt. ------------------

    [Test]
    public void F_CrossBlockAcyclic_OneUse_WriteBetween_AnonymousExemption_Quiet()
    {
        var f = NewFixture();
        var next = f.Function.CreateBlock("next");
        var v = new IRBinaryOp("t0", BinaryOpKind.Add, f.P, f.Q, IntType);
        f.Entry.Instructions.Add(v);
        f.Entry.Instructions.Add(Write(f.P));
        f.Entry.Instructions.Add(new IRBranch(next));
        next.Instructions.Add(Use(v));
        next.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectFires: false);
    }

    // ---- (g) use in a SELF-LOOP block that does not hold the def, write after the use. ---------

    /// <summary>The simplest possible repeated-use cycle: a single self-looping block
    /// (<c>loop -&gt; loop</c>) that holds the use but not the definition (which is in the
    /// preheader). Proves D2's region walk handles the degenerate one-block loop, not only
    /// multi-block head/body/latch shapes.</summary>
    [Test]
    public void G_PreheaderDef_UseInASelfLoopBlock_WriteAfterUse_Fires()
    {
        var f = NewFixture();
        var loop = f.Function.CreateBlock("loop");
        var exit = f.Function.CreateBlock("exit");
        var v = new IRBinaryOp("t0", BinaryOpKind.Add, f.P, f.Q, IntType);
        f.Entry.Instructions.Add(v);
        f.Entry.Instructions.Add(new IRBranch(loop));
        loop.Instructions.Add(Use(v));
        loop.Instructions.Add(Write(f.Q));
        loop.Instructions.Add(new IRConditionalBranch(f.C, loop, exit));
        exit.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectFires: true, expectVariable: "q", expectWriterBlock: "loop", expectUseBlock: "loop");
    }

    // ---- (h) an anonymous NON-PURE value (a call): never has a guard at all. QUIET. -------------

    /// <summary>A <c>Seed(p)</c> call, evaluated once, with one use in a loop that repeats and an
    /// operand (<c>p</c>) written after the use. <see cref="IsPureOperator"/>-gated: only
    /// <c>IRBinaryOp</c>/<c>IRUnaryOp</c>/<c>IRCompare</c>/<c>IRCast</c> can ever have a guard
    /// (only they are ever RE-EMITTED; a call is evaluated once and its arguments are never
    /// re-read), so the dynamic-count check does not even walk this value's operands — proves the
    /// `IsPureOperator` gate in <c>CheckFunction</c> applies to the anonymous dynamic path too, not
    /// only the static one.</summary>
    /// <remarks>Named <c>IsPureOperator</c> in the doc comment above for traceability to
    /// <c>BasicLang/IRVerifier.cs</c>'s private predicate of the same name; not referenced via
    /// <c>&lt;see cref&gt;</c> since it is private.</remarks>
    [Test]
    public void H_PreheaderCallValue_OneUseInLoop_ArgWritten_Quiet()
    {
        var f = NewFixture();
        var head = f.Function.CreateBlock("loop.head");
        var body = f.Function.CreateBlock("loop.body");
        var exit = f.Function.CreateBlock("loop.exit");
        var v = new IRCall("t0", "Seed", IntType);
        v.Arguments.Add(f.P);
        f.Entry.Instructions.Add(v);
        f.Entry.Instructions.Add(new IRBranch(head));
        head.Instructions.Add(new IRConditionalBranch(f.C, body, exit));
        body.Instructions.Add(Use(v));
        body.Instructions.Add(Write(f.P));
        body.Instructions.Add(new IRBranch(head));
        exit.Instructions.Add(new IRReturn());

        AssertVerdict(f.Module, expectFires: false);
    }
}

/// <summary>
/// L1-L7, compiled through the AGGRESSIVE pipeline (never CompileOptimized/CompileToCppOptimized,
/// which are STANDARD-only, and never RunJs/JsTestSupport.Compile, which run no optimizer at all —
/// see CLAUDE.md and docs/HANDOFF.md), asserting <c>IRVerifier.CheckInvariantSPrime</c> returns
/// ZERO violations directly. No process is spawned — fast subset. This is the direct proof behind
/// the ADR-0006 D2 implementation note's "L1-L7 identical before and after, no fires" measurement
/// (S/adr6-d2/matrix-before.txt vs S/adr6-d2/matrix-after.txt, both zero <c>[VERIFY]</c> markers) — a false
/// negative in that comparison (a fire suppressed by catching the wrong exception, say) cannot
/// hide here, because this asserts on the violation LIST, not on whether a compile threw.
/// </summary>
[TestFixture]
public class DynamicUseSPrimeAggressivePipelineStructuralTests
{
    [Test]
    public void L1ThroughL7_AggressivePipeline_VerifierIsQuiet()
    {
        Assert.Multiple(() =>
        {
            foreach (var (name, source) in new (string, string)[]
                     {
                         ("L1", LicmKillVocabularyShapes.L1),
                         ("L2", LicmKillVocabularyShapes.L2),
                         ("L3", LicmKillVocabularyShapes.L3),
                         ("L4", LicmKillVocabularyShapes.L4),
                         ("L5", LicmKillVocabularyShapes.L5),
                         ("L6", LicmKillVocabularyShapes.L6),
                         ("L7", DynamicUseSPrimeProbes.L7),
                     })
            {
                var module = JsTestSupport.BuildModule(source, sourceFilePath: "prog.bas");
                AggressivePipeline.Apply(module);
                var violations = IRVerifier.CheckInvariantSPrime(module);
                Assert.That(violations, Is.Empty,
                    $"{name}: {string.Join(" | ", violations.Select(v => v.ToString()))}");
            }
        });
    }
}

/// <summary>
/// L1-L7 run for real, through the shared AGGRESSIVE helpers (<see cref="FourBackends"/> and its
/// per-backend legs — <see cref="BclE2E.CompileToCppAggressive"/>,
/// <see cref="MsilHarness.RunAggressiveExpectingSuccess"/>,
/// <see cref="ReturnCoercionTests.EmitCSharpAggressiveForTest"/>,
/// <see cref="JsTestSupport.CompileAggressive"/> via <see cref="FourBackends.RunAggressiveJs"/>).
/// L1-L6 reuse <see cref="LicmKillVocabularyShapes"/> verbatim (the same probes
/// <c>LicmKillVocabularyTests.cs</c> already runs for a DIFFERENT purpose — proving LICM's kill
/// vocabulary is complete — this fixture's purpose is proving D2 does not introduce a false
/// positive over them); L7 is <see cref="DynamicUseSPrimeProbes.L7"/>.
///
/// <para>Every cell here is pinned exactly as the implementer measured it
/// (S/adr6-d2/probes/matrix-after.txt): L1/L2 are BL7002 on JavaScript (a ByRef parameter — JavaScript
/// has no reference parameters, unrelated to D2 either way); L5 is correct on JavaScript only
/// (ADR-0006 D1's interim closure rule), with C++ known-wrong for task #140 (backend lambda
/// capture-by-copy, present even with no optimizer running) and MSIL known-not-to-build for task
/// #155 (no lowering for the delegate type a <c>Sub()</c> lambda gets typed as) — both pre-existing
/// gaps this task neither caused nor closes.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class DynamicUseSPrimeExecutionTests
{
    // ---- L1 -------------------------------------------------------------------------------

    [Test]
    public void L1_AggressivePipeline_CSharpCppMsilAgree()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(LicmKillVocabularyShapes.L1))),
                Is.EqualTo(LicmKillVocabularyShapes.Expected12), "C++");
            Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(LicmKillVocabularyShapes.L1)),
                Is.EqualTo(LicmKillVocabularyShapes.Expected12), "MSIL");
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(LicmKillVocabularyShapes.L1)),
                Is.EqualTo(LicmKillVocabularyShapes.Expected12), "C#");
        });

    [Test]
    public void L1_JavaScript_RefusesByRef_BL7002()
    {
        var module = JsTestSupport.BuildModule(LicmKillVocabularyShapes.L1);
        var ex = Assert.Throws<ForeignFeatureException>(() => new JavaScriptCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("BL7002"));
    }

    // ---- L2 -------------------------------------------------------------------------------

    [Test]
    public void L2_AggressivePipeline_CSharpCppMsilAgree()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(LicmKillVocabularyShapes.L2))),
                Is.EqualTo(LicmKillVocabularyShapes.Expected12), "C++");
            Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(LicmKillVocabularyShapes.L2)),
                Is.EqualTo(LicmKillVocabularyShapes.Expected12), "MSIL");
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(LicmKillVocabularyShapes.L2)),
                Is.EqualTo(LicmKillVocabularyShapes.Expected12), "C#");
        });

    [Test]
    public void L2_JavaScript_RefusesByRef_BL7002()
    {
        var module = JsTestSupport.BuildModule(LicmKillVocabularyShapes.L2);
        var ex = Assert.Throws<ForeignFeatureException>(() => new JavaScriptCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("BL7002"));
    }

    // ---- L3 / L4 / L6 / L7: all four backends agree, no ByRef restriction. ---------------------

    [Test]
    public void L3_AggressivePipeline_AllFourBackendsAgree()
        => FourBackends.RunsOnEveryBackendAggressive(LicmKillVocabularyShapes.L3, LicmKillVocabularyShapes.Expected12);

    [Test]
    public void L4_AggressivePipeline_AllFourBackendsAgree()
        => FourBackends.RunsOnEveryBackendAggressive(LicmKillVocabularyShapes.L4, LicmKillVocabularyShapes.Expected12);

    [Test]
    public void L6_AggressivePipeline_AllFourBackendsAgree()
        => FourBackends.RunsOnEveryBackendAggressive(LicmKillVocabularyShapes.L6, LicmKillVocabularyShapes.ExpectedL6);

    [Test]
    public void L7_AggressivePipeline_AllFourBackendsAgree()
        => FourBackends.RunsOnEveryBackendAggressive(DynamicUseSPrimeProbes.L7, DynamicUseSPrimeProbes.ExpectedL7);

    // ---- L5: JavaScript CORRECT (ADR-0006 D1); C++ #140 and MSIL #155 known-wrong/does-not-build.

    /// <summary>CORRECT under ADR-0006 D1's interim closure rule: <c>bump()</c> is call-visible
    /// over the captured local <c>x</c>, so LICM does not hoist <c>x * 2</c>. Matches
    /// <c>LicmKillVocabularyKnownGapsTask122Tests.L5_LambdaCapturedLocal_JavaScript_AggressivePipeline_CorrectAfterAdr6D1</c>'s
    /// pin, repeated here so this file's own probe-by-probe matrix is self-contained.</summary>
    [Test]
    public void L5_JavaScript_AggressivePipeline_Correct()
        => Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(LicmKillVocabularyShapes.L5)),
            Is.EqualTo("seed\n12"),
            "ADR-0006 D1's interim closure rule closes this for JavaScript under --optimize.");

    /// <summary>KNOWN-WRONG, task #140: the C++ backend's own lambda lowering captures BY COPY
    /// (<c>[=]</c>) where BasicLang means by reference — MEASURED present even with NO optimizer
    /// pass running, so unrelated to D2 or to LICM's kill vocabulary either way.</summary>
    [Test]
    public void L5_Cpp_AggressivePipeline_PinnedForTask140()
        => Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(LicmKillVocabularyShapes.L5))),
            Is.EqualTo("seed\n6"),
            "task #140 (C++ BACKEND capture-by-copy, not a kill-vocabulary, LICM or D2 defect).");

    /// <summary>KNOWN-NOT-TO-BUILD, task #155: MSIL has no lowering for the delegate type a
    /// <c>Sub()</c> lambda gets typed as (<c>ilasm</c>: "Reference to undefined class 'Action'"),
    /// a pre-existing gap unrelated to D2. The ADR-0006 D2 ruling itself cites #155 for this (the
    /// implementer brief's contract text), superseding the older #122 attribution
    /// <c>LicmKillVocabularyKnownGapsTask122Tests</c> carries for the same failure.</summary>
    [Test]
    public void L5_Msil_CannotBuild_PinnedForTask155()
    {
        var run = MsilHarness.Run(LicmKillVocabularyShapes.L5, aggressive: true);
        Assert.That(run.Outcome, Is.EqualTo(MsilHarness.MsilOutcome.AssembleFailed), run.Report);
        Assert.That(run.Detail, Does.Contain("Action"),
            "task #155 — MSIL has no lowering for the delegate type a Sub() lambda gets typed as.");
    }
}
