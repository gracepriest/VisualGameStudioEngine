title: Known gaps and open work
lede: What is unfinished, what is deliberately out of scope, and where the authoritative status lives.
---
> [note] **This page summarises; it does not replace `docs/HANDOFF.md`.** That file is a
> dated snapshot maintained alongside the work, and it is the thing to read on a fresh
> checkout. Re-verify anything here before relying on it.

## Where status actually lives

| Question | Source |
|---|---|
| What is the current state? | `docs/HANDOFF.md` — dated snapshot: gates, traps, open work |
| Why was this built this way? | `docs/superpowers/specs/` |
| How was it implemented? | `docs/superpowers/plans/` |
| What changed? | `git log` |
| How do I not break it? | `CLAUDE.md` and [Conventions](#/conventions) |

## Open workstreams

### P2a-2 — .NET classes in native projects <span class="pill ok">shipped</span>

The largest open thread: letting a native (C++-backend) project reach .NET types through
a generated shim. <span class="pill ok">Complete</span> **Done and merged (`77e415b`), closed out 2026-09-14.**
Tasks 1–15 all shipped — the last piece, Task 15 Step 4 (the `IDE/` binary refresh), landed in
`fbb3694`. Kept here only as a pointer to the design record.

> [trap] The plan file is full of unticked `- [ ]` boxes. **They are not status.** At the 2026-09-11
> closeout there were 59 unchecked boxes against 10 checked ones while Tasks 1–14 had all shipped —
> Task 9's heading has read `✅ DONE` for weeks with every one of its step boxes unchecked. The
> **task headings** are the status of record. Do not read the boxes as remaining work, and do not
> tick them.

- Plan: `docs/superpowers/plans/2026-08-02-p2a2-dotnet-native-flip.md`
- Spec: `docs/superpowers/specs/2026-07-29-p2a-dotnet-access-aot-shim-design.md`
- Boundary contract: `docs/superpowers/specs/2026-07-26-dotnet-native-boundary-contract-design.md`

See [.NET interop](#/net-interop) for the machinery.

### VS Code extension host

Exactly **21 unimplemented requests**, enumerated and enforced by
`ExtensionHostRequestCoverageTests.KnownUnimplemented` — a second test fails once an entry
is implemented, so the list can only shrink.

- A missing `sendNotification` handler is a **silent no-op**.
- A missing `sendRequest` handler **rejects inside `activate()`** and kills the extension.
- **Webviews still render as source text.**

Recovery ledger: `docs/superpowers/specs/2026-08-05-extensions-recovery-ledger.md`.

### JavaScript backend

- The `lib.dom.d.ts` → `.bli` generator was **never built**; `dom-core.bli` is hand-curated
  and deliberately small.
- The New Project wizard's JavaScript path **has not been clicked through by a human**.
  View-model and template-service tests cover it; nothing in the suite can drive the
  Avalonia window.
- **A class with two constructors cannot be lowered to JavaScript at all** — `SyntaxError: A class
  may only have one constructor`. Measured with a pair that has no `Optional` anywhere, which is why
  constructor-overload shapes are asserted on MSIL instead.
- **`CStr(Boolean)` prints `true`** where C# and MSIL print `True`. Measured on a plain local, no
  module scope involved; pinned as each backend actually behaves rather than normalised away.

### C++ backend gaps

Parts of `List`, `Console` and `String` are still missing on the native backend, though a
native project can now reach real .NET types through the shim-and-proxy route and the
generated `blnet` C++ facade — see [.NET interop](#/net-interop). The twelve deferred C++
backend defects (arrays, `ByRef`, `Mod`, `Is Nothing`, class emission order and the rest) are
catalogued in
native backend. Catalogue:
`docs/superpowers/specs/2026-07-07-cpp-backend-preexisting-gaps.md`.

Known behavioural limitation: **a `Return` inside a `Try` bypasses its `Finally`.**

### MSIL backend

<span class="pill ok">maintained since 2026-09-15</span> `BasicLang/MSILBackend.cs` went from **2136 to
5356 lines** (+3492 / −272) over this stretch, backed by a round-trip harness —
`VisualGameStudio.Tests/Msil/MsilHarness.cs`: source → `.il` → `ilasm` → a real process → stdout.
The `Msil/` fixture is **4279 lines across six files, 127 `[Test]` methods plus 18 `[TestCase]`
rows**.

> [trap] **Never assert on emitted IL text alone here.** The defect that motivated the harness was a
> `Select Case` that assembled, ran, and answered `Case Else` for every input — a text assertion
> would have had to already know `beq` was missing to catch it.

Landed: `Select Case`, `Try`/`Catch`/`Finally` as real EH regions, properties, instance methods and
`Me`, `Shared` members on user classes, module-level variables, field initializers, array allocation,
`MyBase.New` arguments, ILAsm identifier quoting, the dotted static surface as direct IL, and
`List`/`Dictionary` on a narrow recorded surface.

Still open — each **refused** rather than miscompiled, except where noted:

- Multi-dimensional arrays (`AMultiDimensionalArray_IsRefusedNotSilentlyFlattened`).
- `Select Case` type, tuple and binding patterns (`SelectCase_TypePattern_IsRefusedNotDropped`).
- A lossy widening argument — `int64` → `float64` (`ALongArgument_IsRefusedRatherThanLossilyWidened`).
- A computed `MyBase.New` argument — an IR-level gap, not an MSIL one; C# does not build it either (CS0103).
- **Not refused:** any `ByRef` call still fails with `InvalidProgramException`; an `Iterator Function`
  in a Module or at file scope fails to assemble; a cross-file call mangles the qualified name into
  the method name and spells the signature from the arguments.

> [note] There are **no `_PinnedDivergence` tests left in the MSIL fixture** — the last one
> (`ASharedMethodOnAUserClass_IsAPhantomCall_PinnedDivergence`) went red when `Shared` members
> started working. That is not a claim the backend is complete; the gaps above are real, and the ones
> living in the FRONT END cannot be pinned in that fixture at all.

> [trap] `ilasm` is located, not required. Windows ships one in-box under
> `%WINDIR%\Microsoft.NET\Framework64`; elsewhere restore `runtime.<rid>.Microsoft.NETCore.ILAsm` or
> set `BASICLANG_ILASM`. A machine with none gets `Assert.Ignore`.

## Front-end gaps affecting every backend

- `Inherits ArgumentException` — inheriting from a BCL exception type.
- Assigning an inherited field from a derived class.
- Module-level initializers that need code to run — `Helper()`, `New List(Of Integer)()` — and any
  expression that names a `Const` inside a larger expression (`= K + 1`).
  <span class="pill warn">narrowed 2026-09-18</span> Constant arithmetic now **folds** instead of
  crashing the compiler: `= 2 + 3 * 4` runs 14, a bare `= K` naming a module `Const` runs, and
  `= 7 / 2` reduces. What cannot fold is refused with a diagnostic naming the variable.
- C++ `raise_X()` taking no parameters.
- `For Each … In items.Select(…)` inside a class method fails on C#.

## Baseline test failures

Four failures are pre-existing and **not yours** — but that is the **Windows** count, last measured
at `ee3c086` (2026-09-14), **58 non-merge commits ago**:

1. `SearchSnippets_*` (two of them — these also show in the fast subset)
2. `Cli_Build_CppProject_ProjectReference_Warns…`
3. `NonEx_variants…` — passes alone, fails only when the Native tier runs alongside it

See [Building and testing](#/build-test) for how to distinguish these from real failures
and from contention artifacts.

> [trap] **On Linux the number is not four.** The most recent measured Linux run — at `d2ad064` —
> is **195 failed / 6069 passed / 203 skipped of 6467**, with **170 distinct failing names**, the
> same set as the baseline before it. Every one is environmental: `BasicLang.exe` not deployed (no
> `.exe` suffix off Windows), hardcoded `C:\` / PATHEXT / MSVC-vcvars assertions, 22 blnet
> integration rows needing a win-x64 ILC/AOT shim publish, and native-engine `DllNotFound`. Compare
> a Linux run against that measured set, never against zero — and note that anything touching the
> .NET shim needs a Windows pass, because a Linux run structurally cannot exercise those 22 rows.

## Out of scope — settled decisions

| Decision | Meaning |
|---|---|
| **LLVM backend** | Out of scope. Do not test, fix, or file bugs on it. It builds; that is the whole promise. |
| ~~**MSIL backend**~~ | <span class="pill warn">no longer out of scope</span> **Maintained since 2026-09-15.** The old "MSIL/LLVM are not maintained" policy now covers LLVM only — see *MSIL backend* under Open workstreams above. |
| **COM interop** | Ruled out. Do not add it. |
| **The legacy VB.NET IDE** (`VisualGameStudio`) | Removed. Do not resurrect from old docs. |
| **`VS.BasicLang` VSIX** | Removed. The current VS extension is `BasicLang.VisualStudio`. |

## Recently landed

**123 commits have landed since this wiki was written.** The largest of them:

| What | Where |
|---|---|
| **The MSIL backend overhaul** — `Select Case`, real EH regions, properties, instance methods and `Me`, `Shared` members, module-level variables, field initializers, array allocation, the .NET Console surface, collections | `BasicLang/MSILBackend.cs` (2136 → 5356 lines), `VisualGameStudio.Tests/Msil/` |
| **P2a-2 closed out** — .NET classes in native projects, done and merged | `77e415b`, `fbb3694` |
| **The blnet C++ facade** — `blnet_facade.g.hpp`, so hand-written C++ can say `System::Console::WriteLine("hi")` instead of naming a mangled slot whose trailing hash moves with the signature. Name and signature collisions are omitted rather than guessed at, and reported as **BL6027** — always a warning, never a build failure | `BasicLang/Compiler/CodeGen/Net/NetProxyEmitter.Facade.cs`, plan `docs/superpowers/plans/2026-09-13-blnet-cpp-facade.md` |
| **Front-end sweeps across all four backends** — numeric coercion at the return, store and argument sites; half-to-even rounding on every narrowing conversion; omitted `Optional` arguments filled at the call site; a Module's variables and constants resolved from outside it; `Shared` semantics fixed on JavaScript and C++; a no-modifier procedure defaulting to `Public` with its access enforced everywhere | `fa66d78`, `6e8d455`, `fa47d52`, `3776df2`, `be0fa8d`, PRs `#55`–`#58` |
| **Two optimizer passes withdrawn** — `FunctionInliningPass` is **disabled** (six of seven call shapes miscompiled, five separate defects) and three `AlgebraicSimplification` arms were **deleted** as unsound | `cd08f84`, `9b872cf` |

> [trap] `FunctionInliningPass` still exists as a class in `IROptimizer.cs` but is **not registered**
> — the `AddPass(new FunctionInliningPass())` line is commented out. Do not re-enable it without
> reading the commit message on `cd08f84`; the same undeclared-local defect that sank it also sank
> the deleted `AlgebraicSimplification` arms.

Earlier, a JavaScript project type in the IDE. The compiler could already emit a web site, the
build service could build one, and `F5` could preview one — but the New Project wizard had
no way to *create* one. See [JavaScript backend](#/js-backend) for the pieces, and
`docs/superpowers/plans/2026-08-06-javascript-backend-interop-and-dom.md` (Plan 2d) for the
full account, including the three defects found reviewing it.
