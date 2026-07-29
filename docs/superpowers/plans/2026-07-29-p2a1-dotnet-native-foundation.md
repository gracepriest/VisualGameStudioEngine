# P2a-1 — .NET-in-Native Foundation Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build every transport-neutral component needed for .NET class access from Native (BL+C++) projects, **without changing the behavior of a single existing program** — so P2a-2's flip is a small, reviewable commit rather than a big bang.

**Architecture:** Reference closure → Roslyn-backed type resolution → deterministic mangling → IR carriage → generated shim + native proxies, with the build pipeline restructured into phases. In P2a-1 the resolver runs **warning-only on both backends**, the IR carriage is **read by nobody**, and the proxy emitter is keyed on an **always-empty surface**. Everything is observable and tested; nothing is load-bearing yet.

**Tech Stack:** C# / .NET 8, NUnit 4 (constraint asserts), Roslyn (`Microsoft.CodeAnalysis.CSharp` 4.9.2), Native AOT (`dotnet publish /p:PublishAot=true`), clang++/g++/MSVC via `CppToolchain`.

**Spec:** `docs/superpowers/specs/2026-07-29-p2a-dotnet-access-aot-shim-design.md` — §17 defines this split. Section references below (§n) are to that spec.

---

## Conventions — read before Task 1

These are repo laws. Violating them wastes a build cycle or corrupts files.

- **NEVER build `VisualGameStudioEngine.sln`.** Its BasicLang entry (line 17) points at
  `..\BasicLang\BasicLang\BasicLang.csproj` — a **different repository** that exists on this
  machine. Always build per project:
  ```bash
  dotnet build BasicLang/BasicLang.csproj -c Release
  ```
- **Never round-trip repo files through PowerShell `Get-Content`/`Set-Content`** — it corrupts the
  BOM-less UTF-8 files here. Use Read/Edit/Write. Multi-line commit messages go through a file +
  `git commit -F`.
- **Redirect test runs to a file** — the suite exceeds tool output truncation:
  ```bash
  dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "TestCategory!=Integration" > test-run.txt 2>&1
  ```
  Then read `test-run.txt`. Give the tool a large timeout (600000 ms) for Integration fixtures.
- **Baseline: fast subset is `Passed: 3608, Failed: 0, Skipped: 1` at `dfee728`.** Every task must
  leave it at 3608 + whatever that task added. A drop is a regression, not noise.
- **Asserts are NUnit 4 constraint style only** — `Assert.That(x, Is.EqualTo(y))`, `Does.Contain`,
  `Assert.Throws<T>`. A repo-wide grep for `Assert.AreEqual|Assert.IsTrue|…` returns **zero** hits
  and `NUnit.Analyzers` 4.0.1 will flag classic asserts.
- **`[Category("Integration")]`** goes on any fixture that compiles native code, spawns a process,
  or publishes AOT. Put it per-`[Test]` when a fixture mixes fast string pins with compile-and-run
  cases (`CppBclRuntimeTests` is the model). Add `[NonParallelizable]` to process-spawning fixtures.
- **Missing toolchain = `Assert.Ignore(...)`, never a failure.** Hoist expensive probes into
  `[OneTimeSetUp]` (`BclBackendParityTests.cs:116-121` is the precedent).
- **Temp dirs:** `Path.Combine(Path.GetTempPath(), "<prefix>-" + Guid.NewGuid().ToString("N"))`,
  deleted in `finally`/`[TearDown]` with a 3× / 200 ms retry that swallows the final failure.
- **New product code** goes in `BasicLang/Net/` and `BasicLang/Compiler/CodeGen/Net/` with
  **block-scoped** namespaces (`BasicLang.Net`, `BasicLang.Compiler.CodeGen.Net`), matching the six
  files in `Compiler/CodeGen/CPlusPlus/`. No csproj change is needed — the default glob picks them
  up (the only `<Compile Remove>` is `GeneratedCode\**`).
- **New tests** go in `VisualGameStudio.Tests/Blnet/` with a **file-scoped** namespace
  (`namespace VisualGameStudio.Tests.Blnet;`). Tests group by *subsystem*, not source path — this
  is why `BlnetContract` lives in `Compiler/CodeGen/CPlusPlus/` but its tests live in `Blnet/`.
- **`BasicLang` has `InternalsVisibleTo VisualGameStudio.Tests`** — new types may be `internal` and
  still be unit-tested directly. Prefer `internal`.
- **Every drift/invariant test's failure message must name which side to fix**, e.g.
  *"…drifted — update the registry, not a parallel list"*.
- **Validate codegen through BOTH** `BclE2E.CompileToCppOptimized` (optimizer-running helper, at
  `VisualGameStudio.Tests/Compiler/CppBclEndToEndTests.cs:47`) **and** the shipped CLI
  (`CliTestHarness.RunCli`). A lowering verified through only one is not verified.
- **Spawn child processes via `CliTestHarness.RunProcess`**, never hand-rolled — it drains both
  streams asynchronously so `WaitForExit(timeout)` is a real hang detector.
- **Filter trap:** several fixtures live *inside* another file (`CppDecimalRuntimeTests` inside
  `CppBclRuntimeTests.cs`, `BlnetRuntimeSourcesTests`/`BoundaryTypeRegistryTests` inside
  `BlnetContractTests.cs`). A sweep needs its own `FullyQualifiedName~` clause per fixture.

### Path corrections to the spec

The spec cites some files by bare name. The real paths:

| Spec says | Actually at |
|---|---|
| `ProjectFile.cs` | `BasicLang/ProjectSystem/ProjectFile.cs` |
| `CppProjectBuilder.cs` | `BasicLang/ProjectSystem/CppProjectBuilder.cs` |
| `BuildService.cs` | `VisualGameStudio.ProjectSystem/Services/BuildService.cs` |
| `TypeRegistry.cs` | `BasicLang/TypeRegistry.cs` |
| `CppCapabilityChecker.cs` | `BasicLang/CppCapabilityChecker.cs` (repo **root** of BasicLang/) |

---

## File structure

**New product files**

| Path | Responsibility |
|---|---|
| `BasicLang/Net/NetReferenceResolver.cs` | `.blproj` → assembly closure; BL6021/BL6022 |
| `BasicLang/Net/NetTypeResolver.cs` | Roslyn `Compilation` wrapper: type existence, members, overload resolution |
| `BasicLang/Net/NetAmbientNamespaces.cs` | the single ambient-namespace constant shared with `CSharpBackend` |
| `BasicLang/Net/NetClaimPredicate.cs` | §6.5's three-source "claimed by native handling" predicate |
| `BasicLang/Net/NetNameMangler.cs` | deterministic export names |
| `BasicLang/Net/NetSurface.cs` | the discovered-surface model (empty in P2a-1) |
| `BasicLang/Compiler/CodeGen/Net/BlnetShimSources.cs` | shim's fixed C# scaffolding as string constants |
| `BasicLang/Compiler/CodeGen/Net/NetProxyEmitter.cs` | proxy header + binding table + startup |
| `BasicLang/Compiler/CodeGen/Net/NetShimGenerator.cs` | emits the shim csproj + `Exports.g.cs` |
| `BasicLang/Compiler/CodeGen/Net/NetShimPublisher.cs` | AOT publish + the VS-Installer PATH workaround |
| `BasicLang/Compiler/CodeGen/Net/NetShimCache.cs` | content-hash key + manifest |
| `BasicLang/Compiler/CodeGen/Net/AotDiagnosticMapper.cs` | ILC diagnostics → BL6020 |

