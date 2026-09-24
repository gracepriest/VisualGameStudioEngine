using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.CodeGen.CSharp;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// ⭐ ADR-0003's <b>INV-2, amended 2026-09-24</b>: <c>LoopInvariantCodeMotionPass</c> is
/// REGISTERED in <c>AddAggressivePasses</c> (and absent, as before, from the standard
/// pipeline); <c>LoopUnrollingPass</c> and <c>LoopFusionPass</c> stay unregistered in both. This
/// fixture is the evidence for the split plus the guard that keeps it that way.
///
/// <para>⛔ <b>WHY THE SPLIT.</b> Master's <c>e063faf</c> (PR #85, owner-approved, adopted into
/// this branch by its merge of <c>15e4e63</c>) rewrote <c>LoopInvariantCodeMotionPass</c> —
/// correct invariance (a local is invariant only if nothing in the loop writes it), a real
/// single-entry preheader, and pure-value-only hoisting — and RE-REGISTERED it, measuring all 13
/// loop shapes right on JavaScript, C++ and C#. See ADR-0003's Amendment section for the full
/// record, including what its revisit conditions were and were not measured against.
/// <c>LoopUnrollingPass</c> and <c>LoopFusionPass</c> were not touched by that rewrite and are
/// still broken for the reasons pinned below — master unregistered them independently, having
/// measured unrolling break 9 of 10 single loops and fusion break adjacent same-bound loops.</para>
///
/// <para>They were the FOURTH, FIFTH and SIXTH passes <c>OptimizationPipeline</c> kept but did
/// not ship, after <c>ConstantPropagationPass</c>, <c>FunctionInliningPass</c> and
/// <c>InductionVariablePass</c> — <c>LoopUnrollingPass</c> and <c>LoopFusionPass</c> still are;
/// see <see cref="FunctionInliningDisabledTests"/> and <see cref="InductionVariableDisabledTests"/>,
/// which this fixture is modelled on.</para>
///
/// <para>⛔ <b>THE AGGRESSIVE PIPELINE SHIPS.</b> <c>Configurations["Release"].OptimizationsEnabled</c>
/// reaches <c>CompilerOptions.OptimizeAggressive</c> through <c>Program.cs</c> and
/// <c>BuildService.cs</c>, and thence <c>AddAggressivePasses</c>. A Release <c>.blproj</c> build —
/// CLI or IDE — takes LICM (registered) and does not take unrolling or fusion (still not), and
/// so does <c>BasicLang.exe file.bas --optimize</c>.</para>
///
/// <para>⛔ <b>THEY SHARE ONE SUBSTRATE.</b> All three read <c>ControlFlowGraph.IdentifyLoops</c>,
/// and there is no per-consumer opt-out of it. Before the back-edge predicate was fixed, LICM was
/// the only one of the three that fired at all, and it miscompiled: two sibling loops printed 65
/// where 29 is correct on C#, this suite's reference oracle, and C++ and MSIL ran every counted
/// loop ZERO times. Repairing the substrate is what turned the other two on for the first time —
/// both are still broken when they fire, which the pins below measure, on the corpus
/// <see cref="CfgLoopShapes"/> holds. LICM's own rewrite is the exception: it is what makes
/// its re-registration correct on this corpus.</para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class LoopPassesDisabledTests
{
    // ====================================================================================
    // INV-2: no loop pass is registered in any pipeline.
    // ====================================================================================

    /// <summary>
    /// ⛔ <b>THE M2/M3/M4 GUARD, asserted on the PASS LIST itself</b> rather than on emitted text,
    /// for the reason <c>InductionVariableDisabledTests.ThePassIsAbsentFromTheAggressivePipeline_AndTheClassStillExists</c>
    /// gives: the list is the thing the decision is about.
    ///
    /// <para>⭐ <b>AMENDED 2026-09-24 — LICM IS NOW EXPECTED IN <c>AddAggressivePasses</c>.</b>
    /// Master's <c>e063faf</c> (PR #85, owner-approved) rewrote <c>LoopInvariantCodeMotionPass</c>
    /// and re-registered it, and this branch's merge of <c>15e4e63</c> adopts that. ADR-0003's
    /// Amendment section records the full change; INV-2 is withdrawn as a blanket rule but still
    /// holds for <c>LoopUnrollingPass</c> and <c>LoopFusionPass</c>, which stay banned from BOTH
    /// pipelines below, unchanged from the original ruling. This test used to assert LICM was
    /// banned too, back when the old, unregistered pass was inert-by-brokenness; it now asserts
    /// the opposite for LICM specifically, on purpose, because that is what actually ships.</para>
    ///
    /// <para>⛔ <b>AND HERE IT IS THE ONLY THING THAT CAN SEE THE OTHER TWO.</b>
    /// <c>LoopFusionPass</c> and <c>LoopUnrollingPass</c> re-registered DO change output, on 4
    /// and 8 of 52 aggressive cells respectively, and <see cref="CfgLoopShapesAggressiveTests"/>
    /// catches those too — but only this test catches the pass list directly, which is what the
    /// decision is actually about.</para>
    ///
    /// <para>The positive half is asserted as well: the CLASSES must still exist and still be
    /// constructible, because the "still broken" pins below depend on constructing them, and
    /// deleting a class would silently delete the record of WHY the pass is disabled — the same
    /// trap <see cref="FunctionInliningDisabledTests"/> documents for <c>CloneAndRemap</c>.</para>
    /// </summary>
    [Test]
    public void LicmIsInAggressiveOnly_UnrollingAndFusionAreAbsentFromBothPipelines_AndTheClassesStillExist()
    {
        var standard = PassesOf(p => p.AddStandardPasses());
        var aggressive = PassesOf(p => p.AddAggressivePasses());

        var bannedFromBoth = new[]
        {
            typeof(LoopUnrollingPass),
            typeof(LoopFusionPass),
        };

        Assert.Multiple(() =>
        {
            foreach (var t in bannedFromBoth)
            {
                Assert.That(standard, Does.Not.Contain(t),
                    $"{t.Name} is registered in AddStandardPasses. ADR-0003 INV-2 (amended "
                    + "2026-09-24) still forbids it for this pass. The list was: "
                    + string.Join(", ", standard.Select(x => x.Name)));
                Assert.That(aggressive, Does.Not.Contain(t),
                    $"{t.Name} is back in AddAggressivePasses. ADR-0003's amended D2 unregisters "
                    + "it independently of LICM — master measured unrolling break 9 of 10 single "
                    + "loops and fusion break adjacent same-bound loops. Read this fixture and "
                    + "the disabled AddPass lines in IROptimizer.cs first. The list was: "
                    + string.Join(", ", aggressive.Select(x => x.Name)));
            }

            // LICM: the opposite claim. Absent from the standard pipeline (unchanged), present
            // in the aggressive one (master's e063faf, adopted here — see ADR-0003's Amendment).
            Assert.That(standard, Does.Not.Contain(typeof(LoopInvariantCodeMotionPass)),
                "LoopInvariantCodeMotionPass is registered in AddStandardPasses. Master's "
                + "e063faf registers it only in AddAggressivePasses. The list was: "
                + string.Join(", ", standard.Select(x => x.Name)));
            Assert.That(aggressive, Does.Contain(typeof(LoopInvariantCodeMotionPass)),
                "LoopInvariantCodeMotionPass is missing from AddAggressivePasses. ADR-0003's "
                + "amendment records it as registered there (master's e063faf, PR #85, adopted "
                + "in this branch's merge of 15e4e63) — read the Amendment section before "
                + "removing it again. The list was: "
                + string.Join(", ", aggressive.Select(x => x.Name)));

            // Non-vacuity, both halves: the lists must be real, and aggressive must still be a
            // strict superset of standard. If AddAggressivePasses ever added nothing of its own,
            // every "not in the aggressive list" assertion above would pass for free.
            Assert.That(standard, Is.Not.Empty, "AddStandardPasses added no passes at all");
            Assert.That(aggressive.Count, Is.GreaterThan(standard.Count),
                "AddAggressivePasses adds nothing beyond AddStandardPasses, so the assertions "
                + "above are vacuous for the aggressive pipeline");

            Assert.That(() => new LoopInvariantCodeMotionPass(), Throws.Nothing);
            Assert.That(() => new LoopUnrollingPass(4), Throws.Nothing);
            Assert.That(() => new LoopFusionPass(), Throws.Nothing);
        });
    }

    // ====================================================================================
    // The invariance check, run directly — LICM hoists nothing from a loop with nothing
    // invariant in it, even though (unlike before ADR-0003's amendment) this is now the exact
    // class this branch ships.
    // ====================================================================================

    /// <summary>
    /// ⛔ <b>THE INVARIANCE-CHECK GUARD, RUN DIRECTLY.</b> Constructs
    /// <c>LoopInvariantCodeMotionPass</c> — master's <c>e063faf</c> rewrite, the SAME class
    /// <c>AddAggressivePasses</c> registers since ADR-0003's amendment — and runs it alone
    /// against two shapes that each contain a natural loop but nothing invariant inside it, so a
    /// correct invariance check must hoist nothing from either.
    ///
    /// <para>⚠ It is a single <c>pass.Run(module)</c> and not a <c>pipeline.Run</c>, deliberately
    /// — <c>OptimizationPipeline.Run</c> iterates to a fixed point, so the pass's
    /// <c>ModificationCount</c> is ZERO after any converging pipeline run no matter what the pass
    /// did on the way there, and an assertion made there would be vacuous. The pattern is the one
    /// <c>InductionVariableDisabledTests.RunDirectly_ThePassStillMiscompiles_WhichIsWhyItIsDisabled</c>
    /// established for exactly this situation.</para>
    ///
    /// <para><b>Why these two shapes hoist nothing.</b> <c>A12</c>'s loop body is
    /// <c>acc = acc + i</c>: <c>i</c> is the loop counter and <c>acc</c> is written every
    /// iteration, so by the pass's own invariance test —
    /// <c>!variable.IsGlobal &amp;&amp; !written.Contains(variable.Name)</c> — neither operand of
    /// the add is invariant, and nothing else in the loop is a pure binary/unary/compare op built
    /// only from invariant operands. <c>A13</c> is the same shape twice, over two independent
    /// accumulators. <b>THIS IS NOT A GENERAL LIMIT OF THE PASS</b> — unlike the branch-local fix
    /// ADR-0003 originally shipped, master's rewrite genuinely CAN hoist a loop-invariant
    /// expression out of a loop that has one (measured separately: probe
    /// <c>scratchpad/f111g/licm/L6</c> hoists <c>x * y</c>, where neither operand is written in
    /// the loop, and the program stays correct). These two shapes were chosen because they
    /// contain nothing invariant, which is exactly the case a REGRESSED invariance check gets
    /// wrong: a bare <c>IRVariable</c> operand falling through to a null-<c>ParentBlock</c> check
    /// that reads as "outside the loop" for every local — the defect both the pre-e063faf pass on
    /// master and this branch's original ADR-0003 fix were written against. MEASURED with that
    /// defect present: this same construction turns <c>A12</c> into <c>S=1</c> where <c>S=29</c>
    /// is correct and <c>A13</c> into <c>S=1,S=1</c> where <c>S=29,S=29</c> is — a silent wrong
    /// answer on C#, the reference oracle, with no build failure anywhere.</para>
    ///
    /// <para>⚠ NON-VACUITY: the shape must actually contain a natural loop, asserted here, or
    /// "hoisted nothing" would be a statement about an empty input.</para>
    ///
    /// <para>⚠ Shape choice is measured, not arbitrary. <c>A01</c> would NOT do: when the
    /// invariance check was broken this way (this branch's pre-ADR-0003 fall-through, and
    /// master's own pre-<c>e063faf</c> pass), it was measured to hoist the loop counter and bound
    /// out of shapes like <c>A01</c> while the program still printed the right answer — the loop
    /// only reads them, so a hoisted copy changes nothing observable — so a shape like it is
    /// blind to the regression. <c>A04_while</c> and <c>A05_do</c> would be worse than useless —
    /// with the check broken this way, the emitted C# runs the host out of memory. <c>A12</c> and
    /// <c>A13</c> are the two that go silently, quickly wrong. RE-CONFIRMED here: with the
    /// current, corrected pass, <c>A01</c>, <c>A02</c>, <c>A12</c> and <c>A13</c> all hoist
    /// nothing (<c>ModificationCount == 0</c> for each) — the loop counter is the one non-invariant
    /// operand every one of these shapes' loop-carried expressions depends on.</para>
    /// </summary>
    [Test]
    [TestCase("A12_unrollable", TestName = "LicmRunDirectly_OnACountedCallFreeLoop")]
    [TestCase("A13_fusable", TestName = "LicmRunDirectly_OnTwoSiblingLoops")]
    public void LicmAddedExplicitly_HoistsNothing_AndLeavesTheProgramCorrect(string shapeName)
    {
        var shape = CfgLoopShapes.Get(shapeName);
        var module = JsTestSupport.BuildModule(shape.Source, sourceFilePath: "prog.bas");

        var main = module.Functions.Single(f => f.Name == "Main" && !f.IsExternal);
        var cfg = new ControlFlowGraph(main);
        cfg.Build();
        cfg.ComputeDominators();
        cfg.IdentifyLoops();

        var pass = new LoopInvariantCodeMotionPass();
        bool changed = pass.Run(module);

        Assert.Multiple(() =>
        {
            Assert.That(cfg.NaturalLoops, Is.Not.Empty,
                "non-vacuity: this shape must contain a natural loop, or 'LICM hoisted nothing' "
                + "says nothing about the invariance check");

            Assert.That(changed, Is.False,
                "LICM hoisted something out of a loop that has nothing invariant in it — the "
                + "invariance check has regressed to treating a local as invariant when it "
                + "should not (a local is not invariant if the loop writes it). That is what "
                + "hoists the induction variable out of its own loop. See ADR-0003's Amendment.");
            Assert.That(pass.ModificationCount, Is.Zero,
                "LICM reported modifications: " + pass.ModificationCount);

            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpText(
                    new ImprovedCSharpCodeGenerator().Generate(module))),
                Is.EqualTo(shape.Expected),
                "…and the program still prints the right answer. With the invariance check "
                + "broken this prints S=1 for A12 and S=1,S=1 for A13 — silently, on the "
                + "reference oracle, with no build failure to notice.");
        });
    }

    /// <summary>
    /// ⭐ <b>UPDATED 2026-09-24 — LICM SHIPS NOW; THIS TEST'S PREMISE HAS CHANGED, NOT ITS
    /// VALUE.</b> Written when ADR-0003 forbade LICM outright, this test deliberately built a
    /// pipeline the ADR did not allow, purely to exercise the invariance check end-to-end.
    /// <c>AddAggressivePasses</c> now registers <c>LoopInvariantCodeMotionPass</c> itself (master's
    /// <c>e063faf</c>, adopted by this branch's merge of <c>15e4e63</c> — see ADR-0003's
    /// Amendment), so <c>pipeline.AddPass(new LoopInvariantCodeMotionPass())</c> below now adds a
    /// SECOND instance on top of the one <c>AddAggressivePasses</c> already added — the pipeline
    /// this test builds is a superset of what ships, not a forbidden one. It is kept anyway: it
    /// still catches a regression of the invariance check inside something closer to the real
    /// aggressive pipeline (every other aggressive pass runs too) than
    /// <see cref="LicmAddedExplicitly_HoistsNothing_AndLeavesTheProgramCorrect"/>'s bare
    /// <c>pass.Run</c> does, and it still asserts a genuine invariant: if the invariance check
    /// ever regresses to "every local is invariant" — this branch's pre-ADR-0003 state, and
    /// master's own pre-<c>e063faf</c> state — TWO copies of a broken LICM back to back compound
    /// the damage rather than cancelling it, which makes this a strictly HARDER bar to pass than
    /// the shipping pipeline's single copy, not an easier one.</para>
    ///
    /// <para>MEASURED on the current, corrected pass: correct on all four shapes, and the
    /// pipeline reports ZERO total modifications on them — consistent with
    /// <see cref="LicmIsInAggressiveOnly_UnrollingAndFusionAreAbsentFromBothPipelines_AndTheClassesStillExist"/>'s
    /// probe that none of these four shapes has anything invariant to hoist.</para>
    ///
    /// <para>⛔⛔ <b>THE PIPELINE IS PINNED TO ONE ITERATION, AND THAT IS NOT A CONVENIENCE.</b>
    /// <c>OptimizationPipeline</c> defaults to iterating to a fixed point (up to 10 rounds), and
    /// MEASURED with the invariance check broken the way it was before ADR-0003 and before
    /// <c>e063faf</c>, <b>that loop does not converge</b>: LICM re-hoists its own output round
    /// after round. One case alone took 31 s; another never terminated at all (killed at 200 s);
    /// and inside a full fixture run the test host reached <b>5.9 GB and CRASHED</b>, turning
    /// four clean red rows into <c>Test Run Aborted</c> — a result nobody can read. ⚠ <b>A mutant
    /// that hangs is worse than a mutant that survives</b>, because it takes the rest of the run
    /// with it. One round is enough to expose the defect (one hoist out of a loop is already the
    /// wrong answer) and is bounded by construction.</para>
    /// </summary>
    // ⚠ The first two are labelled CONTROLS, not guards, and the difference is measured: with the
    // invariance check broken, ONE round of LICM over A01 and A02 still prints the right answer
    // (those shapes only go wrong once the fixed-point loop re-hoists, which is the behaviour this
    // test deliberately does not run). They are kept because they must stay correct, and a
    // fixture that quietly dropped every shape it could not kill a mutant with would be claiming
    // narrower coverage than it has. The two literal-bound shapes are the ones with teeth: both go
    // red in ~250 ms with the check broken.
    [Test]
    [TestCase("A01_for_single", TestName = "AggressivePlusLicm_ACountedFor_Control")]
    [TestCase("A02_for_nested", TestName = "AggressivePlusLicm_ANestedCountedFor_Control")]
    [TestCase("A12_unrollable", TestName = "AggressivePlusLicm_ACountedCallFreeLoop")]
    [TestCase("A13_fusable", TestName = "AggressivePlusLicm_TwoSiblingLoops")]
    public void TheAggressivePipelineWithLicmAddedBack_StillPrintsTheCorrectValue(string shapeName)
    {
        var shape = CfgLoopShapes.Get(shapeName);
        var module = JsTestSupport.BuildModule(shape.Source, sourceFilePath: "prog.bas");

        // ⛔ maxIterations: 1 — see the docstring. With IsValueInvariant reverted the default
        // fixed-point loop does not converge and crashes the test host.
        var pipeline = new OptimizationPipeline(maxIterations: 1);
        pipeline.AddAggressivePasses();
        pipeline.AddPass(new LoopInvariantCodeMotionPass());
        pipeline.Run(module);

        Assert.That(PassesOf(pipeline).Select(t => t.Name), Does.Contain(nameof(LoopInvariantCodeMotionPass)),
            "non-vacuity: LICM must actually be in the pipeline this test runs, or it is just "
            + "another assertion about the shipping pipeline");

        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpText(
                new ImprovedCSharpCodeGenerator().Generate(module))),
            Is.EqualTo(shape.Expected),
            "the aggressive pipeline WITH LICM added back printed the wrong answer. The expected "
            + "cause is issue #114 reproduced — LICM plus a broken IsValueInvariant hoisting the "
            + "induction variable out of its own loop — but ANY pass added to the aggressive "
            + "pipeline can fail here, so read the actual diff before assuming which.");
    }

    // ====================================================================================
    // The other two passes, constructed directly: STILL BROKEN, pinned.
    // ====================================================================================

    /// <summary>
    /// ⛔ <c>LoopUnrollingPass</c> IS STILL REACHABLE AND STILL BROKEN, pinned deliberately —
    /// what makes "disabled" a measured claim rather than a comment.
    ///
    /// <para>⚠ <b>IT HAD NEVER EXECUTED BEFORE ADR-0003.</b> On the inverted back-edge predicate
    /// it refused every program at <c>CanUnroll</c>'s trip-count gate, because
    /// <c>FindInitialValue</c> needs a predecessor outside the loop and <c>entry</c> was inside
    /// every bogus loop set. Repairing the predicate is what turns it on for the first time — so
    /// unregistering it removes nothing any program ever received, and NOT unregistering it would
    /// have shipped a never-run pass as a side effect of a substrate repair.</para>
    ///
    /// <para>⚠ <b>ASSERTS THE MECHANISM, NOT A MINTED SPELLING</b>, for the reason
    /// <c>InductionVariableDisabledTests.RunDirectly_ThePassStillMiscompiles_WhichIsWhyItIsDisabled</c>
    /// records: which identifier ends up undeclared depends on which other passes ran. The
    /// assertions are that the pass MODIFIES the function, that it leaves an operand naming a
    /// variable nothing declares, and that it leaves a use pointing at an instruction it replaced.
    /// The <c>_u0_</c> prefix is asserted only as a diagnostic aid alongside the mechanism, never
    /// alone. MEASURED on <c>A12</c>, one direct <c>Run</c>: 8 undeclared names
    /// (<c>_u0_acc</c>, <c>_u0_i</c>, …) and 1 dangling operand; under a pipeline that re-runs the
    /// pass over its own output the prefixes compound to <c>_u0__u0_i</c>, and all four backends
    /// reject the result — C# CS0103, C++ undeclared identifier, JavaScript ReferenceError, MSIL
    /// InvalidProgramException.</para>
    ///
    /// <para>⚠ If someone repairs the pass, this test goes RED — it asserts the defects are still
    /// there. ⛔ Red here is not a licence to re-register it: read ADR-0003's D2 revisit clause,
    /// and <c>OptimizerMintedVariableTests</c>, which records that the optimizer still has no
    /// facility for DECLARING a variable it mints.</para>
    /// </summary>
    [Test]
    public void LoopUnrollingRunDirectly_StillMintsUndeclaredNames_WhichIsWhyItIsDisabled()
    {
        var shape = CfgLoopShapes.Get("A12_unrollable");
        var module = JsTestSupport.BuildModule(shape.Source, sourceFilePath: "prog.bas");
        var namesBefore = OptimizerIr.AllVariableNames(module);

        var pass = new LoopUnrollingPass(4);
        bool changed = pass.Run(module);

        var undeclared = OptimizerIr.UndeclaredOperandNames(module).ToList();
        var minted = OptimizerIr.AllVariableNames(module).Where(n => !namesBefore.Contains(n)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True,
                "non-vacuity: the pass must still FIRE on a counted, call-free loop over literal "
                + "bounds — if it no longer does, this fixture's premise changed and the reason "
                + "the pass is unregistered needs re-measuring, not deleting");

            Assert.That(undeclared, Is.Not.Empty,
                "the pass must still leave operands naming variables nothing declares — it does "
                + "not write IRFunction.LocalVariables, and no pass in IROptimizer.cs ever has. "
                + "Minted: " + string.Join(", ", minted));
            Assert.That(undeclared.Any(n => n.Contains("_u0_", StringComparison.Ordinal)), Is.True,
                "…and at least one of them carries the pass's own minted prefix. Undeclared: "
                + string.Join(", ", undeclared));

            Assert.That(OptimizerIr.DanglingOperandsOf(module), Is.Not.Empty,
                "the pass must still leave a use pointing at an instruction it replaced — the "
                + "same missing ReplaceUses call as InductionVariablePass and, before 67782af, "
                + "CSE and the peephole pass");
        });
    }

    /// <summary>
    /// ⛔ <c>LoopFusionPass</c> IS STILL REACHABLE AND STILL BROKEN, pinned the same way and for
    /// the same reason. Like <c>LoopUnrollingPass</c> it had never executed before ADR-0003 — it
    /// refused at <c>GetLoopBounds</c> returning null on the bogus loop sets.
    ///
    /// <para>⛔ <b>THE DEFECT IS ASSERTED AT THE IR, NOT AT A BACKEND MESSAGE.</b> <c>FuseLoops</c>
    /// removes the second loop's blocks from <c>IRFunction.Blocks</c> while branches still target
    /// them. MEASURED on <c>A13</c>, one direct <c>Run</c>: <c>Main</c> drops from 9 blocks to 6
    /// and <c>for1.cond</c> is still a branch target that is no longer in the function. That
    /// single fact is what every backend then reports in its own dialect — C++ "use of undeclared
    /// label 'for0_inc'", MSIL "Unable to find forward reference label 'for0inc'", JavaScript
    /// REFUSING the function outright with a <c>NotSupportedException</c> — and, ⛔ on C#, NOT
    /// reporting anything at all: it emits a program that compiles, runs and prints <c>29,37</c>
    /// where <c>29,29</c> is correct. "It compiles" is not evidence this pass is safe, which is
    /// why the assertion is on the dangling block reference rather than on a build failure.</para>
    ///
    /// <para>⚠ The shape needs LITERAL bounds: MEASURED, on <c>A03_for_siblings</c> — the same two
    /// sibling loops over a CALL-valued bound — <c>GetLoopBounds</c> returns null and the pass does
    /// not fire at all. That is why <c>A13_fusable</c> exists separately in the corpus.</para>
    /// </summary>
    [Test]
    public void LoopFusionRunDirectly_StillLeavesABranchTargetingARemovedBlock_WhichIsWhyItIsDisabled()
    {
        var shape = CfgLoopShapes.Get("A13_fusable");
        var module = JsTestSupport.BuildModule(shape.Source, sourceFilePath: "prog.bas");
        var main = module.Functions.Single(f => f.Name == "Main" && !f.IsExternal);
        int blocksBefore = main.Blocks.Count;

        var pass = new LoopFusionPass();
        bool changed = pass.Run(module);

        var orphanTargets = BranchTargets(main)
            .Where(t => !main.Blocks.Contains(t))
            .Select(t => t.Name)
            .Distinct()
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True,
                "non-vacuity: the pass must still FIRE on two sibling loops over the same literal "
                + "bound — if it no longer does, this fixture's premise changed");
            Assert.That(main.Blocks.Count, Is.LessThan(blocksBefore),
                $"the pass must still remove blocks (was {blocksBefore}, now {main.Blocks.Count})");

            Assert.That(orphanTargets, Is.Not.Empty,
                "the pass must still leave a branch targeting a block it removed from "
                + "IRFunction.Blocks. That is the defect, and it is silent on C#: the emitted "
                + "program compiles and prints 29,37 where 29,29 is correct.");
        });
    }

    // ====================================================================================
    // Both shipping entry points, aggressive — the routes a Release build actually takes.
    // ====================================================================================

    /// <summary>
    /// ⛔ BOTH SHIPPING ENTRY POINTS, AGGRESSIVE, ASSERTED BY VALUE. Not a fixture-local pipeline:
    /// <c>BasicCompiler.CompileFile</c> is what <c>BasicLang.exe prog.bas --target=… --optimize</c>
    /// runs, and <c>CompileProjectFiles</c> is what the CLI's <c>build</c> runs AND what the IDE's
    /// build service delegates to — the route a Release <c>.blproj</c> takes. CLAUDE.md's rule:
    /// a fix verified only through the in-fixture helper can still break both.
    ///
    /// <para>The shapes are the two that carried the damage: <c>A13</c> printed <c>65</c> for
    /// <c>29</c> through these very entry points at the defect, and <c>A12</c> is the one
    /// <c>LoopUnrollingPass</c> breaks if it is ever registered again.</para>
    ///
    /// <para>⚠ The SPAWNED <c>BasicLang.exe</c> leg (<c>CliTestHarness</c>) is deliberately not
    /// used: it is a Windows apphost that is not deployed on Linux, so every fixture built on it
    /// already sits in this machine's baseline failure set. Calling the compiler's own entry
    /// points in process exercises the same engine and actually runs here. The IR each entry
    /// point produced is then emitted as C# and RUN, because the value is the property — an
    /// IR-shape assertion could not see the 65-for-29.</para>
    /// </summary>
    [Test]
    [TestCase("A12_unrollable", false, TestName = "TheCliSingleFileEntryPoint_Aggressive_ACountedCallFreeLoop")]
    [TestCase("A12_unrollable", true, TestName = "TheProjectBuildEntryPoint_Aggressive_ACountedCallFreeLoop")]
    [TestCase("A13_fusable", false, TestName = "TheCliSingleFileEntryPoint_Aggressive_TwoSiblingLoops")]
    [TestCase("A13_fusable", true, TestName = "TheProjectBuildEntryPoint_Aggressive_TwoSiblingLoops")]
    public void BothCompilerEntryPoints_Aggressive_PrintTheCorrectValue(string shapeName, bool asProject)
    {
        var shape = CfgLoopShapes.Get(shapeName);

        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "BasicLang_CfgLoopEntry_" + System.IO.Path.GetRandomFileName());
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            var file = System.IO.Path.Combine(dir, "prog.bas");
            System.IO.File.WriteAllText(file, shape.Source);

            var compiler = new BasicLang.Compiler.BasicCompiler(
                new BasicLang.Compiler.CompilerOptions { OptimizeAggressive = true });

            var result = asProject
                ? compiler.CompileProjectFiles(new[] { file })
                : compiler.CompileFile(file);

            Assert.That(result.HasErrors, Is.False,
                string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            Assert.That(result.CombinedIR, Is.Not.Null, "the entry point produced no combined IR");

            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpText(
                    new ImprovedCSharpCodeGenerator().Generate(result.CombinedIR))),
                Is.EqualTo(shape.Expected),
                (asProject ? "CompileProjectFiles" : "CompileFile")
                + " with OptimizeAggressive printed the wrong answer — this is the route a "
                + "Release build takes, and it is where the 65-for-29 shipped.");
        }
        finally { try { System.IO.Directory.Delete(dir, true); } catch { /* temp */ } }
    }

    // ====================================================================================
    // Helpers.
    // ====================================================================================

    private static List<Type> PassesOf(Action<OptimizationPipeline> build)
    {
        var pipeline = new OptimizationPipeline();
        build(pipeline);
        return PassesOf(pipeline);
    }

    /// <summary>
    /// The pipeline's pass list. Reflection because <c>_passes</c> is private and the list IS the
    /// subject: INV-2 (amended for LICM — see ADR-0003's Amendment) is a statement about what
    /// <c>AddAggressivePasses</c> puts in it, and inferring that from emitted text would make the
    /// guard depend on a pass's observable effect — which on this fixture's own corpus is nil for
    /// LICM (nothing in these 13 shapes is invariant), even though LICM is not inert in general.
    /// </summary>
    private static List<Type> PassesOf(OptimizationPipeline pipeline)
    {
        var field = typeof(OptimizationPipeline).GetField("_passes",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null,
            "OptimizationPipeline._passes is gone — this guard needs re-anchoring, not deleting");
        return ((IEnumerable<OptimizationPass>)field!.GetValue(pipeline)!)
            .Select(p => p.GetType()).ToList();
    }

    /// <summary>Every block named as a branch target anywhere in <paramref name="function"/>.</summary>
    private static IEnumerable<BasicBlock> BranchTargets(IRFunction function)
    {
        foreach (var block in function.Blocks)
        {
            foreach (var inst in block.Instructions)
            {
                if (inst is IRBranch branch && branch.Target != null)
                    yield return branch.Target;
                else if (inst is IRConditionalBranch cond)
                {
                    if (cond.TrueTarget != null) yield return cond.TrueTarget;
                    if (cond.FalseTarget != null) yield return cond.FalseTarget;
                }
            }
        }
    }
}
