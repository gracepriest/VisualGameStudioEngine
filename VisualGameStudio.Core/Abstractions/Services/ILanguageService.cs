using System.Text.Json;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Language service interface for LSP-based IntelliSense features
/// </summary>
public interface ILanguageService : IDisposable
{
    /// <summary>
    /// Which server this instance drives: what to launch, which files it owns, which
    /// <c>languageId</c> to announce them with. Fixed for the lifetime of the service.
    /// <para>
    /// On the interface because routing is a decision made ABOUT a service from outside it —
    /// <see cref="ILanguageServiceRegistry"/> asks each service who it is in order to hand it
    /// the files it owns, and callers read <see cref="LanguageServerDescriptor.DisplayName"/>
    /// to name the right server in a message. Never null.
    /// </para>
    /// </summary>
    LanguageServerDescriptor Descriptor { get; }

    /// <summary>
    /// Whether the language server is connected and ready.
    /// <para>
    /// ⚠ <b>Per SERVER, not global.</b> With more than one server registered, "the language
    /// server is down" is not a fact about the IDE — BasicLang can be restarting while clangd
    /// answers normally. A caller holding one <see cref="ILanguageService"/> is asking about
    /// THAT server only; to ask about the server that owns a given file, use
    /// <see cref="ILanguageServiceRegistry.IsConnectedFor"/>.
    /// </para>
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// What the connected server reported it can do in its <c>initialize</c> result.
    /// <para>
    /// <b>Null whenever <see cref="IsConnected"/> is false</b> — not merely before the
    /// first handshake, but any time the service is disconnected, stopped, crashed or
    /// disposed. Callers must therefore re-check it rather than caching the value or
    /// null-checking only at startup: a disconnected server supports nothing.
    /// </para>
    /// Without this, "the server does not support X" is indistinguishable from "the
    /// server supports X and the answer is empty" — every feature method here returns
    /// an empty list on failure.
    /// </summary>
    ServerCapabilities? Capabilities { get; }

    /// <summary>
    /// Fires when connection state changes
    /// </summary>
    event EventHandler<bool>? ConnectionChanged;

    /// <summary>
    /// Fires when diagnostics are received for a document
    /// </summary>
    event EventHandler<DiagnosticsEventArgs>? DiagnosticsReceived;

