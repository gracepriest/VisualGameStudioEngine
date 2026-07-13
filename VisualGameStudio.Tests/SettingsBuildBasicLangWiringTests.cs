using System;
using System.IO;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Serialization;
using VisualGameStudio.ProjectSystem.Services;
using VisualGameStudio.Shell.ViewModels;

namespace VisualGameStudio.Tests;

/// <summary>
/// Pins Task 2.4 — the Build + BasicLang settings group wired through <see cref="ISettingsService"/>:
///
/// * <c>build.saveBeforeBuild</c> gates the save-all before a build;
/// * <c>build.showOutput</c> gates revealing the Output panel on build;
/// * <c>build.defaultConfiguration</c> seeds the initial active configuration (validated against the
///   known configurations);
/// * <c>basiclang.compiler.backend</c> supplies the default <see cref="TargetBackend"/> for new
///   projects (written INTO the .blproj, so BOTH the IDE and the CLI honor it) and for .blproj files
///   that omit <c>&lt;TargetBackend&gt;</c>;
/// * <c>basiclang.lsp.path</c> overrides the LSP compiler path when it names an existing file;
/// * <c>basiclang.lsp.autoStart</c> gates the language-server auto-start;
/// * every wired key names a consumer in <see cref="SettingsConsumerRegistry"/>.
///
/// The gating/seeding call sites (BuildAsync / RebuildAsync / ctor / StartAsync) are UI-bound; the
/// pure resolution seams — the part that can silently drift (key names, defaults, clamps, mapping) —
/// are pinned here, plus the .blproj round-trip through both entry points.
/// </summary>
[TestFixture]
public class SettingsBuildBasicLangWiringTests
{
    private string _homeDir = null!;
    private SettingsService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _homeDir = Path.Combine(Path.GetTempPath(), $"SettingsBuildWiring_{Guid.NewGuid()}");
        Directory.CreateDirectory(_homeDir);
        _service = new SettingsService(_homeDir);
    }

    [TearDown]
    public void TearDown()
    {
        try { _service.Dispose(); } catch { /* ignore */ }
        try { if (Directory.Exists(_homeDir)) Directory.Delete(_homeDir, true); }
        catch { /* ignore cleanup errors */ }
    }

    // ---- build.saveBeforeBuild ----

    [Test]
    public void SaveBeforeBuild_DefaultEnabled()
        => Assert.That(MainWindowViewModel.ShouldSaveBeforeBuild(_service), Is.True);

    [Test]
    public void SaveBeforeBuild_WhenDisabled_ReturnsFalse()
    {
        _service.Set("build.saveBeforeBuild", false);
        Assert.That(MainWindowViewModel.ShouldSaveBeforeBuild(_service), Is.False);
    }

    [Test]
    public void SaveBeforeBuild_NullService_TreatedAsEnabled()
        => Assert.That(MainWindowViewModel.ShouldSaveBeforeBuild(null), Is.True);

    // ---- build.showOutput ----

    [Test]
    public void ShowBuildOutput_DefaultEnabled()
        => Assert.That(MainWindowViewModel.ShouldShowBuildOutput(_service), Is.True);

    [Test]
    public void ShowBuildOutput_WhenDisabled_ReturnsFalse()
    {
        _service.Set("build.showOutput", false);
        Assert.That(MainWindowViewModel.ShouldShowBuildOutput(_service), Is.False);
    }

    [Test]
    public void ShowBuildOutput_NullService_TreatedAsEnabled()
        => Assert.That(MainWindowViewModel.ShouldShowBuildOutput(null), Is.True);

    // ---- build.defaultConfiguration ----

    private static readonly string[] KnownConfigs = { "Debug", "Release" };

    [Test]
    public void DefaultConfiguration_DefaultIsDebug()
        => Assert.That(MainWindowViewModel.ResolveDefaultBuildConfiguration(_service, KnownConfigs), Is.EqualTo("Debug"));

    [Test]
    public void DefaultConfiguration_ReleaseIsHonored()
    {
        _service.Set("build.defaultConfiguration", "Release");
        Assert.That(MainWindowViewModel.ResolveDefaultBuildConfiguration(_service, KnownConfigs), Is.EqualTo("Release"));
    }

    [Test]
    public void DefaultConfiguration_IsCaseInsensitive_AndReturnsCanonicalCasing()
    {
        _service.Set("build.defaultConfiguration", "release");
        Assert.That(MainWindowViewModel.ResolveDefaultBuildConfiguration(_service, KnownConfigs), Is.EqualTo("Release"));
    }

    [Test]
    public void DefaultConfiguration_UnknownValue_FallsBackToDebug()
    {
        _service.Set("build.defaultConfiguration", "Bogus");
        Assert.That(MainWindowViewModel.ResolveDefaultBuildConfiguration(_service, KnownConfigs), Is.EqualTo("Debug"));
    }

    [Test]
    public void DefaultConfiguration_NullService_FallsBackToDebug()
        => Assert.That(MainWindowViewModel.ResolveDefaultBuildConfiguration(null, KnownConfigs), Is.EqualTo("Debug"));

    [Test]
    public void DefaultConfiguration_NoDebugInList_UsesFirstKnown()
    {
        _service.Set("build.defaultConfiguration", "Bogus");
        Assert.That(MainWindowViewModel.ResolveDefaultBuildConfiguration(_service, new[] { "Staging", "Prod" }),
            Is.EqualTo("Staging"));
    }

    // ---- basiclang.lsp.autoStart ----

    [Test]
    public void AutoStartLsp_DefaultEnabled()
        => Assert.That(MainWindowViewModel.ShouldAutoStartLanguageServer(_service), Is.True);

    [Test]
    public void AutoStartLsp_WhenDisabled_ReturnsFalse()
    {
        _service.Set("basiclang.lsp.autoStart", false);
        Assert.That(MainWindowViewModel.ShouldAutoStartLanguageServer(_service), Is.False);
    }

    [Test]
    public void AutoStartLsp_NullService_TreatedAsEnabled()
        => Assert.That(MainWindowViewModel.ShouldAutoStartLanguageServer(null), Is.True);

    // ---- basiclang.compiler.backend → default TargetBackend mapping ----

    [Test]
    public void ResolveDefaultBackend_MapsEachSchemaValue()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProjectService.ResolveDefaultBackend("CSharp"), Is.EqualTo(TargetBackend.CSharp));
            Assert.That(ProjectService.ResolveDefaultBackend("MSIL"), Is.EqualTo(TargetBackend.MSIL));
            Assert.That(ProjectService.ResolveDefaultBackend("LLVM"), Is.EqualTo(TargetBackend.LLVM));
            Assert.That(ProjectService.ResolveDefaultBackend("CPP"), Is.EqualTo(TargetBackend.Cpp));
        });
    }

    [Test]
    public void ResolveDefaultBackend_IsCaseInsensitive_AndAcceptsAliases()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProjectService.ResolveDefaultBackend("cpp"), Is.EqualTo(TargetBackend.Cpp));
            Assert.That(ProjectService.ResolveDefaultBackend("c++"), Is.EqualTo(TargetBackend.Cpp));
            Assert.That(ProjectService.ResolveDefaultBackend("c#"), Is.EqualTo(TargetBackend.CSharp));
            Assert.That(ProjectService.ResolveDefaultBackend(" llvm "), Is.EqualTo(TargetBackend.LLVM));
        });
    }

    [Test]
    public void ResolveDefaultBackend_UnknownEmptyOrNull_FallsBackToCSharp()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProjectService.ResolveDefaultBackend("nonsense"), Is.EqualTo(TargetBackend.CSharp));
            Assert.That(ProjectService.ResolveDefaultBackend(""), Is.EqualTo(TargetBackend.CSharp));
            Assert.That(ProjectService.ResolveDefaultBackend("   "), Is.EqualTo(TargetBackend.CSharp));
            Assert.That(ProjectService.ResolveDefaultBackend(null), Is.EqualTo(TargetBackend.CSharp));
        });
    }

    // ---- .blproj load: per-project value wins; omit uses the IDE default ----

    [Test]
    public async Task ProjectLoad_ExplicitBackendWins_OverDefault()
    {
        var path = WriteProjectXml(includeBackend: "Cpp");
        var project = await new ProjectSerializer().LoadAsync(path, TargetBackend.LLVM);
        Assert.That(project.TargetBackend, Is.EqualTo(TargetBackend.Cpp),
            "an explicit <TargetBackend> in the file must beat the IDE default");
    }

    [Test]
    public async Task ProjectLoad_OmittedBackend_UsesProvidedDefault()
    {
        var path = WriteProjectXml(includeBackend: null);
        var project = await new ProjectSerializer().LoadAsync(path, TargetBackend.LLVM);
        Assert.That(project.TargetBackend, Is.EqualTo(TargetBackend.LLVM),
            "a .blproj omitting <TargetBackend> takes the IDE-configured default");
    }

    [Test]
    public async Task ProjectLoad_OmittedBackend_NoDefault_StaysCSharp()
    {
        var path = WriteProjectXml(includeBackend: null);
        var project = await new ProjectSerializer().LoadAsync(path);
        Assert.That(project.TargetBackend, Is.EqualTo(TargetBackend.CSharp),
            "with no IDE default supplied the model default (CSharp) stands");
    }

    // ---- CreateProjectAsync writes the default backend so BOTH entry points honor it ----

    [Test]
    public async Task CreateProject_WritesSettingBackend_ReadableByIdeAndCli()
    {
        _service.Set("basiclang.compiler.backend", "CPP");

        var fileService = new Mock<IFileService>();
        fileService.Setup(f => f.CreateDirectoryAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        fileService.Setup(f => f.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<System.Threading.CancellationToken>()))
            .Returns(Task.CompletedTask);

        var svc = new ProjectService(fileService.Object, _service);
        var project = await svc.CreateProjectAsync("BackendProj", _homeDir, ProjectTemplateKind.ConsoleApplication);

        // 1) The in-memory project picked up the setting default.
        Assert.That(project.TargetBackend, Is.EqualTo(TargetBackend.Cpp));

        // 2) It was persisted into the .blproj (SaveAsync writes real file IO).
        var blproj = project.FilePath;
        Assert.That(File.Exists(blproj), Is.True);
        var xml = await File.ReadAllTextAsync(blproj);
        Assert.That(xml, Does.Contain("<TargetBackend>Cpp</TargetBackend>"));

        // 3) IDE entry point re-reads it as Cpp.
        var ideReload = await new ProjectSerializer().LoadAsync(blproj);
        Assert.That(ideReload.TargetBackend, Is.EqualTo(TargetBackend.Cpp), "IDE parse honors the written backend");

        // 4) CLI entry point (the compiler's own ProjectFile loader) reads the same <TargetBackend>.
        var cliReload = BasicLang.Compiler.ProjectSystem.ProjectFile.Load(blproj);
        Assert.That(cliReload.Backend, Is.EqualTo("Cpp"), "CLI parse honors the written backend");
    }

    // ---- basiclang.lsp.path override validation ----

    [Test]
    public void LspPathOverride_EmptyOrWhitespace_ReturnsNull()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LanguageService.ResolveLspPathOverride(null), Is.Null);
            Assert.That(LanguageService.ResolveLspPathOverride(""), Is.Null);
            Assert.That(LanguageService.ResolveLspPathOverride("   "), Is.Null);
        });
    }

    [Test]
    public void LspPathOverride_NonExistentFile_ReturnsNull()
        => Assert.That(LanguageService.ResolveLspPathOverride(@"C:\definitely\not\here\basiclang.dll"), Is.Null);

    [Test]
    public void LspPathOverride_ExistingFile_ReturnsTrimmedPath()
    {
        var file = Path.Combine(_homeDir, "fake-lsp.dll");
        File.WriteAllText(file, "stub");
        Assert.That(LanguageService.ResolveLspPathOverride("  " + file + "  "), Is.EqualTo(file),
            "an override that names an existing file is used (trimmed)");
    }

    [Test]
    public void LspPathOverride_InjectableFileExists_IsHonored()
    {
        // Pure resolution with an injected existence predicate (no disk).
        Assert.That(LanguageService.ResolveLspPathOverride("X:/custom/basiclang.dll", _ => true),
            Is.EqualTo("X:/custom/basiclang.dll"));
        Assert.That(LanguageService.ResolveLspPathOverride("X:/custom/basiclang.dll", _ => false), Is.Null);
    }

    // ---- Consumer registry ----

    [Test]
    public void BuildAndLspConsumers_AreNamed_AfterRegistrationSeamRuns()
    {
        // The build.* + basiclang.lsp.autoStart keys register from the (heavy) MainWindowViewModel
        // ctor; the extracted static seam lets the contract be pinned headlessly.
        MainWindowViewModel.RegisterBuildAndLspSettingsConsumers();

        // basiclang.compiler.backend registers in ProjectService's ctor; construct one to force it.
        _ = new ProjectService(new Mock<IFileService>().Object, _service);

        // basiclang.lsp.path registers in LanguageService's ctor; construct one to force it.
        _ = new LanguageService(new Mock<IOutputService>().Object, _service);

        foreach (var key in new[]
        {
            "build.saveBeforeBuild",
            "build.showOutput",
            "build.defaultConfiguration",
            "basiclang.compiler.backend",
            "basiclang.lsp.path",
            "basiclang.lsp.autoStart",
        })
        {
            Assert.That(SettingsConsumerRegistry.IsRegistered(key), Is.True,
                $"{key} must name a consumer in SettingsConsumerRegistry (wired in Task 2.4)");
        }
    }

    private string WriteProjectXml(string? includeBackend)
    {
        var backendElement = includeBackend != null ? $"    <TargetBackend>{includeBackend}</TargetBackend>\n" : "";
        var xml =
            "<BasicLangProject Version=\"1.0\">\n" +
            "  <PropertyGroup>\n" +
            "    <ProjectName>Sample</ProjectName>\n" +
            "    <OutputType>Exe</OutputType>\n" +
            "    <RootNamespace>Sample</RootNamespace>\n" +
            backendElement +
            "  </PropertyGroup>\n" +
            "</BasicLangProject>\n";
        var path = Path.Combine(_homeDir, $"proj_{Guid.NewGuid():N}.blproj");
        File.WriteAllText(path, xml);
        return path;
    }
}
