using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

[TestFixture]
public class NavigationServiceTests
{
    private NavigationService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new NavigationService();
    }

    #region Initial State Tests

    [Test]
    public void InitialState_CannotGoBack()
    {
        Assert.That(_service.CanGoBack, Is.False);
    }

    [Test]
    public void InitialState_CannotGoForward()
    {
        Assert.That(_service.CanGoForward, Is.False);
    }

    [Test]
    public void InitialState_CurrentLocationIsNull()
    {
        Assert.That(_service.CurrentLocation, Is.Null);
    }

    [Test]
    public void InitialState_BackHistoryIsEmpty()
    {
        Assert.That(_service.BackHistory, Is.Empty);
    }

    [Test]
    public void InitialState_ForwardHistoryIsEmpty()
    {
        Assert.That(_service.ForwardHistory, Is.Empty);
    }

    #endregion

    #region RecordLocation Tests

    [Test]
    public void RecordLocation_SetsCurrentLocation()
    {
        _service.RecordLocation("/path/to/file.bas", 10, 5, "MyFunction");

        Assert.That(_service.CurrentLocation, Is.Not.Null);
        Assert.That(_service.CurrentLocation!.FilePath, Is.EqualTo("/path/to/file.bas"));
        Assert.That(_service.CurrentLocation.Line, Is.EqualTo(10));
        Assert.That(_service.CurrentLocation.Column, Is.EqualTo(5));
        Assert.That(_service.CurrentLocation.SymbolName, Is.EqualTo("MyFunction"));
    }

    [Test]
    public void RecordLocation_SetsTimestamp()
    {
        var before = DateTime.Now;
        _service.RecordLocation("/path/to/file.bas", 10, 5);
        var after = DateTime.Now;

        Assert.That(_service.CurrentLocation!.Timestamp, Is.InRange(before, after));
    }

    [Test]
    public void RecordLocation_PreviousLocationAddedToBackHistory()
    {
        _service.RecordLocation("/path/file1.bas", 10, 1);
        _service.RecordLocation("/path/file2.bas", 20, 1);

        Assert.That(_service.BackHistory, Has.Count.EqualTo(1));
        Assert.That(_service.BackHistory[0].FilePath, Is.EqualTo("/path/file1.bas"));
    }

    [Test]
    public void RecordLocation_SameLocation_DoesNotAddToHistory()
    {
        _service.RecordLocation("/path/file.bas", 10, 1);
        _service.RecordLocation("/path/file.bas", 10, 5); // Same file and line, different column

        Assert.That(_service.BackHistory, Is.Empty);
    }

    [Test]
    public void RecordLocation_ClearsForwardHistory()
    {
        _service.RecordLocation("/path/file1.bas", 10, 1);
        _service.RecordLocation("/path/file2.bas", 20, 1);
        _service.GoBack();

        Assert.That(_service.CanGoForward, Is.True);

        _service.RecordLocation("/path/file3.bas", 30, 1);

        Assert.That(_service.CanGoForward, Is.False);
        Assert.That(_service.ForwardHistory, Is.Empty);
    }

    [Test]
    public void RecordLocation_TrimsHistoryAtMaxSize()
    {
        // Record more than 100 locations (MaxHistorySize)
        for (int i = 0; i < 105; i++)
        {
            _service.RecordLocation($"/path/file{i}.bas", i, 1);
        }

        Assert.That(_service.BackHistory.Count, Is.LessThanOrEqualTo(100));
    }

    [Test]
    public void RecordLocation_FiresHistoryChangedEvent()
    {
        NavigationHistoryChangedEventArgs? eventArgs = null;
        _service.HistoryChanged += (s, e) => eventArgs = e;

        _service.RecordLocation("/path/file.bas", 10, 1);

        Assert.That(eventArgs, Is.Not.Null);
        Assert.That(eventArgs!.ChangeType, Is.EqualTo(NavigationChangeType.LocationAdded));
        Assert.That(eventArgs.Location, Is.Not.Null);
    }

    #endregion

    #region GoBack Tests

    [Test]
    public void GoBack_WhenCannotGoBack_ReturnsNull()
    {
        var result = _service.GoBack();

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GoBack_ReturnsPreviousLocation()
    {
        _service.RecordLocation("/path/file1.bas", 10, 1);
        _service.RecordLocation("/path/file2.bas", 20, 1);

        var result = _service.GoBack();

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.FilePath, Is.EqualTo("/path/file1.bas"));
    }

    [Test]
    public void GoBack_UpdatesCurrentLocation()
    {
        _service.RecordLocation("/path/file1.bas", 10, 1);
        _service.RecordLocation("/path/file2.bas", 20, 1);

        _service.GoBack();

        Assert.That(_service.CurrentLocation!.FilePath, Is.EqualTo("/path/file1.bas"));
    }

    [Test]
    public void GoBack_AddsCurrentToForwardHistory()
    {
        _service.RecordLocation("/path/file1.bas", 10, 1);
        _service.RecordLocation("/path/file2.bas", 20, 1);

        _service.GoBack();

        Assert.That(_service.CanGoForward, Is.True);
        Assert.That(_service.ForwardHistory[0].FilePath, Is.EqualTo("/path/file2.bas"));
    }

    [Test]
    public void GoBack_FiresHistoryChangedEvent()
    {
        _service.RecordLocation("/path/file1.bas", 10, 1);
        _service.RecordLocation("/path/file2.bas", 20, 1);

        NavigationHistoryChangedEventArgs? eventArgs = null;
        _service.HistoryChanged += (s, e) => eventArgs = e;

        _service.GoBack();

        Assert.That(eventArgs, Is.Not.Null);
        Assert.That(eventArgs!.ChangeType, Is.EqualTo(NavigationChangeType.NavigatedBack));
    }

    [Test]
    public void GoBack_MultipleTimesNavigatesHistory()
    {
        _service.RecordLocation("/path/file1.bas", 10, 1);
        _service.RecordLocation("/path/file2.bas", 20, 1);
        _service.RecordLocation("/path/file3.bas", 30, 1);

        _service.GoBack();
        Assert.That(_service.CurrentLocation!.FilePath, Is.EqualTo("/path/file2.bas"));

        _service.GoBack();
        Assert.That(_service.CurrentLocation!.FilePath, Is.EqualTo("/path/file1.bas"));

        Assert.That(_service.CanGoBack, Is.False);
    }

    #endregion

    #region GoForward Tests

    [Test]
    public void GoForward_WhenCannotGoForward_ReturnsNull()
    {
        var result = _service.GoForward();

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GoForward_ReturnsForwardLocation()
    {
        _service.RecordLocation("/path/file1.bas", 10, 1);
        _service.RecordLocation("/path/file2.bas", 20, 1);
        _service.GoBack();

        var result = _service.GoForward();

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.FilePath, Is.EqualTo("/path/file2.bas"));
    }

    [Test]
    public void GoForward_UpdatesCurrentLocation()
    {
        _service.RecordLocation("/path/file1.bas", 10, 1);
        _service.RecordLocation("/path/file2.bas", 20, 1);
        _service.GoBack();

        _service.GoForward();

        Assert.That(_service.CurrentLocation!.FilePath, Is.EqualTo("/path/file2.bas"));
    }

    [Test]
    public void GoForward_AddsCurrentToBackHistory()
    {
        _service.RecordLocation("/path/file1.bas", 10, 1);
        _service.RecordLocation("/path/file2.bas", 20, 1);
        _service.GoBack();

        _service.GoForward();

        Assert.That(_service.BackHistory[0].FilePath, Is.EqualTo("/path/file1.bas"));
    }

    [Test]
    public void GoForward_FiresHistoryChangedEvent()
    {
        _service.RecordLocation("/path/file1.bas", 10, 1);
        _service.RecordLocation("/path/file2.bas", 20, 1);
        _service.GoBack();

        NavigationHistoryChangedEventArgs? eventArgs = null;
        _service.HistoryChanged += (s, e) => eventArgs = e;

        _service.GoForward();

        Assert.That(eventArgs, Is.Not.Null);
        Assert.That(eventArgs!.ChangeType, Is.EqualTo(NavigationChangeType.NavigatedForward));
    }

    #endregion

    #region ClearHistory Tests

    [Test]
    public void ClearHistory_ClearsAllStacks()
    {
        _service.RecordLocation("/path/file1.bas", 10, 1);
        _service.RecordLocation("/path/file2.bas", 20, 1);
        _service.GoBack();

        _service.ClearHistory();

        Assert.That(_service.CanGoBack, Is.False);
        Assert.That(_service.CanGoForward, Is.False);
        Assert.That(_service.CurrentLocation, Is.Null);
    }

    [Test]
    public void ClearHistory_FiresHistoryChangedEvent()
    {
        _service.RecordLocation("/path/file1.bas", 10, 1);

        NavigationHistoryChangedEventArgs? eventArgs = null;
        _service.HistoryChanged += (s, e) => eventArgs = e;

        _service.ClearHistory();

        Assert.That(eventArgs, Is.Not.Null);
        Assert.That(eventArgs!.ChangeType, Is.EqualTo(NavigationChangeType.HistoryCleared));
        Assert.That(eventArgs.Location, Is.Null);
    }

    #endregion

    #region Integration Tests

    [Test]
    public void BackAndForward_MaintainsCorrectState()
    {
        _service.RecordLocation("/path/file1.bas", 10, 1);
        _service.RecordLocation("/path/file2.bas", 20, 1);
        _service.RecordLocation("/path/file3.bas", 30, 1);

        // Go back twice
        _service.GoBack();
        _service.GoBack();
        Assert.That(_service.CurrentLocation!.FilePath, Is.EqualTo("/path/file1.bas"));

        // Go forward once
        _service.GoForward();
        Assert.That(_service.CurrentLocation!.FilePath, Is.EqualTo("/path/file2.bas"));

        // Record new location (should clear forward history)
        _service.RecordLocation("/path/file4.bas", 40, 1);
        Assert.That(_service.CanGoForward, Is.False);
        Assert.That(_service.CanGoBack, Is.True);
    }

    #endregion
}

