using System;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

// =====================================================================================
//  Task #161 — CopyPropagationPass.Mentions becomes the union of ADR-0008's shared operand walk
//  (OptimizationPass.CollectReads, which already finds a NAMED NESTED operand instruction — a
//  pure-operator value IRBuilder renamed after a variable, NamedAfterVariable) and the pass's own
//  pre-existing structural descent into call-shaped operands' arguments/object
//  (MentionsPastTheWalk, covering what CollectReads does not: it stops at a call-shaped node).
//  Before this task Mentions matched only a bare IRVariable, so a direct store to a renamed
//  operand instruction's OWN name left a stale copy fact standing — pinned as a known-wrong
//  regression in Adr0008GuardTests.cs's Adr0008D2Pins until this task; see
//  Adr0008D2Pins.DirectStoreToANamedNestedOperandsOwnName_KillsTheFact (this fixture's S1,
//  restated with the ADR's own fixture and naming) and IROptimizer.cs's own remarks on
//  CopyPropagationPass.Mentions for the two-halves superset proof.
//
//  Three fixtures:
//    CopyPropagationMentionsTests            — hand-built IR, fast tier (no front end, no
//                                               backend, no process). Every shape ported one-for-
//                                               one from the implementer's scratch harness
//                                               (S/t161/harness/Program.cs, mode `cp`), each cross-
//                                               checked against S/t161/cp-shapes-matrix.txt cell by
//                                               cell before being pinned here.
//    CopyPropagationMentionsStructuralTests  — E3 (the source-level probe that reaches the
//                                               defect) through the AGGRESSIVE pipeline in
//                                               process, asserting IRVerifier.CheckInvariantSPrime
//                                               reports ZERO violations directly. This is the check
//                                               that FIRED at master (ir-E3-base.txt: one S′
//                                               violation) and is quiet after the fix
//                                               (ir-E3-fix.txt) — the discriminating structural
//                                               proof, independent of any backend's own blindness
//                                               to the hazard.
//    CopyPropagationMentionsExecutionTests   — E3 and E4 run for real, through the shared
//                                               AGGRESSIVE helpers (FourBackends), on every backend.
// =====================================================================================

/// <summary>
/// Hand-built IR, ported one-for-one from the implementer's scratch harness
/// (<c>S/t161/harness/Program.cs</c>, mode <c>cp</c>) — re-run live this session against the
/// current, fixed <c>BasicLang/IROptimizer.cs</c> (md5 <c>493c555cff540700a1749f4771e71306</c>),
/// 0 mismatches against <c>S/t161/cp-shapes-matrix.txt</c>'s <c>===== fix</c> block.
///
/// <para>Each shape shares the harness's own skeleton: one function with a declared local
/// <c>x</c>, the shape's own instructions, then the harness's use site — <c>t9 = x + 0</c> — and a
/// <c>Return</c>. <see cref="Kept"/> runs <see cref="CopyPropagationPass"/> once and reports
/// whether the recorded fact was correctly INVALIDATED (KEPT — <c>t9</c>'s left operand is still
/// the variable <c>x</c> itself) or SURVIVED and was substituted (SUBST — <c>t9</c>'s left operand
/// became the recorded value). Every shape below wants KEPT (a real kill task #161 adds or a kill
/// that already worked and must keep working) except <see cref="S1n_Control_StoreToAnUnrelatedName_KeepsTheFact"/>,
/// the anti-vacuity control, which wants SUBST.</para>
/// </summary>
[TestFixture]
public class CopyPropagationMentionsTests
{
    private static readonly TypeInfo I = new("Integer", TypeKind.Primitive);

    private static IRVariable Local(IRFunction f, string name)
    {
        var v = new IRVariable(name, I);
        f.LocalVariables.Add(v);
        return v;
    }

