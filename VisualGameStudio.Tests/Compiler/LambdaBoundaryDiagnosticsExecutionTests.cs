using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task #174, execution — the lambda-boundary diagnostics (BC36639/BC30616/BC30734/BC36667)
/// taken all the way to a REFUSED build at every entry point (the CLI, every <c>--target</c>,
/// both pipelines, and a Release <c>.blproj</c> build), and every accepted shape taken to a
/// RUNNING program where the backend under test supports it. <see cref="LambdaBoundaryDiagnosticsTests"/>
/// is the fast-subset sibling that pins each diagnostic's code, message and line/column directly
/// off the analyzer, and covers the LSP path.
///
/// <para>Probe sources and expected values are the implementer's own measured ones
/// (<c>S/t174/probes</c>, <c>/edge</c>, <c>/blk</c>) and re-verified directly against this build
/// (CLI, real g++/node/ilasm) by the test-writer before being pinned here — never re-derived from
/// what a backend under test prints.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class LambdaBoundaryDiagnosticsExecutionTests
{
    // ====================================================================================
    // Shared plumbing.
    // ====================================================================================

    private static readonly string[] AllCliTargets = { "csharp", "cpp", "javascript", "msil" };

    /// <summary>The CLI must refuse this program, naming <paramref name="code"/>, on EVERY
    /// <c>--target</c> — no backend ever gets to run. Mirrors
    /// <c>PropertyAccessExecutionTests.SingleFileCli_RefusesOnEveryTarget</c> (task #178's own
    /// convention).</summary>
    private static void CliRefusesOnEveryTarget(string source, string code, bool optimize = false)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-lambdaboundary-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var basFile = Path.Combine(dir, "Program.bas");
            File.WriteAllText(basFile, source);

            Assert.Multiple(() =>
            {
                foreach (var target in AllCliTargets)
                {
                    var args = optimize
                        ? new[] { basFile, "--target=" + target, "--optimize" }
                        : new[] { basFile, "--target=" + target };
                    var (exitCode, stdOut, stdErr) = CliTestHarness.RunProcess(
                        CliTestHarness.CliPath(), args, dir, timeoutMs: 60_000);
                    var output = stdOut + "\n" + stdErr;
                    Assert.That(exitCode, Is.Not.EqualTo(0), $"--target={target} must refuse.\n{output}");
                    Assert.That(output, Does.Contain(code), $"--target={target} must name {code}.\n{output}");
                    // No backend artifact should ever be produced for a front-end refusal.
                    Assert.That(File.Exists(Path.Combine(dir, "Program.cs")), Is.False, $"--target={target} wrote C#");
                    Assert.That(File.Exists(Path.Combine(dir, "Program.cpp")), Is.False, $"--target={target} wrote C++");
                }
            });
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort temp cleanup */ }
        }
    }

    private string _projectDir = null!;

    [SetUp]
    public void SetUp()
    {
        _projectDir = Path.Combine(Path.GetTempPath(), "bl-lambdaboundary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectDir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_projectDir, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    /// <summary>The Release <c>.blproj</c> build (the IDE's own path) — same idiom as
    /// <c>PropertyAccessExecutionTests.BuildRelease</c>.</summary>
    private (int ExitCode, string StdOut, string StdErr) BuildRelease(string basSource, string targetBackend)
    {
        File.WriteAllText(Path.Combine(_projectDir, "Main.bas"), basSource);
        File.WriteAllText(Path.Combine(_projectDir, "App.blproj"),
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <OutputType>Exe</OutputType>
                <TargetBackend>{targetBackend}</TargetBackend>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="Main.bas" />
              </ItemGroup>
            </BasicLangProject>
            """);

        return CliTestHarness.RunProcess(
            CliTestHarness.CliPath(),
            new[] { "build", Path.Combine(_projectDir, "App.blproj"), "-c", "Release" },
            _projectDir,
            timeoutMs: 120_000);
    }

    private void ReleaseBlprojBuild_Refuses(string source, string code, string targetBackend = "MSIL")
    {
        var (exitCode, stdOut, stdErr) = BuildRelease(source, targetBackend);
        var output = stdOut + "\n" + stdErr;
        Assert.That(exitCode, Is.Not.EqualTo(0), $"a Release .blproj build must refuse this too.\n{output}");
        Assert.That(output, Does.Contain(code), output);
    }

    /// <summary>Emit C# from IR built WITHOUT the front end's own gate (<c>Assert.That(analyzer.
    /// Analyze(ast), Is.True, …)</c>) — the same idiom as <c>MsilInterfacePropertyCompileTests.
    /// CompileToIlFromUncheckedIr</c> (task #178), applied to the C# backend: the analyzer runs
    /// and its errors are still there, but a refused program is handed to <c>IRBuilder</c>/
    /// <c>ImprovedCSharpCodeGenerator</c> regardless. Used only for a shape #174 now refuses at
    /// the front end, to prove a PRE-EXISTING backend defect underneath that refusal is still
    /// there (task #232) — never to claim the shape itself still compiles through any real,
    /// checked entry point.</summary>
    private static string EmitCSharpFromUncheckedIr(string source)
    {
        var ast = new Parser(new Lexer(source).Tokenize()).Parse();
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);   // errors intentionally IGNORED — see the summary above
        var module = new IRBuilder(analyzer).Build(ast, "LambdaBoundaryProbe");
        return new ImprovedCSharpCodeGenerator().Generate(module);
    }

    /// <summary>Whether Roslyn accepts <paramref name="csharp"/>, and its error diagnostics if
    /// not — same idiom as <c>NameBindingExecutionTests.CSharpFailsToCompile</c>.</summary>
    private static (bool Failed, string Diagnostics) CSharpCompileFails(string csharp)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToImmutableArray();
        var compilation = CSharpCompilation.Create(
            "LambdaBoundaryPinProbe_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(csharp) },
            references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        using var ms = new MemoryStream();
        var emitted = compilation.Emit(ms);
        var diagnostics = string.Join("\n", emitted.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()));
        return (!emitted.Success, diagnostics + "\n--- emitted ---\n" + csharp);
    }

    // ====================================================================================
    // 1. Refused shapes, at BOTH entry points: the CLI (every --target, standard AND
    // --optimize) and the Release .blproj build (CompileProjectFiles — the IDE's own path).
    // ====================================================================================

    private const string R1 = """
        Sub Bump(ByRef n As Integer)
            Dim f As Action = Sub() n = n + 1
            f()
        End Sub
        Sub Main()
            Dim a As Integer = 1
            Bump(a)
            Console.WriteLine(a)
        End Sub
        """;

    private const string N1 = """
        Sub Main()
            Dim x As Integer = 5
            Dim f As Func(Of Integer) = Function()
                    Dim y As Integer = x
                    If y > 0 Then
                        Dim x As Integer = 10
                        y = y + x
                    End If
                    Return y
                End Function
            Console.WriteLine(f())
        End Sub
        """;

    private const string N4 = """
        Function Calc(x As Integer) As Integer
            Dim f As Func(Of Integer) = Function()
                    Dim t As Integer = x * 2
                    For i As Integer = 1 To 1
                        Dim x As Integer = 100
                        t = t + x
                    Next
                    Return t
                End Function
            Return f()
        End Function
        Sub Main()
            Console.WriteLine(Calc(5))
        End Sub
        """;

    private const string N11 = """
        Sub Main()
            Dim f As Func(Of Integer, Integer) = Function(a)
                    Dim g As Func(Of Integer) = Function()
                            Dim a As Integer = 1
                            Return a
                        End Function
                    Return a + g()
                End Function
            Console.WriteLine(f(5))
        End Sub
        """;

    [Test]
    public void R1_ByRefWrite_CliRefusesOnEveryTarget_BC36639() => CliRefusesOnEveryTarget(R1, "BC36639");

    [Test]
    public void R1_ByRefWrite_CliOptimizeAlsoRefuses_BC36639() => CliRefusesOnEveryTarget(R1, "BC36639", optimize: true);

    [Test]
    public void R1_ByRefWrite_ReleaseBlprojBuildAlsoRefuses_BC36639() => ReleaseBlprojBuild_Refuses(R1, "BC36639");

    [Test]
    public void N1_LambdaLocalHidesCreatorsLocal_CliRefusesOnEveryTarget_BC30616() => CliRefusesOnEveryTarget(N1, "BC30616");

    [Test]
    public void N1_LambdaLocalHidesCreatorsLocal_CliOptimizeAlsoRefuses_BC30616() => CliRefusesOnEveryTarget(N1, "BC30616", optimize: true);

    [Test]
    public void N1_LambdaLocalHidesCreatorsLocal_ReleaseBlprojBuildAlsoRefuses_BC30616() => ReleaseBlprojBuild_Refuses(N1, "BC30616");

    [Test]
    public void N4_LambdaLocalHidesProceduresParameter_CliRefusesOnEveryTarget_BC30734() => CliRefusesOnEveryTarget(N4, "BC30734");

    [Test]
    public void N4_LambdaLocalHidesProceduresParameter_ReleaseBlprojBuildAlsoRefuses_BC30734() => ReleaseBlprojBuild_Refuses(N4, "BC30734");

    [Test]
    public void N11_NestedLambdaLocalHidesOuterLambdasParameter_CliRefusesOnEveryTarget_BC36667() => CliRefusesOnEveryTarget(N11, "BC36667");

    [Test]
    public void N11_NestedLambdaLocalHidesOuterLambdasParameter_ReleaseBlprojBuildAlsoRefuses_BC36667() => ReleaseBlprojBuild_Refuses(N11, "BC36667");

    // N7 — the "later declaration" half of BC30616 (the creator's local is declared AFTER the
    // lambda, in the same enclosing block) must reach BOTH entry points too, not only the
    // in-process analyzer the fast fixture drives directly.
    private const string N7 = """
        Sub Main()
            Dim f As Func(Of Integer) = Function()
                    Dim x As Integer = 3
                    Return x
                End Function
            Dim x As Integer = 5
            Console.WriteLine(f() + x)
        End Sub
        """;

    [Test]
    public void N7_CreatorsLaterLocal_CliRefusesOnEveryTarget_BC30616() => CliRefusesOnEveryTarget(N7, "BC30616");

    [Test]
    public void N7_CreatorsLaterLocal_ReleaseBlprojBuildAlsoRefuses_BC30616() => ReleaseBlprojBuild_Refuses(N7, "BC30616");

    // ====================================================================================
    // 2. Must NOT be refused, and must RUN with the expected output on every backend that
    // supports the shape at all.
    // ====================================================================================

    // N5 — a lambda local sharing no name with anything outside it.
    private const string N5 = """
        Sub Main()
            Dim x As Integer = 5
            Dim f As Func(Of Integer) = Function()
                    Dim y As Integer = 3
                    Return x + y
                End Function
            Console.WriteLine(f())
        End Sub
        """;

    [Test]
    public void N5_RunsOnEveryBackend() => FourBackends.RunsOnEveryBackend(N5, "8");

    [Test]
    public void N5_RunsOnEveryBackendAggressive() => FourBackends.RunsOnEveryBackendAggressive(N5, "8");

    // N8 — hiding a class field. Every backend agrees with VB (8); C# printed 10 until #136 (#165) wrote a lambda's own Dim.
    private const string N8 = """
        Class C
            Public x As Integer = 5
            Public Function Run() As Integer
                Dim f As Func(Of Integer) = Function()
                        Dim x As Integer = 3
                        Return x
                    End Function
                Return f() + x
            End Function
        End Class
        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.Run())
        End Sub
        """;

    [Test]
    public void N8_HidingAClassField_CppRuns8() =>
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(N8))), Is.EqualTo("8"));

    [Test]
    public void N8_HidingAClassField_JsRuns8() =>
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(N8)), Is.EqualTo("8"));

    [Test]
    public void N8_HidingAClassField_MsilRuns8() =>
        Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(N8)), Is.EqualTo("8"));

    /// <summary>
    /// ⭐ MOVED PIN (#165, fixed by #136). N8 on C# USED TO print 10 for VB's 8: the C# backend dropped a lambda's own <c>Dim</c> when it
    /// shared a name with a field/global/sibling local, so the emitted lambda body read the OUTER name straight through and the
    /// field's value (5) won over the lambda's own local (3), 5 + 5 for 3 + 5. The lambda body is now written by the function-body
    /// emitter, which declares the lambda's own local, so the local hides the field and C# prints 8 (vbc: 8). Hang-safe runner, both pipelines.
    /// </summary>
    [Test]
    public void N8_HidingAClassField_CSharpRuns8()
    {
        Assert.That(FourBackends.Norm(CSharpProcessRunner.RunExpectingSuccess(ReturnCoercionTests.EmitCSharpForTest(N8))), Is.EqualTo("8"), "standard");
        Assert.That(FourBackends.Norm(CSharpProcessRunner.RunExpectingSuccess(ReturnCoercionTests.EmitCSharpAggressiveForTest(N8))), Is.EqualTo("8"), "aggressive");
    }

    // N9 — hiding a module global. Same shape as N8, same C# answer now.
    private const string N9 = """
        Dim x As Integer = 5
        Sub Main()
            Dim f As Func(Of Integer) = Function()
                    Dim x As Integer = 3
                    Return x
                End Function
            Console.WriteLine(f() + x)
        End Sub
        """;

    [Test]
    public void N9_HidingAModuleGlobal_CppRuns8() =>
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(N9))), Is.EqualTo("8"));

    [Test]
    public void N9_HidingAModuleGlobal_JsRuns8() =>
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(N9)), Is.EqualTo("8"));

    [Test]
    public void N9_HidingAModuleGlobal_MsilRuns8() =>
        Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(N9)), Is.EqualTo("8"));

    /// <summary>⭐ MOVED PIN (#165, fixed by #136): N9 on C# USED TO print 10 for 8, the same dropped lambda local as N8; now 8, like vbc.</summary>
    [Test]
    public void N9_HidingAModuleGlobal_CSharpRuns8()
    {
        Assert.That(FourBackends.Norm(CSharpProcessRunner.RunExpectingSuccess(ReturnCoercionTests.EmitCSharpForTest(N9))), Is.EqualTo("8"), "standard");
        Assert.That(FourBackends.Norm(CSharpProcessRunner.RunExpectingSuccess(ReturnCoercionTests.EmitCSharpAggressiveForTest(N9))), Is.EqualTo("8"), "aggressive");
    }

    // R3 — the ByRef parameter copied into a local first: VB accepts and runs (8) on every
    // backend that can even represent a ByRef parameter's declaration; C# and JS each hit their
    // OWN pre-existing, unrelated gap.
    private const string R3 = """
        Function Twice(ByRef n As Integer) As Integer
            Dim copy As Integer = n
            Dim g As Func(Of Integer) = Function() copy * 2
            Return g()
        End Function
        Sub Main()
            Dim a As Integer = 4
            Console.WriteLine(Twice(a))
        End Sub
        """;

    [Test]
    public void R3_ByRefCopiedIntoLocalFirst_CppRuns8() =>
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(R3))), Is.EqualTo("8"));

    [Test]
    public void R3_ByRefCopiedIntoLocalFirst_MsilRuns8() =>
        Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(R3)), Is.EqualTo("8"));

    /// <summary>Task #232 (pre-existing, unrelated to #174): the C# backend mishandles ANY
    /// function with a ByRef parameter that also creates a lambda, regardless of whether the
    /// lambda touches the parameter — R3's lambda captures only the LOCAL copy, yet the emitted
    /// call site still passes the ORIGINAL argument by <c>ref</c> into a method whose signature
    /// the backend rewrote, giving Roslyn's CS1620. A different outcome (including a clean
    /// compile) means #232 moved — update this pin, do not just delete it.</summary>
    [Test]
    public void R3_ByRefCopiedIntoLocalFirst_CSharp_PinsThePreExistingCompileFailure_Against232()
    {
        var csharp = ReturnCoercionTests.EmitCSharpForTest(R3);
        var (failed, diagnostics) = CSharpCompileFails(csharp);
        Assert.That(failed, Is.True,
            "task #232 (pre-existing, unrelated to #174): the emitted C# must still fail to "
            + "compile. A clean compile here means #232 moved — update this pin, do not just "
            + "delete it.\n" + diagnostics);
        Assert.That(diagnostics, Does.Contain("CS1620"), diagnostics);
    }

    /// <summary>BL7002, by DESIGN, not a defect: JavaScript has no reference parameters, so
    /// <c>Twice</c>'s ByRef parameter alone is enough to refuse lowering — regardless of whether
    /// any lambda in the function touches it. Never "fixed"; only widened were BasicLang to gain
    /// a way to lower ByRef to JS at all.</summary>
    [Test]
    public void R3_ByRefCopiedIntoLocalFirst_JavaScript_RefusesByDesign_BL7002()
    {
        var ex = Assert.Throws<ForeignFeatureException>(() => JsTestSupport.Compile(R3));
        Assert.That(ex!.Message, Does.Contain("BL7002").And.Contain("ByRef"));
    }

    // R6 — a ByVal parameter: never refused, runs everywhere.
    private const string R6 = """
        Function Twice(ByVal n As Integer) As Integer
            Dim g As Func(Of Integer) = Function() n * 2
            Return g()
        End Function
        Sub Main()
            Console.WriteLine(Twice(4))
        End Sub
        """;

    [Test]
    public void R6_ByValParameter_RunsOnEveryBackend() => FourBackends.RunsOnEveryBackend(R6, "8");

    [Test]
    public void R6_ByValParameter_RunsOnEveryBackendAggressive() => FourBackends.RunsOnEveryBackendAggressive(R6, "8");

    // R7 — a lambda parameter spelled like the ByRef parameter around it. Task #217 (the owner's ruling, "the VB way"): VB's BC36641, at the parameter. Before it,
    // the front end ACCEPTED this and the lambda's `n` silently won: 2 on C++, C# and MSIL, and a BL7002 refusal on JavaScript (it has no ByRef parameters, by design).
    // Now no backend ever sees the program: it is refused by the front end at every entry point, and the code is BC36641 on EVERY target, JavaScript included.
    private const string R7 = """
        Sub Run(ByRef n As Integer)
            Dim f As Func(Of Integer, Integer) = Function(n) n + 1
            n = f(n)
        End Sub
        Sub Main()
            Dim a As Integer = 1
            Run(a)
            Console.WriteLine(a)
        End Sub
        """;

    [Test]
    public void R7_LambdaParameterHidesByRefParameter_CliRefusesOnEveryTarget_BC36641() => CliRefusesOnEveryTarget(R7, "BC36641");

    [Test]
    public void R7_LambdaParameterHidesByRefParameter_CliOptimizeAlsoRefuses_BC36641() => CliRefusesOnEveryTarget(R7, "BC36641", optimize: true);

    [Test]
    public void R7_LambdaParameterHidesByRefParameter_ReleaseBlprojBuildAlsoRefuses_BC36641() => ReleaseBlprojBuild_Refuses(R7, "BC36641");

    /// <summary>The JavaScript row of the old R7 pin (BL7002, "ByRef" refused by design) moved: the BC36641 comes from the FRONT END, before the JavaScript backend
    /// is asked, so the refusal names BC36641 and no longer BL7002. It is also still not BC36639: the lambda's own `n` resolves to itself (by SYMBOL, ADR-0013), never to the ByRef parameter.</summary>
    [Test]
    public void R7_LambdaParameterHidesByRefParameter_JavaScript_IsRefusedByTheFrontEnd_NotByBL7002()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-lambdaboundary-r7js-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Program.bas"), R7);
            var (exitCode, stdOut, stdErr) = CliTestHarness.RunProcess(
                CliTestHarness.CliPath(), new[] { Path.Combine(dir, "Program.bas"), "--target=javascript" }, dir, timeoutMs: 60_000);
            var output = stdOut + "\n" + stdErr;
            Assert.That(exitCode, Is.Not.EqualTo(0), output);
            Assert.That(output, Does.Contain("BC36641"), output);
            Assert.That(output, Does.Not.Contain("BL7002"), "the front end refuses before the JavaScript backend is asked.\n" + output);
            Assert.That(output, Does.Not.Contain("BC36639"), "the lambda's own n is not the ByRef parameter.\n" + output);
            Assert.That(File.Exists(Path.Combine(dir, "Program.js")), Is.False, "a front-end refusal writes no JavaScript");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort temp cleanup */ }
        }
    }

    // S5 — Me.V and bare V, both reading the SAME field, inside a lambda.
    private const string S5 = """
        Class C
            Public V As Integer
            Public Function Getter() As Func(Of Integer)
                Return Function() V + Me.V
            End Function
        End Class
        Sub Main()
            Dim c As New C()
            c.V = 7
            Dim f As Func(Of Integer) = c.Getter()
            Console.WriteLine(f())
        End Sub
        """;

    [Test]
    public void S5_BareAndMeQualifiedFieldReads_RunsOnEveryBackend() => FourBackends.RunsOnEveryBackend(S5, "14");

    // ====================================================================================
    // 3. B1-B3 — the general nested-block rule (no lambda at all) is #231's job; #174 must not
    // widen past "a Dim INSIDE A LAMBDA". Not refused on any --target.
    // ====================================================================================

    private const string B1 = """
        Sub Main()
            Dim x As Integer = 5
            If True Then
                Dim x As Integer = 10
                Console.WriteLine(x)
            End If
            Console.WriteLine(x)
        End Sub
        """;

    private const string B2 = """
        Sub Main()
            Dim x As Integer = 5
            For i As Integer = 1 To 1
                Dim x As Integer = 10
                Console.WriteLine(x)
            Next
            Console.WriteLine(x)
        End Sub
        """;

    private const string B3 = """
        Sub Show(x As Integer)
            If True Then
                Dim x As Integer = 10
                Console.WriteLine(x)
            End If
            Console.WriteLine(x)
        End Sub
        Sub Main()
            Show(5)
        End Sub
        """;

    private static void CliDoesNotRefuseWithALambdaBoundaryCode(string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-lambdaboundary-b-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var basFile = Path.Combine(dir, "Program.bas");
            File.WriteAllText(basFile, source);

            Assert.Multiple(() =>
            {
                foreach (var target in AllCliTargets)
                {
                    var (_, stdOut, stdErr) = CliTestHarness.RunProcess(
                        CliTestHarness.CliPath(), new[] { basFile, "--target=" + target }, dir, timeoutMs: 60_000);
                    var output = stdOut + "\n" + stdErr;
                    foreach (var code in new[] { "BC36639", "BC30616", "BC30734", "BC36667" })
                        Assert.That(output, Does.Not.Contain(code), $"--target={target} must not name {code}.\n{output}");
                }
            });
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort temp cleanup */ }
        }
    }

    [Test]
    public void B1_NestedBlockDimHidesOuterLocal_NoLambda_CliDoesNotReportALambdaBoundaryCode_Task231() =>
        CliDoesNotRefuseWithALambdaBoundaryCode(B1);

    [Test]
    public void B2_ForLoopBodyDimHidesOuterLocal_NoLambda_CliDoesNotReportALambdaBoundaryCode_Task231() =>
        CliDoesNotRefuseWithALambdaBoundaryCode(B2);

    [Test]
    public void B3_NestedBlockDimHidesAParameter_NoLambda_CliDoesNotReportALambdaBoundaryCode_Task231() =>
        CliDoesNotRefuseWithALambdaBoundaryCode(B3);

    /// <summary>B1 genuinely RUNS (prints 10, twice) on C# — confirms #174 leaves this shape not
    /// merely "no diagnostic" but actually usable, for at least one backend.</summary>
    [Test]
    public void B1_NestedBlockDimHidesOuterLocal_RunsOnCSharp() =>
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(B1)), Is.EqualTo("10\n10"));

    [Test]
    public void B2_ForLoopBodyDimHidesOuterLocal_RunsOnCSharp() =>
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(B2)), Is.EqualTo("10\n10"));

    /// <summary>B3's own C# emission does not compile at all (CS0136 — C# itself refuses shadowing
    /// a PARAMETER with a block-local of the same name) — a pre-existing, unrelated backend
    /// limitation that predates #174 and has nothing to do with lambdas. Recorded here only so a
    /// future change to it is noticed; #174 itself is not responsible for B3 ever running.</summary>
    [Test]
    public void B3_NestedBlockDimHidesAParameter_CSharpEmissionDoesNotCompile_PreExisting()
    {
        var csharp = ReturnCoercionTests.EmitCSharpForTest(B3);
        var (failed, diagnostics) = CSharpCompileFails(csharp);
        Assert.That(failed, Is.True,
            "pre-existing, unrelated to #174: the C# backend must still fail to compile a "
            + "parameter shadowed by a nested block Dim. A clean compile here means this "
            + "pre-existing C# limitation is gone — update or remove this pin.\n" + diagnostics);
        Assert.That(diagnostics, Does.Contain("CS0136"), diagnostics);
    }

    // ====================================================================================
    // 4. The C#-backend ByRef+lambda bug underneath R5 (#232) is STILL there, even though the
    // front end now correctly refuses R5 before any backend ever sees it — proved the same way
    // #178 kept its own pre-#178 backend-refusal coverage: feed the shape through IR built
    // WITHOUT the front end's gate.
    // ====================================================================================

    private const string R5 = """
        Class Box
            Public Function Twice(ByRef n As Integer) As Integer
                Dim g As Func(Of Integer) = Function() n * 2
                Return g()
            End Function
        End Class
        Sub Main()
            Dim b As New Box()
            Dim a As Integer = 4
            Console.WriteLine(b.Twice(a))
        End Sub
        """;

    [Test]
    public void R5_ByRefParameterOfAClassMethod_IsRefusedByTheFrontEnd_WithBC36639()
    {
        var ast = new Parser(new Lexer(R5).Tokenize()).Parse();
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        var match = analyzer.Errors.FirstOrDefault(e => e.ErrorCode == "BC36639");
        Assert.That(match, Is.Not.Null,
            "expected BC36639; got: " + string.Join(" | ", analyzer.Errors.Select(e => $"{e.ErrorCode}:{e.Message}")));
    }

    /// <summary>Task #232 (pre-existing, unrelated to #174): fed through IR that bypasses the
    /// front end's own gate — the one place left that can still reach this backend defect, now
    /// that a checked compile never does — the C# backend's ByRef+lambda mishandling is still
    /// there for a class method too (CS1628, then CS1620 at the call site). A different outcome
    /// means #232 moved — update this pin, do not just delete it.</summary>
    [Test]
    public void R5_ByRefParameterOfAClassMethod_CSharp_PinsThePreExistingCompileFailure_Against232_WhenFedUncheckedIr()
    {
        var csharp = EmitCSharpFromUncheckedIr(R5);
        var (failed, diagnostics) = CSharpCompileFails(csharp);
        Assert.That(failed, Is.True,
            "task #232 (pre-existing, unrelated to #174): the emitted C# must still fail to "
            + "compile. A clean compile here means #232 moved — update this pin, do not just "
            + "delete it.\n" + diagnostics);
        Assert.That(diagnostics, Does.Contain("CS1628").Or.Contain("CS1620"), diagnostics);
    }
}
