using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 14: the property grid and toolbox.
///
/// <para>⛔ The rows are where D9's tiers stop being a document concept and become something a user
/// can destroy. A <b>Degraded</b> value — one the catalog knows but cannot parse — must be shown,
/// must round-trip unchanged, and must NOT reach a typed editor: binding
/// <c>Checked="maybe"</c> to a toggle would write back <c>False</c> the moment the row rendered,
/// and the user's text would be gone without them touching anything.</para>
/// </summary>
[TestFixture]
public class FormPropertyGridTests
{
    private static FormFile Read(string xml, string name = "F.blwebform") =>
        FormDocumentReader.Read(name, xml);

    private const string WebForm = """
        <WebForm Name="F" Version="1">
          <Controls>
            <CheckBox Id="chk" TabIndex="0" Text="Remember" Checked="true" Enabled="true"/>
          </Controls>
        </WebForm>
        """;

    private const string DegradedForm = """
        <WebForm Name="F" Version="1">
          <Controls>
            <CheckBox Id="chk" TabIndex="0" Text="Remember" Checked="maybe"/>
          </Controls>
        </WebForm>
        """;

    private static FormPropertyGridViewModel GridOver(string xml, string controlId)
    {
        var file = Read(xml);
        var grid = new FormPropertyGridViewModel();
        grid.Load(file);
        grid.SelectedControl = file.Model.FindById(controlId);
        return grid;
    }

    // ==================================================================
    // Task 25 — a component's rows: its name and its catalog properties, nothing positional
    // ==================================================================

    [Test]
    public void Rows_ForAComponent_AreNameAndItsCatalogProperties_WithNoTabIndexAndNoGeometry()
    {
        // ⚠ The helper is Read(string xml, string name) — xml FIRST.
        var file = Read("""
            <Form Name="F" Version="1">
              <Controls/>
              <Components><Timer Id="tmr" Interval="250"/></Components>
            </Form>
            """, "F.blform");
        var grid = new FormPropertyGridViewModel();
        grid.Load(file);
        grid.SelectedControl = file.Model.FindById("tmr");

        Assert.Multiple(() =>
        {
            Assert.That(grid.Rows.Select(r => r.Name), Is.EqualTo(new[] { "Name", "Interval", "Enabled" }),
                "no X/Y/Width/Height/Anchor/Dock, and no TabIndex — a component has none of them");
            Assert.That(grid.Header, Is.EqualTo("tmr"));
            Assert.That(grid.HeaderKind, Is.EqualTo("Timer"));
        });
    }

    // ==================================================================
    // Rows come from the catalog, not from the document
    // ==================================================================

    [Test]
    public void Rows_ComeFromTheCatalog_NotFromWhatTheDocumentHappensToSet()
    {
        // ⛔ Building rows from the document's own attributes would offer exactly the properties
        // already set, and no way to add one that is not.
        var grid = GridOver(WebForm, "chk");

        var expected = FormControlCatalog.Find("CheckBox")!.Properties.Select(p => p.Name).ToList();

        // ⚠ Compared over the CATALOG rows only. The grid now also shows the intrinsic rows VS puts
        // on every control — Name, Col/Row or X/Y/Width/Height, TabIndex — which are fields on the
        // control and its geometry rather than catalog properties. This test is about where the
        // CATALOG rows come from, and that is unchanged: all of them, in catalog order, whether or
        // not the document sets them. The intrinsic rows have their own test below.
        var catalogRows = grid.Rows.Select(r => r.Name).Where(expected.Contains);

        Assert.That(catalogRows, Is.EqualTo(expected),
            "every catalog property, in catalog order — including the ones the document omits");
    }

    /// <summary>
    /// ⛔ The rows VS shows for every control. Without them the grid can style a control but not
    /// place one — there is no way to type an exact X, and dragging is the wrong tool for
    /// "line these three up at 96".
    /// </summary>
    private const string PlacedWebForm = """
        <WebForm Name="F" Version="1">
          <Layout Kind="Grid" Cols="1fr,1fr" Rows="auto"/>
          <Controls>
            <CheckBox Id="chk" Col="1" Row="0" TabIndex="0" Text="Remember"/>
          </Controls>
        </WebForm>
        """;

