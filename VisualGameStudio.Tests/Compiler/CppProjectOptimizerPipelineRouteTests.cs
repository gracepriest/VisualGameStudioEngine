using BasicLang.Compiler.ProjectSystem;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Serialization;
using VisualGameStudio.ProjectSystem.Services;
using CoreBuildConfiguration = VisualGameStudio.Core.Models.BuildConfiguration;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// ⭐ TASK #134 — the two ENTRY POINTS a user actually presses, for "a C++ Release <c>.blproj</c>
/// runs the aggressive pipeline": the CLI (<c>BasicLang build App.blproj -c Release</c>, spawned) and
/// the IDE (<c>BuildService.BuildProjectAsync</c>). CLAUDE.md: the IDE build and the CLI are two
/// routes, and a fix verified through one helper can still break through either.
/// <c>CppProjectOptimizerPipelineTests</c> holds the contract through the builder API and
/// IntelliSense without a process; this fixture is the same contract through the two routes that
/// spawn (the CLI is the process, the IDE's BL6015 diagnosis probes the toolchains with
/// <c>--version</c>), hence Integration.
///
/// <para>A BasicLang native build always needs MSVC, so off Windows both routes write
/// <c>obj/gen</c> and then fail BL6015 — the generated C++ is the witness, and nothing is
/// compiled (see <see cref="CppProjectProbe"/>). The native <c>-O2</c> request is unobservable on
/// those routes there (the toolchain gate precedes the compile database), so its CLI and IDE legs
/// use a PURE C++ project, which any clang++/g++ can build; its Windows-MSVC leg is
/// <c>CppProjectOptimizerPipelineTests.BuildPath_CompileDatabase_MsvcO2_FollowsTheSameAnswer</c>.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class CppProjectOptimizerPipelineRouteTests
{
    private readonly List<CppProjectProbe> _probes = new();
    private readonly List<string> _dirs = new();

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
        foreach (var d in _dirs)
            try { Directory.Delete(d, recursive: true); } catch { /* best-effort temp cleanup */ }
        _dirs.Clear();
    }

    private static void AssertAggressive(CppProjectEmission emission, string source, string what) =>
        AssertCode(emission, source, aggressive: true, what);

    private static void AssertStandard(CppProjectEmission emission, string source, string what) =>
        AssertCode(emission, source, aggressive: false, what);

    private static void AssertCode(CppProjectEmission emission, string source, bool aggressive, string what)
    {
        var wanted = aggressive ? CppPipelineLines.Aggressive(source) : CppPipelineLines.Standard(source);
        var other = aggressive ? CppPipelineLines.Standard(source) : CppPipelineLines.Aggressive(source);
        var pipeline = aggressive ? "AGGRESSIVE" : "STANDARD";
        var actual = CppPipelineLines.Of(emission);

        var missing = CppPipelineLines.Delta(wanted, actual);
        Assert.That(missing, Is.Empty,
            $"{emission.Summary}: {what} must emit the {pipeline} pipeline's code "
            + $"(+ = the pipeline writes it and the project did not, − = the project wrote it and the pipeline did not)\n"
            + CppPipelineLines.Describe(missing));
        Assert.That(CppPipelineLines.Delta(actual, other), Is.Not.Empty,
            $"{emission.Summary}: the code is ALSO the other pipeline's — the probe cannot tell them apart.");
    }

    // ==================================================================================
    // THE IDE: BuildService.BuildProjectAsync -> BuildCppProject -> CppProjectBuilder.Build
    // ==================================================================================

    [TestCaseSource(typeof(CppPipelinePrograms), nameof(CppPipelinePrograms.Discriminating))]
    public void Ide_Release_RunsTheAggressivePipeline(CppPipelineProgram program) =>
        AssertAggressive(Project(program.Source).Emit(CppProjectRoute.IdeBuildService, "Release"),
            program.Source, "a Release C++ project built in the IDE");

    [TestCaseSource(typeof(CppPipelinePrograms), nameof(CppPipelinePrograms.Discriminating))]
    public void Ide_Debug_RunsTheStandardPipeline(CppPipelineProgram program) =>
        AssertStandard(Project(program.Source).Emit(CppProjectRoute.IdeBuildService, "Debug"),
            program.Source, "a Debug C++ project built in the IDE");

    /// <summary>
    /// A <c>BuildService</c> never told a configuration builds <c>Debug</c> (its
    /// <c>CurrentConfiguration</c> defaults to it) — standard. Without this a mutant that always
    /// built Release in the IDE (or always passed a stale name) would only fail for a service that was told.
    /// </summary>
    [Test]
    public void Ide_WithNoConfigurationChosen_BuildsDebug_Standard() =>
        AssertStandard(Project(CppPipelinePrograms.Alg.Source).Emit(CppProjectRoute.IdeBuildService, configuration: null),
            CppPipelinePrograms.Alg.Source, "the IDE's default configuration");

    [Test]
    public void Ide_ACustomConfiguration_WithOptimizeTrue_IsAggressive() =>
        AssertAggressive(Project(CppPipelinePrograms.Alg.Source, CppProjectProbe.Configuration("Profile", optimize: true))
                .Emit(CppProjectRoute.IdeBuildService, "Profile"),
            CppPipelinePrograms.Alg.Source, "configuration 'Profile' (Optimize=true) built in the IDE");

    [Test]
    public void Ide_AReleaseWithOptimizeFalse_IsStandard() =>
        AssertStandard(Project(CppPipelinePrograms.Alg.Source, CppProjectProbe.Configuration("Release", optimize: false))
                .Emit(CppProjectRoute.IdeBuildService, "Release"),
            CppPipelinePrograms.Alg.Source, "configuration 'Release' (Optimize=false) built in the IDE");

    [Test]
    public void Ide_ADebugWithOptimizeTrue_IsAggressive() =>
        AssertAggressive(Project(CppPipelinePrograms.Alg.Source, CppProjectProbe.Configuration("Debug", optimize: true))
                .Emit(CppProjectRoute.IdeBuildService, "Debug"),
            CppPipelinePrograms.Alg.Source, "configuration 'Debug' (Optimize=true) built in the IDE");

    /// <summary>
    /// The IDE does not reimplement the build: its <c>obj/gen</c> is the CLI engine's — byte for byte in
    /// Release; in Debug the same code (a Debug build also writes <c>#line</c> directives naming the
    /// source by absolute path, whose spelling is the OS's business, so the multiset drops them).
    /// </summary>
    [TestCase("Release")]
    [TestCase("Debug")]
    public void Ide_ObjGen_IsTheBuilderApi(string configuration)
    {
        var project = Project(CppPipelinePrograms.Field.Source);

        var api = project.Emit(CppProjectRoute.BuildApi, configuration);
        var ide = project.Emit(CppProjectRoute.IdeBuildService, configuration);

        AssertSameFiles(api, ide, byteForByte: configuration == "Release");
    }

    // ==================================================================================
    // THE CLI: BasicLang build App.blproj [-c <configuration>], a real child process
    // ==================================================================================

    [TestCaseSource(typeof(CppPipelinePrograms), nameof(CppPipelinePrograms.Discriminating))]
    public void Cli_Release_RunsTheAggressivePipeline(CppPipelineProgram program) =>
        AssertAggressive(Project(program.Source).Emit(CppProjectRoute.Cli, "Release"),
            program.Source, "a Release C++ project built by `BasicLang build -c Release`");

    [TestCaseSource(typeof(CppPipelinePrograms), nameof(CppPipelinePrograms.Discriminating))]
    public void Cli_Debug_RunsTheStandardPipeline(CppPipelineProgram program) =>
        AssertStandard(Project(program.Source).Emit(CppProjectRoute.Cli, "Debug"),
            program.Source, "a Debug C++ project built by `BasicLang build -c Debug`");

    /// <summary>No <c>-c</c> is <c>Debug</c> (the CLI's default): standard.</summary>
    [Test]
    public void Cli_WithNoConfigurationFlag_BuildsDebug_Standard() =>
        AssertStandard(Project(CppPipelinePrograms.Alg.Source).Emit(CppProjectRoute.Cli, configuration: null),
            CppPipelinePrograms.Alg.Source, "`BasicLang build` with no -c");

    /// <summary>The long spelling reaches the same place as <c>-c</c>.</summary>
    [Test]
    public void Cli_TheLongConfigurationFlag_IsHonouredToo()
    {
        var project = Project(CppPipelinePrograms.Alg.Source);
        if (Directory.Exists(project.ObjGen)) Directory.Delete(project.ObjGen, recursive: true);

        var (exit, stdout, stderr) = CliTestHarness.RunProcess(CliTestHarness.CliPath(),
            new[] { "build", project.BlprojPath, "--configuration", "Release" }, project.Dir, timeoutMs: 300_000);

        var emission = new CppProjectEmission(project.ReadObjGen(), $"exit {exit}\n{stdout}\n{stderr}", "Cli --configuration Release");
        Assert.That(emission.Files, Does.ContainKey("Main.g.cpp"), emission.Diagnostics);
        AssertAggressive(emission, CppPipelinePrograms.Alg.Source, "`BasicLang build --configuration Release`");
    }

    [Test]
    public void Cli_ACustomConfiguration_WithOptimizeTrue_IsAggressive() =>
        AssertAggressive(Project(CppPipelinePrograms.Alg.Source, CppProjectProbe.Configuration("Profile", optimize: true))
                .Emit(CppProjectRoute.Cli, "Profile"),
            CppPipelinePrograms.Alg.Source, "`BasicLang build -c Profile` (Optimize=true)");

    [Test]
    public void Cli_AReleaseWithOptimizeFalse_IsStandard() =>
        AssertStandard(Project(CppPipelinePrograms.Alg.Source, CppProjectProbe.Configuration("Release", optimize: false))
                .Emit(CppProjectRoute.Cli, "Release"),
            CppPipelinePrograms.Alg.Source, "`BasicLang build -c Release` (Optimize=false)");

    [Test]
    public void Cli_ADebugWithOptimizeTrue_IsAggressive() =>
        AssertAggressive(Project(CppPipelinePrograms.Alg.Source, CppProjectProbe.Configuration("Debug", optimize: true))
                .Emit(CppProjectRoute.Cli, "Debug"),
            CppPipelinePrograms.Alg.Source, "`BasicLang build -c Debug` (Optimize=true)");

    /// <summary>The spawned CLI and the in-process builder write the same <c>obj/gen</c> (byte for byte in Release, as the IDE test says).</summary>
    [TestCase("Release")]
    [TestCase("Debug")]
    public void Cli_ObjGen_IsTheBuilderApi(string configuration)
    {
        var project = Project(CppPipelinePrograms.Field.Source);

        var api = project.Emit(CppProjectRoute.BuildApi, configuration);
        var cli = project.Emit(CppProjectRoute.Cli, configuration);

        AssertSameFiles(api, cli, byteForByte: configuration == "Release");
    }

    private static void AssertSameFiles(CppProjectEmission expected, CppProjectEmission actual, bool byteForByte) =>
        Assert.Multiple(() =>
        {
            Assert.That(actual.Files.Keys.OrderBy(k => k), Is.EqualTo(expected.Files.Keys.OrderBy(k => k)),
                $"{actual.Summary} vs {expected.Summary}: the same generated files");
            if (byteForByte)
                foreach (var (name, text) in expected.Files)
                    Assert.That(actual.Files.GetValueOrDefault(name), Is.EqualTo(text),
                        $"{name}: {actual.Summary} differs from {expected.Summary}");
            else
                Assert.That(CppPipelineLines.Delta(CppPipelineLines.Of(actual), CppPipelineLines.Of(expected)), Is.Empty,
                    $"{actual.Summary} vs {expected.Summary}: the same code");
        });

    // ==================================================================================
    // The native -O2 request through the CLI and the IDE: a PURE C++ project (any clang++/g++
    // builds it), whose compile database the real build writes.
    // ==================================================================================

    private string PureCppProject()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-cpppipe-pure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _dirs.Add(dir);
        File.WriteAllText(Path.Combine(dir, "main.cpp"), "int main() { return 0; }\n");
        File.WriteAllText(Path.Combine(dir, "App.blproj"), """
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <OutputType>Exe</OutputType>
                <Language>Cpp</Language>
                <TargetBackend>Cpp</TargetBackend>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="main.cpp" />
              </ItemGroup>
            </BasicLangProject>
            """);
        return dir;
    }

    private static bool Requests(List<string> args, bool optimize) =>
        optimize ? args.Contains("-O2") || args.Contains("/O2") : args.Contains("-O0") || args.Contains("/Od");

    private static List<string> CompileDatabaseArguments(string dir)
    {
        var path = Path.Combine(dir, "obj", "compile_commands.json");
        Assert.That(File.Exists(path), Is.True, "no obj/compile_commands.json under " + dir);
        var db = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        return db[0]!["arguments"]!.AsArray().Select(a => a!.GetValue<string>()).ToList();
    }

    [TestCase("Release", true)]
    [TestCase("Debug", false)]
    public void Cli_PureCppProject_RequestsO2_ExactlyWhenTheConfigurationOptimizes(string configuration, bool optimize)
    {
        if (CppToolchain.Find() == null) Assert.Ignore("No C++ toolchain available (clang++/g++/MSVC)");
        var dir = PureCppProject();

        var (exit, stdout, stderr) = CliTestHarness.RunProcess(CliTestHarness.CliPath(),
            new[] { "build", Path.Combine(dir, "App.blproj"), "-c", configuration }, dir, timeoutMs: 300_000);

        Assert.That(exit, Is.EqualTo(0), $"build failed.\n{stdout}\n{stderr}");
        var args = CompileDatabaseArguments(dir);
        Assert.That(Requests(args, optimize), Is.True, $"{configuration}: {string.Join(' ', args)}");
        Assert.That(Requests(args, !optimize), Is.False, $"{configuration}: {string.Join(' ', args)}");
    }

    [TestCase("Release", true)]
    [TestCase("Debug", false)]
    public async Task Ide_PureCppProject_RequestsO2_ExactlyWhenTheConfigurationOptimizes(string configuration, bool optimize)
    {
        if (CppToolchain.Find() == null) Assert.Ignore("No C++ toolchain available (clang++/g++/MSVC)");
        var dir = PureCppProject();
        var service = new BuildService(new Mock<IOutputService>().Object);
        service.CurrentConfiguration = new CoreBuildConfiguration { Name = configuration };
        var project = await new ProjectSerializer().LoadAsync(Path.Combine(dir, "App.blproj"));

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var result = await service.BuildProjectAsync(project, cts.Token);

        Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics.Select(d => d.Id + ": " + d.Message)));
        var args = CompileDatabaseArguments(dir);
        Assert.That(Requests(args, optimize), Is.True, $"{configuration}: {string.Join(' ', args)}");
        Assert.That(Requests(args, !optimize), Is.False, $"{configuration}: {string.Join(' ', args)}");
    }
}
