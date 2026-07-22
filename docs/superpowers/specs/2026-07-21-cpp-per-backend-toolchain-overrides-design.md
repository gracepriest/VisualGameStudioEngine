# Per-backend C++ compiler & debugger overrides in Settings

**Date:** 2026-07-21
**Status:** Approved design (user approved all six sections in session)
**Base:** master @ `ba2f8f7`
**Approach:** Approach A — an override-reader + a pure validator plus six flat
settings keys, wired through *existing* extension seams (the `CppProjectBuilder`
resolver seams, a widened debug-launch resolver, the wizard probe). No new
persistence shape, no profile object (Approach B explicitly declined), no extra
debug-adapter descriptors (Approach C explicitly declined).

## Motivation

Today a C++ toolchain is only an id (`llvm` | `gcc` | `msvc`) resolved on PATH
(or vswhere for MSVC) — there is **no notion of a compiler executable *path***
anywhere, and the single native debugger (`lldb-dap`) is located by a fixed
chain. A developer who keeps a toolchain off PATH (e.g. `C:\winlibs\mingw64\bin`,
which this project deliberately keeps off PATH) cannot point the IDE at it, and
cannot choose which debugger a given backend uses.

The user wants a Code::Blocks-style capability: **for each backend, the developer
picks the compiler and the debugger location; once set, that is what the IDE uses
for projects on that backend; the auto-probe must not overwrite the chosen path.**

User decisions locked in during brainstorming:

- **Scope:** three backends × two tools = **six override fields** (llvm/gcc/msvc,
  each: compiler + debugger). Dev-chosen. **Blank ⇒ today's behavior** (auto-probe
  for the compiler, lldb-dap locator chain for the debugger). "Default will be
  probe and lldb-dap debugger."
- **MSVC "compiler" field = a `vcvars64.bat` / VS-install path**, not a bare
  `cl.exe` (cl.exe needs the surrounding VS environment).
- **A set path is authoritative** — it marks that backend "installed" in the New
  Project wizard even when nothing is on PATH (the winlibs-off-PATH goal) — **but
  it is validated in the Settings menu** so a bad path is caught rather than
  silently trusted or silently reverted.
- **clangd stays as-is.** The existing single global `cpp.clangd.path`
  (the IntelliSense language server, a *third* tool distinct from compiler and
  debugger) is out of scope and untouched. It is not made per-backend.

---

## 1. Settings keys & storage

Six new keys join the existing flat `cpp` group in `~/.vgs/settings.json` (same
dot-key convention as `cpp.clangd.path`, registered in
`SettingsService.RegisterAllDefaultSchemas`, `SettingsService.cs:1258-1271`).
All default to `""` (empty = auto-detect):

| Key | Holds | Empty ⇒ |
|---|---|---|
| `cpp.toolchain.llvm.compiler` | path to `clang++.exe` | probe (PATH) |
| `cpp.toolchain.llvm.debugger` | path to a DAP adapter (`lldb-dap.exe`) | lldb-dap locator chain |
| `cpp.toolchain.gcc.compiler`  | path to `g++.exe` | probe (PATH) |
| `cpp.toolchain.gcc.debugger`  | path to a DAP adapter | lldb-dap locator chain |
| `cpp.toolchain.msvc.compiler` | path to `vcvars64.bat` (or a VS-install dir it derives it from) | probe (vswhere) |
| `cpp.toolchain.msvc.debugger` | path to a DAP adapter | lldb-dap locator chain |

**Decisions:**

- **Uniform `.compiler` / `.debugger` key names across all three backends** so one
  reader can loop `for id in {llvm,gcc,msvc}`. The MSVC `.compiler` slot
  semantically holds a vcvars/VS path; that difference lives in the *validation
  rule* (§2), not the key shape. (`cpp.toolchain.msvc.vcvars` was rejected — a
  special-case name forces every consumer to branch on backend.)
