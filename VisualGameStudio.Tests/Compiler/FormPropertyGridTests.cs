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
    // Rows come from the catalog, not from the document
    // ==================================================================

    [Test]
    public void Rows_ComeFromTheCatalog_NotFromWhatTheDocumentHappensToSet()
    {
        // ⛔ Building rows from the document's own attributes would offer exactly the properties
        // already set, and no way to add one that is not.
        var grid = GridOver(WebForm, "chk");

        var expected = FormControlCatalog.Find("CheckBox")!.Properties.Select(p => p.Name);

        Assert.That(grid.Rows.Select(r => r.Name), Is.EqualTo(expected),
            "every catalog property, in catalog order — including the ones the document omits");
    }

    [Test]
    public void Rows_AreEmpty_WithNoSelection()
    {
        var grid = new FormPropertyGridViewModel();
        grid.Load(Read(WebForm));

        Assert.Multiple(() =>
        {
            Assert.That(grid.Rows, Is.Empty);
            Assert.That(grid.IsEmpty, Is.True);
            Assert.That(grid.Header, Is.EqualTo("No selection"));
        });
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
        Assert.Multiple(() =>
        {
            Assert.That(web.Items.Select(i => i.Kind),
                Is.EquivalentTo(FormControlCatalog.For(FormTarget.Web).Select(c => c.Kind)));
            Assert.That(winForms.Items.Select(i => i.Kind),
                Is.EquivalentTo(FormControlCatalog.For(FormTarget.WinForms).Select(c => c.Kind)));
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
            Assert.That(categories, Is.EqualTo(categories.OrderBy(c => c == "Containers" ? 1 : 0)),
                "categories interleave, so a header would be drawn more than once");
            Assert.That(starts, Is.EqualTo(new[] { "Common Controls", "Containers" }),
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
