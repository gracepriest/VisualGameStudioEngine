# Wizard C++ options wiring, C++ color-picker parity, and build quiet policy

**Date:** 2026-07-20
**Status:** Approved design (user approved all five sections in session)
**Base:** master @ `dd09b91`
**Approach:** point changes on existing seams — no new architecture, no template-system unification (explicitly declined).

Three user-requested workstreams, one batch:

1. **Wizard wiring** — the New Project wizard shows C++ toolchain and C++ standard
   pickers that are display-only (`NewProjectWizardViewModel.cs:261`). Wire them
   for real: the standard flows into the generated `.blproj`, and the toolchain
   becomes a new per-project `<CppToolchain>` property that builds enforce.
2. **Color-picker parity** — the inline color swatch/picker that BasicLang game
   code gets must also work in C++ game projects, without importing the latent
   false-positive hazards of the current ungated implementation.
3. **Build quiet policy** — after builds, nothing may pop up except the Output
   pane and a short self-dismissing toast. Panels already open stay open.

User decisions locked in during brainstorming:

- Build feedback: **"Quiet toasts"** — kill all build popups except the Output
  reveal and a ~3-second auto-dismissing success/failure toast with no action buttons.
- Color coverage: **calls + `0x` hex + `Color{}` brace literals** (plus the
  `.bas` `Framework_` prefix fix and a real per-language gate).
- Missing toolchain: **wizard-gated + hard error** — the wizard greys uninstalled
  toolchains at creation time; a build whose `<CppToolchain>` isn't installed
  fails with a clear diagnostic. No silent fallback.

---

## 1. Wizard → project file threading + toolchain gating

### 1.1 Options threading

`CreateProjectOptions` (`VisualGameStudio.Core/Abstractions/Services/IProjectTemplateService.cs:524-575`)
gains two nullable fields:

```csharp
public string? CppStandard { get; set; }    // "c++14" | "c++17" | "c++20" | "c++23"
public string? CppToolchain { get; set; }   // "llvm" | "gcc" | "msvc"
```

The wizard's `CreateProjectAsync` (`NewProjectWizardViewModel.cs:243-285`) copies
its `CppStandard` observable and `SelectedBackend.ToolchainId` into the options
when `SelectedLanguage == ProjectLanguage.Cpp`; both stay null for BasicLang
projects. The ":261 display-only" comment is removed. Note
`CreateSolutionOptions.InitialProjects` reuses `CreateProjectOptions`
(`IProjectTemplateService.cs:605`), so there is no second shape to extend.

### 1.2 Canonical vocabulary and back-compat

`<CppToolchain>` values are exactly the wizard ids: `llvm` | `gcc` | `msvc`,
parsed case-insensitively. **Absence of the element means today's behavior:
machine probe.** Existing `.blproj` files need no migration and behave
identically.

### 1.3 Emission

`ProjectTemplateService.GenerateProjectFileContent`
(`VisualGameStudio.ProjectSystem/Services/ProjectTemplateService.cs:257-340`,
hardcode at `:284`) emits `<CppStandard>` from the option (falling back to
`c++20`) and `<CppToolchain>` whenever the option is non-null.

The wizard sets `CppToolchain` **only when the selected toolchain was actually
available at creation time** (per the §1.4 probe). If the user proceeds on a
machine with no toolchain installed, the element is omitted: the project
self-heals onto the machine probe once anything is installed, and never
hard-errors on a "selection" the user couldn't meaningfully make.

Both serializers learn the new element:

- IDE side: `VisualGameStudio.ProjectSystem/Serialization/ProjectSerializer.cs`
  (parse `:66-73` area, serialize `:211-217` area) round-trips `<CppToolchain>`
  into a new `CppToolchain` property on `CppSettings`
  (`VisualGameStudio.Core/Models/BasicLangProject.cs:68-74`). Emit only when
  non-null.
