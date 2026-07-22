# Per-backend C++ compiler & debugger overrides in Settings

**Date:** 2026-07-21
**Status:** Approved design (user approved all six sections in session); revised
per two-lens review **r1** (codebase fact-check + canonical design review).
**Base:** master @ `ba2f8f7`
**Approach:** Approach A — an override-reader + a pure validator plus six flat
settings keys, wired through *existing* extension seams (the `CppProjectBuilder`
resolver seams, the F5 launch site, the wizard probe). No new persistence shape,
no profile object (Approach B explicitly declined), no extra debug-adapter
descriptors (Approach C explicitly declined).

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
| `cpp.toolchain.msvc.debugger` | path to a DAP adapter (**see §2/§4.4 — limited**) | lldb-dap locator chain |

> **r1 note on `cpp.toolchain.msvc.debugger`:** MSVC binaries carry PDB debug
> info, which `lldb-dap` (a DWARF reader) cannot consume — so this field, though
> retained (the user asked for all three), is honestly surfaced as *limited* by
> validation (§2, §4.4) rather than presented as fully functional.

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
toolchain. The reader that consumes it, `CppToolchainOverrides` (§3), is a
DI-registered service injected into `BuildService`, `MainWindowViewModel` (for the
F5 path, §4) and `CppToolchainProbeService` (§5) — one reader, three consumers.

- **Input:** `(backendId, kind = Compiler | Debugger, path)`.
- **Output:** `ValidationResult { Status, Message, DetectedVersion? }`, status ∈
  `Empty | Valid | Warning | Invalid`.

**The `Usable` predicate (r1).** Availability and authoritative-ness key off the
**existence gate**, not off `Status == Valid`. Define:

> `Usable(path)` ⇔ `Status ∈ { Valid, Warning }` — the existence gate passed.

`Warning` is a legitimately-usable state (a renamed/wrapper binary that *will*
build); only `Invalid` and `Empty` exclude a backend. Every consumer
(§3.2 `FromExplicit`, §5.1 probe OR) uses this same `Usable` predicate — never a
narrower `Status == Valid`. The reader exposes `HasUsableCompilerOverride(id)`
(not `…Valid…`) to make this unambiguous.

**Two-tier check per field:**

1. **Existence (the gate — fast, deterministic, runs synchronously, drives the
   immediate red/green):**
   - **llvm/gcc compiler & any debugger:** the path must name an existing file.
   - **MSVC compiler:** the path must **resolve to a `vcvars64.bat`** — either
     directly, or as a directory containing `VC\Auxiliary\Build\vcvars64.bat`. An
     existing path that is *neither* (e.g. someone points the slot at `cl.exe`)
     ⇒ **Invalid**, not Warning. (Without this, a `cl.exe` path would pass
     existence and get substituted as `_vcvarsPath` into `cmd /c "cl.exe" && cl …`
     and fail cryptically — the exact silent trap this design exists to prevent.)
   Missing / non-resolving ⇒ **Invalid**.

2. **Version smoke (enrichment — runs on Browse / commit / dialog-open, never per
   keystroke; see the threading contract below):** gated on filename shape to
   avoid executing an arbitrary binary.
   - **Recognized driver basename** (`clang++`/`clang`/`clang-cl`; `g++`/`c++`/`gcc`;
     `lldb-dap`) → run `<path> --version` with a short timeout (the technique
     `CppToolchain.IsOnPath` already uses, `CppToolchain.cs:109-128`), behind an
     **injected seam** so tests don't spawn processes. Success ⇒ **Valid** with the
     version string; failure/timeout ⇒ **Warning**.
   - **Unrecognized-but-existing basename** ⇒ **Warning** *without executing it*
     (wrapper-friendly, but never runs an unrecognized binary).
   - **MSVC compiler** (a resolved `vcvars64.bat`, no exe to smoke) ⇒ **Valid** —
     existence is the authoritative gate for MSVC.
   - **`msvc.debugger` slot** (r1): even a cleanly-smoking `lldb-dap` is capped at
     **Warning** carrying the advisory *"lldb-dap can't read MSVC PDB debug info —
     breakpoints may not bind."* The field stays usable (you may still launch), but
     is never shown as silently functional.

