using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.CodeGen.JavaScript;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A write to a class member was SILENTLY DISCARDED when the optimizer rewrote its right-hand
/// side.
///
/// <para>⛔ <c>K = p * 2</c> on a <c>Shared</c> field printed the field's INITIAL value. Strength
/// reduction rewrites the multiply to a shift, and the replacement carried the old
/// <c>Name</c> and <c>SourceLine</c> but not <see cref="IRValue.NamedAfterVariable"/> — the flag
/// that tells a backend "this result IS the assignment to K" rather than "a temp that happens to
/// share K's name". With it false, JavaScript emitted <c>const K = (p &lt;&lt; 1);</c>, a fresh
/// local, and C# emitted an <b>EMPTY METHOD BODY</b>. Nothing failed to compile and a plausible
/// number was printed.</para>
///
/// <para>⛔ ONLY THE OPTIMIZED PATH WAS WRONG, which is why a green suite never saw it. The
/// non-optimizing helper lowers the same source correctly, so every test written against it passed.
/// <c>StrengthReductionPass</c> is in <c>AddStandardPasses</c>, so every shipping route hit the
/// broken path. This is exactly the hazard CLAUDE.md names, and the fixture asserts the OPTIMIZED
/// pipeline throughout.</para>
///
/// <para>⛔ TWO BACKENDS WERE RIGHT AND TWO WERE SILENTLY WRONG. C++ and MSIL never consult the
/// flag and always emitted the store; JavaScript and C# both do, and both lost it — differently.
/// So the defect is ONE omission in the IR, not two backend bugs, and the fix belongs in the pass
/// rather than in either backend.</para>
///
/// <para>⚠ The trigger is narrow and measured: multiplication by a POWER OF TWO. <c>K = p * 3</c>
/// (<c>Math.imul</c>), <c>p * p</c>, <c>p + 1</c>, <c>p - 1</c>, <c>p \ 2</c>, <c>p Mod 4</c> and
/// <c>-p</c> were all correct before and after — the Div and Mod strength-reduction arms were
/// removed long ago as unsound, and Peephole's rewrites produce an <c>IRAssignment</c> with an
/// explicit target, which never depended on the flag. Those shapes are asserted as regression
/// baselines, because "the fix is scoped" is a claim that needs evidence.</para>
///
/// <para>⚠ Only CLASS MEMBERS were affected. A module-level global and a local resolve through
/// their own arms before the member arm is reached, so both were always correct — asserted below
/// for the same reason.</para>
/// </summary>
[TestFixture]
public class StrengthReductionIdentityTests
{
    private static string Program(string body) => $"""
        Class Box
         Public Shared K As Integer = 1
         Public Shared Sub Put(p As Integer)
          {body}
         End Sub
         Public Shared Function Raw() As Integer
          Return K
         End Function
        End Class

        Module M
         Sub Main()
          Box.Put(6)
          PrintLine(CStr(Box.Raw()))
         End Sub
        End Module
        """;

