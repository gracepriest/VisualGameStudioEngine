using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

/// <summary>
/// The mode the command palette is currently operating in.
/// </summary>
public enum CommandPaletteMode
{
    /// <summary>File search mode (Ctrl+P, no prefix). Lists project files with fuzzy matching.</summary>
    File,
    /// <summary>Command mode (> prefix). Lists all IDE commands.</summary>
    Command,
    /// <summary>Go-to-line mode (: prefix). Enter a line number to jump to.</summary>
    GoToLine,
    /// <summary>Symbol search mode (@ prefix). Lists symbols in the current file.</summary>
    Symbol,
    /// <summary>Workspace symbol search mode (# prefix). Lists symbols across all files.</summary>
    WorkspaceSymbol,
    /// <summary>Help mode (? prefix). Shows available prefixes and their descriptions.</summary>
    Help
}

/// <summary>
/// Represents a single command entry in the command palette.
/// </summary>
public class CommandPaletteItem
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string? Shortcut { get; set; }
    public Action? Execute { get; set; }
    /// <summary>
    /// For file mode: the full path to the file. Null for command items.
    /// </summary>
    public string? FilePath { get; set; }
    /// <summary>
    /// For file mode: a short icon label based on file extension.
    /// </summary>
    public string FileIcon { get; set; } = "";
    /// <summary>
    /// Indicates this item is in the recently-used section.
    /// </summary>
    public bool IsRecentlyUsed { get; set; }
    /// <summary>
    /// Unique identifier for MRU tracking.
    /// </summary>
    public string CommandId => string.IsNullOrEmpty(Category) ? Name : $"{Category}: {Name}";
    public string DisplayName => string.IsNullOrEmpty(Category) ? Name : $"{Category}: {Name}";
    /// <summary>
    /// For symbol mode: the line number in the file.
    /// </summary>
    public int Line { get; set; }
    /// <summary>
    /// Indices of characters in the Name that matched the fuzzy search pattern.
    /// Can be used by the view to highlight matched characters.
    /// </summary>
    public List<int> MatchedIndices { get; set; } = new();
}

/// <summary>
/// ViewModel for the Command Palette dialog (Ctrl+Shift+P / Ctrl+P).
/// Supports multiple modes: file search, command palette, go-to-line, symbol search,
/// workspace symbol search, and help.
/// Features MRU tracking, command history, fuzzy matching with character highlighting.
/// </summary>
public partial class CommandPaletteViewModel : ViewModelBase
{
    private readonly List<CommandPaletteItem> _allCommands = new();
    private readonly List<CommandPaletteItem> _allFiles = new();
    private readonly List<CommandPaletteItem> _allSymbols = new();
    private MainWindowViewModel? _mainVm;
    private ISettingsService? _settingsService;
    private IProjectService? _projectService;

    // MRU tracking
    private List<string> _mruCommandIds = new();
    private List<string> _recentFiles = new();
    private const int MaxMruItems = 20;
    private const int MaxRecentFiles = 10;
    private const string MruSettingsKey = "commandPalette.mru";
    private const string RecentFilesSettingsKey = "commandPalette.recentFiles";

    // Command history for up/down navigation
    private List<string> _commandHistory = new();
    private int _historyIndex = -1;
    private const int MaxHistoryItems = 50;
    private const string CommandHistorySettingsKey = "commandPalette.commandHistory";

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private ObservableCollection<CommandPaletteItem> _filteredCommands = new();

    [ObservableProperty]
    private CommandPaletteItem? _selectedItem;

    [ObservableProperty]
    private int _selectedIndex;

    [ObservableProperty]
    private CommandPaletteMode _currentMode = CommandPaletteMode.Command;

    [ObservableProperty]
    private string _modePrefix = ">";

    [ObservableProperty]
    private string _modeName = "Command Palette";

    [ObservableProperty]
    private string _watermarkText = "Type a command name...";

    [ObservableProperty]
    private string _sectionHeader = "";

    public event EventHandler<CommandPaletteItem>? CommandExecuted;
    public event EventHandler? Dismissed;
    /// <summary>
    /// Raised when the user wants to go to a specific line number.
    /// </summary>
    public event EventHandler<int>? GoToLineRequested;
    /// <summary>
    /// Raised when the user wants to open a file by path.
    /// </summary>
    public event EventHandler<string>? FileOpenRequested;
    /// <summary>
    /// Raised when the user wants to navigate to a symbol at a specific file and line.
    /// </summary>
    public event EventHandler<(string FilePath, int Line)>? SymbolNavigationRequested;

    /// <summary>
    /// Loads MRU, recent files, and command history data from the settings service.
    /// Call this before Open() to enable persistence.
    /// </summary>
    public void LoadMruData(ISettingsService? settingsService)
    {
        _settingsService = settingsService;
        if (settingsService == null) return;

        try
        {
            var mruJson = settingsService.Get<string>(MruSettingsKey, "[]");
            _mruCommandIds = JsonSerializer.Deserialize<List<string>>(mruJson) ?? new();
        }
        catch { _mruCommandIds = new(); }

        try
        {
            var recentJson = settingsService.Get<string>(RecentFilesSettingsKey, "[]");
            _recentFiles = JsonSerializer.Deserialize<List<string>>(recentJson) ?? new();
        }
        catch { _recentFiles = new(); }

        try
        {
            var historyJson = settingsService.Get<string>(CommandHistorySettingsKey, "[]");
            _commandHistory = JsonSerializer.Deserialize<List<string>>(historyJson) ?? new();
        }
        catch { _commandHistory = new(); }
    }

    private void SaveMruData()
    {
        if (_settingsService == null) return;

        try
        {
            _settingsService.Set(MruSettingsKey, JsonSerializer.Serialize(_mruCommandIds));
            _settingsService.Set(RecentFilesSettingsKey, JsonSerializer.Serialize(_recentFiles));
            _settingsService.Set(CommandHistorySettingsKey, JsonSerializer.Serialize(_commandHistory));
        }
        catch { /* ignore save errors */ }
    }

    private void RecordCommandUsage(CommandPaletteItem item)
    {
        var id = item.CommandId;

        // Update MRU list
        _mruCommandIds.Remove(id);
        _mruCommandIds.Insert(0, id);
        if (_mruCommandIds.Count > MaxMruItems)
            _mruCommandIds.RemoveRange(MaxMruItems, _mruCommandIds.Count - MaxMruItems);

        // Update command history
        _commandHistory.Remove(id);
        _commandHistory.Insert(0, id);
        if (_commandHistory.Count > MaxHistoryItems)
            _commandHistory.RemoveRange(MaxHistoryItems, _commandHistory.Count - MaxHistoryItems);

        SaveMruData();
    }

    private void RecordFileOpen(string filePath)
    {
        _recentFiles.Remove(filePath);
        _recentFiles.Insert(0, filePath);
        if (_recentFiles.Count > MaxRecentFiles)
            _recentFiles.RemoveRange(MaxRecentFiles, _recentFiles.Count - MaxRecentFiles);

        SaveMruData();
    }

