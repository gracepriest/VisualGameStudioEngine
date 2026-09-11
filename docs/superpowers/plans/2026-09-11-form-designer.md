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

**Shape:** 21 tasks across four slices, plus measured facts, file structure and risks. Slice 0 makes the ground true; slice 1 demos a read-only
canvas with zero writes; slice 2 ships the web designer; slice 3 adds WinForms. Slices 0 and 1 are
independent of each other and can run in either order — everything from slice 2 on needs both.

---

## ⛔ STATUS: NOT STARTED. Nothing below has been built or run.

**No task in this plan has been implemented, and no gate in it has been executed.** The authoring
environment for both this plan and the spec has **no .NET SDK** (`dotnet: command not found`) —
the same constraint that produced the unbuilt patch in Task 1. Every "Gate" line is an instruction,
not a record.

**Owner sign-off: 4 of 5 answered, and every question that shapes the design is closed.** All
twenty-one tasks below are specified to the step — slices 0 through 3, plus a definition of done.
The one question still open (Q5) blocks execution, not design. Spec §10's five questions:

- **Q1 — ✅ ANSWERED 2026-09-11: yes, WinForms also.** The dual-target scope is confirmed. Slice 3
  (Tasks 18–20) is un-gated, and its catalog CI gate is now mandatory infrastructure rather than a
  proposal — it is the only correctness check that target has (spec §10 Q1). Task 2 is on the
  critical path for the same reason.
- **Q2 — ✅ ANSWERED 2026-09-11: Grid/Flow persisted, free pixel-drag with snap resolution**
  (spec D2 + **D2a**). Task 5's catalog is un-gated. Task 8 gains a prerequisite: the
  snap-resolution rule is load-bearing UI with five sub-decisions (spec D2a) and must be designed
  before the canvas gains interaction — **new Task 8a**.
- **Q3 — ✅ ANSWERED 2026-09-11: the designer subsumes page models 2 and 3.** No slice reorders,
  but two staged features become **acceptance obligations on the designer** (spec §7): model 2 is
  delivered by D9 and model 3 by the new **D10a**. D10a settles a conflict the answer creates —
  convention wiring *and* explicit `<Bind>` wiring would double-fire and make "missing handler"
  undecidable, so `<Bind>` wires and convention only suggests the name. New diagnostic `BL8008`
  covers the resulting gap (handler exists, not bound).
- **Q4 — ✅ ANSWERED 2026-09-11: fix cross-file `Implements` in slice 0.** Task 3 is un-gated.
  Gate Tasks 1 and 3 **separately** — slice 0 now carries two `SemanticAnalyzer` changes, one of
  them never compiled, and a shared gate would leave a red suite with two candidate causes.
- **Q5 — still open, and it blocks execution rather than design:** nobody has run a gate yet. The
  `Inherits` patch has never been compiled, and neither the spec nor this plan was written on a
  machine with a .NET SDK. Slice 0 needs an SDK and ~39 minutes per full-suite run, twice.

Q1's answer does **not** re-order the slices: the web half still ships first (spec §9).

⚠ **The `superpowers` plugin is not installed in the environment this plan was written in** — the
`superpowers:` workflows the header names, and that every other plan in this directory relies on,
were unavailable. The plan conforms to the house format by hand. On a machine that has the plugin,
drive it with `superpowers:subagent-driven-development` or `superpowers:executing-plans` as normal.

---

## Measured facts

Every row was re-verified against `origin/master` at `d6b57b6` while writing the spec, on Linux in
a cloud session. **Re-run them rather than trusting them** — line numbers drift, and six rows here
contradict the build prompt (spec §2). A row marked ⚠ is one the prompt got wrong.

