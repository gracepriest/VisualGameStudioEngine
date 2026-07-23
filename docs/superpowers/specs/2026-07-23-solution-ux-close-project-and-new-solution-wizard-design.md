# Solution UX: Close Project + guided New Solution wizard — design

**Date:** 2026-07-23
**Status:** Draft for review
**Area:** IDE (`VisualGameStudio.Shell` + `VisualGameStudio.ProjectSystem`)

## Summary

Three linked changes to how the IDE's File menu handles projects and solutions:

1. **Close Project** — add a File-menu item that closes the currently-open
   standalone project. The close logic already exists but is bound to nothing.
2. **Guided New Solution wizard** — replace the bare two-dialog "New Solution"
   with a wizard that walks the user through naming the solution *and* adding a
   first project, reusing the existing New Project pages.
3. **Unify "Add Project to Solution"** — point the solution's add-project flow at
   the same reused pages and the same creation service, retiring a divergent
   dialog (and its stale backend list) and a duplicate `.blproj` writer.

All three lean on machinery that already exists; the net new UI is a single
window (solution name + location).

## Goals

- A standalone project can be closed from the File menu (today it cannot without
  exiting).
- "New Solution" produces a solution **with a first project already in it**,
  chosen through the same template/backend UI as New Project.
- The two "add a new project" code paths (New Project wizard vs. Add Project to
  Solution) stop diverging — one project-config UI, one creation path.
- LLVM and MSIL remain selectable everywhere they are today (they are
  forward-looking placeholders the user wants kept, not removed).

## Non-goals

- **Consolidating `IProjectService` and `ISolutionService`.** Both declare
  `CreateSolutionAsync`/`CloseSolutionAsync`
  ([IProjectService.cs:61](../../VisualGameStudio.Core/Abstractions/Services/IProjectService.cs),
  [ISolutionService.cs:33](../../VisualGameStudio.Core/Abstractions/Services/ISolutionService.cs));
  the shell only uses `ISolutionService` for solution lifecycle. Untangling that
  is a larger refactor left for later — documented here so it is a known,
  deliberate duplication, not an accident.
- **Changing that a standalone New Project also writes a `.blsln`.** For templates
  with `CreateSolution=true`, `ProjectTemplateService.CreateProjectAsync` already
  emits a solution file next to the project
  ([ProjectTemplateService.cs:81](../../VisualGameStudio.ProjectSystem/Services/ProjectTemplateService.cs)),
  which the IDE then ignores (it opens the `.blproj`). Not touched here.
- No changes to build, debug, or the Solution Explorer's runtime behavior.

## Background — how solutions work today

There are effectively **two solution subsystems**, both writing `.blsln` through
the same `SolutionSerializer`:

- **`IProjectTemplateService`** (`ProjectTemplateService`) — *creation*. Scaffolds
  projects from templates, and already:
  - writes a single-project `.blsln` for a standalone project
    (`CreateSolutionFileAsync`, [:1127](../../VisualGameStudio.ProjectSystem/Services/ProjectTemplateService.cs));
  - has `CreateSolutionAsync(CreateSolutionOptions)` that creates a solution and
    scaffolds each `InitialProjects` entry, registering each into the `.blsln`
    ([:112](../../VisualGameStudio.ProjectSystem/Services/ProjectTemplateService.cs));
  - has `CreateProjectAsync` with `AddToExistingSolution`/`ExistingSolutionPath`
    that load-modify-saves a project into an existing `.blsln`, dedup-aware
    ([:86](../../VisualGameStudio.ProjectSystem/Services/ProjectTemplateService.cs), [:1161](../../VisualGameStudio.ProjectSystem/Services/ProjectTemplateService.cs)).
- **`ISolutionService`** (`SolutionService`) — *session management*. Holds the
  currently-open `BasicLangSolution`, drives the Solution Explorer, and exposes
  runtime add/remove/reference/build-order. Its own `CreateSolutionAsync(name,
  dir)` writes an **empty** solution, and `AddNewProjectAsync` writes a *minimal*
  `.blproj`.

The current File-menu flows are inconsistent about which subsystem they use:

| Command | Today |
|---|---|
| New Project | `NewProjectWizardViewModel` → `ProjectTemplateService.CreateProjectAsync`; opened as a project |
| New Solution | `_dialogService` name box + folder box → `ISolutionService.CreateSolutionAsync` → **empty** solution ([MainWindowViewModel.cs:6775](../../VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs)) |
| Add New Project to Solution | bespoke `AddProjectToSolutionViewModel` dialog → `ISolutionService.AddNewProjectAsync` (writes minimal `.blproj`) **then the VM overwrites it** with a richer `.blproj` ([MainWindowViewModel.cs:6907](../../VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs)) — the file is written twice |
| Close Project | **nothing** — `CloseFolderAsync` calls `_projectService.CloseProjectAsync()` but is bound to no menu item or key ([MainWindowViewModel.cs:6716](../../VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs)) |

The `AddProjectToSolutionViewModel` also hardcodes backends `CSharp/LLVM/MSIL/Cpp`
and templates including `WPF`
([AddProjectToSolutionViewModel.cs:28-36](../../VisualGameStudio.Shell/ViewModels/Dialogs/AddProjectToSolutionViewModel.cs)),
which drift from the New Project wizard's backend list
([NewProjectWizardViewModel.cs:175-184](../../VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectWizardViewModel.cs)).

**Design principle adopted here:** *creation* always goes through
`IProjectTemplateService`; *session management* stays in `ISolutionService`, which
loads the produced `.blsln`. New Solution and Add Project are moved onto the
template service so all creation shares one tested path.

## The reused pages

The New Project wizard is a two-window flow coordinated by the view code-behind:

- Window 1 `NewProjectSelectView(vm)` — template gallery + backend/language;
  raises `NextRequested` → opens window 2 as a modal
  ([NewProjectSelectView.axaml.cs:27](../../VisualGameStudio.Shell/Views/Dialogs/NewProjectSelectView.axaml.cs)).
- Window 2 `NewProjectConfigureView(vm)` — name, location, options; exposes an
  `Outcome` (`Created`/`Cancelled`/`Back`) and a `ProjectCreationResult Result`.
- On `Created`, window 1 closes returning the `ProjectCreationResult`; the VM's
  `CreateProjectAsync` command builds a `CreateProjectOptions`
  ([NewProjectWizardViewModel.cs:387-404](../../VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectWizardViewModel.cs))
  and calls `_templateService.CreateProjectAsync`.

Both `SelectedBackend.SolutionType` (`dotnet`/`msil`/`native`/`llvm`/`cpp`) and the
C++ `ToolchainId`/`CppStandard` are already produced here, so reusing these pages
preserves the full backend set — **including LLVM and MSIL** — automatically.

## Feature A — Close Project

**Menu.** Add to the File menu, immediately above "Close Solution"
([MainWindow.axaml:178](../../VisualGameStudio.Shell/Views/MainWindow.axaml)):

```xml
<MenuItem Header="Close _Project" Command="{Binding CloseProjectCommand}"
          IsEnabled="{Binding HasProjectOpen}"/>
```

**Command.** Add `CloseProjectCommand` to `MainWindowViewModel`, lifting the body
of the orphaned `CloseFolderAsync` (the unsaved-changes prompt + `SaveAllAsync` +
`_projectService.CloseProjectAsync()` + title/status reset). The orphaned
`CloseFolderAsync` is removed (nothing references it).

**Enablement.** Add an observable `HasProjectOpen` (mirroring the existing
`HasSolutionOpen`), toggled from `IProjectService.ProjectOpened`/`ProjectClosed`.
Per the design decision, the item is enabled only for a **standalone** project
(`_projectService.CurrentProject != null`); a solution's projects are closed via
"Close Solution". (If a project is open standalone *and* no solution is open,
Close Project is enabled; when a solution is open, the item is disabled and Close
Solution governs.)

## Feature B — guided New Solution wizard

**Flow (3 windows):**

1. **New window — Solution details.** `NewSolutionView` collects `SolutionName`
   + `Location` (with Browse), validates the name (reuse the same
   invalid-char/`MSBuildText` classifier the New Project wizard uses for its name
   warning) and that `Location/SolutionName` does not already exist non-empty.
   Shows the resulting `…/SolutionName/SolutionName.blsln` path. "Next" advances.
