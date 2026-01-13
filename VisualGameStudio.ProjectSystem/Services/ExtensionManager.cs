using System.Text.Json;
using System.Text.Json.Serialization;
using VisualGameStudio.Core.Extensions;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Manages importing VS Code extensions into Visual Game Studio
/// </summary>
public class ExtensionManager : IExtensionManager
{
    private readonly List<LanguageServerConfig> _languageServers = new();
    private readonly List<DebugAdapterConfig> _debugAdapters = new();
    private readonly List<ImportedTheme> _themes = new();
    private readonly Dictionary<string, List<ImportedSnippet>> _snippets = new();
    private readonly Dictionary<string, TextMateGrammar> _grammars = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public async Task<IReadOnlyList<VSCodeExtension>> ScanExtensionsAsync(string directory)
    {
        var extensions = new List<VSCodeExtension>();

        if (!Directory.Exists(directory))
            return extensions;

        // VS Code extensions are in subdirectories named publisher.name-version
        foreach (var extDir in Directory.GetDirectories(directory))
        {
            var packagePath = Path.Combine(extDir, "package.json");
            if (File.Exists(packagePath))
            {
                try
                {
                    var extension = await LoadExtensionAsync(extDir);
                    if (extension != null)
                    {
                        extensions.Add(extension);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load extension from {extDir}: {ex.Message}");
                }
            }
        }

        return extensions;
    }

    private async Task<VSCodeExtension?> LoadExtensionAsync(string extensionPath)
    {
        var packagePath = Path.Combine(extensionPath, "package.json");
        if (!File.Exists(packagePath))
            return null;

        var json = await File.ReadAllTextAsync(packagePath);
        var packageJson = JsonSerializer.Deserialize<PackageJson>(json, JsonOptions);

        if (packageJson == null)
            return null;

        return new VSCodeExtension
        {
            Id = $"{packageJson.Publisher}.{packageJson.Name}",
            Name = packageJson.DisplayName ?? packageJson.Name ?? "",
            Publisher = packageJson.Publisher ?? "",
            Version = packageJson.Version ?? "",
            Description = packageJson.Description,
            ExtensionPath = extensionPath,
            Contributes = packageJson.Contributes
        };
    }

    public async Task<ExtensionImportResult> ImportExtensionAsync(string extensionPath)
    {
        var result = new ExtensionImportResult();

        try
        {
            var extension = await LoadExtensionAsync(extensionPath);
            if (extension == null)
            {
                result.ErrorMessage = "Failed to load extension package.json";
                return result;
            }

            result.ExtensionId = extension.Id;

            // Import grammars (TextMate)
            foreach (var grammar in extension.Grammars)
            {
                try
                {
                    var imported = await ImportGrammarAsync(extensionPath, grammar, extension.Id);
                    if (imported != null)
                    {
                        _grammars[imported.LanguageId] = imported;
                        result.ImportedComponents.Add($"Grammar: {imported.ScopeName}");
                    }
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Failed to import grammar {grammar.ScopeName}: {ex.Message}");
                }
            }

            // Import themes
            foreach (var theme in extension.Themes)
            {
                try
                {
                    var imported = await ImportThemeAsync(extensionPath, theme, extension.Id);
                    if (imported != null)
                    {
                        _themes.Add(imported);
                        result.ImportedComponents.Add($"Theme: {imported.Name}");
                    }
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Failed to import theme {theme.Label}: {ex.Message}");
                }
            }

            // Import snippets
            foreach (var snippet in extension.Snippets)
            {
                try
                {
                    var imported = await ImportSnippetsAsync(extensionPath, snippet, extension.Id);
                    if (imported.Count > 0)
                    {
                        if (!_snippets.ContainsKey(snippet.Language))
                            _snippets[snippet.Language] = new List<ImportedSnippet>();
                        _snippets[snippet.Language].AddRange(imported);
                        result.ImportedComponents.Add($"Snippets: {imported.Count} for {snippet.Language}");
                    }
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Failed to import snippets for {snippet.Language}: {ex.Message}");
                }
            }

            // Import debuggers
            foreach (var debugger in extension.Debuggers)
            {
                try
                {
                    var config = ImportDebugAdapter(extensionPath, debugger, extension.Id);
                    if (config != null)
                    {
                        _debugAdapters.Add(config);
                        result.ImportedComponents.Add($"Debugger: {config.Name}");
                    }
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Failed to import debugger {debugger.Type}: {ex.Message}");
                }
            }

            result.Success = true;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    public async Task<IReadOnlyList<ExtensionImportResult>> ImportFromVSCodeAsync()
    {
        var results = new List<ExtensionImportResult>();

        // Find VS Code extensions directory
        var vsCodeExtDir = GetVSCodeExtensionsPath();
        if (vsCodeExtDir == null || !Directory.Exists(vsCodeExtDir))
        {
            return results;
        }

        var extensions = await ScanExtensionsAsync(vsCodeExtDir);
        foreach (var ext in extensions)
        {
            var result = await ImportExtensionAsync(ext.ExtensionPath);
            results.Add(result);
        }

        return results;
    }

    private string? GetVSCodeExtensionsPath()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Try different VS Code variants
        var paths = new[]
        {
            Path.Combine(userProfile, ".vscode", "extensions"),
            Path.Combine(userProfile, ".vscode-insiders", "extensions"),
            Path.Combine(userProfile, ".vscode-oss", "extensions"),
        };

        return paths.FirstOrDefault(Directory.Exists);
    }

    private async Task<TextMateGrammar?> ImportGrammarAsync(string extensionPath, GrammarContribution grammar, string sourceExtension)
    {
        var grammarPath = Path.Combine(extensionPath, grammar.Path.TrimStart('/'));
        if (!File.Exists(grammarPath))
            return null;

        var content = await File.ReadAllTextAsync(grammarPath);

        var imported = new TextMateGrammar
        {
            ScopeName = grammar.ScopeName,
            LanguageId = grammar.Language ?? "",
            SourcePath = grammarPath,
            SourceExtension = sourceExtension,
            Content = content
        };

        // Parse the grammar
        try
        {
            if (grammarPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                var parsed = JsonSerializer.Deserialize<TextMateGrammarJson>(content, JsonOptions);
                if (parsed != null)
                {
                    imported.Patterns = ConvertPatterns(parsed.Patterns);
                    if (parsed.Repository != null)
                    {
                        imported.Repository = parsed.Repository.ToDictionary(
                            kvp => kvp.Key,
                            kvp => ConvertPattern(kvp.Value)
                        );
                    }
                    if (parsed.FileTypes != null)
                    {
                        imported.FileExtensions = parsed.FileTypes.Select(ft => ft.StartsWith(".") ? ft : "." + ft).ToList();
                    }
                }
            }
            // TODO: Support plist format
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to parse grammar: {ex.Message}");
        }

        return imported;
    }

    private List<GrammarPattern> ConvertPatterns(List<TextMatePatternJson>? patterns)
    {
        if (patterns == null) return new List<GrammarPattern>();
        return patterns.Select(ConvertPattern).ToList();
    }

    private GrammarPattern ConvertPattern(TextMatePatternJson p)
    {
        return new GrammarPattern
        {
            Name = p.Name,
            Match = p.Match,
            Begin = p.Begin,
            End = p.End,
            Include = p.Include,
            ContentName = p.ContentName,
            Patterns = ConvertPatterns(p.Patterns),
            Captures = p.Captures?.ToDictionary(
                kvp => kvp.Key,
                kvp => new CapturePattern { Name = kvp.Value.Name }
            ),
            BeginCaptures = p.BeginCaptures?.ToDictionary(
                kvp => kvp.Key,
                kvp => new CapturePattern { Name = kvp.Value.Name }
            ),
            EndCaptures = p.EndCaptures?.ToDictionary(
                kvp => kvp.Key,
                kvp => new CapturePattern { Name = kvp.Value.Name }
            )
        };
    }

    private async Task<ImportedTheme?> ImportThemeAsync(string extensionPath, ThemeContribution theme, string sourceExtension)
    {
        var themePath = Path.Combine(extensionPath, theme.Path.TrimStart('/'));
        if (!File.Exists(themePath))
            return null;

        var content = await File.ReadAllTextAsync(themePath);
        var themeJson = JsonSerializer.Deserialize<VSCodeThemeJson>(content, JsonOptions);

        if (themeJson == null)
            return null;

        var imported = new ImportedTheme
        {
            Id = theme.Id ?? theme.Label.ToLowerInvariant().Replace(" ", "-"),
            Name = theme.Label,
            SourceExtension = sourceExtension,
            IsDark = theme.UiTheme?.Contains("dark", StringComparison.OrdinalIgnoreCase) ??
                     themeJson.Type?.Equals("dark", StringComparison.OrdinalIgnoreCase) ?? true
        };

        // Import colors
        if (themeJson.Colors != null)
        {
            foreach (var kvp in themeJson.Colors)
            {
                if (kvp.Value is JsonElement elem && elem.ValueKind == JsonValueKind.String)
                {
                    imported.Colors[kvp.Key] = elem.GetString() ?? "";
                }
            }
        }

        // Import token colors
        if (themeJson.TokenColors != null)
        {
            foreach (var tc in themeJson.TokenColors)
            {
                var rule = new TokenColorRule
                {
                    Name = tc.Name
                };

                if (tc.Scope is JsonElement scopeElem)
                {
                    if (scopeElem.ValueKind == JsonValueKind.String)
                    {
                        rule.Scope = new List<string> { scopeElem.GetString() ?? "" };
                    }
                    else if (scopeElem.ValueKind == JsonValueKind.Array)
                    {
                        rule.Scope = scopeElem.EnumerateArray()
                            .Select(e => e.GetString() ?? "")
                            .ToList();
                    }
                }

                if (tc.Settings != null)
                {
                    rule.Settings = new TokenColorSettings
                    {
                        Foreground = tc.Settings.Foreground,
                        Background = tc.Settings.Background,
                        FontStyle = tc.Settings.FontStyle
                    };
                }

                imported.TokenColors.Add(rule);
            }
        }

        return imported;
    }

    private async Task<List<ImportedSnippet>> ImportSnippetsAsync(string extensionPath, SnippetContribution snippet, string sourceExtension)
    {
        var snippetPath = Path.Combine(extensionPath, snippet.Path.TrimStart('/'));
        if (!File.Exists(snippetPath))
            return new List<ImportedSnippet>();

        var content = await File.ReadAllTextAsync(snippetPath);
        var snippetsJson = JsonSerializer.Deserialize<Dictionary<string, VSCodeSnippetJson>>(content, JsonOptions);

        if (snippetsJson == null)
            return new List<ImportedSnippet>();

        var imported = new List<ImportedSnippet>();
        foreach (var kvp in snippetsJson)
        {
            var s = kvp.Value;
            var body = new List<string>();

            if (s.Body is JsonElement bodyElem)
            {
                if (bodyElem.ValueKind == JsonValueKind.String)
                {
                    body.Add(bodyElem.GetString() ?? "");
                }
                else if (bodyElem.ValueKind == JsonValueKind.Array)
                {
                    body = bodyElem.EnumerateArray()
                        .Select(e => e.GetString() ?? "")
                        .ToList();
                }
            }

            var prefix = "";
            if (s.Prefix is JsonElement prefixElem)
            {
                if (prefixElem.ValueKind == JsonValueKind.String)
                {
                    prefix = prefixElem.GetString() ?? "";
                }
                else if (prefixElem.ValueKind == JsonValueKind.Array)
                {
                    prefix = prefixElem.EnumerateArray().FirstOrDefault().GetString() ?? "";
                }
            }

            imported.Add(new ImportedSnippet
            {
                Name = kvp.Key,
                Prefix = prefix,
                Body = body,
                Description = s.Description,
                Language = snippet.Language,
                SourceExtension = sourceExtension
            });
        }

        return imported;
    }

    private DebugAdapterConfig? ImportDebugAdapter(string extensionPath, DebuggerContribution debugger, string sourceExtension)
    {
        if (string.IsNullOrEmpty(debugger.Program))
            return null;

        var programPath = Path.Combine(extensionPath, debugger.Program.TrimStart('/'));

        return new DebugAdapterConfig
        {
            Id = $"{sourceExtension}.{debugger.Type}",
            Name = debugger.Label ?? debugger.Type,
            Type = debugger.Type,
            Languages = debugger.Languages ?? new List<string>(),
            StartInfo = new ServerStartInfo
            {
                Command = debugger.Runtime ?? "node",
                Arguments = new List<string> { programPath }.Concat(debugger.Args ?? new List<string>()).ToList(),
                Transport = TransportType.Stdio
            }
        };
    }

    public IReadOnlyList<LanguageServerConfig> GetLanguageServers() => _languageServers.AsReadOnly();
    public IReadOnlyList<DebugAdapterConfig> GetDebugAdapters() => _debugAdapters.AsReadOnly();
    public IReadOnlyList<ImportedTheme> GetThemes() => _themes.AsReadOnly();

    public IReadOnlyList<ImportedSnippet> GetSnippets(string languageId)
    {
        return _snippets.TryGetValue(languageId, out var snippets)
            ? snippets.AsReadOnly()
            : new List<ImportedSnippet>().AsReadOnly();
    }

    public TextMateGrammar? GetGrammar(string languageId)
    {
        return _grammars.TryGetValue(languageId, out var grammar) ? grammar : null;
    }

    // JSON model classes for deserialization
    private class PackageJson
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("publisher")]
        public string? Publisher { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("contributes")]
        public ExtensionContributes? Contributes { get; set; }
    }

