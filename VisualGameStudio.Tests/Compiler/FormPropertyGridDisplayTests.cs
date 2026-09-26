using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Shell.ViewModels.Designer;
using VisualGameStudio.Shell.ViewModels.Documents;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>Spec §3 — what the grid DISPLAYS, and how the object selector selects.</summary>
[TestFixture]
public class FormPropertyGridDisplayTests
{
    private const string Doc = """
        <Form Name="F" Version="1" Width="400" Height="300" Text="Hello">
          <Controls>
            <Label Id="lbl" X="0" Y="0" Width="10" Height="10" TabIndex="0" Text="Hi"/>
            <Button Id="btn" X="0" Y="20" Width="10" Height="10" TabIndex="1"/>
          </Controls>
          <Components><Timer Id="tmr"/></Components>
        </Form>
        """;

    private static (FormFile File, FormPropertyGridViewModel Grid) Open(string? select = "lbl", string doc = Doc,
        string name = "F.blform")
    {
        var file = FormDocumentReader.Read(name, doc);
        var grid = new FormPropertyGridViewModel();
        grid.Load(file);
        grid.SelectedControl = select == null ? null : file.Model.FindById(select);
        return (file, grid);
    }

    private static List<string> Lines(FormPropertyGridViewModel grid) =>
        grid.DisplayItems.Select(i => i switch
        {
            FormPropertyCategoryHeader h => "[" + h.Name + "]",
            FormPropertyRow r => r.Name,
            _ => "?"
        }).ToList();

