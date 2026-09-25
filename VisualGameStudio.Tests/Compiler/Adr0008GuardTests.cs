using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

// =====================================================================================
//  ADR-0008 D1/D1-sub/D2 (tasks #157/#156) — replicability-blind Guard(v) via the shared
//  CollectReads walk, the confirmed CYCLE reading (its rename lives in DynamicUseSPrimeTests.cs),
//  and ReadsCallVisible kept.
//
//  `docs/superpowers/decisions/0008-guard-semantics-and-call-visibility-of-computed-values.md`
//  is the ADR; `S/arch-batch/ruling.md` the verbatim ruling this session worked from. Every shape
//  below is ported one-for-one from the implementer's scratch harness
//  (`S/adr8/harness/Program.cs`, its `hand`/`reads`/`cp` modes) — re-run LIVE against a clean
//  rebuild of the CURRENT `BasicLang/IROptimizer.cs`/`IRVerifier.cs` this session (md5
//  `eae27bb045026a04eb2042fa16c3b02b` / `f089e106601df31018d619c7fe9597e3`) to confirm every
//  number below, because the scratchpad's own `harness-*.txt` dumps turned out to be STALE for
//  one entry (IRTupleElement's HitCallShaped) — generated from an earlier build. Trust the
//  numbers here, not the scratch dumps.
//
//  Four fixtures:
//    Adr0008GuardReplicabilityBlindHandBuiltIRTests — hand-built IR, the D1 contract itself: the
//                                                      MUST-FIRE / MUST-STAY-QUIET shapes.
//    Adr0008CollectReadsAgreementTests              — the G2 discriminator: CollectReads and
//                                                      ReadsCallVisible cannot drift, per node
//                                                      kind AND swept over real compiled IR.
//    Adr0008HitCallShapedPinTests                   — the G3 discriminator: HitCallShaped pinned
//                                                      directly, independent of a name check.
//    Adr0008D2Pins                                  — the "Keep" contract: ReadsCallVisible TRUE
//                                                      for every call-shaped kind, a CP1-family
//                                                      New/instance-method-call kill, and the
//                                                      pre-existing CopyPropagation gap this task
//                                                      found (a store to a NAMED NESTED operand
//                                                      does not kill a fact — NOT fixed here; see
//                                                      that fixture's own remarks).
// =====================================================================================

/// <summary>Q2a-Q2e, ported verbatim from <c>S/arch-batch/probes/Q2*.bas</c>/<c>.exp</c> — ADR-0008
/// D2's own byte-identity/output corpus. Shared by <see cref="Adr0008CollectReadsAgreementTests"/>'s
/// corpus sweep here and by the execution pins in <c>DynamicUseSPrimeTests.cs</c> (CLAUDE.md:
/// change shared source once).</summary>
internal static class Adr0008Q2Probes
{
    /// <summary>Q2a — <c>Dim x = Seed()</c> (reads the module global <c>K</c>) then a call
    /// (<c>Inc</c>) that bumps <c>K</c>, then a use of <c>x</c>. IR-identical to HEAD under D2
    /// (MEASURED): the call lowers straight into <c>x</c>, no copy, no fact to relax.</summary>
    internal const string Q2a = """
        Module G
         Public K As Integer = 1
        End Module
        Function Seed() As Integer
         Console.WriteLine("seed")
         Return K * 10
        End Function
        Sub Inc()
         Console.WriteLine("inc")
         K = K + 1
        End Sub
        Sub Main()
         Dim x As Integer = Seed()
         Inc()
         Console.WriteLine(CStr(x + 1))
        End Sub
        """;
    internal const string Q2aExpected = "seed\ninc\n11";

    /// <summary>Q2b — Q2a plus a second local (<c>y = x + 1</c>) and a second call. Same
    /// IR-identical-to-HEAD reason as Q2a.</summary>
    internal const string Q2b = """
        Module G
         Public K As Integer = 1
        End Module
        Function Seed() As Integer
         Console.WriteLine("seed")
         Return K * 10
        End Function
        Sub Inc()
         Console.WriteLine("inc")
         K = K + 1
        End Sub
        Sub Main()
         Dim x As Integer = Seed()
         Inc()
         Dim y As Integer = x + 1
         Inc()
         Console.WriteLine(CStr(x + y))
        End Sub
        """;
    internal const string Q2bExpected = "seed\ninc\ninc\n21";

    /// <summary>Q2c — a copy fact from a <c>New DateTime(...)</c> value-type construction,
    /// substituted across an intervening call (<c>Bump</c>, which reassigns the module global
    /// <c>D</c> through <c>D.AddDays(100)</c>). C#/C++ only (MEASURED,
    /// <c>S/arch-batch/probes/matrix.txt</c>): JavaScript has no <c>DateTime</c> at all
    /// (<c>ReferenceError</c> at run time) and MSIL has no lowering for it (<c>ilasm</c>:
    /// "Reference to undefined class 'DateTime'") — both pre-existing gaps, unrelated to
    /// ADR-0008.</summary>
    internal const string Q2c = """
        Module G
         Public D As DateTime
        End Module
        Sub Bump()
         D = D.AddDays(100)
        End Sub
        Sub Main()
         D = New DateTime(2026, 1, 1)
         Dim later As DateTime = D.AddDays(5)
         Bump()
         Console.WriteLine(CStr(later.Day))
        End Sub
        """;
    internal const string Q2cExpected = "6";

    /// <summary>Q2d — Q2c plus a <c>TimeSpan</c> subtraction over the substituted value. C#/C++
    /// only (MEASURED, <c>S/arch-batch/probes/matrix2.txt</c>): JavaScript refuses
    /// <c>TimeSpan</c> outright (BL7007); MSIL has the same missing-<c>DateTime</c> gap as
    /// Q2c.</summary>
    internal const string Q2d = """
        Module G
         Public D As DateTime
        End Module
        Sub Bump()
         D = D.AddDays(100)
        End Sub
        Sub Main()
         D = New DateTime(2026, 1, 1)
         Dim later As DateTime = D.AddDays(5)
         Bump()
         Dim gap As TimeSpan = later - D
         Console.WriteLine(CStr(gap.Days))
        End Sub
        """;
    internal const string Q2dExpected = "-95";