**When it fires & threading contract (r1):** the sync existence check runs on the
UI thread on file-picker return, text-field commit (blur/Enter), and dialog open,
driving the immediate red/green. The `--version` smoke runs **off the UI thread**
(a `Task`) with a **~3 s timeout** and **per-field cancellation** on re-edit and on
dialog close; its result is marshalled back to update `Status`/`DetectedVersion`.
The smoke never runs on every keystroke.

**"Authoritative but invalid" behavior:** because the Settings dialog live-applies
edits (`SettingsViewModel.AutoSaveSettingToService`), a red/`Invalid` value is
still *stored* (the developer does not lose their typing) — but it is flagged red
in the dialog, and at build/debug time an invalid-but-set path produces a **hard
error**, never a silent fall-back to auto-detect (§3.3, §4.1). That is the point
of the authoritative choice: a broken override announces itself instead of quietly
reverting.

**One definition, two call sites:** the same validator's existence check is what
the §3/§4 resolver calls at build/debug time.

---

## 3. Compiler wiring (build-time)

Guiding constraint: `CppProjectBuilder` and `CppToolchain` live in the compiler
(`BasicLang/`) and have **no settings access** — they are shared with the CLI. The
override is therefore *injected as data* by the settings-aware IDE layer; the
compiler stays settings-agnostic.

### 3.1 `CppToolchain` explicit-path factories

Today the ctor is private (`CppToolchain.cs:66`) and `_executable` holds a bare
name. Add:

- `FromExplicitCompiler(string id, string exePath)` for llvm/gcc → `_executable`
  becomes the **full path**, so the process launch bypasses PATH entirely (this is
  what makes winlibs-off-PATH work). `DriverName` (used for
  `compile_commands.json`) returns the same explicit path.
- **MSVC reuses the existing mechanism.** `CppToolchain.Compile` already runs
  `cmd /s /c ""<vcvarsPath>" >nul && cl …"` (`CppToolchain.cs:271-289`); the
  override just substitutes the user's resolved `vcvars64.bat` for the
  vswhere-discovered one via the existing private-ctor `_vcvarsPath` field. No new
  compile path.

### 3.2 `BuildService` owns the wiring

**r1 correction:** `BuildService` does **not** currently have `ISettingsService`
— its ctor takes only `IOutputService` + `ProjectSerializer`
(`BuildService.cs:38-47`). **Inject `ISettingsService`** (add a ctor param; the DI
registration at `ServiceConfiguration.cs:27` is type-based, so the container
supplies it) so it can construct/consult the `CppToolchainOverrides` reader.

`BuildService.BuildCppProject` (`BuildService.cs:1090-1102`) builds override-aware
resolvers and passes them through the seams `Build`/`EmitCore` expose
(`CppProjectBuilder.cs:67-73, 338-342`):

- **Pinned `<CppToolchain>`** — flows through the **existing** `resolveById`
  parameter (already declared *and already forwarded* to `EmitCore` at
  `CppProjectBuilder.cs:73`). `BuildService` supplies an override-aware
  `resolveById`: `Usable` override ⇒ `FromExplicit…`; blank ⇒ today's
  `CppToolchain.TryFindById`.
- **Unpinned (machine probe)** — today `Build` hardcodes `CppToolchain.Find`
  (`CppProjectBuilder.cs:73`). **`Build` grows exactly one new optional param,
  `resolveToolchain` (default null → `CppToolchain.Find`)** — `resolveById` and
  `probeAvailability` already exist. `BuildService` supplies an override-aware
  `resolveToolchain`.

