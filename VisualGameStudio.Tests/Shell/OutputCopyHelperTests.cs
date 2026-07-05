using NUnit.Framework;
using VisualGameStudio.Core.Utilities;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Tests for the pure line-joining/ordering logic behind the Output panel's
/// Copy (Ctrl+C / context menu) and Copy All features.
/// </summary>
[TestFixture]
public class OutputCopyHelperTests
{
    /// <summary>Stand-in for OutputLine: identity-based, carries display text.</summary>
    private sealed class Line
    {
        public string Text { get; }
        public Line(string text) => Text = text;
    }

    private static string NL => Environment.NewLine;

    [Test]
    public void BuildCopyText_JoinsSelectedLinesInDisplayOrder_NotSelectionOrder()
    {
        var a = new Line("first");
        var b = new Line("second");
        var c = new Line("third");
        var all = new[] { a, b, c };

        // Selection order is reversed (as a ListBox reports click order),
        // but the copied text must follow display order.
        var selected = new[] { c, a };

        var result = OutputCopyHelper.BuildCopyText(all, selected, l => l.Text);

        Assert.That(result, Is.EqualTo("first" + NL + "third"));
    }

    [Test]
    public void BuildCopyText_SingleSelectedLine_ReturnsTextWithoutTrailingNewline()
    {
        var a = new Line("only line");
        var all = new[] { a };

        var result = OutputCopyHelper.BuildCopyText(all, new[] { a }, l => l.Text);

        Assert.That(result, Is.EqualTo("only line"));
    }

    [Test]
    public void BuildCopyText_EmptySelection_ReturnsEmptyString()
    {
        var all = new[] { new Line("x"), new Line("y") };

        var result = OutputCopyHelper.BuildCopyText(all, Array.Empty<Line>(), l => l.Text);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void BuildCopyText_NullSelection_ReturnsEmptyString()
    {
        var all = new[] { new Line("x") };

        var result = OutputCopyHelper.BuildCopyText(all, null, l => l.Text);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void BuildCopyText_NullDisplayList_ReturnsEmptyString()
    {
        var a = new Line("x");

        var result = OutputCopyHelper.BuildCopyText(null, new[] { a }, l => l.Text);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void BuildCopyText_SelectedItemNotInDisplayList_IsIgnored()
    {
        var a = new Line("kept");
        var stranger = new Line("not in list");
        var all = new[] { a };

        var result = OutputCopyHelper.BuildCopyText(all, new[] { a, stranger }, l => l.Text);

        Assert.That(result, Is.EqualTo("kept"));
    }

    [Test]
    public void BuildCopyText_DuplicateTextOnDistinctLines_MatchesByIdentityNotText()
    {
        // Two distinct lines with identical text (very common in build output).
        var first = new Line("warning CS0168: unused variable");
        var middle = new Line("something else");
        var second = new Line("warning CS0168: unused variable");
        var all = new[] { first, middle, second };

        // Only the SECOND duplicate is selected: exactly one line must be copied.
        var result = OutputCopyHelper.BuildCopyText(all, new[] { second }, l => l.Text);

        Assert.That(result, Is.EqualTo("warning CS0168: unused variable"));

        // Selecting both duplicates copies both, once each, in order.
        var both = OutputCopyHelper.BuildCopyText(all, new[] { second, first }, l => l.Text);
        Assert.That(both, Is.EqualTo(
            "warning CS0168: unused variable" + NL + "warning CS0168: unused variable"));
    }

    [Test]
    public void BuildCopyText_DuplicateEntriesInSelection_CopiedOnlyOnce()
    {
        var a = new Line("once");
        var all = new[] { a };

        var result = OutputCopyHelper.BuildCopyText(all, new[] { a, a, a }, l => l.Text);

        Assert.That(result, Is.EqualTo("once"));
    }

    [Test]
    public void BuildCopyText_NullTextFromSelector_TreatedAsEmptyLine()
    {
        var a = new Line("start");
        var b = new Line("end");
        var all = new[] { a, b };

        var result = OutputCopyHelper.BuildCopyText(all, all, l => l.Text == "start" ? null : l.Text);

        Assert.That(result, Is.EqualTo("" + NL + "end"));
    }

    [Test]
    public void BuildCopyAllText_JoinsEveryLineInOrder()
    {
        var all = new[] { new Line("one"), new Line("two"), new Line("three") };

        var result = OutputCopyHelper.BuildCopyAllText(all, l => l.Text);

        Assert.That(result, Is.EqualTo("one" + NL + "two" + NL + "three"));
    }

    [Test]
    public void BuildCopyAllText_EmptyList_ReturnsEmptyString()
    {
        var result = OutputCopyHelper.BuildCopyAllText(Array.Empty<Line>(), l => l.Text);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void BuildCopyAllText_NullList_ReturnsEmptyString()
    {
        var result = OutputCopyHelper.BuildCopyAllText<Line>(null, l => l.Text);

        Assert.That(result, Is.Empty);
    }
}
