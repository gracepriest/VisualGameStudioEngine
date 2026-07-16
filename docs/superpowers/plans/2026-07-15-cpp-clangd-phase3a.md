# C++ Phase 3a — clangd IntelliSense (routing seam) Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A `.cpp`/`.h` file open in VGS Studio gets real IntelliSense from clangd — completion, hover, go-to-definition, find-references, and as-you-type diagnostics — routed alongside the existing BasicLang language server, with no regression to `.bas` behavior.

**Architecture:** Extend the ONE live client (`LanguageService`) from a hardcoded-BasicLang singleton into a parameterized per-server instance, and put a new `ILanguageServiceRegistry` (extension → service) in front of it. Convert the ~26 `IsBasicLangSourceFile` **exclusion gates** into **routing lookups**. Feed clangd by extracting a toolchain-free `EmitForIntelliSense` seam out of `CppProjectBuilder.Build`, so `obj/gen` headers + `compile_commands.json` exist on project open, before any build.

**Tech Stack:** C# / .NET 8, Avalonia 11.3, AvaloniaEdit 11.3, NUnit, LSP 3.17 over stdio, clangd.

**Scope boundary — Phase 3b (NOT this plan):** clangd download/acquisition, LLVM install-dir probing, semantic-token legend negotiation, debounced regen on `.bas` save, `completionItem/resolve`. Phase 3a assumes clangd is on PATH or set via `cpp.clangd.path`, and says so in the status bar when it isn't.

---

## Why this plan looks nothing like spec §4

Spec §4 says *"the single hard-wired LSP connection becomes a small registry"* and *"client machinery (framing, restart policy, completion/hover/diagnostic UI) is shared."* **Both claims are false**, verified by 5-scout recon on 2026-07-15 (master @ `770e100`). Do not plan from the spec's premise. The load-bearing facts:

- There are **THREE** LSP client stacks. Two are capability-aware and multi-server-capable — **and both are dead code, never constructed, never DI-registered**:
  - `VisualGameStudio.ProjectSystem/LSP/LspClient.cs` + `LspClientManager.cs` — **pre-registers clangd at `:167-180`**, which is exactly why the spec assumed the plumbing existed. It is a **trap**.
  - `VisualGameStudio.ProjectSystem/Services/LspClientService.cs` — has tests, no DI registration, no consumers.
  - `VisualGameStudio.ProjectSystem/Services/LanguageService.cs` — **the only one that runs** (`ServiceConfiguration.cs:28`). Single-server, no capability negotiation.
- **`LspClientManager`'s transport would re-ship bugs already paid for**: no BOM guard; reads `Content-Length` **chars** from a UTF-8 reader when the header counts **bytes** (`LspClient.cs:363-372` — this IS the 2026 CRITICAL framing bug, fixed in `LanguageService.cs:603-619`); and **never drains stderr** (`LspClient.cs:55`) — clangd is chatty on stderr, so once the ~4KB pipe buffer fills, **clangd blocks and IntelliSense wedges silently**.
- Therefore: **extend `LanguageService`. Do NOT revive `LspClientManager`.** (User decision, 2026-07-15.)

Full recon lives in the auto-memory file `cpp-phase3-clangd-recon.md`. Spec: `docs/superpowers/specs/2026-07-11-cpp-language-support-design.md`.

## The three silent-failure landmines this plan exists to defuse

Each would cost days, and each fails as **nothing happening** rather than an error:

