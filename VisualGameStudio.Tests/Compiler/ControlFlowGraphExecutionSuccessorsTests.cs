using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;
using TypeInfo = BasicLang.Compiler.SemanticAnalysis.TypeInfo;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// ADR-0016 D3 (amended): <c>ControlFlowGraph.ExecutionSuccessors(function)</c> — the ONE shared
/// successor function under the C++ backend's captured-variable rule
/// (<c>CppCapabilityChecker.CheckLambdaCaptureWrites</c>). It is <c>SuccessorsOf</c>'s laid-out edges
/// PLUS the three families a structured node leaves implicit:
/// <list type="number">
/// <item><b>For Each</b>: the end of a body (a normal, non-<c>IsLoopExit</c> edge to the node's end
/// block) reaches the body entry — the next iteration, which the node repeats implicitly;</item>
/// <item><b>Try</b>: every block of the try region reaches each Catch block and the Finally block; every
/// block of a Catch region reaches the Finally block;</item>
/// <item><b>Finally</b>: the Finally block reaches the Try's end block.</item>
/// </list>
///
/// <para>These are unit tests on hand-built IR, so a family dropped or widened fails HERE by name rather
/// than as a silent wrong answer in a C++ program. The end-to-end witness for each family lives in
/// <c>BaseConstructorCallCppRefusalTests</c> (FE1 for the back edge; the PerIteration E03/E04/E07f for the
/// Try edges; CR1 for the creation instruction), and the mutation proof kills a mutant that drops each.</para>
///
/// <para>And the other half of the contract: it is a SEPARATE analysis edge set. <c>SuccessorsOf</c>,
/// <c>Build</c> and <c>IdentifyLoops</c> keep the laid-out edges — a back edge there would make a natural
/// loop DCE, LICM and the other passes do not expect — and are pinned unchanged below.</para>
/// </summary>
[TestFixture]
public class ControlFlowGraphExecutionSuccessorsTests
{
    private static readonly TypeInfo IntType = new TypeInfo("Integer", TypeKind.Primitive);
    private static readonly TypeInfo VoidType = new TypeInfo("Void", TypeKind.Primitive);

    private static IRFunction NewFunction(out BasicBlock entry)
    {
        var function = new IRFunction("F", VoidType);
        entry = function.CreateBlock("entry");
        function.EntryBlock = entry;
        return function;
    }

    private static void Goto(BasicBlock from, BasicBlock to, bool loopExit = false)
        => from.Instructions.Add(new IRBranch(to) { ParentBlock = from, IsLoopExit = loopExit });

    private static void Return(BasicBlock at)
        => at.Instructions.Add(new IRReturn(null) { ParentBlock = at });

    private static List<BasicBlock> Exec(IRFunction function, BasicBlock from)
        => ControlFlowGraph.ExecutionSuccessors(function)(from).ToList();

    // ============================================================================================
    // Family 1: For Each — end of body -> body entry, except on Exit
    // ============================================================================================

    /// <summary>entry: For Each(body = B, end = E); B falls through to E; E returns.</summary>
    private static (IRFunction F, BasicBlock Entry, BasicBlock Body, BasicBlock End) ForEachOneBlockBody(bool exitBranch = false)
    {
        var f = NewFunction(out var entry);
        var body = f.CreateBlock("body");
        var end = f.CreateBlock("end");
        var collection = new IRVariable("items", IntType);
        entry.Instructions.Add(new IRForEach("item", IntType, collection, body, end) { ParentBlock = entry });
        Goto(body, end, loopExit: exitBranch);
        Return(end);
        return (f, entry, body, end);
    }

    [Test]
    public void ForEach_TheEndOfTheBody_ReachesTheBodyEntry()
    {
        var (f, _, body, end) = ForEachOneBlockBody();

        var successors = Exec(f, body);

        Assert.That(successors, Has.Some.SameAs(end), "the laid-out edge stays");
        Assert.That(successors, Has.Some.SameAs(body), "the NEXT ITERATION: end of body -> body entry");
    }

