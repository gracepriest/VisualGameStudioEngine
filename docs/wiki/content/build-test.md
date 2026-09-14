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
| Full suite at `f54416b` (Windows) | **5,826 total, 4 failures — all baseline** | ~2h |
| Fast subset | ~4,897 passed, 2 failures (both baseline) | ~2 min |

**Four pre-existing failures are baseline and are not yours:** two `SearchSnippets_*`,
`Cli_Build_CppProject_ProjectReference_Warns…`, and `NonEx_variants…` (which passes alone
and fails only when the Native tier runs alongside it). The fast subset shows the two
`SearchSnippets` ones.

> [trap] **The fast subset is not a gate for codegen work.** Execution tests are
> `[Category("Integration")]`. Four fixes once gated green on the fast subset; the first
> full run then found 17 failures and two real regressions that had already been pushed.

### Test layout

`VisualGameStudio.Tests/` — 435 files, ~119K lines:

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
| `Native/` | Native compile-and-run tiers |
| `Integration/` | End-to-end: compile, run, spawn real tools |
| `Infrastructure/` | Harness and helpers |
| `TestAssets/` | Fixtures |

## Reading results correctly

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

Three checks, and the first two are not optional:

1. **Run it through the optimizer.** Every shipping route optimises; the plain unit-test
   helper does not. Use the CLI, or `CompileToCppOptimized` in `CppCollectionTests.cs`.
2. **Run it through both entry points.** The IDE build delegates to the CLI engine
   (`CompileProjectFiles`). A fix verified through only one can break the other.
3. **Use stdout as the oracle.** Not the shape of the generated code — what the program
   actually prints.

## Code coverage

`docs/articles/code-coverage.md` documents the coverage setup and reporting.
