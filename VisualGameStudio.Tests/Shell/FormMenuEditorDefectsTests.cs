using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using BasicLang.Forms;
using NUnit.Framework;
using VisualGameStudio.Shell.Controls;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Form-designer menu-editor defects (test-writer pass), reported by the owner from the REAL IDE:
/// "It's clipping the entries — if I type &amp;Open only &amp;Ope shows.", "the textbox popup is
/// often not inline with where the text will go.", and the accelerator underline (covered instead by
/// <c>FormAcceleratorTests</c> in <c>VisualGameStudio.Tests.Compiler</c>, the natural home for the
/// shared <c>FormAccelerator</c> seam both the emitter and the canvas must agree on).
///
/// <para>⛔ Every test here exercises MULTIPLE ZOOM VALUES AWAY FROM 1.0 on purpose — the existing
/// suite's canvas tests all run at zoom 1.0 (or a Fit that lands near it), which is exactly why none
/// of them caught either defect: at zoom 1.0 the fixed-12px caption most nearly matches its
/// form-unit-sized cell, and no existing test compares the Type-Here slot's text origin against the
/// committed item's. A test only at zoom 1.0 would reproduce the exact blindness that let both ship.
/// </para>
///
/// <para>⛔⛔ THE DECISION (not open for re-debate): captions SCALE WITH THE ZOOM. A caption is drawn
/// at <see cref="FormCanvasTransform.CaptionFontSize"/> × zoom with its inset × zoom; the cell is laid
/// out in FORM units and scaled by the SAME zoom — so a caption that fits at 1:1 fits at every zoom.
/// The real IDE only ever fits at zoom ≤ 1.0 (<c>FormCanvasControl.Fit</c> never zooms past 1), so the
/// zooms below that matter are ≤ 1; a row above 1 is kept only as a guard. The clipping tests below use
/// <see cref="FormCanvasControl.MeasureCaptionForTest(string, double)"/>'s zoom overload, which
/// measures at the CORRECT (decided-design) scaled font size — it does NOT itself prove
/// <c>DrawSchematic</c>/<c>DrawTypeHereSlot</c> apply that scaling yet (they still draw at a FIXED
/// size as of this pass), so a clipping test passing here is a DESIGN-CONSISTENCY check (the
/// schematic's nominal per-character width leaves enough real headroom once both sides are scaled
/// together), not proof the live render path has been updated — see that overload's own doc comment.
/// The overlay-alignment tests, by contrast, DO exercise the live <see cref="FormTypeHereEditor"/> and
/// so DO fail against today's fixed inset/font size.</para>
/// </summary>
[TestFixture]
public class FormMenuEditorDefectsTests
{
    // ==================================================================
    // Shared fixture — one MenuStrip, three real items with real accelerators
    // ==================================================================

    private sealed class Fixture
    {
        public required FormDocument Doc { get; init; }
        public required FormControl MenuStrip { get; init; }
        public required FormControl MnuOpen { get; init; }
        public required FormControl MnuExit { get; init; }
        public required FormControl MnuPrefs { get; init; }
    }

    /// <summary>
    /// ⛔ Every fixture control shares NO text collision risk, but per the CLAUDE.md canvas-testing
    /// trap ("give every fixture control the SAME id — the canvas labels a control Text ?? Id"),
    /// every control here carries an explicit, DISTINCT <c>Text</c> so the label drawn is always the
    /// caption under test and never a fallback to Id.
    /// </summary>
    private static Fixture BuildFixture()
    {
        var mnuOpen = new FormControl { Kind = "ToolStripMenuItem", Id = "i1" };
        mnuOpen.Properties["Text"] = "&Open";
        var mnuExit = new FormControl { Kind = "ToolStripMenuItem", Id = "i1" };
        mnuExit.Properties["Text"] = "E&xit";
        var mnuPrefs = new FormControl { Kind = "ToolStripMenuItem", Id = "i1" };
        mnuPrefs.Properties["Text"] = "&Preferences...";

        var menuStrip = new FormControl { Kind = "MenuStrip", Id = "menuStrip1" };
        menuStrip.Children.Add(mnuOpen);
        menuStrip.Children.Add(mnuExit);
        menuStrip.Children.Add(mnuPrefs);

        var doc = new FormDocument
        {
            Target = FormTarget.WinForms, Name = "MenuForm", Width = 640, Height = 480
        };
        doc.Controls.Add(menuStrip);

        return new Fixture { Doc = doc, MenuStrip = menuStrip, MnuOpen = mnuOpen, MnuExit = mnuExit, MnuPrefs = mnuPrefs };
    }

