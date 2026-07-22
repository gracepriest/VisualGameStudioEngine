# Per-backend C++ compiler & debugger overrides — Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a developer set, per backend (llvm/gcc/msvc), an explicit compiler and debugger path in IDE Settings; a set path is authoritative (used at build/F5, marks the backend available in the wizard even off PATH) and is validated in the dialog; blank = today's auto-probe / lldb-dap chain.

**Architecture:** A pure `ToolchainPathValidator` + a DI-singleton `CppToolchainOverrides` reader (both in `VisualGameStudio.ProjectSystem`) sit over six new flat `cpp.toolchain.*` settings keys. The compiler override rides `CppProjectBuilder`'s existing `resolveById` seam plus one new `resolveToolchain` param, injected by `BuildService`; the debugger override resolves at the F5 site in `MainWindowViewModel` and rides a new optional `DebugConfiguration.AdapterExecutableOverride` honored by `DebugService`; the wizard reacts automatically because `CppToolchainProbeService` OR-s override validity into availability. The BasicLang compiler layer stays settings-agnostic (override injected as data only).

**Tech Stack:** C# / .NET, Avalonia (Settings dialog AXAML), NUnit (`VisualGameStudio.Tests`), the BasicLang compiler (`BasicLang/`).

**Spec:** `docs/superpowers/specs/2026-07-21-cpp-per-backend-toolchain-overrides-design.md`

---

## Standing constraints (do not violate)

- **`C:\winlibs\mingw64` stays OFF PATH.** Tests must not depend on winlibs; use **fake existing-file fixtures** (a temp file that exists) for "usable off-PATH" cases.
- Never round-trip repo files through PowerShell `Get-Content`/`Set-Content`. Use Edit/Write. Commit messages via a file + `git commit -F`.
- **After AXAML changes (Task 11): `dotnet clean` before build.**
- No new `InternalsVisibleTo` — all new types are `public` in `VisualGameStudio.ProjectSystem` (already referenced by `VisualGameStudio.Tests`); BasicLang→Tests IVT already exists for the compiler-side tests.
- Validate codegen through the optimizer/CLI helper (`CompileToCppOptimized` in `CppCollectionTests.cs`) for Task 4/5, not only the non-optimizing helper.
- Test **both** entry points: a compiler-side change must keep the CLI (`Program.cs`, no override params) behaving exactly as today.
- Suite output exceeds tool truncation — redirect full-suite runs to a file. Per-test `--filter` runs are small.

## File structure

**NEW (all in `VisualGameStudio.ProjectSystem/Services/`):**
- `ToolchainPathValidator.cs` — pure: `(backendId, kind, path)` → `ValidationResult{Status,Message,DetectedVersion?}`; `Usable` = Valid∪Warning. Injectable `fileExists`/`dirExists`/`versionProbe` seams.
- `CppToolchainOverrides.cs` — DI singleton over `ISettingsService`; the six key constants/helpers, `ResolveCompiler(id)`/`ResolveDebugger(id)` → tri-state `ToolchainOverride{State,ResolvedPath,Message}`, consumer registration.

**NEW tests (`VisualGameStudio.Tests/`):** `ToolchainPathValidatorTests.cs`, `CppToolchainOverridesTests.cs`, `CppToolchainSchemaTests.cs`, `CppToolchainExplicitPathTests.cs`, `CppProjectBuilderResolveToolchainTests.cs`, `BuildServiceToolchainOverrideTests.cs`, `IntelliSenseEmitterOverrideTests.cs`, `CppToolchainProbeOverrideTests.cs`, `DebugServiceAdapterOverrideTests.cs`, `MwvmDebuggerOverrideSourceGuardTests.cs`, `SettingsDialogToolchainFieldsGuardTests.cs`, `ToolchainOverrideIntegrationTests.cs`.

**MODIFIED:** `BasicLang/ProjectSystem/CppToolchain.cs`, `BasicLang/ProjectSystem/CppProjectBuilder.cs`, `BasicLang/ProjectSystem/IntelliSenseEmitter.cs`, `VisualGameStudio.ProjectSystem/Services/BuildService.cs`, `VisualGameStudio.ProjectSystem/Services/CppToolchainProbeService.cs`, `VisualGameStudio.ProjectSystem/Services/SettingsService.cs`, `VisualGameStudio.Core/Abstractions/Services/IDebugService.cs` (DebugConfiguration), `VisualGameStudio.ProjectSystem/Services/DebugService.cs`, `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs`, `VisualGameStudio.Shell/ViewModels/Dialogs/SettingsViewModel.cs`, `VisualGameStudio.Shell/Views/Dialogs/SettingsDialog.axaml`, `VisualGameStudio.Shell/Configuration/ServiceConfiguration.cs`.

---

## Task 0: Worktree + baseline

**Files:** none (setup).

- [ ] **Step 1: Create the worktree** (per superpowers:using-git-worktrees). From the repo root:

```powershell
git worktree add ../vgs-toolchain-overrides -b cpp-toolchain-overrides master
```

- [ ] **Step 2: Confirm the baseline suite is green.** Run from the worktree and redirect (exceeds truncation):

```powershell
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release 2>&1 | Tee-Object baseline.txt | Select-Object -Last 5
```

Expected: `Passed!` (baseline ≈ 3410 passed / 1 known cpp-game-app BL6009 flake — exit 1 from that flake is normal / 2 skips). Record the number.

- [ ] **Step 3: Commit nothing yet** — Task 0 is a checkpoint. Proceed to Task 1 in the worktree.

---

## Task 1: `ToolchainPathValidator` (pure)

**Files:**
- Create: `VisualGameStudio.ProjectSystem/Services/ToolchainPathValidator.cs`
- Test: `VisualGameStudio.Tests/ToolchainPathValidatorTests.cs`

Design mirrors `LanguageService.ResolveLspPathOverride` (`LanguageService.cs:226`): whitespace-null gate → `Trim()` → injectable `fileExists ?? File.Exists`. Add `dirExists` (for the MSVC VS-install-dir case) and a `versionProbe` seam.

- [ ] **Step 1: Write the failing tests.**

