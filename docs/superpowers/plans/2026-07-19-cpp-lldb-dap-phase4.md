# C++ Phase 4: Native Debugging via lldb-dap — Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** F5 on a native (C++-backend or mixed) project launches the compiled exe under
lldb-dap — breakpoints bind, stepping works, locals/watch evaluate, `cpp_throw` breaks —
through a VS Code-style pluggable debug-adapter registry, while managed BasicLang debugging
keeps working unchanged; a measured Step-0 gate decides whether `.bas`-source debugging via
`#line` ships in v1.

**Architecture:** Three layers mirroring Phase 3a's LSP design: `DebugAdapterDescriptor`
(Core, pure identity + routing predicate + timeout profile), `DebugAdapterRegistry`
(ProjectSystem, public registration door, two built-ins), and `DapSession` (ProjectSystem,
the transport/protocol core extracted from the 1,347-line `DebugService`, which becomes an
orchestrator behind the unchanged `IDebugService` surface). Acquisition mirrors Phase 3b:
`LldbDapLocator` chain + `LldbDapInstaller` with pinned URL/SHA (placeholders until the
self-hosted zip ships per the runbook task).

**Tech Stack:** C# / .NET 8, NUnit (baseline 3250 tests), Avalonia Shell, MSVC + winlibs
lldb-dap 19.1.7 (`C:\winlibs\mingw64\bin`, OFF PATH) for conditional e2e; shipped adapter
pins lldb-dap 22.x (runbook).

**Spec:** `docs/superpowers/specs/2026-07-19-cpp-lldb-dap-phase4-design.md` (approved).
Rationale lives THERE — this plan is tasks. Where the spec's line anchors and the live code
disagree, the live code wins (all anchors below re-verified 2026-07-19 against master
`a551700`).

---

## Ground truth (verified 2026-07-19 — cite these, do not re-derive)

**The F5 native guard (the seam this phase replaces):**
`VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs:3477-3488` — returns with
"Native C++ debugging arrives in a later phase…" when `CurrentProject.IsNativeBuild`
(`VisualGameStudio.Core/Models/BasicLangProject.cs:14`,
`Language == ProjectLanguage.Cpp || TargetBackend == TargetBackend.Cpp`).

**threadId=1 hardcodes (complete inventory, all verified):**
`DebugService.cs:492` (continue), `:540` (step), `:556` (pause), `:588` (run-to-cursor
continue), `:668` (goto), `:803` `GetStackTraceAsync(int threadId = 1)` default;
`IDebugService.cs:134` same default. Callers riding the default: `MainWindowViewModel.cs:3188`,
`CallStackViewModel.cs:72`, `VariablesViewModel.cs:89` and `:216`,
`DebugConsoleViewModel.cs:396`. Correct behavior already modeled at
`IdeInAngerTests.cs:475` (`GetStackTraceAsync(stopped.ThreadId)`) and
`MainWindowViewModel.cs:3417` / `ThreadsViewModel.cs:45,79,91`.

**The proven transport (preserve byte-for-byte, comments included):** `DebugService.cs`
:105-143 (BOM-less `UTF8Encoding(false)` stdin/stdout + the BOM comment), :1066-1087
(reader loop), :1089-1124 (Content-Length framing + Latin1→UTF-8 re-decode + the byte-count
comment), :1126-1206 (response correlation + event switch), :1258-1287 (`SendRequestAsync`,
`RunContinuationsAsynchronously` + internal 30 s timeout), :1289-1318 (`SendMessageAsync`
under `_writeLock`), :432-486 (`CleanupProcesses`, `Kill(entireProcessTree:true)`).

**The handshake bug:** `DebugService.cs:146-186` sends initialize → **awaits launch
response** → setBreakpoints → configurationDone, and **ignores the `initialized` event**
(the event switch :1152-1206 has no case for it). The managed adapter tolerates this;
lldb-dap deadlocks (it defers the launch response past configurationDone). The opposite
error — awaiting `initialized` before sending launch — also deadlocks lldb-dap (it emits
`initialized` only while processing launch). The legacy `--dap-legacy` adapter emits
`initialized` ~50 ms after initialize without waiting for launch → the listener must be
armed **before anything is sent**. The dead `DapClient.cs:388-394` has the correct
ordering — reference it while writing Task 4, then delete it in Task 11.

**Dead DAP stacks (never DI-registered; each has a transport trap the live code fixed):**
`VisualGameStudio.ProjectSystem/DAP/DapClient.cs` (chars-not-bytes framing :259-263),
`VisualGameStudio.ProjectSystem/DAP/DapClientManager.cs`,
`VisualGameStudio.ProjectSystem/Services/DapClientService.cs` (stdin encoding unpinned :105),
`VisualGameStudio.Core/Abstractions/Services/IDapClientService.cs`,
`VisualGameStudio.Tests/Services/DapClientServiceTests.cs`. ⚠ `VisualGameStudio.Core/DAP/IDapClient.cs`
is NOT on the deletion list (spec §3.5 names five files) and its POCOs are referenced by
`Tests/Infrastructure/InfrastructureTests.cs:15-17` — it stays.

**#line port facts:** template `CSharpBackend.EmitLineDirective` :1772-1783 +
`EmitLineHidden` :1785-1790, dedupe fields :59-60, per-instruction call :1839-1845,
per-function dedupe reset :1331-1332. IR carriers: `IRInstruction.SourceLine`
(`IRNodes.cs:12-30`, 0 = unknown), `IRFunction.SourceFilePath` (:1074-1077), both survive
the optimizer and `CombineIRModules` (same instances, no copying). C++ side:
`CppCodeGenerator.cs` — options class :3472-3478, ctor :53-56, `_output` lambda-capture
swap :658/:680 (save/restore), instruction loops `GenerateBlock` :1660-1664,
`EmitBlockInstructions` :3023-3031, `EmitRegionInstructions` :3103-3117 (terminators are
INSIDE these loops — goto model, no separate terminator hook needed);
`CppCodeGenerator.Split.cs` `CaptureSection` :172-188 (per-file `_output` swap).
⚠ The read loop (:1066-1087) does NOT exit on EOF — `ReadMessageAsync` returns null and
the loop spins; a crashed adapter (which never sends the `terminated` event) today means
a silent hang, not a state change. Task 2 deliberately changes this (EOF ⇒ exit + `Closed`).
Build plumbing: `CppProjectBuilder.cs:255` (`new CppCodeGenerator().GenerateSplit(...)`,
`configuration` in scope), :343-349 (`DebugSymbols = true` default overridden by
`config.DebugSymbols` — Debug=true / Release=false per `ProjectFile.cs:87-95`).
⚠ **C++ `#line` filenames are escape-processed string literals** (C#'s are taken
verbatim): `C:\Users\…` contains `\U`, a malformed universal-character-name → compile
error. Emit forward slashes.

**lldb-dap machine reality:** `C:\winlibs\mingw64\bin\lldb-dap.exe` exists (LLDB 19.1.7,
self-contained, OFF PATH deliberately — putting winlibs on PATH would flip
`CppToolchain.Find()` from MSVC to mingw clang++). ⚠ **`lldb-dap --version` HANGS parked
on stdin — never probe it that way.** Existence checks only, everywhere. 19.1.7 is the dev
debugger; the shipped zip pins 22.x — any MSVC/PDB quirk seen on 19 is checked against 22
before being treated as our bug.

**Patterns to mirror (verbatim inventories in the recon dossier):**
`LanguageServerDescriptor` (Core :40, private ctor + static factories, settings-key
consts :48-61), `LanguageServiceRegistry` (`:76` ctor throws on empty/duplicate),
`ServiceConfiguration.cs:46-67` (conditional roster) and `:98`
(`AddSingleton<IDebugService, DebugService>()`), `ClangdLocator` (chain :113-123,
`BuildWindowsLlvmInstallDirectories` :222), `ClangdInstaller` (pins :56-77, closed
FailureStep vocabulary :83-104, seam ctor :126-130, staged extract+swap),
`ClangdDownloadFlow` (+VM wiring `MainWindowViewModel.cs:433-438`, Tools menu
`MainWindow.axaml:296-297`), `IdeInAngerTests.cs:427-526` (debug e2e anatomy:
TCS-per-event, 90 s budgets, `AssertProcessExits`, kill-tree finally),
`ClangdE2ETests.cs:78-128` (mixed-project fixture XML: `<Language>Cpp</Language>` +
`<TargetBackend>Cpp</TargetBackend>`, no `<Compile>` items — sources discovered).

**Test seams:** `DebugService` has NO transport seam today (inline `ProcessStartInfo`,
all stream fields private; only `IOutputService` injected). No in-proc duplex-stream
helper exists anywhere in the suite. `AdapterProcessId` (concrete-only, retained after
Stop/Dispose) is load-bearing for `IdeInAngerTests`.

## Environment rules (violating these costs real time)

- PowerShell 5.1 shell. **NEVER `Get-Content`/`Set-Content` round-trips on repo files**
  (BOM-less UTF-8 mojibake). Use Read/Edit/Write/Grep/Glob. Commit messages: Write a file
  to the scratchpad, `git commit -F <file>`.
- **Full suite before EVERY commit.** Output exceeds the 30k tool truncation — run
  detached and poll the log:
  ```powershell
  Start-Process cmd -ArgumentList '/c dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release > %TEMP%\vgs-p4-suite.log 2>&1'
  # poll until: Select-String -Path $env:TEMP\vgs-p4-suite.log -Pattern 'Duration:.*VisualGameStudio\.Tests\.dll'
  ```
  Baseline: **3250 passed / 1 known env flake**
  (`CppTemplate_CreatesProject_ThatCompilerBuilds("cpp-game-app")`, BL6009 engine .lib —
  `dotnet test` exiting 1 from it is NORMAL) **/ 1 known skip** (toolchain-conditional).
  Runs take 15–20 min. Suite count grows (or, in Task 11, shrinks) every task — each task
  states its delta; carry the running total forward.
- **NO InternalsVisibleTo** anywhere except the existing BasicLang→Tests grant. Every test
  seam is `public` (precedent: `ParseCompletions`, the 3b gate predicates).
- CS8604 is NoWarn in all four IDE projects — `string?` into `string` compiles silently;
  check null-flow by reading, not by trusting the compiler.
- Codegen changes (Task 0): validate through the CLI **and** the optimizer
  (`CompileToCppOptimized` shape, `CppCollectionTests.cs:102-131` — note its
  `Build(ast, "TestModule")` passes NO source path; a `#line` test must pass the third
  argument), and through BOTH entry points (CLI build + IDE build → same
  `CppProjectBuilder.Build`).
- `dotnet clean` before build after AXAML changes (Task 12).
- The user-level `~/.vgs` is sacred in tests: every new service takes an injectable root.
  Installer tests use local zip fixtures, NEVER live URLs.
- e2e fixtures duplicate private helpers (`RecordingOutputService`, `WithTimeout`,
  `AssertProcessExits`) per established convention — `IdeInAngerTests`' helpers are
  private nested and NOT importable.

## File structure (decomposition locked here)

