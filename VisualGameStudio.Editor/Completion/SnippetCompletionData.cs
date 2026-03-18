using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;

namespace VisualGameStudio.Editor.Completion;

/// <summary>
/// Completion data for a code snippet. When accepted, inserts an AvaloniaEdit Snippet
/// with tab-stop placeholders that support Tab/Shift+Tab cycling between positions.
/// </summary>
public class SnippetCompletionData : ICompletionData
{
    private static readonly SolidColorBrush TextBrush = new(Color.FromRgb(220, 220, 220));
    private static readonly SolidColorBrush SnippetBrush = new(Color.FromRgb(180, 140, 255));

    private readonly SnippetDefinition _snippet;

    public SnippetCompletionData(SnippetDefinition snippet)
    {
        _snippet = snippet;
        Text = snippet.Prefixes[0]; // Primary prefix as the display text
    }

    public string Text { get; }

    public object Content => new StackPanel
    {
        Orientation = Avalonia.Layout.Orientation.Horizontal,
        Spacing = 6,
        Children =
        {
            new TextBlock
            {
                Text = "\u2B9E", // snippet icon (right-pointing arrowhead)
                Foreground = SnippetBrush,
                FontSize = 12,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            },
            new TextBlock
            {
                Text = Text,
                Foreground = TextBrush,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            }
        }
    };

    public object? Description => $"{_snippet.Name}: {_snippet.Description}";

    /// <summary>
    /// Snippets get high priority so they appear near the top of the completion list.
    /// </summary>
    public double Priority => 0.5;

    public IImage? Image => null;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        var document = textArea.Document;

        // Get the indentation of the line where the snippet is being inserted
        var line = document.GetLineByOffset(completionSegment.Offset);
        var lineText = document.GetText(line.Offset, completionSegment.Offset - line.Offset);
        var indent = "";
        foreach (var c in lineText)
        {
            if (c == ' ' || c == '\t') indent += c;
            else break;
        }

        // Remove the completion segment text (the typed prefix) so the snippet
        // insertion replaces it cleanly
        document.Remove(completionSegment);
        textArea.Caret.Offset = completionSegment.Offset;

        // Build and insert the AvaloniaEdit Snippet with interactive tab-stops.
        // Tab/Shift+Tab cycles between placeholders; Escape/Enter exits snippet mode.
        var snippet = _snippet.BuildSnippet(indent);
        snippet.Insert(textArea);
    }
}
