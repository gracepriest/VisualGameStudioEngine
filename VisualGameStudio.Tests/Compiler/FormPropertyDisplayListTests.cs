using BasicLang.Forms;
using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// The grid's display projection on its own (spec §3): headers or a flat list, sorted, searched, collapsed —
/// and the selection it keeps across a rebuild. Slice 5's Events tab reuses it, so it is tested without a grid.
/// </summary>
[TestFixture]
public class FormPropertyDisplayListTests
{
    private static FormPropertyRow Row(string name, string category) =>
        new(name, FormPropertyType.String, () => "", null, () => { }, category: category);

    private static readonly FormPropertyRow Enabled = Row("Enabled", "Behavior");
    private static readonly FormPropertyRow Visible = Row("Visible", "Behavior");
    private static readonly FormPropertyRow BackColor = Row("BackColor", "Appearance");
    private static readonly FormPropertyRow ForeColor = Row("ForeColor", "Appearance");
    private static readonly FormPropertyRow Text = Row("text", "appearance");

    // Catalog order, deliberately not sorted.
    private static readonly FormPropertyRow[] Rows = { Visible, Enabled, ForeColor, BackColor };

    private static List<string> Lines(FormPropertyDisplayList list) =>
        list.Items.Select(i => i switch
        {
            FormPropertyCategoryHeader h => "[" + h.Name + "]",
            IFormDisplayRow r => r.Name,
            _ => "?"
        }).ToList();

    private static FormPropertyCategoryHeader Header(FormPropertyDisplayList list, string name) =>
        list.Items.OfType<FormPropertyCategoryHeader>().Single(h => h.Name == name);

    [Test]
    public void Categorized_SortsHeadersAndRowsByName()
    {
        var list = new FormPropertyDisplayList();

        list.Refresh(Rows, "", isCategorized: true, selected: null);

        Assert.That(Lines(list),
            Is.EqualTo(new[] { "[Appearance]", "BackColor", "ForeColor", "[Behavior]", "Enabled", "Visible" }));
    }

    [Test]
    public void Categories_SortCaseInsensitively_LikeRows()
    {
        var list = new FormPropertyDisplayList();

        list.Refresh(new[] { Enabled, Text }, "", isCategorized: true, selected: null);

        Assert.That(Lines(list), Is.EqualTo(new[] { "[appearance]", "text", "[Behavior]", "Enabled" }));
    }

    [Test]
    public void Alphabetical_IsFlat()
    {
        var list = new FormPropertyDisplayList();

        list.Refresh(Rows, "", isCategorized: false, selected: null);

        Assert.That(Lines(list), Is.EqualTo(new[] { "BackColor", "Enabled", "ForeColor", "Visible" }));
    }

    [Test]
    public void Search_FiltersByName_AndDropsEmptyCategories()
    {
        var list = new FormPropertyDisplayList();

        list.Refresh(Rows, "COLOR", isCategorized: true, selected: null);

        Assert.That(Lines(list), Is.EqualTo(new[] { "[Appearance]", "BackColor", "ForeColor" }));
    }

    [Test]
    public void TheSelectedRow_IsKept_WhileItIsStillShown()
    {
        var list = new FormPropertyDisplayList();
        list.Refresh(Rows, "", isCategorized: true, selected: null);

        Assert.Multiple(() =>
        {
            Assert.That(list.Refresh(Rows, "en", isCategorized: true, selected: Enabled), Is.SameAs(Enabled));
            Assert.That(list.Refresh(Rows, "en", isCategorized: false, selected: Enabled), Is.SameAs(Enabled));
            Assert.That(list.Refresh(Rows, "color", isCategorized: false, selected: Enabled), Is.Null,
                "a row the search hides is not selected any more");
        });
    }

    [Test]
    public void ASelectedHeader_IsKept_AsTheRebuiltHeaderOfTheSameName()
    {
        var list = new FormPropertyDisplayList();
        list.Refresh(Rows, "", isCategorized: true, selected: null);
        var old = Header(list, "Behavior");

        var kept = list.Refresh(Rows, "", isCategorized: true, selected: old);

        Assert.That(kept, Is.SameAs(Header(list, "Behavior")));
    }

    [Test]
    public void ASelectedHeader_IsCleared_InTheFlatView()
    {
        var list = new FormPropertyDisplayList();
        list.Refresh(Rows, "", isCategorized: true, selected: null);

        Assert.That(list.Refresh(Rows, "", isCategorized: false, selected: Header(list, "Behavior")), Is.Null);
    }

    [Test]
    public void Collapse_IsRememberedByName_AcrossRefreshes()
    {
        var list = new FormPropertyDisplayList();
        list.Refresh(Rows, "", isCategorized: true, selected: null);

        Header(list, "Behavior").IsExpanded = false;
        list.Refresh(Rows, "", isCategorized: true, selected: null);

        Assert.Multiple(() =>
        {
            Assert.That(Lines(list), Is.EqualTo(new[] { "[Appearance]", "BackColor", "ForeColor", "[Behavior]" }));
            Assert.That(list.IsCollapsed("Behavior"), Is.True);
        });
    }

