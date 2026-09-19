# Component Tray Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Non-visual components (Timer, ToolTip, ErrorProvider, BackgroundWorker; Timer on the web) live in `<Components>`, show in a tray under the canvas, are selectable/editable/deletable/undoable, and are emitted into the designer regions with the same rules as controls.

**Architecture:** A component is a `FormControl` with `Geometry == null`, no children and no tab index, held in `FormDocument.Components : List<FormControl>` (was `List<XElement>`); its catalog row says `IsComponent`. Every walker chooses its lists explicitly (spec §2). The reader is the only place the invariant is enforced; the writer patches `<Components>` in place like `<Controls>`; the region writer emits components before controls with qualified type names and no `Controls.Add`; the web Timer is a `setInterval` handle from a catalog template.

**Tech Stack:** C# / .NET 8, NUnit, Avalonia 11 headless (`[AvaloniaTest]` + Skia), the real `BasicLang.exe` CLI + Roslyn `WinFormsCompile` + node for gates.

**Spec:** `docs/superpowers/specs/2026-09-19-component-tray-design.md` — every design decision and its measurement (M1–M9) is there; this plan does not repeat the why.

**Repo rules that bite here** (from CLAUDE.md, re-stated because each one has already caused a defect):
- Use Read/Edit/Write/Grep/Glob for files. Never PowerShell `Get-Content`/`Set-Content` on repo files. Commit messages via a file + `git commit -F`.
- `dotnet clean VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release` after ANY `.axaml` change, before building.
- Never emit `With`, `Handles`; geometry fans in; handlers precede the init region on the web.
- A catalog row is unfalsifiable without csc: the sweep is the gate. Add rows; never hand-write a `[TestCase]` list.
- "Passed!" is not passed — read the totals. A passing test prints nothing at normal verbosity.
- Mutation-kill every new test (flip the code, watch the test fail, revert) before claiming it.

**Test commands** (from the repo root, PowerShell):
```powershell
# one fixture
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --nologo -v q --filter "FullyQualifiedName~<FixtureName>"
# the fast subset (~2-5 min)
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --nologo -v q --filter "TestCategory!=Integration"
# the csc sweep (Integration, ~3 min)
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --nologo -v q --filter "FullyQualifiedName~WinFormsCatalogSweepTests"
```

---

## File structure

| File | Responsibility in this feature |
|---|---|
| `BasicLang/Forms/FormDocument.cs` | `Components` becomes `List<FormControl>`; `AllComponents()`; `FindById`/`ListContaining` cover both lists |
| `BasicLang/Forms/FormControlCatalog.cs` | `FormControlDef.IsComponent`, `WinFormsEventArgs`, `WebScript` (+ `FormWebScript` record), `SupportsTarget`, `FormSchematic.Component`, the four rows |
| `BasicLang/Forms/DesignDiagnostic.cs` | `BL8020 ComponentMisplaced` |
| `BasicLang/Forms/Serialization/FormDocumentReader.cs` | reads `<Components>` children as components; refuses misplacement; duplicate ids across lists |
| `BasicLang/Forms/Serialization/FormDocumentWriter.cs` | `ApplyComponents`; `Create` writes components; component elements carry no geometry/TabIndex |
| `BasicLang/Forms/RegionWriter.cs` | components first in both regions; qualified types; no `Controls.Add`; web template; checks walk components |
| `BasicLang/Forms/FormHandlers.cs` | stub `e` type from the row |
| `BasicLang/Forms/FormRetarget.cs` | components cross with the same rules; `Create` path no longer drops them |
| `VisualGameStudio.Shell/ViewModels/Designer/FormPlacement.cs` | component kinds go to `Components` |
| `VisualGameStudio.Shell/ViewModels/Designer/FormToolboxViewModel.cs` | "Components" category; glyph |
| `VisualGameStudio.Shell/ViewModels/Designer/FormPropertyGridViewModel.cs` | no TabIndex row for a component |
| `VisualGameStudio.Shell/ViewModels/Designer/FormTrayViewModel.cs` (new) | the tray's items, rebuilt from the document and the selection |
| `VisualGameStudio.Shell/ViewModels/Documents/CodeEditorDocumentViewModel.cs` | `Tray`, rebuild on revision/sync; paste routes components |
| `VisualGameStudio.Shell/Views/Documents/CodeEditorDocumentView.axaml` (+ `.axaml.cs`) | the tray strip under the canvas; drop, double-click, Delete |
| Tests (new): `Compiler/FormComponentDocumentTests.cs`, `Compiler/FormComponentEmissionTests.cs`, `Compiler/FormTrayTests.cs`, `Shell/FormTrayViewTests.cs`, `Compiler/FormComponentAcceptanceTests.cs` | one fixture per seam, named for what it proves |
| Tests (modified): `FormCatalogCoverageTests`, `WinFormsCatalogSweepTests`, `FormCanvasRenderTests`, `FormPlacementTests`, `FormPropertyGridTests`, `FormRetargetTests`, `FormHandlerPlanTests`, `FormDocumentTests` | each gate learns the component shape |

---

## Commit 1 — the format and the emission

### Task 1: The model holds components as controls without a place

**Files:**
- Modify: `BasicLang/Forms/FormDocument.cs:75-77` (Components), `:84-113` (AllControls/FindById/ListContaining)
- Test: `VisualGameStudio.Tests/Compiler/FormComponentDocumentTests.cs` (create)

- [x] **Step 1: Write the failing tests**

```csharp
using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>Task 25 — a component is a FormControl with no place: model, reader, writer.</summary>
[TestFixture]
public class FormComponentDocumentTests
{
    [Test]
    public void FindById_AndListContaining_SeeComponents_BecauseTheyShareTheFieldNamespace()
    {
        var doc = new FormDocument { Target = FormTarget.WinForms, Name = "F" };
        var tmr = new FormControl { Kind = "Timer", Id = "tmr" };
        doc.Components.Add(tmr);

        Assert.Multiple(() =>
        {
            Assert.That(doc.FindById("tmr"), Is.SameAs(tmr), "a component id is a field name like any control's");
            Assert.That(doc.ListContaining(tmr), Is.SameAs(doc.Components), "Delete and Cut remove through this");
            Assert.That(doc.AllControls(), Is.Empty, "the visual tree is unchanged");
            Assert.That(doc.AllComponents(), Is.EqualTo(new[] { tmr }));
        });
    }
}
```

- [x] **Step 2: Run to verify it fails** — `--filter "FullyQualifiedName~FormComponentDocumentTests"` → compile error: `Components` is `List<XElement>`, no `AllComponents`.

- [x] **Step 3: Implement** in `FormDocument.cs`:

```csharp
/// <summary>
/// The tray (Task 25): non-visual components, in document order. Each is a <see cref="FormControl"/>
/// with NO geometry, NO children and NO tab index — the reader is the one place that invariant is
/// enforced (a component element never acquires them), and every walker chooses explicitly whether
/// it visits this list. ⚠ Was a List of raw XElements while the slot was reserved.
/// </summary>
public List<FormControl> Components { get; } = new();

public IEnumerable<FormControl> AllComponents() => Components;

public FormControl? FindById(string id) =>
    AllControls().Concat(Components).FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));
```
and in `ListContaining`, before `return null;`: `if (Components.Contains(control)) return Components;`.
Fix the two compile errors this causes: `FormDocumentReader.cs:162-164` (temporarily `break;` — Task 3 replaces it) and `FormRetarget.cs:218` (temporarily remove the AddRange — Task 9 restores it properly). `FormDocumentWriter.Create` is untouched (it never read the list).

- [x] **Step 4: Run the fixture and `FormDocumentTests`, `FormDocumentRoundTripTests`, `BlFormRoundTripTests`** → all pass (no fixture has a non-empty `<Components>` yet).

### Task 2: The catalog says what a component is, and four rows say which exist

