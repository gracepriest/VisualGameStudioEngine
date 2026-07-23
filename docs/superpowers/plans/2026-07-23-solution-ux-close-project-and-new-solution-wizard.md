# Solution UX: Close Project + guided New Solution wizard — Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a File-menu Close Project item, replace the bare New Solution flow with a guided wizard that reuses the New Project pages, unify "Add Project to Solution" onto the same pages + creation service, and restore a project-reference capability as a Solution Explorer command.

**Architecture:** Route *all* project/solution creation through the already-tested `IProjectTemplateService` (`CreateSolutionAsync` / `CreateProjectAsync(AddToExistingSolution)`); keep `ISolutionService` as the session/explorer manager that loads the produced `.blsln`. A settable `WizardMode` + an injectable `FinishAction` on the existing `NewProjectWizardViewModel` let the same select/configure pages serve New Project, New Solution, and Add-to-Solution. A pure static `SolutionWizardMapper` makes all option-assembly unit-testable.

**Tech Stack:** C# / .NET, Avalonia (MVVM, CommunityToolkit.Mvvm source-generated `[RelayCommand]`/`[ObservableProperty]`), NUnit. BOM-less UTF-8 files — always edit via Edit/Write, never PowerShell `Get/Set-Content`.

**Spec:** `docs/superpowers/specs/2026-07-23-solution-ux-close-project-and-new-solution-wizard-design.md`

---

## Conventions for every task

- Follow @superpowers:test-driven-development — write the failing test first, watch it fail, minimal impl, watch it pass, commit.
- Build the IDE with: `dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release`
- Run a focused test: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~<Class>.<Method>"`
- Full suite (redirect — output exceeds tool limits; exit 1 from the known BL6009 flake is normal):
  `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release > "$env:TEMP\suite.txt" 2>&1`
- **After any `.axaml` change: `dotnet clean` before build** (stale cache crashes).
- One builder at a time in the worktree (concurrent `dotnet` fights `testhost` file locks).
- Commit after each green task. Commit messages end with the `Co-Authored-By` trailer.

## File structure (created / modified)

**New files**
- `VisualGameStudio.ProjectSystem/Services/SolutionWizardMapper.cs` — pure option/result mappers.
- `VisualGameStudio.ProjectSystem/Services/BlprojReferenceWriter.cs` — targeted `<ProjectReference>` XML add for a `.blproj`.
- `VisualGameStudio.Shell/ViewModels/Dialogs/NewSolutionViewModel.cs` — solution-details window VM.
- `VisualGameStudio.Shell/Views/Dialogs/NewSolutionView.axaml` (+ `.axaml.cs`) — solution-details window.
- `VisualGameStudio.Shell/ViewModels/Dialogs/AddProjectReferenceViewModel.cs` — reference picker VM.
- `VisualGameStudio.Shell/Views/Dialogs/AddProjectReferenceDialog.axaml` (+ `.axaml.cs`) — reference picker.
- `VisualGameStudio.Tests/…` — new test classes; a reusable `FakeGitService`.

**Modified files**
- `VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectWizardViewModel.cs` — `WizardMode`, `IsLocationLocked`, `BuildCreateOptions()`, ctor-defaulted `FinishAction`.
- `VisualGameStudio.Shell/Views/Dialogs/NewProjectSelectView.axaml(.cs)` — public `Outcome` + Back button in NewSolution mode.
- `VisualGameStudio.Shell/Views/Dialogs/NewProjectConfigureView.axaml(.cs)` — Location read-only + Browse disabled + checkboxes hidden + mode labels when locked.
- `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs` — `CloseProjectCommand` (renamed), `HasProjectOpen`, rewritten `NewSolutionAsync` + `AddNewProjectToSolutionAsync`.
- `VisualGameStudio.Shell/ViewModels/Dialogs/CommandPaletteViewModel.cs:235` — repoint palette entry.
- `VisualGameStudio.Shell/Views/MainWindow.axaml:178` — Close Project menu item.
- `VisualGameStudio.Shell/ViewModels/Panels/SolutionExplorerViewModel.cs` — `ISolutionService`, `AddProjectReferenceCommand`, persistence fix.
- `VisualGameStudio.Shell/Views/Panels/SolutionExplorerView.axaml` — project-node context section.
- `VisualGameStudio.Shell/Configuration/ServiceConfiguration.cs` — DI for the new `ISolutionService` dependency on the explorer VM (verify it resolves the same singleton).
- **Deleted:** `VisualGameStudio.Shell/ViewModels/Dialogs/AddProjectToSolutionViewModel.cs`, `VisualGameStudio.Shell/Views/Dialogs/AddProjectToSolutionDialog.axaml(.cs)`.

---

## Task 0: Worktree + green baseline

**Files:** none (setup)

- [ ] **Step 1:** Create an isolated worktree off `master` per @superpowers:using-git-worktrees (e.g. `../vgs-solution-ux`, branch `solution-ux`). All remaining tasks run inside it.
- [ ] **Step 2:** Confirm a clean baseline build: `dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release`. Expected: succeeds.
- [ ] **Step 3:** Record the baseline suite tail (redirect to a file). Expected: the known pre-existing counts (≈3470 passed / 1 known BL6009 flake / 2 skips). This is the regression floor.

---

## Task 1: `SolutionWizardMapper` (pure mappers)

