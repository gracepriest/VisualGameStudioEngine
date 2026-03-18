using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace VisualGameStudio.Editor.Highlighting;

/// <summary>
/// A decoded semantic token with absolute line/column positions.
/// </summary>
public readonly record struct DecodedSemanticToken(
    int Line,        // 0-based
    int StartChar,   // 0-based
    int Length,
    int TokenType,
    int TokenModifiers);

/// <summary>
/// DocumentColorizingTransformer that applies semantic token colors from the LSP server,
/// overriding the lexer-based syntax highlighting with more accurate semantic information.
/// </summary>
public class SemanticTokenHighlighter : DocumentColorizingTransformer
{
    private DecodedSemanticToken[] _tokens = Array.Empty<DecodedSemanticToken>();
    private int _documentLineCount;

    // LSP token type indices (must match the legend order in SemanticTokensHandler.cs):
    // 0=Namespace, 1=Type, 2=Class, 3=Enum, 4=Interface, 5=Struct,
    // 6=TypeParameter, 7=Parameter, 8=Variable, 9=Property, 10=EnumMember,
    // 11=Function, 12=Method, 13=Keyword, 14=Modifier, 15=Comment,
    // 16=String, 17=Number, 18=Operator

    // LSP token modifier bit flags:
    // 0=Declaration, 1=Definition, 2=Readonly, 3=Static, 4=Deprecated,
    // 5=Abstract, 6=Async, 7=Modification, 8=Documentation, 9=DefaultLibrary

    private static readonly IBrush CyanBrush = new SolidColorBrush(Color.FromRgb(78, 201, 176));       // namespace
    private static readonly IBrush GreenBrush = new SolidColorBrush(Color.FromRgb(78, 201, 176));      // class/struct/enum (teal-green)
    private static readonly IBrush YellowBrush = new SolidColorBrush(Color.FromRgb(220, 220, 170));    // function/method
    private static readonly IBrush LightBlueBrush = new SolidColorBrush(Color.FromRgb(156, 220, 254)); // variable
    private static readonly IBrush LightCyanBrush = new SolidColorBrush(Color.FromRgb(156, 220, 254)); // property
    private static readonly IBrush BlueBrush = new SolidColorBrush(Color.FromRgb(86, 156, 214));       // keyword
    private static readonly IBrush OrangeBrush = new SolidColorBrush(Color.FromRgb(206, 145, 120));    // string
    private static readonly IBrush LightGreenBrush = new SolidColorBrush(Color.FromRgb(181, 206, 168));// number
    private static readonly IBrush GrayBrush = new SolidColorBrush(Color.FromRgb(106, 153, 85));       // comment
    private static readonly IBrush InterfaceBrush = new SolidColorBrush(Color.FromRgb(184, 215, 163)); // interface
    private static readonly IBrush EnumMemberBrush = new SolidColorBrush(Color.FromRgb(79, 193, 255)); // enum member
    private static readonly IBrush ModifierBrush = new SolidColorBrush(Color.FromRgb(86, 156, 214));   // modifier (same as keyword)
    private static readonly IBrush OperatorBrush = new SolidColorBrush(Color.FromRgb(212, 212, 212));  // operator
    private static readonly IBrush TypeParamBrush = new SolidColorBrush(Color.FromRgb(184, 215, 163)); // type parameter

