using System.Text.Json.Nodes;
using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Serialization;
using VisualGameStudio.ProjectSystem.Services;
using VisualGameStudio.Shell.ViewModels.Dialogs;
// Disambiguates against BasicLang.Compiler.ProjectSystem.ProjectTemplate (a distinct,
// unrelated type) -- IProjectTemplateService's wizard-facing ProjectTemplate lives here.
using ProjectTemplate = VisualGameStudio.Core.Abstractions.Services.ProjectTemplate;
// Disambiguates against VisualGameStudio.Core.Models.ProjectLanguage (the persisted
// .blproj <Language> axis, needed here only for BasicLangProject) -- the wizard's
// deliberately-separate UI-only enum (NewProjectWizardViewModel.cs's own remark) is
// what vm.SelectedLanguage actually is.
using ProjectLanguage = VisualGameStudio.Shell.ViewModels.Dialogs.ProjectLanguage;

namespace VisualGameStudio.Tests;

/// <summary>
/// Task 12 (per-backend C++ toolchain overrides): end-to-end proof that a single
/// <c>cpp.toolchain.gcc.compiler</c> override -- backed only by a fake g++.exe that
/// merely EXISTS on disk (winlibs stays OFF PATH, per the standing constraint) --
/// actually drives the whole feature: (a) <see cref="CppToolchainProbeService"/> reports
/// gcc available even though the base PATH/vswhere probe (faked) says it's off PATH
/// (ties Task 8); (b) a gcc-pinned project built through <see cref="BuildService"/>, with
/// PATH faked empty, resolves the compiler via the override all the way into
/// compile_commands.json (ties Task 6); and (c) <see cref="NewProjectWizardViewModel"/>,
/// fed the REAL override-aware <see cref="CppToolchainProbeService"/> (not a fake), reacts
/// with no wizard-code change at all -- gcc becomes enabled, its "(not installed)" hint
/// clears, and c++23 is offered (spec sections 5.3 / 6.1).
///
/// Unlike the sibling unit tests (<see cref="CppToolchainProbeOverrideTests"/>,
/// <see cref="BuildServiceToolchainOverrideTests"/>), the g++.exe existence check here is
/// NEVER faked via a <c>fileExists</c> seam -- <see cref="CppToolchainOverrides"/> is
/// constructed with its default (real <c>File.Exists</c>), so the override genuinely
/// resolves off the real filesystem, exactly as it would for a developer who points
/// Settings at a real, off-PATH compiler.
/// </summary>
[TestFixture]
public class ToolchainOverrideIntegrationTests
{
    private string _dir = null!;
    private string _fakeGxxPath = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-toi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        // The fake off-PATH g++: a REAL temp file that merely EXISTS. winlibs stays OFF
        // PATH -- this is never a real toolchain and is never actually executed to compile.
        _fakeGxxPath = Path.Combine(_dir, "g++.exe");
        File.WriteAllText(_fakeGxxPath, "");
    }

    // Retries like the sibling toolchain-override tests' TearDown (BuildServiceToolchainOverrideTests.cs,
    // CppProjectBuilderResolveToolchainTests.cs) -- a transient Windows file lock (or AV scan) on a
    // just-written file must not leak the temp dir.
    [TearDown]
    public void TearDown()
    {
        for (var i = 0; i < 3; i++)
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); return; }
            catch { Thread.Sleep(200); }
        }
    }

    /// <summary>The override reader, pointed at the real fake g++.exe, with NO fileExists
    /// seam -- existence is checked against the real filesystem.</summary>
    private CppToolchainOverrides MakeGccOverride() =>
        new(new FakeSettings { ["cpp.toolchain.gcc.compiler"] = _fakeGxxPath });

    // ==================================================================
    // Step 1(a): CppToolchainProbeService.Probe().Gcc == true even with a base probe
    // reporting gcc OFF PATH (ties the override reader to Task 8's probe).
    // ==================================================================
    [Test]
    public void Probe_GccOverride_ReportsAvailable_EvenWithBaseProbeReportingOffPath()
    {
        var probe = new CppToolchainProbeService(MakeGccOverride(),
            baseProbe: () => new CppToolchainAvailability(Llvm: false, Gcc: false, Msvc: false));

        var result = probe.Probe();

        Assert.That(result.Gcc, Is.True,
            "a set, Usable gcc override must mark gcc available even though the base " +
            "PATH/vswhere probe reports it off PATH");
    }

    // ==================================================================
    // Step 1(b): a gcc-pinned project built through BuildService, with PATH faked empty,
    // resolves the compiler via the override -- reaching compile_commands.json as the
    // driver (ties Task 6 + Task 8 end-to-end).
    // ==================================================================
    [Test]
    public async Task Build_GccPinnedProject_WithEmptyPath_ResolvesCompilerViaOverride()
    {
        var output = new RecordingOutput();
        var buildService = new BuildService(output, new ProjectSerializer(), MakeGccOverride(),
            pathResolve: _ => null); // faked-empty PATH: nothing resolves without the override

        var project = await CreateCppProjectOnDisk("GccPinnedIntegration",
            "int main(){ return 0; }\n", cppToolchain: "gcc");

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var result = await buildService.BuildProjectAsync(project, cts.Token);

        // Ignore result.Success: the fake g++.exe is an empty file and can't actually
        // compile anything. What matters is that compile_commands.json was already
        // written with the override as the driver, during EmitCore, before the (failing)
        // compile step.
        var ccPath = Path.Combine(Path.GetDirectoryName(project.FilePath)!, "obj", "compile_commands.json");
        Assert.That(File.Exists(ccPath), Is.True,
            "compile_commands.json must be written before the compile step.\n" + Describe(result, output));
        var args = ReadFirstDriverArgs(ccPath);
        Assert.That(args[0], Is.EqualTo(_fakeGxxPath),
            "the pinned gcc override must reach arguments[0] verbatim, not a PATH probe result");
    }

    // ==================================================================
    // Step 1b: the wizard VM reacts to override-driven availability with NO wizard code
    // change, fed the REAL CppToolchainProbeService (not FakeToolchainProbe) over the
    // same gcc override, gcc off PATH per the faked base probe (spec sections 6.1 / 5.3).
    // ==================================================================
    [Test]
    public async Task Wizard_FedRealOverrideAwareProbeService_EnablesGcc_ClearsHint_OffersCpp23()
    {
        var realProbe = new CppToolchainProbeService(MakeGccOverride(),
            baseProbe: () => new CppToolchainAvailability(Llvm: false, Gcc: false, Msvc: false));

        var vm = new NewProjectWizardViewModel(new FakeTemplateService(), realProbe);

        await vm.ToolchainProbeTask;
        vm.SelectedLanguage = ProjectLanguage.Cpp;

        var gcc = vm.Backends.First(b => b.ToolchainId == "gcc");
        Assert.That(gcc.IsEnabled, Is.True,
            "gcc must be enabled: the wizard's real probe read the same override the probe/build tests above used");
        Assert.That(gcc.AvailabilityHint, Is.Null, "an enabled backend must have its unavailability hint cleared");

        vm.SelectedBackend = gcc;
        Assert.That(vm.CppStandards, Does.Contain("c++23"),
            "gcc offers c++23 once enabled via the override -- RecomputeCppStandards keys off IsEnabled, " +
            "confirming the wizard needed zero code changes to react to the override");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private async Task<BasicLangProject> CreateCppProjectOnDisk(string name, string mainCpp, string? cppToolchain = null)
    {
        var dir = Path.Combine(_dir, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "main.cpp"), mainCpp);
        var pinElement = cppToolchain != null ? $"    <CppToolchain>{cppToolchain}</CppToolchain>\n" : "";
        var blproj = Path.Combine(dir, name + ".blproj");
        File.WriteAllText(blproj, $"""
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>{name}</ProjectName>
                <OutputType>Exe</OutputType>
                <Language>Cpp</Language>
                <TargetBackend>Cpp</TargetBackend>
            {pinElement}  </PropertyGroup>
              <ItemGroup>
                <Compile Include="main.cpp" />
              </ItemGroup>
            </BasicLangProject>
            """);
        return await new ProjectSerializer().LoadAsync(blproj);
    }

    private static List<string> ReadFirstDriverArgs(string compileCommandsPath)
    {
        var db = JsonNode.Parse(File.ReadAllText(compileCommandsPath))!;
        return db[0]!["arguments"]!.AsArray().Select(a => a!.GetValue<string>()).ToList();
    }

    private static string Describe(BuildResult result, RecordingOutput output)
    {
        return "Diagnostics:\n" +
               string.Join("\n", result.Diagnostics.Select(d =>
                   $"  {d.FilePath}({d.Line},{d.Column}): {d.Severity} {d.Id}: {d.Message}")) +
               "\n\nBuild output:\n" + output.Dump();
    }

    /// <summary>Minimal ISettingsService stub -- mirrors CppToolchainOverridesTests' FakeSettings
    /// (and its BuildServiceToolchainOverrideTests / CppToolchainProbeOverrideTests siblings).
    /// Only Get{T} has real behavior; everything else is unused by CppToolchainOverrides and throws.</summary>
    private sealed class FakeSettings : ISettingsService
    {
        private readonly Dictionary<string, string> _values = new();

        public string this[string key]
        {
            set => _values[key] = value;
        }

        public T Get<T>(string key, T defaultValue, SettingsScope scope = SettingsScope.Effective)
        {
            if (_values.TryGetValue(key, out var raw) && raw is T typed) return typed;
            return defaultValue;
        }

        public T? GetValue<T>(string key, T? defaultValue = default) => throw new NotImplementedException();
        public object? Get(string key, SettingsScope scope = SettingsScope.Effective) => throw new NotImplementedException();
        public void Set<T>(string key, T value, SettingsScope scope = SettingsScope.User) => throw new NotImplementedException();
        public void SetValue<T>(string key, T value) => throw new NotImplementedException();
        public void Remove(string key, SettingsScope scope = SettingsScope.User) => throw new NotImplementedException();
        public bool Has(string key, SettingsScope scope = SettingsScope.Effective) => throw new NotImplementedException();
        public IReadOnlyDictionary<string, object?> GetAll(SettingsScope scope = SettingsScope.Effective) => throw new NotImplementedException();
        public IReadOnlyDictionary<string, object?> GetSection(string prefix, SettingsScope scope = SettingsScope.Effective) => throw new NotImplementedException();
        public void ResetToDefault(string key) => throw new NotImplementedException();
        public void ResetAllToDefaults() => throw new NotImplementedException();
        public Task LoadAsync() => throw new NotImplementedException();
        public Task SaveAsync() => throw new NotImplementedException();
        public Task ImportAsync(string filePath) => throw new NotImplementedException();
        public Task ExportAsync(string filePath, SettingsScope scope = SettingsScope.User) => throw new NotImplementedException();
        public void RegisterSchema(SettingsSchema schema) => throw new NotImplementedException();
        public IReadOnlyList<SettingsSchema> GetSchemas() => throw new NotImplementedException();
        public SettingsPropertySchema? GetPropertySchema(string key) => throw new NotImplementedException();
        public void SetWorkspacePath(string? path) => throw new NotImplementedException();
        public bool HasWorkspace => false;
        public string? WorkspaceSettingsPath => null;
        public bool IsOverriddenInWorkspace(string key) => throw new NotImplementedException();
        public event EventHandler<SettingChangedEventArgs>? SettingChanged;
        public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;
        public event EventHandler<string?>? WorkspacePathChanged;
    }

    /// <summary>Thread-safe recording IOutputService so failures show real build output.</summary>
    private sealed class RecordingOutput : IOutputService
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _lines = new();

        public string Dump() => string.Join(Environment.NewLine, _lines);

        public void WriteLine(string message, OutputCategory category = OutputCategory.General) => _lines.Enqueue(message);
        public void Write(string message, OutputCategory category = OutputCategory.General) => _lines.Enqueue(message);
        public void WriteError(string message, OutputCategory category = OutputCategory.General) => _lines.Enqueue("ERROR: " + message);
        public void Clear(OutputCategory category) { }
        public void ClearAll() { }
        public void Activate(OutputCategory category) { }
        public IReadOnlyList<string> GetMessages(OutputCategory category) => _lines.ToArray();
        public event EventHandler<OutputEventArgs>? OutputReceived { add { } remove { } }
        public IOutputChannel CreateChannel(string name) => throw new NotSupportedException();
        public IOutputChannel? GetChannel(string name) => null;
        public IReadOnlyList<IOutputChannel> Channels => Array.Empty<IOutputChannel>();
        public IOutputChannel? ActiveChannel { get; set; }
        public event EventHandler<string>? ChannelCreated { add { } remove { } }
        public event EventHandler<IOutputChannel?>? ActiveChannelChanged { add { } remove { } }
        public void ShowOutput() { }
    }

    /// <summary>Fake IProjectTemplateService -- mirrors NewProjectWizardViewModelTests'
    /// FakeTemplateService. Only used to satisfy NewProjectWizardViewModel's ctor; this
    /// test never exercises project creation, only the toolchain-availability wiring.</summary>
    private sealed class FakeTemplateService : IProjectTemplateService
    {
        public IReadOnlyList<SolutionType> GetSolutionTypes() => SolutionTypes.All;
        public IReadOnlyList<ProjectTemplate> GetProjectTemplates(SolutionType solutionType) =>
            ProjectTemplates.All.Where(t => t.SupportedSolutionTypes.Contains(solutionType.Id)).ToList();
        public IReadOnlyList<ProjectTemplate> GetAllProjectTemplates() => ProjectTemplates.All;
        public Task<ProjectCreationResult> CreateProjectAsync(CreateProjectOptions options, CancellationToken ct = default)
            => Task.FromResult(new ProjectCreationResult { Success = true, ProjectPath = "X:/proj/proj.blproj" });
        public Task<SolutionCreationResult> CreateSolutionAsync(CreateSolutionOptions options, CancellationToken ct = default)
            => Task.FromResult(new SolutionCreationResult { Success = true });
        public ProjectValidationResult ValidateProjectOptions(CreateProjectOptions options) => new() { IsValid = true };
        public void RegisterTemplate(ProjectTemplate template) { }
        public IReadOnlyList<ProjectTemplate> GetRecentTemplates() => new List<ProjectTemplate>();
    }
}
