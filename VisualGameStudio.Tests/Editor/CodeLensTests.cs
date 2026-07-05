using Avalonia.Controls;
using AvaloniaEdit.Document;
using NUnit.Framework;
using VisualGameStudio.Editor.Rendering;
using VisualGameStudio.Editor.Services;
using VisualGameStudio.Editor.TextMarkers;

namespace VisualGameStudio.Tests.Editor;

/// <summary>
/// Tests for the CodeLens rendering pipeline:
/// - <see cref="CodeLensElementGenerator.ComputeInterestedOffset"/> maps lens lines to
///   their END offset (JetBrains-style end-of-line inlay) so the lens is never inserted
///   before the first character of the line (which used to displace the first token).
/// - <see cref="CodeLensInlineElement"/> spans zero document characters and creates no
///   caret stops, so arrow-key navigation and selection behave exactly as without a lens.
/// - <see cref="CodeLensManager"/> line bookkeeping.
/// </summary>
[TestFixture]
public class CodeLensTests
{
    #region ComputeInterestedOffset — lens line to interested-offset mapping

    [Test]
    public void ComputeInterestedOffset_LineWithoutLenses_ReturnsMinusOne()
    {
        int result = CodeLensElementGenerator.ComputeInterestedOffset(
            startOffset: 0, lineOffset: 0, lineEndOffset: 10, lineHasLenses: false);

        Assert.That(result, Is.EqualTo(-1));
    }

    [Test]
    public void ComputeInterestedOffset_QueryAtLineStart_ReturnsLineEndOffset()
    {
        // The generator is first queried at the line start; the lens must anchor to
        // the END of the line, never the start.
        int result = CodeLensElementGenerator.ComputeInterestedOffset(
            startOffset: 0, lineOffset: 0, lineEndOffset: 16, lineHasLenses: true);

        Assert.That(result, Is.EqualTo(16));
    }

    [Test]
    public void ComputeInterestedOffset_QueryMidLine_ReturnsLineEndOffset()
    {
        int result = CodeLensElementGenerator.ComputeInterestedOffset(
            startOffset: 7, lineOffset: 0, lineEndOffset: 16, lineHasLenses: true);

        Assert.That(result, Is.EqualTo(16));
    }

    [Test]
    public void ComputeInterestedOffset_QueryExactlyAtLineEnd_ReturnsLineEndOffset()
    {
        int result = CodeLensElementGenerator.ComputeInterestedOffset(
            startOffset: 16, lineOffset: 0, lineEndOffset: 16, lineHasLenses: true);

        Assert.That(result, Is.EqualTo(16));
    }

    [Test]
    public void ComputeInterestedOffset_QueryPastLineEnd_ReturnsMinusOne()
    {
        // After the zero-length element is constructed at EndOffset, AvaloniaEdit asks
        // again at offset+1 (inside the newline delimiter). We must not return an offset
        // behind the query position.
        int result = CodeLensElementGenerator.ComputeInterestedOffset(
            startOffset: 17, lineOffset: 0, lineEndOffset: 16, lineHasLenses: true);

        Assert.That(result, Is.EqualTo(-1));
    }

    [Test]
    public void ComputeInterestedOffset_EmptyLine_ReturnsMinusOne()
    {
        // Lenses anchor to declarations; an empty line has nothing to annotate and a
        // lens-only visual line would degrade caret navigation. Skip it.
        int result = CodeLensElementGenerator.ComputeInterestedOffset(
            startOffset: 5, lineOffset: 5, lineEndOffset: 5, lineHasLenses: true);

        Assert.That(result, Is.EqualTo(-1));
    }

    [Test]
    public void ComputeInterestedOffset_NeverReturnsOffsetBeforeAnyLineCharacter()
    {
        // Regression guard for the original bug: the lens must never be inserted at or
        // before the first character of the line.
        int result = CodeLensElementGenerator.ComputeInterestedOffset(
            startOffset: 20, lineOffset: 20, lineEndOffset: 35, lineHasLenses: true);

        Assert.That(result, Is.EqualTo(35));
        Assert.That(result, Is.GreaterThan(20));
    }

