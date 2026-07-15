namespace VisualGameStudio.Core.Utilities;

/// <summary>
/// The single source of truth for mapping a file extension to a language ID.
///
/// There are deliberately TWO maps here, and they are NOT the same function:
///
/// <list type="bullet">
/// <item>
///   <see cref="GetLanguageId"/> — <b>LSP routing</b>. Answers "which language server
///   owns this file?". Knows basiclang and cpp only, and returns <c>null</c> for
///   everything else, because null means "no server owns this".
/// </item>
/// <item>
///   <see cref="GetEditorLanguageId"/> — <b>extension host</b>. Answers "what would
///   VS Code call this file?" across ~30 languages. It is a <b>total</b> function: it
///   never returns null, falling back to the bare extension and then "plaintext".
///   <c>ExtensionService.HasExtensionProviders</c> does <c>ContainsKey(languageId)</c>,
///   which throws <see cref="System.ArgumentNullException"/> on null — so extension-host
///   call sites must use this map, never the nullable one.
/// </item>
/// </list>
///
/// Where both maps know a language they must agree; the tests pin that.
/// </summary>
public static class LanguageFileTypes
{
    private const string BasicLangId = "basiclang";
    private const string CppId = "cpp";

    /// <summary>Extensions routed to the BasicLang language server.</summary>
    private static readonly string[] BasicLangExtensions =
    {
        ".bas", ".bl", ".mod", ".cls", ".class"
    };

    /// <summary>
    /// Extensions routed to the C++ language server (clangd).
    /// <c>.h</c> is treated as C++ by decision — clangd handles it.
    /// <c>.c</c> is intentionally absent: C is not routed in Phase 3a.
    /// </summary>
    private static readonly string[] CppExtensions =
    {
        ".cpp", ".cc", ".cxx", ".h", ".hpp", ".hh", ".hxx", ".inl"
    };

    /// <summary>
    /// Every extension that <see cref="GetLanguageId"/> routes, across all languages.
    /// </summary>
    public static IReadOnlyList<string> AllKnownExtensions { get; } =
        BasicLangExtensions.Concat(CppExtensions).ToArray();

    /// <summary>
    /// LSP routing: returns the language ID of the server that owns this file, or
    /// <c>null</c> when no language server does. Callers are expected to handle null.
    /// </summary>
    public static string? GetLanguageId(string? path)
    {
        var ext = GetExtension(path);
        if (ext is null) return null;

        if (Contains(BasicLangExtensions, ext)) return BasicLangId;
        if (Contains(CppExtensions, ext)) return CppId;
        return null;
    }

    /// <summary>
    /// Returns true when the path is a C++ source or header file routed to clangd.
    /// </summary>
    public static bool IsCppSourceFile(string? path)
        => GetLanguageId(path) == CppId;

    /// <summary>
    /// Returns true when the path is a BasicLang source file routed to the BasicLang
    /// language server. <see cref="BasicLangFileTypes.IsBasicLangSourceFile"/> delegates here.
    /// </summary>
    public static bool IsBasicLangSourceFile(string? path)
        => GetLanguageId(path) == BasicLangId;

    /// <summary>
    /// Extension host: the ~30-language editor map. <b>Total — never returns null.</b>
    /// Unknown extensions fall back to the bare extension, and a path with no extension
    /// at all falls back to "plaintext".
    /// </summary>
    public static string GetEditorLanguageId(string? path)
    {
        var ext = System.IO.Path.GetExtension(path)?.ToLowerInvariant();
        return ext switch
        {
            ".html" or ".htm" => "html",
            ".css" => "css",
            ".scss" => "scss",
            ".less" => "less",
            ".js" => "javascript",
            ".jsx" => "javascriptreact",
            ".ts" => "typescript",
            ".tsx" => "typescriptreact",
            ".json" => "json",
            ".jsonc" => "jsonc",
            ".md" or ".markdown" => "markdown",
            ".xml" => "xml",
            ".yaml" or ".yml" => "yaml",
            ".py" => "python",
            ".rs" => "rust",
            ".go" => "go",
            ".cs" => "csharp",
            ".java" => "java",
            ".cpp" or ".cc" or ".cxx" => CppId,
            ".c" => "c",
            ".h" or ".hpp" or ".hh" or ".hxx" or ".inl" => CppId,
            ".lua" => "lua",
            ".bas" or ".bl" or ".mod" or ".cls" or ".class" => BasicLangId,
            ".blproj" => BasicLangId,
            ".sql" => "sql",
            ".sh" or ".bash" => "shellscript",
            ".ps1" => "powershell",
            ".php" => "php",
            ".rb" => "ruby",
            ".swift" => "swift",
            ".kt" or ".kts" => "kotlin",
            _ => ext?.TrimStart('.') ?? "plaintext",
        };
    }

    /// <summary>
    /// Returns the lower-cased extension (including the dot), or null when the path is
    /// null/empty or carries no extension.
    /// </summary>
    private static string? GetExtension(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        var ext = System.IO.Path.GetExtension(path);
        return string.IsNullOrEmpty(ext) ? null : ext.ToLowerInvariant();
    }

    private static bool Contains(string[] extensions, string ext)
    {
        foreach (var candidate in extensions)
        {
            if (string.Equals(candidate, ext, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
