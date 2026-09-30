using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec §2.5/§5 — a row's events are a LIST, the old single-event fields are derived from its default
/// entry, and <see cref="FormEvents.WiredOn"/> is the ONE answer to "which events are wired on this
/// target" that the emitter, BL8032 and (slice 5) the grid all ask.
/// </summary>
[TestFixture]
public class FormEventsTests
{
    [Test]
    public void EveryRowThatDeclaresEvents_HasExactlyOneDefaultEvent()
    {
        var offenders = FormControlCatalog.All
            .Where(d => d.Events != null && d.Events.Count(e => e.IsDefault) != 1)
            .Select(d => d.Kind);

        Assert.That(offenders, Is.Empty,
            "a row that declares events must name exactly one default — a double-click means one event, " +
            "and with none the derived WinFormsEvent/WebEvent go null and the gesture refuses");
    }

    // ==================================================================
    // Synthetic rows — each pins one guard of the seam, so removing it goes red.
    // ==================================================================

    private static FormControlDef Synthetic(
        string? htmlTag, IReadOnlyList<FormEventDef> events,
        FormPlace place = FormPlace.Positioned, FormWebScript? webScript = null) =>
        new(Kind: "Synthetic", WinFormsType: "Synthetic", HtmlTag: htmlTag, HtmlInputType: null,
            IsContainer: false, Properties: Array.Empty<FormPropertyDef>(),
            Events: events, Place: place, WebScript: webScript);

    [Test]
    public void TheDerivedAccessors_ReadTheIsDefaultEntry_NotTheFirst()
    {
        var row = Synthetic("div", new[]
        {
            new FormEventDef("MouseEnter", WebEvent: "mouseenter"),
            new FormEventDef("Click", "MouseEventArgs", "click", IsDefault: true)
        });

        Assert.Multiple(() =>
        {
            Assert.That(row.WinFormsEvent, Is.EqualTo("Click"));
            Assert.That(row.WebEvent, Is.EqualTo("click"));
            Assert.That(row.WinFormsEventArgs, Is.EqualTo("MouseEventArgs"));
        });
    }

    [Test]
    public void WiredOn_TheWeb_DropsAnEventWithNoWebName()
    {
        var row = Synthetic("div", new[]
        {
            new FormEventDef("Paint", "PaintEventArgs", IsDefault: true),
            new FormEventDef("Click", WebEvent: "click")
        });

        Assert.That(FormEvents.WiredOn(row, FormTarget.Web).Select(e => e.Name), Is.EqualTo(new[] { "Click" }),
            "an event with no DOM name has nothing addEventListener could register");
    }

    [Test]
    public void WiredOn_TheWeb_IsEmpty_ForAKindTheWebDoesNotHave()
    {
        var row = Synthetic(htmlTag: null, new[] { new FormEventDef("Click", WebEvent: "click", IsDefault: true) });

        Assert.That(FormEvents.WiredOn(row, FormTarget.Web), Is.Empty,
            "no tag and no WebScript: the kind is absent on the web, whatever its events say");
    }

    [Test]
    public void WiredOn_TheWeb_ForAComponent_IsOnlyItsDefaultEvent()
    {
        var row = Synthetic(htmlTag: null, new[]
        {
            new FormEventDef("Disposed", WebEvent: "disposed"),
            new FormEventDef("Tick", WebEvent: "tick", IsDefault: true)
        }, FormPlace.Tray, new FormWebScript("Integer", "w.setInterval(AddressOf {handler}, 100)"));

        Assert.That(FormEvents.WiredOn(row, FormTarget.Web).Select(e => e.Name), Is.EqualTo(new[] { "Tick" }),
            "a web component is wired only through its template, on its default event — offering any " +
            "other would be offering a bind the emitter drops");
    }

    [Test]
    public void WiredOn_TheWeb_ForEveryCatalogComponent_IsItsDefaultEventOrNothing()
    {
        Assert.Multiple(() =>
        {
            foreach (var definition in FormControlCatalog.All.Where(d => d.IsComponent))
            {
                var wired = FormEvents.WiredOn(definition, FormTarget.Web)
                    .Select(e => FormEvents.NameOn(e, FormTarget.Web)).ToList();
                var expected = definition.WebScript != null && definition.DefaultEvent(FormTarget.Web) != null
                    ? new[] { definition.DefaultEvent(FormTarget.Web) }
                    : Array.Empty<string?>();

                Assert.That(wired, Is.EqualTo(expected), definition.Kind);
            }
        });
    }

