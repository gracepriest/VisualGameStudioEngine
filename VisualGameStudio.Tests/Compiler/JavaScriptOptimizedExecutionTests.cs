using System.Diagnostics;
using System.IO;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Plan task 29 step 3 — the SAME programs, run optimized and unoptimized, compared.
///
/// <para><b>Every JavaScript test written before this one ran on UNOPTIMIZED IR.</b>
/// <c>JsTestSupport</c> is BuildModule + Generate with nothing between, while all three
/// shipping routes — CLI single-file, CLI project, and the IDE's BuildService — run
/// <c>OptimizationPipeline.AddStandardPasses()</c> UNCONDITIONALLY. There is no flag to turn
/// it off; <c>--optimize</c> only upgrades standard to aggressive. So the green suite has
/// never once exercised the IR that actually ships, which is precisely the failure CLAUDE.md
/// warns about and precisely how the C++ backend's live defects got in.</para>
///
/// <para><b>Why stdout is the oracle and generated text is not.</b> When a pass swaps a node
/// without re-pointing its consumers, this generator does not produce an undeclared
/// identifier the way the C++ one does — <c>Expr</c> falls back to rendering the operand tree
/// INLINE, so the failure is a silently DOUBLE-EVALUATED side effect. A text assertion cannot
/// see that. Comparing what the two programs PRINT can.</para>
/// </summary>
[TestFixture]
[Category("Integration")]   // spawns node
[NonParallelizable]
public class JavaScriptOptimizedExecutionTests
{
    /// <summary>
    /// Compile with the standard passes applied, exactly as every shipping route does.
    ///
    /// <para>Delegates to <see cref="JsTestSupport.CompileOptimized"/> — ONE definition of "the IR
    /// that ships", shared with the codegen-only optimized fixtures that need no Node. A second
    /// copy here would let the two drift about which passes "optimized" means.</para>
    /// </summary>
    private static string CompileOptimized(string source) => JsTestSupport.CompileOptimized(source);

    /// <summary>
    /// Compile with the shipping passes and RUN it. <c>internal</c> so sibling execution
    /// fixtures can reach the optimized path without a second copy of the Node harness —
    /// two copies would drift about timeouts, .mjs and the exit-code assertion, and the
    /// point of this fixture is that the optimized path is exercised the SAME way.
    /// </summary>
    internal static string RunOptimized(string source) => RunNode(CompileOptimized(source));

    /// <summary>
    /// Runs a JS string under Node.
    ///
    /// <para>Reads stdout/stderr ASYNCHRONOUSLY before waiting, and KILLS the process on
    /// timeout. The synchronous <c>ReadToEnd()</c>-then-<c>WaitForExit</c> shape blocks
    /// forever inside the read when the child wedges, so its timeout can never fire — and
    /// this repo has already had Node wedge the whole machine.</para>
    /// </summary>
    private static string RunNode(string js)
    {
        var node = BasicLang.Runtime.NodeLocator.Find();
        if (node == null)
            Assert.Ignore("Node.js not found — the JS execution tier cannot run on this machine.");

        var dir = Path.Combine(Path.GetTempPath(), "BasicLang_JsOpt_" + Path.GetRandomFileName());
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
                CreateNoWindow = true
            };
            psi.ArgumentList.Add(file);

            using var p = Process.Start(psi)!;
            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();

