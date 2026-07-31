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
| `BasicLang/Net/NetReferenceResolver.cs` | `.blproj` → assembly closure; BL6021 |
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
| `BasicLang/Program.cs:466-467` | delete the false "C++ projects have no NuGet dependencies" comment |
| `BasicLang/ProjectSystem/CppProjectBuilder.cs` | reference resolution in `EmitCore`, phase model, gate merge, `CancellationToken`, BL6025 |
| `BasicLang/Compiler/CodeGen/CPlusPlus/BlnetRuntimeSources.cs` | `blnet_load_module`, `blnet_bind_core`, `g_native_vtable` (Task 12) |
| `BasicLang/SemanticAnalyzer.cs` | resolver wired **warning-only** (no `ConfigureTypeRegistry` — deferred to P2a-2) |
| `VisualGameStudio.ProjectSystem/Services/BuildService.cs:307` | `CleanAsync` drops the shim cache (Task 15) |
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

> **Resolution lives in `CppProjectBuilder.EmitCore`, not the entry points.** This is spec §10.1
> phase 1 ("IntelliSense runs it? yes"). Both callers already handle diagnostics —
> `Program.cs:443-448` prints `cppResult.Diagnostics` and gates on `Success` at `:449`, and
> `BuildService.BuildCppProject` maps both at `:1190-1208` — so neither needs new plumbing. The
> only entry-point change in this task is **deleting a false comment**.
>
> ⚠ **`<ProjectReference>` is a WARNING in P2a-1, not an error.** The IDE writes that element into
> native projects itself: "Add Project Reference" is gated only on
> `HasSolution && IsProject && Projects.Count >= 2` (`SolutionExplorerViewModel.cs:625-627`) with
> **no backend filter**, and `:689` calls `BlprojReferenceWriter.AddReference`. Since
> `CppProjectBuilder` reads no reference item today, such a project builds fine on `master`.
> Making it an error here would break projects the IDE creates and falsify this plan's defining
> property. It is promoted to an error at P2a-2's flip.

**Files:**
- Create: `BasicLang/Net/NetReferenceResolver.cs`
- Modify: `BasicLang/ProjectSystem/CppProjectBuilder.cs` (resolve in `EmitCore`, ahead of the
  source partition at `:187`), `BasicLang/Program.cs:466-467` (delete the false comment)
- Test: `VisualGameStudio.Tests/Blnet/NetReferenceResolverTests.cs`, and add cases to
  `VisualGameStudio.Tests/Compiler/CppProjectCliBuildTests.cs`

- [ ] **Step 1: Write the failing unit test**

