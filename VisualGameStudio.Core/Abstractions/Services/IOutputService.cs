namespace VisualGameStudio.Core.Abstractions.Services;

public interface IOutputService
{
    void WriteLine(string message, OutputCategory category = OutputCategory.General);
    void Write(string message, OutputCategory category = OutputCategory.General);
    void WriteError(string message, OutputCategory category = OutputCategory.General);
    void Clear(OutputCategory category);
    void ClearAll();
    void Activate(OutputCategory category);

    IReadOnlyList<string> GetMessages(OutputCategory category);

    event EventHandler<OutputEventArgs>? OutputReceived;
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
    LanguageServer
}
