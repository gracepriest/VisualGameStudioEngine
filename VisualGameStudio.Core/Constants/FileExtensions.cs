namespace VisualGameStudio.Core.Constants;

public static class FileExtensions
{
    public const string BasicLangSource1 = ".bas";
    public const string BasicLangSource2 = ".bl";
    public const string BasicLangSource3 = ".basic";

    public const string Project = ".blproj";
    public const string Solution = ".blsln";

    public static readonly string[] SourceExtensions = { BasicLangSource1, BasicLangSource2, BasicLangSource3 };

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
