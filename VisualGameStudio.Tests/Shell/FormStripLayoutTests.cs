using System.Linq;
using Avalonia;
using BasicLang.Forms;
using NUnit.Framework;
using VisualGameStudio.Shell.Controls;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Task 17 (commit 24c), Step 1: <see cref="FormCanvasTransform.Layout(FormDocument)"/> learns to
/// yield a Docked strip (MenuStrip/ToolStrip/StatusStrip) as a BAND across the surface, before any
/// cell or Type Here slot exists — those are Task 20's job in commit 24d. Pure transform tests: no
/// rendering, no Avalonia application. <c>FormControl</c>, <c>FormDocument</c>, <c>Rect</c> and
/// <c>Point</c> are plain data, exactly like <c>FormCanvasTransformTests</c>.
///
/// <para>⚠ This file compiles against THREE symbols Task 17 has not created yet:
/// <c>VisualGameStudio.Shell.Controls.FormLayoutRole</c>, <c>FormLayoutEntry</c>, and the fact that
/// <c>Layout(FormDocument)</c> must return <c>IEnumerable&lt;FormLayoutEntry&gt;</c> rather than
/// today's <c>IEnumerable&lt;(FormControl Control, Rect Bounds)&gt;</c> tuple (which has no
/// <c>.Role</c>). Until they land, the WHOLE ASSEMBLY fails to build — see the compile-error report
/// handed back with this file, which is the actual red for this step.</para>
/// </summary>
[TestFixture]
public class FormStripLayoutTests
{
    private const int SurfaceWidth = 640;
    private const int SurfaceHeight = 480;

    private static FormDocument WinFormsDocument(params FormControl[] controls)
    {
        var document = new FormDocument
        {
            Target = FormTarget.WinForms, Name = "StripForm", Width = SurfaceWidth, Height = SurfaceHeight
        };
        document.Controls.AddRange(controls);
        return document;
    }

    private static FormControl Strip(string kind, string id, string? dock = null)
    {
        var control = new FormControl { Kind = kind, Id = id };
        if (dock != null)
        {
            control.Properties["Dock"] = dock;
        }

        return control;
    }

    private static FormControl PositionedButton(string id, int x, int y, int w, int h) => new()
    {
        Kind = "Button", Id = id, Geometry = new PixelGeometry { X = x, Y = y, Width = w, Height = h }
    };

    /// <summary>An item under a host — Task 20's cells/dropdowns fixture shape.</summary>
    private static FormControl Item(string kind, string id, string? text = null, params FormControl[] children)
    {
        var control = new FormControl { Kind = kind, Id = id };
        if (text != null)
        {
            control.Properties["Text"] = text;
        }

        control.Children.AddRange(children);
        return control;
    }

    /// <summary>
    /// The schematic width rule (Task 20, spec §3/§4): 8px padding either side of 7px/char, with
    /// <c>&amp;</c> counted as a written character (never stripped as an accelerator marker).
    /// </summary>
    private static double CellWidth(string text) => 8 + 7 * text.Length + 8;

    /// <summary>
    /// The spec §3 document, verbatim: a MenuStrip (mnuFile → mnuOpen, sep1, mnuExit), a ToolStrip
    /// (tsbOpen), a positioned Button, and a StatusStrip (lblStatus) — the exact fixture shape Task
    /// 20's contract (commit 24d) is written against.
    /// </summary>
    private static (FormDocument Document, FormControl MenuStrip, FormControl ToolStrip, FormControl StatusStrip,
        FormControl MnuFile, FormControl MnuOpen, FormControl Sep1, FormControl MnuExit,
        FormControl TsbOpen, FormControl LblStatus) BuildSpec3Document()
    {
        var mnuOpen = Item("ToolStripMenuItem", "mnuOpen", "&Open...");
        var sep1 = Item("ToolStripSeparator", "sep1");
        var mnuExit = Item("ToolStripMenuItem", "mnuExit", "E&xit");
        var mnuFile = Item("ToolStripMenuItem", "mnuFile", "&File", mnuOpen, sep1, mnuExit);
        var menuStrip = Strip("MenuStrip", "menuStrip1");
        menuStrip.Children.Add(mnuFile);

        var tsbOpen = Item("ToolStripButton", "tsbOpen", "Open");
        var toolStrip = Strip("ToolStrip", "toolStrip1");
        toolStrip.Children.Add(tsbOpen);

        var lblStatus = Item("ToolStripStatusLabel", "lblStatus", "Ready");
        var statusStrip = Strip("StatusStrip", "statusStrip1");
        statusStrip.Children.Add(lblStatus);

        var btnGo = PositionedButton("btnGo", 16, 80, 75, 23);

        var document = WinFormsDocument(menuStrip, toolStrip, btnGo, statusStrip);

        return (document, menuStrip, toolStrip, statusStrip, mnuFile, mnuOpen, sep1, mnuExit, tsbOpen, lblStatus);
    }

