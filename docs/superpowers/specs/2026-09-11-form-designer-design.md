# Dual-target visual form designer — FormDoc, FormIR, and a schematic canvas

**Status:** DESIGN ONLY. Nothing here is implemented. No code was written for this spec and
**no build or test was run** — the authoring environment has no .NET SDK (`dotnet: command not
found`), the same constraint that produced the unbuilt patch discussed in §2.1.

**Provenance.** Written against `docs/form-designer-build-prompt.md` as it stands at
`185cb6c` (branch `claude/modest-gauss-0ki0b7`), which supersedes the `764379a` version by
folding in a FormIR lowering contract. Every claim that version carries was re-verified against
`origin/master` at `d6b57b6`; §2 records the six places the re-verification disagreed with it.

**Companion plan:** `docs/superpowers/plans/2026-09-11-form-designer.md`.

---

## 1. The problem

Build a visual form designer in the Visual Game Studio IDE that produces **both** a browser page
driven by the JavaScript/DHTML backend **and** a .NET WinForms desktop app via the C# backend,
from one document.

The web half has an owner mandate. `docs/superpowers/specs/2026-08-04-javascript-backend-design.md:43-54`
stages page models 1→4 and names model 4 *"Visual designer generating markup and handler stubs"*,
with a standing constraint at `:53-54` and again in the risk table at `:343` that the v1 output
shape must not foreclose it. Verified verbatim.

The WinForms half had **no** mandate in the repo — no spec, plan, code, or convention — and entered
this design as inferred symmetry. **The owner has since confirmed it: WinForms is in scope**
(§10 Q1, answered 2026-09-11). That confirmation is a product decision, not new evidence about the
code: blocker 7 still holds, so WinForms ships with **no type metadata at any layer** and the
catalog CI gate of §7 is not a nicety but the only correctness check the target has.

---

## 2. Verification pass — six corrections to the build prompt

The prompt asks that its conclusions not be re-litigated without new evidence. These six are new
evidence. Five of the six leave the prompt's *conclusion* standing and correct its *mechanism* —
which matters, because the mechanism is what the fix is shaped around and what the next session
will grep for.

### 2.1 Blocker 3's mechanism is wrong, and the real one is cheaper to fix

