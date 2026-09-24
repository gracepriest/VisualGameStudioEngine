using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// ⭐ THE ONE DEFINITION OF "AGGRESSIVE" FOR THE WHOLE SUITE.
///
/// <para>Every aggressive-pipeline leg — <c>JsTestSupport.CompileAggressive</c>,
/// <c>BclE2E.CompileToCppAggressive</c>, <c>MsilHarness.CompileToIl(aggressive: true)</c> and
/// <c>ReturnCoercionTests.EmitCSharpAggressiveForTest</c> — calls THIS method and nothing else,
/// for the reason CLAUDE.md gives for <c>ModuleResolver</c> / <c>ModuleTypeWalker</c>: shared
/// substrate is changed once, not per consumer. Four private copies of
/// <c>new OptimizationPipeline(); AddAggressivePasses(); Run(module)</c> would be four things that
/// can drift about what the aggressive pipeline IS, which is the whole subject under test.</para>
///
/// <para>⛔ WHY IT HAD TO BE ADDED. Before this, the suite had NO shared aggressive execution
/// helper at all. <c>FourBackends.RunsOnEveryBackend</c> is not aggressive on any of its four
/// legs (C++ → <c>AddStandardPasses</c>, JavaScript → NO optimizer, MSIL →
/// <c>AddStandardPasses</c>, C# → <c>AddStandardPasses</c>), and
/// <c>JsTestSupport.CompileOptimized</c> / <c>JavaScriptOptimizedExecutionTests.RunOptimized</c>
/// are <c>AddStandardPasses</c> too despite the name. The only aggressive execution in the suite
/// was two PRIVATE duplicates — <c>FunctionInliningDisabledTests.RunAggressive</c> and
/// <c>AlgebraicSimplificationTests.RunAggressive</c> — and both are JavaScript-only, so C#, C++
/// and MSIL had ZERO aggressive coverage. <c>OptimizationPipeline</c> now keeps three passes it
/// does not ship — <c>ConstantPropagationPass</c>, <c>FunctionInliningPass</c> and
/// <c>InductionVariablePass</c> — and TWO of the three were aggressive-only, so this hole is where
/// they shipped broken. (<c>ConstantPropagationPass</c> was a STANDARD pass; it is named here only
/// to keep the list of disabled passes complete.)</para>
/// </summary>
internal static class AggressivePipeline
{
    /// <summary>
    /// Run <c>AddAggressivePasses()</c> over <paramref name="module"/> in place — what
    /// <c>Compiler.cs:292</c> and <c>Compiler.cs:459</c> do when
    /// <c>CompilerOptions.OptimizeAggressive</c> is set, which is what the CLI's
    /// <c>--optimize</c> and a Release <c>.blproj</c> build both request.
    /// </summary>
    internal static void Apply(IRModule module)
    {
        var pipeline = new OptimizationPipeline();
        pipeline.AddAggressivePasses();
        pipeline.Run(module);
    }
}

/// <summary>
/// One program, all four backends compiled AND RUN, in process. The property a fixture asserts
/// through this is "the backends agree", not "one backend prints the number".
///
/// <para>⚠ The C# leg is in-process Roslyn (emit a console assembly to memory, load it, invoke
/// the entry point, capture <c>Console.Out</c>), NOT <c>CliTestHarness.CompileRunCSharp</c>:
/// that spawns <c>BasicLang.exe</c>, a Windows apphost that is not deployed on Linux, which is
/// why the 18 <c>_CSharp</c> rows that use it sit in this machine's baseline failure set. Each
/// program loads as a fresh assembly, so one test's module globals cannot leak into the next.
/// A fixture using this must be <c>[NonParallelizable]</c>: the C# leg redirects
/// <c>Console.Out</c>.</para>
/// </summary>
internal static class FourBackends
{
    internal static string Norm(string s) => (s ?? "").Replace("\r\n", "\n").Trim();