    /// <summary>Builds one function (<c>Main</c>) with a declared local <c>x</c>, runs
    /// <paramref name="body"/> to append the shape's own instructions, appends the harness's own
    /// use site (<c>t9 = x + 0</c>) and a <c>Return</c>, then runs <see cref="CopyPropagationPass"/>
    /// once. Returns <c>true</c> (KEPT) when <c>t9</c>'s left operand is still the SAME reference as
    /// the variable <c>x</c> — the fact was invalidated, or never recorded — or <c>false</c>
    /// (SUBST) when it was replaced by the recorded value.</summary>
    private static bool Kept(Action<IRFunction, BasicBlock, IRVariable> body)
    {
        var m = new IRModule("M");
        var f = new IRFunction("Main", I);
        m.Functions.Add(f);
        var x = new IRVariable("x", I);
        f.LocalVariables.Add(x);
        var e = f.CreateBlock("entry");
        body(f, e, x);
        var use = new IRBinaryOp("t9", BinaryOpKind.Add, x, new IRConstant(0, I), I);
        e.Instructions.Add(use);
        e.Instructions.Add(new IRReturn());

        new CopyPropagationPass().Run(m);
        return use.Left is IRVariable;
    }

    // ================================ S1 family: named nested operand ============================

    /// <summary>S1, the #161 pin itself (restated by shape letter; see
    /// <c>Adr0008D2Pins.DirectStoreToANamedNestedOperandsOwnName_KillsTheFact</c> for the same
    /// shape under the ADR's own naming). <c>u = a + 1</c> (an <c>IRBinaryOp</c> renamed <c>u</c>),
    /// <c>t0 = u * 2</c>, <c>x := t0</c>, then a direct store <c>u = 5</c>. <c>t0</c>'s named nested
    /// operand <c>u</c> is found by <c>OptimizationPass.CollectReads(t0)</c>'s own walk, so the
    /// union half of <c>Mentions</c> kills the fact without ever reaching
    /// <c>MentionsPastTheWalk</c>.</summary>
    [Test]
    public void S1_NamedNestedOperand_DirectStoreToItsName_KillsTheFact()
    {
        var kept = Kept((f, e, x) =>
        {
            var a = Local(f, "a"); var uVar = Local(f, "u");
            var u = new IRBinaryOp("u", BinaryOpKind.Add, a, new IRConstant(1, I), I) { NamedAfterVariable = true };
            var t0 = new IRBinaryOp("t0", BinaryOpKind.Mul, u, new IRConstant(2, I), I);
            e.Instructions.Add(u); e.Instructions.Add(t0);
            e.Instructions.Add(new IRAssignment(x, t0));
            e.Instructions.Add(new IRAssignment(uVar, new IRConstant(5, I)));
        });
        Assert.That(kept, Is.True, "t0 reads the named nested operand 'u' — a direct store to u " +
            "must kill x's fact (the #161 pin).");
    }

    /// <summary>S1r — the fact's recorded value IS the named instruction itself (no outer pure
    /// operator wrapping it): <c>x := u</c> directly, then <c>u = 5</c>. Distinguishes "Mentions
    /// sees a named ROOT value" from S1's "Mentions descends INTO a named nested operand".</summary>
    [Test]
    public void S1r_FactValueIsTheNamedInstructionItself_DirectStoreToItsName_KillsTheFact()
    {
        var kept = Kept((f, e, x) =>
        {
            var a = Local(f, "a"); var uVar = Local(f, "u");
            var u = new IRBinaryOp("u", BinaryOpKind.Add, a, new IRConstant(1, I), I) { NamedAfterVariable = true };
            e.Instructions.Add(u);
            e.Instructions.Add(new IRAssignment(x, u));
            e.Instructions.Add(new IRAssignment(uVar, new IRConstant(5, I)));
        });
        Assert.That(kept, Is.True, "x's fact IS the named instruction u — a direct store to u " +
            "must kill it.");
    }

