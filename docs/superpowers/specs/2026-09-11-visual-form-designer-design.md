# Visual Form Designer — Dual Target (`.blwebform` / `.blform`) — Design

**Date:** 2026-09-11
**Status:** **Draft — awaiting owner sign-off.** No code written.
**Owner feature:** page **model 4** of the JavaScript backend's staged plan — *"Visual designer
generating markup and handler stubs"* (`docs/superpowers/specs/2026-08-04-javascript-backend-design.md:50`),
carried since 2026-08-04 as a standing non-foreclosure constraint (`:53-54`) and as a tracked risk
(`:343`). The WinForms half has no prior mandate in this repo; it is an owner decision recorded here
on 2026-09-11.

## Context

The IDE can build and F5 a JavaScript web project and can build a WinForms desktop project, but both
UIs are written by hand, imperatively. This spec designs the visual designer that has been the
declared end state of the web work since August, and — by owner decision — its desktop sibling.

This design was written after a 31-agent verification pass against the worktree at `d6b57b6`, and
revised after a 9-agent citation audit (376 citations) and adversarial review. Several load-bearing
assumptions were overturned by measurement; those are recorded in *Measured facts* rather than
buried, because several are latent compiler defects that will silently miscompile generated code.

### ⛔⛔ Read this before designing anything that emits BasicLang

**This compiler's default behaviour on a miss is to degrade to `Object` silently, not to error.**
`IsNetType` (`SemanticAnalyzer.cs:2395-2400`) returns true for *any* identifier that is PascalCase,
longer than one character, and contains no underscore — under the in-code comment *"Be VERY
permissive"*. A member-access miss on such a type reaches `SetNodeType(node, memberType ??
ObjectType)` (`:7920`), which makes the strict *"Type 'X' does not have a member 'Y'"* error at
`:7925` unreachable for essentially every name a designer would generate.

The only diagnostic that can fire on that path is warning `BL6017` (`:2777-2779`, probed at `:7902`),
and it requires the .NET resolver to be armed *and* the receiver to resolve from real metadata —
which is true for none of this feature's receivers: WinForms types never resolve (see *Risks*), and
DOM externs and user classes are not metadata types. So for the designer's purposes the behaviour is
**no diagnostic at all**.

Three consequences, all measured, all of which shaped decisions below:

1. A generated property name that does not exist compiles green and fails at `csc` — or, on the web,
   at runtime in the browser.
2. A missing event handler produces a green build with a wired, dead control — **unless** the handler
   name contains an underscore, in which case the same code hard-errors. Two handler names differing
   only in punctuation get opposite build outcomes.
3. Nothing in the repo's existing green test suite disproves any of this, because the shapes that
   would expose it are untested.

## Goals

- Two document formats — `.blwebform` (web) and `.blform` (WinForms) — deliberately allowed to
  diverge, edited by one designer surface.
- A designer that produces **BasicLang source and real markup**, never `.js` and never `.cs`.
- A read-only canvas that ships first, over files that already exist, writing nothing.
- `basiclang design --check`, useful headless in CI from the slice it has a document to check.
- No silent failure: every gate in this spec exists because an equivalent gate would have passed
  vacuously.

## Non-goals for v1

Each of these is scoped out for a stated reason.

| Out of scope | Why |
|---|---|
| **Menus / toolbars / status bars** | The canonical idiom `menuStrip.Items.AddRange(New ToolStripItem() { … })` is unexpressible: `New` parses a type reference plus an optional **positional** argument list and returns, on **both** `New` paths (`Parser.cs:4298`; `Dim x As New T(…)` at `:2504-2524`). A bare-brace array literal *does* exist (`Parser.cs:4446-4461`) so `AddRange({a, b})` parses — but element typing is exact-equality with no base-class widening (`SemanticAnalyzer.cs:6056-6061`), so a menu mixing `ToolStripMenuItem` and `ToolStripSeparator` degrades to `Object[]` plus a warning. A nested non-positional tree also needs its own editor. Deferred as a unit. |
| **Modal dialogs, `DialogResult`** | Needs a form-lifetime model v1 does not have. |
| **Data binding** | Format slot reserved (`<Bind>`); v1 emits nothing and `--check` refuses a populated `<Bind>` with `BL8021`. |
| **Localization / resources** | Format slot reserved (`<Resources>`); `--check` refuses `{res:Key}` with `BL8022`. |
| **Validation** | **No slot reserved.** Validation is a per-property rule set plus a runtime, and reserving an empty slot would imply a model the designer has not chosen. Adding it later is a format addition (a new optional element), not a break, because unknown elements round-trip untouched (D9). Accepted on that basis. |
| **Multi-form navigation, MDI** | Needs a navigation model and a form-lifetime model, neither of which v1 has; both are larger than the designer itself. **But decided now:** the designer never edits `Program.bas`, and the startup form is a **project** property, not a form property. |
| **WYSIWYG rendering** | The IDE has no browser. Its only non-text document view renders HTML into a `SelectableTextBlock` and says so in its own UI (`WebViewDocumentView.axaml:74`). The canvas is a **schematic**; F5 to the system browser is the real renderer (D7). |
| **`msil` on `winforms-app`** | Drop it from `SupportedSolutionTypes` (`IProjectTemplateService.cs:386`). That pipeline stops at a `.il` file (`BuildService.cs:704-707`, early success return `:750-774`), and both existing WinForms build tests pass `SolutionTypes.DotNet`, so the arm has never been exercised. Safe against `ProjectTemplateBackendMappingTests` because `console-app`/`game-app`/`class-library` still name `msil`. Aligns with the standing scope decision that MSIL/LLVM are out of scope. |

---

## The two documents, by example

This is the artifact the feature exists to produce, and the only thing here that cannot be inferred
from existing code. Both formats are shown for the same two-control login form.

### `LoginForm.blwebform`

```xml
<WebForm Name="LoginForm" Version="1">
  <Layout Kind="Grid" Cols="120px,1fr" Rows="auto,auto" Gap="8px"/>
  <Controls>
    <Label   Id="lblUser"  Text="User"     Col="0" Row="0" TabIndex="0"/>
    <TextBox Id="txtUser"  Col="1" Row="0" TabIndex="1">
      <Bind Event="input" Handler="txtUser_Input"/>
    </TextBox>
    <Button  Id="btnLogin" Text="Sign in"  Col="1" Row="1" TabIndex="2">
      <Bind Event="click" Handler="btnLogin_Click"/>
    </Button>
  </Controls>
  <Literal><![CDATA[<p class="hint">Use your work account.</p>]]></Literal>
  <Components/>
  <Resources/>
</WebForm>
```

### `LoginForm.blform`

Same grammar, different layout vocabulary (D3) and different catalog.

```xml
<Form Name="LoginForm" Version="1" Width="400" Height="300" Text="Sign in">
  <Controls>
    <Label   Id="lblUser"  Text="User"    X="20" Y="20" Width="60"  Height="23" TabIndex="0"/>
    <TextBox Id="txtUser"  X="90" Y="20"  Width="200" Height="23" Anchor="Left,Top,Right" TabIndex="1"/>
    <Button  Id="btnLogin" Text="Sign in" X="190" Y="60" Width="100" Height="30" TabIndex="2">
      <Bind Event="Click" Handler="btnLogin_Click"/>
    </Button>
  </Controls>
  <Components/>
  <Resources/>
</Form>
```

### Shared rules

- `Id` is the control's BasicLang identifier and its DOM `id`. Unique per form. Must be a legal
  BasicLang identifier; underscores are permitted (see the naming rule in *Measured facts*).
- `<Bind Event= Handler=>` is the only binding form v1 reads. `<Bind Property= Source= Path=>` is
  reserved, refused by `--check` if populated.
- `TabIndex` is **written on every control in v1**, defaulting to document order on creation.
- `<Literal>` is the `runat="server"` inversion (D9): content passes through to the markup untouched
  and is shown read-only on the canvas. `.blform` has no `<Literal>` — there is no markup to pass
  through.
- `<Components>` and `<Resources>` are reserved and empty in v1.
- Elements and attributes the reader does not recognise **round-trip untouched** (D9).

### What lands on disk

```
LoginForm.blwebform             the truth; what the designer edits
LoginForm.bas                   your handlers, plus two designer-owned regions
bin/Debug/LoginForm.html        build output
bin/Debug/LoginForm.css         build output
bin/Debug/Site.js               JS backend, from every .bas in the project
```

---

## Decisions

### D1 — Isolation: two designer-owned **marked regions inside the user's own file**

**Decision (owner, 2026-09-11).** `LoginForm.blwebform` is the truth. On save, the designer writes
**two** delimited regions into `LoginForm.bas`. Everything outside them is the user's and is never
touched.

Two regions, not one, because the shape this matches puts user code *between* them: in
`WinFormsApp/MainForm.bas` the field declarations are at `:14-15` and `InitializeComponent` is at
`:27-47`, separated by the user's own `Public Sub New()` at `:20-22`. A single contiguous region
would have to swallow the constructor.

```basic
Public Class LoginForm

    ' <vgs:designer region="controls" form="LoginForm.blwebform" hash="3f2a1b7c">
    Protected lblUser As Element
    Protected txtUser As Element
    Protected btnLogin As Element
    ' </vgs:designer>

    Public Sub New()          ' yours
        InitializeComponent()
    End Sub

    ' <vgs:designer region="init" form="LoginForm.blwebform" hash="9d4e0a15">
    Private Sub InitializeComponent()
        lblUser = doc.getElementById("lblUser")
        txtUser = doc.getElementById("txtUser")
        btnLogin = doc.getElementById("btnLogin")
        btnLogin.addEventListener("click", AddressOf btnLogin_Click)
    End Sub
    ' </vgs:designer>

    Private Sub btnLogin_Click(e As DomEvent)   ' yours
    End Sub

