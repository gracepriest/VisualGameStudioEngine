# Wizard C++ Options + C++ Color Picker + Build Quiet — Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire the New Project wizard's C++ standard/toolchain pickers end-to-end (with wizard greying and a BL6015 hard error), give C++ files the inline color picker BasicLang has (properly language-gated), and make builds quiet (Output pane + one 3-second self-dismissing toast, nothing else).

**Architecture:** Point changes on existing seams per the approved spec
`docs/superpowers/specs/2026-07-20-wizard-cpp-options-colorpicker-buildquiet-design.md`
(read it FIRST — it is the authority; this plan is the task breakdown). Three
independent workstreams: (A) options thread wizard → `CreateProjectOptions` →
`ProjectTemplateService` `.blproj` emission → both serializers → compiler-side
`ProjectFile` → `CppProjectBuilder` resolve-by-id; (B) color detection/rewrite
extracted into pure `ColorMatchFinder`/`ColorTextRewriter` classes consumed by
the existing renderer/control; (C) point deletions in `MainWindowViewModel`
plus exposing the already-existing toast auto-dismiss mechanism.

**Tech Stack:** C# / .NET 8, Avalonia 11.3, NUnit, AvaloniaEdit, MSVC+winlibs (gate only).

---

## Conventions (READ FIRST — these prevent real mistakes)

- **Shell:** Windows PowerShell 5.1. No `&&`/`||`. **NEVER** round-trip repo
  files through `Get-Content`/`Set-Content` (mojibake) — use Read/Edit/Write
  tools. Multi-line commit messages: write a file under `$env:TEMP` and
  `git commit -F <file>`; end commit messages with
  `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.
- If Glob/Grep tools error ("claude.exe not found"), fall back to Read +
  `Select-String`.
- **Full suite before EVERY commit.** Output exceeds tool truncation — run
  detached and grep the summary:
  ```powershell
  cmd /c "dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release > %TEMP%\suite.log 2>&1"
  Select-String -Path $env:TEMP\suite.log -Pattern 'Duration:.*VisualGameStudio\.Tests\.dll'
  ```
  Baseline @ dd09b91: **`Failed: 1, Passed: 3293, Skipped: 2`** (the 1 is the
  known BL6009 env flake `CppTemplate_CreatesProject_ThatCompilerBuilds("cpp-game-app")`
  — exit code 1 is normal; `Failed: 0` also fine. Any OTHER failure = stop and fix).
  Each task adds tests; expected Passed grows accordingly.
- **After any `.axaml` change: `dotnet clean` before building** (stale cache crashes).
- **Keep `C:\winlibs\mingw64` OFF PATH.** Task 4 invokes its binaries by
  absolute path only. Putting it on PATH flips `CppToolchain.Find()` for every
  native build on this machine.
- No new `InternalsVisibleTo` (the only allowed one is BasicLang→Tests). New
  Editor/ProjectSystem types that tests touch must be `public`.
- Two language-id maps already exist in this codebase — do not create a third
  or merge them. The color gate consumes `VisualGameStudio.Core/Utilities/LanguageFileTypes.cs`.
- Build commands:
  `dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release`
  (IDE) and `dotnet build BasicLang/BasicLang.csproj -c Release` (compiler).

## Worktree

Work happens on branch `worktree-wizard-color-quiet` in
`.claude/worktrees/wizard-color-quiet` (Task 0 creates it). Base: master @
`5fc3024` or later.

## File map

| File | Change |
|---|---|
| `BasicLang/ProjectSystem/ProjectFile.cs` | + `CppToolchain` prop, parse, serialize |
| `BasicLang/ProjectSystem/CppToolchain.cs` | + `ProbeAvailability()`, `TryFindById()` |
| `BasicLang/ProjectSystem/CppProjectBuilder.cs` | resolve-by-id + BL6015 |
| `BasicLang/ProjectSystem/TemplateEngine.cs` | sync-contract comment only |
| `VisualGameStudio.Core/Abstractions/Services/IProjectTemplateService.cs` | + 2 fields on `CreateProjectOptions` |
| `VisualGameStudio.Core/Abstractions/Services/ICppToolchainProbe.cs` | NEW: probe interface + availability record |
| `VisualGameStudio.Core/Models/BasicLangProject.cs` | + `CppToolchain` on `CppProjectSettings` |
| `VisualGameStudio.ProjectSystem/Serialization/ProjectSerializer.cs` | parse + emit `<CppToolchain>` |
| `VisualGameStudio.ProjectSystem/Services/ProjectTemplateService.cs` | consume options; sync comment |
| `VisualGameStudio.ProjectSystem/Services/CppToolchainProbeService.cs` | NEW: `ICppToolchainProbe` impl wrapping BasicLang probe |
| `VisualGameStudio.Shell/Configuration/ServiceConfiguration.cs` | register probe service |
| `VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectWizardViewModel.cs` | threading, c++23, probe/greying |
| `VisualGameStudio.Shell/Views/Dialogs/NewProjectConfigureView.axaml` | standards dropdown, none-installed warning |
| `VisualGameStudio.Shell/Views/Dialogs/NewProjectSelectView.axaml` | backend list greying + hints (window 1, list at `:40-43`) |
| `VisualGameStudio.Editor/TextMarkers/ColorMatchFinder.cs` | NEW: pure detection |
| `VisualGameStudio.Editor/TextMarkers/ColorTextRewriter.cs` | NEW: pure rewrite |
| `VisualGameStudio.Editor/TextMarkers/InlineColorRenderer.cs` | consume finder; Kind in event args; language gate |
| `VisualGameStudio.Editor/Controls/CodeEditorControl.axaml.cs` | forward filePath; `OnColorPicked` → rewriter |
| `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs` | quiet-build kills + toast overload |
| `VisualGameStudio.Shell/Views/MainWindow.axaml.cs` | per-toast duration |
| Tests | `Compiler/CppProjectFileTests.cs`, NEW `Compiler/CppToolchainResolutionTests.cs`, `Serialization/ProjectSerializerCppTests.cs`, template-service tests, `NewProjectWizardViewModelTests.cs`, NEW `Editor/ColorMatchFinderTests.cs`, NEW `Editor/ColorTextRewriterTests.cs`, in-anger MWVM pins |

---

### Task 0: Worktree + baseline

**Files:** none (setup).

- [ ] **Step 1:** From the repo root:
  ```powershell
  git worktree add .claude/worktrees/wizard-color-quiet -b worktree-wizard-color-quiet
  ```
- [ ] **Step 2:** In the worktree, build both targets (commands above). Expected: 0 errors.
- [ ] **Step 3:** Run the full suite (convention block). Record the baseline
  numbers in the task-completion report. Expected: `Passed: 3293, Skipped: 2`,
  `Failed:` 0 or the 1 known BL6009 flake.

### Task 1: Compiler-side `<CppToolchain>` parse + serialize

**Files:**
- Modify: `BasicLang/ProjectSystem/ProjectFile.cs` (prop near `:57`, parse block `:115-144`, `Save` element list near `:288`)
- Test: `VisualGameStudio.Tests/Compiler/CppProjectFileTests.cs`

- [ ] **Step 1: Write failing tests.** Mirror the existing `CppStandard` parse
  test at `CppProjectFileTests.cs:35-64`. The fixture helper is
  `WriteProject(fullXml)` (`:28-33`) + `ProjectFile.Load` — the
  `LoadProjectWith(fragment)` calls below are shorthand; write the full
  project XML through `WriteProject`:
  ```csharp
  [Test]
  public void Load_ReadsCppToolchain_Lowercased()
  {
      // project XML includes <Language>Cpp</Language><CppToolchain>GCC</CppToolchain>
      var pf = LoadProjectWith("<CppToolchain>GCC</CppToolchain>");
      Assert.That(pf.CppToolchain, Is.EqualTo("gcc"));
  }

  [Test]
  public void Load_NoCppToolchainElement_IsNull()
      => Assert.That(LoadProjectWith("").CppToolchain, Is.Null);

  [Test]
  public void Save_EmitsCppToolchain_WhenSet_EvenForMixedProjects()
  {
      // Language=BasicLang + Backend=Cpp (mixed), CppToolchain="msvc"
      // → saved XML contains <CppToolchain>msvc</CppToolchain>
  }

  [Test]
  public void Save_OmitsCppToolchain_WhenNull() { /* no element in output */ }
  ```
  Adapt helper names to what the fixture actually has — read the file first.
  Note the asymmetry to preserve: `<CppStandard>` emission stays
  `IsCppProject`-gated (`:288`) — do NOT change it; `<CppToolchain>` emission
  is gated only on non-null (mixed projects build natively via
  `IsNativeProject` `:50-54`).
- [ ] **Step 2:** Run just the new tests:
  `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~CppProjectFileTests"` — expected: new tests FAIL (no such property).
