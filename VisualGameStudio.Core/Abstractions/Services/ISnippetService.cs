namespace VisualGameStudio.Core.Abstractions.Services;

public interface ISnippetService
{
    IReadOnlyList<CodeSnippet> GetSnippets();
    IReadOnlyList<CodeSnippet> SearchSnippets(string query);
    CodeSnippet? GetSnippet(string shortcut);
    string ExpandSnippet(CodeSnippet snippet, Dictionary<string, string>? variables = null);
    void RegisterSnippet(CodeSnippet snippet);
    void RemoveSnippet(string shortcut);
}

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