    private const string WinFormsForm = """
        <Form Name="F" Version="1" Width="400" Height="300" Text="F">
          <Controls>
            <CheckBox Id="chk" X="16" Y="24" Width="120" Height="20" TabIndex="0" Text="Remember"/>
          </Controls>
        </Form>
        """;

    [Test]
    public void Rows_IncludeTheIntrinsicOnes_ShapedToTheGeometryTheControlHas()
    {
        var web = Read(PlacedWebForm);
        var webGrid = new FormPropertyGridViewModel();
        webGrid.Load(web);
        webGrid.SelectedControl = web.Model.FindById("chk");
        var webNames = webGrid.Rows.Select(r => r.Name).ToList();

        var winForms = FormDocumentReader.Read("F.blform", WinFormsForm);
        var winGrid = new FormPropertyGridViewModel();
        winGrid.Load(winForms);
        winGrid.SelectedControl = winForms.Model.FindById("chk");
        var winNames = winGrid.Rows.Select(r => r.Name).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(webNames[0], Is.EqualTo("Name"), "identity first, the way VS orders them");
            Assert.That(webNames, Does.Contain("TabIndex"));

            // ⛔ A web control lives in a grid CELL and has no X/Y at all; a WinForms one has pixels
            // and no cell. Offering the wrong pair would let the user set a number the emitter
            // cannot use — the divergence D9 exists to prevent.
            Assert.That(webNames, Does.Contain("Col").And.Contains("Row"));
            Assert.That(webNames, Does.Not.Contain("X").And.Not.Contains("Width"));

            Assert.That(winNames, Does.Contain("X").And.Contains("Y")
                .And.Contains("Width").And.Contains("Height"));
            Assert.That(winNames, Does.Not.Contain("Col").And.Not.Contains("Row"));
        });
    }

    /// <summary>
    /// ⚠ A control the document never placed has NO geometry, and the grid offers none rather than
    /// inventing one. Showing an editable Col on it would write an attribute the user never asked
    /// for the first time a binding pushed a value.
    /// </summary>
    [Test]
    public void Rows_OfferNoGeometry_ForAControlTheDocumentNeverPlaced()
    {
        var grid = GridOver(WebForm, "chk");
        var names = grid.Rows.Select(r => r.Name).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(names, Does.Contain("Name").And.Contains("TabIndex"));
            Assert.That(names, Does.Not.Contain("Col").And.Not.Contains("X"));
        });
    }

    [Test]
    public void TheNameRow_IsFrozen_BecauseRenamingWouldRewriteTheGeneratedField()
    {
        var grid = GridOver(WebForm, "chk");
        var name = grid.Rows.Single(r => r.Name == "Name");

        Assert.Multiple(() =>
        {
            Assert.That(name.RawValue, Is.EqualTo("chk"), "it shows the control's id");
            Assert.That(name.IsFrozen, Is.True,
                "an editable Name would silently produce a document whose generated field no " +
                "longer matches the code the user wrote against it");
            Assert.That(name.FrozenReason, Is.Not.Null.And.Not.Empty,
                "a frozen row must say WHY — that is the whole point of the tier");
        });
    }

    [Test]
    public void AnIntrinsicRow_WritesThroughToTheModel_AndReportsTheEdit()
    {
        var grid = GridOver(WebForm, "chk");
        var edits = 0;
        grid.Edited += (_, _) => edits++;

        var tabIndex = grid.Rows.Single(r => r.Name == "TabIndex");
        tabIndex.IntValue = 7;

        Assert.Multiple(() =>
        {
            Assert.That(grid.SelectedControl!.TabIndex, Is.EqualTo(7),
                "the row writes the live model, not a copy the canvas never sees");
            Assert.That(edits, Is.EqualTo(1), "and tells the host so the document is written");
        });
    }

    [Test]
    public void AnIntrinsicRow_IgnoresAnUnparseableValue_RatherThanSnappingToZero()
    {
        var file = FormDocumentReader.Read("F.blform", WinFormsForm);
        var grid = new FormPropertyGridViewModel();
        grid.Load(file);
        grid.SelectedControl = file.Model.FindById("chk");

        var x = grid.Rows.Single(r => r.Name == "X");
        x.IntValue = 96;

        // A binding can push mid-edit text; coercing it to 0 would move the control across the form
        // while the user was still typing.
        x.StringValue = "not a number";

        Assert.That(((PixelGeometry)grid.SelectedControl!.Geometry!).X, Is.EqualTo(96),
            "an unparseable value left the model where it was");
    }

    /// <summary>
    /// ⛔ The FORM's own properties, shown when nothing on the surface is selected — which is what
    /// VS does. Clicking the form used to say "No selection" and offer nothing, so a form's caption
    /// and size could only be changed by editing the XML by hand.
    /// </summary>
    [Test]
    public void WithNoControlSelected_TheGridShowsTheFormsOwnProperties()
    {
        var winForms = FormDocumentReader.Read("F.blform", WinFormsForm);
        var grid = new FormPropertyGridViewModel();
        grid.Load(winForms);

        var names = grid.Rows.Select(r => r.Name).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(names, Does.Contain("Name").And.Contains("Text")
                .And.Contains("Width").And.Contains("Height"));
            Assert.That(grid.Header, Is.EqualTo("F"), "the header names the form, not 'No selection'");
            Assert.That(grid.IsEmpty, Is.False);

            // ⛔ The form's name is its CLASS name and the file name must agree with it.
            Assert.That(grid.Rows.Single(r => r.Name == "Name").IsFrozen, Is.True);
        });
    }

    [Test]
    public void TheFormsCaption_IsEditable_AndWritesThrough()
    {
        var winForms = FormDocumentReader.Read("F.blform", WinFormsForm);
        var grid = new FormPropertyGridViewModel();
        grid.Load(winForms);
        var edits = 0;
        grid.Edited += (_, _) => edits++;

        grid.Rows.Single(r => r.Name == "Text").StringValue = "Sign in";

        Assert.Multiple(() =>
        {
            Assert.That(winForms.Model.Text, Is.EqualTo("Sign in"));
            Assert.That(edits, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// ⚠ A web page has a track list, not a client size. Offering Width/Height on one would let the
    /// user set numbers the emitter has nowhere to put.
    /// </summary>
    [Test]
    public void AWebPagesFormRows_AreItsGridTracks_NotAClientSize()
    {
        var web = Read(PlacedWebForm);
        var grid = new FormPropertyGridViewModel();
        grid.Load(web);

        var names = grid.Rows.Select(r => r.Name).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(names, Does.Contain("Cols").And.Contains("Rows").And.Contains("Gap"));
            Assert.That(names, Does.Not.Contain("Width").And.Not.Contains("Height"));
        });
    }

    /// <summary>
    /// ⚠ BEHAVIOUR CHANGED. This asserted that no selection meant an empty grid. It no longer does:
    /// no selection now shows the FORM's properties, which is what VS does and what the owner asked
    /// for — a form whose caption and size could only be changed by editing the XML. The empty case
    /// is now "no document at all", which is the only state with genuinely nothing to show.
    /// </summary>
    [Test]
    public void Rows_AreEmpty_WithNoDocument()
    {
        var grid = new FormPropertyGridViewModel();

        Assert.Multiple(() =>
        {
            Assert.That(grid.Rows, Is.Empty);
            Assert.That(grid.IsEmpty, Is.True);
            Assert.That(grid.Header, Is.EqualTo("No selection"));
        });
    }

    [Test]
    public void SelectingAControlAndThenNothing_ReturnsToTheFormsProperties()
    {
        var file = FormDocumentReader.Read("F.blform", WinFormsForm);
        var grid = new FormPropertyGridViewModel();
        grid.Load(file);
        grid.SelectedControl = file.Model.FindById("chk");

        Assert.That(grid.Rows.Select(r => r.Name), Does.Contain("Checked"),
            "the control's rows while it is selected");

        grid.SelectedControl = null;

        Assert.That(grid.Rows.Select(r => r.Name), Does.Contain("Text").And.Contains("Width"),
            "and the form's rows again once it is deselected");
    }

    [Test]
    public void Rows_OmitAPropertyTheTargetDoesNotHave()
    {
        // ⛔ WinForms RadioButton has no GroupName — measured by csc (CS1061). Offering it would
        // let the user set a value that silently never reaches the generated code.
        var winForms = Read("""
            <Form Name="F" Version="1">
              <Controls><RadioButton Id="rad" X="0" Y="0" TabIndex="0"/></Controls>
            </Form>
            """, "F.blform");

        var grid = new FormPropertyGridViewModel();
        grid.Load(winForms);
        grid.SelectedControl = winForms.Model.FindById("rad");

        Assert.Multiple(() =>
        {
            Assert.That(grid.Rows.Select(r => r.Name), Has.No.Member("GroupName"));
            Assert.That(grid.Rows.Select(r => r.Name), Does.Contain("Checked"),
                "the rest of the control is still offered");
        });
    }

    [Test]
    public void AWebFormStillOffersGroupName()
    {
        var web = Read("""
            <WebForm Name="F" Version="1">
              <Controls><RadioButton Id="rad" TabIndex="0"/></Controls>
            </WebForm>
            """);

        var grid = new FormPropertyGridViewModel();
        grid.Load(web);
        grid.SelectedControl = web.Model.FindById("rad");

        Assert.That(grid.Rows.Select(r => r.Name), Does.Contain("GroupName"),
            "web-only is not the same as gone");
    }

    // ==================================================================
    // Editor selection
    // ==================================================================

    [Test]
    public void EachPropertyTypeGetsItsOwnEditor()
    {
        var grid = GridOver(WebForm, "chk");

        FormPropertyRow Row(string name) => grid.Rows.Single(r => r.Name == name);

        Assert.Multiple(() =>
        {
            Assert.That(Row("Checked").IsCheckBox, Is.True, "Bool");
            Assert.That(Row("Text").IsTextBox, Is.True, "String");
            Assert.That(Row("ForeColor").IsColor, Is.True, "Color");
            Assert.That(Row("Enabled").IsCheckBox, Is.True);
        });
    }

    [Test]
    public void AnEnumRowOffersExactlyTheCatalogsAllowedValues()
    {
        var form = Read("""
            <WebForm Name="F" Version="1">
              <Controls><Label Id="lbl" TabIndex="0" Text="Hi"/></Controls>
            </WebForm>
            """);
        var grid = new FormPropertyGridViewModel();
        grid.Load(form);
        grid.SelectedControl = form.Model.FindById("lbl");

        var row = grid.Rows.Single(r => r.Name == "TextAlign");

        Assert.Multiple(() =>
        {
            Assert.That(row.IsComboBox, Is.True);
            Assert.That(row.Choices, Is.EqualTo(new[] { "Left", "Center", "Right" }));
        });
    }

    // ==================================================================
    // D9 — the Degraded tier, where user data is at stake
    // ==================================================================

    [Test]
    public void ADegradedRow_IsFrozen_AndSaysWhy()
    {
        var grid = GridOver(DegradedForm, "chk");
        var row = grid.Rows.Single(r => r.Name == "Checked");

        Assert.Multiple(() =>
        {
            Assert.That(row.IsFrozen, Is.True);
            Assert.That(row.IsEditable, Is.False);
            Assert.That(row.FrozenReason, Does.Contain("Bool"), "the reason names the expected type");
            Assert.That(row.RawValue, Is.EqualTo("maybe"), "and the value is still shown");
        });
    }

    [Test]
    public void ADegradedRow_ShowsNoTypedEditorAtAll()
    {
        // ⛔⛔ Every editor flag must be false. A disabled toggle is not enough — the binding still
        // reads BoolValue, and a control that renders is a control that can push a value back.
        var row = GridOver(DegradedForm, "chk").Rows.Single(r => r.Name == "Checked");

        Assert.Multiple(() =>
        {
            Assert.That(row.IsCheckBox, Is.False);
            Assert.That(row.IsNumericUpDown, Is.False);
            Assert.That(row.IsComboBox, Is.False);
            Assert.That(row.IsTextBox, Is.False);
        });
    }

    [Test]
    public void ADegradedRow_RefusesToWrite_EvenIfSomethingPushesAValue()
    {
        // ⛔⛔ THE data-loss test. Coercing "maybe" to False the moment the row renders would
        // destroy the user's value without them touching anything — and it would look like the
        // document had always said False.
        var grid = GridOver(DegradedForm, "chk");
        var row = grid.Rows.Single(r => r.Name == "Checked");

        row.BoolValue = false;
        row.StringValue = "False";

        Assert.That(row.RawValue, Is.EqualTo("maybe"), "the frozen value is untouched");
    }

    [Test]
    public void TheRestOfADegradedControlStaysEditable()
    {
        // D9's whole point: one bad value costs one row, not the control.
        var grid = GridOver(DegradedForm, "chk");

        Assert.That(grid.Rows.Single(r => r.Name == "Text").IsEditable, Is.True);
    }

    // ==================================================================
    // Committing
    // ==================================================================

    [Test]
    public void EditingARow_WritesThroughToTheControl()
    {
        var grid = GridOver(WebForm, "chk");
        var row = grid.Rows.Single(r => r.Name == "Text");

        row.StringValue = "Remember me";

        Assert.That(grid.SelectedControl!.Properties["Text"], Is.EqualTo("Remember me"));
    }

    [Test]
    public void ABoolWritesTheDocumentsOwnLowercaseSpelling()
    {
        // ⚠ The reader accepts either case, but rewriting every "true" as "True" would report a
        // change the user never made and put it in their diff.
        var grid = GridOver(WebForm, "chk");
        var row = grid.Rows.Single(r => r.Name == "Enabled");

        row.BoolValue = false;

        Assert.That(grid.SelectedControl!.Properties["Enabled"], Is.EqualTo("false"));
    }

    [Test]
    public void ANoOpWrite_RaisesNothing()
    {
        // ⛔ The grid re-pushes every row's value when the selection changes. Forwarding those
        // would mark the document dirty and regenerate the user's region for a selection CLICK.
        var grid = GridOver(WebForm, "chk");
        var edits = 0;
        grid.Edited += (_, _) => edits++;

        var row = grid.Rows.Single(r => r.Name == "Text");
        row.StringValue = row.RawValue;

        Assert.That(edits, Is.Zero);
    }

    [Test]
    public void ARealEdit_RaisesEditedExactlyOnce()
    {
        var grid = GridOver(WebForm, "chk");
        var edits = 0;
        grid.Edited += (_, _) => edits++;

        grid.Rows.Single(r => r.Name == "Text").StringValue = "Changed";

        Assert.That(edits, Is.EqualTo(1));
    }

    [Test]
    public void ChangingTheSelection_RebuildsTheRows()
    {
        var file = Read("""
            <WebForm Name="F" Version="1">
              <Controls>
                <CheckBox Id="chk" TabIndex="0"/>
                <Label Id="lbl" TabIndex="1"/>
              </Controls>
            </WebForm>
            """);
        var grid = new FormPropertyGridViewModel();
        grid.Load(file);

        grid.SelectedControl = file.Model.FindById("chk");
        Assert.That(grid.Rows.Select(r => r.Name), Does.Contain("Checked"));

        grid.SelectedControl = file.Model.FindById("lbl");

        Assert.Multiple(() =>
        {
            Assert.That(grid.Rows.Select(r => r.Name), Does.Not.Contain("Checked"));
            Assert.That(grid.Rows.Select(r => r.Name), Does.Contain("TextAlign"));
            Assert.That(grid.Header, Is.EqualTo("lbl"));
        });
    }

    // ==================================================================
    // Toolbox
    // ==================================================================

    [Test]
    public void TheToolbox_OffersOnlyKindsThatExistOnTheTarget()
    {
        var web = new FormToolboxViewModel { Target = FormTarget.Web };
        var winForms = new FormToolboxViewModel { Target = FormTarget.WinForms };

        // ⚠ Compared as SETS. This asserted sequence equality with the catalog, which tested more
        // than its name claims: the toolbox now groups containers last so it can draw VS-style
        // category headers, and ORDER is a presentation decision the panel owns. What must stay
        // true is the membership — every kind the target has, and nothing it does not.
        // ⚠ Task 24, commit 24b: an ITEM kind (none exist yet; commit 24c adds the first) is never a
        // toolbox row (spec Decision 7) — it is created from the "Type Here" slot on its host, never
        // dragged onto the canvas. The production filter (FormToolboxViewModel.Rebuild) already
        // excludes it; this says the same thing from the other side, so a future Item row cannot
        // silently reappear in the toolbox because only one of the two places agreed to drop it.
        Assert.Multiple(() =>
        {
            Assert.That(web.Items.Select(i => i.Kind),
                Is.EquivalentTo(FormControlCatalog.For(FormTarget.Web).Where(c => c.Place != FormPlace.Item).Select(c => c.Kind)));
            Assert.That(winForms.Items.Select(i => i.Kind),
                Is.EquivalentTo(FormControlCatalog.For(FormTarget.WinForms).Where(c => c.Place != FormPlace.Item).Select(c => c.Kind)));
        });
    }

    /// <summary>
    /// The grouping the panel draws its headers from. Pinned because the headers are rendered by
    /// the FIRST row of each category — if the order interleaves, the same header appears twice and
    /// the toolbox reads as though there are four groups.
    /// </summary>
    [Test]
    public void TheToolbox_GroupsContainersAfterCommonControls()
    {
        var toolbox = new FormToolboxViewModel { Target = FormTarget.WinForms };

        var categories = toolbox.Items.Select(i => i.Category).ToList();
        var starts = toolbox.Items.Where(i => i.StartsCategory).Select(i => i.Category).ToList();

        Assert.Multiple(() =>
        {
            // Task 25 added the third category, last, as VS's tab orders them.
            Assert.That(categories, Is.EqualTo(categories.OrderBy(c => c == "Components" ? 2 : c == "Containers" ? 1 : 0)),
                "categories interleave, so a header would be drawn more than once");
            Assert.That(starts, Is.EqualTo(new[] { "Common Controls", "Containers", "Components" }),
                "exactly one header per category, in that order");
            Assert.That(toolbox.Items.Select(i => i.Glyph).Distinct().Count(),
                Is.EqualTo(toolbox.Items.Count),
                "two controls share a glyph — the mark beside a row must identify it");
        });
    }

    [Test]
    public void TheToolbox_SaysWhatEachKindIsOnThisTarget()
    {
        // "TextBox" alone leaves the user guessing which of the three <input> kinds they are about
        // to place.
        var web = new FormToolboxViewModel { Target = FormTarget.Web };
        var winForms = new FormToolboxViewModel { Target = FormTarget.WinForms };

        Assert.Multiple(() =>
        {
            Assert.That(web.Items.Single(i => i.Kind == "CheckBox").Description,
                Is.EqualTo("<input type=\"checkbox\">"));
            Assert.That(web.Items.Single(i => i.Kind == "Panel").Description, Is.EqualTo("<div>"));
            Assert.That(winForms.Items.Single(i => i.Kind == "CheckBox").Description,
                Is.EqualTo("CheckBox"));
        });
    }

    [Test]
    public void TheToolbox_FollowsTheTarget()
    {
        var toolbox = new FormToolboxViewModel { Target = FormTarget.Web };
        var before = toolbox.Items.Single(i => i.Kind == "Button").Description;

        toolbox.Target = FormTarget.WinForms;

        Assert.That(toolbox.Items.Single(i => i.Kind == "Button").Description, Is.Not.EqualTo(before));
    }
}
