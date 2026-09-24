using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.JavaScript;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>ControlFlowGraph.FindBackEdges</c> tested dominance backwards, so every forward edge was a
/// "back edge": each <c>entry → header</c> edge became a loop containing the entry block, and the
/// real <c>inc → header</c> edge was missed. LoopInvariantCodeMotionPass took the For loop's
/// INCREMENT block for its preheader and hoisted the loop condition into it, so with
/// <c>--optimize</c> a C++ <c>For i = 0 To 2</c> read an uninitialised condition and ran zero
/// times. With real loops found, LICM, LoopUnrollingPass and InductionVariablePass each
/// miscompile on their own, so AddAggressivePasses no longer runs them (see its note).
/// </summary>
[TestFixture]
public class LoopOptimizationTests
{
    internal const string ForLoop = @"
Sub Main()
    For i As Integer = 0 To 2
        Console.WriteLine(i)
    Next
End Sub";

    private static IRFunction Main(IRModule module) => module.Functions.Single(f => f.Name == "Main");

    [Test]
    public void AForLoop_HasExactlyOneLoop_TheConditionIncrementCycle_WithoutTheEntryBlock()
    {
        var main = Main(JsTestSupport.BuildModule(ForLoop));
        var cfg = new ControlFlowGraph(main);
        cfg.Build();
        cfg.ComputeDominators();
        cfg.IdentifyLoops();

        Assert.That(cfg.FindBackEdges().Select(e => (e.From.Name, e.To.Name)),
            Is.EqualTo(new[] { ("for0.inc", "for0.cond") }));
        Assert.That(cfg.NaturalLoops, Has.Count.EqualTo(1));
        Assert.That(cfg.NaturalLoops[0].Select(b => b.Name),
            Is.EquivalentTo(new[] { "for0.cond", "for0.body", "for0.inc" }));
        Assert.That(cfg.IsReducible(), Is.True);
    }

    [Test]
    public void TheAggressivePipeline_NoLongerRunsTheThreeBrokenLoopPasses()
    {
        var pipeline = new OptimizationPipeline();
        pipeline.AddAggressivePasses();
        var passes = (List<OptimizationPass>)typeof(OptimizationPipeline)
            .GetField("_passes", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pipeline)!;

        Assert.That(passes.Select(p => p.GetType()), Has.None.EqualTo(typeof(LoopInvariantCodeMotionPass))
            .And.None.EqualTo(typeof(LoopUnrollingPass))
            .And.None.EqualTo(typeof(InductionVariablePass)));
        Assert.That(passes.Select(p => p.GetType()), Has.Some.EqualTo(typeof(LoopFusionPass)));
    }

    internal static IRModule Aggressive(string source)
    {
        var module = JsTestSupport.BuildModule(source);
        var pipeline = new OptimizationPipeline();
        pipeline.AddAggressivePasses();
        pipeline.Run(module);
        return module;
    }

    [Test]
    public void OnCpp_TheLoopConditionIsComputedInTheConditionBlock()
    {
        var cpp = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(Aggressive(ForLoop));

        Assert.That(Regex.IsMatch(cpp, @"for0_cond: ;\s*\w+ = i <= 2;\s*if \("), Is.True, cpp);
    }
}