    /// <summary>S1c — S1 with the store spelled <c>U</c> (uppercase): the case-insensitive compare
    /// inside <c>Mentions</c> itself (<c>string.Equals(read.Name, name, OrdinalIgnoreCase)</c>) must
    /// still match. The Me mutant (case-SENSITIVE compare) turns this SUBST.</summary>
    [Test]
    public void S1c_NamedNestedOperand_StoreSpelledUppercase_KillsTheFactCaseInsensitively()
    {
        var kept = Kept((f, e, x) =>
        {
            var a = Local(f, "a"); var uVar = Local(f, "U");
            var u = new IRBinaryOp("u", BinaryOpKind.Add, a, new IRConstant(1, I), I) { NamedAfterVariable = true };
            var t0 = new IRBinaryOp("t0", BinaryOpKind.Mul, u, new IRConstant(2, I), I);
            e.Instructions.Add(u); e.Instructions.Add(t0);
            e.Instructions.Add(new IRAssignment(x, t0));
            e.Instructions.Add(new IRAssignment(uVar, new IRConstant(5, I)));
        });
        Assert.That(kept, Is.True, "a store spelled 'U' must still kill a fact reading the named " +
            "nested operand 'u' — Mentions' own name compare is case-insensitive.");
    }

    /// <summary>S1n — the CONTROL: the same S1 shape, but the direct store is to an UNRELATED name
    /// (<c>z</c>, never read by <c>t0</c>). The fact must SURVIVE — proves the S1/S1r/S1c kills
    /// above are catching a real name match, not over-killing on every store regardless of what it
    /// writes.</summary>
    [Test]
    public void S1n_Control_StoreToAnUnrelatedName_KeepsTheFact()
    {
        var kept = Kept((f, e, x) =>
        {
            var a = Local(f, "a"); var zVar = Local(f, "z");
            var u = new IRBinaryOp("u", BinaryOpKind.Add, a, new IRConstant(1, I), I) { NamedAfterVariable = true };
            var t0 = new IRBinaryOp("t0", BinaryOpKind.Mul, u, new IRConstant(2, I), I);
            e.Instructions.Add(u); e.Instructions.Add(t0);
            e.Instructions.Add(new IRAssignment(x, t0));
            e.Instructions.Add(new IRAssignment(zVar, new IRConstant(5, I)));
        });
        Assert.That(kept, Is.False, "control: a store to an unrelated name z must NOT kill x's " +
            "fact — the widened Mentions must not over-kill.");
    }

    // ================================ S2 family: call-argument variable ==========================

    /// <summary>S2 — a plain VARIABLE passed as a call argument: <c>x := Foo(a)</c>, then
    /// <c>a = 5</c>. <c>CollectReads(Foo(a))</c> stops AT the call-shaped node and never looks at
    /// its arguments, so this kill can ONLY come from <c>MentionsPastTheWalk</c>'s own
    /// <c>IRCall</c> arm — the Mb mutant (Mentions = CollectReads only) loses it.</summary>
    [Test]
    public void S2_CallArgumentVariable_DirectStoreToIt_KillsTheFact()
    {
        var kept = Kept((f, e, x) =>
        {
            var a = Local(f, "a");
            var c = new IRCall("t1", "Foo", I); c.Arguments.Add(a);
            e.Instructions.Add(c);
            e.Instructions.Add(new IRAssignment(x, c));
            e.Instructions.Add(new IRAssignment(a, new IRConstant(5, I)));
        });
        Assert.That(kept, Is.True, "a call argument variable 'a' must still be found by " +
            "MentionsPastTheWalk's IRCall arm and kill the fact on a direct store to a.");
    }

