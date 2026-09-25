using System.Linq;
using Avalonia;
using Avalonia.Headless.NUnit;
using BasicLang.Forms;
using NUnit.Framework;
using VisualGameStudio.Shell.Controls;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// ⛔⛔ The LIVE-render half of the menu-editor clipping fix. <c>FormMenuEditorDefectsTests</c>'
/// clipping rows measure through <see cref="FormCanvasControl.MeasureCaptionForTest"/>, which is not
/// the draw path — they stayed green with <c>DrawSchematic</c> and <c>DrawTypeHereSlot</c> still
/// drawing at a fixed 12px. This drives a real document through the real <c>Render</c>
/// (<see cref="FormCanvasControl.RenderDocumentForTest"/>) at a viewport whose fitted zoom is 0.5,
/// and asserts on what the render actually shaped: the font size, and that the caption as drawn
/// ends inside the cell as drawn.
/// </summary>
[TestFixture]
public class FormCaptionLiveRenderTests
{
    [AvaloniaTest]
    public void LiveRender_AtFittedZoomHalf_DrawsItemAndSlotCaptionsScaledAndInsideTheirCells()
    {
        var open = new FormControl { Kind = "ToolStripMenuItem", Id = "i1" };
        open.Properties["Text"] = "&Open";
        var prefs = new FormControl { Kind = "ToolStripMenuItem", Id = "i1" };
        prefs.Properties["Text"] = "&Preferences...";

        var strip = new FormControl { Kind = "MenuStrip", Id = "menuStrip1" };
        strip.Children.Add(open);
        strip.Children.Add(prefs);

        var doc = new FormDocument { Target = FormTarget.WinForms, Name = "F", Width = 640, Height = 480 };
        doc.Controls.Add(strip);

        // Fit's margin is 24 each side: a (640*0.5 + 48) x (480*0.5 + 48) viewport fits at exactly 0.5.
        var viewport = new Size((640 * 0.5) + 48, (480 * 0.5) + 48);
        var frame = FormCanvasControl.RenderDocumentForTest(doc, selected: strip, typeHereHost: strip, viewport);

        Assert.That(frame.Zoom, Is.EqualTo(0.5).Within(1e-9), "the fixture viewport must fit at zoom 0.5");

        var items = frame.Captions.Where(c => c.Control == open || c.Control == prefs).ToList();
        var slot = frame.Captions.Where(c => c.SlotHost == strip).ToList();
        Assert.That(items, Has.Count.EqualTo(2), "both item cells must be drawn by the live render");
        Assert.That(slot, Has.Count.EqualTo(1), "the active strip's Type Here slot must be drawn by the live render");

        var expectedSize = FormCanvasTransform.CaptionFontSize * frame.Zoom;

        Assert.Multiple(() =>
        {
            foreach (var caption in items.Concat(slot))
            {
                Assert.That(caption.Drawn, Is.Not.Null, $"\"{caption.Label}\" drew no caption at all");
                if (caption.Drawn is not { } drawn)
                {
                    continue;
                }

                Assert.That(drawn.FontSize, Is.EqualTo(expectedSize).Within(1e-9),
                    $"\"{caption.Label}\" was shaped at {drawn.FontSize}px; at zoom {frame.Zoom} the live " +
                    $"render must draw it at {expectedSize}px");
                Assert.That(drawn.Origin.X + drawn.Width, Is.LessThanOrEqualTo(caption.Bounds.Right),
                    $"\"{caption.Label}\" as drawn ends at x={drawn.Origin.X + drawn.Width:F1}, past its cell's " +
                    $"right edge {caption.Bounds.Right:F1} — the owner's \"&Ope\"");
            }
        });
    }

