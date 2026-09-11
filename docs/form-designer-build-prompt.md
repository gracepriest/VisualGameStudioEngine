# Build prompt — dual-target visual form designer (DHTML + WinForms)

> Paste this into a fresh Claude Code session rooted at the VisualGameStudioEngine repo.
> It assumes `CLAUDE.md` and `docs/HANDOFF.md` are loaded.

---

Build a **visual form designer** in the Visual Game Studio IDE that produces both a browser page
driven by the JavaScript/DHTML backend and a .NET WinForms desktop app via the C# backend.

**Do not start coding.** Produce a spec and a plan first, in this repo's house style
(`docs/superpowers/specs/YYYY-MM-DD-<name>-design.md` and
`docs/superpowers/plans/YYYY-MM-DD-<name>.md`: numbered tasks, explicit gates, decisions with
rejected alternatives and rationale). Then stop and wait for approval before implementing.

## What has already been established

The following was verified against the source in a prior investigation. **Re-verify anything you
rely on** — file:line references drift — but do not re-litigate these conclusions without new
evidence.

### Hard blockers, verified

1. **There are no partial classes.** `Partial` appears nowhere in `Parser.cs`, `ASTNodes.cs` or
   `SemanticAnalyzer.cs`. A `.Designer.bas` companion file — the actual WinForms designer model —
   is therefore impossible. Isolation must come from **inheritance** (generated base class) or
   from a **delimited region inside one file**, not from a second file contributing to one type.
2. **Cross-file `Inherits` was broken — a patch exists on this branch, UNBUILT.**
   `Visit(ClassNode)` resolved the base through `_typeManager`, which holds only the *current*
   unit's declarations. Siblings never land there: a completed unit's exports reach `GlobalScope`
   via `ImportImplicitProjectSymbols`, a pending one's via `RegisterPendingSiblingSignatures`
   (`SemanticAnalyzer.cs:340`), which registers a class shell and fills its `Members` in a second
   pass. With no `Using` in scope the miss was a hard "Unknown base class"; with one it was worse —
   the base fell into the opaque-.NET arm, which fabricates a `TypeInfo(name, TypeKind.Class)` with
   an **empty `Members` dictionary**, so the build went green and every inherited member silently
   degraded to `Object`. The fix consults `GlobalScope` *before* that arm. **It has never been
   compiled or run** — gate it on the full suite before relying on it.
3. **Cross-file `Implements` is broken, and NOT fixed.** A different mechanism, in three places:
   `Compiler.cs:827-829` — `CollectExportedSymbols` filters exports to `Function || Subroutine ||
   Class`, so `SymbolKind.Interface` never leaves a completed unit; `RegisterPendingSiblingSignatures`
   shells only `ClassNode`, so a pending unit's interface is not registered either; and only then
   does the interface arm of `Visit(ClassNode)` query `_typeManager`. A `GlobalScope` lookup alone
   finds nothing. It has no opaque escape hatch, so it is a hard error on **every** backend.
   `Probe_CrossFileImplements_StillUnresolved` pins it. **A generated base must not `Implements` a
   designer interface declared in another file until this is fixed.**
4. **A generated base's members must be `Protected` or `Public`, never `Private`.**
   `PopulateSiblingClassMembers` guards every arm with `when member.Access != AccessModifier.Private`
   (`SemanticAnalyzer.cs:458` onward), so a private member of a sibling base is invisible to the
   subclass. This is a hard constraint on the lowerer, not a style preference.
5. **`Handles` is lexed but never parsed.** `BasicLangLexer.cs:540` registers the token;
   `Parser.cs` never consumes `TokenType.Handles`. Event wiring must use
   `AddHandler <ctl>.<Event>, AddressOf <handler>` — which works, and which the shipped template
   at `BasicLang.VisualStudio/.../WinFormsApp/MainForm.bas` already uses.
