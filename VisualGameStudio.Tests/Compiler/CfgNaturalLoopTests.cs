using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler.IR;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// ⭐ THE STRUCTURAL FIXTURE FOR ADR-0003's INV-1 AND INV-4, and the ONLY thing in the suite
/// that can see the fix at all.
///
/// <para>⛔ <b>WHY THIS FIXTURE HAD TO BE WRITTEN, AND WHY IT CANNOT BE REPLACED BY A VALUE
/// ASSERTION.</b> The whole of ADR-0003's D1 is one token in
/// <c>ControlFlowGraph.FindBackEdges</c> — <c>block.Dominators.Contains(successor)</c>, which the
/// defect spelled the other way round. MEASURED by re-running the 13-shape corpus below with
/// that token put back: <b>all 104 end-to-end cells (13 shapes × 4 backends × both pipelines)
/// stay byte-identical and stay CORRECT.</b> Not one backend, not one program, not one entry
/// point notices. The reason is D2: all three loop passes are unregistered, so
/// <see cref="ControlFlowGraph.NaturalLoops"/> has <b>no consumer in the shipping compiler at
/// all</b>, and a property with no consumer cannot be observed downstream of itself.</para>
///
/// <para>So the core repair is invisible to every value oracle the suite has, and would be
/// silently revertible forever behind a green suite. The assertions here are made DIRECTLY on
/// <c>ControlFlowGraph</c> — reported loop sets, block by block — because that is the only place
/// the property exists. If a future change gives <c>NaturalLoops</c> a real consumer, this
/// fixture stops being the only guard; until then it is.</para>
///
/// <para>⭐ <b>THE CORPUS IS SHARED.</b> <see cref="CfgLoopShapes"/> holds all 13 shapes with
/// their expected block names, expected loop sets AND expected stdout, and
/// <see cref="CfgLoopShapesAggressiveTests"/> asserts the VALUES of the very same 13 programs on
/// four backends under both pipelines (INV-3). One corpus, two properties: a shape cannot be
/// edited for one fixture and silently diverge for the other.</para>
///
/// <para>⚠ <b>EVERY SHAPE ROUTES ITS VALUES THROUGH A CALL</b> — <c>Bound()</c> for the loop
/// bound, <c>Seed()</c> for an accumulator, <c>Show()</c> for the result. MEASURED elsewhere in
/// this suite: constant folding collapses a loop whose bound AND body are literals, which
/// destroys the control shape the fixture exists to describe and leaves a passing control that
/// proves nothing. The two shapes with LITERAL bounds, <c>A12</c> and <c>A13</c>, are deliberate
/// — <c>LoopUnrollingPass</c> needs a constant trip count and <c>LoopFusionPass</c> needs literal
/// bounds to fire at all — and they keep their accumulators off literals with <c>Seed()</c>.</para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class CfgNaturalLoopTests
{
    // ====================================================================================
    // INV-1, the whole of it, shape by shape.
    // ====================================================================================

    /// <summary>
    /// ⛔ <b>THE GUARD ON THE ONE-TOKEN FIX — the assertion that reverting it is not free.</b>
    /// The reported natural loops for <c>Main</c> are exactly these block sets, named.
    ///
    /// <para>MEASURED with the predicate put back the wrong way round: A01 reports FOUR loops
    /// instead of one — <c>[entry for0.cond]</c>, <c>[entry for0.body for0.cond for0.inc]</c>
    /// twice and <c>[entry for0.body for0.cond for0.end for0.inc]</c> — A02 reports EIGHT instead
    /// of two, and the two loop-free shapes A09/A10 report TWO each instead of zero. Twelve of the
    /// thirteen rows below go red. (A11, a single-block <c>Main</c>, reports zero either way: it
    /// has no edges at all, so it is a control, not a guard. It is kept because a corpus that
    /// silently dropped its degenerate case would not say so.)</para>
    ///
    /// <para>⚠ The expected block-name list is asserted too, and that is the non-vacuity check:
    /// if <c>IRBuilder</c> renames or restructures a lowering, this fails on the NAMES rather
    /// than passing against a corpus that no longer contains the shape it claims to.</para>
    /// </summary>
    [Test]
    [TestCaseSource(typeof(CfgLoopShapes), nameof(CfgLoopShapes.Names))]
    public void TheNaturalLoopsOfMain_AreExactlyTheseBlockSets(string shapeName)
    {
        var shape = CfgLoopShapes.Get(shapeName);
        var cfg = MainCfg(shape);

        Assert.Multiple(() =>
        {
            Assert.That(cfg.Function.Blocks.Select(b => b.Name), Is.EqualTo(shape.MainBlocks),
                "the shape itself changed — Main no longer lowers to the blocks this corpus "
                + "describes, so every expectation below is about a program that no longer exists. "
                + "Re-derive the corpus before touching the assertions.");

            Assert.That(Sets(cfg.NaturalLoops), Is.EquivalentTo(shape.Loops.Select(l => string.Join(" ", l))),
                $"reported loop sets for {shapeName} are not the natural loops of this CFG. "
                + $"ADR-0003 INV-1: FindBackEdges yields (tail, head) iff head ∈ tail.Dominators. "
                + $"Got:\n  {string.Join("\n  ", Sets(cfg.NaturalLoops))}");
        });
    }

    /// <summary>
    /// ⛔ INV-1 AS THE BICONDITIONAL IT IS, against an INDEPENDENT dominator computation.
    ///
    /// <para><see cref="TheNaturalLoopsOfMain_AreExactlyTheseBlockSets"/> pins the ANSWER; this
    /// pins the RULE, over every edge of every function of every shape — not just <c>Main</c>.
    /// An edge <c>tail → head</c> is reported as a back edge if and only if <c>head</c> dominates
    /// <c>tail</c>. Both directions are asserted: the defect satisfied the "only if" half
    /// vacuously by reporting the complement, so a one-directional check would have passed it.</para>
    ///
    /// <para>⚠ The dominator sets are RECOMPUTED here by <see cref="IndependentDominators"/>
    /// rather than read off <c>block.Dominators</c>. Reading the shipped field would make this a
    /// restatement of <c>FindBackEdges</c>'s own input and it could not distinguish a wrong
    /// predicate from a wrong dominator set. ADR-0003 records <c>ComputeDominators</c> as
    /// measured-correct on all 13 shapes; this is the assertion that keeps that true.</para>
    ///
    /// <para>⚠ ONE test over the WHOLE corpus rather than one per shape, because the non-vacuity
    /// check ("some edge was examined") is a statement about the corpus and not about a shape:
    /// <c>A11_singleblock</c> has a single-block <c>Main</c> and a single-block <c>Bound</c>, so
    /// it genuinely contributes ZERO edges and a per-shape guard would fail it for being exactly
    /// what it is meant to be. The per-edge report below names the shape, so a failure is still
    /// localized.</para>
    /// </summary>
    [Test]
    public void ABackEdgeIsReported_ExactlyWhenTheHeadDominatesTheTail_AcrossTheWholeCorpus()
    {
        int edgesSeen = 0;
        int backEdgesSeen = 0;
        var mismatches = new List<string>();

        foreach (var shapeName in CfgLoopShapes.Names)
        {
            var shape = CfgLoopShapes.Get(shapeName);
            var module = JsTestSupport.BuildModule(shape.Source, sourceFilePath: "prog.bas");

            foreach (var function in module.Functions.Where(f => !f.IsExternal))
            {
                var cfg = new ControlFlowGraph(function);
                cfg.Build();
                cfg.ComputeDominators();

                var dom = IndependentDominators(cfg);
                var reported = new HashSet<(BasicBlock, BasicBlock)>(cfg.FindBackEdges()
                    .Select(e => (e.From, e.To)));
                backEdgesSeen += reported.Count;

                foreach (var tail in function.Blocks)
                {
                    if (!dom.ContainsKey(tail)) continue;   // unreachable: no path, no dominance
                    foreach (var head in tail.Successors)
                    {
                        edgesSeen++;
                        bool shouldBeBackEdge = dom[tail].Contains(head);
                        bool isReported = reported.Contains((tail, head));
                        if (shouldBeBackEdge != isReported)
                        {
                            mismatches.Add($"{shapeName} / {function.Name}: {tail.Name} -> {head.Name} "
                                + $"headDominatesTail={shouldBeBackEdge} reportedAsBackEdge={isReported}");
                        }
                    }
                }
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(edgesSeen, Is.GreaterThan(0),
                "no CFG edge was examined at all, so the assertion below is vacuous");
            Assert.That(backEdgesSeen,
                Is.EqualTo(CfgLoopShapes.Names.Sum(n => CfgLoopShapes.Get(n).Loops.Length)),
                "the number of back edges found across the whole corpus does not match the number "
                + "of loops the corpus says it contains. Without this the biconditional could hold "
                + "for free by reporting NOTHING — which is the failure mode the corpus's two "
                + "must-report-zero shapes exist to distinguish from the real answer.");
            Assert.That(mismatches, Is.Empty,
                "ADR-0003 INV-1, as a biconditional over every edge: FindBackEdges must report "
                + "tail -> head exactly when head ∈ tail.Dominators. Disagreements:\n  "
                + string.Join("\n  ", mismatches));
        });
    }

    /// <summary>
    /// No reported loop contains the function's <c>entry</c> block.
    ///
    /// <para>The single most legible symptom of the inverted predicate: the defect reported
    /// <c>entry</c> inside every loop of every shape that had one, which is what made LICM
    /// resolve a loop's "preheader" to its own latch. Entry has no predecessor, so it cannot be
    /// inside any cycle; a loop containing it is a proof the analysis is wrong, independent of
    /// which sets are right.</para>
    /// </summary>
    [Test]
    [TestCaseSource(typeof(CfgLoopShapes), nameof(CfgLoopShapes.Names))]
    public void NoNaturalLoopContainsTheEntryBlock(string shapeName)
    {
        var cfg = MainCfg(CfgLoopShapes.Get(shapeName));

        Assert.That(cfg.NaturalLoops.Where(l => l.Contains(cfg.EntryBlock)).Select(Set), Is.Empty,
            "a natural loop contains `entry`, which has no predecessor and therefore lies on no "
            + "cycle. ADR-0003 INV-1.");
    }

    /// <summary>
    /// ⛔ No loop contains the <c>.end</c> block OF ITS OWN HEAD — and the anchoring is the
    /// point, not an implementation detail.
    ///
    /// <para>⚠ MEASURED: the obvious spelling of this check — "no loop contains any block named
    /// <c>*.end</c>" — fails FOUR CORRECT shapes. A02's outer loop legitimately contains
    /// <c>for1.end</c> (the inner loop's exit block really is inside the outer body, and it really
    /// does run once per outer iteration); A06/A07 legitimately contain <c>if0.end</c>; A08
    /// legitimately contains <c>try0.end</c>. The property that actually holds is that a loop
    /// headed at <c>for0.cond</c> must not contain <c>for0.end</c>, because <c>for0.end</c> is
    /// where control goes when the loop is OVER. So the check is anchored to the head's own name,
    /// and the expected sets in
    /// <see cref="TheNaturalLoopsOfMain_AreExactlyTheseBlockSets"/> carry <c>for1.end</c> for A02
    /// deliberately.</para>
    /// </summary>
    [Test]
    [TestCaseSource(typeof(CfgLoopShapes), nameof(CfgLoopShapes.Names))]
    public void NoNaturalLoopContainsTheEndBlockOfItsOwnHead(string shapeName)
    {
        var cfg = MainCfg(CfgLoopShapes.Get(shapeName));
        var backEdges = cfg.FindBackEdges();

        Assert.That(backEdges, Has.Count.EqualTo(cfg.NaturalLoops.Count),
            "IdentifyLoops produces one loop per back edge in back-edge order; the pairing below "
            + "depends on that and the counts have diverged");

        var violations = new List<string>();
        for (int i = 0; i < cfg.NaturalLoops.Count; i++)
        {
            var head = backEdges[i].To;
            var loop = cfg.NaturalLoops[i];

            Assert.That(loop, Does.Contain(head),
                $"loop #{i} does not contain its own head {head.Name} — the pairing is wrong");

            int dot = head.Name.LastIndexOf('.');
            if (dot < 0) continue;
            var ownEnd = head.Name.Substring(0, dot) + ".end";
            if (loop.Any(b => b.Name == ownEnd))
                violations.Add($"loop headed at {head.Name} contains {ownEnd}: {Set(loop)}");
        }

        Assert.That(violations, Is.Empty,
            "a loop contains the exit block of its own header, so the analysis has swept in the "
            + "code that runs AFTER the loop. ADR-0003 INV-1.\n  " + string.Join("\n  ", violations));
    }

    /// <summary>
    /// ⭐ THE NAMED SHAPE FROM ADR-0003's CONTRACT, spelled out on its own so it cannot be lost in
    /// a table: ONE counted <c>For</c> gives EXACTLY ONE loop, whose blocks are exactly the
    /// condition, the body and the increment — with <c>entry</c> absent and <c>for0.end</c>
    /// absent.
    ///
    /// <para>This is the assertion the whole of D1 reduces to. At the defect it reported four
    /// loops; three of the four differed from this by including <c>entry</c>, and the fourth by
    /// including <c>for0.end</c> as well.</para>
    /// </summary>
    [Test]
    public void ASingleCountedFor_HasExactlyOneLoop_OfConditionBodyAndIncrement()
    {
        var cfg = MainCfg(CfgLoopShapes.Get("A01_for_single"));

        Assert.Multiple(() =>
        {
            Assert.That(cfg.NaturalLoops, Has.Count.EqualTo(1),
                "one counted For is one natural loop. Got: " + string.Join(" | ", Sets(cfg.NaturalLoops)));
            Assert.That(Set(cfg.NaturalLoops[0]), Is.EqualTo("for0.body for0.cond for0.inc"));
            Assert.That(cfg.NaturalLoops[0].Select(b => b.Name), Does.Not.Contain("entry"),
                "entry has no predecessor and cannot lie on a cycle");
            Assert.That(cfg.NaturalLoops[0].Select(b => b.Name), Does.Not.Contain("for0.end"),
                "for0.end is where control goes when the loop is over");
        });
    }

    // ====================================================================================
    // INV-4: structured IR nodes are OPAQUE, and zero is the right answer for them.
    // ====================================================================================

    /// <summary>
    /// ⛔ <b>INV-4, MADE ENFORCEABLE.</b> ADR-0003 D3 says a <c>For Each</c> contributes no
    /// natural loop and that this is the CORRECT answer rather than a gap. The doc comment on
    /// <see cref="ControlFlowGraph.NaturalLoops"/> states the consequence for the next reader —
    /// "may NOT treat 'this block is in no loop' as 'this block executes once'" — and a doc
    /// comment is not a guard. This test is the guard, and it works by exhibiting a
    /// counterexample to the forbidden inference rather than by restating the rule:
    /// <c>foreach0.body</c> is in NO natural loop, and the SAME program, RUN, prints two
    /// <c>S=</c> lines, so that block executed twice.
    ///
    /// <para>Any future loop pass that reads "no loop" as "runs once" — to hoist into a block, or
    /// to compute a trip count for it — is unsound against exactly this program, and this test
    /// is what a reader who wants to write such a pass has to argue with.</para>
    ///
    /// <para>⚠ The zero-loop half is also the OTHER half of the M1 guard: with the predicate
    /// reverted this shape reports TWO loops, <c>[entry foreach0.body]</c> and
    /// <c>[entry foreach0.end]</c>, so the defect invented loops for a construct that has none.</para>
    ///
    /// <para>⚠ Not <c>[Category("Integration")]</c>: the run leg is the in-process Roslyn C#
    /// harness, which compiles and runs without a toolchain or a child process. Repo precedent is
    /// <c>CSharpRightReceiverTests.Right_WithANegativeLength_Throws</c>, which uses the same
    /// harness and is in the fast subset. Keeping it there matters — INV-4 is the invariant a
    /// future loop pass is most likely to violate, and it should be visible in the 2-minute
    /// run.</para>
    /// </summary>
    [Test]
    public void AForEachBodyIsInNoNaturalLoop_AndStillExecutesMoreThanOnce()
    {
        var shape = CfgLoopShapes.Get("A09_foreach");
        var cfg = MainCfg(shape);

        var body = cfg.Function.Blocks.SingleOrDefault(b => b.Name == "foreach0.body");

        Assert.Multiple(() =>
        {
            Assert.That(body, Is.Not.Null,
                "the For Each no longer lowers to a `foreach0.body` block, so this test is "
                + "asserting about a shape that is gone");
            Assert.That(cfg.NaturalLoops, Is.Empty,
                "a For Each lowers to a structured IRForEach node whose edges Build() wires "
                + "FORWARD only. Zero natural loops is the CORRECT answer (ADR-0003 D3), not a "
                + "gap. Got: " + string.Join(" | ", Sets(cfg.NaturalLoops)));
            Assert.That(cfg.NaturalLoops.Any(l => l.Contains(body!)), Is.False,
                "foreach0.body is in no natural loop");

            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(shape.Source)),
                Is.EqualTo(shape.Expected),
                "…and the same program prints TWO S= lines, so the block that is in no loop ran "
                + "TWICE. That is the counterexample INV-4 exists for: absence from NaturalLoops "
                + "is not evidence that a block executes once.");
        });
    }

    /// <summary>
    /// The other two zero-loop shapes, for completeness of the "zero is an answer, not a failure"
    /// half of INV-1: a function whose only control flow is an <c>If</c>, and a function with a
    /// single block.
    ///
    /// <para>⚠ Only the first is a guard. MEASURED with the back-edge predicate reverted, the
    /// <c>If</c> shape reports TWO loops — <c>[entry if0.then]</c> and <c>[entry if0.else]</c> —
    /// because the inverted test selects forward edges, and a plain two-armed branch is enough to
    /// produce them. The single-block function reports zero either way: it has no edges at all,
    /// so it is a labelled CONTROL, kept because a corpus that silently dropped its degenerate
    /// case would be claiming less than it covers.</para>
    /// </summary>
    [Test]
    [TestCase("A10_noloop", TestName = "AnIfWithNoLoop_ReportsZeroNaturalLoops")]
    [TestCase("A11_singleblock", TestName = "ASingleBlockFunction_ReportsZeroNaturalLoops")]
    public void ALoopFreeFunction_ReportsZeroNaturalLoops(string shapeName)
    {
        var cfg = MainCfg(CfgLoopShapes.Get(shapeName));

        Assert.Multiple(() =>
        {
            Assert.That(cfg.Function.Blocks, Is.Not.Empty, "no blocks — the assertion is vacuous");
            Assert.That(cfg.NaturalLoops, Is.Empty,
                "a loop-free function has no back edge and therefore no natural loop. Got: "
                + string.Join(" | ", Sets(cfg.NaturalLoops)));
        });
    }

    // ====================================================================================
    // Helpers.
    // ====================================================================================

    /// <summary>Front end + CFG for the shape's <c>Main</c>, built the way every caller does.</summary>
    private static ControlFlowGraph MainCfg(CfgLoopShapes.Shape shape)
    {
        var module = JsTestSupport.BuildModule(shape.Source, sourceFilePath: "prog.bas");
        var main = module.Functions.SingleOrDefault(f => f.Name == "Main" && !f.IsExternal);
        Assert.That(main, Is.Not.Null, "the shape has no Main function");

        var cfg = new ControlFlowGraph(main!);
        cfg.Build();
        cfg.ComputeDominators();
        cfg.IdentifyLoops();
        return cfg;
    }

    private static string Set(IEnumerable<BasicBlock> loop) =>
        string.Join(" ", loop.Select(b => b.Name).OrderBy(n => n, StringComparer.Ordinal));

    private static List<string> Sets(IEnumerable<List<BasicBlock>> loops) =>
        loops.Select(Set).ToList();

    /// <summary>
    /// Dominators recomputed from scratch, by the textbook fixed point
    /// <c>Dom(n) = {n} ∪ (⋂ Dom(p) over predecessors p)</c> seeded with <c>Dom(entry) = {entry}</c>,
    /// over the blocks REACHABLE from entry.
    ///
    /// <para>Deliberately a second implementation rather than a read of <c>block.Dominators</c>:
    /// the point of
    /// <see cref="ABackEdgeIsReported_ExactlyWhenTheHeadDominatesTheTail_AcrossTheWholeCorpus"/>
    /// is to check <c>FindBackEdges</c> against an oracle it does not share code with. Unreachable
    /// blocks are excluded rather than given the all-blocks set, because "every path from entry"
    /// is vacuous for them and the two conventions would disagree for no useful reason.</para>
    /// </summary>
    private static Dictionary<BasicBlock, HashSet<BasicBlock>> IndependentDominators(ControlFlowGraph cfg)
    {
        var entry = cfg.EntryBlock;
        var result = new Dictionary<BasicBlock, HashSet<BasicBlock>>();
        if (entry == null) return result;

        var reachable = new HashSet<BasicBlock>();
        var stack = new Stack<BasicBlock>();
        stack.Push(entry);
        while (stack.Count > 0)
        {
            var b = stack.Pop();
            if (!reachable.Add(b)) continue;
            foreach (var s in b.Successors) stack.Push(s);
        }

        foreach (var b in reachable)
            result[b] = b == entry ? new HashSet<BasicBlock> { entry } : new HashSet<BasicBlock>(reachable);

        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var b in reachable)
            {
                if (b == entry) continue;
                HashSet<BasicBlock> next = null;
                foreach (var p in b.Predecessors.Where(reachable.Contains))
                {
                    if (next == null) next = new HashSet<BasicBlock>(result[p]);
                    else next.IntersectWith(result[p]);
                }
                next ??= new HashSet<BasicBlock>();
                next.Add(b);
                if (!next.SetEquals(result[b])) { result[b] = next; changed = true; }
            }
        }

        return result;
    }
}