    /// <summary>Q2e — the D2 "Keep" headline: a copy fact from BOTH a <c>New Box()</c>
    /// allocation AND an instance-method call (<c>b.Twice()</c>), each substituted across an
    /// intervening call (<c>Bump</c>, which writes the object's field through the reference).
    /// Right on ALL FOUR backends (MEASURED 12/12 across three entry points,
    /// <c>S/arch-batch/probes/matrix2.txt</c> — this fixture's aggressive-pipeline slice is 4 of
    /// those 12 cells).</summary>
    internal const string Q2e = """
        Class Box
         Public V As Integer
         Public Function Twice() As Integer
          Return V * 2
         End Function
        End Class
        Sub Bump(bx As Box)
         bx.V = 100
        End Sub
        Sub Main()
         Dim b As New Box()
         b.V = 5
         Dim x As Integer = b.Twice()
         Bump(b)
         Console.WriteLine(CStr(x + 1))
         Console.WriteLine(CStr(b.Twice() + 1))
        End Sub
        """;
    internal const string Q2eExpected = "11\n201";
}

/// <summary>
/// ADR-0008 D1's own contract, hand-built — ported one-for-one from
/// <c>S/adr8/harness/Program.cs</c>'s <c>hand</c> mode (all 14 shapes; re-run live this session,
/// 0 mismatches against the CURRENT <c>IROptimizer.cs</c>/<c>IRVerifier.cs</c>). Same six-block
/// skeleton style as <c>DynamicUseSPrimeHandBuiltIRTests</c>, but this harness's own fixture is
/// <c>Sub Work(ByRef n)</c> with a module global <c>g</c> — the shapes the ruling's contract
/// names by variable, not by letter.
/// </summary>
[TestFixture]
public class Adr0008GuardReplicabilityBlindHandBuiltIRTests
{
    private static readonly TypeInfo IntType = new TypeInfo("Integer", TypeKind.Primitive);
    private static readonly TypeInfo BoolType = new TypeInfo("Boolean", TypeKind.Primitive);

    private sealed class Fixture
    {
        public IRModule Module;
        public IRFunction Function;
        public BasicBlock Entry;
        public IRVariable N; // ByRef parameter
        public IRVariable G; // non-Const module global
        public IRVariable C; // condition local, never assigned — only its identity matters
    }

    /// <summary>Matches the harness's <c>Fx()</c>: <c>Sub Work(ByRef n As Integer)</c>, a module
    /// global <c>g</c>, a declared Boolean local <c>c</c> used as every branch condition.</summary>
    private static Fixture NewFixture()
    {
        var module = new IRModule("M");
        var function = new IRFunction("Work", IntType);
        module.Functions.Add(function);
        var n = new IRVariable("n", IntType) { IsParameter = true, IsByRef = true };
        function.Parameters.Add(n);
        var g = new IRVariable("g", IntType) { IsGlobal = true };
        var c = new IRVariable("c", BoolType);
        function.LocalVariables.Add(c);
        var entry = function.CreateBlock("entry");
        return new Fixture { Module = module, Function = function, Entry = entry, N = n, G = g, C = c };
    }

    /// <summary>A call whose only role is to READ <paramref name="x"/> — every loop shape's use site.</summary>
    private static IRCall Use(IRValue x)
    {
        var call = new IRCall("", "Show", IntType);
        call.Arguments.Add(x);
        return call;
    }

    /// <summary>An assignment to <paramref name="x"/> — the DIRECT-STORE shapes' candidate write.</summary>
    private static IRAssignment Write(IRVariable x) => new IRAssignment(x, new IRConstant(5, IntType));

    private static IRInstruction CallIncG(IRVariable n, IRVariable g) => new IRCall("", "IncG", IntType);

    /// <summary>The harness's <c>Loop()</c>: preheader (<c>entry</c>) holds <paramref name="build"/>'s
    /// definitions; <c>loop.head</c>/<c>loop.body</c>/<c>loop.exit</c> follow; the body is
    /// <paramref name="build"/>'s used value (a call-use, or — when <paramref name="useIsCall"/>
    /// is false — a direct assignment to a fresh local <c>s</c>, so a DIRECT STORE can be the
    /// only writer instead of a call) then <paramref name="inLoop"/>'s instruction.</summary>
    private static IRModule Loop(
        Func<IRFunction, IRVariable, IRVariable, (List<IRInstruction> Defs, IRValue Used)> build,
        Func<IRVariable, IRVariable, IRInstruction> inLoop, bool useIsCall = true)
    {
        var f = NewFixture();
        var head = f.Function.CreateBlock("loop.head");
        var body = f.Function.CreateBlock("loop.body");
        var exit = f.Function.CreateBlock("loop.exit");
        var (defs, used) = build(f.Function, f.N, f.G);
        foreach (var d in defs) f.Entry.Instructions.Add(d);
        f.Entry.Instructions.Add(new IRBranch(head));
        head.Instructions.Add(new IRConditionalBranch(f.C, body, exit));
        var sLocal = new IRVariable("s", IntType);
        f.Function.LocalVariables.Add(sLocal);
        body.Instructions.Add(useIsCall ? Use(used) : new IRAssignment(sLocal, used));
        body.Instructions.Add(inLoop(f.N, f.G));
        body.Instructions.Add(new IRBranch(head));
        exit.Instructions.Add(new IRReturn());
        return f.Module;
    }