| Unit | File | Responsibility |
|---|---|---|
| #line port | `BasicLang/CppCodeGenerator.cs`, `CppCodeGenerator.Split.cs`, `BasicLang/ProjectSystem/CppProjectBuilder.cs` | Debug-only `#line` emission, forward slashes, reset-to-generated-file |
| Gate e2e | `VisualGameStudio.Tests/Integration/NativeDebugGateTests.cs` (new) | raw-stdio lldb-dap `.bas` bind/stop/step verdict |
| Session | `VisualGameStudio.ProjectSystem/Services/DapSession.cs` (new) | transport core: spawn/streams/framing/correlation/events; later the handshake |
| Fake ring | `VisualGameStudio.Tests/Services/FakeDapAdapter.cs` (new) | scripted in-proc adapter, three timing regimes |
| Protocol types | `VisualGameStudio.Core/Abstractions/Services/DapProtocol.cs` (new) | `DapTimeoutProfile`, `DapCapabilities`, `DapExceptionFilter`, `DapLaunchCommand` |
| Descriptor | `VisualGameStudio.Core/Abstractions/Services/DebugAdapterDescriptor.cs` (new) | pure identity, routing predicate, launch-command factory, timeouts, fallback filters |
| Registry | `VisualGameStudio.Core/Abstractions/Services/IDebugAdapterRegistry.cs` + `VisualGameStudio.ProjectSystem/Services/DebugAdapterRegistry.cs` (new) | lookup by project/id, public `Register` door |
| Locator | `VisualGameStudio.ProjectSystem/Services/LldbDapLocator.cs` (new) | setting → `~/.vgs/tools` → PATH → known dirs (incl. winlibs) |
| Installer | `VisualGameStudio.ProjectSystem/Services/LldbDapInstaller.cs` (new) | pinned zip (PLACEHOLDER pins) → SHA gate → staged install |
| Download UX | `VisualGameStudio.Shell/Services/LldbDapDownloadFlow.cs` (new), `MainWindowViewModel.cs`, `MainWindow.axaml` | Tools menu, offer toast on native F5 |
| F5 seam | `MainWindowViewModel.cs:3477-3488` + `VisualGameStudio.Shell/Services/DebugLaunchPolicy.cs` (new) | registry routing, Release no-debug-info warning |
| Exceptions | `VisualGameStudio.Shell/Services/ExceptionFilterTranslator.cs` (new), `ExceptionSettingsViewModel.cs`, `IDialogService.cs`, `DialogService.cs`, `MainWindowViewModel.cs:3905-3971` | adapter-driven dialog vocabulary |
| Native e2e | `VisualGameStudio.Tests/Integration/NativeDebugE2ETests.cs` (new) | real MSVC build + real lldb-dap, full debug loop |
| Runbook | `docs/superpowers/specs/2026-07-19-lldb-dap-zip-release-runbook.md` (new) | one-time zip build pipeline spec |

Order retires risk early: 0–1 (the gate, timeboxed) → 2–5 (extraction + protocol fixes,
managed e2e gating every commit) → 6–8 (descriptor/locator/registry) → 9 (F5 live) → 10
(exceptions) → 11 (deletions) → 12–13 (acquisition) → 14 (native e2e) → 15 (DoD). Every
task lands green and independently committable.

---

## Task 0: `#line` port into `CppCodeGenerator` (Step-0 gate, part A)

**Timebox (Tasks 0+1 together): one working day.** Overrun = stop and report to the
orchestrator with findings; that report IS the checkpoint.

**Files:**
- Modify: `BasicLang/CppCodeGenerator.cs` (fields near :18; `GenerateFunction` :1473-1504;
  loops :1660-1664, :3023-3031, :3103-3117; lambda capture :658-680),
  `BasicLang/CppCodeGenerator.Split.cs` (`CaptureSection` :172-188 + its 6 call sites
  in :119-163), `BasicLang/ProjectSystem/CppProjectBuilder.cs:255`
- Test: `VisualGameStudio.Tests/Compiler/CppLineDirectiveTests.cs` (new)

- [ ] **Step 1: Write the failing tests.** New fixture with a private helper mirroring
  `CompileToCppOptimized` (`CppCollectionTests.cs:102-131`) but with a source path and
  options:

```csharp
private string? CompileToCpp(string source, bool emitLineDirectives, bool optimize,
    out List<string> errors, string sourcePath = @"C:\proj\Main.bas")
{
    // Lexer -> Parser -> SemanticAnalyzer (mirror CppCollectionTests.cs:102-118), then:
    //   var irModule = new IRBuilder(analyzer).Build(ast, "TestModule", sourcePath);
    //   ⚠ the THIRD argument is the point — the existing helpers omit it, so
    //   IRFunction.SourceFilePath is null there and #line never fires.
    //   if (optimize)   // AddStandardPasses() returns void (IROptimizer.cs:1030) —
    //   {               // three statements, exactly CppCollectionTests.cs:125-127:
    //       var pipeline = new BasicLang.Compiler.IR.Optimization.OptimizationPipeline();
    //       pipeline.AddStandardPasses();
    //       pipeline.Run(irModule);
    //   }
    //   return new CppCodeGenerator(new CppCodeGenOptions {
    //       GenerateComments = false, EmitLineDirectives = emitLineDirectives }).Generate(irModule);
}

[Test] public void Debug_EmitsForwardSlashDirective_AtTheStatementLine()
    // 3-line Sub Main with "Dim x As Integer = 42" on source line 2 ->
    // output contains the EXACT literal: #line 2 "C:/proj/Main.bas"
[Test] public void Directives_NeverContainBackslashes()
    // every Regex match of ^#line \d+ "(.*)"$ has a capture with no '\\' — the \U landmine, pinned
[Test] public void ConsecutiveSameLineInstructions_EmitOneDirective()
    // "Dim s As String = a & b & c" (several IR instructions, one line) -> exactly one #line for that line
[Test] public void DefaultOptions_EmitNothing()
    // emitLineDirectives:false -> output contains no "#line" (Release/every-other-ctor byte-identity)
[Test] public void OptimizedIR_StillEmits_AndSynthesizedCodeResetsToTheGeneratedFile()
    // optimize:true over a loop the optimizer rewrites -> user statements carry .bas directives AND
    // at least one reset directive naming the generated file (see EmitLineReset below) appears
[Test] public void SplitEmission_ResetsDedupePerFile()
    // build 2 modules via GenerateSplit (mirror CppSplitEmissionTests's driver) with both
    // modules' first statement on .bas line 1 -> EACH .g.cpp contains its own #line 1 directive
    // (dedupe state is per captured file, not global)
[Test] public void LineDirectiveOutput_StillCompilesAndRuns()
    // the CompileRun idiom (CppCollectionTests.cs:245-269, Assert.Ignore without a toolchain):
    // Debug output with directives compiles and prints the same stdout as without
```

- [ ] **Step 2:** Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~CppLineDirectiveTests"`
  Expected: FAIL — `CppCodeGenOptions` has no `EmitLineDirectives` (compile error first,
  then red).
- [ ] **Step 3: Implement the generator side.**
  - `CppCodeGenOptions` (:3472-3478) gains `public bool EmitLineDirectives { get; set; } = false;`
    — default false keeps every other constructor site (`BackendRegistry.cs:36,42`,
    `MultiTargetCompiler.cs:48-54`, `Program.cs:1148,3649-3656`) byte-identical.
  - New state + members on `CppCodeGenerator` (beside `_output`, :18):

```csharp
private int _lastEmittedSourceLine = -1;
private string _lastEmittedSourceFile;      // normalized (forward slashes)
private string _currentGeneratedFileName;   // set per captured split file; module-derived in Generate()
private bool _suppressLineDirectives;       // true while capturing inline lambda bodies

private void EmitLineDirective(int sourceLine, string sourceFile)
{
    if (!_options.EmitLineDirectives || _suppressLineDirectives) return;
    if (sourceLine <= 0 || string.IsNullOrEmpty(sourceFile)) return;
    // C++ #line filenames are ESCAPE-PROCESSED string literals — unlike C#'s
    // (CSharpBackend.EmitLineDirective takes the path verbatim). A raw Windows
    // path breaks the compile: "C:\Users\..." contains \U, a malformed
    // universal-character-name. Forward slashes are accepted by MSVC/clang/gcc.
    var normalized = sourceFile.Replace('\\', '/');
    if (sourceLine == _lastEmittedSourceLine &&
        string.Equals(normalized, _lastEmittedSourceFile, StringComparison.OrdinalIgnoreCase))
        return;
    _lastEmittedSourceLine = sourceLine;
    _lastEmittedSourceFile = normalized;
    // Column-0, unindented, bypassing WriteLine's indent — same rationale as the C# backend.
    _output.AppendLine($"#line {sourceLine} \"{normalized}\"");
}

// C++ has no `#line hidden`: optimizer-synthesized instructions (SourceLine == 0)
// are re-pointed at the generated file ITSELF so the line table maps them to real
// lines instead of smearing them onto the last user statement.
private void EmitLineReset()
{
    if (!_options.EmitLineDirectives || _suppressLineDirectives) return;
    if (_lastEmittedSourceFile == null) return;   // already in generated-file coordinates
    if (string.IsNullOrEmpty(_currentGeneratedFileName)) return;
    _lastEmittedSourceLine = -1;
    _lastEmittedSourceFile = null;
    // `#line N` numbers the line FOLLOWING the directive. The directive lands on
    // physical line (newlines-so-far + 1), so N = newlines + 2. O(buffer) scan,
    // bounded by mapped->synthesized transitions — Debug-only cost, measured fine.
    int newlines = 0;
    for (int i = 0; i < _output.Length; i++) if (_output[i] == '\n') newlines++;
    _output.AppendLine($"#line {newlines + 2} \"{_currentGeneratedFileName}\"");
}
```

  - Per-instruction hook in ALL THREE loops. In `GenerateBlock` (:1660-1664, loop var
    `instruction`):

```csharp
if (instruction.SourceLine > 0)
    EmitLineDirective(instruction.SourceLine, _currentFunction?.SourceFilePath);
else
    EmitLineReset();
instruction.Accept(this);
```

    In `EmitBlockInstructions` (:3023-3031, loop var `inst`) the hook goes **ABOVE the
    branch-skip `continue` at :3028** — terminator lines (If/While conditions) would
    otherwise lose their directive; benign under the goto model, but keep them covered:

```csharp
foreach (var inst in block.Instructions)
{
    if (inst.SourceLine > 0) EmitLineDirective(inst.SourceLine, _currentFunction?.SourceFilePath);
    else EmitLineReset();
    if (inst is IRBranch or IRConditionalBranch) continue;
    inst.Accept(this);
}
```

    In `EmitRegionInstructions` (:3103-3120, loop var `inst`) place the same hook at the
    top of the loop body, above the branch/end-mode switch, for the same reason.
    Do NOT hook the loops at :336/:1294 (static-init/analysis paths — read them first;
    they do not emit function-body statements).
  - Dedupe reset per function: in `GenerateFunction` immediately after
    `_currentFunction = function;` (:1475) add `_lastEmittedSourceLine = -1;
    _lastEmittedSourceFile = null;` (mirror `CSharpBackend.cs:1331-1332`). Grep for any
    other `_currentFunction =` assignment sites in the C++ generator (class-method
    emission) and reset there too.
  - Lambda capture (:658-680): wrap the body-capture region so directives are suppressed —
    set `_suppressLineDirectives = true` where `_output` is swapped (:658), restore in a
    `finally` beside `_output = savedOutput;` (:680). A `#line` inside an inlined lambda
    body would land mid-expression and break the compile.
  - `CaptureSection` (`Split.cs:172-188`): change signature to
    `CaptureSection(string fileName, Action emit)`; before `emit()` set
    `_currentGeneratedFileName = fileName; _lastEmittedSourceLine = -1;
    _lastEmittedSourceFile = null;`, restore `_currentGeneratedFileName = null` in the
    existing `finally` (:183-187). Update the 6 call sites (:119, :127, :134, :141, :156,
    :163) to pass the file name they already hand to `result.Files.Add`. In `Generate(IRModule)` (:64) set
    `_currentGeneratedFileName = module.Name + ".g.cpp"` at entry (single-file path — used
    by unit tests and the demo; the real build goes through `GenerateSplit`).
- [ ] **Step 4: Plumb Debug-config-only emission.** `CppProjectBuilder.cs` at :255 —
  replace the generator construction:

```csharp
// Debug-only #line: keyed off the SAME per-config DebugSymbols bit that decides
// /Zi | -g (CppCompileRequest, :343-349) so source-mapped line tables and debug
// info always travel together. Release output stays byte-identical (DebugSymbols
// defaults false there, ProjectFile.cs:94-95). IntelliSense emission stays clean:
// clangd serves the generated headers to user .cpp and #line would remap its
// diagnostics onto .bas lines.
bool emitLineDirectives = !forIntelliSense;
if (emitLineDirectives && project.Configurations.TryGetValue(configuration, out var cfgForCodegen))
    emitLineDirectives = cfgForCodegen.DebugSymbols;
split = new CppCodeGenerator(new CppCodeGenOptions { EmitLineDirectives = emitLineDirectives })
    .GenerateSplit(compilation.CombinedIR, safeProject, unitIRs, emitMain);
```

