using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Editor.Controls;
using VisualGameStudio.ProjectSystem.Services;
using VisualGameStudio.Shell;
using VisualGameStudio.Shell.Dock;
using VisualGameStudio.Shell.ViewModels;
using VisualGameStudio.Shell.ViewModels.Dialogs;
using VisualGameStudio.Shell.ViewModels.Documents;
using VisualGameStudio.Shell.ViewModels.Panels;
using VisualGameStudio.Shell.Views.Documents;

namespace VisualGameStudio.Tests;

/// <summary>
/// Phase 3 regression net — the settings-consumer contract.
///
/// <para>The whole plan exists to kill the "persists-but-dead" class of defect: a setting that the
/// Tools → Settings dialog exposes and persists, but which no code actually reads and acts on. This
/// fixture makes that class of defect a build failure: for <b>every</b> key the dialog manages
/// (enumerated the same way the dialog builds its inventory), a real consumer must have named itself
/// in <see cref="SettingsConsumerRegistry"/>.</para>
///
/// <para><b>What failure looks like.</b> Someone adds a <c>MakeBool/MakeCombo/...</c> entry to
/// <c>SettingsViewModel.BuildSearchableSettings</c> (so it shows up as an editable control) but
/// forgets to wire a consumer + its one-line <c>RegisterConsumer</c> call. The new key appears in
/// <see cref="SettingsViewModel.DialogSettingKeys"/> with no registry entry, and
/// <see cref="EveryDialogSettingKey_HasARegisteredConsumer"/> fails naming that exact key.</para>
///
/// <para><b>Lazy-init discipline (see the SettingsConsumerRegistry class doc).</b> A consumer only
/// registers when its type/instance initializer actually runs. In a headless test the offending
/// type may never have been loaded, so it would look unregistered even though its wiring is fine.
/// <see cref="ForceAllDialogConsumers"/> therefore forces every registrant first — static-ctor
/// registrants via <see cref="RuntimeHelpers.RunClassConstructor"/>, instance-ctor registrants by
/// constructing a cheap instance, and the two heavy/static seams by direct call — using the exact
/// recipes each group's own Task 2.x wiring test already proved works headlessly.</para>
///
/// <para>The registry is process-wide and append-only, so forcing once (OneTimeSetUp) is enough and
/// cannot be undone by other tests; this fixture never depends on another test having run.</para>
/// </summary>
[TestFixture]
public class SettingsConsumerContractTests
{
    private string _forceHome = null!;

    /// <summary>
    /// Forces every consumer that backs a dialog setting to register itself, then leaves the process
    /// registry populated for the assertions below. Runs once — the registry is append-only.
    /// </summary>
    [OneTimeSetUp]
    public void ForceAllDialogConsumers()
    {
        _forceHome = Path.Combine(Path.GetTempPath(), $"SettingsConsumerContract_{Guid.NewGuid()}");
        Directory.CreateDirectory(_forceHome);
        using var service = new SettingsService(_forceHome);

        // ---- Static-ctor registrants: force the type initializer (registration is its only side
        // effect — no Avalonia app is required, same as the Task 2.1/2.2/2.3 wiring tests). ----
        RuntimeHelpers.RunClassConstructor(typeof(CodeEditorDocumentView).TypeHandle); // 14 editor.* keys
        RuntimeHelpers.RunClassConstructor(typeof(ThemeManager).TypeHandle);           // workbench.colorTheme
        RuntimeHelpers.RunClassConstructor(typeof(CodeEditorControl).TypeHandle);      // intellisense.* keys
        RuntimeHelpers.RunClassConstructor(typeof(TerminalViewModel).TypeHandle);      // terminal.integrated.* keys

        // ---- Static seams on the (too-heavy-to-construct) MainWindowViewModel. ----
        MainWindowViewModel.RegisterBuildAndLspSettingsConsumers();  // build.* + basiclang.lsp.autoStart
        MainWindowViewModel.RegisterEditorSaveSettingsConsumers();   // editor.formatOnSave + trimTrailingWhitespaceOnSave

        // ---- Static-class registrants: call the method that performs the read. ----
        _ = ClangdLocator.Locate(service);                           // cpp.clangd.path
        new VisualGameStudio.ProjectSystem.Services.CppToolchainOverrides(service).RegisterAllConsumers(); // six cpp.toolchain.* keys

        // ---- Instance-ctor registrants: construct a cheap instance (mocks for the deps). ----
        _ = new DockFactory(service);                                   // workbench.startupEditor + sideBar.location
        _ = new ProjectService(new Mock<IFileService>().Object, service);   // basiclang.compiler.backend
        _ = new LanguageService(new Mock<IOutputService>().Object, service); // basiclang.lsp.path
        _ = new AutoSaveService(service);                              // files.autoSave + files.autoSaveDelay

        var recent = new Mock<IRecentProjectsService>();
        recent.Setup(r => r.GetRecentProjects()).Returns(new List<RecentProjectInfo>());
        _ = new WelcomeDocumentViewModel(recent.Object, service);      // (co-)registers workbench.startupEditor

        var git = new Mock<IGitService>();
        var gitVm = new GitChangesViewModel(git.Object, new Mock<IDialogService>().Object,
            new Mock<IOutputService>().Object, service);               // git.confirmSync
        gitVm.IsAutoRefreshEnabled = false;                            // disarm the refresh timer

        var autoFetch = new GitAutoFetchService(new Mock<IGitService>().Object, service); // git.autoFetch*
        autoFetch.Dispose();                                           // disarm the fetch timer
    }