/// <summary>Runs loop programs through the aggressive pipeline under Node and a C++ compiler.</summary>
[TestFixture]
[Category("Integration")]   // spawns node and a C++ compiler
[NonParallelizable]
public class LoopOptimizationExecutionTests
{
    /// <summary>(program, what .NET prints), each measured against the C# backend.</summary>
    private static readonly (string Name, string Source, string Expected)[] Programs =
    {
        ("For", LoopOptimizationTests.ForLoop, "0\n1\n2"),
        ("Nested", @"
Sub Main()
    Dim total As Integer = 0
    For i As Integer = 1 To 3
        For j As Integer = 1 To i
            total = total + i * j
        Next
    Next
    Console.WriteLine(total)
End Sub", "25"),
        ("WhileDo", @"
Sub Main()
    Dim n As Integer = 0
    While n < 5
        n = n + 2
    End While
    Console.WriteLine(n)
    Dim k As Integer = 10
    Do
        k = k - 3
    Loop Until k < 0
    Console.WriteLine(k)
End Sub", "6\n-2"),
        ("ExitFor", @"
Sub Main()
    Dim found As Integer = -1
    For i As Integer = 0 To 100
        If i * i > 50 Then
            found = i
            Exit For
        End If
    Next
    Console.WriteLine(found)
End Sub", "8"),
        ("GuardedDivision", @"
Function Zero() As Integer
    Return 0
End Function

Sub Main()
    Dim d As Integer = Zero()
    Dim q As Integer = -1
    For i As Integer = 1 To 3
        If d <> 0 Then
            q = 100 \ d
        End If
    Next
    Console.WriteLine(q)
End Sub", "-1"),
        ("InvariantAndVarying", @"
Function Get3() As Integer
    Return 3
End Function

Sub Main()
    Dim a As Integer = Get3()
    Dim s As Integer = 0
    Dim x As Integer = 1
    For i As Integer = 1 To 5
        s = s + a * 4 + x
        x = x + i
    Next
    Console.WriteLine(s)
    Console.WriteLine(x)
End Sub", "85\n16"),
    };

    private static IEnumerable<TestCaseData> Cases() =>
        Programs.Select(p => new TestCaseData(p.Source, p.Expected).SetName(p.Name));

    private static string Normalize(string s) => s.Replace("\r\n", "\n").TrimEnd('\n');

    [TestCaseSource(nameof(Cases))]
    public void OnJavaScript_TheAggressivePipelinePrintsWhatDotNetPrints(string source, string expected) =>
        Assert.That(Normalize(JavaScriptExecutionTests.RunNodeScript(
            new JavaScriptCodeGenerator().Generate(LoopOptimizationTests.Aggressive(source)))), Is.EqualTo(expected));

    [TestCaseSource(nameof(Cases))]
    public void OnCpp_TheAggressivePipelinePrintsWhatDotNetPrints(string source, string expected)
    {
        var compiler = VisualGameStudio.Tests.Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");

        var cpp = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(LoopOptimizationTests.Aggressive(source));
        Assert.That(Normalize(VisualGameStudio.Tests.Native.CppCompile.CompileAndRun(cpp, compiler.Value)),
            Is.EqualTo(expected));
    }

    /// <summary>
    /// ⛔ THE PASSES ARE STILL REACHABLE AND STILL BROKEN, pinned deliberately, as
    /// FunctionInliningDisabledTests does for inlining: run directly, each still turns a correct
    /// program into one that fails. If someone repairs a pass its case goes RED, which is the
    /// signal to re-enable it in AddAggressivePasses and delete the case, not to weaken it.
    /// </summary>
    [TestCase("LICM")]
    [TestCase("Unrolling")]
    [TestCase("InductionVariable")]
    public void RunDirectly_EachDisabledPassStillMiscompiles(string passName)
    {
        OptimizationPass pass = passName switch
        {
            "LICM" => new LoopInvariantCodeMotionPass(),
            "Unrolling" => new LoopUnrollingPass(4),
            _ => new InductionVariablePass(),
        };
        const string program = @"
Sub Main()
    Dim s As Integer = 0
    For i As Integer = 0 To 5
        Dim j As Integer = i * 3 + 1
        s = s + j
    Next
    Console.WriteLine(s)
End Sub";

        var module = JsTestSupport.BuildModule(program);
        var pipeline = new OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.AddPass(pass);
        pipeline.Run(module);

        var (exitCode, stdout) = RunNodeAllowingFailure(new JavaScriptCodeGenerator().Generate(module));
        Assert.That(exitCode != 0 || Normalize(stdout) != "51", Is.True,
            $"{passName} now produces the right answer (51). Re-enable it in AddAggressivePasses.");
    }

    private static (int ExitCode, string Stdout) RunNodeAllowingFailure(string js)
    {
        var node = BasicLang.Runtime.NodeLocator.Find();
        if (node == null) Assert.Ignore("Node.js not found");

        var dir = Path.Combine(Path.GetTempPath(), "BasicLang_LoopPass_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "program.mjs");
            File.WriteAllText(file, js);
            var psi = new ProcessStartInfo(node!)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add(file);
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(10000))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                return (-1, "TIMEOUT");   // an infinite loop is a miscompile too
            }
            return (p.ExitCode, stdout.Result);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