- [ ] **Step 3: Implement.**
  ```csharp
  public string? CppToolchain { get; set; }   // "llvm" | "gcc" | "msvc"; null = machine probe
  ```
  Parse (inside the PropertyGroup block, beside `CppStandard` at `:131`):
  ```csharp
  CppToolchain = propertyGroup.Element("CppToolchain")?.Value.Trim().ToLowerInvariant();
  if (string.IsNullOrEmpty(CppToolchain)) CppToolchain = null;
  ```
  Save (in the element list near `:288`):
  ```csharp
  string.IsNullOrEmpty(CppToolchain) ? null : new XElement("CppToolchain", CppToolchain),
  ```
- [ ] **Step 4:** Re-run the filter — expected: PASS. Run the full suite (convention). 
- [ ] **Step 5:** Commit: `feat(compiler): ProjectFile parses and serializes <CppToolchain>`

### Task 2: `CppToolchain.ProbeAvailability()` + `TryFindById()`

**Files:**
- Modify: `BasicLang/ProjectSystem/CppToolchain.cs` (`Find()` at `:52-105`, private ctor `:44`)
- Test: Create `VisualGameStudio.Tests/Compiler/CppToolchainResolutionTests.cs`

- [ ] **Step 1: Read `Find()` closely.** It probes clang++ on PATH → g++ on
  PATH → MSVC via vswhere + vcvars64. The clang/gcc "probes" come from
  parameterizing the existing PATH loop (`:54-73`); the MSVC probe is the
  `:75-102` branch. Extract private helpers so `Find()`, `TryFindById()`, and
  `ProbeAvailability()` share them. `ProbeAvailability()` must NOT run
  vcvars64 (seconds-slow) — its MSVC check = vswhere finds an instance AND
  that instance's `vcvars64.bat` exists on disk (cheap file check; keeps
  `ProbeAvailability_AgreesWithFind` truthful, since `Find()` requires the bat).
- [ ] **Step 2: Write failing tests.**
  ```csharp
  [Test]
  public void TryFindById_UnknownId_ReturnsNull()
      => Assert.That(CppToolchain.TryFindById("borland"), Is.Null);

  [Test]
  public void TryFindById_IsCaseInsensitive_ForInstalledToolchain()
  {
      var avail = CppToolchain.ProbeAvailability();
      if (!avail.Msvc) Assert.Ignore("MSVC not installed on this machine");
      Assert.That(CppToolchain.TryFindById("MSVC"), Is.Not.Null);
  }

  [Test]
  public void ProbeAvailability_AgreesWithFind()
  {
      // Find() succeeds iff at least one toolchain is available
      var avail = CppToolchain.ProbeAvailability();
      var found = CppToolchain.Find();
      Assert.That(found is not null, Is.EqualTo(avail.Llvm || avail.Gcc || avail.Msvc));
  }

  [Test]
  public void TryFindById_NotInstalled_ReturnsNull()
  {
      var avail = CppToolchain.ProbeAvailability();
      if (avail.Gcc) Assert.Ignore("g++ is on PATH on this machine");
      Assert.That(CppToolchain.TryFindById("gcc"), Is.Null);
  }
  ```
  (On the dev machine: MSVC installed, winlibs off PATH → all four run.)
- [ ] **Step 3:** Run filter `~CppToolchainResolutionTests` — expected: FAIL (methods missing).
- [ ] **Step 4: Implement.**
  ```csharp
  public readonly record struct CppToolchainAvailability(bool Llvm, bool Gcc, bool Msvc)
  {
      public string DetectedList
      {
          get
          {
              var found = new List<string>(3);
              if (Llvm) found.Add("llvm");
              if (Gcc) found.Add("gcc");
              if (Msvc) found.Add("msvc");
              return found.Count == 0 ? "none" : string.Join(", ", found);
          }
      }
  }

  public static CppToolchainAvailability ProbeAvailability() =>
      new(ClangOnPath() is not null, GccOnPath() is not null, VsWhereFindsMsvc());

  public static CppToolchain? TryFindById(string id) => id?.Trim().ToLowerInvariant() switch
  {
      "llvm" => FindClang(),   // extracted from Find()'s clang++ branch
      "gcc"  => FindGcc(),     // extracted from Find()'s g++ branch
      "msvc" => FindMsvc(),    // extracted from Find()'s vswhere+vcvars branch
      _ => null,
  };
  ```
  `Find()` becomes `FindClang() ?? FindGcc() ?? FindMsvc()` — behavior identical.
- [ ] **Step 5:** Filter PASS; full suite; commit:
  `feat(compiler): CppToolchain.TryFindById + lightweight ProbeAvailability`

