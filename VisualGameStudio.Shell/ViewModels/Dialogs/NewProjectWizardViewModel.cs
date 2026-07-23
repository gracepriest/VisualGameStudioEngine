using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

// Deliberate UI-only enum, distinct from VisualGameStudio.Core.Models.ProjectLanguage
// (the persisted .blproj <Language> axis). The wizard's language toggle drives template
// filtering / backend presentation only and must NOT become the project-persistence axis
// this pass — do not "unify" these two enums, or the toggle would get wired into
// project persistence.
public enum ProjectLanguage { BasicLang, Cpp }

/// <summary>Which flow is driving this wizard instance. The select/configure pages
/// are shared: NewProject is today's standalone flow; NewSolution and AddToSolution
/// are seams for a future solution-creation wizard reusing the same pages.</summary>
public enum WizardMode { NewProject, NewSolution, AddToSolution }

/// <summary>A selectable backend. For BasicLang it maps to a real SolutionType;
/// for C++ it is a toolchain choice over the single "cpp" SolutionType that is
/// written to the created project (its <c>CppToolchain</c>) when the toolchain
/// is installed on this machine.</summary>
public sealed partial class BackendOption : ObservableObject
{
    public string Name { get; init; } = "";
    public SolutionType SolutionType { get; init; } = SolutionTypes.DotNet;
    public string? ToolchainId { get; init; }   // null for BasicLang; "llvm"/"gcc"/"msvc" for C++

    /// <summary>False when the availability probe found this toolchain missing.
    /// The backend ComboBox greys the item via its container's IsEnabled.</summary>
    [ObservableProperty] private bool _isEnabled = true;

    /// <summary>"(not installed)" when the toolchain is unavailable; null otherwise.</summary>
    [ObservableProperty] private string? _availabilityHint;

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
    private readonly ICppToolchainProbe _toolchainProbe;

    // Serializes probe-result application against backend-list rebuilds: in a
    // headless host (unit tests) the probe continuation runs on a pool thread,
    // where an unsynchronized Backends.Clear() during application could throw.
    // In the app both sides run on the UI thread and the lock is uncontended.
    private readonly object _toolchainSync = new();
    private ToolchainAvailability? _toolchainAvailability;

    // UI-only platform classification (spec): Windows shows all; Cross-platform
    // excludes these; All applies no filter. Not persisted.
    private static readonly HashSet<string> WindowsOnlyTemplateIds = new() { "winforms-app", "wpf-app" };

    // ----- Wizard mode (seam for a future New Solution / Add-to-Solution wizard
    // reusing these same select/configure pages) -----
    /// <summary>Which flow owns this wizard instance. Defaults to today's standalone
    /// behavior; NewSolution/AddToSolution are not yet driven by any caller.</summary>
    public WizardMode Mode { get; set; } = WizardMode.NewProject;

    /// <summary>True when the location field should be locked (read-only) because
    /// it is dictated by an enclosing solution rather than chosen here.</summary>
    public bool IsLocationLocked => Mode != WizardMode.NewProject;

    public bool IsNewSolutionMode => Mode == WizardMode.NewSolution;

    /// <summary>Create-button label: "Create solution" in New-Solution mode, else "Create".</summary>
    public string CreateButtonText => Mode == WizardMode.NewSolution ? "Create solution" : "Create";

    /// <summary>How CreateProjectAsync actually finishes. Defaults to the real
    /// template service in the constructor; a solution-mode caller can replace it
    /// to route through solution-aware creation instead.</summary>
    public Func<CreateProjectOptions, Task<ProjectCreationResult>> FinishAction { get; set; } = null!;

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

    /// <summary>True when the language is C++ and the probe found no toolchain
    /// installed at all. The options stay visible-but-disabled and creation is
    /// still allowed — the project just won't build until a toolchain exists.</summary>
    [ObservableProperty] private bool _noToolchainInstalled;

    /// <summary>The in-flight toolchain probe kicked off at wizard open (a
    /// completed task once its results are applied). Await it to observe
    /// probe-driven state (greying, auto-select, warning) deterministically.</summary>
    public Task ToolchainProbeTask { get; }

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

