using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #123 — the repo's sample programs (Samples/Pong, Samples/SpaceShooter, Samples/Platformer) are the closest thing this
//  repo has to real user code. Two of them did not parse (an untyped `Const`, VB's `If(cond, a, b)`) and, once they parsed,
//  they were not valid VB either (undeclared `KEY_*`, RaylibWrapper's `Framework_*` names, implicit Double→Single narrowing).
//  The owner's ruling was "fix the compiler AND the samples"; both samples now compile with NO diagnostic of any severity.
//
//  ⛔ WHAT THESE ROWS HOLD, AND WHAT THEY DO NOT. Both samples build under the mutant that types every untyped Const as Integer
//  (M1int), so "it builds" holds nothing about D1; the sample rows therefore also pin the C# DECLARATION each constant gets
//  (`const double PADDLE_SPEED`, not `const int`), which is where M1int shows. D1 and D2 themselves are held by the probes in
//  UntypedConstTests / ConditionalExpressionTests / UntypedConstAndConditionalExecutionTests.
//
//  ⛔ STEPS THAT ARE NOT ASSERTED, AND WHY (each measured on this base; the samples' C++ is the existing Platformer's story too):
//    C++        both samples GENERATE C++ (asserted), but `clang++ -fsyntax-only` rejects the output, before #123's edits as well for
//               Samples/Platformer and SampleGames/*: `redefinition of 'hitPos'` (a Dim in two branches) and a non-const lvalue
//               reference to an `Array<bool>` element. Pre-existing C++ backend defects; no task.
//    JavaScript and MSIL  refuse every program that calls the engine (`GameInit`): "no lowering for 'GameInit'" / "MSIL: 'GameInit' is
//               called, but it is neither a procedure this program declares nor a delegate value". By design — the engine is C#/C++.
//    RUNNING     a sample opens a window. Nothing here runs one; the two Samples/Pong shapes that decide its behaviour run as the
//               probe i12pong in the execution fixture.
//    vbc         the BasicLang engine API names (`GameInit`, `IsKeyDown`, `DrawRectangle`, …) exist only in BasicLang, so vbc cannot
//               check a sample end to end; it accepted both edited samples against a stub module declaring FrameworkStdLib's signatures.
// ================================================================================================

internal static class SampleSources
{
    internal static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "VisualGameStudioEngine.sln")))
                return d.FullName;
        throw new DirectoryNotFoundException("VisualGameStudioEngine.sln not found above " + AppContext.BaseDirectory);
    }

    internal static string PathOf(string sample) => Path.Combine(RepoRoot(), "Samples", sample, "Main.bas");

    /// <summary>The constant declarations each sample's C# must carry, with the type D1 gives the initializer the source spells.</summary>
    internal static readonly Dictionary<string, (string Name, string CSharpType)[]> Constants = new()
    {
        ["Pong"] = new[]
        {
            ("SCREEN_WIDTH", "int"), ("SCREEN_HEIGHT", "int"), ("PADDLE_WIDTH", "int"), ("PADDLE_HEIGHT", "int"), ("BALL_SIZE", "int"),
            ("PADDLE_SPEED", "double"),   // Const PADDLE_SPEED = 400.0     (untyped, Double)
            ("BALL_SPEED", "float"),      // Const BALL_SPEED As Single = 350.0   (typed: the module initializer below needs it)
            ("KEY_SPACE", "int"), ("KEY_UP", "int"),
        },
        ["SpaceShooter"] = new[]
        {
            ("SCREEN_WIDTH", "int"), ("SCREEN_HEIGHT", "int"), ("MAX_BULLETS", "int"), ("MAX_ENEMIES", "int"),
            ("PLAYER_SPEED", "double"), ("BULLET_SPEED", "double"), ("ENEMY_SPEED", "double"),   // Const X = 300.0 (untyped, Double)
            ("KEY_SPACE", "int"), ("KEY_UP", "int"),
        },
        ["Platformer"] = new[] { ("SCREEN_WIDTH", "int"), ("GRAVITY", "int"), ("TILE_SIZE", "int"), ("KEY_SPACE", "int") },
    };
}

/// <summary>The samples through the front end and the IR, in process — fast.</summary>
[TestFixture]
public class SampleProgramFrontEndTests
{
    private static readonly string[] Samples = { "Pong", "SpaceShooter", "Platformer" };