```csharp
using NUnit.Framework;
using BasicLang.Net;
using BasicLang.Compiler.ProjectSystem;   // NOT BasicLang.ProjectSystem — see ProjectFile.cs:8

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// Reference resolution for native projects. Before P2a-1 every reference element was parsed
/// into the model and then silently dropped (Program.cs:436 returned before restore), so a
/// typo'd HintPath produced no output at all. These tests pin that references now resolve and
/// that failures are BL6021 rather than silence. (BL6022 is reserved by spec §11.4 for
/// &lt;NetProxy&gt; naming an unknown type — a P2a-2 concern.)
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
    public void ProjectReference_IsABL6021_WARNING_WithTheDocumentedWorkaround()
    {
        var project = new ProjectFile { Backend = "cpp" };
        project.ProjectReferences.Add("..\\Sibling\\Sibling.blproj");

        var result = NetReferenceResolver.Resolve(project, Path.Combine(_dir, "App.blproj"));

        var diag = result.Diagnostics.Single();
        Assert.That(diag.Code, Is.EqualTo("BL6021"));
        Assert.That(diag.IsWarning, Is.True,
            "MUST be a warning in P2a-1. The IDE writes <ProjectReference> into native projects " +
            "itself — 'Add Project Reference' has NO backend filter " +
            "(SolutionExplorerViewModel.cs:625-627 -> :689). An error here breaks projects the " +
            "IDE creates and falsifies this plan's inertness claim. P2a-2 promotes it.");
        Assert.That(diag.Message, Does.Contain("HintPath"),
            "The message must name the <Reference>+<HintPath> workaround (spec §5, §14.9) — " +
            "cross-project compilation does not exist on any build path.");
    }

    [Test]
    public void NoReferences_ProducesNoDiagnosticsAndNoDeclaredAssemblies()
    {
        var project = new ProjectFile { Backend = "cpp" };

        var result = NetReferenceResolver.Resolve(project, Path.Combine(_dir, "App.blproj"));

        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.AssemblyPaths, Is.Empty,
            "AssemblyPaths holds only what the project DECLARED — a project with no references " +
            "must cost nothing, which is what keeps existing native projects unaffected.");
        Assert.That(result.FrameworkPaths, Is.Not.Empty,
            "FrameworkPaths is always populated and is SEPARATE from AssemblyPaths. Spec §6.5 " +
            "step 2 requires `Dim r As New Regex(\"a\")` to resolve with no <Reference> at all, " +
            "so the framework set cannot be conditional on the project declaring something.");
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
        IReadOnlyList<string> AssemblyPaths,     // what the project DECLARED
        IReadOnlyList<string> FrameworkPaths,    // always populated, independent of declarations
        IReadOnlyList<NetReferenceDiagnostic> Diagnostics)
    {
        /// <summary>Everything Roslyn should see. Order-stable and de-duplicated by full path.</summary>
        public IReadOnlyList<string> All { get; } =
            FrameworkPaths.Concat(AssemblyPaths).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static class NetReferenceResolver
    {
        public static NetReferenceClosure Resolve(
            Compiler.ProjectSystem.ProjectFile project, string projectFilePath)
        {
            // 1. <Reference> + <HintPath>, HintPath relative to Path.GetDirectoryName(projectFilePath)
            // 2. <PackageReference> via PackageManager (step 4)
            // 3. <ProjectReference> -> BL6021 WARNING naming the <Reference>+<HintPath> workaround
            // 4. framework assemblies -> FrameworkPaths (see the sourcing rule below)
        }
    }
}
```

Rules:
- Resolve `HintPath` against the **project file's** directory.
- A `<Reference>` with no `HintPath` resolves against the framework set by simple name; failing
  that, **BL6021** (error).
- `<ProjectReference>` is **BL6021 with `IsWarning = true`** (§5, §14.9). Error at the P2a-2 flip.
- An unrestorable `<PackageReference>` is **BL6021** (error) — *not* BL6022, which §11.4 reserves
  for `<NetProxy>` naming an unknown type.
- `AssemblyPaths` and `FrameworkPaths` are separately **de-duplicated by full path** and
  **order-stable** — Task 15's cache key hashes them.

> **Where `FrameworkPaths` comes from, and what if it is absent.** Start from
> `AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")` — the compiler runs on net8.0, so this needs
> **no SDK or targeting pack on the machine**. This matters: today the native path requires only a
> C++ toolchain (`CppProjectBuilder.cs:354-375`), and depending on a targeting pack would introduce
> a brand-new environment failure mode for projects that use no .NET at all. If the resulting set is
> empty, `FrameworkPaths` is empty and resolution proceeds — a project with no .NET usage is
> unaffected either way. The same helper backs Task 4's test fixture (`CSharpRun.cs:28-33` is the
> in-repo precedent).
>
> ⛔ **The raw TPA list is NOT usable as-is — it is the HOST PROCESS's assembly set, not the
> framework's.** This was a defect in an earlier revision of this plan, found by review after Task 3
> shipped and fixed in the follow-up commit. The original wording ("the compiler is itself a net8.0
> process") holds for `BasicLang.exe` and is **false** when `BasicLang.dll` is loaded in-process by
> `VisualGameStudio.exe` — which is the IDE's *only* native build path (`BuildService.cs:1169`) and
> its *only* IntelliSense path (`IntelliSenseEmissionService.cs:204`). `IDE/` ships ~40 non-framework
> managed DLLs (`Avalonia.*`, `AvaloniaEdit`, `Dock.*`, `CommunityToolkit.Mvvm`, `Newtonsoft.Json`,
> `SkiaSharp`, …). Three consequences, all real:
>
> 1. `<Reference Include="Avalonia" />` with no `<HintPath>` resolves **clean in the IDE** and is a
>    **BL6021 error in the CLI**, for a byte-identical `.blproj` — exactly the cross-entry-point
>    divergence CLAUDE.md's "test both entry points" law exists to prevent.
> 2. Task 15 hashes `FrameworkPaths`. Host-dependent content makes alternating IDE and CLI builds on
>    one project a guaranteed cache miss, and would compile the shim against a different framework
>    set depending on who built it.
> 3. Task 4's `NetTypeResolver.Create(closure.All)` would grant BasicLang programs ambient
>    visibility into the IDE's entire dependency graph — in the IDE only.
>
> **Therefore: intersect the TPA list with the shared-framework directory.** Both inputs are
> load-bearing and neither alone is correct:
>
> - **TPA contributes "managed and trusted."** Do NOT simply enumerate the framework directory — it
>   contains native DLLs (`coreclr.dll`, `clrjit.dll`, `mscordbi.dll`, `hostpolicy.dll`) and
>   `MetadataReference.CreateFromFile` throws `BadImageFormatException` on those, which would break
>   Task 4.
> - **The directory contributes "framework, not the host's dependencies."** Source it from
>   `Path.GetDirectoryName(typeof(object).Assembly.Location)` (CoreLib's own directory — the most
>   exact match, since TPA is guaranteed to contain CoreLib from that directory), falling back to
>   `RuntimeEnvironment.GetRuntimeDirectory()` for a single-file bundle where `Location` is empty.
> - ⚠ **Normalize both sides** with `Path.TrimEndingDirectorySeparator`: `GetRuntimeDirectory()`
>   returns a trailing separator and `GetDirectoryName()` does not, so a naive compare matches
>   nothing and **silently empties the set**.
>
> **Pinning it takes three assertions, not one.** A bare directory invariant is *vacuous* — an empty
> `FrameworkPaths` satisfies it:
>
> 1. **Directory invariant** — no entry lives outside the shared-framework directory. Failure
>    message must name the host-TPA trap and say "do NOT fix this by relaxing the filter."
> 2. **Strict-subset count** — `FrameworkPaths.Count < (TPA .dll count)`. This is the assertion that
>    actually fails if the fix is reverted, and it works *because* the test host resembles the Shell:
>    it needs the host to load extra assemblies, which `VisualGameStudio.Tests.csproj:28`'s Avalonia
>    reference guarantees.
> 3. **Over-filtering guard** — `System.Runtime`, `System.Console` and `System.Text.RegularExpressions`
>    are present, since §6.5 needs those to resolve with no `<Reference>` at all.
>
> Do **not** assert the absence of specific names (e.g. "Avalonia"): that pins an accident of this
> host's dependency list rather than the rule.

- [ ] **Step 4: Wire `<PackageReference>` through `PackageManager`**

`BasicLang/ProjectSystem/PackageManager.cs` already restores and knows package paths. Call it for
native projects too. An unrestorable package is **BL6021**.

> `PackageManager.RestoreAsync` is **async** while `Resolve` is synchronous. Do not block on it
> inside `Resolve`. Restore packages in `EmitCore` *before* calling `Resolve`, and pass the
> resolved package assembly paths in as a parameter — this keeps `NetReferenceResolver` a pure,
> synchronously-testable function, which is what the Step 1 tests assume.

- [ ] **Step 5: Run the unit test — expect PASS**

```bash
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~NetReferenceResolverTests" > test-run.txt 2>&1
```

- [ ] **Step 6: Call the resolver from `EmitCore`, and delete the false comment**

In `BasicLang/ProjectSystem/CppProjectBuilder.cs`, call `NetReferenceResolver.Resolve` inside
`EmitCore` (`:161`), **ahead of the source partition at `:187`** — spec §10.1 phase 1. Append its
diagnostics to `result.Diagnostics`; for the error-severity ones use the existing
`Fail(result, code, message, project.FilePath)` idiom (precedent at `:212`). Expose the resulting
`NetReferenceClosure` on `CppEmitOutcome` (`:26-44`) so Tasks 8, 12, 14 and 15 can consume it.

Neither entry point needs plumbing — `Program.cs:443-448` already prints `cppResult.Diagnostics`
and gates on `Success` at `:449`, and `BuildService.BuildCppProject` already maps both at
`:1190-1208`. The **only** entry-point change is deleting the now-false comment at
`Program.cs:466-467` ("C++ projects have no NuGet dependencies and skip restore entirely").

- [ ] **Step 7: Add end-to-end diagnostic cases**

Add to `VisualGameStudio.Tests/Compiler/CppProjectCliBuildTests.cs`, following its `:104-111`
idiom. **Note its helper's real signature:** `MakeCppProject` takes
`params (string Name, string Content)[]` and already returns a `ProjectFile` — there is no
`ProjectFile.Load` wrapper and no `referenceInclude:`/`hintPath:` parameters. Write the
`<Reference>` into the `.blproj` XML the fixture generates, or add an overload.

Three cases:

```csharp
[Test]
public void NativeProject_WithMissingAssemblyReference_ReportsBL6021AndFails()
{
    var project = MakeCppProjectWithReference("Ghost", "lib\\Ghost.dll");

    var result = CppProjectBuilder.Build(project, "Release");

    Assert.That(result.Diagnostics.Select(d => d.Code), Does.Contain("BL6021"));
    Assert.That(result.Success, Is.False);
}

[Test]
public void NativeProject_WithProjectReference_StillBuilds()
{
    var project = MakeCppProjectWithProjectReference("..\\Sibling\\Sibling.blproj");

    var result = CppProjectBuilder.Build(project, "Release");

    Assert.That(result.Success, Is.True,
        "INERTNESS GATE. The IDE writes <ProjectReference> into native projects with no backend " +
        "filter, and such projects build on master. If this fails, P2a-1 is not inert.");
    Assert.That(result.Diagnostics.Single(d => d.Code == "BL6021").IsWarning, Is.True);
}
```

Plus the CLI leg via `CliTestHarness.RunCli` — **both entry points**, per repo law.

- [ ] **Step 8: Run — fast subset AND the Integration fixture**

```bash
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "TestCategory!=Integration" > test-run.txt 2>&1
```
```bash
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~CppProjectCliBuildTests|FullyQualifiedName~NetReferenceResolverTests" > test-run.txt 2>&1
```

> **Two runs are required.** `CppProjectCliBuildTests` carries class-level
> `[Category("Integration")]` (`:6`), so Step 7's new cases **never execute** in the fast subset.
> Running only the first command would give a false green.

Expected: 3608 + new fast tests, 0 failed; and the Integration fixture green.

- [ ] **Step 9: Commit**

```bash
git add BasicLang/Net/NetReferenceResolver.cs BasicLang/ProjectSystem/CppProjectBuilder.cs BasicLang/Program.cs VisualGameStudio.Tests/
git commit -m "feat(p2a1): resolve .blproj references in EmitCore; BL6021 replaces silent drops"
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

> **Consume Task 4's member walk through a seam; do not re-walk.** Task 4 ends with an internal
> `CandidateMembers(string)` returning `(ISymbol, NetMemberDescriptor)` pairs, shared by `GetMembers`
> and this task. Use it. Two reasons this is not optional:
>
> - **Do not take `INamedTypeSymbol` onto the public surface.** That pushes a Roslyn dependency into
>   the analyzer, which Task 8 then inherits.
> - **Re-walking derived→base yourself re-meets the duplicate-override problem in a second place.**
>   Task 4 already paid for that bug once: an undeduped walk returns `FileStream.Read(byte[],int,int)`
>   twice (from `FileStream` and from `Stream`), which this task would read as `Ambiguous` and report
>   as a spurious **BL6018 on an ordinary `fs.Read(buf, 0, n)`**. The dedup must stay in exactly one
>   place.
>
> Task 5 will additionally want `IsParams` and optional-parameter counts on the descriptor. ⚠ Note a
> type parameter currently spells as bare `T` — indistinguishable from a global type named `T`, and
> unsubstituted for `List<Integer>`. Decide explicitly how overload resolution treats that rather
> than discovering it through a wrong match.

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

> ⚠ **There is no class named `CSharpBackend`.** The file `BasicLang/CSharpBackend.cs` contains
> `ImprovedCSharpCodeGenerator` in namespace `BasicLang.Compiler.CodeGen.CSharp`. The Step 1 snippet
> below is written against `CSharpBackend.AmbientNamespacesForTest` and **will not compile as
> literally written** — use the real type name.
>
> Verified while executing: the count really is **17**, all added unconditionally at one call site,
> and there is no third duplicate of the list in `BasicLang/`. **Order does not matter** — `Generate`
> re-sorts `_usings` alphabetically at emission (`CSharpBackend.cs:423`, `.OrderBy(u => u)`) and the
> candidate collection is a `HashSet<string>`, so insertion order never reached the output. Pin
> **set** equality (`Is.EquivalentTo`), not sequence.
>
> ⚠ **Expect the drift test to be tautological against the obvious mutation, and do not "fix" that.**
> After a genuinely pure extraction, `AmbientNamespacesForTest` aliases the same array, so removing a
> namespace from `NetAmbientNamespaces.All` changes *both* sides of the comparison identically and
> the equivalence test still passes. That is **evidence the duplicate is gone**, not a broken test —
> the count assertion is what catches that mutation. To prove the equivalence test is live, mutate
> the *backend* side instead (simulate a stray `_usings.Add(...)` reintroduced outside the shared
> loop); that is the regression it actually guards, and it does fail.

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

- [ ] **Step 2: Run and verify it fails**

> The test must go through **`TypeRegistry`**, not `NetTypeResolver` directly — the latter was
> built in Task 4 and already passes, so a test written against it is green before Step 3 and
> proves nothing. Drive the assertion through the `TypeRegistry` API the LSP actually calls.
- [ ] **Step 3: Route `TypeRegistry`'s three call sites through `NetTypeResolver`.** Replace the
  bare `catch {}` blocks with logged failures — a swallowed error here is why nobody noticed the
  LSP silently degrading.

> ⛔ **Three things Task 5 measured that this task must handle.** The LSP is the first *concurrent,
> long-lived* consumer of the resolver, which is a different workload from a batch compile.
>
> **(a) Cap `_overloadCache` — it is unbounded and grows worse than `_cache`.** Three reasons,
> measured: the key is a whole *request* (call form + type + member + type-args + args), so entry
> count is **combinatorial** in half-typed identifiers rather than linear in names; entries are
> created **before validation**, so every malformed spelling buys a permanent `NoMatch` entry — i.e.
> *rejecting* a spelling costs a cache slot; and entries hold a `NetMemberDescriptor` with its own
> `Parameters` list, so they are larger than `_cache`'s outcome+symbol pairs. On the IntelliSense
> path, typing `Regex.IsMatch(` one character at a time yields a distinct entry **per keystroke**,
> and the resolver's lifetime is the closure's. ⚠ Note the naive fix (validate before keying) moves
> the invalid-input path from cached to recomputed at **~2 ms per probe** — so bound the cache
> rather than reordering the key.
>
> **(b) Resolver ownership is now load-bearing.** Construction costs 209 ms cold and a stable
> **46–49 ms** for each subsequent fresh construction over the same 168 assemblies *in the same
> process*, and a fresh instance discards the lookup cache (so the ~17 ms miss path recurs). Build
> **one per closure and keep it** — do not construct per LSP request or per debounced pass.
>
> **(c) `DoesNotLoadAssembliesIntoTheProcess` is sensitive to bind volume, not just to
> `Assembly.LoadFrom`.** Task 5 hit this: a mutation that merely *reduced cache hits* caused one
> extra Roslyn bind, which lazily loaded a Roslyn assembly and tripped that test's before/after
> `AppDomain` assembly count — confirmed by reverting and re-running clean twice. So if this task
> changes how often the resolver binds or probes, that test can fail for a reason unrelated to the
> defect it exists to catch. Read its failure carefully before concluding you reintroduced
> `Assembly.LoadFrom`.
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
> ⛔ **"Warning-only" is NOT implementable via `SemanticAnalyzer.Warning(...)`.** Discovered while
> executing; the plan's premise was wrong. `Warning(...)` (`SemanticAnalyzer.cs:1560-1563`) appends a
> `SemanticError` with `ErrorSeverity.Warning` to `_errors`, `Errors` (`:120`) is `=> _errors`
> unfiltered, `Analyze` returns `_errors.Count == 0` (`:894`), and `CompilationResult.HasErrors` is
> `AllErrors.Count > 0` — **neither filters severity**. So a "warning" **fails the build and skips IR
> generation entirely**, the exact opposite of this task's premise.
>
> Findings must therefore go on a **separate `NetDiagnostics` list**, surfacing through
> `CppEmitOutcome.NetReferences.Diagnostics` → `CppDiagnostic { IsWarning = true }` — the only
> non-failing channel in the pipeline. Conveniently this is the same bag constraint (b) already
> required, so both resolve to one design.
>
> ⚠ **Side finding — a shipped product bug, NOT this task's to fix.** The analyzer's **10 pre-existing
> `Warning(...)` call sites are all fatal today** for the same reason: `:4255, :4293, :4414, :4563,
> :4614, :4861, :4873, :5098, :5119, :5508`. At least five are reachable on ordinary VB idiom —
> `If x Then` with a numeric condition is a hard build failure, rendered as
> `Error at line N: Warning at line N, column C: Condition should be Boolean`. Verified through the
> CLI. Chip filed; fixing it is a behavior change P2a-1 forbids.
>
> ⚠ **Corrected line numbers and counts** (the plan's were wrong):
> `CppCapabilityChecker`'s early returns start at **`:590`** (Bridged), not `:598`. `new BasicCompiler`
> is at `CppProjectBuilder.cs:`**`289`**, not `:222` (which is mid-comment). And **"22 entries have no
> `EmitStdLibCall` arm" is really 52** — the table has **58** entries and exactly **6** can produce a
> non-null arm (`Console`, `DateTime`, `DateTimeOffset`, `Decimal`, `Guid`, `TimeSpan`).
> **`StringBuilder` is NOT among them**: it is `NativeOwned`, but every `NativeBclSurface` row for it
> is an instance member, so no static probe succeeds. The spec's "22" was a list of *notable* names,
> not a total.

- [ ] **Step 3: Implement the three-source predicate** exactly as §6.5's table specifies:
  (a) `BoundaryTypeRegistry.Categorize ∈ {NativeOwned, Bridged}`;
  (b) `CppCapabilityChecker`'s early returns (`:598-625`);
  (c) **per call** — `IRBuilder.KnownNetStaticTypes` (`:3644-3664`) **AND** `EmitStdLibCall`
  (`CppCodeGenerator.cs:2210-2347`) returning non-null.

  For (c), extract the arm-existence check into something both `EmitStdLibCall` and the predicate
  call, so they cannot drift.

- [ ] **Step 4: Run — expect PASS**
- [ ] **Step 5: Wire the resolver in, warning-only — WITHOUT touching `ConfigureTypeRegistry`**

- On an unresolved .NET type or member, emit a **warning** on **both** backends (BL6016 / BL6017 /
  BL6018 / BL6023). §6.3's native-error behavior lands in **P2a-2**, not here.
- A **claimed** name (Step 3's predicate) never reaches the resolver at all.
- The resolver is constructed from the `NetReferenceClosure` that Task 3 put on `CppEmitOutcome`,
  threaded through `CompilerOptions` to `new BasicCompiler(...)` (`CppProjectBuilder.cs:222`).

> ⛔ **Two constraints inherited from Task 4 — both found by review, both easy to violate.**
>
> **(a) Bare unclaimed generics get a spurious BL6016 unless you handle arity.** `NetTypeResolver`
> deliberately **requires** generic arity and never guesses: `` List`1 `` resolves, `List` is
> `NotFound`. That decision is correct — guessing arity fabricates bindings, which is the whole
> defect this class exists to remove — and it is safe for `List`/`Dictionary`/`HashSet`/`Task`/`Func`/
> `Action`, because §6.5 row (b) *claims* those so they never reach the resolver. But **unclaimed**
> generics do reach it, and a program can plausibly name them with `System.Collections.Generic`
> ambient: `Queue(Of T)`, `Stack(Of T)`, `SortedDictionary`, `LinkedList`, `Comparer(Of T)`,
> `KeyValuePair(Of K,V)`, `Nullable(Of T)`. Each currently yields `NotFound` → a spurious BL6016
> warning on a valid program. Task 4's code asserts "every caller knows the arity", but that caller
> is *this task* and no test enforces it. **Add a bare-unclaimed-generic test** (`Queue(Of Integer)`
> is the cheapest) and map the BasicLang generic arity onto the metadata name before lookup.
>
> **(c) Task 6's shared constant is currently one-sided — close it here.** `NetAmbientNamespaces.All`
> is consumed by the C# backend but **not** by `NetTypeResolver`, because the resolver has no
> ambient/unqualified-name concept at all: every lookup takes a fully-qualified metadata name. Task 6
> deliberately did not invent one. So when this task gives the analyzer unqualified-name resolution,
> it **must read `NetAmbientNamespaces.All` directly** rather than hand-copy a list — otherwise
> Task 6's drift guard is hollow on the resolver side, and §6.3's equal-behavior claim fails exactly
> where Task 6 was meant to protect it: a namespace the C# backend auto-imports but the resolver does
> not know becomes a spurious **BL6016** natively.
>
> **(b) `NetTypeResolver.Diagnostics` must NOT land on `result.Diagnostics`.** Task 4 added a second
> diagnostic bag beside `NetReferenceClosure.Diagnostics`. `IntelliSenseEmitterTests.cs:393-396`
> pins `Has.None.EqualTo("BL6021")` on `IntelliSenseEmitter.Emit(...).Diagnostics`, with the
> remedy spelled out in its failure message: *the complete record lives on
> `CppEmitOutcome.NetReferences.Diagnostics` instead*. Route the resolver's diagnostics into that
> closure — **merging the two bags, not adding a third** — or that test breaks and the IntelliSense
> path starts denying header regeneration.

> ⛔ **Do NOT call `ConfigureTypeRegistry` from `CompileUnit` in P2a-1.** It is at
> `BasicLang/Compiler.cs:524/529` with the analyzer configured at `:543-544` — and activating it on
> the compile path **un-deadens `SemanticAnalyzer.cs:2075-2088`, which shadows the String/common
> fallbacks at `:2090-2102`.** Concrete divergence: `TypeRegistry.GetTypeName` returns `String()`
> for arrays (`TypeRegistry.cs:565`) while `ResolveNetTypeName` only unwraps `[]` (`:2153`),
> yielding `TypeInfo("String()", Class)` instead of the array `TypeInfo`. That is a behavior change
> to existing programs, which this plan forbids. **Moved to P2a-2** and recorded in the exclusions
> list; when it lands there it needs a pinning test over `LookupNetTypeMember` for the non-P1
> fallback set (`String.Split`, `String.Length`, `Stopwatch.ElapsedMilliseconds`,
> `FileStream.Length`).

- [ ] **Step 6: Prove inertness — the whole point of this task**

```bash
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "TestCategory!=Integration" > test-run.txt 2>&1
```

Expected: **0 failed.** Then the P1 batteries, which prove `Console` and friends still route
natively:

```bash
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~BclBackendParityTests" > test-run.txt 2>&1
```

Expected: all 13 parity programs still byte-identical.

- [ ] **Step 7: Add the diagnostics-level inertness gate**

Neither "0 failed" nor byte-identical stdout can observe a **new warning emitted on every existing
program** — and that is exactly what this task introduces. Add a fast fixture
`VisualGameStudio.Tests/Blnet/NetInertnessTests.cs` that compiles the `IDE/` console and game
templates plus the 13 P1 parity sources and asserts:

```csharp
Assert.That(result.Diagnostics.Select(d => d.Code).Where(c => c.StartsWith("BL60")), Is.Empty,
    "P2a-1 must emit NO new BL60xx diagnostic on a program that compiled clean at dfee728. " +
    "A warning is still new output: it changes CLI stdout, the IDE error list, and LSP squiggles.");
```

Keep this fixture through Tasks 9–16 — it is the standing guard on the plan's central claim.

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

> ⛔ **Mangling from (declaring type, member name, parameter types) is NOT collision-free.** Found by
> review of Task 4, which measured it. Two axes of a CLR signature are not parameter types:
>
> - **Generic method arity.** `IMethodSymbol.MetadataName` carries no arity suffix, so
>   `Task.FromException(Exception)` and `Task.FromException<T>(Exception)` mangle **identically**.
>   Measured across the public framework surface: **37 public types** contain such pairs — including
>   every `Expression.Lambda(...)` / `Lambda<TDelegate>(...)` pair, `ValueTask.FromCanceled`,
>   `IQueryProvider.CreateQuery`/`Execute`, and two `Marshal` members.
> - **Parameter `RefKind`.** `ref`/`out`/`in` are not part of the parameter *type*.
>   `EventSource.Write(String, EventSourceOptions, T)` and `Write(String, ref EventSourceOptions, ref T)`
>   mangle identically.
>
> **Mangle from `(fully-qualified declaring type, member name, arity, [refkind + parameter type]…)`.**
> `NetMemberDescriptor` carries `Arity` and per-parameter `RefKind` as of Task 4's second fix
> specifically so this is possible.
>
> ⚠ **Do not write the distinctness test so that it groups by the mangler's own key** — that is
> tautological and cannot fail. Task 4 shipped exactly that mistake: its collision guard grouped by
> byte-for-byte the key the implementation deduplicated on, so it stayed green while the
> implementation silently deleted 186 members. Assert distinctness against an **independently
> derived** signature identity, and mutation-test it (drop `Arity` from the mangler → must fail on
> `Task`; drop `RefKind` → must fail on `EventSource`).
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

    // Capture VALUES, not the node. The pipeline may mutate in place, in which case a
    // captured node reference makes both sides the same object and the assertion is vacuous.
    var expectedTarget   = FindCall(module).ResolvedNetTarget;
    var expectedCategory = FindCall(module).NetCategory;
    Assert.That(expectedTarget, Is.Not.Null, "guard: the fixture must build a RESOLVED call");

    var pipeline = new OptimizationPipeline();   // BasicLang/IROptimizer.cs:1123
    pipeline.AddStandardPasses();                // :1139 — matches BclE2E.CompileToCppOptimized
    pipeline.Run(module);

    var after = FindCall(module);
    Assert.That(after.ResolvedNetTarget, Is.EqualTo(expectedTarget),
        "The optimizer dropped the resolved .NET target. Every IR node copy/clone path must " +
        "carry it, or P2a-2's lowering silently falls back to name-based dispatch — which is " +
        "the wild-pointer class spec §8.5 exists to prevent.");
    Assert.That(after.NetCategory, Is.EqualTo(expectedCategory));
}
```

> **There is no class named `IROptimizer`.** The entry point is `OptimizationPipeline`
> (`BasicLang/IROptimizer.cs:1123`) with `AddStandardPasses()` (`:1139`) — the same pair
> `BclE2E.CompileToCppOptimized` uses at `CppBclEndToEndTests.cs:56-58` (in
> `VisualGameStudio.Tests/Compiler/`, not `Blnet/`). All four citations verified correct.
>
> ⛔ **The test above is TAUTOLOGICAL and cannot fail — proven by mutation.** Step 3's premise
> ("update **every** copy/clone/visit path in `IROptimizer` that reconstructs these nodes") is
> **false: there are none.** All **20** `new IRCall(...)` sites are in `IRBuilder.cs`, building fresh
> nodes from the AST; `IROptimizer.cs` has **zero**. Both clone helpers —
> `FunctionInliningPass.CloneAndRemap` (`:1381`) and `LoopUnrollingPass.CloneInstruction` (`:2245`) —
> fall through to `default: return inst;` for a call, returning the **same object**. Every other pass
> mutates in place (`ConstantFolding:167`, `ConstantPropagation:1569`, `TailCall:1461`) or refuses to
> move calls (`LICM:968`, `LoopUnrolling:2044`, `LoopFusion:2564`); CSE and Peephole never mention
> `IRCall`; DCE removes only BinaryOp/UnaryOp/Compare/Load. **The fields survive by aliasing, not by
> copy logic.**
>
> Demonstrated: adding a dropping `case IRCall` to `CloneAndRemap` left the test above **GREEN**,
> while an aggressive-pipeline test went **RED**. The reason is that **`FunctionInliningPass` is in
> `AddAggressivePasses`, NOT `AddStandardPasses`** — so a standard-pipeline round trip never reaches
> the only clone path an `IRCall` can take. `AddStandardPasses` adds ConstantFolding,
> CopyPropagation, DCE, CSE, StrengthReduction and Peephole (ConstantPropagation is commented out).
>
> **Keep the standard-pipeline test as a future regression guard, but the load-bearing test must run
> the AGGRESSIVE pipeline.** Both clone `default` arms now carry a note that any `IRCall` case added
> there must copy both fields.
>
> ⚠ **`BoundaryTypeCategory.NativeOwned` is `0`.** A bare auto-property therefore defaults **every
> call in every program** to "natively handled" — the most dangerous possible wrong answer.
> Initialize explicitly to `Unknown` and pin it.
>
> ⚠ **`NetCategory` belongs on the CALL, not on the IR type descriptor.** The plan floated
> `TypeInfo`; that is wrong. The marker answers "how must *this* dispatch lower" — a call-site
> property — and `TypeInfo` is a mutable descriptor shared across the front end whose identity feeds
> codegen, so widening it is exactly the behavior-changing risk P2a-1 forbids. It would also be
> ambiguous when receiver and return types differ in category.
>
> ⚠ **`ResolvedNetTarget` stays null through all of P2a-1.** `IRBuilder` has no `NetTypeResolver`
> (the warning-only one runs in `CppProjectBuilder`), so Step 3's "populate them in IRBuilder" is only
> half-achievable. `NetCategory` *is* populated there via `BoundaryTypeRegistry.Categorize`, a pure
> static lookup with no Roslyn, references or I/O.
>
> ⛔ **P2a-2 GAP — instance .NET calls have NO carriage.** `obj.Method()` lowers to
> `IRInstanceMethodCall` (`IRBuilder.cs:3355`), which is a **sibling** of `IRCall` under `IRValue`,
> **not a subclass**, so it inherits neither field. `Regex.IsMatch(s)` is covered; `someRegex.Match(s)`
> is not — and instance calls are the majority of real .NET usage. P2a-2 must add the same two fields
> to `IRInstanceMethodCall`.

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

[Test]
public void ShimAbiConstantComesFromTheContract() =>
    Assert.That(BlnetShimSources.ShimAbiCs, Does.Contain($"= {BlnetContract.AbiVersion};"),
        "The generated shim's ABI constant must be interpolated from BlnetContract.AbiVersion. " +
        "The existing pin (BlnetContractTests.cs:71) covers the HAND shim only, whose ShimAbi is " +
        "hand-appended to BlnetStatus.cs:26-27 — unusable by a generated shim.");
```

> **The ABI constant needs its own file.** It cannot be appended to the status enum, because the
> first test asserts `BlnetStatusCs` is **byte-equal** to `GenerateStatusEnumCs()`. Emit a separate
> `ShimAbi.g.cs`. This is the third of the three §12.4 shim drift invariants §17 assigns to P2a-1.

- [ ] **Step 2: Run and verify it fails**
- [ ] **Step 3: Implement** `BlnetShimSources` mirroring `BlnetRuntimeSources.cs` exactly: a
  `public static class`, verbatim-string constants, and an XML `<summary>` naming both the spec
  section (§8.1) and the drift fixture that pins it — the convention every source-of-truth class in
  this repo follows. Expose `HandleTable`, `BlnetStatusCs` and `ShimAbiCs`.

> **Namespace decision, needed here and consumed by Task 14:** the byte-equality test pins
> `namespace BlnetTestShim;` from `HandleTable.cs:3`. Either keep that namespace in the generated
> shim (simplest — the name is arbitrary and never crosses the ABI), or parameterize it and relax
> the test to compare modulo the namespace line. Pick one **now** and write it down; Task 14's
> `Exports.g.cs` must agree.
>
> ✅ **DECIDED while executing: the generated shim keeps `namespace BlnetTestShim;` verbatim.** Three
> consequences Task 14 must honor:
> 1. `Exports.g.cs` **must** declare `namespace BlnetTestShim;` or it cannot see `HandleTable`.
> 2. `BlnetStatusCs` carries **no namespace** — invariant 2 pins it byte-for-byte to
>    `GenerateStatusEnumCs()`, which emits none. So `BlnetStatus` lands in the **global namespace** in
>    a generated shim while sitting inside `BlnetTestShim` in the hand shim. This compiles (lookup
>    from inside `BlnetTestShim` falls out to global) and is the single shape divergence between the
>    two shims. **Do not "fix" it by prepending a namespace — that breaks invariant 2.**
> 3. The emitted `.csproj` **must** set `ImplicitUsings=enable`: `HandleTable` depends on it for
>    `List<T>`/`Stack<T>` and for LINQ's `Count(predicate)` in `AliveCount`.
>
> ⚠ **Invariants 2 and 3 are TAUTOLOGICAL under a correct implementation** and cannot fail today:
> `BlnetStatusCs => BlnetContract.GenerateStatusEnumCs()` compares a delegation to its own target.
> Their value is as tripwires against a *future* edit that inlines the text or hard-codes the number
> — mutation confirmed both go red under exactly that. **Only invariant 1 is a genuine two-copy drift
> test.** A fourth test was added whose two sides are both hand-written scaffolding in
> `BlnetShimSources` (namespace agreement between `HandleTable` and `ShimAbiCs`); that is the model
> to follow if real teeth are wanted.
>
> ⚠ **`core.autocrlf=true` on this machine and `.gitattributes` sets `* text=auto`.** The
> `.Replace("\r\n", "\n")` normalization in invariant 1 is therefore **load-bearing, not cosmetic** —
> on a fresh clone `HandleTable.cs` and/or the embedded constant will be CRLF. Never simplify it away.
>
> ⛔ **`BlnetContract.CoreExportNames` DOES NOT EXIST.** The only occurrence repo-wide is in *this
> plan document*. The real export-name list lives inline in `BlnetContractTests.cs:92-93` and in the
> `blnet.h` text. **Task 14's Step-1 snippet iterates `BlnetContract.CoreExportNames` and will not
> compile.** Task 14 must either add it to `BlnetContract` as a genuine source of truth (preferred —
> it is exactly the kind of constant that class exists to own, and `blnet.h` plus the test both want
> it) or read the existing list; it must **not** hand-copy a third parallel list.
>
> ℹ️ `BlnetRuntimeSources` is `public static class`, so `BlnetShimSources` mirrors that rather than
> the folder's `internal` default — the source-of-truth family (`BlnetContract`, `BlnetRuntimeSources`)
> is public and drift-paired.
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

- [ ] **Step 3b: Emit `blnet_startup.g.cpp` — the §9.3 startup contract**

The spec explicitly delegates the details here ("the plan fixes the exact text", §9.3), and Task
13's own test requires this file in `request.SourceFiles`. Three symbols are **new** — verified
absent from `BlnetRuntimeSources.cs`, which declares only the `BlnetNativeVtable` *type* (`:59-62`),
the seven export-name macros (`:65-71`), and `inline ShimApi g_shim` (`:100`) with the comment
*"filled by the host: harness now, generated startup in P2"*:

| Symbol | Job |
|---|---|
| `blnet_load_module(const char*)` | `LoadLibrary`/`dlopen`, returns an opaque handle |
| `blnet_bind_core(void*)` | binds P0's seven exports into `g_shim` |
| `g_native_vtable` | the native side of P0's 2-slot positional vtable |

Put these in `BlnetRuntimeSources.cs` (add it to this task's Files list, and note
`BlnetRuntimeSourcesTests` will pin them) — they are transport-neutral and P2b reuses two of three.

The handshake is **two-argument**: `g_shim.initialize(BLNET_ABI_VERSION, &g_native_vtable)`
(`BlnetRuntimeSources.cs:66, 93`). Failure text is normative so Task 14's tests can assert on it:

| Failure | Message | Stream | Exit |
|---|---|---|---|
| module not found | `blnet: failed to load '<name>' (<oserr>)` | stderr | 3 |
| a core export missing | `blnet: shim is missing export '<name>'` | stderr | 3 |
| ABI mismatch | `blnet: shim ABI <got>, expected <want>` | stderr | 3 |
| `initialize` non-OK | `blnet: initialize failed (status <n>)` | stderr | 3 |

Ownership per §9.5: a **static-initializer object** in this TU calls `blnet_startup()` in its
constructor and `blnet_shutdown()` in its destructor, covering both `emitMain == true` and a
user-written `main()`. Document the static-init-order constraint; §9.2's null-slot guard turns any
violation into a clear error rather than a crash.

- [ ] **Step 4: Run — expect PASS**
- [ ] **Step 5: Commit**

```bash
git commit -m "feat(p2a1): NetSurface, NetProxyEmitter and the blnet startup contract; empty surface emits nothing"
```

---

## Task 13: `CppProjectBuilder` phase model, gate merge, cancellation

**The riskiest "inert" task** — restructuring `EmitCore` must produce byte-identical generated C++
for every existing project.

> ⛔ **EVERY `CppProjectBuilder.cs` LINE NUMBER BELOW HAS DRIFTED.** Tasks 3, 7 and 8 all edited this
> file. Verified real anchors at Task 12's commit (`bef38b6`) — re-verify before use, they will move
> again:
>
> | What | Plan says | Actually |
> |---|---|---|
> | `objGenDir` | — | **`:195`** |
> | `emitMain` (`isExe && basicLangMainCount == 1`) | `:262` | **`:397`** |
> | `split` declared null | `:265` | **`:400`** |
> | `obj/gen` clean + write | `:323-325` / `:326-327` | **`:458-462`** |
> | `generatedTus` | `:338-340` | **`:473-475`** |
> | `request.SourceFiles` | `:414` | **`:549`** |
> | include path | `:419-420` | **`:555`** |
>
> ⛔ **`CleanGeneratedDir` (`:893-905`) deletes only `.g.cpp` and `.g.h`.** Task 12 emits **five**
> artifacts — `blnet.h`, `blnet_runtime.hpp`, `blnet_bindings.g.hpp`, `blnet_proxies.g.hpp`,
> `blnet_startup.g.cpp` — and **four of them survive that filter**. So when a project stops using
> .NET, a removed member's proxy header lingers on the include path and can still be `#include`d.
> **This task must widen the clean**, and must not widen it so far that it deletes user files.
>
> ℹ️ **§9.1 lists six ENTRIES, not six files:** five files plus the `shim/` directory, which is phase
> 5's (`NetShimGenerator`, Task 14). §10.1 requires phases 1–4 to give full C++ IntelliSense without
> ever publishing, so `blnet_startup.g.cpp` is the **only** TU
> (`NetProxyEmitter.TranslationUnitFileNames`).

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

- [ ] **Step 3b: Make the phase model explicit, and give tests a surface seam**

Name §10.1's seven phases in code (an enum or explicit method-per-phase) and record which run
under `forIntelliSense: true`:

| # | Phase | IntelliSense? |
|---|---|---|
| 1 | Resolve references (Task 3) | yes |
| 2 | BL → IR | yes |
| 3 | Collect .NET surface | yes |
| 4 | Emit native (incl. proxy artifacts) | yes |
| 5 | Generate + publish shim | **no** |
| 6 | Compile + link | no |
| 7 | Deploy | no |

Phase 3 has no collector in P2a-1 — it returns an **empty** `NetSurface`. Two of this task's three
acceptance tests need a *non-empty* one, so add an **internal seam**: an optional
`NetSurface? surfaceOverride` parameter on `EmitCore` (or a settable internal property on the
options), used only by tests. Without it those tests are unwritable.

Add **BL6025** here: a **library output** (`emitMain == false` *and* not an executable —
`emitMain` is `isExe && basicLangMainCount == 1`, `CppProjectBuilder.cs:262`) with a non-empty
surface is rejected, per §9.5 and §14.12. With an empty surface it builds exactly as today, so
this is inert.

- [ ] **Step 4: Run — expect PASS**
- [ ] **Step 5: Prove byte-identical output — the acceptance criterion for this task**

```bash
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~CppBackendTests|FullyQualifiedName~CppCollectionTests|FullyQualifiedName~CppBclEndToEndTests" > test-run.txt 2>&1
```

Expected: **108 pre-existing C++ fixtures unchanged** — the plan's "118" was wrong; the real count
was measured at **108 / 0 / 0** both before and after the change. This task's entire success
criterion is "nothing changed", and these are what prove it. ⚠ **Always measure the baseline
yourself before changing anything** rather than trusting a number in this document.

> ⛔ **BL6025's gate must be `!isExe`, NOT `emitMain == false`.** `emitMain` is
> `isExe && basicLangMainCount == 1`, so `!emitMain` is *also* true for an **executable with a
> hand-written C++ `main()`** — a shape §9.5 explicitly supports. Gating on `emitMain` rejects it.
> `!isExe ⟹ !emitMain`, so `!isExe` is the correct and narrower condition. Pinned by a test.
>
> ⛔ **This task is NOT the right home for fixing the uncancellable NuGet restore**, despite what the
> earlier note in this plan says. `PackageManager.RestoreAsync` takes **no `CancellationToken`** and
> is shared with the C# backend, so widening it is a separate change. Cancellation here is honored
> **between phases** only; a restore already in flight runs to completion. The stale comment in
> `RestorePackagesForClosure` was corrected rather than left as a promise the code does not keep.
>
> ℹ️ **Cancellation throws** `OperationCanceledException` naming the phase rather than returning a
> failed result — a cancel must not look like a build error in the IDE. Existing callers cannot
> observe it (default `CancellationToken.None`), and **no caller passes a token yet**, so the IDE/CLI
> wiring remains to be done.
>
> ℹ️ **BL6025 is bypassed for IntelliSense**, consistent with every other build-rule gate
> (BL6007/BL6005/BL6009) — enforcing a link-time rule in the editor would cost the user every
> generated header. One condition to flip, with a test pinning it either way.
>
> ⛔ **`ShimAssemblyName` is `<SafeProject>.Blnet`** — invented in Task 13 because the spec never
> names it. **Task 14's `NetShimGenerator` must name its csproj from that same function**, or
> `blnet_load_module` looks for the wrong DLL at runtime.

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

  Two more the spec requires and that are writable only here, since both producers now exist:

```csharp
[Test]
public void ExportsIncludeP0sSevenCoreNames()
{
    var cs = NetShimGenerator.EmitExports(OneMemberSurface());

    foreach (var name in BlnetContract.CoreExportNames)
        Assert.That(cs, Does.Contain($"EntryPoint = \"{name}\""),
            "A generated shim must export P0's seven core names too, not only surface-derived " +
            "wrappers — blnet_bind_core (Task 12) binds them at startup. blnet_abi_version must " +
            "return BlnetContract.AbiVersion and blnet_initialize must return " +
            "BLNET_E_VERSION_MISMATCH when the caller's ABI differs.");
}

[Test]
public void ProxyTableSlotsMatchTheSurfaceDerivedExports()
{
    var surface = OneMemberSurface();
    var slots   = NetProxyEmitter.EmitBindings(surface).SlotNames;
    var exports = NetShimGenerator.SurfaceDerivedExportNames(surface);

    Assert.That(slots, Is.EquivalentTo(exports),
        "Spec §12.4. Scoped to SURFACE-DERIVED exports deliberately — the shim also exports P0's " +
        "seven core names and §8.6's array copy helpers, which are not BlnetProxyTable slots, so " +
        "an unscoped equality is false by construction.");
}
```
- [ ] **Step 2: Run and verify it fails**
- [ ] **Step 3: Implement**, emitting the pattern `Exports.cs:82-93` proves, with §8.2's null
  handling. `TrimmerSingleWarn=false` is load-bearing for Task 16 — without it ILC collapses
  per-assembly warnings and the mapper has nothing to parse.

> ⛔ **NEVER name a generated C# file `*.g.cs` in the shim.** Roslyn classifies `*.g.cs` as
> **auto-generated and silently disables every nullable annotation** — the first publish emitted
> **8 × CS8669**. Give the verbatim splices spec §9.1's real names (`HandleTable.cs`,
> `BlnetStatus.cs`, `ShimAbi.cs`) — §12.4 forbids prepending a directive to them — and put an
> explicit `#nullable enable` at the top of `Exports.g.cs`. Guard with
> `Does.Not.Contain("warning CS")`, **not** `"warning IL"`: §12.3 deliberately inverts that for
> generated shims.
>
> ⛔ **§8.2's inline `rv is null ? 0UL : Table.Create(rv)` does not compile for a value-type result**
> (CS0037), and §8.3's default row sends **every enum** down the handle path. Route returns through a
> `ToHandle(object?)` helper carrying exactly that body.
>
> ⛔ **§8.5's `Unsafe.Unbox<T>` needs value-type-ness, which `NetMemberDescriptor` does not carry.**
> Pass an optional `valueTypeReceiverNames` set rather than mutating the shared descriptor — the
> collector holds the `ITypeSymbol`. **Only receivers need it**: one cast spelling serves reference
> and value *parameters*, and returns need nothing.
>
> ⚠ **§8.6's array helpers and §8.4's delegate dispatcher are NOT emitted.** Both need element-type /
> delegate-ness a descriptor doesn't carry, and both are §12.4-exempt. Emitting ~13 speculative
> uncalled helpers per shim would be worse than the documented gap. P2a-2 owns them.
>
> ⚠ **The §12.4 slot/export equality test is a WIRING TRIPWIRE, not an oracle** — both sides call
> `NetNameMangler` on the same descriptors, so it is true by construction. It catches "someone
> invented a second naming scheme"; nothing subtler. The real oracle added beside it parses the C
> function-pointer signatures out of `blnet_bindings.g.hpp` and the C# signatures out of
> `Exports.g.cs` and compares them through a C→C# table **owned by neither producer** — that one
> catches a width/arity divergence, i.e. stack corruption rather than cosmetic drift.
>
> ⚠ **Two §8.3 wire tables now exist** — `NetProxyEmitter.WireOf` (C) and `NetShimGenerator.WireOf`
> (C#). The signature test above is the **only** thing holding them together; a new §8.3 row must be
> added to both.
>
> ⛔ **`Directory.Build.props` hazard for Tasks 15/16.** The generated shim lands under the *user's*
> `obj/gen/shim/`, so any `Directory.Build.props` above it is imported **before** the csproj body and
> can rewrite §8.1's properties out from under the shim. `ImportDirectoryBuildProps=false` does
> **not** help — it is read too late. The publish test deliberately writes outside the repo to avoid
> this.
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
- [ ] **Step 3: Implement.** Also make clean drop the shim cache: `BuildService.CleanAsync`
  (`VisualGameStudio.ProjectSystem/Services/BuildService.cs:307`) today deletes only
  `config.OutputPath`, never `obj/`. Add the shim cache directory. (Add that file to this task's
  Files list.)
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
- **`IDE/BasicLang.deps.json`** — tracked, and a dependency-set change invalidates it.
- **`IDE/VisualGameStudio.deps.json`** and `IDE/VisualGameStudio.ProjectSystem.dll` — the IDE host's
  deps file **duplicates BasicLang's dependency closure** (today only
  `Microsoft.Extensions.Logging.Console` + `OmniSharp.Extensions.LanguageServer`), and the IDE loads
  BasicLang **in-process** (`BuildService.cs:1169`). Refreshing only `IDE/BasicLang.deps.json`
  leaves the prebuilt IDE throwing `FileNotFoundException` for `Microsoft.CodeAnalysis` on the first
  compile — **invisible to every `dotnet test` run**, because the tests build from source.

