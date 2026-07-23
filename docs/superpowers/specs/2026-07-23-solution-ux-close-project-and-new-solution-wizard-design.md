# Solution UX: Close Project + guided New Solution wizard — design

**Date:** 2026-07-23
**Status:** Draft for review (r1 — incorporates two-lens review feedback)
**Area:** IDE (`VisualGameStudio.Shell` + `VisualGameStudio.ProjectSystem`)

## Summary

Three linked changes to how the IDE's File menu handles projects and solutions:

1. **Close Project** — add a File-menu item that closes the currently-open
   standalone project. The close logic already exists (reachable only via the
   Command Palette today).
2. **Guided New Solution wizard** — replace the bare two-dialog "New Solution"
   with a wizard that walks the user through naming the solution *and* adding a
   first project, reusing the existing New Project pages.
3. **Unify "Add Project to Solution"** — point the solution's add-project flow at
   the same reused pages and the same creation service, retiring a divergent
   dialog (and its stale backend list) and a duplicate `.blproj` writer.

All three lean on machinery that already exists; the net new UI is a single
window (solution name + location).

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
  the shell only uses `ISolutionService` for solution lifecycle. Untangling that
  is a larger refactor left for later — documented so it is a known, deliberate
  duplication.
- **Changing that a standalone New Project also writes a `.blsln`.** For templates
  with `CreateSolution=true`, `ProjectTemplateService.CreateProjectAsync` already
  emits a solution file next to the project
  ([ProjectTemplateService.cs:81](../../VisualGameStudio.ProjectSystem/Services/ProjectTemplateService.cs)),
  which the IDE then ignores (it opens the `.blproj`). Not touched here.
- **Add-project reference picker.** The retired dialog let the user check existing
  projects to reference at creation time (see Risks); the reused pages have no
  such picker. Project references are added afterward via the Solution Explorer /
  `ISolutionService.AddProjectReference`. Flagged, not carried.
- No changes to build, debug, or the Solution Explorer's runtime behavior.

## Background — how solutions work today

There are effectively **two solution subsystems**, both writing `.blsln` through
the same `SolutionSerializer`:

- **`IProjectTemplateService`** (`ProjectTemplateService`) — *creation*. Scaffolds
  projects from templates, and already:
  - writes a single-project `.blsln` for a standalone project
    (`CreateSolutionFileAsync`, [:1127](../../VisualGameStudio.ProjectSystem/Services/ProjectTemplateService.cs));
  - has `CreateSolutionAsync(CreateSolutionOptions)` that creates a solution and
    scaffolds each `InitialProjects` entry, forcing per-project `Location =
    solutionDir`, `AddToExistingSolution = true`, `CreateSolutionFolder = false`,
    `CreateGitRepository = false`, and registering each into the `.blsln`; git is
    initialized once at the solution level
    ([:112-153](../../VisualGameStudio.ProjectSystem/Services/ProjectTemplateService.cs));
  - has `CreateProjectAsync` with `AddToExistingSolution`/`ExistingSolutionPath`
    that load-modify-saves a project into an existing `.blsln`, dedup-aware
    ([:86-90](../../VisualGameStudio.ProjectSystem/Services/ProjectTemplateService.cs), [:1161](../../VisualGameStudio.ProjectSystem/Services/ProjectTemplateService.cs)).
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
| Add New Project to Solution | bespoke `AddProjectToSolutionViewModel` dialog → `ISolutionService.AddNewProjectAsync` (writes a minimal `.blproj`, [SolutionService.cs:104-116](../../VisualGameStudio.ProjectSystem/Services/SolutionService.cs)) **then the host command overwrites it** with a richer `.blproj` ([MainWindowViewModel.cs:6931](../../VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs)) — the file is written twice |
| Close Project | reachable only via Command Palette "Close Folder" → `CloseFolderCommand` → `_projectService.CloseProjectAsync()` ([CommandPaletteViewModel.cs:235](../../VisualGameStudio.Shell/ViewModels/CommandPaletteViewModel.cs), [MainWindowViewModel.cs:6716-6728](../../VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs)); **absent from the File menu** |

The `AddProjectToSolutionViewModel` also hardcodes backends `CSharp/LLVM/MSIL/Cpp`
and templates including `WPF`
([AddProjectToSolutionViewModel.cs:28-36](../../VisualGameStudio.Shell/ViewModels/Dialogs/AddProjectToSolutionViewModel.cs)),
which drift from the New Project wizard's backend list
([NewProjectWizardViewModel.cs:175-184](../../VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectWizardViewModel.cs)).