**Files:**
- Modify: `BasicLang/Forms/FormControlCatalog.cs:267-364` (schematic, record, SupportsTarget), `:440-662` (rows)
- Modify: `VisualGameStudio.Shell/ViewModels/Designer/FormToolboxViewModel.cs:52-79` (category), `:86-120` (glyph)
- Modify: `VisualGameStudio.Tests/Shell/FormCanvasRenderTests.cs:77-79` (exclude components), `VisualGameStudio.Tests/Compiler/FormCatalogCoverageTests.cs` (add)
- Test: `FormCatalogCoverageTests.cs`, `FormToolboxGlyphTests.cs` (existing, catalog-driven)

- [x] **Step 1: Write the failing tests** in `FormCatalogCoverageTests`:

```csharp
/// <summary>Task 25: the four components the brief names, as component rows.</summary>
[Test]
public void TheTrayComponentsAreCatalogRows_MarkedNonVisual()
{
    foreach (var kind in new[] { "Timer", "ToolTip", "ErrorProvider", "BackgroundWorker" })
    {
        var def = FormControlCatalog.Find(kind);
        Assert.That(def, Is.Not.Null, kind);
        Assert.That(def!.IsComponent, Is.True, $"'{kind}' must be a component or the canvas would try to place it");
        Assert.That(def.SupportsTarget(FormTarget.WinForms), Is.True, kind);
        Assert.That(def.Schematic, Is.EqualTo(FormSchematic.Component), kind);
    }
}

/// <summary>
/// ⛔ Common(...) bakes Visible/ForeColor/BackColor into a row. A component has none of them; the
/// row would compile green through BasicLang and fail only at csc.
/// </summary>
[Test]
public void AComponentRow_CarriesNoControlOnlyProperties()
{
    var offenders = FormControlCatalog.All
        .Where(d => d.IsComponent)
        .SelectMany(d => d.Properties.Select(p => (d.Kind, p.Name)))
        .Where(x => x.Name is "Visible" or "ForeColor" or "BackColor")
        .ToList();

    Assert.That(offenders, Is.Empty, string.Join(", ", offenders));
}

/// <summary>⛔ The C# backend imports System.Threading whenever the body says "Thread"; a bare Timer is then CS0104. Every component type is qualified.</summary>
[Test]
public void EveryComponentRow_QualifiesItsWinFormsType()
{
    var bare = FormControlCatalog.All
        .Where(d => d.IsComponent && d.WinFormsType != null && !d.WinFormsType.Contains('.'))
        .Select(d => d.Kind)
        .ToList();

    Assert.That(bare, Is.Empty, "unqualified component types: " + string.Join(", ", bare));
}

[Test]
public void OnlyTheTimer_HasAWebEquivalent_AndItIsScript()
{
    var web = FormControlCatalog.All.Where(d => d.IsComponent && d.SupportsTarget(FormTarget.Web)).ToList();

    Assert.That(web.Select(d => d.Kind), Is.EqualTo(new[] { "Timer" }));
    Assert.That(web[0].HtmlTag, Is.Null, "a Timer is not an element");
    Assert.That(web[0].WebScript, Is.Not.Null);
    Assert.That(web[0].WebScript!.Construct, Does.Contain("{handler}").And.Contain("{Interval}"));
}
```

- [x] **Step 2: Run** `FormCatalogCoverageTests` → compile errors (`IsComponent`, `WebScript`, `FormSchematic.Component`).

- [x] **Step 3: Implement the catalog.** In `FormControlCatalog.cs`:

Add to `FormSchematic` (after `TableContainer`) FOUR members — `Clock` (Timer), `Hint` (ToolTip), `Alert` (ErrorProvider), `Worker` (BackgroundWorker) — documented as tray/toolbox glyph keys that are never drawn (FormCanvasRenderTests excludes IsComponent rows and pins that Layout never sees one). One per kind, as every control kind has its own: `FormPropertyGridTests.TheToolbox_GroupsContainersAfterCommonControls` requires every toolbox row to wear a distinct mark, and a tray holding a Timer and a ToolTip must not show two of the same thing. Glyphs: `(t)`, `(?)`, `(!)`, `(w)`.

Add the record and the facets:
```csharp
/// <summary>
/// How a script-backed component is built on the web — a kind that is not an element (a Timer is
/// a setInterval handle, measured 2026-09-19 under node). <paramref name="Construct"/> is a template
/// over two placeholder kinds: <c>{handler}</c> (the default-event bind's Sub) and <c>{PropertyName}</c>
/// (the property's document value, or the catalog default when unset).
/// </summary>
public sealed record FormWebScript(string FieldType, string Construct);
```
`FormControlDef` gains, after `WebEvent`:
```csharp
    bool IsComponent = false,
    string? WinFormsEventArgs = null,
    bool WebHandlerTakesEvent = true,
    FormWebScript? WebScript = null)
```
with doc comments: `IsComponent` — no geometry, no children, no tab index, no `Controls.Add`, lives in `<Components>`; `WinFormsEventArgs` — the `e` type of the default event's handler, qualified, null = `EventArgs`; `WebHandlerTakesEvent` — false when the web callback is a plain `Action` (spec M6/M7); `WebScript` — spec §4/§5. Change `SupportsTarget`:
```csharp
FormTarget.Web => HtmlTag != null || WebScript != null,
```

Add the rows at the end of `All` (before the closing `};`), with a header comment pointing at the spec:
```csharp
// ==================================================================
// Task 25 — the component tray. ⛔ No Common(): Visible/ForeColor/BackColor do not exist on a
// component. ⛔ Qualified types: the C# backend imports System.Threading whenever the body
// contains "Thread" (CSharpBackend.cs:520), and the scaffold does not import
// System.ComponentModel. Both measured 2026-09-19; see the spec's M1–M9.
// ==================================================================
new("Timer", "System.Windows.Forms.Timer", null, null, false, new List<FormPropertyDef>
    {
        new("Interval", FormPropertyType.Int, "100"),
        // ⚠ WinForms only: a JS interval cannot exist disabled — wired means running (spec §5).
        new("Enabled", FormPropertyType.Bool, "false", Targets: new[] { FormTarget.WinForms })
    },
    Schematic: FormSchematic.Component, WinFormsEvent: "Tick", WebEvent: "tick",
    IsComponent: true,
    // ⛔ Parameterless callback and the TYPED call: Window.setInterval takes an Action, and the
    // typed route refuses Action(Of DomEvent) (spec M7). `w` is the Window the init region
    // declares whenever a script component exists.
    WebHandlerTakesEvent: false,
    WebScript: new FormWebScript("Integer", "w.setInterval(AddressOf {handler}, {Interval})")),

new("ToolTip", "System.Windows.Forms.ToolTip", null, null, false, new List<FormPropertyDef>
    {
        new("InitialDelay", FormPropertyType.Int, "500"),
        new("AutoPopDelay", FormPropertyType.Int, "5000"),
        new("ReshowDelay", FormPropertyType.Int, "100"),
        new("ShowAlways", FormPropertyType.Bool, "false"),
        new("IsBalloon", FormPropertyType.Bool, "false"),
        new("ToolTipTitle", FormPropertyType.String)
    },
    Schematic: FormSchematic.Component, WinFormsEvent: "Popup",
    IsComponent: true, WinFormsEventArgs: "PopupEventArgs"),

new("ErrorProvider", "System.Windows.Forms.ErrorProvider", null, null, false, new List<FormPropertyDef>
    {
        new("BlinkStyle", FormPropertyType.Enum, "BlinkIfDifferentError",
            new[] { "BlinkIfDifferentError", "AlwaysBlink", "NeverBlink" },
            WinFormsEnumType: "ErrorBlinkStyle"),
        new("BlinkRate", FormPropertyType.Int, "250")
    },
    Schematic: FormSchematic.Component, WinFormsEvent: "RightToLeftChanged",
    IsComponent: true),

new("BackgroundWorker", "System.ComponentModel.BackgroundWorker", null, null, false, new List<FormPropertyDef>
    {
        new("WorkerReportsProgress", FormPropertyType.Bool, "false"),
        new("WorkerSupportsCancellation", FormPropertyType.Bool, "false")
    },
    Schematic: FormSchematic.Component, WinFormsEvent: "DoWork",
    IsComponent: true, WinFormsEventArgs: "System.ComponentModel.DoWorkEventArgs"),
```
⚠ Property NAMES are unverified until Task 5's sweep passes csc — that is the gate, not this list.