    /// <summary>S2v — the plain-variable case-insensitivity control: <c>x := a + 1</c>, store
    /// spelled <c>A</c>. This one was ALREADY correct before task #161 (the old Mentions' own
    /// IRVariable arm was always case-insensitive) — pinned here so the S1c/S2v pair discriminates
    /// "case-insensitivity in general" (S2v, unaffected by #161) from "case-insensitivity of the
    /// NEW named-nested-operand arm specifically" (S1c, the Me mutant's real target).</summary>
    [Test]
    public void S2v_VariableOperand_StoreSpelledUppercase_KillsTheFactCaseInsensitively()
    {
        var kept = Kept((f, e, x) =>
        {
            var a = Local(f, "a"); var upperA = Local(f, "A");
            var t = new IRBinaryOp("t1", BinaryOpKind.Add, a, new IRConstant(1, I), I);
            e.Instructions.Add(t);
            e.Instructions.Add(new IRAssignment(x, t));
            e.Instructions.Add(new IRAssignment(upperA, new IRConstant(5, I)));
        });
        Assert.That(kept, Is.True, "a store spelled 'A' must kill a fact reading the plain " +
            "variable 'a' — case-insensitive, as it always was.");
    }

    // ============================ S3: named operand inside a call argument =======================

    /// <summary>S3 — a NAMED nested operand passed AS a call argument: <c>t0 = u * 2</c> (<c>u</c>
    /// named), <c>c = Foo(t0)</c>, <c>x := c</c>, then <c>u = 5</c>. Neither half alone kills this:
    /// <c>CollectReads(c)</c> stops at the call-shaped <c>c</c> before ever reaching <c>t0</c>, and
    /// the OLD <c>MentionsPastTheWalk</c> (pre-#161, the Mf shape) has no named-destination arm to
    /// find <c>u</c> inside <c>t0</c>. Needs the CURRENT arm order: <c>MentionsPastTheWalk</c>'s
    /// <c>IRCall</c> case re-asks the WHOLE <c>Mentions</c> (not just
    /// <c>MentionsPastTheWalk</c>) of each argument, so <c>Mentions(t0, "u")</c> is asked again and
    /// its own CollectReads half finds <c>u</c>. The Mc mutant (call args asked only of
    /// <c>MentionsPastTheWalk</c>) loses exactly this.</summary>
    [Test]
    public void S3_NamedOperandInsideACallArgument_DirectStoreToItsName_KillsTheFact()
    {
        var kept = Kept((f, e, x) =>
        {
            var a = Local(f, "a"); var uVar = Local(f, "u");
            var u = new IRBinaryOp("u", BinaryOpKind.Add, a, new IRConstant(1, I), I) { NamedAfterVariable = true };
            var t0 = new IRBinaryOp("t0", BinaryOpKind.Mul, u, new IRConstant(2, I), I);
            var c = new IRCall("t1", "Foo", I); c.Arguments.Add(t0);
            e.Instructions.Add(u); e.Instructions.Add(t0); e.Instructions.Add(c);
            e.Instructions.Add(new IRAssignment(x, c));
            e.Instructions.Add(new IRAssignment(uVar, new IRConstant(5, I)));
        });
        Assert.That(kept, Is.True, "a named nested operand 'u' inside a call argument must still " +
            "be found and kill the fact on a direct store to u.");
    }

    // ========================= S4 family: field/allocation/instance-call operands ================

    /// <summary>S4 — a field access's OBJECT variable: <c>x := o.F</c>, then <c>o = p</c>. Already
    /// correct before #161 (the old <c>Mentions</c>' own <c>IRFieldAccess</c> arm asked
    /// <c>Mentions(f.Object, name)</c> already) — pinned here as the anti-regression control for
    /// the S4/S4n/S4m trio, which the Mb mutant (CollectReads-only) would ALSO lose, since
    /// <c>CollectReads</c> stops at the field-access node before reaching its object.</summary>
    [Test]
    public void S4_FieldAccessObjectVariable_DirectStoreToIt_KillsTheFact()
    {
        var kept = Kept((f, e, x) =>
        {
            var o = Local(f, "o"); var p = Local(f, "p");
            var fa = new IRFieldAccess("t1", o, "F", I);
            e.Instructions.Add(fa);
            e.Instructions.Add(new IRAssignment(x, fa));
            e.Instructions.Add(new IRAssignment(o, p));
        });
        Assert.That(kept, Is.True, "a field access's object variable 'o' must kill the fact on a " +
            "direct store to o.");
    }

