# IDE Extensions — recovery ledger (Aug 5 2026)

**What this is.** The Aug 4 2026 session that repaired the IDE's VS Code extension support was
deleted from the UI. This document replaces it. It records what is **already done**, how well each
piece was **actually verified**, which existing documents are **wrong**, and what is genuinely
**still open** — so that work is not repeated.

**Why it exists.** Six of the nine bugs that session fixed were "built but never called." The
same failure mode applies to knowledge: several documents in this repo and in memory describe
features as missing that in fact exist and work. Acting on them means rewriting working code.

**How to use it.** Read this before `next-feature-vscode-extensions.md`, before
`docs/comparisons/ide-parity-scorecard.md`, and before `memory/vscode-parity.md` — all three are
stale on Extensions in ways documented below. The companion narrative, with per-bug reasoning, is
the memory topic file `extensions-panel-revival-aug2026.md`.

**Provenance.** Reconstructed on Aug 5 2026 from four surviving sources: the on-disk transcript
(`670c830a-…jsonl`, 3.22 MB — deletion removes a session from the UI, not from disk), the seven
commits and their messages, the memory topic file, and a handoff document. Every claim below was
re-verified against the live repo at the time of writing.

---

## 1. Settled — do not re-litigate

| Decision | Substance |
|---|---|
| **Open VSX only** | Never the Microsoft Marketplace, whose ToS forbids non-VS-Code clients. Already the only registry the live code targets. `marketplace.visualgamestudio.com` in `MarketplaceService.cs:27` is fictional dead code, not an intended endpoint. |
| **Async end-to-end for contribution loading** | Chosen over `ConfigureAwait(false)` everywhere (one missed `await` silently restores the deadlock) and over `Task.Run` + block (still freezes the UI). |
| **Extension themes register per-session, not persisted** | Extensions re-publish contributions every launch; persisting duplicates entries and dangles after uninstall. The handler re-applies the saved theme if it matches, because startup theme resolution runs *before* extension discovery. |
| **Source-guard tests, not behavioural ones** | `ExtensionService._extensionsDir` is hard-coded to `~/.vgs/extensions` and not injectable, so behavioural tests at that layer write into the developer's real profile. Precedents: `BuildSolutionAmplifierGuardTests`, `NewProjectWizardSwapGuardTests`. Making the directory injectable is a **prerequisite** for any real integration test. |
| **Probe ladder for manual testing** | Theme-only (`dracula-theme`) → grammar-only (`mrmlnc.vscode-apache`) → something with a `main`. Avoid heavyweight extensions as a first probe; their failures are uninterpretable. |
| **Opening a file may spawn Node child processes** | Correct VS Code semantics, shipped deliberately with explicit user consent. Do not re-gate it on `IsExtensionHostRunning` — that guard *was* bug 8. |
| **The assistant cannot click the Avalonia UI** | Division of labour: assistant builds, launches, reads logs; the user clicks and pastes Output. Do not burn effort automating it. |
| **"Commit" never implies "push"** | Pushes are requested separately, every time. |

---

## 2. Done — with honest verification levels

Nine chained defects, seven commits, all on `origin/master` (`1a2d5f1` … `3449378`). Verified:
`git merge-base --is-ancestor <sha> origin/master` succeeds for all seven.

**Verification is not uniform. The distinction below is the most important content in this file.**

| # | Fix | Commit | Verified how |
|---|---|---|---|
| 1 | Extensions/Problems ViewModels never constructed | `1a2d5f1` | **Running IDE** (Extensions half) |
| 3 | Open VSX search discarded every result (STJ case-sensitivity) | `3de462c` | **Running IDE** + live-API probe |
| 4 | Install froze the IDE (sync-over-async deadlock) | `dae3b82` | **Running IDE** |
| 5 | Extension themes never registered | `19f55c0` | **Running IDE** — the strongest confirmation of the session |
| 6+7 | Theme identity from manifest; JSON-Schema `type` array | `fbcdce5` | **Running IDE** |
| 8 | `onLanguage` activation never fired | `23a631c` | **Running IDE — event only.** See caveat |
| 2 | Problems panel unfed | `1a2d5f1` | **Tests only — never observed** |
| 9 | Grammar `embeddedLanguages`/`tokenTypes` are objects | `3449378` | **Tests only — never observed** |

**Caveat on #8.** What was confirmed is that `[Extensions] Language opened: …` fires. What was
*not* confirmed is that anything then activates. **No extension with a `main` has ever activated
through this IDE's UI.** Do not read "verified" as "the host works."

**Test footprint.** +22 tests, fast subset 4125 → 4147/0/1, across five files. Only four tests were
ever seen **red** before their fix (three `ExtensionsPanelWiringTests` and the deadlock source
guard); the rest were written afterwards. These guard against *re-breaking the wiring*, not against
the pipeline failing. There is no end-to-end integration test.

