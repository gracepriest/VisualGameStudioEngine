using CommunityToolkit.Mvvm.ComponentModel;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Documents;

/// <summary>
/// ViewModel for extension-contributed webview panels.
/// Displays HTML content from extensions that call vscode.window.createWebviewPanel().
/// </summary>
public partial class WebViewDocumentViewModel : ViewModelBase, IDocumentViewModel
{
    [ObservableProperty]
    private string _htmlContent = "";

    [ObservableProperty]
    private string _title = "WebView";

    /// <summary>
    /// The unique panel ID assigned by the extension host.
    /// </summary>
    public string PanelId { get; }

    /// <summary>
    /// The view type identifier (e.g., "myExtension.preview").
    /// </summary>
    public string ViewType { get; }

    /// <summary>
    /// The extension ID that created this webview.
    /// </summary>
    public string ExtensionId { get; }

    public string Id => $"webview:{PanelId}";
    public string? FilePath => null;
    public bool IsDirty => false;
    public bool CanClose => true;

    public event EventHandler? DirtyChanged;
    public event EventHandler? TitleChanged;

    /// <summary>
    /// Raised when the webview sends a message to the extension via postMessage.
    /// </summary>
    public event EventHandler<string>? MessagePosted;

    public WebViewDocumentViewModel(string panelId, string viewType, string extensionId, string? title = null)
    {
        PanelId = panelId;
        ViewType = viewType;
        ExtensionId = extensionId;
        if (!string.IsNullOrEmpty(title))
        {
            Title = title;
        }
    }

    partial void OnTitleChanged(string value)
    {
        TitleChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Updates the HTML content displayed in the webview.
    /// Called when the extension sends webview/setHtml.
    /// </summary>
    public void SetHtmlContent(string html)
    {
        HtmlContent = html;
    }

    /// <summary>
    /// Sends a message from the webview to the extension (postMessage stub).
    /// In a full implementation, this would route through the extension host.
    /// </summary>
    public void PostMessage(string message)
    {
        MessagePosted?.Invoke(this, message);
    }

    public Task<bool> SaveAsync(CancellationToken cancellationToken = default)
    {
        // WebView panels are not saveable
        return Task.FromResult(false);
    }

    public Task<bool> SaveAsAsync(string path, CancellationToken cancellationToken = default)
    {
        // WebView panels are not saveable
        return Task.FromResult(false);
    }

    public Task<bool> CloseAsync()
    {
        return Task.FromResult(true);
    }
}
