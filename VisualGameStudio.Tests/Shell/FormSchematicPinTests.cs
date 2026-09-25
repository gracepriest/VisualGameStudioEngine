using Avalonia;
using Avalonia.Headless.NUnit;
using BasicLang.Forms;
using NUnit.Framework;
using VisualGameStudio.Shell.Controls;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Task 24, commit 24b: <see cref="FormCanvasControl.DrawControl"/> is about to route every kind's
/// drawing through ONE seam, <c>DrawSchematic</c>, so that a strip/item schematic can be pinned
/// exactly like every other kind BEFORE any catalog row uses one (commit 24c). These two tests are
/// enum-driven, not row-driven — they cover every <see cref="FormSchematic"/> value directly, so a
/// new schematic cannot land with a missing glyph or a shape that repaints an existing one.
///
/// <para>⛔ This fixture will not compile until the implementer lands <c>FormCanvasControl
/// .RenderSchematicForTest</c> and makes it and <c>FormToolboxViewModel.GlyphFor</c> PUBLIC static
/// seams — the Shell grants no <c>InternalsVisibleTo</c> to this test project (by convention; see
/// <c>CodeEditorDocumentView.axaml.cs:770</c>), so an internal <c>GlyphFor</c> is inaccessible here
/// and a missing <c>RenderSchematicForTest</c> does not exist to call. That is 24b's RED, not a
/// mistake in this file.</para>
///
/// <para>⚠ <c>[AvaloniaTest]</c> is required for the render test: without it the control constructs
/// on a worker thread and fails in ways that look like product bugs. Skia is required too —
/// <c>UseHeadlessDrawing = true</c> is the default and makes <c>CaptureRenderedFrame</c> throw; the
/// assembly-wide <see cref="DesignerHeadlessApp"/> already configures <c>UseSkia()</c> +
/// <c>UseHeadlessDrawing = false</c>, exactly as <c>FormCanvasRenderTests</c> does, so this fixture
/// needs no setup of its own.</para>
///
/// <para>⛔⛔ <see cref="FormCanvasControl.RenderSchematicForTest"/> returns a
/// <see cref="FormCanvasControl.SchematicFrame"/> record, not a bare hash string. A record's default
/// equality is STRUCTURAL over every field, so <c>Is.EqualTo</c>/<c>Is.Not.EqualTo</c> on two frames
/// silently compares <c>(Hash, CaptionOrigin)</c> pairs — which still compiles and still passes,
/// while no longer asserting what the test's name says. Every comparison in this file that means to
/// compare rendered PIXELS reads <c>.Hash</c> explicitly, for exactly that reason.</para>
/// </summary>
[TestFixture]
public class FormSchematicPinTests
{
    /// <summary>
    /// Every <see cref="FormSchematic"/> value must have its own toolbox/tray glyph: none may fall
    /// through to the <c>"?"</c> fallback, and no two schematics may share a mark (a shared mark
    /// would let a schematic silently draw as another kind's tray entry).
    /// </summary>
    [Test]
    public void EverySchematic_HasItsOwnGlyph()
    {
        var marks = Enum.GetValues<FormSchematic>().ToDictionary(s => s, FormToolboxViewModel.GlyphFor);

        Assert.That(marks.Values, Has.None.EqualTo("?"), "a schematic fell through to the fallback mark");
        Assert.That(marks.Values.Distinct().Count(), Is.EqualTo(marks.Count), "two schematics wear one mark");
    }

    /// <summary>
    /// Every <see cref="FormSchematic"/> value must paint DIFFERENT pixels from every other value,
    /// through the same seam <c>DrawControl</c> will call — at one bounds, with one label, so only
    /// the shape itself can account for a difference. This is the per-VALUE pin
    /// <c>FormCanvasRenderTests.EveryControlKindRendersDistinctly</c> cannot give: that fixture is
    /// driven by catalog ROWS, and an item schematic with no row yet (or ever, for a schematic no
    /// row happens to use) would have no fixture to render it and no way to be caught.
    ///
    /// <para>Compares <c>.Hash</c> explicitly — this is a DISTINCTNESS gate over rendered pixels, and
    /// comparing whole frames would also fold <c>CaptionOrigin</c> into the key, which is a different
    /// (and, for this gate, irrelevant) question.</para>
    /// </summary>
    [AvaloniaTest]
    public void EverySchematic_PaintsDifferently()
    {
        // Through the seam, at one bounds, with one label — so only the SHAPE can differ.
        var hashes = new Dictionary<string, List<FormSchematic>>();
        foreach (var schematic in Enum.GetValues<FormSchematic>())
        {
            var frame = FormCanvasControl.RenderSchematicForTest(schematic, new Rect(20, 20, 140, 40), "X");
            (hashes.TryGetValue(frame.Hash, out var l) ? l : hashes[frame.Hash] = new()).Add(schematic);
        }

        var collisions = hashes.Values.Where(l => l.Count > 1).ToList();
        Assert.That(collisions, Is.Empty,
            "identical pixels: " + string.Join(" | ", collisions.Select(c => string.Join(",", c))));
    }

