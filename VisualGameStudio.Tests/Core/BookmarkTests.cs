using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Tests.Core;

[TestFixture]
public class BookmarkTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
    {
        var bookmark = new Bookmark();

        Assert.That(bookmark.FilePath, Is.EqualTo(""));
        Assert.That(bookmark.Line, Is.EqualTo(0));
        Assert.That(bookmark.Label, Is.Null);
        Assert.That(bookmark.CreatedAt, Is.Not.EqualTo(default(DateTime)));
    }

    [Test]
    public void FilePath_CanBeSetAndRetrieved()
    {
        var bookmark = new Bookmark { FilePath = @"C:\Project\Source.bas" };

        Assert.That(bookmark.FilePath, Is.EqualTo(@"C:\Project\Source.bas"));
    }

    [Test]
    public void Line_CanBeSetAndRetrieved()
    {
        var bookmark = new Bookmark { Line = 42 };

        Assert.That(bookmark.Line, Is.EqualTo(42));
    }

    [Test]
    public void Label_CanBeSetAndRetrieved()
    {
        var bookmark = new Bookmark { Label = "Important section" };

        Assert.That(bookmark.Label, Is.EqualTo("Important section"));
    }

    [Test]
    public void CreatedAt_CanBeSetAndRetrieved()
    {
        var time = new DateTime(2024, 1, 15, 10, 30, 0);
        var bookmark = new Bookmark { CreatedAt = time };

        Assert.That(bookmark.CreatedAt, Is.EqualTo(time));
    }

    [Test]
    public void FileName_ExtractsFileNameFromPath()
    {
        var bookmark = new Bookmark { FilePath = @"C:\Very\Long\Path\MyFile.bas" };

        Assert.That(bookmark.FileName, Is.EqualTo("MyFile.bas"));
    }

    [Test]
    public void FileName_WithSimplePath_ReturnsFileName()
    {
        var bookmark = new Bookmark { FilePath = "MyFile.bas" };

        Assert.That(bookmark.FileName, Is.EqualTo("MyFile.bas"));
    }

    [Test]
    public void FileName_WithEmptyPath_ReturnsEmpty()
    {
        var bookmark = new Bookmark { FilePath = "" };

        Assert.That(bookmark.FileName, Is.EqualTo(""));
    }

    [Test]
    public void DisplayText_WithNoLabel_ReturnsLineNumber()
    {
        var bookmark = new Bookmark { Line = 42 };

        Assert.That(bookmark.DisplayText, Is.EqualTo("Line 42"));
    }

    [Test]
    public void DisplayText_WithLabel_ReturnsLabel()
    {
        var bookmark = new Bookmark
        {
            Line = 42,
            Label = "Important section"
        };

        Assert.That(bookmark.DisplayText, Is.EqualTo("Important section"));
    }

    [Test]
    public void DisplayText_WithEmptyLabel_ReturnsLineNumber()
    {
        var bookmark = new Bookmark
        {
            Line = 10,
            Label = ""
        };

        Assert.That(bookmark.DisplayText, Is.EqualTo("Line 10"));
    }

    [Test]
    public void DisplayText_WithWhitespaceLabel_ReturnsWhitespace()
    {
        // The actual implementation uses IsNullOrEmpty, not IsNullOrWhiteSpace
        // so whitespace-only labels are returned as-is
        var bookmark = new Bookmark
        {
            Line = 10,
            Label = "   "
        };

        Assert.That(bookmark.DisplayText, Is.EqualTo("   "));
    }

    [Test]
    public void AllProperties_CanBeSetTogether()
    {
        var time = DateTime.Now;
        var bookmark = new Bookmark
        {
            FilePath = @"C:\Test\File.bas",
            Line = 100,
            Label = "Test Label",
            CreatedAt = time
        };

        Assert.That(bookmark.FilePath, Is.EqualTo(@"C:\Test\File.bas"));
        Assert.That(bookmark.Line, Is.EqualTo(100));
        Assert.That(bookmark.Label, Is.EqualTo("Test Label"));
        Assert.That(bookmark.CreatedAt, Is.EqualTo(time));
        Assert.That(bookmark.FileName, Is.EqualTo("File.bas"));
        Assert.That(bookmark.DisplayText, Is.EqualTo("Test Label"));
    }
}

[TestFixture]
public class BookmarkChangedEventArgsTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
    {
        var args = new BookmarkChangedEventArgs();

        Assert.That(args.FilePath, Is.EqualTo(""));
        Assert.That(args.Line, Is.EqualTo(0));
        Assert.That(args.ChangeType, Is.EqualTo(BookmarkChangeType.Added));
    }

    [Test]
    public void FilePath_CanBeSetAndRetrieved()
    {
        var args = new BookmarkChangedEventArgs { FilePath = @"C:\Project\Source.bas" };

        Assert.That(args.FilePath, Is.EqualTo(@"C:\Project\Source.bas"));
    }

    [Test]
    public void Line_CanBeSetAndRetrieved()
    {
        var args = new BookmarkChangedEventArgs { Line = 42 };

        Assert.That(args.Line, Is.EqualTo(42));
    }

    [Test]
    public void ChangeType_Added()
    {
        var args = new BookmarkChangedEventArgs { ChangeType = BookmarkChangeType.Added };

        Assert.That(args.ChangeType, Is.EqualTo(BookmarkChangeType.Added));
    }

    [Test]
    public void ChangeType_Removed()
    {
        var args = new BookmarkChangedEventArgs { ChangeType = BookmarkChangeType.Removed };

        Assert.That(args.ChangeType, Is.EqualTo(BookmarkChangeType.Removed));
    }

    [Test]
    public void ChangeType_Cleared()
    {
        var args = new BookmarkChangedEventArgs { ChangeType = BookmarkChangeType.Cleared };

        Assert.That(args.ChangeType, Is.EqualTo(BookmarkChangeType.Cleared));
    }

    [Test]
    public void InheritsFromEventArgs()
    {
        var args = new BookmarkChangedEventArgs();

        Assert.That(args, Is.InstanceOf<EventArgs>());
    }
}

[TestFixture]
public class BookmarkChangeTypeTests
{
    [Test]
    public void Added_HasValue0()
    {
        Assert.That((int)BookmarkChangeType.Added, Is.EqualTo(0));
    }

    [Test]
    public void Removed_HasValue1()
    {
        Assert.That((int)BookmarkChangeType.Removed, Is.EqualTo(1));
    }

    [Test]
    public void Cleared_HasValue2()
    {
        Assert.That((int)BookmarkChangeType.Cleared, Is.EqualTo(2));
    }

    [Test]
    public void HasThreeValues()
    {
        var values = Enum.GetValues<BookmarkChangeType>();
        Assert.That(values, Has.Length.EqualTo(3));
    }
}
