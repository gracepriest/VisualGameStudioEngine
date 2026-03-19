using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Threading;

namespace VisualGameStudio.Shell.Services;

/// <summary>
/// Priority level for screen reader announcements.
/// </summary>
public enum AnnouncePriority
{
    /// <summary>The announcement is polite — it waits for the current speech to finish.</summary>
    Polite,
    /// <summary>The announcement is assertive — it interrupts current speech.</summary>
    Assertive
}

/// <summary>
/// Service interface for announcing messages to assistive technology (screen readers).
/// Uses Avalonia's AutomationProperties.LiveSetting to update a hidden live region
/// that screen readers will pick up automatically via UIA LiveRegionChanged events.
/// </summary>
public interface IScreenReaderService
{
    /// <summary>
    /// Announce a message to the screen reader.
    /// </summary>
    void Announce(string message, AnnouncePriority priority = AnnouncePriority.Polite);

    /// <summary>
    /// Announce a message assertively, interrupting current speech.
    /// </summary>
    void AnnounceAssertive(string message);
}

/// <summary>
/// Default implementation that uses a hidden TextBlock with AutomationProperties.LiveSetting
/// to broadcast announcements to UIA screen readers (Narrator, NVDA, JAWS).
/// </summary>
public sealed class ScreenReaderService : IScreenReaderService
{
    private static ScreenReaderService? _instance;
    public static ScreenReaderService Instance => _instance ??= new ScreenReaderService();

    /// <summary>
    /// The hidden TextBlock used as a live region. Must be added to the visual tree
    /// (e.g., a StackPanel in MainWindow) to be picked up by UIA.
    /// </summary>
    private TextBlock? _liveRegionPolite;
    private TextBlock? _liveRegionAssertive;

    /// <summary>
    /// Suffix counter to ensure every announcement triggers a property-change event,
    /// even if the message text is the same as the previous one.
    /// </summary>
    private int _counter;

    private ScreenReaderService() { }

    /// <summary>
    /// Call once during MainWindow initialization to wire up the live-region controls.
    /// </summary>
    public void Initialize(TextBlock politeRegion, TextBlock assertiveRegion)
    {
        _liveRegionPolite = politeRegion;
        _liveRegionAssertive = assertiveRegion;
    }

    public void Announce(string message, AnnouncePriority priority = AnnouncePriority.Polite)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        Dispatcher.UIThread.Post(() =>
        {
            _counter++;
            var target = priority == AnnouncePriority.Assertive
                ? _liveRegionAssertive
                : _liveRegionPolite;

            if (target != null)
            {
                // Append invisible counter to force UIA to fire a property-changed event
                target.Text = $"{message} \u200B{_counter}";
            }
        });
    }

    public void AnnounceAssertive(string message)
    {
        Announce(message, AnnouncePriority.Assertive);
    }
}
