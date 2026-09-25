using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A pass that removes or swaps a value must re-point that value's CONSUMERS. Two passes in the
/// standard pipeline did not, and each left a consumer holding a node no longer in the function —
/// which every backend renders as an undeclared temp (C++, C#) or a re-rendered copy of the
/// original expression (C#, JavaScript).
///
/// <para><b>MEASURED on master before the fix</b>, Integer throughout:</para>
/// <code>
///   PeepholeOptimizationPass — a fold used directly as an operand:
///     Show(n - n)     C++: t0 = 0; Show(t8);      C#: t0 = 0; Show(n - n);  (CS0103)
///                     JS:  t0 = 0; ...            (ReferenceError in an ES module)
///     Show(P() * 0)   C# and JS ran P TWICE: once as the call's statement, once inside the
///                     re-rendered `P() * 0`.
///     -(-n), Not (Not b)   the same shape, via the double-negation arms.
///   CommonSubexpressionEliminationPass — its private ReplaceAllUses knew only binary/unary ops,
///   stores and assignments:
///     ShowI(n + 1) : If n + 1 = 5 ... : Return n + 1
///                     C++: t2 = t4 == 5;  return t5;   (JavaScript survived by re-rendering)
/// </code>
/// <para>Stored in a named local (<c>Dim r = n - n</c>) the fold was always fine, and it stays an
/// assignment to that local.</para>
/// </summary>
[TestFixture]
public class OptimizerOrphanedTempTests
{
    internal const string FoldProgram = @"
Function P() As Integer
    Console.WriteLine(""P ran"")
    Return 5
End Function

Function Twice(n As Integer) As Integer
    Return n - n
End Function

Sub ShowI(v As Integer)
    Console.WriteLine(v)
End Sub

Sub ShowB(v As Boolean)
    Console.WriteLine(v)
End Sub

Sub Probe(n As Integer, b As Boolean)
    ShowI(n - n)
    ShowI(n + 0)
    ShowI(0 + n)
    ShowI(n - 0)
    ShowI(n * 1)
    ShowI(n * 0)
    ShowI(-(-n))
    ShowB(Not (Not b))
    ShowI(P() * 0)
    Dim r As Integer = P() * 0
    ShowI(r)
    If n - n = 0 Then
        Console.WriteLine(""branch"")
    End If
    ShowI(Twice(n))
End Sub

Sub Main()
    Probe(4, True)
End Sub
";

    // Booleans are compared lowercased: JavaScript prints `true` (a separate, known spelling gap).
    internal const string FoldExpected =
        "0\n4\n4\n4\n4\n0\n4\ntrue\nP ran\n0\nP ran\n0\nbranch\n0";

    internal const string CseProgram = @"
Sub ShowI(v As Integer)
    Console.WriteLine(v)
End Sub

Function F(n As Integer) As Integer
    ShowI(n + 1)
    If n + 1 = 5 Then
        Console.WriteLine(""five"")
    End If
    ShowI(n + 1)
    Return n + 1
End Function

Sub Main()
    ShowI(F(4))
End Sub
";

    internal const string CseExpected = "5\nfive\n5\n5";

    private static IRModule Optimize(string source)
    {
        var ast = new Parser(new Lexer(source).Tokenize()).Parse();
        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            string.Join("; ", analyzer.Errors.Select(e => e.Message)));
        var module = new IRBuilder(analyzer).Build(ast, "OrphanProbe");