- [ ] **Step 5:** Run the fixture: green. Then the CLI validation (both entry points share
  `CppProjectBuilder.Build`, so the CLI run covers the IDE build path too — state this in
  the task report): `dotnet build BasicLang/BasicLang.csproj -c Release`, then create a
  temp project (`$env:TEMP\vgs-p4-line\App.blproj` + `Program.bas`; XML shape from
  `ClangdE2ETests.cs:80-89` but `<Language>` omitted — plain BasicLang on
  `<TargetBackend>Cpp</TargetBackend>`), run
  `dotnet BasicLang\bin\Release\net8.0\BasicLang.dll build $env:TEMP\vgs-p4-line\App.blproj`.
  (⚠ PowerShell does NOT expand `%TEMP%` — `$env:TEMP` in directly-run commands; `%TEMP%`
  only survives inside `cmd /c '...'` strings like the suite-gate line.)
  Expected: build succeeds; `Select-String -Path $env:TEMP\vgs-p4-line\obj\gen\*.g.cpp -Pattern '#line \d+ "C:/'`
  hits; `Select-String -Pattern '#line \d+ ".*\\'` → zero hits (single escaped
  backslash — a raw un-normalized Windows path contains SINGLE backslashes; a
  double-backslash pattern would pass vacuously; matches the T15 gate form).
- [ ] **Step 6:** Full suite (detached, poll): 3250 + 7 new / 1 flake / 1 skip.
- [ ] **Step 7:** Commit: `feat(cpp): #line directives on the C++ backend — Debug-only, forward-slash paths, reset-to-generated for synthesized code`

## Task 1: Step-0 gate, part B — raw lldb-dap `.bas` e2e + DECISION CHECKPOINT

**Files:**
- Create: `VisualGameStudio.Tests/Integration/NativeDebugGateTests.cs`

The gate predates `DapSession` — the test hand-rolls a minimal raw-stdio DAP driver
(~120 lines, private to the fixture) using the proven shapes: the BOM-less
`ProcessStartInfo` block from `DebugService.cs:105-121` (verbatim, minus WorkingDirectory
plumbing), the Latin1 framing reader from :1089-1124, and a plain seq counter + response
wait. It also hand-rolls the CORRECT handshake order (this is the reference
implementation Task 4 later productizes).

- [ ] **Step 1: Write the fixture.** `[TestFixture] [NonParallelizable]
  [Category("NativeDebugGate")]`. lldb-dap discovery: probe the literal path
  `C:\winlibs\mingw64\bin\lldb-dap.exe`; absent → `Assert.Ignore`. MSVC discovery:
  `CppToolchain.Find()` null → `Assert.Ignore`. ⚠ Never spawn `lldb-dap --version` — it
  parks on stdin and hangs; existence checks only. Temp project fixture — a real MIXED
  project (spec §7 says so explicitly: multi-TU emission and the generated-header
  boundary must be under the gate; a mixed-only failure — e.g. breakpoints binding in a
  single TU but not across split `.g.cpp` files — must not produce a false PASS at the
  checkpoint). Dispose with the 5×250 ms retry-delete from `IdeInAngerTests.cs:123-140`.

  `Logic.bas`:
```
Function AddNumbers(a As Integer, b As Integer) As Integer
    Dim total As Integer = a + b
    Dim doubled As Integer = total * 2
    Return doubled
End Function
```
  `main.cpp`:
```cpp
#include "Logic.g.h"
int main() {
    return AddNumbers(40, 2) == 84 ? 0 : 1;
}
```
  Line numbers are load-bearing: `const int BreakpointLine = 2` (`Dim total`),
  `const int StepTargetLine = 3` (`Dim doubled`) — named constants with a "line numbers
  are load-bearing" comment, exactly the `TempBasicLangProject.BreakpointLineAfterAwait`
  idiom (`IdeInAngerTests.cs:51-56`).

  `App.blproj`: the ClangdE2ETests mixed shape (`ClangdE2ETests.cs:80-89`) —
  `<BasicLangProject Version="1.0">` + `<OutputType>Exe</OutputType>` +
  `<Language>Cpp</Language>` + `<TargetBackend>Cpp</TargetBackend>`, no `<Compile>` items
  (sources discovered). Build via `BuildService.BuildProjectAsync` (serializer-loaded,
  `IdeInAngerTests.cs:221-232` shape) — Debug is the default configuration → `#line` is
  in the generated code (Task 0), `/Zi` is on.

- [ ] **Step 2: Write the gate test** `BasBreakpoint_Binds_Stops_And_StepsToTheNextBasStatement`:
  1. Build; assert `ExecutablePath` exists.
  2. Spawn lldb-dap raw. Handshake in the CORRECT order: send `initialize`
     (client args copied from `DebugService.cs:146-158`) → read its response → send
     `launch` `{ program, cwd = project dir, stopOnEntry = false }` **without waiting for
     its response** → await the `initialized` EVENT → send `setBreakpoints`
     `{ source.path = <absolute Logic.bas>, breakpoints = [{ line = BreakpointLine }] }`.
  3. **Path-form probe (record the answer — it feeds Tasks 9/14):** if the response's
     breakpoint is `verified:false`, retry `setBreakpoints` with the forward-slash form of
     the same path (the PDB records the `#line` spelling `C:/...`; lldb usually normalizes
     separators on Windows, but this is exactly the kind of 19-vs-22 quirk the spec warns
     about). Assert one of the two forms yields `verified:true` at the pinned line, and
     record WHICH in the checkpoint report.
  4. Send `configurationDone` → await the `launch` response (lldb-dap completes it about
     now) → await `stopped` (reason `breakpoint`).
  5. `stackTrace { threadId = <from the stopped event> }` → top frame's `source.path`
     ends with `Logic.bas` at the breakpoint line (NOT a `.g.cpp`).
  6. `next { threadId = <same> }` → await `stopped` (reason `step`) → `stackTrace` again →
     top frame is `Logic.bas` at `StepTargetLine` — not generated glue and not `main.cpp`.
  7. `disconnect { terminateDebuggee = true }` → process exits (poll ≤10 s, kill-tree in
     `finally` regardless — copy `AssertProcessExits` from `IdeInAngerTests.cs:191-202`).
  Budgets: 60 s launch/stopped, 30 s per response, generous by design.
- [ ] **Step 3:** Run it: `dotnet test ... --filter "FullyQualifiedName~NativeDebugGateTests"`.
  First run is the measurement — iterate within the timebox (adapter stderr goes to the
  test output; dump ALL raw DAP traffic on failure, the `output.Dump()` idiom).
- [x] **Step 4: ⛔ DECISION CHECKPOINT — VERDICT: PASS (2026-07-19, commit e483de5).**
  `.bas`-source debugging is IN v1; Task 14 keeps its `.bas` test. **Winning breakpoint
  path form: OS-NATIVE BACKSLASH** — `setBreakpoints` with `C:\...\Logic.bas` verified
  immediately against the forward-slash `#line` spelling (lldb's PDB reader normalizes
  separators); the forward-slash fallback probe stays in the test for other lldb builds.
  Bonus finding: the gate's first run caught `StrengthReductionPass` dropping
  `SourceLine` on replacement ops (step landed in `Logic.g.cpp`) — fixed metadata-only
  in `IROptimizer.cs`; ~14 sibling replacement sites chipped (task_90438de9). Real
  threadId observed (6908) — the threadId=1 landmine is confirmed live.
  *(Original checkpoint text follows for reference.)*
  - **PASS** (all of: verified bind at the right line, stop there, step-next on the next
    `.bas` statement): `.bas`-source debugging is IN v1. Task 14 keeps its `.bas` test.
    Record the winning breakpoint path form here in the plan file.
  - **FAIL / PARTIAL** (binds but steps into glue, or never binds on either path form):
    v1 ships C++-file breakpoints only. Actions: (a) write the evidence to
    `docs/superpowers/specs/2026-07-19-line-gate-findings.md` (raw DAP transcripts, which
    leg failed, 19-vs-22 suspicion notes); (b) mark this test
    `[Explicit("Step-0 gate verdict FAIL — see the findings spec")]` so the suite stays
    green; (c) descope Task 14's `.bas` test (delete its planned step, note here);
    (d) file a follow-up chip for retrying against lldb-dap 22 once the zip exists.
    **Task 0's port STAYS either way** — Debug-only, tested, Release-identical.
- [ ] **Step 5:** Full suite: 3250 + 7 + 1 (or +1 Explicit-skipped) / 1 flake / 1 skip.
- [ ] **Step 6:** Commit: `test(cpp): Step-0 gate — .bas breakpoints through #line + raw lldb-dap, with verdict`

## Task 2: Extract `DapSession` — transport core with a test seam

**Files:**
- Create: `VisualGameStudio.ProjectSystem/Services/DapSession.cs`
- Modify: `VisualGameStudio.ProjectSystem/Services/DebugService.cs`
- Test: regression = the managed debug e2e (no new tests this task; the fake-adapter ring
  arrives in Task 3 against this seam)

This is a MOVE, not a rewrite: wire behavior must be byte-identical. The four
comment-bearing blocks (BOM, Latin1, byte-count framing, RunContinuationsAsynchronously)
move with their comments intact.

- [ ] **Step 1: Create `DapSession`** (public sealed, ns
  `VisualGameStudio.ProjectSystem.Services`):

```csharp
/// <summary>
/// DAP transport + protocol core (Phase 4): adapter process, BOM-less UTF-8 stdio,
/// Latin1 re-decode framing, request/response correlation, raw event dispatch.
/// Owns NO IDE debug state — DebugService orchestrates on top of this.
/// The stream ctor is the test seam (no InternalsVisibleTo in this assembly —
/// public on purpose, like ParseCompletions).
/// </summary>
public sealed class DapSession : IDisposable
{
    // Production: spawn the adapter process described by startInfo.
    // startInfo MUST carry the BOM-less encodings — the factory helper below sets them.
    public DapSession(ProcessStartInfo startInfo, IOutputService outputService);
    // Test seam: drive over in-proc streams; no process is spawned.
    public DapSession(Stream readFromAdapter, Stream writeToAdapter, IOutputService outputService);

    /// Builds the ProcessStartInfo with the load-bearing encodings (moved verbatim
    /// from DebugService.cs:105-121, BOM comment included).
    public static ProcessStartInfo BuildStartInfo(string fileName, string arguments, string? workingDirectory);

    public int? AdapterProcessId { get; }              // null for the stream ctor
    public bool Start();                               // spawn (if process ctor) + start the read loop
    public event EventHandler<DapEventArgs>? EventReceived;   // raw: (string EventType, JsonElement Body)
    /// Session death is an EVENT, not a hang (spec §8): raised exactly once when the
    /// adapter process exits OR the read loop hits EOF. Carries the exit code when known.
    public event EventHandler<DapSessionClosedEventArgs>? Closed;
    public Task<JsonElement> SendRequestAsync(string command, object arguments,
        CancellationToken cancellationToken = default);       // correlation + internal 30s timeout (Task 4 makes it profile-driven)
    public void CancelPending();                        // cancel every pending request TCS
    public void Dispose();                              // kill tree (process ctor), dispose streams; idempotent
}
public sealed class DapEventArgs : EventArgs
{
    public string EventType { get; init; } = "";
    public JsonElement Body { get; init; }
}
public sealed class DapSessionClosedEventArgs : EventArgs
{
    public int? ExitCode { get; init; }   // null when only the stream ended (test seam / EOF before exit)
}
```

  Move these `DebugService` members into it unchanged: `JsonOptions` (:52-56), the spawn +
  stream setup (:105-143 → ctor/`Start`/`BuildStartInfo`), `ReadMessagesAsync` (:1066-1087),
  `ReadMessageAsync` (:1089-1124), the response-correlation half of `ProcessMessage`
  (:1130-1151), `SendRequestAsync` (:1258-1287), `SendMessageAsync` (:1289-1318), the
  transport half of `CleanupProcesses` (:432-486 — writer/reader dispose + adapter
  kill-tree; the `_targetProcess` kill stays in DebugService, it belongs to
  run-without-debugging). The EVENT half of `ProcessMessage` (:1152-1206) becomes: session
  raises `EventReceived`; the whole switch body moves to a private
  `DebugService.OnAdapterEvent` handler, logic unchanged.

  **ONE deliberate behavior change (document it in the code):** the original read loop
  spins on EOF — `ReadMessageAsync` returns null and the `while` just iterates again, so
  a crashed adapter (which never sends the DAP `terminated` event) is a silent hang. The
  extracted loop treats a null read as EOF: `break` out, then raise `Closed` (exactly
  once — an `Interlocked` guard shared with the process-exit path). The process ctor also
  sets `EnableRaisingEvents = true` and wires `Process.Exited` → the same guarded `Closed`
  raise with the exit code. This is the transport half of spec §8's "session death is an
  event, not a hang"; cancellation via `_cts` still exits through the
  `OperationCanceledException` arm WITHOUT raising `Closed` (a user-initiated stop is not
  a death).