    /// <summary>
    /// The three docked-band schematics (spec §6) are chrome, not widgets: they draw NO caption at
    /// all, ever. <see cref="EverySchematic_PaintsDifferently"/> and
    /// <see cref="FormCanvasRenderTests.EveryControlKindRendersDistinctly"/> are both DISTINCTNESS
    /// gates — they hash one frame per kind/row and only require the frames differ from each OTHER,
    /// so a band that gained a caption would still be unique among all schematics and neither gate
    /// would notice. This test is the direct label-invariance pin: the same band rendered with two
    /// very different labels must be byte-identical (<c>.Hash</c>, compared explicitly for the same
    /// reason as every other site in this file), because nothing about a band's own drawing may
    /// depend on the label at all — AND it must report no caption origin at all, which a hash
    /// comparison alone cannot see: two renders that both compute the SAME non-null origin (because
    /// neither one actually depends on the label) would still hash equal to each other while a
    /// caption position was reported for chrome that draws none.
    /// </summary>
    [AvaloniaTest]
    public void EveryBand_IgnoresItsLabel()
    {
        foreach (var band in Bands)
        {
            var shortLabel = FormCanvasControl.RenderSchematicForTest(band, new Rect(20, 20, 140, 40), ShortLabel);
            var longLabel = FormCanvasControl.RenderSchematicForTest(band, new Rect(20, 20, 140, 40), LongLabel);

            Assert.That(shortLabel.CaptionOrigin, Is.Null,
                $"{band} reported a caption origin — bands draw no caption at all (spec §6)");
            Assert.That(longLabel.CaptionOrigin, Is.Null,
                $"{band} reported a caption origin — bands draw no caption at all (spec §6)");
            Assert.That(longLabel.Hash, Is.EqualTo(shortLabel.Hash),
                $"{band} drew a caption: its frame changed when the label changed from " +
                $"\"{ShortLabel}\" to \"{LongLabel}\" — a band must ignore its label entirely");
        }
    }

    /// <summary>
    /// Every schematic that is NOT one of the three bands must report a non-null
    /// <see cref="FormCanvasControl.SchematicFrame.CaptionOrigin"/> — direct proof that the shared
    /// <c>DrawText</c> call at the end of <c>DrawSchematic</c> actually ran for it.
    ///
    /// <para>⛔⛔ This USED to be a hash-difference comparison (render once per a short and a long
    /// label, assert the two frames differ) and that is TAUTOLOGICAL for <see cref="FormSchematic.Group"/>
    /// and <see cref="FormSchematic.Link"/>: both arms draw label-SIZED geometry inside their own
    /// <c>case</c>, before the shared caption draw ever runs — <c>Group</c> paints a background patch
    /// sized <c>caption.Width + 4</c> behind where the caption belongs, and <c>Link</c> draws its
    /// underline the length of <c>linkText.Width</c>. Turn either arm's <c>break</c> into
    /// <c>return null</c> — deleting the caption entirely, exactly like a band — and that background
    /// patch or underline STILL differs in width between a short and a long label, so the hash-diff
    /// test kept passing while the caption itself was never drawn. Asserting directly on
    /// <c>CaptionOrigin</c> — the exact value the shared <c>DrawText</c> call returns — has no such
    /// blind spot: it is null if and only if that call never ran, regardless of what any OTHER
    /// geometry in the same arm happened to measure.</para>
    ///
    /// <para>This still does not (and need not) pin WHERE the caption landed — that is
    /// <see cref="FormSchematicPinTests"/>'s caption-origin pins below, which assert the exact offset
    /// or the exact delta per arm. This test only proves presence.</para>
    /// </summary>
    [AvaloniaTest]
    public void EveryCaptioningSchematic_ActuallyDrawsItsCaption()
    {
        var bands = new HashSet<FormSchematic>(Bands);
        foreach (var schematic in Enum.GetValues<FormSchematic>().Where(s => !bands.Contains(s)))
        {
            var frame = FormCanvasControl.RenderSchematicForTest(schematic, new Rect(20, 20, 140, 40), ShortLabel);

            Assert.That(frame.CaptionOrigin, Is.Not.Null,
                $"{schematic} reported no caption origin — its caption may have silently stopped drawing");
        }
    }

