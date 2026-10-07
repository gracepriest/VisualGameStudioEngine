# ADR 0015: `Me` as a value on the C++ backend — `enable_shared_from_this` roots and two-phase construction

- **Date:** 2026-09-29
- **Status:** Accepted; amended the same day (D2a, the corrected premises, E11, E12), after the
  first implementation attempt stopped on a measured foreign-base shape. The amendment replaces
  the ruling's text where they differ.
- **Decided by:** the architect role, in a ruling (D1–D3) and a binding amendment. Transcribed
  from both; nothing under the Decision headings is editorialised. Implementation notes and
  measurements are marked as such.
- **Brief:** the orchestrator's `architect-brief.md` / `implementer-brief.md` for task #200
  (session scratchpad, not kept in the repo).
- **Task:** #200. Unblocks #140 (C++ onto ClosureLowering), which builds on D1's helper.

## Question

Every use of `Me` as a VALUE failed to compile on C++ only — "no viable conversion from 'Node *'
to 'std::shared_ptr<Node>'", or the `=`, return or `const shared_ptr&` binding equivalent. Every
class instance is a `std::shared_ptr<T>` made by `make_shared`, and `Me` rendered as the raw
`this`. The failing uses (probes M1–M7): an argument, `Return Me` and `Dim n As Node = Me`,
`list.Add(Me)`, `Me` passed out of `Sub New` (the registration pattern), `Me` from a base and a
derived method into base- and derived-typed parameters, a Shared method taking `Me`, and a field
store. C#, JavaScript and MSIL printed VB's answer for all seven. `shared_from_this()` throws
`bad_weak_ptr` inside a C++ constructor, so the constructor case cannot be served by
`shared_from_this` alone. How does `Me` become the owning `shared_ptr`, inside a constructor too,
and at which sites?

## Decision

### D1: `Me` as a value outside a constructor

- Every class with no user base class (the hierarchy ROOT) gets
  `public std::enable_shared_from_this<Root>` as its LAST base-specifier: after the user or
  runtime base and after every interface. This includes templates
  (`enable_shared_from_this<Box<T>>`).
- Derived classes add nothing.
- One runtime helper, `BasicLang::Self(this)`, is the only spelling of `Me`-as-value anywhere.
- It is emitted unconditionally, on every root.

### D2: `Me` as a value inside a constructor — two-phase construction, every class, always

- `New C(args)` becomes `BasicLang::New<C>(args)`.
- The C++ constructor becomes a TAG constructor that leaves every field at its .NET default.
- The base call, the field initializers and the body move into a `ctor_` overload set, one public
  `ctor_` per VB constructor.
- Order inside `ctor_`, per VB: (1) `Base::ctor_(MyBase.New args)`, or an implicit
  `Base::ctor_()`; (2) this class's field initializers, in declaration order; (3) the body.
- A `ctor_` that chains `Me.New(...)` calls the sibling `ctor_` FIRST and emits NO field
  initializers. **DORMANT** (amendment): the front end refuses `Me.New` on every backend; the rule
  is kept verbatim as binding for when chaining lands, because it is VB's order.
- The name carries no `__` (a reserved identifier).
- Structures are untouched: real constructors, value `Me`.

### D2a (amendment): a hierarchy whose root's base is a `#CppInclude`d C++ class

- A *foreign-rooted* hierarchy keeps D2's two-phase protocol with ONE change: its tag
  constructors carry the VB constructor's parameters, so the foreign base is constructed in the
  member-initializer list.
- `BasicLang::New` stays ONE spelling: `if constexpr (std::is_constructible_v<T, Construct, A&...>)`
  passes the arguments to the tag constructor as LVALUES; only `ctor_` receives them forwarded.
- Every `MyBase.New` argument into a foreign-rooted class must be PURE — a parameter, a literal or
  constant, or an operator expression over those that lowers with no preceding statement.
  Anything else is refused by `CppCapabilityChecker` with a named diagnostic.
- `static_assert(!BasicLang::HasSharedFromThis<Foreign>, …)` is emitted beside the class head.
- F6, F10 and F11 keep running. Options (a) refuse `MyBase.New(args)` into a foreign base,
  (b) refuse `Inherits <foreign>`, and (c) one-phase construction for foreign-rooted hierarchies
  are rejected.

### D3: which sites, decided where

