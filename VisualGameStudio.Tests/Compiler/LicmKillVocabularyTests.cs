using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.JavaScript;
using BasicLang.Compiler.CodeGen.MSIL;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

// =====================================================================================
//  LICM's kill vocabulary (BasicLang/IROptimizer.cs, LoopInvariantCodeMotionPass): probes
//  L1-L6 of scratchpad/f111g/licm (dated 2026-09-24), which measured two silent wrong
//  answers under `--optimize` at 15b12e8, both printing 6 where 12 is correct, BEFORE
//  VariablesWrittenIn(loop, function) was rebuilt on the shared OptimizationPass.NamesWrittenBy
//  plus a call-visible arm:
//
//    L1 - x written through an INSTANCE-method ByRef argument (`b.Bump(x)`) inside the loop.
//         The old written-set only read ByRef arguments of a free IRCall, so an
//         IRInstanceMethodCall's ByRef write was invisible and `x * 2` was hoisted as
//         "invariant" (C++, MSIL — JavaScript refuses the ByRef parameter outright, BL7002).
//    L3 - a class field K, read BARE inside one of the class's own methods, written by a
//         bare call to another method on the same instance (`Inc()`). The old written-set
//         only treated `IsGlobal` as call-visible, and a class field read bare is NOT
//         IsGlobal, so `K * 2` was hoisted out of a loop whose Inc() call bumps K (C++,
//         JavaScript, MSIL).
//
//  L2/L4 are the CONTROLS that must stay correct: L2's ByRef write is through a FREE
//  function (an IRCall, always covered), L4's write is a module global (always IsGlobal,
//  always covered). L5 is a KNOWN-WRONG gap the fix does NOT close (task #122 - a local
//  captured BY REFERENCE and written inside a lambda has no capture set yet). L6 is the
//  pass's OWN control: a truly invariant product with no call in the loop at all, proving
//  the fix does not disable LICM wholesale.
//
//  ⛔ THE PROOF REQUIRES BOTH SHIPPING ENTRY POINTS, PER CLAUDE.md - a fix seen only through
//  the non-optimizing/in-fixture helper can still break through the CLI or the IDE build. For
//  C++ specifically, a Release .blproj build runs the STANDARD pipeline (task #134 - LICM is
//  aggressive-only and a Release C++ build does not take AddAggressivePasses), so a C++
//  Release-.blproj leg CANNOT see this regression; BclE2E.CompileToCppAggressive or the CLI's
//  own `--optimize` flag are the two routes that do. MSIL's Release .blproj DOES take the
//  aggressive pipeline (Program.cs:502 maps `Configurations["Release"].OptimizationsEnabled`
//  to CompilerOptions.OptimizeAggressive), so the one Release-.blproj leg below is MSIL, on L1.
// =====================================================================================

/// <summary>BASIC sources for the probes, verbatim from scratchpad/f111g/licm/L1..L6.bas.</summary>
internal static class LicmKillVocabularyShapes
{
    internal const string L1 = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class Box
            Sub Bump(ByRef n As Integer)
                n = n + 1
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            Dim x As Integer = Seed(1)
            Dim s As Integer = 0
            For i As Integer = 1 To 3
                s = s + x * 2
                b.Bump(x)
            Next
            Console.WriteLine(CStr(s))
        End Sub
        """;

    internal const string L2 = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Sub Bump(ByRef n As Integer)
            n = n + 1
        End Sub

        Sub Main()
            Dim x As Integer = Seed(1)
            Dim s As Integer = 0
            For i As Integer = 1 To 3
                s = s + x * 2
                Bump(x)
            Next
            Console.WriteLine(CStr(s))
        End Sub
        """;

    internal const string L3 = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Class Box
            Public K As Integer

            Sub Inc()
                K = K + 1
            End Sub

            Sub Work()
                K = Seed(1)
                Dim s As Integer = 0
                For i As Integer = 1 To 3
                    s = s + K * 2
                    Inc()
                Next
                Console.WriteLine(CStr(s))
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            b.Work()
        End Sub
        """;

    internal const string L4 = """
        Dim g As Integer

        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Sub Inc()
            g = g + 1
        End Sub

