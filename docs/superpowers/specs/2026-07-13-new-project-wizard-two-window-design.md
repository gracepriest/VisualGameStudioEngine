# New Project — two-window wizard (UI redesign)

**Date:** 2026-07-13
**Status:** Design approved (pending spec review + user review)
**Scope:** UI only. No changes to `IProjectTemplateService`, `CreateProjectOptions`,
the `.blproj` schema, or the build/creation pipeline.

## Motivation

The current New Project experience is a single window (`CreateProjectView` /
`CreateProjectViewModel`) that crams solution-type selection, template browsing,
and all project configuration onto one form. The redesign splits this into a
two-step wizard so the two concerns — *what am I making* and *how is it
configured* — are separated, and so the language / backend axis is surfaced
explicitly rather than hidden inside a "Solution Type" combo box.

This is a presentation-layer reorganization of an existing, working dialog. It
adds no project-creation capability; it re-presents what the engine already
supports.

## Current state (what exists today)

- **Live dialog:** `CreateProjectView` (Avalonia `Window`) + `CreateProjectViewModel`.
  Opened only from `MainWindowViewModel.NewProjectAsync()` (`MainWindowViewModel.cs:1709`).
- **Single entry seam:** the File menu, toolbar button, command palette, and the
  Welcome screen card all execute `NewProjectCommand`, which runs
  `NewProjectAsync()`. The Welcome card's `NewProject()` invokes a callback wired
  at `MainWindowViewModel.cs:445` to `NewProjectCommand.Execute(null)`. So there
  is exactly one place that constructs the dialog.
