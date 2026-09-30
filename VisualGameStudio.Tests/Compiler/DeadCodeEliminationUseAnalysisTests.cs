using System;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

// =====================================================================================
//  Task #118 — DeadCodeEliminationPass's use analysis becomes the shared, total
//  OptimizationPass.UsesOf walker, run FUNCTION-WIDE and descending nested operand trees.
//
//  Before this task, DeadCodeEliminationPass kept its own walker (`MarkUsed`), which knew ten
//  node kinds and was consulted one BLOCK at a time. It missed every operand of sixteen kinds
//  outright (IRGetElementPtr, IRCast, IRArrayStore, IRAwait, IRYield, IRIndexerAccess,
//  IRIndexerStore, IRForEach, IRThrow, IRNewObject, IRInstanceMethodCall, IRBaseMethodCall,
//  IRFieldAccess, IRFieldStore, IRTupleElement, IRPhi) and two more in part (IRCall's
//  CalleeValue; IRSwitch's case values and every pattern-case slot, including nested
//  Or/Tuple alternatives) — 33 individual operand slots across those 18 node kinds. It also
//  never looked inside an operand tree that is not itself in a block (a When guard hangs off
//  its IRSwitch), and a value defined in one block and used only in another read as unused.
//  Each of those is a LIVE value the old walker would let this pass DELETE, leaving its
//  consumer holding an instruction that is no longer in the function — a dangling operand.
//
//  ⛔ THE REMOVAL GUARD WAS LATENT WHEN #118 WROTE THESE, AND IS NOT ANY MORE. Task #163 (ADR-0017)
//  switched the instruction removal ON, licensed by a MARKER: `IRValue.IsCompilerTemp`, set only by
//  IRBuilder.MarkCompilerTemps. Until then the guard skipped every value whose name was non-empty
//  and did not start with `_tmp`, so no real program lost an instruction and the use analysis fixed
//  here showed only in hand-built IR. It still shows there — but a hand-built value reaches the
//  removal path only if it is MARKED, and these shapes did not carry the marker when #163 landed:
//  the three "unused value is removed" controls FAILED, and every "value is kept" test below kept
//  passing VACUOUSLY (their values were never removable, so a broken use walker left them green).
//  `Live` now marks its value, and `Run` asserts, before it runs the pass, that the value WOULD be
//  removed if nothing used it (DeadCodeEliminationPass.IsRemovableWhenUnused): a kept test is only
//  worth having if the value could have been removed. The controls each have a twin with the marker
//  absent (the value is kept), which is #163's own contract in the smallest possible form.
//
//  Every shape is ported one-for-one from the implementer's scratch harness
//  (S/t118/harness/Program.cs), cross-checked against its own before/after table (37 of 44
//  shapes WRONG at HEAD, 0 of 44 wrong after the fix) before being pinned here. Two fixtures:
//
//    DeadCodeEliminationUseAnalysisTests        — hand-built IR, fast tier (no front end, no
//                                                  backend, no process): every shape below.
//    The controls at the bottom of this file    — the guard itself: marked values go, unmarked
//                                                  ones stay whatever they are called.
// =====================================================================================

/// <summary>
/// Hand-built IR, ported one-for-one from the implementer's scratch harness
/// (<c>S/t118/harness/Program.cs</c>) — re-run live this session against the current, fixed
/// <c>BasicLang/IROptimizer.cs</c> (md5 <c>1d91aeabd61fdd88cbbe646fddca63cd</c>), 0 mismatches
/// against the harness's own before/after table.
///
/// <para>Every "missing arm" shape below builds ONE function with a single LIVE value <c>L</c> —
/// an <c>IRBinaryOp</c> named <c>_tmpL</c> and MARKED a compiler temp (<c>IsCompilerTemp</c>, ADR-0017:
/// the marker is the licence to delete, the spelling never is) — whose ONLY
/// consumer is one operand slot of the kind under test. <see cref="DeadCodeEliminationPass"/> is
/// run ONCE directly (no pipeline, no iteration) and the assertion is whether <c>L</c> is still
/// present in some block of the function afterward (KEPT) or not (REMOVED, which for a live
/// shape means its consumer is left holding a dangling operand — an instruction reachable from
/// nowhere else in the function). Every missing-arm, cross-block and operand-tree shape wants
/// KEPT; the control shapes state their own expectation, including REMOVED ones and the guard
/// pins (an unused value is deleted only if it is MARKED; a named <c>tN</c> value that no compiler
/// minted is never deleted, whatever it is called).</para>
/// </summary>
[TestFixture]
public class DeadCodeEliminationUseAnalysisTests
{
    private static readonly TypeInfo I = new("Integer", TypeKind.Primitive);
    private static readonly TypeInfo B = new("Boolean", TypeKind.Primitive);
    private static readonly TypeInfo D = new("Double", TypeKind.Primitive);
    private static readonly TypeInfo O = new("Object", TypeKind.Class);

