using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

/// <summary>
/// Represents a single setting entry that can be searched and displayed in a flat list.
/// Supports User/Workspace scope display with badges and override indicators.
/// </summary>
public partial class SearchableSettingItem : ObservableObject
{
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Category { get; init; } = "";
    public string PropertyName { get; init; } = "";
    public SettingControlKind ControlKind { get; init; }

    /// <summary>For ComboBox settings: the list of choices.</summary>
    public ObservableCollection<string>? Choices { get; init; }

    /// <summary>For NumericUpDown settings.</summary>
    public int Minimum { get; init; }
    public int Maximum { get; init; } = 100;
    public int Increment { get; init; } = 1;

    /// <summary>Back-reference to the owning ViewModel so bindings can read/write the actual property.</summary>
    public SettingsViewModel Owner { get; init; } = null!;

    /// <summary>The default value for this setting, used to detect modifications.</summary>
    public object DefaultValue { get; init; } = "";

    // Convenience booleans for AXAML IsVisible bindings
    public bool IsCheckBox => ControlKind == SettingControlKind.CheckBox;
    public bool IsNumericUpDown => ControlKind == SettingControlKind.NumericUpDown;
    public bool IsComboBox => ControlKind == SettingControlKind.ComboBox;

    /// <summary>True when the current value differs from the default value.</summary>
    public bool IsModified => ControlKind switch
    {
        SettingControlKind.CheckBox => BoolValue != (bool)DefaultValue,
        SettingControlKind.NumericUpDown => IntValue != (int)DefaultValue,
        SettingControlKind.ComboBox => !string.Equals(StringValue, (string)DefaultValue, StringComparison.Ordinal),
        _ => false
    };

    /// <summary>The scope where this setting is currently set (User, Workspace, or Default).</summary>
    [ObservableProperty]
    private string _scopeBadge = "";

    /// <summary>Whether this setting is overridden in workspace settings.</summary>
    [ObservableProperty]
    private bool _isOverriddenInWorkspace;

    /// <summary>Whether the setting is set in the current active scope.</summary>
    [ObservableProperty]
    private bool _isSetInCurrentScope;

    // -- Proxy value properties so the flat list can bind without knowing the concrete property name --

    public bool BoolValue
    {
        get => Owner.GetBoolSetting(PropertyName);
        set { Owner.SetBoolSetting(PropertyName, value); OnPropertyChanged(); OnPropertyChanged(nameof(IsModified)); }
    }

    public int IntValue
    {
        get => Owner.GetIntSetting(PropertyName);
        set { Owner.SetIntSetting(PropertyName, value); OnPropertyChanged(); OnPropertyChanged(nameof(IsModified)); }
    }

    public string StringValue
    {
        get => Owner.GetStringSetting(PropertyName);
        set { Owner.SetStringSetting(PropertyName, value); OnPropertyChanged(); OnPropertyChanged(nameof(IsModified)); }
    }

    /// <summary>Resets this setting to its default value.</summary>
    [RelayCommand]
    private void ResetToDefault()
    {
        switch (ControlKind)
        {
            case SettingControlKind.CheckBox:
                BoolValue = (bool)DefaultValue;
                break;
            case SettingControlKind.NumericUpDown:
                IntValue = (int)DefaultValue;
                break;
            case SettingControlKind.ComboBox:
                StringValue = (string)DefaultValue;
                break;
        }
    }

    /// <summary>Updates scope badges from the settings service.</summary>
    public void RefreshScopeBadges(SettingsService? service, SettingsScope activeScope)
    {
        if (service == null) return;

        var scope = service.GetSettingScope(Key);
        ScopeBadge = scope switch
        {
            SettingsScope.Workspace => "Workspace",
            SettingsScope.User => "User",
            _ => ""
        };

        IsOverriddenInWorkspace = activeScope == SettingsScope.User && service.IsOverriddenInWorkspace(Key);
        IsSetInCurrentScope = service.IsModifiedInScope(Key, activeScope);
    }
}

public enum SettingControlKind
{
    CheckBox,
    NumericUpDown,
    ComboBox
}

