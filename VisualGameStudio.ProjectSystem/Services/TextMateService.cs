using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// TextMate grammar service implementation.
/// </summary>
public class TextMateService : ITextMateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly Dictionary<string, TextMateGrammar> _grammars = new();
    private readonly Dictionary<string, TextMateTheme> _themes = new();
    private readonly Dictionary<string, string> _extensionToScope = new();
    private readonly Dictionary<string, Regex> _regexCache = new();
    private TextMateTheme? _currentTheme;

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, TextMateGrammar> Grammars => _grammars;

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, TextMateTheme> Themes => _themes;

    /// <inheritdoc/>
    public TextMateTheme? CurrentTheme
    {
        get => _currentTheme;
        set
        {
            var old = _currentTheme;
            _currentTheme = value;
            ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(old, value));
        }
    }

    /// <inheritdoc/>
    public event EventHandler<GrammarLoadedEventArgs>? GrammarLoaded;

    /// <inheritdoc/>
    public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

    /// <inheritdoc/>
    public async Task<TextMateGrammar?> LoadGrammarAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            var content = await File.ReadAllTextAsync(filePath);
            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            TextMateGrammar? grammar = null;

            if (extension == ".json" || extension == ".tmLanguage.json")
            {
                grammar = ParseJsonGrammar(content);
            }
            else if (extension == ".plist" || extension == ".tmlanguage")
            {
                // PList format - simplified handling
                grammar = ParsePlistGrammar(content);
            }

            if (grammar != null)
            {
                grammar.FilePath = filePath;
                _grammars[grammar.ScopeName] = grammar;

                // Register file types
                foreach (var fileType in grammar.FileTypes)
                {
                    var ext = fileType.StartsWith(".") ? fileType : "." + fileType;
                    _extensionToScope[ext.ToLowerInvariant()] = grammar.ScopeName;
                }

                GrammarLoaded?.Invoke(this, new GrammarLoadedEventArgs(grammar, filePath));
            }

            return grammar;
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public TextMateGrammar? LoadGrammarFromJson(string json, string scopeName)
    {
        try
        {
            var grammar = ParseJsonGrammar(json);
            if (grammar != null)
            {
                if (string.IsNullOrEmpty(grammar.ScopeName))
                {
                    grammar.ScopeName = scopeName;
                }

                _grammars[grammar.ScopeName] = grammar;

                foreach (var fileType in grammar.FileTypes)
                {
                    var ext = fileType.StartsWith(".") ? fileType : "." + fileType;
                    _extensionToScope[ext.ToLowerInvariant()] = grammar.ScopeName;
                }

                GrammarLoaded?.Invoke(this, new GrammarLoadedEventArgs(grammar));
            }

            return grammar;
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public void RegisterExtension(string extension, string scopeName)
    {
        var ext = extension.StartsWith(".") ? extension : "." + extension;
        _extensionToScope[ext.ToLowerInvariant()] = scopeName;
    }

    /// <inheritdoc/>
    public TextMateGrammar? GetGrammarForExtension(string extension)
    {
        var ext = extension.StartsWith(".") ? extension : "." + extension;
        if (_extensionToScope.TryGetValue(ext.ToLowerInvariant(), out var scopeName))
        {
            _grammars.TryGetValue(scopeName, out var grammar);
            return grammar;
        }
        return null;
    }

    /// <inheritdoc/>
    public async Task<TextMateTheme?> LoadThemeAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            var content = await File.ReadAllTextAsync(filePath);
            var name = Path.GetFileNameWithoutExtension(filePath);

            return LoadThemeFromJson(content, name);
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public TextMateTheme? LoadThemeFromJson(string json, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            var root = doc.RootElement;
            var theme = new TextMateTheme
            {
                Name = name
            };

            if (root.TryGetProperty("name", out var nameEl))
            {
                theme.Name = nameEl.GetString() ?? name;
            }

            if (root.TryGetProperty("type", out var typeEl))
            {
                theme.Type = typeEl.GetString() ?? "dark";
            }

            if (root.TryGetProperty("colors", out var colors))
            {
                foreach (var prop in colors.EnumerateObject())
                {
                    theme.Colors[prop.Name] = prop.Value.GetString() ?? "";
                }
            }

            if (root.TryGetProperty("tokenColors", out var tokenColors))
            {
                foreach (var rule in tokenColors.EnumerateArray())
                {
                    var colorRule = new TokenColorRule();

                    if (rule.TryGetProperty("name", out var ruleNameEl))
                    {
                        colorRule.Name = ruleNameEl.GetString();
                    }

                    if (rule.TryGetProperty("scope", out var scopeEl))
                    {
                        if (scopeEl.ValueKind == JsonValueKind.String)
                        {
                            colorRule.Scope = scopeEl.GetString();
                        }
                        else if (scopeEl.ValueKind == JsonValueKind.Array)
                        {
                            var scopes = new List<string>();
                            foreach (var s in scopeEl.EnumerateArray())
                            {
                                if (s.GetString() is string str)
                                {
                                    scopes.Add(str);
                                }
                            }
                            colorRule.Scope = scopes;
                        }
                    }

                    if (rule.TryGetProperty("settings", out var settings))
                    {
                        if (settings.TryGetProperty("foreground", out var fg))
                        {
                            colorRule.Settings.Foreground = fg.GetString();
                        }
                        if (settings.TryGetProperty("background", out var bg))
                        {
                            colorRule.Settings.Background = bg.GetString();
                        }
                        if (settings.TryGetProperty("fontStyle", out var fs))
                        {
                            colorRule.Settings.FontStyle = fs.GetString();
                        }
                    }

                    theme.TokenColors.Add(colorRule);
                }
            }

            _themes[theme.Name] = theme;
            return theme;
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public TokenizationResult TokenizeLine(string line, TextMateGrammar grammar, TokenizerState? previousState = null)
    {
        var result = new TokenizationResult
        {
            EndState = previousState?.Clone() ?? new TokenizerState()
        };

        if (string.IsNullOrEmpty(line))
        {
            return result;
        }

        // Initialize scope stack with grammar scope
        if (result.EndState.ScopeStack.Count == 0)
        {
            result.EndState.ScopeStack.Add(grammar.ScopeName);
        }

        var position = 0;
        var tokens = new List<TextMateToken>();

        // Try to match patterns
        while (position < line.Length)
        {
            var matched = false;
            var bestMatch = FindBestMatch(line, position, grammar.Patterns, grammar, result.EndState);

            if (bestMatch != null)
            {
                // Add any preceding text as default token
                if (bestMatch.StartIndex > position)
                {
                    tokens.Add(new TextMateToken
                    {
                        StartIndex = position,
                        EndIndex = bestMatch.StartIndex,
                        Scopes = new List<string>(result.EndState.ScopeStack)
                    });
                }

                tokens.Add(bestMatch);
                position = bestMatch.EndIndex;
                matched = true;
            }

            if (!matched)
            {
                // No match - advance one character
                var endPos = Math.Min(position + 1, line.Length);

                // Try to extend to next potential match or end of line
                while (endPos < line.Length)
                {
                    var test = FindBestMatch(line, endPos, grammar.Patterns, grammar, result.EndState);
                    if (test != null && test.StartIndex == endPos)
                    {
                        break;
                    }
                    endPos++;
                }

                tokens.Add(new TextMateToken
                {
                    StartIndex = position,
                    EndIndex = endPos,
                    Scopes = new List<string>(result.EndState.ScopeStack)
                });

                position = endPos;
            }
        }

        // Merge adjacent tokens with same scopes
        result.Tokens = MergeTokens(tokens);

        return result;
    }

    /// <inheritdoc/>
    public IReadOnlyList<TokenizationResult> TokenizeDocument(string content, TextMateGrammar grammar)
    {
        var results = new List<TokenizationResult>();
        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        TokenizerState? state = null;

        foreach (var line in lines)
        {
            var result = TokenizeLine(line, grammar, state);
            results.Add(result);
            state = result.EndState;
        }

        return results;
    }

    /// <inheritdoc/>
    public TokenStyle? GetStyleForScopes(IEnumerable<string> scopes)
    {
        if (_currentTheme == null)
        {
            return null;
        }

        var scopeList = scopes.ToList();
        TokenStyle? bestMatch = null;
        var bestSpecificity = -1;

        foreach (var rule in _currentTheme.TokenColors)
        {
            var ruleScopes = rule.GetScopes();
            foreach (var ruleScope in ruleScopes)
            {
                foreach (var scope in scopeList)
                {
                    if (MatchesScope(scope, ruleScope))
                    {
                        var specificity = ruleScope.Split('.').Length;
                        if (specificity > bestSpecificity)
                        {
                            bestSpecificity = specificity;
                            bestMatch = rule.Settings;
                        }
                    }
                }
            }
        }

        return bestMatch;
    }

    #region Private Methods

    private TextMateGrammar? ParseJsonGrammar(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        var root = doc.RootElement;
        var grammar = new TextMateGrammar();

        if (root.TryGetProperty("scopeName", out var scopeName))
        {
            grammar.ScopeName = scopeName.GetString() ?? "";
        }

        if (root.TryGetProperty("name", out var name))
        {
            grammar.Name = name.GetString() ?? "";
        }

        if (root.TryGetProperty("fileTypes", out var fileTypes))
        {
            foreach (var ft in fileTypes.EnumerateArray())
            {
                if (ft.GetString() is string s)
                {
                    grammar.FileTypes.Add(s);
                }
            }
        }

        if (root.TryGetProperty("firstLineMatch", out var flm))
        {
            grammar.FirstLineMatch = flm.GetString();
        }

        if (root.TryGetProperty("foldingStartMarker", out var fsm))
        {
            grammar.FoldingStartMarker = fsm.GetString();
        }

        if (root.TryGetProperty("foldingStopMarker", out var fem))
        {
            grammar.FoldingEndMarker = fem.GetString();
        }

        if (root.TryGetProperty("patterns", out var patterns))
        {
            grammar.Patterns = ParsePatterns(patterns);
        }

        if (root.TryGetProperty("repository", out var repo))
        {
            foreach (var prop in repo.EnumerateObject())
            {
                var pattern = ParsePattern(prop.Value);
                if (pattern != null)
                {
                    grammar.Repository[prop.Name] = pattern;
                }
            }
        }

        return grammar;
    }

    private TextMateGrammar? ParsePlistGrammar(string plist)
    {
        // Simplified plist parsing - in production, use a proper plist parser
        var grammar = new TextMateGrammar();

        // Try to extract key-value pairs using regex
        var scopeMatch = Regex.Match(plist, @"<key>scopeName</key>\s*<string>([^<]+)</string>");
        if (scopeMatch.Success)
        {
            grammar.ScopeName = scopeMatch.Groups[1].Value;
        }

        var nameMatch = Regex.Match(plist, @"<key>name</key>\s*<string>([^<]+)</string>");
        if (nameMatch.Success)
        {
            grammar.Name = nameMatch.Groups[1].Value;
        }

        var fileTypesMatches = Regex.Matches(plist, @"<key>fileTypes</key>\s*<array>([\s\S]*?)</array>");
        foreach (Match m in fileTypesMatches)
        {
            var stringMatches = Regex.Matches(m.Groups[1].Value, @"<string>([^<]+)</string>");
            foreach (Match sm in stringMatches)
            {
                grammar.FileTypes.Add(sm.Groups[1].Value);
            }
        }

        return grammar;
    }

    private List<TextMatePattern> ParsePatterns(JsonElement patternsElement)
    {
        var patterns = new List<TextMatePattern>();

        foreach (var patternEl in patternsElement.EnumerateArray())
        {
            var pattern = ParsePattern(patternEl);
            if (pattern != null)
            {
                patterns.Add(pattern);
            }
        }

        return patterns;
    }

    private TextMatePattern? ParsePattern(JsonElement element)
    {
        var pattern = new TextMatePattern();

        if (element.TryGetProperty("name", out var name))
        {
            pattern.Name = name.GetString();
        }

        if (element.TryGetProperty("match", out var match))
        {
            pattern.Match = match.GetString();
        }

        if (element.TryGetProperty("begin", out var begin))
        {
            pattern.Begin = begin.GetString();
        }

        if (element.TryGetProperty("end", out var end))
        {
            pattern.End = end.GetString();
        }

        if (element.TryGetProperty("while", out var whileEl))
        {
            pattern.While = whileEl.GetString();
        }

        if (element.TryGetProperty("contentName", out var contentName))
        {
            pattern.ContentName = contentName.GetString();
        }

        if (element.TryGetProperty("include", out var include))
        {
            pattern.Include = include.GetString();
        }

        if (element.TryGetProperty("captures", out var captures))
        {
            pattern.Captures = ParseCaptures(captures);
        }

        if (element.TryGetProperty("beginCaptures", out var beginCaptures))
        {
            pattern.BeginCaptures = ParseCaptures(beginCaptures);
        }

        if (element.TryGetProperty("endCaptures", out var endCaptures))
        {
            pattern.EndCaptures = ParseCaptures(endCaptures);
        }

        if (element.TryGetProperty("patterns", out var nestedPatterns))
        {
            pattern.Patterns = ParsePatterns(nestedPatterns);
        }

        if (element.TryGetProperty("applyEndPatternLast", out var aepl))
        {
            pattern.ApplyEndPatternLast = aepl.GetBoolean();
        }

        return pattern;
    }

    private Dictionary<string, CapturePattern> ParseCaptures(JsonElement element)
    {
        var captures = new Dictionary<string, CapturePattern>();

        foreach (var prop in element.EnumerateObject())
        {
            var capture = new CapturePattern();

            if (prop.Value.TryGetProperty("name", out var name))
            {
                capture.Name = name.GetString();
            }

            if (prop.Value.TryGetProperty("patterns", out var patterns))
            {
                capture.Patterns = ParsePatterns(patterns);
            }

            captures[prop.Name] = capture;
        }

        return captures;
    }

    private TextMateToken? FindBestMatch(string line, int position, List<TextMatePattern> patterns, TextMateGrammar grammar, TokenizerState state)
    {
        TextMateToken? bestMatch = null;
        var bestStart = int.MaxValue;

        foreach (var pattern in patterns)
        {
            var token = TryMatchPattern(line, position, pattern, grammar, state);
            if (token != null && token.StartIndex < bestStart)
            {
                bestMatch = token;
                bestStart = token.StartIndex;
            }
        }

        return bestMatch;
    }

    private TextMateToken? TryMatchPattern(string line, int position, TextMatePattern pattern, TextMateGrammar grammar, TokenizerState state)
    {
        // Handle include
        if (!string.IsNullOrEmpty(pattern.Include))
        {
            var includeName = pattern.Include;
            if (includeName.StartsWith("#"))
            {
                var repoName = includeName.Substring(1);
                if (grammar.Repository.TryGetValue(repoName, out var repoPattern))
                {
                    return TryMatchPattern(line, position, repoPattern, grammar, state);
                }
            }
            return null;
        }

        // Handle match pattern
        if (!string.IsNullOrEmpty(pattern.Match))
        {
            try
            {
                var regex = GetOrCreateRegex(pattern.Match);
                var match = regex.Match(line, position);
                if (match.Success && match.Index == position)
                {
                    var scopes = new List<string>(state.ScopeStack);
                    if (!string.IsNullOrEmpty(pattern.Name))
                    {
                        scopes.Add(pattern.Name);
                    }

                    return new TextMateToken
                    {
                        StartIndex = match.Index,
                        EndIndex = match.Index + match.Length,
                        Scopes = scopes
                    };
                }
            }
            catch
            {
                // Invalid regex
            }
        }

        // Handle begin/end patterns (simplified - just match begin for now)
        if (!string.IsNullOrEmpty(pattern.Begin))
        {
            try
            {
                var regex = GetOrCreateRegex(pattern.Begin);
                var match = regex.Match(line, position);
                if (match.Success && match.Index == position)
                {
                    var scopes = new List<string>(state.ScopeStack);
                    if (!string.IsNullOrEmpty(pattern.Name))
                    {
                        scopes.Add(pattern.Name);
                    }

                    return new TextMateToken
                    {
                        StartIndex = match.Index,
                        EndIndex = match.Index + match.Length,
                        Scopes = scopes
                    };
                }
            }
            catch
            {
                // Invalid regex
            }
        }

        return null;
    }

    private Regex GetOrCreateRegex(string pattern)
    {
        if (!_regexCache.TryGetValue(pattern, out var regex))
        {
            // Convert some common TextMate regex syntax to .NET
            var dotNetPattern = ConvertRegexSyntax(pattern);
            regex = new Regex(dotNetPattern, RegexOptions.Compiled | RegexOptions.Multiline, TimeSpan.FromMilliseconds(100));
            _regexCache[pattern] = regex;
        }
        return regex;
    }

    private static string ConvertRegexSyntax(string pattern)
    {
        // TextMate uses Oniguruma regex, which has some differences from .NET
        // This is a simplified conversion
        var result = pattern;

        // Handle possessive quantifiers (not supported in .NET, convert to regular)
        result = Regex.Replace(result, @"\+\+", "+");
        result = Regex.Replace(result, @"\*\+", "*");
        result = Regex.Replace(result, @"\?\+", "?");

        return result;
    }

    private static List<TextMateToken> MergeTokens(List<TextMateToken> tokens)
    {
        if (tokens.Count <= 1) return tokens;

        var merged = new List<TextMateToken>();
        var current = tokens[0];

        for (int i = 1; i < tokens.Count; i++)
        {
            var next = tokens[i];
            if (current.EndIndex == next.StartIndex &&
                current.Scopes.SequenceEqual(next.Scopes))
            {
                current = new TextMateToken
                {
                    StartIndex = current.StartIndex,
                    EndIndex = next.EndIndex,
                    Scopes = current.Scopes
                };
            }
            else
            {
                merged.Add(current);
                current = next;
            }
        }

        merged.Add(current);
        return merged;
    }

    private static bool MatchesScope(string scope, string ruleScope)
    {
        // A scope matches if it equals or starts with the rule scope followed by a dot
        return scope.Equals(ruleScope, StringComparison.OrdinalIgnoreCase) ||
               scope.StartsWith(ruleScope + ".", StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
