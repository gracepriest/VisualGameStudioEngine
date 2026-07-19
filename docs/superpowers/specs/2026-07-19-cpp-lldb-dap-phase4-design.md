# C++ Phase 4: Native Debugging via lldb-dap — Design

**Date:** 2026-07-19
**Status:** Approved design, pre-planning
**Parent spec:** `2026-07-11-cpp-language-support-design.md` (Phase 4 line: DAP registry; lldb-dap; native launch configs; stretch: `#line` → `.bas`-source debugging)
**Prior art this design mirrors:** Phase 3a's LSP registry (`LanguageServerDescriptor` / `LanguageServiceRegistry`) and Phase 3b's tool acquisition (`ClangdInstaller` / locator chain).

## 1. Goal

Pressing F5 on a native (C++-backend or mixed) project launches the compiled
executable under a debugger: breakpoints bind, execution stops, stepping works,
locals and watch evaluate, and thrown C++ exceptions can break. Today that F5
hits a guard ("Native C++ debugging arrives in a later phase") at
`MainWindowViewModel.cs:3477-3488`. Managed BasicLang debugging (the existing
`BasicLang.exe --debug-adapter` flow) must keep working unchanged.

## 2. Decisions (user-approved)

1. **Scope:** C++-file breakpoints are the core deliverable. `.bas`-source
   debugging via `#line` runs as a **measured Step-0 gate** (§7): in scope if
   the gate passes, parked with evidence if it fails.