6. **There is no object-initializer or `With` syntax.** `Parser.cs:4279-4299` — `New` parses a type
   reference and an optional positional argument list, then returns. So generated init code is one
   statement per property, and the canonical menu idiom
   `menuStrip.Items.AddRange(New ToolStripItem() { ... })` is **unexpressible**. Menus are not a
   toolbox entry that falls out of the control catalog; scope them out of v1 explicitly.
7. **WinForms has no type metadata at any layer.** `CompilerOptions.EnableNetResolution` returns
   early for any project with `UseWindowsForms`/`UseWpf` (`Compiler.cs:145-146`). Even armed, the
   resolver closure is `Microsoft.NETCore.App` only (`Net/NetReferenceResolver.cs:115-124`), and
   `System.Windows.Forms.dll` lives in `Microsoft.WindowsDesktop.App`. What actually types `Form`,
   `Button`, `Point`, `Size` is a PascalCase heuristic — `char.IsUpper(name[0]) && name.Length > 1
   && !name.Contains('_')` at `SemanticAnalyzer.cs:2397`, under the comment "Be VERY permissive" —
   and `CommonNetTypes` (`SemanticAnalyzer.cs:203-232`) contains **zero** WinForms names. Member
   access on the resulting synthetic type degrades to `Object` **with no diagnostic**.
   *Consequence:* a WinForms control catalog is unfalsifiable hand-written data whose only check is
   "did `csc` accept it". A CI gate that generates every catalog control with every property set and
   requires the real CLI to exit 0 is the stand-in for the type system you do not have.
   *Corollary:* control names must never contain `_` (the heuristic turns an underscore into a hard
   semantic error on `Me.Text`). Name generated bases `<Name>Base`, never `<Name>_Base`.
8. **`ProjectSerializer` silently strips `<UseWindowsForms>`/`<UseWPF>`/`<TargetFramework>`.** An
   IDE project save therefore breaks the subsequent `BasicLang.exe build` with CS0246 on `Form`. A
   designer adds files, which triggers a save. **This must be fixed before anything touches
   WinForms.**
9. **The typed DOM is 120 lines.** `BasicLang/lib/js/dom-core.bli` — seven `Extern Class` types,
   ~77 members, one flat `Element` (no `HTMLInputElement`, no `NodeList`, no `querySelectorAll`,
   no `DomEvent.type`), and a `CSSStyleDeclaration` with twelve properties and **no
   `position`/`left`/`top`/`zIndex`**. The `lib.dom.d.ts` → `.bli` generator was never built.
   *Consequence:* emit layout as **CSS text in a generated stylesheet**, not through typed member
   access, and the `.bli` grind drops off the critical path entirely.
10. **The IDE has no property grid, no toolbox, and no browser control.** Zero hits across
   Shell/Editor/Core. Avalonia 11.3.13 + Dock.Avalonia 11.3.12.1, no WebView package. The canvas
   is a **schematic**, not WYSIWYG, on both targets — design for that honestly rather than
   apologising for it. Avalonia 11.3 base also ships no ColorPicker and no font dialog; price
   every property-grid type editor as hand-built.

### Free wins, verified

- **`<Compile Include="LoginForm.blform" />` already works.** `ProjectFile.GetSourceFiles()`'s
  explicit-item branch is a bare `Directory.GetFiles(dir, pattern)` with **no extension filter**,
  and `ProjectSerializer` round-trips `<Compile>` on read and write. Add `.blform` to
  `VisualGameStudio.Core/Constants/FileExtensions.cs` `SourceExtensions`; keep it **out of**
  `ProjectFile.BasicLangSourceExtensions` and `ModuleResolver.SupportedExtensions`.