---

## 3. Stale documents — the actual double-work risk

Each of these will send a reader to build something that already exists, or to trust something
unproven.

| Document | What it claims | Reality |
|---|---|---|
| `memory/next-feature-vscode-extensions.md:40-52` | Six "gaps to build" | **Five already exist.** Only webview is real. Carries a retraction banner at :30 that readers skip. |
| `memory/extension-host.md:9` | "Production-ready. HTML extension activates, registers 15 providers" | **Retired.** Bug 8 made that path unreachable, so the result must have come from a direct harness, not the real path. Its *architecture map* is still reliable. |
| `docs/comparisons/ide-parity-scorecard.md:288-292` | Extensions 2/5; five specific gaps | **All five false** against current code. |
| `memory/vscode-parity.md:15` | Extensions 95%, "full Node.js extension host" | Wrong in the opposite direction — the host tier is unproven. |

**Neither parity number is trustworthy.** The accurate statement: the runtime existed but had never
executed through the UI until Aug 4, and the Node-host tier is *still* unproven.

**Scope trap.** `memory/extension-repair-jul2026.md` and `docs/IDE-Extensions.md` are a *different
subsystem* — the external editor plugins that add BasicLang support to VS Code and Visual Studio.
Nothing to do with this IDE hosting VS Code extensions.

---

## 4. Open work

**1. The Node extension host has never run through the IDE UI.** Everything upstream is proven:
install, extract, discover, manifest parse, static contributions, theme and grammar registration,
`onLanguage` firing. The blocker is mundane, not a code defect — **none of the three installed
extensions has a `main`**, so nothing can start the host. `vscode.html-language-features` was
removed from `~/.vgs/extensions` on Aug 4. Reinstalling it (keeping `vscode.html` alongside, as VS
Code does) and opening an `.html` file is the test. `LanguageFileTypes.GetEditorLanguageId(".html")`
returns exactly `"html"`, so `onLanguage:html` will match. **User action — requires clicking the UI.**

**2. Manifest error isolation.** Manifest handling is caught at **whole-extension scope** in two
places (`ExtensionService.cs:923-957` wraps all five loaders in one `try/catch`; the per-directory
catch at `~:151` is likewise whole-extension), so one unread cosmetic field vetoes an extension's
grammars, themes, snippets, commands, keybindings *and* activation. Two proven kills, each patched
pointwise at the DTO level (`fbcdce5`, `3449378`).

> **Measured Aug 5, and far larger than previously believed:** against the real
> `VisualGameStudio.Core.dll` with the production `JsonOptions`, of 16 real VS Code manifest shapes
> **only 2 load — 14 kill the whole extension.** `contributes.menus`, `views` and `viewsContainers`
> are **object maps** in VS Code's schema but are typed `List<T>` in the DTO, so they could never
> have bound a real manifest. An ESLint-shaped manifest dies whole.
>
> **Isolation alone is NOT sufficient** and must not ship by itself: for object-map sections it
> converts a loud kill into permanent silent data loss, and `commands[].icon={light,dark}` would
> drop the entire `commands` section — one of only two sections with a live consumer — leaving an
> empty command palette and a dead `onCommand` path, silently. **Shape fixes first, isolation
> second as the residual net.**

**3. Four Open VSX / install implementations should collapse to one.** `VsixInstaller` +
`OpenVsxClient` is the most complete (update, state file, events) but is **orphaned — never
DI-registered**; `MarketplaceService` is orphaned and points at a fictional host; `ExtensionService`
is the DI-registered one; `ExtensionsViewModel` has its own inlined `HttpClient` and hard-coded URLs
— **the one the UI calls, and the one that had the JSON bug.** The three correct ones are
unreachable. *Never approved — do not start unasked.* Explicitly argued against: a from-scratch
`IExtensionRegistry` redesign, because `VsixInstaller` already is that interface, merely unregistered.

**4. Five install-reliability gaps, recorded nowhere else.** All verified still present:
- Silent-catch old-version cleanup (`ExtensionsViewModel.cs:206`, also `:242`, `:273`) — on Windows a
  failed delete lets `CopyDirectory` write into the surviving directory, producing a
  **mixed-version install with no error shown**.
- No `engines.vscode` compatibility gate — the DTO exists (`IExtensionService.cs:443`), nothing checks it.
- No `extensionDependencies` resolution — a dependent extension installs alone and fails at activation.
- Fire-and-forget enable/disable (`:349`, `:365`) — exceptions vanish.
- Zero tests on any of it.

**5. `workspaceContains:` activation — deliberately deferred, with a prerequisite.**
`NotifyWorkspaceOpenedAsync` is fully implemented (`ExtensionService.cs:1452`) and has **zero
callers**. This is a recorded decision, not an oversight: the workspace root is established in at
least four places, and wiring three of four would recreate the partial-wiring disease the whole
series was curing. **Prerequisite: survey all four root-setting sites first.**

