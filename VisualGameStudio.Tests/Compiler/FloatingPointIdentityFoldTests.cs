using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// The optimizer's algebraic identities hold for INTEGERS and are false for IEEE 754, so the
/// unsafe ones must not fire on Single/Double. The optimizer is shared, so each wrong fold moved
/// every backend together: C# and C++ agreed on every wrong answer below, which is why
/// cross-backend agreement is not the oracle here — real .NET arithmetic is.
///
/// <para><b>MEASURED before the fix</b> (PeepholeOptimizationPass, standard pipeline):</para>
/// <code>
///   x - x   x = +Inf, NaN          printed 0     IEEE: NaN
///   x * 0   x = +Inf, NaN          printed 0     IEEE: NaN
///   x * 0   x = -5                 printed +0    IEEE: -0
///   x + 0   x = -0                 printed -0    IEEE: +0
///   x / x   x = 0, Inf, NaN        printed 1     IEEE: NaN
///   Single b - b, b = 1.0E+30F squared (= +Inf)  printed 0   IEEE: NaN
/// </code>
/// <para>And StrengthReductionPass turned a Double <c>x * 2</c> into <c>x &lt;&lt; 1</c> (it
/// checked only that the CONSTANT was an int): CS0019 on C#, "invalid operands to
/// operator&lt;&lt;" on C++, and a SILENT <c>1.5 * 2 = 2</c> on JavaScript. <c>x * 1</c> went
/// the same way (<c>x &lt;&lt; 0</c>) before the peephole could fold it.</para>
///
/// <para>Kept on floats because they ARE exact for every IEEE value: <c>x - (+0)</c>,
/// <c>x * 1</c>, <c>x / 1</c>. Integer folding is unchanged, and pinned below so it stays so.</para>
///
/// <para>Integration run half: <see cref="FloatingPointIdentityFoldRunTests"/>.</para>
/// </summary>
[TestFixture]
public class FloatingPointIdentityFoldTests
{
    /// <summary>
    /// The standard pipeline's view of <paramref name="expr"/> over a parameter <c>x</c> of type
    /// <paramref name="type"/>: the IRBinaryOps left in F after optimization. Stored in a named
    /// local, the shape the user's own repro used.
    /// </summary>
    private static IRBinaryOp[] OpsAfterOptimizing(string type, string expr)
    {
        var source =
            $"Function F(x As {type}) As {type}\n" +
            $"    Dim r As {type} = {expr}\n" +
            "    Return r\n" +
            "End Function\n" +
            "Sub Main()\n" +
            "End Sub\n";
        var ast = new Parser(new Lexer(source).Tokenize()).Parse();
        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            string.Join("; ", analyzer.Errors.Select(e => e.Message)));
        var module = new IRBuilder(analyzer).Build(ast, "FoldProbe");

        var pipeline = new OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.Run(module);

