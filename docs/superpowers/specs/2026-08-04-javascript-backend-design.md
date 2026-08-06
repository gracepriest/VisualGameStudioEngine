# BasicLang JavaScript Backend (v1) — Design

**Date:** 2026-08-04
**Status:** **Shipped** — plan 1 (`2026-08-04-javascript-backend-core.md`, tasks 1–30) and plan 2
(`2026-08-06-javascript-backend-interop-and-dom.md`, the interop escape hatch) are both complete.
`#JsImport` emits real ES imports and copies its targets to the output; `::` passes raw
JavaScript through in call position; `javascript{ … }` is the universal hatch. **Not yet built:**
the typed DOM (D5), outlined as plan 2b at the end of plan 2 — both of its design decisions are
settled. Known limitations are tabled in plan 2, the two load-bearing ones being that `::` is
**call-only** and that `#JsImport` binds **no names**.
**Owner feature:** BasicLang produces web content ("DHTML projects" for the modern web)

## Context

BasicLang has four backends (`TargetPlatform` in `BasicLang/ICodeGenerator.cs:11`),
two of which are in scope: C# and C++. Neither can produce something that runs in
a browser. The user wants BasicLang to build web content, framed explicitly as the
modern reconstruction of **VB6 DHTML projects**.

The option space was explored and decomposed into three independent sub-projects:

| | Description | Status |
|---|---|---|
| **B** | JavaScript backend — BasicLang runs in the browser as JS | **this spec** |
| **C1a** | Non-engine BasicLang → WASM via Emscripten | later spec |
| **C1b** | Games → WASM (raylib `PLATFORM_WEB`, engine from source) | later spec |

