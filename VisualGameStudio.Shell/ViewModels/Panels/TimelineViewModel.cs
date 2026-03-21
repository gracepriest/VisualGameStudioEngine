using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;
using VisualGameStudio.Core.Events;

namespace VisualGameStudio.Shell.ViewModels.Panels;

public partial class TimelineViewModel : ViewModelBase
{
    private readonly IGitService _gitService;
    private readonly IEventAggregator _eventAggregator;
    private IDisposable? _activeDocumentSubscription;

    [ObservableProperty]
    private string? _currentFilePath;

    [ObservableProperty]
    private string _fileName = "";

    [ObservableProperty]
    private ObservableCollection<TimelineItemViewModel> _items = new();

    [ObservableProperty]
    private TimelineItemViewModel? _selectedItem;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _hasItems;

    [ObservableProperty]
    private int _itemCount;

    /// <summary>
    /// Raised when the user wants to view a diff for a commit.
    /// The subscriber (MainWindowViewModel) opens a diff viewer.
    /// </summary>
    public event EventHandler<TimelineItemViewModel>? DiffRequested;

    public TimelineViewModel(IGitService gitService, IEventAggregator eventAggregator)
    {
        _gitService = gitService;
        _eventAggregator = eventAggregator;

        // Subscribe to active document changes so timeline auto-updates
        _activeDocumentSubscription = _eventAggregator.Subscribe<ActiveDocumentChangedEvent>(
            evt => Avalonia.Threading.Dispatcher.UIThread.Post(
                () => _ = LoadTimelineAsync(evt.FilePath)));
    }

    [RelayCommand]
    public async Task LoadTimelineAsync(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            Items.Clear();
            HasItems = false;
            ItemCount = 0;
            CurrentFilePath = null;
            FileName = "";
            return;
        }

        CurrentFilePath = filePath;
        FileName = Path.GetFileName(filePath);
        IsLoading = true;
        ErrorMessage = null;
        Items.Clear();

        try
        {
            var commits = await _gitService.GetFileHistoryAsync(filePath);

            if (commits.Count == 0)
            {
                ErrorMessage = "No history available. File may not be tracked by Git.";
                HasItems = false;
                ItemCount = 0;
                return;
            }

            foreach (var commit in commits)
            {
                Items.Add(new TimelineItemViewModel
                {
                    CommitHash = commit.Hash,
                    ShortHash = commit.ShortHash,
                    Message = commit.Message,
                    Author = commit.Author,
                    Date = commit.Date,
                    FilePath = filePath
                });
            }

            HasItems = Items.Count > 0;
            ItemCount = Items.Count;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load timeline: {ex.Message}";
            HasItems = false;
            ItemCount = 0;
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
            await LoadTimelineAsync(CurrentFilePath);
        }
    }

    [RelayCommand]
    private void OpenDiff(TimelineItemViewModel? item)
    {
        if (item == null) return;
        DiffRequested?.Invoke(this, item);
    }

    partial void OnSelectedItemChanged(TimelineItemViewModel? value)
    {
        // Optionally auto-open diff on selection
    }
}

public partial class TimelineItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _commitHash = "";

    [ObservableProperty]
    private string _shortHash = "";

    [ObservableProperty]
    private string _message = "";

    [ObservableProperty]
    private string _author = "";

    [ObservableProperty]
    private DateTime _date;

    [ObservableProperty]
    private string _filePath = "";

    public string RelativeDate
    {
        get
        {
            var span = DateTime.Now - Date;
            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} minute{((int)span.TotalMinutes == 1 ? "" : "s")} ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours} hour{((int)span.TotalHours == 1 ? "" : "s")} ago";
            if (span.TotalDays < 30) return $"{(int)span.TotalDays} day{((int)span.TotalDays == 1 ? "" : "s")} ago";
            if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30)} month{((int)(span.TotalDays / 30) == 1 ? "" : "s")} ago";
            return $"{(int)(span.TotalDays / 365)} year{((int)(span.TotalDays / 365) == 1 ? "" : "s")} ago";
        }
    }

    public string AuthorShort => Author.Length > 16 ? Author[..16] + "..." : Author;

    public string ToolTipText => $"{Author}\n{CommitHash}\n{Date:yyyy-MM-dd HH:mm:ss}\n{Message}";

    public string DateFormatted => Date.ToString("yyyy-MM-dd");

    public string DisplayText => Message.Length > 60 ? Message[..57] + "..." : Message;
}
