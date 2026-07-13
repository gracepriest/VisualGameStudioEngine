using System.Collections.Generic;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

/// <summary>
/// One keyboard-shortcut entry for the F1 "Keyboard Shortcuts" dialog and the Settings ▸ Keyboard
/// grid. Both surfaces are honest, read-only references (Decision D4): the shortcut text comes from
/// the app's real bindings, not a hand-maintained parallel list.
/// </summary>
public sealed class ShortcutInfo
{
    /// <summary>Grouping bucket shown/searched in the F1 dialog (File, Edit, Debug, …).</summary>
    public string Category { get; init; } = "";

    /// <summary>Human-readable command name (e.g. "Restart Debugging").</summary>
    public string DisplayName { get; init; } = "";

    /// <summary>
    /// The bound command's property name as it appears in the AXAML binding
    /// (e.g. "RestartDebuggingCommand"). For global bindings this is what the cross-validation test
    /// matches against <c>MainWindow.axaml</c>'s <c>Command="{Binding …}"</c>. Empty for entries with
    /// no single owning command.
    /// </summary>
    public string CommandName { get; init; } = "";

    /// <summary>
    /// The gesture in the SAME raw token form Avalonia uses in <c>MainWindow.axaml</c>
    /// (e.g. "Ctrl+Shift+OemMinus", "Ctrl+D1"). Empty when the command has no keyboard shortcut.
    /// The cross-validation test compares this verbatim; <see cref="DisplayGesture"/> is what the UI
    /// shows.
    /// </summary>
    public string Gesture { get; init; } = "";

    /// <summary>
    /// True when this entry is a real <c>Window.KeyBinding</c> in <c>MainWindow.axaml</c> — the set
    /// the drift test validates. False for menu/palette-only or editor-scoped commands (e.g. Undo,
    /// which the editor control owns rather than a window key binding), which are included for
    /// completeness but not cross-validated.
    /// </summary>
    public bool IsGlobalKeyBinding { get; init; }

    /// <summary>The gesture humanized for display, e.g. "Ctrl+Shift+-" or "Ctrl+1".</summary>
    public string DisplayGesture => KeyboardShortcutRegistry.HumanizeGesture(Gesture);
}

