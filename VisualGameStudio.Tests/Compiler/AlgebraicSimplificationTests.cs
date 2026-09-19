using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>AlgebraicSimplificationPass</c>: the missing <c>ReplaceUses</c>, and the three UNSOUND arms
/// that only the missing <c>ReplaceUses</c> was keeping harmless.
///
/// <para>⛔ <c>Return (a + b) - b</c> under <c>--optimize</c> was a <c>ReferenceError</c>. The pass
/// swapped <c>block.Instructions[i]</c> and never re-pointed the CONSUMERS, which the base class's
/// <c>ReplaceUses</c> doc says a pass doing so MUST:</para>
///
/// <code>
/// const t0 = ((a + b) | 0);
/// t1 = a;                             // UNDECLARED
/// return ((((a + b) | 0) - b) | 0);   // consumer re-materialised the WHOLE original expression
/// </code>
///
/// <para>⛔ THE ARMS ARE UNSOUND, AND THE BROKEN MACHINERY IS THE ONLY REASON NOBODY SAW A WRONG
/// ANSWER — the same story the file already records for the Div and Mod arms removed from
/// <c>StrengthReductionPass</c>. Measured, correct answer first:</para>
///
/// <list type="bullet">
/// <item><c>(a + b) - b</c> with a=1e-19, b=1e18 is <b>0</b>; the arm gives <c>a</c>. Catastrophic
/// cancellation — adding b makes a vanish and subtracting it does not bring a back.</item>
/// <item><c>(a * b) / b</c> with b=0 is <b>NaN</b>; the arm gives <c>a</c>. Its own comment claimed
/// "when b != 0" and the code NEVER CHECKED IT.</item>
/// <item><c>(a * b) / b</c> with a=0.1, b=3 is <b>0.10000000000000002</b>; the arm gives 0.1. Plain
/// rounding: the round trip is not the identity.</item>
/// </list>
///
/// <para>⛔ ADDING <c>ReplaceUses</c> ALONE DOES NOT FIX THE CRASH, which was measured rather than
/// assumed and is why this change does two things. With the arm restored and <c>ReplaceUses</c>
/// working, the consumer IS correctly re-pointed — <c>return t1;</c> instead of the re-materialised
/// expression — but the emission is still <c>t1 = a;</c> with <c>t1</c> UNDECLARED, because those
/// arms introduce a brand-new <c>IRVariable</c> target that nothing adds to the function's
/// <c>LocalVariables</c>. That is a SECOND, independent defect (the same one that sank
/// <c>FunctionInliningPass</c>). Three independent reasons to delete the arms rather than repair
/// them.</para>
///
/// <para>⚠ <c>2 * x -> x + x</c> is KEPT: it is sound on both fronts — <c>x + x</c> is exactly
/// <c>2 * x</c> in IEEE 754, and it wraps identically on integer overflow — and it is the only arm
/// that ever worked, because its replacement is a VALUE carrying the same name, so the orphaned
/// consumer resolved by NAME COINCIDENCE. The base class doc warns that carrying the name is not
/// enough; this pass is the demonstration.</para>
/// </summary>
[TestFixture]
public class AlgebraicSimplificationTests
{
    private static readonly TypeInfo IntType = new TypeInfo("Integer", TypeKind.Primitive);

    /// <summary>
    /// ⛔ THE test that makes <c>ReplaceUses</c> OBSERVABLE. With the unsound arms gone, the
    /// surviving arm's replacement carries the same NAME, so the emitted text is identical whether
    /// or not consumers are re-pointed — measured, and every end-to-end shape in this fixture
    /// passes with the call removed. Asserting the IR directly is what gives the call teeth:
    /// the consumer must hold the REPLACEMENT INSTANCE, not the discarded one.
    ///
    /// <para>⚠ That distinction is the whole contract. A future arm whose replacement carries a
    /// different name — or an <c>IRAssignment</c>, as the deleted ones did — reintroduces the
    /// orphaned consumer silently, and only a reference-identity assertion catches it.</para>
    /// </summary>
    [Test]
    public void TheConsumerIsRePointedAtTheReplacement_NotTheDiscardedNode()
    {
        var module = new IRModule("AlgebraicProbe");

        var function = new IRFunction("F", IntType);
        var entry = new BasicBlock("entry");

        // t0 = 2 * x  — the surviving `2 * x -> x + x` arm.
        var mul = new IRBinaryOp("t0", BinaryOpKind.Mul,
            new IRConstant(2, IntType), new IRVariable("x", IntType), IntType);
        var ret = new IRReturn(mul);   // the consumer holds the NODE, not a variable

        entry.AddInstruction(mul);
        entry.AddInstruction(ret);
        function.Blocks.Add(entry);
        function.EntryBlock = entry;
        module.Functions.Add(function);

        var pipeline = new OptimizationPipeline();
        pipeline.AddPass(new AlgebraicSimplificationPass());
        pipeline.Run(module);

        var replacement = entry.Instructions.OfType<IRBinaryOp>().SingleOrDefault();

        Assert.That(replacement, Is.Not.Null, "the arm did not fire at all");
        Assert.Multiple(() =>
        {
            Assert.That(replacement!.Operation, Is.EqualTo(BinaryOpKind.Add),
                "2 * x should have become x + x");
            Assert.That(ret.Value, Is.SameAs(replacement),
                "the consumer must hold the REPLACEMENT instance. Holding the discarded node is "
                + "the defect: backends key temporaries by object identity, so an orphaned "
                + "consumer renders an identifier that is never declared — or, as measured here, "
                + "re-materialises the whole original expression inline.");
            Assert.That(ret.Value, Is.Not.SameAs(mul),
                "and specifically not the node that was swapped out");
        });
    }