    public NewProjectWizardViewModel(IProjectTemplateService templateService, ICppToolchainProbe toolchainProbe)
    {
        _templateService = templateService;
        _toolchainProbe = toolchainProbe;
        FinishAction = opts => _templateService.CreateProjectAsync(opts);
        Location = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BasicLangProjects");
        SelectedLanguageOption = Languages[0];
        LoadBackends();   // sets Backends + SelectedBackend -> cascades to categories + templates

        // Probe at wizard open — the backend list lives on window 1, so waiting for
        // the configure page would be too late. The probe spawns processes (~1s,
        // worst-case seconds), so it must never run on the UI thread.
        ToolchainProbeTask = ProbeToolchainsAsync();
    }

    private async Task ProbeToolchainsAsync()
    {
        ToolchainAvailability availability;
        try
        {
            availability = await Task.Run(() => _toolchainProbe.Probe());
        }
        catch
        {
            // Probe failure: leave every option enabled rather than block the wizard.
            return;
        }
        // No ConfigureAwait(false): in the app this resumes on the UI thread via the
        // captured SynchronizationContext; headless tests resume on the pool, which
        // is exactly what _toolchainSync guards against.
        lock (_toolchainSync)
        {
            _toolchainAvailability = availability;
            ApplyToolchainAvailability();
        }
    }

