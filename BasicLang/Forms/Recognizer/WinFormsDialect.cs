using BasicLang.Compiler;

namespace BasicLang.Forms.Recognizer;

/// <summary>
/// Recovers a WinForms form from BasicLang source (D12: the recognizer is an <b>importer</b>, not
/// the format).
///
/// <para>⛔ <b>It reads constructor bodies and <c>InitializeComponent</c> alike, and that is the
/// single most important thing about it.</b> Neither shipped IDE template has an
/// <c>InitializeComponent</c>: the <c>winforms-app</c> template builds every control inside
/// <c>Public Sub New()</c>, and the VSIX template uses <c>InitializeComponent</c>. A reader written
/// against the VS convention alone recovers <b>zero controls</b> from the template the IDE itself
/// generates — and recovers them from nothing, so a test suite that only feeds it the VSIX fixture
/// stays green while the feature does not work. The shape is what matters, not the member name.</para>
///
/// <para>The shape, in the order the templates write it:
/// <code>
/// Private lblMessage As Label                    ' declare  (class level, optional)
/// lblMessage = New Label()                       ' construct
/// lblMessage.Text = "Hello"                      ' set properties
/// AddHandler btnClick.Click, AddressOf OnClick   ' wire
/// Me.Controls.Add(lblMessage)                    ' parent
/// </code>
/// Every step is optional except construction: a control assigned but never parented is still a
/// control the user declared, and reporting it is how the designer can show that it is orphaned.</para>
/// </summary>
public static class WinFormsDialect
{
    public static RecognizedForm Read(string source)
    {
        var index = new SourceIndex(source);
        var cursor = new TokenCursor(new Lexer(source).Tokenize());
        var form = new RecognizedForm();

        // Field declarations seen at class level: id -> declared type.
        var declaredTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        var declaredLines = new Dictionary<string, int>(StringComparer.Ordinal);
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

                case TokenType.Inherits when cursor.Peek()?.Type == TokenType.Identifier:
                    form.BaseType ??= cursor.Peek()!.Lexeme;
                    cursor.Advance();
                    continue;

                case TokenType.Sub when cursor.Peek()?.Type == TokenType.Identifier:
                    currentMember = cursor.Peek()!.Lexeme;
                    cursor.Advance();
                    continue;

                // `Sub New()` — New is its own token, not an identifier.
                case TokenType.Sub when cursor.Peek()?.Type == TokenType.New:
                    currentMember = "New";
                    cursor.Advance();
                    continue;

                case TokenType.EndSub:
                    currentMember = null;
                    cursor.Advance();
                    continue;

                // ⛔ D9 Refused, not ignored. `Handles` is lexed but never parsed — Parser.cs has
                // zero TokenType.Handles — so a file using it does not build at all. Importing it
                // would produce a designer view of a program that cannot run.
                case TokenType.Handles:
                    form.Refusals.Add(new RecognitionRefusal(
                        "BL8001",
                        "A 'Handles' clause is not supported by the BasicLang parser, so this file " +
                        "cannot build. Wire the event with 'AddHandler <control>.<Event>, AddressOf " +
                        "<handler>' instead.",
                        token.Line, token.Column));
                    cursor.SkipToNextLine();
                    continue;

                case TokenType.With:
                    HandleWith(cursor, form, declaredTypes, token);
                    continue;
            }

            // --- class-level field declaration: [Private|Public|Protected|Dim] id As Type ---
            if (currentMember == null && IsDeclarationKeyword(token.Type) &&
                cursor.CheckAhead(1, TokenType.Identifier) &&
                cursor.CheckAhead(2, TokenType.As) &&
                cursor.CheckAhead(3, TokenType.Identifier))
            {
                var id = cursor.Peek(1)!.Lexeme;
                declaredTypes[id] = cursor.Peek(3)!.Lexeme;
                declaredLines[id] = token.Line;
                cursor.SkipToNextLine();
                continue;
            }

            // --- everything below is a statement inside some member ---
            if (currentMember == null)
            {
                cursor.Advance();
                continue;
            }

            // AddHandler <id>.<Event>, AddressOf <handler>
            if (token.Type == TokenType.AddHandler)
            {
                ReadAddHandler(cursor, form, token);
                continue;
            }