```csharp
using NUnit.Framework;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests;

[TestFixture]
public class ToolchainPathValidatorTests
{
    // fake filesystem: only these paths "exist"
    private static System.Func<string, bool> Files(params string[] present) =>
        p => System.Array.IndexOf(present, p) >= 0;

    [Test]
    public void Empty_Path_Is_Empty()
    {
        var r = ToolchainPathValidator.Validate("llvm", ToolchainSlotKind.Compiler, "  ");
        Assert.That(r.Status, Is.EqualTo(ToolchainPathStatus.Empty));
        Assert.That(r.Usable, Is.False);
    }

    [Test]
    public void Missing_Compiler_Is_Invalid()
    {
        var r = ToolchainPathValidator.Validate("llvm", ToolchainSlotKind.Compiler,
            @"C:\nope\clang++.exe", fileExists: Files());
        Assert.That(r.Status, Is.EqualTo(ToolchainPathStatus.Invalid));
    }

    [Test]
    public void Recognized_Clang_That_Smokes_Is_Valid_With_Version()
    {
        var r = ToolchainPathValidator.Validate("llvm", ToolchainSlotKind.Compiler,
            @"C:\llvm\bin\clang++.exe",
            fileExists: Files(@"C:\llvm\bin\clang++.exe"),
            versionProbe: _ => new VersionProbeResult(Ran: true, Ok: true, Version: "clang version 18.1.8"));
        Assert.That(r.Status, Is.EqualTo(ToolchainPathStatus.Valid));
        Assert.That(r.DetectedVersion, Does.Contain("18.1.8"));
        Assert.That(r.Usable, Is.True);
    }

    [Test]
    public void Existing_But_Unrecognized_Basename_Is_Warning_And_Never_Executes()
    {
        var executed = false;
        var r = ToolchainPathValidator.Validate("llvm", ToolchainSlotKind.Compiler,
            @"C:\tools\my-wrapper.exe",
            fileExists: Files(@"C:\tools\my-wrapper.exe"),
            versionProbe: _ => { executed = true; return new VersionProbeResult(true, true, "x"); });
        Assert.That(r.Status, Is.EqualTo(ToolchainPathStatus.Warning));
        Assert.That(executed, Is.False, "must not run --version on an unrecognized binary");
        Assert.That(r.Usable, Is.True);
    }

    [Test]
    public void Msvc_Compiler_Direct_Vcvars_Is_Valid()
    {
        var bat = @"C:\VS\VC\Auxiliary\Build\vcvars64.bat";
        var r = ToolchainPathValidator.Validate("msvc", ToolchainSlotKind.Compiler, bat,
            fileExists: Files(bat));
        Assert.That(r.Status, Is.EqualTo(ToolchainPathStatus.Valid));
        Assert.That(r.ResolvedPath, Is.EqualTo(bat));
    }

    [Test]
    public void Msvc_Compiler_Install_Dir_Derives_Vcvars()
    {
        var dir = @"C:\VS";
        var bat = @"C:\VS\VC\Auxiliary\Build\vcvars64.bat";
        var r = ToolchainPathValidator.Validate("msvc", ToolchainSlotKind.Compiler, dir,
            fileExists: Files(bat), dirExists: Files(dir));
        Assert.That(r.Status, Is.EqualTo(ToolchainPathStatus.Valid));
        Assert.That(r.ResolvedPath, Is.EqualTo(bat));
    }

    [Test]
    public void Msvc_Compiler_Pointed_At_ClExe_Is_Invalid()  // the silent trap
    {
        var cl = @"C:\VS\bin\cl.exe";
        var r = ToolchainPathValidator.Validate("msvc", ToolchainSlotKind.Compiler, cl,
            fileExists: Files(cl));
        Assert.That(r.Status, Is.EqualTo(ToolchainPathStatus.Invalid));
    }

    [Test]
    public void Msvc_Debugger_Valid_LldbDap_Is_Warning_With_Pdb_Advisory()
    {
        var p = @"C:\llvm\bin\lldb-dap.exe";
        var r = ToolchainPathValidator.Validate("msvc", ToolchainSlotKind.Debugger, p,
            fileExists: Files(p),
            versionProbe: _ => new VersionProbeResult(true, true, "lldb-dap 22"));
        Assert.That(r.Status, Is.EqualTo(ToolchainPathStatus.Warning));
        Assert.That(r.Message, Does.Contain("PDB"));
        Assert.That(r.Usable, Is.True);
    }
}
```

- [ ] **Step 2: Run to verify failure.**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~ToolchainPathValidatorTests"`
Expected: FAIL to compile (`ToolchainPathValidator` / `ToolchainSlotKind` / `VersionProbeResult` do not exist).

- [ ] **Step 3: Implement `ToolchainPathValidator.cs`.**

```csharp
using System;
using System.IO;
using System.Linq;

namespace VisualGameStudio.ProjectSystem.Services;

public enum ToolchainSlotKind { Compiler, Debugger }
public enum ToolchainPathStatus { Empty, Valid, Warning, Invalid }

public readonly record struct VersionProbeResult(bool Ran, bool Ok, string? Version);

public readonly record struct ValidationResult(
    ToolchainPathStatus Status, string Message, string? DetectedVersion, string? ResolvedPath)
{
    public bool Usable => Status is ToolchainPathStatus.Valid or ToolchainPathStatus.Warning;
}

/// <summary>
/// Pure validator for a user-configured compiler/debugger path. Existence is the
/// authoritative gate; a recognized-driver basename additionally enables a --version
/// smoke (never run on an unrecognized binary). Mirrors LanguageService.ResolveLspPathOverride's
/// injectable-existence shape so it is headless-testable.
/// </summary>
public static class ToolchainPathValidator
{
    private static readonly string[] LlvmDrivers = { "clang++", "clang", "clang-cl" };
    private static readonly string[] GccDrivers  = { "g++", "c++", "gcc" };
    private static readonly string[] DapDrivers   = { "lldb-dap" };

    public static ValidationResult Validate(
        string backendId, ToolchainSlotKind kind, string? path,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? dirExists = null,
        Func<string, VersionProbeResult>? versionProbe = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new(ToolchainPathStatus.Empty, "", null, null);

        var exists = fileExists ?? File.Exists;
        var dirs   = dirExists ?? Directory.Exists;
        var trimmed = path.Trim();
        var id = backendId?.Trim().ToLowerInvariant();

        // MSVC compiler slot: must resolve to a vcvars64.bat (directly or via a VS-install dir).
        if (kind == ToolchainSlotKind.Compiler && id == "msvc")
        {
            var vcvars = ResolveVcvars(trimmed, exists, dirs);
            return vcvars != null
                ? new(ToolchainPathStatus.Valid, "vcvars64.bat found", null, vcvars)
                : new(ToolchainPathStatus.Invalid,
                    "Point at a vcvars64.bat or a Visual Studio install directory (cl.exe is not valid here).",
                    null, null);
        }

        if (!exists(trimmed))
            return new(ToolchainPathStatus.Invalid, "File not found — fix the path or clear it.", null, null);

        // Existence passed. Enrichment: only smoke a recognized driver basename.
        var basename = Path.GetFileNameWithoutExtension(trimmed).ToLowerInvariant();
        var recognized = RecognizedFor(id, kind).Contains(basename);

        // msvc.debugger is honestly limited even when the binary is fine.
        var pdbAdvisory = kind == ToolchainSlotKind.Debugger && id == "msvc"
            ? " lldb-dap can't read MSVC PDB — breakpoints may not bind." : "";

        if (!recognized)
            return new(ToolchainPathStatus.Warning, ("Found; unrecognized name — using anyway." + pdbAdvisory).Trim(),
                null, trimmed);

        var probe = versionProbe ?? RealVersionProbe;
        var vr = probe(trimmed);
        if (vr.Ran && vr.Ok)
        {
            // recognized + smoked clean; msvc.debugger stays Warning (PDB) despite a clean smoke.
            return pdbAdvisory.Length > 0
                ? new(ToolchainPathStatus.Warning, ("Found." + pdbAdvisory).Trim(), vr.Version, trimmed)
                : new(ToolchainPathStatus.Valid, "Valid.", vr.Version, trimmed);
        }
        return new(ToolchainPathStatus.Warning, ("Found; couldn't confirm version — using anyway." + pdbAdvisory).Trim(),
            null, trimmed);
    }

    /// <summary>Resolve a vcvars64.bat from a .bat path or a VS-install dir; null if neither.</summary>
    public static string? ResolveVcvars(string path, Func<string, bool> exists, Func<string, bool> dirs)
    {
        if (path.EndsWith("vcvars64.bat", StringComparison.OrdinalIgnoreCase) && exists(path))
            return path;
        if (dirs(path))
        {
            var derived = Path.Combine(path, "VC", "Auxiliary", "Build", "vcvars64.bat");
            if (exists(derived)) return derived;
        }
        return null;
    }

    private static string[] RecognizedFor(string? id, ToolchainSlotKind kind) =>
        kind == ToolchainSlotKind.Debugger ? DapDrivers
        : id == "gcc" ? GccDrivers
        : id == "llvm" ? LlvmDrivers
        : Array.Empty<string>();

    private static VersionProbeResult RealVersionProbe(string exePath)
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exePath, "--version")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
            var outText = p!.StandardOutput.ReadToEnd();
            var ok = p.WaitForExit(3000) && p.ExitCode == 0;
            return new(true, ok, ok ? outText.Split('\n').FirstOrDefault()?.Trim() : null);
        }
        catch { return new(true, false, null); }
    }
}
```

