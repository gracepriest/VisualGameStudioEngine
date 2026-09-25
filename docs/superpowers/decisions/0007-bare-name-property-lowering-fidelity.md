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

## Implementation note (D1)

- **What landed.** `IRBuilder.AccessorMemberOf` / `AccessorMemberReceiver`,
  used at the two places a bare name is lowered — the identifier READ
  (`Visit(IdentifierExpressionNode)`) and the assignment TARGET
  (`Visit(AssignmentStatementNode)`; a compound `P += 1` goes through both).
  Invariant F is `IRVerifier.CheckInvariantF`, run by
  `VerifyAfterOptimization` beside V and S′ under the same switch.
- **MEASURED "exactly the node its qualified form produces".** The
  no-optimizer IR dump of each bare probe after the change is
  byte-identical, receiver type included, to its qualified twin's dump
  before it: P4 ≡ P1 (`Me.P = 10`), P5 ≡ P3 (`Me.Tick`), P6 ≡ `Box.P = 10`,
  the Shared getter ≡ `Box.Tick`, and the Overridable case below ≡ `Me.V`.
  The emitted C#, JavaScript and MSIL of P4/P5 at CLI and CLI `--optimize`
  is identical (paths normalised) to P1/P3's. No `IRVariable` spelled `P`
  or `Tick` remains in P4/P5.
- **P6's ASSUMPTION holds, measured before the change:** `Box.P = 10` and
  `t = Box.Tick` in a Shared method lower to `IRFieldStore(Box, P)` /
  `IRFieldAccess(Box, Tick)` (call-classified) and print the right value on
  C#, JavaScript and MSIL at all three entry points. The bare Shared form
  lowers to exactly that — the receiver is the DECLARING class, never `Me`
  (`this.P` on a static member is CS0176). P6 is wrong→right, not
  wrong→wrong; no node-addition ruling is needed.
- **Implementer choices beyond the ruling's letter, and the probe matrix
  below, were RATIFIED by the orchestrator:** (1) the accessor-backed scope
  (plain auto-properties out, Overridable/Overrides in); (2) all 69
  wrong→right cells, including P6g, P7 (`P += 10`), P8 (inherited), P9
  (Shared from an instance method), P10 (`P = K + 5`), CP3/CP4
  (CopyPropagation through a bare property — C# was ALSO wrong, printing
  `6` for `16` and `6` for `11`) and MSIL P16 — under ADR-0007's own churn
  rule these are the ruling's own scope ("a bare name the analyzer binds to
  an accessor-backed member"), not unintended changes; (3) the generic
  MSIL ByRef message; (4) the new flags. Item (1)'s detail:
  - *"Accessor-backed" is defined* as: the property declares a Get or Set
    block, OR is Overridable/Overrides (a derived accessor may run in its
    place). One definition, read twice: `PropertyNode.IsAccessorBacked`
    (copied by the analyzer onto `Symbol.IsAccessorBacked`, which the
    lowering reads) and `IRProperty.IsAccessorBacked` (which F reads).
    A plain non-virtual AUTO-property is excluded: no user code runs
    behind it, so it is storage, like the plain field the ruling puts out
    of scope. This matters for Obligation 2: a bare plain auto-property is
    the one bare-property shape C++ builds AND runs right today (MEASURED
    green on all four backends, as `AGetSetPropertyByBareName_IsACppGap_Pinned`'s
    own text says), so it is left exactly as it was. Every Get/Set property
    used bare is COMPILE-FAIL on C++ today (P4–P11, P13, P14), and the one
    other shape C++ builds — an Overridable auto-property, below — prints the
    same wrong value before and after, so Obligation 2's condition is not
    met and the C++ property-codegen fix stays a separate brief. **P12 (the
    bare plain auto-property, MEASURED green on all four backends including
    C++) stays green** — this scope change touches no shape C++ was already
    getting right, so Obligation 2 was NOT triggered by this ruling.
  - *Overridable/Overrides auto-properties are in scope.* MEASURED: a base
    method writing its own `Overridable Property V As Integer` bare, with a
    derived `Overrides` setter writing `K`, printed `3,3` for `12,3` on
    MSIL; it now prints `12,3` (its `Me.V` twin already did). JavaScript is
    wrong for BOTH forms before and after (a class-field initializer
    shadows the derived accessor — a separate JavaScript defect), and C++
    builds and prints `3,3` for both forms, before and after (unchanged).
  - *Static-ness and accessor-backedness are recorded on the analyzer's
    `Symbol`* (`IsShared`, `IsAccessorBacked`, set by `Visit(PropertyNode)`
    and by the pass-1/sibling signature path). The IR class is built in
    source order, so a property declared below its use is not yet in it;
    the symbol the analyzer bound is complete whatever the order or file.
  - *A built-in or .NET base property* (`Exception.Message` read bare in a
    subclass) is not flagged, so it is not lowered: its outputs (C# right,
    JavaScript `ReferenceError`, MSIL `InvalidProgramException`) are
    unchanged, and F only checks bases present in the IR module.
  - *F's exemptions,* mirroring how every backend resolves a bare name: a
    name f declares (parameter, local, For Each / Catch / pattern variable,
    and for a lambda its creators' declarations) or a module global
    shadows the member; the NEAREST member of a name wins up the base chain
    (a derived field shadowing a base property is not a breach). A lambda
    (`__lambda_N`) belongs to the class of the function that creates it.
    "Destination" includes a value renamed after a variable
    (`NamedDestination`): `P = K + 5` / `P += 10` used to lower to an add
    RENAMED `P`, with no `IRAssignment` at all.
- **Probe matrix, 62 probes × 4 backends × 3 entry points:** 69 cells
  wrong→right, 0 right→wrong; P4/P5/P6 are 18 of them. The other 51 are
  all this ruling's defect in another position: compound `P += 10`; an
  inherited property; a Shared property read or written bare from a Shared or an
  instance method; `P = K + 5` (a renamed value); the Overridable case on
  MSIL. And CopyPropagation, which reads the same IR (Obligation 5): with
  `P = 5 : Inc() : P + 1`, and with `P = 5 : P + 1` over a getter
  returning `K * 2`, the copy fact `P = 5` was propagated through the
  getter — `6` for `16` and `6` for `11` on C#, JavaScript AND MSIL at
  every entry point. Both are right now on all three, with no
  CopyPropagation change. Task #146's own probe (a plain FIELD across a
  call) is unchanged: `6` for `16` on all four backends, before and after.