| # | Fact | How it was measured | Result |
|---|---|---|---|
| 1 | No partial classes anywhere | `grep -c Partial BasicLang/Parser.cs BasicLang/ASTNodes.cs BasicLang/SemanticAnalyzer.cs` | `0 / 0 / 0` |
| 2 | `Handles` lexed, never parsed | `grep -n "TokenType.Handles" BasicLang/Parser.cs` | no output; token at `BasicLangLexer.cs:192,540` |
| 3 | `Inherits` resolves via `_typeManager` only | read `SemanticAnalyzer.cs:4569` | opaque arm `:4573-4578`, hard error `:4582` |
| 4 ⚠ | `Symbol.Access` defaults to `Public` **twice** | read `SymbolTable.cs:76`; `AccessModifier` enum | `Access = AccessModifier.Public;` in ctor, **and** `Public` is the enum's zero value (`ASTNodes.cs:366-373`) |
| 5 ⚠ | Interfaces **do** pass the export filter | read `Compiler.cs:815-832` | first arm is `symbol.Access == Public`; only the `IsClassFile` short-circuit drops them |
| 6 | Cross-unit symbols reach `GlobalScope`, never `_typeManager` | read `SemanticAnalyzer.cs:287-328` | `ImportImplicitProjectSymbols` calls `GlobalScope.Define` only |
| 7 | Pending-sibling pass shells `ClassNode` only | read `SemanticAnalyzer.cs:349-368` | no `InterfaceNode` arm |
| 8 | A sibling base's `Private` members are invisible | read `SemanticAnalyzer.cs:448-478` | every arm guarded `when member.Access != AccessModifier.Private` |
| 9 | WinForms is un-armed for .NET resolution | read `Compiler.cs:145-146` | early `return` on `UseWindowsForms \|\| UseWpf` |
| 10 | WinForms types come from a heuristic | read `SemanticAnalyzer.cs:2397` | `char.IsUpper(name[0]) && !name.Contains('_')`, under "Be VERY permissive" |
| 11 | `CommonNetTypes` has zero WinForms names | `sed -n '203,232p' … \| grep -cE '"(Form\|Button\|Point\|Size\|Label\|TextBox)"'` | `0` |
| 12 ⚠ | `ProjectSerializer.Save` **destroys** unknown properties | read `ProjectSerializer.cs:245-273` | `new XDocument(...)` rebuild; emits 7 property elements |
| 13 ⚠ | `dom-core.bli` is 119 lines, with no positioning | `wc -l`; `grep -c "position\|zIndex"` | `119`; `0` |
| 14 | `GetSourceFiles` explicit branch is unfiltered | read `ProjectFile.cs` | bare `Directory.GetFiles(dir, filePattern)` |
| 15 ⚠ | **BL6014 is a C-family allowlist** — it cannot catch `.blform` | read `Compiler.cs:351-381` | filters on `CFamilySourceExtensions` only; loop at `:383` does `File.ReadAllText` on everything else |
| 16 | The seam reaches every build route | `grep -rn "CompileProjectFiles"` | `Compiler.cs:334`, called from `Program.cs:532`, `Compiler.cs:227`, `BuildService.cs:651`, `CppProjectBuilder` |
| 17 | The harness is never overwritten | read `JavaScriptEmitter.cs:105-115` | `⛔ NEVER overwrite the harness` |
| 18 ⚠ | The Shell's `WebView` is a **source viewer** | read `WebViewDocumentView.axaml` | its own banner: "full rendering requires a browser component (e.g., CefNet or WebView2)" |
| 19 | Dropping `"msil"` from `winforms-app` is safe | read `IProjectTemplateService.cs:350,368,432,461` | `console-app`, `game-app`, `class-library`, `unit-test` all carry it |
| 20 | One extension→`ItemType` decision point | read `ProjectService.cs:246` | `FileExtensions.IsSourceFile(path) ? Compile : Content` |
| 21 | `obj/` is already gitignored | `grep -n "\[Oo\]bj/" .gitignore` | `.gitignore:37` — the generated-base location needs no new rule |
| 22 | The prior lexer-pollution incident | read `LspMixedProjectTests.cs:13-40` | a `.cpp` from `GetSourceFiles()` **unfiltered** was "lexed/parsed AS BASICLANG", and "the pollution is invisible in diagnostics" |

### Commands used throughout