    /// <summary>An Exit is not an iteration: the branch is marked <c>IsLoopExit</c>, so there is no
    /// edge back into the body from it.</summary>
    [Test]
    public void ForEach_AnExitBranch_DoesNotReachTheBodyEntry()
    {
        var (f, _, body, end) = ForEachOneBlockBody(exitBranch: true);

        var successors = Exec(f, body);

        Assert.That(successors, Has.Some.SameAs(end));
        Assert.That(successors, Has.None.SameAs(body), "Exit For does not start another iteration");
    }

    /// <summary>A body of TWO blocks: only the block that ENDS the iteration (the one whose edge goes
    /// to the node's end) gets the back edge; the first block just continues to the second.</summary>
    [Test]
    public void ForEach_OnlyTheBlockThatEndsAnIteration_GetsTheBackEdge()
    {
        var f = NewFunction(out var entry);
        var first = f.CreateBlock("body1");
        var second = f.CreateBlock("body2");
        var end = f.CreateBlock("end");
        entry.Instructions.Add(new IRForEach("item", IntType, new IRVariable("items", IntType), first, end) { ParentBlock = entry });
        Goto(first, second);
        Goto(second, end);
        Return(end);

        Assert.That(Exec(f, first), Has.None.SameAs(first), "body1 only continues to body2");
        Assert.That(Exec(f, second), Has.Some.SameAs(first), "body2 ends the iteration: -> body entry");
        Assert.That(Exec(f, second), Has.Some.SameAs(end));
    }

    // ============================================================================================
    // Families 2 and 3: Try -> Catch / Finally; Catch -> Finally; Finally -> end
    // ============================================================================================

    /// <summary>entry: Try(T; Catch C; Finally F; end E). T, C and F each just branch to E.</summary>
    private static (IRFunction F, BasicBlock Entry, BasicBlock Try, BasicBlock Catch, BasicBlock Finally, BasicBlock End) TryCatchFinally()
    {
        var f = NewFunction(out var entry);
        var tryBlock = f.CreateBlock("try");
        var catchBlock = f.CreateBlock("catch");
        var finallyBlock = f.CreateBlock("finally");
        var end = f.CreateBlock("end");
        var clause = new IRCatchClause(new TypeInfo("Exception", TypeKind.Class), "ex", catchBlock);
        entry.Instructions.Add(new IRTryCatch(tryBlock, new List<IRCatchClause> { clause }, finallyBlock, end) { ParentBlock = entry });
        Goto(tryBlock, end);
        Goto(catchBlock, end);
        Goto(finallyBlock, end);
        Return(end);
        return (f, entry, tryBlock, catchBlock, finallyBlock, end);
    }

    [Test]
    public void Try_EveryBlockOfTheTryRegion_ReachesTheCatchAndTheFinally()
    {
        var (f, _, tryBlock, catchBlock, finallyBlock, _) = TryCatchFinally();

        var successors = Exec(f, tryBlock);

        Assert.That(successors, Has.Some.SameAs(catchBlock), "an exception reaches the Catch");
        Assert.That(successors, Has.Some.SameAs(finallyBlock), "every exit reaches the Finally");
    }

    /// <summary>
    /// The contract's third Try edge: "every try and catch block → the Catch and Finally blocks", so a
    /// Catch region reaches the Finally. The Catch block is a STOP block for its own region (the search
    /// for one Catch's region must not wander into another Catch, the Finally or the end), and a region
    /// that stopped at its own entry was empty — no Catch → Finally edge was ever added, and a lambda
    /// created in a Catch escaped a Finally that writes its capture (a silent wrong answer on C++). A
    /// region now always contains its own entry block. Killed by the mutant "Catch → Finally edge
    /// dropped"; the end-to-end witness is <c>BaseConstructorCallCppRefusalTests.CF1_…</c>.
    /// </summary>
    [Test]
    public void Catch_EveryBlockOfTheCatchRegion_ReachesTheFinally()
    {
        var (f, _, _, catchBlock, finallyBlock, end) = TryCatchFinally();

        var successors = Exec(f, catchBlock);

        Assert.That(successors, Has.Some.SameAs(end), "the laid-out edge stays");
        Assert.That(successors, Has.Some.SameAs(finallyBlock), "the Catch block reaches the Finally");
    }

