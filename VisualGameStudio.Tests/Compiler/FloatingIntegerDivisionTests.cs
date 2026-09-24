using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.JavaScript;
using BasicLang.Compiler.CodeGen.MSIL;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;
using VisualGameStudio.Tests.Msil;
using VisualGameStudio.Tests.Native;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  ADR-0005 D1 — `\` (integer division) with a FLOATING operand. VB.NET semantics: each floating
//  operand is converted to Long, rounding half-to-even (the same node `CLng(x)` lowers to — an
//  IRCast(FPToSI), never an IRCall), and the integral divide then truncates toward zero.
//
//  This file is the implementer-facing test/fixtures pass for D1. It does NOT touch production
//  code except where explicitly marked MUTATION (applied and reverted, never committed). See
//  docs/superpowers/decisions/0005-integer-division-cse-destination-interface-property-type.md,
//  D1, and its "Implementation note (D1)" section.
// ================================================================================================

/// <summary>
/// RUNNING fixtures: contract values on all four backends (both pipelines), the CLI/Release
/// entry points, divide-by-zero, the CLng/narrowing prerequisites, and the When-guard shapes
/// (SC6/SC7). Everything here spawns a process (Node, g++/clang++, ilasm+dotnet, or the CLI
/// itself), so it is Category("Integration") like every other run-the-backend fixture in this
/// suite, and its JS legs are why it is in <see cref="JsExecutionTierRosterTests"/>'s roster.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class FloatingIntegerDivisionExecutionTests
{
    // ============================================================================================
    // 1. CONTRACT VALUES — both pipelines, three operand shapes the optimizer cannot fold away
    //    (function-returned, loop-assigned) plus literals. Mirrors the corpus's R1/R2/L1.
    // ============================================================================================

    // R1 — every operand crosses a Function-call boundary, so the optimizer cannot constant-fold
    // the division away. Covers the five core contract values, a Single operand, both operands
    // floating, and the `(a / b) \ c` chain the analyzer's own note says must stay legal.
    private const string R1_FunctionCallOperands = """
        Function D(v As Double) As Double
            Return v
        End Function

        Function S(v As Single) As Single
            Return v
        End Function

        Function I(v As Integer) As Integer
            Return v
        End Function

        Sub Main()
            Console.WriteLine(D(7.5) \ I(2))
            Console.WriteLine(I(7) \ D(2.5))
            Console.WriteLine(D(8.5) \ I(2))
            Console.WriteLine(D(-7.5) \ I(2))
            Console.WriteLine(I(-7) \ I(2))
            Console.WriteLine(S(CSng(7.5)) \ I(2))
            Console.WriteLine(D(7.5) \ D(2.5))
            Console.WriteLine((I(15) / I(2)) \ I(2))
        End Sub
        """;

    private const string R1_Expected = "4\n3\n4\n-4\n-3\n4\n4\n4";

    // L1 — the same five core values as bare literals: the CONSTANT path, which the optimizer's
    // ConstantFolding pass does NOT convert (ADR note (c)) so this exercises the emitted division
    // rather than a folded literal.
    private const string L1_Literals = """
        Sub Main()
            Console.WriteLine(7.5 \ 2)
            Console.WriteLine(7 \ 2.5)
            Console.WriteLine(8.5 \ 2)
            Console.WriteLine(-7.5 \ 2)
            Console.WriteLine(-7 \ 2)
        End Sub
        """;

    private const string L1_Expected = "4\n3\n4\n-4\n-3";

    // R2 — operands assigned inside a loop: the optimizer cannot constant-fold a value that is
    // reassigned across iterations, a different unfoldability shape than a Function call.
    private const string R2_LoopAssignedOperands = """
        Sub Main()
            Dim s As Double = 0
            Dim two As Integer = 0
            For k As Integer = 1 To 3
                s = s + 2.5
            Next
            For k As Integer = 1 To 2
                two = two + 1
            Next
            Dim seven As Integer = two * 3 + 1
            Console.WriteLine(s \ two)
            Console.WriteLine(seven \ (s - 5))
            Console.WriteLine((s + 1) \ two)
            Console.WriteLine((0 - s) \ two)
            Console.WriteLine((0 - seven) \ two)
        End Sub
        """;

    private const string R2_Expected = "4\n3\n4\n-4\n-3";

    [TestCase(nameof(R1_FunctionCallOperands), nameof(R1_Expected), TestName = "R1_FunctionCallOperands_StandardPipeline")]
    [TestCase(nameof(L1_Literals), nameof(L1_Expected), TestName = "L1_Literals_StandardPipeline")]
    [TestCase(nameof(R2_LoopAssignedOperands), nameof(R2_Expected), TestName = "R2_LoopAssignedOperands_StandardPipeline")]
    public void ContractValues_StandardPipeline(string sourceField, string expectedField)
        => FourBackends.RunsOnEveryBackend(Const(sourceField), Const(expectedField));

    [TestCase(nameof(R1_FunctionCallOperands), nameof(R1_Expected), TestName = "R1_FunctionCallOperands_AggressivePipeline")]
    [TestCase(nameof(L1_Literals), nameof(L1_Expected), TestName = "L1_Literals_AggressivePipeline")]
    [TestCase(nameof(R2_LoopAssignedOperands), nameof(R2_Expected), TestName = "R2_LoopAssignedOperands_AggressivePipeline")]
    public void ContractValues_AggressivePipeline(string sourceField, string expectedField)
        => FourBackends.RunsOnEveryBackendAggressive(Const(sourceField), Const(expectedField));

    private static string Const(string fieldName) =>
        (string)typeof(FloatingIntegerDivisionExecutionTests)
            .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static)!
            .GetRawConstantValue()!;

    // ============================================================================================
    // 2. THE CLI AND RELEASE .blproj ENTRY POINTS — C# and MSIL.
    //
    //    BasicCompiler.CompileFile is the CLI single-file entry point; CompileProjectFiles (with
    //    OptimizeAggressive = true) is what a Release .blproj build maps `-c Release` to
    //    (Program.cs:502). Both entry points always run the STANDARD pipeline and add the
    //    aggressive passes on top — never a different pipeline from the in-process helpers above,
    //    which is exactly the risk this section exists to catch (a fix that only works through
    //    the unit-test helper). Rendered through C# (in-process Roslyn) AND MSIL (ilasm+dotnet)
    //    — per CseDestinationInvalidationExecutionTests's established convention for these two
    //    entry points, widened here to cover MSIL too because task #2 asks for "at least C# and
    //    MSIL", not C# alone.
    // ============================================================================================

    private static string RunThroughEntryPoint_CSharp(string source, bool asProject)
    {
        var dir = Path.Combine(Path.GetTempPath(), "BasicLang_D1Entry_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "prog.bas");
            File.WriteAllText(file, source);

            var compiler = new BasicCompiler(new CompilerOptions { OptimizeAggressive = true });
            var result = asProject ? compiler.CompileProjectFiles(new[] { file }) : compiler.CompileFile(file);

            Assert.That(result.HasErrors, Is.False,
                string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            Assert.That(result.CombinedIR, Is.Not.Null, "the entry point produced no combined IR");

            return FourBackends.Norm(FourBackends.RunEmittedCSharpText(
                new ImprovedCSharpCodeGenerator().Generate(result.CombinedIR)));
        }
        finally { try { Directory.Delete(dir, true); } catch { /* temp */ } }
    }

    private static string RunThroughEntryPoint_Msil(string source, bool asProject)
    {
        var dir = Path.Combine(Path.GetTempPath(), "BasicLang_D1EntryMsil_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "prog.bas");
            File.WriteAllText(file, source);

            var compiler = new BasicCompiler(new CompilerOptions { OptimizeAggressive = true });
            var result = asProject ? compiler.CompileProjectFiles(new[] { file }) : compiler.CompileFile(file);

            Assert.That(result.HasErrors, Is.False,
                string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            Assert.That(result.CombinedIR, Is.Not.Null, "the entry point produced no combined IR");

            var il = new MSILCodeGenerator().Generate(result.CombinedIR);
            return FourBackends.Norm(MsilHarness.RunIlExpectingSuccess(il, "D1Entry"));
        }
        finally { try { Directory.Delete(dir, true); } catch { /* temp */ } }
    }

    [Test]
    public void TheCliSingleFileEntryPoint_CSharp()
        => Assert.That(RunThroughEntryPoint_CSharp(L1_Literals, asProject: false), Is.EqualTo(L1_Expected.TrimEnd('\n')),
            "BasicCompiler.CompileFile printed the wrong answer through the C# backend.");

    [Test]
    public void TheCliSingleFileEntryPoint_Msil()
        => Assert.That(RunThroughEntryPoint_Msil(L1_Literals, asProject: false), Is.EqualTo(L1_Expected.TrimEnd('\n')),
            "BasicCompiler.CompileFile printed the wrong answer through the MSIL backend.");

    [Test]
    public void TheProjectBuildEntryPoint_CSharp()
        => Assert.That(RunThroughEntryPoint_CSharp(L1_Literals, asProject: true), Is.EqualTo(L1_Expected.TrimEnd('\n')),
            "BasicCompiler.CompileProjectFiles printed the wrong answer through the C# backend.");

    [Test]
    public void TheProjectBuildEntryPoint_Msil()
        => Assert.That(RunThroughEntryPoint_Msil(L1_Literals, asProject: true), Is.EqualTo(L1_Expected.TrimEnd('\n')),
            "BasicCompiler.CompileProjectFiles printed the wrong answer through the MSIL backend.");

    /// <summary>
    /// The REAL CLI binary, spawned, building a genuine Release .blproj — not the in-process
    /// simulation above. Mirrors <c>MsilBinaryOperandCoercionTests.BuildReleaseMsilAndRun</c>
    /// exactly (confirmed working on this machine: that fixture's own Release/MSIL case already
    /// passes here). ⚠ C++ is deliberately NOT exercised this way: task #134 records that a C++
    /// Release .blproj runs the STANDARD pipeline, not aggressive, so it would not be an
    /// aggressive leg here even though `-c Release` is used — out of this test's scope, which is
    /// "at least C# and MSIL".
    /// </summary>
    [Test]
    public void ReleaseBlprojBuild_Msil_RealCli_ContractValuesMatch()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-d1-release-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Main.bas"), L1_Literals);
            File.WriteAllText(Path.Combine(dir, "App.blproj"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <BasicLangProject Version="1.0">
                  <PropertyGroup>
                    <ProjectName>App</ProjectName>
                    <OutputType>Exe</OutputType>
                    <TargetBackend>MSIL</TargetBackend>
                  </PropertyGroup>
                  <ItemGroup>
                    <Compile Include="Main.bas" />
                  </ItemGroup>
                </BasicLangProject>
                """);

            var (buildExit, buildOut, buildErr) = CliTestHarness.RunProcess(
                CliTestHarness.CliPath(),
                new[] { "build", Path.Combine(dir, "App.blproj"), "-c", "Release" },
                dir,
                timeoutMs: 120_000);
            Assert.That(buildExit, Is.EqualTo(0),
                $"CLI Release MSIL build failed.\nSTDOUT:\n{buildOut}\nSTDERR:\n{buildErr}");

            var ilFiles = Directory.GetFiles(dir, "App.il", SearchOption.AllDirectories);
            Assert.That(ilFiles, Is.Not.Empty,
                $"CLI build claimed success but produced no App.il.\nSTDOUT:\n{buildOut}");

            Assert.That(FourBackends.Norm(MsilHarness.RunIlExpectingSuccess(File.ReadAllText(ilFiles[0]), "App")),
                Is.EqualTo(L1_Expected.TrimEnd('\n')));
        }
        finally { try { Directory.Delete(dir, true); } catch { /* temp */ } }
    }

    // ============================================================================================
    // 3. DIVIDE-BY-ZERO — `7 \ 0.4` must behave EXACTLY like integral `7 \ 0` on each backend.
    //    Pinned per the corpus's Z0 (integral)/Z1 (function-call float)/Z2 (literal float) matrix,
    //    which measured all three IDENTICAL on every backend:
    //      C#/MSIL: throws System.DivideByZeroException at run time (both map `\` to a plain
    //               integral divide once the floating operand is converted to Long).
    //      C++:     prints "before" then a SIGFPE crash (native `int64_t` division by zero is a
    //               hardware trap, not a C++ exception) — exit code 136 (128 + SIGFPE) on Linux.
    //      JS:      does NOT crash. `\` lowers to Math.trunc(l / r) and JS division by zero is
    //               floating Infinity, so it prints "before", "Infinity", "after" and keeps going.
    // ============================================================================================

    private const string Z0_IntegralDivByZero = """
        Function I(v As Integer) As Integer
            Return v
        End Function

        Sub Main()
            Console.WriteLine("before")
            Console.WriteLine(I(7) \ I(0))
            Console.WriteLine("after")
        End Sub
        """;

    private const string Z1_FunctionCallFloatDivByZero = """
        Function D(v As Double) As Double
            Return v
        End Function

        Function I(v As Integer) As Integer
            Return v
        End Function

        Sub Main()
            Console.WriteLine("before")
            Console.WriteLine(I(7) \ D(0.4))
            Console.WriteLine("after")
        End Sub
        """;

    private const string Z2_LiteralFloatDivByZero = """
        Sub Main()
            Console.WriteLine("before")
            Console.WriteLine(7 \ 0.4)
            Console.WriteLine("after")
        End Sub
        """;

    [TestCase(nameof(Z0_IntegralDivByZero), TestName = "Z0_Integral_DivByZero")]
    [TestCase(nameof(Z1_FunctionCallFloatDivByZero), TestName = "Z1_FunctionCallFloat_DivByZero_BehavesLikeZ0")]
    [TestCase(nameof(Z2_LiteralFloatDivByZero), TestName = "Z2_LiteralFloat_DivByZero_BehavesLikeZ0")]
    public void DivideByZero_CSharp_ThrowsDivideByZeroException(string sourceField)
    {
        var source = Const(sourceField);
        var (threw, exceptionType, output) = RunEmittedCSharpAllowingThrow(
            ReturnCoercionTests.EmitCSharpForTest(source));

        Assert.That(threw, Is.True, "expected the emitted C# to throw on divide by zero");
        Assert.That(exceptionType, Is.EqualTo(typeof(DivideByZeroException).FullName));
        Assert.That(output, Does.StartWith("before"));
    }

    [TestCase(nameof(Z0_IntegralDivByZero), TestName = "Z0_Integral_DivByZero_Msil")]
    [TestCase(nameof(Z1_FunctionCallFloatDivByZero), TestName = "Z1_FunctionCallFloat_DivByZero_BehavesLikeZ0_Msil")]
    [TestCase(nameof(Z2_LiteralFloatDivByZero), TestName = "Z2_LiteralFloat_DivByZero_BehavesLikeZ0_Msil")]
    public void DivideByZero_Msil_ThrowsDivideByZeroException(string sourceField)
    {
        var r = MsilHarness.Run(Const(sourceField));

        Assert.That(r.Outcome, Is.EqualTo(MsilHarness.MsilOutcome.RunFailed), r.Report);
        Assert.That(r.Output, Does.Contain("DivideByZeroException"));
        Assert.That(r.Output, Does.StartWith("before"));
    }

    [TestCase(nameof(Z0_IntegralDivByZero), TestName = "Z0_Integral_DivByZero_Cpp")]
    [TestCase(nameof(Z1_FunctionCallFloatDivByZero), TestName = "Z1_FunctionCallFloat_DivByZero_BehavesLikeZ0_Cpp")]
    [TestCase(nameof(Z2_LiteralFloatDivByZero), TestName = "Z2_LiteralFloat_DivByZero_BehavesLikeZ0_Cpp")]
    public void DivideByZero_Cpp_CrashesWithSigfpe(string sourceField)
    {
        var compiler = CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");

        var cpp = BclE2E.CompileToCppOptimized(Const(sourceField));
        var outcome = CppCompile.CompileAndRunFilesForOutcome(
            new Dictionary<string, string> { ["prog.cpp"] = cpp },
            new[] { "prog.cpp" },
            compiler.Value);

        Assert.That(outcome.ExitCode, Is.Not.Zero,
            "a native int64_t division by zero must not exit cleanly");
        Assert.That(outcome.StdOut.Replace("\r\n", "\n"), Is.EqualTo("before\n"),
            "the program must crash immediately after printing \"before\" — before \"after\"");
    }

    [TestCase(nameof(Z0_IntegralDivByZero), TestName = "Z0_Integral_DivByZero_Js")]
    [TestCase(nameof(Z1_FunctionCallFloatDivByZero), TestName = "Z1_FunctionCallFloat_DivByZero_BehavesLikeZ0_Js")]
    [TestCase(nameof(Z2_LiteralFloatDivByZero), TestName = "Z2_LiteralFloat_DivByZero_BehavesLikeZ0_Js")]
    public void DivideByZero_Js_DoesNotCrash_PrintsInfinity(string sourceField)
        => Assert.That(JavaScriptExecutionTests.RunJs(Const(sourceField)),
            Is.EqualTo("before\nInfinity\nafter"));

    /// <summary>
    /// Compiles the given C# straight to an in-memory assembly and runs it, exactly like
    /// <see cref="FourBackends.RunEmittedCSharpText"/> — except a thrown exception is CAUGHT and
    /// reported, not failed, because for divide-by-zero the throw itself is the pinned behaviour.
    /// </summary>
    private static (bool Threw, string ExceptionType, string Output) RunEmittedCSharpAllowingThrow(string csharp)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToImmutableArray();

        var compilation = CSharpCompilation.Create(
            "D1DivideByZeroProbe_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(csharp) },
            references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));

        using var ms = new MemoryStream();
        var emitted = compilation.Emit(ms);
        Assert.That(emitted.Success, Is.True,
            "the emitted C# does not compile:\n" + string.Join("\n",
                emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()))
            + "\n--- emitted ---\n" + csharp);

        var assembly = Assembly.Load(ms.ToArray());
        var entry = assembly.EntryPoint;
        Assert.That(entry, Is.Not.Null, "no entry point in the emitted program");

        var captured = new StringWriter();
        var original = Console.Out;
        Console.SetOut(captured);
        try
        {
            var args = entry!.GetParameters().Length == 0 ? null : new object[] { Array.Empty<string>() };
            entry.Invoke(null, args);
            return (false, null, captured.ToString().Replace("\r\n", "\n").Trim());
        }
        catch (TargetInvocationException ex)
        {
            var inner = ex.InnerException ?? ex;
            return (true, inner.GetType().FullName, captured.ToString().Replace("\r\n", "\n").Trim());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    // ============================================================================================
    // 4. PREREQUISITES — CLng half-to-even and implicit Double->Long narrowing on C#, C++, MSIL
    //    (runtime AND literal); JavaScript's BL7003 refusal of Long is pinned separately in
    //    FloatingIntegerDivisionStructuralTests (a pure compile-time capability check, no process
    //    spawn needed).
    // ============================================================================================

    [TestCase("7.5", "8", TestName = "CLng_SevenPointFive_Runtime")]
    [TestCase("8.5", "8", TestName = "CLng_EightPointFive_Runtime")]
    [TestCase("-7.5", "-8", TestName = "CLng_MinusSevenPointFive_Runtime")]
    public void CLng_RoundsHalfToEven_RuntimeOperand_CSharpCppMsil(string literal, string expected)
    {
        var program = $"""
            Function D(v As Double) As Double
                Return v
            End Function

            Sub Main()
                Console.WriteLine(CLng(D({literal})))
            End Sub
            """;

        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo(expected), "C#");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program))), Is.EqualTo(expected), "C++");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(program)), Is.EqualTo(expected), "MSIL");
        });
    }

    [TestCase("7.5", "8", TestName = "CLng_SevenPointFive_Literal")]
    [TestCase("8.5", "8", TestName = "CLng_EightPointFive_Literal")]
    [TestCase("-7.5", "-8", TestName = "CLng_MinusSevenPointFive_Literal")]
    public void CLng_RoundsHalfToEven_Literal_CSharpCppMsil(string literal, string expected)
    {
        var program = $"Sub Main()\n    Console.WriteLine(CLng({literal}))\nEnd Sub";

        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo(expected), "C#");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program))), Is.EqualTo(expected), "C++");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(program)), Is.EqualTo(expected), "MSIL");
        });
    }

    /// <summary>
    /// Implicit narrowing (P2 in the corpus): a Double assigned straight into a `Long` local,
    /// no CLng call, rounds the same way. Runtime operand (function call) AND a literal.
    /// </summary>
    [Test]
    public void ImplicitDoubleToLongNarrowing_RoundsHalfToEven_RuntimeAndLiteral()
    {
        var program = """
            Function D(v As Double) As Double
                Return v
            End Function

            Sub Main()
                Dim a As Long = D(7.5)
                Dim b As Long = D(8.5)
                Dim c As Long = D(-7.5)
                Dim e As Long = 7.5
                Console.WriteLine(a)
                Console.WriteLine(b)
                Console.WriteLine(c)
                Console.WriteLine(e)
            End Sub
            """;
        const string expected = "8\n8\n-8\n8\n";

        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo(expected.TrimEnd('\n')), "C#");
            Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)), Is.EqualTo(expected), "C++");
            Assert.That(MsilHarness.RunExpectingSuccess(program), Is.EqualTo(expected), "MSIL");
        });
    }

    // ============================================================================================
    // 6. WHEN GUARDS — SC6/SC7 from the corpus, the step-3a prerequisite: a floating IntDiv
    //    operand appears ONLY inside a Select Case `When` guard (never anywhere else in the
    //    program), so this is the shape that needed C++/JS/MSIL taught to render an IRCast in a
    //    guard at all before IRBuilder started emitting one there. The guard's variable is also
    //    read OUTSIDE the guard (Console.WriteLine(y) / (y) and (n)), matching the corpus exactly.
    //
    //    VALUE is pinned on every backend. TEXT is additionally pinned for C++ and JS on BOTH
    //    SC6 and SC7: SC6's y is exactly 8.0 (not a fraction), so its printed value alone cannot
    //    tell "the guard rounds correctly" apart from "the guard reads y some other way that
    //    happens to answer 4 \ 2" — only the emitted cast/helper TEXT proves the guard actually
    //    went through the conversion path (mutation m5's target). SC7 does discriminate rounding
    //    from truncation by value (-8\2=-4 vs -7\2=-3), but is pinned by text too for the same
    //    reason: proving the JS module-scan found a rounding cast whose ONLY appearance in the
    //    whole program is inside a guard (GuardUsesRoundingHelper) is a claim about the SCAN, not
    //    just the number it produces.
    // ============================================================================================

    private const string SC6_Guard = """
        Function D(v As Double) As Double
            Return v
        End Function

        Function I(v As Integer) As Integer
            Return v
        End Function

        Sub Main()
            Dim y As Double = D(8)
            Console.WriteLine(y)
            Select Case I(1)
                Case Is > 0 When y \ 2 = 4
                    Console.WriteLine("guard-even")
                Case Else
                    Console.WriteLine("guard-other")
            End Select
        End Sub
        """;

    private const string SC6_Expected = "8\nguard-even";

    private const string SC7_Guard = """
        Function D(v As Double) As Double
            Return v
        End Function

        Function I(v As Integer) As Integer
            Return v
        End Function

        Sub Main()
            Dim y As Double = D(-7.5)
            Dim n As Integer = I(2)
            Console.WriteLine(y)
            Console.WriteLine(n)
            Select Case I(1)
                Case Is > 0 When y \ n = -4
                    Console.WriteLine("guard-even")
                Case Else
                    Console.WriteLine("guard-other")
            End Select
        End Sub
        """;

    private const string SC7_Expected = "-7.5\n2\nguard-even";

    [TestCase(nameof(SC6_Guard), nameof(SC6_Expected), TestName = "SC6_WhenGuard_StandardPipeline")]
    [TestCase(nameof(SC7_Guard), nameof(SC7_Expected), TestName = "SC7_WhenGuard_StandardPipeline")]
    public void WhenGuard_Value_StandardPipeline(string sourceField, string expectedField)
        => FourBackends.RunsOnEveryBackend(Const(sourceField), Const(expectedField));

    [TestCase(nameof(SC6_Guard), nameof(SC6_Expected), TestName = "SC6_WhenGuard_AggressivePipeline")]
    [TestCase(nameof(SC7_Guard), nameof(SC7_Expected), TestName = "SC7_WhenGuard_AggressivePipeline")]
    public void WhenGuard_Value_AggressivePipeline(string sourceField, string expectedField)
        => FourBackends.RunsOnEveryBackendAggressive(Const(sourceField), Const(expectedField));

    [Test]
    public void SC6_WhenGuard_Cpp_RendersTheConvertingCastInline()
        => Assert.That(BclE2E.CompileToCppOptimized(SC6_Guard),
            Does.Contain("static_cast<int64_t>(std::nearbyint(y))"),
            "the guard must render the same converting cast Visit(IRCast) renders in a statement " +
            "(step3a's RenderInline arm) — SC6's value (8 \\ 2 = 4) cannot tell this apart from a " +
            "guard that merely reads y unconverted, since 8.0 has nothing to round");

    [Test]
    public void SC7_WhenGuard_Cpp_RendersTheConvertingCastInline()
        => Assert.That(BclE2E.CompileToCppOptimized(SC7_Guard),
            Does.Contain("static_cast<int64_t>(std::nearbyint(y))"));

    [Test]
    public void SC6_WhenGuard_Js_EmitsTheRoundingHelper_EvenThoughTheOnlyCastIsInTheGuard()
    {
        var js = JsTestSupport.CompileOptimized(SC6_Guard);
        Assert.Multiple(() =>
        {
            Assert.That(js, Does.Contain("function __blCInt"),
                "GuardUsesRoundingHelper must find the cast even though it is emitted with " +
                "instruction emission suppressed and never enters a block the ordinary scan walks");
            Assert.That(js, Does.Contain("__blCInt(y)"));
        });
    }

    [Test]
    public void SC7_WhenGuard_Js_EmitsTheRoundingHelper_EvenThoughTheOnlyCastIsInTheGuard()
    {
        var js = JsTestSupport.CompileOptimized(SC7_Guard);
        Assert.Multiple(() =>
        {
            Assert.That(js, Does.Contain("function __blCInt"));
            Assert.That(js, Does.Contain("__blCInt(y)"));
        });
    }

    /// <summary>
    /// MUTATION (m6) TARGET. The guard's converted IntDiv must be typed Long so MSIL's own D4
    /// operand coercion converts the OTHER operand (`n`, an Integer) to int64 too — dropping that
    /// result-type fix is invisible everywhere else and visible ONLY here: without it MSIL emits
    /// an int64/int32 `div`, which ECMA-335 forbids and the x64 JIT merely tolerates rather than
    /// rejecting, so no value assertion catches it. Exact IL sequence measured directly
    /// (MsilHarness.CompileToIl on SC7, standard pipeline): the y-side FPToSI cast
    /// (`Math::Round` + `conv.i8`) is immediately followed by `ldloc.1` (n) + a SECOND `conv.i8`
    /// (the coercion this pins) before `div`.
    /// </summary>
    [Test]
    public void SC7_WhenGuard_Msil_CoercesTheOtherOperandToInt64Too()
    {
        var il = MsilHarness.CompileToIl(SC7_Guard, "SC7Il");
        Assert.That(il, Does.Match(@"conv\.i8\s*\r?\n\s*ldloc\.1\s*\r?\n\s*conv\.i8\s*\r?\n\s*div"),
            "expected the y-side FPToSI cast (ending in conv.i8) to be immediately followed by " +
            "loading n and converting IT to int64 too, before the div — the guard IntDiv's own " +
            "result-type fix (a converted guard IntDiv is typed Long) is what drives MSIL's D4 " +
            "operand coercion to add that second conv.i8");
    }

    // ============================================================================================
    // 8. KNOWN FAILURES OUTSIDE D1 — pinned only where cheap and clearly labelled; not chased.
    // ============================================================================================

    /// <summary>
    /// ST1 — `Dim i As Integer = 7.5 \ 2` on JavaScript. NOT a D1 defect: the floating operand
    /// converts to Long correctly (ADR-0005 D1's own job), but narrowing that Long result INTO
    /// an Integer local hits a pre-existing, unrelated JS gap — <c>JavaScriptBackend.TryNumericCast</c>
    /// has no integral-to-integral arm (see its own doc comment, "Integral→integral is NOT
    /// handled"), because before D1 no shape could ever produce a Long value on this backend in
    /// the first place (Long is banned in every DECLARED position, BL7003) to narrow. Measured:
    /// <c>NotSupportedException</c>, "JavaScript backend: IRCast lowering is not implemented yet."
    /// </summary>
    [TestCase(
        "Sub Main()\n    Dim a As Integer = 7.5 \\ 2\n    Console.WriteLine(a)\nEnd Sub",
        TestName = "ST1_LiteralLongToIntegerNarrowing_RefusedOnJs")]
    [TestCase(
        "Function D(v As Double) As Double\n    Return v\nEnd Function\n" +
        "Sub Main()\n    Dim b As Integer = D(8.5) \\ 2\n    Console.WriteLine(b)\nEnd Sub",
        TestName = "ST1_RuntimeLongToIntegerNarrowing_RefusedOnJs")]
    public void KnownGap_LongToIntegerNarrowing_IsRefusedOnJavaScript_NotThisFamilys(string program)
    {
        var ex = Assert.Throws<NotSupportedException>(() => JsTestSupport.CompileOptimized(program));
        Assert.That(ex!.Message, Does.Contain("IRCast lowering is not implemented yet"));
    }
}

/// <summary>
/// STRUCTURAL / compile-time-only fixtures: the ADR's structural pin ("after IRBuilder, no
/// IntDiv node has a floating operand"), ported from <c>scratchpad/.../d1/irprobe/Program.cs</c>'s
/// walk verbatim, plus the JavaScript BL7003 capability-refusal pins. Nothing here spawns a
/// process, so it carries no Category and runs in the fast subset.
/// </summary>
[TestFixture]
public class FloatingIntegerDivisionStructuralTests
{
    // ============================================================================================
    // 5. THE STRUCTURAL PIN — ported from irprobe/Program.cs's Scan, not reinvented. Walks every
    //    module function, class method, property accessor, and every IRSwitch
    //    PatternCases[*].WhenGuard tree (including Or and Tuple sub-patterns), both after
    //    IRBuilder.Build and after AggressivePipeline.Apply.
    // ============================================================================================

    private readonly record struct ProbeResult(int IntDivCount, int FloatingOperandCount, int InGuardCount);

    /// <summary>
    /// Verbatim port of irprobe/Program.cs's <c>Scan</c>. Any IntDiv operand still typed Single
    /// or Double after IRBuilder is a violation (<paramref name="onFloatingOperand"/> — used by
    /// <see cref="TheWalkRecursesIntoOrAndTuplePatterns"/> to prove the recursion reaches a
    /// violation buried inside an Or/Tuple sub-pattern's own WhenGuard, not just the switch's own
    /// top-level guard).
    /// </summary>
    private static ProbeResult Probe(IRModule module, Action<IRBinaryOp> onFloatingOperand = null)
    {
        int n = 0, f = 0, g = 0;
        var seen = new HashSet<IRValue>(ReferenceEqualityComparer.Instance);

        void Val(IRValue v, bool guard)
        {
            if (v == null || !seen.Add(v)) return;
            switch (v)
            {
                case IRBinaryOp b:
                    if (b.Operation == BinaryOpKind.IntDiv)
                    {
                        n++; if (guard) g++;
                        if (b.Left?.Type?.IsFloatingPoint() == true || b.Right?.Type?.IsFloatingPoint() == true)
                        {
                            f++;
                            onFloatingOperand?.Invoke(b);
                        }
                    }
                    Val(b.Left, guard); Val(b.Right, guard); break;
                case IRCompare c: Val(c.Left, guard); Val(c.Right, guard); break;
                case IRUnaryOp u: Val(u.Operand, guard); break;
                case IRCast k: Val(k.Value, guard); break;
                case IRCall call: foreach (var a in call.Arguments) Val(a, guard); break;
            }
        }

        void Pat(IRPatternCase pc)
        {
            if (pc == null) return;
            Val(pc.WhenGuard, true);
            if (pc is IROrPatternCase o) foreach (var a in o.Alternatives) Pat(a);
            if (pc is IRTuplePatternCase t) foreach (var e in t.Elements) Pat(e);
        }

        void Fn(IRFunction fn)
        {
            if (fn == null) return;
            foreach (var bb in fn.Blocks)
                foreach (var ins in bb.Instructions)
                {
                    if (ins is IRValue v) Val(v, false);
                    if (ins is IRSwitch sw) foreach (var pc in sw.PatternCases) Pat(pc);
                    if (ins is IRAssignment asn) Val(asn.Value, false);
                    if (ins is IRReturn r) Val(r.Value, false);
                }
        }

        foreach (var fn in module.Functions) Fn(fn);
        foreach (var cls in module.Classes.Values)
        {
            foreach (var me in cls.Methods) Fn(me.Implementation);
            foreach (var pr in cls.Properties) { Fn(pr.Getter); Fn(pr.Setter); }
        }
        return new ProbeResult(n, f, g);
    }

    private static void AssertEachFloatingOperandIsAConvertedIRCastToLong(IRModule module)
    {
        var violations = new List<string>();
        Probe(module, onFloatingOperand: b => violations.Add(
            $"IntDiv {b.Name}: Left={Describe(b.Left)} Right={Describe(b.Right)}"));
        Assert.That(violations, Is.Empty,
            "no IntDiv operand may be typed Single or Double after IRBuilder:\n" +
            string.Join("\n", violations));

        static string Describe(IRValue v) => v == null ? "null" : $"{v.GetType().Name}:{v.Type?.Name}";
    }

    /// <summary>Asserts every converted operand is exactly what ADR-0005 D1 specifies.</summary>
    private static void AssertConvertedOperandsAreFPToSICastsToLong(IRModule module)
    {
        var badCasts = new List<string>();
        void Walk(IRValue v, HashSet<IRValue> seen)
        {
            if (v == null || !seen.Add(v)) return;
            if (v is IRCast cast && cast.Value?.Type?.IsFloatingPoint() == true
                && cast.Type?.Name == "Long")
            {
                if (cast.Kind != CastKind.FPToSI)
                    badCasts.Add($"{cast.Name}: kind={cast.Kind}, expected FPToSI");
            }
            switch (v)
            {
                case IRBinaryOp b: Walk(b.Left, seen); Walk(b.Right, seen); break;
                case IRCompare c: Walk(c.Left, seen); Walk(c.Right, seen); break;
                case IRUnaryOp u: Walk(u.Operand, seen); break;
                case IRCast k: Walk(k.Value, seen); break;
                case IRCall call: foreach (var a in call.Arguments) Walk(a, seen); break;
            }
        }
        void WalkPattern(IRPatternCase pc, HashSet<IRValue> seen)
        {
            if (pc == null) return;
            Walk(pc.WhenGuard, seen);
            if (pc is IROrPatternCase o) foreach (var a in o.Alternatives) WalkPattern(a, seen);
            if (pc is IRTuplePatternCase t) foreach (var e in t.Elements) WalkPattern(e, seen);
        }
        void WalkFn(IRFunction fn)
        {
            if (fn == null) return;
            var seen = new HashSet<IRValue>(ReferenceEqualityComparer.Instance);
            foreach (var bb in fn.Blocks)
                foreach (var ins in bb.Instructions)
                {
                    if (ins is IRValue v) Walk(v, seen);
                    if (ins is IRSwitch sw) foreach (var pc in sw.PatternCases) WalkPattern(pc, seen);
                    if (ins is IRAssignment asn) Walk(asn.Value, seen);
                    if (ins is IRReturn r) Walk(r.Value, seen);
                }
        }
        foreach (var fn in module.Functions) WalkFn(fn);
        foreach (var cls in module.Classes.Values)
        {
            foreach (var me in cls.Methods) WalkFn(me.Implementation);
            foreach (var pr in cls.Properties) { WalkFn(pr.Getter); WalkFn(pr.Setter); }
        }

        Assert.That(badCasts, Is.Empty,
            "every floating-to-Long conversion IRBuilder inserts for `\\` must be an IRCast with " +
            "kind FPToSI:\n" + string.Join("\n", badCasts));
    }

    /// <summary>
    /// One program exercising every shape the ADR names: a module-level function, a class
    /// method, a class property Get AND Set, and a Select Case `When` guard whose variables are
    /// also read outside it — all with a floating `\` operand.
    /// </summary>
    private const string RepresentativeProgram = """
        Function D(v As Double) As Double
            Return v
        End Function

        Function I(v As Integer) As Integer
            Return v
        End Function

        Class Calc
            Private _v As Double

            Public Sub New(v As Double)
                _v = v
            End Sub

            Public Function Method(a As Double, b As Integer) As Long
                Return a \ b
            End Function

            Public Property Half As Long
                Get
                    Return _v \ 2
                End Get
                Set(value As Long)
                    _v = value
                End Set
            End Property
        End Class

        Sub Main()
            Dim y As Double = D(-7.5)
            Dim n As Integer = I(2)
            Dim c As New Calc(8.5)
            Console.WriteLine(c.Method(7.5, 2))
            Console.WriteLine(c.Half)
            Select Case I(1)
                Case Is > 0 When y \ n = -4
                    Console.WriteLine("guard-even")
                Case Else
                    Console.WriteLine("guard-other")
            End Select
        End Sub
        """;

    private static IRModule BuildRepresentativeModule()
    {
        var parser = new Parser(new Lexer(RepresentativeProgram).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, string.Join("; ", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True, string.Join("; ", analyzer.Errors.Select(e => e.Message)));

        return new IRBuilder(analyzer).Build(ast, "D1Probe");
    }

    [Test]
    public void AfterIRBuilder_NoIntDivHasAFloatingOperand()
    {
        var module = BuildRepresentativeModule();
        var result = Probe(module);

        AssertEachFloatingOperandIsAConvertedIRCastToLong(module);
        AssertConvertedOperandsAreFPToSICastsToLong(module);
        Assert.That(result.IntDivCount, Is.GreaterThan(0), "the probe found no IntDiv at all — the test program is broken");
        Assert.That(result.InGuardCount, Is.GreaterThan(0), "the probe found no IntDiv inside a guard — the test program is broken");
    }

    [Test]
    public void AfterAggressivePipeline_NoIntDivHasAFloatingOperand()
    {
        var module = BuildRepresentativeModule();
        AggressivePipeline.Apply(module);

        AssertEachFloatingOperandIsAConvertedIRCastToLong(module);
        AssertConvertedOperandsAreFPToSICastsToLong(module);
    }

    /// <summary>
    /// Proves the PORTED walk itself recurses into an Or pattern's nested alternative and a
    /// Tuple pattern's element — not just a switch's own top-level WhenGuard — by hand-building
    /// an IRSwitch with a deliberately-still-floating IntDiv buried in each, mirroring
    /// <c>GetOperandsUseCountFixTests.GetOperandsRecursesIntoEveryPatternCaseShape</c>'s
    /// construction. If the port ever drops the <c>IROrPatternCase</c>/<c>IRTuplePatternCase</c>
    /// recursion, this fails; nothing else in this file could catch a truncated port, because a
    /// real BasicLang program cannot express an Or/Tuple pattern with a `\` guard today (the
    /// parser routes every `Select Case` value into <c>PatternCases</c> as flat comparisons —
    /// same reason <c>GetOperandsUseCountFixTests</c> hand-builds these shapes instead of parsing
    /// them).
    /// </summary>
    [Test]
    public void TheWalkRecursesIntoOrAndTuplePatterns()
    {
        var doubleType = new BasicLang.Compiler.SemanticAnalysis.TypeInfo(
            "Double", BasicLang.Compiler.SemanticAnalysis.TypeKind.Primitive);
        var intType = new BasicLang.Compiler.SemanticAnalysis.TypeInfo(
            "Integer", BasicLang.Compiler.SemanticAnalysis.TypeKind.Primitive);
        var subject = new IRVariable("subject", intType);
        var y = new IRVariable("y", doubleType);

        var target = new BasicBlock("case");

        IRBinaryOp FloatingIntDiv(string name) =>
            new(name, BinaryOpKind.IntDiv, y, new IRVariable("two", intType), doubleType);

        var orNested = new IRConstantPatternCase(new IRVariable("k1", intType), target)
            { WhenGuard = FloatingIntDiv("or_violation") };
        var orCase = new IROrPatternCase(target);
        orCase.Alternatives.Add(orNested);

        var tupleElement = new IRConstantPatternCase(new IRVariable("k2", intType), target)
            { WhenGuard = FloatingIntDiv("tuple_violation") };
        var tupleCase = new IRTuplePatternCase(target);
        tupleCase.Elements.Add(tupleElement);

        var switchInst = new IRSwitch(subject, target);
        switchInst.PatternCases.Add(orCase);
        switchInst.PatternCases.Add(tupleCase);

        var fn = new IRFunction("Probe", null);
        var block = fn.CreateBlock("entry");
        block.Instructions.Add(switchInst);

        var module = new IRModule("M");
        module.Functions.Add(fn);

        var found = new List<string>();
        var result = Probe(module, onFloatingOperand: b => found.Add(b.Name));

        Assert.Multiple(() =>
        {
            Assert.That(result.FloatingOperandCount, Is.EqualTo(2),
                "expected both the Or-nested and Tuple-element violations to be found");
            Assert.That(found, Is.EquivalentTo(new[] { "or_violation", "tuple_violation" }));
            Assert.That(result.InGuardCount, Is.EqualTo(2), "both violating IntDivs are inside a guard");
        });
    }

    // ============================================================================================
    // 4 (continued). JavaScript's BL7003 refusal of Long — pure compile-time capability check,
    // no process spawn. A floating `\` operand converts to Long internally (fine, since it never
    // leaves an expression — see ADR note (a)), but a DECLARED Long position is still refused
    // exactly as before D1, matching the corpus's I2/P2 rows.
    // ============================================================================================

    [TestCase(
        "Function L(v As Long) As Long\n    Return v\nEnd Function",
        TestName = "BL7003_DeclaredLongParameterAndReturn_StillRefused")]
    public void JavaScript_StillRefusesDeclaredLong_BL7003(string declaration)
    {
        var program = $"{declaration}\nSub Main()\n    Console.WriteLine(L(9000000001) \\ L(2))\nEnd Sub";
        var module = JsTestSupport.BuildModule(program);
        var ex = Assert.Throws<ForeignFeatureException>(() => new JavaScriptCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("BL7003"));
    }

    [Test]
    public void JavaScript_StillRefusesDeclaredLongLocal_BL7003()
    {
        var program = """
            Function D(v As Double) As Double
                Return v
            End Function

            Sub Main()
                Dim a As Long = D(7.5)
                Console.WriteLine(a)
            End Sub
            """;
        var module = JsTestSupport.BuildModule(program);
        var ex = Assert.Throws<ForeignFeatureException>(() => new JavaScriptCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("BL7003"));
    }
}