**Design principle adopted here:** *creation* always goes through
`IProjectTemplateService`; *session management* stays in `ISolutionService`, which
loads the produced `.blsln`. New Solution and Add Project move onto the template
service so all creation shares one tested path.

## The reused pages

The New Project wizard is a two-window flow coordinated by the view code-behind:

- Window 1 `NewProjectSelectView(vm)` — template gallery + backend/language;
  raises `NextRequested` → opens window 2 as a modal; today it has only
  Cancel/Next (no Back)
  ([NewProjectSelectView.axaml.cs:27-64](../../VisualGameStudio.Shell/Views/Dialogs/NewProjectSelectView.axaml.cs)).
- Window 2 `NewProjectConfigureView(vm)` — name, location, options; exposes an
  `Outcome` (`Created`/`Cancelled`/`Back`) and a `ProjectCreationResult Result`.
- The VM's `CreateProjectAsync` command builds a `CreateProjectOptions`
  ([NewProjectWizardViewModel.cs:387-404](../../VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectWizardViewModel.cs))
  and calls `_templateService.CreateProjectAsync`, raising
  `ProjectCreated` (`EventHandler<ProjectCreationResult>`, [:128,409](../../VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectWizardViewModel.cs)).

Both `SelectedBackend.SolutionType` (`dotnet`/`msil`/`native`/`llvm`/`cpp`) and the
C++ `ToolchainId`/`CppStandard` are produced here, so reusing these pages
preserves the full backend set — **including LLVM and MSIL** — automatically.

## Feature A — Close Project

**Menu.** Add to the File menu, immediately above "Close Solution"
([MainWindow.axaml:178](../../VisualGameStudio.Shell/Views/MainWindow.axaml)):

```xml
<MenuItem Header="Close _Project" Command="{Binding CloseProjectCommand}"
          IsEnabled="{Binding HasProjectOpen}"/>
```

**Command.** Rename the existing `CloseFolderAsync` → `CloseProjectAsync`
(`CloseProjectCommand`), keeping its body verbatim (unsaved-changes prompt +
`SaveAllAsync` + `_projectService.CloseProjectAsync()` + title/status reset,
[MainWindowViewModel.cs:6715-6732](../../VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs)).
Because CommunityToolkit generates the command from the method name, this renames
the generated command `CloseFolderCommand` → `CloseProjectCommand`; the sole other
reference — the Command Palette — must be repointed in the same change:

- [CommandPaletteViewModel.cs:235](../../VisualGameStudio.Shell/ViewModels/CommandPaletteViewModel.cs):
  `AddCommand("File", "Close Folder", null, () => vm.CloseFolderCommand.Execute(null))`
  → `AddCommand("File", "Close Project", null, () => vm.CloseProjectCommand.Execute(null))`.

**Enablement.** Add an observable `HasProjectOpen` (mirroring the existing
`HasSolutionOpen`), toggled from `IProjectService.ProjectOpened`/`ProjectClosed`
(already subscribed at [MainWindowViewModel.cs:545-546](../../VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs)).
Per the design decision, the item is enabled only for a **standalone** project
(`_projectService.CurrentProject != null && !_solutionService.HasSolution`); a
solution's projects are closed via "Close Solution".

## Feature B — guided New Solution wizard

### Orchestration (concrete)

The **host `MainWindowViewModel.NewSolutionAsync`** owns the whole flow; no new
service-heavy view code-behind. It runs a small window loop:

1. **Preconditions.** If `_solutionService.HasSolution`, run the existing Close
   Solution flow first (unsaved-changes prompt + document clear,
   [MainWindowViewModel.cs:6806-6831](../../VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs));
   if that is cancelled, abort. If a standalone project is open
   (`CurrentProject != null`), close it via `CloseProjectAsync` first. After this,
   neither a solution nor a project is open.
2. **Window 1 — Solution details.** Show `NewSolutionView` bound to a new
   `NewSolutionViewModel` exposing `SolutionName` + `Location` (with Browse),
   validation (invalid-char check reusing the same `MSBuildText` classifier the
   wizard uses for its name warning; and `Location/SolutionName` not already a
   non-empty directory), and a computed `…/SolutionName/SolutionName.blsln`
   preview. Returns `Confirmed` (with the two values) or `Cancelled`.
