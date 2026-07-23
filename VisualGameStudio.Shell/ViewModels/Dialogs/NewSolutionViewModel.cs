using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

/// <summary>
/// First window of the guided "New Solution" wizard: collects the solution name,
/// target location, and whether to initialize a git repo. Validates the hard-block
/// rules (empty name/location, invalid filename characters, an already-populated
/// target directory) and previews the resulting .blsln path. Purely a data holder —
/// no I/O beyond read-only filesystem checks used for validation.
/// </summary>
public partial class NewSolutionViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyPropertyChangedFor(nameof(ErrorMessage))]
    [NotifyPropertyChangedFor(nameof(SolutionFilePreview))]
    private string _solutionName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyPropertyChangedFor(nameof(ErrorMessage))]
    [NotifyPropertyChangedFor(nameof(SolutionFilePreview))]
    private string _location = "";

    [ObservableProperty]
    private bool _initializeGit = true;

    public bool DialogResult { get; private set; }
    public Action? CloseDialog { get; set; }

    /// <summary>
    /// Computed path preview: Location\SolutionName\SolutionName.blsln.
    /// Empty until both Name and Location are non-empty.
    /// </summary>
    public string SolutionFilePreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SolutionName) || string.IsNullOrWhiteSpace(Location))
            {
                return "";
            }

            return Path.Combine(Location, SolutionName, SolutionName + ".blsln");
        }
    }

    /// <summary>
    /// Computed hard-block validation message; empty when everything is valid.
    /// </summary>
    public string ErrorMessage
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SolutionName))
            {
                return "Solution name is required.";
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            if (SolutionName.IndexOfAny(invalidChars) >= 0)
            {
                return "Solution name contains invalid characters.";
            }

            if (string.IsNullOrWhiteSpace(Location))
            {
                return "Location is required.";
            }

            var targetDir = Path.Combine(Location, SolutionName);
            if (Directory.Exists(targetDir) && Directory.EnumerateFileSystemEntries(targetDir).Any())
            {
                return $"A non-empty folder '{targetDir}' already exists.";
            }

            return "";
        }
    }

    /// <summary>
    /// True only when name and location are present and ErrorMessage is empty.
    /// </summary>
    public bool CanConfirm =>
        !string.IsNullOrWhiteSpace(SolutionName) &&
        !string.IsNullOrWhiteSpace(Location) &&
        string.IsNullOrEmpty(ErrorMessage);

    [RelayCommand]
    private void Confirm()
    {
        if (!CanConfirm)
        {
            return;
        }

        DialogResult = true;
        CloseDialog?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        CloseDialog?.Invoke();
    }

    [RelayCommand]
    private void BrowseLocation()
    {
        // Handled by the view code-behind.
    }
}