End Class
```

Markers are **ordinary BasicLang comments**, so they are inert to the lexer and need no language
change. `hash` is over the region's generated content, which is what lets the designer detect a hand
edit.

**Region recovery policy** — this is the mechanism that makes writing into a user file safe, so it is
specified, not implied. Each region is classified independently (D9):

| Region state | Tier | Behaviour |
|---|---|---|
| Both markers present, balanced, hash matches | **Canon** | Designer regenerates freely on save. |
| Balanced, hash **mismatches** (hand-edited inside) | **Refused** | That form opens read-only in the designer with `BL8011`; Code view stays fully editable. The user resolves by reverting the edit or by re-importing. The designer never silently discards hand-written code. |
| Marker missing, unbalanced, or duplicated | **Refused** | `BL8012`. Never written to. |
| No markers at all, but a recognisable form shape | **n/a** | This is the import case (D12). `design --import` offers to adopt the file; until then the designer writes nothing. |

**Rejected: a `.Designer.bas` companion contributing to the same type (partial classes).** There are
no partial classes — `Partial` appears nowhere in the lexer, AST, parser or analyzer, and a second
top-level `Class Form1` is a hard error (`SemanticAnalyzer.cs:4555`).

**Rejected: a generated base class the user's class `Inherits` (`obj/gen/designer/LoginForm.g.bas`).**
It is the cleaner separation — generated code gitignored, never in a user's diff — and it was the
originating proposal. Two reasons it lost. First, it does not work on master, **measured**: a
two-file project with the base in a sibling fails with `Error at line 1, column 8: Unknown base class
'Animal'`, exit 1. Base resolution has one site, `SemanticAnalyzer.cs:4569`, and it calls
`_typeManager.GetType` directly, bypassing `ResolveTypeName` (`:2262-2291`) — the only lookup with
the scope-first fallback that lets a cross-file type keep its members. `_typeManager` is per-analyzer
(`:238`) and every analyzer is per-unit (`Compiler.cs:707`), and **every** cross-file channel —
compiled exports (`:301-325`), pending-sibling shells (`:424-441`), explicit `Import` (`:5575-5619`)
— defines symbols in a *scope* and never touches it. With a `Using` in scope the failure is worse
than an error: `:4578` fabricates a `TypeInfo` with an **empty `Members` dictionary**, so the build
goes green and every inherited member is invisible regardless of access modifier.

Second, and decisive even after that is fixed: **IntelliSense**. The user types `btnLogin.` and
expects completion. Under the marked region the declaration is in the same file and the language
server sees it natively. Under a generated base it works only if the fix routes through
`ResolveTypeName`, because the LSP never populates `GlobalScope` — it wires siblings through
`ConfigureProjectSymbols(ProjectSymbolTable)` (`LSP/DocumentManager.cs:565-578`). A `GlobalScope`-only
fix produces a green CLI build, a red squiggle under `Inherits`, and no completion on any control.
`CrossFileMemberCompletionTests` already pins that class of divergence: `InheritsContext_IncludesCrossFileClasses`
(`:120-132`) asserts the IDE *offers* a sibling class after `Inherits ` that the compiler then cannot
resolve.

**Rejected: `Module`-merge.** `Module` is the one container kind with no duplicate check —
`Visit(ModuleNode)` discards `Scope.Define`'s `false` return (`SemanticAnalyzer.cs:4514`,
`SymbolTable.cs:429`) — and same-named `Module` blocks in different files do merge into one emitted
`static class` on the C# backend (`Compiler.cs:841`; `CSharpBackend.cs:316`, `:332-340`). Rejected
anyway: `CppCodeGenerator.cs` and `JavaScriptBackend.cs` contain **zero** `ModuleName` references, so
the merge is invisible on the two backends that matter; cross-unit merge is first-wins on bare name
with a silent drop on collision (`Compiler.cs:872`); and a form is a class, not a module.

**Rejected: `#Include` inside the class body.** It would work — the preprocessor loops over lines
(`Preprocessor.cs:215`) and matches a `TrimStart`'d prefix (`:221`) with no syntactic-context check —
but it carries an order-dependent silent no-op (one `Preprocessor` instance serves every unit,
`Compiler.cs:186`→`:531`, and `ClearIncludedFiles` (`:568`) has **zero call sites repo-wide**), a
line-number shift for every subsequent diagnostic and breakpoint, and error loss (`Process()` clears
`_errors` on entry, `:204`).

### D2 — Two formats: `.blwebform` and `.blform`

**Decision (owner, 2026-09-11).** Not one document with per-target facets. Two formats, sharing the
element grammar, `Id` rules, `<Bind Event=>` shape and reserved sections shown above, deliberately
allowed to diverge in layout vocabulary and catalog — *"a web dev should know he's using web tech and
be prepared to make arrangements."*

**Rejected: one `.blform` with `<Facet>` bags.** A single document forces every layout decision to
satisfy both vocabularies at once, and the honest mapping is not symmetric (D3).

*Rationale for document-over-code:* a form is a tree of positioned controls **plus** non-visual
components, resource references, bindings and tab order. Three of those have no BasicLang statement
shape a recognizer could ever recover — which is why the region is generated output and the document
is the truth, not the reverse.

**Extension safety, measured.** Windows 8.3 short names make a 3-character glob extension sweep in
longer ones: `*.bas` really does return `Lib.basic` and `*.bli` returns `Blink.blink`. Both chosen
extensions are safe — `.blform` truncates to `BLF` and `.blwebform` to `BLW`, neither matching any
entry in `BasicLangSourceExtensions` (`ProjectFile.cs:81-82`). **Any extension beginning `bas`,
`cls`, `mod` or `bli` would be swept into the compile set by the default glob with no `<Compile>`
item and no diagnostic.** The repo guards this quirk in the sibling method (`ProjectFile.cs:453`) and
not in `GetSourceFiles`.

**`.frm` is NOT superseded** (owner decision, 2026-09-11). `docs/MULTI_FILE_SYSTEM_PLAN.md:21` reserves
`.frm` = *"Form + code-behind"*, and its sibling `.bh` row was implemented, so the table is not
fiction. The two are different concepts and both stand:

| | Who authors it | What it is |
|---|---|---|
| `.blform` / `.blwebform` | **the designer** | The artifact drag-and-drop produces. Designer-owned, machine-written, round-tripped by the writer in D9. |
| `.frm` | **the user** | Reserved for a hand-authored form file. Not in this spec's scope, not implemented, and **not obsoleted by it.** |

If `.frm` is ever built, it and `.blform` will need a stated relationship (import? a second dialect of
the recognizer? unrelated?). That is a decision for whoever specs `.frm` — this spec neither makes it
nor forecloses it.

### D3 — Layout: pixel canvas on WinForms, Grid/Flow on web

**Decision (owner, 2026-09-11).** Each target gets its native idiom, which is what D2's split is for.

- **`.blform`:** absolute `X`/`Y`/`Width`/`Height` with `Anchor`/`Dock`. The target's real idiom, and
  it matches the shipped template.
- **`.blwebform`:** `Grid` and `Flow` are primary. `Cols="120px,1fr"` is `grid-template-columns`.
  `Flow Dir="Horizontal"` is flexbox. Absolute pixels are demoted to an explicitly marked `<Canvas>`
  escape — hatched border, "fixed layout" badge, greyed when the viewport slider moves.

