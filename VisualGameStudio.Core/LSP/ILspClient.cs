using System.Text.Json;

namespace VisualGameStudio.Core.LSP;

/// <summary>
/// Generic LSP client interface for connecting to any language server
/// </summary>
public interface ILspClient : IDisposable
{
    /// <summary>
    /// Whether the client is connected and initialized
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// The language server capabilities after initialization
    /// </summary>
    ServerCapabilities? Capabilities { get; }

    /// <summary>
    /// Start and initialize the language server
    /// </summary>
    Task<bool> InitializeAsync(string workspaceRoot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Shutdown the language server gracefully
    /// </summary>
    Task ShutdownAsync();

    /// <summary>
    /// Send a request to the server and get a response
    /// </summary>
    Task<TResponse?> SendRequestAsync<TResponse>(string method, object? parameters, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a notification to the server (no response expected)
    /// </summary>
    Task SendNotificationAsync(string method, object? parameters);

    /// <summary>
    /// Event raised when server sends a notification
    /// </summary>
    event EventHandler<LspNotificationEventArgs>? NotificationReceived;

    /// <summary>
    /// Event raised when server publishes diagnostics
    /// </summary>
    event EventHandler<PublishDiagnosticsEventArgs>? DiagnosticsReceived;

    /// <summary>
    /// Notify server of document open
    /// </summary>
    Task DidOpenAsync(string uri, string languageId, int version, string text);

    /// <summary>
    /// Notify server of document change
    /// </summary>
    Task DidChangeAsync(string uri, int version, IReadOnlyList<TextDocumentContentChange> changes);

    /// <summary>
    /// Notify server of document close
    /// </summary>
    Task DidCloseAsync(string uri);

    /// <summary>
    /// Notify server of document save
    /// </summary>
    Task DidSaveAsync(string uri, string? text = null);

    /// <summary>
    /// Request completions at a position
    /// </summary>
    Task<CompletionList?> GetCompletionAsync(string uri, int line, int character, CancellationToken cancellationToken = default);

    /// <summary>
    /// Request hover information
    /// </summary>
    Task<Hover?> GetHoverAsync(string uri, int line, int character, CancellationToken cancellationToken = default);

    /// <summary>
    /// Request definition locations
    /// </summary>
    Task<IReadOnlyList<Location>?> GetDefinitionAsync(string uri, int line, int character, CancellationToken cancellationToken = default);

    /// <summary>
    /// Request references
    /// </summary>
    Task<IReadOnlyList<Location>?> GetReferencesAsync(string uri, int line, int character, bool includeDeclaration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Request document symbols
    /// </summary>
    Task<IReadOnlyList<DocumentSymbol>?> GetDocumentSymbolsAsync(string uri, CancellationToken cancellationToken = default);

    /// <summary>
    /// Request signature help
    /// </summary>
    Task<SignatureHelp?> GetSignatureHelpAsync(string uri, int line, int character, CancellationToken cancellationToken = default);

    /// <summary>
    /// Request code actions
    /// </summary>
    Task<IReadOnlyList<CodeAction>?> GetCodeActionsAsync(string uri, LspRange range, IReadOnlyList<Diagnostic>? diagnostics, CancellationToken cancellationToken = default);

    /// <summary>
    /// Request formatting for entire document
    /// </summary>
    Task<IReadOnlyList<TextEdit>?> FormatDocumentAsync(string uri, FormattingOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Request rename
    /// </summary>
    Task<WorkspaceEdit?> RenameAsync(string uri, int line, int character, string newName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Event args for LSP notifications
/// </summary>
public class LspNotificationEventArgs : EventArgs
{
    public string Method { get; set; } = "";
    public JsonElement? Parameters { get; set; }
}

/// <summary>
/// Event args for published diagnostics
/// </summary>
public class PublishDiagnosticsEventArgs : EventArgs
{
    public string Uri { get; set; } = "";
    public int? Version { get; set; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; set; } = Array.Empty<Diagnostic>();
}

/// <summary>
/// Text document content change for incremental sync
/// </summary>
public class TextDocumentContentChange
{
    public LspRange? Range { get; set; }
    public int? RangeLength { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>
/// Server capabilities returned from initialization
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
    public RenameOptions? RenameProvider { get; set; }
    public bool FoldingRangeProvider { get; set; }
}

public class TextDocumentSyncOptions
{
    public bool OpenClose { get; set; }
    public TextDocumentSyncKind Change { get; set; }
    public bool WillSave { get; set; }
    public bool WillSaveWaitUntil { get; set; }
    public SaveOptions? Save { get; set; }
}

public enum TextDocumentSyncKind
{
    None = 0,
    Full = 1,
    Incremental = 2
}

public class SaveOptions
{
    public bool IncludeText { get; set; }
}

public class CompletionOptions
{
    public List<string>? TriggerCharacters { get; set; }
    public bool ResolveProvider { get; set; }
}

public class SignatureHelpOptions
{
    public List<string>? TriggerCharacters { get; set; }
    public List<string>? RetriggerCharacters { get; set; }
}

public class RenameOptions
{
    public bool PrepareProvider { get; set; }
}

/// <summary>
/// LSP Position
/// </summary>
public class Position
{
    public int Line { get; set; }
    public int Character { get; set; }

    public Position() { }
    public Position(int line, int character)
    {
        Line = line;
        Character = character;
    }
}

/// <summary>
/// LSP Range (named LspRange to avoid conflict with System.Range)
/// </summary>
public class LspRange
{
    public Position Start { get; set; } = new();
    public Position End { get; set; } = new();

    public LspRange() { }
    public LspRange(Position start, Position end)
    {
        Start = start;
        End = end;
    }
    public LspRange(int startLine, int startChar, int endLine, int endChar)
    {
        Start = new Position(startLine, startChar);
        End = new Position(endLine, endChar);
    }
}

/// <summary>
/// LSP Location
/// </summary>
public class Location
{
    public string Uri { get; set; } = "";
    public LspRange Range { get; set; } = new();
}

/// <summary>
/// LSP Diagnostic
/// </summary>
public class Diagnostic
{
    public LspRange Range { get; set; } = new();
    public DiagnosticSeverity Severity { get; set; }
    public string? Code { get; set; }
    public string? Source { get; set; }
    public string Message { get; set; } = "";
    public List<DiagnosticRelatedInformation>? RelatedInformation { get; set; }
}

public enum DiagnosticSeverity
{
    Error = 1,
    Warning = 2,
    Information = 3,
    Hint = 4
}

public class DiagnosticRelatedInformation
{
    public Location Location { get; set; } = new();
    public string Message { get; set; } = "";
}

/// <summary>
/// LSP Hover result
/// </summary>
public class Hover
{
    public MarkupContent? Contents { get; set; }
    public LspRange? Range { get; set; }
}

public class MarkupContent
{
    public string Kind { get; set; } = "plaintext";
    public string Value { get; set; } = "";
}

/// <summary>
/// LSP Completion List
/// </summary>
public class CompletionList
{
    public bool IsIncomplete { get; set; }
    public List<CompletionItem> Items { get; set; } = new();
}

public class CompletionItem
{
    public string Label { get; set; } = "";
    public CompletionItemKind Kind { get; set; }
    public string? Detail { get; set; }
    public MarkupContent? Documentation { get; set; }
    public string? InsertText { get; set; }
    public InsertTextFormat InsertTextFormat { get; set; }
    public TextEdit? TextEdit { get; set; }
    public List<TextEdit>? AdditionalTextEdits { get; set; }
    public List<string>? CommitCharacters { get; set; }
    public string? SortText { get; set; }
    public string? FilterText { get; set; }
}

public enum CompletionItemKind
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

public enum InsertTextFormat
{
    PlainText = 1,
    Snippet = 2
}

public class TextEdit
{
    public LspRange Range { get; set; } = new();
    public string NewText { get; set; } = "";
}

/// <summary>
/// LSP Document Symbol
/// </summary>
public class DocumentSymbol
{
    public string Name { get; set; } = "";
    public string? Detail { get; set; }
    public SymbolKind Kind { get; set; }
    public LspRange Range { get; set; } = new();
    public LspRange SelectionRange { get; set; } = new();
    public List<DocumentSymbol>? Children { get; set; }
}

public enum SymbolKind
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
/// LSP Signature Help
/// </summary>
public class SignatureHelp
{
    public List<SignatureInformation> Signatures { get; set; } = new();
    public int? ActiveSignature { get; set; }
    public int? ActiveParameter { get; set; }
}

public class SignatureInformation
{
    public string Label { get; set; } = "";
    public MarkupContent? Documentation { get; set; }
    public List<ParameterInformation>? Parameters { get; set; }
}

public class ParameterInformation
{
    public string Label { get; set; } = "";
    public MarkupContent? Documentation { get; set; }
}

/// <summary>
/// LSP Code Action
/// </summary>
public class CodeAction
{
    public string Title { get; set; } = "";
    public CodeActionKind Kind { get; set; }
    public List<Diagnostic>? Diagnostics { get; set; }
    public bool IsPreferred { get; set; }
    public WorkspaceEdit? Edit { get; set; }
    public Command? Command { get; set; }
}

public enum CodeActionKind
{
    QuickFix,
    Refactor,
    RefactorExtract,
    RefactorInline,
    RefactorRewrite,
    Source,
    SourceOrganizeImports,
    SourceFixAll
}

public class Command
{
    public string Title { get; set; } = "";
    public string CommandId { get; set; } = "";
    public List<object>? Arguments { get; set; }
}

/// <summary>
/// LSP Workspace Edit
/// </summary>
public class WorkspaceEdit
{
    public Dictionary<string, List<TextEdit>>? Changes { get; set; }
}

/// <summary>
/// LSP Formatting Options
/// </summary>
public class FormattingOptions
{
    public int TabSize { get; set; } = 4;
    public bool InsertSpaces { get; set; } = true;
    public bool TrimTrailingWhitespace { get; set; }
    public bool InsertFinalNewline { get; set; }
    public bool TrimFinalNewlines { get; set; }
}