- **One seam reaches all four build routes.** `BasicCompiler.CompileProjectFiles` — specifically
  the point where `WithJavaScriptDeclarations` injects `dom-core.bli` (`Compiler.cs:507-516`) — is
  reached by the CLI project route (`Program.cs:532`), the CLI single-file route
  (`Compiler.cs:227`), the IDE `BuildService` (`BuildService.cs:651`) and `CppProjectBuilder`.
  Hook form expansion there and both entry points get it from one change.
- **The classic designer code shape already exists, hand-written.**
  `BasicLang.VisualStudio/src/BasicLang.VisualStudio/Templates/Projects/WinFormsApp/MainForm.bas`
  is `Public Class MainForm / Inherits Form / Private btnClick As Button /
  Private Sub InitializeComponent() / AddHandler btnClick.Click, AddressOf btnClick_Click`. Match
  it exactly — it is already swept by `TemplateBuildSweepTests` and
  `BuildServicePipelineTests.Build_WinFormsTemplate_DotNet_Builds`.
- **`JavaScriptEmitter.Emit` never overwrites an existing `index.html`**
  (`JavaScriptEmitter.cs:109-114`). Generated site assets must live under an asset root that always
  overwrites, so a hand-authored harness stays protected.
- **F5 on the web target already works end to end** — build → `WebPreviewServer.Start(outputDir)`
  (loopback `HttpListener`, OS-assigned port, `Cache-Control: no-store`) → system browser
  (`MainWindowViewModel.cs:4144-4182`). The real renderer is one keystroke from the canvas.

### Owner mandate

`docs/superpowers/specs/2026-08-04-javascript-backend-design.md:43-54` stages page models 1→4 and
names model 4 **"Visual designer generating markup and handler stubs"**, with a standing
instruction at `:53-54` and `:343` that the v1 output shape **must not foreclose it**. There is no
spec, plan, code, or convention anywhere in this repo for a *WinForms* designer — that half is
inferred symmetry. Build the web half as the owner's staged feature; put the WinForms half in the
plan with a date and flag it as a proposal needing sign-off.

## The architecture to specify

**"FormDoc" — document as truth, recognizer as importer, real markup on the web.**

1. **Persistence: `.blform` XML, riding as `<Compile>`.** Structure-preserving writer (unknown
   elements/attributes/comments kept verbatim, deterministic attribute order, children in z-order)
   so hand-edits and designer saves produce minimal diffs. `LoadOptions.SetLineInfo` so every
   diagnostic carries `file(line,col)`.
   **Reserve four sections in the schema on day one, even if v1 leaves them empty** — each is an
   hour now and a format break later: `<Components>` (non-visual: Timer, ToolTip, OpenFileDialog —
   no geometry, no parent), `<Resources>` (images, later localised strings), `<Bind Property=
   Source= Path=>` alongside `<Bind Event=>`, and an explicit `TabIndex` on every control.
   *Rationale for document-over-code:* a form is a tree of positioned controls **plus** non-visual
   components, resource references, data bindings, tab order and menu trees. Three of those have no
   BasicLang statement shape a recognizer could ever recover.
2. **Layout: Grid + Flow as the primary and default vocabulary.** `Cols="120px,1fr"` is
   `grid-template-columns: 120px 1fr` on the web and `TableLayoutPanel.ColumnStyles =
   { Absolute(120), Percent(100) }` on WinForms — idiomatic on both, not an intersection. `Flow
   Dir="Horizontal"` is flexbox and `FlowLayoutPanel`. Absolute pixels are demoted to an explicitly
   marked `<Canvas>` escape: hatched border, "fixed layout" badge, greyed when the viewport slider
   moves; `Anchor`→`left/right/top/bottom` offsets apply to `<Canvas>` children only.
   **This is the decision that stops the web output being a WinForms form drawn in HTML** — the
   exact failure that killed VB6 DHTML.
3. **Control model: core + facets, never an intersection catalog.** Core properties both targets
   honestly express, plus per-target `<Facet>` bags (Literal / Expression / Property / Attribute
   kinds, plus a `<Raw>` hatch). **Hard rule:** a facet aimed at the *other* target is preserved
   byte-for-byte on save, shown greyed with a reason, and reported informationally — never dropped.
   That is the only mechanism that lets one file serve two vocabularies losslessly.
