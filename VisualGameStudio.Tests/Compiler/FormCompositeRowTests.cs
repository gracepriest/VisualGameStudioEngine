using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Slice 3 Task 6 (plan 3.5, spec §3): COMPOSITE rows — Font → Name/Size/Bold/Italic/Underline, Size → Width/Height,
/// Location → X/Y, Padding → All/Left/Top/Right/Bottom. The parent owns the value and accepts typed text; a part reads
/// its piece and writes the WHOLE value back through the parent's own Commit (one statement per composite — fan-in).
/// </summary>
[TestFixture]
public class FormCompositeRowTests
{
    private const string WinForm = """
        <Form Name="F" Version="1" Width="400" Height="300">
          <Controls>
            <Button Id="btn" X="16" Y="24" Width="75" Height="23" TabIndex="0" Text="Go"/>
            <Label Id="lbl" X="16" Y="60" Width="100" Height="23" TabIndex="1" Font="Arial, 10pt" Padding="4, 2, 4, 2"/>
            <Label Id="bad" X="16" Y="90" Width="100" Height="23" TabIndex="2" Font="Arial"/>
          </Controls>
        </Form>
        """;

    private static (FormFile File, FormPropertyGridViewModel Grid) Open(string? select)
    {
        var file = FormDocumentReader.Read("F.blform", WinForm);
        var grid = new FormPropertyGridViewModel();
        grid.Load(file);
        grid.SelectedControl = select == null ? null : file.Model.FindById(select);
        return (file, grid);
    }

    private static FormPropertyRow Top(FormPropertyGridViewModel grid, string name) => grid.Rows.Single(r => r.Name == name);

    private static FormPropertyRow Part(FormPropertyRow parent, string name) => parent.Children.Single(c => c.Name == name);

    [Test]
    public void AControlsLocationAndSize_AreCompositesOverItsGeometry()
    {
        var (file, grid) = Open("btn");
        var location = Top(grid, "Location");
        var size = Top(grid, "Size");
        var pixel = (PixelGeometry)file.Model.FindById("btn")!.Geometry!;

        Assert.Multiple(() =>
        {
            Assert.That(location.Children.Select(c => c.Name), Is.EqualTo(new[] { "X", "Y" }));
            Assert.That(size.Children.Select(c => c.Name), Is.EqualTo(new[] { "Width", "Height" }));
            Assert.That(location.StringValue, Is.EqualTo("16, 24"));
            Assert.That(size.StringValue, Is.EqualTo("75, 23"));
        });

        Part(location, "X").IntValue = 40;
        Assert.That((pixel.X, location.StringValue), Is.EqualTo((40, "40, 24")), "a part moves the geometry and the parent re-reads");

        location.StringValue = "8, 9";
        Assert.That((pixel.X, pixel.Y, Part(location, "Y").IntValue), Is.EqualTo((8, 9, 9)),
            "the parent's typed text sets both, and its parts re-read");
    }

    /// <summary>
    /// ⛔ A part that writes the model directly (X, Y — intrinsic, not through the parent's Commit) must still tell its
    /// PARENT to re-read: the parent's text box is bound to it, and a getter that recomputes proves nothing about the
    /// notification a binding needs.
    /// </summary>
    [Test]
    public void AGeometryPartEdit_NotifiesItsParentsEditor()
    {
        var (_, grid) = Open("btn");
        var location = Top(grid, "Location");
        var raised = new List<string?>();
        location.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        Part(location, "X").IntValue = 50;

        Assert.That(raised, Does.Contain(nameof(FormPropertyRow.StringValue)));
    }

    [Test]
    public void AFontRow_HasVsParts_AndAPartWritesTheWholeFont()
    {
        var (file, grid) = Open("lbl");
        var font = Top(grid, "Font");
        var edits = 0;
        grid.Edited += (_, _) => edits++;

        Assert.That(font.Children.Select(c => c.Name), Is.EqualTo(new[] { "Name", "Size", "Bold", "Italic", "Underline" }));

        Part(font, "Bold").BoolValue = true;
        Part(font, "Size").StringValue = "12";

        Assert.Multiple(() =>
        {
            Assert.That(file.Model.FindById("lbl")!.Properties["Font"], Is.EqualTo("Arial, 12pt, style=Bold"),
                "ONE value, re-emitted from the parts (the fan-in rule)");
            Assert.That(Part(font, "Name").StringValue, Is.EqualTo("Arial"));
            Assert.That(edits, Is.EqualTo(2), "one Edited per part edit — never a second from the part itself");
        });
    }

    /// <summary>
    /// An ambient Font is ABSENT and shows empty; its parts start from the Form's WinForms default (what it inherits), so
    /// toggling Bold on a fresh Button writes a whole, usable font.
    /// </summary>
    [Test]
    public void APartOfAnAbsentFont_StartsFromTheFormsDefaultFont()
    {
        var (file, grid) = Open("btn");
        var font = Top(grid, "Font");

        Assert.That(Part(font, "Name").StringValue, Is.EqualTo("Segoe UI"));

        Part(font, "Bold").BoolValue = true;

        Assert.That(file.Model.FindById("btn")!.Properties["Font"], Is.EqualTo("Segoe UI, 9pt, style=Bold"));
    }

