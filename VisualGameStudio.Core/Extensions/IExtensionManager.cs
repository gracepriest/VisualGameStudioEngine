namespace VisualGameStudio.Core.Extensions;

/// <summary>
/// Manages VS Code extension imports and conversions
/// </summary>
public interface IExtensionManager
{
    /// <summary>
    /// Scan a directory for VS Code extensions
    /// </summary>
    Task<IReadOnlyList<VSCodeExtension>> ScanExtensionsAsync(string directory);

    /// <summary>
    /// Import a VS Code extension into VGS
    /// </summary>
    Task<ExtensionImportResult> ImportExtensionAsync(string extensionPath);

    /// <summary>
    /// Import from VS Code's extension directory
    /// </summary>
    Task<IReadOnlyList<ExtensionImportResult>> ImportFromVSCodeAsync();

    /// <summary>
    /// Get all imported language server configurations
    /// </summary>
    IReadOnlyList<LanguageServerConfig> GetLanguageServers();

    /// <summary>
    /// Get all imported debug adapter configurations
    /// </summary>
    IReadOnlyList<DebugAdapterConfig> GetDebugAdapters();

    /// <summary>
    /// Get all imported themes
    /// </summary>
    IReadOnlyList<ImportedTheme> GetThemes();

    /// <summary>
    /// Get all imported snippets for a language
    /// </summary>
    IReadOnlyList<ImportedSnippet> GetSnippets(string languageId);

    /// <summary>
    /// Get TextMate grammar for a language
    /// </summary>
    TextMateGrammar? GetGrammar(string languageId);
}

/// <summary>
/// Result of importing an extension
/// </summary>
public class ExtensionImportResult
{
    public bool Success { get; set; }
    public string ExtensionId { get; set; } = "";
    public string? ErrorMessage { get; set; }
    public List<string> ImportedComponents { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// A theme imported from VS Code
/// </summary>
public class ImportedTheme
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string SourceExtension { get; set; } = "";
    public bool IsDark { get; set; }
    public Dictionary<string, string> Colors { get; set; } = new();
    public List<TokenColorRule> TokenColors { get; set; } = new();
}

/// <summary>
/// Token color rule for syntax highlighting
/// </summary>
public class TokenColorRule
{
    public string? Name { get; set; }
    public List<string> Scope { get; set; } = new();
    public TokenColorSettings Settings { get; set; } = new();
}

/// <summary>
/// Color settings for a token
/// </summary>
public class TokenColorSettings
{
    public string? Foreground { get; set; }
    public string? Background { get; set; }
    public string? FontStyle { get; set; }
}

/// <summary>
/// A snippet imported from VS Code
/// </summary>
public class ImportedSnippet
{
    public string Name { get; set; } = "";
    public string Prefix { get; set; } = "";
    public List<string> Body { get; set; } = new();
    public string? Description { get; set; }
    public string Language { get; set; } = "";
    public string SourceExtension { get; set; } = "";
}

/// <summary>
/// TextMate grammar for syntax highlighting
/// </summary>
public class TextMateGrammar
{
    public string ScopeName { get; set; } = "";
    public string LanguageId { get; set; } = "";
    public List<string> FileExtensions { get; set; } = new();
    public string SourcePath { get; set; } = "";
    public string SourceExtension { get; set; } = "";

    /// <summary>
    /// The raw grammar content (JSON or plist)
    /// </summary>
    public string Content { get; set; } = "";

    /// <summary>
    /// Parsed patterns from the grammar
    /// </summary>
    public List<GrammarPattern> Patterns { get; set; } = new();

    /// <summary>
    /// Repository of reusable patterns
    /// </summary>
    public Dictionary<string, GrammarPattern> Repository { get; set; } = new();
}

/// <summary>
/// A pattern in a TextMate grammar
/// </summary>
public class GrammarPattern
{
    public string? Name { get; set; }
    public string? Match { get; set; }
    public string? Begin { get; set; }
    public string? End { get; set; }
    public string? Include { get; set; }
    public Dictionary<string, CapturePattern>? Captures { get; set; }
    public Dictionary<string, CapturePattern>? BeginCaptures { get; set; }
    public Dictionary<string, CapturePattern>? EndCaptures { get; set; }
    public List<GrammarPattern>? Patterns { get; set; }
    public string? ContentName { get; set; }
}

/// <summary>
/// A capture pattern
/// </summary>
public class CapturePattern
{
    public string? Name { get; set; }
    public List<GrammarPattern>? Patterns { get; set; }
}
