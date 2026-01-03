namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Service for managing keyboard shortcuts.
/// </summary>
public interface IKeybindingService
{
    /// <summary>
    /// Gets all keybindings.
    /// </summary>
    IReadOnlyList<Keybinding> GetKeybindings();

    /// <summary>
    /// Gets keybindings for a command.
    /// </summary>
    IReadOnlyList<Keybinding> GetKeybindingsForCommand(string commandId);

    /// <summary>
    /// Gets the command bound to a key chord.
    /// </summary>
    string? GetCommandForKeyChord(KeyChord chord, string? context = null);

    /// <summary>
    /// Registers a keybinding.
    /// </summary>
    void RegisterKeybinding(Keybinding keybinding);

    /// <summary>
    /// Registers multiple keybindings.
    /// </summary>
    void RegisterKeybindings(IEnumerable<Keybinding> keybindings);

    /// <summary>
    /// Removes a keybinding.
    /// </summary>
    void RemoveKeybinding(string commandId, KeyChord chord);

    /// <summary>
    /// Sets a user keybinding (overrides default).
    /// </summary>
    void SetUserKeybinding(string commandId, KeyChord chord, string? when = null);

    /// <summary>
    /// Removes a user keybinding.
    /// </summary>
    void RemoveUserKeybinding(string commandId, KeyChord chord);

    /// <summary>
    /// Resets a command to default keybindings.
    /// </summary>
    void ResetToDefault(string commandId);

    /// <summary>
    /// Resets all keybindings to defaults.
    /// </summary>
    void ResetAllToDefaults();

    /// <summary>
    /// Handles a key event and executes the bound command.
    /// </summary>
    /// <param name="chord">The key chord pressed.</param>
    /// <param name="context">The current context (e.g., "editorTextFocus").</param>
    /// <returns>True if a command was executed.</returns>
    bool HandleKeyEvent(KeyChord chord, string? context = null);

    /// <summary>
    /// Checks if a key chord conflicts with existing bindings.
    /// </summary>
    IReadOnlyList<KeybindingConflict> CheckConflicts(KeyChord chord, string? when = null);

    /// <summary>
    /// Loads keybindings from disk.
    /// </summary>
    Task LoadAsync();

    /// <summary>
    /// Saves user keybindings to disk.
    /// </summary>
    Task SaveAsync();

    /// <summary>
    /// Gets available contexts for keybindings.
    /// </summary>
    IReadOnlyList<string> GetContexts();

    /// <summary>
    /// Evaluates if a context expression is true.
    /// </summary>
    bool EvaluateContext(string contextExpression);

    /// <summary>
    /// Sets a context value.
    /// </summary>
    void SetContext(string key, object value);

    /// <summary>
    /// Removes a context value.
    /// </summary>
    void RemoveContext(string key);

    /// <summary>
    /// Raised when a keybinding changes.
    /// </summary>
    event EventHandler<KeybindingChangedEventArgs>? KeybindingChanged;

    /// <summary>
    /// Raised when a command is executed via keybinding.
    /// </summary>
    event EventHandler<KeybindingExecutedEventArgs>? KeybindingExecuted;
}

#region Keybinding Types

/// <summary>
/// Represents a keyboard shortcut.
/// </summary>
public class Keybinding
{
    /// <summary>
    /// Gets or sets the command ID.
    /// </summary>
    public string CommandId { get; set; } = "";

    /// <summary>
    /// Gets or sets the primary key chord.
    /// </summary>
    public KeyChord Key { get; set; } = new();

    /// <summary>
    /// Gets or sets the secondary key chord (for two-part shortcuts like Ctrl+K Ctrl+C).
    /// </summary>
    public KeyChord? SecondKey { get; set; }

    /// <summary>
    /// Gets or sets the when clause (context expression).
    /// </summary>
    public string? When { get; set; }

    /// <summary>
    /// Gets or sets command arguments.
    /// </summary>
    public object? Args { get; set; }

