# C++ as a Peer Language in Visual Game Studio — Design

**Date:** 2026-07-11
**Status:** Approved design, pre-planning
**Approach:** Protocol-first (clangd + lldb-dap/gdb rented via existing LSP/DAP infrastructure; builds driven by the existing `CppToolchain`)

## Goal

Make C++ a first-class peer language in Visual Game Studio, the way Visual Studio
treats VB.NET / C# / C++: a user can create a pure C++ project, a pure BasicLang
project, or freely mix `.cpp`/`.h` and `.bas`/`.mod`/`.cls` files in one project.
Delivered in phases; every phase ships a usable increment. Total toolchain cost: $0.

## Non-goals

- No new compiler. C++ compilation uses external toolchains (clang++ / g++ / MSVC)
  already discovered and invoked by `BasicLang/ProjectSystem/CppToolchain.cs`.
- No C++ IntelliSense engine. Editor smarts come from clangd over LSP.
- Mixed-language projects on the C# / MSIL / LLVM backends are **out of scope**.
  Mixing requires the native (C++ backend) target. P/Invoke auto-bridging for the
  C# backend is a possible future phase, not part of this design.
- No CMake/vcxproj support. `.blproj` is the only project format. CMake *import*
  is a backlog idea only.
- Go-to-definition from C++ into original `.bas` source (rather than the generated
  header) is deferred.

## Decisions and rationale

| Decision | Choice | Why |
|---|---|---|
| Overall approach | Protocol-first: clangd (LSP) + lldb-dap/gdb (DAP) + `CppToolchain` builds | The IDE already speaks LSP and DAP to the BasicLang compiler; adding a language is integration work, not compiler work. Rejected: embedding libclang (reimplements clangd, crash risk in-process); MSBuild/vcxproj delegation (ties a cross-platform IDE to Windows-only, not-fully-free tooling). |
| Mixed-project interop | Native target only | Native objects link directly; no marshaling layer to design. Matches the game-engine use case. |
| Project format | Extend `.blproj` with a `Language` axis | One project system owns both languages, so mixed projects are ordinary projects. Zero new dependencies. |
| Blessed toolchain | clang/LLVM vertical (clang++, clangd, lldb-dap) | One vendor, one download, Apache 2.0 (+ LLVM exception), bundleable/redistributable. g++/gdb are the free fallback; MSVC is detect-only, compile-only. |

## 1. Project model

`.blproj` gains one new property; everything else reuses the existing
PropertyGroup/ItemGroup shape parsed by `ProjectFile.cs`:

```xml
<PropertyGroup>
  <Language>Cpp</Language>            <!-- default: BasicLang -->
  <OutputType>Exe</OutputType>        <!-- Exe | Library (static .lib/.a) -->
  <CppStandard>c++20</CppStandard>    <!-- default matches the BasicLang C++ backend -->
</PropertyGroup>
<ItemGroup>
  <Compile Include="main.cpp" />
  <IncludeDir Include="vendor/include" />
  <NativeLib Include="VisualGameStudioEngine.lib" />
  <Define Include="MY_FLAG" />
</ItemGroup>
```

- Empty source list globs `**/*.cpp` plus headers, mirroring the existing
  `**/*.bas` default.
- **A mixed project is not a distinct project kind.** Any native-target project
  may contain both `.bas`-family and `.cpp`-family files. `Language` only selects
  templates, defaults, and which language the starter `Main` is written in.
- New Project dialog gains a language picker (BasicLang | C++).
- New templates: *C++ Console App*, *C++ Library*, and *C++ Game* — the game
  template links `VisualGameStudioEngine.lib` and includes `framework.h` directly
  (no RaylibWrapper, no P/Invoke; the engine is native C++ already).

## 2. Build pipeline

One engine, both entry points — the IDE's BuildService keeps delegating to the
CLI engine (`CompileProjectFiles`), which routes internally. This preserves the
repo's no-drift invariant between `BasicLang.exe build` and the IDE.

Routing key is **native-ness**, not `Language`: a project is native when
`Backend=Cpp` (the CLI already accepts this backend) — `Language=Cpp` templates
simply default `Backend` to `Cpp`. A BasicLang project with `Backend=Cpp` and a
C++ project share the same pipeline; the former simply starts with a transpile
step.

Native path:

