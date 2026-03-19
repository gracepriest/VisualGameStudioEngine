using VisualGameStudio.Core.Models;

namespace VisualGameStudio.Core.Abstractions.Services;

public interface ISnippetService
{
    /// <summary>
    /// Get all snippets (built-in + user) for a given language scope.
    /// </summary>
    IReadOnlyList<Snippet> GetSnippets(string language = "basiclang");

    /// <summary>
    /// Get all snippets across all languages.
    /// </summary>
    IReadOnlyList<Snippet> GetAllSnippets();

    /// <summary>
    /// Search snippets by query (matches name, prefix, description).
    /// </summary>
    IReadOnlyList<Snippet> SearchSnippets(string query, string? language = null);

    /// <summary>
    /// Find a snippet by exact prefix match.
    /// </summary>
    Snippet? GetSnippetByPrefix(string prefix, string language = "basiclang");

    /// <summary>
    /// Find snippets whose prefix starts with the given text.
    /// </summary>
    IReadOnlyList<Snippet> FindByPrefixStart(string prefixStart, string language = "basiclang");

    /// <summary>
    /// Add a user-defined snippet for a language.
    /// Persists to ~/.vgs/snippets/{language}.json.
    /// </summary>
    void AddUserSnippet(string language, Snippet snippet);

    /// <summary>
    /// Update an existing user snippet (matched by name).
    /// </summary>
    void UpdateUserSnippet(string language, Snippet snippet);

    /// <summary>
    /// Remove a user-defined snippet.
    /// </summary>
    void RemoveUserSnippet(string language, string name);

    /// <summary>
    /// Load snippets from all sources (built-in + user files + project files).
    /// </summary>
    void LoadSnippets();

    /// <summary>
    /// Load user snippets from a VS Code format JSON file.
    /// </summary>
    IReadOnlyList<Snippet> LoadSnippetsFromFile(string filePath, string language);

    /// <summary>
    /// Save user snippets to a VS Code format JSON file.
    /// </summary>
    void SaveUserSnippets(string language);

    /// <summary>
    /// Expand a snippet body, replacing VS Code variables ($TM_FILENAME, $CURRENT_YEAR, etc.)
    /// and returning the text with tab-stop markers intact for the editor to process.
    /// </summary>
    string ExpandSnippet(Snippet snippet, Dictionary<string, string>? variables = null);

    /// <summary>
    /// Import snippets from a VS Code snippet JSON file.
    /// </summary>
    IReadOnlyList<Snippet> ImportSnippets(string filePath, string language);

    /// <summary>
    /// Export snippets to a VS Code snippet JSON file.
    /// </summary>
    void ExportSnippets(string filePath, IEnumerable<Snippet> snippets);

    /// <summary>
    /// Get the path to the user snippets directory.
    /// </summary>
    string GetUserSnippetsDirectory();

    /// <summary>
    /// Raised when the snippet collection changes (add/remove/update).
    /// </summary>
    event EventHandler? SnippetsChanged;
}

/// <summary>
/// Legacy model kept for backward compatibility with existing tests.
/// </summary>
public class CodeSnippet
{
    public string Shortcut { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Body { get; set; } = "";
    public string Category { get; set; } = "General";
    public IReadOnlyList<SnippetVariable> Variables { get; set; } = Array.Empty<SnippetVariable>();
}

public class SnippetVariable
{
    public string Name { get; set; } = "";
    public string DefaultValue { get; set; } = "";
    public string? Description { get; set; }
}
