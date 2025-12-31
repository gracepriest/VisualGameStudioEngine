namespace VisualGameStudio.Core.Models;

public class DiagnosticItem
{
    public string Id { get; set; } = "";
    public string Message { get; set; } = "";
    public DiagnosticSeverity Severity { get; set; } = DiagnosticSeverity.Error;
    public string? FilePath { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
    public string? Source { get; set; }

    public string Location => FilePath != null ? $"{Path.GetFileName(FilePath)}({Line},{Column})" : "";

    public override string ToString()
    {
        return $"{Severity} {Id}: {Message} at {Location}";
    }
}

public enum DiagnosticSeverity
{
    Hidden,
    Info,
    Warning,
    Error
}
