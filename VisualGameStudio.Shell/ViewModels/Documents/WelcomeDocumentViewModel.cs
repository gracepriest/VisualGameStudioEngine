using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Documents;

public partial class WelcomeDocumentViewModel : ViewModelBase
{
    private readonly IRecentProjectsService _recentProjectsService;
    private readonly Action<string>? _openProject;
    private readonly Action? _newProject;
    private readonly Action? _openFile;
    private readonly Action? _openFolder;
    private readonly Action? _cloneRepository;

    [ObservableProperty]
    private ObservableCollection<RecentProjectItem> _recentProjects = new();

    [ObservableProperty]
    private bool _showWelcomeOnStartup = true;

    [ObservableProperty]
    private string _versionText = "Version 1.0";

    /// <summary>
    /// Walkthrough checklist items for Getting Started.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<WalkthroughItem> _walkthroughItems = new();

    /// <summary>
    /// Learn/help links.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<LearnLink> _learnLinks = new();

    /// <summary>
    /// DI-friendly constructor that only requires the service.
    /// Action callbacks can be set later via SetCallbacks().
    /// </summary>
    public WelcomeDocumentViewModel(IRecentProjectsService recentProjectsService)
        : this(recentProjectsService, null, null, null, null, null)
    {
    }

    public WelcomeDocumentViewModel(
        IRecentProjectsService recentProjectsService,
        Action<string>? openProject,
        Action? newProject,
        Action? openFile,
        Action? openFolder,
        Action? cloneRepository)
    {
        _recentProjectsService = recentProjectsService;
        _openProject = openProject;
        _newProject = newProject;
        _openFile = openFile;
        _openFolder = openFolder;
        _cloneRepository = cloneRepository;

        _recentProjectsService.RecentProjectsChanged += (_, _) => LoadRecentProjects();

        InitializeWalkthroughItems();
        InitializeLearnLinks();
        LoadRecentProjects();

        // Try loading version from assembly
        try
        {
            var asm = System.Reflection.Assembly.GetEntryAssembly();
            if (asm != null)
            {
                var ver = asm.GetName().Version;
                if (ver != null)
                {
                    VersionText = $"Version {ver.Major}.{ver.Minor}.{ver.Build}";
                }
            }
        }
        catch { /* ignore */ }
    }

    private void InitializeWalkthroughItems()
    {
        WalkthroughItems = new ObservableCollection<WalkthroughItem>
        {
            new() { Title = "Create a new project", Description = "Use Ctrl+Shift+N to create your first BasicLang project", IsCompleted = false },
            new() { Title = "Write some code", Description = "Open a .bas file and start writing BasicLang code with IntelliSense", IsCompleted = false },
            new() { Title = "Build your project", Description = "Press Ctrl+Shift+B to compile your code", IsCompleted = false },
            new() { Title = "Debug your code", Description = "Press F5 to start debugging with breakpoints", IsCompleted = false },
        };
    }

    private void InitializeLearnLinks()
    {
        LearnLinks = new ObservableCollection<LearnLink>
        {
            new() { Title = "Documentation", Description = "Browse the BasicLang language reference", Icon = "?", Url = "https://github.com/gracepriest/VisualGameStudioEngine/wiki" },
            new() { Title = "Tutorials", Description = "Step-by-step guides to get started", Icon = "T", Url = "https://github.com/gracepriest/VisualGameStudioEngine/wiki/Tutorials" },
            new() { Title = "Release Notes", Description = "See what's new in this version", Icon = "R", Url = "https://github.com/gracepriest/VisualGameStudioEngine/releases" },
            new() { Title = "Report an Issue", Description = "Found a bug? Let us know", Icon = "!", Url = "https://github.com/gracepriest/VisualGameStudioEngine/issues" },
        };
    }

    private void LoadRecentProjects()
    {
        RecentProjects.Clear();
        var recents = _recentProjectsService.GetRecentProjects();

        foreach (var project in recents.Take(20))
        {
            RecentProjects.Add(new RecentProjectItem
            {
                Name = project.Name,
                FilePath = project.Path,
                DirectoryPath = project.DirectoryName,
                LastOpened = project.LastOpened,
                LastOpenedText = project.TimeAgo,
                IsPinned = project.IsPinned,
                Exists = project.Exists
            });
        }
    }

    [RelayCommand]
    private void OpenRecentProject(RecentProjectItem? item)
    {
        if (item != null && !string.IsNullOrEmpty(item.FilePath))
        {
            _openProject?.Invoke(item.FilePath);
        }
    }

    [RelayCommand]
    private void RemoveRecentProject(RecentProjectItem? item)
    {
        if (item != null)
        {
            _recentProjectsService.RemoveRecentProject(item.FilePath);
            // LoadRecentProjects() is called by the event handler
        }
    }

    [RelayCommand]
    private void ClearRecentProjects()
    {
        _recentProjectsService.ClearRecentProjects();
    }

    [RelayCommand]
    private void PinProject(RecentProjectItem? item)
    {
        if (item != null)
        {
            if (item.IsPinned)
                _recentProjectsService.UnpinProject(item.FilePath);
            else
                _recentProjectsService.PinProject(item.FilePath);
        }
    }

    [RelayCommand]
    private void CopyPath(RecentProjectItem? item)
    {
        // This will be handled in the view code-behind using clipboard
        if (item != null)
        {
            CopyPathRequested?.Invoke(this, item.FilePath);
        }
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

    [RelayCommand]
    private void OpenFolder()
    {
        _openFolder?.Invoke();
    }

    [RelayCommand]
    private void CloneRepository()
    {
        _cloneRepository?.Invoke();
    }

    [RelayCommand]
    private void OpenLearnLink(LearnLink? link)
    {
        if (link?.Url != null)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = link.Url,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Refreshes the recent projects list from the service.
    /// </summary>
    public void Refresh()
    {
        LoadRecentProjects();
    }

    /// <summary>
    /// Raised when the user requests to copy a project path to clipboard.
    /// Handled in the view code-behind.
    /// </summary>
    public event EventHandler<string>? CopyPathRequested;

}

public class RecentProjectItem
{
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string DirectoryPath { get; set; } = "";
    public DateTime LastOpened { get; set; }
    public string LastOpenedText { get; set; } = "";
    public bool IsPinned { get; set; }
    public bool Exists { get; set; } = true;

    /// <summary>Legacy: alias for DirectoryPath.</summary>
    public string Directory => DirectoryPath;
}

public class WalkthroughItem
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsCompleted { get; set; }
}

public class LearnLink
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Icon { get; set; } = "";
    public string? Url { get; set; }
}
