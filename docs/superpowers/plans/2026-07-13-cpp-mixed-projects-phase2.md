# C++ Projects Phase 2 — Mixed BasicLang + C++ Projects Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A user can freely mix `.bas`/`.mod`/`.cls` and `.cpp`/`.h` files in one native-target project — C++ calls BasicLang through generated headers in `obj/gen/`, BasicLang calls C++ through the existing `#CppInclude`/`::` passthrough, exactly one entry point is enforced across both languages, and both the CLI and the IDE build mixed projects through one shared pipeline — per Phase 2 of `docs/superpowers/specs/2026-07-11-cpp-language-support-design.md`.

**Architecture:** `CppCodeGenerator` gains split emission (`GenerateSplit` in a new partial-class file): one shared runtime header + one aggregate declarations header + per-module shim headers + per-module `.g.cpp` definition files + an optional entry-point TU, all landing in `obj/gen/`. `CppProjectBuilder` becomes the **single native orchestrator** for ALL native projects (pure C++, pure BasicLang-on-Cpp-backend, and mixed): it partitions sources by extension, transpiles the BasicLang half via `CompileProjectFiles`, enforces the cross-language entry-point rule, and compiles user + generated TUs together through the multi-TU `CppToolchain.Compile`. The legacy single-TU transpile fork (`CppToolchain.CompileToExecutable`, IDE `CompileGeneratedCpp`, CLI `HandleBuildCommand` cpp branch) is **deleted** — this discharges the Phase 1 convergence precondition. The BasicLang LSP server gets an extension filter so mixed projects don't pollute its symbol tables.

**Tech Stack:** C# / .NET 8, NUnit 4 (constraint model), external clang++/g++/MSVC toolchains, C++20 (inline variables, `#pragma once`).

---

## Critical recon facts (verified against master @ c6adf59 by a 6-scout fleet, 2026-07-13 — do not re-derive)

**Codegen (split-emission ground truth):**
- `CppCodeGenerator.Generate(IRModule)` emits ONE string in this order: capability gate → `GenerateHeader` (std includes → `#CppInclude` tokens → `using namespace std;` → `EmitDotNetSurfaceHelpers` → `EmitRuntimePreamble`) → class fwd decls → enums → delegates → interfaces → classes → static member inits → globals → extern "C" decls → standalone fn declarations → fn definitions → `int main` (CppCodeGenerator.cs:61–220).
- ALL runtime support is **inline in the TU**: `BasicLangRt` helpers (~30 lines, always), `BasicLang::Task<T>`/`Generator<T>` (conditional), collections spliced from the C# const `CppCollectionsRuntime.Source` (BasicLang/Compiler/CodeGen/CPlusPlus/CppCollectionsRuntime.cs:34–114 — self-contained, header-only, already compiled standalone by `CppCollectionsRuntimeTests`).
- Classes emit with **fully inline method bodies** (implicitly `inline`, header-safe); standalone functions are already decl/def separated; generics emit real `template <typename T>` prefixes (must live in headers). Static member inits are the only out-of-class definitions today (CppCodeGenerator.cs:826–841, 862–995, 1553–1620).
- Generated bodies use **unqualified std names** (`cout << … << endl` :1812, 2179–2183; `to_string` :2211) — generated headers CANNOT drop `using namespace std;` without a full qualification audit (backlog, NOT Phase 2).
- **No per-module grouping survives to the generator**: `CombineIRModules` (Compiler.cs:593–664) merges everything into one `IRModule("Combined")`, silently dropping duplicate same-named functions (first wins — so duplicate cross-file `Main` never reaches the backend). Per-unit IRs DO survive at `CompilationResult.Units[i].IR` (Compiler.cs:534–541, 290). `IRFunction` carries `ModuleName`/`SourceFilePath` (IRNodes.cs:1072, 1077); `IRClass`/`IRInterface`/`IREnum`/`IRDelegate` carry NO source attribution — type→module mapping must come from per-unit IRs.
- The IR optimizer runs **only on the CombinedIR** (Compiler.cs:268–287). Split emission MUST generate from the optimized CombinedIR (using per-unit IRs only for attribution/counting) or it silently bypasses the optimizer — the exact hidden-bug class the July probe fleet found.
- `#CppInclude` tokens are accumulated **globally** in the Preprocessor with zero per-file attribution and land only on `CombinedIR.CppIncludes` (Preprocessor.cs:23–29; Compiler.cs:148, 273–277).
- Framework externs are inserted by a **post-generation string hack**: search output for first `using namespace std;`, insert used-only `extern "C"` block (CppCodeGenerator.cs:202–217). `_usesFramework` is set during body generation (:2335–2340). `GenerateFrameworkExternDeclarations` holds the full name→declaration catalog (:2349+).
- `int main` is ALWAYS emitted today (`GenerateMainFunction` default true; both native-pipeline call sites — Program.cs:545, BuildService.cs:568 — use default options; zero BasicLang `Main` → runtime stub "No Main function found", exit 0) (CppCodeGenerator.cs:196–200, 1793–1823, 3447). An implementer's grep will also find BackendRegistry.cs:36/42, MultiTargetCompiler.cs:54, and Program.cs demo paths constructing the generator — none passes `GenerateMainFunction=false` on any path Tasks 5/7 touch; leave them alone.
- Class method bodies exist BOTH in `module.Functions` and via `IRClass.Methods[].Implementation`; `IsClassMethod` (:222–234) does identity-based exclusion — any partition of `module.Functions` must reuse it or methods emit twice.
- `IsCppProject` routing key exists (ProjectFile.cs:45–46). Dozens of `Does.Contain` tests pin the single-string `Generate()` output (CppBackendTests, CppCollectionTests, CppPassthroughTests) — that API must remain untouched.

