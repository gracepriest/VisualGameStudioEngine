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
| BasicLang | C# | Compiler: preprocess → lex → parse → semantic → IR → optimize → backends (C#, LLVM, MSIL, C++); also runs as `--lsp` server and `--debug-adapter` |

**IDE**

| Project | Lang | Role |
|---|---|---|
| VisualGameStudio.Core | C# | Abstractions / service interfaces / models |
| VisualGameStudio.Editor | C# | Avalonia code editor (highlighting, IntelliSense, folding) |
| VisualGameStudio.ProjectSystem | C# | Projects, build service, LSP client, debugging |
| VisualGameStudio.Shell | C# | IDE app shell — **the build/run target for the IDE** |
| VisualGameStudio.Tests | C# | xUnit suite (~2,400 tests) |
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
dotnet test  VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release   # full suite
IDE/VisualGameStudio.exe                                                        # run prebuilt IDE
IDE/BasicLang.exe MyFile.bas --target=csharp                                   # CLI compile a file (pass source directly — there is no `compile` subcommand)
IDE/BasicLang.exe build MyProject.blproj                                        # CLI build a project
IDE/BasicLang.exe --lsp                                                         # language server
IDE/BasicLang.exe --debug-adapter                                              # debug adapter (--dap-legacy = old)
```

The native C++ engine builds via VS 2022 MSBuild on `VisualGameStudioEngine.vcxproj`
(x64/Release), auto-discovered through vswhere.

## Working conventions — READ THIS, these prevent real mistakes

- **PowerShell is the primary shell.** Use the dedicated tools (Read/Edit/Write/
  Grep/Glob) for files — a PreToolUse hook (`.claude/hooks/prefer-native-tools.js`)
  blocks reflexive `grep`/`cat`/`find`/`sed`/… through the Bash tool.
- **Never round-trip repo files through PowerShell `Get-Content`/`Set-Content`** — it
  corrupts the BOM-less UTF-8 files here (has caused mojibake more than once). Use
  Edit/Write. For a multi-line git commit message, write a file and use `git commit -F`.
- **After AXAML changes, `dotnet clean` before building** — stale build cache causes crashes.
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

## BasicLang language

VB-like syntax; classes / interfaces / modules; generics; pattern matching (`When`
guards); LINQ; Async/Await; conditional compilation (`#If`/`#IfDef`/`#Else`/`#EndIf`);
multi-file projects (Import/Using); .NET interop via `Using`; four backends. Source
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

- **Current work / status** → auto-memory `MEMORY.md` (loaded each session)
- **History / rationale** → `git log` and `docs/superpowers/{plans,specs}/`
- **Authoritative engine API** → `framework.h` + `RaylibWrapper.vb` (+ `docs/`)
- **Per-area subagent guides** → `BasicLangAgent/`, `IDEAgent/`, `EngineAgent/`,
  `VSExtensionAgent/` each have a focused CLAUDE.md
