using System.Text.Json;

namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Provides Language Server Protocol (LSP) client functionality.
/// Enables communication with any LSP-compliant language server.
/// </summary>
public interface ILspClientService : IDisposable
{
    /// <summary>
    /// Gets the current connection state.
    /// </summary>
    LspConnectionState State { get; }

    /// <summary>
    /// Gets the server capabilities after initialization.
    /// </summary>
    ServerCapabilities? Capabilities { get; }

    /// <summary>
    /// Gets the server information.
    /// </summary>
    ServerInfo? ServerInfo { get; }

    /// <summary>
    /// Starts a language server process and connects to it.
    /// </summary>
    /// <param name="serverPath">Path to the language server executable.</param>
    /// <param name="arguments">Command line arguments.</param>
    /// <param name="workspaceRoot">The workspace root path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> StartServerAsync(string serverPath, string? arguments, string workspaceRoot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Connects to an existing language server via TCP.
    /// </summary>
    /// <param name="host">The host address.</param>
    /// <param name="port">The port number.</param>
    /// <param name="workspaceRoot">The workspace root path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> ConnectAsync(string host, int port, string workspaceRoot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the language server and disconnects.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Notifies the server that a document was opened.
    /// </summary>
    Task DidOpenAsync(string uri, string languageId, int version, string text);

    /// <summary>
    /// Notifies the server that a document was changed.
    /// </summary>
    Task DidChangeAsync(string uri, int version, IEnumerable<TextDocumentContentChangeEvent> changes);

    /// <summary>
    /// Notifies the server that a document was saved.
    /// </summary>
    Task DidSaveAsync(string uri, string? text = null);

    /// <summary>
    /// Notifies the server that a document was closed.
    /// </summary>
    Task DidCloseAsync(string uri);