```powershell
# fast loop while iterating (~2 min) — skips the compile/run/spawn tests
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "TestCategory!=Integration"

# THE GATE (~39 min). Capture BOTH streams: a crashed host still prints a per-assembly "Passed!"
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release 1> suite.out 2> suite.err

# the compiler alone, and the IDE
dotnet build BasicLang/BasicLang.csproj -c Release
dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release   # dotnet clean first after AXAML

# the CLI — stdout is the only valid oracle, because every shipping route runs the IR optimizer
IDE/BasicLang.exe MyFile.bas --target=csharp
IDE/BasicLang.exe build MyProject.blproj
IDE/BasicLang.exe design --check LoginForm.blform     # Task 7
IDE/BasicLang.exe design --import MainForm.bas        # Task 16

# refresh the hand-committed xcopy drop — NEVER /MIR
robocopy VisualGameStudio.Shell/bin/Release/net8.0 IDE /E
IDE/BasicLang.exe new --list                          # verify the deployed binary, not timestamps
```

## File structure

New files this plan creates, in one place. Everything else is a modification to an existing file
named in its task.

```
VisualGameStudio.Core/Forms/
    FormDocument.cs  FormControl.cs  FormControlCatalog.cs    Task 5
    BlformReader.cs  BlformWriter.cs  Schema/                 Task 9
BasicLang/Forms/
    FormLowerer.cs                                            Task 11
    FormIR.cs  WebFormLowerer.cs                              Task 12
    WinFormsLowerer.cs                                        Task 18
VisualGameStudio.Editor/Controls/
    FormCanvasControl.cs                                      Task 8, writes in Task 15
VisualGameStudio.Shell/Views/Panels/
    PropertyGridView.axaml(.cs)  ToolboxView.axaml(.cs)       Task 14
VisualGameStudio.Tests/
    Forms/BlformAlgebraTests.cs                               Task 9
    Compiler/FormLoweringSeamTests.cs                         Task 11
    Forms/WebLoweringTests.cs                                 Task 12
    Forms/FormEventWiringTests.cs                             Task 13
    Forms/WinFormsLoweringTests.cs                            Task 18
    ProjectSerializerRoundTripTests.cs                        Task 2

obj/gen/forms/            generated .bas — gitignored (.gitignore:37), never a <Compile> item
```

## Risks

| Risk | Why it is real here | Mitigation |
|---|---|---|
| **The `Inherits` patch does not compile, or its tests do not discriminate.** | It was authored with no SDK and has never been built or run, in either direction. | Task 1 Step 2 runs the tests against *reverted* code first. Any test green on unpatched source gets rewritten, not accepted. |
| **A `.blform` reaches the BasicLang lexer.** | BL6014 is an allowlist (fact 15) and the parser recovers silently — this exact shape has already hit the repo twice (fact 22). | Tasks 10+11 in one push; the guard test asserts via the **symbol table**, not `result.Success`. |
| **The WinForms catalog is wrong and nothing notices.** | No type metadata at any layer (facts 9–11); member access degrades to `Object` with no diagnostic. | Task 19 generates every control with every property and requires the real CLI **and** `dotnet build` to exit 0. It is the type system. |
| **A backend dispatch map silently defaults to C#.** | Four maps have already done this. | Task 7 and Task 16 both grep every map keyed on backend or solution type; new defaults throw; extend `ProjectTemplateBackendMappingTests`. |
| **The snap rule gets improvised at the keyboard.** | Task 15 is where a designer feels good or bad, and the temptation is to tune it live. | Task 8a is a written, signed-off rule with no code, and Task 15 cannot start before it. |
| **Convention wiring creeps back in as a "helpful" second mechanism.** | It looks like a small kindness and double-fires every handler (spec D10a). | Task 13 Step 4 asserts a conventionally-named handler with no `<Bind>` is **not** wired and raises `BL8008`. |
| **A green suite that is not green.** | A crashed host prints a per-assembly "Passed!" and sends the abort to stderr. | Capture both streams; check the total against the recorded baseline (5826 / 4 known failures). |
| **The IDE and the CLI diverge.** | They are separate entry points into the same seam. | Every task that touches the build path asserts both, in that task, not later. |

---

## Slice 0 — prerequisites. Non-negotiable. (~2 days + two full suite runs)

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

### Task 3: Fix cross-file `Implements`

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
      foreign-target facet into the model so Task 9's writer can preserve it.
