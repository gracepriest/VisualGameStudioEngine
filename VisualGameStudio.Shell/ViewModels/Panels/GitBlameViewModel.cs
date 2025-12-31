using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Panels;

public partial class GitBlameViewModel : ViewModelBase
{
    private readonly IGitService _gitService;

    [ObservableProperty]
    private string? _currentFilePath;

    [ObservableProperty]
    private string _fileName = "";

    [ObservableProperty]
    private ObservableCollection<BlameLineViewModel> _blameLines = new();

    [ObservableProperty]
    private BlameLineViewModel? _selectedLine;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _hasBlameData;

    public GitBlameViewModel(IGitService gitService)
    {
        _gitService = gitService;
    }

    [RelayCommand]
    public async Task LoadBlameAsync(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            BlameLines.Clear();
            HasBlameData = false;
            CurrentFilePath = null;
            FileName = "";
            return;
        }

        CurrentFilePath = filePath;
        FileName = Path.GetFileName(filePath);
        IsLoading = true;
        ErrorMessage = null;
        BlameLines.Clear();

        try
        {
            var blameInfo = await _gitService.GetBlameAsync(filePath);

            if (blameInfo.Count == 0)
            {
                ErrorMessage = "No blame information available. File may not be tracked by Git.";
                HasBlameData = false;
                return;
            }

            // Group consecutive lines by commit for visual grouping
            string? lastCommitHash = null;
            bool isAlternate = false;

            foreach (var line in blameInfo)
            {
                if (line.CommitHash != lastCommitHash)
                {
                    isAlternate = !isAlternate;
                    lastCommitHash = line.CommitHash;
                }

                BlameLines.Add(new BlameLineViewModel
                {
                    LineNumber = line.LineNumber,
                    CommitHash = line.CommitHash,
                    ShortHash = line.ShortHash,
                    Author = line.Author,
                    Date = line.Date,
                    LineContent = line.LineContent,
                    IsAlternate = isAlternate,
                    IsFirstInGroup = BlameLines.Count == 0 ||
                                    BlameLines[^1].CommitHash != line.CommitHash
                });
            }

            HasBlameData = BlameLines.Count > 0;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load blame: {ex.Message}";
            HasBlameData = false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!string.IsNullOrEmpty(CurrentFilePath))
        {
            await LoadBlameAsync(CurrentFilePath);
        }
    }

    [RelayCommand]
    private void GoToLine(BlameLineViewModel? line)
    {
        if (line == null) return;
        // This would navigate to the line in the editor
        // Implementation depends on editor integration
    }
}

public partial class BlameLineViewModel : ObservableObject
{
    [ObservableProperty]
    private int _lineNumber;

    [ObservableProperty]
    private string _commitHash = "";

    [ObservableProperty]
    private string _shortHash = "";

    [ObservableProperty]
    private string _author = "";

    [ObservableProperty]
    private DateTime _date;

    [ObservableProperty]
    private string _lineContent = "";

    [ObservableProperty]
    private bool _isAlternate;

    [ObservableProperty]
    private bool _isFirstInGroup;

    public string DateFormatted => Date.ToString("yyyy-MM-dd");
    public string AuthorShort => Author.Length > 12 ? Author[..12] + "..." : Author;
    public string ToolTipText => $"{Author}\n{CommitHash}\n{Date:yyyy-MM-dd HH:mm:ss}";
}