    /// <summary>
    /// An extra strip fixture: one item of the given kind under one strip control, for the per-kind
    /// overlay-alignment tests. Shares <see cref="Fixture"/>'s naming ("i1" for the item — same id
    /// collision guard, distinct <c>Text</c> so the label drawn is always the caption under test).
    /// </summary>
    // Public, not private: NUnit's TestCaseSource plumbing passes this type through a public test
    // method parameter (OverlayEditorTextOrigin_MatchesTheCommittedCaptionsLeftInset_ForOtherStripKinds
    // below), and a less-accessible nested type there is CS0051 (less accessible than the method).
    public sealed class StripFixture
    {
        public required FormDocument Doc { get; init; }
        public required FormControl Strip { get; init; }

        /// <summary>
        /// The one already-committed item on <see cref="Strip"/>, of the same kind the Type Here
        /// slot after it would create — the ground truth the overlay test below measures its inset
        /// against via the LIVE render, rather than against <c>RenderSchematicForTest</c>'s
        /// always-zoom-1 probe.
        /// </summary>
        public required FormControl Item { get; init; }
    }

    private static StripFixture BuildToolStripFixture()
    {
        var tsbCut = new FormControl { Kind = "ToolStripButton", Id = "i1" };
        tsbCut.Properties["Text"] = "Cut";

        var toolStrip = new FormControl { Kind = "ToolStrip", Id = "toolStrip1" };
        toolStrip.Children.Add(tsbCut);

        var doc = new FormDocument
        {
            Target = FormTarget.WinForms, Name = "MenuForm", Width = 640, Height = 480
        };
        doc.Controls.Add(toolStrip);

        return new StripFixture { Doc = doc, Strip = toolStrip, Item = tsbCut };
    }

    private static StripFixture BuildStatusStripFixture()
    {
        var lblReady = new FormControl { Kind = "ToolStripStatusLabel", Id = "i1" };
        lblReady.Properties["Text"] = "Ready";

        var statusStrip = new FormControl { Kind = "StatusStrip", Id = "statusStrip1" };
        statusStrip.Children.Add(lblReady);

        var doc = new FormDocument
        {
            Target = FormTarget.WinForms, Name = "MenuForm", Width = 640, Height = 480
        };
        doc.Controls.Add(statusStrip);

        return new StripFixture { Doc = doc, Strip = statusStrip, Item = lblReady };
    }

    // ==================================================================
    // B — no clipping at any zoom
    // ==================================================================

