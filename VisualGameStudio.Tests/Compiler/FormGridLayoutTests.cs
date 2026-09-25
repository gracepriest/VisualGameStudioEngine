using Avalonia;
using BasicLang.Forms;
using NUnit.Framework;
using VisualGameStudio.Shell.Controls;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// The grid a <c>.blwebform</c> is drawn on — cell rectangles, and which cell a point is in.
///
/// <para>⛔ <b>An approximation, and it must stay an honest one.</b> <c>Cols</c>/<c>Rows</c> are CSS
/// <c>grid-template-*</c> track lists written verbatim, and CSS track sizing depends on content, on
/// fonts, on the viewport — none of which the IDE has, because the canvas is a schematic and there
/// is no browser behind it. So <c>fr</c> weights and <c>px</c> tracks are honoured, and everything
/// else — <c>auto</c>, <c>minmax()</c>, percentages — is drawn as one flexible share. The canvas
/// shows you WHICH CELL you are dropping into, which is the thing the document actually records;
/// it does not claim to show you what the page will look like.</para>
/// </summary>
[TestFixture]
public class FormGridLayoutTests
{
    private static FormLayout Grid(string? cols, string? rows, string? gap = "0px") =>
        new() { Kind = FormLayoutKind.Grid, Cols = cols, Rows = rows, Gap = gap };

    // ==================================================================
    // Track lists
    // ==================================================================

    [Test]
    public void ATrackList_IsSplitOnCommas()
    {
        Assert.That(FormGridLayout.ParseTracks("120px,1fr,auto"), Has.Count.EqualTo(3));
    }

    [Test]
    public void SpacesAroundTracks_AreIgnored()
    {
        Assert.That(FormGridLayout.ParseTracks(" 120px , 1fr "), Has.Count.EqualTo(2));
    }

    [Test]
    public void AnAbsentTrackList_IsOneTrack()
    {
        // A grid with no Cols is one column wide. Zero tracks would make every cell rectangle empty
        // and the whole page undroppable.
        Assert.Multiple(() =>
        {
            Assert.That(FormGridLayout.ParseTracks(null), Has.Count.EqualTo(1));
            Assert.That(FormGridLayout.ParseTracks(""), Has.Count.EqualTo(1));
            Assert.That(FormGridLayout.ParseTracks("   "), Has.Count.EqualTo(1));
        });
    }

    // ==================================================================
    // Cell rectangles
    // ==================================================================

    [Test]
    public void TwoEqualColumns_SplitTheSurface()
    {
        var layout = Grid("1fr,1fr", "1fr");

        var left = FormGridLayout.CellRect(layout, new Size(400, 300), 0, 0, 1, 1);
        var right = FormGridLayout.CellRect(layout, new Size(400, 300), 1, 0, 1, 1);

        Assert.Multiple(() =>
        {
            Assert.That(left, Is.EqualTo(new Rect(0, 0, 200, 300)));
            Assert.That(right, Is.EqualTo(new Rect(200, 0, 200, 300)));
        });
    }

    [Test]
    public void AWeightedTrack_TakesItsShare()
    {
        var layout = Grid("1fr,3fr", "1fr");

        Assert.Multiple(() =>
        {
            Assert.That(FormGridLayout.CellRect(layout, new Size(400, 100), 0, 0, 1, 1).Width, Is.EqualTo(100));
            Assert.That(FormGridLayout.CellRect(layout, new Size(400, 100), 1, 0, 1, 1).Width, Is.EqualTo(300));
        });
    }

    [Test]
    public void APixelTrack_KeepsItsSize_AndTheRestFlexes()
    {
        var layout = Grid("120px,1fr", "1fr");

        Assert.Multiple(() =>
        {
            Assert.That(FormGridLayout.CellRect(layout, new Size(400, 100), 0, 0, 1, 1).Width, Is.EqualTo(120));
            Assert.That(FormGridLayout.CellRect(layout, new Size(400, 100), 1, 0, 1, 1).Width, Is.EqualTo(280));
        });
    }

    [Test]
    public void AnUnknownTrackUnit_IsDrawnAsOneFlexibleShare()
    {
        // ⚠ `auto`, percentages and calc() are all legal CSS this canvas cannot evaluate — `auto`
        // sizes to content, and there is no content here because there is no browser. Drawing them
        // as an equal share is an approximation; crashing, or drawing a zero-width column the user
        // cannot drop into, would be worse.
        var layout = Grid("auto,1fr", "1fr");

        Assert.Multiple(() =>
        {
            Assert.That(FormGridLayout.CellRect(layout, new Size(400, 100), 0, 0, 1, 1).Width, Is.EqualTo(200));
            Assert.That(FormGridLayout.CellRect(layout, new Size(400, 100), 1, 0, 1, 1).Width, Is.EqualTo(200));
        });
    }

    [Test]
    public void ATrackFunctionContainingACommaSplitsApart_MatchingWhatTheEmitterDoes()
    {
        // ⛔ NOT a canvas bug, and worth knowing before someone "fixes" it here. `Cols` is a
        // COMMA-SEPARATED list, so a CSS function with a comma in it — minmax(a,b), repeat(2,1fr),
        // clamp() — cannot be expressed in this format at all. FormAssetEmitter.Tracks splits on
        // the same commas and rejoins with spaces, so `minmax(100px, 1fr)` already ships as
        // `minmax(100px 1fr)`: invalid CSS, today, with or without this canvas. The canvas splits
        // identically ON PURPOSE — a canvas that guessed differently from the emitter would draw a
        // grid the page does not have.
        var layout = Grid("minmax(100px, 1fr),1fr", "1fr");

        Assert.That(FormGridLayout.ParseTracks(layout.Cols), Has.Count.EqualTo(3),
            "three fragments, exactly as the emitter sees them");
    }