    /// <summary>S4n — an allocation's ARGUMENT variable: <c>x := New T(a)</c>, then <c>a = 5</c>.
    /// Same reasoning as S4, over <c>IRNewObject.Arguments</c>.</summary>
    [Test]
    public void S4n_AllocationArgumentVariable_DirectStoreToIt_KillsTheFact()
    {
        var kept = Kept((f, e, x) =>
        {
            var a = Local(f, "a");
            var n = new IRNewObject("t1", "T", I); n.Arguments.Add(a);
            e.Instructions.Add(n);
            e.Instructions.Add(new IRAssignment(x, n));
            e.Instructions.Add(new IRAssignment(a, new IRConstant(5, I)));
        });
        Assert.That(kept, Is.True, "an allocation argument variable 'a' must kill the fact on a " +
            "direct store to a.");
    }

    /// <summary>S4m — an instance method call's ARGUMENT variable (and, incidentally, its object
    /// <c>o</c> is never stored to here, only the argument): <c>x := o.M(a)</c>, then <c>a = 5</c>.
    /// Same reasoning as S4/S4n, over <c>IRInstanceMethodCall.Arguments</c>.</summary>
    [Test]
    public void S4m_InstanceMethodCallArgumentVariable_DirectStoreToIt_KillsTheFact()
    {
        var kept = Kept((f, e, x) =>
        {
            var a = Local(f, "a"); var o = Local(f, "o");
            var mc = new IRInstanceMethodCall("t1", o, "M", I); mc.Arguments.Add(a);
            e.Instructions.Add(mc);
            e.Instructions.Add(new IRAssignment(x, mc));
            e.Instructions.Add(new IRAssignment(a, new IRConstant(5, I)));
        });
        Assert.That(kept, Is.True, "an instance-method-call argument variable 'a' must kill the " +
            "fact on a direct store to a.");
    }

    // ==================================== S5: the default arm ====================================

    /// <summary>S5 — a value of a kind on NEITHER <c>MentionsPastTheWalk</c>'s pure-operator list
    /// nor its named call-shaped list (<c>IRIndexerAccess</c>, an indexer LOAD): a store to ANY
    /// name, even an UNRELATED one (<c>z</c>, never read by the indexer expression at all), must
    /// still kill the fact — the safe default (<c>default: return true;</c>), matching
    /// <c>CollectReads</c>'s own <c>HitCallShaped</c> default. The Md mutant (default arm answers
    /// <c>false</c>) loses exactly this, and only this — every other shape here is unaffected by
    /// Md because each already has a specific matching arm.</summary>
    [Test]
    public void S5_UnlistedKind_IndexerAccess_AnyStore_KillsTheFact()
    {
        var kept = Kept((f, e, x) =>
        {
            var l = Local(f, "l"); var z = Local(f, "z");
            var ix = new IRIndexerAccess("t1", l, I); ix.Indices.Add(new IRConstant(0, I));
            e.Instructions.Add(ix);
            e.Instructions.Add(new IRAssignment(x, ix));
            e.Instructions.Add(new IRAssignment(z, new IRConstant(5, I)));
        });
        Assert.That(kept, Is.True, "an unlisted value kind (IRIndexerAccess) must be killed by " +
            "ANY named store, via MentionsPastTheWalk's safe default (unmatched kinds answer TRUE).");
    }

    // =============================== R1: the dropped self-referential recording ==================

