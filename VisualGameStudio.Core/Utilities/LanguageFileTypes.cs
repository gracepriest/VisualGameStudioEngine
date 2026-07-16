namespace VisualGameStudio.Core.Utilities;

/// <summary>
/// The single source of truth for mapping a file extension to a language ID.
///
/// There are deliberately TWO maps here, and they are NOT the same function:
///
/// <list type="bullet">
/// <item>
///   <see cref="GetLspLanguageId"/> — <b>LSP routing</b>. Answers "which language server
///   owns this file?". Knows basiclang and cpp only, and returns <c>null</c> for
///   everything else, because null means "no server owns this".
/// </item>
/// <item>
///   <see cref="GetEditorLanguageId"/> — <b>extension host</b>. Answers "what would
///   VS Code call this file?" across ~30 languages. It is a <b>total</b> function: it
///   never returns null, falling back to the bare extension and then "plaintext".
///   <c>ExtensionService.HasExtensionProviders</c> does <c>ContainsKey(languageId)</c>,
///   which throws <see cref="System.ArgumentNullException"/> on null — so extension-host
///   call sites must use this map, never the LSP one.
/// </item>
/// </list>
///
/// <para>
/// <b>The type system does NOT enforce that split, so do not rely on it.</b> CS8604
/// ("possible null reference argument") is in <c>NoWarn</c> for both this project
/// (<c>VisualGameStudio.Core.csproj</c>) and <c>VisualGameStudio.Shell.csproj</c>, so
/// passing the nullable <see cref="GetLspLanguageId"/> result into a non-nullable
/// <c>string</c> parameter compiles clean — not even a warning someone might catch in
/// review. The <c>string?</c> annotation here is documentation, not enforcement; naming
/// and review are the only guards. That is why the narrow, dangerous map is named
/// <c>GetLspLanguageId</c> rather than the tempting, generic <c>GetLanguageId</c> —
/// there should be no "default" name for a maintainer to fall into.
/// </para>
///
/// <para>
/// Related list, deliberately NOT merged: <c>HighlightingLoader.CppExtensions</c>
/// (VisualGameStudio.Editor) drives syntax highlighting and additionally includes
/// <c>.c</c>, because C files should be highlighted as C++. This map omits <c>.c</c>
/// because C is not routed to clangd in Phase 3a. Both are correct — keep them separate.
/// </para>
///
/// Where both maps here know a language they must agree; the tests pin that.
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
    /// <c>.c</c> is intentionally absent: C is not routed in Phase 3a. Note that
    /// <c>HighlightingLoader.CppExtensions</c> DOES list <c>.c</c>; that divergence is
    /// deliberate and must not be "fixed" — see the remarks on this class.
    /// </summary>
    private static readonly string[] CppExtensions =
    {
        ".cpp", ".cc", ".cxx", ".h", ".hpp", ".hh", ".hxx", ".inl"
    };

    /// <summary>
    /// Every extension that <see cref="GetLspLanguageId"/> routes, across all languages.
    /// This is the LSP-routed set only — <see cref="GetEditorLanguageId"/> knows many
    /// more extensions that never appear here.
    /// </summary>
    public static IReadOnlyList<string> LspRoutedExtensions { get; } =
        BasicLangExtensions.Concat(CppExtensions).ToArray();

    /// <summary>
    /// The inverse of <see cref="GetLspLanguageId"/>: every extension routed to
    /// <paramref name="languageId"/>, empty when no language server owns that id.
    /// </summary>
    /// <remarks>
    /// A projection of the SAME arrays <see cref="GetLspLanguageId"/> reads, not a second copy
    /// of the mapping — so the forward and inverse answers cannot drift apart.
    /// <c>LanguageServerDescriptor.Extensions</c> is built from this rather than hand-listing
    /// extensions per server, which is how a server silently stops owning a file type.
    /// <para>
    /// Copies rather than handing back the private arrays themselves. <c>IReadOnlyList&lt;string&gt;</c>
    /// is a read-only VIEW, not a read-only object: the runtime type would still be
    /// <c>string[]</c>, and one downcast — <c>((string[])LspExtensionsFor("cpp"))[0] = ".zzz"</c> —
    /// would corrupt <see cref="GetLspLanguageId"/> process-wide. This class is the authority on
    /// routing; it must not hand out a handle to its own state. The copy costs two allocations at
    /// startup.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> LspExtensionsFor(string? languageId) => languageId switch
    {
        BasicLangId => (string[])BasicLangExtensions.Clone(),
        CppId => (string[])CppExtensions.Clone(),
        _ => Array.Empty<string>()
    };

    /// <summary>
    /// LSP routing: returns the language ID of the server that owns this file, or
    /// <c>null</c> when no language server does. Callers are expected to handle null.
    /// <b>Never pass this to the extension host</b> — see the remarks on this class.
    /// </summary>
    public static string? GetLspLanguageId(string? path)
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
        => GetLspLanguageId(path) == CppId;

    /// <summary>
    /// Returns true when the path is a BasicLang source file routed to the BasicLang
    /// language server. <see cref="BasicLangFileTypes.IsBasicLangSourceFile"/> delegates here.
    /// </summary>
    public static bool IsBasicLangSourceFile(string? path)
        => GetLspLanguageId(path) == BasicLangId;

    /// <summary>
    /// Extension host: the ~30-language editor map. <b>Total — never returns null.</b>
    /// Unknown extensions fall back to the bare extension (the empty string when the path
    /// has no extension at all); only a null path yields "plaintext".
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
