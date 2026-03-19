using System.Collections.Concurrent;
using System.Text;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

public class OutputService : IOutputService
{
    private const int MaxLinesPerChannel = 100_000;

    private readonly ConcurrentDictionary<OutputCategory, List<string>> _messages = new();
    private readonly ConcurrentDictionary<string, OutputChannel> _channels = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IOutputChannel> _channelList = new();
    private readonly object _lock = new();
    private readonly object _channelLock = new();

    /// <summary>
    /// Maps OutputCategory enum values to their default channel names.
    /// </summary>
    private static readonly Dictionary<OutputCategory, string> CategoryChannelNames = new()
    {
        [OutputCategory.General] = "General",
        [OutputCategory.Build] = "Build",
        [OutputCategory.Debug] = "Debug Console",
        [OutputCategory.LanguageServer] = "BasicLang LSP",
        [OutputCategory.Git] = "Git",
        [OutputCategory.Extensions] = "Extensions"
    };

    public event EventHandler<OutputEventArgs>? OutputReceived;
    public event EventHandler<string>? ChannelCreated;
    public event EventHandler<IOutputChannel?>? ActiveChannelChanged;

    /// <summary>
    /// Raised when ShowOutput() is called to bring the panel to front.
    /// The UI layer subscribes to this.
    /// </summary>
    public event EventHandler? ShowOutputRequested;

    private IOutputChannel? _activeChannel;

    public IOutputChannel? ActiveChannel
    {
        get => _activeChannel;
        set
        {
            if (_activeChannel != value)
            {
                _activeChannel = value;
                ActiveChannelChanged?.Invoke(this, value);
            }
        }
    }

    public IReadOnlyList<IOutputChannel> Channels
    {
        get
        {
            lock (_channelLock)
            {
                return _channelList.ToList().AsReadOnly();
            }
        }
    }

    public OutputService()
    {
        // Initialize legacy category storage
        foreach (OutputCategory category in Enum.GetValues<OutputCategory>())
        {
            _messages[category] = new List<string>();
        }

        // Create default channels
        foreach (var kvp in CategoryChannelNames)
        {
            CreateChannel(kvp.Value);
        }

        // Set Build as default active channel
        ActiveChannel = GetChannel("Build");
    }

    // === Channel-based API ===

    public IOutputChannel CreateChannel(string name)
    {
        lock (_channelLock)
        {
            if (_channels.TryGetValue(name, out var existing))
                return existing;

            var channel = new OutputChannel(name, MaxLinesPerChannel, this);
            _channels[name] = channel;
            _channelList.Add(channel);
            ChannelCreated?.Invoke(this, name);
            return channel;
        }
    }

    public IOutputChannel? GetChannel(string name)
    {
        _channels.TryGetValue(name, out var channel);
        return channel;
    }

    public void ShowOutput()
    {
        ShowOutputRequested?.Invoke(this, EventArgs.Empty);
    }

    // === Legacy category-based API ===

    public void WriteLine(string message, OutputCategory category = OutputCategory.General)
    {
        Write(message + Environment.NewLine, category);
    }

    public void Write(string message, OutputCategory category = OutputCategory.General)
    {
        lock (_lock)
        {
            _messages[category].Add(message);
        }

        // Also route to the corresponding channel
        var channelName = CategoryChannelNames.GetValueOrDefault(category, "General");
        var channel = GetChannel(channelName);
        channel?.Append(message);

        OutputReceived?.Invoke(this, new OutputEventArgs(message, category));
    }

    public void WriteError(string message, OutputCategory category = OutputCategory.General)
    {
        lock (_lock)
        {
            _messages[category].Add($"[ERROR] {message}");
        }

        // Also route to the corresponding channel
        var channelName = CategoryChannelNames.GetValueOrDefault(category, "General");
        var channel = GetChannel(channelName);
        channel?.Append($"[ERROR] {message}");

        OutputReceived?.Invoke(this, new OutputEventArgs(message, category, isError: true));
    }

    public void Clear(OutputCategory category)
    {
        lock (_lock)
        {
            _messages[category].Clear();
        }

        // Also clear the corresponding channel
        var channelName = CategoryChannelNames.GetValueOrDefault(category, "General");
        var channel = GetChannel(channelName);
        channel?.Clear();
    }