**Modified**

| Path | Change |
|---|---|
| `BasicLang/BasicLang.csproj` | + Roslyn 4.9.2, + `SatelliteResourceLanguages` |
| `BasicLang/Program.cs:436` | resolve references before the native early-return |
| `VisualGameStudio.ProjectSystem/Services/BuildService.cs:449` | same on the IDE path |
| `BasicLang/ProjectSystem/CppProjectBuilder.cs` | phase model, gate merge, `CancellationToken` |
| `BasicLang/SemanticAnalyzer.cs` | resolver wired warning-only; `ConfigureTypeRegistry` into `CompileUnit` |
| `BasicLang/TypeRegistry.cs` | `Assembly.LoadFrom` → `NetTypeResolver` |
| `BasicLang/CSharpBackend.cs` | ambient namespaces read from the shared constant |
| `BasicLang/IRNodes.cs`, `BasicLang/IRBuilder.cs` | resolved-target + category-marker carriage |
| `BasicLang/IROptimizer.cs` | preserve the new IR fields |

**New tests** — all in `VisualGameStudio.Tests/Blnet/`:
`NetReferenceResolverTests.cs`, `NetTypeResolverTests.cs`, `NetAmbientNamespaceTests.cs`,
`NetClaimPredicateTests.cs`, `NetNameManglerTests.cs`, `NetIrCarriageTests.cs`,
`BlnetShimSourcesTests.cs`, `NetProxyEmitterTests.cs`, `NetShimGeneratorTests.cs`,
`NetShimCacheTests.cs`, `AotDiagnosticMapperTests.cs`, plus additions to
`VisualGameStudio.Tests/Compiler/CppProjectCliBuildTests.cs`.

---

## Task 1: Measure the AOT publish cost

Spec §15.1 records this as unmeasured **anywhere in the repo**, and §10.2's cache design rests on
it. Measure before building on it.

**Files:**
- Modify: `docs/superpowers/specs/2026-07-29-p2a-dotnet-access-aot-shim-design.md` (§15.1)

- [ ] **Step 1: Cold-cache publish, timed**

```bash
dotnet nuget locals all --clear
```

Then, from the repo root, time a publish of the existing hand-written shim:

```bash
dotnet publish VisualGameStudio.Tests/TestAssets/BlnetTestShim/BlnetTestShim.csproj -c Release -r win-x64 -p:PublishAot=true -p:NativeLib=Shared -o "$env:TEMP/blnet_cold" > publish-cold.txt 2>&1
```

Record wall-clock. Expected: several minutes (first run downloads ILCompiler).

- [ ] **Step 2: Warm-cache publish, timed**

```bash
dotnet publish VisualGameStudio.Tests/TestAssets/BlnetTestShim/BlnetTestShim.csproj -c Release -r win-x64 -p:PublishAot=true -p:NativeLib=Shared -o "$env:TEMP/blnet_warm" > publish-warm.txt 2>&1
```

Record wall-clock. This is the number that matters — it is what an uncached build would pay
**every time**.

- [ ] **Step 3: Record the finding in the spec**

Replace §15.1's row with the measured cold and warm numbers and a one-sentence verdict: does
§10.2's content-hash cache suffice, or does §17 need a background pre-warm task moved earlier?

- [ ] **Step 4: Commit**

```bash
git add docs/superpowers/specs/2026-07-29-p2a-dotnet-access-aot-shim-design.md
git commit -m "docs(p2a): measure AOT publish wall-clock; close spec open item 15.1"
```

> **If warm publish exceeds ~60 s**, stop and surface to the human before Task 15 — the cache
> becomes load-bearing for F5 usability rather than a nicety, and §17 may need reordering.

---

## Task 2: Move the shim publisher into the product

`BlnetShimPublisher` lives in the **test assembly** and carries the VS-Installer PATH workaround
that §10.5 says must ship. Nothing in P2a can publish a shim until it moves. Ordering constraint
from §17: this must precede any publisher integration test on this machine.

**Files:**
- Create: `BasicLang/Compiler/CodeGen/Net/NetShimPublisher.cs`
- Modify: `VisualGameStudio.Tests/Blnet/BlnetShimPublisher.cs` (becomes a thin test-only wrapper)
- Test: `VisualGameStudio.Tests/Blnet/NetShimPublisherTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;
using BasicLang.Compiler.CodeGen.Net;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// Pins the publisher's environment hardening. The PATH workaround (spec §10.5) is the reason
/// the first native build on a machine with NoDefaultCurrentDirectoryInExePath=1 succeeds;
/// without it ILCompiler's linker discovery corrupts CppLinker and the build fails MSB3073
/// exit 123, looking like a P2a bug rather than an environment one.
/// </summary>
[TestFixture]
public class NetShimPublisherTests
{
    [Test]
    public void BuildPublishArguments_UsesTheProvenRecipe()
    {
        var args = NetShimPublisher.BuildPublishArguments("C:\\proj\\shim.csproj", "C:\\out", "win-x64");

        Assert.That(args, Is.EqualTo(new[]
        {
            "publish", "C:\\proj\\shim.csproj",
            "-c", "Release",
            "-r", "win-x64",
            "-p:PublishAot=true",
            "-p:NativeLib=Shared",
            "-o", "C:\\out",
        }), "Publish recipe drifted from the P0-proven one (spec §8.1) — update the spec too if this is intentional.");
    }

    [Test]
    public void HardenChildPath_AppendsVsInstallerWhenPresent()
    {
        var env = new Dictionary<string, string?> { ["PATH"] = "C:\\existing" };

        NetShimPublisher.HardenChildPath(env, vsInstallerDir: "C:\\VS\\Installer", installerExists: true);

        Assert.That(env["PATH"], Is.EqualTo("C:\\existing;C:\\VS\\Installer"));
    }

    [Test]
    public void HardenChildPath_LeavesPathAloneWhenInstallerMissing()
    {
        var env = new Dictionary<string, string?> { ["PATH"] = "C:\\existing" };

        NetShimPublisher.HardenChildPath(env, vsInstallerDir: "C:\\nope", installerExists: false);

        Assert.That(env["PATH"], Is.EqualTo("C:\\existing"));
    }
}
```

- [ ] **Step 2: Run it and verify it fails**

```bash
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~NetShimPublisherTests" > test-run.txt 2>&1
```

Expected: **build error** — `NetShimPublisher` does not exist.

- [ ] **Step 3: Create the product publisher**