### Task 3: Builder resolve-by-id + BL6015

**Files:**
- Modify: `BasicLang/ProjectSystem/CppProjectBuilder.cs` (toolchain resolution happens INSIDE `EmitCore` at `:315-327`, deliberately after the obj/gen write — that ordering is pinned by `CppProjectBuilderCleanTests`; the `Func<CppToolchain>` seam is `:135-136`; diagnostic emission is `Fail(result, code, msg, filePath)` at `:492-497`)
- Test: `VisualGameStudio.Tests/Compiler/CppProjectCliBuildTests.cs` — sit beside `Build_NoCppSources_FailsWithBL6007` (`:104-110`), which drives `CppProjectBuilder.Build(projectFile, "Debug")` directly.

- [ ] **Step 1:** Read `EmitCore`'s step-6 toolchain gate (`:315-327`): BL6005
  fires there when no toolchain is found, guarded by `!forIntelliSense`
  (IntelliSense emission must never hard-fail on toolchain problems — BL6015
  gets the same guard). Read `Fail()` (`:492-497`) for the emission shape.
  **Vacancy grep** (spec §2.2): `Select-String -Path` over all `.cs` for
  `BL601[5-9]` → the only hits must be this batch's own — confirm BL6015 is
  still unclaimed before using it.
- [ ] **Step 2: Write failing tests.** Add two optional seams to `Build`
  mirroring the existing `resolveToolchain` parameter style (`:135-136`) so the
  missing-toolchain message is deterministically testable on ANY machine
  (spec §5: "via injected probe"):
  ```csharp
  [Test]
  public void Build_UnknownToolchainId_FailsWithBL6015()
  {
      // ProjectFile with Language=Cpp, CppToolchain="borland" (machine-independent: never resolves)
      // → result.Success false, diagnostic Code "BL6015", message contains "borland"
  }

  [Test]
  public void Build_MissingToolchain_BL6015_NamesRequestedAndDetected()
  {
      // Deterministic on every machine: inject
      //   resolveById: _ => null,
      //   probeAvailability: () => new CppToolchainAvailability(Llvm: false, Gcc: false, Msvc: true)
      // with CppToolchain="gcc" → message contains "gcc", "msvc", and "Install gcc"
  }

  [Test]
  public void Build_NoToolchainElement_UsesMachineProbe_AsToday()
  {
      // null CppToolchain → existing resolveToolchain()/Find() path; reuse the
      // minimal-compile fixture shape from CppProjectCliBuildTests
  }

  [Test]
  public void Build_MissingToolchain_RealMachine_E2E()
  {
      if (CppToolchain.ProbeAvailability().Gcc) Assert.Ignore("g++ installed; not reachable");
      // real TryFindById path, CppToolchain="gcc" → BL6015 (runs un-ignored on the dev machine)
  }
  ```
- [ ] **Step 3:** Run filter — FAIL (builder ignores the property).
- [ ] **Step 4: Implement** inside `EmitCore`'s step-6 gate (`:315-327`),
  preserving the after-obj/gen ordering and the `!forIntelliSense` guard:
  ```csharp
  // inside EmitCore, where the toolchain is currently resolved (:318):
  CppToolchain? toolchain;
  if (!string.IsNullOrEmpty(project.CppToolchain))
  {
      toolchain = resolveById(project.CppToolchain);          // default: CppToolchain.TryFindById
      if (toolchain is null && !forIntelliSense)
      {
          var detected = probeAvailability().DetectedList;    // default: CppToolchain.ProbeAvailability
          return Fail(result, "BL6015",
              $"C++ toolchain '{project.CppToolchain}' requested by the project is not installed. " +
              $"Detected: {detected}. Install {project.CppToolchain} or change <CppToolchain> in the project file.",
              project.FilePath);
      }
  }
  else
  {
      toolchain = resolveToolchain(); // existing behavior, unchanged (BL6005 path stays as-is)
  }
  ```
  Thread `Func<string, CppToolchain?>? resolveById = null` and
  `Func<CppToolchainAvailability>? probeAvailability = null` through `Build` →
  `EmitCore` exactly like the existing `resolveToolchain` param; defaults are
  the real statics, so production behavior has no seam risk. (Adapt the sketch
  to `Fail`'s real signature and the gate's real control flow — the sketch is
  the shape, `:315-327` is the truth.)
- [ ] **Step 5:** Filter PASS; full suite (watch `CppProjectBuilderCleanTests` —
  the obj/gen-before-resolution ordering pin must stay green); commit:
  `feat(compiler): per-project <CppToolchain> resolution with BL6015 hard error`

### Task 4: ⛔ DECISION CHECKPOINT — engine .lib × mingw link gate

**Files:** none in-repo (scratch experiment; record verdict in this plan file + commit).

- [ ] **Step 1:** Locate the engine import library: grep `VisualGameStudioEngine.lib`
  in `CppProjectBuilder.cs` to find the deploy/search path the builder uses,
  and confirm the `.lib` exists there (if not, build the engine:
  vcxproj x64/Release via vswhere-discovered MSBuild — see CLAUDE.md).
- [ ] **Step 2:** Generate the spec's fixture — a REAL cpp-game project
  (spec §2.4 says "a cpp-game project", not a scratch file): use the CLI
  `new` command (check the exact argument shape at `Program.cs:288`
  `HandleNewCommand` — the template id is `cpp-game`) targeting the scratchpad.
- [ ] **Step 3:** Compile + link the generated project's `.cpp` sources with
  both winlibs toolchains **by absolute path**, against the located `.lib`,
  with the flags the builder would use (`-std=c++20`, the include dir the
  generated project expects):
  ```powershell
  C:\winlibs\mingw64\bin\clang++.exe -std=c++20 <generated .cpp files> "<path-to>\VisualGameStudioEngine.lib" -o gate-clang.exe
  C:\winlibs\mingw64\bin\g++.exe     -std=c++20 <generated .cpp files> "<path-to>\VisualGameStudioEngine.lib" -o gate-gcc.exe
  ```
  **Fallback isolation fixture:** if the full project hits confounds unrelated
  to the import lib (missing headers, etc.), reduce to a minimal `gate.cpp`
  declaring three real exports — `Framework_Initialize(int,int,const char*)`
  (`framework.h:280`), `Framework_Shutdown()` (`:283`),
  `Framework_ClearBackground(r,g,b,a)` (`:298`) — take their addresses in
  `main`, and link that instead; record WHICH fixture produced the verdict.
  (Running the exes is optional — the gate question is the LINK.)