1. **Transpile** the `.bas`/`.mod`/`.cls` sources → generated C++ in `obj/gen/`
   (existing `CppCodeGenerator`). In Phase 1 this stays what the backend does
   today: one combined translation unit. Phase 2 adds **per-module split
   emission** (a `.h`/`.cpp` pair per module, with include guards and
   cross-module includes) — this is new backend work, not a repackaging of
   existing output, and it is the enabler for Direction B interop in §3.
2. **Gather** translation units: generated C++ + user `.cpp` files.
3. **Emit `compile_commands.json`** (compilation database for clangd) into the
   obj directory — trivial because this pipeline constructs the exact compiler
   command line per TU anyway.
4. **Compile + link** via `CppToolchain` (exists). Include path automatically
   contains `obj/gen/`, project header dirs, and `<IncludeDir>` items;
   `<NativeLib>` items are passed to the linker.
5. **Parse diagnostics** into the existing `Diagnostic` model so C++ errors land
   in the Error List and click-navigate like BasicLang errors. clang and gcc
   share the `file:line:col: severity: message` format (one parser); MSVC has its
   own (`file(line): error C1234:`). Parsers live alongside `CppToolchain`, one
   per toolchain family.

Phase 1 rebuilds all TUs every build. Per-file `.o` caching with timestamp checks
is a later optimization and changes no interfaces.

## 3. Mixed-project interop

Unifying idea: **the generated-C++ folder is the interop surface.** Transpile
runs first, so by the time any C++ compiles, the BasicLang half *is* C++.
Interop is `#include`, not FFI.

**Direction A — BasicLang calls C++ (exists).** `#CppInclude "player.h"` +
`::` passthrough, already shipped. New work is only include-path wiring so the
project's own headers resolve with zero configuration.

**Direction B — C++ calls BasicLang (new).** Requires the per-module split
emission added in Phase 2 (§2 step 1): today `CppCodeGenerator` emits one
combined translation unit, so producing a stable, consumable `.h` per module in
`obj/gen/` is new backend work — declaration/definition separation, include
guards, and cross-module includes. Once emitted, C++ consumes it as an ordinary
include:

```cpp
#include "Logic.h"                                  // generated from logic.bas
auto score  = Logic::CalculateScore(hits, combo);   // BasicLang Function
auto player = std::make_shared<Logic::Player>();    // BasicLang Class → shared_ptr
```

Boundary types are exactly the backend's existing two-layer model — `String` and
`Structure` by value, classes/interfaces as `shared_ptr<T>`, collections as
`shared_ptr<BasicLang::List<T>>`, generics as real templates. No new marshaling
layer exists to get wrong. User C++ that touches BasicLang types includes the
same runtime headers the backend already emits against.

Rules:

- **Entry point:** for `OutputType=Exe`, exactly one `Main`/`main` across both
  languages; the compiler counts and errors on 0 or 2+. `OutputType=Library`
  requires zero entry points.
- **Freshness at build time:** guaranteed by build order (transpile → compile).
  Editor-time freshness is handled in §4.
- **Symmetry:** "C++ project + `.bas` files" and "BasicLang native project +
  `.cpp` files" hit the identical pipeline; there is no second implementation.

## 4. Editor integration (clangd)

**Language server registry.** The single hard-wired LSP connection becomes a
small registry: file extension → server.

| Extensions | Server |
|---|---|
| `.bas` `.mod` `.cls` | `BasicLang.exe --lsp` (existing) |
| `.cpp` `.h` `.hpp` `.cc` | `clangd --compile-commands-dir=<obj>` (new) |

Client machinery (framing, restart policy, completion/hover/diagnostic UI) is
shared; each server gets its own process and lifecycle. In a mixed project both
servers run; documents route by extension. Features that light up via clangd
with no per-feature work: completion, hover, go-to-definition, find-references,
rename, as-you-type diagnostics, signature help, formatting (clang-format is
built in).

**clangd acquisition:** probe PATH → probe LLVM install dirs → offer a one-click
"Download C++ tools" that fetches the standalone clangd release (~30 MB,
Apache 2.0, redistributable) into `~/.vgs/tools/`. IntelliSense works without a
compiler installed.