**6. Dead `ExtensionHostMain.js` is still a fallback in the host probe chain**
(`ExtensionService.cs:1289-1290`). The 464-line pre-Wave-8 stub, whose own design doc says its
provider registrations are no-ops, is still probed. A deployment missing `ExtensionHost/main.js`
will **silently load a fully-running host with dead providers** instead of failing loudly. If the
host ever appears to start but registers nothing, check *which script resolved* before debugging.

**7. Webview: 2 of 5 RPCs handled.** The Node side is complete
(`ExtensionHost/vscode-api/window.js:332-366`), but `ExtensionHost.cs` registers only `webview/create`
(`:194`) and `webview/setHtml` (`:195`); `postMessage`, `reveal`, `dispose` and `webviewView/register`
go nowhere. `WebViewDocumentView.axaml:88` renders HTML as **source text**. Real rendering needs
WebView2 or CefGlue. Do not rebuild the working JS half.

**8. The `ContributionsLoaded` event shares the `total > 0` gate** (`ExtensionService.cs:940-952`).
The Output line being gated is known; that the *event* — the seam carrying themes to `ThemeManager` —
sits inside the same gate is not. Any future contribution type that populates `stats` while
returning 0 is silently **skipped**, not merely unlogged.

**9. No end-to-end integration test.** Flagged three times, never written. Blocked on the
`_extensionsDir` injectability prerequisite above.

---

## 5. Environment traps — each costs 20–60 minutes to rediscover

- ⛔ **`IDE\VisualGameStudio.exe` is a stale separate copy** (Jul 26). Shell builds do **not** deploy
  there. Run `VisualGameStudio.Shell\bin\Release\net8.0\VisualGameStudio.exe`. `AssemblyName` is
  `VisualGameStudio`, so there is no `VisualGameStudio.Shell.exe`. **Verifying a fix against the
  stale binary shows the old broken behaviour and reads as "the fix didn't work."**
- ⛔ A running IDE locks `VisualGameStudio.ProjectSystem.dll` → MSB3027. Close it first — and **ask
  before force-closing**; it may hold unsaved user edits.
- ⛔ Killing the IDE can leave `dotnet BasicLang.dll --lsp` alive holding `BasicLang.dll`. Find it by
  `ParentProcessId` and kill it too.
- ⛔ `ClientCompletionResolveTests` asserts `lsp.IsConnected` within a 30 s spawn budget and fails
  when concurrent sessions saturate the machine. **Check process load before believing a red run.**
- ⛔ **Do not `git stash pop`.** `stash@{0}` is from **March 20**, touches 27 files including
  `ExtensionService.cs` (+340) and `ExtensionsViewModel.cs` (+275) — precisely the files the seven
  commits rewrote. Drop it, never pop it.
- 💡 A loaded extension can be **invisible** in the Output log: the per-extension line is gated on
  `total > 0`. Count `Discovered N extension(s)` instead.
- 💡 `VSTHRD002/100/110` are in the Shell `.csproj` `NoWarn` list — the analyzers for the deadlock
  defect class. `ExtensionHost.cs(598)` still warns `VSTHRD003`.

---

## 6. Concurrency

Multiple Claude sessions work this repo simultaneously, and **at least one commits directly to the
main checkout's `master`** — not only to worktrees. Before any git write:
`git fetch`, then read every subject in `git log --oneline origin/master..HEAD`, then check
`.git/MERGE_HEAD`. `git status --branch` alone says "ahead N" and looks ordinary.

At the time of writing, the IDE/extensions area (`Shell`, `Editor`, `Core`, `ProjectSystem`) was
**unclaimed**, while `BasicLang/*` and `docs/superpowers/plans` were actively owned. One live seam
for extension work: `feat/js-backend` replaces the body of `ExtensionHost.FindNodeExecutable()` with
a shared `NodeLocator.Find()`. If you need Node discovery, coordinate rather than re-implement.

---

## 7. Two rules this subsystem earned

**Verify reachability before capability.** Ask "is this code even called?" before "does this code
work?" Six of nine bugs were built-but-never-called. `grep "new ExtensionsViewModel"` returning zero
was more diagnostic than reading any implementation.

**When two implementations exist here, the wired one is the broken one.** Three times: the four Open
VSX clients (three correct and orphaned), the two theme loaders (the extension path picked the pure
parser), and the manifest models (`Core/Extensions/VSCodeExtension.cs` already had the correct
shapes). Before fixing a class, check whether a correct sibling already exists.

**Corollary — counters that increment on *attempt* get read as counts of *completion*.** "Loaded
contributions … 1 theme(s)" was printed while the parsed theme was discarded. A failed Avalonia
binding yields `UnsetValue`, so `IsVisible` defaults to **true** — a dead panel renders *more*
chrome than a live one.
