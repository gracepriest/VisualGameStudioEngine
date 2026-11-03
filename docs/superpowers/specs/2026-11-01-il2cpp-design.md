# IL2CPP-style IL→C++ compiler for VGS — Design

**Date:** 2026-11-01
**Status:** Design — approved direction, not yet started
**Builds on:** `MSILBackend.cs`, `CppCodeGenerator.cs`, `TypeMapper.cs`, `blnet` facade (P2a-2)

## 1. Goal

Compile .NET IL assemblies (including, but not limited to, VGS-generated MSIL) to
self-contained native C++, following Unity's IL2CPP model: no .NET runtime dependency,
no blnet shim, no JIT. The generated C++ **is** the program, linked against a small
support runtime.

## 2. Decisions (user-stated, do not relitigate)

1. **Scope of IL input:** All .NET assemblies. VGS-generated MSIL is the primary input,
   but a non-VGS assembly (any .NET DLL) must also be compilable.
2. **BCL strategy:** Option B — full IL2CPP. Compile the entire .NET BCL from IL, not a
   narrow shim. (Unity's key insight: reuse the same BCL code, don't rewrite it.)
3. **Generics:** IL2CPP-style monomorphization. Each concrete instantiation becomes a
   separate C++ type/function. (No C++ templates for generics — monomorphization matches
   IL2CPP semantics and keeps reflection possible.)
4. **Entry point:** New backend `ILCppCodeGenerator`, not an extension of `CppCodeGenerator`.

## 3. Architecture

```
┌─────────────────────────────────────────────┐
│  IL Reader (System.Reflection.Metadata)       │  parses any .NET assembly
├─────────────────────────────────────────────┤
│  IL → VGS IR (IL-IR)                        │  stack-based IL → structured IR
├─────────────────────────────────────────────┤
│  ILCppCodeGenerator                           │  emits C++ (new backend)
├─────────────────────────────────────────────┤
│  Support runtime (VGS libil2cpp)              │  GC (shared_ptr), exceptions, arrays,
│                                             │  strings, threading, reflection stubs
└─────────────────────────────────────────────┘
```

### 3.1 IL Reader
- Uses `System.Reflection.Metadata` (built into .NET SDK, no external deps).
- Reads assemblies, type definitions, method signatures, fields, properties, generics,
  custom attributes, resources.
- Resolves dependencies (only root assemblies specified; transitive deps resolved).
- Handles .NET BCL assemblies (mscorlib/System.Runtime, System, etc.).

### 3.2 IL → VGS IR
- IL is stack-based; VGS IR is structured (IRNodes, ControlFlowGraph).
- Linear scan over IL opcodes → structured control flow (branches, try/catch/finally, loops).
- Reuse `ControlFlowGraph.cs` and `IROptimizer.cs` patterns.
- Generic sharing: monomorphize each concrete instantiation.

### 3.3 C++ Generator (ILCppCodeGenerator)
- New backend, separate from `CppCodeGenerator` (which targets BasicLang IR).
- Emits C++ per method/type from IL-IR.
- Reuses VGS C++ runtime: `shared_ptr` for GC, `CppArrayRuntime` for arrays, `BasicLangRt` for strings.
- Emits `global-metadata.dat` equivalent (runtime metadata for reflection).

### 3.4 Support runtime (VGS libil2cpp)
- GC: `shared_ptr` (already used for classes) — no separate GC needed initially.
- Exceptions: C++ exceptions mapped to .NET exceptions (VGS already has `IRThrow`).
- Threading: std::thread / platform threads.
- Reflection: minimal — type/method/field metadata from `global-metadata.dat`.
- Arrays: `BasicLang::Array<T>` (handle to shared vector).
- Strings: `std::string` (VGS already maps String → std::string).

## 4. What IL2CPP does (and VGS will mirror)

From Unity's IL2CPP:
- AOT compiler translates IL → C++. Runtime library provides GC, threading, files, internal calls.
- Does NOT rewrite the BCL — uses the same mscorlib/System.dll code. (VGS adaptation: reuse
  the existing C++ backend runtime and stdlib rather than compiling the .NET BCL from IL initially.)
