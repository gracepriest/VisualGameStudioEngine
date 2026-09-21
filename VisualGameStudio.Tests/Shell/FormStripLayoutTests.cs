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
    // Items do not exist on the canvas yet — Task 20 (24d) replaces this test
    // ==================================================================

    /// <summary>
    /// ⚠ REPLACED in commit 24d (Task 20): once <c>Cells()</c> lands, a host's top-level items become
    /// <see cref="FormLayoutRole.Cell"/> entries and this assertion flips to expect them. For 24c the
    /// model already nests an item under its host's <c>Children</c> (Tasks 12-16 gave the reader,
    /// writer and clipboard the <c>Place</c> branch), but nothing walks it onto the canvas yet.
    /// </summary>
    [Test]
    public void Layout_DoesNotYieldItemsYet()
    {
        var menuStrip = Strip("MenuStrip", "menuStrip1");
        var item = new FormControl { Kind = "ToolStripMenuItem", Id = "mnuFile" };
        item.Properties["Text"] = "&File";
        menuStrip.Children.Add(item);
        var document = WinFormsDocument(menuStrip);

        var entries = FormCanvasTransform.Layout(document).ToList();

        Assert.That(entries.Select(e => e.Control?.Id), Does.Not.Contain("mnuFile"));
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
