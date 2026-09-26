using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.MSIL;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.SemanticAnalysis;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Msil;

// =====================================================================================
//  Task #155 / ADR-0010 — the D9 refusals, D8's "neither" throw, D1's isolation contract, and
//  the verifier sweep. NONE of these need ilasm: MsilHarness.CompileToIl never calls
//  RequireIlasm, so a refusal (or a verifier failure) is caught at Generate() time, in-process.
//  This fixture therefore carries NO [Category("Integration")] and runs in the fast subset —
//  matching "compile-only tests need no ilasm" in the test-writer brief. Every message string
//  asserted below was measured against this working tree (see the doc comment on each test);
//  none is guessed from reading ClosureLowering.cs alone.
// =====================================================================================

[TestFixture]
public class ClosureLoweringRefusalTests
{
    private static ForeignFeatureException CompileAndCaptureRefusal(string source, bool aggressive = false) =>
        Assert.Throws<ForeignFeatureException>(() => MsilHarness.CompileToIl(source, aggressive: aggressive));

    // ---- R1: ByRef capture (D4) ------------------------------------------------------------

    [Test]
    public void R1_ByRefParameterCaptured_Refused()
    {
        var ex = CompileAndCaptureRefusal("""
            Sub Work(ByRef n As Integer)
                Dim f = Sub() n = n + 1
                f()
            End Sub
            Sub Main()
                Dim v As Integer = 1
                Work(v)
                Console.WriteLine(v)
            End Sub
            """);
        Assert.That(ex.Message, Does.Contain("ByRef"));
        Assert.That(ex.Message, Does.Contain("'n'"));
        Assert.That(ex.Message, Does.Contain("D4"));
    }

    // ---- R2/R3: a lambda inside an Iterator or Async creator (D9) -------------------------

    [Test]
    public void R2_LambdaInsideIteratorFunction_Refused()
    {
        var ex = CompileAndCaptureRefusal("""
            Iterator Function Gen() As IEnumerable(Of Integer)
                Dim k As Integer = 1
                Dim f = Function() k + 1
                Yield f()
            End Function
            Sub Main()
                For Each v As Integer In Gen()
                    Console.WriteLine(v)
                Next
            End Sub
            """);
        Assert.That(ex.Message, Does.Contain("Iterator"));
        Assert.That(ex.Message, Does.Contain("'Gen'"));
        Assert.That(ex.Message, Does.Contain("D9"));
    }

    [Test]
    public void R3_LambdaInsideAsyncFunction_Refused()
    {
        var ex = CompileAndCaptureRefusal("""
            Async Function Work() As Task(Of Integer)
                Dim k As Integer = 1
                Dim f = Function() k + 1
                Return f()
            End Function
            Sub Main()
                Console.WriteLine(Work().Result)
            End Sub
            """);
        Assert.That(ex.Message, Does.Contain("Async"));
        Assert.That(ex.Message, Does.Contain("'Work'"));
        Assert.That(ex.Message, Does.Contain("D9"));
    }

    // ---- R5: N9 — a lambda declares a name it also uses from the creator (D3) -------------

    [Test]
    public void R5_LambdaDeclaresNameItAlsoUsesFromCreator_N9_Refused()
    {
        var ex = CompileAndCaptureRefusal("""
            Sub Main()
                Dim n As Integer = 1
                Dim f = Sub()
                            Console.WriteLine(n)
                            Dim n As Integer = 5
                            Console.WriteLine(n)
                        End Sub
                f()
            End Sub
            """);
        Assert.That(ex.Message, Does.Contain("N9"));
        Assert.That(ex.Message, Does.Contain("'n'"));
    }

    // ---- R6: #169 — a name matches a lambda parameter case-insensitively but not exactly (D3) --

    [Test]
    public void R6_NameMatchesLambdaParameterOnlyByCase_Task169_Refused()
    {
        var ex = CompileAndCaptureRefusal("""
            Sub Main()
                Dim X As Integer = 10
                Dim f = Function(x As Integer) X + 1
                Console.WriteLine(f(1))
            End Sub
            """);
        Assert.That(ex.Message, Does.Contain("#169"));
        Assert.That(ex.Message, Does.Contain("'X'"));
        Assert.That(ex.Message, Does.Contain("'x'"));
    }

    // ---- R9/R17: an arity above the measured cap (D7) --------------------------------------

    [Test]
    public void R9_ActionWithNineTypeArguments_AboveTheCap_Refused()
    {
        var ex = CompileAndCaptureRefusal("""
            Sub Main()
                Dim f As Action(Of Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer) = Sub(a As Integer, b As Integer, c As Integer, d As Integer, e As Integer, g As Integer, h As Integer, i As Integer, j As Integer) Console.WriteLine(a + j)
                f(1, 2, 3, 4, 5, 6, 7, 8, 9)
            End Sub
            """);
        Assert.That(ex.Message, Does.Contain("Action"));
        Assert.That(ex.Message, Does.Contain("9"));
    }

