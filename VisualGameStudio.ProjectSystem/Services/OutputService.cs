using System.Collections.Concurrent;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

public class OutputService : IOutputService
{
    private readonly ConcurrentDictionary<OutputCategory, List<string>> _messages = new();
    private readonly object _lock = new();

    public event EventHandler<OutputEventArgs>? OutputReceived;

    public OutputService()
    {
        foreach (OutputCategory category in Enum.GetValues<OutputCategory>())
        {
            _messages[category] = new List<string>();
        }
    }

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
        OutputReceived?.Invoke(this, new OutputEventArgs(message, category));
    }

    public void WriteError(string message, OutputCategory category = OutputCategory.General)
    {
        lock (_lock)
        {
            _messages[category].Add($"[ERROR] {message}");
        }
        OutputReceived?.Invoke(this, new OutputEventArgs(message, category, isError: true));
    }

    public void Clear(OutputCategory category)
    {
        lock (_lock)
        {
            _messages[category].Clear();
        }
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
    }

    public void Activate(OutputCategory category)
    {
        // This is handled by the UI layer
    }

    public IReadOnlyList<string> GetMessages(OutputCategory category)
    {
        lock (_lock)
        {
            return _messages[category].ToList().AsReadOnly();
        }
    }
}