- A backend-local rendering rule; no IR marker.
- `IRVariable "Me"` in a class renders as `BasicLang::Self(this)` by DEFAULT.
- Exactly two contexts keep raw `this`: the receiver of a member access or call (`this->V`,
  `this->M()`), and an `Is`/`IsNot` operand (compared by `.get()`).
- Everything else is a value site by falling through: an argument, a return, a `Dim` or
  assignment, a field store, a collection add, `TryCast`/`DirectCast`, a concatenation or
  `ToString` argument, the receiver of an extension or free function.
- In a Structure the same switch yields `*this`. **DORMANT** (amendment): a Structure cannot
  declare a method (#230); the clause is the rule for #230. **LIVE since #230** (2026-10-07): a
  Structure declares methods and `Me` there is `(*this)`.

### E11 (amendment, confirmed)

"Base call first" is the first of D2's three steps, not before its own argument evaluation. The
temporaries for `Base::ctor_`'s arguments are the ONLY statements permitted before `Base::ctor_`
in a `ctor_`; no field initializer or body statement precedes it.

### E12 (amendment, confirmed)

`ctor_` never synthesizes a field store from a parameter name; only explicit statements store
fields. The old initializer list's `X(x)` heuristic is dropped, so `3 4` becomes VB's `0 4` — a
divergence fix, recorded as a behaviour change.

### Corrected premises (amendment; these replace the ruling's text)

1. There is no runtime `Exception` on C++: `Throw` lowers to `std::runtime_error` /
   `NetException` at the site, and neither is inheritable. D1's and D2's Exception obligations are
   struck; `StringBuilder` is the only remaining runtime-class obligation. When `Inherits
   Exception` lands (a separate task), its runtime base either owns its message as a field with a
   plain `Construct` constructor, or is a D2a foreign-shaped base over `std::runtime_error`; either
   way it must not carry `enable_shared_from_this`.
2. `Me.New` chaining is refused by the front end on all backends — D2's chaining rule is dormant.
3. Instance field initializers are constants and sized arrays only. Step 2 is still load-bearing:
   a virtual call from a base `ctor_` must see a derived field at its .NET default, so
   initializers run in `ctor_`, never in the tag constructor.
4. Structures have no methods (#230) — D3's `*this` clause is dormant. **Superseded by #230
   (2026-10-07):** a Structure declares methods and the clause is live.
5. `getX() const` is not a live trigger: the trigger is a `const` member function whose body
   renders `IRVariable "Me"` as a value. No speculative `const T*` overload.

## Because

- `make_shared` already creates every instance, so `weak_this` is seeded for free; a hand-rolled
  `weak_ptr` field would reimplement it.
- Detection needs an UNAMBIGUOUS base: two `enable_shared_from_this` subobjects in one object (on
  every class, or on a runtime base too) fail silently — `weak_this` is never set and the program
  dies with `bad_weak_ptr` at run time, not at compile time.
- `Self` takes `T*`, so the dependent-name problem (`this->shared_from_this()` inside a template)
  is solved once, in the runtime.
- Two-phase construction is the VB-exact mechanism: ownership exists before any user code runs, so
  a constructor can hand `Me` out, AND a virtual call from a base constructor dispatches to the
  derived override as .NET's does (the real-constructor emission called the base's).
- A throwing `ctor_` destroys a fully tag-constructed object — defined behaviour, closer to .NET
  than a throwing C++ constructor.
- D2a: the only fact a foreign base's tag constructor lacks is its arguments, and `New<C>(a...)`
  already holds them at `make_shared` time. "Foreign-rooted" is an ANCESTOR walk, not the
  descendant scan that sank D2(b). The owner's rule decides the rest: never regress a running
  program without cause. Purity makes the double evaluation (tag constructor and, at depth >= 2,
  `ctor_` step 1) unobservable.
- D3: only C++ has the `this` / `shared_ptr` split; a marker would be a flag four backends ignore
  and a second list of contexts. Value-by-default with a closed list of receiver exceptions makes
  any future expression kind correct with no change.

## Contract

```cpp
namespace BasicLang {
  struct Construct {};
  template<class T> std::shared_ptr<T> Self(T* self);            // static_pointer_cast<T>(self->shared_from_this())
  template<class T, class... A> std::shared_ptr<T> New(A&&... a);
  //   if constexpr (std::is_constructible_v<T, Construct, A&...>) p = make_shared<T>(Construct{}, a...);
  //   else p = make_shared<T>(Construct{});
  //   p->ctor_(std::forward<A>(a)...);    // args reach the tag ctor as LVALUES; forwarded only to ctor_
  template<class F> inline constexpr bool HasSharedFromThis;     // F derives from some enable_shared_from_this
}
// per class (user-rooted):   explicit C(Construct t) : Base(t), <every field>{} {}
// per class (foreign-rooted), one per VB constructor:
//                            explicit C(Construct t, P... p) : Base(t, <MyBase.New args>) {}
//                            direct-foreign child:            : Foreign(<MyBase.New args>)
// per VB constructor:        void ctor_(<params>)   // public; step 1 omitted by a direct-foreign child
```

- Invariant: exactly ONE `enable_shared_from_this` subobject per complete object; never on an
  interface; never on an inheritable runtime class.
- `Self` yields `shared_ptr<EnclosingClass>`; upcasts to base- or interface-typed targets are
  implicit.
- #140 inherits: the closure environment's `Me` field is a STRONG `std::shared_ptr<T>`, initialised
  with `BasicLang::Self(this)` at the single capture point; #140 never spells `shared_from_this`.
  Inside the lambda `Me` renders from the environment (`env->Me`), never `this`, never `Self(...)`.
  A lambda in a constructor that captures `Me` works only because of D2 — no special case. An
  instance holding a delegate that captures itself is a `shared_ptr` cycle (no GC); #140 documents
  it.

## Obligations

- C#, JavaScript, MSIL and LLVM: byte-identical for every program. There is no IR change.
- C++, a program that declares no class and calls no class `New`: byte-identical.
- C++, a program with a class: the diff is confined to a root's class head (plus D2a's
  `static_assert` beside a foreign-rooted head), constructor emission (tag constructor and `ctor_`,
  E12-shaped body changes included), and `New` sites. Every method body that never uses `Me` as a
  value is byte-identical.