Toolbox (`FormToolboxViewModel.cs`): order `IsComponent ? 2 : IsContainer ? 1 : 0`, category `IsComponent ? "Components" : IsContainer ? "Containers" : "Common Controls"`, description for a web script component `control.WebScript != null ? "script" : …` (keep the HtmlTag branch for elements; a WinForms component shows its qualified type verbatim — that is what it is), and `FormSchematic.Component => "(*)"` in `GlyphFor` above the `_ => "?"` arm. Update the `WinFormsType` doc comment at `FormControlCatalog.cs:243` ("Unqualified") to say: unqualified for controls, QUALIFIED for components, and why (spec M10–M12).

Render test (`FormCanvasRenderTests.cs:77-79`): `.Where(d => d.SupportsTarget(FormTarget.WinForms) && !d.IsComponent)` with a comment; add:
```csharp
/// <summary>A component has no place on the canvas: Layout must never produce bounds for one.</summary>
[Test]
public void AComponent_IsNeverLaidOut()
{
    var document = DocumentWith("Button");
    document.Components.Add(new FormControl { Kind = "Timer", Id = "tmr" });
    var laid = FormCanvasTransform.Layout(document);   // use the transform's real entry — check its signature at FormCanvasTransform.cs:320-330
    Assert.That(laid.Select(l => l.Control.Id), Does.Not.Contain("tmr"));
}
```
(Adjust to the actual `Layout` API; the assertion is what matters.)

- [x] **Step 4: Run** `FormCatalogCoverageTests`, `FormToolboxGlyphTests`, `FormCanvasRenderTests`, `FormDocumentTests` → pass. ⚠ `WinFormsCatalogSweepTests` is now RED for the four rows (it forces geometry and `Controls.Add`) — Task 5 fixes that; do NOT commit before Task 5.

### Task 3: The reader reads `<Components>` as components, and refuses misplacement

**Files:**
- Modify: `BasicLang/Forms/DesignDiagnostic.cs:54-74` (band comment), add `ComponentMisplaced = "BL8020"`
- Modify: `BasicLang/Forms/Serialization/FormDocumentReader.cs:131-175`, `:190-213`, `:290-445`
- Test: `FormComponentDocumentTests.cs`

- [x] **Step 1: Write the failing tests**

```csharp
private const string WithComponents = """
    <Form Name="F" Version="1" Width="400" Height="300" Text="F">
      <Controls>
        <Button Id="btn" Text="Go" X="8" Y="8" Width="75" Height="23" TabIndex="0"/>
      </Controls>
      <Components>
        <Timer Id="tmr" Interval="500" Enabled="true" Note="keep me">
          <Bind Event="Tick" Handler="tmr_Tick"/>
        </Timer>
        <ToolTip Id="tip" InitialDelay="300"/>
      </Components>
      <Resources/>
    </Form>
    """;

private static FormFile Read(string xml, string name = "F.blform") =>
    FormDocumentReader.Read(Path.Combine("C:", "forms", name), xml);

[Test]
public void Read_RecoversComponents_AsControlsWithNoPlace()
{
    var file = Read(WithComponents);
    Assert.That(file.IsRefused, Is.False, string.Join("; ", file.Diagnostics.Select(d => d.Format())));

    var tmr = file.Model.Components.Single(c => c.Id == "tmr");
    Assert.Multiple(() =>
    {
        Assert.That(file.Model.Components.Select(c => c.Kind), Is.EqualTo(new[] { "Timer", "ToolTip" }));
        Assert.That(tmr.Geometry, Is.Null);
        Assert.That(tmr.TabIndex, Is.Zero);
        Assert.That(tmr.Properties["Interval"], Is.EqualTo("500"));
        Assert.That(tmr.Binds.Single().Handler, Is.EqualTo("tmr_Tick"));
        Assert.That(tmr.UnknownAttributes["Note"], Is.EqualTo("keep me"), "D9: unknown content round-trips");
        Assert.That(file.Model.Controls, Has.Count.EqualTo(1), "components are not controls");
    });
}

[Test]
public void Read_TreatsLayoutAttributesOnAComponent_AsUnknown_NeverAsGeometry()
{
    var file = Read("""
        <Form Name="F" Version="1">
          <Controls/>
          <Components><Timer Id="tmr" X="10" TabIndex="3"/></Components>
        </Form>
        """);
    var tmr = file.Model.Components.Single();
    Assert.That(tmr.Geometry, Is.Null);
    Assert.That(tmr.TabIndex, Is.Zero);
    Assert.That(tmr.UnknownAttributes.Keys, Is.EquivalentTo(new[] { "X", "TabIndex" }));
}

[Test]
public void Read_RefusesAComponentKindUnderControls_AndAControlKindUnderComponents()
{
    var timerAsControl = Read("""
        <Form Name="F" Version="1">
          <Controls>
            <Timer Id="tmr" X="8" Y="8" Width="1" Height="1" TabIndex="0"/>
          </Controls>
        </Form>
        """);   // the Timer is on line 3 of the literal — SetLineInfo numbers from the first character
    var buttonAsComponent = Read("""
        <Form Name="F" Version="1">
          <Controls/>
          <Components><Button Id="btn"/></Components>
        </Form>
        """);

    Assert.Multiple(() =>
    {
        Assert.That(timerAsControl.IsRefused, Is.True);
        Assert.That(timerAsControl.Diagnostics.Single().Code, Is.EqualTo(DesignCodes.ComponentMisplaced));
        Assert.That(timerAsControl.Diagnostics.Single().Line, Is.EqualTo(3), "the element's own line");
        Assert.That(buttonAsComponent.IsRefused, Is.True);
        Assert.That(buttonAsComponent.Diagnostics.Single().Code, Is.EqualTo(DesignCodes.ComponentMisplaced));
    });
}

[Test]
public void Read_RefusesAComponentSharingAnIdWithAControl()
{
    var file = Read("""
        <Form Name="F" Version="1">
          <Controls><Button Id="x" X="8" Y="8" Width="1" Height="1" TabIndex="0"/></Controls>
          <Components><Timer Id="x"/></Components>
        </Form>
        """);
    Assert.That(file.Diagnostics.Single().Code, Is.EqualTo(DesignCodes.DuplicateControlId),
        "one class, one field namespace");
}
```

- [x] **Step 2: Run** → fails (components empty / no BL8020 / no duplicate).

- [x] **Step 3: Implement.** `DesignDiagnostic.cs`: band comment `BL8020  a component in <Controls>, or a control in <Components> (here)`; constant with doc: a refusal, both would generate code csc rejects. Reader:
  - `case "Components": foreach child → ReadControl(child, target, …, isComponent: true)` → `model.Components.Add`.
  - `ReadControl(..., bool isComponent = false)`: after `definition` lookup, `if (definition.IsComponent != isComponent) { diagnostics.Add(Error(DesignCodes.ComponentMisplaced, isComponent ? $"<{kind}> is a control, not a component; it cannot live under <Components> — it would be constructed with no Controls.Add and never appear." : $"<{kind}> is a component and has no position; move it under <Components>. Emitting Me.Controls.Add({id}) for it does not compile.", …Line(element)…)); return null; }` — return null so the refused element is not modelled (the document is refused anyway).
  - For `isComponent`: `Geometry = null`, `TabIndex = 0`, and the attribute loop treats EVERY name in `FormControlCatalog.StructuralAttributes` except `Id` as unknown (`IsStructural(name)` target-agnostic → `UnknownAttributes`), never as geometry/TabIndex. Children: only `<Bind>` and unknown elements; a catalog kind under a component is an unknown child (no nesting).
  - `CheckDuplicateIds`: iterate `model.AllControls().Concat(model.AllComponents())`.