    /// <summary>
    /// Registers all available commands from the MainWindowViewModel.
    /// This includes all menu items, editor commands, Git commands, debug commands, etc.
    /// </summary>
    public void RegisterCommands(MainWindowViewModel vm)
    {
        _mainVm = vm;
        _allCommands.Clear();

        // --- File commands ---
        AddCommand("File", "New Project...", "Ctrl+Shift+N", () => vm.NewProjectCommand.Execute(null));
        AddCommand("File", "New File...", null, () => vm.NewFileCommand.Execute(null));
        AddCommand("File", "New Window", null, () => vm.NewWindowCommand.Execute(null));
        AddCommand("File", "Open Project...", "Ctrl+Shift+O", () => vm.OpenProjectCommand.Execute(null));
        AddCommand("File", "Open File...", "Ctrl+O", () => vm.OpenFileCommand.Execute(null));
        AddCommand("File", "Open Folder...", null, () => vm.OpenFolderCommand.Execute(null));
        AddCommand("File", "Open Workspace...", null, () => vm.OpenWorkspaceCommand.Execute(null));
        AddCommand("File", "Add Folder to Workspace...", null, () => vm.AddFolderToWorkspaceCommand.Execute(null));
        AddCommand("File", "Save", "Ctrl+S", () => vm.SaveCommand.Execute(null));
        AddCommand("File", "Save As...", null, () => vm.SaveAsCommand.Execute(null));
        AddCommand("File", "Save All", "Ctrl+Shift+S", () => vm.SaveAllCommand.Execute(null));
        AddCommand("File", "Save Workspace As...", null, () => vm.SaveWorkspaceAsCommand.Execute(null));
        AddCommand("File", "Close Editor", "Ctrl+W", () => vm.CloseEditorCommand.Execute(null));
        AddCommand("File", "Close Folder", null, () => vm.CloseFolderCommand.Execute(null));
        AddCommand("File", "Clear Recent Projects", null, () => vm.ClearRecentProjectsCommand.Execute(null));
        AddCommand("File", "Exit", "Alt+F4", () => vm.ExitCommand.Execute(null));

        // --- Edit commands ---
        AddCommand("Edit", "Find...", "Ctrl+F", () => vm.FindCommand.Execute(null));
        AddCommand("Edit", "Replace...", "Ctrl+H", () => vm.ReplaceCommand.Execute(null));
        AddCommand("Edit", "Find in Files...", "Ctrl+Shift+F", () => vm.ShowFindInFilesCommand.Execute(null));
        AddCommand("Edit", "Replace in Files...", null, () => vm.ReplaceInFilesCommand.Execute(null));
        AddCommand("Edit", "Select All", "Ctrl+A", () => vm.SelectAllCommand.Execute(null));
        AddCommand("Edit", "Go to Definition", "F12", () => vm.GoToDefinitionCommand.Execute(null));
        AddCommand("Edit", "Go to Declaration", null, () => vm.GoToDeclarationCommand.Execute(null));
        AddCommand("Edit", "Go to Type Definition", null, () => vm.GoToTypeDefinitionCommand.Execute(null));
        AddCommand("Edit", "Go to Implementation", null, () => vm.GoToImplementationCommand.Execute(null));
        AddCommand("Edit", "Peek Definition", null, () => vm.PeekDefinitionCommand.Execute(null));
        AddCommand("Edit", "Find All References", "Shift+F12", () => vm.FindReferencesCommand.Execute(null));
        AddCommand("Edit", "Show Call Hierarchy", null, () => vm.ShowCallHierarchyCommand.Execute(null));
        AddCommand("Edit", "Show Type Hierarchy", null, () => vm.ShowTypeHierarchyCommand.Execute(null));
        AddCommand("Edit", "Go to Line...", "Ctrl+G", () => vm.GoToLineCommand.Execute(null));
        AddCommand("Edit", "Go to Symbol...", "Ctrl+T", () => vm.GoToSymbolCommand.Execute(null));
        AddCommand("Edit", "Go to Workspace Symbol...", null, () => vm.GoToWorkspaceSymbolCommand.Execute(null));
        AddCommand("Edit", "Go to Bracket", null, () => vm.GoToBracketCommand.Execute(null));
        AddCommand("Edit", "Rename Symbol...", "Ctrl+R", () => vm.RenameSymbolCommand.Execute(null));
        AddCommand("Edit", "Toggle Comment", "Ctrl+/", () => vm.ToggleCommentCommand.Execute(null));
        AddCommand("Edit", "Toggle Block Comment", null, () => vm.ToggleBlockCommentCommand.Execute(null));
        AddCommand("Edit", "Duplicate Line", "Ctrl+D", () => vm.DuplicateLineCommand.Execute(null));
        AddCommand("Edit", "Copy Line Up", null, () => vm.CopyLineUpCommand.Execute(null));
        AddCommand("Edit", "Copy Line Down", null, () => vm.CopyLineDownCommand.Execute(null));
        AddCommand("Edit", "Move Line Up", "Alt+Up", () => vm.MoveLineUpCommand.Execute(null));
        AddCommand("Edit", "Move Line Down", "Alt+Down", () => vm.MoveLineDownCommand.Execute(null));
        AddCommand("Edit", "Delete Line", "Ctrl+Shift+K", () => vm.DeleteLineCommand.Execute(null));
        AddCommand("Edit", "Add Cursor Above", "Ctrl+Alt+Up", () => vm.AddCursorAboveCommand.Execute(null));
        AddCommand("Edit", "Add Cursor Below", "Ctrl+Alt+Down", () => vm.AddCursorBelowCommand.Execute(null));
        AddCommand("Edit", "Add Cursors to Line Ends", null, () => vm.AddCursorsToLineEndsCommand.Execute(null));
        AddCommand("Edit", "Surround With...", null, () => vm.SurroundWithCommand.Execute(null));

        // --- Refactoring commands ---
        AddCommand("Refactor", "Extract Method...", "Ctrl+Shift+M", () => vm.ExtractMethodCommand.Execute(null));
        AddCommand("Refactor", "Inline Method...", "Ctrl+Shift+I", () => vm.InlineMethodCommand.Execute(null));
        AddCommand("Refactor", "Introduce Variable...", "Ctrl+Shift+V", () => vm.IntroduceVariableCommand.Execute(null));
        AddCommand("Refactor", "Extract Constant...", null, () => vm.ExtractConstantCommand.Execute(null));
        AddCommand("Refactor", "Inline Constant...", null, () => vm.InlineConstantCommand.Execute(null));
        AddCommand("Refactor", "Introduce Field...", null, () => vm.IntroduceFieldCommand.Execute(null));
        AddCommand("Refactor", "Inline Field...", null, () => vm.InlineFieldCommand.Execute(null));
        AddCommand("Refactor", "Change Signature...", "Ctrl+Shift+-", () => vm.ChangeSignatureCommand.Execute(null));
        AddCommand("Refactor", "Encapsulate Field...", null, () => vm.EncapsulateFieldCommand.Execute(null));
        AddCommand("Refactor", "Extract Interface...", null, () => vm.ExtractInterfaceCommand.Execute(null));
        AddCommand("Refactor", "Generate Constructor...", null, () => vm.GenerateConstructorCommand.Execute(null));
        AddCommand("Refactor", "Implement Interface...", "Ctrl+.", () => vm.ImplementInterfaceCommand.Execute(null));
        AddCommand("Refactor", "Override Method...", null, () => vm.OverrideMethodCommand.Execute(null));
        AddCommand("Refactor", "Add Parameter...", null, () => vm.AddParameterCommand.Execute(null));
        AddCommand("Refactor", "Safe Delete...", null, () => vm.SafeDeleteCommand.Execute(null));
        AddCommand("Refactor", "Pull Members Up...", null, () => vm.PullMembersUpCommand.Execute(null));
        AddCommand("Refactor", "Push Members Down...", null, () => vm.PushMembersDownCommand.Execute(null));
        AddCommand("Refactor", "Use Base Type...", null, () => vm.UseBaseTypeCommand.Execute(null));
        AddCommand("Refactor", "Convert to Interface...", null, () => vm.ConvertToInterfaceCommand.Execute(null));
        AddCommand("Refactor", "Invert If...", null, () => vm.InvertIfCommand.Execute(null));
        AddCommand("Refactor", "Convert to Select Case...", null, () => vm.ConvertToSelectCaseCommand.Execute(null));
        AddCommand("Refactor", "Split Declaration...", null, () => vm.SplitDeclarationCommand.Execute(null));
        AddCommand("Refactor", "Move Type to File...", null, () => vm.MoveTypeToFileCommand.Execute(null));

        // --- Selection commands ---
        AddCommand("Selection", "Expand Selection", null, () => vm.ExpandSelectionCommand.Execute(null));
        AddCommand("Selection", "Shrink Selection", null, () => vm.ShrinkSelectionCommand.Execute(null));

        // --- View commands ---
        AddCommand("View", "Solution Explorer", null, () => vm.ShowSolutionExplorerCommand.Execute(null));
        AddCommand("View", "Search", null, () => vm.ShowSearchPanelCommand.Execute(null));
        AddCommand("View", "Source Control", null, () => vm.ShowSourceControlCommand.Execute(null));
        AddCommand("View", "Debug Panel", null, () => vm.ShowDebugPanelCommand.Execute(null));
        AddCommand("View", "Extensions", null, () => vm.ShowExtensionsCommand.Execute(null));
        AddCommand("View", "Output", "Ctrl+Alt+O", () => vm.ShowOutputCommand.Execute(null));
        AddCommand("View", "Error List", "Ctrl+Alt+E", () => vm.ShowErrorListCommand.Execute(null));
        AddCommand("View", "Problems", null, () => vm.ShowProblemsCommand.Execute(null));
        AddCommand("View", "Terminal", null, () => vm.ShowTerminalCommand.Execute(null));
        AddCommand("View", "Create New Terminal", null, () => vm.CreateNewTerminalCommand.Execute(null));
        AddCommand("View", "Find Results", null, () => vm.ShowFindResultsCommand.Execute(null));
        AddCommand("View", "Bookmarks", null, () => vm.ShowBookmarksCommand.Execute(null));
        AddCommand("View", "Toggle Full Screen", "F11", () => vm.ToggleFullScreenCommand.Execute(null));
        AddCommand("View", "Toggle Menu Bar", null, () => vm.ToggleMenuBarCommand.Execute(null));
        AddCommand("View", "Toggle Side Bar", "Ctrl+B", () => vm.ToggleSideBarCommand.Execute(null));
        AddCommand("View", "Toggle Status Bar", null, () => vm.ToggleStatusBarCommand.Execute(null));
        AddCommand("View", "Toggle Panel", null, () => vm.TogglePanelCommand.Execute(null));
        AddCommand("View", "Toggle Minimap", null, () => vm.ToggleMinimapCommand.Execute(null));
        AddCommand("View", "Toggle Breadcrumbs", null, () => vm.ToggleBreadcrumbsCommand.Execute(null));
        AddCommand("View", "Toggle Sticky Scroll", null, () => vm.ToggleStickyScrollCommand.Execute(null));
        AddCommand("View", "Toggle Word Wrap", null, () => vm.ToggleWordWrapCommand.Execute(null));
        AddCommand("View", "Toggle Whitespace", null, () => vm.ToggleWhitespaceCommand.Execute(null));
        AddCommand("View", "Toggle Column Selection Mode", "Alt+Shift+Insert", () => vm.ToggleColumnSelectionModeCommand.Execute(null));
        AddCommand("View", "Toggle Zen Mode", null, () => vm.ToggleZenModeCommand.Execute(null));
        AddCommand("View", "Zoom In", "Ctrl++", () => vm.ZoomInCommand.Execute(null));
        AddCommand("View", "Zoom Out", "Ctrl+-", () => vm.ZoomOutCommand.Execute(null));
        AddCommand("View", "Reset Zoom", "Ctrl+0", () => vm.ZoomResetCommand.Execute(null));
        AddCommand("View", "Split Editor Right", null, () => vm.SplitEditorRightCommand.Execute(null));
        AddCommand("View", "Split Editor Down", null, () => vm.SplitEditorDownCommand.Execute(null));
        AddCommand("View", "Single Editor Layout", null, () => vm.SingleEditorLayoutCommand.Execute(null));
        AddCommand("View", "Two Columns Layout", null, () => vm.TwoColumnsLayoutCommand.Execute(null));
        AddCommand("View", "Grid Layout", null, () => vm.GridLayoutCommand.Execute(null));

        // --- Focus commands ---
        AddCommand("View", "Focus Solution Explorer", null, () => vm.FocusSolutionExplorerCommand.Execute(null));
        AddCommand("View", "Focus Editor", null, () => vm.FocusEditorCommand.Execute(null));
        AddCommand("View", "Focus Output", null, () => vm.FocusOutputCommand.Execute(null));
        AddCommand("View", "Focus Terminal", null, () => vm.FocusTerminalCommand.Execute(null));
        AddCommand("View", "Focus Error List", null, () => vm.FocusErrorListCommand.Execute(null));
        AddCommand("View", "Focus Variables", null, () => vm.FocusVariablesCommand.Execute(null));
        AddCommand("View", "Focus Next Panel", null, () => vm.FocusNextPanelCommand.Execute(null));
        AddCommand("View", "Focus Previous Panel", null, () => vm.FocusPreviousPanelCommand.Execute(null));

        // --- Navigate commands ---
        AddCommand("Navigate", "Go Back", "Alt+Left", () => vm.NavigateBackCommand.Execute(null));
        AddCommand("Navigate", "Go Forward", "Alt+Right", () => vm.NavigateForwardCommand.Execute(null));
        AddCommand("Navigate", "Go to Next Error", "F8", () => vm.GoToNextErrorCommand.Execute(null));
        AddCommand("Navigate", "Go to Previous Error", "Shift+F8", () => vm.GoToPreviousErrorCommand.Execute(null));

        // --- Build commands ---
        AddCommand("Build", "Build Project", "Ctrl+Shift+B", () => vm.BuildCommand.Execute(null));
        AddCommand("Build", "Rebuild Project", null, () => vm.RebuildCommand.Execute(null));
        AddCommand("Build", "Clean Project", null, () => vm.CleanCommand.Execute(null));
        AddCommand("Build", "Cancel Build", null, () => vm.CancelBuildCommand.Execute(null));

        // --- Debug commands ---
        AddCommand("Debug", "Start Debugging", "F5", () => vm.StartDebuggingCommand.Execute(null));
        AddCommand("Debug", "Start Without Debugging", "Ctrl+F5", () => vm.StartWithoutDebuggingCommand.Execute(null));
        AddCommand("Debug", "Run in External Console", null, () => vm.RunInExternalConsoleCommand.Execute(null));
        AddCommand("Debug", "Attach to Process...", null, () => vm.AttachToProcessCommand.Execute(null));
        AddCommand("Debug", "Stop Debugging", "Shift+F5", () => vm.StopDebuggingCommand.Execute(null));
        AddCommand("Debug", "Restart Debugging", "Ctrl+Shift+F5", () => vm.RestartDebuggingCommand.Execute(null));
        AddCommand("Debug", "Continue", "F5", () => vm.ContinueCommand.Execute(null));
        AddCommand("Debug", "Pause", null, () => vm.PauseCommand.Execute(null));
        AddCommand("Debug", "Step Over", "F10", () => vm.StepOverCommand.Execute(null));
        AddCommand("Debug", "Step Into", "F11", () => vm.StepIntoCommand.Execute(null));
        AddCommand("Debug", "Step Out", "Shift+F11", () => vm.StepOutCommand.Execute(null));
        AddCommand("Debug", "Run to Cursor", "Ctrl+F10", () => vm.RunToCursorCommand.Execute(null));
        AddCommand("Debug", "Set Next Statement", null, () => vm.SetNextStatementCommand.Execute(null));
        AddCommand("Debug", "Toggle Breakpoint", "F9", () => vm.ToggleBreakpointCommand.Execute(null));
        AddCommand("Debug", "New Function Breakpoint...", "Ctrl+Shift+F9", () => vm.NewFunctionBreakpointCommand.Execute(null));
        AddCommand("Debug", "New Conditional Breakpoint...", null, () => vm.NewConditionalBreakpointCommand.Execute(null));
        AddCommand("Debug", "New Logpoint...", null, () => vm.NewLogpointCommand.Execute(null));
        AddCommand("Debug", "Add Data Breakpoint...", null, () => vm.AddDataBreakpointCommand.Execute(null));
        AddCommand("Debug", "Exception Settings...", "Ctrl+Alt+X", () => vm.ShowExceptionSettingsCommand.Execute(null));
        AddCommand("Debug", "Edit Launch Configuration...", null, () => vm.EditLaunchConfigurationCommand.Execute(null));

        // --- Debug Windows ---
        AddCommand("Debug", "Show Breakpoints", "Ctrl+Alt+B", () => vm.ShowBreakpointsCommand.Execute(null));
        AddCommand("Debug", "Show Call Stack", "Ctrl+Alt+C", () => vm.ShowCallStackCommand.Execute(null));
        AddCommand("Debug", "Show Variables", "Ctrl+Alt+V", () => vm.ShowVariablesCommand.Execute(null));
        AddCommand("Debug", "Show Watch", "Ctrl+Alt+W", () => vm.ShowWatchCommand.Execute(null));
        AddCommand("Debug", "Show Immediate Window", "Ctrl+Alt+I", () => vm.ShowImmediateWindowCommand.Execute(null));

        // --- Bookmarks ---
        AddCommand("Bookmarks", "Toggle Bookmark", "Ctrl+K", () => vm.ToggleBookmarkCommand.Execute(null));
        AddCommand("Bookmarks", "Next Bookmark", "F2", () => vm.NextBookmarkCommand.Execute(null));
        AddCommand("Bookmarks", "Previous Bookmark", "Shift+F2", () => vm.PreviousBookmarkCommand.Execute(null));
        AddCommand("Bookmarks", "Clear All Bookmarks", null, () => vm.ClearAllBookmarksCommand.Execute(null));

        // --- Git / Source Control ---
        AddCommand("Git", "Show Source Control", null, () => vm.ShowSourceControlCommand.Execute(null));
        AddCommand("Git", "Commit", null, () => vm.GitChanges?.CommitCommand.Execute(null));
        AddCommand("Git", "Pull", null, () => vm.GitChanges?.PullCommand.Execute(null));
        AddCommand("Git", "Push", null, () => vm.GitChanges?.PushCommand.Execute(null));
        AddCommand("Git", "Fetch", null, () => vm.GitBranches?.FetchCommand.Execute(null));
        AddCommand("Git", "Stage All", null, () => vm.GitChanges?.StageAllCommand.Execute(null));
        AddCommand("Git", "Unstage All", null, () => vm.GitChanges?.UnstageAllCommand.Execute(null));
        AddCommand("Git", "Create Branch...", null, () => vm.GitChanges?.CreateBranchCommand.Execute(null));
        AddCommand("Git", "Stash Changes", null, () => vm.GitStash?.StashChangesCommand.Execute(null));
        AddCommand("Git", "Initialize Repository", null, () => vm.GitChanges?.InitRepositoryCommand.Execute(null));

        // --- Terminal ---
        AddCommand("Terminal", "New Terminal", null, () => vm.CreateNewTerminalCommand.Execute(null));
        AddCommand("Terminal", "Split Terminal", null, () => vm.SplitTerminalCommand.Execute(null));
        AddCommand("Terminal", "Configure Default Shell...", null, () => vm.ConfigureDefaultShellCommand.Execute(null));

        // --- Tasks ---
        AddCommand("Tasks", "Run Task...", null, () => vm.RunTaskCommand.Execute(null));
        AddCommand("Tasks", "Run Build Task...", null, () => vm.RunBuildTaskCommand.Execute(null));
        AddCommand("Tasks", "Run Test Task...", null, () => vm.RunTestTaskCommand.Execute(null));
        AddCommand("Tasks", "Configure Tasks...", null, () => vm.ConfigureTasksCommand.Execute(null));

        // --- Preferences ---
        AddCommand("Preferences", "Settings...", null, () => vm.SettingsCommand.Execute(null));
        AddCommand("Preferences", "Open Settings (JSON)...", null, () => vm.OpenSettingsCommand.Execute(null));
        AddCommand("Preferences", "Color Theme...", null, () => vm.OpenColorThemeCommand.Execute(null));
        AddCommand("Preferences", "File Icon Theme...", null, () => vm.OpenFileIconThemeCommand.Execute(null));
        AddCommand("Preferences", "Keyboard Shortcuts...", null, () => vm.ShowKeyboardShortcutsCommand.Execute(null));

        // --- Help ---
        AddCommand("Help", "Welcome", null, () => vm.ShowWelcomeCommand.Execute(null));
        AddCommand("Help", "Documentation", null, () => vm.ShowDocumentationCommand.Execute(null));
        AddCommand("Help", "Release Notes", null, () => vm.ShowReleaseNotesCommand.Execute(null));
        AddCommand("Help", "Report Issue", null, () => vm.ReportIssueCommand.Execute(null));
        AddCommand("Help", "View License", null, () => vm.ViewLicenseCommand.Execute(null));
        AddCommand("Help", "Check for Updates", null, () => vm.CheckForUpdatesCommand.Execute(null));
        AddCommand("Help", "About", null, () => vm.ShowAboutCommand.Execute(null));
    }

