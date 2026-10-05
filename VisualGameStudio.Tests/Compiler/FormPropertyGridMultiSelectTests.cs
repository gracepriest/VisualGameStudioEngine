using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Property-grid slice 6 (pre-flight 2026-10-05) Task 2: the grid takes the SET — plumbing (D-1), which rows a
/// multi-selection offers (D-2), what they show (D-3), the selector and header (D-6), strips (D-7).
///
/// <para>⛔ One fixture, the same ids everywhere (CLAUDE.md: ids that differ only by text make a test vacuous):
/// <c>btn</c>/<c>btn2</c> (Buttons), <c>lbl</c> (Label), <c>txt</c> (TextBox), <c>tmr</c> (Timer).</para>
/// </summary>
[TestFixture]
public partial class FormPropertyGridMultiSelectTests
{
    internal const string MultiDoc = """
        <Form Name="GridForm" Version="1" Width="640" Height="480" Text="GridForm">
          <Controls>
            <Button Id="btn" X="16" Y="16" Width="75" Height="23" TabIndex="0" Text="OK" BackColor="Red" ForeColor="Blue"/>
            <Button Id="btn2" X="16" Y="56" Width="75" Height="23" TabIndex="1" Text="OK" BackColor="Red"/>
            <Label Id="lbl" X="120" Y="16" Width="100" Height="23" TabIndex="2" Text="Hello"/>
            <TextBox Id="txt" X="120" Y="56" Width="100" Height="23" TabIndex="3"/>
          </Controls>
          <Components><Timer Id="tmr"/></Components>
        </Form>
        """;

    private static (FormFile File, FormPropertyGridViewModel Grid) OpenDoc(string doc, params string[] select)
    {
        var file = FormDocumentReader.Read("GridForm.blform", doc);
        Assert.That(file.IsRefused, Is.False, "precondition: the fixture reads — " +
                                               string.Join("; ", file.Diagnostics.Select(d => d.ToString())));
        var grid = new FormPropertyGridViewModel();
        grid.Load(file);
        grid.SetSelection(select.Select(id => file.Model.FindById(id)
                                              ?? throw new InvalidOperationException($"no '{id}' in the fixture")).ToList());
        return (file, grid);
    }

    private static (FormFile File, FormPropertyGridViewModel Grid) Open(params string[] select) => OpenDoc(MultiDoc, select);

    private static List<string> Names(FormPropertyGridViewModel grid) => grid.Rows.Select(r => r.Name).ToList();

    private static FormPropertyRow Row(FormPropertyGridViewModel grid, string name) => grid.Rows.Single(r => r.Name == name);

    // ==================================================================
    // D-1 — the set, the primary, one rebuild
    // ==================================================================

    [Test]
    public void SetSelection_HoldsTheSetInSelectionOrder_AndTheSelectedControlIsThePrimary()
    {
        var (file, grid) = Open("btn", "btn2");

        Assert.Multiple(() =>
        {
            Assert.That(grid.SelectedControls, Is.EqualTo(new[] { file.Model.FindById("btn"), file.Model.FindById("btn2") }));
            Assert.That(grid.IsMultiSelection, Is.True);
            Assert.That(grid.SelectedControl, Is.SameAs(file.Model.FindById("btn2")), "the PRIMARY (selected last), never null");
        });
    }

    [Test]
    public void SetSelection_CopiesTheList_SoTheStoresLaterChangeIsNotSeen()
    {
        var file = FormDocumentReader.Read("GridForm.blform", MultiDoc);
        var grid = new FormPropertyGridViewModel();
        grid.Load(file);
        var live = new List<FormControl> { file.Model.FindById("btn")!, file.Model.FindById("btn2")! };

        grid.SetSelection(live);
        live.Add(file.Model.FindById("lbl")!);

        Assert.That(grid.SelectedControls, Has.Count.EqualTo(2));
    }

