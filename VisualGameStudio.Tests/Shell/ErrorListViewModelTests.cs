using NUnit.Framework;
using VisualGameStudio.Core.Models;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// The Error List count badges and the visible rows obey ONE bucketing contract:
///  - counts are always totals per severity bucket (VS-style — a nonzero count
///    stays visible even when its filter is unchecked), and
///  - every diagnostic falls into exactly one of the three toggle buckets
///    (Errors / Warnings / Messages), so a diagnostic is never shown-but-uncounted
///    nor counted-but-ungoverned-by-a-toggle.
/// Previously MessageCount counted only Info while the filter showed any other
/// severity (Hidden) unconditionally, so count and rows disagreed.
/// </summary>
[TestFixture]
public class ErrorListViewModelTests
{
    private static DiagnosticItem Diag(DiagnosticSeverity severity) =>
        new() { Severity = severity, Message = severity.ToString() };

    [Test]
    public void MessageCount_IncludesEveryNonErrorNonWarningSeverity()
    {
        var vm = new ErrorListViewModel();

        vm.ApplyDiagnostics(new[] { Diag(DiagnosticSeverity.Info), Diag(DiagnosticSeverity.Hidden) });

        Assert.That(vm.MessageCount, Is.EqualTo(2),
            "the Messages bucket must count every non-Error, non-Warning diagnostic (Info, Hidden, ...)");
        Assert.That(vm.ErrorCount, Is.EqualTo(0));
        Assert.That(vm.WarningCount, Is.EqualTo(0));
    }

    [Test]
    public void ShowMessagesFalse_HidesEveryMessageBucketRow_IncludingHidden()
    {
        var vm = new ErrorListViewModel();
        vm.ApplyDiagnostics(new[] { Diag(DiagnosticSeverity.Info), Diag(DiagnosticSeverity.Hidden) });

        vm.ShowMessages = false;

        Assert.That(vm.Diagnostics, Is.Empty,
            "unchecking Messages must hide Info AND Hidden rows — no severity is shown unconditionally");
        Assert.That(vm.MessageCount, Is.EqualTo(2),
            "the count stays a total (VS-style) even while its rows are filtered out");
    }

    [Test]
    public void Counts_AreTotals_RegardlessOfFilters()
    {
        var vm = new ErrorListViewModel();
        vm.ApplyDiagnostics(new[]
        {
            Diag(DiagnosticSeverity.Error),
            Diag(DiagnosticSeverity.Warning),
            Diag(DiagnosticSeverity.Info),
        });

        vm.ShowErrors = false;
        vm.ShowWarnings = false;
        vm.ShowMessages = false;

        Assert.That(vm.ErrorCount, Is.EqualTo(1));
        Assert.That(vm.WarningCount, Is.EqualTo(1));
        Assert.That(vm.MessageCount, Is.EqualTo(1));
        Assert.That(vm.Diagnostics, Is.Empty, "all filters off hides all rows");
    }

    [Test]
    public void ShowErrorsFalse_FiltersOnlyErrorRows_CountStillReported()
    {
        var vm = new ErrorListViewModel();
        vm.ApplyDiagnostics(new[]
        {
            Diag(DiagnosticSeverity.Error),
            Diag(DiagnosticSeverity.Warning),
        });

        vm.ShowErrors = false;

        Assert.That(vm.Diagnostics.Count, Is.EqualTo(1), "only the warning row remains visible");
        Assert.That(vm.Diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        Assert.That(vm.ErrorCount, Is.EqualTo(1), "the error total is still reported next to its toggle");
    }
}