    private static IEnumerable<TestCaseData> EverySample() => Samples.Select(s => new TestCaseData(s).SetName(s));

    private static IEnumerable<TestCaseData> EverySampleByRoute()
        => from sample in Samples
           from route in new[] { "CompileFile", "CompileProjectFiles", "CompileProjectFilesAggressive" }
           select new TestCaseData(sample, route).SetName($"{sample}_{route}");

    private static CompilationResult Compile(string sample, string route)
    {
        var path = SampleSources.PathOf(sample);
        var compiler = new BasicCompiler(new CompilerOptions { OptimizeAggressive = route.EndsWith("Aggressive", StringComparison.Ordinal) });
        return route == "CompileFile" ? compiler.CompileFile(path) : compiler.CompileProjectFiles(new List<string> { path });
    }

    /// <summary>The roster is pinned (this is the fixture's plain <c>[Test]</c>), and the three sources are the files these rows read.</summary>
    [Test]
    public void TheThreeSamples_AreThereToBeRead()
    {
        Assert.Multiple(() =>
        {
            foreach (var sample in Samples)
            {
                Assert.That(File.Exists(SampleSources.PathOf(sample)), Is.True, sample);
                Assert.That(SampleSources.Constants.ContainsKey(sample), Is.True, sample);
            }
            Assert.That(File.ReadAllText(SampleSources.PathOf("Pong")), Does.Contain("Const PADDLE_SPEED = 400.0").And.Contain("If(ballVX > 0, -1, 1)"),
                "Samples/Pong keeps its untyped Const and its If() — they are what D1 and D2 exist for");
            Assert.That(File.ReadAllText(SampleSources.PathOf("SpaceShooter")), Does.Contain("Const PLAYER_SPEED = 300.0"));
        });
    }

    /// <summary>
    /// No diagnostic of ANY severity: the parser, the analyzer's errors and its warnings are all empty. The CSE corpus fixture
    /// builds IR past a rejected front end on purpose, so this is the row that says "the sample is accepted", not merely built.
    /// </summary>
    [TestCaseSource(nameof(EverySample))]
    public void TheSample_HasNoFrontEndDiagnostic(string sample)
    {
        var run = T123Front.Run(File.ReadAllText(SampleSources.PathOf(sample)));

        Assert.Multiple(() =>
        {
            Assert.That(run.ParseErrors, Is.Empty, "parse");
            Assert.That(run.Analyzer.Errors.Select(e => $"{e.Severity}: {e.Message.Split('\n')[0]}"), Is.Empty, "semantic analysis, errors and warnings");
            Assert.That(run.Analyzed, Is.True);
        });
    }