    /// <summary>The index of the entry that places <paramref name="control"/> with the given role — a
    /// unique key in every fixture below, since no control here appears twice under the same role.</summary>
    private static int IndexOfControl(List<FormLayoutEntry> entries, FormControl control, FormLayoutRole role) =>
        entries.FindIndex(e => ReferenceEquals(e.Control, control) && e.Role == role);

    /// <summary>The index of the Type Here slot hosted by <paramref name="host"/>.</summary>
    private static int IndexOfSlot(List<FormLayoutEntry> entries, FormControl host) =>
        entries.FindIndex(e => e.Role == FormLayoutRole.TypeHere && ReferenceEquals(e.Host, host));

    // ==================================================================
    // Bands
    // ==================================================================

    [Test]
    public void Layout_YieldsATopStripAsABandAcrossTheSurface()
    {
        // Nothing here sets the Dock property, so this also pins that a MISSING Dock attribute reads
        // the catalog's default ("Top" for MenuStrip) rather than landing at (0,0,0,0) or vanishing.
        var document = WinFormsDocument(Strip("MenuStrip", "menuStrip1"));

        var entry = FormCanvasTransform.Layout(document).Single(e => e.Control?.Id == "menuStrip1");

        Assert.Multiple(() =>
        {
            Assert.That(entry.Bounds, Is.EqualTo(new Rect(0, 0, SurfaceWidth, 24)));
            Assert.That(entry.Role, Is.EqualTo(FormLayoutRole.Band));
        });
    }

    [Test]
    public void Layout_StacksTopStrips_InDocumentOrder_AndBottomStripsFromTheEdge()
    {
        // Document order: two Top strips (MenuStrip 24px, ToolStrip 25px) stack DOWNWARD from y=0;
        // two Bottom strips (StatusStrip 22px, then a second strip forced Bottom) stack UPWARD from
        // the surface edge, first-in-document ON the edge — a second bottom bar is included so the
        // test actually discriminates the stacking DIRECTION rather than passing on a single strip
        // that happens to sit at the edge either way.
        var menuStrip = Strip("MenuStrip", "menuStrip1");
        var toolStrip = Strip("ToolStrip", "toolStrip1");
        var statusStrip = Strip("StatusStrip", "statusStrip1");
        var secondBottom = Strip("ToolStrip", "toolStrip2", dock: "Bottom");
        var document = WinFormsDocument(menuStrip, toolStrip, statusStrip, secondBottom);

        var bands = FormCanvasTransform.Layout(document)
            .Where(e => e.Role == FormLayoutRole.Band)
            .ToDictionary(e => e.Control!.Id, e => e.Bounds);

        Assert.Multiple(() =>
        {
            Assert.That(bands["menuStrip1"].Y, Is.EqualTo(0));
            Assert.That(bands["toolStrip1"].Y, Is.EqualTo(24), "stacks below the 24px MenuStrip");
            Assert.That(bands["statusStrip1"].Y, Is.EqualTo(SurfaceHeight - 22),
                "the FIRST Bottom strip in document order sits on the edge");
            Assert.That(bands["toolStrip2"].Y, Is.EqualTo(SurfaceHeight - 22 - 25),
                "the SECOND Bottom strip stacks ABOVE the first, not below it and not below the top strips");
        });
    }

    [Test]
    public void Bands_AStatusStripWithNoDockAttribute_StillFallsBackToTheRowsBottomDefault()
    {
        // The Strip() helper leaves Properties["Dock"] entirely UNSET when dock is null — the
        // document-value arm of FormControl.DockEdge must fall through to the catalog row's own
        // default ("Bottom" for StatusStrip), not silently land at "Top" (y = 0).
        var document = WinFormsDocument(Strip("StatusStrip", "statusStrip1"));

        var band = FormCanvasTransform.Layout(document).Single(e => e.Control?.Id == "statusStrip1");

        Assert.That(band.Bounds.Y, Is.EqualTo(SurfaceHeight - 22),
            "a StatusStrip with no Dock property must still dock to its row's own Bottom default");
    }