- [x] **Step 4: Run** the fixture → pass; run `FormDocumentRoundTripTests` + `BlFormRoundTripTests` → still pass.

### Task 4: The writer patches `<Components>` in place, and `Create` writes them

**Files:**
- Modify: `BasicLang/Forms/Serialization/FormDocumentWriter.cs:105-131` (Create), `:143-179` (Apply), `:347-401` (ApplyControl), `:476-539` (ControlElement)
- Test: `FormComponentDocumentTests.cs`

- [x] **Step 1: Write the failing tests** (the D9 algebra over a NON-EMPTY `<Components>`, which no fixture has today):

```csharp
[Test]
public void Algebra_ARoundTripWithComponents_IsByteIdentical()
{
    var file = Read(WithComponents);
    Assert.That(FormDocumentWriter.Write(file), Is.EqualTo(WithComponents));
}

[Test]
public void Algebra_ANoOpPatchWithComponents_WritesNothing()
{
    var dir = Path.Combine(Path.GetTempPath(), "bl-tray-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
        var path = Path.Combine(dir, "F.blform");
        File.WriteAllText(path, WithComponents);
        var file = FormDocumentReader.Read(path, WithComponents);
        Assert.That(FormDocumentWriter.Save(file), Is.False);
    }
    finally { Directory.Delete(dir, true); }
}

[Test]
public void Write_PersistsAComponentEdit_AnAddition_ARemoval_AndAReorder()
{
    var file = Read(WithComponents);
    var tmr = file.Model.Components[0];
    tmr.Properties["Interval"] = "250";
    file.Model.Components.Add(new FormControl { Kind = "ErrorProvider", Id = "err" });
    file.Model.Components.Remove(file.Model.Components.Single(c => c.Id == "tip"));
    file.Model.Components.Reverse();   // err, tmr

    var text = FormDocumentWriter.Write(file);
    var back = Read(text).Model;

    Assert.Multiple(() =>
    {
        Assert.That(back.Components.Select(c => c.Id), Is.EqualTo(new[] { "err", "tmr" }));
        Assert.That(back.Components[1].Properties["Interval"], Is.EqualTo("250"));
        Assert.That(back.Components[1].UnknownAttributes["Note"], Is.EqualTo("keep me"));
        Assert.That(text, Does.Not.Match("<ErrorProvider[^>]*TabIndex"), "no TabIndex is invented for a component");
        Assert.That(text, Does.Not.Match("<ErrorProvider[^>]*\\bX="), "and no geometry");
    });
}

[Test]
public void Write_LeavesAComponentsUnknownTabIndexAlone_OnAnUnrelatedEdit()
{
    // ⛔ The APPLY route, not Create: ApplyControl's SetIntAttributeIfChanged(TabIndex, 0) would
    // rewrite a component's TabIndex="5" (an unknown attribute) to "0" on the first real edit.
    var file = Read("""
        <Form Name="F" Version="1">
          <Controls/>
          <Components><Timer Id="tmr" TabIndex="5" Interval="9"/></Components>
        </Form>
        """);
    file.Model.Components[0].Properties["Interval"] = "10";
    Assert.That(FormDocumentWriter.Write(file), Does.Contain("TabIndex=\"5\"").And.Contain("Interval=\"10\""));
}

[Test]
public void Write_AddsTheComponentsElement_BeforeResources_WhenTheDocumentHadNone()
{
    var file = Read("""
        <Form Name="F" Version="1">
          <Controls/>
          <Resources/>
        </Form>
        """);
    file.Model.Components.Add(new FormControl { Kind = "Timer", Id = "tmr" });
    var text = FormDocumentWriter.Write(file);
    Assert.That(text.IndexOf("<Components>"), Is.LessThan(text.IndexOf("<Resources")));
    Assert.That(Read(text).Model.Components.Single().Id, Is.EqualTo("tmr"));
}

[Test]
public void Create_WritesTheModelsComponents()
{
    var doc = new FormDocument { Target = FormTarget.Web, Name = "F", Layout = new FormLayout() };
    doc.Components.Add(new FormControl { Kind = "Timer", Id = "tmr" });
    doc.Components[0].Properties["Interval"] = "50";
    var text = FormDocumentWriter.Create(doc);
    var back = FormDocumentReader.Read(Path.Combine("C:", "forms", "F.blwebform"), text).Model;
    Assert.That(back.Components.Single().Properties["Interval"], Is.EqualTo("50"));
}

[Test]
public void Algebra_ReadApply_EqualsApplyRead_ForAComponentEdit()
{
    // Edit then write, versus write then re-read then edit — same document (the spec's third law).
    var a = Read(WithComponents); a.Model.Components[0].Properties["Interval"] = "1";
    var b = Read(FormDocumentWriter.Write(Read(WithComponents))); b.Model.Components[0].Properties["Interval"] = "1";
    Assert.That(FormDocumentWriter.Write(a), Is.EqualTo(FormDocumentWriter.Write(b)));
}
```

- [x] **Step 2: Run** → the addition/removal/Create tests fail (write-never).

- [x] **Step 3: Implement.** In `ApplyToDocument`, after `ApplyControls`: `ApplyComponents(root, model)`:
```csharp
private static void ApplyComponents(XElement root, FormDocument model)
{
    var container = root.Element("Components");
    if (container == null)
    {
        if (model.Components.Count == 0) return;   // a document that never had the element keeps not having it
        container = new XElement("Components");
        InsertPreservingIndent(root, container, before: root.Element("Resources"));
    }
    ApplyControlList(container, model.Components, isComponent: true);
}
```
`ApplyControlList(container, controls, bool isComponent = false)` passes the flag to `ApplyControl`; `ApplyControl(element, control, isComponent)`: skip the TabIndex write and the geometry switch when `isComponent`; `ControlElement(control, isComponent)`: no geometry, no `TabIndex` attribute, no children. `Create`: replace `root.Add(new XElement("Components"))` with a `<Components>` element holding `ControlElement(c, isComponent: true)` per component (an empty element when there are none, so the shape of a new document is unchanged). ⚠ Keep the unknown-element guard: `ApplyControlList` only touches elements the catalog knows — a `<Timer>` under `<Components>` is known, so it is ours.

- [x] **Step 4: Run** the fixture, both round-trip fixtures, `FormRetargetTests` → pass.

### Task 5: The region writer emits components — and the sweep gates every row through csc

**Files:**
- Modify: `BasicLang/Forms/RegionWriter.cs:368-415` (GenerateControls/GenerateInit), `:219-306` (checks), `:700-708` (DeclaredType)
- Modify: `BasicLang/Forms/FormHandlers.cs:149-152` (stub signature)
- Modify: `VisualGameStudio.Tests/Compiler/WinFormsCatalogSweepTests.cs:133-214` (component shape)
- Test: `VisualGameStudio.Tests/Compiler/FormComponentEmissionTests.cs` (create)

- [x] **Step 1: Write the failing tests**