- [ ] **Step 4: Run to verify pass.**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~ToolchainPathValidatorTests"`
Expected: PASS (8 tests).

- [ ] **Step 5: Commit.**

```powershell
git add VisualGameStudio.ProjectSystem/Services/ToolchainPathValidator.cs VisualGameStudio.Tests/ToolchainPathValidatorTests.cs
git commit -F <message-file>   # feat(cpp): pure ToolchainPathValidator (existence gate + gated version smoke + msvc rules)
```

---

## Task 2: `CppToolchainOverrides` reader + DI singleton

**Files:**
- Create: `VisualGameStudio.ProjectSystem/Services/CppToolchainOverrides.cs`
- Modify: `VisualGameStudio.Shell/Configuration/ServiceConfiguration.cs` (register singleton)
- Test: `VisualGameStudio.Tests/CppToolchainOverridesTests.cs`

The reader owns the six key names, reads via `ISettingsService.Get<string>(key, "")`, applies the validator, and returns a tri-state. It registers its settings consumers (mirroring `ClangdLocator.cs:70`).

- [ ] **Step 1: Write the failing tests.** Use a fake `ISettingsService` (a minimal in-memory one — see `SettingsConsumerContractTests` for the existing fake, or write a tiny stub implementing only `Get<T>`). Assert: blank → `None`; a fake existing g++ path → `Usable` with `ResolvedPath`; a missing path → `Invalid`; `ResolveCompiler("msvc")` with a fake vcvars → `Usable`. Also assert `SettingsConsumerRegistry.IsRegistered("cpp.toolchain.gcc.compiler")` after a resolve call.

```csharp
[Test]
public void Blank_Override_Is_None()
{
    var ov = new CppToolchainOverrides(new FakeSettings(), fileExists: _ => true);
    Assert.That(ov.ResolveCompiler("gcc").State, Is.EqualTo(OverrideState.None));
}

[Test]
public void Set_Existing_Gcc_Is_Usable_And_Registers_Consumer()
{
    var settings = new FakeSettings { ["cpp.toolchain.gcc.compiler"] = @"C:\w\g++.exe" };
    var ov = new CppToolchainOverrides(settings, fileExists: p => p == @"C:\w\g++.exe");
    var r = ov.ResolveCompiler("gcc");
    Assert.That(r.State, Is.EqualTo(OverrideState.Usable));
    Assert.That(r.ResolvedPath, Is.EqualTo(@"C:\w\g++.exe"));
    Assert.That(SettingsConsumerRegistry.IsRegistered("cpp.toolchain.gcc.compiler"), Is.True);
}

[Test]
public void Set_Missing_Is_Invalid()
{
    var settings = new FakeSettings { ["cpp.toolchain.llvm.compiler"] = @"C:\nope.exe" };
    var ov = new CppToolchainOverrides(settings, fileExists: _ => false);
    Assert.That(ov.ResolveCompiler("llvm").State, Is.EqualTo(OverrideState.Invalid));
}
```

(Provide `FakeSettings` as a small `ISettingsService` stub in the test file — only `Get<string>(key, default, scope)` needs real behavior; the rest can throw `NotImplementedException`. Prefer reusing any existing test fake if one exists.)

- [ ] **Step 2: Run to verify failure.**
Run: `dotnet test ... --filter "FullyQualifiedName~CppToolchainOverridesTests"` → FAIL (type missing).

- [ ] **Step 3: Implement `CppToolchainOverrides.cs`.**