    private static IRConstant C(int n) => new(n, I);

    private static (IRModule m, IRFunction f, BasicBlock e) NewFn()
    {
        var m = new IRModule("M");
        var f = new IRFunction("Main", I);
        m.Functions.Add(f);
        return (m, f, f.CreateBlock("entry"));
    }

    private static IRVariable Local(IRFunction f, string name, TypeInfo t = null)
    {
        var v = new IRVariable(name, t ?? I);
        f.LocalVariables.Add(v);
        return v;
    }

    /// <summary>The LIVE value under test: an <c>IRBinaryOp</c> named <c>_tmpL</c> by default, added to
    /// <paramref name="b"/>. ⭐ It is MARKED a compiler temp (<paramref name="marked"/>, the way IRBuilder
    /// does) so that it is a value DCE would delete if nothing used it: the name is irrelevant to that
    /// (ADR-0017), the marker is everything. An unmarked value is the negative twin.</summary>
    private static IRBinaryOp Live(IRFunction f, BasicBlock b, string name = "_tmpL", bool marked = true)
    {
        var x = Local(f, "x" + f.LocalVariables.Count);
        var v = new IRBinaryOp(name, BinaryOpKind.Add, x, C(1), I) { IsCompilerTemp = marked };
        b.AddInstruction(v);
        return v;
    }

    private static BasicBlock Ret(IRFunction f, string name)
    {
        var b = f.CreateBlock(name);
        b.AddInstruction(new IRReturn());
        return b;
    }

    private static bool InBlocks(IRFunction f, IRInstruction x)
        => f.Blocks.Any(b => b.Instructions.Any(i => ReferenceEquals(i, x)));

    /// <summary>Runs <see cref="DeadCodeEliminationPass"/> once over <paramref name="m"/> and
    /// reports whether <paramref name="l"/> is still present in some block of <paramref
    /// name="f"/> afterward.
    ///
    /// <para>⭐ <paramref name="armed"/> (the default) first asserts that <paramref name="l"/> WOULD be
    /// deleted if nothing used it (<see cref="DeadCodeEliminationPass.IsRemovableWhenUnused"/>): the
    /// marker, the kind and the kill vocabulary all allow it. Without this a "value is kept" test
    /// passes for any reason at all — and 40 of them did, vacuously, the moment #163 made removal
    /// depend on a marker these hand-built values did not carry. The negative twins pass
    /// <c>armed: false</c>: their value is unmarked ON PURPOSE.</para></summary>
    private static bool Run(IRModule m, IRFunction f, IRValue l, bool armed = true)
    {
        if (armed)
            Assert.That(DeadCodeEliminationPass.IsRemovableWhenUnused(l, f), Is.True,
                "VACUITY GUARD: this test's value could not be deleted even if nothing used it (unmarked, or of a kind outside the licence), " +
                "so 'it was kept' proves nothing about the use analysis.");
        new DeadCodeEliminationPass().Run(m);
        return InBlocks(f, l);
    }

    /// <summary>The "same block" shape family: <c>L</c>'s only consumer is <c>use(f, L)</c>,
    /// appended to the entry block, followed by a bare <c>Return</c>.</summary>
    private static bool RunSameBlock(Func<IRFunction, IRValue, IRInstruction> use)
    {
        var (m, f, e) = NewFn();
        var l = Live(f, e);
        e.AddInstruction(use(f, l));
        e.AddInstruction(new IRReturn());
        return Run(m, f, l);
    }