        Sub Main()
            g = Seed(1)
            Dim s As Integer = 0
            For i As Integer = 1 To 3
                s = s + g * 2
                Inc()
            Next
            Console.WriteLine(CStr(s))
        End Sub
        """;

    internal const string L5 = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Sub Main()
            Dim x As Integer = Seed(1)
            Dim bump = Sub() x = x + 1
            Dim s As Integer = 0
            For i As Integer = 1 To 3
                s = s + x * 2
                bump()
            Next
            Console.WriteLine(CStr(s))
        End Sub
        """;

    internal const string L6 = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Sub Main()
            Dim x As Integer = Seed(6)
            Dim y As Integer = Seed(7)
            Dim s As Integer = 0
            For i As Integer = 1 To 3
                s = s + x * y
            Next
            Console.WriteLine(CStr(s))
        End Sub
        """;

    /// <summary>
    /// The discriminator for mutant m3 (see the class doc on <see cref="LicmKillVocabularyStructuralTests"/>):
    /// a loop that DOES contain a call (<c>Console.WriteLine(i)</c>) alongside a product of two
    /// locals neither the call nor anything else in the loop writes. A correct call-visible arm
    /// keys off <c>IsCallVisibleDestination</c> (is this storage a callee could reach at all?), so
    /// <c>x * y</c> still hoists. An OVER-KILLING arm that instead treats every variable the loop
    /// READS as written whenever the loop contains any call would mark x and y "written" too
    /// (both are read here) and lose the hoist — no wrong VALUE on any backend, just a silently
    /// deleted optimization, which is why this is a structural (ModificationCount) witness rather
    /// than a value one.
    /// </summary>
    internal const string OverKillWitness = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Sub Main()
            Dim x As Integer = Seed(6)
            Dim y As Integer = Seed(7)
            Dim s As Integer = 0
            For i As Integer = 1 To 3
                Console.WriteLine(i)
                s = s + x * y
            Next
            Console.WriteLine(CStr(s))
        End Sub
        """;

    internal const string Expected12 = "seed\n12";
    internal const string ExpectedL6 = "seed\nseed\n126";
}

/// <summary>
/// Structural pins: item 2 of the fixture brief. Every one of these runs the STANDARD pipeline
/// to a fixed point and then a single BARE <c>new LoopInvariantCodeMotionPass().Run(module)</c> —
/// never <c>pass.ModificationCount</c> after an <c>OptimizationPipeline.Run</c>, which is always
/// zero because the pipeline iterates to a fixed point and the last round of any converging pass
/// reports no change no matter what it did (see <c>LoopPassesDisabledTests</c>' docs on the same
/// trap). No process is spawned here — fast subset.
/// </summary>
[TestFixture]
public class LicmKillVocabularyStructuralTests
{
    private static (IRModule Module, LoopInvariantCodeMotionPass Pass, bool Changed) RunLicmAfterStandardSettles(string source)
    {
        var module = JsTestSupport.BuildModule(source, sourceFilePath: "prog.bas");
        var standard = new OptimizationPipeline();
        standard.AddStandardPasses();
        standard.Run(module);

        var pass = new LoopInvariantCodeMotionPass();
        bool changed = pass.Run(module);
        return (module, pass, changed);
    }

