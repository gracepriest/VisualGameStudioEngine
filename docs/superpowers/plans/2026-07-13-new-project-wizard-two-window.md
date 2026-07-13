# New Project — two-window wizard Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the single-window New Project dialog with a two-window wizard (window 1 = pick language/backend/platform/type/template; window 2 = configure name/location/framework/options) that re-presents the *existing* project-creation service without changing any creation behavior.

**Architecture:** One `NewProjectWizardViewModel` holds all wizard state and is shared by both windows, so Back/Next never lose state. The VM raises view-agnostic events (`NextRequested`, `BackRequested`, `ProjectCreated`, `Cancelled`); the two windows' code-behind drive the actual modal transitions. The app's single New-Project entry point (`MainWindowViewModel.NewProjectAsync()`) is repointed at the new wizard; the old `CreateProjectView` stays in the tree unreferenced; the dead `NewProjectDialog`/`NewProjectViewModel` are deleted.

**Tech Stack:** C#, Avalonia (MVVM via CommunityToolkit.Mvvm `[ObservableProperty]`/`[RelayCommand]`), NUnit tests. Backing service `IProjectTemplateService` (in `VisualGameStudio.Core`).

**Spec:** `docs/superpowers/specs/2026-07-13-new-project-wizard-two-window-design.md`

---

## Conventions for the implementer (READ FIRST)

- **PowerShell is the primary shell.** Use the Read/Edit/Write/Grep/Glob tools for files — a hook blocks reflexive `grep`/`cat`/`find` through Bash.
- **Never round-trip repo files through PowerShell `Get-Content`/`Set-Content`** (corrupts BOM-less UTF-8). Use Edit/Write.
- **After AXAML changes, run `dotnet clean` before building** (stale cache crashes).
- **Kill any running `VisualGameStudio`/`BasicLang` LSP processes before a build** or the DLL copy fails:
  `Get-Process VisualGameStudio,BasicLang -ErrorAction SilentlyContinue | Stop-Process -Force`
- **Commit messages:** write the message to a scratchpad file and `git commit -F <file>` (embedded quotes get mangled otherwise). End every commit message with:
  `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`
- **`git add <explicit paths>` only** — never `git add -A` (untracked user files like `.superpowers/`, `test.bas` must be left alone).
- Test command (full): `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release`
- Test command (filtered): append `--filter "FullyQualifiedName~NewProjectWizard"`
- Build IDE: `dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release`

---

## File structure

**Create:**