    /// <summary>The "switch" shape family: <c>L</c>'s only consumer is configured onto an
    /// <see cref="IRSwitch"/> terminator in the entry block (a case value or a pattern-case
    /// slot), which branches to a default block and a case block, both returning.</summary>
    private static bool RunSwitch(Action<IRSwitch, IRValue, BasicBlock> configure)
    {
        var (m, f, e) = NewFn();
        var l = Live(f, e);
        var def = Ret(f, "default");
        var cb = Ret(f, "case1");
        var sw = new IRSwitch(Local(f, "sel"), def);
        configure(sw, l, cb);
        e.AddInstruction(sw);
        return Run(m, f, l);
    }

    // =================================================================================
    //  33 missing-arm slots — 16 node kinds MarkUsed missed outright, plus IRCall and
    //  IRSwitch, which it covered only in part. One test per operand slot.
    // =================================================================================

    // ---- IRGetElementPtr (an element pointer's base and each index) ----

    [Test]
    public void Gep_BasePointer_OnlyUseIsAGetElementPtrsBase_Kept()
    {
        var kept = RunSameBlock((f, l) => new IRGetElementPtr("k", l, I));
        Assert.That(kept, Is.True, "IRGetElementPtr.BasePointer is a use; MarkUsed never looked at it.");
    }

    [Test]
    public void Gep_Index_OnlyUseIsAGetElementPtrsIndex_Kept()
    {
        var kept = RunSameBlock((f, l) =>
        {
            var g = new IRGetElementPtr("k", Local(f, "arr", O), I);
            g.Indices.Add(l);
            return g;
        });
        Assert.That(kept, Is.True, "IRGetElementPtr.Indices are uses; MarkUsed never looked at them.");
    }

    // ---- IRCast (the value being cast) ----

    [Test]
    public void Cast_Value_OnlyUseIsACastsValue_Kept()
    {
        var kept = RunSameBlock((f, l) => new IRCast("k", l, I, D, CastKind.SIToFP));
        Assert.That(kept, Is.True, "IRCast.Value is a use; MarkUsed had no IRCast arm at all.");
    }

    // ---- IRArrayStore (array, index, value) ----

    [Test]
    public void ArrayStore_Array_OnlyUseIsAnArrayStoresArrayOperand_Kept()
    {
        var kept = RunSameBlock((f, l) => new IRArrayStore(l, C(0), C(1)));
        Assert.That(kept, Is.True, "IRArrayStore.Array is a use; MarkUsed had no IRArrayStore arm at all.");
    }

    [Test]
    public void ArrayStore_Index_OnlyUseIsAnArrayStoresIndexOperand_Kept()
    {
        var kept = RunSameBlock((f, l) => new IRArrayStore(Local(f, "arr", O), l, C(1)));
        Assert.That(kept, Is.True, "IRArrayStore.Index is a use; MarkUsed had no IRArrayStore arm at all.");
    }

    [Test]
    public void ArrayStore_Value_OnlyUseIsAnArrayStoresValueOperand_Kept()
    {
        var kept = RunSameBlock((f, l) => new IRArrayStore(Local(f, "arr", O), C(0), l));
        Assert.That(kept, Is.True, "IRArrayStore.Value is a use; MarkUsed had no IRArrayStore arm at all.");
    }

    // ---- IRAwait (the awaited expression) ----

    [Test]
    public void Await_Expression_OnlyUseIsAnAwaitsExpression_Kept()
    {
        var kept = RunSameBlock((f, l) => new IRAwait("k", l, I));
        Assert.That(kept, Is.True, "IRAwait.Expression is a use; MarkUsed had no IRAwait arm at all.");
    }

    // ---- IRYield (the yielded value) ----

    [Test]
    public void Yield_Value_OnlyUseIsAYieldsValue_Kept()
    {
        var kept = RunSameBlock((f, l) => new IRYield(l));
        Assert.That(kept, Is.True, "IRYield.Value is a use; MarkUsed had no IRYield arm at all.");
    }

    // ---- IRIndexerAccess (collection, each index) ----

    [Test]
    public void IndexerAccess_Collection_OnlyUseIsAnIndexerLoadsCollection_Kept()
    {
        var kept = RunSameBlock((f, l) => new IRIndexerAccess("k", l, I));
        Assert.That(kept, Is.True, "IRIndexerAccess.Collection is a use; MarkUsed had no IRIndexerAccess arm at all.");
    }

