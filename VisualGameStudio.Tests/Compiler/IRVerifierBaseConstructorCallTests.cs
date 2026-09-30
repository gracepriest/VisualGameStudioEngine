using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.SemanticAnalysis;
using TypeInfo = BasicLang.Compiler.SemanticAnalysis.TypeInfo;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// ADR-0016 D5 — the verifier invariant (<c>IRVerifier.CheckInvariantP</c>):
/// <list type="bullet">
/// <item><b>(a)</b> def-before-use for <c>IRBaseConstructorCall</c>'s operands;</item>
/// <item><b>(b)</b> at most one per function, only in a constructor, only in the prologue region
/// (the entry block, or the blocks an <c>AndAlso</c>/<c>OrElse</c> argument spreads it over);</item>
/// <item><b>(c)</b> D1's prologue invariants — expression-only, and CLOSED (no prologue value used after
/// the call);</item>
/// <item><b>(d)</b> every IR lambda is referenced by exactly one lambda-creation operand — no orphan,
/// which is what #170 was: a lambda in <c>IRConstructor.BaseConstructorArgs</c>, a list no block held.</item>
/// </list>
///
/// <para>The falsifier (ADR-0016's own falsifier 7): mutate a REAL, freshly-built module — delete an
/// operand's definition, orphan a lambda, move a use of a prologue value past the call, add a second
/// call — and require the verifier to FAIL each, naming the clause; and require the clean shapes to
/// PASS, on IRBuilder's output and after the standard and the aggressive pipeline. A verifier that
/// never fires is exactly the "green build" this whole task started from, so the negative half is as
/// load-bearing as the positive.</para>
///
/// <para>⚠ Invariant P runs on UN-LOWERED IR only: ClosureLowering's output legitimately holds
/// environment stores before the call (D3), so <c>VerifyAfterOptimization(lowered: true)</c> skips it.
/// That is tested too, so a "fix" that runs it on lowered IR fails here instead of on MSIL.</para>
/// </summary>
[TestFixture]
[NonParallelizable] // IRVerifier.Mode is process-wide static state
public class IRVerifierBaseConstructorCallTests
{
    // ============================================================================================
    // The shapes: one per way the prologue can look
    // ============================================================================================

    /// <summary>(name, source): a lambda (B5), a computed value (W1), a call (W2), an AndAlso — a
    /// MULTI-BLOCK prologue —, the barrier probe (E06), and an array literal (a store INTO an array the
    /// prologue allocated, which (c) must admit).</summary>
    private static IEnumerable<TestCaseData> CleanShapes()
    {
        yield return new TestCaseData(BaseConstructorCallLoweringExecutionTests.B5).SetName("lambda_B5");
        yield return new TestCaseData(BaseConstructorCallLoweringExecutionTests.W1).SetName("computed_W1");
        yield return new TestCaseData(BaseConstructorCallLoweringExecutionTests.W2).SetName("call_W2");
        yield return new TestCaseData(BaseConstructorCallLoweringExecutionTests.E04_AndAlsoArg).SetName("multiBlock_AndAlso_E04");
        yield return new TestCaseData(BaseConstructorCallLoweringExecutionTests.E04b_OrElseArg).SetName("multiBlock_OrElse_E04b");
        yield return new TestCaseData(BaseConstructorCallLoweringExecutionTests.E06_Barrier).SetName("lambdaAndComputed_E06");
        yield return new TestCaseData(BaseConstructorCallLoweringExecutionTests.ArrayLiteralArg).SetName("arrayLiteral");
    }

    private static IEnumerable<TestCaseData> Named(string prefix)
        => CleanShapes().Select(t => new TestCaseData(t.Arguments).SetName(prefix + t.TestName));
    private static IEnumerable<TestCaseData> CleanShapesBuilder() => Named("IRBuilder_");
    private static IEnumerable<TestCaseData> CleanShapesStandard() => Named("Standard_");
    private static IEnumerable<TestCaseData> CleanShapesAggressive() => Named("Aggressive_");

    private static IRModule Build(string source) => JsTestSupport.BuildModule(source);

    private static (IRFunction Function, IRBaseConstructorCall Call, BasicBlock Block) FindCall(IRModule module)
    {
        foreach (var f in module.Functions)
            foreach (var b in f.Blocks)
                foreach (var i in b.Instructions)
                    if (i is IRBaseConstructorCall c) return (f, c, b);
        Assert.Fail("the program has no IRBaseConstructorCall — the shape under test was not built");
        return default;
    }

