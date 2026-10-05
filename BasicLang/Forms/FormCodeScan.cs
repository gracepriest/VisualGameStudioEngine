using System.Text;
using BasicLang.Compiler;
using LexToken = BasicLang.Compiler.Token;

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
/// BL8013 ordering check all ask it.
///
/// <para>⛔ Built on BasicLang's OWN lexer (<see cref="Lexer"/>), not on line regexes (review round 5, coordinator decision:
/// the regex scanner drew findings in four review rounds). The lexer already knows comments, strings, <c>_</c>
/// continuations, keywords and directives, and it works on code that does not parse — this runs on a file the user is midway
/// through editing. The scanner works on STATEMENTS: logical lines (continuations joined by the lexer, a line also joined
/// while a parenthesis is open), split at top-level <c>:</c>.</para>
/// <list type="bullet">
/// <item>Only the FORM class's own members: the class named after the form, else the first top-level class (a
/// <c>Namespace</c> is transparent). A nested <c>Class</c>/<c>Structure</c>/<c>Interface</c>/<c>Module</c>/<c>Enum</c> is a
/// member NAME of the form, and its own members are not the form's.</item>
/// <item>A Sub inside <c>#If False</c> / <c>#If 0</c> is skipped (its <c>#Else</c> counts). Any other <c>#If</c> — and
/// <c>#IfDef</c>/<c>#IfNDef</c> — is read as active on every branch (full evaluation is piece 2's <c>ProcessForEditor</c>).</item>
/// <item>A declaration is attributes, modifiers, <c>Sub</c>, a name, an optional <c>(Of T)</c>, and an optional parameter
/// list. <c>ByRef</c> is kept (<see cref="FormCodeParameter.IsByRef"/>; it never fits an event, CS0123); <c>Shared</c> is
/// kept (never a handler).</item>
/// <item>Member names come from member-level statements only: Functions, Properties, Events, Delegates, Declares, Consts,
/// fields with or without <c>Dim</c> — every declarator (<c>Dim a, b</c>, <c>Private a As T, b As U</c>), <c>WithEvents</c>
/// being a modifier — and nested type names. A member BODY (a Sub, a Function, an expanded Property, a Custom Event, an
/// Operator, a multi-line lambda) is skipped, so a local is never a member. A body the user has not closed yet is closed by
/// the next member declaration (a local statement never starts with an access modifier).</item>
/// <item>A Sub inside a designer region (<c>InitializeComponent</c>, the <c>VgsOn_</c> wrappers) is not the user's.</item>
/// </list>
/// <para>⚠ Where the lexer cannot help, said and handled: <c>#End If</c>, <c>#IfDef</c>, <c>#IfNDef</c> arrive as
/// <c>Unknown</c> tokens (read by their text); <c>End Event</c>/<c>End AddHandler</c>/<c>End RemoveHandler</c>/
/// <c>End RaiseEvent</c> arrive as a bare <c>End</c> identifier with the second word swallowed (read from the line); an
/// unterminated string — or any other lexer error — makes it throw, so the scan falls back to one line at a time, each cut
/// at its error.</para>
/// </summary>
public static class FormCodeScan
{
    /// <summary>A statement: its tokens (comments gone, attributes still present) and the raw source lines.</summary>
    private sealed record Statement(IReadOnlyList<LexToken> Tokens)
    {
        public bool IsDirective => Tokens.Count > 0 && Tokens[0].Lexeme.StartsWith('#');
    }

    private sealed record Block(string Kind, string Name);

    private static readonly HashSet<TokenType> ModifierTokens = new()
    {
        TokenType.Public, TokenType.Private, TokenType.Protected, TokenType.Friend, TokenType.Shared,
        TokenType.Overridable, TokenType.Overrides, TokenType.MustOverride, TokenType.NotOverridable, TokenType.MustInherit,
        TokenType.Async, TokenType.Iterator, TokenType.ReadOnly, TokenType.WriteOnly, TokenType.Widening, TokenType.Narrowing,
    };

