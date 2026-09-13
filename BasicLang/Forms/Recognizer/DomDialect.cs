using BasicLang.Compiler;

namespace BasicLang.Forms.Recognizer;

/// <summary>
/// Recovers a browser page from BasicLang source (D12).
///
/// <para>⛔ <b>It reads any <c>Sub</c>, not a designated one.</b> The <c>web-site</c> template has
/// no form, no class and no <c>InitializeComponent</c> — it builds the DOM directly inside
/// <c>Sub Main()</c>. A reader that looked for a designer member would recover nothing from the
/// only web fixture that exists.</para>
///
/// <para>The shape:
/// <code>
/// Dim heading As Element = doc.createElement("h1")          ' construct
/// heading.textContent = "Hello"                              ' set properties
/// doc.body.appendChild(heading)                              ' parent
/// button.addEventListener("click", Sub(e As DomEvent) …)     ' wire
/// </code>
/// The handler is normally an inline lambda here, which is idiomatic for the web and is recorded as
/// such rather than treated as a failure to find a named <c>Sub</c>.</para>
/// </summary>
public static class DomDialect
{
    public static RecognizedForm Read(string source)
    {
        var index = new SourceIndex(source);
        var cursor = new TokenCursor(new Lexer(source).Tokenize());
        var form = new RecognizedForm();
        string? currentMember = null;

        while (!cursor.AtEnd)
        {
            var token = cursor.Current!;

            switch (token.Type)
            {
                case TokenType.Class when cursor.Peek()?.Type == TokenType.Identifier:
                    form.ClassName ??= cursor.Peek()!.Lexeme;
                    cursor.Advance();
                    continue;

                case TokenType.Sub when cursor.Peek()?.Type == TokenType.Identifier:
                    currentMember = cursor.Peek()!.Lexeme;
                    cursor.Advance();
                    continue;

                case TokenType.EndSub:
                    currentMember = null;
                    cursor.Advance();
                    continue;

                // ⛔ D9 Refused. Same reason as the WinForms dialect: a 'Handles' clause is lexed
                // but never parsed, so the file cannot build at all.
                case TokenType.Handles:
                    form.Refusals.Add(new RecognitionRefusal(
                        "BL8001",
                        "A 'Handles' clause is not supported by the BasicLang parser, so this file " +
                        "cannot build. Wire the event with addEventListener instead.",
                        token.Line, token.Column));
                    cursor.SkipToNextLine();
                    continue;

                case TokenType.With:
                    HandleWith(cursor, form, token);
                    continue;

                // An AddHandler line in a DOM page wires a BasicLang event, not a DOM one, and its
                // handler is usually an inline lambda. Skip the whole line: walking into the lambda
                // body would read `heading.textContent = …` from INSIDE the handler as if it were
                // the element's designer value, so a page whose click handler rewrites a caption
                // would open showing the post-click text.
                case TokenType.AddHandler:
                    cursor.SkipToNextLine();
                    continue;
            }

            // Dim <id> As <Type> = <expr>.createElement("<tag>")
            if (token.Type == TokenType.Dim && cursor.CheckAhead(1, TokenType.Identifier))
            {
                var id = cursor.Peek(1)!.Lexeme;
                var tag = TagFromCreateElement(cursor);
                if (tag != null)
                {
                    AddControl(form, id, tag, token.Line, currentMember);
                }

                cursor.SkipToNextLine();
                continue;
            }

            if (token.Type == TokenType.Identifier)
            {
                // <id> = <expr>.createElement("<tag>")  — an element assigned to an existing local
                if (cursor.CheckAhead(1, TokenType.Assignment))
                {
                    var tag = TagFromCreateElement(cursor);
                    if (tag != null)
                    {
                        AddControl(form, token.Lexeme, tag, token.Line, currentMember);
                        cursor.SkipToNextLine();
                        continue;
                    }
                }

                // <id>.addEventListener("<event>", …)
                if (cursor.CheckAhead(1, TokenType.Dot) && cursor.CheckName(2, "addEventListener"))
                {
                    ReadAddEventListener(cursor, form, token);
                    continue;
                }

                // <recv>.appendChild(<id>)  — the receiver may be `doc.body`, so scan the line.
                if (LineMentions(cursor, "appendChild"))
                {
                    MarkParented(cursor, form);
                    continue;
                }

                // <id>.<prop> = <raw>
                if (cursor.CheckAhead(1, TokenType.Dot) && cursor.CheckAhead(2, TokenType.Identifier) &&
                    cursor.CheckAhead(3, TokenType.Assignment) && form[token.Lexeme] != null)
                {
                    form[token.Lexeme]!.Properties[cursor.Peek(2)!.Lexeme] = RawRightHandSide(cursor, index, 4);
                    cursor.SkipToNextLine();
                    continue;
                }
            }

            cursor.Advance();
        }

        return form;
    }

