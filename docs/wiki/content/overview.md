title: Overview
lede: BasicLang, a cross-platform IDE, and a 2D game engine — one repository, three products that only make sense together.
---
Visual Game Studio Engine is a game-development stack built from the compiler up. Three
products live in one repository, and each exists because the other two need it:

<div class="stat-row">
<div class="stat"><div class="stat-v">~473K</div><div class="stat-l">Lines of code</div></div>
<div class="stat"><div class="stat-v">2,913</div><div class="stat-l">Engine exports</div></div>
<div class="stat"><div class="stat-v">~6,500</div><div class="stat-l">Tests</div></div>
<div class="stat"><div class="stat-v">5</div><div class="stat-l">Compiler backends</div></div>
</div>

<div class="card-grid">
<a class="card" data-pillar="compiler" href="#/language"><span class="card-k">Language</span><span class="card-t">BasicLang</span><span class="card-d">A VB-like language with classes, generics, pattern matching, LINQ and async — compiled to C#, native C++, .NET IL, or JavaScript.</span></a>
<a class="card" data-pillar="ide" href="#/ide"><span class="card-k">Tooling</span><span class="card-t">Visual Game Studio</span><span class="card-d">A cross-platform Avalonia IDE that speaks LSP and DAP, so BasicLang and C++ get equal treatment.</span></a>
<a class="card" data-pillar="engine" href="#/engine"><span class="card-k">Runtime</span><span class="card-t">The engine DLL</span><span class="card-d">A Raylib-backed 2D engine exposed over a stable C ABI, reachable from BasicLang, C++, C# and VB.NET alike.</span></a>
</div>

## What you actually get

**A language, not a DSL.** BasicLang has the surface of Visual Basic — `Dim`, `Sub`,
`End If` — and the semantics of a modern language: generics that become real C++
templates, `Select Case` pattern matching with `When` guards, LINQ query expressions,
`Async`/`Await`, interfaces, modules, and conditional compilation. It compiles to five
targets; three of them — C#, C++ and MSIL — are maintained and gated by tests.

**An IDE that treats C++ as a peer.** The Avalonia shell is not a text editor with a
build button. It runs `BasicLang.exe --lsp` for BasicLang and `clangd` for C++, both
downloaded on demand, and debugs through DAP — a managed adapter for the C# backend and
`lldb-dap` for native builds, with breakpoints set in `.bas` files mapping into native
code through emitted `#line` directives.

**An engine with a real surface.** `VisualGameStudioEngine.dll` exports 2,913 C
functions covering far more than drawing: ECS, 2D physics with joints, behaviour trees,
finite state machines, A* navigation and steering, dialogue trees, quests, inventory and
equipment, skeletal animation, tweening, an event bus, timers, object pools, 2D lighting
with shadows, localization, achievements, leaderboards, a command console, save/load,
and a profiler.

## How the three fit together

The two engine-facing backends, C# and C++, converge on the same native engine, so a game written once can
ship managed or native without touching the source:

```text
BasicLang source (.bas / .cls / .mod)  +  hand-written C++
        |
        +-- C# backend  --> .NET assembly --> RaylibWrapper.vb (P/Invoke) --+
        |                                                                  |
        +-- C++ backend --> clang/gcc/msvc --> native .exe (links .lib) ---+
                                                                           |
                                                                           v
                                                    VisualGameStudioEngine.dll
                                                       (Raylib, stable C ABI)
```

That convergence is the reason the repo is a monorepo. The engine's C ABI is the contract;
[the wrapper invariant](#/engine-binding) is what keeps both sides of it honest.

## Maturity, honestly

| Area | State |
|---|---|
| C# backend | <span class="pill ok">Maintained</span> Primary managed target, full .NET interop |
| C++ backend | <span class="pill ok">Maintained</span> Native executables, `-std=c++20`, links the engine |
| JavaScript backend | <span class="pill warn">Active</span> Emits an ES-module web site; typed DOM is hand-curated |
| MSIL backend | <span class="pill ok">Maintained</span> ILAsm round-trip suite (`Integration`); properties, Try/Catch, Select Case, Shared members, collections |
| LLVM backend | <span class="pill mute">Unmaintained</span> Builds, but out of scope — don't file issues |
| IDE shell | <span class="pill ok">Maintained</span> The build and run target |
| VS 2022 extension | <span class="pill warn">v2.4.0</span> CPS + LSP; no debug launch provider |
| VS Code extension | <span class="pill warn">Partial</span> Grammar and LSP client; not in the `.sln` |
| Engine DLL | <span class="pill ok">Maintained</span> Builds through VS 2022 MSBuild only |

## Where to go next

- Never seen the repo before → [Architecture](#/architecture), then [Repository map](#/repo-map).
- Want to write a game → [Getting started](#/getting-started) and [Samples](#/samples).
- Working on the compiler → [Compiler pipeline](#/pipeline) and [Backends](#/backends).
- Working on the IDE → [IDE overview](#/ide) and [Services](#/ide-services).
- Working on the engine → [Engine overview](#/engine) and [Engine ⇄ wrapper](#/engine-binding).
- About to change anything → [Conventions and traps](#/conventions). It is short and it is load-bearing.