1. **`rootUri = (string?)null`** (`LanguageService.cs:300`) — clangd resolves `compile_commands.json` from the workspace root. With none it falls back to per-file heuristics and emits garbage. → Task 4 (DONE, `e9d7c3b`) fixed the **protocol layer**. **The IDE layer is only half-delivered — see the ⛔ below.**

   ### ⛔ ROOTLESS AUTOSTART — Task 6/12 MUST handle this. Do NOT assume clangd receives a root.
   Task 4 fixed `BuildInitializeParams` and threaded `workspaceRoot` through `StartAsync` → `StartCoreAsync` → `InitializeAsync` (and persisted it in a field, so auto-restarts don't come back rootless). **But `MainWindowViewModel:544` autostarts the language server in the VIEW-MODEL CONSTRUCTOR — before any project is open — and `StartAsync` does not re-root an already-connected server.** So on the normal path the server runs **rootless for the entire session**. Only the command-palette "Start Language Server" path delivers a real root; Stop→Start is today's only re-root.

   Task 4 took option (a) deliberately and did NOT paper over this. Option (b) (`workspace/didChangeWorkspaceFolders`) requires advertising the `workspace.workspaceFolders` **client capability** — which Task 3's constraint explicitly forbids expanding — and the restart alternative kills the server, loses `didOpen` state, and races in-flight requests. Tasks 6/10 own that area.

   **For BasicLang this is survivable** (its server doesn't need a root). **For clangd it is fatal:** no root → no `compile_commands.json` → garbage diagnostics on every TU, **silently**.

   ### ✅ RESOLVED in Task 6 (`00590cc`) — but NOT the way this plan predicted
   The plan assumed the fix was "move the constructor autostart to `ProjectOpened`". **Task 6 refused, and was right:** that would **regress BasicLang** — a lone `.bas` opened with no project would then get **no server at all** — against Task 6's own criterion that BasicLang behavior be unchanged.

   Instead it made a rootless start **un-expressible**: `StartAllAsync` requires a root and **throws** otherwise, and the constructor autostart reaches only the BasicLang service (through the DI shim), so **it cannot start clangd**. Any server needing a root can only start via `StartAllAsync`. BasicLang keeps the rootless autostart it survives; **clangd cannot start rootless by construction** — closed now, with a test, and with no regression. The `MainWindowViewModel` comment naming Task 6 as this gap's fixer was corrected rather than left as a lie in the code.

   ⚠ **What remains is the SECOND project, not the first** — see Task 12's ⛔ PROJECT SWITCH box.
2. **Capabilities discarded** — `initialize`'s result is thrown away (`LanguageService.cs:317`); there is no `Capabilities` member anywhere; every feature method is wrapped in `catch { return Array.Empty<>(); }`. clangd failing looks identical to clangd working on an empty file. → Task 3.
3. **Position encoding is accidentally correct and structurally unpinned** — `character = column - 1` works only because AvaloniaEdit's `Caret.Column` is UTF-16 code units and LSP defaults to UTF-16. `positionEncoding`/`offsetEncoding` have **zero matches repo-wide**. Anyone adding the widely-copy-pasted `--offset-encoding=utf-8` clangd flag shifts every position on every non-ASCII line, with no test to catch it. → Task 3 pins it deliberately (DONE, `5ada16f`); **never pass `--offset-encoding`**.

   ### ⛔ UNRESOLVED — clangd's non-standard `offsetEncoding` (verify in Task 11/12 BEFORE trusting the pin)
   Task 3's code review raised this at **moderate confidence, unverified against a real clangd binary** — nobody has run one yet (it isn't installed on this machine). **Do not treat it as settled either way; go and check.**

   clangd historically negotiates encoding via a **non-standard `offsetEncoding`** field that predates LSP 3.17's standard `positionEncoding`, and reports it in its initialize result. Task 3's parser reads **only** `positionEncoding`, and defaults a missing value to `utf-16` (correct per LSP 3.17 — omitted *means* utf-16).

   **The hazard:** if clangd replies `offsetEncoding: "utf-8"` and omits `positionEncoding`, `ParseServerCapabilities` silently returns `"utf-16"` — **reconstructing landmine #3 exactly, for the exact server this phase targets.** Every position on every non-ASCII line would be wrong, silently, and the capability we added to detect it would report all-clear.

   **What Task 11/12 must do:** with a real clangd, dump its raw initialize result. If it sends `offsetEncoding`, the parser must read it too and `ServerCapabilities` must expose it — and a mismatch (server says utf-8, client is utf-16-only) must FAIL LOUDLY, not default. Note that today's only defense is a code comment saying "don't pass the flag" — which is precisely the class of protection this plan's own philosophy rejects.

## Repo rules that will bite you

- **⚠ THE COMPILER WILL NOT CATCH NULLABLE MISUSE.** `CS8604` ("possible null reference argument") is in `NoWarn` in **all four** relevant projects: `VisualGameStudio.Core.csproj:10`, `VisualGameStudio.Shell.csproj:13`, `VisualGameStudio.ProjectSystem.csproj:10`, `VisualGameStudio.Editor.csproj:10`. Passing a `string?` into a `string` parameter compiles **silently — not even a warning**. So `string?` annotations here are documentation, not enforcement. This matters most in Task 7 (~26 call sites): naming, tests, and review are the ONLY guards. (Found by Task 2's code review; scope widened from 2 to 4 projects when Task 2's implementer verified it.)
- **⚠ GREP CANNOT PROVE DEAD CODE IN THIS REPO.** Task 1 learned this the hard way: `ILspClientService.cs` — verified "100% dead" by 5 scouts and 2 adversarial reviewers — declared a live enum into the *same namespace* as a live file, so it bound implicitly with **no `using` directive and no textual reference to find**. Only the compiler knows. Tasks 5–7 move/delete more files in `Core/Abstractions/Services` — build, don't just grep.
- **PowerShell is the shell.** Use Read/Edit/Write/Grep/Glob — a PreToolUse hook blocks `grep`/`cat`/`find`/`sed` via Bash.
- **The full suite takes ~14-15 min and its output EXCEEDS the PowerShell tool's 30000-char truncation**, which silently eats the summary line and makes a normal run look like a crash. Redirect to a file and read the tail.
- **NEVER round-trip repo files through `Get-Content`/`Set-Content`** — it corrupts these BOM-less UTF-8 files (has caused mojibake 3×). Use Edit/Write. For multi-line commit messages, write a file and `git commit -F`.
- **After AXAML changes, `dotnet clean` before building** — stale cache causes crashes.
- **Test BOTH entry points.** The IDE build delegates to the CLI engine; a fix verified through one can break the other.
- **MSIL/LLVM are OUT OF SCOPE** (user, 2026-07-15). Do not test or fix them.
- Build: `dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release`
- Test: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release`
- Single test: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~TestName"`

**Baseline:** every line/`:NNN` reference below was captured at master `770e100`. Line numbers drift as you edit — **grep for the symbol, don't trust the number**.

---

## File Structure

**Create:**
| File | Responsibility |
|---|---|
| `VisualGameStudio.Core/Utilities/LanguageFileTypes.cs` | THE single source of truth: extension → languageId. Absorbs the drifted copies. |
| `VisualGameStudio.Core/Abstractions/Services/ILanguageServiceRegistry.cs` | `GetFor(path)`, `IsConnectedFor(path)`, `StartAllAsync`, `StopAllAsync`. |
| `VisualGameStudio.ProjectSystem/Services/LanguageServiceRegistry.cs` | Owns N `LanguageService` instances; DI singleton so Dispose cascades. |
| `VisualGameStudio.Core/Abstractions/Services/LanguageServerDescriptor.cs` | Per-server identity: id, display name, extensions, launch, languageId, settings key. |
| `VisualGameStudio.Core/Utilities/ExecutableLocator.cs` | Real PATH search returning an ABSOLUTE path (lifted from `ShellProfileDetector.FindOnPath`). |
| `VisualGameStudio.ProjectSystem/Services/ClangdLocator.cs` | setting override → PATH probe. (LLVM dir probe = Phase 3b.) |
| `BasicLang/ProjectSystem/IntelliSenseEmitter.cs` | Toolchain-free `obj/gen` + `compile_commands.json` emission. |
| `VisualGameStudio.Tests/LSP/LanguageServiceRegistryTests.cs` | Routing + per-server isolation. |
| `VisualGameStudio.Tests/LSP/CapabilityNegotiationTests.cs` | initialize result captured; positionEncoding pinned. |
| `VisualGameStudio.Tests/Core/LanguageFileTypesTests.cs` | Extension map, incl. the drift cases. |
| `VisualGameStudio.Tests/Compiler/IntelliSenseEmitterTests.cs` | Emission without a toolchain; wipe-on-throw. |
| `VisualGameStudio.Tests/LSP/ClangdE2ETests.cs` | clangd-conditional end-to-end. |

**Modify:**
| File | Change |
|---|---|
| `VisualGameStudio.ProjectSystem/Services/LanguageService.cs` | Parameterize the 3 BasicLang spots; add rootUri + capabilities. |
| `VisualGameStudio.Core/Abstractions/Services/ILanguageService.cs` | Add `Capabilities`, `Descriptor`. |
| `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs` | 24 gates → routing; remove `GetLanguageIdForFile`; fix outline bug. |
| `VisualGameStudio.Editor/Controls/CodeEditorControl.axaml.cs` | 2 gates → routing. |
| `VisualGameStudio.Shell/Configuration/ServiceConfiguration.cs` | Register the registry. |
| `BasicLang/ProjectSystem/CppProjectBuilder.cs` | Extract emit core; fix wipe-on-throw. |
| `VisualGameStudio.Shell/ViewModels/Dialogs/SettingsViewModel.cs` + `SettingsService.cs` | `cpp.clangd.path`. |
| `VisualGameStudio.Tests/SettingsConsumerContractTests.cs` | Forcing line for the new consumer. |
| `docs/comparisons/ide-parity-scorecard.md:106` | It falsely claims multi-language LSP is "DONE". |

**Delete:** `ProjectSystem/LSP/LspClient.cs`, `ProjectSystem/LSP/LspClientManager.cs`, `Core/LSP/ILspClient.cs`, `ProjectSystem/Services/LspClientService.cs`, `Core/Abstractions/Services/ILspClientService.cs`, `VisualGameStudio.Tests/.../LspClientServiceTests.cs`.

---

## Task 1: Delete the dead LSP stacks + fix the scorecard lie

**Why first:** these files are a trap. `LspClientManager.cs:167-180` pre-registers clangd, and the scorecard marks multi-language LSP "DONE" — together they already misled the spec. Removing them before building the real seam stops a later subagent from "helpfully" wiring into a corpse. **Steal their good ideas first** (below), then delete.

**Files:**
- Delete: `VisualGameStudio.ProjectSystem/LSP/LspClient.cs`, `VisualGameStudio.ProjectSystem/LSP/LspClientManager.cs`, `VisualGameStudio.Core/LSP/ILspClient.cs`, `VisualGameStudio.ProjectSystem/Services/LspClientService.cs`, `VisualGameStudio.Core/Abstractions/Services/ILspClientService.cs`
- Delete: the corresponding test file(s) — grep for `LspClientServiceTests`
- Modify: `docs/comparisons/ide-parity-scorecard.md:106`

- [ ] **Step 1: Harvest the good ideas into a scratch note before deleting**

Read and record (you will need these in Tasks 5–6):
- `Core/Extensions/LanguageServerConfig.cs` — the config shape (a **class**, not a record): `Id`, `Name`, `LanguageIds`, `FileExtensions`, `StartInfo`, `TransportType`. **Keep this file.** Verified safe to keep while deleting `ILspClient.cs`: it self-declares `LanguageServerConfig`/`ServerStartInfo`/`TransportType`/`DebugAdapterConfig` in `VisualGameStudio.Core.Extensions` with **no dependency on `ILspClient`**, and `ExtensionManager.cs`/`IExtensionManager.cs` reference only it. It informs the descriptor in Task 5.
- `LspClientService.cs:571-604, 687-702` — byte-exact framing on raw `BaseStream`s with a write lock, immune to encoding bugs by construction. Compare against `LspFrameWriter.cs`.
- `LspClientManager.cs` — per-server `SemaphoreSlim`, `StopAllAsync`, and `LspDiagnosticsEventArgs.LanguageId` (server identity on the diagnostics payload — the live path lacks this).

- [ ] **Step 2: Prove they are unreferenced**

```
Grep pattern: LspClientManager|ILspClientManager|ILspClientService|LspClientService|ILspClient
Expect: matches ONLY inside the files being deleted, their tests, and docs.
```
`Core/LSP/ILspClient.cs` also declares a **duplicate model tree** (`CompletionItem`, `DocumentSymbol`, `SymbolKind`, `SignatureHelp`, `Hover`, `Location`, `TextEdit`, `WorkspaceEdit`, `DiagnosticSeverity`, `CompletionItemKind`). The UI binds the `Core/Abstractions/Services` copies. **Before deleting, grep each type name** to confirm no live consumer binds the `Core.LSP` copy. If one does, STOP and report — that changes the task.

- [ ] **Step 3: Delete the files and their tests**

- [ ] **Step 4: Build — expect 0 errors**

Run: `dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release`
Expected: 0 errors. Any error means something DID reference them — do not "fix" it by re-adding; report.

- [ ] **Step 5: Fix the false doc claims**

- `docs/comparisons/ide-parity-scorecard.md:106` reads *"Multi-language LSP | Yes | Yes | DONE | `LspClientManager.cs` supports 12 languages"*. **Every part of that is false** — the file is dead and supports nothing. Change it to reality: BasicLang only today; clangd routing is Phase 3a, in progress.
- `IDEAgent/ide_agent.py` (`:58, :74, :457, :513`) refers to `ILspClientService` in prose. Python, not compiled, so it won't break the build — but it's the same trap for the next reader. Correct or drop those references.

- [ ] **Step 6: Full suite**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release`
Expected: same pass count as baseline **minus** the deleted `LspClientServiceTests`. Record both numbers.

- [ ] **Step 7: Commit**

```
git add -A
git commit -m "chore(lsp): delete three dead LSP client stacks and correct the parity scorecard"
```

---

## Task 2: One source of truth for extension → languageId

**Why:** the extension lists **already disagree** across the codebase, and Phase 3a multiplies the cost of that drift. Verified:
- `HighlightingLoader.CppExtensions` includes `.inl`; `LspClientManager`'s clangd config did not.
- `MainWindowViewModel.GetLanguageIdForFile` (`:7815-7853`) maps `.cpp`/`.h` → `"cpp"` **but is used only by the extension host, never LSP**, omits `.hh`/`.hxx`/`.inl`, and omits `.mod`/`.cls`/`.class` from `"basiclang"` — they fall through `_ => ext.TrimStart('.')` yielding the junk ids `"mod"` / `"cls"`.
- `BasicLangFileTypes.cs:11-14` lists `.bas .bl .mod .cls .class`.

### ⛔ READ THIS FIRST — there are TWO maps, not one. Do not merge them.

An earlier draft of this plan said "delete `GetLanguageIdForFile`". **That was wrong and would have broken the extension host.** The adversarial claims review caught it. The two functions are not the same function:

| | **LSP routing** (new) | **Extension host** (existing) |
|---|---|---|
| Name | `LanguageFileTypes.GetLanguageId(path)` | `LanguageFileTypes.GetEditorLanguageId(path)` |
| Domain | basiclang + cpp **only** | **~30 languages** (html, css, js, jsx, ts, tsx, json, markdown, xml, yaml, python, rust, go, csharp, java, lua, sql, shellscript, powershell, php, ruby, swift, kotlin, …) |
| Unknown input | **returns `null`** ("no server owns this") | **never null** — falls back to `ext.TrimStart('.')`, then `"plaintext"` (`MainWindowViewModel.cs:7851`) |
| Consumers | the registry (Task 6) | `_extensionService` (3 sites) |

**If you route the extension host through the nullable map, you break Python/TypeScript/JSON/Markdown hover and completion.** `MainWindowViewModel.cs:2063` and `:2186` call `_extensionService.HasExtensionProviders(GetLanguageIdForFile(...))`, which lands on `ExtensionService.cs:627`:
```csharp
public bool HasExtensionProviders(string languageId) => _extensionProviderLanguages.ContainsKey(languageId);
```
**`ContainsKey(null)` throws `ArgumentNullException`.** Both `IExtensionService.HasExtensionProviders` (`:148`) and `NotifyDocumentOpenedAsync` (`:153`) declare **non-nullable** `string languageId`, and `MainWindowViewModel.cs:1913` comments that the host is notified *"for all file types"*.

**So: move BOTH maps into `LanguageFileTypes`, keep them as two distinct functions, and change only the `.mod`/`.cls`/`.class` and C++ arms.**

**Files:**
- Create: `VisualGameStudio.Core/Utilities/LanguageFileTypes.cs`
- Create: `VisualGameStudio.Tests/Core/LanguageFileTypesTests.cs`
- Modify: `VisualGameStudio.Core/Utilities/BasicLangFileTypes.cs` (delegate to the new map; keep the public API — it has ~26 callers and its own passing tests)
- Modify: `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs` (delete `GetLanguageIdForFile`, call the shared one)

- [ ] **Step 1: Write the failing tests**

```csharp
[TestFixture]
public class LanguageFileTypesTests
{
    [TestCase("a.bas", "basiclang")]
    [TestCase("a.bl", "basiclang")]
    [TestCase("a.mod", "basiclang")]     // regression: used to yield "mod"
    [TestCase("a.cls", "basiclang")]     // regression: used to yield "cls"
    [TestCase("a.class", "basiclang")]
    [TestCase("a.cpp", "cpp")]
    [TestCase("a.cc", "cpp")]
    [TestCase("a.cxx", "cpp")]
    [TestCase("a.hpp", "cpp")]
    [TestCase("a.hh", "cpp")]            // regression: was omitted
    [TestCase("a.hxx", "cpp")]           // regression: was omitted
    [TestCase("a.inl", "cpp")]           // regression: only HighlightingLoader knew this one
    [TestCase("A.CPP", "cpp")]           // case-insensitive
    public void GetLanguageId_MapsKnownExtensions(string path, string expected)
        => Assert.That(LanguageFileTypes.GetLanguageId(path), Is.EqualTo(expected));

    // `.h` is C++ here BY DECISION (spec §4 assigns .h to clangd). Pin it so nobody "fixes" it to "c".
    [Test]
    public void GetLanguageId_DotH_IsCpp_ByDecision()
        => Assert.That(LanguageFileTypes.GetLanguageId("a.h"), Is.EqualTo("cpp"));

    // The LSP map is nullable ON PURPOSE: null means "no language server owns this file".
    [TestCase("a.txt")]
    [TestCase("a.json")]
    [TestCase("noextension")]
    [TestCase("")]
    [TestCase(null)]
    public void GetLanguageId_UnknownOrEmpty_ReturnsNull(string? path)
        => Assert.That(LanguageFileTypes.GetLanguageId(path), Is.Null);

    // ---- The EXTENSION-HOST map is a DIFFERENT, TOTAL function. See the ⛔ box above. ----

    [TestCase("a.py", "python")]
    [TestCase("a.ts", "typescript")]
    [TestCase("a.json", "json")]
    [TestCase("a.md", "markdown")]
    [TestCase("a.cs", "csharp")]
    public void GetEditorLanguageId_KeepsTheThirtyLanguageMap(string path, string expected)
        => Assert.That(LanguageFileTypes.GetEditorLanguageId(path), Is.EqualTo(expected));

    // THE regression guard. ExtensionService.HasExtensionProviders does ContainsKey(languageId)
    // and throws ArgumentNullException on null. This must NEVER return null.
    [TestCase("a.txt")]
    [TestCase("a.wildlyunknown")]
    [TestCase("noextension")]
    [TestCase("")]
    [TestCase(null)]
    public void GetEditorLanguageId_IsTotal_NeverReturnsNull(string? path)
        => Assert.That(LanguageFileTypes.GetEditorLanguageId(path), Is.Not.Null);

    // The drift fix: these used to yield the junk ids "mod"/"cls" via `_ => ext.TrimStart('.')`.
    [TestCase("a.mod")]
    [TestCase("a.cls")]
    [TestCase("a.class")]
    public void GetEditorLanguageId_BasicLangExtensions_AreBasicLang_NotJunk(string path)
        => Assert.That(LanguageFileTypes.GetEditorLanguageId(path), Is.EqualTo("basiclang"));

    // Where both maps DO agree, they must not drift apart again.
    [TestCase("a.bas")]
    [TestCase("a.cpp")]
    [TestCase("a.h")]
    public void TheTwoMaps_AgreeOnLanguagesTheyBothKnow(string path)
        => Assert.That(LanguageFileTypes.GetEditorLanguageId(path),
                       Is.EqualTo(LanguageFileTypes.GetLanguageId(path)));

    [Test]
    public void IsCppSourceFile_And_IsBasicLangSourceFile_AreDisjoint()
    {
        foreach (var ext in LanguageFileTypes.AllKnownExtensions)
        {
            var p = "f" + ext;
            Assert.That(LanguageFileTypes.IsCppSourceFile(p) && BasicLangFileTypes.IsBasicLangSourceFile(p),
                Is.False, $"{ext} claimed by both languages");
        }
    }

    // BasicLangFileTypes must keep answering exactly as before — ~26 call sites depend on it.
    [TestCase("a.bas", true)]
    [TestCase("a.cpp", false)]
    [TestCase("a.txt", false)]
    public void BasicLangFileTypes_BehaviorUnchanged(string path, bool expected)
        => Assert.That(BasicLangFileTypes.IsBasicLangSourceFile(path), Is.EqualTo(expected));
}
```

- [ ] **Step 2: Run — expect FAIL (type does not exist)**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~LanguageFileTypesTests"`

- [ ] **Step 3: Implement `LanguageFileTypes` with BOTH functions**

- `GetLanguageId(path)` → `string?` — LSP routing. basiclang + cpp only. Null for anything else.
- `GetEditorLanguageId(path)` → `string` — **total**, never null. Move the ~30-language body of `MainWindowViewModel.GetLanguageIdForFile` (`:7815-7853`) here **verbatim**, including its `_ => ext?.TrimStart('.') ?? "plaintext"` fallback (`:7851`), then change only two things: route `.mod`/`.cls`/`.class` → `"basiclang"`, and add the missing C++ extensions (`.hh`, `.hxx`, `.inl`).
- `AllKnownExtensions` for the disjointness test.
- `BasicLangFileTypes` delegates here rather than keeping a second list — **keep its public API unchanged** (26 production callers + its own green tests). ⚠ `IsBasicLangSourceFile` currently uses `EndsWith`, not `Path.GetExtension`; delegation is safe (verified — `BasicLangFileTypesTests.cs:47-51` pins nothing that breaks), but keep its tests green as the proof.
- `.c` → `"c"` is intentionally NOT in the LSP map for Phase 3a. Keep it minimal and honest.

- [ ] **Step 4: Run — expect PASS.** Also run `--filter "FullyQualifiedName~BasicLangFileTypesTests"` — the existing suite must stay green.

- [ ] **Step 5: Repoint the callers — do NOT delete the ~30-language map**

- **Extension host (3 sites)** — `MainWindowViewModel.cs:1916` (`NotifyDocumentOpenedAsync`), `:2063` and `:2186` (`HasExtensionProviders`): repoint to **`LanguageFileTypes.GetEditorLanguageId`** (the total one). Then delete the now-empty `GetLanguageIdForFile` private method.
- **Nothing routes to `GetLanguageId` yet** — the registry (Task 6) is its first consumer. That is expected; it is dead until then.

⚠ The only intended behavior change is `.mod`/`.cls`/`.class` → `"basiclang"` instead of the junk `"mod"`/`"cls"`. Grep the extension host for consumers of `"mod"`/`"cls"` language ids; if any exist, report before proceeding.

- [ ] **Step 5b: Prove you didn't break the extension host**

```
Grep: HasExtensionProviders|NotifyDocumentOpenedAsync
For EVERY hit, confirm the languageId argument comes from GetEditorLanguageId (total), NEVER GetLanguageId (nullable).
```
A null reaching `ExtensionService.cs:627` throws `ArgumentNullException` at runtime — the compiler will NOT catch this, because `GetLanguageId` returns `string?` into a `string` parameter only as a nullable-warning, and this repo builds with warnings non-fatal.

- [ ] **Step 6: Build + full suite — expect green**

- [ ] **Step 7: Commit**

```
git add -A
git commit -m "refactor(lsp): single source of truth for extension to languageId mapping"
```

---

## Task 3: Capability negotiation + pin position encoding

**Why:** `LanguageService.InitializeAsync` (`:295-319`) fires `initialize` and **discards the result** at `:317`. There is no `Capabilities` member on `ILanguageService`. Every feature method is guarded only by `if (!IsConnected)` and wrapped in `catch { return Array.Empty<>(); }` — so clangd not supporting something is **indistinguishable from clangd working on an empty file**. This is landmine #2.

**Files:**
- Modify: `VisualGameStudio.Core/Abstractions/Services/ILanguageService.cs` (add `ServerCapabilities? Capabilities`)
- Modify: `VisualGameStudio.ProjectSystem/Services/LanguageService.cs:295-319`
- Create: `VisualGameStudio.Tests/LSP/CapabilityNegotiationTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// Parse-level tests — no real server needed. Test the pure parser, not the process.
[Test]
public void ParseServerCapabilities_ReadsCompletionAndHoverProviders()
{
    var json = """
    {"capabilities":{"completionProvider":{"resolveProvider":true},
                     "hoverProvider":true,"definitionProvider":true,
                     "positionEncoding":"utf-16"}}
    """;
    var caps = LanguageService.ParseServerCapabilities(json);
    Assert.Multiple(() =>
    {
        Assert.That(caps.HasCompletionProvider, Is.True);
        Assert.That(caps.HasCompletionResolveProvider, Is.True);
        Assert.That(caps.HasHoverProvider, Is.True);
        Assert.That(caps.PositionEncoding, Is.EqualTo("utf-16"));
    });
}
```

> **⚠ ERRATUM (found during execution — the plan was wrong here).** This snippet originally commented
> `CompletionResolveProvider` as *"clangd = true, BasicLang = false"*. **False.** That described
> `SimpleLspServer` (`resolveProvider:false` at `:279`), which only runs under **`--lsp-simple`** and which
> the client NEVER launches. The live `--lsp` server is the OmniSharp `BasicLangLanguageServer`
> (`Program.cs:41-47`) and it returns `completionProvider.resolveProvider: **true**`. Anyone reasoning
> about BasicLang's real capabilities from the original comment would be wrong.
>
> Two more facts settled by probing the live handshake (do not re-derive):
> - The real server sends `"hoverProvider": {}` — an **empty object**, not a boolean (likewise
>   `definitionProvider`/`referencesProvider`/`documentSymbolProvider`). So `boolean | XxxOptions` union
>   handling is **required, not defensive** — booleans-only parsing reports every capability false.
>   `SimpleLspServer`'s booleans are real but unreachable from the live tests.
> - The real server sends **no** `positionEncoding` → utf-16-by-default is genuinely the live path.
>
> Also renamed during review: `CompletionResolveProvider` → `HasCompletionResolveProvider` (its six
> siblings all carry `Has`), and `ParseServerCapabilities` now takes a **`JsonElement`**, not a string —
> the string signature forced the `Undefined` guard out to the call site, which Task 12's clangd call site
> would have had to remember independently. A thin `string` overload is retained for tests.

```csharp

[Test]
public void ParseServerCapabilities_MissingProviders_AreFalse_NotThrow()
{
    var caps = LanguageService.ParseServerCapabilities("""{"capabilities":{}}""");
    Assert.That(caps.HasCompletionProvider, Is.False);
}

[Test]
public void ParseServerCapabilities_Malformed_ReturnsEmptyCapabilities_NotThrow()
{
    Assert.DoesNotThrow(() => LanguageService.ParseServerCapabilities("not json"));
}

// THE PIN. See the plan's landmine #3.
[Test]
public void ClientCapabilities_AdvertisePositionEncoding_Utf16_Only()
{
    var json = JsonSerializer.Serialize(LanguageService.BuildClientCapabilities());
    Assert.That(json, Does.Contain("utf-16"),
        "LSP positions are UTF-16 code units; AvaloniaEdit Caret.Column matches ONLY utf-16. " +
        "If this ever becomes utf-8, every position on every non-ASCII line shifts silently.");
    Assert.That(json, Does.Not.Contain("utf-8"));
}
```

- [ ] **Step 2: Run — expect FAIL**

- [ ] **Step 3: Implement**

- Add a `ServerCapabilities` record to `Core/Abstractions/Services/`.
- Extract `public static ServerCapabilities ParseServerCapabilities(string json)` and `public static object BuildClientCapabilities()` as **pure static** methods — mirrors the existing testability idiom of `ResolveLspPathOverride` (`LanguageService.cs:106-112`) and `PathToUri` (`:887`).
- At `:317`, capture the result. ⚠ **The plan's original one-liner does NOT compile:** `SendRequestAsync` returns `Task<JsonElement>`, not `Task<string>`. Keep the string seam the tests specify and pass `initResult.GetRawText()` — but **guard `JsonValueKind.Undefined` first**: `ProcessMessage:744` does `tcs.SetResult(default)` for a response with no `result` member, and `GetRawText()` on that **throws**. (Found by Task 3's implementer.)
- In `BuildClientCapabilities`, add `general: { positionEncodings: ["utf-16"] }`.
- **Null out `Capabilities` before every (re)negotiation** so an auto-restart whose handshake times out cannot leave the previous connection's capabilities readable as current — otherwise landmine #2 returns by the back door.