4. **Generated code: a base class in `obj/gen/forms/`; the user's class `Inherits` it.** Gitignored,
   never a `<Compile>` item, never in Solution Explorer — which beats checked-in generated files
   whose staleness depends on a CI verb nothing enforces. **Gated on fix (2) above.**
5. **Lowering target is BasicLang source, never C# and never JavaScript.** A `FormLowerer` injects
   generated `.bas` into the compile set at the `WithJavaScriptDeclarations` seam. One change,
   four build routes, and the existing backends do the rest.

   ```
                       ┌─ LoginForm.html  (markup)
           web ────────┼─ LoginForm.css   (layout)
          ╱            └─ LoginForm.g.bas ──→ JS backend ──→ Site.js + Site.js.map
   FormIR
          ╲
           WinForms ─── LoginForm.g.bas ──→ C# backend ──→ .cs ──→ dotnet build ──→ .exe
   ```

   Emitting `.js` directly would bypass the JavaScript backend and lose source maps back to `.bas`
   lines, `JsCapabilityChecker` rejections, semantic type-checking, and — the load-bearing one —
   the ability for the user's handler bodies to call the generated declarations with real types.
   The five existing backends stay the only code in the repo that knows a target language; a sixth
   backend later needs no designer change.
6. **Web output is real markup.** An HTML fragment with stable ids, an emitted `.css`, and a `.bas`
   that touches only `getElementById` and `addEventListener` (both already declared at
   `dom-core.bli:24` and `:57`). Zero `.bli` additions needed to ship. This *is* the owner's model 4.
   Generated markup **always overwrites** — every new form needs a starting point — so ship it from
   an asset root, leaving `JavaScriptEmitter.cs:109-114`'s never-overwrite guard ("⛔ NEVER overwrite
   the harness") to protect only a hand-authored `index.html` that never entered that root.
