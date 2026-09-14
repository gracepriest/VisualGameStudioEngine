title: Glossary
lede: Terms, file names and acronyms that appear throughout this repository.
---
## Products and binaries

| Term | Meaning |
|---|---|
| **BasicLang** | The language, and `BasicLang.exe` — compiler, language server and debug adapter in one binary |
| **Visual Game Studio** | The Avalonia IDE; `VisualGameStudio.Shell` is its project, `VisualGameStudio.exe` its binary |
| **The engine** | `VisualGameStudioEngine.dll`, the Raylib-backed 2D engine with a C ABI |
| **RaylibWrapper** | `RaylibWrapper.vb`, the VB.NET P/Invoke bindings to the engine |
| **`IDE/`** | A hand-committed xcopy drop of built binaries — convenient and prone to going stale |

## File formats

| Extension | What it is |
|---|---|
| `.bas` | BasicLang source (also `.mod`, `.cls`) |
| `.bli` | BasicLang interface/declaration file — e.g. `dom-core.bli` |
| `.blproj` | A BasicLang project (XML) |
| `.blsln` | A BasicLang solution |
| `.vcxproj` | The engine's MSBuild project |
| `.vsix` / `.vstemplate` / `.vstman` | VS extension package, template, template manifest |
| `.xshd` | AvaloniaEdit syntax-highlighting definition |

## Compiler terms

| Term | Meaning |
|---|---|
| **Pipeline** | preprocess → lex → parse → semantic → IR → optimize → backend |
| **IR** | The intermediate representation every backend consumes; built by `IRBuilder`, nodes in `IRNodes` |
| **CFG** | Control-flow graph, per routine; backends reconstruct source-level control flow from it |
| **Backend** | A code generator for one target: C#, C++, JavaScript, LLVM, MSIL |
| **Capability checker** | A per-backend gate that rejects constructs it cannot lower — `BL6xxx` for C++, `BL7xxx` for JS |
| **StdLib shim** | Per-backend mapping of BasicLang's built-in surface onto the target's |
| **`BLxxxx`** | A structured diagnostic code; the first digit is the phase — see [Diagnostics](#/diagnostics) |
| **Extern Class** | A type declaration that emits nothing because the runtime already provides it (the JavaScript DOM) |
| **Passthrough** | Unchecked access to the target language — `#CppInclude` + `::` types, or `javascript{ }` |

## Tooling protocols

| Term | Meaning |
|---|---|
| **LSP** | Language Server Protocol — completion, hover, diagnostics, navigation |
| **DAP** | Debug Adapter Protocol — breakpoints, stepping, inspection |
| **clangd** | The C++ language server; downloaded on demand |
| **lldb-dap** | The native debug adapter; downloaded on demand |
| **`compile_commands.json`** | The compilation database clangd needs to know each file's flags |
| **CPS** | Common Project System — the Visual Studio 2022 project-system framework the VSIX uses |
| **pkgdef** | The VS registry-fragment file that registers a package's menus, factories and templates |
| **Open VSX** | The extension marketplace the IDE's extension host queries |

## Engine terms

| Term | Meaning |
|---|---|
| **Framework layer** | The immediate-mode API: initialise, poll, draw, shut down |
| **Engine layer** | The retained API on top: ECS, scenes, and the gameplay subsystems |
| **ECS** | Entity-component-system — integer entity handles plus eight built-in components |
| **Handle** | An `int` identifying an engine-owned object; the only thing that crosses the ABI for most types |
| **C ABI** | `extern "C"` + `__cdecl` + plain C types — the contract that lets four languages call the engine |
| **Introspection** | Runtime component field read/write by entity + component type + field index |
| **ABI-safe struct** | A POD (`Transform2DData` etc.) that may cross the boundary by value |

## Repository conventions

| Term | Meaning |
|---|---|
| **`CLAUDE.md`** | The operating guide — conventions and invariants; explicitly *not* a changelog |
| **`docs/HANDOFF.md`** | A dated state snapshot for a fresh checkout: gates, traps, open work |
| **`docs/superpowers/plans/`** | Implementation plans |
| **`docs/superpowers/specs/`** | Design specs and rationale |
| **Baseline failure** | A known-failing test that is not caused by your change |
| **Fast subset** | `--filter "TestCategory!=Integration"` — ~2 minutes, and not a gate for codegen work |
| **P2a / P2a-2** | The workstream giving native projects access to .NET types |

## Agent applications

| Term | Meaning |
|---|---|
| **BasicLangAgent** | Claude Agent SDK app for compiler testing and review |
| **IDEAgent** | Agent app for IDE feature development and testing |
| **EngineAgent** | Agent app for C++ / VB.NET engine sync maintenance |
| **VSExtensionAgent** | Agent app for VSIX build and validation |