    /// <summary>A Catch region of TWO blocks: BOTH reach the Finally (a region is what
    /// <c>SuccessorsOf</c> reaches from the Catch block without passing through the node's end,
    /// Finally or another Catch block), and a SECOND Catch clause is not swallowed into the first's
    /// region.</summary>
    [Test]
    public void Catch_ACatchRegionOfTwoBlocks_AndTwoCatchClauses_EachReachTheFinally()
    {
        var f = NewFunction(out var entry);
        var t = f.CreateBlock("try");
        var c1a = f.CreateBlock("catch1a");
        var c1b = f.CreateBlock("catch1b");
        var c2 = f.CreateBlock("catch2");
        var fin = f.CreateBlock("finally");
        var end = f.CreateBlock("end");
        var clauses = new List<IRCatchClause>
        {
            new IRCatchClause(new TypeInfo("Exception", TypeKind.Class), "e1", c1a),
            new IRCatchClause(new TypeInfo("Exception", TypeKind.Class), "e2", c2),
        };
        entry.Instructions.Add(new IRTryCatch(t, clauses, fin, end) { ParentBlock = entry });
        Goto(t, end);
        Goto(c1a, c1b);
        Goto(c1b, end);
        Goto(c2, end);
        Goto(fin, end);
        Return(end);

        Assert.That(Exec(f, c1a), Has.Some.SameAs(fin), "the first Catch's entry");
        Assert.That(Exec(f, c1b), Has.Some.SameAs(fin), "the first Catch's second block");
        Assert.That(Exec(f, c2), Has.Some.SameAs(fin), "the second Catch clause");
        Assert.That(Exec(f, c1b), Has.None.SameAs(c2), "the second Catch is not part of the first's region");
    }

    [Test]
    public void Finally_ReachesTheEndOfTheTry()
    {
        var (f, _, _, _, finallyBlock, end) = TryCatchFinally();

        Assert.That(Exec(f, finallyBlock), Has.Some.SameAs(end));
    }

    /// <summary>The regions are what <c>SuccessorsOf</c> reaches from their entry WITHOUT passing
    /// through the node's end / Finally / Catch blocks — so a try region of TWO blocks gives BOTH the
    /// edges.</summary>
    [Test]
    public void Try_ATryRegionOfTwoBlocks_GivesBothTheEdges()
    {
        var f = NewFunction(out var entry);
        var t1 = f.CreateBlock("try1");
        var t2 = f.CreateBlock("try2");
        var fin = f.CreateBlock("finally");
        var end = f.CreateBlock("end");
        entry.Instructions.Add(new IRTryCatch(t1, new List<IRCatchClause>(), fin, end) { ParentBlock = entry });
        Goto(t1, t2);
        Goto(t2, end);
        Goto(fin, end);
        Return(end);

        Assert.That(Exec(f, t1), Has.Some.SameAs(fin));
        Assert.That(Exec(f, t2), Has.Some.SameAs(fin));
    }

    // ============================================================================================
    // The other half of the contract: SuccessorsOf / Build / IdentifyLoops are UNCHANGED
    // ============================================================================================

    /// <summary><c>SuccessorsOf</c> is the laid-out edge rule and nothing else: the For Each body's
    /// end does NOT reach the body entry there, and the Try block does NOT reach its Finally.</summary>
    [Test]
    public void SuccessorsOf_KeepsTheLaidOutEdgesOnly()
    {
        var (_, _, body, end) = ForEachOneBlockBody();
        Assert.That(ControlFlowGraph.SuccessorsOf(body).ToList(), Is.EqualTo(new[] { end }).Using<BasicBlock>((a, b) => ReferenceEquals(a, b)));

        var (_, _, tryBlock, _, finallyBlock, tryEnd) = TryCatchFinally();
        var fromTry = ControlFlowGraph.SuccessorsOf(tryBlock).ToList();
        Assert.That(fromTry, Has.None.SameAs(finallyBlock), "SuccessorsOf(try) is its own branch only");
        Assert.That(fromTry, Has.Some.SameAs(tryEnd));
    }

