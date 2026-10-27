using System.Text;

namespace BasicLang.Forms;

/// <summary>
/// The ONE splitter for a document's comma-separated CSS track lists (<c>Cols</c>/<c>Rows</c>).
///
/// <para>⛔ Commas inside a CSS function are part of the track, not separators: <c>minmax(100px, 1fr)</c>
/// and <c>repeat(2, 1fr)</c> are single tracks. Splitting on every comma emitted invalid CSS
/// (<c>minmax(100px 1fr)</c>), and the canvas drew the same wrong grid. Order-of-magnitude fix: one
/// top-level scan shared by the emitter and the canvas.</para>
///
/// <para>⚠ Empty entries are dropped, as the old splitter did. A blank list is EMPTY here; callers
/// that need CSS's “one implicit track” (the canvas) spell that themselves.</para>
/// </summary>
public static class FormTracks
{
    /// <summary>The track texts, with top-level commas as separators and whitespace trimmed.</summary>
    public static IReadOnlyList<string> Split(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            return Array.Empty<string>();
        }

        var parts = new List<string>();
        var current = new StringBuilder();
        var depth = 0;
        var quote = '\0';
        var escaped = false;

        void Flush()
        {
            var text = current.ToString().Trim();
            if (text.Length > 0)
            {
                parts.Add(text);
            }

            current.Clear();
        }

        foreach (var c in spec)
        {
            if (quote != '\0')
            {
                current.Append(c);
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
                current.Append(c);
                continue;
            }

            if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (c == ',' && depth == 0)
            {
                Flush();
                continue;
            }

            current.Append(c);
        }

        Flush();
        return parts;
    }

    /// <summary>The list as CSS wants it: space-separated, one track per function preserved.</summary>
    public static string ToCss(string? spec) => string.Join(" ", Split(spec));
}
