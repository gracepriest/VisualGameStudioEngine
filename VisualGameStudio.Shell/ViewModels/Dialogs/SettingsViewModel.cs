using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

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

    public Action? CloseDialog { get; set; }
    public bool DialogResult { get; private set; }

    public static event EventHandler? SettingsChanged;

    public SettingsViewModel()
    {
        InitializeShortcuts();
        LoadSettings();
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