    /// <summary>Asserts QUIET, or FIRES with the fields that discriminate a real hit: the exact
    /// guarded <see cref="InvariantViolation.Variable"/>, whether it is the value's own destination
    /// (<see cref="InvariantViolation.IsDestination"/>), and <see cref="InvariantViolation.UseRepeats"/>
    /// (true for every loop shape below, false for the two straight-line "static twin"/"named
    /// nested operand" shapes, which share statically instead).</summary>
    private static void AssertVerdict(IRModule module, bool expectFires, bool expectUseRepeats = true,
        string expectVariable = null, bool? expectIsDestination = null)
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
            Assert.That(violations[0].UseRepeats, Is.EqualTo(expectUseRepeats), "UseRepeats — got: " + violations[0]);
            if (expectVariable != null)
                Assert.That(violations[0].Variable, Is.EqualTo(expectVariable), "wrong guarded variable — got: " + violations[0]);
            if (expectIsDestination.HasValue)
                Assert.That(violations[0].IsDestination, Is.EqualTo(expectIsDestination.Value), "wrong IsDestination — got: " + violations[0]);
        });
    }

    // ============================= MUST FIRE (the ruling's contract) =============================

    /// <summary>(i) — the ruling's shape (i): a preheader value reading a <c>ByRef</c> parameter,
    /// one in-loop use, a CALL in the loop. Pre-ADR8 this operand was pruned from Guard as
    /// non-replicable (<see cref="IRReplicability.IsReplicable"/> says a <c>ByRef</c> parameter
    /// never is) — QUIET. Post-ADR8, Guard is replicability-blind: FIRES. The G1 mutant kill.</summary>
    [Test]
    public void I_PreheaderTimesTwoOverByRefParam_OneUseInLoop_CallInLoop_Fires()
    {
        var module = Loop((f, n, g) =>
        {
            var v = new IRBinaryOp("t0", BinaryOpKind.Mul, n, new IRConstant(2, IntType), IntType);
            return (new List<IRInstruction> { v }, (IRValue)v);
        }, CallIncG);
        AssertVerdict(module, expectFires: true, expectVariable: "n", expectIsDestination: false);
    }

    /// <summary>(ii) — the ruling's shape (ii): the same, over a non-<c>Const</c> module global
    /// instead of a <c>ByRef</c> parameter. Same reasoning, same G1 kill.</summary>
    [Test]
    public void Ii_PreheaderTimesTwoOverNonConstGlobal_OneUseInLoop_CallInLoop_Fires()
    {
        var module = Loop((f, n, g) =>
        {
            var v = new IRBinaryOp("t0", BinaryOpKind.Mul, g, new IRConstant(2, IntType), IntType);
            return (new List<IRInstruction> { v }, (IRValue)v);
        }, CallIncG);
        AssertVerdict(module, expectFires: true, expectVariable: "g", expectIsDestination: false);
    }

    /// <summary>(iii) — the ruling's shape (iii): same as (i), but the loop's writer is a DIRECT
    /// STORE (<c>n = 5</c>, an <c>IRAssignment</c>) instead of a call. Guard's operand half does
    /// not care whether the writer is a call or a direct store — either way <c>n</c> is guarded
    /// and written between the definition and the (repeated) use.</summary>
    [Test]
    public void Iii_PreheaderTimesTwoOverByRefParam_OneUseInLoop_DirectStoreInLoop_Fires()
    {
        var module = Loop((f, n, g) =>
        {
            var v = new IRBinaryOp("t0", BinaryOpKind.Mul, n, new IRConstant(2, IntType), IntType);
            return (new List<IRInstruction> { v }, (IRValue)v);
        }, (n, g) => Write(n));
        AssertVerdict(module, expectFires: true, expectVariable: "n", expectIsDestination: false);
    }

    /// <summary>(iii-g) — the direct-store twin of (ii): same as (iii), over the global.</summary>
    [Test]
    public void IiiG_PreheaderTimesTwoOverGlobal_OneUseInLoop_DirectStoreInLoop_Fires()
    {
        var module = Loop((f, n, g) =>
        {
            var v = new IRBinaryOp("t0", BinaryOpKind.Mul, g, new IRConstant(2, IntType), IntType);
            return (new List<IRInstruction> { v }, (IRValue)v);
        }, (n, g) => Write(g), useIsCall: false);
        AssertVerdict(module, expectFires: true, expectVariable: "g", expectIsDestination: false);
    }

    /// <summary>The STATIC twin the ruling names explicitly: two STATIC uses of a value reading a
    /// <c>ByRef</c> parameter, with a call between them — no loop at all, so
    /// <see cref="InvariantViolation.UseRepeats"/> is FALSE here; this shape shares purely
    /// statically (ADR-0005 D2's literal "&gt; 1"), proving D1's blindness to replicability is not
    /// limited to the dynamic-sharing case E already measured zero-fire on.</summary>
    [Test]
    public void StaticTwin_ByRefParam_TwoStaticUses_CallBetween_Fires()
    {
        var f = NewFixture();
        var v = new IRBinaryOp("t0", BinaryOpKind.Mul, f.N, new IRConstant(2, IntType), IntType);
        f.Entry.Instructions.Add(v);
        f.Entry.Instructions.Add(Use(v));
        f.Entry.Instructions.Add(new IRCall("", "IncG", IntType));
        f.Entry.Instructions.Add(Use(v));
        f.Entry.Instructions.Add(new IRReturn());
        AssertVerdict(f.Module, expectFires: true, expectUseRepeats: false, expectVariable: "n", expectIsDestination: false);
    }

    /// <summary>The global twin of the static shape above.</summary>
    [Test]
    public void StaticTwin_Global_TwoStaticUses_CallBetween_Fires()
    {
        var f = NewFixture();
        var v = new IRBinaryOp("t0", BinaryOpKind.Mul, f.G, new IRConstant(2, IntType), IntType);
        f.Entry.Instructions.Add(v);
        f.Entry.Instructions.Add(Use(v));
        f.Entry.Instructions.Add(new IRCall("", "IncG", IntType));
        f.Entry.Instructions.Add(Use(v));
        f.Entry.Instructions.Add(new IRReturn());
        AssertVerdict(f.Module, expectFires: true, expectUseRepeats: false, expectVariable: "g", expectIsDestination: false);
    }

    /// <summary>(q1d) — a store to a NAMED operand <c>t</c> (a named nested operand: <c>t = Foo(n)</c>
    /// renamed <c>t</c>, <c>v = t + 1</c>) between the definition and the use: the name IS read
    /// (every backend reads a named operand instruction back by its name), so a direct store to
    /// that SAME NAME fires — even though Guard stops descending PAST <c>t</c> (a call-shaped
    /// node) into its own operand <c>n</c>, per the quiet shapes below.</summary>
    [Test]
    public void NamedNestedOperand_DirectStoreToItsOwnName_Fires()
    {
        var module = Loop((f, n, g) =>
        {
            f.LocalVariables.Add(new IRVariable("t", IntType));
            var t = new IRCall("t", "Foo", IntType) { NamedAfterVariable = true };
            t.Arguments.Add(n);
            var v = new IRBinaryOp("t2", BinaryOpKind.Add, t, new IRConstant(1, IntType), IntType);
            return (new List<IRInstruction> { t, v }, (IRValue)v);
        }, (n, g) => Write(new IRVariable("t", IntType)), useIsCall: false);
        AssertVerdict(module, expectFires: true, expectVariable: "t", expectIsDestination: false);
    }

    /// <summary>(nn) — THE G4 DISCRIMINATOR: <c>t0 = a * 2</c>, where <c>a</c> is itself a NAMED
    /// pure operator (<c>a = p + 1</c>, <c>NamedAfterVariable</c>) — a named nested operand one
    /// level deeper than the shape above. Guard(t0) must contain not only <c>a</c>'s own name but
    /// also <c>p</c>, <c>a</c>'s OWN operand — the walk still descends into a named pure operand's
    /// operands (the ADR's own words). Two static uses of <c>t0</c>, a write of <c>p</c> between
    /// them: FIRES. A mutant that stops <c>CollectReads</c> at a named nested operand WITHOUT
    /// descending into it (G4) would never reach <c>p</c> at all and turn this QUIET.</summary>
    [Test]
    public void NamedNestedOperand_DescendsIntoItsOwnOperands_WriteToNestedOperand_Fires()
    {
        var f = NewFixture();
        var a = new IRVariable("a", IntType);
        var p = new IRVariable("p", IntType);
        f.Function.LocalVariables.Add(a);
        f.Function.LocalVariables.Add(p);
        var named = new IRBinaryOp("a", BinaryOpKind.Add, p, new IRConstant(1, IntType), IntType) { NamedAfterVariable = true };
        var v = new IRBinaryOp("t0", BinaryOpKind.Mul, named, new IRConstant(2, IntType), IntType);
        f.Entry.Instructions.Add(named);
        f.Entry.Instructions.Add(v);
        f.Entry.Instructions.Add(Use(v));
        f.Entry.Instructions.Add(Write(p));
        f.Entry.Instructions.Add(Use(v));
        f.Entry.Instructions.Add(new IRReturn());
        AssertVerdict(f.Module, expectFires: true, expectUseRepeats: false, expectVariable: "p", expectIsDestination: false);
    }

    // ============================ MUST STAY QUIET (the ruling's contract) ========================

    /// <summary>(q1) — <c>t = Foo(n)</c> (call-shaped, anonymous), <c>v = t + 1</c>, a call in the
    /// loop: QUIET. Guard(v) stops AT the call-shaped <c>t</c> — <c>t</c> is evaluated once where
    /// it is defined, so <c>n</c> (its argument, never re-read) is not collected at all. This is
    /// the ruling's own quiet-shape text: "Guard stops at <c>t</c>; <c>n</c> is not collected".</summary>
    [Test]
    public void Q1_AnonCallShapedOperand_GuardStopsAtT_CallInLoop_Quiet()
    {
        var module = Loop((f, n, g) =>
        {
            var t = new IRCall("t1", "Foo", IntType);
            t.Arguments.Add(n);
            var v = new IRBinaryOp("t2", BinaryOpKind.Add, t, new IRConstant(1, IntType), IntType);
            return (new List<IRInstruction> { t, v }, (IRValue)v);
        }, CallIncG);
        AssertVerdict(module, expectFires: false);
    }

    /// <summary>(q1b) — the same shape, but the loop's writer is a DIRECT STORE to <c>n</c>
    /// instead of a call. Still QUIET: <c>n</c> was never collected in the first place, so it
    /// makes no difference whether its writer is a call or a direct store.</summary>
    [Test]
    public void Q1b_AnonCallShapedOperand_GuardStopsAtT_DirectStoreToNInLoop_Quiet()
    {
        var module = Loop((f, n, g) =>
        {
            var t = new IRCall("t1", "Foo", IntType);
            t.Arguments.Add(n);
            var v = new IRBinaryOp("t2", BinaryOpKind.Add, t, new IRConstant(1, IntType), IntType);
            return (new List<IRInstruction> { t, v }, (IRValue)v);
        }, (n, g) => Write(n), useIsCall: false);
        AssertVerdict(module, expectFires: false);
    }

    /// <summary>(q1c) — the same shape as (q1), but <c>t</c> is a NAMED declared local (not
    /// anonymous). Still QUIET: nothing writes <c>t</c>'s name here (the call in the loop is
    /// call-visible, but <c>t</c>'s guard entry is call-visible too, per <c>IsCallVisible</c>'s
    /// own name check — wait: this shape's call DOES write everything call-visible, but there is
    /// no candidate write of <c>t</c> at all in the loop, only the same unrelated call as (q1)).
    /// Distinguishes "named but nothing writes the name" from (q1d)'s "named AND something writes
    /// the name".</summary>
    [Test]
    public void Q1c_NamedCallShapedOperand_NothingWritesItsName_CallInLoop_Quiet()
    {
        var module = Loop((f, n, g) =>
        {
            f.LocalVariables.Add(new IRVariable("t", IntType));
            var t = new IRCall("t", "Foo", IntType) { NamedAfterVariable = true };
            t.Arguments.Add(n);
            var v = new IRBinaryOp("t2", BinaryOpKind.Add, t, new IRConstant(1, IntType), IntType);
            return (new List<IRInstruction> { t, v }, (IRValue)v);
        }, CallIncG);
        AssertVerdict(module, expectFires: false);
    }

    /// <summary>(q2) — a FIELD-LOAD operand: <c>t = b.F</c> (anonymous <see cref="IRFieldAccess"/>,
    /// call-shaped), <c>v = t + 1</c>, a call in the loop. QUIET for the same reason as (q1): the
    /// walk stops at the call-shaped field load.</summary>
    [Test]
    public void Q2_AnonFieldLoadOperand_GuardStopsAtT_CallInLoop_Quiet()
    {
        var module = Loop((f, n, g) =>
        {
            var b = new IRVariable("b", IntType);
            f.LocalVariables.Add(b);
            var t = new IRFieldAccess("t1", b, "F", IntType);
            var v = new IRBinaryOp("t2", BinaryOpKind.Add, t, new IRConstant(1, IntType), IntType);
            return (new List<IRInstruction> { t, v }, (IRValue)v);
        }, CallIncG);
        AssertVerdict(module, expectFires: false);
    }

    /// <summary>(b2) — the D2-regression control: plain BY-VALUE declared locals (never <c>ByRef</c>,
    /// never global) stay quiet across a call, exactly as before ADR-0008 — replicability-blindness
    /// only widens Guard to variables it used to prune (<c>ByRef</c>/global), it does not add
    /// anything for an ordinary local.</summary>
    [Test]
    public void B2_PlainDeclaredLocals_CallInLoop_Quiet()
    {
        var module = Loop((f, n, g) =>
        {
            var p = new IRVariable("p", IntType);
            var q = new IRVariable("q", IntType);
            f.LocalVariables.Add(p);
            f.LocalVariables.Add(q);
            var v = new IRBinaryOp("t0", BinaryOpKind.Add, p, q, IntType);
            return (new List<IRInstruction> { v }, (IRValue)v);
        }, CallIncG);
        AssertVerdict(module, expectFires: false);
    }

    /// <summary>(const) — a <c>Const</c> global, with a call in the loop and NO store of anything:
    /// QUIET (the negative control — nothing writes <c>K</c>, const or not).</summary>
    [Test]
    public void Const_ConstGlobal_CallInLoop_NoStore_Quiet()
    {
        var module = Loop((f, n, g) =>
        {
            var k = new IRVariable("K", IntType) { IsGlobal = true, IsConst = true };
            var v = new IRBinaryOp("t0", BinaryOpKind.Mul, k, new IRConstant(2, IntType), IntType);
            return (new List<IRInstruction> { v }, (IRValue)v);
        }, CallIncG);
        AssertVerdict(module, expectFires: false);
    }
}

