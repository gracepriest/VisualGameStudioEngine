# Phase 0 Spike Plan — IL2CPP for VGS

**Date:** 2026-11-01
**Goal:** Prove that VGS can compile a trivial MSIL assembly to native C++ without a .NET runtime.
**Duration:** 1 week
**Deliverable:** A working `--target=il2cpp` (or `--target=cpp-from-il`) that compiles "Hello, world!" from IL to a native binary.

## Prerequisites

- VGS builds on Windows (MSVC) and Linux (g++/clang++).
- `System.Reflection.Metadata` is available (built into .NET SDK, no NuGet needed).
- The MSIL backend (`--target=msil`) produces valid IL assemblies.

## Task 0.1 — IL reader spike (2 days)

**Goal:** Read a VGS-generated MSIL assembly and print its types/methods.

1. Create a new project: `BasicLang/ILReader/` or add to `BasicLang/`.
2. Use `System.Reflection.Metadata`:
   - `MetadataReaderProvider` to open the assembly.
   - Iterate `TypeDefinitions` → print type names.
   - Iterate `MethodDefinitions` → print method names + signatures.
   - Read method bodies (IL bytecode) as byte arrays.
3. Test: read the IL from `hello.bas --target=msil` output.

**Acceptance:** Can print "Module Hello / Sub Main / Console.WriteLine" from the IL assembly.

**Files to create:**
- `BasicLang/ILReader/ILAssemblyReader.cs`
- `BasicLang/ILReader/ILTypeReader.cs`
- `BasicLang/ILReader/ILMethodReader.cs`

## Task 0.2 — Minimal C++ emitter (2 days)

**Goal:** Emit a C++ "hello world" from the IL reader output.

1. Create `ILCppCodeGenerator.cs` (new backend, `BackendName => "ILCPP"`).
2. For the spike, hardcode the emission:
   - `#include <iostream>`
   - `int main() { std::cout << "Hello, IL2CPP!" << std::endl; return 0; }`
3. Wire into CLI: `--target=il2cpp` (or `--target=cpp-from-il`).
4. Compile the emitted C++ with MSVC/g++ and run.

**Acceptance:** `BasicLang.exe hello.bas --target=il2cpp` → compiles → runs → prints "Hello, IL2CPP!".

**Files to create:**
- `BasicLang/ILCppCodeGenerator.cs`
- `BasicLang/BackendRegistry.cs` (add registration)

## Task 0.3 — Read IL method body and emit a call (2 days)

**Goal:** Replace the hardcoded emitter with one that reads the IL method body and emits a `Console.WriteLine` call.

1. Parse the `Main` method's IL bytes.
2. Recognize opcodes: `ldstr`, `call`, `ret`.
3. Emit C++: `std::cout << "Hello, IL2CPP!" << std::endl;` (mapping `ldstr` + `call Console.WriteLine`).
4. Handle the entry point (`Main` → `main`).

**Acceptance:** The generated C++ is derived from the IL, not hardcoded. Changing the string in the .bas file changes the output.

**Files to modify:**
- `BasicLang/ILCppCodeGenerator.cs`

## Task 0.4 — Build and run (1 day)

**Goal:** End-to-end: .bas → MSIL → IL reader → C++ → native binary → run.

1. CLI pipeline: `BasicLang.exe hello.bas --target=il2cpp`:
   - Compile .bas to MSIL (existing).
   - Read MSIL assembly (new IL reader).
   - Emit C++ (new generator).
   - Compile C++ with MSVC/g++ (reuse `CppCompile`).
   - Run the binary.
2. Test on Windows (MSVC) and Linux (g++).

**Acceptance:** Full pipeline works on both platforms. No .NET runtime needed to run the output.

## Task 0.5 — Spike report (1 day)

**Goal:** Document findings and decide whether to proceed to Phase 1.

1. Write `docs/superpowers/spike-results/2026-11-01-il2cpp-phase0.md`:
   - What worked.
   - What was harder than expected.
   - Estimated effort for Phase 1.
   - Go/no-go recommendation.

## Definition of done for Phase 0

- [ ] `BasicLang.exe hello.bas --target=il2cpp` produces a native binary.
- [ ] The binary runs and prints the expected output.
- [ ] No .NET runtime is needed to run the output.
- [ ] The IL reader can parse VGS-generated MSIL assemblies.
- [ ] Spike report written with go/no-go recommendation.

## What Phase 0 does NOT do

- No generics, no exceptions, no arrays, no classes.
- No arbitrary .NET assemblies (only VGS-generated MSIL).
- No BCL compilation (hardcoded `std::cout` for the spike).
- No runtime library (libil2cpp equivalent) — that's Phase 3.

## Risks

- `System.Reflection.Metadata` API may be more complex than expected (metadata tokens, type specs).
- IL method body parsing (opcodes, exception handlers) has edge cases.
- The MSIL backend must produce IL that the reader can parse (it should, since it's valid IL).

## Next steps after Phase 0

If the spike succeeds:
- Proceed to Phase 1 (IL reader + type system).
- Write a detailed Phase 1 plan.
- Consider whether to reuse `CppCodeGenerator` or keep `ILCppCodeGenerator` separate.

If the spike fails:
- Reassess: maybe start with a narrower scope (only value types, no classes).
- Consider using Mono's `Cecil` library instead of `System.Reflection.Metadata` (but Cecil is an external dependency).