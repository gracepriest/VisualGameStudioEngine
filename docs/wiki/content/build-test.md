title: Building and testing
lede: The commands, the expected numbers, and how to tell a real failure from noise.
---
## Build

```powershell
dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release   # the IDE
dotnet build BasicLang/BasicLang.csproj -c Release                             # compiler alone
```

The native engine builds only through **VS 2022 MSBuild** on
`VisualGameStudioEngine.vcxproj` (x64/Release), auto-discovered via `vswhere`.

The VS 2022 extension likewise needs VS MSBuild, not `dotnet build`:

```powershell
"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe" `
  BasicLang.VisualStudio/src/BasicLang.VisualStudio/BasicLang.VisualStudio.csproj -p:Configuration=Release
```

> [note] After any `.axaml` change: `dotnet clean` first. Stale Avalonia build cache
> causes crashes that do not look like markup errors.

## Test

```powershell
# full suite — integration tests compile and run native code, spawn clangd and a DAP
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release

# fast subset — skips [Category("Integration")]
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "TestCategory!=Integration"
```

### Expected numbers

| Run | Result | Time |
|---|---|---|
| Full suite at `d2ad064` (= HEAD, **Linux** container) | **6,467 total — 6,069 passed / 195 failed / 203 skipped; 195 is the standing baseline, 0 new** | — |
| Full suite at `ee3c086` (**Windows**, 2026-09-14) | **5,899 total — 5,893 passed / 4 failed / 2 skipped; all 4 baseline** | 55m42s |
| Full suite at `f54416b` (Windows, 134 commits behind HEAD) | 5,826 total, 4 failures — all baseline | ~2h |
| Fast subset at `6139386` (144 commits behind HEAD) | 4,897 passed / 2 failed / 1 skipped — both failures baseline | ~2 min |

**The baseline is platform-specific — read the row that matches your machine.**

**On Windows, four pre-existing failures are baseline and are not yours:** two
`SearchSnippets_*`, `Cli_Build_CppProject_ProjectReference_Warns…`, and `NonEx_variants…`
(which passes alone and fails only when the Native tier runs alongside it). The fast subset
shows the two `SearchSnippets` ones. All four still exist at HEAD.

**On Linux the standing baseline is 195 failures, not 4** — 195 anchored `  Failed ` lines
covering **170 distinct names**, plus 203 skips, and the set is recorded as environmental
rather than as defects: `BasicLang.exe` is a Windows apphost that is never deployed (the 18
`_CSharp` rows going through `CliTestHarness.CompileRunCSharp` are in the set for exactly that
reason), a further set hardcodes `C:\` / PATHEXT / MSVC-vcvars paths, the blnet integration rows
need a win-x64-only Native AOT shim publish, and the native-engine rows hit `DllNotFound`.
Compare **by name** against the 170-name set; a bare count will not tell you whether something
new broke.

> [note] The fast-subset row has not been re-measured since `6139386`, 144 commits back, and
> the suite has grown a lot underneath it — `[Category("Integration")]` went from 143
> attributes to 268 over that span, so the subset now skips a far larger share than it did.
> Treat `~4,897` as a stale floor, not a gate.

> [trap] **The fast subset is not a gate for codegen work.** Execution tests are
> `[Category("Integration")]`. Four fixes once gated green on the fast subset; the first
> full run then found 17 failures and two real regressions that had already been pushed.

> [trap] **The `Msil/` tests need `ilasm`, and a machine without one *skips* them.**
> `MsilHarness` takes `BASICLANG_ILASM` as an explicit override first, then looks for
> `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\ilasm.exe`, then a restored
> `runtime.<rid>.Microsoft.NETCore.ILAsm` package, then `ilasm` on `PATH`. With none of those it
> calls `Assert.Ignore` — so the whole MSIL round-trip tier passes silently by not running.
> Check the skip count, not just `Failed: 0`.

### Test layout

`VisualGameStudio.Tests/` — 474 C# files, ~136K lines:

| Directory | Covers |
|---|---|
| `Compiler/` | Lexer, parser, semantic analysis, IR, optimizer, backends |
| `LSP/` | Language server handlers |
| `Debugger/` | Debug adapter, breakpoints, variable inspection |
| `Editor/` | Folding, highlighting, completion, multi-cursor |
| `Services/` | The ProjectSystem service layer |
| `Shell/` | View models |
| `Dialogs/` | Dialog view models |
| `Core/` | Models and abstractions |
| `Serialization/` | Project and workspace persistence |
| `Blnet/` | The .NET boundary |
| `Msil/` | MSIL round-trip harness: source → `.il` → `ilasm` → a real process → stdout |
| `Native/` | Native compile-and-run tiers |
| `Integration/` | End-to-end: compile, run, spawn real tools |
| `Infrastructure/` | Harness and helpers |
| `TestAssets/` | Fixtures |

Thirty more `.cs` files sit loose at the project root — the Settings dialog/service wiring
suites, the C++ toolchain override and probe tests, and a handful of Shell guards. They belong
to none of the directories above, so a directory-by-directory sweep misses them.

## Reading results correctly

> [trap] **A failure count is anchored log lines, not distinct tests.** At HEAD a Linux run
> reports 195 failures from **170 distinct test names** — a `[TestCase]`-driven test contributes
> one line per case. Compare failure *names* against the baseline set: equal counts can hide a
> swap, and a changed count can be nothing but a newly added `[TestCase]`.

> [trap] **A "Passed!" summary line does not mean the suite passed.** A crashed test host
> still prints a per-assembly summary; the abort goes to **stderr**. Capture both streams
> and check the total against the expected count, not just `Failed: 0`.

> [trap] **Build contention looks exactly like a test failure.** A native test failing with
> `C++ compile timed out after 240s` is a load artifact. Measured: one such test "failed"
> after 8m49s in a full run and passed alone in 34s. **Re-run in isolation before
> investigating.** Only an assertion failure is evidence.

> [trap] **Green is not the same as proven.** Tests in this repo have passed for structural
> reasons — an assertion comparing a constant with itself cannot fail even if the code it
> claims to cover is deleted. When a test matters, check that it *can* fail: break the
> production code deliberately and watch it go red.

## Validating a codegen change

Four checks, and the first two are not optional:

1. **Run it through the optimizer.** Every shipping route optimises; the plain unit-test
   helper does not. Use the CLI, or `CompileToCppOptimized` in `CppCollectionTests.cs`.
2. **Run it through both entry points.** The IDE build delegates to the CLI engine
   (`CompileProjectFiles`). A fix verified through only one can break the other.
3. **Use stdout as the oracle.** Not the shape of the generated code — what the program
   actually prints.

4. **Run it on every backend when the change is semantic.**
   `FourBackends.RunsOnEveryBackend` (`VisualGameStudio.Tests/Compiler/FourBackends.cs`)
   compiles and runs one program through C++, JavaScript, MSIL and C# in process and asserts
   they agree. The property it pins is "the backends agree", not "one backend prints the
   number". A fixture using it must be `[NonParallelizable]` — the C# leg redirects
   `Console.Out` — and its C# leg is in-process Roslyn, not `CliTestHarness.CompileRunCSharp`,
   which spawns `BasicLang.exe` and therefore fails on Linux.

## Code coverage

`docs/articles/code-coverage.md` documents the coverage setup and reporting.
