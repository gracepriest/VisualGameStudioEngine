using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BasicLang.Forms.Recognizer;

namespace BasicLang.Forms;

/// <summary>How a designer-owned region in a user's file may be found (D1).</summary>
public enum RegionState
{
    /// <summary>Both markers present, balanced, and the hash matches. The designer regenerates freely.</summary>
    Canon,

    /// <summary>
    /// Balanced, but the content hash does not match the marker — somebody hand-edited inside the
    /// region. <b>Refused</b>: the form opens read-only with <c>BL8011</c> and Code view stays fully
    /// editable. The designer never silently discards hand-written code.
    /// </summary>
    HashMismatch,

    /// <summary>A marker is missing its partner, nested, or duplicated. <b>Refused</b> with <c>BL8012</c>; never written to.</summary>
    Malformed,

    /// <summary>No markers at all. Not an error — this is the import case (D12); the designer writes nothing.</summary>
    Absent
}

/// <summary>One designer-owned region located in a source file.</summary>
/// <param name="Name"><c>controls</c> or <c>init</c>.</param>
/// <param name="StartOffset">Offset of the first character of the OPEN marker line.</param>
/// <param name="EndOffset">Offset just past the CLOSE marker line's last character (excluding its terminator).</param>
/// <param name="Content">The generated text between the markers, exactly as it is in the file.</param>
/// <param name="DeclaredHash">The hash the open marker claims.</param>
public sealed record FormRegion(
    string Name,
    RegionState State,
    int StartOffset,
    int EndOffset,
    string Content,
    string DeclaredHash,
    int Line);

/// <summary>
/// Finds, classifies and formats the designer-owned regions D1 puts inside the user's own file.
///
/// <para>The markers are <b>ordinary BasicLang comments</b>, so they are inert to the lexer and need
/// no language change. That is the whole reason this mechanism is cheap; do not be tempted to make
/// them a real syntax.</para>
/// </summary>
public static class RegionMarkers
{
    /// <summary>Field declarations. Separate from <see cref="Init"/> because user code sits between them.</summary>
    public const string Controls = "controls";

    /// <summary>The generated <c>InitializeComponent</c>.</summary>
    public const string Init = "init";

    public static readonly string[] All = { Controls, Init };

    private static readonly Regex OpenMarker = new(
        """^\s*'\s*<vgs:designer\s+region="(?<region>[^"]*)"\s+form="(?<form>[^"]*)"\s+hash="(?<hash>[^"]*)"\s*>\s*$""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CloseMarker = new(
        @"^\s*'\s*</vgs:designer>\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string FormatOpen(string region, string formFileName, string hash, string indent) =>
        $"""{indent}' <vgs:designer region="{region}" form="{formFileName}" hash="{hash}">""";

    public static string FormatClose(string indent) => $"{indent}' </vgs:designer>";

    /// <summary>
    /// A short, stable hash of a region's generated content.
    ///
    /// <para>⛔ Line endings are normalised to LF before hashing. Without that, the same content
    /// checked out on Windows and on Linux hashes differently, every region reads as hand-edited,
    /// and every form silently opens read-only with BL8011 — a failure that would look like data
    /// corruption and be nothing of the kind.</para>
    ///
    /// <para>Trailing whitespace per line is also trimmed, because editors strip it on save and a
    /// hash that flips when an editor tidies the file is a hash nobody can rely on.</para>
    /// </summary>
    public static string HashContent(string content)
    {
        var normalised = string.Join("\n",
            content.Replace("\r\n", "\n").Split('\n').Select(line => line.TrimEnd()));

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalised));
        return Convert.ToHexString(bytes, 0, 4).ToLowerInvariant();
    }

    /// <summary>
    /// Locates every designer region in <paramref name="source"/> and classifies it.
    ///
    /// <para>Scans line by line rather than with the lexer: this has to work on a file that does not
    /// compile, and a comment is a comment whatever the rest of the file is doing.</para>
    /// </summary>
    public static IReadOnlyList<FormRegion> Scan(string source)
    {
        var index = new SourceIndex(source);
        var found = new List<FormRegion>();
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        string? openRegion = null;
        string? openHash = null;
        var openLine = 0;
        var openStart = 0;
        var contentStart = 0;

        for (var line = 1; line <= index.LineCount; line++)
        {
            var text = index.LineText(line);

            var open = OpenMarker.Match(text);
            if (open.Success)
            {
                if (openRegion != null)
                {
                    // Nested open — unbalanced. Both the outer and this one are unusable.
                    found.Add(Malformed(openRegion, openStart, index.EndOfLine(line), openLine));
                    openRegion = null;
                }

                openRegion = open.Groups["region"].Value;
                openHash = open.Groups["hash"].Value;
                openLine = line;
                openStart = index.OffsetOf(line, 1);
                contentStart = line < index.LineCount ? index.OffsetOf(line + 1, 1) : index.Length;

                seen[openRegion] = seen.TryGetValue(openRegion, out var count) ? count + 1 : 1;
                continue;
            }

            if (!CloseMarker.IsMatch(text))
            {
                continue;
            }

            if (openRegion == null)
            {
                // A close with no open. Recorded under a sentinel name so the caller reports BL8012
                // rather than silently ignoring a marker the user can see.
                found.Add(Malformed("(unopened)", index.OffsetOf(line, 1), index.EndOfLine(line), line));
                continue;
            }

            var closeStart = index.OffsetOf(line, 1);
            var content = index.Slice(contentStart, closeStart);

            found.Add(new FormRegion(
                openRegion,
                HashContent(content) == openHash ? RegionState.Canon : RegionState.HashMismatch,
                openStart,
                index.EndOfLine(line),
                content,
                openHash ?? "",
                openLine));

            openRegion = null;
        }

        if (openRegion != null)
        {
            // Opened and never closed.
            found.Add(Malformed(openRegion, openStart, index.Length, openLine));
        }

        // A duplicated region name is Malformed even when each copy is individually balanced:
        // regenerating would have to pick one and silently orphan the other.
        return found
            .Select(r => seen.TryGetValue(r.Name, out var count) && count > 1
                ? r with { State = RegionState.Malformed }
                : r)
            .ToList();
    }

    public static FormRegion? Find(IReadOnlyList<FormRegion> regions, string name) =>
        regions.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

    private static FormRegion Malformed(string name, int start, int end, int line) =>
        new(name, RegionState.Malformed, start, end, "", "", line);
}