3. **Windows 2-3 — reused select→configure.** Build a `NewProjectWizardViewModel`
   with `Mode = WizardMode.NewSolution`, preset `Location = <solutionDir>` (locked,
   read-only), and a **finish delegate** (below) closed over the window-1 values.
   Show `NewProjectSelectView` (which chains to `NewProjectConfigureView` exactly
   as today). It returns a `ProjectCreationResult?`, or a **Back** signal.
4. **Back.** In `NewSolution` mode `NewProjectSelectView` shows a Back button that
   closes it with a Back outcome; the host loops back to step 2 with the
   `NewSolutionViewModel` values preserved. (Configure→Select Back is unchanged.)
5. **Finish.** On a successful `ProjectCreationResult`, open the solution:
   ```csharp
   var solution = await _solutionService.LoadSolutionAsync(result.SolutionPath!);
   SolutionExplorer.LoadSolution(solution);
   _recentProjectsService.AddRecentProject(result.SolutionPath!, solution.SolutionName);
   foreach (var f in result.FilesToOpen) await OpenFileAsync(f);   // existing open path
   ```

### The finish delegate + result adapter

`NewProjectWizardViewModel` gains an injectable finish action:

```csharp
// default (NewProject mode): calls the template service, unchanged behavior
Func<CreateProjectOptions, Task<ProjectCreationResult>> FinishAction { get; set; }
    = opts => _templateService.CreateProjectAsync(opts);
```

The VM's `CreateProjectAsync` command becomes
`var result = await FinishAction(BuildCreateOptions());` followed by the existing
success/error handling — so **project mode is behavior-identical** (default finish
= `CreateProjectAsync`).

For New Solution the host supplies a finish delegate that maps the *solution*
creation back into a `ProjectCreationResult` via a pure adapter:

```csharp
FinishAction = async firstProject => {
    var solOpts = SolutionWizardMapper.BuildSolutionOptions(
        solutionName, solutionLocation, firstProject);      // pure, testable
    var sr = await _templateService.CreateSolutionAsync(solOpts);
    return SolutionWizardMapper.ToProjectResult(sr);        // pure, testable
};
```

`SolutionWizardMapper.ToProjectResult(SolutionCreationResult sr)` maps:
`Success→Success`, `Error→Error`, `SolutionPath→SolutionPath`,
`ProjectPaths[0]→ProjectPath` (null-safe), `FilesToOpen→FilesToOpen`. This keeps
the VM's `result.Success` branch and the host's `result.SolutionPath!`/`FilesToOpen`
consumption working unchanged.

This replaces `NewSolutionAsync` in its entirety; the old empty-solution path
(`ISolutionService.CreateSolutionAsync`) is no longer reached from the menu.

## Feature C — unify "Add Project to Solution"

"Add New Project to Solution" reuses the same two pages (no solution-details step,
since a solution is already open) with the project **Location locked** to the
current solution directory. Concretely:

1. **Save first.** Call `ISolutionService.SaveSolutionAsync()` so any in-memory
   structural edits are flushed before the template service load-modify-saves the
   `.blsln` from disk (else those edits are silently dropped).
2. Build a `NewProjectWizardViewModel` with `Mode = WizardMode.AddToSolution`,
   preset `Location = CurrentSolution.SolutionDirectory` (locked), and a finish
   delegate that adds to the existing solution:
   ```csharp
   FinishAction = opts => {
       var a = SolutionWizardMapper.BuildAddToSolutionOptions(
           opts, _solutionService.CurrentSolution!);   // pure, testable:
                                                        //   Location = sol dir
                                                        //   CreateSolutionFolder = false
                                                        //   CreateGitRepository = false   <-- avoids git re-init
                                                        //   AddToExistingSolution = true
                                                        //   ExistingSolutionPath = sol.FilePath
       return _templateService.CreateProjectAsync(a);
   };
   ```
   Forcing `CreateGitRepository = false` prevents `CreateProjectAsync` from
   re-running `InitRepositoryAsync` + clobbering `.gitignore` on the existing
   solution tree ([ProjectTemplateService.cs:93-97](../../VisualGameStudio.ProjectSystem/Services/ProjectTemplateService.cs)).
