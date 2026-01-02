namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Service for managing navigation history (back/forward navigation in the editor)
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// Raised when the navigation history changes
    /// </summary>
    event EventHandler<NavigationHistoryChangedEventArgs>? HistoryChanged;

    /// <summary>
    /// Gets whether there is a location to navigate back to
    /// </summary>
    bool CanGoBack { get; }

    /// <summary>
    /// Gets whether there is a location to navigate forward to
    /// </summary>
    bool CanGoForward { get; }

    /// <summary>
    /// Gets the current navigation location
    /// </summary>
    NavigationLocation? CurrentLocation { get; }

    /// <summary>
    /// Gets the back history stack (most recent first)
    /// </summary>
    IReadOnlyList<NavigationLocation> BackHistory { get; }

    /// <summary>
    /// Gets the forward history stack (most recent first)
    /// </summary>
    IReadOnlyList<NavigationLocation> ForwardHistory { get; }

    /// <summary>
    /// Records a new navigation location (e.g., when user navigates to a definition)
    /// </summary>
    void RecordLocation(string filePath, int line, int column, string? symbolName = null);

    /// <summary>
    /// Navigate back to the previous location
    /// </summary>
    /// <returns>The location navigated to, or null if no back history</returns>
    NavigationLocation? GoBack();

    /// <summary>
    /// Navigate forward to the next location
    /// </summary>
    /// <returns>The location navigated to, or null if no forward history</returns>
    NavigationLocation? GoForward();

    /// <summary>
    /// Clear all navigation history
    /// </summary>
    void ClearHistory();
}

/// <summary>
/// Represents a location in the navigation history
/// </summary>
public class NavigationLocation
{
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public string? SymbolName { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;

    public string FileName => Path.GetFileName(FilePath);
    public string DisplayText => string.IsNullOrEmpty(SymbolName)
        ? $"{FileName}:{Line}"
        : $"{SymbolName} - {FileName}:{Line}";
}

/// <summary>
/// Event args for navigation history changes
/// </summary>
public class NavigationHistoryChangedEventArgs : EventArgs
{
    public NavigationChangeType ChangeType { get; set; }
    public NavigationLocation? Location { get; set; }
}

/// <summary>
/// Type of navigation history change
/// </summary>
public enum NavigationChangeType
{
    LocationAdded,
    NavigatedBack,
    NavigatedForward,
    HistoryCleared
}
