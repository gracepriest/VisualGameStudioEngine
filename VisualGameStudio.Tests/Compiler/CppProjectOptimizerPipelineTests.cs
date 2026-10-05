using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// ⭐ TASK #134 — A C++ <c>.blproj</c> BUILT IN RELEASE RUNS THE AGGRESSIVE OPTIMIZER PIPELINE.
///
/// <para><c>CppProjectBuilder.EmitCore</c> used to build its <c>CompilerOptions</c> without
/// <c>OptimizeAggressive</c>, so a C++ project took <c>AddStandardPasses()</c> in EVERY
/// configuration while C#, JavaScript and MSIL took <c>AddAggressivePasses()</c> in Release. It
/// is one line, <c>OptimizeAggressive = project.OptimizationsEnabledFor(configuration)</c>, and
/// nothing in the suite could tell it from its absence: every C++ execution leg ran a program
/// through <c>BclE2E.CompileToCppOptimized</c> (standard) or <c>CompileToCppAggressive</c> (called
/// directly), never through a project.</para>
///
/// <para><b>The witness is <c>obj/gen</c>.</b> A BasicLang native build always needs MSVC, so off
/// Windows the builder runs the whole front end and code generator, writes <c>obj/gen</c>, and
/// then fails BL6015 at the toolchain gate (<see cref="CppProjectProbe"/>). Nothing here compiles
/// or runs C++; these are fast. The execution tier is <c>CppReleaseProjectExecutionTests</c>.</para>
///
/// <para><b>The oracle is the pipelines, not a string.</b> The expected code is produced in
/// process by <c>BclE2E.CompileToCppAggressive</c> / <c>CompileToCppOptimized</c> and compared as
/// a line multiset (<see cref="CppPipelineLines"/>), so the contract under test is "a Release
/// project's code IS the aggressive pipeline's code", whatever that code is today. Each program is
/// asserted to be one where the two pipelines DIFFER, so a pipeline change that made them agree
/// fails loudly instead of turning every comparison here into a tautology.</para>
///
/// <para>Mutants of the line at <c>CppProjectBuilder.cs</c> ~542 this fixture kills (measured):
/// M1 the line removed (always standard), M2 <c>= true</c> (always aggressive), M3
/// <c>OptimizationsEnabledFor("Debug")</c> (asks about the wrong configuration), M5
/// <c>configuration == "Release"</c> (ignores a configuration's own <c>&lt;Optimize&gt;</c>); and, on the
/// <c>-O2</c> request at ~934, <c>Optimize = false</c> / <c>true</c> / the project-wide flag. M4
/// (<c>!forIntelliSense &amp;&amp; …</c>, the IntelliSense headers left standard) is killed by the
/// <see cref="CppProjectRoute.IntelliSense"/> cases.</para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class CppProjectOptimizerPipelineTests
{
    private readonly List<CppProjectProbe> _probes = new();

    private CppProjectProbe Project(string source, string configurationGroups = "")
    {
        var probe = new CppProjectProbe(source, configurationGroups);
        _probes.Add(probe);
        return probe;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var p in _probes) p.Dispose();
        _probes.Clear();
    }

    // The routes a test can run with no process at all: the builder up to its toolchain gate, the
    // toolchain-free IntelliSense emitter, and the IDE service that calls it on project open/save
    // (what actually fires in a shipping IDE). The IDE's BuildService and the CLI are in
    // CppProjectOptimizerPipelineRouteTests (Integration: they spawn).
    private static IEnumerable<TestCaseData> RouteAndProgram()
    {
        foreach (var route in new[] { CppProjectRoute.BuildApi, CppProjectRoute.IntelliSense, CppProjectRoute.IntelliSenseService })
            foreach (TestCaseData program in CppPipelinePrograms.Discriminating())
            {
                var p = (CppPipelineProgram)program.Arguments[0]!;
                yield return new TestCaseData(route, p).SetArgDisplayNames(route.ToString(), p.Name);
            }
    }

    // ==================================================================================
    // 0. The probes are worth comparing: the two pipelines write DIFFERENT code for each.
    // ==================================================================================

    [TestCaseSource(typeof(CppPipelinePrograms), nameof(CppPipelinePrograms.Discriminating))]
    public void TheTwoPipelines_WriteDifferentCode_ForEveryProbe(CppPipelineProgram program)
    {
        var aggressive = CppPipelineLines.Aggressive(program.Source);
        var standard = CppPipelineLines.Standard(program.Source);
        var delta = CppPipelineLines.Delta(aggressive, standard);

        Assert.That(delta, Is.Not.Empty,
            $"{program.Name}: AddAggressivePasses and AddStandardPasses wrote the same C++ — this probe no longer tells "
            + "the two pipelines apart, so every comparison built on it would pass on a mutant.");
    }

    // ==================================================================================
    // 1. THE CONTRACT, per route: Release = aggressive, Debug = standard.
    //    Kills M1 (Release left standard), M2 (Debug made aggressive), M3, and — on the
    //    IntelliSense route — M4.
    // ==================================================================================

    [TestCaseSource(nameof(RouteAndProgram))]
    public void Release_RunsTheAggressivePipeline(CppProjectRoute route, CppPipelineProgram program)
    {
        var emission = Project(program.Source).Emit(route, "Release");

        AssertCodeIs(emission, CppPipelineLines.Aggressive(program.Source), CppPipelineLines.Standard(program.Source),
            "AGGRESSIVE", "a Release C++ project");
    }

    [TestCaseSource(nameof(RouteAndProgram))]
    public void Debug_RunsTheStandardPipeline_NeverTheAggressiveOne(CppProjectRoute route, CppPipelineProgram program)
    {
        var emission = Project(program.Source).Emit(route, "Debug");

        AssertCodeIs(emission, CppPipelineLines.Standard(program.Source), CppPipelineLines.Aggressive(program.Source),
            "STANDARD", "a Debug C++ project");
    }

    /// <summary>
    /// The IntelliSense headers hold the code the build compiles (the reason the line lives in
    /// <c>EmitCore</c> and not in <c>Build</c>): a clangd that read standard-pipeline headers while
    /// the build compiled the aggressive ones would show the user code that is not in their program.
    /// </summary>
    [TestCaseSource(typeof(CppPipelinePrograms), nameof(CppPipelinePrograms.Discriminating))]
    public void IntelliSenseRelease_HoldsExactlyTheCodeTheReleaseBuildCompiles(CppPipelineProgram program)
    {
        var project = Project(program.Source);

        var built = project.Emit(CppProjectRoute.BuildApi, "Release");
        var intelliSense = project.Emit(CppProjectRoute.IntelliSense, "Release");

        Assert.Multiple(() =>
        {
            Assert.That(intelliSense.Files.Keys.OrderBy(k => k), Is.EqualTo(built.Files.Keys.OrderBy(k => k)),
                "the same generated files");
            foreach (var (name, text) in built.Files)
                Assert.That(intelliSense.Files[name], Is.EqualTo(text), $"{name}: IntelliSense differs from the build");
        });
    }

    // The same for Debug, where the two differ by #line directives only (a Debug BUILD writes
    // them, IntelliSense never does: clangd would map its diagnostics onto the .bas lines).
    [TestCaseSource(typeof(CppPipelinePrograms), nameof(CppPipelinePrograms.Discriminating))]
    public void IntelliSenseDebug_HoldsTheCodeTheDebugBuildCompiles_ModuloLineDirectives(CppPipelineProgram program)
    {
        var project = Project(program.Source);

        var built = project.Emit(CppProjectRoute.BuildApi, "Debug");
        var intelliSense = project.Emit(CppProjectRoute.IntelliSense, "Debug");

        Assert.That(CppPipelineLines.Delta(CppPipelineLines.Of(intelliSense), CppPipelineLines.Of(built)), Is.Empty,
            "the statements are the same; only #line lines (which the multiset drops) may differ");
    }

    private static void AssertCodeIs(CppProjectEmission emission,
        SortedDictionary<string, int> expected, SortedDictionary<string, int> notExpected,
        string pipeline, string what)
    {
        var actual = CppPipelineLines.Of(emission);
        var missing = CppPipelineLines.Delta(expected, actual);
        Assert.That(missing, Is.Empty,
            $"{emission.Summary}: {what} must emit the {pipeline} pipeline's code. "
            + $"(+ = the {pipeline} pipeline writes it and the project did not, − = the project wrote it and the pipeline did not)\n"
            + CppPipelineLines.Describe(missing));
        Assert.That(CppPipelineLines.Delta(actual, notExpected), Is.Not.Empty,
            $"{emission.Summary}: the code is ALSO the other pipeline's — the probe cannot tell them apart.");
    }

    // ==================================================================================
    // 2. Configuration SHAPES. One question per shape: aggressive or standard?
    //    ProjectFile.OptimizationsEnabledFor decides, and the same answer must reach the
    //    IR pipeline AND the -O2 request. The anchor program is x_alg (`2 * x` -> `x + x`).
    // ==================================================================================

    // (name, extra PropertyGroups, configuration built, expected: aggressive?)
    private static IEnumerable<TestCaseData> ConfigurationShapes()
    {
        const bool Aggressive = true, Standard = false;
        TestCaseData Row(string name, string groups, string cfg, bool aggressive) =>
            new TestCaseData(groups, cfg, aggressive).SetArgDisplayNames(name);

        // The default project: Debug and Release exactly as ProjectFile declares them.
        yield return Row("default project, Release", "", "Release", Aggressive);
        yield return Row("default project, Debug", "", "Debug", Standard);
        // A configuration the project does not declare does not optimize...
        yield return Row("default project, undeclared Foo", "", "Foo", Standard);
        // ...and the dictionary's lookup is CASE-SENSITIVE: `-c release` is not `Release`. Pinned as
        // implemented (ProjectFile.OptimizationsEnabledFor's own comment says so); the managed
        // route's CLI build asks the same question, so the two CLI routes agree. The IDE's
        // MANAGED build reads its own model (VisualGameStudio.Core's BuildConfiguration.Optimize),
        // which differs — a follow-up, not this ticket.
        yield return Row("default project, lower-case release", "", "release", Standard);

        // A declared configuration decides for itself, whatever its name is. (M5 fails here.)
        yield return Row("custom Profile, Optimize=true", CppProjectProbe.Configuration("Profile", optimize: true), "Profile", Aggressive);
        yield return Row("custom Profile, Optimize=false", CppProjectProbe.Configuration("Profile", optimize: false), "Profile", Standard);
        // A declared configuration REPLACES the default one of its name: a Release that says nothing
        // about <Optimize> is not optimizing (the BuildConfiguration default is false).
        yield return Row("Release redeclared without Optimize", CppProjectProbe.Configuration("Release", optimize: null, debugSymbols: false), "Release", Standard);

        // Debug and Release are not special: each follows its OWN <Optimize>. (M2 fails the first,
        // M3 and M1 the second.)
        yield return Row("Debug with Optimize=true", CppProjectProbe.Configuration("Debug", optimize: true), "Debug", Aggressive);
        yield return Row("Release with Optimize=false", CppProjectProbe.Configuration("Release", optimize: false), "Release", Standard);
        // M3 asks about "Debug" whatever was built: a Debug that optimizes beside a Release that does not.
        var swapped = CppProjectProbe.Configuration("Debug", optimize: true) + CppProjectProbe.Configuration("Release", optimize: false);
        yield return Row("swapped: Release (Optimize=false) beside Debug (Optimize=true), Release", swapped, "Release", Standard);
        yield return Row("swapped: Release (Optimize=false) beside Debug (Optimize=true), Debug", swapped, "Debug", Aggressive);

        // Optimization is the <Optimize> element and nothing else (not debug symbols).
        yield return Row("Release with DebugSymbols=true", CppProjectProbe.Configuration("Release", optimize: true, debugSymbols: true), "Release", Aggressive);
        yield return Row("Debug with DebugSymbols=false", CppProjectProbe.Configuration("Debug", optimize: false, debugSymbols: false), "Debug", Standard);
    }

    /// <summary>The anchor program: <c>2 * x</c> becomes <c>x + x</c> and the constants fold only under the aggressive passes.</summary>
    private static CppPipelineProgram Anchor => CppPipelinePrograms.Alg;

    [TestCaseSource(nameof(ConfigurationShapes))]
    public void BuildRoute_ConfigurationShape_PicksThePipeline(string groups, string configuration, bool aggressive)
    {
        var emission = Project(Anchor.Source, groups).Emit(CppProjectRoute.BuildApi, configuration);

        if (aggressive)
            AssertCodeIs(emission, CppPipelineLines.Aggressive(Anchor.Source), CppPipelineLines.Standard(Anchor.Source),
                "AGGRESSIVE", $"configuration '{configuration}'");
        else
            AssertCodeIs(emission, CppPipelineLines.Standard(Anchor.Source), CppPipelineLines.Aggressive(Anchor.Source),
                "STANDARD", $"configuration '{configuration}'");
    }

    /// <summary>The same shapes through IntelliSense (M4 and any route-local copy of the rule).</summary>
    [TestCaseSource(nameof(ConfigurationShapes))]
    public void IntelliSenseRoute_ConfigurationShape_PicksThePipeline(string groups, string configuration, bool aggressive)
    {
        var emission = Project(Anchor.Source, groups).Emit(CppProjectRoute.IntelliSense, configuration);

        if (aggressive)
            AssertCodeIs(emission, CppPipelineLines.Aggressive(Anchor.Source), CppPipelineLines.Standard(Anchor.Source),
                "AGGRESSIVE", $"configuration '{configuration}'");
        else
            AssertCodeIs(emission, CppPipelineLines.Standard(Anchor.Source), CppPipelineLines.Aggressive(Anchor.Source),
                "STANDARD", $"configuration '{configuration}'");
    }

    /// <summary>
    /// The project-wide <c>&lt;Optimize&gt;</c> (a PropertyGroup with no Condition) is NOT consulted:
    /// only the named configuration decides, so a project that says <c>Optimize=false</c> globally
    /// still optimizes in Release and one that says <c>true</c> globally still does not in Debug.
    /// </summary>
    [TestCase("true", "Debug", false)]
    [TestCase("false", "Release", true)]
    [TestCase("true", "Foo", false)]
    public void TheProjectWideOptimize_IsNotConsulted(string projectWide, string configuration, bool aggressive)
    {
        var project = Project(Anchor.Source);
        File.WriteAllText(project.BlprojPath, $"""
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <OutputType>Exe</OutputType>
                <TargetBackend>Cpp</TargetBackend>
                <Optimize>{projectWide}</Optimize>
              </PropertyGroup>
            </BasicLangProject>
            """);
        Assert.That(project.Load().OptimizationsEnabled, Is.EqualTo(bool.Parse(projectWide)),
            "setup: the project-wide flag was read as written");

        var emission = project.Emit(CppProjectRoute.BuildApi, configuration);

        if (aggressive)
            AssertCodeIs(emission, CppPipelineLines.Aggressive(Anchor.Source), CppPipelineLines.Standard(Anchor.Source),
                "AGGRESSIVE", $"configuration '{configuration}' beside a project-wide Optimize={projectWide}");
        else
            AssertCodeIs(emission, CppPipelineLines.Standard(Anchor.Source), CppPipelineLines.Aggressive(Anchor.Source),
                "STANDARD", $"configuration '{configuration}' beside a project-wide Optimize={projectWide}");
    }

    // ==================================================================================
    // 3. The native -O2 request asks the SAME question (CppProjectBuilder ~934): -O2 and the
    //    aggressive passes travel together. Observable with no compiler, from the compile
    //    database the builder writes before it ever invokes one.
    // ==================================================================================

    [TestCaseSource(nameof(ConfigurationShapes))]
    public void IntelliSense_CompileDatabase_O2_FollowsTheSameAnswer(string groups, string configuration, bool aggressive)
    {
        var project = Project(Anchor.Source, groups);

        project.Emit(CppProjectRoute.IntelliSense, configuration);
        var args = project.CompileCommandArguments();

        Assert.Multiple(() =>
        {
            Assert.That(args, Has.One.EqualTo(aggressive ? "-O2" : "-O0"),
                $"configuration '{configuration}': -O2 must be requested exactly when the aggressive pipeline ran. {string.Join(' ', args)}");
            Assert.That(args, Has.None.EqualTo(aggressive ? "-O0" : "-O2"));
        });
    }

    /// <summary>
    /// The BUILD path of the same request, up to the compile step: <c>EmitCore</c> with
    /// <c>forIntelliSense: false</c> and a toolchain that never runs (MSVC, the one a BasicLang
    /// native build always uses) writes <c>/O2</c> or <c>/Od</c>.
    /// </summary>
    [TestCaseSource(nameof(ConfigurationShapes))]
    public void BuildPath_CompileDatabase_MsvcO2_FollowsTheSameAnswer(string groups, string configuration, bool aggressive)
    {
        var project = Project(Anchor.Source, groups);
        var fakeMsvc = CppToolchain.FromExplicit("msvc", Path.Combine(Path.GetTempPath(), "fake-vcvars64.bat"))!;

        CppProjectBuilder.EmitCore(project.Load(), configuration, new CppProjectBuildResult(),
            resolveToolchain: () => throw new InvalidOperationException("a BasicLang native build never probes the machine"),
            forIntelliSense: false,
            resolveById: _ => fakeMsvc,
            publishShim: false);
        var args = project.CompileCommandArguments();

        Assert.Multiple(() =>
        {
            Assert.That(args, Has.One.EqualTo(aggressive ? "/O2" : "/Od"),
                $"configuration '{configuration}': {string.Join(' ', args)}");
            Assert.That(args, Has.None.EqualTo(aggressive ? "/Od" : "/O2"));
        });
    }

    // ==================================================================================
    // 4. One rule. Every build's CompilerOptions (and the native -O2 request) ask
    //    ProjectFile.OptimizationsEnabledFor, and none reads a configuration's flag itself —
    //    a second copy of the rule is how #134 happened. (The CLI's `--optimize` flag, a
    //    literal `true` in Program.cs, is a different thing and is left alone.)
    // ==================================================================================

    [TestCase("BasicLang/Program.cs", @"OptimizeAggressive\s*=\s*project\.OptimizationsEnabledFor\(configuration\)")]
    [TestCase("BasicLang/ProjectSystem/CppProjectBuilder.cs", @"OptimizeAggressive\s*=\s*project\.OptimizationsEnabledFor\(configuration\)")]
    [TestCase("BasicLang/ProjectSystem/CppProjectBuilder.cs", @"\bOptimize\s*=\s*project\.OptimizationsEnabledFor\(configuration\)")]
    public void EveryBuildAsks_OptimizationsEnabledFor(string file, string assignment)
    {
        var text = File.ReadAllText(Path.Combine(SampleSources.RepoRoot(), file.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Match(assignment), $"{file} no longer asks ProjectFile.OptimizationsEnabledFor for: {assignment}");
            Assert.That(System.Text.RegularExpressions.Regex.IsMatch(text, @"\.OptimizationsEnabled\b"), Is.False,
                $"{file} reads a configuration's OptimizationsEnabled itself; go through ProjectFile.OptimizationsEnabledFor.");
        });
    }
}