    [Test]
    public void ComputeInterestedOffset_WithRealDocument_MapsDeclarationLineToItsEndOffset()
    {
        var document = new TextDocument("Public Sub Foo()\r\n    x = 1\r\nEnd Sub");
        var line1 = document.GetLineByNumber(1);

        // Query at the start of the declaration line, as AvaloniaEdit does.
        int result = CodeLensElementGenerator.ComputeInterestedOffset(
            line1.Offset, line1.Offset, line1.EndOffset, lineHasLenses: true);

        Assert.That(result, Is.EqualTo(line1.EndOffset));
        Assert.That(document.GetText(line1.Offset, result - line1.Offset),
            Is.EqualTo("Public Sub Foo()"),
            "the lens anchor must sit after the full line text");
    }

    #endregion

    #region CodeLensInlineElement — caret and edit safety

    private static CodeLensInlineElement CreateElement()
        => new(new Control());

    [Test]
    public void CodeLensInlineElement_SpansZeroDocumentCharacters()
    {
        var element = CreateElement();

        Assert.That(element.DocumentLength, Is.EqualTo(0),
            "the lens must not consume document text");
    }

    [Test]
    public void CodeLensInlineElement_ProvidesNoCaretStops()
    {
        var element = CreateElement();

        foreach (var direction in new[] { LogicalDirection.Forward, LogicalDirection.Backward })
        {
            foreach (CaretPositioningMode mode in Enum.GetValues(typeof(CaretPositioningMode)))
            {
                foreach (var visualColumn in new[] { -1, 0, 1, 2, 100 })
                {
                    Assert.That(element.GetNextCaretPosition(visualColumn, direction, mode),
                        Is.EqualTo(-1),
                        $"no caret stop expected (column={visualColumn}, {direction}, {mode})");
                }
            }
        }
    }

    [Test]
    public void CodeLensInlineElement_HandlesLineBorders_SuppressesImplicitStopAfterLens()
    {
        var element = CreateElement();

        Assert.That(element.HandlesLineBorders, Is.True,
            "the implicit end-of-line caret stop after the lens must be suppressed so " +
            "End/Left land before the lens, on the last text character boundary");
    }

    #endregion

    #region CodeLensManager — line bookkeeping

    [Test]
    public void CodeLensManager_SetCodeLens_GroupsItemsByLine()
    {
        var manager = new CodeLensManager();
        manager.SetCodeLens(new[]
        {
            new CodeLensItem { Line = 3, Title = "2 references" },
            new CodeLensItem { Line = 3, Title = "Run" },
            new CodeLensItem { Line = 8, Title = "1 reference" }
        });

        Assert.That(manager.HasLenses, Is.True);
        Assert.That(manager.HasLensesForLine(3), Is.True);
        Assert.That(manager.HasLensesForLine(8), Is.True);
        Assert.That(manager.HasLensesForLine(4), Is.False);
        Assert.That(manager.GetLensesForLine(3), Has.Count.EqualTo(2));
        Assert.That(manager.GetLensesForLine(8), Has.Count.EqualTo(1));
        Assert.That(manager.GetLensesForLine(4), Is.Empty);
        Assert.That(manager.LinesWithLenses, Is.EquivalentTo(new[] { 3, 8 }));
    }

    [Test]
    public void CodeLensManager_SetCodeLens_ReplacesPreviousData()
    {
        var manager = new CodeLensManager();
        manager.SetCodeLens(new[] { new CodeLensItem { Line = 1, Title = "old" } });
        manager.SetCodeLens(new[] { new CodeLensItem { Line = 2, Title = "new" } });

        Assert.That(manager.HasLensesForLine(1), Is.False);
        Assert.That(manager.HasLensesForLine(2), Is.True);
        Assert.That(manager.GetLensesForLine(2)[0].Title, Is.EqualTo("new"));
    }

    [Test]
    public void CodeLensManager_ClearCodeLens_RemovesEverythingAndRaisesChanged()
    {
        var manager = new CodeLensManager();
        manager.SetCodeLens(new[] { new CodeLensItem { Line = 5, Title = "x" } });

        int changedCount = 0;
        manager.Changed += (_, _) => changedCount++;
        manager.ClearCodeLens();

        Assert.That(manager.HasLenses, Is.False);
        Assert.That(manager.HasLensesForLine(5), Is.False);
        Assert.That(changedCount, Is.EqualTo(1));
    }

    [Test]
    public void CodeLensManager_SetCodeLens_RaisesChanged()
    {
        var manager = new CodeLensManager();
        int changedCount = 0;
        manager.Changed += (_, _) => changedCount++;

        manager.SetCodeLens(new[] { new CodeLensItem { Line = 1, Title = "x" } });

        Assert.That(changedCount, Is.EqualTo(1));
    }

    #endregion
}