    [Test]
    public void R17_FuncWithTenTypeArguments_AboveTheCap_Refused()
    {
        var ex = CompileAndCaptureRefusal("""
            Sub Main()
                Dim g2 As Func(Of Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer) = Function(a As Integer, b As Integer, c As Integer, d As Integer, e As Integer, g As Integer, h As Integer, i As Integer, j As Integer) a * j
                Console.WriteLine(g2(2, 2, 3, 4, 5, 6, 7, 8, 9))
            End Sub
            """);
        Assert.That(ex.Message, Does.Contain("Func"));
        Assert.That(ex.Message, Does.Contain("10"));
    }

    // ---- R10: a captured variable typed by a generic parameter of the creator (D9) --------

    [Test]
    public void R10_CapturedVariableTypedByCreatorsGenericParameter_Refused()
    {
        var ex = CompileAndCaptureRefusal("""
            Function Wrap(Of T)(v As T) As T
                Dim f = Function() v
                Return f()
            End Function
            Sub Main()
                Console.WriteLine(Wrap(Of Integer)(5))
            End Sub
            """);
        Assert.That(ex.Message, Does.Contain("generic parameter"));
        Assert.That(ex.Message, Does.Contain("'T'"));
        Assert.That(ex.Message, Does.Contain("D9"));
    }

    // ---- R11: a capture set that cannot be listed (D3) -------------------------------------

    /// <summary>Raw inline <c>msil{ }</c> code inside the lambda body makes #122's capture
    /// analysis unable to enumerate what the lambda touches (null capture set) — closure
    /// conversion refuses rather than guess.</summary>
    [Test]
    public void R11_CaptureSetCannotBeEnumerated_RawInlineCode_Refused()
    {
        var ex = CompileAndCaptureRefusal("""
            Sub Main()
                Dim n As Integer = 1
                Dim f = Sub()
                            msil{
                                nop
                            }
                            Console.WriteLine(n)
                        End Sub
                f()
            End Sub
            """);
        Assert.That(ex.Message, Does.Contain("could not be enumerated"));
        Assert.That(ex.Message, Does.Contain("D3"));
    }

    // ---- R12: a captured variable passed ByRef (D9) ----------------------------------------

    [Test]
    public void R12_CapturedVariablePassedByRef_Refused()
    {
        var ex = CompileAndCaptureRefusal("""
            Sub Bump(ByRef n As Integer)
                n = n + 1
            End Sub
            Sub Main()
                Dim n As Integer = 1
                Dim f = Sub() Console.WriteLine(n)
                Bump(n)
                f()
            End Sub
            """);
        Assert.That(ex.Message, Does.Contain("'n'"));
        Assert.That(ex.Message, Does.Contain("ByRef"));
        Assert.That(ex.Message, Does.Contain("'Bump'"));
    }

    // ---- R14: MyBase in a lambda (D9) -------------------------------------------------------

    [Test]
    public void R14_MyBaseInsideLambda_Refused()
    {
        var ex = CompileAndCaptureRefusal("""
            Class Base
                Public Overridable Function Tag() As Integer
                    Return 1
                End Function
            End Class
            Class Derived
                Inherits Base
                Public Overrides Function Tag() As Integer
                    Dim f = Function() MyBase.Tag() + 10
                    Return f()
                End Function
            End Class
            Sub Main()
                Dim d As New Derived()
                Console.WriteLine(d.Tag())
            End Sub
            """);
        Assert.That(ex.Message, Does.Contain("MyBase.Tag"));
    }

    // ---- R15: a When guard reading a captured variable (D9) --------------------------------

    [Test]
    public void R15_WhenGuardReadsCapturedVariable_Refused()
    {
        var ex = CompileAndCaptureRefusal("""
            Sub Main()
                Dim lim As Integer = 5
                Dim f = Sub() lim = lim + 1
                f()
                Dim v As Integer = 7
                Select Case v
                    Case Is > 0 When v > lim + 1
                        Console.WriteLine("big")
                    Case Else
                        Console.WriteLine("small")
                End Select
            End Sub
            """);
        Assert.That(ex.Message, Does.Contain("When"));
        Assert.That(ex.Message, Does.Contain("guard"));
    }

    // ---- (c) D8: a call to a name that is neither a declared procedure nor a delegate value --

    /// <summary>The RUNNING half of this rule (a delegate variable shadowing a module procedure
    /// invokes the delegate) is <c>ClosureLoweringContractTests.D8_DelegateVariable_
    /// ShadowsModuleProcedure_InvokesDelegate</c>, which needs ilasm+dotnet; this half needs
    /// neither. Nothing in this program declares 'Ghost' — measured: the front end does not
    /// itself reject an undefined call, so this exercises MSILBackend's own D8 guard.</summary>
    [Test]
    public void D8_CallToUndeclaredName_NeitherProcedureNorDelegate_Refused()
    {
        var ex = CompileAndCaptureRefusal("""
            Sub Main()
                Ghost()
            End Sub
            """);
        Assert.That(ex.Message, Does.Contain("'Ghost'"));
        Assert.That(ex.Message, Does.Contain("neither a procedure"));
        Assert.That(ex.Message, Does.Contain("D8"));
    }

    // ---- Every refusal fires identically under the aggressive pipeline too ----------------

