using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels;

namespace VisualGameStudio.Tests;

/// <summary>
/// Pins Task 15 — the toast auto-dismiss override + per-toast duration seams added to
/// <see cref="MainWindowViewModel"/> / <see cref="NotificationEventArgs"/> so callers (Task 16's
/// quiet build toasts) can force auto-dismiss regardless of severity and set a shorter/longer
/// on-screen duration, without changing behavior for any existing caller.
///
/// <see cref="MainWindowViewModel"/> is DI-only and is never constructed in this suite (~40
/// services; see SettingsConsumerContractTests.cs "too-heavy-to-construct"). These tests exercise
/// only the pure static seam (<see cref="MainWindowViewModel.ComputeToastAutoDismiss"/>) and the
/// public <see cref="NotificationEventArgs"/> data class directly — never the view-model itself.
/// </summary>
[TestFixture]
public class ToastAutoDismissTests
{
    // ---- ComputeToastAutoDismiss: overrideFlag wins outright; null falls back to info&&noActions ----

    [Test]
    public void ComputeToastAutoDismiss_NullOverride_InfoNoActions_True()
        => Assert.That(MainWindowViewModel.ComputeToastAutoDismiss("info", 0, null), Is.True);

    [Test]
    public void ComputeToastAutoDismiss_NullOverride_ErrorSeverity_False()
        => Assert.That(MainWindowViewModel.ComputeToastAutoDismiss("error", 0, null), Is.False);

    [Test]
    public void ComputeToastAutoDismiss_NullOverride_InfoWithActions_False()
        => Assert.That(MainWindowViewModel.ComputeToastAutoDismiss("info", 2, null), Is.False);

    [Test]
    public void ComputeToastAutoDismiss_OverrideTrue_WinsOverErrorSeverity_True()
        => Assert.That(MainWindowViewModel.ComputeToastAutoDismiss("error", 3, true), Is.True);

    [Test]
    public void ComputeToastAutoDismiss_OverrideFalse_WinsOverInfo_False()
        => Assert.That(MainWindowViewModel.ComputeToastAutoDismiss("info", 0, false), Is.False);

    // ---- NotificationEventArgs.DismissAfterSeconds: null default (5s IDE fallback), round-trips ----

    [Test]
    public void NotificationEventArgs_DismissAfterSeconds_DefaultsNull()
    {
        var args = new NotificationEventArgs("hello", "info");
        Assert.That(args.DismissAfterSeconds, Is.Null);
    }

    [Test]
    public void NotificationEventArgs_DismissAfterSeconds_RoundTrips()
    {
        var args = new NotificationEventArgs(
            "hello", "info", null, true, null, 0, false, false, null, dismissAfterSeconds: 1.5);
        Assert.That(args.DismissAfterSeconds, Is.EqualTo(1.5));
    }

    // ---- ComposeBuildToast (Task 16 — the quiet-build kill list): both outcomes are quiet ----
    // Pure static seam, same rationale as ComputeToastAutoDismiss above: pinned headlessly without
    // constructing the DI-only MainWindowViewModel.

    [Test]
    public void ComposeBuildToast_Success_IsQuiet()
    {
        var spec = MainWindowViewModel.ComposeBuildToast(succeeded: true, errorCount: 0, elapsedSeconds: 1.2);

        Assert.That(spec.AutoDismiss, Is.True);
        Assert.That(spec.DismissAfterSeconds, Is.EqualTo(MainWindowViewModel.QuietBuildToastSeconds));
        Assert.That(spec.DismissAfterSeconds, Is.EqualTo(3));
        Assert.That(spec.Severity, Is.EqualTo("info").Or.EqualTo("success"));
        Assert.That(spec.Message, Does.Contain("1.2"));
    }

    [Test]
    public void ComposeBuildToast_Failure_IsQuiet()
    {
        var spec = MainWindowViewModel.ComposeBuildToast(succeeded: false, errorCount: 7, elapsedSeconds: 0.8);

        Assert.That(spec.AutoDismiss, Is.True);
        Assert.That(spec.DismissAfterSeconds, Is.EqualTo(MainWindowViewModel.QuietBuildToastSeconds));
        Assert.That(spec.Message, Does.Contain("7"));
    }

    // ---- Source guards (Task 16 — the quiet-build kill list) ----
    // MainWindowViewModel is DI-only and never constructed in this suite; these guards read the
    // source text directly, mirroring NewProjectWizardSwapGuardTests.cs's FindRepoFile pattern.

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

    /// <summary>Extracts a method's full body (braces included) by brace-depth scanning, so the
    /// guard doesn't depend on which method happens to follow it in the file.</summary>
    private static string ExtractMethodBody(string src, string methodSignatureNeedle)
    {
        var startIdx = src.IndexOf(methodSignatureNeedle, StringComparison.Ordinal);
        Assert.That(startIdx, Is.GreaterThanOrEqualTo(0), $"'{methodSignatureNeedle}' not found in source.");

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
    public void MainWindowSource_FailedBuild_DoesNotActivateErrorList()
    {
        var src = ReadMainWindowViewModelSource();
        if (src == null) return;

        var body = ExtractMethodBody(src, "private void OnBuildCompleted(");

        Assert.That(body, Does.Not.Contain("ActivateTool(\"ErrorList\")"),
            "OnBuildCompleted must not seize focus to the ErrorList panel on a failed build (Task 16) " +
            "— the quiet toast + Output pane already report it.");

        // Population (Problems badge / status-bar counts feed) must still happen.
        Assert.That(body, Does.Contain("SetBuildDiagnostics"),
            "OnBuildCompleted must still populate build diagnostics into the aggregator.");
        Assert.That(body, Does.Contain("UpdateDiagnostics"),
            "OnBuildCompleted must still push the updated snapshot into the ErrorList view-model.");
    }

    [Test]
    public void MainWindowSource_NoBuildFailedMessageBox()
    {
        var src = ReadMainWindowViewModelSource();
        if (src == null) return;

        Assert.That(src, Does.Not.Contain("\"Build Failed\""),
            "The F5 'Build Failed' modal must be removed (Task 16) — the failure toast + Output " +
            "pane already report it, and nothing consumes the dialog's result.");
    }
}
