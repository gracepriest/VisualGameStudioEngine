using BasicLang.Compiler;

namespace BasicLang.Forms.Recognizer;

/// <summary>
/// A forward cursor over a token list, shared by both dialects.
///
/// <para>Deliberately not a parser. It never builds a tree, never reports a syntax error and never
/// refuses to move on — the recognizer has to read files that do not fully parse (D12), because the
/// designer is asked to render a form while the user is still typing in the code view. Everything
/// here either matches a shape or declines to, and declining is always safe.</para>
/// </summary>
internal sealed class TokenCursor
{
    private readonly List<Token> _tokens;

    public TokenCursor(List<Token> tokens) => _tokens = tokens;

    public int Position { get; private set; }

    public bool AtEnd => Position >= _tokens.Count;

    public Token? Current => Position < _tokens.Count ? _tokens[Position] : null;

    public Token? Peek(int ahead = 1) =>
        Position + ahead < _tokens.Count ? _tokens[Position + ahead] : null;

    public void Advance() => Position++;

    public bool Check(TokenType type) => Current?.Type == type;

    public bool CheckAhead(int ahead, TokenType type) => Peek(ahead)?.Type == type;

    /// <summary>Consumes and returns the current token when it is of <paramref name="type"/>.</summary>
    public Token? Match(TokenType type)
    {
        if (!Check(type))
        {
            return null;
        }

        var token = Current;
        Advance();
        return token;
    }

    /// <summary>
    /// True when the current token is an identifier with this exact text. Identifier comparison is
    /// ORDINAL: BasicLang is case-insensitive for keywords, but two identifiers differing only in
    /// case are the user's business and conflating them would let the designer bind to the wrong
    /// control.
    /// </summary>
    public bool CheckIdentifier(string lexeme) =>
        Current?.Type == TokenType.Identifier &&
        string.Equals(Current.Lexeme, lexeme, StringComparison.Ordinal);

    /// <summary>Method-name comparison, which IS case-insensitive — these are library names.</summary>
    public bool CheckName(int ahead, string name) =>
        Peek(ahead)?.Type == TokenType.Identifier &&
        string.Equals(Peek(ahead)!.Lexeme, name, StringComparison.OrdinalIgnoreCase);

    /// <summary>Moves past the next newline, i.e. to the start of the following statement.</summary>
    public void SkipToNextLine()
    {
        while (!AtEnd && Current!.Type != TokenType.Newline)
        {
            Advance();
        }

        if (!AtEnd)
        {
            Advance();
        }
    }

    /// <summary>
    /// The first comment token at or after <paramref name="from"/> on the current line, or null.
    /// Used to stop a raw source slice before a trailing comment.
    /// </summary>
    public Token? FirstCommentOnLine(int from)
    {
        for (var i = from; ; i++)
        {
            var token = Peek(i);
            if (token == null || token.Type == TokenType.Newline)
            {
                return null;
            }

            if (token.Type == TokenType.Comment)
            {
                return token;
            }
        }
    }

    /// <summary>
    /// Lexes <paramref name="source"/>, returning null when the lexer refused it.
    ///
    /// <para>⛔ <c>Lexer.Tokenize()</c> THROWS — six reachable sites: an unterminated string, an
    /// unterminated interpolated string, a number too large for Long, a malformed
    /// <c>&amp;H</c>/<c>&amp;O</c>/<c>&amp;B</c> prefix, <c>&amp;H</c> with no digits, and an
    /// unterminated <c>cpp{ }</c> block. The first of those is the single most common state a file
    /// is in WHILE BEING EDITED (<c>btnClick.Text = "</c>), which is exactly when the designer is
    /// asked to render it. Letting that escape would turn a half-typed line into a crash on the one
    /// path whose whole premise is tolerating incomplete source.</para>
    /// </summary>
    public static TokenCursor? TryLex(string source, out string? error)
    {
        try
        {
            error = null;
            return new TokenCursor(new Lexer(source).Tokenize());
        }
        catch (LexerException ex)
        {
            error = ex.Message;
            return null;
        }
    }
}