    /// <summary>
    /// Commit 24b's caption-origin pin, and the reason <see cref="FormCanvasControl.SchematicFrame
    /// .CaptionOrigin"/> exists at all: mutant (d) (the <see cref="FormSchematic.Button"/> arm's
    /// centring arithmetic deleted) survived every gate above it — the frame is still unique among
    /// every OTHER schematic's frame (<see cref="EverySchematic_PaintsDifferently"/>), and it still
    /// reports a non-null origin (<see cref="EveryCaptioningSchematic_ActuallyDrawsItsCaption"/>),
    /// because a caption drawn at the WRONG origin is still drawn — presence and position are
    /// orthogonal facts, and only a recorded coordinate can pin the second.
    ///
    /// <para>⛔ Font metrics differ across machines, so a literal expected coordinate (Button's
    /// measured 86.46 at width 140) would be exactly the brittleness golden pixel hashes were refused
    /// for here. Two kinds of arm need two kinds of assertion:</para>
    ///
    /// <para>— <b>Metric-dependent arms</b> (<see cref="FormSchematic.Button"/> centres on
    /// <c>caption.Width</c>, <see cref="FormSchematic.StatusLabel"/> on <c>caption.Height</c>) are
    /// pinned by the DELTA between two bounds, which cancels the measured term algebraically for ANY
    /// caption size: widening a Button's bounds by 40px moves its origin.X by exactly +20 no matter
    /// what "X" measures as (<c>((180-w)/2) - ((140-w)/2) == 20</c> for every <c>w</c>), and
    /// heightening a StatusLabel's bounds by 40px moves its origin.Y by exactly +20 the same way.
    /// <see cref="CaptionOrigin_MetricDependentArms_MoveByExactlyTheBoundsDelta"/> also checks the
    /// OTHER axis does not move, and that the <c>Math.Max</c> clamp each arm applies is not reached
    /// at either bounds pair — a clamped delta would not be 20 and the test would be measuring the
    /// clamp instead of the centring.</para>
    ///
    /// <para>— <b>Constant-offset arms</b> (<see cref="FormSchematic.MenuItem"/>,
    /// <see cref="FormSchematic.ToolButton"/>, <see cref="FormSchematic.Check"/>/
    /// <see cref="FormSchematic.Radio"/>, <see cref="FormSchematic.Tabs"/>, and the default arm) never
    /// consult measured text, so <see cref="CaptionOrigin_ConstantOffsetArms_MatchTheirExactOffset"/>
    /// asserts their offset from <c>bounds</c> EXACTLY. A delta-only table would let, say, MenuItem's
    /// documented 8px inset (<c>bounds.X + 8</c>) silently become ToolButton's 4px inset
    /// (<c>box.X + 4</c>, itself <c>bounds.X + 6</c> once <c>box</c>'s own offset is folded in) and
    /// still pass every delta check, because the delta of any constant offset against itself is
    /// always zero.</para>
    /// </summary>
    [AvaloniaTest]
    public void CaptionOrigin_MetricDependentArms_MoveByExactlyTheBoundsDelta()
    {
        // Button centres on caption.Width: widening the rect by 40 must move X by exactly half that
        // — +20 — for ANY caption width, because the measured term cancels out of the subtraction.
        var buttonNarrow = FormCanvasControl.RenderSchematicForTest(
            FormSchematic.Button, new Rect(20, 20, 140, 40), ShortLabel);
        var buttonWide = FormCanvasControl.RenderSchematicForTest(
            FormSchematic.Button, new Rect(20, 20, 180, 40), ShortLabel);

        Assert.That(buttonNarrow.CaptionOrigin, Is.Not.Null, "Button@140 reported no caption origin");
        Assert.That(buttonWide.CaptionOrigin, Is.Not.Null, "Button@180 reported no caption origin");

        var narrowPoint = buttonNarrow.CaptionOrigin!.Value;
        var widePoint = buttonWide.CaptionOrigin!.Value;

        // ⚠ Confirms the Math.Max(3, …) clamp is not reached at either width: (140-w)/2 and
        // (180-w)/2 are both comfortably above 3 for any short caption, so the delta below measures
        // the centring arithmetic and not the clamp.
        Assert.That(narrowPoint.X, Is.GreaterThan(20 + 3), "Button@140's origin sits at the clamp floor, not centred");

        Assert.That(widePoint.X - narrowPoint.X, Is.EqualTo(20).Within(0.001),
            "Button's centring must move by exactly half the width increase, independent of caption width");
        Assert.That(widePoint.Y, Is.EqualTo(narrowPoint.Y).Within(0.001),
            "Button's height did not change between the two bounds, so Y must not move");

        // StatusLabel centres on caption.Height: heightening the rect by 40 must move Y by exactly
        // half that — +20 — for ANY caption height, by the same cancellation.
        var statusShort = FormCanvasControl.RenderSchematicForTest(
            FormSchematic.StatusLabel, new Rect(20, 20, 140, 40), ShortLabel);
        var statusTall = FormCanvasControl.RenderSchematicForTest(
            FormSchematic.StatusLabel, new Rect(20, 20, 140, 80), ShortLabel);

        Assert.That(statusShort.CaptionOrigin, Is.Not.Null, "StatusLabel@40 reported no caption origin");
        Assert.That(statusTall.CaptionOrigin, Is.Not.Null, "StatusLabel@80 reported no caption origin");

        var shortPoint = statusShort.CaptionOrigin!.Value;
        var tallPoint = statusTall.CaptionOrigin!.Value;

        // ⚠ Confirms the Math.Max(2, …) clamp is not reached at either height, the same reason as
        // Button's clamp check above.
        Assert.That(shortPoint.Y, Is.GreaterThan(20 + 2), "StatusLabel@40's origin sits at the clamp floor, not centred");

        Assert.That(tallPoint.Y - shortPoint.Y, Is.EqualTo(20).Within(0.001),
            "StatusLabel's centring must move by exactly half the height increase, independent of caption height");
        Assert.That(tallPoint.X, Is.EqualTo(shortPoint.X).Within(0.001),
            "StatusLabel's width did not change between the two bounds, so X must not move");
    }

