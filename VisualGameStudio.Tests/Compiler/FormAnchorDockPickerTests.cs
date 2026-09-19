using BasicLang.Forms;
using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 26: the Anchor and Dock pickers — VS's two most recognisable property-grid widgets.
///
/// <para>⛔ The WIDGET is view code; the ALGEBRA is not. Which edges an unset Anchor implies, what
/// clearing the last one means, and whether Dock is exclusive are all decisions that produce a
/// wrong form rather than an ugly one, so they live on the row and are tested here.</para>
///
/// <para>⛔⛔ D3: neither picker exists for a <c>.blwebform</c>. A web control lives in a grid CELL
/// and has no edges to anchor to — and the rows are built only in the <c>PixelGeometry</c> arm, so
/// there is no "hide on web" rule that could be forgotten.</para>
/// </summary>
[TestFixture]
public class FormAnchorDockPickerTests
{
    /// <summary>
    /// A grid loaded over a real document, through the real reader.
    ///
    /// <para>⚠ <c>FormFile</c>'s constructor is internal, so a test cannot fabricate one — which is
    /// the right constraint: going through <c>FormDocumentWriter</c> and <c>FormDocumentReader</c>
    /// means these rows are built over a document that genuinely round-tripped, not a hand-made
    /// model that might not survive a save.</para>
    ///
    /// <para>⛔ Returns the control FROM THE MODEL. The one passed in is a different object after
    /// the round trip, and asserting on it would test a copy the grid never touched.</para>
    /// </summary>
    private static (FormPropertyGridViewModel Grid, FormControl Control) GridFor(
        FormGeometry geometry, FormTarget target)
    {
        var form = new FormDocument { Target = target, Name = "F", Width = 400, Height = 300 };
        if (target == FormTarget.Web)
        {
            form.Layout = new FormLayout { Kind = FormLayoutKind.Grid, Cols = "1fr", Rows = "auto" };
        }
        else
        {
            form.Text = "F";
        }

        form.Controls.Add(new FormControl
        {
            Kind = "Button", Id = "btn", TabIndex = 0, Geometry = geometry
        });

        var name = target == FormTarget.Web ? "F.blwebform" : "F.blform";
        var file = BasicLang.Forms.Serialization.FormDocumentReader.Read(
            name, BasicLang.Forms.Serialization.FormDocumentWriter.Create(form));

        Assert.That(file.IsRefused, Is.False,
            string.Join("; ", file.Diagnostics.Select(d => d.Format())));

        var control = file.Model.Controls[0];
        var grid = new FormPropertyGridViewModel();
        grid.Load(file);
        grid.SelectedControl = control;
        return (grid, control);
    }

    private static PixelGeometry Pixel() =>
        new() { X = 10, Y = 10, Width = 80, Height = 24 };

    private static PixelGeometry GeometryOf(FormControl control) => (PixelGeometry)control.Geometry!;

    private static FormPropertyRow Row(FormPropertyGridViewModel grid, string name) =>
        grid.Rows.Single(r => r.Name == name);

    // ==================================================================
    // Which rows exist — D3
    // ==================================================================

    [Test]
    public void APixelControlGetsBothPickers()
    {
        var (grid, _) = GridFor(Pixel(), FormTarget.WinForms);

        Assert.Multiple(() =>
        {
            Assert.That(Row(grid, "Anchor").IsAnchorPicker, Is.True);
            Assert.That(Row(grid, "Dock").IsDockPicker, Is.True);
        });
    }

    /// <summary>⛔ D3. A cell has no edges; offering an anchor would be inventing vocabulary.</summary>
    [Test]
    public void AWebControlGetsNeitherPicker()
    {
        var (grid, _) = GridFor(new GridGeometry { Col = 1, Row = 1 }, FormTarget.Web);

        Assert.That(
            grid.Rows.Select(r => r.Name),
            Has.No.Member("Anchor").And.No.Member("Dock"));
    }

    /// <summary>
    /// ⛔ A picker row must NOT also render the shared typed editor. Its declared type is String,
    /// so binding visibility to IsEditable would put a text box under the picker and let the two
    /// contradict each other.
    /// </summary>
    [Test]
    public void APickerRowDoesNotAlsoRenderTheSharedTextEditor()
    {
        var (grid, _) = GridFor(Pixel(), FormTarget.WinForms);

        Assert.Multiple(() =>
        {
            Assert.That(Row(grid, "Anchor").UsesTypedEditor, Is.False);
            Assert.That(Row(grid, "Anchor").IsTextBox, Is.False);
            Assert.That(Row(grid, "Dock").UsesTypedEditor, Is.False);
            Assert.That(Row(grid, "X").UsesTypedEditor, Is.True, "an ordinary row still does");
        });
    }

    // ==================================================================
    // Anchor algebra
    // ==================================================================

    /// <summary>
    /// ⛔⛔ An UNSET Anchor is not "anchored to nothing" — WinForms defaults to Top, Left. Showing
    /// four empty edges would state something false about an untouched form, and the first click
    /// would appear to ADD an anchor while silently REMOVING two.
    /// </summary>
    [Test]
    public void AnUnsetAnchorShowsTheWinFormsDefaultOfTopAndLeft()
    {
        var (grid, _) = GridFor(Pixel(), FormTarget.WinForms);
        var row = Row(grid, "Anchor");

        Assert.Multiple(() =>
        {
            Assert.That(row.AnchorTop, Is.True);
            Assert.That(row.AnchorLeft, Is.True);
            Assert.That(row.AnchorBottom, Is.False);
            Assert.That(row.AnchorRight, Is.False);
        });
    }