    /// <summary>
    /// Updates the semantic tokens. Call from the UI thread.
    /// </summary>
    /// <param name="encodedData">Raw LSP semantic token data: [deltaLine, deltaStartChar, length, tokenType, tokenModifiers] * N</param>
    /// <param name="lineCount">Current document line count (for validation).</param>
    public void Update(int[] encodedData, int lineCount)
    {
        _documentLineCount = lineCount;

        if (encodedData == null || encodedData.Length == 0 || encodedData.Length % 5 != 0)
        {
            _tokens = Array.Empty<DecodedSemanticToken>();
            return;
        }

        var count = encodedData.Length / 5;
        var decoded = new DecodedSemanticToken[count];

        int line = 0;
        int startChar = 0;

        for (int i = 0; i < count; i++)
        {
            int offset = i * 5;
            int deltaLine = encodedData[offset];
            int deltaStartChar = encodedData[offset + 1];
            int length = encodedData[offset + 2];
            int tokenType = encodedData[offset + 3];
            int tokenModifiers = encodedData[offset + 4];

            if (deltaLine > 0)
            {
                line += deltaLine;
                startChar = deltaStartChar;
            }
            else
            {
                startChar += deltaStartChar;
            }

            decoded[i] = new DecodedSemanticToken(line, startChar, length, tokenType, tokenModifiers);
        }

        _tokens = decoded;
    }

    /// <summary>
    /// Clears all semantic tokens.
    /// </summary>
    public void Clear()
    {
        _tokens = Array.Empty<DecodedSemanticToken>();
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (_tokens.Length == 0) return;

        // DocumentLine.LineNumber is 1-based; LSP tokens are 0-based
        int lineIndex = line.LineNumber - 1;

        // Binary search for first token on this line
        int lo = 0, hi = _tokens.Length - 1;
        int firstOnLine = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (_tokens[mid].Line < lineIndex)
                lo = mid + 1;
            else if (_tokens[mid].Line > lineIndex)
                hi = mid - 1;
            else
            {
                firstOnLine = mid;
                hi = mid - 1; // keep searching left for the very first
            }
        }

        if (firstOnLine < 0) return;

        int docLength = CurrentContext.Document.TextLength;

        for (int i = firstOnLine; i < _tokens.Length; i++)
        {
            ref readonly var token = ref _tokens[i];
            if (token.Line != lineIndex) break;

            int startOffset = line.Offset + token.StartChar;
            int endOffset = startOffset + token.Length;

            // Safety bounds
            if (startOffset < line.Offset || endOffset > line.EndOffset || startOffset >= docLength)
                continue;
            if (endOffset > docLength)
                endOffset = docLength;
            if (startOffset >= endOffset)
                continue;

            var brush = GetBrushForTokenType(token.TokenType);
            if (brush == null) continue;

            bool isItalic = token.TokenType == 7; // parameter
            bool isDeprecated = (token.TokenModifiers & (1 << 4)) != 0; // deprecated bit

            ChangeLinePart(startOffset, endOffset, element =>
            {
                element.TextRunProperties.SetForegroundBrush(brush);
                if (isItalic)
                {
                    element.TextRunProperties.SetTypeface(new Typeface(
                        element.TextRunProperties.Typeface.FontFamily,
                        FontStyle.Italic,
                        element.TextRunProperties.Typeface.Weight));
                }
                if (isDeprecated)
                {
                    element.TextRunProperties.SetTextDecorations(TextDecorations.Strikethrough);
                }
            });
        }
    }

    private static IBrush? GetBrushForTokenType(int tokenType)
    {
        return tokenType switch
        {
            0 => CyanBrush,          // namespace
            1 => GreenBrush,         // type
            2 => GreenBrush,         // class
            3 => GreenBrush,         // enum
            4 => InterfaceBrush,     // interface
            5 => GreenBrush,         // struct
            6 => TypeParamBrush,     // typeParameter
            7 => LightBlueBrush,     // parameter (also italic via ColorizeLine)
            8 => LightBlueBrush,     // variable
            9 => LightCyanBrush,     // property
            10 => EnumMemberBrush,   // enumMember
            11 => YellowBrush,       // function
            12 => YellowBrush,       // method
            13 => BlueBrush,         // keyword
            14 => ModifierBrush,     // modifier
            15 => GrayBrush,         // comment
            16 => OrangeBrush,       // string
            17 => LightGreenBrush,   // number
            18 => OperatorBrush,     // operator
            _ => null
        };
    }
}
