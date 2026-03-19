using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Shell.ViewModels.Documents;

namespace VisualGameStudio.Shell.ViewModels;

/// <summary>
/// Manages all tab-related state and operations: pinning, preview mode,
/// MRU tracking, tab persistence, overflow, and context menu actions.
/// </summary>
public partial class TabManagerViewModel : ObservableObject
{
    private readonly List<TabItemViewModel> _mruOrder = new();
    private string? _projectDirectory;

    public TabManagerViewModel()
    {
        Tabs = new ObservableCollection<TabItemViewModel>();
    }

    /// <summary>
    /// All open tabs in display order (pinned first, then unpinned).
    /// </summary>
    public ObservableCollection<TabItemViewModel> Tabs { get; }

    /// <summary>
    /// The currently active tab.
    /// </summary>
    [ObservableProperty]
    private TabItemViewModel? _activeTab;

    /// <summary>
    /// The current preview tab (italic title, reused on single-click).
    /// </summary>
    [ObservableProperty]
    private TabItemViewModel? _previewTab;

    /// <summary>
    /// Whether the overflow chevron should be visible.
    /// </summary>
    [ObservableProperty]
    private bool _hasOverflow;

    /// <summary>
    /// Filter text for the overflow dropdown.
    /// </summary>
    [ObservableProperty]
    private string _overflowFilterText = "";

