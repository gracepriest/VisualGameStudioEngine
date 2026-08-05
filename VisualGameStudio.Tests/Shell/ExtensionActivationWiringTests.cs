using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Guards the activation-event wiring. <c>IExtensionService.NotifyLanguageOpenedAsync</c> builds
/// <c>onLanguage:&lt;id&gt;</c> and dispatches it through <c>TriggerActivationEventAsync</c> — it
/// was fully implemented and called from nowhere. Opening a document only invoked
/// <c>NotifyDocumentOpenedAsync</c>, and only when the extension host was ALREADY running.
///
/// That produced a closed loop: the host starts when an extension with a JS entry point
/// activates; those extensions activate on onLanguage; and onLanguage was never fired. The
/// observable consequence was categorical — no extension with a "main" could ever activate, so
/// only purely static extensions (themes, grammars) worked. vscode.html-language-features
/// (main: htmlClientMain, activationEvents: onLanguage:html) loaded its manifest and then sat
/// inert forever.
///
/// MainWindowViewModel is DI-only and never constructed in this suite (~40 services), so these
/// are source guards, following BuildSolutionAmplifierGuardTests / NewProjectWizardSwapGuardTests.
/// </summary>
[TestFixture]
public class ExtensionActivationWiringTests
{
    private static string? FindRepoFile(params string[] relativeParts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    private static string? ReadMainWindowViewModelSource()
    {
        var path = FindRepoFile("VisualGameStudio.Shell", "ViewModels", "MainWindowViewModel.cs");
        if (path == null)
        {
            Assert.Ignore("MainWindowViewModel.cs not found from the test base directory — skipping source guard.");
            return null;
        }
        return File.ReadAllText(path);
    }

    /// <summary>Extracts a method body by brace-depth scanning.</summary>
    private static string ExtractMethodBody(string src, string signatureNeedle)
    {
        var startIdx = src.IndexOf(signatureNeedle, StringComparison.Ordinal);
        Assert.That(startIdx, Is.GreaterThanOrEqualTo(0), $"'{signatureNeedle}' not found in source.");

        var braceStart = src.IndexOf('{', startIdx);
        Assert.That(braceStart, Is.GreaterThan(startIdx), "Could not find the method body's opening brace.");

        var depth = 0;
        var i = braceStart;
        for (; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}')
            {
                depth--;
                if (depth == 0) break;
            }
        }
        Assert.That(i, Is.LessThan(src.Length), "Could not find the method body's closing brace.");
        return src.Substring(braceStart, i - braceStart + 1);
    }

    [Test]
    public void OpeningADocument_FiresTheOnLanguageActivationEvent()
    {
        var src = ReadMainWindowViewModelSource();
        if (src == null) return;

        Assert.That(src, Does.Contain("NotifyLanguageOpenedAsync("),
            "opening a document must fire onLanguage:<id>; without it no extension with a JS " +
            "entry point can ever activate, because nothing else starts the extension host.");
    }

    /// <summary>
    /// The ordering IS the bug. Firing onLanguage is what starts the host, so it must happen
    /// BEFORE any IsExtensionHostRunning test — otherwise the guard can never become true and
    /// the whole activation path is dead code.
    /// </summary>
    [Test]
    public void ActivationEvent_IsFiredBeforeTheHostRunningCheck()
    {
        var src = ReadMainWindowViewModelSource();
        if (src == null) return;

        var body = ExtractMethodBody(src, "private async Task ActivateExtensionsForDocumentAsync(");

        var notifyIdx = body.IndexOf("NotifyLanguageOpenedAsync(", StringComparison.Ordinal);
        var hostCheckIdx = body.IndexOf("IsExtensionHostRunning", StringComparison.Ordinal);

        Assert.That(notifyIdx, Is.GreaterThanOrEqualTo(0),
            "ActivateExtensionsForDocumentAsync must fire the onLanguage activation event.");
        Assert.That(hostCheckIdx, Is.GreaterThanOrEqualTo(0),
            "premise: the method still guards the document notification on the host being up.");
        Assert.That(notifyIdx, Is.LessThan(hostCheckIdx),
            "onLanguage must be fired BEFORE checking IsExtensionHostRunning — firing it is what " +
            "starts the host, so testing the flag first is a closed loop that can never open.");
    }

    /// <summary>
    /// The activation hop must not block the UI thread. This subsystem already produced one
    /// sync-over-async freeze (LoadContributions -> GetAwaiter().GetResult()); starting Node.js
    /// on the UI thread would be the same mistake with a much longer stall.
    /// </summary>
    [Test]
    public void ActivationHop_DoesNotBlockTheCallingThread()
    {
        var src = ReadMainWindowViewModelSource();
        if (src == null) return;

        var body = ExtractMethodBody(src, "private async Task ActivateExtensionsForDocumentAsync(");

        Assert.That(body, Does.Not.Contain(".Result"),
            "activation must not block — it may start Node.js and spawn language servers.");
        Assert.That(body, Does.Not.Contain("GetAwaiter().GetResult()"),
            "activation must not block — see the LoadContributions deadlock (dae3b82).");
        Assert.That(body, Does.Contain("catch"),
            "the call site is fire-and-forget, so the method must swallow its own exceptions " +
            "rather than surfacing them as unobserved task exceptions.");
    }
}
