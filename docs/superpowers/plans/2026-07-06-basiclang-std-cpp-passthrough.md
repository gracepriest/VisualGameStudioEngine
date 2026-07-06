# BasicLang Std Layering + C++ Std Passthrough — Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the BasicLang C++ backend portable everyday collections (`List`/`Dictionary`/`HashSet` lowered to thin `BasicLang::List<T>` wrappers over the C++ std containers) plus full C++ std passthrough (a `#CppInclude` directive and `::`-qualified opaque foreign types), so `Dim m As std::mutex` works the way "all of .NET" works on the C# backend.

**Architecture:** Two layers. (1) Everyday collections are real C++ wrapper types emitted once into the generated `.cpp` runtime preamble (same mechanism as the existing `BasicLang::Task`/`Generator`); the wrappers own all .NET-name→C++ semantics, so most member calls lower by **raw passthrough** and only `.Count`/`.Keys`/`.Values` and the Dictionary indexer need codegen bridges. (2) Passthrough is a new `#CppInclude` preprocessor directive feeding the existing `_headerIncludes` emission, plus a new `TypeKind.Foreign` for `::`-qualified names that map verbatim (value semantics, `(Of …)`→`<…>`) with unchecked opaque member access. The C#/LLVM/MSIL backends reject both with a clean error.

**Tech Stack:** C# (net8.0) compiler; NUnit 4 tests; generated C++ targets `-std=c++20`; real C++ compiler probed via `clang++`/`g++`/MSVC-vswhere for compile-and-run tests.

**Spec:** `docs/superpowers/specs/2026-07-06-basiclang-std-cpp-passthrough-design.md`

---

## Conventions (read once)

- **Build the compiler:** `dotnet build BasicLang/BasicLang.csproj -c Release`
- **Run the whole suite (the gate every task ends on):** `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release`
- **Run one test class fast:** `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~CppCollectionTests"`
- **Repo root:** `C:\Users\melvi\source\repos\VisualGameStudioEngine` (work happens here on a feature branch; the declared worktree dir is empty — see Execution Handoff).
- **Editing hazard (from project memory):** ALWAYS use the Edit/Write tools on this repo's BOM-less UTF-8 files. Never round-trip through PowerShell `Get-Content`/`Set-Content` (it has corrupted files here 3×). For git commit messages with special chars, write the message to a temp file and use `git commit -F <file>`.
- **TDD loop for every step:** write failing test → run it, see it fail for the expected reason → minimal implementation → run it, see it pass → run the full suite → commit. One logical change per commit.
- The C++ capability checker throws *before* codegen, so **Task 2 must relax it before any collection codegen (Task 3) can run** — respect task order.

---

## File Structure

**Create:**
- `BasicLang/Compiler/CodeGen/CPlusPlus/CppCollectionsRuntime.cs` — `public static class CppCollectionsRuntime { public const string Source = @"…"; }`. Single source of truth for the `BasicLang::List/Dictionary/HashSet` C++ wrapper text. Emitted by the preamble AND compiled directly by the unit test (no duplication).
- `BasicLang/Compiler/CodeGen/ForeignFeatureChecker.cs` — `ForeignFeatureChecker.Check(IRModule)` + `ForeignFeatureException`; rejects `#CppInclude` headers / `TypeKind.Foreign` types on non-C++ backends.
- `VisualGameStudio.Tests/Native/CppCollectionsRuntimeTests.cs` — compiles `CppCollectionsRuntime.Source` + a test `main` with a real compiler and asserts runtime behavior (semantics-in-isolation).
- `VisualGameStudio.Tests/Compiler/CppCollectionTests.cs` — codegen string-asserts + C++ compile-and-run E2E for collections.
- `VisualGameStudio.Tests/Compiler/CppPassthroughTests.cs` — `#CppInclude` + `::` foreign-type codegen + E2E.
- `VisualGameStudio.Tests/Compiler/ForeignFeatureGuardTests.cs` — C#/LLVM/MSIL clean-error tests + `#Include` regression guard.

**Modify:**
- `BasicLang/SymbolTable.cs:161` — add `Foreign` to `enum TypeKind`.
- `BasicLang/CppCapabilityChecker.cs` — allow `List`/`Dictionary`/`HashSet` and `::` foreign types.
- `BasicLang/CppCodeGenerator.cs` — `MapType` branches (collections + foreign), `MemberAccessOp` guard, `Visit(IRNewObject)` collection value-init, `.Count`/`.Keys`/`.Values` + indexer lowering, `usesCollections` preamble gate + includes, `#CppInclude` token emission, `CppCodeGenOptions.CppIncludes`.
- `BasicLang/Preprocessor.cs` — `#CppInclude` dispatch branch + `_cppIncludes` collection + `CppIncludes` accessor.
- `BasicLang/Compiler.cs` — read `_preprocessor.CppIncludes` after `Process`, thread into `CppCodeGenOptions.CppIncludes`.
- `BasicLang/SemanticAnalyzer.cs` — `ResolveTypeName` `::`→`Foreign`; `Visit(MemberAccessExpressionNode)` opaque-Foreign branch; extend `GetCommonMethodReturnType` with collection member return types.
- `BasicLang/CSharpBackend.cs`, `BasicLang/LLVMBackend.cs`, `BasicLang/MSILBackend.cs` — call `ForeignFeatureChecker.Check` at the top of `Generate`.
- (If needed) `BasicLang/Lexer`/`Parser`/`ASTNodes.cs` — accept `::` in type-name tokens (verified in Task 5 Step 1).

---

## Task 1: Collection runtime wrappers + isolated C++ unit tests