    [Test]
    public void TheDerivedAccessors_ReadTheDefaultEntry()
    {
        var button = FormControlCatalog.Find("Button")!;
        var menu = FormControlCatalog.Find("MenuStrip")!;
        var timer = FormControlCatalog.Find("Timer")!;

        Assert.Multiple(() =>
        {
            Assert.That(button.WinFormsEvent, Is.EqualTo("Click"));
            Assert.That(button.WebEvent, Is.EqualTo("click"));
            Assert.That(button.WinFormsEventArgs, Is.Null, "null means EventArgs");
            Assert.That(menu.WinFormsEventArgs, Is.EqualTo("ToolStripItemClickedEventArgs"));
            Assert.That(timer.DefaultEvent(FormTarget.Web), Is.EqualTo("tick"));
        });
    }

    [Test]
    public void WiredOn_TheWeb_IsExactlyTheEventsWithAWebName()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FormEvents.WiredOn(FormControlCatalog.Find("Button")!, FormTarget.Web)
                    .Select(e => FormEvents.NameOn(e, FormTarget.Web)),
                Is.EqualTo(new[] { "click" }));
            Assert.That(FormEvents.WiredOn(FormControlCatalog.Find("CheckedListBox")!, FormTarget.Web), Is.Empty,
                "a kind with no web row wires nothing on the web");
        });
    }

    [Test]
    public void WiredOn_WinForms_IsEveryCatalogEvent()
    {
        var button = FormControlCatalog.Find("Button")!;

        Assert.That(FormEvents.WiredOn(button, FormTarget.WinForms).Select(e => e.Name),
            Is.EqualTo(button.Events!.Select(e => e.Name)));
    }

    /// <summary>
    /// ⚠ RE-CHECK IN SLICE 5: the Form gains Load/Shown/… then, and this becomes non-empty.
    /// </summary>
    [Test]
    public void WiredOn_TheFormRoot_IsEmpty_UntilFormEventsExist()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FormEvents.WiredOn(FormControlCatalog.FormRoot, FormTarget.WinForms), Is.Empty);
            Assert.That(FormEvents.WiredOn(FormControlCatalog.FormRoot, FormTarget.Web), Is.Empty);
        });
    }

    /// <summary>
    /// Owner decision O3 (2026-09-29, re-checked in Visual Studio): double-clicking a GroupBox creates an ENTER
    /// handler, as VS does (the snapshot's DefaultEvent agrees); on the page Enter is the fieldset's
    /// <c>focusin</c> — focus moving into the box, which bubbles from its children exactly as WinForms' Enter is
    /// raised for them. Click stays wired as a NON-default event (its existing binds keep working).
    /// </summary>
    [Test]
    public void AGroupBox_DefaultsToEnter_FocusinOnThePage_AndKeepsClick()
    {
        var group = FormControlCatalog.Find("GroupBox")!;

        Assert.Multiple(() =>
        {
            Assert.That(group.DefaultEvent(FormTarget.WinForms), Is.EqualTo("Enter"));
            Assert.That(group.DefaultEvent(FormTarget.Web), Is.EqualTo("focusin"));
            Assert.That(group.WinFormsEventArgs, Is.Null, "Enter is an EventHandler: e As EventArgs");
            Assert.That(FormEvents.WiredOn(group, FormTarget.WinForms).Select(e => e.Name),
                Is.EquivalentTo(new[] { "Enter", "Click" }));
            Assert.That(FormEvents.WiredOn(group, FormTarget.Web).Select(e => FormEvents.NameOn(e, FormTarget.Web)),
                Is.EquivalentTo(new[] { "focusin", "click" }));
            Assert.That(group.Events!.Single(e => e.Name == "Enter").OracleExemption, Is.Null,
                "the snapshot agrees with Enter — nothing to exempt");
        });
    }

    /// <summary>
    /// Owner decision (2026-09-29): default events MATCH WinForms — a Panel, FlowLayoutPanel and TableLayoutPanel open
    /// on Paint (with its PaintEventArgs), and each keeps Click as a non-default event.
    /// </summary>
    [TestCase("Panel")]
    [TestCase("FlowLayoutPanel")]
    [TestCase("TableLayoutPanel")]
    public void APanelKind_DefaultsToPaint_AndKeepsClick(string kind)
    {
        var def = FormControlCatalog.Find(kind)!;

        Assert.Multiple(() =>
        {
            Assert.That(def.DefaultEvent(FormTarget.WinForms), Is.EqualTo("Paint"));
            Assert.That(def.WinFormsEventArgs, Is.EqualTo("PaintEventArgs"));
            Assert.That(def.DefaultEventDef!.WebEvent, Is.Null, "a page has no Paint");
            Assert.That(FormEvents.WiredOn(def, FormTarget.WinForms).Select(e => e.Name), Is.EquivalentTo(new[] { "Paint", "Click" }));
        });
    }

    /// <summary>
    /// ⛔ A web Panel's double-click opens Click — declared on the row (<see cref="FormEventDef.IsWebDefault"/>), never
    /// guessed as "the first event with a web name" — and a Paint bind is never offered on the web.
    /// </summary>
    [Test]
    public void AWebPanel_OpensItsDeclaredWebDefault_Click()
    {
        var panel = FormControlCatalog.Find("Panel")!;

        Assert.Multiple(() =>
        {
            Assert.That(panel.DefaultEvent(FormTarget.Web), Is.EqualTo("click"));
            Assert.That(panel.DefaultEventDefOn(FormTarget.Web)!.Name, Is.EqualTo("Click"));
            Assert.That(FormEvents.WiredOn(panel, FormTarget.Web).Select(e => e.Name), Is.EquivalentTo(new[] { "Click" }));
        });
    }

    /// <summary>
    /// A web default is a FALLBACK for a default with no web name: at most one per row, it must have a web name, and a
    /// row whose default already has one declares none (it would never be read, and would read as if it were).
    /// </summary>
    [Test]
    public void AWebDefault_IsDeclaredOnlyWhereTheDefaultHasNoWebName_AtMostOnce()
    {
        var offenders = FormControlCatalog.All.Where(d => d.Events != null && d.Events.Any(e => e.IsWebDefault))
            .Where(d => d.Events!.Count(e => e.IsWebDefault) > 1 ||
                        d.Events!.Single(e => e.IsWebDefault).WebEvent == null ||
                        d.DefaultEventDef?.WebEvent != null ||
                        d.Events!.Single(e => e.IsWebDefault).IsDefault)
            .Select(d => d.Kind);

        Assert.That(offenders, Is.Empty);
    }

    /// <summary>
    /// Owner decision (2026-09-29): TrackBar opens on Scroll, as VS does. On the page Scroll is the range input's
    /// <c>input</c> (it fires per step of a drag, as WinForms' Scroll does) and ValueChanged becomes <c>change</c> —
    /// two events cannot share one DOM name, because a web bind is stored BY that name.
    /// </summary>
    [Test]
    public void ATrackBar_DefaultsToScroll_Input_AndValueChangedIsChange()
    {
        var bar = FormControlCatalog.Find("TrackBar")!;

        Assert.Multiple(() =>
        {
            Assert.That(bar.DefaultEvent(FormTarget.WinForms), Is.EqualTo("Scroll"));
            Assert.That(bar.DefaultEvent(FormTarget.Web), Is.EqualTo("input"));
            Assert.That(bar.WinFormsEventArgs, Is.Null, "TrackBar.Scroll is an EventHandler");
            Assert.That(bar.Events!.Single(e => e.Name == "ValueChanged").WebEvent, Is.EqualTo("change"));
            Assert.That(bar.Events!.Select(e => e.WebEvent).Where(w => w != null), Is.Unique);
        });
    }

    [Test]
    public void ADataGridView_DefaultsToCellContentClick_AndKeepsCellClick()
    {
        var grid = FormControlCatalog.Find("DataGridView")!;

        Assert.Multiple(() =>
        {
            Assert.That(grid.DefaultEvent(FormTarget.WinForms), Is.EqualTo("CellContentClick"));
            Assert.That(grid.WinFormsEventArgs, Is.EqualTo("DataGridViewCellEventArgs"));
            Assert.That(grid.Events!.Select(e => e.Name), Is.EquivalentTo(new[] { "CellContentClick", "CellClick" }));
        });
    }

    /// <summary>No two events of one row may share a web name: a web bind is stored BY that name, so it would be ambiguous.</summary>
    [Test]
    public void NoRow_HasTwoEventsWithOneWebName()
    {
        var offenders = FormControlCatalog.All.Where(d => d.Events != null)
            .Where(d => d.Events!.Where(e => e.WebEvent != null).GroupBy(e => e.WebEvent, StringComparer.OrdinalIgnoreCase)
                .Any(g => g.Count() > 1))
            .Select(d => d.Kind);

        Assert.That(offenders, Is.Empty);
    }

    [Test]
    public void TheRegionWriter_AcceptsTheCatalogsWebEvent_ThroughTheSeam()
    {
        var form = new FormDocument { Target = FormTarget.Web, Name = "F" };
        var button = new FormControl { Kind = "Button", Id = "btn", Geometry = new GridGeometry() };
        button.Binds.Add(new FormBind { Event = "click", Handler = "btn_Click" });
        form.Controls.Add(button);

        var result = RegionWriter.Write("F.bas", FormScaffolder.Create("F", FormTarget.Web).CodeText, form, "F.blwebform");

        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics.Select(d => d.Code), Has.No.Member(DesignCodes.UnknownWebEvent),
                "a bind on the row's own web event must never be refused as BL8032");
            // ⚠ Without these an unrelated early refusal would pass the line above by emitting nothing.
            Assert.That(result.Refused, Is.False, string.Join("; ", result.Diagnostics.Select(d => d.Format())));
            Assert.That(result.Diagnostics.Where(d => !d.IsWarning), Is.Empty);
            Assert.That(result.Text, Does.Contain("""btn.addEventListener("click", AddressOf btn_Click)"""),
                "the accepted bind must actually be wired");
        });
    }
}