    /// <summary>
    /// The sample reaches the IR through the CLI's single-file route and through <c>CompileProjectFiles</c> (what the IDE and a
    /// `build` call), with the standard and the aggressive passes: no error, a combined module with a Main.
    /// </summary>
    [TestCaseSource(nameof(EverySampleByRoute))]
    public void TheSample_BuildsIR_ThroughEveryRoute(string sample, string route)
    {
        var result = Compile(sample, route);

        Assert.Multiple(() =>
        {
            Assert.That(result.AllErrors.Select(e => e.Message.Split('\n')[0]), Is.Empty);
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.CombinedIR, Is.Not.Null);
            Assert.That(result.CombinedIR.Functions.Select(f => f.Name), Does.Contain("Main"));
        });
    }

    /// <summary>
    /// The sample generates C#, through every route, and each constant is declared with the CLR type D1 gives its initializer:
    /// `const int SCREEN_WIDTH`, `const double PADDLE_SPEED` (the untyped `400.0`), `const float BALL_SPEED`. A constant typed Integer
    /// whatever its literal (mutant M1int) builds and is caught here.
    /// </summary>
    [TestCaseSource(nameof(EverySampleByRoute))]
    public void TheSample_GeneratesCSharp_WithEachConstantItsLiteralsType(string sample, string route)
    {
        var csharp = new ImprovedCSharpCodeGenerator().Generate(Compile(sample, route).CombinedIR);

        Assert.Multiple(() =>
        {
            Assert.That(csharp, Does.Contain("FrameworkWrapper.Framework_Initialize("), "GameInit is the engine's Framework_Initialize");
            foreach (var (name, type) in SampleSources.Constants[sample])
                Assert.That(csharp, Does.Match($@"\bconst {type} {name} = "), $"{sample}: Const {name} should be `{type}`");
        });
    }

    /// <summary>
    /// What the fixes the samples forced buy, visible in their C#: Pong's module initializer over a Const folds
    /// (`ballVY = 175.0f`: <c>SubstituteFoldedConstGlobals</c> and the Integer→Single fold), and SpaceShooter's DrawTriangle call converts
    /// its Single positions (`Convert.ToInt32(…)`; without the analyzer's mirror C# rejects it with CS1503).
    /// </summary>
    [Test]
    public void TheFixesTheSamplesForced_AreVisibleInTheirCSharp([Values("CompileFile", "CompileProjectFilesAggressive")] string route)
    {
        var pong = new ImprovedCSharpCodeGenerator().Generate(Compile("Pong", route).CombinedIR);
        var shooter = new ImprovedCSharpCodeGenerator().Generate(Compile("SpaceShooter", route).CombinedIR);

        Assert.Multiple(() =>
        {
            Assert.That(pong, Does.Match(@"static float ballVY = 175(\.0)?f;"), "Dim ballVY As Single = BALL_SPEED / 2 folds");
            Assert.That(shooter, Does.Contain("Framework_DrawTriangle(Convert.ToInt32("));
            Assert.That(shooter, Does.Not.Match(@"Framework_DrawTriangle\(\w+,"), "no position is passed raw");
        });
    }

    /// <summary>
    /// The samples generate C++ too. Not compiled: the C++ output of every engine sample (Platformer included) is rejected by clang for
    /// reasons that predate #123 (see the header), so only the generation is asserted.
    /// </summary>
    [TestCaseSource(nameof(EverySampleByRoute))]
    public void TheSample_GeneratesCpp(string sample, string route)
    {
        var cpp = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false }).Generate(Compile(sample, route).CombinedIR);

        Assert.That(cpp, Does.Contain("Framework_Initialize("), "GameInit is the engine's Framework_Initialize");
    }
}