**Rejected: pixel-first on the web.** Absolute `left`/`top` per control is exactly what VS 2002/2003
emitted under `MS_POSITIONING="GridLayout"`, and Microsoft flipped the default to flow in VS 2005
because those pages broke on text resize, different fonts and localisation. Shipping that as the web
default makes the web output a WinForms form drawn in HTML — the precise failure that killed VB6
DHTML, which is the thing this feature reconstructs.

**A corrected premise, recorded so nobody re-derives it.** It is tempting to argue CSS-in-a-stylesheet
is *forced* because `dom-core.bli`'s `CSSStyleDeclaration` has no `position`/`left`/`top`/`zIndex`.
That reasoning is false. The typed DOM is not a capability gate: measured against the real CLI with
`--target=javascript`, `el.style.position = "absolute"`, `el.style.zIndex = "10"`, `e.type` on a
`DomEvent` and `el.querySelectorAll("p")` **all compile green, exit 0**, and emit correct browser
JavaScript — because all seven DOM extern types are PascalCase and therefore intercepted by
`IsNetType` at `SemanticAnalyzer.cs:7894` before the strict member error can fire. Its 77 members
bound what is *typed*, not what is *legal*. The stylesheet decision stands on cascade, media queries,
one place to restyle, and diffable output.

### D4 — The designer produces **BasicLang source and markup**, never `.js` and never `.cs`

```
                     ┌─ LoginForm.html   (markup, stable ids)   ── build output
  .blwebform ────────┼─ LoginForm.css    (layout)               ── build output
                     └─ regions in LoginForm.bas ──→ JS backend ──→ Site.js + Site.js.map

  .blform    ─────────  regions in LoginForm.bas ──→ C# backend ──→ .cs ──→ dotnet build ──→ .exe
```

Emitting `.js` directly would bypass the JavaScript backend and lose four things that already work:
source maps back to `.bas` lines, `JsCapabilityChecker` rejections, semantic type-checking, and — the
load-bearing one — the ability for the user's handler bodies to call the generated declarations with
real types. It also preserves the invariant that **the five existing backends stay the only code in
the repo that knows a target language**, so a sixth backend later needs no designer change.

**There is no compile-time form expansion.** The designer writes the regions; the compiler never sees
a form document as source. This is a direct consequence of D1 and it is what keeps the compiler
change surface to a skip (D11) plus an asset emitter (D6).

*(An earlier draft of this spec also specified a compile-time lowering seam at
`PreprocessModFile`/`PreprocessClassFile`. With D1 chosen, that seam would emit a duplicate class on
every form. It is deleted, deliberately, and this paragraph exists so it is not reintroduced.)*

### D5 — Where the code lives: `BasicLang/Forms/`, and the canvas in the Shell

**Decision.** The document model, the readers/writers, the recognizer and the markup emitter all live
in **`BasicLang/Forms/`**. `FormCanvasControl` lives in **`VisualGameStudio.Shell/Controls/`**.

**Rejected: the model in `VisualGameStudio.Core`.** It is the natural home for a shared model and it
is wrong here, because there is no reference edge that would let the compiler use it. The project
graph is: `ProjectSystem → {Core, BasicLang}`, `Editor → Core`, `Shell → {Core, Editor,
ProjectSystem}`, `Tests → all`. `BasicLang.csproj` declares exactly one `ProjectReference`
(`RaylibWrapper.vbproj`, `ReferenceOutputAssembly="false"`) and `VisualGameStudio.Core.csproj`
declares **none**. `basiclang build` must emit the markup on a machine with no IDE, so the emitter
has to be reachable from `BasicLang` — which means the model does too.

**Rejected: the canvas in `VisualGameStudio.Editor`**, next to `MinimapControl` whose drawing pattern
it copies. `Editor → Core` only, so it cannot see `BasicLang/Forms/`. Putting the canvas in the Shell
(`Shell → ProjectSystem → BasicLang`) needs **no new project edge**; copying a drawing pattern does
not require co-location. Verify the transitive reference actually flows before relying on it.

### D6 — Markup and CSS are **build** output, written beside `Site.js`

**Decision.** The form asset emitter runs at build time, from the document, and writes into **the
same directory the JavaScript emitter was handed** — not a directory it computes itself.

Two reasons. First, generated-on-save artifacts go stale: `basiclang build` on a clean checkout, or
any build with the designer closed, would produce a page with no markup and no styling. Second, the
two entry points disagree about the output directory — IDE `bin\Debug` (`BuildService.cs:623`), CLI
`bin\Debug\<tfm>` (`Program.cs:488`) — and reusing the emitter's own `outputDirectory` makes them
agree automatically instead of adding a third path computation to keep in sync.

**There is no asset root today; this must be built.** Greps for `asset`/`wwwroot`/`CopyToOutput` across
`JavaScriptEmitter.cs` and `BuildService.cs` return zero. Nothing in the BasicLang build pipeline
copies an arbitrary project file — a `.css`, an extra `.html`, an image — into the output directory.

`JavaScriptEmitter.Emit` writes `<script>.js` (always overwritten, `:102`), `<script>.js.map`
(`:87-91`), `index.html` **only when absent** (`:109-114`, *"⛔ NEVER overwrite the harness"*), copied
`#JsImport` targets (`:213`), and `package.json` **only when absent** (`:143-148`). Form pages are a
sixth output and **always overwrite** — every form needs a starting point on every build. They are
generated files, not a harness; `index.html` remains the only protected file, and it stays protected
because of D7. ⛔ Note `index.html` is protected from Build but **destroyed by Clean/Rebuild**.

### D7 — One `.js` per project; F5 opens the startup form's page

**Decision (owner, 2026-09-11).** Keep one module.

The backend emits a single ES module with one invocation,
`Line($"{SanitizeName(function.Name)}();")` at `JavaScriptBackend.cs:587`. Per-form bundles would
need multiple entry points the emitter does not model, plus cross-module `import` emission between
generated files that the compiler does not do. That is a **backend** change, not a designer change;
do not take it on until bundle size is a *measured* problem.

Each form gets its own page and names itself: `<body data-form="LoginForm">`, with `Main()`
dispatching on the body's `data-form` attribute.

⛔ **The dispatch must be written in two steps.** `Document.body As Element` (`dom-core.bli:22`) and
`Element.getAttribute(…) As String` (`:49`) are both declared, but the chained form **does not
type** — measured: `Dim s As String = doc.body.getAttribute("data-form")` fails with *"Cannot assign
value of type 'Object' to variable of type 'String'"*. Chained access through a declared `Property`
loses the declared type. This compiles and runs:

```basic
Dim b As Element = doc.body
Dim formName As String = b.getAttribute("data-form")
```

**F5 opens `/<StartupForm>.html`, not `/`.** The preview server maps only the bare root to
`index.html` (`WebPreviewServer.cs:162`, 404 at `:165-170`), and F5 currently hands the browser the
root URL (`MainWindowViewModel.cs:4181`, via `StartJavaScriptPreviewAsync` `:4144-4182`; both F5
`:3847` and Ctrl+F5 `:4097` route there). Generated form pages are therefore unreachable unless F5
appends the page name. Appending it is a one-line change and needs **no exception** to the
never-overwrite rule: `index.html` stays the user's hand-authored harness, served at `/` exactly as
today. The startup form is a project property (see *Non-goals*).

**Name collisions are smaller than they look under D1.** Generated declarations are **class members**
(`Protected btnLogin As Element` inside `Public Class LoginForm`), not top-level names, so two forms
each with a `btnLogin` do not collide and `BL7010` — which fires on top-level collisions with
`#JsImport`-bound names (`JsCapabilityChecker:160-214`; `#JsImport` binds names in three of its four
forms, `Preprocessor.cs:81`, `:102`, `:107`, `:131`) — is not reachable from per-control fields. The
only generated top-level name is the `Main()` dispatch helper: **one per project**, fixed spelling,
collision-checked once by `--check` with `BL8031`.

### D8 — Events: `AddHandler … AddressOf` on WinForms, `addEventListener` on web. Never mixed.

**`Handles` is forbidden**, and the reason is stronger than "it isn't parsed": it is a lexer token
only (`BasicLangLexer.cs:192`, `:540`; the parser never consumes it), and emitting one is a **hard
parse error**, not an ignored clause — `ConsumeNewlines` returns early on a non-Newline token, so a
trailing `Handles Btn.Click` becomes the first token of the method body and dies at `Parser.cs:4464`
*"Unexpected token in expression"*. There is no degrade-gracefully path, and no reader can tolerate a
file carrying one.

The two wiring channels are **disjoint and must not be mixed**:

- **WinForms:** `AddHandler ctl.Event, AddressOf handler`. Lowers to a magic-string
  `IRCall("Delegate.Combine")` (`IRBuilder.cs:1775`) that `CSharpBackend.cs:3186` rewrites to `+=`.
  There is no dedicated IR node, and **`CppCodeGenerator.cs` has no arm for it at all** — so the
  WinForms designer is C#-backend-only by construction.
- **Web:** `el.addEventListener("click", AddressOf handler)`. The declared signature is
  `Action(Of DomEvent)` (`dom-core.bli:57`).

⛔⛔ **THE ORDERING RULE — the web writer's single hardest constraint.** `AddressOf` to a `Sub`
declared **later in the file** erases its parameter types to `Action(Of Object)`, and because the DOM
declarations are genuinely typed, that is a **hard error**: *"Argument 2: cannot convert from
'Action&lt;Object&gt;' to 'Action&lt;DomEvent&gt;'"*. Measured in both a `Class` and a `Module`.
**Handlers must be emitted before the region that wires them.** Note this is the **opposite** of the
shipped WinForms template order (`InitializeComponent` first, handlers after) — WinForms tolerates
either only because its event member degrades to `Object`, so no conversion check ever fires. Both
targets are safe if the designer always emits handlers first.

With that rule obeyed, the web shape works end to end (measured — see *Measured facts → Web target*).
The emitted binding is correct: `const t2 = this.btnLogin_Click.bind(this);`.

⛔ Three neighbouring shapes are **green builds that fail at runtime or hard-error**, and the writer
must avoid all three:

| Shape | Outcome |
|---|---|
| Inside a class, a lambda calling an unqualified method — `Sub(e As DomEvent) OnClick(e)` | Compiles. Emits a **bare** `OnClick(e)` inside the arrow function while `OnClick` is a prototype method → **`ReferenceError: OnClick is not defined`** at runtime. Measured under Node. |
| Inside a class, `Me.`-qualifying it — `Sub(e As DomEvent) Me.OnClick(e)` | **Hard error**: *"Type 'F' does not have a member 'OnClick'. Available members: btn, .ctor0"* — class members are not populated at that point. |
| A qualified module call — `LoginForm.InitializeComponent()` | Compiles. JS emits flat free functions with **no module container** (`JavaScriptBackend.cs` has zero `ModuleName` references) → **`ReferenceError: LoginForm is not defined`**. Call it unqualified. |

⛔ **`AddHandler` against a DOM element compiles green and is nonsense.** `TryEventCall`
(`JavaScriptBackend.cs:2182`) matches on the IR function name alone and emits `{recv}.add(handler)`
unconditionally — so `AddHandler el.click, AddressOf H` emits `el.click.add(H)` → runtime `TypeError`.
(`click` *does* resolve on `Element` — `dom-core.bli:56` declares it and `TypeInfo.Members` is
`OrdinalIgnoreCase`, `SymbolTable.cs:111` — so this one is not an `IsNetType` degradation; it is the
event-call rewrite firing on a resolved method. Same green build, different route.) The writer must
fork by target and never emit `AddHandler` against a DOM receiver.

**A missing handler must be an error, and today nothing checks it.**
`Visit(AddHandlerStatementNode)` is a two-statement stub (`SemanticAnalyzer.cs:6172-6179`; its
`RemoveHandler` twin at `:6181-6188` is identical) with no validation of either operand.
`Overridable`/`Overrides` are never validated either — `Overrid` appears **zero times** in
`SemanticAnalyzer.cs` — and the two targets disagree: C# emits `override` and gets CS0115 from `csc`,
JS emits nothing and the method is silently redefined and never fails. The designer's guarantee
cannot rest on the accident that `btnLogin_Click` contains an underscore and so escapes `IsNetType`.
Plan **Task 15** adds the check at the `AddressOf` arm (`:7583-7586`), where `GetNodeSymbol(operand)`
returning null must become an error rather than falling back to `CreatePointerType`.

### D9 — Safety: per-property tiers, and the canvas reads the artifact

**Decision.** Three tiers, with an explicit assignment rule and an explicit unit:

| Tier | Unit | Rule | Behaviour |
|---|---|---|---|
| **Canon** | property | The attribute is known to the catalog **and** its value parses to the catalog's declared type. | Fully editable. |
| **Degraded** | property | The attribute is known but the value does not parse. | That one property-grid row is frozen with a reason string; every other row on the control stays editable; the value round-trips **unchanged** on save. |
| **Refused** | document | A condition that makes the whole file unsafe to write: a region in `BL8011`/`BL8012` state (D1), a `Handles` clause, a `With` block over a designer-managed control, or a populated reserved section (`<Bind Property=>`, `{res:Key}`). | The form opens **read-only** in the designer with the naming diagnostic; Code view stays fully editable. |

Unknown elements and attributes are neither Degraded nor Refused — they **round-trip untouched**,
which is what makes the format forward-compatible and is why Validation needs no reserved slot.

The canvas renders the model recovered **from the persisted artifact**, never from the in-memory
delta, so any writer/reader disagreement surfaces in one tick instead of as slow corruption.

Because D1 puts the designer inside a user-owned file, this is the mechanism that makes D1 safe, and
its algebra is tested directly: a no-op patch writes **nothing**; round-trip is byte-identical;
`Read∘Apply == Apply∘Read`.

Also required in slice 1 even though Ctrl+C is not wired: **serialize-subtree /
deserialize-subtree-with-rename**. Nearly free alongside the writer, very expensive to bolt on later.

### D10 — A `BL8xxx` band with a positioned, collected carrier

**Decision.** Claim `BL8001–BL8999`. Verified free by repo-wide sweep: `/BL8[0-9]{3}/` matches
nothing outside this spec. Existing bands are `BL0001-BL0030` (six of them squatted undocumented by
`BuildService`), `BL1001-BL1010`, `BL2001-BL2012`, `BL3001-BL3025`, `BL4001-BL4003`, `BL5001-BL5003`,
`BL6001-BL6026`, `BL7001-BL7012`, `BL9998`, `BL9999`.

**A fourth carrier already exists and is the model to copy.** `CppDiagnostic`
(`ProjectSystem/CppDiagnosticsParser.cs:9-17`) carries exactly the six fields needed — FilePath, Line,
Column, IsWarning, Code, Message — is **collected**, never thrown, and is already rendered by
`FormatNormalized` (`:123-130`) as `{FilePath}({Line},{Column}): {kind} {Code}: {Message}`, printed
from `Program.cs:452-457`. `NetReferenceDiagnostic`'s own doc comment (`NetReferenceResolver.cs:12-13`)
names it as the positioned alternative it deliberately is not.

Define `record DesignDiagnostic(Code, Message, FilePath, Line, Column, IsWarning)` — a separate record
because `CppDiagnostic` is C++-named and mutable — and render it in `CppDiagnostic`'s shape:
`{FilePath}({Line},{Column}): {kind} {Code}: {Message}`. ⛔ **Not** the `{label}: {Code}: {Message}`
shape at `Program.cs:541`/`:1116`: that one carries no file, line or column, because
`NetReferenceDiagnostic` has none — copying it would reintroduce the exact defect this record exists
to avoid.

⛔ Do not rely on `SemanticError.ErrorCode` to reach a human. It is assigned at three sites
(`SymbolTable.cs:790`, `:806`; `Compiler.cs:374`) and read at two (`Compiler.cs:1102`, the clone copy;
`CppProjectBuilder.cs:1619`). It **is** printed on the native route (`Program.cs:452-457`), but on the
C#/JavaScript routes this feature targets, `Program.cs:554` prints only `{label}: {Message}`, so a
`BL8xxx` placed there is invisible. `Compiler.cs:372-374` already shows the fix: put the code in
**both** the message string and the field.

⛔ Register the values in `enum ErrorCode` (`ErrorFormatter.cs:14-81`) and fix its stale band comment
(`:11-12`), which claims only BL1–5 + BL9 and omits BL0xxx, BL6xxx and BL7xxx. Those two largest
active bands were added as bare strings with no enum entry, which is why no single file answers "what
codes exist".

### D11 — Form documents ride as `<Compile>`, skipped on **both** compile routes

**Decision.** File them as `<Compile>` items and skip both extensions on both routes with a named
diagnostic.

`<Compile Include="LoginForm.blwebform" />` does resolve — the explicit-item branch is a bare
`Directory.GetFiles(dir, pattern)` with no extension filter (`ProjectFile.cs:429`), already exercised
in-tree by `<Compile Include="main.cpp" />`. But it is not inert:

1. **`CompileProjectFiles` has no allowlist, only a C-family denylist** (`Compiler.cs:360-361`,
   `BL6014`). A form document reaches `new Lexer(processedSource)` (`:547`) ungated and is parsed as
   BasicLang. `IsEntryLikeFile` (`:477-482`) excludes only `.mod/.cls/.class/.bli`, so it is even a
   candidate to hold `Main`.
2. **`CompileFile` is a separate route and gates on nothing but `.bli`** (`Compiler.cs:216`). So
   `BasicLang.exe LoginForm.blwebform --target=csharp` — the single-file invocation from CLAUDE.md's
   run table, and the debug adapter's arm at `Debugger/DebugSession.cs:190`, `:209` — sends XML to the
   lexer. The skip must cover it, emitting `BL8001` *"a form document is not a program; build the
   project"*, mirroring the `.bli` message already at `Compiler.cs:218-220`.
3. **The IDE never writes the `<Compile>` item.** Two extension deciders send unknown extensions to
   `Content` (`SolutionExplorerViewModel.cs:1164`, `ProjectService.cs:246`), and the IDE build
   compiles **only** explicit `<Compile>` items (`BasicLangProject.cs:32-35` → `BuildService.cs:480`).
   Two other writers are extension-blind and do write `Compile` (`ProjectTemplateService.cs:328`,
   `ProjectService.cs:282`).

Three further traps to carry into the plan:

- **The first explicit `<Compile>` item turns the default glob completely off** (`ProjectFile.cs:411`
  is a mutually-exclusive `if/else`). A hand-authored `.blproj` with no ItemGroup that gains one form
  item **silently loses every `.bas` file**.
- **The explicit branch is top-directory-only.** `<Compile Include="Forms\LoginForm.blwebform" />`
  works; `<Compile Include="**\*.blwebform" />` is **silently skipped**.
- **Three routes disagree.** The CLI/C# route lexes it; `CppProjectBuilder.cs:500-505` silently drops
  it; `LspProjectContext.cs:394` drops it too — which is correct, and means a form document is never
  claimed by the BasicLang language server (`TextDocumentSyncHandler.cs:38-42`) and produces no editor
  noise.

### D12 — Keep the recognizer, as an **importer**

A token-stream reader with a line/column→offset `SourceIndex`, shipped as `design --import`. It is the
one thing that gives existing hand-written forms a migration path.

**Rejected: a parser/AST walk.** The reader must work on files that do not fully parse (that is the
normal state of a file someone is mid-edit on) and must preserve exact source offsets so the writer
can splice regions without reformatting anything around them. An AST walk gives neither, and it would
couple the designer to parser changes.

⛔ **It must read the shapes that actually exist, not an idealised one.** The IDE's shipped
`winforms-app` template has **no** `InitializeComponent` — `ProjectTemplateService.cs:609-629` builds
every control inside `Public Sub New()`. The shipped `web-site` template
(`ProjectTemplateService.cs:501-540`) has no form and no class; it builds the DOM imperatively inside
`Sub Main()`. The only `InitializeComponent` in the tree is the VSIX `WinFormsApp/MainForm.bas:27`,
which carries unsubstituted `$safeprojectname$` placeholders and has zero build coverage. So the
WinForms dialect reads **constructor bodies and `InitializeComponent` alike**, and the DOM dialect
reads `createElement`/`appendChild`/`addEventListener` sequences **in any Sub**.

### D13 — Property-grid edits commit on focus-loss/Enter

**Rejected: per-keystroke with undo coalescing**, which is what most property grids do. Coalescing
needs a time- or focus-based boundary anyway, and the designer's undo has to interleave with the text
editor's `TextDocument.UndoStack` — one commit per committed value keeps that mapping one-to-one.
Otherwise typing "Sign in" is eight undo entries.

---

## Measured facts

Verified against the worktree at `d6b57b6`, then re-audited (376 citations, 23 corrected). Each row is
marked by how it was established: **[M]** measured by running something, **[R]** read from source at
the cited line, **[P]** pinned by a named test. Rows marked ⛔ are latent defects that produce a green
build and wrong behaviour.

### Language and codegen

| Fact | How | Evidence |
|---|---|---|
| ⛔⛔ **`With … End With` silently drops every `.Prop = value`.** A generated file using `With` compiles clean and produces a form with no properties set. | R | `Visit(AssignmentStatementNode)` (`IRBuilder.cs:3140`) dispatches the store over four target kinds and the if/else chain **ends at `:3356` with no final `else`**. `ImplicitWithMemberNode` matches none, so the RHS is evaluated at `:3144` and no store is emitted. Root cause: `Visit(ImplicitWithMemberNode)` (`:2852-2869`) builds an `IRFieldAccess` and never calls `EmitInstruction` — contrast the explicit path at `:3779`. `.Method()` mis-lowers into the delegate-invocation arm (`:4172`), a failure the repo's own comment at `:3791` names. |
| ⛔ **`With` over a .NET-typed receiver is *also* a hard error**, before the above applies. | R | `Visit(ImplicitWithMemberNode)` (`SemanticAnalyzer.cs:6916-6941`) is a second member-access implementation that never got the `IsNetType` hatch its twin has at `:7894`. A synthetic .NET `TypeInfo` has an empty `Members` dict (`:2333`), so `With btn` + `.Text` errors unconditionally at `:6939` on a legal `Button`. |
| **Why nobody noticed:** `With` has zero compiler-test coverage. | M | The only `End With` hit in the test project is an editor **folding** test (`BasicLangFoldingStrategyTests.cs:404-410`). No `.bas`/`.cls`/`.mod` file in the repo puts a statement inside a `With` block. |
| Bare-brace array literal `{a, b, c}` exists and is first-class. ⛔ Element typing is exact-equality with no widening — a mixed array degrades to `Object[]` plus a warning. | R | `Parser.cs:4446-4461`; `SemanticAnalyzer.cs:6040-6069` (degradation at `:6056-6061`); `IRBuilder.cs:1586-1594`. |
| `New T() { … }` is unexpressible on **both** `New` paths. | R | `Parser.cs:4298`; `:2504-2524`. |
| ⛔ `Handles` emitted into source is a **hard parse error**. | R | `BasicLangLexer.cs:192`, `:540`; never consumed; dies at `Parser.cs:4464`. |
| `AddHandler` lowers to a magic-string `IRCall("Delegate.Combine")`; **C++ has no arm**. | R | `IRBuilder.cs:1775`; `CSharpBackend.cs:3186`; `JavaScriptBackend.cs:2182`. Grep for `Delegate\.Combine` in `CppCodeGenerator.cs`: zero. |
| ⛔ `AddHandler`'s semantic analysis is a two-statement stub; so is `RemoveHandler`'s. | R | `SemanticAnalyzer.cs:6172-6179`, `:6181-6188`. |
| ⛔ `Overrides` is never validated. C# → CS0115 from `csc`; JS → silently redefined, never fails. | M | Zero `Overrid` matches in `SemanticAnalyzer.cs`. `CSharpBackend.cs:1239`; `JavaScriptBackend.cs:670-672`. |
| ⛔ `AddressOf` on a deleted handler: no diagnostic if PascalCase without `_`; hard error if it has one. | R | `SemanticAnalyzer.cs:2397`, `:7714`, `:7583-7586`. |
| ⛔ Both shipped WinForms templates name controls `btnClick`/`lblMessage` — **camelCase**. A rule requiring PascalCase control names would reject the project system's own templates. | R | `WinFormsApp/MainForm.bas:14-15`; `ProjectTemplateService.cs:609-629`. |
| The naming rule applies to **TYPE names, not identifiers**. `Dim txt_user As TextBox` is safe; a *type* or *class* name with `_` is not. | R | `SemanticAnalyzer.cs:7894` passes `objectType.Name`. Hard-error sites `:2050`, `:7940`. Rename `MainForm` to `Main_Form` and every `Me.<inherited member>` becomes a hard error. |
| `Protected` is parsed, survives IR, emits as C# `protected`, erased on JS. ⛔ `Protected Friend` silently downgrades to `private`. | R | `BasicLangLexer.cs:116`, `:464`; `Parser.cs:857-860`; `IRBuilder.cs:954`; `CSharpBackend.cs:1413`. Downgrade at `IRBuilder.cs:955` (`_ => Private`). |
| ℹ️ The C++ `Friend`-field gap is **unreachable**, not a live defect. | M | `CppCodeGenerator.cs:1065/1083/1105` bucket by exact equality with no `Friend` arm, but `IRBuilder.MapAccessModifier` (`:948-957`) collapses `Friend` to `Private` before any `IRField` is built, and both construction sites (`:816-823`, `:1218-1225`) go through it. Not filable without first changing the map. |