    [Test]
    public void IndexerAccess_Index_OnlyUseIsAnIndexerLoadsIndex_Kept()
    {
        var kept = RunSameBlock((f, l) =>
        {
            var a = new IRIndexerAccess("k", Local(f, "lst", O), I);
            a.Indices.Add(l);
            return a;
        });
        Assert.That(kept, Is.True, "IRIndexerAccess.Indices are uses; MarkUsed had no IRIndexerAccess arm at all.");
    }

    // ---- IRIndexerStore (collection, each index, value) ----

    [Test]
    public void IndexerStore_Collection_OnlyUseIsAnIndexerStoresCollection_Kept()
    {
        var kept = RunSameBlock((f, l) =>
        {
            var s = new IRIndexerStore(l, C(1));
            s.Indices.Add(C(0));
            return s;
        });
        Assert.That(kept, Is.True, "IRIndexerStore.Collection is a use; MarkUsed had no IRIndexerStore arm at all.");
    }

    [Test]
    public void IndexerStore_Index_OnlyUseIsAnIndexerStoresIndex_Kept()
    {
        var kept = RunSameBlock((f, l) =>
        {
            var s = new IRIndexerStore(Local(f, "lst", O), C(1));
            s.Indices.Add(l);
            return s;
        });
        Assert.That(kept, Is.True, "IRIndexerStore.Indices are uses; MarkUsed had no IRIndexerStore arm at all.");
    }

    [Test]
    public void IndexerStore_Value_OnlyUseIsAnIndexerStoresValue_Kept()
    {
        var kept = RunSameBlock((f, l) =>
        {
            var s = new IRIndexerStore(Local(f, "lst", O), l);
            s.Indices.Add(C(0));
            return s;
        });
        Assert.That(kept, Is.True, "IRIndexerStore.Value is a use; MarkUsed had no IRIndexerStore arm at all.");
    }

    // ---- IRForEach (the collection being iterated) ----

    [Test]
    public void ForEach_Collection_OnlyUseIsAForEachsCollection_Kept()
    {
        var (m, f, e) = NewFn();
        var l = Live(f, e);
        var body = f.CreateBlock("body");
        var end = Ret(f, "end");
        body.AddInstruction(new IRBranch(end));
        e.AddInstruction(new IRForEach("item", I, l, body, end));
        Assert.That(Run(m, f, l), Is.True, "IRForEach.Collection is a use; MarkUsed had no IRForEach arm at all.");
    }

    // ---- IRThrow (the thrown exception) ----

    [Test]
    public void Throw_Exception_OnlyUseIsAThrowsException_Kept()
    {
        var kept = RunSameBlock((f, l) => new IRThrow(l));
        Assert.That(kept, Is.True, "IRThrow.Exception is a use; MarkUsed had no IRThrow arm at all.");
    }

    // ---- IRNewObject (an allocation's argument) ----

    [Test]
    public void NewObject_Argument_OnlyUseIsAnAllocationsArgument_Kept()
    {
        var kept = RunSameBlock((f, l) =>
        {
            var n = new IRNewObject("k", "C", O);
            n.Arguments.Add(l);
            return n;
        });
        Assert.That(kept, Is.True, "IRNewObject.Arguments are uses; MarkUsed had no IRNewObject arm at all.");
    }

    // ---- IRInstanceMethodCall (object, argument) ----

    [Test]
    public void InstanceMethodCall_Object_OnlyUseIsAnInstanceCallsObject_Kept()
    {
        var kept = RunSameBlock((f, l) => new IRInstanceMethodCall("k", l, "M", I));
        Assert.That(kept, Is.True, "IRInstanceMethodCall.Object is a use; MarkUsed had no IRInstanceMethodCall arm at all.");
    }

    [Test]
    public void InstanceMethodCall_Argument_OnlyUseIsAnInstanceCallsArgument_Kept()
    {
        var kept = RunSameBlock((f, l) =>
        {
            var c = new IRInstanceMethodCall("k", Local(f, "o", O), "M", I);
            c.Arguments.Add(l);
            c.ByRefArguments.Add(false);
            return c;
        });
        Assert.That(kept, Is.True, "IRInstanceMethodCall.Arguments are uses; MarkUsed had no IRInstanceMethodCall arm at all.");
    }

    // ---- IRBaseMethodCall (argument) ----

