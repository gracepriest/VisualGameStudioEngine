# C++ clangd Phase 3b Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents
> available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`)
> syntax for tracking.

**Goal:** Ship the five deferred Phase 3b capabilities — one-click clangd download, the grown
locator probe chain, semantic-token legend negotiation, debounced regen on `.bas` save +
`.blproj` watching, and capability-gated `completionItem/resolve` — plus the Task 0 hygiene fix.

**Architecture:** Everything extends the shipped 3a substrate (`LanguageServiceRegistry` routing,
`LanguageService` transport, `IntelliSenseEmissionService` emission, `ClangdLocator` discovery).
No roster mutation: a downloaded clangd activates on IDE restart (user decision). New units:
`TrailingEdgeDebouncer` (Core), `FileDownloader` + `ClangdInstaller` + `RegenOnSaveCoordinator`
(ProjectSystem), `SemanticTokenLegendMap` (Core), and thin Shell wiring.

**Tech Stack:** C# / .NET 8, NUnit (~3,138 tests), Avalonia Shell, real clangd 22.1.6 for
conditional integration tests.

**Spec:** `docs/superpowers/specs/2026-07-17-cpp-clangd-phase3b-design.md` (approved; reviewer
verified 26/27 citations). Rationale lives THERE — this plan is tasks. Where this plan and the
spec disagree, this plan's MEASURED Step 0 results win (they postdate the spec).

---

## Measured Step 0 results (empirical, 2026-07-17 — cite these, do not re-derive)

**S0.1 — Release pin (verified by download + hash):**
- URL: `https://github.com/clangd/clangd/releases/download/22.1.6/clangd-windows-22.1.6.zip`
- Size: exactly **28,198,778 bytes**. SHA-256:
  `CE54F16E0B4FD76D450EEDA9664420B195360B73FEBCFE40E661108FA57F2CE1`
  (matches GitHub's per-asset API digest; no `.sha256` asset exists).
- Archive root is **already** `clangd_22.1.6/` containing `bin/clangd.exe` (50,005,504 bytes,
  byte-identical to the installed ground truth) + `LICENSE.TXT` + `lib/clang/22/include/`
  (~340 builtin headers — **REQUIRED for clangd's include resolution; extract the whole
  archive, never just the exe**). No rename step needed.

**S0.2 — clangd 22.1.6 semantic-token legend (captured from a real initialize reply; raw
artifacts in the session scratchpad `clangd-legend-probe/`):**
- `legend.tokenTypes` has **25 entries WITH DUPLICATE NAMES** (clangd's internal
  HighlightingKind order): 0=variable, 1=variable, 2=parameter, 3=function, 4=method,
  5=function, 6=property, 7=variable, 8=class, 9=interface, 10=enum, 11=enumMember, 12=type,
  13=type, 14=unknown, 15=namespace, 16=typeParameter, 17=concept, 18=type, 19=macro,
  20=modifier, 21=operator, 22=bracket, 23=label, 24=comment. ⚠ A naive
  name-keyed `Dictionary.Add` THROWS on this legend — construction must be duplicate-tolerant
  (duplicates map to the same canonical slot, so first-wins/last-wins are equivalent).
- "clangd's 0 is variable" — TRUE. **No clangd index coincides with its BasicLang index** —
  identity passthrough miscolors everything.
- `legend.tokenModifiers` has 18 bits; the overlapping ones SHUFFLE: clangd→BasicLang
  0→0 declaration, 1→1 definition, **2→4 deprecated, 4→2 readonly, 5→3 static, 6→5 abstract**,
  9→9 defaultLibrary. All other clangd bits (deduced, virtual, dependentName, bits 10–17) have
  no BasicLang slot → masked out. ⚠ Without the bit remap, clangd's readonly (bit 4) hits the
  highlighter's Deprecated test → **every `const` renders struck-through**.
- No-BasicLang-slot type names (render uncolored, per spec): unknown(14), concept(17),
  macro(19), bracket(22), label(23). Keep macro uncolored (spec-pure); an alias is a recorded
  possible follow-up, not 3b.
- `full: { delta: true }`, **`range: false`** — never send `semanticTokens/range`.
- clangd stderr: the top-level `offsetEncoding` extension is **deprecated, removed in
  clangd 23**. No action: `DescribeEncodingMismatch` already treats an absent field as
  "never said", so clangd 23 will pass the handshake. Do not add the legacy client capability.

**S0.3 — clangd 22.1.6 `completionProvider.resolveProvider` is `false` (measured).** clangd
does NOT implement `completionItem/resolve`; docs arrive inline on items. The resolve feature's
live beneficiary is **BasicLang** (its real `--lsp` server has `ResolveProvider = true` with a
working lazy-docs handler — `BasicLang/LSP/CompletionHandler.cs:41-87`). clangd gates OFF
cleanly via `HasCompletionResolveProvider == false`. The spec's "both languages benefit"
sentence is amended alongside this plan.

**S0.4 — LLVM install dirs (this machine):** ALL candidates absent (Program Files\LLVM, scoop,
MSYS2 variants, and VS 2022 Enterprise's optional `VC\Tools\Llvm` — not installed here). ⇒ the
LLVM-dir probe ships with **pure injectable-seam tests only**; no local integration target
exists. The DI-exactness pin test auto-follows the chain because it calls production
`ResolveClangdPath` (spec-reviewer-verified).

**S0.5 — `workspace/didChangeWatchedFiles`: MEASURED — does NOT heal.** clangd 22.1.6 accepts
the notification (stderr logs receipt, no error) and does NOTHING with it — no CDB load, no
re-parse, for both Created-late and Changed-existing databases; run twice per variant with
passing controls (didChange healed in ~0.2s every time; db-present-from-start fixture clean).
clangd never sends `client/registerCapability` for file watches; its initialize reply declares
`compilationDatabase: {automaticReload: true}` — "I re-stat the CDB myself on my next build",
and only a `didChange` on the open file triggers that build. **Task 11's gate resolves to the
NO-NUDGE branch.** Two hazards to pin in comments: (1) the notification is accepted SILENTLY —
code that sends it gets false confidence, do not re-add it expecting CDB reload; (2) after a
healing edit, the FIRST publishDiagnostics still carries stale errors for ~60-150ms (preamble
rebuild in flight) — never latch the first publish after an edit.

## Environment rules (violating these costs real time — all verified)

- PowerShell 5.1. NEVER `Get-Content`/`Set-Content` on repo files (BOM-less UTF-8 mojibake).
  Use Read/Edit/Write/Grep/Glob. Multi-line commit messages: Write a file + `git commit -F`.
- Full-suite output exceeds the 30k tool truncation and eats the summary line — redirect to a
  file, read the tail, run foreground with timeout 600000. Baseline at branch point:
  **3136 passed / 1 known env flake** (`CppTemplate...("cpp-game-app")` BL6009 engine .lib) **/
  1 known skip** (toolchain-conditional). `dotnet test` exits 1 from the flake — NORMAL.
- clangd 22.1.6 at `%USERPROFILE%\.vgs\tools\clangd_22.1.6\bin\clangd.exe`, deliberately NOT on
  PATH. Never install LLVM or put clang++ on PATH (flips `CppToolchain.Find()` MSVC→ClangLike,
  makes the D2 pin vacuous). This machine HAS MSVC — tests needing no-toolchain pass
  `toolchain: null` explicitly.
- `dotnet clean` before build after AXAML changes.
- **Never `Does.Contain` for bytes reaching a process or wire** — parse, assert exact tokens.
- CS8604 is NoWarn in all four IDE projects — `string?` into `string` compiles SILENTLY; check
  null-flow by reading.
- `EventAggregator.Subscribe` stores a WeakReference — subscribers MUST hold the returned
  subscription strongly or they are silently GC'd.
- The user-level `~/.vgs` is sacred in tests: every new service takes an INJECTABLE root.
- ⚠ Tests must NEVER hit the live GitHub URL. `FileDownloader` tests use a local
  `HttpListener`; `ClangdInstaller` tests use a local zip fixture.

## File structure (decomposition locked here)

| Unit | File | Responsibility |
|---|---|---|
| Task 0 | `VisualGameStudio.Shell/App.axaml.cs` | delete the `MainWindow.Opened` dev leftover (:133-162) |
| Debouncer | `VisualGameStudio.Core/Utilities/TrailingEdgeDebouncer.cs` (new) | one debounce mechanic, shared |
| Downloader | `VisualGameStudio.ProjectSystem/Services/FileDownloader.cs` (new) | URL → file, progress, deadline, `.partial` hygiene |
| Installer | `VisualGameStudio.ProjectSystem/Services/ClangdInstaller.cs` (new) | pinned download → SHA verify → staged extract → `~/.vgs/tools/clangd_22.1.6/` |
| Locator | `VisualGameStudio.Core/Utilities/ExecutableLocator.cs` + `VisualGameStudio.ProjectSystem/Services/ClangdLocator.cs` | `FindInDirectories` + the 4-link chain |
| Legend | `VisualGameStudio.Core/Utilities/SemanticTokenLegendMap.cs` (new) + `VisualGameStudio.ProjectSystem/Services/LanguageService.cs` (capture) | legend capture + duplicate-tolerant remap tables + data rewrite |
| Regen | `VisualGameStudio.ProjectSystem/Services/RegenOnSaveCoordinator.cs` (new) | FileSavedEvent/.blproj → debounced RequestEmit |
| Resolve | `VisualGameStudio.Core/Abstractions/Services/ILanguageService.cs` (+`Data`, legend types) + `LanguageService.cs` (+`ResolveCompletionAsync`) | model + client call |
| Shell wiring | `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs`, `Views/MainWindow.axaml`, `Views/Documents/CodeEditorDocumentView.axaml.cs`, `VisualGameStudio.Editor/Completion/*` | toast action, Tools menu, remap seam wiring, resolve UI hook, kind map |
| Test discovery | `VisualGameStudio.Tests/LSP/ClangdTestDiscovery.cs` (new) | the shared probe (3rd-consumer trigger fired) |

Tasks are ordered so each lands green independently: 0 (hygiene) → 1 (debouncer) → 2
(downloader) → 3 (installer) → 4 (locator chain) → 5 (test-discovery dedup) → 6 (download UX)
→ 7 (legend capture) → 8 (legend map) → 9 (remap wiring) → 10 (regen coordinator) → 11
(.blproj watch + nudge gate) → 12 (resolve model+client) → 13 (resolve UI + kinds) → 14 (e2e
+ DoD). Suite count grows every task; run the FULL suite before every commit (3a discipline).

---

## Task 0: Delete the startup dev leftover

**Files:** Modify: `VisualGameStudio.Shell/App.axaml.cs:133-162`

- [ ] **Step 1:** Read the block. It is the whole `MainWindow.Opened` handler auto-opening
  `C:\Users\melvi\Documents\TestProject\TestProject.blproj` and auto-adding three breakpoints
  (:151-153). Delete the entire handler registration and its lambda; leave the surrounding
  `OnFrameworkInitializationCompleted` intact (the settings load at `:185-196` and shutdown
  wiring at `:120-131` are NOT part of the leftover).
- [ ] **Step 2:** `dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release`
  — clean. Grep `TestProject.blproj` in Shell → zero hits.
- [ ] **Step 3:** Full suite (redirect, tail): 3136/1-flake/1-skip unchanged.
- [ ] **Step 4:** Commit: `chore(shell): remove hardcoded TestProject auto-open dev leftover`

## Task 1: `TrailingEdgeDebouncer` (shared helper)

**Files:** Create: `VisualGameStudio.Core/Utilities/TrailingEdgeDebouncer.cs`;
Test: `VisualGameStudio.Tests/Core/TrailingEdgeDebouncerTests.cs`

The `SettingsService.ScheduleSave` mechanics (`SettingsService.cs:941-986`) extracted: CTS +
`Task.Delay` + cancel-restart; the trailing edge always fires. NOT the
`FileWatcherService.IsDebounced` throttle (drops the trailing edge). No flush-on-dispose (the
regen consumer must not hold shutdown); `Dispose` cancels the pending fire.

- [ ] **Step 1: Write the failing tests** (async-friendly, no wall-clock flakiness — take the
  delay as a `TimeSpan` and use short values ~50ms with generous poll bounds):

```csharp
[Test] public async Task Signal_FiresOnceAfterTheQuietPeriod()
[Test] public async Task Burst_CoalescesToOneTrailingFire()          // 5 signals 10ms apart -> exactly 1 fire, after the LAST
[Test] public async Task SignalAfterAFire_ArmsAgain()                // debounce is reusable
[Test] public async Task Dispose_CancelsThePendingFire()             // no fire after dispose
[Test] public void Callback_RunsOffTheCallersThread()                 // fire happens on the thread pool (Task.Run), never inline in Signal()
```

  Shape: `sealed class TrailingEdgeDebouncer : IDisposable` with
  `TrailingEdgeDebouncer(TimeSpan quietPeriod, Action fire)`, `void Signal()`, thread-safe via
  a small lock; each `Signal` cancels the previous CTS and schedules
  `Task.Run(async () => { await Task.Delay(quiet, token); if (!token.IsCancellationRequested) fire(); })`
  with the OperationCanceledException swallowed. `fire` exceptions are caught and swallowed
  (document why: a debounced background action must never take down the scheduler; consumers
  own their error reporting).
- [ ] **Step 2:** Run the new fixture — expect FAIL (type missing).
- [ ] **Step 3:** Implement (~60 lines with docs). Doc comment MUST name both the template
  (`SettingsService.ScheduleSave`) and the anti-template (`FileWatcherService.IsDebounced`
  drops the trailing edge) with the keep-in-sync signpost convention.
- [ ] **Step 4:** Fixture green. **Step 5:** Full suite. **Step 6:** Commit:
  `feat(core): trailing-edge debouncer — the shared helper the 3a deferred list called for`

## Task 2: `FileDownloader`

**Files:** Create: `VisualGameStudio.ProjectSystem/Services/FileDownloader.cs`;
Test: `VisualGameStudio.Tests/Services/FileDownloaderTests.cs`

Lift `OpenVsxClient.DownloadVsixToFileAsync`'s mechanics (`OpenVsxClient.cs:186-216`:
ResponseHeadersRead, 80KB loop, `IProgress<(long bytesDownloaded, long totalBytes)>`, ct) with
the three recon-found fixes. `OpenVsxClient` itself is NOT migrated (non-goal).

- [ ] **Step 1: Write the failing tests** against a local `HttpListener` on a loopback port
  (never live GitHub; pick the port with `TcpListener(IPAddress.Loopback, 0)` handoff or retry
  loop — no fixed ports, the suite runs on dev machines):

```csharp
[Test] public async Task Download_WritesTheExactBytes_AndReportsProgress()
    // serve 1MB of known bytes with Content-Length; assert file bytes equal, final progress == (1MB, 1MB),
    // at least one intermediate report, and NO .partial file remains
[Test] public async Task Download_WithoutContentLength_ReportsMinusOneTotal()
[Test] public async Task Failure_MidStream_LeavesNoPartialFile()
    // server closes after half the body -> exception surfaces, destination absent, .partial deleted
[Test] public async Task Cancellation_AbortsAndCleansUp()
[Test] public async Task Deadline_AbortsALongTransfer()               // per-call deadline param, slow-dripping server
[Test] public void UserAgent_IsSent_AndAcceptJsonIsNot()              // capture request headers on the listener; exact assert
```

- [ ] **Step 2:** FAIL. **Step 3: Implement.** `sealed class FileDownloader : IDisposable`:
  owned `HttpClient` with `Timeout = Timeout.InfiniteTimeSpan` and
  `DefaultRequestHeaders.UserAgent` = `VisualGameStudio/1.0` (GitHub mandates one; NO `Accept`
  header — the lifted code's `application/json` was wrong for binaries);
  `Task DownloadAsync(string url, string destinationPath, TimeSpan deadline, IProgress<(long,long)>? progress = null, CancellationToken ct = default)`
  — linked CTS (caller ct + `CancelAfter(deadline)`), stream to `destinationPath + ".partial"`,
  `File.Move(partial, destination, overwrite: true)` on success, `finally`-delete the partial on
  any failure. Doc comment: why infinite client timeout + per-call deadline (the 30s
  `HttpClient.Timeout` covers the whole ResponseHeadersRead read and killed long transfers —
  cite `OpenVsxClient.cs:36`).
- [ ] **Step 4:** Green. **Step 5:** Full suite. **Step 6:** Commit:
  `feat(projectsystem): FileDownloader — streamed, deadline-bounded, partial-file-hygienic`

## Task 3: `ClangdInstaller`

**Files:** Create: `VisualGameStudio.ProjectSystem/Services/ClangdInstaller.cs`;
Test: `VisualGameStudio.Tests/Services/ClangdInstallerTests.cs`

- [ ] **Step 1: Write the failing tests** (local zip fixtures built in-test with `ZipArchive`
  into a temp dir; injectable tools root; injectable downloader seam so no HTTP at all here):

```csharp
[Test] public async Task Install_ProducesTheProbeLayout()
    // fixture zip with root clangd_22.1.6/bin/clangd.exe + lib/clang/22/include/x.h
    // -> <root>/clangd_22.1.6/bin/clangd.exe exists AND lib survived (headers are load-bearing, S0.1)
[Test] public async Task ShaMismatch_RejectsBeforeExtraction()
    // corrupt one byte -> InstallFailed result naming the SHA step; tools root has NO new dir; archive deleted
[Test] public async Task ExistingSameVersionDir_IsReplaced()
    // pre-create clangd_22.1.6/ with a stale marker file -> after install the marker is gone (broken-install repair)
[Test] public async Task StagingDir_NeverLeaksOnFailure()
    // make extraction fail (truncated zip w/ correct sha over the truncated bytes is fine) -> no .staging-* under root
[Test] public async Task ToolsRoot_IsNotCreatedAtConstruction()      // anti-VsixInstaller: ctor must not mkdir (spec)
[Test] public async Task SecondConcurrentInstall_IsCoalesced()       // single-flight: 2 calls -> 1 downloader invocation
```

- [ ] **Step 2:** FAIL. **Step 3: Implement.** Pinned consts (from S0.1, verbatim): URL,
  `ExpectedSha256 = "CE54F16E0B4FD76D450EEDA9664420B195360B73FEBCFE40E661108FA57F2CE1"`,
  `ExpectedSizeBytes = 28_198_778`, `InstalledDirName = "clangd_22.1.6"`. Flow: single-flight
  `SemaphoreSlim(1,1)` (busy ⇒ report already-in-progress, do NOT queue a second download) →
  download zip to `%TEMP%` via the injected downloader (deadline ~10 min) → compute SHA-256
  (`SHA256.HashData` over a stream) and compare ordinal-ignore-case; mismatch ⇒ delete + fail →
  `ZipFile.ExtractToDirectory` into `<toolsRoot>/.staging-<guid>/` → verify
  `.staging/<InstalledDirName>/bin/clangd.exe` exists → if `<toolsRoot>/<InstalledDirName>`
  exists, `Directory.Delete(recursive)` it → `Directory.Move(staging child, final)` →
  `finally`: delete staging + temp zip, swallowing cleanup failures to the output service.
  Result type: a small record `ClangdInstallResult(bool Success, string? InstalledExePath,
  string? FailureStep)` — callers compose toasts from it; the installer never shows UI.
  Progress: forward the downloader tuple through a `IProgress<(long,long)>?` parameter.
- [ ] **Step 4:** Green. **Step 5:** Full suite. **Step 6:** Commit:
  `feat(cpp): ClangdInstaller — pinned 22.1.6 download, SHA-256 gate, staged atomic install`

## Task 4: Locator chain — `FindInDirectories` + tools probe + LLVM dirs

**Files:** Modify: `VisualGameStudio.Core/Utilities/ExecutableLocator.cs`,
`VisualGameStudio.ProjectSystem/Services/ClangdLocator.cs`;
Test: extend `VisualGameStudio.Tests/Core/ExecutableLocatorTests.cs`,
`VisualGameStudio.Tests/Services/ClangdLocatorTests.cs`

- [ ] **Step 1: Write the failing tests:**

```csharp
// ExecutableLocatorTests — pure, injectable fileExists:
[Test] public void FindInDirectories_ProbesEachDirInOrder_WithCandidateNames()   // PATHEXT applied per dir, first hit wins
[Test] public void FindInDirectories_ReturnsAbsolutePaths()                       // Path.GetFullPath guarantee, like Find
[Test] public void FindInDirectories_SkipsDirsThatThrow()                         // per-dir try/catch, like FindIn

// ClangdLocatorTests — the 4-link chain, all seams injected:
[Test] public void Chain_OverrideBeatsToolsDir()
[Test] public void Chain_ToolsDirBeatsPath()             // spec rationale: IDE-managed beats environmental drift
[Test] public void Chain_PathBeatsLlvmDirs()
[Test] public void ToolsProbe_PicksHighestNumericVersion()      // clangd_22.1.6 beats clangd_9.0.0 (ordinal would invert!)
[Test] public void ToolsProbe_UnparseableSuffixLosesToParseable()
[Test] public void ToolsProbe_PosixBinaryName()                  // bin/clangd (no .exe) when not Windows — injectable isWindows
[Test] public void LlvmProbe_UsesThePinnedCandidateList()        // exact list assert (S0.4: pure-seam only, no local install exists)
```

- [ ] **Step 2:** FAIL. **Step 3: Implement.**
  - `ExecutableLocator.FindInDirectories(string? executable, IEnumerable<string> directories, string? pathExtValue, Func<string,bool> fileExists, bool isWindows)`
    — reuses `CandidateNames`; absolutizes hits.
  - `ClangdLocator`: `ResolveClangdPath` grows two injectable probes between the override and
    the PATH probe / after it: signature gains `toolsProbe`/`llvmProbe` funcs defaulting to the
    real implementations. Real tools probe: `Directory.GetDirectories(toolsRoot, "clangd*")`,
    parse the version suffix after `clangd_` with `Version.TryParse`, pick max (unparseable
    ranks below any parsed), probe `bin/clangd(.exe)`. Real LLVM probe:
    `FindInDirectories("clangd", PinnedLlvmDirs, ...)` where `PinnedLlvmDirs` =
    `%ProgramFiles%\LLVM\bin`, `%ProgramFiles(x86)%\LLVM\bin`, vswhere→`VC\Tools\Llvm\x64\bin`
    and `VC\Tools\Llvm\bin`, `%USERPROFILE%\scoop\shims`,
    `%USERPROFILE%\scoop\apps\llvm\current\bin`, `C:\msys64\{mingw64,ucrt64,clang64}\bin`
    (Windows-only list; empty on POSIX for now). The vswhere leg reuses the file-existence
    style ONLY (locate vswhere, run `-property installationPath` with a short wait — this runs
    at DI time ONCE; measure: vswhere alone is ~1s, acceptable; document that the 35s hazard
    was the compiler SPAWN probes, not vswhere).
  - `~/.vgs/tools` root: the probe composes its default root from
    `ClangdInstaller.DefaultToolsRoot` (added by Task 3's fix batch — single-sources the root
    between installer and probe; the canonical whole-`~/.vgs` helper remains future work).
    Injectable for tests.

    > **Plan correction (approved, Task 3 fix batch).** This bullet originally said to default
    > from UserProfile here and write a NEW one-line "another copy of the ~/.vgs root" comment
    > ("do not build the helper here"). Task 3's review batch added
    > `ClangdInstaller.DefaultToolsRoot`, which now carries that comment — compose from it
    > instead of duplicating the path construction again.
  - Also truth-repair the now-stale test remark at `LanguageServiceRegistryTests.cs:643-646`
    ("the locator's answer here is the PATH probe alone") — after this task the production
    chain is override → tools → PATH → LLVM dirs; the test auto-follows, the remark must too.
- [ ] **Step 4:** Green. Also confirm `Di_RegistersClangd_ExactlyWhenTheLocatorFindsOne` still
  passes — it calls production `ResolveClangdPath` so it auto-follows; on THIS machine it now
  finds the installed clangd via the tools probe even with no setting. **Step 5:** Full suite.
  **Step 6:** Commit: `feat(cpp): locator learns ~/.vgs/tools and LLVM install dirs`

## Task 5: Shared `ClangdTestDiscovery` (the 3rd-consumer trigger fired)

**Files:** Create: `VisualGameStudio.Tests/LSP/ClangdTestDiscovery.cs`;
Modify: `VisualGameStudio.Tests/LSP/ClangdLaunchTests.cs:34-103`,
`VisualGameStudio.Tests/LSP/ClangdE2ETests.cs:163-198`

- [ ] **Step 1:** Extract the duplicated `LocateClangd`/`ProbeVgsToolsDir`/`RequireClangd` trio
  into `internal static class ClangdTestDiscovery`. ⚠ Extract by MEMBER, not by line range: in
  `ClangdLaunchTests` the trio is INTERLEAVED with the fixture's `SetUp`/`TearDown` (:71-93 sit
  between `ProbeVgsToolsDir` and `RequireClangd`) — a range-guided cut would nuke the setup.
  (Keep the ordinal-descending pick HERE —
  tests want "any working clangd", the comment must say so explicitly and point at the
  production probe's numeric compare as the deliberate difference). Both fixtures delegate;
  behavior byte-identical; keep each fixture's ignore-message wording.
- [ ] **Step 2:** Run BOTH fixtures against real clangd: `--filter "FullyQualifiedName~ClangdLaunchTests|FullyQualifiedName~ClangdE2ETests"`
  — all run, 0 skips, same counts as before the extraction.
- [ ] **Step 3:** Full suite. **Step 4:** Commit:
  `refactor(tests): shared ClangdTestDiscovery — third consumer arrived with the production probe`

## Task 6: Download UX — toast action, Tools menu, progress, restart prompt

**Files:** Modify: `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs`
(`ReportClangdMissingForCppFile` :1164-1187, new command + orchestration method),
`VisualGameStudio.Shell/Views/MainWindow.axaml` (Tools menu),
`VisualGameStudio.Shell/Configuration/ServiceConfiguration.cs` (DI: `ClangdInstaller` singleton);
Test: `VisualGameStudio.Tests/Shell/ClangdDownloadFlowTests.cs` (new — service-level: the
orchestration lives in a small testable `ClangdDownloadFlow` class in the Shell project, since
`new MainWindowViewModel` is impossible; the VM wires thin)

- [ ] **Step 1: Write the failing tests** for `ClangdDownloadFlow` (deps injected: installer
  seam, toast sink `Action<string,string>`, progress sink, already-resolved supplier):

```csharp
[Test] public async Task WhenClangdAlreadyResolved_ReportsPathAndDoesNotDownload()   // spec: menu on resolved = info toast, nothing else
[Test] public async Task SuccessfulInstall_ShowsRestartPromptWording()               // exact string assert incl. "restart the IDE"
[Test] public async Task FailedInstall_NamesTheFailingStep()
[Test] public async Task ProgressTuples_BecomeZeroToOneFractions()                   // (14099389, 28198778) -> 0.5 within tolerance; -1 total -> indeterminate
[Test] public async Task ConcurrentTrigger_ReportsAlreadyInProgress()                // installer single-flight surfaces as a toast, not a queued dup
[Test] public async Task NonWindowsPlatform_ReportsNotSupportedAndDoesNotDownload()  // spec §1's POSIX sentence — injectable isWindows; exact toast wording
```

- [ ] **Step 2:** FAIL. **Step 3: Implement.**
  - `ClangdDownloadFlow` (Shell, ~80 lines): the policy above; fire-and-forget-safe (the toast
    `NotificationAction` callback is a sync `Action` — the flow method is `async void`-free;
    callers do `_ = flow.RunAsync(...)` with the flow catching everything into the toast sink).
  - `ReportClangdMissingForCppFile`: switch to the ACTIONS overload of `ShowNotification`
    (`:1744-1757`; info+actions ⇒ persists until dismissed) with
    `[Download C++ tools]` → `_ = _downloadFlow.RunAsync(...)`. Keep `_clangdMissingReported`
    once-per-session semantics UNCHANGED (the menu is the retry path — user decision).
    Reword the toast body: the install-remedy sentence stays restart-bound and gains the
    download offer; keep it one sentence, exact wording pinned by the flow test above.
  - Tools menu: `<MenuItem Header="Download C++ _Tools…" Command="{Binding DownloadCppToolsCommand}"/>`
    next to the existing Tools items in `MainWindow.axaml`; command calls the same flow.
  - Progress: `ShowProgressNotification("clangd-download", $"Downloading clangd… {mb:F1} MB of 26.9 MB", fraction)`
    + `DismissNotification` on completion either way.
  - DI: `services.AddSingleton<ClangdInstaller>()` constructed with the production tools root +
    a `FileDownloader`; disposed by the container.
- [ ] **Step 4:** Green. `dotnet clean` (AXAML changed) + build Shell. **Step 5:** Full suite.
  **Step 6:** Commit: `feat(cpp): one-click clangd download — toast action, Tools menu, restart prompt`

## Task 7: Capture the semantic-token legend at the handshake

**Files:** Modify: `VisualGameStudio.Core/Abstractions/Services/ILanguageService.cs:236-289`
(`ServerCapabilities`), `VisualGameStudio.ProjectSystem/Services/LanguageService.cs:558-592`
(`ParseServerCapabilities`); Test: extend `VisualGameStudio.Tests/LSP/CapabilityNegotiationTests.cs`

- [ ] **Step 1: Write the failing tests** (synthetic JSON fixtures — include one built from the
  MEASURED clangd 22.1.6 reply in S0.2, duplicates and all):

```csharp
[Test] public void Parse_CapturesSemanticTokensLegend_TypesAndModifiersInOrder()
    // the real 25-entry clangd fixture: [0]=="variable", [24]=="comment", 18 modifier names in order
[Test] public void Parse_SemanticTokensAbsent_HasProviderFalse_LegendNull()
[Test] public void Parse_SemanticTokensBoolTrue_HasProviderTrue_LegendNull()   // boolean|options union — the real BasicLang server sends objects, but the union is REQUIRED handling (3a finding #4)
[Test] public void Parse_LegendWithDuplicateNames_DoesNotThrow()               // S0.2's landmine, pinned at the parse layer too
```

- [ ] **Step 2:** FAIL. **Step 3: Implement.** `ServerCapabilities` gains
  `bool HasSemanticTokensProvider` and `SemanticTokensLegend? SemanticTokensLegend` where
  `sealed record SemanticTokensLegend(IReadOnlyList<string> TokenTypes, IReadOnlyList<string> TokenModifiers)`
  (Core, beside `ServerCapabilities`). Parser reads `capabilities.semanticTokensProvider` —
  `true`/object union; object ⇒ read `legend.tokenTypes[]` + `legend.tokenModifiers[]` as raw
  ordered string arrays (NO dedup here — the arrays ARE the wire indices).
- [ ] **Step 4:** Green. **Step 5:** Full suite. **Step 6:** Commit:
  `feat(lsp): capture the semantic-token legend the handshake used to discard`

## Task 8: `SemanticTokenLegendMap` — the duplicate-tolerant remap

**Files:** Create: `VisualGameStudio.Core/Utilities/SemanticTokenLegendMap.cs`;
Test: `VisualGameStudio.Tests/Core/SemanticTokenLegendMapTests.cs`

- [ ] **Step 1: Write the failing tests.** The centerpiece is the MEASURED table (S0.2) as a
  single exact assert:

```csharp
[Test] public void Build_FromTheMeasuredClangdLegend_ProducesTheExactTable()
    // full 25-entry input -> expected int[25] { 8,8,7,11,12,11,9,8,2,4,3,10,1,1,-1,0,6,-1,1,-1,14,18,-1,-1,15 }
    // (clangd idx -> BasicLang canonical idx; -1 = uncolored: unknown, concept, macro, bracket, label)
[Test] public void Build_FromBasicLangsOwnLegend_IsIdentity()                  // 19 canonical names -> 0..18
[Test] public void Build_DuplicateNames_MapToTheSameSlot_NoThrow()             // the Dictionary.Add landmine, pinned
[Test] public void RemapData_RewritesTypeIndicesAndModifierBits_InTheEncodedQuintuples()
    // encoded [ΔL,ΔC,len,type,mods]*: clangd type 8 (class) -> 2; clangd mods bit4 (readonly) -> bit2; bit2 (deprecated) -> bit4
[Test] public void RemapData_UnknownType_BecomesTheUncoloredSentinel()         // -1 -> emit an index >= 19 so the highlighter's default arm skips it (assert exact emitted value)
[Test] public void RemapData_UnmappedModifierBits_AreMaskedOut()               // clangd bit7 virtual, bits 10-17 -> dropped
[Test] public void RemapData_ClangdReadonly_DoesNotStrikeThrough()             // the const-renders-deprecated bug, pinned by name
[Test] public void RemapData_LengthNotMultipleOfFive_ReturnsInputUntouched()   // mirror the highlighter's guard
```

- [ ] **Step 2:** FAIL. **Step 3: Implement.** `sealed class SemanticTokenLegendMap` with
  `static SemanticTokenLegendMap Build(SemanticTokensLegend serverLegend)` (canonical names +
  modifier names hardcoded here as THE client-side canonical list, mirroring
  `SemanticTokensHandler.cs:65-97` — add the reciprocal keep-in-sync signpost on BOTH files)
  and `int[] RemapData(int[] encodedData)` (returns a rewritten copy; type −1 →
  `UncoloredIndex = 999` — any value ≥19 hits the highlighter's null-brush default arm,
  `SemanticTokenHighlighter.cs:196` → skip at `:150`; assert 999 exactly so nobody "tidies" it
  to something meaningful). Identity fast-path: when the server legend equals the canonical
  list, `RemapData` returns the input array unchanged (reference-equal — pinned in the
  BasicLang identity test; keystroke-path allocation matters).
- [ ] **Step 4:** Green. **Step 5:** Full suite. **Step 6:** Commit:
  `feat(core): duplicate-tolerant semantic-token legend remap, pinned by the measured clangd table`

## Task 9: Wire the remap at the fetch seam + gate the request

**Files:** Modify: `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs:7733-7760`
(`RefreshSemanticTokensAsync`), `VisualGameStudio.ProjectSystem/Services/LanguageService.cs:2324-2350`
(`GetSemanticTokensAsync` gate); Test: extend `CapabilityNegotiationTests` (gate) +
`SemanticTokenLegendMapTests` (cache behavior)

- [ ] **Step 1: Write the failing tests.** ⚠ There is NO scripted/fake LSP server harness in
  this suite (claims-verified) — do not invent one. Test through the seams that exist:

```csharp
[Test] public void SemanticTokensGate_IsAPurePredicate()
    // extract the gate as `public static bool ShouldRequestSemanticTokens(ServerCapabilities?)`
    // — PUBLIC, not internal: VisualGameStudio.ProjectSystem has NO InternalsVisibleTo for the
    // test project (the assembly says so itself, LanguageService.cs:223; only BasicLang.csproj
    // grants IVT). cf. ParseCompletions, the public-static seam precedent.
    // exhaustive: null caps -> false, provider false -> false, provider true -> true
[Test] public void LegendMapCache_BuildsOncePerLegendInstance()
    // Build is called once for repeated remaps of the same server (cache keyed by legend reference)
```

  The negative wire assertion ("no request actually leaves for a provider-less server") is NOT
  economically testable without a fake-server harness — both live servers advertise the
  provider. Recorded, not silently dropped: the gate predicate test + the positive-path e2e
  (Task 14 proves requests DO flow when the provider exists) are the coverage; the predicate
  is called from exactly one place (assert via a code-review note in the task report).
- [ ] **Step 2:** FAIL. **Step 3: Implement.**
  - `LanguageService.GetSemanticTokensAsync`: early-return null when
    `ShouldRequestSemanticTokens(Capabilities)` is false. Truth about the downstream (the spec
    was corrected on this once already — do not regress it): at the fetch seam null and empty
    BOTH clear today (`MainWindowViewModel.cs:7746-7753` — `Data != null && Length > 0` else
    clear; stale tokens survive only the early returns — disconnected, cancelled, null
    FilePath at `:7737` — and the catch blocks at `:7755-7759`). A provider-less server
    therefore gets its (nonexistent) tokens cleared — correct and harmless. Preserve that
    handling byte-identically; the remap slots in on the non-empty path only.
  - `RefreshSemanticTokensAsync`: `var map = SemanticTokenLegendMap.GetOrBuild(svc.Capabilities?.SemanticTokensLegend);`
    (small static cache, `ConditionalWeakTable` or reference-keyed dictionary; null legend ⇒
    identity) then `document.UpdateSemanticTokens(map.RemapData(result.Data))` — remap only on
    the non-empty branch; the null/empty-both-clear handling stays byte-identical (see Step 3's
    truth note above).
  - REWRITE the false comment at `:7738-7739`: the delta decode is server-agnostic; the
    type/modifier indices are legend-relative and are remapped HERE to BasicLang's canonical
    order before the editor sees them.
- [ ] **Step 4:** Green. **Step 5:** Full suite. **Step 6:** Commit:
  `feat(cpp): route clangd semantic tokens through the legend remap — colors mean the same thing everywhere`

## Task 10: `RegenOnSaveCoordinator`

**Files:** Create: `VisualGameStudio.ProjectSystem/Services/RegenOnSaveCoordinator.cs`;
Modify: `VisualGameStudio.Shell/Configuration/ServiceConfiguration.cs` (singleton, constructed
with the emission service + project service + event aggregator **+ the build service** — the
fire expression needs `CurrentConfiguration`, four dependencies, not three);
Test: `VisualGameStudio.Tests/Services/RegenOnSaveCoordinatorTests.cs`

- [ ] **Step 1: Write the failing tests** (fakes: `IEventAggregator` real instance is fine,
  `IProjectService` stub with a settable `CurrentProject`, emission = the PUBLIC test-seam ctor
  of `IntelliSenseEmissionService` (`:81-85`) or an `IIntelliSenseEmissionService` mock —
  prefer the mock: assert CALLS, not emission internals; debounce interval injected, use 50ms):

```csharp
[Test] public async Task BasSaveUnderTheCurrentProject_TriggersOneEmitAfterTheQuietPeriod()
[Test] public async Task SaveBurst_CoalescesToOneEmit()                    // 5 saves -> 1 RequestEmit (trailing)
[Test] public async Task CppSave_DoesNotTrigger()                          // extension filter: .bas/.mod/.cls only
[Test] public async Task SaveOutsideTheProjectDirectory_DoesNotTrigger()
[Test] public async Task NoProjectOpen_DoesNotTrigger()
[Test] public void Subscription_IsHeldStrongly()                            // GC.Collect after subscribe; a save still triggers (the WeakReference landmine, pinned)
[Test] public async Task Dispose_StopsTriggering()
[Test] public async Task EmitReceives_CurrentProjectAndConfiguration()      // exact args assert
```

- [ ] **Step 2:** FAIL. **Step 3: Implement.** ~90 lines: ctor subscribes
  `Subscribe<FileSavedEvent>` and STORES the returned subscription in a field (the handler runs
  synchronously on the UI thread — it does only: extension check
  (`.bas`/`.mod`/`.cls`, OrdinalIgnoreCase), path-under-`CurrentProject.ProjectDirectory` check
  (`Path.GetFullPath` prefix compare, OrdinalIgnoreCase — document the Windows-casing
  rationale), then `_debouncer.Signal()`). The debouncer (Task 1) fires
  `_emission.RequestEmit(_projects.CurrentProject, _build.CurrentConfiguration?.Name ?? "Debug")`
  — re-reading `CurrentProject` at FIRE time, not capture time (a project switched mid-quiet-
  period must not emit the OLD project — RequestEmit's own gate re-checks native-ness).
  Production interval: `TimeSpan.FromMilliseconds(1500)` const with the derivation comment
  (emission costs seconds and is non-cancellable in flight; 1.5s absorbs auto-save bursts
  without adding perceptible staleness). Dispose: dispose debouncer + subscription.
  DI: singleton, constructed eagerly with the app (it must exist to subscribe — document that
  a lazily-resolved singleton nobody injects never subscribes). The forcing-construction
  precedent lives in `App.axaml.cs:100` (`GetRequiredService<GitAutoFetchService>()` at
  startup), NOT in ServiceConfiguration — add the same forcing line beside it.
- [ ] **Step 4:** Green. **Step 5:** Full suite. **Step 6:** Commit:
  `feat(cpp): .bas saves regenerate IntelliSense headers — debounced, filtered, trailing-edge`

## Task 11: `.blproj` watching + the open-document nudge (⛔ GATED on S0.5)

**Files:** Modify: `RegenOnSaveCoordinator.cs` (watch wiring);
Test: extend `RegenOnSaveCoordinatorTests.cs`

- [x] **Step 0 — GATE RESOLVED (measured, see S0.5): NO NUDGE SHIPS.** `didChangeWatchedFiles`
  is a measured no-op in clangd 22.1.6. The coordinator gains a one-paragraph rationale
  comment: typing heals (~0.2s once the db is right); a `.bas`-save regen means the user is
  editing `.bas` anyway; and — pin this so nobody re-adds it — clangd ACCEPTS
  `didChangeWatchedFiles` silently and does nothing with it (it self-reloads the CDB via its
  `compilationDatabase.automaticReload` contract on the next didChange-triggered build).
- [ ] **Step 1: Write the failing tests** — `.blproj` watch: external change → debounced emit;
  IDE-side save suppressed (`SuppressNotifications` window); watch starts on project open,
  stops on close (drive via the stub project service's events).
- [ ] **Step 2:** FAIL. **Step 3: Implement.** `FileWatcherService.WatchFile(<project>.blproj)`
  (the DEAD-but-complete service — `FileWatcherService.cs:10-226`, DI-registered at
  `ServiceConfiguration.cs:89`; this is its first production caller, note it in the commit) +
  `FileChangedExternally` → the same debouncer. Wrap `ProjectService.SaveProjectAsync`-adjacent
  saves with `SuppressNotifications`. Document the accepted limitation verbatim from the spec:
  the RequestEmit gate reads the stale in-memory project, so an external edit flipping a
  project TO native needs a reopen (the emission itself reloads from disk).
- [ ] **Step 4:** Green. **Step 5:** Full suite. **Step 6:** Commit:
  `feat(cpp): .blproj watching wakes the regen path`

## Task 12: Resolve — model + client call

**Files:** Modify: `VisualGameStudio.Core/Abstractions/Services/ILanguageService.cs:310-332`
(`CompletionItem` + interface method) **and the STALE doc comment at `:250-254`** — it claims
"Both clangd and BasicLang's real `--lsp` server report true" for resolve; S0.3 measured clangd
FALSE. Truth-repair it (the same discipline as the :7738-7739 comment in Task 9).
`VisualGameStudio.ProjectSystem/Services/LanguageService.cs` (`ParseCompletions` :1477-1518,
new `ResolveCompletionAsync`);
Test: `VisualGameStudio.Tests/LSP/ClientCompletionResolveTests.cs` — ⚠ the name
`CompletionResolveTests` is TAKEN: `VisualGameStudio.Tests/LSP/CompletionResolveTests.cs:16`
already declares that class (a SERVER-side fixture testing BasicLang's CompletionHandler).
This new file tests the CLIENT side; cross-signpost the two fixtures' headers.

⚠ There is NO scripted/fake LSP server harness in this suite (claims-verified). The wire-level
tests use the repo's established pure-seam idiom instead: make the resolve REQUEST BUILDER a
`public static` (like `ParseCompletions`) and assert its exact JSON; gate logic is a pure
predicate; end-to-end truth comes from the two LIVE servers.

- [ ] **Step 1: Write the failing tests:**

```csharp
[Test] public void ParseCompletions_PreservesTheDataField()               // JsonElement round-trips; exact re-serialization compare
[Test] public void ParseCompletions_NoData_LeavesDataNull()
[Test] public void ShouldResolve_IsAPurePredicate()                        // caps null / provider false / provider true x data null / data present — exhaustive truth table
[Test] public void BuildResolveParams_EmitsTheExactJson()                  // public static builder; exact serialized-body compare incl. data round-trip (never Does.Contain)
[Test] public async Task Resolve_MergesDocumentationAndDetail_FromAReplyElement()
    // the merge is a pure function over a parsed reply JsonElement — test it directly with fixture JSON
[Test] public async Task Resolve_AgainstTheRealBasicLangServer_FillsLazyDocs()
    // live idiom = ClangdE2ETests.cs:241-243: `new LanguageService(_output)` auto-probes the BasicLang.dll deployed beside the test assembly; complete a .NET member, resolve, assert Documentation non-empty
[Test] public async Task Resolve_AgainstRealClangd_GatesOffAndReturnsTheOriginal()
    // real clangd advertises resolveProvider FALSE (S0.3) — the gate-off path tested against the genuine article, conditional via ClangdTestDiscovery
```

- [ ] **Step 2:** FAIL. **Step 3: Implement.** `CompletionItem` gains
  `public JsonElement? Data { get; init; }` (cloned at parse: `item.GetProperty("data").Clone()`
  — the parse doc gets the 3a warning: `default(JsonElement).GetRawText()` throws
  `InvalidOperationException` NOT `JsonException`, guard `ValueKind == Undefined` outside any
  parser). `ILanguageService.ResolveCompletionAsync(CompletionItem item, CancellationToken ct)`
  → returns the enriched-or-original item; implementation: gate on
  `Capabilities?.HasCompletionResolveProvider != true || item.Data is null` ⇒ return item;
  else `SendRequestAsync("completionItem/resolve", BuildResolveParams(item))` — the builder,
  the `ShouldResolve` predicate, AND the documentation/detail merge are ALL `public static`
  (ProjectSystem has no InternalsVisibleTo — same rule as Task 9's gate seam), serializing
  label/kind/data; reuse the 10s timeout;
  catch-all ⇒ original (rethrow caller-initiated OCE, the `GetCompletionsAsync` catch-shape at
  `:941-951`). ⚠ Do NOT touch `InsertTextFormat` (`ILanguageService.cs:337-341`, wire-int
  comparison at `:1508-1512`).
- [ ] **Step 4:** Green (live BasicLang test RUNS — the compiler build exists beside the test
  output as in the existing live tests). **Step 5:** Full suite. **Step 6:** Commit:
  `feat(lsp): completionItem/resolve — data preserved, capability-gated, BasicLang lazy docs live`

## Task 13: Resolve UI hook + completion-kind map completion

**Files:** Modify: `VisualGameStudio.Shell/Views/Documents/CodeEditorDocumentView.axaml.cs`
(:617-686 — `CompletionData` construction + `ConvertCompletionKind`),
`VisualGameStudio.Editor/Completion/CompletionData.cs` (updatable description),
`VisualGameStudio.Editor/Controls/CodeEditorControl.axaml.cs` (selection-changed wiring);
Test: `VisualGameStudio.Tests/Shell/CompletionKindMapTests.cs` (new) + resolve-coordination
tests in `ClientCompletionResolveTests.cs` (Task 12's file — NOT the pre-existing server-side
`CompletionResolveTests`)

- [ ] **Step 1: Write the failing tests:**

```csharp
[Test] public void ConvertCompletionKind_MapsAllTwentyFiveKinds()      // exhaustive: every Core kind -> its Editor kind, none fall to Text except Text itself
[Test] public async Task SelectionResolve_StaleReplyIsDropped()        // coordinator: resolve for item A completing after selection moved to B is discarded (selection-token compare)
[Test] public async Task SelectionResolve_UpdatesTheDescription()      // CompletionData.Description reflects the merged docs after resolve
```

- [ ] **Step 2:** FAIL. **Step 3: Implement.**
  - `ConvertCompletionKind`: complete the switch. Claims-verified add-list (10 kinds):
    **EnumMember, Event, Operator, TypeParameter, File, Folder, Reference, Unit, Value, Color**
    — `Struct` and `Constant` are ALREADY mapped (`:682-683`), and `Value` is the one earlier
    drafts missed. Glyphs largely exist (`CompletionData.cs:138-155`). The exhaustive test is
    the contract and kills the silent `_ => Text` funnel regardless of any list here.
  - `CompletionData.Description` becomes settable-with-notification (the AvaloniaEdit tooltip
    reads it at selection time — `INotifyPropertyChanged` on the data item + re-assigning the
    tooltip content on change; verify against the fetched AvaloniaEdit source behavior:
    the tooltip content is set FROM `Description` in `CompletionList_SelectionChanged`, so a
    late update must re-set the tooltip's content control — smallest working mechanism wins,
    but it MUST be verified in the user smoke, it cannot be proven headless).
  - Selection hook: subscribe `CompletionList.SelectionChanged` where the window is created
    (`CodeEditorControl.ShowCompletion`, `:3849-4030`); per-selection token; background
    `ResolveCompletionAsync` via the document's routed service; never through
    `CompletionSession`'s pending gate (`CompletionSession.cs:40-46` — it would starve typing).
    Items with null `Data` or a non-providing server short-circuit (free, S0.3: that is ALL
    clangd completions today).
- [ ] **Step 4:** Green. `dotnet clean` if AXAML touched. **Step 5:** Full suite.
  **Step 6:** Commit: `feat(editor): lazy documentation on completion selection + the full kind map`

## Task 14: e2e additions + Definition of Done

**Files:** Modify: `VisualGameStudio.Tests/LSP/ClangdE2ETests.cs` (one semantic-tokens e2e),
plus the DoD checklist below.

- [ ] **Step 1:** Add ONE e2e to the existing fixture (shared world, read-only):
  `SemanticTokens_ForTheMixedProjectCpp_RemapToCanonicalIndices` — fetch
  `textDocument/semanticTokens/full` for main.cpp via the real clangd, run the data through
  `SemanticTokenLegendMap` built from the REAL negotiated legend, and assert a known token
  (the `Player` class reference) carries BasicLang's Class index (2) after remap — proving
  capture → build → remap end-to-end against the live server. Poll-bounded like its siblings;
  dumps traffic on timeout.
- [ ] **Step 2:** Run the e2e fixture twice — 7/7 (was 6), 0 skips, both runs.
- [ ] **Step 3 — DoD checklist:**
  - Full suite green: baseline 3136 + all new tests, same 1 known flake + 1 known skip only.
  - Greps: `Does.Contain` in the new test files → only where content (not wire/argv) is
    asserted; `offset-encoding` → still no live argument; `\.vgs` new occurrences → only the
    installer's injectable-root default + its convention comment.
  - Both entry points sanity: the locator/installer touch NO build path — confirm
    `IDE/BasicLang.exe build` on a native project still works (one CLI smoke build).
  - **USER IDE SMOKE (do not skip — 3a/2/1 all caught real defects only here):**
    1. Delete `cpp.clangd.path` from `~/.vgs/settings.json` + restart → clangd still found
       (the tools probe replaces the hand-edit workaround).
    2. Temporarily rename `~/.vgs/tools/clangd_22.1.6` to a name NOT matching `clangd*`
       (e.g. `_backup_clangd`) — a `clangd*` backup with `bin/clangd.exe` would still be FOUND
       by the tools probe, so clangd never reads as missing and the toast never appears.
       Restart → toast now offers [Download C++ tools]; click it →
       progress toast with a real bar → success toast says restart; restart → clangd Running.
       Afterwards DELETE the `_backup_clangd` dir (the fresh install replaces it; renaming it
       back would collide).
    3. Tools → Download C++ Tools… with clangd resolved → "already installed at <path>", no download.
    4. Open the mixed project → `.cpp` semantic colors: a class renders like a BasicLang class,
       a `const` local does NOT render struck-through (the readonly/deprecated shuffle, live).
    5. Edit a `.bas` file, save → within ~2s + emission time, obj/gen timestamps refresh; type
       in the open `.cpp` → new BasicLang symbol resolves (nudge or heal-by-typing per S0.5).
    6. In a `.bas` file, select a .NET member completion → docs tooltip fills in lazily.
    7. `.cpp` completion selection → no errors, no tooltip regression (clangd resolve gates off).
  - superpowers:finishing-a-development-branch (merge decision, IDE/ binaries refresh decision).
- [ ] **Step 4:** Commit: `test(cpp): semantic-token e2e — capture, remap, and real colors end-to-end`

## Deferred / non-goals (from the spec — the plan adds nothing to this list)

Hot-add roster; transport honesty gap (chip `task_e08444c4`); multi-project emission; POSIX
download mechanics (Linux/mac asset hashes are recorded in S0.1's report for that future work);
markdown doc rendering (`MarkdownLite` noted); download cancel button; theme
`semanticTokenColors`; `OpenVsxClient` → `FileDownloader` migration; macro-token alias
(uncolored for now, S0.2); re-download/repair; the dead `SemanticTokensRefreshNeeded` and
`RestartLspRequested` stubs (leave; delete-decisions recorded, not 3b work).

