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
}