    private static bool ReferencesVariable(IRBinaryOp op, string name) =>
        (op.Left as IRVariable)?.Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true ||
        (op.Right as IRVariable)?.Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// L1 — <c>x</c> is written through <c>b.Bump(x)</c>, an IRInstanceMethodCall ByRef argument.
    /// ModificationCount must be zero, and <c>x * 2</c> must still be sitting in <c>for0.body</c>
    /// (StrengthReductionPass, a standard pass, turns the literal <c>* 2</c> into a shift by the
    /// time LICM runs — the assertion checks for the operand, not the opcode, so it survives that).
    ///
    /// <para>⛔ MUTANT m1 (revert VariablesWrittenIn to master's hand-written written-set, which
    /// read ByRef arguments of IRCall only) and m4 (VariablesWrittenIn ignores IRInstanceMethodCall
    /// specifically) both kill this test: x drops out of the written-set, x * 2 becomes "invariant"
    /// and gets hoisted, ModificationCount becomes 1.</para>
    /// </summary>
    [Test]
    public void L1_InstanceMethodByRefWrite_ModificationCountIsZero_AndTheProductStaysInTheLoop()
    {
        var (module, pass, changed) = RunLicmAfterStandardSettles(LicmKillVocabularyShapes.L1);
        var main = module.Functions.Single(f => f.Name == "Main" && !f.IsExternal);
        var body = main.Blocks.Single(b => b.Name == "for0.body");

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.False,
                "LICM hoisted something out of L1's loop — x is written through b.Bump(x), an "
                + "IRInstanceMethodCall ByRef argument, and must count as written");
            Assert.That(pass.ModificationCount, Is.Zero, "ModificationCount from the bare Run");
            Assert.That(body.Instructions.OfType<IRBinaryOp>().Any(op => ReferencesVariable(op, "x")), Is.True,
                "x * 2 (or its strength-reduced shift) must still be in for0.body, not hoisted to the preheader");
        });
    }

    /// <summary>
    /// L3 (the <c>Work</c> method's loop) — <c>K</c> is a class field read BARE inside the loop and
    /// written by a bare call to <c>Inc()</c> on the same (implicit <c>Me</c>) instance. Neither
    /// write is visible through the assignment/store/rename/ByRef arms of the shared kill
    /// vocabulary at all — K is written from the call-visible arm alone, which requires the loop to
    /// contain a call (it does: the bare <c>Inc()</c>) AND K to be call-visible
    /// (<c>IsCallVisibleDestination</c>: not a parameter, and a non-const class member — so
    /// <c>IsGlobal &amp;&amp; !IsConst</c> is true for it despite being read with no receiver).
    ///
    /// <para>⛔ MUTANT m2 (drop the call-visible arm entirely) kills this test: K is never marked
    /// written by anything else in K's own written-set contribution, K * 2 becomes "invariant".</para>
    /// </summary>
    [Test]
    public void L3_ClassFieldWrittenByBareCall_ModificationCountIsZero_AndTheProductStaysInTheLoop()
    {
        var (module, pass, changed) = RunLicmAfterStandardSettles(LicmKillVocabularyShapes.L3);
        var work = module.Functions.Single(f => f.Name == "Work" && !f.IsExternal);
        var body = work.Blocks.Single(b => b.Name == "for0.body");

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.False,
                "LICM hoisted something out of L3's loop — K is a class field written by the bare "
                + "call Inc() and must count as written even though it is read with no receiver");
            Assert.That(pass.ModificationCount, Is.Zero, "ModificationCount from the bare Run");
            Assert.That(body.Instructions.OfType<IRBinaryOp>().Any(op => ReferencesVariable(op, "K")), Is.True,
                "K * 2 (or its strength-reduced shift) must still be in for0.body, not hoisted to the preheader");
        });
    }

    /// <summary>
    /// L6 — the pass's own control. <c>x * y</c> has no call at all in the loop and nothing writes
    /// x or y: it is genuinely invariant, must still hoist (ModificationCount == 1), and must land
    /// in the preheader (<c>entry</c>, the loop's single-entry predecessor). Proves the fix does
    /// not disable LICM wholesale.
    /// </summary>
    [Test]
    public void L6_TrulyInvariantProduct_ModificationCountIsOne_AndTheProductLandsInThePreheader()
    {
        var (module, pass, changed) = RunLicmAfterStandardSettles(LicmKillVocabularyShapes.L6);
        var main = module.Functions.Single(f => f.Name == "Main" && !f.IsExternal);
        var entry = main.Blocks.Single(b => b.Name == "entry");

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True, "x * y has no call and nothing writes x or y — it must still hoist");
            Assert.That(pass.ModificationCount, Is.EqualTo(1), "exactly the one multiply should move");
            Assert.That(entry.Instructions.OfType<IRBinaryOp>().Any(op => op.Operation == BinaryOpKind.Mul), Is.True,
                "x * y should have moved to the preheader (entry)");
        });
    }

    /// <summary>
    /// The m3 (over-kill) discriminator — see <see cref="LicmKillVocabularyShapes.OverKillWitness"/>.
    /// A loop with a call AND a truly invariant product of two locals neither the call nor anything
    /// else touches: the fix must still hoist the product. An over-killing call-visible arm that
    /// treats every variable the loop READS as written whenever the loop contains any call would
    /// mark x and y "written" (both are read in the loop) and lose the hoist — no wrong value on
    /// any backend, a silently deleted optimization, hence a structural (ModificationCount) witness.
    ///
    /// <para>⛔ MUTANT m3 kills this test: ModificationCount goes from 1 to 0.</para>
    /// </summary>
    [Test]
    public void OverKillWitness_CallPlusTrulyInvariantProduct_StillHoists()
    {
        var (module, pass, changed) = RunLicmAfterStandardSettles(LicmKillVocabularyShapes.OverKillWitness);
        var main = module.Functions.Single(f => f.Name == "Main" && !f.IsExternal);
        var entry = main.Blocks.Single(b => b.Name == "entry");

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True,
                "x * y has no call touching x or y and neither is call-visible — a call ELSEWHERE "
                + "in the loop (Console.WriteLine(i)) must not, by itself, make every local written");
            Assert.That(pass.ModificationCount, Is.EqualTo(1));
            Assert.That(entry.Instructions.OfType<IRBinaryOp>().Any(op => op.Operation == BinaryOpKind.Mul), Is.True,
                "x * y should have moved to the preheader (entry) despite the call in the loop");
        });
    }
}

