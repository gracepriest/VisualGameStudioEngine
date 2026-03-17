using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

/// <summary>
/// Represents a single command entry in the command palette.
/// </summary>
public class CommandPaletteItem
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string? Shortcut { get; set; }
    public Action? Execute { get; set; }
    public string DisplayName => string.IsNullOrEmpty(Category) ? Name : $"{Category}: {Name}";
}

/// <summary>
/// ViewModel for the Command Palette dialog (Ctrl+Shift+P).
/// Provides a searchable list of all available IDE commands with fuzzy filtering.
/// </summary>
public partial class CommandPaletteViewModel : ViewModelBase
{
    private readonly List<CommandPaletteItem> _allCommands = new();

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private ObservableCollection<CommandPaletteItem> _filteredCommands = new();

    [ObservableProperty]
    private CommandPaletteItem? _selectedItem;

    [ObservableProperty]
    private int _selectedIndex;

    public event EventHandler<CommandPaletteItem>? CommandExecuted;
    public event EventHandler? Dismissed;

    /// <summary>
    /// Registers all available commands from the MainWindowViewModel.
    /// </summary>
    public void RegisterCommands(MainWindowViewModel vm)
    {
        _allCommands.Clear();

        // File commands
        AddCommand("File", "New Project...", "Ctrl+Shift+N", () => vm.NewProjectCommand.Execute(null));
        AddCommand("File", "Open Project...", "Ctrl+Shift+O", () => vm.OpenProjectCommand.Execute(null));
        AddCommand("File", "Open File...", "Ctrl+O", () => vm.OpenFileCommand.Execute(null));
        AddCommand("File", "Save", "Ctrl+S", () => vm.SaveCommand.Execute(null));
        AddCommand("File", "Save All", "Ctrl+Shift+S", () => vm.SaveAllCommand.Execute(null));
        AddCommand("File", "Exit", "Alt+F4", () => vm.ExitCommand.Execute(null));

        // Edit commands
        AddCommand("Edit", "Find...", "Ctrl+F", () => vm.FindCommand.Execute(null));
        AddCommand("Edit", "Replace...", "Ctrl+H", () => vm.ReplaceCommand.Execute(null));
        AddCommand("Edit", "Find in Files...", "Ctrl+Shift+F", () => vm.ShowFindInFilesCommand.Execute(null));
        AddCommand("Edit", "Go to Definition", "F12", () => vm.GoToDefinitionCommand.Execute(null));
        AddCommand("Edit", "Find All References", "Shift+F12", () => vm.FindReferencesCommand.Execute(null));
        AddCommand("Edit", "Go to Line...", "Ctrl+G", () => vm.GoToLineCommand.Execute(null));
        AddCommand("Edit", "Go to Symbol...", "Ctrl+T", () => vm.GoToSymbolCommand.Execute(null));
        AddCommand("Edit", "Rename Symbol...", "Ctrl+R", () => vm.RenameSymbolCommand.Execute(null));
        AddCommand("Edit", "Toggle Comment", "Ctrl+/", () => vm.ToggleCommentCommand.Execute(null));
        AddCommand("Edit", "Duplicate Line", "Ctrl+D", () => vm.DuplicateLineCommand.Execute(null));
        AddCommand("Edit", "Move Line Up", "Alt+Up", () => vm.MoveLineUpCommand.Execute(null));
        AddCommand("Edit", "Move Line Down", "Alt+Down", () => vm.MoveLineDownCommand.Execute(null));
        AddCommand("Edit", "Delete Line", "Ctrl+Shift+K", () => vm.DeleteLineCommand.Execute(null));

        // Edit - Refactoring commands
        AddCommand("Edit", "Extract Method...", "Ctrl+Shift+M", () => vm.ExtractMethodCommand.Execute(null));
        AddCommand("Edit", "Inline Method...", "Ctrl+Shift+I", () => vm.InlineMethodCommand.Execute(null));
        AddCommand("Edit", "Introduce Variable...", "Ctrl+Shift+V", () => vm.IntroduceVariableCommand.Execute(null));
        AddCommand("Edit", "Change Signature...", null, () => vm.ChangeSignatureCommand.Execute(null));
        AddCommand("Edit", "Encapsulate Field...", null, () => vm.EncapsulateFieldCommand.Execute(null));
        AddCommand("Edit", "Extract Interface...", null, () => vm.ExtractInterfaceCommand.Execute(null));
        AddCommand("Edit", "Generate Constructor...", null, () => vm.GenerateConstructorCommand.Execute(null));
        AddCommand("Edit", "Implement Interface...", "Ctrl+.", () => vm.ImplementInterfaceCommand.Execute(null));

        // View commands
        AddCommand("View", "Solution Explorer", null, () => vm.ShowSolutionExplorerCommand.Execute(null));
        AddCommand("View", "Output", "Ctrl+Alt+O", () => vm.ShowOutputCommand.Execute(null));
        AddCommand("View", "Error List", "Ctrl+Alt+E", () => vm.ShowErrorListCommand.Execute(null));
        AddCommand("View", "Find Results", null, () => vm.ShowFindResultsCommand.Execute(null));
        AddCommand("View", "Bookmarks", null, () => vm.ShowBookmarksCommand.Execute(null));

        // Build commands
        AddCommand("Build", "Build Project", "Ctrl+Shift+B", () => vm.BuildCommand.Execute(null));
        AddCommand("Build", "Rebuild Project", null, () => vm.RebuildCommand.Execute(null));
        AddCommand("Build", "Clean Project", null, () => vm.CleanCommand.Execute(null));
        AddCommand("Build", "Cancel Build", null, () => vm.CancelBuildCommand.Execute(null));

        // Debug commands
        AddCommand("Debug", "Start Debugging", "F5", () => vm.StartDebuggingCommand.Execute(null));
        AddCommand("Debug", "Start Without Debugging", "Ctrl+F5", () => vm.StartWithoutDebuggingCommand.Execute(null));
        AddCommand("Debug", "Run in External Console", "Ctrl+Shift+F5", () => vm.RunInExternalConsoleCommand.Execute(null));
        AddCommand("Debug", "Stop Debugging", "Shift+F5", () => vm.StopDebuggingCommand.Execute(null));
        AddCommand("Debug", "Continue", "F5", () => vm.ContinueCommand.Execute(null));
        AddCommand("Debug", "Step Over", "F10", () => vm.StepOverCommand.Execute(null));
        AddCommand("Debug", "Step Into", "F11", () => vm.StepIntoCommand.Execute(null));
        AddCommand("Debug", "Step Out", "Shift+F11", () => vm.StepOutCommand.Execute(null));
        AddCommand("Debug", "Run to Cursor", "Ctrl+F10", () => vm.RunToCursorCommand.Execute(null));
        AddCommand("Debug", "Toggle Breakpoint", "F9", () => vm.ToggleBreakpointCommand.Execute(null));
        AddCommand("Debug", "New Function Breakpoint...", "Ctrl+Shift+F9", () => vm.NewFunctionBreakpointCommand.Execute(null));
        AddCommand("Debug", "Exception Settings...", "Ctrl+Alt+X", () => vm.ShowExceptionSettingsCommand.Execute(null));

        // Debug Windows
        AddCommand("Debug", "Show Breakpoints", "Ctrl+Alt+B", () => vm.ShowBreakpointsCommand.Execute(null));
        AddCommand("Debug", "Show Call Stack", "Ctrl+Alt+C", () => vm.ShowCallStackCommand.Execute(null));
        AddCommand("Debug", "Show Variables", "Ctrl+Alt+V", () => vm.ShowVariablesCommand.Execute(null));
        AddCommand("Debug", "Show Watch", "Ctrl+Alt+W", () => vm.ShowWatchCommand.Execute(null));
        AddCommand("Debug", "Show Immediate Window", "Ctrl+Alt+I", () => vm.ShowImmediateWindowCommand.Execute(null));

        // Bookmarks
        AddCommand("Bookmarks", "Toggle Bookmark", "Ctrl+K", () => vm.ToggleBookmarkCommand.Execute(null));
        AddCommand("Bookmarks", "Next Bookmark", "F2", () => vm.NextBookmarkCommand.Execute(null));
        AddCommand("Bookmarks", "Previous Bookmark", "Shift+F2", () => vm.PreviousBookmarkCommand.Execute(null));
        AddCommand("Bookmarks", "Clear All Bookmarks", null, () => vm.ClearAllBookmarksCommand.Execute(null));

        // Selection
        AddCommand("Selection", "Expand Selection", null, () => vm.ExpandSelectionCommand.Execute(null));
        AddCommand("Selection", "Shrink Selection", null, () => vm.ShrinkSelectionCommand.Execute(null));

        UpdateFilteredCommands();
    }