3. Show `NewProjectSelectView` → returns `ProjectCreationResult`.
4. **Reload** so the in-memory solution reflects the on-disk registration:
   ```csharp
   var solution = await _solutionService.LoadSolutionAsync(_solutionService.CurrentSolution!.FilePath);
   SolutionExplorer.LoadSolution(solution);
   foreach (var f in result.FilesToOpen) await OpenFileAsync(f);
   ```

`CreateProjectAsync` registers the project into the existing `.blsln` itself, so
no `ISolutionService.AddNewProjectAsync` call is needed.

**Retired:** `AddProjectToSolutionViewModel` + `AddProjectToSolutionDialog` + the
`AddNewProjectToSolutionAsync` body's bespoke `.blproj` writer + project-reference
XML. This removes the stale `LLVM/MSIL` hardcoded list (the reused pages show the
live backend list) and the double `.blproj` write.

- **"Add Existing Project to Solution"** is unchanged — still
  `ISolutionService.AddExistingProjectAsync`.
- `ISolutionService.AddNewProjectAsync` is left in place (part of the tested
  interface) but is no longer called by the shell.

## Shared seam — wizard modes, locking, cosmetics

`NewProjectWizardViewModel`:

- `enum WizardMode { NewProject, NewSolution, AddToSolution }` exposed as a
  **public settable property** `Mode` (default `NewProject`) the host sets before
  showing the windows.
- `public CreateProjectOptions BuildCreateOptions()` — extract the existing
  options-building block ([:387-404](../../VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectWizardViewModel.cs))
  into a public method so all flows produce identical options (carrying
  `CppStandard`/`CppToolchain`/`TargetFramework`/`Namespace`).
- `bool IsLocationLocked => Mode != WizardMode.NewProject`. In locked modes the
  host presets `Location` to the (display) solution folder so `CanCreate`'s
  non-empty-`Location` requirement ([:116-120](../../VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectWizardViewModel.cs))
  is satisfied.
- `FinishAction` seam (above), default = `CreateProjectAsync`.

`NewProjectConfigureView.axaml`:

- `IsReadOnly="{Binding IsLocationLocked}"` on the Location TextBox
  ([:71](../../VisualGameStudio.Shell/Views/Dialogs/NewProjectConfigureView.axaml)).
- Bind the "Create solution folder" and "Initialize Git repository" checkboxes
  ([:98-99](../../VisualGameStudio.Shell/Views/Dialogs/NewProjectConfigureView.axaml)) to
  `IsVisible/IsEnabled = !IsLocationLocked` so the reused UI can't drive git or
  folder layout in solution/add modes (belt-and-suspenders with the forced option
  values).
- Cosmetic: bind the Create button text / busy label to `Mode` so New Solution
  reads "Create solution" instead of "Creating project…" ([:122,126](../../VisualGameStudio.Shell/Views/Dialogs/NewProjectConfigureView.axaml)).

`SolutionWizardMapper` (new, pure static — the testable seam):

- `CreateSolutionOptions BuildSolutionOptions(string name, string location, CreateProjectOptions firstProject)`
- `CreateProjectOptions BuildAddToSolutionOptions(CreateProjectOptions opts, BasicLangSolution solution)`
- `ProjectCreationResult ToProjectResult(SolutionCreationResult sr)`

## Testing

New/updated tests (NUnit, in `VisualGameStudio.Tests`), all provable without UI —
the pure mappers are what make the option-assembly assertions reachable:

**Close Project**
- `HasProjectOpen` follows `ProjectOpened`/`ProjectClosed`; false when a solution
  is open.
- `CloseProjectCommand` with a standalone project calls `CloseProjectAsync` and
  resets title/status; unsaved-changes prompt path.
