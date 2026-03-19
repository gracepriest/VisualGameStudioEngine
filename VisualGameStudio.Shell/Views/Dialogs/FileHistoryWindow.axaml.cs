using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class FileHistoryWindow : Window
{
    private readonly IGitService _gitService;
    private string _filePath = "";
    private List<FileHistoryEntry> _entries = new();

    public FileHistoryWindow()
    {
        InitializeComponent();
        _gitService = App.Services.GetService<IGitService>()!;
    }

    public async Task LoadHistoryAsync(string filePath)
    {
        _filePath = filePath;
        FileNameText.Text = $"- {Path.GetFileName(filePath)}";
        Title = $"File History - {Path.GetFileName(filePath)}";

        LoadingPanel.IsVisible = true;
        ErrorPanel.IsVisible = false;
        EmptyPanel.IsVisible = false;
        CommitGrid.IsVisible = false;

        try
        {
            var history = await _gitService.GetFileHistoryAsync(filePath, 100);

            if (history.Count == 0)
            {
                EmptyPanel.IsVisible = true;
                LoadingPanel.IsVisible = false;
                return;
            }

            _entries = history.Select(c => new FileHistoryEntry
            {
                Hash = c.Hash,
                ShortHash = c.ShortHash,
                Author = c.Author,
                Date = c.Date,
                RelativeDate = FormatRelativeDate(c.Date),
                Message = c.Message
            }).ToList();

            CommitGrid.ItemsSource = _entries;
            CommitGrid.IsVisible = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = $"Failed to load file history: {ex.Message}";
            ErrorPanel.IsVisible = true;
        }
        finally
        {
            LoadingPanel.IsVisible = false;
        }
    }

    private void OnCommitSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        ViewDiffButton.IsEnabled = CommitGrid.SelectedItem is FileHistoryEntry;
    }

    private void OnCommitDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (CommitGrid.SelectedItem is FileHistoryEntry entry)
        {
            _ = ShowDiffForCommitAsync(entry);
        }
    }

    private void OnViewDiffClicked(object? sender, RoutedEventArgs e)
    {
        if (CommitGrid.SelectedItem is FileHistoryEntry entry)
        {
            _ = ShowDiffForCommitAsync(entry);
        }
    }

    private async Task ShowDiffForCommitAsync(FileHistoryEntry entry)
    {
        try
        {
            var diff = await _gitService.GetFileDiffAtCommitAsync(_filePath, entry.Hash);

            if (string.IsNullOrWhiteSpace(diff))
            {
                diff = $"--- No diff available for commit {entry.ShortHash} ---";
            }

            var diffVm = new DiffViewerViewModel(_gitService);
            diffVm.LoadFromRawDiff(
                diff,
                Path.GetFileName(_filePath),
                $"{Path.GetFileName(_filePath)} ({entry.ShortHash}^)",
                $"{Path.GetFileName(_filePath)} ({entry.ShortHash})");

            var diffView = new DiffViewerView
            {
                DataContext = diffVm
            };

            await diffView.ShowDialog(this);
        }
        catch (Exception ex)
        {
            ErrorText.Text = $"Failed to load diff: {ex.Message}";
            ErrorPanel.IsVisible = true;
        }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private static string FormatRelativeDate(DateTime date)
    {
        var span = DateTime.Now - date;

        if (span.TotalMinutes < 1)
            return "just now";
        if (span.TotalMinutes < 60)
            return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24)
            return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 7)
            return $"{(int)span.TotalDays}d ago";
        if (span.TotalDays < 30)
            return $"{(int)(span.TotalDays / 7)}w ago";
        if (span.TotalDays < 365)
            return $"{(int)(span.TotalDays / 30)}mo ago";

        return $"{(int)(span.TotalDays / 365)}y ago";
    }
}

public class FileHistoryEntry
{
    public string Hash { get; set; } = "";
    public string ShortHash { get; set; } = "";
    public string Author { get; set; } = "";
    public DateTime Date { get; set; }
    public string RelativeDate { get; set; } = "";
    public string Message { get; set; } = "";
}