    private void LoadBackends()
    {
        lock (_toolchainSync)
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

            // The probe may have completed before this rebuild (e.g. the user
            // switched language after open) — re-apply the stored result.
            ApplyToolchainAvailability();
        }
    }

    /// <summary>Greys out uninstalled toolchains, auto-selects the first available
    /// one when the current selection is unavailable, and raises the none-installed
    /// warning. No-op until the probe has produced a result. Callers hold _toolchainSync.</summary>
    private void ApplyToolchainAvailability()
    {
        var availability = _toolchainAvailability;
        if (availability == null) return;

        bool anyToolchainOptions = false;
        bool anyInstalled = false;
        foreach (var backend in Backends)
        {
            if (backend.ToolchainId == null) continue; // BasicLang backends are not probe-gated
            anyToolchainOptions = true;
            bool installed = backend.ToolchainId switch
            {
                "llvm" => availability.Llvm,
                "gcc"  => availability.Gcc,
                "msvc" => availability.Msvc,
                _      => true,
            };
            backend.IsEnabled = installed;
            backend.AvailabilityHint = installed ? null : "(not installed)";
            if (installed) anyInstalled = true;
        }

        NoToolchainInstalled = anyToolchainOptions && !anyInstalled;

        if (SelectedBackend is { IsEnabled: false } && anyInstalled)
            SelectedBackend = Backends.First(b => b.IsEnabled);

        // The selection can keep its identity while its availability changes
        // (none-installed keeps the disabled selection), so recompute here too.
        RecomputeCppStandards();
    }

    /// <summary>c++23 is offered only for an installed llvm/gcc selection (an
    /// explicit whitelist: MSVC's cl maxes at c++20 here, and a disabled or
    /// absent selection must not admit c++23 either). The collection instance
    /// stays stable for the view; the selection snaps back to c++20 when the
    /// current standard leaves the offer.</summary>
    private void RecomputeCppStandards()
    {
        bool offerCpp23 = SelectedBackend is { IsEnabled: true, ToolchainId: "llvm" or "gcc" };
        var desired = offerCpp23
            ? new[] { "c++23", "c++20", "c++17", "c++14" }
            : new[] { "c++20", "c++17", "c++14" };

        if (!CppStandards.SequenceEqual(desired))
        {
            // Capture BEFORE Clear(): window 2 is closed on Back but stays rooted
            // by the VM, so its live TwoWay SelectedItem binding pushes null into
            // CppStandard on the Reset. Re-assigning after the refill restores a
            // still-offered user pick (e.g. c++17 across llvm -> msvc) instead of
            // silently discarding it.
            var current = CppStandard;
            CppStandards.Clear();
            foreach (var s in desired) CppStandards.Add(s);
            CppStandard = desired.Contains(current) ? current : "c++20";
        }
        else if (!CppStandards.Contains(CppStandard))
        {
            // Unchanged offer but an out-of-list value (defensive): snap back.
            CppStandard = "c++20";
        }
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
        RecomputeCppStandards();
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

    partial void OnSelectedTemplateChanged(ProjectTemplate? value)
    {
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(CanCreate));
    }

    partial void OnProjectNameChanged(string value)
    {
        OnPropertyChanged(nameof(CanCreate));
        ClearError();
        // Same classifier the build layer escapes with, so the hint can't drift.
        var specials = BasicLang.Compiler.ProjectSystem.MSBuildText.FindSpecialCharacters(value);
        NameWarning = specials.Length == 0
            ? null
            : $"The name contains special characters ({string.Join(" ", specials.ToCharArray())}) — " +
              "it will build fine, but a simpler name is easier to work with.";
    }

    partial void OnLocationChanged(string value)
    {
        OnPropertyChanged(nameof(CanCreate));
        ClearError();
    }

    partial void OnIsCreatingChanged(bool value) => OnPropertyChanged(nameof(CanCreate));

    private void ClearError()
    {
        ErrorMessage = null;
        HasError = false;
    }

    [RelayCommand]
    private void GoNext()
    {
        if (CanGoNext) NextRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void GoBack()
    {
        // Clear any prior create error so returning to window 1 and coming back
        // doesn't re-show a stale error banner on the shared VM.
        ClearError();
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);

    /// <summary>Maps the wizard's current selections to the options the template
    /// service (or an injected FinishAction) creates from. Extracted so a future
    /// solution-mode flow can build the same options without duplicating the
    /// mapping, and so it is independently testable.</summary>
    public CreateProjectOptions BuildCreateOptions() => new()
    {
        Name = ProjectName,
        Location = Location,
        SolutionType = SelectedBackend!.SolutionType,
        Template = SelectedTemplate!,
        CreateSolutionFolder = CreateSolutionFolder,
        CreateGitRepository = CreateGitRepository,
        TargetFramework = TargetFramework,
        Namespace = CustomNamespace,
        // C++ projects carry the wizard's standard; the toolchain travels
        // only when the selected one is actually installed — never persist
        // an unavailable choice into the project.
        CppStandard = SelectedLanguage == ProjectLanguage.Cpp ? CppStandard : null,
        CppToolchain = SelectedLanguage == ProjectLanguage.Cpp && SelectedBackend is { IsEnabled: true }
            ? SelectedBackend.ToolchainId
            : null
    };

    [RelayCommand]
    private async Task CreateProjectAsync()
    {
        if (!CanCreate) return;
        ClearError();
        IsCreating = true;
        try
        {
            // Close the create-before-probe race: availability (backend IsEnabled,
            // auto-select) must reflect the probe before deciding whether the
            // toolchain travels. Never faults — probe exceptions are swallowed.
            await ToolchainProbeTask;

            var options = BuildCreateOptions();
            var result = await FinishAction(options);
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
}

/// <summary>Design-time VM so the two wizard views preview in the AXAML designer.</summary>
public class NewProjectWizardDesignViewModel : NewProjectWizardViewModel
{
    public NewProjectWizardDesignViewModel() : base(new DesignTemplateService(), new DesignToolchainProbe())
    {
        ProjectName = "MyProject";
    }

    /// <summary>Everything "installed" so the designer never greys the options.</summary>
    private sealed class DesignToolchainProbe : ICppToolchainProbe
    {
        public ToolchainAvailability Probe() => new(Llvm: true, Gcc: true, Msvc: true);
    }

    private sealed class DesignTemplateService : IProjectTemplateService
    {
        public IReadOnlyList<SolutionType> GetSolutionTypes() => SolutionTypes.All;
        public IReadOnlyList<ProjectTemplate> GetProjectTemplates(SolutionType s) =>
            ProjectTemplates.All.Where(t => t.SupportedSolutionTypes.Contains(s.Id)).ToList();
        public IReadOnlyList<ProjectTemplate> GetAllProjectTemplates() => ProjectTemplates.All;
        public Task<ProjectCreationResult> CreateProjectAsync(CreateProjectOptions o, CancellationToken c = default)
            => Task.FromResult(new ProjectCreationResult { Success = true });
        public Task<SolutionCreationResult> CreateSolutionAsync(CreateSolutionOptions o, CancellationToken c = default)
            => Task.FromResult(new SolutionCreationResult { Success = true });
        public ProjectValidationResult ValidateProjectOptions(CreateProjectOptions o) => new() { IsValid = true };
        public void RegisterTemplate(ProjectTemplate t) { }
        public IReadOnlyList<ProjectTemplate> GetRecentTemplates() => new List<ProjectTemplate>();
    }
}
