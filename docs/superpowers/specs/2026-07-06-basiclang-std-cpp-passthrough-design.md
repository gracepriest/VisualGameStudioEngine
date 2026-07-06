# BasicLang Std Layering + C++ Std Passthrough — Design Decisions (Spec)

> **Status: DESIGN LOCKED 2026-07-06** — approved during brainstorming (main session,
> 2026-07-06). This spec is the authority for the implementation plan
> `docs/superpowers/plans/2026-07-06-basiclang-std-cpp-passthrough.md` (to be written next).
> Follows on from the C++ backend overhaul (`2026-07-05-cpp-backend-overhaul-decisions.md`),
> which explicitly deferred the ".NET surface" gap this spec now fills.

## Problem

When the C++ backend is selected, BasicLang has no everyday collections and no way to reach
the C++ standard library. Two concrete gaps:

1. **No portable collections.** `List(Of T)`, `Dictionary(Of K, V)`, `HashSet(Of T)` are
   `TypeKind.Class` names the C++ capability checker rejects with
   `".NET type 'List' — no C++ mapping exists"` (CppCapabilityChecker.cs:141). They work on
   the C# backend but cannot lower to C++ at all. Real game/tool code needs data structures.
2. **No C++ std passthrough.** There is no analog to "all of .NET is just there on C#." A
   user cannot pull in a C++ header and write `Dim m As std::mutex`. The two existing foreign
   passthrough mechanisms (`Extern`/inline, below) bind *one function or block* at a time —
   they are not a general "use any std type" surface.

