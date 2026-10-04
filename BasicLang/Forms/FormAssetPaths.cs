namespace BasicLang.Forms;

/// <summary>
/// The ONE reading of an image or icon path (slice 4 D-5a) — what the catalog's Image/Icon rules, the region writer, the
/// page emitter and the build copy all ask. A document stores a path RELATIVE TO THE PROJECT DIRECTORY with forward slashes
/// (<c>Resources/logo.png</c>); the reader accepts backslashes, and everything normalises through <see cref="Normalise"/>.
/// </summary>
public static class FormAssetPaths
{
    /// <summary>Forward slashes — the one stored spelling. A URL or a rooted path is returned unchanged.</summary>
    public static string Normalise(string value) =>
        IsUrl(value) || IsRooted(value) ? value : value.Replace('\\', '/');

    /// <summary>An <c>http://</c> or <c>https://</c> URL — web only, as a value (<c>src</c> verbatim, never copied).</summary>
    public static bool IsUrl(string value) =>
        (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
         value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Host.Length > 0;

    /// <summary>
    /// A rooted path on ANY machine's spelling — a drive (<c>C:\…</c>, <c>C:/…</c>, and the DRIVE-RELATIVE <c>C:logo.png</c>
    /// or a bare <c>C:</c>, which name the current folder of drive C — never the project), a UNC share (<c>\\server\…</c>)
    /// or a POSIX root (<c>/…</c>). Judged by text, never by this OS: a Windows document read on Linux is still rooted.
    /// </summary>
    public static bool IsRooted(string value) =>
        value.Length >= 2 && char.IsAsciiLetter(value[0]) && value[1] == ':' ||
        value.StartsWith(@"\\", StringComparison.Ordinal) || value.StartsWith("/", StringComparison.Ordinal);

    /// <summary>True when a RELATIVE path climbs above the project directory (<c>../x.png</c>, <c>a/../../x.png</c>).</summary>
    public static bool EscapesProject(string value)
    {
        var depth = 0;
        foreach (var segment in Normalise(value).Split('/'))
        {
            if (segment == "..")
            {
                if (--depth < 0)
                {
                    return true;
                }
            }
            else if (segment.Length > 0 && segment != ".")
            {
                depth++;
            }
        }

        return false;
    }

    /// <summary>A path the project can carry: relative, inside the project, naming a file (not empty, not a folder).</summary>
    public static bool IsInsideProject(string value) =>
        !IsUrl(value) && !IsRooted(value) && !EscapesProject(value) &&
        Normalise(value) is var path && path.Trim().Length > 0 && !path.EndsWith('/');

    /// <summary>The extension, lower case, with its dot (<c>.png</c>), or "" — of the path, or of a URL's path part.</summary>
    public static string Extension(string value)
    {
        var path = IsUrl(value) && Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri.AbsolutePath : value;
        var name = path.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        var dot = name.LastIndexOf('.');
        return dot <= 0 ? "" : name[dot..].ToLowerInvariant();
    }

    /// <summary>
    /// The value as a page URL (D-5f): a web address verbatim; a project path relative to the site, PERCENT-ENCODED per
    /// segment (a space or a <c>#</c> would otherwise break the reference — HTML-attribute escaping alone does not);
    /// null for a rooted path, which no page can reach.
    /// </summary>
    public static string? PageUrl(string value) =>
        IsUrl(value) ? value
        : IsInsideProject(value) ? string.Join("/", Normalise(value).Split('/').Select(Uri.EscapeDataString))
        : null;

    /// <summary>
    /// The project directory a form document belongs to: the nearest ancestor directory holding a <c>*.blproj</c>, else the
    /// document's own directory (slice 4 D-5e). The build knows its project exactly; the two agree whenever the
    /// <c>.blproj</c> is an ancestor of the form — the normal shape.
    /// </summary>
    public static string ProjectRootFor(string documentPath)
    {
        var start = Path.GetDirectoryName(Path.GetFullPath(documentPath)) ?? ".";
        for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
        {
            if (dir.Exists && dir.EnumerateFiles("*.blproj").Any())
            {
                return dir.FullName;
            }
        }

        return start;
    }
}