Verify the `"BasicLang/1.0.0"` dependencies block in `IDE/VisualGameStudio.deps.json` now names
`Microsoft.CodeAnalysis.CSharp`. With `SatelliteResourceLanguages=en` (Task 4) the 26 locale DLLs do
not appear — confirm with `git status` that no `cs/`, `de/`, `fr/`… folders showed up under `IDE/`.

Then extend Step 6's verification to compile a project **through the prebuilt
`IDE/VisualGameStudio.exe`**, not just from source. That is the only thing that exercises the
shipped deps files.

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
- No `NetSurfaceCollector` — surfaces are hand-fed via Task 13's internal seam, in tests only.
- No typed-catch lowering, no collection consumption, no outbound array copy, no delegates.
- The resolver **warns**; it never fails a build. §6.3's native-error behavior is P2a-2.
- **The C# backend is NOT wired.** Task 8's Step 5 says "warning-only on **both** backends" and spec
  §6.3 gives the C# backend a warning row, but `CompilerOptions.NetResolverFactory` is set at exactly
  one site — `CppProjectBuilder.cs:321`. Every C#-backend path (`Program.cs:506`, `:1022`,
  `BuildService.cs:624`, `MultiTargetCompiler.cs:237`) leaves it null, so **no C# project can produce
  BL6016/BL6017/BL6023**. Deliberate: wiring it adds spurious-warning risk to a path P2a-1 gains
  nothing from, and Task 8's gates already prove the resolver on the native backend. **§6.3's C#
  warning row moves to P2a-2.** The seam itself is backend-agnostic (it lives in `SemanticAnalyzer`),
  so this is a one-line change when P2a-2 wants it.