```csharp
using System;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

public enum OverrideState { None, Usable, Invalid }

public readonly record struct ToolchainOverride(OverrideState State, string? ResolvedPath, string Message);

/// <summary>
/// Reads the six per-backend cpp.toolchain.* override paths, validates them, and
/// returns a tri-state. DI singleton; the single reader for BuildService, DebugService's
/// F5 caller (MainWindowViewModel) and CppToolchainProbeService.
/// </summary>
public sealed class CppToolchainOverrides
{
    public static string CompilerKey(string id) => $"cpp.toolchain.{id}.compiler";
    public static string DebuggerKey(string id) => $"cpp.toolchain.{id}.debugger";
    public static readonly string[] Backends = { "llvm", "gcc", "msvc" };

    private readonly ISettingsService _settings;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, bool> _dirExists;
    private readonly Func<string, VersionProbeResult>? _versionProbe;

    public CppToolchainOverrides(ISettingsService settings,
        Func<string, bool>? fileExists = null, Func<string, bool>? dirExists = null,
        Func<string, VersionProbeResult>? versionProbe = null)
    {
        _settings = settings;
        _fileExists = fileExists ?? System.IO.File.Exists;
        _dirExists = dirExists ?? System.IO.Directory.Exists;
        _versionProbe = versionProbe;
    }

    public ToolchainOverride ResolveCompiler(string id) => Resolve(id, ToolchainSlotKind.Compiler, CompilerKey(id),
        $"CppToolchainOverrides → {id} compiler path override");

    public ToolchainOverride ResolveDebugger(string id) => Resolve(id, ToolchainSlotKind.Debugger, DebuggerKey(id),
        $"CppToolchainOverrides → {id} debugger path override");

    private ToolchainOverride Resolve(string id, ToolchainSlotKind kind, string key, string consumerDesc)
    {
        SettingsConsumerRegistry.RegisterConsumer(key, consumerDesc);
        var raw = _settings?.Get<string>(key, "") ?? "";
        var vr = ToolchainPathValidator.Validate(id, kind, raw, _fileExists, _dirExists, _versionProbe);
        return vr.Status switch
        {
            ToolchainPathStatus.Empty => new(OverrideState.None, null, ""),
            ToolchainPathStatus.Invalid => new(OverrideState.Invalid, null, vr.Message),
            _ => new(OverrideState.Usable, vr.ResolvedPath, vr.Message), // Valid or Warning
        };
    }

    /// <summary>Forces consumer registration for all six keys (for the settings contract test).</summary>
    public void RegisterAllConsumers()
    {
        foreach (var id in Backends) { ResolveCompiler(id); ResolveDebugger(id); }
    }
}
```

- [ ] **Step 4: Register the DI singleton.** In `ServiceConfiguration.cs`, next to the existing `AddSingleton<ICppToolchainProbe, CppToolchainProbeService>()` (`:132`):

```csharp
services.AddSingleton<CppToolchainOverrides>();
```

- [ ] **Step 5: Run to verify pass.**
Run: `dotnet test ... --filter "FullyQualifiedName~CppToolchainOverridesTests"` → PASS. Also build the Shell to confirm DI compiles: `dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release`.

- [ ] **Step 6: Commit.** `feat(cpp): CppToolchainOverrides reader (tri-state, consumer registration) + DI singleton`

---

## Task 3: Settings schema — six keys + consumer force

**Files:**
- Modify: `VisualGameStudio.ProjectSystem/Services/SettingsService.cs:1258-1271` (the `cpp` schema `Properties` list)
- Modify: `VisualGameStudio.Tests/SettingsConsumerContractTests.cs` (`ForceAllDialogConsumers`, ~:57-94)
- Test: `VisualGameStudio.Tests/CppToolchainSchemaTests.cs`

- [ ] **Step 1: Write the failing test.**

```csharp
[Test]
public void All_Six_Toolchain_Keys_Are_In_Schema_With_Empty_Default()
{
    var svc = new SettingsService(/* same ctor the other schema tests use */);
    foreach (var id in CppToolchainOverrides.Backends)
        foreach (var key in new[] { CppToolchainOverrides.CompilerKey(id), CppToolchainOverrides.DebuggerKey(id) })
        {
            Assert.That(svc.GetAllKnownKeys(), Does.Contain(key), key);      // or whatever the schema-key accessor is
            Assert.That(svc.Get<string>(key, "sentinel"), Is.EqualTo(""));   // schema default seeds ""
        }
}
```