/// <summary>
/// Item 3 of the fixture brief — the controls that must STAY 12: L2's ByRef write goes through a
/// free function (an <c>IRCall</c>, always covered by the shared kill vocabulary, fixed or not) and
/// L4's write is a module global (always <c>IsGlobal</c>, always call-visible). Neither shape
/// exercises the gap this change closes; they exist to prove the fix does not regress what already
/// worked. Aggressive pipeline, all four backends where the shape allows (L2's JavaScript leg
/// refuses ByRef outright — BL7002 — matching L1's).
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class LicmKillVocabularyControlExecutionTests
{
    [Test]
    public void L2_FreeFunctionByRefWrite_AggressivePipeline_CSharpCppMsilAgree()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(LicmKillVocabularyShapes.L2))),
                Is.EqualTo(LicmKillVocabularyShapes.Expected12), "C++");
            Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(LicmKillVocabularyShapes.L2)),
                Is.EqualTo(LicmKillVocabularyShapes.Expected12), "MSIL");
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(LicmKillVocabularyShapes.L2)),
                Is.EqualTo(LicmKillVocabularyShapes.Expected12), "C#");
        });

    /// <summary>Structural, not a value assertion — JavaScript has no reference parameters at all
    /// and refuses ByRef at codegen regardless of what LICM decided. Matches
    /// <c>DestinationInvalidation_D4_ByRefExecutionTests.D4_JavaScript_RefusesByRef_BL7002</c>'s pattern.</summary>
    [Test]
    public void L2_JavaScript_RefusesByRef_BL7002()
    {
        var module = JsTestSupport.BuildModule(LicmKillVocabularyShapes.L2);
        var ex = Assert.Throws<ForeignFeatureException>(
            () => new JavaScriptCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("BL7002"));
        Assert.That(ex.Message, Does.Contain("ByRef"));
    }

    [Test]
    public void L4_ModuleGlobalWrittenByBareCall_AggressivePipeline_AllFourBackendsAgree()
        => FourBackends.RunsOnEveryBackendAggressive(LicmKillVocabularyShapes.L4, LicmKillVocabularyShapes.Expected12);
}

