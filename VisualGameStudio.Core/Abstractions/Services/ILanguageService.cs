using VisualGameStudio.Core.Models;

namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Language service interface for LSP-based IntelliSense features
/// </summary>
public interface ILanguageService : IDisposable
{
    /// <summary>
    /// Whether the language server is connected and ready
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Fires when connection state changes
    /// </summary>
    event EventHandler<bool>? ConnectionChanged;

    /// <summary>
    /// Fires when diagnostics are received for a document
    /// </summary>
    event EventHandler<DiagnosticsEventArgs>? DiagnosticsReceived;

    /// <summary>
    /// Start the language server
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop the language server
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Notify the server that a document was opened
    /// </summary>
    Task OpenDocumentAsync(string uri, string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Notify the server that a document was changed
    /// </summary>
    Task ChangeDocumentAsync(string uri, string text, int version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Notify the server that a document was closed
    /// </summary>
    Task CloseDocumentAsync(string uri, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get completions at a position
    /// </summary>
    Task<IReadOnlyList<CompletionItem>> GetCompletionsAsync(string uri, int line, int column, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get hover information at a position
    /// </summary>
    Task<HoverInfo?> GetHoverAsync(string uri, int line, int column, CancellationToken cancellationToken = default);

    /// <summary>
    /// Go to definition
    /// </summary>
    Task<LocationInfo?> GetDefinitionAsync(string uri, int line, int column, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find all references
    /// </summary>
    Task<IReadOnlyList<LocationInfo>> FindReferencesAsync(string uri, int line, int column, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get document symbols (outline)
    /// </summary>
    Task<IReadOnlyList<DocumentSymbol>> GetDocumentSymbolsAsync(string uri, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get signature help at a position
    /// </summary>
    Task<SignatureHelp?> GetSignatureHelpAsync(string uri, int line, int column, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get supertypes (base classes/interfaces) for a type at the given position
    /// </summary>
    Task<IReadOnlyList<TypeHierarchyItemInfo>> GetSupertypesAsync(string uri, int line, int column, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get subtypes (derived classes) for a type at the given position
    /// </summary>
    Task<IReadOnlyList<TypeHierarchyItemInfo>> GetSubtypesAsync(string uri, int line, int column, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get incoming calls (callers) for a method at the given position
    /// </summary>
    Task<IReadOnlyList<CallHierarchyItemInfo>> GetIncomingCallsAsync(string uri, int line, int column, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get outgoing calls (callees) for a method at the given position
    /// </summary>
    Task<IReadOnlyList<CallHierarchyItemInfo>> GetOutgoingCallsAsync(string uri, int line, int column, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rename a symbol at the given position
    /// </summary>
    Task<WorkspaceEditInfo?> RenameAsync(string uri, int line, int column, string newName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get code actions (quick fixes, refactorings) for a range
    /// </summary>
    Task<IReadOnlyList<CodeActionInfo>> GetCodeActionsAsync(string uri, int startLine, int startColumn, int endLine, int endColumn, IReadOnlyList<DiagnosticItem>? diagnostics = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Format the entire document
    /// </summary>
    Task<IReadOnlyList<TextEditInfo>> FormatDocumentAsync(string uri, FormattingOptionsInfo? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Format a range of the document
    /// </summary>
    Task<IReadOnlyList<TextEditInfo>> FormatRangeAsync(string uri, int startLine, int startColumn, int endLine, int endColumn, FormattingOptionsInfo? options = null, CancellationToken cancellationToken = default);
}

public class DiagnosticsEventArgs : EventArgs
{
    public string Uri { get; set; } = "";
    public IReadOnlyList<DiagnosticItem> Diagnostics { get; set; } = Array.Empty<DiagnosticItem>();
}

public class CompletionItem
{
    public string Label { get; set; } = "";
    public string? Detail { get; set; }
    public string? Documentation { get; set; }
    public CompletionItemKind Kind { get; set; }
    public string? InsertText { get; set; }
    public string? FilterText { get; set; }
    public string? SortText { get; set; }
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

public class HoverInfo
{
    public string Contents { get; set; } = "";
    public int StartLine { get; set; }
    public int StartColumn { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
}

public class LocationInfo
{
    public string Uri { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
}

public class DocumentSymbol
{
    public string Name { get; set; } = "";
    public string? Detail { get; set; }
    public SymbolKind Kind { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
    public List<DocumentSymbol> Children { get; set; } = new();
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

public class SignatureHelp
{
    public List<SignatureInfo> Signatures { get; set; } = new();
    public int ActiveSignature { get; set; }
    public int ActiveParameter { get; set; }
}

public class SignatureInfo
{
    public string Label { get; set; } = "";
    public string? Documentation { get; set; }
    public List<ParameterInfo> Parameters { get; set; } = new();
}

public class ParameterInfo
{
    public string Label { get; set; } = "";
    public string? Documentation { get; set; }
}

/// <summary>
/// Information about a type in the type hierarchy
/// </summary>
public class TypeHierarchyItemInfo
{
    public string Name { get; set; } = "";
    public HierarchyTypeKind Kind { get; set; }
    public string? Detail { get; set; }
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
}

/// <summary>
/// Kind of type in the hierarchy
/// </summary>
public enum HierarchyTypeKind
{
    Class,
    Interface,
    Struct,
    Module
}

/// <summary>
/// Information about a callable in the call hierarchy
/// </summary>
public class CallHierarchyItemInfo
{
    public string Name { get; set; } = "";
    public HierarchyCallableKind Kind { get; set; }
    public string? Detail { get; set; }
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public List<CallSiteItemInfo> CallSites { get; set; } = new();
}

/// <summary>
/// Kind of callable in the hierarchy
/// </summary>
public enum HierarchyCallableKind
{
    Function,
    Method,
    Subroutine,
    Constructor,
    Property
}

/// <summary>
/// Information about a specific call site
/// </summary>
public class CallSiteItemInfo
{
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public string? Preview { get; set; }
}

/// <summary>
/// Workspace edit containing changes across multiple files
/// </summary>
public class WorkspaceEditInfo
{
    public Dictionary<string, List<TextEditInfo>> Changes { get; set; } = new();
}

/// <summary>
/// A single text edit
/// </summary>
public class TextEditInfo
{
    public int StartLine { get; set; }
    public int StartColumn { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
    public string NewText { get; set; } = "";
}

/// <summary>
/// Code action (quick fix or refactoring)
/// </summary>
public class CodeActionInfo
{
    public string Title { get; set; } = "";
    public CodeActionKind Kind { get; set; }
    public bool IsPreferred { get; set; }
    public WorkspaceEditInfo? Edit { get; set; }
    public List<DiagnosticItem>? Diagnostics { get; set; }
}

/// <summary>
/// Formatting options
/// </summary>
public class FormattingOptionsInfo
{
    public int TabSize { get; set; } = 4;
    public bool InsertSpaces { get; set; } = true;
}
