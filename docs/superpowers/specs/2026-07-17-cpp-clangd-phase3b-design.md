# C++ IntelliSense Phase 3b — clangd acquisition, legend negotiation, save-regen, resolve

**Date:** 2026-07-17
**Status:** Approved design, pre-plan
**Parent:** `2026-07-11-cpp-language-support-design.md` §4 (the Phase 3 design; Phase 3a shipped
2026-07-17 at master `a5ad4b9` — routing registry + clangd launch + per-server status + degradation).
**Scope decision (user, Jul 15):** Phase 3 was split 3a/3b. This spec covers 3b: the five items
deferred by the 3a plan's "Deferred to Phase 3b" section, plus the hygiene item below. Nothing else.

## User decisions binding this spec (made Jul 17)

1. **Activation after download = restart prompt, NOT hot-add.** The server roster stays fixed at
   IDE startup (`ServiceConfiguration.cs:46-67` resolves `ClangdLocator.Locate` once, at container
   build). A freshly downloaded clangd is announced with "restart the IDE to enable C++
   IntelliSense". Hot-adding a server mid-session is a NON-GOAL: the fixed-roster invariant
   (immutable `_services`/`_byLanguageId` in `LanguageServiceRegistry`, ctor-time event wiring and
   status seeding in `MainWindowViewModel.cs:536-556`, disposal ownership, the DI-exactness pin
   test) was hardened deliberately in 3a and is not reopened for a once-per-machine convenience.
2. **Download UX = toast action button + Tools menu item.** The existing clangd-missing toast
   (`MainWindowViewModel.ReportClangdMissingForCppFile`, `:1164-1187`) gains a
   `[Download C++ tools]` action (the actions overload of `ShowNotification`, `:1744-1757`;
   info+actions ⇒ `autoDismiss=false`). A permanent **Tools → Download C++ Tools…** menu item is
   the discoverability and retry path — the toast stays once-per-session
   (`_clangdMissingReported`, `:1190`); a declined or failed download is retried from the menu,
   so no new suppression states are introduced.
3. Recorded for completeness: the startup status-bar presentation ("clangd: Stopped" + non-green
   dot before a project opens) stays **as-is** (user decision at the 3a smoke).

## 1. Acquisition — pinned download, verified, no settings write

**New `ClangdInstaller`** (VisualGameStudio.ProjectSystem), following the repo's injectable-root
convention (ctor takes the tools root, production default `~/.vgs/tools`; tests point it at a temp
dir — same shape as `VsixInstaller.cs:46-52`, which recon confirmed is an UNWIRED pattern donor
with zero production references, not a dependency). Do NOT copy `VsixInstaller`'s eager
`Directory.CreateDirectory` at construction (`:54`) — the tools dir is created only when a
download actually runs, so non-C++ users never grow one.

- **Pinned version + integrity:** the installer downloads exactly **clangd 22.1.6** — the binary
  every Phase 3a test ran against — from the official standalone release, and verifies a
  **pinned SHA-256** before extraction (`System.Security.Cryptography.SHA256`; the repo has no
  existing checksum utility). Mismatch ⇒ delete the file, report failure, nothing extracted.
  The exact release URL and hash are **pinned at plan time (Step 0)** — neither exists in-repo
  today. No auto-update, no version choice UI.
- **Transfer via a new shared `FileDownloader`**, lifted from
  `OpenVsxClient.DownloadVsixToFileAsync` (`OpenVsxClient.cs:186-216`: ResponseHeadersRead,
  80 KB streaming loop, `IProgress<(long bytesDownloaded, long totalBytes)>`, honors
  `CancellationToken`) with its three recon-found hazards fixed:
  1. `HttpClient.Timeout = 30s` (`:36`) aborts long transfers mid-stream under
     ResponseHeadersRead ⇒ `Timeout.InfiniteTimeSpan` on the client + a per-call overall
     deadline via linked CTS.
  2. No partial-file cleanup (`:204-215`) ⇒ download to `<dest>.partial`, rename into place on
     success, delete on any failure.
  3. Headers: keep a `User-Agent` (GitHub requires one), drop the JSON `Accept`.
  `OpenVsxClient` itself is NOT migrated to `FileDownloader` in 3b (no extension-code churn);
  the plan may note it as a follow-up.
