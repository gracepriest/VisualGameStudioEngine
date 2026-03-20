using System.Reflection;
using System.Xml;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using VisualGameStudio.Core.Extensions;
using VisualGameStudio.Core.TextMate;

namespace VisualGameStudio.Editor.Highlighting;

public static class HighlightingLoader
{
    private static bool _isRegistered;
    private static IHighlightingDefinition? _basicLangDefinition;
    private static IHighlightingDefinition? _darkDefinition;
    private static IHighlightingDefinition? _lightDefinition;
    private static IHighlightingDefinition? _highContrastDefinition;

    /// <summary>
    /// Maps language IDs to their TextMate-derived highlighting definitions.
    /// </summary>
    private static readonly Dictionary<string, IHighlightingDefinition> _textMateDefinitions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Maps file extensions to language IDs for TextMate grammar lookup.
    /// </summary>
    private static readonly Dictionary<string, string> _extensionToLanguage = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Stores loaded language configurations from VS Code extensions.
    /// </summary>
    private static readonly Dictionary<string, LanguageConfigurationData> _languageConfigurations = new(StringComparer.OrdinalIgnoreCase);

    public static void RegisterHighlighting()
    {
        if (_isRegistered) return;

        var definition = LoadBasicLangDefinition();
        if (definition != null)
        {
            HighlightingManager.Instance.RegisterHighlighting(
                "BasicLang",
                new[] { ".bas", ".bl", ".basic" },
                definition);
            _isRegistered = true;
        }
    }

    public static IHighlightingDefinition? GetBasicLangDefinition()
    {
        if (_basicLangDefinition == null)
        {
            _basicLangDefinition = LoadBasicLangDefinition();
        }
        return _basicLangDefinition;
    }

    /// <summary>
    /// Returns the appropriate highlighting definition for the current theme.
    /// </summary>
    public static IHighlightingDefinition? GetDefinitionForTheme(bool isDark, bool isHighContrast = false)
    {
        if (isHighContrast)
        {
            _highContrastDefinition ??= LoadDefinitionFromResource("VisualGameStudio.Editor.Highlighting.BasicLangHighContrast.xshd");
            return _highContrastDefinition;
        }

        if (isDark)
        {
            _darkDefinition ??= LoadDefinitionFromResource("VisualGameStudio.Editor.Highlighting.BasicLang.xshd");
            return _darkDefinition;
        }
        else
        {
            _lightDefinition ??= LoadDefinitionFromResource("VisualGameStudio.Editor.Highlighting.BasicLangLight.xshd");
            return _lightDefinition;
        }
    }

    /// <summary>
    /// Re-registers the highlighting for the current theme. Call after theme switch.
    /// </summary>
    public static void UpdateForTheme(bool isDark, bool isHighContrast = false)
    {
        var definition = GetDefinitionForTheme(isDark, isHighContrast);
        if (definition != null)
        {
            HighlightingManager.Instance.RegisterHighlighting(
                "BasicLang",
                new[] { ".bas", ".bl", ".basic" },
                definition);
            _basicLangDefinition = definition;
        }
    }

    #region TextMate Grammar Registration

