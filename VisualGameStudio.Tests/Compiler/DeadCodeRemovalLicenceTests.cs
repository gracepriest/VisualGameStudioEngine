using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using TypeInfo = BasicLang.Compiler.SemanticAnalysis.TypeInfo;
using TypeKind = BasicLang.Compiler.SemanticAnalysis.TypeKind;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// ⭐ ADR-0017 D2 and D3 (#163), on HAND-BUILT IR: the removal licence, kind by kind, and what a
/// deletion may never leave behind. Fast tier — no front end, no backend, no process.
///
/// <para>The licence is: <b>marked</b> (<c>IsCompilerTemp &amp;&amp; !NamedAfterVariable</c>) ∧ <b>a pure,
/// non-trapping kind</b> ∧ the kill vocabulary agrees ∧ <b>unused</b>, by identity and by identity ONLY (ADR-0018
/// D4 removed ADR-0017's "and no variable spells its name" keep) ∧ <b>settled point 3</b> holds. Each clause has a test that removes it and watches the
/// value survive, and a twin that holds the clause and watches the value go — because a test that
/// only says "kept" passes vacuously the moment the value stops being removable for an unrelated
/// reason. That is exactly how the three moved pins in
/// <see cref="DeadCodeEliminationUseAnalysisTests"/> went stale.</para>
///
/// <para>Three of the implementer's mutants are MASKED on real IR by a second check, and can only be
/// killed by an isolated hand-built shape: <b>M7</b> (a division removable: on real IR the divide's
/// operands are variables, so only the kind guard stops it — killed on both tiers), <b>M9</b> (`++`
/// removable: the kill vocabulary also refuses, because `++a` writes `a`) and <b>M10</b> (an element
/// load removable: settled point 3 also refuses, because the element pointer would lose its only
/// use). <see cref="TheKindGuard_AloneStopsTheDivisionsTheIncrementsAndTheElementReads"/> builds the
/// shape where the OTHER checks pass and asserts that, so the kind guard is the only thing standing.</para>
/// </summary>
[TestFixture]
public class DeadCodeRemovalLicenceTests
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

    private static IRVariable V(IRFunction f, string name, TypeInfo t = null)
    {
        var existing = f.LocalVariables.FirstOrDefault(v => v.Name == name);
        if (existing != null) return existing;
        var v = new IRVariable(name, t ?? I);
        f.LocalVariables.Add(v);
        return v;
    }

    /// <summary>Marks <paramref name="value"/> the way IRBuilder would: a compiler temp.</summary>
    private static T Temp<T>(T value) where T : IRValue
    {
        value.IsCompilerTemp = true;
        return value;
    }

    private static T Add<T>(BasicBlock block, T inst) where T : IRInstruction
    {
        block.AddInstruction(inst);
        return inst;
    }

    private static bool InBlocks(IRFunction f, IRInstruction x)
        => f.Blocks.Any(b => b.Instructions.Any(i => ReferenceEquals(i, x)));

    private static TestCaseData Row(string name, Func<IRFunction, BasicBlock, IRInstruction> build)
        => new TestCaseData(build).SetName(name);

    // ============================================================================================
    // The licence, kind by kind
    // ============================================================================================

    private static IEnumerable<TestCaseData> Removable()
    {
        foreach (var op in Enum.GetValues<BinaryOpKind>().Where(k => k is not (BinaryOpKind.Div or BinaryOpKind.Mod or BinaryOpKind.IntDiv)))
            yield return Row($"Binary_{op}", (f, e) => Temp(Add(e, new IRBinaryOp("t0", op, V(f, "a"), C(2), I))));

        foreach (var op in new[] { UnaryOpKind.Neg, UnaryOpKind.Not, UnaryOpKind.BitwiseNot, UnaryOpKind.AddressOf })
            yield return Row($"Unary_{op}", (f, e) => Temp(Add(e, new IRUnaryOp("t0", op, V(f, "a"), I))));

        foreach (var kind in Enum.GetValues<CompareKind>())
            yield return Row($"Compare_{kind}", (f, e) => Temp(Add(e, new IRCompare("t0", kind, V(f, "a"), C(3), B))));

        yield return Row("IdentityCompare_Is", (f, e) => Temp(Add(e, new IRIdentityCompare("t0", V(f, "o", O), V(f, "p", O), false, B))));
        yield return Row("IdentityCompare_IsNot", (f, e) => Temp(Add(e, new IRIdentityCompare("t0", V(f, "o", O), V(f, "p", O), true, B))));

        yield return Row("Load_OfALocalVariable", (f, e) => Temp(Add(e, new IRLoad("t0", V(f, "p"), I))));

        // A load of an alloca slot. The slot is a non-replicable operand, so settled point 3 wants it to keep
        // two uses after the load goes: two stores give it those.
        yield return Row("Load_OfAnAllocaSlot", (f, e) =>
        {
            var slot = Add(e, new IRAlloca("slot", I));
            Add(e, new IRStore(C(1), slot));
            Add(e, new IRStore(C(2), slot));
            return Temp(Add(e, new IRLoad("t0", slot, I)));
        });
    }

    /// <summary>Every marked, unused value of a pure kind goes. The twin of every "kept" row below: this is
    /// what proves the marker, the kind and the kill vocabulary all pass for these shapes.</summary>
    [TestCaseSource(nameof(Removable))]
    public void EveryPureKind_MarkedAndUnused_IsRemoved(Func<IRFunction, BasicBlock, IRInstruction> build)
    {
        var (m, f, e) = NewFn();
        var value = (IRValue)build(f, e);
        Add(e, new IRReturn());

        Assert.That(DeadCodeEliminationPass.IsRemovableWhenUnused(value, f), Is.True, "the licence says this kind is removable");
        new DeadCodeEliminationPass().Run(m);

        Assert.That(InBlocks(f, value), Is.False, "a marked, unused, pure value must be deleted");
    }

    private static IEnumerable<TestCaseData> NotLicensed()
    {
        // The trapping divisions (M7). The kind guard is what stops them; see the isolation test below.
        foreach (var op in new[] { BinaryOpKind.Div, BinaryOpKind.IntDiv, BinaryOpKind.Mod })
            yield return Row($"Binary_{op}", (f, e) => Temp(Add(e, new IRBinaryOp("t0", op, V(f, "a"), V(f, "z"), I))));

        // `++` and `--` write their operand (M9).
        foreach (var op in new[] { UnaryOpKind.Inc, UnaryOpKind.Dec })
            yield return Row($"Unary_{op}", (f, e) => Temp(Add(e, new IRUnaryOp("t0", op, V(f, "a"), I))));

        // ⭐ Calls are never removed (M3): a marked call result with no use is still a call.
        yield return Row("Call", (f, e) => Temp(Add(e, new IRCall("t0", "F", I))));
        yield return Row("InstanceMethodCall", (f, e) => Temp(Add(e, new IRInstanceMethodCall("t0", V(f, "o", O), "M", I))));
        yield return Row("BaseMethodCall", (f, e) => Temp(Add(e, new IRBaseMethodCall("t0", "M", I))));
        yield return Row("NewObject", (f, e) => Temp(Add(e, new IRNewObject("t0", "C", O))));
        yield return Row("Await", (f, e) => Temp(Add(e, new IRAwait("t0", V(f, "task", O), I))));

        // Kinds the ADR does not license: a member read may run a getter, a cast may throw, an element pointer
        // is address arithmetic the backends fold into a load.
        yield return Row("FieldAccess", (f, e) => Temp(Add(e, new IRFieldAccess("t0", V(f, "o", O), "F", I))));
        yield return Row("Cast", (f, e) => Temp(Add(e, new IRCast("t0", V(f, "a"), I, D, CastKind.SIToFP))));
        yield return Row("IndexerAccess", (f, e) => Temp(Add(e, new IRIndexerAccess("t0", V(f, "lst", O), I))));
        yield return Row("GetElementPtr", (f, e) => Temp(Add(e, new IRGetElementPtr("t0", V(f, "arr", O), I))));
        yield return Row("Alloca", (f, e) => Temp(Add(e, new IRAlloca("t0", I))));

        // Stores, throws, yields, the base-constructor call: instructions, not values, so no marker can reach them.
        yield return Row("Store", (f, e) => Add(e, new IRStore(C(1), V(f, "p"))));
        yield return Row("ArrayStore", (f, e) => Add(e, new IRArrayStore(V(f, "arr", O), C(0), C(1))));
        yield return Row("FieldStore", (f, e) => Add(e, new IRFieldStore(V(f, "o", O), "F", C(1))));
        yield return Row("Throw", (f, e) => Add(e, new IRThrow(V(f, "ex", O))));
        yield return Row("Yield", (f, e) => Add(e, new IRYield(C(1))));
        yield return Row("BaseConstructorCall", (f, e) => Add(e, new IRBaseConstructorCall(new IRValue[] { V(f, "p") })));
    }

    [TestCaseSource(nameof(NotLicensed))]
    public void EveryKindOutsideTheLicence_EvenMarkedAndUnused_IsKept(Func<IRFunction, BasicBlock, IRInstruction> build)
    {
        var (m, f, e) = NewFn();
        var inst = build(f, e);
        Add(e, new IRReturn());

        if (inst is IRValue value)
        {
            Assert.That(value.IsCompilerTemp, Is.True, "precondition: the value IS marked, so only its kind can keep it");
            Assert.That(DeadCodeEliminationPass.IsRemovableWhenUnused(value, f), Is.False, "the licence refuses this kind");
        }
        new DeadCodeEliminationPass().Run(m);

        Assert.That(InBlocks(f, inst), Is.True, "a call, a store, a throw, an await, the base-constructor call and every other kind outside the licence is never deleted");
    }

    /// <summary>
    /// ⭐ M7, M9 and M10. The shapes where the kind guard is the ONLY defence: the kill vocabulary names nothing but
    /// the value's own name, and settled point 3 has nothing to refuse. The test asserts that first, so a future
    /// second defence cannot silently turn it into a vacuous pass.
    /// </summary>
    private static IEnumerable<TestCaseData> KindGuardOnly()
    {
        foreach (var op in new[] { BinaryOpKind.Div, BinaryOpKind.IntDiv, BinaryOpKind.Mod })
            yield return Row($"Binary_{op}_overVariables", (f, e) => Temp(Add(e, new IRBinaryOp("t0", op, V(f, "a"), V(f, "z"), I))));

        // `++` over an operand that is NOT a variable: the kill vocabulary then writes no name of its own beyond the
        // result's, so it agrees with removal. (Over a variable it writes that variable and refuses by itself.)
        foreach (var op in new[] { UnaryOpKind.Inc, UnaryOpKind.Dec })
            yield return Row($"Unary_{op}_overAnElement", (f, e) =>
                Temp(Add(e, new IRUnaryOp("t0", op, new IRCast("c", V(f, "a"), I, D, CastKind.SIToFP), I))));

        // An element read: the pointer has TWO other uses, so removing the load leaves it two, and settled point 3
        // lets it go. (With one use, settled point 3 refuses on its own: that is why M10 shows no output change.)
        yield return Row("Load_throughAnElementPointer", (f, e) =>
        {
            var pointer = new IRGetElementPtr("g", V(f, "arr", O), I);
            pointer.Indices.Add(C(0));
            Add(e, pointer);
            Add(e, new IRStore(C(1), pointer));
            Add(e, new IRStore(C(2), pointer));
            return Temp(Add(e, new IRLoad("t0", pointer, I)));
        });
    }

    [TestCaseSource(nameof(KindGuardOnly))]
    public void TheKindGuard_AloneStopsTheDivisionsTheIncrementsAndTheElementReads(Func<IRFunction, BasicBlock, IRInstruction> build)
    {
        var (m, f, e) = NewFn();
        var value = (IRValue)build(f, e);
        Add(e, new IRReturn());

        var writes = OptimizationPass.NamesWrittenBy(value, f);
        Assert.Multiple(() =>
        {
            Assert.That(value.IsCompilerTemp && !value.NamedAfterVariable, Is.True, "marked, and not user storage");
            Assert.That(writes.IsClassified && !writes.IsUniversal && !writes.IsCall, Is.True, "the kill vocabulary classifies it as no call");
            Assert.That(writes.Names.All(n => n == value.Name), Is.True,
                "isolation: the kill vocabulary writes nothing but the value's own name, so IT does not refuse: " + writes);
            Assert.That(DeadCodeEliminationPass.KeepsOperandMaterialisation(value, RecordingDeadCodePass.UseCounts(f)), Is.True,
                "isolation: settled point 3 has nothing to refuse either");
        });
        Assert.That(DeadCodeEliminationPass.IsRemovableWhenUnused(value, f), Is.False, "the kind guard refuses it");

        new DeadCodeEliminationPass().Run(m);
        Assert.That(InBlocks(f, value), Is.True, "a division, a `++`/`--` and an element read can trap or write: never deleted");
    }

    // ============================================================================================
    // The marker is the licence — never the spelling (M1, M4b)
    // ============================================================================================

    [TestCase("t5")]
    [TestCase("T5")]
    [TestCase("t0")]
    [TestCase("_tmp1")]
    [TestCase("_t3")]
    [TestCase("_t0")]
    [TestCase("_tmpL")]
    [TestCase("")]
    [TestCase("x")]
    public void AnUnmarkedValue_IsKept_WhateverItsName(string name)
    {
        var (m, f, e) = NewFn();
        var value = Add(e, new IRBinaryOp(name, BinaryOpKind.Add, V(f, "a"), C(1), I));
        Add(e, new IRReturn());

        new DeadCodeEliminationPass().Run(m);

        Assert.That(InBlocks(f, value), Is.True,
            $"a value spelled '{name}' that no compiler minted is user storage or unknown: never deleted. " +
            "(#118 measured 37 of 62 spelling-guard removals as user variables; the pre-#163 `_tmp` guard deleted `Dim _tmp1`.)");
    }

    [TestCase("weird")]
    [TestCase("x")]
    [TestCase("")]
    [TestCase("t5")]
    [TestCase("_tmp1")]
    public void AMarkedValue_IsRemoved_WhateverItsName(string name)
    {
        var (m, f, e) = NewFn();
        var value = Temp(Add(e, new IRBinaryOp(name, BinaryOpKind.Add, V(f, "a"), C(1), I)));
        Add(e, new IRReturn());

        new DeadCodeEliminationPass().Run(m);

        Assert.That(InBlocks(f, value), Is.False, $"the flag, not the spelling '{name}', is the licence");
    }

    /// <summary>D2 re-checks <c>NamedAfterVariable</c> although D1 already excludes it at the source: a flag wrongly
    /// set on user storage must not be enough to delete a store (M4b).</summary>
    [Test]
    public void AMarkedValue_ThatIsNamedAfterAVariable_IsKept()
    {
        var (m, f, e) = NewFn();
        var value = Add(e, new IRBinaryOp("x", BinaryOpKind.Add, V(f, "a"), C(1), I) { IsCompilerTemp = true, NamedAfterVariable = true });
        Add(e, new IRReturn());

        Assert.That(DeadCodeEliminationPass.IsRemovableWhenUnused(value, f), Is.False);
        new DeadCodeEliminationPass().Run(m);

        Assert.That(InBlocks(f, value), Is.True, "`Dim x = a + 1` is ONE binop renamed x; its reads are reads of the variable, so it has no operand use — and it is the variable");
    }

    // ============================================================================================
    // "Unused" is by IDENTITY only — ADR-0018 D4 removed ADR-0017's by-name keep (was M8)
    // ============================================================================================

    /// <summary>
    /// ⭐ The keep is gone. ADR-0017 kept an unused marked temp when an <c>IRVariable</c> operand SPELLED its name (`t0`, any case),
    /// because a user variable IRBuilder did not reserve (a Catch, For Each, pattern or LINQ variable) could share the name and the
    /// C++ counter then collided with it. IRBuilder reserves every such name now, so the minter never hands one out, and the rule
    /// had no witness left (ADR-0018 D4, measured: 0 collision cells with the keep and 0 without it). DCE decides by identity: a
    /// variable spelled like the temp does not keep it, in either case, and neither does any other name. The three rows are the
    /// old rule's three, flipped; <b>reinstating the keep fails the first two.</b>
    /// </summary>
    [TestCase("t0", TestName = "ByIdentity_AVariableSpellingTheTempsName_DoesNotKeepIt")]
    [TestCase("T0", TestName = "ByIdentity_TheSpellingIsCaseInsensitive_SoItDoesNotKeepItEither")]
    [TestCase("k", TestName = "ByIdentity_ADifferentName_KeepsNothing")]
    public void AVariableThatSpellsATempsName_DoesNotKeepTheTemp(string variableName)
    {
        var (m, f, e) = NewFn();
        var temp = Temp(Add(e, new IRBinaryOp("t0", BinaryOpKind.Add, V(f, "a"), C(1), I)));
        Add(e, new IRReturn(new IRVariable(variableName, I)));

        Assert.That(DeadCodeEliminationPass.IsRemovableWhenUnused(temp, f), Is.True, "precondition: the licence alone deletes it");
        new DeadCodeEliminationPass().Run(m);

        Assert.That(InBlocks(f, temp), Is.False,
            $"the temp is unused by identity, and a variable spelled '{variableName}' is not the temp: nothing about a NAME keeps a value (ADR-0018 D4)");
    }

    /// <summary>The variable need not be a direct operand of a block instruction: the old rule's descent through operand trees found
    /// it. It finds nothing now, and the temp goes all the same.</summary>
    [Test]
    public void ByIdentity_AVariableInsideAnOperandTree_DoesNotKeepTheTemp()
    {
        var (m, f, e) = NewFn();
        var temp = Temp(Add(e, new IRBinaryOp("t0", BinaryOpKind.Add, V(f, "a"), C(1), I)));
        var tree = new IRBinaryOp("s", BinaryOpKind.Add, new IRVariable("t0", I), C(2), I); // in no block
        Add(e, new IRReturn(tree));

        new DeadCodeEliminationPass().Run(m);

        Assert.That(InBlocks(f, temp), Is.False);
    }

    /// <summary>The twin of the two above, so "removed" cannot pass because the temp had become removable for an unrelated reason: the
    /// SAME temp, used by identity — directly by the return, or as an operand inside a tree that lives in no block — is kept.</summary>
    [TestCase(false, TestName = "ByIdentity_ATempTheReturnUses_IsKept")]
    [TestCase(true, TestName = "ByIdentity_ATempInsideAnOperandTree_IsKept")]
    public void ByIdentity_AUsedTemp_IsKept(bool insideATree)
    {
        var (m, f, e) = NewFn();
        var temp = Temp(Add(e, new IRBinaryOp("t0", BinaryOpKind.Add, V(f, "a"), C(1), I)));
        IRValue used = insideATree ? new IRBinaryOp("s", BinaryOpKind.Add, temp, C(2), I) : temp; // the tree is in no block
        Add(e, new IRReturn(used));

        new DeadCodeEliminationPass().Run(m);

        Assert.That(InBlocks(f, temp), Is.True, "a value some instruction uses, by identity, is never deleted");
    }

    /// <summary>
    /// ⭐ The shape the keep used to hold is now a VERIFIER refusal (ADR-0018 D4, Invariant T). It was: an unused, marked, pure temp
    /// under the name of a variable the function's declarations own (ADR-0017's `CT_wbr_t0`: a Catch variable `t0`). With every such
    /// name reserved, a minted temp never carries one; if a future leak lets it, the verifier NAMES the temp instead of a keep
    /// hiding the leak as a wrong answer. Either spelling refuses (the reservation ignores case), and the temp is no longer held.
    /// </summary>
    [TestCase("t0", TestName = "TheShapeTheKeepUsedToHold_IsAVerifierRefusal")]
    [TestCase("T0", TestName = "TheShapeTheKeepUsedToHold_IsAVerifierRefusal_WhateverTheCase")]
    public void ACompilerTempUnderAReservedName_IsRefusedByTheVerifier_AndNoLongerKept(string reservedSpelling)
    {
        var (m, f, e) = NewFn();
        f.Reserve(reservedSpelling); // what IRBuilder does for a `Catch t0` / `For Each t0`
        var temp = Temp(Add(e, new IRBinaryOp("t0", BinaryOpKind.Add, V(f, "a"), C(1), I)));
        Add(e, new IRReturn());

        var violations = IRVerifier.CheckInvariantT(m);
        new DeadCodeEliminationPass().Run(m);

        Assert.Multiple(() =>
        {
            Assert.That(violations, Has.Count.EqualTo(1), string.Join(" | ", violations));
            Assert.That(violations[0].Invariant, Is.EqualTo("T"));
            Assert.That(violations[0].Function, Is.EqualTo("Main"));
            Assert.That(violations[0].Variable, Is.EqualTo("t0"));
            Assert.That(violations[0].Value, Is.SameAs(temp));
            Assert.That(InBlocks(f, temp), Is.False, "the refusal replaced the keep: DCE deletes an unused marked temp whatever its name");
        });
    }

    /// <summary>The twin: the same temp with NO reservation under its name is not a violation. Invariant T is about a name the
    /// program owns, never about a name's shape.</summary>
    [Test]
    public void ACompilerTemp_UnderANameNobodyReserved_IsNotAViolation()
    {
        var (m, f, e) = NewFn();
        f.Reserve("k");
        Temp(Add(e, new IRBinaryOp("t0", BinaryOpKind.Add, V(f, "a"), C(1), I)));
        Add(e, new IRReturn());

        Assert.That(IRVerifier.CheckInvariantT(m), Is.Empty);
    }

    // ============================================================================================
    // ⭐ M5 — an operand of IRBaseConstructorCall is a use (the #170 interaction)
    // ============================================================================================

    [Test]
    public void ATemp_WhoseOnlyUseIsTheBaseConstructorCall_IsKept_AndTheCallToo()
    {
        var (m, f, e) = NewFn();
        var temp = Temp(Add(e, new IRBinaryOp("t0", BinaryOpKind.Add, V(f, "p"), C(1), I)));
        var call = Add(e, new IRBaseConstructorCall(new IRValue[] { temp }));
        Add(e, new IRReturn());

        Assert.That(DeadCodeEliminationPass.IsRemovableWhenUnused(temp, f), Is.True, "precondition: only the use analysis can keep it");
        new DeadCodeEliminationPass().Run(m);

        Assert.Multiple(() =>
        {
            Assert.That(InBlocks(f, temp), Is.True, "`MyBase.New(p + 1)`: deleting the temp leaves the base call with an undefined operand");
            Assert.That(InBlocks(f, call), Is.True, "the base call runs user code: never deleted");
        });
    }

    [Test]
    public void TheSameTemp_WithoutTheBaseCall_IsRemoved()
    {
        var (m, f, e) = NewFn();
        var temp = Temp(Add(e, new IRBinaryOp("t0", BinaryOpKind.Add, V(f, "p"), C(1), I)));
        Add(e, new IRBaseConstructorCall(Array.Empty<IRValue>()));
        Add(e, new IRReturn());

        new DeadCodeEliminationPass().Run(m);

        Assert.That(InBlocks(f, temp), Is.False, "the twin: the base call's USE is what kept it");
    }

    // ============================================================================================
    // ⭐ M6 — ADR-0008 settled point 3, in the pass (D3)
    // ============================================================================================

    private static IRValue Call(IRFunction f, BasicBlock e, string result, string callee, params IRValue[] args)
    {
        var call = new IRCall(result, callee, I);
        foreach (var a in args) { call.Arguments.Add(a); call.ByRefArguments.Add(false); }
        return Temp(Add(e, call));
    }

    /// <summary>
    /// The candidate <c>m = c * 0</c> (dead), with <c>c</c> the result of a call — a NON-replicable operand.
    /// The C# backend declares a local for such a value only when it has more than one use, and inlines it at its
    /// one use otherwise. Deleting <c>m</c> would therefore change where <c>c</c> is evaluated.
    /// </summary>
    private static (IRModule m, IRFunction f, IRValue candidate) CallOperandShape(int otherUses, bool callBetween)
    {
        var (m, f, e) = NewFn();
        var c = Call(f, e, "c", "F");
        var candidate = Temp(Add(e, new IRBinaryOp("m", BinaryOpKind.Mul, c, C(0), I)));
        if (callBetween) Call(f, e, "g", "G");
        for (var i = 0; i < otherUses; i++) Call(f, e, "s" + i, "Show", c);
        Add(e, new IRReturn());
        return (m, f, candidate);
    }

    [TestCase(0, false, false, TestName = "SettledPoint3_OneUseToNone_Refused")]
    [TestCase(1, false, false, TestName = "SettledPoint3_TwoUsesToOne_Adjacent_Refused")]
    [TestCase(1, true, false, TestName = "SettledPoint3_TwoUsesToOne_NotAdjacent_Refused_R5sShape")]
    [TestCase(2, false, true, TestName = "SettledPoint3_ThreeUsesToTwo_Allowed")]
    [TestCase(3, true, true, TestName = "SettledPoint3_FourUsesToThree_Allowed")]
    public void ADeadConsumer_OfANonReplicableOperand_IsRemovedOnlyIfTheOperandKeepsTwoUses(int otherUses, bool callBetween, bool removed)
    {
        var (m, f, candidate) = CallOperandShape(otherUses, callBetween);

        Assert.That(DeadCodeEliminationPass.IsRemovableWhenUnused(candidate, f), Is.True,
            "precondition: kind, marker and kill vocabulary all allow it, so settled point 3 alone decides");
        new DeadCodeEliminationPass().Run(m);

        Assert.That(InBlocks(f, candidate), Is.EqualTo(!removed),
            removed
                ? "the operand keeps at least two uses, so the C# materialisation rule is unchanged: the dead consumer goes"
                : "removing it would leave the call single-use (evaluated at that one use, away from its definition) or unused (emitted as a statement)");
    }

    [Test]
    public void ADeadConsumer_OfAReplicableOperand_IsRemoved_WhateverItsUseCount()
    {
        var (m, f, e) = NewFn();
        var x = Temp(Add(e, new IRBinaryOp("x", BinaryOpKind.Add, V(f, "a"), V(f, "b"), I))); // replicable: pure over variables
        var candidate = Temp(Add(e, new IRBinaryOp("m", BinaryOpKind.Mul, x, C(0), I)));
        Add(e, new IRReturn());

        new DeadCodeEliminationPass().Run(m);

        Assert.That(InBlocks(f, candidate), Is.False, "a replicable operand is re-evaluated at every use anyway: nothing to preserve");
    }

    private static IEnumerable<TestCaseData> MaterialisationCases()
    {
        // (name, the operand, how many operand slots hold it in the function, how many the candidate holds, keeps?)
        yield return new TestCaseData(new IRCall("c", "F", I), 1, 1, false).SetName("Unit_CallOperand_OneUse_Refused");
        yield return new TestCaseData(new IRCall("c", "F", I), 2, 1, false).SetName("Unit_CallOperand_TwoUses_Refused");
        yield return new TestCaseData(new IRCall("c", "F", I), 3, 1, true).SetName("Unit_CallOperand_ThreeUses_Allowed");
        yield return new TestCaseData(new IRCall("c", "F", I), 3, 2, false).SetName("Unit_CallOperandUsedTwiceByTheCandidate_ThreeUses_Refused");
        yield return new TestCaseData(new IRCall("c", "F", I), 4, 2, true).SetName("Unit_CallOperandUsedTwiceByTheCandidate_FourUses_Allowed");
        yield return new TestCaseData(new IRLoad("l", new IRVariable("p", I), I), 1, 1, false).SetName("Unit_LoadOperand_OneUse_Refused");
        yield return new TestCaseData(new IRBinaryOp("x", BinaryOpKind.Add, new IRVariable("a", I), new IRVariable("b", I), I), 1, 1, true).SetName("Unit_ReplicableOperand_Allowed");
        yield return new TestCaseData(new IRVariable("v", I), 1, 1, true).SetName("Unit_VariableOperand_Allowed");
        yield return new TestCaseData(new IRConstant(7, I), 1, 1, true).SetName("Unit_ConstantOperand_Allowed");
    }

    [TestCaseSource(nameof(MaterialisationCases))]
    public void KeepsOperandMaterialisation_CountsOperandSlots(IRValue operand, int totalSlots, int candidateSlots, bool keeps)
    {
        var candidate = candidateSlots == 1
            ? new IRBinaryOp("m", BinaryOpKind.Mul, operand, new IRConstant(0, I), I)
            : new IRBinaryOp("m", BinaryOpKind.Mul, operand, operand, I);
        var counts = new Dictionary<IRValue, int>(ReferenceEqualityComparer.Instance) { [operand] = totalSlots };

        Assert.That(DeadCodeEliminationPass.KeepsOperandMaterialisation(candidate, counts), Is.EqualTo(keeps));
    }

    [Test]
    public void KeepsOperandMaterialisation_AnOperandItCannotCount_IsRefused_NeverAssumedFine()
    {
        var c = new IRCall("c", "F", I);
        var candidate = new IRBinaryOp("m", BinaryOpKind.Mul, c, new IRConstant(0, I), I);

        Assert.That(DeadCodeEliminationPass.KeepsOperandMaterialisation(candidate, new Dictionary<IRValue, int>()), Is.False,
            "unknown counts only ever make the pass refuse MORE (a use in a tree that lives in no block is not counted)");
        Assert.That(DeadCodeEliminationPass.KeepsOperandMaterialisation(candidate, null), Is.False);
    }

    /// <summary>A run updates its counts as it deletes: two dead consumers of a call that has THREE uses cannot both
    /// go — the first takes it to two, the second would take it to one.</summary>
    [Test]
    public void OneRun_UpdatesTheCountsAsItDeletes()
    {
        var (m, f, e) = NewFn();
        var c = Call(f, e, "c", "F");
        var first = Temp(Add(e, new IRBinaryOp("m1", BinaryOpKind.Mul, c, C(0), I)));
        var second = Temp(Add(e, new IRBinaryOp("m2", BinaryOpKind.Mul, c, C(0), I)));
        Call(f, e, "s", "Show", c);
        Add(e, new IRReturn());

        new DeadCodeEliminationPass().Run(m);

        Assert.Multiple(() =>
        {
            Assert.That(InBlocks(f, second), Is.False, "the later consumer goes first (the pass walks each block backwards): three uses to two");
            Assert.That(InBlocks(f, first), Is.True, "the earlier consumer would take the call from two uses to one, so it is refused");
        });
    }
}
