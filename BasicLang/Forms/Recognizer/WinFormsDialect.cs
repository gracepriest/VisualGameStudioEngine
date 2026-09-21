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
        var form = new RecognizedForm();

        var cursor = TokenCursor.TryLex(source, out var lexError);
        if (cursor == null)
        {
            // The lexer refused the text. Return an empty form that SAYS so rather than throwing:
            // a half-typed string literal is the commonest mid-edit state, and the designer is asked
            // to render exactly then.
            form.UnreadableReason = lexError;
            return form;
        }

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
                        "BL8002",
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

            // --- declaration: [Private|Public|Protected|Dim] id As Type ---
            if (IsDeclarationKeyword(token.Type) &&
                cursor.CheckAhead(1, TokenType.Identifier) &&
                cursor.CheckAhead(2, TokenType.As))
            {
                var id = cursor.Peek(1)!.Lexeme;

                // `As New Label()` — the combined declare-and-construct form. It is idiomatic and
                // this repo's own web template uses it (`Dim counter As New ClickCounter()`), yet it
                // carries no Assignment token, so the `<id> = New <Type>(` shape below never sees
                // it. A form written this way used to open completely empty.
                if (cursor.CheckAhead(3, TokenType.New) && cursor.CheckAhead(4, TokenType.Identifier))
                {
                    var newType = cursor.Peek(4)!.Lexeme;
                    declaredTypes[id] = newType;
                    declaredLines[id] = token.Line;

                    if (FormControlCatalog.Find(newType) != null)
                    {
                        var declared = form[id] ?? Add(form, id);
                        declared.TypeName = newType;
                        declared.CatalogKind = FormControlCatalog.Find(newType)!.Kind;
                        declared.ConstructionLine = token.Line;
                        declared.DeclarationLine = token.Line;
                        form.BuiltIn ??= currentMember;
                    }

                    cursor.SkipToNextLine();
                    continue;
                }

                if (cursor.CheckAhead(3, TokenType.Identifier))
                {
                    declaredTypes[id] = cursor.Peek(3)!.Lexeme;
                    declaredLines[id] = token.Line;
                    cursor.SkipToNextLine();
                    continue;
                }
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
                // <receiver>.Controls.Add(<id>) — receiver is `Me` OR a container control.
                // ⚠ Matching only `Me` was a real defect: the catalog ships Panel and GroupBox as
                // containers, and `pnlBox.Controls.Add(lblInner)` did not match, so every child of
                // every container collected a spurious "created but never added to the form"
                // warning. That would have been design --check's most common false positive.
                if (cursor.CheckName(2, "Controls") && cursor.CheckName(4, "Add"))
                {
                    MarkParented(cursor, form);
                    continue;
                }

                // A STRIP's own host verbs (Task 24, spec §1) — `menuStrip1.Items.Add(mnuFile)` and
                // `mnuFile.DropDownItems.Add(mnuOpen)`. Identical token layout to `Controls.Add`, so
                // `Peek(6)` is the child id. Without these arms, importing a hand-written menu through
                // `design --check` reported every item BL8006 "created but never added to the form".
                //
                // ⚠ A ComboBox's own `cmb.Items.Add("Apple")` reaches the Items arm too, with a STRING
                // LITERAL where a child id would be. MarkParented requires an Identifier token at that
                // position and ignores anything else — the same net effect as the fallthrough this arm
                // replaces — so do NOT "fix" this by excluding ComboBox by name: the token-type guard
                // already handles it, and a name list would be a second catalog to keep in step.
                if ((cursor.CheckName(2, "Items") || cursor.CheckName(2, "DropDownItems")) &&
                    cursor.CheckName(4, "Add"))
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

                    // Only a CATALOG type is a control. `Me.Size = New Size(...)`, `New Point(...)`
                    // and `New Font(...)` all match this shape and none of them is a control.
                    // ⚠ This used to also accept any identifier the class had declared, whatever its
                    // type — so `Private db As Connection` + `db = New Connection()` became a
                    // "control" with no catalog row and earned a BL8004 warning on an ordinary
                    // field. It also disagreed with the declared-but-unconstructed pass below, which
                    // was already catalog-only. One rule now.
                    if (FormControlCatalog.Find(typeName) != null)
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
                "BL8003",
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
        // <receiver> . Controls|Items|DropDownItems . Add ( <id> ) — one token layout, three verbs.
        // ⛔ The Identifier guard is load-bearing, not defensive: `cmb.Items.Add("Apple")` arrives here
        // with a string literal at this position and must parent nothing.
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
    private static string RawRightHandSide(TokenCursor cursor, SourceIndex index, int ahead) =>
        RawSlice.RightHandSide(cursor, index, ahead);
}