- **Install layout:** extraction must land `~/.vgs/tools/clangd_<version>/bin/clangd.exe` —
  preserving the release zip's root folder, which is exactly what the probe (§2) and both
  existing test probes expect (`ClangdLaunchTests.cs:52-69`). The directory IS the state:
  **no JSON state file, and no settings write** — the locator's new tools probe finds the result
  at next start. (This deliberately sidesteps the settings-dialog X-reverts-live-edits footgun
  that forced hand-editing `settings.json` during the 3a smoke.)
- **Progress:** `ShowProgressNotification(id, message, progress)` (`MainWindowViewModel.cs:1765-1772`)
  with a real 0–1 value — the first determinate consumer (both existing callers are
  indeterminate builds). **No cancel button** in 3b: no notification-level cancel hook exists;
  the per-call deadline bounds a wedged transfer. Documented limitation.
- **Platform:** download is **Windows-only** in 3b (zip). The parent spec is silent on POSIX
  download mechanics; `.tar.xz`, `chmod +x` (`ZipFile` does not restore the exec bit), and
  POSIX archive handling are deferred. On non-Windows the action reports "not supported on this
  platform yet". Probe logic (§2) remains cross-platform.
- **Completion:** success toast — "clangd installed — restart the IDE to enable C++
  IntelliSense." No programmatic restart (none exists; composing the NewWindow/ExitAsync halves
  has flush-ordering hazards and is YAGNI). Failure ⇒ error toast naming the step that failed;
  the Tools menu is the retry.
- The download callback on the toast is a synchronous `Action`
  (`NotificationAction`, `:8404-8414`) — it must fire-and-forget into a tracked task; concurrent
  triggers (toast + menu) must coalesce to one download (single-flight, same discipline as
  `LanguageService._startGate`).

## 2. Locator — the probe chain grows two links