    private static string Describe(IReadOnlyList<InvariantViolation> v) =>
        v.Count == 0 ? "(none)" : string.Join("\n", v.Select(x => x.UseBlock));

    // ============================================================================================
    // The clean shape PASSES — on IRBuilder's output and after both pipelines
    // ============================================================================================

    [TestCaseSource(nameof(CleanShapesBuilder))]
    public void TheCleanShape_PassesOnIRBuildersOutput(string source)
        => Assert.That(IRVerifier.CheckInvariantP(Build(source)), Is.Empty, "IRBuilder's output");

    [TestCaseSource(nameof(CleanShapesStandard))]
    public void TheCleanShape_PassesAfterTheStandardPipeline(string source)
    {
        var module = Build(source);
        var pipeline = new OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.Run(module);
        Assert.That(IRVerifier.CheckInvariantP(module), Is.Empty, "after the standard passes");
    }

    [TestCaseSource(nameof(CleanShapesAggressive))]
    public void TheCleanShape_PassesAfterTheAggressivePipeline(string source)
    {
        var module = Build(source);
        AggressivePipeline.Apply(module);
        Assert.That(IRVerifier.CheckInvariantP(module), Is.Empty, "after the aggressive passes");
    }

    /// <summary>The verifier's real gate: <c>VerifyAfterOptimization</c> in Throw mode does not
    /// throw for a clean shape, and (below) does for a broken one.</summary>
    [Test]
    public void VerifyAfterOptimization_InThrowMode_AcceptsACleanShape()
    {
        var previous = IRVerifier.Mode;
        IRVerifier.Mode = IRVerifierMode.Throw;
        try { Assert.That(() => IRVerifier.VerifyAfterOptimization(Build(BaseConstructorCallLoweringExecutionTests.E06_Barrier)), Throws.Nothing); }
        finally { IRVerifier.Mode = previous; }
    }

    // ============================================================================================
    // (a) an operand used before its definition
    // ============================================================================================

    /// <summary>Delete the definition of the computed operand (W1's <c>p + 1</c>): the base call is
    /// left consuming a value nothing defines — what a DCE bug would do once #163 turns removal on,
    /// which is why D5(a) exists ("a DCE bug shows up as an undefined operand, not a run-time defect").</summary>
    [Test]
    public void A_AnOperandWhoseDefinitionIsDeleted_Fails()
    {
        var module = Build(BaseConstructorCallLoweringExecutionTests.W1);
        var (function, call, _) = FindCall(module);
        var def = call.Args.OfType<IRInstruction>().First(i => i is not IRVariable && i is not IRConstant
            && function.Blocks.Any(b => b.Instructions.Contains(i)));
        foreach (var b in function.Blocks) b.Instructions.Remove(def);

        var violations = IRVerifier.CheckInvariantP(module);
        Assert.That(violations.Select(v => v.UseBlock), Has.Some.Contains("D5(a)"), Describe(violations));
    }

    // ============================================================================================
    // (b) exactly one, in a constructor, in the prologue region
    // ============================================================================================

    [Test]
    public void B_ASecondBaseCall_Fails()
    {
        var module = Build(BaseConstructorCallLoweringExecutionTests.W1);
        var (_, call, block) = FindCall(module);
        block.Instructions.Insert(block.Instructions.IndexOf(call) + 1,
            new IRBaseConstructorCall(call.Args) { ParentBlock = block });

        var violations = IRVerifier.CheckInvariantP(module);
        Assert.That(violations.Select(v => v.UseBlock), Has.Some.Contains("a second IRBaseConstructorCall"), Describe(violations));
    }

    [Test]
    public void B_ABaseCallOutsideAConstructor_Fails()
    {
        var module = Build(BaseConstructorCallLoweringExecutionTests.W1);
        var (_, call, _) = FindCall(module);
        var main = module.Functions.First(f => f.Name == "Main");
        var entry = main.EntryBlock ?? main.Blocks.First();
        entry.Instructions.Insert(0, new IRBaseConstructorCall(call.Args.Where(a => a is IRConstant || a is IRVariable)) { ParentBlock = entry });

        var violations = IRVerifier.CheckInvariantP(module);
        Assert.That(violations.Select(v => v.UseBlock), Has.Some.Contains("no class constructor"), Describe(violations));
    }

