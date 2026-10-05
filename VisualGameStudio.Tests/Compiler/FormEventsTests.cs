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

    /// <summary>
    /// Slice 5: the Button lists many events, so "exactly click" became "exactly the events with a web name" — derived
    /// from the row, plus the point the old pin made (click is there) and the one D-2 makes (a WinForms-only event —
    /// Paint, TextChanged — never is).
    /// </summary>
    [Test]
    public void WiredOn_TheWeb_IsExactlyTheEventsWithAWebName()
    {
        var button = FormControlCatalog.Find("Button")!;
        var web = FormEvents.WiredOn(button, FormTarget.Web).Select(e => FormEvents.NameOn(e, FormTarget.Web)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(web, Is.EqualTo(button.Events!.Where(e => e.WebEvent != null).Select(e => e.WebEvent)));
            Assert.That(web, Does.Contain("click"));
            Assert.That(FormEvents.WiredOn(button, FormTarget.Web).Select(e => e.Name),
                Has.No.Member("Paint").And.No.Member("TextChanged"), "a web form never shows a WinForms-only event");
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
    /// Slice 5 D-3: the Form's ten events, default Load (WinForms' own <c>[DefaultEvent]</c>); on the page only the six
    /// with an honest DOM source — Load as a call at the end of init, Resize on the window, the rest on the body.
    /// </summary>
    [Test]
    public void WiredOn_TheFormRoot_IsItsTenEvents_OnWinForms_AndSix_OnTheWeb()
    {
        var root = FormControlCatalog.FormRoot;
        var web = FormEvents.WiredOn(root, FormTarget.Web);

        Assert.Multiple(() =>
        {
            Assert.That(FormEvents.WiredOn(root, FormTarget.WinForms).Select(e => e.Name), Is.EqualTo(new[]
            {
                "Load", "Shown", "Activated", "FormClosing", "FormClosed", "Resize", "Click", "KeyDown", "KeyUp", "KeyPress"
            }));
            Assert.That(web.Select(e => e.Name), Is.EqualTo(new[] { "Load", "Resize", "Click", "KeyDown", "KeyUp", "KeyPress" }));
            Assert.That(root.DefaultEvent(FormTarget.WinForms), Is.EqualTo("Load"));
            Assert.That(root.DefaultEvent(FormTarget.Web), Is.EqualTo("load"));
            Assert.That(web.ToDictionary(e => e.Name, e => e.WebWiring), Is.EquivalentTo(new Dictionary<string, FormWebWiring>
            {
                ["Load"] = FormWebWiring.AfterInit, ["Resize"] = FormWebWiring.Window, ["Click"] = FormWebWiring.Element,
                ["KeyDown"] = FormWebWiring.Element, ["KeyUp"] = FormWebWiring.Element, ["KeyPress"] = FormWebWiring.Element
            }));
            Assert.That(FormEvents.ListenType(web.Single(e => e.Name == "KeyPress")), Is.EqualTo("keydown"));
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
                Does.Contain("Enter").And.Contain("Click"));
            Assert.That(FormEvents.WiredOn(group, FormTarget.Web).Select(e => FormEvents.NameOn(e, FormTarget.Web)),
                Does.Contain("focusin").And.Contain("click"));
            Assert.That(group.Events!.Single(e => e.Name == "Enter").OracleExemption, Is.Null,
                "the snapshot agrees with Enter — nothing to exempt");
            Assert.That(group.Events!.Single(e => e.Name == "Click").OracleExemption, Is.Not.Null,
                "GroupBox.Click is [Browsable(false)] — kept, exempt, so existing binds keep working");
        });
    }

    /// <summary>Every row whose WinForms default is Paint — catalog-driven, never a hand list beside the catalog.</summary>
    private static IEnumerable<TestCaseData> PaintDefaultKinds() =>
        FormControlCatalog.All.Where(d => d.DefaultEventDef?.Name == "Paint")
            .Select(d => new TestCaseData(d.Kind).SetName("{m}(" + d.Kind + ")"));

    [Test]
    public void PaintDefaultKinds_IsNotEmpty() =>
        Assert.That(PaintDefaultKinds().Count(), Is.GreaterThanOrEqualTo(3),
            "Panel, FlowLayoutPanel and TableLayoutPanel default to Paint — a source yielding nothing passes by absence");

    /// <summary>
    /// Owner decision (2026-09-29): default events MATCH WinForms — a panel kind opens on Paint (with its
    /// PaintEventArgs), keeps Click as a non-default event, and never shows Paint on the page.
    /// </summary>
    [TestCaseSource(nameof(PaintDefaultKinds))]
    public void APanelKind_DefaultsToPaint_AndKeepsClick(string kind)
    {
        var def = FormControlCatalog.Find(kind)!;

        Assert.Multiple(() =>
        {
            Assert.That(def.DefaultEvent(FormTarget.WinForms), Is.EqualTo("Paint"));
            Assert.That(def.WinFormsEventArgs, Is.EqualTo("PaintEventArgs"));
            Assert.That(def.DefaultEventDef!.WebEvent, Is.Null, "a page has no Paint");
            Assert.That(FormEvents.WiredOn(def, FormTarget.WinForms).Select(e => e.Name), Does.Contain("Paint").And.Contain("Click"));
            Assert.That(FormEvents.WiredOn(def, FormTarget.Web).Select(e => e.Name), Has.No.Member("Paint"));
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
            Assert.That(FormEvents.WiredOn(panel, FormTarget.Web).Select(e => e.Name),
                Does.Contain("Click").And.No.Member("Paint"));
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
            Assert.That(grid.Events!.Select(e => e.Name), Does.Contain("CellContentClick").And.Contain("CellClick"));
        });
    }

    /// <summary>No two events of one row may share a web name: a web bind is stored BY that name, so it would be ambiguous.</summary>
    [Test]
    public void NoRow_HasTwoEventsWithOneWebName()
    {
        var offenders = FormControlCatalog.All.Append(FormControlCatalog.FormRoot).Where(d => d.Events != null)
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

    // ==================================================================
    // Slice 5 — the web wiring invariants (ADR 0021), each over every row AND the Form.
    // ==================================================================

    private static IEnumerable<(FormControlDef Definition, FormEventDef Event)> EveryEvent(bool includeRoot = true) =>
        (includeRoot ? FormControlCatalog.All.Append(FormControlCatalog.FormRoot) : FormControlCatalog.All)
        .SelectMany(d => (d.Events ?? Array.Empty<FormEventDef>()).Select(e => (d, e)));

    private static string Where((FormControlDef Definition, FormEventDef Event) x) => $"{x.Definition.Kind}.{x.Event.Name}";

    [Test]
    public void WindowAndAfterInitWiring_AreOnTheFormRootOnly()
    {
        var offenders = EveryEvent(includeRoot: false).Where(x => x.Event.WebWiring != FormWebWiring.Element).Select(Where);

        Assert.That(offenders, Is.Empty,
            "a control's events listen on its own element — window and end-of-init are the Form's (D-3)");
    }

    [Test]
    public void AtMostOneAfterInitEvent_PerRow_AndItIsTheDefault()
    {
        var offenders = FormControlCatalog.All.Append(FormControlCatalog.FormRoot)
            .Where(d => d.Events != null)
            .Where(d => d.Events!.Count(e => e.WebWiring == FormWebWiring.AfterInit) > 1 ||
                        d.Events!.Any(e => e.WebWiring == FormWebWiring.AfterInit && !e.IsDefault))
            .Select(d => d.Kind);

        Assert.That(offenders, Is.Empty, "one call at the end of InitializeComponent, and it is what a double-click opens");
    }

    [Test]
    public void KeyPressKeys_IsExactlyOnTheEventsStoredKeypress_AndListensAsKeydown()
    {
        var offenders = EveryEvent()
            .Where(x => (x.Event.WebFilter == FormWebFilter.KeyPressKeys) != (x.Event.WebEvent == "keypress") ||
                        (x.Event.WebEvent == "keypress" && FormEvents.ListenType(x.Event) != "keydown"))
            .Select(Where);

        Assert.Multiple(() =>
        {
            Assert.That(offenders, Is.Empty,
                "a stored keypress is emitted as the filtered keydown (coordinator ruling 1) — the filter is what says so");
            Assert.That(EveryEvent().Count(x => x.Event.WebEvent == "keypress"), Is.GreaterThan(0),
                "a sweep over no KeyPress event proves nothing");
        });
    }

    [Test]
    public void FromOutside_IsExactlyOnFocusinAndFocusout()
    {
        var offenders = EveryEvent()
            .Where(x => (x.Event.WebFilter == FormWebFilter.FromOutside) != (x.Event.WebEvent is "focusin" or "focusout"))
            .Select(Where);

        Assert.That(offenders, Is.Empty,
            "Enter/Leave mean focus from OUTSIDE the element (coordinator ruling 2) — on every focusin/focusout, nowhere else");
    }

    [Test]
    public void AWebFilter_IsOnlyOnAnEventWithAWebName()
    {
        var offenders = EveryEvent().Where(x => x.Event.WebFilter != FormWebFilter.None && x.Event.WebEvent == null)
            .Select(Where);

        Assert.That(offenders, Is.Empty, "a WinForms-only event has no page listener to filter");
    }

    [Test]
    public void ListenType_IsTheStoredName_ExceptForKeyPress()
    {
        Assert.Multiple(() =>
        {
            foreach (var x in EveryEvent().Where(x => x.Event.WebEvent != null && x.Event.WebFilter != FormWebFilter.KeyPressKeys))
            {
                Assert.That(FormEvents.ListenType(x.Event), Is.EqualTo(x.Event.WebEvent), Where(x));
            }

            Assert.That(FormEvents.ListenType(new FormEventDef("Paint")), Is.Null, "no web name, nothing to listen to");
        });
    }

    /// <summary>
    /// D-8 item 3: the DOM-interface table is the ONE answer the run tiers and piece 2's coverage gate read — every web
    /// event a row stores, except a Timer's <c>tick</c> (a <c>setInterval</c> callback, never a dispatched event).
    /// </summary>
    [Test]
    public void EveryWebEventButTick_IsInTheDomInterfaceTable_AndTickIsNot()
    {
        var missing = EveryEvent().Where(x => x.Event.WebEvent is { } w && w != "tick" && FormEvents.DomInterfaceOf(w) == null)
            .Select(Where);

        Assert.Multiple(() =>
        {
            Assert.That(missing, Is.Empty);
            Assert.That(FormEvents.DomInterfaceOf("tick"), Is.Null);
            Assert.That(FormEvents.DomInterfaces.Keys, Has.No.Member("tick"));
            Assert.That(FormEvents.DomInterfaceOf("keypress"), Is.EqualTo("KeyboardEvent"),
                "a KeyPress is dispatched as a KeyboardEvent keydown");
        });
    }

    /// <summary>⛔ Gates only: the emitter answers "what is listened to" through ListenType, never through this table.</summary>
    [Test]
    public void TheDomInterfaceTable_IsNeverReadByTheRegionWriter()
    {
        var path = FindRepoFile("BasicLang", "Forms", "RegionWriter.cs");
        Assert.That(path, Is.Not.Null, "RegionWriter.cs not found above the test binaries");

        Assert.That(File.ReadAllText(path!), Does.Not.Contain("DomInterface"));
    }

    /// <summary>
    /// The WinForms scaffold imports neither <c>System.ComponentModel</c> nor anything else (CLAUDE.md, M4): an args type
    /// whose namespace is not <c>System</c>/<c>System.Windows.Forms</c> must be written fully qualified, or the stub the
    /// designer writes does not compile. Read from the oracle's <c>argsFullName</c>.
    /// </summary>
    [Test]
    public void EveryArgsTypeOutsideSystemAndWinForms_IsWrittenQualified()
    {
        var snapshot = WinFormsMetadata.Load();
        var offenders = new List<string>();
        var judged = 0;

        foreach (var x in EveryEvent().Where(x => x.Definition.SupportsTarget(FormTarget.WinForms)))
        {
            var full = snapshot.Type(x.Definition.Kind)?.Event(x.Event.Name)?.ArgsFullName;
            if (full == null)
            {
                continue; // an exempt, non-browsable event — parity names a missing one
            }

            var ns = full.Contains('.') ? full[..full.LastIndexOf('.')] : "";
            if (ns is "System" or "System.Windows.Forms")
            {
                continue;
            }

            judged++;
            if (!string.Equals(x.Event.WinFormsArgs, full, StringComparison.Ordinal))
            {
                offenders.Add($"{Where(x)}: {x.Event.WinFormsArgs ?? "EventArgs"} → {full}");
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(offenders, Is.Empty);
            Assert.That(judged, Is.GreaterThan(0), "Validating/DoWork/LoadCompleted must be judged — or this proves nothing");
        });
    }

    private static string? FindRepoFile(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
