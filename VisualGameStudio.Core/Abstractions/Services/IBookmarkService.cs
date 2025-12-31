namespace VisualGameStudio.Core.Abstractions.Services;

public interface IBookmarkService
{
    event EventHandler<BookmarkChangedEventArgs>? BookmarkChanged;

    IReadOnlyList<Bookmark> GetBookmarks(string filePath);
    IReadOnlyList<Bookmark> GetAllBookmarks();
    void ToggleBookmark(string filePath, int line, string? label = null);
    void RemoveBookmark(string filePath, int line);
    void ClearBookmarks(string? filePath = null);
    Bookmark? GetNextBookmark(string filePath, int currentLine);
    Bookmark? GetPreviousBookmark(string filePath, int currentLine);
    void UpdateBookmarkLine(string filePath, int oldLine, int newLine);
}

public class Bookmark
{
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
    public string? Label { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string FileName => Path.GetFileName(FilePath);
    public string DisplayText => string.IsNullOrEmpty(Label) ? $"Line {Line}" : Label;
}

public class BookmarkChangedEventArgs : EventArgs
{
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
    public BookmarkChangeType ChangeType { get; set; }
}

public enum BookmarkChangeType
{
    Added,
    Removed,
    Cleared
}