    [Test]
    public void ASearch_ShowsAMatchingCollapsedCategoryExpanded_WithoutForgettingTheCollapse()
    {
        var list = new FormPropertyDisplayList();
        list.Refresh(Rows, "", isCategorized: true, selected: null);
        Header(list, "Behavior").IsExpanded = false;

        list.Refresh(Rows, "enab", isCategorized: true, selected: null);
        var searching = Lines(list);
        var collapsedWhileSearching = list.IsCollapsed("Behavior");

        list.Refresh(Rows, "", isCategorized: true, selected: null);

        Assert.Multiple(() =>
        {
            Assert.That(searching, Is.EqualTo(new[] { "[Behavior]", "Enabled" }), "never a lone header");
            Assert.That(collapsedWhileSearching, Is.True, "the search is display only");
            Assert.That(Lines(list), Has.No.Member("Enabled"), "clearing the search restores the collapse");
        });
    }

    [Test]
    public void TogglingAHeader_EditsInPlace_AndExpandingRestoresTheRows()
    {
        var list = new FormPropertyDisplayList();
        list.Refresh(Rows, "", isCategorized: true, selected: null);
        var before = Lines(list);
        var appearance = Header(list, "Appearance");

        appearance.IsExpanded = false;
        var collapsed = Lines(list);
        appearance.IsExpanded = true;

        Assert.Multiple(() =>
        {
            Assert.That(collapsed, Is.EqualTo(new[] { "[Appearance]", "[Behavior]", "Enabled", "Visible" }));
            Assert.That(Lines(list), Is.EqualTo(before));
            Assert.That(list.Items[0], Is.SameAs(appearance), "the header under the click is not recreated");
        });
    }

    /// <summary>Not a FormPropertyRow — what slice 5's Events tab will hand the list.</summary>
    private sealed record FakeRow(string Name, string Category) : IFormDisplayRow;

    [Test]
    public void AnyDisplayRow_IsGroupedSearchedAndCollapsed_NotOnlyAPropertyRow()
    {
        var click = new FakeRow("Click", "Action");
        var doubleClick = new FakeRow("DoubleClick", "Action");
        var keyDown = new FakeRow("KeyDown", "Key");
        var rows = new IFormDisplayRow[] { keyDown, doubleClick, click };
        var list = new FormPropertyDisplayList();

        list.Refresh(rows, "", isCategorized: true, selected: null);
        var grouped = Lines(list);

        list.Refresh(rows, "click", isCategorized: true, selected: null);
        var searched = Lines(list);

        list.Refresh(rows, "", isCategorized: true, selected: null);
        Header(list, "Action").IsExpanded = false;
        var collapsed = Lines(list);

        Assert.Multiple(() =>
        {
            Assert.That(grouped, Is.EqualTo(new[] { "[Action]", "Click", "DoubleClick", "[Key]", "KeyDown" }));
            Assert.That(searched, Is.EqualTo(new[] { "[Action]", "Click", "DoubleClick" }));
            Assert.That(collapsed, Is.EqualTo(new[] { "[Action]", "[Key]", "KeyDown" }),
                "the collapse removes every row under the header, whatever its type");
        });
    }

    /// <summary>
    /// ⛔ While searching, every matching category is shown expanded REGARDLESS of the remembered collapse, so a
    /// toggle then is display only — collapse-then-expand during a search must not erase a pre-search collapse.
    /// </summary>
    [Test]
    public void HeaderTogglesWhileSearching_DoNotChangeTheRememberedCollapse()
    {
        var list = new FormPropertyDisplayList();
        list.Refresh(Rows, "", isCategorized: true, selected: null);
        Header(list, "Behavior").IsExpanded = false;
        list.Refresh(Rows, "en", isCategorized: true, selected: null);

        Header(list, "Behavior").IsExpanded = false;
        var collapsedInSearch = Lines(list);
        Header(list, "Behavior").IsExpanded = true;
        list.Refresh(Rows, "", isCategorized: true, selected: null);

        Assert.Multiple(() =>
        {
            Assert.That(collapsedInSearch, Is.EqualTo(new[] { "[Behavior]" }), "the toggle still works on screen");
            Assert.That(list.IsCollapsed("Behavior"), Is.True, "the pre-search collapse survives");
            Assert.That(Lines(list), Has.No.Member("Enabled"));
        });
    }

    [Test]
    public void ACollapseThatHidesRows_IsReported()
    {
        var list = new FormPropertyDisplayList();
        list.Refresh(Rows, "", isCategorized: true, selected: null);
        var raised = 0;
        list.RowsHidden += (_, _) => raised++;

        Header(list, "Behavior").IsExpanded = false;
        Header(list, "Behavior").IsExpanded = true;

        Assert.That(raised, Is.EqualTo(1), "only the collapse hides rows");
    }
}