    [Test]
    public void AFixedTrackWiderThanTheSurface_DoesNotProduceNegativeCells()
    {
        var layout = Grid("900px,1fr", "1fr");

        var flexible = FormGridLayout.CellRect(layout, new Size(400, 100), 1, 0, 1, 1);
        Assert.That(flexible.Width, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void TheGap_SitsBetweenTracksAndNotOutsideThem()
    {
        // ⛔ n tracks have n-1 gaps. Counting one too many leaves a margin down the right-hand edge
        // that nothing explains; one too few and the last column runs off the surface.
        var layout = Grid("1fr,1fr", "1fr", gap: "10px");

        var left = FormGridLayout.CellRect(layout, new Size(410, 100), 0, 0, 1, 1);
        var right = FormGridLayout.CellRect(layout, new Size(410, 100), 1, 0, 1, 1);

        Assert.Multiple(() =>
        {
            Assert.That(left.X, Is.EqualTo(0));
            Assert.That(left.Width, Is.EqualTo(200));
            Assert.That(right.X, Is.EqualTo(210));
            Assert.That(right.Right, Is.EqualTo(410));
        });
    }

    [Test]
    public void RowsWorkTheSameWayAsColumns()
    {
        var layout = Grid("1fr", "1fr,1fr");

        Assert.Multiple(() =>
        {
            Assert.That(FormGridLayout.CellRect(layout, new Size(100, 300), 0, 0, 1, 1).Height, Is.EqualTo(150));
            Assert.That(FormGridLayout.CellRect(layout, new Size(100, 300), 0, 1, 1, 1).Y, Is.EqualTo(150));
        });
    }

    [Test]
    public void ASpanCoversItsCellsAndTheGapBetweenThem()
    {
        var layout = Grid("1fr,1fr", "1fr", gap: "10px");

        var spanned = FormGridLayout.CellRect(layout, new Size(410, 100), 0, 0, colSpan: 2, rowSpan: 1);

        Assert.That(spanned.Width, Is.EqualTo(410), "two 200s plus the 10 between them");
    }

    [Test]
    public void ACellOutsideTheGrid_IsClampedRatherThanThrowing()
    {
        // A document can name Col="9" on a two-column grid — it is the user's file, and the reader
        // does not police it. Drawing it somewhere sane beats an exception in a render pass.
        var layout = Grid("1fr,1fr", "1fr");

        Assert.DoesNotThrow(() => FormGridLayout.CellRect(layout, new Size(400, 100), 9, 9, 1, 1));
    }

    // ==================================================================
    // Which cell is under the pointer
    // ==================================================================

    [Test]
    public void APointInAColumn_NamesThatColumn()
    {
        var layout = Grid("1fr,1fr", "1fr,1fr");

        Assert.Multiple(() =>
        {
            Assert.That(FormGridLayout.CellAt(layout, new Size(400, 300), new Point(50, 50)),
                Is.EqualTo((0, 0)));
            Assert.That(FormGridLayout.CellAt(layout, new Size(400, 300), new Point(300, 50)),
                Is.EqualTo((1, 0)));
            Assert.That(FormGridLayout.CellAt(layout, new Size(400, 300), new Point(300, 250)),
                Is.EqualTo((1, 1)));
        });
    }

    [Test]
    public void APointInTheGap_StillNamesACell()
    {
        // ⛔ Dropping on a gutter must not be a refusal. The gaps are a few pixels wide and the user
        // cannot see them at canvas zoom; "nothing happened" is the failure this whole feature was
        // added to stop. The nearer track wins.
        var layout = Grid("1fr,1fr", "1fr", gap: "10px");

        Assert.That(FormGridLayout.CellAt(layout, new Size(410, 100), new Point(205, 50)),
            Is.Not.Null);
    }

    [Test]
    public void APointOutsideTheSurface_IsNotACell()
    {
        var layout = Grid("1fr,1fr", "1fr");

        Assert.Multiple(() =>
        {
            Assert.That(FormGridLayout.CellAt(layout, new Size(400, 300), new Point(-5, 50)), Is.Null);
            Assert.That(FormGridLayout.CellAt(layout, new Size(400, 300), new Point(50, 400)), Is.Null);
        });
    }

    [Test]
    public void EveryCellOfTheGrid_CanBeReachedByAPointInsideIt()
    {
        // The round trip that matters: a cell the canvas DREW must be a cell the pointer can hit,
        // or there are drop targets on screen that do not accept drops.
        var layout = Grid("120px,1fr,2fr", "auto,1fr", gap: "8px");
        var surface = new Size(600, 400);

        Assert.Multiple(() =>
        {
            for (var col = 0; col < 3; col++)
            {
                for (var row = 0; row < 2; row++)
                {
                    var rect = FormGridLayout.CellRect(layout, surface, col, row, 1, 1);
                    Assert.That(FormGridLayout.CellAt(layout, surface, rect.Center),
                        Is.EqualTo((col, row)), $"centre of cell {col},{row}");
                }
            }
        });
    }
}