    /// <summary>
    /// Gets or sets whether this is a user-defined keybinding.
    /// </summary>
    public bool IsUserDefined { get; set; }

    /// <summary>
    /// Gets or sets whether this keybinding is from an extension.
    /// </summary>
    public string? ExtensionId { get; set; }

    /// <summary>
    /// Gets or sets the source (default, extension, user).
    /// </summary>
    public KeybindingSource Source { get; set; } = KeybindingSource.Default;

    /// <summary>
    /// Gets the display string for this keybinding.
    /// </summary>
    public string GetDisplayString()
    {
        var primary = Key.GetDisplayString();
        if (SecondKey != null)
        {
            return $"{primary} {SecondKey.GetDisplayString()}";
        }
        return primary;
    }
}

/// <summary>
/// Source of a keybinding.
/// </summary>
public enum KeybindingSource
{
    Default,
    Extension,
    User
}

/// <summary>
/// Represents a key combination.
/// </summary>
public class KeyChord
{
    /// <summary>
    /// Gets or sets the key code.
    /// </summary>
    public KeyCode Key { get; set; }

    /// <summary>
    /// Gets or sets whether Ctrl is pressed.
    /// </summary>
    public bool Ctrl { get; set; }

    /// <summary>
    /// Gets or sets whether Shift is pressed.
    /// </summary>
    public bool Shift { get; set; }

    /// <summary>
    /// Gets or sets whether Alt is pressed.
    /// </summary>
    public bool Alt { get; set; }

    /// <summary>
    /// Gets or sets whether Meta (Win/Cmd) is pressed.
    /// </summary>
    public bool Meta { get; set; }

    /// <summary>
    /// Creates a key chord from a string (e.g., "Ctrl+Shift+P").
    /// </summary>
    public static KeyChord Parse(string keyString)
    {
        var chord = new KeyChord();
        var parts = keyString.Split('+');

        foreach (var part in parts)
        {
            var normalized = part.Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "ctrl":
                case "control":
                    chord.Ctrl = true;
                    break;
                case "shift":
                    chord.Shift = true;
                    break;
                case "alt":
                    chord.Alt = true;
                    break;
                case "meta":
                case "win":
                case "cmd":
                case "super":
                    chord.Meta = true;
                    break;
                default:
                    if (System.Enum.TryParse<KeyCode>(part.Trim(), true, out var keyCode))
                    {
                        chord.Key = keyCode;
                    }
                    break;
            }
        }

        return chord;
    }

    /// <summary>
    /// Gets the display string for this chord.
    /// </summary>
    public string GetDisplayString()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Shift) parts.Add("Shift");
        if (Alt) parts.Add("Alt");
        if (Meta) parts.Add("Win");
        parts.Add(Key.ToString());
        return string.Join("+", parts);
    }

    public override bool Equals(object? obj)
    {
        if (obj is not KeyChord other) return false;
        return Key == other.Key && Ctrl == other.Ctrl && Shift == other.Shift && Alt == other.Alt && Meta == other.Meta;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Key, Ctrl, Shift, Alt, Meta);
    }
}

/// <summary>
/// Key codes.
/// </summary>
public enum KeyCode
{
    None = 0,

    // Letters
    A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,

    // Numbers
    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,

    // Function keys
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,

    // Navigation
    Up, Down, Left, Right, Home, End, PageUp, PageDown,

    // Editing
    Backspace, Delete, Insert, Enter, Tab, Escape, Space,

    // Punctuation
    Semicolon, Comma, Period, Slash, Backslash, Quote, Backtick,
    OpenBracket, CloseBracket, Minus, Equals,

    // Numpad
    Numpad0, Numpad1, Numpad2, Numpad3, Numpad4,
    Numpad5, Numpad6, Numpad7, Numpad8, Numpad9,
    NumpadAdd, NumpadSubtract, NumpadMultiply, NumpadDivide, NumpadDecimal,