2. **Acquisition:** one-click download of a **self-hosted, SHA-256-pinned zip**
   of lldb-dap (built `LLDB_ENABLE_PYTHON=OFF`) from the VGS GitHub releases
   page — the exact `ClangdInstaller` pattern. Rationale: the official LLVM
   Windows installer is 434 MB and broken on clean machines (its `liblldb`
   links a python DLL that is not shipped — LLVM issues #85764/#58095/#74073).
3. **Debugger architecture:** a **VS Code-style pluggable debugger registry**.
   Debug adapters are described declaratively and registered through a public
   door; built-ins use the same door an extension would. Adapter choice is
   routed by project type and paired with the selected C++ toolchain.
4. **Debug-format stance:** MSVC/PDB is **first-class** — it is what the
   toolchain probe finds on the primary dev machine and what the engine `.lib`
   is built with. clang(-msvc)/PDB and g++(mingw)/DWARF are also served. All
   three routes are served by **one adapter engine, lldb-dap**, whose native
   PDB reader has been the LLDB default since 2025. We cannot ship Microsoft's
   vsdbg (proprietary; licensed for VS/VS Code only) — "MSVC debugging" means
   lldb-dap reading MSVC-produced PDBs.
5. **Exceptions:** the Exception Settings dialog becomes **adapter-driven**:
   the client stops discarding the DAP `initialize` response and renders
   whatever `exceptionBreakpointFilters` the active adapter advertises
   (`cpp_throw`/`cpp_catch` for lldb-dap; the managed adapter's
   `all`/`uncaught`). Any
   future adapter gets correct exception UI for free.

## 3. Architecture

Three layers, mirroring Phase 3a's LSP design. Approach chosen over
alternatives (parallel client; parameterized spawn) because it is the only one
where the registry is real and the protocol fixes benefit both adapters.

### 3.1 `DebugAdapterDescriptor` (Core)

A declarative record describing one adapter — the extension point. Fields:

- `Id` (`"basiclang-managed"`, `"lldb-dap"`), `DisplayName`.
- A launch-command factory: adapter executable + arguments, resolved through a
  locator at session start (not at registration — the adapter may be installed
  mid-session).
- A routing predicate: which projects this adapter serves (managed vs native
  build, via `BasicLangProject.IsNativeBuild`, `Core/Models/BasicLangProject.cs:14`).
- Toolchain pairing metadata (which C++ toolchains this adapter debugs; in v1
  lldb-dap claims MSVC, clang, and g++ output).
- A timeout profile (§8): per-adapter launch/request/step/disconnect budgets.
- Fallback exception filters, used only if the adapter's `initialize` response
  advertises none.

Mirrors `LanguageServerDescriptor` (Core, ~:40) in shape and placement.

### 3.2 `DebugAdapterRegistry` (ProjectSystem)

Mirrors `LanguageServiceRegistry` (`LanguageServiceRegistry.cs:108-147`):
lookup by project, built-ins registered at DI composition
(`ServiceConfiguration.cs` conditional-composition pattern, ~:46-67) through
the same public registration method an extension would call. Two built-ins in
v1: the managed BasicLang adapter and lldb-dap.

### 3.3 `DapSession` (ProjectSystem)

The transport and protocol core **extracted** from `DebugService.cs`
(1,347 lines today): process spawn, stdio framing, request/response
correlation, event dispatch. The proven BOM/framing machinery is preserved
byte-for-byte (UTF-8 no-BOM stdin/stdout at :115-120; the Latin1 reader
re-decoding UTF-8 at :137-141 and :1089-1124) — this is exactly the code the
repo's two dead DAP stacks lack.

Three mandatory correctness fixes land here, once, for both adapters:

1. **Spec-correct handshake**, with the `launch` request's position
   explicit. Arm the `initialized`-event listener **before anything is
   sent** — DAP permits a conforming adapter to emit `initialized` any time
   after the initialize response, and the repo's legacy `--dap-legacy`
   adapter does exactly that (~50 ms after, before any launch); the two v1
   adapters emit it while processing launch/attach. Then: `initialize`
   request → `initialize` response (capabilities retained) → send `launch`
   **without awaiting its response** → await `initialized` →
   `setBreakpoints`/`setExceptionBreakpoints` → `configurationDone` → await
   the `launch` response. Launch-response completion timing is
   **adapter-dependent** — the managed adapter completes it before
   configuration, lldb-dap defers it until after `configurationDone` — so
   the session awaits it only after `configurationDone` and must accept it
   having arrived earlier; neither timing is a protocol error. Today's
   client ignores the `initialized` event (event switch :1158-1204) and
   awaits the `launch` response before sending `configurationDone`
   (:160→:182); the managed adapter tolerates this, lldb-dap stalls on it.
   The opposite error is just as fatal against lldb-dap: waiting for
   `initialized` before sending `launch` deadlocks, because lldb-dap emits
   it only during launch processing.
2. **Real threadIds:** the threadId delivered in `stopped` events is threaded
   through continue/step/pause/goto and every stack fetch. Today `1` is
   hardcoded five times in `DebugService.cs` (:492/:540/:556/:588, plus :668
   in the goto/Set Next Statement request) and baked into the default
   parameter `GetStackTraceAsync(int threadId = 1)` (`DebugService.cs:803`,
   `IDebugService.cs:134`), which the UI stack fetches silently rely on
   (`MainWindowViewModel.cs:3188`, `CallStackViewModel.cs:72`,
   `VariablesViewModel.cs:89`). The plumbing task must grep for both
   patterns. The e2e harness already models the correct behavior —
   `IdeInAngerTests.cs:475` threads `stopped.ThreadId` into its stack fetch —
   so the native e2e asserts propagation the same way.
3. **Capabilities retained:** the `initialize` response is kept on the session
   (today discarded at :146-157). This feeds the adapter-driven Exception
   dialog (§2.5) and lets the session skip requests the adapter doesn't
   support.

### 3.4 `DebugService` becomes an orchestrator

Pick descriptor from registry → ensure adapter installed → spawn `DapSession`
→ expose the **same `IDebugService` surface**. Debug viewmodels (call stack,
variables, breakpoints, watch) keep their single dependency and never learn
how many adapters exist. The threadId plumbing (§3.3.2) is the only viewmodel
change.

### 3.5 Deletions

The two dead DAP stacks are **deleted in this phase** — five files:
`DAP/DapClient.cs` (+ `DapClientManager`), `Services/DapClientService.cs`,
the orphan interface `Core/Abstractions/Services/IDapClientService.cs`, and
`Tests/Services/DapClientServiceTests.cs` (which does construct the dead
service — "never constructed" is true of product code only). Neither stack
is DI-registered, and each carries a transport trap the live code already
fixed: `DapClient` reads frames chars-not-bytes on a default-encoding reader
(:259-263); `DapClientService` frames by bytes but leaves `StandardInput`
encoding unpinned (:105), so a BOM preamble can still be injected. The 3a
`LspClientManager` verdict repeats. One historical note: the dead
`DapClient.cs:388-394` had the handshake ordering right; use it as a
reference while writing §3.3.1, then delete it with the rest.

## 4. F5 data flow

1. **Route.** Registry picks the descriptor by project. Managed projects take
   today's path unchanged.
2. **Ensure adapter.** Locator chain (§5) resolves lldb-dap. Missing → the
   3b-style offer toast → download → F5 resumes; no IDE restart needed.
3. **Build.** Existing `BuildService.BuildCppProject` already returns
   `result.ExecutablePath` (:1030-1083). Guard: if the active configuration
   emits no debug info (Release — `/Zi`/`-g` are Debug-only in
   `CppToolchain.FlagsFor`, `CppToolchain.cs:118-140`), warn in Output that
   breakpoints will not bind and suggest the Debug configuration.
4. **Launch.** Handshake per §3.3.1 — the `launch` request goes out right
   after the `initialize` response and stays in flight while breakpoints are
   pushed from the existing path+line-keyed store (no extension gate — `.cpp`
   gutter breakpoints including conditional/hit-count/logpoints already
   round-trip, they just never reached an adapter). Launch arguments:
   `program = ExecutablePath`, `cwd` = **project directory**, matching
   Ctrl+F5 run semantics so a game finds its assets identically under debug
   and run. The `.vgs/launch.json` overlay (gathered at
   `MainWindowViewModel.cs:3531-3574`) keeps working for overrides.
5. **Session.** stopped/step/locals/watch/exception flow through the fixed
   core. Disconnect sends `terminateDebuggee: true` for launched sessions and
   kills the process tree as backstop (never on attach — but native attach is
   out of v1, §9).

## 5. Acquisition: `LldbDapInstaller` + locator

Structural sibling of `ClangdInstaller`:

- Pinned URL + pinned SHA-256; download with progress toast; extract to
  `~/.vgs/tools/lldb-dap_<version>/`.
- Entry points: **Tools → Download C++ Debugger** and the offer toast on first
  F5 of a native project (the `ClangdDownloadFlow` pattern).
- Locator chain, checked in order: explicit setting (`cpp.lldbDap.path`) →
  `~/.vgs/tools` → PATH → known LLVM install dirs (reuses 3b's
  `ExecutableLocator`). A user with their own working lldb-dap is served
  without downloading.

**The zip is a one-time build-pipeline deliverable** (its own plan task):
lldb-dap 22.x built with `LLDB_ENABLE_PYTHON=OFF` (kills the broken-python
trap), containing `lldb-dap.exe`, `liblldb.dll`, `lldb-argdumper.exe`.
Published on VGS GitHub releases; estimated 40–80 MB. Apache-2.0 +
LLVM-exception — redistribution-clean. Acceptance: on a fresh Windows VM with
no LLVM installed, the zip alone debugs a real executable.

## 6. Toolchain pairing

| Binary built by | Debug format | lldb-dap path | v1 status |
|---|---|---|---|
| MSVC (`cl /Zi`) | CodeView/PDB | native PDB reader (default since 2025) | **first-class, live-tested** |
| clang++ (msvc triple, `-g` via lld-link) | CodeView/PDB | same | supported; unit/pairing-tested |
| g++ / clang++ (mingw, `-g`) | DWARF | lldb's native format | supported; unit/pairing-tested |
| any, Release config | none | — | warned, breakpoints hollow |

Known PDB caveats (documented, accepted for v1): expression evaluation is
weaker than DWARF; occasional adapter crashes on complex evaluations — which
is why session-death handling (§8) is a first-class path, not an edge case.
gdb is a dead end (cannot read PDB; DAP mode python-gated) and vsdbg is
license-prohibited; neither is a v1 built-in. The registry keeps the door
open for either as a future plug-in where licensing allows.

## 7. Step-0 gate: `#line` → `.bas`-source debugging

**The port.** The IR already carries source mapping end-to-end:
`IRInstruction.SourceLine` (`IRNodes.cs:12-21`, set by `IRBuilder` :129-147)
and `IRFunction.SourceFilePath` (:1077) survive the optimizer and module
combiner into codegen, where they die by omission. The port teaches
`CppCodeGenerator` to emit `#line <n> "<absolute .bas path>"` before each
statement — template: `CSharpBackend.EmitLineDirective` (:1748-1790,
:1840-1842). Both MSVC `/Zi` and clang `-g` honor `#line` in their line
tables, so the debug info maps addresses to `.bas` lines and lldb-dap binds
`.bas` breakpoints like any source file.

**Adaptations:**
- C++ has no `#line hidden`: optimizer-synthesized instructions
  (`SourceLine=0`) are re-pointed at the generated `.g.cpp` itself.
- **The `#line` filename is a C/C++ string literal** — escape sequences are
  processed, so a raw Windows path breaks the compile (`C:\Users\…` contains
  `\U`, a malformed universal-character-name). Emit forward slashes (or
  doubled backslashes). The C# `#line` directive takes its filename
  literally, so the template does no escaping — the port must add it.
- **Debug configuration only.** Release output stays byte-identical to today.

**Gate mechanics.** Step 0 of the implementation plan, timeboxed. Build a real
mixed project (Debug), drive lldb-dap through the e2e harness, assert:

1. a breakpoint at a `.bas` line **binds** (verified, correct line);
2. execution **stops** there;
3. **step-next** lands on the next `.bas` statement, not in generated glue.

**Pass** → `.bas` debugging is in v1 (free ride: the breakpoint store is
path-keyed, so `.bas` and `.cpp` breakpoints flow to the same session; `.bas`
breakpoints are the stabler kind since `obj/gen` is cleaned and rewritten
every build, shifting generated-file lines). **Fail** → v1 ships C++-file
breakpoints only; findings written up; follow-up chip filed.

**Out of gate scope:** locals fidelity. BasicLang locals compile to generated
C++ names; the Variables pane shows those names in v1 either way.

## 8. Error handling

- **Session death is an event, not a hang.** Adapter process exit at any point
  → the terminated path: UI returns to edit mode, diagnostic in Output.
- **`evaluate` failures are per-request** — a failed watch/hover errors that
  entry only, never the session.
- **Timeout profile on the descriptor:** lldb-dap gets a 60 s launch budget
  (native symbol load); steps stay tight (5 s); requests 30 s; disconnect
  grace then tree-kill. Today's flat timeouts live at the client level and
  move to the descriptor.
- **Hollow breakpoints:** the existing verified/unverified round-trip
  (`BreakpointsViewModel.cs:61-123`) is exactly lldb's late-binding model.
- **No-debug-info warning** on Release builds (§4.3).
- **UI backstops:** debug-session start activating panes goes through the
  guarded activation path (the `EnsureMainArea` lesson) — layout surprises
  degrade to a logged no-op, never a crash.

## 9. Non-goals (v1)

- Native **attach** (launch only; attach stays managed-only; follow-up chip).
- Renaming generated locals back to BasicLang names in the Variables pane.
- gdb or vsdbg built-ins (§6). Multi-session / multi-target debugging.
- MSIL/LLVM backends (out of scope per standing project decision).
- `terminate`/`restart`/`setVariable`/`source`/`modules` DAP requests (client
  doesn't send them today; unchanged).

## 10. Testing

Three rings, all gating commits via the full suite as usual:

1. **Unit — session core vs a scripted fake adapter** (in-proc DAP stub over
   pipes): handshake ordering under **both timing regimes** — an
   lldb-dap-shaped stub that emits `initialized` only after receiving
   `launch` and defers the launch response past `configurationDone` (today's
   client deadlocks on it), and a managed-shaped stub that emits
   `initialized` immediately after the initialize response and completes
   launch before configuration — plus capabilities retention, threadId
   propagation, and the framing edge cases (UTF-8/BOM — load-bearing per
   the 3a lesson).
   Registry routing, descriptor lookup, and installer behavior (SHA mismatch,
   partial download, extract) get plain unit tests mirroring 3b's.
2. **Regression — the managed-adapter e2e stays green through the refactor.**
   This is the insurance for extracting `DapSession` out of `DebugService`.
3. **Native e2e — the real thing** (`IdeInAngerTests.cs:428-526` template:
   temp project, real compile, real adapter, TCS-per-event, 90 s budgets,
   `AssertProcessExits` kill-tree safety): breakpoint binds → stops → steps →
   locals present → watch evaluates → `cpp_throw` breaks on a thrown
   exception → disconnect leaves zero orphan processes. The Step-0 gate
   (§7) reuses this harness.

**Machine caveat (honest):** the primary dev machine has MSVC only, so live
e2e coverage is the MSVC/PDB path — the first-class path by decision. clang
and g++ routes carry descriptor-pairing and unit coverage and are marked
untested-live until a machine has those toolchains.

## 11. Risks

| Risk | Mitigation |
|---|---|
| lldb-dap stalls/quirks differ from managed adapter | spec-correct handshake (§3.3.1); descriptor timeout profile; session-death path (§8) |
| Managed debugging regression during extraction | regression e2e ring (§10.2) gates every commit |
| `#line` gate fails on PDB line tables | it's a gate — measured first, parked with evidence on failure |
| Self-host zip build effort / hosting | one-time pipeline task with fresh-VM acceptance (§5); locator chain serves user-provided binaries meanwhile |
| PDB expression-eval crashes the adapter | per-request evaluate isolation + session-death path (§8) |