    /// <summary>
    /// Requests completion items at a position.
    /// </summary>
    Task<CompletionList?> GetCompletionAsync(string uri, int line, int character, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests hover information at a position.
    /// </summary>
    Task<Hover?> GetHoverAsync(string uri, int line, int character, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests the definition location of a symbol.
    /// </summary>
    Task<IReadOnlyList<Location>> GetDefinitionAsync(string uri, int line, int character, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests all references to a symbol.
    /// </summary>
    Task<IReadOnlyList<Location>> GetReferencesAsync(string uri, int line, int character, bool includeDeclaration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests document symbols (outline).
    /// </summary>
    Task<IReadOnlyList<LspDocumentSymbol>> GetDocumentSymbolsAsync(string uri, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests workspace symbols matching a query.
    /// </summary>
    Task<IReadOnlyList<SymbolInformation>> GetWorkspaceSymbolsAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests code actions at a range.
    /// </summary>
    Task<IReadOnlyList<LspCodeAction>> GetCodeActionsAsync(string uri, LspRange range, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests document formatting.
    /// </summary>
    Task<IReadOnlyList<LspTextEdit>> FormatDocumentAsync(string uri, LspFormattingOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests range formatting.
    /// </summary>
    Task<IReadOnlyList<LspTextEdit>> FormatRangeAsync(string uri, LspRange range, LspFormattingOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests rename of a symbol.
    /// </summary>
    Task<WorkspaceEdit?> RenameAsync(string uri, int line, int character, string newName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests signature help.
    /// </summary>
    Task<LspSignatureHelp?> GetSignatureHelpAsync(string uri, int line, int character, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a custom request to the server.
    /// </summary>
    Task<JsonElement?> SendRequestAsync(string method, object? parameters, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a custom notification to the server.
    /// </summary>
    Task SendNotificationAsync(string method, object? parameters);

    /// <summary>
    /// Raised when the connection state changes.
    /// </summary>
    event EventHandler<LspStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Raised when diagnostics are published by the server.
    /// </summary>
    event EventHandler<LspDiagnosticsEventArgs>? DiagnosticsReceived;

    /// <summary>
    /// Raised when a log message is received.
    /// </summary>
    event EventHandler<LogMessageEventArgs>? LogMessageReceived;

    /// <summary>
    /// Raised when a show message request is received.
    /// </summary>
    event EventHandler<ShowMessageEventArgs>? ShowMessageReceived;
}

#region LSP Types

/// <summary>
/// LSP connection state.
/// </summary>
public enum LspConnectionState
{
    /// <summary>Not connected.</summary>
    Disconnected,
    /// <summary>Connecting to server.</summary>
    Connecting,
    /// <summary>Initializing protocol.</summary>
    Initializing,
    /// <summary>Ready for requests.</summary>
    Ready,
    /// <summary>Shutting down.</summary>
    ShuttingDown,
    /// <summary>Error state.</summary>
    Error
}

/// <summary>
/// Server capabilities returned after initialization.
/// </summary>
public class ServerCapabilities
{
    public TextDocumentSyncOptions? TextDocumentSync { get; set; }
    public bool HoverProvider { get; set; }
    public CompletionOptions? CompletionProvider { get; set; }
    public SignatureHelpOptions? SignatureHelpProvider { get; set; }
    public bool DefinitionProvider { get; set; }
    public bool ReferencesProvider { get; set; }
    public bool DocumentSymbolProvider { get; set; }
    public bool WorkspaceSymbolProvider { get; set; }
    public bool CodeActionProvider { get; set; }
    public bool DocumentFormattingProvider { get; set; }
    public bool DocumentRangeFormattingProvider { get; set; }
    public bool RenameProvider { get; set; }
    public bool FoldingRangeProvider { get; set; }
}

/// <summary>
/// Text document sync options.
/// </summary>
public class TextDocumentSyncOptions
{
    public bool OpenClose { get; set; }
    public TextDocumentSyncKind Change { get; set; }
    public bool Save { get; set; }
}

/// <summary>
/// Text document sync kind.
/// </summary>
public enum TextDocumentSyncKind
{
    None = 0,
    Full = 1,
    Incremental = 2
}

/// <summary>
/// Completion provider options.
/// </summary>
public class CompletionOptions
{
    public List<string>? TriggerCharacters { get; set; }
    public bool ResolveProvider { get; set; }
}

/// <summary>
/// Signature help options.
/// </summary>
public class SignatureHelpOptions
{
    public List<string>? TriggerCharacters { get; set; }
    public List<string>? RetriggerCharacters { get; set; }
}

/// <summary>
/// Server information.
/// </summary>
public class ServerInfo
{
    public string Name { get; set; } = "";
    public string? Version { get; set; }
}

/// <summary>
/// Text document content change event.
/// </summary>
public class TextDocumentContentChangeEvent
{
    public LspRange? Range { get; set; }
    public int? RangeLength { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>
/// LSP Range.
/// </summary>
public class LspRange
{
    public LspPosition Start { get; set; } = new();
    public LspPosition End { get; set; } = new();
}

/// <summary>
/// LSP Position.
/// </summary>
public class LspPosition
{
    public int Line { get; set; }
    public int Character { get; set; }
}

/// <summary>
/// Location in a document.
/// </summary>
public class Location
{
    public string Uri { get; set; } = "";
    public LspRange Range { get; set; } = new();
}

/// <summary>
/// Completion list.
/// </summary>
public class CompletionList
{
    public bool IsIncomplete { get; set; }
    public List<LspCompletionItem> Items { get; set; } = new();
}

/// <summary>
/// LSP Completion item.
/// </summary>
public class LspCompletionItem
{
    public string Label { get; set; } = "";
    public LspCompletionItemKind Kind { get; set; }
    public string? Detail { get; set; }
    public string? Documentation { get; set; }
    public bool Deprecated { get; set; }
    public string? InsertText { get; set; }
    public InsertTextFormat InsertTextFormat { get; set; }
    public LspTextEdit? TextEdit { get; set; }
    public List<LspTextEdit>? AdditionalTextEdits { get; set; }
    public string? SortText { get; set; }
    public string? FilterText { get; set; }
}

/// <summary>
/// LSP Completion item kind.
/// </summary>
public enum LspCompletionItemKind
{
    Text = 1,
    Method = 2,
    Function = 3,
    Constructor = 4,
    Field = 5,
    Variable = 6,
    Class = 7,
    Interface = 8,
    Module = 9,
    Property = 10,
    Unit = 11,
    Value = 12,
    Enum = 13,
    Keyword = 14,
    Snippet = 15,
    Color = 16,
    File = 17,
    Reference = 18,
    Folder = 19,
    EnumMember = 20,
    Constant = 21,
    Struct = 22,
    Event = 23,
    Operator = 24,
    TypeParameter = 25
}

/// <summary>
/// Insert text format.
/// </summary>
public enum InsertTextFormat
{
    PlainText = 1,
    Snippet = 2
}

/// <summary>
/// LSP Text edit.
/// </summary>
public class LspTextEdit
{
    public LspRange Range { get; set; } = new();
    public string NewText { get; set; } = "";
}

/// <summary>
/// Hover information.
/// </summary>
public class Hover
{
    public MarkupContent? Contents { get; set; }
    public LspRange? Range { get; set; }
}

/// <summary>
/// Markup content.
/// </summary>
public class MarkupContent
{
    public string Kind { get; set; } = "plaintext";
    public string Value { get; set; } = "";
}

/// <summary>
/// LSP Document symbol.
/// </summary>
public class LspDocumentSymbol
{
    public string Name { get; set; } = "";
    public string? Detail { get; set; }
    public LspSymbolKind Kind { get; set; }
    public bool Deprecated { get; set; }
    public LspRange Range { get; set; } = new();
    public LspRange SelectionRange { get; set; } = new();
    public List<LspDocumentSymbol>? Children { get; set; }
}

/// <summary>
/// Symbol information.
/// </summary>
public class SymbolInformation
{
    public string Name { get; set; } = "";
    public LspSymbolKind Kind { get; set; }
    public bool Deprecated { get; set; }
    public Location Location { get; set; } = new();
    public string? ContainerName { get; set; }
}

/// <summary>
/// LSP Symbol kind.
/// </summary>
public enum LspSymbolKind
{
    File = 1,
    Module = 2,
    Namespace = 3,
    Package = 4,
    Class = 5,
    Method = 6,
    Property = 7,
    Field = 8,
    Constructor = 9,
    Enum = 10,
    Interface = 11,
    Function = 12,
    Variable = 13,
    Constant = 14,
    String = 15,
    Number = 16,
    Boolean = 17,
    Array = 18,
    Object = 19,
    Key = 20,
    Null = 21,
    EnumMember = 22,
    Struct = 23,
    Event = 24,
    Operator = 25,
    TypeParameter = 26
}

/// <summary>
/// LSP Code action.
/// </summary>
public class LspCodeAction
{
    public string Title { get; set; } = "";
    public LspCodeActionKind Kind { get; set; }
    public List<LspDiagnostic>? Diagnostics { get; set; }
    public bool IsPreferred { get; set; }
    public WorkspaceEdit? Edit { get; set; }
    public LspCommand? Command { get; set; }
}

/// <summary>
/// LSP Code action kind.
/// </summary>
public enum LspCodeActionKind
{
    Empty,
    QuickFix,
    Refactor,
    RefactorExtract,
    RefactorInline,
    RefactorRewrite,
    Source,
    SourceOrganizeImports,
    SourceFixAll
}

/// <summary>
/// Workspace edit.
/// </summary>
public class WorkspaceEdit
{
    public Dictionary<string, List<LspTextEdit>>? Changes { get; set; }
}

/// <summary>
/// LSP Command.
/// </summary>
public class LspCommand
{
    public string Title { get; set; } = "";
    public string CommandId { get; set; } = "";
    public List<object>? Arguments { get; set; }
}

/// <summary>
/// LSP Formatting options.
/// </summary>
public class LspFormattingOptions
{
    public int TabSize { get; set; } = 4;
    public bool InsertSpaces { get; set; } = true;
    public bool TrimTrailingWhitespace { get; set; }
    public bool InsertFinalNewline { get; set; }
    public bool TrimFinalNewlines { get; set; }
}

/// <summary>
/// LSP Signature help.
/// </summary>
public class LspSignatureHelp
{
    public List<SignatureInformation> Signatures { get; set; } = new();
    public int? ActiveSignature { get; set; }
    public int? ActiveParameter { get; set; }
}

/// <summary>
/// Signature information.
/// </summary>
public class SignatureInformation
{
    public string Label { get; set; } = "";
    public string? Documentation { get; set; }
    public List<ParameterInformation>? Parameters { get; set; }
}

/// <summary>
/// Parameter information.
/// </summary>
public class ParameterInformation
{
    public string Label { get; set; } = "";
    public string? Documentation { get; set; }
}

/// <summary>
/// LSP Diagnostic.
/// </summary>
public class LspDiagnostic
{
    public LspRange Range { get; set; } = new();
    public LspDiagnosticSeverity Severity { get; set; }
    public string? Code { get; set; }
    public string? Source { get; set; }
    public string Message { get; set; } = "";
    public List<DiagnosticRelatedInformation>? RelatedInformation { get; set; }
}

/// <summary>
/// LSP Diagnostic severity.
/// </summary>
public enum LspDiagnosticSeverity
{
    Error = 1,
    Warning = 2,
    Information = 3,
    Hint = 4
}

/// <summary>
/// Diagnostic related information.
/// </summary>
public class DiagnosticRelatedInformation
{
    public Location Location { get; set; } = new();
    public string Message { get; set; } = "";
}

#endregion

#region Event Args

/// <summary>
/// Event args for state changes.
/// </summary>
public class LspStateChangedEventArgs : EventArgs
{
    public LspConnectionState OldState { get; }
    public LspConnectionState NewState { get; }
    public string? Error { get; }

    public LspStateChangedEventArgs(LspConnectionState oldState, LspConnectionState newState, string? error = null)
    {
        OldState = oldState;
        NewState = newState;
        Error = error;
    }
}

/// <summary>
/// Event args for LSP diagnostics.
/// </summary>
public class LspDiagnosticsEventArgs : EventArgs
{
    public string Uri { get; }
    public IReadOnlyList<LspDiagnostic> Diagnostics { get; }

    public LspDiagnosticsEventArgs(string uri, IReadOnlyList<LspDiagnostic> diagnostics)
    {
        Uri = uri;
        Diagnostics = diagnostics;
    }
}

/// <summary>
/// Event args for log messages.
/// </summary>
public class LogMessageEventArgs : EventArgs
{
    public MessageType Type { get; }
    public string Message { get; }

    public LogMessageEventArgs(MessageType type, string message)
    {
        Type = type;
        Message = message;
    }
}

/// <summary>
/// Event args for show message.
/// </summary>
public class ShowMessageEventArgs : EventArgs
{
    public MessageType Type { get; }
    public string Message { get; }

    public ShowMessageEventArgs(MessageType type, string message)
    {
        Type = type;
        Message = message;
    }
}

/// <summary>
/// Message type.
/// </summary>
public enum MessageType
{
    Error = 1,
    Warning = 2,
    Info = 3,
    Log = 4
}

#endregion