/// <summary>
/// Single source of truth for the IDE's keyboard-shortcut reference. The <see cref="Global"/> entries
/// mirror <c>MainWindow.axaml</c>'s <c>Window.KeyBindings</c> exactly — a unit test
/// (<c>KeyboardShortcutRegistryTests</c>) parses that AXAML at test time and fails if the two drift,
/// so this list can never silently rot the way the old hand-maintained lists did (they showed
/// Ctrl+Shift+F5 as "Run in External Console" when it is actually Restart Debugging, invented
/// gestures like New Project = Ctrl+Shift+N, and omitted ~15 real bindings).
/// </summary>
public static class KeyboardShortcutRegistry
{
    /// <summary>
    /// The real <c>Window.KeyBindings</c> from <c>MainWindow.axaml</c>. Gestures are stored in the
    /// raw Avalonia token form so the cross-validation test can compare them verbatim. Keep this in
    /// sync with the AXAML — the test enforces it.
    /// </summary>
    public static IReadOnlyList<ShortcutInfo> Global { get; } = new List<ShortcutInfo>
    {
        // ---- Debug ----
        G("Debug", "Start Debugging", "StartDebuggingCommand", "F5"),
        G("Debug", "Start Without Debugging", "StartWithoutDebuggingCommand", "Ctrl+F5"),
        G("Debug", "Restart Debugging", "RestartDebuggingCommand", "Ctrl+Shift+F5"),
        G("Debug", "Stop Debugging", "StopDebuggingCommand", "Shift+F5"),
        G("Debug", "Attach to Process...", "AttachToProcessCommand", "Ctrl+Alt+P"),
        G("Debug", "Toggle Breakpoint", "ToggleBreakpointCommand", "F9"),
        G("Debug", "New Function Breakpoint...", "NewFunctionBreakpointCommand", "Ctrl+Shift+F9"),
        G("Debug", "Run to Cursor", "RunToCursorCommand", "Ctrl+F10"),
        G("Debug", "Set Next Statement", "SetNextStatementCommand", "Ctrl+Shift+F10"),
        G("Debug", "Step Over", "StepOverCommand", "F10"),
        G("Debug", "Step Into", "StepIntoCommand", "F11"),
        G("Debug", "Step Out", "StepOutCommand", "Shift+F11"),
        G("Debug", "Show Breakpoints", "ShowBreakpointsCommand", "Ctrl+Alt+B"),
        G("Debug", "Show Call Stack", "ShowCallStackCommand", "Ctrl+Alt+C"),
        G("Debug", "Show Variables", "ShowVariablesCommand", "Ctrl+Alt+V"),
        G("Debug", "Show Watch", "ShowWatchCommand", "Ctrl+Alt+W"),
        G("Debug", "Show Immediate Window", "ShowImmediateWindowCommand", "Ctrl+Alt+I"),
        G("Debug", "Exception Settings...", "ShowExceptionSettingsCommand", "Ctrl+Alt+X"),

        // ---- Build ----
        G("Build", "Build Project", "BuildCommand", "Ctrl+Shift+B"),

        // ---- File ----
        G("File", "Save", "SaveCommand", "Ctrl+S"),
        G("File", "Save All", "SaveAllCommand", "Ctrl+Shift+S"),

        // ---- Edit / Navigation ----
        G("Edit", "Go to Line...", "GoToLineCommand", "Ctrl+G"),
        G("Edit", "Go to Symbol...", "GoToSymbolCommand", "Ctrl+T"),
        G("Edit", "Find...", "FindCommand", "Ctrl+F"),
        G("Edit", "Replace...", "ReplaceCommand", "Ctrl+H"),
        G("Edit", "Find in Files...", "ShowFindInFilesCommand", "Ctrl+Shift+F"),
        G("Edit", "Go to Definition", "GoToDefinitionCommand", "F12"),
        G("Edit", "Go to Implementation", "GoToImplementationCommand", "Ctrl+F12"),
        G("Edit", "Find All References", "FindReferencesCommand", "Shift+F12"),
        G("Edit", "Go to Next Error", "GoToNextErrorCommand", "F8"),
        G("Edit", "Go to Previous Error", "GoToPreviousErrorCommand", "Shift+F8"),
        G("Edit", "Toggle Column Selection Mode", "ToggleColumnSelectionModeCommand", "Alt+Shift+Insert"),

        // ---- Edit / Refactoring ----
        G("Edit", "Rename Symbol...", "RenameSymbolCommand", "Ctrl+R"),
        G("Edit", "Extract Method...", "ExtractMethodCommand", "Ctrl+Shift+M"),
        G("Edit", "Inline Method...", "InlineMethodCommand", "Ctrl+Shift+I"),
        G("Edit", "Introduce Variable...", "IntroduceVariableCommand", "Ctrl+Shift+V"),
        G("Edit", "Change Signature...", "ChangeSignatureCommand", "Ctrl+Shift+OemMinus"),
        G("Edit", "Encapsulate Field...", "EncapsulateFieldCommand", "Ctrl+Shift+E"),
        G("Edit", "Move Type to File...", "MoveTypeToFileCommand", "Ctrl+Shift+T"),
        G("Edit", "Extract Interface...", "ExtractInterfaceCommand", "Ctrl+Shift+X"),
        G("Edit", "Generate Constructor...", "GenerateConstructorCommand", "Ctrl+Shift+G"),
        G("Edit", "Implement Interface...", "ImplementInterfaceCommand", "Ctrl+OemPeriod"),

        // ---- Bookmarks ----
        G("Bookmarks", "Toggle Bookmark", "ToggleBookmarkCommand", "Ctrl+K"),
        G("Bookmarks", "Next Bookmark", "NextBookmarkCommand", "F2"),
        G("Bookmarks", "Previous Bookmark", "PreviousBookmarkCommand", "Shift+F2"),

        // ---- View / Panels ----
        G("View", "Output", "ShowOutputCommand", "Ctrl+Alt+O"),
        G("View", "Error List", "ShowErrorListCommand", "Ctrl+Alt+E"),
        G("View", "Terminal", "ShowTerminalCommand", "Ctrl+OemTilde"),
        G("View", "New Terminal", "CreateNewTerminalCommand", "Ctrl+Shift+OemTilde"),
        G("View", "Command Palette...", "OpenCommandPaletteCommand", "Ctrl+Shift+P"),
        G("View", "Quick Open...", "OpenQuickOpenCommand", "Ctrl+P"),
        G("View", "Zen Mode", "ToggleZenModeCommand", "Ctrl+Shift+Z"),
        G("View", "Full Screen", "ToggleFullScreenCommand", "Shift+Alt+Enter"),
        G("View", "Toggle Render Whitespace", "ToggleWhitespaceCommand", "Ctrl+Shift+W"),
        G("View", "Focus Solution Explorer", "FocusSolutionExplorerCommand", "Ctrl+D1"),
        G("View", "Focus Editor", "FocusEditorCommand", "Ctrl+D2"),
        G("View", "Focus Output", "FocusOutputCommand", "Ctrl+D3"),
        G("View", "Focus Terminal", "FocusTerminalCommand", "Ctrl+D4"),
        G("View", "Focus Error List", "FocusErrorListCommand", "Ctrl+D5"),
        G("View", "Focus Variables", "FocusVariablesCommand", "Ctrl+D0"),
        G("View", "Focus Next Panel", "FocusNextPanelCommand", "F6"),
        G("View", "Focus Previous Panel", "FocusPreviousPanelCommand", "Shift+F6"),
        G("View", "Zoom In", "ZoomInCommand", "Ctrl+OemPlus"),
        G("View", "Zoom Out", "ZoomOutCommand", "Ctrl+OemMinus"),
        G("View", "Reset Zoom", "ZoomResetCommand", "Ctrl+Shift+D0"),

        // ---- Help ----
        G("Help", "Keyboard Shortcuts...", "ShowKeyboardShortcutsCommand", "F1"),
    };

