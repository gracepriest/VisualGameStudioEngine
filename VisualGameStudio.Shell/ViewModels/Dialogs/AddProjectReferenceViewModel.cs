using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

/// <summary>
/// Backs the "Add Project Reference" picker: lets the user choose which of the
/// solution's other projects the selected project should reference. The actual
/// reference-writing (both .blsln and .blproj) happens in
/// <see cref="Panels.SolutionExplorerViewModel.ApplyProjectReferencesAsync"/> — this
/// view model only collects the user's selection.
/// </summary>
public partial class AddProjectReferenceViewModel : ViewModelBase
{
    /// <summary>The name of the project the reference is being added FROM (display only).</summary>
    [ObservableProperty]
    private string _fromProjectName = "";

    public ObservableCollection<CheckableItem> Candidates { get; } = new();

    public bool DialogResult { get; private set; }

    /// <summary>Set by the hosting Window's code-behind to close it with <see cref="DialogResult"/>.</summary>
    public Action? CloseDialog { get; set; }

    public IReadOnlyList<string> SelectedNames =>
        Candidates.Where(c => c.IsChecked).Select(c => c.Name).ToList();

    /// <summary>Populates <see cref="Candidates"/> from the given project names, all unchecked.</summary>
    public void Initialize(IEnumerable<string> candidateProjectNames)
    {
        Candidates.Clear();
        foreach (var name in candidateProjectNames)
        {
            Candidates.Add(new CheckableItem { Name = name });
        }
    }

    [RelayCommand]
    private void Ok()
    {
        DialogResult = true;
        CloseDialog?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        CloseDialog?.Invoke();
    }
}
