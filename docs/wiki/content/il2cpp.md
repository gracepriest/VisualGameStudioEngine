title: IL2CPP backend
lede: Native executables from .NET IL — no .NET runtime dependency.
---
## What is IL2CPP?

IL2CPP is a backend that compiles .NET IL assemblies (including VGS-generated MSIL) to self-contained native C++, following Unity's IL2CPP model. No .NET runtime dependency, no JIT.

## Pipeline

```
.bas → MSIL → ilasm → PE → IL reader → C++ emitter → MSVC → native binary
```

## Usage

```bash
basiclang program.bas --target=il2cpp
basiclang project.blproj  # (with Backend=IL2CPP)
```

## Features

- Classes, inheritance, virtual dispatch (vtables)
- Arrays, strings, exceptions (try/catch/throw)
- Loops, multi-arg method calls
- Project-level builds
- BCL methods via BclShim.h (~75 methods)

## Architecture

- **IL Reader**: Uses System.Reflection.Metadata to parse any .NET assembly
- **Type System**: ILTypeMapper maps IL types to C++ types
- **Emitter**: ILCppEmitter lowers IL opcodes to C++ (55+ opcodes)
- **Runtime**: shared_ptr GC, CppArrayRuntime, BasicLangRt strings

## Design Documents

- [Design spec](docs/superpowers/specs/2026-11-01-il2cpp-design.md)
- [Phase 0 spike plan](docs/superpowers/plans/2026-11-01-il2cpp-phase0-spike.md)
- [Spike results](docs/superpowers/spike-results/2026-11-01-il2cpp-phase0.md)

## Related

- [Backends](#/backends) — all available backends
- [NewCOM](#/newcom) — cross-platform component model
- [C++ interop](#/cpp-interop) — native C++ integration