The fix is a deliberate **two-layer** std architecture, mirroring how the C# backend already
works: a portable everyday layer that compiles on every backend, plus a backend-specific
full-platform passthrough (all of .NET on C#; all of `std::` on C++).

## Audit findings (verified against code)

| Fact | Evidence |
|---|---|
| Everyday scalar std already exists as an extensible switch (strings, math, conversions, `Print`) | `EmitStdLibCall` (CppCodeGenerator.cs:1605), `null` fall-through for unknown names (CppCodeGenerator.cs:1654) |
| Capability checker is data-driven: one whitelist + one gate | `MappedTypeNames` HashSet (CppCapabilityChecker.cs:49); `CheckType` (CppCapabilityChecker.cs:106); recurses into generic args (CppCapabilityChecker.cs:119–121) |
| `List`/`Dictionary`/`HashSet` are rejected exactly here | class-kind, not user-defined, not in whitelist → diag (CppCapabilityChecker.cs:138–142) |
| Runtime types are already emitted as a usage-gated preamble (the precedent for collection wrappers) | `BasicLang::Task<T>` / `Generator<T>`; includes added on demand e.g. `coroutine`, `exception` (CppCodeGenerator.cs:234–239) |
| Per-backend passthrough #1 — `Extern` with per-platform implementation | `ExternDeclarationNode.GetImplementation(platform)` (ASTNodes.cs:889 / IRNodes.cs:1204); spliced by C++ backend (CppCodeGenerator.cs:1501) |
| Per-backend passthrough #2 — language-tagged inline code blocks | `InlineCodeNode` (ASTNodes.cs:1591) → `IRInlineCode` (IRNodes.cs:732) → emitted (CppCodeGenerator.cs:2370); tag captured by lexer (`InlineCodeValue`, BasicLangLexer.cs:344) |
| Analyzer already types collection members on the C# path (so the everyday layer only needs C++ codegen work) | `LookupNetTypeMember` / `GetStringMethodReturnType` / `GetCommonMethodReturnType` / `IsIndexableGenericType` + `IRIndexerAccess` (per CLAUDE.md "Recent Bug Fixes") |
| **`#Include` is already taken** — it does VB-style *source-file splicing*, not C++ header emission, and owns **both** delimiter forms | `Preprocessor.cs:80` dispatches `#Include` case-insensitively; `ProcessInclude` (Preprocessor.cs:153) parses `#Include "file.bas"` **and** `#Include <file.bh>` (angle = system-path source include, line 166–169), with include guards (`_includedFiles`, line 203) |
| C++ header-emission plumbing **already exists** — only the wiring from a user directive is missing | `_headerIncludes` list declared and seeded internally (CppCodeGenerator.cs:21/40/237), emitted deduped (HashSet, line 231) and ordered at file top; no user-directive path feeds it yet |
| `TypeKind` enum's home (for the new `Foreign` value) | `enum TypeKind` (SymbolTable.cs:161) |

## Locked decisions

### Layer 1 — Everyday std (portable across backends)

1. **Scalar functions unchanged.** The existing `EmitStdLibCall` surface stays exactly as-is
   and is **not** expanded in this work (YAGNI).

2. **Three portable collections added**, lowered natively per backend:

   | BasicLang | C++ wrapper | wraps | C# |
   |---|---|---|---|
   | `List(Of T)` | `BasicLang::List<T>` | `std::vector<T>` | .NET `List<T>` |
   | `Dictionary(Of K, V)` | `BasicLang::Dictionary<K,V>` | `std::unordered_map<K,V>` | .NET `Dictionary<K,V>` |
   | `HashSet(Of T)` | `BasicLang::HashSet<T>` | `std::unordered_set<T>` | .NET `HashSet<T>` |

3. **Core method surface (v1):**
   - **List** — `Add`, `Count`, `Item(i)` / indexer `list(i)`, `Contains`, `IndexOf`,
     `Remove`, `RemoveAt`, `Insert`, `Clear`, `For Each`.
   - **Dictionary** — `Add(k,v)`, `Item(k)` / indexer `dict(k)`, `ContainsKey`,
     `TryGetValue(k, ByRef v)`, `Keys`, `Values`, `Remove`, `Count`, `Clear`.
   - **HashSet** — `Add`, `Contains`, `Remove`, `Count`, `Clear`, `For Each`.

4. **.NET-faithful semantics in the wrapper**, not C++-native shortcuts:
   - Dictionary indexer **read** of a missing key **throws** (like `KeyNotFoundException`),
     not silent-insert (C++ `operator[]` behavior). Read/write map to distinct wrapper
     entry points so the read path can throw while the write path inserts.
   - `Dictionary.Add` on a **duplicate key throws** (matches .NET). We have real exceptions
     since the overhaul (`IRThrow`), so this is expressible.

5. **Lowering site = thin runtime wrapper header** (chosen over a pure codegen translation
   table). `BasicLang::List<T>` / `Dictionary<K,V>` / `HashSet<T>` are emitted **inline into
   the generated `.cpp` as a usage-gated preamble** — the exact mechanism already used for
   `BasicLang::Task`/`Generator`. **No new file to ship**, no version skew. Wrappers are thin
   `public`-composition over the std container and inline to zero cost at `-O2`. Member calls
   pass through **unchanged** (`list.Add(x)` → `list.Add(x)`); the wrapper owns all
   name/semantic translation in real, unit-testable C++.

6. **The one unavoidable codegen bridge:** C++ has no properties, so property-syntax members
   `.Count` / `.Keys` / `.Values` get `()` appended by a small, bounded codegen rule. Every
   method-style member passes through untouched. (Exact IR shape — MemberAccess vs
   parameterless Call — to be confirmed during implementation; the rule adapts to whichever.)

7. **`For Each` scope (v1):** over `List` and `HashSet` (element type) and over `dict.Keys` /
   `dict.Values` (which are `List`s). Direct `For Each` over a `Dictionary` yielding
   `KeyValuePair` is a **follow-up** (keeps v1 bounded; iterate `.Keys`/`.Values` meanwhile).

### Layer 2 — Full C++ std passthrough (C++ backend only)

8. **`#CppInclude` directive** — a **new, distinct** directive (NOT `#include`/`#Include`,
   which already means VB-style source-file splicing; see audit table). Syntax:
   `#CppInclude <mutex>` (system header) and `#CppInclude "mygrid.h"` (local header). On C++ →
   emits a real `#include` at file top by feeding the **existing** `_headerIncludes` list
   (already deduped + ordered — CppCodeGenerator.cs:231/237); the work is *wiring*, not new
   emission. On any other backend → clean capability error. The existing `#Include`
   source-splicer is left completely untouched. Routed in `Preprocessor.cs` alongside the
   sibling `#`-directives.

9. **Foreign types = any `::`-qualified type name** (`std::mutex`, `std::deque`,
   `mylib::Widget`, `::GlobalThing` where a leading `::` denotes the C++ global namespace).
   Recognized as a new `TypeKind.Foreign`, treated as **opaque**:
   - Templates use BasicLang `(Of …)`: `std::deque(Of Integer)` → `std::deque<int32_t>`,
     nesting recursively (`std::map(Of String, std::vector(Of Integer))` →
     `std::map<std::string, std::vector<int32_t>>`). Angle brackets never enter the grammar.
   - **Member access is unchecked passthrough:** `m.lock()` → `m.lock()` verbatim; result is
     `Foreign` (usable, not deeply checked). The C++ compiler is the type checker — we do not
     re-derive C++'s type system. Documented tradeoff: past the `#CppInclude`, you get C++'s
     error messages, not BasicLang's.
   - **Value semantics:** foreign types emit as plain value locals (`std::mutex m;`), **not**
     `shared_ptr` like BasicLang classes. Heap/ownership is opt-in via explicit
     `std::unique_ptr(Of T)` / `std::shared_ptr(Of T)`.

10. **Existing `Extern`/inline mechanisms are preserved and documented** as the third
    passthrough path (bind one specific function via `GetImplementation("Cpp")`, or drop a raw
    `cpp{ … }` block). No change to their behavior.

### Cross-cutting

11. **Capability checker changes (C++):** allow `List`/`Dictionary`/`HashSet` (recursing into
    element/key/value types) and any `::`-qualified `Foreign` type. Keep rejecting all other
    bare .NET types (`Object`, un-shimmed `DateTime`, …) — unchanged.

12. **Backend honesty matrix (never silent garbage):**
    - **C++** — collections + passthrough fully work, compile-and-run.
    - **C#** — collections already work natively; `#CppInclude` / `std::` foreign types →
      clean *"C++ passthrough requires the C++ backend"* error.
    - **LLVM / MSIL** — collections, `#CppInclude`, foreign types → clean *"not yet supported
      on <backend>"* error.

13. **Error handling:**
    - Unknown collection method → normal BasicLang semantic diagnostic (analyzer already
      knows the member set).
    - Bad `#CppInclude` / foreign-type typo → surfaced from the C++ compiler through the
      build (the accepted cost of opaque passthrough).
    - Passthrough on the wrong backend → clean capability error (decision 12).

14. **Process:** TDD (NUnit), one commit per green task, full `dotnet test` suite as the gate
    each time. New/extended tests:
    - **Wrapper unit tests** — tiny C++ harness exercising `BasicLang::List`/`Dictionary`/
      `HashSet` semantics directly (incl. the throwing paths).
    - **E2E compile-and-run (C++)** — build all three collections; `Add`/`Count`/
      `ContainsKey`/`TryGetValue`/iterate; print; assert output; compiled with the real
      toolchain via `CppToolchain` at `-std=c++20`. Extends the `Cpp_EndToEnd` pattern.
    - **E2E portability (C#)** — the *same* collection source compiles + runs on C# → proves
      the everyday layer is truly portable.
    - **Passthrough E2E** — `#CppInclude <mutex>` + `Dim m As std::mutex` + `m.lock()`/
      `m.unlock()` builds and runs.
    - **Capability-error tests** — collections / `#CppInclude` / foreign types on
      C#/LLVM/MSIL assert the clean diagnostic. Include a test that a plain `#Include` of a
      BasicLang source file still splices correctly (regression guard on the un-touched
      directive).
    - **Codegen string-asserts** — `List(Of Integer)` → `BasicLang::List<int32_t>`;
      `std::deque(Of Integer)` → `std::deque<int32_t>`.

## Files touched

- `BasicLang/CppCapabilityChecker.cs` — allow-list for collections + `Foreign` types.
- `BasicLang/CppCodeGenerator.cs` (+ type mapper) — collection/foreign type mapping, wrapper
  preamble emission (usage-gated), property-bridge rule, Dictionary indexer read/write,
  wiring `#CppInclude` targets into the **existing** `_headerIncludes` list (CppCodeGenerator.cs:21/40/231/237 —
  emission already exists), foreign passthrough (value semantics, `(Of …)` → `<…>`, opaque
  members).
- `BasicLang/Preprocessor.cs` — recognize the new `#CppInclude` directive (route to C++ header
  collection on C++; clean error elsewhere) **without altering** the existing `#Include`
  source-splicer (Preprocessor.cs:80/153).
- `BasicLang/SymbolTable.cs` — add `Foreign` to `enum TypeKind` (SymbolTable.cs:161).
- `BasicLang/BasicLangLexer.cs` / `Parser` / `ASTNodes.cs` — `::`-qualified type-name parsing
  (incl. leading `::`); `(Of …)` on qualified names. (The `#CppInclude` directive is handled
  in the preprocessor, ahead of the lexer.)
- `BasicLang/SemanticAnalyzer.cs` — `TypeKind.Foreign` recognition, opaque member access,
  non-C++ backend guards; confirm collection member typing on the C++ path.
- `BasicLang/IRNodes.cs` / `IRBuilder.cs` — IR for foreign types as needed (the `#CppInclude`
  target may be carried as codegen state rather than an IR node — settled in the plan).
- `BasicLang/CSharpBackend.cs`, `BasicLang/LLVMBackend.cs`, `BasicLang/MSILBackend.cs` — clean
  errors for `#CppInclude` / foreign types (decision 12).
- `VisualGameStudio.Tests/` — the test set in decision 14.

## Out of scope (explicit follow-ups)

- Queue / Stack / LinkedList / SortedDictionary and other containers.
- LINQ-style operators (`Where`/`Select`/`OrderBy`) beyond what already exists.
- `For Each` directly over a `Dictionary` (KeyValuePair) — decision 7.
- LLVM / MSIL collection + passthrough support (they emit clean errors in v1).
- Richer, non-opaque foreign-type checking (opaque is the design, not a limitation to fix).
- Expanding `DateTime` / other .NET scalar surface on the C++ backend.

## Task order (for the plan)

1. Runtime wrapper header (`BasicLang::List`/`Dictionary`/`HashSet`) + C++ unit tests
   (semantics first, no compiler wiring).
2. Capability checker allow-list + type mapping for the three collections; codegen preamble
   emission (usage-gated) + auto-includes.
3. Member/indexer/property lowering (property-bridge, indexer read/write, `TryGetValue`,
   `Keys`/`Values`, `For Each`); C++ E2E compile-and-run + C# portability E2E.
   *Budget a short investigation spike first* to confirm the IR shape of `.Count`/`.Keys`/
   `.Values` (MemberAccess vs parameterless Call — decision 6) so the property-bridge rule
   isn't blocked mid-task.
4. `#CppInclude` directive: recognize in `Preprocessor.cs` (distinct from `#Include`) → wire
   targets into the existing `_headerIncludes` emission + non-C++ clean errors. Regression
   test that plain `#Include` source-splicing still works.
5. `Foreign` types: `::`-qualified parsing, `TypeKind.Foreign`, opaque members, value
   semantics, `(Of …)` → `<…>`; capability allow-list; passthrough E2E.
6. Cross-backend clean-error tests (C#/LLVM/MSIL); docs (BasicLang reference); IDE binary
   refresh.
