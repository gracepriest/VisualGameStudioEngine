using System.Text.Json;
using System.Text.Json.Serialization;
using VisualGameStudio.Core.Abstractions.Services;
using ITextMateService = VisualGameStudio.Core.Abstractions.Services.ITextMateService;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Loads and registers TextMate grammars from installed VS Code extensions.
/// Parses .tmLanguage.json, .tmLanguage, and .json grammar files and registers them
/// with the IDE's TextMateService for syntax highlighting.
/// </summary>
public class TextMateRegistrar
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ITextMateService _textMateService;

    /// <summary>
    /// Maps language IDs to their grammar scope names (e.g., "python" -> "source.python").
    /// </summary>
    private readonly Dictionary<string, string> _languageToScope = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Maps file extensions to language IDs (e.g., ".py" -> "python").
    /// </summary>
    private readonly Dictionary<string, string> _extensionToLanguage = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Maps language IDs to their display names (e.g., "python" -> "Python").
    /// </summary>
    private readonly Dictionary<string, string> _languageNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Maps language IDs to their file extensions.
    /// </summary>
    private readonly Dictionary<string, List<string>> _languageExtensions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Maps scope names to their injection targets for grammar injection.
    /// </summary>
    private readonly Dictionary<string, List<string>> _injectionGrammars = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Tracks which grammars have been loaded, keyed by scope name.
    /// </summary>
    private readonly Dictionary<string, LoadedGrammarInfo> _loadedGrammars = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Callback invoked after a grammar file is loaded. Used by the editor to convert the
    /// grammar into an AvalonEdit highlighting definition.
    /// Parameters: languageId, languageName, fileExtensions, grammarFilePath, grammarJsonContent.
    /// </summary>
    public Action<string, string, IEnumerable<string>, string, string?>? OnGrammarLoaded { get; set; }

    /// <summary>
    /// Callback invoked when a language configuration is loaded from an extension.
    /// Parameters: languageId, config.
    /// </summary>
    public Action<string, LanguageConfiguration>? OnLanguageConfigurationLoaded { get; set; }

    /// <summary>
    /// Raised when a grammar is registered from an extension.
    /// </summary>
    public event EventHandler<GrammarRegisteredEventArgs>? GrammarRegistered;

    public TextMateRegistrar(ITextMateService textMateService)
    {
        _textMateService = textMateService ?? throw new ArgumentNullException(nameof(textMateService));
    }

    /// <summary>
    /// Registers all grammars and languages from a single extension's package.json.
    /// </summary>
    /// <param name="extensionPath">Root directory of the installed extension.</param>
    /// <param name="extensionId">Extension ID (publisher.name) for tracking.</param>
    /// <returns>Number of grammars successfully registered.</returns>
    public async Task<int> RegisterFromExtensionAsync(string extensionPath, string extensionId)
    {
        var packageJsonPath = Path.Combine(extensionPath, "package.json");
        if (!File.Exists(packageJsonPath))
            return 0;

        try
        {
            var json = await File.ReadAllTextAsync(packageJsonPath);
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            var root = doc.RootElement;
            if (!root.TryGetProperty("contributes", out var contributes))
                return 0;

            // Step 1: Register language contributions (file extensions, aliases)
            if (contributes.TryGetProperty("languages", out var languages))
            {
                RegisterLanguages(languages, extensionPath);
            }

            // Step 2: Register grammar contributions
            var count = 0;
            if (contributes.TryGetProperty("grammars", out var grammars))
            {
                foreach (var grammar in grammars.EnumerateArray())
                {
                    if (await RegisterGrammarAsync(grammar, extensionPath, extensionId))
                    {
                        count++;
                    }
                }
            }

            return count;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to register grammars from {extensionId}: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Unregisters all grammars that were loaded from the specified extension.
    /// </summary>
    /// <param name="extensionId">Extension ID to remove grammars for.</param>
    public void UnregisterExtension(string extensionId)
    {
        var toRemove = _loadedGrammars
            .Where(kvp => kvp.Value.ExtensionId.Equals(extensionId, StringComparison.OrdinalIgnoreCase))
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var scopeName in toRemove)
        {
            _loadedGrammars.Remove(scopeName);
        }

        // Also clean up language/extension mappings from this extension
        var languagesToRemove = _extensionToLanguage
            .Where(kvp => _loadedGrammars.Values.All(g => g.LanguageId != kvp.Value))
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var ext in languagesToRemove)
        {
            _extensionToLanguage.Remove(ext);
        }
    }

    /// <summary>
    /// Gets the language ID for a file extension.
    /// </summary>
    /// <param name="fileExtension">File extension including the dot (e.g., ".py").</param>
    public string? GetLanguageId(string fileExtension)
    {
        var ext = fileExtension.StartsWith(".") ? fileExtension : "." + fileExtension;
        return _extensionToLanguage.TryGetValue(ext, out var langId) ? langId : null;
    }

    /// <summary>
    /// Gets the grammar scope name for a language ID.
    /// </summary>
    /// <param name="languageId">Language ID (e.g., "python").</param>
    public string? GetScopeForLanguage(string languageId)
    {
        return _languageToScope.TryGetValue(languageId, out var scope) ? scope : null;
    }

    /// <summary>
    /// Gets all injection grammars that target the given scope.
    /// </summary>
    /// <param name="targetScope">The target grammar scope name.</param>
    public IReadOnlyList<string> GetInjectionGrammars(string targetScope)
    {
        return _injectionGrammars.TryGetValue(targetScope, out var injections)
            ? injections
            : new List<string>();
    }

    /// <summary>
    /// Gets info about all loaded grammars.
    /// </summary>
    public IReadOnlyDictionary<string, LoadedGrammarInfo> LoadedGrammars => _loadedGrammars;

    /// <summary>
    /// Gets the file extensions registered for a language ID.
    /// </summary>
    public IReadOnlyList<string> GetFileExtensions(string languageId)
    {
        return _languageExtensions.TryGetValue(languageId, out var exts) ? exts : new List<string>();
    }

    /// <summary>
    /// Gets the display name for a language ID.
    /// </summary>
    public string GetLanguageName(string languageId)
    {
        return _languageNames.TryGetValue(languageId, out var name) ? name : languageId;
    }

    #region Private Methods

    private void RegisterLanguages(JsonElement languages, string extensionPath)
    {
        foreach (var lang in languages.EnumerateArray())
        {
            var id = lang.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            if (string.IsNullOrEmpty(id))
                continue;

            // Get display name from aliases or id
            if (lang.TryGetProperty("aliases", out var aliases))
            {
                var aliasList = aliases.EnumerateArray().ToList();
                if (aliasList.Count > 0)
                {
                    var displayName = aliasList[0].GetString();
                    if (!string.IsNullOrEmpty(displayName))
                    {
                        _languageNames[id] = displayName;
                    }
                }
            }
            if (!_languageNames.ContainsKey(id))
            {
                _languageNames[id] = id;
            }

            // Register file extensions
            if (lang.TryGetProperty("extensions", out var extensions))
            {
                if (!_languageExtensions.ContainsKey(id))
                    _languageExtensions[id] = new List<string>();

                foreach (var ext in extensions.EnumerateArray())
                {
                    var extStr = ext.GetString();
                    if (!string.IsNullOrEmpty(extStr))
                    {
                        var normalized = extStr.StartsWith(".") ? extStr : "." + extStr;
                        var normalizedLower = normalized.ToLowerInvariant();
                        _extensionToLanguage[normalizedLower] = id;

                        if (!_languageExtensions[id].Contains(normalizedLower))
                            _languageExtensions[id].Add(normalizedLower);
                    }
                }
            }

            // Register filenames (e.g., "Makefile", "Dockerfile")
            if (lang.TryGetProperty("filenames", out var filenames))
            {
                foreach (var fn in filenames.EnumerateArray())
                {
                    var fnStr = fn.GetString();
                    if (!string.IsNullOrEmpty(fnStr))
                    {
                        // Store filename-to-language mappings with a special prefix
                        _extensionToLanguage["@" + fnStr.ToLowerInvariant()] = id;
                    }
                }
            }

            // Load language configuration if specified
            if (lang.TryGetProperty("configuration", out var configEl))
            {
                var configPath = configEl.GetString();
                if (!string.IsNullOrEmpty(configPath))
                {
                    var fullPath = Path.Combine(extensionPath, configPath.TrimStart('/'));
                    if (File.Exists(fullPath))
                    {
                        _ = LoadLanguageConfigurationAsync(fullPath, id);
                    }
                }
            }
        }
    }

    private async Task<bool> RegisterGrammarAsync(JsonElement grammar, string extensionPath, string extensionId)
    {
        try
        {
            var scopeName = grammar.TryGetProperty("scopeName", out var scopeEl) ? scopeEl.GetString() : null;
            var grammarPath = grammar.TryGetProperty("path", out var pathEl) ? pathEl.GetString() : null;
            var languageId = grammar.TryGetProperty("language", out var langEl) ? langEl.GetString() : null;

            if (string.IsNullOrEmpty(scopeName) || string.IsNullOrEmpty(grammarPath))
                return false;

            // Resolve the full path to the grammar file
            var fullPath = Path.Combine(extensionPath, grammarPath.TrimStart('/'));
            if (!File.Exists(fullPath))
            {
                System.Diagnostics.Debug.WriteLine($"Grammar file not found: {fullPath}");
                return false;
            }

            // Map language to scope
            if (!string.IsNullOrEmpty(languageId))
            {
                _languageToScope[languageId] = scopeName;

                // Register file extensions for this language with the TextMateService
                var fileExts = _extensionToLanguage
                    .Where(kvp => kvp.Value.Equals(languageId, StringComparison.OrdinalIgnoreCase) && !kvp.Key.StartsWith("@"))
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var ext in fileExts)
                {
                    _textMateService.RegisterExtension(ext, scopeName);
                }
            }

            // Handle injection grammars (grammar that injects into other grammars)
            if (grammar.TryGetProperty("injectTo", out var injectToEl))
            {
                foreach (var target in injectToEl.EnumerateArray())
                {
                    var targetScope = target.GetString();
                    if (!string.IsNullOrEmpty(targetScope))
                    {
                        if (!_injectionGrammars.TryGetValue(targetScope, out var list))
                        {
                            list = new List<string>();
                            _injectionGrammars[targetScope] = list;
                        }
                        if (!list.Contains(scopeName))
                        {
                            list.Add(scopeName);
                        }
                    }
                }
            }

            // Handle embedded languages
            Dictionary<string, string>? embeddedLanguages = null;
            if (grammar.TryGetProperty("embeddedLanguages", out var embeddedEl))
            {
                embeddedLanguages = new Dictionary<string, string>();
                foreach (var prop in embeddedEl.EnumerateObject())
                {
                    embeddedLanguages[prop.Name] = prop.Value.GetString() ?? "";
                }
            }

            // Load the grammar file into the TextMateService
            var loadedGrammar = await _textMateService.LoadGrammarAsync(fullPath);

            // Track loaded grammar info
            _loadedGrammars[scopeName] = new LoadedGrammarInfo
            {
                ScopeName = scopeName,
                LanguageId = languageId ?? "",
                FilePath = fullPath,
                ExtensionId = extensionId,
                EmbeddedLanguages = embeddedLanguages,
                IsInjection = grammar.TryGetProperty("injectTo", out _)
            };

            // Notify editor to convert grammar for AvalonEdit highlighting
            if (!string.IsNullOrEmpty(languageId) && OnGrammarLoaded != null)
            {
                try
                {
                    var displayName = GetLanguageName(languageId);
                    var fileExts2 = GetFileExtensions(languageId);
                    string? grammarContent = null;
                    try { grammarContent = await File.ReadAllTextAsync(fullPath); } catch { }
                    OnGrammarLoaded(languageId, displayName, fileExts2, fullPath, grammarContent);
                }
                catch (Exception convEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to convert grammar for AvalonEdit: {convEx.Message}");
                }
            }

            GrammarRegistered?.Invoke(this, new GrammarRegisteredEventArgs(
                scopeName, languageId ?? "", extensionId, fullPath));

            return loadedGrammar != null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to register grammar: {ex.Message}");
            return false;
        }
    }

    private async Task LoadLanguageConfigurationAsync(string configPath, string languageId)
    {
        try
        {
            var json = await File.ReadAllTextAsync(configPath);
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            var root = doc.RootElement;
            var config = new LanguageConfiguration { LanguageId = languageId };

            // Parse comments
            if (root.TryGetProperty("comments", out var comments))
            {
                if (comments.TryGetProperty("lineComment", out var lineComment))
                {
                    config.LineComment = lineComment.GetString();
                }
                if (comments.TryGetProperty("blockComment", out var blockComment) &&
                    blockComment.ValueKind == JsonValueKind.Array)
                {
                    var arr = blockComment.EnumerateArray().ToList();
                    if (arr.Count >= 2)
                    {
                        config.BlockCommentStart = arr[0].GetString();
                        config.BlockCommentEnd = arr[1].GetString();
                    }
                }
            }

            // Parse brackets
            if (root.TryGetProperty("brackets", out var brackets))
            {
                foreach (var pair in brackets.EnumerateArray())
                {
                    var arr = pair.EnumerateArray().ToList();
                    if (arr.Count >= 2)
                    {
                        config.Brackets.Add((arr[0].GetString() ?? "", arr[1].GetString() ?? ""));
                    }
                }
            }

            // Parse auto-closing pairs
            if (root.TryGetProperty("autoClosingPairs", out var autoClose))
            {
                foreach (var pair in autoClose.EnumerateArray())
                {
                    if (pair.ValueKind == JsonValueKind.Array)
                    {
                        var arr = pair.EnumerateArray().ToList();
                        if (arr.Count >= 2)
                        {
                            config.AutoClosingPairs.Add((arr[0].GetString() ?? "", arr[1].GetString() ?? ""));
                        }
                    }
                    else if (pair.ValueKind == JsonValueKind.Object)
                    {
                        var open = pair.TryGetProperty("open", out var o) ? o.GetString() ?? "" : "";
                        var close = pair.TryGetProperty("close", out var c) ? c.GetString() ?? "" : "";
                        if (!string.IsNullOrEmpty(open))
                        {
                            config.AutoClosingPairs.Add((open, close));
                        }
                    }
                }
            }

            // Parse surrounding pairs
            if (root.TryGetProperty("surroundingPairs", out var surrounding))
            {
                foreach (var pair in surrounding.EnumerateArray())
                {
                    if (pair.ValueKind == JsonValueKind.Array)
                    {
                        var arr = pair.EnumerateArray().ToList();
                        if (arr.Count >= 2)
                        {
                            config.SurroundingPairs.Add((arr[0].GetString() ?? "", arr[1].GetString() ?? ""));
                        }
                    }
                    else if (pair.ValueKind == JsonValueKind.Object)
                    {
                        var open = pair.TryGetProperty("open", out var o) ? o.GetString() ?? "" : "";
                        var close = pair.TryGetProperty("close", out var c) ? c.GetString() ?? "" : "";
                        if (!string.IsNullOrEmpty(open))
                        {
                            config.SurroundingPairs.Add((open, close));
                        }
                    }
                }
            }

            // Parse folding markers
            if (root.TryGetProperty("folding", out var folding))
            {
                if (folding.TryGetProperty("markers", out var markers))
                {
                    if (markers.TryGetProperty("start", out var start))
                    {
                        config.FoldingStartMarker = start.GetString();
                    }
                    if (markers.TryGetProperty("end", out var end))
                    {
                        config.FoldingEndMarker = end.GetString();
                    }
                }
            }

            // Parse indentation rules
            if (root.TryGetProperty("indentationRules", out var indentation))
            {
                if (indentation.TryGetProperty("increaseIndentPattern", out var inc))
                {
                    config.IncreaseIndentPattern = inc.GetString();
                }
                if (indentation.TryGetProperty("decreaseIndentPattern", out var dec))
                {
                    config.DecreaseIndentPattern = dec.GetString();
                }
            }

            // Parse word pattern
            if (root.TryGetProperty("wordPattern", out var wordPattern))
            {
                config.WordPattern = wordPattern.GetString();
            }

            // Store configuration for later use
            _languageConfigurations[languageId] = config;

            // Notify editor about the loaded configuration
            OnLanguageConfigurationLoaded?.Invoke(languageId, config);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load language config for {languageId}: {ex.Message}");
        }
    }

    private readonly Dictionary<string, LanguageConfiguration> _languageConfigurations = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the language configuration for a language ID if one was loaded from an extension.
    /// </summary>
    public LanguageConfiguration? GetLanguageConfiguration(string languageId)
    {
        return _languageConfigurations.TryGetValue(languageId, out var config) ? config : null;
    }

    /// <summary>
    /// Gets all loaded language configurations.
    /// </summary>
    public IReadOnlyDictionary<string, LanguageConfiguration> LanguageConfigurations => _languageConfigurations;

    #endregion
}