    /// <summary>The call in a block nothing reaches from the entry: no path runs it, let alone once.</summary>
    [Test]
    public void B_ABaseCallOutsideTheEntryRegion_Fails()
    {
        var module = Build(BaseConstructorCallLoweringExecutionTests.W1);
        var (function, call, block) = FindCall(module);
        block.Instructions.Remove(call);
        var orphan = function.CreateBlock("orphan_after_t170");
        orphan.Instructions.Add(call);
        call.ParentBlock = orphan;
        orphan.Instructions.Add(new IRReturn(null) { ParentBlock = orphan });

        var violations = IRVerifier.CheckInvariantP(module);
        Assert.That(violations.Select(v => v.UseBlock), Has.Some.Contains("not reachable from the entry block"), Describe(violations));
    }

    /// <summary>The call's own block branching BACK into the prologue region — a loop — would run the
    /// call more than once on some path.</summary>
    [Test]
    public void B_ABlockThatBranchesBackIntoThePrologueRegion_Fails()
    {
        var module = Build(BaseConstructorCallLoweringExecutionTests.E04_AndAlsoArg);
        var (function, _, block) = FindCall(module);
        var entry = function.EntryBlock ?? function.Blocks.First();
        Assert.That(function.Blocks.Count, Is.GreaterThan(1), "the AndAlso shape must be multi-block");
        var terminator = block.GetTerminator();
        block.Instructions[block.Instructions.IndexOf(terminator)] = new IRBranch(entry) { ParentBlock = block };

        var violations = IRVerifier.CheckInvariantP(module);
        Assert.That(violations.Select(v => v.UseBlock), Has.Some.Contains("branches into the prologue region"), Describe(violations));
    }

    // ============================================================================================
    // (c) the prologue is expression-only and CLOSED
    // ============================================================================================

    /// <summary>Insert a use of the prologue value (<c>p + 1</c>) AFTER the call: C# renders the
    /// prologue inside <c>: base(...)</c>, where no body statement can reach it, so a later use is a
    /// shape it cannot express — what CSE or hoisting could create (the reason for D5(c)).</summary>
    [Test]
    public void C_APrologueValueUsedAfterTheCall_Fails()
    {
        var module = Build(BaseConstructorCallLoweringExecutionTests.W1);
        var (_, call, block) = FindCall(module);
        var value = call.Args.OfType<IRInstruction>().OfType<IRValue>().First(v => v is not IRVariable && v is not IRConstant);
        var tmp = new IRVariable("t170probe", value.Type);
        block.Instructions.Insert(block.Instructions.IndexOf(call) + 1, new IRAssignment(tmp, value) { ParentBlock = block });

        var violations = IRVerifier.CheckInvariantP(module);
        Assert.That(violations.Select(v => v.UseBlock), Has.Some.Contains("is used after the call"), Describe(violations));
    }

    /// <summary>A STATEMENT in the prologue — a field store before the base call, i.e. an argument that
    /// "wrote" something — is not part of evaluating an argument.</summary>
    [Test]
    public void C_AStatementInThePrologue_Fails()
    {
        var module = Build(BaseConstructorCallLoweringExecutionTests.W1);
        var (function, call, block) = FindCall(module);
        var value = function.Parameters.First();
        block.Instructions.Insert(block.Instructions.IndexOf(call),
            new IRFieldStore(value, "N", value) { ParentBlock = block });

        var violations = IRVerifier.CheckInvariantP(module);
        Assert.That(violations.Select(v => v.UseBlock), Has.Some.Contains("not part of evaluating an argument"), Describe(violations));
    }

    /// <summary>D4's belt-and-braces at IR level: the prologue must not use <c>Me</c> as a value.</summary>
    [Test]
    public void C_TheObjectUnderConstructionUsedInThePrologue_Fails()
    {
        var module = Build(BaseConstructorCallLoweringExecutionTests.W1);
        var (function, call, block) = FindCall(module);
        var me = new IRVariable("Me", new TypeInfo("Object", TypeKind.Class));
        var probe = new IRAssignment(new IRVariable("t170me", me.Type), me) { ParentBlock = block };
        block.Instructions.Insert(block.Instructions.IndexOf(call), probe);

        var violations = IRVerifier.CheckInvariantP(module);
        Assert.That(violations.Select(v => v.UseBlock), Has.Some.Contains("the object under construction"), Describe(violations));
    }

    // ============================================================================================
    // (d) every lambda is created exactly once
    // ============================================================================================