    /// <summary>
    /// Registers commands contributed by extensions so they appear in the command palette.
    /// </summary>
    public void RegisterExtensionCommands(IExtensionService extensionService)
    {
        // Remove any previously registered extension commands
        _allCommands.RemoveAll(c => c.Category?.StartsWith("[Ext]") == true || c.Category?.EndsWith(" (Extension)") == true);

        var contributedCommands = extensionService.GetContributedCommands();
        foreach (var cmd in contributedCommands)
        {
            var commandId = cmd.CommandId;
            var category = cmd.Category ?? cmd.ExtensionId;
            var extensionSvc = extensionService;

            _allCommands.Add(new CommandPaletteItem
            {
                Category = category,
                Name = cmd.Title,
                Shortcut = GetShortcutForCommand(extensionService, commandId),
                Execute = () => _ = extensionSvc.ExecuteContributedCommandAsync(commandId)
            });
        }
    }

    private static string? GetShortcutForCommand(IExtensionService extensionService, string commandId)
    {
        var keybindings = extensionService.GetContributedKeybindings();
        var kb = keybindings.FirstOrDefault(k => k.CommandId == commandId);
        return kb?.Key;
    }

    /// <summary>
    /// Populates the file list from the current project and its directory.
    /// Enumerates .bas, .bl, and .blproj files with file icons and relative paths.
    /// </summary>
    public void RegisterFiles(IProjectService projectService)
    {
        _projectService = projectService;
        _allFiles.Clear();

        var project = projectService.CurrentProject;
        if (project == null) return;

        var projectDir = project.ProjectDirectory;
        if (string.IsNullOrEmpty(projectDir) || !Directory.Exists(projectDir)) return;

        // Collect files from project items first
        var addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in project.Items.Where(i =>
            i.ItemType == ProjectItemType.Compile || i.ItemType == ProjectItemType.Content))
        {
            var fullPath = Path.IsPathRooted(item.Include)
                ? item.Include
                : Path.Combine(projectDir, item.Include);

            if (File.Exists(fullPath) && addedPaths.Add(fullPath))
            {
                var relativePath = Path.GetRelativePath(projectDir, fullPath);
                _allFiles.Add(new CommandPaletteItem
                {
                    Name = Path.GetFileName(fullPath),
                    Category = Path.GetDirectoryName(relativePath) ?? "",
                    FilePath = fullPath,
                    FileIcon = GetFileIcon(Path.GetFileName(fullPath))
                });
            }
        }

