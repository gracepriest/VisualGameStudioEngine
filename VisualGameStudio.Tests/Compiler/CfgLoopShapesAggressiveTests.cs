using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// ⭐ ADR-0003's <b>INV-3</b>, whole: for all 13 CFG shapes on all four backends,
/// <c>--optimize</c> output is behaviourally identical to default output — and both are the
/// arithmetically correct answer.
///
/// <para>⛔ <b>"IT COMPILES" IS NOT THE ORACLE, AND "THE PIPELINES AGREE" IS NOT EITHER.</b> The
/// baseline verdict census for this corpus counted 49 cells as "OK" — a census that included
/// A13 printing <c>65</c> where <c>29</c> is correct on C#, this suite's reference oracle, and
/// included every C++/MSIL run that executed its loop ZERO times and exited 0. Two pipelines can
/// also agree on a wrong answer. So every cell here is pinned to the VALUE, computed by hand from
/// the source, and the default pipeline is asserted alongside the aggressive one rather than used
/// as the expectation.</para>
///
/// <para>⭐ <b>THE SHAPES ARE THE SAME OBJECTS</b> <see cref="CfgNaturalLoopTests"/> asserts loop
/// sets for — <see cref="CfgLoopShapes"/> carries source, block names, loop sets and expected
/// stdout together. A shape cannot be edited into a different program for one fixture while the
/// other keeps asserting about the old one.</para>
///
/// <para>⛔ <b>WHAT EACH SHAPE IS FOR.</b> <c>A13_fusable</c> is the silent one: two sibling loops
/// with the same bound, where the aggressive pipeline printed 65-for-29 at the defect and prints
/// <c>29,37</c> with <c>LoopFusionPass</c> re-registered — a program that COMPILES on C# and is
/// wrong, which is why C++'s "undeclared label" and MSIL's forward-reference error are not the
/// assertion. <c>A12_unrollable</c> is the counted, call-free loop every <c>LoopUnrollingPass</c>
/// gate accepts; with that pass re-registered all four backends fail on the doubly-prefixed
/// <c>_u0__u0_i</c> it mints. The other eleven cover the remaining lowerings.</para>
///
/// <para>⚠ ONE SHAPE PER TEST, for the reason
/// <c>InductionVariableDisabledTests.ACountedForWithAMultiply_RunsTheSameOnBothPipelines</c>
/// gives: both runners use <c>Assert.Multiple</c> and <c>FourBackends.RunEmittedCSharp</c> is
/// in-process Roslyn with NO timeout, so several programs in one case would report the first
/// one's compile error for all of them and a non-terminating program would hang the test host.
/// It is also one C++ compile per case.</para>
///
/// <para>⚠ <c>[Category("Integration")]</c>: each case compiles C++ with a real toolchain,
/// assembles IL and spawns Node. The whole fixture measured at ~34 s.</para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class CfgLoopShapesAggressiveTests
{
    /// <summary>
    /// ⛔ THE VALUE HALF OF THE CONTRACT. Each shape run on C++, JavaScript, MSIL and C#, under
    /// the DEFAULT pipeline and under <c>AddAggressivePasses()</c>, all eight cells equal to the
    /// value computed from the source.
    ///
    /// <para>MEASURED at this change: 104 of 104 cells correct. ⭐ That number is also the
    /// evidence for the promotions this change makes — <c>FourBackends.RunsOnEveryBackendAggressive</c>,
    /// <c>BclE2E.CompileToCppAggressive</c> and <c>MsilHarness.CompileToIl(aggressive:)</c> each
    /// carried a docstring saying a counted <c>For</c> ran ZERO times under the aggressive
    /// pipeline on C++ and MSIL. It does not any more, and the eight counted-loop rows below are
    /// what says so.</para>
    ///
    /// <para>⚠ These rows are NOT what protects the core fix. With the back-edge predicate put
    /// back the wrong way round, all 104 cells stay byte-identical and correct, because no loop
    /// pass is registered and nothing reads <c>ControlFlowGraph.NaturalLoops</c>. That guard is
    /// <see cref="CfgNaturalLoopTests"/>, and this fixture cannot substitute for it.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    [TestCase("A01_for_single", TestName = "ACountedFor")]
    [TestCase("A02_for_nested", TestName = "ANestedCountedFor")]
    [TestCase("A03_for_siblings", TestName = "TwoSiblingCountedFors")]
    [TestCase("A04_while", TestName = "AWhileLoop")]
    [TestCase("A05_do", TestName = "ADoWhileLoop")]
    [TestCase("A06_for_exit", TestName = "ACountedForWithAnExitFor")]
    [TestCase("A07_for_if", TestName = "ACountedForWithAnIfElseInItsBody")]
    [TestCase("A08_for_try", TestName = "ACountedForWithATryCatchInItsBody")]
    [TestCase("A09_foreach", TestName = "AForEachOverAList")]
    [TestCase("A10_noloop", TestName = "AnIfWithNoLoopAtAll")]
    [TestCase("A11_singleblock", TestName = "ASingleBlockMain")]
    [TestCase("A12_unrollable", TestName = "ACountedCallFreeLoopOverLiteralBounds")]
    [TestCase("A13_fusable", TestName = "TwoSiblingLoopsWithTheSameBound")]
    public void BothPipelinesPrintTheCorrectValueOnAllFourBackends(string shapeName)
    {
        var shape = CfgLoopShapes.Get(shapeName);

        FourBackends.RunsOnEveryBackend(shape.Source, shape.Expected);
        FourBackends.RunsOnEveryBackendAggressive(shape.Source, shape.Expected);
    }
}
