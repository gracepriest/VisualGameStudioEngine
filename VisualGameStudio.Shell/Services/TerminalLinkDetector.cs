using System.Text.RegularExpressions;

namespace VisualGameStudio.Shell.Services;

/// <summary>
/// Detects file path patterns in terminal output and produces segments
/// that are either plain text or clickable file links.
///
/// Supported formats:
///   C:\path\file.bas(10,5)        — MSBuild error format (line, col)
///   C:\path\file.bas(10)          — MSBuild short format (line only)
///   C:\path\file.bas:10:5         — GCC-style (line:col)
///   C:\path\file.bas:10           — GCC-style (line only)
///   /unix/path/file.bas:10:5      — Unix GCC-style
///   file.bas(10,5)                — Short MSBuild (relative path)
///   file.bas(10)                  — Short MSBuild (relative path, line only)
/// </summary>
public static class TerminalLinkDetector
{
    /// <summary>
    /// A segment of terminal text that is either plain text or a file link.
    /// </summary>
    public readonly record struct LinkSegment(
        string Text,
        bool IsLink,
        string FilePath,
        int Line,
        int Column);

    // Matches file paths with line/col info.
    // Group 1: full file path
    // Group 2: (line,col) or (line) — MSBuild style
    // Group 3: line from MSBuild
    // Group 4: col from MSBuild (optional)
    // Group 5: :line:col or :line — GCC style
    // Group 6: line from GCC
    // Group 7: col from GCC (optional)
    //
    // File path patterns:
    //   - Drive letter paths: C:\...\file.ext
    //   - UNC paths: \\server\share\file.ext
    //   - Unix absolute: /path/to/file.ext
    //   - Relative: file.ext, dir\file.ext, dir/file.ext
    private static readonly Regex FilePathPattern = new(
        @"(?<path>(?:[A-Za-z]:[/\\]|[/\\]{2}|[/\\])?(?:[\w\-. ]+[/\\])*[\w\-. ]+\.[\w]+)" +
        @"(?:" +
            @"\((?<mline>\d+)(?:,(?<mcol>\d+))?\)" +  // MSBuild: (line,col) or (line)
            @"|" +
            @":(?<gline>\d+)(?::(?<gcol>\d+))?" +      // GCC: :line:col or :line
        @")",
        RegexOptions.Compiled);

    /// <summary>
    /// Scans a plain-text string (no ANSI codes) for file path patterns
    /// and splits it into plain-text and link segments.
    /// </summary>
    public static List<LinkSegment> Detect(string text)
    {
        var segments = new List<LinkSegment>();
        if (string.IsNullOrEmpty(text))
            return segments;

        int lastIndex = 0;
        var matches = FilePathPattern.Matches(text);

        foreach (Match match in matches)
        {
            // Add plain text before the match
            if (match.Index > lastIndex)
            {
                var plain = text.Substring(lastIndex, match.Index - lastIndex);
                segments.Add(new LinkSegment(plain, false, "", 0, 0));
            }

            var filePath = match.Groups["path"].Value;
            int line = 1;
            int col = 1;

            // MSBuild format
            if (match.Groups["mline"].Success)
            {
                int.TryParse(match.Groups["mline"].Value, out line);
                if (match.Groups["mcol"].Success)
                    int.TryParse(match.Groups["mcol"].Value, out col);
            }
            // GCC format
            else if (match.Groups["gline"].Success)
            {
                int.TryParse(match.Groups["gline"].Value, out line);
                if (match.Groups["gcol"].Success)
                    int.TryParse(match.Groups["gcol"].Value, out col);
            }

            segments.Add(new LinkSegment(match.Value, true, filePath, line, col));
            lastIndex = match.Index + match.Length;
        }

        // Add remaining text after the last match
        if (lastIndex < text.Length)
        {
            segments.Add(new LinkSegment(text.Substring(lastIndex), false, "", 0, 0));
        }

        return segments;
    }
}