**Files:**
- Create: `VisualGameStudio.ProjectSystem/Services/SolutionWizardMapper.cs`
- Test: `VisualGameStudio.Tests/Services/SolutionWizardMapperTests.cs`

Option/result types live in `VisualGameStudio.Core.Abstractions.Services` (`CreateProjectOptions`, `CreateSolutionOptions`, `ProjectCreationResult`, `SolutionCreationResult`); `BasicLangSolution` in `VisualGameStudio.Core.Models`.

- [ ] **Step 1: Write failing tests**

```csharp
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Services;

[TestFixture]
public class SolutionWizardMapperTests
{
    [Test]
    public void BuildSolutionOptions_carries_args_and_single_initial_project()
    {
        var first = new CreateProjectOptions { Name = "App", SolutionType = SolutionTypes.Llvm };
        var s = SolutionWizardMapper.BuildSolutionOptions("MySln", @"C:\src", initGit: false, first);

        Assert.That(s.Name, Is.EqualTo("MySln"));
        Assert.That(s.Location, Is.EqualTo(@"C:\src"));
        Assert.That(s.CreateGitRepository, Is.False);
        Assert.That(s.SolutionType, Is.EqualTo(SolutionTypes.Llvm));   // preserves LLVM
        Assert.That(s.InitialProjects, Has.Count.EqualTo(1));
        Assert.That(s.InitialProjects[0], Is.SameAs(first));
    }

    [Test]
    public void BuildAddToSolutionOptions_pins_location_and_disables_folder_and_git()
    {
        var sln = new BasicLangSolution { FilePath = @"C:\src\S\S.blsln", SolutionName = "S" };
        var opts = new CreateProjectOptions { Name = "Lib", Location = @"C:\wrong" };

        var a = SolutionWizardMapper.BuildAddToSolutionOptions(opts, sln);

        Assert.That(a.Location, Is.EqualTo(sln.SolutionDirectory));
        Assert.That(a.CreateSolutionFolder, Is.False);
        Assert.That(a.CreateGitRepository, Is.False);       // prevents git re-init / .gitignore clobber
        Assert.That(a.AddToExistingSolution, Is.True);
        Assert.That(a.ExistingSolutionPath, Is.EqualTo(sln.FilePath));
    }

    [Test]
    public void ToProjectResult_maps_first_project_path()
    {
        var sr = new SolutionCreationResult { Success = true, SolutionPath = "s.blsln",
            ProjectPaths = { "p.blproj" }, FilesToOpen = { "a.bas" } };
        var r = SolutionWizardMapper.ToProjectResult(sr);
        Assert.That(r.Success, Is.True);
        Assert.That(r.ProjectPath, Is.EqualTo("p.blproj"));
        Assert.That(r.SolutionPath, Is.EqualTo("s.blsln"));
        Assert.That(r.FilesToOpen, Is.EquivalentTo(new[] { "a.bas" }));
    }

    [Test]
    public void ToProjectResult_empty_projects_is_failure_not_silent_success()
    {
        var sr = new SolutionCreationResult { Success = true, SolutionPath = "s.blsln" }; // no ProjectPaths
        var r = SolutionWizardMapper.ToProjectResult(sr);
        Assert.That(r.Success, Is.False);
        Assert.That(r.Error, Is.Not.Null.And.Contains("first project"));
        Assert.That(r.ProjectPath, Is.Null);
    }

    [Test]
    public void ToProjectResult_propagates_failure()
    {
        var r = SolutionWizardMapper.ToProjectResult(new SolutionCreationResult { Success = false, Error = "boom" });
        Assert.That(r.Success, Is.False);
        Assert.That(r.Error, Is.EqualTo("boom"));
    }
}
```

- [ ] **Step 2: Run — verify fail** (`~SolutionWizardMapperTests`). Expected: compile error / type not found.

- [ ] **Step 3: Implement**

```csharp
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Pure translation between the New Project wizard's <see cref="CreateProjectOptions"/> and the
/// solution-creation / add-to-solution option shapes. No I/O — unit-testable in isolation.
/// </summary>
public static class SolutionWizardMapper
{
    public static CreateSolutionOptions BuildSolutionOptions(
        string name, string location, bool initGit, CreateProjectOptions firstProject)
    {
        var opts = new CreateSolutionOptions
        {
            Name = name,
            Location = location,
            CreateGitRepository = initGit,
            SolutionType = firstProject.SolutionType,
        };
        opts.InitialProjects.Add(firstProject);
        return opts;
    }

    public static CreateProjectOptions BuildAddToSolutionOptions(CreateProjectOptions opts, BasicLangSolution solution)
    {
        opts.Location = solution.SolutionDirectory;
        opts.CreateSolutionFolder = false;
        opts.CreateGitRepository = false;
        opts.AddToExistingSolution = true;
        opts.ExistingSolutionPath = solution.FilePath;
        return opts;
    }

    public static ProjectCreationResult ToProjectResult(SolutionCreationResult sr)
    {
        if (!sr.Success)
            return new ProjectCreationResult { Success = false, Error = sr.Error, SolutionPath = sr.SolutionPath };
        if (sr.ProjectPaths.Count == 0)
            return new ProjectCreationResult
            {
                Success = false, SolutionPath = sr.SolutionPath,
                Error = "The solution was created but its first project could not be scaffolded."
            };
        return new ProjectCreationResult
        {
            Success = true, SolutionPath = sr.SolutionPath,
            ProjectPath = sr.ProjectPaths[0], FilesToOpen = sr.FilesToOpen
        };
    }
}
```