    [Test]
    public void BaseMethodCall_Argument_OnlyUseIsABaseCallsArgument_Kept()
    {
        var kept = RunSameBlock((f, l) =>
        {
            var c = new IRBaseMethodCall("k", "M", I);
            c.Arguments.Add(l);
            return c;
        });
        Assert.That(kept, Is.True, "IRBaseMethodCall.Arguments are uses; MarkUsed had no IRBaseMethodCall arm at all.");
    }

    // ---- IRFieldAccess (object) ----

    [Test]
    public void FieldAccess_Object_OnlyUseIsAFieldAccessesObject_Kept()
    {
        var kept = RunSameBlock((f, l) => new IRFieldAccess("k", l, "F", I));
        Assert.That(kept, Is.True, "IRFieldAccess.Object is a use; MarkUsed had no IRFieldAccess arm at all.");
    }

    // ---- IRFieldStore (object, value) ----

    [Test]
    public void FieldStore_Object_OnlyUseIsAFieldStoresObject_Kept()
    {
        var kept = RunSameBlock((f, l) => new IRFieldStore(l, "F", C(1)));
        Assert.That(kept, Is.True, "IRFieldStore.Object is a use; MarkUsed had no IRFieldStore arm at all.");
    }

    [Test]
    public void FieldStore_Value_OnlyUseIsAFieldStoresValue_Kept()
    {
        var kept = RunSameBlock((f, l) => new IRFieldStore(Local(f, "o", O), "F", l));
        Assert.That(kept, Is.True, "IRFieldStore.Value is a use; MarkUsed had no IRFieldStore arm at all.");
    }

    // ---- IRTupleElement (the tuple) ----

    [Test]
    public void TupleElement_Tuple_OnlyUseIsATupleElementsTuple_Kept()
    {
        var kept = RunSameBlock((f, l) => new IRTupleElement(l, 0, I));
        Assert.That(kept, Is.True, "IRTupleElement.Tuple is a use; MarkUsed had no IRTupleElement arm at all.");
    }

    // ---- IRPhi (an operand) ----

    [Test]
    public void Phi_Operand_OnlyUseIsAPhisOperand_Kept()
    {
        var kept = RunSameBlock((f, l) =>
        {
            var p = new IRPhi("k", I);
            p.Operands.Add((l, f.Blocks[0]));
            return p;
        });
        Assert.That(kept, Is.True, "IRPhi.Operands are uses; MarkUsed had no IRPhi arm at all.");
    }

    // ---- IRCall, covered only in part (CalleeValue; Arguments was already known) ----

    [Test]
    public void Call_CalleeValue_OnlyUseIsACallsCalleeValue_Kept()
    {
        var kept = RunSameBlock((f, l) => new IRCall("k", "", I) { CalleeValue = l });
        Assert.That(kept, Is.True, "IRCall.CalleeValue is a use; MarkUsed's IRCall arm only knew Arguments.");
    }

    // ---- IRSwitch, covered only in part (Value was already known; case values and every ----
    // ---- pattern-case slot, including nested Or/Tuple alternatives, were missed) ----

    [Test]
    public void Switch_CaseValue_OnlyUseIsASwitchsCaseValue_Kept()
    {
        var kept = RunSwitch((sw, l, cb) => sw.Cases.Add((l, cb)));
        Assert.That(kept, Is.True, "IRSwitch.Cases' CaseValue is a use; MarkUsed's IRSwitch arm only knew Value.");
    }

    [Test]
    public void Switch_Pattern_WhenGuard_OnlyUseIsAPatternCasesWhenGuard_Kept()
    {
        var kept = RunSwitch((sw, l, cb) => sw.PatternCases.Add(new IRBindingPatternCase(cb) { WhenGuard = l }));
        Assert.That(kept, Is.True, "IRPatternCase.WhenGuard is a use; MarkUsed never looked at pattern cases at all.");
    }

    [Test]
    public void Switch_Pattern_RangeLowerBound_OnlyUseIsARangePatternsLowerBound_Kept()
    {
        var kept = RunSwitch((sw, l, cb) => sw.PatternCases.Add(new IRRangePatternCase(l, C(10), cb)));
        Assert.That(kept, Is.True, "IRRangePatternCase.LowerBound is a use; MarkUsed never looked at pattern cases at all.");
    }

