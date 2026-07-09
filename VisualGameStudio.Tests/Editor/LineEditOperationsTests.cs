using AvaloniaEdit.Document;
using NUnit.Framework;
using VisualGameStudio.Editor.Controls;

namespace VisualGameStudio.Tests.Editor;

[TestFixture]
public class LineEditOperationsTests
{
    private static TextDocument Doc(string text) => new TextDocument(text);

    [Test]
    public void ToggleLineComment_UncommentedLines_CommentsThem()
    {
        var doc = Doc("Dim x As Integer\nx = 1");
        LineEditOperations.ToggleLineComment(doc, 1, 2);
        Assert.That(doc.Text, Is.EqualTo("' Dim x As Integer\n' x = 1"));
    }

    [Test]
    public void ToggleLineComment_CommentedLines_UncommentsThem()
    {
        var doc = Doc("' Dim x As Integer\n' x = 1");
        LineEditOperations.ToggleLineComment(doc, 1, 2);
        Assert.That(doc.Text, Is.EqualTo("Dim x As Integer\nx = 1"));
    }

    [Test]
    public void ToggleLineComment_BlockWithBlankLine_RoundTrips()
    {
        // Regression: a blank line inside the block used to make "all commented?" false, so
        // the block could never be uncommented — toggling re-commented it instead.
        var doc = Doc("a = 1\n\nb = 2");
        LineEditOperations.ToggleLineComment(doc, 1, 3); // comment
        Assert.That(doc.Text, Is.EqualTo("' a = 1\n\n' b = 2"));
        LineEditOperations.ToggleLineComment(doc, 1, 3); // uncomment
        Assert.That(doc.Text, Is.EqualTo("a = 1\n\nb = 2"));
    }

    [Test]
    public void ToggleLineComment_RemCommentedLine_Uncomments()
    {
        // Regression: REM-prefixed lines counted as "commented" but were never stripped.
        var doc = Doc("REM legacy comment");
        LineEditOperations.ToggleLineComment(doc, 1, 1);
        Assert.That(doc.Text, Is.EqualTo("legacy comment"));
    }

    [Test]
    public void ToggleLineComment_PreservesIndentation()
    {
        var doc = Doc("    x = 1");
        LineEditOperations.ToggleLineComment(doc, 1, 1);
        Assert.That(doc.Text, Is.EqualTo("    ' x = 1"));
    }

    [Test]
    public void DeleteLineRange_MiddleLines_RemovesThemWithNewlines()
    {
        var doc = Doc("one\ntwo\nthree\nfour");
        LineEditOperations.DeleteLineRange(doc, 2, 3);
        Assert.That(doc.Text, Is.EqualTo("one\nfour"));
    }

    [Test]
    public void DeleteLineRange_SingleLine_RemovesIt()
    {
        var doc = Doc("one\ntwo\nthree");
        LineEditOperations.DeleteLineRange(doc, 2, 2);
        Assert.That(doc.Text, Is.EqualTo("one\nthree"));
    }

    [Test]
    public void DeleteLineRange_ThroughLastLine_RemovesPrecedingNewline()
    {
        var doc = Doc("one\ntwo\nthree");
        LineEditOperations.DeleteLineRange(doc, 2, 3);
        Assert.That(doc.Text, Is.EqualTo("one"));
    }

    [Test]
    public void DeleteLineRange_AllLines_EmptiesDocument()
    {
        var doc = Doc("one\ntwo");
        LineEditOperations.DeleteLineRange(doc, 1, 2);
        Assert.That(doc.Text, Is.EqualTo(""));
    }
}
