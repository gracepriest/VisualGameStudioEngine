using System;
using System.Collections.Immutable;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.SemanticAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// What happens when a <c>Return</c> hands back a value of a different numeric type than the
/// function declared.
///
/// <para>⛔ VB's <c>/</c> is ALWAYS floating-point division, so <c>Return v / 2</c> from a
/// <c>Function … As Integer</c> returns a Double where an Integer was promised. Nothing inserted
/// the conversion, and each backend then did something different — measured, not assumed:</para>
///
/// <list type="bullet">
/// <item><b>C#</b> emitted <c>return (double)(v) / (double)(2);</c> from an <c>int</c> method:
/// <b>CS0266, does not compile</b>. The BasicLang build still reported success, because it only
/// writes the source — nothing invokes the C# compiler.</item>
/// <item><b>MSIL</b> emitted <c>ret</c> with a float64 on the stack from an int32 method and
/// returned <b>0</b>. Silent wrong answer.</item>
/// <item><b>JavaScript</b> returned <b>3.5</b> for <c>Half(7)</c> — it has no types to disagree
/// about, so nothing narrowed. It looked correct only for inputs whose quotient was already
/// whole.</item>
/// <item><b>C++</b> was the one that happened to be right, and not because the compiler did
/// anything: C++ narrows implicitly on return.</item>
/// </list>
///
/// <para><b>Why this fixture and not the parity one.</b>
/// <c>BclBackendParityTests.Backends_PrintIdenticalOutput</c> is the repo's cross-backend oracle,
/// but its C# leg goes through <c>BasicLang.exe build</c>, which is not deployed next to the tests
/// on every machine — it is a baseline failure on this container. So the C# half is checked here
/// by compiling the emitted source with Roslyn IN-PROCESS, which is the assertion that actually
/// matters for this defect: the old output was not merely wrong, it did not build.</para>
/// </summary>
[TestFixture]
public class ReturnCoercionTests
{
    /// <summary>
    /// The shape the whole fixture is about: floating division returned from an Integer function.
    /// </summary>
    private const string HalfProgram = """
        Module M
         Function Half(v As Integer) As Integer
          Return v / 2
         End Function
         Sub Main()
          PrintLine(CStr(Half(84)) & "," & CStr(Half(7)))
         End Sub
        End Module
        """;

    // ====================================================================================
    // C# — the half that did not compile.
    // ====================================================================================

    /// <summary>
    /// ⛔ The headline defect. <c>return (double)(v) / (double)(2);</c> from an <c>int</c> method
    /// is CS0266, and no test in the repo noticed because nothing compiled the emitted source.
    /// Roslyn in-process is the cheapest thing that would have caught it.
    /// </summary>
    [Test]
    public void TheEmittedCSharp_Compiles_WhereItUsedToBeCs0266()
    {
        var errors = CompileEmittedCSharp(HalfProgram);

        Assert.That(errors, Is.Empty,
            "the emitted C# must compile; CS0266 here means the return conversion is missing:\n"
            + string.Join("\n", errors));
    }

    /// <summary>
    /// A Sub, and a Function whose return already matches, must be left alone — the coercion is
    /// inserted only where the types actually disagree, so this pins that it is not blanket.
    /// </summary>
    [Test]
    public void AMatchingReturn_IsNotGivenARedundantCast()
    {
        var csharp = EmitCSharp("""
            Module M
             Function Twice(v As Integer) As Integer
              Return v * 2
             End Function
             Sub Main()
              PrintLine(CStr(Twice(21)))
             End Sub
            End Module
            """);

        Assert.That(csharp, Does.Not.Contain("(int)("),
            "Integer * Integer is already Integer; inserting a cast would be noise:\n" + csharp);
    }