| File | Responsibility |
|---|---|
| `VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectWizardViewModel.cs` | All wizard state + filtering + options-mapping + events. Also holds `BackendOption`, `LanguageOption`, `ProjectLanguage` enum, and a design-time VM + stub service (mirroring `CreateProjectViewModel`'s design VM). |
| `VisualGameStudio.Shell/Views/Dialogs/NewProjectSelectView.axaml` (+ `.axaml.cs`) | Window 1 (selection). Code-behind opens window 2 on `NextRequested` and returns the final result. |
| `VisualGameStudio.Shell/Views/Dialogs/NewProjectConfigureView.axaml` (+ `.axaml.cs`) | Window 2 (configuration). Code-behind wires the folder picker and maps VM events to an `Outcome` (Back/Cancelled/Created). |
| `VisualGameStudio.Tests/NewProjectWizardViewModelTests.cs` | Headless VM tests (backend mapping, filters, gating, options mapping). |
| `VisualGameStudio.Tests/NewProjectWizardSwapGuardTests.cs` | Source-scan guard: the app calls the new wizard, not `CreateProjectView`. |

**Modify:**

| File | Change |
|---|---|
| `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs` (`NewProjectAsync`, ~1705–1730) | Open `NewProjectSelectView` with a `NewProjectWizardViewModel` instead of `CreateProjectView`. Only call-site change. |

**Delete (dead code, unreferenced):**

- `VisualGameStudio.Shell/Views/Dialogs/NewProjectDialog.axaml`
- `VisualGameStudio.Shell/Views/Dialogs/NewProjectDialog.axaml.cs`
- `VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectViewModel.cs`

**Preserve (leave untouched, now unreferenced):**

- `VisualGameStudio.Shell/Views/Dialogs/CreateProjectView.axaml(.cs)`
- `VisualGameStudio.Shell/ViewModels/Dialogs/CreateProjectViewModel.cs`

---

## Task 0: Branch setup

- [ ] **Step 1: Create the feature branch**

Run:
```
git checkout -b new-project-wizard
```
Expected: `Switched to a new branch 'new-project-wizard'`. All work in this plan lands on this branch; merge to `master` is a separate, user-authorized step at the end.

---

## Task 1: Wizard VM — construction + language→backend mapping (TDD)

**Files:**
- Create: `VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectWizardViewModel.cs`
- Test: `VisualGameStudio.Tests/NewProjectWizardViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `VisualGameStudio.Tests/NewProjectWizardViewModelTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Tests;

[TestFixture]
public class NewProjectWizardViewModelTests
{
    // Fake service that mirrors the real filtering: templates are returned for a
    // solution type when SupportedSolutionTypes contains its id. CreateProjectAsync
    // captures the options so tests can assert the state -> options mapping.
    private sealed class FakeTemplateService : IProjectTemplateService
    {
        public CreateProjectOptions? LastOptions { get; private set; }
        public IReadOnlyList<SolutionType> GetSolutionTypes() => SolutionTypes.All;
        public IReadOnlyList<ProjectTemplate> GetProjectTemplates(SolutionType solutionType) =>
            ProjectTemplates.All.Where(t => t.SupportedSolutionTypes.Contains(solutionType.Id)).ToList();
        public IReadOnlyList<ProjectTemplate> GetAllProjectTemplates() => ProjectTemplates.All;
        public Task<ProjectCreationResult> CreateProjectAsync(CreateProjectOptions options, CancellationToken ct = default)
        {
            LastOptions = options;
            return Task.FromResult(new ProjectCreationResult { Success = true, ProjectPath = "X:/proj/proj.blproj" });
        }
        public Task<SolutionCreationResult> CreateSolutionAsync(CreateSolutionOptions options, CancellationToken ct = default)
            => Task.FromResult(new SolutionCreationResult { Success = true });
        public ProjectValidationResult ValidateProjectOptions(CreateProjectOptions options) => new() { IsValid = true };
        public void RegisterTemplate(ProjectTemplate template) { }
        public IReadOnlyList<ProjectTemplate> GetRecentTemplates() => new List<ProjectTemplate>();
    }

    private static NewProjectWizardViewModel NewVm(out FakeTemplateService svc)
    {
        svc = new FakeTemplateService();
        return new NewProjectWizardViewModel(svc);
    }

    [Test]
    public void BasicLang_Backends_MapToSolutionTypes()
    {
        var vm = NewVm(out _);
        vm.SelectedLanguage = ProjectLanguage.BasicLang;

        var ids = vm.Backends.Select(b => b.SolutionType.Id).ToList();
        Assert.That(ids, Is.EqualTo(new[] { "dotnet", "msil", "native", "llvm" }));
        Assert.That(vm.Backends.All(b => b.ToolchainId == null), Is.True);
    }

    [Test]
    public void Cpp_Backends_AreToolchains_OverCppSolutionType()
    {
        var vm = NewVm(out _);
        vm.SelectedLanguage = ProjectLanguage.Cpp;

        Assert.That(vm.Backends.Select(b => b.Name).ToList(),
            Is.EqualTo(new[] { "LLVM (clang++)", "GCC (g++)", "MSVC" }));
        Assert.That(vm.Backends.All(b => b.SolutionType.Id == "cpp"), Is.True);
        Assert.That(vm.Backends.Select(b => b.ToolchainId).ToList(),
            Is.EqualTo(new[] { "llvm", "gcc", "msvc" }));
    }

    [Test]
    public void SwitchingLanguage_ReselectsFirstBackend_AndReloadsTemplates()
    {
        var vm = NewVm(out _);
        vm.SelectedLanguage = ProjectLanguage.Cpp;

        Assert.That(vm.SelectedBackend!.SolutionType.Id, Is.EqualTo("cpp"));
        // cpp templates only: cpp-console-app, cpp-library, cpp-game-app
        Assert.That(vm.VisibleTemplates.Select(t => t.Id),
            Is.EquivalentTo(new[] { "cpp-console-app", "cpp-library", "cpp-game-app" }));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~NewProjectWizard"`
Expected: FAIL — `NewProjectWizardViewModel` / `ProjectLanguage` do not exist (compile error).

- [ ] **Step 3: Create the ViewModel (enough to construct + backend mapping + template load)**

Create `VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectWizardViewModel.cs`:

```csharp
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
```

- [ ] **Step 4: Run to verify Task 1 tests pass**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~NewProjectWizard"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

Write commit message to scratchpad, then:
```
git add VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectWizardViewModel.cs VisualGameStudio.Tests/NewProjectWizardViewModelTests.cs
git commit -F <scratchpad-msg>
```
Message: `feat: New Project wizard VM — language/backend mapping + template load (task 1)`

---

## Task 2: Wizard VM — platform / category / search filters compose (TDD)

**Files:**
- Modify: `VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectWizardViewModel.cs` (add filter change-handlers)
- Test: `VisualGameStudio.Tests/NewProjectWizardViewModelTests.cs` (add tests)

- [ ] **Step 1: Add failing tests**

Append to the test fixture:

```csharp
[Test]
public void PlatformFilter_CrossPlatform_ExcludesWinFormsAndWpf()
{
    var vm = NewVm(out _);
    vm.SelectedLanguage = ProjectLanguage.BasicLang;
    vm.SelectedBackend = vm.Backends.First(b => b.SolutionType.Id == "dotnet");

    var withAll = vm.VisibleTemplates.Select(t => t.Id).ToList();
    Assert.That(withAll, Does.Contain("winforms-app"));

    vm.SelectedPlatform = "Cross-platform";
    var ids = vm.VisibleTemplates.Select(t => t.Id).ToList();
    Assert.That(ids, Does.Not.Contain("winforms-app"));
    Assert.That(ids, Does.Not.Contain("wpf-app"));
    Assert.That(ids, Does.Contain("avalonia-app"));
}

[Test]
public void CategoryFilter_NarrowsToOneCategory()
{
    var vm = NewVm(out _);
    vm.SelectedBackend = vm.Backends.First(b => b.SolutionType.Id == "dotnet");

    vm.SelectedCategory = "Library";
    Assert.That(vm.VisibleTemplates.All(t => t.Category == "Library"), Is.True);
    Assert.That(vm.VisibleTemplates.Select(t => t.Id), Does.Contain("class-library"));
}

[Test]
public void Search_MatchesNameDescriptionOrTags()
{
    var vm = NewVm(out _);
    vm.SelectedBackend = vm.Backends.First(b => b.SolutionType.Id == "dotnet");

    vm.SearchText = "winforms"; // matches a tag
    Assert.That(vm.VisibleTemplates.Select(t => t.Id), Is.EqualTo(new[] { "winforms-app" }));
}

[Test]
public void Filters_Compose_CategoryPlusSearch()
{
    var vm = NewVm(out _);
    vm.SelectedBackend = vm.Backends.First(b => b.SolutionType.Id == "dotnet");

    vm.SelectedCategory = "Desktop";
    vm.SearchText = "avalonia";
    Assert.That(vm.VisibleTemplates.Select(t => t.Id), Is.EqualTo(new[] { "avalonia-app" }));
}

[Test]
public void SwitchingBackend_ResetsCategoryToAll_AndRebuildsCategoryList()
{
    var vm = NewVm(out _);
    vm.SelectedBackend = vm.Backends.First(b => b.SolutionType.Id == "dotnet");
    vm.SelectedCategory = "Web";

    vm.SelectedLanguage = ProjectLanguage.Cpp; // reselects cpp backend
    Assert.That(vm.SelectedCategory, Is.EqualTo("All"));
    Assert.That(vm.Categories, Does.Not.Contain("Web")); // cpp has no Web template
}
```

- [ ] **Step 2: Run to verify they fail**

Run the filtered test command.
Expected: FAIL — the filter change-handlers (`OnSelectedPlatformChanged`, etc.) do not yet re-run `RefreshTemplates`, so at least the platform/category/search tests fail.

- [ ] **Step 3: Add the change-handlers**

Add to `NewProjectWizardViewModel` (below `OnSelectedBackendChanged`):

```csharp
partial void OnSelectedPlatformChanged(string value) => RefreshTemplates();
partial void OnSelectedCategoryChanged(string value) => RefreshTemplates();
partial void OnSearchTextChanged(string value) => RefreshTemplates();
```

- [ ] **Step 4: Run to verify pass**

Run the filtered test command.
Expected: PASS (all Task 1 + Task 2 tests).

- [ ] **Step 5: Commit**

```
git add VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectWizardViewModel.cs VisualGameStudio.Tests/NewProjectWizardViewModelTests.cs
git commit -F <scratchpad-msg>
```
Message: `feat: New Project wizard VM — composable platform/category/search filters (task 2)`

---

## Task 3: Wizard VM — navigation gating, name warning, options mapping, events (TDD)

**Files:**
- Modify: `VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectWizardViewModel.cs`
- Test: `VisualGameStudio.Tests/NewProjectWizardViewModelTests.cs`

- [ ] **Step 1: Add failing tests**

Append:

```csharp
[Test]
public void CanGoNext_RequiresSelectedTemplate()
{
    var vm = NewVm(out _);
    Assert.That(vm.CanGoNext, Is.True); // a template auto-selected on load
    vm.SelectedTemplate = null;
    Assert.That(vm.CanGoNext, Is.False);
}

[Test]
public void CanCreate_RequiresNameAndLocation()
{
    var vm = NewVm(out _);
    vm.ProjectName = "";
    Assert.That(vm.CanCreate, Is.False);
    vm.ProjectName = "Demo";
    Assert.That(vm.CanCreate, Is.True); // Location defaulted in ctor
    vm.Location = "";
    Assert.That(vm.CanCreate, Is.False);
}

[Test]
public void GoNext_RaisesNextRequested_OnlyWhenAllowed()
{
    var vm = NewVm(out _);
    int fired = 0;
    vm.NextRequested += (_, _) => fired++;

    vm.SelectedTemplate = null;
    vm.GoNextCommand.Execute(null);
    Assert.That(fired, Is.EqualTo(0));

    // Re-select a template directly. (Do NOT re-assign the already-selected
    // backend: CommunityToolkit's [ObservableProperty] setter guards on
    // equality, and Backends[0] is reference-equal to the current SelectedBackend,
    // so that assignment is a no-op and would not repopulate SelectedTemplate.)
    vm.SelectedTemplate = vm.VisibleTemplates.First();
    vm.GoNextCommand.Execute(null);
    Assert.That(fired, Is.EqualTo(1));
}

[Test]
public async Task CreateProject_MapsState_ToOptions_AndFiresProjectCreated()
{
    var vm = NewVm(out var svc);
    vm.SelectedLanguage = ProjectLanguage.BasicLang;
    vm.SelectedBackend = vm.Backends.First(b => b.SolutionType.Id == "dotnet");
    vm.ProjectName = "Demo";
    vm.Location = "X:/here";
    vm.TargetFramework = "net7.0";
    vm.CustomNamespace = "Acme.Demo";

    ProjectCreationResult? got = null;
    vm.ProjectCreated += (_, r) => got = r;

    await vm.CreateProjectCommand.ExecuteAsync(null);

    Assert.That(svc.LastOptions, Is.Not.Null);
    Assert.That(svc.LastOptions!.Name, Is.EqualTo("Demo"));
    Assert.That(svc.LastOptions.SolutionType.Id, Is.EqualTo("dotnet"));
    Assert.That(svc.LastOptions.TargetFramework, Is.EqualTo("net7.0"));
    Assert.That(svc.LastOptions.Namespace, Is.EqualTo("Acme.Demo"));
    Assert.That(got, Is.Not.Null.And.Property("Success").True);
}

[Test]
public async Task CreateProject_Cpp_UsesCppSolutionType_AndIgnoresToolchain()
{
    var vm = NewVm(out var svc);
    vm.SelectedLanguage = ProjectLanguage.Cpp;
    vm.SelectedBackend = vm.Backends.First(b => b.ToolchainId == "gcc"); // display-only
    vm.ProjectName = "NativeDemo";
    vm.Location = "X:/here";

    await vm.CreateProjectCommand.ExecuteAsync(null);

    Assert.That(svc.LastOptions!.SolutionType.Id, Is.EqualTo("cpp"));
    // CreateProjectOptions has no toolchain field — the gcc choice cannot leak.
}

[Test]
public void NameWarning_SetForSpecialCharacters_NullWhenClean()
{
    var vm = NewVm(out _);
    vm.ProjectName = "Clean";
    Assert.That(vm.NameWarning, Is.Null);
    vm.ProjectName = "Bad;Name";
    Assert.That(vm.NameWarning, Is.Not.Null);
}

[Test]
public void ShowSelectors_TrackLanguageAndBackend()
{
    var vm = NewVm(out _);
    vm.SelectedBackend = vm.Backends.First(b => b.SolutionType.Id == "dotnet");
    Assert.That(vm.ShowFrameworkSelector, Is.True);
    Assert.That(vm.ShowCppStandardSelector, Is.False);

    vm.SelectedLanguage = ProjectLanguage.Cpp;
    Assert.That(vm.ShowFrameworkSelector, Is.False);
    Assert.That(vm.ShowCppStandardSelector, Is.True);
}
```

- [ ] **Step 2: Run to verify they fail**

Run the filtered test command.
Expected: FAIL — commands (`GoNextCommand`, `CreateProjectCommand`), `NameWarning` wiring, and `CanCreate` notifications don't exist yet.

- [ ] **Step 3: Add gating notifications, name warning, commands, and creation**

Add to `NewProjectWizardViewModel`:

```csharp
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
private void GoBack() => BackRequested?.Invoke(this, EventArgs.Empty);

[RelayCommand]
private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);

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
            SolutionType = SelectedBackend!.SolutionType,
            Template = SelectedTemplate!,
            CreateSolutionFolder = CreateSolutionFolder,
            CreateGitRepository = CreateGitRepository,
            TargetFramework = TargetFramework,
            Namespace = CustomNamespace
            // NOTE (spec): CppStandard + toolchain are display-only this pass —
            // there is no field on CreateProjectOptions to carry them.
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
```

Confirm `VisualGameStudio.Shell.csproj` already references `BasicLang` (it does — `CreateProjectViewModel` uses `MSBuildText`). No new reference needed.

- [ ] **Step 4: Run to verify pass**

Run the filtered test command.
Expected: PASS (all wizard VM tests).

- [ ] **Step 5: Add the design-time VM (so AXAML `Design.DataContext` works) — no test, compile only**

Append to `NewProjectWizardViewModel.cs`:

```csharp
/// <summary>Design-time VM so the two wizard views preview in the AXAML designer.</summary>
public class NewProjectWizardDesignViewModel : NewProjectWizardViewModel
{
    public NewProjectWizardDesignViewModel() : base(new DesignTemplateService())
    {
        ProjectName = "MyProject";
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
```

Add `using System.Threading;` to the file if not already present (for `CancellationToken`).

- [ ] **Step 6: Build to verify the VM compiles**

Run: `dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release`
Expected: Build succeeded.

- [ ] **Step 7: Commit**

```
git add VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectWizardViewModel.cs VisualGameStudio.Tests/NewProjectWizardViewModelTests.cs
git commit -F <scratchpad-msg>
```
Message: `feat: New Project wizard VM — gating, name warning, options mapping, events + design VM (task 3)`

---

## Task 4: Window 1 — NewProjectSelectView (selection)

**Files:**
- Create: `VisualGameStudio.Shell/Views/Dialogs/NewProjectSelectView.axaml`
- Create: `VisualGameStudio.Shell/Views/Dialogs/NewProjectSelectView.axaml.cs`

No unit test (Avalonia view); verified by build + Task 7 smoke.

- [ ] **Step 1: Create the AXAML**

Create `VisualGameStudio.Shell/Views/Dialogs/NewProjectSelectView.axaml`. Mirror `CreateProjectView.axaml`'s theming (`SystemControl*` DynamicResource brushes) so it inherits the current theme system:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        xmlns:vm="using:VisualGameStudio.Shell.ViewModels.Dialogs"
        xmlns:services="using:VisualGameStudio.Core.Abstractions.Services"
        mc:Ignorable="d" d:DesignWidth="760" d:DesignHeight="560"
        x:Class="VisualGameStudio.Shell.Views.Dialogs.NewProjectSelectView"
        x:DataType="vm:NewProjectWizardViewModel"
        Title="New project — select a project type"
        Width="760" Height="580" MinHeight="480"
        WindowStartupLocation="CenterOwner"
        CanResize="True" ShowInTaskbar="False">

  <Design.DataContext>
    <vm:NewProjectWizardDesignViewModel/>
  </Design.DataContext>

  <Grid RowDefinitions="Auto,*,Auto">
    <Border Grid.Row="0" Background="{DynamicResource SystemControlBackgroundChromeMediumBrush}" Padding="20,16">
      <StackPanel>
        <TextBlock Text="Select a project type" FontSize="22" FontWeight="SemiBold"/>
        <TextBlock Text="Choose a language, backend, and template to get started"
                   Foreground="{DynamicResource SystemControlForegroundBaseMediumBrush}"/>
      </StackPanel>
    </Border>

    <Grid Grid.Row="1" ColumnDefinitions="300,*">
      <!-- Left: filters -->
      <Border BorderBrush="{DynamicResource SystemControlForegroundBaseLowBrush}" BorderThickness="0,0,1,0">
        <StackPanel Margin="16" Spacing="12">
          <StackPanel>
            <TextBlock Text="Language" FontWeight="SemiBold" Margin="0,0,0,4"/>
            <ComboBox ItemsSource="{Binding Languages}"
                      SelectedItem="{Binding SelectedLanguageOption}"
                      HorizontalAlignment="Stretch"/>
          </StackPanel>
          <StackPanel>
            <TextBlock Text="Backend" FontWeight="SemiBold" Margin="0,0,0,4"/>
            <ComboBox ItemsSource="{Binding Backends}"
                      SelectedItem="{Binding SelectedBackend}"
                      HorizontalAlignment="Stretch"/>
            <TextBlock Text="{Binding SelectedBackend.SolutionType.Description}"
                       TextWrapping="Wrap" FontSize="12"
                       Foreground="{DynamicResource SystemControlForegroundBaseMediumBrush}"
                       Margin="0,6,0,0"/>
          </StackPanel>
          <StackPanel>
            <TextBlock Text="Platform" FontWeight="SemiBold" Margin="0,0,0,4"/>
            <ComboBox ItemsSource="{Binding Platforms}"
                      SelectedItem="{Binding SelectedPlatform}"
                      HorizontalAlignment="Stretch"/>
          </StackPanel>
          <StackPanel>
            <TextBlock Text="Project type" FontWeight="SemiBold" Margin="0,0,0,4"/>
            <ComboBox ItemsSource="{Binding Categories}"
                      SelectedItem="{Binding SelectedCategory}"
                      HorizontalAlignment="Stretch"/>
          </StackPanel>
        </StackPanel>
      </Border>

      <!-- Right: search + template list -->
      <Grid Grid.Column="1" RowDefinitions="Auto,*" Margin="16">
        <TextBox Grid.Row="0" Watermark="Search templates..." Text="{Binding SearchText}" Margin="0,0,0,8"/>
        <ListBox Grid.Row="1"
                 ItemsSource="{Binding VisibleTemplates}"
                 SelectedItem="{Binding SelectedTemplate}">
          <ListBox.ItemTemplate>
            <DataTemplate x:DataType="services:ProjectTemplate">
              <StackPanel Margin="4">
                <TextBlock Text="{Binding Name}" FontWeight="SemiBold"/>
                <TextBlock Text="{Binding ShortDescription}" FontSize="12"
                           Foreground="{DynamicResource SystemControlForegroundBaseMediumBrush}"
                           TextTrimming="CharacterEllipsis"/>
              </StackPanel>
            </DataTemplate>
          </ListBox.ItemTemplate>
        </ListBox>
      </Grid>
    </Grid>

    <Border Grid.Row="2" Background="{DynamicResource SystemControlBackgroundChromeMediumBrush}" Padding="20,12">
      <Grid ColumnDefinitions="*,Auto,Auto">
        <Button Grid.Column="1" Content="Cancel" Command="{Binding CancelCommand}" MinWidth="80" Margin="0,0,8,0"/>
        <Button Grid.Column="2" Content="Next" Command="{Binding GoNextCommand}"
                IsEnabled="{Binding CanGoNext}" Classes="accent" MinWidth="80"/>
      </Grid>
    </Border>
  </Grid>
</Window>
```

- [ ] **Step 2: Create the code-behind**

Create `VisualGameStudio.Shell/Views/Dialogs/NewProjectSelectView.axaml.cs`:

```csharp
using System;
using Avalonia.Controls;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class NewProjectSelectView : Window
{
    private readonly NewProjectWizardViewModel? _vm;
    private bool _configureOpen;
    private ProjectCreationResult? _result;

    public NewProjectSelectView()
    {
        InitializeComponent();
    }

    public NewProjectSelectView(NewProjectWizardViewModel vm) : this()
    {
        _vm = vm;
        DataContext = vm;
        vm.NextRequested += OnNextRequested;
        vm.Cancelled += OnCancelled;
    }

    private async void OnNextRequested(object? sender, EventArgs e)
    {
        if (_vm == null) return;

        _configureOpen = true;
        var configure = new NewProjectConfigureView(_vm);
        await configure.ShowDialog(this);
        _configureOpen = false;

        switch (configure.Outcome)
        {
            case NewProjectConfigureView.WizardOutcome.Created:
                _result = configure.Result;
                Close(_result);
                break;
            case NewProjectConfigureView.WizardOutcome.Cancelled:
                _result = null;
                Close(_result);
                break;
            case NewProjectConfigureView.WizardOutcome.Back:
                // stay on this window with state preserved
                break;
        }
    }

    private void OnCancelled(object? sender, EventArgs e)
    {
        // While window 2 is open it owns cancel; ignore the echo here.
        if (_configureOpen) return;
        _result = null;
        Close(_result);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_vm != null)
        {
            _vm.NextRequested -= OnNextRequested;
            _vm.Cancelled -= OnCancelled;
        }
        base.OnClosed(e);
    }
}
```

- [ ] **Step 3: Build (will fail until Task 5 provides NewProjectConfigureView)**

Note: this references `NewProjectConfigureView`, created next. Do NOT build in isolation; build after Task 5. Commit the two files now so the pair is atomic across Task 4+5, OR defer the commit to the end of Task 5. Choose: **defer commit to end of Task 5.**

---

## Task 5: Window 2 — NewProjectConfigureView (configuration) + build the pair

**Files:**
- Create: `VisualGameStudio.Shell/Views/Dialogs/NewProjectConfigureView.axaml`
- Create: `VisualGameStudio.Shell/Views/Dialogs/NewProjectConfigureView.axaml.cs`

- [ ] **Step 1: Create the AXAML**

Create `VisualGameStudio.Shell/Views/Dialogs/NewProjectConfigureView.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        xmlns:vm="using:VisualGameStudio.Shell.ViewModels.Dialogs"
        mc:Ignorable="d" d:DesignWidth="620" d:DesignHeight="620"
        x:Class="VisualGameStudio.Shell.Views.Dialogs.NewProjectConfigureView"
        x:DataType="vm:NewProjectWizardViewModel"
        Title="New project — configure"
        Width="620" Height="640" MinHeight="520"
        WindowStartupLocation="CenterOwner"
        CanResize="True" ShowInTaskbar="False">

  <Design.DataContext>
    <vm:NewProjectWizardDesignViewModel/>
  </Design.DataContext>

  <Grid RowDefinitions="Auto,*,Auto">
    <Border Grid.Row="0" Background="{DynamicResource SystemControlBackgroundChromeMediumBrush}" Padding="20,16">
      <TextBlock Text="Configure your project" FontSize="22" FontWeight="SemiBold"/>
    </Border>

    <ScrollViewer Grid.Row="1" Padding="20" VerticalScrollBarVisibility="Auto">
      <StackPanel Spacing="16">
        <!-- Read-only template description (like the old form) -->
        <Border Background="{DynamicResource SystemControlBackgroundChromeMediumBrush}"
                CornerRadius="4" Padding="16"
                IsVisible="{Binding SelectedTemplate, Converter={x:Static ObjectConverters.IsNotNull}}">
          <StackPanel>
            <TextBlock Text="{Binding SelectedTemplate.Name}" FontSize="16" FontWeight="SemiBold"/>
            <TextBlock Text="{Binding SelectedTemplate.Description}" TextWrapping="Wrap"
                       Foreground="{DynamicResource SystemControlForegroundBaseMediumBrush}" Margin="0,6,0,0"/>
            <ItemsControl ItemsSource="{Binding SelectedTemplate.Tags}" Margin="0,10,0,0">
              <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate><WrapPanel/></ItemsPanelTemplate>
              </ItemsControl.ItemsPanel>
              <ItemsControl.ItemTemplate>
                <DataTemplate>
                  <Border Background="{DynamicResource SystemControlBackgroundBaseLowBrush}"
                          CornerRadius="2" Padding="6,2" Margin="0,0,4,4">
                    <TextBlock Text="{Binding}" FontSize="11"/>
                  </Border>
                </DataTemplate>
              </ItemsControl.ItemTemplate>
            </ItemsControl>
          </StackPanel>
        </Border>

        <!-- Project name -->
        <StackPanel>
          <TextBlock Text="Project name" FontWeight="SemiBold" Margin="0,0,0,4"/>
          <TextBox Text="{Binding ProjectName}" Watermark="Enter project name"/>
          <Border Background="#33806000" CornerRadius="4" Padding="8" Margin="0,4,0,0"
                  IsVisible="{Binding NameWarning, Converter={x:Static StringConverters.IsNotNullOrEmpty}}">
            <TextBlock Text="{Binding NameWarning}" Foreground="#E5C07B" FontSize="11" TextWrapping="Wrap"/>
          </Border>
        </StackPanel>

        <!-- Location -->
        <StackPanel>
          <TextBlock Text="Location" FontWeight="SemiBold" Margin="0,0,0,4"/>
          <Grid ColumnDefinitions="*,Auto">
            <TextBox Grid.Column="0" Text="{Binding Location}" Watermark="Select project location"/>
            <Button Grid.Column="1" Content="Browse..." Command="{Binding BrowseLocationCommand}" Margin="8,0,0,0"/>
          </Grid>
        </StackPanel>

        <!-- Target framework (.NET/MSIL) -->
        <StackPanel IsVisible="{Binding ShowFrameworkSelector}">
          <TextBlock Text="Target framework" FontWeight="SemiBold" Margin="0,0,0,4"/>
          <ComboBox ItemsSource="{Binding TargetFrameworks}" SelectedItem="{Binding TargetFramework}"
                    HorizontalAlignment="Stretch"/>
        </StackPanel>

        <!-- C++ standard (pure C++) -->
        <StackPanel IsVisible="{Binding ShowCppStandardSelector}">
          <TextBlock Text="C++ standard" FontWeight="SemiBold" Margin="0,0,0,4"/>
          <ComboBox ItemsSource="{Binding CppStandards}" SelectedItem="{Binding CppStandard}"
                    HorizontalAlignment="Stretch"/>
        </StackPanel>

        <!-- Options -->
        <StackPanel Spacing="8">
          <TextBlock Text="Options" FontWeight="SemiBold"/>
          <CheckBox Content="Create solution folder" IsChecked="{Binding CreateSolutionFolder}"/>
          <CheckBox Content="Initialize Git repository" IsChecked="{Binding CreateGitRepository}"/>
        </StackPanel>

        <!-- Advanced -->
        <Border Background="{DynamicResource SystemControlBackgroundChromeMediumBrush}" CornerRadius="4" Padding="12">
          <Expander Header="Advanced options" IsExpanded="False">
            <StackPanel Margin="0,12,0,0">
              <TextBlock Text="Custom namespace (optional)" FontWeight="SemiBold" Margin="0,0,0,4"/>
              <TextBox Text="{Binding CustomNamespace}" Watermark="Leave empty to use project name"/>
            </StackPanel>
          </Expander>
        </Border>

        <!-- Error -->
        <Border Background="#44FF0000" CornerRadius="4" Padding="12" IsVisible="{Binding HasError}">
          <TextBlock Text="{Binding ErrorMessage}" Foreground="White" TextWrapping="Wrap"/>
        </Border>
      </StackPanel>
    </ScrollViewer>

    <Border Grid.Row="2" Background="{DynamicResource SystemControlBackgroundChromeMediumBrush}" Padding="20,12">
      <Grid ColumnDefinitions="Auto,*,Auto,Auto">
        <Button Grid.Column="0" Content="Back" Command="{Binding GoBackCommand}" MinWidth="80"/>
        <TextBlock Grid.Column="1" Text="Creating project..." IsVisible="{Binding IsCreating}"
                   FontStyle="Italic" VerticalAlignment="Center" Margin="12,0,0,0"
                   Foreground="{DynamicResource SystemControlForegroundBaseMediumBrush}"/>
        <Button Grid.Column="2" Content="Cancel" Command="{Binding CancelCommand}" MinWidth="80" Margin="0,0,8,0"/>
        <Button Grid.Column="3" Content="Create" Command="{Binding CreateProjectCommand}"
                IsEnabled="{Binding CanCreate}" Classes="accent" MinWidth="80"/>
      </Grid>
    </Border>
  </Grid>
</Window>
```

- [ ] **Step 2: Create the code-behind**

Create `VisualGameStudio.Shell/Views/Dialogs/NewProjectConfigureView.axaml.cs`:

```csharp
using System;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class NewProjectConfigureView : Window
{
    public enum WizardOutcome { Back, Cancelled, Created }

    private readonly NewProjectWizardViewModel? _vm;

    public WizardOutcome Outcome { get; private set; } = WizardOutcome.Back;
    public ProjectCreationResult? Result { get; private set; }

    public NewProjectConfigureView()
    {
        InitializeComponent();
    }

    public NewProjectConfigureView(NewProjectWizardViewModel vm) : this()
    {
        _vm = vm;
        DataContext = vm;
        vm.BrowseLocationCommand = new AsyncRelayCommand(BrowseLocationAsync);
        vm.BackRequested += OnBack;
        vm.Cancelled += OnCancelled;
        vm.ProjectCreated += OnProjectCreated;
    }

    private async Task BrowseLocationAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Project Location",
            AllowMultiple = false
        });
        if (folders.Count > 0 && _vm != null)
            _vm.Location = folders[0].Path.LocalPath;
    }

    private void OnBack(object? sender, EventArgs e)
    {
        Outcome = WizardOutcome.Back;
        Close();
    }

    private void OnCancelled(object? sender, EventArgs e)
    {
        Outcome = WizardOutcome.Cancelled;
        Close();
    }

    private void OnProjectCreated(object? sender, ProjectCreationResult result)
    {
        Outcome = WizardOutcome.Created;
        Result = result;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_vm != null)
        {
            _vm.BackRequested -= OnBack;
            _vm.Cancelled -= OnCancelled;
            _vm.ProjectCreated -= OnProjectCreated;
        }
        base.OnClosed(e);
    }
}
```

- [ ] **Step 3: Clean + build the pair**

Because AXAML changed:
```
Get-Process VisualGameStudio,BasicLang -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet clean VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release
dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release
```
Expected: Build succeeded (both windows compile; the Task 4 view's reference to `NewProjectConfigureView.WizardOutcome` now resolves).

- [ ] **Step 4: Commit Tasks 4 + 5 together**

```
git add VisualGameStudio.Shell/Views/Dialogs/NewProjectSelectView.axaml VisualGameStudio.Shell/Views/Dialogs/NewProjectSelectView.axaml.cs VisualGameStudio.Shell/Views/Dialogs/NewProjectConfigureView.axaml VisualGameStudio.Shell/Views/Dialogs/NewProjectConfigureView.axaml.cs
git commit -F <scratchpad-msg>
```
Message: `feat: New Project wizard views — select + configure windows with shared VM (tasks 4-5)`

---

## Task 6: Swap the seam + guard test + delete dead code

**Files:**
- Modify: `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs` (`NewProjectAsync`)
- Create: `VisualGameStudio.Tests/NewProjectWizardSwapGuardTests.cs`
- Delete: `NewProjectDialog.axaml`, `NewProjectDialog.axaml.cs`, `NewProjectViewModel.cs`

- [ ] **Step 1: Write the failing guard test**

Create `VisualGameStudio.Tests/NewProjectWizardSwapGuardTests.cs`:

```csharp
using System;
using System.IO;
using NUnit.Framework;