- Command Palette entry now targets `CloseProjectCommand` (guards the rename so
  the regression can't silently reintroduce `CloseFolderCommand`).

**Pure mappers (`SolutionWizardMapper`)**
- `BuildSolutionOptions` → `Name`/`Location` from args, `InitialProjects` has
  exactly one entry == the passed first project.
- `BuildAddToSolutionOptions` → `Location` = solution dir, `CreateSolutionFolder`
  = false, `CreateGitRepository` = false, `AddToExistingSolution` = true,
  `ExistingSolutionPath` = `solution.FilePath`.
- `ToProjectResult` → `ProjectPaths[0]→ProjectPath`, Success/Error/SolutionPath/
  FilesToOpen passthrough; empty `ProjectPaths` → null `ProjectPath` without throw.

**Option building (`NewProjectWizardViewModel`)**
- `BuildCreateOptions()` returns the same options project-mode uses (backend,
  template, `CppStandard`/`CppToolchain` when C++, framework, namespace).
- `IsLocationLocked` true iff `Mode != NewProject`.
- Default `FinishAction` calls `_templateService.CreateProjectAsync` (project mode
  unchanged); a custom `FinishAction` is invoked with `BuildCreateOptions()` and
  its result flows to `ProjectCreated`.

**End-to-end against the real `ProjectTemplateService`** (temp dir)
- New Solution via `BuildSolutionOptions` + `CreateSolutionAsync` yields
  `SolutionName/SolutionName.blsln` loadable by `SolutionService.LoadSolutionAsync`,
  first project present in `solution.Projects`. Cover a C# first project, an
  **LLVM** first project (proves LLVM survives), and a C++ first project (proves
  `CppToolchain`/`CppStandard` travel).
- Add-to-solution via `BuildAddToSolutionOptions` + `CreateProjectAsync` writes
  the `.blproj` **once** and the reloaded solution contains the new project; git is
  not re-initialized (assert `.gitignore` mtime / no second init on a seeded repo).

**Regression**
- Existing `NewProjectWizardViewModelTests` stay green (project mode unchanged).

## Risks / edge cases

- **Reference-picker parity loss.** The retired dialog could set project→project
  references at creation ([AddProjectToSolutionViewModel.cs:38,71-74](../../VisualGameStudio.Shell/ViewModels/Dialogs/AddProjectToSolutionViewModel.cs);
  [MainWindowViewModel.cs:6895-6905](../../VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs)).
  The reused Configure page has no such picker, so references are now added after
  creation via the Solution Explorer / `AddProjectReference`. Called out as a
  deliberate parity change (Non-goals). **Decision point for the user.**
- **Stale in-memory solution after Add.** Mitigated by save-first (Feature C step
  1) + reload (step 4).
- **Already-open state at New Solution.** Handled by the precondition step
  (close solution with prompt / close standalone project) before window 1.
- **Cancel/Back.** Cancel on any window aborts without creating; window-1 Cancel
  closes the whole flow; Back returns Configure→Select (existing) and
  Select→SolutionDetails (new, NewSolution mode only). AddToSolution has no
  window 1, so Select's Back is disabled/absent in that mode.
- **Name collisions.** Window-1 validates non-empty-target before Next;
  `ValidateProjectOptions` guards the project step.
- **Backend availability greying.** The reused pages already grey unavailable C++
  toolchains via the probe; solution/add modes inherit that unchanged.

## Files touched (anticipated)

- `VisualGameStudio.Shell/Views/MainWindow.axaml` — Close Project menu item.
- `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs` — rename
  `CloseFolderAsync`→`CloseProjectAsync`, add `HasProjectOpen`, rewrite
  `NewSolutionAsync` + `AddNewProjectToSolutionAsync`.
- `VisualGameStudio.Shell/ViewModels/CommandPaletteViewModel.cs` — repoint the
  palette entry to `CloseProjectCommand` ("Close Project").
- `VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectWizardViewModel.cs` —
  `Mode`, `BuildCreateOptions()`, `IsLocationLocked`, `FinishAction`.
- `VisualGameStudio.Shell/Views/Dialogs/NewProjectSelectView.axaml(.cs)` — Back
  button + Back outcome in NewSolution mode.
- `VisualGameStudio.Shell/Views/Dialogs/NewProjectConfigureView.axaml(.cs)` —
  Location read-only + hide git/solution-folder checkboxes + mode-aware labels
  when locked.
- `VisualGameStudio.Shell/Views/Dialogs/NewSolutionView.axaml(.cs)` +
  `ViewModels/Dialogs/NewSolutionViewModel.cs` — **new** solution-details window.
- `VisualGameStudio.ProjectSystem/Services/SolutionWizardMapper.cs` — **new** pure
  mappers (placement in ProjectSystem so tests reach it without the Shell).
- **Removed:** `AddProjectToSolutionViewModel.cs`, `AddProjectToSolutionDialog.axaml(.cs)`.
- `VisualGameStudio.Tests/*` — new/updated tests above.
