using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.CodeGen.JavaScript;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>FunctionInliningPass</c> is DISABLED, and this fixture is the evidence for why plus the
/// guard that it stays out of the shipping pipeline.
///
/// <para>⛔ IT NEVER PRODUCED CORRECT OUTPUT FOR ANY FUNCTION IT ACTUALLY INLINED, and it
/// miscompiled SILENTLY — a clean build, then a <c>ReferenceError</c> at run time. Measured on
/// <c>Function F(p As Integer) As Integer : Return p * 2</c> called as <c>F(6)</c>, the aggressive
/// pipeline produced this <c>Main</c>:</para>
///
/// <code>
/// _inline_t1_0 = 12;        // undeclared, and nothing reads it
/// t1 = (p &lt;&lt; 1);            // undeclared t1, and the CALLEE'S PARAMETER p leaked in
/// const t0 = String(F(6));  // ...and the original call still happens
/// </code>
///
/// <para>⛔ SIX OF SEVEN call shapes failed at run time. The seventh passed only because
/// <c>IsInlineable</c> REFUSES it for block count — so there was no shape where inlining
/// succeeded. All seven were correct without the pass, which is what the parameterised test below
/// pins on BOTH pipelines.</para>
///
/// <para>⛔ FIVE SEPARATE DEFECTS, which is why this is a rewrite rather than a patch: inlined
/// locals are never added to the caller's <c>LocalVariables</c>; a definition is renamed by a temp
/// counter while its USES are renamed by <c>prefix + name</c>, two schemes that can never agree;
/// <c>RemapValue</c> rewrites only an <c>IRVariable</c> and returns any nested operand tree
/// untouched, leaking the callee's own variables; <c>InlineCallsInBlock</c> never calls
/// <c>ReplaceUses</c>, so consumers still reference the removed call and the callee runs anyway;
/// and <c>depth</c> is passed 0 and never incremented, making <c>_maxInlineDepth</c> dead.</para>
///
/// <para>⚠ The PASS ITSELF IS KEPT, not deleted. Its <c>CloneAndRemap</c> is still the only clone
/// path an <c>IRCall</c> can reach, and <c>NetIrCarriageTests</c> guards the .NET resolution
/// carriage through it by constructing the pass explicitly. Deleting the class would silently
/// delete that protection — which is why those four tests were changed to add the pass themselves
/// rather than relying on the pipeline.</para>
///
/// <para>⚠ This follows the precedent already set one method above it in the same file:
/// <c>ConstantPropagationPass</c> is commented out of <c>AddStandardPasses</c> with
/// "incorrectly propagates across control flow merges". Inlining buys nothing here in any case —
/// clang, the CLR JIT and V8 all inline far better than this pass could, downstream.</para>
/// </summary>
[TestFixture]
public class FunctionInliningDisabledTests
{
    /// <summary>
    /// ⛔ THE payoff: every shape the pass used to mangle now runs correctly under
    /// <c>--optimize</c>, AND agrees with the standard pipeline. Asserting both is the point —
    /// "the optimized path agrees with the unoptimized one" is the property, and a test that
    /// checked only one of them could not see a pipeline-specific miscompile at all. That is
    /// exactly how the discarded-store defect survived a green suite.
    /// </summary>
    [Test]
    [Category("Integration")]
    [TestCase("a plain call", "12", """
        Module M
         Function F(p As Integer) As Integer
          Return p * 2
         End Function
         Sub Main()
          PrintLine(CStr(F(6)))
         End Sub
        End Module
        """)]
    [TestCase("a callee with a local", "24", """
        Module M
         Function F(p As Integer) As Integer
          Dim x As Integer = 2 * p
          Return x + x
         End Function
         Sub Main()
          PrintLine(CStr(F(6)))
         End Sub
        End Module
        """)]
    [TestCase("no arguments", "5", """
        Module M
         Function F() As Integer
          Return 5
         End Function
         Sub Main()
          PrintLine(CStr(F()))
         End Sub
        End Module
        """)]
    [TestCase("two arguments", "10", """
        Module M
         Function F(a As Integer, b As Integer) As Integer
          Return a + b
         End Function
         Sub Main()
          PrintLine(CStr(F(4, 6)))
         End Sub
        End Module
        """)]
    [TestCase("two calls in one expression", "11", """
        Module M
         Function F(p As Integer) As Integer
          Return p + 1
         End Function
         Sub Main()
          PrintLine(CStr(F(4) + F(5)))
         End Sub
        End Module
        """)]
    [TestCase("a Sub with a side effect", "7", """
        Module M
         Dim G As Integer = 0
         Sub S(p As Integer)
          G = p
         End Sub
         Sub Main()
          S(7)
          PrintLine(CStr(G))
         End Sub
        End Module
        """)]
    public void EveryCallShape_RunsTheSame_OnBothPipelines(string shape, string expected, string program)
    {
        Assert.Multiple(() =>
        {
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo(expected),
                $"{shape}: standard pipeline");
            Assert.That(RunAggressive(program), Is.EqualTo(expected),
                $"{shape}: AGGRESSIVE pipeline — this was a ReferenceError while inlining ran");
        });
    }

    /// <summary>
    /// ⚠ A callee with branching was the one shape that "worked", and only because
    /// <c>IsInlineable</c> skipped it. Kept so the set is complete rather than curated: a future
    /// re-enable that also widens <c>IsInlineable</c> must keep this right too.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void ABranchingCallee_WhichWasOnlyEverSkipped_StillRuns()
    {
        const string program = """
            Module M
             Function F(p As Integer) As Integer
              If p > 3 Then
               Return 6
              End If
              Return 0
             End Function
             Sub Main()
              PrintLine(CStr(F(4)))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("6"));
            Assert.That(RunAggressive(program), Is.EqualTo("6"));
        });
    }

    /// <summary>
    /// ⛔ THE GUARD that keeps the pass out: the aggressive pipeline must not introduce the
    /// inliner's rename prefix, and must not leave the call un-replaced behind garbage. Asserted on
    /// the emitted text because that is where the defect was visible — a run only shows its
    /// consequence, and a future re-enable of a still-broken pass would trip this first.
    /// </summary>
    [Test]
    public void TheAggressivePipeline_DoesNotInline()
    {
        const string program = """
            Module M
             Function F(p As Integer) As Integer
              Return p * 2
             End Function
             Sub Main()
              PrintLine(CStr(F(6)))
             End Sub
            End Module
            """;

        var js = Aggressive(program);

        Assert.Multiple(() =>
        {
            Assert.That(js, Does.Not.Contain("_inline_"),
                "the inliner's rename prefix must not appear in shipped output:\n" + js);
            Assert.That(js, Does.Contain("F(6)"), "the call stands:\n" + js);
            Assert.That(js, Does.Contain("function F(p)"), "and so does the callee:\n" + js);
        });
    }

    /// <summary>
    /// ⛔ THE PASS IS STILL REACHABLE AND STILL BROKEN, pinned deliberately. This is what makes
    /// "disabled" a measured claim rather than a comment: constructing the pass directly still
    /// reproduces the undeclared-identifier garbage, so the fixture documents the defect instead of
    /// hiding it behind the pipeline change.
    ///
    /// <para>It also protects the decision. If someone repairs the pass, this test goes RED, which
    /// is the signal to re-enable it in <c>AddAggressivePasses</c> and delete this test — not to
    /// weaken it.</para>
    /// </summary>
    [Test]
    public void RunDirectly_ThePassStillMiscompiles_WhichIsWhyItIsDisabled()
    {
        const string program = """
            Module M
             Function F(p As Integer) As Integer
              Return p * 2
             End Function
             Sub Main()
              PrintLine(CStr(F(6)))
             End Sub
            End Module
            """;

        var module = JsTestSupport.BuildModule(program, sourceFilePath: "prog.bas");

        var pipeline = new OptimizationPipeline();
        pipeline.AddPass(new FunctionInliningPass());
        pipeline.Run(module);

        var js = new JavaScriptCodeGenerator().Generate(module);

        var main = js.Substring(js.IndexOf("function Main()", System.StringComparison.Ordinal));

        // ⚠ WHICH identifier ends up undeclared depends on which OTHER passes run, so these
        // assertions name the MECHANISM rather than one spelling. Run alone, the pass emits
        // `const _inline_t1_0 = ...` — the backend declares a name it has not seen — and leaves the
        // result variable `t1` bare; in the full aggressive pipeline, later passes rewrite that
        // line and `_inline_t1_0` loses its declaration too. The first draft of this test asserted
        // the `_inline_` spelling and FAILED, because that was an artefact of pass ORDERING rather
        // than the defect.
        var assignsUndeclaredResult = main
            .Split('\n')
            .Select(l => l.Trim())
            .Any(l => l.StartsWith("t1 = ", System.StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(js, Does.Contain("_inline_"),
                "the pass still rewrites — if it no longer does, this fixture's premise changed:\n" + js);
            Assert.That(js, Does.Contain("F(6)"),
                "and STILL calls the callee it just inlined (defect 4, no ReplaceUses):\n" + js);
            Assert.That(assignsUndeclaredResult, Is.True,
                "the call's result variable is assigned with no declaration (defect 1):\n" + js);
            Assert.That(main, Does.Contain("Math.imul(p, 2)"),
                "and the CALLEE'S PARAMETER p leaked into Main un-remapped (defect 3):\n" + js);
        });
    }

    /// <summary>
    /// ⚠ Was a private copy of "build the module, <c>AddAggressivePasses</c>, run, generate JS".
    /// It is now <c>JsTestSupport.CompileAggressive</c>, which goes through
    /// <c>AggressivePipeline.Apply</c> — ONE definition of aggressive for every backend, per the
    /// <c>ModuleResolver</c>/<c>ModuleTypeWalker</c> rule in CLAUDE.md. This fixture's copy and
    /// <c>AlgebraicSimplificationTests</c>' identical one were the ONLY aggressive execution
    /// anywhere in the suite, and both were JavaScript-only, which is how this pass and
    /// <c>InductionVariablePass</c> both shipped broken. See <c>AggressivePipeline</c>.
    /// </summary>
    private static string Aggressive(string source) =>
        JsTestSupport.CompileAggressive(source);

    private static string RunAggressive(string source) =>
        JavaScriptExecutionTests.RunNodeScript(Aggressive(source));
}