- **Written at User (global) scope** by the dialog — a machine-level toolchain
  config, like Code::Blocks' global compiler settings. Because the settings
  system already resolves Folder > Workspace > User
  (`SettingsService.GetEffectiveValue`, `SettingsService.cs:628-641`), a project
  *can* still override per-workspace via hand-edit — a free consequence, not a
  dialog feature.
- **The existing `cpp.lldbDap.path` stays and stays a fallback.** A per-backend
  debugger override sits *above* today's locator chain; a blank per-backend field
  falls through to the current chain
  (`cpp.lldbDap.path` → `~/.vgs/tools` → PATH → known dirs). Nothing existing
  breaks. `cpp.lldbDap.path` is not newly surfaced in the dialog (unchanged).
- **Each key registers a settings consumer** via `SettingsConsumerRegistry` so the
  `SettingsConsumerContractTests` "every dialog key has a registered consumer"
  guard stays green.

---

## 2. Validation rules

All validation lives in one pure class, `ToolchainPathValidator`
(`VisualGameStudio.ProjectSystem`), so it is unit-testable without a UI or a real
toolchain.

- **Input:** `(backendId, kind = Compiler | Debugger, path)`.
- **Output:** `ValidationResult { Status, Message, DetectedVersion? }`, status ∈
  `Empty | Valid | Warning | Invalid`.

**Two-tier check per field:**

1. **Existence (the gate — fast, deterministic, runs live):** the path must name
   an existing file. For MSVC's compiler slot the target is a `vcvars64.bat`, or a
   directory that contains `VC\Auxiliary\Build\vcvars64.bat` (the resolver derives
   the `.bat`). Missing ⇒ **Invalid**. This is the check that gates
   authoritative-ness and the one the §3/§4 resolver re-uses at build/debug time.
2. **Version smoke (enrichment — runs on Browse / commit / dialog-open, never per
   keystroke):** for exe fields, run `<path> --version` with a short timeout (the
   same technique `CppToolchain.IsOnPath` already uses, `CppToolchain.cs:109-128`),
   behind an **injected seam** so tests do not spawn processes. Success ⇒ **Valid**
   with the version string (`clang++ 18.1.8`). Exists but the smoke cannot confirm
   it — timed out, or the filename is not a recognized driver
   (`clang++`/`clang`/`clang-cl`, `g++`/`c++`/`gcc`, `lldb-dap`) ⇒ **Warning**
   ("found, couldn't confirm — using anyway"), so a wrapper or renamed binary still
   works.

**When it fires:** on the file-picker returning, on text-field commit (blur/Enter),
and on dialog open (so a path valid last week but since deleted shows Invalid now).
The `--version` run never happens on every keystroke.

**"Authoritative but invalid" behavior:** because the Settings dialog live-applies
edits (`SettingsViewModel.AutoSaveSettingToService`), a red/Invalid value is still
*stored* (the developer does not lose their typing) — but it is flagged red in the
dialog, and at build/debug time an invalid-but-set path produces a **hard error**,
never a silent fall-back to auto-detect (§3, §4). That is the point of the
authoritative choice: a broken override announces itself instead of quietly
reverting.

**One definition, two call sites:** the same validator's existence check is what
the §3/§4 resolver calls at build/debug time.

---

## 3. Compiler wiring (build-time)

Guiding constraint (from recon): `CppProjectBuilder` and `CppToolchain` live in
the compiler (`BasicLang/`) and have **no settings access** — they are shared with
the CLI. The override is therefore *injected as data* by the settings-aware IDE
layer; the compiler stays settings-agnostic.

### 3.1 `CppToolchain` explicit-path factories

Today the ctor is private (`CppToolchain.cs:66`) and `_executable` holds a bare
name. Add:

- `FromExplicitCompiler(string id, string exePath)` for llvm/gcc → `_executable`
  becomes the **full path**, so the process launch bypasses PATH entirely (this is
  what makes winlibs-off-PATH work). `DriverName` (used for
  `compile_commands.json`) returns the same explicit path.
- **MSVC reuses the existing mechanism.** `CppToolchain.Compile` already runs
  `cmd /s /c ""<vcvarsPath>" >nul && cl …"` (`CppToolchain.cs:271-289`); the
  override just substitutes the user's `vcvars64.bat` for the vswhere-discovered
  one via the existing private-ctor `_vcvarsPath` field. No new compile path.