Server-side web apps (ASP.NET via the existing C# backend) and static-site
generation were explicitly deferred by the user — later, not now.

B is first because C1a/C1b are *porting* projects whose cost lives outside the
compiler (build systems, platform assumptions), while B is a self-contained
backend with an established interface and three worked examples to follow. B also
settles the browser-side model that C1b's JS glue will need regardless.

### Why VB6 DHTML is the right model and the wrong delivery

VB6 DHTML projects got the *authoring* model right — a page, code-behind, event
handlers bound to elements. They died on *delivery*: a compiled ActiveX DLL pushed
to the client, requiring the VB runtime, IE only. A JS backend inverts exactly that
failure: plain JavaScript, no runtime install, every browser, real devtools.

### Page-model staging (user decision)

The user chose to start at **model 1** and treat **model 4** as the end game:

1. **Script-only / DOM library** — a backend plus a typed DOM surface. ← **v1, this spec**
2. Code-behind on hand-written HTML, explicit wiring.
3. Code-behind with auto-wiring (`Sub btnSave_Click()` → `<button id="btnSave">`).
4. Visual designer generating markup and handler stubs.

2, 3, and 4 are all layers *on top of* 1, so nothing built here is discarded.
**This spec must not foreclose 4**: the DOM surface and emitted output shape have
to be things a generated-markup designer could later target unchanged.

## Goals

- A `TargetPlatform.JavaScript` backend producing runnable ES-module JavaScript.
- Support exactly the language features that lower **cleanly** to JS; reject the
  rest loudly at build time with dedicated diagnostics.
- A broad, typed DOM surface that drives IntelliSense through the existing LSP
  with no LSP changes.
- An escape hatch to raw JS and third-party JS libraries.
- Source maps, so devtools breakpoints and stack traces land on `.bas` lines.
- F5 in the IDE builds, serves, and opens the result in the system browser.

## Non-goals

- **Page models 2, 3, 4** — code-behind, auto-wiring, visual designer. Later specs.
- **Server-side web apps** (ASP.NET via the C# backend) — deferred by the user.
- **Static site generation** — deferred by the user.
- **WASM / Emscripten** — separate specs (C1a, C1b).
- **Bundling, minification, tree-shaking** — the output is a single module; a
  bundler is a user's choice, not the compiler's job.
- **npm / package management for JS** — `#JsImport` takes a path or URL. Package
  management is its own feature (cf. the deferred NuGet-in-IDE roadmap).
- **Feature parity with the C# backend.** Explicit non-goal. The whole design
  principle is that the JS backend is *smaller* by rule.
- **MSIL / LLVM backends** — out of scope per standing project direction.

## Decisions and alternatives considered

### D1 — The capability line: "lowers cleanly, or is rejected"

**Decision.** A feature is included only when it maps 1:1 onto a native JS
construct. Anything requiring emulation is rejected at build time by a
`JsCapabilityChecker`, modeled directly on `CppCapabilityChecker`
(`BasicLang/CppCapabilityChecker.cs`, 653 lines, `BL60xx` codes).

**Alternatives rejected.**
- *Full parity with the C# backend.* The entire added cost is the semantic-mismatch
  tail — value structs, overloading, `ByRef`, `Long`. That tail is precisely where a
  backend emits code that **runs and is quietly wrong**.
- *Minimal scripting core* (no async/LINQ/generics/iterators). Rejected because those
  four are nearly free in JS; omitting them discards the target's main advantage.

**Rationale.** Every currently-open C++ backend bug is a feature that *looked*
supported and silently did the wrong thing — `Sub Bump(ByRef x)` emitting
`void Bump(int32_t x)` and printing 1 instead of 11 (`task_0636d478`). A refusal
is discovered at build time by the compiler; a silent miscompile is discovered at
runtime by the user. The diagnostics are therefore part of the feature, not
overhead on it.

The user framed this as: *"the dev will choose this backend knowing it will have
limits — everything should be lowerable into JS, all the free and clean ones."*

### D2 — Type erasure

**Decision.** Generics, interfaces, and **all numeric types** erase at emit.
BasicLang stays statically typed; the semantic analyzer enforces the contract and
codegen discards it.

**Rationale.** This is the opposite posture from the C++ backend, which *preserves*
the type system into the output (real templates, `int32_t`, value structs) and pays
in complexity. JS cannot represent these types at all, so preserving them would mean
emulating them — which D1 forbids. Erasure is both cheaper and consistent.

`Integer`, `Single`, and `Double` all become `number` (user decision: *"integer
should match as close to JS as possible… int, double, single = number"*).

### D3 — Output shape: one ES module, no build step

**Decision.** One `.js` file per project, emitted as an ES module, plus a `.js.map`
and a generated `index.html` harness when the project supplies none.

**Alternative rejected.** *Classic `<script>`.* It loads from `file://`, which would
avoid needing a local server. Rejected: modules give proper scoping and `import`
for interop, and the server is needed anyway (see D6).

**Note.** The C# backend's generated-`.csproj`-plus-`dotnet build` step
(`BasicLang/Program.cs:684`) has no analogue here. JS needs no compiler; emit is
the whole build.

### D4 — Interop: reuse the C++ foreign-code syntax

**Decision.**
- `#JsImport "./chart.js"` — a preprocessor directive collected exactly like
  `#CppInclude` (`BasicLang/Preprocessor.cs:24`, `CppIncludes`), emitted as a real
  `import`.
- `::` prefix for raw JS identifiers — `::console.log(x)`, `::window.alert(...)`.

**Rationale.** Both spellings already exist for C++ foreign code and are already
parsed and documented. Reusing them costs nothing and teaches the user nothing new.

### D5 — The DOM is declared in BasicLang, not in a C# table

**Decision.** The typed DOM surface lives in BasicLang **declaration files**
(`.bli`) using ordinary `Class`/`Interface` syntax with no bodies, marked `Extern`:

```basiclang
Public Extern Class Element
    Public Property TextContent As String                      ' → .textContent
    Public Property ClassName As String                        ' → .className
    Public Function QuerySelector(sel As String) As Element     ' → .querySelector
    Public Sub AddEventListener(evt As String, handler As Action)
End Class
```

**Alternatives rejected.**
- *`IStdLibProvider` tables* (`BasicLang/StdLib/IStdLib.cs`). Models **free
  functions** (`Math`, `Console`). The DOM is types with methods and properties.
- *`Declare Function … Lib`* (`BasicLang/Parser.cs:1803`). Models **flat C-ABI
  imports**. Same mismatch.

**Rationale — this is the decision that makes a broad DOM affordable.** The LSP
already resolves BasicLang types. Declaring the DOM *in BasicLang* means completion,
hover, signature help, and go-to-definition work with **zero LSP changes**, and the
semantic analyzer type-checks DOM usage for free. Codegen needs exactly one new
concept: an `Extern` marker meaning *emit the raw JS name, do not mangle*.

Everything after that is **data, not code** — adding 200 DOM members becomes editing
a text file. The mechanism also generalizes: the same declaration files later
describe third-party JS libraries, and eventually the C1b WASM surface.

**Authoring (user decision).** Generate `.bli` files from TypeScript's
`lib.dom.d.ts` — an authoritative, machine-readable DOM definition — then
hand-curate each batch before it lands. The generator must filter what cannot
lower: TS overloads (rejected per D1 — pick one or drop), union types, and
optional-parameter shapes.

**Batching.** The raylib parity grind is the proven in-repo pattern for exactly this
shape of work. Same play: `dom-core` → `dom-forms` → `dom-events` →
`fetch`/`timers`/`storage` → `canvas`.

### D6 — Run story: a local static server

**Decision.** F5 → emit → start a local `HttpListener` static file server → launch
the **system** browser.

**Rationale.** Avalonia has no browser control (the same wall found when scoping the
VS Code extensions feature), so F5 must launch an external browser regardless. ES
modules require `http://`. And `.wasm` also cannot load from `file://`, so C1a and
C1b will need this identical server. Roughly 50 lines, paid once, used three times.

### D7 — Source maps in v1

**Decision.** Emit `.js.map` alongside the `.js`.

**Rationale.** The direct analogue of the `#line` work already done so `.bas`
breakpoints resolve under lldb-dap for the C++ backend. Without source maps every
breakpoint and stack trace lands in generated JS the developer never wrote.

## The capability line (normative)

### IN — maps 1:1 onto a native JS construct

| BasicLang | JavaScript | Note |
|---|---|---|
| `Sub` / `Function` | `function` | |
| `If` / `While` / `For` / `Do` / `Select Case` | same | |
| `Integer` / `Single` / `Double` | `number` | erased per D2 |
| `Boolean` | `boolean` | |
| `String` + string methods | `string` | **both immutable** — same semantics, not an approximation |
| Arrays, `For Each` | `Array`, `for…of` | |
| `List` / `Dictionary` | `Array` / `Map` | **reference semantics** — matches .NET and the C++ backend's decision |
| `Class` / `Inherits` / `Interface` | `class` / `extends` / erased | |
| Lambdas, closures | arrow functions | closures are native |
| `Try` / `Catch` / `Finally` / `Throw` | `try` / `catch` / `finally` / `throw` | **`Return` inside `Try` works** — a known C++ backend limitation that does not apply here |
| `Async` / `Await` | native `async` / `await` | free; the C++ backend only emulates this synchronously |
| Iterators / `Yield` | native `function*` / `yield` | free; the C++ backend hand-builds C++20 coroutines |
| Generics | erased | free — JS is dynamically typed |
| LINQ | `.map` / `.filter` / `.reduce` | |
| `Math`, `DateTime`, `Random`, `Regex` | `Math`, `Date`, PRNG, `RegExp` | kept by user decision — these have genuine JS equivalents |

### OUT — each requires emulation, each gets a diagnostic

| Feature | Would require | Code |
|---|---|---|
| Method overloading | name mangling — JS has no overloads | `BL7001` |
| `ByRef` parameters | boxing every argument | `BL7002` |
| `Long` / Int64 | BigInt, which contaminates all arithmetic it touches | `BL7003` |
| `Char` | no JS char type; `String` only (user decision) | `BL7004` |
| `Structure` value semantics | deep clone on every assignment and pass | `BL7005` |
| Operator overloading | JS has none | `BL7006` |
| .NET BCL types (`Stream`, `FileInfo`, `DirectoryInfo`, `Uri`, …) | no BCL in a browser | `BL7007` |

`BL7xxx` is a free range — confirmed no existing use in-tree. `BL60xx` belongs to
the C++ backend and P2a.

## Numeric model

One runtime numeric type (`number`). Consequences:

| BasicLang | JavaScript | Why |
|---|---|---|
| `\` (integer division) | `Math.trunc(a / b)` | JS `/` is always float; VB `\` has no JS operator |
| `Mod` | `%` | sign semantics match .NET exactly |
| `CInt(x)` | `Math.trunc(x)` | |
| `CDbl` / `CSng` | identity | all three are `number` |

**Documented limit (user decision).** `.NET Integer` wraps at 2³¹; JS `number` is a
double and goes imprecise past 2⁵³. No `|0` masking is emitted — output stays clean
and the divergence is documented. Code that depends on 32-bit overflow behaves
differently on this backend.

Numeric overloads cannot be distinguished at runtime — consistent with `BL7001`,
which rejects overloading outright.

## Components

| Component | Location | Note |
|---|---|---|
| `JavaScriptCodeGenerator` | `BasicLang/JavaScriptBackend.cs` | implements `ICodeGenerator`; ~35 `Visit` methods |
| `JavaScriptTypeMapper` | same file | `ITypeMapper`; near-trivial under erasure |
| `JsCapabilityChecker` | `BasicLang/JsCapabilityChecker.cs` | mirrors `CppCapabilityChecker` |
| `JavaScriptStdLib` | `BasicLang/StdLib/JavaScriptStdLib.cs` | `IStdLibProvider`; `Console`/`Math`/`String`/`Random`/`DateTime`/`Regex` |
| DOM declarations | `BasicLang/Lib/dom/*.bli` | generated from `lib.dom.d.ts`, curated per batch |
| `lib.dom.d.ts` generator | `tools/` (one-off) | not shipped in the compiler |
| Source-map emitter | `BasicLang/JavaScriptSourceMap.cs` | standard Source Map v3 |
| Static dev server | shared (see Risks) | `HttpListener`; reused by C1a/C1b |
| Registry entry | `BasicLang/BackendRegistry.cs:32` | `TargetPlatform.JavaScript`, names `"JavaScript"` and `"JS"` |
| Enum member | `BasicLang/ICodeGenerator.cs:11` | `TargetPlatform.JavaScript` |
| `.blproj` value | `BasicLang/ProjectSystem/ProjectFile.cs:46` | `<TargetBackend>JavaScript</TargetBackend>` |

### Sizing reference

| Existing backend | Lines |
|---|---|
| `CSharpBackend.cs` | 3,478 |
| `CppCodeGenerator.cs` | 4,058 (+653 checker) |
| `LLVMBackend.cs` | 2,039 |

JS sits nearer C# than C++ in difficulty — garbage collected, no header ordering,
no memory model — but pays for overload mangling absence and the erasure rules.
Expect ~2,000–3,000 lines for the generator, checker, and stdlib combined,
excluding DOM declaration content.

## Data flow

```
.bas → Preprocessor (collects #JsImport) → Lexer → Parser → SemanticAnalyzer
     → JsCapabilityChecker  ──reject──→ BL70xx diagnostics, build fails
     → IRBuilder → IROptimizer
     → JavaScriptCodeGenerator → app.js + app.js.map [+ index.html]
     → static server → system browser
```

The capability check runs **after** semantic analysis (types are needed to detect
`Long`, `Char`, and value `Structure` usage) and **before** IR construction, so a
rejected program never reaches codegen.

## Error handling

- **Capability rejections** are build errors with `BL70xx` codes, a source position,
  and a suggested alternative (e.g. `BL7002` → "return a value instead of using
  `ByRef`"). They surface in the CLI, the IDE build output, and the editor via the
  LSP, exactly as `BL60xx` already do.
- **Runtime errors** propagate as ordinary JS exceptions. BasicLang `Try/Catch`
  maps to JS `try/catch` directly, so `Throw` of a BasicLang exception type and
  a caught JS `Error` are the same mechanism.
- **`#JsImport` of a missing module** is not a compile error — the compiler emits
  the `import` verbatim and the browser reports it. The compiler does not resolve
  or fetch JS modules.

## Testing strategy

The failure mode this plan is built against is stated plainly in CLAUDE.md — *"the
green suite has hidden bugs the optimizer/CLI exposed"* — and every open C++ chip is
a **silent wrong-output** bug rather than a compile error.

| Tier | Proves | Notes |
|---|---|---|
| **Execution under Node** | the emitted JS runs and is **correct** | compile `.bas` → `.js` → run → assert stdout. Analogue of the C++ integration tests. `[Category("Integration")]`. |
| **Through the IR optimizer** | the optimized path, not just the unit helper | CLAUDE.md is explicit; the optimizer has exposed real bugs before |
| **Both entry points** | CLI **and** the IDE build path | the IDE delegates to the CLI engine; a fix verified through one can break the other |
| **Capability rejection** | every OUT feature produces its diagnostic | highest-value tier — this is what prevents inheriting the C++ silent-miscompile bug class |
| **DOM declarations** | each curated batch resolves and emits the right JS name | per-batch, mirroring the raylib parity tests |
| Golden snapshots | readable diffs | supplement only; proves nothing about behavior |

Node is located by `FindNodeExecutable()`
(`VisualGameStudio.ProjectSystem/Services/ExtensionHost.cs:768`) — a full locator
chain over PATH, Program Files, and nvm. See Risks: it is in the wrong assembly.

## Risks

| Risk | Mitigation |
|---|---|
| **Node locator is in the wrong assembly.** `FindNodeExecutable()` lives in `VisualGameStudio.ProjectSystem`; the compiler cannot reach it. | **Extract to a shared home, do not duplicate.** CLAUDE.md: *"change it once, not per-consumer."* Mirrors where `CppToolchain` probing already lives. |
| **`lib.dom.d.ts` generation over-produces.** TS overloads and unions have no BasicLang equivalent; a naive generator emits members that cannot lower. | Per-batch curation is mandatory (D5). Each batch gets resolution tests before landing. |
| **Broad DOM is a long grind.** Comparable in shape to raylib parity. | Batch it the same way. Grind, not risk — the method is proven in this repo three times over. |
| **Erased numerics diverge silently.** Overflow-dependent code behaves differently with no diagnostic. | Documented limit by user decision. Call it out in the backend's user docs, not just this spec. |
| **`Extern` is a new language concept.** Touches parser, semantic analyzer, and LSP. | Keep it strictly a marker — no new type rules, no new resolution behavior. Its only effect is on name emission. |
| **Foreclosing page model 4.** A designer must be able to target this output later. | Constraint carried through D3 and D5: markup stays real HTML, the DOM surface is data. Re-check before v1 ships. |

## Open extensions

- **Page models 2 and 3** — code-behind file pairing and `Name_Event` auto-wiring.
  Both layer over this spec's DOM surface with no backend change.
- **Page model 4** — visual designer. Needs a design surface in Avalonia built from
  nothing; the largest of the four.
- **C1a / C1b** — Emscripten. Reuses this spec's static dev server, and C1b will
  reuse the `Extern` declaration mechanism for its JS glue.
- **Source-map-aware IDE debugging** — attaching the IDE's debugger to a browser via
  the Chrome DevTools Protocol, rather than sending the developer to devtools.
- **JS package management** — resolving `#JsImport` against npm or a CDN.