    internal static void RunsOnEveryBackend(string program, string expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program))), Is.EqualTo(expected), "C++");
            Assert.That(Norm(JavaScriptExecutionTests.RunJs(program)), Is.EqualTo(expected), "JavaScript");
            Assert.That(Norm(RunEmittedCSharp(program)), Is.EqualTo(expected), "C#");
            // Last: on a machine without ilasm this leg ends the block as Ignored (see
            // MsilHarness.RequireIlasm), so every other backend must already have run.
            Assert.That(Norm(Msil.MsilHarness.RunExpectingSuccess(program)), Is.EqualTo(expected), "MSIL");
        });
    }

    /// <summary>
    /// ⭐ THE AGGRESSIVE SIBLING of <see cref="RunsOnEveryBackend"/>: one program, all four
    /// backends compiled AND RUN through <see cref="AggressivePipeline"/>. The shared runner the
    /// suite did not have; see <see cref="AggressivePipeline"/> for what its absence cost.
    ///
    /// <para>⭐ <b>USABLE FOR LOOPS SINCE ADR-0003 — this used to say it was not.</b> The
    /// exclusion it carried was real: under <c>AddAggressivePasses()</c>
    /// <c>LoopInvariantCodeMotionPass</c> sank a counted loop's condition out of the condition
    /// block and into the loop's own LATCH, because <c>ControlFlowGraph.IdentifyLoops</c> handed
    /// it a "preheader" that was the latch; C++ and MSIL emit the CFG as labels and <c>goto</c>s,
    /// read the condition flag before anything wrote it, and ran the loop ZERO times. That was
    /// issue #114. ADR-0003 unregisters all three loop passes, so no aggressive pass now touches
    /// a loop at all.</para>
    ///
    /// <para>RE-MEASURED across the 13-shape CFG corpus (<see cref="CfgLoopShapes"/>) on all four
    /// backends under both pipelines: <b>104 of 104 cells correct</b>, including every counted
    /// <c>For</c>, <c>While</c>, <c>Do While</c>, <c>Exit For</c> and nested-loop shape on C++
    /// and MSIL. <see cref="CfgLoopShapesAggressiveTests"/> is that measurement, committed. Write
    /// a four-backend aggressive loop assertion here and expect the right answer.</para>
    ///
    /// <para>⚠ <c>Assert.Multiple</c> and an in-process Roslyn C# leg with no timeout, exactly as
    /// <see cref="RunsOnEveryBackend"/>: ONE shape per test.</para>
    /// </summary>
    internal static void RunsOnEveryBackendAggressive(string program, string expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(program))), Is.EqualTo(expected), "C++");
            Assert.That(Norm(RunAggressiveJs(program)), Is.EqualTo(expected), "JavaScript");
            Assert.That(Norm(Msil.MsilHarness.RunAggressiveExpectingSuccess(program)), Is.EqualTo(expected), "MSIL");
            Assert.That(Norm(RunEmittedCSharpAggressive(program)), Is.EqualTo(expected), "C#");
        });
    }

    internal static string RunEmittedCSharp(string program) =>
        RunEmittedCSharpText(ReturnCoercionTests.EmitCSharpForTest(program));

    /// <summary>The C# leg, aggressive. Same in-process Roslyn run, aggressive IR.</summary>
    internal static string RunEmittedCSharpAggressive(string program) =>
        RunEmittedCSharpText(ReturnCoercionTests.EmitCSharpAggressiveForTest(program));

    /// <summary>The JavaScript leg, aggressive: aggressive IR, then the shared Node harness.</summary>
    internal static string RunAggressiveJs(string program) =>
        JavaScriptExecutionTests.RunNodeScript(JsTestSupport.CompileAggressive(program));

    internal static string RunEmittedCSharpText(string csharp)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToImmutableArray();

        var compilation = CSharpCompilation.Create(
            "FourBackendsProbe_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(csharp) },
            references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));

        using var ms = new MemoryStream();
        var emitted = compilation.Emit(ms);
        Assert.That(emitted.Success, Is.True,
            "the emitted C# does not compile:\n" + string.Join("\n",
                emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()))
            + "\n--- emitted ---\n" + csharp);

        var assembly = Assembly.Load(ms.ToArray());
        var entry = assembly.EntryPoint;
        Assert.That(entry, Is.Not.Null, "no entry point in the emitted program");

        var captured = new StringWriter();
        var original = Console.Out;
        Console.SetOut(captured);
        try
        {
            var args = entry!.GetParameters().Length == 0 ? null : new object[] { Array.Empty<string>() };
            entry.Invoke(null, args);
        }
        catch (TargetInvocationException ex)
        {
            Assert.Fail("the emitted C# threw: " + (ex.InnerException ?? ex) + "\n--- emitted ---\n" + csharp);
        }
        finally
        {
            Console.SetOut(original);
        }
        return captured.ToString();
    }
}