- Generic sharing reduces code size.
- Exceptions configurable (None / IL Only / Full).
- Metadata in `global-metadata.dat`.
- Test-first, TDD, clear inputs/outputs.

## 5. What IL2CPP does NOT do (VGS will mirror)

- No JIT, no `Reflection.Emit`, no `dynamic`.
- No runtime code generation.
- No separate .NET runtime dependency.
- Reflection is limited.

## 6. Phases

### Phase 0 — Spike (1 week)
- Use `System.Reflection.Metadata` to read VGS's own `--target=msil` output.
- Emit a C++ "hello world" (`Console.WriteLine` via `std::cout`, no .NET).
- No blnet, no .NET runtime dependency. Pure C++ output.
- **Deliverable:** proof of concept that IL→C++ is feasible.

### Phase 1 — IL reader + type system (2 weeks)
- Parse assemblies, types, methods, fields, generics, attributes.
- Map IL types → C++ types (reuse `TypeMapper`/`_typeMap`).
- Monomorphize generics.
- **Deliverable:** metadata model consumable by the generator.

### Phase 2 — Method body lowering (3 weeks)
- Parse IL opcodes → structured IR.
- Handle control flow, try/catch/finally, loops, boxing/unboxing.
- **Deliverable:** method bodies compile to C++.

### Phase 3 — Runtime support (2 weeks)
- Reuse VGS C++ runtime (`shared_ptr`, `CppArrayRuntime`, `BasicLangRt`).
- Strings, arrays, boxing, exceptions.
- **Deliverable:** runtime-complete programs run natively.

### Phase 4 — BCL strategy (ongoing, Option B)
- Compile the entire .NET BCL from IL (full IL2CPP).
- Start with VGS-generated IL, expand to arbitrary .NET assemblies.
- Provide C++ implementations for BCL internals (Math, String, List, Dictionary, etc.)
  where IL compilation isn't sufficient.
- **Deliverable:** arbitrary .NET assemblies compile to native C++.

### Phase 5 — Integration
- Add `--target=il2cpp` CLI flag.
- Reuse `FourBackends` parity testing.
- Add IL2CPP test suite mirroring `MsilDecimalExecutionTests`.

## 7. Key risks

- **Scope:** Full IL2CPP is a multi-year project (Unity's took years). Phase 0 spike de-risks.
- **BCL completeness:** Even with Option B, some BCL internals need native C++ (GC, threading, P/Invoke).
- **Reflection:** IL2CPP supports limited reflection. VGS should match (minimal initially).
- **Generics monomorphization:** Can cause code size explosion. Generic sharing (like IL2CPP) mitigates.
- **Testing:** TDD from the start (Unity's approach). Every opcode lowering needs a test.

## 8. Reuse map

| IL2CPP need | Existing VGS asset |
|---|---|
| C++ emission | `CppCodeGenerator.cs` (4,058 lines) |
| Array runtime | `CppArrayRuntime` (`BasicLang::Array<T>`) |
| Boxing/coercion | `EmitCoerceToSlot`, `ValueTypeBoxToken` (MSILBackend) |
| Strings | `BasicLangRt` (String → std::string) |
| Exceptions | `IRThrow`, try/catch/finally |
| Testing | `MsilHarness`, `FourBackends`, `BclE2E` |
| GC | `shared_ptr` (classes) |
| Type mapping | `TypeMapper`, `_typeMap` (MSILBackend) |
| Metadata | `System.Reflection.Metadata` (built-in .NET) |

## 9. Success criteria

- Phase 0: A trivial VGS program compiles through MSIL → IL reader → C++ → runs natively with no .NET runtime.
- Phase 1: A non-trivial VGS program (with classes, generics, exceptions) compiles and runs.
- Phase 4: A non-VGS .NET assembly (e.g., a simple console app) compiles to native C++ and runs.

## 10. Non-goals (for now)

- JIT support (AOT only).
- `Reflection.Emit` / runtime code generation.
- Full .NET reflection (minimal subset initially).
- WebAssembly target (can be added later via Emscripten, like IL2CPP).