using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public enum ProjectLanguage { BasicLang, Cpp }

/// <summary>A selectable backend. For BasicLang it maps to a real SolutionType;
/// for C++ it is a display-only toolchain over the single "cpp" SolutionType.</summary>
public sealed class BackendOption
{
    public string Name { get; init; } = "";
    public SolutionType SolutionType { get; init; } = SolutionTypes.DotNet;
    public string? ToolchainId { get; init; }   // null for BasicLang; "llvm"/"gcc"/"msvc" for C++
    public override string ToString() => Name;
}

/// <summary>Display wrapper so the language toggle can show "C++" for the Cpp enum.</summary>
public sealed class LanguageOption
{
    public string Display { get; init; } = "";
    public ProjectLanguage Value { get; init; }
    public override string ToString() => Display;
}

public partial class NewProjectWizardViewModel : ObservableObject
{
    private readonly IProjectTemplateService _templateService;

    // UI-only platform classification (spec): Windows shows all; Cross-platform
    // excludes these; All applies no filter. Not persisted.
    private static readonly HashSet<string> WindowsOnlyTemplateIds = new() { "winforms-app", "wpf-app" };

    // ----- Window 1 state -----
    [ObservableProperty] private ProjectLanguage _selectedLanguage = ProjectLanguage.BasicLang;
    [ObservableProperty] private ObservableCollection<BackendOption> _backends = new();
    [ObservableProperty] private BackendOption? _selectedBackend;
    [ObservableProperty] private ObservableCollection<string> _platforms = new() { "All", "Windows", "Cross-platform" };
    [ObservableProperty] private string _selectedPlatform = "All";
    [ObservableProperty] private ObservableCollection<string> _categories = new();
    [ObservableProperty] private string _selectedCategory = "All";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private ObservableCollection<ProjectTemplate> _visibleTemplates = new();
    [ObservableProperty] private ProjectTemplate? _selectedTemplate;

    public ObservableCollection<LanguageOption> Languages { get; } = new()
    {
        new LanguageOption { Display = "BasicLang", Value = ProjectLanguage.BasicLang },
        new LanguageOption { Display = "C++", Value = ProjectLanguage.Cpp },
    };
    [ObservableProperty] private LanguageOption? _selectedLanguageOption;

    // ----- Window 2 state -----
    [ObservableProperty] private string _projectName = "";
    [ObservableProperty] private string _location = "";
    [ObservableProperty] private string? _nameWarning;
    [ObservableProperty] private string _targetFramework = "net8.0";
    [ObservableProperty] private string _cppStandard = "c++20";
    [ObservableProperty] private string? _customNamespace;
    [ObservableProperty] private bool _createSolutionFolder = true;
    [ObservableProperty] private bool _createGitRepository = true;
    [ObservableProperty] private bool _isCreating;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _hasError;

    public ObservableCollection<string> TargetFrameworks { get; } =
        new() { "net8.0", "net7.0", "net6.0", "net5.0", "netcoreapp3.1" };
    public ObservableCollection<string> CppStandards { get; } =
        new() { "c++20", "c++17", "c++14" };

    // ----- Derived -----
    public bool ShowFrameworkSelector => SelectedBackend?.SolutionType.Id is "dotnet" or "msil";
    public bool ShowCppStandardSelector => SelectedLanguage == ProjectLanguage.Cpp;
    public bool CanGoNext => SelectedTemplate != null;
    public bool CanCreate => !string.IsNullOrWhiteSpace(ProjectName)
                             && !string.IsNullOrWhiteSpace(Location)
                             && SelectedBackend != null
                             && SelectedTemplate != null
                             && !IsCreating;

    /// <summary>Set by the view for a platform folder picker.</summary>
    public IAsyncRelayCommand? BrowseLocationCommand { get; set; }