        // Also scan directory for .bas, .bl, .blproj files not yet in the project
        try
        {
            var extensions = new[] { "*.bas", "*.bl", "*.blproj" };
            foreach (var ext in extensions)
            {
                foreach (var file in Directory.EnumerateFiles(projectDir, ext, SearchOption.AllDirectories))
                {
                    if (addedPaths.Add(file))
                    {
                        var relativePath = Path.GetRelativePath(projectDir, file);
                        _allFiles.Add(new CommandPaletteItem
                        {
                            Name = Path.GetFileName(file),
                            Category = Path.GetDirectoryName(relativePath) ?? "",
                            FilePath = file,
                            FileIcon = GetFileIcon(Path.GetFileName(file))
                        });
                    }
                }
            }
        }
        catch
        {
            // Directory enumeration may fail for inaccessible directories
        }
    }

    /// <summary>
    /// Sets the path of the currently active document for symbol search.
    /// </summary>
    public string? ActiveDocumentPath { get; set; }

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
    /// Resets the palette state for a fresh open in the specified mode.
    /// </summary>
    public void Open(CommandPaletteMode mode = CommandPaletteMode.Command)
    {
        CurrentMode = mode;
        SearchText = "";
        SelectedIndex = 0;
        _historyIndex = -1;
        _allSymbols.Clear(); // Force reload on next symbol search
        ApplyModeSettings();
        UpdateFilteredItems();
    }

    private void ApplyModeSettings()
    {
        switch (CurrentMode)
        {
            case CommandPaletteMode.File:
                ModePrefix = "";
                ModeName = "Quick Open";
                WatermarkText = "Type a file name... (> commands, : line, @ symbol, # workspace, ? help)";
                break;
            case CommandPaletteMode.Command:
                ModePrefix = ">";
                ModeName = "Command Palette";
                WatermarkText = "Type a command name...";
                break;
            case CommandPaletteMode.GoToLine:
                ModePrefix = ":";
                ModeName = "Go to Line";
                WatermarkText = "Type a line number...";
                break;
            case CommandPaletteMode.Symbol:
                ModePrefix = "@";
                ModeName = "Go to Symbol in File";
                WatermarkText = "Type a symbol name...";
                break;
            case CommandPaletteMode.WorkspaceSymbol:
                ModePrefix = "#";
                ModeName = "Go to Symbol in Workspace";
                WatermarkText = "Type a symbol name to search across all files...";
                break;
            case CommandPaletteMode.Help:
                ModePrefix = "?";
                ModeName = "Help";
                WatermarkText = "";
                break;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        _historyIndex = -1; // Reset history navigation on typed input

        // Detect mode switches based on prefix typed
        if (CurrentMode == CommandPaletteMode.File)
        {
            if (value.StartsWith(">"))
            {
                CurrentMode = CommandPaletteMode.Command;
                ApplyModeSettings();
                SearchText = value.Substring(1);
                return;
            }
            else if (value.StartsWith(":"))
            {
                CurrentMode = CommandPaletteMode.GoToLine;
                ApplyModeSettings();
                SearchText = value.Substring(1);
                return;
            }
            else if (value.StartsWith("@"))
            {
                CurrentMode = CommandPaletteMode.Symbol;
                ApplyModeSettings();
                SearchText = value.Substring(1);
                return;
            }
            else if (value.StartsWith("#"))
            {
                CurrentMode = CommandPaletteMode.WorkspaceSymbol;
                ApplyModeSettings();
                SearchText = value.Substring(1);
                return;
            }
            else if (value.StartsWith("?"))
            {
                CurrentMode = CommandPaletteMode.Help;
                ApplyModeSettings();
                SearchText = value.Substring(1);
                return;
            }
        }
        else if (CurrentMode == CommandPaletteMode.Command)
        {
            if (value.StartsWith(":"))
            {
                CurrentMode = CommandPaletteMode.GoToLine;
                ApplyModeSettings();
                SearchText = value.Substring(1);
                return;
            }
            else if (value.StartsWith("@"))
            {
                CurrentMode = CommandPaletteMode.Symbol;
                ApplyModeSettings();
                SearchText = value.Substring(1);
                return;
            }
            else if (value.StartsWith("#"))
            {
                CurrentMode = CommandPaletteMode.WorkspaceSymbol;
                ApplyModeSettings();
                SearchText = value.Substring(1);
                return;
            }
            else if (value.StartsWith("?"))
            {
                CurrentMode = CommandPaletteMode.Help;
                ApplyModeSettings();
                SearchText = value.Substring(1);
                return;
            }
        }

        UpdateFilteredItems();
    }

    private void UpdateFilteredItems()
    {
        switch (CurrentMode)
        {
            case CommandPaletteMode.File:
                UpdateFilteredFiles();
                break;
            case CommandPaletteMode.Command:
                UpdateFilteredCommands();
                break;
            case CommandPaletteMode.GoToLine:
                UpdateGoToLine();
                break;
            case CommandPaletteMode.Symbol:
                UpdateSymbolSearch();
                break;
            case CommandPaletteMode.WorkspaceSymbol:
                UpdateWorkspaceSymbolSearch();
                break;
            case CommandPaletteMode.Help:
                UpdateHelp();
                break;
        }
    }

    private void UpdateFilteredFiles()
    {
        FilteredCommands.Clear();
        SectionHeader = "";

        var filter = SearchText?.Trim() ?? "";

        if (string.IsNullOrEmpty(filter))
        {
            // Show recently opened files first
            var recentItems = new List<CommandPaletteItem>();
            foreach (var recentPath in _recentFiles)
            {
                var fileItem = _allFiles.FirstOrDefault(f =>
                    string.Equals(f.FilePath, recentPath, StringComparison.OrdinalIgnoreCase));
                if (fileItem != null)
                {
                    recentItems.Add(new CommandPaletteItem
                    {
                        Name = fileItem.Name,
                        Category = fileItem.Category,
                        FilePath = fileItem.FilePath,
                        FileIcon = fileItem.FileIcon,
                        IsRecentlyUsed = true
                    });
                }
            }

            if (recentItems.Count > 0)
            {
                SectionHeader = "recently opened";
                foreach (var item in recentItems.Take(MaxRecentFiles))
                    FilteredCommands.Add(item);
            }

            // Add remaining files
            var recentPaths = new HashSet<string>(_recentFiles, StringComparer.OrdinalIgnoreCase);
            foreach (var item in _allFiles.Where(f => !recentPaths.Contains(f.FilePath ?? "")).Take(50 - FilteredCommands.Count))
                FilteredCommands.Add(item);
        }
        else
        {
            // Fuzzy match with scoring and boost for recent files
            var scored = _allFiles
                .Select(f =>
                {
                    var (nameScore, indices) = FuzzyMatchWithIndices(f.Name, filter);
                    var pathScore = FuzzyMatch(f.Category, filter) / 2;
                    var recentBoost = _recentFiles.Contains(f.FilePath ?? "", StringComparer.OrdinalIgnoreCase) ? 25 : 0;
                    return new { File = f, Score = nameScore + pathScore + recentBoost, Indices = indices };
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.File.Name.Length)
                .Take(50);

            foreach (var item in scored)
            {
                FilteredCommands.Add(new CommandPaletteItem
                {
                    Name = item.File.Name,
                    Category = item.File.Category,
                    FilePath = item.File.FilePath,
                    FileIcon = item.File.FileIcon,
                    MatchedIndices = item.Indices
                });
            }
        }

        SelectFirst();
    }

    private void UpdateFilteredCommands()
    {
        FilteredCommands.Clear();
        SectionHeader = "";

        var filter = SearchText?.Trim() ?? "";

        if (string.IsNullOrEmpty(filter))
        {
            // Show MRU commands first
            var mruItems = new List<CommandPaletteItem>();
            foreach (var mruId in _mruCommandIds)
            {
                var cmd = _allCommands.FirstOrDefault(c => c.CommandId == mruId);
                if (cmd != null)
                {
                    mruItems.Add(new CommandPaletteItem
                    {
                        Name = cmd.Name,
                        Category = cmd.Category,
                        Shortcut = cmd.Shortcut,
                        Execute = cmd.Execute,
                        IsRecentlyUsed = true
                    });
                }
            }

            if (mruItems.Count > 0)
            {
                SectionHeader = "recently used";
                foreach (var item in mruItems.Take(MaxMruItems))
                    FilteredCommands.Add(item);
            }

            // Add remaining commands
            var mruIds = new HashSet<string>(_mruCommandIds);
            foreach (var item in _allCommands.Where(c => !mruIds.Contains(c.CommandId)).Take(50 - FilteredCommands.Count))
                FilteredCommands.Add(item);
        }
        else
        {
            var scored = _allCommands
                .Select(cmd =>
                {
                    var (score, indices) = FuzzyMatchWithIndices(cmd.DisplayName, filter);
                    var mruIndex = _mruCommandIds.IndexOf(cmd.CommandId);
                    var mruBoost = mruIndex >= 0 ? Math.Max(0, 20 - mruIndex) : 0;
                    return new { Command = cmd, Score = score + mruBoost, Indices = indices };
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Command.DisplayName.Length)
                .Take(50);

            foreach (var item in scored)
            {
                FilteredCommands.Add(new CommandPaletteItem
                {
                    Name = item.Command.Name,
                    Category = item.Command.Category,
                    Shortcut = item.Command.Shortcut,
                    Execute = item.Command.Execute,
                    MatchedIndices = item.Indices
                });
            }
        }

        SelectFirst();
    }

    private void UpdateGoToLine()
    {
        FilteredCommands.Clear();
        SectionHeader = "";

        var text = SearchText?.Trim() ?? "";
        if (int.TryParse(text, out var lineNumber) && lineNumber > 0)
        {
            FilteredCommands.Add(new CommandPaletteItem
            {
                Name = $"Go to line {lineNumber}",
                Category = "Navigate",
                Execute = () => GoToLineRequested?.Invoke(this, lineNumber)
            });
            SelectedIndex = 0;
            SelectedItem = FilteredCommands[0];
        }
        else if (string.IsNullOrEmpty(text))
        {
            FilteredCommands.Add(new CommandPaletteItem
            {
                Name = "Type a line number and press Enter",
                Category = "Navigate"
            });
            SelectedIndex = 0;
            SelectedItem = FilteredCommands[0];
        }
        else
        {
            SelectedItem = null;
        }
    }

    private void UpdateSymbolSearch()
    {
        FilteredCommands.Clear();
        SectionHeader = "";

        if (_allSymbols.Count == 0)
            LoadCurrentFileSymbols();

        var filter = SearchText?.Trim() ?? "";

        if (_allSymbols.Count == 0)
        {
            // Fall back to the LSP-based symbol search command
            FilteredCommands.Add(new CommandPaletteItem
            {
                Name = "Go to Symbol... (opens symbol search)",
                Category = "Navigate",
                Execute = () => _mainVm?.GoToSymbolCommand.Execute(null)
            });
            SelectFirst();
            return;
        }

        IEnumerable<CommandPaletteItem> items;
        if (string.IsNullOrEmpty(filter))
        {
            items = _allSymbols;
        }
        else
        {
            items = _allSymbols
                .Select(s =>
                {
                    var (score, indices) = FuzzyMatchWithIndices(s.Name, filter);
                    return new { Symbol = s, Score = score, Indices = indices };
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Symbol.Name.Length)
                .Select(x =>
                {
                    x.Symbol.MatchedIndices = x.Indices;
                    return x.Symbol;
                });
        }

        foreach (var item in items.Take(50))
            FilteredCommands.Add(item);

        SelectFirst();
    }

    private void UpdateWorkspaceSymbolSearch()
    {
        FilteredCommands.Clear();
        SectionHeader = "";

        var filter = SearchText?.Trim() ?? "";
        if (string.IsNullOrEmpty(filter))
        {
            FilteredCommands.Add(new CommandPaletteItem
            {
                Name = "Type a symbol name to search across all files",
                Category = "Navigate"
            });
            SelectFirst();
            return;
        }

        var workspaceSymbols = LoadWorkspaceSymbols();

        var scored = workspaceSymbols
            .Select(s =>
            {
                var (score, indices) = FuzzyMatchWithIndices(s.Name, filter);
                return new { Symbol = s, Score = score, Indices = indices };
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Symbol.Name.Length)
            .Take(50);

        foreach (var item in scored)
        {
            item.Symbol.MatchedIndices = item.Indices;
            FilteredCommands.Add(item.Symbol);
        }

        if (FilteredCommands.Count == 0)
        {
            FilteredCommands.Add(new CommandPaletteItem
            {
                Name = $"No symbols matching '{filter}'",
                Category = "Navigate"
            });
        }

        SelectFirst();
    }

    private void UpdateHelp()
    {
        FilteredCommands.Clear();
        SectionHeader = "";

        FilteredCommands.Add(new CommandPaletteItem
        {
            Name = "... Type a file name to search",
            Category = "(no prefix)",
            Shortcut = "Ctrl+P"
        });
        FilteredCommands.Add(new CommandPaletteItem
        {
            Name = "> Type a command to run",
            Category = "> prefix",
            Shortcut = "Ctrl+Shift+P"
        });
        FilteredCommands.Add(new CommandPaletteItem
        {
            Name = ": Type a line number to go to",
            Category = ": prefix",
            Shortcut = "Ctrl+G"
        });
        FilteredCommands.Add(new CommandPaletteItem
        {
            Name = "@ Type a symbol name in current file",
            Category = "@ prefix",
            Shortcut = "Ctrl+T"
        });
        FilteredCommands.Add(new CommandPaletteItem
        {
            Name = "# Type a symbol name across all files",
            Category = "# prefix"
        });
        FilteredCommands.Add(new CommandPaletteItem
        {
            Name = "? Show this help",
            Category = "? prefix"
        });

        SelectFirst();
    }

    private void SelectFirst()
    {
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

    private void LoadCurrentFileSymbols()
    {
        _allSymbols.Clear();

        var docPath = ActiveDocumentPath;
        if (string.IsNullOrEmpty(docPath) || !File.Exists(docPath)) return;

        try
        {
            var content = File.ReadAllText(docPath);
            var fileName = Path.GetFileName(docPath);
            _allSymbols.AddRange(ParseSymbols(content, docPath, fileName));
        }
        catch { /* Ignore file read errors */ }
    }

    private List<CommandPaletteItem> LoadWorkspaceSymbols()
    {
        var symbols = new List<CommandPaletteItem>();
        if (_projectService?.CurrentProject == null) return symbols;

        var projectDir = _projectService.CurrentProject.ProjectDirectory;

        foreach (var item in _projectService.CurrentProject.Items)
        {
            try
            {
                var fullPath = Path.IsPathRooted(item.Include)
                    ? item.Include
                    : Path.Combine(projectDir, item.Include);
                if (!File.Exists(fullPath)) continue;

                var content = File.ReadAllText(fullPath);
                var fileName = Path.GetFileName(fullPath);
                symbols.AddRange(ParseSymbols(content, fullPath, fileName));
            }
            catch { /* Ignore file read errors */ }
        }

        return symbols;
    }

    private static List<CommandPaletteItem> ParseSymbols(string content, string fullPath, string fileName)
    {
        var symbols = new List<CommandPaletteItem>();
        var lines = content.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            var lineNumber = i + 1;

            if (line.StartsWith("Module ", StringComparison.OrdinalIgnoreCase))
            {
                var name = ExtractName(line, "Module ");
                if (!string.IsNullOrEmpty(name))
                    symbols.Add(CreateSymbolItem(name, "Module", fileName, fullPath, lineNumber));
            }
            else if (line.StartsWith("Class ", StringComparison.OrdinalIgnoreCase))
            {
                var name = ExtractName(line, "Class ");
                if (!string.IsNullOrEmpty(name))
                    symbols.Add(CreateSymbolItem(name, "Class", fileName, fullPath, lineNumber));
            }
            else if (line.StartsWith("Interface ", StringComparison.OrdinalIgnoreCase))
            {
                var name = ExtractName(line, "Interface ");
                if (!string.IsNullOrEmpty(name))
                    symbols.Add(CreateSymbolItem(name, "Interface", fileName, fullPath, lineNumber));
            }
            else if (line.StartsWith("Enum ", StringComparison.OrdinalIgnoreCase))
            {
                var name = ExtractName(line, "Enum ");
                if (!string.IsNullOrEmpty(name))
                    symbols.Add(CreateSymbolItem(name, "Enum", fileName, fullPath, lineNumber));
            }
            else if (line.StartsWith("Sub ", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains(" Sub ", StringComparison.OrdinalIgnoreCase))
            {
                var name = ExtractSubFunctionName(line, "Sub ");
                if (!string.IsNullOrEmpty(name))
                    symbols.Add(CreateSymbolItem(name, "Sub", fileName, fullPath, lineNumber));
            }
            else if (line.StartsWith("Function ", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains(" Function ", StringComparison.OrdinalIgnoreCase))
            {
                var name = ExtractSubFunctionName(line, "Function ");
                if (!string.IsNullOrEmpty(name))
                    symbols.Add(CreateSymbolItem(name, "Function", fileName, fullPath, lineNumber));
            }
            else if (line.StartsWith("Property ", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains(" Property ", StringComparison.OrdinalIgnoreCase))
            {
                var name = ExtractSubFunctionName(line, "Property ");
                if (!string.IsNullOrEmpty(name))
                    symbols.Add(CreateSymbolItem(name, "Property", fileName, fullPath, lineNumber));
            }
        }

        return symbols;
    }

    private static string ExtractName(string line, string keyword)
    {
        var idx = line.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return "";
        var rest = line.Substring(idx + keyword.Length).Trim();
        var endIdx = rest.IndexOfAny(new[] { ' ', '(', '\r', '\n' });
        return endIdx > 0 ? rest.Substring(0, endIdx) : rest;
    }

    private static string ExtractSubFunctionName(string line, string keyword)
    {
        var idx = line.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return "";
        var rest = line.Substring(idx + keyword.Length).Trim();
        var endIdx = rest.IndexOf('(');
        if (endIdx < 0) endIdx = rest.IndexOfAny(new[] { ' ', '\r', '\n' });
        return endIdx > 0 ? rest.Substring(0, endIdx).Trim() : rest.Trim();
    }

    private static CommandPaletteItem CreateSymbolItem(string name, string kind, string fileName, string fullPath, int lineNumber)
    {
        return new CommandPaletteItem
        {
            Name = name,
            Category = $"{kind} - {fileName}:{lineNumber}",
            FilePath = fullPath,
            Line = lineNumber
        };
    }

    private static string GetFileIcon(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".bas" => "BL",
            ".bl" => "BL",
            ".blproj" => "BP",
            ".cs" => "C#",
            ".vb" => "VB",
            ".cpp" or ".cxx" or ".cc" => "C+",
            ".h" or ".hpp" => "H",
            ".json" => "{}",
            ".xml" => "<>",
            ".txt" => "Tx",
            ".md" => "Md",
            _ => ".."
        };
    }

    /// <summary>
    /// Simple fuzzy match returning only a score.
    /// </summary>
    private static int FuzzyMatch(string text, string pattern)
    {
        var (score, _) = FuzzyMatchWithIndices(text, pattern);
        return score;
    }

    /// <summary>
    /// Fuzzy match returning a score and the matched character indices for highlighting.
    /// Bonuses for: exact substring, starts-with, consecutive matches, separator matches, camelCase.
    /// </summary>
    private static (int Score, List<int> MatchedIndices) FuzzyMatchWithIndices(string text, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return (1, new List<int>());
        if (string.IsNullOrEmpty(text)) return (0, new List<int>());

        // Exact substring match (case insensitive)
        var substringIdx = text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (substringIdx >= 0)
        {
            var indices = Enumerable.Range(substringIdx, pattern.Length).ToList();
            var score = 100 + (substringIdx == 0 ? 50 : 0);
            return (score, indices);
        }

        // Fuzzy match - characters must appear in order
        var patternIdx = 0;
        var totalScore = 0;
        var consecutive = 0;
        var matchedIndices = new List<int>();

        for (var i = 0; i < text.Length && patternIdx < pattern.Length; i++)
        {
            if (char.ToLowerInvariant(text[i]) == char.ToLowerInvariant(pattern[patternIdx]))
            {
                matchedIndices.Add(i);
                totalScore += 10 + consecutive * 5;
                consecutive++;
                patternIdx++;

                // Bonus for matching after separator
                if (i > 0 && (text[i - 1] == ' ' || text[i - 1] == ':' || text[i - 1] == '/' || text[i - 1] == '\\' || text[i - 1] == '.'))
                    totalScore += 15;

                // Bonus for camelCase boundary
                if (i > 0 && char.IsLower(text[i - 1]) && char.IsUpper(text[i]))
                    totalScore += 10;
            }
            else
            {
                consecutive = 0;
            }
        }

        return patternIdx == pattern.Length ? (totalScore, matchedIndices) : (0, new List<int>());
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

    /// <summary>
    /// Navigate to previous command in history (when in Command mode with empty search).
    /// </summary>
    [RelayCommand]
    private void HistoryUp()
    {
        if (CurrentMode != CommandPaletteMode.Command) return;
        if (_commandHistory.Count == 0) return;

        _historyIndex = Math.Min(_historyIndex + 1, _commandHistory.Count - 1);
        var historyItem = _commandHistory[_historyIndex];

        for (int i = 0; i < FilteredCommands.Count; i++)
        {
            if (FilteredCommands[i].CommandId == historyItem)
            {
                SelectedIndex = i;
                SelectedItem = FilteredCommands[i];
                break;
            }
        }
    }

    /// <summary>
    /// Navigate to next (more recent) command in history.
    /// </summary>
    [RelayCommand]
    private void HistoryDown()
    {
        if (CurrentMode != CommandPaletteMode.Command) return;
        if (_commandHistory.Count == 0 || _historyIndex < 0) return;

        _historyIndex = Math.Max(_historyIndex - 1, -1);
        if (_historyIndex >= 0)
        {
            var historyItem = _commandHistory[_historyIndex];
            for (int i = 0; i < FilteredCommands.Count; i++)
            {
                if (FilteredCommands[i].CommandId == historyItem)
                {
                    SelectedIndex = i;
                    SelectedItem = FilteredCommands[i];
                    break;
                }
            }
        }
    }

    [RelayCommand]
    private void Confirm()
    {
        if (CurrentMode == CommandPaletteMode.File && SelectedItem?.FilePath != null)
        {
            RecordFileOpen(SelectedItem.FilePath);
            FileOpenRequested?.Invoke(this, SelectedItem.FilePath);
            Dismissed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (CurrentMode == CommandPaletteMode.GoToLine)
        {
            var text = SearchText?.Trim() ?? "";
            if (int.TryParse(text, out var lineNumber) && lineNumber > 0)
            {
                GoToLineRequested?.Invoke(this, lineNumber);
                Dismissed?.Invoke(this, EventArgs.Empty);
                return;
            }
        }

        if ((CurrentMode == CommandPaletteMode.Symbol || CurrentMode == CommandPaletteMode.WorkspaceSymbol)
            && SelectedItem?.FilePath != null && SelectedItem.Line > 0)
        {
            SymbolNavigationRequested?.Invoke(this, (SelectedItem.FilePath, SelectedItem.Line));
            Dismissed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (CurrentMode == CommandPaletteMode.Help && SelectedItem != null)
        {
            // Selecting a help item switches to that mode
            var prefix = SelectedItem.Name.Length > 0 ? SelectedItem.Name[0] : ' ';
            switch (prefix)
            {
                case '>':
                    CurrentMode = CommandPaletteMode.Command;
                    break;
                case ':':
                    CurrentMode = CommandPaletteMode.GoToLine;
                    break;
                case '@':
                    CurrentMode = CommandPaletteMode.Symbol;
                    break;
                case '#':
                    CurrentMode = CommandPaletteMode.WorkspaceSymbol;
                    break;
                case '?':
                    return; // Already in help mode
                default:
                    CurrentMode = CommandPaletteMode.File;
                    break;
            }
            SearchText = "";
            ApplyModeSettings();
            UpdateFilteredItems();
            return;
        }

        if (SelectedItem != null)
        {
            RecordCommandUsage(SelectedItem);
            CommandExecuted?.Invoke(this, SelectedItem);
        }
    }

    [RelayCommand]
    private void Dismiss()
    {
        Dismissed?.Invoke(this, EventArgs.Empty);
    }
}
