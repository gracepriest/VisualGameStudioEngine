# Visual Game Studio Engine — Claude Code Guide

Operating guide for this repo. **This is not a changelog.** History lives in
`git log`, design rationale in `docs/superpowers/{plans,specs}/`, and current work
status in the auto-memory (`MEMORY.md`, loaded automatically each session). Keep
this file durable — don't append dated bug-fix logs.

## What this is

BasicLang (a VB-like language + compiler), a cross-platform IDE (Avalonia), and a
2D game engine (C++/Raylib) with a VB.NET P/Invoke binding layer.

## Projects (`VisualGameStudioEngine.sln`)

**Compiler**

| Project | Lang | Role |
|---|---|---|
| BasicLang | C# | Compiler: preprocess → lex → parse → semantic → IR → optimize → backends (C#, C++, JavaScript, LLVM, MSIL); also runs as `--lsp` server and `--debug-adapter` |

**IDE**

| Project | Lang | Role |
|---|---|---|
| VisualGameStudio.Core | C# | Abstractions / service interfaces / models |
| VisualGameStudio.Editor | C# | Avalonia code editor (highlighting, IntelliSense, folding) |
| VisualGameStudio.ProjectSystem | C# | Projects, build service, LSP client, debugging |
| VisualGameStudio.Shell | C# | IDE app shell — **the build/run target for the IDE** |
| VisualGameStudio.Tests | C# | NUnit suite (~2,400 tests) |
| BasicLang.VisualStudio | C# (VSIX) | VS 2022 CPS extension — details in `docs/vs-extension-notes.md` |

**GameEngine**

| Project | Lang | Role |
|---|---|---|
| VisualGameStudioEngine | C++ | 2D engine DLL on Raylib (C-ABI exports in `framework.h`) |
| RaylibWrapper | VB.NET | P/Invoke bindings to the engine DLL |
| CPPengineTest | C++ | Native engine smoke / game-loop test |
| TestVbDLL | VB.NET | Sample game exercising engine + wrapper |

Not in the `.sln`: `vscode-basiclang` (VS Code extension). Removed (do not resurrect
from old docs): the legacy `VisualGameStudio` VB.NET IDE and the `VS.BasicLang` VSIX.

## Build / test / run

```powershell
dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release   # the IDE
dotnet build BasicLang/BasicLang.csproj -c Release                             # compiler alone
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release                            # full suite (~39 min; integration tests compile/run native code, spawn clangd/DAP)
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "TestCategory!=Integration"  # fast subset (~2 min; skips the [Category("Integration")] compile/run/spawn tests)
IDE/VisualGameStudio.exe                                                        # run prebuilt IDE
IDE/BasicLang.exe MyFile.bas --target=csharp                                   # CLI compile a file (pass source directly — there is no `compile` subcommand)
IDE/BasicLang.exe build MyProject.blproj                                        # CLI build a project
IDE/BasicLang.exe --lsp                                                         # language server
IDE/BasicLang.exe --debug-adapter                                              # debug adapter (--dap-legacy = old)
```

The native C++ engine builds via VS 2022 MSBuild on `VisualGameStudioEngine.vcxproj`
(x64/Release), auto-discovered through vswhere.

**On Linux / in a cloud container** the SDK installs from the distro archive — the package index in
a fresh container is stale, so update first:

```bash
apt-get update && apt-get install -y --no-install-recommends dotnet-sdk-8.0
```