    /// <summary>True when this dialect is the right one for a file — used to pick between dialects.</summary>
    public static bool Looks(string source) =>
        source.Contains("createElement", StringComparison.OrdinalIgnoreCase) ||
        source.Contains("::document", StringComparison.Ordinal);

    private static void AddControl(RecognizedForm form, string id, string tag, int line, string? member)
    {
        var control = form[id] ?? new RecognizedControl { Id = id };
        if (form[id] == null)
        {
            form.Controls.Add(control);
        }

        control.TypeName = tag;
        // Null when the catalog has no row for the tag, or when the tag is ambiguous (select, input).
        // Reported rather than guessed — see FormControlCatalog.FindByHtmlTag.
        control.CatalogKind = FormControlCatalog.FindByHtmlTag(tag)?.Kind;
        control.ConstructionLine = line;
        form.BuiltIn ??= member;
    }

    /// <summary>
    /// The tag string from a <c>…createElement("tag")</c> anywhere on the current line, or null.
    /// Scans the line rather than matching a fixed offset because the receiver varies —
    /// <c>doc.createElement</c>, <c>document.createElement</c>, <c>::document.createElement</c>.
    /// </summary>
    private static string? TagFromCreateElement(TokenCursor cursor)
    {
        for (var i = 0; ; i++)
        {
            var token = cursor.Peek(i);
            if (token == null || token.Type == TokenType.Newline)
            {
                return null;
            }

            if (token.Type != TokenType.Identifier ||
                !string.Equals(token.Lexeme, "createElement", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var literal = cursor.Peek(i + 2);       // ( "tag"
            return cursor.Peek(i + 1)?.Type == TokenType.LeftParen &&
                   literal?.Type == TokenType.StringLiteral
                ? TextOf(literal)
                : null;
        }
    }

    private static bool LineMentions(TokenCursor cursor, string name)
    {
        for (var i = 0; ; i++)
        {
            var token = cursor.Peek(i);
            if (token == null || token.Type == TokenType.Newline)
            {
                return false;
            }

            if (token.Type == TokenType.Identifier &&
                string.Equals(token.Lexeme, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
    }

    private static void ReadAddEventListener(TokenCursor cursor, RecognizedForm form, Token token)
    {
        // <id> . addEventListener ( "<event>" , <handler…>
        var control = form[token.Lexeme];
        var literal = cursor.Peek(4);

        if (control != null && cursor.CheckAhead(3, TokenType.LeftParen) &&
            literal?.Type == TokenType.StringLiteral)
        {
            var isAddressOf = cursor.CheckAhead(6, TokenType.AddressOf);
            var handler = isAddressOf && cursor.CheckAhead(7, TokenType.Identifier)
                ? cursor.Peek(7)!.Lexeme
                : null;

            control.Handlers.Add(new RecognizedHandler(TextOf(literal), handler, !isAddressOf, token.Line));
        }

        cursor.SkipToNextLine();
    }

    private static void MarkParented(TokenCursor cursor, RecognizedForm form)
    {
        // The argument is the last identifier before the closing paren; scan the line for any
        // identifier that names a control we already know.
        for (var i = 0; ; i++)
        {
            var token = cursor.Peek(i);
            if (token == null || token.Type == TokenType.Newline)
            {
                break;
            }

            if (token.Type == TokenType.Identifier && form[token.Lexeme] is { } control)
            {
                control.IsParented = true;
            }
        }

        cursor.SkipToNextLine();
    }

    private static void HandleWith(TokenCursor cursor, RecognizedForm form, Token token)
    {
        var target = cursor.Peek()?.Type == TokenType.Identifier ? cursor.Peek()!.Lexeme : null;
        if (target != null && form[target] != null)
        {
            form.Refusals.Add(new RecognitionRefusal(
                "BL8002",
                $"'With {target}' cannot be imported: a '.Property = value' inside a With block is " +
                "silently discarded by the compiler, so the designer would show properties the " +
                "running program never sets. Assign through the element name instead.",
                token.Line, token.Column));
        }

        cursor.SkipToNextLine();
    }

    /// <summary>A string literal's text without its quotes.</summary>
    private static string TextOf(Token literal) =>
        literal.Value?.ToString() ?? literal.Lexeme.Trim('"');

    private static string RawRightHandSide(TokenCursor cursor, SourceIndex index, int ahead)
    {
        var first = cursor.Peek(ahead);
        if (first == null || first.Type == TokenType.Newline)
        {
            return "";
        }

        var start = index.OffsetOf(first.Line, first.Column);
        return index.Slice(start, index.EndOfLine(first.Line)).Trim();
    }
}