Create `BasicLang/Compiler/CodeGen/Net/NetShimPublisher.cs`. Port `PublishCore` from
`VisualGameStudio.Tests/Blnet/BlnetShimPublisher.cs:24-93` with three changes:

1. **Extract the two testable seams** as internal statics — `BuildPublishArguments(csproj, outDir, rid)`
   returning `string[]`, and `HardenChildPath(IDictionary<string,string?> env, string vsInstallerDir, bool installerExists)`.
2. **Drop the `Lazy` and `TestContext` dependency.** The product version takes explicit paths;
   `FindRepoRoot` (`:97-104`) does not move — it is test-only.
3. **Return a result record** rather than throwing for a non-zero exit, so `AotDiagnosticMapper`
   (Task 16) can consume the output:
   ```csharp
   internal sealed record NetShimPublishResult(bool Success, string DllPath, string Output, int ExitCode);
   ```

Keep the 600 000 ms timeout, the async double-drain, `Kill(entireProcessTree: true)` on timeout,
and the full PATH-workaround comment from `:56-62` **verbatim** — that comment is the only record
of why the workaround exists.

- [ ] **Step 4: Run the test and verify it passes**

```bash
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~NetShimPublisherTests" > test-run.txt 2>&1
```

Expected: PASS (3 tests).

- [ ] **Step 5: Point the test-only publisher at the product one**

Rewrite `VisualGameStudio.Tests/Blnet/BlnetShimPublisher.cs` to keep its `Lazy` + `FindRepoRoot`
but delegate the actual publish to `NetShimPublisher`. Its public surface (`PublishOnce()`,
`PublishOutput`) must not change — `BlnetShimPublishTests` and `BlnetConformanceTests` depend on it.

- [ ] **Step 6: Verify the frozen P0 suite is still green**

```bash
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~VisualGameStudio.Tests.Blnet" > test-run.txt 2>&1
```

Expected: all 28 Blnet Integration tests pass, including the 16 conformance scenarios.

- [ ] **Step 7: Commit**

```bash
git add BasicLang/Compiler/CodeGen/Net/NetShimPublisher.cs VisualGameStudio.Tests/Blnet/
git commit -m "feat(p2a1): move the AOT shim publisher and its VS-Installer PATH workaround into the product"
```

---

## Task 3: Reference resolution on the native path

Today `<Reference>` and `<PackageReference>` are **silently discarded** for native projects. This
task makes them resolve, and makes an unresolvable one a real diagnostic.

**Files:**
- Create: `BasicLang/Net/NetReferenceResolver.cs`
- Modify: `BasicLang/Program.cs:436`, `VisualGameStudio.ProjectSystem/Services/BuildService.cs:449`
- Test: `VisualGameStudio.Tests/Blnet/NetReferenceResolverTests.cs`, and add cases to
  `VisualGameStudio.Tests/Compiler/CppProjectCliBuildTests.cs`

- [ ] **Step 1: Write the failing unit test**

```csharp
using NUnit.Framework;
using BasicLang.Net;
using BasicLang.ProjectSystem;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// Reference resolution for native projects. Before P2a-1 every reference element was parsed
/// into the model and then silently dropped (Program.cs:436 returned before restore), so a
/// typo'd HintPath produced no output at all. These tests pin that references now resolve and
/// that failures are BL6021/BL6022 rather than silence.
/// </summary>
[TestFixture]
public class NetReferenceResolverTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "netref-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        for (var i = 0; i < 3; i++)
        {
            try { Directory.Delete(_dir, recursive: true); return; }
            catch { Thread.Sleep(200); }
        }
    }

    [Test]
    public void HintPath_ResolvesRelativeToTheProjectFile_NotTheOutputDirectory()
    {
        var libDir = Path.Combine(_dir, "lib");
        Directory.CreateDirectory(libDir);
        var dll = Path.Combine(libDir, "MyLib.dll");
        File.WriteAllBytes(dll, new byte[] { 0x4D, 0x5A });   // "MZ" — existence is all that is checked here

        var project = new ProjectFile { Backend = "cpp" };
        project.AssemblyReferences.Add(new AssemblyReference { Name = "MyLib", HintPath = "lib\\MyLib.dll" });

        var result = NetReferenceResolver.Resolve(project, Path.Combine(_dir, "App.blproj"));

        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.AssemblyPaths, Does.Contain(dll),
            "HintPath must resolve relative to the PROJECT FILE. Resolving against the output " +
            "directory is the pre-existing C# backend hazard recorded in spec §5.");
    }

    [Test]
    public void MissingHintPath_IsBL6021_NotSilence()
    {
        var project = new ProjectFile { Backend = "cpp" };
        project.AssemblyReferences.Add(new AssemblyReference { Name = "Ghost", HintPath = "lib\\Ghost.dll" });

        var result = NetReferenceResolver.Resolve(project, Path.Combine(_dir, "App.blproj"));

        Assert.That(result.Diagnostics.Select(d => d.Code), Does.Contain("BL6021"));
        Assert.That(result.Diagnostics.Single().Message, Does.Contain("Ghost"));
    }

    [Test]
    public void ProjectReference_IsBL6021_WithTheDocumentedWorkaround()
    {
        var project = new ProjectFile { Backend = "cpp" };
        project.ProjectReferences.Add("..\\Sibling\\Sibling.blproj");

        var result = NetReferenceResolver.Resolve(project, Path.Combine(_dir, "App.blproj"));

        Assert.That(result.Diagnostics.Select(d => d.Code), Does.Contain("BL6021"));
        Assert.That(result.Diagnostics.Single().Message, Does.Contain("HintPath"),
            "BL6021 for a ProjectReference must name the <Reference>+<HintPath> workaround " +
            "(spec §5, §14.9) — cross-project compilation does not exist on any build path.");
    }

    [Test]
    public void NoReferences_ProducesNoDiagnosticsAndAnEmptyClosure()
    {
        var project = new ProjectFile { Backend = "cpp" };

        var result = NetReferenceResolver.Resolve(project, Path.Combine(_dir, "App.blproj"));

        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.AssemblyPaths, Is.Empty,
            "A project with no references must cost nothing — this is what keeps every existing " +
            "native project unaffected by P2a-1.");
    }
}
```

- [ ] **Step 2: Run and verify it fails**

```bash
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~NetReferenceResolverTests" > test-run.txt 2>&1
```

Expected: build error — `NetReferenceResolver` does not exist.

- [ ] **Step 3: Implement `NetReferenceResolver`**

Create `BasicLang/Net/NetReferenceResolver.cs`, namespace `BasicLang.Net`, block-scoped.

```csharp
namespace BasicLang.Net
{
    internal sealed record NetReferenceDiagnostic(string Code, string Message, bool IsWarning);

    internal sealed record NetReferenceClosure(
        IReadOnlyList<string> AssemblyPaths,
        IReadOnlyList<NetReferenceDiagnostic> Diagnostics);

    internal static class NetReferenceResolver
    {
        public static NetReferenceClosure Resolve(ProjectSystem.ProjectFile project, string projectFilePath)
        {
            // 1. <Reference> + <HintPath>, HintPath relative to Path.GetDirectoryName(projectFilePath)
            // 2. <PackageReference> via PackageManager (Task 3 step 4)
            // 3. <ProjectReference> -> BL6021 naming the <Reference>+<HintPath> workaround
            // 4. framework assemblies from the net8.0 targeting pack
        }
    }
}
```

