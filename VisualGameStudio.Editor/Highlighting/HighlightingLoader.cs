using System.Reflection;
using System.Xml;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

namespace VisualGameStudio.Editor.Highlighting;

public static class HighlightingLoader
{
    private static bool _isRegistered;
    private static IHighlightingDefinition? _basicLangDefinition;

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

    private static IHighlightingDefinition? LoadBasicLangDefinition()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "VisualGameStudio.Editor.Highlighting.BasicLang.xshd";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            // Fallback: try to load from file
            var assemblyLocation = Path.GetDirectoryName(assembly.Location);
            var filePath = Path.Combine(assemblyLocation ?? "", "Highlighting", "BasicLang.xshd");
            if (File.Exists(filePath))
            {
                using var fileStream = File.OpenRead(filePath);
                using var reader = new XmlTextReader(fileStream);
                return HighlightingLoader.Load(reader, HighlightingManager.Instance);
            }
            return null;
        }

        using var xmlReader = new XmlTextReader(stream);
        return HighlightingLoader.Load(xmlReader, HighlightingManager.Instance);
    }

    private static IHighlightingDefinition Load(XmlReader reader, IHighlightingDefinitionReferenceResolver resolver)
    {
        var xshd = HighlightingLoader.LoadXshd(reader);
        return HighlightingLoader.Load(xshd, resolver);
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