### IDE surface

| Fact | How | Evidence |
|---|---|---|
| `OpenFileAsync` has **no extension dispatch** — every file becomes a `CodeEditorDocumentViewModel`. | M | `MainWindowViewModel.cs:2408-2456`, construction at `:2447`. Zero `GetExtension` matches in that file. |
| `_openDocuments` is typed to the **concrete** class and read at ~70 sites. | R | `:348`. `IDocumentViewModel` exists but nothing in the Shell holds documents through it. |
| The Design∣Code toggle is feasible **without a new document type**, and the swap idiom is proven in the same view. | R | `CodeEditorDocumentView.axaml:15` (3-row grid, rows 0/1 already non-editor chrome); `IsVisible="{Binding !IsSplitView}"` at `:42`; VM template `IsSplitView`/`SplitOrientation` at `CodeEditorDocumentViewModel.cs:46-50`. |
| ⛔⛔ **`Text` and `TextDocument` are two hand-synced stores, and `SaveAsync` writes the string.** A designer editing `TextDocument` alone **saves stale text to disk** and may not mark the tab dirty. | R | `CodeEditorDocumentViewModel.cs:618`; `UpdateTextFromEditor` `:701-720`; `IsDirty` from the string at `:183`. The single most likely way to silently break an implementer. |
| ⛔ `ViewLocator` has **two** lists that must agree; a miss renders a `TextBlock` reading "Not Found: …", not an exception. | R | `ViewLocator.cs:16-44` (Build), `:61-88` (Match), `CreateDefault` `:47-58`. |
| ⛔ The `WebView*` name is **taken** by an extension-host HTML-*source* document type across Core/Shell/DockFactory/ViewLocator. Not a browser. | R | `IExtensionService.cs:310`; `DockFactory.cs:559`/`:575`/`:1001`; `WebViewDocumentView.axaml:88`. |
| A hand-built `ColorPickerPopup` exists and should be extracted. ⛔ `SettingControlKind.ColorPicker` is a declared-but-never-rendered dead arm and is **not** a precedent. | M | `VisualGameStudio.Editor/Controls/ColorPickerPopup.cs:15`; hosted at `CodeEditorControl.axaml.cs:4812-4841`. Dead arm: `SettingsViewModel.cs:246`, zero AXAML bindings. |
| The closest thing to a property grid exists, and its template is **already duplicated twice**. | R | `SettingsViewModel.cs:21-116`; `SettingsDialog.axaml:140-207`, duplicated at `:266-330`. |
| `Avalonia.Controls.DataGrid` 11.3.13 is referenced and in production use. ⛔ No `Avalonia.Headless` — no headless UI-test harness exists. | R | `VisualGameStudio.Shell.csproj:26`; `VisualGameStudio.Tests.csproj:28-29`. |
| ⛔ `MinimapControl`'s advertised bitmap caching **does not exist** (four dead fields), and its forward/inverse transform is hand-duplicated **three times**. | R | `:34-37`; transforms at `:439-447`, `:386-394`, `:835-842`. |
| ⛔ Dock re-attaches the same control instance and can leave a hidden duplicate view subscribed to the VM. | R | `CodeEditorControl.axaml.cs:762-767`, `:788`; `CodeEditorDocumentView.axaml.cs:122-130`. |
| ⛔ Three Solution Explorer tree builders with three different filters. An extension added only to the project-items path **vanishes** when the project opens as part of a solution. | R | `SolutionExplorerViewModel.cs:177-249`, `:348-352`, `:766-815`. |

### Web target

| Fact | How | Evidence |
|---|---|---|
| `dom-core.bli` is 119 lines, 7 `Extern Class` types, 77 members, one flat `Element`. | M | `getElementById:24`, `createElement:26`, `className:35`, `textContent:36`, `innerHTML:37`, `value:39`, `checked:40`, `style:46`, `classList:47`, `getAttribute:49`, `setAttribute:50`, `appendChild:52`, `click:56`, `addEventListener:57`. |
| ⛔ **It is not a capability gate.** Green, exit 0: `el.style.position`, `el.style.zIndex`, `e.type`, `el.querySelectorAll(…)` — all emit correct JS. | M | `SemanticAnalyzer.cs:7894` intercepts before `:7925`. `BliDeclarationFileTests.cs:243` documents the adjacent case. |
| A second deployed copy exists and can drift. | M | `IDE/lib/js/dom-core.bli` — byte-identical today, own git history. Load-bearing per `HANDOFF.md:76-78`. |
| ⛔ No asset root; nothing copies a `.css` or extra `.html` into the served output. | M | Zero `asset`/`wwwroot`/`CopyToOutput` hits in `JavaScriptEmitter.cs` / `BuildService.cs`. |
| Served root is `<project>\bin\Debug`; only the **root** request maps to `index.html`; header is `no-store, must-revalidate`. | R | `BuildConfiguration.cs:6` + `BuildService.cs:623`; `WebPreviewServer.cs:162`, 404 at `:165-170`, header at `:188`. F5 `:3847` / Ctrl+F5 `:4097` → `MainWindowViewModel.cs:4144-4182`, URL at `:4181`. |
| `#JsImport` binds names in three of four forms → `BL7010` on top-level collision. | R | `Preprocessor.cs:81`, `:102`, `:107`, `:131`; `JsCapabilityChecker:160-214`. |
| ⛔ `BL7007` does **not** ban `EventArgs` — it is a JavaScript-only, name-based allow-list refusal with zero effect on C#/C++. Do not model designer gating on it. | R | `JsCapabilityChecker.cs:572-589`. |
| ✅ **The web form shape WORKS END TO END — compiled *and* run**, in both a `Class` and a `Module` flavour, provided the ordering rule in D8 is obeyed. The class form emits `const t2 = this.btnLogin_Click.bind(this);` — correctly bound — and the handler's side effect lands: `lblUser.textContent` goes from `null` to `"CLICKED"`. | M | `BasicLang.exe p_final_class.bas --target=javascript` → exit 0; run under Node v22 with a DOM stub that captures the registered listener, fires it, and asserts the mutated element. |
| ⛔⛔ **`AddressOf` to a later-declared `Sub` erases parameter types to `Action(Of Object)`** → hard error against `Action(Of DomEvent)`. Measured in a `Class` and a `Module`; moving the handler above the wiring code fixes it. **This is the web writer's ordering rule (D8).** | M | *"Argument 2: cannot convert from 'Action&lt;Object&gt;' to 'Action&lt;DomEvent&gt;'"*. |
| ⛔ **Inside a class, a lambda calling an unqualified method emits a bare identifier** → `ReferenceError` at runtime, green build. `Me.`-qualifying it hard-errors instead (*"does not have a member … Available members: btn, .ctor0"*). | M | Emitted `OnClick(e);` inside `(e) => { … }` while `OnClick` is a prototype method. |
| ⛔ **A qualified module call is a runtime `ReferenceError` on JS** — `LoginForm.InitializeComponent()` compiles, but JS emits flat functions with no module container. Call unqualified. | M | `ReferenceError: LoginForm is not defined`. |
| ⛔ **Chained access through a declared `Property` loses its type.** `doc.body.getAttribute("x")` types as `Object`; the two-step form types as `String`. Hits D7's `data-form` dispatch directly. | M | *"Cannot assign value of type 'Object' to variable of type 'String'"* on the chained form; two-step compiles. |

### Project system

