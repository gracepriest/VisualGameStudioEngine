using System.Text;
using System.Text.RegularExpressions;

namespace BasicLang.Forms;

/// <summary>One parameter of a declared Sub: its name, and its <c>As</c> type as written (null when untyped).</summary>
public sealed record FormCodeParameter(string Name, string? Type);

/// <summary>A <c>Sub</c> the user declared in a code-behind, outside the designer's regions.</summary>
/// <param name="Line">The 1-based line of its <c>Sub</c> keyword.</param>
public sealed record FormDeclaredSub(string Name, int Line, IReadOnlyList<FormCodeParameter> Parameters);

/// <summary>
/// ⛔ THE one reader of "which Subs does this code-behind declare" (slice 5 D-11) — the gesture's navigate-or-create
/// (<see cref="FormHandlers"/>), the Events tab's handler drop-down (<c>FittingHandlers</c>) and the region writer's
/// BL8013 ordering check all ask it. It replaced TWO mirrored copies (<c>FormHandlers.FindDeclarationLine</c>,
/// <c>RegionWriter.FindHandlerDeclarationLine</c>) that agreed on two latent defects: they matched names with
/// <c>Ordinal</c> in a case-INSENSITIVE language (a hand-written <c>btnlogin_click</c> was not found, and the gesture
/// wrote a second <c>btnLogin_Click</c> — a duplicate member), and they took <c>"Sub "</c> ANYWHERE in a line.
///
/// <para>⚠ A TEXT scan, deliberately: it runs on a file the user is midway through editing, which does not parse. Rules:
/// a declaration is optional modifiers, <c>Sub</c>, a name, then an optional parameter list that may continue over
/// lines (implicit continuation inside the parentheses, or an explicit <c>_</c>); a <c>'</c> outside a string ends the
/// line; <c>End Sub</c>, <c>Exit Sub</c>, <c>Dim f = Sub(x) …</c> and a <c>Function</c> are not Sub declarations; a
/// Sub inside a designer region (<c>InitializeComponent</c>, the generated <c>VgsOn_</c> wrappers) is not the user's
/// and is never returned.</para>
/// </summary>
public static class FormCodeScan
{
    private static readonly Regex Declaration = new(
        @"^\s*(?:(?:Public|Private|Protected|Friend|Shared|Overrides|Overridable|NotOverridable|MustOverride|Overloads|Shadows|Async|Partial|Iterator)\s+)*Sub\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?<rest>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ParameterModifiers = new(
        @"^(?:(?:ByVal|ByRef|Optional|ParamArray)\s+)*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Every Sub the user declared, in document order.</summary>
    public static IReadOnlyList<FormDeclaredSub> DeclaredSubs(string codeText)
    {
        var index = new Recognizer.SourceIndex(codeText);
        var regions = RegionMarkers.Scan(codeText)
            .Where(r => r.EndOffset > r.StartOffset)
            .Select(r => (Start: index.PositionOf(r.StartOffset).Line, End: index.PositionOf(r.EndOffset).Line))
            .ToList();

        var subs = new List<FormDeclaredSub>();
        for (var line = 1; line <= index.LineCount; line++)
        {
            if (regions.Any(r => line >= r.Start && line <= r.End))
            {
                continue;
            }

            var match = Declaration.Match(Code(index.LineText(line)));
            if (!match.Success)
            {
                continue;
            }

            var rest = match.Groups["rest"].Value.TrimStart();
            var parameters = rest.StartsWith("(", StringComparison.Ordinal)
                ? Parameters(ParameterText(index, line, rest))
                : Array.Empty<FormCodeParameter>();

            subs.Add(new FormDeclaredSub(match.Groups["name"].Value, line, parameters));
        }

        return subs;
    }

    /// <summary>The declared Sub named <paramref name="name"/>, matched IGNORING CASE as BasicLang binds names, or null.</summary>
    public static FormDeclaredSub? FindSub(string codeText, string name) =>
        DeclaredSubs(codeText).FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The line with any comment removed — a <c>'</c> outside a string literal ends it.</summary>
    private static string Code(string line)
    {
        var inString = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
            {
                inString = !inString;
            }
            else if (line[i] == '\'' && !inString)
            {
                return line[..i];
            }
        }

        return line;
    }

    /// <summary>
    /// The text inside the parameter list's parentheses, following it onto later lines while it is still open.
    /// </summary>
    private static string ParameterText(Recognizer.SourceIndex index, int line, string rest)
    {
        var text = new StringBuilder();
        var depth = 0;
        var source = rest;
        while (true)
        {
            foreach (var ch in source)
            {
                if (ch == '(')
                {
                    if (depth++ == 0)
                    {
                        continue;
                    }
                }
                else if (ch == ')')
                {
                    if (--depth == 0)
                    {
                        return text.ToString();
                    }
                }

                if (depth > 0)
                {
                    text.Append(ch);
                }
            }

            if (++line > index.LineCount)
            {
                return text.ToString();   // unterminated: the file is mid-edit; take what there is
            }

            // An explicit " _" continuation is just whitespace inside the list.
            var trimmed = text.ToString().TrimEnd();
            if (trimmed.EndsWith(" _", StringComparison.Ordinal) || trimmed == "_")
            {
                text.Length = trimmed.Length - 1;
            }

            text.Append(' ');
            source = Code(index.LineText(line));
        }
    }

    /// <summary>Splits a parameter list on its TOP-LEVEL commas (a <c>List(Of T, U)</c> stays one) and reads each.</summary>
    private static IReadOnlyList<FormCodeParameter> Parameters(string list)
    {
        if (string.IsNullOrWhiteSpace(list))
        {
            return Array.Empty<FormCodeParameter>();
        }

        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < list.Length; i++)
        {
            switch (list[i])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
                case ',' when depth == 0:
                    parts.Add(list[start..i]);
                    start = i + 1;
                    break;
            }
        }

        parts.Add(list[start..]);
        return parts.Select(Parameter).ToList();
    }

    private static FormCodeParameter Parameter(string text)
    {
        var p = ParameterModifiers.Replace(text.Trim(), "");
        var equals = p.IndexOf('=');
        if (equals >= 0)
        {
            p = p[..equals].Trim();
        }

        var asAt = Regex.Match(p, @"\s+As\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var name = (asAt.Success ? p[..asAt.Index] : p).Trim();
        var type = asAt.Success ? p[(asAt.Index + asAt.Length)..].Trim() : null;
        if (name.EndsWith("()", StringComparison.Ordinal))
        {
            name = name[..^2];
        }

        return new FormCodeParameter(name, string.IsNullOrEmpty(type) ? null : type);
    }
}
