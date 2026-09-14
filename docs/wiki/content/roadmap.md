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

### P2a-2 — .NET classes in native projects

The largest open thread: letting a native (C++-backend) project reach .NET types through
a generated shim. Task 14 is most of the way done; Task 15 is the closeout.

- Plan: `docs/superpowers/plans/2026-08-02-p2a2-dotnet-native-flip.md`
- Spec: `docs/superpowers/specs/2026-07-29-p2a-dotnet-access-aot-shim-design.md`
- Boundary contract: `docs/superpowers/specs/2026-07-26-dotnet-native-boundary-contract-design.md`

See [.NET interop](#/net-interop) for the machinery.

### VS Code extension host

Roughly **24 unimplemented requests**, enumerated and enforced by
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

### C++ backend gaps

Broad .NET API surface — parts of `List`, `Console` and `String` — is missing on the
native backend. Catalogue:
`docs/superpowers/specs/2026-07-07-cpp-backend-preexisting-gaps.md`.

Known behavioural limitation: **a `Return` inside a `Try` bypasses its `Finally`.**

## Front-end gaps affecting every backend

- `Inherits ArgumentException` — inheriting from a BCL exception type.
- Assigning an inherited field from a derived class.
- Module-level non-constant initializers.
- C++ `raise_X()` taking no parameters.
- `For Each … In items.Select(…)` inside a class method fails on C#.

## Baseline test failures

Four failures are pre-existing and **not yours**:

1. `SearchSnippets_*` (two of them — these also show in the fast subset)
2. `Cli_Build_CppProject_ProjectReference_Warns…`
3. `NonEx_variants…` — passes alone, fails only when the Native tier runs alongside it

See [Building and testing](#/build-test) for how to distinguish these from real failures
and from contention artifacts.

## Out of scope — settled decisions

| Decision | Meaning |
|---|---|
| **MSIL and LLVM backends** | Do not test, fix, or file bugs on them. They build; that is the whole promise. |
| **COM interop** | Ruled out. Do not add it. |
| **The legacy VB.NET IDE** (`VisualGameStudio`) | Removed. Do not resurrect from old docs. |
| **`VS.BasicLang` VSIX** | Removed. The current VS extension is `BasicLang.VisualStudio`. |

## Recently landed

A JavaScript project type in the IDE. The compiler could already emit a web site, the
build service could build one, and `F5` could preview one — but the New Project wizard had
no way to *create* one. See [JavaScript backend](#/js-backend) for the pieces, and
`docs/superpowers/plans/2026-08-06-javascript-backend-interop-and-dom.md` (Plan 2d) for the
full account, including the three defects found reviewing it.
