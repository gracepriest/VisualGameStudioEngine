# The Desktop | Web switch — piece 3 of "one form, either target" (design)

Status: REVISION 1, 2026-10-05. Written under the owner's delegation of 2026-10-04 ("go with your recommendations, finish
everything"): every question that would normally go to the owner is decided here as a **DECIDED** item with the
alternatives that lost and why, so the owner can overrule any one of them later.
Branch: `feat/target-switch`, based on `origin/master` @ `ecc30f28` (property grid complete).
Piece 2 is read from `origin/feat/portable-controls` @ `c722b644` (Tasks 0–7 of its plan; the library itself is not
written yet). Paths are relative to the repo root; `P2:` prefixes a path read from that branch.

## 0. The programme and this piece

### 0.1 Programme decisions (owner — do not relitigate)
From `docs/superpowers/specs/2026-09-27-web-pixel-layout-design.md` §0: **P-D1** design in pixels · **P-D2** the web
page is desktop-exact, follows Anchor/Dock, stacks on phones · **P-D3** a portable control library (piece 2) ·
**P-D4 a Desktop | Web switch on the toolbar; one project builds either way; F5 runs the chosen target (THIS PIECE)** ·
**P-D5** retire `.blwebform` with convert-on-open (piece 4) · **P-D6** desktop-only controls badged; a web build
containing one stops with an error naming it.
Piece 1's spec lists piece 3 as "the Desktop | Web toolbar switch **and the one-form build** (P-D4, P-D6)" (`:25-26`).
Piece 2 hands piece 3 one explicit seam (P2 spec §6.2, `:648-650`): *the portable web init equals the WinForms init minus
a set of lines the region writer tags as geometry or property lines; piece 3 wraps exactly that set in `#If DESKTOP`.*

### 0.2 What this piece delivers
1. **A platform model**: a project declares the platforms it builds for (`Desktop`, `Web`); the IDE has ONE active
   platform, chosen on the toolbar beside Debug/Release, persisted per user.
2. **Build, Ctrl+F5 and F5 follow the active platform**: Desktop builds the C# WinForms exe and runs/debugs it as today;
   Web builds the JavaScript site and opens the startup form's page in the browser.