    [Test]
    public void Bands_AStatusStripWithAnEmptyDockProperty_StillDocksToBottom()
    {
        // Dock="" is legal XML and round-trips verbatim (D9 Degraded tier); it must be treated as
        // "no value" and fall back to the row default, not merely as "not Bottom" => Top.
        var document = WinFormsDocument(Strip("StatusStrip", "statusStrip1", dock: ""));

        var band = FormCanvasTransform.Layout(document).Single(e => e.Control?.Id == "statusStrip1");

        Assert.That(band.Bounds.Y, Is.EqualTo(SurfaceHeight - 22),
            "Dock=\"\" must still fall back to the row's Bottom default, not read as merely 'not Bottom'");
    }

    [Test]
    public void Layout_NeverYieldsAZeroRect_ForAStrip()
    {
        // Recon rank 2 (spec §2): a strip carries no PixelGeometry, so a naive reuse of BoundsOf (or
        // a Bands() that forgets the row's DefaultHeight) collapses it to a 0x0 rect at the origin —
        // a phantom resize grip nobody dropped, with nothing else on screen looking wrong.
        var document = WinFormsDocument(
            Strip("MenuStrip", "menuStrip1"), Strip("ToolStrip", "toolStrip1"),
            Strip("StatusStrip", "statusStrip1"));

        var bands = FormCanvasTransform.Layout(document).Where(e => e.Role == FormLayoutRole.Band).ToList();

        Assert.That(bands, Has.Count.EqualTo(3));
        Assert.Multiple(() =>
        {
            foreach (var band in bands)
            {
                Assert.That(band.Bounds.Width, Is.GreaterThan(0), $"{band.Control!.Id} has zero width");
                Assert.That(band.Bounds.Height, Is.GreaterThan(0), $"{band.Control!.Id} has zero height");
            }
        });
    }