/// <summary>
/// ADR-0008 D1's obligation: "Implement ONE operand walk ... so Guard and ReadsCallVisible cannot
/// drift." <c>ReadsCallVisible(v, f) == HitCallShaped || Names.Any(r =&gt; IsCallVisible(r, f))</c>
/// is now true BY CONSTRUCTION for the current code (ReadsCallVisible literally computes it that
/// way) — so the interesting mutant is one that reverts ReadsCallVisible to a PRIVATE recursion
/// that no longer agrees with what CollectReads (still feeding Guard) finds. The implementer's own
/// G2 mutant (<c>S/adr8/mutate.py</c>) restores the OLD recursion verbatim, which is mathematically
/// equivalent (measured 0 mismatches) — proving the agreement pin needs a recursion that actually
/// DRIFTS (the brief's own suggestion: drop the named-operand check) to have anything to kill; see
/// the mutation-prove notes in the handback for the mutant actually used.
/// </summary>
[TestFixture]
public class Adr0008CollectReadsAgreementTests
{
    private static IRFunction NewFunction(out IRVariable local, out IRVariable byRefParam)
    {
        var f = new IRFunction("Work", IntType);
        local = new IRVariable("x", IntType);
        f.LocalVariables.Add(local);
        byRefParam = new IRVariable("n", IntType) { IsParameter = true, IsByRef = true };
        f.Parameters.Add(byRefParam);
        return f;
    }