Build and prove the `BasicLang::List/Dictionary/HashSet` C++ wrappers *before* wiring any compiler code. The wrapper text is a C# const so the test compiles the exact bytes the codegen will emit.

**Files:**
- Create: `BasicLang/Compiler/CodeGen/CPlusPlus/CppCollectionsRuntime.cs`
- Create: `VisualGameStudio.Tests/Native/CppCollectionsRuntimeTests.cs`

- [ ] **Step 1: Write the wrapper runtime const**

Create `CppCollectionsRuntime.cs`:

```csharp
namespace BasicLang.Compiler.CodeGen.CPlusPlus
{
    /// <summary>
    /// C++ source for the everyday-collection wrappers (BasicLang::List/Dictionary/HashSet).
    /// Single source of truth: emitted verbatim into the generated .cpp runtime preamble
    /// (see CppCodeGenerator.EmitRuntimePreamble) AND compiled directly by the runtime
    /// unit tests. Requires <vector> <unordered_map> <unordered_set> <algorithm>
    /// <stdexcept> <cstdint> (the C++ backend already includes all but stdexcept/the
    /// unordered_* headers — added under the usesCollections gate).
    /// </summary>
    public static class CppCollectionsRuntime
    {
        public const string Source = @"namespace BasicLang {

template <typename T>
class List {
    std::vector<T> _v;
public:
    List() = default;
    void Add(const T& item) { _v.push_back(item); }
    int32_t Count() const { return static_cast<int32_t>(_v.size()); }
    T& operator[](int32_t i) { return _v.at(static_cast<size_t>(i)); }
    const T& operator[](int32_t i) const { return _v.at(static_cast<size_t>(i)); }
    bool Contains(const T& item) const { return std::find(_v.begin(), _v.end(), item) != _v.end(); }
    int32_t IndexOf(const T& item) const {
        auto it = std::find(_v.begin(), _v.end(), item);
        return it == _v.end() ? -1 : static_cast<int32_t>(it - _v.begin());
    }
    void Remove(const T& item) {
        auto it = std::find(_v.begin(), _v.end(), item);
        if (it != _v.end()) _v.erase(it);
    }
    void RemoveAt(int32_t i) { _v.erase(_v.begin() + i); }
    void Insert(int32_t i, const T& item) { _v.insert(_v.begin() + i, item); }
    void Clear() { _v.clear(); }
    typename std::vector<T>::iterator begin() { return _v.begin(); }
    typename std::vector<T>::iterator end() { return _v.end(); }
    typename std::vector<T>::const_iterator begin() const { return _v.begin(); }
    typename std::vector<T>::const_iterator end() const { return _v.end(); }
};

template <typename K, typename V>
class Dictionary {
    std::unordered_map<K, V> _m;
public:
    Dictionary() = default;
    void Add(const K& key, const V& value) {
        if (_m.count(key)) throw std::runtime_error(""An item with the same key has already been added."");
        _m[key] = value;
    }
    V Get(const K& key) const {
        auto it = _m.find(key);
        if (it == _m.end()) throw std::runtime_error(""The given key was not present in the dictionary."");
        return it->second;
    }
    void Set(const K& key, const V& value) { _m[key] = value; }
    bool ContainsKey(const K& key) const { return _m.count(key) > 0; }
    bool TryGetValue(const K& key, V& value) const {
        auto it = _m.find(key);
        if (it == _m.end()) return false;
        value = it->second;
        return true;
    }
    List<K> Keys() const { List<K> ks; for (const auto& kv : _m) ks.Add(kv.first); return ks; }
    List<V> Values() const { List<V> vs; for (const auto& kv : _m) vs.Add(kv.second); return vs; }
    bool Remove(const K& key) { return _m.erase(key) > 0; }
    int32_t Count() const { return static_cast<int32_t>(_m.size()); }
    void Clear() { _m.clear(); }
};

template <typename T>
class HashSet {
    std::unordered_set<T> _s;
public:
    HashSet() = default;
    bool Add(const T& item) { return _s.insert(item).second; }
    bool Contains(const T& item) const { return _s.count(item) > 0; }
    bool Remove(const T& item) { return _s.erase(item) > 0; }
    int32_t Count() const { return static_cast<int32_t>(_s.size()); }
    void Clear() { _s.clear(); }
    typename std::unordered_set<T>::iterator begin() { return _s.begin(); }
    typename std::unordered_set<T>::iterator end() { return _s.end(); }
    typename std::unordered_set<T>::const_iterator begin() const { return _s.begin(); }
    typename std::unordered_set<T>::const_iterator end() const { return _s.end(); }
};

}
";
    }
}
```

Note: `""` inside the C# verbatim string produces a single `"` in the C++ output (needed for the exception messages).

- [ ] **Step 2: Write the failing isolated runtime test**

Create `CppCollectionsRuntimeTests.cs`. It builds a small C++ program (the wrapper `Source` + includes + a `main` exercising the semantics), compiles it to an **executable**, runs it, and asserts stdout. Reuse the compiler-probe pattern from `CppBackendTests.FindCppCompiler`, but a **compile-and-run** variant (the existing one is syntax-only). Skip cleanly when no compiler is present.