    private void AddCommand(string category, string name, string? shortcut, Action execute)
    {
        _allCommands.Add(new CommandPaletteItem
        {
            Category = category,
            Name = name,
            Shortcut = shortcut,
            Execute = execute
        });
    }

    /// <summary>
    /// Resets the palette state for a fresh open.
    /// </summary>
    public void Open()
    {
        SearchText = "";
        SelectedIndex = 0;
        UpdateFilteredCommands();
    }

    partial void OnSearchTextChanged(string value)
    {
        UpdateFilteredCommands();
    }

    private void UpdateFilteredCommands()
    {
        FilteredCommands.Clear();

        var filter = SearchText?.Trim() ?? "";

        IEnumerable<CommandPaletteItem> items;

        if (string.IsNullOrEmpty(filter))
        {
            items = _allCommands;
        }
        else
        {
            items = _allCommands
                .Select(cmd => new { Command = cmd, Score = FuzzyMatch(cmd.DisplayName, filter) })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Command.DisplayName.Length)
                .Select(x => x.Command);
        }

        foreach (var item in items.Take(50))
        {
            FilteredCommands.Add(item);
        }

        if (FilteredCommands.Count > 0)
        {
            SelectedIndex = 0;
            SelectedItem = FilteredCommands[0];
        }
        else
        {
            SelectedItem = null;
        }
    }

    private static int FuzzyMatch(string text, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return 1;
        if (string.IsNullOrEmpty(text)) return 0;

        // Exact substring match (case insensitive)
        if (text.Contains(pattern, StringComparison.OrdinalIgnoreCase))
        {
            return 100 + (text.StartsWith(pattern, StringComparison.OrdinalIgnoreCase) ? 50 : 0);
        }

        // Fuzzy match - characters must appear in order
        var patternIdx = 0;
        var score = 0;
        var consecutive = 0;

        for (var i = 0; i < text.Length && patternIdx < pattern.Length; i++)
        {
            if (char.ToLowerInvariant(text[i]) == char.ToLowerInvariant(pattern[patternIdx]))
            {
                score += 10 + consecutive * 5;
                consecutive++;
                patternIdx++;

                // Bonus for matching after separator (space, colon)
                if (i > 0 && (text[i - 1] == ' ' || text[i - 1] == ':'))
                {
                    score += 15;
                }
            }
            else
            {
                consecutive = 0;
            }
        }

        return patternIdx == pattern.Length ? score : 0;
    }

    [RelayCommand]
    private void MoveUp()
    {
        if (FilteredCommands.Count == 0) return;
        SelectedIndex = Math.Max(0, SelectedIndex - 1);
        SelectedItem = FilteredCommands[SelectedIndex];
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (FilteredCommands.Count == 0) return;
        SelectedIndex = Math.Min(FilteredCommands.Count - 1, SelectedIndex + 1);
        SelectedItem = FilteredCommands[SelectedIndex];
    }

    [RelayCommand]
    private void Confirm()
    {
        if (SelectedItem != null)
        {
            CommandExecuted?.Invoke(this, SelectedItem);
        }
    }

    [RelayCommand]
    private void Dismiss()
    {
        Dismissed?.Invoke(this, EventArgs.Empty);
    }
}
