namespace VisualGameStudio.Core.Constants;

public static class FileExtensions
{
    public const string BasicLangSource1 = ".bas";
    public const string BasicLangSource2 = ".bl";
    public const string BasicLangSource3 = ".basic";
    public const string BasicLangModule = ".mod";
    public const string BasicLangClass1 = ".cls";
    public const string BasicLangClass2 = ".class";

    /// <summary>A declaration file — Extern Class declarations of runtime-provided types (the DOM). Compiled, never an entry point.</summary>
    public const string BasicLangDeclaration = ".bli";

    /// <summary>A WinForms designer document. Designer-owned; never reaches the lexer.</summary>
    public const string FormDocument = ".blform";

    /// <summary>A browser-page designer document. Designer-owned; never reaches the lexer.</summary>
    public const string WebFormDocument = ".blwebform";

    public const string Project = ".blproj";
    public const string Solution = ".blsln";

    /// <summary>
    /// The designer document formats.
    ///
    /// <para>⛔ Both extensions are SAFE against the Windows 8.3 short-name quirk, and that is not an
    /// accident: a 3-character glob sweeps longer extensions, so <c>*.bas</c> really does return
    /// <c>Lib.basic</c>. <c>.blform</c> truncates to <c>BLF</c> and <c>.blwebform</c> to <c>BLW</c>,
    /// neither matching a source extension. Any extension beginning <c>bas</c>, <c>cls</c>,
    /// <c>mod</c> or <c>bli</c> would be swept into the compile set by the default glob with no
    /// <c>&lt;Compile&gt;</c> item and no diagnostic.</para>
    /// </summary>
    public static readonly string[] FormDocumentExtensions = { FormDocument, WebFormDocument };

    /// <summary>
    /// Extensions the IDE treats as project SOURCE — i.e. items that ride as <c>&lt;Compile&gt;</c>.
    ///
    /// <para>⚠ Form documents ARE here (D11): they are listed in the project file so the build can
    /// find them, and the compiler SKIPS them on both routes. "Source" here means "a project item
    /// the IDE compiles into the program", not "text the lexer reads".</para>
    ///
    /// <para>⛔ This is the IDE-side list. Its compiler-side counterparts —
    /// <c>ProjectFile.BasicLangSourceExtensions</c> and <c>ModuleResolver.SupportedExtensions</c> —
    /// are two more copies of the same idea and the form extensions must NEVER be added to either:
    /// both feed paths that end at the lexer.</para>
    /// </summary>
    public static readonly string[] SourceExtensions = { BasicLangSource1, BasicLangSource2, BasicLangSource3, BasicLangModule, BasicLangClass1, BasicLangClass2, BasicLangDeclaration, FormDocument, WebFormDocument };

    /// <summary>True for a <c>.blform</c> or <c>.blwebform</c> — a designer document, not a program.</summary>
    public static bool IsFormDocument(string path)
    {
        var ext = Path.GetExtension(path);
        return FormDocumentExtensions.Any(e => ext.Equals(e, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// File extensions that represent BasicLang module files (.mod).
    /// </summary>
    public static bool IsModuleFile(string path)
    {
        return Path.GetExtension(path).Equals(BasicLangModule, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// File extensions that represent BasicLang class files (.cls, .class).
    /// </summary>
    public static bool IsClassFile(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(BasicLangClass1, StringComparison.OrdinalIgnoreCase)
            || ext.Equals(BasicLangClass2, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSourceFile(string path)
    {
        var ext = Path.GetExtension(path);
        return SourceExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsProjectFile(string path)
    {
        return Path.GetExtension(path).Equals(Project, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSolutionFile(string path)
    {
        return Path.GetExtension(path).Equals(Solution, StringComparison.OrdinalIgnoreCase);
    }
}