    /// <summary>
    /// Start the language server, rooted at <paramref name="workspaceRoot"/>.
    /// </summary>
    /// <param name="workspaceRoot">
    /// Local directory path the server should treat as the workspace — sent as
    /// <c>rootUri</c>/<c>rootPath</c>/<c>workspaceFolders</c> and used as the server
    /// process's working directory. Null means "no workspace is open", under which the
    /// server resolves nothing against a project.
    /// <para>
    /// ⚠ Only honoured by the call that actually STARTS the server: this is a no-op
    /// when a server is already connected, so passing a root to a running service does
    /// NOT re-root it. A workspace opened after the server started needs a restart.
    /// </para>
    /// </param>
    Task StartAsync(string? workspaceRoot = null, CancellationToken cancellationToken = default);

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
    /// Notify the server that a document was saved
    /// </summary>
    Task SaveDocumentAsync(string uri, string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get completions at a position
    /// </summary>
    Task<IReadOnlyList<CompletionItem>> GetCompletionsAsync(string uri, int line, int column, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolve a completion item's lazy details via <c>completionItem/resolve</c>: servers
    /// that advertise <see cref="ServerCapabilities.HasCompletionResolveProvider"/> defer
    /// expensive fields (documentation, detail) until an item is actually highlighted, keying
    /// the lookup off the opaque <see cref="CompletionItem.Data"/> token they attached.
    /// <para>
    /// Returns an ENRICHED COPY when the server supplied documentation/detail, otherwise the
    /// <b>original instance</b> — when the server never advertised the provider (real clangd
    /// 22 does not), when the item carries no <see cref="CompletionItem.Data"/>, when the
    /// reply adds nothing, or when the request fails. Resolve is an enrichment, never a gate:
    /// no failure here may make an already-listed item worse. The one exception is
    /// caller-initiated cancellation, which rethrows so "you cancelled it" stays
    /// distinguishable from "the server had nothing".
    /// </para>
    /// </summary>
    Task<CompletionItem> ResolveCompletionAsync(CompletionItem item, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get hover information at a position
    /// </summary>
    Task<HoverInfo?> GetHoverAsync(string uri, int line, int column, CancellationToken cancellationToken = default);

    /// <summary>
    /// Go to definition
    /// </summary>
    Task<LocationInfo?> GetDefinitionAsync(string uri, int line, int column, CancellationToken cancellationToken = default);

    /// <summary>
    /// Go to implementation
    /// </summary>
    Task<LocationInfo?> GetImplementationAsync(string uri, int line, int column, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Format on type - triggered after the user types a character (e.g., Enter, End Sub, End If)
    /// </summary>
    Task<IReadOnlyList<TextEditInfo>> OnTypeFormattingAsync(string uri, int line, int column, string ch, FormattingOptionsInfo? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get code lenses for the document
    /// </summary>
    Task<IReadOnlyList<CodeLensInfo>> GetCodeLensAsync(string uri, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get type definition location (go to the type of a variable/parameter)
    /// </summary>
    Task<LocationInfo?> GetTypeDefinitionAsync(string uri, int line, int column, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get semantic tokens for the full document
    /// </summary>
    Task<SemanticTokensResult?> GetSemanticTokensAsync(string uri, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get inlay hints for a range
    /// </summary>
    Task<IReadOnlyList<InlayHintInfo>> GetInlayHintsAsync(string uri, int startLine, int startColumn, int endLine, int endColumn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get selection ranges at positions (smart expand/shrink selection)
    /// </summary>
    Task<IReadOnlyList<SelectionRangeInfo>> GetSelectionRangesAsync(string uri, IReadOnlyList<(int line, int column)> positions, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get document highlights (occurrences of symbol under cursor)
    /// </summary>
    Task<IReadOnlyList<DocumentHighlightResult>> GetDocumentHighlightsAsync(string uri, int line, int column, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get document links (clickable imports, file paths, URLs)
    /// </summary>
    Task<IReadOnlyList<DocumentLinkInfo>> GetDocumentLinksAsync(string uri, CancellationToken cancellationToken = default);

    /// <summary>
    /// Search for symbols across the entire workspace using the LSP workspace/symbol request
    /// </summary>
    Task<IReadOnlyList<WorkspaceSymbolInfo>> GetWorkspaceSymbolsAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get folding ranges for the document (regions, blocks, imports, comments)
    /// </summary>
    Task<IReadOnlyList<FoldingRangeInfo>> GetFoldingRangesAsync(string uri, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get linked editing ranges at a position (e.g., synchronized variable renaming)
    /// </summary>
    Task<LinkedEditingRangeResult?> GetLinkedEditingRangesAsync(string uri, int line, int column, CancellationToken cancellationToken = default);
}

/// <summary>
/// The subset of an LSP <c>initialize</c> result this client acts on: which of the
/// features it advertises the server actually provides, plus the position encoding
/// the server picked. Deliberately NOT a mirror of the whole LSP spec — it carries
/// what callers need to make a decision, and grows only when a caller needs more.
/// </summary>
public record ServerCapabilities
{
    /// <summary>
    /// The only position encoding this client can correctly consume, and therefore
    /// the only one it offers. LSP positions are UTF-16 code units and this client
    /// converts them as <c>character = column - 1</c> against AvaloniaEdit's
    /// <c>Caret.Column</c>, which is a 1-based UTF-16 code-unit index. Any other
    /// encoding silently shifts every position on every non-ASCII line.
    /// It is also the LSP 3.17 default when a server omits <c>positionEncoding</c>.
    /// </summary>
    public const string Utf16 = "utf-16";

    public bool HasCompletionProvider { get; init; }

    /// <summary>
    /// Server implements <c>completionItem/resolve</c>. BasicLang's real <c>--lsp</c>
    /// server reports true (and serves lazy documentation through it); <b>real clangd 22
    /// reports FALSE</b> (measured, Phase 3b Step 0.3 — an earlier version of this comment
    /// claimed clangd true, from assumption), as does the <c>--lsp-simple</c> fallback.
    /// <see cref="ILanguageService.ResolveCompletionAsync"/> gates off cleanly on this
    /// flag, so false costs nothing but the laziness.
    /// </summary>
    public bool HasCompletionResolveProvider { get; init; }

    public bool HasHoverProvider { get; init; }
    public bool HasDefinitionProvider { get; init; }
    public bool HasReferencesProvider { get; init; }
    public bool HasDocumentSymbolProvider { get; init; }
    public bool HasSignatureHelpProvider { get; init; }

    /// <summary>
    /// Server implements <c>textDocument/semanticTokens</c> (LSP types it as
    /// <c>boolean | SemanticTokensOptions</c>; both forms count, like every other
    /// provider here). True with a null <see cref="SemanticTokensLegend"/> means the
    /// feature exists but the handshake carried no legend to decode its indices with —
    /// supported and decodable are different claims.
    /// </summary>
    public bool HasSemanticTokensProvider { get; init; }

    /// <summary>
    /// The name tables this server's semantic-token integers index into, verbatim from
    /// the <c>initialize</c> reply — or null when the reply carried none. Consumed by
    /// the per-server index remap wired at the token fetch seam (Phase 3b Task 9):
    /// token data is defined against THIS reply's legend and nothing else, so a
    /// consumer without it can only guess with a hardcoded table.
    /// See <see cref="VisualGameStudio.Core.Abstractions.Services.SemanticTokensLegend"/>
    /// for the duplicate-names reality.
    /// </summary>
    public SemanticTokensLegend? SemanticTokensLegend { get; init; }

    /// <summary>
    /// The encoding the server picked from the client's offered list, verbatim.
    /// Defaults to <see cref="Utf16"/> when the server omits it, per LSP 3.17.
    /// </summary>
    public string PositionEncoding { get; init; } = Utf16;

    /// <summary>
    /// clangd's own <c>offsetEncoding</c>, verbatim — a <b>top-level sibling</b> of the
    /// <c>capabilities</c> object in the <c>initialize</c> result, not a member of it.
    /// <b>Null when the server did not send it</b>, which is every spec-conformant server
    /// (BasicLang's included); only clangd does.
    ///
    /// <para>
    /// Non-standard: it predates LSP 3.17's <see cref="PositionEncoding"/>, and real clangd 22.1.6
    /// sends BOTH — in agreement, including when an <c>--offset-encoding</c> flag overrides the
    /// negotiation (measured; both fields moved together). It is captured anyway because it is the
    /// field clangd's own semantics are defined against, so it is where a divergence would surface
    /// if a future version ever moved one without the other. See
    /// <c>LanguageService.DescribeEncodingMismatch</c>.
    /// </para>
    ///
    /// <para>
    /// ⚠ Null must never be read as "utf-16". "The server never said" and "the server said
    /// utf-16" are different claims, and only the second one has been verified.
    /// </para>
    /// </summary>
    public string? OffsetEncoding { get; init; }
}

/// <summary>
/// The index tables an LSP server's semantic-token integers decode against: a token's
/// type is an index into <see cref="TokenTypes"/>, its modifiers a bit set over
/// <see cref="TokenModifiers"/>. Captured verbatim from the server's <c>initialize</c>
/// reply — position IS the contract, so no dedup and no normalization, ever.
/// </summary>
/// <remarks>
/// ⚠ Names REPEAT. Real clangd 22.1.6 sends 25 tokenTypes in which "variable" sits at
/// [0], [1] and [7], "type" at [12], [13] and [18], and "function" at [3] and [5]
/// (measured, Phase 3b Step 0.2). These are positional wire tables, not sets: a
/// name-keyed <c>Dictionary.Add</c> over them THROWS, and deduplicating silently
/// shifts every index after the first duplicate. A consumer that wants a name-keyed
/// map owns duplicate tolerance; this record's only job is to carry the arrays
/// exactly as the server defined them.
/// </remarks>
/// <param name="TokenTypes">Token-type names in wire order; a token's type integer is
/// an index into this list.</param>
/// <param name="TokenModifiers">Modifier names in wire order; bit <c>N</c> of a
/// token's modifier integer refers to entry <c>N</c> of this list.</param>
public sealed record SemanticTokensLegend(
    IReadOnlyList<string> TokenTypes,
    IReadOnlyList<string> TokenModifiers);

/// <summary>
/// A symbol found via workspace/symbol search
/// </summary>
public class WorkspaceSymbolInfo
{
    public string Name { get; set; } = "";
    public SymbolKind Kind { get; set; }
    public string ContainerName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
}

public class DiagnosticsEventArgs : EventArgs
{
    public string Uri { get; set; } = "";
    public IReadOnlyList<DiagnosticItem> Diagnostics { get; set; } = Array.Empty<DiagnosticItem>();
}

/// <summary>
/// An LSP completion item in the Core model.
/// ⚠ Adding a property? Update <c>LanguageService.MergeResolvedCompletion</c>'s
/// field-by-field copy too — this is a class, not a record, so no compiler forces that
/// copy to stay complete, and a missed field silently reverts to its default on resolve.
/// </summary>
public class CompletionItem
{
    public string Label { get; set; } = "";
    public string? Detail { get; set; }
    public string? Documentation { get; set; }
    public CompletionItemKind Kind { get; set; }
    public string? InsertText { get; set; }
    public string? FilterText { get; set; }
    public string? SortText { get; set; }

    /// <summary>
    /// How <see cref="InsertText"/> should be interpreted on commit. When
    /// <see cref="InsertTextFormat.Snippet"/>, the text contains LSP snippet
    /// syntax ($0, $N, ${N:default}) that must be expanded into tab stops,
    /// never inserted literally.
    /// </summary>
    public InsertTextFormat InsertTextFormat { get; set; } = InsertTextFormat.PlainText;

    /// <summary>
    /// When true, the server wants this item initially selected in the list.
    /// </summary>
    public bool Preselect { get; set; }

    /// <summary>
    /// The server's OPAQUE resolve token, verbatim from the completion reply — the key
    /// <see cref="ILanguageService.ResolveCompletionAsync"/> echoes back in
    /// <c>completionItem/resolve</c> so the server can find the item again (BasicLang's
    /// <c>--lsp</c> stashes type+member here for lazy .NET docs). Null when the server sent
    /// none (or sent JSON null), which gates resolve off for this item.
    /// <para>
    /// Never inspected, never rewritten: its shape belongs to the server. Cloned at parse
    /// (<c>JsonElement.Clone()</c>) so it stays readable after the reply's backing
    /// <c>JsonDocument</c> is gone.
    /// </para>
    /// </summary>
    public JsonElement? Data { get; init; }
}

/// <summary>
/// How a completion item's insert text should be interpreted on commit.
/// </summary>
public enum InsertTextFormat
{
    PlainText = 1,
    Snippet = 2
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

/// <summary>
/// Code lens information
/// </summary>
public class CodeLensInfo
{
    public int Line { get; set; }
    public int Column { get; set; }
    public string Title { get; set; } = "";
    public string CommandName { get; set; } = "";
    public List<object>? CommandArguments { get; set; }
}

/// <summary>
/// Semantic tokens result from LSP
/// </summary>
public class SemanticTokensResult
{
    /// <summary>
    /// Encoded token data: [deltaLine, deltaStartChar, length, tokenType, tokenModifiers] * N
    /// </summary>
    public int[] Data { get; set; } = Array.Empty<int>();
    public string? ResultId { get; set; }
}

/// <summary>
/// Inlay hint information
/// </summary>
public class InlayHintInfo
{
    public int Line { get; set; }
    public int Column { get; set; }
    public string Label { get; set; } = "";
    public InlayHintKind Kind { get; set; }
    public bool PaddingLeft { get; set; }
    public bool PaddingRight { get; set; }
}

public enum InlayHintKind
{
    Type = 1,
    Parameter = 2
}

/// <summary>
/// Selection range info (nested ranges for smart expand/shrink)
/// </summary>
public class SelectionRangeInfo
{
    public int StartLine { get; set; }
    public int StartColumn { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
    public SelectionRangeInfo? Parent { get; set; }
}

/// <summary>
/// Document highlight result (symbol occurrences)
/// </summary>
public class DocumentHighlightResult
{
    public int StartLine { get; set; }
    public int StartColumn { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
    public DocumentHighlightKind Kind { get; set; }
}

public enum DocumentHighlightKind
{
    Text = 1,
    Read = 2,
    Write = 3
}

/// <summary>
/// Document link information (clickable imports, file paths, URLs)
/// </summary>
public class DocumentLinkInfo
{
    public int StartLine { get; set; }
    public int StartColumn { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
    public string Target { get; set; } = "";
    public string? Tooltip { get; set; }
}

/// <summary>
/// Folding range information from LSP textDocument/foldingRange
/// </summary>
public class FoldingRangeInfo
{
    /// <summary>Start line (1-based)</summary>
    public int StartLine { get; set; }
    /// <summary>End line (1-based)</summary>
    public int EndLine { get; set; }
    /// <summary>Optional kind: "comment", "imports", "region", or null for code blocks</summary>
    public string? Kind { get; set; }
}

/// <summary>
/// Result of a linked editing range request (textDocument/linkedEditingRange)
/// </summary>
public class LinkedEditingRangeResult
{
    /// <summary>
    /// The ranges that are linked together. Editing one should update all others.
    /// </summary>
    public List<LinkedEditingRange> Ranges { get; set; } = new();

    /// <summary>
    /// Optional word pattern (regex) that describes valid contents for the ranges.
    /// </summary>
    public string? WordPattern { get; set; }
}

/// <summary>
/// A single range within a linked editing range result
/// </summary>
public class LinkedEditingRange
{
    public int StartLine { get; set; }
    public int StartColumn { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
}