The prompt (and `11419b7`'s commit message) says cross-file `Implements` breaks in three places,
the first being: *"`CollectExportedSymbols` filters exports to Function/Subroutine/Class, so
`SymbolKind.Interface` never leaves a completed unit."*

It does leave. The filter at `Compiler.cs:826-832` is:

```csharp
if (symbol.Access == AST.AccessModifier.Public ||
    symbol.Kind == SymbolKind.Function ||
    symbol.Kind == SymbolKind.Subroutine ||
    symbol.Kind == SymbolKind.Class)
```

`AccessModifier.Public` is the enum's **zero value** (`ASTNodes.cs:366-373`) *and* the `Symbol`
constructor assigns it explicitly (`SymbolTable.cs:76`: `Access = AccessModifier.Public;`).
`Visit(InterfaceNode)` (`SemanticAnalyzer.cs:4712`) constructs its symbol and never touches
`Access`. So every interface satisfies the **first** arm and is exported normally.

The one case where an interface really is dropped is the `unit.IsClassFile` short-circuit above it
(`Compiler.cs:815-823`), which exports only `SymbolKind.Class` and `continue`s — i.e. an interface
declared in a `.cls`/`.class` file. A designer interface would live in a `.bas`; v1 is unaffected.

**The actual root cause is the same as blocker 2's.** Cross-unit symbols land in `GlobalScope`
(`ImportImplicitProjectSymbols`, `SemanticAnalyzer.cs:287-328`) or are shelled there by
`RegisterPendingSiblingSignatures` (`:340`). Both the base-class arm and the interface arm of
`Visit(ClassNode)` query `_typeManager`, which holds **only the current unit's** declarations.
One defect, two arms. The prompt's second mechanism is correct and stands: pass 1 of
`RegisterPendingSiblingSignatures` (`:349-368`) shells `ClassNode` only, so a *pending* sibling's
interface is never registered at all.

**Consequence — this is the part that changes the plan.** The prompt defers the `Implements` fix
because it "touches `Compiler.CollectExportedSymbols`, which is shared machinery". It does not need
to. The fix is two changes, both narrow and both symmetric with the `Inherits` patch already
written:

1. shell `InterfaceNode` in `RegisterPendingSiblingSignatures` pass 1, and
2. add a `GlobalScope` fallback to the interface arm, accepting `SymbolKind.Interface`.

`CollectExportedSymbols` is not touched. That moves cross-file `Implements` from "defer, it is
risky" to "do it in the same task, it is the same shape" — see plan Task 3.

### 2.2 The `.blform` free win contains a trap that has already bitten this repo twice

The prompt's headline free win — `<Compile Include="LoginForm.blform" />` already resolves —
is **true**: `ProjectFile.GetSourceFiles()`'s explicit-item branch is a bare
`Directory.GetFiles(dir, filePattern)` with no extension filter (`ProjectFile.cs`, verified).

But resolving is not the end of the path. `CompileProjectFiles` takes that set and, at
`Compiler.cs:383-393`, does `File.ReadAllText(file)` on **every** member and feeds it to the
preprocessor and lexer. The only guard is BL6014 (`Compiler.cs:351-381`), and it is an
**allowlist of C-family extensions** (`CFamilySourceExtensions` — `.c/.cpp/...` plus `.h/.hpp`).
A `.blform` matches nothing in it, sails through, and arrives at the BasicLang lexer as XML.

The repo has been bitten by this exact shape twice, and both scars are in the tree:

- BL6014 itself exists because C++ sources reached the lexer.
- `VisualGameStudio.Tests/LSP/LspMixedProjectTests.cs:13-40` documents the LSP version: a
  `.cpp`/`.h` taken from `GetSourceFiles()` **unfiltered** was *"lexed/parsed AS BASICLANG by the
  error-recovering parser and silently registered a junk module under its basename"* — and,
  critically, *"the pollution is invisible in diagnostics"*.

**Consequence.** The `FormLowerer` seam is not "inject additively at `WithJavaScriptDeclarations`".
It is **partition, then inject**, and it belongs in the same guard region as BL6014 — after the
`files.Count == 0` check, before the registration loop. `.blform` files come *out* of the compile
set and go *in* to the lowerer; generated `.bas` goes back in. See D5.

### 2.3 `ProjectSerializer` is destructive, not merely lossy — so the fix must be bigger

The prompt says `ProjectSerializer` "silently strips" `<UseWindowsForms>`/`<UseWPF>`/
`<TargetFramework>`. Verified, and it is worse than stripping. `Save` constructs
`new XDocument(...)` from scratch (`ProjectSerializer.cs:245-273`) and emits exactly seven
property elements: `ProjectName`, `OutputType`, `RootNamespace`, `TargetBackend`, and conditionally
`Language`, `CppStandard`, `CppToolchain`. Anything else that was in the file on load is **not
carried through the object model at all** — the first IDE save destroys it.

**Consequence.** Adding three known properties fixes today's three symptoms and leaves the defect
in place for the fourth property anyone adds. The fix is to make `ProjectSerializer` preserve
unknown `PropertyGroup` children — the same structure-preserving principle D1 demands of the
`.blform` writer, applied to the file format that already ships. Plan Task 2.

### 2.4 Blocker 10's evidence is wrong; its conclusion holds

The prompt says the IDE has "no property grid, no toolbox, and no browser control — **zero hits**
across Shell/Editor/Core".

Property grid and toolbox: confirmed absent. Browser control: **there are hits.** The Shell ships
`WebViewDocumentViewModel`, `WebViewDocumentView.axaml(.cs)`, wired through `ViewLocator.cs` and
`DockFactory.cs`, for extension-contributed panels (`vscode.window.createWebviewPanel()`).

It is not a renderer. The view is a `SelectableTextBlock` showing HTML **source**, above a banner
that says so in as many words: *"HTML source view - full rendering requires a browser component
(e.g., CefNet or WebView2)."* No WebView package is referenced.

So the design conclusion is unchanged — **the canvas is a schematic on both targets** — but the
evidence is recorded correctly here, because a future session that greps `WebView`, finds five
files, and concludes the prompt is stale would reach the wrong answer twice over.

### 2.5 Dropping `"msil"` from `winforms-app` is safe — verified, not assumed

The prompt asks for it without saying what guards it. `ProjectTemplateBackendMappingTests.cs:86-95`
(`EverySolutionType_HasAtLeastOneTemplate`) fails if any solution type is left with no template.
`"msil"` is also carried by `console-app`, `game-app`, `class-library` and `unit-test`
(`IProjectTemplateService.cs:350, 368, 432, 461`), so removing it from `winforms-app:386` cannot
empty the type. Schedulable without a coverage gate.

### 2.6 `dom-core.bli` is 119 lines, not 120

Immaterial on its own; recorded only so the number is not re-derived. The load-bearing half is
confirmed: `grep -c "position\|zIndex"` over the file returns **0**, so there is no typed route to
CSS positioning and D6's "emit CSS text" follows.

### 2.7 A latent hazard worth recording

`Symbol.Access` defaults to `Public` **twice over** — enum zero value and an explicit constructor
assignment. Any symbol whose declaration path forgets to set `Access` is silently public to every
access check and every export filter in the compiler. That is what made §2.1 true. It is out of
scope here, but it is the kind of default that produces a security-shaped bug in a language with
`Private`, and someone should look at it deliberately rather than discover it the way this spec did.

---

## 3. Blockers — re-verified against `d6b57b6`

| # | Blocker | Status | Evidence |
|---|---|---|---|
| 1 | **No partial classes.** `.Designer.bas` companion impossible; isolation must come from inheritance or a delimited region. | ✅ confirmed | `grep -c Partial` = 0 in `Parser.cs`, `ASTNodes.cs`, `SemanticAnalyzer.cs` |
| 2 | **Cross-file `Inherits` broken**; patch exists on `claude/modest-gauss-0ki0b7`, **never built or run**. | ✅ confirmed on master | `SemanticAnalyzer.cs:4569` `_typeManager.GetType`; opaque arm `:4573-4578`; hard error `:4582` |
| 3 | **Cross-file `Implements` broken** — but by a different mechanism than stated. | ⚠️ **corrected, §2.1** | `Compiler.cs:815-832`, `SemanticAnalyzer.cs:340-368, 4712` |
| 4 | **A generated base's members must be `Protected`/`Public`, never `Private`.** | ✅ confirmed | `PopulateSiblingClassMembers`, `SemanticAnalyzer.cs:448-478` — every arm guarded `when member.Access != AccessModifier.Private` |
| 5 | **`Handles` lexed, never parsed.** Wire with `AddHandler … AddressOf …`. | ✅ confirmed | token `BasicLangLexer.cs:192, 540`; **zero** `TokenType.Handles` in `Parser.cs` |
| 6 | **No object-initializer or `With`.** One statement per property; the menu idiom is unexpressible. | ✅ confirmed | `Parser.cs` `New` parses type ref + optional positional args only |
| 7 | **WinForms has no type metadata at any layer.** | ✅ confirmed | early return `Compiler.cs:145-146`; PascalCase heuristic `SemanticAnalyzer.cs:2397`; `CommonNetTypes` `:203-232` contains **zero** of `Form/Button/Label/TextBox/Point/Size` |
| 8 | **`ProjectSerializer` loses project properties.** | ⚠️ **worse, §2.3** | `ProjectSerializer.cs:245-273` — full rebuild, seven elements |
| 9 | **Typed DOM is a stub.** No `position`/`left`/`top`/`zIndex`. | ✅ confirmed (119 ln) | `BasicLang/lib/js/dom-core.bli` |
| 10 | **No property grid, no toolbox, no *rendering* browser.** | ⚠️ **evidence corrected, §2.4** | `WebViewDocumentView.axaml` is a source viewer by its own banner |

Blocker 7's corollary stands and is a hard naming rule: the heuristic excludes any identifier
containing `_`, so a control or generated type named `Foo_Base` drops out of "could be a .NET type"
and `Me.Text` becomes a hard semantic error. **Generated bases are `<Name>Base`, never
`<Name>_Base`.**

---

## 4. Free wins — re-verified

- **`<Compile Include="X.blform">` resolves.** ✅ — but see §2.2 before relying on it.
- **One seam reaches every build route.** ✅ `CompileProjectFiles` (`Compiler.cs:334`) is the
  single funnel: CLI project (`Program.cs:532`), CLI single file (`Compiler.cs:227`), IDE
  `BuildService` (`BuildService.cs:651`), and `CppProjectBuilder`. `WithJavaScriptDeclarations`
  is called from `:341`, inside it.
- **The classic designer code shape already ships, hand-written.** ✅
  `BasicLang.VisualStudio/.../WinFormsApp/MainForm.bas` is exactly
  `Public Class MainForm / Inherits Form / Private lblMessage As Label / Private Sub InitializeComponent() /
  Me.Size = New Size(400, 300) / lblMessage.Location = New Point(20, 20) / Me.Controls.Add(lblMessage) /
  AddHandler btnClick.Click, AddressOf …`. This is the recognizer's grammar target, and it is
  already swept by `TemplateBuildSweepTests` and `BuildServicePipelineTests`.
- **`JavaScriptEmitter` never overwrites `index.html`.** ✅ `JavaScriptEmitter.cs:105-115`, comment
  `⛔ NEVER overwrite the harness`, because the single-file CLI route writes next to the source.
- **F5 on the web target works end to end.** ✅ `WebPreviewServer` is registered as a singleton
  (`ServiceConfiguration.cs:96`) and injected into `MainWindowViewModel` (`:64, :385`).
- **A `Render(DrawingContext)` custom-draw precedent exists.** ✅ `MinimapControl.axaml.cs:417`,
  plus six margin controls in `VisualGameStudio.Editor/Margins/`.

---

## 5. Design

### D1 — Persistence is a `.blform` XML document, and the document is the truth

A structure-preserving writer: unknown elements and attributes kept verbatim, comments kept,
deterministic attribute order, children in z-order, `LoadOptions.SetLineInfo` so every diagnostic
carries `file(line,col)`.

**Reserve four sections in the schema on day one even though v1 writes none of them:**
`<Components>` (non-visual: Timer, ToolTip, OpenFileDialog — no geometry, no parent),
`<Resources>`, `<Bind Property= Source= Path=>` alongside `<Bind Event=>`, and an explicit
`TabIndex` on every control. Each is an hour now and a format break later.

*Rejected — code is the truth, recovered by a recognizer.* A form is a tree of positioned controls
**plus** non-visual components, resource references, data bindings, tab order and a menu tree.
Components have no parent and no geometry; bindings and tab order have no statement shape at all.
Three of the four have no BasicLang syntax a recognizer could ever recover, so a code-as-truth
design does not merely make v1 harder — it makes those features permanently unreachable.

*Rejected — a binary or JSON document.* Diffability in review is the property that makes a
generated-file design survive contact with a team, and XML is what `.blproj` already is.

### D2 — Layout is Grid + Flow first; absolute pixels are a marked escape hatch

`Cols="120px,1fr"` is `grid-template-columns: 120px 1fr` on the web and
`TableLayoutPanel.ColumnStyles = { Absolute(120), Percent(100) }` on WinForms.
`Flow Dir="Horizontal"` is flexbox and `FlowLayoutPanel`. Both are idiomatic on both targets —
this is a union of two native vocabularies, not an intersection.

Absolute pixels are demoted to an explicitly marked `<Canvas>` region: hatched border, "fixed
layout" badge, greyed when the viewport slider moves. `Anchor` → `left/right/top/bottom` offsets
applies to `<Canvas>` children **only**.

**This is the decision that stops the web output being a WinForms form drawn in HTML** — the exact
failure that killed VB6 DHTML. Absolute `left`/`top` on every control is what VS 2002/2003 emitted
under `MS_POSITIONING="GridLayout"`; Microsoft flipped the default to flow in VS 2005 because those
pages broke on text resize, on a different font, and on localisation.

*Rejected — absolute positioning everywhere, as classic VB.* Cheapest to build, simplest canvas,
and it produces a web page that is broken in the three ways above. It also makes the designer's
output worse than hand-written HTML, which is the one thing it cannot afford to be.

*Cost, and how it is paid:* a naive Grid/Flow designer is a **constraint editor with a preview**,
not a pixel canvas — dragging a button does not set `left`, it re-parents into a cell. That is the
thing a VB-familiar user notices in the first thirty seconds, and §10 Q2 put it to the owner.
**The owner chose to keep Grid/Flow persistence and buy back the gesture** — see D2a.

### D2a — Persist Grid/Flow, but the editing gesture is free pixel-drag with snap resolution

✅ **Owner decision, §10 Q2, 2026-09-11.** The two halves of the layout question get different
answers, and they are separable: **what is stored** is Grid/Flow (D2), **what the hand does** is a
free drag with snaplines, exactly as in the VS 2022 WinForms designer.

On drop, the designer **resolves** the pixel position into the nearest cell or flow-insertion point,
writes the constraint, and *shows what it wrote* — a status-bar readout and a transient highlight on
the target cell. The user drags where they like; the document records intent.

```
You drag here ────┐
                  ▼
 ┌─120px─┬───1fr────┐
 │ User  │[txtUser ]│   ◄ resolves to cell (col 1, row 0)
 └───────┴──────────┘
 status bar: "col 1, row 0 — stretch"
```

*Why this and not the alternatives.* VS 2022 itself has no single answer — it picks per target.
Its **WinForms** designer is absolute: every control carries `Location`/`Size`, and what makes it
feel precise is **snaplines** (VS 2005 onward), which snap to *alignment with neighbouring
controls*, not to a layout grid. Its **WPF/XAML** designer is Grid-first: a new Window has a `<Grid>`
root and drops write `Grid.Row`/`Grid.Column`. This decision takes the WPF answer for persistence
and the WinForms answer for feel.

*Rejected — per-target defaults, mirroring VS 2022 exactly* (new WinForms form opens as an absolute
`<Canvas>`, new web form opens Grid/Flow). Maximum familiarity per target, and it is what the
modelled IDE does. But WinForms is the familiar entry point, so most forms would start Canvas-rooted
and stay that way; enabling the web target later then reproduces the VB6-DHTML failure by default.
A design whose default output is bad has chosen wrong, however faithful the imitation.

*Rejected — absolute everywhere with Grid/Flow opt-in.* Same failure, reached sooner.

*Rejected — Grid/Flow with cell-click placement and no drag.* Cheapest, and the honest version of
the constraint editor. Rejected because the gesture is the product here: a form designer that does
not let you drag a button where you want it will not be used, whatever its output quality.

**This decision creates work that must be designed, not improvised.** The snap-resolution rule is
now load-bearing UI and needs its own treatment before Task 8:

1. **Resolution rule.** Which cell wins when a drop straddles a boundary — centroid, or the
   pointer's own position? (Pointer: it is what the user is looking at.)
2. **Creating structure by dragging.** Dropping outside every existing cell must either extend the
   grid (add a row/column) or refuse with a visible reason. Silently clamping into the nearest
   existing cell is the behaviour users will call "it moved my button somewhere else".
3. **Within-cell placement.** Once resolved, does the control stretch, or sit at an alignment
   (start/center/end)? Drop position within the cell should pick the alignment; stretch is the
   default only for a single occupant.
4. **Reversibility.** Undo restores the prior constraint, not the prior pixels — the two differ,
   and the undo entry must name the constraint (D13's commit-on-focus-loss rule applies here too).
5. **`<Canvas>` keeps the raw gesture.** Inside a `<Canvas>` region, drag sets `X`/`Y` with no
   resolution and snaplines behave exactly as WinForms users expect. That is what the escape hatch
   is for, and it is where the fidelity argument is honoured in full.

⚠ **The resolution rule is UI behaviour, not persistence.** It must never be able to produce a
document the writer would not have produced from the property grid — same document algebra,
same `Read∘Apply == Apply∘Read` obligation (D11).

### D3 — Control model is core + per-target facets, never an intersection catalog

Core properties both targets honestly express, plus per-target `<Facet>` bags (`Literal` /
`Expression` / `Property` / `Attribute` kinds, plus a `<Raw>` hatch).

**Hard rule:** a facet aimed at the *other* target is preserved byte-for-byte on save, shown greyed
with a reason, and reported informationally — **never dropped**. That is the only mechanism that
lets one document serve two vocabularies losslessly, and it is what makes a designer usable by a
team where one person has Windows and another does not.

*Rejected — intersection catalog (only what both targets support).* Yields a toolbox of about six
controls, neither target's users get what they need, and every future target narrows it further.

*Rejected — two documents, one per target.* They diverge on day two, and there is then no answer
to "which one is the form".

### D4 — Generated code is a base class under `obj/gen/forms/`; the user's class `Inherits` it

Gitignored, never a `<Compile>` item, never shown in Solution Explorer. Members are `Protected`
(blocker 4 — `Private` members of a sibling base are invisible to the subclass). Named
`<Name>Base`, never `<Name>_Base` (blocker 7 corollary).

**Gated on the blocker-2 fix**, which is unbuilt.

*Rejected — a region inside the user's file (`'#Region " Designer generated code "`).* Puts a
machine writer inside a file a human edits, which is the failure mode `.Designer.cs` was invented
to escape. Merge conflicts land in the middle of the user's class.

*Rejected — checked-in generated files.* Staleness becomes a property of whether someone ran a CI
verb, and nothing in this repo enforces one.

### D5 — The compile-set hook is **partition, then inject**, in the BL6014 guard region

Per §2.2. `.blform` files are partitioned **out** of the compile set before the registration loop
at `Compiler.cs:383`; the lowerer consumes them and injects generated `.bas` **in**. One change,
four build routes.

*Rejected — inject additively at `WithJavaScriptDeclarations` and leave `.blform` in the set.*
XML reaches the BasicLang lexer. The error-recovering parser does not throw; it registers a junk
module under the file's basename, and `LspMixedProjectTests.cs:13-40` records that this pollution
is invisible in diagnostics. A green build with a silently corrupted symbol table is the worst
available outcome.

*Rejected — teach `GetSourceFiles` to filter `.blform` out.* Then the project file no longer
records that the form belongs to the project, and Solution Explorer loses it.

### D6 — Lowering targets BasicLang source, never C# and never JavaScript

```
                    ┌─ LoginForm.html  (markup)
        web ────────┼─ LoginForm.css   (layout)
       ╱            └─ LoginForm.g.bas ──→ JS backend ──→ Site.js + Site.js.map
FormIR
       ╲
        WinForms ─── LoginForm.g.bas ──→ C# backend ──→ .cs ──→ dotnet build ──→ .exe
```

Emitting `.js` or `.cs` directly would bypass the backend and lose source maps back to `.bas`
lines, `JsCapabilityChecker` rejections, semantic type-checking, and — the load-bearing one — the
ability for the user's handler bodies to call generated declarations with real types. The five
existing backends stay the only code in the repo that knows a target language; a sixth backend
later needs no designer change.

Web layout is emitted as **CSS text in a generated stylesheet**, not through typed member access —
`dom-core.bli` has no `position`/`left`/`top`/`zIndex` (§2.6), and this drops the `.bli` grind off
the critical path entirely. Generated markup **always overwrites**, shipped from an asset root, so
`JavaScriptEmitter.cs:105-115`'s never-overwrite guard keeps protecting only a hand-authored
`index.html` that never entered that root.

### D7 — The lowering is a real pass, not a textual macro

Two properties of the mapping force this:

- **Geometry fans in on WinForms and out on the web.** `X="96" Y="80"` becomes two independent CSS
  declarations (`#btnLogin { left: 96px; top: 80px }`) but **one** statement on .NET:
  `btnLogin.Location = New Point(96, 80)`. They cannot be emitted separately — `Location` returns a
  `Point` **struct**, so `btnLogin.Location.X = 96` is CS1612. And BasicLang will not catch it:
  WinForms types come from the PascalCase heuristic and member access degrades to `Object` with no
  diagnostic (blocker 7), so it compiles clean all the way to `csc`.
- **Types erase differently per target.** One IR node, two static types. `TextBox` has no DOM peer
  in `dom-core.bli`, so the web lowering is `Dim txtUser As Element = doc.getElementById(...)`
  while WinForms is `Protected txtUser As TextBox`. This is why D3 is core + facets.

### D8 — One `.js` per project, not per form

The backend emits a single ES module with `Main();` as the sole invocation
(`JavaScriptBackend.cs:238` `EmitEntryPoint`, spec decision D3 of the JS backend design). Per-form
bundles would need multiple entry points the emitter does not model, plus cross-module `import`
emission between generated files that the compiler does not do. **That is a backend change, not a
designer change — do not take it on.** Each form gets its own page and names itself:
`<body data-form="LoginForm">`, with `Main()` dispatching on
`doc.body.getAttribute("data-form")`. Revisit only if bundle size becomes a *measured* problem.

### D9 — Steal `runat="server"` from Web Forms — **this is how page model 2 is delivered**

Literal markup passes through untouched; only marked elements become designer-owned objects. It is
the cleanest shipped answer to "what happens when the user hand-writes HTML in my generated page",
and it gives the canvas a principled way to show non-owned content as read-only rather than
clobbering it.

⚠ **Promoted by the §10 Q3 answer.** With the designer subsuming page model 2 ("code-behind on
hand-written HTML, explicit wiring"), D9 stops being a courtesy to hand-editors and *becomes* that
model's delivery mechanism. Two obligations follow, and neither is optional:

- **Marking must be opt-in, not required.** A page with **zero** designer-owned elements must still
  build and still support code-behind. Otherwise model 2 is only reachable by first adopting the
  designer, which is precisely the "nothing is discarded" promise at
  `javascript-backend-design.md:51` being broken.
- **Unowned markup is preserved byte-for-byte**, and that is a test, not an intention — it falls
  out of D1's structure-preserving writer and D11's round-trip obligation.

### D10a — One wiring mechanism: convention names the handler, `<Bind>` wires it

✅ **Forced by the §10 Q3 answer, 2026-09-11.** Page model 3 is auto-wiring —
`Sub btnSave_Click()` binding to `<button id="btnSave">` by name alone. Subsuming it into the
designer raises a conflict D10 does not resolve on its own: if a handler can be wired *both* by an
explicit `<Bind Event=>` and by name convention, then a control with both gets **wired twice and
fires twice**, and "missing handler" stops being decidable — an unmatched `btnSave_Click` could be
a typo or just an ordinary sub.

**The resolution: the `<Bind>` element is the only thing that wires.** The naming convention
survives as the *suggested name* — double-clicking a button in the designer creates
`Sub btnSave_Click()` and writes the matching `<Bind>` in the same edit. The user gets model 3's
ergonomics; the document keeps one source of truth.

*Rejected — honour both mechanisms.* Double-fire, and it destroys D10's "a missing handler is an
error" rule, which is the thing standing between a renamed handler and a green build with a dead
button.

*Rejected — convention only, no `<Bind>` element.* Renaming a control silently unwires its handler,
with no diagnostic possible because nothing ever declared the relationship. This is the classic
auto-wiring failure and the reason the explicit form exists.

*Consequence for the reader:* a hand-written `btnSave_Click` with no `<Bind>` is **not** wired, and
the designer must say so visibly rather than leaving the user to discover it at runtime — surface
it as an informational diagnostic on the control ("handler exists but is not bound"), with a
one-click fix that writes the `<Bind>`.

### D10 — Events: a portable `FormEvent` plus a per-target thunk

A private `(sender, EventArgs)` sub on WinForms, an inline lambda on DOM. This dodges the
`Action` vs `Action(Of DomEvent)` arity wall and BL7007's ban on `EventArgs` in one move. Wiring is
`AddHandler <ctl>.<Event>, AddressOf <handler>` (blocker 5).

**A missing handler is an error, not a warning.** The analyzer has no override validation, so a
renamed or deleted handler otherwise yields a green build with a wired, dead button. Either error
by default, or have the generated base call a name that must exist so the failure lands at the call
site.

### D11 — Safety: a three-tier read policy, enforced per property

**Canon** (fully understood, freely editable) / **Degraded** (**per-property**, not per-form — one
unparseable value freezes exactly one property-grid row with a reason and leaves the rest editable)
/ **Refused** (never written to).

The canvas renders the model recovered **from the persisted artifact**, never from the in-memory
delta, so any writer/reader disagreement surfaces within one tick instead of as slow corruption.

Test the algebra, not just the cases: a no-op patch writes **nothing**; round-trip is
byte-identical; `Read∘Apply == Apply∘Read`.

### D12 — Keep the `InitializeComponent` recognizer — as an importer, not as the format

A token-stream reader over `Private Sub InitializeComponent()` using a `SourceIndex`
line/column→offset mapper (no lexer change needed). Shipped as `design --import`. It is the one
thing that gives existing hand-written forms — including the shipped VSIX template — a migration
path, and it is the engine of the slice-1 demo.

### D13 — Property-grid edits commit on focus-loss/Enter, not per keystroke

Otherwise typing "Sign in" is eight undo entries.

### D14 — `ProjectSerializer` becomes structure-preserving for unknown properties

Per §2.3. Not "add three elements" — preserve what was loaded, so the fourth property someone adds
does not repeat the bug. Same principle as D1, applied to the format that already ships.

---

## 6. Diagnostics

A `BL8xxx` block, all carrying `file(line,col)` from `LoadOptions.SetLineInfo`:

| Code | Meaning |
|---|---|
| `BL8001` | `.blform` is not well-formed XML |
| `BL8002` | unknown control kind (Refused — file is never written) |
| `BL8003` | unparseable property value (Degraded — this row only) |
| `BL8004` | facet targets the other backend (informational; preserved) |
| `BL8005` | handler named by `<Bind Event=>` does not exist (**error**, D10) |
| `BL8006` | `{res:Key}` used while resources are out of scope (named refusal, §9) |
| `BL8007` | duplicate control id within a form |
| `BL8008` | a handler matching the naming convention exists but is not bound (informational, D10a) — offer the one-click `<Bind>` fix |

---

## 7. Testing

- **The document algebra** (D11): no-op patch writes nothing; byte-identical round-trip;
  `Read∘Apply == Apply∘Read`. Property-based over generated documents, not three hand-written cases.
- **Recognizer fixtures**, both dialects, against the two templates that already ship.
- **Page-model acceptance tests** (§10 Q3 — the designer subsumes models 2 and 3, so these are the
  only place those capabilities get proven):
  - *Model 2:* a page whose markup is entirely hand-written, with **zero** designer-owned elements,
    builds and supports code-behind; and a mixed page's unowned markup is **byte-identical** after a
    designer save. A near-miss here is silent data loss in someone's hand-written HTML.
  - *Model 3:* double-clicking a control produces a conventionally-named handler that is **actually
    wired** — assert the `<Bind>` was written, not merely that the sub exists. Asserting the sub
    alone would pass under the rejected convention-only design and is therefore not discriminating.
  - *D10a's exclusion:* a conventionally-named handler with **no** `<Bind>` is **not** wired, and
    raises `BL8008`. This is the assertion that stops convention wiring creeping back in as a
    "helpful" second mechanism and double-firing every handler.
- **A catalog CI gate is the stand-in for the type system WinForms does not have** (blocker 7).
  Generate every catalog control with every property set, and require the **real CLI** to exit 0.
  A hand-written WinForms catalog is otherwise unfalsifiable data whose only check is whether `csc`
  happened to accept it.
- **Both entry points, every time**: `BasicLang.exe build X.blproj` *and* the IDE `BuildService`
  path. A fix verified through only one still breaks the other.
- **Validate through the CLI or an optimizer-running helper.** Every shipping route runs the IR
  optimizer; the unit-test helper does not. **stdout is the only valid oracle.**
- **Backend-dispatch maps**: a missing switch arm does not fail, it silently builds C#. Four maps
  have already defaulted that way. Grep for *every* map keyed on a backend or solution type, make
  new defaults throw, and extend `ProjectTemplateBackendMappingTests`.
- **"Passed!" does not mean the suite passed.** A crashed host still prints a per-assembly summary
  and sends the abort to stderr. Capture both and check the total against the expected count.

---

## 8. Scope — explicitly out, with reasons

| Out of v1 | Why |
|---|---|
| Menus / toolbars / status bars | No array-initializer syntax (blocker 6) makes the canonical idiom unexpressible; a nested non-positional tree needs its own editor anyway |
| Modal dialogs and `DialogResult` | No return-value story across two targets where one has no modality |
| Data binding | Format slot reserved (D1), nothing emitted |
| Localization and resources | Slot reserved; `{res:Key}` refused with `BL8006` |
| Multi-form navigation and MDI | **But decide now:** the designer never edits `Program.bas`, and the startup form is a **project** property, not a form property |
| Validation | — |
| `"msil"` on `winforms-app` | That pipeline stops at a `.il` file. Safe to drop — §2.5 |

**Required in slice 1 even though the UI does not use it:** the model must expose
**serialize-subtree / deserialize-subtree-with-rename**. It is nearly free alongside the writer and
very expensive to bolt on later, and it is what Ctrl+C, duplicate, and templating all become.

---

## 9. Sequencing summary

The first **writing, shipping, owner-facing** designer is the **web** one: it has the owner
mandate, `dom-core.bli` is machine-readable ground truth a catalog can be pinned against, and F5
puts the real renderer one keystroke away. WinForms is rendered in the read-only slice anyway,
because the template already exists and a window is the better demo.

Full task breakdown and gates: `docs/superpowers/plans/2026-09-11-form-designer.md` — **21 tasks
across four slices, specified to the step, with a definition of done.** Slice 0 makes the ground
true (the two `SemanticAnalyzer` fixes, the `ProjectSerializer` fix, DPI); slice 1 demos a
read-only canvas with zero writes; slice 2 ships the web designer; slice 3 adds WinForms.

Three tasks carry warnings worth repeating here, because each is a place where the obvious
implementation is wrong:

- **Tasks 10 and 11 must land in the same push.** Between them a `.blform` listed as `<Compile>`
  is handed to the BasicLang lexer (§2.2).
- **Task 15 (drag) must not start before Task 8a** (the snap-resolution rule) is written down.
- **Task 19 (the WinForms catalog gate) is not a nicety** — with no type metadata at any layer, it
  is the only correctness check that target has.

---

## 10. Questions for the owner — blocking, answer before implementation

**Q1. Is a WinForms designer wanted at all?** — ✅ **ANSWERED 2026-09-11: yes, WinForms also.**

The question was asked because nothing in this repo asked for one: the web half is staged model 4
of a spec the owner approved, the WinForms half was inferred symmetry. It is now a product
decision, and three consequences follow that the rest of this spec is written to carry:

1. **The catalog CI gate (§7) is mandatory infrastructure, not a nicety.** WinForms has no type
   metadata at any layer (blocker 7): `Form`, `Button`, `Point` and `Size` are typed by a
   PascalCase heuristic, member access on the result degrades to `Object` with **no diagnostic**,
   and `CommonNetTypes` contains zero WinForms names. A hand-written catalog is unfalsifiable data
   whose only check is whether `csc` happened to accept it. Generating every catalog control with
   every property set and requiring the real CLI to exit 0 **is** the type system for this target.
2. **Task 2 (`ProjectSerializer`) moves onto the critical path.** It was already "must fix before
   anything touches WinForms"; with WinForms confirmed it is simply a prerequisite. A designer adds
   files → the IDE saves → `<UseWindowsForms>` is destroyed → the next CLI build fails CS0246 on
   `Form`.
3. **The naming rule in blocker 7's corollary is now load-bearing product code**, not a note:
   generated bases are `<Name>Base`, never `<Name>_Base`, because the heuristic excludes any
   identifier containing `_` and `Me.Text` then becomes a hard semantic error.

Still open under this answer, and **not** implied by it: whether WinForms ships *first* or second.
§9 sequences the web half first — owner mandate, machine-readable ground truth to pin a catalog
against, and F5 already reaching a real renderer — and this answer does not disturb that.

**Q2. Is Grid/Flow-first acceptable?** — ✅ **ANSWERED 2026-09-11: yes, with the gesture bought
back. Grid/Flow is persisted; editing is free pixel-drag with snap resolution.** See **D2a**.

The owner asked first what VS 2022's form designer does. The answer is that it has no single
answer — it picks per target. Its **WinForms** designer is absolute positioning with **snaplines**
(alignment guides against neighbouring controls, not a layout grid); its **WPF/XAML** designer is
Grid-first, writing `Grid.Row`/`Grid.Column`. This decision takes WPF's persistence and WinForms'
feel, rather than mirroring either wholesale.

Consequence: **the snap-resolution rule is now load-bearing UI that must be designed before Task
8** — five sub-decisions are enumerated in D2a, of which (2) "dropping outside every existing cell"
is the one that will otherwise ship as "it moved my button somewhere else".

**Q3. Page models 2 and 3 — subsumed or shipped first?** — ✅ **ANSWERED 2026-09-11: the designer
subsumes both.** The staging at `javascript-backend-design.md:43-54` runs 1→2→3→4; the project goes
1→4, with 2 and 3 arriving inside 4.

This changes nothing about the slice order, but it converts two staged features into **acceptance
obligations on the designer** — the owner's own constraint at `:51` is that "nothing built here is
discarded", and skipping 2 and 3 means their capability has to exist through the designer or not at
all:

- **Model 2 is delivered by D9** (`runat`-style marking), which is why that decision now carries a
  hard opt-in rule: a page with zero designer-owned elements must still build and still support
  code-behind.
- **Model 3 is delivered by D10a**, which had to resolve a conflict the answer creates: convention
  wiring plus explicit `<Bind>` wiring would double-fire and make "missing handler" undecidable.
  `<Bind>` wires; convention only suggests the name.

Both are testable, and §7 now carries them as named acceptance tests rather than prose.

**Q4. Cross-file `Implements` — fix it now?** — ✅ **ANSWERED 2026-09-11: fix it in slice 0.**

§2.1 found it materially cheaper and lower-risk than the build prompt assumed: two narrow changes
symmetric with the `Inherits` patch, and `Compiler.CollectExportedSymbols` is **not** touched.
Plan Task 3 is un-gated.

The accepted cost: slice 0 now carries **two** `SemanticAnalyzer` changes, one of which has never
been compiled. Gate them **separately** — land and full-suite the `Inherits` patch (Task 1) before
the `Implements` change (Task 3) goes in, so a red suite has one candidate cause rather than two.
Two full-suite runs, ~39 minutes each.

**Q5. Who runs the first gate?** The blocker-2 patch on `claude/modest-gauss-0ki0b7` has never been
compiled or executed, and this session could not change that — there is no .NET SDK here either.
Everything in slice 0 needs a machine with the SDK and roughly 39 minutes for the full suite.
