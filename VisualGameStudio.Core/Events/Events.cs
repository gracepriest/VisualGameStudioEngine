using VisualGameStudio.Core.Models;

namespace VisualGameStudio.Core.Events;

public record FileOpenedEvent(string FilePath, string Content);
public record FileSavedEvent(string FilePath);
public record FileClosedEvent(string FilePath);

public record ProjectOpenedEvent(BasicLangProject Project);
public record ProjectClosedEvent(BasicLangProject Project);
public record ProjectChangedEvent(BasicLangProject Project);

public record SolutionOpenedEvent(BasicLangSolution Solution);
public record SolutionClosedEvent(BasicLangSolution Solution);

public record BuildStartedEvent(BasicLangProject Project);
public record BuildCompletedEvent(BuildResult Result);
public record BuildProgressEvent(string Message, int? PercentComplete);

public record DiagnosticsUpdatedEvent(string FilePath, IReadOnlyList<DiagnosticItem> Diagnostics);

public record ActiveDocumentChangedEvent(string? FilePath);
public record DocumentDirtyChangedEvent(string FilePath, bool IsDirty);

public record ThemeChangedEvent(string ThemeName);
