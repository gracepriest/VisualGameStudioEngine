# .NET ⇄ Native Boundary Contract (v1) Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the boundary-contract spec (`docs/superpowers/specs/2026-07-26-dotnet-native-boundary-contract-design.md`): the type-ownership registry, the ABI constants/headers, the native-side runtime (handle RAII, callback table, queue, pump), a minimal Native-AOT test shim, and the 16-test conformance suite that gates P2.

**Architecture:** One C# source of truth (`BlnetContract` + `BoundaryTypeRegistry`) feeds everything: the capability checker/type mapper (replacing three hand-synced lists), the generated status sections of two checked-in C++ headers (drift-tested, following the existing `CppRuntimeSources.cs` single-source pattern), and the C# shim enum. The native side of the contract (NetRef, callback table, queue, pump, universal thunk) is header-only C++ stored as string constants in `BlnetRuntimeSources.cs` — P2 later emits the same strings. Conformance runs a hand-written AOT shim (`NativeLib=Shared` + `[UnmanagedCallersOnly]`) against a native harness compiled via the existing `CppCompile` test helper.

**Tech Stack:** C# (net8.0 for the shim — it MUST match VisualGameStudio.Tests' net8.0 so the ProjectReference resolves (NU1201 otherwise); Native AOT `NativeLib=Shared` is fully supported on net8.0 and SDK 9.0.309 publishes it), C++20, NUnit, existing `CppCompile.FindRunCompiler()` probe (clang++/g++/MSVC-vcvars).

**Read first:** the spec (path above) — sections C1–C7 define every rule implemented here. Skills: @superpowers:test-driven-development, @superpowers:verification-before-completion.

**Conventions that prevent real mistakes in this repo:**
- Never round-trip repo files through PowerShell `Get-Content`/`Set-Content` (mojibake). Use Read/Edit/Write tools.
- Run tests with output redirected to a file (`> test-run.txt 2>&1`) — the suite exceeds tool output truncation.
- Fast subset: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "TestCategory!=Integration"`.
- Commit messages here are single-line; use `git commit -m "..."` directly.

**Mechanism details this plan adds beyond the spec** (the spec's rules require them; flagged for transparency):
1. **Slot descriptors at callback registration.** The spec fixes a callback's shape at registration; queued dispatch must deep-copy arguments, so registration takes a per-slot descriptor (`BlnetSlotDesc {kind, size}`) telling the runtime which slots are values/strings/structs/handles.
2. **Initialization handshake.** The shim export `blnet_initialize(expected_abi_version, const BlnetNativeVtable*)` performs the C7 version fail-fast and gives the managed side the native thunk + native-error accessor as function pointers.
3. **Same-thread detection.** A thread is "the thread that entered managed code" iff a native-side `thread_local` depth counter is > 0; the RAII guard `BlnetCallScope` (used around every native→managed call) maintains it.

---

## File Structure

| File | Responsibility |
|---|---|
| Create `BasicLang/Compiler/CodeGen/CPlusPlus/BlnetContract.cs` | ABI version + status-code table + text emitters (C header section, C# enum) |
| Create `BasicLang/BoundaryTypeRegistry.cs` | C1 four-category type registry; replaces the checker's two private sets |
| Modify `BasicLang/CppCapabilityChecker.cs` | Read the registry instead of `MappedTypeNames`/`UnmappedNetTypes` (behavior-preserving) |
| Modify `BasicLang/TypeMapper.cs` | `internal` accessor exposing `_typeMap` keys for the invariant test |
| Create `BasicLang/Compiler/CodeGen/CPlusPlus/BlnetRuntimeSources.cs` | Checked-in text of `blnet.h` + `blnet_runtime.hpp` (native side of the contract) |
| Create `VisualGameStudio.Tests/Blnet/BlnetContractTests.cs` | Unit tests: status table, emitters, registry, drift, mapper invariant |
| Create `VisualGameStudio.Tests/Blnet/BlnetNativeRuntimeTests.cs` | Integration: pure-C++ runtime behavior via `CppCompile` (no shim) |
| Create `VisualGameStudio.Tests/TestAssets/BlnetTestShim/BlnetTestShim.csproj` | Minimal AOT shim project (also builds as a plain lib for in-process tests) |
| Create `VisualGameStudio.Tests/TestAssets/BlnetTestShim/HandleTable.cs` | C2 generation-tagged GCHandle table (plain class, unit-testable) |
| Create `VisualGameStudio.Tests/TestAssets/BlnetTestShim/BlnetStatus.cs` | C# status enum — must equal `BlnetContract.GenerateStatusEnumCs()` (drift-tested) |
| Create `VisualGameStudio.Tests/TestAssets/BlnetTestShim/Exports.cs` | `[UnmanagedCallersOnly]` contract + test exports |
| Create `VisualGameStudio.Tests/Blnet/HandleTableTests.cs` | Fast in-process unit tests for `HandleTable` |
| Create `VisualGameStudio.Tests/Blnet/BlnetConformanceTests.cs` | Integration: AOT publish + harness compile + scenarios 1–16 |
| Create `VisualGameStudio.Tests/TestAssets/BlnetHarness/main.cpp.txt` | Native harness source (checked in as `.txt` so nothing tries to compile it in place) |
| Modify `VisualGameStudio.Tests/VisualGameStudio.Tests.csproj` | ProjectReference to the shim + copy `TestAssets` to output |

The shim project is **not** added to `VisualGameStudioEngine.sln` — it's a test asset, referenced only by the test project.

---

### Task 1: `BlnetContract` — status codes, ABI version, emitters

**Files:**
- Create: `BasicLang/Compiler/CodeGen/CPlusPlus/BlnetContract.cs`
- Test: `VisualGameStudio.Tests/Blnet/BlnetContractTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using BasicLang.Compiler.CodeGen.CPlusPlus;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

[TestFixture]
public class BlnetContractTests
{
    [Test]
    public void StatusCodes_AreDenseFromZero_AndUniquelyNamed()
    {
        var codes = BlnetContract.StatusCodes;
        Assert.That(codes[0], Is.EqualTo(("BLNET_OK", 0, codes[0].Doc)));
        for (int i = 0; i < codes.Count; i++)
            Assert.That(codes[i].Value, Is.EqualTo(i), $"status values must be dense: {codes[i].Name}");
        Assert.That(codes.Select(c => c.Name), Is.Unique);
    }

    [Test]
    public void StatusCodes_ContainEverySpecStatus()
    {
        var names = BlnetContract.StatusCodes.Select(c => c.Name).ToHashSet();
        foreach (var required in new[]
        {
            "BLNET_OK", "BLNET_E_STALE_HANDLE", "BLNET_E_STALE_CALLBACK",
            "BLNET_E_MANAGED_EXCEPTION", "BLNET_E_NATIVE_EXCEPTION",
            "BLNET_E_CROSS_THREAD_RESULT", "BLNET_E_PUMP_REENTRY",
            "BLNET_E_VERSION_MISMATCH", "BLNET_E_ALLOC",
        })
            Assert.That(names, Does.Contain(required));
    }

    [Test]
    public void GenerateStatusHeader_EmitsOneDefinePerCode_WithGeneratedBanner()
    {
        var header = BlnetContract.GenerateStatusHeader();
        Assert.That(header, Does.StartWith("/* GENERATED from BlnetContract"));
        Assert.That(header, Does.Contain($"#define BLNET_ABI_VERSION {BlnetContract.AbiVersion}"));
        foreach (var (name, value, _) in BlnetContract.StatusCodes)
            Assert.That(header, Does.Contain($"#define {name} {value}"));
    }

