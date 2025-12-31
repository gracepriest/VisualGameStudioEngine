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
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _allDiagnostics = diagnostics.ToList();
            RefreshFilteredDiagnostics();

            ErrorCount = _allDiagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
            WarningCount = _allDiagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
            MessageCount = _allDiagnostics.Count(d => d.Severity == DiagnosticSeverity.Info);
        });
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
            bool include = d.Severity switch
            {
                DiagnosticSeverity.Error => ShowErrors,
                DiagnosticSeverity.Warning => ShowWarnings,
                DiagnosticSeverity.Info => ShowMessages,
                _ => true
            };

            if (include)
            {
                Diagnostics.Add(d);
            }
        }
    }

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