Rules:
- Resolve `HintPath` against the **project file's** directory.
- A `<Reference>` with no `HintPath` resolves against the targeting pack by simple name; failing
  that, **BL6021**.
- `<ProjectReference>` is always **BL6021** with the workaround in the message (§5, §14.9).
- The closure is **deduplicated by full path** and order-stable (it feeds the Task 15 cache key).

- [ ] **Step 4: Wire `<PackageReference>` through `PackageManager`**

`BasicLang/ProjectSystem/PackageManager.cs` already restores and knows package paths. Call it for
native projects too. An unrestorable package is **BL6022**.

- [ ] **Step 5: Run the unit test — expect PASS**

```bash
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~NetReferenceResolverTests" > test-run.txt 2>&1
```

- [ ] **Step 6: Wire it into both build entry points**

In `BasicLang/Program.cs`, inside the `if (project.IsNativeProject)` block at `:436`, resolve
references **before** `CppProjectBuilder.Build` and merge the diagnostics into `cppResult.Diagnostics`
so they print through the existing loop at `:443-448`. Delete the now-false comment at `:466-467`
("C++ projects have no NuGet dependencies and skip restore entirely").

Mirror the change in `VisualGameStudio.ProjectSystem/Services/BuildService.cs:449`.

- [ ] **Step 7: Add end-to-end diagnostic cases**

Add to `VisualGameStudio.Tests/Compiler/CppProjectCliBuildTests.cs`, following its existing
idiom at `:104-111`:

```csharp
[Test]
public void NativeProject_WithMissingAssemblyReference_ReportsBL6021()
{
    var proj = MakeCppProject(referenceInclude: "Ghost", hintPath: "lib\\Ghost.dll");

    var result = CppProjectBuilder.Build(ProjectFile.Load(proj), "Release");

    Assert.That(result.Diagnostics.Select(d => d.Code), Does.Contain("BL6021"));
    Assert.That(result.Success, Is.False);
}
```

Add the matching CLI-leg case via `CliTestHarness.RunCli` — **both entry points**, per repo law.

- [ ] **Step 8: Full fast subset**

```bash
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "TestCategory!=Integration" > test-run.txt 2>&1
```

Expected: 3608 + new fast tests, **0 failed**.

- [ ] **Step 9: Commit**

```bash
git add BasicLang/Net/NetReferenceResolver.cs BasicLang/Program.cs VisualGameStudio.ProjectSystem/Services/BuildService.cs VisualGameStudio.Tests/
git commit -m "feat(p2a1): resolve .blproj references on the native path; BL6021/BL6022 replace silent drops"
```

---

## Task 4: Roslyn dependency + `NetTypeResolver` (types and members)

**Files:**
- Modify: `BasicLang/BasicLang.csproj`
- Create: `BasicLang/Net/NetTypeResolver.cs`
- Test: `VisualGameStudio.Tests/Blnet/NetTypeResolverTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;
using BasicLang.Net;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// NetTypeResolver is the compiler's first real .NET type knowledge (spec §6.1). Before P2a-1
/// the analyzer accepted any PascalCase identifier as a .NET type, so New Regex(1,2,3)
/// type-checked clean and failed later in csc. Roslyn reads metadata WITHOUT loading assemblies
/// into the process, which is also the fix for TypeRegistry's Assembly.LoadFrom (spec §6.2).
/// </summary>
[TestFixture]
public class NetTypeResolverTests
{
    private static NetTypeResolver FrameworkOnly() => NetTypeResolver.Create(NetTypeResolverTestRefs.FrameworkPaths);

    [Test]
    public void ResolvesAFrameworkType()
    {
        var t = FrameworkOnly().ResolveType("System.Text.RegularExpressions.Regex");

        Assert.That(t, Is.Not.Null);
        Assert.That(t!.FullName, Is.EqualTo("System.Text.RegularExpressions.Regex"));
    }

    [Test]
    public void ReturnsNullForATypeThatDoesNotExist()
    {
        Assert.That(FrameworkOnly().ResolveType("System.Text.Rejex"), Is.Null,
            "A miss must be null so the caller can raise BL6016 — never a fabricated TypeInfo, " +
            "which is exactly what the permissive analyzer did before P2a-1.");
    }

    [Test]
    public void EnumeratesInheritedMembers()
    {
        var members = FrameworkOnly().GetMembers("System.Text.StringBuilder").Select(m => m.Name).ToList();

        Assert.That(members, Does.Contain("Append"));
        Assert.That(members, Does.Contain("ToString"), "Inherited members must be included (spec §7.2).");
    }

    [Test]
    public void DoesNotLoadAssembliesIntoTheProcess()
    {
        var before = AppDomain.CurrentDomain.GetAssemblies().Length;
        FrameworkOnly().ResolveType("System.Text.RegularExpressions.Regex");
        Assert.That(AppDomain.CurrentDomain.GetAssemblies().Length, Is.EqualTo(before),
            "MetadataReference.CreateFromFile reads metadata without loading. If this fails the " +
            "resolver has regressed to Assembly.LoadFrom semantics (file locks, module " +
            "initializers, no unload) — the very defect spec §6.2 exists to remove.");
    }
}
```

Add a small helper `NetTypeResolverTestRefs` in the same file that derives framework paths from
`AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")` — the exact pattern already used at
`VisualGameStudio.Tests/Native/CSharpRun.cs:28-33`.

- [ ] **Step 2: Run and verify it fails**

```bash
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~NetTypeResolverTests" > test-run.txt 2>&1
```

- [ ] **Step 3: Add Roslyn to `BasicLang.csproj`**

In the existing `ItemGroup` at `:23-29`, matching the file's convention that every package block
carries a comment saying **why**:

```xml
    <!-- .NET type resolution for Native projects (spec §6.1): metadata reading,
         overload resolution and inheritance, without loading assemblies. -->
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.9.2" />
```

Add to the `PropertyGroup` at `:3-11`:

```xml
    <!-- Roslyn ships 26 satellite resource DLLs across 13 locales; the IDE/ drop is
         hand-committed, so keep it to en. -->
    <SatelliteResourceLanguages>en</SatelliteResourceLanguages>
```

> **Version 4.9.2 is deliberate** — it matches `VisualGameStudio.Tests.csproj:32` exactly. A
> different version gives the test project two conflicting requests. Verified: zero conflict, since
> BasicLang already resolves `System.Collections.Immutable` 8.0.0, `System.Reflection.Metadata`
> 8.0.0 and `System.Runtime.CompilerServices.Unsafe` 6.0.0, and OmniSharp 0.19.9 pulls no Roslyn.

- [ ] **Step 4: Implement `NetTypeResolver`**