    [OneTimeTearDown]
    public void Cleanup()
    {
        try { if (Directory.Exists(_forceHome)) Directory.Delete(_forceHome, true); }
        catch { /* ignore cleanup errors */ }
    }

    /// <summary>Builds a dialog VM against a fresh temp store and returns its managed keys.</summary>
    private static IReadOnlyList<string> DialogKeys(out SettingsService service, out string home)
    {
        home = Path.Combine(Path.GetTempPath(), $"SettingsConsumerContract_inv_{Guid.NewGuid()}");
        Directory.CreateDirectory(home);
        service = new SettingsService(home);
        return new SettingsViewModel(service).DialogSettingKeys;
    }

    // ---- Positive control: the seam actually enumerates a non-trivial inventory. ----

    [Test]
    public void DialogInventory_IsNonEmpty_AndIncludesKnownKeys()
    {
        var keys = DialogKeys(out var service, out var home);
        try
        {
            Assert.That(keys.Count, Is.GreaterThan(30),
                "the dialog manages dozens of keys; a near-empty inventory means the seam broke and the " +
                "contract below would vacuously pass");
            Assert.That(keys, Does.Contain("editor.fontSize"));
            Assert.That(keys, Does.Contain("workbench.colorTheme"));
            Assert.That(keys, Is.Unique, "each dialog key must be listed once");
        }
        finally
        {
            service.Dispose();
            try { Directory.Delete(home, true); } catch { }
        }
    }

    // ---- The contract: every dialog setting names a real consumer. ----

    [Test]
    public void EveryDialogSettingKey_HasARegisteredConsumer()
    {
        var keys = DialogKeys(out var service, out var home);
        try
        {
            var unregistered = keys.Where(k => !SettingsConsumerRegistry.IsRegistered(k)).ToList();

            Assert.That(unregistered, Is.Empty,
                "Every setting the Tools → Settings dialog exposes must name a consumer in " +
                "SettingsConsumerRegistry (add the wiring + a one-line RegisterConsumer at the " +
                "consumer, or remove the setting from the dialog per D3). Dialog keys with no " +
                "consumer: " + string.Join(", ", unregistered));
        }
        finally
        {
            service.Dispose();
            try { Directory.Delete(home, true); } catch { }
        }
    }

    // ---- Inverse sanity: every dialog-managed key exists in the settings schema. ----
    // Catches a dialog entry whose key is a typo / drifted from the schema (it would persist under a
    // key nothing else knows about, and its scope/type metadata would be missing).

    [Test]
    public void EveryDialogSettingKey_ExistsInSchema()
    {
        var keys = DialogKeys(out var service, out var home);
        try
        {
            var missingFromSchema = keys.Where(k => service.GetPropertySchema(k) == null).ToList();

            Assert.That(missingFromSchema, Is.Empty,
                "Every dialog-managed key must be defined in the settings schema (SettingsService." +
                "RegisterAllDefaultSchemas). Dialog keys absent from the schema: " +
                string.Join(", ", missingFromSchema));
        }
        finally
        {
            service.Dispose();
            try { Directory.Delete(home, true); } catch { }
        }
    }

    // ---- Guard: the four D3-removed keys stay out of the dialog (they are dead as settings). ----
    // Regression pin — re-adding any of these to the dialog without wiring a real consumer would (and
    // should) trip EveryDialogSettingKey_HasARegisteredConsumer; this makes the intent explicit.

    [Test]
    public void D3RemovedKeys_AreNotInTheDialogInventory()
    {
        var keys = DialogKeys(out var service, out var home);
        try
        {
            foreach (var dead in new[]
            {
                "editor.minimap.side",
                "editor.autoIndent",
                "debug.console.fontSize",
                "debug.allowBreakpointsEverywhere",
            })
            {
                Assert.That(keys, Does.Not.Contain(dead),
                    $"{dead} has no behavioral consumer and was removed from the dialog (D3); it must " +
                    "not reappear without real wiring");
            }
        }
        finally
        {
            service.Dispose();
            try { Directory.Delete(home, true); } catch { }
        }
    }
}
