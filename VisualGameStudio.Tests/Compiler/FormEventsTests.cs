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
    ///
    /// <para>⛔ PLACEHOLDER until plan Task 6 Step 9(f). <c>FormControlCatalog.FormRoot</c> does not exist
    /// yet, so the real body cannot compile even under <c>[Ignore]</c>. It is kept verbatim in the
    /// comment below; the active body FAILS, so deleting the Ignore without restoring the body goes red
    /// rather than passing by absence.</para>
    /// </summary>
    [Test]
    [Ignore("FormRoot lands in Task 6")]
    public void WiredOn_TheFormRoot_IsEmpty_UntilFormEventsExist()
    {
        // Task 6 Step 9(f): replace this Assert.Fail with —
        // Assert.Multiple(() =>
        // {
        //     Assert.That(FormEvents.WiredOn(FormControlCatalog.FormRoot, FormTarget.WinForms), Is.Empty);
        //     Assert.That(FormEvents.WiredOn(FormControlCatalog.FormRoot, FormTarget.Web), Is.Empty);
        // });
        Assert.Fail("restore the FormRoot body (plan Task 6 Step 9(f)) before removing the Ignore");
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