- `StringBuilder` keeps its own `enable_shared_from_this` and stays non-inheritable.
- `Inherits ::ns::Foo`, engine-struct bases and `Inherits Exception` stay out of scope.

## Implementation notes (implementer, measured — not part of the ruling)

- **Where:** `BasicLang/CppCodeGenerator.cs` (class head, `GenerateTwoPhaseConstruction`,
  `GenerateCtorPhase`, the `New` site, `GetValueNameCore`'s `Me` arm, `ReceiverName`, the `Is`
  operand in `IdentityText`, the prologue hook in `GenerateBlock`),
  `Compiler/CodeGen/CPlusPlus/CppObjectModelRuntime.cs` (the runtime block),
  `Compiler/CodeGen/CPlusPlus/CppObjectModel.cs` (root / foreign-rooted / argument-placement
  rules, shared by the checker and the generator), `CppCapabilityChecker.cs` (the two refusals),
  `CppCodeGenerator.Split.cs` (the runtime splice in split mode).
- **The runtime block is spliced ON DEMAND** — only for a module that declares a class — in both
  emission modes, so a class-free program stays byte-identical. The byte classifier therefore has
  a fourth, mechanically checked kind: the exact runtime block.
- **Field declarations keep their in-class initializers** (so those lines stay byte-identical); the
  tag constructor's `field{}` member-initializers override them, so they never run. The value is
  set in `ctor_` step 2 as `this->F = <init>;` — qualified, because a VB parameter may share a
  field's name.
- **`BaseConstructorArgs` can be STALE** after optimization: it is not an instruction operand, so
  a pass that replaces an argument's node (strength reduction's `a * 2` → a new `a << 1`, on the
  default pipeline) re-points instruction operands only. A stale OPERATOR node renders inline
  (its replacement is equivalent by construction); any other non-live argument is refused.
- **E11 placement:** the prologue (step 1 + step 2) is written right after the last entry-block
  instruction that evaluates an argument — the arguments' operand closure, extended over the
  stores that fill an array-literal argument — and a build that never placed it fails loudly. A
  lambda argument renders inline, like any lambda use (`MyBase.New(Function(n) n + 100)`, t187
  `E13_mybase`, went from "use of undeclared identifier '__lambda_0'" to running). An argument
  built with control flow (`AndAlso`/`OrElse` make a local across blocks) is refused by name; it
  was "use of undeclared identifier" before.
- **Purity admits a module-level variable** beyond the amendment's literal list: between its two
  evaluations no BasicLang code runs, and `MyBase.New(G)` into a `#CppInclude`d base compiled and
  ran before (probe E14d) — refusing it would regress it. Casts are not admitted.