- **C++:** every moved C++ cell is COMPILE-FAIL → COMPILE-FAIL; the first
  error changes from "use of undeclared identifier 'P'" to the qualified
  form's "no member named 'P' in 'Box'" (task #141).
- **Corpus pins: unchanged, nothing to attribute.** The sample games
  declare no class (0 bare-property sites); `Corpus_Platformer_Makes6Merges`
  and `Corpus_SpaceShooter_Makes0Merges` pass unchanged. Across the whole
  suite the lowering fires 19 times (one test program compiled for several
  backends counts once per compile; 7 test programs), the fast subset
  none. Over every optimizer run those programs' fixtures make (612
  pipeline runs, 15 fixture classes), the per-pass modification totals are
  identical with the lowering on and off: no CSE merge, LICM hoist or other
  pass rewrite is lost.
- **F alone would fire** — MEASURED with the lowering switched off and F
  on: 11 F reports over the property-bearing fixtures (11 real sites), and
  5 existing tests turned red in Throw mode. That is Obligation 3's reason
  for one commit, observed: F alone would have turned 5 passing tests red
  for a defect the lowering alone would have left undetectable elsewhere.
  With the lowering ON (this commit, as shipped), **F fires ZERO times**
  across the whole suite (see the Suite bullet below) — quiet exactly
  where it should be.
- **Separate defects found and tracked while measuring the above, each
  UNRELATED to this ruling's lowering and left exactly as found:** task
  #150 (an Overridable auto-property's Overrides does not DISPATCH on
  JavaScript or C++ — both print the BASE value, `3,3`, for both the bare
  and the `Me.`-qualified form of P16, before and after this ADR; a
  class-field initializer shadows the derived accessor on JavaScript,
  unmeasured root cause on C++); task #151 (P15 — `Message` read bare in a
  class inheriting `System.Exception`; JavaScript `ReferenceError`, MSIL
  `InvalidProgramException`, unchanged by this ADR because F only checks
  bases PRESENT in the IR module, and a built-in .NET base is not); task
  #152 (P17 — a `Private` property declared BELOW its bare use fails to
  compile at all, "Undefined identifier 'P'", on all four backends; P17p,
  the identical shape with the property `Public`, runs correctly — a
  pass-1/sibling-signature visibility gap in the semantic analyzer, not a
  lowering defect).
- **One existing expectation changed:**
  `MsilByRefTests.BarePropertyNameArgument_IsRefused` (a bare Get/Set
  property passed `ByRef`) is still REFUSED by MSIL, but now with the
  message its qualified twin `QualifiedPropertyArgument_IsRefused` already
  gets ("an expression's value lives in a temporary") instead of "'X' is a
  PROPERTY" — the argument is an `IRFieldAccess` now, not a name. No
  backend's build or run outcome for that program changed (C# CS0206,
  C++ compile error, JavaScript BL7002, MSIL refusal, before and after).
  The "is a PROPERTY" arm is still live for a bare plain auto-property.
- **Suite (Linux):** the fast subset is cell-for-cell identical to the
  base (5763 tests, 0 failed); the full suite is 7979 tests, 0 failed, 269
  skipped, with the verifier in Throw mode — so V, S′ and F are quiet
  across all of it.
- **OWED:** the Windows run for MSIL and `.blproj` (see the third settled
  point's NOTE) — the owner has explicitly ACCEPTED that this stays owed
  for this merge, on the strength of the Linux MSIL measurements above
  (ilasm and a real CLR run, on every entry point) standing in for it.

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