```csharp
namespace BasicLang.Net
{
    internal sealed class NetTypeResolver
    {
        private readonly CSharpCompilation _compilation;

        public static NetTypeResolver Create(IEnumerable<string> assemblyPaths) =>
            new(CSharpCompilation.Create("blnet.resolver",
                references: assemblyPaths.Select(p => MetadataReference.CreateFromFile(p))));

        public NetTypeInfo? ResolveType(string fullName) { /* GetTypeByMetadataName */ }
        public IReadOnlyList<NetMemberInfo> GetMembers(string fullName) { /* incl. base types */ }
    }
}
```

`GetTypeByMetadataName` returns **null on ambiguity** as well as on absence — Task 8 turns that
into BL6023, so preserve the distinction now via a `ResolveTypeDetailed` returning a small
`(symbol, ambiguous)` result.

- [ ] **Step 5: Build and run — expect PASS**

```bash
dotnet build BasicLang/BasicLang.csproj -c Release
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~NetTypeResolverTests" > test-run.txt 2>&1
```

- [ ] **Step 6: Commit**

```bash
git add BasicLang/BasicLang.csproj BasicLang/Net/NetTypeResolver.cs VisualGameStudio.Tests/Blnet/NetTypeResolverTests.cs
git commit -m "feat(p2a1): Roslyn-backed NetTypeResolver for type existence and member enumeration"
```

---

## Task 5: `NetTypeResolver` overload resolution

This is the capability that cannot be approximated and the whole reason §6.1 chose Roslyn.

**Files:**
- Modify: `BasicLang/Net/NetTypeResolver.cs`
- Test: `VisualGameStudio.Tests/Blnet/NetTypeResolverTests.cs`

- [ ] **Step 1: Write the failing tests**

Cover, at minimum: exact-arity selection; selection differing only by parameter type
(`Regex.IsMatch(String)` vs `Regex.IsMatch(String, String)`); an inherited overload; a generic
method; **ambiguity returning a distinct result rather than null**; and a no-match returning a
distinct result. Each assertion message must say which spec section it pins.

- [ ] **Step 2: Run and verify it fails**
- [ ] **Step 3: Implement** via `CSharpCompilation`'s semantic model / `OverloadResolution`, exposing:

```csharp
internal enum NetOverloadOutcome { Resolved, NoMatch, Ambiguous }
internal sealed record NetOverloadResult(NetOverloadOutcome Outcome, NetMemberInfo? Member);
```

The three outcomes map onto BL6017 / BL6018 in Task 8. Keeping them distinct **here** is what
makes those diagnostics honest later.

- [ ] **Step 4: Run — expect PASS**
- [ ] **Step 5: Commit**

```bash
git commit -m "feat(p2a1): overload resolution in NetTypeResolver with distinct no-match and ambiguous outcomes"
```

---

## Task 6: The shared ambient-namespace constant

§6.5 requires one constant used by **both** `NetTypeResolver` and `CSharpBackend`, with a drift
invariant. Without it, §6.3's "valid programs behave identically on both backends" is false.

**Files:**
- Create: `BasicLang/Net/NetAmbientNamespaces.cs`
- Modify: `BasicLang/CSharpBackend.cs:171-187`
- Test: `VisualGameStudio.Tests/Blnet/NetAmbientNamespaceTests.cs`

- [ ] **Step 1: Write the failing drift test**

```csharp
[Test]
public void CSharpBackendAndResolverShareOneAmbientSet()
{
    Assert.That(CSharpBackend.AmbientNamespacesForTest, Is.EquivalentTo(NetAmbientNamespaces.All),
        "The C# backend's auto-imported namespaces and the resolver's ambient set drifted — " +
        "update NetAmbientNamespaces, not a parallel list. If they differ, a program that " +
        "compiles on the C# backend becomes BL6016 natively and spec §6.3's equal-behavior " +
        "claim is false.");
}

[Test]
public void AmbientSetContainsTheSeventeenKnownNamespaces()
{
    Assert.That(NetAmbientNamespaces.All, Has.Length.EqualTo(17));
    Assert.That(NetAmbientNamespaces.All, Does.Contain("System"));
    Assert.That(NetAmbientNamespaces.All, Does.Contain("System.Text.RegularExpressions"));
}
```

- [ ] **Step 2: Run and verify it fails**
- [ ] **Step 3: Extract the constant.** Read `BasicLang/CSharpBackend.cs:171-187`, move the 17
  namespaces into `NetAmbientNamespaces.All`, and have `CSharpBackend` read from it. Expose an
  internal `AmbientNamespacesForTest`. **The emitted C# must be byte-identical** — this is a pure
  extraction.
- [ ] **Step 4: Run — expect PASS**
- [ ] **Step 5: Prove the extraction changed no output**

```bash
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "TestCategory!=Integration" > test-run.txt 2>&1
```

Expected: unchanged pass count + the 2 new tests. Any C#-backend test failure means the extraction
was not faithful.

- [ ] **Step 6: Commit**

```bash
git commit -m "refactor(p2a1): single ambient-namespace constant shared by CSharpBackend and NetTypeResolver"
```

---

## Task 7: Replace the LSP's `Assembly.LoadFrom`

`BasicLang/TypeRegistry.cs` has three `Assembly.LoadFrom` call sites (`:145`, `:310`, `:346`) that
load into the compiler process — file locks, module initializers, no unload — with failures
swallowed by a bare `catch {}`. LSP-only surface, so this is a contained correctness win.

**Files:**
- Modify: `BasicLang/TypeRegistry.cs`
- Test: `VisualGameStudio.Tests/Blnet/NetTypeResolverTests.cs` (add), plus existing LSP tests

- [ ] **Step 1: Write the failing test** — resolving a type from a real on-disk assembly must not
  lock the file:

```csharp
[Test]
public void ResolvingFromAnAssemblyDoesNotLockTheFile()
{
    var copy = Path.Combine(_dir, "Probe.dll");
    File.Copy(typeof(NetTypeResolver).Assembly.Location, copy);

    var resolver = NetTypeResolver.Create(NetTypeResolverTestRefs.FrameworkPaths.Append(copy));
    resolver.ResolveType("BasicLang.Net.NetTypeResolver");

    Assert.DoesNotThrow(() => File.Delete(copy),
        "Assembly.LoadFrom would hold a lock here. Spec §6.2 replaces it precisely because the " +
        "LSP could not reload a rebuilt assembly without restarting.");
}
```

- [ ] **Step 2: Run and verify it fails** (it fails today against the `TypeRegistry` path).
- [ ] **Step 3: Route `TypeRegistry`'s three call sites through `NetTypeResolver`.** Replace the
  bare `catch {}` blocks with logged failures — a swallowed error here is why nobody noticed the
  LSP silently degrading.
- [ ] **Step 4: Run the LSP fixtures**

```bash
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~VisualGameStudio.Tests.LSP" > test-run.txt 2>&1
```

- [ ] **Step 5: Commit**

```bash
git commit -m "fix(p2a1): LSP type registry reads metadata instead of Assembly.LoadFrom; no more file locks or swallowed failures"
```