    /// <summary>
    /// ⛔⛔ THE OWNER'S FIRST REPORT: "if I type &amp;Open only &amp;Ope shows." A cell's width is
    /// computed in FORM units from the caption's CHARACTER COUNT
    /// (<c>FormCanvasTransform.CellWidth</c>: 8 + 7×len + 8) and then scaled to canvas pixels by
    /// zoom. Per THE DECISION, the caption itself is now measured at
    /// <see cref="FormCanvasTransform.CaptionFontSize"/> × zoom too
    /// (<see cref="FormCanvasControl.MeasureCaptionForTest(string, double)"/>) and the inset scales
    /// the same way — so a caption that fits at 1:1 must keep fitting at every zoom ≤ 1, which is the
    /// only range the real IDE's Fit ever produces. 1.5 is kept only as a guard above that range.
    /// </summary>
    [TestCase(0.35)]
    [TestCase(0.5)]
    [TestCase(0.75)]
    [TestCase(1.0)]
    [TestCase(1.5)]
    [AvaloniaTest]
    public void CommittedItemCaptions_FitInsideTheirCell_AtEveryZoom(double zoom)
    {
        var fx = BuildFixture();
        var transform = new FormCanvasTransform(zoom);
        var entries = FormCanvasTransform.Layout(fx.Doc, selected: null)
            .Where(e => e.Role == FormLayoutRole.Cell && e.Control != null)
            .ToList();

        Assert.That(entries, Is.Not.Empty, "the fixture must lay out at least one item cell");

        var failures = new List<string>();
        foreach (var entry in entries)
        {
            var caption = FormCanvasControl.CaptionForTest(entry.Control!);
            var measuredWidth = FormCanvasControl.MeasureCaptionForTest(caption, zoom).Width;
            var canvasRect = transform.ToCanvas(entry.Bounds);
            var available = canvasRect.Width - (FormCanvasTransform.MenuItemCaptionInset * zoom);

            if (measuredWidth > available)
            {
                failures.Add($"\"{caption}\" needs {measuredWidth:F1}px but only {available:F1}px is " +
                             $"available in its cell (cell canvas width {canvasRect.Width:F1}px) at zoom {zoom}");
            }
        }

        Assert.That(failures, Is.Empty,
            "caption(s) clip at zoom " + zoom + ":\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// The Type-Here slot's own caption ("Type Here") is sized by the SAME rule
    /// (<c>CellWidth(TypeHereCaption)</c>) and, once the design decision lands, drawn at the same
    /// zoom-scaled size — so it must fit at every zoom ≤ 1 for the same reason a committed item's
    /// caption does. Selecting the strip makes it the ACTIVE strip
    /// (<c>FormCanvasTransform.ActiveStrip</c>), which is what puts a Type-Here entry into
    /// <c>Layout</c> at all. ⛔ The inset is <see cref="FormCanvasTransform.MenuItemCaptionInset"/>
    /// itself — <c>DrawTypeHereSlot</c> draws its placeholder at that SAME inset, never a private
    /// "slot inset" copy that could disagree with it.
    /// </summary>
    [TestCase(0.35)]
    [TestCase(0.5)]
    [TestCase(0.75)]
    [TestCase(1.0)]
    [TestCase(1.5)]
    [AvaloniaTest]
    public void TypeHereSlotCaption_FitsInsideItsSlot_AtEveryZoom(double zoom)
    {
        var fx = BuildFixture();
        var transform = new FormCanvasTransform(zoom);
        var slots = FormCanvasTransform.Layout(fx.Doc, selected: fx.MenuStrip)
            .Where(e => e.Role == FormLayoutRole.TypeHere && ReferenceEquals(e.Host, fx.MenuStrip))
            .ToList();

        Assert.That(slots, Has.Count.EqualTo(1), "selecting the strip must produce exactly one Type-Here slot for it");
        var slot = slots[0];

        var measuredWidth = FormCanvasControl.MeasureCaptionForTest(FormCanvasTransform.TypeHereCaption, zoom).Width;
        var canvasRect = transform.ToCanvas(slot.Bounds);
        var available = canvasRect.Width - (FormCanvasTransform.MenuItemCaptionInset * zoom);

        Assert.That(measuredWidth, Is.LessThanOrEqualTo(available),
            $"the \"Type Here\" placeholder needs {measuredWidth:F1}px but only {available:F1}px is " +
            $"available in the slot (slot canvas width {canvasRect.Width:F1}px) at zoom {zoom}");
    }

    // ==================================================================
    // C — the overlay editor's text must align with where the committed caption lands
    // ==================================================================

    /// <summary>
    /// ⛔⛔ THE OWNER'S SECOND REPORT: "the textbox popup is often not inline with where the text
    /// will go." Three different code paths currently pick three different left insets for what is
    /// meant to be the SAME piece of text: <c>DrawTypeHereSlot</c> draws the placeholder at
    /// <c>bounds.X + 4</c>; the committed <c>MenuItem</c> arm draws its caption at
    /// <c>bounds.X + 8</c>; and <see cref="FormTypeHereEditor"/>'s overlay <c>TextBox</c> is
    /// positioned at the SLOT's own <c>bounds.X</c> with only a <c>Padding(2, 0)</c>, so its own text
    /// starts near <c>bounds.X + 2</c> plus whatever the template's internal presenter adds. None of
    /// the three agree, so what the user sees while typing is never where the word lands once
    /// committed.
    ///
    /// <para>Hosts the real <see cref="FormCanvasControl"/> and the real
    /// <see cref="FormTypeHereEditor"/> together, in the same Grid cell, exactly as
    /// <c>CodeEditorDocumentView.axaml</c> wires them (see <c>FormStripViewTests</c>) — and reads the
    /// overlay's actual <see cref="TextPresenter"/> position out of the real, laid-out visual tree,
    /// never a hand-computed expectation of where Avalonia will put it.</para>
    /// </summary>
    /// <para>⛔⛔ Per THE DECISION, both the expected inset AND the expected font size scale by the
    /// FITTED zoom the window size actually produces (<c>FormCanvasControl.Fit</c>, the same
    /// computation <c>Render</c> uses) — reported below rather than left implicit, so a failure names
    /// the zoom the window size produced. The three window widths below deliberately span the fitted
    /// range the real IDE ever produces: 300 → ~0.34, 640 → ~0.83, 1280 → 1.0 (Fit never exceeds 1).
    /// </para>
    [TestCase(300)]
    [TestCase(640)]
    [TestCase(1280)]
    [AvaloniaTest]
    public void OverlayEditorTextOrigin_MatchesTheCommittedCaptionsLeftInset_AtEveryWindowSize(double windowWidth)
    {
        var fx = BuildFixture();
        var windowHeight = Math.Max(200, windowWidth * 0.7);

        // SelectedControl makes the strip ACTIVE (so its slot appears in Layout at all); TypeHereHost
        // is the separate "currently being edited" flag Render reads to populate TypeHereBounds — see
        // FormCanvasControl.Render's IsEditedSlot local function. Both are needed.
        var canvas = new FormCanvasControl
        {
            Document = fx.Doc, SelectedControl = fx.MenuStrip, TypeHereHost = fx.MenuStrip
        };
        var editor = new FormTypeHereEditor();

        var grid = new Grid();
        grid.Children.Add(canvas);
        grid.Children.Add(editor); // added second: same Z-order as the real view (editor paints above)

        var window = new Window { Width = windowWidth, Height = windowHeight, Content = grid };
        window.Show();

        // Force a render so TypeHereBounds (written at the END of FormCanvasControl.Render) is
        // populated, and so canvas.Bounds is settled before Fit is recomputed from it below.
        using (window.CaptureRenderedFrame()) { }

        // The SAME Fit computation Render used for this exact viewport, so the expected numbers below
        // use the zoom that was ACTUALLY in effect, not a hand-guessed one.
        var zoom = FormCanvasControl.Fit(fx.Doc, canvas.Bounds.Size).Zoom;

        var slotBounds = canvas.TypeHereBounds;
        Assert.That(slotBounds, Is.Not.EqualTo(default(Rect)),
            "selecting the strip must populate TypeHereBounds with the active strip's slot");

        editor.SlotBounds = slotBounds;
        editor.Host = fx.MenuStrip;
        editor.IsActive = true;
        window.UpdateLayout();
        using (window.CaptureRenderedFrame()) { } // realise the TextBox template so its presenter exists

        var box = (TextBox)editor.Children[0];
        var presenter = box.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();
        Assert.That(presenter, Is.Not.Null, "the TextBox template must have realised its TextPresenter by now");

        var presenterOriginInCanvas = presenter!.TranslatePoint(new Point(0, 0), canvas);
        Assert.That(presenterOriginInCanvas, Is.Not.Null, "the presenter must be positioned inside the canvas's visual tree");

        var actualTextX = presenterOriginInCanvas!.Value.X;
        var expectedTextX = slotBounds.X + (FormCanvasTransform.MenuItemCaptionInset * zoom);

        // ⛔ A FLAT ±1px tolerance lets a wrong, UNSCALED inset (the pre-fix literal 2, instead of
        // MenuItemCaptionInset × zoom) pass at the smallest window: 8×0.34 ≈ 2.7, only 0.7px from the
        // mutant's ~2px, comfortably inside ±1px. The tolerance itself must scale with the inset being
        // checked — proportional to zoom, floored so it never vanishes at zoom ≈ 0 — or the gate goes
        // blind exactly where a scaling bug is most visible as a fraction of the inset.
        var tolerance = Math.Max(0.5, 0.25 * FormCanvasTransform.MenuItemCaptionInset * zoom);

        Assert.That(actualTextX, Is.EqualTo(expectedTextX).Within(tolerance),
            $"overlay text starts at canvas x={actualTextX:F1}, but the committed item's caption will " +
            $"draw at x={expectedTextX:F1} (slot.X {slotBounds.X:F1} + the MenuItem arm's " +
            $"{FormCanvasTransform.MenuItemCaptionInset}px inset scaled by the fitted zoom {zoom:F3}) " +
            $"— window {windowWidth}x{windowHeight}");

        var expectedFontSize = FormCanvasTransform.CaptionFontSize * zoom;
        Assert.That(box.FontSize, Is.EqualTo(expectedFontSize).Within(0.01),
            $"the overlay TextBox's FontSize must scale with the fitted zoom ({zoom:F3}): expected " +
            $"{expectedFontSize:F2}px, got {box.FontSize:F2}px — window {windowWidth}x{windowHeight}. " +
            "What the user sees while typing must match what appears once they commit at every zoom, " +
            "not only at 1:1.");
    }

    /// <summary>
    /// The zoom=1 base case: the overlay's <c>TextBox</c> at the fitted zoom the fixture's 640x480
    /// form and a 640x480-ish window happen to produce close to 1:1. The multi-window-size test above
    /// (<see cref="OverlayEditorTextOrigin_MatchesTheCommittedCaptionsLeftInset_AtEveryWindowSize"/>)
    /// is what actually proves the scaling ACROSS zooms; this one is kept as the simplest possible
    /// regression pin, constructed without a document or a canvas at all, for the single case where
    /// <c>CaptionFontSize × zoom == CaptionFontSize</c>.
    /// </summary>
    [AvaloniaTest]
    public void OverlayEditorFontSize_MatchesTheCommittedCaptionsFontSize_AtZoomOne()
    {
        var editor = new FormTypeHereEditor();
        var window = new Window { Width = 400, Height = 300, Content = editor };
        window.Show();
        editor.SlotBounds = new Rect(30, 40, 120, 22);
        editor.IsActive = true;
        window.UpdateLayout();

        var box = (TextBox)editor.Children[0];
        const double committedCaptionFontSizeAtZoomOne = FormCanvasTransform.CaptionFontSize;

        Assert.That(box.FontSize, Is.EqualTo(committedCaptionFontSizeAtZoomOne),
            "the overlay TextBox must render at the same font size the committed caption is drawn at " +
            "when the fitted zoom is 1:1, or what the user sees while typing does not match what " +
            "appears once they commit");
    }

    // ==================================================================
    // C.2 — the overlay must line up for EVERY host kind, not just menus
    // ==================================================================

    /// <summary>
    /// Cases for <see cref="OverlayEditorTextOrigin_MatchesTheCommittedCaptionsLeftInset_ForOtherStripKinds"/>:
    /// a fixture builder and the <see cref="FormSchematic"/> its one item draws as.
    /// </summary>
    private static IEnumerable<TestCaseData> StripOverlayCases()
    {
        yield return new TestCaseData((Func<StripFixture>)BuildToolStripFixture, FormSchematic.ToolButton)
            .SetName("OverlayEditorTextOrigin_MatchesTheCommittedCaptionsLeftInset_ForToolStrip");
        yield return new TestCaseData((Func<StripFixture>)BuildStatusStripFixture, FormSchematic.StatusLabel)
            .SetName("OverlayEditorTextOrigin_MatchesTheCommittedCaptionsLeftInset_ForStatusStrip");
    }

    /// <summary>
    /// ⛔⛔ NEW, currently uncovered — the implementer measured this but nothing pinned it. Committed
    /// captions draw at DIFFERENT insets per host kind: <c>FormSchematic.MenuItem</c> +8,
    /// <c>ToolButton</c> +6 (a 2px inset box plus a 4px caption offset), <c>StatusLabel</c> +4. A
    /// mutant overlay that hard-codes <see cref="FormCanvasTransform.MenuItemCaptionInset"/> for
    /// EVERY host would start its typed text 2px or 4px to the right of where the committed item
    /// will actually draw, and this must catch it.
    ///
    /// <para>⛔⛔ THE FIX (was the wrong test): the expected x must come from the LIVE render of
    /// THIS document, not <see cref="FormCanvasControl.RenderSchematicForTest"/>. That probe always
    /// renders at zoom 1 (a bare <c>new FormCanvasControl()</c>, whose <c>_transform</c> defaults to
    /// <c>new FormCanvasTransform()</c> — zoom 1 — and is never re-fitted, since the probe branch in
    /// <c>Render</c> returns before <c>Fit</c> runs), while the window here fits this 640×480 document
    /// into a 640×480 viewport at a zoom measurably below 1 (Fit's margin). The old expectation was
    /// therefore <c>slot.X + inset</c> (unscaled) while the live overlay draws at
    /// <c>slot.X + inset × zoom</c> — comparing an unscaled probe answer to a scaled live one, off by
    /// exactly the missing zoom factor. Measured: this row PASSED under the mutant "menu inset for
    /// every kind" (both answers were equally wrong-by-the-same-missing-zoom) and FAILED against the
    /// correct, zoom-scaling overlay.
    ///
    /// <para>The fix asks the LIVE render (<see cref="FormCanvasControl.RenderDocumentForTest"/>) for
    /// where the fixture's own already-committed item of this exact kind (<see cref="StripFixture.Item"/>)
    /// draws its caption, at the SAME viewport the overlay is being measured against — then compares
    /// the overlay's inset (relative to the slot's own left edge) to that item's inset (relative to
    /// its own cell's left edge). Both are shaped by the same zoom, from the same document, in the
    /// same render, so no zoom factor can go missing on either side.</para>
    /// </summary>
    [TestCaseSource(nameof(StripOverlayCases))]
    [AvaloniaTest]
    public void OverlayEditorTextOrigin_MatchesTheCommittedCaptionsLeftInset_ForOtherStripKinds(
        Func<StripFixture> buildFixture, FormSchematic itemSchematic)
    {
        var fx = buildFixture();

        var canvas = new FormCanvasControl
        {
            Document = fx.Doc, SelectedControl = fx.Strip, TypeHereHost = fx.Strip
        };
        var editor = new FormTypeHereEditor();

        var grid = new Grid();
        grid.Children.Add(canvas);
        grid.Children.Add(editor);

        var window = new Window { Width = 640, Height = 480, Content = grid };
        window.Show();
        using (window.CaptureRenderedFrame()) { }

        var slotBounds = canvas.TypeHereBounds;
        Assert.That(slotBounds, Is.Not.EqualTo(default(Rect)),
            "selecting the strip must populate TypeHereBounds with the active strip's slot");

        editor.SlotBounds = slotBounds;
        editor.Host = fx.Strip;
        editor.IsActive = true;
        window.UpdateLayout();
        using (window.CaptureRenderedFrame()) { }

        var box = (TextBox)editor.Children[0];
        var presenter = box.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();
        Assert.That(presenter, Is.Not.Null, "the TextBox template must have realised its TextPresenter by now");

        var presenterOriginInCanvas = presenter!.TranslatePoint(new Point(0, 0), canvas);
        Assert.That(presenterOriginInCanvas, Is.Not.Null, "the presenter must be positioned inside the canvas's visual tree");

        // The LIVE draw path's own answer, at the SAME document and viewport the overlay is sitting
        // on: the fixture's already-committed item of this exact kind, as DrawSchematic actually
        // shaped it — never RenderSchematicForTest's fixed zoom-1 probe, which cannot see the fitted
        // zoom this window produced.
        var frame = FormCanvasControl.RenderDocumentForTest(
            fx.Doc, selected: fx.Strip, typeHereHost: fx.Strip, canvas.Bounds.Size);
        var itemCaption = frame.Captions.SingleOrDefault(c => c.Control == fx.Item);
        Assert.That(itemCaption?.Drawn, Is.Not.Null,
            $"the fixture's committed {itemSchematic} item must draw a caption in the live render");

        var relativeInset = itemCaption!.Drawn!.Origin.X - itemCaption.Bounds.X;

        var actualTextX = presenterOriginInCanvas!.Value.X;
        var expectedTextX = slotBounds.X + relativeInset;

        Assert.That(actualTextX, Is.EqualTo(expectedTextX).Within(1.0),
            $"overlay text starts at canvas x={actualTextX:F1}, but a committed {itemSchematic} caption " +
            $"in this document draws {relativeInset:F2}px right of its own cell's left edge (expected " +
            $"x={expectedTextX:F1}) — the overlay must use THIS host kind's own inset, not the MenuItem " +
            "one hard-coded in FormTypeHereEditor's constructor");
    }

    // ==================================================================
    // C.3 — "Can't edit a menu item once it is entered" (owner's report, 2026-09-23): the
    // overlay must line up with the LIVE caption of the item being RENAMED, not just a newly
    // typed one. Contract: FormCanvasControl.EditingItem (new styled property, stubbed — not
    // yet consumed by Render), so TypeHereBounds stays default while editing and every case
    // below is measured red at its very first assertion.
    // ==================================================================

    private static IEnumerable<TestCaseData> EditItemAlignmentCases()
    {
        Func<(FormDocument Doc, FormControl Host, FormControl Item)> menuStrip = () =>
        {
            var fx = BuildFixture();
            return (fx.Doc, fx.MenuStrip, fx.MnuOpen);
        };
        Func<(FormDocument Doc, FormControl Host, FormControl Item)> toolStrip = () =>
        {
            var fx = BuildToolStripFixture();
            return (fx.Doc, fx.Strip, fx.Item);
        };

        yield return new TestCaseData(menuStrip, 1280.0)
            .SetName("OverlayAlignment_WhileEditingAnExistingItem_MenuStripItem_AtZoomNearOne");
        yield return new TestCaseData(menuStrip, 380.0)
            .SetName("OverlayAlignment_WhileEditingAnExistingItem_MenuStripItem_AtZoomNearHalf");
        yield return new TestCaseData(toolStrip, 1280.0)
            .SetName("OverlayAlignment_WhileEditingAnExistingItem_ToolStripButton_AtZoomNearOne");
        yield return new TestCaseData(toolStrip, 380.0)
            .SetName("OverlayAlignment_WhileEditingAnExistingItem_ToolStripButton_AtZoomNearHalf");
    }

    /// <summary>
    /// While RENAMING an existing item (F2 / second click — not the Type Here create flow), the
    /// overlay must sit exactly on that item's OWN cell, at the ORIGIN AND FONT SIZE its caption
    /// is actually drawn at right now — read from the LIVE render
    /// (<see cref="FormCanvasControl.RenderDocumentForTest"/>) of this exact document and
    /// viewport, never a hand-computed expectation. Fails today at the very first assertion:
    /// <c>EditingItem</c> is a property stub Render does not read yet, so
    /// <c>TypeHereBounds</c> never leaves <c>default(Rect)</c>.
    /// </summary>
    [TestCaseSource(nameof(EditItemAlignmentCases))]
    [AvaloniaTest]
    public void OverlayAlignment_WhileEditingAnExistingItem_MatchesItsLiveRenderedCaption(
        Func<(FormDocument Doc, FormControl Host, FormControl Item)> buildFixture, double windowWidth)
    {
        var (doc, host, item) = buildFixture();
        var windowHeight = Math.Max(200, windowWidth * 0.7);

        var canvas = new FormCanvasControl
        {
            Document = doc, SelectedControl = host, EditingItem = item
        };
        var editor = new FormTypeHereEditor();

        var grid = new Grid();
        grid.Children.Add(canvas);
        grid.Children.Add(editor);

        var window = new Window { Width = windowWidth, Height = windowHeight, Content = grid };
        window.Show();
        using (window.CaptureRenderedFrame()) { }

        var slotBounds = canvas.TypeHereBounds;
        Assert.That(slotBounds, Is.Not.EqualTo(default(Rect)),
            "editing an item must populate TypeHereBounds with THAT ITEM'S OWN cell — EditingItem " +
            "is a stub Render does not consume yet, so this is red today");

        // The item's OWN kind decides its inset (a ToolStripButton and a MenuItem differ), so the
        // overlay must be hosted by the ITEM being renamed — never its container, which is what
        // the create-flow's Host means.
        editor.SlotBounds = slotBounds;
        editor.Host = item;
        editor.IsActive = true;
        window.UpdateLayout();
        using (window.CaptureRenderedFrame()) { }

        var box = (TextBox)editor.Children[0];
        var presenter = box.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();
        Assert.That(presenter, Is.Not.Null, "the TextBox template must have realised its TextPresenter by now");

        var presenterOrigin = presenter!.TranslatePoint(new Point(0, 0), canvas);
        Assert.That(presenterOrigin, Is.Not.Null, "the presenter must be positioned inside the canvas's visual tree");

        var frame = FormCanvasControl.RenderDocumentForTest(doc, selected: host, typeHereHost: null, canvas.Bounds.Size);
        var itemCaption = frame.Captions.SingleOrDefault(c => c.Control == item);
        Assert.That(itemCaption?.Drawn, Is.Not.Null, "the item being edited must draw a caption in the live render");

        var expectedX = slotBounds.X + (itemCaption!.Drawn!.Origin.X - itemCaption.Bounds.X);
        Assert.That(presenterOrigin!.Value.X, Is.EqualTo(expectedX).Within(1.0),
            $"overlay text starts at x={presenterOrigin.Value.X:F1}, but the item's own live caption " +
            $"draws at x={expectedX:F1}");

        Assert.That(box.FontSize, Is.EqualTo(itemCaption.Drawn.FontSize).Within(0.01),
            $"overlay font size {box.FontSize:F2} must match the item's own live-rendered caption " +
            $"font size {itemCaption.Drawn.FontSize:F2}");
    }

    // ==================================================================
    // D — render-level: the accelerator mark itself must not be drawn
    // ==================================================================

    /// <summary>
    /// ⛔ THE OWNER'S THIRD REPORT (render-level half — the underline RULE itself is
    /// <c>FormAcceleratorTests</c>'s job): "&amp;" must never reach <c>DrawText</c> as a literal
    /// character. Pinned through the drawing seam (<see cref="FormCanvasControl.CaptionForTest"/>)
    /// rather than at the pixel level: a pixel-hash comparison of "&amp;Open" against "Open" would
    /// also change once an underline decoration is added post-fix (a DIFFERENT, expected pixel
    /// change), so hash equality/inequality cannot express "differs only by the underline, never by a
    /// literal ampersand character". The string actually handed to the text renderer is the
    /// unambiguous, render-level fact: it must never contain a bare "&amp;".
    /// </summary>
    /// <summary>
    /// ⚠ <c>[AvaloniaTest]</c> is required here even though nothing is rendered: any static member of
    /// <see cref="FormCanvasControl"/> triggers its static constructor, which builds
    /// <c>Avalonia.Input.Cursor</c> instances that need a platform (<c>ICursorFactory</c>) resolved —
    /// and once that constructor throws once in a test run, .NET caches the
    /// <c>TypeInitializationException</c> and rethrows it for every later touch in the SAME process,
    /// including from tests that ARE <c>[AvaloniaTest]</c>-attributed. Measured: this method was
    /// originally a plain <c>[Test]</c> and poisoned every other test in this fixture.
    /// </summary>
    [TestCase("&Open", "Open")]
    [TestCase("E&xit", "Exit")]
    [TestCase("&Preferences...", "Preferences...")]
    [AvaloniaTest]
    public void CaptionForTest_NeverPassesALiteralAccelerator_ToTheTextRenderer(string rawText, string expectedDisplay)
    {
        var control = new FormControl { Kind = "ToolStripMenuItem", Id = "i1" };
        control.Properties["Text"] = rawText;

        var caption = FormCanvasControl.CaptionForTest(control);

        Assert.That(caption, Is.EqualTo(expectedDisplay),
            $"DrawControl must hand DrawSchematic \"{expectedDisplay}\" for Text=\"{rawText}\", not the " +
            "raw string with its accelerator mark still in it — a literal \"&\" reaching DrawText is " +
            "exactly what the owner saw in the IDE");
    }
}
