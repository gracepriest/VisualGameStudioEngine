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

/// <summary>
/// The form designer's findings for ONE file — its complete set, so an empty list is a retraction
/// rather than a no-op.
///
/// <para>⚠ Its OWN type rather than a reuse of <see cref="DiagnosticsUpdatedEvent"/>, which is the
/// general channel. The handler files these into a named collection so a designer refusal and the
/// language server's findings for the same .bas cannot erase one another — which means whoever
/// publishes on this channel decides that collection. On a shared channel, a future publisher would
/// have its findings filed as the designer's AND would clear the designer's, from a line that
/// looked entirely reasonable where it was written.</para>
/// </summary>
public record DesignerDiagnosticsEvent(string FilePath, IReadOnlyList<DiagnosticItem> Diagnostics);

public record ActiveDocumentChangedEvent(string? FilePath);
public record DocumentDirtyChangedEvent(string FilePath, bool IsDirty);

public record ThemeChangedEvent(string ThemeName);