- [ ] **Step 3:** **Serialize-subtree / deserialize-subtree-with-rename**, per spec §8 — required in
      slice 1 even though no UI calls it. Nearly free now, very expensive later.
- [ ] **Step 4:** Three-tier read policy (spec D11) with **per-property** Degraded state carrying a
      reason string, not a per-form flag.
- [ ] **Step 5:** The catalog's container kinds are `Grid`, `Flow` and `Canvas` (spec D2 + D2a).
      `Grid` carries `Cols`/`Rows` track lists and children carry `Col`/`Row`; `Canvas` children
      carry `X`/`Y` and are the **only** ones that may carry `Anchor`. Model this now even though
      slice 1 never writes it — the recognizer in Task 6 must be able to produce a `Canvas` model,
      because that is what both shipped templates actually are.

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

⛔ **No drag in slice 1.** Selection and hit-testing only. Dragging a control *is* a write, and
slice 1's whole contract is zero writes; the drag gesture arrives in Task 15, behind Task 8a. The
temptation is real, because a canvas that highlights on click feels one small step from a canvas
that moves things. It is not — the step is the entire snap-resolution design.

**The demo:** open the shipped `winforms-app` template and the shipped `web-site` template — files
that already exist and already build — and see the form.

**Gate:** full suite if anything reached `VisualGameStudio.Editor`; otherwise the fast subset plus a
manual run of `IDE/VisualGameStudio.exe`.

---

## Slice 2 — the web designer writes (the first shipping designer)

Ordered so **the first writing, shipping, owner-facing designer is the web one** — it has the owner
mandate, `dom-core.bli` is machine-readable ground truth a catalog can be pinned against, and F5
already reaches a real renderer (`MainWindowViewModel.cs:4168`, `_webPreviewServer.Start(...)`).

⚠ **Tasks 10 and 11 must land in the same push.** Between them there is a window in which a
`.blform` listed as `<Compile>` is handed to the BasicLang lexer. Do not split them across commits,
and do not leave that window open overnight.

### Task 8a: Design the snap-resolution rule (no code)

The owner's Q2 answer (spec D2a) is only real if this is decided before a drag gesture exists.
Output is a short spec section appended to the design doc, not source.

- [ ] **Step 1:** Which cell wins when a drop straddles a boundary. **Recommendation: the pointer's
      own position**, not the control's centroid — the pointer is what the user is looking at.
- [ ] **Step 2:** What a drop *outside every existing cell* does — extend the grid (add a row or
      column) or refuse with a visible reason. ⚠ **Silently clamping into the nearest existing cell
      is what users report as "it moved my button somewhere else".** This is the sub-decision that
      ships badly if it is improvised.
- [ ] **Step 3:** Within-cell placement — stretch, or an alignment (start/center/end) picked from
      where in the cell the drop landed. Stretch is the default only for a single occupant.
- [ ] **Step 4:** Undo restores the prior **constraint**, not the prior pixels, and the undo entry
      names the constraint.
- [ ] **Step 5:** `<Canvas>` children keep the raw, unresolved gesture — drag sets `X`/`Y` and
      snaplines behave exactly as a WinForms user expects. This is where the fidelity argument is
      honoured in full.
- [ ] **Step 6:** State the invariant explicitly: **the rule is UI behaviour, not persistence.** It
      must never produce a document the property grid could not have produced, so it inherits D11's
      `Read∘Apply == Apply∘Read` obligation.

**Gate:** owner or reviewer sign-off on the written rule. No build.

### Task 9: `.blform` schema and the structure-preserving writer

**Files:**
- Create: `VisualGameStudio.Core/Forms/BlformReader.cs`, `BlformWriter.cs`
- Create: `VisualGameStudio.Core/Forms/Schema/` (element and attribute names as constants)
- Test: `VisualGameStudio.Tests/Forms/BlformAlgebraTests.cs`

- [ ] **Step 1: Write the algebra tests first, property-based over generated documents** — not three
      hand-written cases. Three laws: a no-op patch writes **nothing** (assert bytes, not a dirty
      flag); round-trip is **byte-identical**; `Read∘Apply == Apply∘Read`.
