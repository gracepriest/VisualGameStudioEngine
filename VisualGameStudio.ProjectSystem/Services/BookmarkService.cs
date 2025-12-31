using System.Collections.Concurrent;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

public class BookmarkService : IBookmarkService
{
    private readonly ConcurrentDictionary<string, List<Bookmark>> _bookmarks = new();

    public event EventHandler<BookmarkChangedEventArgs>? BookmarkChanged;

    public IReadOnlyList<Bookmark> GetBookmarks(string filePath)
    {
        if (_bookmarks.TryGetValue(NormalizePath(filePath), out var bookmarks))
        {
            return bookmarks.OrderBy(b => b.Line).ToList();
        }
        return Array.Empty<Bookmark>();
    }

    public IReadOnlyList<Bookmark> GetAllBookmarks()
    {
        return _bookmarks.Values
            .SelectMany(b => b)
            .OrderBy(b => b.FilePath)
            .ThenBy(b => b.Line)
            .ToList();
    }

    public void ToggleBookmark(string filePath, int line, string? label = null)
    {
        var normalizedPath = NormalizePath(filePath);
        var bookmarks = _bookmarks.GetOrAdd(normalizedPath, _ => new List<Bookmark>());

        var existing = bookmarks.FirstOrDefault(b => b.Line == line);
        if (existing != null)
        {
            bookmarks.Remove(existing);
            BookmarkChanged?.Invoke(this, new BookmarkChangedEventArgs
            {
                FilePath = filePath,
                Line = line,
                ChangeType = BookmarkChangeType.Removed
            });
        }
        else
        {
            bookmarks.Add(new Bookmark
            {
                FilePath = filePath,
                Line = line,
                Label = label
            });
            BookmarkChanged?.Invoke(this, new BookmarkChangedEventArgs
            {
                FilePath = filePath,
                Line = line,
                ChangeType = BookmarkChangeType.Added
            });
        }
    }

    public void RemoveBookmark(string filePath, int line)
    {
        var normalizedPath = NormalizePath(filePath);
        if (_bookmarks.TryGetValue(normalizedPath, out var bookmarks))
        {
            var bookmark = bookmarks.FirstOrDefault(b => b.Line == line);
            if (bookmark != null)
            {
                bookmarks.Remove(bookmark);
                BookmarkChanged?.Invoke(this, new BookmarkChangedEventArgs
                {
                    FilePath = filePath,
                    Line = line,
                    ChangeType = BookmarkChangeType.Removed
                });
            }
        }
    }

    public void ClearBookmarks(string? filePath = null)
    {
        if (filePath != null)
        {
            var normalizedPath = NormalizePath(filePath);
            _bookmarks.TryRemove(normalizedPath, out _);
            BookmarkChanged?.Invoke(this, new BookmarkChangedEventArgs
            {
                FilePath = filePath,
                ChangeType = BookmarkChangeType.Cleared
            });
        }
        else
        {
            _bookmarks.Clear();
            BookmarkChanged?.Invoke(this, new BookmarkChangedEventArgs
            {
                ChangeType = BookmarkChangeType.Cleared
            });
        }
    }

    public Bookmark? GetNextBookmark(string filePath, int currentLine)
    {
        var normalizedPath = NormalizePath(filePath);
        if (_bookmarks.TryGetValue(normalizedPath, out var bookmarks))
        {
            var next = bookmarks
                .Where(b => b.Line > currentLine)
                .OrderBy(b => b.Line)
                .FirstOrDefault();

            if (next != null) return next;

            // Wrap around to first bookmark
            return bookmarks.OrderBy(b => b.Line).FirstOrDefault();
        }

        // Try to find next in any file
        var allBookmarks = GetAllBookmarks();
        return allBookmarks.FirstOrDefault();
    }

    public Bookmark? GetPreviousBookmark(string filePath, int currentLine)
    {
        var normalizedPath = NormalizePath(filePath);
        if (_bookmarks.TryGetValue(normalizedPath, out var bookmarks))
        {
            var prev = bookmarks
                .Where(b => b.Line < currentLine)
                .OrderByDescending(b => b.Line)
                .FirstOrDefault();

            if (prev != null) return prev;

            // Wrap around to last bookmark
            return bookmarks.OrderByDescending(b => b.Line).FirstOrDefault();
        }

        // Try to find prev in any file
        var allBookmarks = GetAllBookmarks();
        return allBookmarks.LastOrDefault();
    }

    public void UpdateBookmarkLine(string filePath, int oldLine, int newLine)
    {
        var normalizedPath = NormalizePath(filePath);
        if (_bookmarks.TryGetValue(normalizedPath, out var bookmarks))
        {
            var bookmark = bookmarks.FirstOrDefault(b => b.Line == oldLine);
            if (bookmark != null)
            {
                bookmark.Line = newLine;
            }
        }
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).ToLowerInvariant();
    }
}
