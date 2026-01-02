using AvaloniaEdit.Document;
using NUnit.Framework;
using System.Reflection;
using VisualGameStudio.Editor.TextMarkers;

namespace VisualGameStudio.Tests.Editor;

[TestFixture]
public class BracketHighlighterTests
{
    private BracketHighlighter _highlighter = null!;

    [SetUp]
    public void SetUp()
    {
        _highlighter = new BracketHighlighter();
    }

    private (int offset1, int offset2) GetHighlightOffsets()
    {
        var type = typeof(BracketHighlighter);
        var offset1Field = type.GetField("_highlightOffset1", BindingFlags.NonPublic | BindingFlags.Instance);
        var offset2Field = type.GetField("_highlightOffset2", BindingFlags.NonPublic | BindingFlags.Instance);

        var offset1 = (int)offset1Field!.GetValue(_highlighter)!;
        var offset2 = (int)offset2Field!.GetValue(_highlighter)!;

        return (offset1, offset2);
    }

    [Test]
    public void UpdateBracketHighlight_NullDocument_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => _highlighter.UpdateBracketHighlight(null!, 0));
    }

    [Test]
    public void UpdateBracketHighlight_NegativeOffset_DoesNotThrow()
    {
        var document = new TextDocument("(test)");

        Assert.DoesNotThrow(() => _highlighter.UpdateBracketHighlight(document, -1));
    }

    [Test]
    public void UpdateBracketHighlight_OffsetBeyondLength_DoesNotThrow()
    {
        var document = new TextDocument("(test)");

        Assert.DoesNotThrow(() => _highlighter.UpdateBracketHighlight(document, 100));
    }

    [Test]
    public void UpdateBracketHighlight_Parentheses_MatchesCorrectly()
    {
        var document = new TextDocument("(test)");
        // Caret right after opening paren: "(|test)"
        _highlighter.UpdateBracketHighlight(document, 1);

        var (offset1, offset2) = GetHighlightOffsets();

        Assert.That(offset1, Is.EqualTo(0)); // Opening paren
        Assert.That(offset2, Is.EqualTo(5)); // Closing paren
    }

    [Test]
    public void UpdateBracketHighlight_SquareBrackets_MatchesCorrectly()
    {
        var document = new TextDocument("[test]");
        _highlighter.UpdateBracketHighlight(document, 1);

        var (offset1, offset2) = GetHighlightOffsets();

        Assert.That(offset1, Is.EqualTo(0));
        Assert.That(offset2, Is.EqualTo(5));
    }

    [Test]
    public void UpdateBracketHighlight_CurlyBraces_MatchesCorrectly()
    {
        var document = new TextDocument("{test}");
        _highlighter.UpdateBracketHighlight(document, 1);

        var (offset1, offset2) = GetHighlightOffsets();

        Assert.That(offset1, Is.EqualTo(0));
        Assert.That(offset2, Is.EqualTo(5));
    }

    [Test]
    public void UpdateBracketHighlight_NestedParentheses_MatchesInnermost()
    {
        var document = new TextDocument("((inner))");
        // Caret after second opening paren
        _highlighter.UpdateBracketHighlight(document, 2);

        var (offset1, offset2) = GetHighlightOffsets();

        Assert.That(offset1, Is.EqualTo(1)); // Inner opening
        Assert.That(offset2, Is.EqualTo(7)); // Inner closing
    }

    [Test]
    public void UpdateBracketHighlight_ClosingBracket_MatchesOpening()
    {
        var document = new TextDocument("(test)");
        // Caret right after closing paren: "(test)|"
        _highlighter.UpdateBracketHighlight(document, 6);

        var (offset1, offset2) = GetHighlightOffsets();

        Assert.That(offset1, Is.EqualTo(5)); // Closing paren
        Assert.That(offset2, Is.EqualTo(0)); // Opening paren
    }

    [Test]
    public void UpdateBracketHighlight_NoBracket_NoHighlight()
    {
        var document = new TextDocument("test");
        _highlighter.UpdateBracketHighlight(document, 2);

        var (offset1, offset2) = GetHighlightOffsets();

        Assert.That(offset1, Is.EqualTo(-1));
        Assert.That(offset2, Is.EqualTo(-1));
    }

    [Test]
    public void UpdateBracketHighlight_UnmatchedBracket_NoHighlight()
    {
        var document = new TextDocument("(test");
        _highlighter.UpdateBracketHighlight(document, 1);

        var (offset1, offset2) = GetHighlightOffsets();

        Assert.That(offset2, Is.EqualTo(-1)); // No match found
    }

    [Test]
    public void UpdateBracketHighlight_MixedBrackets_MatchesCorrectType()
    {
        var document = new TextDocument("([{test}])");
        // Caret after curly brace
        _highlighter.UpdateBracketHighlight(document, 3);

        var (offset1, offset2) = GetHighlightOffsets();

        Assert.That(offset1, Is.EqualTo(2)); // Opening curly
        Assert.That(offset2, Is.EqualTo(7)); // Closing curly
    }

    [Test]
    public void UpdateBracketHighlight_BracketAtStart_MatchesCorrectly()
    {
        var document = new TextDocument("(x)");
        // Caret at the opening paren position, check character at caret
        _highlighter.UpdateBracketHighlight(document, 0);

        var (offset1, offset2) = GetHighlightOffsets();

        Assert.That(offset1, Is.EqualTo(0));
        Assert.That(offset2, Is.EqualTo(2));
    }

    [Test]
    public void UpdateBracketHighlight_MultipleCallsResetState()
    {
        var document = new TextDocument("(a)(b)");

        _highlighter.UpdateBracketHighlight(document, 1);
        var (offset1a, offset2a) = GetHighlightOffsets();

        _highlighter.UpdateBracketHighlight(document, 4);
        var (offset1b, offset2b) = GetHighlightOffsets();

        // Second call should update to new positions
        Assert.That(offset1a, Is.EqualTo(0));
        Assert.That(offset1b, Is.EqualTo(3));
    }

    [Test]
    public void UpdateBracketHighlight_BracketInsideString_SkipsString()
    {
        // The highlighter has a simplified string detection
        var document = new TextDocument("(\"test)\")");
        _highlighter.UpdateBracketHighlight(document, 1);

        var (offset1, offset2) = GetHighlightOffsets();

        // Should match the outer parentheses, skipping the one in the string
        Assert.That(offset1, Is.EqualTo(0));
        Assert.That(offset2, Is.EqualTo(8));
    }

    [Test]
    public void UpdateBracketHighlight_EmptyDocument_NoHighlight()
    {
        var document = new TextDocument("");
        _highlighter.UpdateBracketHighlight(document, 0);

        var (offset1, offset2) = GetHighlightOffsets();

        Assert.That(offset1, Is.EqualTo(-1));
        Assert.That(offset2, Is.EqualTo(-1));
    }

    [Test]
    public void UpdateBracketHighlight_OnlyOpeningBracket_NoMatch()
    {
        var document = new TextDocument("(");
        _highlighter.UpdateBracketHighlight(document, 1);

        var (offset1, offset2) = GetHighlightOffsets();

        Assert.That(offset2, Is.EqualTo(-1));
    }

    [Test]
    public void UpdateBracketHighlight_OnlyClosingBracket_NoMatch()
    {
        var document = new TextDocument(")");
        _highlighter.UpdateBracketHighlight(document, 1);

        var (offset1, offset2) = GetHighlightOffsets();

        Assert.That(offset2, Is.EqualTo(-1));
    }

    [Test]
    public void UpdateBracketHighlight_DeeplyNested_MatchesCorrectPair()
    {
        var document = new TextDocument("((((x))))");
        _highlighter.UpdateBracketHighlight(document, 4);

        var (offset1, offset2) = GetHighlightOffsets();

        Assert.That(offset1, Is.EqualTo(3)); // Innermost opening
        Assert.That(offset2, Is.EqualTo(5)); // Innermost closing
    }

    [Test]
    public void UpdateBracketHighlight_WhitespaceAroundBrackets_MatchesCorrectly()
    {
        var document = new TextDocument("(  test  )");
        _highlighter.UpdateBracketHighlight(document, 1);

        var (offset1, offset2) = GetHighlightOffsets();

        Assert.That(offset1, Is.EqualTo(0));
        Assert.That(offset2, Is.EqualTo(9));
    }

    [Test]
    public void UpdateBracketHighlight_NewlinesInside_MatchesCorrectly()
    {
        var document = new TextDocument("(\ntest\n)");
        _highlighter.UpdateBracketHighlight(document, 1);

        var (offset1, offset2) = GetHighlightOffsets();

        Assert.That(offset1, Is.EqualTo(0));
        Assert.That(offset2, Is.EqualTo(7));
    }
}