> `BasicLang/ExternalLibraryLoader.cs:169` is a **second** `Assembly.LoadFrom` channel, reachable
> from the compile path via `Import … From` (`SemanticAnalyzer.cs:3376-3379`, `:3434`). It is
> **out of scope** for P2a-1 — note it in the commit body and leave it.

---

## Task 8: The claim predicate + resolver wired warning-only

**This task is where P2a-1's inertness is bought.** The resolver becomes reachable from the
analyzer on both backends, but only ever emits **warnings** — so it can be wrong without breaking
a build.

**Files:**
- Create: `BasicLang/Net/NetClaimPredicate.cs`
- Modify: `BasicLang/SemanticAnalyzer.cs`
- Test: `VisualGameStudio.Tests/Blnet/NetClaimPredicateTests.cs`

- [ ] **Step 1: Write the failing predicate tests**

```csharp
/// <summary>
/// Spec §6.5's claim predicate. Getting this wrong is the single most dangerous mistake in
/// P2a: reading "claimed" as "registry + CppCapabilityChecker" routes Console.WriteLine through
/// the shim and rewrites the behavior of every existing program, P1's parity battery included.
/// </summary>
[TestFixture]
public class NetClaimPredicateTests
{
    [TestCase("String")]      // BoundaryTypeRegistry -> Bridged
    [TestCase("DateTime")]    // BoundaryTypeRegistry -> NativeOwned
    [TestCase("List")]        // CppCapabilityChecker :620-623
    [TestCase("Dictionary")]
    [TestCase("HashSet")]
    [TestCase("Task")]        // CppCapabilityChecker :599-604
    [TestCase("Func")]
    [TestCase("Action")]
    public void ClaimedTypeNames(string name) =>
        Assert.That(NetClaimPredicate.IsClaimedTypeName(name), Is.True);

    [TestCase("Regex")]       // ManagedOwned after P2a-2's flip -> shim-routed, NOT claimed
    [TestCase("Uri")]
    [TestCase("Stream")]
    public void ManagedOwnedNamesAreNotClaimed(string name) =>
        Assert.That(NetClaimPredicate.IsClaimedTypeName(name), Is.False,
            "Row (a) is {NativeOwned, Bridged}, NOT `!= Unknown`. Claiming ManagedOwned would " +
            "make spec §4.2's Regex_Match__string slot ungeneratable. The predicate must give " +
            "the same answer before and after P2a-2's registry flip.");

    [Test]
    public void ConsoleWriteLineIsClaimedPerCall()
    {
        Assert.That(NetClaimPredicate.IsClaimedCall("Console", "WriteLine"), Is.True,
            "Console is claimed by IRBuilder.KnownNetStaticTypes -> EmitStdLibCall, NOT by " +
            "CppCapabilityChecker. Missing this routes every existing program through the shim.");
    }

    [Test]
    public void ConsoleReadKeyIsNotClaimed()
    {
        Assert.That(NetClaimPredicate.IsClaimedCall("Console", "ReadKey"), Is.False,
            "Row (c) is PER CALL: KnownNetStaticTypes is a call-SHAPE classifier, not an " +
            "inventory of native implementations. EmitStdLibCall returns null here.");
    }

    [TestCase("File", "ReadAllText")]
    [TestCase("Activator", "CreateInstance")]
    public void TableMembersWithoutAnEmitArmAreNotClaimed(string type, string member) =>
        Assert.That(NetClaimPredicate.IsClaimedCall(type, member), Is.False,
            "22 KnownNetStaticTypes entries have no EmitStdLibCall arm. Claiming them by table " +
            "membership would strand the surface spec §1.1 exists to deliver.");
}
```

- [ ] **Step 2: Run and verify it fails**
- [ ] **Step 3: Implement the three-source predicate** exactly as §6.5's table specifies:
  (a) `BoundaryTypeRegistry.Categorize ∈ {NativeOwned, Bridged}`;
  (b) `CppCapabilityChecker`'s early returns (`:598-625`);
  (c) **per call** — `IRBuilder.KnownNetStaticTypes` (`:3644-3664`) **AND** `EmitStdLibCall`
  (`CppCodeGenerator.cs:2210-2347`) returning non-null.

  For (c), extract the arm-existence check into something both `EmitStdLibCall` and the predicate
  call, so they cannot drift.

- [ ] **Step 4: Run — expect PASS**
- [ ] **Step 5: Wire the resolver into `SemanticAnalyzer`, warning-only**

- Call `ConfigureTypeRegistry` from `CompileUnit` construction so the compile path is configured
  identically to the LSP path (`LSP/DocumentManager.cs:571` is currently its only caller).
- On an unresolved .NET type or member, emit a **warning** on **both** backends (BL6016 / BL6017 /
  BL6018 / BL6023). §6.3's native-error behavior lands in **P2a-2**, not here.
- A **claimed** name never reaches the resolver at all.

- [ ] **Step 6: Prove inertness — the whole point of this task**

```bash
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "TestCategory!=Integration" > test-run.txt 2>&1
```

Expected: **0 failed.** Then the P1 batteries, which are the real proof that `Console` and friends
still route natively:

```bash
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~BclBackendParityTests" > test-run.txt 2>&1
```

Expected: all 13 parity programs still byte-identical.

- [ ] **Step 7: Commit**

```bash
git commit -m "feat(p2a1): claim predicate + resolver wired warning-only on both backends"
```

---

## Task 9: `NetNameMangler`

**Files:**
- Create: `BasicLang/Net/NetNameMangler.cs`
- Test: `VisualGameStudio.Tests/Blnet/NetNameManglerTests.cs`

- [ ] **Step 1: Write the failing tests** — determinism across calls; **collision-freedom over
  fully-qualified declaring types** (`MyLib.Customer` vs `OtherLib.Customer` — §7.3's explicit
  trap); distinctness across an overload set; independence from input order; and output being a
  legal C identifier.
- [ ] **Step 2: Run and verify it fails**
- [ ] **Step 3: Implement.** Mangle from (fully-qualified declaring type, member name, parameter
  types). Keep it a pure function — Task 15's cache key depends on its stability.
- [ ] **Step 4: Run — expect PASS**
- [ ] **Step 5: Commit**

```bash
git commit -m "feat(p2a1): deterministic, collision-free export name mangler"
```

---

## Task 10: IR carriage read by nobody

Adds the resolved-target and category-marker fields. Nothing consumes them until P2a-2 — but the
**optimizer must preserve them**, and that is what this task pins.

**Files:**
- Modify: `BasicLang/IRNodes.cs`, `BasicLang/IRBuilder.cs`, `BasicLang/IROptimizer.cs`
- Test: `VisualGameStudio.Tests/Blnet/NetIrCarriageTests.cs`

- [ ] **Step 1: Write the failing optimizer round-trip test**