    /// <summary>Modifiers the lexer has no keyword for (they arrive as identifiers).</summary>
    private static readonly HashSet<string> ModifierWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Overloads", "Shadows", "Partial", "Default", "Static", "WithEvents", "Custom", "NotInheritable",
    };

    private static readonly HashSet<TokenType> AccessTokens = new()
    {
        TokenType.Public, TokenType.Private, TokenType.Protected, TokenType.Friend,
    };

    private static readonly Dictionary<TokenType, string> BlockStarts = new()
    {
        [TokenType.Class] = "Class", [TokenType.Structure] = "Structure", [TokenType.Interface] = "Interface",
        [TokenType.Module] = "Module", [TokenType.Enum] = "Enum", [TokenType.Namespace] = "Namespace",
    };

    private static readonly Dictionary<TokenType, string> BlockEnds = new()
    {
        [TokenType.EndClass] = "Class", [TokenType.EndStructure] = "Structure", [TokenType.EndInterface] = "Interface",
        [TokenType.EndModule] = "Module", [TokenType.EndEnum] = "Enum", [TokenType.EndNamespace] = "Namespace",
    };

    /// <summary>Every Sub the FORM class declares, in document order. <paramref name="formName"/>: the form's class name.</summary>
    public static IReadOnlyList<FormDeclaredSub> DeclaredSubs(string codeText, string? formName = null) =>
        Scan(codeText, formName).Subs;

    /// <summary>The form class's Sub named <paramref name="name"/>, matched IGNORING CASE as BasicLang binds names, or null.</summary>
    public static FormDeclaredSub? FindSub(string codeText, string name, string? formName = null) =>
        Scan(codeText, formName).Find(name);

    /// <summary>ONE scan of the form class: its Subs and its other members' names (see <see cref="FormCodeScanResult"/>).</summary>
    public static FormCodeScanResult Scan(string codeText, string? formName = null)
    {
        var lines = codeText.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var statements = Statements(Tokens(codeText, lines, RegionLines(codeText)));

        // Pass 1 — which top-level class is the form's: the one named after it, else the first.
        var classes = new List<string>();
        Walk(statements, (_, _) => { }, (block, depth) =>
        {
            if (depth == 0 && block.Kind == "Class")
            {
                classes.Add(block.Name);
            }
        }, () => { });

        var formOrdinal = classes.FindIndex(c => formName != null && c.Equals(formName, StringComparison.OrdinalIgnoreCase));
        if (formOrdinal < 0)
        {
            formOrdinal = classes.Count > 0 ? 0 : -1;
        }

        // Pass 2 — the form class's member-level statements.
        var subs = new List<FormDeclaredSub>();
        var others = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var classOrdinal = -1;
        string? body = null;          // the End keyword that closes the body we are in
        var lambdaDepth = 0;          // multi-line lambdas open inside that body
        var pendingProperty = false;  // a Property header was read; its accessors may follow

        Walk(statements, (statement, blocks) =>
        {
            var owners = blocks.Where(b => b.Kind != "Namespace").ToList();
            if (owners.Count != 1 || owners[0].Kind != "Class" || classOrdinal != formOrdinal || formOrdinal < 0)
            {
                return;
            }

            var tokens = WithoutAttributes(statement.Tokens);

            if (body != null)
            {
                var end = EndKind(tokens, lines);
                if (end != null)
                {
                    if (lambdaDepth > 0 && end is "Sub" or "Function")
                    {
                        lambdaDepth--;
                    }
                    else if (end.Equals(body, StringComparison.OrdinalIgnoreCase))
                    {
                        body = null;
                    }

                    return;
                }

                if (!DeclaresAMember(tokens))
                {
                    if (IsMultiLineLambda(tokens))
                    {
                        lambdaDepth++;
                    }

                    return;
                }

                // Mid-edit: a member declaration inside a body the user has not closed yet closes it.
                body = null;
                lambdaDepth = 0;
            }

            if (pendingProperty)
            {
                if (tokens.Count == 0)
                {
                    return;   // an attribute line between the header and its accessor
                }

                pendingProperty = false;
                var first = SkipModifiers(tokens, 0);
                if (first < tokens.Count && tokens[first].Type is TokenType.Get or TokenType.Set)
                {
                    body = "Property";
                    return;
                }
            }

            ReadMember(tokens, subs, others, kind => { body = kind; lambdaDepth = 0; }, () => pendingProperty = true);
        }, (block, depth) =>
        {
            if (depth == 0 && block.Kind == "Class")
            {
                classOrdinal++;
            }
            else if (depth == 1 && body == null && classOrdinal == formOrdinal && formOrdinal >= 0 && block.Kind != "Namespace")
            {
                others.Add(block.Name);   // a nested type is a member name of the form
            }

            body = null;
            lambdaDepth = 0;
            pendingProperty = false;
        }, () =>
        {
            body = null;
            lambdaDepth = 0;
            pendingProperty = false;
        });

        return new FormCodeScanResult(subs, others);
    }

    // ==================================================================
    // Tokens and statements
    // ==================================================================

    /// <summary>The 1-based line ranges of the designer regions (their Subs are the designer's, never the user's).</summary>
    private static List<(int Start, int End)> RegionLines(string codeText)
    {
        var index = new Recognizer.SourceIndex(codeText);
        return RegionMarkers.Scan(codeText)
            .Where(r => r.EndOffset > r.StartOffset)
            .Select(r => (index.PositionOf(r.StartOffset).Line, index.PositionOf(r.EndOffset).Line))
            .ToList();
    }

    /// <summary>
    /// The file's tokens, comments and region lines removed, a <see cref="TokenType.Newline"/> between logical lines. ⚠ The
    /// lexer THROWS on an unterminated string (normal mid-edit): then each physical line is tokenized on its own, cut at its
    /// error, and a trailing <c>_</c> joins it to the next — what the whole-file lexer does with a continuation.
    /// </summary>
    private static List<LexToken> Tokens(string codeText, string[] lines, List<(int Start, int End)> regions)
    {
        List<LexToken>? tokens;
        try
        {
            tokens = new Lexer(codeText).Tokenize();

            // ⚠ The lexer lets a string run across lines until the next quote — so an unterminated string mid-edit does NOT
            // throw when a later line has a quote: it swallows everything up to it (measured: a Sub after it vanished). A
            // string spanning a line break is treated like the error it almost always is here.
            if (tokens.Any(t => t.Type is TokenType.StringLiteral or TokenType.InterpolatedStringLiteral && t.Lexeme.Contains('\n')))
            {
                tokens = null;
            }
        }
        catch (Exception e) when (IsNonFatal(e))
        {
            // ⛔ Not only LexerException (round 6, CRITICAL): a literal too big for a Long throws a RAW OverflowException
            // (long.Parse / Convert.ToInt64 inside the lexer) — half-typed mid-edit, it threw out of the grid's refresh.
            tokens = null;
        }

        if (tokens == null)
        {
            tokens = new List<LexToken>();
            for (var i = 0; i < lines.Length; i++)
            {
                var lineTokens = TokenizeLine(lines[i]);
                foreach (var t in lineTokens)
                {
                    t.Line = i + 1;
                }

                if (lineTokens.Count > 0 && lineTokens[^1] is { Type: TokenType.Identifier, Lexeme: "_" })
                {
                    tokens.AddRange(lineTokens.Take(lineTokens.Count - 1));   // a continuation: no line break
                    continue;
                }

                tokens.AddRange(lineTokens);
                tokens.Add(new LexToken(TokenType.Newline, "\\n", null, i + 1, 1));
            }
        }

        return tokens
            .Where(t => t.Type is not (TokenType.Comment or TokenType.EOF))
            .Where(t => !regions.Any(r => t.Line >= r.Start && t.Line <= r.End))
            .ToList();
    }

    /// <summary>
    /// One physical line's tokens, cut at its error. A <see cref="LexerException"/> says where (its column); any other
    /// non-fatal failure (a raw <see cref="OverflowException"/> from a literal) does not, so the line is cut back one
    /// word at a time until what remains tokenizes — `Private big As Long = 999…L` still names `big`.
    /// </summary>
    private static List<LexToken> TokenizeLine(string line)
    {
        while (line.Length > 0)
        {
            try
            {
                return new Lexer(line).Tokenize().Where(t => t.Type != TokenType.EOF).ToList();
            }
            catch (LexerException e) when (e.Column > 1 && e.Column - 1 < line.Length)
            {
                line = line[..(e.Column - 1)];   // keep what precedes the error
            }
            catch (Exception e) when (IsNonFatal(e))
            {
                var cut = line.TrimEnd().LastIndexOfAny(new[] { ' ', '\t', '=', '(', ',' });
                line = cut <= 0 ? "" : line[..cut];
            }
        }

        return new List<LexToken>();
    }

    /// <summary>
    /// Every exception but the ones a process cannot recover from — what a scan of a half-typed file may swallow.
    /// ⚠ <see cref="StackOverflowException"/> and <see cref="ThreadAbortException"/> can never reach a catch on .NET 8 (a
    /// stack overflow ends the process; thread abort is not supported); they are listed for clarity only.
    /// </summary>
    private static bool IsNonFatal(Exception e) =>
        e is not (OutOfMemoryException or StackOverflowException or AccessViolationException or ThreadAbortException);

    /// <summary>
    /// Logical lines split at top-level <c>:</c> (a one-line <c>Sub X() : End Sub</c> is two statements). A line ending
    /// inside open parentheses continues onto the next (implicit continuation) — unless the next line starts a declaration
    /// or an <c>End</c>, so a <c>Sub Foo(</c> typed mid-edit cannot swallow the rest of the file.
    /// </summary>
    private static List<Statement> Statements(List<LexToken> tokens)
    {
        // Physical/logical lines first.
        var lines = new List<List<LexToken>> { new() };
        foreach (var t in tokens)
        {
            if (t.Type == TokenType.Newline)
            {
                lines.Add(new List<LexToken>());
            }
            else
            {
                lines[^1].Add(t);
            }
        }

        var joined = new List<List<LexToken>>();
        foreach (var line in lines.Where(l => l.Count > 0))
        {
            if (joined.Count > 0 && ParenDepth(joined[^1]) > 0 && !StartsADeclarationOrEnd(line) && !line[0].Lexeme.StartsWith('#'))
            {
                joined[^1].AddRange(line);
            }
            else
            {
                joined.Add(new List<LexToken>(line));
            }
        }

        var statements = new List<Statement>();
        foreach (var line in joined)
        {
            if (line[0].Lexeme.StartsWith('#'))
            {
                statements.Add(new Statement(line));
                continue;
            }

            var depth = 0;
            var current = new List<LexToken>();
            foreach (var t in line)
            {
                depth += t.Type is TokenType.LeftParen or TokenType.LeftBrace ? 1 : t.Type is TokenType.RightParen or TokenType.RightBrace ? -1 : 0;
                if (t.Type == TokenType.Colon && depth <= 0)
                {
                    if (current.Count > 0)
                    {
                        statements.Add(new Statement(current));
                    }

                    current = new List<LexToken>();
                    continue;
                }

                current.Add(t);
            }

            if (current.Count > 0)
            {
                statements.Add(new Statement(current));
            }
        }

        return statements;
    }

    private static int ParenDepth(IEnumerable<LexToken> tokens) =>
        tokens.Sum(t => t.Type == TokenType.LeftParen ? 1 : t.Type == TokenType.RightParen ? -1 : 0);

    private static bool StartsADeclarationOrEnd(IReadOnlyList<LexToken> line)
    {
        var tokens = WithoutAttributes(line);
        if (tokens.Count == 0)
        {
            return false;
        }

        var first = tokens[0];
        return AccessTokens.Contains(first.Type) || BlockEnds.ContainsKey(first.Type) ||
               first.Type is TokenType.EndSub or TokenType.EndFunction or TokenType.EndProperty or TokenType.EndOperator
                   or TokenType.Sub or TokenType.Function or TokenType.Property or TokenType.Event ||
               first is { Type: TokenType.Identifier, Lexeme: var word } && word.Equals("End", StringComparison.OrdinalIgnoreCase);
    }

    // ==================================================================
    // The walk: directives, blocks
    // ==================================================================

    /// <summary>
    /// Walks the statements outside <c>#If False</c>/<c>#If 0</c>, tracking the block stack: <paramref name="onStatement"/>
    /// for each non-block statement with the blocks it sits in; <paramref name="onOpen"/> when a block opens (with how many
    /// non-namespace blocks enclose it); <paramref name="onClose"/> when one closes.
    /// </summary>
    private static void Walk(
        List<Statement> statements, Action<Statement, IReadOnlyList<Block>> onStatement, Action<Block, int> onOpen, Action onClose)
    {
        var blocks = new List<Block>();
        var conditions = new Stack<(bool Dead, bool ParentDead)>();

        foreach (var statement in statements)
        {
            var deadNow = conditions.Count > 0 && conditions.Peek().Dead;

            if (statement.IsDirective)
            {
                var directive = statement.Tokens[0];
                var word = directive.Lexeme.ToLowerInvariant();
                if (directive.Type == TokenType.PreprocessorIf)
                {
                    var cond = statement.Tokens.Skip(1).Where(t => t.Type != TokenType.Then).ToList();
                    var literalFalse = cond.Count == 1 &&
                                       ((cond[0].Type == TokenType.BooleanLiteral && cond[0].Lexeme.Equals("False", StringComparison.OrdinalIgnoreCase)) ||
                                        (cond[0].Type == TokenType.IntegerLiteral && cond[0].Lexeme == "0"));
                    conditions.Push((deadNow || literalFalse, deadNow));
                }
                else if (word is "#ifdef" or "#ifndef")
                {
                    // ⚠ The lexer calls these Unknown; unevaluated (they need the #Define set) — live unless inside a dead block.
                    conditions.Push((deadNow, deadNow));
                }
                else if (directive.Type is TokenType.PreprocessorElse or TokenType.PreprocessorElseIf && conditions.Count > 0)
                {
                    var (_, parentDead) = conditions.Pop();
                    conditions.Push((parentDead, parentDead));
                }
                else if ((directive.Type == TokenType.PreprocessorEndIf || word == "#end") && conditions.Count > 0)
                {
                    // ⚠ `#End If` (VB's spelling) reaches us as an Unknown "#End" — the lexer swallows the "If".
                    conditions.Pop();
                }

                continue;
            }

            if (deadNow)
            {
                continue;
            }

            var tokens = WithoutAttributes(statement.Tokens);
            if (tokens.Count > 0 && BlockEnds.TryGetValue(tokens[0].Type, out var endKind))
            {
                var at = blocks.FindLastIndex(b => b.Kind == endKind);
                if (at >= 0)
                {
                    blocks.RemoveRange(at, blocks.Count - at);
                }

                onClose();
                continue;
            }

            var start = SkipModifiers(tokens, 0);
            if (start < tokens.Count && BlockStarts.TryGetValue(tokens[start].Type, out var kind) && start + 1 < tokens.Count)
            {
                var name = new StringBuilder();
                for (var i = start + 1; i < tokens.Count && tokens[i].Type is TokenType.Identifier or TokenType.Dot; i++)
                {
                    name.Append(tokens[i].Lexeme);
                }

                if (name.Length > 0)
                {
                    var block = new Block(kind, name.ToString());
                    onOpen(block, blocks.Count(b => b.Kind != "Namespace"));
                    blocks.Add(block);
                    continue;
                }
            }

            onStatement(statement, blocks);
        }
    }

    // ==================================================================
    // Members
    // ==================================================================

    /// <summary>Reads one member-level statement: a Sub (recorded), any other named member (its name), a field's names.</summary>
    private static void ReadMember(
        IReadOnlyList<LexToken> tokens, List<FormDeclaredSub> subs, HashSet<string> others, Action<string> enter, Action propertyHeader)
    {
        if (tokens.Count == 0)
        {
            return;
        }

        var k = SkipModifiers(tokens, 0);
        if (k >= tokens.Count)
        {
            return;
        }

        var mustOverride = tokens.Take(k).Any(t => t.Type == TokenType.MustOverride);
        var keyword = tokens[k];

        switch (keyword.Type)
        {
            case TokenType.Sub when Name(tokens, k + 1, allowNew: true, afterMemberKeyword: true) is { } subName:
            {
                var isShared = tokens.Take(k).Any(t => t.Type == TokenType.Shared);
                subs.Add(new FormDeclaredSub(subName.Name, keyword.Line, Parameters(tokens, subName.Next), isShared));
                if (!mustOverride)
                {
                    enter("Sub");
                }

                return;
            }

            case TokenType.Operator:
                enter("Operator");
                return;

            case TokenType.Function when Name(tokens, k + 1, afterMemberKeyword: true) is { } functionName:
                others.Add(functionName.Name);
                if (!mustOverride)
                {
                    enter("Function");
                }

                return;

            case TokenType.Property when Name(tokens, k + 1, afterMemberKeyword: true) is { } propertyName:
                others.Add(propertyName.Name);
                if (!mustOverride)
                {
                    propertyHeader();   // an auto-property has no accessors; an expanded one's Get/Set come next
                }

                return;

            case TokenType.Event when Name(tokens, k + 1, afterMemberKeyword: true) is { } eventName:
                others.Add(eventName.Name);
                if (tokens.Take(k).Any(t => t.Lexeme.Equals("Custom", StringComparison.OrdinalIgnoreCase)))
                {
                    enter("Event");
                }

                return;

            case TokenType.Delegate when k + 1 < tokens.Count && Name(tokens, k + 2, afterMemberKeyword: true) is { } delegateName:
                others.Add(delegateName.Name);
                return;

            case TokenType.Declare:
            {
                var i = k + 1;
                // ⚠ By TEXT, not token type: `Auto` is a lexer KEYWORD token (round 6 fix 5), `Ansi`/`Unicode` identifiers.
                while (i < tokens.Count &&
                       (tokens[i].Lexeme.Equals("Ansi", StringComparison.OrdinalIgnoreCase) ||
                        tokens[i].Lexeme.Equals("Unicode", StringComparison.OrdinalIgnoreCase) ||
                        tokens[i].Lexeme.Equals("Auto", StringComparison.OrdinalIgnoreCase)))
                {
                    i++;
                }

                if (i < tokens.Count && tokens[i].Type is TokenType.Sub or TokenType.Function &&
                    Name(tokens, i + 1, afterMemberKeyword: true) is { } declared)
                {
                    others.Add(declared.Name);
                }

                return;
            }

            case TokenType.Dim or TokenType.Const:
                Declarators(tokens, k + 1, others);
                break;

            case TokenType.Identifier or TokenType.LeftBracket when k > 0:
                Declarators(tokens, k, others);   // a field with no Dim: `Private counter As Integer`
                break;

            default:
                return;
        }

        if (IsMultiLineLambda(tokens))
        {
            enter(LambdaKind(tokens));   // `Dim f = Sub(x)` … `End Sub` at member level
        }
    }

    /// <summary>Every declarator's name from <paramref name="from"/> on: split at top-level commas, modifiers skipped.</summary>
    private static void Declarators(IReadOnlyList<LexToken> tokens, int from, HashSet<string> others)
    {
        var depth = 0;
        var atStart = true;
        for (var i = from; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (atStart)
            {
                i = SkipModifiers(tokens, i);
                if (i < tokens.Count && Name(tokens, i) is { } name)
                {
                    others.Add(name.Name);
                    i = name.Next - 1;
                }

                atStart = false;
                continue;
            }

            depth += t.Type is TokenType.LeftParen or TokenType.LeftBrace ? 1 : t.Type is TokenType.RightParen or TokenType.RightBrace ? -1 : 0;
            if (t.Type == TokenType.Comma && depth == 0)
            {
                atStart = true;
            }
        }
    }

    /// <summary>
    /// A name at <paramref name="at"/>: an identifier, a bracketed one (<c>[Get]</c>), or — for a Sub — <c>New</c>. Returns
    /// the name and the index after it, or null.
    /// </summary>
    private static (string Name, int Next)? Name(IReadOnlyList<LexToken> tokens, int at, bool allowNew = false, bool afterMemberKeyword = false)
    {
        if (at >= tokens.Count)
        {
            return null;
        }

        if (tokens[at].Type == TokenType.Identifier || (allowNew && tokens[at].Type == TokenType.New))
        {
            return (tokens[at].Lexeme, at + 1);
        }

        // ⚠ LEXER GAP: right after Sub/Function/Property/Event/Delegate/Declare the next word IS the member's name, even
        // when the lexer calls it a keyword (`Property Auto As String` — `Auto` is a BasicLang keyword token). The regex
        // scanner read it; so does this, so a handler can never be given that member's name.
        if (afterMemberKeyword && tokens[at].Lexeme.Length > 0 && tokens[at].Lexeme.All(c => char.IsLetterOrDigit(c) || c == '_') &&
            char.IsLetter(tokens[at].Lexeme[0]) && tokens[at].Type is not (TokenType.As or TokenType.Of or TokenType.Lib))
        {
            return (tokens[at].Lexeme, at + 1);
        }

        if (tokens[at].Type == TokenType.LeftBracket && at + 2 < tokens.Count && tokens[at + 2].Type == TokenType.RightBracket)
        {
            return (tokens[at + 1].Lexeme, at + 3);
        }

        return null;
    }

    /// <summary>The parameters after a Sub's name: a <c>(Of …)</c> list skipped, then the list itself.</summary>
    private static IReadOnlyList<FormCodeParameter> Parameters(IReadOnlyList<LexToken> tokens, int at)
    {
        if (at < tokens.Count && tokens[at].Type == TokenType.LeftParen && at + 1 < tokens.Count && tokens[at + 1].Type == TokenType.Of)
        {
            at = Matching(tokens, at) + 1;
        }

        if (at <= 0 || at >= tokens.Count || tokens[at].Type != TokenType.LeftParen)
        {
            return Array.Empty<FormCodeParameter>();
        }

        var close = Matching(tokens, at);
        var inner = tokens.Skip(at + 1).Take((close < 0 ? tokens.Count : close) - at - 1).ToList();
        if (inner.Count == 0)
        {
            return Array.Empty<FormCodeParameter>();
        }

        var parts = new List<List<LexToken>> { new() };
        var depth = 0;
        foreach (var t in inner)
        {
            depth += t.Type is TokenType.LeftParen or TokenType.LeftBrace ? 1 : t.Type is TokenType.RightParen or TokenType.RightBrace ? -1 : 0;
            if (t.Type == TokenType.Comma && depth == 0)
            {
                parts.Add(new List<LexToken>());
                continue;
            }

            parts[^1].Add(t);
        }

        return parts.Where(p => p.Count > 0).Select(Parameter).ToList();
    }

    private static FormCodeParameter Parameter(List<LexToken> part)
    {
        var i = 0;
        var byRef = false;
        while (i < part.Count && part[i].Type is TokenType.ByVal or TokenType.ByRef or TokenType.Optional or TokenType.ParamArray)
        {
            byRef |= part[i].Type == TokenType.ByRef;
            i++;
        }

        var name = Name(part, i) is { } n ? n : (i < part.Count ? (part[i].Lexeme, i + 1) : ("", i));
        i = name.Item2;
        if (i + 1 < part.Count && part[i].Type == TokenType.LeftParen && part[i + 1].Type == TokenType.RightParen)
        {
            i += 2;   // name() — an array parameter
        }

        string? type = null;
        if (i < part.Count && part[i].Type == TokenType.As)
        {
            var typeTokens = new List<LexToken>();
            var depth = 0;
            for (var j = i + 1; j < part.Count; j++)
            {
                depth += part[j].Type == TokenType.LeftParen ? 1 : part[j].Type == TokenType.RightParen ? -1 : 0;
                if (part[j].Type == TokenType.Assignment && depth == 0)
                {
                    break;   // `= default`
                }

                typeTokens.Add(part[j]);
            }

            type = Render(typeTokens);
        }

        return new FormCodeParameter(name.Item1, string.IsNullOrEmpty(type) ? null : type, byRef);
    }

    /// <summary>A type as written, rebuilt from its tokens: <c>List(Of Integer)</c>, <c>System.EventArgs</c>, <c>Integer()</c>.</summary>
    private static string Render(IReadOnlyList<LexToken> tokens)
    {
        var sb = new StringBuilder();
        LexToken? previous = null;
        foreach (var t in tokens)
        {
            var tight = previous == null || previous.Type is TokenType.Dot or TokenType.LeftParen ||
                        t.Type is TokenType.Dot or TokenType.LeftParen or TokenType.RightParen or TokenType.Comma;
            if (!tight)
            {
                sb.Append(' ');
            }

            sb.Append(t.Lexeme);
            if (t.Type == TokenType.Comma)
            {
                sb.Append(' ');
            }

            previous = t.Type == TokenType.Comma ? new LexToken(TokenType.LeftParen, "", null, 0, 0) : t;
        }

        return sb.ToString().Trim();
    }

    // ==================================================================
    // Statement shapes
    // ==================================================================

    /// <summary>The tokens with leading attribute blocks (<c>&lt;Obsolete("x")&gt; &lt;A&gt;</c>) removed.</summary>
    private static IReadOnlyList<LexToken> WithoutAttributes(IReadOnlyList<LexToken> tokens)
    {
        var i = 0;
        while (i < tokens.Count && tokens[i].Type == TokenType.LessThan)
        {
            var depth = 0;
            var end = -1;
            for (var j = i; j < tokens.Count; j++)
            {
                if (tokens[j].Type == TokenType.LessThan) depth++;
                else if (tokens[j].Type == TokenType.GreaterThan && --depth == 0) { end = j; break; }
            }

            if (end < 0)
            {
                return tokens.Skip(i).ToList();   // an unterminated attribute: leave it to read as nothing
            }

            i = end + 1;
        }

        return i == 0 ? tokens : tokens.Skip(i).ToList();
    }

    private static bool IsModifier(LexToken t) =>
        ModifierTokens.Contains(t.Type) || (t.Type == TokenType.Identifier && ModifierWords.Contains(t.Lexeme));

    /// <summary>The index of the first non-modifier token at or after <paramref name="from"/>.</summary>
    private static int SkipModifiers(IReadOnlyList<LexToken> tokens, int from)
    {
        var i = from;
        while (i < tokens.Count && IsModifier(tokens[i]) &&
               // a modifier WORD followed by As/=/,/( is itself a name (`Dim Default As Integer`)
               !(tokens[i].Type == TokenType.Identifier && i + 1 < tokens.Count &&
                 tokens[i + 1].Type is TokenType.As or TokenType.Assignment or TokenType.Comma or TokenType.LeftParen))
        {
            i++;
        }

        return i;
    }

    /// <summary>
    /// Which body an <c>End …</c> statement closes: Sub, Function, Property, Operator, or — read from the LINE, because the
    /// lexer turns <c>End Event</c>/<c>End AddHandler</c>/… into a bare <c>End</c> identifier — that second word. Else null.
    /// </summary>
    private static string? EndKind(IReadOnlyList<LexToken> tokens, string[] lines)
    {
        if (tokens.Count == 0)
        {
            return null;
        }

        var first = tokens[0];
        switch (first.Type)
        {
            case TokenType.EndSub: return "Sub";
            case TokenType.EndFunction: return "Function";
            case TokenType.EndProperty: return "Property";
            case TokenType.EndOperator: return "Operator";
        }

        if (first.Type != TokenType.Identifier || !first.Lexeme.Equals("End", StringComparison.OrdinalIgnoreCase) ||
            first.Line < 1 || first.Line > lines.Length)
        {
            return null;
        }

        var text = lines[first.Line - 1];
        var after = Math.Min(text.Length, first.Column - 1 + first.Lexeme.Length);
        var word = new string(text[after..].TrimStart().TakeWhile(char.IsLetter).ToArray());
        return word.Length == 0 ? null : word;
    }

    /// <summary>
    /// A statement that declares a member (mid-edit, inside a body the user left open, it closes that body): it starts with
    /// an access modifier (a local statement never does) — or with Sub/Function/Property and a name — and is not a
    /// property's own <c>Private Set</c>/<c>Get</c>.
    /// </summary>
    private static bool DeclaresAMember(IReadOnlyList<LexToken> tokens)
    {
        if (tokens.Count == 0)
        {
            return false;
        }

        var k = SkipModifiers(tokens, 0);
        if (k < tokens.Count && tokens[k].Type is TokenType.Get or TokenType.Set)
        {
            return false;
        }

        if (AccessTokens.Contains(tokens[0].Type) || tokens[0].Type is TokenType.Shared or TokenType.Overrides or TokenType.Overridable
                or TokenType.MustOverride or TokenType.NotOverridable)
        {
            return true;
        }

        return tokens[0].Type is TokenType.Sub or TokenType.Function or TokenType.Property &&
               Name(tokens, 1, allowNew: true, afterMemberKeyword: true) != null;
    }

    /// <summary>
    /// A multi-line lambda opening: <c>Sub(…)</c>/<c>Function(…)</c> (not <c>Exit Sub</c>) with nothing after its parameter
    /// list but an optional <c>As T</c> — its own <c>End Sub</c>/<c>End Function</c> must not end the enclosing member.
    /// </summary>
    private static bool IsMultiLineLambda(IReadOnlyList<LexToken> tokens) => LambdaAt(tokens) >= 0;

    private static string LambdaKind(IReadOnlyList<LexToken> tokens) =>
        tokens[LambdaAt(tokens)].Type == TokenType.Function ? "Function" : "Sub";

    private static int LambdaAt(IReadOnlyList<LexToken> tokens)
    {
        for (var i = 0; i < tokens.Count - 1; i++)
        {
            if (tokens[i].Type is not (TokenType.Sub or TokenType.Function) || tokens[i + 1].Type != TokenType.LeftParen ||
                (i > 0 && tokens[i - 1].Type is TokenType.Exit or TokenType.Declare or TokenType.Delegate))
            {
                continue;
            }

            var close = Matching(tokens, i + 1);
            if (close < 0)
            {
                continue;
            }

            if (close == tokens.Count - 1 || tokens[close + 1].Type == TokenType.As)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The index of the parenthesis matching the one at <paramref name="open"/>, or -1.</summary>
    private static int Matching(IReadOnlyList<LexToken> tokens, int open)
    {
        var depth = 0;
        for (var i = open; i < tokens.Count; i++)
        {
            if (tokens[i].Type == TokenType.LeftParen) depth++;
            else if (tokens[i].Type == TokenType.RightParen && --depth == 0) return i;
        }

        return -1;
    }
}
