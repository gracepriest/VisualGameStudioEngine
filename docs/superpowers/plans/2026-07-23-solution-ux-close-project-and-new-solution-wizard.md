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

- [ ] **Step 1: Write failing tests** — add to the existing fixture. The real helper is `NewVm(out FakeTemplateService svc, ToolchainAvailability? toolchains = null)` (`NewProjectWizardViewModelTests.cs:45`) — use it, not `MakeVm()`:

```csharp
[Test]
public void IsLocationLocked_only_in_solution_modes()
{
    var vm = NewVm(out _);
    Assert.That(vm.IsLocationLocked, Is.False);
    vm.Mode = WizardMode.NewSolution;   Assert.That(vm.IsLocationLocked, Is.True);
    vm.Mode = WizardMode.AddToSolution; Assert.That(vm.IsLocationLocked, Is.True);
}

[Test]
public void BuildCreateOptions_reflects_full_selection()
{
    var vm = NewVm(out _);
    vm.ProjectName = "App"; vm.Location = @"C:\x";
    // (select a known backend/template via the existing fixture seams; for a C++ backend set CppStandard)
    var o = vm.BuildCreateOptions();
    Assert.That(o.Name, Is.EqualTo("App"));
    Assert.That(o.Location, Is.EqualTo(@"C:\x"));
    Assert.That(o.SolutionType, Is.EqualTo(vm.SelectedBackend!.SolutionType));
    Assert.That(o.Template, Is.EqualTo(vm.SelectedTemplate));
    Assert.That(o.TargetFramework, Is.EqualTo(vm.TargetFramework));
    Assert.That(o.Namespace, Is.EqualTo(vm.CustomNamespace));
    // C++ path: CppStandard/CppToolchain travel only when language==Cpp (assert in a C++ variant of this test)
}

[Test]
public async Task CreateProject_routes_through_FinishAction()
{
    var vm = NewVm(out _);
    vm.ProjectName = "App"; vm.Location = @"C:\x";
    CreateProjectOptions? seen = null;
    vm.FinishAction = o => { seen = o; return Task.FromResult(new ProjectCreationResult { Success = true, ProjectPath = "p" }); };
    ProjectCreationResult? raised = null;
    vm.ProjectCreated += (_, r) => raised = r;

    await vm.CreateProjectCommand.ExecuteAsync(null);

    Assert.That(seen, Is.Not.Null);                 // delegate received the built options
    Assert.That(raised?.ProjectPath, Is.EqualTo("p"));
}

[Test]
public void Backends_keep_LLVM_and_MSIL_selectable()   // user requirement: keep them as forward-looking options
{
    var vm = NewVm(out _);                             // BasicLang language by default
    var names = vm.Backends.Select(b => b.Name).ToList();
    Assert.That(names, Does.Contain("MSIL"));
    Assert.That(names.Any(n => n.Contains("LLVM")), Is.True);
}
```

> **Scope note (LLVM/MSIL):** the standing rule (`MEMORY.md` → `backend-scope-csharp-cpp`) is do-not-test the LLVM/MSIL **backends** (codegen/build). The user separately requires they stay **selectable** in the wizard. This plan honors both: it proves "kept selectable" via the option-list test above and the pure `SolutionWizardMapper` passthrough (Task 1 uses `SolutionTypes.Llvm`) — neither compiles anything — and it scaffolds/loads only the in-scope **C# and C++** backends in the Task 7 E2E.

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
    public bool IsNewSolutionMode => Mode == WizardMode.NewSolution;   // Task 5 binds the Back button to this
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

- [ ] **Step 1: Write failing tests as SOURCE-TEXT GUARDS.** `MainWindowViewModel` is DI-only (40+ ctor deps) and is **never constructed in tests** — the repo convention is source guards (see `VisualGameStudio.Tests/MwvmDebuggerOverrideSourceGuardTests.cs`, which reads the file text via a `ReadMainWindowViewModelSource()`/`ExtractMethodBody()` helper). `CommandPaletteViewModel.RegisterCommands` also requires a real MWVM, so guard it by source text too. The actual *behavior* (close + title/status reset, unsaved-changes prompt) is verified by the Task 16 manual smoke.