#region Supporting Types

/// <summary>
/// Information about a grammar that was loaded from an extension.
/// </summary>
public class LoadedGrammarInfo
{
    /// <summary>
    /// TextMate scope name (e.g., "source.python").
    /// </summary>
    public string ScopeName { get; set; } = "";

    /// <summary>
    /// VS Code language ID (e.g., "python").
    /// </summary>
    public string LanguageId { get; set; } = "";

    /// <summary>
    /// Path to the grammar file on disk.
    /// </summary>
    public string FilePath { get; set; } = "";

    /// <summary>
    /// ID of the extension that provided this grammar.
    /// </summary>
    public string ExtensionId { get; set; } = "";

    /// <summary>
    /// Embedded language scope-to-languageId mappings (for grammars like HTML that embed JS/CSS).
    /// </summary>
    public Dictionary<string, string>? EmbeddedLanguages { get; set; }

    /// <summary>
    /// Whether this is an injection grammar (injects into another grammar).
    /// </summary>
    public bool IsInjection { get; set; }
}

/// <summary>
/// Language configuration loaded from a language-configuration.json file.
/// Provides bracket matching, auto-closing, comment tokens, and indentation rules.
/// </summary>
public class LanguageConfiguration
{
    /// <summary>
    /// Language ID this configuration applies to.
    /// </summary>
    public string LanguageId { get; set; } = "";