    public void ClearAll()
    {
        lock (_lock)
        {
            foreach (var list in _messages.Values)
            {
                list.Clear();
            }
        }

        // Clear all channels
        lock (_channelLock)
        {
            foreach (var channel in _channelList)
            {
                channel.Clear();
            }
        }
    }

    public void Activate(OutputCategory category)
    {
        var channelName = CategoryChannelNames.GetValueOrDefault(category, "General");
        var channel = GetChannel(channelName);
        if (channel != null)
        {
            ActiveChannel = channel;
        }
    }

    public IReadOnlyList<string> GetMessages(OutputCategory category)
    {
        lock (_lock)
        {
            return _messages[category].ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// Gets the channel name for a given OutputCategory.
    /// Used by the ViewModel to map between the legacy enum and channel system.
    /// </summary>
    public static string GetChannelName(OutputCategory category)
    {
        return CategoryChannelNames.GetValueOrDefault(category, "General");
    }

    /// <summary>
    /// Gets the OutputCategory for a given channel name, if it maps to one.
    /// </summary>
    public static OutputCategory? GetCategoryForChannel(string channelName)
    {
        foreach (var kvp in CategoryChannelNames)
        {
            if (string.Equals(kvp.Value, channelName, StringComparison.OrdinalIgnoreCase))
                return kvp.Key;
        }
        return null;
    }
}

/// <summary>
/// A thread-safe output channel with ring buffer line storage.
/// </summary>
internal class OutputChannel : IOutputChannel
{
    private readonly int _maxLines;
    private readonly OutputService _owner;
    private readonly object _lock = new();
    private readonly LinkedList<string> _lines = new();
    private readonly StringBuilder _pendingLine = new();

    public string Name { get; }
    public bool PreserveScrollPosition { get; set; }

    public int LineCount
    {
        get
        {
            lock (_lock)
            {
                return _lines.Count;
            }
        }
    }

    public event EventHandler<string>? TextAppended;
    public event EventHandler? Cleared;

    public OutputChannel(string name, int maxLines, OutputService owner)
    {
        Name = name;
        _maxLines = maxLines;
        _owner = owner;
    }

    public void Append(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        lock (_lock)
        {
            // Split text by newlines and add complete lines to the buffer
            var parts = text.Split('\n');

            for (int i = 0; i < parts.Length; i++)
            {
                _pendingLine.Append(parts[i].TrimEnd('\r'));

                // If this isn't the last part, or the text ends with \n, commit the line
                if (i < parts.Length - 1)
                {
                    AddLine(_pendingLine.ToString());
                    _pendingLine.Clear();
                }
            }

            // If text ends with newline, the last part is empty and already committed
            // If it doesn't, the pending content stays in _pendingLine for the next Append
        }

        TextAppended?.Invoke(this, text);
    }

    public void AppendLine(string text)
    {
        Append(text + "\n");
    }

    public void Clear()
    {
        lock (_lock)
        {
            _lines.Clear();
            _pendingLine.Clear();
        }

        Cleared?.Invoke(this, EventArgs.Empty);
    }

    public void Show()
    {
        _owner.ActiveChannel = this;
        _owner.ShowOutput();
    }

    public string GetContent()
    {
        lock (_lock)
        {
            var sb = new StringBuilder();
            foreach (var line in _lines)
            {
                sb.AppendLine(line);
            }
            if (_pendingLine.Length > 0)
            {
                sb.Append(_pendingLine);
            }
            return sb.ToString();
        }
    }

    public IReadOnlyList<string> GetLines()
    {
        lock (_lock)
        {
            var result = new List<string>(_lines);
            if (_pendingLine.Length > 0)
            {
                result.Add(_pendingLine.ToString());
            }
            return result.AsReadOnly();
        }
    }

    private void AddLine(string line)
    {
        _lines.AddLast(line);

        // Ring buffer: remove oldest lines when exceeding max
        while (_lines.Count > _maxLines)
        {
            _lines.RemoveFirst();
        }
    }
}
