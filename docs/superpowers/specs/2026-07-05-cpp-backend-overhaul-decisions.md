# C++ Backend Overhaul — Design Decisions (Spec)

Decisions locked during brainstorming (side-chat session, 2026-07-05). This spec is the
authority for the implementation plan `docs/superpowers/plans/2026-07-05-cpp-backend-overhaul.md`.

## Problem

The C++ backend (`BasicLang/CppCodeGenerator.cs`, ~1,890 lines) reliably handles a
"procedural core plus simple classes" subset of BasicLang. Outside that subset there are
three failure modes: `#warning` in output (async/yield), C++ that won't compile
(generics, unmapped .NET types, catch types), and — worst — C++ that compiles but behaves
differently than the C# backend (value vs. reference object semantics, silently dropped
`Finally` blocks).

## Audit findings (verified against code)

| Feature | Status | Evidence |
|---|---|---|
| Generics/templates | Silently dropped; `T` emitted as undefined type | `GenerateClass`/`GenerateFunction` never read `GenericParameters`; `TypeMapperBase.MapType` (TypeMapper.cs:36) ignores `TypeInfo.GenericArguments` |
| Async/Await | `#warning`, runs synchronously | CppCodeGenerator.cs:1536 |
| Iterators (Yield) | `#warning`, values never yielded | CppCodeGenerator.cs:1542 |
| Finally blocks | Silently dropped | `Visit(IRTryCatch)` (CppCodeGenerator.cs:1628) never reads `FinallyBlock` (IRNodes.cs:877) |
| Throw | Broken on **every** backend — IR never carries it | `IRBuilder.Visit(ThrowStatementNode)` (IRBuilder.cs:2677) emits only `IRComment` |
| Catch exception types | .NET type name emitted verbatim → won't compile | CppCodeGenerator.cs:1644 |
| Lambdas | `IsLambda` never consulted; `__lambda_N` refs dangle | delegate aliases → `std::function` exist (CppCodeGenerator.cs:263); C# blueprint: skip at top level (CSharpBackend.cs:1358), inline at use site (CSharpBackend.cs:2659) |
| .NET types (List, Dictionary, DateTime…) | Not in `CppTypeMapper` map → undefined names | TypeMapper.cs:205 |
| Reference semantics | Objects constructed **by value** → copy semantics, slicing | `Visit(IRNewObject)` (CppCodeGenerator.cs:1551) |

## Locked decisions

1. **Reference semantics**: `std::shared_ptr<Foo>` for `TypeKind.Class`/`Interface` values,
   `std::make_shared` construction, `->` member access, `nullptr` for `Nothing`.
   `TypeKind.Structure` stays a value type.
2. **Generics**: emit real C++ templates (`template <typename T>`). Constraints
   (`Of T As IComparable`) are dropped with a comment — parity with the C# backend today;
   C++20 concepts later. Recursive generic argument mapping: `Stack(Of Integer)` →
   `Stack<int32_t>`. `T` default-initialization via value-init `{}`.
3. **Async**: synchronous `Task<T>` emulation (type-correct struct in runtime preamble,
   no scheduler). **Yield**: real C++20 coroutine `Generator<T>` in a runtime preamble.
   Generated code targets `-std=c++20`.
4. **Capability diagnostics are hard errors** (thrown from `CppCodeGenerator.Generate`),
   removed one-by-one as features land. Diagnostics for unmapped .NET types and `Object`
   stay **permanently** (the un-designed ".NET surface" gap is out of scope).
5. **Finally** lowered as: finally body emitted after catches, plus
   `catch (...) { <finally>; throw; }`. Documented limitation: `Return` inside `Try`
   bypasses the finally body.
6. **Throw** requires a new `IRThrow` IR node (default no-op visit so LLVM/MSIL are
   unaffected); C# backend gets a real `throw` emission at the same time since it is
   equally broken today.
7. **Process**: TDD (NUnit, manual-pipeline helper modeled on
   `CompilationTests.CompileToCSharp`); new test file
   `VisualGameStudio.Tests/Compiler/CppBackendTests.cs`; one commit per green task; full
   suite (`dotnet test`) as the gate each time.

## Task order

1. Backend capability diagnostics (makes all gaps honest)
2. Generics → real C++ templates
3. Reference semantics (`shared_ptr`)
4. Throw + Finally + exception type mapping
5. Lambdas → C++ lambdas
6. Async (`Task<T>` emulation) + Yield (C++20 coroutines)
7. End-to-end validation (real C++ compiler smoke test), docs, IDE binary refresh