- [ ] **Step 2:** Reader on `XDocument.Load` with `LoadOptions.SetLineInfo` so every diagnostic
      carries `file(line,col)`. Unknown elements, unknown attributes and comments are retained on
      the model, not discarded.
- [ ] **Step 3:** Writer with deterministic attribute order and children in z-order.
- [ ] **Step 4:** Reserve the four day-one sections even though v1 writes none of them:
      `<Components>`, `<Resources>`, `<Bind Property= Source= Path=>`, and an explicit `TabIndex`
      on every control (spec D1). Each is an hour now and a format break later.
- [ ] **Step 5:** `BL8001` (not well-formed), `BL8002` (unknown control kind → Refused, file never
      written), `BL8003` (unparseable value → that **one property** Degraded), `BL8007` (duplicate
      id).
- [ ] **Step 6:** Wire the reader into `design --check` from Task 7 so the CLI validates `.blform`
      as well as recovered source.

**Gate:** `--filter "TestCategory!=Integration"`. ⛔ **A failing algebra law is a hard stop, not a
known issue** — every later task assumes these three hold.

### Task 10: `.blform` enters the project system

**Files:**
- Modify: `VisualGameStudio.Core/Constants/FileExtensions.cs` (add `BasicLangForm = ".blform"` and
  put it in `SourceExtensions`)
- Verify unchanged: `ProjectFile.BasicLangSourceExtensions`, `ModuleResolver.SupportedExtensions` —
  `.blform` must **not** appear in either
- Modify: `VisualGameStudio.ProjectSystem/Services/ProjectService.cs:246` — the single
  extension→`ProjectItemType` decision point, so an added `.blform` becomes `Compile`, not `Content`

- [ ] **Step 1:** Add the extension and confirm `<Compile Include="LoginForm.blform" />` round-trips
      through `ProjectSerializer` (it already does — verified spec §4 — so this is a regression
      test, not new code).
- [ ] **Step 2:** ⚠ **Assert the two exclusions.** A test that fails if `.blform` ever appears in
      `ProjectFile.BasicLangSourceExtensions` or `ModuleResolver.SupportedExtensions`. Both are
      "obviously wrong to add" right up until someone adds them to fix a different bug.
- [ ] **Step 3:** Solution Explorer shows `.blform` with its own icon and opens it in the designer.
- [ ] **Step 4:** ⛔ **Go straight to Task 11 in the same push.**

**Gate:** `--filter "TestCategory!=Integration"` + `SolutionExplorerViewModelTests`.

### Task 11: The `FormLowerer` seam — partition, then inject

Spec §2.2 and D5. **This is the task the build prompt got wrong**, so read §2.2 before starting.

**Files:**
- Modify: `BasicLang/Compiler.cs` — the BL6014 guard region (`:351-381`), after the
  `files.Count == 0` check and **before** the registration loop at `:383`
- Create: `BasicLang/Forms/FormLowerer.cs`
- Test: `VisualGameStudio.Tests/Compiler/FormLoweringSeamTests.cs`

- [ ] **Step 1: Write the failing guard test first** — a project with a `.blform` `<Compile>` item
      compiles, and the `.blform` **never reaches the lexer**. ⚠ **Assert via the symbol table /
      module registry directly.** `LspMixedProjectTests.cs:13-40` records why: the error-recovering
      parser does not throw, it registers a junk module under the file's basename, and *"the
      pollution is invisible in diagnostics"*. A test that only checks `result.Success` passes while
      the symbol table is corrupt.
- [ ] **Step 2:** Confirm BL6014 does **not** catch it today — `CFamilySourceExtensions` is an
      allowlist. Run the test and watch XML reach the lexer. That is the bug, reproduced.
- [ ] **Step 3:** Partition `.blform` out of `files`; hand them to the lowerer; inject the generated
      `.bas` back in. One seam, four build routes (`Program.cs:532`, `Compiler.cs:227`,
      `BuildService.cs:651`, `CppProjectBuilder`).
- [ ] **Step 4:** Generated sources land in `obj/gen/forms/` — gitignored already (`.gitignore:37`,
      `[Oo]bj/`), never a `<Compile>` item, never in Solution Explorer (spec D4).