    private static readonly TypeInfo IntType = new TypeInfo("Integer", TypeKind.Primitive);

    /// <summary>Asserts BOTH the hardcoded per-kind expectation (a regression pin) AND the
    /// agreement formula itself, independent of that expectation (the G2 discriminator: this half
    /// stays true for ANY correct implementation, so it is what actually proves Guard and
    /// ReadsCallVisible cannot drift, rather than merely proving today's numbers match
    /// yesterday's). Plain <c>Assert.That</c>, deliberately NOT its own <c>Assert.Multiple</c>:
    /// every call site wraps the whole per-kind table in ONE outer <c>Assert.Multiple</c>, so one
    /// kind's mismatch does not hide every kind after it in the same run.</summary>
    private static void Pin(IRFunction f, string what, IRValue v, bool expectRcv, bool expectHit)
    {
        var (names, hit) = OptimizationPass.CollectReads(v);
        var rcv = OptimizationPass.ReadsCallVisible(v, f);
        Assert.That(hit, Is.EqualTo(expectHit), $"{what}: HitCallShaped");
        Assert.That(rcv, Is.EqualTo(expectRcv), $"{what}: ReadsCallVisible");
        Assert.That(rcv, Is.EqualTo(hit || names.Any(r => OptimizationPass.IsCallVisible(r, f))),
            $"{what}: the G2 agreement pin — ReadsCallVisible(v,f) must equal " +
            "HitCallShaped || Names.Any(IsCallVisible), by ADR-0008 D1's own obligation");
    }

    /// <summary>Every node kind the walk distinguishes, ported from <c>S/adr8/harness/Program.cs</c>'s
    /// <c>reads</c> mode (re-run live this session against the current code — the scratch dump had
    /// a stale entry for <c>IRTupleElement</c>; the value here (<c>Hit=true</c>) is the CONFIRMED
    /// one). Adds the harness's uncovered case: a variable WITHOUT a name (an empty
    /// <see cref="IRVariable.Name"/>) — <see cref="StorageRead.Name"/>'s own doc comment calls this
    /// out as the one case it can be null/empty for.</summary>
    [Test]
    public void PerNodeKind_AgreementAndPinnedValues() => Assert.Multiple(() =>
    {
        var f = NewFunction(out var local, out var byRefParam);
        var obj = new IRVariable("o", IntType);
        f.LocalVariables.Add(obj);
        var nameless = new IRVariable("", IntType);

        var call = new IRCall("t1", "Foo", IntType);
        call.Arguments.Add(local);

        // ---- call-shaped kinds: RCV=True, Hit=True, no names collected (the walk stops there). --
        Pin(f, "IRCall Foo(x)", call, true, true);
        Pin(f, "IRInstanceMethodCall o.M(x)", new IRInstanceMethodCall("t1", obj, "M", IntType), true, true);
        Pin(f, "IRBaseMethodCall MyBase.M()", new IRBaseMethodCall("t1", "M", IntType), true, true);
        Pin(f, "IRNewObject New Box()", new IRNewObject("t1", "Box", IntType), true, true);
        Pin(f, "IRFieldAccess o.F", new IRFieldAccess("t1", obj, "F", IntType), true, true);
        Pin(f, "IRIndexerAccess o(0)", new IRIndexerAccess("t1", obj, IntType), true, true);
        Pin(f, "IRAwait", new IRAwait("t1", call, IntType), true, true);

        // ---- default-arm kinds (on neither list): RCV=True, Hit=True too. --------------------
        Pin(f, "IRArrayAlloc", new IRArrayAlloc("t1", IntType, 3), true, true);
        Pin(f, "IRLoad", new IRLoad("t1", obj, IntType), true, true);
        Pin(f, "IRPhi", new IRPhi("t1", IntType), true, true);
        Pin(f, "IRGetElementPtr", new IRGetElementPtr("t1", obj, IntType), true, true);
        // IRTupleElement: Hit=TRUE (default arm), but its RCV would stay True even under a
        // HitCallShaped=false mutant, because its own auto-name ("_tuple_elem_0") is undeclared
        // and so call-visible by NAME alone — the reason item 3's HitCallShaped pins use Load/
        // Phi/GetElementPtr/ArrayAlloc as the G3 discriminator instead of this one.
        Pin(f, "IRTupleElement", new IRTupleElement(obj, 0, IntType), true, true);

        // ---- pure operators over a plain declared local: neither call-visible nor call-shaped. --
        Pin(f, "x + 1 (declared local)",
            new IRBinaryOp("t1", BinaryOpKind.Add, local, new IRConstant(1, IntType), IntType), false, false);
        Pin(f, "-(CType(x)) (nested pure, anonymous operand)",
            new IRUnaryOp("t2", UnaryOpKind.Neg, new IRCast("t1", local, IntType, IntType, CastKind.Bitcast), IntType), false, false);

        // ---- pure operator over a ByRef parameter: call-visible via the variable, not HitCallShaped.
        Pin(f, "n + 1 (ByRef param)",
            new IRBinaryOp("t1", BinaryOpKind.Add, byRefParam, new IRConstant(1, IntType), IntType), true, false);

        // ---- pure operator nested INSIDE a call-shaped operand: the walk stops, so the outer
        // node is call-shaped (Hit=True) regardless of what is inside Foo(x).
        Pin(f, "Foo(x) + 1",
            new IRBinaryOp("t2", BinaryOpKind.Add, call, new IRConstant(1, IntType), IntType), true, true);

        // ---- a NAMED nested pure operand (K undeclared): its name is collected (call-visible by
        // name, since K is not a declared variable) but it is NOT call-shaped (a pure operator).
        var namedK = new IRBinaryOp("K", BinaryOpKind.Add, local, new IRConstant(1, IntType), IntType) { NamedAfterVariable = true };
        Pin(f, "(K = x + 1 named, K undeclared) * 2",
            new IRBinaryOp("t2", BinaryOpKind.Mul, namedK, new IRConstant(2, IntType), IntType), true, false);

        // ---- a variable WITHOUT a name: StorageRead.Name is empty; IsCallVisible(read, f) falls
        // to the Variable overload (its flags, not a name lookup) — and that overload's own words
        // are "An IRVariable with no name is not a declaration of anything — unknown, so visible":
        // call-visible TRUE (MEASURED; corrects this test's first draft, which assumed false).
        Pin(f, "(nameless variable) + 1",
            new IRBinaryOp("t1", BinaryOpKind.Add, nameless, new IRConstant(1, IntType), IntType), true, false);
    });