    /// <summary>A part's value the parent refuses (a family that could break out of a string) is refused — nothing written.</summary>
    [Test]
    public void APartValueTheParentRefuses_IsNotWritten_AndTheParentSaysWhy()
    {
        var (file, grid) = Open("lbl");
        var font = Top(grid, "Font");

        Part(font, "Name").StringValue = "Seg\"oe";

        Assert.Multiple(() =>
        {
            Assert.That(file.Model.FindById("lbl")!.Properties["Font"], Is.EqualTo("Arial, 10pt"));
            Assert.That(font.Refusal, Is.Not.Null.And.Contains("Seg\"oe"), "the PARENT row carries the refusal the pane shows");
        });
    }

    [Test]
    public void APaddingRow_HasAllAndTheFourSides_AllIsMinusOneWhenTheyDiffer()
    {
        var (file, grid) = Open("lbl");
        var padding = Top(grid, "Padding");

        Assert.Multiple(() =>
        {
            Assert.That(padding.Children.Select(c => c.Name), Is.EqualTo(new[] { "All", "Left", "Top", "Right", "Bottom" }));
            Assert.That(Part(padding, "All").IntValue, Is.EqualTo(-1), "WinForms' Padding.All is -1 when the sides differ");
            Assert.That(Part(padding, "Top").IntValue, Is.EqualTo(2));
        });

        Part(padding, "Right").IntValue = 9;
        Assert.That(file.Model.FindById("lbl")!.Properties["Padding"], Is.EqualTo("4, 2, 9, 2"));

        Part(padding, "All").IntValue = 3;
        Assert.Multiple(() =>
        {
            Assert.That(file.Model.FindById("lbl")!.Properties["Padding"], Is.EqualTo("3"));
            Assert.That(Part(padding, "Left").IntValue, Is.EqualTo(3), "the siblings re-read");
        });
    }

    [Test]
    public void TheFormsSizeRows_AreCompositesToo()
    {
        var (file, grid) = Open(select: null);
        var minimum = Top(grid, "MinimumSize");

        Part(minimum, "Width").IntValue = 200;

        Assert.Multiple(() =>
        {
            Assert.That(minimum.Children.Select(c => c.Name), Is.EqualTo(new[] { "Width", "Height" }));
            Assert.That(file.Model.Properties["MinimumSize"], Is.EqualTo("200, 0"));
            Assert.That(Top(grid, "ClientSize").Children.Select(c => c.Name), Is.EqualTo(new[] { "Width", "Height" }));
        });
    }

    [Test]
    public void AFrozenComposite_FreezesItsParts()
    {
        var (_, grid) = Open("bad");
        var font = Top(grid, "Font");

        Assert.Multiple(() =>
        {
            Assert.That(font.IsFrozen, Is.True, "Font=\"Arial\" has no size: Degraded");
            Assert.That(font.Children.All(c => c.IsFrozen), Is.True, "nothing may coerce the preserved text through a part");
        });
    }

    // ==================================================================
    // The display list: expand, collapse, memory, search
    // ==================================================================

    [Test]
    public void ExpandingAComposite_ShowsItsPartsRightAfterIt_AndCollapsingHidesThem()
    {
        var (_, grid) = Open("lbl");
        var font = Top(grid, "Font");

        Assert.That(grid.DisplayItems, Has.No.Member(Part(font, "Bold")), "collapsed by default, as in VS");

        font.IsExpanded = true;
        var at = grid.DisplayItems.IndexOf(font);

        Assert.That(grid.DisplayItems.Skip(at + 1).Take(5), Is.EqualTo(font.Children),
            "the parts, in VS's order, right after the parent");

        font.IsExpanded = false;
        Assert.That(grid.DisplayItems.OfType<FormPropertyRow>().Where(r => r.Parent == font), Is.Empty);
    }

    [Test]
    public void AnExpansion_IsRememberedAcrossSelections()
    {
        var (file, grid) = Open("lbl");
        Top(grid, "Font").IsExpanded = true;

        grid.SelectedControl = file.Model.FindById("btn");

        Assert.Multiple(() =>
        {
            Assert.That(Top(grid, "Font").IsExpanded, Is.True, "the next control's Font opens expanded, as VS remembers it");
            Assert.That(grid.DisplayItems, Has.Member(Part(Top(grid, "Font"), "Bold")));
            Assert.That(Top(grid, "Location").IsExpanded, Is.False, "only what the user expanded");
        });
    }

    [Test]
    public void SearchingForAPart_FindsItsComposite_AndShowsThePart()
    {
        var (_, grid) = Open("lbl");

        grid.SearchText = "Bold";
        var rows = grid.DisplayItems.OfType<FormPropertyRow>().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(rows.Select(r => r.Name), Does.Contain("Font").And.Contains("Bold"));
            Assert.That(Top(grid, "Font").IsExpanded, Is.False, "display only: the expansion is not changed by a search");
        });
    }

    [Test]
    public void CollapsingAComposite_WhoseSelectedPartIsHidden_ClearsTheDescribedRow()
    {
        var (_, grid) = Open("lbl");
        var font = Top(grid, "Font");
        font.IsExpanded = true;
        grid.SelectedItem = Part(font, "Bold");

        font.IsExpanded = false;

        Assert.That(grid.SelectedRow, Is.Null, "the pane must not describe a row nobody can see");
    }
}
