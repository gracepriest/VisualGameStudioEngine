using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using VisualGameStudio.Core.Extensions;
using VisualGameStudio.Core.TextMate;

namespace VisualGameStudio.Editor.Highlighting;

/// <summary>
/// Converts TextMate grammars (from VS Code extensions) into AvalonEdit IHighlightingDefinition
/// objects. This is an approximate conversion that extracts keywords, strings, comments, and
/// numbers from the grammar patterns and produces a usable syntax highlighting definition.
/// </summary>
public static class TextMateToAvalonEditConverter
{
    // Default scope-to-color mappings (dark theme)
    private static readonly Dictionary<string, (string Color, string? FontWeight, string? FontStyle)> ScopeColorMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Comments
        { "comment", ("#6A9955", null, "italic") },
        { "comment.line", ("#6A9955", null, "italic") },
        { "comment.block", ("#6A9955", null, "italic") },

        // Strings
        { "string", ("#CE9178", null, null) },
        { "string.quoted", ("#CE9178", null, null) },
        { "string.template", ("#CE9178", null, null) },
        { "string.regexp", ("#D16969", null, null) },

        // Keywords
        { "keyword", ("#569CD6", "bold", null) },
        { "keyword.control", ("#C586C0", "bold", null) },
        { "keyword.operator", ("#D4D4D4", null, null) },
        { "keyword.other", ("#569CD6", null, null) },
        { "storage", ("#569CD6", null, null) },
        { "storage.type", ("#569CD6", null, null) },
        { "storage.modifier", ("#569CD6", null, null) },

        // Types
        { "entity.name.type", ("#4EC9B0", null, null) },
        { "entity.name.class", ("#4EC9B0", null, null) },
        { "entity.other.inherited-class", ("#4EC9B0", null, null) },
        { "support.type", ("#4EC9B0", null, null) },
        { "support.class", ("#4EC9B0", null, null) },

        // Functions
        { "entity.name.function", ("#DCDCAA", null, null) },
        { "support.function", ("#DCDCAA", null, null) },
        { "meta.function-call", ("#DCDCAA", null, null) },

        // Variables
        { "variable", ("#9CDCFE", null, null) },
        { "variable.parameter", ("#9CDCFE", null, null) },
        { "variable.other", ("#9CDCFE", null, null) },
        { "variable.language", ("#569CD6", null, null) },

        // Constants
        { "constant", ("#4FC1FF", null, null) },
        { "constant.numeric", ("#B5CEA8", null, null) },
        { "constant.language", ("#569CD6", null, null) },
        { "constant.character", ("#D7BA7D", null, null) },

        // Punctuation
        { "punctuation", ("#D4D4D4", null, null) },

        // Tags (HTML/XML)
        { "entity.name.tag", ("#569CD6", null, null) },
        { "entity.other.attribute-name", ("#9CDCFE", null, null) },

        // Support
        { "support.constant", ("#4FC1FF", null, null) },
        { "support.variable", ("#9CDCFE", null, null) },

        // Invalid
        { "invalid", ("#F44747", null, null) },

