using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Plan 2 — the interop escape hatch, RUN under Node rather than string-matched.
///
/// <para>Separate from <see cref="JavaScriptInteropTests"/> because the execution-tier roster
/// reads TYPE-level <c>[Category]</c> (<see cref="JsExecutionTierRosterTests"/>), so a fixture
/// mixing fast codegen tests with node-spawning ones cannot be rostered at all. Same split as
/// <c>BooleanOperatorTests</c> / <c>BooleanOperatorExecutionTests</c>.</para>
///
/// <para><b>Why running matters here specifically.</b> An inline block is passed through
/// VERBATIM, so a codegen assertion is close to tautological — it checks that text the
/// generator copied is present in text the generator produced. What it cannot check is that the
/// block ends up somewhere the surrounding program actually reaches: emitted inside the right
/// function, at the right nesting, with its own statements intact. Only stdout answers that.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class JavaScriptInteropExecutionTests
{
    [Test]
    public void InlineJavaScriptBlock_Executes()
        => Assert.That(
            JavaScriptExecutionTests.RunJs("Sub Main()\njavascript{ console.log(\"inline\"); }\nEnd Sub"),
            Is.EqualTo("inline"));

    /// <summary>
    /// A block sits INSIDE Main's body, in order, not hoisted to the top of the file. Three
    /// prints rather than one, because a block emitted at file scope still prints its own line
    /// and a single-print test cannot tell the two apart.
    /// </summary>
    [Test]
    public void InlineJavaScriptBlock_RunsInPlaceBetweenStatements()
        => Assert.That(
            JavaScriptExecutionTests.RunJs(
                "Sub Main()\nConsole.WriteLine(\"before\")\njavascript{ console.log(\"middle\"); }\n" +
                "Console.WriteLine(\"after\")\nEnd Sub"),
            Is.EqualTo("before\nmiddle\nafter"));

    /// <summary>
    /// The hatch is only useful if it can SEE the program around it. A block reading a
    /// BasicLang local depends on the emitted name matching — which is the generator's
    /// SanitizeName output, not the BasicLang spelling, so this is a real coupling and not a
    /// tautology.
    /// </summary>
    [Test]
    public void InlineJavaScriptBlock_CanReadABasicLangLocal()
        => Assert.That(
            JavaScriptExecutionTests.RunJs(
                "Sub Main()\nDim n As Integer = 41\njavascript{ console.log(n + 1); }\nEnd Sub"),
            Is.EqualTo("42"));

    /// <summary>
    /// Multi-line blocks are the real shape — the milestone program is one. Each source line
    /// must survive as its own output line (Visit(IRInlineCode) emits through Line()), which is
    /// also what keeps the source map below the block honest.
    /// </summary>
    [Test]
    public void InlineJavaScriptBlock_MultiLine_Executes()
        => Assert.That(
            JavaScriptExecutionTests.RunJs(
                "Sub Main()\njavascript{\nconst parts = [\"a\", \"b\"];\n" +
                "console.log(parts.join(\"-\"));\n}\nEnd Sub"),
            Is.EqualTo("a-b"));

    /// <summary>
    /// ⛔ THE OPTIMIZED PATH. Every shipping route runs OptimizationPipeline.AddStandardPasses()
    /// unconditionally and <c>JsTestSupport.Compile</c> runs none of it — the gap that once hid
    /// six live defects behind 351 green tests. An inline block is opaque: no operands, no
    /// result, nothing for dead-code elimination to prove live. Confirm it is not pruned on the
    /// IR users actually get.
    /// </summary>
    [Test]
    public void Optimized_InlineJavaScriptBlock_StillRuns()
        => Assert.That(
            JavaScriptOptimizedExecutionTests.RunOptimized(
                "Sub Main()\nDim x As Integer = 2 + 3 * 4\njavascript{ console.log(\"hatch\"); }\n" +
                "Console.WriteLine(x)\nEnd Sub"),
            Is.EqualTo("hatch\n14"));
}