**The two native pipelines (convergence ground truth):**
- **Path A (transpile), CLI**: `HandleBuildCommand` → restore → `CompileProjectFiles` → one `Generate()` string → `bin/<config>/<TFM>/<AssemblyName|ProjectName|Program>.cpp` → `CppToolchain.CompileToExecutable` (single TU, hardcoded `-std=c++20 /EHsc`, **zero `-I`/`-D`/opt flags**, sync `ReadToEnd` — the deadlock-prone legacy body per RunProcess's own comment, CppToolchain.cs:310–365, 347–350). No toolchain → **warn + "success" source-only**. Engine auto-link via `EngineDeployment.UsesEngineCpp` scanning generated code for the line-anchored `#define FRAMEWORK_API` marker (Program.cs:770–835). Raw unparsed diagnostics; no compile_commands.json.
- **Path A, IDE**: `CompileWithBasicLangApiAsync` → generated `.cpp` at `projectDir/<config.OutputPath>` (default `bin\Debug`, **no TFM** — differs from CLI!) → `CompileGeneratedCpp` → same `CompileToExecutable`; BL6002 no-toolchain soft warn, BL6003 engine lib missing, BL6004 raw-blob compile failure (BuildService.cs:512, 617–635, 1089–1160). Exe named `project.Name` (CLI uses AssemblyName-first).
- **Path B (Language=Cpp)**: `CppProjectBuilder.Build` → `bin/<config>` (no TFM) → multi-TU `CppToolchain.Compile(CppCompileRequest)` with IncludeDirs (projectDir first)/Defines/std/opt/debug, parsed diagnostics, `obj/compile_commands.json`, BL6005 hard fail on no toolchain (CppProjectBuilder.cs:27–210).
- `CppToolchain.Compile` **rejects duplicate TU basenames** OrdinalIgnoreCase (objects collide flat in workdir, CppToolchain.cs:184–193, 205–207) — generated file names must not collide with user basenames (`main.cpp` vs a generated `main.cpp`).
- `CppCompileRequest.IncludeDirs` feeds BOTH the real compile and compile_commands.json through the single `FlagsFor` (CppToolchain.cs:119–141, 161–178) — one `IncludeDirs.Add(objGen)` wires both.
- `GetCppTranslationUnits`' default glob **excludes bin/ and obj/** (ProjectFile.cs:398–428) — generated TUs in `obj/gen` are invisible to discovery and must be appended to `request.SourceFiles` explicitly.
- `CompileProjectFiles` is the convergence pivot: preprocess→parse→semantic→combine→optimize, **no codegen** — codegen lives in its callers. `CompilationResult { Success, Units, AllErrors, CombinedIR }` (Compiler.cs:199–303).
- On path A, quoted `#CppInclude "user.h"` next to the .blproj does NOT resolve today (generated TU lives in bin/…, no `-I` exists) — the green passthrough e2es only pass because the test helper writes headers into the temp dir beside the generated file (CppCompile.cs:105–174). Phase 2's include wiring **fixes a real Phase 1 gap**.
- Linker errors for 0/2+ mains do **not** parse into clickable diagnostics (MSVC `LIB(obj) : error LNK2019` form is rejected by the paren-excluding file class; GNU `undefined reference` matches nothing) → BL6006 raw blob. The entry-point rule must be enforced **pre-link**.

**Guards / gathering:**
- BL6008 raised in exactly one place (CppProjectBuilder.cs:40–52), pinned by `Build_BasicLangSourcesPresent_FailsWithBL6008` (CppProjectCliBuildTests.cs:142–150). Both are removed/replaced by this plan.
- `GetSourceFiles`: default-glob mode = BasicLang extensions only (`.bas .bl .basic .mod .cls .class`), recursive, **no bin/obj exclusion**; explicit-items mode = every `<Compile>` item **regardless of extension** (ProjectFile.cs:354–389). The two gatherers partition cleanly ONLY in glob mode; in explicit mode a `.cpp` item appears in both lists and `.h` in `GetSourceFiles` only.
- A `.cpp` Compile item in a managed-backend project is lexed as BasicLang today → loud parse errors (Compiler.cs:199–241 has no extension filter). The single shared choke point with backend knowledge for the spec-§9 guard is `BasicCompiler.CompileProjectFiles` (both callers set `CompilerOptions.TargetBackend`: Program.cs:488–493, BuildService.cs:511–519).
- Taken BL6xxx: 6001–6010 (guards-scout grep). **BL6011+ free.** Also in the paths: BL0001/BL0002/BL0020/BL4001/BL9999, CPP1001/CPP1002.
- No duplicate-Main or no-Main diagnostic exists anywhere; C# backend synthesizes a Main when absent (CSharpBackend.cs:387–406) — managed behavior is untouched by this plan.

**IDE:**
- `BuildInternalAsync` routes ONLY on `project.Language == ProjectLanguage.Cpp` (BuildService.cs:333–358); `BuildCppProject` reloads the .blproj via the CLI `ProjectFile.Load` and delegates to `CppProjectBuilder.Build`, mapping `CppDiagnostic`→`DiagnosticItem` Source="cpp" (:1026–1080). It is deliberately no-throw (BL6010) so the shared finalization always fires `BuildCompleted` at :380 — **the Error List's only diagnostics feed** (MainWindowViewModel.cs:492, 1373–1374). Early returns before :380 kill the feed.
- F5 guard checks ONLY `Language==Cpp` (MainWindowViewModel.cs:3224–3234) — a native BasicLang project passes the guard and hands a native exe to the BasicLang debug adapter.
- Ctrl+F5 / external console do no probing — they trust `buildResult.ExecutablePath` (MainWindowViewModel.cs:3365–3416).
- LSP document sync + hover/completion/signature/GoToDef/GoToImpl/outline/codelens/semantic-tokens/folding are gated on `BasicLangFileTypes.IsBasicLangSourceFile`. **STILL UNGATED** (send .cpp URIs to the BasicLang server): Rename (:4874–4884), FormatDocumentContentAsync incl. format-on-save (:7595–7599), OnTypeFormatting (:7629–7633), GoToTypeDefinition (:4403–4408), FindReferences (:4627–4632), code actions (:7664–7693), doc highlights (:2101–2106), doc symbols (:4200–4204), selection ranges (:7383–7393), debug data-tip LSP hover fallback (:1665–1673).
- `ProjectSerializer.SaveAsync` emits `<Language>`/`<CppStandard>` ONLY when `Language==Cpp` (:209–214) — a mixed Language=BasicLang project **loses CppStandard on save**. IncludeDir/NativeLib/Define items already round-trip regardless of Language (:271–282). (Pre-existing unrelated strips: PackageReference/TargetFramework/UseWindowsForms/UseWPF — NOT Phase 2 scope.)
- `AddExistingFileAsync` hardcodes `ProjectItemType.Compile` for every pick including All-Files (SolutionExplorerViewModel.cs:1219–1223), bypassing `GetItemTypeForExtension` (:1065–1075).
- New-file default-extension sites: SolutionExplorerViewModel.cs:1000–1003 and :1090–1093, both `Language == ProjectLanguage.Cpp ? ".cpp" : ".bas"`.
- IDE grep for `ProjectLanguage.Cpp`: only BuildService:333, ProjectSerializer:65/209/212, the two default-ext sites, F5 guard :3226.

**BasicLang LSP server (compiler-side):**
- Default server = OmniSharp `BasicLangLanguageServer` (`--lsp`); no workspace scan — discovery is per-didOpen via `LspProjectContextProvider` (walk up ≤6 dirs for a .blproj containing the file, else sibling scan of `*.bas/*.bl/*.mod/*.cls` only).
- When a .blproj IS found, the LSP takes `ProjectFile.GetSourceFiles()` **unfiltered** — explicit `.cpp`/`.h` Compile items are content-read (≤1MB) and lexed+parsed as BasicLang (LspProjectContext.cs:194–233, 366–397). Error-recovering parser → small C++ files register REAL (mostly empty) modules under their basenames; a `.cpp` sharing a basename with a `.bas` gets junk merged into the real module via `MergeFrom` (:499–503).
- Pollution is **silent** (LSP publishes only analyzer errors, never recovered parse errors) — verify via symbol-table contents, not visible diagnostics. Content stamp hashes .cpp content → every .cpp edit re-triggers project symbol collection (waste, not wrongness).
- `TextDocumentSyncHandler.GetTextDocumentAttributes` answers EVERY URI as "basiclang" (:32–35) — the server-side root of "answers unknown URIs as BasicLang". No first-party client sends .cpp today (IDE gates by extension; clangd config exists in LspClientManager.cs:167–180).
- `#CppInclude` and `::` produce ZERO LSP noise today (directive lexes to Unknown → recovered silently; `::` resolves to TypeKind.Foreign).
- BasicLang.csproj does NOT reference VisualGameStudio.Core — the compiler-side filter cannot reuse the IDE's `BasicLangFileTypes`; single source of truth must live on `ProjectFile`.
- Do NOT filter inside `ProjectFile.GetSourceFiles()` itself — `CppProjectBuilder`'s partition and `GetCppTranslationUnits`' explicit-items branch depend on it returning ALL Compile items.
- Sibling-scan pattern list (`.bas/.bl/.mod/.cls`, LspProjectContext.cs:340) disagrees with the default globs (adds `.basic/.class`) — reconcile via the shared list.

**Tests / conventions:**
- Fixtures to extend: `CppProjectCliBuildTests` (in-process builder + spawned-CLI e2e; `RunCli` helper, 5-min timeout, kill tree), `TemplateBuildSweepTests` (IDE templates built by spawned CLI; CLI/IDE template byte-identity :160–187), `BuildServicePipelineTests` (in-process IDE BuildService vs real templates), `CppCollectionTests` (`CompileToCppOptimized` — optimizer-running helper, load-bearing), `CppPassthroughTests` + `Native/CppCompile.CompileAndRun(source, compiler, extraFiles)` (real-toolchain run helper; extraFiles land beside the generated TU), `CppCollectionsRuntimeTests` (compiles the runtime const standalone).
- Conventions: NUnit 4 constraint asserts; `[NonParallelizable]` on process-spawning fixtures; temp dirs `Path.GetTempPath()/"<prefix>-"+Guid`; teardown retries 3× / 200 ms; toolchain skip `if (CppToolchain.Find() == null) Assert.Ignore(...)`; one fixture: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~<Fixture>"`.
- The test csproj project-references RaylibWrapper and copies `IDE/VisualGameStudioEngine.lib/.dll` beside the test host; `BasicLang.exe` deploys via project reference. In a fresh worktree, copy `IDE/RaylibWrapper.dll` + engine DLL/.lib into `BasicLang/bin/Release/net8.0/` after the first build or the game-template sweep fails spuriously (Phase 1 lesson).
- Repo rules: Edit/Write tools only (never PS `Get-Content`/`Set-Content` round-trips); `dotnet clean` after AXAML changes; test through BOTH CLI and IDE entry points; commit messages via file + `git commit -F` when multi-line.

## Design decisions (defaults chosen; reviewer/user may override)

- **D1 — One aggregate declarations header + per-module shims, not true per-module headers.** Spec §2 sketches "a `.h`/`.cpp` pair per module". True per-module headers are cycle-prone: classes emit with inline bodies (calling across modules requires complete types), so mutual module references create unresolvable include cycles. Instead: all declarations land in one `<Project>.g.h`; each module gets a shim `<Module>.g.h` that includes it (preserving the spec §3 `#include "Logic.h"` UX); function **definitions** still split per module into `<Module>.g.cpp` via `IRFunction.ModuleName`. Cycle-proof, spec-UX-compatible, and per-module TUs keep the door open for incremental compiles (Phase 5).
- **D2 — Generated headers keep `using namespace std;`.** Inline class bodies emit unqualified `cout`/`to_string`/`string`; qualifying every emission is a generator-wide audit (backlog). Documented wart: user C++ including a generated header gets namespace std imported.
- **D3 — Globals and static member inits become C++17 `inline` definitions in the aggregate header.** Removes the only out-of-class emissions, avoids per-module attribution for data, ODR-safe under C++20.
- **D4 — `.g.h`/`.g.cpp` suffix for all generated files.** `Path.GetFileNameWithoutExtension("Main.g.cpp")` = `"Main.g"` — never collides with a user `main.cpp` under the toolchain's duplicate-basename guard.
- **D5 — Framework externs move into the runtime header (full catalog, always emitted in split mode).** Kills the string-insertion hack on the split path; declarations cost nothing; user C++ in mixed game projects gets `Framework_*` declarations for free. Engine **linking** keys on the generator's `UsesFramework` flag (BasicLang side) or an explicit `<NativeLib>` item (C++ side) — NOT on marker-scanning generated text.
- **D6 — No-toolchain becomes a hard BL6005 failure for ALL native projects.** Path A's "warn + success (source-only)" (BL6002) dies with the legacy path. Rationale: a "successful" build that Ctrl+F5 then can't run is dishonest, and Language=Cpp already hard-fails. Behavior change — flagged to the user in the final task.
- **D7 — Converged output layout = `bin/<config>/` (no TFM), exe named `AssemblyName ?? ProjectName`.** Native output has no TFM; this is what Path B ships and tests pin. Backend=Cpp BasicLang projects MOVE from `bin/<config>/<TFM>/` (CLI) / `bin/Debug/` (IDE, OutputPath-honoring) to `bin/<config>/`. `<OutputPath>` is ignored for native builds (matches Path B today). Run flows keep working: CLI probe list is updated; IDE trusts `ExecutablePath`.
- **D8 — Mixed is not a project kind (spec §1).** No Language change, no new templates. `Language=Cpp` + `.bas` files and `Language=BasicLang` + `Backend=Cpp` + `.cpp` files both converge on the same builder. `Language` keeps selecting only templates/default-extension.
- **D9 — Entry-point scanning of user C++ is a comment/string-stripped textual heuristic.** `int|auto main(`, `wmain(`, `WinMain(` count as candidates. Preprocessor-conditional mains can overcount (documented limitation; the error names every candidate file so the user can restructure). Pre-link enforcement is required because 0/2-main linker errors don't parse (recon).
- **D10 — Direction-B symbols are consumed at GLOBAL scope, not `Logic::…`.** The backend emits user code with bare global names (no C++ namespaces; `CppCodeGenOptions.Namespace` is dead). The spec §3 sketch shows `Logic::CalculateScore` — the actual call is `CalculateScore(...)` after including the module shim. Consistent with the spec's "boundary types are exactly the backend's existing model" rule; namespacing generated code is backlog.
- **D11 — Module-name → file-name handling is OrdinalIgnoreCase throughout.** Module names come verbatim from `Path.GetFileNameWithoutExtension` (ModuleResolver.cs:316–319), so `logic.bas` yields module `logic`. Split emission normalizes the sanitized module name to PascalCase-as-authored (use the name as-is) but ALL comparisons — the module==project shim-skip, generated-name dedupe, and the toolchain's duplicate-basename guard — are OrdinalIgnoreCase, because `obj/gen` lands on case-insensitive filesystems (Windows) where `game.g.h` and `Game.g.h` are the same file. Test fixtures in this plan use PascalCase file names (`Logic.bas`) so the literal filename assertions hold.

**New diagnostic IDs introduced by this plan:**

| Id | Severity | Meaning |
|---|---|---|
| BL6011 | Error | `OutputType=Exe` native project has no entry point in either language |
| BL6012 | Error | Multiple entry points across languages (message lists every candidate with file) |
| BL6013 | Error | `OutputType=Library` native project contains an entry point |
| BL6014 | Error | C/C++ sources (`.cpp .cc .cxx .c .h .hpp`) in a project targeting a managed backend (C#/MSIL/LLVM) — mixing requires the native backend |

**Retired/changed:** BL6008 removed (mixed now supported). BL6002/BL6003/BL6004 retired with the legacy IDE transpile fork. BL6007 reworded to "project contains no C++ translation units and no BasicLang sources".

**File structure (new files):**

| File | Responsibility |
|---|---|
| `BasicLang/Compiler/CodeGen/CPlusPlus/CppRuntimeSources.cs` | C# consts for the runtime header pieces currently emitted via WriteLine (BasicLangRt helpers, Task<T>, Generator<T>) — mirrors the existing `CppCollectionsRuntime` pattern |
| `BasicLang/CppCodeGenerator.Split.cs` | `partial class CppCodeGenerator`: `GenerateSplit(...)` → `CppSplitResult`; capture-based reuse of the existing private section emitters |
| `BasicLang/ProjectSystem/NativeEntryPoints.cs` | C++ TU main-scanner (comment/string stripping) + BasicLang per-unit Main counting + the Exe/Library rule → BL6011/6012/6013 |
| `VisualGameStudio.Tests/Compiler/CppSplitEmissionTests.cs` | Split output file-set/content unit tests (no toolchain) |
| `VisualGameStudio.Tests/Native/CppSplitCompileTests.cs` | Real-toolchain compile+run of split output (multi-module, collections/async/iterators) |
| `VisualGameStudio.Tests/Compiler/NativeEntryPointTests.cs` | Scanner + rule unit tests |
| `VisualGameStudio.Tests/Compiler/MixedProjectBuildTests.cs` | Builder-level + spawned-CLI e2e for mixed projects, both interop directions, entry-point matrix, BL6014 |
| `VisualGameStudio.Tests/LSP/LspMixedProjectTests.cs` | LSP project-context extension filtering tests |

Modified: `CppCodeGenerator.cs` (thin hooks only), `Compiler.cs` (BL6014 guard), `CppProjectBuilder.cs` (transpile stage + entry rule + include wiring), `CppToolchain.cs` (delete `CompileToExecutable`), `EngineDeployment.cs` (retire `UsesEngineCpp` if orphaned), `Program.cs` (routing + run probing), `ProjectFile.cs` (shared `BasicLangSourceExtensions`), `BasicLang/LSP/LspProjectContext.cs`, `BasicLang/LSP/TextDocumentSyncHandler.cs`, `BasicLang/LSP/DocumentManager.cs` (server-side gates); `BuildService.cs` (routing + legacy-path deletion), `ProjectSerializer.cs` (CppStandard decoupling), `SolutionExplorerViewModel.cs` (AddExisting item types, new-item templates for native), `MainWindowViewModel.cs` (F5 guard + LSP gating batch); test edits: `CppProjectCliBuildTests.cs`, `BuildServicePipelineTests.cs`, `TemplateBuildSweepTests.cs`, `ProjectSerializerCppTests.cs`.

Skills to apply throughout: @superpowers:test-driven-development (every task is RED→GREEN→commit), @superpowers:verification-before-completion (final task). Worktree: `.claude/worktrees/cpp-phase2`, branch `worktree-cpp-phase2`, baseline 2691/0 (1 toolchain-conditional skip).

---

### Task 1: Runtime sources extraction + `GenerateSplit` core

**Files:**
- Create: `BasicLang/Compiler/CodeGen/CPlusPlus/CppRuntimeSources.cs`
- Create: `BasicLang/CppCodeGenerator.Split.cs`
- Modify: `BasicLang/CppCodeGenerator.cs` (make class `partial`; route `EmitDotNetSurfaceHelpers`/`EmitRuntimePreamble` bodies through the new consts so there is ONE source of truth; expose `internal bool UsesFramework => _usesFramework;` and make `GenerateFrameworkExternDeclarations`' catalog reachable from the partial)
- Test: `VisualGameStudio.Tests/Compiler/CppSplitEmissionTests.cs` (create)

The split API and file inventory (project name `Game`, modules `Logic` + `Player`, entry emitted):

```
obj/gen/BasicLangRuntime.g.h   #pragma once; full std-include superset; using namespace std;
                               user #CppInclude tokens (global, deduped); BasicLangRt helpers;
                               BasicLang::Task<T>; BasicLang::Generator<T>; collections runtime;
                               full Framework_* extern "C" catalog
obj/gen/Game.g.h               #pragma once; #include "BasicLangRuntime.g.h"; fwd decls; enums;
                               delegates; interfaces; classes (inline bodies, templates);
                               inline global-variable definitions; inline static member inits;
                               extern "C" user decls; standalone fn DECLARATIONS
obj/gen/Logic.g.h              #pragma once; #include "Game.g.h"      (per-module shim)
obj/gen/Player.g.h             (shim likewise; skipped if sanitized module name == project name)
obj/gen/Logic.g.cpp            #include "Game.g.h"; non-template standalone fn DEFINITIONS with
                               IRFunction.ModuleName == "Logic"
obj/gen/Player.g.cpp           (only emitted when the module contributes definitions)
obj/gen/Game.__shared.g.cpp    definitions whose ModuleName is empty/unmatched (fallback bucket)
obj/gen/Game.main.g.cpp        int main(...) → calls BasicLang Main; ONLY when emitMain==true
```

- [ ] **Step 1: Write the failing tests.** Cover, using the existing `CompileToCpp`-style front half (lex→parse→analyze→IRBuilder per source, then `BasicCompiler.CompileProjectFiles`-equivalent combination — simplest: write two temp `.bas` files and run `new BasicCompiler(options).CompileProjectFiles(files)`, then call `GenerateSplit`):

```csharp
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CPlusPlus;   // CppCodeGenerator + CppSplitResult live HERE (see CppBackendTests.cs:8)
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class CppSplitEmissionTests
{
    private static CppSplitResult Split(bool emitMain, params (string name, string code)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-split-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var paths = files.Select(f => { var p = Path.Combine(dir, f.name); File.WriteAllText(p, f.code); return p; }).ToList();
            var compiler = new BasicCompiler(new CompilerOptions { TargetBackend = "cpp" });
            var result = compiler.CompileProjectFiles(paths);
            Assert.That(result.Success, Is.True, string.Join("\n", result.AllErrors.Select(e => e.Message)));
            var gen = new CppCodeGenerator();
            return gen.GenerateSplit(result.CombinedIR, "Game", result.Units.Select(u => u.IR).ToList(), emitMain);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Test]
    public void Split_TwoModules_EmitsRuntimeAggregateShimsAndPerModuleCpp()
    {
        var r = Split(emitMain: true,
            ("Logic.bas", "Function CalculateScore(hits As Integer) As Integer\n    Return hits * 10\nEnd Function\nSub Main()\n    PrintLine CalculateScore(3)\nEnd Sub"),
            ("Player.bas", "Function PlayerTag() As String\n    Return \"p1\"\nEnd Function"));
        Assert.That(r.Files.Keys, Is.SupersetOf(new[] {
            "BasicLangRuntime.g.h", "Game.g.h", "Logic.g.h", "Player.g.h",
            "Logic.g.cpp", "Player.g.cpp", "Game.main.g.cpp" }));
        Assert.That(r.HasBasicLangMain, Is.True);
        Assert.That(r.ProjectHeaderFileName, Is.EqualTo("Game.g.h"));
        // shims are one include deep
        Assert.That(r.Files["Logic.g.h"], Does.Contain("#include \"Game.g.h\""));
        // declarations in the aggregate, definitions in the module TU
        Assert.That(r.Files["Game.g.h"], Does.Contain("CalculateScore"));
        Assert.That(r.Files["Logic.g.cpp"], Does.Contain("CalculateScore"));
        Assert.That(r.Files["Player.g.cpp"], Does.Not.Contain("CalculateScore"));
        // main is its own TU and nowhere else
        Assert.That(r.Files["Game.main.g.cpp"], Does.Contain("int main("));
        Assert.That(r.Files["Logic.g.cpp"], Does.Not.Contain("int main("));
        Assert.That(r.TranslationUnitFileNames, Is.EquivalentTo(new[] { "Logic.g.cpp", "Player.g.cpp", "Game.main.g.cpp" }));
    }

    [Test]
    public void Split_EmitMainFalse_OmitsMainTu()
    {
        var r = Split(emitMain: false, ("Logic.bas", "Sub Main()\n    PrintLine \"hi\"\nEnd Sub"));
        Assert.That(r.HasBasicLangMain, Is.True);                    // detection independent of emission
        Assert.That(r.Files.Keys, Has.None.EqualTo("Game.main.g.cpp"));
        Assert.That(string.Join("|", r.Files.Values), Does.Not.Contain("int main("));
    }

    [Test]
    public void Split_HeadersHaveNoMainAndCppsHaveNoPragmaOnce()
    {
        var r = Split(emitMain: true, ("Logic.bas", "Sub Main()\nEnd Sub"));
        foreach (var (name, content) in r.Files)
        {
            if (name.EndsWith(".g.h")) Assert.That(content, Does.StartWith("#pragma once"), name);
            if (name.EndsWith(".g.cpp")) Assert.That(content, Does.Not.Contain("#pragma once"), name);
        }
    }

    [Test]
    public void Split_ClassAndGenerics_LiveEntirelyInAggregateHeader()
    {
        var r = Split(emitMain: true, ("Logic.bas",
            "Class Player\n    Public Name As String\n    Function Tag() As String\n        Return Name\n    End Function\nEnd Class\nSub Main()\n    Dim p As New Player()\nEnd Sub"));
        Assert.That(r.Files["Game.g.h"], Does.Contain("class Player"));
        Assert.That(r.Files["Logic.g.cpp"], Does.Not.Contain("class Player"));
    }

    [Test]
    public void Split_RuntimeHeader_ContainsRuntimeAndFrameworkCatalogOnce()
    {
        var r = Split(emitMain: true, ("Logic.bas", "Sub Main()\n    Dim xs As New List(Of Integer)\n    xs.Add(1)\nEnd Sub"));
        var rt = r.Files["BasicLangRuntime.g.h"];
        Assert.That(rt, Does.Contain("namespace BasicLang"));        // collections/Task/Generator home
        Assert.That(rt, Does.Contain("Framework_Initialize"));       // full catalog, always
        Assert.That(r.Files["Game.g.h"], Does.Not.Contain("namespace BasicLangRt")); // runtime only in runtime header
    }

    [Test]
    public void Split_ModuleNamedLikeProject_SkipsShimWithoutCollision()
    {
        // deliberately lower-case on disk: module name comes back verbatim as "game",
        // and the shim-skip comparison MUST be OrdinalIgnoreCase (D11) or obj/gen gets
        // game.g.h AND Game.g.h — the same file on Windows, silently overwritten
        var r = Split(emitMain: true, ("game.bas", "Sub Main()\nEnd Sub"));
        Assert.That(r.Files.Keys.Count(k => k.Equals("Game.g.h", StringComparison.OrdinalIgnoreCase)),
                    Is.EqualTo(1));
    }

    [Test]
    public void SingleStringGenerate_IsUnchangedByThisTask()
    {
        // canary: the legacy API still emits main + inline runtime (dozens of tests pin it)
        var dir = Path.Combine(Path.GetTempPath(), "bl-split-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var p = Path.Combine(dir, "m.bas");
            File.WriteAllText(p, "Sub Main()\n    PrintLine \"x\"\nEnd Sub");
            var compiler = new BasicCompiler(new CompilerOptions { TargetBackend = "cpp" });
            var result = compiler.CompileProjectFiles(new[] { p });
            var single = new CppCodeGenerator().Generate(result.CombinedIR);
            Assert.That(single, Does.Contain("int main("));
            Assert.That(single, Does.Contain("using namespace std;"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
```

- [ ] **Step 2: Run to verify failure.** `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~CppSplitEmissionTests"` — expected: compile error (`GenerateSplit`/`CppSplitResult` do not exist).

- [ ] **Step 3: Implement.**
  1. `CppRuntimeSources.cs`: move the string bodies of `EmitDotNetSurfaceHelpers` (BasicLangRt) and `EmitRuntimePreamble` (Task<T>, Generator<T>) into consts (`DotNetSurfaceHelpers`, `TaskEmulation`, `GeneratorCoroutine`) following the `CppCollectionsRuntime` pattern; rewrite the two emit methods to splice the consts line-by-line so `Generate()` output is byte-identical (the canary + existing suite enforce this).
  2. Mark `CppCodeGenerator` as `partial`. In `CppCodeGenerator.Split.cs` add:

```csharp
public sealed class CppSplitResult
{
    public Dictionary<string, string> Files { get; } = new();          // fileName → content
    public bool HasBasicLangMain { get; set; }
    public bool UsesFramework { get; set; }
    public string ProjectHeaderFileName { get; set; } = "";
    public List<string> TranslationUnitFileNames { get; } = new();     // .g.cpp files to compile
}

public CppSplitResult GenerateSplit(IRModule combined, string projectName,
                                    IReadOnlyList<IRModule> unitModules, bool emitMain)
```

  Implementation strategy — **capture, don't rewrite**: add a private helper that swaps `_output` to a fresh `StringBuilder`, runs an emission action, restores, returns the text. Run the capability check once on `combined` (same gate as `Generate`). Then:
  - Emit body/definition sections FIRST (so `_usesFramework` and helper-usage flags are populated before the runtime header is rendered), capturing: per-`combined.Functions` standalone definitions (excluding class methods via the existing `IsClassMethod`), grouped by `IRFunction.ModuleName` (sanitize; empty/unknown → `__shared` bucket); template functions are NOT captured here — they go declaration+definition into the aggregate header.
  - Capture the aggregate-header sections in the exact `Generate()` order: fwd decls, enums, delegates, interfaces, classes, static member inits (prefix `inline `), globals (emit as `inline` definitions), extern "C" user decls, standalone fn declarations, template fn full definitions.
  - Render the runtime header LAST: `#pragma once`, the full std-include superset (union of the fixed set + coroutine/exception + unordered_map/unordered_set/stdexcept + `_headerIncludes`), user `#CppInclude` tokens from `combined.CppIncludes` (global, deduped), `using namespace std;`, `CppRuntimeSources` consts + `CppCollectionsRuntime.Source`, then the FULL framework extern catalog (refactor `GenerateFrameworkExternDeclarations` so the whole name→declaration map can be rendered unconditionally; the used-only string-insert hack in `Generate()` stays untouched).
  - Assemble: runtime header; `<SafeProject>.g.h` = pragma once + `#include "BasicLangRuntime.g.h"` + aggregate sections; per input module (sanitized name from each unit `IRModule`'s name, used as-authored) a shim `<Module>.g.h` unless it equals `<SafeProject>` **OrdinalIgnoreCase** (D11 — module names come verbatim from file basenames, and `game.g.h` vs `Game.g.h` are the same file on Windows); per module WITH captured definitions a `<Module>.g.cpp` = `#include "<SafeProject>.g.h"` + definitions; `emitMain` → `<SafeProject>.main.g.cpp` reusing `GenerateMainFunction`'s body logic (set `HasBasicLangMain` from a case-insensitive standalone `Main` lookup on `combined` regardless of `emitMain`).
  - Populate `TranslationUnitFileNames` with every emitted `.g.cpp`.
  3. Expose `internal bool UsesFramework` and set `result.UsesFramework = _usesFramework` after definition capture.

- [ ] **Step 4: Run the new fixture AND the pinned legacy fixtures.**

```powershell
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~CppSplitEmissionTests|FullyQualifiedName~CppBackendTests|FullyQualifiedName~CppCollectionTests|FullyQualifiedName~CppPassthroughTests"
```
Expected: all PASS (legacy single-string output byte-stable).

- [ ] **Step 5: Commit.** `git add -A; git commit -m "feat(cpp): split emission core - runtime header + aggregate header + per-module TUs (GenerateSplit)"`

---

### Task 2: Split output compiles and runs against a real toolchain

**Files:**
- Test: `VisualGameStudio.Tests/Native/CppSplitCompileTests.cs` (create)
- Modify: `BasicLang/CppCodeGenerator.Split.cs` (fixes the compiler shakes out)

- [ ] **Step 1: Write the failing/probing tests.** Reuse the `CppCompile` helper's compiler discovery (`FindRunCompiler`) but compile a **directory of files**: write all `CppSplitResult.Files` into a temp dir, compile `TranslationUnitFileNames` + a hand-written consumer TU, run, assert stdout. Cases (all `Assert.Ignore` when no compiler):
  1. `Split_MultiModuleProgram_CompilesAndRuns` — modules Logic+Main across 2 files, `PrintLine CalculateScore(3)` → stdout `30`.
  2. `Split_DirectionB_UserCppCallsBasicLang` — BasicLang side authored as `Logic.bas` (PascalCase per D11); user `consumer.cpp` does `#include "Logic.g.h"` and calls `CalculateScore(4)` at GLOBAL scope (D10 — no `Logic::` namespace), prints; BasicLang has NO Main; `emitMain:false`; user TU provides `int main`. Asserts C++→BasicLang interop through the shim header — **the Phase 2 headline feature**.
  3. `Split_CollectionsAsyncIterators_CompileThroughSharedRuntimeHeader` — one module using `List(Of T)`, one `Async Function`, one `Iterator Function`; compiles (runtime lives once in the shared header; templates ODR-fine).
  4. `Split_ClassAcrossModules_SharedPtrRoundTrip` — module A defines `Class Player`, module B function takes/returns it; user main constructs via generated API (`std::make_shared<Player>()`), calls B's function, prints.
- [ ] **Step 2: Run.** `dotnet test ... --filter "FullyQualifiedName~CppSplitCompileTests"` — expect failures/ignores; iterate.
- [ ] **Step 3: Fix `GenerateSplit` until green.** Likely shakeout: missing std includes in the superset, decl/def ordering, template routing, `inline` spelling on globals/static inits, shim self-include.
- [ ] **Step 4: Full C++-related sweep.** `--filter "FullyQualifiedName~Cpp"` — all green.
- [ ] **Step 5: Commit.** `git commit -m "test(cpp): split emission verified against real toolchain incl. C++->BasicLang direction"`

---

### Task 3: Cross-language entry-point analysis (`NativeEntryPoints`)

**Files:**
- Create: `BasicLang/ProjectSystem/NativeEntryPoints.cs`
- Test: `VisualGameStudio.Tests/Compiler/NativeEntryPointTests.cs` (create)

- [ ] **Step 1: Write the failing tests.**

```csharp
using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class NativeEntryPointTests
{
    [TestCase("int main() { return 0; }", 1)]
    [TestCase("int main(int argc, char* argv[]) { }", 1)]
    [TestCase("auto main() -> int { }", 1)]
    [TestCase("int wmain(int argc, wchar_t** argv) { }", 1)]
    [TestCase("int WinMain(void*, void*, char*, int) { }", 1)]
    [TestCase("// int main() { }", 0)]
    [TestCase("/* int main() { } */", 0)]
    [TestCase("const char* s = \"int main()\";", 0)]
    [TestCase("void mainframe(); int domain();", 0)]           // word boundaries
    [TestCase("int main(); ", 0)]                              // declaration only (no body brace)
    [TestCase("int main() { } int wmain() { }", 2)]
    public void CountMains_TextualHeuristic(string source, int expected)
        => Assert.That(NativeEntryPoints.CountCppMains(source), Is.EqualTo(expected));

    [Test]
    public void Rule_Exe_ZeroMains_IsBL6011()
    {
        var d = NativeEntryPoints.Apply(isExe: true, basicLangMainCount: 0,
            cppMains: new List<(string file, int count)>());
        Assert.That(d, Has.Count.EqualTo(1));
        Assert.That(d[0].Code, Is.EqualTo("BL6011"));
    }

    [Test]
    public void Rule_Exe_BasicLangMainPlusCppMain_IsBL6012_ListingCandidates()
    {
        var d = NativeEntryPoints.Apply(true, 1, new() { ("main.cpp", 1) });
        Assert.That(d[0].Code, Is.EqualTo("BL6012"));
        Assert.That(d[0].Message, Does.Contain("main.cpp").And.Contain("Main"));
    }

    [Test]
    public void Rule_Exe_ExactlyOneEither_IsClean()
    {
        Assert.That(NativeEntryPoints.Apply(true, 1, new()), Is.Empty);
        Assert.That(NativeEntryPoints.Apply(true, 0, new() { ("main.cpp", 1) }), Is.Empty);
    }

    [Test]
    public void Rule_Library_AnyMain_IsBL6013()
    {
        Assert.That(NativeEntryPoints.Apply(false, 1, new())[0].Code, Is.EqualTo("BL6013"));
        Assert.That(NativeEntryPoints.Apply(false, 0, new() { ("a.cpp", 1) })[0].Code, Is.EqualTo("BL6013"));
        Assert.That(NativeEntryPoints.Apply(false, 0, new()), Is.Empty);
    }
}
```

- [ ] **Step 2: Run → FAIL** (type missing).
- [ ] **Step 3: Implement.** `CountCppMains`: strip `//…`, `/*…*/`, `"…"` (with `\"` escapes) and `'…'`; then count `\b(?:int|auto)\s+(?:main|wmain)\s*\([^;]*?\)\s*(?:->\s*int\s*)?\{` plus `\bWinMain\s*\([^;]*?\)\s*\{` (definition = followed by `{`, not `;`). `Apply(isExe, basicLangMainCount, cppMains)` returns `List<CppDiagnostic>` implementing the D9 rule; messages name every candidate ("Main (BasicLang)" / "<file>: main"). Document the heuristic limits (preprocessor-conditional mains) in the XML doc.
- [ ] **Step 4: Run → PASS.**
- [ ] **Step 5: Also add a probe test for the duplicate-BasicLang-Main question** (recon open question): two `.bas` files each with `Sub Main` through `CompileProjectFiles` — pin whatever the frontend does today (semantic error OR silent first-wins). If silent, that justifies counting from per-unit IRs in Task 4; add the finding as a comment. Commit: `git commit -m "feat(cpp): cross-language entry-point analysis (BL6011/6012/6013)"`

---

### Task 4: `CppProjectBuilder` convergence — transpile stage, mixed partition, include wiring

**Files:**
- Modify: `BasicLang/ProjectSystem/CppProjectBuilder.cs` (the bulk)
- Modify: `BasicLang/ProjectSystem/ProjectFile.cs` (add `public static readonly string[] BasicLangSourceExtensions` = the 6-extension list; default globs + BL6008-successor partition + LSP (Task 9) all consume it)
- Test: `VisualGameStudio.Tests/Compiler/MixedProjectBuildTests.cs` (create), edits to `VisualGameStudio.Tests/Compiler/CppProjectCliBuildTests.cs`

New `Build` flow (replaces the BL6008 guard):

```
1. Partition GetSourceFiles(): blSources = BasicLangSourceExtensions matches (excluding
   IsInBuildOutputDir — keep the guard's existing filter); userTus = GetCppTranslationUnits();
   headers/others ignored (reachable via include path).
2. blSources OR userTus empty+empty → BL6007 (reworded).
3. If blSources.Any(): run the transpile stage:
   - KEEP the BasicCompiler instance: `var compiler = new BasicCompiler(new CompilerOptions
     { TargetBackend = "cpp", … }); var result = compiler.CompileProjectFiles(blSources);`
   - semantic/parse failure → **`SemanticError` has NO FilePath, and `result.Units` is EMPTY on
     failure** (Compiler.cs returns via FinalizeResult before Units is populated). Attribute errors
     per-unit via `compiler.Registry.Modules` → each unit's `Errors` + `FilePath` — mirror the
     existing `BuildService.MapCompilerDiagnostics` pattern (BuildService.cs:795–829), with a
     fallback diagnostic pinned to the .blproj for anything unattributable. Map into CppDiagnostic
     {FilePath, Line, Column, Code from the error if present, Message} → failed result (Error List
     clickable via the existing FormatNormalized echo).
   - basicLangMainCount = per-unit standalone Main count (case-insensitive) from
     `compiler.Registry.Modules` per-unit IRs (failure-safe; result.Units only fills on success) —
     NOT from CombinedIR (combiner silently dedupes; Task 3 probe documents this).
4. Entry rule: cppMains = userTus.Select(t => (t, NativeEntryPoints.CountCppMains(File.ReadAllText(t)))).
   diagnostics = NativeEntryPoints.Apply(isExe, basicLangMainCount, cppMains) → fail if any.
   emitMain = isExe && basicLangMainCount == 1 (the single-entry winner is BasicLang's Main).
5. If transpiled: GenerateSplit(combinedIR, name, unitIRs, emitMain) — **wrap THIS call in the
   CppCapabilityException catch** (the capability gate throws from codegen, not from
   CompileProjectFiles, which does no codegen) → single error diagnostic, code "BL6001" (matches
   the IDE's existing meaning for capability violations). CLEAN obj/gen of *.g.h/*.g.cpp first
   (stale files from renamed modules would otherwise still compile; deletion is OrdinalIgnoreCase-
   safe on Windows per D11), write Files, generatedTus = TranslationUnitFileNames as absolute paths.
6. Toolchain null → BL6005 (now ALL native projects — D6).
7. request.SourceFiles = userTus + generatedTus; IncludeDirs = [projectDir, objGenDir, …items];
   engine lib: existing NativeLib resolution UNCHANGED, plus auto-append (and DLL deploy) when
   split.UsesFramework && no explicit engine NativeLib item — resolve via LocateImportLib, BL6009 if
   framework-used-but-unresolvable.
8. compile_commands (now covers generated TUs automatically) → Compile → ApplyCompileOutcome →
   engine DLL deploy — all existing code, untouched.
```

- [ ] **Step 1: Write the failing tests** in `MixedProjectBuildTests` (`[NonParallelizable]`; toolchain-skip pattern; temp-dir + retry teardown; follow `CppProjectCliBuildTests` helper shapes — write `.blproj` + sources, call `CppProjectBuilder.Build(ProjectFile.Load(...), "Debug")`):
  1. `Mixed_LanguageCppProject_WithBasFile_BuildsAndRuns` — `Language=Cpp` project: `main.cpp` (has `int main`, includes `"Logic.g.h"`, calls `CalculateScore`), `logic.bas` (no Main). Succeeds; run exe → expected stdout. **Direction B through the real builder.**
  2. `Mixed_BasicLangNativeProject_WithUserCpp_DirectionA` — `Language` absent, `<Backend>Cpp</Backend>`: `App.bas` with `Sub Main` + `#CppInclude "helper.h"` + `::`-call into user `helper.h` (header-only) sitting NEXT TO the .blproj. Succeeds (proves projectDir include wiring; impossible on the old path). Run → stdout.
  3. `Mixed_PureBasicLang_NativeBackend_StillBuilds` — no user .cpp at all; BL6007 must NOT fire; exe at `bin/Debug/<name>.exe` (converged layout).
  4. `Mixed_BothMains_FailsBL6012` / `Mixed_ExeNoMain_FailsBL6011` / `Mixed_LibraryWithMain_FailsBL6013`.
  5. `Mixed_NoToolchain_HardFailsBL6005` — follow the existing `Build_NoToolchain_FailsWithBL6005` (CppProjectCliBuildTests.cs:153) mechanism exactly: it is an environment BRANCH, not a simulation seam — assert BL6005 when `CppToolchain.Find() == null`, assert its absence otherwise.
  6. `Mixed_SemanticError_SurfacesAsClickableDiagnostic` — `.bas` with a type error → failed result, diagnostic has FilePath ending `Logic.bas` + Line>0 (exercises the Registry.Modules attribution path — `result.Units` is empty on failure).
  7. `Mixed_CompileCommands_IncludesGeneratedAndUserTus_AndObjGenInclude` — parse `obj/compile_commands.json`, assert entries for `main.cpp` AND `Logic.g.cpp`, and `-I`/`/I` containing `obj\gen`.
  8. `Mixed_StaleGeneratedFiles_AreCleaned` — pre-create `obj/gen/Old.g.cpp` with garbage; build; assert it is gone and build succeeded.
- [ ] **Step 2: Run → FAIL** (BL6008 fires / APIs missing).
- [ ] **Step 3: Implement the flow above.** Update `Build_BasicLangSourcesPresent_FailsWithBL6008` → replace with `Build_MixedSources_NoLongerRejected` (asserts BL6008 is gone; folds into test 1). Keep `ProjectFile.GetSourceFiles`/`GetCppTranslationUnits` semantics UNTOUCHED (LSP + explicit-item behavior depend on them); the partition lives in the builder.
- [ ] **Step 4: Run the fixture + neighbors.** `--filter "FullyQualifiedName~MixedProjectBuildTests|FullyQualifiedName~CppProjectCliBuildTests"` → PASS.
- [ ] **Step 5: Commit.** `git commit -m "feat(cpp): CppProjectBuilder builds mixed projects - transpile stage, entry rule, obj/gen include wiring (retires BL6008)"`

---

### Task 5: CLI routing — all native projects through the builder; run/probe updates

**Files:**
- Modify: `BasicLang/Program.cs` (`HandleBuildCommand` ~:406–835, `HandleRunCommand` ~:857–950)
- Test: extend `VisualGameStudio.Tests/Compiler/MixedProjectBuildTests.cs` (spawned-CLI e2e region)

- [ ] **Step 1: Failing tests** (RunCli pattern): `Cli_Build_MixedProject_BothDirections_AndRun` — build the Task 4 fixture-1 project via spawned `BasicLang.exe build`, assert exit 0 + exe exists at `bin/Debug/`; then `BasicLang.exe run` → expected stdout. `Cli_Build_BackendCppBasicLangProject_LandsInConvergedLayout` — a plain Backend=Cpp .bas project builds to `bin/Debug/<name>.exe` (NOT `bin/Debug/<TFM>/`), and `run` finds it. `Cli_Build_MixedProject_CppError_EmitsNormalizedDiagnostic` — bad user .cpp → stderr contains `file(line,col): error`.
- [ ] **Step 2: Run → FAIL** (old layout / raw diagnostics).
- [ ] **Step 3: Implement.** In `HandleBuildCommand`: route `project.IsCppProject || IsNativeBackend(project)` (helper: Backend/TargetBackend normalized ∈ {"cpp","c++"}) to the `CppProjectBuilder` branch (existing Language=Cpp branch, now shared); DELETE the legacy cpp arm of the backend switch (single-TU write + `CompileToExecutable` call + `UsesEngineCpp` scan, ~:733–835). Restore stays skipped for native routes (native builds don't consume NuGet packages). In `HandleRunCommand`: use the same `IsNativeBackend` condition for native-first exe probing. The single-file path (`BasicLang.exe file.bas --target=cpp`) is UNTOUCHED (still single-string `Generate`, stops at source).
- [ ] **Step 4: Run fixture + `CliBuildTests` + `TemplateBuildSweepTests`** (native templates' output-path expectations may pin the old TFM layout — update those assertions to `bin/<config>/`). PASS.
- [ ] **Step 5: Commit.** `git commit -m "feat(cpp): CLI routes all native projects through CppProjectBuilder; converged bin/<config> layout"`

---

### Task 6: BL6014 — C/C++ sources on managed backends fail with a clear error

**Files:**
- Modify: `BasicLang/Compiler.cs` (`CompileProjectFiles` entry, ~:199)
- Test: extend `MixedProjectBuildTests` (unit-level, no toolchain needed)

- [ ] **Step 1: Failing tests.** `ManagedBackend_CppSource_FailsBL6014` — `CompileProjectFiles` with a `.cpp` in the list and `TargetBackend="csharp"` → `Success=false`, one error whose message contains `BL6014` and the file name, and NO BasicLang parse errors for that file (fail fast, don't lex it). Same for `"msil"`/`"llvm"` via `[TestCase]`. `NativeBackend_CppSourceInList_IsIgnoredNotLexed` — with `TargetBackend="cpp"`, C-family files in the list are silently skipped (the builder passes only .bas, but defensive: no parse errors from them).
- [ ] **Step 2: Run → FAIL** (today: lexer explosion on managed, parse errors on cpp).
- [ ] **Step 3: Implement** at the top of `CompileProjectFiles`: partition input by C-family extensions `{.cpp,.cc,.cxx,.c,.h,.hpp}`; if any and backend is managed → add error (`BL6014: C/C++ source files require the native C++ backend (<Backend>Cpp</Backend>): <files>`) matching the existing `SemanticError` shape (embed the code in the message if the type has no code field) and return failure; if backend is cpp → drop them from the lex list silently. Managed behavior for `.bas`-only projects unchanged.
- [ ] **Step 4: Run new tests + `CompilationTests`/`BuildServicePipelineTests` smoke** (the fixture is named `CompilationTests`, NOT `CompilerTests` — an empty `--filter` match errors out; IDE managed path picks the guard up for free via `CompileProjectFiles`). PASS.
- [ ] **Step 5: Commit.** `git commit -m "feat(cpp): BL6014 - C++ sources on managed backends fail with actionable error (spec §9)"`

---

### Task 7: IDE BuildService convergence + legacy-path deletion

**Files:**
- Modify: `VisualGameStudio.ProjectSystem/Services/BuildService.cs` (routing :332–358; delete `CompileGeneratedCpp` :1089–1160 and the `backend=="cpp"` codegen arm :627–635)
- Modify: `BasicLang/ProjectSystem/CppToolchain.cs` (DELETE `CompileToExecutable` — last caller gone), `BasicLang/ProjectSystem/EngineDeployment.cs` (delete `UsesEngineCpp` if grep shows no remaining callers)
- Modify: `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs` (F5 guard :3224–3234)
- Test: extend `VisualGameStudio.Tests/Services/BuildServicePipelineTests.cs`

- [ ] **Step 1: Failing tests.** `IdeBuild_MixedProject_Succeeds_AndFiresBuildCompletedWithDiagnostics` — in-process BuildService on a mixed fixture (Language=BasicLang + TargetBackend=Cpp + user .cpp): success path sets `ExecutablePath` under `bin/<config>/`; then introduce a C++ error → `BuildCompleted` fires with a per-file `DiagnosticItem` (FilePath endsWith the .cpp, Line > 0) — NOT one blob. `IdeBuild_BackendCppPureBasicLang_RoutesThroughSharedBuilder` — assert converged layout + parsed diagnostics on a semantic error. Known dying pins to update: `Build_ConsoleAppTemplate_Cpp_ProducesCppSource_NotCs` (BuildServicePipelineTests.cs:114–147) pins THREE behaviors this task deletes — `GeneratedFileName` ends with `.cpp`, `.cpp` files in the OutputPath-honoring dir, and no-toolchain → source-only success (:142–146, the direct D6 casualty) — rewrite it for the converged path; also reword the assertion MESSAGE at BuildServicePipelineTests.cs:455 that mentions "BL6002" (Task 11's retirement grep will flag it).
- [ ] **Step 2: Run → FAIL.**
- [ ] **Step 3: Implement.** Routing: `if (project.Language == ProjectLanguage.Cpp || project.TargetBackend == TargetBackend.Cpp) → BuildCppProject(...)` — the enum is named `TargetBackend` (VisualGameStudio.Core/Models/BasicLangProject.cs:51–57: CSharp, Cpp, LLVM, MSIL); there is NO `BackendType`. Same no-throw contract, same fall-through to shared finalization (the `BuildCompleted` invariant at :380 — NO new early returns). Delete `CompileGeneratedCpp` + the cpp codegen arm + BL6002/6003/6004 emission sites (verified complete caller set: CompileToExecutable = Program.cs:803 [gone in Task 5] + BuildService.cs:1135; UsesEngineCpp = Program.cs:786 + BuildService.cs:1110; BL6002/6003/6004 emission = BuildService.cs:1099/1122/1141; NO test calls either API directly). Then delete `CppToolchain.CompileToExecutable` and `EngineDeployment.UsesEngineCpp`. Widen the F5 guard (MainWindowViewModel.cs:3226) to `Language == ProjectLanguage.Cpp || TargetBackend == TargetBackend.Cpp` → existing "Native C++ debugging arrives in a later phase — use Ctrl+F5" message.
- [ ] **Step 4: Run.** `--filter "FullyQualifiedName~BuildServicePipelineTests|FullyQualifiedName~TemplateBuildSweepTests"` (the native BasicLang templates now build through the converged path) → PASS.
- [ ] **Step 5: Commit.** `git commit -m "feat(cpp): IDE builds all native projects via shared CppProjectBuilder; legacy single-TU path deleted (BL6002-6004 retired)"`

---

### Task 8: IDE project-surface fixes — serializer, add-file item types, new-item templates

**Files:**
- Modify: `VisualGameStudio.ProjectSystem/Serialization/ProjectSerializer.cs` (:59–74 load, :209–214 save)
- Modify: `VisualGameStudio.Shell/ViewModels/Panels/SolutionExplorerViewModel.cs` (:1219–1223 AddExisting; new-item template availability around :1000/:1090)
- Test: extend `VisualGameStudio.Tests/Serialization/ProjectSerializerCppTests.cs`; SolutionExplorer VM tests if an existing fixture covers these flows (else assert via serializer round-trips)

- [ ] **Step 1: Failing tests.** `Serializer_CppStandard_RoundTrips_ForBasicLangNativeProject` — Language absent (BasicLang) + `<CppStandard>c++17</CppStandard>` + IncludeDir/NativeLib/Define items: LoadAsync populates CppSettings; SaveAsync re-emits CppStandard (today it is dropped). `Serializer_MixedCompileItems_RoundTripVerbatim` — `.bas` + `.cpp` + `.h` Compile items survive a load/save cycle byte-for-content.
- [ ] **Step 2: Run → FAIL** (CppStandard dropped for Language=BasicLang).
- [ ] **Step 3: Implement.** Load: parse `<CppStandard>` regardless of Language (keep `<Language>` handling as-is). Save: emit `<CppStandard>` whenever `CppSettings?.CppStandard` is non-default, independent of Language (keep emitting `<Language>` only when Cpp — D8). AddExistingFileAsync: replace the hardcoded `ProjectItemType.Compile` with `GetItemTypeForExtension` (a `.txt` added via All Files must no longer become a Compile item). New-item templates: for native projects (`Language==Cpp || TargetBackend==Cpp`), the new-file template list offers BOTH language groups (read the current gating around the two default-extension sites; if templates are already ungated by language, verify and skip with a note). Default-extension logic stays language-based.
- [ ] **Step 4: Run serializer + solution-explorer fixtures → PASS.**
- [ ] **Step 5: Commit.** `git commit -m "fix(ide): CppStandard round-trip for native BasicLang projects; AddExisting respects item types; C++ file templates on native projects"`

---

### Task 9: BasicLang LSP server — extension filtering for mixed projects

**Files:**
- Modify: `BasicLang/LSP/LspProjectContext.cs` (filter in `GetBlprojSourceFiles` :366–397; reconcile sibling patterns :340 via the shared list)
- Modify: `BasicLang/LSP/TextDocumentSyncHandler.cs` (:32–47) + `BasicLang/LSP/DocumentManager.cs` (`UpdateDocument`) — defense-in-depth non-BasicLang URI gate
- Test: `VisualGameStudio.Tests/LSP/LspMixedProjectTests.cs` (create — NOTE: no existing test exercises `LspProjectContext`; this fixture is its FIRST. Construct `LspProjectContextProvider` directly: both types are `public` (LspProjectContext.cs:19, 130) and BasicLang has `InternalsVisibleTo("VisualGameStudio.Tests")`. Pattern-match the harness style of the fixtures in `VisualGameStudio.Tests/LSP/` — namespace is `VisualGameStudio.Tests.LSP`)

- [ ] **Step 1: Failing tests.** `MixedBlproj_ProjectContext_ExcludesCppItems` — .blproj with `.bas` + `.cpp` + `.h` Compile items: project context's file set / module registry contains the `.bas` module but NO module named after the `.cpp` basename, and IndeterminateImports does not contain it. `CppFileSharingBasename_DoesNotPolluteRealModule` — `logic.bas` + `logic.cpp`: the `Logic` module's symbols come from the `.bas` only (the MergeFrom hazard). `DidOpen_CppUri_PublishesNoDiagnostics_AndRegistersNoDocument` — via DocumentManager directly.
- [ ] **Step 2: Run → FAIL** (junk modules registered today).
- [ ] **Step 3: Implement.** Use `ProjectFile.BasicLangSourceExtensions` (added in Task 4) as the single whitelist: filter in `GetBlprojSourceFiles` (NOT inside `ProjectFile.GetSourceFiles` — builder partition depends on it); replace the sibling-scan literal pattern list with patterns derived from the same array (adds `.basic`/`.class`, closing the recon-noted inconsistency). DocumentManager.UpdateDocument: early-return (no registration, no diagnostics) for URIs whose extension is not in the whitelist; `GetTextDocumentAttributes` answers non-BasicLang URIs as `"plaintext"`.
- [ ] **Step 4: Run new fixture + the whole LSP test group** — use `--filter "FullyQualifiedName~LSP|FullyQualifiedName~Lsp"` (the LSP-folder fixtures live in namespace `VisualGameStudio.Tests.LSP`, uppercase; only four fixtures elsewhere literally contain "Lsp", and vstest `~` matching may be case-sensitive) → PASS.
- [ ] **Step 5: Commit.** `git commit -m "fix(lsp): BasicLang server ignores non-BasicLang files in mixed projects (single shared extension whitelist)"`

---

### Task 10: IDE LSP gating batch — the ten ungated call sites

**Files:**
- Modify: `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs` — gate on `BasicLangFileTypes.IsBasicLangSourceFile(<doc>.FilePath)`: RenameSymbolAsync (:4874), FormatDocumentContentAsync (:7595 — covers format-on-save), OnTypeFormatting (:7629), GoToTypeDefinition (:4403), FindReferences (:4627), ShowCodeActionsAsync (:7664), document highlights (:2101), document symbols (:4200), selection ranges (:7383), FallbackToLspHover (:1665)
- Test: none unit-testable without an LSP harness (these are VM command paths); verification is compile + the Task 11 manual smoke (line numbers above are recon-verified anchors — re-grep before editing, the file is >7,000 lines and drifts)

- [ ] **Step 1: Gate each site** with an early return (silent no-op for non-BasicLang docs; format-on-save must skip silently, not error). Match the style Phase 1 used for GoToDefinition (:4433).
- [ ] **Step 2: `dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release`** → 0 errors. Grep the file for `IsConnected` LSP calls WITHOUT an `IsBasicLangSourceFile` check in the surrounding method to confirm none of the ten were missed and no new ones exist.
- [ ] **Step 3: Commit.** `git commit -m "fix(ide): gate remaining ten LSP features on BasicLang file type (rename/format/references/etc.)"`

---

### Task 11: Full verification — suite, CLI smoke, IDE smoke, docs, memory

**Files:**
- Modify: `docs/superpowers/specs/2026-07-11-cpp-language-support-design.md` (status header only: Phase 2 shipped note), auto-memory
- Test: everything

- [ ] **Step 1: Full suite.**

```powershell
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release
```
Expected: 0 failures (baseline 2691 + new tests; 1 pre-existing conditional skip). Then verify no orphaned symbols: `Grep "CompileToExecutable|UsesEngineCpp|BL6002|BL6003|BL6004"` → only historical docs/plan hits.

- [ ] **Step 2: CLI smoke (through the real binary).**

There is NO native-BasicLang template id (CLI ids: console, classlib, game, empty, webapi, sln, test, cpp-console, cpp-library, cpp-game — and `console` pins `<TargetBackend>CSharp</TargetBackend>`). Hand-write the fixture:

```powershell
$d = "$env:TEMP\MixedSmoke"; Remove-Item $d -Recurse -Force -ErrorAction SilentlyContinue; New-Item -ItemType Directory $d | Out-Null
# Write (with the Write tool, not Set-Content) MixedSmoke.blproj:
#   <Project><PropertyGroup><OutputType>Exe</OutputType><Backend>Cpp</Backend></PropertyGroup>
#   <ItemGroup><Compile Include="Logic.bas"/><Compile Include="main.cpp"/></ItemGroup></Project>
# plus Logic.bas (CalculateScore, NO Main) and main.cpp (#include "Logic.g.h", int main prints CalculateScore(3))
BasicLang/bin/Release/net8.0/BasicLang.exe build $d\MixedSmoke.blproj              # succeeds
& "$d\bin\Debug\MixedSmoke.exe"                                                     # prints 30
Get-Content $d\obj\compile_commands.json                                            # entries for main.cpp AND Logic.g.cpp
```

- [ ] **Step 3: IDE smoke (manual, real Shell).** `dotnet clean` + build Shell, run it. Verify: (1) open a mixed project — builds, runs with Ctrl+F5; (2) introduce errors in BOTH the `.bas` and the `.cpp` — Error List shows clickable per-file entries for each; (3) F5 on a native project → the "later phase" message (not a debug-adapter crash); (4) rename/format/find-references on an open `.cpp` → silent no-ops; on a `.bas` → still work; (5) `.bas` file inside the mixed project has IntelliSense with NO spurious diagnostics; (6) add a `.cpp` to a BasicLang native project and a `.bas` to a C++ project via Solution Explorer — both build; (7) a pure managed (C# backend) project with a stray `.cpp` → BL6014 in Error List; (8) pure BasicLang and pure C++ projects still build/run unchanged.
- [ ] **Step 4: Behavior-change callout to the user (in the completion report, not docs):** native output moved to `bin/<config>/` (no TFM); `<OutputPath>` ignored for native; no-toolchain native builds now hard-fail BL6005 (was warn+source-only); Library on the native backend now produces a real `.lib/.a` with zero entry points enforced.
- [ ] **Step 5: Update memory/docs + final commit.** Spec status line; auto-memory `cpp-peer-language.md` Phase 2 status. `git add -A; git commit -m "feat(cpp): Phase 2 complete - mixed BasicLang+C++ projects, converged native pipeline"`

---

## Out of scope for this plan (later phases, per spec §6)

- clangd IntelliSense for `.cpp` (LSP registry, discovery/download, compile_commands on open/change, debounced gen-header refresh) — Phase 3. This plan's `obj/gen` + compile_commands work is deliberately Phase-3-ready.
- Native debugging (lldb-dap/gdb DAP, `#line` directives) — Phase 4.
- Incremental per-TU `.o` caching; qualifying generated code to drop `using namespace std;` from headers; per-file `#CppInclude` attribution; response files for long MSVC command lines; gen→`.bas` go-to-def — Phase 5 backlog.
- The IDE serializer's pre-existing stripping of PackageReference/TargetFramework/UseWindowsForms/UseWPF (recon-confirmed, predates Phase 2, affects managed projects) — separate fix, flagged as a spawnable background task.
- P/Invoke auto-bridging for mixed projects on the C# backend — explicitly out of scope per spec non-goals.