```csharp
[Test]
public void OptimizerPreservesTheResolvedNetTargetAndCategoryMarker()
{
    var module = BuildModuleWithAResolvedNetCall();
    var before = FindCall(module);

    new IROptimizer().Run(module);   // match the real pipeline entry used by BclE2E

    var after = FindCall(module);
    Assert.That(after.ResolvedNetTarget, Is.EqualTo(before.ResolvedNetTarget),
        "The optimizer dropped the resolved .NET target. Every IR node copy/clone path must " +
        "carry it, or P2a-2's lowering silently falls back to name-based dispatch — which is " +
        "the wild-pointer class spec §8.5 exists to prevent.");
    Assert.That(after.NetCategory, Is.EqualTo(before.NetCategory));
}
```

- [ ] **Step 2: Run and verify it fails**
- [ ] **Step 3: Add the fields** to `IRCall` (and the IR type descriptor for the category marker),
  populate them in `IRBuilder` where the fused receiver+member name is built (`:3342`), and update
  **every** copy/clone/visit path in `IROptimizer` that reconstructs these nodes.
- [ ] **Step 4: Run — expect PASS**
- [ ] **Step 5: Prove no codegen change**

```bash
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~CppBackendTests" > test-run.txt 2>&1
```

Expected: unchanged. A new IR field must not alter a single byte of emitted C++.

- [ ] **Step 6: Commit**

```bash
git commit -m "feat(p2a1): IR carriage for resolved .NET targets and the managed category marker"
```

---

## Task 11: `BlnetShimSources` + drift invariants

**Files:**
- Create: `BasicLang/Compiler/CodeGen/Net/BlnetShimSources.cs`
- Test: `VisualGameStudio.Tests/Blnet/BlnetShimSourcesTests.cs`

- [ ] **Step 1: Write the failing drift tests**

```csharp
[Test]
public void HandleTableMatchesTheHandWrittenShimTheFrozenSuiteValidates()
{
    var shipped = File.ReadAllText(PathToTestShimHandleTable()).Replace("\r\n", "\n");
    Assert.That(BlnetShimSources.HandleTable.Replace("\r\n", "\n"), Is.EqualTo(shipped),
        "BlnetShimSources.HandleTable drifted from VisualGameStudio.Tests/TestAssets/" +
        "BlnetTestShim/HandleTable.cs. The frozen P0 conformance suite (spec §12.2) validates " +
        "the hand-written copy; if the generated shim's handle model differs, the suite proves " +
        "nothing about generated shims. Update BOTH, or neither.");
}

[Test]
public void StatusEnumComesFromTheContract() =>
    Assert.That(BlnetShimSources.BlnetStatusCs, Is.EqualTo(BlnetContract.GenerateStatusEnumCs()),
        "The shim's status enum must be generated from BlnetContract — never hand-copied.");
```

- [ ] **Step 2: Run and verify it fails**
- [ ] **Step 3: Implement** `BlnetShimSources` mirroring `BlnetRuntimeSources.cs` exactly: a
  `public static class`, verbatim-string constants, and an XML `<summary>` naming both the spec
  section (§8.1) and the drift fixture that pins it — the convention every source-of-truth class in
  this repo follows.
- [ ] **Step 4: Run — expect PASS**
- [ ] **Step 5: Commit**

```bash
git commit -m "feat(p2a1): BlnetShimSources carries the shim's fixed C# scaffolding in the product"
```

---

## Task 12: `NetSurface` model + `NetProxyEmitter` on an empty surface

**Files:**
- Create: `BasicLang/Net/NetSurface.cs`, `BasicLang/Compiler/CodeGen/Net/NetProxyEmitter.cs`
- Test: `VisualGameStudio.Tests/Blnet/NetProxyEmitterTests.cs`

- [ ] **Step 1: Write the failing tests** — an **empty** surface emits **no files at all** (the
  property that keeps every existing project unaffected); a hand-fed one-member surface emits the
  six §9.1 artifacts; each generated proxy body contains a `BlnetCallScope` and a null-slot guard
  (§9.2); the emitted header compiles standalone.

For the compile-smoke, follow `BlnetNativeRuntimeTests.cs:13-31` — `[OneTimeSetUp]` probing
`CppCompile.FindRunCompiler()` with `Assert.Ignore` when absent, then `CompileAndRun` with the
headers as `extraFiles`. Mark that test `[Category("Integration")]`; keep the string pins fast.

- [ ] **Step 2: Run and verify it fails**
- [ ] **Step 3: Implement.** `NetSurface` is a record with the member list and the `<NetProxy>`
  declared types. `NetProxyEmitter` produces the artifact set keyed on the surface being non-empty
  — **independent of BasicLang sources**, which is what Task 13 then wires up.
- [ ] **Step 4: Run — expect PASS**
- [ ] **Step 5: Commit**

```bash
git commit -m "feat(p2a1): NetSurface model and NetProxyEmitter; empty surface emits nothing"
```

---

## Task 13: `CppProjectBuilder` phase model, gate merge, cancellation

**The riskiest "inert" task** — restructuring `EmitCore` must produce byte-identical generated C++
for every existing project.

**Files:**
- Modify: `BasicLang/ProjectSystem/CppProjectBuilder.cs`
- Test: `VisualGameStudio.Tests/Blnet/NetBuildPipelineTests.cs`

- [ ] **Step 1: Write the failing tests** — a project with `.bas` sources and an empty surface
  produces the **same** file set as before; a project with **zero `.bas` files** and a non-empty
  surface produces `obj/gen` artifacts **and** gets `blnet_startup.g.cpp` into `request.SourceFiles`
  **and** `obj/gen` on the include path; a cancelled token aborts between phases.
- [ ] **Step 2: Run and verify it fails**
- [ ] **Step 3: Implement the merge — not a widened condition.**

`split` is declared null at `CppProjectBuilder.cs:265` and assigned only inside the gate, so
relaxing the `if` to `surface.IsNonEmpty || blSources.Count > 0` **null-dereferences**
`split.Files` (`:326-327`) and `split.TranslationUnitFileNames` (`:338-340`). Per §9.5:

- `NetProxyEmitter` produces its artifacts whenever `surface.IsNonEmpty`, independent of `split`.
- `EmitCore` creates/cleans `obj/gen` (`:323-325`) and writes the **merged** set — proxy artifacts
  plus `split.Files` **only when `split != null`**.
- `generatedTus` (`:338-340`, feeding `request.SourceFiles` at `:414`) **unions** the proxy TUs
  with `split.TranslationUnitFileNames` when non-null.
- The include path (`:419-420`) is gated on the **union** being non-empty.

Thread a `CancellationToken` through `Build` and honor it between phases.

- [ ] **Step 4: Run — expect PASS**
- [ ] **Step 5: Prove byte-identical output — the acceptance criterion for this task**

```bash
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~CppBackendTests|FullyQualifiedName~CppCollectionTests|FullyQualifiedName~CppBclEndToEndTests" > test-run.txt 2>&1
```

Expected: **118 pre-existing C++ fixtures unchanged.** This task's entire success criterion is
"nothing changed", and these are what prove it.

- [ ] **Step 6: Commit**

```bash
git commit -m "refactor(p2a1): CppProjectBuilder phase model, surface/source gate merge, cancellation"
```