    [Test]
    public void SetSelection_RebuildsOnce_AndIsANoOpForTheSameList()
    {
        var file = FormDocumentReader.Read("GridForm.blform", MultiDoc);
        var grid = new FormPropertyGridViewModel();
        grid.Load(file);
        var resets = 0;
        grid.Rows.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) resets++;
        };

        grid.SetSelection(new[] { file.Model.FindById("btn")!, file.Model.FindById("btn2")! });
        Assert.That(resets, Is.EqualTo(1), "one rebuild for a new selection");

        grid.SetSelection(new[] { file.Model.FindById("btn")!, file.Model.FindById("btn2")! }); // a NEW list, same elements
        Assert.That(resets, Is.EqualTo(1), "the same selection again rebuilds nothing");

        grid.SetSelection(new[] { file.Model.FindById("btn2")!, file.Model.FindById("btn")! });
        Assert.That(resets, Is.EqualTo(2), "the same members in another ORDER is another primary: a rebuild");
    }

    [Test]
    public void TheCanvasEchoOfThePrimary_RebuildsNothing_AndAnotherControlMeansExactlyThatOne()
    {
        var (file, grid) = Open("btn", "btn2");
        var resets = 0;
        grid.Rows.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) resets++;
        };

        grid.SelectedControl = file.Model.FindById("btn2"); // the canvas's TwoWay echo
        Assert.Multiple(() =>
        {
            Assert.That(resets, Is.Zero);
            Assert.That(grid.SelectedControls, Has.Count.EqualTo(2), "the echo does not collapse the set");
        });

        grid.SelectedControl = file.Model.FindById("lbl");
        Assert.Multiple(() =>
        {
            Assert.That(resets, Is.EqualTo(1));
            Assert.That(grid.SelectedControls, Is.EqualTo(new[] { file.Model.FindById("lbl") }));
            Assert.That(grid.IsMultiSelection, Is.False);
        });
    }

    [Test]
    public void ASelectionOfOne_ShowsExactlyTheSingleSelectionRows()
    {
        var (file, single) = Open("btn");
        var old = new FormPropertyGridViewModel();
        old.Load(file);
        old.SelectedControl = file.Model.FindById("btn");

        Assert.Multiple(() =>
        {
            Assert.That(Names(single), Is.EqualTo(Names(old)), "a selection of one runs today's path unchanged");
            Assert.That(Names(single), Does.Contain("Name").And.Contain("TabIndex"));
            Assert.That(single.Rows.Any(r => r.IsMerged), Is.False);
        });
    }

    // ==================================================================
    // D-2 — which rows
    // ==================================================================

    [Test]
    public void TwoButtons_OfferLocationSizeAnchorDock_AndNeverNameOrTabIndex()
    {
        var (_, grid) = Open("btn", "btn2");
        var names = Names(grid);

        Assert.Multiple(() =>
        {
            Assert.That(names, Does.Contain("Location").And.Contain("Size").And.Contain("Anchor").And.Contain("Dock"),
                "D-2 rule 4: Location IS offered (Control.Location is mergeable, M1)");
            Assert.That(names, Does.Not.Contain("Name"), "VS hides (Name) for a multi-selection");
            Assert.That(names, Does.Not.Contain("TabIndex"), "Control.TabIndex is [MergableProperty(false)] (M1)");
            Assert.That(names, Does.Contain("Text").And.Contain("BackColor").And.Contain("TextAlign"));
            Assert.That(grid.Rows.All(r => r.IsMerged), Is.True, "every row of a multi-selection is a merged row");
        });
    }

    [Test]
    public void AButtonAndATextBox_OfferNoTextAlign_TheirAlignmentsAreDifferentTypes()
    {
        var (_, grid) = Open("btn", "txt");

        Assert.Multiple(() =>
        {
            Assert.That(Names(grid), Does.Not.Contain("TextAlign"), "ContentAlignment x HorizontalAlignment (D-2 rule 1)");
            Assert.That(Names(grid), Does.Contain("Text").And.Contain("Font").And.Contain("BackColor"));
        });
    }

    [Test]
    public void TheMergedRows_FollowThePrimarysOrder()
    {
        var (_, a) = Open("lbl", "btn");
        var (_, b) = Open("btn", "lbl");

        Assert.Multiple(() =>
        {
            Assert.That(Names(a), Is.EquivalentTo(Names(b)), "the same intersection either way round");
            var buttonOrder = Names(Open("btn").Grid).Where(Names(a).Contains).ToList();
            Assert.That(Names(a), Is.EqualTo(buttonOrder), "primary btn: the Button's order");
            var labelOrder = Names(Open("lbl").Grid).Where(Names(b).Contains).ToList();
            Assert.That(Names(b), Is.EqualTo(labelOrder), "primary lbl: the Label's order");
        });
    }

    /// <summary>
    /// ⛔ Catalog-driven (the plan's sweep): for EVERY pair of kinds of either target, the offered names equal the D-2
    /// intersection computed here from the catalog — same row (<see cref="FormPropertyDef.SharesShapeWith"/>), mergeable,
    /// applying on the target; the intrinsic geometry rows by geometry, never Name or TabIndex — in the PRIMARY's order.
    /// One test over a loop (a TestCaseSource of this size stops `--filter` selecting — Task 1's lesson).
    /// </summary>
    [Test]
    public void EveryPairOfKinds_OnBothTargets_OffersExactlyTheD2Intersection_InThePrimarysOrder()
    {
        var findings = new List<string>();
        var pairs = 0;

        foreach (var target in new[] { FormTarget.WinForms, FormTarget.Web })
        {
            var kinds = FormControlCatalog.For(target).ToList();
            foreach (var first in kinds)
            {
                foreach (var primary in kinds)
                {
                    var document = new FormDocument { Target = target, Name = "GridForm", Width = 640, Height = 480 };
                    var a = FormCatalogShapes.Canonical(document, first, "a");
                    var b = FormCatalogShapes.Canonical(document, primary, "b");
                    var name = target == FormTarget.Web ? "GridForm.blwebform" : "GridForm.blform";
                    var file = FormDocumentReader.Read(name, FormDocumentWriter.Create(document));
                    var ca = file.Model.FindById("a")!;
                    var cb = file.Model.FindById("b")!;

                    var grid = new FormPropertyGridViewModel();
                    grid.Load(file);
                    grid.SetSelection(new[] { ca, cb });
                    pairs++;

                    var expected = Geometry(cb).Where(Geometry(ca).Contains)
                        .Concat(primary.Properties
                            .Where(p => p.AppliesTo(target) && p.Mergeable &&
                                        first.Properties.Any(q => q.AppliesTo(target) && q.Mergeable && q.SharesShapeWith(p)))
                            .Select(p => p.Name))
                        .ToList();

                    var actual = Names(grid);
                    if (!actual.SequenceEqual(expected))
                    {
                        findings.Add($"{target} {first.Kind}+{primary.Kind}: offered [{string.Join(",", actual)}], " +
                                     $"expected [{string.Join(",", expected)}]");
                    }
                }
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(pairs, Is.GreaterThan(500), "precondition: the sweep swept");
            Assert.That(findings, Is.Empty, string.Join("\n", findings.Take(40)));
        });

        static IEnumerable<string> Geometry(FormControl c) => c.Geometry switch
        {
            PixelGeometry => new[] { "Location", "Size", "Anchor", "Dock" },
            GridGeometry => new[] { "Col", "Row" },
            _ => Array.Empty<string>()
        };
    }

    [Test]
    public void AStripInTheSelection_DropsEveryGeometryRow()
    {
        const string doc = """
            <Form Name="GridForm" Version="1" Width="640" Height="480" Text="GridForm">
              <Controls>
                <MenuStrip Id="ms" Dock="Top"/>
                <Button Id="btn" X="16" Y="40" Width="75" Height="23" TabIndex="0"/>
              </Controls>
            </Form>
            """;
        var (_, grid) = OpenDoc(doc, "btn", "ms");

        Assert.That(Names(grid), Has.None.AnyOf("Location", "Size", "Anchor", "Dock", "Col", "Row", "TabIndex", "Name"),
            "D-7: a strip has no geometry, so the intersection has none (D-2 rule 3, not a special case)");
    }

    // ==================================================================
    // D-3 — what the merged rows show
    // ==================================================================

    [Test]
    public void AValueEveryMemberShows_IsShown_AndADifferingOneIsBlankAndMixed()
    {
        var (_, buttons) = Open("btn", "btn2");
        var (_, mixed) = Open("btn", "lbl");

        Assert.Multiple(() =>
        {
            Assert.That(Row(buttons, "Text").DisplayValue, Is.EqualTo("OK"));
            Assert.That(Row(buttons, "Text").IsMixed, Is.False);
            Assert.That(Row(buttons, "Text").StringValue, Is.EqualTo("OK"), "the editor reads the shared value");
            Assert.That(Row(mixed, "Text").DisplayValue, Is.Empty, "OK vs Hello: blank, never the primary's value");
            Assert.That(Row(mixed, "Text").IsMixed, Is.True);
            Assert.That(Row(mixed, "Text").StringValue, Is.Empty);
            Assert.That(Row(buttons, "Location").DisplayValue, Is.Empty, "16,16 vs 16,56");
            Assert.That(Row(buttons, "Size").DisplayValue, Is.EqualTo("75, 23"), "both 75x23");
            Assert.That(Row(buttons, "Location").Children.Select(c => (c.Name, c.DisplayValue)),
                Is.EqualTo(new[] { ("X", "16"), ("Y", "") }), "each PART merges over the members' own parts");
        });
    }

    [Test]
    public void TwoAbsentRows_WhoseKindsDefaultDifferently_AreBlankAndMixed_AndNotGrey()
    {
        var (_, grid) = Open("lbl", "txt");

        var differing = grid.Rows.Where(r => r.Members.All(m => !m.IsPresent) &&
                                             r.Members[0].DisplayValue != r.Members[1].DisplayValue).ToList();
        var agreeing = grid.Rows.Where(r => r.Members.All(m => !m.IsPresent) && !r.Members[0].IsFrozen &&
                                            r.Members[0].DisplayValue == r.Members[1].DisplayValue &&
                                            r.Members.All(m => m.IsDefaultShown)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(differing, Is.Not.Empty, "precondition: a Label and a TextBox default some shared row differently");
            foreach (var row in differing)
            {
                Assert.That(row.DisplayValue, Is.Empty, $"{row.Name}: blank");
                Assert.That(row.IsMixed, Is.True, $"{row.Name}: mixed");
                Assert.That(row.IsDefaultShown, Is.False, $"{row.Name}: a mixed row is never grey");
            }

            Assert.That(agreeing, Is.Not.Empty, "precondition: some shared row defaults the same on both");
            foreach (var row in agreeing)
            {
                Assert.That(row.DisplayValue, Is.EqualTo(row.Members[0].DisplayValue), $"{row.Name}: the shared default");
                Assert.That(row.IsDefaultShown, Is.True, $"{row.Name}: grey when every member is absent and agrees");
            }
        });
    }

    [Test]
    public void Bold_IsAnyMember_AndReset_IsOfferedOnlyWhenEveryMemberCanReset()
    {
        var (_, grid) = Open("btn", "btn2");
        var backColor = Row(grid, "BackColor"); // Red on both
        var foreColor = Row(grid, "ForeColor"); // Blue on btn, absent on btn2

        Assert.Multiple(() =>
        {
            Assert.That(backColor.IsBold, Is.True);
            Assert.That(backColor.CanReset, Is.True, "both set: both can reset");
            Assert.That(backColor.IsDefaultShown, Is.False);

            Assert.That(foreColor.IsBold, Is.True, "VS: bold when ANY member would serialize");
            Assert.That(foreColor.CanReset, Is.False, "VS: Reset only when EVERY member can — btn2 has nothing to reset");
            Assert.That(foreColor.IsMixed, Is.True);
            Assert.That(foreColor.IsDefaultShown, Is.False, "mixed is never grey");
        });
    }

    [Test]
    public void AFrozenMember_FreezesTheSet_AndTheReasonNamesIt()
    {
        var doc = MultiDoc.Replace("""Text="OK" BackColor="Red"/>""", """Text="OK" BackColor="Red" Enabled="maybe"/>""");
        var (file, grid) = OpenDoc(doc, "btn", "btn2");
        Assert.That(file.DegradedReason("btn2", "Enabled"), Is.Not.Null, "precondition: Enabled=\"maybe\" is Degraded");

        var enabled = Row(grid, "Enabled");
        Assert.Multiple(() =>
        {
            Assert.That(enabled.IsFrozen, Is.True);
            Assert.That(enabled.FrozenReason, Does.StartWith("'btn2': "), "the member is named");
            Assert.That(enabled.CanReset, Is.False);
            Assert.That(enabled.DisplayValue, Is.Empty, "btn shows its default, btn2 its preserved text: blank");
        });
    }

    // ==================================================================
    // D-6 — selector, header, description
    // ==================================================================

    [Test]
    public void TheHeader_IsBlank_ForAMultiSelection()
    {
        var (_, grid) = Open("btn", "btn2");

        Assert.Multiple(() =>
        {
            Assert.That(grid.Header, Is.Empty);
            Assert.That(grid.HeaderKind, Is.Empty);
        });
    }

    /// <summary>D-6: an EMPTY Events intersection (a Button and a Timer share no event) is said in the pane.</summary>
    [Test]
    public void TheEventsTab_SaysWhenTheSelectionSharesNoEvents()
    {
        var (_, grid) = Open("btn", "tmr");

        grid.IsEventsMode = true;

        Assert.Multiple(() =>
        {
            Assert.That(grid.EventRows, Is.Empty);
            Assert.That(grid.DescriptionBody, Is.EqualTo("The selected controls share no events on WinForms."));
        });
    }

    [Test]
    public void ThePropertiesPane_PromptsForARow_InAMultiSelection()
    {
        var (_, grid) = Open("btn", "lbl");

        Assert.That(grid.DescriptionBody, Is.EqualTo("Select a property to see what it does."));
    }

    // ==================================================================
    // The host never writes the grid's SelectedControl (D-1)
    // ==================================================================

    [Test]
    public void TheDocumentViewModel_NeverAssignsTheGridsSelectedControl()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? path = null;
        while (dir != null && path == null)
        {
            var candidate = Path.Combine(dir.FullName, "VisualGameStudio.Shell", "ViewModels", "Documents",
                "CodeEditorDocumentViewModel.cs");
            path = File.Exists(candidate) ? candidate : null;
            dir = dir.Parent;
        }

        Assert.That(path, Is.Not.Null, "a source gate that cannot find its file must FAIL");
        var source = File.ReadAllText(path!);
        // Any spelling of an assignment — `PropertyGrid.SelectedControl =`, `PropertyGrid!.SelectedControl=`, `?.` — but
        // not a comparison (`==`) or a read.
        var assignment = new System.Text.RegularExpressions.Regex(@"PropertyGrid\s*[!?]?\s*\.\s*SelectedControl\s*=(?!=)");
        Assert.Multiple(() =>
        {
            Assert.That(assignment.IsMatch("PropertyGrid!.SelectedControl=x;") && assignment.IsMatch("PropertyGrid .SelectedControl = x;") &&
                        !assignment.IsMatch("PropertyGrid.SelectedControl == x"), Is.True, "precondition: the pin's own pattern");
            Assert.That(assignment.Matches(source).Select(m => m.Value), Is.Empty,
                "slice 6 D-1: the host hands the grid the SET (SetSelection); only the canvas binding writes SelectedControl");
            Assert.That(source, Does.Contain("PropertyGrid.SetSelection(Selection.Controls)"));
        });
    }
}