```csharp
[Test]
public void MainWindowViewModel_renames_close_and_wires_HasProjectOpen()
{
    var src = ReadSource("VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs");
    Assert.That(src, Does.Contain("CloseProjectAsync"));
    Assert.That(src, Does.Not.Contain("CloseFolderAsync"));     // renamed, not duplicated
    Assert.That(src, Does.Contain("HasProjectOpen"));
    Assert.That(src, Does.Contain("RecomputeHasProjectOpen"));
}

[Test]
public void CommandPalette_targets_CloseProject_not_CloseFolder()
{
    var src = ReadSource("VisualGameStudio.Shell/ViewModels/Dialogs/CommandPaletteViewModel.cs");
    Assert.That(src, Does.Contain("\"Close Project\"").And.Contain("CloseProjectCommand"));
    Assert.That(src, Does.Not.Contain("CloseFolderCommand"));
    Assert.That(src, Does.Not.Contain("\"Close Folder\""));
}
```

(`ReadSource` = a small repo-root file reader mirroring `ReadMainWindowViewModelSource()` in `MwvmDebuggerOverrideSourceGuardTests.cs`. Also: rename/repoint any pre-existing test that references `CloseFolderCommand`/`CloseFolderAsync`.)

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
[Test] public void Existing_nonempty_target_dir_blocks_confirm()
{
    var dir = Path.Combine(Path.GetTempPath(), "vgs_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(dir, "MySln"));
    File.WriteAllText(Path.Combine(dir, "MySln", "x.txt"), "x");   // target already populated
    var vm = new NewSolutionViewModel { SolutionName = "MySln", Location = dir };
    Assert.That(vm.CanConfirm, Is.False);
    Assert.That(vm.ErrorMessage, Does.Contain("exists"));
}
[Test] public void InitializeGit_defaults_true() => Assert.That(new NewSolutionViewModel().InitializeGit, Is.True);
```

- [ ] **Step 2: Run — verify fail.**

- [ ] **Step 3: Implement** — mirror `AddProjectToSolutionViewModel`'s structure (CommunityToolkit `[ObservableProperty]`, `Create`/`Cancel`/`BrowseLocation` commands, `DialogResult`, `CloseDialog` action). Three **hard-blocks** on `CanConfirm` (matching the spec): (a) non-empty name; (b) name contains no `Path.GetInvalidFileNameChars()`; (c) the target `Path.Combine(Location, SolutionName)` is not an existing non-empty directory (`Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any()` → block with an `ErrorMessage` containing "exists"). Also surface the (non-blocking) MSBuild special-char note via `BasicLang.Compiler.ProjectSystem.MSBuildText.FindSpecialCharacters`. Expose `SolutionName`, `Location`, `InitializeGit=true`, `ErrorMessage`, `SolutionFilePreview` (=`Path.Combine(Location, SolutionName, SolutionName + ".blsln")` when both set), and `CanConfirm`. `Confirm()` sets `DialogResult=true` + `CloseDialog?.Invoke()`.

- [ ] **Step 4: Run — verify pass.**
- [ ] **Step 5: Commit** — `feat(wizard): NewSolutionViewModel (name/location/git + validation)`.

---

## Task 5: `NewSolutionView` window + `NewProjectSelectView` Outcome/Back

**Files:**
- Create: `VisualGameStudio.Shell/Views/Dialogs/NewSolutionView.axaml` (+ `.axaml.cs`)
- Modify: `VisualGameStudio.Shell/Views/Dialogs/NewProjectSelectView.axaml(.cs)`

> View/AXAML layer — verified by build + the Task 16 manual smoke, not unit tests (Avalonia windows aren't headless-tested in this repo).

- [ ] **Step 1:** Author `NewSolutionView.axaml` bound to `NewSolutionViewModel`: name TextBox, location TextBox + Browse (code-behind folder picker, mirroring the existing configure view's Browse), an "Initialize Git repository" CheckBox, the `SolutionFilePreview` label, `ErrorMessage`, and Cancel/Next buttons (`Next` enabled on `CanConfirm`). Code-behind exposes the confirmed `(SolutionName, Location, InitializeGit)` or a cancel.
- [ ] **Step 2:** In `NewProjectSelectView.axaml.cs`, expose two **public** members the host (Task 6) reads after `ShowDialog` (today it only keeps a private `_result` and returns it via `Close(_result)`):
  ```csharp
  public enum WizardOutcome { Created, Cancelled, Back }   // or reuse NewProjectConfigureView.WizardOutcome
  public WizardOutcome Outcome { get; private set; }
  public ProjectCreationResult? Result { get; private set; }   // set alongside _result on the Created path
  ```
  Set `Outcome = Created`/`Cancelled` on the existing close paths (and assign `Result`). Add a **Back** button bound `IsVisible="{Binding IsNewSolutionMode}"` (the flag added in Task 2) whose handler sets `Outcome = Back` and closes.
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
  4. On a successful result, use the **same two-call sequence** the existing `OpenSolutionAsync` uses (`MainWindowViewModel.cs:6754-6758`) — there is no single `LoadSolutionAsync` host helper:
     ```csharp
     var solution = await _solutionService.LoadSolutionAsync(result.SolutionPath!);
     SolutionExplorer.LoadSolution(solution);
     _recentProjectsService.AddRecentProject(result.SolutionPath!, solution.SolutionName);
     foreach (var f in result.FilesToOpen) await OpenFileAsync(f);
     ```
     On failure, the wizard already surfaced the error; just return.
- [ ] **Step 2:** `dotnet clean` + build. Expected: succeeds.
- [ ] **Step 3: Commit** — `feat(ide): guided New Solution wizard orchestration`.

---

## Task 7: New Solution end-to-end (real `ProjectTemplateService`)

**Files:**
- Test: `VisualGameStudio.Tests/Services/NewSolutionEndToEndTests.cs`

Use **Moq** for `IGitService` (it's already a test dependency; `IGitService` is a ~70-member interface — a hand-rolled fake is needless boilerplate). Observe the init count with `.Verify(g => g.InitRepositoryAsync(It.IsAny<string>()), Times.Once/Never)`. Scaffold **only the in-scope C# and C++ backends** (per the LLVM/MSIL scope note in Task 2 — LLVM/MSIL "kept selectable" is already proven there without compiling anything).

- [ ] **Step 1: Write failing tests** — construct the real `ProjectTemplateService` with the git mock (verify its ctor accepts `IGitService?`) in a temp dir:

```csharp
[TestCase("dotnet")]   // in-scope C#
public async Task NewSolution_creates_loadable_solution_with_first_project(string backendId)
{
    var git = new Mock<IGitService>();
    var svc = new ProjectTemplateService(git.Object);   // adjust to real ctor
    var first = new CreateProjectOptions {
        Name = "App", SolutionType = SolutionTypes.All.First(t => t.Id == backendId),
        Template = ProjectTemplates.ConsoleApp };
    var solOpts = SolutionWizardMapper.BuildSolutionOptions("MySln", tempDir, initGit: true, first);

    var sr = await svc.CreateSolutionAsync(solOpts);
    Assert.That(sr.Success, Is.True);

    var solution = await new SolutionService().LoadSolutionAsync(sr.SolutionPath!);
    Assert.That(solution.Projects.Select(p => p.Name), Does.Contain("App"));
    Assert.That(File.Exists(solution.Projects[0].GetFullPath(solution.SolutionDirectory)), Is.True);
    git.Verify(g => g.InitRepositoryAsync(It.IsAny<string>()), Times.Once);   // solution-level git only
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
    // initGit:false -> git.Verify(g => g.InitRepositoryAsync(It.IsAny<string>()), Times.Never)
}
```

- [ ] **Step 2: Run — verify fail**, then fix construction (verify the real `ProjectTemplateService(IGitService?)` ctor).
- [ ] **Step 3: Run — verify pass.**
- [ ] **Step 4: Commit** — `test(solution): New Solution E2E (C#/C++, git-init count via Moq)`.

---

## Task 8: Feature C — unify "Add Project to Solution"

**Files:**
- Modify: `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs` (`AddNewProjectToSolutionAsync` ~6868-6953)
- Test: `VisualGameStudio.Tests/Services/AddProjectToSolutionEndToEndTests.cs`

- [ ] **Step 1: Write failing E2E** — against real `ProjectTemplateService` + `SolutionService` + a Moq `IGitService` in a temp dir: create a solution with one project, then add a second via `BuildAddToSolutionOptions` + `CreateProjectAsync`:

```csharp
[Test]
public async Task AddProject_writes_blproj_once_and_registers_without_git_reinit()
{
    // arrange: an on-disk solution with project "A" (via CreateSolutionAsync)
    git.Invocations.Clear();                                   // ignore the solution-create init
    var opts = new CreateProjectOptions { Name = "B", SolutionType = SolutionTypes.DotNet, Template = ProjectTemplates.ConsoleApp };
    SolutionWizardMapper.BuildAddToSolutionOptions(opts, loadedSolution);
    var r = await svc.CreateProjectAsync(opts);

    Assert.That(r.Success, Is.True);
    git.Verify(g => g.InitRepositoryAsync(It.IsAny<string>()), Times.Never);   // add path never re-inits git
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
  4. Show `NewProjectSelectView`; on a successful result, reload via the two-call sequence (mirroring `OpenSolutionAsync`):
     ```csharp
     var solution = await _solutionService.LoadSolutionAsync(_solutionService.CurrentSolution!.FilePath);
     SolutionExplorer.LoadSolution(solution);
     foreach (var f in result.FilesToOpen) await OpenFileAsync(f);
     ```
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

- [ ] **Step 1: Move `CheckableItem` out first (unconditional).** `CheckableItem` is declared inside `AddProjectToSolutionViewModel.cs:218-228` and the Task 14 reference picker reuses it. Move it to its own shared file `VisualGameStudio.Shell/ViewModels/Dialogs/CheckableItem.cs` (same namespace) **before** deleting anything, so the deletion can't strand it.
- [ ] **Step 2:** Grep the solution for `AddProjectToSolution` — confirm only the now-rewritten (Task 8) `AddNewProjectToSolutionAsync` referenced it, and it no longer does. Delete `AddProjectToSolutionViewModel.cs` + `AddProjectToSolutionDialog.axaml(.cs)`.
- [ ] **Step 3:** `dotnet clean` + build. Expected: succeeds (no dangling references).
- [ ] **Step 4: Commit** — `refactor(ide): remove superseded AddProjectToSolution dialog (CheckableItem hoisted)`.

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

Unlike `MainWindowViewModel`, **`SolutionExplorerViewModel` is constructable in tests** (ctor: `IProjectService, IFileService, IDialogService, IGitService?, IWorkspaceService?` + the new `ISolutionService`). Its ctor subscribes to five `IProjectService` events (`ProjectOpened/ProjectClosed/ProjectChanged/SolutionOpened/SolutionClosed`, `SolutionExplorerViewModel.cs:108-112`), so the fakes must expose them or construction throws.

- [ ] **Step 1a: Build the fixture with Moq** — the existing `SolutionExplorerViewModelTests.cs` already uses Moq for service doubles; align with it (do NOT hand-roll fakes):
  - `var dialog = new Mock<IDialogService>();` and capture the surfaced text via `.Callback` on the real `ShowMessageAsync` overload → `lastMessage`.
  - `new Mock<IProjectService>()`, `new Mock<IFileService>()`, `new Mock<IWorkspaceService>()` (Moq auto-satisfies the five events the ctor subscribes to).
  - Use a **real** `SolutionService` for the new `ISolutionService` param (needed for genuine cycle validation + on-disk persistence). Seed a temp solution with projects A, B, C via the real `ProjectTemplateService`, then `LoadSolutionAsync` into that `SolutionService`.

- [ ] **Step 1b: Write failing tests** against the **public** apply method (keeps UI out) and the enablement predicate:

```csharp
[Test]
public async Task ApplyReferences_dual_writes_blsln_and_blproj()
{
    await vm.ApplyProjectReferencesAsync("B", new[] { "A" });                 // B references A
    var reloaded = await new SolutionService().LoadSolutionAsync(slnPath);
    Assert.That(reloaded.GetProject("B")!.ProjectReferences, Does.Contain("A"));   // .blsln persisted on disk
    var bXml = XDocument.Load(bBlprojPath);
    Assert.That(bXml.Descendants("ProjectReference").Any(e => ((string)e.Attribute("Include")!).Contains("A.blproj")), Is.True); // .blproj persisted
}

[Test]
public async Task ApplyReferences_rejects_cycle_with_message()
{
    await vm.ApplyProjectReferencesAsync("B", new[] { "A" });
    await vm.ApplyProjectReferencesAsync("A", new[] { "B" });                 // would cycle
    Assert.That(recordingDialog.LastMessage, Does.Contain("circular"));       // surfaced ex.Message
    var reloadedA = (await new SolutionService().LoadSolutionAsync(slnPath)).GetProject("A")!;
    Assert.That(reloadedA.ProjectReferences, Does.Not.Contain("B"));
}

[Test]
public void CanAddProjectReference_gated_on_two_projects_and_project_node()
{
    // single-project solution loaded, project node selected -> false
    Assert.That(vm.CanAddProjectReference, Is.False);
    // load the 3-project solution, select a project node -> true
    SelectProjectNode(vm, "B");   Assert.That(vm.CanAddProjectReference, Is.True);
    // select a non-project (file) node -> false
    SelectFileNode(vm);           Assert.That(vm.CanAddProjectReference, Is.False);
}
```

- [ ] **Step 2: Run — verify fail.**
- [ ] **Step 3: Implement**
  - Add `ISolutionService` to the ctor + field; register the dependency in `ServiceConfiguration.cs` (**verify** the explorer VM resolves the *same* singleton `MainWindowViewModel` uses — both are `AddSingleton`).
  - Enablement predicate + command:
    ```csharp
    public bool CanAddProjectReference =>
        _solutionService.HasSolution && SelectedNode?.IsProject == true
        && (_solutionService.CurrentSolution?.Projects.Count ?? 0) >= 2;

    [RelayCommand(CanExecute = nameof(CanAddProjectReference))]
    private async Task AddProjectReferenceAsync()
    {
        // show AddProjectReferenceDialog (Task 14) to collect target names, then:
        await ApplyProjectReferencesAsync(SelectedNode!.Name, selectedTargets);
    }
    ```
    In the `SelectedNode` change handler, call `AddProjectReferenceCommand.NotifyCanExecuteChanged();` so the menu enables/disables live.
  - The testable core is **`public`** (Shell grants no `InternalsVisibleTo` to the test project — repo convention is public seams, never `internal`+IVT):
    ```csharp
    public async Task ApplyProjectReferencesAsync(string fromName, IEnumerable<string> targets)
    {
        foreach (var to in targets)
        {
            try { _solutionService.AddProjectReference(fromName, to); }        // validates cycles
            catch (InvalidOperationException ex) { await _dialogService.ShowMessageAsync("Add Reference", ex.Message); continue; }

            // .blproj dual-write (relative path from the 'from' project dir to the 'to' .blproj)
            var sol = _solutionService.CurrentSolution!;
            var from = sol.GetProject(fromName)!;
            var toProj = sol.GetProject(to)!;
            var fromBlproj = from.GetFullPath(sol.SolutionDirectory);
            var fromDir = Path.GetDirectoryName(fromBlproj)!;
            var toBlproj = toProj.GetFullPath(sol.SolutionDirectory);
            BlprojReferenceWriter.AddReference(fromBlproj, Path.GetRelativePath(fromDir, toBlproj));
        }
        await _solutionService.SaveSolutionAsync();     // <-- ISolutionService, NOT _projectService (that no-ops)
        LoadSolution(_solutionService.CurrentSolution!); // this VM's own reload method (used by MWVM at :6755)
    }
    ```
  - **Update the one existing construction site:** adding `ISolutionService` as a **required** ctor param breaks `SolutionExplorerViewModelTests.cs:43` (`new SolutionExplorerViewModel(proj, file, dialog)` → CS7036 — it relies on the two trailing optionals). Add the new arg there (a real `SolutionService` or a `Mock<ISolutionService>`). Grep confirms this is the **only** non-DI construction site.
  - **Persistence fix — repoint ALL FOUR sites** (`SolutionExplorerViewModel.cs` lines **435, 481, 541, 602**) from `_projectService.SaveSolutionAsync()` to `_solutionService.SaveSolutionAsync()`. All four carry the identical no-op bug (`ProjectService.SaveSolutionAsync` early-returns when `_projectService.CurrentSolution` is null, which it always is on the live path — the solution lives on `_solutionService`). This is what the Task 16 DoD grep (`_projectService.SaveSolutionAsync` → zero hits) enforces.

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