/// <summary>
/// Item 1 of the fixture brief — L1 and L3, the two shapes that MEASURED as silent wrong answers
/// (6 for 12) at 15b12e8, through BOTH shipping entry points: the aggressive pipeline
/// (<c>AggressivePipeline.Apply</c>, what <c>FourBackends.RunsOnEveryBackendAggressive</c> and its
/// per-backend legs run) and the CLI's own <c>--optimize</c> flag
/// (<c>CompilerOptions.OptimizeAggressive = true</c> through <c>BasicCompiler.CompileFile</c> —
/// <c>Program.cs:1087-1089</c>'s mapping, exercised in process the same way
/// <c>LoopPassesDisabledTests.BothCompilerEntryPoints_Aggressive_PrintTheCorrectValue</c> does).
///
/// <para>⚠ L1's C++/MSIL/C# legs and L3's four-backend legs are what actually pin the fix — before
/// it, L1 printed 6 on C++/MSIL and L3 printed 6 on C++/JavaScript/MSIL (see
/// <c>scratchpad/f111g/licm/matrix-merged.txt</c> vs <c>matrix-step14.txt</c>). L1's JavaScript leg
/// is excluded from the value assertion: <c>b.Bump(x)</c> is a ByRef instance-method argument and
/// JavaScript refuses ByRef outright (BL7002), unaffected by LICM either way.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class LicmKillVocabularyExecutionTests
{
    /// <summary>
    /// The CLI single-file entry point (<c>BasicCompiler.CompileFile</c> with
    /// <c>OptimizeAggressive = true</c> — what <c>BasicLang.exe prog.bas --target=… --optimize</c>
    /// runs) for <paramref name="source"/>. A fresh compile per call, deliberately: the caller
    /// generates from the returned module through exactly one backend, matching
    /// <c>LoopInvariantCodeMotionRunTests</c>' per-backend-fresh-module idiom rather than reusing
    /// one module across several code generators.
    /// </summary>
    private static IRModule CliOptimizeModule(string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "BasicLang_LicmCliOptimize_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "prog.bas");
            File.WriteAllText(file, source);

            var compiler = new BasicCompiler(new CompilerOptions { OptimizeAggressive = true });
            var result = compiler.CompileFile(file);

            Assert.That(result.HasErrors, Is.False, string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            Assert.That(result.CombinedIR, Is.Not.Null, "the entry point produced no combined IR");
            return result.CombinedIR;
        }
        finally { try { Directory.Delete(dir, true); } catch { /* temp */ } }
    }

    // ---- L1 -------------------------------------------------------------------------------

    [Test]
    public void L1_AggressivePipeline_CSharpCppMsilAgree()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(LicmKillVocabularyShapes.L1))),
                Is.EqualTo(LicmKillVocabularyShapes.Expected12), "C++, aggressive pipeline");
            Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(LicmKillVocabularyShapes.L1)),
                Is.EqualTo(LicmKillVocabularyShapes.Expected12), "MSIL, aggressive pipeline");
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(LicmKillVocabularyShapes.L1)),
                Is.EqualTo(LicmKillVocabularyShapes.Expected12), "C#, aggressive pipeline");
        });

    [Test]
    public void L1_JavaScript_RefusesByRef_BL7002()
    {
        var module = JsTestSupport.BuildModule(LicmKillVocabularyShapes.L1);
        var ex = Assert.Throws<ForeignFeatureException>(
            () => new JavaScriptCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("BL7002"));
    }

    [Test]
    public void L1_CliOptimize_CSharpCppMsilAgree()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(
                    new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
                        .Generate(CliOptimizeModule(LicmKillVocabularyShapes.L1)))),
                Is.EqualTo(LicmKillVocabularyShapes.Expected12), "C++ via the CLI's --optimize entry point");
            Assert.That(FourBackends.Norm(MsilHarness.RunIlExpectingSuccess(
                    new MSILCodeGenerator().Generate(CliOptimizeModule(LicmKillVocabularyShapes.L1)))),
                Is.EqualTo(LicmKillVocabularyShapes.Expected12), "MSIL via the CLI's --optimize entry point");
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpText(
                    new ImprovedCSharpCodeGenerator().Generate(CliOptimizeModule(LicmKillVocabularyShapes.L1)))),
                Is.EqualTo(LicmKillVocabularyShapes.Expected12), "C# via the CLI's --optimize entry point");
        });

    /// <summary>
    /// The ONE Release-.blproj leg the brief asks for (task #134's note: a C++ Release build takes
    /// the STANDARD pipeline and cannot see this regression at all — MSIL's Release build DOES take
    /// the aggressive one, so it is the backend that can actually stand in for "a Release build" here).
    /// Drives the real deployed <c>BasicLang.exe build App.blproj -c Release</c>, matching
    /// <c>MsilBinaryOperandCoercionTests.BuildReleaseMsilAndRun</c>'s pattern.
    /// </summary>
    [Test]
    public void L1_ReleaseBlprojBuild_Msil_PrintsTheCorrectValue()
    {
        var dir = Path.Combine(Path.GetTempPath(), "BasicLang_LicmL1Release_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Main.bas"), LicmKillVocabularyShapes.L1);
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
                Is.EqualTo(LicmKillVocabularyShapes.Expected12),
                "task #134 — a C++ Release .blproj runs the STANDARD pipeline (no LICM) and cannot "
                + "detect this regression at all; MSIL's Release .blproj DOES take the aggressive "
                + "pipeline (Program.cs:502), which is why this leg is MSIL rather than C++.");
        }
        finally { try { Directory.Delete(dir, true); } catch { /* temp */ } }
    }

    // ---- L3 -------------------------------------------------------------------------------

    [Test]
    public void L3_AggressivePipeline_AllFourBackendsAgree()
        => FourBackends.RunsOnEveryBackendAggressive(LicmKillVocabularyShapes.L3, LicmKillVocabularyShapes.Expected12);

    [Test]
    public void L3_CliOptimize_AllFourBackendsAgree()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(
                    new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
                        .Generate(CliOptimizeModule(LicmKillVocabularyShapes.L3)))),
                Is.EqualTo(LicmKillVocabularyShapes.Expected12), "C++ via the CLI's --optimize entry point");
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(
                    new JavaScriptCodeGenerator().Generate(CliOptimizeModule(LicmKillVocabularyShapes.L3)))),
                Is.EqualTo(LicmKillVocabularyShapes.Expected12), "JavaScript via the CLI's --optimize entry point");
            Assert.That(FourBackends.Norm(MsilHarness.RunIlExpectingSuccess(
                    new MSILCodeGenerator().Generate(CliOptimizeModule(LicmKillVocabularyShapes.L3)))),
                Is.EqualTo(LicmKillVocabularyShapes.Expected12), "MSIL via the CLI's --optimize entry point");
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpText(
                    new ImprovedCSharpCodeGenerator().Generate(CliOptimizeModule(LicmKillVocabularyShapes.L3)))),
                Is.EqualTo(LicmKillVocabularyShapes.Expected12), "C# via the CLI's --optimize entry point");
        });
}