```csharp
[TestFixture]
public class FormComponentEmissionTests
{
    private static FormDocument WinFormsWith(params FormControl[] components)
    {
        var doc = new FormDocument { Target = FormTarget.WinForms, Name = "F", Width = 400, Height = 300, Text = "F" };
        doc.Controls.Add(new FormControl { Kind = "Button", Id = "btn", Geometry = new PixelGeometry { X = 8, Y = 8, Width = 75, Height = 23 } });
        doc.Components.AddRange(components);
        return doc;
    }

    private static string Emit(FormDocument doc)
    {
        var scaffold = FormScaffolder.Create("F", doc.Target).CodeText;
        var written = RegionWriter.Write("F.bas", scaffold, doc, "F" + doc.FileExtension);
        Assert.That(written.Refused, Is.False, string.Join("; ", written.Diagnostics.Select(d => d.Format())));
        return written.Text;
    }

    [Test]
    public void WinForms_AComponent_IsDeclaredConstructedSetAndWired_BeforeTheControls_AndNeverAdded()
    {
        var tmr = new FormControl { Kind = "Timer", Id = "tmr" };
        tmr.Properties["Interval"] = "500";
        tmr.Properties["Enabled"] = "true";
        tmr.Binds.Add(new FormBind { Event = "Tick", Handler = "tmr_Tick" });

        var code = Emit(WinFormsWith(tmr));

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("Private tmr As System.Windows.Forms.Timer"));
            Assert.That(code, Does.Contain("tmr = New System.Windows.Forms.Timer()"));
            Assert.That(code, Does.Contain("tmr.Interval = 500"));
            Assert.That(code, Does.Contain("tmr.Enabled = True"));
            Assert.That(code, Does.Contain("AddHandler tmr.Tick, AddressOf tmr_Tick"));
            Assert.That(code, Does.Not.Contain("Controls.Add(tmr)"), "a component is not a control");
            Assert.That(code, Does.Not.Contain("tmr.Location").And.Not.Contain("tmr.TabIndex"));
            Assert.That(code.IndexOf("Private tmr As"), Is.LessThan(code.IndexOf("Private btn As")), "VS's order: components first");
            Assert.That(code.IndexOf("tmr = New"), Is.LessThan(code.IndexOf("btn = New")));
        });
    }

    [Test]
    public void Web_ATimerWithATickHandler_IsASetIntervalHandle()
    {
        var doc = new FormDocument { Target = FormTarget.Web, Name = "F", Layout = new FormLayout() };
        var tmr = new FormControl { Kind = "Timer", Id = "tmr" };
        tmr.Properties["Interval"] = "250";
        tmr.Binds.Add(new FormBind { Event = "tick", Handler = "tmr_Tick" });
        doc.Components.Add(tmr);

        var code = Emit(doc);

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("Private tmr As Integer"));
            Assert.That(code, Does.Contain("Dim w As Window = ::window"), "the typed Window, declared once");
            Assert.That(code, Does.Contain("tmr = w.setInterval(AddressOf tmr_Tick, 250)"));
            Assert.That(code, Does.Not.Contain("getElementById(\"tmr\")"), "a Timer is not an element");
        });
    }

    [Test]
    public void Web_AFormWithNoScriptComponent_DeclaresNoWindow()
    {
        var doc = new FormDocument { Target = FormTarget.Web, Name = "F", Layout = new FormLayout() };
        doc.Controls.Add(new FormControl { Kind = "Button", Id = "btn", Geometry = new GridGeometry() });
        Assert.That(Emit(doc), Does.Not.Contain("As Window"), "byte-for-byte what today's region is");
    }

    [Test]
    public void Web_TheTimerStub_IsParameterless_BecauseSetIntervalTakesAnAction()
    {
        var doc = new FormDocument { Target = FormTarget.Web, Name = "F", Layout = new FormLayout() };
        doc.Components.Add(new FormControl { Kind = "Timer", Id = "tmr" });
        var plan = FormHandlers.PlanDefault(doc, doc.Components[0], FormScaffolder.Create("F", FormTarget.Web).CodeText);
        Assert.That(plan.CodeText, Does.Contain("Private Sub tmr_Tick()"));
        Assert.That(plan.CodeText, Does.Not.Contain("tmr_Tick(e As DomEvent)"));
    }

    [Test]
    public void Web_ATimerWithNoHandler_EmitsOnlyItsField()
    {
        // The field, unconditionally (a user's clearInterval compiles before the handler is wired);
        // no construct line, because setInterval needs a callback. The `Dim w As Window` local IS
        // still declared — it is keyed on "a script component exists", not on "one is wired", and an
        // unused local is the cheaper wrong over a second rule to keep in step.
        var doc = new FormDocument { Target = FormTarget.Web, Name = "F", Layout = new FormLayout() };
        doc.Components.Add(new FormControl { Kind = "Timer", Id = "tmr" });
        var code = Emit(doc);
        Assert.That(code, Does.Contain("Private tmr As Integer").And.Not.Contain("setInterval"));
    }

    [Test]
    public void Web_TheTemplateUsesTheCatalogDefault_WhenThePropertyIsUnset()
    {
        var doc = new FormDocument { Target = FormTarget.Web, Name = "F", Layout = new FormLayout() };
        var tmr = new FormControl { Kind = "Timer", Id = "tmr" };
        tmr.Binds.Add(new FormBind { Event = "tick", Handler = "tmr_Tick" });
        doc.Components.Add(tmr);
        Assert.That(Emit(doc), Does.Contain("w.setInterval(AddressOf tmr_Tick, 100)"));
    }

    [Test]
    public void AComponentPropertyTheTargetLacks_IsSkippedAndWarned_LikeAControls()
    {
        var doc = new FormDocument { Target = FormTarget.Web, Name = "F", Layout = new FormLayout() };
        var tmr = new FormControl { Kind = "Timer", Id = "tmr" };
        tmr.Properties["Enabled"] = "true";
        doc.Components.Add(tmr);
        var written = RegionWriter.Write("F.bas", FormScaffolder.Create("F", FormTarget.Web).CodeText, doc, "F.blwebform");
        Assert.That(written.Diagnostics.Select(d => d.Code), Does.Contain(DesignCodes.PropertyNotOnTarget));
    }

    [Test]
    public void TheHandlerStub_TakesTheRowsEventArgsType()
    {
        var doc = WinFormsWith(new FormControl { Kind = "BackgroundWorker", Id = "bw" });
        var plan = FormHandlers.PlanDefault(doc, doc.Components[0], FormScaffolder.Create("F", FormTarget.WinForms).CodeText);
        Assert.That(plan.CodeText, Does.Contain("Private Sub bw_DoWork(sender As Object, e As System.ComponentModel.DoWorkEventArgs)"));
    }

    /// <summary>Spec §2's "no" row for the markup emitter, pinned: a component has no element.</summary>
    [Test]
    public void TheWebPage_CarriesNoMarkupForAComponent()
    {
        var doc = new FormDocument { Target = FormTarget.Web, Name = "F", Layout = new FormLayout() };
        doc.Controls.Add(new FormControl { Kind = "Button", Id = "btn", Geometry = new GridGeometry() });
        doc.Components.Add(new FormControl { Kind = "Timer", Id = "tmr" });
        var html = FormAssetEmitter.Html(doc, "App.js");
        Assert.That(html, Does.Contain("id=\"btn\"").And.Not.Contain("tmr"));
    }
}
```

- [x] **Step 2: Run** → fail (no declaration / getElementById emitted / EventArgs stub).