- [ ] **Step 5: Both entry points.** `BasicLang.exe build X.blproj` **and** the IDE `BuildService`
      path. Assert both, in this task, not later.

**Gate: FULL SUITE.** This edits `Compiler.CompileProjectFiles`, which every build route runs
through.

### Task 12: FormIR and the web lowerer

**Files:**
- Create: `BasicLang/Forms/FormIR.cs`, `BasicLang/Forms/WebFormLowerer.cs`
- Test: `VisualGameStudio.Tests/Forms/WebLoweringTests.cs`

- [ ] **Step 1:** `FormIR` as the single intermediate both targets lower from (spec D6). ⚠ **Lower
      to BasicLang source, never to JavaScript.** Emitting `.js` directly would bypass source maps,
      `JsCapabilityChecker`, semantic type-checking, and the ability for handler bodies to call
      generated declarations with real types.
- [ ] **Step 2:** Emit three artifacts per form: an HTML fragment with stable ids, a `.css`, and a
      `.g.bas` that touches only `getElementById` and `addEventListener` (both already declared —
      `dom-core.bli:24` and `:57`). **Zero `.bli` additions.**
- [ ] **Step 3: Layout is CSS text, not typed member access.** `dom-core.bli` has no
      `position`/`left`/`top`/`zIndex` (verified: `grep -c` returns 0). Geometry **fans out** on the
      web — `X="96" Y="80"` becomes two independent declarations (spec D7).
- [ ] **Step 4:** Generated markup **always overwrites**, shipped from an asset root, so
      `JavaScriptEmitter.cs:105-115`'s never-overwrite guard keeps protecting only a hand-authored
      `index.html` that never entered that root. Test both halves: generated markup is replaced on
      rebuild; a hand-authored `index.html` is not.
- [ ] **Step 5:** One `.js` per project (spec D8). `<body data-form="LoginForm">`, `Main()`
      dispatching on `doc.body.getAttribute("data-form")`. ⛔ **Do not add multi-entry-point support
      to `JavaScriptBackend` — that is a backend change, not a designer change.**
- [ ] **Step 6:** Generated base class members are `Protected`, never `Private`
      (`SemanticAnalyzer.cs:448-478` — a private member of a sibling base is invisible to the
      subclass), and the type is `<Name>Base`, never `<Name>_Base` (the PascalCase heuristic at
      `:2397` excludes identifiers containing `_`).

**Gate:** the real CLI. **stdout is the only valid oracle** — every shipping route runs the IR
optimizer and the unit-test helper does not.

### Task 13: Events — `<Bind>` wires, convention names

Spec D10 and **D10a**, which exists because the owner's Q3 answer made auto-wiring the designer's
job.

**Files:**
- Modify: `BasicLang/Forms/WebFormLowerer.cs` (DOM thunk), `FormIR.cs`
- Test: `VisualGameStudio.Tests/Forms/FormEventWiringTests.cs`

- [ ] **Step 1:** `FormEvent` plus a per-target thunk — an inline lambda on DOM, a private
      `(sender, EventArgs)` sub on WinForms. This dodges the `Action` vs `Action(Of DomEvent)` arity
      wall and BL7007's ban on `EventArgs` together.
- [ ] **Step 2:** Wiring is `AddHandler <ctl>.<Event>, AddressOf <handler>` — `Handles` is lexed but
      **never parsed** (`Parser.cs` has zero `TokenType.Handles`), so it is not available.
- [ ] **Step 3: A missing handler is an error** (`BL8005`), not a warning. The analyzer has no
      override validation, so a renamed or deleted handler otherwise yields a green build with a
      wired, dead button.
- [ ] **Step 4: Exactly one wiring mechanism** (D10a). `<Bind>` wires; the naming convention only
      *suggests* the name. ⚠ **Test that a conventionally-named handler with no `<Bind>` is NOT
      wired** and raises `BL8008`. That assertion is what stops convention wiring creeping back in
      as a "helpful" second mechanism and double-firing every handler.

**Gate:** `--filter "TestCategory!=Integration"` + a CLI run proving a real button fires once.

### Task 14: The property grid and toolbox

**Files:**
- Create: `VisualGameStudio.Shell/Views/Panels/PropertyGridView.axaml(.cs)`, `ToolboxView.axaml(.cs)`
  and their view models
