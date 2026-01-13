using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using VisualGameStudio.Core.TextMate;

namespace VisualGameStudio.ProjectSystem.TextMate;

/// <summary>
/// Service for managing TextMate grammars and themes
/// </summary>
public class TextMateService : ITextMateService
{
    private readonly ConcurrentDictionary<string, TextMateGrammarInfo> _grammarsByScopeName = new();
    private readonly ConcurrentDictionary<string, TextMateGrammarInfo> _grammarsByLanguage = new();
    private readonly ConcurrentDictionary<string, TextMateGrammarInfo> _grammarsByExtension = new();
    private readonly JsonSerializerOptions _jsonOptions;

    public TextMateTheme? CurrentTheme { get; private set; }

    public TextMateService()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new JsonStringEnumConverter() }
        };

        // Register built-in grammars (basic language support)
        RegisterBuiltInGrammars();
    }

    private void RegisterBuiltInGrammars()
    {
        // BasicLang (our own language)
        RegisterGrammar(new TextMateGrammarInfo
        {
            ScopeName = "source.basiclang",
            LanguageId = "basiclang",
            Name = "BasicLang",
            FileExtensions = new List<string> { ".bl", ".bas" }
        });
    }

    public async Task<TextMateGrammarInfo?> LoadGrammarAsync(string grammarPath)
    {
        try
        {
            var content = await File.ReadAllTextAsync(grammarPath);
            var extension = Path.GetExtension(grammarPath).ToLowerInvariant();

            if (extension == ".json" || extension == ".tmLanguage.json")
            {
                return LoadGrammarFromJson(grammarPath, content);
            }
            else if (extension == ".plist" || extension == ".tmLanguage")
            {
                // TODO: Add plist parser for older TextMate grammars
                return null;
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public TextMateGrammarInfo? LoadGrammarFromJson(string scopeNameOrPath, string jsonContent)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonContent, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            var root = doc.RootElement;
            var grammar = new TextMateGrammarInfo
            {
                RawContent = jsonContent,
                SourcePath = scopeNameOrPath
            };

            // Parse scope name
            if (root.TryGetProperty("scopeName", out var scopeName))
            {
                grammar.ScopeName = scopeName.GetString() ?? "";
            }
            else if (!scopeNameOrPath.Contains(Path.DirectorySeparatorChar) &&
                     !scopeNameOrPath.Contains(Path.AltDirectorySeparatorChar))
            {
                grammar.ScopeName = scopeNameOrPath;
            }

            // Parse name
            if (root.TryGetProperty("name", out var name))
            {
                grammar.Name = name.GetString() ?? grammar.ScopeName;
            }

            // Parse file types
            if (root.TryGetProperty("fileTypes", out var fileTypes))
            {
                foreach (var ft in fileTypes.EnumerateArray())
                {
                    var ext = ft.GetString();
                    if (!string.IsNullOrEmpty(ext))
                    {
                        if (!ext.StartsWith('.'))
                            ext = "." + ext;
                        grammar.FileExtensions.Add(ext);
                    }
                }
            }

            // Parse first line match
            if (root.TryGetProperty("firstLineMatch", out var firstLine))
            {
                grammar.FirstLineMatch = firstLine.GetString();
            }

            // Parse patterns
            if (root.TryGetProperty("patterns", out var patterns))
            {
                grammar.Patterns = ParsePatterns(patterns);
            }

            // Parse repository
            if (root.TryGetProperty("repository", out var repository))
            {
                foreach (var prop in repository.EnumerateObject())
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
        catch (Exception)
        {
            return null;
        }
    }

    private List<TextMatePattern> ParsePatterns(JsonElement element)
    {
        var patterns = new List<TextMatePattern>();

        if (element.ValueKind != JsonValueKind.Array)
            return patterns;

        foreach (var item in element.EnumerateArray())
        {
            var pattern = ParsePattern(item);
            if (pattern != null)
            {
                patterns.Add(pattern);
            }
        }

        return patterns;
    }

    private TextMatePattern? ParsePattern(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        var pattern = new TextMatePattern();

        if (element.TryGetProperty("name", out var name))
            pattern.Name = name.GetString();

        if (element.TryGetProperty("match", out var match))
            pattern.Match = match.GetString();

        if (element.TryGetProperty("begin", out var begin))
            pattern.Begin = begin.GetString();

        if (element.TryGetProperty("end", out var end))
            pattern.End = end.GetString();

        if (element.TryGetProperty("while", out var whilePattern))
            pattern.While = whilePattern.GetString();

        if (element.TryGetProperty("include", out var include))
            pattern.Include = include.GetString();

        if (element.TryGetProperty("contentName", out var contentName))
            pattern.ContentName = contentName.GetString();

        if (element.TryGetProperty("captures", out var captures))
            pattern.Captures = ParseCaptures(captures);

        if (element.TryGetProperty("beginCaptures", out var beginCaptures))
            pattern.BeginCaptures = ParseCaptures(beginCaptures);

        if (element.TryGetProperty("endCaptures", out var endCaptures))
            pattern.EndCaptures = ParseCaptures(endCaptures);

        if (element.TryGetProperty("patterns", out var patterns))
            pattern.Patterns = ParsePatterns(patterns);

        return pattern;
    }

    private Dictionary<string, TextMateCapture>? ParseCaptures(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        var captures = new Dictionary<string, TextMateCapture>();

        foreach (var prop in element.EnumerateObject())
        {
            var capture = new TextMateCapture();

            if (prop.Value.TryGetProperty("name", out var name))
                capture.Name = name.GetString();

            if (prop.Value.TryGetProperty("patterns", out var patterns))
                capture.Patterns = ParsePatterns(patterns);

            captures[prop.Name] = capture;
        }

        return captures;
    }

    public TextMateGrammarInfo? GetGrammarForLanguage(string languageId)
    {
        _grammarsByLanguage.TryGetValue(languageId.ToLowerInvariant(), out var grammar);
        return grammar;
    }

    public TextMateGrammarInfo? GetGrammarForExtension(string extension)
    {
        var ext = extension.StartsWith('.') ? extension.ToLowerInvariant() : $".{extension.ToLowerInvariant()}";
        _grammarsByExtension.TryGetValue(ext, out var grammar);
        return grammar;
    }

    public void RegisterGrammar(TextMateGrammarInfo grammar)
    {
        _grammarsByScopeName[grammar.ScopeName] = grammar;

        if (!string.IsNullOrEmpty(grammar.LanguageId))
        {
            _grammarsByLanguage[grammar.LanguageId.ToLowerInvariant()] = grammar;
        }

        foreach (var ext in grammar.FileExtensions)
        {
            var key = ext.StartsWith('.') ? ext.ToLowerInvariant() : $".{ext.ToLowerInvariant()}";
            _grammarsByExtension[key] = grammar;
        }
    }

    public IReadOnlyList<TextMateGrammarInfo> GetAllGrammars()
    {
        return _grammarsByScopeName.Values.ToList();
    }

    public TextMateTheme? ConvertVSCodeTheme(string themePath)
    {
        try
        {
            var content = File.ReadAllText(themePath);
            return ConvertVSCodeThemeFromJson(themePath, content);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public TextMateTheme? ConvertVSCodeThemeFromJson(string themeNameOrPath, string jsonContent)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonContent, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            var root = doc.RootElement;
            var theme = new TextMateTheme();

            // Parse name
            if (root.TryGetProperty("name", out var name))
            {
                theme.Name = name.GetString() ?? Path.GetFileNameWithoutExtension(themeNameOrPath);
            }
            else
            {
                theme.Name = Path.GetFileNameWithoutExtension(themeNameOrPath);
            }

            theme.Id = theme.Name.ToLowerInvariant().Replace(" ", "-");

            // Detect if dark theme
            if (root.TryGetProperty("type", out var type))
            {
                theme.IsDark = type.GetString()?.Contains("dark", StringComparison.OrdinalIgnoreCase) ?? false;
            }

            // Parse colors
            if (root.TryGetProperty("colors", out var colors))
            {
                foreach (var prop in colors.EnumerateObject())
                {
                    var value = prop.Value.GetString();
                    if (!string.IsNullOrEmpty(value))
                    {
                        theme.Colors[prop.Name] = value;
                    }
                }
            }

            // Parse token colors
            if (root.TryGetProperty("tokenColors", out var tokenColors))
            {
                foreach (var item in tokenColors.EnumerateArray())
                {
                    var tokenColor = ParseTokenColor(item);
                    if (tokenColor != null)
                    {
                        theme.TokenColors.Add(tokenColor);
                    }
                }
            }

            // Handle "include" for base themes
            if (root.TryGetProperty("include", out var include))
            {
                var includeFile = include.GetString();
                if (!string.IsNullOrEmpty(includeFile))
                {
                    var basePath = Path.GetDirectoryName(themeNameOrPath);
                    if (!string.IsNullOrEmpty(basePath))
                    {
                        var includePath = Path.Combine(basePath, includeFile);
                        if (File.Exists(includePath))
                        {
                            var baseTheme = ConvertVSCodeTheme(includePath);
                            if (baseTheme != null)
                            {
                                // Merge base theme (current theme overrides base)
                                foreach (var kvp in baseTheme.Colors)
                                {
                                    if (!theme.Colors.ContainsKey(kvp.Key))
                                    {
                                        theme.Colors[kvp.Key] = kvp.Value;
                                    }
                                }

                                // Prepend base token colors (so current theme's rules take precedence)
                                theme.TokenColors.InsertRange(0, baseTheme.TokenColors);
                            }
                        }
                    }
                }
            }

            return theme;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private TextMateTokenColor? ParseTokenColor(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        var tokenColor = new TextMateTokenColor();

        // Parse name
        if (element.TryGetProperty("name", out var name))
        {
            tokenColor.Name = name.GetString();
        }

        // Parse scope (can be string or array)
        if (element.TryGetProperty("scope", out var scope))
        {
            if (scope.ValueKind == JsonValueKind.String)
            {
                var scopeStr = scope.GetString();
                if (!string.IsNullOrEmpty(scopeStr))
                {
                    // Scope can be comma-separated
                    tokenColor.Scope = scopeStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .ToList();
                }
            }
            else if (scope.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in scope.EnumerateArray())
                {
                    var scopeVal = s.GetString();
                    if (!string.IsNullOrEmpty(scopeVal))
                    {
                        tokenColor.Scope.Add(scopeVal);
                    }
                }
            }
        }

        // Parse settings
        if (element.TryGetProperty("settings", out var settings))
        {
            if (settings.TryGetProperty("foreground", out var fg))
                tokenColor.Settings.Foreground = fg.GetString();

            if (settings.TryGetProperty("background", out var bg))
                tokenColor.Settings.Background = bg.GetString();

            if (settings.TryGetProperty("fontStyle", out var fontStyle))
                tokenColor.Settings.FontStyle = fontStyle.GetString();
        }

        return tokenColor;
    }

    public void SetTheme(TextMateTheme theme)
    {
        CurrentTheme = theme;
    }
}