[TestFixture]
public class NavigationLocationTests
{
    [Test]
    public void DefaultLocation_HasDefaultValues()
    {
        var location = new NavigationLocation();

        Assert.That(location.FilePath, Is.EqualTo(""));
        Assert.That(location.Line, Is.EqualTo(0));
        Assert.That(location.Column, Is.EqualTo(0));
        Assert.That(location.SymbolName, Is.Null);
    }

    [Test]
    public void Location_CanSetAllProperties()
    {
        var timestamp = DateTime.Now;
        var location = new NavigationLocation
        {
            FilePath = "/path/to/file.bas",
            Line = 42,
            Column = 10,
            SymbolName = "TestMethod",
            Timestamp = timestamp
        };

        Assert.That(location.FilePath, Is.EqualTo("/path/to/file.bas"));
        Assert.That(location.Line, Is.EqualTo(42));
        Assert.That(location.Column, Is.EqualTo(10));
        Assert.That(location.SymbolName, Is.EqualTo("TestMethod"));
        Assert.That(location.Timestamp, Is.EqualTo(timestamp));
    }
}

[TestFixture]
public class NavigationHistoryChangedEventArgsTests
{
    [Test]
    public void EventArgs_CanSetProperties()
    {
        var location = new NavigationLocation { FilePath = "/path/file.bas", Line = 10 };
        var args = new NavigationHistoryChangedEventArgs
        {
            ChangeType = NavigationChangeType.LocationAdded,
            Location = location
        };

        Assert.That(args.ChangeType, Is.EqualTo(NavigationChangeType.LocationAdded));
        Assert.That(args.Location, Is.EqualTo(location));
    }
}