    /// <summary>R1 — RECORDING, not invalidation: <c>x := (x' * 2)</c> where <c>x'</c> is a named
    /// nested operand spelled <c>x</c> itself (the fact's OWN target name), and nothing else writes
    /// anything afterward. <c>CopyPropagationPass.PropagateCopies</c> only records a fact when
    /// <c>!Mentions(value, target.Name)</c> — before #161, <c>Mentions(t0, "x")</c> was FALSE (the
    /// old walk never found the named nested operand <c>x'</c>), so the self-referential fact
    /// <c>x := t0</c> was wrongly RECORDED, and <c>t9 = x + 0</c> was wrongly substituted to
    /// <c>t9 = t0 + 0</c> — reading <c>x</c>'s OWN pre-store value through a name that, after a real
    /// store to <c>x</c>, would already have moved on. After #161, <c>Mentions(t0, "x")</c> is TRUE
    /// (the union's CollectReads half finds the named nested operand <c>x'</c>), so the recording is
    /// correctly REFUSED and <c>t9</c> keeps reading the variable <c>x</c> directly.</summary>
    [Test]
    public void R1_SelfReferenceByANamedOperandSharingTheTargetsName_RecordingIsRefused()
    {
        IRBinaryOp t0Capture = null;
        var kept = Kept((f, e, x) =>
        {
            var a = Local(f, "a");
            var xn = new IRBinaryOp("x", BinaryOpKind.Add, a, new IRConstant(1, I), I) { NamedAfterVariable = true };
            var t0 = new IRBinaryOp("t0", BinaryOpKind.Mul, xn, new IRConstant(2, I), I);
            t0Capture = t0;
            e.Instructions.Add(xn); e.Instructions.Add(t0);
            e.Instructions.Add(new IRAssignment(x, t0));
        });

        // Sanity: the named nested operand x' (spelled "x", the target's own name) IS visible to
        // CollectReads — confirms the recording refusal below is exercising the #161 arm, not some
        // other reason PropagateCopies might have skipped recording.
        Assert.That(OptimizationPass.CollectReads(t0Capture).Names.Select(r => r.Name), Does.Contain("x"),
            "CollectReads must see t0 as reading storage named 'x' (the named nested operand x') " +
            "for this to be exercising the #161 self-reference-refusal arm");

        Assert.That(kept, Is.True, "x's own fact must never be RECORDED when its value reads a " +
            "named nested operand sharing x's own name — t9 must keep reading x directly, not the " +
            "self-referential t0.");
    }
}

/// <summary>
/// The source-level probe that reaches the defect (E3, <c>S/t161/corpus/E3.bas</c>), compiled
/// through the AGGRESSIVE pipeline IN PROCESS and checked directly against
/// <see cref="IRVerifier.CheckInvariantSPrime"/> — no backend, no process spawned, fast subset.
/// This is the exact check that FIRED at master (one S′ violation, <c>S/t161/ir-E3-base.txt</c>:
/// "operand 'u' is written by IRAssignment in entry before a use in entry") and is silent after
/// the fix (<c>S/t161/ir-E3-fix.txt</c>) — the discriminating structural proof, independent of
/// whether any particular backend happens to re-evaluate the hazardous value. Matches the idiom
/// <c>DynamicUseSPrimeAggressivePipelineStructuralTests</c> already established for L1-L7.
/// </summary>
[TestFixture]
public class CopyPropagationMentionsStructuralTests
{
    /// <summary><c>Dim u As Integer = a + b : Dim x As Double = a + b : u = 5 : Dim y As Double = x
    /// : Return y * c + u</c>. CSE forwards the second <c>a + b</c> to the renamed <c>u</c>; on the
    /// pipeline's second iteration the fact is <c>x := CDbl(u)</c> (an <c>IRCast</c> whose operand
    /// is the named nested operand <c>u</c>), and <c>y</c>'s copy of it gets propagated into
    /// <c>y * c</c> past the direct store <c>u = 5</c> unless <c>Mentions</c> catches it.</summary>
    internal const string E3 = """
        Function F(a As Integer, b As Integer, c As Integer) As Double
            Dim u As Integer = a + b
            Dim x As Double = a + b
            u = 5
            Dim y As Double = x
            Return y * c + u
        End Function

        Sub Main()
            Console.WriteLine(CStr(F(3, 4, 2)))
        End Sub
        """;
    internal const string E3Expected = "19";