- Modify: `VisualGameStudio.Shell/Dock/DockFactory.cs` (two new dock regions)

- [ ] **Step 1:** Both are hand-built. Avalonia 11.3 base ships **no** `PropertyGrid`, **no**
      `ColorPicker` and **no** font dialog — price every type editor as bespoke.
- [ ] **Step 2:** Edits commit on **focus-loss/Enter**, not per keystroke (spec D13) — otherwise
      typing "Sign in" is eight undo entries.
- [ ] **Step 3:** Degraded rows are **per property** (spec D11): one unparseable value freezes
      exactly one row, shows its reason, and leaves every other row editable.
- [ ] **Step 4:** Foreign-target facets render greyed with a reason and are **never dropped on save**
      (spec D3) — the round-trip test from Task 9 covers the persistence half; this is the UI half.
- [ ] **Step 5: Double-click-to-create-handler is ONE atomic edit** (D10a): it writes
      `Sub btnSave_Click()` **and** the matching `<Bind>` together. ⚠ Shipping the sub without the
      bind looks like model 3 working and is a dead button.
- [ ] **Step 6:** ⚠ **`dotnet clean` before building** — AXAML plus a stale cache crashes.

**Gate:** full suite (this reaches `VisualGameStudio.Shell`) + a manual `IDE/VisualGameStudio.exe`
run.

### Task 15: The canvas writes — drag with snap resolution

This is where slice 1's read-only canvas gains a write path, and where Task 8a's rule is
implemented. Not before.

**Files:**
- Modify: `VisualGameStudio.Editor/Controls/FormCanvasControl.cs`

- [ ] **Step 1:** Drag, drop from toolbox, delete, reorder — each resolved through Task 8a's rule.
- [ ] **Step 2:** Snaplines while dragging, and the status-bar readout naming the constraint the drop
      will write ("col 1, row 0 — stretch") **before** the mouse is released.
- [ ] **Step 3:** Undo/redo restores the prior **constraint**, not the prior pixels.
- [ ] **Step 4: The canvas keeps rendering the model recovered from the persisted artifact**, never
      from the in-memory delta (spec D11), so a writer/reader disagreement surfaces within one tick
      instead of as slow corruption.
- [ ] **Step 5:** Re-run Task 9's algebra tests against documents produced by dragging, not only by
      the property grid. The gesture must not be able to produce a document the grid could not.

**Gate:** full suite + manual IDE run.

### Task 16: `design --import` — the recognizer as an importer

**Files:**
- Modify: `BasicLang/Program.cs` (the `design` verb)