```csharp
using System.Diagnostics;
using NUnit.Framework;
using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace VisualGameStudio.Tests.Native;

[TestFixture]
public class CppCollectionsRuntimeTests
{
    private const string TestMain = @"
#include <iostream>
#include <vector>
#include <unordered_map>
#include <unordered_set>
#include <algorithm>
#include <stdexcept>
#include <cstdint>
#include <string>
using namespace std;
" + "\n" + CppCollectionsRuntime.Source + @"
int main() {
    BasicLang::List<int32_t> l;
    l.Add(10); l.Add(20); l.Add(30);
    cout << l.Count() << "" "" << l[1] << "" "" << (l.Contains(20) ? 1 : 0) << ""\n"";

    BasicLang::Dictionary<std::string, int32_t> d;
    d.Add(""a"", 1); d.Set(""b"", 2);
    int32_t got = 0; bool ok = d.TryGetValue(""b"", got);
    cout << d.Count() << "" "" << (d.ContainsKey(""a"") ? 1 : 0) << "" "" << ok << "" "" << got << "" "" << d.Get(""a"") << ""\n"";

    bool threw = false;
    try { d.Get(""missing""); } catch (const std::runtime_error&) { threw = true; }
    cout << threw << ""\n"";

    BasicLang::HashSet<int32_t> s;
    cout << s.Add(5) << "" "" << s.Add(5) << "" "" << (s.Contains(5) ? 1 : 0) << "" "" << s.Count() << ""\n"";
    return 0;
}
";

    [Test]
    public void CollectionsRuntime_Semantics_MatchDotNet()
    {
        var compiler = CppCompile.FindRunCompiler();
        if (compiler == null)
            Assert.Ignore("No C++ compiler available (clang++/g++/MSVC)");

        var stdout = CppCompile.CompileAndRun(TestMain, compiler.Value);
        Assert.That(stdout.Replace("\r\n", "\n"), Is.EqualTo(
            "3 20 1\n" +      // List: Count, [1], Contains(20)
            "2 1 1 2 1\n" +   // Dictionary: Count, ContainsKey(a), TryGetValue ok, got, Get(a)
            "1\n" +           // Get(missing) threw
            "1 0 1 1\n"));    // HashSet: Add(5)=true, Add(5)=false, Contains(5), Count
    }
}
```

- [ ] **Step 3: Add the shared compile-and-run helper**

Create `VisualGameStudio.Tests/Native/CppCompile.cs` — a reusable helper (the existing `FindCppCompiler` in `CppBackendTests` is private and syntax-only). `FindRunCompiler()` returns `(exe, compileArgsTemplate)` that produces an **exe** (drop `-fsyntax-only`/`/Zs`, add `-o {1}` / `/Fe:{1}`); `CompileAndRun(src, compiler)` writes a temp `.cpp`, compiles to a temp exe, runs it, returns stdout, asserts exit code 0, cleans up. Model the probe on `CppBackendTests.cs:665-706` (clang++/g++ on PATH, then MSVC via vswhere+vcvars64). For MSVC use `cl /nologo /std:c++20 /EHsc /Fe:{1} {0}`; for clang/g++ use `-std=c++20 {0} -o {1}`.

- [ ] **Step 4: Run the test to verify it passes (or skips cleanly)**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~CppCollectionsRuntimeTests"`
Expected: PASS if a C++ compiler is installed; otherwise a single Ignored test (not a failure). Fix wrapper C++ until stdout matches exactly.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release`
Expected: all green (new test passes or is ignored).

- [ ] **Step 6: Commit**

```
git add BasicLang/Compiler/CodeGen/CPlusPlus/CppCollectionsRuntime.cs VisualGameStudio.Tests/Native/
git commit -F <msgfile>   # feat(cpp): BasicLang::List/Dictionary/HashSet runtime wrappers + isolated C++ tests
```

---

## Task 2: `TypeKind.Foreign`, capability relaxation, type mapping, preamble emission

Make the compiler *accept* collections and emit the wrapper preamble. No member lowering yet — that's Task 3. After this task, `Dim l As List(Of Integer)` and `New List(Of Integer)()` compile to valid C++ that declares a `BasicLang::List<int32_t>`.

**Files:**
- Modify: `BasicLang/SymbolTable.cs:161`
- Modify: `BasicLang/CppCapabilityChecker.cs`
- Modify: `BasicLang/CppCodeGenerator.cs` (`MapType` 434-488, `Visit(IRNewObject)` 2086-2132, `GenerateHeader` 221-255, `EmitRuntimePreamble` 302-359)
- Test: `VisualGameStudio.Tests/Compiler/CppCollectionTests.cs`

- [ ] **Step 1: Add `Foreign` to `TypeKind`**

`SymbolTable.cs:161-177` — change `        Void` to `        Void,` and append `        Foreign      // ::-qualified opaque C++ passthrough type`.

- [ ] **Step 2: Failing test — capability checker allows collections**

Create `CppCollectionTests.cs` with a helper `CompileToCpp` copied from `CppBackendTests.cs:24-57` (same pipeline; `CppCodeGenOptions { GenerateComments = false }`). First test:

```csharp
[Test]
public void Cpp_ListLocal_MapsToBasicLangListValue()
{
    var source = @"
Sub Main()
    Dim numbers As New List(Of Integer)()
End Sub";
    var output = CompileToCpp(source, out var errors);
    Assert.That(errors, Is.Empty, string.Join("; ", errors));
    Assert.That(output, Does.Contain("BasicLang::List<int32_t>"));
    Assert.That(output, Does.Not.Contain("std::make_shared<List"));   // must be a value, not shared_ptr
    Assert.That(output, Does.Not.Contain("std::shared_ptr<BasicLang::List"));
}
```

Run: `dotnet test … --filter "FullyQualifiedName~Cpp_ListLocal_MapsToBasicLangListValue"`
Expected: FAIL — today `CppCapabilityChecker` throws `CppCapabilityException` on `List` (`.NET type 'List' … no C++ mapping`).

- [ ] **Step 3: Relax the capability checker**