2. **Reused window — template/backend** (`NewProjectSelectView`).
3. **Reused window — project details** (`NewProjectConfigureView`), with the
   **Location field locked** to the solution folder (the template service forces
   it anyway; see below) and labeled "created inside &lt;SolutionName&gt;".

**Creation (single call).** On finish, the host builds:

```csharp
var options = new CreateSolutionOptions {
    Name = solutionName,
    Location = solutionLocation,
    SolutionType = vm.SelectedBackend!.SolutionType,
    CreateGitRepository = vm.CreateGitRepository,
    InitialProjects = { vm.BuildCreateOptions() }   // the wizard's own options
};
var result = await _templateService.CreateSolutionAsync(options);
```

`CreateSolutionAsync` creates `Location/Name/`, writes the `.blsln`, and for the
one initial project forces `Location = solutionDir`, `AddToExistingSolution =
true`, `CreateSolutionFolder = false`, then scaffolds + registers it
([ProjectTemplateService.cs:131-146](../../VisualGameStudio.ProjectSystem/Services/ProjectTemplateService.cs)).
The host then opens the solution:

```csharp
var solution = await _solutionService.LoadSolutionAsync(result.SolutionPath!);
SolutionExplorer.LoadSolution(solution);
_recentProjectsService.AddRecentProject(result.SolutionPath!, solution.SolutionName);
// open result.FilesToOpen
```

This replaces `NewSolutionAsync` in its entirety. The old empty-solution behavior
(`ISolutionService.CreateSolutionAsync`) is no longer reached from the menu.

## Feature C — unify "Add Project to Solution"

"Add New Project to Solution" reuses the same two pages (no solution-details
step, since a solution is already open) with the project **Location locked** to
the current solution directory. On finish the host calls:

```csharp
var opts = vm.BuildCreateOptions();
opts.Location = _solutionService.CurrentSolution!.SolutionDirectory;
opts.CreateSolutionFolder = false;
opts.AddToExistingSolution = true;
opts.ExistingSolutionPath = _solutionService.CurrentSolution.FilePath;
var result = await _templateService.CreateProjectAsync(opts);
// reload so the in-memory solution reflects the on-disk registration:
var solution = await _solutionService.LoadSolutionAsync(_solutionService.CurrentSolution.FilePath);
SolutionExplorer.LoadSolution(solution);
```

`CreateProjectAsync` registers the project into the existing `.blsln` itself
([:86](../../VisualGameStudio.ProjectSystem/Services/ProjectTemplateService.cs)),
so no separate `ISolutionService.AddNewProjectAsync` call is needed. A reload from
disk refreshes `CurrentSolution` (which fires `SolutionLoaded`).

**Retired:** `AddProjectToSolutionViewModel` + `AddProjectToSolutionDialog` + the
`AddNewProjectToSolutionAsync` body's bespoke `.blproj` writer + project-reference
XML. This removes the stale `LLVM/MSIL` hardcoded list (the reused pages show the
proper, live backend list), and the double `.blproj` write.

- **"Add Existing Project to Solution"** is unchanged — it still uses
  `ISolutionService.AddExistingProjectAsync`.
- `ISolutionService.AddNewProjectAsync` is left in place (part of the tested
  interface) but is no longer called by the shell.

## Shared seam — wizard modes + option building

`NewProjectWizardViewModel` gains a small mode so the same pages serve all three
flows without duplicating the config UI:

- `enum WizardMode { NewProject, NewSolution, AddToSolution }` (default
  `NewProject`).
- `public CreateProjectOptions BuildCreateOptions()` — extract the existing
  options-building block from `CreateProjectAsync`
  ([:387-404](../../VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectWizardViewModel.cs))
  into a public method so all three flows produce identical options (carrying
  `CppStandard`/`CppToolchain`/`TargetFramework`/`Namespace`).
- **Location lock.** In `NewSolution`/`AddToSolution` modes the Configure page's
  Location is read-only and preset to the (display) solution folder. The VM
  exposes `bool IsLocationLocked => Mode != WizardMode.NewProject`.
