using System.Text;
using System.Text.RegularExpressions;

namespace BasicLang.Forms;

/// <summary>One parameter of a declared Sub: its name, its <c>As</c> type as written (null when untyped), and whether it is ByRef.</summary>
public sealed record FormCodeParameter(string Name, string? Type, bool IsByRef = false);

/// <summary>A <c>Sub</c> the user declared in a code-behind's FORM class, outside the designer's regions.</summary>
/// <param name="Line">The 1-based line of its <c>Sub</c> keyword.</param>
public sealed record FormDeclaredSub(string Name, int Line, IReadOnlyList<FormCodeParameter> Parameters);

/// <summary>
/// ⛔ THE one reader of "which Subs does this code-behind's form declare" (slice 5 D-11) — the gesture's navigate-or-create
/// (<see cref="FormHandlers"/>), the Events tab's handler drop-down (<c>FittingHandlers</c>) and the region writer's
/// BL8013 ordering check all ask it. It replaced TWO mirrored copies (<c>FormHandlers.FindDeclarationLine</c>,
/// <c>RegionWriter.FindHandlerDeclarationLine</c>) that matched names with <c>Ordinal</c> in a case-INSENSITIVE language
/// and took <c>"Sub "</c> anywhere in a line.
///
/// <para>⚠ A TEXT scan, deliberately: it runs on a file the user is midway through editing, which does not parse. Rules
/// (review of Task 3, coordinator rulings 1–2):</para>
/// <list type="bullet">
/// <item>Only the FORM class's own members: the class named after the form, else the first top-level class (a
/// <c>Namespace</c> is transparent). A Sub in a nested <c>Class</c>/<c>Structure</c>/<c>Interface</c>/<c>Module</c>, or in
/// another class, is not the form's and is never returned.</item>
/// <item>A Sub inside <c>#If False</c> / <c>#If 0</c> is skipped (its <c>#Else</c> counts). Any other <c>#If</c> is read
/// as active on every branch — full evaluation is a follow-up (piece 2's <c>ProcessForEditor</c>).</item>
/// <item>A declaration is attributes (<c>&lt;Obsolete&gt;</c>), modifiers, <c>Sub</c>, a name, an optional
/// <c>(Of T)</c>, and an optional parameter list — which may continue over lines (implicit inside the parentheses, or with
/// <c>_</c> after any whitespace, the name and its list included). A <c>'</c> outside a string ends the line.</item>
/// <item><c>End Sub</c>, <c>Exit Sub</c>, <c>Dim f = Sub(x) …</c> and a <c>Function</c> are not Sub declarations; a Sub
/// inside a designer region (<c>InitializeComponent</c>, the <c>VgsOn_</c> wrappers) is not the user's.</item>
/// <item>A <c>ByRef</c> parameter is kept (<see cref="FormCodeParameter.IsByRef"/>): it never fits an event (CS0123).</item>
/// </list>
/// </summary>
public static class FormCodeScan
{
    private const string Modifiers =
        @"(?:(?:Public|Private|Protected|Friend|Shared|Overrides|Overridable|NotOverridable|MustOverride|Overloads|Shadows|Async|Partial|Iterator|MustInherit|NotInheritable|ReadOnly|WriteOnly|Default|Static)\s+)*";

