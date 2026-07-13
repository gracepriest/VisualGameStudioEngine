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
    /// Populates the keybinding list from <see cref="KeyboardShortcutRegistry"/> — whose global
    /// entries mirror <c>MainWindow.axaml</c>'s real <c>Window.KeyBindings</c> (cross-validated by a
    /// unit test). The <paramref name="mainVm"/> parameter is retained for call-site compatibility;
    /// the list no longer depends on it.
    /// </summary>
    public void LoadFromCommandPalette(MainWindowViewModel mainVm) => LoadShortcuts();

    /// <summary>Rebuilds the entry list from the shared shortcut registry.</summary>
    public void LoadShortcuts()
    {
        _allEntries.Clear();

        foreach (var s in KeyboardShortcutRegistry.All)
        {
            _allEntries.Add(new KeybindingEntry
            {
                Category = s.Category,
                Command = s.DisplayName,
                Keybinding = s.DisplayGesture
            });
        }

        UpdateFilter();
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
