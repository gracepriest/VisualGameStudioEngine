using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class ExceptionSettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<ExceptionCategoryViewModel> _exceptionCategories = new();

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private ExceptionCategoryViewModel? _selectedCategory;

    public bool DialogResult { get; private set; }
    public List<ExceptionSetting> ResultSettings { get; private set; } = new();

    public event EventHandler? SettingsApplied;
    public event EventHandler? Cancelled;

    public ExceptionSettingsViewModel()
    {
        InitializeDefaultCategories();
    }

    public ExceptionSettingsViewModel(IEnumerable<ExceptionSetting>? currentSettings) : this()
    {
        if (currentSettings != null)
        {
            ApplyCurrentSettings(currentSettings);
        }
    }

    private void InitializeDefaultCategories()
    {
        // BasicLang exception categories
        var allExceptions = new ExceptionCategoryViewModel("All Exceptions")
        {
            Description = "Break on all exceptions"
        };

        var runtimeExceptions = new ExceptionCategoryViewModel("Runtime Exceptions")
        {
            Description = "Exceptions thrown during program execution",
            Children = new ObservableCollection<ExceptionCategoryViewModel>
            {
                new("DivisionByZeroException") { Description = "Division by zero" },
                new("NullReferenceException") { Description = "Null reference access" },
                new("IndexOutOfRangeException") { Description = "Array index out of bounds" },
                new("OverflowException") { Description = "Arithmetic overflow" },
                new("InvalidCastException") { Description = "Invalid type cast" },
                new("ArgumentException") { Description = "Invalid argument" },
                new("ArgumentNullException") { Description = "Null argument" },
                new("ArgumentOutOfRangeException") { Description = "Argument out of range" },
                new("InvalidOperationException") { Description = "Invalid operation" },
                new("NotImplementedException") { Description = "Not implemented" },
                new("NotSupportedException") { Description = "Not supported" }
            }
        };

        var ioExceptions = new ExceptionCategoryViewModel("IO Exceptions")
        {
            Description = "File and stream exceptions",
            Children = new ObservableCollection<ExceptionCategoryViewModel>
            {
                new("FileNotFoundException") { Description = "File not found" },
                new("DirectoryNotFoundException") { Description = "Directory not found" },
                new("IOException") { Description = "General IO error" },
                new("UnauthorizedAccessException") { Description = "Access denied" }
            }
        };

        var customExceptions = new ExceptionCategoryViewModel("User Exceptions")
        {
            Description = "User-defined exceptions in BasicLang code"
        };

        ExceptionCategories.Add(allExceptions);
        ExceptionCategories.Add(runtimeExceptions);
        ExceptionCategories.Add(ioExceptions);
        ExceptionCategories.Add(customExceptions);
    }

    private void ApplyCurrentSettings(IEnumerable<ExceptionSetting> settings)
    {
        var settingsDict = settings.ToDictionary(s => s.ExceptionType, s => s);

        void ApplyToCategory(ExceptionCategoryViewModel category)
        {
            if (settingsDict.TryGetValue(category.Name, out var setting))
            {
                category.BreakWhenThrown = setting.BreakWhenThrown;
                category.BreakWhenUserUnhandled = setting.BreakWhenUserUnhandled;
            }

            if (category.Children != null)
            {
                foreach (var child in category.Children)
                {
                    ApplyToCategory(child);
                }
            }
        }

        foreach (var category in ExceptionCategories)
        {
            ApplyToCategory(category);
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        FilterCategories(value);
    }

    private void FilterCategories(string searchText)
    {
        void SetVisibility(ExceptionCategoryViewModel category, string search)
        {
            bool matches = string.IsNullOrWhiteSpace(search) ||
                           category.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                           (category.Description?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);

            bool childMatches = false;
            if (category.Children != null)
            {
                foreach (var child in category.Children)
                {
                    SetVisibility(child, search);
                    if (child.IsVisible) childMatches = true;
                }
            }

            category.IsVisible = matches || childMatches;
            category.IsExpanded = childMatches && !string.IsNullOrWhiteSpace(search);
        }

        foreach (var category in ExceptionCategories)
        {
            SetVisibility(category, searchText);
        }
    }

    [RelayCommand]
    private void Apply()
    {
        ResultSettings = CollectSettings();
        DialogResult = true;
        SettingsApplied?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void EnableAll()
    {
        SetAllBreakWhenThrown(true);
    }

    [RelayCommand]
    private void DisableAll()
    {
        SetAllBreakWhenThrown(false);
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        foreach (var category in ExceptionCategories)
        {
            ResetCategory(category);
        }
    }

    private void SetAllBreakWhenThrown(bool value)
    {
        void SetCategory(ExceptionCategoryViewModel category)
        {
            category.BreakWhenThrown = value;
            if (category.Children != null)
            {
                foreach (var child in category.Children)
                {
                    SetCategory(child);
                }
            }
        }

        foreach (var category in ExceptionCategories)
        {
            SetCategory(category);
        }
    }

    private void ResetCategory(ExceptionCategoryViewModel category)
    {
        category.BreakWhenThrown = false;
        category.BreakWhenUserUnhandled = true; // Default: break on user-unhandled

        if (category.Children != null)
        {
            foreach (var child in category.Children)
            {
                ResetCategory(child);
            }
        }
    }

    private List<ExceptionSetting> CollectSettings()
    {
        var settings = new List<ExceptionSetting>();

        void CollectFromCategory(ExceptionCategoryViewModel category)
        {
            settings.Add(new ExceptionSetting
            {
                ExceptionType = category.Name,
                BreakWhenThrown = category.BreakWhenThrown,
                BreakWhenUserUnhandled = category.BreakWhenUserUnhandled
            });

            if (category.Children != null)
            {
                foreach (var child in category.Children)
                {
                    CollectFromCategory(child);
                }
            }
        }

        foreach (var category in ExceptionCategories)
        {
            CollectFromCategory(category);
        }

        return settings;
    }
}

public partial class ExceptionCategoryViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    private bool _breakWhenThrown;

    [ObservableProperty]
    private bool _breakWhenUserUnhandled = true;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private ObservableCollection<ExceptionCategoryViewModel>? _children;

    public ExceptionCategoryViewModel(string name)
    {
        _name = name;
    }

    partial void OnBreakWhenThrownChanged(bool value)
    {
        // When "break when thrown" is enabled, propagate to children
        if (value && Children != null)
        {
            foreach (var child in Children)
            {
                child.BreakWhenThrown = true;
            }
        }
    }
}

public class ExceptionSetting
{
    public string ExceptionType { get; set; } = "";
    public bool BreakWhenThrown { get; set; }
    public bool BreakWhenUserUnhandled { get; set; } = true;
}