`CppCapabilityChecker.cs` — in `CheckType` (line 106-143), before the final class-kind rejection (line 138), allow the collections and foreign types. Add after the `Func`/`Action` guard (line 130):

```csharp
if (name.Equals("List", StringComparison.OrdinalIgnoreCase)
    || name.Equals("Dictionary", StringComparison.OrdinalIgnoreCase)
    || name.Equals("HashSet", StringComparison.OrdinalIgnoreCase))
    return; // BasicLang::List/Dictionary/HashSet wrappers; generic args already recursed above
if (name.Contains("::"))
    return; // ::-qualified C++ foreign type (opaque passthrough)
```

(Generic args are already recursed at lines 119-121, so `List(Of DateTime)` still correctly rejects `DateTime`.)

- [ ] **Step 4: Map the collection types (value, no shared_ptr)**

`CppCodeGenerator.MapType` (434-488) — insert AFTER the array/pointer guard (line 439) and BEFORE the `string bare;` block (line 468), mirroring the `IEnumerable` branch (each returns directly, bypassing the shared_ptr wrap at 485):

```csharp
// Foreign C++ passthrough type (::-qualified) — emit verbatim, value semantics.
if (type.Name != null && type.Name.Contains("::"))
{
    if (type.GenericArguments != null && type.GenericArguments.Count > 0)
        return $"{type.Name}<{string.Join(", ", type.GenericArguments.Select(MapType))}>";
    return type.Name;
}
// Everyday collections -> BasicLang wrappers (value types, never shared_ptr).
if (type.Name == "List" && type.GenericArguments?.Count > 0)
    return $"BasicLang::List<{MapType(type.GenericArguments[0])}>";
if (type.Name == "Dictionary" && type.GenericArguments?.Count > 1)
    return $"BasicLang::Dictionary<{MapType(type.GenericArguments[0])}, {MapType(type.GenericArguments[1])}>";
if (type.Name == "HashSet" && type.GenericArguments?.Count > 0)
    return $"BasicLang::HashSet<{MapType(type.GenericArguments[0])}>";
```

- [ ] **Step 5: Value-init in `Visit(IRNewObject)`**

`CppCodeGenerator.cs` `Visit(IRNewObject)` (2086-2132) — after the `String` special-case (line 2103) and before the generic `bareName` block (line 2109), add a collections branch so `New List(Of Integer)()` emits a value init, not `make_shared`:

```csharp
if (newObj.ClassName == "List" || newObj.ClassName == "Dictionary" || newObj.ClassName == "HashSet")
{
    var cppType = MapType(newObj.Type);   // BasicLang::List<int32_t> etc.
    var ctorArgs = string.Join(", ", newObj.Arguments.Select(a => GetValueName(a)));
    WriteLine($"{GetValueName(newObj)} = {cppType}({ctorArgs});");
    return;
}
```

- [ ] **Step 6: Run Step 2's test — expect PASS.** Then add a Dictionary + HashSet variant test (assert `BasicLang::Dictionary<std::string, int32_t>` and `BasicLang::HashSet<int32_t>`, no shared_ptr). Implement until green.

- [ ] **Step 7: Failing test — the wrapper preamble is emitted when collections are used, gated off when not**

```csharp
[Test]
public void Cpp_UsesCollections_EmitsWrapperPreamble()
{
    var output = CompileToCpp("Sub Main()\n Dim l As New List(Of Integer)()\nEnd Sub", out var e);
    Assert.That(output, Does.Contain("template <typename T>\nclass List"));   // preamble present
    Assert.That(output, Does.Contain("#include <unordered_map>"));
}

[Test]
public void Cpp_NoCollections_OmitsWrapperPreamble()
{
    var output = CompileToCpp("Sub Main()\n Dim x As Integer = 1\nEnd Sub", out var e);
    Assert.That(output, Does.Not.Contain("class List"));
}
```

- [ ] **Step 8: Add the `usesCollections` gate + includes + preamble emission**

`CppCodeGenerator.cs`:
1. Add a detector (collections appear in locals/params/fields/returns, not function flags — a data-flow scan is required). Add a private helper:

```csharp
private static bool ModuleUsesCollections(IRModule module)
{
    bool IsColl(TypeInfo t)
    {
        if (t == null) return false;
        if (t.Name == "List" || t.Name == "Dictionary" || t.Name == "HashSet") return true;
        if (t.GenericArguments != null && t.GenericArguments.Any(IsColl)) return true;
        return IsColl(t.ElementType);
    }
    foreach (var f in module.Functions)
    {
        if (IsColl(f.ReturnType)) return true;
        if (f.Parameters.Any(p => IsColl(p.Type))) return true;
        if (f.LocalVariables.Any(lv => IsColl(lv.Type))) return true;
    }
    foreach (var c in module.Classes.Values)
        if (c.Fields.Any(fld => IsColl(fld.Type))) return true;
    return false;
}
```

(Confirm the exact field/property member names on `IRFunction`/`IRClass` while implementing — `LocalVariables`, `Parameters`, `ReturnType`, `Classes`, `Fields` are used elsewhere in this file; match them.)

2. In `GenerateHeader` (221-255): compute `var usesCollections = ModuleUsesCollections(module);`. Under it, add includes:

```csharp
if (usesCollections)
{
    includes.Add("unordered_map");
    includes.Add("unordered_set");
    includes.Add("stdexcept");
    // vector, algorithm, cstdint already in the base set
}
```

Change the call at line 253-254 to `if (hasAsync || hasIterators || usesCollections) EmitRuntimePreamble(hasAsync, hasIterators, usesCollections);`.