    /// <summary>ORPHAN a lambda: the base call no longer consumes it, so no instruction anywhere
    /// references the lambda function — the exact shape #170 was, and the check that "would have caught
    /// #170 on day one".</summary>
    [Test]
    public void D_AnOrphanedLambda_Fails()
    {
        var module = Build(BaseConstructorCallLoweringExecutionTests.B5);
        var (_, call, block) = FindCall(module);
        var lambda = call.Args.OfType<IRVariable>().First(v => v.Name.StartsWith("__lambda_", StringComparison.Ordinal));
        var index = call.Args.ToList().IndexOf(lambda);
        block.Instructions[block.Instructions.IndexOf(call)] = new IRBaseConstructorCall(
            call.Args.Select((v, i) => i == index ? new IRConstant(null, lambda.Type) : v)) { ParentBlock = block };

        var violations = IRVerifier.CheckInvariantP(module);
        Assert.That(violations.Select(v => v.UseBlock), Has.Some.Contains("an orphan (D5(d))"), Describe(violations));
    }

    /// <summary>The other half of "exactly one": a lambda consumed by TWO instructions.</summary>
    [Test]
    public void D_ALambdaReferencedTwice_Fails()
    {
        var module = Build(BaseConstructorCallLoweringExecutionTests.B5);
        var (function, call, block) = FindCall(module);
        var lambda = call.Args.OfType<IRVariable>().First(v => v.Name.StartsWith("__lambda_", StringComparison.Ordinal));
        block.Instructions.Insert(block.Instructions.IndexOf(call),
            new IRAssignment(new IRVariable("t170dup", lambda.Type), lambda) { ParentBlock = block });

        var violations = IRVerifier.CheckInvariantP(module);
        Assert.That(violations.Select(v => v.UseBlock), Has.Some.Contains("referenced 2 times"), Describe(violations));
    }

    // ============================================================================================
    // The gate itself
    // ============================================================================================

    /// <summary>Every breach above reaches a build through <c>VerifyAfterOptimization</c>: in Throw mode
    /// a broken module throws <see cref="IRVerificationException"/>, carrying Invariant P.</summary>
    [Test]
    public void VerifyAfterOptimization_InThrowMode_RejectsABrokenModule()
    {
        var module = Build(BaseConstructorCallLoweringExecutionTests.W1);
        var (_, call, block) = FindCall(module);
        block.Instructions.Insert(block.Instructions.IndexOf(call) + 1, new IRBaseConstructorCall(call.Args) { ParentBlock = block });

        var previous = IRVerifier.Mode;
        IRVerifier.Mode = IRVerifierMode.Throw;
        try
        {
            var ex = Assert.Throws<IRVerificationException>(() => IRVerifier.VerifyAfterOptimization(module));
            Assert.That(ex!.Violations.Select(v => v.Invariant), Has.Some.EqualTo("P"));
        }
        finally { IRVerifier.Mode = previous; }
    }

    /// <summary>ClosureLowering's output holds environment stores before the call by design (D3), so
    /// <c>lowered: true</c> must NOT run Invariant P — pinned so it is not "tightened" into a false
    /// positive on every MSIL build.</summary>
    [Test]
    public void VerifyAfterOptimization_OnLoweredIR_SkipsInvariantP()
    {
        // An orphaned lambda breaks ONLY Invariant P (a second base call would also trip S′, because the
        // barrier writes everything), so the skip is what this test observes.
        var module = Build(BaseConstructorCallLoweringExecutionTests.B5);
        var (_, call, block) = FindCall(module);
        var lambda = call.Args.OfType<IRVariable>().First(v => v.Name.StartsWith("__lambda_", StringComparison.Ordinal));
        var index = call.Args.ToList().IndexOf(lambda);
        block.Instructions[block.Instructions.IndexOf(call)] = new IRBaseConstructorCall(
            call.Args.Select((v, i) => i == index ? new IRConstant(null, lambda.Type) : v)) { ParentBlock = block };
        Assert.That(IRVerifier.CheckInvariantP(module), Is.Not.Empty, "precondition: the module really is broken");

        var previous = IRVerifier.Mode;
        IRVerifier.Mode = IRVerifierMode.Throw;
        try { Assert.That(() => IRVerifier.VerifyAfterOptimization(module, lowered: true), Throws.Nothing); }
        finally { IRVerifier.Mode = previous; }
    }
}