    /// <summary>
    /// ⛔⛔ GAP — <c>FormMenuEditorDefectsTests.CaptionForTest_NeverPassesALiteralAccelerator_…</c>
    /// calls <see cref="FormCanvasControl.CaptionForTest"/> directly, bypassing the draw path
    /// entirely, so a mutant "<c>DrawControl</c> draws the raw <c>Text</c>" (never resolves the
    /// accelerator at all) survives it — <c>CaptionForTest</c> would still report the correct display
    /// string even while <c>DrawControl</c> ignored it. This drives the same three captions through
    /// the real <see cref="FormCanvasControl.RenderDocumentForTest"/> and compares the RECORDED drawn
    /// width against an Avalonia measurement of the display text — and, separately, against the raw
    /// text — at the font size the live render actually used, so a draw that reverted to the raw
    /// string is visible as a width that matches the WRONG string.
    /// </summary>
    [TestCase("&Open", "Open")]
    [TestCase("E&xit", "Exit")]
    [TestCase("&Preferences...", "Preferences...")]
    [AvaloniaTest]
    public void LiveRender_DrawsTheDisplayCaptionWidth_NeverTheRawAcceleratorText(string rawText, string displayText)
    {
        var item = new FormControl { Kind = "ToolStripMenuItem", Id = "i1" };
        item.Properties["Text"] = rawText;

        var strip = new FormControl { Kind = "MenuStrip", Id = "menuStrip1" };
        strip.Children.Add(item);

        var doc = new FormDocument { Target = FormTarget.WinForms, Name = "F", Width = 640, Height = 480 };
        doc.Controls.Add(strip);

        // Same fitted-0.5 viewport as the test above — the width check must hold away from 1:1 too.
        var viewport = new Size((640 * 0.5) + 48, (480 * 0.5) + 48);
        var frame = FormCanvasControl.RenderDocumentForTest(doc, selected: null, typeHereHost: null, viewport);

        var record = frame.Captions.SingleOrDefault(c => c.Control == item);
        Assert.That(record?.Drawn, Is.Not.Null, $"\"{rawText}\" must draw a caption in the live render");
        var drawn = record!.Drawn!;

        var displayWidth = FormCanvasControl.MeasureCaptionForTest(displayText, frame.Zoom).Width;
        var rawWidth = FormCanvasControl.MeasureCaptionForTest(rawText, frame.Zoom).Width;

        Assert.That(drawn.Width, Is.EqualTo(displayWidth).Within(0.5),
            $"\"{rawText}\" drew at width {drawn.Width:F2}px, but the display text \"{displayText}\" " +
            $"measures {displayWidth:F2}px at the recorded font size {drawn.FontSize:F2} — DrawControl " +
            "must hand DrawSchematic the resolved display caption, never the raw accelerator-marked text");
        Assert.That(drawn.Width, Is.Not.EqualTo(rawWidth).Within(0.5),
            $"\"{rawText}\" drew at width {drawn.Width:F2}px, indistinguishable from the RAW text's own " +
            $"{rawWidth:F2}px — the live path must resolve the accelerator, never draw the literal \"&\"");
    }

    /// <summary>
    /// ⛔⛔ GAP — nothing pins where the Type Here PLACEHOLDER text itself is drawn; every existing
    /// clipping/inset test measures a COMMITTED item's caption. A drifted inset in
    /// <c>DrawTypeHereSlot</c> (a private "4 × zoom" copy instead of the shared
    /// <see cref="FormCanvasTransform.ItemCaptionInset"/> answer the overlay and the committed item
    /// arms both use) survived every existing test. An empty <c>MenuStrip</c> has no items, so
    /// <see cref="FormCanvasTransform.SlotCaption(BasicLang.Forms.FormControl?, double)"/> resolves
    /// its default kind to <see cref="FormSchematic.MenuItem"/> — the same 8px-at-1:1 inset a
    /// committed menu item caption uses.
    /// </summary>
    [TestCase(0.5)]
    [TestCase(1.0)]
    [AvaloniaTest]
    public void LiveRender_DrawsTheTypeHerePlaceholder_AtTheSharedPerKindInset(double targetZoom)
    {
        var strip = new FormControl { Kind = "MenuStrip", Id = "menuStrip1" };
        var doc = new FormDocument { Target = FormTarget.WinForms, Name = "F", Width = 640, Height = 480 };
        doc.Controls.Add(strip);

        // Same margin arithmetic as the fixture above, generalised to the zoom under test.
        var viewport = new Size((640 * targetZoom) + 48, (480 * targetZoom) + 48);
        var frame = FormCanvasControl.RenderDocumentForTest(doc, selected: strip, typeHereHost: strip, viewport);

        Assert.That(frame.Zoom, Is.EqualTo(targetZoom).Within(1e-9), "the fixture viewport must fit at the target zoom");

        var slot = frame.Captions.SingleOrDefault(c => c.SlotHost == strip);
        Assert.That(slot?.Drawn, Is.Not.Null, "the active strip's Type Here slot must draw a placeholder caption");
        var drawn = slot!.Drawn!;

        var expectedInsetAtOne = FormCanvasTransform.ItemCaptionInset(FormSchematic.MenuItem);
        var expectedX = slot.Bounds.X + (expectedInsetAtOne * frame.Zoom);

        Assert.That(drawn.Origin.X, Is.EqualTo(expectedX).Within(0.5),
            $"the Type Here placeholder drew at x={drawn.Origin.X:F2}, but the shared per-kind inset " +
            $"({expectedInsetAtOne}px × zoom {frame.Zoom:F3}) puts it at x={expectedX:F2} — " +
            "DrawTypeHereSlot must use the same shared inset the overlay and the committed item arms " +
            "use, never a private copy that can drift");
    }
}
