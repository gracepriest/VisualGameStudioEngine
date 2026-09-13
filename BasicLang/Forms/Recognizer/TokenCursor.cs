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

    /// <summary>The last token before the next newline, for measuring a statement's extent.</summary>
    public Token? LastTokenOnLine()
    {
        Token? last = null;
        for (var i = Position; i < _tokens.Count && _tokens[i].Type != TokenType.Newline; i++)
        {
            last = _tokens[i];
        }

        return last;
    }
}