- [ ] **Step 4: Run — verify pass.**
- [ ] **Step 5: Commit** — `feat(solution): pure SolutionWizardMapper for wizard option assembly`.

---

## Task 2: `NewProjectWizardViewModel` seam (Mode + BuildCreateOptions + FinishAction)

**Files:**
- Modify: `VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectWizardViewModel.cs` (options block ~387-404; `CreateProjectAsync` ~375-414; ctor ~131-138)
- Test: `VisualGameStudio.Tests/NewProjectWizardViewModelTests.cs` (extend existing)

- [ ] **Step 1: Write failing tests** — add to the existing fixture (it already constructs the VM with fakes):

```csharp
[Test]
public void IsLocationLocked_only_in_solution_modes()
{
    var vm = MakeVm();                      // existing helper: new NewProjectWizardViewModel(fakeTemplateSvc, fakeProbe)
    Assert.That(vm.IsLocationLocked, Is.False);
    vm.Mode = WizardMode.NewSolution;   Assert.That(vm.IsLocationLocked, Is.True);
    vm.Mode = WizardMode.AddToSolution; Assert.That(vm.IsLocationLocked, Is.True);
}

[Test]
public void BuildCreateOptions_reflects_selection()
{
    var vm = MakeVm();
    vm.ProjectName = "App"; vm.Location = @"C:\x";
    // (select a known backend/template via the existing test seams the fixture uses)
    var o = vm.BuildCreateOptions();
    Assert.That(o.Name, Is.EqualTo("App"));
    Assert.That(o.Location, Is.EqualTo(@"C:\x"));
    Assert.That(o.SolutionType, Is.EqualTo(vm.SelectedBackend!.SolutionType));
}

[Test]
public async Task CreateProject_routes_through_FinishAction()
{
    var vm = MakeVm();
    vm.ProjectName = "App"; vm.Location = @"C:\x";
    CreateProjectOptions? seen = null;
    vm.FinishAction = o => { seen = o; return Task.FromResult(new ProjectCreationResult { Success = true, ProjectPath = "p" }); };
    ProjectCreationResult? raised = null;
    vm.ProjectCreated += (_, r) => raised = r;

    await vm.CreateProjectCommand.ExecuteAsync(null);

    Assert.That(seen, Is.Not.Null);                 // delegate received the built options
    Assert.That(raised?.ProjectPath, Is.EqualTo("p"));
}
```

- [ ] **Step 2: Run — verify fail.**

- [ ] **Step 3: Implement**
  - Add near the top (the file already has `enum ProjectLanguage`):
    ```csharp
    public enum WizardMode { NewProject, NewSolution, AddToSolution }
    ```
  - Add properties:
    ```csharp
    public WizardMode Mode { get; set; } = WizardMode.NewProject;
    public bool IsLocationLocked => Mode != WizardMode.NewProject;
    public Func<CreateProjectOptions, Task<ProjectCreationResult>> FinishAction { get; set; } = null!;
    ```
  - In the ctor, **after** `_templateService = templateService;`, default the delegate:
    ```csharp
    FinishAction = opts => _templateService.CreateProjectAsync(opts);
    ```
  - Extract the existing options block (the `var options = new CreateProjectOptions { … }` at ~387-404) into:
    ```csharp
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
        CppStandard = SelectedLanguage == ProjectLanguage.Cpp ? CppStandard : null,
        CppToolchain = SelectedLanguage == ProjectLanguage.Cpp && SelectedBackend is { IsEnabled: true }
            ? SelectedBackend.ToolchainId : null,
    };
    ```
  - In `CreateProjectAsync`, keep `await ToolchainProbeTask;`, then replace the inline options build + `_templateService.CreateProjectAsync(options)` call with:
    ```csharp
    var options = BuildCreateOptions();
    var result = await FinishAction(options);
    ```
    Leave the existing `if (result.Success) { ProjectCreated?.Invoke(this, result); } else { ErrorMessage = …; HasError = true; }` handling unchanged.

- [ ] **Step 4: Run — verify pass** (new tests + existing `NewProjectWizardViewModelTests` regression).
- [ ] **Step 5: Commit** — `feat(wizard): Mode + BuildCreateOptions + injectable FinishAction seam`.

---

## Task 3: Feature A — Close Project

**Files:**
- Modify: `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs` (`CloseFolderAsync` ~6715-6732; project event handlers subscribed ~545-546; `OnSolutionLoaded/Closed` ~6986-7002)
- Modify: `VisualGameStudio.Shell/ViewModels/Dialogs/CommandPaletteViewModel.cs:235`
- Modify: `VisualGameStudio.Shell/Views/MainWindow.axaml:178`
- Test: `VisualGameStudio.Tests/…/MainWindowViewModel_CloseProjectTests.cs`

- [ ] **Step 1: Write failing tests** (mirror how existing MWVM tests construct the VM with fakes):