- [x] **Step 3: Implement.**
  - `GenerateControls`: `foreach (var c in form.Components) body.Append($"{inner}Private {c.Id} As {DeclaredType(form, c)}")` BEFORE the controls loop. `DeclaredType`: `if (control.Definition is { IsComponent: true, WebScript: { } script } && form.Target == Web) return script.FieldType;` else existing.
  - `GenerateInit`: after caption/size (WinForms) or `Dim doc …` (web), on the web emit `Dim w As Window = ::window` iff `form.Components.Any(c => c.Definition?.WebScript != null)`; then `foreach (var c in form.Components) AppendComponentInit(body, form, c, inner, newline, filePath, diagnostics);` then `AppendSiblings(...)` as today.
  - `AppendComponentInit`: WinForms → `{Id} = New {DeclaredType}()`, then the SAME property loop as `AppendControlInit` (extract it into `AppendProperties(body, form, control, inner, newline, filePath, diagnostics)` and call it from both — DRY, no second copy of the Degraded/IsItemCollection rules), then the same bind loop (extract `AppendBinds`). Web → if `WebScript` is null, nothing; else find the default-event bind (`definition.DefaultEvent(Web)`, case-insensitive); if none, emit nothing; else `{Id} = {Expand(template)}` where `Expand` replaces `{handler}` with the bind's handler and every `{Name}` with `control.Properties[Name]` if present and `Accepts`, else the row's `Default`, else `""`. The web bind loop is NOT run for a script component: its one bind IS the constructor argument, and `tmr.addEventListener` on an Integer is a runtime TypeError.
  - `CheckTargetProperties`, `CheckHandlerOrdering`: iterate `form.AllControls().Concat(form.AllComponents())`.
  - `FormHandlers.Insert` currently takes `(form, codeText, index, init, eventName, handler)` — no control. `PlanDefault` threads `control.Definition` through as a new parameter; then WinForms → `(sender As Object, e As {def?.WinFormsEventArgs ?? "EventArgs"})`; web → `def?.WebHandlerTakesEvent == false ? "()" : "(e As DomEvent)"` (M7: the typed `Window.setInterval` refuses `Action(Of DomEvent)`).
  - ⚠ The qualified names in the field, the constructor and the handler parameter are MEASURED (spec M13: real CLI → csc 0 errors → ran), so no fallback is planned; if the sweep nonetheless goes red on a component row, the failure is the ROW's property or event name, and the fix is the row.
  - Sweep (`WinFormsCatalogSweepTests`): in both `EveryProperty_…` and `TheDefaultEvent_…`, build the control with `Geometry = definition.IsComponent ? null : new PixelGeometry{…}` and add it to `definition.IsComponent ? form.Components : form.Controls`. Add a comment: a component row is gated exactly as a control row; only its place differs.

- [x] **Step 4: Run** `FormComponentEmissionTests`, `FormRegionWriterTests`, `FormHandlerPlanTests` → pass. Then **the csc sweep** (Integration): `--filter "FullyQualifiedName~WinFormsCatalogSweepTests"` → every row incl. the four components passes both the property sweep and the default-event stub. ⚠ If a property name is wrong here (e.g. `ToolTipTitle`), csc says CS1061 — fix the ROW, never the gate.

- [x] **Step 5: Mutation kills** (one build each; revert after): (a) drop the `!IsComponent` exclusion in the sweep → the four rows fail csc on `Controls.Add` (proves the gate reaches them); (b) emit `Controls.Add` for components → `WinForms_AComponent_…` fails; (c) template ignores the default → `Web_TheTemplateUsesTheCatalogDefault` fails; (d) `WinFormsEventArgs` ignored → `TheHandlerStub_…` fails and the BackgroundWorker default-event sweep row still compiles (contravariance — record that this mutant is caught by the unit test, not csc).

- [x] **Step 6: Gate and commit** — fast subset + sweep + `FormCanvasRenderTests`; write the message to a scratch file; `git commit -F`. Message: `feat(designer): Task 25a — components in the model, the document and the regions` with the measured facts and gates. Trailer `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.

---

## Commit 2 — the surface

### Task 6: Placement, toolbox category, property grid

**Files:**
- Modify: `VisualGameStudio.Shell/ViewModels/Designer/FormPlacement.cs:37-59`
- Modify: `VisualGameStudio.Shell/ViewModels/Designer/FormPropertyGridViewModel.cs:125-178`
- Test: `FormPlacementTests.cs`, `FormPropertyGridTests.cs`, `FormToolboxGlyphTests.cs` (add)

- [x] **Step 1: Failing tests**

```csharp
// FormPlacementTests
[Test]
public void PlacingAComponent_LandsInTheTray_AndIgnoresThePoint()
{
    var document = new FormDocument { Target = FormTarget.WinForms, Name = "F", Width = 400, Height = 300 };
    var result = FormPlacement.Place(document, "Timer", 999, 999);
    Assert.Multiple(() =>
    {
        Assert.That(result.Refusal, Is.Null);
        Assert.That(document.Components.Single(), Is.SameAs(result.Control));
        Assert.That(result.Control!.Id, Is.EqualTo("Timer1"));
        Assert.That(result.Control.Geometry, Is.Null);
        Assert.That(result.Control.TabIndex, Is.Zero);
        Assert.That(document.Controls, Is.Empty);
    });
}

[Test]
public void PlacingAWebComponent_NeedsNoLayout_BecauseItHasNoCell()
{
    var document = new FormDocument { Target = FormTarget.Web, Name = "F" };   // no Layout at all
    Assert.That(FormPlacement.Place(document, "Timer", 0, 0).Refusal, Is.Null);
    Assert.That(FormPlacement.Place(document, "ToolTip", 0, 0).Refusal, Does.Contain("not available"));
}

// FormPropertyGridTests
[Test]
public void Rows_ForAComponent_AreNameAndItsCatalogProperties_WithNoTabIndex()
{
    // ⚠ The fixture's helper is Read(string xml, string name = "F.blwebform") — xml FIRST.
    var file = Read("""<Form Name="F" Version="1"><Controls/><Components><Timer Id="tmr"/></Components></Form>""", "F.blform");
    var grid = new FormPropertyGridViewModel();
    grid.Load(file);
    grid.SelectedControl = file.Model.FindById("tmr");
    Assert.That(grid.Rows.Select(r => r.Name), Is.EqualTo(new[] { "Name", "Interval", "Enabled" }));
}

// FormToolboxGlyphTests
[Test]
public void ComponentsSitInTheirOwnCategory_LastLikeVs()
{
    var items = new FormToolboxViewModel { Target = FormTarget.WinForms }.Items;
    Assert.That(items.Where(i => i.Category == "Components").Select(i => i.Kind),
        Is.EquivalentTo(new[] { "Timer", "ToolTip", "ErrorProvider", "BackgroundWorker" }));
    Assert.That(items.Last().Category, Is.EqualTo("Components"));
}
```

- [x] **Step 2: Run** → fail. **Step 3: Implement**: `FormPlacement.Place` — after `SupportsTarget`: `if (definition.IsComponent) { var c = new FormControl { Kind = definition.Kind, Id = NextId(document, definition.Kind) }; document.Components.Add(c); return new FormPlacementResult(c, null); }` (before the web branch: no layout needed). `AddIntrinsicRows`: wrap the TabIndex row in `if (control.Definition?.IsComponent != true)`. **Step 4: Run** → pass.

### Task 7: The tray view model, wired to the document and the selection

**Files:**
- Create: `VisualGameStudio.Shell/ViewModels/Designer/FormTrayViewModel.cs`
- Modify: `CodeEditorDocumentViewModel.cs:176-219` (own it, sync it), add `partial void OnDesignModelRevisionChanged`
- Test: `VisualGameStudio.Tests/Compiler/FormTrayTests.cs` (create; mirror `FormCanvasUndoTests` construction: Moq `IFileService`, `vm.SetContent(...)`, never `vm.Text =`)

- [x] **Step 1: Failing tests**

```csharp
[Test]
public void PlacingAComponent_ShowsItInTheTray_SelectsIt_AndWritesTheDocument()
{
    var vm = OpenWinForms();                       // helper: VM over a scaffolded .blform text
    Assert.That(vm.PlaceControl("Timer", 0, 0), Is.Null);
    Assert.Multiple(() =>
    {
        Assert.That(vm.Tray.Items.Select(i => i.Id), Is.EqualTo(new[] { "Timer1" }));
        Assert.That(vm.Tray.Items[0].Glyph, Is.EqualTo("(*)"));
        Assert.That(vm.PropertyGrid.SelectedControl?.Id, Is.EqualTo("Timer1"));
        Assert.That(vm.Text, Does.Contain("<Timer Id=\"Timer1\""), "it reached the document, so it is undoable");
    });
}