(Match the exact `SettingsService` construction + key-enumeration API used by the existing `SettingsConsumerContractTests.EveryDialogSettingKey_ExistsInSchema` at `:162` — copy that fixture's setup.)

- [ ] **Step 2: Run to verify failure.** → FAIL (keys not in schema).

- [ ] **Step 3: Add the six `Prop(...)` entries** to the `cpp` schema `Properties` list (`SettingsService.cs:1266`, after the lldbDap Prop):

```csharp
Prop(CppToolchainOverrides.CompilerKey("llvm"), SettingsPropertyType.String, "LLVM compiler path", "",
    "Path to clang++.exe. Blank = auto-detect on PATH."),
Prop(CppToolchainOverrides.DebuggerKey("llvm"), SettingsPropertyType.String, "LLVM debugger path", "",
    "Path to a DAP adapter (lldb-dap.exe). Blank = default lldb-dap locator."),
Prop(CppToolchainOverrides.CompilerKey("gcc"), SettingsPropertyType.String, "GCC compiler path", "",
    "Path to g++.exe. Blank = auto-detect on PATH."),
Prop(CppToolchainOverrides.DebuggerKey("gcc"), SettingsPropertyType.String, "GCC debugger path", "",
    "Path to a DAP adapter (lldb-dap.exe). Blank = default lldb-dap locator."),
Prop(CppToolchainOverrides.CompilerKey("msvc"), SettingsPropertyType.String, "MSVC compiler path", "",
    "Path to vcvars64.bat or a Visual Studio install directory. Blank = auto-detect via vswhere."),
Prop(CppToolchainOverrides.DebuggerKey("msvc"), SettingsPropertyType.String, "MSVC debugger path", "",
    "Path to a DAP adapter. Note: lldb-dap can't read MSVC PDB — breakpoints may not bind."),
```

(`SettingsService.cs` already `using`s the ProjectSystem services namespace? Add the `using VisualGameStudio.ProjectSystem.Services;` if not — `CppToolchainOverrides` lives there, same assembly.)

- [ ] **Step 4: Force the new consumer in the contract test.** In `SettingsConsumerContractTests.ForceAllDialogConsumers` (`:57-94`), next to the `_ = ClangdLocator.Locate(service);` line (`:75`), add:

```csharp
new VisualGameStudio.ProjectSystem.Services.CppToolchainOverrides(service).RegisterAllConsumers();
```

- [ ] **Step 5: Run to verify pass.**
Run: `dotnet test ... --filter "FullyQualifiedName~CppToolchainSchemaTests|FullyQualifiedName~SettingsConsumerContractTests"`
Expected: PASS (schema test + the existing contract guards — the six keys are NOT yet in the dialog inventory, so `EveryDialogSettingKey_*` don't cover them until Task 11; the schema + this force-line keep everything green now, and Task 11's dialog items land already-covered).

- [ ] **Step 6: Commit.** `feat(cpp): register six cpp.toolchain.* settings keys + force consumer in contract test`

---

## Task 4: `CppToolchain.FromExplicit` factories

**Files:**
- Modify: `BasicLang/ProjectSystem/CppToolchain.cs` (after `TryFindById`, ~:87)
- Test: `VisualGameStudio.Tests/CppToolchainExplicitPathTests.cs`

- [ ] **Step 1: Write the failing tests.** Assert `FromExplicit("llvm", path)` produces a toolchain whose `DriverName` == the full path (so `compile_commands.json` names it), and `FromExplicit("msvc", vcvars)` yields `Kind == Msvc`. Validate a full compile command uses the override path via `CompileToCppOptimized` (see `CppCollectionTests.cs`) — a minimal C++ project pinned with an explicit override should emit a build/compile-command referencing the override path.

```csharp
[Test]
public void FromExplicit_Llvm_DriverName_Is_Full_Path()
{
    var tc = CppToolchain.FromExplicit("llvm", @"C:\llvm\bin\clang++.exe");
    Assert.That(tc, Is.Not.Null);
    Assert.That(tc!.DriverName, Is.EqualTo(@"C:\llvm\bin\clang++.exe"));
    Assert.That(tc.Kind, Is.EqualTo(CppToolchainKind.ClangLike));
}

[Test]
public void FromExplicit_Msvc_Is_Msvc_Kind()
{
    var tc = CppToolchain.FromExplicit("msvc", @"C:\VS\VC\Auxiliary\Build\vcvars64.bat");
    Assert.That(tc!.Kind, Is.EqualTo(CppToolchainKind.Msvc));
    Assert.That(tc.DriverName, Is.EqualTo("cl"));
}
```

- [ ] **Step 2: Run to verify failure.** → FAIL (`FromExplicit` missing).

- [ ] **Step 3: Implement.** In `CppToolchain.cs` after `TryFindById` (`:87`):

```csharp
/// <summary>
/// Build a toolchain from an explicit, user-configured path (Settings override).
/// llvm/gcc: <paramref name="resolvedPath"/> is the compiler exe (invoked by full
/// path, bypassing PATH). msvc: <paramref name="resolvedPath"/> is a vcvars64.bat
/// (the existing cmd+vcvars mechanism is reused). Null for an unknown id.
/// </summary>
public static CppToolchain FromExplicit(string id, string resolvedPath) => id?.Trim().ToLowerInvariant() switch
{
    "llvm" => new CppToolchain("clang++ (configured)", resolvedPath),
    "gcc"  => new CppToolchain("g++ (configured)", resolvedPath),
    "msvc" => new CppToolchain("MSVC (cl.exe, configured)", "cmd.exe", resolvedPath),
    _ => null,
};
```

- [ ] **Step 4: Run to verify pass.**
Run: `dotnet test ... --filter "FullyQualifiedName~CppToolchainExplicitPathTests"` → PASS.

- [ ] **Step 5: Commit.** `feat(cpp): CppToolchain.FromExplicit factory (full-path llvm/gcc; vcvars msvc)`

---

## Task 5: `CppProjectBuilder.Build` — `resolveToolchain` param

**Files:**
- Modify: `BasicLang/ProjectSystem/CppProjectBuilder.cs:67-73`
- Test: `VisualGameStudio.Tests/CppProjectBuilderResolveToolchainTests.cs`

- [ ] **Step 1: Write the failing test.** A minimal unpinned C++ `ProjectFile`; call `Build(project, "Debug", resolveToolchain: () => CppToolchain.FromExplicit("llvm", fakePath))` and assert the outcome's toolchain identity is the override (DriverName == fakePath). Also assert `Build(project, "Debug")` with NO override params behaves as today (defaults to `CppToolchain.Find`).

- [ ] **Step 2: Run to verify failure.** → FAIL (Build has no `resolveToolchain` param).

- [ ] **Step 3: Implement.** Change `Build`'s signature and the `EmitCore` call:

```csharp
public static CppProjectBuildResult Build(ProjectFile project, string configuration,
    Func<string, CppToolchain> resolveById = null,
    Func<CppToolchainAvailability> probeAvailability = null,
    Func<CppToolchain> resolveToolchain = null)          // NEW
{
    var result = new CppProjectBuildResult();
    var emit = EmitCore(project, configuration, result,
        resolveToolchain ?? CppToolchain.Find,           // was: CppToolchain.Find
        forIntelliSense: false, resolveById, probeAvailability);
    ...
```

- [ ] **Step 4: Run to verify pass.**
Run: `dotnet test ... --filter "FullyQualifiedName~CppProjectBuilderResolveToolchainTests"` → PASS.
Also confirm the CLI is unaffected: `dotnet build BasicLang/BasicLang.csproj -c Release` (Program.cs still calls `Build(projectFile, configuration)` — the new param defaults null).

- [ ] **Step 5: Commit.** `feat(cpp): CppProjectBuilder.Build accepts an override-aware resolveToolchain (CLI unchanged)`

---

## Task 6: `BuildService` — inject reader, override-aware resolvers, pre-validate

**Files:**
- Modify: `VisualGameStudio.ProjectSystem/Services/BuildService.cs` (ctor `:38-47`; `BuildCppProject` `:1090-1102`)
- Modify: `VisualGameStudio.Shell/Configuration/ServiceConfiguration.cs` (BuildService registration — verify the container supplies the new dep)
- Test: `VisualGameStudio.Tests/BuildServiceToolchainOverrideTests.cs`

- [ ] **Step 1: Write the failing tests** (BuildService is DI-constructed but testable — see `BuildServicePipelineTests`). Construct a `BuildService` with a fake `IOutputService` + a `CppToolchainOverrides` over a fake `ISettingsService` + fake `fileExists`. Cases:
  1. Pinned gcc + usable gcc override (fake existing g++), nothing on PATH → build resolves via the override path (assert the compile command / build message names the override).
  2. **Unpinned discriminating tie-break:** llvm on PATH (fake TryFindById), usable gcc override → selects llvm (fixed order), NOT gcc.
  3. Pinned gcc + **Invalid** gcc override while gcc also "on PATH" → `BuildResult` is a failure whose message names the bad path + Settings › C++; assert it did NOT fall back to PATH.
  4. `Build`-path with no override configured → unchanged behavior.

(For #2's "llvm on PATH" without a real clang, inject the resolvers through the seam: BuildService builds `resolveToolchain`/`resolveById` from the overrides reader **plus** an injectable PATH probe. To keep it testable, thread a `Func<string, CppToolchain>` PATH-resolver seam into `BuildService`'s override-resolver construction defaulting to `CppToolchain.TryFindById`, so tests can fake "llvm on PATH".)

- [ ] **Step 2: Run to verify failure.** → FAIL (ctor has no overrides dep; resolvers not wired).

- [ ] **Step 3: Implement.**
  - Add `CppToolchainOverrides` to the ctor (and a test-only optional `Func<string,CppToolchain> pathResolve = null` defaulting to `CppToolchain.TryFindById`, plus `Func<CppToolchain> pathFind = null` → `CppToolchain.Find`). Keep the existing `BuildService(IOutputService)` convenience ctor delegating with a `new CppToolchainOverrides(...)` built from a settings service — OR require the settings service. Simplest: primary ctor `BuildService(IOutputService, ProjectSerializer, CppToolchainOverrides)`; keep a convenience ctor for existing callers/tests that don't exercise overrides.
  - In `BuildCppProject`, before calling `CppProjectBuilder.Build`, build the override-aware resolvers and pre-validate:

```csharp
var requestedId = project /* the ProjectFile */ .CppToolchain;

// Pre-validate a pinned, set-but-Invalid override → hard error (even if on PATH).
if (!string.IsNullOrEmpty(requestedId))
{
    var pin = _overrides.ResolveCompiler(requestedId);
    if (pin.State == OverrideState.Invalid)
        return FailCppBuild(
            $"{requestedId} compiler path is set but not found: {pin.Message} " +
            "Fix or clear it in Settings › C++.");
}

Func<string, CppToolchain> resolveById = id =>
{
    var r = _overrides.ResolveCompiler(id);
    return r.State == OverrideState.Usable
        ? CppToolchain.FromExplicit(id, r.ResolvedPath)
        : _pathResolve(id);                                  // blank → probe (Invalid pre-handled)
};

Func<CppToolchain> resolveToolchain = () =>
{
    foreach (var id in CppToolchainOverrides.Backends)       // fixed order llvm,gcc,msvc
    {
        var r = _overrides.ResolveCompiler(id);
        if (r.State == OverrideState.Usable) return CppToolchain.FromExplicit(id, r.ResolvedPath);
        if (r.State == OverrideState.None && _pathResolve(id) is { } tc) return tc;
        // Invalid → non-candidate, skip
    }
    return null;                                             // → BL6005 in EmitCore
};

var buildResult = CppProjectBuilder.Build(projectFile, configuration, resolveById, probeAvailability: null, resolveToolchain);
```

(`FailCppBuild` = compose a failed `BuildResult` with the message via the existing output/diagnostic path — reuse whatever `BuildCppProject` already does for a BL-level failure. Do NOT fire a different code path than the existing failure surface.)

- [ ] **Step 4: Run to verify pass.**
Run: `dotnet test ... --filter "FullyQualifiedName~BuildServiceToolchainOverrideTests"` → PASS. Build the Shell to confirm DI still resolves BuildService's new dep.

- [ ] **Step 5: Commit.** `feat(cpp): BuildService wires per-backend compiler overrides (pinned + unpinned precedence, invalid→hard error)`

---

## Task 7: `IntelliSenseEmitter` — override-aware `resolveById`

**Files:**
- Modify: `BasicLang/ProjectSystem/IntelliSenseEmitter.cs:40-45`
- Modify: the IDE caller that drives IntelliSense regen (grep for `IntelliSenseEmitter.Emit(` — likely a regen coordinator in `VisualGameStudio.ProjectSystem`) to pass an override-aware `resolveById`.
- Test: `VisualGameStudio.Tests/IntelliSenseEmitterOverrideTests.cs`

- [ ] **Step 1: Write the failing test.** A C++ project pinned to `gcc` with a usable gcc override (fake existing g++) → the emitted `compile_commands.json` names the override g++ (not `clang++`). Drive `IntelliSenseEmitter.Emit(project, cfg, resolveById: id => CppToolchain.FromExplicit(id, fakeGpp))` and read the produced compile-commands driver.

- [ ] **Step 2: Run to verify failure.** → FAIL (`Emit` has no `resolveById` param).

- [ ] **Step 3: Implement.** Add an optional `resolveById` param to `Emit` and forward it to `EmitCore`:

```csharp
public static CppProjectBuildResult Emit(
    ProjectFile project, string configuration, CppToolchain toolchain = null,
    Func<string, CppToolchain> resolveById = null)               // NEW
{
    var result = new CppProjectBuildResult();
    var outcome = CppProjectBuilder.EmitCore(
        project, configuration, result, () => toolchain, forIntelliSense: true, resolveById);
    result.Success = outcome.Completed;
    return result;
}
```

Then update the IDE regen caller to pass `resolveById: id => { var r = overrides.ResolveCompiler(id); return r.State == OverrideState.Usable ? CppToolchain.FromExplicit(id, r.ResolvedPath) : CppToolchain.TryFindById(id); }` (reuse the same lambda shape as Task 6 — consider a small shared helper on `CppToolchainOverrides`, e.g. `CppToolchain? UsableCompilerToolchain(string id)`, to avoid duplicating the lambda in BuildService, IntelliSense, and tests — DRY).

- [ ] **Step 4: Run to verify pass.**
Run: `dotnet test ... --filter "FullyQualifiedName~IntelliSenseEmitterOverrideTests"` → PASS.

- [ ] **Step 5: Commit.** `feat(cpp): IntelliSense compile_commands honors a pinned compiler override (build/edit parity)`

---

## Task 8: `CppToolchainProbeService` — authoritative-when-set availability

**Files:**
- Modify: `VisualGameStudio.ProjectSystem/Services/CppToolchainProbeService.cs:10-16`
- Test: `VisualGameStudio.Tests/CppToolchainProbeOverrideTests.cs`

- [ ] **Step 1: Write the failing tests.** Inject a fake `ISettingsService` + fake `fileExists`. Cases: gcc override usable, gcc off PATH → `Probe().Gcc == true`; gcc override **Invalid** while gcc "on PATH" → `Probe().Gcc == false`; blank → falls to the pure PATH result (unchanged). (For "gcc on PATH" determinism without a real g++, thread an injectable base-probe seam into `CppToolchainProbeService` defaulting to `CppToolchain.ProbeAvailability`.)

- [ ] **Step 2: Run to verify failure.** → FAIL.

- [ ] **Step 3: Implement.** Inject `CppToolchainOverrides` (+ an optional `Func<CppToolchainAvailability> baseProbe = null` test seam → `CppToolchain.ProbeAvailability`). Per backend apply the authoritative-when-set rule:

```csharp
public ToolchainAvailability Probe()
{
    var basep = (_baseProbe ?? CppToolchain.ProbeAvailability)();
    bool Avail(string id, bool onPath) => _overrides.ResolveCompiler(id).State switch
    {
        OverrideState.None => onPath,       // blank → probe
        OverrideState.Usable => true,       // set & usable → available
        OverrideState.Invalid => false,     // set & broken → greyed (even if on PATH)
        _ => onPath,
    };
    return new ToolchainAvailability(
        Avail("llvm", basep.Llvm), Avail("gcc", basep.Gcc), Avail("msvc", basep.Msvc));
}
```

Register the new ctor dep in DI (it's already a singleton; the container supplies `CppToolchainOverrides`).

- [ ] **Step 4: Run to verify pass.**
Run: `dotnet test ... --filter "FullyQualifiedName~CppToolchainProbeOverrideTests"` → PASS. Build the Shell.

- [ ] **Step 5: Commit.** `feat(cpp): probe availability honors a set compiler override (authoritative-when-set)`

---

## Task 9: `DebugConfiguration.AdapterExecutableOverride` + `DebugService` honors it

**Files:**
- Modify: `VisualGameStudio.Core/Abstractions/Services/IDebugService.cs:239-255` (DebugConfiguration)
- Modify: `VisualGameStudio.ProjectSystem/Services/DebugService.cs:239`
- Test: `VisualGameStudio.Tests/DebugServiceAdapterOverrideTests.cs`

- [ ] **Step 1: Write the failing test.** Assert `ResolveAdapterLaunch` (or a small extracted helper on it) uses `config.AdapterExecutableOverride` as the launch `FileName` when non-null, and falls to `descriptor.ResolveLaunchCommand()` when null. (If `ResolveAdapterLaunch` is private, extract the override-vs-descriptor decision into an `internal static` pure helper `ResolveCommand(DebugConfiguration, DebugAdapterDescriptor)` returning `DapLaunchCommand?` and test that — no new IVT needed since tests are in-assembly? DebugService is in ProjectSystem; make the helper `public static`.)

- [ ] **Step 2: Run to verify failure.** → FAIL.

- [ ] **Step 3: Implement.**
  - Add to `DebugConfiguration` (next to `AdapterId`): `public string? AdapterExecutableOverride { get; set; }`.
  - In `ResolveAdapterLaunch` at `:239`, replace `var command = descriptor.ResolveLaunchCommand();` with:

```csharp
var command = config.AdapterExecutableOverride is string ovPath
    ? new DapLaunchCommand(ovPath, string.Empty)
    : descriptor.ResolveLaunchCommand();
```

(The existing null-check block at `:240-247` stays — a non-null override yields a non-null command, so it passes the "installed" gate.)

- [ ] **Step 4: Run to verify pass.**
Run: `dotnet test ... --filter "FullyQualifiedName~DebugServiceAdapterOverrideTests"` → PASS.

- [ ] **Step 5: Commit.** `feat(cpp): DebugConfiguration.AdapterExecutableOverride honored by DebugService`

---

## Task 10: MWVM F5 site — per-backend debugger override + DI + source-guard

**Files:**
- Modify: `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs` (ctor `:358`; `StartDebuggingAsync` `:3556`, `:3582-3674`)
- Test: `VisualGameStudio.Tests/MwvmDebuggerOverrideSourceGuardTests.cs` (source-guard — MWVM is not constructed in tests) + reuse `CppToolchainOverridesTests` for the pure `ResolveDebugger` tri-state.

- [ ] **Step 1: Write the failing tests.** Pure behavior is already covered by `CppToolchainOverridesTests` (add a `ResolveDebugger` blank/usable/invalid trio if missing). Add a **source-guard** test asserting `StartDebuggingAsync`'s source resolves the debugger override and aborts on invalid — grep the MWVM source for the required tokens (wcq source-guard pattern):

```csharp
[Test]
public void StartDebugging_Resolves_Per_Backend_Debugger_Override()
{
    var src = File.ReadAllText(MwvmPath);   // resolve the repo-relative path like other source-guards do
    Assert.That(src, Does.Contain("ResolveDebugger"));
    Assert.That(src, Does.Contain("AdapterExecutableOverride"));
    Assert.That(src, Does.Contain("IsNativeBuild"));   // override only for native projects
}
```

- [ ] **Step 2: Run to verify failure.** → FAIL (tokens absent).

- [ ] **Step 3: Implement.**
  - Ctor: add `CppToolchainOverrides overrides` param; assign `_toolchainOverrides = overrides;` (container supplies the singleton).
  - In `StartDebuggingAsync`, replace the installed-check block (`:3582-3595`) so it accounts for the override, and set the config field (`:3664-3674`):

```csharp
var descriptor = _debugAdapterRegistry.GetFor(_projectService.CurrentProject);
if (descriptor is null) { OutputPanel.AppendOutput("No debug adapter serves this project type.\n"); return; }

string? debuggerOverridePath = null;
var proj = _projectService.CurrentProject;
if (proj.IsNativeBuild && !string.IsNullOrEmpty(proj.CppToolchain))
{
    var dr = _toolchainOverrides.ResolveDebugger(proj.CppToolchain);
    if (dr.State == OverrideState.Invalid)
    {
        OutputPanel.AppendOutput(
            $"Error: {proj.CppToolchain} debugger path is set but not found: {dr.Message} " +
            "Fix or clear it in Settings > C++.\n");
        return;   // abort, never silent fall-through
    }
    if (dr.State == OverrideState.Usable) debuggerOverridePath = dr.ResolvedPath;
}

// Installed-check: an override counts as installed.
if (debuggerOverridePath is null && descriptor.ResolveLaunchCommand() is null)
{
    ReportDebugAdapterMissing(descriptor);
    return;
}
```

Then in the `new DebugConfiguration { ... AdapterId = descriptor.Id }` initializer add:

```csharp
AdapterExecutableOverride = debuggerOverridePath,
```

- [ ] **Step 4: Run to verify pass.**
Run: `dotnet test ... --filter "FullyQualifiedName~MwvmDebuggerOverrideSourceGuardTests"` → PASS. Then `dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release` (DI + compile).

- [ ] **Step 5: Commit.** `feat(cpp): F5 resolves the per-backend debugger override at the MWVM site (invalid→abort)`

---

## Task 11: Settings dialog — `FilePath` control kind + six items + Browse + validation

**Files:**
- Modify: `VisualGameStudio.Shell/ViewModels/Dialogs/SettingsViewModel.cs` (enum `:137`; `SearchableSettingItem` `:19-88`; `MakeText`→add `MakeFilePath` `:1050`; six observable props + `LoadFromService` `:1607`; `GetSettingsKeyForProperty` `:1264`; `BuildSearchableSettings` C++ block `:1021`)
- Modify: `VisualGameStudio.Shell/Views/Dialogs/SettingsDialog.axaml` (BOTH `<Panel>` copies: `:167-178` and `:280-291`)
- Test: `VisualGameStudio.Tests/SettingsDialogToolchainFieldsGuardTests.cs` (source-guard — SettingsViewModel dialog wiring)

**⚠ AXAML changed → `dotnet clean` before building this task.**

- [ ] **Step 1: Write the failing test.** Source-guard + inventory: the six keys appear in `BuildSearchableSettings` (via `new SettingsViewModel(service).DialogSettingKeys`) and each maps back through `GetSettingsKeyForProperty`. This also makes the existing `SettingsConsumerContractTests.EveryDialogSettingKey_HasARegisteredConsumer` cover them (consumer already forced in Task 3).

```csharp
[Test]
public void Six_Toolchain_Keys_Are_In_The_Dialog_Inventory()
{
    var vm = new SettingsViewModel(MakeService());   // same construction the contract test uses
    foreach (var id in CppToolchainOverrides.Backends)
        foreach (var key in new[] { CppToolchainOverrides.CompilerKey(id), CppToolchainOverrides.DebuggerKey(id) })
            Assert.That(vm.DialogSettingKeys, Does.Contain(key), key);
}
```

- [ ] **Step 2: Run to verify failure.** → FAIL (keys not in inventory).

- [ ] **Step 3: Implement the VM side.**
  - Enum: add `FilePath` to `SettingControlKind` (`:137`).
  - `SearchableSettingItem`: add `public bool IsFilePath => ControlKind == SettingControlKind.FilePath;`, plus observable validation props `ValidationMessage` / `ValidationSeverity` (an enum or a brush-key string), and a `BrowseCommand` (`IAsyncRelayCommand`) + a `ToolchainSlotKind Slot`/`string BackendId` pair (so the item knows what to validate). On `StringValue` set → run `ToolchainPathValidator.Validate(BackendId, Slot, value)` **existence tier synchronously** to set red/green, then kick a background `Task` for the version smoke that updates `ValidationMessage` (cancel on re-edit — store the latest CTS). The Browse command resolves `IDialogService` via `App.Services` (pattern at `SettingsDialog.axaml.cs:59` / `SettingsViewModel.cs:115`), calls `ShowOpenFileDialogAsync(new FileDialogOptions{ Title=..., Filters={ new("Executable","*.exe"), new("Batch","*.bat"), new("All","*") } })`, and assigns the result to `StringValue`.
  - Add a `MakeFilePath(key, name, desc, category, prop, backendId, slot, defaultValue="")` helper mirroring `MakeText` but `ControlKind = SettingControlKind.FilePath` and carrying `BackendId`/`Slot`.
  - Add six `[ObservableProperty]` string props (`LlvmCompilerPath`, `LlvmDebuggerPath`, `GccCompilerPath`, `GccDebuggerPath`, `MsvcCompilerPath`, `MsvcDebuggerPath`), six `GetSettingsKeyForProperty` cases → `CppToolchainOverrides.CompilerKey/DebuggerKey(id)`, six `LoadFromService` reads (mirror the clangd read at `:1607`), and six `MakeFilePath(...)` lines in the C++ block (`:1021`, after the clangd `MakeText`). Group them Compiler+Debugger per backend, labels "LLVM compiler path", "LLVM debugger path", etc.

- [ ] **Step 4: Implement the AXAML side (both `<Panel>` copies).** Inside each `<Panel>` (`:167-178` and `:280-291`), add a FilePath sub-block gated by `IsFilePath` — a horizontal stack of a `TextBox` (Text `StringValue`), a Browse `Button` (Command `BrowseCommand`), and a validation `TextBlock` (Text `ValidationMessage`, Foreground bound to `ValidationSeverity`). Keep the existing `TextBox IsVisible="{Binding IsTextBox}"` untouched. Example (both copies identical):

```xml
<StackPanel Orientation="Horizontal" Spacing="6" IsVisible="{Binding IsFilePath}">
  <TextBox Text="{Binding StringValue, Mode=TwoWay}" MinWidth="240" FontFamily="Consolas"/>
  <Button Content="Browse..." Command="{Binding BrowseCommand}"/>
  <TextBlock Text="{Binding ValidationMessage}" VerticalAlignment="Center"
             Foreground="{Binding ValidationSeverity}"/>
</StackPanel>
```

- [ ] **Step 5: Build + run to verify pass.**

```powershell
dotnet clean; dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~SettingsDialogToolchainFieldsGuardTests|FullyQualifiedName~SettingsConsumerContractTests"
```
Expected: PASS (inventory + all consumer/schema guards now cover the six keys). Also `dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release` cleanly (AXAML compiles).

- [ ] **Step 6: Commit.** `feat(cpp): Settings dialog FilePath control + six per-backend toolchain fields with Browse + live validation`

---

## Task 12: Integration + definition-of-done + finish

**Files:**
- Test: `VisualGameStudio.Tests/ToolchainOverrideIntegrationTests.cs`

- [ ] **Step 1: Write the integration test.** Fake off-PATH g++ fixture (a temp file that exists as `g++.exe`): configure `cpp.toolchain.gcc.compiler` in a fake settings store → (a) `CppToolchainProbeService.Probe().Gcc == true`; (b) a gcc-pinned `ProjectFile` built through `BuildService` with a faked-empty PATH resolves via the override path. This ties Task 6 + Task 8 together end-to-end.

- [ ] **Step 2: Run it.** `dotnet test ... --filter "FullyQualifiedName~ToolchainOverrideIntegrationTests"` → PASS.

- [ ] **Step 3: Definition-of-done greps.** Confirm:
  - Six keys present with `""` defaults + registered consumers (Task 3 tests + `SettingsConsumerContractTests` green).
  - No silent-fallback for an invalid override: grep `BuildService.cs` and `MainWindowViewModel.cs` for the "set but not found" / abort strings; confirm the Invalid branch returns/fails and never proceeds to auto-detect.
  - CLI unchanged: grep `BasicLang/Program.cs` for `CppProjectBuilder.Build(` — confirm no override params passed.

- [ ] **Step 4: Full suite.**

```powershell
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release 2>&1 | Tee-Object final.txt | Select-Object -Last 8
```
Expected: baseline count + the new tests, all passing (1 known cpp-game-app BL6009 flake / 2 skips as at baseline).

- [ ] **Step 5: Finish the branch** (superpowers:finishing-a-development-branch): review the diff, then ff-merge to master, refresh `IDE/` prebuilt binaries if that is part of the finish ritual, remove the worktree, delete the branch.

- [ ] **Step 6: Commit** any remaining test/DoD files. `test(cpp): end-to-end toolchain-override integration + DoD`

---

## Notes for the executor

- **DRY the resolver lambda.** Tasks 6/7 both build "usable override → FromExplicit, else TryFindById". Add a helper on `CppToolchainOverrides` early (e.g. `public CppToolchain? UsableCompilerToolchain(string id)`) and reuse it in BuildService, IntelliSense, and the probe; the tri-state `ResolveCompiler` stays for the Invalid/None distinction that BuildService pre-validate and the probe need.
- **Two AXAML copies.** The `<Panel>` control block is duplicated (`:167-178`, `:280-291`); the FilePath sub-block must go in both or the field renders in only one of search-results / category views.
- **Version smoke off the UI thread.** In the dialog item, existence validation is synchronous (instant red/green); the `--version` smoke runs on a background `Task` with a ~3 s timeout and is cancelled on re-edit/close — never block the UI thread on it.
- **winlibs stays off PATH** — every "usable off-PATH" test uses a temp file that merely *exists*; never assume a real toolchain.