            if (!p.WaitForExit(30000))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                Assert.Fail($"node did not exit within 30s.\n--- generated JS ---\n{js}");
            }

            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();

            Assert.That(p.ExitCode, Is.Zero,
                $"node exited {p.ExitCode}.\n--- stderr ---\n{stderr}\n--- generated JS ---\n{js}");

            return stdout.Trim();
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// THE oracle: identical stdout with and without the optimizer. A divergence means a pass
    /// changed observable behaviour, which no pass is allowed to do.
    /// </summary>
    private static void AssertSameOutput(string source)
    {
        var unoptimized = JavaScriptExecutionTests.RunJs(source);
        var optimized = RunOptimized(source);

        Assert.That(optimized, Is.EqualTo(unoptimized),
            "the optimizer changed what the program PRINTS.\n" +
            $"--- source ---\n{source}\n--- optimized JS ---\n{CompileOptimized(source)}");
    }

    // ---------------------------------------------------------------- constant folding

    /// <summary>
    /// ConstantFoldingPass deliberately puts IRConstant nodes INTO block.Instructions, and
    /// this is the only backend whose Visit(IRConstant) throws — C#, C++, LLVM and MSIL all
    /// no-op it, so they have silently absorbed this for the optimizer's entire life.
    /// </summary>
    [Test]
    public void ConstantFolding_FoldableArithmetic()
        => AssertSameOutput("Sub Main()\nDim x As Integer = 2 + 3 * 4\nConsole.WriteLine(x)\nEnd Sub");

    [Test]
    public void ConstantFolding_InAPrintArgument()
        => AssertSameOutput("Sub Main()\nConsole.WriteLine(10 - 4)\nEnd Sub");

    [Test]
    public void ConstantFolding_BooleanAndComparison()
        => AssertSameOutput("Sub Main()\nConsole.WriteLine(3 > 2)\nConsole.WriteLine(1 = 2)\nEnd Sub");

    [Test]
    public void ConstantFolding_StringConcatenation()
        => AssertSameOutput("Sub Main()\nDim s As String = \"a\" & \"b\"\nConsole.WriteLine(s)\nEnd Sub");

    /// <summary>
    /// ⛔ THE DUPLICATE-DECLARATION REGRESSION. An IRConstant's name is <c>$"const_{value}"</c>
    /// — derived from the VALUE, so it is not unique. <c>If x &gt; 0 And y &gt; 0</c> folds to
    /// three separate <c>True</c> constants that all want the name <c>const_True</c>, and
    /// binding them emitted <c>const const_True = true;</c> three times: a SyntaxError that
    /// stops the entire module parsing, from a build that reported success.
    ///
    /// <para>Found by hand-compiling an ordinary VB condition (<c>If x &gt; 0 And y &gt; 0</c>),
    /// NOT by the suite — every optimizer test above happened to fold to distinct values.
    /// Reproduced here without <c>And</c>, which is separately unmapped on this backend.</para>
    /// </summary>
    [Test]
    public void ConstantFolding_RepeatedIdenticalConstants_DoNotCollide()
        => AssertSameOutput(
            "Sub Main()\n" +
            "Dim a As Boolean = 1 < 2\n" +
            "Dim b As Boolean = 3 < 4\n" +
            "Dim c As Boolean = 5 < 6\n" +
            "Console.WriteLine(a)\nConsole.WriteLine(b)\nConsole.WriteLine(c)\n" +
            "End Sub");

    /// <summary>
    /// The same collision by a second route: <c>-5</c> and <c>5</c> both sanitize to
    /// <c>const_5</c>, so a name-keyed binding would declare one twice AND give one of them
    /// the wrong value.
    /// </summary>
    [Test]
    public void ConstantFolding_NegativeAndPositive_DoNotCollide()
        => AssertSameOutput(
            "Sub Main()\nConsole.WriteLine(2 - 7)\nConsole.WriteLine(2 + 3)\nEnd Sub");

    // ---------------------------------------------------------------- strength reduction

    /// <summary>
    /// StrengthReduction rewrites multiplication by a power of two into a SHIFT. Within int32
    /// range the two agree, so the oracle holds.
    /// </summary>
    [Test]
    public void StrengthReduction_SmallMultiply()
        => AssertSameOutput("Sub Main()\nDim x As Integer = 5\nConsole.WriteLine(x * 8)\nEnd Sub");

    /// <summary>
    /// Chip <c>task_98f2685b</c>, FIXED — <c>Integer</c> arithmetic now wraps to int32 on
    /// this backend, so optimized and unoptimized agree.
    ///
    /// <para>This began as a CHARACTERIZATION test asserting the two different answers
    /// (4000000000 unoptimized, -294967296 optimized) precisely so that fixing the divergence
    /// would fail loudly rather than pass unnoticed. It did exactly that, and is now inverted
    /// to assert the consistency instead.</para>
    ///
    /// <para><c>Integer</c> is 32-bit everywhere else in the compiler, and ConstantFoldingPass
    /// folds in int32 — so wrapping is what agrees with the optimizer and with the C# and C++
    /// backends. ⚠ It does NOT match VB.NET, where Integer overflow raises
    /// OverflowException; matching that would be a further language decision.</para>
    /// </summary>
    [Test]
    public void LargeMultiply_WrapsConsistently_OptimizedOrNot()
    {
        const string source = "Sub Main()\nDim x As Integer = 1000000000\nConsole.WriteLine(x * 4)\nEnd Sub";

        AssertSameOutput(source);
        Assert.That(RunOptimized(source), Is.EqualTo("-294967296"),
            "Integer is 32-bit; 1000000000 * 4 wraps, matching C# and the optimizer");
    }

    /// <summary>
    /// The half the optimizer could NOT reach — a genuinely non-foldable operand. This is
    /// where the old behaviour showed 4000000000 while the foldable form showed -294967296.
    /// </summary>
    [Test]
    public void LargeMultiply_WrapsEvenWhenNotFoldable()
        => Assert.That(RunOptimized(
                "Function Scale() As Integer\nReturn 4\nEnd Function\n" +
                "Sub Main()\nDim x As Integer = 1000000000\nConsole.WriteLine(x * Scale())\nEnd Sub"),
            Is.EqualTo("-294967296"));

    /// <summary>
    /// ⛔ Math.imul, not `(a * b) | 0`. A double multiply loses precision above 2^53 BEFORE a
    /// coercion can wrap it, so the naive form gives the wrong int32 here.
    /// </summary>
    [Test]
    public void LargeMultiply_IsExact_NotDoubleThenCoerced()
        => Assert.That(RunOptimized(
                "Function Big() As Integer\nReturn 123456789\nEnd Function\n" +
                "Sub Main()\nDim x As Integer = 987654321\nConsole.WriteLine(x * Big())\nEnd Sub"),
            Is.EqualTo(unchecked((int)(987654321 * 123456789L)).ToString()));

    /// <summary>Doubles must NOT be coerced — `| 0` would truncate every fraction.</summary>
    [Test]
    public void DoubleArithmetic_IsNotCoercedToInt32()
        => Assert.That(RunOptimized(
                "Sub Main()\nDim a As Double = 1.5\nDim b As Double = 2.25\nConsole.WriteLine(a + b)\nEnd Sub"),
            Is.EqualTo("3.75"));

    // ---------------------------------------------------------------- block structure

    /// <summary>
    /// DeadCodeEliminationPass removes unreachable BLOCKS, and that is what deleted the
    /// post-loop and post-try continuations on the C++ backend. This generator is
    /// EntryBlock-rooted and follows TERMINATORS, so a dropped continuation loses everything
    /// after the construct — silently, because what remains still compiles.
    /// </summary>
    [Test]
    public void DeadCode_StatementAfterAForLoop_Survives()
        => AssertSameOutput(
            "Sub Main()\nFor i As Integer = 1 To 3\nConsole.WriteLine(i)\nNext\n" +
            "Console.WriteLine(\"after\")\nEnd Sub");

    [Test]
    public void DeadCode_StatementAfterForEach_Survives()
        => AssertSameOutput(
            "Sub Main()\nDim l As New List(Of Integer)()\nl.Add(1)\nl.Add(2)\n" +
            "For Each n As Integer In l\nConsole.WriteLine(n)\nNext\n" +
            "Console.WriteLine(\"after\")\nEnd Sub");

    [Test]
    public void DeadCode_StatementAfterTryCatch_Survives()
        => AssertSameOutput(
            "Sub Main()\nTry\nConsole.WriteLine(\"try\")\nCatch ex As Exception\n" +
            "Console.WriteLine(\"catch\")\nEnd Try\nConsole.WriteLine(\"after\")\nEnd Sub");

    /// <summary>
    /// ⚠ ControlFlowGraph.Build wires IRForEach and IRTryCatch end blocks but NOT
    /// IRSwitch.EndBlock, while this generator walks that EndBlock unconditionally. That is
    /// the same asymmetry whose absence previously let dead-code elimination delete post-loop
    /// and post-try code — Select Case is the one case still shaped that way.
    /// </summary>
    [Test]
    public void DeadCode_StatementAfterSelectCase_Survives()
        => AssertSameOutput(
            "Sub Main()\nDim x As Integer = 2\n" +
            "Select Case x\nCase 1\nConsole.WriteLine(\"one\")\n" +
            "Case 2\nConsole.WriteLine(\"two\")\n" +
            "Case Else\nConsole.WriteLine(\"other\")\nEnd Select\n" +
            "Console.WriteLine(\"after\")\nEnd Sub");

    [Test]
    public void DeadCode_StatementAfterWhile_Survives()
        => AssertSameOutput(
            "Sub Main()\nDim i As Integer = 0\nWhile i < 3\ni = i + 1\nEnd While\n" +
            "Console.WriteLine(i)\nConsole.WriteLine(\"after\")\nEnd Sub");

    // ---------------------------------------------------------------- CSE / copy propagation

    /// <summary>
    /// CommonSubexpressionElimination replaces a repeated expression with one temp. If the
    /// consumers are not re-pointed, this generator renders the operand tree inline again
    /// rather than failing — so the side effect happens TWICE and only the output shows it.
    /// </summary>
    [Test]
    public void CommonSubexpression_SideEffectRunsExactlyOnce()
        => AssertSameOutput(
            "Function Bump() As Integer\nConsole.WriteLine(\"called\")\nReturn 1\nEnd Function\n" +
            "Sub Main()\nDim a As Integer = Bump() + Bump()\nConsole.WriteLine(a)\nEnd Sub");

    [Test]
    public void CopyPropagation_ThroughAReassignedLocal()
        => AssertSameOutput(
            "Sub Main()\nDim a As Integer = 1\nDim b As Integer = a\na = 99\n" +
            "Console.WriteLine(a)\nConsole.WriteLine(b)\nEnd Sub");

    [Test]
    public void CopyPropagation_AcrossAConditional()
        => AssertSameOutput(
            "Sub Main()\nDim x As Integer = 1\nIf x > 0 Then\nx = 5\nEnd If\n" +
            "Console.WriteLine(x)\nEnd Sub");

    // ---------------------------------------------------------------- features Phase 2 added

    [Test]
    public void Optimized_ClassesStillWork()
        => AssertSameOutput(
            "Class Counter\nPublic Count As Integer\n" +
            "Public Sub Bump()\nCount = Count + 1\nEnd Sub\nEnd Class\n" +
            "Sub Main()\nDim c As New Counter()\nc.Bump()\nc.Bump()\nConsole.WriteLine(c.Count)\nEnd Sub");

    [Test]
    public void Optimized_LambdasStillCapture()
        => AssertSameOutput(
            "Sub Main()\nDim n As Integer = 7\nDim f = Function() n * 2\n" +
            "Console.WriteLine(f())\nEnd Sub");

    [Test]
    public void Optimized_IteratorsStayLazy()
        => AssertSameOutput(
            "Iterator Function Numbers() As IEnumerable(Of Integer)\n" +
            "Console.WriteLine(\"gen 1\")\nYield 1\n" +
            "Console.WriteLine(\"gen 2\")\nYield 2\n" +
            "End Function\n" +
            "Sub Main()\nFor Each n As Integer In Numbers()\nConsole.WriteLine(\"got \" & n)\nNext\nEnd Sub");

    [Test]
    public void Optimized_LinqStillChains()
        => AssertSameOutput(
            "Sub Main()\nDim l As New List(Of Integer)()\n" +
            "l.Add(1)\nl.Add(2)\nl.Add(3)\nl.Add(4)\nl.Add(5)\n" +
            "For Each n As Integer In l.Where(Function(x As Integer) x > 2).Select(Function(x As Integer) x * 10)\n" +
            "Console.WriteLine(n)\nNext\nEnd Sub");

    [Test]
    public void Optimized_AsyncStillSequences()
        => AssertSameOutput(
            "Async Function GetValue() As Task(Of Integer)\nReturn 42\nEnd Function\n" +
            "Async Sub Main()\nDim v = Await GetValue()\nConsole.WriteLine(v)\nEnd Sub");

    [Test]
    public void Optimized_StdLibStillLowers()
        => AssertSameOutput(
            "Sub Main()\nConsole.WriteLine(Sqr(16))\nConsole.WriteLine(Abs(-3))\n" +
            "Console.WriteLine(Round(2.5))\nEnd Sub");
}
