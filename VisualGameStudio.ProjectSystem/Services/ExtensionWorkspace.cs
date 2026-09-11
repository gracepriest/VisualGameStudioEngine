using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// <c>workspace.findFiles</c> and <c>workspace.openTextDocument</c>.
///
/// <para>Separate from <see cref="ExtensionHost"/> for the same reason as
/// <see cref="ExtensionFileSystem"/>: the logic is real and deserves behavioural tests rather than a
/// source guard, and the host's RPC handlers stay thin adapters over it.</para>
/// </summary>
public static class ExtensionWorkspace
{
    /// <summary>
    /// Maps a file extension to the language id the IDE and its extensions use. Only the languages
    /// this IDE actually contributes plus the common data formats — an unknown extension answers
    /// "plaintext", which is what VS Code does.
    /// </summary>
    private static readonly Dictionary<string, string> LanguageByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".bas"] = "basiclang",
        [".mod"] = "basiclang",
        [".cls"] = "basiclang",
        [".bli"] = "basiclang",
        [".cpp"] = "cpp",
        [".cc"] = "cpp",
        [".cxx"] = "cpp",
        [".h"] = "cpp",
        [".hpp"] = "cpp",
        [".c"] = "c",
        [".cs"] = "csharp",
        [".vb"] = "vb",
        [".js"] = "javascript",
        [".ts"] = "typescript",
        [".json"] = "json",
        [".xml"] = "xml",
        [".md"] = "markdown",
        [".yml"] = "yaml",
        [".yaml"] = "yaml",
        [".html"] = "html",
        [".css"] = "css",
    };

    private static int _untitledCounter;

    // ------------------------------------------------------------------ findFiles

    /// <summary>
    /// Finds workspace files matching a VS Code glob.
    /// </summary>
    /// <param name="workspaceRoot">
    /// The folder globs resolve against. Null or missing yields an EMPTY result rather than an
    /// error — the IDE legitimately runs with no folder open, and an extension calling findFiles
    /// then should get "nothing", not a rejected promise.
    /// </param>
    /// <param name="include">The glob to match. Relative to the root, forward-slashed.</param>
    /// <param name="exclude">An optional glob whose matches are removed.</param>
    /// <param name="maxResults">A positive cap, or null for unlimited.</param>
    public static List<string> FindFiles(string? workspaceRoot, string include, string? exclude, int? maxResults)
    {
        var results = new List<string>();

        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
        {
            return results;
        }

        if (string.IsNullOrWhiteSpace(include)) return results;

        var includeRegex = GlobToRegex(include);
        var excludeRegex = string.IsNullOrWhiteSpace(exclude) ? null : GlobToRegex(exclude);

        foreach (var file in Directory.EnumerateFiles(workspaceRoot, "*", SearchOption.AllDirectories))
        {
            // Match on the path RELATIVE to the root, forward-slashed. A glob like `src/**` is
            // written against the workspace, not against C:\... — and on Windows the separator
            // would otherwise never match the '/' in the pattern.
            var relative = Path.GetRelativePath(workspaceRoot, file).Replace('\\', '/');

            if (!includeRegex.IsMatch(relative)) continue;
            if (excludeRegex != null && excludeRegex.IsMatch(relative)) continue;

            results.Add(ExtensionHost.ToDocumentUri(file));

            if (maxResults is > 0 && results.Count >= maxResults) break;
        }

        return results;
    }

    /// <summary>
    /// Translates a VS Code glob into an anchored regex.
    ///
    /// <para>⛔ <c>**/</c> MUST be able to match ZERO segments, or <c>**\/*.json</c> finds nested
    /// files and misses one sitting at the workspace root — the classic hand-rolled-glob bug, whose
    /// symptom reads as a configuration problem rather than a matcher problem. That is why the
    /// <c>**/</c> sequence is consumed as a unit and emitted as <c>(?:[^/]*/)*</c> rather than
    /// translating <c>**</c> and <c>/</c> separately.</para>
    ///
    /// <para>⛔ Everything not recognised as a glob operator is regex-ESCAPED. A literal dot in
    /// <c>a.b.json</c> must stay a dot; left active it would also match <c>axbxjson</c>.</para>
    /// </summary>
    public static Regex GlobToRegex(string glob)
    {
        var pattern = new StringBuilder("^");
        var i = 0;

        while (i < glob.Length)
        {
            var c = glob[i];

            if (c == '*')
            {
                var isDoubleStar = i + 1 < glob.Length && glob[i + 1] == '*';

                if (isDoubleStar)
                {
                    // "**/" spans any number of segments INCLUDING NONE.
                    if (i + 2 < glob.Length && glob[i + 2] == '/')
                    {
                        pattern.Append("(?:[^/]*/)*");
                        i += 3;
                        continue;
                    }

                    // A trailing "**" matches everything left, separators included.
                    pattern.Append(".*");
                    i += 2;
                    continue;
                }

                // A single star stays inside one segment.
                pattern.Append("[^/]*");
                i++;
                continue;
            }

            switch (c)
            {
                case '?':
                    pattern.Append("[^/]");
                    i++;
                    break;

                case '{':
                    pattern.Append("(?:");
                    i++;
                    break;

                case '}':
                    pattern.Append(')');
                    i++;
                    break;

                case ',':
                    // A comma only alternates inside braces; elsewhere it is a literal filename
                    // character. Tracking depth would be more precise, but a comma outside braces
                    // in a real glob is vanishingly rare and this keeps the translator readable.
                    pattern.Append('|');
                    i++;
                    break;

                case '[':
                {
                    // Character classes pass through, but the contents are still escaped so a
                    // stray backslash cannot open a regex escape.
                    var end = glob.IndexOf(']', i + 1);
                    if (end < 0)
                    {
                        pattern.Append("\\[");
                        i++;
                        break;
                    }

                    pattern.Append('[').Append(glob, i + 1, end - i - 1).Append(']');
                    i = end + 1;
                    break;
                }

                default:
                    pattern.Append(Regex.Escape(c.ToString()));
                    i++;
                    break;
            }
        }

        pattern.Append('$');

        // Paths are compared case-insensitively: this IDE's primary platform is Windows, where two
        // spellings of the same file must not produce two different answers.
        return new Regex(pattern.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    // ------------------------------------------------------------------ openTextDocument

    /// <summary>
    /// Opens a document for an extension. Two shapes reach here, because workspace.js sends two.
    /// </summary>
    /// <param name="uri">The file to open, for the <c>{ uri }</c> shape.</param>
    /// <param name="content">The body, for the <c>{ content, language, isVirtual }</c> shape.</param>
    /// <param name="language">The language id for a virtual document.</param>
    /// <param name="isVirtual">True for an untitled document with no file behind it.</param>
    public static ExtensionTextDocument OpenTextDocument(
        string? uri,
        string? content,
        string? language,
        bool isVirtual)
    {
        if (isVirtual || string.IsNullOrWhiteSpace(uri))
        {
            // Each untitled document needs its own uri, or two of them collide in the JS document
            // manager and the second silently replaces the first.
            var ordinal = Interlocked.Increment(ref _untitledCounter);

            return new ExtensionTextDocument
            {
                Uri = $"untitled:Untitled-{ordinal}",
                LanguageId = string.IsNullOrWhiteSpace(language) ? "plaintext" : language,
                Version = 1,
                Text = content ?? "",
            };
        }

        var path = ExtensionService.ToLocalDocumentPath(uri);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"No file at '{path}'.", path);
        }

        return new ExtensionTextDocument
        {
            Uri = ExtensionHost.ToDocumentUri(path),
            LanguageId = string.IsNullOrWhiteSpace(language) ? LanguageFor(path) : language,
            Version = 1,
            Text = File.ReadAllText(path),
        };
    }

    /// <summary>The language id for a path, defaulting to plaintext as VS Code does.</summary>
    public static string LanguageFor(string path) =>
        LanguageByExtension.TryGetValue(Path.GetExtension(path), out var id) ? id : "plaintext";
}

/// <summary>
/// The document shape returned to the JS side, which builds a real <c>TextDocument</c> from it.
/// The property names are the wire contract.
/// </summary>
public class ExtensionTextDocument
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = "";

    [JsonPropertyName("languageId")]
    public string LanguageId { get; set; } = "plaintext";

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";
}
