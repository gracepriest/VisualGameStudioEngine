# The portable control library (piece 2) Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One form's code-behind builds for WinForms and for the web. On the web, WinForms' API (`Button`, `.Text`, `.Enabled`, `AddHandler …Click`, `(sender As Object, e As EventArgs)`) is a library of real BasicLang classes, auto-included in web builds, that read and write the live page. Existing web forms keep building unchanged until the user accepts a conversion. First, the compiler learns what the library and the shared code shape need: `#If`/`#ElseIf`/`#Else`/`#End If` with `WEB`/`DESKTOP`/`DEBUG`/`RELEASE` on every build route and in the editor; cross-file `Inherits`; five JavaScript-backend fixes; `Char` and exact `Decimal` on JavaScript; user `Enum` typing; and an `AddHandler` signature check.
**Architecture:** Sub-piece 2.0a fixes the compiler and editor, one measured defect per task. 2.0b adds an exact `Decimal` runtime to the JavaScript backend, with every behaviour checked against .NET running the same program. 2a builds the library core, the portable code-generation style, the first seven control kinds, the rendering fixes and live docking, and ships them behind an opt-in "Add Web Form (portable)" command. 2b adds the remaining kinds and flips the default. 2c converts old web forms on the user's say-so. 2d badges desktop-only controls and stops a web build that contains one. Every mirrored pair has a lock-step test. Edge headless is the page oracle, the real WinForms window is the behaviour oracle, and node runs everything else.
**Tech Stack:** C# / .NET 8, NUnit, BasicLang compiler (C#, C++, JavaScript backends), node (JS execution tier), Roslyn in-process C# runs (`FourBackends.RunEmittedCSharp`), csc via the real CLI (WinForms), Microsoft Edge headless over loopback (Windows), Avalonia 11.3.13 + Avalonia.Headless (Skia) for IDE views.
---

## How to read this plan