/// <summary>
/// Item 4 of the fixture brief — L5 (a local captured BY REFERENCE and written inside a lambda)
/// stays KNOWN-WRONG. Task #122: the shared kill vocabulary's own documented gap
/// (<c>OptimizationPass.NamesWrittenBy</c>'s doc comment) — a lambda capture needs a capture set,
/// which neither <c>NamesWrittenBy</c> nor this LICM fix add. These pins measure the WRONG outputs
/// on purpose (matching <c>CseDestinationKnownGapsTask133Tests</c>' idiom) so a future fix to #122
/// changes these tests loudly instead of being silently absorbed. Correct is <c>seed\n12</c>
/// everywhere; C# alone gets it right today and is not pinned here for that reason.
///
/// <para>⛔ NOT caught by <c>JsExecutionTierRosterTests</c>' name-based auto-discovery (matches
/// neither "JavaScript"/"Js" nor "*ExecutionTests" — same reason
/// <c>CseDestinationKnownGapsTask133Tests</c> needed a manual roster entry), even though the JS pin
/// below spawns Node. Added to the roster by hand.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg (not exercised here, but the fixture pattern) redirects Console.Out
public class LicmKillVocabularyKnownGapsTask122Tests
{
    [Test]
    public void L5_LambdaCapturedLocal_JavaScript_AggressivePipeline_PinnedForTask122()
        => Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(LicmKillVocabularyShapes.L5)),
            Is.EqualTo("seed\n6"),
            "task #122 — a local captured by reference and written inside a lambda has no capture "
            + "set. Correct is seed\\n12; if this changed, #122 may be closed (or the defect moved) "
            + "— re-measure and update or delete this pin, do not just widen it.");

    [Test]
    public void L5_LambdaCapturedLocal_Cpp_StandardPipeline_PinnedForTask122()
        => Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(LicmKillVocabularyShapes.L5))),
            Is.EqualTo("seed\n6"),
            "task #122 — pre-existing lambda-capture defect, present under the STANDARD pipeline "
            + "too (ConstantFolding + CopyPropagation fold across the capture on their own), so "
            + "LICM is not the cause and closing this LICM change cannot close #122 by itself.");

    [Test]
    public void L5_LambdaCapturedLocal_Cpp_AggressivePipeline_PinnedForTask122()
        => Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(LicmKillVocabularyShapes.L5))),
            Is.EqualTo("seed\n6"),
            "task #122 — same defect, aggressive pipeline: wrong \"at every level\" (both pins here "
            + "agree with each other, not just with the standard-pipeline one above).");

    [Test]
    public void L5_LambdaCapturedLocal_Msil_CannotBuild_PinnedForTask122()
    {
        var run = MsilHarness.Run(LicmKillVocabularyShapes.L5, aggressive: true);
        Assert.That(run.Outcome, Is.EqualTo(MsilHarness.MsilOutcome.AssembleFailed), run.Report);
        Assert.That(run.Detail, Does.Contain("Action"),
            "task #122 — MSIL has no lowering for the delegate type a Sub() lambda gets typed as "
            + "(ilasm: \"Reference to undefined class 'Action'\"), a pre-existing gap unrelated to LICM.");
    }
}
