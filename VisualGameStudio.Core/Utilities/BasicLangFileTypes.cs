namespace VisualGameStudio.Core.Utilities;

/// <summary>
/// Central definition of which file extensions are BasicLang source files.
/// Every LSP notification/request gate (didOpen, didChange, didClose, didSave,
/// completion/hover/etc. routing) must use this helper so .mod/.cls/.class
/// files get the same IntelliSense as .bas/.bl.
/// </summary>
public static class BasicLangFileTypes
{
    private static readonly string[] SourceExtensions =
    {
        ".bas", ".bl", ".mod", ".cls", ".class"
    };

    /// <summary>
    /// Returns true when the given path is a BasicLang source file that should
    /// be synced with (and routed to) the BasicLang language server.
    /// </summary>
    public static bool IsBasicLangSourceFile(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;

        foreach (var extension in SourceExtensions)
        {
            if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