/// <summary>
/// ⭐ THE 13-SHAPE CFG CORPUS, shared by <see cref="CfgNaturalLoopTests"/> (INV-1/INV-4,
/// structural) and <see cref="CfgLoopShapesAggressiveTests"/> (INV-3, by value on four backends).
///
/// <para>Every shape carries its expected <c>Main</c> block names, its expected natural-loop sets
/// AND its expected stdout, so the two fixtures cannot drift about what a shape IS. The block
/// names and loop sets were derived by building each CFG and reading it; the stdout values are
/// the arithmetic truth and were confirmed against all four backends under both pipelines.</para>
///
/// <para>⚠ The corpus covers the loop forms BasicLang can lower, plus its three loop-free
/// controls: counted <c>For</c> (single, nested, sibling), <c>While</c>, <c>Do While</c>,
/// <c>Exit For</c>, <c>For</c>+<c>If</c>, <c>For</c>+<c>Try</c>, <c>For Each</c> (the structured
/// node, which contributes NO loop), a lone <c>If</c>, a single block, a counted loop over
/// literal bounds with no call in the body (the unrolling shape) and two sibling loops with the
/// SAME bound (the fusion shape — and the one that showed a silent wrong value at the defect:
/// 65 where 29 is correct).</para>
/// </summary>
internal static class CfgLoopShapes
{
    internal sealed class Shape
    {
        internal string Name = "";
        internal string Source = "";
        /// <summary>Expected block names of <c>Main</c>, in lowering order.</summary>
        internal string[] MainBlocks = Array.Empty<string>();
        /// <summary>Expected natural loops of <c>Main</c>, each as its block names sorted ordinally.</summary>
        internal string[][] Loops = Array.Empty<string[]>();
        /// <summary>Expected stdout, newline-normalized and trimmed.</summary>
        internal string Expected = "";
    }

