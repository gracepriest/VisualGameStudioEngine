using System.Text.Json;
using System.Text.Json.Serialization;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Service for managing keyboard shortcuts.
/// </summary>
public class KeybindingService : IKeybindingService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly List<Keybinding> _defaultKeybindings = new();
    private readonly List<Keybinding> _userKeybindings = new();
    private readonly Dictionary<string, object> _contexts = new();
    private readonly string _userKeybindingsPath;
    private readonly ICommandService? _commandService;

    public KeybindingService(ICommandService? commandService = null)
    {
        _commandService = commandService;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var settingsDir = Path.Combine(appData, "VisualGameStudio");
        Directory.CreateDirectory(settingsDir);
        _userKeybindingsPath = Path.Combine(settingsDir, "keybindings.json");

        // Set platform context
        SetContext(KeybindingContexts.IsWindows, OperatingSystem.IsWindows());
        SetContext(KeybindingContexts.IsLinux, OperatingSystem.IsLinux());
        SetContext(KeybindingContexts.IsMac, OperatingSystem.IsMacOS());

        RegisterDefaultKeybindings();
    }

    public event EventHandler<KeybindingChangedEventArgs>? KeybindingChanged;
    public event EventHandler<KeybindingExecutedEventArgs>? KeybindingExecuted;

    public IReadOnlyList<Keybinding> GetKeybindings()
    {
        var result = new List<Keybinding>();

        // Add default keybindings
        foreach (var kb in _defaultKeybindings)
        {
            // Check if overridden by user
            var userOverride = _userKeybindings.FirstOrDefault(u =>
                u.CommandId == kb.CommandId && u.Key.Equals(kb.Key));
            if (userOverride != null)
            {
                result.Add(userOverride);
            }
            else
            {
                result.Add(kb);
            }
        }

        // Add user keybindings that don't override defaults
        foreach (var kb in _userKeybindings)
        {
            if (!result.Any(r => r.CommandId == kb.CommandId && r.Key.Equals(kb.Key)))
            {
                result.Add(kb);
            }
        }

        return result;
    }

    public IReadOnlyList<Keybinding> GetKeybindingsForCommand(string commandId)
    {
        return GetKeybindings().Where(kb => kb.CommandId == commandId).ToList();
    }

    public string? GetCommandForKeyChord(KeyChord chord, string? context = null)
    {
        var keybinding = FindMatchingKeybinding(chord, context);
        return keybinding?.CommandId;
    }

    public void RegisterKeybinding(Keybinding keybinding)
    {
        keybinding.Source = KeybindingSource.Default;
        _defaultKeybindings.Add(keybinding);
        KeybindingChanged?.Invoke(this, new KeybindingChangedEventArgs(
            keybinding.CommandId, KeybindingChangeType.Added, null, keybinding));
    }

    public void RegisterKeybindings(IEnumerable<Keybinding> keybindings)
    {
        foreach (var kb in keybindings)
        {
            RegisterKeybinding(kb);
        }
    }

    public void RemoveKeybinding(string commandId, KeyChord chord)
    {
        var kb = _defaultKeybindings.FirstOrDefault(k => k.CommandId == commandId && k.Key.Equals(chord));
        if (kb != null)
        {
            _defaultKeybindings.Remove(kb);
            KeybindingChanged?.Invoke(this, new KeybindingChangedEventArgs(
                commandId, KeybindingChangeType.Removed, kb, null));
        }
    }

    public void SetUserKeybinding(string commandId, KeyChord chord, string? when = null)
    {
        var existing = _userKeybindings.FirstOrDefault(k => k.CommandId == commandId && k.Key.Equals(chord));
        var keybinding = new Keybinding
        {
            CommandId = commandId,
            Key = chord,
            When = when,
            IsUserDefined = true,
            Source = KeybindingSource.User
        };

        if (existing != null)
        {
            _userKeybindings.Remove(existing);
            _userKeybindings.Add(keybinding);
            KeybindingChanged?.Invoke(this, new KeybindingChangedEventArgs(
                commandId, KeybindingChangeType.Modified, existing, keybinding));
        }
        else
        {
            _userKeybindings.Add(keybinding);
            KeybindingChanged?.Invoke(this, new KeybindingChangedEventArgs(
                commandId, KeybindingChangeType.Added, null, keybinding));
        }
    }

    public void RemoveUserKeybinding(string commandId, KeyChord chord)
    {
        var kb = _userKeybindings.FirstOrDefault(k => k.CommandId == commandId && k.Key.Equals(chord));
        if (kb != null)
        {
            _userKeybindings.Remove(kb);
            KeybindingChanged?.Invoke(this, new KeybindingChangedEventArgs(
                commandId, KeybindingChangeType.Removed, kb, null));
        }
    }

    public void ResetToDefault(string commandId)
    {
        var toRemove = _userKeybindings.Where(k => k.CommandId == commandId).ToList();
        foreach (var kb in toRemove)
        {
            _userKeybindings.Remove(kb);
            KeybindingChanged?.Invoke(this, new KeybindingChangedEventArgs(
                commandId, KeybindingChangeType.Removed, kb, null));
        }
    }

    public void ResetAllToDefaults()
    {
        var commands = _userKeybindings.Select(k => k.CommandId).Distinct().ToList();
        _userKeybindings.Clear();
        foreach (var cmd in commands)
        {
            KeybindingChanged?.Invoke(this, new KeybindingChangedEventArgs(
                cmd, KeybindingChangeType.Modified, null, null));
        }
    }

    public bool HandleKeyEvent(KeyChord chord, string? context = null)
    {
        var keybinding = FindMatchingKeybinding(chord, context);
        if (keybinding == null) return false;

        var args = new KeybindingExecutedEventArgs(keybinding);
        KeybindingExecuted?.Invoke(this, args);

        if (!args.Handled && _commandService != null)
        {
            _ = _commandService.ExecuteAsync(keybinding.CommandId, keybinding.Args);
            args.Handled = true;
        }

        return args.Handled;
    }

    public IReadOnlyList<KeybindingConflict> CheckConflicts(KeyChord chord, string? when = null)
    {
        var conflicts = new List<KeybindingConflict>();

        foreach (var kb in GetKeybindings())
        {
            if (kb.Key.Equals(chord))
            {
                var conflictType = ConflictType.Overlapping;
                if (kb.When == when)
                {
                    conflictType = ConflictType.Exact;
                }

                conflicts.Add(new KeybindingConflict
                {
                    Keybinding = kb,
                    Type = conflictType
                });
            }
        }

        return conflicts;
    }

    public async Task LoadAsync()
    {
        if (!File.Exists(_userKeybindingsPath)) return;

        try
        {
            var json = await File.ReadAllTextAsync(_userKeybindingsPath);
            var keybindings = JsonSerializer.Deserialize<List<KeybindingDto>>(json, JsonOptions);
            if (keybindings != null)
            {
                _userKeybindings.Clear();
                foreach (var dto in keybindings)
                {
                    _userKeybindings.Add(dto.ToKeybinding());
                }
            }
        }
        catch
        {
            // Ignore load errors
        }
    }

    public async Task SaveAsync()
    {
        try
        {
            var dir = Path.GetDirectoryName(_userKeybindingsPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var dtos = _userKeybindings.Select(k => new KeybindingDto(k)).ToList();
            var json = JsonSerializer.Serialize(dtos, JsonOptions);
            await File.WriteAllTextAsync(_userKeybindingsPath, json);
        }
        catch
        {
            // Ignore save errors
        }
    }

    public IReadOnlyList<string> GetContexts()
    {
        return _contexts.Keys.ToList();
    }

    public bool EvaluateContext(string contextExpression)
    {
        if (string.IsNullOrEmpty(contextExpression)) return true;

        // Simple expression evaluation
        // Supports: key, !key, key1 && key2, key1 || key2
        var expression = contextExpression.Trim();

        // Handle negation
        if (expression.StartsWith("!"))
        {
            return !EvaluateContext(expression.Substring(1));
        }

        // Handle AND
        if (expression.Contains("&&"))
        {
            var parts = expression.Split(new[] { "&&" }, StringSplitOptions.RemoveEmptyEntries);
            return parts.All(p => EvaluateContext(p.Trim()));
        }

        // Handle OR
        if (expression.Contains("||"))
        {
            var parts = expression.Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Any(p => EvaluateContext(p.Trim()));
        }

        // Handle equality
        if (expression.Contains("=="))
        {
            var parts = expression.Split(new[] { "==" }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                var key = parts[0].Trim();
                var value = parts[1].Trim().Trim('\'', '"');
                return _contexts.TryGetValue(key, out var contextValue) &&
                       contextValue?.ToString() == value;
            }
        }

        // Simple context key check
        return _contexts.TryGetValue(expression, out var val) && val is true or "true";
    }

    public void SetContext(string key, object value)
    {
        _contexts[key] = value;
    }

    public void RemoveContext(string key)
    {
        _contexts.Remove(key);
    }

    #region Private Methods

    private Keybinding? FindMatchingKeybinding(KeyChord chord, string? context)
    {
        // User keybindings have priority
        foreach (var kb in _userKeybindings)
        {
            if (kb.Key.Equals(chord) && EvaluateWhenClause(kb.When, context))
            {
                return kb;
            }
        }

        // Then default keybindings
        foreach (var kb in _defaultKeybindings)
        {
            if (kb.Key.Equals(chord) && EvaluateWhenClause(kb.When, context))
            {
                return kb;
            }
        }

        return null;
    }

    private bool EvaluateWhenClause(string? when, string? currentContext)
    {
        if (string.IsNullOrEmpty(when)) return true;

        // If a context is provided, check if it matches
        if (!string.IsNullOrEmpty(currentContext))
        {
            SetContext(currentContext, true);
        }

        var result = EvaluateContext(when);

        if (!string.IsNullOrEmpty(currentContext))
        {
            RemoveContext(currentContext);
        }

        return result;
    }

    private void RegisterDefaultKeybindings()
    {
        // File operations
        RegisterKeybinding(new Keybinding { CommandId = "file.new", Key = KeyChord.Parse("Ctrl+N") });
        RegisterKeybinding(new Keybinding { CommandId = "file.open", Key = KeyChord.Parse("Ctrl+O") });
        RegisterKeybinding(new Keybinding { CommandId = "file.save", Key = KeyChord.Parse("Ctrl+S") });
        RegisterKeybinding(new Keybinding { CommandId = "file.saveAs", Key = KeyChord.Parse("Ctrl+Shift+S") });
        RegisterKeybinding(new Keybinding { CommandId = "file.saveAll", Key = KeyChord.Parse("Ctrl+K S"), SecondKey = null });
        RegisterKeybinding(new Keybinding { CommandId = "file.close", Key = KeyChord.Parse("Ctrl+W") });
        RegisterKeybinding(new Keybinding { CommandId = "file.closeAll", Key = KeyChord.Parse("Ctrl+K Ctrl+W"), SecondKey = null });

        // Edit operations
        RegisterKeybinding(new Keybinding { CommandId = "edit.undo", Key = KeyChord.Parse("Ctrl+Z") });
        RegisterKeybinding(new Keybinding { CommandId = "edit.redo", Key = KeyChord.Parse("Ctrl+Y") });
        RegisterKeybinding(new Keybinding { CommandId = "edit.cut", Key = KeyChord.Parse("Ctrl+X"), When = KeybindingContexts.EditorTextFocus });
        RegisterKeybinding(new Keybinding { CommandId = "edit.copy", Key = KeyChord.Parse("Ctrl+C"), When = KeybindingContexts.EditorTextFocus });
        RegisterKeybinding(new Keybinding { CommandId = "edit.paste", Key = KeyChord.Parse("Ctrl+V"), When = KeybindingContexts.EditorTextFocus });
        RegisterKeybinding(new Keybinding { CommandId = "edit.selectAll", Key = KeyChord.Parse("Ctrl+A") });
        RegisterKeybinding(new Keybinding { CommandId = "edit.find", Key = KeyChord.Parse("Ctrl+F") });
        RegisterKeybinding(new Keybinding { CommandId = "edit.replace", Key = KeyChord.Parse("Ctrl+H") });
        RegisterKeybinding(new Keybinding { CommandId = "edit.findInFiles", Key = KeyChord.Parse("Ctrl+Shift+F") });
        RegisterKeybinding(new Keybinding { CommandId = "edit.replaceInFiles", Key = KeyChord.Parse("Ctrl+Shift+H") });

        // View operations
        RegisterKeybinding(new Keybinding { CommandId = "view.commandPalette", Key = KeyChord.Parse("Ctrl+Shift+P") });
        RegisterKeybinding(new Keybinding { CommandId = "view.quickOpen", Key = KeyChord.Parse("Ctrl+P") });
        RegisterKeybinding(new Keybinding { CommandId = "view.explorer", Key = KeyChord.Parse("Ctrl+Shift+E") });
        RegisterKeybinding(new Keybinding { CommandId = "view.search", Key = KeyChord.Parse("Ctrl+Shift+F") });
        RegisterKeybinding(new Keybinding { CommandId = "view.git", Key = KeyChord.Parse("Ctrl+Shift+G") });
        RegisterKeybinding(new Keybinding { CommandId = "view.debug", Key = KeyChord.Parse("Ctrl+Shift+D") });
        RegisterKeybinding(new Keybinding { CommandId = "view.extensions", Key = KeyChord.Parse("Ctrl+Shift+X") });
        RegisterKeybinding(new Keybinding { CommandId = "view.terminal", Key = KeyChord.Parse("Ctrl+`") });
        RegisterKeybinding(new Keybinding { CommandId = "view.output", Key = KeyChord.Parse("Ctrl+Shift+U") });
        RegisterKeybinding(new Keybinding { CommandId = "view.problems", Key = KeyChord.Parse("Ctrl+Shift+M") });
        RegisterKeybinding(new Keybinding { CommandId = "view.zoomIn", Key = KeyChord.Parse("Ctrl+=") });
        RegisterKeybinding(new Keybinding { CommandId = "view.zoomOut", Key = KeyChord.Parse("Ctrl+-") });
        RegisterKeybinding(new Keybinding { CommandId = "view.zoomReset", Key = KeyChord.Parse("Ctrl+0") });

        // Navigation
        RegisterKeybinding(new Keybinding { CommandId = "navigation.goToDefinition", Key = KeyChord.Parse("F12"), When = KeybindingContexts.EditorTextFocus });
        RegisterKeybinding(new Keybinding { CommandId = "navigation.peekDefinition", Key = KeyChord.Parse("Alt+F12"), When = KeybindingContexts.EditorTextFocus });
        RegisterKeybinding(new Keybinding { CommandId = "navigation.goToReferences", Key = KeyChord.Parse("Shift+F12"), When = KeybindingContexts.EditorTextFocus });
        RegisterKeybinding(new Keybinding { CommandId = "navigation.goToLine", Key = KeyChord.Parse("Ctrl+G") });
        RegisterKeybinding(new Keybinding { CommandId = "navigation.goToSymbol", Key = KeyChord.Parse("Ctrl+Shift+O") });
        RegisterKeybinding(new Keybinding { CommandId = "navigation.goBack", Key = KeyChord.Parse("Alt+Left") });
        RegisterKeybinding(new Keybinding { CommandId = "navigation.goForward", Key = KeyChord.Parse("Alt+Right") });

        // Editor operations
        RegisterKeybinding(new Keybinding { CommandId = "editor.format", Key = KeyChord.Parse("Shift+Alt+F"), When = KeybindingContexts.EditorTextFocus });
        RegisterKeybinding(new Keybinding { CommandId = "editor.formatSelection", Key = KeyChord.Parse("Ctrl+K Ctrl+F"), When = KeybindingContexts.EditorHasSelection });
        RegisterKeybinding(new Keybinding { CommandId = "editor.commentLine", Key = KeyChord.Parse("Ctrl+/"), When = KeybindingContexts.EditorTextFocus });
        RegisterKeybinding(new Keybinding { CommandId = "editor.blockComment", Key = KeyChord.Parse("Shift+Alt+A"), When = KeybindingContexts.EditorTextFocus });
        RegisterKeybinding(new Keybinding { CommandId = "editor.indentLine", Key = KeyChord.Parse("Ctrl+]"), When = KeybindingContexts.EditorTextFocus });
        RegisterKeybinding(new Keybinding { CommandId = "editor.outdentLine", Key = KeyChord.Parse("Ctrl+["), When = KeybindingContexts.EditorTextFocus });
        RegisterKeybinding(new Keybinding { CommandId = "editor.moveLinesUp", Key = KeyChord.Parse("Alt+Up"), When = KeybindingContexts.EditorTextFocus });
        RegisterKeybinding(new Keybinding { CommandId = "editor.moveLinesDown", Key = KeyChord.Parse("Alt+Down"), When = KeybindingContexts.EditorTextFocus });
        RegisterKeybinding(new Keybinding { CommandId = "editor.copyLinesUp", Key = KeyChord.Parse("Shift+Alt+Up"), When = KeybindingContexts.EditorTextFocus });
        RegisterKeybinding(new Keybinding { CommandId = "editor.copyLinesDown", Key = KeyChord.Parse("Shift+Alt+Down"), When = KeybindingContexts.EditorTextFocus });
        RegisterKeybinding(new Keybinding { CommandId = "editor.deleteLine", Key = KeyChord.Parse("Ctrl+Shift+K"), When = KeybindingContexts.EditorTextFocus });
        RegisterKeybinding(new Keybinding { CommandId = "editor.insertLineAbove", Key = KeyChord.Parse("Ctrl+Shift+Enter"), When = KeybindingContexts.EditorTextFocus });
        RegisterKeybinding(new Keybinding { CommandId = "editor.insertLineBelow", Key = KeyChord.Parse("Ctrl+Enter"), When = KeybindingContexts.EditorTextFocus });

        // Multi-cursor
        RegisterKeybinding(new Keybinding { CommandId = "editor.addCursorAbove", Key = KeyChord.Parse("Ctrl+Alt+Up"), When = KeybindingContexts.EditorTextFocus });
        RegisterKeybinding(new Keybinding { CommandId = "editor.addCursorBelow", Key = KeyChord.Parse("Ctrl+Alt+Down"), When = KeybindingContexts.EditorTextFocus });
        RegisterKeybinding(new Keybinding { CommandId = "editor.addCursorAtEndOfLine", Key = KeyChord.Parse("Shift+Alt+I"), When = KeybindingContexts.EditorHasSelection });
        RegisterKeybinding(new Keybinding { CommandId = "editor.selectNextOccurrence", Key = KeyChord.Parse("Ctrl+D"), When = KeybindingContexts.EditorTextFocus });
        RegisterKeybinding(new Keybinding { CommandId = "editor.selectAllOccurrences", Key = KeyChord.Parse("Ctrl+Shift+L"), When = KeybindingContexts.EditorTextFocus });

        // Code actions
        RegisterKeybinding(new Keybinding { CommandId = "editor.quickFix", Key = KeyChord.Parse("Ctrl+."), When = KeybindingContexts.EditorTextFocus });
        RegisterKeybinding(new Keybinding { CommandId = "editor.refactor", Key = KeyChord.Parse("Ctrl+Shift+R"), When = KeybindingContexts.EditorTextFocus });
        RegisterKeybinding(new Keybinding { CommandId = "editor.rename", Key = KeyChord.Parse("F2"), When = KeybindingContexts.EditorTextFocus });
        RegisterKeybinding(new Keybinding { CommandId = "editor.triggerSuggest", Key = KeyChord.Parse("Ctrl+Space"), When = KeybindingContexts.EditorTextFocus });
        RegisterKeybinding(new Keybinding { CommandId = "editor.triggerParameterHints", Key = KeyChord.Parse("Ctrl+Shift+Space"), When = KeybindingContexts.EditorTextFocus });

        // Folding
        RegisterKeybinding(new Keybinding { CommandId = "editor.fold", Key = KeyChord.Parse("Ctrl+Shift+["), When = KeybindingContexts.EditorTextFocus });
        RegisterKeybinding(new Keybinding { CommandId = "editor.unfold", Key = KeyChord.Parse("Ctrl+Shift+]"), When = KeybindingContexts.EditorTextFocus });
        RegisterKeybinding(new Keybinding { CommandId = "editor.foldAll", Key = KeyChord.Parse("Ctrl+K Ctrl+0"), When = KeybindingContexts.EditorTextFocus });
        RegisterKeybinding(new Keybinding { CommandId = "editor.unfoldAll", Key = KeyChord.Parse("Ctrl+K Ctrl+J"), When = KeybindingContexts.EditorTextFocus });

        // Debug
        RegisterKeybinding(new Keybinding { CommandId = "debug.start", Key = KeyChord.Parse("F5") });
        RegisterKeybinding(new Keybinding { CommandId = "debug.startWithoutDebugging", Key = KeyChord.Parse("Ctrl+F5") });
        RegisterKeybinding(new Keybinding { CommandId = "debug.stop", Key = KeyChord.Parse("Shift+F5"), When = KeybindingContexts.InDebugMode });
        RegisterKeybinding(new Keybinding { CommandId = "debug.restart", Key = KeyChord.Parse("Ctrl+Shift+F5"), When = KeybindingContexts.InDebugMode });
        RegisterKeybinding(new Keybinding { CommandId = "debug.continue", Key = KeyChord.Parse("F5"), When = KeybindingContexts.InDebugMode });
        RegisterKeybinding(new Keybinding { CommandId = "debug.stepOver", Key = KeyChord.Parse("F10"), When = KeybindingContexts.InDebugMode });
        RegisterKeybinding(new Keybinding { CommandId = "debug.stepInto", Key = KeyChord.Parse("F11"), When = KeybindingContexts.InDebugMode });
        RegisterKeybinding(new Keybinding { CommandId = "debug.stepOut", Key = KeyChord.Parse("Shift+F11"), When = KeybindingContexts.InDebugMode });
        RegisterKeybinding(new Keybinding { CommandId = "debug.toggleBreakpoint", Key = KeyChord.Parse("F9"), When = KeybindingContexts.EditorTextFocus });

        // Build
        RegisterKeybinding(new Keybinding { CommandId = "build.build", Key = KeyChord.Parse("Ctrl+Shift+B") });
        RegisterKeybinding(new Keybinding { CommandId = "build.rebuild", Key = KeyChord.Parse("Ctrl+Alt+B") });
        RegisterKeybinding(new Keybinding { CommandId = "build.clean", Key = KeyChord.Parse("Ctrl+Alt+C") });

        // Terminal
        RegisterKeybinding(new Keybinding { CommandId = "terminal.new", Key = KeyChord.Parse("Ctrl+Shift+`") });
        RegisterKeybinding(new Keybinding { CommandId = "terminal.kill", Key = KeyChord.Parse("Ctrl+Shift+X"), When = KeybindingContexts.TerminalFocus });

        // Suggest widget
        RegisterKeybinding(new Keybinding { CommandId = "suggest.accept", Key = KeyChord.Parse("Enter"), When = KeybindingContexts.SuggestWidgetVisible });
        RegisterKeybinding(new Keybinding { CommandId = "suggest.accept", Key = KeyChord.Parse("Tab"), When = KeybindingContexts.SuggestWidgetVisible });
        RegisterKeybinding(new Keybinding { CommandId = "suggest.cancel", Key = KeyChord.Parse("Escape"), When = KeybindingContexts.SuggestWidgetVisible });
        RegisterKeybinding(new Keybinding { CommandId = "suggest.next", Key = KeyChord.Parse("Down"), When = KeybindingContexts.SuggestWidgetVisible });
        RegisterKeybinding(new Keybinding { CommandId = "suggest.previous", Key = KeyChord.Parse("Up"), When = KeybindingContexts.SuggestWidgetVisible });
    }

    #endregion

    #region DTO for serialization

    private class KeybindingDto
    {
        public string Command { get; set; } = "";
        public string Key { get; set; } = "";
        public string? Key2 { get; set; }
        public string? When { get; set; }
        public object? Args { get; set; }

        public KeybindingDto() { }

        public KeybindingDto(Keybinding kb)
        {
            Command = kb.CommandId;
            Key = kb.Key.GetDisplayString();
            Key2 = kb.SecondKey?.GetDisplayString();
            When = kb.When;
            Args = kb.Args;
        }

        public Keybinding ToKeybinding()
        {
            return new Keybinding
            {
                CommandId = Command,
                Key = KeyChord.Parse(Key),
                SecondKey = Key2 != null ? KeyChord.Parse(Key2) : null,
                When = When,
                Args = Args,
                IsUserDefined = true,
                Source = KeybindingSource.User
            };
        }
    }

    #endregion
}