    /// <summary>One representative refusal (R1), re-asserted with <c>aggressive: true</c> — the
    /// refusal fires inside <c>ClosureLowering</c>/<c>MSILBackend</c> before any optimizer
    /// choice could matter, so this is a smoke check that the aggressive pipeline does not
    /// somehow dodge it (e.g. by folding the capture away), not a full re-run of all twelve.</summary>
    [Test]
    public void ARefusal_FiresIdenticallyUnderTheAggressivePipeline()
    {
        var ex = Assert.Throws<ForeignFeatureException>(() => MsilHarness.CompileToIl("""
            Sub Work(ByRef n As Integer)
                Dim f = Sub() n = n + 1
                f()
            End Sub
            Sub Main()
                Dim v As Integer = 1
                Work(v)
                Console.WriteLine(v)
            End Sub
            """, aggressive: true));
        Assert.That(ex.Message, Does.Contain("ByRef"));
        Assert.That(ex.Message, Does.Contain("D4"));
    }
}

// =====================================================================================
//  (e) D1 isolation — ClosureLowering.Run's own contract, independent of MSIL emission.
// =====================================================================================

[TestFixture]
public class ClosureLoweringIsolationTests
{
    private static IRModule BuildOptimizedModule(string source, string moduleName = "Probe")
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            "semantic errors:\n" + string.Join("\n", analyzer.Errors.Select(e => e.ToString())));

        var module = new IRBuilder(analyzer).Build(ast, moduleName);
        var pipeline = new OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.Run(module);
        return module;
    }

    /// <summary>D1's contract, first half: no lambda, no AddressOf, no delegate-value call —
    /// <c>Run</c> must hand back the SAME instance, so a program without delegates reaches the
    /// backend byte-for-byte as before.</summary>
    [Test]
    public void Run_NoLambdaAddressOfOrDelegateCall_ReturnsTheSameModuleInstance()
    {
        var module = BuildOptimizedModule("""
            Sub Main()
                Console.WriteLine("x")
            End Sub
            """);

        var result = ClosureLowering.Run(module);

        Assert.That(result, Is.SameAs(module));
    }

    /// <summary>D1's contract, second half: for a program that DOES have a lambda, <c>Run</c>
    /// must hand back a DIFFERENT instance and must leave the INPUT's own printed IR
    /// byte-identical — the clone, not a mutation-in-place.</summary>
    [Test]
    public void Run_WithLambda_ReturnsADifferentInstance_AndLeavesTheInputsPrintedIrByteIdentical()
    {
        var module = BuildOptimizedModule(ClosureLoweringProbes.L4);
        var printedBefore = new IRPrettyPrinter().Print(module);

        var result = ClosureLowering.Run(module);

        Assert.That(result, Is.Not.SameAs(module),
            "a program with a lambda must get back a LOWERED CLONE, not the same instance.");

        var printedAfter = new IRPrettyPrinter().Print(module);
        Assert.That(printedAfter, Is.EqualTo(printedBefore),
            "ClosureLowering.Run must not mutate the module it was handed — the input's own IR " +
            "must print identically before and after.");
    }

    /// <summary>D1's other stated way to prove invisibility: one module, MSIL generated first
    /// (which runs ClosureLowering internally), THEN C# generated from the SAME module
    /// reference — must give byte-identical C# to generating C# from a fresh, untouched module.
    /// This is the test that would catch mutant (f): the lowering applied to C# as well, or the
    /// clone skipped so the input module is mutated.</summary>
    [Test]
    public void GeneratingMsilThenCSharp_FromOneModule_GivesTheSameCSharpAsCSharpAlone()
    {
        var sharedModule = BuildOptimizedModule(ClosureLoweringProbes.L4);
        _ = new MSILCodeGenerator().Generate(sharedModule);
        var csharpAfterMsil = new ImprovedCSharpCodeGenerator().Generate(sharedModule);

        var freshModule = BuildOptimizedModule(ClosureLoweringProbes.L4);
        var csharpAlone = new ImprovedCSharpCodeGenerator().Generate(freshModule);

        Assert.That(csharpAfterMsil, Is.EqualTo(csharpAlone));
    }
}

// =====================================================================================
//  (f) The verifier on lowered IR — with verification forced on, lowering every contract
//  program must raise no verifier failure, on both pipelines.
// =====================================================================================

[TestFixture]
public class ClosureLoweringVerifierTests
{
    [Test]
    public void LoweringEveryContractProgram_RaisesNoVerifierFailure()
    {
        var previousMode = IRVerifier.Mode;
        IRVerifier.Mode = IRVerifierMode.Throw;
        try
        {
            foreach (var (name, source) in ClosureLoweringProbes.AllContractSources())
            {
                Assert.DoesNotThrow(() => MsilHarness.CompileToIl(source, moduleName: name, aggressive: false),
                    $"{name}, standard pipeline: lowering must not fail IRVerifier.");
                Assert.DoesNotThrow(() => MsilHarness.CompileToIl(source, moduleName: name, aggressive: true),
                    $"{name}, aggressive pipeline: lowering must not fail IRVerifier.");
            }
        }
        finally
        {
            IRVerifier.Mode = previousMode;
        }
    }
}