- [ ] **Step 4: Record the verdict** by editing this section:

  **VERDICT: [PASS-BOTH | PASS-CLANG-ONLY | PASS-GCC-ONLY | FAIL-BOTH] — filled at execution time.**

  - PASS for a toolchain → game templates stay selectable on it (Task 8 does nothing extra).
  - FAIL for a toolchain → Task 8 additionally greys that toolchain **for game
    templates only**, with hint "(engine library requires MSVC-compatible linker)".
- [ ] **Step 5:** Commit the plan-file verdict edit:
  `docs(plan): record engine-.lib mingw link gate verdict`

### Task 5: IDE-side `CppSettings.CppToolchain` + serializer round-trip

**Files:**
- Modify: `VisualGameStudio.Core/Models/BasicLangProject.cs` (`CppProjectSettings` `:68-74`)
- Modify: `VisualGameStudio.ProjectSystem/Serialization/ProjectSerializer.cs` (parse `:66-73` area, serialize `:211-217` area)
- Test: `VisualGameStudio.Tests/Serialization/ProjectSerializerCppTests.cs`

- [ ] **Step 1: Write failing tests** mirroring the existing non-default
  `CppStandard` round-trip at `:84-110` and the mixed-project emit at `:146-181`:
  round-trip `CppToolchain="gcc"`; absent element → null → not re-emitted;
  mixed (Language=BasicLang + Backend=Cpp) project round-trips it too
  (language-independent, exactly like the IDE serializer's `CppStandard` handling).
- [ ] **Step 2:** Filter `~ProjectSerializerCppTests` — FAIL.
- [ ] **Step 3: Implement.** `public string? CppToolchain { get; set; }` on
  `CppProjectSettings`; parse + emit beside `CppStandard`, emit only when
  non-null/non-empty, normalize to lowercase on parse.
- [ ] **Step 4:** Filter PASS; full suite; commit:
  `feat(ide): CppSettings.CppToolchain round-trips through ProjectSerializer`

### Task 6: `CreateProjectOptions` fields + template-service emission + sync comments

**Files:**
- Modify: `VisualGameStudio.Core/Abstractions/Services/IProjectTemplateService.cs` (`:524-575`)
- Modify: `VisualGameStudio.ProjectSystem/Services/ProjectTemplateService.cs` (`GenerateProjectFileContent` `:257-340`, hardcode `:284`; sync comment `:1047-1050`)
- Modify: `BasicLang/ProjectSystem/TemplateEngine.cs` (sync comment `:434-438` only — NO behavior change)
- Test: the fixture that already drives `ProjectTemplateService.CreateProjectAsync` — grep `CreateProjectAsync` under `VisualGameStudio.Tests/` (e.g. `TemplateBuildSweepTests.cs:157-187` shows the setup shape) and add tests beside the closest existing template-service fixture.

- [ ] **Step 1: Write failing tests** (drive through `CreateProjectAsync` into a
  temp dir, then read the generated `.blproj` text):
  - cpp template + `CppStandard="c++17"`, `CppToolchain="gcc"` → file contains
    `<CppStandard>c++17</CppStandard>` and `<CppToolchain>gcc</CppToolchain>`.
  - cpp template + both null → `<CppStandard>c++20</CppStandard>` (default) and
    NO `<CppToolchain>` element (the none-installed self-heal case).
  - BasicLang template + both null → neither element (existing behavior).
- [ ] **Step 2:** Run — FAIL (no such option fields).
- [ ] **Step 3: Implement.** Add to `CreateProjectOptions` with doc comments:
  ```csharp
  /// <summary>C++ language standard ("c++14".."c++23"); null → template default (c++20). C++ projects only.</summary>
  public string? CppStandard { get; set; }
  /// <summary>Per-project toolchain id ("llvm"|"gcc"|"msvc"); null → machine probe. Only set when the toolchain was available at creation time.</summary>
  public string? CppToolchain { get; set; }
  ```
  In `GenerateProjectFileContent`, replace the `:284` hardcode (match the
  surrounding StringBuilder style exactly):
  ```csharp
  sb.AppendLine($"    <CppStandard>{options.CppStandard ?? "c++20"}</CppStandard>");
  if (!string.IsNullOrEmpty(options.CppToolchain))
      sb.AppendLine($"    <CppToolchain>{options.CppToolchain}</CppToolchain>");
  ```
  (If neighboring emissions route user strings through `MSBuildText.EscapeValue`,
  do the same — these values come from fixed pickers but consistency wins.)
  Update BOTH sync-contract comments (`ProjectTemplateService.cs:1047-1050`,
  `TemplateEngine.cs:434-438`) to state the new divergence rule: *IDE emits
  user-chosen `<CppStandard>`/`<CppToolchain>`; CLI templates emit defaults only.*
- [ ] **Step 4:** New tests PASS; `TemplateBuildSweepTests` still green (it
  compares generated source files only, never `.blproj`); full suite; commit:
  `feat(ide): CreateProjectOptions carries CppStandard/CppToolchain into .blproj generation`

### Task 7: `ICppToolchainProbe` abstraction + DI

**Files:**
- Create: `VisualGameStudio.Core/Abstractions/Services/ICppToolchainProbe.cs`
- Create: `VisualGameStudio.ProjectSystem/Services/CppToolchainProbeService.cs`
- Modify: `VisualGameStudio.Shell/Configuration/ServiceConfiguration.cs` (register beside `:131`)

- [ ] **Step 1: Implement** (no meaningful unit to TDD beyond the mapping; the
  fake in Task 8's tests is the real consumer):
  ```csharp
  // Core
  public sealed record ToolchainAvailability(bool Llvm, bool Gcc, bool Msvc);
  public interface ICppToolchainProbe
  {
      /// <summary>Cheap existence probe (PATH + vswhere; never vcvars). Safe off the UI thread.</summary>
      ToolchainAvailability Probe();
  }

  // ProjectSystem
  public sealed class CppToolchainProbeService : ICppToolchainProbe
  {
      public ToolchainAvailability Probe()
      {
          var a = BasicLang.Compiler.ProjectSystem.CppToolchain.ProbeAvailability();
          return new ToolchainAvailability(a.Llvm, a.Gcc, a.Msvc);
      }
  }
  ```
  (Use the actual BasicLang namespace seen in `BuildService.cs:1040-1042`.)
  Register: `services.AddSingleton<ICppToolchainProbe, CppToolchainProbeService>();`
- [ ] **Step 2:** Build Shell (0 errors); full suite; commit:
  `feat(ide): ICppToolchainProbe abstraction over the compiler-side availability probe`

### Task 8: Wizard — threading, c++23, greying/auto-select/none-installed

**Files:**
- Modify: `VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectWizardViewModel.cs`
- Modify: `VisualGameStudio.Shell/Views/Dialogs/NewProjectSelectView.axaml` (**the backend list lives here** — window 1, `:40-43`; per-item disable in an Avalonia ComboBox needs an ItemContainerTheme binding to the option's `IsEnabled`)
- Modify: `VisualGameStudio.Shell/Views/Dialogs/NewProjectConfigureView.axaml` (`:84-86` standard dropdown; none-installed warning)
- Test: `VisualGameStudio.Tests/NewProjectWizardViewModelTests.cs` (existing pin `:245-249`; VM construction helper at `:34-38`)

- [ ] **Step 1:** Read the VM top-to-bottom (`enum :19`, `BackendOption :21-27`,
  `_cppStandard :71`, `CppStandards :81-82`, `ShowCppStandardSelector :86`,
  backend options `:124-126`, `CreateProjectAsync :243-285`, display-only note
  `:261-262`) and how existing tests construct it (fixture setup in
  `NewProjectWizardViewModelTests.cs`) — the probe ctor param must slot into
  that construction.
- [ ] **Step 2: Write failing tests** (fake probe: `class FakeProbe : ICppToolchainProbe`
  returning a fixed `ToolchainAvailability`):
  ```csharp
  // Threading
  CreateProject_Cpp_FillsCppStandardAndAvailableToolchain()        // msvc available+selected → options.CppToolchain == "msvc", options.CppStandard == selection
  CreateProject_Cpp_NoneInstalled_OmitsToolchain()                 // probe all-false → options.CppToolchain null; CppStandard still set
  CreateProject_BasicLang_LeavesBothNull()
  // Greying / auto-select
  Probe_MarksUnavailableBackendsDisabled()                          // gcc/llvm false → their IsEnabled false, hint non-empty
  Probe_SelectedUnavailable_AutoSelectsFirstAvailable()
  Probe_NoneInstalled_ShowsWarning_AllowsProceeding()               // NoToolchainInstalled true; CanCreate still true
  // Standards
  CppStandards_IncludesCpp23_ForLlvmAndGcc_NotMsvc()
  SelectingMsvc_WithCpp23Selected_SnapsBackToCpp20()
  CppStandards_NoneInstalled_DoesNotOfferCpp23()     // defensive: a null/none selection state must not admit c++23
  ```
- [ ] **Step 3:** Run filter — FAIL.
- [ ] **Step 4: Implement.**
  - Ctor takes `ICppToolchainProbe`. **Three construction sites to update**:
    MWVM `:1961` (pass the DI service — MWVM itself is DI-only, no test
    fallout), the test helper `NewProjectWizardViewModelTests.cs:34-38`
    (fake probe), and the design-time VM's `base(new DesignTemplateService())`
    at `:291` (needs a design-time fake probe).
  - `BackendOption` (`:23-29`) is a plain `sealed class` with init-only props —
    NOT a record, NOT ObservableObject. Make it
    `sealed partial class BackendOption : ObservableObject` and add
    `[ObservableProperty] private bool _isEnabled = true;` +
    `[ObservableProperty] private string? _availabilityHint;` (keep the
    existing init props as-is).
  - Probe at **wizard OPEN** (spec §1.4 — backends are chosen on window 1, so
    configure-page-entry is too late): `Task.Run(() => _probe.Probe())`, then
    on the UI thread apply enabled/hints ("(not installed)"), auto-select, and
    set `NoToolchainInstalled` + warning text
    ("No C++ toolchain detected — this project won't build until one is installed").
  - `CreateProjectAsync`: when `SelectedLanguage == ProjectLanguage.Cpp` set
    `options.CppStandard = CppStandard;` and
    `options.CppToolchain = SelectedBackendIsAvailable ? SelectedBackend!.ToolchainId : null;`
    Delete the `:261-262` display-only comment AND fix the second stale
    "display-only" mention in the `BackendOption` doc comment at `:22`
    (Task 18's DoD grep requires zero occurrences).
  - Standards: recompute on backend change — base `{c++20, c++17, c++14}`,
    plus `c++23` when the selected toolchain id is not `msvc`; snap-back rule.
  - **Apply the Task 4 verdict:** if a toolchain FAILED the link gate, extend
    the enable computation to also grey it when the selected template is a game
    template, with the gate's hint text.
  - AXAML: bind `IsEnabled` and hint on the backend options; warning `TextBlock`
    bound to `NoToolchainInstalled`.
- [ ] **Step 5:** `dotnet clean` (AXAML changed) → build Shell → filter PASS →
  full suite; commit: `feat(ide): wizard C++ pickers wired — probe greying, c++23 per toolchain, options threading`

### Task 9: `ColorMatchFinder` — extraction + language gate (existing patterns)

**Files:**
- Create: `VisualGameStudio.Editor/TextMarkers/ColorMatchFinder.cs`
- Modify: `VisualGameStudio.Editor/TextMarkers/InlineColorRenderer.cs` (`Draw` `:94-166` consumes the finder; constructor/`SetFile` seam)
- Modify: `VisualGameStudio.Editor/Controls/CodeEditorControl.axaml.cs` (`SetLanguageService` `:407-416` forwards the path)
- Test: Create `VisualGameStudio.Tests/Editor/ColorMatchFinderTests.cs`

- [ ] **Step 1: Write failing tests** (pure, headless — this is the point):
  ```csharp
  // Classification (via LanguageFileTypes — no hand-rolled lists)
  Classify_Bas_Mod_Cls_Class_Bl_AreBasicLang()
  Classify_Cpp_Cc_Cxx_H_Hpp_Hh_Hxx_Inl_AreCpp()
  Classify_Txt_Json_Unknown_AreNone()
  // BasicLang parity with today's renderer
  Bas_WhitelistedCall_RgbTail_Matches()          // ClearBackground(10, 20, 30) → RgbCall, R=10 G=20 B=30, HasAlpha=false
  Bas_WhitelistedCall_RgbaTail_Matches()
  Bas_ComponentOver255_NoMatch()
  Bas_NonWhitelistedName_NoMatch()
  Bas_VbHex_6And8Digits_Match()                  // &H33AAFF, &H8033AAFF → VbHex
  // The gate
  None_Language_NeverMatches()                   // same lines, ColorLanguage.None → empty
  Cpp_UnprefixedWhitelistName_NoMatch()          // DrawRectangle(10,20,100,50) in Cpp → EMPTY (raylib collision killer)
  Cpp_VbHex_NoMatch()
  ```
- [ ] **Step 2:** Run filter `~ColorMatchFinderTests` — FAIL (type missing).
- [ ] **Step 3: Implement.** Port the two regexes and the whitelist verbatim
  from `InlineColorRenderer.cs:39-70` into:
  ```csharp
  public enum ColorLanguage { None, BasicLang, Cpp }
  public enum ColorMatchKind { RgbCall, VbHex, CppHex, BraceInit }

  public sealed record ColorMatch(
      ColorMatchKind Kind,
      int ReplaceStart,      // offset in lineText where the rewrite range begins
      int ReplaceLength,     // length of the rewrite range (see per-kind rules in the spec §3.3)
      byte R, byte G, byte B, byte A,
      bool HasAlphaComponent);

  public static class ColorMatchFinder
  {
      public static ColorLanguage ClassifyFile(string? filePath) { /* LanguageFileTypes sets */ }
      public static IReadOnlyList<ColorMatch> FindMatches(string lineText, ColorLanguage language) { ... }
  }
  ```
  This task implements `RgbCall` (BasicLang: unprefixed whitelist) + `VbHex`
  (BasicLang only). Cpp returns empty for now. Preserve today's replace-range
  semantics exactly: `RgbCall` range = first R digit through the closing paren
  inclusive (`InlineColorRenderer.cs:134`); `VbHex` = the `&H` start through
  the literal end (`:162`).
  Then switch `InlineColorRenderer.Draw` to consume `FindMatches` (renderer
  keeps geometry + the `ScrollOffset` subtraction `:179-188`), add
  `SetFile(string? path)` → stores `ClassifyFile(path)`, and forward the path
  from `CodeEditorControl.SetLanguageService`. The existing `ColorSwatchClicked`
  args and `OnColorPicked` are untouched in this task (only RgbCall/VbHex exist,
  and the old apply path handles both).
- [ ] **Step 4:** Filter PASS; build Shell; full suite (renderer behavior parity —
  watch for any editor test regressions); commit:
  `refactor(editor): extract pure ColorMatchFinder with real language gating`

### Task 10: framework.h audit → whitelist top-up + `Framework_` prefix rules

**Files:**
- Modify: `VisualGameStudio.Editor/TextMarkers/ColorMatchFinder.cs`
- Test: `VisualGameStudio.Tests/Editor/ColorMatchFinderTests.cs`

- [ ] **Step 1: Audit.** Select-String `VisualGameStudioEngine/framework.h` for
  exports whose parameter tails are `unsigned char r, unsigned char g, unsigned char b`
  (± `a`). Produce the list of base names (strip `Framework_`). Cross-check the
  existing whitelist (`InlineColorRenderer.cs:54-70` — now living in the finder);
  note additions. **Also confirm no export takes a packed RGBA-order hex color
  int** (spec §3.2) — record the finding in the commit message. If one IS found:
  exclude its call-argument literals from `CppHex` detection (with a test) and
  surface the finding to the orchestrator before proceeding.
- [ ] **Step 2: Write failing tests:**
  ```csharp
  Bas_FrameworkPrefixedCall_Matches()            // Framework_ClearBackground(10,10,25,255) → RgbCall
  Cpp_FrameworkPrefixedCall_Matches()            // same line, Cpp language → RgbCall
  Cpp_AuditAddedExport_Matches()                 // pick one name the audit added
  Bas_AuditAddedExport_Matches()
  ```
- [ ] **Step 3:** FAIL → **Step 4: Implement:** strip an optional (BasicLang) /
  required (Cpp) `Framework_` prefix before the whitelist `Contains` check;
  extend the whitelist with the audit's names. **Step 5:** PASS; full suite; commit:
  `feat(editor): Framework_-prefixed engine calls light up in .bas and .cpp (whitelist audited)`

### Task 11: `ColorTextRewriter` — extraction (RgbCall + VbHex) + Kind through the event

**Files:**
- Create: `VisualGameStudio.Editor/TextMarkers/ColorTextRewriter.cs`
- Modify: `VisualGameStudio.Editor/TextMarkers/InlineColorRenderer.cs` (`ColorSwatchClicked` args gain `Kind`; click hit-test `:230-262`)
- Modify: `VisualGameStudio.Editor/Controls/CodeEditorControl.axaml.cs` (`OnColorPicked` `:4782-4834` delegates to the rewriter)
- Test: Create `VisualGameStudio.Tests/Editor/ColorTextRewriterTests.cs`

- [ ] **Step 1: Write failing tests** — port the CURRENT apply behaviors as the
  contract (read `:4800-4819` first; the tests pin parity). **Hex contract
  (verified against `:4803-4806`): digit count follows the PICKED alpha, not
  the original literal** — opaque pick → 6 digits, translucent pick → 8 —
  regardless of what the old text had:
  ```csharp
  RgbCall_ThreeComponents_PickedOpaque_StaysThree()   // "10, 20, 30)" + (1,2,3,255) → "1, 2, 3)"
  RgbCall_ThreeComponents_PickedAlpha_BecomesFour()   // + (1,2,3,128) → "1, 2, 3, 128)"
  RgbCall_FourComponents_StaysFour()
  VbHex_OpaquePick_EmitsSixDigits()                   // "&H8033AAFF" + (1,2,3,255) → "&H010203"
  VbHex_TranslucentPick_EmitsEightDigits()            // "&H33AAFF" + (1,2,3,128) → "&H80010203"
  ```
  API: `public static string Rewrite(ColorMatchKind kind, string oldText, byte r, byte g, byte b, byte a)`.
- [ ] **Step 2:** FAIL → **Step 3: Implement** by moving the logic out of
  `OnColorPicked` verbatim (alpha comma-count heuristic, closing-paren
  re-emission, alpha-driven `&H` digit count). Thread `ColorMatchKind` through
  **two** event-args hops: `ColorSwatchClickedEventArgs` → a `ColorPickerPopup`
  property (the popup already carries the offsets this way — pattern at
  `ColorPickerPopup.cs:41`) → `ColorPickedEventArgs` (`:376-383`), since
  `OnColorPicked` receives the latter. Then `OnColorPicked` becomes: validate
  offsets → `var newText = ColorTextRewriter.Rewrite(e.Kind, oldText, r, g, b, a);`
  → `document.Replace` → invalidate layer (keep `:4821-4824` shape).
- [ ] **Step 4:** PASS; build Shell; full suite; commit:
  `refactor(editor): pure ColorTextRewriter; apply path dispatches on match kind`

### Task 12: `CppHex` — rewriter branch FIRST, then finder pattern (one task)

**Files:** `ColorTextRewriter.cs`, `ColorMatchFinder.cs`, both test files.

- [ ] **Step 1: Failing rewriter tests:** `CppHex_OpaquePick_EmitsSixDigits()`
  ("0x8033AAFF" + opaque → "0x010203"), `CppHex_TranslucentPick_EmitsEightDigits()`
  ("0x33AAFF" + A=128 → "0x80010203") — AARRGGBB order and the picked-alpha
  digit rule, both identical to the `&H` contract pinned in Task 11.
- [ ] **Step 2:** Implement the rewriter branch; PASS.
- [ ] **Step 3: Failing finder tests:** `Cpp_CppHex_6And8Digits_Match()`,
  `Bas_CppHex_NoMatch()`, `Cpp_CppHex_ShortOrLongHex_NoMatch()` (`0xFFF`,
  `0xFFFFFFFFF` → no match; pattern is exactly 6 or 8 hex digits with a
  word-boundary, mirroring the `&H` pattern at `InlineColorRenderer.cs:46-48`).
- [ ] **Step 4:** Implement the pattern (Cpp set only); PASS. (Parity note —
  a fair inference from the spec's `&H`-parity intent, not its explicit text:
  like `&H` in .bas today, ANY 6/8-digit `0x` literal gets a swatch. The
  end-of-batch smoke evaluates the noise level; if masks like `0xFFFFFF` prove
  too noisy, that's a follow-up chip, not a mid-task redesign.)
- [ ] **Step 5:** Full suite; commit: `feat(editor): 0x hex color literals in C++ — rewrite before detect`

### Task 13: `BraceInit` — rewriter branch FIRST, then finder pattern (one task)

**Files:** same four as Task 12.

- [ ] **Step 1: Failing rewriter tests:**
  `BraceInit_ThreeComponents_PickedOpaque_StaysThree()` ("{255, 0, 0}" → "{1, 2, 3}"),
  `BraceInit_ThreeComponents_PickedAlpha_BecomesFour()`,
  `BraceInit_FourComponents_StaysFour()`.
- [ ] **Step 2:** Implement; PASS.
- [ ] **Step 3: Failing finder tests:** the three prefixes
  (`Color{255,0,0,255}`, `(Color){255,0,0,255}`, `CLITERAL(Color){ 255, 0, 0, 255 }`)
  each → `BraceInit` with **ReplaceStart at the opening brace** (prefix
  survives, spec §3.3); `Cpp_BareBraces_NoMatch()`; `Bas_BraceInit_NoMatch()`;
  components ≤255 enforced.
- [ ] **Step 4:** Implement (Cpp set only); suggested pattern:
  ```csharp
  @"(?:CLITERAL\s*\(\s*Color\s*\)\s*|\(\s*Color\s*\)\s*|\bColor\s*)(\{\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})(?:\s*,\s*(\d{1,3}))?\s*\})"
  ```
  (group 1 = the brace span = the replace range). PASS.
- [ ] **Step 5:** Full suite; commit: `feat(editor): Color{} brace-literal swatches in C++`

### Task 14: Renderer/control wiring sweep + editor verification

**Files:** `InlineColorRenderer.cs`, `CodeEditorControl.axaml.cs` (verification pass).

- [ ] **Step 1:** Verify every `ColorMatchKind` flows end-to-end: finder →
  swatch drawn (geometry: swatch after the match, viewport-space, ScrollOffset
  subtracted) → click carries Kind + document-absolute replace offsets →
  popup → rewriter → `document.Replace`. Fix anything the earlier tasks left
  dangling (e.g. hit-region math for `BraceInit`/`CppHex` spans). Confirm no
  dead code remains from the pre-extraction era (`Draw`'s inline regexes,
  `OnColorPicked`'s inline branches).
- [ ] **Step 2:** Build Shell; full suite; commit:
  `chore(editor): color-picker wiring sweep — all kinds end-to-end`

### Task 15: Toast plumbing — auto-dismiss override + per-toast duration

**Files:**
- Modify: `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs` (`ShowNotification` overloads `:1802-1832`, internal computation `:1821`, `NotificationEventArgs` `:8483-8499`)
- Modify: `VisualGameStudio.Shell/Views/MainWindow.axaml.cs` (dismiss decision `:431`, 5 s timer `:434`)
- Test: pure/static tests only — **`MainWindowViewModel` is DI-only and is
  never constructed anywhere in the suite** (the suite documents this:
  `SettingsConsumerContractTests.cs:70` "too-heavy-to-construct",
  `SettingsIntelliSenseWiringTests.cs:21` "needs ~40 services"). Do NOT try.

- [ ] **Step 1: Failing tests** (both machine-independent):
  (a) direct `NotificationEventArgs` construction tests (it's a public class,
  `MainWindowViewModel.cs:8483`): new `double? DismissAfterSeconds` property
  (null → the 5 s default);
  (b) the `:1821` auto-dismiss computation moves into a **public static** pure
  helper on MWVM, following the existing `ShouldShowBuildOutput(ISettingsService)`
  static-seam precedent (`:2956`, pinned by `SettingsBuildBasicLangWiringTests:75-86`):
  ```csharp
  public static bool ComputeToastAutoDismiss(string severity, int actionCount, bool? overrideFlag)
  // overrideFlag true/false wins outright; null → severity == "info" && actionCount == 0
  ```
  (No `InternalsVisibleTo` exists for Shell → Tests and none may be added —
  the helper must be public.)
- [ ] **Step 2:** FAIL → **Step 3: Implement.** MWVM: the actions-overload of
  `ShowNotification` gains `bool? autoDismiss = null, double? dismissAfterSeconds = null`,
  computes via `ComputeToastAutoDismiss`, and threads both into the args
  (default behavior for every existing caller is bit-for-bit unchanged).
  MainWindow: timer interval at `:434` becomes
  `TimeSpan.FromSeconds(e.DismissAfterSeconds ?? 5)`.
- [ ] **Step 4:** PASS; full suite; commit:
  `feat(shell): toast auto-dismiss override + per-toast duration (defaults unchanged)`

### Task 16: Quiet builds — the kill list

**Files:**
- Modify: `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs`:
  `:1616` (delete), `:1597-1601` + `:1608-1613` (quiet toasts), `:3555-3557`
  (delete), `:3564-3566` (demote), `:2953-2960` (+ helper), `:7432/:7455/:7482/:3660` (reroute)
- Test: pure static seam + source-guard tests. **The in-anger harness does NOT
  construct MWVM** (it drives services directly), and nothing in the repo ever
  `new`s a `MainWindowViewModel` — dock activation and `NotificationRequested`
  cannot be observed from a test. Use the two existing precedents instead.

- [ ] **Step 1: Write failing pins** using the repo's established patterns:
  (a) a **public static** toast-composition seam (same precedent as Task 15's
  helper) that `OnBuildCompleted` will consume:
  ```csharp
  public sealed record BuildToastSpec(string Message, string Severity, bool AutoDismiss, double DismissAfterSeconds);
  public static BuildToastSpec ComposeBuildToast(bool succeeded, int errorCount, double elapsedSeconds);
  ```
  ```csharp
  ComposeBuildToast_Success_IsQuiet()      // AutoDismiss true, DismissAfterSeconds == 3, severity info
  ComposeBuildToast_Failure_IsQuiet()      // AutoDismiss true, DismissAfterSeconds == 3, names the error count
  ```
  (b) a **source-guard test** following the `NewProjectWizardSwapGuardTests.cs:26`
  precedent (reads `MainWindowViewModel.cs` as text and asserts patterns):
  ```csharp
  MainWindowSource_HasNoErrorListActivation()   // zero occurrences of ActivateTool("ErrorList"); SetBuildDiagnostics still present (population survives)
  MainWindowSource_BuildTern_HasNoBuildFailedMessageBox()  // the :3555 modal pattern is gone
  ```
- [ ] **Step 2:** FAIL → **Step 3: Implement.**
  - Delete `:1616` (`ActivateTool("ErrorList")`); leave `:1621-1622` population intact.
  - Success/failure toasts: drop all action buttons; `OnBuildCompleted` builds
    both from `ComposeBuildToast(...)` (which encodes
    `public const double QuietBuildToastSeconds = 3;`) and passes
    `autoDismiss: spec.AutoDismiss, dismissAfterSeconds: spec.DismissAfterSeconds`
    through the Task 15 overload. Keep the status-bar messages (`:1588/:1602`).
  - Delete the F5 modal `:3555-3557` entirely (the failure toast + Output
    already reported it; nothing consumes the dialog result).
  - `:3564-3566` → replace the modal with the Output-line pattern used by
    Ctrl-F5 at `:3747` (same wording).
  - Extract from `ShowBuildOutput()` a helper that ONLY does the gated reveal:
    ```csharp
    private void RevealOutputIfEnabled()
    {
        // ShouldShowBuildOutput is a STATIC method taking ISettingsService (:2956)
        if (ShouldShowBuildOutput(_settingsService)) _dockFactory.ActivateTool("Output");
    }
    ```
    (Adapt the call to the exact shape `:2953-2960` already uses.)
    `ShowBuildOutput()` = select Build channel + `RevealOutputIfEnabled()`.
    Reroute `:7432/:7455/:7482` and `:3660` through `RevealOutputIfEnabled()`
    **without** touching their own channel selection — deliberate deviation
    from the spec's letter ("route through ShowBuildOutput()"): forcing the
    Build channel would stomp the task-runner/test output channels; the spec's
    intent is the `build.showOutput` gating, which this preserves.
- [ ] **Step 4:** Pins PASS; full suite (watch `DockReopenResilienceTests` and
  `SettingsBuildBasicLangWiringTests` — both must stay green); commit:
  `feat(shell): quiet builds — no ErrorList seizure, no modals, 3s self-dismissing toasts`

### Task 17: Amplifier — Build Solution command uses the combined path

**Files:**
- Modify: `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs` (`BuildSolutionAsync` command `:6749-6792`)

- [ ] **Step 1:** Read both paths side by side: the command's per-project loop
  (`:6773-6790`, one `BuildCompleted` per project via `BuildService.cs:384`)
  vs the Build command's combined call (`:3022-3025` →
  `_buildService.BuildSolutionAsync` → single event at `BuildService.cs:174`).
  Confirm what the loop adds (per-project progress text?) so nothing user-visible
  is lost — the combined service path already aggregates per-project output.
- [ ] **Step 2: Implement:** replace the loop with the combined call, keeping
  the guard modal (`:6753-6755`) and the progress-notification pair
  (`:6767/:6792`) around it. Result: one `BuildCompleted`, one toast, at most
  one Output reveal per gesture.
- [ ] **Step 3:** Build Shell; full suite; commit:
  `fix(shell): Build Solution raises one BuildCompleted — kills the N-toast amplifier`

### Task 18: DoD greps + full suite + CLI chip

**Files:** none (verification).

- [ ] **Step 1: Definition-of-done greps** (all via Select-String; every one must hold):
  - `ActivateTool("ErrorList")` in `MainWindowViewModel.cs` → **0 hits** on any
    build/debug path (View-menu/palette generic activations elsewhere are fine).
  - Literal `<CppStandard>c++20</CppStandard>` in `ProjectTemplateService.cs` →
    **0 hits** (the `?? "c++20"` fallback expression is the only c++20 left).
  - `display-only` in `NewProjectWizardViewModel.cs` → 0 hits.
  - `BL6015` appears in `CppProjectBuilder.cs` and tests.
  - No hand-rolled extension list in `ColorMatchFinder.cs` (it references
    `LanguageFileTypes`; grep `".cpp"` in the file → 0 hits).
  - `Framework_` prefix handling present in `ColorMatchFinder.cs`.
  - The two sync-contract comments mention `<CppToolchain>`.
- [ ] **Step 2:** Full suite, twice if any anomaly (isolation-rerun protocol on
  any non-BL6009 failure). Record final counts.
- [ ] **Step 3 (ORCHESTRATOR, not subagent):** file the background-task chip for
  `BasicLang.exe new --cpp-standard <value>` (CLI flag threading through
  `Program.cs` `HandleNewCommand :288` + `TemplateEngine.CreateProject :625`
  + `{{CppStandard}}` variable at `:650-657` replacing the 3 hardcodes).
- [ ] **Step 4:** Commit any stray fixes:
  `chore: DoD verification for wizard/color/quiet batch`

### Task 19: User smoke + finish branch

- [ ] **Step 1:** Build the worktree IDE and hand it to the user with this checklist:
  1. **Wizard:** New Project → C++ → llvm/gcc show "(not installed)" and are
     disabled (this machine: MSVC only); pick c++17 → created `.blproj` has
     `<CppStandard>c++17</CppStandard>` + `<CppToolchain>msvc</CppToolchain>`;
     project builds. c++23 hidden while MSVC selected.
  2. **BL6015:** hand-edit that `.blproj` to `<CppToolchain>gcc</CppToolchain>`
     → build fails with the BL6015 message naming gcc + detected list.
  3. **Colors:** `.bas` game sample → swatches incl. `Framework_`-prefixed
     calls; `.cpp` in a game project → swatch on `Framework_ClearBackground(10,10,25,255)`,
     on `0x33AAFF`, on `Color{255,0,0,255}`; **no** swatch on
     `DrawRectangle(10,20,100,50)`; click → picker → Apply rewrites correctly; undo works.
  4. **Quiet builds:** successful build → Output reveal + one toast that
     disappears in ~3 s; failing build → NO Error List popup (badge updates),
     toast self-dismisses; F5 on a failing build → no modal; Build Solution on
     a 2-project solution → one toast total.
- [ ] **Step 2:** On user pass: superpowers:finishing-a-development-branch —
  verify suite, ff-merge to master, push, refresh `IDE/` prebuilt binaries
  (same procedure as the Phase 4 refresh commit `dd09b91`), delete
  worktree + branch.