- **Backing service:** `IProjectTemplateService` already models the domain:
  - `SolutionType` — `dotnet` (C#), `msil`, `native` (BasicLang→C++), `llvm`,
    and `cpp` (pure C++). This is the language/backend axis.
  - `ProjectTemplate` — has `Category`, `Tags`, `SupportedSolutionTypes`,
    `Description`. `GetProjectTemplates(solutionType)` filters by
    `SupportedSolutionTypes.Contains(id)`.
  - `CreateProjectOptions` — `Name`, `Location`, `SolutionType`, `Template`,
    `CreateSolutionFolder`, `CreateGitRepository`, `TargetFramework`, `Namespace`.
  - Template search already filters on name / description / tags.
- **C++ toolchain is build-time only.** `CppToolchain.Find()` probes
  `clang++` → `g++` → MSVC (vswhere) at build time. A generated C++ `.blproj`
  writes only `<Language>Cpp</Language>` + `<CppStandard>c++20</CppStandard>` —
  there is **no toolchain field** in the project, and creation does not record a
  toolchain choice.
- **Dead code:** `NewProjectDialog` + `NewProjectViewModel` — an older, hardcoded
  4-template dialog wired to nothing. Not referenced by any live entry point.

## Design

### Windows

**Window 1 — "Select a project type"** (the *what*):

- **Language** selector: `BasicLang` / `C++` (segmented control or two buttons).
- **Backend** selector, contents depend on Language:
  - BasicLang → `C# (.NET)`, `MSIL`, `Native C++`, `LLVM` — these map 1:1 to the
    existing `SolutionType`s `dotnet` / `msil` / `native` / `llvm`.
  - C++ → `LLVM (clang++)`, `GCC (g++)`, `MSVC` — the C++ *toolchain*. Language=C++
    always uses the single `cpp` `SolutionType`; the toolchain choice is
    **display-only** this pass (see "Surfaced but not wired").
- **Platform** filter: `All` / `Windows` / `Cross-platform`. A UI-side
  classification (a static map in the wizard VM) that narrows the template list.
  Not written into the project.
- **Project type** filter: the existing `ProjectTemplate.Category` values
  (`Console`, `Games`, `Desktop`, `Library`, `Web`, `Testing`) plus `All`.
  Narrows the template list.
- **Platform classification** (UI-side, for the Platform filter). `Windows`
  shows everything (all templates run on Windows); `Cross-platform` excludes the
  Windows-only UI frameworks; `All` applies no filter. Full map over the 11
  built-in templates:
  | Template id | Bucket |
  |---|---|
  | `winforms-app`, `wpf-app` | Windows-only |
  | `console-app`, `game-app`, `avalonia-app`, `class-library`, `web-api`, `unit-test`, `cpp-console-app`, `cpp-library`, `cpp-game-app` | Cross-platform |
  This map lives in the wizard VM; it is not persisted and does not affect the
  created project. (If a template set changes later, the map is the one place to
  update.)
- **Template search box** + **template list**: reuse the existing
  name/description/tags filter, scoped to the selected backend's templates and
  further narrowed by the platform + category filters.
- Buttons: **Cancel**, **Next** (enabled once a template is selected).

**Window 2 — "Configure your project"** (the *how*):

- **Read-only description panel**: recap of the selected template (name +
  description + tags) and the chosen language/backend — the same read-only
  template-info panel the old form showed. No editable/persisted description.
- **Project name** (with the existing warn-but-allow special-character hint).
- **Location** + **Browse…** (folder picker, same `StorageProvider` code as the
  old view's `BrowseLocationAsync`).
- **Target framework / C++ standard** dropdown:
  - `.NET` / `MSIL` → the existing `TargetFramework` list (`net8.0` …).
  - C++ → a C++ standard list (`c++20` / `c++17` / `c++14`), default `c++20`.
    A non-default choice is **display-only** this pass.
- **Options**: `Create solution folder`, `Initialize Git repository` (existing).
- **Advanced options** expander: `Custom namespace` (existing).
- Buttons: **Back** (returns to Window 1, state preserved), **Create**.

### ViewModel

A single `NewProjectWizardViewModel` owns all wizard state and all filtering
logic. Both windows bind to the *same instance*, so Back/Next never lose state
and the two windows cannot drift.

Responsibilities:

- Load `SolutionType`s from `IProjectTemplateService` and expose the Language →
  Backend mapping.
- On Language / Backend / Platform / Category / Search change, recompute the
  visible template list (backend scope first, then platform + category + search).
- Expose `CanGoNext` (a template is selected) and `CanCreate` (name + location
  present, not already creating).
- On Create, build a `CreateProjectOptions` from state exactly as
  `CreateProjectViewModel` does today, call
  `_templateService.CreateProjectAsync(options)`, and surface the result.
- Raise view-agnostic events/commands for window transitions
  (`NextRequested`, `BackRequested`, `ProjectCreated`, `Cancelled`) so the views'
  code-behind drives the actual window show/close. The VM references no `Window`.

### Window orchestration

`NewProjectAsync()` opens Window 1 and awaits a `ProjectCreationResult?`. Window 1's
code-behind, on `NextRequested`, shows Window 2 modally (same VM as DataContext).
Window 2's Create sets the result and closes; Back closes Window 2 and returns to
Window 1. A Cancel from either window ends the flow with no result. The result
flows back to `NewProjectAsync()`, which opens the created project exactly as it
does today. The exact `ShowDialog` plumbing is an implementation detail for the
plan; the contract is: `NewProjectAsync()` still ends up with a
`ProjectCreationResult?` and its downstream open-project logic is unchanged.
Note the new flow replaces today's `ShowDialog<bool?>` + `dialog.Result` pair
(the old `CreateProjectView` returns `bool?` and exposes a separate `Result`
property); the plan must define how the two-window flow surfaces the final
`ProjectCreationResult?` back to `NewProjectAsync()`.

### Surfaced but not wired (honest UI-only boundary)

Three controls are shown because the user wants them visible, but do **not**
change the created project this pass — because there is nowhere in the current
model to store them and creation ignores them:

1. **C++ toolchain** (LLVM/GCC/MSVC) — no `.blproj` field exists; the build layer
   auto-discovers the toolchain. Remembered in wizard state only.
2. **Non-default C++ standard** — `CppStandard` *is* a real, persisted `.blproj`
   field that the build already honors (`ProjectSerializer`/`ProjectFile`
   round-trip it; `CppToolchain` emits `-std`/`/std` from it; a test pins a
   non-default `c++17` surviving a save). It is display-only here only because
   `ProjectTemplateService.GenerateProjectFileContent` hardcodes `c++20`
   (`ProjectTemplateService.cs:284`) and `CreateProjectOptions` has no
   `CppStandard` field. The dropdown default matches (`c++20`), so a non-default
   selection has no effect this pass. Future wiring is therefore *small* — add a
   `CppStandard` to `CreateProjectOptions` and thread it into the generator — not
   a schema change.
3. **Platform filter** — filters the template list only; not persisted.

Each becomes a clean follow-up when the build/creation layer is extended to
honor it. This is called out so reviewers and users know these are intentional,
not oversights.

## Files

**Add:**

- `VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectWizardViewModel.cs`
- `VisualGameStudio.Shell/Views/Dialogs/NewProjectSelectView.axaml` (+ `.axaml.cs`) — Window 1
- `VisualGameStudio.Shell/Views/Dialogs/NewProjectConfigureView.axaml` (+ `.axaml.cs`) — Window 2

**Change:**

- `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs` — `NewProjectAsync()`
  opens the new wizard instead of `CreateProjectView`. This is the only call-site
  change; all entry points already route here.

**Preserve (leave in tree, unreferenced):**

- `CreateProjectView` / `CreateProjectViewModel` — "save the old new project."

**Delete (dead code, per user choice):**

- `VisualGameStudio.Shell/Views/Dialogs/NewProjectDialog.axaml` (+ `.axaml.cs`)
- `VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectViewModel.cs`

## Testing

VM-level unit tests (headless — no Avalonia view needed), following the repo's
NUnit convention:

- Language=BasicLang exposes backends {C#, MSIL, Native C++, LLVM}; Language=C++
  exposes {LLVM, GCC, MSVC} and fixes the `cpp` `SolutionType`.
- Selecting a backend loads that solution type's templates; switching Language
  updates the backend list and template list.
- Platform filter narrows the list (e.g. Cross-platform excludes WinForms/WPF).
- Category filter narrows the list; search filters by name/description/tags;
  filters compose.
- `CanGoNext` requires a selected template; `CanCreate` requires name + location.
- State → `CreateProjectOptions` mapping: C++ language yields `cpp` SolutionType;
  .NET yields the chosen `TargetFramework`; namespace/options flow through.
- Guard test: `MainWindowViewModel` (or `NewProjectAsync`) no longer references
  `CreateProjectView` — the app calls the new wizard only.

## Non-goals / future work

- Persisting the C++ toolchain or platform into the project (each needs a new
  `.blproj` field + build-layer support that does not exist today).
- Wiring the C++ standard dropdown to the created project. Unlike the two above,
  the `.blproj` field and build support already exist — this is a small future
  follow-up (`CreateProjectOptions.CppStandard` + generator), intentionally out
  of scope for this UI-only pass.
- An editable, saved project description (no such field in the model today).
- Changing the set of templates, solution types, or creation behavior.

## Risks

- **Two-window modal plumbing** in Avalonia (owner, focus, returning a result
  across two `ShowDialog` calls). Mitigated by keeping the VM view-agnostic and
  confining window management to code-behind, mirroring the existing
  `CreateProjectView` event pattern.
- **Deleting dead code** could break a stray reference. Mitigated by a
  pre-deletion grep confirming `NewProjectDialog`/`NewProjectViewModel` have no
  live callers (current evidence: none).
