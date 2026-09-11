# Dual-target visual form designer — Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents
> available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`)
> syntax for tracking.

**Goal:** A visual form designer in the Visual Game Studio IDE that produces both a browser page
(JavaScript/DHTML backend) and a .NET WinForms desktop app (C# backend) from one `.blform`
document — delivered so that a read-only canvas over the two templates that already ship is
demonstrable in about a week, before anything writes a byte into a user's file.

**Architecture:** The document is the truth (`.blform` XML, structure-preserving writer). A
`FormLowerer` **partitions** `.blform` out of the compile set inside `CompileProjectFiles` and
**injects** generated BasicLang source back in — one seam, four build routes. Lowering targets
BasicLang, never C# or JavaScript, so the five existing backends stay the only code that knows a
target language. Generated code is a `Protected`-membered base class in `obj/gen/forms/` that the
user's class `Inherits`.

**Tech Stack:** C# (compiler + IDE), Avalonia 11.3.13 custom-draw (`Render(DrawingContext)`),
NUnit, XML (`System.Xml.Linq` with `LoadOptions.SetLineInfo`).

**Design spec:** `docs/superpowers/specs/2026-09-11-form-designer-design.md` — read it first,
especially §2 (six corrections to the build prompt, two of which change tasks in this plan).

---

## ⛔ STATUS: NOT STARTED. Nothing below has been built or run.

**No task in this plan has been implemented, and no gate in it has been executed.** The authoring
environment for both this plan and the spec has **no .NET SDK** (`dotnet: command not found`) —
the same constraint that produced the unbuilt patch in Task 1. Every "Gate" line is an instruction,
not a record.

**Blocking on owner sign-off before Task 1.** Spec §10 asks five questions.

- **Q1 — ✅ ANSWERED 2026-09-11: yes, WinForms also.** The dual-target scope is confirmed. Slice 2+
  item 6 is un-gated, and its catalog CI gate is now mandatory infrastructure rather than a
  proposal — it is the only correctness check that target has (spec §10 Q1). Task 2 is on the
  critical path for the same reason.
- **Q2 — still open, and it forks the design.** Grid/Flow-first (spec D2) makes the designer a
  constraint editor with a preview; absolute positioning makes it a pixel canvas. Task 5's catalog
  and Task 8's canvas interaction model both depend on the answer.
- **Q4 — ✅ ANSWERED 2026-09-11: fix cross-file `Implements` in slice 0.** Task 3 is un-gated.
  Gate Tasks 1 and 3 **separately** — slice 0 now carries two `SemanticAnalyzer` changes, one of
  them never compiled, and a shared gate would leave a red suite with two candidate causes.

Q1's answer does **not** re-order the slices: the web half still ships first (spec §9).

---

## Slice 0 — prerequisites. Non-negotiable. (~2 days + one full suite run)

Nothing in slice 1 depends on Tasks 2–4, but everything after slice 1 does, and Task 1 gates the
whole generated-base-class design. Do slice 0 first anyway: it is the smallest set of changes that
makes the ground under this feature true.

### Task 1: Gate the unbuilt cross-file `Inherits` patch

`11419b7` on branch `claude/modest-gauss-0ki0b7` carries a +22-line insert-only change to
`SemanticAnalyzer.cs` and a 250-line `CrossFileInheritanceTests.cs`. Its own commit message says
it plainly: **no .NET SDK was available; it has never been built, and not one of its tests has ever
been executed, in either direction.**

**Files:**
- Verify: `BasicLang/SemanticAnalyzer.cs:4567-4589` (the inserted `GlobalScope` fallback)
- Verify: `VisualGameStudio.Tests/Compiler/CrossFileInheritanceTests.cs`

- [ ] **Step 1: Build it.** This has never been demonstrated to compile.
- [ ] **Step 2: Prove the tests discriminate — revert the `SemanticAnalyzer.cs` hunk and run them.**
      Everything except `Probe_CrossFileImplements_StillUnresolved` must go **red, for the stated
      reason**. Read each failure message; do not accept a red that fails for an unrelated reason.
      ⚠ **Any test that passes on unpatched code is not discriminating and needs rewriting, not
      accepting.** `WithUsingDirective_PrefersSiblingBase_OverOpaqueNetType` is the one that matters
      most — a `Success`-only assertion would pass before *and* after, because the old behaviour was
      a **green build** with an empty `Members` dictionary.
- [ ] **Step 3:** Restore the hunk, re-run, confirm green.
- [ ] **Step 4:** Confirm `GenuineNetBase_StillResolvesOpaquely` — `Inherits Form` must keep the
      opaque path. The fix **narrows** that arm; it must not close it.

**Gate: FULL SUITE.** This is `SemanticAnalyzer.cs`. Expected baseline per `docs/HANDOFF.md`:
5826 tests, 4 known failures. Capture stdout **and stderr** and check the total against the
expected count — a crashed host still prints a per-assembly "Passed!".

### Task 2: Make `ProjectSerializer` structure-preserving

Per spec §2.3, this is **not** "add three elements". `Save` rebuilds the document from scratch
(`ProjectSerializer.cs:245-273`) and emits seven property elements, so an IDE save **destroys**
`<UseWindowsForms>`, `<UseWPF>` and `<TargetFramework>`. A designer adds files, which triggers a
save, which breaks the next `BasicLang.exe build` with CS0246 on `Form`.

**Files:**
- Modify: `VisualGameStudio.ProjectSystem/Serialization/ProjectSerializer.cs`
- Modify: `VisualGameStudio.Core/.../BasicLangProject` (carry the properties on the model)
- Test: `VisualGameStudio.Tests/` — new `ProjectSerializerRoundTripTests.cs`

- [ ] **Step 1: Write the failing test first** — load a `.blproj` containing `<UseWindowsForms>`,
      `<TargetFramework>` **and an invented `<SomeFutureProperty>`**, save, reload, assert all three
      survive. The invented one is the point: it is what stops the fourth property repeating this.
- [ ] **Step 2:** Run, confirm all three are lost.
- [ ] **Step 3:** Round-trip `UseWindowsForms`/`UseWPF`/`TargetFramework` on the model, and preserve
      unknown `PropertyGroup` children verbatim.
- [ ] **Step 4:** Assert the **CLI** reads the saved file correctly — not only the IDE. Both entry
      points.

**Gate:** `--filter "TestCategory!=Integration"` plus the project-system fixtures.

### Task 3: Cross-file `Implements` — decide, then (if yes) fix

⚠ **This task's shape changed.** The build prompt defers this because it "touches
`Compiler.CollectExportedSymbols`, which is shared machinery". Spec §2.1 found that is **wrong**:
`AccessModifier.Public` is the enum's zero value *and* is assigned explicitly in the `Symbol`
constructor (`SymbolTable.cs:76`), while `Visit(InterfaceNode)` never sets `Access` — so every
interface already satisfies the first arm of the export filter and **exports normally** from a
`.bas`/`.mod` unit.

✅ **Confirmed in scope (spec §10 Q4).** ⛔ **Do not start until Task 1 has landed and passed its
own full-suite gate** — two unproven `SemanticAnalyzer` changes under one gate leave a red suite
with two candidate causes.

**Files:**
- Modify: `BasicLang/SemanticAnalyzer.cs:349-368` (shell `InterfaceNode` in pass 1)
- Modify: `BasicLang/SemanticAnalyzer.cs` interface arm of `Visit(ClassNode)` (`GlobalScope`
  fallback accepting `SymbolKind.Interface`)
- **Do NOT modify** `Compiler.CollectExportedSymbols`.

- [ ] **Step 1:** Flip `Probe_CrossFileImplements_StillUnresolved` — it exists to fail loudly when
      someone fixes this. Turn it into the positive assertion.
- [ ] **Step 2:** Add the two changes above, symmetric with Task 1's patch.
- [ ] **Step 3:** Record the residual gap in a test, do not silently leave it: an interface declared
      in a **`.cls`/`.class`** file is still dropped, by the `unit.IsClassFile` short-circuit at
      `Compiler.cs:815-823`. That one *would* need `CollectExportedSymbols`. Out of scope; pin it.

**Gate: FULL SUITE.** `SemanticAnalyzer.cs` again.

### Task 4: Define DPI behaviour

- [ ] Emit `<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>` in **both** csproj
      generators — `ProjectTemplateService.cs:305-309` and `BuildService.cs:1119-1124`. Two
      generators, one behaviour; a change to one only is the recurring shape of bugs here.
- [ ] Drop `"msil"` from `winforms-app`'s `SupportedSolutionTypes`
      (`IProjectTemplateService.cs:386`) — that pipeline stops at a `.il` file. **Verified safe:**
      `EverySolutionType_HasAtLeastOneTemplate` still passes because `console-app`, `game-app`,
      `class-library` and `unit-test` all carry `"msil"` (spec §2.5).

**Gate:** `ProjectTemplateBackendMappingTests` + `--filter "TestCategory!=Integration"`.

---

## Slice 1 — a read-only canvas over the two templates that already ship (~1 week to demo)

**Zero writes, zero new file type, zero project-system change, zero build change, zero shell
refactor.** The whole slice is produced by a *reader*.

**Slice 1 must NOT:** widen `_openDocuments`, add a document type, add a dock region, add a file
extension, touch `JavaScriptEmitter`, or write one byte into a user's file.

### Task 5: The form model

**Files:**
- Create: `VisualGameStudio.Core/Forms/FormDocument.cs`, `FormControl.cs`, `FormControlCatalog.cs`

- [ ] **Step 1:** `FormDocument` / `FormControl` / a 10-kind `FormControlCatalog`.
- [ ] **Step 2:** Per-target `Facet` bags with `Literal` / `Expression` / `Property` / `Attribute`
      kinds plus a `<Raw>` hatch (spec D3). Even with no writer, the **reader** must round-trip a
      foreign-target facet into the model so slice 2's writer can preserve it.
- [ ] **Step 3:** **Serialize-subtree / deserialize-subtree-with-rename**, per spec §8 — required in
      slice 1 even though no UI calls it. Nearly free now, very expensive later.
- [ ] **Step 4:** Three-tier read policy (spec D11) with **per-property** Degraded state carrying a
      reason string, not a per-form flag.

**Gate:** unit tests; `--filter "TestCategory!=Integration"`.

### Task 6: The `InitializeComponent` recognizer, two dialects

A token-stream reader using a `SourceIndex` line/column→offset mapper. **No lexer change.**

The WinForms grammar target is the shipped template verbatim
(`BasicLang.VisualStudio/.../WinFormsApp/MainForm.bas`):

```basiclang
Me.Text = "$safeprojectname$"
Me.Size = New Size(400, 300)
Me.StartPosition = FormStartPosition.CenterScreen
lblMessage = New Label()
lblMessage.Location = New Point(20, 20)
Me.Controls.Add(lblMessage)
AddHandler btnClick.Click, AddressOf btnClick_Click
```

- [ ] **Step 1:** Fixtures for both dialects **before** the reader.
- [ ] **Step 2:** The reader. Unrecognized statement → that property Degraded with a reason; an
      unrecognized *control kind* → Refused (`BL8002`).
- [ ] **Step 3:** Assert against the two templates that already ship and already build.

### Task 7: `BasicLang.exe design --check <file>`

Headless, scriptable, useful in CI on day one — and it proves the grammar before a single pixel
exists.

- [ ] **Step 1:** Parses, reports `BL8xxx` with `file(line,col)`, exits **1** on refusal.
- [ ] **Step 2:** ⚠ **Grep every backend/solution-type dispatch map before adding the verb.** A
      missing switch arm here does not fail — it silently builds C#. Four maps have already
      defaulted that way. Make new defaults **throw**.
- [ ] **Step 3:** Extend `ProjectTemplateBackendMappingTests`.

**Gate:** run the real CLI. **stdout is the only valid oracle.**

### Task 8: The canvas

**Files:**
- Create: `VisualGameStudio.Editor/Controls/FormCanvasControl.cs`
- Modify: `CodeEditorDocumentViewModel` — a `Design | Code` segmented toggle bound to the **same**
  `TextDocument`

- [ ] **Step 1:** `FormCanvasControl : Control` overriding `Render(DrawingContext)`, following
      `MinimapControl.axaml.cs:417` (six margin controls in `VisualGameStudio.Editor/Margins/` are
      the same pattern).
- [ ] **Step 2:** Schematic draw + selection + hit-testing. **Schematic, not WYSIWYG** — the IDE has
      no rendering browser (spec §2.4: the `WebViewDocumentView` that exists is an HTML *source*
      viewer by its own banner) and no property grid or toolbox. Design for that honestly rather
      than apologising for it.
- [ ] **Step 3:** The canvas renders the model recovered **from the persisted artifact**, never from
      an in-memory delta (spec D11).
- [ ] **Step 4:** ⚠ **`dotnet clean` before building** — AXAML changes plus a stale build cache
      cause crashes.

**The demo:** open the shipped `winforms-app` template and the shipped `web-site` template — files
that already exist and already build — and see the form.

**Gate:** full suite if anything reached `VisualGameStudio.Editor`; otherwise the fast subset plus a
manual run of `IDE/VisualGameStudio.exe`.

---

## Slice 2+ — writing (not scheduled here; sequence fixed)

Ordered so **the first writing, shipping, owner-facing designer is the web one** — it has the owner
mandate, `dom-core.bli` is machine-readable ground truth a catalog can be pinned against, and F5
already reaches a real renderer (`WebPreviewServer`, registered at `ServiceConfiguration.cs:96`).

1. **`.blform` persistence + the structure-preserving writer.** Gate on the algebra, not on cases:
   a no-op patch writes **nothing**; round-trip is byte-identical; `Read∘Apply == Apply∘Read`.
   Reserve `<Components>`, `<Resources>`, `<Bind>` and `TabIndex` on day one (spec D1).
2. **`.blform` as a `<Compile>` item.** Add to `FileExtensions.SourceExtensions`; keep **out of**
   `ProjectFile.BasicLangSourceExtensions` and `ModuleResolver.SupportedExtensions`.
   ⚠ **Then immediately do (3) — between them there is a window where XML reaches the lexer.**
3. **The `FormLowerer` seam — partition, then inject** (spec §2.2, D5). In the BL6014 guard region
   of `CompileProjectFiles`, after the `files.Count == 0` check and **before** the registration loop
   at `Compiler.cs:383`. BL6014 is a C-family *allowlist*, so it will not catch `.blform`; without
   this, `File.ReadAllText` hands XML to the BasicLang lexer, the error-recovering parser does not
   throw, and a junk module is silently registered — the exact incident recorded at
   `LspMixedProjectTests.cs:13-40`, where the pollution is **invisible in diagnostics**.
   **Test that a `.blform` never reaches the lexer, via the symbol table directly.**
4. **The toolbox and property grid.** Both are hand-built — Avalonia 11.3 base ships no
   `PropertyGrid`, no `ColorPicker` and no font dialog. Price every type editor.
   Edits commit on focus-loss/Enter, not per keystroke (spec D13).
5. **The markup emitter** — HTML fragment with stable ids + generated `.css`, always overwriting
   from an asset root so `JavaScriptEmitter.cs:105-115`'s never-overwrite guard keeps protecting a
   hand-authored `index.html`. `<body data-form="LoginForm">` with `Main()` dispatching on
   `getAttribute` (spec D8 — **one `.js` per project; do not take on a backend change**).
6. **WinForms lowering** — ✅ confirmed in scope (spec §10 Q1). Its CI gate (generate every catalog control with
   every property, require the real CLI to exit 0) **is** its type system; blocker 7 means there is
   no other check.

---

## Standing house rules for every task above

- **Both entry points.** `BasicLang.exe build X.blproj` *and* the IDE `BuildService` path
  (`BuildService.cs:651`). A fix verified through only one still breaks the other.
- **Validate through the CLI or an optimizer-running helper** (`CompileToCppOptimized`-style).
  Every shipping route runs the IR optimizer; the unit-test helper does not.
- **"Passed!" does not mean the suite passed.** A crashed host still prints a per-assembly summary;
  the abort goes to stderr. Capture both; check the total against the expected count.
- **Build contention looks exactly like a test failure.** Re-run a failing native test in isolation
  before investigating. Only an assertion failure is evidence.
- **After AXAML changes, `dotnet clean` before building.**
- **Never round-trip repo files through PowerShell `Get-Content`/`Set-Content`.** Multi-line commit
  messages go through a file and `git commit -F`.
- **`IDE/` is a hand-committed xcopy drop that goes stale.** Refresh with
  `robocopy <Shell bin> IDE /E` — **never `/MIR`**. Verify against the deployed binary
  (`IDE/BasicLang.exe new --list`), never timestamps.
- **Generated type names never contain `_`.** The PascalCase heuristic at `SemanticAnalyzer.cs:2397`
  excludes any identifier containing an underscore, which turns `Me.Text` into a hard semantic
  error. `<Name>Base`, never `<Name>_Base`.
- **Generated base members are `Protected`, never `Private`** — `PopulateSiblingClassMembers`
  (`SemanticAnalyzer.cs:448-478`) guards every arm with `!= AccessModifier.Private`, so a private
  member of a sibling base is invisible to the subclass.
- Iterate with `--filter "TestCategory!=Integration"` (~2 min); the full suite (~39 min) is the gate.
