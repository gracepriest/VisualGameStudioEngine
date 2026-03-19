using System.Text.RegularExpressions;
using Avalonia.Media;

namespace VisualGameStudio.Shell.Services;

/// <summary>
/// Parses ANSI escape sequences and produces styled text segments.
/// Supports SGR (Select Graphic Rendition) codes for foreground/background colors and bold.
/// Maintains state across calls so colors carry over chunk boundaries.
/// </summary>
public class AnsiParser
{
    /// <summary>
    /// A segment of text with associated style.
    /// </summary>
    public readonly record struct AnsiSegment(string Text, IBrush? Foreground, IBrush? Background, bool Bold);

    // Matches CSI sequences: ESC[ followed by semicolon-separated numbers, ending with 'm'
    private static readonly Regex AnsiPattern = new(@"\x1b\[([0-9;]*)m", RegexOptions.Compiled);

    // Standard ANSI colors (dark variants) -- indexed 0-7
    private static readonly IBrush[] DarkColors =
    {
        new SolidColorBrush(Color.Parse("#000000")), // 0 Black
        new SolidColorBrush(Color.Parse("#CD3131")), // 1 Red
        new SolidColorBrush(Color.Parse("#0DBC79")), // 2 Green
        new SolidColorBrush(Color.Parse("#E5E510")), // 3 Yellow
        new SolidColorBrush(Color.Parse("#2472C8")), // 4 Blue
        new SolidColorBrush(Color.Parse("#BC3FBC")), // 5 Magenta
        new SolidColorBrush(Color.Parse("#11A8CD")), // 6 Cyan
        new SolidColorBrush(Color.Parse("#E5E5E5")), // 7 White
    };

    // Bright ANSI colors -- indexed 0-7 (codes 90-97 / 100-107)
    private static readonly IBrush[] BrightColors =
    {
        new SolidColorBrush(Color.Parse("#666666")), // 0 Bright Black (Gray)
        new SolidColorBrush(Color.Parse("#F14C4C")), // 1 Bright Red
        new SolidColorBrush(Color.Parse("#23D18B")), // 2 Bright Green
        new SolidColorBrush(Color.Parse("#F5F543")), // 3 Bright Yellow
        new SolidColorBrush(Color.Parse("#3B8EEA")), // 4 Bright Blue
        new SolidColorBrush(Color.Parse("#D670D6")), // 5 Bright Magenta
        new SolidColorBrush(Color.Parse("#29B8DB")), // 6 Bright Cyan
        new SolidColorBrush(Color.Parse("#FFFFFF")), // 7 Bright White
    };

    // Current state (persists across Parse calls)
    private IBrush? _foreground;
    private IBrush? _background;
    private bool _bold;

    /// <summary>
    /// Resets the parser state (foreground, background, bold) to defaults.
    /// </summary>
    public void Reset()
    {
        _foreground = null;
        _background = null;
        _bold = false;
    }

    /// <summary>
    /// Parses a string containing ANSI escape codes into styled segments.
    /// State is maintained across calls, so a color set in one chunk applies to subsequent chunks.
    /// </summary>
    public List<AnsiSegment> Parse(string input)
    {
        var segments = new List<AnsiSegment>();
        if (string.IsNullOrEmpty(input))
            return segments;

        int lastIndex = 0;
        var matches = AnsiPattern.Matches(input);

        foreach (Match match in matches)
        {
            // Add text before this escape sequence using current state
            if (match.Index > lastIndex)
            {
                var text = input.Substring(lastIndex, match.Index - lastIndex);
                if (text.Length > 0)
                    segments.Add(new AnsiSegment(text, _foreground, _background, _bold));
            }

            // Parse SGR parameters and update state
            var paramStr = match.Groups[1].Value;
            ApplyCodes(paramStr);

            lastIndex = match.Index + match.Length;
        }

        // Add remaining text after last escape sequence
        if (lastIndex < input.Length)
        {
            var text = input.Substring(lastIndex);
            if (text.Length > 0)
                segments.Add(new AnsiSegment(text, _foreground, _background, _bold));
        }

        return segments;
    }

    /// <summary>
    /// Strips all ANSI escape sequences from a string, returning plain text.
    /// </summary>
    public static string StripAnsi(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;
        return AnsiPattern.Replace(input, "");
    }

    private void ApplyCodes(string paramStr)
    {
        var codes = ParseCodeValues(paramStr);

        foreach (var code in codes)
        {
            switch (code)
            {
                case 0: // Reset all
                    _foreground = null;
                    _background = null;
                    _bold = false;
                    break;

                case 1: // Bold / bright
                    _bold = true;
                    // If a dark foreground is already set, upgrade to bright variant
                    if (_foreground != null)
                    {
                        for (int i = 0; i < DarkColors.Length; i++)
                        {
                            if (ReferenceEquals(_foreground, DarkColors[i]))
                            {
                                _foreground = BrightColors[i];
                                break;
                            }
                        }
                    }
                    break;

                case 22: // Normal intensity (not bold)
                    _bold = false;
                    break;

                case 39: // Default foreground
                    _foreground = null;
                    break;

                case 49: // Default background
                    _background = null;
                    break;

                // Foreground colors 30-37
                case >= 30 and <= 37:
                    _foreground = _bold ? BrightColors[code - 30] : DarkColors[code - 30];
                    break;

                // Background colors 40-47
                case >= 40 and <= 47:
                    _background = DarkColors[code - 40];
                    break;

                // Bright foreground colors 90-97
                case >= 90 and <= 97:
                    _foreground = BrightColors[code - 90];
                    break;

                // Bright background colors 100-107
                case >= 100 and <= 107:
                    _background = BrightColors[code - 100];
                    break;
            }
        }
    }

    private static List<int> ParseCodeValues(string paramStr)
    {
        var codes = new List<int>();
        if (string.IsNullOrEmpty(paramStr))
        {
            // ESC[m is equivalent to ESC[0m (reset)
            codes.Add(0);
            return codes;
        }

        foreach (var part in paramStr.Split(';'))
        {
            if (int.TryParse(part, out var code))
                codes.Add(code);
        }

        return codes;
    }
}
