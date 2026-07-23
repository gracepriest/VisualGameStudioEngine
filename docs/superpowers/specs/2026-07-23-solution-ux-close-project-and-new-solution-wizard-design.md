# Solution UX: Close Project + guided New Solution wizard — design

**Date:** 2026-07-23
**Status:** Draft for review (r3 — adds Feature D reference command per user decision)
**Area:** IDE (`VisualGameStudio.Shell` + `VisualGameStudio.ProjectSystem`)

## Summary

Four linked changes to how the IDE's File menu and Solution Explorer handle
projects and solutions:

1. **Close Project** — add a File-menu item that closes the currently-open
   standalone project. The close logic already exists (reachable only via the
   Command Palette today).
2. **Guided New Solution wizard** — replace the bare two-dialog "New Solution"
   with a wizard that walks the user through naming the solution *and* adding a
   first project, reusing the existing New Project pages.
3. **Unify "Add Project to Solution"** — point the solution's add-project flow at
   the same reused pages and the same creation service, retiring a divergent
   dialog (and its stale backend list) and a duplicate `.blproj` writer.
4. **Solution Explorer "Add Project Reference"** — a project-scoped command that
   restores (and generalizes) the reference-setting capability retired with the
   old dialog.

Features 1-3 lean on machinery that already exists; the net new UI is two small
windows (solution details; reference picker).

## Goals

- A standalone project can be closed from the File menu (today it is only
  reachable via the Command Palette).
- "New Solution" produces a solution **with a first project already in it**,
  chosen through the same template/backend UI as New Project.
- The two "add a new project" code paths (New Project wizard vs. Add Project to
  Solution) stop diverging — one project-config UI, one creation path.
- LLVM and MSIL remain selectable everywhere they are today (they are
  forward-looking placeholders the user wants kept, not removed).

## Non-goals

- **Consolidating `IProjectService` and `ISolutionService`.** Both declare
  `CreateSolutionAsync`/`CloseSolutionAsync`
  ([IProjectService.cs:61,80](../../VisualGameStudio.Core/Abstractions/Services/IProjectService.cs),
  [ISolutionService.cs:33,43](../../VisualGameStudio.Core/Abstractions/Services/ISolutionService.cs));
  the shell only uses `ISolutionService` for solution lifecycle. Left for later.
- **Changing that a standalone New Project also writes a `.blsln`.** For templates
  with `CreateSolution=true`, `ProjectTemplateService.CreateProjectAsync` already
  emits a solution file next to the project
  ([ProjectTemplateService.cs:81](../../VisualGameStudio.ProjectSystem/Services/ProjectTemplateService.cs));
  the IDE ignores it (opens the `.blproj`). Not touched.
- **Unsaved-changes prompt on solution switching.** New Solution loads the new
  solution and replaces whatever is open, exactly as the existing "Open Solution"
  command does today ([MainWindowViewModel.cs:6737-6772](../../VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs),
  which also does not prompt). Adding save-on-switch prompts is a separate,
  pre-existing concern and is out of scope here.
- *(Resolved.)* The project-reference capability retired with the old dialog is
  restored — and generalized — as a Solution Explorer command; see **Feature D**.

## Background — how solutions work today

There are effectively **two solution subsystems**, both writing `.blsln` through
the same `SolutionSerializer`:

- **`IProjectTemplateService`** (`ProjectTemplateService`) — *creation*. Scaffolds
  projects from templates, and already:
  - writes a single-project `.blsln` for a standalone project
    (`CreateSolutionFileAsync`, [:1127](../../VisualGameStudio.ProjectSystem/Services/ProjectTemplateService.cs));
  - has `CreateSolutionAsync(CreateSolutionOptions)` that creates a solution and
    scaffolds each `InitialProjects` entry — forcing per-project `Location =
    solutionDir`, `AddToExistingSolution = true`, `CreateSolutionFolder = false`,
    `CreateGitRepository = false`, registering each into the `.blsln`, and
    initializing git once at the solution level
    ([:112-153](../../VisualGameStudio.ProjectSystem/Services/ProjectTemplateService.cs)).
    **Caveat:** it only accumulates `ProjectPaths` for projects that succeed and
    unconditionally returns `Success = true` ([:141,155](../../VisualGameStudio.ProjectSystem/Services/ProjectTemplateService.cs)),
    so a failed first project yields a *projectless* solution reported as success
    — handled explicitly by the adapter below.
  - has `CreateProjectAsync` with `AddToExistingSolution`/`ExistingSolutionPath`
    that load-modify-saves a project into an existing `.blsln`, dedup-aware
    ([:86-90](../../VisualGameStudio.ProjectSystem/Services/ProjectTemplateService.cs), [:1161](../../VisualGameStudio.ProjectSystem/Services/ProjectTemplateService.cs)).
    It initializes git on `solutionDir` when `CreateGitRepository` is true
    ([:93-97](../../VisualGameStudio.ProjectSystem/Services/ProjectTemplateService.cs)).
- **`ISolutionService`** (`SolutionService`) — *session management*. Holds the
  currently-open `BasicLangSolution`, drives the Solution Explorer, exposes
  runtime add/remove/reference/build-order. Its own `CreateSolutionAsync(name,
  dir)` writes an **empty** solution; `AddNewProjectAsync` writes a *minimal*
  `.blproj`.

Current File-menu flows are inconsistent about which subsystem they use:

| Command | Today |
|---|---|
| New Project | `NewProjectWizardViewModel` → `ProjectTemplateService.CreateProjectAsync`; opened as a project |
| New Solution | name box + folder box → `ISolutionService.CreateSolutionAsync` → **empty** solution ([MainWindowViewModel.cs:6775](../../VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs)) |
| Add New Project to Solution | bespoke `AddProjectToSolutionViewModel` dialog → `ISolutionService.AddNewProjectAsync` (minimal `.blproj`, [SolutionService.cs:104-116](../../VisualGameStudio.ProjectSystem/Services/SolutionService.cs)) **then the host command overwrites it** with a richer `.blproj` ([MainWindowViewModel.cs:6931](../../VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs)) — the file is written twice |
| Close Project | reachable only via Command Palette "Close Folder" → `CloseFolderCommand` → `_projectService.CloseProjectAsync()` ([ViewModels/Dialogs/CommandPaletteViewModel.cs:235](../../VisualGameStudio.Shell/ViewModels/Dialogs/CommandPaletteViewModel.cs), [MainWindowViewModel.cs:6716-6728](../../VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs)); **absent from the File menu** |

`AddProjectToSolutionViewModel` also hardcodes backends `CSharp/LLVM/MSIL/Cpp`
and templates including `WPF`
([AddProjectToSolutionViewModel.cs:28-36](../../VisualGameStudio.Shell/ViewModels/Dialogs/AddProjectToSolutionViewModel.cs)),
drifting from the wizard's backend list
([NewProjectWizardViewModel.cs:175-184](../../VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectWizardViewModel.cs)).

**Design principle adopted here:** *creation* always goes through
`IProjectTemplateService`; *session management* stays in `ISolutionService`, which
loads the produced `.blsln`.

## The reused pages

The New Project wizard is a two-window flow coordinated by the view code-behind,
but the **host (`MainWindowViewModel`) constructs the VM** and reads the result
via `ShowDialog` ([MainWindowViewModel.cs:2005-2008](../../VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs)):

- Window 1 `NewProjectSelectView(vm)` — template gallery + backend/language;
  raises `NextRequested` → opens window 2 as a modal; today has only Cancel/Next
  (no Back) and closes with a `ProjectCreationResult?`
  ([NewProjectSelectView.axaml.cs:27-64](../../VisualGameStudio.Shell/Views/Dialogs/NewProjectSelectView.axaml.cs)).
