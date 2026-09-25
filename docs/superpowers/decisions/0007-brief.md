# BRIEF: User code behind a classified IR kind — a property used by its bare name

Date: 2026-09-24; HEAD `96cd93f` plus the uncommitted ADR-0006 D1 patches (the state this question is about).
Every table was compiled AND run on 4 backends × 3 entry points: CLI, CLI `--optimize`, Release `.blproj`. Each table gives one verdict per backend because the three entry points agreed in every cell. ⚠ A C++ Release `.blproj` runs the STANDARD pipeline (task #134); CSE is a standard pass, so all three entry points exercise it here.

## The question

ADR-0006 D1's Revisit clause: "V is quiet and a probe on a *classified* kind prints a stale value. That means the vocabulary is wrong, not incomplete, and the verifier then needs a real may-alias model." **That trigger is met.**

Inside its own class, a property can be used by its bare name. `P = 10` runs the `Set` accessor and `t = Tick` runs the `Get` accessor, but IRBuilder lowers both as if `P` and `Tick` were plain variables:

| probe (in a method of the class that declares the property) | IR after IRBuilder, no optimizer | IRBuilder site |
|---|---|---|
| P1 `Me.P = 10` | `IRFieldStore Me.P = const_10` | IRBuilder.cs:4330 |
| P3 `t = Me.Tick` | `IRFieldAccess t = Me.Tick` | IRBuilder.cs:4943 |
| **P4 `P = 10`** | **`IRAssignment %P = 10`** | IRBuilder.cs:4300 `GetOrCreateVariable`, :4308 `new IRAssignment` |
| **P5 `t = Tick`** | **`IRAssignment %t = %Tick`**, where `%Tick` is an `IRVariable` | the read of `Tick` from `Visit(IdentifierExpressionNode)`, IRBuilder.cs:4900 `GetOrCreateVariable` |

**The accessor does run on every backend**, because each one resolves the bare name to the class member. JavaScript emits `this.P = 10;` and `t = this.Tick;`, both of which invoke the JS accessor. MSIL emits `callvirt instance void 'Box'::set_P(int32)`. C# emits `P = 10` inside the class, where C# resolves it to the property.
**What is wrong is the optimizer.** Each probe is `K = Seed(1) : Dim a = K + q : <accessor> : l(0) = K + q`, and the accessor writes the field `K`:
- `P`'s setter does `K = value`.
- `Tick`'s getter does `K = K + 10`.

To the kill vocabulary, P4's `IRAssignment` names only `P`, and P5's `IRVariable` read names nothing at all; neither is a call. So CSE keeps `a = K + q` alive across the accessor and reuses it. JavaScript emits `this.P = 10; l[0] = a;`, and MSIL does `callvirt set_P` and then stores `ldloc.1` (that is, `a`).

| probe | expected | C# | C++ | JavaScript | MSIL |
|---|---|---|---|---|---|
| P1 `Me.P = 10` | `12,3` | ✓ | does not build | ✓ | ✓ |
| P3 `t = Me.Tick` | `13,3,11` | ✓ | does not build | ✓ | ✓ |
| **P4 `P = 10`** | `12,3` | ✓ | does not build | **`3,3`** | **`3,3`** |
| **P5 `t = Tick`** | `13,3,11` | ✓ | does not build | **`3,3,11`** | **`3,3,11`** |

- P1 and P3 are right because ADR-0006 D1 (step 6c) classifies `IRFieldStore` and `IRFieldAccess` as calls; property Get/Set reached through `Me.` lowers to them.
- C# is right on P4 and P5 only because it re-emits `K + q` as text instead of honouring the merge, the same accidental correctness ADR-0005 D2 describes.
- C++ does not build any property probe (`this->P = 10` where no member `P` exists; a mutating getter in a `const` method). That is a separate C++ property-codegen defect.

User-defined operators and conversions: the parser accepts an `Operator` declaration, but the analyzer rejects its use (probe OP1: "Arithmetic operator '+' requires numeric operands"). So no program can reach one today, and they are not part of this question. They WILL be, the day operator resolution lands.

**Decision: how does the optimizer learn that a bare-name property access runs user code?**

## Current state

- The kill vocabulary is `OptimizationPass.NamesWrittenBy` (total since ADR-0006 D1: every kind is Named, None or Universal, and invariant V checks totality) plus one call-visibility predicate, `IsCallVisible` (ADR-0006 D3's declarations rule: a by-value parameter or declared local is private, and any undeclared name is visible). CSE, LICM and `IRVerifier` all use it.
- A bare property name is undeclared in the method, so `IsCallVisible` already reports `P`, `Tick` and `K` as call-visible. The gap is that the assignment and the read are not CALLS, so nothing kills the call-visible `K`.
- IRBuilder already emits the `Me.`-member nodes for `Me.P`, and all backends except C++ handle them.

## Constraints already fixed

- ADR-0001 E1 (evaluate once); ADR-0004 D2 (IsReplicable is structural); ADR-0005 D2 (S′); ADR-0006 D1 (one total vocabulary, with V as the verifier's independence) and D3 (one call-visibility predicate, unknown = visible).
- CLAUDE.md: shared logic changes once, not per consumer.

## What breaks if wrong

- Too narrow: JavaScript and MSIL print a stale number with no diagnostic, while C# stays right by re-emitting text, so the oracle hides it. The verifier shares the gap and certifies it.
- Too wide: CSE and LICM lose merges and hoists. Killing on EVERY read or write of an undeclared name would also hit plain fields, which are undeclared too. The cost of that is UNMEASURED.

## Options the team already sees

- **A: lower a bare property name like its `Me.` form.** IRBuilder emits `IRFieldStore(Me, P)` / `IRFieldAccess(Me, Tick)` when the analyzer binds the name to a property of the enclosing class. These are node kinds the backends already emit for `Me.P`, and D1 already classifies them as calls. It changes the IR (and the emitted text) for every bare property use; the output churn on each backend is UNMEASURED.
- **B: the optimizer consults declarations.** `NamesWrittenBy` / `IsCallVisible` learn which undeclared names are properties with accessors, from the class declarations the IR carries, and treat an assignment to or read of such a name as a call. The IR is unchanged, but the optimizer needs class metadata it does not consult today.
- **C: an assignment to or read of any undeclared name is a call.** No metadata is needed. It over-kills every plain field access; the merges and hoists lost are UNMEASURED.

For the verifier: under A, V and S′ see the calls with no new mechanism; under B, the verifier must consult the same declarations (one predicate); under C, no change. ADR-0006 D1's "real may-alias model" would matter only if none of these covered a form, and none measured here needs one.

Related, not part of this question:
- CopyPropagation keeps its own kill rules with no call arm: `K = 5 : Inc() : K + 1` prints 6 where 16 is right, on all four backends (task #146). It is being fixed under the existing one-vocabulary rule.
- C# drops a statement-level `MyBase.M()` (task #139).

---
*Corrections by the orchestrator after review (2026-09-24), each re-measured:*
- *The brief role said JavaScript and MSIL never run the accessor and treat the bare name as a global. That is false: the emitted JS is `this.P = 10;` / `t = this.Tick;`, and the MSIL `callvirt set_P`. The wrong answer is CSE reusing `a` across the accessor, read from the emitted JS and IL.*
- *C#'s "hidden compensation" is its ordinary text re-emission.*
- *P5's IR site was given as IRBuilder.cs:4308, the assignment arm; the read of `Tick` comes from `Visit(IdentifierExpressionNode)` at :4900. The IR was re-dumped with no optimizer pass.*
- *Options were rewritten. The draft's option B ("accept the wrong answer as a dialect difference") answered a different question. Its option C cited ADR-0006 D3's "kill on every call", which is not "treat an undeclared-name access as a call".*