        return module.Functions.Single(f => f.Name == "F")
            .Blocks.SelectMany(b => b.Instructions).OfType<IRBinaryOp>().ToArray();
    }

    [TestCase("Double", "x - x", BinaryOpKind.Sub)]
    [TestCase("Double", "x * 0", BinaryOpKind.Mul)]
    [TestCase("Double", "0 * x", BinaryOpKind.Mul)]
    [TestCase("Double", "x + 0", BinaryOpKind.Add)]
    [TestCase("Double", "0 + x", BinaryOpKind.Add)]
    [TestCase("Double", "x / x", BinaryOpKind.Div)]
    [TestCase("Single", "x - x", BinaryOpKind.Sub)]
    [TestCase("Single", "x * 0", BinaryOpKind.Mul)]
    [TestCase("Single", "x + 0", BinaryOpKind.Add)]
    [TestCase("Single", "x / x", BinaryOpKind.Div)]
    public void FloatingIdentity_ThatIsFalseInIeee754_IsNotFolded(string type, string expr, BinaryOpKind op)
    {
        var ops = OpsAfterOptimizing(type, expr);
        Assert.That(ops.Any(o => o.Operation == op), Is.True,
            $"{type} `{expr}` was folded away; it is not an identity for Inf/NaN/-0");
    }

    [TestCase("Integer", "x - x", BinaryOpKind.Sub)]
    [TestCase("Integer", "x * 0", BinaryOpKind.Mul)]
    [TestCase("Integer", "0 * x", BinaryOpKind.Mul)]
    [TestCase("Integer", "x + 0", BinaryOpKind.Add)]
    [TestCase("Integer", "0 + x", BinaryOpKind.Add)]
    [TestCase("Integer", "x - 0", BinaryOpKind.Sub)]
    [TestCase("Long", "x - x", BinaryOpKind.Sub)]
    [TestCase("Long", "x * 0", BinaryOpKind.Mul)]
    [TestCase("Long", "x + 0", BinaryOpKind.Add)]
    [TestCase("Double", "x - 0", BinaryOpKind.Sub)]
    [TestCase("Double", "x * 1", BinaryOpKind.Mul)]
    // (No Double `x / 1` row: the builder widens a Double division's int operand to an IRCast,
    // so IsOne never matched it before this change either.)
    public void ExactIdentity_IsStillFolded(string type, string expr, BinaryOpKind op)
    {
        // The guard against overshooting: integer folding must stay exactly as it was, and the
        // three identities that ARE exact in IEEE 754 keep folding on floats too.
        var ops = OpsAfterOptimizing(type, expr);
        Assert.That(ops.Any(o => o.Operation == op), Is.False,
            $"{type} `{expr}` should still fold");
        Assert.That(ops.Any(o => o.Operation == BinaryOpKind.Shl), Is.False,
            $"{type} `{expr}` folded into a shift");
    }

    /// <summary>
    /// <c>x - (+0)</c> is exact and keeps folding on floats; <c>x - (-0)</c> is <c>x + 0</c>, which
    /// turns <c>-0</c> into <c>+0</c>, so it must not. Hand-built IR: from source, <c>x - -0.0</c>
    /// arrives as a unary negation, not a <c>-0.0</c> constant, so this guards the pass against
    /// any upstream fold that starts producing one.
    /// </summary>
    [TestCase(0.0, true)]
    [TestCase(-0.0, false)]
    public void FloatingSubtractZero_FoldsOnlyForPositiveZero(double zero, bool folds)
    {
        var dbl = new TypeInfo("Double", TypeKind.Primitive);
        var module = new IRModule("FoldProbe");
        var f = module.CreateFunction("F", dbl);
        var x = new IRVariable("x", dbl);
        f.Parameters.Add(x);
        var block = f.CreateBlock("entry");
        var sub = new IRBinaryOp("r", BinaryOpKind.Sub, x, new IRConstant(zero, dbl), dbl);
        block.AddInstruction(sub);
        block.AddInstruction(new IRReturn(sub));

        new PeepholeOptimizationPass().Run(module);

        Assert.That(block.Instructions.OfType<IRBinaryOp>().Any(o => o.Operation == BinaryOpKind.Sub),
            Is.EqualTo(!folds), $"x - ({zero:R}) folded={!folds}, expected folded={folds}");
    }

    /// <summary>
    /// <c>x / x</c> is not 1 at <c>x = 0</c> for ANY type: an integer division throws
    /// (DivideByZeroException) and a float one is NaN. The arm never checked <c>x != 0</c>, so it
    /// is gone. Hand-built IR because no source shape reaches an integral <c>Div</c> of one
    /// variable by itself (Integer <c>/</c> is a Double division over casts; <c>\</c> is IntDiv):
    /// this pins the pass itself, against a builder path that starts producing one.
    /// </summary>
    [TestCase("Integer")]
    [TestCase("Long")]
    public void IntegralDivideBySelf_IsNotFoldedToOne(string type)
    {
        var t = new TypeInfo(type, TypeKind.Primitive);
        var module = new IRModule("FoldProbe");
        var f = module.CreateFunction("F", t);
        var x = new IRVariable("x", t);
        f.Parameters.Add(x);
        var block = f.CreateBlock("entry");
        var div = new IRBinaryOp("r", BinaryOpKind.Div, x, x, t) { NamedAfterVariable = true };
        block.AddInstruction(div);
        block.AddInstruction(new IRReturn(div));

        new PeepholeOptimizationPass().Run(module);

        Assert.That(block.Instructions.OfType<IRBinaryOp>().Any(o => o.Operation == BinaryOpKind.Div),
            Is.True, $"{type} x / x was folded; at x = 0 it must throw, not yield 1");
    }

    [TestCase("Double", "x * 2")]
    [TestCase("Double", "x * 8")]
    [TestCase("Single", "x * 4")]
    public void FloatingMultiplyByPowerOfTwo_IsNotReducedToAShift(string type, string expr)
    {
        var ops = OpsAfterOptimizing(type, expr);
        Assert.That(ops.Any(o => o.Operation == BinaryOpKind.Shl), Is.False,
            $"{type} `{expr}` became a shift — CS0019 on C#, ill-formed C++, truncation on JavaScript");
        Assert.That(ops.Any(o => o.Operation == BinaryOpKind.Mul), Is.True);
    }

    [TestCase("Integer", "x * 2")]
    [TestCase("Long", "x * 8")]
    public void IntegralMultiplyByPowerOfTwo_IsStillReducedToAShift(string type, string expr)
    {
        var ops = OpsAfterOptimizing(type, expr);
        Assert.That(ops.Any(o => o.Operation == BinaryOpKind.Shl), Is.True,
            $"{type} `{expr}` should still strength-reduce");
    }
}

