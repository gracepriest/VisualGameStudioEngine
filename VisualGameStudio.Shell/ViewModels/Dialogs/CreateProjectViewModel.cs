using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

/// <summary>
/// ViewModel for the Create Project dialog.
/// </summary>
public partial class CreateProjectViewModel : ObservableObject
{
    private readonly IProjectTemplateService _templateService;

    [ObservableProperty]
    private ObservableCollection<SolutionType> _solutionTypes = new();

    [ObservableProperty]
    private SolutionType? _selectedSolutionType;

    [ObservableProperty]
    private ObservableCollection<ProjectTemplate> _projectTemplates = new();

    [ObservableProperty]
    private ProjectTemplate? _selectedTemplate;

    [ObservableProperty]
    private string _projectName = "";

    [ObservableProperty]
    private string _location = "";

    [ObservableProperty]
    private bool _createSolutionFolder = true;

    [ObservableProperty]
    private bool _createGitRepository = true;

    [ObservableProperty]
    private string _targetFramework = "net8.0";

    [ObservableProperty]
    private string? _customNamespace;

    [ObservableProperty]
    private bool _isCreating;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _searchText = "";

    public ObservableCollection<string> TargetFrameworks { get; } = new()
    {
        "net8.0",
        "net7.0",
        "net6.0",
        "net5.0",
        "netcoreapp3.1"
    };

    public bool CanCreate => !string.IsNullOrWhiteSpace(ProjectName) &&
                             !string.IsNullOrWhiteSpace(Location) &&
                             SelectedSolutionType != null &&
                             SelectedTemplate != null &&
                             !IsCreating;

    public bool ShowFrameworkSelector => SelectedSolutionType?.Id is "dotnet" or "msil";

    /// <summary>
    /// Command to browse for project location. Can be set by the view for platform-specific dialogs.
    /// </summary>
    public IAsyncRelayCommand? BrowseLocationCommand { get; set; }

    public event EventHandler<ProjectCreationResult>? ProjectCreated;
    public event EventHandler? Cancelled;

    public CreateProjectViewModel(IProjectTemplateService templateService)
    {
        _templateService = templateService;
        LoadSolutionTypes();
        SetDefaultLocation();
    }

    private void LoadSolutionTypes()
    {
        SolutionTypes.Clear();
        foreach (var type in _templateService.GetSolutionTypes())
        {
            SolutionTypes.Add(type);
        }

        if (SolutionTypes.Count > 0)
        {
            SelectedSolutionType = SolutionTypes[0];
        }
    }

    private void SetDefaultLocation()
    {
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        Location = Path.Combine(documentsPath, "BasicLangProjects");
    }

    partial void OnSelectedSolutionTypeChanged(SolutionType? value)
    {
        LoadProjectTemplates();
        OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(ShowFrameworkSelector));
    }

    partial void OnSelectedTemplateChanged(ProjectTemplate? value)
    {
        OnPropertyChanged(nameof(CanCreate));
    }

    partial void OnProjectNameChanged(string value)
    {
        OnPropertyChanged(nameof(CanCreate));
        ClearError();
    }

    partial void OnLocationChanged(string value)
    {
        OnPropertyChanged(nameof(CanCreate));
        ClearError();
    }

    partial void OnSearchTextChanged(string value)
    {
        FilterProjectTemplates();
    }

    private void LoadProjectTemplates()
    {
        ProjectTemplates.Clear();

        if (SelectedSolutionType == null) return;

        var templates = _templateService.GetProjectTemplates(SelectedSolutionType);
        foreach (var template in templates)
        {
            ProjectTemplates.Add(template);
        }

        if (ProjectTemplates.Count > 0)
        {
            SelectedTemplate = ProjectTemplates[0];
        }
    }

    private void FilterProjectTemplates()
    {
        if (SelectedSolutionType == null) return;

        var allTemplates = _templateService.GetProjectTemplates(SelectedSolutionType);
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? allTemplates
            : allTemplates.Where(t =>
                t.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                t.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                t.Tags.Any(tag => tag.Contains(SearchText, StringComparison.OrdinalIgnoreCase)));

        ProjectTemplates.Clear();
        foreach (var template in filtered)
        {
            ProjectTemplates.Add(template);
        }

        if (SelectedTemplate != null && !ProjectTemplates.Contains(SelectedTemplate))
        {
            SelectedTemplate = ProjectTemplates.FirstOrDefault();
        }
    }

    private void ClearError()
    {
        ErrorMessage = null;
        HasError = false;
    }

    [RelayCommand]
    private async Task CreateProjectAsync()
    {
        if (!CanCreate) return;

        ClearError();
        IsCreating = true;

        try
        {
            var options = new CreateProjectOptions
            {
                Name = ProjectName,
                Location = Location,
                SolutionType = SelectedSolutionType!,
                Template = SelectedTemplate!,
                CreateSolutionFolder = CreateSolutionFolder,
                CreateGitRepository = CreateGitRepository,
                TargetFramework = TargetFramework,
                Namespace = CustomNamespace
            };

            var result = await _templateService.CreateProjectAsync(options);

            if (result.Success)
            {
                ProjectCreated?.Invoke(this, result);
            }
            else
            {
                ErrorMessage = result.Error ?? "Failed to create project.";
                HasError = true;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            HasError = true;
        }
        finally
        {
            IsCreating = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    private void BrowseLocation()
    {
        // This is overridden by the view for platform-specific dialogs
    }

    [RelayCommand]
    private void SelectSolutionType(SolutionType solutionType)
    {
        SelectedSolutionType = solutionType;
    }

    [RelayCommand]
    private void SelectTemplate(ProjectTemplate template)
    {
        SelectedTemplate = template;
    }
}

/// <summary>
/// Design-time ViewModel for the Create Project dialog.
/// </summary>
public class CreateProjectDesignViewModel : CreateProjectViewModel
{
    public CreateProjectDesignViewModel() : base(new DesignProjectTemplateService())
    {
        ProjectName = "MyGame";
        SelectedSolutionType = SolutionTypes.FirstOrDefault();
        SelectedTemplate = ProjectTemplates.FirstOrDefault();
    }

    private class DesignProjectTemplateService : IProjectTemplateService
    {
        public IReadOnlyList<SolutionType> GetSolutionTypes() => Core.Abstractions.Services.SolutionTypes.All;
        public IReadOnlyList<ProjectTemplate> GetProjectTemplates(SolutionType solutionType) => Core.Abstractions.Services.ProjectTemplates.All.Where(t => t.SupportedSolutionTypes.Contains(solutionType.Id)).ToList();
        public IReadOnlyList<ProjectTemplate> GetAllProjectTemplates() => Core.Abstractions.Services.ProjectTemplates.All;
        public Task<ProjectCreationResult> CreateProjectAsync(CreateProjectOptions options, CancellationToken cancellationToken = default) => Task.FromResult(new ProjectCreationResult { Success = true });
        public Task<SolutionCreationResult> CreateSolutionAsync(CreateSolutionOptions options, CancellationToken cancellationToken = default) => Task.FromResult(new SolutionCreationResult { Success = true });
        public ProjectValidationResult ValidateProjectOptions(CreateProjectOptions options) => new() { IsValid = true };
        public void RegisterTemplate(ProjectTemplate template) { }
        public IReadOnlyList<ProjectTemplate> GetRecentTemplates() => new List<ProjectTemplate>();
    }
}