    /// <summary>
    /// Filtered tabs for the overflow dropdown.
    /// </summary>
    public IEnumerable<TabItemViewModel> FilteredOverflowTabs =>
        string.IsNullOrWhiteSpace(OverflowFilterText)
            ? Tabs
            : Tabs.Where(t => t.FileName.Contains(OverflowFilterText, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Tabs ordered by most recently used (for Ctrl+Tab switcher).
    /// </summary>
    public IReadOnlyList<TabItemViewModel> MruTabs => _mruOrder.AsReadOnly();

    /// <summary>
    /// Fired when a tab should be activated in the dock.
    /// </summary>
    public event EventHandler<TabItemViewModel>? TabActivationRequested;

    /// <summary>
    /// Fired when a tab should be closed in the dock.
    /// </summary>
    public event EventHandler<TabItemViewModel>? TabCloseRequested;

    /// <summary>
    /// Fired when the editor should split for a tab.
    /// </summary>
    public event EventHandler<(TabItemViewModel Tab, string Direction)>? SplitRequested;

    /// <summary>
    /// Fired when a file path should be revealed in the Solution Explorer.
    /// </summary>
    public event EventHandler<string>? RevealInSideBarRequested;

    /// <summary>
    /// Fired when the containing folder should be opened in Windows Explorer.
    /// </summary>
    public event EventHandler<string>? OpenContainingFolderRequested;

    /// <summary>
    /// Fired when a save confirmation is needed before closing a dirty tab.
    /// Returns true if the close should proceed, false to cancel.
    /// </summary>
    public Func<TabItemViewModel, Task<bool>>? ConfirmCloseUnsaved { get; set; }

    /// <summary>
    /// Sets the project directory for workspace persistence.
    /// </summary>
    public void SetProjectDirectory(string? projectDir)
    {
        _projectDirectory = projectDir;
    }

    partial void OnOverflowFilterTextChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredOverflowTabs));
    }

    partial void OnActiveTabChanged(TabItemViewModel? oldValue, TabItemViewModel? newValue)
    {
        if (oldValue != null)
            oldValue.IsActive = false;

        if (newValue != null)
        {
            newValue.IsActive = true;
            newValue.LastActivated = DateTime.UtcNow;
            UpdateMru(newValue);
        }
    }

    /// <summary>
    /// Adds or activates a tab for a document. If isPreview is true, reuses the current preview tab.
    /// </summary>
    public TabItemViewModel AddOrActivateTab(CodeEditorDocumentViewModel document, bool isPreview = false)
    {
        // Check if tab already exists
        var existingTab = Tabs.FirstOrDefault(t =>
            t.FilePath != null && t.FilePath.Equals(document.FilePath, StringComparison.OrdinalIgnoreCase));

        if (existingTab != null)
        {
            // If it was a preview and we're now opening permanently, promote it
            if (existingTab.IsPreview && !isPreview)
            {
                existingTab.IsPreview = false;
            }
            ActiveTab = existingTab;
            return existingTab;
        }

        // If opening in preview mode, close the existing preview tab first
        if (isPreview && PreviewTab != null)
        {
            RemoveTabInternal(PreviewTab);
        }

        var tab = new TabItemViewModel(document)
        {
            IsPreview = isPreview
        };

        if (isPreview)
            PreviewTab = tab;

        // Insert after pinned tabs
        var insertIndex = Tabs.Count(t => t.IsPinned);
        if (!tab.IsPinned)
        {
            Tabs.Insert(insertIndex < Tabs.Count ? insertIndex : Tabs.Count, tab);
        }
        else
        {
            Tabs.Insert(0, tab);
        }

        _mruOrder.Insert(0, tab);
        ActiveTab = tab;
        return tab;
    }

    /// <summary>
    /// Promotes the current preview tab to a permanent tab (on edit or double-click).
    /// </summary>
    public void PromotePreviewTab()
    {
        if (PreviewTab != null)
        {
            PreviewTab.IsPreview = false;
            PreviewTab = null;
        }
    }

    /// <summary>
    /// Removes a tab from the collection and fires close event.
    /// </summary>
    public async Task CloseTabAsync(TabItemViewModel tab)
    {
        // Pinned tabs cannot be closed with Ctrl+W — must unpin first
        if (tab.IsPinned)
            return;

        // Check for unsaved changes
        if (tab.IsModified && ConfirmCloseUnsaved != null)
        {
            var proceed = await ConfirmCloseUnsaved(tab);
            if (!proceed) return;
        }

        RemoveTabInternal(tab);
        TabCloseRequested?.Invoke(this, tab);
    }

    /// <summary>
    /// Force-closes a tab regardless of pinned state (used by Close All, etc.).
    /// </summary>
    public async Task ForceCloseTabAsync(TabItemViewModel tab)
    {
        if (tab.IsModified && ConfirmCloseUnsaved != null)
        {
            var proceed = await ConfirmCloseUnsaved(tab);
            if (!proceed) return;
        }

        if (tab.IsPinned)
            tab.IsPinned = false;

        RemoveTabInternal(tab);
        TabCloseRequested?.Invoke(this, tab);
    }

    private void RemoveTabInternal(TabItemViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        _mruOrder.Remove(tab);

        if (tab == PreviewTab)
            PreviewTab = null;

        // Activate another tab if the closed one was active
        if (tab == ActiveTab)
        {
            if (_mruOrder.Count > 0)
            {
                ActiveTab = _mruOrder[0];
                TabActivationRequested?.Invoke(this, _mruOrder[0]);
            }
            else if (Tabs.Count > 0)
            {
                var newIndex = Math.Min(index, Tabs.Count - 1);
                ActiveTab = Tabs[newIndex];
                TabActivationRequested?.Invoke(this, Tabs[newIndex]);
            }
            else
            {
                ActiveTab = null;
            }
        }
    }

    /// <summary>
    /// Removes a tab by file path (called when dock framework closes a document).
    /// </summary>
    public void OnDocumentClosed(string filePath)
    {
        var tab = Tabs.FirstOrDefault(t =>
            t.FilePath != null && t.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
        if (tab != null)
        {
            RemoveTabInternal(tab);
        }
    }

    /// <summary>
    /// Activates tab by file path.
    /// </summary>
    public void ActivateByFilePath(string filePath)
    {
        var tab = Tabs.FirstOrDefault(t =>
            t.FilePath != null && t.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
        if (tab != null)
        {
            ActiveTab = tab;
        }
    }

    // ────────────────────────────────────────────────────
    // Context Menu Commands
    // ────────────────────────────────────────────────────

    [RelayCommand]
    private async Task CloseTab(TabItemViewModel? tab)
    {
        if (tab == null) return;
        await CloseTabAsync(tab);
    }

    [RelayCommand]
    private async Task CloseOtherTabs(TabItemViewModel? tab)
    {
        if (tab == null) return;
        var others = Tabs.Where(t => t != tab && !t.IsPinned).ToList();
        foreach (var other in others)
        {
            await ForceCloseTabAsync(other);
        }
    }

    [RelayCommand]
    private async Task CloseAllTabs()
    {
        var all = Tabs.ToList();
        foreach (var tab in all)
        {
            await ForceCloseTabAsync(tab);
        }
    }

    [RelayCommand]
    private async Task CloseTabsToTheRight(TabItemViewModel? tab)
    {
        if (tab == null) return;
        var index = Tabs.IndexOf(tab);
        if (index < 0) return;
        var toClose = Tabs.Skip(index + 1).Where(t => !t.IsPinned).ToList();
        foreach (var t in toClose)
        {
            await ForceCloseTabAsync(t);
        }
    }

    [RelayCommand]
    private async Task CloseTabsToTheLeft(TabItemViewModel? tab)
    {
        if (tab == null) return;
        var index = Tabs.IndexOf(tab);
        if (index < 0) return;
        var toClose = Tabs.Take(index).Where(t => !t.IsPinned).ToList();
        foreach (var t in toClose)
        {
            await ForceCloseTabAsync(t);
        }
    }

    [RelayCommand]
    private async Task CloseSavedTabs()
    {
        var saved = Tabs.Where(t => !t.IsModified && !t.IsPinned).ToList();
        foreach (var tab in saved)
        {
            RemoveTabInternal(tab);
            TabCloseRequested?.Invoke(this, tab);
        }
    }

    [RelayCommand]
    private void CopyPath(TabItemViewModel? tab)
    {
        if (tab?.FilePath != null)
        {
            CopyToClipboard(tab.FilePath);
        }
    }

    [RelayCommand]
    private void CopyRelativePath(TabItemViewModel? tab)
    {
        if (tab?.FilePath != null && _projectDirectory != null)
        {
            try
            {
                var relativePath = Path.GetRelativePath(_projectDirectory, tab.FilePath);
                CopyToClipboard(relativePath);
            }
            catch
            {
                CopyToClipboard(tab.FilePath);
            }
        }
        else if (tab?.FilePath != null)
        {
            CopyToClipboard(tab.FilePath);
        }
    }

    [RelayCommand]
    private void RevealInSideBar(TabItemViewModel? tab)
    {
        if (tab?.FilePath != null)
        {
            RevealInSideBarRequested?.Invoke(this, tab.FilePath);
        }
    }

    [RelayCommand]
    private void OpenContainingFolder(TabItemViewModel? tab)
    {
        if (tab?.FilePath != null)
        {
            OpenContainingFolderRequested?.Invoke(this, tab.FilePath);
        }
    }

    [RelayCommand]
    private void PinTab(TabItemViewModel? tab)
    {
        if (tab == null || tab.IsPinned) return;

        tab.IsPinned = true;
        tab.IsPreview = false;
        if (tab == PreviewTab)
            PreviewTab = null;

        // Move to end of pinned section
        var currentIndex = Tabs.IndexOf(tab);
        var pinnedCount = Tabs.Count(t => t.IsPinned);
        var targetIndex = pinnedCount - 1;

        if (currentIndex != targetIndex && currentIndex >= 0)
        {
            Tabs.Move(currentIndex, targetIndex);
        }
    }

    [RelayCommand]
    private void UnpinTab(TabItemViewModel? tab)
    {
        if (tab == null || !tab.IsPinned) return;

        tab.IsPinned = false;

        // Move to after pinned section
        var currentIndex = Tabs.IndexOf(tab);
        var pinnedCount = Tabs.Count(t => t.IsPinned);

        if (currentIndex < pinnedCount && currentIndex >= 0)
        {
            Tabs.Move(currentIndex, pinnedCount);
        }
    }

    [RelayCommand]
    private void SplitRight(TabItemViewModel? tab)
    {
        if (tab != null)
            SplitRequested?.Invoke(this, (tab, "Right"));
    }

    [RelayCommand]
    private void SplitDown(TabItemViewModel? tab)
    {
        if (tab != null)
            SplitRequested?.Invoke(this, (tab, "Down"));
    }

    // ────────────────────────────────────────────────────
    // Tab Navigation
    // ────────────────────────────────────────────────────

    /// <summary>
    /// Move to the next tab (Ctrl+PageDown).
    /// </summary>
    [RelayCommand]
    private void NextTab()
    {
        if (Tabs.Count == 0 || ActiveTab == null) return;
        var index = Tabs.IndexOf(ActiveTab);
        var nextIndex = (index + 1) % Tabs.Count;
        ActivateTab(Tabs[nextIndex]);
    }

    /// <summary>
    /// Move to the previous tab (Ctrl+PageUp).
    /// </summary>
    [RelayCommand]
    private void PreviousTab()
    {
        if (Tabs.Count == 0 || ActiveTab == null) return;
        var index = Tabs.IndexOf(ActiveTab);
        var prevIndex = (index - 1 + Tabs.Count) % Tabs.Count;
        ActivateTab(Tabs[prevIndex]);
    }

    /// <summary>
    /// Switch to tab by 1-based position (Alt+1 through Alt+9).
    /// </summary>
    [RelayCommand]
    private void SwitchToTabByPosition(int position)
    {
        if (position < 1 || position > Tabs.Count) return;
        ActivateTab(Tabs[position - 1]);
    }

    /// <summary>
    /// Get the next MRU tab (for Ctrl+Tab navigation).
    /// </summary>
    public TabItemViewModel? GetNextMruTab()
    {
        if (_mruOrder.Count < 2) return null;
        return _mruOrder[1]; // Index 0 is current, index 1 is previous
    }

    /// <summary>
    /// Get the previous MRU tab (for Ctrl+Shift+Tab navigation).
    /// </summary>
    public TabItemViewModel? GetPreviousMruTab()
    {
        if (_mruOrder.Count < 2) return null;
        return _mruOrder[_mruOrder.Count - 1]; // Last in MRU
    }

    /// <summary>
    /// Activates a tab and fires the activation event.
    /// </summary>
    public void ActivateTab(TabItemViewModel tab)
    {
        ActiveTab = tab;
        TabActivationRequested?.Invoke(this, tab);
    }

    // ────────────────────────────────────────────────────
    // Tab Drag and Drop (reorder)
    // ────────────────────────────────────────────────────

    /// <summary>
    /// Move a tab from one position to another (drag reorder).
    /// Respects pinned/unpinned boundary.
    /// </summary>
    public void MoveTab(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= Tabs.Count) return;
        if (toIndex < 0 || toIndex >= Tabs.Count) return;
        if (fromIndex == toIndex) return;

        var tab = Tabs[fromIndex];
        var pinnedCount = Tabs.Count(t => t.IsPinned);

        // Enforce boundary: pinned tabs stay in pinned zone, unpinned in unpinned zone
        if (tab.IsPinned)
        {
            toIndex = Math.Min(toIndex, pinnedCount - 1);
        }
        else
        {
            toIndex = Math.Max(toIndex, pinnedCount);
        }

        Tabs.Move(fromIndex, toIndex);
    }

    // ────────────────────────────────────────────────────
    // MRU Tracking
    // ────────────────────────────────────────────────────

    private void UpdateMru(TabItemViewModel tab)
    {
        _mruOrder.Remove(tab);
        _mruOrder.Insert(0, tab);
    }

    // ────────────────────────────────────────────────────
    // Tab Persistence
    // ────────────────────────────────────────────────────

    /// <summary>
    /// Saves open tabs state to workspace.json.
    /// </summary>
    public async Task SaveWorkspaceAsync()
    {
        if (_projectDirectory == null) return;

        try
        {
            var vgsDir = Path.Combine(_projectDirectory, ".vgs");
            Directory.CreateDirectory(vgsDir);
            var workspacePath = Path.Combine(vgsDir, "workspace.json");

            var state = new WorkspaceState
            {
                OpenTabs = Tabs.Select(t => new TabState
                {
                    FilePath = t.FilePath ?? "",
                    IsPinned = t.IsPinned,
                    IsPreview = t.IsPreview,
                    Order = Tabs.IndexOf(t)
                }).ToList(),
                ActiveTabPath = ActiveTab?.FilePath
            };

            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(workspacePath, json);
        }
        catch
        {
            // Silently ignore workspace save errors
        }
    }

    /// <summary>
    /// Loads workspace state. Returns the list of file paths to open and which to activate.
    /// </summary>
    public async Task<WorkspaceState?> LoadWorkspaceAsync()
    {
        if (_projectDirectory == null) return null;

        try
        {
            var workspacePath = Path.Combine(_projectDirectory, ".vgs", "workspace.json");
            if (!File.Exists(workspacePath)) return null;

            var json = await File.ReadAllTextAsync(workspacePath);
            return JsonSerializer.Deserialize<WorkspaceState>(json);
        }
        catch
        {
            return null;
        }
    }

    // ────────────────────────────────────────────────────
    // Error Decoration
    // ────────────────────────────────────────────────────

    /// <summary>
    /// Updates error state for a tab by file path.
    /// </summary>
    public void SetTabErrors(string filePath, bool hasErrors)
    {
        var tab = Tabs.FirstOrDefault(t =>
            t.FilePath != null && t.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
        if (tab != null)
        {
            tab.HasErrors = hasErrors;
        }
    }

    /// <summary>
    /// Updates git status for a tab by file path.
    /// </summary>
    public void SetTabGitStatus(string filePath, string? gitStatus)
    {
        var tab = Tabs.FirstOrDefault(t =>
            t.FilePath != null && t.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
        if (tab != null)
        {
            tab.GitStatus = gitStatus;
        }
    }

    // ────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────

    private static void CopyToClipboard(string text)
    {
        try
        {
            // Use Avalonia clipboard on UI thread
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                var mainWindow = (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                var clipboard = mainWindow != null ? Avalonia.Controls.TopLevel.GetTopLevel(mainWindow)?.Clipboard : null;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(text);
                }
            });
        }
        catch
        {
            // Silently ignore clipboard errors
        }
    }
}

/// <summary>
/// Serializable workspace state for tab persistence.
/// </summary>
public class WorkspaceState
{
    public List<TabState> OpenTabs { get; set; } = new();
    public string? ActiveTabPath { get; set; }
}

/// <summary>
/// Serializable state for a single tab.
/// </summary>
public class TabState
{
    public string FilePath { get; set; } = "";
    public bool IsPinned { get; set; }
    public bool IsPreview { get; set; }
    public int Order { get; set; }
}
