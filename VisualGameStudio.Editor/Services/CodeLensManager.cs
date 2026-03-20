using VisualGameStudio.Editor.TextMarkers;

namespace VisualGameStudio.Editor.Services;

/// <summary>
/// Manages CodeLens data for a code editor instance.
/// Stores CodeLens items grouped by line and notifies listeners when data changes.
/// </summary>
public class CodeLensManager
{
    private readonly Dictionary<int, List<CodeLensItem>> _lensByLine = new();
    private readonly object _lock = new();

    /// <summary>
    /// Raised when the CodeLens data changes (items added, removed, or cleared).
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets CodeLens items for a specific 1-based line number.
    /// Returns an empty list if the line has no CodeLens.
    /// </summary>
    public IReadOnlyList<CodeLensItem> GetLensesForLine(int lineNumber)
    {
        lock (_lock)
        {
            return _lensByLine.TryGetValue(lineNumber, out var list)
                ? list.AsReadOnly()
                : Array.Empty<CodeLensItem>();
        }
    }

    /// <summary>
    /// Gets all line numbers that have CodeLens items.
    /// </summary>
    public IReadOnlyCollection<int> LinesWithLenses
    {
        get
        {
            lock (_lock)
            {
                return _lensByLine.Keys.ToList().AsReadOnly();
            }
        }
    }

    /// <summary>
    /// Returns true if any CodeLens items are present.
    /// </summary>
    public bool HasLenses
    {
        get
        {
            lock (_lock)
            {
                return _lensByLine.Count > 0;
            }
        }
    }

    /// <summary>
    /// Replaces all CodeLens items with the given collection.
    /// Items are grouped by their Line property.
    /// </summary>
    public void SetCodeLens(IEnumerable<CodeLensItem> items)
    {
        lock (_lock)
        {
            _lensByLine.Clear();
            foreach (var item in items)
            {
                if (!_lensByLine.TryGetValue(item.Line, out var list))
                {
                    list = new List<CodeLensItem>();
                    _lensByLine[item.Line] = list;
                }
                list.Add(item);
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Removes all CodeLens items.
    /// </summary>
    public void ClearCodeLens()
    {
        lock (_lock)
        {
            _lensByLine.Clear();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Returns true if a given 1-based line number has CodeLens items.
    /// </summary>
    public bool HasLensesForLine(int lineNumber)
    {
        lock (_lock)
        {
            return _lensByLine.ContainsKey(lineNumber);
        }
    }
}
