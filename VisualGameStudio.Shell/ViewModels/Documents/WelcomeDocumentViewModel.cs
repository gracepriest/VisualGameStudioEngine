using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.ViewModels;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Shell.ViewModels.Documents;

public partial class WelcomeDocumentViewModel : ViewModelBase
{
    private readonly RecentProjectsService _recentProjectsService;
    private readonly Action<string>? _openProject;
    private readonly Action? _newProject;
    private readonly Action? _openFile;

    [ObservableProperty]
    private ObservableCollection<RecentProjectItem> _recentProjects = new();

    public WelcomeDocumentViewModel(
        RecentProjectsService recentProjectsService,
        Action<string>? openProject = null,
        Action? newProject = null,
        Action? openFile = null)
    {
        _recentProjectsService = recentProjectsService;
        _openProject = openProject;
        _newProject = newProject;
        _openFile = openFile;

        LoadRecentProjects();
    }

    private void LoadRecentProjects()
    {
        RecentProjects.Clear();
        var recents = _recentProjectsService.RecentProjects;

        foreach (var project in recents.Take(10))
        {
            RecentProjects.Add(new RecentProjectItem
            {
                Name = Path.GetFileNameWithoutExtension(project.FilePath),
                FilePath = project.FilePath,
                LastOpened = project.LastOpened,
                LastOpenedText = GetRelativeTimeText(project.LastOpened)
            });
        }
    }

    [RelayCommand]
    private void OpenRecentProject(RecentProjectItem? item)
    {
        if (item != null)
        {
            _openProject?.Invoke(item.FilePath);
        }
    }

    [RelayCommand]
    private async Task RemoveRecentProjectAsync(RecentProjectItem? item)
    {
        if (item != null)
        {
            await _recentProjectsService.RemoveRecentProjectAsync(item.FilePath);
            RecentProjects.Remove(item);
        }
    }

    [RelayCommand]
    private async Task ClearRecentProjectsAsync()
    {
        await _recentProjectsService.ClearAsync();
        RecentProjects.Clear();
    }

    [RelayCommand]
    private void NewProject()
    {
        _newProject?.Invoke();
    }

    [RelayCommand]
    private void OpenProject()
    {
        _openProject?.Invoke("");
    }

    [RelayCommand]
    private void OpenFile()
    {
        _openFile?.Invoke();
    }

    public void Refresh()
    {
        LoadRecentProjects();
    }

    private static string GetRelativeTimeText(DateTime dateTime)
    {
        var span = DateTime.Now - dateTime;

        if (span.TotalMinutes < 1)
            return "Just now";
        if (span.TotalMinutes < 60)
            return $"{(int)span.TotalMinutes} minutes ago";
        if (span.TotalHours < 24)
            return $"{(int)span.TotalHours} hours ago";
        if (span.TotalDays < 7)
            return $"{(int)span.TotalDays} days ago";
        if (span.TotalDays < 30)
            return $"{(int)(span.TotalDays / 7)} weeks ago";
        if (span.TotalDays < 365)
            return $"{(int)(span.TotalDays / 30)} months ago";

        return dateTime.ToString("MMM d, yyyy");
    }
}

public class RecentProjectItem
{
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public DateTime LastOpened { get; set; }
    public string LastOpenedText { get; set; } = "";

    public string Directory => Path.GetDirectoryName(FilePath) ?? "";
}
