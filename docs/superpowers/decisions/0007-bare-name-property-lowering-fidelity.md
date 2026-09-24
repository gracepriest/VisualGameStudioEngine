# ADR 0007: bare-name property lowering fidelity

- **Date:** 2026-09-24
- **Status:** Accepted
- **Decided by:** the architect role, dispatched on its pinned model with no
  per-invocation override.
- **Brief:** [`0007-brief.md`](0007-brief.md) — one question on user code
  behind a classified IR kind: a property used by its bare name. It was
  written by the brief role and corrected by the orchestrator, with the
  corrections listed at its foot.

## Questions

One question:

- **Q1 (→ D1):** How does the optimizer learn that a bare-name property
  access runs user code?

## D1: How does the optimizer learn that a bare-name property access runs user code?

### Decision

Option A. IRBuilder lowers a bare name that the analyzer binds to an
accessor-backed member of the enclosing class (property today; operator/
conversion when reachable) to exactly the node its qualified form already
produces — `IRFieldStore(Me, P)` / `IRFieldAccess(Me, Tick)` — and never to
`IRAssignment` / `IRVariable`. The kill vocabulary is unchanged. A new
verifier invariant F ("lowering fidelity") makes this class of defect
structurally detectable.

### Because

- The vocabulary was not wrong; the IR was. `IRAssignment %P = 10` is a
  false statement about the program. Fixing the fact at its single source
  (IRBuilder, where the analyzer's binding already exists) corrects every
  consumer at once — CSE, LICM, CopyPropagation (#146), the verifier, any
  future pass. B corrects one consumer and leaves the lie for the others to
  rediscover; today each backend already re-resolves the bare name
  privately, which is exactly the per-consumer duplication CLAUDE.md
  forbids.
- A adds no mechanism: the node kinds, their classification (ADR-0006 D1
  step 6c) and the backends' emission of them all exist. B adds a metadata
  dependency to the optimizer and a second reader of that metadata in the
  verifier — the drift ADR-0006 D1 rejected.
- C is sound but pays an unmeasured price on every plain-field read for a
  defect that is not about plain fields, and the IR would still lie.

### Contract

- P4 prints `12,3`, P5 prints `13,3,11`; P1/P3 unchanged — C#, JavaScript,
  MSIL, all three entry points (CLI, CLI `--optimize`, Release `.blproj`).
  C++: "does not build" may not become anything worse; it is fixed by the
  separate property-codegen defect and re-measured then.
- P6 (new probe): a Shared property used by bare name inside a Shared
  method, accessor writing a Shared field, same seed/merge shape.
  ASSUMPTION: the qualified form `ClassName.P` already has a
  call-classified node and P6 lowers to it. If no such node exists, P6 is
  recorded wrong→wrong (not a regression), tracked, and becomes a NEED for
  a follow-up node ruling — it may not be silently left as `IRAssignment`.

  NOTE (clarifying, not a ruling change): whether `ClassName.P` (a Shared
  property) has a call-classified node today is the ruling's ASSUMPTION,
  not a measured fact. The implementer measures it first.
- No-optimizer IR dump of P4/P5 shows no `IRVariable` spelled `P` or
  `Tick`.
- Plain fields are out of scope: a bare `K` lowers as today. A is scoped by
  "accessor-backed", not by "undeclared".
- Invariant F: for each function f, no `IRVariable` (operand or
  destination) spells a member with accessors declared on f's enclosing
  class or on a base class present in the IR module. Hand-built IR with
  `IRAssignment %P = 10` where P is a property fails F. F is quiet on the
  whole suite once the lowering lands.
- Corpus pins as they stand may move only downward, each lost merge
  attributed to a bare-property use the merge spanned (a correct kill),
  recorded in the implementation note as ADR-0006 did for SpaceShooter. Any
  upward move is a lost kill and blocks.
- `OverKillWitness` survives.

### Obligations

1. Before landing: one IR dump to count bare-property-use sites in the
   sample games and test corpus; run the fast subset plus P1–P6 on four
   backends × three entry points at HEAD for the before-table.
2. If (1) shows a C++ cell green today that uses a bare property, the C++
   property-codegen fix lands FIRST; otherwise it stays a separate brief.
3. ONE commit: lowering + F + probes P4/P5/P6. Not two — F alone would fire
   on existing tests (green→red cells), and the lowering alone leaves the
   defect class undetectable.
4. LLVM (untested here) gets no new node kind; whatever it does for `Me.P`
   covers the bare form.
5. Task #146 (CopyPropagation) observes the same IR; it needs no
   property-specific kill rule.

### Rejected

- **B (optimizer consults declarations):** fixes one consumer; IR stays
  false; second metadata reader in the verifier is the drift D1 rejected;
  every future pass re-learns it.
- **C (any undeclared-name access is a call):** over-kills plain fields,
  unmeasured, IR still lies, backends' private re-resolution remains.
- **A-wide (plain fields through `IRFieldAccess` too):** `IRFieldAccess` is
  a call under D1 6c, so this is C by another route.
- **May-alias model now:** nothing measured needs it; premature until the
  amended Revisit fires.
- **Two commits (F first):** F fires green→red on existing tests.

### Revisit if

The amended Revisit clause fires (see the first settled point, below); or
a bare-name form the analyzer binds to user code has no call-classified
node to lower to (P6's ASSUMPTION fails) — then a node-addition ruling
with five-backend obligations.

### Amends

ADR-0006 D1 (Revisit clause replaced; invariant F added beside V). ADR-0006
D3 unchanged — `IsCallVisible` already reports these names visible; this
ruling supplies the missing call.

NOTE (clarifying, not a ruling change): ADR-0006 will carry an "Amended by
ADR-0007" callout under its title, the way ADR-0005 carries "Amended by
ADR-0006" — added by the orchestrator after the concurrent ADR-0006 edit
lands. Not added here.

## The three settled points

1. ADR-0006 D1's Revisit clause is amended, not discharged. The trigger
   fired, but the diagnosis was lowering fidelity, not vocabulary.
   Replacement clause: "V and F are quiet, a probe on a classified kind
   prints a stale value, AND the no-optimizer IR dump shows the construct
   lowered to a kind faithful to its semantics (user code appears as a
   call-classified node). Then the vocabulary is wrong and needs a real
   may-alias model." F takes the fidelity arm, V the completeness arm.
2. Operators and conversions: this ruling binds. Principle: user code is a
   call in the IR, never inferred from a name or a type by a pass. The
   commit that makes operator/conversion resolution reachable must (a)
   lower a user-defined operator or conversion to `IRCall` (or a node V
   classifies as a call), never to `IRBinaryOp` or a cast node; (b) ship
   OP1 reshaped as a kill probe (operator body writes a field read across
   the use) with the correct value required on all four backends, three
   entry points; (c) extend F: no non-call node over an operand whose
   static type declares a user operator/conversion for that operation.
3. Churn: acceptable — it is the fix. C# text changes from `P = 10` to the
   qualified form; JavaScript and MSIL already emit `this.P` / `callvirt
   set_P` for bare names, so their text should be identical. Golden-text
   expectations may be updated where executed output is unchanged; any
   executed-output or build change other than P4/P5/P6 going wrong→right
   blocks the commit. Measured before landing: fast subset green
   cell-for-cell on Linux, a Windows run for MSIL and `.blproj`, corpus
   pins with attribution per lost merge.

   NOTE (clarifying, not a ruling change): the ruling asks for "a Windows
   run for MSIL and `.blproj`". This work is done in a Linux cloud
   container, which has ilasm and runs MSIL, so every MSIL cell here is
   compiled AND run on Linux. A Windows run is not available to this
   session. `docs/HANDOFF.md`'s standing rule already says a green Linux
   run is necessary but not sufficient for MSIL-touching changes, so the
   Windows run is recorded as OWED (the owner's to run), not skipped.
