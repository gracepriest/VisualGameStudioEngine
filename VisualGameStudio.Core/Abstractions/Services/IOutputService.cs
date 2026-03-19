namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Service for managing output channels in the IDE.
/// Provides both the legacy category-based API and the new channel-based API.
/// </summary>
public interface IOutputService
{
    // === Legacy category-based API (backward compatible) ===

    void WriteLine(string message, OutputCategory category = OutputCategory.General);
    void Write(string message, OutputCategory category = OutputCategory.General);
    void WriteError(string message, OutputCategory category = OutputCategory.General);
    void Clear(OutputCategory category);
    void ClearAll();
    void Activate(OutputCategory category);

    IReadOnlyList<string> GetMessages(OutputCategory category);

    event EventHandler<OutputEventArgs>? OutputReceived;

    // === New channel-based API ===

    /// <summary>
    /// Creates a new output channel with the given name.
    /// If a channel with the name already exists, returns the existing one.
    /// </summary>
    IOutputChannel CreateChannel(string name);

    /// <summary>
    /// Gets an existing output channel by name. Returns null if not found.
    /// </summary>
    IOutputChannel? GetChannel(string name);

    /// <summary>
    /// All available output channels.
    /// </summary>
    IReadOnlyList<IOutputChannel> Channels { get; }

    /// <summary>
    /// The currently active (visible) channel.
    /// </summary>
    IOutputChannel? ActiveChannel { get; set; }

    /// <summary>
    /// Raised when a new channel is created.
    /// </summary>
    event EventHandler<string>? ChannelCreated;

    /// <summary>
    /// Raised when the active channel changes.
    /// </summary>
    event EventHandler<IOutputChannel?>? ActiveChannelChanged;

    /// <summary>
    /// Brings the output panel to the front.
    /// </summary>
    void ShowOutput();
}

/// <summary>
/// Represents a single output channel (e.g., Build, Debug, LSP, Git).
/// Thread-safe. Supports ANSI color codes (pass-through to renderer).
/// Uses a ring buffer to cap at MaxLines.
/// </summary>
public interface IOutputChannel
{
    /// <summary>
    /// Display name of the channel.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Appends text to the channel without a trailing newline.
    /// </summary>
    void Append(string text);

    /// <summary>
    /// Appends text to the channel with a trailing newline.
    /// </summary>
    void AppendLine(string text);

    /// <summary>
    /// Clears all content in the channel.
    /// </summary>
    void Clear();

    /// <summary>
    /// Switches the output panel to display this channel.
    /// </summary>
    void Show();

    /// <summary>
    /// Gets the full content of the channel as a single string.
    /// </summary>
    string GetContent();

    /// <summary>
    /// Gets all lines currently in the channel.
    /// </summary>
    IReadOnlyList<string> GetLines();

    /// <summary>
    /// Gets the number of lines in the channel.
    /// </summary>
    int LineCount { get; }

    /// <summary>
    /// Raised when new text is appended to the channel.
    /// The string argument is the newly appended text.
    /// </summary>
    event EventHandler<string>? TextAppended;

    /// <summary>
    /// Raised when the channel is cleared.
    /// </summary>
    event EventHandler? Cleared;

    /// <summary>
    /// Whether to preserve the scroll position when new text arrives.
    /// When false (default), auto-scrolls to the bottom.
    /// </summary>
    bool PreserveScrollPosition { get; set; }
}

public class OutputEventArgs : EventArgs
{
    public string Message { get; }
    public OutputCategory Category { get; }
    public bool IsError { get; }

    public OutputEventArgs(string message, OutputCategory category, bool isError = false)
    {
        Message = message;
        Category = category;
        IsError = isError;
    }
}

public enum OutputCategory
{
    General,
    Build,
    Debug,
    LanguageServer,
    Git,
    Extensions
}