    private class TextMateGrammarJson
    {
        [JsonPropertyName("scopeName")]
        public string? ScopeName { get; set; }

        [JsonPropertyName("fileTypes")]
        public List<string>? FileTypes { get; set; }

        [JsonPropertyName("patterns")]
        public List<TextMatePatternJson>? Patterns { get; set; }

        [JsonPropertyName("repository")]
        public Dictionary<string, TextMatePatternJson>? Repository { get; set; }
    }

    private class TextMatePatternJson
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("match")]
        public string? Match { get; set; }

        [JsonPropertyName("begin")]
        public string? Begin { get; set; }

        [JsonPropertyName("end")]
        public string? End { get; set; }

        [JsonPropertyName("include")]
        public string? Include { get; set; }

        [JsonPropertyName("contentName")]
        public string? ContentName { get; set; }

        [JsonPropertyName("patterns")]
        public List<TextMatePatternJson>? Patterns { get; set; }

        [JsonPropertyName("captures")]
        public Dictionary<string, TextMateCaptureJson>? Captures { get; set; }

        [JsonPropertyName("beginCaptures")]
        public Dictionary<string, TextMateCaptureJson>? BeginCaptures { get; set; }

        [JsonPropertyName("endCaptures")]
        public Dictionary<string, TextMateCaptureJson>? EndCaptures { get; set; }
    }

    private class TextMateCaptureJson
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private class VSCodeThemeJson
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("colors")]
        public Dictionary<string, object>? Colors { get; set; }

        [JsonPropertyName("tokenColors")]
        public List<TokenColorJson>? TokenColors { get; set; }
    }

    private class TokenColorJson
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("scope")]
        public object? Scope { get; set; }

        [JsonPropertyName("settings")]
        public TokenSettingsJson? Settings { get; set; }
    }

    private class TokenSettingsJson
    {
        [JsonPropertyName("foreground")]
        public string? Foreground { get; set; }

        [JsonPropertyName("background")]
        public string? Background { get; set; }

        [JsonPropertyName("fontStyle")]
        public string? FontStyle { get; set; }
    }

    private class VSCodeSnippetJson
    {
        [JsonPropertyName("prefix")]
        public object? Prefix { get; set; }

        [JsonPropertyName("body")]
        public object? Body { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }
}