    /// <summary><c>Build</c> writes only laid-out edges into <c>Successors</c>/<c>Predecessors</c>:
    /// calling <c>ExecutionSuccessors</c> before OR after changes nothing a pass will see.</summary>
    [Test]
    public void Build_WritesOnlyLaidOutEdges_AndExecutionSuccessorsDoesNotTouchThem()
    {
        var (f, _, body, end) = ForEachOneBlockBody();
        var cfg = new ControlFlowGraph(f);

        cfg.Build();
        var before = f.Blocks.Select(b => (b.Name, Succ: b.Successors.Select(s => s.Name).ToList(), Pred: b.Predecessors.Select(p => p.Name).ToList())).ToList();
        _ = Exec(f, body);
        cfg.Build();
        var after = f.Blocks.Select(b => (b.Name, Succ: b.Successors.Select(s => s.Name).ToList(), Pred: b.Predecessors.Select(p => p.Name).ToList())).ToList();

        Assert.That(after, Is.EqualTo(before));
        Assert.That(body.Successors, Has.None.SameAs(body), "Build did NOT add the iteration back edge");
        Assert.That(body.Successors, Has.Some.SameAs(end));
    }

    /// <summary><c>IdentifyLoops</c> reports NO natural loop for a function whose only loop is a For
    /// Each — the documented, CORRECT answer (<c>NaturalLoops</c>'s own remarks) that adding the back
    /// edge to <c>Build</c> would have broken; the back edge exists only in
    /// <c>ExecutionSuccessors</c>.</summary>
    [Test]
    public void IdentifyLoops_StillReportsNoLoopForAForEach()
    {
        var (f, _, _, _) = ForEachOneBlockBody();
        var cfg = new ControlFlowGraph(f);
        cfg.Build();
        cfg.IdentifyLoops();

        Assert.That(cfg.NaturalLoops, Is.Empty);
    }

    // ============================================================================================
    // Null-safety and the real-IR shape
    // ============================================================================================

    [Test]
    public void ANullFunctionOrBlock_YieldsNoSuccessors()
    {
        Assert.That(ControlFlowGraph.ExecutionSuccessors(null)(null), Is.Empty);
    }

    /// <summary>The same three families from REAL IRBuilder output: E03's Try/Finally, and FE1's For
    /// Each, both from the C++ refusal fixture's own programs.</summary>
    [Test]
    public void RealIR_TheTryAndForEachFamiliesAreBothPresent()
    {
        var tryModule = JsTestSupport.BuildModule(PerIterationLoopBodyDimProbes.E03);
        var main = tryModule.Functions.First(fn => fn.Name == "Main");
        var tryNode = main.Blocks.SelectMany(b => b.Instructions).OfType<IRTryCatch>().First();
        var execution = ControlFlowGraph.ExecutionSuccessors(main);
        Assert.That(execution(tryNode.TryBlock), Has.Some.SameAs(tryNode.FinallyBlock), "try -> finally");
        Assert.That(execution(tryNode.FinallyBlock), Has.Some.SameAs(tryNode.EndBlock), "finally -> end");
        Assert.That(ControlFlowGraph.SuccessorsOf(tryNode.TryBlock), Has.None.SameAs(tryNode.FinallyBlock),
            "and SuccessorsOf(try) still does not");

        var feModule = JsTestSupport.BuildModule("""
            Sub Main()
                Dim items As New List(Of Integer)()
                items.Add(1)
                Dim x As Integer = 0
                For Each n As Integer In items
                    x = x + n
                Next
            End Sub
            """);
        var feMain = feModule.Functions.First(fn => fn.Name == "Main");
        var forEach = feMain.Blocks.SelectMany(b => b.Instructions).OfType<IRForEach>().First();
        var backEdges = feMain.Blocks.Where(b => ControlFlowGraph.ExecutionSuccessors(feMain)(b).Any(s => ReferenceEquals(s, forEach.BodyBlock))
                                                 && !ControlFlowGraph.SuccessorsOf(b).Any(s => ReferenceEquals(s, forEach.BodyBlock))).ToList();
        Assert.That(backEdges, Is.Not.Empty, "a For Each body's end reaches its body entry only in ExecutionSuccessors");
    }
}