/// <summary>
/// The run half of <see cref="FloatingPointIdentityFoldTests"/>: the programs compiled THROUGH
/// the optimizer and executed, with stdout as the oracle. The expected text is what .NET itself
/// computes for the same arithmetic (checked against a hand-written C# program, not BasicLang).
///
/// <para>Values are classified in BasicLang (NaN / ±Inf / ±0) rather than printed raw: the C++
/// runtime spells NaN <c>-nan</c> on glibc and <c>-nan(ind)</c> on MSVC. Every result is stored in
/// a named local before use — passing <c>x - x</c> straight to a call is a separate, pre-existing
/// peephole defect (the folded temp is never declared) that this fixture does not exercise.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class FloatingPointIdentityFoldRunTests
{
    private const string Harness = @"
Function Mk(a As Double, b As Double) As Double
    Return a / b
End Function

Function Cls(v As Double) As String
    If v <> v Then
        Return ""NaN""
    ElseIf v > 1.0E+308 Then
        Return ""+Inf""
    ElseIf v < -1.0E+308 Then
        Return ""-Inf""
    ElseIf v = 0 Then
        If 1.0 / v < 0 Then
            Return ""-0""
        End If
        Return ""+0""
    End If
    Return """"
End Function

Sub Show(label As String, v As Double)
    Dim k As String = Cls(v)
    If k = """" Then
        Console.WriteLine(label)
        Console.WriteLine(v)
    Else
        Console.WriteLine(label & "" "" & k)
    End If
End Sub

Sub Probe(name As String, x As Double)
    Console.WriteLine(""== "" & name)
    Dim r0 As Double = x - x
    Show(""x-x"", r0)
    Dim r1 As Double = x * 0
    Show(""x*0"", r1)
    Dim r2 As Double = 0 * x
    Show(""0*x"", r2)
    Dim r3 As Double = x + 0
    Show(""x+0"", r3)
    Dim r4 As Double = 0 + x
    Show(""0+x"", r4)
    Dim r5 As Double = x - 0
    Show(""x-0"", r5)
    Dim r6 As Double = x * 1
    Show(""x*1"", r6)
    Dim r7 As Double = x * 2
    Show(""x*2"", r7)
    Dim r8 As Double = x / 1
    Show(""x/1"", r8)
    Dim r9 As Double = x / x
    Show(""x/x"", r9)
End Sub
";

    private const string DoubleProgram = Harness + @"
Sub Main()
    Dim inf As Double = Mk(1.0, 0.0)
    Probe(""+Inf"", inf)
    Probe(""NaN"", Mk(0.0, 0.0))
    Probe(""-0"", Mk(-1.0, inf))
    Probe(""-5"", -5.0)
    Probe(""3"", 3.0)
End Sub
";

    private const string DoubleExpected =
        "== +Inf\nx-x NaN\nx*0 NaN\n0*x NaN\nx+0 +Inf\n0+x +Inf\nx-0 +Inf\nx*1 +Inf\nx*2 +Inf\nx/1 +Inf\nx/x NaN\n" +
        "== NaN\nx-x NaN\nx*0 NaN\n0*x NaN\nx+0 NaN\n0+x NaN\nx-0 NaN\nx*1 NaN\nx*2 NaN\nx/1 NaN\nx/x NaN\n" +
        "== -0\nx-x +0\nx*0 -0\n0*x -0\nx+0 +0\n0+x +0\nx-0 -0\nx*1 -0\nx*2 -0\nx/1 -0\nx/x NaN\n" +
        "== -5\nx-x +0\nx*0 -0\n0*x -0\nx+0\n-5\n0+x\n-5\nx-0\n-5\nx*1\n-5\nx*2\n-10\nx/1\n-5\nx/x\n1\n" +
        "== 3\nx-x +0\nx*0 +0\n0*x +0\nx+0\n3\n0+x\n3\nx-0\n3\nx*1\n3\nx*2\n6\nx/1\n3\nx/x\n1";

    /// <summary>The user's own repro: Single overflow to +Inf, then Inf - Inf.</summary>
    private const string SingleProgram = Harness + @"
Sub ProbeSingle(a As Single)
    Dim b As Single = a * a
    Dim c As Single = b - b
    Show(""single b-b"", c)
End Sub

Sub Main()
    ProbeSingle(1.0E+30F)
End Sub
";

    private static string Norm(string s) => FourBackends.Norm(s);

    [Test]
    public void DoubleIdentities_MatchIeee754_CSharp()
        => Assert.That(Norm(FourBackends.RunEmittedCSharp(DoubleProgram)), Is.EqualTo(DoubleExpected));

    [Test]
    public void DoubleIdentities_MatchIeee754_Cpp()
        => Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(DoubleProgram))), Is.EqualTo(DoubleExpected));

    /// <summary>
    /// JavaScript is where the strength-reduction half was SILENT (<c>x * 2</c> → <c>x &lt;&lt; 1</c>
    /// truncates to int32), so it earns a row even though the peephole half is shared.
    /// ⚠ Through <c>CompileOptimized</c>, NOT <c>JavaScriptExecutionTests.RunJs</c>: that one
    /// runs no optimizer, and this row passed against the unfixed pass through it (measured).
    /// </summary>
    [Test]
    public void DoubleIdentities_MatchIeee754_JavaScript()
        => Assert.That(
            Norm(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.CompileOptimized(DoubleProgram))),
            Is.EqualTo(DoubleExpected));

    [Test]
    public void SingleInfMinusInf_IsNaN_CSharp()
        => Assert.That(Norm(FourBackends.RunEmittedCSharp(SingleProgram)), Is.EqualTo("single b-b NaN"));

    // No JavaScript row: JS has no 32-bit float, so 1.0E+30F squared does not overflow there
    // (a separate, pre-existing Single-semantics gap).
    [Test]
    public void SingleInfMinusInf_IsNaN_Cpp()
        => Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(SingleProgram))), Is.EqualTo("single b-b NaN"));
}