- **Receiver sites that keep `this`:** field read, field store, instance call (incl. the ToString
  shim), accessor-property receivers, the array-field lvalue, foreign member reads/calls, and the
  `Is`/`IsNot` operand (including its Nothing test).
- **Measured at implementation** (the #200 probe corpus, the t118/t122–t189 probe sets and the five
  samples, every backend × {CLI, CLI `-O`, Release `.blproj`}): C#, JavaScript and MSIL
  byte-identical; C++ byte-identical for all 973 class-free cells; every differing C++ line one of
  the ruled kinds or the runtime block; no C++ program that compiled and ran before stopped; the
  only output changes are V1 (a virtual call from a base constructor, now .NET's answer) and E12.
- **Hand-written C++ (owner ruling, "Direction B"):** hand-written C++ in a mixed project (spec
  `2026-07-11-cpp-language-support-design.md` §3) creates a BasicLang class with
  `BasicLang::New<T>(args)`, exactly like generated code. `std::make_shared<T>()` no longer
  compiles — a class has only the tag constructor — and that is intended: a public one-phase
  constructor would be a second protocol in which `Me` is unowned (`bad_weak_ptr` on the first
  value use inside `Sub New`). The spec, the wiki and `Split_ClassAcrossModules_SharedPtrRoundTrip`
  say `BasicLang::New`.

## Rejected

| Alternative | Why dropped |
|---|---|
| Common root `BasicLang::Object : enable_shared_from_this<Object>` | A universal base plus `dynamic_pointer_cast` at every `Me` site; no VB semantic gained. |
| `enable_shared_from_this<Derived>` on every class | Two subobjects are ambiguous: `weak_this` silently unset, `bad_weak_ptr` at run time. |
| Hand-rolled `weak_ptr<T> self_` seeded by a factory | Reimplements `enable_shared_from_this`; D2 closes the constructor gap soundly. |
| An explicit `shared_ptr<T> self` parameter on every method | Rewrites every signature, call site, virtual and interface. |
| D2(b), two-phase per hierarchy | A hierarchy-wide property needing a descendant scan across files; two protocols are a mirrored pair. |
| D2(c), a non-owning alias | Dangles in the registration pattern (M4). |
| D2(d), an honest refusal | Not VB; would also refuse #140's `Me` capture inside a constructor. |
| A thread-local construction stack with a custom control block | Multiple inheritance's `this` adjustment defeats the address match; an exception mid-construction is close to UB. |
| Conditional `enable_shared_from_this`, only where `Me` is used as a value | A whole-hierarchy pre-scan before the head is emitted; every future value site is a new flag to forget. |
| An IR marker "Me used as a value" | A flag four backends ignore, and a second list of contexts. |
| A `weak_ptr` capture of `Me` in #140 | VB closures keep the instance alive. |
| D2a (a): refuse `MyBase.New(args)` into a foreign base | Regresses F11; the arguments exist at `make_shared` time. |
| D2a (b): refuse `Inherits <foreign>` | Regresses F6, F10, F11; the shape is sound. |
| D2a (c): one-phase construction for foreign-rooted hierarchies | Two protocols, and `Me` in those constructors is unowned — the M4 dangle. |
| Admit calls in foreign-rooted `MyBase.New` arguments | Double evaluation at depth >= 2 is observable, and a call is exactly where it shows. |
| Cache computed base arguments in a member | A field written by the tag constructor breaks "every field at its .NET default". |
| A second `New` spelling for foreign-rooted classes | `if constexpr` on the tag-constructor signature keeps one spelling. |
| Run-time detection of a foreign `enable_shared_from_this` | A silent `bad_weak_ptr`; the `static_assert` is one line and loud. |

## Revisit if

- The backend ever emits a `const` member function whose body renders `Me` as a value (`Self`
  then needs a `const T*` overload; clang will say so).
- An instance is ever created other than by `BasicLang::New` / `make_shared`.
- A measured program shows a VB construction order the `ctor_` sequence cannot reproduce.
- The 16-byte `weak_this` per object shows up in an engine profile.
- A third context legitimately needs raw `this` — add it to D3's closed list; never a marker.
- A corpus program needs a call in a `MyBase.New` argument into a foreign-rooted class — then
  evaluate the argument tuple once inside `New` and hand it to both phases; never admit double
  evaluation.
