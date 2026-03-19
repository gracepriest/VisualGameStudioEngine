namespace VisualGameStudio.Core.Models;

/// <summary>
/// Represents a code snippet with tab-stop placeholders, supporting both
/// built-in and user-defined snippets in VS Code snippet format.
/// </summary>
public class Snippet
{
    /// <summary>
    /// Display name of the snippet.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Trigger text that activates the snippet (e.g., "for", "if", "class").
    /// </summary>
    public string Prefix { get; set; } = "";

    /// <summary>
    /// Template lines with tab-stop markers ($1, ${1:default}, ${1|choice1,choice2|}, $0).
    /// </summary>
    public string[] Body { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Human-readable description shown in IntelliSense and the snippet manager.
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Language scope (e.g., "basiclang"). Empty means all languages.
    /// </summary>
    public string Scope { get; set; } = "basiclang";

    /// <summary>
    /// True for built-in snippets that cannot be deleted by the user.
    /// </summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// Source file path, or "built-in" for built-in snippets.
    /// </summary>
    public string Source { get; set; } = "built-in";

    /// <summary>
    /// Category for grouping in the snippet manager (e.g., "Control Flow", "Declarations").
    /// </summary>
    public string Category { get; set; } = "General";

    /// <summary>
    /// Returns the body joined as a single string with newline separators.
    /// </summary>
    public string BodyText => string.Join("\n", Body);
}
