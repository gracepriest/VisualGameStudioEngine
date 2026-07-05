using NUnit.Framework;
using VisualGameStudio.Editor.Utils;

namespace VisualGameStudio.Tests.Editor;

[TestFixture]
public class MarkdownLiteTests
{
    [Test]
    public void ParseBlocks_PlainText_SingleTextBlock()
    {
        var blocks = MarkdownLite.ParseBlocks("hello world");

        Assert.That(blocks, Has.Count.EqualTo(1));
        Assert.That(blocks[0].IsCode, Is.False);
        Assert.That(blocks[0].Text, Is.EqualTo("hello world"));
    }

    [Test]
    public void ParseBlocks_FencedCode_BecomesCodeBlock_WithoutFenceMarkers()
    {
        var markdown = "```vb\nDim x As Integer\n```";

        var blocks = MarkdownLite.ParseBlocks(markdown);

        Assert.That(blocks, Has.Count.EqualTo(1));
        Assert.That(blocks[0].IsCode, Is.True);
        Assert.That(blocks[0].Text, Is.EqualTo("Dim x As Integer"));
        Assert.That(blocks[0].Text, Does.Not.Contain("```"));
    }

    [Test]
    public void ParseBlocks_CodeThenText_ProducesBothBlocks()
    {
        var markdown = "```vb\nSub Main()\n```\nEntry point of the program.";

        var blocks = MarkdownLite.ParseBlocks(markdown);

        Assert.That(blocks, Has.Count.EqualTo(2));
        Assert.That(blocks[0].IsCode, Is.True);
        Assert.That(blocks[0].Text, Is.EqualTo("Sub Main()"));
        Assert.That(blocks[1].IsCode, Is.False);
        Assert.That(blocks[1].Text, Does.Contain("Entry point"));
    }

    [Test]
    public void ParseBlocks_MultiLineCode_PreservesLines()
    {
        var markdown = "```\nline1\nline2\n```";

        var blocks = MarkdownLite.ParseBlocks(markdown);

        Assert.That(blocks[0].Text, Is.EqualTo("line1\nline2"));
    }

    [Test]
    public void ParseBlocks_UnterminatedFence_TreatsRestAsCode()
    {
        var markdown = "```vb\nDim x";

        var blocks = MarkdownLite.ParseBlocks(markdown);

        Assert.That(blocks, Has.Count.EqualTo(1));
        Assert.That(blocks[0].IsCode, Is.True);
        Assert.That(blocks[0].Text, Is.EqualTo("Dim x"));
    }

    [Test]
    public void ParseBlocks_EmptyInput_ReturnsEmpty()
    {
        Assert.That(MarkdownLite.ParseBlocks(""), Is.Empty);
    }

    [Test]
    public void ParseInlines_Bold_IsExtracted()
    {
        var segments = MarkdownLite.ParseInlines("this is **bold** text");

        Assert.That(segments, Has.Count.EqualTo(3));
        Assert.That(segments[0].Text, Is.EqualTo("this is "));
        Assert.That(segments[0].IsBold, Is.False);
        Assert.That(segments[1].Text, Is.EqualTo("bold"));
        Assert.That(segments[1].IsBold, Is.True);
        Assert.That(segments[2].Text, Is.EqualTo(" text"));
    }

    [Test]
    public void ParseInlines_InlineCode_IsExtracted()
    {
        var segments = MarkdownLite.ParseInlines("use `Trim()` here");

        Assert.That(segments, Has.Count.EqualTo(3));
        Assert.That(segments[1].Text, Is.EqualTo("Trim()"));
        Assert.That(segments[1].IsCode, Is.True);
    }

    [Test]
    public void ParseInlines_NoMarkup_SingleSegment()
    {
        var segments = MarkdownLite.ParseInlines("plain text");

        Assert.That(segments, Has.Count.EqualTo(1));
        Assert.That(segments[0].Text, Is.EqualTo("plain text"));
        Assert.That(segments[0].IsBold, Is.False);
        Assert.That(segments[0].IsCode, Is.False);
    }

    [Test]
    public void ParseInlines_NoAsterisksLeakIntoOutput()
    {
        var segments = MarkdownLite.ParseInlines("**a** and **b**");

        foreach (var seg in segments)
        {
            Assert.That(seg.Text, Does.Not.Contain("**"));
        }
    }
}
