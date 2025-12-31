namespace VisualGameStudio.Core.Models;

public class BuildResult
{
    public bool Success { get; set; }
    public int ExitCode { get; set; }
    public string? OutputPath { get; set; }
    public string? ExecutablePath { get; set; }
    public string? GeneratedCode { get; set; }
    public string? GeneratedFileName { get; set; }
    public List<DiagnosticItem> Diagnostics { get; set; } = new();
    public TimeSpan Duration { get; set; }

    public int ErrorCount => Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
    public int WarningCount => Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);

    public IEnumerable<DiagnosticItem> Errors => Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);
    public IEnumerable<DiagnosticItem> Warnings => Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning);
}
