using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.ViewModels;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.Shell.ViewModels.Panels;

public partial class ErrorListViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<DiagnosticItem> _diagnostics = new();

    [ObservableProperty]
    private DiagnosticItem? _selectedDiagnostic;

    [ObservableProperty]
    private bool _showErrors = true;

    [ObservableProperty]
    private bool _showWarnings = true;

    [ObservableProperty]
    private bool _showMessages = true;

    [ObservableProperty]
    private int _errorCount;

    [ObservableProperty]
    private int _warningCount;

    [ObservableProperty]
    private int _messageCount;

    private List<DiagnosticItem> _allDiagnostics = new();

    public event EventHandler<DiagnosticItem>? DiagnosticDoubleClicked;

    public void UpdateDiagnostics(IEnumerable<DiagnosticItem> diagnostics)
    {
        var snapshot = diagnostics.ToList();
        Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyDiagnostics(snapshot));
    }

    /// <summary>
    /// Replace the diagnostics, recompute the counts, and rebuild the filtered view.
    /// Split out from <see cref="UpdateDiagnostics"/> (which marshals to the UI thread)
    /// so the count/filter contract is unit-testable without an Avalonia dispatcher.
    /// </summary>
    public void ApplyDiagnostics(IEnumerable<DiagnosticItem> diagnostics)
    {
        _allDiagnostics = diagnostics.ToList();
        RefreshFilteredDiagnostics();

        // Counts are totals per bucket (VS-style): a nonzero count stays visible next
        // to its toggle even when that toggle is off. Every diagnostic maps to exactly
        // one bucket (see Bucket), so the counts and the visible rows can never disagree
        // about which toggle governs a diagnostic.
        ErrorCount = _allDiagnostics.Count(d => Bucket(d.Severity) == FilterBucket.Errors);
        WarningCount = _allDiagnostics.Count(d => Bucket(d.Severity) == FilterBucket.Warnings);
        MessageCount = _allDiagnostics.Count(d => Bucket(d.Severity) == FilterBucket.Messages);
    }

    public void Clear()
    {
        _allDiagnostics.Clear();
        Diagnostics.Clear();
        ErrorCount = 0;
        WarningCount = 0;
        MessageCount = 0;
    }

    private void RefreshFilteredDiagnostics()
    {
        Diagnostics.Clear();

        foreach (var d in _allDiagnostics)
        {
            if (IsBucketVisible(Bucket(d.Severity)))
            {
                Diagnostics.Add(d);
            }
        }
    }

    private enum FilterBucket { Errors, Warnings, Messages }

    // The three Error List toggle buckets. Everything that is not an Error or a
    // Warning (Info, Hidden, Hint, ...) is a Message, so every diagnostic is counted
    // in exactly one badge and governed by exactly one toggle — counts and rows can
    // never disagree about which toggle owns a diagnostic.
    private static FilterBucket Bucket(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => FilterBucket.Errors,
        DiagnosticSeverity.Warning => FilterBucket.Warnings,
        _ => FilterBucket.Messages,
    };

    private bool IsBucketVisible(FilterBucket bucket) => bucket switch
    {
        FilterBucket.Errors => ShowErrors,
        FilterBucket.Warnings => ShowWarnings,
        _ => ShowMessages,
    };

    partial void OnShowErrorsChanged(bool value) => RefreshFilteredDiagnostics();
    partial void OnShowWarningsChanged(bool value) => RefreshFilteredDiagnostics();
    partial void OnShowMessagesChanged(bool value) => RefreshFilteredDiagnostics();

    [RelayCommand]
    private void GoToError()
    {
        if (SelectedDiagnostic != null)
        {
            DiagnosticDoubleClicked?.Invoke(this, SelectedDiagnostic);
        }
    }
}
