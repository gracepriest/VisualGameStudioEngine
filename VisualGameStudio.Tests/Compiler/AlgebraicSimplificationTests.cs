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
    /// ⭐ BOTH ORIENTATIONS OF THE GATE, DIRECTLY ON THE PASS — <c>2 * call</c> (constant on the
    /// LEFT) and <c>call * 2</c> (constant on the RIGHT) each write the SAME call object into
    /// both slots of the rewrite unless gated. ADR-0001/ADR-0004 D4's fix guards each arm's own
    /// branch with <c>IRReplicability.IsReplicable</c> on the OTHER operand (the non-constant
    /// one) — a mutant that checks only one orientation (e.g. always tests
    /// <c>binOp.Left</c> regardless of which side the constant is on) lets the other orientation's
    /// call get duplicated. This test constructs the IR directly, one <c>IRCall</c> as the
    /// non-constant operand, and runs ONLY <c>AlgebraicSimplificationPass</c> — no front end, no
    /// CSE — so it is a probe of this one pass's gate and nothing else.
    /// </summary>
    [Test]
    [TestCase(true, TestName = "TheArmIsGated_ConstantOnTheLeft_2TimesCall")]
    [TestCase(false, TestName = "TheArmIsGated_ConstantOnTheRight_CallTimes2")]
    public void TheArmIsGated_RegardlessOfWhichSideTheConstantIsOn(bool constantOnLeft)
    {
        var module = new IRModule("AlgebraicOrientationProbe");
        var function = new IRFunction("F", IntType);
        var entry = new BasicBlock("entry");

        var call = new IRCall("t0", "Tag", IntType);
        var two = new IRConstant(2, IntType);
        var mul = constantOnLeft
            ? new IRBinaryOp("t1", BinaryOpKind.Mul, two, call, IntType)
            : new IRBinaryOp("t1", BinaryOpKind.Mul, call, two, IntType);
        var ret = new IRReturn(mul);

        entry.AddInstruction(call);
        entry.AddInstruction(mul);
        entry.AddInstruction(ret);
        function.Blocks.Add(entry);
        function.EntryBlock = entry;
        module.Functions.Add(function);

        var pipeline = new OptimizationPipeline();
        pipeline.AddPass(new AlgebraicSimplificationPass());
        pipeline.Run(module);

        var survivingBinOps = entry.Instructions.OfType<IRBinaryOp>().ToList();
        Assert.That(survivingBinOps, Has.Count.EqualTo(1));
        var op = survivingBinOps[0];

        Assert.Multiple(() =>
        {
            Assert.That(op.Operation, Is.EqualTo(BinaryOpKind.Mul),
                (constantOnLeft ? "`2 * Tag()`" : "`Tag() * 2`")
                + " must stay a MULTIPLY — the non-constant operand is a call, not replicable, on "
                + (constantOnLeft ? "the RIGHT" : "the LEFT")
                + " of the multiply. If this is Add, the gate checked the wrong operand for this "
                + "orientation and the call is being duplicated.");
            Assert.That(op.Left, Is.Not.SameAs(op.Right),
                "no instruction may hold the same call object in both operand slots");
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

    /// <summary>
    /// ⭐ ADR-0001/ADR-0004 D4's gate, at the IR level, through the FULL aggressive pipeline (not
    /// just this one pass in isolation, the way <see cref="TheConsumerIsRePointedAtTheReplacement_NotTheDiscardedNode"/>
    /// does) — <c>2 * Tag()</c> must stay <c>Mul(2, t0)</c>, never become <c>Add(t0, t0)</c> with
    /// the SAME call object in both slots. Asserted structurally rather than by running the
    /// program, because it is the shape the C# oracle's use-count materialisation depends on:
    /// see <c>Family111MaterialisationBehaviourTests</c> for the same guarantee proved by output.
    /// </summary>
    [Test]
    [TestCase("Integer", "3", TestName = "TheArmDoesNotFire_ForANonReplicableCallOperand_Integer")]
    [TestCase("Double", "1.5", TestName = "TheArmDoesNotFire_ForANonReplicableCallOperand_Double")]
    public void TheArmDoesNotFire_ForANonReplicableCallOperand(string type, string literal)
    {
        var program = $"""
            Function Tag() As {type}
             Console.WriteLine("tag")
             Return {literal}
            End Function

            Sub Main()
             Dim r As {type} = 2 * Tag()
             Console.WriteLine(r)
            End Sub
            """;

        var module = JsTestSupport.BuildModule(program, sourceFilePath: "prog.bas");
        AggressivePipeline.Apply(module);

        var main = module.Functions.Single(f => f.Name == "Main");
        var binOps = main.Blocks.SelectMany(b => b.Instructions).OfType<IRBinaryOp>().ToList();

        Assert.That(binOps, Has.Count.EqualTo(1),
            "expected exactly one surviving IRBinaryOp for `2 * Tag()`; found: "
            + string.Join(", ", binOps.Select(b => $"{b.Operation}({b.Left?.Name},{b.Right?.Name})")));

        var op = binOps[0];
        Assert.Multiple(() =>
        {
            Assert.That(op.Operation, Is.EqualTo(BinaryOpKind.Mul),
                "the gate must keep `2 * Tag()` as a MULTIPLY — the operand (a call) is not "
                + "replicable, so the 2*x -> x+x rewrite must not fire. If this is Add, the call "
                + "is being evaluated twice (`Tag() + Tag()`), which prints \"tag\" an extra time "
                + "on the C# oracle (ADR-0001's measured instance).");

            var callOperand = (op.Left as IRCall) ?? (op.Right as IRCall);
            Assert.That(callOperand, Is.Not.Null, "expected one operand to be the Tag() call");
            Assert.That(op.Left, Is.Not.SameAs(op.Right),
                "no instruction may hold the SAME call object in both operand slots — that is a "
                + "use count of 2 on a side-effecting call, which is exactly the hazard this gate "
                + "closes (ADR-0001 Obligations, ADR-0004 D4)");
        });
    }

    /// <summary>
    /// ⭐ THE POSITIVE CONTROL — the gate must not disable the rewrite wholesale. For a REPLICABLE
    /// operand (here, a parameter — no side effect, safe to evaluate twice), `2 * x` must still
    /// become `x + x`, exactly as <see cref="TheSurvivingArm_StillFiresAndIsCorrect"/> already
    /// proves by VALUE; this pins the same case at the IR level, alongside the negative case
    /// above, so the two read as one gate rather than two unrelated assertions.
    /// </summary>
    [Test]
    public void TheArmStillFires_ForAReplicableParameterOperand()
    {
        var program = """
            Function Double2(x As Integer) As Integer
             Return 2 * x
            End Function

            Sub Main()
             Console.WriteLine(Double2(6))
            End Sub
            """;

        var module = JsTestSupport.BuildModule(program, sourceFilePath: "prog.bas");
        AggressivePipeline.Apply(module);

        var fn = module.Functions.Single(f => f.Name == "Double2");
        var binOps = fn.Blocks.SelectMany(b => b.Instructions).OfType<IRBinaryOp>().ToList();

        Assert.That(binOps, Has.Count.EqualTo(1),
            "expected exactly one surviving IRBinaryOp for `2 * x`");

        var op = binOps[0];
        Assert.Multiple(() =>
        {
            Assert.That(op.Operation, Is.EqualTo(BinaryOpKind.Add),
                "for a replicable operand (a parameter) the rewrite must still fire — the gate is "
                + "specific to non-replicable operands, not a wholesale disabling of the arm");
            Assert.That(op.Left, Is.SameAs(op.Right),
                "duplicating a replicable operand object is exactly what the surviving arm does "
                + "(both slots are the SAME IRVariable) — harmless here because re-reading a "
                + "parameter has no side effect");
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

    /// <summary>
    /// ⚠ Was a private copy of "build the module, <c>AddAggressivePasses</c>, run, generate JS",
    /// identical to <c>FunctionInliningDisabledTests</c>'. Both are now
    /// <c>JsTestSupport.CompileAggressive</c>, which goes through
    /// <c>AggressivePipeline.Apply</c> — ONE definition of aggressive for every backend, per the
    /// <c>ModuleResolver</c>/<c>ModuleTypeWalker</c> rule in CLAUDE.md. Those two copies were the
    /// ONLY aggressive execution anywhere in the suite, and both were JavaScript-only. See
    /// <c>AggressivePipeline</c>.
    /// </summary>
    private static string Aggressive(string source) =>
        JsTestSupport.CompileAggressive(source);

    private static string RunAggressive(string source) =>
        JavaScriptExecutionTests.RunNodeScript(Aggressive(source));
}