[Test]
public void SelectingATrayItem_GoesThroughTheOneSelectionPath()
{
    var vm = OpenWinForms(); vm.PlaceControl("Timer", 0, 0); vm.PlaceControl("Button", 8, 8);
    vm.Tray.Select(vm.Tray.Items[0]);
    Assert.That(vm.Selection.Primary?.Id, Is.EqualTo("Timer1"));
    Assert.That(vm.Tray.Items[0].IsSelected, Is.True);
}

[Test]
public void DeletingATrayItem_RemovesIt_AndUndoBringsItBack()
{
    var vm = OpenWinForms(); vm.PlaceControl("Timer", 0, 0);
    var tmr = vm.DesignDocument!.Components.Single();
    vm.DeleteControlCommand.Execute(tmr);
    Assert.That(vm.Tray.Items, Is.Empty);
    Assert.That(vm.Text, Does.Not.Contain("<Timer"));
    vm.UndoDesignerEditCommand.Execute(null);
    Assert.That(vm.Tray.Items.Select(i => i.Id), Is.EqualTo(new[] { "Timer1" }));
}
```

- [x] **Step 3: Implement** `FormTrayViewModel : ObservableObject` with `ObservableCollection<FormTrayItem> Items` (`FormTrayItem(FormControl Control, string Id, string Kind, string Glyph)` + observable `IsSelected`), `bool IsVisible => Items.Count > 0`, `Rebuild(FormDocument? doc)`, `Select(FormTrayItem)` → `selection.Set(item.Control)`; it holds the `FormSelection` given at construction and re-marks on `selection.Changed`. In the document VM: `public FormTrayViewModel Tray { get; }` — ⚠ constructed in the CONSTRUCTOR (`Tray = new(Selection)`), not as a property initializer: an initializer cannot reference the instance member `Selection` (CS0236). `SyncDesignerPanels` and `partial void OnDesignModelRevisionChanged(int value)` call `Tray.Rebuild(DesignDocument)`. ⚠ The glyph comes from the toolbox's `GlyphFor` — make it `internal static` and reuse; two glyph tables would drift.

### Task 8: The tray strip in the design view

**Files:**
- Modify: `VisualGameStudio.Shell/Views/Documents/CodeEditorDocumentView.axaml:223-234` (column 1 → two-row grid), `.axaml.cs` (tray drop + double-tap + Delete)
- Test: `VisualGameStudio.Tests/Shell/FormTrayViewTests.cs` (create: an AXAML reachability test like `SolutionExplorerRetargetTests.SolutionExplorerView_Binds…`, and one `[AvaloniaTest]` that puts a component as `SelectedControl` on a real `FormCanvasControl` in a shown `Window`, renders a frame, and presses Delete — mirror `FormCanvasKeyboardTests.Surface`).

- [x] **Step 1: Failing tests**: AXAML contains `Tray.Items`, a `KeyBinding Gesture="Delete"` bound to `DeleteControlCommand` INSIDE the `ComponentTray` element, `Focusable="True"` on that element (parse the AXAML with `XDocument` and assert on the element, not on a substring elsewhere), the `OnTrayItemDoubleTapped` handler name, and the drop wiring (`OnTrayDrop` in the code-behind). The headless test puts a component as `SelectedControl` on a real `FormCanvasControl` in a shown `Window` (mirror `FormCanvasKeyboardTests.Surface`), captures a frame (no exception, no handles), presses Delete and asserts the recorder command received the component — that proves the CANVAS-focus path; the AXAML assertions are what prove the TRAY-focus path exists.
- [x] **Step 3: Implement** AXAML: replace the `FormCanvasControl` element with
```xml
<Grid Grid.Column="1" RowDefinitions="*,Auto">
  <designer:FormCanvasControl Grid.Row="0" … (unchanged bindings) …/>
  <!-- Task 25: the component tray — VS's strip under the surface for things with no position. -->
  <!-- ⛔ Focusable="True" is load-bearing: a KeyBinding fires only while keyboard focus is INSIDE
       the element, and a Border is not focusable by default. Without it the tray's Delete path is
       dead the moment the user has clicked anything but the canvas — complete, tested, unreachable.
       OnTrayItemPressed focuses this Border; FormTrayViewTests asserts the attribute. -->
  <Border Grid.Row="1" x:Name="ComponentTray" IsVisible="{Binding Tray.IsVisible}" Focusable="True"
          Background="{DynamicResource IdePanelBg}" BorderBrush="{DynamicResource IdeBorder}" BorderThickness="0,1,0,0"
          Padding="6,4" DragDrop.AllowDrop="True">
    <Border.KeyBindings>
      <KeyBinding Gesture="Delete" Command="{Binding DeleteControlCommand}" CommandParameter="{Binding PropertyGrid.SelectedControl}"/>
    </Border.KeyBindings>
    <ItemsControl ItemsSource="{Binding Tray.Items}">
      <ItemsControl.ItemsPanel><ItemsPanelTemplate><WrapPanel Orientation="Horizontal"/></ItemsPanelTemplate></ItemsControl.ItemsPanel>
      <ItemsControl.ItemTemplate>
        <DataTemplate x:DataType="designerVm:FormTrayItem">
          <Border Classes="tray-item" Classes.selected="{Binding IsSelected}" Margin="0,0,8,0" Padding="6,3" PointerPressed="OnTrayItemPressed" DoubleTapped="OnTrayItemDoubleTapped">
            <StackPanel Orientation="Horizontal" Spacing="6">
              <TextBlock Text="{Binding Glyph}" FontFamily="Cascadia Code, Consolas, monospace" FontSize="10"/>
              <TextBlock Text="{Binding Id}" FontSize="12"/>
            </StackPanel>
          </Border>
        </DataTemplate>
      </ItemsControl.ItemTemplate>
    </ItemsControl>
  </Border>