    // Misc
    CapsLock, NumLock, ScrollLock, PrintScreen, Pause,
    ContextMenu
}

/// <summary>
/// Represents a keybinding conflict.
/// </summary>
public class KeybindingConflict
{
    /// <summary>
    /// Gets or sets the conflicting keybinding.
    /// </summary>
    public Keybinding Keybinding { get; set; } = new();

    /// <summary>
    /// Gets or sets the conflict type.
    /// </summary>
    public ConflictType Type { get; set; }
}

/// <summary>
/// Type of keybinding conflict.
/// </summary>
public enum ConflictType
{
    /// <summary>
    /// Exact same key chord and context.
    /// </summary>
    Exact,

    /// <summary>
    /// Same key chord with overlapping context.
    /// </summary>
    Overlapping,

    /// <summary>
    /// Part of a multi-chord sequence.
    /// </summary>
    PartialChord
}

/// <summary>
/// Event args for keybinding changes.
/// </summary>
public class KeybindingChangedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the command ID.
    /// </summary>
    public string CommandId { get; }

    /// <summary>
    /// Gets the change type.
    /// </summary>
    public KeybindingChangeType ChangeType { get; }

    /// <summary>
    /// Gets the old keybinding (if applicable).
    /// </summary>
    public Keybinding? OldKeybinding { get; }

    /// <summary>
    /// Gets the new keybinding (if applicable).
    /// </summary>
    public Keybinding? NewKeybinding { get; }

    public KeybindingChangedEventArgs(string commandId, KeybindingChangeType changeType, Keybinding? oldKeybinding = null, Keybinding? newKeybinding = null)
    {
        CommandId = commandId;
        ChangeType = changeType;
        OldKeybinding = oldKeybinding;
        NewKeybinding = newKeybinding;
    }
}

/// <summary>
/// Type of keybinding change.
/// </summary>
public enum KeybindingChangeType
{
    Added,
    Removed,
    Modified
}

/// <summary>
/// Event args for keybinding execution.
/// </summary>
public class KeybindingExecutedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the keybinding that was executed.
    /// </summary>
    public Keybinding Keybinding { get; }

    /// <summary>
    /// Gets or sets whether the execution was handled.
    /// </summary>
    public bool Handled { get; set; }

    public KeybindingExecutedEventArgs(Keybinding keybinding)
    {
        Keybinding = keybinding;
    }
}

#endregion

#region Common Keybinding Contexts

/// <summary>
/// Common context keys for keybindings.
/// </summary>
public static class KeybindingContexts
{
    public const string EditorFocus = "editorFocus";
    public const string EditorTextFocus = "editorTextFocus";
    public const string EditorHasSelection = "editorHasSelection";
    public const string EditorHasMultipleSelections = "editorHasMultipleSelections";
    public const string EditorReadonly = "editorReadonly";
    public const string TextInputFocus = "textInputFocus";
    public const string InputFocus = "inputFocus";
    public const string TerminalFocus = "terminalFocus";
    public const string PanelFocus = "panelFocus";
    public const string SidebarFocus = "sideBarFocus";
    public const string ExplorerFocus = "explorerViewletFocus";
    public const string SearchFocus = "searchViewletFocus";
    public const string DebugFocus = "debugFocus";
    public const string InDebugMode = "inDebugMode";
    public const string BreakpointsViewFocus = "breakpointsFocused";
    public const string FindWidgetVisible = "findWidgetVisible";
    public const string ReplaceActive = "replaceActive";
    public const string SuggestWidgetVisible = "suggestWidgetVisible";
    public const string SuggestWidgetMultipleSuggestions = "suggestWidgetMultipleSuggestions";
    public const string ParameterHintsVisible = "parameterHintsVisible";
    public const string IsWindows = "isWindows";
    public const string IsLinux = "isLinux";
    public const string IsMac = "isMac";
}

#endregion