3. Change `EmitRuntimePreamble` signature to `(bool hasAsync, bool hasIterators, bool usesCollections)`. The Task/Generator types live in `namespace BasicLang { … }` (lines 304-357). Emit the collections **outside** that block to avoid double-nesting, since `CppCollectionsRuntime.Source` opens its own `namespace BasicLang { … }`. Simplest: after the existing namespace close (line 357), add:

```csharp
if (usesCollections)
{
    foreach (var line in CppCollectionsRuntime.Source.Split('\n'))
        WriteLine(line.TrimEnd('\r'));
}
```

(Emitting at indent level 0 is fine — the wrapper source has no leading indentation expectations.)

- [ ] **Step 9: Run Step 7 tests — expect PASS.** Run the full suite. Commit.

```
git commit -F <msgfile>   # feat(cpp): accept List/Dictionary/HashSet — TypeKind.Foreign, capability relax, type map, wrapper preamble
```

---

## Task 3: Collection member/indexer/property/foreach lowering + semantics + E2E

Make collection *operations* generate correct C++, then prove it compiles-and-runs, and prove the same source runs on C#.

**Files:**
- Modify: `BasicLang/SemanticAnalyzer.cs` (`GetCommonMethodReturnType` 1996-2034)
- Modify: `BasicLang/CppCodeGenerator.cs` (`MemberAccessOp` 575-583, `Visit(IRFieldAccess)` 2229-2264, `Visit(IRIndexerAccess)` 2407-2414, the indexed-store visitor — found in Step 1)
- Test: `VisualGameStudio.Tests/Compiler/CppCollectionTests.cs`

- [ ] **Step 1 (INVESTIGATION SPIKE — do first, ~10 min): pin two IR shapes.** The spec (decision 6) flagged these as unknowns:
  1. **How does `list.Count` arrive in IR?** Add a throwaway test that compiles `Dim n As Integer = l.Count` and dumps the generated C++ (or set a breakpoint). Determine whether `.Count` is an `IRFieldAccess` (→ needs a `()` bridge) or an `IRInstanceMethodCall` with no args (→ raw passthrough already emits `Count()`, no bridge needed). Steps 4-5 branch on this.
  2. **How does `dict(k) = v` (indexed write) lower?** Compile `d(""x"") = 1` and find which visitor emits it (grep `CppCodeGenerator.cs` for the store node used by indexer LHS — candidates: an `IRIndexerStore`/`IRArrayStore`/an `IRIndexerAccess` consumed by an assignment). Record the node name; Step 6 targets it.
  Write findings as a comment at the top of `CppCollectionTests.cs`.

- [ ] **Step 2: MemberAccessOp guard (collections + foreign use `.`)**

`CppCodeGenerator.MemberAccessOp` (575-583) — collections and foreign types are values, so they must use `.` not `->`. Add before the Class/Interface check:

```csharp
var tn = obj?.Type?.Name;
if (tn != null && (tn == "List" || tn == "Dictionary" || tn == "HashSet" || tn.Contains("::")))
    return ".";
```

- [ ] **Step 3: Failing test — method passthrough + Count + iterate, compile-and-run**

```csharp
[Test]
public void Cpp_ListOperations_CompileAndRun()
{
    var source = @"
Sub Main()
    Dim l As New List(Of Integer)()
    l.Add(10)
    l.Add(20)
    l.Add(30)
    Dim total As Integer = 0
    For Each n In l
        total = total + n
    Next
    Console.WriteLine(total)
    Console.WriteLine(l.Count)
    Console.WriteLine(l(1))
End Sub";
    var output = CompileToCpp(source, out var errors);
    Assert.That(errors, Is.Empty, string.Join("; ", errors));
    var compiler = CppCompile.FindRunCompiler();
    if (compiler == null) Assert.Ignore("No C++ compiler");
    var stdout = CppCompile.CompileAndRun(output, compiler.Value).Replace("\r\n", "\n");
    Assert.That(stdout, Is.EqualTo("60\n3\n20\n"));
}
```

Run it: expect FAIL (either a semantic error on `.Count`/`.Add` return types, or wrong C++ for `.Count`).

- [ ] **Step 4: Extend semantic return types for collection members**

`SemanticAnalyzer.GetCommonMethodReturnType` (1996-2034) — add return types so typed assignments don't degrade to `Object`. Add cases for List/Dictionary/HashSet members: `Add`→Void, `Count`→Integer, `Contains`/`ContainsKey`/`TryGetValue`/`Remove`(HashSet/Dict bool)→Boolean, `IndexOf`→Integer, `Keys`/`Values`→(List of the key/value type), `Clear`/`Insert`/`RemoveAt`→Void. Match the existing switch style in that method (it already handles `Count`/`Contains`/`Any`/`All`). For `Keys`/`Values` returning `List(Of …)`, construct a `TypeInfo("List", TypeKind.Class)` with the appropriate generic arg pulled from the receiver's `GenericArguments`.

- [ ] **Step 5: `.Count`/`.Keys`/`.Values` property bridge (only if Step 1 found `IRFieldAccess`)**

If Step 1.1 showed `.Count` is an `IRFieldAccess`: in `Visit(IRFieldAccess)` (2229-2264), before the raw fallthrough (line 2260), add a collection-property branch:

```csharp
if (fieldAccess.Object?.Type?.Name is "List" or "Dictionary" or "HashSet"
    && fieldAccess.FieldName is "Count" or "Keys" or "Values")
{
    var recv = GetValueName(fieldAccess.Object);
    WriteLine($"{result} = {recv}.{SanitizeName(fieldAccess.FieldName)}();");
    return;
}
```

