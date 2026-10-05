using System.Text;
using System.Text.RegularExpressions;

namespace BasicLang.Forms;

/// <summary>One parameter of a declared Sub: its name, its <c>As</c> type as written (null when untyped), and whether it is ByRef.</summary>
public sealed record FormCodeParameter(string Name, string? Type, bool IsByRef = false);

/// <summary>A <c>Sub</c> the user declared in a code-behind's FORM class, outside the designer's regions.</summary>
/// <param name="Line">The 1-based line of its <c>Sub</c> keyword.</param>
/// <param name="IsShared">Declared <c>Shared</c>: never a handler (an <c>AddressOf</c> of a Shared Sub emits a bare,
/// undefined name on the JavaScript backend — measured; the designer's wiring is instance-shaped on both targets).</param>
public sealed record FormDeclaredSub(string Name, int Line, IReadOnlyList<FormCodeParameter> Parameters, bool IsShared = false);

/// <summary>
/// One scan of a code-behind's FORM class: its Subs, and the names of its OTHER members (Functions, Properties, Events,
/// fields, Consts) — what a new handler name must not collide with.
/// </summary>
public sealed record FormCodeScanResult(IReadOnlyList<FormDeclaredSub> Subs, IReadOnlyCollection<string> OtherMembers)
{
    /// <summary>The Sub named <paramref name="name"/>, ignoring case as BasicLang binds names, or null.</summary>
    public FormDeclaredSub? Find(string name) =>
        Subs.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
}

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
/// <item>Member names (<see cref="FormCodeScanResult.OtherMembers"/>) come from MEMBER-LEVEL lines only (review round 4):
/// Functions, Properties, Events, Delegates, Declares, Consts, fields with or without <c>Dim</c> (<c>WithEvents</c>, several
/// names), and nested type names. A member's body — a Sub, a Function, an expanded Property, a Custom Event, an Operator, a
/// multi-line lambda — is skipped, so a local is never a member.</item>
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

    // ⛔ BasicLang's OWN directive table (review ruling 1, CRITICAL), read from the compiler rather than guessed:
    //   BasicLangLexer.ScanDirective — #If, #ElseIf, #Else, #EndIf (ONE word), #Define, #Undef/#Undefine, #Include, #Const,
    //   #Region, #End Region; Preprocessor.Process — #IfDef, #IfNDef, #Else, #EndIf (StartsWith, so #ElseIf too).
    //   VB's two-word `#End If` is marked Unknown by the lexer; accepted here too, so a VB habit cannot leave a block open.
    // Only the conditional ones matter to a scanner: #If (dead when literally False/0), #IfDef/#IfNDef (pushed LIVE so
    // their own #Else/#EndIf never pop the enclosing entry), #Else/#ElseIf, #EndIf/#End If. #End Region is not an #EndIf.
    private static readonly Regex DirectiveIf = new(@"^\s*#\s*If(?![A-Za-z0-9_])\s*(?<cond>.*?)\s*(?:Then)?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex DirectiveIfDef = new(@"^\s*#\s*IfN?Def\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex DirectiveElse = new(@"^\s*#\s*Else(?:If)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex DirectiveEnd = new(@"^\s*#\s*(?:EndIf|End\s+If)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private const string MemberModifiers =
        "(?:(?:Public|Private|Protected|Friend|Shared|Overrides|Overridable|NotOverridable|MustOverride|Overloads|Shadows|Async|Partial|Iterator|ReadOnly|WriteOnly|Default|Static|Custom|Widening|Narrowing)\\s+)*";

    /// <summary>
    /// A NAMED member that is not a Sub: Function, Property, Event (Custom too), Delegate Sub/Function, Declare Sub/Function
    /// (with an optional Ansi/Unicode/Auto), Const, Dim. <c>kind</c> says whether a body follows.
    /// </summary>
    private static readonly Regex OtherMember = new(
        @"^\s*" + MemberModifiers +
        @"(?<kind>Function|Property|Event|Delegate\s+(?:Sub|Function)|Declare\s+(?:(?:Ansi|Unicode|Auto)\s+)?(?:Sub|Function)|Const|Dim)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// A FIELD with no <c>Dim</c> (round 4 ruling 3): modifiers (at least one) — <c>WithEvents</c> among them — then one or
    /// more names (<c>Public a, b As Integer</c>, <c>items() As String</c>). ⚠ Tried AFTER every keyword form, so
    /// <c>Private Sub</c> never reads as a field named <c>Sub</c>.
    /// </summary>
    private static readonly Regex Field = new(
        @"^\s*(?:(?:Public|Private|Protected|Friend|Shared|Shadows|ReadOnly|Static|WithEvents)\s+)+(?<names>[A-Za-z_][A-Za-z0-9_]*(?:\s*\([^)]*\))?(?:\s*,\s*[A-Za-z_][A-Za-z0-9_]*(?:\s*\([^)]*\))?)*)\s*(?:As\b|=|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The end of a member body: <c>End Sub</c>, <c>End Function</c>, <c>End Property</c>, <c>End Event</c>, <c>End Operator</c>.</summary>
    private static readonly Regex BodyEnd = new(@"^\s*End\s+(?<kind>Sub|Function|Property|Event|Operator)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>An <c>Operator</c> declaration (a body; no name to collide with).</summary>
    private static readonly Regex OperatorDeclaration = new(@"^\s*" + MemberModifiers + @"Operator\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// A MULTI-LINE lambda opening inside a body — <c>Sub(…)</c>/<c>Function(…)</c> with nothing after its parameter list
    /// (and an optional <c>As T</c>) — whose own <c>End Sub</c>/<c>End Function</c> must not end the enclosing member.
    /// </summary>
    private static readonly Regex LambdaStart = new(
        @"(?<![A-Za-z0-9_])(?<kind>Sub|Function)\s*\((?:[^()]|\([^()]*\))*\)\s*(?:As\s+[\w.]+(?:\([^)]*\))?\s*)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex MustOverride = new(@"\bMustOverride\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex AccessorStart = new(
        @"^\s*(?:(?:Public|Private|Protected|Friend)\s+)*(?:Get|Set|AddHandler|RemoveHandler|RaiseEvent)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex SharedModifier = new(@"^\s*(?:\w+\s+)*?Shared\s", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ParameterModifiers = new(
        @"^(?<mods>(?:(?:ByVal|ByRef|Optional|ParamArray)\s+)*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Continuation = new(@"\s_\s*$", RegexOptions.CultureInvariant);

    private sealed record Block(string Kind, string Name);

    /// <summary>Every Sub the FORM class declares, in document order. <paramref name="formName"/>: the form's class name.</summary>
    public static IReadOnlyList<FormDeclaredSub> DeclaredSubs(string codeText, string? formName = null) =>
        Scan(codeText, formName).Subs;

    /// <summary>ONE scan of the form class: its Subs and its other members' names (see <see cref="FormCodeScanResult"/>).</summary>
    public static FormCodeScanResult Scan(string codeText, string? formName = null)
    {
        var others = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

        // Pass 2 — the form class's MEMBER-LEVEL lines (round 4 ruling 3): its Subs, and every other member's name. ⛔ The
        // lines inside a member's BODY (a Sub, Function, Property with accessors, Custom Event, Operator, a multi-line lambda)
        // are skipped — a `Dim total` there is a local, never a member, and refusing its name was over-strict.
        var subs = new List<FormDeclaredSub>();
        var classOrdinal = -1;
        string? body = null;      // the End keyword that closes the member body we are in (Sub/Function/Property/Event/Operator)
        var lambdaDepth = 0;      // multi-line lambdas open inside that body
        Walk(index, regions, (line, code, blocks) =>
        {
            var owners = blocks.Where(b => !b.Kind.Equals("Namespace", StringComparison.OrdinalIgnoreCase)).ToList();
            if (owners.Count != 1 || !owners[0].Kind.Equals("Class", StringComparison.OrdinalIgnoreCase) ||
                classOrdinal != formClass.Ordinal || formClass.Name == null)
            {
                return;
            }

            if (body != null)
            {
                if (BodyEnd.Match(code) is { Success: true } end)
                {
                    var kind = end.Groups["kind"].Value;
                    if (lambdaDepth > 0 && (kind.Equals("Sub", StringComparison.OrdinalIgnoreCase) ||
                                            kind.Equals("Function", StringComparison.OrdinalIgnoreCase)))
                    {
                        lambdaDepth--;
                    }
                    else if (kind.Equals(body, StringComparison.OrdinalIgnoreCase))
                    {
                        body = null;
                    }
                }
                else if (LambdaStart.IsMatch(code))
                {
                    lambdaDepth++;
                }

                return;
            }

            var declaration = WithoutAttributes(code);
            var match = Declaration.Match(declaration);
            if (match.Success)
            {
                var rest = match.Groups["rest"].Value;
                var parameters = ParameterList(index, line, rest);
                var prefix = declaration[..match.Groups["name"].Index];
                subs.Add(new FormDeclaredSub(match.Groups["name"].Value, line, parameters, SharedModifier.IsMatch(prefix)));
                if (!MustOverride.IsMatch(prefix))
                {
                    Enter("Sub");
                }

                return;
            }

            if (OperatorDeclaration.IsMatch(declaration))
            {
                Enter("Operator");
                return;
            }

            if (OtherMember.Match(declaration) is { Success: true } other)
            {
                others.Add(other.Groups["name"].Value);
                var kind = other.Groups["kind"].Value;
                var prefix = declaration[..other.Groups["kind"].Index];
                if (kind.Equals("Function", StringComparison.OrdinalIgnoreCase) && !MustOverride.IsMatch(prefix))
                {
                    Enter("Function");
                }
                else if (kind.Equals("Property", StringComparison.OrdinalIgnoreCase) && !MustOverride.IsMatch(prefix) &&
                         NextCodeLine(index, regions, line) is { } next && AccessorStart.IsMatch(WithoutAttributes(next)))
                {
                    Enter("Property");   // an expanded property; an auto-property (`Property X As T`) has no body
                }
                else if (kind.Equals("Event", StringComparison.OrdinalIgnoreCase) &&
                         Regex.IsMatch(prefix, @"\bCustom\b", RegexOptions.IgnoreCase))
                {
                    Enter("Event");
                }
                else if (LambdaStart.Match(declaration) is { Success: true } initializer)
                {
                    Enter(initializer.Groups["kind"].Value);   // `Dim f = Sub(x)` … `End Sub` at member level
                }

                return;
            }

            if (Field.Match(declaration) is { Success: true } field)
            {
                foreach (var name in field.Groups["names"].Value.Split(','))
                {
                    var bare = Regex.Replace(name, @"\(.*$", "").Trim();
                    if (bare.Length > 0 && !BasicLang.Compiler.Lexer.IsKeyword(bare))
                    {
                        others.Add(bare);
                    }
                }

                if (LambdaStart.Match(declaration) is { Success: true } initializer)
                {
                    Enter(initializer.Groups["kind"].Value);
                }
            }
        }, (block, depthOutsideNamespaces) =>
        {
            if (depthOutsideNamespaces == 0 && block.Kind.Equals("Class", StringComparison.OrdinalIgnoreCase))
            {
                classOrdinal++;
            }
            else if (depthOutsideNamespaces == 1 && body == null && classOrdinal == formClass.Ordinal && formClass.Name != null &&
                     !block.Kind.Equals("Namespace", StringComparison.OrdinalIgnoreCase))
            {
                others.Add(block.Name);   // a nested Class/Structure/Enum/Interface/Module is a member name of the form
            }
        });

        return new FormCodeScanResult(subs, others);

        void Enter(string kind)
        {
            body = kind;
            lambdaDepth = 0;
        }
    }

    /// <summary>The next physical code line after <paramref name="line"/> (comments stripped, blanks and region lines skipped), or null.</summary>
    private static string? NextCodeLine(Recognizer.SourceIndex index, List<(int Start, int End)> regions, int line)
    {
        for (var next = line + 1; next <= index.LineCount; next++)
        {
            if (regions.Any(r => next >= r.Start && next <= r.End))
            {
                continue;
            }

            var code = Code(index.LineText(next));
            if (!string.IsNullOrWhiteSpace(code))
            {
                return code;
            }
        }

        return null;
    }

    /// <summary>The form class's Sub named <paramref name="name"/>, matched IGNORING CASE as BasicLang binds names, or null.</summary>
    public static FormDeclaredSub? FindSub(string codeText, string name, string? formName = null) =>
        Scan(codeText, formName).Find(name);

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

            if (DirectiveIfDef.IsMatch(code))
            {
                // Unevaluated (it needs the #Define set): live, unless it sits inside a dead block.
                conditions.Push((deadNow, deadNow));
                continue;
            }

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

            // A logical line: a trailing ` _` (after any whitespace) continues onto the next physical line — a split class
            // header, `Sub G(Of T) _`, a parameter list. Reported at its FIRST line.
            var first = line;
            while (Continuation.Match(code) is { Success: true } more && line < index.LineCount)
            {
                line++;
                code = code[..more.Index] + " " + Code(index.LineText(line)).Trim();
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

            onLine(first, code, blocks);
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