    [Test]
    public void GenerateStatusEnumCs_EmitsOneMemberPerCode()
    {
        var cs = BlnetContract.GenerateStatusEnumCs();
        Assert.That(cs, Does.Contain("public enum BlnetStatus"));
        foreach (var (name, value, _) in BlnetContract.StatusCodes)
            Assert.That(cs, Does.Contain($"{name} = {value},"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~BlnetContractTests" > test-run.txt 2>&1`
Expected: build FAILS — `BlnetContract` does not exist.

- [ ] **Step 3: Implement `BlnetContract`**

```csharp
namespace BasicLang.Compiler.CodeGen.CPlusPlus
{
    /// <summary>
    /// Single source of truth for the .NET⇄native boundary ABI constants
    /// (spec: docs/superpowers/specs/2026-07-26-dotnet-native-boundary-contract-design.md, C4/C7).
    /// The C header status section and the shim's C# enum are BOTH generated from this
    /// table (drift-tested in BlnetContractTests) — never edit those by hand.
    /// </summary>
    public static class BlnetContract
    {
        /// <summary>C7: bumped on ANY change to the ABI — status codes, slot encoding, export signatures.</summary>
        public const int AbiVersion = 1;

        public static readonly IReadOnlyList<(string Name, int Value, string Doc)> StatusCodes = new[]
        {
            ("BLNET_OK", 0, "Success."),
            ("BLNET_E_STALE_HANDLE", 1, "Generation mismatch on an object handle: use-after-release or double-release."),
            ("BLNET_E_STALE_CALLBACK", 2, "Generation mismatch on a callback handle."),
            ("BLNET_E_MANAGED_EXCEPTION", 3, "A .NET exception was caught at the boundary; details via blnet_last_error."),
            ("BLNET_E_NATIVE_EXCEPTION", 4, "A native exception was caught inside a callback; details via blnet_last_error."),
            ("BLNET_E_CROSS_THREAD_RESULT", 5, "Result-bearing callback invoked cross-thread without the Immediate flag."),
            ("BLNET_E_PUMP_REENTRY", 6, "blnet_pump entered concurrently from a second thread."),
            ("BLNET_E_VERSION_MISMATCH", 7, "blnet_initialize ABI version check failed."),
            ("BLNET_E_ALLOC", 8, "Allocation failed at the boundary."),
        };

        public static string GenerateStatusHeader()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("/* GENERATED from BlnetContract — do not edit by hand. */\n");
            sb.Append($"#define BLNET_ABI_VERSION {AbiVersion}\n");
            foreach (var (name, value, doc) in StatusCodes)
                sb.Append($"#define {name} {value} /* {doc} */\n");
            return sb.ToString();
        }

        public static string GenerateStatusEnumCs()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("// GENERATED from BlnetContract.StatusCodes — do not edit by hand.\n");
            sb.Append("public enum BlnetStatus\n{\n");
            foreach (var (name, value, doc) in StatusCodes)
                sb.Append($"    /// <summary>{doc}</summary>\n    {name} = {value},\n");
            sb.Append("}\n");
            return sb.ToString();
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: same command as Step 2. Expected: 4 PASS.

- [ ] **Step 5: Commit**

```bash
git add BasicLang/Compiler/CodeGen/CPlusPlus/BlnetContract.cs VisualGameStudio.Tests/Blnet/BlnetContractTests.cs
git commit -m "feat(blnet): BlnetContract - ABI version, status codes, header/enum emitters"
```

---

### Task 2: `BoundaryTypeRegistry` (spec C1)

**Files:**
- Create: `BasicLang/BoundaryTypeRegistry.cs`
- Test: append to `VisualGameStudio.Tests/Blnet/BlnetContractTests.cs`

Pre-P1 category assignments are **exactly today's behavior**: today's `MappedTypeNames` ⇒ `Bridged`; today's `UnmappedNetTypes` + `Object` ⇒ `Rejected`; `NativeOwned`/`ManagedOwned` start **empty** (P1/P2 populate them). Unknown names ⇒ `Unknown` (the checker's class-kind/user-defined logic keeps handling those).

- [ ] **Step 1: Write the failing tests** (new fixture in the same file)

```csharp
[TestFixture]
public class BoundaryTypeRegistryTests
{
    [TestCase("Integer")] [TestCase("String")] [TestCase("ULong")] [TestCase("Void")]
    public void TodaysMappedPrimitives_AreBridged(string name) =>
        Assert.That(BasicLang.BoundaryTypeRegistry.Categorize(name),
            Is.EqualTo(BasicLang.BoundaryTypeCategory.Bridged));

    [TestCase("Object")] [TestCase("Decimal")] [TestCase("SByte")]
    [TestCase("DateTime")] [TestCase("DateTimeOffset")] [TestCase("TimeSpan")]
    [TestCase("Guid")] [TestCase("StringBuilder")] [TestCase("Regex")]
    [TestCase("Uri")] [TestCase("Stream")] [TestCase("FileInfo")] [TestCase("DirectoryInfo")]
    public void TodaysRejectList_IsRejected(string name) =>
        Assert.That(BasicLang.BoundaryTypeRegistry.Categorize(name),
            Is.EqualTo(BasicLang.BoundaryTypeCategory.Rejected));

    [Test]
    public void CategorizeIsCaseInsensitive() =>
        Assert.That(BasicLang.BoundaryTypeRegistry.Categorize("datetime"),
            Is.EqualTo(BasicLang.BoundaryTypeCategory.Rejected));

    [Test]
    public void UnknownName_IsUnknown() =>
        Assert.That(BasicLang.BoundaryTypeRegistry.Categorize("MyGameSprite"),
            Is.EqualTo(BasicLang.BoundaryTypeCategory.Unknown));

    [Test]
    public void NativeOwnedAndManagedOwned_StartEmpty_PreP1()
    {
        Assert.That(BasicLang.BoundaryTypeRegistry.NamesInCategory(
            BasicLang.BoundaryTypeCategory.NativeOwned), Is.Empty);
        Assert.That(BasicLang.BoundaryTypeRegistry.NamesInCategory(
            BasicLang.BoundaryTypeCategory.ManagedOwned), Is.Empty);
    }
}
```

- [ ] **Step 2: Run to verify failure** (same filter trick, `FullyQualifiedName~BoundaryTypeRegistryTests`). Expected: build FAILS.

- [ ] **Step 3: Implement**

```csharp
namespace BasicLang
{
    /// <summary>Spec C1 category of a type name at the .NET⇄native boundary.</summary>
    public enum BoundaryTypeCategory
    {
        /// <summary>Pure C++ implementation; never crosses as a handle (populated by P1).</summary>
        NativeOwned,
        /// <summary>GC-heap object; crosses only as a generation-tagged handle (populated by P2).</summary>
        ManagedOwned,
        /// <summary>Value-converted at the edge; both sides have a native representation.</summary>
        Bridged,
        /// <summary>Known to the registry, no permitted use in native projects (e.g. Object: void* erasure is unsound).</summary>
        Rejected,
        /// <summary>Not a registry name — user-defined / generic / foreign types resolve elsewhere.</summary>
        Unknown,
    }

    /// <summary>
    /// Single source of truth for boundary type ownership
    /// (spec C1, docs/superpowers/specs/2026-07-26-dotnet-native-boundary-contract-design.md).
    /// Replaces the previously hand-synchronized CppCapabilityChecker.MappedTypeNames /
    /// UnmappedNetTypes sets; CppTypeMapper._typeMap keys are held to this registry by
    /// BlnetContractTests.MapperInvariant. INVARIANT: Bridged must be exactly the key set
    /// of CppTypeMapper._typeMap MINUS 'Object' (which is Rejected: void* erasure is
    /// unsound). SByte and Decimal are NOT mapped by CppTypeMapper and must stay Rejected.
    /// </summary>
    public static class BoundaryTypeRegistry
    {
        private static readonly HashSet<string> Bridged = new(StringComparer.OrdinalIgnoreCase)
        {
            "Integer", "Long", "Single", "Double", "String", "Boolean", "Char", "Void",
            "Byte", "Short", "UByte", "UShort", "UInteger", "ULong"
        };

        private static readonly HashSet<string> Rejected = new(StringComparer.OrdinalIgnoreCase)
        {
            "Object",
            "Decimal", "SByte",
            "DateTime", "DateTimeOffset", "TimeSpan", "Guid", "StringBuilder", "Regex",
            "Uri", "Stream", "FileInfo", "DirectoryInfo"
        };

        private static readonly HashSet<string> NativeOwned = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> ManagedOwned = new(StringComparer.OrdinalIgnoreCase);

        public static BoundaryTypeCategory Categorize(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return BoundaryTypeCategory.Unknown;
            if (NativeOwned.Contains(typeName)) return BoundaryTypeCategory.NativeOwned;
            if (ManagedOwned.Contains(typeName)) return BoundaryTypeCategory.ManagedOwned;
            if (Bridged.Contains(typeName)) return BoundaryTypeCategory.Bridged;
            if (Rejected.Contains(typeName)) return BoundaryTypeCategory.Rejected;
            return BoundaryTypeCategory.Unknown;
        }

        public static IReadOnlyCollection<string> NamesInCategory(BoundaryTypeCategory category) =>
            category switch
            {
                BoundaryTypeCategory.NativeOwned => NativeOwned,
                BoundaryTypeCategory.ManagedOwned => ManagedOwned,
                BoundaryTypeCategory.Bridged => Bridged,
                BoundaryTypeCategory.Rejected => Rejected,
                _ => Array.Empty<string>(),
            };
    }
}
```

- [ ] **Step 4: Run to verify pass.**
- [ ] **Step 5: Commit** — `git commit -m "feat(blnet): BoundaryTypeRegistry - C1 four-category type ownership"`

---

### Task 3: Refactor `CppCapabilityChecker` to read the registry

**Files:**
- Modify: `BasicLang/CppCapabilityChecker.cs` (delete the sets at ~lines 56–75; rewrite two checks in `CheckType` at ~lines 269, 277–289)

Behavior-preserving: **diagnostic message strings must not change** (tests assert on them). The full existing suite is the gate.

- [ ] **Step 1: Replace the two private sets with registry queries.**

Delete `MappedTypeNames` and `UnmappedNetTypes` (both declarations and their doc comments; move any still-relevant invariant wording into `BoundaryTypeRegistry`'s doc comment — Task 2's version already carries it). In `CheckType`:

Replace (line ~269):
```csharp
if (string.IsNullOrEmpty(name) || MappedTypeNames.Contains(name)) return;
```
with:
```csharp
var category = BoundaryTypeRegistry.Categorize(name);
if (string.IsNullOrEmpty(name) || category == BoundaryTypeCategory.Bridged) return;
```

Replace the `Object` + `UnmappedNetTypes` blocks (lines ~277–289):
```csharp
if (name.Equals("Object", StringComparison.OrdinalIgnoreCase))
{
    diags.Add($"'Object' ({where}) — 'Object' has no C++ mapping");
    return;
}
if (category == BoundaryTypeCategory.Rejected)
{
    diags.Add($".NET type '{name}' ({where}) — no C++ mapping exists for this type");
    return;
}
```
(`Object` keeps its distinct message; it is category `Rejected` too, so the `Object` check must stay FIRST.)

- [ ] **Step 2: Build + run the fast subset**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "TestCategory!=Integration" > test-run.txt 2>&1`
Expected: same pass count as before the change (the BL6009 flake exit-1 is normal — check the failure list, not the exit code). **Record the pass count** — Task 12 Step 1 compares against exactly this baseline number.

- [ ] **Step 3: Commit** — `git commit -m "refactor(blnet): CppCapabilityChecker reads BoundaryTypeRegistry (behavior-preserving)"`

---

### Task 4: Mechanical mapper invariant

**Files:**
- Modify: `BasicLang/TypeMapper.cs` — `CppTypeMapper` lives in namespace `BasicLang.Compiler.CodeGen` (~line 201), `_typeMap` is a protected instance `Dictionary` on `TypeMapperBase`, and parameterless construction works. Add to `CppTypeMapper`:
```csharp
/// <summary>Invariant hook: BlnetContractTests asserts these equal BoundaryTypeRegistry's Bridged set + 'Object'.</summary>
internal IEnumerable<string> MappedTypeNamesForInvariantCheck => _typeMap.Keys;
```
The test constructs `new BasicLang.Compiler.CodeGen.CppTypeMapper()` (add the `using`) and reads the property. `InternalsVisibleTo` for the test assembly already exists — tests already use `CppCodeGenerator.RuntimeHeaderFileName`, an `internal const`.
- Test: append to `BlnetContractTests.cs`:

```csharp
[Test]
public void MapperInvariant_TypeMapKeys_Equal_BridgedPlusObject()
{
    var expected = BasicLang.BoundaryTypeRegistry
        .NamesInCategory(BasicLang.BoundaryTypeCategory.Bridged)
        .Append("Object")
        .Select(n => n.ToLowerInvariant()).OrderBy(n => n).ToArray();
    var actual = new BasicLang.Compiler.CodeGen.CppTypeMapper().MappedTypeNamesForInvariantCheck
        .Select(n => n.ToLowerInvariant()).OrderBy(n => n).ToArray();
    Assert.That(actual, Is.EqualTo(expected),
        "CppTypeMapper._typeMap and BoundaryTypeRegistry drifted — update the registry, not a parallel list");
}
```

- [ ] **Step 1: Write the test, run, adjust the accessor until it compiles, verify the invariant actually holds** (if `_typeMap` contains extra names, that's a REAL pre-existing drift: stop and report it rather than papering over it — do not silently widen the registry).
- [ ] **Step 2: Fast subset green. Commit** — `git commit -m "test(blnet): mechanical registry<->CppTypeMapper invariant replaces comment-based sync"`

---

### Task 5: `BlnetRuntimeSources` — the two C++ headers

**Files:**
- Create: `BasicLang/Compiler/CodeGen/CPlusPlus/BlnetRuntimeSources.cs`
- Test: append drift + smoke tests

Two public string constants (pattern: `CppRuntimeSources.cs`), assembled with the generated status section spliced in:

**`BlnetHeader`** (`blnet.h`) — C, includable from C or C++:

```c
/* blnet.h — .NET⇄native boundary contract v1 (spec 2026-07-26). */
/* SOURCE OF TRUTH: BasicLang BlnetRuntimeSources.cs — do not edit the emitted copy. */
#pragma once
#include <stdint.h>

#if defined(_WIN32) && defined(_M_IX86)
#define BLNET_CALL __cdecl
#else
#define BLNET_CALL
#endif

/* C2: {generation: high 32 | index: low 32}. Index 0 is reserved (a zero handle is never valid). */
typedef uint64_t blnet_handle;
typedef uint64_t blnet_callback;

{STATUS_SECTION}

/* C5 slot descriptor: how one 64-bit slot is encoded (needed for deep-copy at enqueue). */
typedef enum BlnetSlotKind {
    BLNET_SLOT_VALUE = 0,   /* blittable scalar or struct <= 8 bytes, in-slot */
    BLNET_SLOT_STRING = 1,  /* UTF-8 char*, C3 ownership rules */
    BLNET_SLOT_STRUCT = 2,  /* pointer to blittable struct > 8 bytes, borrowed for the call */
    BLNET_SLOT_HANDLE = 3,  /* blnet_handle */
    BLNET_SLOT_OUT = 4      /* caller-provided pointer the callee writes through (inline-only) */
} BlnetSlotKind;

typedef struct BlnetSlotDesc { int32_t kind; int32_t size; /* bytes; used for STRUCT/OUT */ } BlnetSlotDesc;

/* C5: the single universal thunk (native-side). */
typedef int32_t (BLNET_CALL *BlnetInvokeCallbackFn)(
    uint64_t callback_handle, const uint64_t* args, int32_t argc, uint64_t* result);
/* Retrieves the pending native-exception message after BLNET_E_NATIVE_EXCEPTION
   (buffer allocated with blnet_alloc; receiver frees via blnet_free). */
typedef int32_t (BLNET_CALL *BlnetGetNativeErrorFn)(char** message);

typedef struct BlnetNativeVtable {
    BlnetInvokeCallbackFn invoke_callback;
    BlnetGetNativeErrorFn get_native_error;
} BlnetNativeVtable;

/* Shim exports (managed side). Native code binds these by name. */
#define BLNET_EXPORT_ABI_VERSION   "blnet_abi_version"   /* int32_t (void) */
#define BLNET_EXPORT_INITIALIZE    "blnet_initialize"    /* int32_t (int32_t expected_abi, const BlnetNativeVtable*) */
#define BLNET_EXPORT_ADDREF        "blnet_addref"        /* int32_t (blnet_handle) */
#define BLNET_EXPORT_RELEASE       "blnet_release"       /* int32_t (blnet_handle) */
#define BLNET_EXPORT_ALLOC         "blnet_alloc"         /* void*   (int64_t size) — NULL on failure */
#define BLNET_EXPORT_FREE          "blnet_free"          /* void    (void*) */
#define BLNET_EXPORT_LAST_ERROR    "blnet_last_error"    /* int32_t (char** type_name, char** message) — buffers freed via blnet_free */
```

**`BlnetRuntime`** (`blnet_runtime.hpp`) — C++20, header-only, native side of the contract. Contents (complete, ~250 lines; the executor writes exactly this design):

```cpp
/* blnet_runtime.hpp — native-side runtime of the boundary contract v1. Header-only C++20. */
#pragma once
#include "blnet.h"
#include <atomic>
#include <cstring>
#include <deque>
#include <functional>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <string>
#include <vector>

namespace BasicLang { namespace blnet {

/* ---- Shim binding (filled by the host: harness now, generated startup in P2) ---- */
struct ShimApi {
    int32_t (BLNET_CALL *abi_version)(void) = nullptr;
    int32_t (BLNET_CALL *initialize)(int32_t, const BlnetNativeVtable*) = nullptr;
    int32_t (BLNET_CALL *addref)(blnet_handle) = nullptr;
    int32_t (BLNET_CALL *release)(blnet_handle) = nullptr;
    void*   (BLNET_CALL *alloc)(int64_t) = nullptr;
    void    (BLNET_CALL *free_)(void*) = nullptr;
    int32_t (BLNET_CALL *last_error)(char**, char**) = nullptr;
};
inline ShimApi g_shim;

/* ---- C6/C5 same-thread detection: depth of native->managed calls on this thread ---- */
inline thread_local int g_call_depth = 0;
struct BlnetCallScope { BlnetCallScope() { ++g_call_depth; } ~BlnetCallScope() { --g_call_depth; } };

/* ---- C4: NetCheck — status to C++ exception ---- */
inline void NetCheck(int32_t status) {
    if (status == BLNET_OK) return;
    std::string msg = "blnet status " + std::to_string(status);
    if (g_shim.last_error) {
        char* type = nullptr; char* m = nullptr;
        if (g_shim.last_error(&type, &m) == BLNET_OK) {
            if (type) { msg += " ["; msg += type; msg += "]"; g_shim.free_(type); }
            if (m)    { msg += ": "; msg += m;   g_shim.free_(m); }
        }
    }
    throw std::runtime_error(msg);
}

/* ---- C2: NetRef — RAII over a managed handle (shared_ptr custom-deleter pattern,
   mirroring the collection layer's reference semantics) ---- */
class NetRef {
    std::shared_ptr<void> ref_;
public:
    NetRef() = default;
    /* Takes ownership of one table reference (fresh handles are born refcount 1). */
    explicit NetRef(blnet_handle h)
        : ref_(h ? std::shared_ptr<void>(reinterpret_cast<void*>(h),
              [](void* p) { if (g_shim.release) g_shim.release(reinterpret_cast<blnet_handle>(p)); })
                 : nullptr) {}
    blnet_handle get() const { return reinterpret_cast<blnet_handle>(ref_.get()); }
    explicit operator bool() const { return static_cast<bool>(ref_); }
    /* A new INDEPENDENT NetRef for the same object goes through blnet_addref. */
    static NetRef Duplicate(const NetRef& other) {
        if (other && g_shim.addref) NetCheck(g_shim.addref(other.get()));
        return NetRef(other.get());
    }
};

/* ---- C5: callback table (generation-tagged, mirrors C2) ---- */
using NativeCallbackFn = std::function<int32_t(const uint64_t* args, int32_t argc, uint64_t* result)>;

struct CallbackFlags { bool result_bearing = false; bool immediate = false; };

namespace detail {
    struct CallbackEntry {
        NativeCallbackFn fn; std::vector<BlnetSlotDesc> slots;
        uint32_t generation = 1; CallbackFlags flags{}; bool alive = false;
    };
    struct QueuedInvocation {
        blnet_callback handle{};
        std::vector<uint64_t> args;
        /* deep-copied storage owned by the queue (freed by the pump after execution) */
        std::vector<std::unique_ptr<char[]>> owned_strings;
        std::vector<std::vector<unsigned char>> owned_structs;
        std::vector<blnet_handle> owned_handles; /* addref'd at enqueue */
    };
    inline std::mutex g_cb_mutex;
    inline std::vector<CallbackEntry> g_callbacks;      /* index 0 reserved */
    inline std::vector<uint32_t> g_cb_freelist;
    inline std::mutex g_queue_mutex;
    inline std::deque<QueuedInvocation> g_queue;
    inline std::atomic<bool> g_pumping{false};
    inline thread_local std::string g_native_error;      /* pending native-exception message */
    inline void (*g_error_hook)(int32_t, const char*) = nullptr;

    inline CallbackEntry* lookup(blnet_callback h, uint32_t* out_index) {
        uint32_t index = static_cast<uint32_t>(h & 0xFFFFFFFFu);
        uint32_t gen   = static_cast<uint32_t>(h >> 32);
        if (index == 0 || index >= g_callbacks.size()) return nullptr;
        auto& e = g_callbacks[index];
        if (!e.alive || e.generation != gen) return nullptr;
        if (out_index) *out_index = index;
        return &e;
    }
}

inline blnet_callback blnet_register_callback(
    NativeCallbackFn fn, const BlnetSlotDesc* slots, int32_t argc, CallbackFlags flags) {
    std::lock_guard<std::mutex> lk(detail::g_cb_mutex);
    if (detail::g_callbacks.empty()) detail::g_callbacks.emplace_back(); /* burn index 0 */
    uint32_t index;
    if (!detail::g_cb_freelist.empty()) { index = detail::g_cb_freelist.back(); detail::g_cb_freelist.pop_back(); }
    else { index = static_cast<uint32_t>(detail::g_callbacks.size()); detail::g_callbacks.emplace_back(); }
    auto& e = detail::g_callbacks[index];
    e.fn = std::move(fn); e.flags = flags; e.alive = true;
    e.slots.clear();
    if (argc > 0) e.slots.assign(slots, slots + argc); /* guard: zero-arg registration may pass slots == nullptr */
    return (static_cast<uint64_t>(e.generation) << 32) | index;
}

inline int32_t blnet_callback_release(blnet_callback h) {
    std::lock_guard<std::mutex> lk(detail::g_cb_mutex);
    uint32_t index;
    auto* e = detail::lookup(h, &index);
    if (!e) return BLNET_E_STALE_CALLBACK;
    e->alive = false; e->fn = nullptr; ++e->generation;    /* generation bumps on free (C2 rule mirrored) */
    detail::g_cb_freelist.push_back(index);
    return BLNET_OK;
}

inline void blnet_set_error_hook(void (*hook)(int32_t, const char*)) { detail::g_error_hook = hook; }

/* invoke inline, translating native exceptions per C4 */
inline int32_t invoke_entry(detail::CallbackEntry& e, const uint64_t* args, int32_t argc, uint64_t* result) {
    try { return e.fn(args, argc, result); }
    catch (const std::exception& ex) { detail::g_native_error = ex.what(); return BLNET_E_NATIVE_EXCEPTION; }
    catch (...) { detail::g_native_error = "unknown native exception"; return BLNET_E_NATIVE_EXCEPTION; }
}

/* C5: THE universal thunk — managed code holds exactly this function pointer. */
inline int32_t BLNET_CALL blnet_invoke_callback(
    uint64_t callback_handle, const uint64_t* args, int32_t argc, uint64_t* result) {
    detail::CallbackEntry snapshot; /* copy under lock, invoke outside it */
    {
        std::lock_guard<std::mutex> lk(detail::g_cb_mutex);
        auto* e = detail::lookup(callback_handle, nullptr);
        if (!e) return BLNET_E_STALE_CALLBACK;
        snapshot = *e;
    }
    const bool same_thread = g_call_depth > 0;
    if (same_thread || snapshot.flags.immediate)
        return invoke_entry(snapshot, args, argc, result);
    if (snapshot.flags.result_bearing)
        return BLNET_E_CROSS_THREAD_RESULT;
    /* queued fire-and-forget notification: deep-copy per slot descriptors */
    detail::QueuedInvocation q; q.handle = callback_handle; q.args.assign(args, args + argc);
    for (int32_t i = 0; i < argc; ++i) {
        switch (snapshot.slots[i].kind) {
            case BLNET_SLOT_STRING: {
                const char* s = reinterpret_cast<const char*>(args[i]);
                size_t n = s ? std::strlen(s) + 1 : 1;
                auto buf = std::make_unique<char[]>(n);
                std::memcpy(buf.get(), s ? s : "", n);
                q.args[i] = reinterpret_cast<uint64_t>(buf.get());
                q.owned_strings.push_back(std::move(buf));
                break;
            }
            case BLNET_SLOT_STRUCT: {
                if (!args[i]) break; /* null struct pointer: tolerated as a null slot, no copy */
                auto size = static_cast<size_t>(snapshot.slots[i].size);
                std::vector<unsigned char> buf(size);
                std::memcpy(buf.data(), reinterpret_cast<const void*>(args[i]), size);
                q.args[i] = reinterpret_cast<uint64_t>(buf.data());
                q.owned_structs.push_back(std::move(buf));
                break;
            }
            case BLNET_SLOT_HANDLE: {
                if (args[i] && g_shim.addref) {
                    int32_t st = g_shim.addref(args[i]);
                    if (st != BLNET_OK) {
                        /* stale at enqueue fails the invocation immediately — but first
                           release the refs already taken for EARLIER handle slots, or
                           they leak (the queue never sees this invocation). */
                        for (auto h : q.owned_handles) if (g_shim.release) g_shim.release(h);
                        return st;
                    }
                    q.owned_handles.push_back(args[i]);
                }
                break;
            }
            default: break; /* BLNET_SLOT_VALUE: already in q.args */
        }
    }
    { std::lock_guard<std::mutex> lk(detail::g_queue_mutex); detail::g_queue.push_back(std::move(q)); }
    return BLNET_OK;
}

/* C4/C5: drain the queue on the pump thread. Continues on failure; hook fires per
   failure; returns the FIRST failure's status. Reentry is a defined failure. */
inline int32_t blnet_pump() {
    bool expected = false;
    if (!detail::g_pumping.compare_exchange_strong(expected, true)) return BLNET_E_PUMP_REENTRY;
    int32_t first_failure = BLNET_OK;
    for (;;) {
        detail::QueuedInvocation q;
        {
            std::lock_guard<std::mutex> lk(detail::g_queue_mutex);
            if (detail::g_queue.empty()) break;
            q = std::move(detail::g_queue.front()); detail::g_queue.pop_front();
        }
        int32_t st;
        detail::CallbackEntry snapshot;
        {
            std::lock_guard<std::mutex> lk(detail::g_cb_mutex);
            auto* e = detail::lookup(q.handle, nullptr);
            st = e ? BLNET_OK : BLNET_E_STALE_CALLBACK;
            if (e) snapshot = *e;
        }
        if (st == BLNET_OK)
            st = invoke_entry(snapshot, q.args.data(), static_cast<int32_t>(q.args.size()), nullptr);
        if (st != BLNET_OK) {
            if (first_failure == BLNET_OK) first_failure = st;
            /* a throwing hook must not leave g_pumping stuck true */
            if (detail::g_error_hook)
                try { detail::g_error_hook(st, detail::g_native_error.c_str()); } catch (...) {}
        }
        for (auto h : q.owned_handles) if (g_shim.release) g_shim.release(h);
        /* owned_strings / owned_structs free when q goes out of scope — queue-owned storage, pump-freed */
    }
    detail::g_pumping.store(false);
    return first_failure;
}

/* Vtable entry: shim pulls the pending native-exception message (blnet_alloc'd; shim frees). */
inline int32_t BLNET_CALL blnet_get_native_error(char** message) {
    if (!message) return BLNET_E_ALLOC;
    const auto& s = detail::g_native_error;
    char* buf = static_cast<char*>(g_shim.alloc ? g_shim.alloc(static_cast<int64_t>(s.size() + 1)) : nullptr);
    if (!buf) { *message = nullptr; return BLNET_E_ALLOC; }
    std::memcpy(buf, s.c_str(), s.size() + 1);
    *message = buf;
    return BLNET_OK;
}

}} /* namespace BasicLang::blnet */
```

`BlnetRuntimeSources.cs` assembles: `public static string BlnetHeader => Header1 + BlnetContract.GenerateStatusHeader() + Header2;` and `public static string BlnetRuntime => <the hpp text>;` (verbatim string constants; escape `"` as `""`).

- [ ] **Step 1: Write the failing tests**

```csharp
[TestFixture]
public class BlnetRuntimeSourcesTests
{
    [Test]
    public void Header_ContainsGeneratedStatusSection() =>
        Assert.That(BlnetRuntimeSources.BlnetHeader,
            Does.Contain(BlnetContract.GenerateStatusHeader()));

    [Test]
    public void Header_DefinesCallMacro_HandleTypes_AndAllExportNames()
    {
        var h = BlnetRuntimeSources.BlnetHeader;
        Assert.That(h, Does.Contain("#define BLNET_CALL"));
        Assert.That(h, Does.Contain("typedef uint64_t blnet_handle;"));
        foreach (var export in new[] { "blnet_abi_version", "blnet_initialize", "blnet_addref",
            "blnet_release", "blnet_alloc", "blnet_free", "blnet_last_error" })
            Assert.That(h, Does.Contain($"\"{export}\""));
    }
}
```

And the compile smoke test (Integration, in `BlnetNativeRuntimeTests.cs`):

```csharp
[TestFixture]
[Category("Integration")]
public class BlnetNativeRuntimeTests
{
    private (string exe, string argsTemplate)? _compiler;

    [OneTimeSetUp]
    public void FindCompiler() => _compiler = Native.CppCompile.FindRunCompiler();

    private string Run(string mainBody)
    {
        if (_compiler is null) Assert.Ignore("No C++ compiler available");
        var src = "#include \"blnet_runtime.hpp\"\n#include <cstdio>\n" + mainBody;
        return Native.CppCompile.CompileAndRun(src, _compiler.Value, new Dictionary<string, string>
        {
            ["blnet.h"] = BlnetRuntimeSources.BlnetHeader,
            ["blnet_runtime.hpp"] = BlnetRuntimeSources.BlnetRuntime,
        });
    }

    [Test]
    public void HeadersCompileStandalone() =>
        Assert.That(Run("int main(){ printf(\"ok\"); return 0; }"), Is.EqualTo("ok"));
}
```

- [ ] **Step 2: Run unit tests (fail: type missing), implement `BlnetRuntimeSources.cs`, unit tests pass.**
- [ ] **Step 3: Run the Integration smoke test**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~BlnetNativeRuntimeTests" > test-run.txt 2>&1`
Expected: `HeadersCompileStandalone` PASS (fix C++ compile errors in the string constants until clean — this is the debugging loop for the hpp).

- [ ] **Step 4: Commit** — `git commit -m "feat(blnet): blnet.h + blnet_runtime.hpp as single-source string constants (drift-tested, compile-smoked)"`

---

### Task 6: Native-only runtime behavior tests (no shim yet)

**Files:** append to `VisualGameStudio.Tests/Blnet/BlnetNativeRuntimeTests.cs`.

Each test compiles a small `main` (via the Task 5 `Run` helper) exercising the callback table/queue/pump **pure-natively** (register native lambdas, invoke `blnet_invoke_callback` directly from `main` and from `std::thread`s). Each program prints markers the test asserts. Write the test first, watch it fail (usually a compile error or wrong output), fix the hpp, re-run. One commit per green test is fine; batching all of Task 6 into one commit is also acceptable.

- [ ] **Test: same-thread inline dispatch.** `main` wraps the invoke in `BlnetCallScope` (depth > 0 → inline), registers a notification callback setting a flag, invokes the thunk, prints flag immediately. Expect `inline=1`.
- [ ] **Test: stale callback.** Register, release, invoke → expect printed status == `BLNET_E_STALE_CALLBACK`; double-release → same.
- [ ] **Test: generation reuse.** Release; register a new callback (reuses the slot index); the OLD handle must still fail. Print both statuses.
- [ ] **Test: cross-thread notification queues; pump fires it.** Invoke from a `std::thread` (no scope guard → depth 0 → queued). After join: flag still 0. After `blnet_pump()`: flag 1, pump returned `BLNET_OK`.
- [ ] **Test: cross-thread result-bearing rejection.** Register with `result_bearing=true`, invoke from a thread → returned status `BLNET_E_CROSS_THREAD_RESULT`, and the callback never ran.
- [ ] **Test: Immediate flag.** `result_bearing=true, immediate=true`, invoked from a thread → runs inline on that thread, result slot filled.
- [ ] **Test: pump drain semantics.** Queue three notifications; the 2nd throws `std::runtime_error("boom2")`, the 3rd throws `"boom3"`. Install an error hook counting invocations and recording messages. `blnet_pump()` → all three attempted, hook fired twice, return status == `BLNET_E_NATIVE_EXCEPTION` (first failure), queue empty after.
- [ ] **Test: pump reentry.** Register a notification whose body calls `blnet_pump()` (we are ON the pump thread inside a drain) → inner call returns `BLNET_E_PUMP_REENTRY`; also spawn a thread calling `blnet_pump()` while main pumps a long queue — the loser returns `BLNET_E_PUMP_REENTRY`. (Deterministic version: the in-callback reentry alone is sufficient; the two-thread race is best-effort and may be omitted if flaky.)
- [ ] **Test: string deep-copy at enqueue.** Register a 1-arg notification with `BLNET_SLOT_STRING`; invoke from a thread passing a stack buffer; overwrite the buffer after join, then pump — callback must see the ORIGINAL contents (non-ASCII bytes included, e.g. `"h\xC3\xA9llo"` — a narrow literal with explicit UTF-8 byte escapes; in C++20 `u8"..."` is `const char8_t*` and does NOT convert to `const char*` on any toolchain).
- [ ] **Commit** — `git commit -m "test(blnet): native-side runtime conformance (callback table, queue, pump) via CppCompile"`

---

### Task 7: Test shim project + `HandleTable` (fast unit tests)

**Files:**
- Create: `VisualGameStudio.Tests/TestAssets/BlnetTestShim/BlnetTestShim.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <!-- net8.0: must match VisualGameStudio.Tests (net8.0) or its ProjectReference fails NU1201.
         Native AOT NativeLib=Shared is fully supported on net8.0. -->
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <!-- AOT applies at publish; a normal build stays a managed lib the test project can reference. -->
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization>
    <RootNamespace>BlnetTestShim</RootNamespace>
  </PropertyGroup>
</Project>
```

- Create: `VisualGameStudio.Tests/TestAssets/BlnetTestShim/BlnetStatus.cs` — paste the exact output of `BlnetContract.GenerateStatusEnumCs()` wrapped in `namespace BlnetTestShim;`, plus (same regenerate-from-contract rule):
```csharp
public static class ShimAbi { public const int AbiVersion = 1; } // keep equal to BlnetContract.AbiVersion (drift-tested)
```
- Create: `VisualGameStudio.Tests/TestAssets/BlnetTestShim/HandleTable.cs`:

```csharp
using System.Runtime.InteropServices;

namespace BlnetTestShim;

/// <summary>
/// Spec C2: generation-tagged table of GCHandles. Handle = {generation:high32 | index:low32}.
/// Index 0 reserved. Fresh handle refcount = 1. Generation increments when a slot is FREED
/// (refcount hits zero), not per release. Table grows without bound (amortized append).
/// </summary>
public sealed class HandleTable
{
    private struct Slot { public GCHandle Gc; public uint Generation; public int RefCount; public bool Alive; }
    private readonly object _lock = new();
    private readonly List<Slot> _slots = new() { default };  // burn index 0
    private readonly Stack<uint> _free = new();

    public ulong Create(object target)
    {
        lock (_lock)
        {
            uint index;
            if (_free.Count > 0) index = _free.Pop();
            else { _slots.Add(default); index = (uint)(_slots.Count - 1); }
            var s = _slots[(int)index];
            if (s.Generation == 0) s.Generation = 1;
            s.Gc = GCHandle.Alloc(target);
            s.RefCount = 1;
            s.Alive = true;
            _slots[(int)index] = s;
            return ((ulong)s.Generation << 32) | index;
        }
    }

    public BlnetStatus TryGet(ulong handle, out object? target)
    {
        lock (_lock)
        {
            target = null;
            if (!Validate(handle, out var index)) return BlnetStatus.BLNET_E_STALE_HANDLE;
            target = _slots[(int)index].Gc.Target;
            return BlnetStatus.BLNET_OK;
        }
    }

    public BlnetStatus AddRef(ulong handle)
    {
        lock (_lock)
        {
            if (!Validate(handle, out var index)) return BlnetStatus.BLNET_E_STALE_HANDLE;
            var s = _slots[(int)index]; s.RefCount++; _slots[(int)index] = s;
            return BlnetStatus.BLNET_OK;
        }
    }

    public BlnetStatus Release(ulong handle)
    {
        lock (_lock)
        {
            if (!Validate(handle, out var index)) return BlnetStatus.BLNET_E_STALE_HANDLE;
            var s = _slots[(int)index];
            if (--s.RefCount == 0)
            {
                s.Gc.Free();
                s.Alive = false;
                s.Generation++;          // stale detection: old handles now fail Validate
                _slots[(int)index] = s;
                _free.Push(index);
            }
            else _slots[(int)index] = s;
            return BlnetStatus.BLNET_OK;
        }
    }

    public int AliveCount { get { lock (_lock) { return _slots.Count(s => s.Alive); } } }

    private bool Validate(ulong handle, out uint index)
    {
        index = (uint)(handle & 0xFFFFFFFF);
        uint gen = (uint)(handle >> 32);
        if (index == 0 || index >= _slots.Count) return false;
        var s = _slots[(int)index];
        return s.Alive && s.Generation == gen;
    }
}
```

- Modify: `VisualGameStudio.Tests/VisualGameStudio.Tests.csproj` — add (all three items, unconditionally):
```xml
<ItemGroup>
  <ProjectReference Include="TestAssets\BlnetTestShim\BlnetTestShim.csproj" />
  <!-- The test csproj uses default SDK globs with NO existing Compile excludes: without this
       Remove, the shim's sources (and its nested obj\ generated AssemblyInfo files) are
       double-compiled into the test assembly -> CS0436 type conflicts + CS0579 duplicate
       attributes. Required, not conditional. -->
  <Compile Remove="TestAssets\**" />
  <!-- Task 9's fixture reads main.cpp.txt from TestContext.CurrentContext.TestDirectory;
       'Update' (not 'Include') because a .txt is already a default None item. Harmless
       MSBuild no-op until Task 9 creates the file. -->
  <None Update="TestAssets\BlnetHarness\main.cpp.txt" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

- Create: `VisualGameStudio.Tests/Blnet/HandleTableTests.cs` — fast tests (no Category), TDD order:

```csharp
using BlnetTestShim;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

[TestFixture]
public class HandleTableTests
{
    [Test]
    public void Create_TryGet_RoundTrips()
    {
        var t = new HandleTable();
        var obj = new List<int> { 1, 2, 3 };
        var h = t.Create(obj);
        Assert.That(h, Is.Not.Zero);
        Assert.That(t.TryGet(h, out var got), Is.EqualTo(BlnetStatus.BLNET_OK));
        Assert.That(got, Is.SameAs(obj));
    }

    [Test]
    public void Release_ThenUse_IsStale_NotCorruption()
    {
        var t = new HandleTable();
        var h = t.Create(new object());
        Assert.That(t.Release(h), Is.EqualTo(BlnetStatus.BLNET_OK));
        Assert.That(t.TryGet(h, out _), Is.EqualTo(BlnetStatus.BLNET_E_STALE_HANDLE));
        Assert.That(t.Release(h), Is.EqualTo(BlnetStatus.BLNET_E_STALE_HANDLE)); // double release
    }

    [Test]
    public void AddRef_KeepsAlive_UntilLastRelease()
    {
        var t = new HandleTable();
        var h = t.Create(new object());
        Assert.That(t.AddRef(h), Is.EqualTo(BlnetStatus.BLNET_OK));
        Assert.That(t.Release(h), Is.EqualTo(BlnetStatus.BLNET_OK));
        Assert.That(t.TryGet(h, out _), Is.EqualTo(BlnetStatus.BLNET_OK)); // still alive
        Assert.That(t.Release(h), Is.EqualTo(BlnetStatus.BLNET_OK));
        Assert.That(t.TryGet(h, out _), Is.EqualTo(BlnetStatus.BLNET_E_STALE_HANDLE));
    }

    [Test]
    public void GenerationReuse_OldHandleStillFails()
    {
        var t = new HandleTable();
        var h1 = t.Create(new object());
        t.Release(h1);
        var h2 = t.Create(new object());       // reuses slot index 1
        Assert.That((uint)(h2 & 0xFFFFFFFF), Is.EqualTo((uint)(h1 & 0xFFFFFFFF)), "slot must be reused for this test to bite");
        Assert.That(t.TryGet(h1, out _), Is.EqualTo(BlnetStatus.BLNET_E_STALE_HANDLE));
        Assert.That(t.TryGet(h2, out _), Is.EqualTo(BlnetStatus.BLNET_OK));
    }

    [Test]
    public void ZeroHandle_IsAlwaysStale()
    {
        var t = new HandleTable();
        Assert.That(t.TryGet(0, out _), Is.EqualTo(BlnetStatus.BLNET_E_STALE_HANDLE));
    }

    [Test]
    public void Concurrency_ParallelCreateReleaseHammer_NoCorruption()
    {
        var t = new HandleTable();
        Parallel.For(0, 8, _ =>
        {
            for (int i = 0; i < 5_000; i++)
            {
                var h = t.Create(i);
                Assert.That(t.TryGet(h, out var v), Is.EqualTo(BlnetStatus.BLNET_OK));
                Assert.That(v, Is.EqualTo(i));
                Assert.That(t.Release(h), Is.EqualTo(BlnetStatus.BLNET_OK));
            }
        });
        Assert.That(t.AliveCount, Is.Zero);
    }
}
```

Plus the enum drift test (append to `BlnetContractTests.cs`):
```csharp
[Test]
public void ShimStatusEnum_MatchesContract()
{
    foreach (var (name, value, _) in BlnetContract.StatusCodes)
        Assert.That((int)Enum.Parse<BlnetTestShim.BlnetStatus>(name), Is.EqualTo(value),
            "BlnetStatus.cs drifted — regenerate from BlnetContract.GenerateStatusEnumCs()");
    Assert.That(BlnetTestShim.ShimAbi.AbiVersion, Is.EqualTo(BlnetContract.AbiVersion),
        "ShimAbi.AbiVersion drifted from BlnetContract.AbiVersion");
}
```

- [ ] **Step 1: Write tests → red (types missing) → implement csproj/enum/table → green.**
- [ ] **Step 2: Fast subset green** (proves the ProjectReference didn't break the suite).
- [ ] **Step 3: Commit** — `git commit -m "feat(blnet): test shim project + generation-tagged HandleTable with fast unit tests"`

---

### Task 8: Shim exports + AOT publish smoke

**Files:**
- Create: `VisualGameStudio.Tests/TestAssets/BlnetTestShim/Exports.cs`

Contract exports (every body wrapped in the non-throwing catch-all per C4):

> **Execution deviation (Task 8 review):** Exports.cs as committed hardens three spots beyond this code block — null-vtable guard + try/catch in Initialize, try/catch inside TestInvokeFromThread's thread lambda, and unconditional error-slot reset on the native-exception path in TestInvoke. Rationale: an exception escaping [UnmanagedCallersOnly] under AOT is FailFast; an unhandled exception on a spawned thread kills the test host.

```csharp
using System.Runtime.InteropServices;
using System.Text;

namespace BlnetTestShim;

public static unsafe class Exports
{
    internal static readonly HandleTable Table = new();
    private static delegate* unmanaged[Cdecl]<ulong, ulong*, int, ulong*, int> _thunk;
    private static delegate* unmanaged[Cdecl]<byte**, int> _getNativeError;

    [ThreadStatic] private static string? _lastErrorType;
    [ThreadStatic] private static string? _lastErrorMessage;

    private static int Fail(Exception ex)
    {
        try { _lastErrorType = ex.GetType().FullName; _lastErrorMessage = ex.Message; }
        catch { /* C4: the handler itself must be non-throwing; degrade to status-only */ }
        return (int)BlnetStatus.BLNET_E_MANAGED_EXCEPTION;
    }

    private static byte* AllocUtf8(string s)
    {
        var bytes = Encoding.UTF8.GetByteCount(s);
        var buf = (byte*)NativeMemory.Alloc((nuint)(bytes + 1));
        fixed (char* c = s) Encoding.UTF8.GetBytes(c, s.Length, buf, bytes);
        buf[bytes] = 0;
        return buf;
    }
    private static string? Utf8ToString(byte* p) => p == null ? null : Marshal.PtrToStringUTF8((nint)p);

    [UnmanagedCallersOnly(EntryPoint = "blnet_abi_version", CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    public static int AbiVersion() => ShimAbi.AbiVersion; // single source: drift-tested against BlnetContract.AbiVersion

    [UnmanagedCallersOnly(EntryPoint = "blnet_initialize", CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    public static int Initialize(int expectedAbi, void* vtable)
    {
        if (expectedAbi != ShimAbi.AbiVersion) return (int)BlnetStatus.BLNET_E_VERSION_MISMATCH;
        var vt = (void**)vtable;
        _thunk = (delegate* unmanaged[Cdecl]<ulong, ulong*, int, ulong*, int>)vt[0];
        _getNativeError = (delegate* unmanaged[Cdecl]<byte**, int>)vt[1];
        return (int)BlnetStatus.BLNET_OK;
    }

    [UnmanagedCallersOnly(EntryPoint = "blnet_addref", CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    public static int AddRef(ulong h) { try { return (int)Table.AddRef(h); } catch (Exception ex) { return Fail(ex); } }

    [UnmanagedCallersOnly(EntryPoint = "blnet_release", CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    public static int Release(ulong h) { try { return (int)Table.Release(h); } catch (Exception ex) { return Fail(ex); } }

    [UnmanagedCallersOnly(EntryPoint = "blnet_alloc", CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    public static void* Alloc(long size) { try { return NativeMemory.Alloc((nuint)size); } catch { return null; } }

    [UnmanagedCallersOnly(EntryPoint = "blnet_free", CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    public static void Free(void* p) { if (p != null) NativeMemory.Free(p); }

    [UnmanagedCallersOnly(EntryPoint = "blnet_last_error", CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    public static int LastError(byte** typeName, byte** message)
    {
        try
        {
            if (typeName != null) *typeName = _lastErrorType is null ? null : AllocUtf8(_lastErrorType);
            if (message != null) *message = _lastErrorMessage is null ? null : AllocUtf8(_lastErrorMessage);
            return (int)BlnetStatus.BLNET_OK;
        }
        catch (Exception ex) { return Fail(ex); }
    }

    // ---- test exports (drive conformance scenarios; NOT part of the contract) ----

    [UnmanagedCallersOnly(EntryPoint = "blnet_test_create_list", CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    public static int TestCreateList(ulong* outHandle)
    { try { *outHandle = Table.Create(new List<int>()); return (int)BlnetStatus.BLNET_OK; } catch (Exception ex) { return Fail(ex); } }

    [UnmanagedCallersOnly(EntryPoint = "blnet_test_list_add", CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    public static int TestListAdd(ulong h, int value)
    {
        try
        {
            var st = Table.TryGet(h, out var o);
            if (st != BlnetStatus.BLNET_OK) return (int)st;
            ((List<int>)o!).Add(value);
            return (int)BlnetStatus.BLNET_OK;
        }
        catch (Exception ex) { return Fail(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "blnet_test_list_count", CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    public static int TestListCount(ulong h, int* outCount)
    {
        try
        {
            var st = Table.TryGet(h, out var o);
            if (st != BlnetStatus.BLNET_OK) return (int)st;
            *outCount = ((List<int>)o!).Count;
            return (int)BlnetStatus.BLNET_OK;
        }
        catch (Exception ex) { return Fail(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "blnet_test_echo", CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    public static int TestEcho(byte* input, byte** output)
    { try { *output = AllocUtf8(Utf8ToString(input) ?? ""); return (int)BlnetStatus.BLNET_OK; } catch (Exception ex) { return Fail(ex); } }

    [UnmanagedCallersOnly(EntryPoint = "blnet_test_throw", CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    public static int TestThrow()
    { try { throw new ArgumentException("bøøm from managed"); } catch (Exception ex) { return Fail(ex); } }

    [UnmanagedCallersOnly(EntryPoint = "blnet_test_invoke", CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    public static int TestInvoke(ulong cb, ulong* args, int argc, ulong* result)
    {
        try
        {
            int st = _thunk(cb, args, argc, result);
            if (st == (int)BlnetStatus.BLNET_E_NATIVE_EXCEPTION && _getNativeError != null)
            {
                byte* msg = null;
                if (_getNativeError(&msg) == (int)BlnetStatus.BLNET_OK && msg != null)
                { _lastErrorType = "BasicLangNativeException"; _lastErrorMessage = Utf8ToString(msg); NativeMemory.Free(msg); /* == blnet_free's allocator: the buffer came from blnet_alloc */ }
            }
            return st;
        }
        catch (Exception ex) { return Fail(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "blnet_test_invoke_from_thread", CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    public static int TestInvokeFromThread(ulong cb, ulong* args, int argc)
    {
        try
        {
            // Copy args: the .NET thread outlives this frame's pointers validity window otherwise.
            var local = new ulong[argc];
            for (int i = 0; i < argc; i++) local[i] = args[i];
            int st = 0;
            // NB: 'fixed' over a ZERO-length array yields a null pointer — fine for argc == 0
            // (the thunk never dereferences args then); do not "fix" this.
            var t = new Thread(() => { fixed (ulong* p = local) st = _thunk(cb, p, argc, null); });
            t.Start(); t.Join();
            return st; // cross-thread: queued (notification) / BLNET_E_CROSS_THREAD_RESULT (result-bearing)
        }
        catch (Exception ex) { return Fail(ex); }
    }
}
```

- Test: append to `BlnetConformanceTests.cs` (created in Task 9) OR run the publish once here as its own Integration test:

```csharp
[TestFixture]
[Category("Integration")]
public class BlnetShimPublishTests
{
    [Test]
    public void ShimPublishesUnderNativeAot()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("AOT shim conformance is Windows-only (LoadLibrary harness)");
        var dll = BlnetShimPublisher.PublishOnce();   // helper below, reused by Task 9
        Assert.That(File.Exists(dll), dll);
    }
}
```

`BlnetShimPublisher` (in `VisualGameStudio.Tests/Blnet/`): locates the repo root by walking up from `TestContext.CurrentContext.TestDirectory` until `VisualGameStudioEngine.sln` is found; runs `dotnet publish <shim.csproj> -c Release -r win-x64 -p:PublishAot=true -p:NativeLib=Shared -o <scratch>` (10-minute timeout, capture output into the assert message); returns `<scratch>/BlnetTestShim.dll`; caches the result in a static so multiple fixtures publish once per test run.

- [ ] **Step 1: Write `Exports.cs` + publisher + publish test; run the Integration publish test; iterate until AOT publish succeeds with zero AOT warnings in the output** (assert `!output.Contains("AOT analysis")` is too brittle — instead read the output and fail on `warning IL` occurrences: `Assert.That(output, Does.Not.Contain("warning IL"))`).
- [ ] **Step 2: Fast subset still green. Commit** — `git commit -m "feat(blnet): shim contract+test exports; AOT NativeLib publish smoke (warning-free)"`

---

### Task 9: Conformance harness + scenarios 1–6 (handles, strings, exceptions)

**Files:**
- Create: `VisualGameStudio.Tests/TestAssets/BlnetHarness/main.cpp.txt` (checked in as text; the fixture writes it beside the generated headers)
- Create: `VisualGameStudio.Tests/Blnet/BlnetConformanceTests.cs`

**Harness skeleton** (complete this file as scenarios are added; scenario = `argv[1]`; prints `PASS <name>` or `FAIL <name>: <detail>`; exit 0 on pass, 1 on fail):

```cpp
#include "blnet.h"
#include "blnet_runtime.hpp"
#include <windows.h>
#include <cstdio>
#include <cstring>
#include <string>
#include <thread>

using namespace BasicLang::blnet;

// Test-export signatures (beyond the contract, shim test surface)
static int32_t (BLNET_CALL *test_create_list)(uint64_t*);
static int32_t (BLNET_CALL *test_list_add)(uint64_t, int32_t);
static int32_t (BLNET_CALL *test_list_count)(uint64_t, int32_t*);
static int32_t (BLNET_CALL *test_echo)(const char*, char**);
static int32_t (BLNET_CALL *test_throw)(void);
static int32_t (BLNET_CALL *test_invoke)(uint64_t, uint64_t*, int32_t, uint64_t*);
static int32_t (BLNET_CALL *test_invoke_from_thread)(uint64_t, uint64_t*, int32_t);
static int32_t (BLNET_CALL *shim_initialize)(int32_t, const BlnetNativeVtable*);
static int32_t (BLNET_CALL *shim_abi_version)(void);

#define REQUIRE(cond, name, detail) \
    do { if (!(cond)) { printf("FAIL %s: %s\n", name, detail); return 1; } } while (0)

template <typename T> static void bind(HMODULE m, T& fn, const char* name) {
    fn = reinterpret_cast<T>(GetProcAddress(m, name));
    if (!fn) { printf("FAIL bind: missing export %s\n", name); exit(2); }
}

static int scenario_handle_roundtrip() { /* test 1 */
    uint64_t h = 0;
    { BlnetCallScope s; NetCheck(test_create_list(&h)); }
    NetRef ref(h);
    { BlnetCallScope s; NetCheck(test_list_add(ref.get(), 42)); }
    int32_t count = 0;
    { BlnetCallScope s; NetCheck(test_list_count(ref.get(), &count)); }
    REQUIRE(count == 1, "handle_roundtrip", "count != 1");
    printf("PASS handle_roundtrip\n");
    return 0;
}
// ... one function per scenario; main dispatches on argv[1] ...
```

**Fixture:**

```csharp
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class BlnetConformanceTests
{
    private static string? _harnessExe;
    private static string? _workDir;

    [OneTimeSetUp]
    public void PublishAndCompileOnce()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("Windows-only (LoadLibrary + win-x64 AOT)");
        var compiler = Native.CppCompile.FindRunCompiler();
        if (compiler is null) Assert.Ignore("No C++ compiler available");
        var shimDll = BlnetShimPublisher.PublishOnce();

        _workDir = Path.Combine(Path.GetTempPath(), "blnet_conf_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
        File.WriteAllText(Path.Combine(_workDir, "blnet.h"), BlnetRuntimeSources.BlnetHeader);
        File.WriteAllText(Path.Combine(_workDir, "blnet_runtime.hpp"), BlnetRuntimeSources.BlnetRuntime);
        var mainCpp = File.ReadAllText(Path.Combine(
            TestContext.CurrentContext.TestDirectory, "TestAssets", "BlnetHarness", "main.cpp.txt"));
        File.WriteAllText(Path.Combine(_workDir, "main.cpp"), mainCpp);
        _harnessExe = CompileHarness(compiler.Value, _workDir);          // format argsTemplate, assert exit 0
        File.Copy(shimDll, Path.Combine(_workDir, "BlnetTestShim.dll"));
    }

    private static string RunScenario(string name)
    {
        // Process.Start harness with argument = name, WorkingDirectory = _workDir, 60s timeout;
        // assert exit code 0 with stdout+stderr in the failure message; return stdout.
    }

    [TestCase("handle_roundtrip")]        // spec test 1
    [TestCase("stale_handle")]            // spec test 2
    [TestCase("double_release_addref")]   // spec test 3
    [TestCase("generation_reuse")]        // spec test 4
    [TestCase("string_roundtrip")]        // spec test 5
    [TestCase("managed_exception")]       // spec test 6
    public void Conformance(string scenario) =>
        Assert.That(RunScenario(scenario), Does.StartWith("PASS " + scenario));
}
```

Scenario bodies (write each, run, iterate):
1. **handle_roundtrip** — above.
2. **stale_handle** — create, `blnet_release` once via NetRef destruction, then `test_list_add` on the dead handle → expect `BLNET_E_STALE_HANDLE` returned (NOT a crash).
3. **double_release_addref** — `blnet_addref` then two releases OK, third → stale; `test_list_count` between releases proves aliveness matches refcount.
4. **generation_reuse** — release; `test_create_list` again (slot reuse); old handle still stale, new handle works.
5. **string_roundtrip** — `test_echo("h\xC3\xA9llo w\xC3\xB6rld \xE2\x9C\x93")` → returned buffer equal byte-for-byte; free via `blnet_free`. (Narrow literal with UTF-8 byte escapes — not `u8"..."`, which is `const char8_t*` in C++20; the escapes also remove any MSVC source-encoding dependency.)
6. **managed_exception** — `test_throw()` → `BLNET_E_MANAGED_EXCEPTION`; `blnet_last_error` yields type containing `ArgumentException` and message containing `bøøm`; NetCheck path: wrap in try/catch and assert the C++ exception message carries both.

- [ ] **Steps: write fixture + harness scenarios 1–6, red → green one scenario at a time. Commit** — `git commit -m "test(blnet): ABI conformance scenarios 1-6 (handles, strings, exceptions) against the AOT shim"`

---

### Task 10: Conformance scenarios 7–13 (callbacks, pump)

Extend `main.cpp.txt` + `[TestCase]`s. Register callbacks with `blnet_register_callback` and pass the handle to the shim's `test_invoke` / `test_invoke_from_thread`:

7. **inline_native_exception** — inline notification whose body throws `std::runtime_error("nätive boom")`; `test_invoke` (inside `BlnetCallScope`) → `BLNET_E_NATIVE_EXCEPTION`; `blnet_last_error` message contains `nätive boom` (the shim pulled it via `get_native_error`).
8. **inline_result_and_out** — result-bearing callback: 2 args (`VALUE`, `OUT` slot); returns arg0*2 in `result` and writes arg0+1 through the out-slot pointer. Invoke inline → both values correct.
9. **struct_slots** — callback taking a 16-byte struct (`{ double x; double y; }`) via `STRUCT` slot and returning one via an out-buffer in `result`; assert bit-exact round-trip.
10. **queued_notification_addref** — notification with one `HANDLE` slot; `test_invoke_from_thread` → returns OK, callback NOT yet run; release the native `NetRef` (queue still holds its addref); `blnet_pump()` → callback ran, its `test_list_count` on the handle succeeded (object was alive), pump returned `BLNET_OK`.
11. **cross_thread_result_rejected** — result-bearing, non-Immediate, `test_invoke_from_thread` → `BLNET_E_CROSS_THREAD_RESULT`, callback never ran, nothing queued (`blnet_pump()` → OK, flag still unset).
12. **pump_error_surfacing** — queue three notifications (2nd and 3rd throw); install hook; pump → hook fired twice, return status `BLNET_E_NATIVE_EXCEPTION`, first callback's effect present.
13. **deadlock_guard** — from the pump thread, call `test_invoke_from_thread` (shim spawns a thread that invokes → queued) and RETURN (join happens shim-side; enqueue is fire-and-forget so the join cannot deadlock); flag unset; `blnet_pump()` → flag set. PASS iff no hang (the 60s harness timeout is the hang detector).

- [ ] **Steps: scenario at a time, red → green. Commit** — `git commit -m "test(blnet): ABI conformance scenarios 7-13 (duplex callbacks, queue, pump)"`

---

### Task 11: Conformance scenarios 14–16 + version mismatch

14. **version_mismatch** — call `blnet_initialize(BLNET_ABI_VERSION + 998, &vtable)` FIRST → expect `BLNET_E_VERSION_MISMATCH`; then `blnet_initialize(BLNET_ABI_VERSION, ...)` → OK, and `REQUIRE(shim_abi_version() == BLNET_ABI_VERSION, ...)`. (The harness's normal startup init also uses `BLNET_ABI_VERSION` — never a literal — so a `BlnetContract.AbiVersion` bump mechanically breaks the drift test AND the conformance suite until both sides regenerate, which is exactly C7's intent. Each scenario is a fresh process, so the failed init can't poison others.)
15. **concurrency_hammer** — 8 `std::thread`s × 2,000 iterations of create/add/count/release through the shim (each thread wraps calls in `BlnetCallScope`); zero non-OK statuses; final create/release still OK.
16. **stale_callback_via_release** — register, `blnet_callback_release`, `test_invoke` inline → `BLNET_E_STALE_CALLBACK`; release again → `BLNET_E_STALE_CALLBACK`.

- [ ] **Steps: red → green each. Then run the FULL conformance fixture end-to-end:**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~BlnetConformanceTests|FullyQualifiedName~BlnetShimPublishTests|FullyQualifiedName~BlnetNativeRuntimeTests" > test-run.txt 2>&1`
Expected: all green.

- [ ] **Commit** — `git commit -m "test(blnet): ABI conformance scenarios 14-16 - version fail-fast, concurrency, stale callback"`

---

### Task 12: Full verification + closeout

- [ ] **Step 1: Fast subset** — `dotnet test ... --filter "TestCategory!=Integration" > test-run.txt 2>&1` → same pass count as the pre-plan baseline (record the baseline number in Task 3; BL6009 flake exit-1 is known-normal).
- [ ] **Step 2: Full Blnet Integration set** (Task 11's filter) → all green.
- [ ] **Step 3: Update the spec's status line** — change `**Status:** Approved design, pre-implementation` to `**Status:** Implemented (conformance suite: VisualGameStudio.Tests/Blnet/) — P1/P2 pending`.
- [ ] **Step 4: Commit** — `git commit -m "docs(blnet): mark boundary contract v1 implemented; conformance suite is the P2 acceptance gate"`
- [ ] **Step 5:** Use @superpowers:verification-before-completion before reporting done. Report: pass counts, publish duration, any scenario that needed spec-relevant adjustment (if a conformance scenario forced a CONTRACT change — not a bug fix — stop and surface it: the spec must be amended first, ABI version bumped if the change is post-P2-ABI).

**Known risks the executor should expect:**
- AOT publish needs VC++ link.exe — present on this machine (the engine's vcxproj builds via vswhere), but the first publish downloads ILCompiler NuGet packages (~1 min network).
- `CallConvCdecl` on `[UnmanagedCallersOnly]` is correct on x64 (the CallConv is a no-op there but keeps the header honest for x86).
- If g++/winlibs is the found compiler and `std::thread` fails to link, prefer MSVC (reorder in a local probe) — do NOT modify `CppCompile.FindRunCompiler()` for this; write a local probe in the fixture if needed.
- Non-ASCII string content in the harness/tests MUST use `\xNN`-escaped narrow literals (as the scenarios above already do): C++20 makes `u8"..."` a `const char8_t*` (a hard compile error against `const char*` on clang, g++, AND MSVC), and raw non-ASCII source bytes would additionally need MSVC's `/utf-8`. The escape form sidesteps both.