        // Markup
        { "markup.heading", ("#569CD6", "bold", null) },
        { "markup.bold", ("#569CD6", "bold", null) },
        { "markup.italic", ("#569CD6", null, "italic") },
    };

    /// <summary>
    /// Converts a TextMateGrammarInfo (from Core.TextMate) to an AvalonEdit highlighting definition.
    /// </summary>
    public static IHighlightingDefinition? ConvertGrammar(TextMateGrammarInfo grammar, string languageName, IEnumerable<string>? fileExtensions = null)
    {
        try
        {
            var xshd = BuildXshdFromGrammar(grammar.Patterns, grammar.Repository, languageName, fileExtensions);
            return LoadFromXshd(xshd);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to convert TextMate grammar to AvalonEdit: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Converts a TextMateGrammar (from Core.Extensions) to an AvalonEdit highlighting definition.
    /// </summary>
    public static IHighlightingDefinition? ConvertExtensionGrammar(TextMateGrammar grammar, string languageName, IEnumerable<string>? fileExtensions = null)
    {
        try
        {
            var patterns = ConvertExtensionPatterns(grammar.Patterns);
            var repository = grammar.Repository.ToDictionary(
                kvp => kvp.Key,
                kvp => ConvertExtensionPattern(kvp.Value)
            );
            var xshd = BuildXshdFromGrammar(patterns, repository, languageName, fileExtensions);
            return LoadFromXshd(xshd);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to convert extension grammar to AvalonEdit: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Converts a raw JSON grammar content string to an AvalonEdit highlighting definition.
    /// </summary>
    public static IHighlightingDefinition? ConvertFromJson(string jsonContent, string languageName, IEnumerable<string>? fileExtensions = null)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(jsonContent, new System.Text.Json.JsonDocumentOptions
            {
                CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            var root = doc.RootElement;
            var patterns = new List<Core.TextMate.TextMatePattern>();
            var repository = new Dictionary<string, Core.TextMate.TextMatePattern>();

            if (root.TryGetProperty("patterns", out var patternsEl))
            {
                patterns = ParseJsonPatterns(patternsEl);
            }
            if (root.TryGetProperty("repository", out var repoEl))
            {
                foreach (var prop in repoEl.EnumerateObject())
                {
                    var pat = ParseJsonPattern(prop.Value);
                    if (pat != null)
                        repository[prop.Name] = pat;
                }
            }

            var xshd = BuildXshdFromGrammar(patterns, repository, languageName, fileExtensions);
            return LoadFromXshd(xshd);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to convert JSON grammar to AvalonEdit: {ex.Message}");
            return null;
        }
    }

    #region XSHD Building

    private static string BuildXshdFromGrammar(
        List<Core.TextMate.TextMatePattern> patterns,
        Dictionary<string, Core.TextMate.TextMatePattern> repository,
        string languageName,
        IEnumerable<string>? fileExtensions)
    {
        // Collect all patterns including repository entries
        var allPatterns = new List<Core.TextMate.TextMatePattern>(patterns);
        foreach (var repoEntry in repository.Values)
        {
            allPatterns.Add(repoEntry);
            if (repoEntry.Patterns != null)
                allPatterns.AddRange(repoEntry.Patterns);
        }

        // Flatten nested patterns
        var flattened = FlattenPatterns(allPatterns, repository, maxDepth: 3);

        // Extract categorized patterns
        var comments = ExtractCommentPatterns(flattened);
        var strings = ExtractStringPatterns(flattened);
        var keywords = ExtractKeywordPatterns(flattened, repository);
        var numbers = ExtractNumberPatterns(flattened);
        var types = ExtractScopePatterns(flattened, "entity.name.type", "support.type", "support.class", "entity.name.class");
        var functions = ExtractScopePatterns(flattened, "entity.name.function", "support.function");
        var constants = ExtractScopePatterns(flattened, "constant.language");
        var variables = ExtractScopePatterns(flattened, "variable.language");

        var extensions = fileExtensions != null ? string.Join(";", fileExtensions) : "";
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\"?>");
        sb.AppendLine($"<SyntaxDefinition name=\"{EscapeXml(languageName)}\" extensions=\"{EscapeXml(extensions)}\"");
        sb.AppendLine("                  xmlns=\"http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008\">");
        sb.AppendLine();

        // Define colors based on scope mappings
        sb.AppendLine("  <Color name=\"Comment\" foreground=\"#6A9955\" fontStyle=\"italic\" />");
        sb.AppendLine("  <Color name=\"String\" foreground=\"#CE9178\" />");
        sb.AppendLine("  <Color name=\"Number\" foreground=\"#B5CEA8\" />");
        sb.AppendLine("  <Color name=\"Keyword\" foreground=\"#569CD6\" fontWeight=\"bold\" />");
        sb.AppendLine("  <Color name=\"ControlKeyword\" foreground=\"#C586C0\" fontWeight=\"bold\" />");
        sb.AppendLine("  <Color name=\"Type\" foreground=\"#4EC9B0\" />");
        sb.AppendLine("  <Color name=\"Function\" foreground=\"#DCDCAA\" />");
        sb.AppendLine("  <Color name=\"Constant\" foreground=\"#4FC1FF\" />");
        sb.AppendLine("  <Color name=\"Variable\" foreground=\"#9CDCFE\" />");
        sb.AppendLine("  <Color name=\"Operator\" foreground=\"#D4D4D4\" />");
        sb.AppendLine("  <Color name=\"Regex\" foreground=\"#D16969\" />");
        sb.AppendLine();

        sb.AppendLine("  <RuleSet>");

        // Comments
        foreach (var comment in comments)
        {
            if (comment.IsLine)
            {
                sb.AppendLine("    <Span color=\"Comment\">");
                sb.AppendLine($"      <Begin>{EscapeXml(EscapeRegexForXshd(comment.Begin))}</Begin>");
                sb.AppendLine("    </Span>");
            }
            else if (!string.IsNullOrEmpty(comment.Begin) && !string.IsNullOrEmpty(comment.End))
            {
                sb.AppendLine("    <Span color=\"Comment\" multiline=\"true\">");
                sb.AppendLine($"      <Begin>{EscapeXml(EscapeRegexForXshd(comment.Begin))}</Begin>");
                sb.AppendLine($"      <End>{EscapeXml(EscapeRegexForXshd(comment.End))}</End>");
                sb.AppendLine("    </Span>");
            }
        }

        // Strings
        foreach (var str in strings)
        {
            if (!string.IsNullOrEmpty(str.Begin) && !string.IsNullOrEmpty(str.End))
            {
                var multiline = str.IsMultiline ? " multiline=\"true\"" : "";
                sb.AppendLine($"    <Span color=\"String\"{multiline}>");
                sb.AppendLine($"      <Begin>{EscapeXml(EscapeRegexForXshd(str.Begin))}</Begin>");
                sb.AppendLine($"      <End>{EscapeXml(EscapeRegexForXshd(str.End))}</End>");
                sb.AppendLine("    </Span>");
            }
        }

        // Numbers
        if (numbers.Count > 0)
        {
            // Use a standard number rule
            sb.AppendLine("    <Rule color=\"Number\">");
            sb.AppendLine(@"      \b0[xX][0-9a-fA-F]+\b|\b0[bB][01]+\b|\b0[oO][0-7]+\b|\b[0-9]+\.?[0-9]*([eE][+-]?[0-9]+)?\b");
            sb.AppendLine("    </Rule>");
        }

        // Keywords
        if (keywords.Count > 0)
        {
            var controlKeywords = keywords
                .Where(k => k.Scope?.Contains("control") == true)
                .SelectMany(k => k.Words)
                .Distinct()
                .ToList();

            var regularKeywords = keywords
                .Where(k => k.Scope?.Contains("control") != true)
                .SelectMany(k => k.Words)
                .Distinct()
                .Except(controlKeywords)
                .ToList();

            if (controlKeywords.Count > 0)
            {
                sb.AppendLine("    <Keywords color=\"ControlKeyword\">");
                foreach (var kw in controlKeywords)
                {
                    sb.AppendLine($"      <Word>{EscapeXml(kw)}</Word>");
                }
                sb.AppendLine("    </Keywords>");
            }

            if (regularKeywords.Count > 0)
            {
                sb.AppendLine("    <Keywords color=\"Keyword\">");
                foreach (var kw in regularKeywords)
                {
                    sb.AppendLine($"      <Word>{EscapeXml(kw)}</Word>");
                }
                sb.AppendLine("    </Keywords>");
            }
        }

        // Types (as keywords)
        if (types.Count > 0)
        {
            sb.AppendLine("    <Keywords color=\"Type\">");
            foreach (var t in types.Distinct())
            {
                sb.AppendLine($"      <Word>{EscapeXml(t)}</Word>");
            }
            sb.AppendLine("    </Keywords>");
        }

        // Built-in functions (as keywords)
        if (functions.Count > 0)
        {
            sb.AppendLine("    <Keywords color=\"Function\">");
            foreach (var f in functions.Distinct())
            {
                sb.AppendLine($"      <Word>{EscapeXml(f)}</Word>");
            }
            sb.AppendLine("    </Keywords>");
        }

        // Constants (as keywords)
        if (constants.Count > 0)
        {
            sb.AppendLine("    <Keywords color=\"Constant\">");
            foreach (var c in constants.Distinct())
            {
                sb.AppendLine($"      <Word>{EscapeXml(c)}</Word>");
            }
            sb.AppendLine("    </Keywords>");
        }

        // Language variables (as keywords)
        if (variables.Count > 0)
        {
            sb.AppendLine("    <Keywords color=\"Variable\">");
            foreach (var v in variables.Distinct())
            {
                sb.AppendLine($"      <Word>{EscapeXml(v)}</Word>");
            }
            sb.AppendLine("    </Keywords>");
        }

        sb.AppendLine("  </RuleSet>");
        sb.AppendLine("</SyntaxDefinition>");

        return sb.ToString();
    }

    #endregion

    #region Pattern Extraction

    private static List<Core.TextMate.TextMatePattern> FlattenPatterns(
        List<Core.TextMate.TextMatePattern> patterns,
        Dictionary<string, Core.TextMate.TextMatePattern> repository,
        int maxDepth)
    {
        var result = new List<Core.TextMate.TextMatePattern>();
        FlattenPatternsRecursive(patterns, repository, result, maxDepth, 0);
        return result;
    }

    private static void FlattenPatternsRecursive(
        List<Core.TextMate.TextMatePattern> patterns,
        Dictionary<string, Core.TextMate.TextMatePattern> repository,
        List<Core.TextMate.TextMatePattern> result,
        int maxDepth,
        int currentDepth)
    {
        if (currentDepth >= maxDepth) return;

        foreach (var pattern in patterns)
        {
            result.Add(pattern);

            // Resolve includes
            if (!string.IsNullOrEmpty(pattern.Include))
            {
                var key = pattern.Include.TrimStart('#');
                if (repository.TryGetValue(key, out var included))
                {
                    result.Add(included);
                    if (included.Patterns != null)
                    {
                        FlattenPatternsRecursive(included.Patterns, repository, result, maxDepth, currentDepth + 1);
                    }
                }
            }

            // Recurse into nested patterns
            if (pattern.Patterns != null)
            {
                FlattenPatternsRecursive(pattern.Patterns, repository, result, maxDepth, currentDepth + 1);
            }
        }
    }

    private static List<CommentInfo> ExtractCommentPatterns(List<Core.TextMate.TextMatePattern> patterns)
    {
        var comments = new List<CommentInfo>();

        foreach (var p in patterns)
        {
            var scope = p.Name ?? p.ContentName ?? "";
            if (!scope.StartsWith("comment", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrEmpty(p.Match))
            {
                // Single-line comment like // or #
                var prefix = ExtractLiteralPrefix(p.Match);
                if (!string.IsNullOrEmpty(prefix))
                {
                    comments.Add(new CommentInfo { Begin = prefix, IsLine = true });
                }
            }
            else if (!string.IsNullOrEmpty(p.Begin) && !string.IsNullOrEmpty(p.End))
            {
                var begin = ExtractLiteralPrefix(p.Begin);
                var end = ExtractLiteralPrefix(p.End);
                if (!string.IsNullOrEmpty(begin) && !string.IsNullOrEmpty(end))
                {
                    comments.Add(new CommentInfo { Begin = begin, End = end, IsLine = false });
                }
                else if (!string.IsNullOrEmpty(begin))
                {
                    // Block comment with regex end - treat as line comment
                    comments.Add(new CommentInfo { Begin = begin, IsLine = true });
                }
            }
        }

        // Deduplicate
        return comments.GroupBy(c => c.Begin).Select(g => g.First()).ToList();
    }

    private static List<StringInfo> ExtractStringPatterns(List<Core.TextMate.TextMatePattern> patterns)
    {
        var strings = new List<StringInfo>();

        foreach (var p in patterns)
        {
            var scope = p.Name ?? p.ContentName ?? "";
            if (!scope.StartsWith("string", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrEmpty(p.Begin) && !string.IsNullOrEmpty(p.End))
            {
                var begin = ExtractLiteralPrefix(p.Begin);
                var end = ExtractLiteralPrefix(p.End);
                if (!string.IsNullOrEmpty(begin) && !string.IsNullOrEmpty(end))
                {
                    var isMultiline = scope.Contains("multi", StringComparison.OrdinalIgnoreCase) ||
                                     begin.Length >= 3; // Triple-quoted strings (""", ''')
                    strings.Add(new StringInfo { Begin = begin, End = end, IsMultiline = isMultiline });
                }
            }
        }

        // If no strings found, add defaults
        if (strings.Count == 0)
        {
            strings.Add(new StringInfo { Begin = "\"", End = "\"", IsMultiline = false });
            strings.Add(new StringInfo { Begin = "'", End = "'", IsMultiline = false });
        }

        return strings.GroupBy(s => s.Begin).Select(g => g.First()).ToList();
    }

    private static List<KeywordInfo> ExtractKeywordPatterns(
        List<Core.TextMate.TextMatePattern> patterns,
        Dictionary<string, Core.TextMate.TextMatePattern> repository)
    {
        var keywords = new List<KeywordInfo>();

        foreach (var p in patterns)
        {
            var scope = p.Name ?? "";
            if (!scope.StartsWith("keyword", StringComparison.OrdinalIgnoreCase) &&
                !scope.StartsWith("storage", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrEmpty(p.Match))
            {
                var words = ExtractWordsFromRegex(p.Match);
                if (words.Count > 0)
                {
                    keywords.Add(new KeywordInfo { Words = words, Scope = scope });
                }
            }
        }

        // Also check captures for keyword scopes
        foreach (var p in patterns)
        {
            if (p.Captures != null)
            {
                foreach (var cap in p.Captures.Values)
                {
                    var capScope = cap.Name ?? "";
                    if (capScope.StartsWith("keyword", StringComparison.OrdinalIgnoreCase) ||
                        capScope.StartsWith("storage", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(p.Match))
                        {
                            var words = ExtractWordsFromRegex(p.Match);
                            if (words.Count > 0)
                            {
                                keywords.Add(new KeywordInfo { Words = words, Scope = capScope });
                            }
                        }
                    }
                }
            }
        }

        return keywords;
    }

    private static List<bool> ExtractNumberPatterns(List<Core.TextMate.TextMatePattern> patterns)
    {
        var found = new List<bool>();
        foreach (var p in patterns)
        {
            var scope = p.Name ?? "";
            if (scope.StartsWith("constant.numeric", StringComparison.OrdinalIgnoreCase))
            {
                found.Add(true);
                break;
            }
        }
        return found;
    }

    private static List<string> ExtractScopePatterns(List<Core.TextMate.TextMatePattern> patterns, params string[] scopePrefixes)
    {
        var words = new List<string>();

        foreach (var p in patterns)
        {
            var scope = p.Name ?? "";
            if (!scopePrefixes.Any(sp => scope.StartsWith(sp, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (!string.IsNullOrEmpty(p.Match))
            {
                words.AddRange(ExtractWordsFromRegex(p.Match));
            }
        }

        // Also check captures
        foreach (var p in patterns)
        {
            if (p.Captures != null)
            {
                foreach (var cap in p.Captures.Values)
                {
                    var capScope = cap.Name ?? "";
                    if (scopePrefixes.Any(sp => capScope.StartsWith(sp, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (!string.IsNullOrEmpty(p.Match))
                        {
                            words.AddRange(ExtractWordsFromRegex(p.Match));
                        }
                    }
                }
            }
        }

        return words;
    }

    #endregion

    #region Regex Helpers

    /// <summary>
    /// Extracts literal words from a regex pattern like \b(if|else|while|for)\b
    /// </summary>
    private static List<string> ExtractWordsFromRegex(string regex)
    {
        var words = new List<string>();

        // Match patterns like \b(word1|word2|word3)\b or (?:word1|word2)
        var alternationMatch = Regex.Match(regex, @"(?:\\b)?\((?:\?[:=!])?([a-zA-Z_|]+)\)(?:\\b)?");
        if (alternationMatch.Success)
        {
            var alternatives = alternationMatch.Groups[1].Value.Split('|');
            foreach (var alt in alternatives)
            {
                var word = alt.Trim();
                if (IsValidWord(word))
                {
                    words.Add(word);
                }
            }
        }

        // Also try to match simple \bword\b patterns
        if (words.Count == 0)
        {
            var simpleMatch = Regex.Match(regex, @"\\b([a-zA-Z_][a-zA-Z0-9_]*)\\b");
            if (simpleMatch.Success)
            {
                words.Add(simpleMatch.Groups[1].Value);
            }
        }

        return words;
    }

    /// <summary>
    /// Extracts the literal prefix from a regex (for comment/string delimiters).
    /// </summary>
    private static string? ExtractLiteralPrefix(string regex)
    {
        if (string.IsNullOrEmpty(regex))
            return null;

        var sb = new StringBuilder();
        var i = 0;

        while (i < regex.Length)
        {
            var c = regex[i];

            // Stop at regex metacharacters
            if (c == '[' || c == '(' || c == '.' || c == '*' || c == '+' || c == '?' || c == '{' || c == '^' || c == '$')
            {
                break;
            }

            if (c == '\\' && i + 1 < regex.Length)
            {
                var next = regex[i + 1];
                // Common escapes for literal characters
                if (next == '/' || next == '*' || next == '.' || next == '(' || next == ')' ||
                    next == '[' || next == ']' || next == '{' || next == '}' || next == '+' ||
                    next == '?' || next == '|' || next == '^' || next == '$' || next == '\\' ||
                    next == '#' || next == '"' || next == '\'')
                {
                    sb.Append(next);
                    i += 2;
                    continue;
                }
                // \b, \s, etc. are not literal
                break;
            }

            sb.Append(c);
            i++;
        }

        var result = sb.ToString();
        return result.Length > 0 ? result : null;
    }

    private static bool IsValidWord(string word)
    {
        if (string.IsNullOrEmpty(word) || word.Length < 2)
            return false;

        // Must start with letter or underscore
        if (!char.IsLetter(word[0]) && word[0] != '_')
            return false;

        // Must be all alphanumeric/underscore
        return word.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    private static string EscapeRegexForXshd(string input)
    {
        // XSHD uses .NET regex, so most patterns from TextMate (Oniguruma) work.
        // Just pass through the literal text since we've already extracted literals.
        return Regex.Escape(input);
    }

    #endregion

    #region XML/Loading Helpers

    private static string EscapeXml(string input)
    {
        return input
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    private static IHighlightingDefinition? LoadFromXshd(string xshdContent)
    {
        try
        {
            using var reader = new StringReader(xshdContent);
            using var xmlReader = new XmlTextReader(reader);
            var xshd = AvaloniaEdit.Highlighting.Xshd.HighlightingLoader.LoadXshd(xmlReader);
            return AvaloniaEdit.Highlighting.Xshd.HighlightingLoader.Load(xshd, HighlightingManager.Instance);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load XSHD: {ex.Message}");
            return null;
        }
    }

    #endregion

    #region Extension Pattern Conversion

    private static List<Core.TextMate.TextMatePattern> ConvertExtensionPatterns(List<GrammarPattern> extensionPatterns)
    {
        return extensionPatterns.Select(ConvertExtensionPattern).ToList();
    }

    private static Core.TextMate.TextMatePattern ConvertExtensionPattern(GrammarPattern ep)
    {
        return new Core.TextMate.TextMatePattern
        {
            Name = ep.Name,
            Match = ep.Match,
            Begin = ep.Begin,
            End = ep.End,
            Include = ep.Include,
            ContentName = ep.ContentName,
            Captures = ep.Captures?.ToDictionary(
                kvp => kvp.Key,
                kvp => new Core.TextMate.TextMateCapture { Name = kvp.Value.Name }
            ),
            BeginCaptures = ep.BeginCaptures?.ToDictionary(
                kvp => kvp.Key,
                kvp => new Core.TextMate.TextMateCapture { Name = kvp.Value.Name }
            ),
            EndCaptures = ep.EndCaptures?.ToDictionary(
                kvp => kvp.Key,
                kvp => new Core.TextMate.TextMateCapture { Name = kvp.Value.Name }
            ),
            Patterns = ep.Patterns != null ? ConvertExtensionPatterns(ep.Patterns) : null
        };
    }

    #endregion

    #region JSON Pattern Parsing

    private static List<Core.TextMate.TextMatePattern> ParseJsonPatterns(System.Text.Json.JsonElement element)
    {
        var patterns = new List<Core.TextMate.TextMatePattern>();
        if (element.ValueKind != System.Text.Json.JsonValueKind.Array) return patterns;

        foreach (var item in element.EnumerateArray())
        {
            var p = ParseJsonPattern(item);
            if (p != null)
                patterns.Add(p);
        }

        return patterns;
    }

    private static Core.TextMate.TextMatePattern? ParseJsonPattern(System.Text.Json.JsonElement element)
    {
        if (element.ValueKind != System.Text.Json.JsonValueKind.Object) return null;

        var pattern = new Core.TextMate.TextMatePattern();

        if (element.TryGetProperty("name", out var name)) pattern.Name = name.GetString();
        if (element.TryGetProperty("match", out var match)) pattern.Match = match.GetString();
        if (element.TryGetProperty("begin", out var begin)) pattern.Begin = begin.GetString();
        if (element.TryGetProperty("end", out var end)) pattern.End = end.GetString();
        if (element.TryGetProperty("include", out var include)) pattern.Include = include.GetString();
        if (element.TryGetProperty("contentName", out var contentName)) pattern.ContentName = contentName.GetString();

        if (element.TryGetProperty("captures", out var captures))
            pattern.Captures = ParseJsonCaptures(captures);
        if (element.TryGetProperty("beginCaptures", out var beginCaptures))
            pattern.BeginCaptures = ParseJsonCaptures(beginCaptures);
        if (element.TryGetProperty("endCaptures", out var endCaptures))
            pattern.EndCaptures = ParseJsonCaptures(endCaptures);
        if (element.TryGetProperty("patterns", out var patterns))
            pattern.Patterns = ParseJsonPatterns(patterns);

        return pattern;
    }

    private static Dictionary<string, Core.TextMate.TextMateCapture>? ParseJsonCaptures(System.Text.Json.JsonElement element)
    {
        if (element.ValueKind != System.Text.Json.JsonValueKind.Object) return null;

        var captures = new Dictionary<string, Core.TextMate.TextMateCapture>();
        foreach (var prop in element.EnumerateObject())
        {
            var cap = new Core.TextMate.TextMateCapture();
            if (prop.Value.TryGetProperty("name", out var name))
                cap.Name = name.GetString();
            captures[prop.Name] = cap;
        }
        return captures;
    }

    #endregion

    #region Info Types

    private class CommentInfo
    {
        public string Begin { get; set; } = "";
        public string? End { get; set; }
        public bool IsLine { get; set; }
    }

    private class StringInfo
    {
        public string Begin { get; set; } = "";
        public string End { get; set; } = "";
        public bool IsMultiline { get; set; }
    }

    private class KeywordInfo
    {
        public List<string> Words { get; set; } = new();
        public string? Scope { get; set; }
    }

    #endregion
}
