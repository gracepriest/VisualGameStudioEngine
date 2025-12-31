namespace VisualGameStudio.Core.Abstractions.Services;

public interface IDialogService
{
    Task<string?> ShowOpenFileDialogAsync(FileDialogOptions options);
    Task<string[]?> ShowOpenFilesDialogAsync(FileDialogOptions options);
    Task<string?> ShowSaveFileDialogAsync(FileDialogOptions options);
    Task<string?> ShowFolderDialogAsync(FolderDialogOptions options);
    Task<DialogResult> ShowMessageAsync(string title, string message, DialogButtons buttons = DialogButtons.Ok, DialogIcon icon = DialogIcon.Information);
    Task<string?> ShowInputDialogAsync(string title, string prompt, string defaultValue = "");
    Task<string?> ShowFunctionBreakpointDialogAsync();
    Task<BreakpointConditionResult?> ShowBreakpointConditionDialogAsync(string location, string? condition, string? hitCount, string? logMessage);
    Task<List<ExceptionSettingResult>?> ShowExceptionSettingsDialogAsync(IEnumerable<ExceptionSettingResult>? currentSettings = null);
    Task<int?> ShowGoToLineDialogAsync(int currentLine, int totalLines);
    Task<GoToSymbolResult?> ShowGoToSymbolDialogAsync(string sourceCode, string? filePath = null);
    Task<int> ShowListSelectionAsync(string title, string prompt, IEnumerable<string> items);
}

public class GoToSymbolResult
{
    public string Name { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public string? FilePath { get; set; }
}

public class BreakpointConditionResult
{
    public string? Condition { get; set; }
    public string? HitCount { get; set; }
    public string? LogMessage { get; set; }
    public bool DialogResult { get; set; }
}

public class ExceptionSettingResult
{
    public string ExceptionType { get; set; } = "";
    public bool BreakWhenThrown { get; set; }
    public bool BreakWhenUserUnhandled { get; set; } = true;
}

public class FileDialogOptions
{
    public string Title { get; set; } = "Select File";
    public string? InitialDirectory { get; set; }
    public string? InitialFileName { get; set; }
    public List<FileDialogFilter> Filters { get; set; } = new();
}

public class FileDialogFilter
{
    public string Name { get; set; } = "";
    public List<string> Extensions { get; set; } = new();

    public FileDialogFilter() { }
    public FileDialogFilter(string name, params string[] extensions)
    {
        Name = name;
        Extensions = extensions.ToList();
    }
}

public class FolderDialogOptions
{
    public string Title { get; set; } = "Select Folder";
    public string? InitialDirectory { get; set; }
}

public enum DialogResult
{
    None,
    Ok,
    Cancel,
    Yes,
    No
}

public enum DialogButtons
{
    Ok,
    OkCancel,
    YesNo,
    YesNoCancel
}

public enum DialogIcon
{
    None,
    Information,
    Warning,
    Error,
    Question
}

/// <summary>
/// Extension methods for IDialogService providing convenience methods
/// </summary>
public static class DialogServiceExtensions
{
    /// <summary>
    /// Shows a prompt dialog and returns the entered text
    /// </summary>
    public static Task<string?> PromptAsync(this IDialogService dialogService, string title, string prompt, string defaultValue = "")
    {
        return dialogService.ShowInputDialogAsync(title, prompt, defaultValue);
    }

    /// <summary>
    /// Shows a confirmation dialog with Yes/No buttons
    /// </summary>
    public static async Task<bool> ConfirmAsync(this IDialogService dialogService, string title, string message)
    {
        var result = await dialogService.ShowMessageAsync(title, message, DialogButtons.YesNo, DialogIcon.Question);
        return result == DialogResult.Yes;
    }

    /// <summary>
    /// Shows an open file dialog with the specified filters
    /// </summary>
    public static Task<string[]?> ShowOpenFileDialogAsync(
        this IDialogService dialogService,
        string title,
        (string Name, string[] Extensions)[] filters,
        bool allowMultiple = false)
    {
        var options = new FileDialogOptions
        {
            Title = title,
            Filters = filters.Select(f => new FileDialogFilter(f.Name, f.Extensions)).ToList()
        };

        return allowMultiple
            ? dialogService.ShowOpenFilesDialogAsync(options)
            : dialogService.ShowOpenFileDialogAsync(options).ContinueWith(t =>
                t.Result != null ? new[] { t.Result } : null);
    }
}
