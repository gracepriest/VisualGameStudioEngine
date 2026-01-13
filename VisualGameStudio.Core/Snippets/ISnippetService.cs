namespace VisualGameStudio.Core.Snippets;

/// <summary>
/// Service for managing code snippets
/// </summary>
public interface ISnippetService
{
    /// <summary>
    /// Load snippets from a VS Code snippet file
    /// </summary>
    Task<IReadOnlyList<Snippet>> LoadSnippetsFromFileAsync(string snippetPath, string languageId);

    /// <summary>
    /// Load snippets from JSON content
    /// </summary>
    IReadOnlyList<Snippet> LoadSnippetsFromJson(string jsonContent, string languageId);

    /// <summary>
    /// Get all snippets for a language
    /// </summary>
    IReadOnlyList<Snippet> GetSnippetsForLanguage(string languageId);

    /// <summary>
    /// Get snippets matching a prefix
    /// </summary>
    IReadOnlyList<Snippet> GetSnippetsByPrefix(string languageId, string prefix);

    /// <summary>
    /// Register a snippet
    /// </summary>
    void RegisterSnippet(Snippet snippet);

    /// <summary>
    /// Register multiple snippets
    /// </summary>
    void RegisterSnippets(IEnumerable<Snippet> snippets);

    /// <summary>
    /// Get all languages with snippets
    /// </summary>
    IReadOnlyList<string> GetLanguagesWithSnippets();

    /// <summary>
    /// Expand a snippet body with placeholder values
    /// </summary>
    string ExpandSnippet(Snippet snippet, Dictionary<string, string>? placeholderValues = null);
}

/// <summary>
/// A code snippet
/// </summary>
public class Snippet
{
    /// <summary>
    /// Snippet name
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Prefix that triggers the snippet
    /// </summary>
    public string Prefix { get; set; } = "";

    /// <summary>
    /// Alternative prefixes
    /// </summary>
    public List<string> AlternatePrefixes { get; set; } = new();

    /// <summary>
    /// The snippet body (with placeholders)
    /// </summary>
    public string Body { get; set; } = "";

    /// <summary>
    /// Description of the snippet
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Language ID this snippet applies to
    /// </summary>
    public string LanguageId { get; set; } = "";

    /// <summary>
    /// Source extension that provided this snippet
    /// </summary>
    public string? SourceExtension { get; set; }

    /// <summary>
    /// Scope/context where the snippet is valid
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>
    /// Parsed placeholders in the body
    /// </summary>
    public List<SnippetPlaceholder> Placeholders { get; set; } = new();

    /// <summary>
    /// Whether this is a file template snippet
    /// </summary>
    public bool IsFileTemplate { get; set; }
}

/// <summary>
/// A placeholder in a snippet
/// </summary>
public class SnippetPlaceholder
{
    /// <summary>
    /// Placeholder index (1, 2, 3, etc.)
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Default value
    /// </summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// Placeholder name
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Choice values (for choice placeholders)
    /// </summary>
    public List<string>? Choices { get; set; }

    /// <summary>
    /// Transform to apply
    /// </summary>
    public string? Transform { get; set; }

    /// <summary>
    /// Position in the body
    /// </summary>
    public int StartPosition { get; set; }

    /// <summary>
    /// Length in the body
    /// </summary>
    public int Length { get; set; }
}