    [Test]
    public void Switch_Pattern_RangeUpperBound_OnlyUseIsARangePatternsUpperBound_Kept()
    {
        var kept = RunSwitch((sw, l, cb) => sw.PatternCases.Add(new IRRangePatternCase(C(0), l, cb)));
        Assert.That(kept, Is.True, "IRRangePatternCase.UpperBound is a use; MarkUsed never looked at pattern cases at all.");
    }

    [Test]
    public void Switch_Pattern_ComparisonValue_OnlyUseIsAComparisonPatternsValue_Kept()
    {
        var kept = RunSwitch((sw, l, cb) => sw.PatternCases.Add(new IRComparisonPatternCase(">", l, cb)));
        Assert.That(kept, Is.True, "IRComparisonPatternCase.CompareValue is a use; MarkUsed never looked at pattern cases at all.");
    }

    [Test]
    public void Switch_Pattern_ConstantValue_OnlyUseIsAConstantPatternsValue_Kept()
    {
        var kept = RunSwitch((sw, l, cb) => sw.PatternCases.Add(new IRConstantPatternCase(l, cb)));
        Assert.That(kept, Is.True, "IRConstantPatternCase.Value is a use; MarkUsed never looked at pattern cases at all.");
    }

    [Test]
    public void Switch_Pattern_OrAlternative_OnlyUseIsAnOrPatternsAlternative_Kept()
    {
        var kept = RunSwitch((sw, l, cb) =>
        {
            var or = new IROrPatternCase(cb);
            or.Alternatives.Add(new IRConstantPatternCase(l, cb));
            sw.PatternCases.Add(or);
        });
        Assert.That(kept, Is.True, "an IROrPatternCase alternative's Value is a use, nested two levels " +
            "into PatternCases; MarkUsed never looked at pattern cases at all.");
    }

    [Test]
    public void Switch_Pattern_TupleElement_OnlyUseIsATuplePatternsElement_Kept()
    {
        var kept = RunSwitch((sw, l, cb) =>
        {
            var tp = new IRTuplePatternCase(cb);
            tp.Elements.Add(new IRConstantPatternCase(l, cb));
            sw.PatternCases.Add(tp);
        });
        Assert.That(kept, Is.True, "an IRTuplePatternCase element's Value is a use, nested two levels " +
            "into PatternCases; MarkUsed never looked at pattern cases at all.");
    }

    // =================================================================================
    //  Cross-block: L is defined in one block and used only in ANOTHER — the old walker
    //  built its `used` set per block, so a use in a different block read as unused.
    // =================================================================================

    [Test]
    public void CrossBlock_UsedOnlyInAnotherBlocksReturn_Kept()
    {
        var (m, f, e) = NewFn();
        var l = Live(f, e);
        var b2 = f.CreateBlock("b2");
        e.AddInstruction(new IRBranch(b2));
        b2.AddInstruction(new IRReturn(l));
        Assert.That(Run(m, f, l), Is.True,
            "L is defined in entry and used only in b2's Return; the old per-block `used` set never saw it.");
    }

    [Test]
    public void CrossBlock_UsedOnlyInALoopBody_Kept()
    {
        // entry: L ; br head   head: acc = acc + L (named after acc) ; cbr c, head, exit   exit: ret
        var (m, f, e) = NewFn();
        var l = Live(f, e);
        var head = f.CreateBlock("head");
        var exit = Ret(f, "exit");
        e.AddInstruction(new IRBranch(head));
        var acc = Local(f, "acc");
        head.AddInstruction(new IRBinaryOp("acc", BinaryOpKind.Add, acc, l, I) { NamedAfterVariable = true });
        head.AddInstruction(new IRConditionalBranch(Local(f, "c", B), head, exit));
        Assert.That(Run(m, f, l), Is.True,
            "L is defined in entry and used only in head's loop body; the old per-block `used` set never saw it.");
    }

    // =================================================================================
    //  Operand trees: L's only consumer is an instruction that is not ITSELF in any block
    //  (it hangs off a block instruction's operand tree) — the old walker never descended.
    // =================================================================================

    [Test]
    public void OperandTree_ReachableOnlyOffAReturn_Kept()
    {
        var (m, f, e) = NewFn();
        var l = Live(f, e);
        var tree = new IRBinaryOp("k", BinaryOpKind.Mul, l, C(2), I); // not itself in any block
        e.AddInstruction(new IRReturn(tree));
        Assert.That(Run(m, f, l), Is.True,
            "L's only consumer, tree, is a binary op reachable only through Return.Value, not itself " +
            "an instruction in any block; the old walker never descended into an operand tree.");
    }