/// <summary>
/// The samples through the real tools — spawned processes, Roslyn against the engine's managed wrapper, and a real `dotnet build`.
/// ⚠ Every spawn works on a COPY: the CLI writes <c>Main.cs</c> / <c>Main.cpp</c> next to its input, and a sample must not collect
/// generated files in the repo.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class SampleProgramBuildTests
{
    private static string CopyOf(string sample)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-t123-sample-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.Copy(SampleSources.PathOf(sample), Path.Combine(dir, "Main.bas"));
        return dir;
    }

    private static void Clean(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* temp */ }
    }

    /// <summary>The CLI on a copy: the generated text, asserting exit 0 and that the file was written NEXT TO THE COPY.</summary>
    private static string CliGenerate(string sample, string target, bool optimize)
    {
        var dir = CopyOf(sample);
        try
        {
            var args = new List<string> { "Main.bas", "--target=" + target };
            if (optimize) args.Add("--optimize");
            var (exit, stdout, stderr) = CliTestHarness.RunProcess(CliTestHarness.CliPath(), args.ToArray(), dir, timeoutMs: 120_000);
            var output = Path.Combine(dir, "Main" + (target == "csharp" ? ".cs" : ".cpp"));

            Assert.That(exit, Is.EqualTo(0), $"BasicLang {string.Join(" ", args)} failed:\n{stdout}{stderr}");
            Assert.That(stdout + stderr, Does.Not.Contain("Warning at"), "no diagnostic of any severity");
            Assert.That(File.Exists(output), Is.True, $"the CLI wrote no {Path.GetFileName(output)}:\n{stdout}{stderr}");
            return File.ReadAllText(output);
        }
        finally { Clean(dir); }
    }

    private static string[] RoslynErrors(string csharp)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => a.Location)
            .Append(Path.Combine(AppContext.BaseDirectory, "RaylibWrapper.dll"))
            .Append(typeof(Microsoft.VisualBasic.Strings).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(l => (MetadataReference)MetadataReference.CreateFromFile(l))
            .ToImmutableArray();
        var compilation = CSharpCompilation.Create(
            "T123Sample_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(csharp) },
            references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        using var ms = new MemoryStream();
        return compilation.Emit(ms).Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()).ToArray();
    }

    /// <summary>The managed wrapper the samples' C# is built against is deployed next to the tests (the harness's precondition).</summary>
    [Test]
    public void TheEnginesManagedWrapper_IsDeployedForTheBuild()
        => Assert.That(File.Exists(Path.Combine(AppContext.BaseDirectory, "RaylibWrapper.dll")), Is.True,
            "RaylibWrapper.dll is referenced by BasicLang.csproj and must be copied next to the tests");

    /// <summary>
    /// The CLI, on a copy, generates C# and C++ for each sample, standard and with --optimize, exit 0 and no diagnostic. The C#
    /// carries each constant's D1 type.
    /// </summary>
    [Test]
    public void TheSample_GeneratesThroughTheCli_FromACopy([ValueSource(nameof(Names))] string sample, [Values("csharp", "cpp")] string target, [Values] bool optimize)
    {
        var text = CliGenerate(sample, target, optimize);

        if (target == "csharp")
            foreach (var (name, type) in SampleSources.Constants[sample])
                Assert.That(text, Does.Match($@"\bconst {type} {name} = "), $"{sample}: Const {name} should be `{type}`");
        else
            Assert.That(text, Does.Contain("Framework_Initialize("));

        foreach (var generated in new[] { "Main.cs", "Main.cpp" })
            Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(SampleSources.PathOf(sample))!, generated)), Is.False,
                $"{generated} appeared next to the repo's sample: something compiled it in place");
    }

    private static readonly string[] Names = { "Pong", "SpaceShooter", "Platformer" };

    /// <summary>
    /// The generated C# BUILDS against RaylibWrapper, from the CLI's single-file output (standard and --optimize) and from
    /// <c>CompileProjectFiles</c>: Roslyn, zero errors. Without the DrawTriangle mirror SpaceShooter fails here with CS1503.
    /// </summary>
    [Test]
    public void TheSamplesCSharp_CompilesAgainstRaylibWrapper([ValueSource(nameof(Names))] string sample, [Values("cli", "cli-optimize", "project")] string route)
    {
        var csharp = route switch
        {
            "cli" => CliGenerate(sample, "csharp", optimize: false),
            "cli-optimize" => CliGenerate(sample, "csharp", optimize: true),
            _ => new ImprovedCSharpCodeGenerator().Generate(
                new BasicCompiler(new CompilerOptions { OptimizeAggressive = true }).CompileProjectFiles(new List<string> { SampleSources.PathOf(sample) }).CombinedIR),
        };

        Assert.That(RoslynErrors(csharp), Is.Empty, $"{sample} via {route}: the emitted C# does not build against RaylibWrapper");
    }

    /// <summary>
    /// `BasicLang build` of a project holding the sample: the CLI's project route runs `dotnet build` on the generated project,
    /// referencing the wrapper the compiler auto-injects. Release (aggressive passes).
    /// </summary>
    [Test]
    public void TheSample_BuildsAsAProject_ThroughTheCli([ValueSource(nameof(Names))] string sample)
    {
        if (!CliTestHarness.DotnetOnPath())
            Assert.Ignore("dotnet SDK not found on PATH — the CLI's C# backend cannot build the generated project.");

        var dir = CopyOf(sample);
        try
        {
            File.WriteAllText(Path.Combine(dir, "P.blproj"),
                "<BasicLangProject>\n  <PropertyGroup><ProjectName>P</ProjectName><AssemblyName>P</AssemblyName><OutputType>Exe</OutputType><TargetBackend>CSharp</TargetBackend></PropertyGroup>\n</BasicLangProject>\n");
            var (exit, stdout, stderr) = CliTestHarness.RunProcess(CliTestHarness.CliPath(), new[] { "build", "P.blproj", "-c", "Release" }, dir, timeoutMs: 300_000);

            Assert.That(exit, Is.EqualTo(0), $"BasicLang build failed:\n{stdout}{stderr}");
            Assert.That(Directory.GetFiles(dir, "P.dll", SearchOption.AllDirectories), Is.Not.Empty, $"`dotnet build` produced no P.dll:\n{stdout}");
        }
        finally { Clean(dir); }
    }
}
