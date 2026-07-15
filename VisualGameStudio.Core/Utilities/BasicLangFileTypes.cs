namespace VisualGameStudio.Core.Utilities;

/// <summary>
/// Central definition of which file extensions are BasicLang source files.
/// Every LSP notification/request gate (didOpen, didChange, didClose, didSave,
/// completion/hover/etc. routing) must use this helper so .mod/.cls/.class
/// files get the same IntelliSense as .bas/.bl.
/// </summary>
public static class BasicLangFileTypes
{
    /// <summary>
    /// Returns true when the given path is a BasicLang source file that should
    /// be synced with (and routed to) the BasicLang language server.
    /// </summary>
    /// <remarks>
    /// The extension list lives in <see cref="LanguageFileTypes"/> — the single source
    /// of truth shared with the C++/clangd routing and the extension-host map. This
    /// method keeps its own name and signature because it has many callers.
    /// </remarks>
    public static bool IsBasicLangSourceFile(string? path)
        => LanguageFileTypes.IsBasicLangSourceFile(path);
}