    /// <summary>See the caption-origin pin's summary above for why this is exact rather than a delta.</summary>
    [AvaloniaTest]
    public void CaptionOrigin_ConstantOffsetArms_MatchTheirExactOffset()
    {
        var bounds = new Rect(20, 20, 140, 40);

        // (schematic, offset from bounds.X, offset from bounds.Y)
        var table = new (FormSchematic Schematic, double OffsetX, double OffsetY)[]
        {
            // 8px inset from the cell's own edge — NOT the 4px ToolButton uses; the two must stay
            // distinguishable or a MenuItem row could silently draw with ToolButton's inset.
            (FormSchematic.MenuItem, 8, 2),

            // box = (bounds.X + 2, bounds.Y + 2, …); label sits at (box.X + 4, box.Y + 3).
            (FormSchematic.ToolButton, 6, 5),

            // glyph = (bounds.X + 1, …, side, side) with side == GlyphSide (13) at this bounds size;
            // label sits at glyph.Right + 4 == bounds.X + 1 + 13 + 4.
            (FormSchematic.Check, 18, 2),
            (FormSchematic.Radio, 18, 2),

            // Fixed, below the tab strip — never consults the label or the bounds size.
            (FormSchematic.Tabs, 6, 18),

            // The unmodified default: FormSchematic.Input never assigns labelOrigin, so it keeps the
            // value declared before the switch. Picked over FormSchematic.Text (which also leaves it
            // unmodified) because Input is the one that actually falls through to `default:`.
            (FormSchematic.Input, 4, 2),
        };

        foreach (var (schematic, offsetX, offsetY) in table)
        {
            var frame = FormCanvasControl.RenderSchematicForTest(schematic, bounds, ShortLabel);
            var expected = new Point(bounds.X + offsetX, bounds.Y + offsetY);

            Assert.That(frame.CaptionOrigin, Is.EqualTo(expected),
                $"{schematic}'s caption origin drifted from its documented constant offset from bounds");
        }
    }

    /// <summary>
    /// The three docked-band schematics report NO caption origin at all — the direct pin
    /// <see cref="EveryBand_IgnoresItsLabel"/> also carries (duplicated there because a mutation that
    /// only breaks the null-ness, and not the label-invariance, needs both to go red for the same
    /// reason <see cref="EveryCaptioningSchematic_ActuallyDrawsItsCaption"/> needed CaptionOrigin
    /// instead of a hash diff: a band returning its unused <c>labelOrigin</c> instead of <c>null</c>
    /// changes NO pixel — the return happens before anything is drawn — so nothing but reading
    /// <c>CaptionOrigin</c> directly can see it).
    /// </summary>
    [AvaloniaTest]
    public void CaptionOrigin_Bands_AreAlwaysNull()
    {
        var bounds = new Rect(20, 20, 140, 40);
        foreach (var band in Bands)
        {
            var frame = FormCanvasControl.RenderSchematicForTest(band, bounds, ShortLabel);
            Assert.That(frame.CaptionOrigin, Is.Null,
                $"{band} reported a caption origin — bands draw no caption at all (spec §6)");
        }
    }

    /// <summary>The three docked-band schematics — chrome strips that draw no caption (spec §6).</summary>
    private static readonly FormSchematic[] Bands =
    {
        FormSchematic.MenuBar, FormSchematic.ToolBar, FormSchematic.StatusBar
    };

    // Different length AND different glyphs, so neither label can coincidentally measure to the same
    // width or clip to the same pixels as the other: a single "X" versus twelve "W"s (a wide glyph).
    private const string ShortLabel = "X";
    private const string LongLabel = "WWWWWWWWWWWW";
}
