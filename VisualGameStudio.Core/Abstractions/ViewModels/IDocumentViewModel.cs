namespace VisualGameStudio.Core.Abstractions.ViewModels;

public interface IDocumentViewModel
{
    string Id { get; }
    string Title { get; }
    string? FilePath { get; }
    bool IsDirty { get; }
    bool CanClose { get; }

    Task<bool> SaveAsync(CancellationToken cancellationToken = default);
    Task<bool> SaveAsAsync(string path, CancellationToken cancellationToken = default);
    Task<bool> CloseAsync();

    event EventHandler? DirtyChanged;
    event EventHandler? TitleChanged;
}
