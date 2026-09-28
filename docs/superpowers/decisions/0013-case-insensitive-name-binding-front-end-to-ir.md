# ADR 0013: Case-insensitive name binding between the front end and the IR

- **Date:** 2026-09-28
- **Status:** Accepted (D2 pending an owner decision; see D2)
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