    /// <summary>⚠ Reading the default must not WRITE it — opening a form changes nothing.</summary>
    [Test]
    public void ReadingTheDefaultDoesNotWriteItToTheDocument()
    {
        var (grid, control) = GridFor(Pixel(), FormTarget.WinForms);

        _ = Row(grid, "Anchor").AnchorTop;

        Assert.That(GeometryOf(control).Anchor, Is.Null, "the document keeps its unset state");
    }

    [Test]
    public void TogglingAnEdgeWritesTheWholeSet()
    {
        var (grid, control) = GridFor(Pixel(), FormTarget.WinForms);

        Row(grid, "Anchor").AnchorRight = true;

        Assert.That(GeometryOf(control).Anchor, Is.EqualTo("Top,Left,Right"));
    }

    /// <summary>⚠ Flag order, not alphabetical — "Bottom,Left,Right,Top" would read as scrambled.</summary>
    [Test]
    public void EdgesAreWrittenInFlagOrder()
    {
        var (grid, control) = GridFor(Pixel(), FormTarget.WinForms);

        Row(grid, "Anchor").AnchorBottom = true;

        Assert.That(GeometryOf(control).Anchor, Is.EqualTo("Top,Bottom,Left"));
    }

    [Test]
    public void UncheckingAnEdgeRemovesIt()
    {
        var (grid, control) = GridFor(Pixel(), FormTarget.WinForms);
        var row = Row(grid, "Anchor");
        row.AnchorRight = true;

        row.AnchorTop = false;

        Assert.That(GeometryOf(control).Anchor, Is.EqualTo("Left,Right"));
    }

    /// <summary>
    /// ⚠ No edges is a REAL state — <c>AnchorStyles.None</c> — and not the same as unset. A control
    /// the user deliberately un-anchored moves half the distance the form is resized, which is a
    /// documented WinForms behaviour, so it has to survive as a value rather than collapse to null.
    /// </summary>
    [Test]
    public void ClearingEveryEdgeWritesNoneRatherThanNothing()
    {
        var (grid, control) = GridFor(Pixel(), FormTarget.WinForms);
        var row = Row(grid, "Anchor");

        row.AnchorTop = false;
        row.AnchorLeft = false;

        Assert.That(GeometryOf(control).Anchor, Is.EqualTo("None"));
    }

    /// <summary>
    /// ⛔⛔ The whole point of Task 26, end to end: what the PICKER produces is what the region
    /// writer EMITS, multi-edge included. Before the analyzer exemption this combination was
    /// refused outright with BL8015.
    /// </summary>
    [Test]
    public void ThePickersOutputIsWhatTheRegionWriterEmits()
    {
        var (grid, control) = GridFor(Pixel(), FormTarget.WinForms);
        Row(grid, "Anchor").AnchorRight = true;

        var form = new FormDocument
        {
            Target = FormTarget.WinForms, Name = "PickForm", Width = 800, Height = 450, Text = "P"
        };
        form.Controls.Add(control);

        var written = RegionWriter.Write(
            "PickForm.bas",
            FormScaffolder.Create("PickForm", FormTarget.WinForms).CodeText,
            form,
            "PickForm.blform");

        Assert.Multiple(() =>
        {
            Assert.That(written.Refused, Is.False,
                string.Join("; ", written.Diagnostics.Select(d => d.Format())));
            // Top(1) + Left(4) + Right(8) = 13
            Assert.That(written.Text, Does.Contain("btn.Anchor = CType(13, AnchorStyles)"));
        });
    }

    // ==================================================================
    // Dock algebra
    // ==================================================================

    [Test]
    public void AnUnsetDockIsNone()
    {
        var (grid, _) = GridFor(Pixel(), FormTarget.WinForms);
        var row = Row(grid, "Dock");

        Assert.Multiple(() =>
        {
            Assert.That(row.DockValue, Is.EqualTo("None"));
            Assert.That(row.IsDockedNone, Is.True);
        });
    }

    /// <summary>⛔ DockStyle is not flags — choosing a region REPLACES the previous one.</summary>
    [Test]
    public void ChoosingARegionReplacesThePreviousOne()
    {
        var (grid, control) = GridFor(Pixel(), FormTarget.WinForms);
        var row = Row(grid, "Dock");

        row.SetDock("Left");
        row.SetDock("Fill");

        Assert.Multiple(() =>
        {
            Assert.That(GeometryOf(control).Dock, Is.EqualTo("Fill"));
            Assert.That(row.IsDockedLeft, Is.False);
            Assert.That(row.IsDockedFill, Is.True);
        });
    }

    /// <summary>
    /// ⚠ None writes NOTHING, not the word — an undocked control carries no Dock attribute, which
    /// is the same "write only non-default values" rule the rest of the document follows.
    /// </summary>
    [Test]
    public void NoneClearsTheAttributeRatherThanWritingTheWord()
    {
        var (grid, control) = GridFor(Pixel(), FormTarget.WinForms);
        var row = Row(grid, "Dock");
        row.SetDock("Top");

        row.SetDock("None");

        Assert.That(GeometryOf(control).Dock, Is.Null);
    }

    [Test]
    public void DockAndAnchorAreIndependent()
    {
        var (grid, control) = GridFor(Pixel(), FormTarget.WinForms);

        Row(grid, "Dock").SetDock("Fill");
        Row(grid, "Anchor").AnchorBottom = true;

        Assert.Multiple(() =>
        {
            Assert.That(GeometryOf(control).Dock, Is.EqualTo("Fill"));
            Assert.That(GeometryOf(control).Anchor, Is.EqualTo("Top,Bottom,Left"));
        });
    }
}