- **Finish routing.** In `NewProject` mode the VM keeps calling
  `_templateService.CreateProjectAsync` and raising `ProjectCreated` (unchanged).
  In the other two modes it does **not** create; the host supplies the creation
  via an injected finish delegate `Func<CreateProjectOptions, Task<ProjectCreationResult>>`
  that the VM invokes on Create, then raises `ProjectCreated` with the result.
  This keeps window coordination (`Outcome`/`Result`) identical.

The exact delegate-vs-event wiring is an implementation choice for the plan; the
constraint is: **project-mode behavior is byte-for-byte unchanged**, and the two
solution modes route creation through the template service.

## Testing

New/updated tests (NUnit, in `VisualGameStudio.Tests`), all provable without UI:

**Close Project**
- `HasProjectOpen` follows `ProjectOpened`/`ProjectClosed`.
- Invoking `CloseProjectCommand` with a standalone project calls
  `CloseProjectAsync` and resets title/status; unsaved-changes prompt path.
- Menu item disabled when no standalone project is open.

**New Solution (option building + orchestration)**
- `BuildCreateOptions()` returns the same options project-mode uses (backend,
  template, `CppStandard`/`CppToolchain` when C++, framework, namespace).
- New Solution builds a `CreateSolutionOptions` with exactly one `InitialProjects`
  entry, `SolutionType` = selected backend, and calls
  `_templateService.CreateSolutionAsync`.
- End-to-end against the real `ProjectTemplateService` (temp dir): creating a
  solution yields `SolutionName/SolutionName.blsln` loadable by
  `SolutionService.LoadSolutionAsync`, with the first project present in
  `solution.Projects` at `SolutionName/…/….blproj`. Cover a C# first project and
  an **LLVM** first project (proves LLVM survives) and a C++ first project (proves
  `CppToolchain`/`CppStandard` travel).

**Add Project to Solution (unification)**
- Add flow calls `CreateProjectAsync` with `AddToExistingSolution=true` and the
  current solution path; the `.blproj` is written **once** (assert single write /
  single file, no overwrite).
- After add, the reloaded solution contains the new project; the backend list
  offered includes LLVM and MSIL.

**Regression**
- Existing `NewProjectWizardViewModelTests` stay green (project mode unchanged).

## Risks / edge cases

- **Stale in-memory solution after Add.** `CreateProjectAsync` mutates the `.blsln`
  on disk; we reload via `LoadSolutionAsync` to resync. If the open solution had
  unsaved in-memory structure changes, they must be saved first (the flow saves
  before/after as today) — call out in the plan.
- **Name collisions.** `CreateSolutionAsync`/`CreateProjectAsync` create
  directories; the Solution-details window validates non-empty-target before
  Next, and `ValidateProjectOptions` guards the project step.
- **Cancel semantics.** Cancelling any window returns without creating; the
  solution-details window Cancel closes the whole flow (matches New Project's
  window-1 Cancel).
- **Backend availability greying.** The reused pages already grey unavailable C++
  toolchains via the probe; solution/add modes inherit that unchanged.

## Files touched (anticipated)

- `VisualGameStudio.Shell/Views/MainWindow.axaml` — Close Project menu item.
- `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs` — `CloseProjectCommand`,
  `HasProjectOpen`, rewritten `NewSolutionAsync` + `AddNewProjectToSolutionAsync`,
  removed `CloseFolderAsync`.
- `VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectWizardViewModel.cs` —
  `WizardMode`, `BuildCreateOptions()`, `IsLocationLocked`, finish delegate.
- `VisualGameStudio.Shell/Views/Dialogs/NewProjectConfigureView.axaml(.cs)` —
  bind Location read-only when locked.
- `VisualGameStudio.Shell/Views/Dialogs/NewSolutionView.axaml(.cs)` — **new**
  solution-details window.
- **Removed:** `AddProjectToSolutionViewModel.cs`, `AddProjectToSolutionDialog.axaml(.cs)`.
- `VisualGameStudio.Tests/*` — new/updated tests above.