If Step 1.1 showed it's an `IRInstanceMethodCall`, no bridge is needed — raw passthrough already emits `.Count()`. Note which path was taken in the test-file comment.

- [ ] **Step 6: Dictionary indexer read/write (Get/Set), List indexer via operator[]**

`Visit(IRIndexerAccess)` (2407-2414, READ) — Dictionary reads must call `.Get(k)` (throws on missing, .NET-faithful); List reads keep `[i]`:

```csharp
if (indexer.Collection?.Type?.Name == "Dictionary")
    WriteLine($"{result} = {collection}.Get({indices});");
else
    WriteLine($"{result} = {collection}[{indices}];");
```

For the indexed **write** node found in Step 1.2 — Dictionary writes call `.Set(k, v)`, List/array writes keep `[i] = v`. Add the analogous branch in that visitor. If Step 1.2 finds indexed writes are not distinguishable from array writes cleanly, fall back to `operator[]` for Dictionary writes too (define `V& operator[](const K&)` on the wrapper) and document the single deviation (write-inserts is already .NET-correct; only read-throw is the strict requirement, already met by `.Get`).

- [ ] **Step 7: Run the collection ops test — expect PASS.** Add Dictionary + HashSet compile-and-run tests (Add/ContainsKey/TryGetValue/Keys iteration; HashSet Add-returns-bool/Contains/Count). Implement until green.

- [ ] **Step 8: C# portability test (same source runs on C#)**

Decision: the test project has **no Roslyn** (`VisualGameStudio.Tests.csproj`). Add `<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.9.2" />` to the test csproj and write an in-memory compile+run helper `CSharpRun.CompileAndRun(generatedCSharp)` (Roslyn `CSharpCompilation.Create` → `Emit` to a `MemoryStream` → `Assembly.Load` → invoke entry point with `Console.SetOut` redirected to a `StringWriter`). Then:

```csharp
[Test]
public void Portability_SameCollectionSource_RunsOnCSharpAndCpp()
{
    var source = /* the List ops source from Step 3 */;
    // C# path
    var cs = CompileToCSharp(source, out var csErrors);   // helper copied from CompilationTests.cs:21
    Assert.That(csErrors, Is.Empty);
    Assert.That(CSharpRun.CompileAndRun(cs).Replace("\r\n","\n"), Is.EqualTo("60\n3\n20\n"));
    // C++ path asserted in Cpp_ListOperations_CompileAndRun — same expected output proves portability.
}
```

If adding Roslyn is undesirable at execution time, fall back to a **string-assert** that the C# output uses real `.NET` `List<int>`/`Console.WriteLine` (consistent with the existing `CompilationTests` string-only pattern) and note the reduced strength in the test comment.

- [ ] **Step 9: Add the collections E2E to the existing structural E2E gate** — extend `CppBackendTests.Cpp_EndToEnd_GeneratedCodeIsValidCpp`'s source (or add a sibling) to include a List/Dictionary/HashSet snippet so the representative program keeps covering collections. Run the full suite. Commit.

```
git commit -F <msgfile>   # feat(cpp): lower List/Dictionary/HashSet operations + .NET-faithful semantics + E2E
```

---

## Task 4: `#CppInclude` directive

Let users pull in C++ headers. Distinct from the existing `#Include` source-splicer (which owns both `<…>` and `"…"`).

**Files:**
- Modify: `BasicLang/Preprocessor.cs` (fields 14-20, dispatch 79-134)
- Modify: `BasicLang/Compiler.cs` (313-326)
- Modify: `BasicLang/CppCodeGenerator.cs` (ctor 35-44, `GenerateHeader` include loop 242-245, `CppCodeGenOptions` 2583-2589)
- Test: `VisualGameStudio.Tests/Compiler/CppPassthroughTests.cs`, `ForeignFeatureGuardTests.cs`

- [ ] **Step 1: Failing test — `#CppInclude` reaches the generated C++; `#Include` is untouched**

```csharp
[Test]
public void Cpp_CppInclude_EmitsRealInclude()
{
    var source = "#CppInclude <mutex>\nSub Main()\nEnd Sub";
    var output = CompileToCpp(source, out var errors);
    Assert.That(errors, Is.Empty, string.Join("; ", errors));
    Assert.That(output, Does.Contain("#include <mutex>"));
}
```

