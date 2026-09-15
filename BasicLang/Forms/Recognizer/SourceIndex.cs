namespace BasicLang.Forms.Recognizer;

/// <summary>
/// Maps between the lexer's 1-based (line, column) positions and absolute character offsets in the
/// source text.
///
/// <para><b>Why this exists.</b> <c>BasicLang.Compiler.Token</c> carries <c>Line</c> and
/// <c>Column</c> but no offset, and the region writer needs offsets: it has to replace an exact
/// span of the user's file and leave every byte outside it alone. Recomputing a position by
/// counting newlines at each of a few hundred call sites is both slow and easy to get subtly wrong
/// around CR/LF. Building the line table once is neither.</para>
///
/// <para>⛔ <b>No lexer change.</b> Adding an offset to <c>Token</c> would touch a type on the hot
/// path of every compile, every LSP keystroke and every backend — to serve one consumer that can
/// derive the value itself.</para>
/// </summary>
public sealed class SourceIndex
{
    private readonly string _text;

    /// <summary>Absolute offset at which each 1-based line starts. Index 0 is unused.</summary>
    private readonly int[] _lineStarts;

    public SourceIndex(string text)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));

        var starts = new List<int> { 0, 0 };   // [0] unused, [1] = start of line 1
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n')
            {
                continue;
            }

            starts.Add(i + 1);
        }

        _lineStarts = starts.ToArray();
    }

    /// <summary>Number of lines. A file ending in a newline has an empty final line, as editors show it.</summary>
    public int LineCount => _lineStarts.Length - 1;

    public int Length => _text.Length;

    /// <summary>
    /// Absolute offset of a 1-based (line, column). Out-of-range positions clamp rather than throw:
    /// this reads sources that may not fully parse, and refusing to locate a token in a file that is
    /// mid-edit would make the recognizer useless exactly when the designer is live.
    /// </summary>
    public int OffsetOf(int line, int column)
    {
        if (line < 1)
        {
            return 0;
        }

        if (line > LineCount)
        {
            return _text.Length;
        }

        var lineStart = _lineStarts[line];
        var lineEnd = EndOfLine(line);
        var offset = lineStart + Math.Max(0, column - 1);
        return Math.Min(offset, lineEnd);
    }

    /// <summary>The 1-based (line, column) of an absolute offset.</summary>
    public (int Line, int Column) PositionOf(int offset)
    {
        offset = Math.Clamp(offset, 0, _text.Length);

        // Binary search for the last line whose start is <= offset.
        var low = 1;
        var high = LineCount;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (_lineStarts[mid] <= offset)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return (low, offset - _lineStarts[low] + 1);
    }

    /// <summary>
    /// Offset just past the last character of a line, NOT counting its line terminator. A CRLF file
    /// therefore stops before the CR — a span that swallowed the CR but not the LF would leave a
    /// lone LF behind and silently change the file's line endings.
    /// </summary>
    public int EndOfLine(int line)
    {
        if (line < 1)
        {
            return 0;
        }

        if (line >= LineCount)
        {
            return TrimTerminator(_text.Length);
        }

        // The next line starts just after this line's '\n'.
        return TrimTerminator(_lineStarts[line + 1] - 1);
    }

    private int TrimTerminator(int end)
    {
        if (end > 0 && end <= _text.Length && end - 1 < _text.Length && _text[end - 1] == '\r')
        {
            return end - 1;
        }

        return end;
    }

    /// <summary>The source text between two offsets. Clamped, never throws.</summary>
    public string Slice(int start, int end)
    {
        start = Math.Clamp(start, 0, _text.Length);
        end = Math.Clamp(end, start, _text.Length);
        return _text.Substring(start, end - start);
    }

    /// <summary>One line's text, without its terminator.</summary>
    public string LineText(int line) => Slice(OffsetOf(line, 1), EndOfLine(line));
}