    [Test]
    public void Categorized_GroupsRowsUnderSortedHeaders_EachSortedByName()
    {
        var (_, grid) = Open();
        var lines = Lines(grid);
        var headers = lines.Where(l => l.StartsWith('[')).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(grid.IsCategorized, Is.True, "Categorized is VS's default view");
            Assert.That(headers, Is.Ordered.Using((IComparer<string>)StringComparer.OrdinalIgnoreCase),
                "categories are listed alphabetically");
            Assert.That(lines.First(), Does.StartWith("["), "every row sits under a header");
            var behavior = lines.SkipWhile(l => l != "[Behavior]").Skip(1).TakeWhile(l => !l.StartsWith('[')).ToList();
            var expected = grid.Rows.Where(r => r.Category == "Behavior").Select(r => r.Name)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            Assert.That(expected, Has.Count.GreaterThan(1), "fixture premise: Behavior has rows to sort");
            Assert.That(behavior, Is.EqualTo(expected));
        });
    }

    [Test]
    public void Alphabetical_IsOneFlatSortedList_WithNoHeaders()
    {
        var (_, grid) = Open();

        grid.IsAlphabetical = true;
        var lines = Lines(grid);

        Assert.Multiple(() =>
        {
            Assert.That(grid.IsCategorized, Is.False);
            Assert.That(lines.Any(l => l.StartsWith('[')), Is.False);
            Assert.That(lines, Is.Ordered.Using((IComparer<string>)StringComparer.OrdinalIgnoreCase));
            Assert.That(lines, Is.EquivalentTo(grid.Rows.Select(r => r.Name)), "every row, once");
        });
    }

    [Test]
    public void CollapsingACategory_HidesItsRows_AndSurvivesReselection()
    {
        var (file, grid) = Open();

        grid.DisplayItems.OfType<FormPropertyCategoryHeader>().Single(h => h.Name == "Behavior").IsExpanded = false;

        Assert.That(Lines(grid), Has.No.Member("Enabled"), "a collapsed category's rows are not listed");

        grid.SelectedControl = file.Model.FindById("btn");

        Assert.Multiple(() =>
        {
            Assert.That(Lines(grid), Has.No.Member("Enabled"), "the collapse is remembered across selections");
            Assert.That(Lines(grid), Does.Contain("[Behavior]"));
        });
    }

    [Test]
    public void ExpandingACategoryAgain_PutsItsRowsBackUnderIt_InOrder()
    {
        var (_, grid) = Open();
        var before = Lines(grid);
        var header = grid.DisplayItems.OfType<FormPropertyCategoryHeader>().Single(h => h.Name == "Behavior");

        header.IsExpanded = false;
        header.IsExpanded = true;

        Assert.That(Lines(grid), Is.EqualTo(before));
    }

    [Test]
    public void Search_FiltersByName_InBothModes()
    {
        var (_, grid) = Open();

        grid.SearchText = "color";

        Assert.That(Lines(grid).Where(l => !l.StartsWith('[')), Is.EquivalentTo(new[] { "BackColor", "ForeColor" }));

        grid.IsAlphabetical = true;

        Assert.That(Lines(grid), Is.EqualTo(new[] { "BackColor", "ForeColor" }));
    }

    [Test]
    public void Search_HidesACategoryWithNoMatchingRow()
    {
        var (_, grid) = Open();

        grid.SearchText = "tabindex";

        Assert.That(Lines(grid), Is.EqualTo(new[] { "[Behavior]", "TabIndex" }));
    }

    /// <summary>
    /// ⛔ A search keystroke or a sort toggle rebuilds the list; the row being described must survive it
    /// while it is still shown — and must NOT survive being filtered out, or the pane describes a hidden row.
    /// </summary>
    [Test]
    public void TheSelectedRow_SurvivesSearchAndSort_WhileShown_AndIsClearedWhenHidden()
    {
        var (_, grid) = Open();
        var enabled = grid.Rows.Single(r => r.Name == "Enabled");
        grid.SelectedItem = enabled;

        grid.SearchText = "en";
        var afterEn = (grid.SelectedItem, grid.SelectedRow, grid.DescriptionTitle);

        grid.SearchText = "";
        grid.IsAlphabetical = true;
        var afterSort = (grid.SelectedItem, grid.SelectedRow);

        grid.SearchText = "color";

        Assert.Multiple(() =>
        {
            Assert.That(afterEn.SelectedItem, Is.SameAs(enabled), "typing 'en' keeps Enabled");
            Assert.That(afterEn.SelectedRow, Is.SameAs(enabled));
            Assert.That(afterEn.DescriptionTitle, Is.EqualTo("Enabled"));
            Assert.That(afterSort.SelectedItem, Is.SameAs(enabled), "toggling Alphabetical keeps it");
            Assert.That(afterSort.SelectedRow, Is.SameAs(enabled));
            Assert.That(grid.SelectedItem, Is.Null, "'color' hides Enabled, so it is not selected");
            Assert.That(grid.SelectedRow, Is.Null, "and the pane does not describe a hidden row");
        });
    }

    [Test]
    public void CollapsingTheSelectedRowsCategory_ClearsTheDescribedRow()
    {
        var (_, grid) = Open();
        grid.SelectedItem = grid.Rows.Single(r => r.Name == "Enabled");

        grid.DisplayItems.OfType<FormPropertyCategoryHeader>().Single(h => h.Name == "Behavior").IsExpanded = false;

        Assert.That(grid.SelectedRow, Is.Null);
    }

    /// <summary>A match inside a COLLAPSED category shows the category expanded — never a lone header.</summary>
    [Test]
    public void ASearchMatchInACollapsedCategory_IsShown_AndClearingTheSearchRestoresTheCollapse()
    {
        var (_, grid) = Open();
        grid.DisplayItems.OfType<FormPropertyCategoryHeader>().Single(h => h.Name == "Behavior").IsExpanded = false;

        grid.SearchText = "enabled";
        var searching = Lines(grid);
        grid.SearchText = "";

        Assert.Multiple(() =>
        {
            Assert.That(searching, Is.EqualTo(new[] { "[Behavior]", "Enabled" }));
            Assert.That(Lines(grid), Does.Contain("[Behavior]").And.No.Member("Enabled"),
                "the remembered collapse is back once the search is cleared");
        });
    }

    /// <summary>
    /// A control whose kind the catalog does not know still has a name, a place and a tab order — it shows
    /// those rows, never the FORM's rows as though nothing were selected.
    /// </summary>
    [Test]
    public void AControlWithNoCatalogRow_ShowsItsIntrinsicRows_NotTheForms()
    {
        var (file, grid) = Open(select: null);
        var mystery = new FormControl
        {
            Kind = "UnknownWidgetKind",
            Id = "mystery1",
            Geometry = new PixelGeometry { X = 1, Y = 2, Width = 3, Height = 4 }
        };
        file.Model.Controls.Add(mystery);
        Assert.That(mystery.Definition, Is.Null, "fixture premise: an unknown Kind has no catalog row");

        grid.SelectedControl = mystery;

        Assert.Multiple(() =>
        {
            Assert.That(grid.Rows.Select(r => r.Name),
                Is.EqualTo(new[] { "Name", "X", "Y", "Width", "Height", "Anchor", "Dock", "TabIndex" }));
            Assert.That(grid.Header, Is.EqualTo("mystery1"));
        });
    }

    [Test]
    public void TheDescriptionPane_ShowsTheRowsDescription_AndAFrozenRowsReason()
    {
        var (_, grid) = Open();

        grid.SelectedItem = grid.Rows.Single(r => r.Name == "Enabled");
        var enabled = grid.DescriptionBody;
        grid.SelectedItem = grid.Rows.Single(r => r.Name == "Name");
        var name = grid.DescriptionBody;

        Assert.Multiple(() =>
        {
            Assert.That(grid.DescriptionTitle, Is.EqualTo("Name"));
            Assert.That(enabled, Is.EqualTo("Indicates whether the control is enabled."));
            Assert.That(name, Does.Contain("read-only"), "a frozen row keeps its reason (spec §3)");
        });
    }

    [Test]
    public void SelectingAHeader_ClearsTheDescribedRow()
    {
        var (_, grid) = Open();
        grid.SelectedItem = grid.Rows.Single(r => r.Name == "Enabled");

        grid.SelectedItem = grid.DisplayItems.OfType<FormPropertyCategoryHeader>().First();

        Assert.That(grid.SelectedRow, Is.Null);
    }

    [Test]
    public void TheObjectSelector_ListsTheFormEveryControlAndEveryComponent()
    {
        var (_, grid) = Open();

        Assert.Multiple(() =>
        {
            Assert.That(grid.Objects.Select(o => o.Display),
                Is.EqualTo(new[] { "F  Form", "lbl  Label", "btn  Button", "tmr  Timer" }));
            Assert.That(grid.SelectedObject!.Name, Is.EqualTo("lbl"));
        });
    }

    /// <summary>⛔ ONE selection store (CLAUDE.md): the grid ASKS; it never writes SelectedControl itself.</summary>
    [Test]
    public void PickingAnObject_RequestsTheSelection_AndNeverWritesSelectedControlItself()
    {
        var (file, grid) = Open();
        FormControl? requested = null;
        var requests = 0;
        grid.SelectionRequested += (_, c) => { requested = c; requests++; };

        grid.SelectedObject = grid.Objects.Single(o => o.Name == "btn");

        Assert.Multiple(() =>
        {
            Assert.That(requests, Is.EqualTo(1));
            Assert.That(requested, Is.SameAs(file.Model.FindById("btn")));
            Assert.That(grid.SelectedControl, Is.SameAs(file.Model.FindById("lbl")),
                "the store answers by setting it — the grid must not");
        });
    }

    [Test]
    public void PickingTheForm_RequestsNull()
    {
        var (_, grid) = Open();
        var requests = new List<FormControl?>();
        grid.SelectionRequested += (_, c) => requests.Add(c);

        grid.SelectedObject = grid.Objects[0];

        Assert.That(requests, Is.EqualTo(new FormControl?[] { null }), "the form is SelectInDesigner(null) (spec §3)");
    }

    [Test]
    public void AnOutsideSelectionChange_MovesTheSelector_WithoutRequestingAnything()
    {
        var (file, grid) = Open();
        var requests = 0;
        grid.SelectionRequested += (_, _) => requests++;

        grid.SelectedControl = file.Model.FindById("tmr");

        Assert.Multiple(() =>
        {
            Assert.That(grid.SelectedObject!.Name, Is.EqualTo("tmr"));
            Assert.That(requests, Is.Zero, "an echo of the store's own change is not a new request");
        });
    }

    /// <summary>
    /// A fresh item for the control ALREADY selected (a combo re-pushing an equal entry after its items are
    /// rebuilt) must request nothing — requesting would re-run SelectInDesigner and close an open Type Here
    /// editor for a click that changed nothing. This is the test the identity guard needs (M6).
    /// </summary>
    [Test]
    public void ReselectingWhatIsAlreadySelected_RequestsNothing()
    {
        var (file, grid) = Open();
        var requests = 0;
        grid.SelectionRequested += (_, _) => requests++;
        var lbl = file.Model.FindById("lbl")!;

        grid.SelectedObject = new FormObjectItem("lbl", "Label", lbl); // a NEW instance, so the setter fires

        Assert.That(requests, Is.Zero);
    }

    [Test]
    public void TheFormsRows_ComeFromFormRoot()
    {
        var (file, grid) = Open(select: null);
        var clientSize = grid.Rows.Single(r => r.Name == "ClientSize");

        Assert.Multiple(() =>
        {
            Assert.That(grid.Rows.Select(r => r.Name), Is.EquivalentTo(new[] { "Name", "Text", "ClientSize" }));
            Assert.That(clientSize.StringValue, Is.EqualTo("400, 300"));
            Assert.That(grid.Rows.Single(r => r.Name == "Text").IsBold, Is.True);
            Assert.That(clientSize.Description, Is.EqualTo("The size of the client area of the form, in pixels."),
                "the description is FormRoot's, not a copy in the grid");
        });

        clientSize.StringValue = "640, 480";

        Assert.That((file.Model.Width, file.Model.Height), Is.EqualTo((640, 480)));
    }

    [Test]
    public void ResettingTheFormsText_RemovesTheAttribute()
    {
        var (file, grid) = Open(select: null);

        grid.Rows.Single(r => r.Name == "Text").ResetCommand.Execute(null);

        Assert.That(FormDocumentWriter.Write(file), Does.Not.Contain("Text=\"Hello\""));
    }

    [Test]
    public void TheFormsClientSize_OffersNoReset()
    {
        var (_, grid) = Open(select: null);

        Assert.That(grid.Rows.Single(r => r.Name == "ClientSize").CanReset, Is.False,
            "the writer never removes Width/Height (FormRootValues.CanReset)");
    }

    /// <summary>
    /// ⛔ "0, 300" passes <c>Accepts</c> (it parses as a Size) and is then refused by
    /// <see cref="FormRootValues.Set"/>. The refusal must reach the row: no Edited, the document untouched,
    /// and the editor re-reads what the document still holds.
    /// </summary>
    [Test]
    public void ANonPositiveClientSize_IsRefused_AndRaisesNoEdit()
    {
        var (file, grid) = Open(select: null);
        var edits = 0;
        grid.Edited += (_, _) => edits++;
        var row = grid.Rows.Single(r => r.Name == "ClientSize");
        var refreshed = new List<string?>();
        row.PropertyChanged += (_, e) => refreshed.Add(e.PropertyName);

        row.StringValue = "0, 300";

        Assert.Multiple(() =>
        {
            Assert.That(edits, Is.Zero);
            Assert.That((file.Model.Width, file.Model.Height), Is.EqualTo((400, 300)));
            Assert.That(row.StringValue, Is.EqualTo("400, 300"));
            Assert.That(refreshed, Does.Contain(nameof(FormPropertyRow.StringValue)), "the editor snaps back");
        });
    }

    /// <summary>A ClientSize that writes the numbers the document already holds changed nothing.</summary>
    [Test]
    public void AClientSizeRespelling_ThatChangesNoNumber_RaisesNoEdit()
    {
        var (_, grid) = Open(select: null);
        var edits = 0;
        grid.Edited += (_, _) => edits++;

        grid.Rows.Single(r => r.Name == "ClientSize").StringValue = "400,300";

        Assert.That(edits, Is.Zero);
    }

    /// <summary>
    /// ⛔ An INTRINSIC write that changes nothing must not raise Edited: "007" on an X of 7 is not the text
    /// the row displays ("7"), so it reaches the write — which stores the same number.
    /// </summary>
    [Test]
    public void AnIntrinsicWriteThatChangesNothing_RaisesNoEdit()
    {
        var (file, grid) = Open(select: "btn");
        var edits = 0;
        grid.Edited += (_, _) => edits++;

        grid.Rows.Single(r => r.Name == "Y").StringValue = "020";
        grid.Rows.Single(r => r.Name == "Width").StringValue = "abc";

        Assert.Multiple(() =>
        {
            Assert.That(edits, Is.Zero);
            Assert.That(((PixelGeometry)file.Model.FindById("btn")!.Geometry!).Y, Is.EqualTo(20));
        });
    }

    [Test]
    public void AnIntrinsicWriteThatChangesSomething_StillRaisesOneEdit()
    {
        var (file, grid) = Open(select: "btn");
        var edits = 0;
        grid.Edited += (_, _) => edits++;

        grid.Rows.Single(r => r.Name == "Y").StringValue = "40";

        Assert.Multiple(() =>
        {
            Assert.That(edits, Is.EqualTo(1));
            Assert.That(((PixelGeometry)file.Model.FindById("btn")!.Geometry!).Y, Is.EqualTo(40));
        });
    }

    /// <summary>
    /// ⛔ A frozen ClientSize shows the document's own text — its reason says the attributes are "preserved
    /// exactly as written", and an empty box beside that sentence contradicts it.
    /// </summary>
    [Test]
    public void AFrozenClientSize_ShowsTheDocumentsRawText()
    {
        var (_, grid) = Open(select: null, doc: """
            <Form Name="F" Version="1" Width="abc" Height="300"><Controls/></Form>
            """);
        var row = grid.Rows.Single(r => r.Name == "ClientSize");

        Assert.Multiple(() =>
        {
            Assert.That(row.IsFrozen, Is.True);
            Assert.That(row.StringValue, Is.EqualTo("Width=\"abc\" Height=\"300\""));
            Assert.That(row.FrozenReason, Does.Contain("preserved exactly as written"));
            Assert.That(row.IsDefaultShown, Is.False, "the document carries it — it is not a greyed default");
        });
    }

    /// <summary>
    /// A web page with no &lt;Layout&gt; now offers Cols/Rows/Gap; the first write creates the layout, and the
    /// document it writes must read back with that value.
    /// </summary>
    [Test]
    public void AWebFormWithNoLayout_FirstTrackWrite_WritesAValidLayout()
    {
        var (file, grid) = Open(select: null, name: "F.blwebform", doc: """
            <WebForm Name="F" Version="1">
              <Controls/>
            </WebForm>
            """);
        var edits = 0;
        grid.Edited += (_, _) => edits++;

        Assert.That(grid.Rows.Select(r => r.Name), Is.EquivalentTo(new[] { "Name", "Text", "Cols", "Rows", "Gap" }));

        grid.Rows.Single(r => r.Name == "Cols").StringValue = "120px,1fr";
        var written = FormDocumentWriter.Write(file);
        var reread = FormDocumentReader.Read("F.blwebform", written);

        Assert.Multiple(() =>
        {
            Assert.That(edits, Is.EqualTo(1));
            Assert.That(written, Does.Contain("<Layout"));
            Assert.That(reread.Diagnostics, Is.Empty);
            Assert.That(reread.Model.Layout?.Cols, Is.EqualTo("120px,1fr"));
            Assert.That(reread.Model.Layout?.Kind, Is.EqualTo(FormLayoutKind.Grid));
        });
    }

    [Test]
    public void TheDocumentViewModel_RoutesTheSelectorThroughTheOneSelectionStore()
    {
        var vm = new CodeEditorDocumentViewModel(new Mock<IFileService>().Object, new Mock<IEventAggregator>().Object)
        {
            FilePath = "/proj/F.blform"
        };
        vm.SetContent(Doc);
        Assert.That(vm.EnterDesignModeForFormDocument(), Is.True);
        var requests = 0;
        vm.PropertyGrid.SelectionRequested += (_, _) => requests++;

        vm.PropertyGrid.SelectedObject = vm.PropertyGrid.Objects.Single(o => o.Name == "tmr");

        Assert.Multiple(() =>
        {
            Assert.That(requests, Is.EqualTo(1), "the store's answer is an echo, not a second request");
            Assert.That(vm.Selection.Primary?.Id, Is.EqualTo("tmr"), "the canvas's store selected it");
            Assert.That(vm.PropertyGrid.SelectedControl?.Id, Is.EqualTo("tmr"));
            Assert.That(vm.Tray.Items.Single(i => i.Id == "tmr").IsSelected, Is.True, "and the tray agrees");
        });

        vm.PropertyGrid.SelectedObject = vm.PropertyGrid.Objects[0];

        Assert.Multiple(() =>
        {
            Assert.That(vm.Selection.Primary, Is.Null, "picking the form clears the store");
            Assert.That(vm.PropertyGrid.SelectedControl, Is.Null);
            Assert.That(vm.PropertyGrid.Rows.Select(r => r.Name), Does.Contain("ClientSize"));
        });
    }

    [Test]
    public void LoadingOverASelection_BuildsTheRowsOnce()
    {
        var (file, grid) = Open(select: "lbl");
        var rebuilds = 0;
        grid.Rows.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                rebuilds++;
            }
        };

        grid.Load(file);

        Assert.Multiple(() =>
        {
            Assert.That(rebuilds, Is.EqualTo(1));
            Assert.That(grid.Rows.Select(r => r.Name), Does.Contain("ClientSize"), "the form's rows");
        });
    }

    [Test]
    public void PlacingAControl_ThroughTheDocumentViewModel_PutsItInTheSelector()
    {
        var vm = new CodeEditorDocumentViewModel(new Mock<IFileService>().Object, new Mock<IEventAggregator>().Object)
        {
            FilePath = "/proj/F.blform"
        };
        vm.SetContent(Doc);
        Assert.That(vm.EnterDesignModeForFormDocument(), Is.True);
        var before = vm.PropertyGrid.Objects.Count;

        Assert.That(vm.PlaceControl("CheckBox", 50, 50), Is.Null, "fixture premise: the drop is accepted");

        var placed = vm.Selection.Primary!;
        Assert.Multiple(() =>
        {
            Assert.That(vm.PropertyGrid.Objects, Has.Count.EqualTo(before + 1));
            Assert.That(vm.PropertyGrid.Objects.Select(o => o.Control), Has.Member(placed));
            Assert.That(vm.PropertyGrid.SelectedObject?.Control, Is.SameAs(placed), "and it is the one selected");
        });
    }
}
