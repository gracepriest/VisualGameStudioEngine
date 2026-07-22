using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Serialization;
using VisualGameStudio.ProjectSystem.Services;
using VisualGameStudio.Shell.Configuration;
using CppToolchain = BasicLang.Compiler.ProjectSystem.CppToolchain;

namespace VisualGameStudio.Tests;

/// <summary>
/// Task 6 (per-backend C++ toolchain overrides): <see cref="BuildService.BuildCppProject"/>
/// wires the DI-injected <see cref="CppToolchainOverrides"/> reader into
/// <c>CppProjectBuilder.Build</c>'s <c>resolveById</c>/<c>resolveToolchain</c> seams, and
/// pre-validates a PINNED, set-but-Invalid override as a hard error before any emission
/// happens. Every "on PATH" case here is faked through the ctor's <c>pathResolve</c>/
/// <c>pathFind</c> test seams — winlibs stays OFF PATH (standing constraint) and none of
/// these tests may depend on a real installed compiler.
/// </summary>
[TestFixture]
public class BuildServiceToolchainOverrideTests
{
    private string _rootDir = null!;

    [SetUp]
    public void SetUp()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "bl-bsov-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDir);
    }

    [TearDown]
    public void TearDown()
    {
        for (var i = 0; i < 3; i++)
        {
            try { if (Directory.Exists(_rootDir)) Directory.Delete(_rootDir, recursive: true); return; }
            catch { Thread.Sleep(200); }
        }
    }

    // ------------------------------------------------------------------
    // 1. Pinned gcc + usable override (fake existing g++), nothing on PATH
    //    -> the build resolves via the override path.
    // ------------------------------------------------------------------

    [Test]
    public async Task PinnedGcc_UsableOverride_NothingOnPath_ResolvesViaOverride()
    {
        var overridePath = @"C:\fake-override\gcc\bin\g++.exe"; // never on disk, never really executed
        var settings = new FakeSettings { ["cpp.toolchain.gcc.compiler"] = overridePath };
        var overrides = new CppToolchainOverrides(settings, fileExists: p => p == overridePath);

        var output = new RecordingOutput();
        var buildService = new BuildService(output, new ProjectSerializer(), overrides,
            pathResolve: _ => null,   // nothing on PATH, deterministically
            pathFind: () => null);

        var project = await CreateCppProjectOnDisk("PinnedGccUsable",
            "int main(){ return 0; }\n", cppToolchain: "gcc");

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var result = await buildService.BuildProjectAsync(project, cts.Token);

        // The fake path can't actually compile (Process.Start fails to find the exe), so
        // ignore result.Success — what matters is that the override reached
        // arguments[0] (the driver a real build invokes) in compile_commands.json,
        // written during EmitCore BEFORE the (failing) compile step.
        var ccPath = Path.Combine(Path.GetDirectoryName(project.FilePath)!, "obj", "compile_commands.json");
        Assert.That(File.Exists(ccPath), Is.True,
            "compile_commands.json must be written before the compile step.\n" + Describe(result, output));
        var args = ReadFirstDriverArgs(ccPath);
        Assert.That(args[0], Is.EqualTo(overridePath),
            "the pinned gcc override must reach arguments[0] verbatim, not a PATH probe result");
    }

    // ------------------------------------------------------------------
    // 2. Unpinned discriminating tie-break: llvm on PATH (faked) + usable gcc
    //    override -> selects llvm (fixed order), NOT gcc.
    // ------------------------------------------------------------------

    [Test]
    public async Task Unpinned_LlvmOnPath_PlusUsableGccOverride_SelectsLlvm_NotGcc()
    {
        var gccOverridePath = @"C:\fake-override\gcc\bin\g++.exe";
        var llvmOnPathFake = @"C:\fake-on-path\llvm\bin\clang++.exe";
        var settings = new FakeSettings { ["cpp.toolchain.gcc.compiler"] = gccOverridePath };
        var overrides = new CppToolchainOverrides(settings, fileExists: p => p == gccOverridePath);

        var output = new RecordingOutput();
        var buildService = new BuildService(output, new ProjectSerializer(), overrides,
            pathResolve: id => id == "llvm" ? CppToolchain.FromExplicit("llvm", llvmOnPathFake) : null,
            // Not all backends are None here (gcc has a Usable override), so the
            // "nothing configured at all" fast path must never fire in this scenario —
            // proven by making it explode if it ever does.
            pathFind: () => throw new InvalidOperationException(
                "pathFind must not be called when a backend override is configured"));

        var project = await CreateCppProjectOnDisk("UnpinnedTieBreak", "int main(){ return 0; }\n");

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var result = await buildService.BuildProjectAsync(project, cts.Token);

        var ccPath = Path.Combine(Path.GetDirectoryName(project.FilePath)!, "obj", "compile_commands.json");
        Assert.That(File.Exists(ccPath), Is.True, Describe(result, output));
        var args = ReadFirstDriverArgs(ccPath);
        Assert.That(args[0], Is.EqualTo(llvmOnPathFake),
            "fixed precedence llvm -> gcc -> msvc must pick llvm (found on PATH) over gcc's " +
            "Settings override, since llvm itself has no override configured (blank = probe).");
    }

    // ------------------------------------------------------------------
    // 3. Pinned gcc + Invalid override, while gcc is ALSO "on PATH" -> hard
    //    error, never a fallback to PATH.
    // ------------------------------------------------------------------

    [Test]
    public async Task PinnedGcc_InvalidOverride_EvenWhenOnPath_IsHardErrorNotPathFallback()
    {
        var badPath = @"C:\does-not-exist\gcc\bin\g++.exe";
        var settings = new FakeSettings { ["cpp.toolchain.gcc.compiler"] = badPath };
        var overrides = new CppToolchainOverrides(settings, fileExists: _ => false); // -> Invalid

        var output = new RecordingOutput();
        var buildService = new BuildService(output, new ProjectSerializer(), overrides,
            // gcc IS "on PATH" per this fake — if the pinned-Invalid gate ever fell back to
            // PATH, this would resolve fine and the test's failure assertions below would
            // catch it. Throwing makes the proof airtight: the seam must never even run.
            pathResolve: _ => throw new InvalidOperationException(
                "pathResolve must not be called for a pinned, set-but-Invalid override"),
            pathFind: () => throw new InvalidOperationException(
                "pathFind must not be called for a pinned, set-but-Invalid override"));

        var project = await CreateCppProjectOnDisk("PinnedGccInvalid",
            "int main(){ return 0; }\n", cppToolchain: "gcc");

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var result = await buildService.BuildProjectAsync(project, cts.Token);

        Assert.That(result.Success, Is.False, Describe(result, output));
        var diag = result.Diagnostics.FirstOrDefault(d => d.Id == "BL6015");
        Assert.That(diag, Is.Not.Null, "expected a BL6015 hard-error diagnostic.\n" + Describe(result, output));
        Assert.That(diag!.Message, Does.Contain("gcc"),
            "the message must name which backend's configured compiler path is broken");
        Assert.That(diag.Message, Does.Contain("Settings"));
        Assert.That(diag.Message, Does.Contain("C++"));

        // Never fell back to PATH: EmitCore was never reached, so no compile_commands.json.
        var ccPath = Path.Combine(Path.GetDirectoryName(project.FilePath)!, "obj", "compile_commands.json");
        Assert.That(File.Exists(ccPath), Is.False,
            "a pinned Invalid override must hard-fail before EmitCore runs at all, never silently substitute PATH");
    }

    // ------------------------------------------------------------------
    // 4. No override configured at all -> unchanged (today's) behavior.
    // ------------------------------------------------------------------

    [Test]
    public async Task NoOverrideConfigured_UnchangedBehavior()
    {
        // A BuildService built the same way every pre-Task-6 caller builds one: no settings
        // service (every backend reads as None) and no faked PATH seams, i.e. the exact
        // default CppToolchain.TryFindById/Find probes CppProjectBuilder.Build used before
        // this change existed.
        var output = new RecordingOutput();
        var buildService = new BuildService(output, new ProjectSerializer(), new CppToolchainOverrides(null));

        var project = await CreateCppProjectOnDisk("NoOverrideAtAll", "int main(){ return 0; }\n");

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var result = await buildService.BuildProjectAsync(project, cts.Token);

        Assert.That(result, Is.Not.Null);
        if (!result.Success)
        {
            Assert.That(result.Diagnostics, Is.Not.Empty,
                "a failing build with no override configured must still fail through the " +
                "normal toolchain gate (BL6005) or a real compile error, not silently.\n" +
                Describe(result, output));
        }
    }

    // ------------------------------------------------------------------
    // 5. DI trap guard: the CONTAINER-composed IBuildService must carry a
    //    non-null overrides reader. A directly-constructed BuildService (as in
    //    tests 1-4 above) cannot catch a by-type-registration regression — only
    //    exercising ServiceConfiguration.ConfigureServices can.
    // ------------------------------------------------------------------

    [Test]
    public void DiComposedBuildService_CarriesNonNullOverridesReader()
    {
        var services = new ServiceCollection();
        services.ConfigureServices();
        // Last registration wins in MS.DI: keep the real BuildService/CppToolchainOverrides
        // wiring under test while stopping the real SettingsService from touching ~/.vgs.
        services.AddSingleton(Mock.Of<ISettingsService>());
        using var provider = services.BuildServiceProvider();

        var buildService = provider.GetRequiredService<IBuildService>();

        Assert.That(buildService, Is.InstanceOf<BuildService>(),
            "IBuildService must resolve to a real BuildService");

        var overridesField = typeof(BuildService).GetField("_overrides", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(overridesField, Is.Not.Null, "BuildService must declare an _overrides field");
        var overridesValue = overridesField!.GetValue(buildService);

        Assert.That(overridesValue, Is.Not.Null,
            "the container-composed IBuildService has a NULL overrides reader — this is exactly " +
            "the DI trap: a by-type AddSingleton<IBuildService, BuildService>() registration " +
            "silently falls back to a convenience ctor and the whole override feature no-ops.");
        Assert.That(overridesValue, Is.InstanceOf<CppToolchainOverrides>());
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private async Task<BasicLangProject> CreateCppProjectOnDisk(string name, string mainCpp, string? cppToolchain = null)
    {
        var dir = Path.Combine(_rootDir, name);
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

    /// <summary>
    /// Minimal <see cref="ISettingsService"/> stub — mirrors CppToolchainOverridesTests'
    /// FakeSettings. Only <see cref="Get{T}"/> has real behavior; everything else is unused
    /// by <see cref="CppToolchainOverrides"/> and throws.
    /// </summary>
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
}