**Unpinned selection precedence (r1 — was unspecified):** iterate backends in
today's fixed `Find` order **llvm → gcc → msvc**; a backend is a *candidate* if it
has a `Usable` compiler override **or** is on PATH/vswhere; pick the **first
candidate in that fixed order** (preserving today's clang-first preference).
Within the chosen backend, a `Usable` override wins over PATH. (So the tie-break is
by backend order, not a global override-beats-PATH rule.)

**The CLI (`Program.cs:440`) passes none of these params, so `BasicLang.exe`
behavior is unchanged** — overrides are an IDE-settings feature.

### 3.3 Invalid-but-set = hard error

`BuildService` pre-validates the **chosen** backend's override — the pinned one, or
the one the unpinned precedence selects (§3.2). If it is set but broken, the build
fails with a clear Output message pointing at Settings › C++ ("llvm compiler path
is set but not found: `…` — fix or clear it in Settings › C++"), never a silent
fall-back. A blank field still falls through to the probe; a pinned-but-absent
toolchain still yields the existing **BL6015** (`CppProjectBuilder.cs:347-351`).

### 3.4 Editing/building parity

**r1 correction:** `IntelliSenseEmitter.Emit` today forwards only
`resolveToolchain` (`() => toolchain`) and takes **no `resolveById`**
(`IntelliSenseEmitter.cs:40-45`) — so for a **pinned** backend (the primary
override case, e.g. winlibs `g++` off PATH pinned to `gcc`) `EmitCore` would resolve
by id via the default `TryFindById`, get null off PATH, and `compile_commands.json`
would name `clang++` — clangd drifting from the build. Fix: **add an override-aware
`resolveById` parameter to `IntelliSenseEmitter.Emit` and forward it to `EmitCore`**,
supplied by the same IDE layer that drives IntelliSense regen, so clangd sees the
overridden compiler the build uses. `IntelliSenseEmitter.cs` is therefore a modified
file (§6.3).

---

## 4. Debugger wiring (F5)

**r1 correction (material):** the earlier claim that `DebugService` holds the
project and routes via `GetFor` was **wrong**. `registry.GetFor(project)` runs in
`MainWindowViewModel.OnDebugAsync` (`MainWindowViewModel.cs:3582`), where
`CurrentProject` is in scope; MWVM already calls `descriptor.ResolveLaunchCommand()`
there (`:3591`) for its installed-check and builds a `DebugConfiguration` carrying
only `AdapterId` (`:3664-3674`). `DebugService` receives that config, resolves the
adapter via `registry.GetById(adapterId)`, and holds **neither the project nor
`ISettingsService`**. So the per-backend debugger override is resolved **at the F5
site (MWVM)**, where the project — and thus its pinned backend — is already in hand.
**No descriptor-signature change; `DebugService` stays project- and
settings-agnostic.**

### 4.1 Resolve + validate at the F5 site

In `OnDebugAsync`, for a **native** project (`project.IsNativeBuild`; the override
never applies to the managed BasicLang adapter), MWVM resolves the per-backend
debugger override through the same `CppToolchainOverrides` reader used for the
compiler: `overrides.ResolveDebugger(project.CppToolchain)` → tri-state. Fold it
into the single "effective adapter path" computation MWVM already does for its
installed-check (`:3591`):

- **Invalid** (set but the file is gone) → abort F5 with the clear error ("gcc
  debugger path is set but not found: `…` — fix or clear it in Settings › C++"),
  never a silent fall-through.
- **Usable(path)** → the effective adapter path is the override.
- **None/blank** → the effective adapter path is `descriptor.ResolveLaunchCommand()`
  (today's `LldbDapLocator` chain, `cpp.lldbDap.path` → `~/.vgs/tools` → PATH →
  known dirs incl. winlibs).

If the effective path is null → the existing `ReportDebugAdapterMissing` flow
(download-offer toast), unchanged.

### 4.2 Thread the chosen path via `DebugConfiguration`

`DebugConfiguration` gains **one optional field**, `AdapterExecutableOverride`
(default null). MWVM sets it to the override path when `Usable`, leaves it null
otherwise. `DebugService`, when it resolves the launch for `config.AdapterId`, uses
`config.AdapterExecutableOverride` when present, else `descriptor.ResolveLaunchCommand()`
exactly as today. **That is the whole `DebugService` change** — honor one optional
field. MWVM owns the settings-aware resolution and the invalid-abort.

### 4.3 Scope + the unpinned case

The per-backend debugger override applies when the project **pins** a backend
(`<CppToolchain>` set — which the wizard always writes for these projects). An
unpinned project falls to the default chain. This is benign even though §3.2 makes
the unpinned *compiler* override-aware: the default debugger chain is
backend-agnostic `lldb-dap` regardless of which compiler produced the binary, so the
debugger does not depend on the unpinned compiler selection.

### 4.4 `msvc.debugger` honesty

(Cross-ref §2.) A `Usable` `lldb-dap` in the `msvc.debugger` slot is surfaced at
**Warning** with the advisory "lldb-dap can't read MSVC PDB — breakpoints may not
bind" (project history: lldb even mis-binds on `g++` DWARF5; MSVC PDB is worse).
The field is **retained** (the user asked for all three) but never presented as
silently functional.

**Caveat (for the record):** the override must point at a **DAP-speaking adapter**
(realistically an `lldb-dap` build — winlibs and LLVM each ship one). Validation
checks existence + filename shape; it cannot guarantee an arbitrary exe speaks DAP.

---

## 5. Wizard / probe (availability)

Because everything routes through the probe, the wizard itself needs **almost no
change**.

### 5.1 Make the probe override-aware — at the ProjectSystem boundary

`CppToolchainProbeService` (`CppToolchainProbeService.cs:10-16`; it lives in
ProjectSystem and can take `ISettingsService`/the overrides reader; today it merely
wraps the pure compiler-side probe) gains the `CppToolchainOverrides` reader. Its
`Probe()` returns, per backend:

> `available = onPath/vswhere  OR  HasUsableCompilerOverride(backend)`

(using the §2 `Usable` predicate — `Valid` *or* `Warning`, so a working wrapper
counts). So gcc pointed at winlibs off PATH arrives as `Gcc = true`. The
compiler-side `CppToolchain.ProbeAvailability()` (`CppToolchain.cs:95-96`) stays
pure PATH/vswhere (shared with the CLI, no settings) — the OR happens only in the
ProjectSystem adapter.

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

The probe ORs in `HasUsableCompilerOverride` (existence passed), so a set-but-broken
(`Invalid`) override leaves the backend greyed in the wizard while the Settings page
shows the red error — consistent, no "available but won't build."

### 5.5 Wizard/build agreement

Because §3's override-aware resolver uses the **same `Usable` predicate** as the
probe, a backend shown available in the wizard resolves to a real toolchain at build
time (never trips BL6015 for it). "Shows available" and "actually builds" cannot
disagree — including for a `Warning`-status wrapper.

**Optional polish left OUT of v1** (keeps the probe's record a simple bool triple):
a distinct `"(configured)"` hint to signal *why* a backend is available. Enabled is
enabled for now.

---

## 6. Testing strategy & component map

Everything testable-in-isolation is pulled into pure/injectable units so nothing
depends on a DI-constructed dialog/MWVM or a real toolchain. Standing lessons
honored: MWVM/dialogs are never constructed in tests (use pure classes +
source-guards); **winlibs stays off PATH** so fixtures use *fake existing files*,
not a real install; validate codegen through the CLI/optimizer
(`CompileToCppOptimized`), not only the non-optimizing unit helper; exercise
**both** entry points (CLI and IDE).

### 6.1 Units & tests

| Unit (new unless noted) | Purpose | Key tests |
|---|---|---|
| `ToolchainPathValidator` (pure) | `(backend, kind, path)` → `Empty/Valid/Warning/Invalid` + `Usable` | empty→Empty; missing→Invalid; clang++→Valid(version) via **fake** version-probe seam; unrecognized-but-existing basename→Warning **without executing**; **msvc compiler: dir→derives vcvars=Valid, `cl.exe`→Invalid**; **msvc.debugger valid lldb-dap→Warning + PDB advisory**; `Usable` = Valid∪Warning |
| `CppToolchainOverrides` (reader, DI) | reads 6 keys, applies validator → tri-state + `HasUsableCompilerOverride` | fake `ISettingsService`: blank/usable/invalid per backend; consumer registration asserted |
| `CppToolchain` factories (BasicLang) | `FromExplicitCompiler` / msvc-vcvars | emitted **build command invokes the full override path** (assert via `CompileToCppOptimized` + CLI); msvc substitutes resolved vcvars |
| `BuildService` wiring (+`ISettingsService`) | override-aware `resolveById` + new `resolveToolchain`; pre-validate | pinned-gcc + usable override, nothing on PATH → builds with override; **unpinned: gcc override usable + off PATH → builds gcc; tie-break llvm>gcc>msvc order**; set-but-invalid → **hard error, no silent fallback**; **`Build()` with no override params = today's behavior (CLI unchanged)** |
| `IntelliSenseEmitter` (+`resolveById`) | compile_commands parity | **pinned winlibs-gcc override → `compile_commands.json` names the override g++**, not clang++ |
| `DebugConfiguration` + `DebugService` | honor `AdapterExecutableOverride` | config with override path → launch uses it; without → uses `descriptor.ResolveLaunchCommand()` (no regression) |
| `CppToolchainOverrides.ResolveDebugger` + MWVM F5 (source-guard) | per-backend debugger resolution | pure: blank→chain, usable→path, invalid→error tri-state; **source-guard**: `OnDebugAsync` resolves the debugger override + aborts on invalid (MWVM not constructed) |
| `CppToolchainProbeService` | OR override validity | gcc override usable, off PATH → `Probe().Gcc==true`; invalid → false; none → pure PATH result unchanged |
| Wizard VM (existing tests) | availability follows probe | `FakeToolchainProbe` reporting gcc-via-override → backend enabled, hint cleared, c++23 offered |
| Settings dialog | new `FilePath` control kind + 6 items | **source-guard** test (wcq pattern): each key present in `BuildSearchableSettings` + `GetSettingsKeyForProperty`; `SettingsConsumerContractTests` stays green |
| Integration | ties §3 + §5 together | fake off-PATH g++ fixture: configure → wizard shows gcc available → build succeeds using the override path |

### 6.2 Guardrails (definition-of-done greps)

- 6 keys exist with `""` defaults + registered consumers.
- No silent-fallback path for an invalid override (grep the resolver / BuildService
  / the MWVM F5 site for the error emission).
- `Program.cs` (CLI) passes no override params — CLI path unchanged.
- All new pure classes are `public`, so **no new `InternalsVisibleTo`** is needed
  (only the existing BasicLang→Tests).

### 6.3 File map

**NEW:** `ToolchainPathValidator.cs`, `CppToolchainOverrides.cs` (both
`VisualGameStudio.ProjectSystem`), plus the test files above.

**MODIFIED:**

- `CppToolchain.cs` — explicit-path factories.
- `CppProjectBuilder.cs` — add the one `resolveToolchain` forwarding param to
  `Build()` (`resolveById`/`probeAvailability` already exist).
- `IntelliSenseEmitter.cs` — add + forward an override-aware `resolveById` (§3.4).
- `BuildService.cs` — **inject `ISettingsService`**; build override-aware resolvers
  + pre-validate.
- `CppToolchainProbeService.cs` — OR in `HasUsableCompilerOverride`.
- `DebugConfiguration` model — new optional `AdapterExecutableOverride` field.
- `DebugService.cs` — honor `AdapterExecutableOverride` when present.
- `MainWindowViewModel.cs` — resolve + validate the per-backend debugger override at
  the F5 site (`OnDebugAsync`), abort on invalid (source-guarded).
- `SettingsService.cs` — 6 keys in the `cpp` group.
- `SettingsViewModel.cs` + `SettingsDialog.axaml` — the `FilePath` control kind + 6
  items.
- `ServiceConfiguration.cs` — register `CppToolchainOverrides`; inject it (or
  `ISettingsService`) into `BuildService`, `CppToolchainProbeService`, and
  `MainWindowViewModel`.

(The earlier plan to widen `DebugAdapterDescriptor.ResolveLaunchCommand(project)` is
dropped in r1 — the F5-site resolution replaces it.)

---

## Out of scope (explicit)

- **clangd** (`cpp.clangd.path`) — untouched; stays a single global setting, not
  made per-backend.
- **CLI honoring overrides** — `BasicLang.exe` continues to auto-probe; overrides
  are an IDE-settings feature. (Possible follow-up: teach the CLI to read
  `~/.vgs/settings.json`.)
- **A first-class toolchain-profile object / linker & extra-flags fields**
  (Approach B) — declined; the flat six-key model is sufficient and can grow into a
  profile later without rework of the resolvers.
- **A Windows-native (cppvsdbg) PDB-capable debug adapter** — none exists today;
  `msvc.debugger` is retained but honestly flagged as limited (§4.4) until such an
  adapter ships.
- **The `"(configured)"` wizard hint** (§5.5) — optional polish, deferred.

## Standing constraints preserved

- `C:\winlibs\mingw64` **stays off PATH**; overrides are precisely the mechanism
  that lets the IDE use winlibs without PATH pollution.
- No PowerShell `Get-Content`/`Set-Content` round-trips on repo files.
- No new `InternalsVisibleTo` beyond the existing BasicLang→Tests.
- MSIL/LLVM backends remain out of scope.