    [Test]
    public void OperandTree_WhenGuardTree_Kept()
    {
        var kept = RunSwitch((sw, l, cb) =>
            sw.PatternCases.Add(new IRBindingPatternCase(cb) { WhenGuard = new IRCompare("k", CompareKind.Gt, l, C(0), B) }));
        Assert.That(kept, Is.True,
            "L's only consumer is a compare that is itself only reachable through the pattern case's " +
            "WhenGuard, not an instruction in any block; the old walker never descended into an operand tree.");
    }

    // =================================================================================
    //  Controls: shapes that already worked, or that establish what DCE removes. Since #163
    //  (ADR-0017) a value is removed only if it is MARKED a compiler temp; every control that
    //  says "removed" marks its value, and has a NEGATIVE TWIN that leaves it unmarked and
    //  says "kept". The three "removed" controls are the ones #163 moved (they hand-built
    //  `_tmp`-named and nameless values that carried no marker, and failed).
    // =================================================================================

    [Test]
    public void Control_UnusedTempNamedValue_Removed()
    {
        var (m, f, e) = NewFn();
        var l = Live(f, e, "_tmpD");
        e.AddInstruction(new IRReturn());
        Assert.That(Run(m, f, l), Is.False,
            "a genuinely unused MARKED pure value must be removed — whatever it is called.");
    }

    /// <summary>The negative twin, and #163's fix in one line: the value is spelled `_tmp` — the old guard's
    /// licence — and is not a compiler temp, so it is user storage or unknown and stays. `Dim _tmp1 = a + b`
    /// printed 0 for VB's 67 in 12 of 12 cells before this.</summary>
    [Test]
    public void Control_UnusedTempNamedValue_NotMarked_Kept()
    {
        var (m, f, e) = NewFn();
        var l = Live(f, e, "_tmpD", marked: false);
        e.AddInstruction(new IRReturn());
        Assert.That(Run(m, f, l, armed: false), Is.True,
            "an unused value spelled `_tmp…` that no compiler minted must NOT be removed (the spelling is never the licence).");
    }

    [Test]
    public void Control_UnusedNamelessValue_Removed()
    {
        var (m, f, e) = NewFn();
        var l = Live(f, e, "");
        e.AddInstruction(new IRReturn());
        Assert.That(Run(m, f, l), Is.False,
            "a genuinely unused nameless MARKED pure value must be removed.");
    }

    [Test]
    public void Control_UnusedNamelessValue_NotMarked_Kept()
    {
        var (m, f, e) = NewFn();
        var l = Live(f, e, "", marked: false);
        e.AddInstruction(new IRReturn());
        Assert.That(Run(m, f, l, armed: false), Is.True,
            "a nameless value that no compiler minted is not removed: namelessness is not the licence either.");
    }

    /// <summary>The unary op, the compare and the load — the three other removable kinds beside IRBinaryOp.
    /// All three are checked (the pre-#163 control asserted on the load alone).</summary>
    private static (IRModule m, IRFunction f, IRValue[] values) UnaryCompareLoad(bool marked)
    {
        var (m, f, e) = NewFn();
        var u = new IRUnaryOp("_tmpU", UnaryOpKind.Neg, Local(f, "a"), I) { IsCompilerTemp = marked };
        var c = new IRCompare("_tmpC", CompareKind.Lt, Local(f, "b"), C(0), B) { IsCompilerTemp = marked };
        var ld = new IRLoad("_tmpLd", Local(f, "p"), I) { IsCompilerTemp = marked };
        e.AddInstruction(u);
        e.AddInstruction(c);
        e.AddInstruction(ld);
        e.AddInstruction(new IRReturn());
        return (m, f, new IRValue[] { u, c, ld });
    }

    [Test]
    public void Control_UnusedUnaryCompareLoad_Removed()
    {
        var (m, f, values) = UnaryCompareLoad(marked: true);
        Assert.That(values.Select(v => DeadCodeEliminationPass.IsRemovableWhenUnused(v, f)), Has.All.True, "precondition: all three are licensed");
        new DeadCodeEliminationPass().Run(m);
        Assert.That(values.Select(v => InBlocks(f, v)), Has.All.False,
            "an unused MARKED unary op, compare and load (the other three removable kinds, alongside " +
            "IRBinaryOp) must be removed when genuinely unused.");
    }