    internal static IEnumerable<string> Names => All.Select(s => s.Name);

    internal static Shape Get(string name)
    {
        var shape = All.SingleOrDefault(s => s.Name == name);
        Assert.That(shape, Is.Not.Null, "no CFG corpus shape named " + name);
        return shape!;
    }

    /// <summary>
    /// The printing sink most shapes share. ⚠ Load-bearing: routing the loop's value through a
    /// CALL is what stops constant folding collapsing the control shape into a literal and leaving
    /// a passing control that proves nothing. The ten shapes that pair it with a call-valued bound
    /// (<c>Bound()</c>) are literal-free throughout; <c>A12</c>/<c>A13</c> deliberately use LITERAL
    /// bounds — <c>LoopUnrollingPass</c> needs a constant trip count and <c>LoopFusionPass</c>
    /// returns null from <c>GetLoopBounds</c> without one — and keep their accumulators off
    /// literals with <c>Seed()</c> instead. <c>A11</c> has no loop and no sink at all.
    /// </summary>
    private const string Preamble = """
        Sub Show(v As Integer)
         PrintLine("S=" & CStr(v))
        End Sub
        """;

    private static readonly Shape[] All =
    {
        new Shape
        {
            Name = "A01_for_single",
            Source = Preamble + """

                Function Bound() As Integer
                 Return 3
                End Function
                Sub Main()
                 Dim n As Integer = Bound()
                 For i As Integer = 0 To n
                  Show(i)
                 Next
                 PrintLine("DONE")
                End Sub
                """,
            MainBlocks = new[] { "entry", "for0.cond", "for0.body", "for0.inc", "for0.end" },
            Loops = new[] { new[] { "for0.body", "for0.cond", "for0.inc" } },
            Expected = "S=0\nS=1\nS=2\nS=3\nDONE",
        },
        new Shape
        {
            Name = "A02_for_nested",
            Source = Preamble + """

                Function Bound() As Integer
                 Return 3
                End Function
                Sub Main()
                 Dim n As Integer = Bound()
                 For i As Integer = 0 To n
                  For j As Integer = 0 To n
                   Show(i + j)
                  Next
                 Next
                 PrintLine("DONE")
                End Sub
                """,
            MainBlocks = new[] { "entry", "for0.cond", "for0.body", "for0.inc", "for0.end",
                                 "for1.cond", "for1.body", "for1.inc", "for1.end" },
            // ⚠ The OUTER loop contains the INNER loop's `for1.end` — correctly: the inner loop's
            // exit block is inside the outer body and runs once per outer iteration. This is the
            // row that breaks an unanchored "no loop holds a *.end block" check.
            Loops = new[]
            {
                new[] { "for0.body", "for0.cond", "for0.inc", "for1.body", "for1.cond", "for1.end", "for1.inc" },
                new[] { "for1.body", "for1.cond", "for1.inc" },
            },
            Expected = "S=0\nS=1\nS=2\nS=3\nS=1\nS=2\nS=3\nS=4\nS=2\nS=3\nS=4\nS=5\n"
                     + "S=3\nS=4\nS=5\nS=6\nDONE",
        },
        new Shape
        {
            Name = "A03_for_siblings",
            Source = Preamble + """

                Function Bound() As Integer
                 Return 3
                End Function
                Sub Main()
                 Dim n As Integer = Bound()
                 For i As Integer = 0 To n
                  Show(i)
                 Next
                 For k As Integer = 0 To n
                  Show(k)
                 Next
                 PrintLine("DONE")
                End Sub
                """,
            MainBlocks = new[] { "entry", "for0.cond", "for0.body", "for0.inc", "for0.end",
                                 "for1.cond", "for1.body", "for1.inc", "for1.end" },
            // ⛔ TWO DISJOINT loops. At the defect the bogus sets SPANNED both, which is what let
            // LICM move loop 0's body into loop 1's latch.
            Loops = new[]
            {
                new[] { "for0.body", "for0.cond", "for0.inc" },
                new[] { "for1.body", "for1.cond", "for1.inc" },
            },
            Expected = "S=0\nS=1\nS=2\nS=3\nS=0\nS=1\nS=2\nS=3\nDONE",
        },
        new Shape
        {
            Name = "A04_while",
            Source = Preamble + """

                Function Bound() As Integer
                 Return 3
                End Function
                Sub Main()
                 Dim n As Integer = Bound()
                 Dim i As Integer = 0
                 While i <= n
                  Show(i)
                  i = i + 1
                 End While
                 PrintLine("DONE")
                End Sub
                """,
            MainBlocks = new[] { "entry", "while0.cond", "while0.body", "while0.end" },
            Loops = new[] { new[] { "while0.body", "while0.cond" } },
            Expected = "S=0\nS=1\nS=2\nS=3\nDONE",
        },
        new Shape
        {
            Name = "A05_do",
            Source = Preamble + """

                Function Bound() As Integer
                 Return 3
                End Function
                Sub Main()
                 Dim n As Integer = Bound()
                 Dim i As Integer = 0
                 Do While i <= n
                  Show(i)
                  i = i + 1
                 Loop
                 PrintLine("DONE")
                End Sub
                """,
            MainBlocks = new[] { "entry", "do0.cond", "do0.body", "do0.end" },
            Loops = new[] { new[] { "do0.body", "do0.cond" } },
            Expected = "S=0\nS=1\nS=2\nS=3\nDONE",
        },
        new Shape
        {
            Name = "A06_for_exit",
            Source = Preamble + """

                Function Bound() As Integer
                 Return 5
                End Function
                Sub Main()
                 Dim n As Integer = Bound()
                 For i As Integer = 0 To n
                  If i > 2 Then
                   Exit For
                  End If
                  Show(i)
                 Next
                 PrintLine("DONE")
                End Sub
                """,
            MainBlocks = new[] { "entry", "for0.cond", "for0.body", "for0.inc", "for0.end",
                                 "if0.then", "if0.end" },
            // ⚠ `if0.then` is the Exit For arm: it branches to for0.end, OUT of the loop, so it is
            // correctly NOT in the loop. `if0.end` is the fall-through and IS.
            Loops = new[] { new[] { "for0.body", "for0.cond", "for0.inc", "if0.end" } },
            Expected = "S=0\nS=1\nS=2\nDONE",
        },
        new Shape
        {
            Name = "A07_for_if",
            Source = Preamble + """

                Function Bound() As Integer
                 Return 4
                End Function
                Sub Main()
                 Dim n As Integer = Bound()
                 For i As Integer = 0 To n
                  If i > 1 Then
                   Show(i)
                  Else
                   Show(0)
                  End If
                 Next
                 PrintLine("DONE")
                End Sub
                """,
            MainBlocks = new[] { "entry", "for0.cond", "for0.body", "for0.inc", "for0.end",
                                 "if0.then", "if0.else", "if0.end" },
            Loops = new[]
            {
                new[] { "for0.body", "for0.cond", "for0.inc", "if0.else", "if0.end", "if0.then" },
            },
            Expected = "S=0\nS=0\nS=2\nS=3\nS=4\nDONE",
        },
        new Shape
        {
            Name = "A08_for_try",
            Source = Preamble + """

                Function Bound() As Integer
                 Return 3
                End Function
                Sub Main()
                 Dim n As Integer = Bound()
                 For i As Integer = 0 To n
                  Try
                   Show(i)
                  Catch ex As Exception
                   PrintLine("E")
                  End Try
                 Next
                 PrintLine("DONE")
                End Sub
                """,
            MainBlocks = new[] { "entry", "for0.cond", "for0.body", "for0.inc", "for0.end",
                                 "try0.body", "try0.end", "try0.catch0" },
            // ⚠ The Try contributes no loop of its own (IRTryCatch is structured, INV-4), but its
            // blocks ARE inside the For's loop, reached through the forward edges Build() wires.
            Loops = new[]
            {
                new[] { "for0.body", "for0.cond", "for0.inc", "try0.body", "try0.catch0", "try0.end" },
            },
            Expected = "S=0\nS=1\nS=2\nS=3\nDONE",
        },
        new Shape
        {
            Name = "A09_foreach",
            Source = Preamble + """

                Function Bound() As Integer
                 Return 3
                End Function
                Sub Main()
                 Dim xs As New List(Of Integer)
                 xs.Add(Bound())
                 xs.Add(7)
                 For Each v As Integer In xs
                  Show(v)
                 Next
                 PrintLine("DONE")
                End Sub
                """,
            MainBlocks = new[] { "entry", "foreach0.body", "foreach0.end" },
            // ⛔ ZERO, and that is the CORRECT answer — ADR-0003 D3 / INV-4. `foreach0.body` runs
            // twice here and is in no natural loop.
            Loops = Array.Empty<string[]>(),
            Expected = "S=3\nS=7\nDONE",
        },
        new Shape
        {
            Name = "A10_noloop",
            Source = Preamble + """

                Function Bound() As Integer
                 Return 3
                End Function
                Sub Main()
                 Dim n As Integer = Bound()
                 If n > 1 Then
                  Show(n)
                 Else
                  Show(0)
                 End If
                 PrintLine("DONE")
                End Sub
                """,
            MainBlocks = new[] { "entry", "if0.then", "if0.else", "if0.end" },
            Loops = Array.Empty<string[]>(),
            Expected = "S=3\nDONE",
        },
        new Shape
        {
            Name = "A11_singleblock",
            Source = """
                Function Bound() As Integer
                 Return 3
                End Function
                Sub Main()
                 Dim n As Integer = Bound()
                 PrintLine("N=" & CStr(n))
                End Sub
                """,
            MainBlocks = new[] { "entry" },
            Loops = Array.Empty<string[]>(),
            Expected = "N=3",
        },
        new Shape
        {
            Name = "A12_unrollable",
            // ⛔ THE UNROLLING SHAPE: literal bounds, no call in the body, so every gate
            // LoopUnrollingPass names is satisfied. This is the shape that shows unrolling damage
            // (`_u0__u0_i`, undeclared on all four backends).
            Source = """
                Sub Show(v As Integer)
                 PrintLine("S=" & CStr(v))
                End Sub
                Function Seed() As Integer
                 Return 1
                End Function
                Sub Main()
                 Dim acc As Integer = Seed()
                 For i As Integer = 0 To 7
                  acc = acc + i
                 Next
                 Show(acc)
                 PrintLine("DONE")
                End Sub
                """,
            MainBlocks = new[] { "entry", "for0.cond", "for0.body", "for0.inc", "for0.end" },
            Loops = new[] { new[] { "for0.body", "for0.cond", "for0.inc" } },
            // 1 + (0+1+…+7) = 29.
            Expected = "S=29\nDONE",
        },
        new Shape
        {
            Name = "A13_fusable",
            // ⛔ THE FUSION SHAPE, and the one that carries a SILENT WRONG VALUE at the defect:
            // two sibling loops with the SAME bound, each accumulating 0..7 onto its own variable.
            // At the defect the aggressive pipeline printed 65 on C# — the reference oracle — where
            // 29 is correct, and 29,37 with LoopFusion registered. `Seed()` keeps the accumulators
            // off literals so the folder cannot collapse either loop.
            Source = """
                Sub Show(v As Integer)
                 PrintLine("S=" & CStr(v))
                End Sub
                Function Seed() As Integer
                 Return 1
                End Function
                Sub Main()
                 Dim a As Integer = Seed()
                 Dim b As Integer = Seed()
                 For i As Integer = 0 To 7
                  a = a + i
                 Next
                 For j As Integer = 0 To 7
                  b = b + j
                 Next
                 Show(a)
                 Show(b)
                 PrintLine("DONE")
                End Sub
                """,
            MainBlocks = new[] { "entry", "for0.cond", "for0.body", "for0.inc", "for0.end",
                                 "for1.cond", "for1.body", "for1.inc", "for1.end" },
            Loops = new[]
            {
                new[] { "for0.body", "for0.cond", "for0.inc" },
                new[] { "for1.body", "for1.cond", "for1.inc" },
            },
            Expected = "S=29\nS=29\nDONE",
        },
    };
}
