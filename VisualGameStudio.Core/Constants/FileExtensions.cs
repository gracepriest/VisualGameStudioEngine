namespace VisualGameStudio.Core.Constants;

public static class FileExtensions
{
    public const string BasicLangSource1 = ".bas";
    public const string BasicLangSource2 = ".bl";
    public const string BasicLangSource3 = ".basic";
    public const string BasicLangModule = ".mod";
    public const string BasicLangClass1 = ".cls";
    public const string BasicLangClass2 = ".class";

    public const string Project = ".blproj";
    public const string Solution = ".blsln";

    public static readonly string[] SourceExtensions = { BasicLangSource1, BasicLangSource2, BasicLangSource3, BasicLangModule, BasicLangClass1, BasicLangClass2 };

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