    /// <summary>
    /// ⛔ The shapes that were a <c>ReferenceError</c>. They now run, and agree with the standard
    /// pipeline — which is the property, since a pipeline-specific answer is the failure mode this
    /// whole area keeps producing.
    /// </summary>
    [Test]
    [Category("Integration")]
    [TestCase("(a + b) - b", "4")]
    [TestCase("(a - b) + b", "4")]
    public void TheFormerlyCrashingShapes_RunAndAgreeAcrossPipelines(string expression, string expected)
    {
        var program = $"""
            Module M
             Function F(a As Integer, b As Integer) As Integer
              Return {expression}
             End Function
             Sub Main()
              PrintLine(CStr(F(4, 9)))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo(expected));
            Assert.That(JavaScriptOptimizedExecutionTests.RunOptimized(program), Is.EqualTo(expected));
            Assert.That(RunAggressive(program), Is.EqualTo(expected),
                "the AGGRESSIVE pipeline, where this pass runs — this was a ReferenceError");
        });
    }

    /// <summary>
    /// ⛔ THE SOUNDNESS CASES. Each is a value the deleted arms would have got WRONG, and each is
    /// asserted against the arithmetic truth rather than merely "both pipelines agree" — two
    /// pipelines can agree on a wrong answer.
    /// </summary>
    [Test]
    [Category("Integration")]
    [TestCase("(a + b) - b", "0.0000000000000000001", "1000000000000000000.0", "0",
        TestName = "CatastrophicCancellation_AddThenSubtractIsNotTheIdentity")]
    [TestCase("(a * b) / b", "4.0", "0.0", "NaN",
        TestName = "DivisionByZero_TheArmsOwnPreconditionWasNeverChecked")]
    [TestCase("(a * b) / b", "0.1", "3.0", "0.10000000000000002",
        TestName = "FloatRounding_TheRoundTripIsNotTheIdentity")]
    public void TheDeletedArmsWouldHaveBeenWrong(string expression, string a, string b, string expected)
    {
        var program = $"""
            Module M
             Function F(a As Double, b As Double) As Double
              Return {expression}
             End Function
             Sub Main()
              PrintLine(CStr(F({a}, {b})))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(RunAggressive(program), Is.EqualTo(expected),
                "the aggressive pipeline must produce the ARITHMETICALLY CORRECT value, not the "
                + "simplified one — this is what the deleted arms got wrong");
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo(expected),
                "and it must match the pipeline that never simplified");
        });
    }

    /// <summary>
    /// ⚠ The surviving arm still fires and is still correct, across the consumer shapes that reach
    /// it. Without these the change could have "fixed" the crash by simply making the pass do
    /// nothing at all.
    /// </summary>
    [Test]
    [Category("Integration")]
    [TestCase("Return 2 * p", "12")]
    [TestCase("Return (2 * p) + 1", "13")]
    [TestCase("Return (2 * p) * 3", "36")]
    public void TheSurvivingArm_StillFiresAndIsCorrect(string body, string expected)
    {
        var program = $"""
            Module M
             Function F(p As Integer) As Integer
              {body}
             End Function
             Sub Main()
              PrintLine(CStr(F(6)))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(RunAggressive(program), Is.EqualTo(expected));
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo(expected));
        });
    }

    /// <summary>⚠ And it really is the rewrite, not the original multiply, reaching the output.</summary>
    [Test]
    public void TheSurvivingArm_EmitsAnAdditionRatherThanAMultiply()
    {
        var js = Aggressive("""
            Module M
             Function F(p As Integer) As Integer
              Return 2 * p
             End Function
             Sub Main()
              PrintLine(CStr(F(6)))
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(js, Does.Contain("(p + p)"), js);
            Assert.That(js, Does.Not.Contain("Math.imul(2, p)"),
                "the multiply should have been rewritten:\n" + js);
        });
    }

    private static string Aggressive(string source)
    {
        var module = JsTestSupport.BuildModule(source, sourceFilePath: "prog.bas");

        var pipeline = new OptimizationPipeline();
        pipeline.AddAggressivePasses();
        pipeline.Run(module);

        return new BasicLang.Compiler.CodeGen.JavaScript.JavaScriptCodeGenerator().Generate(module);
    }

    private static string RunAggressive(string source) =>
        JavaScriptExecutionTests.RunNodeScript(Aggressive(source));
}
