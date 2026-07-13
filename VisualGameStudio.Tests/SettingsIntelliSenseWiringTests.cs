using System;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Editor.Controls;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests;

/// <summary>
/// Pins Task 2.3 — the IntelliSense settings group wired through <see cref="ISettingsService"/>,
/// read at point-of-use so each toggle is live:
///
/// * <c>intellisense.autoComplete</c> gates the auto (word/member) completion triggers while leaving
///   explicit Ctrl+Space (Invoked) working;
/// * <c>intellisense.signatureHelp</c> gates the ( / , signature-help trigger;
/// * <c>intellisense.delay</c> drives the completion debounce interval (was a hard 120 ms const),
///   clamped to 0..2000 with the schema default 200;
/// * <c>intellisense.quickInfo</c> gates the LSP hover request (asserted here only at the registry
///   level — the gate lives in the MainWindowViewModel hover handler and is code-trace + boot-smoke
///   verified, since MainWindowViewModel needs ~40 services to construct);
/// * every IntelliSense consumer names itself in the <see cref="SettingsConsumerRegistry"/>.
///
/// The gating call sites (TriggerCompletion / TriggerSignatureHelp / RestartCompletionDebounce /
/// onHover) are UI/LSP-bound; the pure resolution seams on <see cref="CodeEditorControl"/> — the
/// part that can silently drift (key names, defaults, clamp) — are pinned here.
/// </summary>
[TestFixture]
public class SettingsIntelliSenseWiringTests
{
    private string _homeDir = null!;
    private SettingsService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _homeDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"SettingsIntelliSenseWiring_{Guid.NewGuid()}");
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

    // ---- intellisense.autoComplete ----

    [Test]
    public void AutoComplete_DefaultEnabled()
    {
        Assert.That(CodeEditorControl.IsAutoCompleteEnabled(_service), Is.True,
            "intellisense.autoComplete defaults to true (schema)");
    }

    [Test]
    public void AutoComplete_WhenDisabled_ReturnsFalse()
    {
        _service.Set("intellisense.autoComplete", false);
        Assert.That(CodeEditorControl.IsAutoCompleteEnabled(_service), Is.False);
    }

    [Test]
    public void AutoComplete_NullService_TreatedAsEnabled()
    {
        Assert.That(CodeEditorControl.IsAutoCompleteEnabled(null), Is.True);
    }

    // ---- intellisense.signatureHelp ----

    [Test]
    public void SignatureHelp_DefaultEnabled()
    {
        Assert.That(CodeEditorControl.IsSignatureHelpEnabled(_service), Is.True);
    }

    [Test]
    public void SignatureHelp_WhenDisabled_ReturnsFalse()
    {
        _service.Set("intellisense.signatureHelp", false);
        Assert.That(CodeEditorControl.IsSignatureHelpEnabled(_service), Is.False);
    }

    [Test]
    public void SignatureHelp_NullService_TreatedAsEnabled()
    {
        Assert.That(CodeEditorControl.IsSignatureHelpEnabled(null), Is.True);
    }

    // ---- intellisense.delay ----

    [Test]
    public void CompletionDelay_DefaultIsSchema200()
    {
        Assert.That(CodeEditorControl.ResolveCompletionDelayMs(_service), Is.EqualTo(200),
            "unset intellisense.delay resolves to the schema default 200");
    }

    [Test]
    public void CompletionDelay_CustomValue_IsHonored()
    {
        _service.Set("intellisense.delay", 500);
        Assert.That(CodeEditorControl.ResolveCompletionDelayMs(_service), Is.EqualTo(500));
    }

    [Test]
    public void CompletionDelay_ClampsToRange()
    {
        _service.Set("intellisense.delay", 5000);
        Assert.That(CodeEditorControl.ResolveCompletionDelayMs(_service), Is.EqualTo(2000));

        _service.Set("intellisense.delay", -10);
        Assert.That(CodeEditorControl.ResolveCompletionDelayMs(_service), Is.EqualTo(0));
    }

    [Test]
    public void CompletionDelay_NullService_FallsBackToBuiltIn120()
    {
        Assert.That(CodeEditorControl.ResolveCompletionDelayMs(null), Is.EqualTo(120),
            "with no service the debounce keeps its built-in 120 ms");
    }

    // ---- Consumer registry: every wired intellisense.* key names its consumer ----

    [Test]
    public void IntelliSenseSettings_AllNameAConsumer_AfterTypeInitializes()
    {
        // Force the CodeEditorControl type initializer (it only calls RegisterConsumer — no Avalonia
        // app is needed), the same discipline the Phase 3 contract test uses.
        RuntimeHelpers.RunClassConstructor(typeof(CodeEditorControl).TypeHandle);

        foreach (var key in new[]
        {
            "intellisense.autoComplete",
            "intellisense.quickInfo",
            "intellisense.signatureHelp",
            "intellisense.delay",
        })
        {
            Assert.That(SettingsConsumerRegistry.IsRegistered(key), Is.True,
                $"{key} must name a consumer in SettingsConsumerRegistry (wired in Task 2.3)");
        }
    }
}