- Compiler side: `BasicLang/ProjectSystem/ProjectFile.cs` (parse `:115-144`,
  serialize near `:288`) gains a `CppToolchain` string property, emitted
  whenever set — **not** gated on `IsCppProject`, because mixed
  BasicLang+Cpp-backend projects also build natively (`IsNativeProject`,
  `:50-54`). (Known asymmetry left unchanged: the existing `<CppStandard>`
  emit at `:288` is `IsCppProject`-gated, so mixed projects get it only from
  the IDE serializer.)

The CLI's parallel template system (`BasicLang/ProjectSystem/TemplateEngine.cs`,
hardcodes at `:454/:490/:521`) stays hardcoded at the `c++20` default —
CLI-created projects behave exactly as today. The reciprocal sync-contract
comments (`ProjectTemplateService.cs:1047-1050`, `TemplateEngine.cs:434-438`)
are updated to describe the new divergence rule: the IDE emits user-chosen
values; the CLI emits defaults. A background-task chip proposes a
`BasicLang.exe new --cpp-standard` flag as future work.

### 1.4 Wizard probing and greying

New lightweight availability probe on `CppToolchain`
(`BasicLang/ProjectSystem/CppToolchain.cs`):

```csharp
public static CppToolchainAvailability ProbeAvailability();
// flags: LlvmFound, GccFound, MsvcFound
```

Implementation constraint: `clang++`/`g++` presence via PATH lookup, MSVC via
vswhere **only** — never `vcvars64` (that spawn costs seconds and belongs to
real builds). The probe is injectable at the wizard viewmodel boundary
(delegate or small interface) so tests never depend on the machine.

Wizard behavior at open:

- Run the probe asynchronously (never block the UI thread).
- Uninstalled toolchains render disabled with a "(not installed)" hint.
- If the current selection is unavailable, auto-select the first available.
- If **none** are installed: all options remain visible-but-disabled, a warning
  is shown ("No C++ toolchain detected — this project won't build until one is
  installed"), and project creation is still allowed. Creating is not building;
  blocking creation on an empty machine is hostile.

### 1.5 Standards per toolchain

The current list `c++20 / c++17 / c++14` (`NewProjectWizardViewModel.cs:81-82`)
is valid on all three toolchains (`/std:c++14|17|20` on MSVC, `-std=` elsewhere)
and stays. Add `c++23`, offered **only** when the selected toolchain is
llvm or gcc (MSVC has no `/std:c++23`). Switching to MSVC while `c++23` is
selected snaps the selection back to `c++20`. The bound strings remain the
verbatim flag values.

---

## 2. Build-side toolchain enforcement

### 2.1 Resolve-by-id

`CppToolchain` gains a factory beside the existing `Find()`
(`CppToolchain.cs:52-105`; constructor is private at `:44`):

```csharp
public static CppToolchain? TryFindById(string id); // "llvm" | "gcc" | "msvc"
```

`llvm` probes only clang++, `gcc` only g++, `msvc` only vswhere+vcvars64.
`CppProjectBuilder.Build` (`BasicLang/ProjectSystem/CppProjectBuilder.cs:63-64`,
resolver seam already a `Func<CppToolchain>` at `:136`) resolves by the
project's `CppToolchain` when set, machine probe (`Find()`) when absent.
Because both the CLI and the IDE build through `CppProjectBuilder`
(`ProjectFile.IsNativeProject`, `ProjectFile.cs:50-54`), one change covers both
entry points. The compile_commands.json driver/dialect invariant
(`CppProjectBuilder.cs:336-341`) holds automatically — flags derive from the
resolved toolchain.

No IDE→compiler hand-off code is needed: `BuildService.BuildCppProject`
re-loads the `.blproj` from disk via `ProjectFile.Load`
(`VisualGameStudio.ProjectSystem/Services/BuildService.cs:1040-1042`) — there
is no `CppSettings`→`ProjectFile` value copy anywhere. Teaching `ProjectFile.cs`
to parse `<CppToolchain>` (§1.3) therefore covers the IDE build automatically;
the IDE-side `CppSettings.CppToolchain` exists for the wizard/serializer
round-trip, not for the build.

### 2.2 Hard error

Requested-but-missing produces **BL6015** (verified the next free BL60xx id @
`dd09b91` — nothing above BL6014 exists; implementer re-confirms with a grep
before claiming it):

> BL6015: C++ toolchain 'gcc' requested by the project is not installed.
> Detected: msvc. Install GCC or change `<CppToolchain>` in the project file.

One sentence stating requested, detected (from `ProbeAvailability()`), and both
remedies. The build stops; no fallback.

### 2.3 Non-goals

- Debugger pairing stays informational. All three toolchains route to lldb-dap;
  `<CppToolchain>` does not change adapter selection.
- No DWARF5 warning this pass (gcc emits DWARF5 which lldb 19 cannot set
  breakpoints in until the 22.x zip ships — that is debugger-side state, and
  the acquisition chip already tracks it).

### 2.4 Measured check: engine .lib on mingw toolchains

`VisualGameStudioEngine.lib` is MSVC-built; game templates on llvm/gcc link it
with a mingw linker. Believed fine (`extern "C" __cdecl`), never verified live.
The plan includes an early gate task: link a cpp-game project against the .lib
with winlibs clang++ and g++ (winlibs lives at `C:\winlibs\mingw64`, kept off
PATH). **If the gate fails, the remedy is wizard-side: grey non-MSVC toolchains
for game templates specifically** — do not ship a combination that cannot link.

---

## 3. Color picker: patterns, gating, apply paths

Current mechanism (all verified @ dd09b91): a client-side regex background
renderer, `VisualGameStudio.Editor/TextMarkers/InlineColorRenderer.cs`
(attached unconditionally for every document at
`CodeEditorControl.axaml.cs:701-704`), a hand-built `ColorPickerPopup`
(`VisualGameStudio.Editor/Controls/ColorPickerPopup.cs`), and an apply path in
`CodeEditorControl.OnColorPicked` (`CodeEditorControl.axaml.cs:4782-4834`).
There is **no language gate** — the "BasicLang-only" behavior is emergent from
the regex shapes. LSP is not involved and cannot be: neither the BasicLang LSP
server nor clangd implements `textDocument/documentColor`. Regex stays the
mechanism.

### 3.1 Real language gate

`CodeEditorControl.SetLanguageService(service, filePath)`
(`CodeEditorControl.axaml.cs:407-416`) already receives the file path; it
forwards the path (or a derived language tag) to `InlineColorRenderer`, which
selects a pattern set by extension. The extension→language classification
**reuses the canonical map** in
`VisualGameStudio.Core/Utilities/LanguageFileTypes.cs` (BasicLang set `:53`,
C++ set `:65`) — do **not** hand-roll a third list (standing rule: two
language-id maps already exist in this codebase and must not be merged or
multiplied). That means the gate inherits the canonical sets exactly
(BasicLang: `.bas .bl .mod .cls .class`; C++: `.cpp .cc .cxx .h .hpp .hh
.hxx .inl`).

- **Any extension outside those sets: no color patterns** — killing today's
  latent false positives in arbitrary file types.

### 3.2 Pattern sets

**BasicLang** — today's behavior plus the fix:

- Function-call pattern (whitelist `ColorFunctions`,
  `InlineColorRenderer.cs:54-70`, values ≤255 validation retained) now strips a
  `Framework_` prefix before the whitelist check, so both `ClearBackground(0,0,0)`
  and `Framework_ClearBackground(10,10,25,255)` light up.
- `&H` hex (`&HRRGGBB` / `&HAARRGGBB`) unchanged.

**C++**:

- Function calls **require** the `Framework_` prefix (strip → same whitelist).
  This matches how C++ game projects call the engine C-ABI and is the
  false-positive killer: raylib's unprefixed `DrawRectangle(10,20,100,50)` has
  an x/y/w/h integer tail and must never light up.
- `0x` hex: `0xRRGGBB` / `0xAARRGGBB` — same byte order as `&H` for IDE
  consistency.
- Brace literals: `Color{r,g,b,a}`, `(Color){r,g,b,a}`, `CLITERAL(Color){r,g,b,a}` —
  a `Color`-token prefix is required; bare `{255,0,0}` never matches. 3 or 4
  components, each ≤255.

A plan task audits `VisualGameStudioEngine/framework.h` for all exports with
`r,g,b,a` color tails and tops up the whitelist (known examples at
`framework.h:298/:300/:1494-1502/:2169`; there is no `CreateColor` — that name
does not exist in the engine). The same audit confirms the engine exposes **no
RGBA-order hex color API** (raylib's `GetColor` convention is `0xRRGGBBAA`) —
if one exists, its literals must be excluded from `CppHex` detection rather
than silently misread in `AARRGGBB` order.

### 3.3 Apply-back by kind

Each match carries a `Kind` (`RgbCall`, `VbHex`, `CppHex`, `BraceInit`) through
the `ColorSwatchClicked` event args, and `OnColorPicked` switches on it — no
sniffing the old text. Each kind rebuilds its own shape:

- `RgbCall`: `R, G, B[, A])` — end offset includes the closing paren (existing
  behavior, `CodeEditorControl.axaml.cs:4810-4819`); alpha-preservation
  heuristic retained.
- `VbHex`: `&H[AA]RRGGBB` (existing, `:4800-4807`).
- `CppHex`: `0x[AA]RRGGBB`, preserving 6- vs 8-digit form.
- `BraceInit`: the replacement range starts at the **opening brace** (the
  `Color` / `(Color)` / `CLITERAL(Color)` prefix survives untouched) and ends
  at the closing brace inclusive. Arity is preserved: a 3-component literal is
  rewritten with 3 components unless the picked alpha is < 255 (then 4) —
  mirroring the `RgbCall` alpha heuristic.

**Ordering rule: each new pattern lands in the same implementation task as its
apply branch.** Detection must never outrun rewriting — an unrecognized literal
falling into the RGB-rebuild branch corrupts the document.

### 3.4 Testability extraction

Detection moves out of the UI `Draw()` loop (`InlineColorRenderer.cs:94-166`)
into a pure `ColorMatchFinder` (line text + language in → typed matches with
offsets and `Kind` out); the rewrite side gets an equally pure
`ColorTextRewriter` (kind + old text + picked color → new text). The renderer
keeps only geometry — including the mandatory `TextView.ScrollOffset`
subtraction (`InlineColorRenderer.cs:179-188`) — and the control's
`OnColorPicked` becomes a thin `document.Replace` call. The color path
currently has zero tests because it is welded to the TextView; both pure
classes get dense headless NUnit coverage.

---

## 4. Build quiet policy

Inventory base (all verified @ dd09b91): the sole `BuildCompleted` subscriber is
`MainWindowViewModel` ("MWVM", `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs:541`,
handler `:1578-1626`). The activation primitive `DockFactory.ActivateTool`
(`VisualGameStudio.Shell/Dock/DockFactory.cs:703-735`) reopens + raises + focuses.

### 4.1 What dies

| Change | Site | Detail |
|---|---|---|
| Delete ErrorList auto-activation | MWVM:1616 | Population (`:1621-1622`), Problems badge (`:687-691`), and status-bar counts (`:1591`) all stay. No test pins the activation. |
| Delete F5 "Build Failed" modal | MWVM:3555-3557 | Failure toast + Output already report it; nothing consumes the dialog result. |
| Demote "No executable found" modal to an Output line | MWVM:3564-3566 | Matches the Ctrl-F5 precedent at `:3747`. |
| Fix the per-project popup amplifier | MWVM:6749, loop :6773-6790 | The Build Solution command switches to the combined `_buildService.BuildSolutionAsync` path the Build command already uses (`:3022-3025`, single `BuildCompleted` from `BuildService.cs:174`) — one result event per gesture. |

### 4.2 The quiet toast

Success (`MWVM:1597-1601`) and failure (`:1608-1613`) toasts lose their action
buttons and become explicitly auto-dismissing. The mechanism **already exists
end-to-end**: `NotificationEventArgs.AutoDismiss` (`MWVM:8488`) is honored
regardless of severity at `MainWindow.axaml.cs:431`. The actual gaps are two:
(a) the actions-overload of `ShowNotification` computes
`autoDismiss = info && no actions` internally with no caller override
(`MWVM:1821`) — it gains an optional override parameter; and (b) the dismiss
timer is fixed at 5 s (`MainWindow.axaml.cs:434`) — it gains an optional
per-toast duration, with quiet build toasts using a named ~3 s constant and
everything else keeping 5 s. (Today "Build succeeded" persists forever because
it carries an action button, and a severity-error "Build failed" would persist
even without one.) Every toast still lands silently in the status-bar
notification center (`MWVM:1813/:1831`), so history is preserved.

### 4.3 What stays, deliberately

- Output reveal via `ShowBuildOutput()` (`MWVM:2953-2960`), gated by
  `build.showOutput` (`SettingsService.cs:1235`). The bypassing stragglers —
  task-runner activations (`MWVM:7432/:7455/:7482`) and adapter-missing
  (`:3660`) — are routed through `ShowBuildOutput()` for consistency.
- "Nothing is open" guard modals (`MWVM:2996-2998/:6753-6755/:3506-3508`) —
  direct feedback to a user-invoked command, not build noise.
- Debug-session UX: `ShowDebugPanels` (`MWVM:3174-3190`), debug-started toast
  (`:3163`), foreground-on-break (`:3214-3219`), lldb-dap offer toast
  (`:3670-3676`). Debugger behavior, untouched this pass.

**No new settings.** This is policy, not preference surface; `build.showOutput`
remains the only gate.

---

## 5. Testing strategy

Suite baseline: 3293 passed / 1 known BL6009 flake / 2 known skips. Full suite
before every commit.

- **Wizard**: viewmodel tests (extend
  `VisualGameStudio.Tests/NewProjectWizardViewModelTests.cs`) for option
  threading (C++ fills both fields; BasicLang leaves them null), `c++23`
  visibility + MSVC snap-back, and greying/auto-select driven by the injected
  probe.
- **Generation/serialization**: `GenerateProjectFileContent` emits chosen
  values; `<CppToolchain>` round-trips in both serializers
  (`ProjectSerializerCppTests`, `CppProjectFileTests`); BasicLang projects emit
  no toolchain element (mirror `CppProjectFileTests:112`);
  `TemplateBuildSweepTests` needs no change — it compares generated **source
  files** only (`:160-166`), never `.blproj` content, so the IDE-only emission
  of chosen values cannot break it.
- **Enforcement**: BL60xx missing-toolchain error via injected probe; one
  machine-conditional e2e (this machine has MSVC only on PATH, so requesting
  `gcc` genuinely errors); the engine-.lib link gate (§2.4) as an early plan
  task whose verdict decides the game-template greying question.
- **Color picker**: dense table tests over `ColorMatchFinder` and
  `ColorTextRewriter`: every kind × both languages, ≤255 validation, prefix
  rules, 6- vs 8-digit hex, alpha preservation, and the false-positive table
  (`DrawRectangle(10,20,100,50)` in .cpp, bare braces, non-code file
  extensions → no matches).
- **Build quiet**: pin "failed build does not activate ErrorList" and "build
  toasts carry auto-dismiss" through the existing in-anger MWVM harness where
  practical; toast visuals and wizard greying land in the end-of-batch manual
  IDE smoke.

Existing pins that must keep passing: `SettingsBuildBasicLangWiringTests:71-80`
(`build.showOutput` resolve), `DockReopenResilienceTests:100-120` (Output
activation resilience), `NewProjectWizardViewModelTests:245-249`
(`ShowCppStandardSelector`).

---

## 6. Cuts (explicit non-goals)

- CLI `BasicLang.exe new --cpp-standard` flag — filed as a background-task chip
  instead.
- Template-system unification (IDE consuming the CLI `TemplateEngine`) —
  declined with Approach 1.
- Debugger-pairing changes and the DWARF5 warning — deferred (§2.3).
- Post-creation editing UI for `<CppToolchain>`/`<CppStandard>` — remains a
  hand-edit of the `.blproj`; the BL60xx message names the element.
- Named-color detection (`RED`, `WHITE`), multi-line/nested color arguments.
- Binding the dormant `InlineColorSwatchesEnabled` toggle
  (`CodeEditorControl.axaml.cs:4848-4856`, zero callers today).
