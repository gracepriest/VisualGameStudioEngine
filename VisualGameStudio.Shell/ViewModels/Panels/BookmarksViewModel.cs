using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Panels;

public partial class BookmarksViewModel : ViewModelBase
{
    private readonly IBookmarkService _bookmarkService;

    [ObservableProperty]
    private ObservableCollection<BookmarkItemViewModel> _bookmarks = new();

    [ObservableProperty]
    private BookmarkItemViewModel? _selectedBookmark;

    public event EventHandler<BookmarkNavigationEventArgs>? NavigationRequested;

    public BookmarksViewModel(IBookmarkService bookmarkService)
    {
        _bookmarkService = bookmarkService;
        _bookmarkService.BookmarkChanged += OnBookmarkChanged;
        RefreshBookmarks();
    }

    private void OnBookmarkChanged(object? sender, BookmarkChangedEventArgs e)
    {
        RefreshBookmarks();
    }

    [RelayCommand]
    private void RefreshBookmarks()
    {
        Bookmarks.Clear();
        foreach (var bookmark in _bookmarkService.GetAllBookmarks())
        {
            Bookmarks.Add(new BookmarkItemViewModel
            {
                FilePath = bookmark.FilePath,
                FileName = bookmark.FileName,
                Line = bookmark.Line,
                Label = bookmark.Label ?? "",
                DisplayText = bookmark.DisplayText
            });
        }
    }

    [RelayCommand]
    private void NavigateToBookmark(BookmarkItemViewModel? bookmark)
    {
        if (bookmark == null) return;

        NavigationRequested?.Invoke(this, new BookmarkNavigationEventArgs
        {
            FilePath = bookmark.FilePath,
            Line = bookmark.Line
        });
    }

    [RelayCommand]
    private void RemoveBookmark(BookmarkItemViewModel? bookmark)
    {
        if (bookmark == null) return;
        _bookmarkService.RemoveBookmark(bookmark.FilePath, bookmark.Line);
    }

    [RelayCommand]
    private void ClearAllBookmarks()
    {
        _bookmarkService.ClearBookmarks();
    }

    [RelayCommand]
    private void GoToNextBookmark()
    {
        if (SelectedBookmark != null)
        {
            var next = _bookmarkService.GetNextBookmark(SelectedBookmark.FilePath, SelectedBookmark.Line);
            if (next != null)
            {
                NavigationRequested?.Invoke(this, new BookmarkNavigationEventArgs
                {
                    FilePath = next.FilePath,
                    Line = next.Line
                });
            }
        }
    }

    [RelayCommand]
    private void GoToPreviousBookmark()
    {
        if (SelectedBookmark != null)
        {
            var prev = _bookmarkService.GetPreviousBookmark(SelectedBookmark.FilePath, SelectedBookmark.Line);
            if (prev != null)
            {
                NavigationRequested?.Invoke(this, new BookmarkNavigationEventArgs
                {
                    FilePath = prev.FilePath,
                    Line = prev.Line
                });
            }
        }
    }
}

public partial class BookmarkItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _filePath = "";

    [ObservableProperty]
    private string _fileName = "";

    [ObservableProperty]
    private int _line;

    [ObservableProperty]
    private string _label = "";

    [ObservableProperty]
    private string _displayText = "";
}

public class BookmarkNavigationEventArgs : EventArgs
{
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
}