</Grid>
```
plus a `Styles` entry for `.tray-item.selected` (accent border). Code-behind: `OnTrayItemPressed` → `vm.Tray.Select(item)` then `ComponentTray.Focus()` (the Border is `Focusable`, so this actually moves keyboard focus and the KeyBinding above becomes live); `OnTrayItemDoubleTapped` → `vm.ActivateControlCommand.Execute(item.Control)`; `AddHandler(DragDrop.DropEvent, OnTrayDrop)` on `ComponentTray` in the constructor → if `e.Data.Contains(FormCanvasControl.ControlKindFormat)` then `vm.TrayDropCommand.Execute(kind)` — the TRAY's own command, never the canvas's `PlaceDroppedControlCommand` (a `FormControlDropRequest` carries no origin, so the canvas path would place a Button at (0,0)); `DragOver` sets Copy only when `FormControlCatalog.Find(kind)?.IsComponent == true`, else None. On the VM: `[RelayCommand] private void TrayDrop(string? kind)` (⚠ the method name IS the command name minus "Command" — `TrayDrop` → `TrayDropCommand`; a mismatch here was a real defect once) publishes BL8019 ("'Button' has a position; drop it on the form, not the tray") for a non-component and otherwise calls `PlaceControl(kind, 0, 0)`; test both branches in `FormTrayTests`, and add "`TrayDropCommand` appears in the code-behind" to the `FormTrayViewTests` reachability assertions. **`dotnet clean` the Shell before building.**

- [x] **Step 5: Gate and commit** — `dotnet clean` Shell; fast subset + `FormCanvas*` + `FormTray*` fixtures. Mutation kills: tray not rebuilt on revision (→ placing test fails); TabIndex row not skipped (→ grid test); component placed into Controls (→ placement test). Commit `feat(designer): Task 25b — the tray: placement, toolbox, property grid, selection, delete, undo`.

---

## Commit 3 — retarget and clipboard

### Task 9: Components cross a retarget with the same rules

**Files:**
- Modify: `BasicLang/Forms/FormRetarget.cs:183-220` (ConvertRoot), `:57-84` (Convert), `:100-149` (ConvertToPair stub loop)
- Test: `FormRetargetTests.cs` (add)

- [x] **Step 1: Failing tests**: a `.blform` with a Timer (Tick bind, Interval, Enabled) and a ToolTip → web: Timer crosses (`tick`, Interval kept, `Enabled` → BL8024), ToolTip → BL8023 naming it, no BL8025 for a component; web → WinForms: Timer crosses (`Tick`); `EveryCatalogKind_Retargets_…` sweep extended to component rows — the source control goes into `Components` when `definition.IsComponent`, and the crossed control is read from `result.Document.Components` (not `.Controls`) for those rows; the loss/crossed assertions are unchanged; the pair's code-behind for a web Timer contains `w.setInterval` and the parameterless stub; `Create` keeps components (Task 4 already).
- [x] **Step 3: Implement**: `ConvertControls(source.Components, Document.Components, "the tray")` (no geometry map entries; `Hoist` for a component drops it with BL8023 — it has no children); `ToCells`/`ToPixels` untouched (they only walk `Controls`); `ConvertToPair`'s stub loop iterates `AllControls().Concat(AllComponents())`. `Convert_LeavesTheSourceUntouched` and the fixed-point tests keep passing.

### Task 10: Copy, cut and paste route a component to the tray

**Files:**
- Modify: `BasicLang/Forms/FormDocument.cs` (`FormClipboard.ToElement`/`FromElement`: no TabIndex for a component; `DeserializeSubtree` unchanged), `CodeEditorDocumentViewModel.cs:405-440` (paste routes `IsComponent` to `Components`; taken ids from both lists; no tab renumber for components)
- Test: `FormDesignerCommandTests.cs` (add): copy a Timer + Button, paste → `Components` has `Timer2`, `Controls` has `Button2`, the Timer's bind handler renamed by convention.

- [x] **Gate and commit** — fast subset + `FormRetargetTests` + `FormDesignerCommandTests`. Commit `feat(designer): Task 25c — components cross a retarget and a paste`.

---

## Commit 4 — run it, then record it

### Task 11: Acceptance — a Timer ticks on both targets

**Files:**
- Create: `VisualGameStudio.Tests/Compiler/FormComponentAcceptanceTests.cs` (`[Category("Integration")]`, `[NonParallelizable]`)

- [x] **WinForms**: scaffold a form named `LoginForm` (the node harness hard-codes `data-form="LoginForm"`; use the same name on both targets), `vm.PlaceControl("Timer", 0, 0)`, set `Interval=1`, `Enabled=true` through the property grid rows — ⚠ the acceptance fixture's `SetProperty` finds controls via `AllControls().Single(...)`; write the Timer's rows by selecting `vm.DesignDocument!.FindById("Timer1")` — `ActivateControlCommand` on the component (creates `Timer1_Tick`), insert `Console.WriteLine("TICK")` + `Timer1.Enabled = False` in the stub, `SaveAsync`, compile with the real CLI (`--target=csharp`), build a driver app exactly as `FormDesignerAcceptanceTests.WinForms_…` does but pumping `Application.DoEvents()` in a loop for up to 2 s; assert `TICK` in the run output.
- [x] **Web**: same through the designer on a `.blwebform`, `Main.bas` with the dispatch, `basiclang build`, then `FormDesignerAcceptanceTests.RunPageUnderNode` — extend its harness with `setInterval: (cb, ms) => { console.log("setInterval " + ms); cb(); return 7; }` and `clearInterval` on `globalThis.window`, installed BEFORE the `import` (the harness sets `globalThis.window = globalThis`, so this REPLACES node's real `setInterval` — a real one would keep the process alive until the 60 s timeout); the two stubs are inert for existing callers. The user's line in the parameterless stub is `Console.WriteLine("TICK")`; assert `TICK`. ⚠ The generated call is the TYPED `w.setInterval`, so a wrong stub signature fails the BUILD here — assert the build exit is 0 with the output attached.
- [x] Both must PASS, not be skipped: read the totals.

### Task 12: Docs, followups, handoff, memory, IDE drop

- [x] Spec `2026-09-11-visual-form-designer-design.md:127`: replace "reserved and empty in v1" with "`<Components>` holds the tray (see 2026-09-19-component-tray-design.md); `<Resources>` is reserved and empty".
- [x] `docs/form-designer-followups.md`: **19** extender properties (ToolTip/ErrorProvider per-control values; web `title`, M9); **20** two compiler gaps user code will meet: `Container`→`IContainer` assignability (M5, with the `Container`-typed field that does work, M14) and a component's `GetToolTip`/`GetError` result refused into a typed variable (M15).
- [x] `CLAUDE.md` form-designer section: one ⛔ bullet — a component is a `FormControl` with no place; walkers pick their lists explicitly; qualified component types because of the `System.Threading` import; the sweep covers component rows in `Components`.
- [x] `docs/HANDOFF.md`: task table (25 done), a short section, gates. Memory file + `MEMORY.md`.
- [x] Full suite on the final binaries (`--no-build` after the last build), compare the failure NAMES against the 8-row baseline; then `robocopy VisualGameStudio.Shell\bin\Release\net8.0 IDE /E` (never `/MIR`), verify `IDE\BasicLang.exe --help` exits 0 and `IDE\lib\js\dom-core.bli` exists; commit the drop separately. (25d: 7155/8/2 of 7165; 25e: 7177/8/2 of 7187; drop from the post-mutant clean build, `--help` and `new --list` both exit 0.)

---

## Commit 5 — what the adversarial review found (added 2026-09-19; spec §7a)

Four finders over the four commits' diff, two skeptics per finding, majority to confirm: 14 raised,
8 confirmed. Tests first, then the fix, then a mutant per test.

### Task 13: One selection path

- [x] `FormTrayTests`: the repro — click Timer1, drop a ToolTip, click Timer1, Delete through the grid's control → the ToolTip survives, the selection empties; a drop highlights the dropped item.
- [x] `CodeEditorDocumentViewModel.SelectInDesigner` sets `Selection` AND the grid; the constructor makes the grid follow `Selection.Changed`; `PlaceControl`, `ActivateControl` and `DeleteControl` go through it.

### Task 14: What the web cannot wire is named

- [x] `FormComponentEmissionTests`: a bind on a non-default event → BL8028, never emitted, never refused by BL8013; a kind with no web row → BL8029; a Degraded template value → default + BL8009; `EmitsOnlyItsField` pins the `Window` local and the not-an-element shape.
- [x] `RegionWriter`: `CheckComponentTargets`, `CheckComponentBinds`, `IsEmittedBind` shared by the emitter and the ordering check; `ExpandWebScript` reports.

### Task 15: "Wired means running" crosses by rule

- [x] `FormRetargetTests`: wired web Timer → WinForms arrives `Enabled=true` + BL8027 and the pair emits `tmr.Enabled = True`; unwired arrives as it was; WinForms wired+enabled → web is BL8027 not BL8024; wired but not enabled → BL8027 "will run"; the fixed-point round trip is byte-identical INCLUDING `Enabled`.
- [x] `FormWebScript.Implies` (`FormImpliedProperty`), Timer row `Implies: ("Enabled", "true")`; `FormRetarget.WiredRunState` / `CrossRunState`; `ConvertProperties` treats the implied property as the wiring, not a loss.

### Task 16: The tests the review found soft

- [x] `FormTrayViewTests`: "draws nothing" is a frame-hash equality; the AXAML gate reads the Delete `CommandParameter`.
- [x] `WinFormsCatalogSweepTests.EveryEnumValue_OfEveryControl_EmitsCSharpThatCscAccepts`: one control per allowed value, one csc compile per kind.
- [x] `FormComponentDocumentTests` no-op Save asserts `IsRefused` first; the "IsSkipped" test renamed to what it can prove.
- [x] Gate: designer fixtures 276/0, Integration 91/0, full suite 7177/8/2 of 7187 (the 8 baseline names); 12 mutants killed; commit `feat(designer): Task 25e — the review's eight`; docs (spec §7a, HANDOFF, CLAUDE.md, followups 21), memory.
