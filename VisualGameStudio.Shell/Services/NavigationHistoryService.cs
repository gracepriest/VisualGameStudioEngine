namespace VisualGameStudio.Shell.Services;

/// <summary>
/// Tracks cursor position history across files for Back/Forward navigation (Alt+Left/Right).
/// Coalesces nearby positions within 5 lines to avoid polluting the history.
/// </summary>
public class NavigationHistoryService
{
    private readonly List<NavigationPosition> _history = new();
    private int _currentIndex = -1;
    private const int MaxHistory = 50;
    private const int CoalesceLineThreshold = 5;

    /// <summary>
    /// Record a navigation position. Coalesces with the current position if within 5 lines in the same file.
    /// </summary>
    public void RecordPosition(string filePath, int line, int column)
    {
        if (string.IsNullOrEmpty(filePath)) return;

        var newPos = new NavigationPosition(filePath, line, column);

        // Check if we should coalesce with the current position
        if (_currentIndex >= 0 && _currentIndex < _history.Count)
        {
            var current = _history[_currentIndex];
            if (string.Equals(current.FilePath, filePath, StringComparison.OrdinalIgnoreCase) &&
                Math.Abs(current.Line - line) <= CoalesceLineThreshold)
            {
                // Update current position in-place instead of adding a new entry
                _history[_currentIndex] = newPos;
                return;
            }
        }

        // Remove any forward history when we navigate to a new position
        if (_currentIndex < _history.Count - 1)
        {
            _history.RemoveRange(_currentIndex + 1, _history.Count - _currentIndex - 1);
        }

        _history.Add(newPos);
        _currentIndex = _history.Count - 1;

        // Trim old history if we exceed the maximum
        if (_history.Count > MaxHistory)
        {
            var excess = _history.Count - MaxHistory;
            _history.RemoveRange(0, excess);
            _currentIndex -= excess;
            if (_currentIndex < 0) _currentIndex = 0;
        }
    }

    /// <summary>
    /// Navigate back in history. Returns the previous position, or null if at the beginning.
    /// </summary>
    public NavigationPosition? GoBack()
    {
        if (_currentIndex <= 0) return null;
        _currentIndex--;
        return _history[_currentIndex];
    }

    /// <summary>
    /// Navigate forward in history. Returns the next position, or null if at the end.
    /// </summary>
    public NavigationPosition? GoForward()
    {
        if (_currentIndex >= _history.Count - 1) return null;
        _currentIndex++;
        return _history[_currentIndex];
    }

    /// <summary>True when there is a previous position to go back to.</summary>
    public bool CanGoBack => _currentIndex > 0;

    /// <summary>True when there is a next position to go forward to.</summary>
    public bool CanGoForward => _currentIndex < _history.Count - 1;

    /// <summary>
    /// Raised when CanGoBack or CanGoForward changes.
    /// </summary>
    public event EventHandler? StateChanged;

    internal void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// A recorded cursor position for navigation history.
/// </summary>
public record NavigationPosition(string FilePath, int Line, int Column);
