title: Conventions and traps
lede: Short, and load-bearing. Every item here shipped a green build that did the wrong thing.
---
## Shell and file handling

- **PowerShell is the primary shell.** Use the dedicated file tools rather than reflexive
  `grep` / `cat` / `find` / `sed`; a PreToolUse hook
  (`.claude/hooks/prefer-native-tools.js`) blocks them through Bash.
- **Never round-trip repo files through PowerShell `Get-Content` / `Set-Content`.** It
  corrupts the BOM-less UTF-8 files here and has caused mojibake more than once. For a
  multi-line git commit message, write a file and use `git commit -F`.
- **PowerShell 5.1 reports a native command's stderr as failure.** Verify a `git push` by
  comparing SHAs, never by exit code.

## Build

- **After AXAML changes, `dotnet clean` before building.** Stale build cache causes
  crashes that look nothing like markup errors.
- **`VisualGameStudio.Shell` is the IDE.** `VisualGameStudio.Editor` is a library.
- **The native engine and the VSIX need VS 2022 MSBuild**, not `dotnet build`.

## Codegen

> [trap] **A missing switch arm does not fail — it silently builds C#.** Four separate
> backend-dispatch maps have defaulted to C#. The most recent,
> `ProjectTemplateService.GenerateProjectFileContent`, would have written
> `<TargetBackend>CSharp</TargetBackend>` for a JavaScript project. Its default now throws
> and `ProjectTemplateBackendMappingTests` pins every id in `SolutionTypes.All`.
> **When you add a backend or solution type, grep for every map keyed on it.**

> [trap] **The C# backend reconstructs control flow by matching block NAMES, not graph
> shape.** `IsIfThenElse` wants `.then` / `.else` and looks up `{prefix}.end`. A CFG named
> anything else falls to a path that emits both arms and **never the merge** — the program
> prints its first operand and stops. Name new CFGs exactly like `Visit(IfStatementNode)`
> does. The JavaScript backend differs again (it derives the merge via `FindMergeBlock`, so
> the true target must never *be* the merge); C++ is goto-based and tolerates any shape.

> [trap] **Every shipping route runs the IR optimizer; the unit-test helper does not.** A
> fixture can be green while the CLI and the IDE both miscompile. Validate through the CLI
> or an optimizer-running helper (`CompileToCppOptimized` in `CppCollectionTests.cs`).
> **stdout is the only valid oracle.**

- **Test both entry points.** The IDE build delegates to the CLI engine
  (`CompileProjectFiles`); a fix verified only through the test helper can still break via
  the IDE or the CLI.

## Shared source

Some source backs more than one consumer. **Change it once, not per consumer:**

- `ModuleResolver.cs` backs both the compiler and the LSP.
- `ModuleTypeWalker.cs` is shared across the compiler, the C++ backend and the capability
  checkers.

## The deployed drop

> [trap] **`IDE/` is a hand-committed xcopy drop and goes stale.** A stale drop has been
> mistaken for a code bug more than once. Deploy with `robocopy <Shell bin> IDE /E` —
> **never `/MIR`**, since the engine DLL and import lib live only there.
> **`IDE/lib/js/dom-core.bli` is load-bearing**: the deployed compiler auto-includes it for
> every JavaScript build, and without it the typed DOM does not resolve. Verify a refresh
> against the deployed files (`IDE/BasicLang.exe new --list`), never against timestamps.

## Reading test results

> [trap] **A "Passed!" summary line does not mean the suite passed.** A crashed test host
> still prints a per-assembly summary; the abort goes to **stderr**. Capture both streams
> and check the total against the expected count, not just `Failed: 0`.

> [trap] **Build contention looks exactly like a test failure.** A native test failing with
> `C++ compile timed out after 240s` is a load artifact — one such test "failed" after
> 8m49s in a loaded full run and passed alone in 34s. Re-run in isolation first; only an
> assertion failure is evidence.

## The engine boundary

- **Every `__declspec(dllexport)` in `framework.h` needs a matching `<DllImport>` in
  `RaylibWrapper.vb`** — `extern "C"`, `__cdecl`, `LPStr` string marshaling. The export
  count is in the thousands and drifts: **grep to confirm, never trust a cached number.**

## Diagnostics

- A diagnostic's message text must never name a *different* code than the one it carries.
  Users seeing `BL6001` over text naming another code is the exact confusion this rule
  prevents.
- A backend that cannot express something must produce a diagnostic, **never
  plausible-looking wrong code**. That is what `CppCapabilityChecker` and
  `JsCapabilityChecker` are for.

## Documentation discipline

`CLAUDE.md` is an operating guide, **not a changelog**. History belongs in `git log`,
rationale in `docs/superpowers/{plans,specs}/`, current status in
[`docs/HANDOFF.md`](#/roadmap). Do not append dated bug-fix logs to the operating guide —
it stops being readable and people stop reading it.

Per-area guides exist for subagents and are worth reading before working in an area:
`BasicLangAgent/CLAUDE.md`, `IDEAgent/CLAUDE.md`, `EngineAgent/CLAUDE.md`,
`VSExtensionAgent/CLAUDE.md`.

## Scope decisions already made

- **MSIL and LLVM are out of scope.** Do not test, fix, or file bugs on them.
- **COM interop is ruled out.**