3. **The one-form build**: a `.blform` and its code-behind build for BOTH platforms — the web build emits the form's
   page from the `.blform` (piece 1's pixel path) and compiles the SAME code-behind against piece 2's library.
4. **The editor follows the platform**: `#If WEB` / `#If DESKTOP` dimming, diagnostics and completion flip with the
   switch; the Error List shows which platform a build diagnostic came from.
5. **Migration**: existing WinForms projects add the Web platform in one confirmed step; existing JS projects add
   Desktop when they have no `.blwebform`; `.blwebform` itself is piece 4's.

### 0.3 Non-goals
- Debugging JavaScript (breakpoints, stepping) — F5 on Web runs without the debugger (§5.4, DECIDED T-D12).
- Choosing the browser ("Browse With…"). The default browser is used, as today.
- Per-platform property VALUES (conditional `<PropertyGroup>`s), per-platform source files (`<Compile Condition>`).
  Target-specific code is `#If WEB` / `#If DESKTOP` (piece 2 O3).
- A C++ / native project on the Web platform; a Desktop platform on any backend but C#.
- The Visual Studio extension (VSIX) and the VS Code extension: neither gains a platform picker here (§10, risk R2).
- Multi-form navigation on the web (opening a second form's page from code); run-time-created controls (piece 2 O11).
- A phone-width preview in the canvas.
- Retiring or converting `.blwebform` (piece 4).

## 1. Grounded facts (master `ecc30f28` unless prefixed `P2:`)

### 1.1 How a project's target is chosen today
| # | Fact | Where |
|---|---|---|
| T1 | Two parsers of one `.blproj`. **Compiler side** `ProjectFile`: backend = `<Backend>` ?? `<TargetBackend>` ?? `"CSharp"`; reads only the FIRST unconditional `<PropertyGroup>` for properties | `BasicLang/ProjectSystem/ProjectFile.cs:198-231`, `:209-211` |
| T2 | `ProjectFile` parses `$(Configuration)`-conditioned groups (Optimize, DebugSymbols, DefineConstants) and no other condition | `ProjectFile.cs:315-339` |
| T3 | ⛔ `ProjectFile.Save()` REBUILDS the file from its model, dropping every element it does not model (already drops `UseWindowsForms`, `TargetFramework`, …). Its callers: `PackageManager` (`add`/`remove` package) | `ProjectFile.cs:371-466`; `BasicLang/ProjectSystem/PackageManager.cs:299`, `:316` |
| T4 | **IDE side** `ProjectSerializer` reads only `<TargetBackend>` into the enum `CSharp, Cpp, LLVM, MSIL, JavaScript`; `SavePreservingAsync` edits the file in place and keeps unknown elements and comments | `VisualGameStudio.ProjectSystem/Serialization/ProjectSerializer.cs:55-59`, `:483-564`, `:289-300`; `VisualGameStudio.Core/Models/BasicLangProject.cs:104-125` |
| T5 | There is no `<Platform>`/`<Platforms>`/`<ProjectType>` for BasicLang; a web project is just `<TargetBackend>JavaScript</TargetBackend>` | `BasicLang/ProjectSystem/TemplateEngine.cs:452` |
| T6 | `IsNativeBuild = Language==Cpp ‖ TargetBackend==Cpp` | `BasicLangProject.cs:14` |
| T7 | The IDE reads the backend in exactly four places: the build (`GetBackendId(project.TargetBackend)`), the build log line, and F5 / Ctrl+F5's JavaScript branch | `BuildService.cs:614`, `:379`; `MainWindowViewModel.cs:3945`, `:4195` |
| T8 | "Add New Form" picks the document kind from `UseWindowsForms` (`.blform` when true, else `.blwebform`) | `VisualGameStudio.Shell/ViewModels/Panels/SolutionExplorerViewModel.cs:1331-1335` |

### 1.2 Build routes
| # | Fact | Where |
|---|---|---|
| B1 | CLI `build` parses only `-c`/`--configuration` (default Debug); the backend comes ONLY from the project. `--target=` is parsed by the single-file route alone | `BasicLang/Program.cs:740-747`, `:839`, `:1438-1448` |
| B2 | ⛔ The VSIX passes `build "<blproj>" --target=X` from a map that DEFAULTS TO `csharp` and has no JavaScript arm — today harmless only because `build` ignores the flag | `BasicLang.VisualStudio/src/BasicLang.VisualStudio/Commands/CommandHandlers.cs:164-170`, `:255`, `:280` |
| B3 | IDE `GetBackendId` THROWS on an unmapped backend (no default) | `BuildService.cs:979-989` |
| B4 | IDE output = the configuration's `OutputPath` (`bin\Debug`); CLI managed output = `bin/<config>/<TFM>/` — the two layouts already differ | `BuildService.cs:615`, `:968`; `Program.cs:794` |
| B5 | A JS build writes script + map + `index.html` + `<Form>.html`/`.css` per web form into the same output folder | `BuildService.cs:808-829`; `Program.cs:950-971`; `BasicLang/Forms/FormAssetEmitter.cs:114-118` |
| B6 | Form documents are found by their own glob; a JS build loads ONLY `FormTarget.Web` documents (pages + the generated dispatch `obj/<config>/VgsFormDispatch.g.bas`); a C# build loads `.blform` only to copy assets; a document of the other kind is silently ignored | `BuildService.cs:623-657`, `:885-891`; `Program.cs:807-835`, `:975-982`; `BasicLang/Forms/FormDocumentLoader.cs:42-57` |
| B7 | `FormDocumentLoader` says in its summary: *"Web documents only. A `.blform` becomes WinForms code through the region writer … handing one to the page emitter would produce an HTML file for a desktop window"* — the sentence this piece reverses for dual-platform projects | `FormDocumentLoader.cs:16-18` |
| B8 | A solution build builds every project in dependency order with ONE configuration name; each project's own backend applies | `BuildService.cs:81-140`; `SolutionService.cs:245` |
| B9 | `P2:` `BuildSymbols.For(backend, configuration, defineConstants)` is THE answer to the symbol set: `WEB` for javascript/js, `DESKTOP` otherwise, DEBUG/RELEASE by configuration; WEB/DESKTOP in DefineConstants are ignored with a warning. Called by the `BasicCompiler` constructor (every route) and the LSP | `P2:BasicLang/BuildSymbols.cs:18-90`; `P2:BasicLang/Compiler.cs:198-204`; `P2:BuildService.cs:661-674` |
| B10 | `BuildSymbols` does NOT exist on master; master's `CompilerOptions` has no symbol field and `DefineConstants` never reaches the compiler | `BasicLang/Compiler.cs:90-155`, `:186` |

### 1.3 Run and debug
| # | Fact | Where |
|---|---|---|
| R1 | F5 → `StartDebuggingCommand`, Ctrl+F5 → `StartWithoutDebuggingCommand`, Shift+F5 → Stop, Ctrl+Shift+B → Build | `VisualGameStudio.Shell/Views/MainWindow.axaml:17-29` |
| R2 | F5 / Ctrl+F5 branch to the JS preview BEFORE the adapter pre-flight when the project's backend is JavaScript | `MainWindowViewModel.cs:3945-3949`, `:4195-4199` |
| R3 | Desktop F5: adapter from `_debugAdapterRegistry.GetFor(project)` (managed `--debug-adapter` / lldb-dap by `IsNativeBuild`), build, `.vgs/launch.json`, `DebugService.StartDebuggingAsync` | `MainWindowViewModel.cs:3955-4084`; `DebugService.cs:144-194`; `Core/Abstractions/Services/DebugAdapterDescriptor.cs:138-172` |
| R4 | Web run: `StartJavaScriptPreviewAsync` → `WebPreviewServer` (loopback `HttpListener`, `/` = `index.html`, `no-store`) → `OpenInBrowser` (`UseShellExecute`, the default browser). Opens the one form page when there is exactly one, else the root; its comment: *"the choice is the project's StartupForm property, which does not exist yet"* | `MainWindowViewModel.cs:4255-4336`; `VisualGameStudio.ProjectSystem/Services/WebPreviewServer.cs:24-188` |
| R5 | No JS debugging anywhere (no CDP, no `--remote-debugging-port`); the IDE prints "Console output appears in the browser's developer tools" | `MainWindowViewModel.cs:4234-4236`, `:4307` |
| R6 | Stop runs `StopWebPreview()` first, then the DAP stop | `MainWindowViewModel.cs:4432-4437`, `:4339` |
| R7 | The JS entry point runs `Main` (if any) and then starts the page's form through the generated dispatch, unless user code calls/references the dispatch; Main runs on EVERY page | `BasicLang/JavaScriptBackend.cs:1096-1127` |
| R8 | The WinForms template's entry point is `Module Program` / `Sub Main` calling `Application.EnableVisualStyles()`, `Application.SetCompatibleTextRenderingDefault(False)`, `Application.Run(New MainForm())` | `VisualGameStudio.ProjectSystem/Services/ProjectTemplateService.cs:627-643`; `BasicLang/ProjectSystem/TemplateEngine.cs:568` |
| R9 | `P2:` piece 2's library has no `Application` class (its spec and plan never mention one) | grep of `P2:` spec + plan |

### 1.4 Toolbar, persistence, Error List
| # | Fact | Where |
|---|---|---|
| U1 | The toolbar has ONE configuration combo (`Configurations` = hard-coded `{Debug, Release}`, `CurrentConfiguration`), then Build, then ▶ Start. No platform combo; no startup-project combo | `MainWindow.axaml:385-404`; `MainWindowViewModel.cs:172-175`, `:3505-3508` |
| U2 | The status bar also shows `CurrentConfiguration` | `MainWindow.axaml:543-548` |
| U3 | The selected configuration is NOT persisted; the default comes from setting `build.defaultConfiguration` | `MainWindowViewModel.cs:719`, `:3296-3306` |
| U4 | Per-project per-user state: `~/.vgs/workspaceStorage/<sha256(dir)>/state.json`, model `WorkspaceStateModel` (layout, open documents, active document). ⚠ A `Version` mismatch DISCARDS the whole state | `VisualGameStudio.Core/Models/WorkspaceState.cs:9-33`; `VisualGameStudio.ProjectSystem/Services/WorkspaceStateStore.cs:53-99` (`:67`) ; capture/restore `MainWindowViewModel.cs:1709`, `:1727-1729`, `:1774-1777` |
| U5 | No `.user` files exist (`*.user` appears only in the generated `.gitignore`) | `ProjectTemplateService.cs:1365` |
| U6 | Error List: one `DiagnosticsAggregator` with three keyspaces — LSP per file, build (replaced whole per build), extensions; filters are severity only; `DiagnosticItem.Source` exists and is ignored by the list | `VisualGameStudio.Core/Utilities/DiagnosticsAggregator.cs:18-75`; `MainWindowViewModel.cs:1887-1926`, `:1017-1039`; `Shell/ViewModels/Panels/ErrorListViewModel.cs:17-108` |
| U7 | Startup project is a Solution Explorer context-menu choice, stored as `DefaultProject` (`IsStartupProject` is not saved) | `SolutionExplorerViewModel.cs:552-558`; `BasicLangSolution.cs:29-62`; `SolutionService.cs:229-238` |

### 1.5 The editor
| # | Fact | Where |
|---|---|---|
| L1 | `P2:` the LSP reads the project's backend and the Debug configuration's DefineConstants and asks `BuildSymbols.For(backend, "Debug", …)`; no `.blproj` → `DESKTOP`+`DEBUG` | `P2:BasicLang/LSP/LspProjectContext.cs:86-91`, `:259-260`, `:494` |
| L2 | `P2:` the symbol key is part of the document cache stamp, so a changed symbol set re-preprocesses; inactive lines are emitted as `comment` semantic tokens; no completion on an inactive line | `P2:BasicLang/LSP/DocumentManager.cs:117`, `:549-552`; `P2:SemanticTokensHandler.cs:189`; `P2:CompletionService.cs:85` |
| L3 | `P2:` the LSP adds `dom-core.bli` for a web backend; the library joins via `WebDeclarationFiles()` (P2 plan Task 34) | `P2:LspProjectContext.cs:457-470` |
| L4 | The IDE DOES request and paint semantic tokens (`RefreshSemanticTokensAsync`, debounced after text changes; `SemanticTokenHighlighter`) — so the IDE editor will dim once the server reports inactive lines (P2 plan Task 6 left this "unverified") | `MainWindowViewModel.cs:3025-3044`, `:8445-8458`; `VisualGameStudio.Editor/Highlighting/SemanticTokenHighlighter.cs:21`; `LanguageService.cs:2571-2585` |
| L5 | The IDE's LSP client has a generic `SendNotificationAsync`; the server is OmniSharp's `LanguageServer.From` with handler registration | `VisualGameStudio.ProjectSystem/Services/LanguageService.cs:1587`; `BasicLang/LSP/BasicLangLanguageServer.cs:24-64` |

### 1.6 Piece 2 artefacts piece 3 consumes (P2 plan `docs/superpowers/plans/2026-09-29-portable-control-library.md`)
| Piece-2 task | What piece 3 needs from it | Status on `c722b644` |
|---|---|---|
| 1 `#If`/`#ElseIf`/`#Else`/`#End If`; 3 build symbols | the `#If DESKTOP` wrap; WEB/DESKTOP by backend | ✅ on the branch, not on master |
| 6 LSP `#If` blanking | editor dimming per platform | ✅ on the branch |
| 7d WinForms .NET resolution (DECIDED in memory) | Desktop-platform IntelliSense / checking | ⏳ |
| 28 library skeleton + inclusion hook `WithWebFormsLibrary` | the code-behind compiles on Web | ⏳ |
| 29 `Control`/`Form` core | `Inherits Form` on Web | ⏳ |
| 30 portable codegen — **tags every generated line** ("the seam for piece 3", plan `:3321`) | the `#If DESKTOP` wrap | ⏳ |
| 34 build errors + editor awareness (`WebDeclarationFiles`) | LSP library on Web | ⏳ |
| 41 the default flip (every new web form portable) | — (piece 3 makes `.blform` the one form) | ⏳ |
| 46 badges, drops, `DesktopOnlyKind` | P-D6 on a dual-platform `.blform` | ⏳ |

## 2. Decisions

Each **DECIDED** item names the alternative(s) that lost. "VS" = Visual Studio 2022's behaviour, the reference where one
exists.

### T-D1 — The concept is a PLATFORM, declared by the project, chosen in the IDE (VS's Solution Platforms)
A project lists its platforms in a new `<Platforms>` property (`Desktop;Web`); the IDE has one active platform. This is
VS's model exactly: an SDK project lists `<Platforms>AnyCPU;x64</Platforms>`, the toolbar's **Solution Platforms** combo
picks the active one, and `$(Platform)` is not stored in the project.
- ✗ *Two projects (a desktop project and a web project sharing files)* — every form and code-behind would be linked into
  two projects, every "Add New Form" would have to add to both, the startup project would decide the target (so the
  switch would be a startup-project picker), and the LSP would see each file in two contexts. Rejected: P-D4 says "one
  project builds either way".
- ✗ *Repurpose `<TargetBackend>` and let the switch REWRITE the `.blproj`* — the switch would dirty a committed file on
  every click, and two developers on one repo would fight over it. VS never writes the platform into the project.
- ✗ *MSBuild-style conditional groups (`Condition="'$(Platform)'=='Web'"`)* — a third condition evaluator across two
  parsers that today understand only `$(Configuration)` (T1, T2, T4). Rejected for piece 3; recorded as the route if
  per-platform property VALUES are ever wanted.

### T-D2 — Platform → backend is FIXED: `Desktop` = C#, `Web` = JavaScript
A dual-platform project has no backend choice per platform. `Desktop` means the managed WinForms build (C#); `Web` means
the JavaScript site. One pure function owns it (§4.2).
- ✗ *A `<DesktopBackend>` property allowing C++* — no form designer, no library, no WinForms on C++; nothing to build.

### T-D3 — Backward compatibility: no `<Platforms>` = ONE platform derived from `<TargetBackend>`
A project without `<Platforms>` builds exactly as today: JavaScript → `Web`; C# (and MSIL/LLVM, out of scope) →
`Desktop`; C++ → `Desktop (native)`, which is never switchable. Nothing is written to an existing project until the user
adds a platform (§7). With `<Platforms>` present, the FIRST listed is the **default platform**, and `<TargetBackend>` is
written equal to the default platform's backend on every save — so an older IDE or CLI that knows nothing of
`<Platforms>` still builds the default platform correctly.

### T-D4 — The active platform is per user, per workspace, in the existing workspace state
`WorkspaceStateModel` gains `ActivePlatform` (string, nullable) and — VS parity, same mechanism — `ActiveConfiguration`
(U3: today not persisted). Stored in `~/.vgs/workspaceStorage/<hash>/state.json` beside the layout (U4). ⛔ The
schema `Version` is NOT bumped: a bump discards every user's saved layout (`WorkspaceStateStore.cs:67`), and a new
nullable property needs none (System.Text.Json leaves it null in an old file). Null / unknown / not declared → the
project's default platform.
- ✗ *A `.blproj.user` file beside the project (VS's `ActiveDebugProfile` home)* — it lands in commits for anyone without
  the template's `.gitignore`, and the CLI would be tempted to read it, making a CI build depend on a developer's last
  click. VS keeps the active solution platform in the hidden `.suo`, i.e. per-user state — the workspace store is ours.
- ✗ *`<project>/.vgs/settings.json`* — per-project but shared through the repo for anyone who commits `.vgs/`.

### T-D5 — One active platform for the workspace; each project maps to it (VS's solution → project mapping, simplified)
With a solution open, the toolbar's platform applies to every project: a project that declares the active platform
builds it; a project that does not builds its default platform, and the build log says so on one line
(`"<Project>: Web is not one of its platforms — building Desktop"`). F5 runs the startup project (U7) on the active
platform; if the startup project lacks it, F5 offers to add it (§7.1) rather than silently running the other target.
- ✗ *A per-project active platform* — two combos' worth of state for one decision; VS has one Solution Platforms combo.

### T-D6 — The toolbar: a second combo beside Debug/Release, always offering both platforms
`[Debug ▾] [Desktop ▾]  Build  ▶ Start (Desktop)` — the combo sits immediately right of the configuration combo
(`MainWindow.axaml:385-390`), as VS places Solution Platforms right of Solution Configurations.
- Items: **Desktop** and **Web**, always both. A platform the startup project does not declare shows as
  `Web (add to project…)`; choosing it runs the add-platform step (§7.1); cancelling restores the previous selection.
  (VS's equivalent is the combo's `Configuration Manager…` item; ours is narrower and needs no extra dialog.)
- A C++ project: the combo shows `Desktop`, disabled, tooltip *"C++ projects build for the desktop only."*
- **Disabled while debugging or while a web preview runs** (VS disables both combos in a debug session); Stop re-enables.
- The ▶ Start button's text names the platform (`▶ Start (Desktop)` / `▶ Start (Web)`), its tooltip says what F5 will do
  (*"Build for the web and open the page in your browser (F5)"*). The status bar shows `Debug | Web` (U2).
- Command palette: `Platform: Desktop`, `Platform: Web`. No default key binding (VS has none).
- `AutomationProperties.Name="Target Platform"` (the configuration combo's convention).
- ✗ *A segmented two-button toggle* — reads well for two values but does not match VS, has no room for the add-platform
  affordance, and is a new control style in this toolbar.
- ✗ *Hide `Web` for a project that does not declare it* — then the owner's switch would be invisible on every existing
  WinForms project, which is exactly where it is wanted.

### T-D7 — Build output is separated per platform: `bin\<Platform>\<Configuration>\`
For a project WITH `<Platforms>`, outputs go to `bin\<Platform>\<Configuration>\` and intermediates to
`obj\<Platform>\<Configuration>\` (MSBuild's own `bin\x64\Debug` convention), on BOTH routes, from ONE function in
BasicLang that the IDE and the CLI call (§4.3) — so the two routes stop disagreeing (B4) for these projects. Projects
without `<Platforms>` keep their exact current folders.
- Why: both platforms in one folder would put `index.html` next to the `.exe`, let the web preview server serve the
  desktop binaries, and leave a stale page from a previous web build beside a fresh desktop build.
- ✗ *`bin\<Configuration>\<platform>`* — inverts MSBuild's order for no gain.

### T-D8 — The CLI: `build --platform=Desktop|Web|All`
`BasicLang.exe build App.blproj --platform=Web` (case-insensitive). Absent → the default platform. `All` builds each
declared platform in declared order, each into its own folder, and fails if any fails. A platform the project does not
declare is an ERROR naming the declared ones — never a fallback (the "missing arm silently builds C#" trap, B2).
`--target=` keeps its single-file meaning (a backend name) and stays ignored by `build` (B1); the VSIX is unchanged
(R2 in §10).
- ✗ *Reuse `--target=web` on `build`* — the VSIX already passes `--target=csharp` for EVERY project, JavaScript ones
  included (B2); the day `build` honoured `--target`, every VSIX build of a web project would silently become a C# build.

### T-D9 — `.blform` is the ONE form document of a dual-platform project
In a project whose platforms include Desktop, "Add New Form" creates a `.blform` (unchanged — `UseWindowsForms` is true,
T8). A Web build of that project loads its `.blform` documents and emits each form's page from them through piece 1's
pixel path (Canvas semantics: a `.blform` already stores exactly what a Canvas web form stores — piece 1 D2). The
`.blform` page is always a PORTABLE page (piece 2 §7.7).
- `MobileBreakpoint` (piece 1 §2.3, today web-Canvas only, on `<Layout>`) becomes a root row that ALSO applies to a
  `.blform`, stored as a ROOT attribute (a `.blform` has no `<Layout>`), shown in the grid only when the project declares
  Web, never emitted into WinForms code (the WinForms parity oracle gets the same web-only exemption piece 1 gave it).
  Piece 1's retarget sweep then carries it (no longer a BL8024 drop for Canvas → WinForms).
- A `.blwebform` in a dual-platform project still builds its page on Web; on Desktop it is an error,
  `WebOnlyFormOnDesktop`, naming the file and saying piece 4's conversion makes it a `.blform` (until piece 4 lands:
  "remove it from the project or keep the project web-only").
- ✗ *Keep two documents per form (`.blform` + `.blwebform`)* — two designs of one form drift; P-D5 retires the second.
- ✗ *A new extension for the shared form* — a third document kind, a third reader vocabulary, and piece 4 would convert
  into it instead of into the format every WinForms project already has.

### T-D10 — The code-behind: ONE init region; geometry and property lines wrapped in `#If DESKTOP`
The region writer emits a `.blform`'s init region as the WinForms init with piece 2's tagged geometry/property lines
(P2 §6.2) wrapped in `#If DESKTOP Then … #End If` — tray-component property lines excepted, `x.Name = "x"` NOT wrapped.
Under `WEB` the region compiles to exactly piece 2's portable web init; under `DESKTOP` to exactly the WinForms init.
- **For EVERY `.blform`, dual-platform or not** — one shape. Adding Web to a project then changes no region, and the
  region writer needs no knowledge of the project (the CLI `design` verb has none).
- Consecutive wrapped lines share one `#If` block (a control's geometry + property run is one block), so the region
  stays readable; the exact grouping is a plan detail pinned by golden files.
- ⚠ Every existing `.blform` region changes once (the hash is rewritten on the next designer save — the region is Canon,
  as piece 2 §6.3 already does for its `Name` line). Landing piece 3's region change in the same release as piece 2's
  avoids a second rehash for users.
- ✗ *Wrap only in dual-platform projects* — "Add Web" would then rewrite every form's region at the moment of adding,
  the region writer would need the project, and the CLI `design` verb would need a project argument.
- ✗ *Two init regions (`#If DESKTOP` region + `#If WEB` region)* — duplicates the `New`/`Name`/`AddHandler`/`Controls.Add`
  walk, the very lines P2 §6.2 proved identical.

### T-D11 — The startup form: a `<StartupForm>` project property; `Application` joins the web library
- `.blproj` gains `<StartupForm>MainForm</StartupForm>` (VB's "Startup form"; the property `AppendStartupFormPage`'s
  comment already anticipates, R4). Web F5/Ctrl+F5 open `<StartupForm>.html`. Written by the template and by the
  add-platform step (§7.1), which proposes the form from a scan of `Main` for `Application.Run(New X())` (asked of the IR,
  as `UserCodeCallsDispatch` asks, R7) and asks the user to pick when the scan finds none or several.
- **`Application` is added to the portable library** (piece 2 has none, R9): `EnableVisualStyles()` and
  `SetCompatibleTextRenderingDefault(Boolean)` are no-ops; `Run(form As Form)` ADOPTS `form` as the page's form when its
  class is the page's form (the dispatch then starts it instead of constructing a second instance) and is otherwise a
  run-time error naming both forms; `Exit()` closes the page's form (`Form.Close`, piece 2 O12). Any other member is
  piece 2's `WebUnavailableMember`. So the template's `Main` (R8) compiles and means the same thing on both platforms.
- **On the web, `Main` runs only on the startup form's page**; any other form page starts its own form directly (the
  generated entry point compares the page's `data-form` with the startup form, which the build knows). Without this, a
  second form's page would run `Main`, construct the STARTUP form, and attach it to elements that page does not have.
- A Web build warns when `Main`'s `Application.Run(New X())` names a form other than `<StartupForm>`
  (`StartupFormMismatch`), and errors when `<StartupForm>` names no form in the project (`StartupFormMissing`).
- ✗ *Wrap the template's `Main` body in `#If DESKTOP`* — every existing WinForms project's `Main` would need a hand edit
  to build for the web; the library route makes the unchanged `Main` portable.
- ✗ *Derive the startup form from the IR scan alone* — `Dim f As New MainForm() : Application.Run(f)` and factory calls
  defeat it; a scan is a good PROPOSAL, a property is a reliable ANSWER.

### T-D12 — F5 on Web runs without the debugger and says so once
F5 with Web active builds and opens the page exactly as Ctrl+F5 does (today's behaviour for JS projects, R2), and the
first F5 of a session shows an info notification: *"Breakpoints are not hit on the Web platform yet. The page is open in
your browser; use its developer tools (F12) — source maps are included."* with a "Don't show again" action. Breakpoints
in the editor stay (they apply when you switch back to Desktop). Shift+F5 stops the preview server (R6).
- ✗ *JS debugging through the browser's DevTools protocol (Edge `--remote-debugging-port` + a DAP↔CDP bridge, or VS Code's
  js-debug adapter through the extension host)* — a debugger of its own (launch, source maps to `.bas` lines, breakpoints,
  stepping, variables) larger than this whole piece. Recorded as the follow-up; nothing here blocks it (the platform
  model already routes F5 per platform).
- ✗ *F5 refuses on Web* — punishes the user for the IDE's gap; VS never refuses F5 for a run-only target.

### T-D13 — The editor follows the active platform through ONE custom LSP notification
The IDE sends `basiclang/didChangeActivePlatform { projectUri, platform }` on project open (after `initialized`), on
every switch, and after an LSP restart. The server keeps a per-project override; with none it uses the project's default
platform; the backend for `BuildSymbols.For` is the platform's backend (T-D2). On a change the server re-diagnoses every
open document of that project (the symbol key already invalidates the cache, L2) and the IDE re-requests semantic tokens
for every open document (`RefreshSemanticTokensAsync`, L4); a client that supports it also gets
`workspace/semanticTokens/refresh`.
- ✗ *`workspace/didChangeConfiguration`* — settings are workspace-wide; the platform is per project.
- ✗ *`initializationOptions`* — fixed at start; a switch would need a server restart.
- ✗ *The LSP reads the IDE's workspace store* — couples the server to one client's private files; VS Code has none.

### T-D14 — Switching clears the other platform's build diagnostics; build diagnostics carry their platform
On a switch the Error List's BUILD keyspace is cleared (it describes the other platform's last build — U6) and the LSP
keyspace refreshes through T-D13. `DiagnosticItem` gains `Platform` (null for non-platform builds), the Error List shows
a **Platform** column only when some item has one, and the Output's build header names it
(`Build started: App — Debug | Web`).
- **Build → Build All Platforms** (VS's Batch Build, narrowed): builds every declared platform of the startup project (or
  every project in a solution), merging the diagnostics, each tagged — the cheap way to learn that a desktop change broke
  the web build. Same as CLI `--platform=All`.
- ✗ *Keep the other platform's build errors after a switch* — they point at code that is, after the switch, possibly
  dimmed and not built; a user fixes errors that are not there.

### T-D15 — Desktop-only badges follow the project's DECLARED platforms, never the active one
A `.blform` in a project whose platforms include Web shows piece 2's "desktop" badge on desktop-only kinds (toolbox,
property-grid header via the ONE selection store, CLAUDE.md); a Web build stops with `DesktopOnlyKind` (piece 2 §9). The
designer, canvas and toolbox do NOT change when the toolbar switches — the form is one form.
- ✗ *Badges only while Web is active* — a toolbox that changes under the user's hand on a toolbar click, and a desktop-
  only control dropped "while on Desktop" that silently breaks the web build later.

### T-D16 — WinForms-only property values on a `.blform` built for Web: a WARNING per property
A row whose `Targets` is WinForms-only (e.g. DateTimePicker `Format`) set to a non-default value on a `.blform` is reported
on a Web build as `DesktopOnlyProperty` (warning, naming form, control and property: *"'dtpStart.Format' has no web
equivalent and is ignored on the web"*). The page is built without it. Code that SETS such a member is piece 2's
`WebUnavailableMember` error, unchanged.
- ✗ *Error* — the value is cosmetic and P-D6's "web versions arrive over time" applies to properties too; a desktop-first
  form should not need edits to build for the web unless a CONTROL is unavailable.

### T-D17 — New WinForms projects are dual-platform by default
The Windows Forms App template (IDE `ProjectTemplateService` and CLI `TemplateEngine`, both — they mirror) writes
`<Platforms>Desktop;Web</Platforms>` and `<StartupForm>MainForm</StartupForm>`. Desktop stays the default (first).
The JavaScript/web template is unchanged until piece 4 (a web-only project may be a plain DOM site).
- ✗ *A third template "Forms App (Desktop + Web)"* — a choice the user cannot make well at creation time and can make
  later with one click (§7.1).

## 3. The user experience

### 3.1 The toolbar and menus
```
[Save All] | [Debug ▾] [Desktop ▾]  Build | ▶ Start (Desktop)  ⏸ ■ …
                       ├ Desktop
                       └ Web                (or "Web (add to project…)")
```
- **Build (Ctrl+Shift+B)** builds the active platform. **Build All Platforms** (Build menu) builds every declared one.
- **Start Without Debugging (Ctrl+F5)**: Desktop runs the exe (today's path); Web builds, starts the preview server on
  `bin\Web\<Config>\` and opens `<StartupForm>.html`.
- **Start Debugging (F5)**: Desktop debugs (today's path); Web = Ctrl+F5 plus the one-time notice (T-D12).
- Status bar: `Debug | Web`.

### 3.2 What changes visibly on a switch
| Surface | Changes |
|---|---|
| Toolbar | combo value; ▶ Start label and tooltip |
| Editor | `#If WEB` / `#If DESKTOP` branches: the inactive one dims (comment-coloured semantic tokens), loses diagnostics and completion; the active one gains them |
| Error List | build diagnostics cleared; LSP diagnostics re-published for the new platform (e.g. a `WebUnavailableMember` in shared code appears on Web) |
| Output | nothing until the next build, whose header names the platform |
| Designer / toolbox / property grid | **nothing** (T-D15) |
| Files | **nothing** — a switch writes no project or source file (only the per-user workspace state) |

### 3.3 What a desktop user sees that they did not before
Only the platform combo, `▶ Start (Desktop)`, and — in a project that declares Web — desktop badges and
`MobileBreakpoint` in the form's property grid.

## 4. Data model

### 4.1 `.blproj` additions
```xml
<PropertyGroup>
  <TargetBackend>CSharp</TargetBackend>          <!-- = the default platform's backend (T-D3) -->
  <Platforms>Desktop;Web</Platforms>             <!-- new; first = default -->
  <StartupForm>MainForm</StartupForm>            <!-- new; a form class name in this project -->
  <OutputType>WinExe</OutputType>
  <UseWindowsForms>true</UseWindowsForms>        <!-- read by the Desktop build only -->
  …
</PropertyGroup>
```
- `<Platforms>`: `;`-separated, trimmed, case-insensitive, de-duplicated, each `Desktop` or `Web`. An unknown entry, an
  empty list, or `<Platforms>` on a C++ project is a project-load ERROR naming the entry (never ignored — an ignored
  `Web` would silently build C#).
- Both parsers (T1, T4) read them into their models; both savers write them: `ProjectSerializer.SavePreservingAsync`
  (in place) and ⛔ `ProjectFile.Save` (T3 — without this, `BasicLang.exe add package` would silently strip
  `<Platforms>` and the project would quietly become desktop-only).
- `UseWindowsForms`, `OutputType=WinExe`, `TargetFramework` apply to the Desktop build only; the Web build ignores them,
  as a JS build does today.

### 4.2 One function owns the platform answer — BasicLang `ProjectPlatforms`
`BasicLang/ProjectSystem/ProjectPlatforms.cs` **[impl: name]**, in the compiler assembly so the CLI, the IDE build, the
LSP and the tests share it (the IDE model passes the raw strings; the IDE references BasicLang already):
- `Declared(platformsText, targetBackend, language) → IReadOnlyList<Platform>` — T-D3's rule, the load error, the default.
- `BackendOf(Platform) → "csharp" | "javascript"` — T-D2.
- `Resolve(declared, requested) → (Platform platform, string? note)` — T-D5's mapping and its log line.
- `Platform` is `Desktop | Web | DesktopNative` (the C++ case, never switchable).
⛔ No other code compares a backend to decide a platform, or a platform to decide a backend — the four IDE sites in T7
and the CLI's `project.Backend` reads (B1) call it. (The mirrored-pair rule of CLAUDE.md: two parsers, ONE answer.)

### 4.3 One function owns the output layout — `ProjectOutputLayout.For(projectDir, platforms, platform, configuration)`
Returns `bin\<Platform>\<Configuration>\` and `obj\<Platform>\<Configuration>\` for a project with `<Platforms>`, and the
EXISTING per-route folders otherwise (the IDE's `OutputPath`, the CLI's `bin/<config>/<TFM>/`) — legacy layouts are not
unified by this piece (out of scope; recorded). `FormDispatch.Write`'s `obj/<config>` (`BuildService.cs:647-648`) takes
the same answer.

### 4.4 IDE models
- `BasicLangProject` (Core): `Platforms` (string, raw) and `StartupForm` (string?); a computed `DeclaredPlatforms` via
  §4.2.
- `WorkspaceStateModel`: `ActivePlatform`, `ActiveConfiguration` (T-D4). Captured with the layout; a switch marks the
  state dirty through the existing debounced save.
- `BuildService`: `CurrentPlatform` beside `CurrentConfiguration` (`BuildService.cs:21-35`), set by the shell exactly as
  the configuration is (`MainWindowViewModel.cs:3505-3508`).
- `MainWindowViewModel`: `Platforms` (the two items + add affordance), `CurrentPlatform`, `CanSwitchPlatform`
  (false while debugging / previewing), `StartButtonText`.

## 5. Build and run per platform

### 5.1 Desktop
Exactly today's C# route (`CompileWithBasicLangApiAsync` → `GenerateCode` csharp → csproj → `dotnet build`), with
`TargetBackend = "csharp"` from §4.2 (so `DESKTOP` is defined, B9), output from §4.3. The `.blform` documents load only
for asset copying (B6) — their WinForms code is the region in the `.bas`. A `.blwebform` in the project →
`WebOnlyFormOnDesktop` (T-D9).

### 5.2 Web
`TargetBackend = "javascript"` (so `WEB` is defined; the library is included by piece 2's trigger, because a `.blform`
code-behind has `Using System.Windows.Forms`). Form documents: `.blwebform` as today PLUS every `.blform`, each emitted
as a portable pixel page (T-D9). Checks before compile, raised where web forms load on BOTH routes (the IDE and CLI
mirror, B6): `DesktopOnlyKind` (piece 2), `DesktopOnlyProperty` (T-D16), `StartupFormMissing`, a stale region
(§8.3). The dispatch is generated as today; the entry point applies T-D11's startup-page rule. Output: §4.3.

### 5.3 Running
| | Ctrl+F5 | F5 | Shift+F5 |
|---|---|---|---|
| Desktop | `DebugService.StartWithoutDebuggingAsync` on `bin\Desktop\<Config>\<Name>.exe` | managed DAP (R3) | DAP stop |
| Web | `WebPreviewServer` on `bin\Web\<Config>\`, default browser on `<StartupForm>.html` | = Ctrl+F5 + T-D12 notice | stop the preview server |
The F5/Ctrl+F5 branch (R2) asks §4.2 for the active platform's backend, not `project.TargetBackend`.

### 5.4 Debugging support per platform
Desktop: full (managed DAP). Desktop native (C++): unchanged (lldb-dap). Web: none in this piece (T-D12); the source map
the JS emitter already writes makes the browser's own debugger usable.

## 6. LSP behaviour
- §T-D13's notification; the server's `LspProjectContext` resolves `(project, platform) → backend` through §4.2 and passes
  it to `BuildSymbols.For` where it passes `projectFile.Backend` today (`P2:LspProjectContext.cs:494`, `:259-260`), and to
  `WithWebDeclarations` (L3) — so on Web the server also knows `dom-core.bli` and the library, on Desktop the .NET
  WinForms surface (piece 2 Task 7d).
- The editor's configuration stays Debug (L1, unchanged) — the toolbar's configuration does not reach the LSP in this
  piece (recorded: VS does follow the configuration too; a later one-line change through the same notification).
- A file outside any `.blproj`: unchanged (`DESKTOP`).
- The IDE re-sends the platform after the server's auto-restart (the notification is idempotent).

## 7. Migration

### 7.1 Adding a platform (the only step that writes the project)
Triggered by choosing an undeclared platform in the combo, by F5 with an undeclared active platform on the startup
project, or by **Project → Add Platform…**. One confirmation dialog lists what will be written, then writes it through
`SavePreservingAsync`:
- **WinForms project + Web**: `<Platforms>Desktop;Web</Platforms>` (Desktop stays default), `<StartupForm>` (proposed by
  the IR scan, else picked from the project's forms); the dialog LISTS — does not fix — desktop-only controls (the future
  `DesktopOnlyKind` errors) and forms whose regions are stale or hand-edited (§8.3). Every `.blform` region is
  regenerated through the region writer (Canon regions only; a hand-edited region, BL8011, is listed and left alone).
- **JavaScript project + Desktop**: refused while the project contains any `.blwebform` (*"Its web forms must be converted
  first"* — piece 4's convert-on-open will make this possible); otherwise `<Platforms>Web;Desktop</Platforms>` (Web stays
  default), and `OutputType`/`UseWindowsForms` only if the project has `.blform` documents (it cannot, before piece 4 — so
  in piece 3 a JS project gains a CONSOLE desktop build).
- **C++ project**: not offered.
- Cancel writes nothing and restores the combo.

### 7.2 Existing projects that are never migrated
WinForms-only, JS-only and C++ projects open, build and run byte-for-byte as today: no `<Platforms>`, same output folders,
same backend. The combo shows their one platform; the only visible change is the combo itself.

### 7.3 `.blwebform`
Unchanged in piece 3 (builds its page on Web; `WebOnlyFormOnDesktop` in a dual project's Desktop build). Piece 4 converts
it to a `.blform` on open; after piece 4, §7.1's JS-project rule loses its refusal.

## 8. Interaction with piece 2 and piece 4

### 8.1 What piece 3 can build BEFORE piece 2's library lands (Phase A — base: master)
Everything that does not compile a form for the web: §4 data model (both parsers, both savers, `ProjectPlatforms`,
`ProjectOutputLayout`); CLI `--platform`; IDE `CurrentPlatform` through the build; output layout; F5/Ctrl+F5 routing;
the toolbar combo, status bar, persistence, disabled-while-debugging; the Error List platform column, clearing on switch,
Build All Platforms; the add-platform dialog for the JS → Desktop case and for projects without forms; the LSP
notification plumbing (with `BuildSymbols` absent on master, the server passes the platform's backend to what it uses
today — the visible symbol effect arrives with piece 2's merge, by construction, because `BuildSymbols.For` keys on the
backend). A dual-platform project WITHOUT forms (e.g. a console program) builds and runs on both platforms at the end of
Phase A — that is Phase A's acceptance test.
⚠ Until Phase B, the add-platform step REFUSES a project containing `.blform` documents, with the reason (*"web builds
of Windows Forms arrive with the portable control library"*) — never a Web build that silently has no pages.

### 8.2 What needs piece 2 (Phase B)
| Piece-3 item | Needs piece-2 task |
|---|---|
| `#If DESKTOP` wrap in the region (T-D10) | 1 + 3 (`#If`, symbols) and **30** (the tagged lines) |
| `.blform` → portable page (T-D9) | 28–33 (library, codegen style, rendering fixes); piece 2's page style for a `.blform` |
| `Application` in the library (T-D11) | 29 (`Form` core), 34 (`WebUnavailableMember`) |
| Badges + `DesktopOnlyKind` on a dual `.blform` (T-D15) | **46** |
| Editor dims per platform (T-D13) | 6 (already on the branch) |
| Desktop IntelliSense on WinForms members | 7d |
| The template default flip (T-D17) | 41 (piece 2 makes web forms portable by default) — ordering only |
Piece 3's Phase B starts when piece 2's 2a has merged to master; Tasks 46–47 (2d) must merge before piece 3's gate.

### 8.3 The stale-region case (between pieces)
A `.blform` whose code-behind still has a pre-T-D10 region (Canon but unwrapped) builds for Desktop unchanged; a Web
build stops with `StaleDesignerRegion` (error, naming the file: *"Open the form in the designer (or run Add Platform) to
regenerate its code for both platforms."*). The build never regenerates in memory — it compiles the file the user sees.

### 8.4 Piece 4
- Piece 4's convert-on-open turns a `.blwebform` into a `.blform` (pixel geometry is nearly a rename — piece 1 §0); after
  it, a web-only project's forms are `.blform` too, and §7.1's JS-project refusal is removed.
- Piece 4 should treat `MobileBreakpoint` on `<Layout>` (`.blwebform`) → ROOT attribute (`.blform`, T-D9) as part of
  the conversion.
- Nothing in piece 3 assumes `.blwebform` persists; `WebOnlyFormOnDesktop` is retired by piece 4.

## 9. Testing

Repo rules apply throughout: validate codegen through the CLI AND the IDE build (`CompileProjectFiles`); the IR optimizer
on (`CompileToCppOptimized`-style helpers, or the CLI); Windows-only prerequisites SKIP with a reason
(`[Platform(Include = "Win")]`, Edge-absent skips, `NativeBuildSkip`); `TestSkip.IgnoreEvenInsideMultiple` inside
`Assert.Multiple`.

### 9.1 Pure functions (table tests, run everywhere)
`ProjectPlatforms.Declared` / `BackendOf` / `Resolve` — every legacy backend, every `<Platforms>` spelling, the load
errors, the default, the solution mapping note. `ProjectOutputLayout.For` — with and without `<Platforms>`. The
startup-form IR scan (literal `New X()`, a local, a factory, none, two).

### 9.2 The project file
Round trip through BOTH savers: `SavePreservingAsync` keeps `<Platforms>`/`<StartupForm>` and every unknown element;
⛔ `ProjectFile.Save` (via a real `PackageManager` add) keeps them. `<TargetBackend>` follows the default platform.
A legacy project reads, builds and saves byte-for-byte.

### 9.3 Both routes, both platforms
For one dual-platform project: CLI `build --platform=Desktop|Web|All` and the IDE `BuildService` with each platform
produce the same output FILES in the same folders (§4.3), and a platform the project does not declare is an error on
both. The symbol set on each (`#If WEB` / `#If DESKTOP` picks the right branch — Phase B, after `BuildSymbols` merges).

### 9.4 The real IDE view (Avalonia.Headless + Skia, `[AvaloniaTest]`)
- Read `MainWindow.axaml` for the combo's bindings (CLAUDE.md: a command or property nothing binds is unreachable) and
  host the real `MainWindow` toolbar: selecting Web sets `BuildService.CurrentPlatform`, the Start label, the status bar.
- Persistence: switch, close the project, reopen — through the real `WorkspaceStateStore` in a temp root — Web is
  restored; an old `state.json` without the field restores the layout AND defaults the platform (no version discard).
- Disabled during a (faked) debug session and a running preview.
- The add-platform dialog through the real generated command with a fake `IDialogService`: confirm writes, cancel writes
  nothing and restores the combo; the refusal cases.
- The Error List: switch clears build items; Build All Platforms tags items; the Platform column appears only when tagged.

### 9.5 LSP sessions
A real server session: open a file with `#If WEB … #Else … #End If` in a dual project; send the notification for Web
then Desktop; assert inactive lines, diagnostics on the right branch, completion refused on the inactive line, and that
the IDE's `RefreshSemanticTokensAsync` runs for each open document. Restart the server: the IDE re-sends the platform.

### 9.6 Code generation (Phase B)
Golden regions per catalog fixture: the T-D10 shape; the relation "under DESKTOP = WinForms init, under WEB = piece 2's
portable init" proven by preprocessing the region both ways and comparing with piece 2's two generators (the P2 §11.6
pin, now over one region). `WinFormsCatalogSweepTests` (csc) compiles the wrapped region with `DESKTOP`; a node-run web
build compiles it with `WEB`.

### 9.7 Run-the-app acceptance, both platforms (Integration, Windows-gated)
From the dual-platform WinForms TEMPLATE, through the real entry points (the template service, `BuildService`, the CLI):
add a Button and a Label in the designer, double-click the Button, write `lblMsg.Text = "Hi"` in the handler — then
1. **Desktop**: build, RUN the WinForms window, `PerformClick` (shown, visible, enabled — CLAUDE.md), read the Label.
2. **Web**: build, serve, Edge headless (piece 1's harness, `localhost`, fresh `--user-data-dir`, kill by PID) on
   `<StartupForm>.html`, click through CDP (piece 2's twin harness), read the Label.
Both show `Hi`; the SAME `.bas` file (byte-identical, asserted) built both. Also: a second form's page does not run
`Main` (T-D11); `Application.Run` adopts (one instance — the handler fires once per click on Web).

### 9.8 Reviews, mutations, cadence
Programme rule: per task an implementer, a spec review and a quality review, and mutation checks; lean test runs —
named `--filter` rows per task, the fast subset (`TestCategory!=Integration`) at each review, Integration for the
touched categories, the full gate once per phase on the merged tree (compare sorted FAILURE NAMES, never counts).
Mutation checks on the load-bearing rules: platform → backend; legacy derivation; unknown platform = error (not
fallback); `ProjectFile.Save` keeping `<Platforms>`; `<TargetBackend>` sync; output folder per platform; the F5 branch
asking the platform, not `TargetBackend`; no version bump; the LSP override vs default; the build-keyspace clear; the
`#If DESKTOP` wrap set (one line moved in/out turns a test red on one platform); `Main` only on the startup page;
`Application.Run` adoption (a second instance must turn the click count red).

## 10. Risks
- **R1 — piece 2 slips.** Phase A is useful alone (dual console projects, the switch, the plumbing) and refuses `.blform`
  projects honestly (§8.1). One branch; it merges after Phase B unless piece 2 stalls, in which case Phase A merges alone
  with the refusal in place.
- **R2 — the "missing arm builds C#" trap.** The VSIX's map defaults to C# (B2). Piece 3 does not make `build` honour
  `--target`, rejects unknown platforms, and adds a test that `build --target=csharp` on a Web-default project still
  builds Web. A VSIX platform picker is a follow-up chip.
- **R3 — the lossy `ProjectFile.Save`** (T3) would silently strip `<Platforms>`; covered by §4.1 and §9.2. (It also drops
  `UseWindowsForms` today — a pre-existing defect, chipped, not fixed here.)
- **R4 — a site still reading `TargetBackend`** builds or runs the wrong platform with nothing looking wrong. §4.2 makes
  `ProjectPlatforms` the only answer; the plan's first task greps every `TargetBackend`/`Backend` read on both sides and
  lists each with its new call.
- **R5 — the output-folder move** for migrated projects: an old `bin\Debug` stays behind; tests and the preview server
  must use §4.3, never a literal. Legacy projects do not move.
- **R6 — LSP/IDE disagreement** after a server restart or a project reload; the notification is re-sent on both, tested.
- **R7 — `Main` on every page** (R7) and double construction; T-D11's startup-page rule and adoption, both mutation-checked.
- **R8 — every `.blform` region rehashes** (T-D10), a second time if piece 3 lands in a later release than piece 2's
  `Name` line. Coordinate the release; the expectation diff is intended and listed.
- **R9 — the WinForms catalog gates** gain a web-only root row on a WinForms document (`MobileBreakpoint`, T-D9); the
  parity oracle needs the exemption piece 1 used; the sweep must not emit it.
- **R10 — `IDE/` drop staleness**: the library's `Application` lands in `IDE/lib/js/forms/` (load-bearing, piece 2
  §12); the drop step lists it.

## 11. Task outline (ordered; not a plan)
**Phase A — base master, no piece-2 dependency**
1. Pre-flight: re-verify every §1 anchor on the base; grep all backend reads (R4); measure today's CLI and IDE output
   folders for a C# and a JS project; record.
2. `ProjectPlatforms` + `ProjectOutputLayout` (pure, table-tested) in BasicLang.
3. `.blproj` schema: both parsers, both savers (`SavePreservingAsync`, `ProjectFile.Save`), load errors, `<TargetBackend>`
   sync; round-trip tests incl. `PackageManager`.
4. CLI `build --platform=Desktop|Web|All`; output layout; R2's test.
5. IDE build: `BuildService.CurrentPlatform`, backend from §4.2, output layout, dispatch `obj` path; IDE = CLI parity test.
6. Run routing: F5 / Ctrl+F5 / Stop by active platform; preview server folder; `<StartupForm>` page; T-D12 notice.
7. Toolbar combo, Start label, status bar, command palette, disabled-while-running; persistence (`ActivePlatform`,
   `ActiveConfiguration`, no version bump); headless view tests with the AXAML binding read.
8. Error List: platform tag + column, clear on switch, Build All Platforms.
9. LSP: `basiclang/didChangeActivePlatform`, server override, re-diagnose, IDE token refresh, re-send on restart.
10. Add Platform (dialog, command, combo affordance, F5 offer): JS → Desktop and formless WinForms → Web; the `.blform`
    refusal (§8.1).
11. Phase A gate: a dual console project built and run on both platforms through both routes; fast + Integration named.

**Phase B — after piece 2's 2a has merged (and 2d before the gate)**
12. Region writer: T-D10 wrap over piece 2's tagged lines, golden files, both-way preprocess relation, csc sweep + node.
13. Web build of `.blform`: loader, portable pixel page, `MobileBreakpoint` root row (+ oracle exemption, retarget),
    `DesktopOnlyProperty`, `StaleDesignerRegion`, `WebOnlyFormOnDesktop`; IDE and CLI.
14. Library `Application` (+ coverage), startup-page entry-point rule, `StartupFormMismatch` / `StartupFormMissing`.
15. Badges and `DesktopOnlyKind` on dual `.blform` (consumes piece 2 Task 46); designer unchanged on switch.
16. Add Platform for WinForms projects with forms (region regeneration, startup-form proposal); lift the §8.1 refusal.
17. Templates (IDE + CLI) dual by default (T-D17); the run-the-app acceptance on both platforms (§9.7).
18. IDE drop (`IDE/`, incl. `lib/js/forms/`), HANDOFF, the merged-tree gate (trial merge in a detached worktree, then the
    named gate), owner click-through list.

## 12. Questions that would have gone to the owner — DECIDED
| # | Question | Decided | Why |
|---|---|---|---|
| Q1 | One project with two platforms, or two projects? | One project, `<Platforms>` (T-D1) | P-D4's words; VS's model |
| Q2 | Where is the active choice stored? | Per user, workspace state (T-D4) | VS's `.suo`; never dirties the repo |
| Q3 | Per project or per solution? | One for the workspace, mapped per project (T-D5) | VS's Solution Platforms |
| Q4 | What does F5 do on Web? | Runs without debugging + one-time notice (T-D12) | JS debugging is its own project |
| Q5 | Which browser? | The default browser (unchanged) | No regression; "Browse With…" later |
| Q6 | Which page opens? | `<StartupForm>.html` (T-D11) | Deterministic; the anticipated property |
| Q7 | Does the region carry `#If` for desktop-only projects too? | Yes, every `.blform` (T-D10) | One shape; adding Web rewrites nothing |
| Q8 | Desktop-only property values on Web? | Warning, ignored on the page (T-D16) | Cosmetic; controls are the hard line |
| Q9 | New WinForms projects dual by default? | Yes, Desktop default (T-D17) | Every form portable from the start |
| Q10 | Does the switch change the designer? | No (T-D15) | One form; badges follow declared platforms |
| Q11 | Persist Debug/Release too? | Yes, same store (T-D4) | VS parity; same mechanism, near-zero cost |
| Q12 | Does the editor follow the configuration too? | Not in this piece (§6) | Recorded; one field away |
