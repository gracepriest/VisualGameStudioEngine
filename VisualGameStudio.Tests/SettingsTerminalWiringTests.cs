using System;
using System.Linq;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;
using VisualGameStudio.Shell.ViewModels.Dialogs;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Tests;

/// <summary>
/// Pins Task 2.2 — the Terminal settings group wired through <see cref="ISettingsService"/>:
///
/// * <c>terminal.integrated.fontFamily</c> / <c>fontSize</c> resolve onto the bound VM font
///   properties, inheriting the editor font when the terminal family is empty and clamping the size
///   to the schema's 6..72 range (default 14 — the old hardcoded 13 didn't even match it);
/// * <c>terminal.integrated.defaultProfile</c> reads through the service (was raw %APPDATA% JSON),
///   honoring the VS Code key precedence (platform-specific → generic → legacy);
/// * the D3 dialog removal of <c>terminal.integrated.cursorStyle</c> (key stays in the schema);
/// * every terminal consumer names itself in the <see cref="SettingsConsumerRegistry"/>.
///
/// The font application onto the AXAML SelectableTextBlock / TextBox and the profile re-resolve on
/// live SettingChanged are UI-bound and covered by code-trace + build + the boot smoke; the pure
/// resolution seams (<see cref="TerminalViewModel.ResolveTerminalFont"/> /
/// <see cref="TerminalViewModel.ResolveSavedShellName"/>) — the part that can silently drift — are
/// pinned here.
/// </summary>
[TestFixture]
public class SettingsTerminalWiringTests
{
    private string _homeDir = null!;
    private SettingsService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _homeDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"SettingsTerminalWiring_{Guid.NewGuid()}");
        System.IO.Directory.CreateDirectory(_homeDir);
        _service = new SettingsService(_homeDir);
    }

    [TearDown]
    public void TearDown()
    {
        try { _service.Dispose(); } catch { /* ignore */ }
        try { if (System.IO.Directory.Exists(_homeDir)) System.IO.Directory.Delete(_homeDir, true); }
        catch { /* ignore cleanup errors */ }
    }

    private static string PlatformProfileKey =>
        Environment.OSVersion.Platform == PlatformID.Win32NT
            ? "terminal.integrated.defaultProfile.windows"
            : "terminal.integrated.defaultProfile.linux";

    // ---- Font resolution ----

    [Test]
    public void TerminalFont_Unset_DefaultsToEditorFontAndSchemaSize()
    {
        var (family, size) = TerminalViewModel.ResolveTerminalFont(_service);

        // terminal.integrated.fontFamily default is "" -> inherits editor.fontFamily ("Cascadia Code").
        Assert.That(family, Is.EqualTo("Cascadia Code"));
        Assert.That(size, Is.EqualTo(14), "schema default font size is 14 (the old hardcoded 13 was wrong)");
    }

    [Test]
    public void TerminalFont_ExplicitTerminalFont_Wins()
    {
        _service.Set("terminal.integrated.fontFamily", "Fira Code");
        _service.Set("terminal.integrated.fontSize", 20);

        var (family, size) = TerminalViewModel.ResolveTerminalFont(_service);

        Assert.That(family, Is.EqualTo("Fira Code"));
        Assert.That(size, Is.EqualTo(20));
    }

    [Test]
    public void TerminalFont_EmptyTerminalFamily_FallsBackToEditorFamily()
    {
        _service.Set("editor.fontFamily", "JetBrains Mono");
        // terminal family left empty

        var (family, _) = TerminalViewModel.ResolveTerminalFont(_service);

        Assert.That(family, Is.EqualTo("JetBrains Mono"));
    }

    [Test]
    public void TerminalFont_SizeClampsToSchemaRange()
    {
        _service.Set("terminal.integrated.fontSize", 500);
        Assert.That(TerminalViewModel.ResolveTerminalFont(_service).size, Is.EqualTo(72));

        _service.Set("terminal.integrated.fontSize", 2);
        Assert.That(TerminalViewModel.ResolveTerminalFont(_service).size, Is.EqualTo(6));
    }

    [Test]
    public void TerminalFont_NullService_ReturnsMonospaceFallback()
    {
        var (family, size) = TerminalViewModel.ResolveTerminalFont(null);
        Assert.That(family, Is.EqualTo("Cascadia Code, Consolas, monospace"));
        Assert.That(size, Is.EqualTo(14));
    }

    // ---- Default profile resolution (VS Code key precedence) ----

    [Test]
    public void DefaultProfile_PlatformKeyWinsOverGeneric()
    {
        _service.Set(PlatformProfileKey, "PowerShell");
        _service.Set("terminal.integrated.defaultProfile", "Bash");

        Assert.That(TerminalViewModel.ResolveSavedShellName(_service), Is.EqualTo("PowerShell"),
            "the platform-specific key must take precedence over the generic key");
    }

    [Test]
    public void DefaultProfile_FallsBackToGenericKey()
    {
        _service.Set("terminal.integrated.defaultProfile", "Bash");
        Assert.That(TerminalViewModel.ResolveSavedShellName(_service), Is.EqualTo("Bash"));
    }

    [Test]
    public void DefaultProfile_FallsBackToLegacyDefaultShell()
    {
        _service.Set("Terminal.DefaultShell", "Git Bash");
        Assert.That(TerminalViewModel.ResolveSavedShellName(_service), Is.EqualTo("Git Bash"));
    }

    [Test]
    public void DefaultProfile_Unset_ReturnsEmpty()
    {
        Assert.That(TerminalViewModel.ResolveSavedShellName(_service), Is.EqualTo(""));
    }

    [Test]
    public void DefaultProfile_NullService_ReturnsEmpty()
    {
        Assert.That(TerminalViewModel.ResolveSavedShellName(null), Is.EqualTo(""));
    }

    // ---- D3 dialog removal (key stays in schema) ----

    [Test]
    public void TerminalCursorStyle_RemovedFromDialogInventory()
    {
        var vm = new SettingsViewModel(_service);
        vm.SearchText = "terminal.integrated.cursorStyle";
        var proxy = vm.FilteredSettings.FirstOrDefault(i => i.Key == "terminal.integrated.cursorStyle");

        Assert.That(proxy, Is.Null,
            "terminal.integrated.cursorStyle must not appear in the dialog (D3) — no terminal cursor-rendering surface exists");
    }

    // ---- Consumer registry: every wired terminal.* key names its consumer ----

    [Test]
    public void TerminalSettings_AllNameAConsumer_AfterTypeInitializes()
    {
        // Force the TerminalViewModel type initializer to run (it only calls RegisterConsumer — no
        // Avalonia app is needed), the same discipline the Phase 3 contract test uses.
        RuntimeHelpers.RunClassConstructor(typeof(TerminalViewModel).TypeHandle);

        foreach (var key in new[]
        {
            "terminal.integrated.fontFamily",
            "terminal.integrated.fontSize",
            "terminal.integrated.defaultProfile",
        })
        {
            Assert.That(SettingsConsumerRegistry.IsRegistered(key), Is.True,
                $"{key} must name a consumer in SettingsConsumerRegistry (wired in TerminalViewModel)");
        }
    }
}
