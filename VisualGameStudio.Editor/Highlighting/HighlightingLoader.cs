using System.Reflection;
using System.Xml;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

namespace VisualGameStudio.Editor.Highlighting;

public static class HighlightingLoader
{
    private static bool _isRegistered;
    private static IHighlightingDefinition? _basicLangDefinition;
    private static IHighlightingDefinition? _darkDefinition;
    private static IHighlightingDefinition? _lightDefinition;

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
    public static IHighlightingDefinition? GetDefinitionForTheme(bool isDark)
    {
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
    public static void UpdateForTheme(bool isDark)
    {
        var definition = GetDefinitionForTheme(isDark);
        if (definition != null)
        {
            HighlightingManager.Instance.RegisterHighlighting(
                "BasicLang",
                new[] { ".bas", ".bl", ".basic" },
                definition);
            _basicLangDefinition = definition;
        }
    }

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
}
