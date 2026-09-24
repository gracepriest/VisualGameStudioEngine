using System;
using NUnit.Framework;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Direct, hand-built-IR unit tests for <see cref="IRReplicability.IsReplicable(IRValue, System.Func{IRValue, bool?})"/> —
/// ADR-0004 D2's whitelist. No compile, no run: every case constructs the IR node(s) itself and
/// asks the predicate, the way <c>OperandWalkerTotalityTests</c> probes a walker directly rather
/// than through a program.
///
/// <para>⛔ <b>Structural, whitelist-only, default FALSE</b> is the entire contract: a wrong "yes"
/// here is a silent miscompile (C#'s materialisation gate consumes this directly —
/// <c>CSharpBackend.ComputeMaterialisedTemps</c>/<c>IsReplicableHere</c>), so every case not
/// explicitly whitelisted below must come back <c>false</c>, including a node kind this file has
/// never heard of.</para>
/// </summary>
[TestFixture]
public class IRReplicabilityTests
{
    private static readonly TypeInfo IntType = new TypeInfo("Integer", TypeKind.Primitive);
    private static readonly TypeInfo VoidType = new TypeInfo("Void", TypeKind.Primitive);

    private static IRVariable Local(string name = "loc") => new IRVariable(name, IntType);
    private static IRVariable Parameter(string name = "p") => new IRVariable(name, IntType) { IsParameter = true };
    private static IRVariable Me(string name = "Me") => new IRVariable(name, IntType) { IsParameter = true };
    private static IRVariable ConstGlobal(string name = "G") =>
        new IRVariable(name, IntType) { IsGlobal = true, IsConst = true };
    private static IRVariable NonConstGlobal(string name = "G") =>
        new IRVariable(name, IntType) { IsGlobal = true, IsConst = false };
    private static IRVariable ByRefParameter(string name = "r") =>
        new IRVariable(name, IntType) { IsParameter = true, IsByRef = true };
    private static IRConstant Const(int v = 3) => new IRConstant(v, IntType);
    private static IRCall Call(string name = "t") => new IRCall(name, "Tag", IntType);

    // ====================================================================================
    // The whitelist: true.
    // ====================================================================================

    [Test]
    public void IRConstant_IsReplicable()
        => Assert.That(IRReplicability.IsReplicable(Const()), Is.True);

    [Test]
    public void ALocalVariable_IsReplicable()
        => Assert.That(IRReplicability.IsReplicable(Local()), Is.True);

    [Test]
    public void AParameter_IsReplicable()
        => Assert.That(IRReplicability.IsReplicable(Parameter()), Is.True);

    [Test]
    public void Me_IsReplicable()
        // Me is carried as an ordinary IRVariable(IsParameter=true) — there is no separate
        // "IsMe" flag in the IR, so this pins the SAME arm as AParameter_IsReplicable rather
        // than a distinct one; recorded because the contract names Me explicitly.
        => Assert.That(IRReplicability.IsReplicable(Me()), Is.True);

    [Test]
    public void AConstGlobal_IsReplicable()
        => Assert.That(IRReplicability.IsReplicable(ConstGlobal()), Is.True);

    // ====================================================================================
    // The whitelist: false — never replicable.
    // ====================================================================================

    [Test]
    public void ANonConstGlobal_IsNotReplicable()
        => Assert.That(IRReplicability.IsReplicable(NonConstGlobal()), Is.False);

    [Test]
    public void AByRefParameter_IsNotReplicable()
        => Assert.That(IRReplicability.IsReplicable(ByRefParameter()), Is.False);

    [Test]
    public void ByRefTakesPrecedenceOverParameter()
        // A ByRef parameter is still a parameter; the ByRef check must be consulted, not
        // shadowed by "it's a parameter, so true". IsByRef=true on IsParameter=true is exactly
        // the shape a careless reordering of the switch's guards would get backwards.
        => Assert.That(IRReplicability.IsReplicable(
            new IRVariable("r", IntType) { IsParameter = true, IsByRef = true }), Is.False);

    [Test]
    public void ACall_IsNotReplicable()
        => Assert.That(IRReplicability.IsReplicable(Call()), Is.False);

    [Test]
    public void AnInstanceMethodCall_IsNotReplicable()
        => Assert.That(IRReplicability.IsReplicable(
            new IRInstanceMethodCall("t", Me(), "Foo", IntType)), Is.False);

    [Test]
    public void AFieldLoad_IsNotReplicable()
        => Assert.That(IRReplicability.IsReplicable(
            new IRFieldAccess("t", Me(), "_field", IntType)), Is.False);

    [Test]
    public void AnIndexerLoad_IsNotReplicable()
        => Assert.That(IRReplicability.IsReplicable(
            new IRIndexerAccess("t", Local("list"), IntType)), Is.False);

    [Test]
    public void AnAllocation_IsNotReplicable()
        => Assert.That(IRReplicability.IsReplicable(
            new IRArrayAlloc("t", IntType, 4)), Is.False);

    [Test]
    public void AnUnrecognisedKind_IsNotReplicable()
        // IRAlloca and IRGetElementPtr are IRValue kinds IRReplicability's switch has no arm
        // for at all — the default case, which the contract requires to answer false rather
        // than throw or guess true.
        => Assert.Multiple(() =>
        {
            Assert.That(IRReplicability.IsReplicable(new IRAlloca("t", IntType)), Is.False, "IRAlloca");
            Assert.That(IRReplicability.IsReplicable(
                new IRGetElementPtr("t", Local(), IntType)), Is.False, "IRGetElementPtr");
        });

    [Test]
    public void Null_IsNotReplicable()
        => Assert.That(IRReplicability.IsReplicable(null), Is.False);