Run: expect FAIL — `#CppInclude` is an unknown directive today (the preprocessor's trailing `else` comments it out or passes it through, and it never reaches includes).

- [ ] **Step 2: Preprocessor — collect `#CppInclude` headers**

`Preprocessor.cs`: add field (near line 18) `private readonly List<string> _cppIncludes = new List<string>();` and accessor (near line 20) `public IReadOnlyList<string> CppIncludes => _cppIncludes;`. Do NOT clear it in `Process` (it recurses for nested `#Include`).

In the dispatch chain (79-134), add a branch AFTER `#EndIf` (after line 121) and BEFORE the trailing `else` (line 122):

```csharp
else if (trimmedLine.StartsWith("#CppInclude", StringComparison.OrdinalIgnoreCase))
{
    var angle = System.Text.RegularExpressions.Regex.Match(trimmedLine, @"#CppInclude\s+<([^>]+)>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    var quote = System.Text.RegularExpressions.Regex.Match(trimmedLine, "#CppInclude\\s+\"([^\"]+)\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    if (angle.Success)      _cppIncludes.Add("<" + angle.Groups[1].Value + ">");
    else if (quote.Success) _cppIncludes.Add("\"" + quote.Groups[1].Value + "\"");
    else _errors.Add(new PreprocessorError { Line = lineNumber, Message = $"Invalid #CppInclude syntax: {trimmedLine}" });
    result.AppendLine($"' {line}");   // comment the directive out of BasicLang source
}
```

(Store the full delimited token so angle vs quoted survives to emission.)

- [ ] **Step 3: `CppCodeGenOptions.CppIncludes` + generator plumbing**

`CppCodeGenerator.cs`: add `public List<string> CppIncludes { get; set; } = new List<string>();` to `CppCodeGenOptions` (2583-2589). Add field `private readonly List<string> _cppIncludeTokens;` and seed it in the ctor (35-44): `_cppIncludeTokens = new List<string>(_options.CppIncludes);`. In `GenerateHeader` after the angle-only loop (242-245), add:

```csharp
foreach (var tok in _cppIncludeTokens)
    WriteLine($"#include {tok}");   // tok is already <...> or "..."
```

- [ ] **Step 4: Compiler threads preprocessor headers into options**

`Compiler.cs`: after the `_preprocessor.Process` / error-read block (313-326), accumulate `_preprocessor.CppIncludes` into a compile-scoped list, and where the `CppCodeGenerator`/`CppCodeGenOptions` is constructed for a C++ build, set `options.CppIncludes = collectedCppHeaders`. (Find the C++ construction site — `BackendRegistry.cs`, `MultiTargetCompiler.cs`, or `Program.cs` per the anchor notes — and pass the list through. If the generator is built in a registry without compiler state, add the headers to the `IRModule` as a new `List<string> CppIncludes` and read them in the generator instead; pick whichever site already has both the module and options.)

- [ ] **Step 5: Run Step 1 test — expect PASS.** Add a quoted-header test (`#CppInclude "grid.h"` → `#include "grid.h"`). Implement until green.

- [ ] **Step 6: Commit.**

```
git commit -F <msgfile>   # feat(cpp): #CppInclude directive emits real C++ #include (distinct from #Include splicer)
```

---

## Task 5: Foreign `::` types (opaque passthrough)

`Dim m As std::mutex` + `m.lock()`. Parsing, semantic Foreign recognition, opaque member access, value emission.

**Files:**
- Verify/Modify: `BasicLang/Lexer`/`Parser`/`ASTNodes.cs` (Step 1)
- Modify: `BasicLang/SemanticAnalyzer.cs` (`ResolveTypeName` 1756-1792, `Visit(MemberAccessExpressionNode)` ~5101-5133)
- (MapType foreign branch + MemberAccessOp guard already added in Tasks 2-3)
- Test: `VisualGameStudio.Tests/Compiler/CppPassthroughTests.cs`

- [ ] **Step 1 (SPIKE): can the lexer/parser carry `::` in a type name?** Add a failing test that compiles `Dim m As std::mutex` and inspect the error. If the parser already yields a `TypeReferenceNode` with `Name == "std::mutex"`, no grammar change is needed (proceed to Step 2). If `::` breaks tokenization, extend the type-name parse to accept `Identifier ("::" Identifier)+` (and a leading `::`). Record which in the test comment.

- [ ] **Step 2: Failing test — foreign type + opaque member, compile-and-run**

```csharp
[Test]
public void Cpp_ForeignType_OpaquePassthrough_CompilesAndRuns()
{
    var source = @"
#CppInclude <mutex>
Sub Main()
    Dim m As std::mutex
    m.lock()
    m.unlock()
    Console.WriteLine(""ok"")
End Sub";
    var output = CompileToCpp(source, out var errors);
    Assert.That(errors, Is.Empty, string.Join("; ", errors));
    Assert.That(output, Does.Contain("std::mutex m"));    // value, not shared_ptr
    Assert.That(output, Does.Contain("m.lock()"));         // opaque passthrough with '.'
    var compiler = CppCompile.FindRunCompiler();
    if (compiler == null) Assert.Ignore("No C++ compiler");
    Assert.That(CppCompile.CompileAndRun(output, compiler.Value).Replace("\r\n","\n"), Is.EqualTo("ok\n"));
}
```

Run: expect FAIL — semantic analysis errors on the unknown type `std::mutex` and/or member `lock`.

- [ ] **Step 3: `ResolveTypeName` — `::` → `TypeKind.Foreign`**

`SemanticAnalyzer.ResolveTypeName` (1756-1792) — as the FIRST statement:

```csharp
if (name != null && name.Contains("::"))
    return new TypeInfo(name, TypeKind.Foreign);
```

- [ ] **Step 4: Opaque member access on Foreign receivers**

`SemanticAnalyzer.Visit(MemberAccessExpressionNode)` — after the `objectType.Members.TryGetValue` block and BEFORE the `else if (IsNetType(...))` branch (~line 5107):

```csharp
else if (objectType.Kind == TypeKind.Foreign)
{
    SetNodeType(node, new TypeInfo(objectType.Name + "::" + node.MemberName, TypeKind.Foreign));
}
```

This short-circuits the "does not have a member" error and keeps chained access (`a.b.c`) opaque. (The `MapType` foreign branch from Task 2 Step 4 and the `MemberAccessOp` `.` guard from Task 3 Step 2 already handle emission.)

- [ ] **Step 5: Run Step 2 test — expect PASS.** Add a foreign-template test: `Dim q As std::deque(Of Integer)` (needs `#CppInclude <deque>`) → asserts `std::deque<int32_t>`. Implement/verify until green. Run full suite. Commit.

```
git commit -F <msgfile>   # feat(cpp): :: foreign types — opaque passthrough, value semantics, (Of ...) templates
```

---

## Task 6: Cross-backend clean errors + docs + IDE binaries

Non-C++ backends must reject `#CppInclude`/foreign types cleanly (never silent garbage), and the existing `#Include` splicer must keep working.

**Files:**
- Create: `BasicLang/Compiler/CodeGen/ForeignFeatureChecker.cs`
- Modify: `BasicLang/CSharpBackend.cs` (Generate 153), `BasicLang/LLVMBackend.cs` (Generate 110), `BasicLang/MSILBackend.cs` (Generate 70)
- Modify: `BasicLang/IRNodes.cs` (add `List<string> CppIncludes` to `IRModule` if not already added in Task 4)
- Test: `VisualGameStudio.Tests/Compiler/ForeignFeatureGuardTests.cs`
- Docs: `docs/BasicLang-Reference.md`

- [ ] **Step 1: Failing tests — non-C++ backends reject; `#Include` still splices**

```csharp
[Test]
public void CSharp_ForeignType_ThrowsCleanError()
{
    var source = "Sub Main()\n Dim m As std::mutex\nEnd Sub";
    Assert.Throws<ForeignFeatureException>(() => CompileToCSharp(source, out _));
}

[Test]
public void CSharp_CppInclude_ThrowsCleanError()
{
    var source = "#CppInclude <mutex>\nSub Main()\nEnd Sub";
    Assert.Throws<ForeignFeatureException>(() => CompileToCSharp(source, out _));
}

[Test]
public void Include_SourceSplicing_StillWorks()
{
    // regression: plain #Include of a BasicLang file still textually includes it (unchanged behavior)
    // (build two temp .bas files; assert the included symbol resolves / output contains spliced code)
}
```

(Add LLVM/MSIL variants using their generators.) Run: expect FAIL — no guard exists yet.

- [ ] **Step 2: Shared `ForeignFeatureChecker` + exception**

Create `ForeignFeatureChecker.cs` mirroring `CppCapabilityChecker`/`CppCapabilityException` (CppCapabilityChecker.cs:29-38):

```csharp
public class ForeignFeatureException : Exception
{
    public ForeignFeatureException(string message)
        : base("This backend cannot compile C++ passthrough features: " + message) { }
}

public static class ForeignFeatureChecker
{
    // Throws if the module uses #CppInclude headers or any ::-qualified (Foreign) type.
    public static void Check(IRModule module, string backendName)
    {
        if (module.CppIncludes != null && module.CppIncludes.Count > 0)
            throw new ForeignFeatureException($"#CppInclude is only supported by the C++ backend (target: {backendName}).");
        // Walk function/param/local/field types for TypeKind.Foreign (reuse the same
        // recursion shape as CppCapabilityChecker.CheckType / ModuleUsesCollections).
        // On the first Foreign type, throw:
        //   throw new ForeignFeatureException($"'{name}' is a C++ type usable only on the C++ backend (target: {backendName}).");
    }
}
```

(Requires `IRModule.CppIncludes` — add it in `IRNodes.cs` `IRModule` (1107-1139), init in ctor like `NetUsings`, if Task 4 routed headers via options rather than the module. If Task 4 used the module route, it already exists.)

- [ ] **Step 3: Call the guard from the three non-C++ `Generate` entries**

- `CSharpBackend.cs` line ~155 (after `_currentModule = module;`): `ForeignFeatureChecker.Check(module, "C#");`
- `LLVMBackend.cs` line ~112 (after `_module = module;`): `ForeignFeatureChecker.Check(module, "LLVM");`
- `MSILBackend.cs` line ~72 (after `_module = module;`): `ForeignFeatureChecker.Check(module, "MSIL");`

- [ ] **Step 4: Run Step 1 tests — expect PASS.** Run the full suite. Commit.

```
git commit -F <msgfile>   # feat: clean cross-backend errors for #CppInclude / :: foreign types (C#/LLVM/MSIL)
```

- [ ] **Step 5: Docs — document both layers**

`docs/BasicLang-Reference.md` — add a "C++ backend standard library" section: the portable collections (`List`/`Dictionary`/`HashSet`, member surface, `.NET`-faithful semantics), and the passthrough layer (`#CppInclude`, `::` foreign types, `(Of …)` templates, the "you get C++'s errors past the include" contract, and that it's C++-backend-only). Note `Extern`/inline as the third passthrough path.

- [ ] **Step 6: Refresh IDE binaries** — per project convention, rebuild and copy the compiler outputs the IDE ships (`dotnet build BasicLang/BasicLang.csproj -c Release`, then update `IDE/` binaries as prior "refresh IDE binaries" commits did — check the last such commit for the exact file set). Commit.

```
git commit -F <msgfile>   # chore: docs + refresh IDE binaries (collections + C++ passthrough)
```

---

## Definition of Done

- `List(Of T)`/`Dictionary(Of K,V)`/`HashSet(Of T)` compile-and-run on the C++ backend with `.NET`-faithful semantics (missing-key read throws; duplicate `Add` throws).
- The identical collection source compiles+runs on the C# backend (portability proven).
- `#CppInclude <hdr>` / `#CppInclude "hdr"` emit real `#include`s; `Dim x As std::type` + opaque `x.member()` compiles-and-runs; `(Of …)` templates map to `<…>`.
- C#/LLVM/MSIL throw a clean `ForeignFeatureException` on `#CppInclude`/foreign types; plain `#Include` source-splicing still works.
- Full `dotnet test` suite green after every task; docs updated; IDE binaries refreshed.

## Out of scope (per spec)

Queue/Stack/LinkedList/SortedDictionary; LINQ operators beyond current; `For Each` directly over a `Dictionary` (use `.Keys`/`.Values`); LLVM/MSIL collection/passthrough *support* (they error in v1); non-opaque foreign-type checking; expanding `DateTime`/other .NET scalar surface.