- **The LSP is not wired either** — `DocumentManager` calls only `ConfigureTypeRegistry`. So no
  BL60xx reaches editor squiggles in P2a-1; findings appear only in CLI/IDE build output.
- **BL6018 is not emitted, and BL6017 is member-existence only.** `ResolveOverload` has **zero**
  product callers, deliberately: it answers `NoMatch` when *any* argument type is unknown, and
  `Visit(CallExpressionNode)`'s early-return chain has no single point where every argument is typed.
  Wiring it in a warning-only task is how you manufacture spurious warnings. Spec §11.4 defines
  BL6017 as "member not found / **no matching overload**" — the second half, and all of BL6018,
  land in P2a-2.
- **`ConfigureTypeRegistry` is NOT wired into `CompileUnit`** (`BasicLang/Compiler.cs:524/529`,
  analyzer configured at `:543-544`). Doing so un-deadens `SemanticAnalyzer.cs:2075-2088`, which
  shadows the String/common fallbacks at `:2090-2102` — a behavior change to existing programs.
  P2a-2 takes it, with a pinning test over `LookupNetTypeMember` for the non-P1 fallback set.
- `<ProjectReference>` is a **warning** here; P2a-2 promotes it to an error.
- `BasicLang/ExternalLibraryLoader.cs:169`'s `Assembly.LoadFrom` channel (reachable via
  `Import … From`) is untouched.
