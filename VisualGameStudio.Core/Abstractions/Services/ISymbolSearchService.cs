namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Provides symbol search capabilities for "Go to Symbol" and similar functionality.
/// </summary>
public interface ISymbolSearchService
{
    /// <summary>
    /// Searches for symbols matching the query in a single file.
    /// </summary>
    /// <param name="sourceCode">The source code to search.</param>
    /// <param name="query">The search query (can be partial match).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of matching symbols.</returns>
    Task<IReadOnlyList<SymbolSearchResult>> SearchInFileAsync(string sourceCode, string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for symbols in multiple files within a project.
    /// </summary>
    /// <param name="filePaths">The file paths to search.</param>
    /// <param name="query">The search query.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of matching symbols with file information.</returns>
    Task<IReadOnlyList<SymbolSearchResult>> SearchInProjectAsync(IEnumerable<string> filePaths, string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all symbols defined in a file (for outline/structure view).
    /// </summary>
    /// <param name="sourceCode">The source code to analyze.</param>
    /// <returns>A hierarchical list of symbols in the file.</returns>
    IReadOnlyList<SymbolInfo> GetFileSymbols(string sourceCode);

    /// <summary>
    /// Finds the symbol at a specific location in the source code.
    /// </summary>
    /// <param name="sourceCode">The source code.</param>
    /// <param name="line">The line number (1-based).</param>
    /// <param name="column">The column number (1-based).</param>
    /// <returns>The symbol at the location, or null if none found.</returns>
    SymbolInfo? GetSymbolAtLocation(string sourceCode, int line, int column);

    /// <summary>
    /// Gets the breadcrumb path for a location (e.g., "MyClass > MyMethod > If block").
    /// </summary>
    /// <param name="sourceCode">The source code.</param>
    /// <param name="line">The line number (1-based).</param>
    /// <returns>A list of containing symbols from outermost to innermost.</returns>
    IReadOnlyList<SymbolInfo> GetBreadcrumb(string sourceCode, int line);
}

/// <summary>
/// Represents a symbol search result.
/// </summary>
public class SymbolSearchResult
{
    /// <summary>
    /// Gets or sets the symbol information.
    /// </summary>
    public SymbolInfo Symbol { get; set; } = new();

    /// <summary>
    /// Gets or sets the file path where the symbol was found.
    /// </summary>
    public string FilePath { get; set; } = "";

    /// <summary>
    /// Gets or sets the relevance score (higher = better match).
    /// </summary>
    public int Score { get; set; }

    /// <summary>
    /// Gets or sets the matched portion of the symbol name.
    /// </summary>
    public string MatchedText { get; set; } = "";
}

/// <summary>
/// Represents information about a symbol in the code.
/// </summary>
public class SymbolInfo
{
    /// <summary>
    /// Gets or sets the symbol name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the fully qualified name (e.g., "MyClass.MyMethod").
    /// </summary>
    public string FullName { get; set; } = "";

    /// <summary>
    /// Gets or sets the symbol kind.
    /// </summary>
    public SymbolKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the line number where the symbol starts (1-based).
    /// </summary>
    public int StartLine { get; set; }

    /// <summary>
    /// Gets or sets the column number where the symbol starts (1-based).
    /// </summary>
    public int StartColumn { get; set; }

    /// <summary>
    /// Gets or sets the line number where the symbol ends (1-based).
    /// </summary>
    public int EndLine { get; set; }

    /// <summary>
    /// Gets or sets the column number where the symbol ends (1-based).
    /// </summary>
    public int EndColumn { get; set; }

    /// <summary>
    /// Gets or sets the container symbol (parent class/module).
    /// </summary>
    public string? ContainerName { get; set; }

    /// <summary>
    /// Gets or sets the access modifier.
    /// </summary>
    public AccessModifier AccessModifier { get; set; }

    /// <summary>
    /// Gets or sets the signature (for methods/functions).
    /// </summary>
    public string? Signature { get; set; }

    /// <summary>
    /// Gets or sets the return type (for functions/properties).
    /// </summary>
    public string? ReturnType { get; set; }

    /// <summary>
    /// Gets or sets child symbols (for classes/modules containing methods).
    /// </summary>
    public List<SymbolInfo> Children { get; set; } = new();
}

// Note: SymbolKind enum is defined in ILanguageService.cs

/// <summary>
/// Access modifiers for symbols.
/// </summary>
public enum AccessModifier
{
    /// <summary>No explicit modifier (default access).</summary>
    None,
    /// <summary>Public access.</summary>
    Public,
    /// <summary>Private access.</summary>
    Private,
    /// <summary>Protected access.</summary>
    Protected,
    /// <summary>Friend (internal) access.</summary>
    Friend,
    /// <summary>Protected Friend access.</summary>
    ProtectedFriend
}
