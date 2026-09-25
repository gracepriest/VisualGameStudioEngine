using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.JavaScript;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Loops under <c>--optimize</c> (<see cref="OptimizationPipeline.AddAggressivePasses"/>).
/// MEASURED on master 42a2280, with the right answer first:
/// <list type="bullet">
/// <item><c>For i = 1 To 5 : s = s + i * 3</c> — 45; C++ printed 0 (every For loop did).</item>
/// <item>a multiply inside an If in a loop — 120; JavaScript threw "t5 is not defined".</item>
/// <item>nested loops — 108; JavaScript threw "t7 is not defined".</item>
/// <item>two adjacent loops — 1020, 40, 48; JavaScript printed 2020, 30, 18.</item>
/// </list>
/// Three causes, fixed together because fixing the first makes the others live:
/// <c>ControlFlowGraph.FindBackEdges</c> tested dominance backwards, so every "loop" held the entry
/// block; <c>LoopInvariantCodeMotionPass</c> treated every local (the loop counter included) as
/// invariant and hoisted into a block that was not a preheader; and once loops were right,
/// <c>LoopUnrollingPass</c> and <c>LoopFusionPass</c> fired for real and broke nearly everything,
/// so they are out of the pipeline (reasons on the commented-out lines).
/// </summary>
[TestFixture]
public class LoopInvariantCodeMotionTests
{
    internal static readonly (string Name, string Body, string Expected)[] Programs =
    {
        ("for step 1", @"
    Dim s As Integer = 0
    For i As Integer = 1 To 5
        s = s + i * 3
    Next
    Console.WriteLine(CStr(s))", "45"),
        ("for step 2", @"
    Dim s As Integer = 0
    For i As Integer = 0 To 9 Step 2
        s = s + i * 5
    Next
    Console.WriteLine(CStr(s))", "100"),
        ("for step -1", @"
    Dim s As Integer = 0
    For i As Integer = 5 To 1 Step -1
        s = s + i * 2
    Next
    Console.WriteLine(CStr(s))", "30"),
        ("if in loop", @"
    Dim s As Integer = 0
    For i As Integer = 1 To 6
        If i Mod 2 = 0 Then
            s = s + i * 10
        End If
    Next
    Console.WriteLine(CStr(s))", "120"),
        ("nested", @"
    Dim s As Integer = 0
    For i As Integer = 1 To 3
        For j As Integer = 1 To 3
            s = s + i * 4 + j * 2
        Next
    Next
    Console.WriteLine(CStr(s))", "108"),
        ("do while", @"
    Dim s As Integer = 0
    Dim i As Integer = 0
    Do While i < 4
        i = i + 1
        s = s + i * 6
    Loop
    Console.WriteLine(CStr(s))", "60"),
        ("while", @"
    Dim i As Integer = 0
    Dim s As Integer = 0
    While i < 5
        Dim k As Integer = i * 4
        s = s + k
        i = i + 1
    End While
    Console.WriteLine(CStr(s))", "40"),
        ("adjacent independent", @"
    Dim a As Integer = 0
    Dim b As Integer = 0
    For i As Integer = 1 To 4
        a = a + i
    Next
    For j As Integer = 1 To 4
        b = b + j * 2
    Next
    Console.WriteLine(CStr(a * 100 + b))", "1020"),
        ("adjacent dependent", @"
    Dim a As Integer = 0
    Dim b As Integer = 0
    For i As Integer = 1 To 4
        a = a + i
    Next
    For j As Integer = 1 To 4
        b = b + a
    Next
    Console.WriteLine(CStr(b))", "40"),
        ("adjacent same variable", @"
    Dim a As Integer = 0
    For i As Integer = 1 To 3
        a = a + i
    Next
    For i As Integer = 1 To 3
        a = a * 2
    Next
    Console.WriteLine(CStr(a))", "48"),
        // A truly invariant product, which SHOULD be hoisted — the pass must still do its job. The
        // operands come from calls so constant folding cannot erase the multiply first.
        ("invariant product", @"
    Dim x As Integer = Six()
    Dim y As Integer = Seven()
    Dim s As Integer = 0
    For i As Integer = 1 To 3
        s = s + x * y
    Next
    Console.WriteLine(CStr(s))", "126"),
        // A guarded division by zero must NOT be hoisted out of its If: it would throw.
        ("guarded division", @"
    Dim x As Integer = 10
    Dim z As Integer = 0
    Dim s As Integer = 0
    For i As Integer = 1 To 3
        If z <> 0 Then
            s = s + x \ z
        End If
        s = s + 1
    Next
    Console.WriteLine(CStr(s))", "3"),
    };

    // Six/Seven give "invariant product" values the optimizer cannot see (inlining is off).
    internal static string Program(string body) =>
        "Function Six() As Integer\n    Return 6\nEnd Function\n" +
        "Function Seven() As Integer\n    Return 7\nEnd Function\n" +
        "Sub Main()" + body + "\nEnd Sub\n";

    internal static IRModule BuildAggressive(string source)
    {
        var module = JsTestSupport.BuildModule(source, sourceFilePath: "prog.bas");
        var pipeline = new OptimizationPipeline();
        pipeline.AddAggressivePasses();
        pipeline.Run(module);
        return module;
    }

    private static IRFunction Main(IRModule module) => module.Functions.Single(f => f.Name == "Main");

    private static ControlFlowGraph Cfg(IRFunction fn)
    {
        var cfg = new ControlFlowGraph(fn);
        cfg.Build();
        cfg.ComputeDominators();
        cfg.IdentifyLoops();
        return cfg;
    }

    // ---- ControlFlowGraph ------------------------------------------------------------------

    /// <summary>A For loop has ONE back edge, inc -> cond, and a loop that is not the entry block.</summary>
    [Test]
    public void ForLoop_HasExactlyOneBackEdge_AndItsLoopExcludesEntry()
    {
        var fn = Main(JsTestSupport.BuildModule(Program(Programs[0].Body), sourceFilePath: "prog.bas"));
        var cfg = Cfg(fn);

        var backEdges = cfg.FindBackEdges();
        Assert.Multiple(() =>
        {
            Assert.That(backEdges.Select(e => (e.From.Name, e.To.Name)),
                Is.EquivalentTo(new[] { ("for0.inc", "for0.cond") }),
                "was every forward edge, entry->for0.cond included");
            Assert.That(cfg.NaturalLoops, Has.Count.EqualTo(1));
            Assert.That(cfg.NaturalLoops[0].Select(b => b.Name),
                Is.EquivalentTo(new[] { "for0.cond", "for0.body", "for0.inc" }));
            // NOTE (test/docs pass, 2026-09-24): this assertion is now vacuous and kept only as
            // a smoke check that IsReducible() still runs. After the back-edge orientation fix,
            // IsReducible() can never return false: it walks exactly the edges FindBackEdges()
            // just returned and re-checks tail.Dominators.Contains(head) — the identical
            // predicate FindBackEdges() already required to admit each edge in the first place
            // (see ControlFlowGraph.cs, both methods). So every edge it loops over already
            // satisfies the condition it is testing, and the loop body's `return false` is
            // unreachable from any input. This was not true before the fix: the old, inverted
            // FindBackEdges() predicate and the old IsReducible() check were NOT the same
            // condition, so IsReducible() could disagree with what FindBackEdges() had just
            // produced. See ADR-0003's Amendment: D4 (delete IsReducible) was reversed and it was
            // restored with this orientation fix specifically because this test calls it.
            Assert.That(cfg.IsReducible(), Is.True);
        });
    }

    [Test]
    public void NestedLoops_TwoLoops_InnerInsideOuter()
    {
        var fn = Main(JsTestSupport.BuildModule(Program(Programs[4].Body), sourceFilePath: "prog.bas"));
        var cfg = Cfg(fn);
        Assert.That(cfg.NaturalLoops, Has.Count.EqualTo(2));
        var (inner, outer) = cfg.NaturalLoops[0].Count < cfg.NaturalLoops[1].Count
            ? (cfg.NaturalLoops[0], cfg.NaturalLoops[1])
            : (cfg.NaturalLoops[1], cfg.NaturalLoops[0]);
        Assert.Multiple(() =>
        {
            Assert.That(inner.All(outer.Contains), Is.True, "the inner loop nests in the outer");
            Assert.That(outer.Any(b => b.Name == "entry"), Is.False);
        });
    }

    // ---- LICM decisions ----------------------------------------------------------------------

    /// <summary>The loop counter's update and test stay in the loop.</summary>
    [Test]
    public void LoopCounterArithmetic_IsNotHoisted()
    {
        var fn = Main(BuildAggressive(Program(Programs[0].Body)));
        var entry = fn.Blocks.Single(b => b.Name == "entry");
        Assert.That(entry.Instructions.OfType<IRCompare>(), Is.Empty,
            "the loop condition was hoisted out of the loop");
        Assert.That(entry.Instructions.OfType<IRBinaryOp>()
            .Where(op => op.Operation == BinaryOpKind.Add), Is.Empty,
            "the loop increment was hoisted out of the loop");
    }

    /// <summary>A product of two variables the loop never writes IS hoisted — the pass still works.</summary>
    [Test]
    public void TrulyInvariantProduct_IsHoistedToThePreheader()
    {
        var fn = Main(BuildAggressive(Program(Programs.Single(p => p.Name == "invariant product").Body)));
        var entry = fn.Blocks.Single(b => b.Name == "entry");
        Assert.That(entry.Instructions.OfType<IRBinaryOp>()
            .Any(op => op.Operation == BinaryOpKind.Mul), Is.True,
            "x * y should have moved to the preheader");
    }

    /// <summary>A division stays where the program put it, even when its operands are invariant.</summary>
    [Test]
    public void GuardedDivision_IsNotHoisted()
    {
        var fn = Main(BuildAggressive(Program(Programs.Single(p => p.Name == "guarded division").Body)));
        var entry = fn.Blocks.Single(b => b.Name == "entry");
        Assert.That(entry.Instructions.OfType<IRBinaryOp>()
            .Any(op => op.Operation == BinaryOpKind.IntDiv), Is.False);
    }

    // ---- the two passes taken out of the pipeline ----------------------------------------

    /// <summary>No unrolled clone (<c>_u0_</c>) in shipped --optimize output.</summary>
    [Test]
    public void Aggressive_DoesNotUnroll()
    {
        var cs = new ImprovedCSharpCodeGenerator().Generate(BuildAggressive(Program(Programs[0].Body)));
        Assert.That(cs, Does.Not.Contain("_u0_"), cs);
    }

    /// <summary>Two adjacent loops stay two loops in shipped --optimize output.</summary>
    [Test]
    public void Aggressive_DoesNotFuse()
    {
        var fn = Main(BuildAggressive(Program(Programs.Single(p => p.Name == "adjacent independent").Body)));
        Assert.That(Cfg(fn).NaturalLoops, Has.Count.EqualTo(2));
    }

    /// <summary>
    /// ⛔ STILL BROKEN when run directly, pinned so "disabled" stays a measured claim. If someone
    /// repairs a pass this goes RED — the signal to re-enable it and delete the pin, not to weaken it.
    /// </summary>
    [Test]
    public void RunDirectly_UnrollingStillRenamesTheUsersVariables()
    {
        var module = JsTestSupport.BuildModule(Program(Programs[0].Body), sourceFilePath: "prog.bas");
        var pipeline = new OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.AddPass(new LoopUnrollingPass(4));
        pipeline.Run(module);
        var fn = Main(module);
        var names = fn.Blocks.SelectMany(b => b.Instructions).OfType<IRAssignment>()
            .Select(a => (a.Target as IRVariable)?.Name).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(names.Any(n => n != null && n.StartsWith("_u")), Is.True,
                "it still renames the user's variables");
            Assert.That(fn.LocalVariables.Any(v => v.Name.StartsWith("_u")), Is.False,
                "and still never declares them");
        });
    }
}

/// <summary>Every program compiled with the --optimize pipeline AND run.</summary>
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class LoopInvariantCodeMotionRunTests
{
    private static IEnumerable<TestCaseData> Cases() =>
        LoopInvariantCodeMotionTests.Programs.Select(p => new TestCaseData(p.Body, p.Expected).SetArgDisplayNames(p.Name));

    private static string Source(string body) => LoopInvariantCodeMotionTests.Program(body);

    [TestCaseSource(nameof(Cases))]
    public void JavaScript_Runs(string body, string expected)
    {
        var js = new JavaScriptCodeGenerator().Generate(LoopInvariantCodeMotionTests.BuildAggressive(Source(body)));
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(js)), Is.EqualTo(expected));
    }

    [TestCaseSource(nameof(Cases))]
    public void Cpp_Runs(string body, string expected)
    {
        var cpp = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(LoopInvariantCodeMotionTests.BuildAggressive(Source(body)));
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(cpp)), Is.EqualTo(expected));
    }

    [TestCaseSource(nameof(Cases))]
    public void CSharp_Runs(string body, string expected)
    {
        var cs = new ImprovedCSharpCodeGenerator().Generate(LoopInvariantCodeMotionTests.BuildAggressive(Source(body)));
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpText(cs)), Is.EqualTo(expected));
    }
}
