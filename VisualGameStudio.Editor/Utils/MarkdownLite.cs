using System.Text;
using System.Text.RegularExpressions;

namespace VisualGameStudio.Editor.Utils;

/// <summary>
/// Minimal markdown parser for hover tooltips: splits ``` fenced code blocks
/// from prose and extracts **bold** / `inline code` runs, so raw markdown
/// markers are never shown to the user.
/// </summary>
public static class MarkdownLite
{
    /// <summary>A top-level block: either a fenced code block or prose text.</summary>
    public sealed record Block(bool IsCode, string Text, string? Language = null);

    /// <summary>An inline run of prose: plain, bold or inline-code.</summary>
    public sealed record InlineSegment(bool IsBold, bool IsCode, string Text);

    private static readonly Regex InlinePattern = new(@"\*\*(.+?)\*\*|`([^`]+)`", RegexOptions.Compiled);

    /// <summary>
    /// Splits markdown into fenced code blocks and prose blocks. Fence markers
    /// (```lang / ```) are consumed and never appear in the output. An
    /// unterminated fence treats the rest of the input as code.
    /// </summary>
    public static IReadOnlyList<Block> ParseBlocks(string? markdown)
    {
        var blocks = new List<Block>();
        if (string.IsNullOrEmpty(markdown)) return blocks;

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var current = new StringBuilder();
        var inCode = false;
        string? language = null;

        void Flush()
        {
            var text = current.ToString();
            current.Clear();
            if (text.Trim().Length > 0)
            {
                blocks.Add(new Block(inCode, text.Trim('\n'), inCode ? language : null));
            }
        }

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                Flush();
                if (!inCode)
                {
                    var lang = trimmed.Substring(3).Trim();
                    language = lang.Length > 0 ? lang : null;
                }
                inCode = !inCode;
                continue;
            }

            if (current.Length > 0) current.Append('\n');
            current.Append(line);
        }

        Flush();
        return blocks;
    }

    /// <summary>
    /// Extracts **bold** and `inline code` runs from a prose block. The
    /// returned segments concatenate to the original text minus the markers.
    /// </summary>
    public static IReadOnlyList<InlineSegment> ParseInlines(string? text)
    {
        var segments = new List<InlineSegment>();
        if (string.IsNullOrEmpty(text)) return segments;

        var last = 0;
        foreach (Match match in InlinePattern.Matches(text))
        {
            if (match.Index > last)
            {
                segments.Add(new InlineSegment(false, false, text.Substring(last, match.Index - last)));
            }

            if (match.Groups[1].Success)
            {
                segments.Add(new InlineSegment(true, false, match.Groups[1].Value));
            }
            else
            {
                segments.Add(new InlineSegment(false, true, match.Groups[2].Value));
            }

            last = match.Index + match.Length;
        }

        if (last < text.Length)
        {
            segments.Add(new InlineSegment(false, false, text.Substring(last)));
        }

        return segments;
    }
}