- [ ] **Step 1:** Convert a hand-written form (Task 6's recognizer output) into a `.blform` plus a
      trimmed code-behind. This is the migration path for existing forms, including the shipped VSIX
      template.
- [ ] **Step 2:** Refuses rather than guesses — anything the recognizer marks Refused stops the
      import with a named diagnostic and writes nothing.
- [ ] **Step 3:** ⚠ **Grep every backend/solution-type dispatch map again** before adding the
      subcommand. A missing switch arm does not fail, it silently builds C#; four maps have already
      defaulted that way.

**Gate:** the real CLI, against both shipped templates.

### Task 17: Web end-to-end — the shipping milestone

- [ ] **Step 1:** New project → design a form on the canvas → F5 → the form renders in the system
      browser and a button fires exactly once.
- [ ] **Step 2:** **Model 2 acceptance** (spec §7, §10 Q3): a page with **zero** designer-owned
      elements still builds and still supports code-behind; a mixed page's unowned markup is
      **byte-identical** after a designer save. A near-miss on the second is silent data loss in
      someone's hand-written HTML.
- [ ] **Step 3:** **Model 3 acceptance:** double-click produces a conventionally-named handler that
      is **actually wired** — assert the `<Bind>` was written, not merely that the sub exists.
      Asserting the sub alone would pass under the convention-only design that D10a rejected, and so
      proves nothing.

**Gate: FULL SUITE**, and this is the point at which the web designer is claimable as shipped.

---

## Slice 3 — WinForms

Confirmed in scope by the owner (spec §10 Q1). Everything here rests on Task 1 and Task 2 having
landed, and on the fact that **this target has no type metadata at any layer**.

### Task 18: The WinForms lowerer

**Files:**
- Create: `BasicLang/Forms/WinFormsLowerer.cs`
- Test: `VisualGameStudio.Tests/Forms/WinFormsLoweringTests.cs`

- [ ] **Step 1:** Match the shipped template's shape exactly
      (`BasicLang.VisualStudio/.../WinFormsApp/MainForm.bas`) — it is already swept by
      `TemplateBuildSweepTests` and `BuildServicePipelineTests.Build_WinFormsTemplate_DotNet_Builds`.
- [ ] **Step 2: Geometry fans IN here** (spec D7). `X="96" Y="80"` is **one** statement —
      `btnLogin.Location = New Point(96, 80)`. ⚠ It cannot be emitted as two: `Location` returns a
      `Point` **struct**, so `btnLogin.Location.X = 96` is CS1612 — and BasicLang will not catch it,
      because WinForms types come from the PascalCase heuristic and member access degrades to
      `Object` with no diagnostic. It compiles clean until `csc`.
- [ ] **Step 3:** One statement per property — there is no object-initializer or `With` syntax
      (`Parser.cs` `New` parses a type reference and optional positional args, then returns).
- [ ] **Step 4:** Grid/Flow containers lower to `TableLayoutPanel`/`FlowLayoutPanel`; `<Canvas>`
      children lower to `Location`/`Size` with `Anchor`.

**Gate:** the real CLI **and** `dotnet build` on the emitted project — `csc` is the only thing that
actually type-checks this target.

### Task 19: The catalog CI gate — the stand-in for the type system

⛔ **This is not a nicety. It is the only correctness check WinForms has.** `EnableNetResolution`
returns early for `UseWindowsForms` (`Compiler.cs:145-146`), the resolver closure is
`Microsoft.NETCore.App` only, and `CommonNetTypes` contains **zero** WinForms names — so the catalog
is otherwise unfalsifiable hand-written data.

- [ ] **Step 1:** Generate a project containing **every catalog control with every property set**.
- [ ] **Step 2:** Require the **real CLI** to exit 0, then `dotnet build` to exit 0.
- [ ] **Step 3:** Run it on every catalog change. A control added without passing this gate is a
      control nobody has established exists.
- [ ] **Step 4:** ⚠ Assert no generated identifier contains `_` — the heuristic excludes such names
      and `Me.Text` becomes a hard semantic error.

**Gate:** the gate *is* the test. Wire it into the suite as `[Category("Integration")]`.

### Task 20: WinForms end-to-end

- [ ] **Step 1:** New WinForms project → design a form → build → run → a window appears and a button
      fires once.
- [ ] **Step 2:** Confirm Task 2's fix holds end-to-end: add a file through the IDE (which triggers a
      project save), then build **from the CLI**. Before Task 2 this fails CS0246 on `Form`.
- [ ] **Step 3:** `design --import` on the shipped VSIX template produces a `.blform` that builds to
      the same window.

**Gate: FULL SUITE.**

---

## Definition of done

The feature is shippable when all of these hold at once:

- [ ] Slice 0's four tasks landed, each full-suite gated where it touches `SemanticAnalyzer`.
- [ ] Task 9's three algebra laws hold — no-op writes nothing, round-trip byte-identical,
      `Read∘Apply == Apply∘Read` — against documents produced by **both** the property grid and the
      drag gesture.
- [ ] A `.blform` never reaches the BasicLang lexer, asserted via the symbol table.
- [ ] Both entry points build every form: `BasicLang.exe build X.blproj` and the IDE.
- [ ] Page model 2 and model 3 acceptance tests pass (Task 17) — they are the only place those
      staged capabilities get proven.
- [ ] The WinForms catalog gate passes with every control and every property.
- [ ] `IDE/` refreshed with `robocopy <Shell bin> IDE /E` — **never `/MIR`** — and verified against
      the deployed binary (`IDE/BasicLang.exe new --list`), not timestamps.
- [ ] The full suite matches the recorded baseline, with stdout **and stderr** captured and the
      total checked against the expected count.

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
