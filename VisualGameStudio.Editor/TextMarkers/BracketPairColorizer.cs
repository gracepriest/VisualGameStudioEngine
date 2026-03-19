using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace VisualGameStudio.Editor.TextMarkers;

/// <summary>
/// Colors nested bracket pairs by depth, cycling through Gold, Violet, and Cyan.
/// Scans the entire document to track nesting depth across lines, skipping
/// brackets inside string literals and single-line comments.
/// </summary>
public class BracketPairColorizer : DocumentColorizingTransformer
{
    private static readonly IBrush[] DepthBrushes =
    {
        new SolidColorBrush(Color.Parse("#FFD700")), // Gold   — depth 0
        new SolidColorBrush(Color.Parse("#DA70D6")), // Violet — depth 1
        new SolidColorBrush(Color.Parse("#00BFFF")), // Cyan   — depth 2
    };

    private static readonly HashSet<char> OpenBrackets  = new() { '(', '[', '{' };
    private static readonly HashSet<char> CloseBrackets = new() { ')', ']', '}' };

    /// <summary>
    /// Maps each bracket offset to the nesting depth at which it should be colored.
    /// Rebuilt whenever the document text changes.
    /// </summary>
    private Dictionary<int, int> _bracketDepths = new();
    private int _lastDocVersion = -1;

    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Call this after the document changes (e.g., on TextChanged) to rebuild the
    /// bracket depth map. The map is only rebuilt when the document version changes.
    /// </summary>
    public void Invalidate(TextDocument? document)
    {
        if (document == null) return;

        var version = document.Version?.GetHashCode() ?? -1;
        if (version == _lastDocVersion) return;
        _lastDocVersion = version;

        _bracketDepths = BuildBracketDepthMap(document);
    }

    private static Dictionary<int, int> BuildBracketDepthMap(TextDocument document)
    {
        var map = new Dictionary<int, int>();
        var text = document.Text;
        int depth = 0;
        bool inString = false;
        bool inComment = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            // End of line resets comment state
            if (c == '\n')
            {
                inComment = false;
                continue;
            }

            if (inComment) continue;

            // Toggle string state on double-quote (simple — doesn't handle escaped quotes)
            if (c == '"' && !inComment)
            {
                inString = !inString;
                continue;
            }

            if (inString) continue;

            // Single-line comment
            if (c == '\'')
            {
                inComment = true;
                continue;
            }

            // Rem comment (case-insensitive, must be preceded by whitespace or be at SOL)
            if ((c == 'R' || c == 'r') && i + 2 < text.Length
                && (text[i + 1] == 'e' || text[i + 1] == 'E')
                && (text[i + 2] == 'm' || text[i + 2] == 'M')
                && (i == 0 || char.IsWhiteSpace(text[i - 1]))
                && (i + 3 >= text.Length || char.IsWhiteSpace(text[i + 3])))
            {
                inComment = true;
                continue;
            }

            if (OpenBrackets.Contains(c))
            {
                map[i] = depth;
                depth++;
            }
            else if (CloseBrackets.Contains(c))
            {
                depth = Math.Max(0, depth - 1);
                map[i] = depth;
            }
        }

        return map;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (!IsEnabled || _bracketDepths.Count == 0) return;

        int lineStart = line.Offset;
        int lineEnd = line.EndOffset;

        for (int offset = lineStart; offset < lineEnd; offset++)
        {
            if (_bracketDepths.TryGetValue(offset, out int depth))
            {
                int colorIndex = depth % DepthBrushes.Length;
                var brush = DepthBrushes[colorIndex];
                ChangeLinePart(offset, offset + 1, element =>
                {
                    element.TextRunProperties.SetForegroundBrush(brush);
                });
            }
        }
    }
}