    [Test]
    public void Layout_YieldsBandsOnAWebPage_RegardlessOfLayoutKind()
    {
        // A Flow page lays out NOTHING today for a positioned/cell control
        // (FormCanvasTransformTests.AFlowPage_LaysOutNothing) — there is no cell a point could mean.
        // A strip is page CHROME, not a cell, so it must appear even when the layout kind yields no
        // cells at all; if WebLayout only appended Bands() inside the Grid branch, this fails.
        var document = new FormDocument
        {
            Target = FormTarget.Web, Name = "Page", Width = SurfaceWidth, Height = SurfaceHeight,
            Layout = new FormLayout { Kind = FormLayoutKind.Flow }
        };
        document.Controls.Add(Strip("MenuStrip", "menuStrip1"));

        var entries = FormCanvasTransform.Layout(document).Where(e => e.Control?.Id == "menuStrip1").ToList();

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].Role, Is.EqualTo(FormLayoutRole.Band));
    }

    // ==================================================================
    // HitTest / ContainerAt / ControlsIn must agree the band is not a droppable, selectable control
    // ==================================================================

    [Test]
    public void HitTest_ReturnsTheStrip_OnTheBand_OnBothTargets()
    {
        var winForms = WinFormsDocument(Strip("MenuStrip", "menuStrip1"));
        var web = new FormDocument
        {
            Target = FormTarget.Web, Name = "Page", Width = SurfaceWidth, Height = SurfaceHeight,
            Layout = new FormLayout { Kind = FormLayoutKind.Grid, Cols = "1fr", Rows = "1fr" }
        };
        web.Controls.Add(Strip("MenuStrip", "menuStrip1"));
        var transform = new FormCanvasTransform();
        var pointInBand = new Point(10, 10); // inside Rect(0,0,640,24) on either target

        Assert.Multiple(() =>
        {
            Assert.That(transform.HitTest(winForms, pointInBand)?.Id, Is.EqualTo("menuStrip1"),
                "WinForms HitTest must read the same Layout the render pass paints from");
            Assert.That(transform.HitTest(web, pointInBand)?.Id, Is.EqualTo("menuStrip1"),
                "the web branch already reads Layout; this pins the WinForms branch catching up to it");
        });
    }

    [Test]
    public void HitTest_ABand_OutHitTestsAPositionedControlItOverlaps()
    {
        // ⛔ HitTest's `.LastOrDefault()` makes the ORDER Layout(FormDocument) concatenates entries
        // in the actual rule: bands must come LAST (topmost) so they out-hit-test anything they
        // overlap. `HitTest_ReturnsTheStrip_OnTheBand_OnBothTargets` above never proves this — its
        // band is the ONLY entry at that point, so LastOrDefault would return it regardless of
        // concatenation order. This fixture overlaps a positioned Button with the band so the two
        // orders disagree, which is the only way to prove the ordering is load-bearing.
        var button = PositionedButton("btnUnderBar", 0, 0, 100, 24);
        var menuStrip = Strip("MenuStrip", "menuStrip1"); // band = Rect(0, 0, 640, 24)
        var document = WinFormsDocument(button, menuStrip);

        var hit = new FormCanvasTransform().HitTest(document, new Point(10, 10));

        Assert.That(hit?.Id, Is.EqualTo("menuStrip1"),
            "document order is z-order: the band must out-hit-test the positioned control it overlaps");
    }

    [Test]
    public void ContainerAt_NeverReturnsAStrip()
    {
        // A drop point over the band must land on the FORM (null container), never inside the strip —
        // a MenuStrip is not a container a control can be dropped into.
        var document = WinFormsDocument(Strip("MenuStrip", "menuStrip1"));

        var container = FormCanvasTransform.ContainerAt(document, new Point(10, 10));

        Assert.That(container, Is.Null);
    }

    [Test]
    public void ContainerAt_TheExplicitDockedSkip_LetsAContainerBehindTheStripStillBeFound()
    {
        // ⛔ A strip never carries PixelGeometry through the real reader, so BoundsOf(strip, ...)
        // already returns null for it — the explicit `Definition?.Place == FormPlace.Docked` skip
        // in ContainerAt is otherwise unreachable through any document a real .blform ever
        // produces, and deleting it would fail nothing under the test above. Falsifying it needs a
        // strip that DOES have real bounds — hand-built, bypassing the reader, the same technique
        // already used elsewhere in this feature to pin an otherwise-unreachable arm.
        //
        // Without the explicit skip, a hit on the (non-container) strip's bounds would `return null`
        // immediately (MenuStrip.IsContainer == false) — stopping the walk cold — instead of
        // `continue`ing past the strip to the Panel underneath it. Document order makes the strip
        // topmost (checked first).
        var panel = new FormControl
        {
            Kind = "Panel", Id = "pnl",
            Geometry = new PixelGeometry { X = 0, Y = 0, Width = 200, Height = 200 }
        };
        var strip = new FormControl
        {
            Kind = "MenuStrip", Id = "menuStrip1",
            Geometry = new PixelGeometry { X = 0, Y = 0, Width = 200, Height = 24 }
        };
        var document = WinFormsDocument(panel, strip);

        var container = FormCanvasTransform.ContainerAt(document, new Point(10, 10));

        Assert.That(container?.Container.Id, Is.EqualTo("pnl"),
            "the Docked skip must let the walk pass THROUGH a (hypothetically bounded) strip to " +
            "whatever container sits behind it, not stop the walk at a non-container return");
    }

    [Test]
    public void ControlsIn_NeverReturnsAStripOrAnItem()
    {
        // A rubber-band drag covering the whole surface intersects the MenuStrip's band (and, once
        // Task 20 lands cells, its items too) — VS band-selects positioned controls only, never a bar
        // or a menu item (spec §2's ControlsIn row). The item is nested under the strip today (Tasks
        // 12-16 already let the model hold one) even though nothing walks it onto the canvas until
        // Task 20 — this proves the exclusion is by PLACE, not merely "Layout never yields it yet".
        var menuStrip = Strip("MenuStrip", "menuStrip1");
        var item = new FormControl { Kind = "ToolStripMenuItem", Id = "mnuFile" };
        item.Properties["Text"] = "&File";
        menuStrip.Children.Add(item);
        var document = WinFormsDocument(menuStrip, PositionedButton("btnGo", 16, 80, 75, 23));

        var covered = FormCanvasTransform.ControlsIn(document, new Rect(0, 0, SurfaceWidth, SurfaceHeight));

        Assert.Multiple(() =>
        {
            Assert.That(covered.Select(c => c.Id), Does.Not.Contain("menuStrip1"));
            Assert.That(covered.Select(c => c.Id), Does.Not.Contain("mnuFile"),
                "items are not laid out until Task 20, but the day they are the marquee must still skip them");
            Assert.That(covered.Select(c => c.Id), Does.Contain("btnGo"));
        });
    }

    // ==================================================================
    // Task 20 (commit 24d) — cells, dropdowns and Type Here slots in Layout
    //
    // ⚠ REPLACES Layout_DoesNotYieldItemsYet (24c): that test's own doc comment said it would be
    // replaced the day a host's top-level items became FormLayoutRole.Cell entries — this is that day.
    // ==================================================================

    [Test]
    public void Cells_AToolStripsTopLevelItem_UsesTheBandsYAndHeight_NotTheItemsOwnDefaultHeight()
    {
        // ⛔⛔ Blocker 5 (2026-09-21 pre-flight): NONE of the four Item rows declares DefaultHeight
        // (FormControlCatalog.cs :948/:962/:969/:981), so all four inherit the record default 24 —
        // IDENTICAL to MenuStrip's own band height (24, :909). An implementation that lays a cell at
        // `item.Definition!.DefaultHeight` instead of reading the BAND's own height, or that lays
        // every band's cells at y=0, would still pass a MenuStrip-only assertion. ToolStrip's band
        // height is 25 (:927) — it can never accidentally agree with the inherited 24, so only the
        // band's OWN Y and Height, taken off the Band entry, can make this pass.
        var (document, _, _, _, _, _, _, _, tsbOpen, _) = BuildSpec3Document();

        var cell = FormCanvasTransform.Layout(document).Single(e => ReferenceEquals(e.Control, tsbOpen));

        Assert.Multiple(() =>
        {
            Assert.That(cell.Role, Is.EqualTo(FormLayoutRole.Cell));
            // ToolStrip is the second Top band, stacked below the 24px MenuStrip: Y = 24, not 0.
            Assert.That(cell.Bounds, Is.EqualTo(new Rect(0, 24, CellWidth("Open"), 25)),
                "must use the ToolStrip band's own Y (24) and Height (25) — never the item's inherited " +
                "DefaultHeight (24) and never y=0");
        });
    }

    [Test]
    public void Cells_AStatusStripsTopLevelItem_UsesTheBandsYAndHeight_NotTheItemsOwnDefaultHeight()
    {
        // The second discriminating fixture Blocker 5 requires: StatusStrip's band height is 22
        // (never 24), and it sits at the BOTTOM of the surface (Y = 480 - 22 = 458), not at y=0.
        var (document, _, _, _, _, _, _, _, _, lblStatus) = BuildSpec3Document();

        var cell = FormCanvasTransform.Layout(document).Single(e => ReferenceEquals(e.Control, lblStatus));

        Assert.Multiple(() =>
        {
            Assert.That(cell.Role, Is.EqualTo(FormLayoutRole.Cell));
            Assert.That(cell.Bounds, Is.EqualTo(new Rect(0, SurfaceHeight - 22, CellWidth("Ready"), 22)),
                "must use the StatusStrip band's own Y (458) and Height (22) — never the item's " +
                "inherited DefaultHeight (24) and never y=0");
        });
    }

    [Test]
    public void Layout_TheMenuStripItselfSelected_YieldsItsCellAndOneSlot_ButOpensNoDropdown()
    {
        var (document, menuStrip, _, _, mnuFile, mnuOpen, sep1, mnuExit, _, _) = BuildSpec3Document();

        var entries = FormCanvasTransform.Layout(document, menuStrip).ToList();
        var mnuFileCell = entries.Single(e => ReferenceEquals(e.Control, mnuFile));
        var slots = entries.Where(e => e.Role == FormLayoutRole.TypeHere).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(mnuFileCell.Role, Is.EqualTo(FormLayoutRole.Cell));
            Assert.That(mnuFileCell.Bounds, Is.EqualTo(new Rect(0, 0, CellWidth("&File"), 24)),
                "'&' counts as a written character in the schematic width rule");

            Assert.That(slots, Has.Count.EqualTo(1), "the strip is the only active host — exactly one slot");
            Assert.That(slots[0].Control, Is.Null);
            Assert.That(slots[0].Host, Is.EqualTo(menuStrip));
            Assert.That(slots[0].Bounds,
                Is.EqualTo(new Rect(mnuFileCell.Bounds.Right, 0, CellWidth("Type Here"), 24)),
                "the slot sits at the last cell's Right, with the band's own Y and Height");

            Assert.That(entries.Any(e => ReferenceEquals(e.Control, mnuOpen)), Is.False,
                "selecting the STRIP itself must not open mnuFile's dropdown");
            Assert.That(entries.Any(e => ReferenceEquals(e.Control, sep1)), Is.False);
            Assert.That(entries.Any(e => ReferenceEquals(e.Control, mnuExit)), Is.False);
        });
    }

    [Test]
    public void Layout_WithNoSelection_YieldsNoTypeHereEntryAnywhere()
    {
        var (document, _, _, _, _, _, _, _, _, _) = BuildSpec3Document();

        var entries = FormCanvasTransform.Layout(document, selected: null).ToList();

        Assert.That(entries.Where(e => e.Role == FormLayoutRole.TypeHere), Is.Empty);
    }

    [Test]
    public void Layout_WithAStripSelected_YieldsExactlyOneSlot_AtTheLastCellsRight_HostedByThatStrip()
    {
        var (document, _, toolStrip, _, _, _, _, _, tsbOpen, _) = BuildSpec3Document();

        var entries = FormCanvasTransform.Layout(document, toolStrip).ToList();
        var slots = entries.Where(e => e.Role == FormLayoutRole.TypeHere).ToList();
        var tsbOpenCell = entries.Single(e => ReferenceEquals(e.Control, tsbOpen));

        Assert.Multiple(() =>
        {
            Assert.That(slots, Has.Count.EqualTo(1));
            Assert.That(slots[0].Host, Is.EqualTo(toolStrip));
            Assert.That(slots[0].Control, Is.Null);
            Assert.That(slots[0].Bounds,
                Is.EqualTo(new Rect(tsbOpenCell.Bounds.Right, 24, CellWidth("Type Here"), 25)));
        });
    }

    [Test]
    public void Layout_SelectingANestedItem_ExpandsItsAncestorDropdowns_OutermostFirst_AfterAllBandAndCellEntries()
    {
        // mnuOpen has no children of its own, but it IS a host BY ROW (every ToolStripMenuItem lists
        // Items, regardless of whether it currently has any) — its own dropdown is empty. Per the
        // Host doc comment on FormLayoutEntry, two KINDS of slot can be visible together: the ACTIVE
        // STRIP's own band-level slot (menuStrip1 is mnuOpen's root strip via the parent map, so it
        // counts as active even though mnuOpen — not the strip — is what is selected) and each
        // expanded ancestor's own dropdown slot (mnuFile's, then mnuOpen's) — three slots total here.
        var (document, menuStrip, _, _, mnuFile, mnuOpen, sep1, mnuExit, _, _) = BuildSpec3Document();

        var entries = FormCanvasTransform.Layout(document, mnuOpen).ToList();

        var menuStripBandIndex = IndexOfControl(entries, menuStrip, FormLayoutRole.Band);
        var mnuFileCellIndex = IndexOfControl(entries, mnuFile, FormLayoutRole.Cell);
        var menuStripSlotIndex = IndexOfSlot(entries, menuStrip);
        var mnuOpenRowIndex = IndexOfControl(entries, mnuOpen, FormLayoutRole.Cell);
        var sep1RowIndex = IndexOfControl(entries, sep1, FormLayoutRole.Cell);
        var mnuExitRowIndex = IndexOfControl(entries, mnuExit, FormLayoutRole.Cell);
        var mnuFileSlotIndex = IndexOfSlot(entries, mnuFile);
        var mnuOpenSlotIndex = IndexOfSlot(entries, mnuOpen);

        Assert.Multiple(() =>
        {
            // --- existence ---
            Assert.That(menuStripBandIndex, Is.GreaterThanOrEqualTo(0), "the MenuStrip band must still be present");
            Assert.That(mnuFileCellIndex, Is.GreaterThanOrEqualTo(0), "mnuFile's own top-level cell must still be present");
            Assert.That(menuStripSlotIndex, Is.GreaterThanOrEqualTo(0),
                "menuStrip1 is still the ACTIVE strip (mnuOpen's root) and keeps its own band-level slot");
            Assert.That(mnuOpenRowIndex, Is.GreaterThanOrEqualTo(0), "mnuFile's dropdown row for mnuOpen");
            Assert.That(sep1RowIndex, Is.GreaterThanOrEqualTo(0), "mnuFile's dropdown row for sep1");
            Assert.That(mnuExitRowIndex, Is.GreaterThanOrEqualTo(0), "mnuFile's dropdown row for mnuExit");
            Assert.That(mnuFileSlotIndex, Is.GreaterThanOrEqualTo(0), "mnuFile's own dropdown slot");
            Assert.That(mnuOpenSlotIndex, Is.GreaterThanOrEqualTo(0), "mnuOpen's own (empty) dropdown slot");

            // --- ordering: "the dropdown entries come LAST ... after every band/cell entry" ---
            Assert.That(menuStripBandIndex, Is.LessThan(mnuOpenRowIndex));
            Assert.That(mnuFileCellIndex, Is.LessThan(mnuOpenRowIndex));
            Assert.That(menuStripSlotIndex, Is.LessThan(mnuOpenRowIndex),
                "the active strip's OWN band-level slot is not one of 'the dropdown entries' and must still precede them");

            // --- ordering within the tail: outermost ancestor first, each host's rows before its slot ---
            Assert.That(mnuOpenRowIndex, Is.LessThan(sep1RowIndex));
            Assert.That(sep1RowIndex, Is.LessThan(mnuExitRowIndex));
            Assert.That(mnuExitRowIndex, Is.LessThan(mnuFileSlotIndex));
            Assert.That(mnuFileSlotIndex, Is.LessThan(mnuOpenSlotIndex));

            // --- geometry: mnuFile's dropdown hangs from (cell.X, cell.Bottom) = (0, 24); its width is
            // the widest child cell (mnuOpen's, 72), floored at the 80px minimum ---
            Assert.That(entries[mnuOpenRowIndex].Bounds, Is.EqualTo(new Rect(0, 24, 80, 22)));
            Assert.That(entries[sep1RowIndex].Bounds, Is.EqualTo(new Rect(0, 46, 80, 6)), "a separator row is 6px");
            Assert.That(entries[mnuExitRowIndex].Bounds, Is.EqualTo(new Rect(0, 52, 80, 22)));
            Assert.That(entries[mnuFileSlotIndex].Control, Is.Null);
            Assert.That(entries[mnuFileSlotIndex].Host, Is.EqualTo(mnuFile));
            Assert.That(entries[mnuFileSlotIndex].Bounds, Is.EqualTo(new Rect(0, 74, 80, 22)),
                "below the last row (24+22+6+22=74), same width as the dropdown");

            // --- geometry: mnuOpen's own dropdown hangs from (cell.Right, cell.Y) = (80, 24); it has
            // no children, so it takes the 80px minimum width, and with no rows its slot sits right at
            // the dropdown's own origin ---
            Assert.That(entries[mnuOpenSlotIndex].Control, Is.Null);
            Assert.That(entries[mnuOpenSlotIndex].Host, Is.EqualTo(mnuOpen));
            Assert.That(entries[mnuOpenSlotIndex].Bounds, Is.EqualTo(new Rect(80, 24, 80, 22)));

            // --- the menuStrip1 band-level slot itself, unaffected by the deeper selection ---
            Assert.That(entries[menuStripSlotIndex].Host, Is.EqualTo(menuStrip));
            Assert.That(entries[menuStripSlotIndex].Bounds,
                Is.EqualTo(new Rect(entries[mnuFileCellIndex].Bounds.Right, 0, CellWidth("Type Here"), 24)));
        });
    }

    [Test]
    public void TypeHereAt_ReturnsTheHostForAPointInTheSlot_AndNullElsewhere()
    {
        var (document, menuStrip, _, _, _, _, _, _, _, _) = BuildSpec3Document();

        // ⚠ An INSTANCE method taking CANVAS points, exactly like its twin HitTest — the two are
        // called back-to-back on one point in OnPointerPressed, and a sibling that took form space
        // would make a dropped ToForm invisible at the identity zoom every test here runs at. The
        // default transform IS identity, so the canvas points below are also the form points the
        // rects are written in.
        var transform = new FormCanvasTransform();

        // mnuFile's cell is Rect(0,0,51,24); its slot (menuStrip1 active) is Rect(51,0,79,24).
        var pointInSlot = new Point(60, 10);
        var pointInMnuFilesCell = new Point(10, 10);
        var pointFarOutside = new Point(700, 700);

        Assert.Multiple(() =>
        {
            Assert.That(transform.TypeHereAt(document, pointInSlot, menuStrip), Is.EqualTo(menuStrip));
            Assert.That(transform.TypeHereAt(document, pointInMnuFilesCell, menuStrip), Is.Null,
                "a point in the CELL, not the slot, must not resolve to a host");
            Assert.That(transform.TypeHereAt(document, pointFarOutside, menuStrip), Is.Null);
            Assert.That(transform.TypeHereAt(document, pointInSlot, selected: null), Is.Null,
                "with nothing selected there is no slot for any point to land in");
        });
    }

    [Test]
    public void HitTest_OnADropdownCell_ReturnsTheNestedItem()
    {
        var (document, _, _, _, _, mnuOpen, _, mnuExit, _, _) = BuildSpec3Document();

        // mnuExit's dropdown row is Rect(0,52,80,22) once selecting mnuOpen expands mnuFile's
        // dropdown — a point well inside it, away from any edge.
        var pointInMnuExitsRow = new Point(10, 60);

        var hit = new FormCanvasTransform().HitTest(document, pointInMnuExitsRow, mnuOpen);

        Assert.That(hit, Is.EqualTo(mnuExit));
    }

    [Test]
    public void ControlsIn_StillExcludesEveryStripAndItem_OnceTheirCellsExist()
    {
        // ⚠ ControlsIn takes no `selected`, so it only ever sees Layout(document, selected: null) —
        // the band-level cells (mnuFile, tsbOpen, lblStatus) exist unconditionally under that call and
        // are this test's real discriminating coverage; mnuOpen/sep1/mnuExit are not laid out at all
        // without a selection, so their absence here is trivial today — kept as documentation for the
        // day ControlsIn learns to forward a selection, exactly as ControlsIn_NeverReturnsAStripOrAnItem
        // (24c) already documents for the pre-Task-20 shape.
        var (document, menuStrip, toolStrip, statusStrip, mnuFile, mnuOpen, sep1, mnuExit, tsbOpen, lblStatus) =
            BuildSpec3Document();

        var covered = FormCanvasTransform.ControlsIn(document, new Rect(0, 0, SurfaceWidth, SurfaceHeight));
        var ids = covered.Select(c => c.Id).ToList();

        Assert.Multiple(() =>
        {
            foreach (var excluded in new[]
                     { menuStrip, toolStrip, statusStrip, mnuFile, mnuOpen, sep1, mnuExit, tsbOpen, lblStatus })
            {
                Assert.That(ids, Does.Not.Contain(excluded.Id),
                    $"{excluded.Id} is a strip or an item — never band-selectable");
            }

            Assert.That(ids, Does.Contain("btnGo"));
        });
    }

    // ==================================================================
    // The overflow edge (spec §2's Layout row)
    // ==================================================================

    /// <summary>
    /// ⚠ Pin of the behaviour spec §2 DESCRIBES for the new walk, not an independent requirement:
    /// "Unifying the WinForms <c>HitTest</c> onto <c>Layout</c> changes one edge — the recursive walk
    /// descended into a child only INSIDE its container, while <c>Layout</c> yields child rects
    /// unclipped, so a child overflowing its Panel becomes hittable where it is PAINTED". Today's
    /// recursive <c>HitTest</c> tests the PARENT's own bounds before ever looking at its children, so
    /// a point outside the Panel never reaches an overflowing child inside it — this test's point is
    /// exactly such a point. If the landed implementation disagrees with the spec text, the expected
    /// id below is what should change, not the reasoning in this comment.
    /// </summary>
    [Test]
    public void HitTest_FindsAChildWhereItIsPainted_EvenOutsideItsPanel()
    {
        var panel = new FormControl
        {
            Kind = "Panel", Id = "pnl", Geometry = new PixelGeometry { X = 10, Y = 10, Width = 50, Height = 50 }
        };
        var overflowing = new FormControl
        {
            Kind = "Button", Id = "spillover", Geometry = new PixelGeometry { X = 40, Y = 40, Width = 50, Height = 50 }
        };
        panel.Children.Add(overflowing);
        var document = WinFormsDocument(panel);

        // Absolute: panel occupies (10,10)-(60,60); the child occupies (50,50)-(100,100) — this point
        // is inside the CHILD and outside the PANEL that hosts it.
        var pointOutsidePanelInsideChild = new Point(80, 80);

        Assert.That(new FormCanvasTransform().HitTest(document, pointOutsidePanelInsideChild)?.Id,
            Is.EqualTo("spillover"));
    }
}