**Feeding clangd:** `compile_commands.json` **and** the generated `obj/gen`
headers are emitted on project **open** and on project-file **change**, not only
on build — so IntelliSense precedes the first compile and a fresh checkout of a
mixed project resolves its generated includes immediately. Generated headers are
additionally refreshed (debounced) on `.bas` save so C++-side completion of
BasicLang symbols stays current — this
also gives C++ files completion/hover/go-to-def on BasicLang modules for free,
because clangd just reads the generated headers.

**Syntax highlighting:** register AvaloniaEdit's C++ highlighting definition for
the new extensions.

**Degradation:** clangd absent → editing still works (highlighting only), status
bar hints at the download action.

## 5. Debugging

Same registry pattern on the DAP side:

| Project kind | Adapter |
|---|---|
| BasicLang on C#/MSIL backend | `BasicLang.exe --debug-adapter` (existing) |
| Native (C++ or mixed) | `lldb-dap` (ships in LLVM) or `gdb -i dap` (gdb ≥ 14) |

BasicLang on the LLVM backend keeps its existing behavior unchanged; the
registry does not affect it.

- **Pairing rule:** toolchain picks the debugger — clang++ → lldb-dap,
  MinGW g++ → gdb DAP. MSVC-built binaries are compile-only (Microsoft's debug
  adapter is not freely licensed); clang is the blessed toolchain on Windows.
- Breakpoints, stepping, stack, locals arrive through DAP messages the debug UI
  already renders.
- **Stretch (Phase 4+):** emit `#line N "logic.bas"` directives in generated C++
  so DWARF/PDB line tables reference `.bas` sources — lldb then sets breakpoints
  in and steps through BasicLang source on the native backend, including
  mixed-language stacks, with no debugger work on our side.

## 6. Phasing

Each phase gets its own implementation plan; do not plan the whole spec as one
monolith.

| Phase | Ships | Contents |
|---|---|---|
| 1 | C++ projects build & run | `Language` in `ProjectFile`; native routing in `CompileProjectFiles`; TU gathering; diagnostics parsers → Error List; `compile_commands.json`; templates (Console, Library, Game); New Project language picker; C++ highlighting; run-without-debug |
| 2 | Mixed projects | **Per-module split emission in `CppCodeGenerator`** (new backend work: `.h`/`.cpp` per module, include guards, cross-module includes) landing in `obj/gen`; include-path wiring; entry-point rule (Exe only); both-direction interop e2e tests |
| 3 | IntelliSense | LSP registry + routing; clangd discovery + download; `compile_commands.json` on open/change; debounced gen-header refresh |
| 4 | Debugging | DAP registry; lldb-dap / gdb DAP; native launch configs; stretch: `#line` → `.bas`-source debugging |
| 5 | Backlog | Incremental `.o` caching; gen→`.bas` go-to-def mapping; CMake import; clang-tidy |

## 7. Licensing ("basically free")

| Component | License | Cost | Distribution |
|---|---|---|---|
| clang++ / clangd / lldb-dap | Apache 2.0 + LLVM exception | $0 | May bundle or auto-download |
| MinGW-w64 g++ / gdb | GPL (separate processes; IDE and user output unencumbered) | $0 | Download-on-demand (MSYS2 / winlibs) |
| MSVC Build Tools | Free install; VS license terms | $0 | Detect only — never bundle |

Everything on the critical path is LLVM-licensed: one vendor, one download,
bundleable.

## 8. Testing

- Every new template builds e2e through **both** entry points — CLI
  (`CliBuildTests` pattern) and IDE BuildService (`TemplateBuildSweepTests`
  pattern) — per the repo rule that a fix verified through one entry point can
  still break the other.
- Diagnostics-parser unit tests per toolchain family (clang/gcc format, MSVC
  format) using captured real compiler output.
- A mixed-project fixture exercising both call directions (C++→BasicLang via
  generated header; BasicLang→C++ via `#CppInclude`).
- `compile_commands.json` content tests (paths, flags, generated TUs included).
- Toolchain-dependent tests skip gracefully when no compiler is installed
  (existing E2E pattern).

## 9. Error handling

- No toolchain found → actionable message naming what was probed and offering
  the download action (never a cryptic failure).
- clangd missing → editor degrades to highlighting; status-bar hint.
- `.cpp` files present in a project targeting C#/MSIL/LLVM → clear compile
  error stating mixing requires the native target.
- Zero or multiple entry points across languages → specific error listing the
  candidates found.