- Window 2 `NewProjectConfigureView(vm)` — name, location, options; exposes an
  `Outcome` (`Created`/`Cancelled`/`Back`) + `ProjectCreationResult Result`.
- The VM's `CreateProjectAsync` command builds a `CreateProjectOptions`
  ([:387-404](../../VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectWizardViewModel.cs))
  and calls `_templateService.CreateProjectAsync`, raising `ProjectCreated`
  (`EventHandler<ProjectCreationResult>`, [:128,409](../../VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectWizardViewModel.cs)).

`SelectedBackend.SolutionType` (`dotnet`/`msil`/`native`/`llvm`/`cpp`) and the C++
`ToolchainId`/`CppStandard` are produced here, so reusing these pages preserves
the full backend set — **including LLVM and MSIL** — automatically.

## Feature A — Close Project

**Menu.** Add above "Close Solution"
([MainWindow.axaml:178](../../VisualGameStudio.Shell/Views/MainWindow.axaml)):

```xml
<MenuItem Header="Close _Project" Command="{Binding CloseProjectCommand}"
          IsEnabled="{Binding HasProjectOpen}"/>
```

**Command.** Rename `CloseFolderAsync` → `CloseProjectAsync` (`CloseProjectCommand`),
body verbatim (unsaved-changes prompt + `SaveAllAsync` +
`_projectService.CloseProjectAsync()` + title/status reset,
[MainWindowViewModel.cs:6715-6732](../../VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs)).
The rename changes the generated command name, so the sole other reference — the
Command Palette — is repointed in the same change:

- [ViewModels/Dialogs/CommandPaletteViewModel.cs:235](../../VisualGameStudio.Shell/ViewModels/Dialogs/CommandPaletteViewModel.cs):
  `("File", "Close Folder", … vm.CloseFolderCommand …)` →
  `("File", "Close Project", … vm.CloseProjectCommand …)`.

**Enablement.** Add observable `HasProjectOpen` (mirroring `HasSolutionOpen`),
toggled from `IProjectService.ProjectOpened`/`ProjectClosed` (already subscribed at
[MainWindowViewModel.cs:545-546](../../VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs)).
Enabled only for a **standalone** project
(`_projectService.CurrentProject != null && !_solutionService.HasSolution`).

## Feature B — guided New Solution wizard

### Orchestration (host-owned, concrete)

`MainWindowViewModel.NewSolutionAsync` owns the flow as a small window loop.
**No open solution/project is closed up front** (see Non-goals); the new solution
simply replaces what is open on success, as Open Solution does.

