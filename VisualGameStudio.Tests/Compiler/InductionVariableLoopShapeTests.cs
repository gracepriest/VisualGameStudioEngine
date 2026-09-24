// Merged in from master (c283d6d, #79), which disabled InductionVariablePass independently of this
// branch (3338dfd). Both files originally declared `InductionVariableDisabledTests`; this one is
// renamed so neither side's coverage is lost. The branch file keeps the original name because
// other fixtures cite it. NOTE: the "inside If" exclusion below was for LoopInvariantCodeMotionPass,
// which this branch also unregisters (ADR-0003, 60b7226).
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.JavaScript;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>InductionVariablePass</c> is OUT of <c>AddAggressivePasses</c> (the <c>--optimize</c>
/// pipeline): it broke every loop it rewrote. MEASURED on master 152d6b9 over 11 loop programs —
/// it fired on 6 and JavaScript threw "ReferenceError: Cannot access '_div_t1' before
/// initialization" on all 6, because the derived variable it introduces is never initialised or
/// declared. The reasons, and why it is disabled rather than repaired, are on the commented-out
/// line in <c>AddAggressivePasses</c>. Same arrangement as <see cref="FunctionInliningDisabledTests"/>.
/// </summary>
[TestFixture]
public class InductionVariableLoopShapeTests
{
    /// <summary>The shapes that fired, each with the answer .NET gives.</summary>
    internal static readonly (string Name, string Body, string Expected)[] Loops =
    {
        ("step 1", @"
    Dim s As Integer = 0
    For i As Integer = 1 To 5
        s = s + i * 3
    Next
    Console.WriteLine(CStr(s))", "45"),
        ("step 2", @"
    Dim s As Integer = 0
    For i As Integer = 0 To 9 Step 2
        s = s + i * 5
    Next
    Console.WriteLine(CStr(s))", "100"),
        ("Long", @"
    Dim s As Long = 0
    For i As Long = 1 To 4
        s = s + i * 7
    Next
    Console.WriteLine(CStr(s))", "70"),
        ("inside If", @"
    Dim s As Integer = 0
    For i As Integer = 1 To 6
        If i Mod 2 = 0 Then
            s = s + i * 10
        End If
    Next
    Console.WriteLine(CStr(s))", "120"),
        ("Dim in body", @"
    Dim s As Integer = 0
    For i As Integer = 1 To 5
        Dim k As Integer = i * 3
        s = s + k
    Next
    Console.WriteLine(CStr(s))", "45"),
    };

    internal static string Program(string body) => "Sub Main()" + body + "\nEnd Sub\n";

    internal static IRModule BuildAggressive(string source)
    {
        var module = JsTestSupport.BuildModule(source, sourceFilePath: "prog.bas");
        var pipeline = new OptimizationPipeline();
        pipeline.AddAggressivePasses();
        pipeline.Run(module);
        return module;
    }

    private static System.Collections.Generic.IEnumerable<TestCaseData> LoopCases() =>
        Loops.Select(l => new TestCaseData(l.Body).SetName("Aggressive_NoDerivedVariable(" + l.Name + ")"));

    /// <summary>
    /// No <c>_div_</c> variable — the pass's signature — in shipped <c>--optimize</c> output.
    /// Read off the C# backend because it accepts every shape here; JavaScript refuses Long by
    /// design (BL7003).
    /// </summary>
    [TestCaseSource(nameof(LoopCases))]
    public void Aggressive_NoDerivedVariable(string body)
    {
        var cs = new ImprovedCSharpCodeGenerator().Generate(BuildAggressive(Program(body)));
        Assert.That(cs, Does.Not.Contain("_div_"), cs);
    }

    /// <summary>
    /// ⛔ THE PASS IS STILL BROKEN when constructed directly, pinned deliberately so "disabled"
    /// stays a measured claim. If someone repairs it this goes RED — the signal to re-enable it
    /// in <c>AddAggressivePasses</c> and delete this test, not to weaken it.
    /// </summary>
    [Test]
    public void RunDirectly_ThePassStillIntroducesAnUninitialisedVariable()
    {
        var module = JsTestSupport.BuildModule(Program(Loops[0].Body), sourceFilePath: "prog.bas");
        var pipeline = new OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.AddPass(new InductionVariablePass());
        pipeline.Run(module);

        var main = module.Functions.Single(f => f.Name == "Main");
        Assert.Multiple(() =>
        {
            var derivedReads = main.Blocks.SelectMany(b => b.Instructions)
                .OfType<IRBinaryOp>()
                .Any(op => op.Left is IRVariable v && v.Name.StartsWith("_div_"));
            Assert.That(derivedReads, Is.True, "the pass still rewrites — if not, this fixture's premise changed");
            Assert.That(main.LocalVariables.Any(v => v.Name.StartsWith("_div_")), Is.False,
                "and still never declares the variable it reads (defect 2)");
        });
    }
}

/// <summary>The same loops compiled with the <c>--optimize</c> pipeline AND run.</summary>
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class InductionVariableLoopShapeRunTests
{
    // "inside If" is left out of the RUN tests on purpose: with this pass gone it no longer
    // introduces `_div_`, but LoopInvariantCodeMotionPass still breaks it under --optimize — it
    // moves the loop's `i + 1` into the If branch ("ReferenceError: t5 is not defined"). That is
    // a separate defect, measured by skipping each aggressive pass in turn: only skipping LICM
    // makes it print 120. Running it here would pin LICM's bug, not this pass's.
    private static System.Collections.Generic.IEnumerable<(string Name, string Body, string Expected)> Runnable() =>
        InductionVariableLoopShapeTests.Loops.Where(l => l.Name != "inside If");

    private static System.Collections.Generic.IEnumerable<TestCaseData> Cases() =>
        Runnable().Select(l => new TestCaseData(l.Body, l.Expected).SetArgDisplayNames(l.Name));

    // JavaScript refuses Long by design (BL7003).
    private static System.Collections.Generic.IEnumerable<TestCaseData> JsCases() =>
        Runnable().Where(l => l.Name != "Long")
            .Select(l => new TestCaseData(l.Body, l.Expected).SetArgDisplayNames(l.Name));

    [TestCaseSource(nameof(JsCases))]
    public void JavaScript_Runs(string body, string expected)
    {
        var js = new JavaScriptCodeGenerator().Generate(
            InductionVariableLoopShapeTests.BuildAggressive(InductionVariableLoopShapeTests.Program(body)));
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(js)), Is.EqualTo(expected));
    }

    [TestCaseSource(nameof(Cases))]
    public void CSharp_Runs(string body, string expected)
    {
        var cs = new ImprovedCSharpCodeGenerator().Generate(
            InductionVariableLoopShapeTests.BuildAggressive(InductionVariableLoopShapeTests.Program(body)));
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpText(cs)), Is.EqualTo(expected));
    }
}
