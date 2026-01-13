using System.Text.Json;
using System.Text.Json.Serialization;

namespace VisualGameStudio.Core.Extensions;

/// <summary>
/// Represents a VS Code extension that can be imported into VGS
/// </summary>
public class VSCodeExtension
{
    /// <summary>
    /// Extension ID (publisher.name)
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Display name
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Publisher name
    /// </summary>
    public string Publisher { get; set; } = "";

    /// <summary>
    /// Version
    /// </summary>
    public string Version { get; set; } = "";

    /// <summary>
    /// Description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Path to the extension directory
    /// </summary>
    public string ExtensionPath { get; set; } = "";

    /// <summary>
    /// Contributes section from package.json
    /// </summary>
    public ExtensionContributes? Contributes { get; set; }

    /// <summary>
    /// Languages contributed by this extension
    /// </summary>
    public List<LanguageContribution> Languages => Contributes?.Languages ?? new();

    /// <summary>
    /// Grammars contributed by this extension
    /// </summary>
    public List<GrammarContribution> Grammars => Contributes?.Grammars ?? new();

    /// <summary>
    /// Themes contributed by this extension
    /// </summary>
    public List<ThemeContribution> Themes => Contributes?.Themes ?? new();

    /// <summary>
    /// Snippets contributed by this extension
    /// </summary>
    public List<SnippetContribution> Snippets => Contributes?.Snippets ?? new();

    /// <summary>
    /// Debuggers contributed by this extension
    /// </summary>
    public List<DebuggerContribution> Debuggers => Contributes?.Debuggers ?? new();
}

/// <summary>
/// Contributions from a VS Code extension
/// </summary>
public class ExtensionContributes
{
    [JsonPropertyName("languages")]
    public List<LanguageContribution>? Languages { get; set; }

    [JsonPropertyName("grammars")]
    public List<GrammarContribution>? Grammars { get; set; }

    [JsonPropertyName("themes")]
    public List<ThemeContribution>? Themes { get; set; }

    [JsonPropertyName("snippets")]
    public List<SnippetContribution>? Snippets { get; set; }

    [JsonPropertyName("debuggers")]
    public List<DebuggerContribution>? Debuggers { get; set; }
}

/// <summary>
/// Language contribution from VS Code extension
/// </summary>
public class LanguageContribution
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("aliases")]
    public List<string>? Aliases { get; set; }

    [JsonPropertyName("extensions")]
    public List<string>? Extensions { get; set; }

    [JsonPropertyName("filenames")]
    public List<string>? Filenames { get; set; }

    [JsonPropertyName("configuration")]
    public string? Configuration { get; set; }

    [JsonPropertyName("firstLine")]
    public string? FirstLine { get; set; }
}

/// <summary>
/// Grammar contribution (TextMate grammar)
/// </summary>
public class GrammarContribution
{
    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("scopeName")]
    public string ScopeName { get; set; } = "";

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("embeddedLanguages")]
    public Dictionary<string, string>? EmbeddedLanguages { get; set; }

    [JsonPropertyName("tokenTypes")]
    public Dictionary<string, string>? TokenTypes { get; set; }

    [JsonPropertyName("injectTo")]
    public List<string>? InjectTo { get; set; }
}

/// <summary>
/// Theme contribution
/// </summary>
public class ThemeContribution
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("uiTheme")]
    public string? UiTheme { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";
}

/// <summary>
/// Snippet contribution
/// </summary>
public class SnippetContribution
{
    [JsonPropertyName("language")]
    public string Language { get; set; } = "";

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";
}

/// <summary>
/// Debugger contribution
/// </summary>
public class DebuggerContribution
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("program")]
    public string? Program { get; set; }

    [JsonPropertyName("runtime")]
    public string? Runtime { get; set; }

    [JsonPropertyName("args")]
    public List<string>? Args { get; set; }

    [JsonPropertyName("languages")]
    public List<string>? Languages { get; set; }

    [JsonPropertyName("configurationAttributes")]
    public JsonElement? ConfigurationAttributes { get; set; }
}
