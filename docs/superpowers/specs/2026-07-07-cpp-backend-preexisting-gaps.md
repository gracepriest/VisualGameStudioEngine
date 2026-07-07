# Pre-existing C++ Backend Gaps — Follow-ups (surfaced 2026-07-07)

> These are **pre-existing** C++ backend defects that exist independently of the
> BasicLang std-library / passthrough feature (branch `feat/cpp-stdlib-passthrough`).
> They were surfaced by a 20-agent probe sweep that drove the **real CLI** (which runs the
> IR optimizer) rather than the string-only test helper. Per the maintainer's scope decision
> (2026-07-07), the std-library branch fixes the ~13 feature-owned bugs plus the `For Each`
> block-walker blocker; **these remaining backend gaps are deferred to separate follow-ups.**
>
> Each entry: severity, a minimal repro, and the verified failure. All were confirmed by
> generating C++ via `BasicLang.exe compile <f> --target=cpp` and compiling with MSVC
> (`cl /std:c++20 /EHsc`), or by observing the front-end behavior.

## Meta-finding (important, partly addressed on the std-lib branch)

**The C++ backend's test suite was not running the IR optimizer.** The `CompileToCpp` test
helper went Lexer→Parser→Semantic→IRBuilder→CppCodeGenerator, skipping the optimizer passes
that the real CLI runs — so an entire class of optimizer-dependent codegen bugs was invisible
to the 2300+ green tests (e.g. the `For Each` dead-code-elimination bug fixed in `4dc1fba`).
A `CompileToCppOptimized` helper was added on the std-lib branch to close this; **future C++
backend tests should exercise the optimizer (or drive the CLI) so they test what users get.**

## Gaps (deferred)

1. **Plain arrays are broken on the C++ backend (CRITICAL).**
   - `Dim a(3) As Integer` emits `Integer[] a = {};` — `Integer` is the BasicLang type name (not a
     C++ type) and `Type[] name` is not valid C++. `MapType` has no array case → falls through to
     the literal `"Integer[]"`. cl: C2065/C2146.
   - Array element READ `a(0)` lowers via `IRGetElementPtr`+`IRLoad` as `t = &a[0]; *t;` but the
     temps are typed as the element type (`int32_t`) not `int32_t*` → cl C2100/C2440.
     (Note: our `IRIndexerStore` work did NOT touch this — arrays use the GEP/Load path, not the
     collection indexer path.)
   - Array literal init `Dim a() As Integer = {5,10,15}` scrambles temp wiring (duplicate temp
     decl, literal discarded, store to a phantom undeclared temp).
   - **Status:** the array-literal-decl type-mapping piece is being fixed separately in a spun-off
     session (task `task_de673a90`). The read/store temp-typing pieces remain.

2. **`"text" & anInteger` (String concat with a non-string) emits `const char* + int` pointer
   arithmetic (CRITICAL).** Reproduces with a plain `int` and no collections. `"n=" & 5` prints
   garbage / fails to compile (C2110/C2676). The `&`/`+` concat lowering doesn't convert the
   non-string operand via `std::to_string`. Ubiquitous idiom; high value.

3. **`ByRef` parameters are lowered to pass-by-value (CRITICAL) / param-reassignment crash.**
   `FormatParameter` only honors `IsByRef` on the extern-"C" path; normal-function `ByRef` params
   emit by value, so reassigning (and sometimes mutating) a `ByRef` arg is invisible to the caller.
   A parameter reassignment followed by a bare `.Add(arg)` also crashes codegen (internal
   `ArgumentOutOfRangeException`, no `.cpp` emitted). Universal, not collection-specific.

4. **An interface that declares a `Property` HANGS the compiler (IMPORTANT).**
   `Interface IFoo : Property V As Integer : End Interface` spins forever (front-end parser/
   analyzer infinite loop) — no diagnostic, no output. A hang is worse than an error.

5. **C++ backend emits classes in file-listing order (no topological sort) (CRITICAL).**
   A class whose inline method dereferences a later-emitted class fails with C2027 (incomplete
   type). Needs a topological ordering (or forward declarations) of emitted class definitions.

6. **`x * 2^n` strength-reduction returns an undeclared temp (CRITICAL).**
   A non-constant `int * powerOf2` is strength-reduced to a shift but the optimizer/codegen wires
   the result to an undeclared temp (`t1` vs declared `t0`) → non-compiling C++. Optimizer bug.

7. **`Is Nothing` / `IsNot Nothing` do not parse (IMPORTANT).**
   Only `= Nothing` works; `x Is Nothing` fails to parse. This is a general parser gap (not
   collection-specific). Note: the std-lib spec's promise of `collection Is Nothing` null checks
   is blocked by this — collections DO lower null correctly (nullptr), but the `Is Nothing`
   *syntax* is unavailable until the parser supports it. Use `= Nothing` meanwhile.

8. **`Boolean.ToString` / printing a Boolean shows `1`/`0` instead of `True`/`False` (IMPORTANT).**
   `Console.WriteLine(someBool)` prints `1`/`0` on C++ (raw `<<` of a bool) rather than .NET's
   `True`/`False`. Affects `HashSet.Add`/`Contains`/`Remove` result printing among others.

9. **`Extern … Cpp: "impl" … End Extern` never parses (CRITICAL) — the whole per-backend Extern
   feature is unreachable.** The lexer registers `cpp`/`csharp`/`llvm`/`msil` as case-insensitive
   inline-code keywords, so the Extern platform label `Cpp:` lexes as `InlineCpp`, and
   `ParseExtern`'s `Consume(Identifier)` for the platform name can never match. Predates the `::`
   work (confirmed against `3020b93^`). Makes `GenerateExternDeclaration` + the canonical demo
   dead code. (The inline `cpp{}` block form is separate and works on the C++ backend.)

## Not deferred (fixed on the std-lib branch)

For reference, the feature-owned bugs (foreign `New`, type-inferred foreign locals, call-vs-indexer,
interface generic dropping, indexer typing, `.Item`, `Remove` return type, nested indexer, struct
`operator==`, and the honesty-matrix gaps) plus the `For Each`/`Try` block-walker blocker were fixed
on `feat/cpp-stdlib-passthrough` — see that branch's `fix(cpp): …` commits.