    /// <summary>The agreement formula swept over REAL compiled IR — L1-L7 (LICM's kill-vocabulary
    /// probes plus <c>DynamicUseSPrimeProbes.L7</c>/<c>L4r</c>) and Q2a-Q2e, each built and run
    /// through the AGGRESSIVE pipeline (never the non-optimizing helper — CLAUDE.md/HANDOFF's own
    /// trap: the green suite has hidden bugs the optimizer exposed before). This is the corpus half
    /// of the "measured on 76,167 calls" claim in the ADR's implementation note — a much smaller
    /// corpus here (this fixture proves the PROPERTY over source the suite already trusts; the
    /// 76,167-call figure is the implementer's own full-corpus sweep, not reproduced in-process
    /// here).</summary>
    [Test]
    public void SweptOverAggressivePipelineIR_L1ThroughL7AndQ2aThroughQ2e()
    {
        int checkedCount = 0;
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
                         ("L4r", DynamicUseSPrimeProbes.L4r),
                         ("Q2a", Adr0008Q2Probes.Q2a),
                         ("Q2b", Adr0008Q2Probes.Q2b),
                         ("Q2c", Adr0008Q2Probes.Q2c),
                         ("Q2d", Adr0008Q2Probes.Q2d),
                         ("Q2e", Adr0008Q2Probes.Q2e),
                     })
            {
                var module = JsTestSupport.BuildModule(source, sourceFilePath: "prog.bas");
                AggressivePipeline.Apply(module);
                foreach (var function in module.Functions)
                {
                    if (function == null || function.IsExternal || function.Blocks == null) continue;
                    foreach (var block in function.Blocks)
                        foreach (var inst in block.Instructions)
                        {
                            if (inst is not IRValue value) continue;
                            checkedCount++;
                            var (names, hit) = OptimizationPass.CollectReads(value);
                            var rcv = OptimizationPass.ReadsCallVisible(value, function);
                            var formula = hit || names.Any(r => OptimizationPass.IsCallVisible(r, function));
                            Assert.That(rcv, Is.EqualTo(formula),
                                $"{name}/{function.Name}/{block.Name}: {value.GetType().Name} '{value.Name}' — " +
                                "ReadsCallVisible disagrees with CollectReads (the G2 agreement pin)");
                        }
                }
            }
        });
        Assert.That(checkedCount, Is.GreaterThan(50),
            "the corpus sweep checked suspiciously few instructions — probes may have stopped compiling");
    }
}

/// <summary>
/// ADR-0008 D1's <c>HitCallShaped</c> half, pinned DIRECTLY (the implementer's G3 advice) —
/// independent of <see cref="OptimizationPass.ReadsCallVisible"/>'s name-based arm, which can stay
/// TRUE for a node whose default name is undeclared even when <c>HitCallShaped</c> itself is
/// wrong (see <see cref="Adr0008CollectReadsAgreementTests"/>'s note on <c>IRTupleElement</c> —
/// excluded here for exactly that reason, per the ruling's brief).
/// </summary>
[TestFixture]
public class Adr0008HitCallShapedPinTests
{
    private static readonly TypeInfo IntType = new TypeInfo("Integer", TypeKind.Primitive);

    private static void PinHit(string what, IRValue v, bool expectHit)
        => Assert.That(OptimizationPass.CollectReads(v).HitCallShaped, Is.EqualTo(expectHit), what);

    [Test]
    public void CallShapedKinds_HitCallShapedTrue()
    {
        var obj = new IRVariable("o", IntType);
        var call = new IRCall("t1", "Foo", IntType);
        Assert.Multiple(() =>
        {
            PinHit("IRCall", call, true);
            PinHit("IRInstanceMethodCall", new IRInstanceMethodCall("t1", obj, "M", IntType), true);
            PinHit("IRBaseMethodCall", new IRBaseMethodCall("t1", "M", IntType), true);
            PinHit("IRNewObject", new IRNewObject("t1", "Box", IntType), true);
            PinHit("IRFieldAccess", new IRFieldAccess("t1", obj, "F", IntType), true);
            PinHit("IRIndexerAccess", new IRIndexerAccess("t1", obj, IntType), true);
            PinHit("IRAwait", new IRAwait("t1", call, IntType), true);
        });
    }

