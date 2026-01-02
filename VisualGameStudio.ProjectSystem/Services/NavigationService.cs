using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Service for managing navigation history (back/forward navigation in the editor)
/// </summary>
public class NavigationService : INavigationService
{
    private readonly Stack<NavigationLocation> _backStack = new();
    private readonly Stack<NavigationLocation> _forwardStack = new();
    private NavigationLocation? _currentLocation;
    private const int MaxHistorySize = 100;

    public event EventHandler<NavigationHistoryChangedEventArgs>? HistoryChanged;

    public bool CanGoBack => _backStack.Count > 0;
    public bool CanGoForward => _forwardStack.Count > 0;
    public NavigationLocation? CurrentLocation => _currentLocation;

    public IReadOnlyList<NavigationLocation> BackHistory => _backStack.ToList();
    public IReadOnlyList<NavigationLocation> ForwardHistory => _forwardStack.ToList();

    public void RecordLocation(string filePath, int line, int column, string? symbolName = null)
    {
        // Don't record if it's the same location
        if (_currentLocation != null &&
            _currentLocation.FilePath == filePath &&
            _currentLocation.Line == line)
        {
            return;
        }

        // Push current location to back stack if we have one
        if (_currentLocation != null)
        {
            _backStack.Push(_currentLocation);

            // Trim history if it exceeds max size
            if (_backStack.Count > MaxHistorySize)
            {
                var items = _backStack.ToList();
                _backStack.Clear();
                foreach (var item in items.Take(MaxHistorySize).Reverse())
                {
                    _backStack.Push(item);
                }
            }
        }

        // Clear forward stack when recording a new location
        _forwardStack.Clear();

        // Set new current location
        _currentLocation = new NavigationLocation
        {
            FilePath = filePath,
            Line = line,
            Column = column,
            SymbolName = symbolName,
            Timestamp = DateTime.Now
        };

        RaiseHistoryChanged(NavigationChangeType.LocationAdded, _currentLocation);
    }

    public NavigationLocation? GoBack()
    {
        if (!CanGoBack)
            return null;

        // Push current location to forward stack
        if (_currentLocation != null)
        {
            _forwardStack.Push(_currentLocation);
        }

        // Pop from back stack
        _currentLocation = _backStack.Pop();

        RaiseHistoryChanged(NavigationChangeType.NavigatedBack, _currentLocation);

        return _currentLocation;
    }

    public NavigationLocation? GoForward()
    {
        if (!CanGoForward)
            return null;

        // Push current location to back stack
        if (_currentLocation != null)
        {
            _backStack.Push(_currentLocation);
        }

        // Pop from forward stack
        _currentLocation = _forwardStack.Pop();

        RaiseHistoryChanged(NavigationChangeType.NavigatedForward, _currentLocation);

        return _currentLocation;
    }

    public void ClearHistory()
    {
        _backStack.Clear();
        _forwardStack.Clear();
        _currentLocation = null;

        RaiseHistoryChanged(NavigationChangeType.HistoryCleared, null);
    }

    private void RaiseHistoryChanged(NavigationChangeType changeType, NavigationLocation? location)
    {
        HistoryChanged?.Invoke(this, new NavigationHistoryChangedEventArgs
        {
            ChangeType = changeType,
            Location = location
        });
    }
}