| Fact | How | Evidence |
|---|---|---|
| ⛔⛔ An IDE project save is **lossy**, and "add a file" is what triggers it. `SaveAsync` rebuilds the `.blproj` from a model with no field for the lost elements. | R | `ProjectSerializer.cs:243-374`; `LoadAsync` (`:16-241`) never parses them (the PropertyGroup loop is `:37-103`; the ItemGroup loop that drops `ProjectItem.Metadata` is `:106-215`). Lost: `<UseWindowsForms>`, `<UseWPF>`, `<TargetFramework>`, `<Backend>`, `<AssemblyName>`, the `<Version>` **element**, `<Authors>`, `<Description>`, global `<Optimize>`/`<DebugSymbols>`, `<NetProxy>`, and all `ProjectItem.Metadata`. |
| ⛔⛔ **Far worse than lost properties:** `LoadAsync:22` deliberately accepts a VSIX `<Project Sdk="Microsoft.NET.Sdk">` root and `SaveAsync:247` always writes `<BasicLangProject>`. One save destroys the `Sdk` attribute, both `<Import>`s, `<ProjectCapability>`, `<ProjectTypeGuids>`, the TFM, the UI flags — **and `<BasicLangCompile Include="**\*.bas"/>`, the item type the VSIX actually builds from** (`BasicLang.targets:41`). VS 2022 can no longer load the file. | R | `.../WpfApp/Project.blproj:1`. |
| Four shipped templates **do** emit `<TargetFramework>`; the WinForms and WPF ones pin the non-default `net8.0-windows`. | R | `.../WinFormsApp/Project.blproj:10-11`. |
| ⛔ The IDE build masks the strip only narrowly — gated on `OutputType == WinExe` (`BuildService.cs:1087`) and on string-sniffing the generated C# (`:1068-1071`). An `Exe`-typed WinForms project or a fully-qualified WPF program breaks inside the IDE too. | R | Acceptance must be `BasicLang.exe build` after an IDE save, never an IDE F5. |
| The non-destructive precedent exists: one IDE mutation edits the `.blproj` in place and preserves unknown elements. | R | `BlprojReferenceWriter.cs:18-42`, called from `SolutionExplorerViewModel.cs:689`. The model for a merge-on-save fix. |
| ⛔ The wizard's `TargetFramework` is collected and **silently discarded**. Every created project builds at `net8.0`. | M | `NewProjectWizardViewModel.cs:415`; zero `TargetFramework` hits in `ProjectTemplateService.cs`. |
| ⛔ `ApplicationHighDpiMode` is emitted **nowhere**; needs four coordinated places. | M | Repo-wide grep: no matches. `BuildService.cs:1104-1127`; `Program.cs:715-725`; `ProjectFile` + Load; `BasicLangProject` + `ProjectSerializer`. |
| ⛔ IDE and CLI output directories diverge: `bin\Debug` vs `bin\Debug\<tfm>`. | R | `BuildService.cs:623`; `Program.cs:488`. |
| ⛔ `ProjectService.AddFileToProjectAsync` **does not save**; model "add file" on `SolutionExplorerViewModel.ConfirmNewItemAsync` (`:1096-1118`). `SaveBeforeBuildAsync` commits pending changes on the next Build/F5 anyway, default on. | R | `ProjectService.cs:241-254`; `MainWindowViewModel.cs:3239`. |
| ℹ️ The template's compile-item emission is unescaped (`ProjectTemplateService.cs:328`) — but every value reaching it today comes from a closed switch of literal filenames (`:399-416`), so this is **prospective hardening**, not a live defect. It becomes live the moment the designer introduces a user-named item. | M | |
| ⛔ Two sibling backend dispatches still silently default to C#, one documenting itself. Neither is pinned by any test. | R | `BuildService.cs:719` (`default: // csharp`); `:887-894` (`_ => "csharp"`). Only `ProjectTemplateService.cs:265-275` was hardened. |
| ⛔ `obj/gen` is owned by the C++ builder — wiped every native build, on the C++ include path, and its sweep deletes only `*.g.cpp`/`*.g.h`. | R | `CppProjectBuilder.cs:431`, `:788-790`, `:1712-1722`, `:906-907`. Not used by this design, recorded so it is not reached for. |

### Cross-file resolution *(NOT in this plan — owner decision 2026-09-11; recorded so the findings are not lost)*

⚠ **Disambiguation, because "we don't need `Inherits`" could be misread.** `Inherits Form` — a class
inheriting a **.NET** base — is load-bearing for this feature and **works**: it is in the generated
region, and the end-to-end runtime probe above depends on it. What is broken, and what is out of
scope, is `Inherits`/`Implements` against a **user-declared type in a sibling project file**. D1 means
the designer never does that. A user who writes a multi-file project with their own base class still
hits it; that is a language defect to file separately, not designer work.

| Fact | How | Evidence |
|---|---|---|
| ⛔ Cross-file `Inherits` is broken: `Unknown base class 'Animal'`, exit 1. With a `Using` in scope: opaque member-less `TypeInfo`, green build. | M | `SemanticAnalyzer.cs:4569`, `:4582`, `:4578`. |
| ⛔ Cross-file `Implements` is broken by the **same** mechanism, with **no** opaque hatch: `Unknown interface 'IShape'`, exit 1, every backend. | M | `SemanticAnalyzer.cs:4598`, `:4601`. |
| The export filter is **not** the blocker — interfaces do export from `.bas` siblings. A duplicate declaration errors *"Symbol 'IShape' is already defined in this scope"*, which can only fire if the sibling's interface reached `GlobalScope`. | M | `Compiler.cs:826` — first disjunct is `symbol.Access == Public`, hard-set by `Symbol`'s ctor (`SymbolTable.cs:364`). Only blocker is the `IsClassFile` early-continue (`:816-823`), i.e. `.cls` units. |
| The real gaps: pass 1 of `RegisterPendingSiblingSignatures` shells only `ClassNode` (`:359`, no `InterfaceNode` arm in `:539-609`, admitted at `:606-608`), and the resolution site queries `_typeManager` alone. | R | |
| The fix shape already exists in the same file, twice — both GlobalScope-first, one already accepting `SymbolKind.Interface`. | R | `ResolveSiblingSignatureTypeName` (`:752-768`, interface at `:762`); `ResolveTypeName` (`:2286-2291`, `_projectSymbols` at `:2306`). `:4569`/`:4598` are the outliers. |
| ⛔ The `11419b7` patch is **not on this branch** (only `origin/claude/modest-gauss-0ki0b7`) and is **incomplete** — `GlobalScope`-only, so the LSP path stays broken. | M | `LSP/DocumentManager.cs:565-578`. |
| There is no **compiler-path** cross-file inheritance test. `Probe_CrossFileImplements_StillUnresolved` does not exist. LSP-path tests **do** exist. | M | `CrossFileCompileOrderTests.cs` has no `Inherits`/`Implements`. `CrossFileMemberCompletionTests.cs:120-132`, `:134-147`, `:170-197` are LSP-path. |
| ⛔ `PopulateSiblingClassMembers`' private guard is compile-order-dependent, one of six arms is unguarded, and there is a **second copy** in the LSP with a different arm set. | R | `SemanticAnalyzer.cs:448`, arms `456/468/491/501/511`, unguarded `ConstructorNode` at `:479`. Second copy `LSP/LspProjectContext.cs:704-775`, with an `EventDeclarationNode` arm (`:760`) the compiler lacks. |

### Testing and gates

