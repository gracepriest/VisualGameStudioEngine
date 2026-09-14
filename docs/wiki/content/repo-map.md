title: Repository map
lede: Every top-level directory, what it is, and how big it actually is.
---
## In the solution

`VisualGameStudioEngine.sln` groups projects under three solution folders — Compiler,
GameEngine, and Games — plus the IDE projects at the root.

### Compiler

| Project | Lang | Lines | Role |
|---|---|---|---|
| `BasicLang/` | C# | ~124K | The compiler. Also the LSP server (`--lsp`) and debug adapter (`--debug-adapter`) |

### IDE

| Project | Lang | Lines | Role |
|---|---|---|---|
| `VisualGameStudio.Core/` | C# | ~17K | Abstractions, service interfaces, models, DAP protocol types |
| `VisualGameStudio.Editor/` | C# | ~19K | Avalonia code editor control — highlighting, folding, completion, multi-cursor |
| `VisualGameStudio.ProjectSystem/` | C# | ~46K | Service implementations: projects, build, LSP client, debugging, git, extensions |
| `VisualGameStudio.Shell/` | C# | ~71K | The IDE application — **the build and run target** |
| `VisualGameStudio.Tests/` | C# | ~119K | NUnit suite, 435 test files |
| `BasicLang.VisualStudio/` | C# (VSIX) | ~2K | VS 2022 CPS extension |

### GameEngine

| Project | Lang | Lines | Role |
|---|---|---|---|
| `VisualGameStudioEngine/` | C++ | ~35K | The 2D engine DLL on Raylib; C-ABI exports in `framework.h` |
| `RaylibWrapper/` | VB.NET | ~14K | P/Invoke bindings to the engine DLL |
| `CPPengineTest/` | C++ | — | Native engine smoke / game-loop test |
| `TestVbDLL/` | VB.NET | — | Sample game exercising engine + wrapper end to end |

## Not in the solution

| Path | What it is |
|---|---|
| `vscode-basiclang/` | VS Code extension — grammar, snippets, LSP client, packaged `.vsix` |
| `BasicLangAgent/` `IDEAgent/` `EngineAgent/` `VSExtensionAgent/` | Claude Agent SDK apps (Python) for automated maintenance, each with its own `CLAUDE.md` |
| `IDE/` | Prebuilt xcopy drop of the IDE and CLI binaries |
| `SampleGames/` | Buildable `.blproj` samples — Pong, Space Shooter |
| `Samples/` | Single-file BasicLang sources — Pong, Space Shooter, Platformer |
| `TestGame/` `TestMultiFile/` `TestWinForms/` | Scratch projects used by manual and integration testing |
| `docs/` | Documentation, design specs, implementation plans, comparison research |

> [note] Two things were removed and should not be resurrected from old documentation:
> the legacy `VisualGameStudio` VB.NET IDE, and the `VS.BasicLang` MEF-based VSIX.
> The current VS extension is `BasicLang.VisualStudio` (CPS-based).

## Inside the compiler

```text
BasicLang/
├─ *.cs                 pipeline stages and backends at the root
├─ Compiler/CodeGen/    C++ and .NET code-generation support
│   ├─ CPlusPlus/
│   └─ Net/             NetShimGenerator, NetProxyEmitter
├─ Debugger/            managed debug adapter, CorDebug wrappers, source mapping
├─ LSP/                 language server and one handler per feature
├─ Net/                 .NET surface discovery, type resolution, marshalling
├─ ProjectSystem/       project build, C++ toolchain, compile_commands, templates
├─ Runtime/             node locator, safe zip
├─ StdLib/              per-backend standard-library shims
└─ lib/js/dom-core.bli  hand-curated typed DOM for the JavaScript backend
```

The ten largest compiler files, which is a fair proxy for where the complexity lives:

| File | Lines |
|---|---|
| `SemanticAnalyzer.cs` | 8,571 |
| `Parser.cs` | 5,216 |
| `CppCodeGenerator.cs` | 5,186 |
| `Debugger/NetDebugAdapter.cs` | 4,962 |
| `IRBuilder.cs` | 4,509 |
| `CSharpBackend.cs` | 4,208 |
| `Program.cs` | 4,099 |
| `LSP/CompletionService.cs` | 3,529 |
| `JavaScriptBackend.cs` | 3,098 |
| `IROptimizer.cs` | 3,028 |

## Inside the IDE

```text
VisualGameStudio.Core/
├─ Abstractions/Services/   44 service interfaces — the IDE's contract surface
├─ Abstractions/ViewModels/
├─ DAP/                     Debug Adapter Protocol types
├─ Models/                  project, solution, diagnostic, build result
├─ Snippets/  TextMate/  Constants/  Events/  Extensions/  Utilities/

VisualGameStudio.Editor/
├─ Completion/  Folding/  Formatting/  Highlighting/
├─ Margins/  MultiCursor/  Rendering/  TextMarkers/  Controls/

VisualGameStudio.ProjectSystem/
├─ Services/                ~60 service implementations
│   └─ ExtensionHost/       Node-based VS Code extension host
├─ Serialization/  Snippets/  TextMate/

VisualGameStudio.Shell/
├─ ViewModels/Panels/       23 tool-window view models
├─ ViewModels/Dialogs/      50+ dialogs, mostly refactorings
├─ ViewModels/Documents/    code editor, web view, welcome
├─ Views/                   matching AXAML
├─ Dock/  Services/  Converters/  Resources/Styles/
```

## Shared code that must change once, not per consumer

Two files back more than one consumer. Editing a copy of the behaviour instead of the
shared file is a recurring source of divergence:

- **`ModuleResolver.cs`** backs both the compiler and the LSP.
- **`ModuleTypeWalker.cs`** is shared across the compiler, the C++ backend, and the
  capability checkers.

## Where documentation lives

| Path | Contents |
|---|---|
| `README.md` | Product-level overview and the engine API tables |
| `CLAUDE.md` | Operating guide — conventions, build commands, invariants |
| `docs/HANDOFF.md` | Dated state snapshot for a fresh checkout: gates, traps, open work |
| `docs/superpowers/plans/` | Implementation plans, dated |
| `docs/superpowers/specs/` | Design specs and rationale, dated |
| `docs/articles/` | Guides: engine, language, IDE, debugging, coverage |
| `docs/comparisons/` `docs/research/` | VS Code parity scorecards and research notes |
| `docs/wiki/` | This site |