    [Test]
    public void Control_UnusedUnaryCompareLoad_NotMarked_Kept()
    {
        var (m, f, values) = UnaryCompareLoad(marked: false);
        new DeadCodeEliminationPass().Run(m);
        Assert.That(values.Select(v => InBlocks(f, v)), Has.All.True,
            "the same three values, unmarked, are all kept: the kind alone is not the licence.");
    }

    /// <summary>THE GUARD PIN, restated for #163. It used to say: a value named `t0` that is unused is not removed,
    /// "because the removal guard only considers `_tmp`", and "this test is the one that must fail, deliberately, the
    /// day that decision ships". It shipped (ADR-0017) and this still passes — for the OPPOSITE reason: the name
    /// `t0` is IRBuilder's own spelling for a temp, and it is STILL not the licence. A value spelled `t0` that nothing
    /// minted (a user's `Dim t0`, hand-built IR) is kept; the marked twin below is removed.</summary>
    [Test]
    public void GuardPin_UnusedValueNamedT0_NotMarked_Kept_TheSpellingIsNeverTheLicence()
    {
        var (m, f, e) = NewFn();
        var l = Live(f, e, "t0", marked: false);
        e.AddInstruction(new IRReturn());
        Assert.That(Run(m, f, l, armed: false), Is.True,
            "a genuinely unused value named 't0' that no compiler minted must NOT be removed. IRBuilder's temps are named " +
            "t0, t1, … but so may a user's variables be; #118 measured 37 of 62 spelling-guard removals as user variables.");
    }

    [Test]
    public void GuardPin_UnusedValueNamedT0_Marked_Removed()
    {
        var (m, f, e) = NewFn();
        var l = Live(f, e, "t0");
        e.AddInstruction(new IRReturn());
        Assert.That(Run(m, f, l), Is.False, "the same value, MARKED, is a compiler temp and goes: the marker decides, in either direction.");
    }

    [Test]
    public void Control_UnusedT0InAnotherBlock_NotMarked_Kept()
    {
        var (m, f, e) = NewFn();
        var b2 = f.CreateBlock("b2");
        e.AddInstruction(new IRBranch(b2));
        var l = Live(f, b2, "t0", marked: false);
        b2.AddInstruction(new IRReturn());
        Assert.That(Run(m, f, l, armed: false), Is.True,
            "same guard pin, in a non-entry block — the marker, not block placement, is what keeps it.");
    }

    [Test]
    public void Control_UnusedT0InAnotherBlock_Marked_Removed()
    {
        var (m, f, e) = NewFn();
        var b2 = f.CreateBlock("b2");
        e.AddInstruction(new IRBranch(b2));
        var l = Live(f, b2, "t0");
        b2.AddInstruction(new IRReturn());
        Assert.That(Run(m, f, l), Is.False, "removal is not confined to the entry block");
    }

    [Test]
    public void Control_KnownArmSameBlock_Kept()
    {
        var (m, f, e) = NewFn();
        var l = Live(f, e);
        e.AddInstruction(new IRReturn(l));
        Assert.That(Run(m, f, l), Is.True,
            "L used by Return.Value in the same block — a kind MarkUsed already knew, still correct.");
    }

    [Test]
    public void Control_DeadChainOneRun_Kept()
    {
        // _tmpA used only by dead _tmpB: one run removes _tmpB and keeps _tmpA (the pipeline's
        // next iteration would take it, but DeadCodeEliminationPass.Run is called only ONCE here).
        var (m, f, e) = NewFn();
        var a = Live(f, e, "_tmpA");
        var b = new IRBinaryOp("_tmpB", BinaryOpKind.Mul, a, C(2), I) { IsCompilerTemp = true };
        e.AddInstruction(b);
        e.AddInstruction(new IRReturn());
        Assert.That(Run(m, f, a), Is.True,
            "one Run() only removes the dead chain's outermost link (_tmpB); _tmpA is used by " +
            "_tmpB at the moment `used` is computed, so it survives this single run.");
        Assert.That(InBlocks(f, b), Is.False, "and the outermost link (_tmpB, marked like _tmpA) really did go: the chain is live, not vacuous");
    }
}