| Fact | How | Evidence |
|---|---|---|
| Baseline at current master: **5826 tests, 4 failures, all pre-existing.** Fast subset ≈ 4897/2/1. | P | `HANDOFF.md:103`, `:111-114`: two `SearchSnippets_*`, `Cli_Build_CppProject_ProjectReference_Warns…`, `NonEx_variants…`. Only the last is load-dependent. |
| ⛔ The fast subset is **not** a gate for codegen work — execution tests are `[Category("Integration")]`. | P | `HANDOFF.md:116-119`. |
| ⛔ **A new template gets zero build coverage for free.** `ProjectTemplates.All` is hard-coded; `TemplateBuildSweepTests`' cases are hand-written `[TestCase]` strings. Of 24 references, six enumerate the list but **none of those builds anything**. | M | `IProjectTemplateService.cs:542-556`; `TemplateBuildSweepTests.cs:61-68`. The one enumerating guard to model on is `ProjectTemplateBackendMappingTests.cs:92`. |
| The model to copy: a source-of-truth table plus a completeness guard that **fails** on a missing row. | P | `ProjectTemplateBackendMappingTests.cs:25-33`, `:56-58` — *"add a row, never widen the default"*. |
| ✅ **The classic designer shape WORKS END TO END — compiled *and* run.** Measured 2026-09-11: the VSIX `MainForm.bas` shape (`Inherits Form`, `InitializeComponent`, `AddHandler … AddressOf`, `New Point`/`New Size`, `MessageBox.Show`) built through `BasicLang.exe build` → exit 0, a 151,552-byte `.exe` and a `.dll`; `csc` accepted the generated C#. The `.exe` then **launched, displayed its window, and the button click fired the handler**: a side-effect file was written, the label mutated to `"CLICKED"`, and a real Win32 MessageBox (class `#32770`) appeared carrying `Static text='Button clicked!'` and an `OK` button. **This is the only end-to-end runtime evidence in this document — every other row is compile-time or static.** | M | `BasicLang.exe build`, then launch + `InvokePattern.Invoke` on the button, then Win32 `EnumWindows`/`EnumChildWindows` over the process. ⚠ UIAutomation cannot see into a thread blocked in a modal loop and reported *no* dialog — the Win32 enumeration is what settled it. Do not use UIAutomation alone to gate a modal dialog. |
| ⛔ …but it has **zero build coverage**, so nothing stops it regressing. Both named tests drive `ProjectTemplateService`, which emits a **different** WinForms source. | M | Greps for `MainForm\.bas`, `InitializeComponent`, `btnClick` return no test-project hit. |
| **The generated C# is idiomatic and debuggable** — this is the emission the writer must produce. `Me.` → `this.`, `AddHandler x.Click, AddressOf h` → `x.Click += h;`, `New Point(20, 20)` → `new Point(20, 20)` as **one** statement, `[STAThread]` added automatically, and `#line` directives mapping every statement back to the `.bas`. Namespace is `GeneratedCode`. | M | Generated `WinFormsProbe.cs`, verified line by line. |
| ℹ️ **`Private` fields are sufficient under D1.** The measured build declares `private Label lblMessage;` and the subclass-visibility problem never arises, because the region and the handlers are in the **same class in the same file**. `Protected` would only be required under the rejected generated-base-class design. | M | Generated `WinFormsProbe.cs:11-12`. |
| ⛔ **Three disagreeing WinForms templates.** VSIX (`Program.bas` + `MainForm.bas`, SDK-style `.blproj`); IDE (one `Main.bas`, no `InitializeComponent`, handler `OnButtonClick`); CLI `TemplateEngine` — **none at all**. | M | `ProjectTemplateService.cs:609-629`; zero `winforms` matches in `TemplateEngine.cs`. |
| ⛔ **No optimizer-running C# helper exists.** C++ and JavaScript both have one. Every `CompileToCSharp` in the suite is a per-fixture non-optimizing copy. | M | `CppBclEndToEndTests.cs:47`; `JsTestSupport.cs:119`. **The single most likely way this feature ships a silent miscompile.** |
| ⛔ `Assert.Inconclusive`/`Assert.Ignore` = pass-by-absence, and `FindCompiler` reads a **different** binary from the one the suite deploys. Use `CliTestHarness.CliPath()`, which hard-fails. | R | `TemplateBuildSweepTests.cs:25-37`, `:71-73`, `:110`, `:113`; `CliTestHarness.cs:18-24`. |
| Exit 0 proves nothing when the deliverable is files — the JS sweep says so in its own comment. | P | `TemplateBuildSweepTests.cs:193-197`, `AssertSiteWasWritten` `:200-218`. |
| ⛔ `TestAssets\**` is `Compile`-Removed; a `.cs` fixture placed there silently does not exist. | R | `VisualGameStudio.Tests.csproj:64`. |
| ℹ️ Six verb arms in `Program.cs` return 0 unconditionally regardless of handler return type (`new`, `restore`, `add`, `remove`, `list`, `search`); `build` and `run` propagate an int. | M | `Program.cs:100-144`. |
| ℹ️ Global flag sniffing runs **before** the verb switch — `design --check foo.bas -i` would launch the REPL. | R | `Program.cs:34-86`, `-i` at `:71`. |

---

## Delivery

### Slice 0 — prerequisites

`ProjectSerializer` must stop destroying what it does not understand, **before** anything adds a file.
That, the wizard's discarded TFM, and the two silent-C# defaults are the whole of Slice 0 — three
small, independently gateable changes. Cross-file `Inherits`/`Implements` is **not** here (owner
decision 1).

**Gating (owner decision 4):** the recorded full-suite green at `f54416b` — 5826 tests, 4 baseline
failures (`HANDOFF.md:14-18`) — stands; `d6b57b6` is doc-only on top. No re-baseline. Each task gates
on the fast subset plus its own touched suites and **states which it ran and why**; the full suite is
required only where a change reaches shared compiler machinery. ⛔ The one standing caveat is
unchanged and is not a build gate: `46fd2c5`'s 27 tests "pass but have no mutation kills and no
review" (`HANDOFF.md:26-33`).

### Slice 1 — a read-only canvas over files that already exist

Zero writes, zero new file type, zero project-system change, zero build change, zero shell refactor.

The demo: open the shipped `winforms-app` and `web-site` templates and see the form — recovered by a
reader that reads the shapes those files **actually have** (D12), not an idealised
`InitializeComponent`.

`design --check` in this slice validates **recognizer input**: a `.bas` whose form shape is
recoverable, reporting Refused/Degraded findings. It gains the document formats in Slice 2.

**Slice 1 must not:** widen `_openDocuments`, add a document type, add a dock region, add a file
extension, touch `JavaScriptEmitter`, or write one byte into a user's file.

### Then

Writing, persistence, the toolbox, the property grid, the region writer and the markup emitter — with
**`.blwebform` writing first** (owner decision): it has the standing mandate, `dom-core.bli` is
machine-readable ground truth a catalog can be pinned against, and F5 puts the real renderer one
keystroke away. `.blform` follows with its own schema, its own round-trip gate, and its own
CLI-exit-0 catalog gate.

Task-level sequencing, files and gates: `docs/superpowers/plans/2026-09-11-visual-form-designer.md`.

---

## Risks

| Risk | Mitigation |
|---|---|
| **A WinForms control catalog is unfalsifiable.** `EnableNetResolution` returns early for `UseWindowsForms` (`Compiler.cs:145`), the resolver closure is TPA ∩ CoreLib's directory so `System.Windows.Forms.dll` is unreachable (`NetReferenceResolver.cs:154-179`), `CommonNetTypes` has zero WinForms names (`SemanticAnalyzer.cs:203-232`), and the LSP's `TypeRegistry` hard-codes `Microsoft.NETCore.App.Ref` (`TypeRegistry.cs:1413`). Every `Form`/`Button`/`Point` member access types `Object` with no diagnostic. | A CI gate generating **every** catalog control with **every** property set and requiring the real CLI to exit 0 is the stand-in for the type system that does not exist. Drive it from the catalog, not a hand-written `[TestCase]` list. |
| **The designer writes into a user-owned file** (D1). | D9's per-property tiers and D1's region recovery policy, plus the tested algebra: a no-op patch writes nothing, round-trip is byte-identical, `Read∘Apply == Apply∘Read`. The canvas renders the persisted artifact, never the in-memory delta. |
| **A user hand-edits inside a region, or deletes a marker.** | Hash mismatch and marker damage are both **Refused** — read-only in the designer, fully editable in Code view, with `BL8011`/`BL8012`. The designer never silently discards hand-written code. |
| **A merge conflict lands inside a region.** | Regions are contiguous comment-delimited blocks with a content hash, so a conflicted region fails its hash and goes Refused rather than being regenerated over. Regenerating from the document is always available as the resolution. |
| **`With`, `Handles` and `AddHandler`-on-DOM are three ways to emit a green build that does the wrong thing.** | Forbid all three in the writer *and* refuse each in `--check`, so a hand-edited file is caught too. File the `With` IR drop as a compiler defect independently of this feature. |
| **A silent miscompile via the non-optimizing test helper.** | Add `CompileToCSharpOptimized` alongside `JsTestSupport.CompileOptimized`, and gate every behaviour claim through the CLI. stdout is the only valid oracle. |
| **A missing switch arm silently builds C#.** Two are still live. | Fix both before adding any designer axis; extend `ProjectTemplateBackendMappingTests`; make new defaults throw. |
| **New Integration sweeps worsen contention**, which looks exactly like a test failure. | Budget it; re-run failures in isolation before investigating; only an assertion failure is evidence. |
| **The IDE has no renderer**, so the canvas can never be WYSIWYG. | Designed for as a schematic; F5 is the real renderer. Label it, do not apologise for it. |

## Owner decisions — 2026-09-11

All four questions are answered. Recorded here so the plan does not relitigate them.

1. **Cross-file `Inherits`/`Implements` is OUT of this plan.** D1 means the designer never inherits
   across a file boundary, so nothing here depends on it. The findings are kept in *Measured facts →
   Cross-file resolution* and the fix is to be filed as its own work item. ⚠ This is **not** a
   statement that `Inherits` is unnecessary — `Inherits Form` is load-bearing and works; see the
   disambiguation on that section.
2. **`.frm` stays.** `.blform`/`.blwebform` are designer-produced; `.frm` remains reserved for a
   user-authored form file. Not obsoleted. See D2.
3. **The VSIX WinForms template becomes canonical** — it is the shape the designer generates and the
   shape proven to work end to end. The IDE roster and the CLI `TemplateEngine` (which has no WinForms
   template at all) are brought into line with it, and it gains the build coverage it has never had.
4. **Trust the recorded green; gate proportionally.** No full-suite re-baseline. The recorded
   5826/4-baseline at `f54416b` stands. The full suite is required only where a change reaches shared
   compiler machinery — in this plan that is the missing-handler check and the WinForms slice; every
   other task gates on the fast subset plus its own touched suites, and says which it ran and why.
