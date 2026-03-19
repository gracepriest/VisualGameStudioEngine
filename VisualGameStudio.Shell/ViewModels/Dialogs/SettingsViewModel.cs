using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

/// <summary>
/// Represents a single setting entry that can be searched and displayed in a flat list.
/// </summary>
public partial class SearchableSettingItem : ObservableObject
{
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

    // Convenience booleans for AXAML IsVisible bindings
    public bool IsCheckBox => ControlKind == SettingControlKind.CheckBox;
    public bool IsNumericUpDown => ControlKind == SettingControlKind.NumericUpDown;
    public bool IsComboBox => ControlKind == SettingControlKind.ComboBox;

    // -- Proxy value properties so the flat list can bind without knowing the concrete property name --

    public bool BoolValue
    {
        get => Owner.GetBoolSetting(PropertyName);
        set { Owner.SetBoolSetting(PropertyName, value); OnPropertyChanged(); }
    }

    public int IntValue
    {
        get => Owner.GetIntSetting(PropertyName);
        set { Owner.SetIntSetting(PropertyName, value); OnPropertyChanged(); }
    }

    public string StringValue
    {
        get => Owner.GetStringSetting(PropertyName);
        set { Owner.SetStringSetting(PropertyName, value); OnPropertyChanged(); }
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
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VisualGameStudio",
        "settings.json");

    // Editor Settings
    [ObservableProperty]
    private string _fontFamily = "Cascadia Code";

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

    // Keyboard Shortcuts
    [ObservableProperty]
    private ObservableCollection<KeyboardShortcut> _shortcuts = new();

