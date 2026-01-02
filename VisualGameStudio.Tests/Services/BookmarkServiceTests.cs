using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

[TestFixture]
public class BookmarkServiceTests
{
    private BookmarkService _service = null!;
    private string _testFilePath = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new BookmarkService();
        _testFilePath = Path.GetFullPath("test.bas");
    }

    [Test]
    public void GetBookmarks_NoBookmarks_ReturnsEmpty()
    {
        var bookmarks = _service.GetBookmarks(_testFilePath);

        Assert.That(bookmarks, Is.Empty);
    }

    [Test]
    public void ToggleBookmark_AddsBookmark()
    {
        _service.ToggleBookmark(_testFilePath, 10);

        var bookmarks = _service.GetBookmarks(_testFilePath);
        Assert.That(bookmarks, Has.Count.EqualTo(1));
        Assert.That(bookmarks[0].Line, Is.EqualTo(10));
    }

    [Test]
    public void ToggleBookmark_Twice_RemovesBookmark()
    {
        _service.ToggleBookmark(_testFilePath, 10);
        _service.ToggleBookmark(_testFilePath, 10);

        var bookmarks = _service.GetBookmarks(_testFilePath);
        Assert.That(bookmarks, Is.Empty);
    }

    [Test]
    public void ToggleBookmark_WithLabel_SetsLabel()
    {
        _service.ToggleBookmark(_testFilePath, 10, "Important");

        var bookmarks = _service.GetBookmarks(_testFilePath);
        Assert.That(bookmarks[0].Label, Is.EqualTo("Important"));
    }

    [Test]
    public void ToggleBookmark_WithoutLabel_LabelIsNull()
    {
        _service.ToggleBookmark(_testFilePath, 10);

        var bookmarks = _service.GetBookmarks(_testFilePath);
        Assert.That(bookmarks[0].Label, Is.Null);
    }

    [Test]
    public void ToggleBookmark_SetsFilePath()
    {
        _service.ToggleBookmark(_testFilePath, 10);

        var bookmarks = _service.GetBookmarks(_testFilePath);
        Assert.That(bookmarks[0].FilePath, Is.EqualTo(_testFilePath));
    }

    [Test]
    public void ToggleBookmark_MultipleLines_AddsAll()
    {
        _service.ToggleBookmark(_testFilePath, 10);
        _service.ToggleBookmark(_testFilePath, 20);
        _service.ToggleBookmark(_testFilePath, 30);

        var bookmarks = _service.GetBookmarks(_testFilePath);
        Assert.That(bookmarks, Has.Count.EqualTo(3));
    }

    [Test]
    public void GetBookmarks_ReturnsOrderedByLine()
    {
        _service.ToggleBookmark(_testFilePath, 30);
        _service.ToggleBookmark(_testFilePath, 10);
        _service.ToggleBookmark(_testFilePath, 20);

        var bookmarks = _service.GetBookmarks(_testFilePath);
        Assert.That(bookmarks[0].Line, Is.EqualTo(10));
        Assert.That(bookmarks[1].Line, Is.EqualTo(20));
        Assert.That(bookmarks[2].Line, Is.EqualTo(30));
    }

    [Test]
    public void GetAllBookmarks_ReturnsFromAllFiles()
    {
        var file1 = Path.GetFullPath("file1.bas");
        var file2 = Path.GetFullPath("file2.bas");

        _service.ToggleBookmark(file1, 10);
        _service.ToggleBookmark(file2, 20);

        var allBookmarks = _service.GetAllBookmarks();
        Assert.That(allBookmarks, Has.Count.EqualTo(2));
    }

    [Test]
    public void GetAllBookmarks_ReturnsOrderedByFilePathThenLine()
    {
        var fileA = Path.GetFullPath("aaa.bas");
        var fileB = Path.GetFullPath("bbb.bas");

        _service.ToggleBookmark(fileB, 10);
        _service.ToggleBookmark(fileA, 30);
        _service.ToggleBookmark(fileA, 10);

        var allBookmarks = _service.GetAllBookmarks();
        Assert.That(allBookmarks[0].FilePath, Does.Contain("aaa"));
        Assert.That(allBookmarks[0].Line, Is.EqualTo(10));
        Assert.That(allBookmarks[1].FilePath, Does.Contain("aaa"));
        Assert.That(allBookmarks[1].Line, Is.EqualTo(30));
        Assert.That(allBookmarks[2].FilePath, Does.Contain("bbb"));
    }

    [Test]
    public void RemoveBookmark_RemovesSpecificBookmark()
    {
        _service.ToggleBookmark(_testFilePath, 10);
        _service.ToggleBookmark(_testFilePath, 20);

        _service.RemoveBookmark(_testFilePath, 10);

        var bookmarks = _service.GetBookmarks(_testFilePath);
        Assert.That(bookmarks, Has.Count.EqualTo(1));
        Assert.That(bookmarks[0].Line, Is.EqualTo(20));
    }

    [Test]
    public void RemoveBookmark_NonExistent_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => _service.RemoveBookmark(_testFilePath, 999));
    }

    [Test]
    public void ClearBookmarks_WithFilePath_ClearsOnlyThatFile()
    {
        var file1 = Path.GetFullPath("file1.bas");
        var file2 = Path.GetFullPath("file2.bas");

        _service.ToggleBookmark(file1, 10);
        _service.ToggleBookmark(file2, 20);

        _service.ClearBookmarks(file1);

        Assert.That(_service.GetBookmarks(file1), Is.Empty);
        Assert.That(_service.GetBookmarks(file2), Has.Count.EqualTo(1));
    }

    [Test]
    public void ClearBookmarks_WithoutFilePath_ClearsAll()
    {
        var file1 = Path.GetFullPath("file1.bas");
        var file2 = Path.GetFullPath("file2.bas");

        _service.ToggleBookmark(file1, 10);
        _service.ToggleBookmark(file2, 20);

        _service.ClearBookmarks();

        Assert.That(_service.GetAllBookmarks(), Is.Empty);
    }

    [Test]
    public void GetNextBookmark_ReturnsNextInFile()
    {
        _service.ToggleBookmark(_testFilePath, 10);
        _service.ToggleBookmark(_testFilePath, 20);
        _service.ToggleBookmark(_testFilePath, 30);

        var next = _service.GetNextBookmark(_testFilePath, 15);

        Assert.That(next, Is.Not.Null);
        Assert.That(next!.Line, Is.EqualTo(20));
    }

    [Test]
    public void GetNextBookmark_WrapsAround()
    {
        _service.ToggleBookmark(_testFilePath, 10);
        _service.ToggleBookmark(_testFilePath, 20);

        var next = _service.GetNextBookmark(_testFilePath, 25);

        Assert.That(next, Is.Not.Null);
        Assert.That(next!.Line, Is.EqualTo(10));
    }

    [Test]
    public void GetNextBookmark_NoBookmarks_ReturnsNull()
    {
        var next = _service.GetNextBookmark(_testFilePath, 10);

        Assert.That(next, Is.Null);
    }

    [Test]
    public void GetPreviousBookmark_ReturnsPreviousInFile()
    {
        _service.ToggleBookmark(_testFilePath, 10);
        _service.ToggleBookmark(_testFilePath, 20);
        _service.ToggleBookmark(_testFilePath, 30);

        var prev = _service.GetPreviousBookmark(_testFilePath, 25);

        Assert.That(prev, Is.Not.Null);
        Assert.That(prev!.Line, Is.EqualTo(20));
    }

    [Test]
    public void GetPreviousBookmark_WrapsAround()
    {
        _service.ToggleBookmark(_testFilePath, 10);
        _service.ToggleBookmark(_testFilePath, 20);

        var prev = _service.GetPreviousBookmark(_testFilePath, 5);

        Assert.That(prev, Is.Not.Null);
        Assert.That(prev!.Line, Is.EqualTo(20));
    }

    [Test]
    public void GetPreviousBookmark_NoBookmarks_ReturnsNull()
    {
        var prev = _service.GetPreviousBookmark(_testFilePath, 10);

        Assert.That(prev, Is.Null);
    }

    [Test]
    public void UpdateBookmarkLine_UpdatesLine()
    {
        _service.ToggleBookmark(_testFilePath, 10);

        _service.UpdateBookmarkLine(_testFilePath, 10, 15);

        var bookmarks = _service.GetBookmarks(_testFilePath);
        Assert.That(bookmarks[0].Line, Is.EqualTo(15));
    }

    [Test]
    public void UpdateBookmarkLine_NonExistentLine_DoesNothing()
    {
        _service.ToggleBookmark(_testFilePath, 10);

        _service.UpdateBookmarkLine(_testFilePath, 999, 15);

        var bookmarks = _service.GetBookmarks(_testFilePath);
        Assert.That(bookmarks[0].Line, Is.EqualTo(10));
    }

    [Test]
    public void BookmarkChanged_AddedEvent_IsFired()
    {
        BookmarkChangedEventArgs? receivedArgs = null;
        _service.BookmarkChanged += (s, e) => receivedArgs = e;

        _service.ToggleBookmark(_testFilePath, 10);

        Assert.That(receivedArgs, Is.Not.Null);
        Assert.That(receivedArgs!.ChangeType, Is.EqualTo(BookmarkChangeType.Added));
        Assert.That(receivedArgs.Line, Is.EqualTo(10));
    }

    [Test]
    public void BookmarkChanged_RemovedEvent_IsFired()
    {
        _service.ToggleBookmark(_testFilePath, 10);

        BookmarkChangedEventArgs? receivedArgs = null;
        _service.BookmarkChanged += (s, e) => receivedArgs = e;

        _service.ToggleBookmark(_testFilePath, 10);

        Assert.That(receivedArgs, Is.Not.Null);
        Assert.That(receivedArgs!.ChangeType, Is.EqualTo(BookmarkChangeType.Removed));
    }

    [Test]
    public void BookmarkChanged_ClearedEvent_IsFired()
    {
        _service.ToggleBookmark(_testFilePath, 10);

        BookmarkChangedEventArgs? receivedArgs = null;
        _service.BookmarkChanged += (s, e) => receivedArgs = e;

        _service.ClearBookmarks(_testFilePath);

        Assert.That(receivedArgs, Is.Not.Null);
        Assert.That(receivedArgs!.ChangeType, Is.EqualTo(BookmarkChangeType.Cleared));
    }

    [Test]
    public void RemoveBookmark_FiresRemovedEvent()
    {
        _service.ToggleBookmark(_testFilePath, 10);

        BookmarkChangedEventArgs? receivedArgs = null;
        _service.BookmarkChanged += (s, e) => receivedArgs = e;

        _service.RemoveBookmark(_testFilePath, 10);

        Assert.That(receivedArgs, Is.Not.Null);
        Assert.That(receivedArgs!.ChangeType, Is.EqualTo(BookmarkChangeType.Removed));
    }

    [Test]
    public void PathNormalization_DifferentCases_TreatedAsSame()
    {
        var path1 = Path.GetFullPath("Test.bas");
        var path2 = path1.ToLower();

        _service.ToggleBookmark(path1, 10);
        var bookmarks = _service.GetBookmarks(path2);

        Assert.That(bookmarks, Has.Count.EqualTo(1));
    }
}