    // ----- View-agnostic transition events -----
    public event EventHandler? NextRequested;
    public event EventHandler? BackRequested;
    public event EventHandler<ProjectCreationResult>? ProjectCreated;
    public event EventHandler? Cancelled;

    public NewProjectWizardViewModel(IProjectTemplateService templateService)
    {
        _templateService = templateService;
        Location = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BasicLangProjects");
        SelectedLanguageOption = Languages[0];
        LoadBackends();   // sets Backends + SelectedBackend -> cascades to categories + templates
    }

    private void LoadBackends()
    {
        Backends.Clear();
        if (SelectedLanguage == ProjectLanguage.BasicLang)
        {
            Backends.Add(new BackendOption { Name = "C# (.NET)",  SolutionType = SolutionTypes.DotNet });
            Backends.Add(new BackendOption { Name = "MSIL",        SolutionType = SolutionTypes.Msil });
            Backends.Add(new BackendOption { Name = "Native C++",  SolutionType = SolutionTypes.Native });
            Backends.Add(new BackendOption { Name = "LLVM",        SolutionType = SolutionTypes.Llvm });
        }
        else
        {
            Backends.Add(new BackendOption { Name = "LLVM (clang++)", SolutionType = SolutionTypes.Cpp, ToolchainId = "llvm" });
            Backends.Add(new BackendOption { Name = "GCC (g++)",      SolutionType = SolutionTypes.Cpp, ToolchainId = "gcc" });
            Backends.Add(new BackendOption { Name = "MSVC",          SolutionType = SolutionTypes.Cpp, ToolchainId = "msvc" });
        }
        SelectedBackend = Backends[0];
    }

    partial void OnSelectedLanguageOptionChanged(LanguageOption? value)
    {
        if (value != null) SelectedLanguage = value.Value;
    }

    partial void OnSelectedLanguageChanged(ProjectLanguage value)
    {
        LoadBackends();
        OnPropertyChanged(nameof(ShowCppStandardSelector));
        OnPropertyChanged(nameof(ShowFrameworkSelector));
    }

    partial void OnSelectedBackendChanged(BackendOption? value)
    {
        RebuildCategories();
        RefreshTemplates();
        OnPropertyChanged(nameof(ShowFrameworkSelector));
        OnPropertyChanged(nameof(CanCreate));
    }

    partial void OnSelectedPlatformChanged(string value) => RefreshTemplates();
    partial void OnSelectedCategoryChanged(string value) => RefreshTemplates();
    partial void OnSearchTextChanged(string value) => RefreshTemplates();

    private void RebuildCategories()
    {
        Categories.Clear();
        Categories.Add("All");
        if (SelectedBackend != null)
        {
            foreach (var cat in _templateService.GetProjectTemplates(SelectedBackend.SolutionType)
                         .Select(t => t.Category).Distinct().OrderBy(c => c))
                Categories.Add(cat);
        }
        SelectedCategory = "All";
    }

    private void RefreshTemplates()
    {
        VisibleTemplates.Clear();
        if (SelectedBackend == null) return;

        IEnumerable<ProjectTemplate> templates = _templateService.GetProjectTemplates(SelectedBackend.SolutionType);

        if (SelectedPlatform == "Cross-platform")
            templates = templates.Where(t => !WindowsOnlyTemplateIds.Contains(t.Id));

        if (!string.IsNullOrEmpty(SelectedCategory) && SelectedCategory != "All")
            templates = templates.Where(t => t.Category == SelectedCategory);

        if (!string.IsNullOrWhiteSpace(SearchText))
            templates = templates.Where(t =>
                t.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                t.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                t.Tags.Any(tag => tag.Contains(SearchText, StringComparison.OrdinalIgnoreCase)));

        foreach (var t in templates) VisibleTemplates.Add(t);

        if (SelectedTemplate == null || !VisibleTemplates.Contains(SelectedTemplate))
            SelectedTemplate = VisibleTemplates.FirstOrDefault();
    }
}
