using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

/// <summary>
/// Represents a single keybinding entry displayed in the Keyboard Shortcuts dialog.
/// </summary>
public class KeybindingEntry
{
    public string Command { get; set; } = "";
    public string Category { get; set; } = "";
    public string Keybinding { get; set; } = "";

    /// <summary>
    /// Display string combining category and command for search matching.
    /// </summary>
    public string SearchText => $"{Category} {Command} {Keybinding}";
}

/// <summary>
/// ViewModel for the Keyboard Shortcuts dialog.
/// Displays a searchable, read-only list of all IDE commands and their keybindings.
/// </summary>
public partial class KeyboardShortcutsViewModel : ViewModelBase
{
    private readonly List<KeybindingEntry> _allEntries = new();

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private ObservableCollection<KeybindingEntry> _filteredEntries = new();

    [ObservableProperty]
    private KeybindingEntry? _selectedEntry;

    [ObservableProperty]
    private string _statusText = "";

    public Action? CloseDialog { get; set; }

    /// <summary>
    /// Populates the keybinding list with all IDE commands and their shortcuts.
    /// </summary>
    public void LoadFromCommandPalette(MainWindowViewModel mainVm)
    {
        _allEntries.Clear();

        // Build the complete list of all commands with their keybindings.
        // This mirrors the command registration in CommandPaletteViewModel.RegisterCommands()
        // but captures every entry without the palette's display limit.

        // File commands
        Add("File", "New Project...", "Ctrl+Shift+N");
        Add("File", "Open Project...", "Ctrl+Shift+O");
        Add("File", "Open File...", "Ctrl+O");
        Add("File", "Save", "Ctrl+S");
        Add("File", "Save All", "Ctrl+Shift+S");
        Add("File", "Exit", "Alt+F4");

        // Edit commands
        Add("Edit", "Find...", "Ctrl+F");
        Add("Edit", "Replace...", "Ctrl+H");
        Add("Edit", "Find in Files...", "Ctrl+Shift+F");
        Add("Edit", "Go to Definition", "F12");
        Add("Edit", "Go to Implementation", "Ctrl+F12");
        Add("Edit", "Find All References", "Shift+F12");
        Add("Edit", "Go to Line...", "Ctrl+G");
        Add("Edit", "Go to Symbol...", "Ctrl+T");
        Add("Edit", "Rename Symbol...", "Ctrl+R");
        Add("Edit", "Toggle Comment", "Ctrl+/");
        Add("Edit", "Duplicate Line", "Ctrl+D");
        Add("Edit", "Move Line Up", "Alt+Up");
        Add("Edit", "Move Line Down", "Alt+Down");
        Add("Edit", "Delete Line", "Ctrl+Shift+K");

        // Edit - Refactoring
        Add("Edit", "Extract Method...", "Ctrl+Shift+M");
        Add("Edit", "Inline Method...", "Ctrl+Shift+I");
        Add("Edit", "Introduce Variable...", "Ctrl+Shift+V");
        Add("Edit", "Change Signature...", "");
        Add("Edit", "Encapsulate Field...", "Ctrl+Shift+E");
        Add("Edit", "Move Type to File...", "Ctrl+Shift+T");
        Add("Edit", "Extract Interface...", "Ctrl+Shift+X");
        Add("Edit", "Generate Constructor...", "Ctrl+Shift+G");
        Add("Edit", "Implement Interface...", "Ctrl+.");

        // View commands
        Add("View", "Command Palette...", "Ctrl+Shift+P");
        Add("View", "Quick Open...", "Ctrl+P");
        Add("View", "Solution Explorer", "");
        Add("View", "Output", "Ctrl+Alt+O");
        Add("View", "Error List", "Ctrl+Alt+E");
        Add("View", "Terminal", "");
        Add("View", "Find Results", "");
        Add("View", "Bookmarks", "");
        Add("View", "Zen Mode", "Ctrl+Shift+Z");
        Add("View", "Full Screen", "Shift+Alt+Enter");

        // Build commands
        Add("Build", "Build Project", "Ctrl+Shift+B");
        Add("Build", "Rebuild Project", "");
        Add("Build", "Clean Project", "");
        Add("Build", "Cancel Build", "");

        // Debug commands
        Add("Debug", "Start Debugging", "F5");
        Add("Debug", "Start Without Debugging", "Ctrl+F5");
        Add("Debug", "Run in External Console", "Ctrl+Shift+F5");
        Add("Debug", "Stop Debugging", "Shift+F5");
        Add("Debug", "Restart Debugging", "Ctrl+Shift+F5");
        Add("Debug", "Continue", "F5");
        Add("Debug", "Step Over", "F10");
        Add("Debug", "Step Into", "F11");
        Add("Debug", "Step Out", "Shift+F11");
        Add("Debug", "Run to Cursor", "Ctrl+F10");
        Add("Debug", "Toggle Breakpoint", "F9");
        Add("Debug", "New Function Breakpoint...", "Ctrl+Shift+F9");
        Add("Debug", "Exception Settings...", "Ctrl+Alt+X");

        // Debug Windows
        Add("Debug", "Show Breakpoints", "Ctrl+Alt+B");
        Add("Debug", "Show Call Stack", "Ctrl+Alt+C");
        Add("Debug", "Show Variables", "Ctrl+Alt+V");
        Add("Debug", "Show Watch", "Ctrl+Alt+W");
        Add("Debug", "Show Immediate Window", "Ctrl+Alt+I");

        // Bookmarks
        Add("Bookmarks", "Toggle Bookmark", "Ctrl+K");
        Add("Bookmarks", "Next Bookmark", "F2");
        Add("Bookmarks", "Previous Bookmark", "Shift+F2");
        Add("Bookmarks", "Clear All Bookmarks", "");

        // Selection
        Add("Selection", "Expand Selection", "");
        Add("Selection", "Shrink Selection", "");

        // Tools
        Add("Tools", "Keyboard Shortcuts...", "");
        Add("Tools", "Settings...", "");

        UpdateFilter();
    }

    private void Add(string category, string command, string keybinding)
    {
        _allEntries.Add(new KeybindingEntry
        {
            Category = category,
            Command = command,
            Keybinding = keybinding
        });
    }

    partial void OnSearchTextChanged(string value)
    {
        UpdateFilter();
    }

    private void UpdateFilter()
    {
        FilteredEntries.Clear();

        var filter = SearchText?.Trim() ?? "";
        IEnumerable<KeybindingEntry> items;

        if (string.IsNullOrEmpty(filter))
        {
            items = _allEntries;
        }
        else
        {
            items = _allEntries.Where(e =>
                e.SearchText.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var entry in items)
        {
            FilteredEntries.Add(entry);
        }

        StatusText = $"{FilteredEntries.Count} of {_allEntries.Count} commands";
    }

    [RelayCommand]
    private void Close()
    {
        CloseDialog?.Invoke();
    }
}