    /// <summary>
    /// Commands surfaced in menus/palette that are NOT window key bindings. Editor-scoped edits
    /// (Undo/Cut/Copy/…) really do fire — the AvalonEdit control owns those keys — so their gesture
    /// is shown honestly; file/panel commands that have no working shortcut are listed with an empty
    /// gesture rather than a fabricated one. These are not cross-validated against the AXAML.
    /// </summary>
    public static IReadOnlyList<ShortcutInfo> PaletteOnly { get; } = new List<ShortcutInfo>
    {
        // ---- File (no window key binding; menu InputGesture is display-only in Avalonia) ----
        P("File", "New Project...", "NewProjectCommand", ""),
        P("File", "New Solution...", "NewSolutionCommand", ""),
        P("File", "Open Project...", "OpenProjectCommand", ""),
        P("File", "Open File...", "OpenFileCommand", ""),
        P("File", "Open Folder...", "OpenFolderCommand", ""),
        P("File", "Open Workspace...", "OpenWorkspaceCommand", ""),

        // ---- Edit (editor-scoped: handled by the code editor control) ----
        P("Edit", "Undo", "UndoCommand", "Ctrl+Z"),
        P("Edit", "Redo", "RedoCommand", "Ctrl+Y"),
        P("Edit", "Cut", "CutCommand", "Ctrl+X"),
        P("Edit", "Copy", "CopyCommand", "Ctrl+C"),
        P("Edit", "Paste", "PasteCommand", "Ctrl+V"),
        P("Edit", "Toggle Comment", "ToggleCommentCommand", "Ctrl+OemQuestion"),
        P("Edit", "Duplicate Line", "DuplicateLineCommand", "Ctrl+D"),

        // ---- Tools / Preferences ----
        P("Tools", "Settings...", "SettingsCommand", ""),
        P("Tools", "Color Theme...", "OpenColorThemeCommand", ""),
    };

    /// <summary>All entries — real key bindings first, then palette/menu-only commands.</summary>
    public static IReadOnlyList<ShortcutInfo> All { get; } = BuildAll();

    private static IReadOnlyList<ShortcutInfo> BuildAll()
    {
        var list = new List<ShortcutInfo>(Global.Count + PaletteOnly.Count);
        list.AddRange(Global);
        list.AddRange(PaletteOnly);
        return list;
    }

    private static ShortcutInfo G(string category, string name, string command, string gesture) =>
        new() { Category = category, DisplayName = name, CommandName = command, Gesture = gesture, IsGlobalKeyBinding = true };

    private static ShortcutInfo P(string category, string name, string command, string gesture) =>
        new() { Category = category, DisplayName = name, CommandName = command, Gesture = gesture, IsGlobalKeyBinding = false };

    /// <summary>
    /// Converts a raw Avalonia gesture ("Ctrl+Shift+OemMinus", "Ctrl+D1") into a friendly display
    /// string ("Ctrl+Shift+-", "Ctrl+1"). Idempotent for already-friendly tokens.
    /// </summary>
    public static string HumanizeGesture(string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture)) return "";

        var parts = gesture.Split('+');
        for (int i = 0; i < parts.Length; i++)
        {
            parts[i] = HumanizeToken(parts[i].Trim());
        }
        return string.Join("+", parts);
    }

    private static string HumanizeToken(string token) => token switch
    {
        "OemMinus" => "-",
        "OemPlus" => "+",
        "OemPeriod" => ".",
        "OemComma" => ",",
        "OemTilde" => "`",
        "OemQuestion" => "/",
        "OemPipe" => "\\",
        "OemBackslash" => "\\",
        "OemOpenBrackets" => "[",
        "OemCloseBrackets" => "]",
        "OemSemicolon" => ";",
        "OemQuotes" => "'",
        // Number-row digits are reported as D0–D9 by Avalonia; show the bare digit.
        "D0" => "0",
        "D1" => "1",
        "D2" => "2",
        "D3" => "3",
        "D4" => "4",
        "D5" => "5",
        "D6" => "6",
        "D7" => "7",
        "D8" => "8",
        "D9" => "9",
        _ => token
    };
}