    // ====================================================================================
    // Binary/Unary/Compare/Cast recurse: one non-replicable leaf anywhere makes the whole
    // expression non-replicable.
    // ====================================================================================

    [Test]
    public void Binary_BothOperandsReplicable_IsReplicable()
        => Assert.That(IRReplicability.IsReplicable(
            new IRBinaryOp("t", BinaryOpKind.Add, Const(1), Local(), IntType)), Is.True);

    [Test]
    public void Binary_LeftIsACall_IsNotReplicable()
        => Assert.That(IRReplicability.IsReplicable(
            new IRBinaryOp("t", BinaryOpKind.Add, Call(), Const(1), IntType)), Is.False);

    [Test]
    public void Binary_RightIsACall_IsNotReplicable()
        => Assert.That(IRReplicability.IsReplicable(
            new IRBinaryOp("t", BinaryOpKind.Add, Const(1), Call(), IntType)), Is.False);

    [Test]
    public void Binary_NonReplicableLeafBuriedTwoLevelsDeep_IsNotReplicable()
        // (1 + (Tag() + 2)) — the call is not a direct operand of the outer Add at all, but
        // recursion must still find it and fail the whole expression.
        => Assert.That(IRReplicability.IsReplicable(
            new IRBinaryOp("outer", BinaryOpKind.Add, Const(1),
                new IRBinaryOp("inner", BinaryOpKind.Add, Call(), Const(2), IntType), IntType)),
            Is.False);

    [Test]
    public void Unary_ReplicableOperand_IsReplicable()
        => Assert.That(IRReplicability.IsReplicable(
            new IRUnaryOp("t", UnaryOpKind.Neg, Local(), IntType)), Is.True);

    [Test]
    public void Unary_NonReplicableOperand_IsNotReplicable()
        => Assert.That(IRReplicability.IsReplicable(
            new IRUnaryOp("t", UnaryOpKind.Neg, Call(), IntType)), Is.False);

    [Test]
    public void Compare_BothOperandsReplicable_IsReplicable()
        => Assert.That(IRReplicability.IsReplicable(
            new IRCompare("t", CompareKind.Lt, Local(), Const(), IntType)), Is.True);

    [Test]
    public void Compare_OneOperandNotReplicable_IsNotReplicable()
        => Assert.That(IRReplicability.IsReplicable(
            new IRCompare("t", CompareKind.Lt, Local(), Call(), IntType)), Is.False);

    [Test]
    public void Cast_ReplicableValue_IsReplicable()
        => Assert.That(IRReplicability.IsReplicable(
            new IRCast("t", Local(), IntType, IntType, CastKind.Bitcast)), Is.True);

    [Test]
    public void Cast_NonReplicableValue_IsNotReplicable()
        => Assert.That(IRReplicability.IsReplicable(
            new IRCast("t", Call(), IntType, IntType, CastKind.Bitcast)), Is.False);

    // ====================================================================================
    // The consumer-override hook.
    // ====================================================================================

    [Test]
    public void ConsumerOverride_IsConsultedForTheRootValue()
        // The structural rule for a call is false; the hook says true for THIS call — the hook
        // wins, because it is consulted before the switch (the CSharpBackend use: an
        // already-materialised call IS a local, hence replicable).
        => Assert.That(IRReplicability.IsReplicable(Call(), _ => true), Is.True);

    [Test]
    public void ConsumerOverride_CanForceFalseEvenForAStructurallyReplicableLeaf()
        => Assert.That(IRReplicability.IsReplicable(Local(), _ => false), Is.False);

    [Test]
    public void ConsumerOverride_IsConsultedForEveryOperandRecursively_NotJustTheRoot()
        // 1 + Tag() would structurally be false (Tag() is a call); the hook answers true for
        // the Tag() node specifically (and null — "no opinion" — for everything else), so the
        // whole expression comes back true. This is the "hook precedence for EVERY node
        // visited" contract, not just the value passed in directly.
        => Assert.That(IRReplicability.IsReplicable(
            new IRBinaryOp("t", BinaryOpKind.Add, Const(1), Call("theCall"), IntType),
            v => (v is IRCall c && c.Name == "theCall") ? true : (bool?)null),
            Is.True);

    [Test]
    public void ConsumerOverride_NullAnswerFallsThroughToTheStructuralRule()
        // A hook that answers null for EVERY node is the same as no hook at all.
        => Assert.That(IRReplicability.IsReplicable(Call(), _ => (bool?)null), Is.False);

    [Test]
    public void ConsumerOverride_CannotWidenWhatTheRulesCallPure_UnlessItAnswersForThatNode()
        // The hook answers ONLY for a specific IRCall instance (by reference); a DIFFERENT
        // call, buried as an operand of the same expression, gets no opinion from the hook and
        // must fall through to the structural rule (false) — the hook does not turn "impure" as
        // a CONCEPT into "pure", it only ever answers for the exact node it was asked about.
        => Assert.Multiple(() =>
        {
            var materialisedCall = Call("materialised");
            var otherCall = Call("other");

            bool? OnlyForMaterialised(IRValue v) =>
                ReferenceEquals(v, materialisedCall) ? true : (bool?)null;

            Assert.That(IRReplicability.IsReplicable(materialisedCall, OnlyForMaterialised), Is.True,
                "the node the hook actually answers for");

            Assert.That(IRReplicability.IsReplicable(
                    new IRBinaryOp("t", BinaryOpKind.Add, materialisedCall, otherCall, IntType),
                    OnlyForMaterialised),
                Is.False,
                "otherCall gets no opinion from the hook and is structurally a call: false, "
                + "and false on any operand makes the whole binary op false");
        });
}