    [Test]
    public void E3_AggressivePipeline_VerifierIsQuiet()
    {
        var module = JsTestSupport.BuildModule(E3, sourceFilePath: "prog.bas");
        AggressivePipeline.Apply(module);
        var violations = IRVerifier.CheckInvariantSPrime(module);
        Assert.That(violations, Is.Empty,
            "E3: " + string.Join(" | ", violations.Select(v => v.ToString())) +
            " — at master this fired exactly one S' violation over t2 (the IRCast reading the " +
            "renamed operand u); confirm this test FAILS against the Mf mutant (variables-only " +
            "walk) before trusting it as the discriminator.");
    }
}

/// <summary>
/// E3 (and E4, which adds a THIRD occurrence of <c>a + b</c> computed BEFORE the store — an
/// over-kill control: the fix must not touch <c>w</c>'s own forwarded copy) run for real, through
/// the shared AGGRESSIVE helpers (<see cref="FourBackends.RunsOnEveryBackendAggressive"/>), on
/// every backend. MEASURED (implementer): every backend already printed the right number even at
/// master — the S′ violation was a hazard, not a wrong answer YET, because no backend happened to
/// re-evaluate the stale cast — so these pins exist to prove the fix changes nothing observable,
/// not to catch a wrong answer master already had. <c>*Optimized</c> helpers are STANDARD-only and
/// <c>RunJs</c> runs no optimizer at all (CLAUDE.md/HANDOFF's own trap) — this fixture never uses
/// either.
/// </summary>
[TestFixture]
[Category("Integration")]   // compiles/runs C++, spawns node, assembles/runs IL
[NonParallelizable]         // the C# leg redirects Console.Out (see FourBackends)
public class CopyPropagationMentionsExecutionTests
{
    [Test]
    public void E3_AggressivePipeline_AllFourBackendsAgree()
        => FourBackends.RunsOnEveryBackendAggressive(
            CopyPropagationMentionsStructuralTests.E3, CopyPropagationMentionsStructuralTests.E3Expected);

    /// <summary><c>Dim u As Integer = a + b : Dim x As Double = a + b : Dim w As Integer = (a + b)
    /// * 3 : u = 5 : Dim y As Double = x : Return y * c + u + w</c>. <c>w</c>'s own copy of
    /// <c>(a + b) * 3</c> forwards to the SAME renamed <c>u</c> as <c>x</c>'s cast does, but is
    /// computed and consumed entirely BEFORE the store <c>u = 5</c> — the over-kill control: a
    /// <c>Mentions</c> that killed too broadly (say, every fact whenever ANY name is stored,
    /// matching S5's default arm unconditionally) would still print the right number for a probe
    /// this simple only by accident; a `Mentions` that killed `w`'s already-safe fact would not
    /// change this program's OUTPUT either (w is consumed immediately), so this pin is a coverage
    /// addition over E3, not a stronger discriminator than the structural check above — the
    /// structural check is what actually proves correctness.</summary>
    internal const string E4 = """
        Function F(a As Integer, b As Integer, c As Integer) As Double
            Dim u As Integer = a + b
            Dim x As Double = a + b
            Dim w As Integer = (a + b) * 3
            u = 5
            Dim y As Double = x
            Return y * c + u + w
        End Function

        Sub Main()
            Console.WriteLine(CStr(F(3, 4, 2)))
        End Sub
        """;
    internal const string E4Expected = "40";

    [Test]
    public void E4_AggressivePipeline_AllFourBackendsAgree()
        => FourBackends.RunsOnEveryBackendAggressive(E4, E4Expected);
}