namespace VisualGameStudio.Tests;

[TestFixture]
public class NewProjectWizardSwapGuardTests
{
    private static string? FindRepoFile(params string[] relativeParts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    [Test]
    public void NewProjectAsync_OpensNewWizard_NotOldCreateProjectView()
    {
        var path = FindRepoFile("VisualGameStudio.Shell", "ViewModels", "MainWindowViewModel.cs");
        if (path == null)
        {
            Assert.Ignore("MainWindowViewModel.cs not found from the test base directory — skipping swap guard.");
            return;
        }

        var src = File.ReadAllText(path);
        Assert.That(src, Does.Contain("NewProjectSelectView"),
            "NewProjectAsync must open the new wizard's select window.");
        Assert.That(src, Does.Not.Contain("new Views.Dialogs.CreateProjectView"),
            "The app must no longer construct the old CreateProjectView.");
    }

    [Test]
    public void DeadNewProjectDialog_IsDeleted()
    {
        Assert.That(FindRepoFile("VisualGameStudio.Shell", "Views", "Dialogs", "NewProjectDialog.axaml"),
            Is.Null, "The dead NewProjectDialog should be removed.");
        Assert.That(FindRepoFile("VisualGameStudio.Shell", "ViewModels", "Dialogs", "NewProjectViewModel.cs"),
            Is.Null, "The dead NewProjectViewModel should be removed.");
    }
}
```

Add `using System.Linq;` at the top (for `Concat`).

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~NewProjectWizardSwapGuard"`
Expected: FAIL — `MainWindowViewModel.cs` still constructs `CreateProjectView` and the dead files still exist.

- [ ] **Step 3: Repoint NewProjectAsync**

In `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs`, replace the entire body of `NewProjectAsync` (currently ~1705–1730) with exactly this — construct the wizard VM directly (there is no factory type) and open the select window:

```csharp
    [RelayCommand]
    private async Task NewProjectAsync()
    {
        if (App.MainWindow == null) return;

        var wizardVm = new ViewModels.Dialogs.NewProjectWizardViewModel(_projectTemplateService);
        var selectWindow = new Views.Dialogs.NewProjectSelectView(wizardVm);

        var result = await selectWindow.ShowDialog<ProjectCreationResult?>(App.MainWindow);

        if (result != null && result.Success && !string.IsNullOrEmpty(result.ProjectPath))
        {
            try
            {
                await _projectService.OpenProjectAsync(result.ProjectPath);
                StatusText = $"Project created: {Path.GetFileNameWithoutExtension(result.ProjectPath)}";
            }
            catch (Exception ex)
            {
                await _dialogService.ShowMessageAsync("Error", $"Failed to open created project: {ex.Message}",
                    DialogButtons.Ok, DialogIcon.Error);
            }
        }
    }
```

`ProjectCreationResult` and the Core services namespace are already imported at the top of `MainWindowViewModel.cs`, so `ShowDialog<ProjectCreationResult?>` resolves as-is (no fully-qualified fallback needed).

- [ ] **Step 4: Delete the dead dialog**

Delete these three files:
```
git rm VisualGameStudio.Shell/Views/Dialogs/NewProjectDialog.axaml VisualGameStudio.Shell/Views/Dialogs/NewProjectDialog.axaml.cs VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectViewModel.cs
```
(Before deleting, run a final safety grep to confirm no live C# references remain — the `AXAML` compiled partial `NewProjectDialog` should have no `new NewProjectDialog(` callers.)

Run: `Grep pattern "NewProjectDialog|NewProjectViewModel" over VisualGameStudio.Shell/**/*.cs`
Expected: no matches outside the deleted files. If any remain (e.g. a stray using), fix them.

- [ ] **Step 5: Clean + build**

```
Get-Process VisualGameStudio,BasicLang -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet clean VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release
dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release
```
Expected: Build succeeded.

- [ ] **Step 6: Run the guard tests**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~NewProjectWizardSwapGuard"`
Expected: PASS (2 tests).

- [ ] **Step 7: Commit**

```
git add VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs VisualGameStudio.Tests/NewProjectWizardSwapGuardTests.cs
git commit -F <scratchpad-msg>
```
(The `git rm` deletions are already staged.) Message: `feat: route New Project to the two-window wizard; retire dead NewProjectDialog (task 6)`

---

## Task 7: Full verification + manual smoke

- [ ] **Step 1: Run the full test suite**

```
Get-Process VisualGameStudio,BasicLang -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release
```
Expected: all tests pass (previous green count + the new wizard tests, minus the 0 tests for the deleted dead dialog — there were none).

- [ ] **Step 2: Manual smoke of the wizard**

Run the freshly built IDE: `VisualGameStudio.Shell/bin/Release/net8.0/VisualGameStudio.exe` (or the repo `IDE/VisualGameStudio.exe` after a deploy). Verify:

1. File ▸ New Project (and the toolbar button, the command palette "New Project...", and the Welcome card) all open **window 1**.
2. Language = BasicLang shows backends {C# (.NET), MSIL, Native C++, LLVM}; switching to C++ shows {LLVM (clang++), GCC (g++), MSVC} and the template list becomes the three cpp templates.
3. Platform = Cross-platform hides WinForms/WPF (BasicLang, C# backend). Project type filters. Search filters.
4. Next → **window 2**. Back returns to window 1 with selections intact.
5. Window 2 shows the read-only template description; Target framework shows for C#/MSIL, C++ standard shows for C++, neither shows for Native/LLVM.
6. Browse... picks a folder. Create makes the project and the IDE opens it (status bar: "Project created: ..."). Cancel from either window closes with nothing created.

- [ ] **Step 3: Record the smoke result**

If all pass, note it in the final report. If any fail, fix in a follow-up commit on this branch and re-smoke.

- [ ] **Step 4: Stop — hand back for merge**

Do NOT merge to `master` or push. Report the branch (`new-project-wizard`), the green test count, and the smoke result, and let the user decide on merge (mirrors repo convention: merge/push only on explicit request).

---

## Notes / guardrails

- **UI-only invariant:** no edits to `IProjectTemplateService`, `CreateProjectOptions`, `ProjectTemplateService`, `TemplateEngine`, `ProjectFile`, `ProjectSerializer`, or any `.blproj` generation. If a task seems to require one, stop — it's out of scope for this plan.
- **Display-only controls:** the C++ toolchain dropdown, a non-default C++ standard, and the platform filter must not reach `CreateProjectOptions`. The tests in Task 3 assert the toolchain can't leak; keep it that way.
- **Don't touch** `CreateProjectView`/`CreateProjectViewModel` — they stay as the preserved "old" dialog.
```