- [ ] **Step 2: Rewire `DebugService`.** Fields `_debugProcess/_writer/_reader/_readTask/
  _requestSeq/_pendingRequests/_lock/_writeLock` are deleted; a single
  `private DapSession? _session;` replaces them. `StartDebuggingAsync`/`AttachToProcessAsync`
  build the PSI via `DapSession.BuildStartInfo("dotnet", $"\"{_compilerPath}\" --debug-adapter", ...)`,
  construct the session (through the factory seam below), `Start()`, subscribe
  `EventReceived`, then run the EXISTING handshake sequence (:146-186 — still the old
  order; Task 4 fixes it) via `_session.SendRequestAsync`. `AdapterProcessId`: set from
  `_session.AdapterProcessId` at start and RETAINED after Stop/Dispose (load-bearing,
  `IdeInAngerTests` reads it post-Dispose). **Subscribe `Closed` too** (the UI half of
  spec §8's "session death is an event, not a hang"): when the closing session is still
  the active one and `State` is not already `Stopped`, run the existing terminated
  handling — `CleanupProcesses()` + `SetState(DebugState.Stopped)` — and write the
  diagnostic
  `"[Debug] Adapter '{name}' exited unexpectedly (code {X}) — debug session ended."`
  (`name` = the launch command's file name for now; Task 8 upgrades it to the
  descriptor's `DisplayName`; omit the code clause when `ExitCode` is null). A normal
  Stop/disconnect must NOT double-report: the handler no-ops when state is already
  `Stopped`. Add the factory seam as an optional ctor
  parameter — MS.DI honors defaulted parameters, so `AddSingleton<IDebugService, DebugService>()`
  keeps resolving:

```csharp
public DebugService(IOutputService outputService,
    Func<ProcessStartInfo, DapSession>? sessionFactory = null)
```

- [ ] **Step 3:** Build: `dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release` — clean.
- [ ] **Step 4: The regression gate for this whole refactor** — run the managed e2e
  explicitly: `dotnet test ... --filter "FullyQualifiedName~IdeInAngerTests"` → the debug
  e2e (`Debug_BreakpointAfterAwait_StackAndLocals_ThenCleanShutdown`) passes.
- [ ] **Step 5:** Full suite: same counts as Task 1 (pure move).
- [ ] **Step 6:** Commit: `refactor(debug): extract DapSession — transport core with a public stream seam, wire behavior byte-identical`

## Task 3: `FakeDapAdapter` + the transport unit ring

**Files:**
- Create: `VisualGameStudio.Tests/Services/FakeDapAdapter.cs`,
  `VisualGameStudio.Tests/Services/DapSessionTests.cs`

No in-proc duplex-stream helper exists in the suite (verified) — build one on two
anonymous-pipe pairs (works in-proc: `AnonymousPipeServerStream` + client stream over its
handle).

- [ ] **Step 1: Build the fake** (public sealed, test assembly):

```csharp
public sealed class FakeDapAdapter : IDisposable
{
    public enum InitializedTiming
    {
        AfterLaunchRequestReceived,      // lldb-dap-shaped
        DuringLaunchBeforeItsResponse,   // managed-shaped
        RightAfterInitializeResponse     // legacy --dap-legacy-shaped (early emission)
    }
    public InitializedTiming Timing { get; set; }
    public bool DeferLaunchResponseUntilConfigurationDone { get; set; }   // lldb-dap-shaped
    public object CapabilitiesBody { get; set; }   // returned as the initialize response body
    public ConcurrentQueue<(string Command, JsonElement Arguments)> Received { get; }
    public byte[] FirstBytesFromSession { get; }   // raw capture before framing parse
    public Stream SessionReads { get; }            // adapter -> session
    public Stream SessionWrites { get; }           // session -> adapter
    public void EmitEvent(string eventType, object body);   // push e.g. stopped {reason, threadId}
    public void RespondToNextRequestWithFailure(string command, string message);
    public void CloseFromAdapterSide();            // simulates adapter death
    // Regime factories:
    public static FakeDapAdapter LldbShaped();     // Timing=AfterLaunchRequestReceived, DeferLaunch=true,
                                                   // caps: exceptionBreakpointFilters cpp_throw/cpp_catch
    public static FakeDapAdapter ManagedShaped();  // Timing=DuringLaunchBeforeItsResponse, caps: all/uncaught/thrown
    public static FakeDapAdapter LegacyShaped();   // Timing=RightAfterInitializeResponse
}
```

  The adapter loop mirrors the wire contract exactly: reads
  `Content-Length: N\r\n\r\n` + N BYTES, parses UTF-8 JSON, replies
  `{ type:"response", request_seq, success, command, body }`, events
  `{ type:"event", event, body }`. It responds to every request immediately EXCEPT per the
  regime knobs above.
- [ ] **Step 2: Write the failing transport tests** (construct
  `new DapSession(fake.SessionReads, fake.SessionWrites, new RecordingOutputService())` —
  duplicate `RecordingOutputService` into this file per suite convention):

```csharp
[Test] public async Task FirstBytesFromSession_AreAContentLengthHeader_NoBomEver()
    // send one request; fake's FirstBytesFromSession starts with ASCII "Content-Length:" — EF BB BF pinned out
[Test] public async Task MultibyteBody_DoesNotCorruptFramingOfTheNextMessage()
    // fake emits an output event whose body contains "héllo — ✓" then answers a request;
    // both arrive intact (the Latin1 byte-count re-decode, pinned)
[Test] public async Task FailureResponse_FaultsTheRequestTask()
[Test] public async Task AdapterDeath_EndsTheReadLoopQuietly_AndPendingRequestsDoNotHangForever()
    // CloseFromAdapterSide -> pending SendRequestAsync completes canceled/faulted within its timeout
[Test] public async Task AdapterDeath_RaisesClosedExactlyOnce()
    // CloseFromAdapterSide -> Closed fires once (EOF path); a subsequent Dispose does not re-raise
[Test] public async Task UserStop_DoesNotRaiseClosed()
    // cancel via the session's own stop path -> read loop exits through the OCE arm, Closed silent
[Test] public async Task AdapterCrashMidSession_ReturnsToEditMode_AndWritesDiagnostic()
    // the UI half, DebugService over the sessionFactory seam + ManagedShaped fake:
    // handshake completes -> CloseFromAdapterSide -> DebugState.Stopped within the budget,
    // RecordingOutputService contains "exited unexpectedly", nothing hangs
[Test] public async Task Events_AreRaisedWithTypeAndBody()
```

- [ ] **Step 3:** Run: FAIL (missing types) → implement → green:
  `dotnet test ... --filter "FullyQualifiedName~DapSessionTests"`.
- [ ] **Step 4:** Full suite: +8. **Step 5:** Commit:
  `test(debug): FakeDapAdapter — scripted in-proc adapter + the transport ring the wire never had`

## Task 4: Spec-correct handshake in `DapSession` + capabilities retention + timeout profile

**Files:**
- Create: `VisualGameStudio.Core/Abstractions/Services/DapProtocol.cs`
- Modify: `VisualGameStudio.ProjectSystem/Services/DapSession.cs`,
  `VisualGameStudio.ProjectSystem/Services/DebugService.cs` (:146-186 launch, :245-285
  attach — both replaced by one session call),
  `VisualGameStudio.Core/Abstractions/Services/IDebugService.cs` (+`Capabilities`)
- Test: extend `VisualGameStudio.Tests/Services/DapSessionTests.cs`

- [ ] **Step 1: Core protocol types** (new file, complete):

```csharp
namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>Per-adapter timeout budgets (spec §8). Lives on the descriptor from Task 6 on.</summary>
public sealed record DapTimeoutProfile(TimeSpan Launch, TimeSpan Request, TimeSpan Step, TimeSpan DisconnectGrace)
{
    public static readonly DapTimeoutProfile Managed =
        new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
    public static readonly DapTimeoutProfile LldbDap = Managed with { Launch = TimeSpan.FromSeconds(60) };
}

public sealed record DapExceptionFilter(string Id, string Label, bool Default, bool SupportsCondition = false);

/// <summary>The initialize response, RETAINED (today it is discarded at DebugService.cs:146-157).</summary>
public sealed class DapCapabilities
{
    public JsonElement Raw { get; }
    public IReadOnlyList<DapExceptionFilter> ExceptionBreakpointFilters { get; }
    public bool Supports(string capabilityName);   // TryGetProperty + GetBoolean, false on absent/undefined
    public static DapCapabilities Parse(JsonElement initializeResponseBody);
    // Parse notes: body may be Undefined (adapter sent no body) -> empty filters, Supports==false.
    // exceptionBreakpointFilters entries: { filter, label, default?, supportsCondition? }.
    // ⚠ default(JsonElement) access throws InvalidOperationException, not JsonException —
    // guard ValueKind == Undefined (the 3a lesson).
}
```

- [ ] **Step 2: Write the failing handshake tests** (the three timing regimes; run against
  the CURRENT extracted code first — the lldb-shaped one deadlocks, that is the red):

```csharp
[Test] public async Task Handshake_LldbShaped_Completes()
    // TODAY: times out — the client awaits the launch response before configurationDone,
    // and lldb-dap defers that response until after configurationDone. THE phase-blocking bug.
[Test] public async Task Handshake_ManagedShaped_Completes()
[Test] public async Task Handshake_LegacyShaped_EarlyInitializedIsNotLost()
    // initialized arrives right after the initialize response, possibly before launch is
    // sent — passes ONLY if the listener is armed before anything goes out
[Test] public async Task Handshake_AdapterSeesInitializeLaunchBreakpointsConfigurationDone_InThatOrder()
    // assert fake.Received command order exactly
[Test] public async Task Handshake_RetainsCapabilities()
    // LldbShaped -> session.Capabilities.ExceptionBreakpointFilters has cpp_throw + cpp_catch
[Test] public async Task LaunchResponse_CompletingBeforeConfigurationDone_IsAccepted()
    // ManagedShaped: neither timing is a protocol error
[Test] public async Task RequestTimeouts_ComeFromTheProfile()
    // profile with 200ms Request budget + a request the fake never answers -> canceled at ~200ms, not 30s
[Test] public async Task UnsupportedRequests_AreSkippedByCapability()
    // caps supportsDataBreakpoints=false -> GetDataBreakpointInfoAsync returns empty, fake.Received has no
    // dataBreakpointInfo (session skips what the adapter disclaims — spec §3.3.3)
```

- [ ] **Step 3: Implement.** `DapSession` gains
  `DapTimeoutProfile Timeouts` (ctor param, default `DapTimeoutProfile.Managed`),
  `public DapCapabilities? Capabilities { get; private set; }`, an optional per-call
  timeout on `SendRequestAsync(string, object, TimeSpan? timeout = null, CancellationToken cancellationToken = default)`
  (default = `Timeouts.Request`; the flat 30 s dies), and THE method:

```csharp
/// <summary>
/// Spec-correct DAP startup (spec §3.3.1). The exact sequence, in order, no reordering:
///  1. ARM the initialized listener BEFORE anything is sent — DAP allows emission any
///     time after the initialize response; the legacy --dap-legacy adapter fires it
///     ~50ms after initialize without waiting for launch. Arming late is a lost-event race.
///  2. initialize request -> response; RETAIN capabilities.
///  3. Send launch/attach WITHOUT awaiting its response. lldb-dap emits `initialized`
///     only while processing launch: awaiting the launch response here (the old client)
///     OR awaiting `initialized` before sending launch BOTH deadlock against it.
///  4. Await `initialized` (may have completed already — legacy).
///  5. Configuration: pushBreakpoints callback, then configurationDone.
///  6. NOW await the launch response — completed long ago (managed) or only now
///     (lldb-dap defers it past configurationDone). Neither timing is an error.
/// </summary>
public async Task InitializeAndLaunchAsync(
    string launchCommand,                 // "launch" | "attach"
    object launchArguments,
    object initializeArguments,
    Func<Task> pushConfigurationAsync,    // setBreakpoints / setExceptionBreakpoints
    CancellationToken cancellationToken)
{
    var initialized = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    _initializedTcs = initialized;                                   // completed by the read loop

    var initResponse = await SendRequestAsync("initialize", initializeArguments,
        cancellationToken: cancellationToken);
    Capabilities = DapCapabilities.Parse(initResponse);

    var launchTask = SendRequestAsync(launchCommand, launchArguments,
        timeout: Timeouts.Launch, cancellationToken: cancellationToken);

    try
    {
        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            cts.CancelAfter(Timeouts.Launch);
            using var _ = cts.Token.Register(() => initialized.TrySetCanceled());
            await initialized.Task;
        }

        await pushConfigurationAsync();
        await SendRequestAsync("configurationDone", new { }, cancellationToken: cancellationToken);
        await launchTask;
    }
    catch
    {
        // The launch request is still in flight on every failure path after step 3
        // (initialized timeout, breakpoint push, configurationDone). Never leave it
        // un-awaited — an unobserved fault would surface as UnobservedTaskException
        // long after the session died. Observe and swallow; the original failure wins.
        _ = launchTask.ContinueWith(t => _ = t.Exception,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        throw;
    }
}
```

  The read loop completes `_initializedTcs` on `event == "initialized"` (and still raises
  `EventReceived` for it). `DebugService`: both :146-186 and the attach twin :245-285
  collapse into one `InitializeAndLaunchAsync` call each (the initialize/launch/attach
  argument objects move verbatim; the breakpoint-push foreach becomes the
  `pushConfigurationAsync` lambda — and now ALSO pushes `setExceptionBreakpoints` when the
  UI armed filters pre-session, closing today's UI-only gap). Step requests pass
  `timeout: Timeouts.Step`; disconnect passes `Timeouts.DisconnectGrace` then kill-tree
  (existing :393-430 shape). Capability gates: `SetDataBreakpointsAsync` /
  `GetDataBreakpointInfoAsync` early-return empty unless `Supports("supportsDataBreakpoints")`;
  `SetFunctionBreakpointsAsync` unless `Supports("supportsFunctionBreakpoints")`.
  `IDebugService` gains `DapCapabilities? Capabilities { get; }` (doc: null until a
  session's initialize response arrives); `DebugService` proxies `_session?.Capabilities`.
- [ ] **Step 4:** Fixture green. **Step 5:** Managed e2e explicitly
  (`--filter FullyQualifiedName~IdeInAngerTests`) — the REAL managed adapter must accept
  the new order live (it emits `initialized` during launch processing; the new sequence
  awaits it — this is the one live-behavior change this task makes).
- [ ] **Step 6:** Full suite: +8. **Step 7:** Commit:
  `fix(debug): spec-correct DAP handshake — arm-before-send, launch in flight through configuration, capabilities retained`

## Task 5: Real threadIds end to end

**Files:**
- Modify: `VisualGameStudio.ProjectSystem/Services/DebugService.cs` (:492, :540, :556,
  :588, :668, :803 + the stopped case of `OnAdapterEvent`),
  `VisualGameStudio.Core/Abstractions/Services/IDebugService.cs:134`,
  `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs:3188`
- Test: extend `VisualGameStudio.Tests/Services/DebugServiceTests.cs` (new fixture region
  driving DebugService over the fake adapter via the Task 2 `sessionFactory` seam)

- [ ] **Step 1: Write the failing tests** (start DebugService with
  `sessionFactory: psi => new DapSession(fake.SessionReads, fake.SessionWrites, output)`,
  run the handshake against `FakeDapAdapter.ManagedShaped()`, then
  `fake.EmitEvent("stopped", new { reason = "breakpoint", threadId = 7 })`):

```csharp
[Test] public async Task StoppedThreadId_FlowsIntoContinue()      // continue args threadId == 7
[Test] public async Task StoppedThreadId_FlowsIntoStepPauseAndGoto()
[Test] public async Task StackTrace_DefaultsToTheStoppedThread()  // GetStackTraceAsync() -> threadId 7 on the wire
[Test] public async Task StackTrace_ExplicitThreadIdWins()        // GetStackTraceAsync(3) -> 3
```

- [ ] **Step 2:** FAIL (wire args carry 1). **Step 3: Implement.**
  - `private int _currentThreadId = 1;` — doc: "the threadId of the most recent stopped
    event; 1 until the first stop (DAP offers nothing better pre-stop)". Set in the
    stopped case beside the existing `ThreadId` parse (the `?? 1` default when the body
    lacks threadId stays).
  - Replace all five `threadId = 1` literals with `threadId = _currentThreadId`.
  - `GetStackTraceAsync(int threadId = 1)` → `GetStackTraceAsync(int threadId = 0)` in
    BOTH `DebugService.cs:803` and `IDebugService.cs:134`; `0` ⇒ `_currentThreadId`.
    Doc the sentinel on the interface. Callers riding the default
    (`CallStackViewModel.cs:72`, `VariablesViewModel.cs:89/:216`,
    `DebugConsoleViewModel.cs:396`) now get the current stopped thread with zero changes —
    that is the point. The ONE viewmodel edit (spec §3.4): `MainWindowViewModel.cs:3188`
    passes the event's id explicitly, mirroring :3417 and `IdeInAngerTests.cs:475`:
    `var frames = await _debugService.GetStackTraceAsync(e.ThreadId);`
- [ ] **Step 4:** Green. Grep gate (product code only —
  `VisualGameStudio.Core|ProjectSystem|Shell|Editor`, exclude Tests):
  `Select-String -Pattern '\bthreadId = 1'` → zero (word boundary — Select-String
  is case-insensitive by default and `_currentThreadId = 1` must NOT trip this);
  `Select-String -Pattern 'GetStackTraceAsync\(int threadId = 1\)'` → zero.
- [ ] **Step 5:** Managed e2e explicit run, then full suite: +4. **Step 6:** Commit:
  `fix(debug): thread the stopped threadId through continue/step/pause/goto and stack fetches`

## Task 6: `DebugAdapterDescriptor` (Core)

**Files:**
- Create: `VisualGameStudio.Core/Abstractions/Services/DebugAdapterDescriptor.cs`
- Test: `VisualGameStudio.Tests/Core/DebugAdapterDescriptorTests.cs`

- [ ] **Step 1: Write the failing tests:**

```csharp
[Test] public void Routing_ManagedServesManaged_LldbServesNative()
    // BasicLangProject permutations: CSharp backend -> managed only; TargetBackend.Cpp and
    // Language=Cpp -> lldb-dap only (IsNativeBuild is the predicate, BasicLangProject.cs:14)
[Test] public void LaunchCommand_IsResolvedPerCall_NeverCached()
    // counting resolver invoked twice for two ResolveLaunchCommand() calls —
    // the installed-mid-session contract (contrast clangd's DI-time resolution, D1)
[Test] public void LaunchCommand_NullWhenTheLocatorFindsNothing()
[Test] public void ManagedFactory_ComposesTheDotnetDebugAdapterCommand()
    // exact: FileName "dotnet", Arguments "\"<path>\" --debug-adapter"
[Test] public void Timeouts_LldbLaunchIsSixtySeconds_ManagedThirty()
[Test] public void FallbackFilters_AreThePinnedVocabulary()
    // lldb-dap: cpp_throw/cpp_catch; managed: all/uncaught/thrown (matches MainWindowViewModel.cs:3919-3944)
[Test] public void Toolchains_PairingMetadata_IsPinned()
    // lldb-dap: exactly { "msvc", "clang", "g++" } in order — spec §6's one-engine/three-routes
    // claim, pinned so a route can't silently drop; managed: empty
```

- [ ] **Step 2:** FAIL. **Step 3: Implement** (mirror `LanguageServerDescriptor`: sealed
  class, private ctor, static factories, consts co-located; NOT a record — it carries
  delegates):

```csharp
public sealed class DebugAdapterDescriptor
{
    public const string BasicLangManagedId = "basiclang-managed";
    public const string LldbDapId = "lldb-dap";
    /// <summary>Settings override key — mirror of LanguageServerDescriptor.ClangdSettingsKey.</summary>
    public const string LldbDapSettingsKey = "cpp.lldbDap.path";

    private readonly Func<DapLaunchCommand?> _resolveLaunchCommand;
    private readonly Func<BasicLangProject, bool> _serves;
    // private ctor assigning everything

    public string Id { get; }
    public string DisplayName { get; }
    public IReadOnlyList<string> Toolchains { get; }             // pairing metadata (v1: informational)
    public DapTimeoutProfile Timeouts { get; }
    public IReadOnlyList<DapExceptionFilter> FallbackExceptionFilters { get; }
    /// <summary>Resolved AT SESSION START — the adapter may be installed mid-session. Never cache.</summary>
    public DapLaunchCommand? ResolveLaunchCommand() => _resolveLaunchCommand();
    public bool Serves(BasicLangProject project) => _serves(project);

    public static DebugAdapterDescriptor BasicLangManaged(Func<string?> resolveCompilerPath) => new(
        id: BasicLangManagedId, displayName: "BasicLang (managed)",
        resolveLaunchCommand: () => resolveCompilerPath() is string p
            ? new DapLaunchCommand("dotnet", $"\"{p}\" --debug-adapter") : null,
        serves: p => !p.IsNativeBuild,
        toolchains: Array.Empty<string>(),
        timeouts: DapTimeoutProfile.Managed,
        fallbackExceptionFilters: new[]
        {
            new DapExceptionFilter("all", "All Exceptions", false),
            new DapExceptionFilter("uncaught", "Uncaught Exceptions", true),
            new DapExceptionFilter("thrown", "Thrown Exceptions", false, SupportsCondition: true),
        });

    public static DebugAdapterDescriptor LldbDap(Func<string?> resolveExecutable) => new(
        id: LldbDapId, displayName: "lldb-dap (native C++)",
        resolveLaunchCommand: () => resolveExecutable() is string p
            ? new DapLaunchCommand(p, string.Empty) : null,
        serves: p => p.IsNativeBuild,
        toolchains: new[] { "msvc", "clang", "g++" },        // one engine, three routes (spec §6)
        timeouts: DapTimeoutProfile.LldbDap,
        fallbackExceptionFilters: new[]
        {
            new DapExceptionFilter("cpp_throw", "C++ Throw", false),
            new DapExceptionFilter("cpp_catch", "C++ Catch", false),
        });
}
public sealed record DapLaunchCommand(string FileName, string Arguments);
```

  (`DapLaunchCommand` goes in `DapProtocol.cs` if you prefer one home — either way, Core.)
- [ ] **Step 4:** Green. **Step 5:** Full suite: +7. **Step 6:** Commit:
  `feat(debug): DebugAdapterDescriptor — declarative adapters, routed by IsNativeBuild, session-start resolution`

## Task 7: `LldbDapLocator`

**Files:**
- Create: `VisualGameStudio.ProjectSystem/Services/LldbDapLocator.cs`
- Modify: `VisualGameStudio.ProjectSystem/Services/ClangdLocator.cs` (expose the cached
  LLVM-dir list if not already public), `VisualGameStudio.ProjectSystem/Services/SettingsService.cs`
  (schema `Prop` beside the clangd one, ~:1266)
- Test: `VisualGameStudio.Tests/Services/LldbDapLocatorTests.cs`

- [ ] **Step 1: Write the failing tests** (all seams injected — mirror
  `ClangdLocatorTests`):

```csharp
[Test] public void Chain_OverrideBeatsToolsDir()
[Test] public void Chain_ToolsDirBeatsPath()
[Test] public void Chain_PathBeatsKnownDirs()
[Test] public void Chain_AllEmpty_ReturnsNull()               // null is a real answer (pre-install)
[Test] public void ToolsProbe_PicksHighestNumericVersion()     // lldb-dap_22.1.0 beats lldb-dap_9.0.0 (ordinal inverts!)
[Test] public void ToolsProbe_ProbesBinThenRootExeLayouts()    // bin\lldb-dap.exe canonical; root-level tolerated
[Test] public void KnownDirs_IncludeWinlibsMingw64()           // exact list assert; C:\winlibs\mingw64\bin present
[Test] public void KnownDirs_NeverSpawnAnything()              // fileExists-probe funcs only — no Process use in the sweep
```

- [ ] **Step 2:** FAIL. **Step 3: Implement** — structural copy of `ClangdLocator`
  (:67 `Locate`, :106 `ResolveClangdPath` seams, :146 `FindInToolsRoot`, :208 known-dirs):
  - `public static string? Locate(ISettingsService? settingsService)` —
    `SettingsConsumerRegistry.RegisterConsumer(DebugAdapterDescriptor.LldbDapSettingsKey, "LldbDapLocator → lldb-dap executable path override for native debugging")`
    then `Resolve(settingsService?.Get<string>(DebugAdapterDescriptor.LldbDapSettingsKey, ""))`.
  - `public static string? Resolve(string? configuredPath, Func<string,bool>? fileExists = null, Func<string?>? toolsProbe = null, Func<string?>? pathProbe = null, Func<string?>? knownDirsProbe = null)` —
    chain: override (reuse `LanguageService.ResolveLspPathOverride`) → tools root
    (`lldb-dap*` dirs under `ClangdInstaller.DefaultToolsRoot`'s root, numeric-version
    ranked exactly like `ClangdLocator.FindInToolsRoot` :146-206 — copy the shape, probe
    `bin\lldb-dap.exe` then `lldb-dap.exe`) → `ExecutableLocator.Find("lldb-dap")` →
    `ExecutableLocator.FindInDirectories("lldb-dap", KnownInstallDirectories, ...)` where
    `KnownInstallDirectories` = the ClangdLocator LLVM list (reuse
    `BuildWindowsLlvmInstallDirectories`/the cached list — make it public with a
    keep-in-sync signpost if it is not) **plus `C:\winlibs\mingw64\bin`** (appended in the
    lldb list only; do NOT add winlibs to the clangd list — it would flip nothing today
    but the lists are deliberately separate).
  - File header warning, verbatim: `⚠ NEVER probe lldb-dap by spawning it ("--version"
    parks on stdin and hangs). File-existence checks only, like FindInLlvmInstallDirectories.`
  - Settings schema: `Prop(DebugAdapterDescriptor.LldbDapSettingsKey, SettingsPropertyType.String, "lldb-dap Path", "", ...)` beside the clangd entry.
- [ ] **Step 4:** Green. On THIS machine also assert live once (manual step, not a test):
  `LldbDapLocator.Resolve(null)` from a scratch console or debugger returns the winlibs
  path via the known-dirs leg.
- [ ] **Step 5:** Full suite: +8. **Step 6:** Commit:
  `feat(debug): LldbDapLocator — setting, ~/.vgs/tools, PATH, known dirs incl. winlibs; never spawns`

## Task 8: `DebugAdapterRegistry` + `DebugService` orchestration + DI

**Files:**
- Create: `VisualGameStudio.Core/Abstractions/Services/IDebugAdapterRegistry.cs`,
  `VisualGameStudio.ProjectSystem/Services/DebugAdapterRegistry.cs`
- Modify: `VisualGameStudio.Core/Abstractions/Services/IDebugService.cs`
  (`DebugConfiguration` +`AdapterId`), `VisualGameStudio.ProjectSystem/Services/DebugService.cs`,
  `VisualGameStudio.Shell/Configuration/ServiceConfiguration.cs:98`
- Test: `VisualGameStudio.Tests/Services/DebugAdapterRegistryTests.cs`, extend
  `DebugServiceTests.cs`

- [ ] **Step 1: Write the failing tests:**

```csharp
// DebugAdapterRegistryTests
[Test] public void Register_DuplicateId_Throws()                  // the two-process orphan lesson, ordinal compare
[Test] public void GetFor_PicksTheFirstServingDescriptor_InRegistrationOrder()
[Test] public void GetFor_NullProject_ReturnsNull()
[Test] public void GetById_OrdinalLookup_NullOnMiss()
// DebugServiceTests (fake adapter through the sessionFactory seam)
[Test] public async Task StartDebugging_WithAdapterId_SpawnsTheDescriptorCommand()
    // registry with a test descriptor whose command is ("fake.exe","--x"); sessionFactory
    // records the ProcessStartInfo it was handed -> FileName/Arguments match exactly
[Test] public async Task StartDebugging_NullAdapterId_KeepsTheLegacyManagedPath()
    // no behavior change for every existing caller/test
[Test] public async Task StartDebugging_AdapterNotInstalled_FailsCleanly_NoSpawn()
    // descriptor resolves null -> returns false, output names the adapter, sessionFactory never called
```

- [ ] **Step 2:** FAIL. **Step 3: Implement.**
  - `IDebugAdapterRegistry` (Core): `IReadOnlyList<DebugAdapterDescriptor> All { get; }`,
    `DebugAdapterDescriptor? GetFor(BasicLangProject? project)`,
    `DebugAdapterDescriptor? GetById(string id)`,
    `void Register(DebugAdapterDescriptor descriptor)` — doc: "the public door; built-ins
    and extensions use the same method" (spec §2.3). Registry owns NO processes (unlike
    the LSP registry — DAP sessions are per-launch, owned by DebugService), so no
    IDisposable and no start/stop lifecycle.
  - `DebugAdapterRegistry` (ProjectSystem): `List<DebugAdapterDescriptor>` under a lock;
    `Register` throws `ArgumentException` on duplicate Id (ordinal); `GetFor` = first
    registered whose `Serves(project)`.
  - `DebugConfiguration` gains `public string? AdapterId { get; set; }` — doc: "descriptor
    id from the registry; null = the legacy managed path (back-compat for every existing
    caller)". Routing stays a VM concern (the registry pick happens at the F5 seam,
    Task 9, where the project is in hand); DebugService resolves the id:
    ctor grows `IDebugAdapterRegistry? registry = null` (before `sessionFactory`);
    `StartDebuggingAsync`: `AdapterId != null` → `registry.GetById(...)` →
    `ResolveLaunchCommand()` (null ⇒ output "`<DisplayName>` is not installed…", return
    false, no spawn) → `DapSession.BuildStartInfo(cmd.FileName, cmd.Arguments, config.WorkingDirectory)`
    + the descriptor's `Timeouts` into the session; `AdapterId == null` → today's
    dotnet+compilerPath path, `DapTimeoutProfile.Managed`. Extract the ctor's compiler
    probe (:58-84) into `public static string? ResolveCompilerPath()` so Task 8's DI
    factory can hand it to the managed descriptor.
  - DI (`ServiceConfiguration.cs:98`) — replace the one-liner with the named-factory
    pattern (precedent at :92-97):

```csharp
services.AddSingleton<IDebugAdapterRegistry>(sp =>
{
    var settingsService = sp.GetRequiredService<ISettingsService>();
    var registry = new DebugAdapterRegistry();
    // Built-ins register through the SAME public door an extension would use (spec §2.3).
    // Launch commands resolve at SESSION START (descriptor contract) — an lldb-dap
    // installed mid-session is found on the next F5, no IDE restart (contrast clangd, D1).
    registry.Register(DebugAdapterDescriptor.BasicLangManaged(DebugService.ResolveCompilerPath));
    registry.Register(DebugAdapterDescriptor.LldbDap(() => LldbDapLocator.Locate(settingsService)));
    return registry;
});
services.AddSingleton<IDebugService>(sp => new DebugService(
    sp.GetRequiredService<IOutputService>(),
    sp.GetRequiredService<IDebugAdapterRegistry>()));
```

- [ ] **Step 4:** Green; managed e2e explicit run (null-AdapterId path untouched — prove
  it). **Step 5:** Full suite: +7. **Step 6:** Commit:
  `feat(debug): DebugAdapterRegistry — two built-ins through the public door, DebugService routes by descriptor`

## Task 9: The F5 seam — registry routing replaces the native guard

**Files:**
- Create: `VisualGameStudio.Shell/Services/DebugLaunchPolicy.cs`
- Modify: `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs` (:3477-3488 guard,
  :3557-3564 config construction, ctor + field for `IDebugAdapterRegistry`, the attach
  command ~:3577)
- Test: `VisualGameStudio.Tests/Shell/DebugLaunchPolicyTests.cs`

- [ ] **Step 1: Write the failing tests** (`MainWindowViewModel` is not constructible in
  tests — the decisions live in a pure public policy class, the 3b `ClangdDownloadFlow`
  pattern):

```csharp
[Test] public void ShouldWarnNoDebugInfo_TrueOnlyForNativeAdapterWithDebugSymbolsOff()
    // truth table: (managed, any cfg) false; (lldb, DebugSymbols true) false;
    // (lldb, DebugSymbols false) true; (lldb, null cfg) false
[Test] public void ComposeNoDebugInfoWarning_NamesTheConfiguration()
[Test] public void ComposeAdapterMissingMessage_NamesAdapterAndBothRemedies()
    // exact string: display name + "Tools → Download C++ Debugger" + the cpp.lldbDap.path setting
```

- [ ] **Step 2:** FAIL. **Step 3: Implement** `public static class DebugLaunchPolicy`
  (three small members, exact strings pinned by the tests; the warning text explains
  `/Zi | -g` are emitted only when the configuration's DebugSymbols is on —
  `CppToolchain.FlagsFor`, `CppToolchain.cs:118-140`).
- [ ] **Step 4: Rewire F5.** In `StartDebuggingAsync`, replace :3477-3488 with:

```csharp
var descriptor = _debugAdapterRegistry.GetFor(_projectService.CurrentProject);
if (descriptor is null)
{
    OutputPanel.AppendOutput("No debug adapter serves this project type.\n");
    return;
}
if (descriptor.ResolveLaunchCommand() is null)
{
    ReportDebugAdapterMissing(descriptor);   // Output + status; Task 12 upgrades this to the offer toast
    return;
}
```

  After the build-success checks (:3510-3524), add the Release warning:

```csharp
if (DebugLaunchPolicy.ShouldWarnNoDebugInfo(descriptor, _buildService.CurrentConfiguration))
    OutputPanel.AppendOutput(DebugLaunchPolicy.ComposeNoDebugInfoWarning(
        _buildService.CurrentConfiguration!.Name) + "\n");
```

  In the `DebugConfiguration` construction (:3557-3564) add `AdapterId = descriptor.Id`.
  Everything else stays byte-identical — the `.vgs/launch.json` overlay (:3531-3555)
  already defaults `cwd` to the project directory, matching Ctrl+F5 semantics (:3638-3642),
  which is exactly the spec §4.4 requirement; breakpoints already flow from the path-keyed
  store (:3567) with no extension gate, so `.cpp` (and `.bas`, if the gate passed)
  breakpoints reach the adapter for free. If Task 1 recorded that only the FORWARD-SLASH
  path form binds, normalize breakpoint source paths in `DebugService.SetBreakpointsAsync`
  when the active descriptor is lldb-dap (one `Replace('\\','/')` at the wire, comment
  citing the gate finding). Constructor: inject `IDebugAdapterRegistry` (field + ctor
  param — the VM is DI-constructed, `GetRequiredService` chain handles it). Native ATTACH
  stays out of v1 (spec §9): in the attach command (~:3577), guard
  `CurrentProject?.IsNativeBuild == true` → output "Native attach is out of scope for
  v1 — launch (F5) instead." and return.
- [ ] **Step 5:** Build Shell (no AXAML touched — no clean needed). **LIVE SMOKE (the
  first native F5 ever):** run `IDE/VisualGameStudio.exe`? NO — IDE/ binaries are stale;
  run the fresh build
  (`VisualGameStudio.Shell/bin/Release/net8.0/VisualGameStudio.exe`). Open a native
  project, set a `.cpp`-visible breakpoint (or `.bas` if the gate passed), F5: build →
  lldb-dap found via winlibs → breakpoint hits → step → stop. Verify the
  debug-start pane activation rides the guarded activation path — layout surprises
  degrade to a logged no-op, never a crash (the `EnsureMainArea` lesson, spec §8). Then a
  managed project F5: unchanged. Record all three in the task report.
- [ ] **Step 6:** Full suite: +3. **Step 7:** Commit:
  `feat(debug): F5 routes native projects to lldb-dap — the "later phase" guard retires`

## Task 10: Adapter-driven Exception Settings

**Files:**
- Create: `VisualGameStudio.Shell/Services/ExceptionFilterTranslator.cs`
- Modify: `VisualGameStudio.Core/Abstractions/Services/IDebugService.cs`
  (+`ActiveExceptionFilters`), `VisualGameStudio.ProjectSystem/Services/DebugService.cs`,
  `VisualGameStudio.Core/Abstractions/Services/IDialogService.cs:13`,
  `VisualGameStudio.Shell/Services/DialogService.cs:267`,
  `VisualGameStudio.Shell/ViewModels/Dialogs/ExceptionSettingsViewModel.cs`,
  `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs:3905-3971`
- Test: `VisualGameStudio.Tests/Shell/ExceptionFilterTranslatorTests.cs`

- [ ] **Step 1: Write the failing tests:**

```csharp
[Test] public void Translate_ManagedVocabulary_MatchesTheLegacyMappingExactly()
    // table-drive the moved :3914-3962 logic: "All Exceptions"+BreakWhenThrown -> ["all"];
    // category rows + BreakWhenUserUnhandled -> ["uncaught"]; individual type ->
    // ["thrown"] + option {FilterId:"thrown", Condition:<type>}; unhandled-only individual
    // type -> option {FilterId:"uncaught", Condition:<type>}
[Test] public void Translate_AdapterFilters_MapCheckedRowsToFilterIds()
    // available = cpp_throw/cpp_catch; row "C++ Throw" checked -> (["cpp_throw"], null)
[Test] public void Translate_NothingChecked_SendsEmptyFilters()   // clears server-side state
[Test] public void ActiveFilters_PreferAdvertised_FallBackToDescriptorDefaults()
    // DebugService over the fake: caps advertise cpp_throw only -> [cpp_throw];
    // caps advertise none -> the active descriptor's FallbackExceptionFilters (spec §2.5)
[Test] public void DialogVm_BuildsCategoriesFromAdapterFilters()
    // ExceptionSettingsViewModel(new[]{cpp_throw, cpp_catch}) -> exactly two categories,
    // labels from the filters, no "User Exceptions" add-row
```

- [ ] **Step 2:** FAIL. **Step 3: Implement.**
  - `IDebugService.ActiveExceptionFilters` (`IReadOnlyList<DapExceptionFilter>`):
    `Capabilities?.ExceptionBreakpointFilters` when non-empty, else the active
    descriptor's `FallbackExceptionFilters`, else the managed fallback set (no session).
  - `ExceptionFilterTranslator.Translate(IReadOnlyList<ExceptionSettingResult> results,
    IReadOnlyList<DapExceptionFilter> available)` → `(List<string> Filters,
    List<ExceptionFilterOption>? Options)`. Two modes: available contains any of
    all/uncaught/thrown ⇒ the legacy mapping MOVED VERBATIM out of
    `MainWindowViewModel.cs:3914-3962` (this is a refactor-with-tests, not new logic);
    otherwise ⇒ each result row whose `ExceptionType` matches a filter's Label or Id with
    `BreakWhenThrown` contributes that filter's Id, options null.
  - `ExceptionSettingsViewModel`: new ctor overload
    `(IEnumerable<ExceptionSetting>? currentSettings, IReadOnlyList<DapExceptionFilter> adapterFilters)`
    — when the filter-id set is NOT the classic managed trio, categories are built from
    the filters (label, checked = current setting else `filter.Default`) and the
    User-Exceptions add/remove affordance is hidden; classic set ⇒ existing hardcoded
    categories byte-identical (:44-80 untouched path).
  - `IDialogService.ShowExceptionSettingsDialogAsync` gains an optional
    `IReadOnlyList<DapExceptionFilter>? adapterFilters = null` parameter; `DialogService`
    passes it through; `MainWindowViewModel.ShowExceptionSettingsAsync` passes
    `_debugService.ActiveExceptionFilters` and replaces :3914-3962 with one
    `ExceptionFilterTranslator.Translate(result, _debugService.ActiveExceptionFilters)`
    call feeding the existing `SetExceptionBreakpointsAsync` (:3966-3968).
- [ ] **Step 4:** Green. `dotnet clean` + rebuild Shell IF `ExceptionSettingsDialog.axaml`
  needed edits (only if hiding the add-row required XAML — prefer an `IsVisible` binding).
- [ ] **Step 5:** Full suite: +5. **Step 6:** Commit:
  `feat(debug): Exception Settings renders the adapter's advertised filters — cpp_throw/cpp_catch for free`

## Task 11: Delete the two dead DAP stacks (5 files)

**Files:**
- Delete: `VisualGameStudio.ProjectSystem/DAP/DapClient.cs`,
  `VisualGameStudio.ProjectSystem/DAP/DapClientManager.cs`,
  `VisualGameStudio.ProjectSystem/Services/DapClientService.cs`,
  `VisualGameStudio.Core/Abstractions/Services/IDapClientService.cs`,
  `VisualGameStudio.Tests/Services/DapClientServiceTests.cs`

Deleted only NOW because `DapClient.cs:388-394` was the in-repo reference for the correct
handshake ordering until Task 4 landed it in `DapSession`. Neither stack is DI-registered;
each re-ships a transport trap the live code fixed (chars-not-bytes framing :259-263;
unpinned stdin encoding :105) — the 3a `LspClientManager` verdict repeats: do NOT revive.

- [ ] **Step 1:** Confirm Task 4 is merged (the handshake lives in
  `DapSession.InitializeAndLaunchAsync`). Delete the five files (`git rm`).
- [ ] **Step 2:** ⚠ `VisualGameStudio.Core/DAP/IDapClient.cs` STAYS — it is not on the
  spec's deletion list and `Tests/Infrastructure/InfrastructureTests.cs:15-17` constructs
  its POCOs (`DapStackFrame`/`Breakpoint`/`Variable`). It is now a models-only orphan with
  a competing `StoppedEventArgs`/`SourceBreakpoint` — file a follow-up chip for its
  cleanup at execution time; do not widen this task.
- [ ] **Step 3:** Reference grep:
  `Select-String -Pattern 'DapClientService|DapClientManager|IDapClientService|ProjectSystem\.DAP'`
  across all five project dirs → zero hits outside git history.
- [ ] **Step 4:** Build the Shell + Tests projects — clean. Full suite: count DROPS by the
  deleted `DapClientServiceTests` fixture's tests — record the new baseline number in the
  task report; still 1 flake / 1 skip.
- [ ] **Step 5:** Commit: `chore(debug): delete the two dead DAP stacks — DapSession is the one true transport`

## Task 12: `LldbDapInstaller` + download UX (placeholder pins)

**Files:**
- Create: `VisualGameStudio.ProjectSystem/Services/LldbDapInstaller.cs`,
  `VisualGameStudio.Shell/Services/LldbDapDownloadFlow.cs`
- Modify: `VisualGameStudio.Shell/Configuration/ServiceConfiguration.cs` (installer
  singleton beside :74's clangd one), `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs`
  (flow wiring beside :433-438, command beside :1227-1228, missing-adapter toast),
  `VisualGameStudio.Shell/Views/MainWindow.axaml` (Tools menu, beside :296-297)
- Test: `VisualGameStudio.Tests/Services/LldbDapInstallerTests.cs`,
  `VisualGameStudio.Tests/Shell/LldbDapDownloadFlowTests.cs`

- [ ] **Step 1: Write the failing installer tests** — structural copies of
  `ClangdInstallerTests` (in-test zip fixtures, injectable tools root + downloader
  delegate, NEVER live HTTP, lazy root creation asserted, TearDown bounded-retry delete):

```csharp
[Test] public async Task Install_ProducesTheProbeLayout()
    // fixture zip root lldb-dap_22.1.0/bin/{lldb-dap.exe, liblldb.dll, lldb-argdumper.exe}
    // -> <root>/lldb-dap_22.1.0/bin/lldb-dap.exe exists AND the dlls survived (self-contained is the point)
[Test] public async Task ShaMismatch_RejectsBeforeExtraction()
[Test] public async Task ExistingSameVersionDir_IsReplaced()
[Test] public async Task StagingDir_NeverLeaksOnFailure()
[Test] public async Task TruncatedDownload_SizeMismatchRejectsBeforeHashing()
    // downloader delegate writes half the fixture bytes -> the verify-size step fails
    // (cheap check first), no SHA attempt, no staging dir, temp zip deleted
[Test] public async Task ToolsRoot_IsNotCreatedAtConstruction()
[Test] public async Task SecondConcurrentInstall_IsCoalesced()
[Test] public void ReleasePins_MatchTheRunbookOnceFilled()
    // if (!LldbDapInstaller.IsReleasePinned) Assert.Ignore("zip not published — runbook pending");
    // else download+hash the real asset (the ClangdInstaller PinnedConstants test shape)
```

- [ ] **Step 2:** FAIL. **Step 3: Implement the installer** — mirror `ClangdInstaller`
  member-for-member (result record, download delegate sibling, closed FailureStep
  vocabulary, single-flight `Wait(0)`, size→SHA-256 streamed verify, whole-zip staged
  extract, dir swap, finally-cleanup; toolsRoot defaults from
  `ClangdInstaller.DefaultToolsRoot` — single-sourced root, the installer/probe drift
  guard). Pins:

```csharp
// ⚠ PLACEHOLDER PINS — the self-hosted zip is a release-time deliverable
// (docs/superpowers/specs/2026-07-19-lldb-dap-zip-release-runbook.md, Task 13).
// Fill DownloadUrl/ExpectedSha256/ExpectedSizeBytes/InstalledDirName from the runbook's
// measured values; IsReleasePinned gates the download UX until then.
public const string DownloadUrl =
    "https://github.com/gracepriest/VisualGameStudioEngine/releases/download/lldb-dap-22.1.0/lldb-dap-windows-22.1.0.zip";
public const string ExpectedSha256 = "REPLACE-AT-RELEASE-TIME";
public const long ExpectedSizeBytes = 0;
public const string InstalledDirName = "lldb-dap_22.1.0";
public static bool IsReleasePinned =>
    !ExpectedSha256.StartsWith("REPLACE", StringComparison.Ordinal);
```

- [ ] **Step 4: Write the failing flow tests + implement `LldbDapDownloadFlow`** — mirror
  `ClangdDownloadFlow` (sinks injected, never throws, `FormatProgress`, single-flight
  toast id `"lldb-dap-download"`), with two deliberate differences, both pinned by tests:
  (a) `WhenNotReleasePinned_ReportsNotYetPublished_AndDoesNotDownload()` — info toast
  naming the runbook; (b) success toast says **"lldb-dap installed — press F5 to debug"**
  (NO restart language: the locator runs at session start, unlike clangd's DI-time
  resolution). Note in the flow's doc comment: the spec's "download → F5 resumes" is
  DELIBERATELY implemented as "available on the next F5" — the 3b pattern; no restart
  needed, and no mid-flow F5 resumption is attempted.
  Also `WhenAlreadyResolved_ReportsPathAndDoesNotDownload()`,
  `FailedInstall_NamesTheFailingStep()`, `ProgressTuples_BecomeFractions()`,
  `ConcurrentTrigger_ReportsAlreadyInProgress()`.
- [ ] **Step 5: Shell wiring.** DI: `services.AddSingleton(sp => new LldbDapInstaller());`
  beside :74. VM: construct the flow beside the clangd one (:433-438 shape) with
  `resolveExisting: () => LldbDapLocator.Locate(_settingsService)`;
  `[RelayCommand] private void DownloadCppDebugger() => _ = _lldbDapDownloadFlow.RunAsync();`;
  upgrade Task 9's `ReportDebugAdapterMissing` to the ACTIONS toast overload with
  `[Download C++ Debugger]` → `RunAsync()` (once-per-session flag
  `_lldbDapMissingReported`, mirroring `_clangdMissingReported` :1231). Menu
  (`MainWindow.axaml`, under `_Tools` beside :296-297):

```xml
<MenuItem Header="Download C++ _Debugger..." Command="{Binding DownloadCppDebuggerCommand}"
          ToolTip.Tip="Download lldb-dap for native C++ debugging (available on the next F5)"/>
```

- [ ] **Step 6:** `dotnet clean` (AXAML changed) + build Shell. Green fixtures. Full
  suite: +14. **Step 7:** Commit:
  `feat(debug): one-click lldb-dap acquisition — installer with placeholder pins, offer toast, Tools menu`

## Task 13: The zip release runbook (doc-only)

**Files:**
- Create: `docs/superpowers/specs/2026-07-19-lldb-dap-zip-release-runbook.md`

The zip build itself is NOT a task in this plan (spec §5: one-time build-pipeline
deliverable, off the critical path — winlibs 19.1.7 unblocks all dev/e2e today).

- [ ] **Step 1: Author the runbook** with these sections, concrete enough that a fresh
  agent can execute it without this plan:
  1. **Source + toolchain:** LLVM 22.x release source; build only lldb-dap
     (`ninja lldb-dap`); CMake: `-DLLVM_ENABLE_PROJECTS="clang;lldb"
     -DLLDB_ENABLE_PYTHON=OFF -DLLDB_ENABLE_LUA=OFF -DLLDB_ENABLE_LIBEDIT=OFF
     -DCMAKE_BUILD_TYPE=Release` — `LLDB_ENABLE_PYTHON=OFF` kills the official-installer
     trap (liblldb linked against an unshipped python DLL — LLVM issues
     #85764/#58095/#74073, the 434 MB installer is broken on clean machines).
  2. **Zip layout (the locator's contract):**
     `lldb-dap_<version>/bin/{lldb-dap.exe, liblldb.dll, lldb-argdumper.exe}` — matches
     `LldbDapLocator`'s tools probe and `LldbDapInstaller.InstalledDirName`.
  3. **Fresh-VM acceptance (the gate):** clean Windows VM, no LLVM, no python — unzip
     alone must debug a real MSVC `/Zi` executable end-to-end (bind → stop → step →
     locals) over raw DAP; `lldb-dap.exe` must not demand any DLL outside the zip.
     ⚠ never probe with `--version` (stdin hang).
  4. **Measure + publish:** `Get-FileHash -Algorithm SHA256`, exact byte size, upload as
     a GitHub release asset on the VGS repo; then fill the four
     `LldbDapInstaller` constants — `ReleasePins_MatchTheRunbookOnceFilled` activates
     itself. Estimated 40–80 MB; Apache-2.0 + LLVM-exception, redistribution-clean.
  5. **Version policy:** ship 22.x (mature native-PDB era). The dev machine's winlibs
     19.1.7 is the development debugger only; any MSVC/PDB quirk on 19 is re-checked on
     22 before being filed as ours.

  Note in the runbook's header: a tracking chip is filed at phase end (Task 15) for
  executing this runbook and filling the four `LldbDapInstaller` placeholder pins —
  `IsReleasePinned == false` must not linger past the release.
- [ ] **Step 2:** Full suite (unchanged counts — uniform gate). **Step 3:** Commit:
  `docs(debug): lldb-dap zip release runbook — build, layout, fresh-VM acceptance, pin fill-in`

## Task 14: Native e2e ring — the real thing

**Files:**
- Create: `VisualGameStudio.Tests/Integration/NativeDebugE2ETests.cs`

`IdeInAngerTests.cs:427-526` is the template (TCS-per-event with
`RunContinuationsAsynchronously`, 90 s start / 60 s stop / 30 s request budgets,
`AssertProcessExits`, kill-tree finally, `output.Dump()` on every assert). Duplicate the
private helpers into this fixture (suite convention). Gates: `CppToolchain.Find()` null →
Ignore; `LldbDapLocator.Resolve(null)` null → Ignore — on THIS machine both resolve, so
**a skip here is a task failure**.

- [ ] **Step 1: The fixture project** (temp-dir, ClangdE2ETests XML shape —
  `<Language>Cpp</Language>` + `<TargetBackend>Cpp</TargetBackend>`, sources discovered):
  `Logic.bas` (the `CalculateScore` shape from `ClangdE2ETests.cs:96-106`) + `main.cpp`
  with pinned lines (comment: "line numbers are load-bearing"):

```cpp
#include "Logic.g.h"
#include <stdexcept>

static int triple(int x) {
    int local = x * 3;                       // line 5  <- breakpoint A
    return local;                            // line 6  <- step-over lands here
}

int main() {
    int score = CalculateScore(5);           // line 10
    int t = triple(score);                   // line 11
    if (t == 150) {
        throw std::runtime_error("boom");    // line 13 <- cpp_throw target
    }
    return 0;
}
```

- [ ] **Step 2: The main e2e**
  `NativeDebug_CppBreakpoint_Steps_Locals_Watch_CppThrow_CleanShutdown` — the FULL product
  path, no raw driver: `new DebugService(new RecordingOutputService(), registry)` where
  the registry is built exactly as `ServiceConfiguration` builds it (managed + lldb-dap
  descriptors, locator with null settings); build via `BuildService.BuildProjectAsync`
  (Debug); `StartDebuggingAsync(config with AdapterId = LldbDapId, breakpoints = { [main.cpp] = [{Line=5}] })`.
  Assert, in order:
  1. start returns true; `Stopped` fires ≤60 s, `Reason == Breakpoint`;
     `BreakpointsChanged` delivered a `Verified == true` breakpoint at line 5 (the
     verified/hollow round-trip, `BreakpointsViewModel.cs:61-123` model);
  2. `GetStackTraceAsync(stopped.ThreadId)` top frame ends `main.cpp`, `Line == 5`;
  3. `StepOverAsync` → stopped `Reason == Step` at line 6 (real threadId on the wire —
     the fake-ring proved the args; this proves the adapter honors them);
  4. scopes + variables for the top frame contain `local` and `x` (real C++ names — no
     demangling expectations, locals-fidelity is a non-goal);
  5. `EvaluateAsync("x", frameId)` returns `"50"`;
  6. `SetExceptionBreakpointsAsync(new[] { "cpp_throw" })` → `ContinueAsync` → stopped
     `Reason == Exception` (the throw at line 13);
  7. `StopDebuggingAsync` (disconnect `terminateDebuggee:true`) → `AdapterProcessId`
     exits ≤10 s AND `Process.GetProcessesByName` for the debuggee exe name is empty —
     zero orphans;
  8. finally: kill-tree both PIDs regardless.
- [ ] **Step 3 (only if Task 1's verdict was PASS): the `.bas` test**
  `NativeDebug_BasBreakpoint_BindsStopsAndSteps` — same fixture, breakpoint on the
  `Return hits * 10` line of `Logic.bas` (use the path form Task 1 recorded), assert
  bind-verified → stop with the top frame mapped to `Logic.bas` at that line → step-next
  stays in `.bas` coordinates. If the verdict was FAIL, this step was descoped at the
  checkpoint — skip it and say so in the task report.
- [ ] **Step 4: The DWARF route** (spec §6 rows 2-3 claim "supported; unit/pairing-tested"
  — this is the live half of that claim) —
  `NativeDebug_DwarfRoute_GppBuild_BreakpointStepAndLocals`: compile a small standalone
  `dwarf.cpp` (the `triple` shape above, self-contained `main`, no generated headers)
  DIRECTLY with `C:\winlibs\mingw64\bin\g++.exe -g -O0 -o dwarf.exe dwarf.cpp` (spawned
  by the test, 60 s wait — this deliberately bypasses the product build path, which would
  probe MSVC; the claim under test is the DEBUG route: registry → lldb-dap descriptor →
  `DapSession` reading DWARF). Then the product session path exactly as Step 2:
  `StartDebuggingAsync(config with AdapterId = LldbDapId, Program = dwarf.exe)` →
  breakpoint binds → stops → step-over → locals contain `local`/`x`. Guard:
  `File.Exists(@"C:\winlibs\mingw64\bin\g++.exe")` false → `Assert.Ignore` (clean
  machines skip; on THIS machine winlibs g++ 14.2 exists, so it runs live — a skip here
  is a task failure).
- [ ] **Step 5:** Run the fixture TWICE (stability):
  `dotnet test ... --filter "FullyQualifiedName~NativeDebugE2ETests"` — green both runs.
  Known-load caveat: the managed debug e2e has flaked under heavy external machine load —
  isolation reruns are the arbiter.
- [ ] **Step 6:** Full suite: +2 or +3. **Step 7:** Commit:
  `test(debug): native e2e — real MSVC build, real lldb-dap, breakpoint→step→locals→watch→cpp_throw→no orphans`

## Task 15: Definition of Done — greps, suite, user smoke

**Files:** none (verification + checklist)

- [ ] **Step 1: DoD greps** (product dirs = Core/ProjectSystem/Shell/Editor, exclude
  Tests, bin, obj):
  - `\bthreadId = 1` (word boundary; `_currentThreadId = 1` is legitimate) → zero.
    `GetStackTraceAsync\(int threadId = 1\)` → zero.
  - `DapClientService|DapClientManager|IDapClientService|ProjectSystem\.DAP` → zero
    (Core/DAP/IDapClient.cs itself is the only surviving Core.DAP file, chip filed).
  - Rebuild the Task 0 temp project in Debug; in `obj\gen\*.g.cpp`:
    `#line \d+ "C:/` present, `#line \d+ ".*\\` absent. Rebuild with a Release
    configuration: zero `#line` anywhere.
  - `lldb-dap.*--version|--version.*lldb-dap` → zero (the stdin-hang hazard never
    shipped).
  - `cpp.lldbDap.path` appears in exactly: the descriptor const, the locator consumer
    registration, the settings schema Prop.
- [ ] **Step 2: Both entry points.** CLI: `dotnet BasicLang\bin\Release\net8.0\BasicLang.dll build <native>.blproj`
  green. IDE: covered live by the smoke below (F5 builds through the same
  `CompileProjectFiles`/`CppProjectBuilder` engine).
- [ ] **Step 3:** Final full suite (detached, poll): record the closing count — expected
  ≈ 3250 + ~75 new − the Task 11 deletions, same 1 known flake + 1 known skip only.
- [ ] **Step 4: USER IDE SMOKE (do not skip — every prior phase caught real defects only
  here).** Hand the user this checklist against the freshly built Shell:
  1. Open a native project, breakpoint in a `.cpp` (+ a `.bas` one if the gate passed),
     F5 → builds, stops on the breakpoint, yellow arrow on the right line.
  2. Step over / into / out; Call Stack, Variables, Watch populate; a watch on a local
     evaluates. Threads pane marks the current thread; switching threads refreshes the
     stack (real threadIds, live).
  3. Debug → Exception Settings during the native session → shows "C++ Throw"/"C++ Catch"
     (adapter-driven); enable C++ Throw → a thrown C++ exception breaks.
  4. Stop Debugging → Task Manager shows no orphan `lldb-dap.exe` or game exe.
  5. Switch to Release configuration, F5 → Output warns "no debug info… breakpoints will
     not bind", program still launches.
  6. Ctrl+F5 native run: unchanged. Managed project F5: breakpoints/step/locals exactly
     as before Phase 4 (the regression that matters most).
  7. Tools → Download C++ Debugger… → the "not yet published — runbook pending" info
     toast (placeholder pins; flips to the real flow at release time).
  8. Temporarily set `cpp.lldbDap.path` to a bogus path in settings → F5 still works
     (chain falls through to winlibs); clear it after.
- [ ] **Step 5: File the phase-end chips:** (a) execute the zip release runbook + fill
  the four `LldbDapInstaller` placeholder pins (`IsReleasePinned` must not linger);
  (b) `Core/DAP/IDapClient.cs` models-only orphan cleanup (Task 11's finding);
  (c) retry the Step-0 gate against lldb-dap 22 IF Task 1's verdict was FAIL.
- [ ] **Step 6:** superpowers:finishing-a-development-branch — merge decision, IDE/
  prebuilt-binaries refresh decision, MEMORY topic-file update.

## Non-goals (spec §9 — this plan adds nothing to the list)

Native attach (guarded with a message, chip-worthy later); renaming generated locals to
BasicLang names in Variables; gdb/vsdbg built-ins; multi-session debugging; MSIL/LLVM
backends (standing decision); `terminate`/`restart`/`setVariable`/`source`/`modules` DAP
requests; sending `DebugConfiguration.Environment` to the adapter (never sent today,
unchanged); the actual zip build (runbook executes at release time);
`Core/DAP/IDapClient.cs` cleanup (chip).