    /// <summary>
    /// ⛔ THE headline, on the pipeline that ships. Reads the field back through a separate method,
    /// so a write that never lands cannot be masked by reading the same expression again.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void ASharedFieldAssignedFromAComputedExpression_IsActuallyWritten()
    {
        var program = Program("K = p * 2");

        Assert.Multiple(() =>
        {
            Assert.That(JavaScriptOptimizedExecutionTests.RunOptimized(program), Is.EqualTo("12"),
                "the OPTIMIZED path — this printed 1, the field's initial value");
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("12"),
                "and the non-optimizing path, which was always correct");
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("12\n"),
                "MSIL never consulted the flag and was always correct");
        });
    }

    /// <summary>
    /// ⛔ The emitted JavaScript: a write to the field, not a declaration of a new local. The
    /// defect and the fix are both visible in this one line.
    /// </summary>
    [Test]
    public void TheEmittedJavaScript_AssignsTheField_NotAFreshLocal()
    {
        var js = JsTestSupport.CompileOptimized(Program("K = p * 2"));

        Assert.Multiple(() =>
        {
            Assert.That(js, Does.Contain("Box.K = (p << 1)"), js);
            Assert.That(js, Does.Not.Contain("const K ="),
                "a fresh local here silently throws the write away:\n" + js);
        });
    }

    /// <summary>
    /// ⛔ C# lost the statement ENTIRELY — the method body came out empty — so it needs its own
    /// assertion. The same flag, a different symptom, one root cause.
    /// </summary>
    [Test]
    public void TheEmittedCSharp_StillContainsTheStore()
    {
        var program = Program("K = p * 2");

        Assert.Multiple(() =>
        {
            Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(program), Is.Empty,
                "the emitted C# must still compile clean");
            Assert.That(CSharpFor(program), Does.Contain("K = p << 1"),
                "C# emitted an EMPTY method body before this fix");
        });
    }

    /// <summary>⚠ An INSTANCE field takes the same path and was equally broken.</summary>
    [Test]
    [Category("Integration")]
    public void AnInstanceFieldAssignedFromAComputedExpression_IsActuallyWritten()
    {
        const string program = """
            Class Box
             Private N As Integer = 1
             Public Sub Put(p As Integer)
              N = p * 2
             End Sub
             Public Function Raw() As Integer
              Return N
             End Function
            End Class

            Module M
             Sub Main()
              Dim c As New Box()
              c.Put(6)
              PrintLine(CStr(c.Raw()))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(JavaScriptOptimizedExecutionTests.RunOptimized(program), Is.EqualTo("12"));
            Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)), Is.EqualTo("12\n"),
                "C++ was always correct — asserted so a regression there would show");
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("12\n"));
        });
    }

    /// <summary>⚠ Every power of two the pass reduces, not just the one that was reported.</summary>
    [Test]
    [Category("Integration")]
    [TestCase("K = p * 2", "12")]
    [TestCase("K = p * 4", "24")]
    [TestCase("K = p * 8", "48")]
    [TestCase("K = p * 16", "96")]
    public void EveryPowerOfTwoMultiplier_IsActuallyWritten(string body, string expected)
    {
        var program = Program(body);

        Assert.Multiple(() =>
        {
            Assert.That(JavaScriptOptimizedExecutionTests.RunOptimized(program), Is.EqualTo(expected));
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo(expected + "\n"));
        });
    }

    /// <summary>
    /// ⚠ REGRESSION BASELINES — shapes the pass never rewrote, so they were never affected. They
    /// are asserted because "the fix is narrowly scoped" is a claim, and because a future pass that
    /// starts rewriting one of these would reintroduce the defect silently.
    /// </summary>
    [Test]
    [Category("Integration")]
    [TestCase("K = p * 3", "18")]
    [TestCase("K = p * p", "36")]
    [TestCase("K = p + 1", "7")]
    [TestCase("K = p - 1", "5")]
    [TestCase("K = p \\ 2", "3")]
    [TestCase("K = p Mod 4", "2")]
    [TestCase("K = -p", "-6")]
    public void ShapesTheOptimizerDoesNotRewrite_WereAlwaysCorrect(string body, string expected)
    {
        var program = Program(body);

        Assert.Multiple(() =>
        {
            Assert.That(JavaScriptOptimizedExecutionTests.RunOptimized(program), Is.EqualTo(expected));
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo(expected + "\n"));
        });
    }

    /// <summary>
    /// ⚠ A module-level global and a LOCAL were never affected: both resolve through their own arm
    /// before the class-member arm is reached. Asserted so the scope claim has evidence.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void AModuleGlobalAndALocal_WereNeverAffected()
    {
        const string global = """
            Module M
             Dim G As Integer = 1
             Sub Put(p As Integer)
              G = p * 2
             End Sub
             Sub Main()
              Put(6)
              PrintLine(CStr(G))
             End Sub
            End Module
            """;

        const string local = """
            Module M
             Sub Main()
              Dim p As Integer = 6
              Dim L As Integer = 1
              L = p * 2
              PrintLine(CStr(L))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(JavaScriptOptimizedExecutionTests.RunOptimized(global), Is.EqualTo("12"));
            Assert.That(JavaScriptOptimizedExecutionTests.RunOptimized(local), Is.EqualTo("12"));
        });
    }

    /// <summary>
    /// ⛔ THE SECOND SITE, with the identical omission, in the AGGRESSIVE pipeline.
    /// <c>AlgebraicSimplificationPass</c> rewrites <c>2 * x</c> to <c>x + x</c> and built its
    /// replacement the same way. Strength reduction does not fire for <c>2 * p</c> — its arm only
    /// matches a constant on the RIGHT — so this shape reaches the algebraic pass and was broken
    /// through a different rewrite. Measured: <c>K = 2 * p</c> under <c>--optimize</c> printed 1.
    ///
    /// <para>⚠ Fixing only the standard-pipeline site would have left this one silently broken by
    /// the same missing line, which is the half-work hazard this repo keeps running into.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void TheAggressivePipelinesAlgebraicRewrite_AlsoWritesTheField()
    {
        var module = JsTestSupport.BuildModule(Program("K = 2 * p"), sourceFilePath: "prog.bas");

        var pipeline = new OptimizationPipeline();
        pipeline.AddAggressivePasses();
        pipeline.Run(module);

        var js = new JavaScriptCodeGenerator().Generate(module);

        Assert.Multiple(() =>
        {
            Assert.That(js, Does.Contain("Box.K = ((p + p) | 0)"), js);
            Assert.That(js, Does.Not.Contain("const K ="), js);
            Assert.That(JavaScriptExecutionTests.RunNodeScript(js), Is.EqualTo("12"));
        });
    }

    /// <summary>
    /// ⚠ The replacement keeps BOTH halves of its identity, asserted on the IR directly rather than
    /// through emitted text.
    ///
    /// <para>The <c>SourceLine</c> half was already carried by hand and is now carried by the shared
    /// helper; dropping it makes a debug build emit a <c>#line</c> reset onto generated glue for a
    /// line the user wrote, so stepping lands in the wrong place. The
    /// <see cref="IRValue.NamedAfterVariable"/> half is this fix. Asserting the IR is what keeps
    /// the two from being confused: an emitted-text test cannot see the line number at all.</para>
    /// </summary>
    [Test]
    public void TheReducedValueKeepsItsNameFlagAndSourceLine()
    {
        var module = JsTestSupport.BuildModule(Program("K = p * 2"), sourceFilePath: "prog.bas");

        var pipeline = new OptimizationPipeline();
        pipeline.AddPass(new StrengthReductionPass());
        pipeline.Run(module);

        var shifted = module.Functions
            .SelectMany(f => f.Blocks)
            .SelectMany(b => b.Instructions)
            .OfType<IRBinaryOp>()
            .SingleOrDefault(op => op.Operation == BinaryOpKind.Shl);

        Assert.That(shifted, Is.Not.Null, "strength reduction did not produce a shift at all");
        Assert.Multiple(() =>
        {
            Assert.That(shifted!.Name, Is.EqualTo("K"), "the replacement keeps the name");
            Assert.That(shifted.NamedAfterVariable, Is.True,
                "and the flag that makes it an ASSIGNMENT rather than a temp");
            Assert.That(shifted.SourceLine, Is.GreaterThan(0),
                "and the source line, or a debug build steps into generated glue");
        });
    }

    /// <summary>Emitted C# for a program, through the same route the C# fixtures use.</summary>
    private static string CSharpFor(string source)
    {
        var module = JsTestSupport.BuildModule(source, sourceFilePath: "prog.bas");

        var pipeline = new OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.Run(module);

        return new BasicLang.Compiler.CodeGen.CSharp.CSharpCodeGenerator().Generate(module);
    }
}