---

## Task 14: `NetShimGenerator` on a hand-fed surface

**Files:**
- Create: `BasicLang/Compiler/CodeGen/Net/NetShimGenerator.cs`
- Test: `VisualGameStudio.Tests/Blnet/NetShimGeneratorTests.cs`

- [ ] **Step 1: Write the failing tests** — the emitted csproj carries every §8.1 property
  (`net8.0`, `PublishAot`, `AllowUnsafeBlocks`, `InvariantGlobalization`, `IsAotCompatible`,
  **`TrimmerSingleWarn=false`**); an export body guards handle `0` without consulting the table and
  encodes a null return as `0` (§8.2); a value-type receiver uses `Unsafe.Unbox<T>` (§8.5); and an
  Integration test that the generated shim **publishes successfully** via `NetShimPublisher`.
- [ ] **Step 2: Run and verify it fails**
- [ ] **Step 3: Implement**, emitting the pattern `Exports.cs:82-93` proves, with §8.2's null
  handling. `TrimmerSingleWarn=false` is load-bearing for Task 16 — without it ILC collapses
  per-assembly warnings and the mapper has nothing to parse.
- [ ] **Step 4: Run — expect PASS**
- [ ] **Step 5: Commit**

```bash
git commit -m "feat(p2a1): NetShimGenerator emits the shim project and exports from a surface"
```

---

## Task 15: Content-hash cache

**Files:**
- Create: `BasicLang/Compiler/CodeGen/Net/NetShimCache.cs`
- Test: `VisualGameStudio.Tests/Blnet/NetShimCacheTests.cs`

- [ ] **Step 1: Write the failing tests** — the key changes when a reference's **MVID** changes;
  it does **not** change when an unrelated file is touched; it changes when the mangled member set,
  the shim template version, the TFM/RID/toolchain, or the SDK/ILCompiler version changes; a
  missing or unparsable manifest is a **miss**, never a silent stale hit.

```csharp
[Test]
public void KeyUsesMvidNotTimestampAndSize()
{
    // Two assemblies with identical length and timestamp but different content must key differently.
    Assert.That(NetShimCache.KeyFor(asmA, surface), Is.Not.EqualTo(NetShimCache.KeyFor(asmB, surface)),
        "Spec §10.2 requires MVID. Timestamp+size collides on a rebuilt assembly whose content " +
        "changed — a false cache HIT ships a stale shim, which is the worst failure mode here.");
}
```

- [ ] **Step 2: Run and verify it fails**
- [ ] **Step 3: Implement.** Also make `Clean` drop the shim cache — today it deletes `bin/<config>`
  but not `obj/`.
- [ ] **Step 4: Run — expect PASS**
- [ ] **Step 5: Commit**

```bash
git commit -m "feat(p2a1): MVID-keyed content hash cache for the shim publish"
```

---

## Task 16: `AotDiagnosticMapper` + closeout

**Files:**
- Create: `BasicLang/Compiler/CodeGen/Net/AotDiagnosticMapper.cs`
- Test: `VisualGameStudio.Tests/Blnet/AotDiagnosticMapperTests.cs`
- Modify: `docs/superpowers/plans/2026-07-29-p2a1-dotnet-native-foundation.md` (mark complete)

- [ ] **Step 1: Write the failing tests** over **captured ILC output text** (no publish needed —
  keep these fast). Cover all three §11.3 tiers and §15.10's severity split:

| Input | Expected |
|---|---|
| IL3050 against a generated wrapper | BL6020 **error**, mapped to the `.bas` line via the provenance map |
| IL2026 against a generated wrapper | BL6020 **warning**, mapped to the `.bas` line |
| IL2026 inside a referenced assembly | BL6020 warning, named assembly + origin member, attributed to the project |
| IL3053 assembly-level aggregate | BL6020 warning, assembly named only |
| unparsable line | reported, never dropped |

- [ ] **Step 2: Run and verify it fails**
- [ ] **Step 3: Implement** the scan over **all** `ILxxxx` trim/AOT diagnostics — not a two-code
  allowlist — plus the provenance map from mangled wrapper name to `.bas` location, and a second
  origin kind for `<NetProxy>` items in the `.blproj`.
- [ ] **Step 4: Run — expect PASS**
- [ ] **Step 5: Full verification**

```bash
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "TestCategory!=Integration" > test-run.txt 2>&1
```
```bash
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~VisualGameStudio.Tests.Blnet" > test-run.txt 2>&1
```
```bash
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~CppBackendTests|FullyQualifiedName~CppCollectionTests|FullyQualifiedName~CppBclEndToEndTests|FullyQualifiedName~CppBclRuntimeTests|FullyQualifiedName~CppDecimalRuntimeTests|FullyQualifiedName~CppNativeBclDiagnosticTests|FullyQualifiedName~BclBackendParityTests" > test-run.txt 2>&1
```

**Gates:** fast subset 3608 + new, 0 failed · all Blnet tests green incl. the 16 frozen scenarios ·
118 pre-existing C++ fixtures unchanged · 13 parity programs still byte-identical.

- [ ] **Step 6: Verify P2a-1's central claim — nothing changed for existing programs**

Build a game project and a console project from `IDE/` templates and confirm identical output to
`master`. If anything differs, P2a-1 was not inert and the flip in P2a-2 is now carrying hidden
behavior change.

- [ ] **Step 7: Refresh the prebuilt IDE binaries**

Adding Roslyn changes BasicLang's dependency set. Per repo convention this is a **separate** commit
titled `chore: refresh prebuilt IDE binaries with <what>`, and it must include:

- `IDE/BasicLang.dll`, `IDE/BasicLang.exe`
- `IDE/Microsoft.CodeAnalysis.dll`, `IDE/Microsoft.CodeAnalysis.CSharp.dll`
- **`IDE/BasicLang.deps.json`** — tracked, and a dependency-set change invalidates it. The host will
  not resolve the new DLLs without it. Ordinary code-only refreshes don't touch it, which is exactly
  why this one is easy to forget.

With `SatelliteResourceLanguages=en` (Task 4) the 26 locale DLLs do not appear. Verify with
`git status` that no `cs/`, `de/`, `fr/`… folders showed up under `IDE/`.

- [ ] **Step 8: Commit and close out**

```bash
git add -A
git commit -m "feat(p2a1): AotDiagnosticMapper; P2a-1 foundation complete"
```

Then update `MEMORY.md` and `dotnet-in-native-projects.md` with the completion state, the measured
publish cost from Task 1, and anything learned that would be expensive to rediscover.

---

## What P2a-1 deliberately does NOT do

Stated so a reviewer can tell "missing" from "deferred" (all of this is P2a-2, spec §17):

- No .NET type is accepted by `CppCapabilityChecker` — the registry flip has not happened.
- No `NetSurfaceCollector` — surfaces are hand-fed in tests only.
- No typed-catch lowering, no collection consumption, no outbound array copy, no delegates.
- The resolver **warns**; it never fails a build.
- No parity programs and no generated-shim conformance suite.