public partial class SettingsViewModel : ViewModelBase
{
    private static readonly string LegacySettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VisualGameStudio",
        "settings.json");

    private SettingsService? _settingsService;

    // Current active scope for editing
    [ObservableProperty]
    private SettingsScope _activeScope = SettingsScope.User;

    [ObservableProperty]
    private bool _isUserScopeActive = true;

    [ObservableProperty]
    private bool _isWorkspaceScopeActive;

    [ObservableProperty]
    private bool _isJsonEditorActive;

    [ObservableProperty]
    private bool _hasWorkspace;

    [ObservableProperty]
    private string _jsonEditorContent = "{\n}";

    [ObservableProperty]
    private ObservableCollection<string> _jsonValidationErrors = new();

    [ObservableProperty]
    private string _userSettingsPath = "";

    [ObservableProperty]
    private string _workspaceSettingsPath = "";

    // Editor Settings (backed by ISettingsService for new settings, kept for legacy compat)
    [ObservableProperty]
    private string _fontFamily = "Cascadia Code";

    [ObservableProperty]
    private bool _fontLigatures;

    [ObservableProperty]
    private int _fontSize = 14;

    [ObservableProperty]
    private int _tabSize = 4;

    [ObservableProperty]
    private bool _convertTabsToSpaces = true;

    [ObservableProperty]
    private bool _showLineNumbers = true;

    [ObservableProperty]
    private bool _highlightCurrentLine = true;

    [ObservableProperty]
    private bool _showWhitespace;

    [ObservableProperty]
    private bool _wordWrap;

    [ObservableProperty]
    private bool _autoIndent = true;

    [ObservableProperty]
    private bool _bracketMatching = true;

    [ObservableProperty]
    private bool _autoCloseBrackets = true;

    [ObservableProperty]
    private bool _smoothScrolling = true;

    // Theme Settings
    [ObservableProperty]
    private string _selectedTheme = "Dark";

    [ObservableProperty]
    private ObservableCollection<string> _availableThemes = new() { "Dark", "Light", "High Contrast" };

    // IntelliSense Settings
    [ObservableProperty]
    private bool _enableAutoComplete = true;

    [ObservableProperty]
    private bool _showQuickInfo = true;

    [ObservableProperty]
    private bool _showSignatureHelp = true;

    [ObservableProperty]
    private int _autoCompleteDelay = 200;

    // Build Settings
    [ObservableProperty]
    private bool _saveBeforeBuild = true;

    [ObservableProperty]
    private bool _showBuildOutput = true;

    [ObservableProperty]
    private string _defaultConfiguration = "Debug";

    [ObservableProperty]
    private ObservableCollection<string> _configurations = new() { "Debug", "Release" };

    // Format On Save
    [ObservableProperty]
    private bool _formatOnSave;

    // Render Whitespace
    [ObservableProperty]
    private string _renderWhitespace = "none";

    [ObservableProperty]
    private ObservableCollection<string> _renderWhitespaceOptions = new() { "none", "all", "boundary", "selection" };

    // Word Wrap mode
    [ObservableProperty]
    private string _wordWrapMode = "off";

    [ObservableProperty]
    private ObservableCollection<string> _wordWrapModes = new() { "off", "on", "wordWrapColumn" };

    // Cursor Blinking
    [ObservableProperty]
    private string _cursorBlinking = "blink";

    [ObservableProperty]
    private ObservableCollection<string> _cursorBlinkingOptions = new() { "blink", "smooth", "phase", "expand", "solid" };

    // Auto Save
    [ObservableProperty]
    private string _autoSaveMode = "off";

    [ObservableProperty]
    private ObservableCollection<string> _autoSaveModes = new() { "off", "afterDelay", "onFocusChange", "onWindowChange" };

    // Compiler Backend
    [ObservableProperty]
    private string _compilerBackend = "CSharp";

    [ObservableProperty]
    private ObservableCollection<string> _compilerBackends = new() { "CSharp", "MSIL", "LLVM", "CPP" };

    // Keyboard Shortcuts
    [ObservableProperty]
    private ObservableCollection<KeyboardShortcut> _shortcuts = new();

    // Available Fonts - populated from system fonts at construction
    [ObservableProperty]
    private ObservableCollection<string> _availableFonts = new();

    // Search
    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private bool _isSearchActive;

    [ObservableProperty]
    private ObservableCollection<SearchableSettingItem> _filteredSettings = new();

    /// <summary>All searchable settings (built once).</summary>
    private List<SearchableSettingItem> _allSettings = new();

    public Action? CloseDialog { get; set; }
    public bool DialogResult { get; private set; }

    public static event EventHandler? SettingsChanged;

    public SettingsViewModel()
    {
        // Try to resolve SettingsService from DI
        try
        {
            _settingsService = App.Services?.GetService(typeof(ISettingsService)) as SettingsService;
        }
        catch { }

        PopulateAvailableFonts();
        InitializeShortcuts();
        LoadSettings();
        BuildSearchableSettings();
        UpdateScopePaths();
    }

    /// <summary>
    /// Constructor for injection.
    /// </summary>
    public SettingsViewModel(SettingsService settingsService) : this()
    {
        _settingsService = settingsService;
        LoadFromService();
        UpdateScopePaths();
    }

    private void UpdateScopePaths()
    {
        if (_settingsService != null)
        {
            UserSettingsPath = _settingsService.UserSettingsPath;
            WorkspaceSettingsPath = _settingsService.WorkspaceSettingsPath ?? "";
            HasWorkspace = _settingsService.HasWorkspace;
        }
    }

    /// <summary>
    /// Populates the AvailableFonts collection from system-installed font families.
    /// </summary>
    private void PopulateAvailableFonts()
    {
        var priorityFonts = new[]
        {
            "Cascadia Code", "Cascadia Mono",
            "Consolas",
            "Fira Code",
            "JetBrains Mono",
            "Source Code Pro",
            "Hack",
            "Inconsolata",
            "IBM Plex Mono",
            "Ubuntu Mono",
            "Roboto Mono",
            "SF Mono",
            "Monaco",
            "Menlo",
            "DejaVu Sans Mono",
            "Courier New"
        };

        var systemFontNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var fontManager = FontManager.Current;
            foreach (var family in fontManager.SystemFonts)
            {
                systemFontNames.Add(family.Name);
            }
        }
        catch { }

        AvailableFonts.Clear();

        foreach (var font in priorityFonts)
        {
            if (systemFontNames.Contains(font))
            {
                AvailableFonts.Add(font);
                systemFontNames.Remove(font);
            }
        }

        foreach (var font in systemFontNames.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            AvailableFonts.Add(font);
        }

        if (AvailableFonts.Count == 0)
        {
            foreach (var font in priorityFonts)
                AvailableFonts.Add(font);
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        IsSearchActive = !string.IsNullOrWhiteSpace(value);
        ApplySearchFilter(value);
    }

    partial void OnActiveScopeChanged(SettingsScope value)
    {
        IsUserScopeActive = value == SettingsScope.User;
        IsWorkspaceScopeActive = value == SettingsScope.Workspace;

        // Reload settings for the new scope
        LoadFromService();
        RefreshAllScopeBadges();

        // Update JSON editor if active
        if (IsJsonEditorActive)
        {
            LoadJsonEditorContent();
        }
    }

    [RelayCommand]
    private void SwitchToUserScope()
    {
        ActiveScope = SettingsScope.User;
    }

    [RelayCommand]
    private void SwitchToWorkspaceScope()
    {
        if (HasWorkspace)
            ActiveScope = SettingsScope.Workspace;
    }

    [RelayCommand]
    private void ToggleJsonEditor()
    {
        IsJsonEditorActive = !IsJsonEditorActive;
        if (IsJsonEditorActive)
        {
            LoadJsonEditorContent();
        }
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = "";
    }

    [RelayCommand]
    private void SaveJsonEditor()
    {
        if (_settingsService != null)
        {
            _ = _settingsService.SetRawJsonAsync(JsonEditorContent, ActiveScope);
            LoadFromService();
            RefreshAllScopeBadges();
        }
    }

    [RelayCommand]
    private void ValidateJsonEditor()
    {
        JsonValidationErrors.Clear();
        if (_settingsService == null) return;

        try
        {
            // Try to parse
            var stripped = StripJsonComments(JsonEditorContent);
            JsonSerializer.Deserialize<Dictionary<string, object>>(stripped);

            // Check for unknown keys
            var unknownKeys = _settingsService.ValidateJson(JsonEditorContent);
            foreach (var key in unknownKeys)
            {
                JsonValidationErrors.Add($"Unknown setting: \"{key}\"");
            }

            if (JsonValidationErrors.Count == 0)
            {
                JsonValidationErrors.Add("No issues found.");
            }
        }
        catch (JsonException ex)
        {
            JsonValidationErrors.Add($"JSON parse error: {ex.Message}");
        }
    }

    private void LoadJsonEditorContent()
    {
        if (_settingsService != null)
        {
            JsonEditorContent = _settingsService.GetRawJson(ActiveScope);
        }
    }

    private static string StripJsonComments(string json)
    {
        var result = new System.Text.StringBuilder();
        bool inString = false;
        bool escaped = false;

        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];

            if (escaped) { result.Append(c); escaped = false; continue; }
            if (c == '\\' && inString) { result.Append(c); escaped = true; continue; }
            if (c == '"' && !escaped) { inString = !inString; result.Append(c); continue; }

            if (!inString)
            {
                if (c == '/' && i + 1 < json.Length && json[i + 1] == '/')
                {
                    while (i < json.Length && json[i] != '\n') i++;
                    if (i < json.Length) result.Append('\n');
                    continue;
                }
                if (c == '/' && i + 1 < json.Length && json[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < json.Length && !(json[i] == '*' && json[i + 1] == '/')) i++;
                    i += 1;
                    continue;
                }
            }
            result.Append(c);
        }

        return System.Text.RegularExpressions.Regex.Replace(result.ToString(), @",\s*([\}\]])", "$1");
    }

    private void ApplySearchFilter(string query)
    {
        FilteredSettings.Clear();

        if (string.IsNullOrWhiteSpace(query))
            return;

        bool filterModifiedOnly = false;
        var cleanQuery = query;
        if (query.Contains("@modified", StringComparison.OrdinalIgnoreCase))
        {
            filterModifiedOnly = true;
            cleanQuery = query.Replace("@modified", "", StringComparison.OrdinalIgnoreCase).Trim();
        }

        var terms = cleanQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var item in _allSettings)
        {
            if (filterModifiedOnly && !item.IsModified)
                continue;

            bool matches = true;
            foreach (var term in terms)
            {
                if (!item.Name.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                    !item.Description.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                    !item.Category.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                    !item.Key.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    matches = false;
                    break;
                }
            }
            if (matches)
                FilteredSettings.Add(item);
        }
    }

    private void BuildSearchableSettings()
    {
        _allSettings = new List<SearchableSettingItem>
        {
            // Editor > Font
            MakeCombo("editor.fontFamily", "Font Family", "Font family used in the code editor", "Editor", nameof(FontFamily), AvailableFonts, "Cascadia Code"),
            MakeNumeric("editor.fontSize", "Font Size", "Font size for the code editor (in points)", "Editor", nameof(FontSize), 8, 72, defaultValue: 14),
            MakeBool("editor.fontLigatures", "Font Ligatures", "Enable font ligatures for supported fonts (e.g. Cascadia Code, Fira Code, JetBrains Mono).", "Editor", nameof(FontLigatures), false),

            // Editor > Tabs
            MakeNumeric("editor.tabSize", "Tab Size", "Number of spaces per tab stop", "Editor", nameof(TabSize), 1, 16, defaultValue: 4),
            MakeBool("editor.insertSpaces", "Convert Tabs to Spaces", "Insert spaces instead of tab characters when pressing Tab", "Editor", nameof(ConvertTabsToSpaces), true),

            // Editor > Display
            MakeBool("editor.lineNumbers", "Show Line Numbers", "Display line numbers in the editor gutter", "Editor", nameof(ShowLineNumbers), true),
            MakeBool("editor.highlightCurrentLine", "Highlight Current Line", "Highlight the line where the cursor is located", "Editor", nameof(HighlightCurrentLine), true),
            MakeCombo("editor.renderWhitespace", "Render Whitespace", "Controls rendering of whitespace characters", "Editor", nameof(RenderWhitespace), RenderWhitespaceOptions, "none"),
            MakeCombo("editor.wordWrap", "Word Wrap", "Controls how lines should wrap", "Editor", nameof(WordWrapMode), WordWrapModes, "off"),

            // Editor > Behavior
            MakeBool("editor.autoIndent", "Auto Indent", "Automatically indent new lines based on the previous line", "Editor", nameof(AutoIndent), true),
            MakeBool("editor.bracketPairColorization", "Bracket Matching", "Highlight matching brackets when the cursor is near one", "Editor", nameof(BracketMatching), true),
            MakeBool("editor.autoClosingBrackets", "Auto Close Brackets", "Automatically insert closing brackets, quotes, and parentheses", "Editor", nameof(AutoCloseBrackets), true),
            MakeBool("editor.smoothScrolling", "Smooth Scrolling", "Animate scrolling for a smoother visual experience", "Editor", nameof(SmoothScrolling), true),
            MakeBool("editor.formatOnSave", "Format On Save", "Format a file on save", "Editor", nameof(FormatOnSave), false),
            MakeCombo("editor.cursorBlinking", "Cursor Blinking", "Controls cursor blinking animation style", "Editor", nameof(CursorBlinking), CursorBlinkingOptions, "blink"),

            // IntelliSense
            MakeBool("intellisense.autoComplete", "Enable Auto Complete", "Show completion suggestions as you type", "IntelliSense", nameof(EnableAutoComplete), true),
            MakeBool("intellisense.quickInfo", "Show Quick Info", "Display type and documentation info on hover", "IntelliSense", nameof(ShowQuickInfo), true),
            MakeBool("intellisense.signatureHelp", "Show Signature Help", "Show parameter info when typing function arguments", "IntelliSense", nameof(ShowSignatureHelp), true),
            MakeNumeric("intellisense.delay", "Auto Complete Delay", "Delay in milliseconds before showing completions", "IntelliSense", nameof(AutoCompleteDelay), 0, 2000, 50, defaultValue: 200),

            // Build
            MakeBool("build.saveBeforeBuild", "Save Before Build", "Automatically save all open files before building", "Build", nameof(SaveBeforeBuild), true),
            MakeBool("build.showOutput", "Show Build Output", "Show the build output panel when a build starts", "Build", nameof(ShowBuildOutput), true),
            MakeCombo("build.defaultConfiguration", "Default Configuration", "Default build configuration for new projects", "Build", nameof(DefaultConfiguration), Configurations, "Debug"),

            // Appearance
            MakeCombo("workbench.colorTheme", "Color Theme", "Overall color theme for the IDE", "Appearance", nameof(SelectedTheme), AvailableThemes, "Dark"),

            // Files
            MakeCombo("files.autoSave", "Auto Save", "Controls auto save of editors", "Files", nameof(AutoSaveMode), AutoSaveModes, "off"),

            // BasicLang
            MakeCombo("basiclang.compiler.backend", "Compiler Backend", "The default compilation backend", "BasicLang", nameof(CompilerBackend), CompilerBackends, "CSharp"),
        };
    }

    private SearchableSettingItem MakeBool(string key, string name, string desc, string category, string prop, bool defaultValue = true) =>
        new() { Key = key, Name = name, Description = desc, Category = category, PropertyName = prop, ControlKind = SettingControlKind.CheckBox, Owner = this, DefaultValue = defaultValue };

    private SearchableSettingItem MakeNumeric(string key, string name, string desc, string category, string prop, int min, int max, int inc = 1, int defaultValue = 0) =>
        new() { Key = key, Name = name, Description = desc, Category = category, PropertyName = prop, ControlKind = SettingControlKind.NumericUpDown, Minimum = min, Maximum = max, Increment = inc, Owner = this, DefaultValue = defaultValue };

    private SearchableSettingItem MakeCombo(string key, string name, string desc, string category, string prop, ObservableCollection<string> choices, string defaultValue = "") =>
        new() { Key = key, Name = name, Description = desc, Category = category, PropertyName = prop, ControlKind = SettingControlKind.ComboBox, Choices = choices, Owner = this, DefaultValue = defaultValue };

    private void RefreshAllScopeBadges()
    {
        foreach (var item in _allSettings)
        {
            item.RefreshScopeBadges(_settingsService, ActiveScope);
        }
    }

    // -- Reflection-free property accessors for SearchableSettingItem proxies --

    internal bool GetBoolSetting(string prop) => prop switch
    {
        nameof(FontLigatures) => FontLigatures,
        nameof(ConvertTabsToSpaces) => ConvertTabsToSpaces,
        nameof(ShowLineNumbers) => ShowLineNumbers,
        nameof(HighlightCurrentLine) => HighlightCurrentLine,
        nameof(ShowWhitespace) => ShowWhitespace,
        nameof(WordWrap) => WordWrap,
        nameof(AutoIndent) => AutoIndent,
        nameof(BracketMatching) => BracketMatching,
        nameof(AutoCloseBrackets) => AutoCloseBrackets,
        nameof(SmoothScrolling) => SmoothScrolling,
        nameof(EnableAutoComplete) => EnableAutoComplete,
        nameof(ShowQuickInfo) => ShowQuickInfo,
        nameof(ShowSignatureHelp) => ShowSignatureHelp,
        nameof(SaveBeforeBuild) => SaveBeforeBuild,
        nameof(ShowBuildOutput) => ShowBuildOutput,
        nameof(FormatOnSave) => FormatOnSave,
        _ => false
    };

    internal void SetBoolSetting(string prop, bool value)
    {
        switch (prop)
        {
            case nameof(FontLigatures): FontLigatures = value; break;
            case nameof(ConvertTabsToSpaces): ConvertTabsToSpaces = value; break;
            case nameof(ShowLineNumbers): ShowLineNumbers = value; break;
            case nameof(HighlightCurrentLine): HighlightCurrentLine = value; break;
            case nameof(ShowWhitespace): ShowWhitespace = value; break;
            case nameof(WordWrap): WordWrap = value; break;
            case nameof(AutoIndent): AutoIndent = value; break;
            case nameof(BracketMatching): BracketMatching = value; break;
            case nameof(AutoCloseBrackets): AutoCloseBrackets = value; break;
            case nameof(SmoothScrolling): SmoothScrolling = value; break;
            case nameof(EnableAutoComplete): EnableAutoComplete = value; break;
            case nameof(ShowQuickInfo): ShowQuickInfo = value; break;
            case nameof(ShowSignatureHelp): ShowSignatureHelp = value; break;
            case nameof(SaveBeforeBuild): SaveBeforeBuild = value; break;
            case nameof(ShowBuildOutput): ShowBuildOutput = value; break;
            case nameof(FormatOnSave): FormatOnSave = value; break;
        }
    }

    internal int GetIntSetting(string prop) => prop switch
    {
        nameof(FontSize) => FontSize,
        nameof(TabSize) => TabSize,
        nameof(AutoCompleteDelay) => AutoCompleteDelay,
        _ => 0
    };

    internal void SetIntSetting(string prop, int value)
    {
        switch (prop)
        {
            case nameof(FontSize): FontSize = value; break;
            case nameof(TabSize): TabSize = value; break;
            case nameof(AutoCompleteDelay): AutoCompleteDelay = value; break;
        }
    }

    internal string GetStringSetting(string prop) => prop switch
    {
        nameof(FontFamily) => FontFamily,
        nameof(SelectedTheme) => SelectedTheme,
        nameof(DefaultConfiguration) => DefaultConfiguration,
        nameof(RenderWhitespace) => RenderWhitespace,
        nameof(WordWrapMode) => WordWrapMode,
        nameof(CursorBlinking) => CursorBlinking,
        nameof(AutoSaveMode) => AutoSaveMode,
        nameof(CompilerBackend) => CompilerBackend,
        _ => ""
    };

    internal void SetStringSetting(string prop, string value)
    {
        switch (prop)
        {
            case nameof(FontFamily): FontFamily = value; break;
            case nameof(SelectedTheme): SelectedTheme = value; break;
            case nameof(DefaultConfiguration): DefaultConfiguration = value; break;
            case nameof(RenderWhitespace): RenderWhitespace = value; break;
            case nameof(WordWrapMode): WordWrapMode = value; break;
            case nameof(CursorBlinking): CursorBlinking = value; break;
            case nameof(AutoSaveMode): AutoSaveMode = value; break;
            case nameof(CompilerBackend): CompilerBackend = value; break;
        }
    }

    private void InitializeShortcuts()
    {
        Shortcuts.Add(new KeyboardShortcut { Action = "Save", CurrentBinding = "Ctrl+S", DefaultBinding = "Ctrl+S" });
        Shortcuts.Add(new KeyboardShortcut { Action = "Save All", CurrentBinding = "Ctrl+Shift+S", DefaultBinding = "Ctrl+Shift+S" });
        Shortcuts.Add(new KeyboardShortcut { Action = "Open File", CurrentBinding = "Ctrl+O", DefaultBinding = "Ctrl+O" });
        Shortcuts.Add(new KeyboardShortcut { Action = "New Project", CurrentBinding = "Ctrl+Shift+N", DefaultBinding = "Ctrl+Shift+N" });
        Shortcuts.Add(new KeyboardShortcut { Action = "Find", CurrentBinding = "Ctrl+F", DefaultBinding = "Ctrl+F" });
        Shortcuts.Add(new KeyboardShortcut { Action = "Replace", CurrentBinding = "Ctrl+H", DefaultBinding = "Ctrl+H" });
        Shortcuts.Add(new KeyboardShortcut { Action = "Find in Files", CurrentBinding = "Ctrl+Shift+F", DefaultBinding = "Ctrl+Shift+F" });
        Shortcuts.Add(new KeyboardShortcut { Action = "Go to Definition", CurrentBinding = "F12", DefaultBinding = "F12" });
        Shortcuts.Add(new KeyboardShortcut { Action = "Find References", CurrentBinding = "Shift+F12", DefaultBinding = "Shift+F12" });
        Shortcuts.Add(new KeyboardShortcut { Action = "Build", CurrentBinding = "Ctrl+Shift+B", DefaultBinding = "Ctrl+Shift+B" });
        Shortcuts.Add(new KeyboardShortcut { Action = "Start Debugging", CurrentBinding = "F5", DefaultBinding = "F5" });
        Shortcuts.Add(new KeyboardShortcut { Action = "Step Over", CurrentBinding = "F10", DefaultBinding = "F10" });
        Shortcuts.Add(new KeyboardShortcut { Action = "Step Into", CurrentBinding = "F11", DefaultBinding = "F11" });
        Shortcuts.Add(new KeyboardShortcut { Action = "Toggle Breakpoint", CurrentBinding = "F9", DefaultBinding = "F9" });
        Shortcuts.Add(new KeyboardShortcut { Action = "Comment Line", CurrentBinding = "Ctrl+/", DefaultBinding = "Ctrl+/" });
        Shortcuts.Add(new KeyboardShortcut { Action = "Duplicate Line", CurrentBinding = "Ctrl+D", DefaultBinding = "Ctrl+D" });
    }

    [RelayCommand]
    private void Save()
    {
        SaveSettings();
        DialogResult = true;

        // Apply theme change immediately
        ThemeManager.Apply(SelectedTheme);

        // Push to ISettingsService
        SaveToService();

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        CloseDialog?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        CloseDialog?.Invoke();
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        FontFamily = "Cascadia Code";
        FontLigatures = false;
        FontSize = 14;
        TabSize = 4;
        ConvertTabsToSpaces = true;
        ShowLineNumbers = true;
        HighlightCurrentLine = true;
        ShowWhitespace = false;
        WordWrap = false;
        AutoIndent = true;
        BracketMatching = true;
        AutoCloseBrackets = true;
        SmoothScrolling = true;
        SelectedTheme = "Dark";
        EnableAutoComplete = true;
        ShowQuickInfo = true;
        ShowSignatureHelp = true;
        AutoCompleteDelay = 200;
        SaveBeforeBuild = true;
        ShowBuildOutput = true;
        DefaultConfiguration = "Debug";
        FormatOnSave = false;
        RenderWhitespace = "none";
        WordWrapMode = "off";
        CursorBlinking = "blink";
        AutoSaveMode = "off";
        CompilerBackend = "CSharp";

        foreach (var shortcut in Shortcuts)
        {
            shortcut.CurrentBinding = shortcut.DefaultBinding;
        }

        if (_settingsService != null)
        {
            _settingsService.ResetAllToDefaults();
        }
    }

    /// <summary>
    /// Loads settings from ISettingsService into the ViewModel properties.
    /// </summary>
    private void LoadFromService()
    {
        if (_settingsService == null) return;

        var scope = ActiveScope == SettingsScope.Workspace ? SettingsScope.Workspace : SettingsScope.Effective;

        FontFamily = _settingsService.Get("editor.fontFamily", "Cascadia Code", scope);
        FontLigatures = _settingsService.Get("editor.fontLigatures", false, scope);
        FontSize = _settingsService.Get("editor.fontSize", 14, scope);
        TabSize = _settingsService.Get("editor.tabSize", 4, scope);
        ConvertTabsToSpaces = _settingsService.Get("editor.insertSpaces", true, scope);
        ShowLineNumbers = _settingsService.Get("editor.lineNumbers", "on", scope) != "off";
        HighlightCurrentLine = _settingsService.Get("editor.highlightCurrentLine", true, scope);
        RenderWhitespace = _settingsService.Get("editor.renderWhitespace", "none", scope);
        ShowWhitespace = RenderWhitespace != "none";
        WordWrapMode = _settingsService.Get("editor.wordWrap", "off", scope);
        WordWrap = WordWrapMode != "off";
        AutoIndent = _settingsService.Get("editor.autoIndent", true, scope);
        BracketMatching = _settingsService.Get("editor.bracketPairColorization", true, scope);
        AutoCloseBrackets = _settingsService.Get("editor.autoClosingBrackets", "always", scope) != "never";
        SmoothScrolling = _settingsService.Get("editor.smoothScrolling", true, scope);
        FormatOnSave = _settingsService.Get("editor.formatOnSave", false, scope);
        CursorBlinking = _settingsService.Get("editor.cursorBlinking", "blink", scope);
        SelectedTheme = _settingsService.Get("workbench.colorTheme", "Dark", scope);
        EnableAutoComplete = _settingsService.Get("intellisense.autoComplete", true, scope);
        ShowQuickInfo = _settingsService.Get("intellisense.quickInfo", true, scope);
        ShowSignatureHelp = _settingsService.Get("intellisense.signatureHelp", true, scope);
        AutoCompleteDelay = _settingsService.Get("intellisense.delay", 200, scope);
        SaveBeforeBuild = _settingsService.Get("build.saveBeforeBuild", true, scope);
        ShowBuildOutput = _settingsService.Get("build.showOutput", true, scope);
        DefaultConfiguration = _settingsService.Get("build.defaultConfiguration", "Debug", scope);
        AutoSaveMode = _settingsService.Get("files.autoSave", "off", scope);
        CompilerBackend = _settingsService.Get("basiclang.compiler.backend", "CSharp", scope);
    }

    /// <summary>
    /// Pushes ViewModel properties to ISettingsService.
    /// </summary>
    private void SaveToService()
    {
        if (_settingsService == null) return;

        var scope = ActiveScope;

        _settingsService.Set("editor.fontFamily", FontFamily, scope);
        _settingsService.Set("editor.fontLigatures", FontLigatures, scope);
        _settingsService.Set("editor.fontSize", FontSize, scope);
        _settingsService.Set("editor.tabSize", TabSize, scope);
        _settingsService.Set("editor.insertSpaces", ConvertTabsToSpaces, scope);
        _settingsService.Set("editor.lineNumbers", ShowLineNumbers ? "on" : "off", scope);
        _settingsService.Set("editor.highlightCurrentLine", HighlightCurrentLine, scope);
        _settingsService.Set("editor.renderWhitespace", RenderWhitespace, scope);
        _settingsService.Set("editor.wordWrap", WordWrapMode, scope);
        _settingsService.Set("editor.autoIndent", AutoIndent, scope);
        _settingsService.Set("editor.bracketPairColorization", BracketMatching, scope);
        _settingsService.Set("editor.autoClosingBrackets", AutoCloseBrackets ? "always" : "never", scope);
        _settingsService.Set("editor.smoothScrolling", SmoothScrolling, scope);
        _settingsService.Set("editor.formatOnSave", FormatOnSave, scope);
        _settingsService.Set("editor.cursorBlinking", CursorBlinking, scope);
        _settingsService.Set("workbench.colorTheme", SelectedTheme, scope);
        _settingsService.Set("intellisense.autoComplete", EnableAutoComplete, scope);
        _settingsService.Set("intellisense.quickInfo", ShowQuickInfo, scope);
        _settingsService.Set("intellisense.signatureHelp", ShowSignatureHelp, scope);
        _settingsService.Set("intellisense.delay", AutoCompleteDelay, scope);
        _settingsService.Set("build.saveBeforeBuild", SaveBeforeBuild, scope);
        _settingsService.Set("build.showOutput", ShowBuildOutput, scope);
        _settingsService.Set("build.defaultConfiguration", DefaultConfiguration, scope);
        _settingsService.Set("files.autoSave", AutoSaveMode, scope);
        _settingsService.Set("basiclang.compiler.backend", CompilerBackend, scope);
    }

    private void LoadSettings()
    {
        // First try ISettingsService
        if (_settingsService != null)
        {
            LoadFromService();
            return;
        }

        // Legacy: load from old settings file
        try
        {
            if (File.Exists(LegacySettingsPath))
            {
                var json = File.ReadAllText(LegacySettingsPath);
                var settings = JsonSerializer.Deserialize<SettingsData>(json);

                if (settings != null)
                {
                    FontFamily = settings.FontFamily ?? FontFamily;
                    FontLigatures = settings.FontLigatures;
                    FontSize = settings.FontSize > 0 ? settings.FontSize : FontSize;
                    TabSize = settings.TabSize > 0 ? settings.TabSize : TabSize;
                    ConvertTabsToSpaces = settings.ConvertTabsToSpaces;
                    ShowLineNumbers = settings.ShowLineNumbers;
                    HighlightCurrentLine = settings.HighlightCurrentLine;
                    ShowWhitespace = settings.ShowWhitespace;
                    WordWrap = settings.WordWrap;
                    AutoIndent = settings.AutoIndent;
                    BracketMatching = settings.BracketMatching;
                    AutoCloseBrackets = settings.AutoCloseBrackets;
                    SmoothScrolling = settings.SmoothScrolling;
                    SelectedTheme = settings.SelectedTheme ?? SelectedTheme;
                    EnableAutoComplete = settings.EnableAutoComplete;
                    ShowQuickInfo = settings.ShowQuickInfo;
                    ShowSignatureHelp = settings.ShowSignatureHelp;
                    AutoCompleteDelay = settings.AutoCompleteDelay > 0 ? settings.AutoCompleteDelay : AutoCompleteDelay;
                    SaveBeforeBuild = settings.SaveBeforeBuild;
                    ShowBuildOutput = settings.ShowBuildOutput;
                    DefaultConfiguration = settings.DefaultConfiguration ?? DefaultConfiguration;

                    if (settings.Shortcuts != null)
                    {
                        foreach (var shortcut in Shortcuts)
                        {
                            if (settings.Shortcuts.TryGetValue(shortcut.Action, out var binding))
                            {
                                shortcut.CurrentBinding = binding;
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Use defaults
        }
    }

    private void SaveSettings()
    {
        try
        {
            // Save to legacy location for backward compatibility
            var dir = Path.GetDirectoryName(LegacySettingsPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var settings = new SettingsData
            {
                FontFamily = FontFamily,
                FontLigatures = FontLigatures,
                FontSize = FontSize,
                TabSize = TabSize,
                ConvertTabsToSpaces = ConvertTabsToSpaces,
                ShowLineNumbers = ShowLineNumbers,
                HighlightCurrentLine = HighlightCurrentLine,
                ShowWhitespace = ShowWhitespace,
                WordWrap = WordWrap,
                AutoIndent = AutoIndent,
                BracketMatching = BracketMatching,
                AutoCloseBrackets = AutoCloseBrackets,
                SmoothScrolling = SmoothScrolling,
                SelectedTheme = SelectedTheme,
                EnableAutoComplete = EnableAutoComplete,
                ShowQuickInfo = ShowQuickInfo,
                ShowSignatureHelp = ShowSignatureHelp,
                AutoCompleteDelay = AutoCompleteDelay,
                SaveBeforeBuild = SaveBeforeBuild,
                ShowBuildOutput = ShowBuildOutput,
                DefaultConfiguration = DefaultConfiguration,
                Shortcuts = Shortcuts.ToDictionary(s => s.Action, s => s.CurrentBinding)
            };

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(LegacySettingsPath, json);
        }
        catch
        {
            // Ignore save errors
        }
    }

    public static SettingsData LoadCurrentSettings()
    {
        try
        {
            if (File.Exists(LegacySettingsPath))
            {
                var json = File.ReadAllText(LegacySettingsPath);
                return JsonSerializer.Deserialize<SettingsData>(json) ?? new SettingsData();
            }
        }
        catch
        {
        }
        return new SettingsData();
    }
}

public class KeyboardShortcut : ObservableObject
{
    private string _currentBinding = "";

    public string Action { get; set; } = "";
    public string DefaultBinding { get; set; } = "";

    public string CurrentBinding
    {
        get => _currentBinding;
        set => SetProperty(ref _currentBinding, value);
    }

    public bool IsModified => CurrentBinding != DefaultBinding;
}

public class SettingsData
{
    public string? FontFamily { get; set; }
    public bool FontLigatures { get; set; }
    public int FontSize { get; set; }
    public int TabSize { get; set; }
    public bool ConvertTabsToSpaces { get; set; } = true;
    public bool ShowLineNumbers { get; set; } = true;
    public bool HighlightCurrentLine { get; set; } = true;
    public bool ShowWhitespace { get; set; }
    public bool WordWrap { get; set; }
    public bool AutoIndent { get; set; } = true;
    public bool BracketMatching { get; set; } = true;
    public bool AutoCloseBrackets { get; set; } = true;
    public bool SmoothScrolling { get; set; } = true;
    public string? SelectedTheme { get; set; }
    public bool EnableAutoComplete { get; set; } = true;
    public bool ShowQuickInfo { get; set; } = true;
    public bool ShowSignatureHelp { get; set; } = true;
    public int AutoCompleteDelay { get; set; } = 200;
    public bool SaveBeforeBuild { get; set; } = true;
    public bool ShowBuildOutput { get; set; } = true;
    public string? DefaultConfiguration { get; set; }
    public Dictionary<string, string>? Shortcuts { get; set; }
}

/// <summary>
/// Converts a boolean IsModified value to a Thickness for the left border indicator.
/// Modified settings get a 3px blue left border; unmodified settings get no border.
/// </summary>
public class ModifiedBorderConverter : IValueConverter
{
    public static readonly ModifiedBorderConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true)
            return new Thickness(3, 0, 0, 0);
        return new Thickness(0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Converts a scope badge string to visibility. Non-empty = visible.
/// </summary>
public class ScopeBadgeVisibilityConverter : IValueConverter
{
    public static readonly ScopeBadgeVisibilityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string s && !string.IsNullOrEmpty(s);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