    private static readonly Regex Declaration = new(
        @"^\s*" + Modifiers + @"Sub\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?<rest>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex BlockStart = new(
        @"^\s*" + Modifiers + @"(?<kind>Class|Structure|Interface|Module|Enum|Namespace)\s+(?<name>[A-Za-z_][A-Za-z0-9_.]*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex BlockEnd = new(
        @"^\s*End\s+(?<kind>Class|Structure|Interface|Module|Enum|Namespace)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex DirectiveIf = new(@"^\s*#\s*If\s+(?<cond>.*?)\s*(?:Then)?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex DirectiveElse = new(@"^\s*#\s*(?:Else\b|ElseIf\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex DirectiveEnd = new(@"^\s*#\s*End\s+If\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ParameterModifiers = new(
        @"^(?<mods>(?:(?:ByVal|ByRef|Optional|ParamArray)\s+)*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Continuation = new(@"\s_\s*$", RegexOptions.CultureInvariant);

    private sealed record Block(string Kind, string Name);

    /// <summary>Every Sub the FORM class declares, in document order. <paramref name="formName"/>: the form's class name.</summary>
    public static IReadOnlyList<FormDeclaredSub> DeclaredSubs(string codeText, string? formName = null)
    {
        var index = new Recognizer.SourceIndex(codeText);
        var regions = RegionMarkers.Scan(codeText)
            .Where(r => r.EndOffset > r.StartOffset)
            .Select(r => (Start: index.PositionOf(r.StartOffset).Line, End: index.PositionOf(r.EndOffset).Line))
            .ToList();

        // Pass 1 — which top-level class is the form's: the one named after it, else the first.
        var classes = new List<(string Name, int Ordinal)>();
        Walk(index, regions, (line, code, blocks) => { }, (block, depthOutsideNamespaces) =>
        {
            if (depthOutsideNamespaces == 0 && block.Kind.Equals("Class", StringComparison.OrdinalIgnoreCase))
            {
                classes.Add((block.Name, classes.Count));
            }
        });
        var formClass = classes.FirstOrDefault(c => formName != null && c.Name.Equals(formName, StringComparison.OrdinalIgnoreCase));
        if (formClass.Name == null)
        {
            formClass = classes.FirstOrDefault();
        }

        // Pass 2 — the Subs whose innermost non-namespace block is the form class, at the top level.
        var subs = new List<FormDeclaredSub>();
        var classOrdinal = -1;
        Walk(index, regions, (line, code, blocks) =>
        {
            var owners = blocks.Where(b => !b.Kind.Equals("Namespace", StringComparison.OrdinalIgnoreCase)).ToList();
            if (owners.Count != 1 || !owners[0].Kind.Equals("Class", StringComparison.OrdinalIgnoreCase) ||
                classOrdinal != formClass.Ordinal || formClass.Name == null)
            {
                return;
            }

            var match = Declaration.Match(WithoutAttributes(code));
            if (!match.Success)
            {
                return;
            }

            var rest = match.Groups["rest"].Value;
            var parameters = ParameterList(index, line, rest);
            subs.Add(new FormDeclaredSub(match.Groups["name"].Value, line, parameters));
        }, (block, depthOutsideNamespaces) =>
        {
            if (depthOutsideNamespaces == 0 && block.Kind.Equals("Class", StringComparison.OrdinalIgnoreCase))
            {
                classOrdinal++;
            }
        });

        return subs;
    }

    /// <summary>The form class's Sub named <paramref name="name"/>, matched IGNORING CASE as BasicLang binds names, or null.</summary>
    public static FormDeclaredSub? FindSub(string codeText, string name, string? formName = null) =>
        DeclaredSubs(codeText, formName).FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Walks the code lines outside the designer regions and outside <c>#If False</c>/<c>#If 0</c>, tracking the block
    /// stack; calls <paramref name="onLine"/> for each code line with the blocks it sits in, and <paramref name="onOpen"/>
    /// when a block opens (with how many non-namespace blocks enclose it).
    /// </summary>
    private static void Walk(
        Recognizer.SourceIndex index, List<(int Start, int End)> regions,
        Action<int, string, IReadOnlyList<Block>> onLine, Action<Block, int> onOpen)
    {
        var blocks = new List<Block>();
        var conditions = new Stack<(bool Dead, bool ParentDead)>();

        for (var line = 1; line <= index.LineCount; line++)
        {
            var code = Code(index.LineText(line));
            var deadNow = conditions.Count > 0 && conditions.Peek().Dead;

            if (DirectiveIf.Match(code) is { Success: true } ifMatch)
            {
                var cond = ifMatch.Groups["cond"].Value.Trim();
                var literalFalse = cond.Equals("False", StringComparison.OrdinalIgnoreCase) || cond == "0";
                conditions.Push((deadNow || literalFalse, deadNow));
                continue;
            }

            if (DirectiveElse.IsMatch(code) && conditions.Count > 0)
            {
                var (_, parentDead) = conditions.Pop();
                // The #Else of a literally-false #If is live; every other branch is unevaluated, so live — unless the whole
                // #If sits inside a dead block.
                conditions.Push((parentDead, parentDead));
                continue;
            }

            if (DirectiveEnd.IsMatch(code) && conditions.Count > 0)
            {
                conditions.Pop();
                continue;
            }

            if (deadNow || regions.Any(r => line >= r.Start && line <= r.End))
            {
                continue;
            }

            if (BlockEnd.Match(code) is { Success: true } end)
            {
                var at = blocks.FindLastIndex(b => b.Kind.Equals(end.Groups["kind"].Value, StringComparison.OrdinalIgnoreCase));
                if (at >= 0)
                {
                    blocks.RemoveRange(at, blocks.Count - at);
                }

                continue;
            }

            if (BlockStart.Match(WithoutAttributes(code)) is { Success: true } start)
            {
                var block = new Block(start.Groups["kind"].Value, start.Groups["name"].Value);
                onOpen(block, blocks.Count(b => !b.Kind.Equals("Namespace", StringComparison.OrdinalIgnoreCase)));
                blocks.Add(block);
                continue;
            }

            onLine(line, code, blocks);
        }
    }

    /// <summary>The line with leading attribute blocks (<c>&lt;Obsolete("x")&gt; &lt;A&gt;</c>) removed.</summary>
    private static string WithoutAttributes(string code)
    {
        var text = code.TrimStart();
        while (text.StartsWith("<", StringComparison.Ordinal))
        {
            var depth = 0;
            var inString = false;
            var end = -1;
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (ch == '"') inString = !inString;
                else if (inString) continue;
                else if (ch == '<') depth++;
                else if (ch == '>' && --depth == 0) { end = i; break; }
            }

            if (end < 0)
            {
                return code;   // an unterminated attribute: not a declaration we can read
            }

            text = text[(end + 1)..].TrimStart();
        }

        return text;
    }

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
    /// The parameters after a Sub's name: skips a <c>(Of …)</c> type-parameter list, follows a <c>_</c> continuation
    /// before the list, then reads the list (which may itself continue).
    /// </summary>
    private static IReadOnlyList<FormCodeParameter> ParameterList(Recognizer.SourceIndex index, int line, string rest)
    {
        var text = rest.Trim();
        while ((text == "_" || Continuation.IsMatch(" " + text)) && line < index.LineCount && !text.Contains('('))
        {
            line++;
            text = Code(index.LineText(line)).Trim();
        }

        if (Regex.IsMatch(text, @"^\(\s*Of\s", RegexOptions.IgnoreCase))
        {
            var close = MatchingParen(text);
            text = close < 0 ? "" : text[(close + 1)..].TrimStart();
        }

        return text.StartsWith("(", StringComparison.Ordinal)
            ? Parameters(ParameterText(index, line, text))
            : Array.Empty<FormCodeParameter>();
    }

    private static int MatchingParen(string text)
    {
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')' && --depth == 0) return i;
        }

        return -1;
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

            // An explicit "_" continuation (after any whitespace — a tab too) is just whitespace inside the list.
            var current = text.ToString();
            var m = Continuation.Match(current);
            if (m.Success)
            {
                text.Length = m.Index;
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
        var trimmed = text.Trim();
        var mods = ParameterModifiers.Match(trimmed);
        var byRef = Regex.IsMatch(mods.Groups["mods"].Value, @"\bByRef\b", RegexOptions.IgnoreCase);
        var p = trimmed[mods.Length..];
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

        return new FormCodeParameter(name, string.IsNullOrEmpty(type) ? null : type, byRef);
    }
}
