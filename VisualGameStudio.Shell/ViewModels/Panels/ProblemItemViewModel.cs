using CommunityToolkit.Mvvm.ComponentModel;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.Shell.ViewModels.Panels;

/// <summary>
/// Represents a single problem (error, warning, or info) in the Problems panel.
/// Wraps a <see cref="DiagnosticItem"/> with presentation-layer properties.
/// </summary>
public partial class ProblemItemViewModel : ObservableObject
{
    public ProblemItemViewModel(DiagnosticItem diagnostic, string? source = null)
    {
        Severity = diagnostic.Severity;
        Message = diagnostic.Message;
        Code = diagnostic.Id;
        Source = source ?? diagnostic.Source ?? "BasicLang";
        FilePath = diagnostic.FilePath;
        FileName = diagnostic.FilePath != null ? Path.GetFileName(diagnostic.FilePath) : "";
        Line = diagnostic.Line;
        Column = diagnostic.Column;
        EndLine = diagnostic.EndLine;
        EndColumn = diagnostic.EndColumn;
        TimeStamp = DateTime.Now;
        OriginalDiagnostic = diagnostic;
    }

    /// <summary>
    /// The severity level of this problem.
    /// </summary>
    public DiagnosticSeverity Severity { get; }

    /// <summary>
    /// The diagnostic message describing the problem.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// The diagnostic code (e.g., BL1001).
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// The source of the diagnostic (e.g., "BasicLang", "Build", "Runtime").
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// The full file path where the problem occurred.
    /// </summary>
    public string? FilePath { get; }

    /// <summary>
    /// The file name only (no directory) for display.
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// 1-based line number where the problem starts.
    /// </summary>
    public int Line { get; }

    /// <summary>
    /// 1-based column number where the problem starts.
    /// </summary>
    public int Column { get; }

    /// <summary>
    /// 1-based line number where the problem ends.
    /// </summary>
    public int EndLine { get; }

    /// <summary>
    /// 1-based column number where the problem ends.
    /// </summary>
    public int EndColumn { get; }

    /// <summary>
    /// When this problem was reported.
    /// </summary>
    public DateTime TimeStamp { get; }

    /// <summary>
    /// The underlying diagnostic model item for navigation.
    /// </summary>
    public DiagnosticItem OriginalDiagnostic { get; }

    /// <summary>
    /// Icon text for the severity level, used in the view.
    /// </summary>
    public string SeverityIcon => Severity switch
    {
        DiagnosticSeverity.Error => "\u2716",    // heavy multiplication sign (X)
        DiagnosticSeverity.Warning => "\u26A0",  // warning sign
        DiagnosticSeverity.Info => "\u2139",     // info sign
        _ => "\u2139"
    };

    /// <summary>
    /// Color string for the severity icon.
    /// </summary>
    public string SeverityColor => Severity switch
    {
        DiagnosticSeverity.Error => "#F14C4C",
        DiagnosticSeverity.Warning => "#CCA700",
        DiagnosticSeverity.Info => "#3794FF",
        _ => "#3794FF"
    };

    /// <summary>
    /// Location string for display: "filename(line,col)".
    /// </summary>
    public string Location => FilePath != null ? $"{FileName}({Line},{Column})" : "";

    /// <summary>
    /// File path relative for grouping display, or the file name.
    /// </summary>
    public string FileGroupKey => FilePath ?? "(no file)";

    public override string ToString()
    {
        return $"{Severity} {Code}: {Message} at {Location}";
    }
}