### 3.2 `BuildService` owns the wiring

`BuildService.BuildCppProject` (`BuildService.cs:1090-1102`) already has
`ISettingsService`. It builds override-aware resolvers from the `CppToolchainOverrides`
reader (§2 validator applied) and passes them through the seams `EmitCore` already
exposes (`CppProjectBuilder.cs:67-69, 145-148, 338-342`):

- `resolveById` — used for a pinned `<CppToolchain>`.
- `resolveToolchain` — used for the unpinned machine-probe path.

Per backend: a valid override ⇒ `FromExplicit…`; a blank field ⇒ today's
`TryFindById` / `Find`. The override is **override-aware for both pinned and
unpinned** projects — the unpinned `Find` equivalent iterates `llvm,gcc,msvc` and
treats a backend as available when its override is valid *or* it is on PATH.

`CppProjectBuilder.Build()` grows two optional forwarding params (default `null`),
forwarding to `EmitCore`'s existing seams. **The CLI (`Program.cs:440`) passes
neither, so `BasicLang.exe` behavior is unchanged** — overrides are an IDE-settings
feature.

### 3.3 Invalid-but-set = hard error

`BuildService` pre-validates the chosen backend's override (the §2 validator). If
it is set but broken, the build fails with a clear Output message pointing at
Settings › C++ (e.g. "llvm compiler path is set but not found: `…` — fix or clear
it in Settings › C++"), never a silent fall-back to auto-detect. A blank field
still falls through to the probe; a pinned-but-absent toolchain still yields the
existing **BL6015** (`CppProjectBuilder.cs:347-351`).

### 3.4 Editing/building parity

The same override-aware resolver feeds the IntelliSense / `compile_commands.json`
emit path (`IntelliSenseEmitter.Emit` → `EmitCore` with `forIntelliSense:true`,
`CompileCommandsWriter`, `CppProjectBuilder.cs:482`) wherever the IDE drives it, so
clangd sees the same compiler the build uses — no drift between what compiles and
what IntelliSense reasons about.

---

## 4. Debugger wiring (F5)

**Blocker (from recon):** `lldb-dap` serves *all* native C++ regardless of
toolchain (`DebugAdapterDescriptor.cs:159-172`, routing on the single
`IsNativeBuild` boolean), and launch resolution (`ResolveLaunchCommand()` / the
`resolveExecutable` delegate) takes **no project argument** — it is a bare
`Func<string?>` (`DebugAdapterDescriptor.cs:73, 122, 159`). A per-backend debugger
override needs the project's backend.

### 4.1 Thread the project into launch resolution

Widen `DebugAdapterDescriptor.ResolveLaunchCommand()` to
`ResolveLaunchCommand(project)`, and the `LldbDap(...)` factory's resolver from
`Func<string?>` to `Func<project, string?>`. `DebugService` already holds the
project at the call site (it routed there via `GetFor(project)`,
`DebugService.cs:230-257`) — it just passes it in. The `BasicLangManaged`
descriptor ignores the arg. **No new adapters, no multiple descriptors** — routing
stays the single `IsNativeBuild` boolean; the per-backend choice happens *inside*
resolution.

### 4.2 Resolution order (per the project's pinned backend)

1. `cpp.toolchain.<backend>.debugger` — set & valid ⇒ authoritative.
2. Blank ⇒ today's `LldbDapLocator.Locate` chain
   (`cpp.lldbDap.path` → `~/.vgs/tools` → PATH → known dirs, winlibs included),
   fully backward-compatible.

The registration lambda (`ServiceConfiguration.cs:116`) becomes:

```csharp
DebugAdapterDescriptor.LldbDap(project =>
    debuggerOverrides.Resolve(project.CppToolchain)   // per-backend, validated
        ?? LldbDapLocator.Locate(settingsService))    // existing chain
```

`debuggerOverrides` is the **same** `CppToolchainOverrides` reader used for the
compiler (§2/§3) — one class serves both tools.

### 4.3 Scope choice

The per-backend debugger override applies when the project **pins** a backend
(`<CppToolchain>` set — which the wizard now always writes for these projects, per
the 2026-07-20 batch). An unpinned project falls to the default chain, rather than
inventing "last-built-with-backend-X" state. Deliberate simplification.

### 4.4 Invalid-but-set = error (mirrors §3.3)

`DebugService` (settings- and project-aware) pre-validates; a set-but-broken
debugger path aborts F5 with a clear message ("gcc debugger path is set but not
found: `…` — fix or clear it in Settings › C++"), distinct from the existing
"adapter not installed" text (`DebugLaunchPolicy.ComposeAdapterMissingMessage`,
`DebugLaunchPolicy.cs:85-98`), never a silent fall-through.

**Caveat (for the spec of record):** the override must point at a **DAP-speaking
adapter** (realistically an `lldb-dap` build — winlibs and LLVM each ship one).
Validation checks existence + filename shape; it cannot guarantee an arbitrary exe
speaks DAP.

---

## 5. Wizard / probe (availability)

Because everything routes through the probe, the wizard itself needs **almost no
change**.

### 5.1 Make the probe override-aware — at the ProjectSystem boundary

`CppToolchainProbeService` (`CppToolchainProbeService.cs:10-16`; it lives in
ProjectSystem and can take `ISettingsService`; today it merely wraps the pure
compiler-side probe) gains the `CppToolchainOverrides` reader. Its `Probe()`
returns, per backend:

> `available = onPath/vswhere  OR  validCompilerOverride`

So gcc pointed at winlibs off PATH arrives as `Gcc = true`. The compiler-side
`CppToolchain.ProbeAvailability()` (`CppToolchain.cs:95-96`) stays pure PATH/vswhere
(shared with the CLI, no settings) — the OR happens only in the ProjectSystem
adapter.

### 5.2 Availability keys off the *compiler* override only

Not the debugger — a project can be created and built without a debugger, so a
debugger path must not gate project creation.

### 5.3 The wizard is unchanged

`NewProjectWizardViewModel.ApplyToolchainAvailability` (`:197-228`) already
consumes the `(Llvm, Gcc, Msvc)` booleans, so an overridden backend automatically
un-greys, drops its `"(not installed)"` hint, passes the create-time gate
(`:401-403`), and gets c++23 offered by `RecomputeCppStandards` (`:235-259`). No
wizard-VM edits.

### 5.4 Broken override ≠ available

The probe ORs in "set **and valid**" (the fast existence check), so a set-but-broken
override leaves the backend greyed in the wizard while the Settings page shows the
red error — consistent, no "available but won't build."

### 5.5 Wizard/build agreement

Because §3's override-aware resolver produces a real toolchain for an overridden
backend, the build never trips BL6015 for it. "Shows available in the wizard" and
"actually builds" cannot disagree.

**Optional polish left OUT of v1** (keeps the probe's record a simple bool triple):
a distinct `"(configured)"` hint to signal *why* a backend is available. Enabled is
enabled for now.

---

## 6. Testing strategy & component map

Everything testable-in-isolation is pulled into pure/injectable units so nothing
depends on a DI-constructed dialog or a real toolchain. Standing lessons honored:
MWVM/dialogs are never constructed in tests (use pure classes + source-guards);
**winlibs stays off PATH** so fixtures use *fake existing files*, not a real
install; validate codegen through the CLI/optimizer (`CompileToCppOptimized`), not
only the non-optimizing unit helper; exercise **both** entry points (CLI and IDE).

### 6.1 Units & tests

| Unit (new unless noted) | Purpose | Key tests |
|---|---|---|
| `ToolchainPathValidator` (pure) | `(backend, kind, path)` → `Empty/Valid/Warning/Invalid` | empty→Empty; missing→Invalid; clang++→Valid(version) via **fake** version-probe seam; odd name→Warning; msvc dir→derives vcvars, missing→Invalid |
| `CppToolchainOverrides` (reader) | reads 6 keys, applies validator → tri-state + `HasValidCompilerOverride` | fake `ISettingsService`: blank/valid/broken per backend; consumer registration asserted |
| `CppToolchain` factories (BasicLang) | `FromExplicitCompiler` / msvc-vcvars | emitted **build command invokes the full override path** (assert via `CompileToCppOptimized` + CLI); msvc substitutes vcvars |
| `BuildService` wiring | override-aware resolvers + pre-validate | pinned-gcc + valid override, nothing on PATH → builds with override; set-but-broken → **hard error, no silent fallback**; **`Build()` with no override params = today's behavior (CLI unchanged)** |
| Descriptor + `DebugService` | project threaded into launch resolution | pinned gcc + valid debugger override → launches it; blank → existing locator chain (no regression); set-but-broken → F5 aborts with clear error; managed descriptor unaffected by widened signature |
| `CppToolchainProbeService` | OR override validity | gcc override valid, off PATH → `Probe().Gcc==true`; broken → false; none → pure PATH result unchanged |
| Wizard VM (existing tests) | availability follows probe | `FakeToolchainProbe` reporting gcc-via-override → backend enabled, hint cleared, c++23 offered |
| Settings dialog | new `FilePath` control kind + 6 items | **source-guard** test (wcq pattern): each key present in `BuildSearchableSettings` + `GetSettingsKeyForProperty`; `SettingsConsumerContractTests` stays green |
| Integration | ties §3 + §5 together | fake off-PATH g++ fixture: configure → wizard shows gcc available → build succeeds using the override path |

### 6.2 Guardrails (definition-of-done greps)

- 6 keys exist with `""` defaults + registered consumers.
- No silent-fallback path for an invalid override (grep the resolver / BuildService
  / DebugService for the error emission).
- `Program.cs` (CLI) passes no override params — CLI path unchanged.
- All new pure classes are `public`, so **no new `InternalsVisibleTo`** is needed
  (only the existing BasicLang→Tests).

### 6.3 File map

**NEW:** `ToolchainPathValidator.cs`, `CppToolchainOverrides.cs` (both
`VisualGameStudio.ProjectSystem`), plus the test files above.

**MODIFIED:** `CppToolchain.cs` (explicit-path factories), `CppProjectBuilder.cs`
(forward the resolver seams from `Build()`), `BuildService.cs` (override-aware
resolvers + pre-validate), `CppToolchainProbeService.cs` (OR overrides),
`DebugAdapterDescriptor.cs` (project arg on `ResolveLaunchCommand` + widened
`LldbDap` resolver), `DebugService.cs` (pass project + pre-validate),
`SettingsService.cs` (6 keys in the `cpp` group), `SettingsViewModel.cs` +
`SettingsDialog.axaml` (the `FilePath` control kind + 6 items), and
`ServiceConfiguration.cs` (registration lambda + inject the overrides reader).

---

## Out of scope (explicit)

- **clangd** (`cpp.clangd.path`) — untouched; stays a single global setting, not
  made per-backend.
- **CLI honoring overrides** — `BasicLang.exe` continues to auto-probe; overrides
  are an IDE-settings feature. (Possible follow-up: teach the CLI to read
  `~/.vgs/settings.json`.)
- **A first-class toolchain-profile object / linker & extra-flags fields**
  (Approach B) — declined; the flat six-key model is sufficient for the stated
  goal and can grow into a profile later without rework of the resolvers.
- **A Windows-native (cppvsdbg) debug adapter** — none exists today; the debugger
  override points at a DAP-speaking adapter regardless of backend.
- **The `"(configured)"` wizard hint** (§5.5) — optional polish, deferred.

## Standing constraints preserved

- `C:\winlibs\mingw64` **stays off PATH**; overrides are precisely the mechanism
  that lets the IDE use winlibs without PATH pollution.
- No PowerShell `Get-Content`/`Set-Content` round-trips on repo files.
- No new `InternalsVisibleTo` beyond the existing BasicLang→Tests.
- MSIL/LLVM backends remain out of scope.
