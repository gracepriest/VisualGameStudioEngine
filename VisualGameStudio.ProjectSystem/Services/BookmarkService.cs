using System.Collections.Concurrent;
using System.Text.Json;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

public class BookmarkService : IBookmarkService
{
    private readonly ConcurrentDictionary<string, List<Bookmark>> _bookmarks = new();
    private string? _projectDirectory;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

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

        _ = SaveAsync();
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

        _ = SaveAsync();
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

        _ = SaveAsync();
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
                _ = SaveAsync();
            }
        }
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).ToLowerInvariant();
    }

    // --- Per-project persistence (mirrors breakpoints: {projectDir}/.vgs/bookmarks.json) ---

    /// <summary>Sets the project whose bookmarks are persisted; null disables persistence.</summary>
    public void SetProjectDirectory(string? path)
    {
        _projectDirectory = path;
    }

    /// <summary>Loads bookmarks for the current project, replacing the in-memory set.</summary>
    public async Task LoadAsync()
    {
        var dir = _projectDirectory;
        if (string.IsNullOrEmpty(dir)) return;

        var path = Path.Combine(dir, ".vgs", "bookmarks.json");
        if (!File.Exists(path)) return;

        try
        {
            var json = await File.ReadAllTextAsync(path);
            var data = JsonSerializer.Deserialize<BookmarksPersistenceData>(json, s_jsonOptions);

            _bookmarks.Clear();
            if (data?.Bookmarks != null)
            {
                foreach (var item in data.Bookmarks)
                {
                    if (string.IsNullOrEmpty(item.FilePath)) continue;
                    var list = _bookmarks.GetOrAdd(NormalizePath(item.FilePath), _ => new List<Bookmark>());
                    if (list.All(b => b.Line != item.Line))
                    {
                        list.Add(new Bookmark { FilePath = item.FilePath, Line = item.Line, Label = item.Label });
                    }
                }
            }

            // A single event triggers a full panel refresh from GetAllBookmarks().
            BookmarkChanged?.Invoke(this, new BookmarkChangedEventArgs { ChangeType = BookmarkChangeType.Cleared });
        }
        catch
        {
            // Best-effort: a corrupt/unreadable file leaves the in-memory set as-is.
        }
    }

    /// <summary>Persists the current bookmarks for the project (best-effort, fire-and-forget).</summary>
    public async Task SaveAsync()
    {
        var dir = _projectDirectory;
        if (string.IsNullOrEmpty(dir)) return;

        try
        {
            var data = new BookmarksPersistenceData
            {
                Version = 1,
                Bookmarks = GetAllBookmarks()
                    .Select(b => new BookmarkPersistenceItem { FilePath = b.FilePath, Line = b.Line, Label = b.Label })
                    .ToList()
            };

            var vgsDir = Path.Combine(dir, ".vgs");
            Directory.CreateDirectory(vgsDir);
            var json = JsonSerializer.Serialize(data, s_jsonOptions);
            await File.WriteAllTextAsync(Path.Combine(vgsDir, "bookmarks.json"), json);
        }
        catch
        {
            // Persistence is non-critical; ignore IO failures.
        }
    }

    private class BookmarksPersistenceData
    {
        public int Version { get; set; } = 1;
        public List<BookmarkPersistenceItem> Bookmarks { get; set; } = new();
    }

    private class BookmarkPersistenceItem
    {
        public string FilePath { get; set; } = "";
        public int Line { get; set; }
        public string? Label { get; set; }
    }
}
