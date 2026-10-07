# ADR 0013: Case-insensitive name binding between the front end and the IR

- **Date:** 2026-09-28
- **Status:** Accepted (D2 decided by the owner, 2026-10-05; see the amendment under D2)
- **Decided by:** the architect role, in two rulings on the same day (D1–D4, then D5–D8 on the
  registration gap D1 and D3 left). Transcribed from the rulings; nothing below the headings is
  editorialised.
- **Brief:** the orchestrator's `architect-brief.md` for task #169 (session scratchpad, not kept
  in the repo).
- **Tasks:** #169 (a lambda parameter referenced in a different case), #199 (the IR builder's
  version map was never scoped per procedure), #124 (C++/JavaScript do not case-fold
  identifiers — the same root, program-wide).

## Question

BasicLang is case-insensitive; the IR builder's variable maps (`_variableVersions`, `_locals`,
`_globalVariables`) and `ResolvesToExistingStorage` are Ordinal. A reference spelled differently
from its declaration either minted a second, undeclared IR variable (`Function(N) n * 2`: CS0103 on
C#, undeclared on C++, ReferenceError on JavaScript, refused on MSIL) or fell through to a
same-spelled class member (`Private n … Function(N) n * 10` printed 10 on C#, C++ and JavaScript;
VB prints 40). Where is "which declaration does this spelling denote" decided for the IR?

## Decision

### D1: the analyzer records the binding; the IR builder binds through it (Option B)

The semantic analyzer records the binding on the AST, and the IR builder binds through it and never
folds case itself. `_variableVersions`, `_locals`, `_globalVariables` and
`ResolvesToExistingStorage` stay **Ordinal**.

- **Where it lives:** ON THE NODE, not in a side table. It is set at the analyzer's single
  `SymbolTable` lookup point and overwritten on every analysis pass, so a stale binding cannot
  survive re-analysis. The written `Name` is never rewritten. Invariant: `Binding.DeclaredName`
  equals `Name` under OrdinalIgnoreCase, always.
- **What it holds:** `DeclaredName` is the declaration site's spelling, verbatim. `Declaration`
  is compared by reference, never by name.
- **How the IR builder uses it,** for a bound reference: the key is `Binding.DeclaredName`, and
  `IRVariable.Name` is that spelling; the lookup is restricted to the store its Kind names — Local,
  Parameter and LambdaParameter go to `_variableVersions` only, never to `_moduleGlobals` or class
  members (the K8 fix); a miss for a bound reference is an **internal compiler error, never a
  silent create**.
- **Exempt** (`Binding == null`; the existing path is unchanged): IR temps; ClosureLowering
  environment names; `Me`/`MyBase`/`MyClass`; `::` foreign C++ names; .NET interop members with
  no BasicLang `Symbol`; any name the analyzer degrades to `Object` without a symbol.

### D2: BC36641 — PENDING an owner decision

Architect's recommendation: do not report it. A lambda parameter shadows, case-insensitively, and
K1, K6 and K7 behave identically: inside the body the parameter is read and written, and the
enclosing variable is untouched and absent from the capture set. Whether BC36641 is reported is
language policy and goes to the owner; #169 adds NO diagnostic and implements the shadowing D1
produces on its own. Invariant for #169: the capture set for K1 and K6 equals K7's.

**Amendment (task #217, 2026-10-05):** the owner ruled the VB way. BC36641 IS reported: a lambda
parameter named, case-insensitively, like a local or parameter declared outside its lambda within
the procedure around it (a `Dim`, a local `Const`, a `For` / `For Each` / `Catch` variable, a
parameter of the procedure or of an enclosing lambda, including one declared later in an enclosing
block) is refused at the parameter, so K1, K6 and K7 no longer compile and there is no shadowing to
bind. A class field or property, a module global, a sibling lambda's parameter and a local of a
sibling block are not hidden-by-error; the case-insensitive binding above still decides those.

### D3: the scope boundary between #169 and #124

#169 delivers the general mechanism, and #124 extends where it is consumed. #169 records D1's
binding for EVERY reference the analyzer resolves, and consumes it at ONE IRBuilder site: the
identifier-expression path that ends in `GetOrCreateVariable`, for the Kinds Local, Parameter and
LambdaParameter. At a consuming site, a Kind is bound through the record everywhere or nowhere,
never half. A declaration the analyzer synthesizes for an undeclared name (Option Explicit Off, if
supported) is its own Kind (`ImplicitLocal`), outside #169, and keeps the create path.

### D4: #199 lands first

#199 lands FIRST, as its own commit with its own corpus diff, before #169. `_variableVersions`
and `_locals` push a scope at each procedure body and pop it at exit. Lambda bodies nest inside the
creator's scope (they capture) and are NOT new scopes.

### D5: register every analyzer-declared Local/Parameter/LambdaParameter at its declaration site

Every declaration the analyzer binds as Local, Parameter or LambdaParameter is registered in
`_variableVersions` at its declaration site, before the body that can reference it.

- **For Each control variable:** registered when the statement declares it (`As`, or a fresh
  name), and scoped to the loop body. The ordinal `ResolvesToExistingStorage` new-vs-existing
  decision is untouched.
- **`__foreach_N` (ADR-0009):** synthesized, so it carries no Binding and is exempt from the ICE
  rule. Registered at its synthesis site anyway.
- **Setter parameter:** the storage stays the backends' `value` contract, and the declared
  spelling (`nv`) is registered as an ALIAS to that IRVariable. This is the ONE sanctioned place
  where `IRVariable.Name != DeclaredName`, and it is chosen at the declaration site, never at a
  reference.
- **LINQ range variables:** registered where the lowering introduces the parameter that carries
  them, for the clause's scope.

Invariant: after #169, no bound Local/Parameter/LambdaParameter reference reaches the create
branch of `GetOrCreateVariable`. D1's ICE stays as the detector.

*Amendment (task #219, 2026-10-07): the setter `value` alias is no longer the only place a Local's IR
name differs from its declared spelling. The implicit return variable of a Function or a property Get
(`F = v`, `P = v`) is a synthesized Local of the procedure's scope, and its carrier `__ret` is the
second. It is bound through `binding.Declaration`, compared by reference, never by name: a bare
own-name reference is bound to it by `SemanticAnalyzer.AsReturnVariable`, while a call `F(n - 1)` and
`AddressOf F` still name the procedure. Like the alias, it is chosen at the declaration site, and the
carrier's name is reserved under ADR-0018.*

### D6: the #169 / #124 boundary, restated

#169 owns DECLARATION REGISTRATION wherever a Local, Parameter or LambdaParameter is declared,
plus the identifier-expression REFERENCE site. #124 owns the RESOLUTION DECISIONS at
non-identifier sites — `ResolvesToExistingStorage` choosing new-vs-existing for `For`/`For Each`
without `As`, and the Catch/Using/ReDim declarators — and the reference kinds Field,
ModuleGlobal, Property, Method, Type and Event. K10 (a case-differing `For Each` control variable
read inside a lambda) lands in #169.

### D7: Event symbols

Left unbound (`Binding == null`) in #169; `Event` is added to `NameBindingKind` when #124 consumes
it. An unbound reference takes the existing verbatim path unchanged.

### D8: a resolved `Symbol.Name` that fails the OrdinalIgnoreCase invariant

No Binding is recorded and the reference stays on the verbatim path. The analyzer sets `Binding`
only when `Symbol.Name` equals `node.Name` under OrdinalIgnoreCase; the nulls-by-mismatch are
counted in debug, so #124 can size them.

## Because

- K8 proves the two resolvers already disagree; case-insensitive IR maps (Option A) keep both and
  only make them agree more often.
- With the declared spelling as the lookup key, an Ordinal IR map is correct by construction, so
  the ordinal rule in `ResolvesToExistingStorage` needs no re-deciding.
- Byte-identity for spelling-matched programs is automatic: there, `DeclaredName == Name`.

## Contract

```csharp
public enum NameBindingKind { Local, Parameter, LambdaParameter, Field, ModuleGlobal, Property, Method, Type /* #124 extends */ }
public sealed record NameBinding(string DeclaredName, NameBindingKind Kind, Symbol Declaration);
// additive, on the identifier-reference AST node the analyzer resolves via SymbolTable:
public NameBinding? Binding { get; set; }
```

## Obligations

- MSIL's ADR-0010 "differs only by case" check stays as the backstop. After #169 it must never
  fire on a program the analyzer accepted; a firing means a reference bypassed the binding.
- LSP: the property is additive and ignorable.
- `ModuleResolver.cs` is untouched.

## Rejected

| Alternative | Why rejected |
|---|---|
| Option A: OrdinalIgnoreCase IR maps | Keeps two resolvers, and K8 shows they disagree. It widens #199's leak and forces re-deciding `ResolvesToExistingStorage`. |
| Option C: per-backend folding | Collides with case-sensitive .NET names, rewrites every emitted file, and gives the IR a different meaning per backend. |
| Fold case only inside lambda bodies | A syntactic scope for a rule about names: the same Local would bind differently inside and outside a lambda. |
| Record `DeclaredName` only, with no Kind or identity | K8 cannot be fixed without Kind, because the field and the parameter share a spelling. #124 and #199 need identity. |
| A side table `Dictionary<Node, NameBinding>` | It would have to be threaded through `CompileProjectFiles`, the LSP and every test helper. The AST already travels. |
| Uniquify IR names per declaration (`n`, `n_1`) | Changes user-visible names (DAP, generated source) and breaks byte-identity. |
| Bundle #199 into #169 | Makes the corpus constraint unmeasurable. |
| D5 (B): a separate Kind for the unregistered declarations, left on the create path | Refused by the follow-up ruling. |
| D5 (C): fall back to creating on a bound miss | Refused; D1's ICE stays. |

## Revisit if

- **D1:** any corpus program whose every reference already matches its declaration's spelling
  byte-differs after the change.
- **D4:** #199 alone changes the output of any corpus program that compiles and runs correctly on
  all backends today.
- **D5:** a spelling-matched corpus program byte-differs (registration disturbed naming), or there
  is any ICE on the corpus (a declaration site was missed).
- **D6:** #124 needs to touch a declaration-registration site, rather than a resolution site or a
  new Kind.

## Amendment (2026-09-30): #124 consumed D3

This section records facts, not a new ruling. It states what #124 built on D1, D3, D6 and D7, where it
went past what this ADR wrote down, and one deliberate deviation from the orchestrator's answer to the
brief's Q3, which is left for the architect (#250). Sources: the #124 fix (`c55e91bd`), the orchestrator's
binding answers (Q1/Q2: the counted `For`; Q3, option (A): consume where the IR node is created), and
the test-writer's measurements.

- **What #124 consumed (D3, D6).** Every reference kind the analyzer records, at every path that creates
  its IR node: "a Kind is bound through the record everywhere or nowhere, never half".
  - `IRBuilder.ReferencedVariable` goes through `BoundVariable`, keyed by `Binding.DeclaredName` and
    looking only in the store its Kind names. Local, Parameter and LambdaParameter: `_variableVersions`,
    a miss an ICE (#169, unchanged). Field and Property: the member's own variable, by declared name.
    ModuleGlobal: the global itself, by declared name (a Module's own, another file's, or a file-scope
    one). Method, Type and Event: a name, not storage, by declared spelling.
  - Module-member globals and imported globals, read and write; `AccessorMemberOf`; an `Await` callee;
    `RaiseEvent`; and the counted `For` (below).
  - Not changed: `For Each` (the analyzer already decides reuse case-insensitively), `Catch` (always a new
    declaration; a `Catch err` with no `As` that should reuse a variable is #248), `Using` (no statement
    form exists), `ReDim` (lowered to an assignment, so covered).
- **Member access and `New` consume the analyzer's symbol or type, not a `NameBinding`.** D1 puts the
  binding on `IdentifierExpressionNode`. `MemberAccessExpressionNode` (`obj.m`, `C.m`, `Me.m`,
  `MyBase.m`) and `NewExpressionNode` are not identifier references, so they carry none. They take the
  spelling from what the analyzer RESOLVED for that node (`GetNodeSymbol`, `GetNodeType`):
  `DeclaredMemberSpelling` for a member read, a member store, an instance call, a Shared call and a
  `MyBase` call, and the resolved class for `New`. The declared spelling replaces the written one ONLY when
  the two differ by case alone; a .NET member (resolved through its descriptor) keeps the spelling it was
  written in. There is no second lookup by name.
  - `IRBuilder.CanonicaliseMemberNames` predates this ADR and still rewrites member names after the walk,
    by its own lookup of the receiver's class. It is a second resolver, and the sites above make it
    redundant for an instance member of a class the analyzer knows. Whether it is retired is **#250**.
  - ⚠ While it stays, the sites and the post-pass mask each other in the final IR: four of the twenty
    #124 mutants (the accessor member, the member read, the member store and the instance call spelled
    as written) changed nothing observable end to end. A test that must see a member site's own answer
    reads the IR BEFORE the post-pass (`BindingSiteIr.BuildBeforeCanonicalisation`). A fifth mutant
    (`raise_<written spelling>`) was masked the same way one layer down: the C# and JavaScript backends
    look the event up again by their own case-insensitive name.
- **D7: `Event` joined `NameBindingKind`.** `BindingKindOf` maps `SymbolKind.Event` to
  `NameBindingKind.Event`, so an event named as an `AddHandler` operand is bound like any identifier. A
  `RaiseEvent` statement carries the event by name, not as an identifier node, so the analyzer records it
  through a synthesized `RaiseEventStatementNode.EventReference`, bound at the one recording point
  (`SetNodeSymbol`); the IR builder raises `raise_<declared spelling>`. The reference is metadata, not a
  child of the statement: nothing visits it, and it is overwritten on every analysis pass.
- **D3/D6: the counted `For` with no `As`.** The decision "existing storage, or declare a variable" is the
  analyzer's, recorded as `ForLoopNode.ControlReference`: a synthesized reference to the loop's control
  name, bound at the one recording point to whatever the name already denotes. When that binding is a Local,
  Parameter, LambdaParameter, Field, Property or ModuleGlobal, the loop DRIVES that storage, reached by its
  declared spelling through `BoundVariable`, in any case (VB: `For total = ...` over a field `Total` drives
  the field). The `As` form declares a new variable and records no control reference. A Method or Type name
  is not storage. The Ordinal `ResolvesToExistingStorage` is asked only when the analyzer bound no storage;
  it stays, for one job: not declaring twice a same-spelled local the function already declares (a `Dim` in
  an earlier, closed block, which the analyzer rightly no longer sees).
- **The deliberate absence of a Field / Property internal compiler error.** The orchestrator's answer to Q3
  was that a bound Field or ModuleGlobal reference whose declaration is not found is an ICE, mirroring
  `ReferencedVariable`. #124 applies the ICE to Local, Parameter and LambdaParameter (D1, unchanged) and to
  a FILE-SCOPE ModuleGlobal, and does NOT apply it to a Field or a Property, nor to an owning-module or
  imported ModuleGlobal (a Module may be declared after its use, so an absent declaration is a forward
  reference, as it always was). Why: a member has no IR-side registration that is complete at the
  reference. The IR class list is filled in declaration order and holds THIS unit's classes only, so an ICE
  would refuse programs VB accepts. The analyzer's own declaration is the evidence the member exists.
  - Measured by the test-writer: two shapes reach it from source and still build and print VB's answer or
    fail in their own backend, never in the IR builder (an enclosing class's Shared field read from a nested
    class, and a base declared after its derived class; the nested-class shape fails on every backend for a
    reason that predates #124, in the same-case control too). A Structure's member cannot be named bare (a
    Structure holds fields only) and a base class in another file is refused today (`Unknown base class`).
    Those two are covered by hand-built input: a Field or Property binding whose declared name nothing
    registers builds without throwing.
  - ⚠ The file-scope ICE cannot be reached from source either (the analyzer refuses a file-scope global
    used before its declaration, and the declaration always registers it); it is tested on a real front end
    with a hand-tampered binding, the idiom the Local ICE's test uses.
- **Obligations, checked.** MSIL's "differs only by case" backstop fired 0 times across the corpus (7,602
  common cells before and after, and the probe matrix). The byte diff of 1,091 programs x 14 cells was
  confined to programs that spell a name in a different case from its declaration (280 cells, 25
  programs); 0 cells differ outside that set, 0 verifier fires, 0 return-code changes.
- **Follow-ups this work filed:** #244 (cross-file module globals that differ only by case), #245 (events),
  #246 (C#: a `For` over another module's global), #247 (`For x As T` leaves the variable bound), #248
  (`Catch err` with no `As`), #249 (a Function's own name as its return value), #250 (the architect
  question above), #251 (the `dotnet test --filter` trap; see `docs/HANDOFF.md`).