    /// <summary>The G3 discriminator: a kind on NEITHER the pure nor the call-shaped list also
    /// sets <c>HitCallShaped</c> — "the safe default, matching D1's Universal". A mutant that flips
    /// the default arm's <c>hitCallShaped = true</c> to <c>false</c> turns every one of these
    /// FALSE. <c>IRTupleElement</c> is deliberately NOT here — its default name stays call-visible
    /// through the NAME check regardless, so it does not discriminate G3 on its own (it still would
    /// under a DIRECT HitCallShaped read, as pinned in
    /// <see cref="Adr0008CollectReadsAgreementTests.PerNodeKind_AgreementAndPinnedValues"/>, but
    /// per the brief's own steer this fixture uses the four kinds named there instead).</summary>
    [Test]
    public void DefaultArmKinds_HitCallShapedTrue()
    {
        var obj = new IRVariable("o", IntType);
        Assert.Multiple(() =>
        {
            PinHit("IRLoad", new IRLoad("t1", obj, IntType), true);
            PinHit("IRPhi", new IRPhi("t1", IntType), true);
            PinHit("IRGetElementPtr", new IRGetElementPtr("t1", obj, IntType), true);
            PinHit("IRArrayAlloc", new IRArrayAlloc("t1", IntType, 3), true);
        });
    }

    /// <summary>The negative control: a PURE operator over a plain declared local never sets
    /// <c>HitCallShaped</c> — proves the pins above are discriminating the call-shaped/default
    /// arms specifically, not merely confirming the field is always true.</summary>
    [Test]
    public void PureOperatorOverPlainLocal_HitCallShapedFalse()
    {
        var local = new IRVariable("x", IntType);
        PinHit("x + 1", new IRBinaryOp("t1", BinaryOpKind.Add, local, new IRConstant(1, IntType), IntType), false);
    }
}

/// <summary>
/// ADR-0008 D2 (task #156) — "Keep": <c>ReadsCallVisible</c> stays TRUE for every call-shaped
/// value, for both consumers (CSE and CopyPropagation). Unit pins per the ADR's own obligations
/// text, plus a CP1-family New/instance-method-call kill CopyPropagationSharedVocabularyTests does
/// NOT already cover (that file's CP-family probes are field/global/ByRef-aliasing shapes, none of
/// them a copy fact recorded from a <c>New</c> or an instance-method-call VALUE) — so this fixture
/// adds it rather than duplicating an existing pin.
/// </summary>
[TestFixture]
public class Adr0008D2Pins
{
    private static readonly TypeInfo IntType = new TypeInfo("Integer", TypeKind.Primitive);

    // ==================== ReadsCallVisible TRUE pins (the ADR's own obligations list) ============

    [Test]
    public void ReadsCallVisible_TrueForEveryCallShapedKind()
    {
        var f = new IRFunction("Work", IntType);
        var obj = new IRVariable("o", IntType);
        f.LocalVariables.Add(obj);
        Assert.Multiple(() =>
        {
            Assert.That(OptimizationPass.ReadsCallVisible(new IRCall("t1", "Foo", IntType), f), Is.True, "IRCall");
            Assert.That(OptimizationPass.ReadsCallVisible(new IRInstanceMethodCall("t1", obj, "M", IntType), f), Is.True, "IRInstanceMethodCall");
            Assert.That(OptimizationPass.ReadsCallVisible(new IRNewObject("t1", "Box", IntType), f), Is.True, "IRNewObject (allocation)");
            Assert.That(OptimizationPass.ReadsCallVisible(new IRFieldAccess("t1", obj, "F", IntType), f), Is.True, "IRFieldAccess");
            Assert.That(OptimizationPass.ReadsCallVisible(new IRIndexerAccess("t1", obj, IntType), f), Is.True, "IRIndexerAccess (indexer load)");
        });
    }

    // ==================== CP1-family: New / instance-method-call value, killed by a call =========

    /// <summary>Builds <c>x := t</c> where <c>t</c> is <paramref name="valueBuilder"/>'s call-shaped
    /// value (a <c>New Box()</c> allocation, or an instance-method call), runs
    /// <see cref="CopyPropagationPass"/>, and reports whether the later use's operand got rewritten
    /// from <c>x</c> to <c>t</c> (SURVIVED — stale) or still reads <c>x</c> (KILLED — correct).
    /// Mirrors <c>S/adr8/harness/Program.cs</c>'s <c>cp</c> mode <c>Case()</c> helper.</summary>
    private static bool FactSurvivesAnInterveningCall(Func<IRVariable, IRFunction, BasicBlock, IRValue> valueBuilder,
        bool interveningCall)
    {
        var m = new IRModule("M");
        var f = new IRFunction("Main", IntType);
        m.Functions.Add(f);
        var box = new TypeInfo("Box", TypeKind.Class);
        var x = new IRVariable("x", box);
        f.LocalVariables.Add(x);
        var e = f.CreateBlock("entry");

        var t = valueBuilder(x, f, e); // adds its own defining instruction(s) to `e`
        e.Instructions.Add(new IRAssignment(x, t)); // the fact: x := t
        if (interveningCall) e.Instructions.Add(new IRCall("", "Touch", IntType));
        var use = new IRBinaryOp("t9", BinaryOpKind.Add, x, new IRConstant(0, IntType), IntType);
        e.Instructions.Add(use);
        var show = new IRCall("", "Show", IntType);
        show.Arguments.Add(use);
        e.Instructions.Add(show);
        e.Instructions.Add(new IRReturn());

        new CopyPropagationPass().Run(m);
        return ReferenceEquals(use.Left, t);
    }

    [Test]
    public void CopyFactFromNewObject_KilledByAnInterveningCall()
    {
        bool survived = FactSurvivesAnInterveningCall((x, f, e) =>
        {
            var t = new IRNewObject("t0", "Box", x.Type);
            e.Instructions.Add(t);
            return t;
        }, interveningCall: true);
        Assert.That(survived, Is.False, "a New-object copy fact must be killed by an intervening call " +
            "(ReadsCallVisible(New Box()) is unconditionally true, so the call arm hits it on the FIRST call)");
    }