- No parity programs and no generated-shim conformance suite.

## The one place inertness is knowingly traded

Stated explicitly so it is not mistaken for an oversight. Found by review after Task 3 shipped.

**A native project that declares `<Reference>` or `<PackageReference>` changes behavior.** On
`master` the element is parsed into the model and silently dropped, so the project builds. After
Task 3 it resolves — and if it does not resolve, the build fails **BL6021**. For
`<PackageReference>` the build path additionally creates `obj/`, prints
`Restoring packages for <name>...`, and **may reach nuget.org over the network**.

This is the intended feature (§5: "not silence"; Task 3 Step 4: "An unrestorable package is
BL6021"), not a regression. But note the trade is *reachable by real users*, by the same kind of
route that made `<ProjectReference>` a warning rather than an error:

- `BasicLang/Program.cs:339-368` — `basiclang add package <id>` writes `<PackageReference>` into
  whatever project `FindProjectFile` returns, with **no `IsNativeProject` check**.
- `SolutionExplorerViewModel.cs:625-627 → :689` — "Add Project Reference" has no backend filter
  either (which is *why* `<ProjectReference>` is only a warning).

So the honest statement of this plan's central claim is: **no project that compiled clean at
`dfee728` and declares no reference elements changes in any way.** A project that *does* declare
one was already silently broken; it now says so. No repo test or IDE template creates such a
project (verified by grep — nothing anywhere builds a native project with a `<PackageReference>`),
so the suite gained no network dependency.

⚠ Two consequences for later tasks: the blocking restore has **no timeout, no cancellation, and no
IDE-visible progress** (`HttpClient`'s default 100 s, no `CancellationToken`, progress written to
`Console` which the GUI Shell discards). **Task 13** threads a `CancellationToken` through `Build`
and is the right home for fixing this.

### The second trade: Task 7 changes live LSP behavior — for the better

Task 7 is the first task touching **running product code**, so "inert" there means *no observable
change except the defects being fixed*. Two changes are observable, both deliberate:

1. **.NET completions get dramatically better.** `TypeRegistry.BuildIndex()` was spending **7295 ms
   to produce a completely empty index**: the LSP runs on net8.0 while `DetectDotNetSdkPath` supplies
   the *newest* installed reference pack, and `Assembly.LoadFrom` cannot load a reference assembly
   built for a higher framework than the running runtime. **141 of 164 assemblies failed with
   `FileLoadException`, every one swallowed by a bare `catch {}`** — reproduced independently
   (`succeeded 23, failed 141, types seen 0`). Every .NET completion the IDE has ever offered came
   from the small `PreloadCoreTypes` fallback. After: **827 ms, 0 diagnostics, 1430 types.**
2. **By-ref/pointer parameter spelling changes** in signature help for `PreloadCoreTypes` types:
   `Int32&` → `Integer&`, `Int32*` → `Integer*`. Reflection's ladder never matched the primitive arm
   (`typeof(int).MakeByRefType() != typeof(int)`) and fell through to `Type.Name`, while the metadata
   producer re-added the suffix to the VB-mapped name. Both now use the VB spelling, which every
   other name the class produces already used. This **is** a user-visible change; it is deliberate
   and pinned by a dedicated test (the whole-type producer-agreement test does **not** catch it —
   `Regex` has no by-ref parameters).

⚠ **Two pre-existing defects found but NOT fixed** (chips filed) — both gate whether users actually
see improvement 1:

- **`LoadIndexFromCache` treats an empty/parseable cache as success** (`:324-352`; returns `true` at
  `:346` for a zero-line file), so `DocumentManager.cs:55` never calls `BuildIndex()`. On a machine
  with a stale cache the fix is **inert**; fresh installs do run it.
- **`DetectDotNetSdkPath:1194-1219` sorts reference packs with `OrderByDescending(d => d)` — a
  STRING sort on the path.** With `10.0.0`, `8.0.23`, `9.0.12` installed it picks **9.0.12**, since
  `"9" > "8" > "1"`. net10.0 is silently skipped.

⚠ **Three limitations recorded for later tasks:**

- **`CoreLibraryFileNames` keys on file name only.** A `<Reference>` pointing at a stray *facade*
  `System.Runtime.dll` (a pure type-forwarder declaring no `System.Object`) satisfies
  `bringsOwnCoreLibrary`, suppresses the framework fallback, and silently restores the error-symbol
  base types the reference-set rule exists to prevent.
- **`_requestedPaths` only grows**, so after `BuildIndex` every later user reference is read against
  the reference pack rather than the runtime.
- **`TypeRegistry.Diagnostics` has no product consumer** — failures reach stderr only, so an end user
  still sees nothing in the editor. Task 8 or P2a-2 should surface them.

⚠ **Thread safety — do not remove the lock on the theory that the LSP is single-threaded.**
`TypeRegistry` is a DI **singleton** shared across all LSP documents (`DocumentManager.cs:137`); its
five collections are now under one `_stateLock`. The reason this never bit before Task 7 is subtle
and worth keeping: OmniSharp marks didOpen/didChange/didSave `[Serial]` and
completion/hover/definition `[Parallel]`, scheduled under an **outer `Concat` over groups**, so
mutation never overlaps reads under normal scheduling. The hole is
`DefaultRequestInvoker.RouteRequest`'s `Observable.Amb(Timer, handler)` — when the timer wins the
scheduler advances **while the handler keeps running**, and the sync handlers never observe their
`CancellationToken`. Task 7 changed *volume*, not concurrency, which raised both the odds of hitting
that hatch and the damage when hit. Damage is not "a lost entry": a concurrent insert during a
resize can leave a bucket chain cyclic so a later `TryGetValue` **spins forever**.

⚠ **Never let a fixture write the user-scope LSP cache.** `BuildIndex` → `SaveIndexToCache` writes
the fixed path `%LOCALAPPDATA%\BasicLang\namespace_index.json` and clears it first. Task 7's tests
did this and left the real cache pointing at deleted temp DLLs — which, because
`LoadIndexFromCache` then returns `true`, **reinstated the exact empty-index failure the task
existed to fix.** `TypeRegistry` now has an `internal TypeRegistry(string cacheFilePath)` seam; any
fixture constructing one **must** use it.