[TestFixture]
public class BookmarkModelTests
{
    [Test]
    public void Bookmark_DefaultValues()
    {
        var bookmark = new Bookmark();

        Assert.That(bookmark.FilePath, Is.EqualTo(""));
        Assert.That(bookmark.Line, Is.EqualTo(0));
        Assert.That(bookmark.Label, Is.Null);
        Assert.That(bookmark.CreatedAt, Is.Not.EqualTo(default(DateTime)));
    }

    [Test]
    public void Bookmark_FileName_ReturnsFileNameFromPath()
    {
        var bookmark = new Bookmark { FilePath = @"C:\Projects\Test\main.bas" };

        Assert.That(bookmark.FileName, Is.EqualTo("main.bas"));
    }

    [Test]
    public void Bookmark_DisplayText_WithLabel_ReturnsLabel()
    {
        var bookmark = new Bookmark { Label = "Important section" };

        Assert.That(bookmark.DisplayText, Is.EqualTo("Important section"));
    }

    [Test]
    public void Bookmark_DisplayText_WithoutLabel_ReturnsLineNumber()
    {
        var bookmark = new Bookmark { Line = 42 };

        Assert.That(bookmark.DisplayText, Is.EqualTo("Line 42"));
    }

    [Test]
    public void Bookmark_DisplayText_EmptyLabel_ReturnsLineNumber()
    {
        var bookmark = new Bookmark { Line = 42, Label = "" };

        Assert.That(bookmark.DisplayText, Is.EqualTo("Line 42"));
    }
}

[TestFixture]
public class BookmarkChangedEventArgsTests
{
    [Test]
    public void DefaultValues()
    {
        var args = new BookmarkChangedEventArgs();

        Assert.That(args.FilePath, Is.EqualTo(""));
        Assert.That(args.Line, Is.EqualTo(0));
        Assert.That(args.ChangeType, Is.EqualTo(BookmarkChangeType.Added));
    }

    [Test]
    public void AllProperties_CanBeSet()
    {
        var args = new BookmarkChangedEventArgs
        {
            FilePath = "/path/to/file.bas",
            Line = 42,
            ChangeType = BookmarkChangeType.Removed
        };

        Assert.That(args.FilePath, Is.EqualTo("/path/to/file.bas"));
        Assert.That(args.Line, Is.EqualTo(42));
        Assert.That(args.ChangeType, Is.EqualTo(BookmarkChangeType.Removed));
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
    public void Added_HasCorrectValue()
    {
        Assert.That((int)BookmarkChangeType.Added, Is.EqualTo(0));
    }

    [Test]
    public void Removed_HasCorrectValue()
    {
        Assert.That((int)BookmarkChangeType.Removed, Is.EqualTo(1));
    }

    [Test]
    public void Cleared_HasCorrectValue()
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