    [Test]
    public void CopyFactFromInstanceMethodCall_KilledByAnInterveningCall()
    {
        bool survived = FactSurvivesAnInterveningCall((x, f, e) =>
        {
            var receiver = new IRVariable("b", x.Type);
            f.LocalVariables.Add(receiver);
            var t = new IRInstanceMethodCall("t0", receiver, "Clone", x.Type);
            e.Instructions.Add(t);
            return t;
        }, interveningCall: true);
        Assert.That(survived, Is.False, "an instance-method-call copy fact must be killed by an intervening call");
    }

    /// <summary>The CONTROL: with NO intervening call, both facts above SURVIVE — proves the two
    /// pins above are catching a real kill, not an unconditional one.</summary>
    [Test]
    public void CopyFactFromNewObject_SurvivesWithNoInterveningCall_Control()
    {
        bool survived = FactSurvivesAnInterveningCall((x, f, e) =>
        {
            var t = new IRNewObject("t0", "Box", x.Type);
            e.Instructions.Add(t);
            return t;
        }, interveningCall: false);
        Assert.That(survived, Is.True, "control: with no intervening call the fact must survive");
    }

    // ==================== Settled point 4, plus the pre-existing gap this task found ==============

    /// <summary>Settled point 4 (the ruling's ASSUMPTION, measured true): a DIRECT STORE to any
    /// variable the fact's value reads kills the fact, not only a call. <c>t0 = a + 1</c>,
    /// <c>x := t0</c>, then <c>IRStore 99 -&gt; address a</c> (not an <c>IRAssignment</c> — the
    /// pointer-store shape) — the fact is killed. Ported from the harness's <c>cp</c> mode.</summary>
    [Test]
    public void SettledPoint4_DirectStoreThroughAnAddress_KillsTheFact()
    {
        var m = new IRModule("M");
        var f = new IRFunction("Main", IntType);
        m.Functions.Add(f);
        var a = new IRVariable("a", IntType);
        var x = new IRVariable("x", IntType);
        f.LocalVariables.Add(a);
        f.LocalVariables.Add(x);
        var e = f.CreateBlock("entry");
        var t = new IRBinaryOp("t0", BinaryOpKind.Add, a, new IRConstant(1, IntType), IntType);
        e.Instructions.Add(t);
        e.Instructions.Add(new IRAssignment(x, t));
        e.Instructions.Add(new IRStore(new IRConstant(99, IntType), a));
        var use = new IRBinaryOp("t9", BinaryOpKind.Add, x, new IRConstant(0, IntType), IntType);
        e.Instructions.Add(use);
        e.Instructions.Add(new IRReturn());

        new CopyPropagationPass().Run(m);
        Assert.That(ReferenceEquals(use.Left, t), Is.False, "an IRStore through a's address must kill x's fact");
    }

    /// <summary>
    /// ⚠ PRE-EXISTING GAP, FOUND BY ADR-0008's IMPLEMENTATION — NOT FIXED HERE (task #161).
    /// <c>u = a + 1</c> renamed <c>u</c>
    /// (<c>NamedAfterVariable</c>, a declared local), <c>t0 = u * 2</c>, <c>x := t0</c>. A DIRECT
    /// STORE to <c>u</c>'s own name (<c>u = 5</c>, an <c>IRAssignment</c> to the DECLARED local
    /// <c>u</c>) does NOT kill <c>x</c>'s fact — <c>CollectReads(t0)</c> correctly says <c>t0</c>
    /// reads storage named <c>u</c> (<c>CollectReads(t).Names</c> includes it — see
    /// <c>Adr0008CollectReadsAgreementTests</c>), but <c>CopyPropagationPass.Invalidate</c>'s
    /// redefinition kill (<c>Mentions</c>, <c>IROptimizer.cs</c>) walks the recorded VALUE
    /// structurally looking for an <see cref="IRVariable"/> named <c>u</c> — it has no case for "a
    /// named pure-operator INSTRUCTION whose destination happens to be named <c>u</c>", so it never
    /// finds it. MEASURED (this session, live): the fact SURVIVES. This is a genuine defect in
    /// <c>CopyPropagationPass</c>'s OWN kill rule (separate from ADR-0008's Guard/ReadsCallVisible
    /// walk, which gets this case right), pinned here as a KNOWN-WRONG regression test so a future
    /// fix turns it green rather than silently landing unnoticed. It blocks #118 (DCE) the same way
    /// settled point 4 does: DCE cannot safely remove <c>u</c>'s own defining instruction while a
    /// stale copy fact might still reference it.
    /// </summary>
    [Test]
    public void KnownGap_DirectStoreToANamedNestedOperandsOwnName_DoesNotKillTheFact()
    {
        var m = new IRModule("M");
        var f = new IRFunction("Main", IntType);
        m.Functions.Add(f);
        var a = new IRVariable("a", IntType);
        var x = new IRVariable("x", IntType);
        var uVar = new IRVariable("u", IntType);
        f.LocalVariables.Add(a);
        f.LocalVariables.Add(x);
        f.LocalVariables.Add(uVar);
        var e = f.CreateBlock("entry");
        var u = new IRBinaryOp("u", BinaryOpKind.Add, a, new IRConstant(1, IntType), IntType) { NamedAfterVariable = true };
        var t = new IRBinaryOp("t0", BinaryOpKind.Mul, u, new IRConstant(2, IntType), IntType);
        e.Instructions.Add(u);
        e.Instructions.Add(t);
        e.Instructions.Add(new IRAssignment(x, t));
        e.Instructions.Add(new IRAssignment(uVar, new IRConstant(5, IntType)));
        var use = new IRBinaryOp("t9", BinaryOpKind.Add, x, new IRConstant(0, IntType), IntType);
        e.Instructions.Add(use);
        e.Instructions.Add(new IRReturn());

        // Sanity: CollectReads (Guard/ReadsCallVisible's shared walk) DOES see `u` as a read of t.
        Assert.That(OptimizationPass.CollectReads(t).Names.Select(r => r.Name), Does.Contain("u"),
            "CollectReads must see 't0' as reading storage named 'u' — if this fails, the gap " +
            "pinned below has moved into ADR-0008's own walk, which would be a real regression");

        new CopyPropagationPass().Run(m);
        Assert.That(ReferenceEquals(use.Left, t), Is.True,
            "KNOWN GAP (pre-existing, not part of ADR-0008): CopyPropagationPass.Invalidate's Mentions() " +
            "does not recognise a store to a named nested operand's own name as redefining it, so the " +
            "fact wrongly SURVIVES. If this assertion starts failing, the gap has been fixed — update " +
            "this test to assert False and drop the KnownGap naming.");
    }
}