1. **Window 1 — Solution details.** Show `NewSolutionView` bound to a new
   `NewSolutionViewModel` exposing `SolutionName`, `Location` (Browse), and an
   `InitializeGit` checkbox (default true, matching New Project's git option).
   **Validation (hard-block on Next):** non-empty name; name contains no
   `Path.GetInvalidFileNameChars()` (the same hard check
   `AddProjectToSolutionViewModel.Create` uses, [:177-178](../../VisualGameStudio.Shell/ViewModels/Dialogs/AddProjectToSolutionViewModel.cs));
   `Location/SolutionName` is not an existing non-empty directory. Additionally
   surface the wizard's *non-blocking* MSBuild special-char warning
   (`MSBuildText.FindSpecialCharacters`, [NewProjectWizardViewModel.cs:335](../../VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectWizardViewModel.cs)).
   Shows the computed `…/SolutionName/SolutionName.blsln`. Returns
   `Confirmed(name, location, initGit)` or `Cancelled`.
2. **Windows 2-3 — reused select→configure.** Build a `NewProjectWizardViewModel`
   with `Mode = WizardMode.NewSolution`, preset `Location = <solutionDir>`
   (locked), and a **finish delegate** (below). Show `NewProjectSelectView`
   (chains to `NewProjectConfigureView` as today).
3. **Back / Cancel.** `NewProjectSelectView` gains a public `Outcome`
   (`Created`/`Cancelled`/`Back`) mirroring `NewProjectConfigureView.Outcome`; the
   host reads it after `ShowDialog` (no longer relying on a null result to mean
   two things). A Back button (visible only when `Mode == NewSolution`) sets
   `Outcome = Back`; the host loops back to window 1 with `NewSolutionViewModel`
   values preserved. Cancel aborts.
4. **Finish.** On `Outcome == Created` with a successful `ProjectCreationResult`:
   ```csharp
   var solution = await _solutionService.LoadSolutionAsync(result.SolutionPath!);
   SolutionExplorer.LoadSolution(solution);
   _recentProjectsService.AddRecentProject(result.SolutionPath!, solution.SolutionName);
   foreach (var f in result.FilesToOpen) await OpenFileAsync(f);   // existing open path
   ```
   On a failed result the wizard shows the error and stays open (the VM's existing
   `HasError` path); nothing is loaded.

### The finish delegate + result adapter

`NewProjectWizardViewModel` gains an injectable finish action, defaulted **in the
constructor** (a property initializer can't reference the instance field —
CS0236):

```csharp
public Func<CreateProjectOptions, Task<ProjectCreationResult>> FinishAction { get; set; }
// in ctor, after _templateService is assigned:
FinishAction ??= opts => _templateService.CreateProjectAsync(opts);
```

The VM's `CreateProjectAsync` command becomes
`var result = await FinishAction(BuildCreateOptions());` + the existing
success/error handling — so **project mode is behavior-identical**.

For New Solution the host (in `MainWindowViewModel`, field `_projectTemplateService`)
supplies:

```csharp
FinishAction = async firstProject => {
    var solOpts = SolutionWizardMapper.BuildSolutionOptions(
        solutionName, solutionLocation, initGit, firstProject);   // pure
    var sr = await _projectTemplateService.CreateSolutionAsync(solOpts);
    return SolutionWizardMapper.ToProjectResult(sr);              // pure
};
```

`SolutionWizardMapper.ToProjectResult(SolutionCreationResult sr)`:

- `!sr.Success` → `{ Success = false, Error = sr.Error }`.
- `sr.Success && sr.ProjectPaths.Count == 0` → **`{ Success = false, Error =
  "The solution was created but its first project could not be scaffolded." }`**
  (closes the silent-empty-solution hole — the failure is surfaced and the host
  does not load a projectless solution).
- else → `{ Success = true, SolutionPath = sr.SolutionPath, ProjectPath =
  sr.ProjectPaths[0], FilesToOpen = sr.FilesToOpen }`.

This replaces `NewSolutionAsync` entirely; the old empty-solution path is no
longer reached from the menu.

## Feature C — unify "Add Project to Solution"

Reuse the same two pages (no solution-details window) with **Location locked** to
the current solution directory:

1. **Save first.** `await _solutionService.SaveSolutionAsync()` so in-memory
   structural edits are flushed before the template service load-modify-saves the
   `.blsln` from disk (else those edits are dropped).
2. Build `NewProjectWizardViewModel` with `Mode = WizardMode.AddToSolution`,
   preset locked `Location = CurrentSolution.SolutionDirectory`, and a finish
   delegate:
   ```csharp
   FinishAction = opts => {
       var a = SolutionWizardMapper.BuildAddToSolutionOptions(opts, _solutionService.CurrentSolution!);
       // Location = sol dir; CreateSolutionFolder = false; AddToExistingSolution = true;
       // ExistingSolutionPath = sol.FilePath; CreateGitRepository = false  <-- prevents
       //   CreateProjectAsync from re-init'ing git / clobbering .gitignore (:93-97)
       return _projectTemplateService.CreateProjectAsync(a);
   };
   ```
3. Show `NewProjectSelectView` → `ProjectCreationResult`.
4. **Reload** so the in-memory solution reflects the on-disk registration:
   ```csharp
   var solution = await _solutionService.LoadSolutionAsync(_solutionService.CurrentSolution!.FilePath);
   SolutionExplorer.LoadSolution(solution);
   foreach (var f in result.FilesToOpen) await OpenFileAsync(f);
   ```

`CreateProjectAsync` registers into the existing `.blsln` itself, so no
`ISolutionService.AddNewProjectAsync` call is needed.

**Retired:** `AddProjectToSolutionViewModel` + `AddProjectToSolutionDialog` + the
`AddNewProjectToSolutionAsync` bespoke `.blproj` writer + reference XML. Removes
the stale `LLVM/MSIL` hardcoded list and the double `.blproj` write.

- **"Add Existing Project to Solution"** unchanged (`AddExistingProjectAsync`).
- `ISolutionService.AddNewProjectAsync` left in place but no longer called.

## Feature D — Solution Explorer "Add Project Reference" command

Restores — and generalizes — the reference capability retired with the old dialog,
as a project-scoped Solution Explorer command available **anytime**, not only when
adding a project.

**Command.** Add `AddProjectReferenceCommand` to `SolutionExplorerViewModel`,
mirroring the existing project-scoped commands `SetAsStartupProjectAsync` /
`RemoveFromSolutionAsync`
([SolutionExplorerViewModel.cs:518,575](../../VisualGameStudio.Shell/ViewModels/Panels/SolutionExplorerViewModel.cs)):
gated on `SelectedNode?.IsProject == true` and `CurrentSolution` having ≥2
projects. It reads the selected `SolutionProject` from the node and shows a small
picker — a checkbox list of the *other* projects in the solution — reusing the
`CheckableItem` pattern from the retired dialog.

**Context menu.** Add "Add Project Reference…" to the project-node context menu in
`SolutionExplorerView.axaml`, alongside Set as Startup / Remove from Solution.

**Persistence (dual-write — the correctness crux).** References are consumed in
two stores, so both must be updated (matching what the old dialog did at
[MainWindowViewModel.cs:6895-6905](../../VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs)):

1. **In-memory `.blsln` model + build order.** For each checked target call
   `ISolutionService.AddProjectReference(from, to)`, which validates against
   cycles ([SolutionService.cs:183-215](../../VisualGameStudio.ProjectSystem/Services/SolutionService.cs));
   catch its `InvalidOperationException` ("would create a circular dependency"),
   surface it as a dialog, and skip that target. Then `SaveSolutionAsync()` to
   persist `SolutionProject.ProjectReferences` to the `.blsln`
   ([SolutionSerializer.cs:145-147](../../VisualGameStudio.ProjectSystem/Serialization/SolutionSerializer.cs)).
   This store drives build order (topological sort,
   [SolutionService.cs:245-305](../../VisualGameStudio.ProjectSystem/Services/SolutionService.cs)).
2. **The `from` project's `.blproj` `<ProjectReference>`.** This is what the
   compiler actually resolves
   ([ProjectFile.cs:202-206,337-340](../../BasicLang/ProjectSystem/ProjectFile.cs)).
   Add `<ProjectReference Include="..\{to}\{to}.blproj" />` (relative path computed
   from the two project locations) via a targeted XML add — the same element the
   old dialog wrote — idempotent (skip if already present).

`SolutionExplorerViewModel` gains an `ISolutionService` dependency for
`AddProjectReference` (it currently holds only `IProjectService`); wire it in DI.

> **Plan Step 0 (empirical).** Confirm which store the build actually needs: verify
> that compiling project B which references library A resolves A only when the
> `.blproj` `<ProjectReference>` is present (not merely the `.blsln` model), so the
> dual-write — and specifically the `.blproj` write being load-bearing — is
> justified rather than assumed.

## Shared seam — wizard modes, locking, cosmetics

`NewProjectWizardViewModel`:

- `enum WizardMode { NewProject, NewSolution, AddToSolution }` as a **public
  settable property** `Mode` (default `NewProject`), set by the host.
- `public CreateProjectOptions BuildCreateOptions()` — extract the options block
  ([:387-404](../../VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectWizardViewModel.cs))
  into a public method (carries `CppStandard`/`CppToolchain`/`TargetFramework`/`Namespace`).
- `bool IsLocationLocked => Mode != WizardMode.NewProject`; the host presets
  `Location` in locked modes so `CanCreate`'s non-empty check
  ([:116-120](../../VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectWizardViewModel.cs)) passes.
- `FinishAction` seam (above), defaulted in ctor.

`NewProjectConfigureView.axaml`:

- Location TextBox `IsReadOnly="{Binding IsLocationLocked}"` **and** the adjacent
  Browse button `IsEnabled`/`IsVisible = !IsLocationLocked`
  ([:70-73](../../VisualGameStudio.Shell/Views/Dialogs/NewProjectConfigureView.axaml)) — otherwise
  Browse repoints the locked Location.
- "Create solution folder" + "Initialize Git repository" checkboxes
  ([:98-99](../../VisualGameStudio.Shell/Views/Dialogs/NewProjectConfigureView.axaml)) hidden when locked.
- Cosmetic: Create button / busy label bound to `Mode` ("Create solution" in
  NewSolution mode).

`NewProjectSelectView`: add public `Outcome { Created, Cancelled, Back }`
(mirroring `NewProjectConfigureView`) + a Back button shown only when
`Mode == NewSolution`.

`SolutionWizardMapper` (new, pure static in `VisualGameStudio.ProjectSystem` so
tests reach it without the Shell):

- `CreateSolutionOptions BuildSolutionOptions(string name, string location, bool initGit, CreateProjectOptions firstProject)`
  — sets `Name`/`Location`, `CreateGitRepository = initGit`, `SolutionType =
  firstProject.SolutionType`, `InitialProjects = { firstProject }`.
- `CreateProjectOptions BuildAddToSolutionOptions(CreateProjectOptions opts, BasicLangSolution solution)`.
- `ProjectCreationResult ToProjectResult(SolutionCreationResult sr)` (rules above).

## Testing

NUnit, in `VisualGameStudio.Tests`, provable without UI (the pure mappers are what
make option-assembly reachable):

**Close Project**
- `HasProjectOpen` follows `ProjectOpened`/`ProjectClosed`; false when a solution
  is open.
- `CloseProjectCommand` closes a standalone project + resets title/status;
  unsaved-changes prompt path.
- Command Palette entry targets `CloseProjectCommand` (guards the rename).

**Pure mappers (`SolutionWizardMapper`)**
- `BuildSolutionOptions` → `Name`/`Location`/`CreateGitRepository` from args;
  `InitialProjects` has exactly one entry == the passed first project;
  `SolutionType` == first project's.
- `BuildAddToSolutionOptions` → `Location` = sol dir, `CreateSolutionFolder` =
  false, `CreateGitRepository` = false, `AddToExistingSolution` = true,
  `ExistingSolutionPath` = `solution.FilePath`.
- `ToProjectResult`: success+one path → `ProjectPath` set; **success+empty paths →
  `Success == false` with the surfaced error** (failure-path); `!success` →
  error passthrough; no throw on empty.

**Option building (`NewProjectWizardViewModel`)**
- `BuildCreateOptions()` matches project-mode options (backend/template/C++/
  framework/namespace).
- `IsLocationLocked` true iff `Mode != NewProject`.
- Default `FinishAction` calls `_templateService.CreateProjectAsync`; a custom
  `FinishAction` receives `BuildCreateOptions()` and its result reaches
  `ProjectCreated`.

**End-to-end vs. real `ProjectTemplateService`** (temp dir, **fake `IGitService`**
injected — the ctor accepts one, [:18](../../VisualGameStudio.ProjectSystem/Services/ProjectTemplateService.cs))
- New Solution via `BuildSolutionOptions` + `CreateSolutionAsync` yields
  `SolutionName/SolutionName.blsln` loadable by `SolutionService.LoadSolutionAsync`
  with the first project present. Cover C#, **LLVM** (proves LLVM survives), and
  C++ (proves `CppToolchain`/`CppStandard` travel) first projects. Assert
  `InitRepositoryAsync` called once (solution level) when `initGit` true, zero
  when false.
- Add-to-solution via `BuildAddToSolutionOptions` + `CreateProjectAsync` writes
  the `.blproj` **once**, the reloaded solution contains the new project, and
  `InitRepositoryAsync` is **never** called (assert call count on the fake).

**Add Project Reference (Feature D)**
- Cycle rejection: A→B then B→A surfaces the circular-dependency error and leaves
  no reference added.
- Dual persistence: after the command, a `.blsln` reload shows `from.ProjectReferences`
  contains `to`, **and** the `from` `.blproj` contains a `<ProjectReference>` to
  `to`'s relative path; repeating the command is idempotent (no duplicate element).
- Command disabled for a non-project node or a solution with <2 projects.

**Regression**
- Existing `NewProjectWizardViewModelTests` stay green.

## Risks / edge cases

- **Half-created solution on first-project failure.** The `.blsln` (+ dir) exists
  on disk but is not loaded; the user sees the error and can retry. Acceptable;
  not cleaned up (parity with existing partial-failure behavior).
- **Solution switch without save prompt.** Matches Open Solution (Non-goals).
- **Standalone project left open when a solution loads.** Pre-existing with Open
  Solution; `HasProjectOpen`'s `!HasSolution` guard keeps Close Project disabled
  once a solution is loaded, so no broken state.
- **Cancel/Back.** No creation until the user Creates on window 3; Cancel on any
  window aborts cleanly. Back returns Configure→Select (existing) and
  Select→SolutionDetails (new, NewSolution only). AddToSolution has no window 1,
  so Select's Back is hidden there.
- **Backend availability greying** inherited unchanged from the reused pages.

## Files touched (anticipated)

- `VisualGameStudio.Shell/Views/MainWindow.axaml` — Close Project menu item.
- `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs` — rename
  `CloseFolderAsync`→`CloseProjectAsync`, add `HasProjectOpen`, rewrite
  `NewSolutionAsync` + `AddNewProjectToSolutionAsync`.
- `VisualGameStudio.Shell/ViewModels/Dialogs/CommandPaletteViewModel.cs` —
  repoint palette entry to `CloseProjectCommand`.
- `VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectWizardViewModel.cs` —
  `Mode`, `BuildCreateOptions()`, `IsLocationLocked`, ctor-defaulted `FinishAction`.
- `VisualGameStudio.Shell/Views/Dialogs/NewProjectSelectView.axaml(.cs)` — public
  `Outcome` + Back button (NewSolution mode).
- `VisualGameStudio.Shell/Views/Dialogs/NewProjectConfigureView.axaml(.cs)` —
  Location read-only + Browse disabled + checkboxes hidden + mode-aware labels
  when locked.
- `VisualGameStudio.Shell/Views/Dialogs/NewSolutionView.axaml(.cs)` +
  `ViewModels/Dialogs/NewSolutionViewModel.cs` — **new** solution-details window.
- `VisualGameStudio.ProjectSystem/Services/SolutionWizardMapper.cs` — **new** pure
  mappers.
- `VisualGameStudio.Shell/ViewModels/Panels/SolutionExplorerViewModel.cs` +
  `Views/Panels/SolutionExplorerView.axaml` — **Feature D** command + context menu
  + `ISolutionService` injection.
- `VisualGameStudio.Shell/Views/Dialogs/AddProjectReferenceDialog.axaml(.cs)` +
  VM — **new** small reference picker (checkbox list).
- `VisualGameStudio.ProjectSystem/…` — **new** `.blproj` `<ProjectReference>`
  writer helper (targeted XML add, idempotent).
- **Removed:** `AddProjectToSolutionViewModel.cs`, `AddProjectToSolutionDialog.axaml(.cs)`.
- `VisualGameStudio.Tests/*` — new/updated tests + a reusable fake `IGitService`.