The full suite runs in ~8 minutes there, but **~170 tests fail for environmental reasons** (hardcoded
`C:\` paths, clang, MSVC). The raw number therefore means nothing on its own: build the merge-base
in a `git worktree`, run both, and compare the sorted FAILURE NAMES with `comm -23`. A count alone
hides a regression that lands as another test goes green. Windows remains the release gate.

## Working conventions — READ THIS, these prevent real mistakes

- **PowerShell is the primary shell.** Use the dedicated tools (Read/Edit/Write/
  Grep/Glob) for files — a PreToolUse hook (`.claude/hooks/prefer-native-tools.js`)
  blocks reflexive `grep`/`cat`/`find`/`sed`/… through the Bash tool.
- **Never round-trip repo files through PowerShell `Get-Content`/`Set-Content`** — it
  corrupts the BOM-less UTF-8 files here (has caused mojibake more than once). Use
  Edit/Write. For a multi-line git commit message, write a file and use `git commit -F`.
- **After AXAML changes, `dotnet clean` before building** — stale build cache causes crashes.
- **`IDE/` is a WINDOWS xcopy drop.** `IDE/BasicLang.exe` is a PE32+ binary — **never refresh it
  from a Linux build**, which would swap the Windows executables for ELF apphosts. Refresh with
  `robocopy <Shell bin> IDE /E` on Windows — **never `/MIR`**.
- **Validate codegen through the CLI *and* the IR optimizer**, not only the non-optimizing
  unit-test helper — the green suite has hidden bugs the optimizer/CLI exposed. Run the CLI,
  or use the optimizer-running test helper (`CompileToCppOptimized` in `CppCollectionTests.cs`).
- **Test both entry points.** The IDE build delegates to the CLI engine
  (`CompileProjectFiles`); a fix verified only through the test helper can still break
  via the IDE or the CLI. Exercise both.
- **Some resolver source is shared across consumers — change it once, not per-consumer.**
  `ModuleResolver.cs` backs both the compiler and the LSP; `ModuleTypeWalker.cs` is shared
  across the compiler and the C++ backend / capability checkers.

## Compiler layout (`BasicLang/`)

Pipeline: `Preprocessor.cs` → `BasicLangLexer.cs` → `Parser.cs` → `SemanticAnalyzer.cs`
→ `IRBuilder.cs` (`IRNodes.cs`) → `IROptimizer.cs` → backends.
Backends: `CSharpBackend.cs`, `LLVMBackend.cs`, `MSILBackend.cs`, `CppCodeGenerator.cs`
(+ `CppCapabilityChecker.cs`). Resolution/types: `ModuleResolver.cs`,
`ModuleTypeWalker.cs`, `TypeMapper.cs`. LSP: `BasicLang/LSP/` (server +
per-feature handlers, `CompletionService.cs`).

## Form designer (`BasicLang/Forms/`, `VisualGameStudio.Shell/Controls/`)

Two document formats, one reader and one writer. `.blform` (WinForms, absolute pixels) and
`.blwebform` (web, Grid/Flow) share the element grammar and diverge in layout vocabulary and
catalog; `FormDocumentReader`/`FormDocumentWriter` pick the vocabulary from the ROOT element, and a
file whose name disagrees with its root is refused. `FormControlCatalog` is the single source of
truth for which controls exist and what can be set on them — toolbox, property grid, emitters and
the CI gate all read it. **Add a row; never hand-write a `[TestCase]` list beside it.**

The designer writes into two marked regions inside the user's own `.bas` (`RegionWriter`), refusing
rather than overwriting anything hand-edited. Design view is a MODE on the existing code editor, not
a second document type.

- ⛔ **A WinForms catalog row is unfalsifiable without `csc`.** `EnableNetResolution` returns early
  for `UseWindowsForms` and the resolver cannot reach `System.Windows.Forms.dll`, so every
  `Form`/`Button`/`Point` member types as `Object` with no diagnostic — **a misspelled property name
  compiles green**. `WinFormsCatalogSweepTests` generates every control with every property and
  requires csc to accept it; that gate has already caught six wrong catalog rows.
- ⛔ **Never emit `With` from a generator** — the IR builder silently drops every `.Prop = value`
  inside it (see `docs/form-designer-followups.md`).
- ⛔ **Geometry fans in**: `Location = New Point(x, y)` as ONE statement. `Location.X = 96` is
  CS1612, and BasicLang reports nothing because the member degrades to `Object`.
- ⚠ The handler-ordering rule (a handler must precede the region that wires it) is **web-only** —
  measured. On WinForms the same shape compiles, which is why the shipped VSIX template does it.
- The canvas is a **schematic**, not a preview: the IDE has no browser and no WinForms surface. F5
  to the real target is the renderer.
- ⛔⛔ **A generator with no caller is the failure mode here — three separate pieces of this feature
  were complete, unit-tested and unreachable, with a green suite throughout.** `RegionWriter.Write`
  never ran, so a scaffolded form's `InitializeComponent` was never generated; `DispatchSource`
  never ran, so every page's `data-form` was read by nothing; `JavaScriptEmitter.Emit(forms:)` was
  optional and only tests passed it, so no page was ever written. When you add one, the question is
  **who calls it in a shipping build** — and the answer must be a test that drives the real entry
  point (`SaveAsync`, the CLI), not one that constructs the generator. `FormClipboard` is still
  unreachable; see `docs/form-designer-followups.md`.
- ⛔ **Ask the CATALOG what a value means, never the shape of the string.** A `Type.Member` regex
  used to decide "is this already source?" and was wrong in both directions:
  `Text="config.json"` emitted unquoted (form stops building), and
  `TextAlign="ContentAlignment.Bogus"` sailed past the Degraded check into CS0117 with no
  diagnostic. `FormPropertyDef.IsSourceForm` is the answer.
- ⛔⛔ **A GREEN BUILD IS NOT A RUNNING PAGE.** The D7 dispatch was generated from a
  `Public Module`; it compiled clean, every string a test looked for was there, and every page died
  on load with `ReferenceError: VgsForms is not defined` — the JavaScript backend **flattens a
  module's members to bare globals while emitting the call site qualified**, so the script referenced
  an object that appears nowhere in the file. No string assertion can see that. `FormBuildEmissionTests`
  now RUNS the emitted script under node. The dispatch is generated as `Public Class` + `Public Shared
  Sub`, which emits a real `class` with a `static` member. The backend bug itself is UNFIXED
  (`docs/form-designer-followups.md` 14) and will bite anyone calling a module across files.
- ⚠ A bare top-level `Sub` in one `.bas` is not callable from another at all
  (*"no lowering for 'Helper.Helper'"*). Between that and the above, **a class is the only shape that
  works across files on the JavaScript backend** — both are compiler gaps, not designer ones.

## BasicLang language

VB-like syntax; classes / interfaces / modules; generics; pattern matching (`When`
guards); LINQ; Async/Await; conditional compilation (`#If`/`#IfDef`/`#Else`/`#EndIf`);
multi-file projects (Import/Using); .NET interop via `Using`; five backends. Source
files: `.bas` (also `.mod`, `.cls`). In a `.cls` file, a
first code-line `Option Public` marks the implicit class public (legacy bare `Public`
still works but warns).

## C++ backend (`CppCodeGenerator.cs` ↔ engine)

- **Two-layer std:** collections lower to `std::shared_ptr<BasicLang::List<T>>`
  (**reference** semantics, matching .NET — value wrappers diverged and were wrong);
  `String`/structs stay values. Foreign C++ via `#CppInclude` / `::`. Targets `-std=c++20`.
- **Reference vs value:** classes/interfaces → `shared_ptr<T>` + `make_shared` + `->`;
  `Structure` → value `struct`. Generics → real C++ templates.
- Exceptions via the `IRThrow` node (known limitation: a `Return` inside a `Try` bypasses
  its `Finally` on the C++ backend); iterators are real C++20 coroutines (`Generator<T>` /
  `co_yield`); async is synchronous `Task<T>` emulation (no scheduler).
- Plans/specs in `docs/superpowers/`. Known gap: broad .NET API surface
  (List/Console/String methods) on the C++ backend —
  `docs/superpowers/specs/2026-07-07-cpp-backend-preexisting-gaps.md`.

## Engine ⇄ wrapper sync invariant

Every `__declspec(dllexport)` in `VisualGameStudioEngine/framework.h` needs a matching
`<DllImport>` in `RaylibWrapper/RaylibWrapper.vb` — `extern "C"`, `__cdecl`, `LPStr`
string marshaling. The export count is in the thousands and drifts — grep to confirm,
never trust a cached number. On a successful build the compiler auto-injects the
wrapper reference and deploys the native DLL for game apps.

## Where things live (don't duplicate it here)

- **On a machine with no auto-memory** (a cloud session, a fresh clone) → start at
  `docs/HANDOFF.md`: the in-repo snapshot of state, gates and traps. The auto-memory
  below lives outside the repo and does not travel.
- **Current work / status** → auto-memory `MEMORY.md` (loaded each session)
- **History / rationale** → `git log` and `docs/superpowers/{plans,specs}/`
- **Authoritative engine API** → `framework.h` + `RaylibWrapper.vb` (+ `docs/`)
- **Per-area subagent guides** → `BasicLangAgent/`, `IDEAgent/`, `EngineAgent/`,
  `VSExtensionAgent/` each have a focused CLAUDE.md