7. **The lowering is a real pass, not a textual macro.** Two properties of the mapping force this,
   and both must be in the spec:
   - **Geometry fans in on WinForms and out on the web.** `X="96" Y="80"` becomes two independent
     CSS declarations (`#btnLogin { left: 96px; top: 80px }`) but **one** statement on .NET —
     `btnLogin.Location = New Point(96, 80)`. They cannot be emitted separately: `Location` returns
     a `Point` **struct**, so `btnLogin.Location.X = 96` is CS1612 ("cannot modify the return
     value"). And BasicLang will not catch it — WinForms types come from the PascalCase heuristic
     and member access degrades to `Object` with no diagnostic, so it compiles clean until `csc`.
   - **Types erase differently per target.** One IR node, two static types: `TextBox` has no DOM
     peer in `dom-core.bli`, so the web lowering is `Dim txtUser As Element = doc.getElementById(...)`
     while WinForms is `Protected txtUser As TextBox`. This is why the control model is core +
     facets rather than an intersection catalog.
   Absolute `left`/`top` for every control is exactly what VS 2002/2003 emitted under
   `MS_POSITIONING="GridLayout"`, and Microsoft flipped the default to flow in VS 2005 because those
   pages broke on text resize, different fonts and localisation. Scope the pixel mapping to
   `<Canvas>` children.
8. **One `.js` per project, not per form** — the backend emits a single ES module with `Main();` as
   the sole invocation (`JavaScriptBackend.cs:238`, spec decision D3). Per-form bundles would mean
   multiple entry points the emitter does not model, plus cross-module `import` emission between
   generated files that the compiler does not do (`#JsImport` binds no names). That is a backend
   change, not a designer change — do not take it on. Each form gets its own page and names itself:
   `<body data-form="LoginForm">`, with `Main()` dispatching on
   `doc.body.getAttribute("data-form")` (`getAttribute` is already declared). Revisit only if bundle
   size becomes a *measured* problem.
9. **Steal `runat="server"` from Web Forms.** Literal markup passes through untouched; only marked
   elements become designer-owned objects. It is the cleanest shipped answer to "what happens when
   the user hand-writes HTML in my generated page", and it gives the canvas a principled way to show
   non-owned content as read-only instead of clobbering it.
10. **Events: a portable `FormEvent` plus a per-target thunk** — a private `(sender, EventArgs)` sub
   on WinForms, an inline lambda on DOM. This dodges the `Action` vs `Action(Of DomEvent)` arity
   wall and BL7007's ban on `EventArgs` in one move.
   **A missing handler must be an error, not a warning.** The analyzer has no override validation,
   so a renamed or deleted handler otherwise yields a green build with a wired, dead button. Either
   error by default, or have the generated base call a name that must exist so the failure lands at
   the call site.
11. **Safety: a three-tier read policy.** Canon (fully understood, freely editable) / Degraded
   (**per-property**, not per-form — one unparseable value freezes exactly one property-grid row
   with a reason and leaves the rest editable) / Refused (never written to). The canvas renders the
   model recovered **from the persisted artifact**, never from the in-memory delta, so any
   writer/reader disagreement surfaces in one tick instead of as slow corruption. Test the algebra:
   a no-op patch writes nothing; round-trip is byte-identical; `Read∘Apply == Apply∘Read`.
12. **Keep the `InitializeComponent` recognizer — as an importer, not as the format.** A token-stream
   reader over `Private Sub InitializeComponent()` using a `SourceIndex` line/column→offset mapper
   (no lexer change needed). Ship it as `design --import`. It is the one thing that gives existing
   hand-written forms — including the shipped VSIX template — a migration path, and it is the
   engine of the slice-1 demo below.
13. **Property-grid edits commit on focus-loss/Enter, not per keystroke.** Otherwise typing
    "Sign in" is eight undo entries.

## Delivery

### Slice 0 — prerequisites (~2 days + one suite run). Non-negotiable.

- Run the full suite on master and record the baseline. `docs/HANDOFF.md:15-35` says this is the
  first job: `87a6c5e` reached master **without a full-suite gate**.
- Fix `ProjectSerializer` + `BasicLangProject` to round-trip `<UseWindowsForms>`/`<UseWPF>`/
  `<TargetFramework>`.
- **Gate the cross-file `Inherits` patch that already sits on this branch.** The fix and
  `VisualGameStudio.Tests/Compiler/CrossFileInheritanceTests.cs` were authored on a machine with no
  .NET SDK and have **never been built or run**, in either direction. Before trusting either:
  revert the `SemanticAnalyzer.cs` hunk, run `CrossFileInheritanceTests`, confirm everything except
  `Probe_CrossFileImplements_StillUnresolved` goes red *for the stated reason*, restore, re-run,
  then the full suite — it is `SemanticAnalyzer`. If any test passes on the unpatched code it is
  not discriminating and needs rewriting, not accepting.
- Decide whether to fix cross-file `Implements` (blocker 3) now or defer it. It is a three-part
  change touching `Compiler.CollectExportedSymbols`, which is shared machinery — deliberately left
  out of the `Inherits` patch rather than widening it.
- Emit `<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>` in both csproj generators so
  scaling behaviour is defined rather than undefined.

### Slice 1 — a read-only canvas over the two templates that already ship (~1 week to demo)

Zero writes, zero new file type, zero project-system change, zero build change, zero shell refactor.

- `VisualGameStudio.Core/Forms/`: `FormDocument`, `FormControl`, `FormControlCatalog` (10 kinds),
  and the recognizer reader in two dialects (WinForms statement grammar, DOM statement grammar).
- `BasicLang.exe design --check <file>` — parses, reports `BL8xxx` with `file(line,col)`, exits 1
  on refusal. Headless, scriptable, useful in CI on day one, and it proves the grammar before a
  single pixel exists.
- In the Shell: a `Design | Code` segmented toggle on the existing `CodeEditorDocumentViewModel`,
  bound to the same `TextDocument`; a `FormCanvasControl : Control` overriding
  `Render(DrawingContext)` (follow the `MinimapControl.axaml.cs:417` pattern) that draws the
  recovered model schematically with selection and hit-testing.

**The demo:** open the shipped `winforms-app` template and the shipped `web-site` template — files
that already exist and already build — and see the form. Produced entirely by a reader.

**Slice 1 must not:** widen `_openDocuments`, add a document type, add a dock region, add a file
extension, touch `JavaScriptEmitter`, or write one byte into a user's file.

### Then

Writing, `.blform` persistence, the toolbox, the property grid, the lowerer, and the markup emitter
— sequenced so **the first writing, shipping, owner-facing designer is the web one** (it has an
owner mandate, `dom-core.bli` is machine-readable ground truth a catalog can be pinned against, and
F5 puts the real renderer one keystroke away). Render WinForms in the read-only slice anyway,
because the template exists and a window is the better demo.

## Explicitly out of scope for v1 — say so in the spec, with reasons

Menus / toolbars / status bars (no array-initializer syntax; a nested non-positional tree needs its
own editor). Modal dialogs and `DialogResult`. Data binding (reserve the format slot, emit nothing).
Localization and resources (reserve the slot; refuse `{res:Key}` with a named diagnostic).
Multi-form navigation and MDI — but **do** decide and document now that the designer never edits
`Program.bas` and that the startup form is a *project* property, not a form property.
Validation. Drop `"msil"` from `winforms-app`'s `SupportedSolutionTypes` — that pipeline stops at a
`.il` file.

Do require, in slice 1, that the model exposes **serialize-subtree / deserialize-subtree-with-rename**
even if Ctrl+C is not wired: nearly free alongside the writer, very expensive to bolt on later.

## House rules you must follow

- **Both entry points.** `BasicLang.exe build X.blproj` *and* the IDE build path. A fix verified
  through only one still breaks the other.
- **Validate through the CLI or an optimizer-running helper.** Every shipping route runs the IR
  optimizer; the unit-test helper does not. **stdout is the only valid oracle.**
- **A missing switch arm does not fail — it silently builds C#.** Four separate backend-dispatch
  maps have defaulted to C#. Grep for *every* map keyed on a backend or solution type; make new
  defaults throw; extend `ProjectTemplateBackendMappingTests`.
- **"Passed!" does not mean the suite passed.** A crashed host still prints a per-assembly summary;
  the abort goes to stderr. Capture both and check the total against the expected count.
- **Build contention looks exactly like a test failure.** Re-run a failing native test in isolation
  before investigating; only an assertion failure is evidence.
- **After AXAML changes, `dotnet clean` before building.**
- Never round-trip repo files through PowerShell `Get-Content`/`Set-Content`. Multi-line commit
  messages go through a file and `git commit -F`.
- `IDE/` is a hand-committed xcopy drop that goes stale. Refresh with
  `robocopy <Shell bin> IDE /E` — **never `/MIR`**. Verify against the deployed binary
  (`IDE/BasicLang.exe new --list`), never timestamps.
- Use `--filter "TestCategory!=Integration"` (~2 min) while iterating; the full suite (~39 min,
  ~2,400 tests) is the gate.

## Deliverables for this session

1. The design spec, with each decision stating its rejected alternatives and why.
2. The numbered plan with per-task gates.
3. A short list of questions for the owner — at minimum: **is a WinForms designer wanted at all**
   (nothing in the repo says so), and is Grid/Flow-first acceptable given it means the designer is a
   constraint editor with a preview rather than a pixel canvas.

Then stop.