**Granularity decision (the plan owner's; stated so nobody re-litigates it).**

- **2.0a (Tasks 1–17) and 2.0b (Tasks 18–27): full TDD step granularity.** Each task has exact files, anchors verified on today's `origin/master`, complete failing test code, the command and the expected red, the implementation code (or precise quoted-anchor edits where the change is too large to paste), the expected green, the mutation(s) that must go red, and a commit step.
- **2a, 2b, 2c, 2d (Tasks 28–47): TASK granularity.** Files and responsibilities, the tests each must add, risks, gate. **Each is expanded into full steps just before it starts, after a pre-flight against the tree as the earlier tasks leave it** — exactly as piece 1's Tasks 9–16 were. Their anchors WILL drift: 2.0 rewrites the preprocessor, the analyzer's base resolution, the JS event lowering and the prelude; property-grid slice 3 (in flight on `feat/property-grid-slice3`) rewrites catalog rows, `FormCss` and the root rows; `fix/unknown-dock-diagnostic` (merged, `bc29391e`) added `FormDock` and BL8033.

Every `file:line` anchor below was verified by reading the file on **`origin/master` @ `bc29391e`** (2026-09-29), except where marked **[X]**: those are on **`098b4de9`** (`fix/js-cross-file-calls` head at revision 2 of this plan — `40e9c172` plus review fixes and a master merge; not merged to master), and **[S3]**: on **`e90a24bb`** (`feat/property-grid-slice3` head). ⚠ Master has since moved to **`5b4ca51e`** (#145 rewrote ~230 lines of `IROptimizer.cs` and moved the JS roster pin) — Task 0 re-anchors. **Re-verify each anchor before editing; if a line has moved, find the code by its quoted text, never by the number.**

**Revision 2 (plan review of `e93985d5`)** changed: the Decimal oracle rule and a new Task 20A that fixes the C# backend where it diverges from VB (review CRITICAL); Task 10's library-shape tests; Task 15 no longer re-marks `Using` (guard rows added); .NET's Decimal↔Double algorithms and generated tables; project-route Decimal rows; Tasks 30/40 anchored on slice 3's handler rework; a new Task 35A (automated headless end-to-end); the O12 methods and collection members assigned to tasks with a manifest-driven gate; Task 9's C++/optimizer legs and concrete edits; and every minor.

Spec: `docs/superpowers/specs/2026-09-29-portable-control-library-design.md` (cited "spec §N"; owner decisions O1–O19 are §0.2–§0.3; measured facts M1–M25 are §3; the final review's notes are §11b).

### ⛔ Dependency: 2.0 starts only after `fix/js-cross-file-calls` lands
✅ **Landed** — PR #146, merged at `6fbde6a5` (final head `214af384`). Confirmed and re-anchored in the **Task 0 RECORD**, whose corrections win over the anchors below.
That branch (`40e9c172` + review fixes `3073435a`, head `098b4de9`) adds `ResolveTypeSymbol`, `TryResolveOtherUnitModuleBlockMember`, `LacksDeclaredMember`, `RecordDeclaredMembers`, `TypeInfo.DeclaredMemberNames`/`DeclaredBaseName` (`SymbolTable.cs:120`, `:123` [X]), sibling class shells that get their `BaseType` (pass 2, `SemanticAnalyzer.cs:≈604-630` [X]) and `CrossFileBindingTests` [X]. It rewrites the missing-member / .NET-fallback path that Tasks 7, 11 and 15 build on. **Task 0 confirms it is merged and re-measures.** If it is still unmerged, stop and report; do not rebase this work onto an unmerged branch.

### How to build and run tests (used by every task)

PowerShell, from the worktree root. `$sp` is your session scratchpad directory.

```powershell
dotnet build VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release
cmd /c "dotnet test VisualGameStudio.Tests\VisualGameStudio.Tests.csproj -c Release --no-build --filter `"FullyQualifiedName~X`" > `"$sp\run.txt`" 2>&1"
```

Then Read `$sp\run.txt`.
- ⛔ Both streams are captured (`2>&1` inside `cmd`, never on a native exe in PowerShell 5.1).
- ⛔ A "Passed!" line is not the verdict. Read the counts and the failure NAMES.
- ⛔ A passing test prints NOTHING at normal verbosity. To see that a named test RAN, re-run it alone with `--no-build --filter` and check the count is non-zero.
- ⛔ The Bash tool is banned (a hook blocks it, and it opens wsl.exe). Use Read/Grep/Glob/Edit/Write, and PowerShell only for build/test/git/node.
- ⛔ A number without its base is meaningless: every count you record says which commit it was measured on.

### Commits (every task)

Write the message with the Write tool to `$sp\taskN-commit.txt`, ending with:

```
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

Stage **by name** (`git add <each file>`, never `git add -A`, never `csc.dll`), run `git status`, then `git commit -F "$sp\taskN-commit.txt"`. Never round-trip a repo file through `Get-Content`/`Set-Content`. No push until the coordinator says so.

### Diagnostics are named, numbered late
Design codes are referred to by NAME (`DesktopOnlyKind`, `DomUsageFinding`, `MixedCodeStyle`). The number is assigned **in the task that adds the code**, from `BasicLang/Forms/DesignDiagnostic.cs` on master that day (BL8033 = unknown Dock, taken by `ac98aba5`; property-grid slice 3 takes the next). Compiler diagnostics keep their message family and get a name here (`HandlerSignatureMismatch`, `DecimalNotSupportedOnWeb`, `WebUnavailableMember`, …); a BL code is allocated in the task from the range the neighbouring codes use.

### Scope calls made while anchoring (deviations from the spec's wording; each is deliberate)

| # | Spec says | This plan does | Why |
|---|---|---|---|
| S1 | §4.1 "the lexer's `PreprocessorIf` token, `PreprocessorIfNode` and its four visitors … are deleted" | **Delete** `PreprocessorIfNode`, `PreprocessorElseIfClause`, the `IASTVisitor` member and its **three** implementations (`ASTPrettyPrinter.cs:1174`, `IRBuilder.cs:3240`, `SemanticAnalyzer.cs:9064`). **Keep** the lexer's `PreprocessorIf`/`ElseIf`/`Else`/`EndIf` tokens. | There are three visitors, not four. The tokens are reachable: the lexer runs on raw text (a directive the preprocessor did not consume, the IDE's own tooling), and `LexerTests.Lexer_PreprocessorIf` (`LexerTests.cs:460-466`) pins them. The node is unreachable: `Parser.cs` never constructs it (grep). |
| S2 | §4.1 "Symbols: `WEB` when the backend is JavaScript …, `DEBUG`/`RELEASE` by configuration" | One pure function, `BuildSymbols.For(targetBackend, configuration, defineConstants)`, called by the `BasicCompiler` constructor from two new `CompilerOptions` fields (`Configuration`, `DefineConstants`). A null configuration defines neither `DEBUG` nor `RELEASE`. The CLI single-file route passes `"Debug"`; the CLI project route and the IDE pass the build's configuration; `DebugSession` passes `"Debug"`. | One place defines the symbols (spec §4.1 "Defined in ONE place"). Every route already constructs a `BasicCompiler` from a `CompilerOptions`, so the options carry them and no route can forget. API callers (most tests) get only `WEB`/`DESKTOP`, which is what they had plus a harmless target symbol. |
| S3 | §4.12 "the LSP runs the same preprocessor … and replaces every inactive-branch line with an EMPTY line" | `Preprocessor` gains an editor mode (`ProcessForEditor`) — the SAME directive walker, with directive lines and inactive lines written as empty lines and `#Include` never spliced. Both LSP parse sites call it (`DocumentManager.cs:518`, `LspProjectContext.cs:445`). | Two directive walkers would be a mirrored pair. The compiler already comments out skipped lines (`' [IFDEF SKIP] …`, `Preprocessor.cs:306`), which also preserves lines; the editor mode differs only in what it writes. |
| S4 | §4.5 "`MyBase.P` in a property getter/setter lowers to `super.P`" (JavaScript) | Fixed on **all three shipping backends**: `IRFieldAccess`/`IRFieldStore` gain `ThroughBase`; JS renders `super.P`, C# `base.P`, C++ the base-qualified accessor. | Measured today: the C# backend emits `this.Text` inside `public override string Text` too (`p2probe\_run_master\a\p.cs`), which is a stack overflow at run time. The IR drops the MyBase marker (`IRBuilder.cs:2409-2425`), so the fix is in the IR and every backend. |
| S5 | §4.6 "Base-first class emission (JavaScript)" as a 2.0 task | **Already on master** (`27e8307c`, `IRModule.ClassesBaseFirst`, `IRNodes.cs:1681`, used at `JavaScriptBackend.cs:209`, `CppCodeGenerator.cs:191`, `CppCodeGenerator.Split.cs:273`). Re-measured: M5 runs (`Form1 Form`). This plan only adds the CROSS-FILE order case to Task 7. | Measured on `bc29391e`. |
| S6 | §4.4 "the event store is a list of `{receiver, method}` entries" | Events become JS ARRAYS; `AddressOf recv.M` produces a function tagged with `__blTarget`/`__blMethod`; `RemoveHandler` removes the LAST matching entry; `RaiseEvent` iterates a SNAPSHOT. | .NET semantics: an invocation list, duplicates allowed, remove takes the last match, and a raise invokes the list as it was when the raise began. A `Set` dedupes a delegate stored in a variable and added twice. |
| S7 | §4.8 "`AddHandler` checks the handler against a BasicLang event" | The check already exists (`ValidateHandlerWiring`, `SemanticAnalyzer.cs:8579-8632`) and is silent because (a) events are never class MEMBERS (`Visit(ClassNode)` member loop `:6146-6200` and `PopulateClassMemberSignatures` `:744-820` skip them), and (b) a `Private` handler declared below its wiring is unbound when the wiring is analyzed. Task 10 makes events members and DEFERS an unresolved handler's check to the end of its class. The message family stays (`the handler passed to AddHandler takes …`). | Fix the reason it is silent, not a second check. |
| S8 | §4.9 "On a JavaScript build, a Using … that names a namespace DECLARED IN THE PROGRAM resolves to it" | **A `Using` is never re-marked** (revised on plan review): the library declares `Namespace System` (for `EventArgs`), so re-marking would make `Using System` stop counting as .NET and break `Console`/`Math`. Instead a bare name already resolves to a program type first (scope before `IsNetType`), and Task 15 adds only (a) dotted `Namespace` declarations and (b) a QUALIFIED name whose prefix is a program namespace resolving to the program's type when that type exists (else the existing .NET path, unchanged). The analyzer learns the target through `SemanticAnalyzer.ConfigureTarget(backend)` (beside `ConfigureModuleSystem`, `Compiler.cs:750-751`, and in `JsTestSupport`); (b) is gated on JavaScript. | The analyzer has no target today (only `_netNativeBackend`, `:390`). C# must stay byte-identical (spec §4.9). M8's failures were the parse error, the qualified name and the upcast that followed from it — not the bare name. |
| S9 | §4.11 "D21 … it must fold EXACTLY … or not at all" | Measured: `IROptimizer` does not fold Decimal at all (`FoldAdd`/`Sub`/`Mul`/`Div` `:1829-1872` match int/long/float/double/string; `TryFoldCompare` skips decimal at `:1802`). Task 26 PINS "not folded through double" under standard and aggressive pipelines and adds no folding. | "Or not at all" is today's behaviour; adding a System.Decimal folder is optional work with its own risk and no user need. |
| S10 | §4.11 "expected values COMPUTED BY .NET inside the test" | **The oracle rule (revised on plan review):** BasicLang follows VB. The C# backend's run is the oracle **only for rows where the C# emission's Decimal semantics EQUAL VB's** — arithmetic, comparison of typed Decimals, printing, `Math`, `Decimal.Parse`, Decimal↔Double, widening. **Exception rows use VB-LITERAL expectations written in the test**, derived from VB/.NET semantics, and Task 20A FIXES the C# backend so it agrees: (E1) Decimal → integral narrowing by `CType` or implicitly — VB rounds half-to-even and throws `OverflowException` out of range; the C# backend emits `(int)(d)` = truncate (`CSharpBackend.EmitCastText`, ≈4467-4498 on `5b4ca51e`: only Double/Single round); (E2) `o = p` on boxed Decimals — VB compares VALUES (spec D19, owner-approved — **the spec stands; never "correct" it**); C# compares references; (E3) `CType(o, Decimal)` of a boxed Integer/Double — VB converts; C#'s unbox throws `InvalidCastException`; (E4) `CShort`/`CByte` of an out-of-range Decimal — `OverflowException`. Every other row: the oracle, plus a few literal expectations (`1.10`, `0.3333333333333333333333333333`) against both being wrong alike. | A hand table for EVERY row would be a second Decimal implementation to get wrong; but the C# backend is a BasicLang backend, not VB, and where it diverges it is the thing to fix, never the thing to copy. |
| S11 | §4.2 "cross-file `Inherits` … Tests (JS + C#, CLI + IDE)" | Tests go INTO `CrossFileBindingTests` (Edit), reusing its `RunsOnEveryBackend(expected, files…)` (both compile orders; C#, JS, C++). The CLI leg reuses its `TheCli_BuildsAndRunsAJavaScriptProject_ThatCrossesFiles` shape. | One harness for cross-file behaviour; a second fixture would copy its helpers. |
| S12 | (not in spec) chip `task_e7af351e` | Folded: item 1 (cross-file `Inherits` refused) → Task 7; item 2 (derived-before-base emission) → already fixed on master (S5), Task 7 adds the cross-file order test; items 3 (C# CS0101: a static class named like the FILE collides with a user class of that name when the class holds a lambda) and 4 (C# CS0542: a Sub named like its file) → Task 8. | Both C# items hit the shared form shape: a form class lives in a file of its own name (`LoginForm.bas` / `Class LoginForm`) and handlers use lambdas. |

**No question here is architecture-altitude** beyond what the spec settled (and the spec was owner-approved). `docs/superpowers/decisions/` holds ADR-0016 (MyBase.New as an instruction) — Task 13 must not disturb `IRBaseConstructorCall`; nothing else there bears on this plan.

### Spec claims found FALSE or incomplete while anchoring (recorded, not worked around silently)

1. **§4.6 base-first emission is already done** (S5).
2. **§4.4 "`AddressOf recv.M` yields the SAME delegate identity" is necessary but not sufficient**: a `Set` also dedupes a delegate added twice through a variable (S6).
3. **§4.5 is not JavaScript-only**: C# recurses too (S4).
4. **§4.8's check exists** and is silent for two structural reasons (S7).
5. **§4.11 D21 "the optimizer already folds Decimal constants"**: false — the C# probe's `s = 0.1m + 0.2m` is copy-propagated, not folded (S9).
6. **§4.11 D1 "an untyped numeric literal in a Decimal context"**: true today (`TryRetypeLiteralToDecimal`, `SemanticAnalyzer.cs:2189-2210`); the `D` suffix is what is missing — `ScanNumber` (`BasicLangLexer.cs:1051-1132`) knows only `F` and `L`, so `0.1D` lexes as `0.1` then identifier `D`.
7. **§4.11 D15 "`CDec(s)`"**: `CDec` is not registered at all (conversion functions at `SemanticAnalyzer.cs:1557-1602` stop at CBool; `CSharpBackend.VbConversionText` `:240-275` has no arm), so it falls to the permissive path and types as `Object` (M20).
8. **K5 "a `#Define` line is not echoed"** — also: `#Define` and `#Include` are NOT gated on the active branch (`Preprocessor.cs:221`, `:235`), and the recursive `Process` for an include clears the PARENT's errors and conditional stack (`:204-205`, called at `:387`). Task 2 fixes all three; the last would break `#If … #Include … #End If`.
9. **M7's cause** is not the analyzer's `_netNamespaces` but the IR builder's call routing: `IsKnownNetStaticType` answers TRUE for any PascalCase name once the unit has a .NET `Using` (`IRBuilder.cs:6636-6656`), and `Me` is PascalCase (`IRBuilder.cs:5914`, `:5941`). It is reproducible ONLY through the project route (`CurrentUnit` is set by `ConfigureModuleSystem`); `JsTestSupport` never sets it, so a test through that helper passes with the defect present.
10. **The JS execution roster is 94 on `bc29391e`** (`JsExecutionTierRosterTests.cs:470-471`), and `RosterCoversEveryJavaScriptIntegrationFixture` auto-discovers any Integration fixture in `VisualGameStudio.Tests.Compiler` whose name starts with `JavaScript`/`Js` or ends with `ExecutionTests`. Every such fixture this plan adds goes into `ExecutionTier` and bumps the pin in the same commit (read the pin first — it moves on every merge).
11. **The LSP has no notion of the project's backend** (`LspProjectContext` holds `ProjectFilePath`, `SourceFiles`, `Symbols`, `Stamp` — `LspProjectContext.cs:19-66`). Task 6 adds `TargetBackend`, read from the same `ProjectFile.Load` (`:382`).
12. **`JavaScriptInteropTests.cs:55-58`** says "`#If` is a LEXER/parser construct and never reaches the directive collector". True today; false after Task 1. Its comment is corrected in Task 1 (the test itself stays).

---

## Task 0: Pre-flight, baseline and re-measurement

**Files:** none modified (the probe script lives in `$sp`).

- [x] **Step 1: Confirm the dependency landed.**

```powershell
git -C C:\Users\melvi\source\repos\VisualGameStudioEngine fetch origin
git -C C:\Users\melvi\source\repos\VisualGameStudioEngine merge-base --is-ancestor 40e9c172 origin/master; "landed=$LASTEXITCODE"
```
`landed=0` is required. Anything else: stop and report.

- [x] **Step 2: Bring this branch up to master — trial first.** `git merge-tree` is NOT a conflict check here (CLAUDE.md). Trial in a detached worktree: `git worktree add --detach "$sp\wt-trial" HEAD`, `git -C "$sp\wt-trial" merge origin/master`, read the result, remove the worktree. Then do the real merge on `feat/portable-controls` (message file, `git commit -F`).

- [x] **Step 3: Build and record the baseline.**

```powershell
dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release
dotnet build VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release
cmd /c "dotnet test VisualGameStudio.Tests\VisualGameStudio.Tests.csproj -c Release --no-build --filter TestCategory!=Integration > `"$sp\baseline-fast.txt`" 2>&1"
cmd /c "dotnet test VisualGameStudio.Tests\VisualGameStudio.Tests.csproj -c Release --no-build --filter `"FullyQualifiedName~CrossFileBindingTests|FullyQualifiedName~ForwardDeclaredType|FullyQualifiedName~JavaScriptEventTests|FullyQualifiedName~JavaScriptInteropTests|FullyQualifiedName~CppPassthroughTests|FullyQualifiedName~JsExecutionTierRosterTests|FullyQualifiedName~OverridablePropertyTests|FullyQualifiedName~InheritedMemberTests|FullyQualifiedName~JavaScriptProjectBuildTests|FullyQualifiedName~CrossFileAnalysisTests`" > `"$sp\baseline-int.txt`" 2>&1"
```
Record for both, **with the base SHA**: total / passed / failed / skipped and the SORTED failure NAMES. Known fast-subset machine failures: `Emit_ReplacesAnImportedModuleThatAnotherHandleHasMapped`, `Emit_ReplacesAScriptThatAnotherHandleHasMapped`, `Emit_ReplacingAnImportedModule_LeavesNoTempFileBehind` (intermittent), `SearchSnippets_EmptyQuery_ReturnsAll`, `SearchSnippets_WhitespaceQuery_ReturnsAll`. Any other name is new at this base and goes into the record.

- [x] **Step 4: Re-measure the spec's rows on the merged tree.** Copy the probe programs of spec §3 into `$sp\p2probe\` (their sources are in the Appendix of this plan) and run each with the freshly built `BasicLang\bin\Release\net8.0\BasicLang.exe`: single files with `--target=javascript` then `node`, projects with `build Site.blproj`. Record, per row, "still reproduces" or "fixed by <sha>":

| Row | Probe | Measured on `bc29391e` (this plan) | Task |
|---|---|---|---|
| M2 RemoveHandler no-op | `d` | reproduces (`clicked 1..4`) | 12 |
| M4 MyBase.Property recursion | `a` | reproduces (JS `RangeError`; C# emits `this.Text`) | 13 |
| M5 derived above base | `h` | **fixed** (`27e8307c`) | — (S5) |
| M6 cross-file Inherits | `e2`, `e3`, `c3` | reproduces on JS and C#, and on `40e9c172` too | 7 |
| M7 `Using` + `Me.M()` | `v1`, `v2`, `u1` | reproduces, and on `40e9c172` too | 11 |
| M8 dotted Namespace | `b`, `b2`, `c` | reproduces | 15 |
| M9 `#If` | `i` | reproduces | 1 |
| M12 Enum member typing | `g`, `f2` | reproduces | 9 |
| M13 handler signature | `w1` | reproduces | 10 |
| M16 class named `F` | `v3` renamed back to `F` | re-measure (40e9c172 claims a fix) | none (record) |
| M18 Char | `j4` | reproduces (BL7004) | 14 |
| M20 Decimal | `j3` | reproduces (BL7007) | 18–26 |
| M22 unqualified self-call | `j5` | works | — |
| M23 lambda `Me.` | `j1d` | works | 16 (pinned) |
| M24 `AddressOf Me.X` above | `j1a` | works | 16 (pinned) |
| M25 `AddressOf Me.X` below | `j1c` | refused (the erasure) | 16 (pinned) |

A row that no longer reproduces: its task is reduced to its regression test (write the test, see it GREEN, commit it with "already fixed by <sha>"). Never delete a task silently.

- [x] **Step 5: Re-verify every anchor Tasks 1–27 cite** (search by the quoted code). Note any move in the task's first checkbox before starting it.

### Task 0 RECORD — pre-flight on the merged tree (2026-09-30) · ⛔ these corrections WIN over the task text below

**Base of every number here: `feat/portable-controls` @ `3c1713c8`** = the plan branch (docs only) merged with `origin/master` **`6fbde6a5`** (PR #146 `fix/js-cross-file-calls`, final head `214af384` — which had already merged `5b4ca51e`: #145 emission order, #163 DCE temps; plus #143 selection store, #144 Dock). `git diff 214af384 6fbde6a5` is empty.

- [x] **Step 1 — the dependency landed.** `merge-base --is-ancestor` exit 0 for `40e9c172`, `098b4de9`, `214af384`, `5b4ca51e` against `origin/master` `6fbde6a5`. PR #146's later commits (`3073435a` review fixes — extension-method tolerance in `LacksDeclaredMember`, `Me.New` / file-level `Const` messages, CLAUDE.md rewrites — and the `5b4ca51e` merge) are all in.
- [x] **Step 2 — merged.** Trial merge of `03b13887` + `origin/master` in `git worktree add --detach`: clean. Real merge `3c1713c8`: clean, no conflicts (the branch adds docs only).
- [x] **Step 3 — baseline** (Shell and Tests built at project level, 0 errors).
  - **Fast subset** (`TestCategory!=Integration`) on `3c1713c8`: **Total 9474 · Passed 9447 · Failed 8 · Skipped 19** (6 m 33 s). Sorted failure names:
    `Emit_ReplacesAScriptThatAnotherHandleHasMapped` · `Emit_ReplacesAnImportedModuleThatAnotherHandleHasMapped` · `Emit_ReplacingAnImportedModule_LeavesNoTempFileBehind` · `EveryTextRoute_UsesTheFormatter_NeverToStringOrABareCout` · `Resolve_AgainstTheRealBasicLangServer_FillsLazyDocs` · `Save_CalledTwice_DoesNotWriteThePreEditDocumentBack` · `SearchSnippets_EmptyQuery_ReturnsAll` · `SearchSnippets_WhitespaceQuery_ReturnsAll`.
    Six are the known machine rows (`ReadingAnMvidTakesNoLockOnTheFile`, also known, passed this time). The two others were environmental under load — the probe MSVC builds ran concurrently: `Resolve_AgainstTheRealBasicLangServer_FillsLazyDocs` = "Initialize timed out after 10 seconds" (the `--lsp` server never connected), `Save_CalledTwice_DoesNotWriteThePreEditDocumentBack` = `UnauthorizedAccessException` in `File.Move` (`XmlTextIO.SaveIfChanged`). **Re-run alone with `--no-build --filter`: 2/2 passed.** Neither is a product regression; treat both as intermittent machine rows when comparing later runs.
  - **Named Integration set** (the Step 3 filter) on `3c1713c8`: **Total 262 · Passed 262 · Failed 0 · Skipped 0** (22 m 27 s — MSVC compiles dominate). No failure names.

- [x] **Step 4 — re-measure** (fresh `BasicLang\bin\Release\net8.0\BasicLang.exe` of `3c1713c8`; probes copied to `$sp\p2probe\_run_merged\`; single files `--target=javascript` + node with the stub prelude, projects `build Site.blproj`; C# emission read from `--target=csharp`; C# / C++ projects built and their `Site.exe` RUN — the C++ route compiles with MSVC):

| Row | Probe | On `6fbde6a5` | Task |
|---|---|---|---|
| M2 RemoveHandler no-op | `d` | **reproduces** — `clicked 1..4` | 12 |
| M4 MyBase.Property recursion | `a` | **reproduces** — JS `RangeError: Maximum call stack size exceeded` at `this.Text = value`; C# emits `this.Text`, never `base.Text` | 13 |
| M5 derived above base | `h` | fixed (runs: `Form1 Form`) | — (S5) |
| M6 cross-file Inherits | `e2` (JS), `e3` (C#), **`e3cpp` (C++, new)**, `c3` (JS) | **reproduces on all THREE backends** — `Unknown base class 'Base'` + `MyBase can only be used in a class that inherits…` + `Cannot assign value of type 'D' to variable of type 'Base'` (C++ as `BL3001`); `c3`: `Unknown base class 'Form'`. `e1` (cross-file `New`) runs `base`. | 7 |
| M7 `Using` + `Me.M()` | `v1`, `v2`, `u1` | **reproduces** — `no lowering for 'Me.Init'` / `'Me.InitializeComponent'`; `v3` (no `Using`) runs `init` | 11 |
| M8 dotted Namespace | `b`, `b2`, `c` | **reproduces** — `b`: `Unexpected token at top level: '.'`; `b2`: `Cannot assign … 'System.Windows.Forms.Button' to … 'System.Windows.Forms.Control'`; `c` (project): `Cannot assign … 'Button' to … 'Control'` | 15 |
| M9 `#If` | `i` | **reproduces** — `#Else without matching #IfDef or #IfNDef` | 1 |
| M12 Enum member typing | `g`, `f2` | **reproduces** — `Cannot assign value of type 'Object' to variable of type 'Shade'` / `'MyKeys'` | 9 |
| M13 handler signature | `w1` | **reproduces** — builds silently, runs `clicked 1..4` | 10 |
| M16 class named `F` | `vF` (= `v3` with the class renamed `F`) | **FIXED on JS and C#** — JS runs `init`; C# project builds and runs `init`. (Claimed by `40e9c172`; not bisected.) The C++ project fails, but NOT because of the name — see C6. | none — chip `task_ef845b99` can be closed for JS/C# |
| M18 Char | `j4` | **reproduces** — `BL7004` | 14 |
| M20 Decimal | `j3` | **reproduces** — `BL7007` | 18–26 |
| M21 Optional | `j2` | works (`x3`, `y3`, `z7`) | — |
| M22 unqualified self-call | `j5` | works (`unqualified ok`) | — |
| M23 lambda `Me.` | `j1d` | works (`hit`) | 16 (pinned) |
| M24 `AddressOf Me.X` above | `j1a` | works (`above via Me 1`) | 16 (pinned) |
| M25 `AddressOf Me.X` below | `j1c` | refused, same message (`Argument 2: cannot convert from 'Pointer To Pointer To Object' to 'Action<DomEvent>'`) | 16 (pinned) |

  **No task is made unnecessary by master**: every row a task owns still reproduces. The only row that moved is M16, which no task owned.

- [x] **Step 5 — anchor drift.** Only 8 files cited by Tasks 1–27 changed between the anchor bases and `6fbde6a5`: `IRBuilder.cs`, `IRNodes.cs`, `IROptimizer.cs`, `SemanticAnalyzer.cs`, `SymbolTable.cs` (BasicLang) and `CrossFileBindingTests.cs`, `InheritedMemberTests.cs`, `JsExecutionTierRosterTests.cs` (tests). **Every anchor in any other file (`CSharpBackend.cs`, `JavaScriptBackend.cs`, `CppCodeGenerator.cs`, `Preprocessor.cs`, `BasicLangLexer.cs`, `Parser.cs`, `Compiler.cs`, `Program.cs`, `BuildService.cs`, the LSP, `JsCapabilityChecker.cs`, the Forms files) is unchanged and stands.** Between `098b4de9` and `6fbde6a5` `SemanticAnalyzer.cs`, `SymbolTable.cs` and `CrossFileBindingTests.cs` did NOT change, so every **[X]** anchor in them stands verbatim (`SymbolTable.cs:120`/`:123`, `SemanticAnalyzer.cs:≈604-630`, `≈6421`, `:5908-5938`, `:6687-6720`, `CrossFileBindingTests.cs:149`). ⚠ But `CrossFileBindingTests` holds **26 test cases** (16 `[Test]` + 5 methods × 2 `[TestCase]`), not "17" — see C3. The **`bc29391e`** anchors in the changed files moved; each was mapped through the diff hunks AND re-found by its quoted code:

| Task | Anchor as written (base) | Now on `6fbde6a5` | Verified by |
|---|---|---|---|
| S1, 5 | `IRBuilder.cs:3240` | **`:3281`** | `public void Visit(PreprocessorIfNode node)` |
| S1, 5 | `SemanticAnalyzer.cs:9064` | **`:9424`** | `public void Visit(PreprocessorIfNode node)` |
| S4, 13 | `IRBuilder.cs:2409-2425` (MyBase), `:2411-2413`, `:2423-2424` | **`:2450-2466`**, `:2452-2454`, `:2464-2465` | `public void Visit(MyBaseExpressionNode node)` at 2450 |
| S5, 8 | `IRNodes.cs:1681` (`ClassesBaseFirst`; `IRModule` collections) | **`:1721`** (`class IRModule` at 1699) | quote |
| S7, 10 | `SemanticAnalyzer.cs:8579-8632` `ValidateHandlerWiring` | **`:8939-8992`** | quote |
| S7, 10 | `SemanticAnalyzer.cs:6146-6200` `Visit(ClassNode)` member loop | **`:6504-6558`** (`Visit(ClassNode)` at 6389) | `foreach (var member in node.Members)` at 6504 |
| S7, 10 | `SemanticAnalyzer.cs:744-820` `PopulateClassMemberSignatures` | **`:776-852`** — still no `EventDeclarationNode` case | quote |
| 10 | `SemanticAnalyzer.cs:8494-8524` `Visit(EventDeclarationNode)`, `:8507-8510`, `:8514` | **`:8854-8884`**, `:8867-8870`, **`:8874`** (`GetType("EventHandler")`) | quote |
| S9, 26 | `IROptimizer.cs:1829-1872` fold arms, `:1829` `FoldAdd`, `:1802` decimal skip | **`:1838-1881`**, **`:1838`**, **`:1811`** — still no decimal arm anywhere (S9 holds) | quote |
| §-list 6, 18 | `SemanticAnalyzer.cs:2189-2210` `TryRetypeLiteralToDecimal` | **`:2245-2266`** | quote |
| §-list 7, 18 | `SemanticAnalyzer.cs:1557-1602` conversion functions | **`:1613-1658`** (`CDbl` 1619, `CBool` 1658) | quote |
| 18 | `SemanticAnalyzer.cs:440` `CommonNetTypes` | **`:434`** (the declaration; `:440` pointed into the set) | quote |
| §-list 9, 11, 15 | `IRBuilder.cs:6636-6656` `IsKnownNetStaticType`, `:6649` | **`:6677-6697`**, **`:6690`** (`u.IsNetNamespace`) | quote |
| §-list 9, 15 | `IRBuilder.cs:5914`, `:5941` | **`:5955`** (`bool isNetType = …`), **`:5982`** (`isStaticCall = (exactClassMatch || isNetType) && !isLocalOrParam;`) | quote |
| 11 | `IRBuilder.cs:5905-5942` call-routing block | **`:5946-5983`** | quotes |
| §-list 10, 11–16, 19 | `JsExecutionTierRosterTests.cs:470-471`, pin **94** | **`:479-480`, pin 96** | read |
| 7 | `SemanticAnalyzer.cs:6060-6098` (`bc29391e`) `Visit(ClassNode)` base block | **`:6418-6456`**; the quote `var baseType = _typeManager.GetType(node.BaseClass);` is at **`:6421`** (= the [X] number) | quote |
| 7 | `SemanticAnalyzer.cs:5593-5630` `RegisterClassBases` | **`:5951-5988`**; its lookup is `var baseType = _typeManager.GetType(cls.BaseClass);` at **`:5959`** (variable `cls`) | quote |
| 8 | `IRBuilder.cs:2485` lambda `ModuleName` | **`:2526`** | quote |
| 9 | `SemanticAnalyzer.cs:592` sibling loop | **`:604`** (`RegisterSiblingClassShell(classNode, unit)`) | quote |
| 9 | `SemanticAnalyzer.cs:6329-6362` `Visit(EnumNode)` | **`:6687-6720`** (= the [X] number) | quote |
| 9 | `SemanticAnalyzer.cs:11418-11638` `BindMemberAccess`, `:11569`, `:11615` | **`:11778-12041`**, **`:11951`** (`objectType.ResolveMember`), `:12008` — these were `bc29391e` numbers despite the line's [X] tag | quote |
| 13 | `IRNodes.cs:2457` `IRFieldAccess`, `:2515` `IRFieldStore` | **`:2497`**, **`:2555`** | quote |
| 13 | `IRBuilder.cs:5655` member read, `:4906` member store | **`:5696`**, **`:4947`** | quotes |
| 15 | `SemanticAnalyzer.cs:2927` `ResolveTypeName` | **`:2983`** (`ResolveTypeSymbol` at 3066) | quote |
| 15 | `SemanticAnalyzer.cs:7118-7128` the `UsingDirectiveNode` marking | **`:7476-7486`** (method at 7442) | quote |
| 15 | `Visit(NamespaceNode)` `:5973-5986` (it is `SemanticAnalyzer.cs`, not `IRBuilder.cs`) | **`:6331-6344`** | quote |
| 18 | `IRBuilder.cs:5412-5417` Decimal `IRConstant` | **`:5453-5458`** | quote |
| 20A | `IRBuilder.cs:5236-5239` `new IRCompare(tempName, cmpKind, …)` | **`:5277`** | quote |
| 22 | `SemanticAnalyzer.cs:2260-2264` `IsDecimalFloatingMix` | **`:2316-2320`** | quote |
| 22 | `SymbolTable.cs:905-912` `GetCommonType` | **`:907`** — unchanged (this one was a `098b4de9` number) | quote |

  Unchanged and re-confirmed in passing: `SemanticAnalyzer.cs:390` (`_netNativeBackend`), `CSharpBackend.cs:240` / `:415` / `:496` / `:3451` / `:3637` / `:3776` / `:3841` / `:3869` / `:4017` / `:4711`, and `EmitCastText` starts at **`CSharpBackend.cs:4443`** (the [X] number; "≈4467-4498 on `5b4ca51e`" is the rounding rule inside it — `CSharpBackend.cs` has not changed since `bc29391e`, so both are the same code).

**Corrections to Tasks 1–27 (these WIN):**

- **C1 — the dependency section and the header's "⚠ Master has since moved to `5b4ca51e`" are resolved**: the branch is on `6fbde6a5`; use the table above, never the old numbers.
- **C2 — the JS roster pin is 96** (`JsExecutionTierRosterTests.cs:479-480`). Every "`RosterIsPinned` +1" in Tasks 11–14, 16, 19 starts from the number read that day (96 now).
- **C3 — Task 7 Step 2's RED is broader than written**: the defect reproduces on C++ too (`e3cpp`, `BL3001`), so all three `RunsOnEveryBackend` legs go red for the same reason, not only JS and C#. `RegisterClassBases`' variable is `cls` (Step 3's "keep its own name"). Step 2's "the existing 17 stay green" and Step 4's "all 17 + 5 new" read **26** and **26 + 5** (the fixture has 26 test cases on `6fbde6a5`; 17 was a miscount — the file is unchanged since `098b4de9`).
- **C4 — Task 7 / Task 13 C++ legs: a PRE-EXISTING C++ defect can masquerade as their red.** Measured on `6fbde6a5`, project route, MSVC: `Me.X()` calling a `Sub` declared **BELOW** the call in the same class emits `void* t0 = {}; … t0 = this->Init();` → **MSVC `C2440: '=': cannot convert from 'void' to 'void *'`**. Unqualified `Init()` below, and `Me.Init()` above, both build and run (`init`). Independent of `Inherits` and of the class name. It is not a premise of any task (C++ is not a forms target), but Task 7's `DerivedFile` calls `Me.Hello()` on an inherited Sub and Task 13's rows may call `Me.` members: if a C++ leg fails with that C2440 after the task's own fix, it is this defect — isolate it with the three shapes above and report it; do not fold a fix into Task 7/13 without the coordinator's say. (New chip candidate.)
- **C5 — M16 no longer reproduces on JS/C#.** The trap "never name a probe class `F`" is now only a caution; chip `task_ef845b99` can be closed for JS and C# (its C++ symptom is C4).
- **C6 — no task is removed.** M2, M4, M6, M7, M8, M9, M12, M13, M18, M20 all reproduce; M25 is still refused (Task 16 pins it). Task 8 (C# container naming) was not in Task 0's table; `CSharpBackend.cs` is unchanged since `bc29391e`, so its measured premise stands untouched.

---

## Task 1: `#If` / `#ElseIf` / `#Else` / `#End If` in the preprocessor

Spec §4.1 (O3). The conditional stack learns VB's `#If` form; `#IfDef`/`#IfNDef`/`#EndIf` keep working and may mix with it.

**Files:**
- Create: `BasicLang/PreprocessorCondition.cs`
- Modify: `BasicLang/Preprocessor.cs` (dispatch `:221-308`; `ConditionalState` `:175-180`; `ProcessIfDef` `:465-497`; `ProcessElse` `:502-526`; `ProcessEndIf` `:531-544`; `IsConditionalActive` `:549-563`; unclosed-block message `:317`)
- Modify: `VisualGameStudio.Tests/Compiler/JavaScriptInteropTests.cs:55-58` (comment only)
- Create: `VisualGameStudio.Tests/Compiler/PreprocessorConditionalTests.cs`

- [ ] **Step 1: Grep for the error texts that change.** `Grep` the tests for `without matching #IfDef or #IfNDef`, `#EndIf missing`, `Duplicate #Else`. Record the hits (none on `bc29391e`). Any hit is updated in this task's commit and listed in its message.

- [ ] **Step 2: Write the failing tests.** `VisualGameStudio.Tests/Compiler/PreprocessorConditionalTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec 2026-09-29 §4.1 — VB's <c>#If … Then / #ElseIf … Then / #Else / #End If</c>, beside the
/// existing <c>#IfDef</c>/<c>#IfNDef</c>/<c>#EndIf</c>. Measured before (M9): <c>#If WEB Then</c> reached the
/// parser as "Unexpected token in expression: '#If'" and its <c>#Else</c> was "#Else without matching
/// #IfDef or #IfNDef".
/// </summary>
[TestFixture]
public class PreprocessorConditionalTests
{
    private static (string[] Lines, List<PreprocessorError> Errors) Run(string source, params string[] symbols)
    {
        var pre = new Preprocessor();
        foreach (var s in symbols) pre.Define(s);
        var output = pre.Process(source, "test.bas");
        var lines = output.Replace("\r\n", "\n").Split('\n');
        return (lines, pre.Errors);
    }

    /// <summary>The lines that reach the lexer as CODE: not blank, not a comment.</summary>
    private static string[] Active(string source, params string[] symbols) =>
        Run(source, symbols).Lines.Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("'")).ToArray();

    private const string Chain =
        "#If A Then\nONE\n#ElseIf B Then\nTWO\n#ElseIf C Then\nTHREE\n#Else\nFOUR\n#End If";

    [TestCase(new[] { "A" }, "ONE")]
    [TestCase(new[] { "B" }, "TWO")]
    [TestCase(new[] { "B", "C" }, "TWO")]   // the FIRST true branch wins
    [TestCase(new[] { "C" }, "THREE")]
    [TestCase(new string[0], "FOUR")]
    [TestCase(new[] { "A", "B", "C" }, "ONE")]
    public void AnIfChain_CompilesExactlyOneBranch(string[] symbols, string expected) =>
        Assert.That(Active(Chain, symbols), Is.EqualTo(new[] { expected }));

    [TestCase("WEB", "Not WEB", false)]
    [TestCase("WEB", "WEB And DEBUG", false)]
    [TestCase("WEB", "WEB Or DEBUG", true)]
    [TestCase("WEB", "WEB AndAlso Not DEBUG", true)]
    [TestCase("WEB", "DEBUG OrElse (WEB And Not DESKTOP)", true)]
    [TestCase("WEB", "True", true)]
    [TestCase("WEB", "False Or False", false)]
    [TestCase("WEB", "web", true)]                       // symbols are case-insensitive, as #IfDef's are
    [TestCase("WEB", "Not (WEB Or DEBUG)", false)]
    public void TheCondition_IsEvaluatedWithVbOperators(string defined, string condition, bool taken) =>
        Assert.That(Active($"#If {condition} Then\nYES\n#Else\nNO\n#End If", defined),
            Is.EqualTo(new[] { taken ? "YES" : "NO" }));

    [Test]
    public void DirectivesAreCaseInsensitive_AndEndIfHasBothSpellings()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Active("#if WEB then\nA\n#elseif X then\nB\n#else\nC\n#end if", "WEB"), Is.EqualTo(new[] { "A" }));
            Assert.That(Active("#If WEB Then\nA\n#EndIf", "WEB"), Is.EqualTo(new[] { "A" }));
            Assert.That(Run("#If WEB Then\nA\n#EndIf", "WEB").Errors, Is.Empty);
            Assert.That(Run("#IfDef WEB\nA\n#End If", "WEB").Errors, Is.Empty);
        });
    }

    /// <summary>⛔ The prefix trap: "#ElseIf" starts with "#Else". Taken as #Else it would run the chain wrong.</summary>
    [Test]
    public void ElseIf_IsNotMistakenForElse() =>
        Assert.That(Active("#If A Then\nONE\n#ElseIf B Then\nTWO\n#End If"), Is.Empty,
            "neither A nor B is defined: nothing is active");

    [Test]
    public void NestedBlocks_InsideAnInactiveBranch_StayInactive()
    {
        const string src = "#If A Then\n#If B Then\nINNER\n#Else\nINNER_ELSE\n#End If\n#Else\nOUTER_ELSE\n#End If";
        Assert.Multiple(() =>
        {
            Assert.That(Active(src, "B"), Is.EqualTo(new[] { "OUTER_ELSE" }));
            Assert.That(Active(src, "A"), Is.EqualTo(new[] { "INNER_ELSE" }));
            Assert.That(Active(src, "A", "B"), Is.EqualTo(new[] { "INNER" }));
        });
    }

    [Test]
    public void IfAndIfDef_Mix() =>
        Assert.That(Active("#IfDef A\n#If Not B Then\nX\n#End If\n#EndIf", "A"), Is.EqualTo(new[] { "X" }));

    /// <summary>Every source line keeps its line number: the directives and skipped lines are commented, never removed.</summary>
    [Test]
    public void TheOutput_HasOneLinePerSourceLine()
    {
        var (lines, _) = Run(Chain + "\nAFTER", "B");
        Assert.Multiple(() =>
        {
            Assert.That(lines.Length - 1, Is.EqualTo(Chain.Split('\n').Length + 1),
                "the trailing newline of the last AppendLine adds one empty element");
            Assert.That(lines[3], Is.EqualTo("TWO"));
            Assert.That(lines[9], Is.EqualTo("AFTER"));
        });
    }

    [TestCase("#If WEB\nX\n#End If", "#If requires a condition followed by 'Then'")]
    [TestCase("#If WEB + 1 Then\nX\n#End If", "Invalid #If condition")]
    [TestCase("#If Then\nX\n#End If", "#If requires a condition followed by 'Then'")]
    [TestCase("#If (WEB Then\nX\n#End If", "Invalid #If condition")]
    [TestCase("#If A Then\n#Else\n#ElseIf B Then\n#End If", "#ElseIf after #Else")]
    [TestCase("#IfDef A\n#ElseIf B Then\n#EndIf", "#ElseIf is only valid inside #If")]
    [TestCase("#ElseIf B Then", "#ElseIf without matching #If")]
    [TestCase("#Else", "#Else without matching #If, #IfDef or #IfNDef")]
    [TestCase("#End If", "#End If without matching #If, #IfDef or #IfNDef")]
    [TestCase("#If A Then\nX", "Unclosed conditional block")]
    [TestCase("#If A Then\n#Else\n#Else\n#End If", "Duplicate #Else in conditional block")]
    public void MalformedConditionals_AreReported(string source, string expected) =>
        Assert.That(Run(source).Errors.Select(e => e.Message), Has.Some.Contains(expected));

    /// <summary>#JsImport and #CppInclude honour an inactive #If exactly as they honour an inactive #IfDef.</summary>
    [Test]
    public void GatedDirectives_HonourAnInactiveIf()
    {
        var pre = new Preprocessor();
        pre.Process("#If WEB Then\n#JsImport \"./web.js\"\n#Else\n#CppInclude <unistd.h>\n#End If", "test.bas");
        Assert.Multiple(() =>
        {
            Assert.That(pre.JsImports, Is.Empty);
            Assert.That(pre.CppIncludes, Is.EqualTo(new[] { "<unistd.h>" }));
        });
    }
}
```

- [ ] **Step 3: Run it — expect RED.**

```powershell
dotnet build VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release
cmd /c "dotnet test VisualGameStudio.Tests\VisualGameStudio.Tests.csproj -c Release --no-build --filter `"FullyQualifiedName~PreprocessorConditionalTests`" > `"$sp\run.txt`" 2>&1"
```
Expected: the `#If` rows fail — `Active(...)` returns the RAW directive lines (`#If A Then` reaches the output uncommented, `ONE`/`TWO`/… are all active), and the error rows fail with `#Else without matching #IfDef or #IfNDef`. `IfAndIfDef_Mix` fails the same way.

- [ ] **Step 4: Implement the condition evaluator.** Create `BasicLang/PreprocessorCondition.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace BasicLang.Compiler
{
    /// <summary>
    /// The condition of <c>#If … Then</c> / <c>#ElseIf … Then</c> (spec 2026-09-29 §4.1): defined-symbol names,
    /// <c>Not</c>, <c>And</c>/<c>AndAlso</c>, <c>Or</c>/<c>OrElse</c>, parentheses, <c>True</c>/<c>False</c>.
    /// VB precedence: Not binds tightest, then And, then Or. No values, no comparisons — a symbol is defined or
    /// it is not. A PURE function: the preprocessor's compile mode and editor mode both call it.
    /// </summary>
    internal static class PreprocessorCondition
    {
        public static bool TryEvaluate(string text, Func<string, bool> isDefined, out bool value, out string error)
        {
            value = false;
            var tokens = Tokenize(text ?? "", out error);
            if (tokens == null) return false;

            var pos = 0;
            try
            {
                value = ParseOr(tokens, ref pos, isDefined);
                if (pos != tokens.Count)
                {
                    error = $"unexpected '{tokens[pos]}'";
                    return false;
                }
                return true;
            }
            catch (FormatException ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static List<string> Tokenize(string text, out string error)
        {
            error = null;
            var tokens = new List<string>();
            var i = 0;
            while (i < text.Length)
            {
                var c = text[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '(' || c == ')') { tokens.Add(c.ToString()); i++; continue; }
                if (char.IsLetter(c) || c == '_')
                {
                    var start = i;
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                    tokens.Add(text.Substring(start, i - start));
                    continue;
                }
                error = $"unexpected character '{c}'";
                return null;
            }
            if (tokens.Count == 0)
            {
                error = "the condition is empty";
                return null;
            }
            return tokens;
        }

        private static bool Is(List<string> t, int pos, string word) =>
            pos < t.Count && string.Equals(t[pos], word, StringComparison.OrdinalIgnoreCase);

        private static bool ParseOr(List<string> t, ref int pos, Func<string, bool> d)
        {
            var value = ParseAnd(t, ref pos, d);
            while (Is(t, pos, "Or") || Is(t, pos, "OrElse"))
            {
                pos++;
                var right = ParseAnd(t, ref pos, d);   // always parsed: a malformed right side is an error either way
                value = value || right;
            }
            return value;
        }

        private static bool ParseAnd(List<string> t, ref int pos, Func<string, bool> d)
        {
            var value = ParseNot(t, ref pos, d);
            while (Is(t, pos, "And") || Is(t, pos, "AndAlso"))
            {
                pos++;
                var right = ParseNot(t, ref pos, d);
                value = value && right;
            }
            return value;
        }

        private static bool ParseNot(List<string> t, ref int pos, Func<string, bool> d)
        {
            if (Is(t, pos, "Not"))
            {
                pos++;
                return !ParseNot(t, ref pos, d);
            }
            return ParsePrimary(t, ref pos, d);
        }

        private static readonly HashSet<string> Keywords =
            new(StringComparer.OrdinalIgnoreCase) { "And", "AndAlso", "Or", "OrElse", "Not", "Then" };

        private static bool ParsePrimary(List<string> t, ref int pos, Func<string, bool> d)
        {
            if (pos >= t.Count) throw new FormatException("the condition ends too early");
            var token = t[pos++];
            if (token == "(")
            {
                var inner = ParseOr(t, ref pos, d);
                if (!Is(t, pos, ")")) throw new FormatException("missing ')'");
                pos++;
                return inner;
            }
            if (token == ")") throw new FormatException("unexpected ')'");
            if (string.Equals(token, "True", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(token, "False", StringComparison.OrdinalIgnoreCase)) return false;
            if (Keywords.Contains(token)) throw new FormatException($"unexpected '{token}'");
            return d(token);
        }
    }
}
```

- [ ] **Step 5: Rewrite the conditional stack in `Preprocessor.cs`.**
  - Replace `ConditionalState` (`:175-180`) with:

```csharp
        private class ConditionalState
        {
            public bool ParentActive { get; set; }    // was the enclosing block active when this one opened?
            public bool BranchActive { get; set; }    // is the current branch the one being compiled?
            public bool AnyBranchTaken { get; set; }  // has an earlier branch of this block been taken?
            public bool SeenElse { get; set; }        // has #Else been seen (no #ElseIf may follow)?
            public bool IsIfBlock { get; set; }       // opened by #If (true) or by #IfDef/#IfNDef (false)
        }
```
  - `IsConditionalActive` (`:549-563`) becomes: empty stack → `true`; else `var s = _conditionalStack.Peek(); return s.ParentActive && s.BranchActive;`.
  - In `ProcessIfDef`, both `Push` calls become `new ConditionalState { ParentActive = IsConditionalActive(), BranchActive = conditionTrue, AnyBranchTaken = conditionTrue }` (the error arm with `false`). ⚠ `ParentActive` is read BEFORE the push.
  - Replace the dispatch arms `#IfDef` … `#EndIf` (`:239-262`) with this order (regexes are `static readonly Regex` fields, `RegexOptions.IgnoreCase | RegexOptions.Compiled`):

```csharp
        private static readonly Regex IfDirective = new(@"^#If\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ElseIfDirective = new(@"^#ElseIf\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ElseDirective = new(@"^#Else\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex EndIfDirective = new(@"^#End\s*If\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex IfForm = new(@"^#(?:Else)?If\s+(?<cond>.*?)\s+Then\s*(?:'.*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
```

```csharp
                // ⛔ ORDER IS LOAD-BEARING. #IfDef/#IfNDef before #If (\b stops "#If" matching "#IfDef" anyway);
                // #ElseIf before #Else (\b: "#Else" + "If" has no word boundary, so ElseDirective cannot take it).
                else if (trimmedLine.StartsWith("#IfDef", StringComparison.OrdinalIgnoreCase))
                {
                    ProcessIfDef(trimmedLine, lineNumber, false);
                    result.AppendLine($"' {line}");
                }
                else if (trimmedLine.StartsWith("#IfNDef", StringComparison.OrdinalIgnoreCase))
                {
                    ProcessIfDef(trimmedLine, lineNumber, true);
                    result.AppendLine($"' {line}");
                }
                else if (ElseIfDirective.IsMatch(trimmedLine))
                {
                    ProcessElseIf(trimmedLine, lineNumber);
                    result.AppendLine($"' {line}");
                }
                else if (ElseDirective.IsMatch(trimmedLine))
                {
                    ProcessElse(lineNumber);
                    result.AppendLine($"' {line}");
                }
                else if (EndIfDirective.IsMatch(trimmedLine))
                {
                    ProcessEndIf(lineNumber);
                    result.AppendLine($"' {line}");
                }
                else if (IfDirective.IsMatch(trimmedLine))
                {
                    ProcessIf(trimmedLine, lineNumber);
                    result.AppendLine($"' {line}");
                }
```
  - Add:

```csharp
        private void ProcessIf(string line, int lineNumber)
        {
            var parentActive = IsConditionalActive();
            var taken = EvaluateIfForm(line, "#If", lineNumber);
            _conditionalStack.Push(new ConditionalState
            {
                ParentActive = parentActive, BranchActive = taken, AnyBranchTaken = taken, IsIfBlock = true
            });
        }

        private void ProcessElseIf(string line, int lineNumber)
        {
            if (_conditionalStack.Count == 0) { Fail(lineNumber, "#ElseIf without matching #If"); return; }
            var state = _conditionalStack.Peek();
            if (!state.IsIfBlock) { Fail(lineNumber, "#ElseIf is only valid inside #If … #End If, not #IfDef/#IfNDef"); return; }
            if (state.SeenElse) { Fail(lineNumber, "#ElseIf after #Else"); return; }
            var taken = EvaluateIfForm(line, "#ElseIf", lineNumber);
            state.BranchActive = !state.AnyBranchTaken && taken;
            state.AnyBranchTaken |= state.BranchActive;
        }

        /// <summary>The condition of an #If/#ElseIf line; a malformed one is reported and counts as false.</summary>
        private bool EvaluateIfForm(string line, string directive, int lineNumber)
        {
            var match = IfForm.Match(line);
            if (!match.Success || match.Groups["cond"].Value.Trim().Length == 0)
            {
                Fail(lineNumber, $"{directive} requires a condition followed by 'Then'");
                return false;
            }
            var condition = match.Groups["cond"].Value;
            if (!PreprocessorCondition.TryEvaluate(condition, IsDefined, out var value, out var error))
            {
                Fail(lineNumber, $"Invalid {directive} condition '{condition}': {error}");
                return false;
            }
            return value;
        }

        private void Fail(int lineNumber, string message) =>
            _errors.Add(new PreprocessorError { Line = lineNumber, Message = message });
```
  (`Fail` near `:59` is a LOCAL function inside the `#JsImport` parser, not a member — no collision with this instance method.)
  - `ProcessElse` (`:502-526`): error text `"#Else without matching #If, #IfDef or #IfNDef"`; then `if (state.SeenElse) { … "Duplicate #Else in conditional block" … }`; then `state.BranchActive = !state.AnyBranchTaken; state.AnyBranchTaken = true; state.SeenElse = true;`.
  - `ProcessEndIf` (`:531-544`): error text `"#End If without matching #If, #IfDef or #IfNDef"`.
  - `:317`: `$"Unclosed conditional block: {_conditionalStack.Count} #End If missing"`.
  - `JavaScriptInteropTests.cs:55-58`: replace the comment's claim with "#IfDef here only because this test predates #If; both are directives the preprocessor consumes."

- [ ] **Step 6: Run — expect GREEN.** Same command. Then run the neighbours: `--filter "FullyQualifiedName~CppPassthroughTests|FullyQualifiedName~JavaScriptInteropTests|FullyQualifiedName~ForeignFeatureGuardTests|FullyQualifiedName~LexerTests"` — all green (Integration rows included by name).

- [ ] **Step 7: Mutations (each must turn a named test red; revert with Edit + rebuild).**
  1. Swap the `ElseIfDirective` and `ElseDirective` arms → `AnIfChain_CompilesExactlyOneBranch` + `ElseIf_IsNotMistakenForElse` red.
  2. In `ProcessElseIf`, drop `!state.AnyBranchTaken &&` → the `{B, C}` row red.
  3. In `ParseOr`, swap the call order so `Or` binds tighter than `And` (call `ParseNot` from `ParseOr` and `ParseOr` from `ParseAnd`) → a precedence row red (add `[TestCase("WEB", "WEB Or DEBUG And False", true)]` first if none catches it — record which).
  4. `IsConditionalActive` returns `s.BranchActive` only → `NestedBlocks_…` red.

- [ ] **Step 8: Commit.** Stage `BasicLang/PreprocessorCondition.cs`, `BasicLang/Preprocessor.cs`, `VisualGameStudio.Tests/Compiler/PreprocessorConditionalTests.cs`, `VisualGameStudio.Tests/Compiler/JavaScriptInteropTests.cs`. Message: "Preprocessor: #If/#ElseIf/#Else/#End If with VB conditions (spec §4.1, M9)" + the mutation table.

---

## Task 2: Directive hygiene — `#Define` keeps its line and is gated; `#Include` is gated and keeps the parent's state

Spec-claims #8. Three pre-existing defects that `#If` makes reachable.

**Files:**
- Modify: `BasicLang/Preprocessor.cs` (`#Include` arm `:221-233`; `#Define` arm `:235-238`; `Process` `:202-322`; `ProcessInclude` `:327-401`)
- Modify: `VisualGameStudio.Tests/Compiler/PreprocessorConditionalTests.cs` (Edit — add a region)

- [ ] **Step 1: Write the failing tests** (append inside the class):

```csharp
    // ------------------------------------------------------------ directive hygiene (Task 2)

    [Test]
    public void ADefineLine_KeepsItsLine_SoEveryLaterLineKeepsItsNumber()
    {
        var (lines, errors) = Run("Sub Main()\n#Define X\nCODE\nEnd Sub");
        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty);
            Assert.That(lines[1].TrimStart(), Does.StartWith("'"), "the directive is commented, not removed");
            Assert.That(lines[2], Is.EqualTo("CODE"), "line 3 of the source is line 3 of the output");
        });
    }

    [Test]
    public void ADefine_InAnInactiveBranch_DefinesNothing() =>
        Assert.That(Active("#If NOPE Then\n#Define Y\n#End If\n#If Y Then\nLEAKED\n#End If"), Is.Empty);

    [Test]
    public void AnInclude_InAnInactiveBranch_IsNotSpliced()
    {
        var dir = System.IO.Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "bl-pre-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "inc.bas"), "INCLUDED");
            var pre = new Preprocessor();
            var output = pre.Process("#If NOPE Then\n#Include \"inc.bas\"\n#End If",
                System.IO.Path.Combine(dir, "main.bas"));
            Assert.That(output, Does.Not.Contain("INCLUDED"));
        }
        finally { System.IO.Directory.Delete(dir, true); }
    }

    /// <summary>⛔ Before: the include's recursive Process() cleared the PARENT's conditional stack and errors,
    /// so the parent's #End If was "without matching" and the lines after it were compiled unconditionally.</summary>
    [Test]
    public void AnInclude_InsideAnActiveIf_KeepsTheParentsBlock()
    {
        var dir = System.IO.Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "bl-pre-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "inc.bas"), "#If Z Then\nZED\n#End If\nINC");
            var pre = new Preprocessor();
            pre.Define("A");
            var output = pre.Process("#If A Then\n#Include \"inc.bas\"\nAFTER\n#Else\nOTHER\n#End If",
                System.IO.Path.Combine(dir, "main.bas"));
            var active = output.Replace("\r\n", "\n").Split('\n')
                .Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("'")).ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(pre.Errors, Is.Empty);
                Assert.That(active, Is.EqualTo(new[] { "INC", "AFTER" }));
            });
        }
        finally { System.IO.Directory.Delete(dir, true); }
    }
```

- [ ] **Step 2: Run — expect RED:** `ADefineLine_…` (line 2 is `CODE`: the lines shifted), `ADefine_InAnInactiveBranch_…` (`LEAKED` active), `AnInclude_InAnInactiveBranch_…` (`INCLUDED` present), `AnInclude_InsideAnActiveIf_…` (an `#End If without matching` error, and `OTHER` active).

- [ ] **Step 3: Implement.**
  - `#Define` arm: `if (IsConditionalActive()) ProcessDefine(trimmedLine, lineNumber); result.AppendLine($"' {line}");`
  - `#Include` arm: wrap the existing body in `if (IsConditionalActive()) { … } else { result.AppendLine($"' [IFDEF SKIP] {line}"); }`.
  - Split `Process`: the public `Process(source, filePath)` keeps `_errors.Clear(); _conditionalStack.Clear();` and the include-guard registration, then calls a new `private string ProcessCore(string source, string filePath)` holding the line loop and the unclosed-block check (`:211-321`).
  - `ProcessInclude` (`:387`) calls, instead of `Process(includeContent, resolvedPath)`:

```csharp
                // ⛔ An included file has its OWN conditional blocks, and must not see or disturb the
                // includer's. Before, the recursive public Process() cleared _errors and _conditionalStack.
                var parentBlocks = _conditionalStack.ToArray();   // top first
                _conditionalStack.Clear();
                result.Append(ProcessCore(includeContent, resolvedPath));
                _conditionalStack.Clear();
                for (var i = parentBlocks.Length - 1; i >= 0; i--) _conditionalStack.Push(parentBlocks[i]);
```

- [ ] **Step 4: Run — expect GREEN**, plus the Task 1 fixture and `CppPassthroughTests`/`ForeignFeatureGuardTests` (the `#Include` rows) by name.
- [ ] **Step 5: Mutations:** (1) drop the `AppendLine` in the `#Define` arm → `ADefineLine_…` red; (2) drop the `IsConditionalActive()` gate on `#Define` → `ADefine_…` red; (3) call `Process` instead of `ProcessCore` from `ProcessInclude` → `AnInclude_InsideAnActiveIf_…` red.
- [ ] **Step 6: Commit** (`BasicLang/Preprocessor.cs`, the test file). Message: "Preprocessor: #Define keeps its line and honours the branch; #Include is gated and keeps the includer's blocks".

---

## Task 3: Build symbols — `WEB`/`DESKTOP`/`DEBUG`/`RELEASE`/`DefineConstants` on every route

Spec §4.1, O16, O19. Scope call S2.

**Files:**
- Create: `BasicLang/BuildSymbols.cs`
- Modify: `BasicLang/Compiler.cs` (`CompilerOptions` `:90-155`; `BasicCompiler` ctor `:180-194`)
- Modify: `BasicLang/Program.cs` (project build options `:835-840`; single-file options `:1423-1452`)
- Modify: `VisualGameStudio.ProjectSystem/Services/BuildService.cs` (`:661-666`)
- Modify: `BasicLang/ProjectSystem/CppProjectBuilder.cs` (`:532-536`; `configuration` is in scope from `EmitCore`, `:400`)
- Modify: `BasicLang/Debugger/DebugSession.cs` (`:184`, `:208`)
- Modify: `VisualGameStudio.Tests/Compiler/JsTestSupport.cs` (`BuildModule`'s preprocessor branch `:42-50`)
- Create: `VisualGameStudio.Tests/Compiler/BuildSymbolTests.cs` (fast) and `VisualGameStudio.Tests/Compiler/BuildSymbolRouteTests.cs` (Integration; name does not match the JS roster patterns — it is NOT a roster fixture; confirm with `RosterCoversEveryJavaScriptIntegrationFixture`)

- [ ] **Step 1: Write the failing unit tests.** `BuildSymbolTests.cs`:

```csharp
using System.Collections.Generic;
using BasicLang.Compiler;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>Spec §4.1 / O16 / O19 — the ONE answer to "which conditional-compilation symbols does this build define".</summary>
[TestFixture]
public class BuildSymbolTests
{
    [TestCase("javascript", "WEB")]
    [TestCase("js", "WEB")]
    [TestCase("JavaScript", "WEB")]
    [TestCase("csharp", "DESKTOP")]
    [TestCase("cpp", "DESKTOP")]
    [TestCase("msil", "DESKTOP")]
    [TestCase("llvm", "DESKTOP")]
    [TestCase(null, "DESKTOP")]
    public void TheTargetSymbol(string backend, string expected) =>
        Assert.That(BuildSymbols.For(backend, null, null), Is.EqualTo(new[] { expected }));

    [TestCase("Debug", "DEBUG")]
    [TestCase("debug", "DEBUG")]
    [TestCase("Release", "RELEASE")]
    public void TheConfigurationSymbol(string configuration, string expected) =>
        Assert.That(BuildSymbols.For("csharp", configuration, null), Is.EqualTo(new[] { "DESKTOP", expected }));

    [Test]
    public void ACustomConfiguration_DefinesNeither() =>
        Assert.That(BuildSymbols.For("csharp", "Staging", null), Is.EqualTo(new[] { "DESKTOP" }));

    [Test]
    public void DefineConstants_AreSplitTrimmedNamedAndDeduplicated() =>
        Assert.That(BuildSymbols.For("javascript", "Debug", new[] { " TRACE ; DEBUG;;LEVEL=2,Extra" }),
            Is.EqualTo(new[] { "WEB", "DEBUG", "TRACE", "LEVEL", "Extra" }));

    /// <summary>The compiler object defines them — every route that builds a BasicCompiler from options gets them.</summary>
    [Test]
    public void BasicCompiler_DefinesTheSymbolsItsOptionsName()
    {
        var dir = System.IO.Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "bl-sym-" + System.Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var file = System.IO.Path.Combine(dir, "Main.bas");
            System.IO.File.WriteAllText(file,
                "Sub Main()\n#If WEB AndAlso RELEASE Then\nConsole.WriteLine(\"web release\")\n" +
                "#Else\nThisIsNotCode\n#End If\nEnd Sub\n");
            var result = new BasicCompiler(new CompilerOptions { TargetBackend = "javascript", Configuration = "Release" })
                .CompileProjectFiles(new[] { file });
            Assert.That(result.HasErrors, Is.False, string.Join(" | ", System.Linq.Enumerable.Select(result.AllErrors, e => e.Message)));
        }
        finally { System.IO.Directory.Delete(dir, true); }
    }
}
```

- [ ] **Step 2: Run — expect RED:** does not compile (`BuildSymbols`, `CompilerOptions.Configuration` do not exist). That compile error IS the red; record it.

- [ ] **Step 3: Implement `BuildSymbols`** — create `BasicLang/BuildSymbols.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace BasicLang.Compiler
{
    /// <summary>
    /// ⛔ THE one answer to "which conditional-compilation symbols does this build define" (spec 2026-09-29 §4.1,
    /// O16, O19): <c>WEB</c> for a JavaScript build and <c>DESKTOP</c> for every other backend; <c>DEBUG</c> in a
    /// Debug configuration and <c>RELEASE</c> in a Release one (null or any other name: neither); then the project's
    /// <c>&lt;DefineConstants&gt;</c>, split on ';' or ',', trimmed, a <c>NAME=value</c> entry contributing NAME.
    /// Case-insensitive and de-duplicated, first spelling wins. Called by the <see cref="BasicCompiler"/>
    /// constructor (every build route) and the LSP (Task 6) — never re-derived.
    /// </summary>
    public static class BuildSymbols
    {
        public const string Web = "WEB";
        public const string Desktop = "DESKTOP";
        public const string Debug = "DEBUG";
        public const string Release = "RELEASE";

        public static bool IsWebBackend(string targetBackend) =>
            targetBackend?.Trim().ToLowerInvariant() is "javascript" or "js";

        public static IReadOnlyList<string> For(
            string targetBackend, string configuration, IEnumerable<string> defineConstants)
        {
            var symbols = new List<string>();
            void Add(string name)
            {
                name = name?.Trim();
                if (string.IsNullOrEmpty(name)) return;
                if (symbols.Any(s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase))) return;
                symbols.Add(name);
            }

            Add(IsWebBackend(targetBackend) ? Web : Desktop);

            var config = configuration?.Trim();
            if (string.Equals(config, "Debug", StringComparison.OrdinalIgnoreCase)) Add(Debug);
            else if (string.Equals(config, "Release", StringComparison.OrdinalIgnoreCase)) Add(Release);

            foreach (var entry in defineConstants ?? Enumerable.Empty<string>())
            {
                foreach (var part in (entry ?? "").Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    Add(part.Split('=')[0]);
                }
            }

            return symbols;
        }
    }
}
```

- [ ] **Step 4: `CompilerOptions` and the constructor.** In `CompilerOptions` after `SearchPaths` (`:96`):

```csharp
        /// <summary>The build configuration ("Debug"/"Release"). Null defines neither DEBUG nor RELEASE (spec §4.1).</summary>
        public string Configuration { get; set; }

        /// <summary>Extra conditional-compilation symbols — the project's &lt;DefineConstants&gt;.</summary>
        public List<string> DefineConstants { get; set; } = new List<string>();
```
In the `BasicCompiler` constructor, right after `_preprocessor = new Preprocessor();` (`:186`):

```csharp
            // ⛔ The ONE place a build's symbols are defined (BuildSymbols). Every route — CLI file, CLI project,
            // IDE, native, debugger — constructs a BasicCompiler from options, so none can forget them.
            foreach (var symbol in BuildSymbols.For(_options.TargetBackend, _options.Configuration, _options.DefineConstants))
            {
                _preprocessor.Define(symbol);
            }
```

- [ ] **Step 5: Every route passes its configuration.**
  - `Program.cs` project build (`:835-840`): add `Configuration = configuration,` and, after the initializer, `if (project.Configurations.TryGetValue(configuration, out var defineConfig)) options.DefineConstants.AddRange(defineConfig.DefineConstants);` (a new variable name: `config` is already the initializer's `out var`).
  - `Program.cs` single file (`:1423`): `var options = new BasicLang.Compiler.CompilerOptions { Configuration = "Debug" };` and two new flags in the loop: `--configuration=<name>` sets `options.Configuration`; `--define=<A;B>` adds to `options.DefineConstants`. Add both to the CLI help text next to `--optimize` (grep `--optimize` in `Program.cs` for the help block).
  - `BuildService.cs` (`:661-666`): add `Configuration = config.Name,` and after the initializer `if (!string.IsNullOrWhiteSpace(config.DefineConstants)) compilerOptions.DefineConstants.Add(config.DefineConstants);` (the IDE model's `DefineConstants` is one `;` string — `VisualGameStudio.Core/Models/BuildConfiguration.cs:9`; `BuildSymbols` splits it).
  - `CppProjectBuilder.cs` (`:532-536`): extract the options construction into `internal static CompilerOptions CompilerOptionsFor(ProjectFile project, string configuration)` (TargetBackend "cpp", `Configuration = configuration`, the project config's `DefineConstants` looked up as `:927` does; the `NetResolverFactory` stays at the call site) and call it — the unit test above reads it without MSVC; the native BUILD itself stays gated by `NativeBuildSkip`.
  - `DebugSession.cs` (`:184`, `:208`): both sites use `internal static CompilerOptions CompilerOptionsForTests() => new CompilerOptions { Configuration = "Debug" };` (name it for what it is if a better name fits — the point is one construction the test can read). `VisualGameStudio.Tests` needs `InternalsVisibleTo` — check `BasicLang.csproj`/`AssemblyInfo` (it is already granted if other tests reach `internal` compiler members; grep `InternalsVisibleTo`).
  - `JsTestSupport.cs` `BuildModule` (`:44`): after `var pre = new Preprocessor();` add `foreach (var s in BuildSymbols.For("javascript", null, null)) pre.Define(s);` with a comment: "the JS helper builds for the web — WEB is defined as every JavaScript route defines it".

- [ ] **Step 6: Write the route tests.** `BuildSymbolRouteTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.JavaScript;
using BasicLang.Compiler.IR.Optimization;
using NUnit.Framework;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Serialization;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec §4.1 — each build ROUTE defines its target and configuration symbols. CLAUDE.md: a fix verified one way can
/// break the other; the IDE build and the CLI are separate routes into the same engine.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class BuildSymbolRouteTests
{
    private const string Program =
        "Sub Main()\n" +
        "#If WEB AndAlso RELEASE Then\n    Console.WriteLine(\"web release\")\n" +
        "#ElseIf WEB Then\n    Console.WriteLine(\"web debug\")\n" +
        "#ElseIf DESKTOP AndAlso DEBUG Then\n    Console.WriteLine(\"desktop debug\")\n" +
        "#Else\n    Console.WriteLine(\"desktop other\")\n" +
        "#End If\nEnd Sub\n";

    private string _dir = "";

    [SetUp]
    public void SetUp() => _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
        "bl-symroute-" + Path.GetRandomFileName())).FullName;

    [TearDown]
    public void TearDown() { try { Directory.Delete(_dir, true); } catch { } }

    private string WriteMain() { var p = Path.Combine(_dir, "Main.bas"); File.WriteAllText(p, Program); return p; }

    private static BasicLang.Compiler.IR.IRModule Optimized(BasicLang.Compiler.IR.IRModule ir)
    {
        var pipeline = new OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.Run(ir);
        return ir;
    }

    [Test]
    public void TheCompilerApi_JavaScriptRelease_IsWebRelease()
    {
        var r = new BasicCompiler(new CompilerOptions { TargetBackend = "javascript", Configuration = "Release" })
            .CompileProjectFiles(new[] { WriteMain() });
        Assert.That(r.HasErrors, Is.False);
        var js = new JavaScriptCodeGenerator().Generate(Optimized(r.CombinedIR!));
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(js)), Is.EqualTo("web release"));
    }

    [Test]
    public void TheCompilerApi_CSharpDebug_IsDesktopDebug()
    {
        var r = new BasicCompiler(new CompilerOptions { TargetBackend = "csharp", Configuration = "Debug" })
            .CompileProjectFiles(new[] { WriteMain() });
        Assert.That(r.HasErrors, Is.False);
        var cs = new CSharpCodeGenerator().Generate(Optimized(r.CombinedIR!));
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpText(cs)), Is.EqualTo("desktop debug"));
    }

    [Test]
    public void TheCompilerApi_NoConfiguration_DefinesNeither()
    {
        var r = new BasicCompiler(new CompilerOptions { TargetBackend = "csharp" }).CompileProjectFiles(new[] { WriteMain() });
        Assert.That(r.HasErrors, Is.False, string.Join(" | ", r.AllErrors.Select(e => e.Message)));
        var cs = new CSharpCodeGenerator().Generate(Optimized(r.CombinedIR!));
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpText(cs)), Is.EqualTo("desktop other"));
    }

    /// <summary>The debugger route (DebugSession builds its own BasicCompiler) is a Debug, desktop build.</summary>
    [Test]
    public void TheDebuggerRoute_IsDesktopDebug()
    {
        var options = BasicLang.Debugger.DebugSession.CompilerOptionsForTests();
        Assert.That(BuildSymbols.For(options.TargetBackend, options.Configuration, options.DefineConstants),
            Is.EqualTo(new[] { "DESKTOP", "DEBUG" }));
    }

    /// <summary>The native route (CppProjectBuilder) passes its configuration; it always builds with MSVC, so it SKIPS
    /// without it (Native/NativeBuildSkip — CLAUDE.md).</summary>
    [Test]
    public void TheNativeRoute_PassesItsConfiguration()
    {
        var options = BasicLang.Compiler.ProjectSystem.CppProjectBuilder.CompilerOptionsFor(
            new BasicLang.Compiler.ProjectSystem.ProjectFile(), "Release");
        Assert.That(BuildSymbols.For(options.TargetBackend, options.Configuration, options.DefineConstants),
            Is.EqualTo(new[] { "DESKTOP", "RELEASE" }));
    }

    /// <summary>The CLI single-file route defaults to Debug.</summary>
    [Test]
    public async Task TheCli_SingleFile_IsWebDebug()
    {
        WriteMain();
        var (exit, stdout, stderr) = await CliTestHarness.RunCli(_dir, "Main.bas", "--target=javascript");
        Assert.That(exit, Is.Zero, stdout + stderr);
        var js = File.ReadAllText(Path.Combine(_dir, "Main.js"));
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(js)), Is.EqualTo("web debug"));
    }

    [Test]
    public async Task TheCli_ProjectBuild_Release_IsWebRelease()
    {
        WriteMain();
        File.WriteAllText(Path.Combine(_dir, "Site.blproj"),
            "<Project>\n  <PropertyGroup>\n    <ProjectName>Site</ProjectName>\n" +
            "    <TargetBackend>JavaScript</TargetBackend>\n  </PropertyGroup>\n  <ItemGroup>\n" +
            "    <Compile Include=\"Main.bas\" />\n  </ItemGroup>\n</Project>\n");
        var (exit, stdout, stderr) = await CliTestHarness.RunCli(_dir, "build", "Site.blproj", "-c", "Release");
        Assert.That(exit, Is.Zero, stdout + stderr);
        var js = Directory.GetFiles(Path.Combine(_dir, "bin", "Release"), "Site.js", SearchOption.AllDirectories).Single();
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(File.ReadAllText(js))), Is.EqualTo("web release"));
    }

    /// <summary>The IDE route: BuildService with the Release configuration selected.</summary>
    [Test]
    public async Task TheIde_BuildService_Release_IsWebRelease()
    {
        WriteMain();
        var path = Path.Combine(_dir, "Site.blproj");
        await File.WriteAllTextAsync(path,
            "<Project>\n  <PropertyGroup>\n    <ProjectName>Site</ProjectName>\n" +
            "    <TargetBackend>JavaScript</TargetBackend>\n  </PropertyGroup>\n  <ItemGroup>\n" +
            "    <Compile Include=\"Main.bas\" />\n  </ItemGroup>\n</Project>\n");
        var project = await new ProjectSerializer().LoadAsync(path);
        var output = new RecordingOutput();
        var service = new BuildService(output) { CurrentConfiguration = new BuildConfiguration { Name = "Release" } };
        var result = await service.BuildProjectAsync(project);
        Assert.That(result.Success, Is.True, output.Dump());
        Assert.Multiple(() =>
        {
            Assert.That(result.GeneratedCode, Does.Contain("web release"));
            Assert.That(result.GeneratedCode, Does.Not.Contain("web debug"));
        });
    }

    [Test]
    public void TheJsTestHelper_DefinesWeb() =>
        Assert.That(JsTestSupport.Compile(Program, runPreprocessor: true), Does.Contain("web debug").Or.Contain("web release"));
}
```
  ⚠ `RecordingOutput` is the helper `JavaScriptProjectBuildTests` uses (`VisualGameStudio.Tests/Services/JavaScriptProjectBuildTests.cs:106-111`); find its declaration by grep and reuse it (make it `internal` if it is `private` there — one line, same commit). ⚠ The last test: with no configuration `JsTestSupport` defines only `WEB`, so the chain yields "web debug"; assert exactly `Does.Contain("web debug")` once confirmed.

- [ ] **Step 7: Run both fixtures — expect GREEN.** Then the route neighbours by name: `JavaScriptProjectBuildTests`, `CrossFileBindingTests`, `NetBuildPipelineTests`, `DebugSession*` tests (grep), `CppProjectBuilder*` tests (skip without MSVC — `NativeBuildSkip`).
- [ ] **Step 8: Mutations:** (1) remove the constructor loop → every route test red; (2) `BuildService` drops `Configuration = config.Name` → `TheIde_…` red ("web debug"); (3) `Program.cs` project route drops `Configuration = configuration` → `TheCli_ProjectBuild_…` red; (4) `IsWebBackend` returns false for `"js"` → the `js` row red.
- [ ] **Step 9: Commit** every file listed. Message names the routes covered and the mutation results.

---

## Task 4: The golden test over existing `#IfDef` programs, and the release note

O19. Defining `DEBUG`/`RELEASE`/`WEB`/`DESKTOP` must change NOTHING for a program that does not name them.

**Files:**
- Create: `VisualGameStudio.Tests/Compiler/BuildSymbolGoldenTests.cs`
- Modify: `docs/wiki/content/language.md` (the conditional-compilation section; grep "IfDef"), `docs/wiki/wiki-content.js` (regenerated mirror — update it exactly as `27e8307c` did for `language.md`), `CLAUDE.md` ("BasicLang language": `conditional compilation (#If/#IfDef/#Else/#EndIf)` → the true list)

- [ ] **Step 1: Write the test.**

```csharp
using System;
using System.Linq;
using BasicLang.Compiler;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// O19 — the build now defines WEB/DESKTOP/DEBUG/RELEASE. A program that names none of them must preprocess
/// byte-for-byte as before; one that does changes exactly as listed. The corpus is every #IfDef/#IfNDef source in
/// the suite on bc29391e (CppPassthroughTests.cs:206/218, JavaScriptInteropTests.cs:64) plus shapes users write.
/// </summary>
[TestFixture]
public class BuildSymbolGoldenTests
{
    private static readonly string[] Corpus =
    {
        "#IfNDef WINDOWS\n#CppInclude <unistd.h>\n#EndIf\nSub Main()\nEnd Sub",
        "#IfDef NEVER_DEFINED\n#JsImport \"./nope.js\"\n#EndIf\nSub Main()\nEnd Sub",
        "#IfDef TRACE\nSub Log()\nEnd Sub\n#Else\nSub Log2()\nEnd Sub\n#EndIf",
        "#Define LOCAL\n#IfDef LOCAL\nSub A()\nEnd Sub\n#EndIf",
    };

    private static readonly string[] NamesABuildSymbol =
    {
        "#IfDef DEBUG\nSub Trace()\nEnd Sub\n#EndIf",
        "#IfNDef RELEASE\nSub Trace()\nEnd Sub\n#EndIf",
        "#IfDef WEB\nSub OnlyWeb()\nEnd Sub\n#EndIf",
    };

    private static string Pre(string source, params string[] symbols)
    {
        var pre = new Preprocessor();
        foreach (var s in symbols) pre.Define(s);
        return pre.Process(source, "golden.bas");
    }

    private static readonly (string Backend, string Config)[] Builds =
    {
        ("csharp", "Debug"), ("csharp", "Release"), ("javascript", "Debug"), ("javascript", "Release"),
        ("cpp", "Debug"), ("cpp", null),
    };

    [Test]
    public void AProgramNamingNoBuildSymbol_PreprocessesIdentically_OnEveryBuild()
    {
        Assert.Multiple(() =>
        {
            foreach (var source in Corpus)
            foreach (var (backend, config) in Builds)
            {
                Assert.That(Pre(source, BuildSymbols.For(backend, config, null).ToArray()), Is.EqualTo(Pre(source)),
                    $"{backend}/{config ?? "none"}: {source.Split('\n')[0]}");
            }
        });
    }

    /// <summary>The listed difference: code under #IfDef DEBUG now compiles in Debug builds (the release note).</summary>
    [Test]
    public void AProgramNamingABuildSymbol_ChangesExactlyAsListed()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Pre(NamesABuildSymbol[0], "DESKTOP", "DEBUG"), Does.Contain("\nSub Trace()"));
            Assert.That(Pre(NamesABuildSymbol[0], "DESKTOP", "RELEASE"), Does.Not.Contain("\nSub Trace()"));
            Assert.That(Pre(NamesABuildSymbol[1], "DESKTOP", "RELEASE"), Does.Not.Contain("\nSub Trace()"));
            Assert.That(Pre(NamesABuildSymbol[2], "WEB"), Does.Contain("\nSub OnlyWeb()"));
            Assert.That(Pre(NamesABuildSymbol[2], "DESKTOP"), Does.Not.Contain("\nSub OnlyWeb()"));
        });
    }
}
```

- [ ] **Step 2: Run — expect GREEN at once** (Tasks 1–3 are in). This test is a GUARD, not a driver: prove it can fail by mutation (Step 3).
- [ ] **Step 3: Mutation:** make `BuildSymbols.For` also add `"TRACE"` → `AProgramNamingNoBuildSymbol_…` red on the TRACE corpus entry. Revert.
- [ ] **Step 4: Docs.** `language.md`: document `#If`/`#ElseIf`/`#Else`/`#End If`, the operators, and the symbols with the release-note line: "`DEBUG` and `RELEASE` are now defined by the build configuration, and `WEB`/`DESKTOP` by the target — code under `#IfDef DEBUG` now compiles in Debug builds." Regenerate/update `wiki-content.js` the way `27e8307c` did. `CLAUDE.md`: correct the language bullet.
- [ ] **Step 5: Commit.**

---

## Task 5: Delete the dormant `#If` AST node

Scope call S1.

**Files:**
- Modify: `BasicLang/ASTNodes.cs` (`IASTVisitor` member `:112`; `PreprocessorIfNode` `:1864-1879`; `PreprocessorElseIfClause` `:1881-…`)
- Modify: `BasicLang/ASTPrettyPrinter.cs:1174`, `BasicLang/IRBuilder.cs:3240`, `BasicLang/SemanticAnalyzer.cs:9064` (delete each `Visit(PreprocessorIfNode)`)
- Modify: `VisualGameStudio.Tests/Compiler/PreprocessorConditionalTests.cs` (Edit)

- [ ] **Step 1: Failing test** (append):

```csharp
    /// <summary>S1 — the parser never built PreprocessorIfNode; #If is the preprocessor's (Task 1). A second,
    /// parse-time conditional path would be a mirrored pair of the first.</summary>
    [Test]
    public void TheDormantParseTimeIfNode_IsGone() =>
        Assert.That(typeof(BasicLang.Compiler.AST.ASTNode).Assembly.GetType("BasicLang.Compiler.AST.PreprocessorIfNode"),
            Is.Null);
```
- [ ] **Step 2: Run — RED** (the type exists).
- [ ] **Step 3: Delete** the two node classes, the interface member and the three `Visit` methods. Grep `PreprocessorIfNode|PreprocessorElseIfClause` across `BasicLang/` and `VisualGameStudio.Tests/` — zero hits. Keep `TokenType.PreprocessorIf*` (S1; `LexerTests.Lexer_PreprocessorIf` stays green).
- [ ] **Step 4: Build + run GREEN**, plus `LexerTests`, `ASTPrettyPrinter*` tests (grep).
- [ ] **Step 5: Commit.** Message: "Remove the unreachable parse-time #If node (the preprocessor owns #If); keep the lexer tokens".

---

## Task 6: The editor — `#If` in the LSP, line for line, and web projects' declarations

O18, spec §4.12. Scope call S3.

**Files:**
- Modify: `BasicLang/Preprocessor.cs` (add `ProcessForEditor`)
- Modify: `BasicLang/LSP/LspProjectContext.cs` (`LspProjectContext` `:19-66` gains `TargetBackend`; `GetBlprojSourceFiles` `:369-405` records `projectFile.Backend`; `ParseCached` `:436-460`)
- Modify: `BasicLang/LSP/DocumentManager.cs` (`DocumentState.Parse` `:507-551`)
- Create: `VisualGameStudio.Tests/LSP/LspConditionalCompilationTests.cs`

- [x] **Step 1: Failing tests.**

```csharp
using System;
using System.IO;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.LSP;
using NUnit.Framework;
using OmniSharp.Extensions.LanguageServer.Protocol;

namespace VisualGameStudio.Tests.LSP;

/// <summary>
/// O18 / spec §4.12 — the editor sees what the build compiles: the inactive branch of an #If is blanked LINE FOR
/// LINE (so every reported position is the user's), and a web project's files know the DOM declarations.
/// Measured before: the LSP had no preprocessor at all (no "Preprocessor" in BasicLang/LSP/) and no backend.
/// </summary>
[TestFixture]
public class LspConditionalCompilationTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp() => _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
        "bl-lspif-" + Path.GetRandomFileName())).FullName;

    [TearDown]
    public void TearDown() { try { Directory.Delete(_dir, true); } catch { } }

    // Line 5 is a type error that exists only in the DESKTOP branch.
    private const string Source =
        "Module M\n" +                               // 1
        "Sub Main()\n" +                             // 2
        "#If WEB Then\n" +                           // 3
        "    Dim a As Integer = 1\n" +               // 4
        "#Else\n" +                                  // 5
        "    Dim b As Integer = \"not a number\"\n" +// 6
        "#End If\n" +                                // 7
        "End Sub\n" +                                // 8
        "End Module\n";

    private DocumentState Open(string backend)
    {
        File.WriteAllText(Path.Combine(_dir, "App.blproj"),
            $"<Project>\n  <PropertyGroup>\n    <ProjectName>App</ProjectName>\n    <TargetBackend>{backend}</TargetBackend>\n" +
            "  </PropertyGroup>\n  <ItemGroup>\n    <Compile Include=\"Main.bas\" />\n  </ItemGroup>\n</Project>\n");
        var path = Path.Combine(_dir, "Main.bas");
        File.WriteAllText(path, Source);
        return new DocumentManager().UpdateDocument(DocumentUri.FromFileSystemPath(path), Source);
    }

    [Test]
    public void AWebProject_ReportsNothing_FromTheDesktopBranch() =>
        Assert.That(Open("JavaScript").Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty);

    [Test]
    public void ADesktopProject_ReportsTheDesktopBranch_AtItsOwnLine()
    {
        var errors = Open("CSharp").Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(errors, Has.Count.EqualTo(1), string.Join(" | ", errors.Select(e => e.Message)));
            Assert.That(errors[0].Line, Is.EqualTo(6), "blanking keeps every line where it was");
        });
    }

    [Test]
    public void TheEditorMode_BlanksDirectivesAndInactiveLines_AndKeepsTheLineCount()
    {
        var pre = new Preprocessor();
        pre.Define("WEB");
        var blanked = pre.ProcessForEditor(Source, "Main.bas").Replace("\r\n", "\n").Split('\n');
        var original = Source.Split('\n');
        Assert.Multiple(() =>
        {
            Assert.That(blanked.Length, Is.EqualTo(original.Length));
            Assert.That(blanked[2], Is.Empty, "#If");
            Assert.That(blanked[3], Is.EqualTo(original[3]), "the active branch is kept verbatim");
            Assert.That(blanked[5], Is.Empty, "the inactive branch is blank");
            Assert.That(blanked[6], Is.Empty, "#End If");
        });
    }

    /// <summary>A web project's files see dom-core.bli's Document/Element, as the web build does (K1).</summary>
    [Test]
    public void AWebProject_KnowsTheDomDeclarations()
    {
        const string dom = "Module M\nSub Main()\nDim d As Document = ::document\nDim e As Element = d.getElementById(\"x\")\n" +
                           "e.textContent = \"a\"\nEnd Sub\nEnd Module\n";
        File.WriteAllText(Path.Combine(_dir, "App.blproj"),
            "<Project>\n  <PropertyGroup>\n    <ProjectName>App</ProjectName>\n    <TargetBackend>JavaScript</TargetBackend>\n" +
            "  </PropertyGroup>\n  <ItemGroup>\n    <Compile Include=\"Main.bas\" />\n  </ItemGroup>\n</Project>\n");
        var path = Path.Combine(_dir, "Main.bas");
        File.WriteAllText(path, dom);
        var state = new DocumentManager().UpdateDocument(DocumentUri.FromFileSystemPath(path), dom);
        Assert.That(state.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty,
            string.Join(" | ", state.Diagnostics.Select(d => d.Message)));
    }
}
```
  ⚠ Check the `DocumentState`/`Diagnostic` type names and namespaces against `CrossFileAnalysisTests.cs:60-104` (the existing in-process LSP harness) and adjust the usings to match; `Diagnostic.Line` is 1-based there (`DocumentManager.cs:597-598`).
- [x] **Step 2: Run — RED:** the web test reports the desktop branch's error (and a parse error on `#If`); `ProcessForEditor` does not exist (compile error — the red for that test); the DOM test may already be red with unknown `Document`/`Element` (record which).
- [x] **Step 3: Implement `ProcessForEditor`.** In `Preprocessor`, add a private `bool _editorMode` and:

```csharp
        /// <summary>
        /// The editor's view of <paramref name="source"/> (spec §4.12, O18): the SAME directive walker as
        /// <see cref="Process"/>, but every directive line and every inactive line becomes an EMPTY line and
        /// <c>#Include</c> is never spliced — so the result has exactly the source's lines, and every position the
        /// language server reports is the user's own.
        /// </summary>
        public string ProcessForEditor(string source, string filePath)
        {
            _editorMode = true;
            try { return Process(source, filePath); }
            finally { _editorMode = false; }
        }
```
  In `ProcessCore`, every `result.AppendLine($"' {line}")` for a directive, the `[IFDEF SKIP]` line and the `#Include` arm write `result.AppendLine(_editorMode ? "" : …)`; in editor mode the `#Include` arm never calls `ProcessInclude`.
- [x] **Step 4: The LSP uses it.**
  - `LspProjectContext`: add `public string TargetBackend { get; }` (constructor parameter with default `null`, stored), and populate it where the `.blproj` is loaded (`GetBlprojSourceFiles`, `ProjectFile.Load(blprojPath)` at `:382` — keep the `Backend` beside the source list in `BlprojSnapshot`). An implicit (no `.blproj`) project: null → `DESKTOP`.
  - A helper in `LspProjectContext`: `public string Preprocess(string content, string path)` → `var pre = new Preprocessor(); foreach (var s in BuildSymbols.For(TargetBackend, "Debug", null)) pre.Define(s); return pre.ProcessForEditor(content, path);`. The editor is a Debug view **[impl]**.
  - `DocumentManager.DocumentState.Parse` (`:518`): `var text = LspProjectContext.PreprocessFor(ProjectContext, Content, FilePath);` — a static helper that uses the context's backend, or, for a LOOSE file (null `ProjectContext`), `BuildSymbols.For(null, "Debug", null)` (DESKTOP, DEBUG) — then `new Lexer(text)` and `ImplicitContainer.Parse(parser, FilePath ?? Uri?.Path, text)`. ⚠ `Content` stays the user's text (completion, hover and formatting read it); only the lexer sees the blanked copy.
  - **Dimming — decided [impl]: inactive branches are dimmed, as Visual Studio does.** `ProcessForEditor` also records the inactive line ranges (`Preprocessor.InactiveLines`, set by the editor mode); `DocumentState` keeps them; `SemanticTokensHandler` reports each inactive line as one `comment` token (every theme dims comments — no client extension needed). Add to Step 1 the test `AnInactiveBranch_IsReportedAsCommentTokens` (drive `SemanticTokensHandler` the way its existing tests do — grep `SemanticTokensHandler` in `VisualGameStudio.Tests/LSP`) and `ALooseFile_IsPreprocessedAsADesktopDebugBuild` (a `DocumentManager.UpdateDocument` on a file with no `.blproj` above it: the `#If DESKTOP` branch is analyzed, the `#Else` is not). The IDE's own editor (VisualGameStudio.Editor) is out of scope for dimming here — record it as a follow-up.
  - `LspProjectContext.ParseCached` (`:445`): the same `Preprocess` before `new Lexer`.
  - Web projects: when `BuildSymbols.IsWebBackend(TargetBackend)` and `File.Exists(BasicCompiler.DomDeclarationsPath)`, add that path to the project's source list (the sibling symbol table then carries `Document`/`Element`), exactly as `WithJavaScriptDeclarations` does for the build (`Compiler.cs:550-559`). The portable library joins here in Task 34.
- [x] **Step 5: Run — GREEN**, plus the LSP suite by name: `CrossFileAnalysisTests`, `LspMixedProjectTests`, `ModClsDocumentTests`, `CompletionServiceTests`, `BaseConstructorCallDiagnosticsTests`.
- [x] **Step 6: Mutations:** (1) editor mode writes the `' …` comment instead of `""` for a skipped line → the blanking test red (a comment is not blank) — then check the diagnostic line test also still guards line numbers; (2) `ProcessForEditor` splices includes → add a `[Test]` with an include and assert it is not spliced if no test catches it; (3) `Preprocess` ignores `TargetBackend` → the web test red.
- [x] **Step 7: Commit.**

### Task 6 RECORD (base `d1234b62`)

- **Steps 1–7 done.** `LspConditionalCompilationTests`: 15 cases (the plan's 4 + the two Step-4 tests + 9 more below). RED: compile error (`ProcessForEditor`/`InactiveLines` missing); with the preprocessor half only, 10/13 red — the web project reported the desktop branch's `Error(6,5)`, the DOM test `Error(4,1): Cannot assign value of type 'Object' to variable of type 'Element'` (so the DOM test WAS red before), no comment tokens, no preprocessor diagnostics. GREEN 15/15, 0 skipped.
- **Mutations, all killed:** (1) skipped line written as `' [IFDEF SKIP]` in editor mode → blanking + comment-token tests; (1b) skipped line DROPPED in editor mode → both line tests, the published-range test and both loose-file tests (the line guard holds); (2) editor splices `#Include` → `TheEditorMode_NeverSplicesAnInclude` (added — nothing else caught it); (3) symbols ignore the backend → web, comment-token and retarget tests; (4) DOM declarations ungated → `ADesktopProject_DoesNotSeeTheDomDeclarations`; (4b) never added → `AWebProject_KnowsTheDomDeclarations`; (5) no inactive-line tokens → comment-token test; (6) preprocessor diagnostics dropped on analysis → `AMalformedDirective_IsReportedAtItsLine`; (7) the document cache ignores the symbol key → `RetargetingTheProject_ReblanksAnUnchangedDocument`; (8) siblings lexed raw → `ASiblingFile_IsPreprocessedWithTheProjectsSymbols(CSharp)`. (4b–7 ran as one build; exactly their four tests failed.)
- **Deviations (each minimal):** (a) the editor symbols include the project's **Debug `<DefineConstants>`** (`BuildSymbols.For(backend, "Debug", debugDefines)`), not `null` — exactly what the debugger route compiles (`DebugSession.CompilerOptionsForDebugging`); with `null`, a `#If TRACE` the Debug build compiles would be dimmed. (b) `Preprocess` returns an `EditorSource` (text + inactive lines + errors), not a string. (c) The preprocessor's **errors are reported** as editor diagnostics (the build fails on them, `Compiler.cs:598`). (d) The symbol key is part of the project stamp, the sibling AST cache, the document parse cache and the document short-circuit — without it a retargeted project kept its old parse. (e) Editor output drops the trailing line break `AppendLine` adds, so the line count equals the source's. (f) `InactiveLines` dims a conditional directive only when its block sits inside a dead branch (VS does not dim a live block's directives).
- **Plan text found false:** "drive `SemanticTokensHandler` the way its existing tests do" — there were none; the test calls the handler's public `Handle(SemanticTokensParams)` and decodes the wire data.
- **Measured in passing (not fixed, pre-existing):** the LSP types a cross-file module `Function … As Integer` as `Object` at the call site (`Cannot assign value of type 'Object' to … 'Integer'`), whichever branch declares it and with no `#If` at all; `As String` resolves. The sibling test reads the project table instead.
- **Follow-up (out of scope, recorded):** the IDE's own editor (VisualGameStudio.Editor) dimming of inactive branches is unverified — the server now reports them as `comment` semantic tokens; whether the IDE requests and paints them is not checked here.
- **Seam for Tasks 28/34:** `LspProjectContextProvider.WebDeclarationFiles()` — the portable library's files join that list; `WithWebDeclarations` gates it on the backend.
- **Review follow-ups (commit after `30d8b890`):** a CRLF case of `AnInactiveBranch_IsReportedAsCommentTokens` (the mutant dropping `TrimEnd('\r')` in `PushInactiveLine` survived every LF source; now red: length 38 vs 37); completion on an inactive line offers nothing (`CompletionService.GetCompletions` → `DocumentState.IsInactiveLine`; was 86 `Console` members; mutant red), test `CompletionOnAnInactiveLine_OffersNothing` through the real `CompletionHandler`.
- **Further follow-ups (recorded, not done):** (a) directive lines of a LIVE block get no semantic tokens (they are blank for the lexer) — the client's grammar colours them; (b) `dom-core.bli` sits in the web project's `SourceFiles` — when rename / find-references / definition become project-wide they must exclude declaration files (`BasicCompiler.IsDeclarationFile`); (c) a file with bare `\r` line endings would desync `DocumentState.Lines` (split on `\n`) from the preprocessor (splits on `\r\n`/`\r`/`\n`) — negligible.

---

## Task 7: Cross-file `Inherits`, on every backend

Spec §4.2, M6, chip `task_e7af351e` items 1–2 (S12). Built ON `fix/js-cross-file-calls` (`098b4de9` [X]): sibling class shells already get their `BaseType` in pass 2 (`SemanticAnalyzer.cs:≈604-630` [X]); what is left is that `Visit(ClassNode)` and the pass-1 `RegisterClassBases` look the base up in the type manager ONLY, where a pending sibling's class is not.

**Files:**
- Modify: `BasicLang/SemanticAnalyzer.cs` — `Visit(ClassNode)`'s base block (`≈6421` [X]; `:6060-6098` on `bc29391e`; quote: `var baseType = _typeManager.GetType(node.BaseClass);`) and `RegisterClassBases` (`:5593-5630` on `bc29391e`; the same lookup)
- Modify: `VisualGameStudio.Tests/Compiler/CrossFileBindingTests.cs` (Edit — S11)

- [ ] **Step 1: Failing tests** — append a region to `CrossFileBindingTests` (it is `[Category("Integration")]`, `[NonParallelizable]`; `RunsOnEveryBackend` compiles both file orders and runs C#, JS and C++):

```csharp
    // ------------------------------------------------------------------ item 4: Inherits across files
    // Portable-controls plan Task 7 (spec §4.2, M6). Measured on bc29391e AND 40e9c172: "Unknown base class
    // 'Base'" + "MyBase can only be used in a class that inherits from another class" + the upcast refused, on
    // JavaScript and C# alike. With any Using line the base silently became an opaque .NET class instead.

    private const string BaseFile =
        "Public Class Base\n Public Overridable Function Hi() As String\n  Return \"base\"\n End Function\n" +
        " Public Sub Hello()\n  PrintLine(\"hello from base\")\n End Sub\nEnd Class\n";

    private const string DerivedFile =
        "Public Class D\n Inherits Base\n Public Overrides Function Hi() As String\n  Return \"D:\" & MyBase.Hi()\n" +
        " End Function\n Public Sub Greet()\n  Me.Hello()\n End Sub\nEnd Class\n";

    private const string UseBaseAndDerived =
        "Module Program\n Sub Main()\n  Dim x As Base = New D()\n  PrintLine(x.Hi())\n  Dim d As New D()\n  d.Greet()\n End Sub\nEnd Module\n";

    [Test]
    public void AClassInheritsAClassFromAnotherFile() => RunsOnEveryBackend("D:base\nhello from base",
        ("Base.bas", BaseFile), ("Derived.bas", DerivedFile), ("Main.bas", UseBaseAndDerived));

    /// <summary>⛔ The Using shape: the analyzer's "unresolved base + a .NET Using = an opaque .NET class" must not win
    /// over a sibling's real class. The WinForms scaffold always has Using lines (FormScaffolder.cs:201-212).</summary>
    [Test]
    public void AClassInheritsAClassFromAnotherFile_UnderAUsing() => RunsOnEveryBackend("D:base\nhello from base",
        ("Base.bas", BaseFile), ("Derived.bas", "Using System\n" + DerivedFile), ("Main.bas", UseBaseAndDerived));

    [Test]
    public void AThreeLevelChain_SplitOverThreeFiles() => RunsOnEveryBackend("C>B>A",
        ("A.bas", "Public Class A\n Public Overridable Function Name() As String\n  Return \"A\"\n End Function\nEnd Class\n"),
        ("B.bas", "Public Class B\n Inherits A\n Public Overrides Function Name() As String\n  Return \"B>\" & MyBase.Name()\n End Function\nEnd Class\n"),
        ("C.bas", "Public Class C\n Inherits B\n Public Overrides Function Name() As String\n  Return \"C>\" & MyBase.Name()\n End Function\nEnd Class\n"),
        ("Main.bas", "Module Program\n Sub Main()\n  Dim a As A = New C()\n  PrintLine(a.Name())\n End Sub\nEnd Module\n"));
```
  Also a CLI leg and an IDE leg (both entry points, CLAUDE.md): copy the body of `TheCli_BuildsAndRunsAJavaScriptProject_ThatCrossesFiles` (`:149` [X]) into `TheCli_BuildsAndRunsAJavaScriptProject_WithACrossFileBase` using the three files above, and add `TheIde_BuildsAJavaScriptProject_WithACrossFileBase` driving `BuildService.BuildProjectAsync` (as Task 3's IDE test does) and running `result.GeneratedCode` under node. Expected output in both: `D:base\nhello from base`.
- [ ] **Step 2: Run — RED** (`--filter "FullyQualifiedName~CrossFileBindingTests"`): the new rows fail at `Assert.That(result.HasErrors, Is.False, …)` with `Unknown base class 'Base'`; the Using row fails later (C# `CS0246`/JS `ReferenceError`, the opaque-.NET path). The existing 17 stay green.
- [ ] **Step 3: Implement.** In both places replace the lookup:

```csharp
                // ⛔ A base declared in ANOTHER file is a sibling SHELL in GlobalScope (RegisterSiblingClassShell),
                // not a type-manager entry, until its own unit is analyzed. ResolveTypeSymbol (40e9c172) finds a
                // TYPE symbol through the scopes — values of the same name are skipped — so the sibling's class is
                // found before the "unresolved + .NET Using = opaque .NET class" fallback below can claim it.
                var baseType = ResolveTypeSymbol(node.BaseClass)?.Type ?? _typeManager.GetType(node.BaseClass);
```
  (`RegisterClassBases` uses its own node variable name — keep it.) The cycle check (`InheritanceWouldCycle`) and the "is not a class" check run unchanged on the found type.
- [ ] **Step 4: Run — GREEN** (all 17 + 5 new). Then `ForwardDeclaredTypeTests`, `ForwardDeclaredTypeExecutionTests`, `InheritedMemberTests`, `OverridablePropertyTests`, `NetBuildPipelineTests` by name.
- [ ] **Step 5: Mutations:** (1) revert the lookup in `Visit(ClassNode)` only → the rows go red (record whether `RegisterClassBases` alone rescues any); (2) put the `_netNamespaces` fallback BEFORE the sibling lookup → the Using row red.
- [ ] **Step 6: Commit.** Message names chip `task_e7af351e` items 1–2 as closed (item 2: already fixed on master by `27e8307c`; the cross-file order is now pinned by `AThreeLevelChain_…` in both file orders).

**Task 7 follow-ups (recorded by the Task 7 review, 2026-09-30 — not fixed here):**
- `Implements` an interface declared in ANOTHER file → "Unknown interface" (the interface counterpart of this task: `Visit(ClassNode)`'s interface loop and `RegisterClassBases` still read `_typeManager.GetType(interfaceName)` only).
- `Inherits Box(Of Integer)` (a generic base) and a `MustOverride Function` without a body do not parse — pre-existing parser gaps, independent of files.
- ~~A bare call that a class member (own or inherited) shadows is now EMITTED as the member on every backend (`ProcedureCallTarget` + `IRCall.CalleeModule`), but the ANALYZER still binds the bare name to the Module's symbol when the member is inherited, so a same-named Module `Function` with a different signature would type (and check arguments of) the call by the Module's declaration. Same-signature Subs are correct today.~~ **FIXED by Task 7b**: `SemanticAnalyzer.ClassScopeCallee` binds a bare call inside a class by the class chain before module scope, through `SemanticAnalyzer.ClassScopeProcedure` — the ONE predicate the IR builder's `IsCurrentClassProcedure` now delegates to.
- ⛔ **Before 2a — the name-based cycle check will false-positive.** `InheritanceWouldCycle` matches classes by NAME (sound only while class names are unique per project). Once the library's BasicLang `System.Windows.Forms` classes exist, a user `Class Button Inherits Button` (a user class over the library's `System.Windows.Forms.Button`) is a legal chain the check calls "cannot inherit from itself". Piece 2 must give it a namespace-qualified identity before 2a lands the library classes.
- **Task 7c (2026-10-04) — the Task 7b review items, following VB:**
  - ✅ A delegate LOCAL / PARAMETER / LAMBDA PARAMETER is now invoked as a VALUE (`IRBuilder.IsDelegateValueSymbol` → `EmitDelegateValueInvocation`, ADR-0010 D8) — the JS defect below is FIXED on every backend.
  - ✅ ANY member kind shadows a Module procedure (`SemanticAnalyzer.ClassScopeMember`, still the one predicate the IR builder reads). `P()` on a parameterless property is its read; an argument list on a scalar (numeric/Boolean/Char/Date/Enum) field, property, constant, local or parameter is **BC30471**.
  - ✅ An instance member named bare from a `Shared` method, or an enclosing class's instance member from a nested class, is **BC30469** (no fall-through to the Module; no misleading argument count). `Symbol.IsShared` is now set on every class method/Sub/field symbol (pass 2, pass-1/sibling signature, `LspProjectContext`) so a base in another file is judged too.
  - ✅ A member of a RESOLVED .NET base (`NetBaseDeclaresMember`, resolver armed) shadows a Module procedure: no symbol, the bare call goes to the target compiler.
  - ⛔ **NOT fixed — WinForms.** `EnableNetResolution` returns early for `UseWindowsForms`, so `Form`'s members are unknowable here and `Close()` in `Class F1 Inherits Form` beside a Module `Close` still binds the Module's. Fixing it needs the desktop reference set in the resolver closure (or a WinForms member list) — a decision for piece 2, which will own `System.Windows.Forms` as BasicLang classes anyway (then `ClassScopeMember` sees their members with no change).
  - ⚠ Residual: a bare NON-call reference (`n = Value`, no parens) still resolves lexically first (`ResolveBareName` → pass 1's flattened global copy) and can reach a Module FUNCTION before a class member of that name. Same rule, different entry point.
  - ⚠ Pre-existing, seen while testing: `D.S()` (a Shared Sub of class D) called from ANOTHER file emits `Derived.S()` on C# (the file's name, CS0103).
- **Task 7d (2026-10-04) — the Task 7c review, following VB:**
  - ✅ **VB-qualified built-ins**: `Strings.Left(…)`, `Microsoft.VisualBasic.Left(…)`, `Microsoft.VisualBasic.Strings.Left(…)` for every built-in a `Microsoft.VisualBasic` module declares with BasicLang's SHAPE (`VbIntrinsicQualifiers`, ENUMERATED by reflection, pinned by `VbQualifiedIntrinsicTests`: 23 today — Strings.Len/Mid/Left/Right/UCase/LCase/Trim/InStr/Replace, Conversion.Str/Val, Interaction.Beep, Information.LBound/UBound, VBMath.Randomize, DateAndTime.Year/Month/Day/Hour/Minute/Second, FileSystem.FileCopy/FileLen). Lowered as the bare built-in on every backend. `Shell` is excluded by name (shape matches, meaning does not: exit code vs process id). `Chr`, `Asc`, `Format` are NOT in BasicLang's built-in table, so they have no qualified spelling either (a table change, not this rule). Bare `Left(…)` under a member `Left` stays BC30471 and now names the way out ("use Strings.Left(…)"). ⚠ The backends still let ANY user PROCEDURE named like a built-in shadow it program-wide (`_userFunctionNames`), the qualified spelling included — pre-existing.
  - ✅ **Review round 2 (f81acb62 review):** a qualified built-in now carries `IRCall.IsIntrinsic`, and C#, JavaScript and C++ each take their built-in table FIRST for it (no class-member or user-function re-binding; refused when no row exists) — before, a class METHOD `Len()`/`Trim()`/`Left()` captured `Strings.X` (JS printed 99 silently, C# CS1501) and a user `Function Left` captured it program-wide. VB `Val`/`Str` now lower on all three backends (C#: `Microsoft.VisualBasic.Conversion`; JS: inline; C++: `BasicLang::VbVal`/`VbStr`). A bare `Left(…)` beside a same-file `Module … Function Left` now binds the user's (it bound the built-in and C# emitted an unqualified CS0103 call). ⚠ The same bare call to a user procedure in a SIBLING FILE still binds the built-in: the sibling's signature is first-wins flattened into the global scope, where the built-in already is (`RegisterSiblingFunctionSignature`).
  - ⚠ What the qualifiable rule deliberately does NOT do (beside `Shell`): ARITY follows BasicLang's signatures, not VB's overloads — `Strings.Mid(s, 2)` and `Strings.InStr(1, s, "l")` are refused because BasicLang's `Mid`/`InStr` have one shape; `Rnd` is excluded (VB returns Single, BasicLang Double); `Now`/`Today` are excluded (VB properties, not methods).
  - ✅ Shared `Sub New` is a Shared context (BC30469). ✅ An event called bare is BC32022. ✅ LSP rows pin `IsShared` for Function/Sub/field/property. ✅ A Shared accessor calling a Shared sibling RUNS (C#, JS).
  - ✅ `AccessorMemberOf` uses `ClassScopeMember` (one predicate). The IR builder's method is `IsCurrentClassScopeMember` (`IsCurrentClassMember` was already taken by the IR-class field/property check). `NetBaseDeclaresMember` goes on past an unresolved link (not observable from a test today — a resolved .NET base is the end of the modelled chain).
  - ⚠ Pre-existing, seen while testing: C++ `Left(s, n)` lowers to `s.substr(…)`, which does not compile on a bare LITERAL (`"hello".substr`); `Box.P` from a sibling file named `Box.bas` reads Box as the file's module.
- **Task 7d proper (2026-10-05) — WinForms .NET resolution:** `EnableNetResolution` now ARMS a `UseWindowsForms` project where the WindowsDesktop reference pack is installed (`NetReferenceResolver.WindowsDesktopAssemblies`: `packs\Microsoft.WindowsDesktop.App.Ref\<runtime ver or highest same M.m>\ref\netM.m`, Windows only; `WithWindowsDesktop` lets the desktop copies of System.Drawing/WindowsBase/Microsoft.VisualBasic win). Off Windows, or with no pack, it stays un-armed as before (WPF too). Measured first: `GetMembers` (the CALL surface) excludes events and protected members, so a missing-member ERROR asks the new `NetTypeResolver.DeclaresNameableMember` (any non-private kind incl. events/nested types; protected only from a derived class; extension methods for `x.Name`, never for a bare name). Effects: a bare `Close()` in `Inherits Form` is Form.Close (`NetBaseOf` also follows a base kept only as a NAME); a misspelled member on a WinForms receiver (`b.Textt`) or through `Me`/`MyBase` in a Form subclass is BL6017 as an ERROR (other .NET types keep §6.3's warning row). Oracle: every catalog control with every property + its default event, compiled ARMED, has no error (`WinFormsNetResolutionTests`). ⚠ Not covered: a BARE misspelled inherited member (`Textt = "x"` with no `Me.`) still takes the permissive bare-identifier arm; the LSP does not arm WinForms (editor shows the error only on build).
- **Task 7e DONE (2026-10-05)** — each RUN on C#, JavaScript and C++ (`RuntimeGapsTask7eTests`); the defects were not JS-only (C# failed 3 of 4 too):
  - ✅ Nested classes: `Outer.Inner` in a type position / `New Outer.Inner()` binds the nested class, which every backend emits flat under its own name (`SemanticAnalyzer.NestedUserClass`; IR `New`). ⚠ Two nested classes of one name in different outers still collide — the flat emission is pre-existing.
  - ✅ The function-name return variable: `F = …` / `F = F + i` in F's body is a hidden local `F_FnResult`, returned on falling off the end and by `Exit Function` (which returned NOTHING from a Function before).
  - ✅ `s(i)` on a String value (property, field, local, parameter) is VB's `Chars(i)`, lowered as the intrinsic one-character `Mid` — ⚠ typed String, not Char, until every backend has Char (JS in 2.0b).
  - ✅ C++ `Left/Right/Mid/Len/InStr` on a string LITERAL (receiver wrapped in `std::string(...)`); C++ `Beep`/`FileCopy`/`FileLen` lowered (`BasicLang::VbBeep/VbFileCopy/VbFileLen`, C stdio).
  - ✅ Review round (2026-10-05): `p.Name(1)` / `obj.Text(0)` through a member is Chars too; Chars is its own intrinsic (`IRBuilder.StringCharsIntrinsic`) that THROWS past the end on every backend (C# indexer, JS checked index, C++ `.at`); `Exit Function` with no name assignment returns the default (pinned); the shared resolver cache is keyed by path set and REPLACES on a changed stamp; WinForms behaviour keys on the project's `UseWindowsForms` (`CompilerOptions.NetResolutionIsWinForms`); C++ `VbFileLen` is 64-bit.
  - ⚠ NOT matched to VB: a `Finally` that assigns the function name after `Exit Function` — VB returns the value as of AFTER the Finally (2), every backend here returns it as of the Exit (1), because the return value is evaluated before the handler runs (C#/JS `return x;` semantics; C++'s copied Finally runs before the `return` but reads the variable first). Fixing it means returning through a label after the Finally; recorded, not done.
  - ⚠ Seen, not fixed: an auto-property INITIALIZER (`Property Name As String = "abc"`) does not parse.
- **Task 7e — JavaScript runtime failures the 7c reviewer measured, all on the baseline too (next after 7d). Repros in scratchpad `probes7c\`:**
  1. **Nested classes**: `Outer.Inner` → `ReferenceError: OuterInner is not defined` (`p04_nested.bas`: `Dim i As New Outer.Inner()`).
  2. **The function-name return variable**: `SFact = 1` / `SFact = n * SFact(n - 1)` in a Shared Function (`p11_recursion.bas`), and `Total = Total + i` accumulating into the function's own name — ReferenceError / wrong result.
  4. **C++ bare `Beep()`/`FileCopy(…)`/`FileLen(…)`** emit calls nothing defines (a late native-compile failure); and C++ `Left("literal", n)` lowers to `"literal".substr` (const char[]).
  5. Recorded by the 7d review, pre-existing, not 7e's scope unless cheap: `WithEvents`/`Handles` do not parse; `Controls.Item(0)` types Object; enum `Or` (`AnchorStyles.Top Or AnchorStyles.Left`) is "Logical operator 'Or' requires Boolean operands"; `MessageBox.Show(…)`'s DialogResult types Object.
  6. Task 7d round 2 (2026-10-05): an armed WinForms project now adds NO diagnostic to a valid program — protected members are nameable through MyBase/Me and from any class over a .NET base (`MyBase.OnPaint(e)` was a false BL6017 error), and the §6.5 C#-path WARNING row is silent when WinForms-armed (Object-typed receivers, `Application.Run(New Form1())` BL6019); resolvers for a closure are shared per process (`NetTypeResolver.CreateShared`: ~390 ms → ~35 ms per repeat compile, measured in-process on the reviewer's form); pack discovery falls back to the NuGet cache.
  3. **`Name(0)` on a String property** (`p18_prop_paren.bas`: `Return Name() & Size().ToString() & Name(0)`) — wrong result (VB: `Name(0)` is the String's default `Chars(0)`, "a").
- ~~⚠ **JavaScript calls a class method for a bare call to a delegate PARAMETER or LOCAL of the same name**~~ (fixed by Task 7c) (Task 7b, measured): `Sub Greet(Hello As Func(Of Integer, Integer))` in `D Inherits Base` (Base has `Hello()`), `PrintLine(Hello(3))` emits `this.Hello(3)` and prints Base's 5. The analyzer binds the parameter (pinned by `CrossFileBindingTests.AParameterIsNearerThanTheClass_InTheAnalyzer`); the IR builder hands the call to `EmitProcedureCall` with the parameter symbol and no owner, and `JavaScriptBackend.CallTarget`'s `MethodReference` resolves the bare name against the class before anything else. Fix shape: lower a call whose callee symbol is a delegate-typed local/parameter as a delegate-value invocation (`EmitDelegateValueInvocation`), or tell the backend the call is not a member call.

---

## Task 8: C# — the file-named static class never collides with a user type or member

Chip `task_e7af351e` items 3–4 (S12). A form class lives in a file of its own name, so this shape is the shared code-behind's.

- ✅ Done in 5a927bde + review follow-up: "Main" → "Program" runs the same collision check (Main.bas beside a user
  `Class Program` → `ProgramModule`), and the `{Name}Module` spelling is itself checked against types, the container's
  members and every other container, taking a numeric suffix (`ToolsModule2`) until free.
- ⚠ Cosmetic, recorded not fixed: a renamed container shows in the debugger's frames as `GreetModule.Greet`
  (`SourceMapper` maps C# frame names back by the container's spelling).

**Files:**
- Modify: `BasicLang/CSharpBackend.cs` — `standaloneFunctions` (`:414-416`, quote `.Where(f => !f.IsExternal && !IsClassMethod(f, module))`); the per-module class name (`:494-500`, quote `if (className.Equals("Main", StringComparison.OrdinalIgnoreCase))`); `ModuleClassName` (`:3450-3452`)
- Create: `VisualGameStudio.Tests/Compiler/CsFileNamedContainerTests.cs`

- [ ] **Step 1: Failing tests.**

```csharp
using System.IO;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.IR.Optimization;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Chip task_e7af351e items 3–4. The C# backend puts a file's top-level procedures in a static class named after the
/// FILE (CSharpBackend.cs:474-500). (3) A lambda inside a class is a standalone IR function whose ModuleName is the
/// file's (IRBuilder.cs:2485), so a file LoginForm.bas holding Class LoginForm with a lambda emitted an EMPTY
/// `static class LoginForm` beside the user's class: CS0101. (4) A Sub named like its file became a member named like
/// its enclosing class: CS0542. Only "Main" was special-cased (→ "Program").
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class CsFileNamedContainerTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp() => _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
        "bl-csname-" + Path.GetRandomFileName())).FullName;

    [TearDown]
    public void TearDown() { try { Directory.Delete(_dir, true); } catch { } }

    private string RunCSharp(params (string Name, string Text)[] files)
    {
        var paths = files.Select(f => { var p = Path.Combine(_dir, f.Name); File.WriteAllText(p, f.Text); return p; }).ToArray();
        var result = new BasicCompiler(new CompilerOptions { TargetBackend = "csharp" }).CompileProjectFiles(paths);
        Assert.That(result.HasErrors, Is.False, string.Join(" | ", result.AllErrors.Select(e => e.Message)));
        var pipeline = new OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.Run(result.CombinedIR!);
        return FourBackends.Norm(FourBackends.RunEmittedCSharpText(new CSharpCodeGenerator().Generate(result.CombinedIR!)));
    }

    [Test]
    public void AClassWithALambda_InAFileOfItsOwnName() => Assert.That(RunCSharp(
        ("LoginForm.bas", "Public Class LoginForm\n Public Function Run() As Integer\n" +
                          "  Dim f As Func(Of Integer, Integer) = Function(x As Integer) x * 2\n  Return f(21)\n End Function\nEnd Class\n"),
        ("Main.bas", "Module Program\n Sub Main()\n  Dim l As New LoginForm()\n  PrintLine(l.Run())\n End Sub\nEnd Module\n")),
        Is.EqualTo("42"));

    [Test]
    public void ASubNamedLikeItsFile_IsCallable() => Assert.That(RunCSharp(
        ("Greet.bas", "Sub Greet()\n PrintLine(\"hi\")\nEnd Sub\n"),
        ("Main.bas", "Module Program\n Sub Main()\n  Greet()\n End Sub\nEnd Module\n")),
        Is.EqualTo("hi"));

    [Test]
    public void AUserClassNamedLikeAFileWithProcedures_KeepsItsName() => Assert.That(RunCSharp(
        ("Tools.bas", "Public Class Tools\n Public Function N() As Integer\n  Return 7\n End Function\nEnd Class\n" +
                      "Sub Helper()\n PrintLine(New Tools().N())\nEnd Sub\n"),
        ("Main.bas", "Module Program\n Sub Main()\n  Helper()\n End Sub\nEnd Module\n")),
        Is.EqualTo("7"));
}
```
- [ ] **Step 2: Run — RED:** `the emitted C# does not compile: … CS0101 …` (rows 1 and 3) and `… CS0542 …` (row 2).
- [ ] **Step 3: Implement.**
  - `:414-416`: `.Where(f => !f.IsExternal && !f.IsLambda && !IsClassMethod(f, module))` — a lambda is emitted inline (`GenerateFunction` returns early for it, `:1730-1733`), so grouping it only ever produced an empty class.
  - `ModuleClassName` becomes the ONE container-naming rule, used by the declaration too:

```csharp
        /// <summary>
        /// ⛔ THE container name for a Module / file's procedures — the declaration (the per-module loop) and every
        /// qualified use (globals, calls) read it, so they cannot disagree. "Main" is "Program" (a method may not be
        /// named like its class); so is any other name that collides with a TYPE this module declares or with a
        /// PROCEDURE/GLOBAL of the container itself: that container is spelled "{Name}Module" (chip task_e7af351e).
        /// </summary>
        private string ModuleClassName(string moduleName)
        {
            if (moduleName.Equals("Main", StringComparison.OrdinalIgnoreCase)) return "Program";
            var name = SanitizeName(moduleName);
            return ContainerCollides(moduleName) ? name + "Module" : name;
        }

        private bool ContainerCollides(string moduleName)
        {
            if (_containerModule == null) return false;
            bool Same(string n) => string.Equals(n, moduleName, StringComparison.OrdinalIgnoreCase);
            return _containerModule.Classes.Keys.Any(k => Same(k.Split('.').Last()))
                || _containerModule.Interfaces.Keys.Any(k => Same(k.Split('.').Last()))
                || _containerModule.Enums.Keys.Any(k => Same(k.Split('.').Last()))
                || _containerModule.Functions.Any(f => !f.IsLambda && !f.IsExternal
                        && Same(f.ModuleName ?? _options.ClassName) && Same(f.Name))
                || _containerModule.GlobalVariables.Values.Any(g => Same(g.ModuleName ?? _options.ClassName) && Same(g.Name));
        }
```
  Set `private IRModule _containerModule;` at the top of `Generate(IRModule module)`. ⚠ Check the real collection names on `IRModule` (`Classes`, `Interfaces`, `Enums`, `Functions`, `GlobalVariables` — `IRNodes.cs` near `:1681`) and the key shape (simple or qualified) before relying on `Split('.')`.
  - `:494-499`: `var className = ModuleClassName(moduleName);` (delete the local `"Main"` special case).
  - Grep `_currentModuleClass` and every `static class` / `.{` qualification in `CSharpBackend.cs`: every place that spells a container name goes through `ModuleClassName` (record the list in the commit).
- [ ] **Step 4: Run — GREEN**, plus `CsFileScopeQualificationTests`, `ModuleProcedureCallTests`, `ModuleMemberAccessTests`, `ModuleProcedureAccessTests`, `CrossFileBindingTests`, `WinFormsTemplateBuildTests` by name.
- [ ] **Step 5: Mutations:** (1) drop `!f.IsLambda` → row 1 red; (2) `ContainerCollides` returns false → rows 2–3 red; (3) the declaration keeps its own `"Main"` check instead of calling `ModuleClassName` → a qualified cross-file call test red (name it).
- [ ] **Step 6: Commit.**

---

## Task 9: A user `Enum`'s member has the Enum's type

Spec §4.7, M12.

**Files:**
- Modify: `BasicLang/SemanticAnalyzer.cs` — `Visit(EnumNode)` (`:6329-6362` on `bc29391e`, `:6687-6720` [X]; each member becomes a scope constant `"{Enum}.{Member}"` and `enumType.Members` is never populated; an Enum is defined ONLY here — no pass-1 registration exists, grep `TypeKind.Enum)` finds only this `DefineType` [X]); `RegisterClassTypes` (pass 1, sweep 1 — `:5908-5938` [X], the `case ClassNode`/`case InterfaceNode` pre-registration with `_preRegisteredClasses`/`_preRegisteredInterfaces` consumed by the visits); the pass-1 SIBLING loop that calls `RegisterSiblingClassShell` (`:592` on `bc29391e`); `BindMemberAccess` (`:11418-11638`; `objectType.ResolveMember(...)` `:11569`; the Object fallback `:11615`) — unchanged once Members is populated
- Create: `VisualGameStudio.Tests/Compiler/EnumMemberTypingTests.cs`

- [ ] **Step 1: Failing tests.**

```csharp
using System.IO;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.JavaScript;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec §4.7, M12 — `Dim k As Shade = Shade.Dark` was "Cannot assign value of type 'Object' to variable of type
/// 'Shade'" on JavaScript AND C#: the member access found no member on the Enum's TypeInfo (Members was never
/// populated) and fell to the PascalCase ".NET type" fallback, typed Object. The library's Keys, MouseButtons,
/// DockStyle, AnchorStyles, BorderStyle, ContentAlignment, DialogResult and MessageBoxButtons depend on this.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class EnumMemberTypingTests
{
    /// <summary>Every shipping backend, through the optimizer too (CLAUDE.md: validate through the optimizer).</summary>
    private static void OnJsAndCSharp(string program, string expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(program)), Is.EqualTo(expected), "JavaScript");
            Assert.That(FourBackends.Norm(JavaScriptOptimizedExecutionTests.RunOptimized(program)), Is.EqualTo(expected), "JavaScript (optimized)");
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo(expected), "C#");
        });
        // Outside Assert.Multiple: CompileRun IGNORES when there is no C++ compiler (CrossFileBindingTests' rule).
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program))), Is.EqualTo(expected), "C++ (optimized)");
    }

    private const string Decl = "Enum Shade\n Light\n Dark\n Keyed = 65\nEnd Enum\n";

    [Test]
    public void AssignedToADeclaredLocal() => OnJsAndCSharp(Decl +
        "Sub Main()\n Dim k As Shade = Shade.Dark\n Console.WriteLine(CInt(k))\n Dim j As Shade\n j = Shade.Keyed\n Console.WriteLine(CInt(j))\nEnd Sub", "1\n65");

    [Test]
    public void ComparedAndSelected() => OnJsAndCSharp(Decl +
        "Sub Main()\n Dim k As Shade = Shade.Light\n If k = Shade.Light Then Console.WriteLine(\"light\")\n" +
        " Select Case k\n  Case Shade.Dark\n   Console.WriteLine(\"dark\")\n  Case Shade.Light\n   Console.WriteLine(\"case light\")\n End Select\nEnd Sub",
        "light\ncase light");

    [Test]
    public void PassedAsAnArgument() => OnJsAndCSharp(Decl +
        "Sub Show(s As Shade)\n Console.WriteLine(CInt(s))\nEnd Sub\nSub Main()\n Show(Shade.Dark)\nEnd Sub", "1");

    [Test]
    public void InsideAModule() => OnJsAndCSharp(
        "Module M\n" + Decl + " Sub Main()\n  Dim k As Shade = Shade.Dark\n  Console.WriteLine(CInt(k))\n End Sub\nEnd Module", "1");

    [Test]
    public void DeclaredBelowItsUse() => OnJsAndCSharp(
        "Sub Main()\n Dim k As Shade = Shade.Dark\n Console.WriteLine(CInt(k))\nEnd Sub\n" + Decl, "1");

    [Test]
    public void DeclaredInASiblingFile()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "bl-enum-" + Path.GetRandomFileName())).FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "Shade.bas"), Decl);
            File.WriteAllText(Path.Combine(dir, "Main.bas"), "Module Program\n Sub Main()\n  Dim k As Shade = Shade.Dark\n  PrintLine(CInt(k))\n End Sub\nEnd Module\n");
            var r = new BasicCompiler(new CompilerOptions { TargetBackend = "javascript" })
                .CompileProjectFiles(new[] { Path.Combine(dir, "Shade.bas"), Path.Combine(dir, "Main.bas") });
            Assert.That(r.HasErrors, Is.False, string.Join(" | ", r.AllErrors.Select(e => e.Message)));
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(new JavaScriptCodeGenerator().Generate(r.CombinedIR!))),
                Is.EqualTo("1"));
        }
        finally { Directory.Delete(dir, true); }
    }
}
```
- [ ] **Step 2: Run — RED:** `InvalidOperationException … Cannot assign value of type 'Object' to variable of type 'Shade'` from `JsTestSupport`.
- [ ] **Step 3: Implement.**
  - A helper both passes call:

```csharp
        /// <summary>Spec §4.7 — an Enum's members are MEMBERS of its type (typed as the Enum), so `Shade.Dark` binds
        /// through ResolveMember instead of falling to the PascalCase ".NET type" fallback (M12).</summary>
        private static void RecordEnumMembers(EnumNode node, TypeInfo enumType)
        {
            foreach (var member in node.Members)
            {
                enumType.Members[member.Name] =
                    new Symbol(member.Name, SymbolKind.Constant, enumType, member.Line, member.Column);
            }
        }
```
  - `RegisterClassTypes` gains `case EnumNode en: if (_typeManager.DefineType(en.Name, TypeKind.Enum) is { } pre) { _preRegisteredEnums.Add(en.Name); RecordEnumMembers(en, pre); } break;` with a new `private readonly HashSet<string> _preRegisteredEnums = new(StringComparer.OrdinalIgnoreCase);` beside `_preRegisteredClasses`.
  - `Visit(EnumNode)`: `var enumType = _typeManager.DefineType(node.Name, TypeKind.Enum); if (enumType == null && _preRegisteredEnums.Remove(node.Name)) enumType = _typeManager.GetType(node.Name);` then the existing duplicate error when still null (the class pattern, `Visit(ClassNode)` `:5888-5897` on `bc29391e`); after the scope constants, `RecordEnumMembers(node, enumType);` (idempotent).
  - Sibling file: in the pass-1 sibling loop beside the `RegisterSiblingClassShell` call, an `EnumNode` gets a shell — `GlobalScope.Define(new Symbol(en.Name, SymbolKind.Type, t, 0, 0) { IsImported = true, IsSiblingSignature = true, SourceModule = unit.ModuleName })` over `var t = new TypeInfo(en.Name, TypeKind.Enum)` with `RecordEnumMembers(en, t)` — skipped when `GlobalScope.Resolve(en.Name) != null` (the class-shell rule, `:656-673`).
  - `BindMemberAccess`: no change (verify — do NOT add a special case after `:11615`).
- [ ] **Step 4: Run — GREEN** (all four legs; C++ skips without a compiler), plus `SelectCase*`, `CppSelectCaseTests`, `TypeOfTests`, `CTypeConversionTests`, and every test that greps `Enum` in its name.
- [ ] **Step 5: Mutations:** (1) skip the Members population → every row red; (2) populate only in `Visit(EnumNode)` (not pass 1) → `DeclaredBelowItsUse` + `DeclaredInASiblingFile` red.
- [ ] **Step 6: Commit.**

---

## Task 10: Events are class members; `AddHandler` checks the handler's shape (`HandlerSignatureMismatch`)

Spec §4.8, M13. Scope call S7.

**Files:**
- Modify: `BasicLang/SemanticAnalyzer.cs` — `PopulateClassMemberSignatures` (`:744-820`: no `EventDeclarationNode` case); `Visit(ClassNode)` member loop (`:6146-6200`: Function/Sub/Variable/Property only); `Visit(EventDeclarationNode)` (`:8494-8524`: the event's type is `Action(Of …)` from its parameters `:8507-8510`, bare `Event X` → `EventHandler` `:8514`); `ValidateHandlerWiring` (`:8579-8632`)
- Create: `VisualGameStudio.Tests/Compiler/HandlerSignatureTests.cs`

- [ ] **Step 1: Failing tests.**

```csharp
using System.Collections.Generic;
using BasicLang.Compiler;
using BasicLang.Compiler.SemanticAnalysis;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec §4.8, M13 (`HandlerSignatureMismatch`) — `AddHandler btn.Click, AddressOf H` with `H(n As Integer)` against
/// `Event Click(sender As Object, e As EventArgs2)` built green and ran, passing the sender as n. The shape check
/// existed (ValidateHandlerWiring) and never saw either side: events were not class members, and a Private handler
/// declared below its wiring was unbound when the wiring was analyzed. This is what makes an unconverted
/// `(e As DomEvent)` handler a build error on the web (spec §8).
/// </summary>
[TestFixture]
public class HandlerSignatureTests
{
    private static List<string> Errors(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, "the test's source must parse");
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        return analyzer.Errors.ConvertAll(e => e.Message);
    }

    private const string Button =
        "Public Class EventArgs2\nEnd Class\n" +
        "Public Class Btn\n Public Event Click(sender As Object, e As EventArgs2)\n" +
        " Public Sub Press()\n  RaiseEvent Click(Me, New EventArgs2())\n End Sub\nEnd Class\n";

    private static string Form(string handler, bool handlerFirst = false)
    {
        var init = " Private b As Btn\n Public Sub New()\n  b = New Btn()\n  AddHandler b.Click, AddressOf H\n End Sub\n";
        return "Public Class Form1\n" + (handlerFirst ? handler + init : init + handler) + "End Class\n";
    }

    [Test]
    public void TooFewParameters_BelowItsWiring_IsReported() =>
        Assert.That(Errors(Button + Form(" Private Sub H(n As Integer)\n End Sub\n")),
            Has.Some.Contains("takes 1 parameter(s) but the event supplies 2"));

    [Test]
    public void AWrongParameterType_BelowItsWiring_IsReported() =>
        Assert.That(Errors(Button + Form(" Private Sub H(sender As Object, e As Integer)\n End Sub\n")),
            Has.Some.Contains("takes 'Integer' as parameter 2, but the event supplies 'EventArgs2'"));

    [Test]
    public void TheWinFormsShape_BelowOrAbove_IsClean()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Errors(Button + Form(" Private Sub H(sender As Object, e As EventArgs2)\n End Sub\n")), Is.Empty);
            Assert.That(Errors(Button + Form(" Private Sub H(sender As Object, e As EventArgs2)\n End Sub\n", handlerFirst: true)), Is.Empty);
        });
    }

    [Test]
    public void TheEventsClass_DeclaredBelowTheWiring_IsStillChecked() =>
        Assert.That(Errors(Form(" Private Sub H(n As Integer)\n End Sub\n") + Button),
            Has.Some.Contains("takes 1 parameter(s) but the event supplies 2"));

    /// <summary>⛔ .NET events stay silent: on a WinForms build `b.Click` types as Object (K14) and csc checks it.</summary>
    [Test]
    public void AnUnresolvedDotNetEvent_StaysSilent() =>
        Assert.That(Errors("Using System.Windows.Forms\nPublic Class Form1\n Private b As Button\n" +
                           " Public Sub New()\n  AddHandler b.Click, AddressOf H\n End Sub\n" +
                           " Private Sub H(n As Integer)\n End Sub\nEnd Class\n"), Is.Empty);

    /// <summary>§11.7 — the diagnostic is assertable BY NAME.</summary>
    [Test]
    public void TheMismatch_IsNamed() =>
        Assert.That(Errors(Button + Form(" Private Sub H(n As Integer)\n End Sub\n")), Has.Some.Contains("HandlerSignatureMismatch"));

    /// <summary>The LIBRARY's shape: the event is declared on a BASE class and wired on a field of a DERIVED type.</summary>
    [Test]
    public void AnEventOfABaseClass_WiredOnADerivedField_IsChecked() =>
        Assert.That(Errors(
            "Public Class EventArgs2\nEnd Class\n" +
            "Public Class Control\n Public Event Click(sender As Object, e As EventArgs2)\nEnd Class\n" +
            "Public Class Button\n Inherits Control\nEnd Class\n" +
            "Public Class Form1\n Private b As Button\n Public Sub New()\n  b = New Button()\n  AddHandler b.Click, AddressOf H\n End Sub\n" +
            " Private Sub H(n As Integer)\n End Sub\nEnd Class\n"),
            Has.Some.Contains("takes 1 parameter(s) but the event supplies 2"));
}
```
  And `VisualGameStudio.Tests/Compiler/HandlerSignatureRouteTests.cs` (Integration) — the same library shape split over SIBLING files, on the project route, the CLI and the IDE (spec §4 preamble: CLI + IDE):

```csharp
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BasicLang.Compiler;
using NUnit.Framework;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Serialization;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>§4.8 on the library's real shape: `Control` (declares Click) and `Button` (Inherits Control) in one file,
/// the form wiring `AddHandler b.Click` on a `Button` field in ANOTHER — through every build route.</summary>
[TestFixture]
[Category("Integration")]
public class HandlerSignatureRouteTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "bl-hsig-" + Path.GetRandomFileName())).FullName;
        File.WriteAllText(Path.Combine(_dir, "Lib.bas"),
            "Public Class EventArgs2\nEnd Class\n" +
            "Public Class Control\n Public Event Click(sender As Object, e As EventArgs2)\nEnd Class\n" +
            "Public Class Button\n Inherits Control\nEnd Class\n");
        File.WriteAllText(Path.Combine(_dir, "Form1.bas"),
            "Public Class Form1\n Private b As Button\n Public Sub New()\n  b = New Button()\n  AddHandler b.Click, AddressOf H\n End Sub\n" +
            " Private Sub H(n As Integer)\n End Sub\nEnd Class\nModule Program\n Sub Main()\n  Dim f As New Form1()\n End Sub\nEnd Module\n");
        File.WriteAllText(Path.Combine(_dir, "Site.blproj"),
            "<Project>\n  <PropertyGroup>\n    <ProjectName>Site</ProjectName>\n    <TargetBackend>JavaScript</TargetBackend>\n" +
            "  </PropertyGroup>\n  <ItemGroup>\n    <Compile Include=\"Lib.bas\" />\n    <Compile Include=\"Form1.bas\" />\n  </ItemGroup>\n</Project>\n");
    }

    [TearDown]
    public void TearDown() { try { Directory.Delete(_dir, true); } catch { } }

    [TestCase(false)]
    [TestCase(true)]
    public void TheProjectRoute_BothFileOrders(bool reversed)
    {
        var files = new[] { Path.Combine(_dir, "Lib.bas"), Path.Combine(_dir, "Form1.bas") };
        if (reversed) files = files.Reverse().ToArray();
        var r = new BasicCompiler(new CompilerOptions { TargetBackend = "javascript" }).CompileProjectFiles(files);
        Assert.That(r.AllErrors.Select(e => e.Message), Has.Some.Contains("HandlerSignatureMismatch"));
    }

    [Test]
    public async Task TheCli()
    {
        var (exit, stdout, stderr) = await CliTestHarness.RunCli(_dir, "build", "Site.blproj");
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.Not.Zero);
            Assert.That(stdout + stderr, Does.Contain("HandlerSignatureMismatch"));
        });
    }

    [Test]
    public async Task TheIde()
    {
        var project = await new ProjectSerializer().LoadAsync(Path.Combine(_dir, "Site.blproj"));
        var result = await new BuildService(new RecordingOutput()).BuildProjectAsync(project);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Select(d => d.Message), Has.Some.Contains("HandlerSignatureMismatch"));
        });
    }
}
```
- [ ] **Step 2: Run — RED:** rows 1, 2, 4, the base-class row and `TheMismatch_IsNamed` have NO error (the defect); `HandlerSignatureRouteTests` fail on every route (the build succeeds); rows 3 and 5 are green (guards — record them as such).
- [ ] **Step 3: Implement.**
  - Extract from `Visit(EventDeclarationNode)` (`:8507-8514`) a helper `private TypeInfo EventDelegateType(EventDeclarationNode node)` returning exactly what that code builds; the visit calls it.
  - `PopulateClassMemberSignatures`: a new case `case EventDeclarationNode ev:` (all access levels — an event raised and wired inside its own class is still its member) → `classType.Members[ev.Name] = new Symbol(ev.Name, SymbolKind.Event, EventDelegateType(ev), 0, 0) { Access = ev.Access };`. ⚠ `ResolveSiblingSignatureType` is what the neighbouring cases use for types from a sibling — `EventDelegateType` must resolve the parameter types the same way when called from here (pass a resolver or build the parameter list with `ResolveSiblingSignatureType`).
  - `Visit(ClassNode)` member loop: `else if (member is EventDeclarationNode evt && _nodeSymbols.TryGetValue(evt, out var evtSymbol)) classType.Members[evt.Name] = evtSymbol;` — confirm `Visit(EventDeclarationNode)` records `_nodeSymbols[node]`; add it if not.
  - `ValidateHandlerWiring`: when `expected != null` and `actual == null` and the handler is `AddressOf <simple name>` (or `AddressOf Me.<name>`) and the analyzer is inside a class, queue `(expected, name, keyword, line, column)` in a `List<PendingHandlerCheck>` for the current class instead of returning. Extract the count/type comparison (`:8616-8631`) into `CompareHandlerShapes(expected, actual, keyword, line, column)`.
  - At the end of `Visit(ClassNode)` (after the member loop), for each queued check of THIS class: `classType.Members.TryGetValue(name, out var h)` → `GetDelegateParameterTypes(DelegateTypeOf(h))` → `CompareHandlerShapes`. Clear the queue for the class.
  - The event side through a BASE class: `b.Click` on a `Button` field must find `Click` in `Control`'s Members through the `BaseType` chain — `ResolveMember` walks it (confirm; a sibling shell's members come from `RecordDeclaredMembers` [X] and `PopulateClassMemberSignatures`, which now includes events).
  - **Name it:** both messages end `" (HandlerSignatureMismatch)"`. Grep the tests for `the handler passed to` — any exact-text assertion is updated in this commit.
- [ ] **Step 4: Run — GREEN** (both fixtures), plus `JavaScriptEventTests`, `UserDelegateConversionExecutionTests`, `DelegateMemberInvocationExecutionTests`, `NetDelegateTests`, `WinFormsTemplateBuildTests`, `FormRegionWriterTests` by name (a WinForms region's `AddHandler` must stay silent — K14).
- [ ] **Step 5: Mutations:** (1) no `EventDeclarationNode` case in `PopulateClassMemberSignatures` → row 4 red; (2) no deferral → rows 1–2 red; (3) the deferred check compares against the wrong class's members (use the outer class) → record which row catches it, add one if none.
- [ ] **Step 6: Commit.** Message: "AddHandler/RemoveHandler check a BasicLang event's handler shape (HandlerSignatureMismatch)".

---

### Tasks 9–10 review follow-up (2026-10-05) — done, and recorded

- ✅ Fixed (`EnumAndHandlerReviewTests`, run on C#/JS/C++): an Enum value PRINTS as its member name (`k.ToString()`,
  `Console.WriteLine(k)`, `"v=" & k` — JS `__blEnumName`, C++ `BlEnumName` + `operator<<`); an Enum-member FIELD
  initializer (`IREnumMemberValue`); VB's Enum → numeric widening (`Dim n As Integer = Shade.Keyed`, cast at the store);
  `And`/`Or`/ordering over ONE Enum (flags — `AnchorStyles.Top Or AnchorStyles.Left`; IR BitwiseAnd/BitwiseOr, C++
  operator overloads); a non-constant Case value (`Case Lim.Max`) is a C# `case var _ when` compare; `Enum X` + `Module X`
  in two files refused in every order; Main.bas's "Program" checked against other containers; relaxed handlers (no
  parameters / wider parameters) wrapped in a lambda on C#; a Module procedure as a handler qualified on C#.
- ⚠ Recorded, not fixed (pre-existing):
  - `Xor` is not a BasicLang binary operator at all (`a Xor b` is "End of statement expected").
  - Enum `+`/`-` (VB: Enum + Integer → Integer) stays refused.
  - Overlapping `Case` ranges/values reach C# as CS8120 (subsumed case) from a clean BasicLang build.
  - Two classes each nesting an `Enum Mode` (probe `rv10p\e2.bas`): the nested enums share one name space in the IR
    (`_module.Enums[node.Name]` is keyed by the bare name), so the second wins.
  - C++: an event WITH parameters does not compile (`raise_X()` takes none — open chip), and `Object` has no C++ mapping
    (capability refusal), so the handler rows run on C# and JavaScript only.
  - JavaScript (seen in Task 11): `Math.Max(3, 7)` under `Using System` on the project route is "no lowering for 'Math.Max'".
    Measured: PRE-EXISTING and not Using-related (the no-Using file route fails the same way; Task 11 changed routing only
    for a receiver bound to a value symbol, and `Math` has none). Bare `Max(a, b)` maps; the QUALIFIED `Math.X` does not
    reach JavaScriptStdLib. Fixed in Task 14's commit (Max/Min/Abs/Floor/Ceiling/Round with banker's rounding/Sqrt/Pow).
- ✅ Second review (da43d56b/23e5150d/1e929b29), folded into Task 13's commit: `CType(i, Shade)` (Integer → Enum) lowers on
  JavaScript (the value unchanged); comparing two DIFFERENT Enums is refused in the analyzer (VB Option Strict; it was C#
  CS0019 / C++ C2676 / JS a silent number compare).
- ⚠ Recorded from the second review, not fixed:
  - `Public Shared Event` fails C# CS0120 and JS (`push` on an undefined static list, TypeError) — pre-existing. The
    library's controls raise INSTANCE events only (WinForms has no shared control events), so not needed by 2a.
  - `Dim d As Action(Of Integer) = AddressOf Me.A` is refused — pre-existing.
  - ⛔ DECISION NEEDED by Task 28+: with no `<Flags>`, `Bold Or Italic` prints `3` on every backend, while WinForms'
    `AnchorStyles.Top Or AnchorStyles.Left` prints `Top, Left`. Recommendation (owner-delegated default): the LIBRARY's
    flags enums carry a `ToString` that matches WinForms, library-side (a shared helper the flags enums' text routes
    through), rather than a BasicLang `<Flags>` attribute — no language change, and only the library's enums need it.
    Record the choice when Task 28 lands.
  - JS `RemoveHandler` (Task 12) covers BasicLang events only; a DOM `addEventListener` wiring is not removable by a
    fresh `AddressOf` (the DOM compares identity). Library concern — Task 28+ keeps the bound handler it attaches.
  - Roster pin: the reviewer measured the branch at 103 and master at 107 → merged 109; Task 13 adds one (branch 104 →
    merged 110), and every later rostered fixture one more. RE-READ BOTH at Task 17's merge, never add deltas from memory.

## Task 11: JavaScript — `Me` (or any value) is never a .NET static receiver

M7, spec-claims #9.

**Files:**
- Modify: `BasicLang/IRBuilder.cs` — the call-routing block (`:5905-5942`; quote `bool isNetType = objVar.Name.Contains('.') || IsKnownNetStaticType(objVar.Name)` and `isStaticCall = (exactClassMatch || isNetType) && !isLocalOrParam;`)
- Create: `VisualGameStudio.Tests/Compiler/JavaScriptMeUnderUsingTests.cs`
- Modify: `VisualGameStudio.Tests/Compiler/JsExecutionTierRosterTests.cs` (add the fixture to `ExecutionTier`, bump `RosterIsPinned` by 1 — read the current pin first)

- [ ] **Step 1: Failing tests.**

```csharp
using System.IO;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.JavaScript;
using BasicLang.Compiler.IR.Optimization;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// M7 — with ANY `Using` line in the file, `Me.Init()` failed the JavaScript build: "no lowering for 'Me.Init'".
/// IsKnownNetStaticType answers true for every PascalCase name once the unit has a .NET Using (IRBuilder.cs:6636-6656),
/// and `Me` is PascalCase, so the call was routed as a static call on a type named Me. ⛔ Only the PROJECT route
/// sets CurrentUnit (ConfigureModuleSystem), so this fixture compiles through BasicCompiler — JsTestSupport would pass
/// with the defect present. The WinForms scaffold is exactly this shape (three Usings + Me.InitializeComponent()).
/// </summary>
[TestFixture]
[Category("Integration")]
public class JavaScriptMeUnderUsingTests
{
    private static string Run(string source)
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "bl-meusing-" + Path.GetRandomFileName())).FullName;
        try
        {
            var file = Path.Combine(dir, "Main.bas");
            File.WriteAllText(file, source);
            var r = new BasicCompiler(new CompilerOptions { TargetBackend = "javascript" }).CompileProjectFiles(new[] { file });
            Assert.That(r.HasErrors, Is.False, string.Join(" | ", r.AllErrors.Select(e => e.Message)));
            var pipeline = new OptimizationPipeline();
            pipeline.AddStandardPasses();
            pipeline.Run(r.CombinedIR!);
            return FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(new JavaScriptCodeGenerator().Generate(r.CombinedIR!)));
        }
        finally { Directory.Delete(dir, true); }
    }

    private const string Body =
        "Public Class Widget\n Public Sub Go()\n  Console.WriteLine(\"go\")\n End Sub\nEnd Class\n" +
        "Public Class Frm\n Private Button1 As Widget\n" +
        " Public Sub New()\n  Me.Init()\n  Console.WriteLine(Me.Twice(21))\n  Button1 = New Widget()\n  Button1.Go()\n  Use(Button1)\n End Sub\n" +
        " Private Sub Init()\n  Console.WriteLine(\"init\")\n End Sub\n" +
        " Public Function Twice(n As Integer) As Integer\n  Return n * 2\n End Function\n" +
        " Private Sub Use(Thing As Widget)\n  Thing.Go()\n End Sub\nEnd Class\n" +
        "Sub Main()\n Dim f As New Frm()\nEnd Sub\n";

    [TestCase("Using System\n")]
    [TestCase("Using System.Drawing\n")]
    [TestCase("Using System.Windows.Forms\n")]
    [TestCase("Using System\nUsing System.Drawing\nUsing System.Windows.Forms\n")]
    [TestCase("")]
    public void SelfCalls_FieldAndParameterReceivers_UnderAnyUsing(string usings) =>
        Assert.That(Run(usings + Body), Is.EqualTo("init\n42\ngo\ngo"));

    [Test]
    public void AnInheritedMethod_ThroughMe_UnderAUsing() =>
        Assert.That(Run("Using System\nPublic Class B0\n Public Sub Hello()\n  Console.WriteLine(\"hello\")\n End Sub\nEnd Class\n" +
                        "Public Class Frm\n Inherits B0\n Public Sub New()\n  Me.Hello()\n End Sub\nEnd Class\n" +
                        "Sub Main()\n Dim f As New Frm()\nEnd Sub\n"), Is.EqualTo("hello"));
}
```
- [ ] **Step 2: Run — RED:** every Using row fails `HasErrors`/generation with `no lowering for 'Me.Init'`; the `""` row is green (the guard).
- [ ] **Step 3: Implement** in the routing block, before `isStaticCall`:

```csharp
                    // ⛔ A VALUE is never a type (M7). `Me`/`MyClass` — and any receiver the analyzer bound to a
                    // value symbol (a field, property, parameter or local) — is an instance receiver however it is
                    // spelled. IsKnownNetStaticType's PascalCase-under-a-.NET-Using heuristic claimed `Me` itself.
                    bool isValueReceiver = IsSelfExpression(memberExpr.Object)
                        || IsValueSymbol(_semanticAnalyzer.GetNodeSymbol(memberExpr.Object));
```
  and `isStaticCall = (exactClassMatch || isNetType) && !isLocalOrParam && !isValueReceiver;`. Add the two private helpers (`IsSelfExpression`: the AST's Me / MyClass node types — grep `class Me` in `ASTNodes.cs` for the exact names; `IsValueSymbol`: `SymbolKind.Variable`, `Parameter`, `Property`, `Field` if it exists, `Constant`). Confirm `GetNodeSymbol` is reachable from the IR builder (it is used as `_semanticAnalyzer.GetNodeType(node)` elsewhere; if `GetNodeSymbol` is private, make it `internal`).
- [ ] **Step 4: Add the fixture to the roster** (`ExecutionTier`, `RosterIsPinned` +1). Run — GREEN, plus `NetBuildPipelineTests` (its `InstanceCallOnAPascalCaseLocal_UnderANetUsing_…` guard), `JsExecutionTierRosterTests`, `ModuleProcedureCallTests`, `CrossFileBindingTests` by name.
- [ ] **Step 5: Mutation:** remove `&& !isValueReceiver` → the Using rows red.
- [ ] **Step 6: Commit.**

---

## Task 12: JavaScript — `RemoveHandler` removes; a raise invokes a snapshot

Spec §4.4, M2, scope call S6.

**Files:**
- Modify: `BasicLang/JavaScriptBackend.cs` — the event field (`:807-808`, quote `{SanitizeName(evt.Name)} = new Set();`); `TryEventCall` (`:3144-3172`); `UnaryText`'s AddressOf arms (`:1538-1555`); the prelude sequence (`:184-187`) + a new `EmitDelegatePrelude` and `UsesDelegateHelpers(IRModule)` scan (pattern: `UsesRoundingHelper`, `:261`; the reason it is a scan: `:471-474`)
- Modify: `VisualGameStudio.Tests/Compiler/JavaScriptEventTests.cs` (`:189` `Clicked = new Set();` → `Clicked = [];`; `:192` `.add(` → `.push(`; the class summary's `RemoveHandler … removes nothing` paragraph `:26-29`) and the comment in `FormRegionWriterTests.cs:243-244` (`.add(` → `.push(`)
- Create: `VisualGameStudio.Tests/Compiler/JavaScriptDelegateIdentityTests.cs`; roster +1

- [ ] **Step 1: Failing tests.**

```csharp
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec §4.4, M2 — `RemoveHandler b.Clicked, AddressOf Me.H` removed nothing on JavaScript: each `AddressOf` was a
/// fresh `.bind(recv)` function, and the event a `Set`, so `delete` never found it. .NET's rules, now the page's:
/// an invocation LIST (the same handler twice fires twice), RemoveHandler takes the LAST matching entry, a delegate
/// to an instance method is equal to another to the same (instance, method), and a raise invokes the list as it was
/// when the raise began. The same program runs on C# as the oracle.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class JavaScriptDelegateIdentityTests
{
    private static void Same(string program, string expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(program)), Is.EqualTo(expected), "JavaScript");
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo(expected), "C# (the oracle)");
        });
    }

    private const string Btn =
        "Public Class Btn\n Public Event Clicked(n As Integer)\n Private count As Integer\n" +
        " Public Sub Press()\n  count = count + 1\n  RaiseEvent Clicked(count)\n End Sub\nEnd Class\n";

    [Test]
    public void AddedTwice_RemovedOnce_OneRemains() => Same(Btn +
        "Public Class Listener\n Private b As Btn\n Public Sub New()\n  b = New Btn()\n" +
        "  AddHandler b.Clicked, AddressOf Me.H\n  AddHandler b.Clicked, AddressOf Me.H\n  b.Press()\n" +
        "  RemoveHandler b.Clicked, AddressOf Me.H\n  b.Press()\n  RemoveHandler b.Clicked, AddressOf Me.H\n  b.Press()\n" +
        "  Console.WriteLine(\"done\")\n End Sub\n Private Sub H(n As Integer)\n  Console.WriteLine(\"h \" & n)\n End Sub\nEnd Class\n" +
        "Sub Main()\n Dim l As New Listener()\nEnd Sub\n",
        "h 1\nh 1\nh 2\ndone");

    [Test]
    public void ADelegateToAnotherInstance_IsNotRemoved() => Same(Btn +
        "Public Class Who\n Private name As String\n Public Sub New(n As String)\n  name = n\n End Sub\n" +
        " Public Sub H(n As Integer)\n  Console.WriteLine(name & \" \" & n)\n End Sub\nEnd Class\n" +
        "Sub Main()\n Dim b As New Btn()\n Dim a As New Who(\"a\")\n Dim c As New Who(\"c\")\n" +
        " AddHandler b.Clicked, AddressOf a.H\n RemoveHandler b.Clicked, AddressOf c.H\n b.Press()\nEnd Sub\n",
        "a 1");

    [Test]
    public void AFreeFunction_AddsAndRemoves() => Same(Btn +
        "Sub F(n As Integer)\n Console.WriteLine(\"f \" & n)\nEnd Sub\n" +
        "Sub Main()\n Dim b As New Btn()\n AddHandler b.Clicked, AddressOf F\n b.Press()\n RemoveHandler b.Clicked, AddressOf F\n b.Press()\n Console.WriteLine(\"done\")\nEnd Sub\n",
        "f 1\ndone");

    [Test]
    public void RemovingANeverAddedHandler_ChangesNothing() => Same(Btn +
        "Sub F(n As Integer)\n Console.WriteLine(\"f \" & n)\nEnd Sub\nSub G(n As Integer)\n Console.WriteLine(\"g \" & n)\nEnd Sub\n" +
        "Sub Main()\n Dim b As New Btn()\n AddHandler b.Clicked, AddressOf F\n RemoveHandler b.Clicked, AddressOf G\n b.Press()\nEnd Sub\n",
        "f 1");

    /// <summary>A lambda is removed only by the SAME delegate value (as in .NET); a second, textually equal lambda is a
    /// different delegate and removes nothing.</summary>
    [Test]
    public void ALambda_IsRemovedOnlyByTheSameDelegate() => Same(Btn +
        "Sub Main()\n Dim b As New Btn()\n Dim h As Action(Of Integer) = Sub(n As Integer) Console.WriteLine(\"l \" & n)\n" +
        " AddHandler b.Clicked, h\n RemoveHandler b.Clicked, Sub(n As Integer) Console.WriteLine(\"l \" & n)\n b.Press()\n" +
        " RemoveHandler b.Clicked, h\n b.Press()\n Console.WriteLine(\"done\")\nEnd Sub\n",
        "l 1\ndone");

    /// <summary>A handler that subscribes another during a raise: the new one runs from the NEXT raise.</summary>
    [Test]
    public void ARaise_InvokesTheListAsItWas() => Same(Btn +
        "Public Class Grow\n Private b As Btn\n Public Sub New()\n  b = New Btn()\n  AddHandler b.Clicked, AddressOf Me.First\n" +
        "  b.Press()\n  b.Press()\n End Sub\n" +
        " Private Sub First(n As Integer)\n  Console.WriteLine(\"first \" & n)\n  If n = 1 Then AddHandler b.Clicked, AddressOf Me.Second\n End Sub\n" +
        " Private Sub Second(n As Integer)\n  Console.WriteLine(\"second \" & n)\n End Sub\nEnd Class\n" +
        "Sub Main()\n Dim g As New Grow()\nEnd Sub\n",
        "first 1\nfirst 2\nsecond 2");
}
```
- [ ] **Step 2: Run — RED:** row 1 JS prints `h 1\nh 1\nh 2\nh 2\nh 3\nh 3\ndone`-shaped output (nothing removed); rows 2, 3 and `RemovingANeverAddedHandler_…` green (guards); the lambda row — record (a `Set` removes by identity, which is right for a lambda, so it may be green); the snapshot row — record (a `Set` iterated live may call `second 1`). C# legs green throughout (real .NET delegate equality is the oracle here — no exception row). ⚠ If the language refuses `AddHandler b.Clicked, h` with a delegate VARIABLE, record it and replace the lambda row's variable with `AddressOf` a Sub (the removal-by-identity property is then covered by rows 1–2).
- [ ] **Step 3: Implement.**
  - Prelude (emitted when `UsesDelegateHelpers(module)`: any class declares an event, or any function contains an `IRUnaryOp` with `AddressOf`):

```csharp
        private const string DelegatePrelude = """
            function __blBind(target, method) {
              const f = method.bind(target);
              f.__blTarget = target;
              f.__blMethod = method;
              return f;
            }
            function __blSameDelegate(a, b) {
              return a === b || (a.__blMethod !== undefined && a.__blMethod === b.__blMethod && a.__blTarget === b.__blTarget);
            }
            function __blRemoveHandler(list, handler) {
              for (let i = list.length - 1; i >= 0; i--) {
                if (__blSameDelegate(list[i], handler)) { list.splice(i, 1); return; }
              }
            }

            """;
```
  Emitted after `EmitPrimitiveStaticsPrelude` (`:187`) by `EmitDelegatePrelude(module)`.
  - `UnaryText`: `return $"this.{SanitizeName(declared)}.bind(this)";` → `return $"__blBind(this, this.{SanitizeName(declared)})";`; the receiver arm → `$"__blBind({receiver}, {receiver}.{SanitizeName(viaReceiver)})"`; the `this.` fallback → `$"__blBind(this, {operand})"`.
  - Event field: `= [];`.
  - `TryEventCall`: Combine → `$"{Expr(args[0])}.push({Expr(args[1])});"`; Remove → `$"__blRemoveHandler({Expr(args[0])}, {Expr(args[1])});"`; raise → `$"for (const h of [...this.{SanitizeName(evt.Name)}]) h({rendered});"`.
  - ⚠ A DOM `addEventListener(…, AddressOf H)` now receives a `__blBind` function — it still works; `removeEventListener` with a fresh `AddressOf` still finds nothing (the DOM compares identity), exactly as before. Record in the commit; the library (2a) never removes a DOM listener by `AddressOf`.
- [ ] **Step 4: Edit `JavaScriptEventTests`** (`:189`, `:192`, the summary) and the `FormRegionWriterTests` comment. Add the new fixture to the roster (+1).
- [ ] **Step 5: Run — GREEN:** the new fixture, `JavaScriptEventTests`, `JsExecutionTierRosterTests`, `FormBuildEmissionTests`, `FormDesignerAcceptanceTests` (web pages wire DOM events with `AddressOf`), `WebMainStartupTests` by name.
- [ ] **Step 6: Mutations:** (1) `__blSameDelegate` compares only `===` → row 1 red; (2) remove the FIRST match instead of the last → invisible here (equal entries) — record as equivalent; (3) raise iterates `this.X` live → row 4 red (if row 4 was green in Step 2, record that the live `Array` iteration also visits a pushed element and that the snapshot is what makes it pass).
- [ ] **Step 7: Commit.**

---

## Task 13: `MyBase.Property` reaches the base property, on JavaScript, C# and C++

Spec §4.5, M4, scope call S4. ⚠ ADR-0016 (MyBase.New as an instruction) is untouched: this task changes member ACCESS, never `IRBaseConstructorCall`.

**Files:**
- Modify: `BasicLang/IRNodes.cs` — `IRFieldAccess` (`:2457`) and `IRFieldStore` (`:2515`) gain `public bool ThroughBase { get; set; }`
- Modify: `BasicLang/IRBuilder.cs` — member READ (`:5655`, quote `var fieldAccess = new IRFieldAccess(tempName, obj, node.MemberName, memberType);`) and member STORE (`:4906`, quote `var fieldStore = new IRFieldStore(obj, memberExpr.MemberName, value);`): set `ThroughBase` when the receiver is `MyBaseExpressionNode` AND the member's symbol is a `SymbolKind.Property` (a FIELD through MyBase stays `this.field` — fields are not virtual, and JS `super.field` reads the prototype, not the instance: `IRBuilder.cs:2411-2413`)
- Modify: every optimizer/IR pass that CLONES an `IRFieldAccess`/`IRFieldStore` (grep `new IRFieldAccess(` and `new IRFieldStore(` across `BasicLang/` outside `IRBuilder.cs`) — copy `ThroughBase`
- Modify: `BasicLang/JavaScriptBackend.cs` — `FieldAccess` (`:4015-4054`): `if (fa.ThroughBase) return $"super.{SanitizeName(fa.FieldName)}";` first; `Visit(IRFieldStore)` (`:4061-4066`): `super.{member} = …` when `ThroughBase`
- Modify: `BasicLang/CSharpBackend.cs` — the two `case IRFieldAccess fieldAccess:` renders (`:3776`, `:3869`) and `Visit(IRFieldStore)` (`:4711`): `base.{member}` when `ThroughBase`
- Modify: `BasicLang/CppCodeGenerator.cs` — a C++ property IS a pair of accessor methods, `get_X()` / `set_X(value)` (`PropertyAccessorSignature`, `:1927-1937` [X]), so a `ThroughBase` property READ renders `{Base}::get_{X}()` and a STORE renders `{Base}::set_{X}({value});`, where `{Base}` is the C++ class name of `fa.Object.Type` (the MyBase receiver carries the BASE type — `IRBuilder.cs:2423-2424`) mapped the way the generator names classes elsewhere (grep the helper the class header emission uses for a class's own name). The read site is the property-read arm of the field-access rendering (grep `get_` in the field-access paths near `:3664` / `:5615` [X]); the store site is `Visit(IRFieldStore)` (`:5764` on `bc29391e`). A qualified call to a base member from inside a member function is plain C++ (`Base::get_X()`), no `this->` needed.
- Create: `VisualGameStudio.Tests/Compiler/MyBasePropertyExecutionTests.cs` (roster: its name ends with `ExecutionTests` → +1)

- [ ] **Step 1: Failing tests.**

```csharp
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec §4.5, M4, scope call S4 — inside `Overrides Property Text`, `MyBase.Text` lowered to `this.Text` on JavaScript
/// AND C#, calling the override itself: RangeError / StackOverflow at run time from a green build. The IR carried the
/// base only as the receiver's TYPE (IRBuilder.cs:2409-2425); ThroughBase now says "bypass virtual dispatch".
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class MyBasePropertyExecutionTests
{
    private const string Program =
        "Public Class Control\n Private _text As String = \"\"\n Public Overridable Property Text As String\n" +
        "  Get\n   Return _text\n  End Get\n  Set(value As String)\n   _text = value\n  End Set\n End Property\nEnd Class\n" +
        "Public Class Button\n Inherits Control\n Public Overrides Property Text As String\n" +
        "  Get\n   Return \"B:\" & MyBase.Text\n  End Get\n  Set(value As String)\n   MyBase.Text = value & \"!\"\n  End Set\n End Property\nEnd Class\n" +
        "Public Class Fancy\n Inherits Button\n Public Overrides Property Text As String\n" +
        "  Get\n   Return \"F:\" & MyBase.Text\n  End Get\n  Set(value As String)\n   MyBase.Text = value\n  End Set\n End Property\nEnd Class\n" +
        "Sub Main()\n Dim b As New Button()\n b.Text = \"hi\"\n Console.WriteLine(b.Text)\n" +
        " Dim c As Control = New Fancy()\n c.Text = \"yo\"\n Console.WriteLine(c.Text)\nEnd Sub\n";

    private const string Expected = "B:hi!\nF:B:yo!";

    [Test] public void OnJavaScript() => Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(Program)), Is.EqualTo(Expected));
    [Test] public void OnJavaScript_Optimized() => Assert.That(FourBackends.Norm(JavaScriptOptimizedExecutionTests.RunOptimized(Program)), Is.EqualTo(Expected));
    [Test] public void OnCSharp() => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(Program)), Is.EqualTo(Expected));
    [Test] public void OnCpp() => Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(Program))), Is.EqualTo(Expected));

    /// <summary>A FIELD through MyBase is the instance's field (not virtual) — it must stay `this.field`.</summary>
    [Test]
    public void AFieldThroughMyBase_IsStillTheInstancesField() =>
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(
            "Public Class A\n Public n As Integer = 5\nEnd Class\nPublic Class B\n Inherits A\n Public Function Get2() As Integer\n  Return MyBase.n * 2\n End Function\nEnd Class\n" +
            "Sub Main()\n Dim b As New B()\n Console.WriteLine(b.Get2())\nEnd Sub\n")), Is.EqualTo("10"));
}
```
- [ ] **Step 2: Run — RED:** JS `node exited …` with `RangeError: Maximum call stack size exceeded`; C# the in-process run throws `StackOverflowException` — ⚠ that KILLS the test host. Run `OnCSharp` LAST and alone, and expect a crashed run as the red (record it); every later run of it is after the fix. C++: `this->get_Text()` inside `get_Text()` recurses the same way (a crash of the child process — `CompileRun` reports the exit code). The field row is green (guard).
- [ ] **Step 3: Implement** (the files above). Confirm the symbol-kind test: `_semanticAnalyzer.GetNodeSymbol(memberExpr)?.Kind == SymbolKind.Property`.
- [ ] **Step 4: Run — GREEN** (add the fixture to the roster first), plus `OverridablePropertyTests`, `InheritedMemberTests`, `BaseConstructorCall*`, `MsilBaseConstructor*` (ADR-0016 untouched), `ForwardDeclaredType*`, `IRVerifier*` by name.
- [ ] **Step 5: Mutations:** (1) set `ThroughBase` for fields too → the field row red on JS; (2) drop the clone-copy in one optimizer pass → `OnJavaScript_Optimized` red (if not, find which pass clones and prove it); (3) JS renders `this.` despite `ThroughBase` → `OnJavaScript` red.
- [ ] **Step 6: Commit.**

---

## Task 14: `Char` on JavaScript

O13, spec §4.10, M18.

- ⚠ **Carried in from Task 7e (review of e2502c0b) — this task must fix it:** VB's Chars (`s(i)`, `p.Name(i)`) is typed
  **String** today (`SemanticAnalyzer.TryStringChars`, the `"Chars"` intrinsic), because JavaScript has no Char. So
  `Dim c As Char = s(1)` is REJECTED as a String → Char narrowing on every backend. Once Char exists here, type the Chars
  intrinsic as `Char` (C#: drop the `.ToString()` in the `chars` arm; JS: a one-character string IS this task's Char;
  C++: `.at()` without the `std::string(1, …)` wrap) and add the `Dim c As Char = s(1)` row to `RuntimeGapsTask7eTests`.
- ✅ Done (Task 14 commit): Char on JS (a one-character string; default `"\0"`; BL7004 retired; `Char`/`System.Char` in
  the capability checker's primitives); Chars typed Char on every backend (`IndexingAString_IsAChar`); `Asc`/`AscW`/`Chr`/
  `ChrW` REGISTERED in the analyzer (they typed Object — `AscW(c) + 1` was refused) with C#/JS/C++ lowerings (C++ had
  none); Char ordering and `&` of a Char in the analyzer; C# spells a Char `&` operand as its string (char + char was an
  int); the qualified `Math.Max/Min/Abs/Floor/Ceiling/Round/Sqrt/Pow/Sin/Cos/Tan/Exp/Log` on JS (Round = banker's).
  ⛔ Also an OPTIMIZER fix found here: an ordering fold of a pair `CompareLt`/`CompareGt` cannot order answered `false`
  (`"a"c < "z"c` folded to False on C#) — such a pair is no longer folded (the shape of chip `task_5d27b8c8`).
- ⚠ Seen in Task 14's neighbour run, PRE-EXISTING (identical at `cc6eceda`, measured in a detached worktree):
  `PrimitiveStaticSurfaceRunTests.CSharp_Runs` (`dbl(-Infinity)` → FormatException) and `Cpp_Runs` / `Cpp_Aggressive_Runs`
  (MSVC C2312 — a `catch (const std::runtime_error&)` caught twice). Not on the known-failure list: triage at Task 17.
- ⚠ Recorded: `Chr` still answers a String (VB's is Char) — changing it moves every `Chr(n) & …`; `ChrW` is the Char
  spelling. The C++ `Asc` reads the code point unsigned but only for the first byte (no UTF-8 decoding).

**Files:**
- Modify: `BasicLang/JsCapabilityChecker.cs` — `BannedTypes` (`:297-306`): remove the `["Char"] = ("BL7004", …)` row; keep BL7004 in the remarks as retired (never reuse)
- Modify: `BasicLang/JavaScriptBackend.cs` — the constant renderer's `case char: throw JsCapabilityChecker.BannedConstantRejection("Char", v);` (`:1739`) → render as a JSON string literal of the one character; `TryStringBuiltin` (`:1913-1963`): add `ascw` → `.charCodeAt(0)` and `chrw` → `String.fromCharCode(…)` beside `asc`/`chr` (`:1955-1957`); the default value of a `Char` local/field/array element (grep the JS default-value emission — `"null"`/`"0"` defaults by type) → `"\0"`
- Create: `VisualGameStudio.Tests/Compiler/JavaScriptCharTests.cs`; roster +1

- [ ] **Step 1: Failing test.**

```csharp
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// O13 / spec §4.10 — Char on the JavaScript backend, a one-character string, so KeyPressEventArgs.KeyChar is a real
/// Char on both targets. Measured before (M18): BL7004 "JavaScript has no character type". The oracle is the same
/// program on C#.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class JavaScriptCharTests
{
    private const string Program =
        "Public Class Holder\n Public C As Char\nEnd Class\n" +
        "Sub Show(c As Char)\n Console.WriteLine(\"[\" & c & \"]\")\nEnd Sub\n" +
        "Function Next1(c As Char) As Char\n Return ChrW(AscW(c) + 1)\nEnd Function\n" +
        "Sub Main()\n Dim a As Char = \"a\"c\n Dim z As Char = \"z\"c\n Console.WriteLine(a)\n Console.WriteLine(a < z)\n" +
        " Console.WriteLine(AscW(a))\n Console.WriteLine(ChrW(66))\n Console.WriteLine(Asc(\"A\"c))\n Console.WriteLine(Chr(67))\n" +
        " Console.WriteLine(CStr(a) & \"!\")\n Show(z)\n Console.WriteLine(Next1(a))\n" +
        " Dim h As New Holder()\n Console.WriteLine(AscW(h.C))\n" +
        " Dim list As New List(Of Char)\n list.Add(\"x\"c)\n list.Add(\"y\"c)\n Console.WriteLine(list.Count)\n" +
        " Console.WriteLine(a = \"a\"c)\nEnd Sub\n";

    [Test]
    public void TheCharTable_MatchesCSharp()
    {
        var js = FourBackends.Norm(JavaScriptExecutionTests.RunJs(Program));
        var cs = FourBackends.Norm(FourBackends.RunEmittedCSharp(Program));
        Assert.Multiple(() =>
        {
            Assert.That(js, Is.EqualTo(cs), "JavaScript vs the C# oracle");
            Assert.That(cs, Is.EqualTo("a\nTrue\n97\nB\n65\nC\na!\n[z]\nb\n0\n2\nTrue"), "the oracle itself");
        });
    }
}
```
- [ ] **Step 2: Run — RED:** `ForeignFeatureException … BL7004 …` from `JsTestSupport.Compile`.
- [ ] **Step 3: Implement** (files above). ⚠ `JsCapabilityChecker.BaseName` (`:323-337`) maps `System.Char` to Char — nothing to change once the row is gone. ⚠ Search the JS tests for `BL7004` (grep): each asserting the refusal is updated to assert lowering (list them in the commit).
- [ ] **Step 4: Run — GREEN**, plus every fixture that grepped `BL7004`, `JsExecutionTierRosterTests`.
- [ ] **Step 5: Mutations:** (1) a `Char` default of `null` → `AscW(h.C)` row (`0`) red; (2) `chrw` unmapped → red.
- [ ] **Step 6: Commit.**

---

## Task 15: Program-declared namespaces win over .NET on a web build; dotted `Namespace` declarations

Spec §4.9, M8, scope call S8.

**Files:**
- Modify: `BasicLang/Parser.cs` — `ParseNamespace` (`:300-338`; `:305` quote `node.Name = Consume(TokenType.Identifier, "Expected namespace name").Lexeme;`): consume `Identifier ('.' Identifier)*`
- Modify: `BasicLang/SemanticAnalyzer.cs` — new `public void ConfigureTarget(string targetBackend)` and `public void ConfigureProgramNamespaces(IEnumerable<string> names)`; `ResolveTypeName` (`:2927`). **The `UsingDirectiveNode` visit (`:7118-7128`) is NOT changed** (S8 revised).
- Modify: `BasicLang/Compiler.cs` — beside `analyzer.ConfigureModuleSystem(_registry, _resolver, unit);` (`:750-751`): `analyzer.ConfigureTarget(_options.TargetBackend);` and `analyzer.ConfigureProgramNamespaces(<every NamespaceNode's FULL name across all parsed units — no prefixes>)` (collect once after Phase 1 parse, `:427-430`)
- Modify: `VisualGameStudio.Tests/Compiler/JsTestSupport.cs` (`BuildModule`: `analyzer.ConfigureTarget("javascript")` and the source's own namespaces)
- Create: `VisualGameStudio.Tests/Compiler/JavaScriptProgramNamespaceTests.cs`; roster +1

- [ ] **Step 1: Failing tests.**

```csharp
using System.IO;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.JavaScript;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec §4.9, M8 — `Namespace System.Windows.Forms` was a parse error; the nested spelling parsed, but `Using
/// System.Windows.Forms` and `System.Windows.Forms.Button` then resolved as .NET (BL6016) because every dotted Using is
/// marked .NET at parse time (Parser.cs:714-720). On a JavaScript build there is no .NET: a namespace the PROGRAM
/// declares wins. C# is unchanged (the library is never included there).
/// </summary>
[TestFixture]
[Category("Integration")]
public class JavaScriptProgramNamespaceTests
{
    private static string RunProject(params (string Name, string Text)[] files)
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "bl-ns-" + Path.GetRandomFileName())).FullName;
        try
        {
            var paths = files.Select(f => { var p = Path.Combine(dir, f.Name); File.WriteAllText(p, f.Text); return p; }).ToArray();
            var r = new BasicCompiler(new CompilerOptions { TargetBackend = "javascript" }).CompileProjectFiles(paths);
            Assert.That(r.HasErrors, Is.False, string.Join(" | ", r.AllErrors.Select(e => e.Message)));
            return FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(new JavaScriptCodeGenerator().Generate(r.CombinedIR!)));
        }
        finally { Directory.Delete(dir, true); }
    }

    private const string Library =
        "Namespace System.Windows.Forms\n" +
        "Public Class Control\n Public Overridable Function Describe() As String\n  Return \"Control\"\n End Function\nEnd Class\n" +
        "Public Class Button\n Inherits Control\n Public Overrides Function Describe() As String\n  Return \"Button\"\n End Function\nEnd Class\n" +
        "End Namespace\n";

    private const string Form =
        "Using System\nUsing System.Windows.Forms\n" +
        "Public Class Form1\n Public Sub Go()\n  Dim b As Button = New Button()\n  Dim c As Control = b\n" +
        "  Dim q As System.Windows.Forms.Button = b\n  PrintLine(c.Describe() & \"/\" & q.Describe())\n End Sub\nEnd Class\n" +
        "Module Program\n Sub Main()\n  Dim f As New Form1()\n  f.Go()\n End Sub\nEnd Module\n";

    [Test]
    public void ADottedNamespace_UsedThroughAUsing_AndQualified() =>
        Assert.That(RunProject(("Lib.bas", Library), ("Form1.bas", Form)), Is.EqualTo("Button/Button"));

    [Test]
    public void TheNestedSpelling_Works_Too() =>
        Assert.That(RunProject(
            ("Lib.bas", "Namespace System\nNamespace Windows\nNamespace Forms\n" +
                        Library.Replace("Namespace System.Windows.Forms\n", "").Replace("End Namespace\n", "") +
                        "End Namespace\nEnd Namespace\nEnd Namespace\n"),
            ("Form1.bas", Form)), Is.EqualTo("Button/Button"));

    [Test]
    public void ADottedNamespace_Parses() =>
        Assert.That(() => JsTestSupport.BuildModule("Namespace Acme.Widgets\nPublic Class W\nEnd Class\nEnd Namespace\nSub Main()\nEnd Sub"),
            Throws.Nothing);

    /// <summary>⛔ Guard (plan review): the library declares `Namespace System` (EventArgs) and System.Windows.Forms. A
    /// program that ALSO says `Using System` must keep Console, Math and a qualified System.Math working.</summary>
    [Test]
    public void TheStandardSurface_StillWorks_UnderUsingSystem_WhenTheProgramDeclaresSystemNamespaces() =>
        Assert.That(RunProject(
            ("Lib.bas", "Namespace System\nPublic Class EventArgs2\nEnd Class\nEnd Namespace\n" + Library),
            ("Main.bas", "Using System\nUsing System.Windows.Forms\nModule Program\n Sub Main()\n  Console.WriteLine(Math.Max(2, 3))\n" +
                         "  Console.WriteLine(System.Math.Abs(-4))\n  Dim b As New Button()\n  Console.WriteLine(b.Describe())\n End Sub\nEnd Module\n")),
            Is.EqualTo("3\n4\nButton"));
}
```
- [ ] **Step 2: Run — RED:** row 3 `parse failed … Unexpected token at top level: '.'`; rows 1–2 `Cannot assign value of type 'Button' to variable of type 'Control'` / BL6016-shaped failures (M8).
- [ ] **Step 3: Implement.** Parser: the dotted loop. Analyzer: `_isJavaScriptTarget` from `ConfigureTarget` (`BuildSymbols.IsWebBackend`); `_programNamespaces` set (full names only). In `ResolveTypeName`: when `_isJavaScriptTarget` and the name is dotted and its prefix (everything before the last `.`) is in `_programNamespaces` AND the last segment names a program TYPE (`ResolveTypeSymbol` [X]), return that type; otherwise fall through to today's path unchanged (so `System.Math.Abs` is still the stdlib's `Math`). Nothing about `Using` changes: `IsNetNamespace` and `_netNamespaces` keep their meaning, so the IR builder's heuristic (`IsKnownNetStaticType`, `IRBuilder.cs:6636-6656`, which reads `u.IsNetNamespace` at `:6649`) still routes `Console.X`/`Math.X` as before, and a program class is claimed first by `exactClassMatch` (`IRBuilder.cs:5941`) — confirm with the guard row. ⚠ Types are registered by bare name (`Visit(NamespaceNode)` `:5973-5986` only enters a scope) — two program namespaces declaring the same simple name is out of scope; record it as a known limit in the commit.
- [ ] **Step 4: Run — GREEN**, plus `NetBuildPipelineTests`, `CsFileScopeQualificationTests`, every test grepping `Namespace` in `BasicLang` parser tests, and the WinForms sweep's C# compile (`WinFormsTemplateBuildTests`) — C# must be unchanged.
- [ ] **Step 5: Mutations:** (1) re-mark a `Using` of a program namespace as not-.NET (the rejected design) → the guard row red (`Console`/`Math`); (2) skip the qualified-name arm → row 1 red on `q`; (3) drop the `_isJavaScriptTarget` gate → compare the C# output of a program with `Namespace Acme.Widgets` + a qualified `Acme.Widgets.W` before/after (add `ACSharpBuild_QualifiedProgramType_IsByteIdentical` pinning today's C# text if no existing test catches it).
- [ ] **Step 6: Commit.**

---

## Task 16: The library-idiom probe, committed

Spec §4.13, review I6.

**Files:**
- Create: `VisualGameStudio.Tests/Compiler/JavaScriptLibraryIdiomTests.cs`; roster +1

- [ ] **Step 1: Write it** (it is a GUARD for the idioms the library will use; most rows are green now — M21–M24 — and the MyBase row is green because of Task 13):

```csharp
using System.IO;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.JavaScript;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec §4.13 — the idioms the portable library relies on, each RUN under node against a stub DOM (node's own
/// EventTarget), compiled through the real project route so dom-core.bli is included. If a backend change breaks an
/// idiom, this fails before the library does. M25 is pinned as REFUSED: a DOM listener must be declared ABOVE the
/// method that attaches it.
/// </summary>
[TestFixture]
[Category("Integration")]
public class JavaScriptLibraryIdiomTests
{
    private const string StubDom =
        "globalThis.__els = {};\n" +
        "globalThis.document = { getElementById(id) { return globalThis.__els[id] ??= Object.assign(new EventTarget(), " +
        "{ id, click() { this.dispatchEvent(new Event('click')); } }); } };\n" +
        "globalThis.window = globalThis;\n";

    private static (bool Ok, string Output) Run(string source)
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "bl-idiom-" + Path.GetRandomFileName())).FullName;
        try
        {
            var file = Path.Combine(dir, "Main.bas");
            File.WriteAllText(file, source);
            var r = new BasicCompiler(new CompilerOptions { TargetBackend = "javascript" }).CompileProjectFiles(new[] { file });
            if (r.HasErrors) return (false, string.Join(" | ", r.AllErrors.Select(e => e.Message)));
            var js = new JavaScriptCodeGenerator().Generate(r.CombinedIR!);
            return (true, FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(StubDom + js)));
        }
        finally { Directory.Delete(dir, true); }
    }

    private const string Listener =
        "Public Class Ctl\n Private _el As Element\n Private _hits As Integer = 0\n" +
        " Private Sub VgsOnDomClick(e As DomEvent)\n  _hits = _hits + 1\n  Console.WriteLine(\"dom click \" & _hits)\n End Sub\n" +
        " Public Sub Attach(id As String)\n  Dim doc As Document = ::document\n  _el = doc.getElementById(id)\n" +
        "  _el.addEventListener(\"click\", AddressOf Me.VgsOnDomClick)\n End Sub\nEnd Class\n" +
        "Sub Main()\n Dim c As New Ctl()\n c.Attach(\"b\")\n Dim doc As Document = ::document\n doc.getElementById(\"b\").click()\nEnd Sub\n";

    [Test] public void M24_AListenerDeclaredAbove_SeesMe() => Assert.That(Run(Listener), Is.EqualTo((true, "dom click 1")));

    [Test]
    public void M23_ALambdaCallingMe() => Assert.That(Run(
        "Public Class Ctl\n Private _el As Element\n Private Sub Hit()\n  Console.WriteLine(\"hit\")\n End Sub\n" +
        " Public Sub Attach(id As String)\n  Dim doc As Document = ::document\n  _el = doc.getElementById(id)\n" +
        "  _el.addEventListener(\"click\", Sub(e As DomEvent) Me.Hit())\n End Sub\nEnd Class\n" +
        "Sub Main()\n Dim c As New Ctl()\n c.Attach(\"b\")\n Dim doc As Document = ::document\n doc.getElementById(\"b\").click()\nEnd Sub\n"),
        Is.EqualTo((true, "hit")));

    [Test]
    public void M21_OptionalParameters() => Assert.That(Run(
        "Public Class C\n Public Function D(Optional p As String = \"x\", Optional n As Integer = 3) As String\n  Return p & n\n End Function\nEnd Class\n" +
        "Sub Main()\n Dim c As New C()\n Console.WriteLine(c.D())\n Console.WriteLine(c.D(\"y\"))\n Console.WriteLine(c.D(\"z\", 7))\nEnd Sub\n"),
        Is.EqualTo((true, "x3\ny3\nz7")));

    [Test]
    public void M1_RaiseThroughABaseOnX() => Assert.That(Run(
        "Public Class EventArgs2\nEnd Class\n" +
        "Public Class Control\n Public Event Click(sender As Object, e As EventArgs2)\n" +
        " Protected Overridable Sub OnClick(e As EventArgs2)\n  RaiseEvent Click(Me, e)\n End Sub\n" +
        " Public Sub PerformClick()\n  Me.OnClick(New EventArgs2())\n End Sub\nEnd Class\n" +
        "Public Class Button\n Inherits Control\nEnd Class\n" +
        "Public Class Frm\n Private b As Button\n Public Sub New()\n  b = New Button()\n  AddHandler b.Click, AddressOf Me.H\n  b.PerformClick()\n End Sub\n" +
        " Private Sub H(sender As Object, e As EventArgs2)\n  If sender Is b Then\n   Console.WriteLine(\"sender ok\")\n  Else\n   Console.WriteLine(\"wrong sender\")\n  End If\n End Sub\nEnd Class\n" +
        "Sub Main()\n Dim f As New Frm()\nEnd Sub\n"),
        Is.EqualTo((true, "sender ok")));

    /// <summary>M4 fixed (Task 13) — the library's Text override calls through MyBase.</summary>
    [Test]
    public void M4_APropertyOverride_ThroughMyBase() => Assert.That(Run(
        "Public Class Control\n Private _t As String = \"\"\n Public Overridable Property Text As String\n  Get\n   Return _t\n  End Get\n" +
        "  Set(value As String)\n   _t = value\n  End Set\n End Property\nEnd Class\n" +
        "Public Class Label\n Inherits Control\n Public Overrides Property Text As String\n  Get\n   Return MyBase.Text\n  End Get\n" +
        "  Set(value As String)\n   MyBase.Text = \"[\" & value & \"]\"\n  End Set\n End Property\nEnd Class\n" +
        "Sub Main()\n Dim l As New Label()\n l.Text = \"hi\"\n Console.WriteLine(l.Text)\nEnd Sub\n"),
        Is.EqualTo((true, "[hi]")));

    /// <summary>M25 — the erasure: a listener declared BELOW its use is refused (the library's ordering rule).</summary>
    [Test]
    public void M25_AListenerDeclaredBelow_IsRefused()
    {
        var (ok, output) = Run(
            "Public Class Ctl\n Private _el As Element\n Public Sub Attach(id As String)\n  Dim doc As Document = ::document\n" +
            "  _el = doc.getElementById(id)\n  _el.addEventListener(\"click\", AddressOf Me.VgsLater)\n End Sub\n" +
            " Private Sub VgsLater(e As DomEvent)\n End Sub\nEnd Class\nSub Main()\nEnd Sub\n");
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(output, Does.Contain("Action<DomEvent>"));
        });
    }
}
```
  ⚠ Never name a probe class `F` (chip `task_ef845b99`, M16) — Task 0 re-measures that chip separately.
- [ ] **Step 2: Run** (roster +1 first). Every row GREEN; if one is red, it is a real defect in an idiom the library needs: fix it in this task (TDD, the failing row is the test) or stop and report.
- [ ] **Step 3: Mutation (proves the probe can fail):** in `UnaryText`, drop `__blBind` for the sibling-method arm and emit the bare `this.X` → `M24_…` red (`this` is undefined in the handler). Revert.
- [ ] **Step 4: Commit.**

---

## Task 17: Gate for 2.0a

- [ ] **Step 1: Clean build** of the Shell and the test project (Release).
- [ ] **Step 2: Fast subset** into `$sp\gate20a-fast.txt` (both streams). Compare the SORTED failure NAMES with `baseline-fast.txt` (Task 0): zero new names. State the base with every number.
- [ ] **Step 3: Integration by name:** every fixture Tasks 1–16 created or edited, plus `CrossFileBindingTests`, `ForwardDeclaredType*`, `JavaScriptEventTests`, `JavaScriptInteropTests`, `CppPassthroughTests`, `NetBuildPipelineTests`, `OverridablePropertyTests`, `InheritedMemberTests`, `JavaScriptProjectBuildTests`, `WinFormsTemplateBuildTests`, `WinFormsCatalogSweepTests`, `FormBuildEmissionTests`, `FormDesignerAcceptanceTests`, `WebMainStartupTests`, the LSP suite (`CrossFileAnalysisTests`, `LspMixedProjectTests`, `ModClsDocumentTests`, `CompletionServiceTests`), `JsExecutionTierRosterTests`. Compare names with `baseline-int.txt`.
- [ ] **Step 4: The mutation table** of Tasks 1–16 in one place (commit message or `docs/HANDOFF.md`): each mutant, the test that went red.
- [ ] **Step 5: Records.** `docs/HANDOFF.md` "NEWEST" (auto-memory does not travel): what 2.0a changed for users (`#If`, the symbols, the release note), the gate numbers with their base. `CLAUDE.md`: correct the MODULE-call matrix / JS-trap bullets that 2.0a makes stale (the unqualified self-call row, `Me.` inside a lambda, `RemoveHandler`), exactly as `40e9c172` corrected its rows.
- [ ] **Step 6: Commit.** No IDE drop (nothing the owner needs to click).

---

# Sub-piece 2.0b — exact `Decimal` on JavaScript (Tasks 18–27)

Spec §4.11 (O14), rows D1–D22. May run in parallel with 2a (after Task 17); must land before Task 38 (NumericUpDown).

**The oracle (scope call S10 — read it; it is the rule every 2.0b test follows).** BasicLang follows VB. For a row whose C# emission has VB's Decimal semantics, the assertion is: the SAME BasicLang program, compiled by the C# backend and run in-process (real `System.Decimal`), prints X; the JavaScript backend — plain AND optimized — prints X. **Exception rows E1–E4** (Decimal → integral narrowing by `CType`/implicit; boxed-Decimal `=`; `CType` of a boxed non-Decimal number to Decimal; `CShort`/`CByte` out of range) are asserted against **VB-literal expectations written in the test**, on BOTH backends, and Task 20A fixes the C# backend until it passes them. Never "change the expectation" to match a backend; never "correct the spec" (D19 stands). A few literal expectations on oracle rows guard against both being wrong alike. The tests set `CultureInfo.CurrentCulture = InvariantCulture` for the C# leg (JS prints invariant — D11). Operands are passed through `Function Id(d As Decimal) As Decimal` so neither the BasicLang optimizer nor Roslyn folds them into constants (Roslyn rejects an overflowing CONSTANT expression at compile time, CS0463, where the program means a run-time `OverflowException`).

**Task order in 2.0b: 18 → 19 → 20 → 20A → 21 → 22 → 23 → 24 → 25 → 26 → 27** (20A fixes the C# backend before the VB-literal rows of 21–22 are written).

**Every "out" row** is refused at compile time with `BL7014` (the next free BL70xx on `bc29391e`: BL7013 is the highest, `JavaScriptBackend.cs:3381`) and the message contains the NAME `DecimalNotSupportedOnWeb`, the construct, and the supported spelling where one exists. Tests assert the NAME, so the number can move.

## Task 18: Decimal literals and `CDec` — the front end, on every target

D1, D15 (part), spec-claims #6–#7.

**Files:**
- Modify: `BasicLang/BasicLangLexer.cs` — `ScanNumber` (`:1051-1132`; fractional suffix `f`/`F` `:1084-1089`, integer `L` `:1099`, `F` `:1106`); `TokenType` gains `DecimalLiteral`
- Modify: `BasicLang/Parser.cs` — where `TokenType.DoubleLiteral` becomes a literal expression (grep `DoubleLiteral` in `ParsePrimary`), add `DecimalLiteral` → a literal whose value is a `decimal`
- Modify: `BasicLang/SemanticAnalyzer.cs` — the literal's type is `Decimal` (the same `TypeInfo` `As Decimal` resolves to — `CommonNetTypes`, `:440`); register `CDec` beside `CDbl` (`:1557-1602`) returning `Decimal`
- Modify: `BasicLang/CSharpBackend.cs` — `VbConversionText` (`:240-275`): `CDec(x)` → `Convert.ToDecimal(x)`
- Create: `VisualGameStudio.Tests/Compiler/DecimalLiteralTests.cs`

- [ ] **Step 1: Failing tests.**

```csharp
using System.Globalization;
using System.Linq;
using BasicLang.Compiler;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec §4.11 D1/D15, spec-claims #6–#7 — `0.1D` lexed as `0.1` followed by an identifier `D` (ScanNumber knows only F
/// and L), and CDec was not a registered function at all, so `CDec("0.1")` typed as Object.
/// </summary>
[TestFixture]
public class DecimalLiteralTests
{
    private static Token[] Lex(string s) => new Lexer(s).Tokenize().Where(t => t.Type != TokenType.EOF && t.Type != TokenType.Newline).ToArray();

    [TestCase("0.1D", "0.1")]
    [TestCase("1.10D", "1.10")]
    [TestCase("5D", "5")]
    [TestCase("5d", "5")]
    [TestCase("79228162514264337593543950335D", "79228162514264337593543950335")]
    [TestCase("0.0000000000000000000000000001D", "0.0000000000000000000000000001")]
    public void ADSuffix_IsOneDecimalLiteral_KeepingItsScale(string text, string value)
    {
        var tokens = Lex(text);
        Assert.Multiple(() =>
        {
            Assert.That(tokens, Has.Length.EqualTo(1));
            Assert.That(tokens[0].Type, Is.EqualTo(TokenType.DecimalLiteral));
            Assert.That(((decimal)tokens[0].Value).ToString(CultureInfo.InvariantCulture), Is.EqualTo(value));
        });
    }

    [Test]
    public void AnIdentifierAfterTheDigits_IsNotASuffix() =>
        Assert.That(Lex("1Dx").Select(t => t.Type), Is.EqualTo(new[] { TokenType.IntegerLiteral, TokenType.Identifier }));

    /// <summary>In a Decimal context the suffix changes nothing in the C# output; inferred, it makes the variable Decimal.</summary>
    [Test]
    public void TheCSharpOutput()
    {
        var withSuffix = CSharpTestSupport.CompileToCSharp("Sub Main()\n Dim d As Decimal = 0.1D\n Console.WriteLine(d)\nEnd Sub");
        var without = CSharpTestSupport.CompileToCSharp("Sub Main()\n Dim d As Decimal = 0.1\n Console.WriteLine(d)\nEnd Sub");
        var inferred = CSharpTestSupport.CompileToCSharp("Sub Main()\n Dim x = 0.1D\n Console.WriteLine(x)\nEnd Sub");
        var cdec = CSharpTestSupport.CompileToCSharp("Sub Main()\n Dim d As Decimal = CDec(\"0.1\")\n Console.WriteLine(d)\nEnd Sub");
        Assert.Multiple(() =>
        {
            Assert.That(withSuffix, Is.EqualTo(without));
            Assert.That(withSuffix, Does.Contain("0.1m"));
            Assert.That(inferred, Does.Contain("decimal x").And.Contain("0.1m"));
            Assert.That(cdec, Does.Contain("Convert.ToDecimal("));
        });
    }
}
```
  ⚠ Confirm the `Token` type/property names (`Type`, `Value`) and the EOF/newline token kinds in `BasicLangLexer.cs` before running; confirm `CSharpTestSupport.CompileToCSharp` (`CSharpTestSupport.cs:58`) returns the C# text.
- [ ] **Step 2: Run — RED:** two tokens for `0.1D` (`DoubleLiteral`, `Identifier`), `DecimalLiteral` does not exist (compile error — record), `Convert.ToDecimal` absent.
- [ ] **Step 3: Implement.** In `ScanNumber`: after the digits (integer or fractional path), if the next char is `D`/`d` AND the char after it is not a letter, digit or `_`, consume it and produce `TokenType.DecimalLiteral` with `decimal.Parse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture)` (an out-of-range literal is a lexer error "Decimal literal out of range"). The integer path must NOT first parse the digits as `long` when the suffix is `D` (the max-value row). Parser and analyzer arms as listed. `IRBuilder` already turns a Decimal-typed literal into `IRConstant(decimal)` (`IRBuilder.cs:5412-5417`) and C# prints it as `…m` (`CSharpBackend.cs:5008-5009`) — verify, do not duplicate.
- [ ] **Step 4: Run — GREEN**, plus `LexerTests`, `CTypeConversionTests`, `NarrowIntegerWrapTests`, every test grepping `Decimal` in `VisualGameStudio.Tests/Compiler`.
- [ ] **Step 5: Mutations:** (1) accept `D` even when a letter follows → `AnIdentifierAfterTheDigits_…` red; (2) parse the integer digits as `long` before checking the suffix → the max-value row red.
- [ ] **Step 6: Commit.**

## Task 19: The `VgsDecimal` runtime — literals, locals, members, printing

D1, D2, D11, D22.

**Files:**
- Modify: `BasicLang/JsCapabilityChecker.cs` — `BuildAllowedTypeNames` (`:402-457`): allow `Decimal`/`System.Decimal`
- Modify: `BasicLang/JavaScriptBackend.cs` — prelude (`:184-187`): `EmitDecimalPrelude(module)` BEFORE `EmitConversionPrelude` when `UsesDecimal(module)` (a scan of the WHOLE module the generator receives — on the project route that is the merged module of every unit, `Compiler.cs:460-461` — covering every function's locals/parameters/returns/instructions, every class's fields/properties/methods, globals and constants; a Decimal only in a sibling file's field must be found — `DecimalOnlyInASiblingFilesField_OnEveryRoute`); the constant renderer's `case decimal m:` (`:1727`) → `VgsDecimal.parse("<m invariant>")`; `__blStr` (in `EmitConversionPrelude`, `:476`) returns `v.toString()` for a `VgsDecimal`; the default value of a Decimal local/field/element → `VgsDecimal.ZERO`
- Modify: `BasicLang/JavaScriptBackend.cs` `EmitExceptionPrelude` (`:611`) and `BasicLang/JsExceptionTypes.cs` (the provided exception list) — a module that uses Decimal needs `OverflowException`, `DivideByZeroException`, `FormatException`, `InvalidCastException`: read how `EmitExceptionPrelude` decides which classes to emit and count Decimal usage as needing these four (the Decimal prelude is emitted AFTER the exception prelude — the TDZ comment at `:182-183`)
- Create: `VisualGameStudio.Tests/Compiler/JavaScriptDecimalTests.cs` (roster +1); later 2.0b tasks Edit it

- [ ] **Step 1: Failing tests.**

```csharp
using System;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// O14 / spec §4.11 — Decimal on the JavaScript backend, EXACT, with .NET as the oracle (plan S10): each program runs on
/// the C# backend in-process (real System.Decimal, InvariantCulture) and on JavaScript plain and optimized, and the
/// three must agree. Measured before (M20): BL7007 "'Decimal' is not available on the JavaScript backend".
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class JavaScriptDecimalTests
{
    private CultureInfo _saved = CultureInfo.CurrentCulture;

    [SetUp] public void Invariant() { _saved = CultureInfo.CurrentCulture; CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; }
    [TearDown] public void Restore() => CultureInfo.CurrentCulture = _saved;

    /// <summary>Runs <paramref name="program"/> on C# (the oracle) and JavaScript (plain + optimized); returns the oracle's output.</summary>
    internal static string Both(string program)
    {
        var cs = FourBackends.Norm(FourBackends.RunEmittedCSharp(program));
        var js = FourBackends.Norm(JavaScriptExecutionTests.RunJs(program));
        var jsOpt = FourBackends.Norm(JavaScriptOptimizedExecutionTests.RunOptimized(program));
        Assert.Multiple(() =>
        {
            Assert.That(js, Is.EqualTo(cs), "JavaScript vs .NET");
            Assert.That(jsOpt, Is.EqualTo(cs), "JavaScript (optimized) vs .NET");
        });
        return cs;
    }

    /// <summary>A BasicLang Decimal literal for <paramref name="d"/>, scale AND SIGN kept — `-0.0m` is not `&lt; 0`, so the
    /// sign is read from the bits (decimal.GetBits' flags word), never from a comparison.</summary>
    internal static string L(decimal d)
    {
        var negative = (decimal.GetBits(d)[3] & unchecked((int)0x80000000)) != 0;
        var magnitude = (negative ? -d : d).ToString(CultureInfo.InvariantCulture);
        return negative ? $"(-{magnitude}D)" : magnitude + "D";
    }

    /// <summary>VB-literal expectations (the exception rows E1–E4) are asserted on BOTH backends.</summary>
    internal static void BothEqual(string program, string vbExpected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo(vbExpected), "C# vs VB");
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(program)), Is.EqualTo(vbExpected), "JavaScript vs VB");
            Assert.That(FourBackends.Norm(JavaScriptOptimizedExecutionTests.RunOptimized(program)), Is.EqualTo(vbExpected), "JavaScript (optimized) vs VB");
        });
    }

    /// <summary>Operands go through Id so no optimizer — BasicLang's or Roslyn's — folds them.</summary>
    internal const string Id = "Function Id(d As Decimal) As Decimal\n Return d\nEnd Function\n";

    // ------------------------------------------------------------ D1, D2, D11: literals, Const, text

    [Test]
    public void LiteralsKeepTheirScale() => Assert.That(Both(
        "Const Rate As Decimal = 0.075D\nSub Main()\n Console.WriteLine(1.10D)\n Console.WriteLine(5D)\n Console.WriteLine(0D)\n" +
        " Console.WriteLine(-2.500D)\n Console.WriteLine(79228162514264337593543950335D)\n Console.WriteLine(-79228162514264337593543950335D)\n" +
        " Console.WriteLine(0.0000000000000000000000000001D)\n Console.WriteLine(Rate)\nEnd Sub\n"),
        Is.EqualTo("1.10\n5\n0\n-2.500\n79228162514264337593543950335\n-79228162514264337593543950335\n0.0000000000000000000000000001\n0.075"));

    [Test]
    public void TheTextForms() => Assert.That(Both(Id +
        "Sub Main()\n Dim d As Decimal = Id(1.50D)\n Console.WriteLine(CStr(d))\n Console.WriteLine(d & \"!\")\n" +
        " Console.WriteLine($\"[{d}]\")\n Console.WriteLine(d.ToString())\n Console.WriteLine(String.Format(\"<{0}>\", d))\nEnd Sub\n"),
        Is.EqualTo("1.50\n1.50!\n[1.50]\n1.50\n<1.50>"));

    // ------------------------------------------------------------ the PROJECT route: UsesDecimal cannot miss

    /// <summary>
    /// Decimal appears ONLY in a sibling file's class field. A `UsesDecimal` scan that looked at one unit, or at locals
    /// only, would leave VgsDecimal out of App.js — "VgsDecimal is not defined" at run time from a green build.
    /// Compiled through CompileProjectFiles, the real CLI and the IDE's BuildService.
    /// </summary>
    [Test]
    public async System.Threading.Tasks.Task DecimalOnlyInASiblingFilesField_OnEveryRoute()
    {
        var dir = System.IO.Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "bl-decproj-" + System.IO.Path.GetRandomFileName())).FullName;
        try
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "Account.bas"),
                "Public Class Account\n Public Balance As Decimal = 2.50D\n Public Function Show() As String\n  Return CStr(Balance)\n End Function\nEnd Class\n");
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "Main.bas"),
                "Module Program\n Sub Main()\n  Dim a As New Account()\n  Console.WriteLine(a.Show())\n End Sub\nEnd Module\n");
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "Site.blproj"),
                "<Project>\n  <PropertyGroup>\n    <ProjectName>Site</ProjectName>\n    <TargetBackend>JavaScript</TargetBackend>\n" +
                "  </PropertyGroup>\n  <ItemGroup>\n    <Compile Include=\"Main.bas\" />\n    <Compile Include=\"Account.bas\" />\n  </ItemGroup>\n</Project>\n");

            var r = new BasicLang.Compiler.BasicCompiler(new BasicLang.Compiler.CompilerOptions { TargetBackend = "javascript" })
                .CompileProjectFiles(new[] { System.IO.Path.Combine(dir, "Main.bas"), System.IO.Path.Combine(dir, "Account.bas") });
            Assert.That(r.HasErrors, Is.False);
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(
                new BasicLang.Compiler.CodeGen.JavaScript.JavaScriptCodeGenerator().Generate(r.CombinedIR!))), Is.EqualTo("2.50"), "API");

            var (exit, stdout, stderr) = await CliTestHarness.RunCli(dir, "build", "Site.blproj");
            Assert.That(exit, Is.Zero, stdout + stderr);
            var js = System.IO.Directory.GetFiles(System.IO.Path.Combine(dir, "bin"), "Site.js", System.IO.SearchOption.AllDirectories).Single();
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(System.IO.File.ReadAllText(js))), Is.EqualTo("2.50"), "CLI");

            var project = await new VisualGameStudio.ProjectSystem.Serialization.ProjectSerializer().LoadAsync(System.IO.Path.Combine(dir, "Site.blproj"));
            var built = await new VisualGameStudio.ProjectSystem.Services.BuildService(new RecordingOutput()).BuildProjectAsync(project);
            Assert.That(built.Success, Is.True);
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(built.GeneratedCode)), Is.EqualTo("2.50"), "IDE");
        }
        finally { System.IO.Directory.Delete(dir, true); }
    }

    // ------------------------------------------------------------ D22: members, parameters, returns, defaults

    [Test]
    public void FieldsPropertiesParametersReturnsAndDefaults() => Both(Id +
        "Public Class Account\n Public Balance As Decimal\n Private _limit As Decimal = 100.00D\n" +
        " Public Property Limit As Decimal\n  Get\n   Return _limit\n  End Get\n  Set(value As Decimal)\n   _limit = value\n  End Set\n End Property\nEnd Class\n" +
        "Function Pass(d As Decimal) As Decimal\n Return d\nEnd Function\n" +
        "Sub Main()\n Dim a As New Account()\n Console.WriteLine(a.Balance)\n Console.WriteLine(a.Limit)\n a.Limit = Id(2.5D)\n" +
        " Console.WriteLine(a.Limit)\n Console.WriteLine(Pass(Id(7.000D)))\n Dim local As Decimal\n Console.WriteLine(local)\nEnd Sub\n");
}
```
- [ ] **Step 2: Run** (roster +1 first) — **RED:** `ForeignFeatureException … BL7007 … 'Decimal' is not available …` from the JS legs; the C# legs are green.
- [ ] **Step 3: Implement the runtime.** The prelude text (a reference implementation — **the oracle is the arbiter**: where a table row disagrees, change this code, never the expectation):

```csharp
        private const string DecimalPrelude = """
            // VgsDecimal — exact System.Decimal semantics for BasicLang's JavaScript backend.
            // Portions derived from dotnet/runtime (System.Decimal.DecCalc: VarR8FromDec, VarDecFromR8 and its
            // power-of-ten table) — (c) .NET Foundation and Contributors, MIT License.
            class VgsDecimal {
              static MAX = (1n << 96n) - 1n;
              static ZERO = new VgsDecimal(false, 0n, 0);
              // .NET's double powers of ten, as double LITERALS (from 1e23 on a literal is not exact — the table must
              // be literals, never a computed power). A static FIELD: built once, not per call.
              static DOUBLE_POWERS_10 = [1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14,
                                         1e15, 1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22, 1e23, 1e24, 1e25, 1e26, 1e27, 1e28];
              constructor(neg, mant, scale) { this.neg = neg && mant !== 0n; this.mant = mant; this.scale = scale; }
              static pow10(n) { return 10n ** BigInt(n); }
              signed() { return this.neg ? -this.mant : this.mant; }
              // .NET's rule: at most 28 fractional digits and a 96-bit magnitude; drop digits with ONE banker's
              // rounding over everything dropped (never digit by digit — that double-rounds).
              static reduce(neg, mant, scale) {
                const MAX = VgsDecimal.MAX;
                let k = Math.max(0, scale - 28);
                while (k < scale && mant / VgsDecimal.pow10(k) > MAX) k++;
                if (k > 0) {
                  const p = VgsDecimal.pow10(k), q = mant / p, r = mant % p, half = p / 2n;
                  mant = (r > half || (r === half && (q & 1n) === 1n)) ? q + 1n : q;
                  scale -= k;
                  if (mant > MAX && scale > 0) return VgsDecimal.reduce(neg, mant, scale);
                }
                if (mant > MAX) throw new OverflowException("Value was either too large or too small for a Decimal.");
                return new VgsDecimal(neg, mant, scale);
              }
              static fromSigned(v, scale) { return VgsDecimal.reduce(v < 0n, v < 0n ? -v : v, scale); }
              static align(a, b) {
                const s = Math.max(a.scale, b.scale);
                return [a.signed() * VgsDecimal.pow10(s - a.scale), b.signed() * VgsDecimal.pow10(s - b.scale), s];
              }
              static fromInt(n) { const v = BigInt(Math.trunc(n)); return new VgsDecimal(v < 0n, v < 0n ? -v : v, 0); }
              // ⛔ (decimal)double is .NET's DecCalc.VarDecFromR8 — PORT IT, line for line (Task 22), from
              // dotnet/runtime src/libraries/System.Private.CoreLib/src/System/Decimal.DecCalc.cs (MIT; add the
              // attribution is the notice at the top of this prelude + THIRD-PARTY-NOTICES.md). It scales the DOUBLE by a double power of ten chosen from the
              // binary exponent, rounds the scaled double half-to-even to an integer of ~15 significant digits, and
              // strips trailing zeros — arithmetic in IEEE doubles, which JavaScript reproduces exactly when the same
              // operations and the same power-of-ten table are used. A decimal-string approach (toExponential/
              // toPrecision) rounds the EXACT binary value instead and differs from .NET on edge cases.
              static fromNumber(d) { return VgsDecimal.varDecFromR8(d); }
              static varDecFromR8(d) { /* the port — Task 22 */ throw new Error("VarDecFromR8 not yet ported"); }
              // NumberStyles.Number, invariant: white space, a leading or trailing sign, group separators, one point.
              static parse(text) {
                const m = String(text).trim().match(/^([+-])?([\d,]*)(?:\.(\d*))?([+-])?$/);
                if (!m || (m[1] && m[4])) throw new FormatException("The input string '" + text + "' was not in a correct format.");
                const whole = m[2].replace(/,/g, ""), frac = m[3] ?? "";
                if (whole === "" && frac === "") throw new FormatException("The input string '" + text + "' was not in a correct format.");
                return VgsDecimal.reduce((m[1] ?? m[4]) === "-", BigInt((whole || "0") + frac), frac.length);
              }
              add(b) { const [x, y, s] = VgsDecimal.align(this, b); return VgsDecimal.fromSigned(x + y, s); }
              sub(b) { const [x, y, s] = VgsDecimal.align(this, b); return VgsDecimal.fromSigned(x - y, s); }
              mul(b) { return VgsDecimal.fromSigned(this.signed() * b.signed(), this.scale + b.scale); }
              div(b) {
                if (b.mant === 0n) throw new DivideByZeroException("Attempted to divide by zero.");
                let scale = this.scale - b.scale, num = this.mant;
                const den = b.mant, MAX = VgsDecimal.MAX;
                while (scale < 0) { num *= 10n; scale++; }
                let q = num / den, r = num % den;
                while (r !== 0n && scale < 28) {
                  const next = q * 10n + (r * 10n) / den;
                  if (next > MAX) break;
                  r = (r * 10n) % den; q = next; scale++;
                }
                if (r !== 0n) { const twice = r * 2n; if (twice > den || (twice === den && (q & 1n) === 1n)) q++; }
                return VgsDecimal.reduce(this.neg !== b.neg, q, scale);
              }
              mod(b) {
                if (b.mant === 0n) throw new DivideByZeroException("Attempted to divide by zero.");
                const [x, y, s] = VgsDecimal.align(this, b);
                return VgsDecimal.fromSigned(x % y, s);
              }
              negate() { return new VgsDecimal(!this.neg, this.mant, this.scale); }
              cmp(b) { const [x, y] = VgsDecimal.align(this, b); return x < y ? -1 : (x > y ? 1 : 0); }
              equals(b) { return b instanceof VgsDecimal && this.cmp(b) === 0; }
              // mode: "even" (Math.Round / CInt), "floor", "ceiling", "trunc".
              roundTo(n, mode) {
                if (this.scale <= n) return this;
                const p = VgsDecimal.pow10(this.scale - n), r = this.mant % p;
                let q = this.mant / p;
                if (r !== 0n) {
                  if (mode === "even") { const half = p / 2n; if (r > half || (r === half && (q & 1n) === 1n)) q++; }
                  else if (mode === "floor" && this.neg) q++;
                  else if (mode === "ceiling" && !this.neg) q++;
                }
                return new VgsDecimal(this.neg, q, n);
              }
              abs() { return new VgsDecimal(false, this.mant, this.scale); }
              // VB's Decimal → integral (CInt/CShort/CByte and CType/implicit narrowing — exception row E1/E4): round
              // half-to-even, then OverflowException outside the target's range (never a wrap).
              toIntegral(min, max, typeName) {
                const v = this.roundTo(0, "even").signed();
                if (v < min || v > max) throw new OverflowException("Value was either too large or too small for " + typeName + ".");
                return Number(v);
              }
              toInt32() { return this.toIntegral(-2147483648n, 2147483647n, "an Int32"); }
              toInt16() { return this.toIntegral(-32768n, 32767n, "an Int16"); }
              toByte() { return this.toIntegral(0n, 255n, "an unsigned byte"); }
              // (double)decimal is .NET's DecCalc.VarR8FromDec: ((double)lo64 + (double)hi32 * 2^64) / 10^scale — the
              // ulong→double conversion, the sum and the division each round in IEEE doubles, and JS rounds the same
              // operations identically; 10^scale is DOUBLE_POWERS_10 (the same literals .NET uses).
              toNumber() {
                const lo = this.mant & ((1n << 64n) - 1n), hi = this.mant >> 64n;
                const d = (Number(lo) + Number(hi) * 18446744073709551616) / VgsDecimal.DOUBLE_POWERS_10[this.scale];
                return this.neg ? -d : d;
              }
              toString() {
                let digits = this.mant.toString();
                if (this.scale > 0) {
                  digits = digits.padStart(this.scale + 1, "0");
                  digits = digits.slice(0, digits.length - this.scale) + "." + digits.slice(digits.length - this.scale);
                }
                return (this.neg ? "-" : "") + digits;
              }
              static objEquals(a, b) {
                if (a instanceof VgsDecimal && b instanceof VgsDecimal) return a.cmp(b) === 0;
                return a === b;
              }
            }

            """;
```
  Wire: `UsesDecimal`, `EmitDecimalPrelude` (before the conversion prelude — `__blStr` references the class), constants, defaults, `__blStr`, the allow-list, the exception types. **Licence:** the prelude's first lines are the dotnet/runtime notice comment above (it is emitted into every user's App.js), and `THIRD-PARTY-NOTICES.md` gains one entry covering `VarR8FromDec`, `VarDecFromR8` and the power-of-ten table (the pattern of the existing dotnet/winforms entry for the catalog's descriptions). A test pins it on the EMITTED text: add to `JavaScriptDecimalTests`

```csharp
    /// <summary>MIT: the ported code ships inside every user's App.js, so the notice must be IN the emitted prelude.</summary>
    [Test]
    public void TheEmittedPrelude_CarriesTheDotnetRuntimeNotice()
    {
        var js = JsTestSupport.CompileOptimized("Sub Main()\n Dim d As Decimal = 1.5D\n Console.WriteLine(d)\nEnd Sub\n");
        Assert.Multiple(() =>
        {
            Assert.That(js, Does.Contain("class VgsDecimal"));
            Assert.That(js, Does.Contain("Portions derived from dotnet/runtime (System.Decimal.DecCalc"));
            Assert.That(js, Does.Contain("MIT License"));
        });
    }
``` Arithmetic/comparison/conversion lowering is Tasks 20–22; this task needs only literals, assignment, members and printing.
- [ ] **Step 4: Run — GREEN**, plus `JsExecutionTierRosterTests`, `JavaScriptCodeGenTests`, every test grepping `BL7007` (a Decimal refusal test there becomes a lowering test — list them).
- [ ] **Step 5: Mutations:** (0) `UsesDecimal` scans only function bodies (not class fields) → `DecimalOnlyInASiblingFilesField_OnEveryRoute` red with `VgsDecimal is not defined`; (1) render the constant with `m.ToString()` under `sv-SE` culture (drop `InvariantCulture`) → with the test run under `sv-SE` (set it in a one-off test) `LiteralsKeepTheirScale` red; (2) `__blStr` without the Decimal arm → `TheTextForms`/`LiteralsKeepTheirScale` red (`[object Object]`); (3) default `null` instead of `ZERO` → `FieldsProperties…` red.
- [ ] **Step 6: Commit.**

## Task 20: Arithmetic, overflow, division by zero

D3, D20.

**Files:**
- Modify: `BasicLang/JavaScriptBackend.cs` — `RenderBinary` (`:1411-1478`): FIRST, when either operand or the result is `Decimal`, return `DecimalBinary(op, l, r)` (Add → `l.add(r)`, Sub → `.sub`, Mul → `.mul`, Div → `.div`, Mod → `.mod`; a non-Decimal operand the analyzer let through is wrapped: integral → `VgsDecimal.fromInt(x)`); `UnaryText` (`:1527`): Neg of a Decimal → `.negate()`
- Modify: `VisualGameStudio.Tests/Compiler/JavaScriptDecimalTests.cs` (Edit)

- [ ] **Step 1: Failing tests** (append):

```csharp
    // ------------------------------------------------------------ D3, D20: arithmetic, overflow, division by zero

    /// <summary>
    /// ≥ 200 cases (D3): every ordered pair of these 15 values = 225 pairs. The set covers scale alignment (1.10 vs
    /// 2.205), the 28-digit cut (0.0000000000000000000000000001, 0.3333…3), 96-bit overflow (Max, Min), negative zero
    /// (built from bits, since the literal -0.0m is not &lt; 0 — see L), zero divisors, and a mixed-scale large value.
    /// </summary>
    private static readonly decimal[] Values =
    {
        1.10m, 2.205m, 0.1m, 3m, -7.5m, 0.5m, 123456789.123456789m, 0.0000000000000000000000000001m,
        -0.001m, decimal.MaxValue, decimal.MinValue, 0.3333333333333333333333333333m,
        12345678901234567890.12345678m, 0m, new decimal(0, 0, 0, true, 1) /* -0.0 */,
    };

    private static readonly (decimal A, decimal B)[] Pairs =
        Values.SelectMany(a => Values.Select(b => (a, b))).ToArray();

    private static string Guarded(string expression) =>
        $" Try\n  Console.WriteLine({expression})\n Catch ex As OverflowException\n  Console.WriteLine(\"overflow\")\n" +
        " Catch ex As DivideByZeroException\n  Console.WriteLine(\"div0\")\n End Try\n";

    [Test]
    public void TheArithmeticTable()
    {
        var sb = new StringBuilder(Id).Append("Sub Main()\n Dim a As Decimal\n Dim b As Decimal\n");
        foreach (var (a, b) in Pairs)
        {
            sb.Append($" a = Id({L(a)})\n b = Id({L(b)})\n");
            foreach (var op in new[] { "+", "-", "*", "/", "Mod" }) sb.Append(Guarded($"a {op} b"));
            sb.Append(Guarded("-a"));
        }
        var oracle = Both(sb.Append("End Sub\n").ToString());
        Assert.That(oracle.Split('\n'), Has.Length.EqualTo(Pairs.Length * 6), "every row printed exactly one line");
    }

    [Test]
    public void KnownAnswers() => Assert.That(Both(Id +
        "Sub Main()\n Console.WriteLine(Id(1D) / Id(3D))\n Console.WriteLine(Id(2D) / Id(3D))\n Console.WriteLine(Id(0.1D) + Id(0.2D))\n" +
        " Console.WriteLine(Id(1.10D) * Id(2D))\nEnd Sub\n"),
        Is.EqualTo("0.3333333333333333333333333333\n0.6666666666666666666666666667\n0.3\n2.20"));
```
- [ ] **Step 2: Run — RED:** JS prints JS string concatenations / `NaN`-shaped output or throws a `TypeError` (`node exited 1`) — record which.
- [ ] **Step 3: Implement** (files above).
- [ ] **Step 4: Run — GREEN.** Any row where the reference `div`/`reduce` disagrees with .NET: fix the runtime; record the case and the fix in the commit.
- [ ] **Step 5: Mutations:** (1) `reduce` rounds digit by digit (the double-rounding loop) → a row red (if none, add a pair that exposes it — a product whose dropped digits are `…49…` followed by a non-zero tail — and record); (2) `div` rounds half up → `KnownAnswers` row 2 or a table row red; (3) no overflow throw → the Max/Min rows red; (4) **scale alignment** — `align` multiplies only the first operand → the 1.10 / 2.205 rows red; (5) **the 28-digit cut** — `reduce` starts `k` at `scale - 27` → the 0.3333… and 1e-28 rows red.
  ⚠ The table prints `Pairs.Length * 6` = 1350 lines in one program; if node or the C# leg is slow, split the table into three programs (by the first value) in ONE test — never drop cases.
- [ ] **Step 6: Commit.**

## Task 20A: The C# backend follows VB for Decimal (exception rows E1–E4)

Plan review CRITICAL; scope call S10. The C# backend is a BasicLang backend, and BasicLang follows VB. Measured by the reviewer on `5b4ca51e`: `EmitCastText` (≈4467-4498; `:4443-4477` [X]) rounds a narrowing only when the source is Double/Single (`IsFloatingTypeName(sourceName)`), so `CType(d, Integer)` and implicit Decimal → Integer emit `(int)(d)` — TRUNCATION; `o = p` on two `Object`s compares references; `CType(o, Decimal)` of a boxed Integer is a C# unbox that throws `InvalidCastException`. This task runs BEFORE Task 21, so the VB-literal rows of Tasks 21–22 are green on C# when they are written.

**Files:**
- Modify: `BasicLang/CSharpBackend.cs` — `EmitCastText`: (E1/E4) the rounding rule's condition becomes `(IsFloatingTypeName(sourceName) || sourceName == "Decimal") && IsIntegralTypeName(targetName)` → `Convert.To{Int32|Int16|Byte|…}(…)` (banker's rounding AND `OverflowException`, exactly VB's `CInt`/`CShort`/`CByte` on a Decimal); (E3) a new arm before the reference-cast arm: `sourceName == "Object" && targetName == "Decimal"` → `Convert.ToDecimal({valueExpr})` (VB converts a boxed Integer/Double; a non-convertible object still throws `InvalidCastException`); the `CShort`/`CByte` builtins with a Decimal argument (grep `CShort` in `CSharpBackend.cs`/`VbConversionText` `:240-275`) → `Convert.ToInt16`/`Convert.ToByte`; (E2) a comparison `=`/`<>` reaches the C# backend as an **`IRCompare`** (built by `IRBuilder.Visit(BinaryExpressionNode)` — `IsComparisonOperator` → `new IRCompare(tempName, cmpKind, left, right, resultType)`, `IRBuilder.cs:5236-5239` [X]; never an `IRBinaryOp` Eq/Ne) and is rendered in THREE places that must agree: `Visit(IRCompare)` (`CSharpBackend.cs:4017-4029` [X], `WriteLine($"{target} = {left} {op} {right};")`) and the two inline renderers `case IRCompare cmp:` (`:3637` and `:3841` [X]). One private method `CompareText(IRCompare, Func<IRValue,string> render)` replaces the three spellings, and routes `Equal`/`NotEqual` to `__BlObjectEquals(a, b)` / `!__BlObjectEquals(a, b)` **ONLY WHEN (both static types are `Object`) OR (one is `Object` and the other `Decimal`)** — ⛔ the plan-review decision: an `Object` compared with any OTHER primitive (`o = 5`, `o = 5.0`, a Long) stays EXACTLY as emitted today (CS0019 — a refusal, never a silently-false comparison: two boxed ints are never reference-equal). VB value equality for boxed numbers IN GENERAL is out of this piece — a recorded follow-up. The helper is emitted once into the generated module class when used:

```csharp
    private static bool __BlObjectEquals(object a, object b)
    {
        // VB compares boxed NUMBERS by value (spec D19). Only a pair involving a Decimal is widened — every other
        // pair keeps the reference comparison this backend has always emitted for two Objects (a KNOWN VB divergence
        // for two boxed Integers of equal value, recorded as a follow-up), so nothing else changes behaviour.
        if ((a is decimal || b is decimal) && __BlIsNumber(a) && __BlIsNumber(b))
            return System.Convert.ToDecimal(a) == System.Convert.ToDecimal(b);
        return a == b;
    }
    private static bool __BlIsNumber(object o) =>
        o is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;
```
- Modify: `VisualGameStudio.Tests/Compiler/FourBackends.cs` — beside `RunEmittedCSharpText` (`:136`), a NON-ASSERTING compile that returns diagnostics (an `Assert` that fails inside a helper is recorded by NUnit even when the caller catches the exception, so "expect it to fail" needs a helper that never asserts). Share the reference list and compilation options with `RunEmittedCSharpText` (extract them into one private method, so the two cannot drift):

```csharp
    /// <summary>Compiles <paramref name="csharp"/> as RunEmittedCSharpText does and RETURNS the errors — never
    /// asserts, so a test can expect a refusal (CS0019) without NUnit recording a failure.</summary>
    internal static IReadOnlyList<Diagnostic> TryCompileCSharp(string csharp)
    {
        using var ms = new MemoryStream();
        var emitted = CreateCompilation(csharp).Emit(ms);
        return emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
    }

    private static CSharpCompilation CreateCompilation(string csharp) =>
        CSharpCompilation.Create(
            "FourBackendsProbe_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(csharp) },
            AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location))
                .Cast<MetadataReference>()
                .ToImmutableArray(),
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));
```
  (`RunEmittedCSharpText` then calls `CreateCompilation(csharp)`; add `using System.Collections.Generic;`.) Task 22's mixing grid uses this helper too.
- Create: `VisualGameStudio.Tests/Compiler/CSharpDecimalVbRulesTests.cs` (Integration, in-process Roslyn, `[NonParallelizable]`)

- [ ] **Step 1: Failing tests** (VB-literal expectations, C# only here — the JS legs come in Tasks 21–22):

```csharp
using System.Globalization;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Plan review CRITICAL / S10 — BasicLang follows VB, and the C# backend diverged on four Decimal rules: a narrowing
/// truncated (EmitCastText rounded only Double/Single), CShort/CByte of an out-of-range Decimal did not throw,
/// boxed-Decimal `=` compared references (spec D19 says values — owner-approved), and CType of a boxed Integer to
/// Decimal threw InvalidCastException. Expectations are VB's, written here; the backend is fixed to meet them.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class CSharpDecimalVbRulesTests
{
    private CultureInfo _saved = CultureInfo.CurrentCulture;
    [SetUp] public void Invariant() { _saved = CultureInfo.CurrentCulture; CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; }
    [TearDown] public void Restore() => CultureInfo.CurrentCulture = _saved;

    private const string Id = "Function Id(d As Decimal) As Decimal\n Return d\nEnd Function\n";

    private static string Overflow(string expr) =>
        $" Try\n  Console.WriteLine({expr})\n Catch ex As OverflowException\n  Console.WriteLine(\"overflow\")\n End Try\n";

    [Test]
    public void E1_NarrowingRoundsHalfToEven() => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(Id +
        "Sub Main()\n Console.WriteLine(CType(Id(2.5D), Integer))\n Console.WriteLine(CType(Id(3.5D), Integer))\n" +
        " Console.WriteLine(CType(Id(-2.5D), Integer))\n Dim j As Integer = Id(3.5D)\n Console.WriteLine(j)\n" +
        Overflow("CType(Id(2147483648D), Integer)") + "End Sub\n")),
        Is.EqualTo("2\n4\n-2\n4\noverflow"));

    [Test]
    public void E4_CShortAndCByte_OutOfRange_Overflow() => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(Id +
        "Sub Main()\n Console.WriteLine(CShort(Id(2.5D)))\n Console.WriteLine(CByte(Id(255.4D)))\n" +
        Overflow("CShort(Id(40000D))") + Overflow("CByte(Id(256D))") + Overflow("CByte(Id(-1D))") + "End Sub\n")),
        Is.EqualTo("2\n255\noverflow\noverflow\noverflow"));

    /// <summary>A boxed value the optimizer cannot see through — a constant `Dim o As Object = 5` is copy-propagated
    /// and FOLDED (measured on the spec base `14c2e17d`: `o = 5` emits `Console.WriteLine(true)`, and `o = 5.0` emits
    /// `Console.WriteLine(false)` where VB says True — a separate finding, recorded below).</summary>
    private const string Box = "Function Box(n As Integer) As Object\n Return n\nEnd Function\n";

    [Test]
    public void E2_E3_Boxing() => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(Id + Box +
        "Public Class Thing\nEnd Class\n" +
        "Sub Main()\n Dim o As Object = Id(1.10D)\n Dim p As Object = Id(1.1D)\n Console.WriteLine(o = p)\n Console.WriteLine(o <> p)\n" +
        " Dim i As Object = Box(5)\n Console.WriteLine(CType(i, Decimal))\n Dim five As Object = Id(5D)\n Console.WriteLine(i = five)\n" +
        " Console.WriteLine(i = Id(5D))\n" +
        " Dim t As Object = New Thing()\n Try\n  Console.WriteLine(CType(t, Decimal))\n Catch ex As InvalidCastException\n  Console.WriteLine(\"invalid cast\")\n End Try\n" +
        " Dim a As Object = New Thing()\n Dim b As Object = a\n Console.WriteLine(a = b)\nEnd Sub\n")),
        Is.EqualTo("True\nFalse\n5\nTrue\nTrue\ninvalid cast\nTrue"),
        "row 5 is Object vs a TYPED Decimal (the helper, value comparison); the last row: two references to one object stay equal");

    /// <summary>
    /// ⛔ Scope guard (plan re-review): an Object compared with a NON-Decimal primitive is emitted exactly as before.
    /// MEASURED on the spec base `14c2e17d` (CLI `--target=csharp`, `Dim o As Object = Box(5)`): the ANALYZER ACCEPTS all
    /// three shapes and emits `o == 5`, `o == 5.0`, `o == Convert.ToInt64(5)` — so the refusal is csc's CS0019, and it
    /// must stay csc's (never a silently-false comparison: two boxed ints are never reference-equal). Uses the
    /// non-asserting Roslyn compile (FourBackends.TryCompileCSharp) — an Assert inside RunEmittedCSharpText is
    /// recorded by NUnit even when caught.
    /// </summary>
    [TestCase("5")]
    [TestCase("5.0")]
    [TestCase("CLng(5)")]
    public void ObjectVersusAnotherPrimitive_IsStillRefusedByCsc(string other)
    {
        var cs = ReturnCoercionTests.EmitCSharpForTest(Box +
            "Sub Main()\n Dim o As Object = Box(5)\n Console.WriteLine(o = " + other + ")\nEnd Sub\n");
        var diagnostics = FourBackends.TryCompileCSharp(cs);
        Assert.Multiple(() =>
        {
            Assert.That(cs, Does.Not.Contain("__BlObjectEquals"));
            Assert.That(diagnostics.Any(d => d.Id == "CS0019"), Is.True,
                "csc still refuses it, as on the base:\n" + string.Join("\n", diagnostics));
        });
    }

    /// <summary>Two Objects holding DIFFERENT boxes of the same Integer: unchanged from today (reference compare →
    /// False). A known divergence from VB (True), recorded as the follow-up "VB value equality for boxed numbers in
    /// general" — pinned here so the Decimal widening cannot drift into it unnoticed.</summary>
    [Test]
    public void TwoObjectsHoldingEqualBoxedIntegers_AreUnchanged() => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(
        "Function Box(n As Integer) As Object\n Return n\nEnd Function\n" +
        "Sub Main()\n Dim a As Object = Box(5)\n Dim b As Object = Box(5)\n Console.WriteLine(a = b)\nEnd Sub\n")),
        Is.EqualTo("False"));
}
```
  The fixture needs `using System.Linq;`.
  ⚠ If the analyzer refuses `Dim j As Integer = Id(3.5D)` (implicit narrowing), drop that row and record the refusal — it is then the same refusal on every target.
  **Finding to record (not fixed here — a follow-up beside "VB value equality for boxed numbers"):** the optimizer copy-propagates a constant into an Object and folds the comparison — `Dim o As Object = 5 : o = 5.0` emits `Console.WriteLine(false)`, where VB says True (measured on `14c2e17d`). A silent miscompile of a program csc would otherwise refuse.
- [ ] **Step 2: Run — RED:** E1 prints `2\n3\n-2\n3` (truncation); E4 prints wrapped/truncated values instead of `overflow`; E2/E3 prints `False\nTrue` then an uncaught `InvalidCastException` (test failure message from `RunEmittedCSharpText`). The guard rows `ObjectVersusAnotherPrimitive_…` (all three) and `TwoObjectsHoldingEqualBoxedIntegers_…` are GREEN on the base — re-confirm the measurement above on the current base before trusting them (if the analyzer now refuses `o = 5`, change the row to assert THAT refusal is unchanged and record it).
- [ ] **Step 3: Implement** (the four edits above).
- [ ] **Step 4: Run — GREEN**, plus the C# fixtures that exercise casts and Object comparisons by name: `CTypeConversionTests`, `NarrowIntegerWrapTests`, `IsIsNotOperatorExecutionTests`, `MsilObjectBoxingExecutionTests` (MSIL is out of scope — record if its expectations diverge from the new C# ones), `WinFormsTemplateBuildTests`.
- [ ] **Step 5: Mutations:** (1) drop `|| sourceName == "Decimal"` → E1 red; (2) `__BlObjectEquals` → `a == b` → E2 red; (3) drop the Object → Decimal arm → E3 red; (4) **route EVERY comparison with an Object operand through the helper** (the rejected scope) → `ObjectVersusAnotherPrimitive_IsStillRefusedByCsc` red on all three rows — `TryCompileCSharp` returns no CS0019 (it now builds) and the text contains `__BlObjectEquals`; (5) route one of the three `IRCompare` render sites around `CompareText` → the row that renders through that site red (find which test inlines vs binds a named temp; add one if a site is uncovered).
- [ ] **Step 6: Commit.** Message: "C# backend: Decimal narrowing rounds and overflows like VB; boxed Decimals compare by value (spec D19); CType of a boxed number to Decimal converts".

## Task 21: Comparisons, `Select Case`, boxing

D5, D6, D19.

**Files:**
- Modify: `BasicLang/JavaScriptBackend.cs` — comparisons are `IRCompare` here too, rendered by `RenderCompare` (`:1695` [X]; reached from `CompareExpr`/`CompareExprInline` `:1660-1661` and `Visit(IRCompare)` `:3448` [X] — NOT `RenderBinary`'s Eq/Ne arms): a Decimal comparison → `(l.cmp(r) === 0)` / `!== 0` / `< 0` / `<= 0` / `> 0` / `>= 0`; `Equal`/`NotEqual` → `VgsDecimal.objEquals(l, r)` under the SAME scope rule as the C# backend (Task 20A): both static types `Object`, or one `Object` and the other `Decimal` — else unchanged; `objEquals` compares by VALUE when either side is a `VgsDecimal` and the other a `VgsDecimal` or a JS number (a number is converted with `fromInt` if integral, else `fromNumber`), else `===`; `.Equals(x)` on an Object receiver → the same helper; `TypeOf o Is Decimal` → `(o instanceof VgsDecimal)`; `CType(o, Decimal)` (the `IRCast` Object → Decimal arm of `TryNumericCast`/`TryReferenceCast`, `JavaScriptBackend.cs:3328-3424` [X]) → `VgsDecimal.fromObject(o)`: a `VgsDecimal` as is, an integral number `fromInt`, another number `fromNumber`, a string `parse`, anything else `InvalidCastException` (VB's `Conversions.ToDecimal` shape)
- The C# side of E2/E3 was fixed in Task 20A (which runs BEFORE this task), so `Boxing_VbRules`' C# leg is green when this task starts and only its JavaScript legs are red.
- Modify: the test file (Edit)

- [ ] **Step 1: Failing tests** (append):

```csharp
    // ------------------------------------------------------------ D5, D6, D19

    [Test]
    public void TheComparisonTable()
    {
        var sb = new StringBuilder(Id).Append("Sub Main()\n Dim a As Decimal\n Dim b As Decimal\n");
        foreach (var (a, b) in Pairs.Append((1.10m, 1.1m)).Append((-0.0m, 0m)))
        {
            sb.Append($" a = Id({L(a)})\n b = Id({L(b)})\n");
            foreach (var op in new[] { "=", "<>", "<", "<=", ">", ">=" }) sb.Append($" Console.WriteLine(a {op} b)\n");
        }
        Both(sb.Append("End Sub\n").ToString());
    }

    [Test]
    public void ScaleDoesNotMatterToEquality() => Assert.That(Both(Id +
        "Sub Main()\n Console.WriteLine(Id(1.10D) = Id(1.1D))\nEnd Sub\n"), Is.EqualTo("True"));

    [Test]
    public void SelectCase() => Both(Id +
        "Sub Show(d As Decimal)\n Select Case d\n  Case 1.1D\n   Console.WriteLine(\"one point one\")\n" +
        "  Case 2D To 3D\n   Console.WriteLine(\"two to three\")\n  Case Is > 10D\n   Console.WriteLine(\"big\")\n" +
        "  Case Else\n   Console.WriteLine(\"else\")\n End Select\nEnd Sub\n" +
        "Sub Main()\n Show(Id(1.10D))\n Show(Id(2.50D))\n Show(Id(3.00D))\n Show(Id(10.01D))\n Show(Id(-1D))\nEnd Sub\n");

    /// <summary>Oracle rows: Equals, TypeOf, unboxing a boxed DECIMAL.</summary>
    [Test]
    public void Boxing() => Both(Id +
        "Sub Main()\n Dim o As Object = Id(1.10D)\n Dim p As Object = Id(1.1D)\n Console.WriteLine(o.Equals(p))\n" +
        " Console.WriteLine(TypeOf o Is Decimal)\n Dim back As Decimal = CType(o, Decimal)\n Console.WriteLine(back)\nEnd Sub\n");

    /// <summary>EXCEPTION ROWS E2/E3 (VB-literal, S10): VB compares boxed numbers by VALUE (spec D19, owner-approved)
    /// and converts a boxed Integer/Double to Decimal. The C# backend compares references and unboxes strictly
    /// until Task 20A; this test drives that fix on C# too.</summary>
    [Test]
    public void Boxing_VbRules() => BothEqual(Id +
        "Function Box(n As Integer) As Object\n Return n\nEnd Function\n" +
        "Public Class Thing\nEnd Class\n" +
        "Sub Main()\n Dim o As Object = Id(1.10D)\n Dim p As Object = Id(1.1D)\n Console.WriteLine(o = p)\n Console.WriteLine(o <> p)\n" +
        " Dim i As Object = Box(5)\n Console.WriteLine(CType(i, Decimal))\n Dim five As Object = Id(5D)\n Console.WriteLine(i = five)\n" +
        " Console.WriteLine(i = Id(5D))\n" +
        " Dim t As Object = New Thing()\n Try\n  Console.WriteLine(CType(t, Decimal))\n Catch ex As InvalidCastException\n  Console.WriteLine(\"invalid cast\")\n End Try\nEnd Sub\n",
        "True\nFalse\n5\nTrue\nTrue\ninvalid cast");
- [ ] **Step 2: Run — RED:** JS compares object identity (`1.10 = 1.1` prints `False`), `<` compares coerced strings, `Select Case` takes the wrong arm; record each.
- [ ] **Step 3: Implement** (files above).
- [ ] **Step 4: Run — GREEN**, plus `IsIsNotOperatorExecutionTests`, `TypeOfTests`, `SelectCase*` by name.
- [ ] **Step 5: Mutations:** (1) `Eq` → `===` for Decimal → `ScaleDoesNotMatterToEquality` red; (2) `objEquals` → `===` → `Boxing` (on `o.Equals(p)`) and `Boxing_VbRules` (on `o = p`) red; (3) `fromObject` rejects a number → `Boxing_VbRules` red on `CType(i, Decimal)`.
- [ ] **Step 6: Commit.**

## Task 22: Conversions, and the numeric-mixing grid

D7, D8, D9, D10.

**Files:**
- Modify: `BasicLang/JavaScriptBackend.cs` — the `IRCast` rendering (`Visit(IRCast)` `:3328`, `TryNumericCast` `:3424`, the inline twin `:1286` [X]): integral → Decimal `VgsDecimal.fromInt`, Single/Double → Decimal `VgsDecimal.fromNumber`, String → Decimal `VgsDecimal.parse`, Object → Decimal `VgsDecimal.fromObject` (Task 21), Decimal → Double/Single `.toNumber()`, Decimal → Integer `.toInt32()`, → Short `.toInt16()`, → Byte `.toByte()` (round half-to-even, then `OverflowException` out of range — VB, exception rows E1/E4; **never** the integral wrap), Decimal → String `.toString()`; `CallTarget`'s conversion switch (`:2028-2054`): `CDec` by argument type as above; `CInt`/`CShort`/`CByte`/`CDbl`/`CSng` of a Decimal argument as above
- Modify: the prelude's `varDecFromR8` — **the port** of `DecCalc.VarDecFromR8` from dotnet/runtime `src/libraries/System.Private.CoreLib/src/System/Decimal.DecCalc.cs` (MIT): read the function at the runtime version this repo targets (.NET 8), translate it statement for statement into the prelude (doubles stay JS numbers; the 64/96-bit integer steps become BigInt; its power-of-ten double table is `DOUBLE_POWERS_10`, extended exactly as the source's table is). The emitted prelude's notice comment and the `THIRD-PARTY-NOTICES.md` entry (both added in Task 19) already cover this port. `toNumber` is already the port of `VarR8FromDec` (Task 19).
- Modify: the test file (Edit)

- [ ] **Step 1: Failing tests** (append):

```csharp
    // ------------------------------------------------------------ D7, D8: conversions

    private const string IdD = "Function IdD(x As Double) As Double\n Return x\nEnd Function\n";

    private static string Overflow(string expr) =>
        $" Try\n  Console.WriteLine({expr})\n Catch ex As OverflowException\n  Console.WriteLine(\"overflow\")\n End Try\n";

    /// <summary>Oracle rows: CInt (C# already lowers it through Convert — banker's), CDec, widening.</summary>
    [Test]
    public void Conversions() => Both(Id + IdD +
        "Sub Main()\n Console.WriteLine(CInt(Id(2.5D)))\n Console.WriteLine(CInt(Id(3.5D)))\n Console.WriteLine(CInt(Id(-2.5D)))\n" +
        Overflow("CInt(Id(2147483648D))") +
        " Console.WriteLine(CDec(IdD(0.1)))\n Console.WriteLine(CDec(IdD(1.0 / 3.0)))\n Console.WriteLine(CDec(\"12.345\"))\n" +
        " Dim n As Integer = 5\n Dim d As Decimal = n\n Console.WriteLine(d)\n Dim s As Short = 7\n d = s\n Console.WriteLine(d)\n" +
        " Dim y As Byte = 9\n d = y\n Console.WriteLine(d)\nEnd Sub\n");

    /// <summary>EXCEPTION ROWS E1/E4 (VB-literal, S10) — CType/implicit narrowing and CShort/CByte of a Decimal round
    /// half-to-even and throw OverflowException out of range, on BOTH backends (C# fixed in Task 20A).</summary>
    [Test]
    public void Narrowing_VbRules() => BothEqual(Id +
        "Sub Main()\n Console.WriteLine(CType(Id(2.5D), Integer))\n Console.WriteLine(CType(Id(3.5D), Integer))\n" +
        " Dim j As Integer = Id(3.5D)\n Console.WriteLine(j)\n Console.WriteLine(CShort(Id(2.5D)))\n Console.WriteLine(CByte(Id(255.4D)))\n" +
        Overflow("CType(Id(2147483648D), Integer)") + Overflow("CShort(Id(40000D))") + Overflow("CByte(Id(256D))") + Overflow("CByte(Id(-1D))") +
        "End Sub\n",
        "2\n4\n4\n2\n255\noverflow\noverflow\noverflow\noverflow");

    /// <summary>
    /// Decimal → Double (VarR8FromDec) and Double → Decimal (VarDecFromR8), GENERATED: 400 decimals and 400 doubles
    /// from a fixed seed (random scale 0–28, random 96-bit mantissas, plus the edges: ±Max, 1e-28, values whose double
    /// is inexact, doubles near 1e15/1e16 digit boundaries, subnormal-adjacent tiny values that .NET turns into 0,
    /// ±7.9e28 overflow). Decimal → Double is asserted as `CDbl(Id(d)) = IdD(&lt;the C#-computed double, "R"&gt;)` printing
    /// True (no double FORMATTING in the comparison); Double → Decimal prints the Decimal (exact text).
    /// </summary>
    [Test]
    public void DoubleConversionTables_MatchDotNet()
    {
        var rng = new Random(20260929);
        var sb = new StringBuilder(Id + IdD).Append("Sub Main()\n");
        for (var i = 0; i < 400; i++)
        {
            var d = new decimal(rng.Next(), rng.Next(), rng.Next(), rng.Next(2) == 0, (byte)rng.Next(29));
            var expected = ((double)d).ToString("R", CultureInfo.InvariantCulture);
            if (!expected.Contains('E') && !expected.Contains('.')) expected += ".0";
            sb.Append($" Console.WriteLine(CDbl(Id({L(d)})) = IdD({expected}))\n");
        }
        foreach (var x in new[] { 0.1, 1.0 / 3.0, 123456789012345.6, 1e15 - 0.5, 9999999999999999.0, 1e-28, 1e-29, 7.9e28, -2.5e-10 })
            sb.Append(Overflow($"CDec(IdD({x.ToString("R", CultureInfo.InvariantCulture)}))"));
        for (var i = 0; i < 400; i++)
        {
            var x = (rng.NextDouble() - 0.5) * Math.Pow(10, rng.Next(-20, 28));
            sb.Append(Overflow($"CDec(IdD({x.ToString("R", CultureInfo.InvariantCulture)}))"));
        }
        var oracle = Both(sb.Append("End Sub\n").ToString());
        Assert.That(oracle.Split('\n').Take(400), Is.All.EqualTo("True"), "the C# oracle agrees with (double)decimal itself");
    }

    // ------------------------------------------------------------ D10: the mixing grid

    private static string Outcome(Func<string> run)
    {
        try { return "OK:" + FourBackends.Norm(run()); }
        catch (InvalidOperationException ex) { return "REFUSED:" + ex.Message.Split('\n').FirstOrDefault(l => l.StartsWith("Diagnostics:")); }
    }

    /// <summary>The C# leg of a grid cell, never asserting: analyzer refusal → REFUSED, csc refusal → CSFAIL (via
    /// FourBackends.TryCompileCSharp, Task 20A), else the run's output.</summary>
    private static string CSharpOutcome(string program)
    {
        string cs;
        try { cs = ReturnCoercionTests.EmitCSharpForTest(program); }
        catch (InvalidOperationException ex) { return "REFUSED:" + ex.Message.Split('\n').FirstOrDefault(l => l.StartsWith("Diagnostics:")); }
        var errors = FourBackends.TryCompileCSharp(cs);
        return errors.Count > 0 ? "CSFAIL:" + errors[0].Id : "OK:" + FourBackends.Norm(FourBackends.RunEmittedCSharpText(cs));
    }

    /// <summary>
    /// Every numeric type against Decimal, both orders, six operators: each cell either lowers and prints what .NET
    /// prints, or the ANALYZER refuses it on both targets with the same words. No cell may reach a JS TypeError
    /// (RunNodeScript asserts exit 0), and no cell may be accepted by the analyzer and rejected by csc (a CSFAIL is an
    /// analyzer defect: make the analyzer refuse it — the one rule for both targets — and record the cell).
    /// </summary>
    [Test]
    public void TheMixingGrid()
    {
        var findings = new StringBuilder();
        foreach (var t in new[] { "Byte", "Short", "Integer", "Single", "Double" })
        foreach (var op in new[] { "+", "-", "*", "/", "=", "<" })
        foreach (var reversed in new[] { false, true })
        {
            var expr = reversed ? $"y {op} x" : $"x {op} y";
            var program = Id + $"Sub Main()\n Dim x As {t} = 3\n Dim y As Decimal = Id(1.5D)\n Console.WriteLine({expr})\nEnd Sub\n";
            var cs = CSharpOutcome(program);
            var js = Outcome(() => JavaScriptExecutionTests.RunJs(program));
            if (cs.StartsWith("CSFAIL") || cs != js) findings.Append($"{t} {expr}: C#={cs} | JS={js}\n");
        }
        Assert.That(findings.ToString(), Is.Empty);
    }

    // ------------------------------------------------------------ D9: Long stays refused

    [Test]
    public void DecimalToLong_IsTheExistingLongRefusal() =>
        Assert.That(() => JsTestSupport.Compile("Sub Main()\n Dim d As Decimal = 1D\n Dim l As Long = CLng(d)\nEnd Sub"),
            Throws.Exception.With.Message.Contains("BL7003"));
```
  ⚠ An `AssertionException` caught inside a test still marks NUnit's result (the failure is recorded when the assertion fires), which is why the C# leg is `CSharpOutcome` over `FourBackends.TryCompileCSharp` (added in Task 20A) — nothing in the grid catches an assertion.
- [ ] **Step 2: Run — RED:** `Conversions` fails on the JS legs (no conversion arms — `TypeError` or wrong text); the grid lists every JS cell that differs. Record the grid's C# column as it stands (it is the oracle; CSFAIL cells are findings).
- [ ] **Step 3: Implement** (files above). A grid CSFAIL cell is fixed in the ANALYZER (`IsDecimalFloatingMix`, `SemanticAnalyzer.cs:2260-2264`; `GetCommonType` `SymbolTable.cs:905-912`) so both targets refuse it; list each such cell in the commit.
- [ ] **Step 4: Run — GREEN**, plus `CTypeConversionTests`, `NarrowIntegerWrapTests`, `NarrowIntegerDivisionTests` by name.
- [ ] **Step 5: Mutations:** (1) `CInt` of a Decimal truncates instead of banker's → `Conversions` red; (2) `toIntegral` wraps instead of throwing → `Narrowing_VbRules` red; (3) `toNumber` = `Number(this.toString())` (the rejected single-rounding spelling) → `DoubleConversionTables_MatchDotNet` red on at least one generated row (if none, add rows until one differs and record it — the two algorithms DO differ); (4) `varDecFromR8` replaced by `toPrecision(15)` parsing → the Double → Decimal half of the table red (same rule).
  ⚠ A literal like `IdD(1E-28)` must be a valid BasicLang double literal — if the lexer does not accept the `R` format's `E` exponent, write the literal through the lexer's accepted form (check `ScanNumber`) and record.
- [ ] **Step 6: Commit.**

## Task 23: `Math` on Decimal, and `Decimal.Parse`

D13, D14, D15.

**Files:**
- Modify: `BasicLang/JavaScriptBackend.cs` — before `TryStdLib` (`:2003-2012`; `JavaScriptStdLib`'s emitters take strings and cannot see types — `StdLib/JavaScriptStdLib.cs:45-123`): a `Math.X` call with a Decimal argument → `Round(d)` `d.roundTo(0,"even")`, `Round(d, n)` `d.roundTo(n,"even")`, `Abs` `.abs()`, `Min`/`Max` (`a.cmp(b) <= 0 ? a : b` — the oracle decides which operand .NET returns on a tie), `Floor` `.roundTo(0,"floor")`, `Ceiling` `.roundTo(0,"ceiling")`, `Truncate` `.roundTo(0,"trunc")`; any OTHER `Math` member with a Decimal argument, and `Round` with a third (`MidpointRounding`) argument → `BL7014 … DecimalNotSupportedOnWeb`; `Decimal.Parse(s)` → `VgsDecimal.parse(s)`
- Modify: the test file (Edit); create `VisualGameStudio.Tests/Compiler/DecimalRefusalTests.cs` (fast)

- [ ] **Step 1: Failing tests.** Append to `JavaScriptDecimalTests`:

```csharp
    // ------------------------------------------------------------ D13, D15

    [Test]
    public void MathOnDecimal() => Both(Id +
        "Sub Main()\n Console.WriteLine(Math.Round(Id(2.5D)))\n Console.WriteLine(Math.Round(Id(3.5D)))\n Console.WriteLine(Math.Round(Id(-2.5D)))\n" +
        " Console.WriteLine(Math.Round(Id(1.2345D), 2))\n Console.WriteLine(Math.Round(Id(1.235D), 2))\n Console.WriteLine(Math.Round(Id(1.5D), 3))\n" +
        " Console.WriteLine(Math.Abs(Id(-1.50D)))\n Console.WriteLine(Math.Min(Id(1.1D), Id(1.10D)))\n Console.WriteLine(Math.Max(Id(1.1D), Id(1.10D)))\n" +
        " Console.WriteLine(Math.Floor(Id(-1.5D)))\n Console.WriteLine(Math.Ceiling(Id(-1.5D)))\n Console.WriteLine(Math.Truncate(Id(-1.7D)))\n" +
        " Console.WriteLine(Math.Floor(Id(1.50D)))\nEnd Sub\n");

    [Test]
    public void Parse() => Both(
        "Sub Try1(s As String)\n Try\n  Console.WriteLine(Decimal.Parse(s))\n Catch ex As FormatException\n  Console.WriteLine(\"format\")\n" +
        " Catch ex As OverflowException\n  Console.WriteLine(\"overflow\")\n End Try\nEnd Sub\n" +
        "Sub Main()\n Try1(\"1,234.50\")\n Try1(\" -3.14 \")\n Try1(\"12.345\")\n Try1(\"1.\")\n Try1(\".5\")\n Try1(\"5-\")\n" +
        " Try1(\"abc\")\n Try1(\"1e5\")\n Try1(\"\")\n Try1(\"0.12345678901234567890123456789\")\n" +
        " Try1(\"79228162514264337593543950336\")\n Try1(\"-79228162514264337593543950336\")\nEnd Sub\n");
```
  `DecimalRefusalTests.cs`:

```csharp
using BasicLang.Compiler;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>Spec §4.11's "out" rows — refused BY NAME at compile time, never lowered wrong (DecimalNotSupportedOnWeb).</summary>
[TestFixture]
public class DecimalRefusalTests
{
    private static void Refused(string body, string construct) =>
        Assert.That(() => JsTestSupport.Compile("Sub Main()\n Dim d As Decimal = 1.5D\n" + body + "\nEnd Sub"),
            Throws.Exception.With.Message.Contains("DecimalNotSupportedOnWeb").And.Message.Contains(construct));

    [Test] public void RoundWithAMidpointRule() => Refused(" Console.WriteLine(Math.Round(d, 1, MidpointRounding.AwayFromZero))", "MidpointRounding");
    [Test] public void AnotherMathMember() => Refused(" Console.WriteLine(Math.Sign(d))", "Math.Sign");
}
```
- [ ] **Step 2: Run — RED:** `MathOnDecimal`/`Parse` fail on the JS legs (the stdlib emitters apply `Math.round` to an object → `NaN`); both refusal rows compile (no refusal yet).
- [ ] **Step 3: Implement** (files above).
- [ ] **Step 4: Run — GREEN.** Parse rows that disagree with .NET (group separators, a trailing sign, more than 28 digits): fix `parse`; record.
- [ ] **Step 5: Mutations:** (1) `Round` half-up → `MathOnDecimal` red; (2) `parse` rejects group separators → `Parse` red.
- [ ] **Step 6: Commit.**

## Task 24: Decimal in collections; `Optional` Decimal parameters

D17, D18, D22.

**Files:**
- Modify: `BasicLang/JsCapabilityChecker.cs` (`CheckTypeTree`, `:548-598`): a `Dictionary`/`HashSet` whose KEY/element type is `Decimal` → `BL7014 … DecimalNotSupportedOnWeb` ("a Decimal key compares by object identity on JavaScript; key by the Decimal's text")
- Modify: `BasicLang/JavaScriptBackend.cs`: `Contains`/`IndexOf`/`Remove` on a `List(Of Decimal)` and `Array.IndexOf` over a Decimal array → `BL7014` (generator-raised, as BL7008 is — the receiver's element type is known only there)
- Modify: the two test files (Edit)

- [ ] **Step 1: Failing tests.** Append to `JavaScriptDecimalTests`:

```csharp
    // ------------------------------------------------------------ D17, D22

    [Test]
    public void ListsArraysAndOptional() => Both(Id +
        "Function Fee(Optional rate As Decimal = 0.05D) As Decimal\n Return rate\nEnd Function\n" +
        "Sub Main()\n Dim list As New List(Of Decimal)\n list.Add(Id(1.10D))\n list.Add(Id(2.205D))\n Dim sum As Decimal = 0D\n" +
        " For Each x As Decimal In list\n  sum = sum + x\n Next\n Console.WriteLine(sum)\n Console.WriteLine(list(1))\n Console.WriteLine(list.Count)\n" +
        " Dim arr() As Decimal = {1.1D, 2.2D}\n Console.WriteLine(arr(0) + arr(1))\n Console.WriteLine(Fee())\n Console.WriteLine(Fee(Id(0.1D)))\nEnd Sub\n");
```
  Append to `DecimalRefusalTests`:

```csharp
    [Test] public void ADictionaryKeyedByDecimal() => Refused(" Dim m As New Dictionary(Of Decimal, String)\n m(d) = \"x\"", "Dictionary");
    [Test] public void AHashSetOfDecimal() => Refused(" Dim s As New HashSet(Of Decimal)\n s.Add(d)", "HashSet");
    [Test] public void ListContains() => Refused(" Dim l As New List(Of Decimal)\n Console.WriteLine(l.Contains(d))", "Contains");
    [Test] public void ListIndexOf() => Refused(" Dim l As New List(Of Decimal)\n Console.WriteLine(l.IndexOf(d))", "IndexOf");
    [Test] public void ListRemove() => Refused(" Dim l As New List(Of Decimal)\n l.Remove(d)", "Remove");
    [Test] public void ArrayIndexOf() => Refused(" Dim a() As Decimal = {1.5D}\n Console.WriteLine(Array.IndexOf(a, d))", "Array.IndexOf");
```
- [ ] **Step 2: Run — RED:** the refusal rows compile (a Decimal key is silently an object-identity key); `ListsArraysAndOptional` fails if any collection path still lowers Decimal as a number — record.
- [ ] **Step 3: Implement** (files above).
- [ ] **Step 4: Run — GREEN**, plus `JavaScriptCollection*`/`List*` fixtures by name.
- [ ] **Step 5: Mutation:** drop the `HashSet` arm → `AHashSetOfDecimal` red.
- [ ] **Step 6: Commit.**

## Task 25: The remaining "out" rows — `\`, format specifiers, `TryParse`

D4, D12, D16.

**Files:**
- Modify: `BasicLang/JavaScriptBackend.cs` — `IntDiv` with a Decimal operand (the IR converts a floating operand of `\` to Long, ADR-0005 D1, `JavaScriptBackend.cs:1451-1452` — catch the Decimal case BEFORE that Long reaches BL7003): `BL7014` with "use `Math.Truncate(a / b)`"; `ToString(<format>)` / `String.Format` or interpolation with a format specifier / `Format(d, …)` on a Decimal → `BL7014` with "format the text yourself, or use `Math.Round(d, 2).ToString()`"
- Modify: `BasicLang/JsCapabilityChecker.cs` — the BL7002 ByRef call message (`:670`): when the callee is `Decimal.TryParse`, append "Use Decimal.Parse inside Try … Catch ex As FormatException."
- Modify: `DecimalRefusalTests.cs` (Edit)

- [ ] **Step 1: Failing tests** (append):

```csharp
    [Test] public void IntegerDivision() => Refused(" Console.WriteLine(d \\ 1D)", "Math.Truncate");
    [Test] public void AFormatSpecifier_InToString() => Refused(" Console.WriteLine(d.ToString(\"N2\"))", "ToString");
    [Test] public void AFormatSpecifier_InStringFormat() => Refused(" Console.WriteLine(String.Format(\"{0:F2}\", d))", "String.Format");
    [Test] public void AFormatSpecifier_InInterpolation() => Refused(" Console.WriteLine($\"{d:F2}\")", "interpolation");
    [Test] public void VbFormat() => Refused(" Console.WriteLine(Format(d, \"0.00\"))", "Format");

    [Test]
    public void TryParse_IsTheByRefRefusal_WithTheSpellingThatWorks() =>
        Assert.That(() => JsTestSupport.Compile("Sub Main()\n Dim d As Decimal\n Dim ok As Boolean = Decimal.TryParse(\"1\", d)\nEnd Sub"),
            Throws.Exception.With.Message.Contains("BL7002").And.Message.Contains("Decimal.Parse inside Try"));
```
- [ ] **Step 2: Run — RED:** `IntegerDivision` reports BL7003 (the Long route) without the name; the format rows compile and print the default text; the TryParse row lacks the suggestion.
- [ ] **Step 3: Implement** (files above).
- [ ] **Step 4: Run — GREEN**, plus every fixture asserting `BL7002`/`BL7003` text by name.
- [ ] **Step 5: Mutation:** drop the interpolation arm → its row red.
- [ ] **Step 6: Commit.**

## Task 26: The optimizer never folds Decimal through Double

D21, scope call S9.

**Files:**
- Modify: the test file (Edit). No product change expected.

- [ ] **Step 1: Write the pinning tests** (append to `JavaScriptDecimalTests`):

```csharp
    // ------------------------------------------------------------ D21: optimizer folding

    private const string Foldable =
        "Const A As Decimal = 1.1D\nSub Main()\n Console.WriteLine(0.1D + 0.2D)\n Console.WriteLine(A * 3D)\n Console.WriteLine(1D / 3D)\n" +
        " Console.WriteLine(0.1D + 0.2D = 0.3D)\nEnd Sub\n";

    [Test]
    public void ConstantExpressions_AreExact_UnderEveryPipeline()
    {
        var cs = FourBackends.Norm(FourBackends.RunEmittedCSharp(Foldable));
        Assert.Multiple(() =>
        {
            Assert.That(cs, Is.EqualTo("0.3\n3.3\n0.3333333333333333333333333333\nTrue"));
            Assert.That(FourBackends.Norm(JavaScriptOptimizedExecutionTests.RunOptimized(Foldable)), Is.EqualTo(cs), "standard pipeline");
            Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(Foldable)), Is.EqualTo(cs), "aggressive pipeline");
            Assert.That(JsTestSupport.CompileAggressive(Foldable), Does.Not.Contain("0.30000000000000004"));
        });
    }
```
- [ ] **Step 2: Run — expect GREEN** (a guard: the optimizer does not fold Decimal today).
- [ ] **Step 3: Mutation (proves the guard):** add a `decimal` arm to `FoldAdd` (`IROptimizer.cs:1829`) that folds through `double` → red. Revert.
- [ ] **Step 4: Commit.**

## Task 27: Gate for 2.0b

- [ ] Clean build; fast subset by failure NAME vs `baseline-fast.txt`; Integration by name: `JavaScriptDecimalTests`, `DecimalRefusalTests`, `DecimalLiteralTests`, `CSharpDecimalVbRulesTests`, `MsilObjectBoxingExecutionTests` (record any MSIL expectation that now differs from C# — MSIL is out of scope), `JsExecutionTierRosterTests`, `JavaScriptCodeGenTests`, `CTypeConversionTests`, every fixture that grepped `BL7007`/`Decimal`; the mutation table; `docs/HANDOFF.md` and `docs/wiki/content/js-backend.md` (the BL70xx table gains BL7014; `Decimal` leaves the BL7007 list; `Char` leaves BL7004 — Task 14). **Record the follow-up** in `docs/HANDOFF.md`: "VB value equality for boxed numbers in general" — `o = 5` (Object vs a non-Decimal primitive) is still CS0019 on C#, and two Objects holding equal boxed Integers still compare by reference (`TwoObjectsHoldingEqualBoxedIntegers_AreUnchanged` pins today's False; VB says True). Commit. No IDE drop.

---

# Sub-pieces 2a, 2b, 2c, 2d — TASK granularity

**Each task below is expanded into full steps just before it starts, after a pre-flight** (`docs/superpowers/plans/2026-09-29-portable-controls-taskNN-preflight.md`, the piece-1 shape: an anchor table with verdicts, blockers, corrections, decisions taken). Pre-flights WIN over this text. Two reviews per task (spec, then quality) and mutation checks, as piece 1.

**Dependencies for 2a:** Tasks 1–17 merged; property-grid slice 3 merged (its rows, types `Font`/`Padding`/`Cursor`/`Reference`/`Double`, the `FormCss` list API, GroupBox Enter); `fix/unknown-dock-diagnostic` merged (`FormDock`, BL8033 — on master since `bc29391e`). **For 2b's gate:** slice 5 (event lists) and 2.0b merged.

## 2a — library core, codegen, first kinds (Tasks 28–36)

### Task 28: The library skeleton, the inclusion hook, the DOM surface
Spec §5.1, §5.2.
**Files and responsibilities:**
- `BasicLang/lib/js/forms/*.bas` (new): `Namespace System` (`EventArgs` + `Empty`), `Namespace System.Drawing` (`Color`, `Point`, `Size`, `Image`, `Font`, `FontStyle` — classes, copy-on-get/set, spec §5.7), `Namespace System.Windows.Forms` (`Control`, `Form`, `ControlCollection`, `ObjectCollection`, enums `Keys`, `MouseButtons`, `DockStyle`, `AnchorStyles`, `BorderStyle`, `ContentAlignment`, `PictureBoxSizeMode`, `DialogResult`, `MessageBoxButtons` (OK, OKCancel, YesNo)). Idioms of spec §5.1 (listeners declared ABOVE the attaching method, `AddressOf Me.VgsOnDom…`; `Me.`-qualified self-calls; `Optional` instead of overloads; `Vgs`-prefixed internals; constructors assign).
- `BasicLang/BasicLang.csproj`: copy every library file to the output (as `lib\js\dom-core.bli`, `:43`).
- `BasicLang/Compiler.cs`: `WithWebFormsLibrary(files)` beside `WithJavaScriptDeclarations` (`:550-559`), on the same route (`:354`): JavaScript target AND a TOKEN scan (comments and strings skipped — spec §11b note 3) finds `Using`/`Imports System.Windows.Forms`/`System.Drawing` or a qualified `System.Windows.Forms.`/`System.Drawing.` reference.
- `BasicLang/lib/js/dom-core.bli` and `IDE/lib/js/dom-core.bli` (both, one commit): the members spec §5.1 lists (rects/client metrics, `closest`/`contains`, positional style, event fields `button`/`buttons`/`detail`/`relatedTarget`/`code`/modifiers, `getComputedStyle`, `alert`/`confirm`, `CSSStyleSheet.insertRule`/`deleteRule`, `CSSMediaRule`).
- `BasicLang/lib/js/forms/vgs-page.bli` (new): `Extern Class VgsDockHook` (`request`, `reflow`) for spec §5.10's hook.
- Compiler: `PortableLibraryNameCollision` — a program class with a library class's simple name, when the library is included (JS drops namespaces; the merge keeps the first class of a name — `Compiler.cs:986-991`).
**Tests:** `WebFormsLibraryInclusionTests` (a JS project WITHOUT the trigger is byte-identical to `bc29391e`'s output — golden; with a `Using` → included; with a qualified-only reference → included; `' Using System.Windows.Forms` in a comment and the text in a string → NOT included; C# never includes it); `DomCoreLockStepTests` (the two `dom-core.bli` copies are byte-equal); the csproj copies every library file (as `BliDeclarationFileTests.cs:121`); `PortableLibraryNameCollision` (a user `Class Button` + `Using System.Windows.Forms` on JS → the error; on C# → nothing).
**Risks:** the IDE drop's `IDE/lib/js/forms/` is load-bearing and hand-refreshed; name collisions with existing user programs are exactly why the trigger is opt-in; the library itself runs through `JsCapabilityChecker` and every JS trap 2.0 fixed.
**Gate:** fast subset by name; `JavaScriptProjectBuildTests`, `BliDeclarationFileTests`, `JsExecutionTierRosterTests`, the new fixtures.

### Task 29: `Control`, `Form`, attaching, data flow, methods, events core
Spec §5.3–§5.6, O10–O12.
**Files and responsibilities:** `Control.bas`/`Form.bas`/`MessageBox.bas`/`EventArgs.bas` in `lib/js/forms/`. `Name` + element reference only; `Controls.Add` attaches by `getElementById(Name)` inside the parent's element; unattached access throws `InvalidOperationException`; `Element` property; the common members of §5.4 (Text, Enabled by the row's enable target with `data-vgs-enabled` propagation, Visible by the `hidden` attribute with ancestor semantics, ForeColor/BackColor/Font/Cursor/Padding through the §5.8 style rule and slice 3's converters); the methods of §5.5 (O12: Show/Hide/Focus/BringToFront, `MessageBox.Show` OK/OKCancel/YesNo, `Form.Close` hides the form area); `Protected Overridable Sub OnX(e)` + events of §5.6 (Click, TextChanged, MouseDown/Up/Move with `MouseEventArgs`, KeyDown/KeyUp with `KeyEventArgs` and the fixed `code`/`key` → `Keys` table, KeyPress with `KeyChar As Char` (Task 14), Enter via `focusin` + `relatedTarget` outside); change-only programmatic events.
**Methods owned by this task (O12, plan review item 8):** `Control.Show`/`Hide`/`Focus`/`BringToFront`; `MessageBox.Show(text, Optional caption, Optional buttons)` (OK → `alert`; OKCancel/YesNo → `confirm` → `DialogResult`); `Form.Close` (hides the form area); `ControlCollection.Add`/`Remove`/`Count`/`Item(i)`. (`TextBox.Clear`/`AppendText`/`SelectAll` and `Button.PerformClick` → Task 31; `ObjectCollection` `Items.*` → Task 37.)
**The method manifest (the catalog lists no methods):** `BasicLang/Forms/PortableMembers.cs` (new) — THE list of methods and collection members the library ships, per class, with each one's WinForms signature. Read by (a) a gate that the library declares exactly these public methods (a reflection-free check: parse the library's `.bas` and compare member names — no second copy), (b) `WebUnavailableMember` (Task 34: a method not in the manifest is the web-build error), (c) the method coverage gate (Task 35). A method added to the library without a manifest row, or a row without a library member, fails (a).
**Tests:** node tier (`PortableControlNodeTests`, roster +1): a recording stub DOM asserts every `DomMember` write, attach/unattached, `hidden`, enable propagation, change-only events, the caption double-click de-duplication, `relatedTarget` Enter, `MessageBox` button mapping (a stubbed `alert`/`confirm`), `BringToFront` moving the element last, `Form.Close` setting `hidden` on the form area, `Controls.Remove`/`Count`/`Item`, `<b>x</b>` never becomes markup (spec §5.1 security); `PortableMembersManifestTests` (the (a) gate). Edge tier comes with Task 35.
**Risks:** `Char` in `KeyPressEventArgs` needs Task 14; enum typing needs Task 9; `RemoveHandler` identity needs Task 12; a DOM listener declared below its attaching method is refused (M25) — follow the idiom.
**Gate:** node fixtures by name + `JavaScriptLibraryIdiomTests`.

### Task 30: The portable code-generation style, and the opt-in command
Spec §6, review C2/C3/I1/I5, O5.
**Files and responsibilities:**
- `BasicLang/Forms/RegionMarkers.cs`: the open marker accepts `style="portable"` in ONE position (`region=… form=… style=… hash=…`); `FormatOpen` writes it; an unknown value or the two regions disagreeing → `RegionMarkersMalformed` (BL8012). ⚠ A pre-2a IDE reads the new marker as BL8012 (strict order, `:60-62`) — release note.
  - ⛔ **Contract requested by piece 3** (2026-10-05; spec `docs/superpowers/specs/2026-10-05-target-switch-design.md` on `origin/feat/target-switch`): the marker reader must ACCEPT `style="dual"` on a **`.blform`** region (piece 3 writes it for wrapped desktop/web regions), and an UNKNOWN style value is BL8012 on BOTH `.blform` and `.blwebform` — with a test for each. This AMENDS spec §6.1's "WinForms files carry no style": a `.blform` region may carry `style="dual"` (and nothing else). Task 30 implements it.
- `BasicLang/Forms/FormCodeStyle.cs` (new): the ONE reader of a file's style from its markers.
- `BasicLang/Forms/RegionWriter.cs`: for a portable file, the init is the WinForms walk (`GenerateInit` `:600-688`, `AppendSiblings` `:721-757`, `AppendControlInit` `:769-791`) with geometry lines, property lines, FormRoot rows and `Me.<FormProperty>`/reference lines DELETED — each generated line TAGGED (geometry/property/other) so the subset relation is testable (the seam for piece 3); tray components keep their property lines (I5); `x.Name = "x"` right after `New` on BOTH targets (closes chip `task_fa51e644`); field types = WinForms types; wiring = `AddHandler`. BL8013 (`CheckHandlerOrdering` `:478-517`) skips portable files.
- `BasicLang/Forms/FormHandlers.cs` — **anchor on property-grid slice 3's rework, which lands first** (not master's `:157-162`): slice 3 names a handler after the WinForms event on BOTH targets (`e90a24bb` — `FormHandlers.DefaultEventDef(definition, target)` `:60-61` [S3] via `FormControlDef.DefaultEventDefOn(target)` `FormControlCatalog.cs:1280` [S3] and `FormEvents.NameOn` `:1272` [S3]), plans every crossed bind (`995ca04c` — `PlanDefault` `:100`, `PlanBind` `:140` [S3]), and writes the stub signature in one place (`:219-222` [S3]: `()` for a parameterless web component, `(e As DomEvent)` on the web, `(sender As Object, e As {winFormsArgs ?? "EventArgs"})` on WinForms). This task makes that ONE signature site choose by STYLE (portable → the WinForms form on the web too) and the placement by style (after the init region). ⚠ The slice-3 rename is what makes spec §11.2's byte-identical NON-REGION text achievable — the same handler name on both targets — so the twin (Task 35) depends on it.
- Slice 3's default-event changes the portable library must honour (read slice 3's catalog at the pre-flight, never this list): container kinds (Panel, FlowLayoutPanel, TableLayoutPanel) → `Paint`; TrackBar → `Scroll` (web `input`) with `ValueChanged` → `change`; DataGridView → `CellContentClick`; GroupBox → `Enter` (`focusin`, Click kept non-default and exempt); Form `Opacity` in percent. A default event with no web meaning (e.g. `Paint`, if slice 3 marks it WinForms-only) stubs as WinForms-only on a portable web form — the pre-flight decides from slice 3's rows.
- `BasicLang/Forms/FormScaffolder.cs` (`:195-254`): a portable web scaffold (three `Using` lines, `Inherits Form`, init region after `New()`), the style passed EXPLICITLY into the empty regions before `RegionWriter.Write` (`:172-185`).
- `VisualGameStudio.Shell`: the bound command **"Add Web Form (portable)"** (`[RelayCommand]` adjacent to its method — CLAUDE.md's attribute trap) in the same menu as Add New Form (`AddNewFormAsync`).
**Tests:** portable region golden files per kind; the web init = WinForms init minus the tagged lines, per `FormCatalogShapes.Canonical` fixture, components excepted; `Name` line on both targets + the WinForms csc sweep re-run (`WinFormsCatalogSweepTests`); BL8012 for a malformed style; a new form with NO controls is portable (the marker, not content); DOM-style files byte-identical (golden); `FormScaffolderTests`; **the entry-point test**: drive the GENERATED command (`AddWebFormPortableCommand`) through the real view model and read the AXAML for its binding (a `[RelayCommand]` no menu binds is unreachable — CLAUDE.md).
**Risks:** every existing WinForms region rehashes once (the `Name` line) — list every changed expectation; the WinForms order (G4) must be reproduced exactly, not approximated.
**Gate:** `Form*` fast fixtures, `WinFormsCatalogSweepTests`, `FormDesignerAcceptanceTests`, `FormDesignerCommandTests`, the entry-point test.

### Task 31: The first kinds — Button, Label, TextBox, CheckBox, RadioButton, Panel, GroupBox
Spec §5.4 table rows for these kinds.
**Files and responsibilities:** one `.bas` per kind in `lib/js/forms/`; TextBox `Multiline` element swap (keeping id/classes/attributes/listeners), `PasswordChar` (`Char`) → `type=password` + `data-vgs-passwordchar`, `MaxLength` default 32767; RadioButton raises `CheckedChanged` on the one that became unchecked; GroupBox `Text` → legend; CheckBox/RadioButton caption span. **Methods owned here (O12):** `TextBox.Clear()` (raises TextChanged once when the text was non-empty), `AppendText(s)` (TextChanged once), `SelectAll()` (`element.select()`); `Button.PerformClick()` (raises Click with `sender` = the button; WinForms' `CanSelect` rule — no-op when the button or an ancestor is hidden or disabled, CLAUDE.md's PerformClick note). Manifest rows for each (Task 29).
**Tests:** node tier per kind (every catalog property's `DomMember`, every listed event once, `sender` = the control, each owned method's effect and event count); the first slice of the coverage gate (§11.1) for these kinds on node.
**Risks:** WinForms event ORDER (CheckedChanged before Click) is asserted by the twin in Task 35 — the node tier only counts.
**Gate:** node fixtures + roster.

### Task 32: Rendering fixes on portable pages
Spec §7, O7, F5.
**Files and responsibilities:** `BasicLang/Forms/FormControlCatalog.cs` (row fields: caption wrapper, Multiline tag, `BorderStyle` CSS, client insets, enable target, `DomMember`); `FormAssetEmitter.cs` (`AppendControl` `:274-472`): the caption `<label>` with the id, `<textarea>`/`type=password`, the legend + frame pseudo-element on a zero-border fieldset, Panel borders, radios' default `name` = container id, design-time disabled containers, `hidden` for design-time `Visible=False`, `.vgs-form [hidden] { display: none !important }` on EVERY portable page (Grid/Flow too), `data-vgs-*` (anchor, dock via `FormDock`, requested bounds, container design client size); `FormClientArea` (new) — origin inset + dock padding — consumed by `FormDockLayout`, the canvas, the page, the reflow script's core, the library, and the WinForms region writer's resolved docked `Size`. DOM-style pages byte-identical (golden hash, piece-1 C8 style).
**Tests:** `FormAssetEmitterTests` (portable arms; DOM-style golden); `FormDockLayoutTests` + `FormDockScriptTests` lock-step gain bordered Panel and GroupBox fixtures (the legend is not a docked sibling — §11b note 1); `FormCanvasRenderTests`/`FormCanvasTransformTests` expected moves listed INTENTIONALLY (1–19px on `.blform` too); Edge assertions come in Task 35.
**Risks:** `FormClientArea` is a new "one rule" with six consumers — a second copy anywhere is the `Tracks`/`ParseTracks` scar; the canvas moves on WinForms documents.
**Gate:** `Form*`, `FormDock*`, `PixelLayout*` fast + Integration by name.

### Task 33: Run-time geometry, Anchor, the style-rule writer, live docking
Spec §5.8–§5.10, O15, reviews I7/R1/R2, §11b notes 1–2, 4.
**Files and responsibilities:** the library's style-rule writer (one `<style>`, `#id` rules inside the reflow script's `@media (width >= B)` block, unwrapped at breakpoint 0); geometry reads/writes (`FormAnchorCss.Positioned`'s axis rule, current client size at desktop width; below the breakpoint: stored requested bounds and the container's design client size — the recorded divergence of §11b note 4); `Anchor` read/write via `data-vgs-anchor`; the R2 rule (never a geometry property for a docked control); `FormDockScript` for portable pages: always emitted, the live walk over CONTROL elements only (§11b note 1), requested size `data-vgs-w`/`-h`, complete live rules (`auto` for unused edges), `window.vgsDock = { request, reflow }` with one coalescing scheduler shared with the MutationObserver; the MIRRORED PAIRS change in ONE commit — `FormDockLayout` ↔ the script core AND `FormAnchorCss.Docked` ↔ `vgsDockCss` (§11b note 2) — plus the library's anchor axis ↔ `FormAnchorCss.Axis`.
**Tests:** `FormDockScriptTests` lock-step: live cases (undocked at design time → docked at run time FIRST; docked → None; requested size changed; container resized with docked children; a docked sibling hidden/shown; BringToFront of a docked control; bordered Panel; GroupBox with a docked child and its legend); `PortableAnchorLockStepTests` (node vs `FormAnchorCss.Positioned`, 16 edge combinations × near/far/centred × negative sums × sizes ≤ 0); one reflow per `Visible` write (a counter on the hook); the R2 cascade in both `<style>` creation orders; a page with nothing docked at design time still carries the script and the hook (CLI-built page, glue harness).
**Risks:** the script becomes live and global; the performance assertion (100 `Left` writes in one Timer tick → ≤ 1 reflow, exact final position, a budget measured and fixed in the pre-flight) runs in Edge (Task 35).
**Gate:** `FormDockScriptTests`, `FormAnchor*`, `PixelLayout*`, `FormBuildEmissionTests`, the new fixtures.

### Task 34: The build errors and editor awareness
Spec §5.11, O3, O11.
**Files and responsibilities:** `WebUnavailableMember` (the analyzer's missing-member error reworded for a library receiver — a METHOD is available exactly when `PortableMembers` (Task 29) lists it, a property/event exactly when the catalog does — and BL7007 for an undeclared `System.Windows.Forms`/`System.Drawing` type — on the path `40e9c172` rewrote; a `MessageBoxButtons` member beyond the three); `RuntimeControlCreation` (`New` of a library control outside the designer's init region); `ElementOnDesktop` (non-JS build, `.Element` on a receiver whose declared type NAME is a library class — works although WinForms members type as Object, K14); `MixedCodeStyle` (designer + web build: DOM-style markers + `Using System.Windows.Forms`/`Inherits Form` in a web code-behind — a token scan, §11b note 3); the LSP includes the portable library for web projects under the Task 28 trigger (Task 6's `LspProjectContext` hook).
**Tests:** one per error, on the CLI and the IDE route; LSP completion lists a library member on a web control and flags `.Element` on a desktop project.
**Gate:** the new fixtures + the LSP suite by name.

### Task 35: The coverage gate and the behaviour twin
Spec §11.1–§11.4, O9, review I9.
**Files and responsibilities:** `PortableCatalogCoverageTests` (catalog-driven program per web kind: every property applying to BOTH targets set to the sweep sample (`WinFormsCatalogSweepTests.cs:601-610`), read back, every listed event triggered once; compiled for desktop via the CLI + csc and for the web; RUN in the WinForms reference window (Task 12 harness, piece 1) and in Edge (Task 13 harness, piece 1) — **Edge is the authority**; web-only rows under `#If WEB`; WinForms-only rows asserted as `WebUnavailableMember`); `BehaviourTwinTests` (a pixel `.blform` and its Canvas `.blwebform` retarget twin; NON-REGION code-behind text byte-identical, asserted first; one neutral scenario — set Text, toggle Visible/Enabled/Checked, click, type, select, move a Right-anchored control, change a Dock, resize a container with docked children, resize the window; compare values, rectangles ±1px, and the event LOG (sender, event, key args) in order and count). Real input: window messages (`WM_LBUTTONDOWN`/`UP`, `WM_KEYDOWN`/`WM_CHAR`/`WM_KEYUP`) and Edge DevTools `Input.*` over `--remote-debugging-port` (fresh `--user-data-dir`; kill by our own PID tree). Rendering assertions of §11.4 in Edge; the §5.8 performance budget.
**The method gate (plan review item 8):** `PortableMethodCoverageTests` — driven by `PortableMembers` (Task 29), the list the catalog cannot be for methods: for every manifest row, a generated shared program calls the member with a sample argument (the manifest carries one per parameter) and prints its observable effect (the property it changes, the event count it raises, the value it returns); compiled for desktop (CLI + csc) and web; run in the WinForms window and in Edge; the two outputs must be equal. A manifest row whose effect differs between the targets is a defect, not an exemption — except `MessageBox.Show` (a browser dialog vs a WinForms dialog: the gate asserts the RETURN mapping with the dialog answered by the harness — CDP `Page.handleJavaScriptDialog` on Edge, a `WM_COMMAND` to the dialog's button on WinForms — and records the missing title as the spec's divergence).
**Risks:** the harness itself — mutation checks on it (never shown, input before layout, the wrong window); a synthetic `dispatchEvent` step is named where CDP cannot express it.
**Gate:** Integration, Windows-gated, SKIP (never pass) where Edge or the Desktop SDK is absent.

### Task 35A: The automated end-to-end test (spec §11.8)
Plan review item 7 — the owner's click-through is not the only proof.
**Files and responsibilities:** `VisualGameStudio.Tests/Shell/PortableFormEndToEndTests.cs` (Integration; `[AvaloniaTest]`, Skia via `DesignerHeadlessApp`, the REAL `CodeEditorDocumentView` with `AppStyles.axaml` — the rig of `FormPropertyGridRealViewTests.cs`): (1) invoke the GENERATED "Add Web Form (portable)" command through the real `MainWindowViewModel` (not a constructed scaffold) into a temp web project; (2) drop a Label, a TextBox and a Button through the real canvas (pointer events, zoom ≠ 1, a second window size); (3) double-click the Button → assert the stub `Private Sub btnX_Click(sender As Object, e As EventArgs)` lands AFTER the init region; (4) write the handler body through the editor (`lbl.Text = txt.Text & "!"`) and save via `SaveAsync`; (5) build the WEB target through `BuildService` AND the CLI, and run the page in Edge (Task 35's harness): type into the TextBox, click the Button, read the Label; (6) retarget to WinForms (`FormRetarget.ConvertToPair`), build through the CLI + csc, run in the WinForms reference window with the same input: the Label reads the same. Every step drives a shipping entry point (CLAUDE.md "a thing with no caller").
**Risks:** headless traps (double-click timing, centred ListBox-style layouts, bound properties changed in `Render`); keep every fixture control's id stable.
**Gate:** Integration, Windows-gated SKIPs; run twice (flakiness check) before the 2a gate.

### Task 36: 2a mutation pass, gate, and the owner's click-through
- Mutation table over Tasks 28–35A's load-bearing rules (spec §11.9 list), including the manifest gate (a library method with no `PortableMembers` row → red) and the E2E test (skip the Button's handler wiring in the region writer → red).
- Fast subset by NAME vs the Task 0 baseline (+ the names 2.0 added); named Integration fixtures.
- IDE drop (`robocopy VisualGameStudio.Shell\bin\Release\net8.0 IDE /E`, never `/MIR`; `IDE\lib\js\dom-core.bli` and `IDE\lib\js\forms\` included) — the owner needs to click the opt-in command.
- **Drop-vs-source (spec §12):** `IdeDropLibraryMatchesSourceTests` (Integration, Windows) — every file under `BasicLang/lib/js/forms/` and `BasicLang/lib/js/dom-core.bli` is byte-equal to its `IDE/lib/js/…` copy. It is expected to fail BETWEEN drops, so it runs as a named gate step AFTER the drop (and at 2b's drop), never in the fast subset; a failure there means the drop step missed a file.
- **Owner click-through:** (1) Add Web Form (portable) → the code-behind has three `Using` lines, `Inherits Form`, init after `New()`; (2) drop Button/Label/TextBox/CheckBox/RadioButton/Panel/GroupBox; double-click the Button → `(sender As Object, e As EventArgs)` below the region; (3) handler: `lblMsg.Text = txtUser.Text`, `chkRemember.Checked = True`, `btnLogin.Left = btnLogin.Left + 20`; (4) build web, open in Edge: captions, legend, borders visible; the click works; a right-anchored control moved at run time stays anchored on resize; (5) retarget to WinForms, build, run: same behaviour.

## 2b — the remaining kinds, then the default flips (Tasks 37–42)

### Task 37: ComboBox, ListBox, PictureBox, LinkLabel
§5.4 rows (ComboBox `Text` = the selected option's text — a CHANGE from today's `title=`, FAE:338-342; `SelectedIndexChanged` change-only; PictureBox `SizeMode` → `object-fit`; LinkLabel `LinkClicked` with navigation prevented). **Methods owned here (O12):** `ObjectCollection` — `Items.Add`/`Insert`/`Remove`/`RemoveAt`/`Clear`/`Count`/`Item(i)`/`IndexOf`/`Contains` (options created with `createElement`/`textContent`, never `innerHTML`; removing the selected item raises `SelectedIndexChanged` as WinForms does — the twin measures the count). Manifest rows for each. Tests: node tier per kind and per `Items` member; coverage-gate rows; method-gate rows; Edge rows.

### Task 38: NumericUpDown (Decimal), DateTimePicker, TrackBar, ProgressBar
Needs 2.0b. NumericUpDown `Minimum`/`Maximum`/`Value`/`Increment` are `Decimal` (O14) read by exact parse of the element's text and written by exact invariant format; the catalog's Int modelling (FCC:1497-1511) is reconciled with slice 3/5's type work in the pre-flight. Recorded divergence: `input` per keystroke vs WinForms' commit.

### Task 39: Timer, the strips and the items
Timer replaces the web template for portable forms (`System.Windows.Forms.Timer`, constructed in init with its properties — the component exception; `Enabled` becomes a web row; runs only while enabled). MenuStrip/ToolStrip/StatusStrip + four item kinds attach by id; the host's item verb in DOCUMENT order (G4, `RegionWriter.cs:736-750`); `ItemClicked` with `ClickedItem`.

### Task 40: Slice 3 and slice 5 rows join the library
Every web-applicable row slice 3 added (Font, Cursor, Padding on content controls, the FormRoot rows incl. AcceptButton/CancelButton if web rows — Enter/Escape inside the form area; `Opacity` as slice 3 stores it, in percent) and every event slice 5 listed gets a library member; the new default events of slice 3 (Paint on containers, TrackBar `Scroll`→`input` with `ValueChanged`→`change`, DataGridView `CellContentClick` — desktop-only kind, GroupBox `Enter`→`focusin`) are implemented as slice 3's catalog maps them; the coverage gate picks them up from the catalog (no library-side list for properties/events — methods live in `PortableMembers`, Task 29). ⚠ Slice 3's pre-flight B1 (retarget dropped non-default binds) was fixed by `995ca04c` (`PlanBind` for every crossed bind) — the twin's retargeted pair relies on it.

### Task 41: The default flips
The ordinary Add New Form route scaffolds portable web forms; the opt-in command is REMOVED (its menu item and `[RelayCommand]`; the entry-point test becomes a test of the ordinary route); `FormScaffolderTests` expectations move INTENTIONALLY.

### Task 42: 2b gate, IDE drop, click-through
As Task 36, with the full coverage gate over every web kind and the twin over a form using every kind.

## 2c — conversion (Tasks 43–45)

### Task 43: The conversion function and the CLI verb
Spec §8.2–§8.3. `FormCodeConversion.Convert(document, text)` (pure): Usings + `Inherits Form` (a different `Inherits` refuses, nothing written); both regions regenerated portable with the marker; SIMPLE handlers' parameter lists rewritten (keeping the user's name; `WinFormsEventArgs ?? EventArgs`); bodies never edited; a wired DOM-style Timer gets `Enabled="true"` (BL8027 rule); `DomUsageFinding` (warning, name in the message; number from `DesignDiagnostic.cs` that day) per DOM use outside the regions, suggestions from `DomMember`. CLI `design --convert <form>`. Tests: convert, each finding kind, non-simple handlers left and reported, CLI bytes = function bytes; `MixedCodeStyle` for a hand-half-converted file (§8.4).

### Task 44: The IDE offer, and declining
Spec §8.1, §8.4, O2 ("offer on open, write on Convert"). `EnterDesignModeForFormDocument` (`CodeEditorDocumentViewModel.cs:163-173`, called from `MainWindowViewModel.OpenFileAsync` `:2526`) → a notification with Convert / Not now (`ShowNotification(…, actions)`, `MainWindowViewModel.cs:2140-2146`); OPEN only, once per open, never on reload; refused documents not offered; Convert writes once (IDE bytes = CLI bytes); Not now writes nothing (byte-identical) and the designer keeps generating DOM style; findings recomputed on each regeneration and published through `DesignerDiagnosticsEvent`. Tests: the real `OpenFileAsync` route (not a constructed VM), headless view for the notification, decline byte-identity, findings in the Error List.

### Task 45: 2c gate and click-through (open an old web form, convert, see findings, build).

## 2d — desktop-only controls (Tasks 46–47)

### Task 46: Badges, drops, the build error
Spec §9, O8, P-D6. Toolbox (`FormToolboxViewModel.cs:64` lists every kind on a web form; desktop-only kinds badged); drop allowed (the BL8019 refusal for a WinForms-but-not-web kind removed, `FormPlacement.cs:48-51`); property-grid header badge read through `Selection` (never `PropertyGrid.SelectedControl` from the VM — CLAUDE.md); canvas draws the catalog schematic; `DesktopOnlyKind` (error, name in the message; number from `DesignDiagnostic.cs` that day) where web forms load for a build (`FormDocumentLoader`, both routes — `BuildService.cs:631`, `Program.cs:815`). Tests: headless toolbox/grid badges, a drop on a web form, the build error on CLI and IDE, the HTML comment (FAE:277-284) no longer reached by a successful build.

### Task 47: 2d gate and click-through.

---

## Traps (the repo-specific ones that apply here)

- ⛔⛔ **`JsTestSupport` does not set `CurrentUnit`**: a defect that lives on the project route (M7's `IsKnownNetStaticType` heuristic) passes through it with the defect present. Anything about `Using`, sibling files or module resolution compiles through `BasicCompiler.CompileProjectFiles`.
- ⛔ **The JS roster moves on every merge.** Read `RosterIsPinned` (`JsExecutionTierRosterTests.cs:470-471`, 94 on `bc29391e`) before adding a fixture; any Integration fixture in `VisualGameStudio.Tests.Compiler` named `JavaScript*`/`Js*`/`*ExecutionTests` must be in `ExecutionTier`.
- ⛔ **The C# leg of `FourBackends` runs in-process and redirects `Console.Out`** → `[NonParallelizable]`. A `StackOverflowException` there kills the test host (Task 13's red).
- ⛔ **Roslyn folds constant expressions**: an overflowing Decimal CONSTANT is CS0463 at compile time. Pass operands through `Id(…)` (2.0b).
- ⛔⛔ **The C# backend is an oracle only where it equals VB** (S10). Exception rows E1–E4 are VB-literal expectations on BOTH backends, and the C# backend is FIXED (Task 20A) — never copied, and the spec's D19 is never "corrected" to match a backend.
- ⛔ **`-0.0m` is not `< 0`**: read a Decimal's sign from `decimal.GetBits(d)[3]`, never from a comparison (the `L` helper).
- ⛔ **Preprocessor order is load-bearing**: `#IfDef`/`#IfNDef` before `#If`; `#ElseIf` before `#Else`; `#End If` and `#EndIf` both close. An `#Include`'s blocks are its own (Task 2).
- ⛔ **One place defines build symbols** (`BuildSymbols`, the `BasicCompiler` constructor). Never `pre.Define("WEB")` in a route.
- ⛔ **The analyzer's shape check is deliberately silent for .NET events** (K14): WinForms `AddHandler` must stay silent (Task 10's guard test).
- ⛔ **`MyBase` + a FIELD is `this.field`** (not virtual); only a PROPERTY goes `super.`/`base.` (Task 13). Every pass that clones an `IRFieldAccess`/`IRFieldStore` copies `ThroughBase`.
- ⛔ **ADR-0016**: `IRBaseConstructorCall` is untouched by Task 13.
- ⛔ **A DOM listener declared BELOW its attaching method is refused (M25)**; the library declares them above.
- ⛔ **Mirrored pairs change in one commit with their lock-step test**: `FormDockLayout` ↔ the reflow script core; `FormAnchorCss.Docked` ↔ `vgsDockCss`; `FormAnchorCss.Axis` ↔ the library's anchor rule; `BasicLang/lib/js/dom-core.bli` ↔ `IDE/lib/js/dom-core.bli`; `FormAssetEmitter.Tracks` ↔ `FormGridLayout.ParseTracks` (piece 1, untouched here).
- ⛔ **The catalog is the single source of truth**: no hand `[TestCase]` kind lists, no `control.Kind` switch in the emitter/canvas/library generator; the coverage gate reads the catalog.
- ⛔ **A green build is not a running page**: every library behaviour is RUN (node tier; Edge is the authority).
- ⛔ **A thing with no caller**: every new entry point is tested through its SHIPPING route (the hook through a real build, the command through its generated `[RelayCommand]` + AXAML binding, the conversion offer through `OpenFileAsync`, the CLI verb through the CLI).
- ⛔ **The ONE selection store**: badges and grid reads go through `Selection`.
- ⛔ **Headless IDE tests**: `[AvaloniaTest]`, Skia, the real view, `AppStyles.axaml`, close every Window, offset repeat clicks, more than one window size / zoom ≠ 1.
- ⛔ **`dotnet clean` after AXAML changes** (the opt-in command, badges).
- ⛔ **Windows-only prerequisites SKIP with a reason** (Edge, WinForms, csc, MSVC); inside `Assert.Multiple` use `TestSkip.IgnoreEvenInsideMultiple`.
- ⛔ **Kill spawned processes by our own PID tree**, never by name (the owner's Edge is running; a fresh `--user-data-dir`).
- ⛔ **Capture both streams**; "Passed!" is not the verdict; a passing test prints nothing; compare failure NAMES, not counts; state the base with every number.
- ⛔ **Mutation reverts use Edit + rebuild**, never `git checkout --`/`Copy-Item`.
- ⛔ **A subagent that adds tests uses Edit on an existing test file**; Write only for files this plan creates.
- ⛔ **Stage by name; never `git add -A`; never `csc.dll`; commit via `-F`**; never round-trip a repo file through `Get-Content`/`Set-Content`; build special characters from code points, never a typed backslash-u escape.
- ⛔ **`git merge-tree` is not a conflict check** — trial merges in `git worktree add --detach`.
- ⚠ **Never name a probe class `F`** (chip `task_ef845b99`).
- ⚠ **PowerShell 5.1**: no `&&`; `2>&1` on a native exe wraps stderr — use `cmd /c`. The Bash tool is banned.
- ⚠ **The security hook flags the text `.exec(`** in any file written through Edit/Write (a false positive on JavaScript regex calls) — use `String.prototype.match`.

## Tests to re-check when later tasks land (absence tests that may be vacuous today)

| Test | Why it may be vacuous / change | Re-check in |
|---|---|---|
| `JavaScriptInteropTests.JsImport_InsideInactiveConditional_IsNotCollected` (comment `:55-58`) | Its "#If is a parser construct" claim is false after Task 1 | Task 1 (comment fixed) |
| `LexerTests.Lexer_PreprocessorIf` | Pins a token the parser never consumes (S1) | Task 5 (kept deliberately) |
| `JavaScriptEventTests` (`:189`, `:192`, summary `:26-29`) | Pins `new Set()`/`.add(` and "RemoveHandler removes nothing" | Task 12 (changed) |
| `NetBuildPipelineTests.InstanceCallOnAPascalCaseLocal_UnderANetUsing_…` | The M7 guard must not reopen the C1 shape | Task 11 |
| `CrossFileBindingTests.AClassInheritsAClassFromAnotherFile_UnderAUsing` | Task 7 calls the inherited Sub UNQUALIFIED because `Me.Hello()` under a Using is M7 (JS "no lowering for 'Me.Hello'") — switch it back to `DerivedFile` verbatim (`Me.Hello()`) | Task 11 |
| `HandlerSignatureTests.AnUnresolvedDotNetEvent_StaysSilent` / `TheWinFormsShape_…` | Guards, green from the start | Task 10 (mutations prove the others) |
| `JavaScriptLibraryIdiomTests` (most rows) | Guards of today's behaviour | Task 16 (mutation) |
| `ConstantExpressions_AreExact_UnderEveryPipeline` | Guards "not folded" — becomes a real test if anyone adds Decimal folding | Task 26 |
| `BuildSymbolGoldenTests` | Guard; mutation proves it | Task 4 |
| Every test asserting `BL7004` / `BL7007` for Char/Decimal | Become lowering tests | Tasks 14, 19 |
| `FormScaffolderTests`, `FormRegionWriterTests` WinForms expectations | The `Name` line rehashes regions | Task 30 |
| `FormCanvasRenderTests` / `FormCanvasTransformTests` bordered/GroupBox fixtures | Client insets move children 1–19px | Task 32 |
| `FormAssetEmitterTests` ComboBox `title=` | Becomes the selected option's text on portable pages | Task 37 |
| `FormPlacement` BL8019 for desktop-only kinds on a web form | Removed | Task 46 |

## File map (every file created or modified)

| File | Task | Responsibility |
|---|---|---|
| `BasicLang/Preprocessor.cs` | 1, 2, 6 | `#If` family; hygiene; editor mode |
| `BasicLang/PreprocessorCondition.cs` | 1 (new) | the condition evaluator |
| `BasicLang/BuildSymbols.cs` | 3 (new) | the one symbol answer |
| `BasicLang/Compiler.cs` | 3, 15, 28 | options → symbols; `ConfigureTarget`/namespaces; `WithWebFormsLibrary` |
| `BasicLang/Program.cs` | 3 | CLI routes pass configuration/defines |
| `VisualGameStudio.ProjectSystem/Services/BuildService.cs` | 3 | IDE route passes configuration/defines |
| `BasicLang/ProjectSystem/CppProjectBuilder.cs` | 3 | native route |
| `BasicLang/Debugger/DebugSession.cs` | 3 | debugger route |
| `BasicLang/ASTNodes.cs`, `ASTPrettyPrinter.cs` | 5 | dormant node removed |
| `BasicLang/LSP/LspProjectContext.cs`, `DocumentManager.cs` | 6, 34 | backend, blanking, DOM + library declarations |
| `BasicLang/SemanticAnalyzer.cs` | 5, 7, 9, 10, 15, 18, 22 | base lookup; enum members; events as members + deferred check; target + namespaces; CDec; mixing rule |
| `BasicLang/CSharpBackend.cs` | 8, 13, 18, 20A | container naming; `base.`; `CDec`; VB Decimal narrowing, boxed-Decimal equality, boxed-number → Decimal |
| `BasicLang/Forms/PortableMembers.cs` | 29 (new) | THE list of library methods/collection members (read by the manifest gate, `WebUnavailableMember`, the method gate) |
| `THIRD-PARTY-NOTICES.md` | 19 | one dotnet/runtime (MIT) entry covering BOTH ports in the emitted Decimal prelude — `VarR8FromDec` (Task 19) and `VarDecFromR8` (Task 22) — and the `DOUBLE_POWERS_10` table; the emitted prelude carries a short notice comment because it ships inside every user's App.js |
| `BasicLang/IRBuilder.cs` | 5, 11, 13 | dormant visitor removed; value receivers; `ThroughBase` |
| `BasicLang/IRNodes.cs` | 13 | `ThroughBase` |
| `BasicLang/JavaScriptBackend.cs` | 12, 13, 14, 19–25 | delegate identity; `super.`; Char; Decimal runtime + lowering + refusals |
| `BasicLang/CppCodeGenerator.cs` | 13 | base-qualified property access |
| `BasicLang/JsCapabilityChecker.cs` | 14, 19, 24, 25 | Char allowed; Decimal allowed; BL7014; TryParse message |
| `BasicLang/JsExceptionTypes.cs` | 19 | exceptions Decimal needs |
| `BasicLang/BasicLangLexer.cs`, `Parser.cs` | 15, 18 | dotted Namespace; Decimal literal |
| `BasicLang/lib/js/dom-core.bli`, `IDE/lib/js/dom-core.bli` | 28 | the library's DOM surface (lock-step) |
| `BasicLang/lib/js/forms/*.bas`, `vgs-page.bli` | 28–31, 37–40 | the library |
| `BasicLang/BasicLang.csproj` | 28 | copy the library |
| `BasicLang/Forms/RegionMarkers.cs`, `FormCodeStyle.cs`, `RegionWriter.cs`, `FormHandlers.cs`, `FormScaffolder.cs` | 30 | the portable style |
| `BasicLang/Forms/FormControlCatalog.cs`, `FormAssetEmitter.cs`, `FormClientArea.cs`, `FormDockLayout.cs`, `FormDockScript.cs`, `FormAnchorCss.cs` | 32, 33 | rendering, client area, live docking |
| `BasicLang/Forms/FormCodeConversion.cs`, `DesignDiagnostic.cs` | 43, 46 | conversion; `DomUsageFinding`, `MixedCodeStyle`, `DesktopOnlyKind` |
| `VisualGameStudio.Shell/…` (menu AXAML + VM command; toolbox/grid badges; conversion notification) | 30, 44, 46 | entry points |
| `docs/wiki/content/language.md`, `js-backend.md`, `wiki-content.js`, `CLAUDE.md`, `docs/HANDOFF.md` | 4, 17, 27, gates | records |
| Tests: `PreprocessorConditionalTests`, `BuildSymbolTests`, `BuildSymbolRouteTests`, `BuildSymbolGoldenTests`, `LspConditionalCompilationTests`, `CrossFileBindingTests` (Edit), `CsFileNamedContainerTests`, `EnumMemberTypingTests`, `HandlerSignatureTests`, `HandlerSignatureRouteTests`, `CSharpDecimalVbRulesTests`, (2a) `PortableMembersManifestTests`, `PortableMethodCoverageTests`, `PortableFormEndToEndTests`, `IdeDropLibraryMatchesSourceTests`, `JavaScriptMeUnderUsingTests`, `JavaScriptDelegateIdentityTests`, `MyBasePropertyExecutionTests`, `JavaScriptCharTests`, `JavaScriptProgramNamespaceTests`, `JavaScriptLibraryIdiomTests`, `DecimalLiteralTests`, `JavaScriptDecimalTests`, `DecimalRefusalTests`, `JsExecutionTierRosterTests` (Edit), `JavaScriptEventTests` (Edit), `JsTestSupport` (Edit) | 1–26 | |

---

## Appendix: the probe programs of spec §3 (for Task 0's re-measurement)

Run single files with `BasicLang.exe p.bas --target=javascript` then `node p.js` (listener probes: prepend the stub DOM of `JavaScriptLibraryIdiomTests.StubDom` to `p.js` first); projects with a `Site.blproj` (`<TargetBackend>JavaScript</TargetBackend>`, or `CSharp` for `e3`) and `BasicLang.exe build Site.blproj`.

- **a (M4)** — `Class Control` with `Public Overridable Property Text` over `_text`; `Class Button : Inherits Control` with `Public Overrides Property Text` whose getter returns `"B:" & MyBase.Text` and whose setter does `MyBase.Text = value`; a form that sets and prints it. Expect: `RangeError`.
- **d (M1, M2)** — one file: `EventArgs`, `Control` (`Event Click(sender As Object, e As EventArgs)`, `Property Text` calling `Protected Overridable Sub OnTextChanged`, `Overridable Function Describe`, `PerformClick` raising Click), `Button` (overrides calling `MyBase.`), `Form`, `Form1 : Inherits Form` (two `AddHandler`, `PerformClick`, `RemoveHandler`, `PerformClick`, an upcast). Expect today: `clicked 1..4` (M2).
- **h (M5)** — `Form1 : Inherits Form` declared above `Form`. Expect on `bc29391e`: runs.
- **v1/v2/v3 (M7)** — `Using System` + (with/without `Inherits B0`) + `Public Sub New() : Me.Init() : End Sub` + `Private Sub Init()`; v3 without `Using`. Expect: v1/v2 "no lowering for 'Me.Init'", v3 `init`.
- **e1/e2/e3 (M6)** — `a.bas`: `Class Base` with `Overridable Function Hi`; `b.bas`: e1 uses `New Base()`, e2/e3 `Class D : Inherits Base` overriding `Hi` with `MyBase.Hi()` and `Dim x As Base = New D()`. Expect: e1 runs, e2 (JS) / e3 (C#) "Unknown base class 'Base'".
- **b/b2/c (M8)** — `Namespace System.Windows.Forms` (b, dotted), nested namespaces (b2), a two-file project with `Using System.Windows.Forms` (c).
- **i (M9)** — `#If WEB Then … #Else … #End If` and `#IfDef WEB … #Else … #EndIf` in `Sub Main`.
- **g/f2 (M12)** — `Enum Shade … End Enum` + `Dim k As Shade = Shade.Dark` (g in a Module).
- **w1 (M13)** — probe d with `Sub btn_Click(n As Integer)`.
- **j1a/j1c/j1d/j5 (M22–M25)** — the listener, later-declared listener, lambda and unqualified-self-call shapes of `JavaScriptLibraryIdiomTests`.
- **j2 (M21)** — `Optional` parameters.
- **j3 (M20)** — `Dim d As Decimal = 0.1` (and `0.1D`, `CDec("0.1")`).
- **j4 (M18)** — `Dim c As Char = "a"c`.