    /// <summary>
    /// Registers a TextMate grammar from a Core.TextMate.TextMateGrammarInfo as an AvalonEdit
    /// highlighting definition. Converts the grammar patterns to XSHD-based highlighting and
    /// registers it with the HighlightingManager for files with matching extensions.
    /// </summary>
    /// <param name="languageId">The language ID (e.g., "python").</param>
    /// <param name="languageName">The display name (e.g., "Python").</param>
    /// <param name="fileExtensions">File extensions this grammar applies to (e.g., ".py", ".pyw").</param>
    /// <param name="grammar">The parsed TextMate grammar.</param>
    /// <returns>True if the grammar was successfully converted and registered.</returns>
    public static bool RegisterTextMateGrammar(string languageId, string languageName, IEnumerable<string> fileExtensions, TextMateGrammarInfo grammar)
    {
        try
        {
            var definition = TextMateToAvalonEditConverter.ConvertGrammar(grammar, languageName, fileExtensions);
            if (definition == null)
                return false;

            var extensions = fileExtensions.Select(e => e.StartsWith(".") ? e : "." + e).ToArray();

            HighlightingManager.Instance.RegisterHighlighting(languageName, extensions, definition);

            _textMateDefinitions[languageId] = definition;
            foreach (var ext in extensions)
            {
                _extensionToLanguage[ext.ToLowerInvariant()] = languageId;
            }

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to register TextMate grammar for {languageId}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Registers a TextMate grammar from a Core.Extensions.TextMateGrammar (imported by ExtensionManager).
    /// </summary>
    public static bool RegisterExtensionGrammar(string languageId, string languageName, IEnumerable<string> fileExtensions, TextMateGrammar grammar)
    {
        try
        {
            var definition = TextMateToAvalonEditConverter.ConvertExtensionGrammar(grammar, languageName, fileExtensions);
            if (definition == null)
                return false;

            var extensions = fileExtensions.Select(e => e.StartsWith(".") ? e : "." + e).ToArray();

            HighlightingManager.Instance.RegisterHighlighting(languageName, extensions, definition);

            _textMateDefinitions[languageId] = definition;
            foreach (var ext in extensions)
            {
                _extensionToLanguage[ext.ToLowerInvariant()] = languageId;
            }

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to register extension grammar for {languageId}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Registers a TextMate grammar from raw JSON content (e.g., .tmLanguage.json file).
    /// </summary>
    public static bool RegisterTextMateGrammarFromJson(string languageId, string languageName, IEnumerable<string> fileExtensions, string jsonContent)
    {
        try
        {
            var definition = TextMateToAvalonEditConverter.ConvertFromJson(jsonContent, languageName, fileExtensions);
            if (definition == null)
                return false;

            var extensions = fileExtensions.Select(e => e.StartsWith(".") ? e : "." + e).ToArray();

            HighlightingManager.Instance.RegisterHighlighting(languageName, extensions, definition);

            _textMateDefinitions[languageId] = definition;
            foreach (var ext in extensions)
            {
                _extensionToLanguage[ext.ToLowerInvariant()] = languageId;
            }

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to register TextMate grammar from JSON for {languageId}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Gets the highlighting definition for a file extension, checking TextMate grammars first,
    /// then falling back to built-in definitions.
    /// </summary>
    /// <param name="fileExtension">File extension including the dot (e.g., ".py").</param>
    /// <returns>The highlighting definition, or null if none found.</returns>
    public static IHighlightingDefinition? GetDefinitionForExtension(string fileExtension)
    {
        var ext = fileExtension.StartsWith(".") ? fileExtension.ToLowerInvariant() : "." + fileExtension.ToLowerInvariant();

        // Check TextMate-derived definitions first
        if (_extensionToLanguage.TryGetValue(ext, out var langId) &&
            _textMateDefinitions.TryGetValue(langId, out var tmDef))
        {
            return tmDef;
        }

        // Fall back to AvalonEdit's built-in highlighting manager
        return HighlightingManager.Instance.GetDefinitionByExtension(ext);
    }

    /// <summary>
    /// Gets the TextMate-derived highlighting definition for a language ID.
    /// </summary>
    public static IHighlightingDefinition? GetTextMateDefinition(string languageId)
    {
        return _textMateDefinitions.TryGetValue(languageId, out var def) ? def : null;
    }

    /// <summary>
    /// Checks whether a TextMate grammar has been registered for the given file extension.
    /// </summary>
    public static bool HasTextMateGrammar(string fileExtension)
    {
        var ext = fileExtension.StartsWith(".") ? fileExtension.ToLowerInvariant() : "." + fileExtension.ToLowerInvariant();
        return _extensionToLanguage.ContainsKey(ext);
    }

    /// <summary>
    /// Gets all registered TextMate language IDs.
    /// </summary>
    public static IReadOnlyCollection<string> RegisteredTextMateLanguages => _textMateDefinitions.Keys;

    #endregion

    #region Language Configuration

    /// <summary>
    /// Stores a language configuration loaded from a VS Code extension.
    /// </summary>
    public static void RegisterLanguageConfiguration(string languageId, LanguageConfigurationData config)
    {
        _languageConfigurations[languageId] = config;
    }

    /// <summary>
    /// Gets the language configuration for a language ID.
    /// </summary>
    public static LanguageConfigurationData? GetLanguageConfiguration(string languageId)
    {
        return _languageConfigurations.TryGetValue(languageId, out var config) ? config : null;
    }

    /// <summary>
    /// Gets the language configuration for a file extension.
    /// </summary>
    public static LanguageConfigurationData? GetLanguageConfigurationForExtension(string fileExtension)
    {
        var ext = fileExtension.StartsWith(".") ? fileExtension.ToLowerInvariant() : "." + fileExtension.ToLowerInvariant();
        if (_extensionToLanguage.TryGetValue(ext, out var langId))
        {
            return GetLanguageConfiguration(langId);
        }
        return null;
    }

    /// <summary>
    /// Updates the syntax highlighting definitions when a custom theme is applied.
    /// Re-registers the highlighting with the new custom colors from EditorTheme.
    /// </summary>
    public static void UpdateForCustomTheme()
    {
        // Re-register highlighting to pick up the new custom colors
        _isRegistered = false;
        RegisterHighlighting();
    }

    #endregion

    #region Private Methods

    private static IHighlightingDefinition? LoadBasicLangDefinition()
    {
        return LoadDefinitionFromResource("VisualGameStudio.Editor.Highlighting.BasicLang.xshd");
    }

    private static IHighlightingDefinition? LoadDefinitionFromResource(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            // Fallback: try to load from file
            var fileName = resourceName.Replace("VisualGameStudio.Editor.Highlighting.", "");
            var assemblyLocation = Path.GetDirectoryName(assembly.Location);
            var filePath = Path.Combine(assemblyLocation ?? "", "Highlighting", fileName);
            if (File.Exists(filePath))
            {
                using var fileStream = File.OpenRead(filePath);
                using var reader = new XmlTextReader(fileStream);
                return Load(reader, HighlightingManager.Instance);
            }
            return null;
        }

        using var xmlReader = new XmlTextReader(stream);
        return Load(xmlReader, HighlightingManager.Instance);
    }

    private static IHighlightingDefinition Load(XmlReader reader, IHighlightingDefinitionReferenceResolver resolver)
    {
        var xshd = LoadXshd(reader);
        return Load(xshd, resolver);
    }

    private static XshdSyntaxDefinition LoadXshd(XmlReader reader)
    {
        return AvaloniaEdit.Highlighting.Xshd.HighlightingLoader.LoadXshd(reader);
    }

    private static IHighlightingDefinition Load(XshdSyntaxDefinition syntaxDefinition, IHighlightingDefinitionReferenceResolver resolver)
    {
        return AvaloniaEdit.Highlighting.Xshd.HighlightingLoader.Load(syntaxDefinition, resolver);
    }

    #endregion
}