```csharp
[Test]
public void HasProjectOpen_true_for_standalone_project_false_under_solution()
{
    var (vm, projSvc, solSvc) = MakeMwvm();
    projSvc.RaiseProjectOpened(new BasicLangProject());   // fake raises ProjectOpened
    Assert.That(vm.HasProjectOpen, Is.True);
    solSvc.RaiseSolutionLoaded();                          // solution now open
    Assert.That(vm.HasProjectOpen, Is.False);
    projSvc.RaiseProjectClosed();
    Assert.That(vm.HasProjectOpen, Is.False);
}

[Test]
public void CommandPalette_exposes_Close_Project_not_Close_Folder()
{
    var palette = MakeCommandPalette();      // however the fixture builds it
    var names = palette.AllCommandNames();   // or inspect the built command list
    Assert.That(names, Does.Contain("Close Project"));
    Assert.That(names, Does.Not.Contain("Close Folder"));
}
```

- [ ] **Step 2: Run — verify fail.**

- [ ] **Step 3: Implement**
  - Rename `CloseFolderAsync` → `CloseProjectAsync` (method name only; keep the body verbatim). The generated command becomes `CloseProjectCommand`.
  - Add `[ObservableProperty] private bool _hasProjectOpen;` and a helper:
    ```csharp
    private void RecomputeHasProjectOpen() =>
        HasProjectOpen = _projectService.CurrentProject != null && !_solutionService.HasSolution;
    ```
    Call `RecomputeHasProjectOpen()` from the existing project `ProjectOpened`/`ProjectClosed` handlers **and** from `OnSolutionLoaded`/`OnSolutionClosed` (~6986-7002).
  - `CommandPaletteViewModel.cs:235`: `("File", "Close Folder", null, () => vm.CloseFolderCommand.Execute(null))` → `("File", "Close Project", null, () => vm.CloseProjectCommand.Execute(null))`.
  - `MainWindow.axaml` (above line 178's "Close Solution"):
    ```xml
    <MenuItem Header="Close _Project" Command="{Binding CloseProjectCommand}" IsEnabled="{Binding HasProjectOpen}"/>
    ```

- [ ] **Step 4: Run — verify pass** (+ `dotnet clean` before build, AXAML changed).
- [ ] **Step 5: Commit** — `feat(ide): Close Project File-menu item; repoint palette; HasProjectOpen`.

---

## Task 4: `NewSolutionViewModel` (solution-details window VM)

**Files:**
- Create: `VisualGameStudio.Shell/ViewModels/Dialogs/NewSolutionViewModel.cs`
- Test: `VisualGameStudio.Tests/…/NewSolutionViewModelTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
[Test] public void CanConfirm_false_when_name_empty() { var vm = new NewSolutionViewModel(); vm.Location = @"C:\x"; Assert.That(vm.CanConfirm, Is.False); }
[Test] public void Invalid_filename_chars_block_confirm()
{
    var vm = new NewSolutionViewModel { SolutionName = "a:b", Location = @"C:\x" };
    Assert.That(vm.CanConfirm, Is.False);
    Assert.That(vm.ErrorMessage, Is.Not.Empty);
}
[Test] public void SolutionFilePreview_composes_path()
{
    var vm = new NewSolutionViewModel { SolutionName = "MySln", Location = @"C:\src" };
    Assert.That(vm.SolutionFilePreview, Does.EndWith(@"MySln\MySln.blsln"));
    Assert.That(vm.CanConfirm, Is.True);
}
[Test] public void InitializeGit_defaults_true() => Assert.That(new NewSolutionViewModel().InitializeGit, Is.True);
```

- [ ] **Step 2: Run — verify fail.**

- [ ] **Step 3: Implement** — mirror `AddProjectToSolutionViewModel`'s structure (CommunityToolkit `[ObservableProperty]`, `Create`/`Cancel`/`BrowseLocation` commands, `DialogResult`, `CloseDialog` action). Hard-block invalid chars via `Path.GetInvalidFileNameChars()`; surface (non-blocking) MSBuild special-char note via `BasicLang.Compiler.ProjectSystem.MSBuildText.FindSpecialCharacters`. Expose `SolutionName`, `Location`, `InitializeGit=true`, `ErrorMessage`, `SolutionFilePreview` (=`Path.Combine(Location, SolutionName, SolutionName + ".blsln")` when both set), and `CanConfirm`. `Confirm()` sets `DialogResult=true` + `CloseDialog?.Invoke()`.

- [ ] **Step 4: Run — verify pass.**
- [ ] **Step 5: Commit** — `feat(wizard): NewSolutionViewModel (name/location/git + validation)`.

---

## Task 5: `NewSolutionView` window + `NewProjectSelectView` Outcome/Back

**Files:**
- Create: `VisualGameStudio.Shell/Views/Dialogs/NewSolutionView.axaml` (+ `.axaml.cs`)
- Modify: `VisualGameStudio.Shell/Views/Dialogs/NewProjectSelectView.axaml(.cs)`

> View/AXAML layer — verified by build + the Task 16 manual smoke, not unit tests (Avalonia windows aren't headless-tested in this repo).

- [ ] **Step 1:** Author `NewSolutionView.axaml` bound to `NewSolutionViewModel`: name TextBox, location TextBox + Browse (code-behind folder picker, mirroring the existing configure view's Browse), an "Initialize Git repository" CheckBox, the `SolutionFilePreview` label, `ErrorMessage`, and Cancel/Next buttons (`Next` enabled on `CanConfirm`). Code-behind exposes the confirmed `(SolutionName, Location, InitializeGit)` or a cancel.
- [ ] **Step 2:** In `NewProjectSelectView.axaml.cs` add `public enum WizardOutcome { Created, Cancelled, Back }` (or reuse `NewProjectConfigureView.WizardOutcome`) and `public WizardOutcome Outcome { get; private set; }`. Set `Created`/`Cancelled` on the existing close paths; add a **Back** button (bind `IsVisible` to a VM flag that is true only when `Mode == WizardMode.NewSolution`) whose handler sets `Outcome = Back` and closes.
- [ ] **Step 3:** `dotnet clean` + build. Expected: succeeds.
- [ ] **Step 4: Commit** — `feat(wizard): NewSolutionView window + SelectView Outcome/Back`.

---

## Task 6: `NewSolutionAsync` host orchestration

**Files:**
- Modify: `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs` (`NewSolutionAsync` ~6775-6803)

> The window loop is smoke-verified (Task 16); the *creation* logic it calls is covered by Task 1 (mapper) + Task 7 (E2E).

- [ ] **Step 1: Implement** — replace `NewSolutionAsync` body with the loop:
  1. Show `NewSolutionView` (bound to a `NewSolutionViewModel`). If cancelled → return. Capture `(name, location, initGit)`.
  2. Build `NewProjectWizardViewModel(_projectTemplateService, _cppToolchainProbe)`, set `Mode = WizardMode.NewSolution`, `Location = Path.Combine(location, name)`, and:
     ```csharp
     wizardVm.FinishAction = async first =>
     {
         var solOpts = SolutionWizardMapper.BuildSolutionOptions(name, location, initGit, first);
         var sr = await _projectTemplateService.CreateSolutionAsync(solOpts);
         return SolutionWizardMapper.ToProjectResult(sr);
     };
     ```
  3. Show `NewProjectSelectView(wizardVm)`; after `ShowDialog`, switch on `view.Outcome`: `Back` → loop to step 1 preserving the `NewSolutionViewModel`; `Cancelled` → return; `Created` → use `view.Result`.
  4. On a successful result: `LoadSolutionAsync(result.SolutionPath!)` → `SolutionExplorer.LoadSolution(...)` → `_recentProjectsService.AddRecentProject(...)` → `foreach (var f in result.FilesToOpen) await OpenFileAsync(f);`. On failure, the wizard already surfaced the error; just return.
- [ ] **Step 2:** `dotnet clean` + build. Expected: succeeds.
- [ ] **Step 3: Commit** — `feat(ide): guided New Solution wizard orchestration`.

---

## Task 7: New Solution end-to-end (real `ProjectTemplateService`)

**Files:**
- Test: `VisualGameStudio.Tests/Services/NewSolutionEndToEndTests.cs`
- Create (if absent): `VisualGameStudio.Tests/…/FakeGitService.cs` (records `InitRepositoryAsync` calls; verify the real `IGitService` interface for the exact signature).

- [ ] **Step 1: Write failing tests** — construct the real `ProjectTemplateService` with a `FakeGitService` (verify its ctor accepts `IGitService?`), a temp dir, then:

```csharp
[TestCase("dotnet")]  [TestCase("llvm")]   // proves LLVM survives
public async Task NewSolution_creates_loadable_solution_with_first_project(string backendId)
{
    var svc = new ProjectTemplateService(fakeGit);      // adjust to real ctor
    var first = new CreateProjectOptions {
        Name = "App", SolutionType = SolutionTypes.All.First(t => t.Id == backendId),
        Template = ProjectTemplates.ConsoleApp };
    var solOpts = SolutionWizardMapper.BuildSolutionOptions("MySln", tempDir, initGit: true, first);

    var sr = await svc.CreateSolutionAsync(solOpts);
    Assert.That(sr.Success, Is.True);

    var solution = await new SolutionService().LoadSolutionAsync(sr.SolutionPath!);
    Assert.That(solution.Projects.Select(p => p.Name), Does.Contain("App"));
    Assert.That(File.Exists(solution.Projects[0].GetFullPath(solution.SolutionDirectory)), Is.True);
    Assert.That(fakeGit.InitCount, Is.EqualTo(1));       // solution-level git init only
}

[Test]
public async Task NewSolution_cpp_first_project_travels_toolchain_and_standard()
{
    // SolutionType = Cpp, Template = CppConsoleApp, CppStandard="c++20", CppToolchain per availability;
    // assert the created .blproj carries CppStandard/CppToolchain (parse the file).
}

[Test]
public async Task NewSolution_initGit_false_does_not_init()
{
    // initGit:false -> fakeGit.InitCount == 0
}
```

- [ ] **Step 2: Run — verify fail** (until FakeGitService / ctor wiring compiles), then **implement** `FakeGitService` and fix construction.
- [ ] **Step 3: Run — verify pass.**
- [ ] **Step 4: Commit** — `test(solution): New Solution E2E (C#/LLVM/C++, git-init count)`.

---

## Task 8: Feature C — unify "Add Project to Solution"

**Files:**
- Modify: `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs` (`AddNewProjectToSolutionAsync` ~6868-6953)
- Test: `VisualGameStudio.Tests/Services/AddProjectToSolutionEndToEndTests.cs`

- [ ] **Step 1: Write failing E2E** — against real `ProjectTemplateService` + `SolutionService` + `FakeGitService` in a temp dir: create a solution with one project, then add a second via `BuildAddToSolutionOptions` + `CreateProjectAsync`:

```csharp
[Test]
public async Task AddProject_writes_blproj_once_and_registers_without_git_reinit()
{
    // arrange: an on-disk solution with project "A" (via CreateSolutionAsync)
    var opts = new CreateProjectOptions { Name = "B", SolutionType = SolutionTypes.DotNet, Template = ProjectTemplates.ConsoleApp };
    SolutionWizardMapper.BuildAddToSolutionOptions(opts, loadedSolution);
    var r = await svc.CreateProjectAsync(opts);

    Assert.That(r.Success, Is.True);
    Assert.That(fakeGit.InitCount, Is.EqualTo(initCountAfterSolutionCreate)); // unchanged: no re-init
    var reloaded = await new SolutionService().LoadSolutionAsync(loadedSolution.FilePath);
    Assert.That(reloaded.Projects.Select(p => p.Name), Does.Contain("B"));
    // .blproj for B exists and was written exactly once (no minimal-then-rich double write)
}
```

- [ ] **Step 2: Run — verify fail.**
- [ ] **Step 3: Implement** — rewrite `AddNewProjectToSolutionAsync`:
  1. `if (!_solutionService.HasSolution) return;`
  2. `await _solutionService.SaveSolutionAsync();` (flush in-memory edits before the template service reads the `.blsln` from disk).
  3. Build `NewProjectWizardViewModel` with `Mode = WizardMode.AddToSolution`, `Location = _solutionService.CurrentSolution!.SolutionDirectory`, and
     ```csharp
     wizardVm.FinishAction = opts =>
         _projectTemplateService.CreateProjectAsync(
             SolutionWizardMapper.BuildAddToSolutionOptions(opts, _solutionService.CurrentSolution!));
     ```
  4. Show `NewProjectSelectView`; on a successful result: reload
     `LoadSolutionAsync(_solutionService.CurrentSolution!.FilePath)` → `SolutionExplorer.LoadSolution(...)` → open `result.FilesToOpen`.
  Delete the old bespoke `.blproj`-string writer + reference XML block entirely.
- [ ] **Step 4: Run — verify pass.**
- [ ] **Step 5: Commit** — `feat(ide): unify Add Project to Solution onto reused pages + template service`.

---

## Task 9: `NewProjectConfigureView` locked-mode bindings

**Files:**
- Modify: `VisualGameStudio.Shell/Views/Dialogs/NewProjectConfigureView.axaml` (Location TextBox ~71; Browse button ~70-73; checkboxes ~98-99; Create button/label ~122,126)

> View layer — build + smoke verified.

- [ ] **Step 1:** Location TextBox `IsReadOnly="{Binding IsLocationLocked}"`; the adjacent Browse button `IsEnabled="{Binding !IsLocationLocked}"` (or `IsVisible`). "Create solution folder" + "Initialize Git repository" checkboxes `IsVisible="{Binding !IsLocationLocked}"`. Bind the Create button text / busy label to `Mode` so New Solution reads "Create solution" (a small `IValueConverter` or a computed VM string property `CreateButtonText`).
- [ ] **Step 2:** `dotnet clean` + build. Expected: succeeds.
- [ ] **Step 3: Commit** — `feat(wizard): lock Location + hide git/folder options in solution modes`.

---

## Task 10: Retire the old Add-Project dialog

**Files:**
- Delete: `VisualGameStudio.Shell/ViewModels/Dialogs/AddProjectToSolutionViewModel.cs`
- Delete: `VisualGameStudio.Shell/Views/Dialogs/AddProjectToSolutionDialog.axaml` (+ `.axaml.cs`)

- [ ] **Step 1:** Grep the solution for `AddProjectToSolution` — confirm only the (now-rewritten) `AddNewProjectToSolutionAsync` referenced it, and it no longer does. Delete the three files.
- [ ] **Step 2:** `dotnet clean` + build. Expected: succeeds (no dangling references). If `CheckableItem` (defined in the deleted VM) is reused by the reference picker, move it to a shared location first — see Task 14.
- [ ] **Step 3: Commit** — `refactor(ide): remove superseded AddProjectToSolution dialog`.

---

## Task 11: Feature D — Step 0 empirical checks

**Files:** scratch only (no commit)

- [ ] **Step 0a — is the `.blproj` `<ProjectReference>` load-bearing?** Create two BasicLang projects in a temp solution: library `A` exporting a symbol, app `B` using it. Compile `B` via the CLI (`IDE/BasicLang.exe build …`) with and without a `<ProjectReference>` to `A` in `B.blproj`. Record whether cross-project resolution requires the `.blproj` element (expected: yes). If resolution instead comes only from the `.blsln`, adjust Task 12/13 accordingly.
- [ ] **Step 0b — does a targeted XML add preserve `.blproj` content?** Take a real `.blproj` containing `PropertyGroup`, `CppToolchain`, and `Compile` items; run the intended `XDocument` load → add one `<ProjectReference>` → save; diff. Confirm nothing else changes. (Informs whether the optional Task 15 serializer round-trip is safe.)
- [ ] Record findings inline in the plan/PR notes before proceeding.

---

## Task 12: `.blproj` `<ProjectReference>` writer

**Files:**
- Create: `VisualGameStudio.ProjectSystem/Services/BlprojReferenceWriter.cs`
- Test: `VisualGameStudio.Tests/Services/BlprojReferenceWriterTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
[Test]
public void Adds_project_reference_and_is_idempotent()
{
    var path = WriteTempBlproj(@"<BasicLangProject Version=""1.0""><PropertyGroup><ProjectName>B</ProjectName></PropertyGroup><ItemGroup/></BasicLangProject>");
    BlprojReferenceWriter.AddReference(path, @"..\A\A.blproj");
    BlprojReferenceWriter.AddReference(path, @"..\A\A.blproj");   // idempotent

    var xml = XDocument.Load(path);
    var refs = xml.Descendants("ProjectReference").Where(e => (string?)e.Attribute("Include") == @"..\A\A.blproj").ToList();
    Assert.That(refs, Has.Count.EqualTo(1));
}

[Test]
public void Preserves_existing_content()
{
    // load a blproj with CppToolchain + Compile items, add a ref, assert those nodes still present
}
```

- [ ] **Step 2: Run — verify fail.**
- [ ] **Step 3: Implement** — `public static void AddReference(string blprojPath, string includeRelPath)`: `XDocument.Load`; find/create an `ItemGroup`; skip if a `ProjectReference` with a case-insensitively equal `Include` exists; else add `new XElement("ProjectReference", new XAttribute("Include", includeRelPath))`; `Save`. Match the element shape the compiler reads (`ProjectFile.cs:202-207,337-344`).
- [ ] **Step 4: Run — verify pass.**
- [ ] **Step 5: Commit** — `feat(projsys): idempotent .blproj ProjectReference writer`.

---

## Task 13: Feature D — `AddProjectReferenceCommand` + persistence fix

**Files:**
- Modify: `VisualGameStudio.Shell/ViewModels/Panels/SolutionExplorerViewModel.cs` (ctor/fields; `SetAsStartupProjectAsync` ~518; `RemoveFromSolutionAsync` ~575; saves at ~435,481,541,602)
- Modify: `VisualGameStudio.Shell/Configuration/ServiceConfiguration.cs` (inject `ISolutionService`)
- Test: `VisualGameStudio.Tests/…/SolutionExplorer_ReferenceTests.cs`

- [ ] **Step 1: Write failing tests** — construct the VM with fakes + a **real** `SolutionService` holding an in-memory solution with projects A, B, C on disk (temp). Test the extracted apply method (keeps UI out):

```csharp
[Test]
public async Task ApplyReferences_dual_writes_blsln_and_blproj()
{
    // B references A
    await vm.ApplyProjectReferencesAsync("B", new[] { "A" });
    var reloaded = await new SolutionService().LoadSolutionAsync(slnPath);
    Assert.That(reloaded.GetProject("B")!.ProjectReferences, Does.Contain("A"));      // .blsln persisted
    var bXml = XDocument.Load(bBlprojPath);
    Assert.That(bXml.Descendants("ProjectReference").Any(e => ((string)e.Attribute("Include")!).Contains("A.blproj")), Is.True); // .blproj persisted
}

[Test]
public async Task ApplyReferences_rejects_cycle_with_message()
{
    await vm.ApplyProjectReferencesAsync("B", new[] { "A" });
    await vm.ApplyProjectReferencesAsync("A", new[] { "B" });   // would cycle
    Assert.That(lastDialogMessage, Does.Contain("circular"));   // surfaced ex.Message
    Assert.That(reloadedA.ProjectReferences, Does.Not.Contain("B"));
}
```

- [ ] **Step 2: Run — verify fail.**
- [ ] **Step 3: Implement**
  - Add `ISolutionService` to the ctor + field; register the dependency in `ServiceConfiguration.cs` (**verify** the explorer VM resolves the *same* singleton `MainWindowViewModel` uses — both are `AddSingleton`).
  - `[RelayCommand] AddProjectReferenceAsync`: gate on `_solutionService.HasSolution && SelectedNode?.IsProject == true && CurrentSolution.Projects.Count >= 2`; show `AddProjectReferenceDialog` (Task 14) to collect target names; then `await ApplyProjectReferencesAsync(SelectedNode.Name, targets)`.
  - Extract the testable core:
    ```csharp
    internal async Task ApplyProjectReferencesAsync(string fromName, IEnumerable<string> targets)
    {
        foreach (var to in targets)
        {
            try { _solutionService.AddProjectReference(fromName, to); }        // validates cycles
            catch (InvalidOperationException ex) { await _dialogService.ShowMessageAsync("Add Reference", ex.Message); continue; }

            // .blproj dual-write (relative path from the 'from' project dir to the 'to' .blproj)
            var from = _solutionService.CurrentSolution!.GetProject(fromName)!;
            var toProj = _solutionService.CurrentSolution.GetProject(to)!;
            var fromDir = Path.GetDirectoryName(from.GetFullPath(_solutionService.CurrentSolution.SolutionDirectory))!;
            var toBlproj = toProj.GetFullPath(_solutionService.CurrentSolution.SolutionDirectory);
            BlprojReferenceWriter.AddReference(
                from.GetFullPath(_solutionService.CurrentSolution.SolutionDirectory),
                Path.GetRelativePath(fromDir, toBlproj));
        }
        await _solutionService.SaveSolutionAsync();     // <-- ISolutionService, NOT _projectService (that no-ops)
        SolutionExplorer_Reload();                       // refresh tree from the updated model
    }
    ```
  - **Persistence fix:** change the `_projectService.SaveSolutionAsync()` calls in `SetAsStartupProjectAsync` (~541) and `RemoveFromSolutionAsync` (~602) to `_solutionService.SaveSolutionAsync()` (the `_projectService` variant no-ops because `_projectService.CurrentSolution` is null on the live path).

- [ ] **Step 4: Run — verify pass** (incl. a regression test that Set-as-Startup now persists on a `.blsln` reload).
- [ ] **Step 5: Commit** — `feat(explorer): Add Project Reference command + fix no-op solution saves`.

---

## Task 14: Reference picker dialog + project-node context menu

**Files:**
- Create: `VisualGameStudio.Shell/ViewModels/Dialogs/AddProjectReferenceViewModel.cs` (checkbox list of sibling projects — reuse `CheckableItem`; if it was deleted with the old dialog in Task 10, first move `CheckableItem` to a shared file, e.g. `ViewModels/Dialogs/CheckableItem.cs`)
- Create: `VisualGameStudio.Shell/Views/Dialogs/AddProjectReferenceDialog.axaml(.cs)`
- Modify: `VisualGameStudio.Shell/Views/Panels/SolutionExplorerView.axaml` (add a project-node context section)

> View layer — build + smoke verified; the picker VM's selection logic can get a small unit test.

- [ ] **Step 1:** `AddProjectReferenceViewModel`: takes the sibling project names, exposes `ObservableCollection<CheckableItem>`, `SelectedNames`, OK/Cancel. Small test: checking two items yields both in `SelectedNames`.
- [ ] **Step 2:** `AddProjectReferenceDialog.axaml`: checkbox list + OK/Cancel.
- [ ] **Step 3:** In `SolutionExplorerView.axaml`, add a **project-node** context section. Because today's single menu isn't node-type gated, gate the new items' `IsVisible`/`IsEnabled` on the selected node being a project (bind to a VM `SelectedIsProject` flag). Add "Add Project Reference…" (→ `AddProjectReferenceCommand`) and **surface the currently-orphaned** "Set as Startup Project", "Remove from Solution", "Build Project" commands here too.
- [ ] **Step 4:** `dotnet clean` + build. Expected: succeeds.
- [ ] **Step 5: Commit** — `feat(explorer): reference picker + project-node context menu`.

---

## Task 15 (optional, gated on Task 11 Step 0b): teach `ProjectSerializer` to round-trip `<ProjectReference>`

**Files:**
- Modify: `VisualGameStudio.ProjectSystem/Serialization/ProjectSerializer.cs` (read ~165-176; write ~279-287)
- Test: `VisualGameStudio.Tests/…/ProjectSerializerReferenceTests.cs`

Only do this if Step 0b showed a full-file round-trip is lossless. Teach `LoadAsync` to read `<ProjectReference Include>` into `BasicLangProject.References` with `IsProjectReference=true`, and `SaveAsync` to emit those as `<ProjectReference Include="{Path}">`. Closes the latent erase hazard (a future serializer save dropping references written in Task 13). TDD: load-modify-save preserves both `<Reference>` and `<ProjectReference>`.

- [ ] Standard TDD steps + commit `fix(projsys): ProjectSerializer round-trips <ProjectReference>`.

---

## Task 16: Definition of done — suite, greps, manual smoke

**Files:** none (verification) — follow @superpowers:verification-before-completion

- [ ] **Step 1:** Full suite (redirect to file). Expected: baseline counts hold (no new failures beyond the known BL6009 flake / 2 skips).
- [ ] **Step 2: Grep guards:**
  - No `CloseFolderCommand` / `"Close Folder"` remain (`git grep -n "CloseFolder"`).
  - No references to `AddProjectToSolutionViewModel` / `AddProjectToSolutionDialog`.
  - No `_projectService.SaveSolutionAsync` left in `SolutionExplorerViewModel.cs`.
- [ ] **Step 3: Manual smoke in the built IDE** (`IDE/VisualGameStudio.exe` after robocopy refresh, or run the Shell):
  1. Open a standalone project → **Close Project** enabled → closes it; item disabled when a solution is open.
  2. **New Solution** → name/location/git → template/backend (try **LLVM** and **C++**) → project step (Location locked, "Create solution") → solution opens in Explorer with the first project; source file opens.
  3. New Solution → on the template page press **Back** → returns to solution-details with values intact.
  4. **Add New Project to Solution** on the open solution → new project appears; `.blproj` written once; `.gitignore` untouched.
  5. Solution Explorer → right-click a project → **Add Project Reference…** → pick another project → reference persists (reopen solution; check `B.blproj` has `<ProjectReference>`); adding a cycle shows the error.
  6. Right-click a project → **Set as Startup** persists across reload (was a silent no-op before).
- [ ] **Step 4:** Finish the branch per @superpowers:finishing-a-development-branch (ff-merge to master, refresh `IDE/` binaries, push).

---

## Notes & risks (carried from the spec)

- **Solution switch replaces without a save prompt** — matches the existing Open Solution behavior (out of scope to change here).
- **Half-created solution on first-project failure** — the `.blsln` exists but isn't loaded; the wizard surfaces the error (Task 1's `ToProjectResult`).
- **IDE `ProjectSerializer` erase hazard** — mitigated permanently only by Task 15; otherwise references are durable until some future serializer re-save of that `.blproj`.
- **`IProjectService` vs `ISolutionService` duplication** — untouched (non-goal); Task 13 deliberately uses `ISolutionService` for persistence.