        var pipeline = new OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.Run(module);
        return module;
    }

    /// <summary>
    /// Every computed operand (binary op, unary op, compare) that an instruction consumes must
    /// still BE an instruction of the same function. One that is not is exactly what a backend
    /// renders as an undeclared temp.
    /// </summary>
    private static List<string> OrphanedOperands(IRModule module)
    {
        var orphans = new List<string>();
        foreach (var function in module.Functions.Where(f => !f.IsExternal))
        {
            var defined = new HashSet<IRInstruction>(
                function.Blocks.SelectMany(b => b.Instructions), ReferenceEqualityComparer.Instance);
            foreach (var inst in function.Blocks.SelectMany(b => b.Instructions))
                foreach (var operand in IROperandWalker.EnumerateOperands(inst))
                    if (operand is IRBinaryOp or IRUnaryOp or IRCompare && !defined.Contains(operand))
                        orphans.Add($"{function.Name}: {inst.GetType().Name} consumes removed {operand.GetType().Name} '{operand.Name}'");
        }
        return orphans;
    }

    [TestCase("ShowI(n - n)")]
    [TestCase("ShowI(n + 0)")]
    [TestCase("ShowI(0 + n)")]
    [TestCase("ShowI(n - 0)")]
    [TestCase("ShowI(n * 0)")]
    [TestCase("ShowI(P() * 0)")]
    [TestCase("ShowI(-(-n))")]
    [TestCase("ShowB(Not (Not b))")]
    [TestCase("Return n - n")]
    [TestCase("If n - n = 0 Then\n        ShowI(1)\n    End If")]
    public void PeepholeFold_UsedDirectly_LeavesNoOrphanedOperand(string statement)
    {
        var source = @"
Function P() As Integer
    Return 5
End Function
Sub ShowI(v As Integer)
End Sub
Sub ShowB(v As Boolean)
End Sub
Function F(n As Integer, b As Boolean) As Integer
    " + statement + @"
    Return 0
End Function
Sub Main()
End Sub
";
        Assert.That(OrphanedOperands(Optimize(source)), Is.Empty);
    }

    [TestCase("ShowI(n + 1)\n    ShowI(n + 1)")]
    [TestCase("ShowI(n + 1)\n    If n + 1 = 5 Then\n        ShowI(1)\n    End If")]
    [TestCase("ShowI(n + 1)\n    Return n + 1")]
    public void CommonSubexpression_ConsumedByAnyInstruction_LeavesNoOrphanedOperand(string statements)
    {
        var source = @"
Sub ShowI(v As Integer)
End Sub
Function F(n As Integer) As Integer
    " + statements + @"
    Return 0
End Function
Sub Main()
End Sub
";
        Assert.That(OrphanedOperands(Optimize(source)), Is.Empty);
    }

    [Test]
    public void TheRunPrograms_LeaveNoOrphanedOperand()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OrphanedOperands(Optimize(FoldProgram)), Is.Empty, "fold program");
            Assert.That(OrphanedOperands(Optimize(CseProgram)), Is.Empty, "CSE program");
        });
    }

    /// <summary>
    /// The guard against overshooting: a fold stored in a NAMED local stays an assignment to that
    /// local (it is the user's variable, and other code may read it by name).
    /// </summary>
    [Test]
    public void FoldIntoANamedLocal_StaysAnAssignmentToIt()
    {
        var module = Optimize(@"
Function F(n As Integer) As Integer
    Dim r As Integer = n - n
    Return r
End Function
Sub Main()
End Sub
");
        var f = module.Functions.Single(fn => fn.Name == "F");
        var assignments = f.Blocks.SelectMany(b => b.Instructions).OfType<IRAssignment>().ToList();
        Assert.That(assignments.Any(a => a.Target.Name == "r" && a.Value is IRConstant c && Equals(c.Value, 0)),
            Is.True, "Dim r = n - n should still fold to r = 0");
        Assert.That(OrphanedOperands(module), Is.Empty);
    }
}

/// <summary>
/// The run half of <see cref="OptimizerOrphanedTempTests"/>: the same programs compiled THROUGH
/// the optimizer and executed on C# (in-process Roslyn), C++ and JavaScript.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class OptimizerOrphanedTempRunTests
{
    private static string Norm(string s) => FourBackends.Norm(s).Replace("True", "true");

    private static string Cs(string program) => Norm(FourBackends.RunEmittedCSharp(program));

    private static string Cpp(string program) => Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)));

    // CompileOptimized, NOT JavaScriptExecutionTests.RunJs — that one runs no optimizer.
    private static string Js(string program) =>
        Norm(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.CompileOptimized(program)));

    [Test]
    public void FoldUsedDirectly_CompilesAndRunsOnce_CSharp()
        => Assert.That(Cs(OptimizerOrphanedTempTests.FoldProgram), Is.EqualTo(OptimizerOrphanedTempTests.FoldExpected));

    [Test]
    public void FoldUsedDirectly_CompilesAndRunsOnce_Cpp()
        => Assert.That(Cpp(OptimizerOrphanedTempTests.FoldProgram), Is.EqualTo(OptimizerOrphanedTempTests.FoldExpected));

    [Test]
    public void FoldUsedDirectly_CompilesAndRunsOnce_JavaScript()
        => Assert.That(Js(OptimizerOrphanedTempTests.FoldProgram), Is.EqualTo(OptimizerOrphanedTempTests.FoldExpected),
            "a doubled 'P ran' means the consumer re-rendered the discarded `P() * 0`");

    [Test]
    public void CommonSubexpressionInCompareAndReturn_Runs_CSharp()
        => Assert.That(Cs(OptimizerOrphanedTempTests.CseProgram), Is.EqualTo(OptimizerOrphanedTempTests.CseExpected));

    [Test]
    public void CommonSubexpressionInCompareAndReturn_Runs_Cpp()
        => Assert.That(Cpp(OptimizerOrphanedTempTests.CseProgram), Is.EqualTo(OptimizerOrphanedTempTests.CseExpected));

    [Test]
    public void CommonSubexpressionInCompareAndReturn_Runs_JavaScript()
        => Assert.That(Js(OptimizerOrphanedTempTests.CseProgram), Is.EqualTo(OptimizerOrphanedTempTests.CseExpected));
}