    /// <summary>
    /// Single-line comment prefix (e.g., "//", "#").
    /// </summary>
    public string? LineComment { get; set; }

    /// <summary>
    /// Block comment start token (e.g., "/*").
    /// </summary>
    public string? BlockCommentStart { get; set; }

    /// <summary>
    /// Block comment end token (e.g., "*/").
    /// </summary>
    public string? BlockCommentEnd { get; set; }

    /// <summary>
    /// Bracket pairs for matching (open, close).
    /// </summary>
    public List<(string Open, string Close)> Brackets { get; set; } = new();

    /// <summary>
    /// Pairs that auto-close when the opening character is typed.
    /// </summary>
    public List<(string Open, string Close)> AutoClosingPairs { get; set; } = new();

    /// <summary>
    /// Pairs used for surround-with functionality.
    /// </summary>
    public List<(string Open, string Close)> SurroundingPairs { get; set; } = new();

    /// <summary>
    /// Regex pattern that marks the start of a foldable region.
    /// </summary>
    public string? FoldingStartMarker { get; set; }

    /// <summary>
    /// Regex pattern that marks the end of a foldable region.
    /// </summary>
    public string? FoldingEndMarker { get; set; }

    /// <summary>
    /// Regex pattern for lines that should increase indent.
    /// </summary>
    public string? IncreaseIndentPattern { get; set; }

    /// <summary>
    /// Regex pattern for lines that should decrease indent.
    /// </summary>
    public string? DecreaseIndentPattern { get; set; }

    /// <summary>
    /// Word pattern regex for word selection.
    /// </summary>
    public string? WordPattern { get; set; }
}

/// <summary>
/// Event args raised when a grammar is registered from an extension.
/// </summary>
public class GrammarRegisteredEventArgs : EventArgs
{
    public string ScopeName { get; }
    public string LanguageId { get; }
    public string ExtensionId { get; }
    public string FilePath { get; }

    public GrammarRegisteredEventArgs(string scopeName, string languageId, string extensionId, string filePath)
    {
        ScopeName = scopeName;
        LanguageId = languageId;
        ExtensionId = extensionId;
        FilePath = filePath;
    }
}

#endregion