            if (token.Type == TokenType.Identifier || token.Type == TokenType.Me)
            {
                // Me.Controls.Add(<id>)
                if (token.Type == TokenType.Me && cursor.CheckName(2, "Controls") && cursor.CheckName(4, "Add"))
                {
                    MarkParented(cursor, form);
                    continue;
                }

                // Me.<Prop> = <raw>
                if (token.Type == TokenType.Me && cursor.CheckAhead(1, TokenType.Dot) &&
                    cursor.CheckAhead(2, TokenType.Identifier) && cursor.CheckAhead(3, TokenType.Assignment))
                {
                    form.FormProperties[cursor.Peek(2)!.Lexeme] = RawRightHandSide(cursor, index, 4);
                    cursor.SkipToNextLine();
                    continue;
                }

                // <id> = New <Type>(
                if (cursor.CheckAhead(1, TokenType.Assignment) && cursor.CheckAhead(2, TokenType.New) &&
                    cursor.CheckAhead(3, TokenType.Identifier))
                {
                    var id = token.Lexeme;
                    var typeName = cursor.Peek(3)!.Lexeme;

                    // Only a catalog type — or an identifier the class declared as a field — is a
                    // control. `Me.Size = New Size(...)`, `New Point(...)` and `New Font(...)` all
                    // match this shape and none of them is a control.
                    if (FormControlCatalog.Find(typeName) != null || declaredTypes.ContainsKey(id))
                    {
                        var control = form[id] ?? Add(form, id);
                        control.TypeName = typeName;
                        control.CatalogKind = FormControlCatalog.Find(typeName)?.Kind;
                        control.ConstructionLine = token.Line;
                        control.DeclarationLine = declaredLines.TryGetValue(id, out var line) ? line : 0;
                        form.BuiltIn ??= currentMember;
                    }

                    cursor.SkipToNextLine();
                    continue;
                }

                // <id>.<Prop> = <raw>
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

        // A field declared as a catalog type but never constructed is still the user's intent, and
        // a designer that dropped it would silently lose a control from the form.
        foreach (var (id, typeName) in declaredTypes)
        {
            if (form[id] != null || FormControlCatalog.Find(typeName) == null)
            {
                continue;
            }

            var control = Add(form, id);
            control.TypeName = typeName;
            control.CatalogKind = FormControlCatalog.Find(typeName)!.Kind;
            control.DeclarationLine = declaredLines[id];
        }

        return form;
    }

    /// <summary>True when this dialect is the right one for a file — used to pick between dialects.</summary>
    public static bool Looks(string source) =>
        source.Contains("Inherits Form", StringComparison.OrdinalIgnoreCase) ||
        source.Contains("System.Windows.Forms", StringComparison.OrdinalIgnoreCase);

    private static bool IsDeclarationKeyword(TokenType type) =>
        type is TokenType.Private or TokenType.Public or TokenType.Protected or TokenType.Dim;

    private static RecognizedControl Add(RecognizedForm form, string id)
    {
        var control = new RecognizedControl { Id = id };
        form.Controls.Add(control);
        return control;
    }

    /// <summary>
    /// ⛔ D9 Refused. A <c>With</c> block over a recognised control is refused because
    /// <c>.Prop = value</c> inside one is SILENTLY DROPPED: <c>IRBuilder</c>'s assignment dispatch
    /// has no arm for an implicit With member and no final else, so the statement compiles to
    /// nothing. Importing such a form would show the designer properties the running program never
    /// sets — the designer and the program would disagree, with nothing to indicate which is right.
    /// </summary>
    private static void HandleWith(
        TokenCursor cursor, RecognizedForm form, Dictionary<string, string> declaredTypes, Token token)
    {
        var target = cursor.Peek()?.Type == TokenType.Identifier ? cursor.Peek()!.Lexeme : null;
        var isControl = target != null &&
                        (form[target] != null ||
                         (declaredTypes.TryGetValue(target, out var type) && FormControlCatalog.Find(type) != null));

        if (isControl)
        {
            form.Refusals.Add(new RecognitionRefusal(
                "BL8002",
                $"'With {target}' cannot be imported: a '.Property = value' inside a With block is " +
                "silently discarded by the compiler, so the designer would show properties the " +
                "running program never sets. Assign through the control name instead.",
                token.Line, token.Column));
        }

        cursor.SkipToNextLine();
    }

    private static void ReadAddHandler(TokenCursor cursor, RecognizedForm form, Token token)
    {
        // AddHandler <id> . <Event> , AddressOf <handler>
        if (cursor.CheckAhead(1, TokenType.Identifier) && cursor.CheckAhead(2, TokenType.Dot) &&
            cursor.CheckAhead(3, TokenType.Identifier) && cursor.CheckAhead(4, TokenType.Comma))
        {
            var id = cursor.Peek(1)!.Lexeme;
            var eventName = cursor.Peek(3)!.Lexeme;
            var control = form[id];

            if (control != null)
            {
                var isAddressOf = cursor.CheckAhead(5, TokenType.AddressOf);
                var handler = isAddressOf && cursor.CheckAhead(6, TokenType.Identifier)
                    ? cursor.Peek(6)!.Lexeme
                    : null;

                control.Handlers.Add(new RecognizedHandler(eventName, handler, !isAddressOf, token.Line));
            }
        }

        cursor.SkipToNextLine();
    }

    private static void MarkParented(TokenCursor cursor, RecognizedForm form)
    {
        // Me . Controls . Add ( <id> )
        if (cursor.CheckAhead(6, TokenType.Identifier))
        {
            var control = form[cursor.Peek(6)!.Lexeme];
            if (control != null)
            {
                control.IsParented = true;
            }
        }

        cursor.SkipToNextLine();
    }

    /// <summary>
    /// The RAW source text of a statement's right-hand side, from the token <paramref name="ahead"/>
    /// positions along to the end of the line.
    ///
    /// <para>Raw text rather than a parsed value, because the recognizer is an importer: a value it
    /// cannot interpret (<c>New Font("Segoe UI", 12)</c>) must still survive into the document, and
    /// D9's tiering decides later what is editable. Parsing here would mean discarding whatever did
    /// not fit.</para>
    /// </summary>
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