New production resolution order in `ClangdLocator.ResolveClangdPath` (`ClangdLocator.cs:66-76`),
all steps injectable pure-static seams (the class's only test seam, by its own documentation):

**`cpp.clangd.path` override → `~/.vgs/tools` probe → PATH → LLVM install dirs**

- **Tools-before-PATH rationale:** a downloaded copy is IDE-managed, version-pinned, and exists
  only because the user explicitly fetched it; environmental PATH drift must not shadow it. The
  override remains the escape hatch for anyone who wants a different binary.
- **Tools probe:** scan `~/.vgs/tools/clangd*/bin/clangd(.exe)` choosing the highest version by
  **numeric version comparison** — NOT the test probes' ordinal-descending sort, under which
  `clangd_9` outranks `clangd_22`. Unparseable dir suffixes lose to parseable ones. Windows
  probes `bin\clangd.exe`; POSIX probes `bin/clangd`.
- **LLVM-dir probe:** cheap file-existence checks only — never the 35-second spawn-probe style of
  `CppToolchain.Find()` (`IntelliSenseEmissionService.cs:45-47` records why project-open must
  not pay that). Candidate list is pinned at plan time; candidates to verify: `%ProgramFiles%\LLVM\bin`,
  VS-bundled `<VS>\VC\Tools\Llvm\x64\bin` (locatable via the existing vswhere pattern), scoop
  (`~\scoop\apps\llvm\current\bin`, `~\scoop\shims`), MSYS2 (`C:\msys64\{mingw64,ucrt64,clang64}\bin`).
  All are currently UNVERIFIED external knowledge — the plan pins the final list.
- `ExecutableLocator` (`Core/Utilities/ExecutableLocator.cs`) gains a probe-directories entry
  point reusing `CandidateNames` + PATHEXT handling (there is no such API today; `Find` is
  PATH-only), returning absolute paths per its existing guarantee.
- **Test ripple:** the DI-exactness pin `Di_RegistersClangd_ExactlyWhenTheLocatorFindsOne`
  (`LanguageServiceRegistryTests.cs:649-663`) must mirror the full new chain or it lies on any
  machine with a downloaded clangd. The production tools probe is the **third writing** of the
  tools-discovery logic — the trigger the 3a plan recorded (`plans/...phase3a.md:1136`) for
  extracting the two duplicated test probes into a shared `ClangdTestDiscovery` helper. Do both.
- Re-running `Locate` in-process is safe (`SettingsConsumerRegistry.RegisterConsumer` is
  idempotent for an identical key+description — `SettingsConsumerRegistry.cs:63-81`).
- Toast wording: the current "restart the IDE to pick it up" sentence stays truthful under
  restart-prompt activation; the wording is re-checked when the download action is added.

## 3. Semantic tokens — capture the legend, remap at the one two-sided seam

- **Capture:** `ParseServerCapabilities` (`LanguageService.cs:558-592`) currently discards
  `semanticTokensProvider` entirely. It gains: `HasSemanticTokensProvider` and the legend
  (`tokenTypes: string[]`, `tokenModifiers: string[]`) on `ServerCapabilities`
  (`ILanguageService.cs:236-289` — the record's doc comment sanctions growth on caller need).
- **Remap at the fetch seam:** `MainWindowViewModel.RefreshSemanticTokensAsync`
  (`:7733-7760`) is the only place holding BOTH the owning service (hence its capabilities) and
  the token data. A per-server remap table, built once from the server's legend names →
  BasicLang's canonical indices (both servers use standard LSP token-type names; BasicLang's
  canonical order is `SemanticTokensHandler.cs:65-97`, mirrored by the highlighter's constants),
  rewrites tokenType indices and modifier bits in the `int[]` before it is pushed to the editor.
  Unknown names map to a sentinel that renders uncolored (the highlighter already skips unknown
  indices — `SemanticTokenHighlighter.cs:196`, `:150`). **The Editor project is untouched**; the
  alternative — threading the legend through the three `int[]`-only hops into the highlighter —
  was rejected as widening three interfaces for the same result.
- **Gate:** the request is gated on `HasSemanticTokensProvider` (today `GetSemanticTokensAsync`
  fires at any connected server and a blanket catch eats the error — `LanguageService.cs:2326`,
  `:2346-2349`).
- **Truth repair:** the FALSE comment at the seam ("the editor decodes the token data, which is
  server-agnostic", `MainWindowViewModel.cs:7738-7739`) is corrected — only the delta decode is
  server-agnostic; index interpretation is legend-relative.
- **Step 0 (plan time):** capture real clangd 22.1.6's legend from its `initialize` reply — the
  3a plan's "clangd's 0 is variable" claim is UNVERIFIED in-repo — and pin the remap test with
  the captured data. BasicLang's legend maps names to themselves: zero behavior change, pinned.
- **Preserved distinction:** null result (error) keeps stale tokens; empty data clears
  (`MainWindowViewModel.cs:7746-7753`) — the remap path must not collapse the two.
- Theme `semanticTokenColors` (parsed but consumer-less — `VsCodeThemeLoader.cs:126-136`) stays
  dead: explicit non-goal.

## 4. Regen on save — a coordinator service with a trailing-edge debounce

- **New `RegenOnSaveCoordinator`** (ProjectSystem; policy lives in a service because
  `MainWindowViewModel` is untestable by construction — `new MainWindowViewModel` appears
  nowhere). It subscribes to `FileSavedEvent` via the EventAggregator and **holds the
  subscription strongly** (`EventAggregator.Subscribe` stores a WeakReference —
  `EventAggregator.cs:60`; a casual lambda subscriber is silently GC'd; pattern:
  `TimelineViewModel.cs:52-54`).
- **Filters, in order (handler runs synchronously on the UI thread — it must be O(1) and defer
  real work):** extension ∈ {`.bas`, `.mod`, `.cls`} (the event fires for EVERY saved file type,
  auto-save included — `CodeEditorDocumentViewModel.cs:625`, `MainWindowViewModel.cs:2746-2756`);
  path under `IProjectService.CurrentProject.ProjectDirectory`; then hand off. Native-only
  gating is already inside `RequestEmit` (`IntelliSenseEmissionService.cs:93-94`).
- **Debounce:** the `SettingsService.ScheduleSave` template (`SettingsService.cs:941-986` —
  CTS + `Task.Delay` + cancel-restart; the trailing edge always fires), explicitly NOT
  `FileWatcherService.IsDebounced` (`:158-171` — a throttle that DROPS the trailing edge).
  Interval pinned in the plan at **1.5–2 s** (an emission runs the whole non-cancellable
  front end + optimizer, seconds per run — `IIntelliSenseEmissionService.cs:16-17`). No
  flush-on-dispose: `IntelliSenseEmissionService.Dispose` already refuses to hold shutdown for
  an emission, the coordinator follows suit.
- **Invocation:** `RequestEmit(CurrentProject, currentConfiguration)` — same expression as the
  project-open call site (`MainWindowViewModel.cs:988-989`). `RequestEmit`'s unconditional
  supersede is pre-blessed for exactly this trigger by its own comment
  (`IntelliSenseEmissionService.cs:113-117`); multi-project remains the recorded breaker and a
  non-goal.
- **`.blproj` watching:** turn on the dead-but-complete `FileWatcherService` (implemented
  `:10-226`, DI-registered, zero production callers): `WatchFile(<project>.blproj)` on project
  open, unwatch on close, `SuppressNotifications` around IDE-side project saves
  (`ProjectService.SaveProjectAsync`), external change → the same debounced `RequestEmit`.
  FSW events arrive on threadpool threads — the debounce absorbs marshaling. **Accepted
  limitation (documented):** `RequestEmit`'s gate reads the STALE in-memory project, so an
  external edit that flips a project TO native won't pass the gate until reopen (the emission
  itself reloads from disk — `IntelliSenseEmissionService.cs:183-184`; only the gate lags).
  The second, also-dead watcher stack (`IFileService.WatchDirectory`) stays dead — noted.
- **Open-document staleness — Step 0 (plan time):** after a regen rewrites `obj/gen` + the CDB,
  an idle-open `.cpp` learns nothing (Task 13 MEASURED: lone didOpen does not heal; didChange
  does). Empirically test whether clangd 22.1.6 honors `workspace/didChangeWatchedFiles` for
  CDB/header updates (never exercised in-repo). If yes: send it after emission completes —
  clean, no interaction with the per-document didChange version counters (which live in an
  inaccessible closure, `MainWindowViewModel.cs:2425-2436`). If no: ship regen WITHOUT a nudge
  (typing heals; a `.bas`-save regen means the user is editing `.bas` anyway) and record it.

## 5. completionItem/resolve — lazy documentation, capability-gated

- **Model:** `CompletionItem` (`ILanguageService.cs:310-332`) gains a `Data` carrier
  (`JsonElement?`, cloned); `ParseCompletions` (`LanguageService.cs:1477-1518`) currently drops
  the LSP `data` field on the floor and now preserves it. Items from non-LSP sources (extension
  host, snippets) carry null `Data` and are never resolved.
- **Client call:** `LanguageService.ResolveCompletionAsync(item)` sends `completionItem/resolve`,
  gated on `ServerCapabilities.HasCompletionResolveProvider` — already parsed and stored
  (`LanguageService.cs:580-584`), consumed nowhere today, i.e. a free gate. Reuses
  `SendRequestAsync`'s 10 s timeout machinery; on any error returns the original item.
  Recon corrected the plan's premise: BasicLang's REAL `--lsp` server has `ResolveProvider = true`
  with a working handler (`CompletionHandler.cs:41-87`), so both languages benefit; the
  `--lsp-simple` fallback reports false and no-ops through the gate.
- **UI:** hook `CompletionList.SelectionChanged` (public AvaloniaEdit event) to fire a background
  resolve for the selected item — never through `CompletionSession`'s pending-request gate
  (it would starve word triggers; `CompletionSession.cs:40-46`). **Refresh hazard (recon):**
  AvaloniaEdit reads `Description` once at selection time; a late resolve must actually repaint —
  the tooltip content becomes updatable (bindable description or explicit tooltip-content
  re-set on resolve completion). Stale-drop: a resolve completing after the selection moved on
  is discarded.
- **Adjacent fold-in:** `ConvertCompletionKind` (`CodeEditorDocumentView.axaml.cs:666-686`) maps
  only ~14 of 25 kinds; clangd emits several of the dropped ones (EnumMember, Operator,
  TypeParameter, …) which currently all render as Text. Complete the mapping (glyphs largely
  exist — `CompletionData.cs:148-150`).
- Documentation stays plain text in 3b: `ParseCompletions` flattens `MarkupContent` to its value
  string; markdown rendering (`MarkdownLite.cs` is a candidate) is a recorded non-goal.

## 6. Hygiene

- **Task 0:** remove the dev leftover at `App.axaml.cs:133-144` that hardcodes auto-opening
  `C:\Users\melvi\Documents\TestProject\TestProject.blproj` on every startup. It pollutes every
  launch on this machine and would re-trigger under any future restart flow.

## Non-goals (all recorded, none silently dropped)

Hot-add server roster (user decision); transport honesty gap in `LanguageService`
(start-failures swallowed / stop-failure orphan hazard — spun off as background task chip
`task_e08444c4`); multi-project emission supersede; POSIX download mechanics (`.tar.xz`,
`chmod +x`); markdown documentation rendering; download cancel button; theme
`semanticTokenColors` consumption; the settings-dialog X-reverts-live-edits footgun (owned by
the settings workstream; §1's no-settings-write design removes this feature's exposure to it);
migrating `OpenVsxClient` onto `FileDownloader`; the dead `SemanticTokensRefreshNeeded` debounce
in `CodeEditorControl` and the dead `RestartLspRequested` stub (noted for the plan as delete-or-
leave decisions, not features).

## Testing strategy (3a discipline carries over)

TDD per task; **exact-token/parse assertions for anything reaching a wire or a process** (never
`Does.Contain` for argv or protocol bytes); real-clangd conditional tests via the shared
discovery helper (`Assert.Ignore` when absent — but they RUN on the dev machine);
`FileDownloader` tested against a local HTTP fixture (never live GitHub in the suite); SHA-256
verification tested with known vectors including a corrupted-archive rejection; installer tested
against a local zip fixture producing the exact `clangd_<v>/bin/clangd.exe` layout; legend remap
pinned with the CAPTURED real clangd legend (Step 0) plus a BasicLang identity-map test; the
regen coordinator tested through `IntelliSenseEmissionService`'s public emit-seam test
constructor; resolve tested against real clangd (launch-tests pattern) and with mocks for the
gate/no-data/stale-drop paths. Suite conventions: full-suite output redirected to a file;
exit 1 from the known cpp-game-app env flake is normal; `dotnet clean` after AXAML changes;
this machine HAS MSVC — tests needing "no toolchain" pass `toolchain: null` explicitly.

## Plan-time Step 0s (empirical, before tasks are written)

1. Pin the exact clangd 22.1.6 Windows release URL + SHA-256 (external; not in repo).
2. Capture clangd 22.1.6's semantic-token legend from a real `initialize` reply.
3. Measure whether clangd 22.1.6 honors `workspace/didChangeWatchedFiles` for CDB/header
   updates (decides §4's nudge).
4. Verify the LLVM install-dir candidate list against real installs where possible; pin the list.