**⚠ DO NOT pass `--offset-encoding=utf-8` to clangd in Task 12.** Leave a comment at the launch site saying why.

- [ ] **Step 4: Run — expect PASS**

- [ ] **Step 5: Full suite — BasicLang behavior must be unchanged** (BasicLang's `initialize` result is now parsed instead of dropped; nothing should branch on it yet)

- [ ] **Step 6: Commit**

```
git add -A
git commit -m "feat(lsp): capture server capabilities from initialize and pin utf-16 position encoding"
```

---

## Task 4: Workspace root (rootUri / rootPath / workspaceFolders + WorkingDirectory)

**Why:** landmine #1. `LanguageService.cs:300` sends `rootUri = (string?)null`, no `rootPath`, no `workspaceFolders`, and `ProcessStartInfo` sets no `WorkingDirectory` (`:145-160`) — the server inherits the IDE's cwd. clangd cannot find `compile_commands.json` without a root and will emit garbage diagnostics for every TU. (The now-deleted `LspClient.cs:90, 145-148` was the only code that sent these — the live client is the regressed one.)

**Files:**
- Modify: `VisualGameStudio.ProjectSystem/Services/LanguageService.cs:145-160` (WorkingDirectory), `:295-319` (rootUri/rootPath/workspaceFolders)
- Modify: `VisualGameStudio.Core/Abstractions/Services/ILanguageService.cs` — ⚠ the CURRENT signature is `StartAsync(CancellationToken cancellationToken = default)`. The combined signature is **`StartAsync(string? workspaceRoot = null, CancellationToken cancellationToken = default)`** — both optional, so the existing callers at `MainWindowViewModel.cs:544` and `:2704` keep compiling unchanged.
- Test: `VisualGameStudio.Tests/LSP/CapabilityNegotiationTests.cs`

- [ ] **Step 1: Write the failing tests** — assert `BuildInitializeParams(workspaceRoot)` emits `rootUri` as a `file://` URI (reuse `PathToUri` at `:887`), `rootPath`, and a `workspaceFolders` entry; and that a null root omits them rather than emitting `"null"`.

- [ ] **Step 2: Run — expect FAIL**

- [ ] **Step 3: Implement.** Extract `BuildInitializeParams(string? workspaceRoot)` as a pure static. Thread `workspaceRoot` from `StartAsync` → `StartCoreAsync` → `InitializeAsync`, and set `ProcessStartInfo.WorkingDirectory = workspaceRoot` when non-null.

- [ ] **Step 4: Run — expect PASS**

- [ ] **Step 5: Pass the real root from the IDE.** `MainWindowViewModel` starts the service at `:544` and `:2704`; the project root is available via `IProjectService.ProjectOpened` (`ProjectService.cs:129`, subscribed at `MainWindowViewModel.cs:490`). BasicLang server behavior with a real root must be verified unchanged — run the LSP tests.

- [ ] **Step 6: Full suite**

- [ ] **Step 7: Commit**

```
git add -A
git commit -m "feat(lsp): send workspace root (rootUri/rootPath/workspaceFolders) on initialize"
```

---

## Task 5: Parameterize `LanguageService` — extract server identity into a descriptor

**Why:** BasicLang-ness lives in exactly **three** places, and everything else in the ~30-method `ILanguageService` surface is already server-agnostic (every method keyed by uri + position; nothing names BasicLang). The three:
1. compiler-path probe + `basiclang.lsp.path` (`LanguageService.cs:63-98`, resolver `:106-112`)
2. `FileName="dotnet"`, `Arguments = $"\"{_compilerPath}\" --lsp"` (`:145-160`)
3. `languageId = "basiclang"` hardcoded in `OpenDocumentAsync` (`:331`)

### ⚠ D1 — DECIDE THIS HERE, or Task 12 forces you to rework a committed task

clangd needs `--compile-commands-dir=<projectDir>/obj`, which is **project-scoped**. But Task 6 constructs services through **DI at app startup**, when no project is open and `<projectDir>` is unknowable. Two ways out:

- **(a) CHOSEN — derive it inside `BuildStartInfo` from `workspaceRoot`.** The descriptor stays pure identity (no project state); `StartAsync(workspaceRoot)` already carries the root as of Task 4, and `BuildStartInfo` computes `Path.Combine(workspaceRoot, "obj")`. DI construction stays valid, and the Task 6 disposal guarantee is preserved.
- (b) Rejected: construct clangd lazily at project open — that puts the instance outside the container, which is **exactly** the never-disposed / orphaned-process bug Task 6 exists to prevent.

The Task 5 tests below pin (a) — `Clangd(...)` is a **1-arg** factory. `obj` must match `CompileCommandsWriter`'s output dir (`CompileCommandsWriter.cs:17, 31` → `<projectDir>/obj/compile_commands.json`) — if that ever moves, both move together.

**Files:**
- Create: `VisualGameStudio.Core/Abstractions/Services/LanguageServerDescriptor.cs`
- Modify: `VisualGameStudio.ProjectSystem/Services/LanguageService.cs`
- Modify: `VisualGameStudio.Shell/Configuration/ServiceConfiguration.cs:28` — `AddSingleton<ILanguageService, LanguageService>` cannot resolve a descriptor from the container. **Give the ctor param a default (BasicLang) so Tasks 5 and 6 stay independently green**; Task 6 replaces the registration properly.
- Modify: `VisualGameStudio.Tests/SettingsConsumerContractTests.cs:77` — `_ = new LanguageService(...)` is the forcing line and breaks on a ctor change for the same reason.
- Test: `VisualGameStudio.Tests/LSP/LanguageServiceRegistryTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// ⚠ ERRATUM — as originally written this test CANNOT PASS. Task 4 added a
// `Directory.Exists` guard, and `C:\proj` doesn't exist, so WorkingDirectory is null.
// BuildStartInfo therefore needs the injectable `directoryExists` seam (matching
// `ResolveWorkingDirectory`/`ResolveLspPathOverride`); tests pass `_ => true`.
[Test]
public void Descriptor_BasicLang_ProducesUnchangedStartInfo()
{
    var d = LanguageServerDescriptor.BasicLang(compilerPath: @"C:\x\BasicLang.dll");
    var psi = LanguageService.BuildStartInfo(d, workspaceRoot: @"C:\proj", directoryExists: _ => true);
    Assert.Multiple(() =>
    {
        Assert.That(psi.FileName, Is.EqualTo("dotnet"));
        Assert.That(psi.Arguments, Is.EqualTo(@"""C:\x\BasicLang.dll"" --lsp"));
        Assert.That(psi.WorkingDirectory, Is.EqualTo(@"C:\proj"));
        Assert.That(psi.RedirectStandardError, Is.True, "stderr MUST be drained or the server wedges");
    });
}

// D1 (decided — see the box above): the descriptor holds NO project-scoped state.
// --compile-commands-dir is DERIVED from workspaceRoot at BuildStartInfo time.
//
// ⚠ ERRATUM — `Does.Contain` HERE IS A BUG, AND IT WAS CAUGHT BY MUTATION TESTING.
// Task 5's implementer deliberately broke clangd's argument QUOTING and this assertion
// STILL PASSED. Real project paths contain spaces; CommandLineToArgvW would split the
// token and clangd would ignore the fragment — SILENTLY. Assert the exact token, not a
// substring. This is the same lesson Task 3's review taught about `positionEncodings`:
// substring assertions pass while the thing under test is broken. Do not use Does.Contain
// for anything whose exact bytes reach a process or a wire.
[Test]
public void Descriptor_Clangd_DerivesCompileCommandsDirFromWorkspaceRoot()
{
    var d = LanguageServerDescriptor.Clangd(clangdPath: @"C:\llvm\clangd.exe");   // no project state
    var psi = LanguageService.BuildStartInfo(d, workspaceRoot: @"C:\proj", directoryExists: _ => true);
    Assert.That(psi.FileName, Is.EqualTo(@"C:\llvm\clangd.exe"));
    // exact-token, quoting-sensitive — NOT Does.Contain. A path with spaces must survive argv splitting.
    Assert.That(psi.Arguments, Is.EqualTo(@"--compile-commands-dir=C:\proj\obj"));
    Assert.That(psi.Arguments, Does.Not.Contain("--offset-encoding"),
        "Never pass --offset-encoding: the client's column math is utf-16 only. See plan landmine #3.");
}

[Test]
public void Descriptor_LanguageId_ComesFromTheFile_NotAConstant()
{
    Assert.That(LanguageServerDescriptor.BasicLang("x").LanguageIdFor("a.bas"), Is.EqualTo("basiclang"));
    Assert.That(LanguageServerDescriptor.Clangd("x").LanguageIdFor("a.cpp"), Is.EqualTo("cpp"));   // 1-arg per D1
}
```

- [ ] **Step 2: Run — expect FAIL**

- [ ] **Step 3: Implement.** `LanguageServerDescriptor` = `Id`, `DisplayName`, `Extensions`, launch inputs, `LanguageIdFor(path)` (delegating to **`LanguageFileTypes.GetLspLanguageId`** — note: renamed from `GetLanguageId` during Task 2's review, because the unqualified name was the *inverted default*: it belonged to the narrow/nullable/dangerous function while the safe total one was qualified. Do NOT use `GetEditorLanguageId` here — that's the extension host's total map), `SettingsKey`. Static factories `BasicLang(compilerPath)` / `Clangd(clangdPath)` — **neither takes project-scoped state (D1)**. Extract `LanguageService.BuildStartInfo(descriptor, workspaceRoot)` as a **pure static**. Replace the hardcoded `languageId = "basiclang"` constant (**`:330`** — the plan originally said `:331`, which is `version = 1`; grep for the literal rather than trusting either) with `_descriptor.LanguageIdFor(path)`. `LanguageService` takes a descriptor in its ctor **with a BasicLang default** so `ServiceConfiguration.cs:28` and `SettingsConsumerContractTests.cs:77` keep compiling until Task 6.

⚠ **Preserve the hardening at `:154-159` (BOM-less `StandardInputEncoding`), `:165-177` (stderr drain), `:183-187` (Latin1 framing reader).** These are the 2026 CRITICAL fixes. Any new start path must inherit all three. The stderr drain is not optional for clangd.

- [ ] **Step 4: Run — expect PASS**

- [ ] **Step 5: Full suite — BasicLang must be byte-identical in behavior**

- [ ] **Step 6: Commit**

```
git add -A
git commit -m "refactor(lsp): extract server identity into LanguageServerDescriptor"
```

---

## Task 6: `ILanguageServiceRegistry` — N servers, per-server state

**Why:** every piece of connection state today is single-server and would be corrupted by a second: `_serverProcess`, `_writer`, `_reader`, `_readTask`, `_cts`, `_requestId` + `_pendingRequests` (one id space!), `_lock`, `_frameWriter`, `_compilerPath`, `IsConnected` (`:45`), `ServerProcessId`, `_stopping`/`_disposed`/`_disconnectHandled`, and **`_restartPolicy` (`:41`) — one restart budget, so a crash-looping clangd would exhaust BasicLang's**. Instantiating one `LanguageService` per descriptor solves all of these for free; the registry only routes.

**⚠ DI is load-bearing.** The orphan-on-exit fix is `App.axaml.cs:129` `(Services as IDisposable)?.Dispose()`. A registry that lazily `new`s a service **outside the container is never disposed and re-orphans clangd on every exit** — exactly the bug `LspClientManager.GetClientAsync` would have shipped. The registry MUST be the DI singleton and MUST dispose its children.

### ⚠ Two things Task 5 hands you — read before starting

1. **`Descriptor` lives on the concrete `LanguageService`, NOT on `ILanguageService`.** Task 5 deliberately left the interface alone (no consumer existed yet). **Your own test below does `GetFor(path)!.Descriptor.Id`, so YOU must lift it to the interface.** Add `ILanguageService.cs` to your file list.
2. **The rootless-autostart decision lands here** (see landmine #1's ⛔ box). `MainWindowViewModel:553` autostarts the server in the **constructor**, before any project is open, and `StartCoreAsync` won't re-root a connected server — so today the server runs rootless for the whole session. **BasicLang survives that; clangd does not** (no root → no `compile_commands.json` → silent garbage). Task 5 confirmed the shape of the failure: with a null root, `--compile-commands-dir` is **omitted entirely** and clangd falls back to searching upward from each file, which won't find `obj/`. Your `StartAllAsync(workspaceRoot)` is the natural home for the fix: start servers on `ProjectOpened`, not in a constructor. **Decide it, and say what you decided.**

**Files:**
- Create: `VisualGameStudio.Core/Abstractions/Services/ILanguageServiceRegistry.cs`
- Create: `VisualGameStudio.ProjectSystem/Services/LanguageServiceRegistry.cs`
- Modify: `VisualGameStudio.Core/Abstractions/Services/ILanguageService.cs` — lift `Descriptor` onto the interface (see above)
- Modify: `VisualGameStudio.Shell/Configuration/ServiceConfiguration.cs:28`
- Test: `VisualGameStudio.Tests/LSP/LanguageServiceRegistryTests.cs` (Task 5 named its file `LanguageServerDescriptorTests.cs` precisely to leave this name free for you)

- [ ] **Step 1: Write the failing tests** (use fakes — do NOT start real processes)

```csharp
[Test]
public void GetFor_RoutesByExtension()
{
    var r = new LanguageServiceRegistry(new[] { FakeBasicLang(), FakeClangd() });
    Assert.That(r.GetFor("a.bas")!.Descriptor.Id, Is.EqualTo("basiclang"));
    Assert.That(r.GetFor("a.cpp")!.Descriptor.Id, Is.EqualTo("clangd"));
    Assert.That(r.GetFor("a.h")!.Descriptor.Id,   Is.EqualTo("clangd"));
    Assert.That(r.GetFor("a.txt"), Is.Null);
}

// THE regression this whole task exists to prevent.
[Test]
public void IsConnectedFor_IsPerServer_NotGlobal()
{
    var bl = FakeBasicLang(connected: false);      // BasicLang down/restarting
    var cl = FakeClangd(connected: true);
    var r = new LanguageServiceRegistry(new[] { bl, cl });
    Assert.That(r.IsConnectedFor("a.cpp"), Is.True,
        "clangd must keep working when the BasicLang server is down");
    Assert.That(r.IsConnectedFor("a.bas"), Is.False);
}

[Test]
public void IsConnectedFor_UnknownExtension_IsFalse()
    => Assert.That(new LanguageServiceRegistry(new[] { FakeBasicLang() }).IsConnectedFor("a.txt"), Is.False);

[Test]
public void Dispose_DisposesEveryServer()   // guards the orphan-on-exit regression
{
    var bl = FakeBasicLang(); var cl = FakeClangd();
    new LanguageServiceRegistry(new[] { bl, cl }).Dispose();
    Assert.That(bl.Disposed, Is.True);
    Assert.That(cl.Disposed, Is.True);
}

[Test]
public void EachServer_HasItsOwnRestartPolicy()
{
    var r = new LanguageServiceRegistry(new[] { FakeBasicLang(), FakeClangd() });
    Assert.That(r.GetFor("a.bas")!.RestartPolicy, Is.Not.SameAs(r.GetFor("a.cpp")!.RestartPolicy));
}
```

- [ ] **Step 2: Run — expect FAIL**

- [ ] **Step 3: Implement.** `ILanguageServiceRegistry`: `GetFor(path)`, `IsConnectedFor(path)`, `StartAllAsync(workspaceRoot)`, `StopAllAsync()`, `All`, `IDisposable`. Route via `LanguageFileTypes`. Registry is `IDisposable` and disposes every child.

- [ ] **Step 4: Run — expect PASS**

- [ ] **Step 5: Register in DI.** In `ServiceConfiguration.cs`, register `ILanguageServiceRegistry` as a singleton.

**Which descriptors at this point: BasicLang ONLY.** clangd cannot be registered yet — its descriptor needs a resolved clangd path, and `ClangdLocator` doesn't exist until Task 11. Task 12 adds it. So Task 6 ships a registry with one server, which is correct and fully testable (the fakes in Step 1 cover the two-server routing).

**⚠ KEEP the existing `AddSingleton<ILanguageService, LanguageService>` registration (`:28`) — resolving to the SAME BasicLang instance the registry holds, not a second one.** Task 7 converts the ~26 call sites that still inject `ILanguageService`; until then they must keep working. Two independent instances would mean two BasicLang server processes, one of them orphaned — so register the registry first and have the `ILanguageService` registration resolve out of it (`sp => sp.GetRequiredService<ILanguageServiceRegistry>().GetFor("x.bas")!` or equivalent). **Comment the duality as temporary and name Task 7 as its removal.** Add a test pinning that the two registrations resolve to the same object — it's the cheap guard against the two-process bug, and it fails loudly the moment someone splits them.

- [ ] **Step 6: Full suite, then commit.** BasicLang behavior must be unchanged: same one process, same handshake, all ~26 existing call sites still functioning through the shim.

**Keep `ILanguageService` registered** (resolving to the BasicLang instance) so the ~26 not-yet-converted call sites still compile — Task 7 removes that shim. Note the temporary duality in a comment.

- [ ] **Step 6: Full suite**

- [ ] **Step 7: Commit**

```
git add -A
git commit -m "feat(lsp): LanguageServiceRegistry routes documents to a per-extension server"
```

---

## Task 7: Convert the ~26 exclusion gates into routing

**Why:** these are the actual size of the job that spec §4 called "one connection". They are `IsBasicLangSourceFile` **exclusion** gates ("is this BasicLang? else do nothing"), not routing. **There is no compiler help if you miss one** — a missed site silently sends `.cpp` positions to the BasicLang server.

**The 24 in `MainWindowViewModel.cs`:** `:1041` (didClose), `:1067` (re-sync on reconnect), `:1670` (debug datatip hover), `:1908` (**didOpen — the root of all downstream silence**), `:2057` (hover), `:2089` (signatureHelp), `:2106` (documentHighlight), `:2153` (completion), `:2244` (didChange), `:2583` (didSave), `:4206` (documentSymbol/GoToSymbol), `:4415` (typeDefinition), `:4442` (definition), `:4468` (implementation), `:4637` (references), `:4885` (rename), `:7401` (selectionRange), `:7435` (**outline — see the bug below**), `:7459` (codeLens), `:7491` (semanticTokens), `:7523` (codeLens exec), `:7621` (formatting + format-on-save), `:7658` (onTypeFormatting), `:7697` (codeAction).
**Plus 2 in `CodeEditorControl.axaml.cs`:** `:1971` (folding), `:4084` (`IsBasicLangDocument`, used at `:3861` for snippets).

**Files:**
- Modify: `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs` (the 24 gates + the ungated `GoToWorkspaceSymbolAsync` `:4270` + the global `_diagnosticsAggregator.Clear()` `:1137`)
- Modify: `VisualGameStudio.Editor/Controls/CodeEditorControl.axaml.cs` (holds its own `_languageService` via `SetLanguageService` `:401-410` — the seam must cover the control, not just the VM)
- Modify: `VisualGameStudio.Shell/ViewModels/Panels/CallHierarchyViewModel.cs` (`:180`, `:210` — ungated, 2 live `_languageService.` refs)
- Modify: `VisualGameStudio.Shell/ViewModels/Panels/TypeHierarchyViewModel.cs` (`:154`, `:180` — same)
- Modify: `VisualGameStudio.Shell/ViewModels/Panels/DocumentOutlineViewModel.cs` (Step 3's outline bug)
- Modify: `VisualGameStudio.Shell/Configuration/ServiceConfiguration.cs` (drop the `ILanguageService` shim)

- [ ] **Step 1: Convert every gate.** Pattern:

```csharp
// BEFORE
if (_languageService.IsConnected && BasicLangFileTypes.IsBasicLangSourceFile(activeDoc.FilePath))
    var r = await _languageService.GetHoverAsync(...);

// AFTER
var svc = _languageServices.GetFor(activeDoc.FilePath);
if (svc is { IsConnected: true })
    var r = await svc.GetHoverAsync(...);
```

- [ ] **Step 2: Leave these BasicLang-only — they are genuinely language-specific, not routing**

- `:7523` `HandleCodeLensCommandAsync` dispatches on the literal `"basiclang.showReferences"` (`:7527`). clangd emits different command names → keep the BasicLang check; add a TODO for Phase 3b.
- `:2153` completion **fallback**: `GetFallbackCompletions()` is a BasicLang keyword dump — correctly gated by `isBasicLangDocument` at `:2204`. Route the LSP call, keep the fallback BasicLang-only.
- `:4885` rename **fallback** to `_refactoringService` — BasicLang-specific. Route the LSP call, keep the fallback gated.
- `CodeEditorControl.axaml.cs:4084` snippets — BasicLang statement snippets. **Leave entirely.**

- [ ] **Step 3: FIX THE LIVE BUG at `:7435` — the outline runs the BasicLang parser on C++**

The else-branch (`:7435-7442`) calls `DocumentOutline.UpdateOutline(filePath, content)`, whose parser (`DocumentOutlineViewModel.cs:120-165`) skips only `'`/`REM ` comments (**not `//`**) and matches `"Class "` case-insensitively at line start — so `class Foo {` produces an outline node and everything else is dropped. **C++ files get a partial, wrong outline today.** Route `.cpp` to clangd's `documentSymbol` (`UpdateOutlineFromLspAsync` at `DocumentOutlineViewModel.cs:31` is already pure LSP), and restrict the text-parser fallback to BasicLang.

Write a regression test first:
```csharp
[Test]
public void DocumentOutline_TextFallback_IsNotAppliedToCppFiles()
{
    // "class Foo {" must NOT produce a BasicLang-parsed outline node for a .cpp file
}
```

- [ ] **Step 4: Fix the ungated sites Phase 2 MISSED** — they query whichever server the singleton holds:

`MainWindowViewModel.GoToWorkspaceSymbolAsync` (`:4270`), `CallHierarchyViewModel.cs:180` and `:210`, `TypeHierarchyViewModel.cs:154` and `:180`.
For workspace symbol, "search all servers and merge" is arguably correct — but decide **explicitly** and comment it; today it silently searches BasicLang only in a mixed project.

- [ ] **Step 5: `_diagnosticsAggregator.Clear()` (`:1137`) is GLOBAL** — a BasicLang restart would wipe clangd's diagnostics. Make it clear only the restarting server's files, or document why global is acceptable. Write a test.

- [ ] **Step 6: Sweep for misses**

```
Grep: _languageService\.
Expect: ZERO hits outside the registry itself.
Grep: IsBasicLangSourceFile
Expect: only the 4 genuinely-BasicLang sites from Step 2.
```

- [ ] **Step 7: Build + full suite.** `dotnet clean` first if any AXAML changed.

- [ ] **Step 8: Commit**

```
git add -A
git commit -m "feat(lsp): route LSP features per-document instead of excluding non-BasicLang files"
```

---

## Task 8: Fix `obj/gen` wipe-on-throw (pre-existing bug)

**Why:** `CleanGeneratedDir` (`CppProjectBuilder.cs:128`) runs **before** `GenerateSplit` (`:130`). If generation throws — `CppCapabilityException` (`CppCodeGenerator.Split.cs:47-49`) or the reserved-name `ArgumentException` (`Split.cs:101`) — `obj/gen` is already wiped and nothing is rewritten. **One unsupported construct silently destroys every IntelliSense header.** A mid-loop IO fault at `:132-133` leaves a partial set for the same reason. This is a real bug today, and Phase 3a makes it far more visible because IntelliSense depends on those headers between builds.

Cheap fix: `CppSplitResult.Files` (`Split.cs:19`) is **already a fully in-memory dict** → generate → clean → write. A reorder, not a redesign.

**Files:**
- Modify: `BasicLang/ProjectSystem/CppProjectBuilder.cs:113-157`
- Test: `VisualGameStudio.Tests/Compiler/CppProjectBuilderCleanTests.cs` — ⚠ **not** `IntelliSenseEmitterTests.cs`: `IntelliSenseEmitter` doesn't exist until Task 9, and this bug is in `Build()` today. Task 9 adds its own file.

- [ ] **Step 1: Write the failing test** — a project whose codegen throws (use a construct that raises `CppCapabilityException`; `CppSelectCaseTests.cs` has working examples of type/tuple/binding patterns being rejected) must leave previously-generated headers intact.

**Seeding the "previously-generated headers" precondition:** call `Build()` once on a *good* project first. Without a toolchain it returns `Success = false` at BL6005 (`:159-168`) — but it writes `obj/gen` at `:113-157` **before** reaching that gate, so the headers exist. That is exactly the ordering Task 9 depends on, so this test doubles as proof of it.

- [ ] **Step 2: Run — expect FAIL (headers wiped)**

- [ ] **Step 3: Reorder to generate → clean → write**

- [ ] **Step 4: Run — expect PASS**

- [ ] **Step 5: Full suite** — the split-emission tests (`CppSplitEmissionTests.cs`) must stay green

- [ ] **Step 6: Commit**

```
git add -A
git commit -m "fix(cpp): generate before cleaning obj/gen so a codegen failure cannot wipe headers"
```

---

## Task 9: Extract a toolchain-free `EmitForIntelliSense` seam

**Why — and the good news:** `obj/gen` emission **already precedes the toolchain probe**. Headers are written at step 5 (`CppProjectBuilder.cs:113-157`); `CppToolchain.Find()` / the BL6005 hard-fail is step 6 (`:159-168`). `GenerateSplit` is pure codegen with no `Process` (proven by the plain unit test `CppSplitEmissionTests.cs:26`), and `BuildCompileCommandArguments` (`CppToolchain.cs:168-177`) is static + kind-keyed, documented "so it is unit-testable without an installed toolchain". **So IntelliSense-without-a-compiler works.** The only problem is there's no seam to call: `CppProjectBuilder.Build` is one monolithic static method (`:30-299`).

**Gates that must be BYPASSED on the IntelliSense path.** `Build()` is a straight-line method where **every** one of these `Fail(...)` + `return`s — so any of them aborts everything downstream, including the `compile_commands.json` write:

| Gate | Line | Blocks | Why bypass |
|---|---|---|---|
| BL6007 no-sources | `:60-70` | headers **and** cc-json | mid-edit empty project still deserves IntelliSense |
| Transpile failure | `:80-87` | headers **and** cc-json | **KEEP this one** — it's what preserves stale headers (see "Error tolerance") |
| Entry-point rule | `:97-107` (`NativeEntryPoints.Apply` at `:101`) | headers **and** cc-json | **a project mid-edit with no `Sub Main` yet would otherwise get no headers** |
| BL6005 no-toolchain | `:159-168` (`Find()` at `:160`) | cc-json only (headers already written at `:113-157`) | the whole point — IntelliSense without a compiler |
| **Native libs (BL6009)** | **`:201-233`** | **cc-json only** | **see the ⛔ below** |
| **Engine framework auto-link** | **`:235-256`** | **cc-json only** | **see the ⛔ below** |
| Compile / link / engine-DLL deploy | after `:270` | — | not our job |

### ⛔ The game-project hole (caught by plan review — the original draft missed this)

The **native-libs block (`:201-233`)** and **engine-framework auto-link (`:235-256`)** both `Fail(result, "BL6009")` and `return`, and they sit **before** the `compile_commands.json` write at `:258`. So on a **game project whose engine `.lib` doesn't resolve**, `EmitForIntelliSense` writes `obj/gen` headers but **no compile db**, and returns `Success = false` — leaving clangd with nothing to read.

This is not hypothetical: the game-app engine-`.lib` gap is **this repo's known environment flake**, called out in this plan's own Definition of Done. It **will** fire on the implementer's machine, and it hits the flagship project type.

**It's a free fix:** `request.Libraries` never reaches `FlagsFor` (`CppToolchain.cs:118-140`) — libraries contribute **nothing** to `compile_commands.json`, which only carries compile flags, not link flags. So the IntelliSense path should **skip both blocks outright**. Nothing is lost.

⚠ Task 9's headline test uses a plain console project and will **not** catch this. The game-project test below is mandatory, not optional.

**Error tolerance — decided:** broken `.bas` yields **no IR at all**. `Compiler.cs:283` (parse) and `:308` (semantic) return before `CombinedIR` is built at `:313` → `CombinedIR` is null → `GenerateSplit` has no input. There is **no partial-IR path**, and adding one is a large compiler change. But a failed transpile returns at `CppProjectBuilder.cs:86`, **before** the clean — so **stale headers survive**, which is exactly right for IntelliSense. **Decision: regen-on-success-only, never wipe on failure.** Task 8 (DONE, `1939de0`) makes this robust rather than accidental: it reordered to generate → clean → write, so a `GenerateSplit` throw can no longer wipe the previous headers.

### ⛔ ERRATUM — THIS MACHINE HAS MSVC. Do NOT build the seam around the BL6005 path.

The plan repeatedly assumed "no toolchain installed → `Build()` returns `Success=false` at BL6005". **That is FALSE here**, verified twice (Task 8's implementer, then independently by its reviewer reproducing `CppToolchain.Find()`'s exact probe):
- `clang++` and `g++` are **NOT** on PATH (both probe branches fall through), **but**
- vswhere returns `C:\Program Files\Microsoft Visual Studio\2022\Enterprise` with `VC.Tools.x86.x64`, and `vcvars64.bat` exists → **`Find()` returns a non-null `MSVC (cl.exe)` toolchain.**

So a real build here proceeds **past** BL6005 and reaches BL6009 (the known `.lib` env failure). Consequences for you:
1. **Don't write tests that depend on the machine having no toolchain.** Pass `toolchain: null` **explicitly** to exercise the toolchain-free path — that's what the tests above do, and it's correct on toolchain-present and toolchain-absent machines alike. (Task 8's test was deliberately written to assert nothing about the good build's success for exactly this reason.)
2. **The driver-default decision now bites in a way the plan didn't anticipate.** "Prefer the real toolchain's kind when one is installed" means that on THIS machine a real build writes `compile_commands.json` with **MSVC-style** flags (`cl.exe`, `/std:c++20`) — and clangd only parses those correctly when it recognizes a cl-style driver (it keys on `arguments[0]`, i.e. `--driver-mode=cl` behavior). **Decide explicitly what the emitter does when `Find()` returns MSVC**, and say what you decided. Emitting MSVC flags under a `clang++` driver name — or vice versa — is precisely the "silently wrong IntelliSense flags" failure this decision exists to prevent.

**Task 9's precondition is CONFIRMED and now pinned by a test** (Task 8's `CppProjectBuilderCleanTests`): `obj/gen` is written unconditionally at section 5, **before** the toolchain gate at section 6.

**Driver default — a real decision, not cosmetic:** `Kind`/`DriverName` come off the object `CppToolchain.Find()` returns (`Find()` at `:160`; `DriverName` declared at `CppToolchain.cs:110`; consumed at `:261`). With no toolchain you must pick one. clangd reads `arguments[0]` as the driver, and MSVC `/std:c++20` vs GNU `-std=c++20` (`CppToolchain.cs:121-138`) change how clangd parses the entry — **guessing wrong yields silently wrong IntelliSense flags**. **Decision: default `ClangLike` / `clang++`** (the spec's blessed toolchain); prefer the real toolchain's kind when one is installed.

⚠ **The default `Kind` is needed EARLIER than you'd think.** Request assembly dereferences `toolchain.Kind` for `OutputPath` at `:174-175` — i.e. before the cc-json write at `:261`. A null toolchain there is a `NullReferenceException`, not a missing flag. Supply the default at the top of the core, not at the cc-json call.

**Files:**
- Create: `BasicLang/ProjectSystem/IntelliSenseEmitter.cs`
- Modify: `BasicLang/ProjectSystem/CppProjectBuilder.cs` (`Build` delegates to the shared core — **no second implementation**, per the discipline both `CppProjectBuilder.cs:24-26` and `BuildService.cs:1024-1029` already commit to)
- Test: `VisualGameStudio.Tests/Compiler/IntelliSenseEmitterTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
[Test]  // THE headline test: no compiler installed, still get headers + a compile db
public void Emit_WithNoToolchain_WritesObjGenAndCompileCommands()
{
    var r = IntelliSenseEmitter.Emit(projectFile, "Debug", toolchain: null);
    Assert.Multiple(() =>
    {
        Assert.That(File.Exists(Path.Combine(dir, "obj", "gen", "BasicLangRuntime.g.h")), Is.True);
        Assert.That(File.Exists(Path.Combine(dir, "obj", "compile_commands.json")), Is.True);
        Assert.That(r.Success, Is.True);
    });
}

// ⚠ ERRATUM — the original of this test used `Does.Contain("clang++")` +
// `Does.Contain("-std=c++20")` on the raw JSON. That is the THIRD instance of this plan's
// own worst antipattern, and the weakest yet: `Does.Contain("clang++")` passes if the string
// appears ANYWHERE — including inside a file path. The driver is `arguments[0]` SPECIFICALLY;
// that is a structural claim and needs a structural assertion. (Task 3's review caught this
// class on a JSON field moving to the wrong parent; Task 5 broke clangd's argument QUOTING and
// the substring assertion still passed.) PARSE the JSON. Assert the position.
[Test]
public void Emit_WithNoToolchain_DefaultsToClangDriver()
{
    IntelliSenseEmitter.Emit(projectFile, "Debug", toolchain: null);
    var db = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "obj", "compile_commands.json")))!;
    var args = db[0]!["arguments"]!.AsArray();
    Assert.That(args[0]!.GetValue<string>(), Is.EqualTo("clang++"),
        "clangd reads arguments[0] as the driver — it must BE the driver, not merely contain it");
    Assert.That(args.Select(a => a!.GetValue<string>()), Has.One.EqualTo("-std=c++20"),
        "GNU-style flag as an exact token, not MSVC /std: and not a substring of something else");
}

[Test]  // no Sub Main yet — mid-edit. Must NOT block emission.
public void Emit_WithNoEntryPoint_StillEmitsHeaders()
{
    var r = IntelliSenseEmitter.Emit(projectWithNoMain, "Debug", toolchain: null);
    Assert.That(r.Success, Is.True);
    Assert.That(File.Exists(Path.Combine(dir, "obj", "gen", "BasicLangRuntime.g.h")), Is.True);
}

[Test]  // broken source: keep the last good headers
public void Emit_WithBrokenSource_LeavesPreviousHeadersIntact()
{
    IntelliSenseEmitter.Emit(goodProject, "Debug", null);
    var before = File.ReadAllText(Path.Combine(dir, "obj", "gen", "Logic.g.h"));
    WriteBrokenSource();
    var r = IntelliSenseEmitter.Emit(brokenProject, "Debug", null);
    Assert.That(r.Success, Is.False);
    Assert.That(File.ReadAllText(Path.Combine(dir, "obj", "gen", "Logic.g.h")), Is.EqualTo(before));
}

// ⚠ ERRATUM — FOURTH instance of the same antipattern, and Task 9's implementer proved it.
// `Does.Contain(objGen)` does NOT catch an include-ORDER shuffle: both dirs are still present
// after a shuffle, so the assertion stays green while `#include "Logic.g.h"` resolves against
// the wrong directory. ORDER is the thing under test here. Parse the -I tokens and assert the
// exact ordered list.
[Test]
public void Emit_IncludePath_ContainsProjectDirAndObjGenInOrder()   // makes #include "Logic.g.h" resolve
{
    IntelliSenseEmitter.Emit(projectFile, "Debug", null);
    var db = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "obj", "compile_commands.json")))!;
    var includes = db[0]!["arguments"]!.AsArray()
        .Select(a => a!.GetValue<string>())
        .Where(a => a.StartsWith("-I"))
        .Select(a => a.Substring(2))
        .ToArray();
    Assert.That(includes, Is.EqualTo(new[] { dir, Path.Combine(dir, "obj", "gen") }),
        "include ORDER is load-bearing — projectDir then objGen then <IncludeDir> items");
}

// MANDATORY — the game-project hole. See the ⛔ above. A game project with an
// unresolvable engine .lib is THIS REPO'S KNOWN ENV FLAKE, so this WILL happen locally.
// Build() rightly fails BL6009; EmitForIntelliSense must NOT — libs contribute nothing
// to compile_commands.json, which carries compile flags only.
[Test]
public void Emit_GameProject_WithUnresolvableEngineLib_StillWritesCompileCommands()
{
    var r = IntelliSenseEmitter.Emit(gameProjectWithMissingEngineLib, "Debug", toolchain: null);
    Assert.Multiple(() =>
    {
        Assert.That(r.Success, Is.True, "a missing link-time .lib must not deny IntelliSense");
        Assert.That(File.Exists(Path.Combine(dir, "obj", "compile_commands.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(dir, "obj", "gen", "BasicLangRuntime.g.h")), Is.True);
    });
}
```

- [ ] **Step 2: Run — expect FAIL**

- [ ] **Step 3: Implement.**

⚠ **Don't look for "step 8" or "step 9" — they don't exist.** `Build()`'s numbered comments stop at `// ---- 7. Request ----`; everything after that (native libs, engine auto-link, `compile_commands.json`, compile, engine-DLL deploy) is **unnumbered**. Use these code landmarks instead of step numbers:

| The shared core RUNS | Landmark |
|---|---|
| partition sources | `:40-58` |
| transpile (**keep** its failure return at `:80-87`) | `:72-95` |
| write `obj/gen` — generate → clean → write, per Task 8 | `:113-157` |
| assemble `CppCompileRequest` | `:172-199` (include-path order `projectDir` → `objGen` → `<IncludeDir>` — **preserve exactly**; it's what makes `#include "Logic.g.h"` resolve) |
| write `compile_commands.json` | `:258-267` |

| The shared core SKIPS on the IntelliSense path | Landmark |
|---|---|
| BL6007 no-sources | `:60-70` |
| entry-point rule | `:97-107` |
| BL6005 toolchain probe/fail | `:159-168` |
| native libs + engine auto-link | `:201-256` |
| compile / link / deploy | `:270`+ |

`Build()` calls the same core, then continues into the toolchain probe and compile. **Read the result to confirm no logic got duplicated** — the no-second-implementation discipline is stated in prose at `CppProjectBuilder.cs:21-27` and `BuildService.cs:1023-1029`, and this task is exactly where it gets violated by accident.

- [ ] **Step 4: Run — expect PASS**

- [ ] **Step 5: Full suite** — `MixedProjectBuildTests`, `CppProjectCliBuildTests`, `CompileCommandsWriterTests` must all stay green. **Test both entry points** (CLI + IDE BuildService) per the repo rule.

- [ ] **Step 6: Commit**

```
git add -A
git commit -m "feat(cpp): toolchain-free IntelliSense emission seam shared with the builder"
```

---

## Task 10: Emit on project open

**Why:** spec §4 requires `compile_commands.json` and `obj/gen` to exist **before** the first build, or clangd has nothing to read on a fresh checkout.

**Hooks (verified):**
- ✅ `IProjectService.ProjectOpened` is **LIVE** — fired at `ProjectService.cs:129` (`OpenProjectAsync`) and `:114` (create); subscribed at `MainWindowViewModel.cs:490` → `OnProjectOpened` (`:920`).
- ❌ `ProjectOpenedEvent` record (`Core/Events/Events.cs:9`) is **DEAD** — published only in `IDEWorkflowTests.cs:64`. **Do not build on it.**
- ✅ `ProjectFile.Load` (`ProjectFile.cs:103`) bridges IDE project → compiler `ProjectFile`; already used at `BuildService.cs:1040-1041`.

**Cost:** emission runs the **entire** front-end via `CompileProjectFiles` (`Compiler.cs:208-332`) including the full `OptimizationPipeline` (`:321-329`) — proportional to the whole project, no incrementality. **Must run off the UI thread, cancellable, and coalesced.**

**Scope note:** `.blproj`-change and `.bas`-save triggers are **Phase 3b**. `FileWatcherService` is fully implemented (`:28-133`) and DI-registered (`ServiceConfiguration.cs:42`) but has **zero production callers** — dead code that just needs turning on. There is **no shared debounce helper**; model one on `SettingsService.ScheduleSave` (`:941+`, CTS + `Task.Delay`, cancel-restart, flush-on-Dispose `:58`). **Do NOT reuse `FileWatcherService.IsDebounced` (`:158-171`) — it is a rate-limiter that DROPS the trailing edge.**

**Files:**
- Modify: `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs` (`OnProjectOpenedCoreAsync` `:936`)

### ⚠ D2 — DECIDE THIS: does project-open probe for a toolchain?

Task 9 handed you an explicit decision. `IntelliSenseEmitter.Emit(project, config, toolchain)` **never probes on its own, by design** — the caller supplies the toolchain or `null`. So project-open must choose:

- **`CppToolchain.Find()`** → the compile database matches what a real build would produce (on this machine: MSVC, `cl` + `/std:c++20`). But `Find()` **shells out to vswhere and can take seconds** — on the UI-adjacent open path.
- **`null`** → fast, no process spawn, database uses the `clang++` / `-std=c++20` default.

Task 9's read (and mine): **`null` is probably right for open, with a refresh after a build** — but decide it **explicitly** and say what you decided. Note the interaction: clangd selects its driver mode from `arguments[0]`, so a `clang++`-flavored database on an MSVC machine is *coherent* (Task 9 pairs Kind+driver from the same source, never mixed) — it just may not match the real build's flags exactly. Judge whether that divergence matters for IntelliSense fidelity.

- [ ] **Step 1: Write the failing test** — opening a native project invokes the emitter once, off the UI thread, and a second open cancels the first.
- [ ] **Step 2: Run — expect FAIL**
- [ ] **Step 3: Implement** — only for native projects (`BasicLangProject.IsNativeBuild`); never block project open; log failures to Output, never to a modal.
- [ ] **Step 4: Run — expect PASS**
- [ ] **Step 5: Full suite**
- [ ] **Step 6: Commit**

```
git add -A
git commit -m "feat(ide): emit IntelliSense headers and compile_commands on project open"
```

---

## Task 11: clangd discovery + the `cpp.clangd.path` setting

**Why reuse:** `basiclang.lsp.path` is **precisely** the "user overrides a tool path, else auto-probe" feature — clone it end to end. Schema `SettingsService.cs:1204`; dialog `SettingsViewModel.cs:1011` (`MakeText`, `:1040-1041`) + property map `:1286` + load `:1592`; consumer + `SettingsConsumerRegistry.RegisterConsumer` at `LanguageService.cs:72-81`; and the pure injectable rule `ResolveLspPathOverride(configuredPath, fileExists)` at `:106-112` (override wins only if non-empty AND the file exists, else null = auto-probe).

**Discovery:** `CppToolchain.Find()` (`:52-105`) is **NOT reusable** — it proves "can compile" by running `--version` on a bare name and **never retains a path**; clangd must be spawned by absolute path. `ShellProfileDetector.FindOnPath` (`:393-413`) **is** a real PATH search returning an absolute path — but it's `internal static` and does not apply `PATHEXT`. Lift it to `Core/Utilities/ExecutableLocator.cs` and add `PATHEXT` handling for Windows.

**Files:**
- Create: `VisualGameStudio.Core/Utilities/ExecutableLocator.cs`, `VisualGameStudio.ProjectSystem/Services/ClangdLocator.cs`
- Modify: `SettingsService.cs` (new `cpp` schema group — the `basiclang` group at `:1196-1199` is language-specific), `SettingsViewModel.cs`, `VisualGameStudio.Tests/SettingsConsumerContractTests.cs`

- [ ] **Step 1: Write the failing tests** — `ResolveClangdPath(configured, fileExists, pathProbe)` as a **pure static** mirroring `:106-112`: configured+exists wins; configured+missing falls back to probe; empty → probe; probe null → null. Plus `ExecutableLocator` finds an absolute path and honors `PATHEXT` on Windows.

- [ ] **Step 2: Run — expect FAIL**

- [ ] **Step 3: Implement**

- [ ] **Step 4: Satisfy the `SettingsConsumerRegistry` contract — 4 ENFORCED tests** (`VisualGameStudio.Tests/SettingsConsumerContractTests.cs`):
  - `EveryDialogSettingKey_HasARegisteredConsumer` (`:134-152`) — real code must `RegisterConsumer(key, description)`
  - `EveryDialogSettingKey_ExistsInSchema` (`:159-176`)
  - `DialogInventory_IsNonEmpty_AndIncludesKnownKeys` (`:112-129`) — keys unique
  - `D3RemovedKeys_AreNotInTheDialogInventory` (`:182-206`) — a 4-key denylist. `cpp.clangd.path` won't trip it, but don't be surprised by a 4th test.
  **⚠ Lazy-init trap** (documented at `SettingsConsumerRegistry.cs:21-42`): registration only happens if the consumer's initializer actually runs. **You MUST add a forcing line to `ForceAllDialogConsumers`** (`:56-91`), alongside the existing `_ = new LanguageService(...)` at `:77`. If the type is too heavy to construct, use the static-seam escape hatch (`:71-72`).

- [ ] **Step 5: Run — expect PASS, including all 4 contract tests**
- [ ] **Step 6: Full suite**
- [ ] **Step 7: Commit**

```
git add -A
git commit -m "feat(cpp): clangd discovery via PATH with a cpp.clangd.path override"
```

---

## Task 12: Launch clangd + status-bar indicator + not-found notification

**Reuse (verified):**
- Status bar: the LSP indicator is the exact precedent — `LspStatus`/`IsLspRunning` (`StatusBarViewModel.cs:99-102`), `UpdateLspStatus(bool)` (`:388-392`), rendered with a status dot at `StatusBarControl.axaml:234` and **clickable** via `Command="{Binding RestartLspCommand}"` (`:228`) → `RestartLsp()` (`:634-637`) → `RestartLspRequested` (`:179`).
- Actionable prompt: **LIVE** `ShowNotification(message, severity, List<NotificationAction> actions, details)` (`MainWindowViewModel.cs:1571-1584`) → `NotificationRequested` (`:1541`) → renders **real buttons** at `MainWindow.axaml.cs:343-366`. `NotificationAction` = `Label` + `Action Callback` (`:8195-8205`).
- ⚠ **DEAD — do not use:** `StatusBarViewModel.AddNotification`'s `actions` parameter (`:436-437`) stores `NotificationItem.Actions` (`:758`) but the notification-center `DataTemplate` (`StatusBarControl.axaml:528-554`) **renders no Actions ItemsControl**. Its `suppressKey` mechanism (`:439-440`) IS useful so "clangd not found" doesn't nag on every C++ file open.

**Files:**
- Modify: `VisualGameStudio.ProjectSystem/Services/LanguageServiceRegistry.cs` (start clangd when a native project opens and clangd resolves)
- Modify: `VisualGameStudio.Shell/ViewModels/StatusBarViewModel.cs`, `VisualGameStudio.Shell/Views/Controls/StatusBarControl.axaml`

### ⛔ PROJECT SWITCH — `StartAllAsync` does NOT re-root. Found by Task 6; it is YOURS to fix.

`StartAsync` **no-ops on an already-connected server** (`StartCoreAsync`'s `if (IsConnected) return;`). So opening project B while clangd is connected to project A leaves clangd **rooted at A**, silently answering from the wrong compilation database — right-looking completions and diagnostics for the wrong project. **Re-rooting requires `StopAllAsync()` then `StartAllAsync(newRoot)`.** Task 6 documented this on the interface but correctly left the fix to you (it owns startup; you own the clangd lifecycle).

Note how this interacts with Task 6's rootless design: BasicLang keeps its rootless constructor autostart (it survives that), and **clangd can only ever start through `StartAllAsync`, which throws on a null root** — so clangd cannot start rootless by construction. Your job is the *second* project, not the first.

- [ ] **Step 0 (BLOCKING — do this before anything else): dump real clangd's initialize result and settle the `offsetEncoding` question.**

See landmine #3's ⛔ box. Task 3's review flagged, unverified, that clangd may negotiate encoding via a **non-standard `offsetEncoding`** field predating LSP 3.17. Task 3's parser reads only `positionEncoding` and defaults a missing value to utf-16. If clangd sends `offsetEncoding: "utf-8"` and omits `positionEncoding`, we silently believe utf-16 and **every position on every non-ASCII line is wrong, with the capability we added to detect it reporting all-clear.**

Run a real clangd, capture the raw initialize result, and report what it actually sends. If `offsetEncoding` is present: extend `ParseServerCapabilities` to read it, expose it on `ServerCapabilities`, and make a utf-8 answer **fail loudly** rather than default. Do NOT proceed to Step 1 on an assumption — this is the one thing in Phase 3a that can silently corrupt every position we send.

- [ ] **Step 1: Write the failing tests** — clangd absent → registry degrades (BasicLang unaffected, `IsConnectedFor("a.cpp")` false, status text says not found, **no exception**); clangd present → started with `--compile-commands-dir=<projectDir>/obj` and **no `--offset-encoding`**.
- [ ] **Step 2: Run — expect FAIL**
- [ ] **Step 3: Implement.** Degradation per spec §4: editing still works (highlighting only — already working, Phase 1), status bar hints. Phase 3a's hint is informational; the download action is Phase 3b.
- [ ] **Step 4: Run — expect PASS**
- [ ] **Step 5: `dotnet clean` (AXAML changed) then build + full suite**
- [ ] **Step 6: Commit**

```
git add -A
git commit -m "feat(cpp): launch clangd per project with status-bar state and graceful degradation"
```

---

## Task 13: End-to-end — clangd actually answers

**Why:** every prior task is unit-level. This is the only proof the whole chain works. Follow the existing toolchain-conditional pattern (`VisualGameStudio.Tests/Native/CppSplitCompileTests.cs` uses `Assert.Ignore` when no compiler is present).

**⚠ clangd is NOT on this machine** (verified 2026-07-15: `clang`, `llc`, `llvm-as`, `opt` all absent from PATH; no `C:\Program Files\LLVM`). MSVC IS available via vswhere. **So this test will SKIP locally.** Do not let it silently pass — it must `Assert.Ignore` with a clear reason, and the task is NOT done until someone runs it with clangd installed. Say so in the handoff.

**Files:**
- Create: `VisualGameStudio.Tests/LSP/ClangdE2ETests.cs`

- [ ] **Step 1: Write the test** — mixed project (`.bas` + `.cpp`) → emit → start clangd → `didOpen` the `.cpp` → assert:
  1. **completion** at a `::` on a std type returns non-empty
  2. **hover** on a symbol returns non-empty
  3. **diagnostics** arrive for a deliberate syntax error, and land in `DiagnosticsAggregator`
  4. **go-to-definition** on a BasicLang symbol lands in the generated `obj/gen/*.g.h` (proves Direction B interop + the include path)
  5. `.bas` files in the SAME project still answer from the BasicLang server (no cross-talk)

- [ ] **Step 2: Run.** Expected locally: **IGNORED** ("clangd not found on PATH"). With clangd installed: PASS.
- [ ] **Step 3: Full suite**
- [ ] **Step 4: Commit**

```
git add -A
git commit -m "test(cpp): clangd end-to-end IntelliSense in a mixed project"
```

---

## Definition of done

- [ ] Full suite green (baseline count + new tests − deleted `LspClientServiceTests`). Known env flake: game-app engine `.lib` gap — stage the lib beside the compiler bin to go green. Known skip: toolchain-conditional no-toolchain contract test.
- [ ] `Grep: _languageService\.` **`--glob "*.cs"`** → zero hits outside the registry. (`CallHierarchyViewModel.cs` and `TypeHierarchyViewModel.cs` each hold 2 — they're in scope via Task 7 Step 4.)
- [ ] `Grep: LspClientManager|ILspClient` **`--glob "*.cs"`** → zero hits. ⚠ Do NOT grep repo-wide: this plan document, the Phase 1/2 plans, `ide-parity-scorecard.md`, and `IDEAgent/ide_agent.py` all mention them in prose and will always match.
- [ ] `Grep: offset-encoding` **`--glob "*.cs"`** → **no LIVE argument**. ⚠ "Zero hits" is unsatisfiable and was wrong: Task 3 deliberately names the flag in a warning comment at the launch site (a warning that dodges the token a dev would actually type is worthless), and Task 12's own prescribed test asserts `Does.Not.Contain("--offset-encoding")`. **Permitted hits: that comment and that negative test. Anything that actually passes the flag to a process = fail.** (Caught by Task 3's implementer.)
- [ ] `Grep: HasExtensionProviders|NotifyDocumentOpenedAsync` → every languageId argument comes from the **total** `GetEditorLanguageId`, never the nullable `GetLspLanguageId`. (Task 2's ⛔ — a null here throws `ArgumentNullException` at runtime, and `CS8604` is in `NoWarn` so the compiler emits **no warning at all**.)
- [ ] Both entry points exercised (CLI + IDE BuildService).
- [ ] **User IDE smoke** (Phases 1 and 2 both caught real defects only here — do not skip):
  1. Open a mixed project → `.cpp` gets completion + hover + as-you-type squigglies
  2. Go-to-definition from `.cpp` on a BasicLang function lands in `obj/gen/*.g.h`
  3. `.bas` files unchanged (completion, hover, diagnostics, outline, folding)
  4. Kill the BasicLang server → **clangd keeps working** (the per-server regression)
  5. Rename clangd off PATH → status bar says not found, editing still works, no crash
  6. Error List shows clangd diagnostics alongside BasicLang ones
- [ ] `Task 13 ran with clangd actually installed` — not just skipped.
- [ ] superpowers:finishing-a-development-branch.

## Deferred to Phase 3b (do not scope-creep into 3a)

- clangd acquisition: no generic tool-download service exists. Build ~200-300 lines modeled on `VsixInstaller.cs:12` (URL → temp → `ZipFile.ExtractToDirectory` `:115` → `~/.vgs/tools/` → JSON state file). **Lift `OpenVsxClient.DownloadVsixToFileAsync` (`:186-216`)** — it is URL-generic (absolute URL bypasses `BaseAddress`) — into a shared `FileDownloader`; do NOT reference `OpenVsxClient` from C++ code. `MarketplaceService.DownloadAsync` is NOT reusable (relative URL). Missing everywhere: checksum/signature verification, POSIX `chmod +x`, `.tar.xz`, retry/resume. **`~/.vgs/` has no canonical path helper** — **7** sites duplicate `UserProfile + ".vgs"` (`SettingsService.cs:80`, `VsixInstaller.cs:51`, `HotExitService.cs:26`, `WorkspaceStateStore.cs:38`, `ExtensionService.cs:76` and `:77`, `SnippetService.cs:241`); `.vgs` is also OVERLOADED (a per-PROJECT `{projectDir}/.vgs/` exists for bookmarks/launch.json/tasks). **Every such service takes an injectable root** so tests don't write to the real `~/.vgs` — follow it.
- LLVM install-dir probing (**no existing implementation** — only vswhere/MSVC exists).
- **Semantic-token legend negotiation** — `SemanticTokenHighlighter.cs:26-30, 173-181` hardcodes BasicLang's legend index order (`0=>namespace, 2=>class`) with a comment saying it must match the server. clangd's legend differs (its 0 is `variable`) → **silently wrong colors**. Read the legend from `Capabilities` (Task 3 makes this possible).
- Debounced regen on `.bas` save (`FileSavedEvent` is LIVE at `CodeEditorDocumentViewModel.cs:625`) + `.blproj` watching (turn on the dead `FileWatcherService`) + the shared debounce helper.
- `completionItem/resolve` — zero call sites repo-wide; BasicLang sets `resolveProvider:false`, clangd sets `true` and defers `documentation` → expect empty doc tooltips until then.
- Dead `ILanguageService` surface — `GetInlayHintsAsync`, `GetDocumentLinksAsync`, `GetLinkedEditingRangesAsync` are implemented with **zero call sites**. Don't budget them as "already wired, just ungate".
- clangd's tolerance for full-text `contentChanges` when it advertises incremental sync (`LanguageService.cs:341-345` always sends full text and never reads the advertised mode) — **verify empirically**.