    // Available Fonts
    [ObservableProperty]
    private ObservableCollection<string> _availableFonts = new()
    {
        "Cascadia Code",
        "Consolas",
        "Fira Code",
        "JetBrains Mono",
        "Source Code Pro",
        "Monaco",
        "Menlo",
        "Courier New"
    };

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
        InitializeShortcuts();
        LoadSettings();
        BuildSearchableSettings();
    }

    partial void OnSearchTextChanged(string value)
    {
        IsSearchActive = !string.IsNullOrWhiteSpace(value);
        ApplySearchFilter(value);
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = "";
    }

    private void ApplySearchFilter(string query)
    {
        FilteredSettings.Clear();

        if (string.IsNullOrWhiteSpace(query))
            return;

        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var item in _allSettings)
        {
            bool matches = true;
            foreach (var term in terms)
            {
                if (!item.Name.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                    !item.Description.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                    !item.Category.Contains(term, StringComparison.OrdinalIgnoreCase))
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
            MakeCombo("Font Family", "Font family used in the code editor", "Editor", nameof(FontFamily), AvailableFonts),
            MakeNumeric("Font Size", "Font size for the code editor (in points)", "Editor", nameof(FontSize), 8, 72),

            // Editor > Tabs
            MakeNumeric("Tab Size", "Number of spaces per tab stop", "Editor", nameof(TabSize), 1, 16),
            MakeBool("Convert Tabs to Spaces", "Insert spaces instead of tab characters when pressing Tab", "Editor", nameof(ConvertTabsToSpaces)),

            // Editor > Display
            MakeBool("Show Line Numbers", "Display line numbers in the editor gutter", "Editor", nameof(ShowLineNumbers)),
            MakeBool("Highlight Current Line", "Highlight the line where the cursor is located", "Editor", nameof(HighlightCurrentLine)),
            MakeBool("Show Whitespace", "Render whitespace characters (spaces, tabs) as visible dots", "Editor", nameof(ShowWhitespace)),
            MakeBool("Word Wrap", "Wrap long lines to fit within the editor width", "Editor", nameof(WordWrap)),

            // Editor > Behavior
            MakeBool("Auto Indent", "Automatically indent new lines based on the previous line", "Editor", nameof(AutoIndent)),
            MakeBool("Bracket Matching", "Highlight matching brackets when the cursor is near one", "Editor", nameof(BracketMatching)),
            MakeBool("Auto Close Brackets", "Automatically insert closing brackets, quotes, and parentheses", "Editor", nameof(AutoCloseBrackets)),

            // IntelliSense
            MakeBool("Enable Auto Complete", "Show completion suggestions as you type", "IntelliSense", nameof(EnableAutoComplete)),
            MakeBool("Show Quick Info", "Display type and documentation info on hover", "IntelliSense", nameof(ShowQuickInfo)),
            MakeBool("Show Signature Help", "Show parameter info when typing function arguments", "IntelliSense", nameof(ShowSignatureHelp)),
            MakeNumeric("Auto Complete Delay", "Delay in milliseconds before showing completions", "IntelliSense", nameof(AutoCompleteDelay), 0, 2000, 50),

            // Build
            MakeBool("Save Before Build", "Automatically save all open files before building", "Build", nameof(SaveBeforeBuild)),
            MakeBool("Show Build Output", "Show the build output panel when a build starts", "Build", nameof(ShowBuildOutput)),
            MakeCombo("Default Configuration", "Default build configuration for new projects", "Build", nameof(DefaultConfiguration), Configurations),

            // Appearance
            MakeCombo("Color Theme", "Overall color theme for the IDE", "Appearance", nameof(SelectedTheme), AvailableThemes),
        };
    }

    private SearchableSettingItem MakeBool(string name, string desc, string category, string prop) =>
        new() { Name = name, Description = desc, Category = category, PropertyName = prop, ControlKind = SettingControlKind.CheckBox, Owner = this };

    private SearchableSettingItem MakeNumeric(string name, string desc, string category, string prop, int min, int max, int inc = 1) =>
        new() { Name = name, Description = desc, Category = category, PropertyName = prop, ControlKind = SettingControlKind.NumericUpDown, Minimum = min, Maximum = max, Increment = inc, Owner = this };

    private SearchableSettingItem MakeCombo(string name, string desc, string category, string prop, ObservableCollection<string> choices) =>
        new() { Name = name, Description = desc, Category = category, PropertyName = prop, ControlKind = SettingControlKind.ComboBox, Choices = choices, Owner = this };

    // -- Reflection-free property accessors for SearchableSettingItem proxies --

    internal bool GetBoolSetting(string prop) => prop switch
    {
        nameof(ConvertTabsToSpaces) => ConvertTabsToSpaces,
        nameof(ShowLineNumbers) => ShowLineNumbers,
        nameof(HighlightCurrentLine) => HighlightCurrentLine,
        nameof(ShowWhitespace) => ShowWhitespace,
        nameof(WordWrap) => WordWrap,
        nameof(AutoIndent) => AutoIndent,
        nameof(BracketMatching) => BracketMatching,
        nameof(AutoCloseBrackets) => AutoCloseBrackets,
        nameof(EnableAutoComplete) => EnableAutoComplete,
        nameof(ShowQuickInfo) => ShowQuickInfo,
        nameof(ShowSignatureHelp) => ShowSignatureHelp,
        nameof(SaveBeforeBuild) => SaveBeforeBuild,
        nameof(ShowBuildOutput) => ShowBuildOutput,
        _ => false
    };

    internal void SetBoolSetting(string prop, bool value)
    {
        switch (prop)
        {
            case nameof(ConvertTabsToSpaces): ConvertTabsToSpaces = value; break;
            case nameof(ShowLineNumbers): ShowLineNumbers = value; break;
            case nameof(HighlightCurrentLine): HighlightCurrentLine = value; break;
            case nameof(ShowWhitespace): ShowWhitespace = value; break;
            case nameof(WordWrap): WordWrap = value; break;
            case nameof(AutoIndent): AutoIndent = value; break;
            case nameof(BracketMatching): BracketMatching = value; break;
            case nameof(AutoCloseBrackets): AutoCloseBrackets = value; break;
            case nameof(EnableAutoComplete): EnableAutoComplete = value; break;
            case nameof(ShowQuickInfo): ShowQuickInfo = value; break;
            case nameof(ShowSignatureHelp): ShowSignatureHelp = value; break;
            case nameof(SaveBeforeBuild): SaveBeforeBuild = value; break;
            case nameof(ShowBuildOutput): ShowBuildOutput = value; break;
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
        _ => ""
    };

    internal void SetStringSetting(string prop, string value)
    {
        switch (prop)
        {
            case nameof(FontFamily): FontFamily = value; break;
            case nameof(SelectedTheme): SelectedTheme = value; break;
            case nameof(DefaultConfiguration): DefaultConfiguration = value; break;
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
        SelectedTheme = "Dark";
        EnableAutoComplete = true;
        ShowQuickInfo = true;
        ShowSignatureHelp = true;
        AutoCompleteDelay = 200;
        SaveBeforeBuild = true;
        ShowBuildOutput = true;
        DefaultConfiguration = "Debug";

        foreach (var shortcut in Shortcuts)
        {
            shortcut.CurrentBinding = shortcut.DefaultBinding;
        }
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<SettingsData>(json);

                if (settings != null)
                {
                    FontFamily = settings.FontFamily ?? FontFamily;
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
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var settings = new SettingsData
            {
                FontFamily = FontFamily,
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
            File.WriteAllText(SettingsPath, json);
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
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
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