    /// <summary>
    /// ⚠ The guard that keeps this away from everything that is not a number. A
    /// <c>Function … As Object</c> returning an Integer must NOT acquire a numeric cast — boxing
    /// is someone else's job, and <c>DetermineCastKind</c> would answer <c>Bitcast</c> for it.
    ///
    /// <para>⛔ This RUNS the program rather than only compiling the C#, and the first version of
    /// it did only the latter — which proved nothing. Measured with the guard removed: C# still
    /// compiles, because the stray cast renders harmlessly there. <b>JavaScript fails to build</b>
    /// ("IRCast lowering is not implemented" — a Bitcast is not a numeric cast), which is what
    /// makes this test discriminating at all.</para>
    ///
    /// <para>⚠ MSIL is deliberately NOT asserted here. <c>Function … As Object</c> already fails
    /// on that backend with or without this change (NullReferenceException), as does a
    /// class-typed return (<c>.method … Base Make()</c> — the <c>class</c> prefix is missing and
    /// ilasm rejects it). Both are pre-existing MSIL gaps unrelated to return coercion; asserting
    /// them here would pin someone else's defect to this fixture.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void ANonNumericReturnType_IsLeftAlone()
    {
        const string program = """
            Module M
             Function Boxed() As Object
              Return 7
             End Function
             Function Label() As String
              Return "hi"
             End Function
             Sub Main()
              PrintLine(CStr(Boxed()) & "," & Label())
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(CompileEmittedCSharp(program), Is.Empty);
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("7,hi"),
                "a Bitcast has no numeric lowering, so a stray cast here fails the JS build");
        });
    }

    // ====================================================================================
    // JavaScript — the half that silently did not narrow.
    // ====================================================================================

    /// <summary>
    /// ⛔ JS returned <c>3.5</c> from a function declared <c>As Integer</c>. Every JS number is an
    /// IEEE double, so nothing removes the fraction unless the cast does — and the JS backend
    /// deliberately threw on narrowing casts ("it needs Math.trunc") rather than pass one through,
    /// which is why inserting the coercion in the IR required implementing that lowering.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void JavaScript_NarrowsTheReturn_InsteadOfKeepingTheFraction()
    {
        Assert.That(JavaScriptExecutionTests.RunJs(HalfProgram), Is.EqualTo("42,4"),
            "42 was always right; the 3 is the case JS used to print as 3.5");
    }

    // ====================================================================================
    // MSIL — the half that returned garbage.
    // ====================================================================================

    /// <summary>
    /// ⛔ <c>ret</c> with a float64 on the stack from an int32 method. The CLR does not reject it
    /// and the JIT runs it; the method returned 0.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void Msil_ConvertsBeforeRet_InsteadOfReturningZero()
    {
        Assert.That(Msil.MsilHarness.RunExpectingSuccess(HalfProgram), Is.EqualTo("42,4\n"));
    }

    // ====================================================================================
    // The three that CAN run here agree — which is the property worth holding.
    // ====================================================================================

    /// <summary>
    /// ⚠ They agree on VB's answer: banker's rounding, so <c>Return 7 / 2</c> is <b>4</b>.
    ///
    /// <para>⛔ This test used to pin the opposite. It read "they agree on TRUNCATION, and that is
    /// not VB.NET's answer ... changing it means changing every <c>IRCast</c> rendering on four
    /// backends, which is a decision about the whole narrowing surface", and asked to go red "the
    /// day someone takes that decision ... rather than drifting". It did exactly that, and the
    /// decision was taken: every narrowing now rounds half-to-even, on all four backends and in
    /// the constant fold, so an implicit return and an explicit <c>CInt</c> can no longer
    /// disagree.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void TheBackendsAgreeOnVbsBankersRounding()
    {
        var js = JavaScriptExecutionTests.RunJs(HalfProgram);
        var msil = Msil.MsilHarness.RunExpectingSuccess(HalfProgram).TrimEnd('\n');

        Assert.That(msil, Is.EqualTo(js),
            $"the two executable backends must not disagree: MSIL '{msil}' vs JS '{js}'");
        Assert.That(js, Is.EqualTo("42,4"),
            "and both round half-to-even: 7 / 2 is 3.5, which VB narrows to 4");
    }

    // ====================================================================================
    // Helpers.
    // ====================================================================================

    /// <summary>BasicLang → C# source, through the optimizer, as the CLI does.</summary>
    private static string EmitCSharp(string source, bool aggressive = false)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            "semantic errors:\n" + string.Join("\n", analyzer.Errors.Select(e => e.ToString())));

        var module = new IRBuilder(analyzer).Build(ast, "ReturnCoercionProbe");
        if (aggressive)
        {
            AggressivePipeline.Apply(module);
        }
        else
        {
            var pipeline = new OptimizationPipeline();
            pipeline.AddStandardPasses();
            pipeline.Run(module);
        }

        return new ImprovedCSharpCodeGenerator().Generate(module);
    }

    /// <summary>
    /// The Roslyn check, shared with <see cref="AssignmentCoercionTests"/> — the store half of
    /// this same coercion needs exactly the same assertion, and two copies would drift about
    /// which diagnostics count.
    /// </summary>
    internal static string[] CompileEmittedCSharpForTest(string source) => CompileEmittedCSharp(source);

    /// <summary>The emitted C# itself, for the few properties that are invisible at run time.</summary>
    internal static string EmitCSharpForTest(string source) => EmitCSharp(source);

    /// <summary>
    /// The emitted C#, AGGRESSIVE — the C# leg of
    /// <c>FourBackends.RunsOnEveryBackendAggressive</c>, through
    /// <see cref="AggressivePipeline"/>. <see cref="EmitCSharpForTest"/> runs
    /// <c>AddStandardPasses</c> only and is blind to every aggressive-only pass; C# is this
    /// suite's reference oracle, so having no aggressive C# route at all is what let
    /// <c>FunctionInliningPass</c> and <c>InductionVariablePass</c> both ship broken.
    /// </summary>
    internal static string EmitCSharpAggressiveForTest(string source) => EmitCSharp(source, aggressive: true);

    /// <summary>
    /// The emitted C#, run past Roslyn. Returns the ERROR diagnostics only — warnings are not this
    /// fixture's business, and the defect it exists for is an error.
    /// </summary>
    private static string[] CompileEmittedCSharp(string source)
    {
        var csharp = EmitCSharp(source);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToImmutableArray();

        var compilation = CSharpCompilation.Create(
            "ReturnCoercionProbe",
            new[] { CSharpSyntaxTree.ParseText(csharp) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToArray();
    }
}
