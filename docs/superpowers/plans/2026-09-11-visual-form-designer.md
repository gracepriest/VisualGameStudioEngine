# Visual Form Designer — Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A visual form designer in the Visual Game Studio IDE producing two document formats —
`.blwebform` (browser page via the JavaScript backend) and `.blform` (WinForms desktop app via the
C# backend) — with the web half shipping first.

**Design spec:** `docs/superpowers/specs/2026-09-11-visual-form-designer-design.md`. Every `Dn`
referenced below is defined there with its rejected alternatives, and both document formats are shown
in full under *The two documents, by example* — read that before any task that touches them.

**Architecture:** The document is the truth. On save the designer writes **two comment-delimited
regions** into the user's own `.bas` (D1); everything outside them is the user's. **There is no
compile-time form expansion** — the compiler only *skips* form documents (D11) and the build emits
markup and CSS (D6). Web layout is Grid/Flow, WinForms layout is absolute pixels (D3).

**Tech Stack:** C# (compiler + IDE), Avalonia 11.3.13, Dock.Avalonia 11.3.12.1,
`Avalonia.Controls.DataGrid` 11.3.13, NUnit. No new NuGet packages, **no new project references**.

---

## ⛔ Read before starting any task

**Assembly placement is load-bearing and is decided in D5.** The project graph is
`ProjectSystem → {Core, BasicLang}`, `Editor → Core`, `Shell → {Core, Editor, ProjectSystem}`.
`BasicLang.csproj` declares one `ProjectReference` (`RaylibWrapper.vbproj`,
`ReferenceOutputAssembly="false"`) and `VisualGameStudio.Core.csproj` declares **none**.

| Component | Project | Why |
|---|---|---|
| Model, readers/writers, recognizer, markup emitter | **`BasicLang/Forms/`** | `basiclang build` must emit markup with no IDE present, so the compiler must reach them. |
| `FormCanvasControl`, designer ViewModels | **`VisualGameStudio.Shell/`** | Reaches `BasicLang` transitively via `ProjectSystem`. `Editor` sees only `Core` and **cannot** be used. |

Putting the model in `VisualGameStudio.Core` looks natural and is wrong — nothing would let the
compiler use it, and the error surfaces at the first `using` in Slice 2, after four tasks are already
gated in the wrong place.

**Three traps that will bite fastest:**

| Trap | Consequence |
|---|---|
| **`With … End With` silently drops every `.Prop = value`** (`IRBuilder.cs:3356`, no final `else`) | Generated code compiles clean and produces a form with no properties set. **Never emit `With`.** |
| **`Text` and `TextDocument` are two hand-synced stores; `SaveAsync` writes the string** (`CodeEditorDocumentViewModel.cs:618`, `:701-720`) | A writer touching `TextDocument` alone **saves stale text to disk** and may not mark the tab dirty. |
| **Any PascalCase name without `_` types as `Object` with no diagnostic** (`SemanticAnalyzer.cs:2395-2400`, `:7920`) | Wrong property names, missing handlers and undeclared DOM members all compile green. Every correctness claim needs a CLI gate, not a unit test. |

## Commands and gating policy

```bash
dotnet build BasicLang\BasicLang.csproj -c Release
```
```bash
dotnet build VisualGameStudio.Shell\VisualGameStudio.Shell.csproj -c Release
```
```bash
dotnet test VisualGameStudio.Tests\VisualGameStudio.Tests.csproj -c Release --filter "TestCategory!=Integration"
```
```bash
dotnet test VisualGameStudio.Tests\VisualGameStudio.Tests.csproj -c Release
```

**Baseline (owner decision 4 — trusted, not re-measured):** 5826 total, 4 failures, all pre-existing —
two `SearchSnippets_*`, `Cli_Build_CppProject_ProjectReference_Warns…`, `NonEx_variants…`
(`HANDOFF.md:103`, `:111-114`; only the last is load-dependent). Fast subset ≈ 4897/2/1.

**Gate proportionally.** Each task runs the fast subset plus its own touched suites and **states which
it ran and why**. The **full suite is required only** for Task 15 (tightens a `SemanticAnalyzer` error
path) and Slice 3's Tasks 17–18 (Integration sweeps + template roster). ⛔ The fast subset is **not** a
gate for codegen work — execution tests are `[Category("Integration")]`, so any task that changes what
is emitted must also run its Integration fixtures and a real CLI build.

⛔ A "Passed!" line does not mean the suite passed — capture both streams and check the total.
⛔ After AXAML changes, `dotnet clean` first.

---

## Slice 0 — prerequisites

Three small, independently gateable changes. Cross-file `Inherits`/`Implements` is **not** here (owner
decision 1); its findings live in the spec and are filed as a chip in Task 19.

### Task 1: `ProjectSerializer` stops destroying what it does not understand

The designer's core gesture is *add a file*, which commits a project save on the next Build/F5
(`MainWindowViewModel.cs:3239`, default on). Today that save rebuilds the `.blproj` from a model with
no field for half its contents.

**Files:**
- Modify: `VisualGameStudio.ProjectSystem/Serialization/ProjectSerializer.cs` — `LoadAsync` is `:16-241` (PropertyGroup loop `:37-103`, ItemGroup loop `:106-215`), `SaveAsync` is `:243-374`
- Modify: `VisualGameStudio.Core/Models/BasicLangProject.cs`
- Reference implementation: `VisualGameStudio.ProjectSystem/Services/BlprojReferenceWriter.cs:18-42` — the one IDE mutation that already edits the `.blproj` in place and preserves unknown elements
- Test: `VisualGameStudio.Tests/Serialization/ProjectSerializerPreservationTests.cs` (create)

- [ ] **Step 1: Write the failing tests.** (a) A `.blproj` with `<UseWindowsForms>`,
      `<TargetFramework>net8.0-windows</TargetFramework>`, `<AssemblyName>`, `<Authors>`, `<NetProxy>`
      and a `ProjectItem` carrying metadata survives Load→Save byte-equivalently. (b) A VSIX-shaped
      `<Project Sdk="Microsoft.NET.Sdk">` file (copy `BasicLang.VisualStudio/.../WpfApp/Project.blproj`)
      survives with its `Sdk` attribute, both `<Import>`s, `<ProjectCapability>`, `<ProjectTypeGuids>`
      and `<BasicLangCompile Include="**\*.bas"/>` intact.
- [ ] **Step 2: Implement preserve-on-save**, following `BlprojReferenceWriter`'s `XDocument.Load` →
      targeted edit → BOM-less save shape rather than rebuild-from-model. Preserving unknown elements
      beats enumerating the lost ones — it also protects `<NetProxy>`, `ProjectItem.Metadata` and every
      future element.
- [ ] **Step 3: Gate.** ⛔ Acceptance is **`BasicLang.exe build <proj>` after an IDE save**, never an
      IDE F5 — `BuildService.cs:1087-1091`'s `OutputType == WinExe` fallback masks the breakage from
      inside the IDE. Then the fast subset + `Serialization` and `Services` suites.

### Task 2: The wizard's `TargetFramework` reaches the file; DPI mode is defined

- [ ] `ProjectTemplateService.GenerateProjectFileContent` (`:277-311`) writes
      `options.TargetFramework` — collected at `NewProjectWizardViewModel.cs:415` and currently
      discarded, making the TFM picker decorative.
- [ ] `<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>` in **four** coordinated places
      or it is dropped: `BuildService.GenerateCsprojContent` (~`:1117`), `Program.cs`'s csproj literal
      (~`:706`), `ProjectFile` + its Load (follow `:151-155`), `BasicLangProject` + `ProjectSerializer`.
      ⛔ It is emitted **nowhere** today.
- [ ] Escape compile-item paths at `ProjectTemplateService.cs:328`. ℹ️ **Prospective hardening**, not a
      live defect — every value reaching that line today comes from a closed switch of literal
      filenames (`:399-416`). It becomes live in Task 13, which introduces a user-named item.
- [ ] **Gate:** fast subset + `Services` suite + a CLI build of a project with a non-default TFM.

### Task 3: Close the two remaining silent-C# defaults

- [ ] `BuildService.cs:719` (`default: // csharp`) and `:887-894` (`_ => "csharp"`, which documents
      its own hazard in `<remarks>`) throw instead. Only `ProjectTemplateService.cs:265-275` was
      hardened; neither of these is pinned by any test.
- [ ] Extend `ProjectTemplateBackendMappingTests` (`:25-33`) to cover both.
- [ ] Drop `"msil"` from `winforms-app`'s `SupportedSolutionTypes` (`IProjectTemplateService.cs:386`)
      — that pipeline stops at a `.il` file and the arm has never been exercised.
- [ ] **Gate:** fast subset + `Services` suite.

---

## Slice 1 — a read-only canvas over files that already exist

**Constraint:** zero writes, zero new file type, zero project-system change, zero build change, zero
shell refactor. **Must not** widen `_openDocuments`, add a document type, add a dock region, add a
file extension, touch `JavaScriptEmitter`, or write one byte into a user's file.

### Task 4: The form model, in `BasicLang/Forms/`

**Files:** `BasicLang/Forms/{FormDocument,FormControl,FormControlCatalog,FormGeometry}.cs` (create);
test `VisualGameStudio.Tests/Compiler/FormDocumentTests.cs` (create)

- [ ] **Step 0:** verify `VisualGameStudio.Shell` really can see `BasicLang` types transitively
      (`Shell → ProjectSystem → BasicLang`, no `PrivateAssets`/`ReferenceOutputAssembly="false"` on
      that edge). If it cannot, add the direct reference **here**, before anything depends on it.
- [ ] Ten control kinds, catalog driven by one source-of-truth table (D2's shared grammar).
- [ ] **Serialize-subtree / deserialize-subtree-with-rename from day one**, even though Ctrl+C is not
      wired — nearly free alongside the writer, very expensive to bolt on later.
- [ ] **Gate:** fast subset.

### Task 5: The recognizer — two dialects, reading the shapes that exist (D12)

⛔ **Neither shipped IDE template has an `InitializeComponent`.** `winforms-app` builds every control
inside `Public Sub New()` (`ProjectTemplateService.cs:609-629`); `web-site`
(`ProjectTemplateService.cs:501-540`) has no form and no class and builds the DOM inside `Sub Main()`.
A reader written against `InitializeComponent` alone recovers **zero controls from both fixtures** and
the suite stays green — the exact pass-by-absence this plan exists to avoid.

**Files:** `BasicLang/Forms/Recognizer/{SourceIndex,WinFormsDialect,DomDialect}.cs` (create);
test `VisualGameStudio.Tests/Compiler/FormRecognizerTests.cs` (create)

- [ ] Token-stream reader with a line/column→offset `SourceIndex`. **No lexer change.** It must
      tolerate files that do not fully parse and preserve exact offsets for the writer (D12).
- [ ] `WinFormsDialect` reads **constructor bodies and `InitializeComponent` alike**: declare field →
      construct → set properties → `AddHandler` → `Controls.Add`.
- [ ] `DomDialect` reads `createElement` / `appendChild` / `addEventListener` sequences **in any Sub**.
- [ ] ⛔ **Refuse**, do not ignore: a `Handles` clause (a hard parse error at `Parser.cs:4464`, so the
      file never builds) and a `With` block over a recognised control (`.Prop = value` inside one is
      silently dropped at `IRBuilder.cs:3356`). Both are D9 **Refused**.
- [ ] **Gate — must be able to fail.** Name the exact fixture paths and assert counts, names and
      positions, not "it runs": from the `winforms-app` template's generated `Main.bas`, recover
      exactly the controls `ProjectTemplateService.cs:609-629` constructs; from `web-site`'s
      `Main.bas`, the elements `:501-540` creates. Add the VSIX `WinFormsApp/MainForm.bas` as a third
      fixture for the `InitializeComponent` shape — ⛔ it carries unsubstituted `$safeprojectname$`
      placeholders (`:2`, `:28`), so the fixture must substitute them.

### Task 6: `DesignDiagnostic` + the `BL8xxx` band + `basiclang design --check` (D10)

**Files:** `BasicLang/Forms/DesignDiagnostic.cs` (create); `BasicLang/ErrorFormatter.cs` (modify);
`BasicLang/Program.cs` (modify); test `VisualGameStudio.Tests/Compiler/DesignCheckCliTests.cs` (create)

- [ ] `record DesignDiagnostic(Code, Message, FilePath, Line, Column, IsWarning)` — **collected into a
      list, never thrown**. Model it on `CppDiagnostic` (`ProjectSystem/CppDiagnosticsParser.cs:9-17`),
      which already carries exactly these six fields and is already collected; a separate record
      because `CppDiagnostic` is C++-named and mutable.
- [ ] Render in `CppDiagnostic`'s shape — `{FilePath}({Line},{Column}): {kind} {Code}: {Message}`
      (`FormatNormalized` `:123-130`, printed from `Program.cs:452-457`). ⛔ **Not** the
      `{label}: {Code}: {Message}` shape at `Program.cs:541`/`:1116`: it carries no file, line or
      column, because `NetReferenceDiagnostic` has none.
- [ ] ⛔ Put the code in **both** the message string and the field — `Compiler.cs:372-374` shows the
      pattern. On the C#/JS routes `Program.cs:554` prints only `{label}: {Message}`, so a code in the
      field alone is invisible there.
- [ ] Register the values in `enum ErrorCode` (`ErrorFormatter.cs:14-81`) and fix the stale band
      comment at `:11-12`, which omits BL0xxx/BL6xxx/BL7xxx.
- [ ] `case "design":` in the subcommand switch (`Program.cs:98-145`), shaped like `case "build":`
      (`:116-125`). ⛔ The arm must `return` the handler's int — six sibling arms (`new`, `restore`,
      `add`, `remove`, `list`, `search`) do `return 0` unconditionally regardless of handler return
      type, which would make the verb useless in CI. Exit 0 clean (incl. warnings), 1 on findings; 2
      stays reserved for argument errors.
- [ ] ⛔ Global flag sniffing runs **before** the verb switch (`Program.cs:34-86`). Avoid `--lsp`,
      `--debug-adapter`, `--dap`, `--repl`, `-i`, `--help`, `-h`, `--version`, `-v` —
      `design --check foo.bas -i` would launch the REPL (`:71`).
- [ ] Update `PrintUsage` Commands (`:261-269`) and Examples (`:283-290`).
- [ ] In this slice `--check` validates **recognizer input** (a `.bas` with a recoverable form shape);
      it gains the document formats in Task 9.
- [ ] **Gate:** an `[Category("Integration")]` CLI test using `CliTestHarness.CliPath()` (hard-fails on
      a missing exe) — ⛔ **not** `TemplateBuildSweepTests.FindCompiler()`, which reads a different
      binary and degrades to `Assert.Inconclusive`. Assert exit code **and** that stdout carries the
      file, line and column.

### Task 7: `FormCanvasControl` and the Design∣Code toggle

**Files:** `VisualGameStudio.Shell/Controls/FormCanvasControl.cs` (create — ⛔ **not** in
`VisualGameStudio.Editor`, which cannot see `BasicLang/Forms/`);
`VisualGameStudio.Shell/Views/Documents/CodeEditorDocumentView.axaml` (modify);
`VisualGameStudio.Shell/ViewModels/Documents/CodeEditorDocumentViewModel.cs` (modify)

- [ ] A segmented toggle bound to a new `IsDesignMode` on the **existing** VM. The swap idiom is proven
      in this exact view — `IsVisible="{Binding !IsSplitView}"` (`:42`) — and `x:DataType` is already
      `CodeEditorDocumentViewModel` (`:11`). Clone `IsSplitView`/`SplitOrientation`
      (`CodeEditorDocumentViewModel.cs:46-50`). **No new document type.**
- [ ] `FormCanvasControl : Control` overriding `Render(DrawingContext)`, following `MinimapControl`'s
      pattern. ⛔ **Extract ONE transform object** used by `Render`, hit-testing and selection alike —
      the minimap hand-duplicates its forward/inverse transform three times (`:439-447`, `:386-394`,
      `:835-842`), and a copy with zoom/pan lands clicks on the wrong control with nothing looking
      wrong. ⛔ Do not copy its "cached bitmap": `_cachedBitmap` is never created and `_bitmapDirty`
      never read — four dead fields, zero caching.
- [ ] ⛔ Attach must be **idempotent** (detach-first) and unsubscribe in `OnDetachedFromVisualTree` —
      Dock re-attaches the same control instance on tab drag, float/re-dock and maximize
      (`CodeEditorControl.axaml.cs:762-767`). ⛔ Route any canvas→VM command through the
      `LiveEditor`-style visual-root guard or it double-applies (`CodeEditorDocumentView.axaml.cs:122-130`).
- [ ] **Gate:** ⛔ `dotnet clean` first (AXAML). Fast subset + `Editor`/`Shell` suites. There is no
      `Avalonia.Headless` reference, so the canvas itself is verified by running the IDE — say so; do
      not claim automated coverage.

**Demo:** open the shipped `winforms-app` and `web-site` templates and see the form, produced entirely
by a reader.

---

## Slice 2 — `.blwebform` writes *(owner: web ships first)*

### Task 8: `CompileToCSharpOptimized`

⛔ The C# backend has **no** optimizer-running test helper; C++ and JavaScript both do. Every
`CompileToCSharp` in the suite is a per-fixture non-optimizing copy, so a fixture written the obvious
way is green on IR that never ships.

- [ ] Add it beside `JsTestSupport.CompileOptimized` (`JsTestSupport.cs:119`) — same shape:
      `BuildModule` → `OptimizationPipeline` → `AddStandardPasses` → `Run` → generate.
- [ ] **Gate:** fast subset.

### Task 9: `.blwebform` schema, structure-preserving reader/writer, and the algebra (D2, D9)

Read the spec's *The two documents, by example* first — the schema is specified there.

**Files:** `BasicLang/Forms/Serialization/*` (create);
test `VisualGameStudio.Tests/Serialization/BlWebFormRoundTripTests.cs` (create)

- [ ] XML with `LoadOptions.SetLineInfo` so every diagnostic carries `file(line,col)`.
      Structure-preserving: unknown elements/attributes/comments kept verbatim, deterministic attribute
      order, children in z-order.
- [ ] Implement the D9 tier rules exactly as tabled: **Canon**, **Degraded** (per property — freeze
      that row, round-trip the value unchanged), **Refused** (document-level: damaged region,
      `Handles`, `With`, populated `<Bind Property=>` → `BL8021`, `{res:Key}` → `BL8022`). Unknown
      elements round-trip untouched and are **neither**.
- [ ] `<Literal>` passes through to markup untouched and is read-only on the canvas.
- [ ] `TabIndex` is written on every control, defaulting to document order on creation.
- [ ] **Test the algebra explicitly:** a no-op patch writes **nothing**; round-trip is byte-identical;
      `Read∘Apply == Apply∘Read`.
- [ ] Extend `design --check` to accept `.blwebform`.
- [ ] **Gate:** fast subset + `Serialization` suite.

### Task 10: Register the extensions; skip them on **both** compile routes (D11)

⛔ A new source-ish extension must land at **11+ independent sites**; this knowledge is duplicated, not
shared.

- [ ] `FileExtensions.cs:18` `SourceExtensions`. ⛔ Keep both extensions **out of**
      `ProjectFile.BasicLangSourceExtensions` (`:81-82`) and `ModuleResolver.SupportedExtensions`
      (`:19`) — the two copies of the same list.
- [ ] Skip both extensions on **both** routes, with `BL8001` *"a form document is not a program; build
      the project"*, mirroring the `.bli` message at `Compiler.cs:218-220`:
      - `CompileProjectFiles` — beside the C-family denylist (`Compiler.cs:360-361`). Without it a form
        document reaches `new Lexer(processedSource)` (`:547`) ungated.
      - `CompileFile` — which gates on nothing but `.bli` (`Compiler.cs:216`), so
        `BasicLang.exe LoginForm.blwebform --target=csharp` sends XML to the lexer. Also reached by
        `Debugger/DebugSession.cs:190`, `:209`.
- [ ] Exclude both from `IsEntryLikeFile` (`Compiler.cs:477-482`), which today excludes only
      `.mod/.cls/.class/.bli` and would treat a form document as a candidate to hold `Main`.
- [ ] Decide and document the item-type path: two extension deciders send unknown extensions to
      `Content` (`SolutionExplorerViewModel.cs:1164`, `ProjectService.cs:246`) while two writers are
      extension-blind and write `Compile` (`ProjectTemplateService.cs:328`, `ProjectService.cs:282`).
- [ ] ⛔ Document in the template and in `--check`: the **first** explicit `<Compile>` item turns the
      default glob completely off (`ProjectFile.cs:411`), and `<Compile Include="**\*.blwebform"/>` is
      **silently skipped** (the explicit branch is top-directory-only).
- [ ] Register for the editor surfaces: `LanguageFileTypes.cs`, `TextMateService.cs:42`,
      `HighlightingLoader.cs`, and the Open-File dialog filters (`MainWindowViewModel.cs:2283-2292`).
- [ ] **Gate:** fast subset + two CLI runs — a project build containing a `.bas` and a `.blwebform`
      (asserting the form is not lexed and the build succeeds), and
      `BasicLang.exe LoginForm.blwebform --target=csharp` asserting `BL8001` and exit 1.

### Task 11: The region writer (D1)

**Files:** `BasicLang/Forms/RegionWriter.cs` (create);
`VisualGameStudio.Shell/ViewModels/Documents/CodeEditorDocumentViewModel.cs` (modify);
test `VisualGameStudio.Tests/Compiler/FormRegionWriterTests.cs` (create)

- [ ] Write the two regions specified in D1 — `region="controls"` and `region="init"` — as ordinary
      BasicLang comments with a content `hash`. Two regions, not one: in the shape this matches, the
      user's `Public Sub New()` sits **between** the field declarations and `InitializeComponent`
      (`WinFormsApp/MainForm.bas:14-15` vs `:27-47`), so one contiguous region would swallow it.
- [ ] Implement the D1 recovery policy: hash match → regenerate; hash mismatch → `BL8011`, form
      read-only in the designer, Code view untouched; marker missing/unbalanced/duplicated → `BL8012`,
      never written to. **The designer never silently discards hand-written code.**
- [ ] ⛔ **Never emit `With`** (silent drop). ⛔ **Never emit `Handles`** (hard parse error).
      ⛔ **Never emit `AddHandler` against a DOM receiver** — `TryEventCall`
      (`JavaScriptBackend.cs:2182`) emits `{recv}.add(handler)` unconditionally, so
      `AddHandler el.click, …` emits `el.click.add(H)` → runtime `TypeError`. (`click` resolves on
      `Element` — `dom-core.bli:56`, members are `OrdinalIgnoreCase` per `SymbolTable.cs:111` — so this
      is the event-call rewrite firing on a resolved method, not an `IsNetType` degradation.)
- [ ] ⛔ Names: **type** names must be PascalCase without `_` (`SemanticAnalyzer.cs:2050`, `:7940`);
      control **identifiers** may contain `_` freely, and both shipped templates use camelCase control
      names, so do not require PascalCase there. ℹ️ `Private` fields are correct here — the region and
      the handlers are the same class in the same file (measured; see the spec).
- [ ] ⛔ When the IDE performs the write, go through **`Text`**, not only `TextDocument` — see the traps
      table. Assert the tab goes dirty and that a subsequent save writes the new content.
- [ ] **Gate:** `CompileToCSharpOptimized` **and** a real CLI build of a project whose `.bas` carries
      written regions. stdout is the only valid oracle. Test **both entry points**:
      `BasicLang.exe build X.blproj` and the IDE `BuildService`.

### Task 12: The build-time markup/CSS emitter, and F5 (D6, D7)

⛔ **No asset root exists** — nothing in the build pipeline copies a `.css` or extra `.html` into the
output directory. This is built here, not reused.

**Files:** `BasicLang/Forms/FormAssetEmitter.cs` (create); `BasicLang/JavaScriptEmitter.cs` (modify);
`VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs` (modify)

- [ ] Emit `LoginForm.html` (stable ids, `<body data-form="LoginForm">`) and `LoginForm.css` at
      **build** time from the document — not on designer save, or `basiclang build` on a clean checkout
      produces a page with no markup.
- [ ] ⛔ Write into **the directory the JavaScript emitter was handed**, not one computed afresh: the
      IDE uses `bin\Debug` (`BuildService.cs:623`) and the CLI `bin\Debug\<tfm>` (`Program.cs:488`), and
      reusing the emitter's own `outputDirectory` makes them agree instead of adding a third path.
- [ ] Form pages **always overwrite**. `index.html` (`JavaScriptEmitter.cs:109-114`) and `package.json`
      (`:143-148`) remain the only never-overwrite files and stay untouched.
- [ ] `Main()` dispatches on `doc.body.getAttribute("data-form")` (`dom-core.bli:49`). One generated
      top-level name, project-wide — collision-checked once by `--check` with `BL8031`.
- [ ] **F5 opens `/<StartupForm>.html`, not `/`.** `WebPreviewServer.cs:162` maps only the bare root to
      `index.html` (404 at `:165-170`), and F5 hands the browser the root URL
      (`MainWindowViewModel.cs:4181`), so generated pages are unreachable today. Appending the page name
      needs **no** exception to the never-overwrite rule. Both F5 (`:3847`) and Ctrl+F5 (`:4097`) route
      through `StartJavaScriptPreviewAsync` (`:4144-4182`).
- [ ] **Gate:** ⛔ exit 0 is **not** a gate when the deliverable is files — copy `AssertSiteWasWritten`
      (`TemplateBuildSweepTests.cs:200-218`): assert the files exist, carry the expected ids, and
      contain **no** unlowered BasicLang (`End Sub`) or unsubstituted placeholders. Assert `index.html`
      is byte-unchanged. Then F5 and confirm in a browser.

### Task 13: Creating a form

Nothing in Slices 0–1 creates a form; without this the feature can read, write and gate documents that
nothing produces, and the `.bas` + `.blwebform` pair is never exercised by the path a user takes.

- [ ] A "New Form" item in the Solution Explorer add-item flow, modelled on
      `SolutionExplorerViewModel.ConfirmNewItemAsync` (`:1096-1118`) — which adds the item **and** calls
      `SaveProjectAsync` (`:1114`). ⛔ Do **not** model it on `ProjectService.AddFileToProjectAsync`
      (`:241-254`), which mutates the model and never writes.
- [ ] It creates the pair: a minimal `.blwebform` and a `.bas` with an empty class and two empty
      regions.
- [ ] ⛔ This is what makes `ProjectTemplateService.cs:328`'s unescaped compile-item emission live
      (Task 2) — a form named with `&` or `"` must not produce a malformed `.blproj`. Test it.
- [ ] ⛔ Three Solution Explorer tree builders with three different filters
      (`SolutionExplorerViewModel.cs:177-249`, `:348-352` hardcoded whitelist, `:766-815`). A form added
      only to the project-items path **vanishes** when the project opens as part of a solution.
- [ ] **Gate:** fast subset + `Shell` suite + create-then-build through both entry points.

### Task 14: Toolbox and property grid

- [ ] Build the property grid by **extracting** the Settings dialog's typed-row editor —
      `SearchableSettingItem` + `SettingControlKind` (`SettingsViewModel.cs:21-116`) and its
      `ItemsControl` template (`SettingsDialog.axaml:140-207`), **already duplicated** at `:266-330`.
      Extract one reusable row control rather than authoring a third copy. Use the already-referenced
      `Avalonia.Controls.DataGrid` 11.3.13.
- [ ] Reuse `VisualGameStudio.Editor/Controls/ColorPickerPopup.cs:15` for colour rows, factoring out its
      editor-specific state (`Line`, `ColorTextStartOffset/EndOffset`, `ColorMatchKind`).
      ⛔ `SettingControlKind.ColorPicker` is a declared-but-never-rendered dead arm, not a precedent.
      Avalonia 11.3 base ships no ColorPicker and no font dialog — price every other type editor as
      hand-built.
- [ ] Degraded rows render frozen with their reason string (D9).
- [ ] Edits commit on **focus-loss/Enter**, not per keystroke (D13).
- [ ] ⛔ Pick identifiers that are not `WebView*` — taken by the extension-host HTML source document
      type across Core/Shell/DockFactory/ViewLocator.
- [ ] ⛔ A designer setting added to the Settings dialog must be registered in
      `CodeEditorDocumentView`'s static ctor (`:24-47`) or the settings contract test will not cover it.
- [ ] **Gate:** ⛔ `dotnet clean` (AXAML), fast subset + `Shell`/`Dialogs` suites, then run the IDE.

### Task 15: A missing handler becomes a hard error (D8)

⛔ Today nothing checks it, and the accidental guarantee is punctuation-dependent: `AddressOf OnClick`
on a deleted handler gives **no diagnostic**; `AddressOf On_Click` gives *"Undefined identifier"*.

- [ ] In the `AddressOf` arm (`SemanticAnalyzer.cs:7583-7586`), when `GetNodeSymbol(operand)` is null,
      **error** rather than falling back to `CreatePointerType`. Gate the pointer fallback on the
      operand's type actually being a pointer. ⛔ Do **not** narrow `IsNetType` globally — that would
      fail programs across the suite.
- [ ] Give `Visit(AddHandlerStatementNode)` (`:6172-6179`, a two-statement stub; its `RemoveHandler`
      twin at `:6181-6188` is identical) real validation: the event expression must resolve to
      `SymbolKind.Event` or a `TypeKind.Delegate` member, and the handler's delegate type must match via
      the existing `GetDelegateParameterTypes` (`:5987-6000`).
- [ ] **Gate: FULL SUITE.** This is `SemanticAnalyzer` and it tightens an error path — one of only two
      places in this plan where the full suite is required.

---

## Slice 3 — `.blform` writes (WinForms)

### Task 16: `.blform` schema, reader/writer, and algebra

D2 makes two formats a deliberate decision; this is the `.blform` half of Task 9 and is the same size.
Without it Task 18 has no input document to write from.

- [ ] Schema per the spec's worked example: absolute `X`/`Y`/`Width`/`Height`, `Anchor`/`Dock`, no
      `<Literal>`.
- [ ] Same structure-preserving reader/writer, same D9 tiers, same algebra tests as Task 9.
- [ ] Extend `design --check` to accept `.blform`.
- [ ] **Gate:** fast subset + `Serialization` suite.

### Task 17: The WinForms control catalog and its CI gate

⛔ WinForms has **no type metadata at any layer**, so the catalog is unfalsifiable hand-written data.
The gate *is* the type system.

- [ ] Catalog as a source-of-truth table modelled on `ProjectTemplateBackendMappingTests.cs:25-33` —
      *"add a row, never widen the default"* — with a completeness guard that **fails** on a missing row.
- [ ] `[Category("Integration")] [TestFixture] [NonParallelizable]`: generate a project containing
      **every** catalog control with **every** property set and require the real CLI to exit 0.
      ⛔ Drive the cases from the catalog via `TestCaseSource`, **not** a hand-written `[TestCase]` list.
- [ ] Prefer `Native.CSharpRun.CompileAndRun` (in-process Roslyn, no SDK gate) for the per-control
      "csc accepts it" leg; keep one real CLI smoke on top.
- [ ] ⛔ **Do not gate a modal dialog on UIAutomation.** It cannot query a thread blocked in a modal
      message loop and returns a false negative — measured 2026-09-11, three runs disagreed. Use Win32
      `EnumWindows`/`EnumChildWindows` (a MessageBox is window class `#32770`).
- [ ] ⛔ Budget for contention: Integration fixtures are `[NonParallelizable]` because each saturates a
      core. Re-run failures in isolation before investigating.
- [ ] **Gate:** full suite.

### Task 18: WinForms region writing, and the VSIX template becomes canonical

**Owner decision 3:** the VSIX shape is canonical. It is what the designer generates and the only
WinForms shape proven to work end to end.

- [ ] Write the regions in the shipped order: declare field → construct → set properties →
      `AddHandler … AddressOf` → `Controls.Add` (`WinFormsApp/MainForm.bas:14-15`, `:41-46`).
- [ ] ⛔ Geometry **fans in** on WinForms: `X="96" Y="80"` must emit **one** statement,
      `btnLogin.Location = New Point(96, 80)`. `Location` returns a `Point` **struct**, so
      `btnLogin.Location.X = 96` fails at `csc` — and BasicLang will not catch it, because WinForms
      member access degrades to `Object` with no diagnostic. (The exact csc code depends on whether the
      optimizer inlines the temp emitted at `CSharpBackend.cs:3629-3637`; do not assert a specific code
      without measuring it.)
- [ ] **Promote the VSIX shape and retire the divergence.** Today: the VSIX ships `Program.bas` +
      `MainForm.bas`; the IDE ships one `Main.bas` with no `InitializeComponent` and handler
      `OnButtonClick` (`ProjectTemplateService.cs:585-636`); the CLI `TemplateEngine` has **no WinForms
      template at all**. Bring the IDE template and a new CLI template into line with the VSIX shape.
      ⛔ Task 5's recognizer fixtures are keyed to the *current* IDE template — update them in the same
      change or they silently stop testing what ships.
- [ ] ⛔ **Add it to the sweep.** The canonical template has had **zero build coverage** its whole life;
      `ProjectTemplates.All` is hard-coded and `TemplateBuildSweepTests`' cases are hand-written
      `[TestCase]` strings, so it gains nothing automatically. Add the IDE↔CLI equivalence case too
      (`TemplateBuildSweepTests.cs:168-191`, `:269-290`) — those two rosters are separate
      implementations and only that test stops them drifting.
- [ ] **Gate:** full suite + `BasicLang.exe build` + the IDE build path.

**The target emission, measured — match it exactly.** The proven build produced:

```csharp
public class MainForm : Form {
    private Label lblMessage;                          // Private is fine: same class, same file
    private void InitializeComponent() {
        this.Text = "WinFormsProbe";                   // Me. → this.
        lblMessage.Location = new Point(20, 20);       // ONE statement — the geometry fan-in
        btnClick.Click += btnClick_Click;              // AddHandler/AddressOf → +=
        this.Controls.Add(btnClick);
    } }
```

with `[STAThread]` added automatically, namespace `GeneratedCode`, and `#line` directives mapping every
statement back to the `.bas` — so breakpoints in the generated region land on the user's file.

---

## Slice 4 — closeout

### Task 19

- [ ] Full suite, both entry points. Baseline 5826 / 4 plus this plan's additions.
- [ ] Refresh the `IDE\` drop: `robocopy <Shell bin> IDE /E` — ⛔ **never `/MIR`**, and verify against
      `IDE/BasicLang.exe new --list`, never timestamps. ⛔ `IDE/lib/js/dom-core.bli` is load-bearing.
- [ ] Update `docs/HANDOFF.md` and `CLAUDE.md`. ⛔ Do **not** touch
      `docs/MULTI_FILE_SYSTEM_PLAN.md:21` — owner decision 2 keeps `.frm` reserved for a user-authored
      form file; it is not obsoleted by `.blform`.
- [ ] File as separate chips, not fixed here:
      - **Cross-file `Inherits`/`Implements`** — both broken, measured (`Unknown base class` /
        `Unknown interface`, exit 1), same cause at `SemanticAnalyzer.cs:4569`/`:4598`. Out of this plan
        by owner decision 1. The spec's *Cross-file resolution* section carries the full diagnosis and
        the fix shape (route through `ResolveTypeName`, add the `InterfaceNode` arm, and do **not**
        touch `CollectExportedSymbols`). ⚠ Affects any user with a multi-file class hierarchy.
      - **`With` IR drop** (`IRBuilder.cs:3356`, `:2866`) and **`With` over a .NET receiver**
        (`SemanticAnalyzer.cs:6939`).
      - **`Overrides` never validated** (zero `Overrid` matches in `SemanticAnalyzer.cs`).
      - **`ClearIncludedFiles` has zero call sites** (`Preprocessor.cs:568`).
      - ⛔ Do **not** file the C++ `Friend`-field gap — `IRBuilder.MapAccessModifier` (`:948-957`)
        collapses `Friend` to `Private` before any `IRField` is built, so it is unreachable and the chip
        would not reproduce.

---

## Out of scope for v1

Menus/toolbars/status bars · modal dialogs and `DialogResult` · data binding (slot reserved, emits
nothing) · localization/resources (slot reserved) · validation (**no slot reserved** — added later as
an optional element, which unknown-element round-tripping makes non-breaking) · multi-form navigation
and MDI · WYSIWYG rendering · `.frm` (a separate, user-authored form file, reserved and untouched).
Reasons are in the spec's *Non-goals* table.

**Decided now:** the designer never edits `Program.bas`, and the startup form is a **project**
property, not a form property.
