using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;

namespace VisualGameStudio.Editor.Completion;

/// <summary>
/// Represents a completion item for the code completion popup.
/// </summary>
public class CompletionData : ICompletionData
{
    // VS Code-style kind colors — chosen to be readable on both light and
    // dark list backgrounds. The label itself inherits the theme foreground.
    private static readonly SolidColorBrush MethodBrush = new(Color.FromRgb(178, 128, 215));   // purple
    private static readonly SolidColorBrush VariableBrush = new(Color.FromRgb(75, 156, 213));  // blue
    private static readonly SolidColorBrush ClassBrush = new(Color.FromRgb(211, 144, 58));     // orange
    private static readonly SolidColorBrush InterfaceBrush = new(Color.FromRgb(75, 156, 213)); // blue
    private static readonly SolidColorBrush KeywordBrush = new(Color.FromRgb(120, 129, 138));  // gray
    private static readonly SolidColorBrush SnippetBrush = new(Color.FromRgb(180, 140, 255));  // violet
    private static readonly SolidColorBrush EnumBrush = new(Color.FromRgb(211, 144, 58));      // orange
    private static readonly SolidColorBrush PropertyBrush = new(Color.FromRgb(97, 175, 154));  // teal
    private static readonly SolidColorBrush DefaultBrush = new(Color.FromRgb(120, 129, 138));  // gray

    public CompletionData(
        string text,
        string? description = null,
        CompletionItemKind kind = CompletionItemKind.Text,
        string? insertText = null,
        bool isSnippet = false,
        string? filterText = null,
        string? sortText = null,
        bool preselect = false)
    {
        Label = text;
        // AvaloniaEdit's CompletionList filters/matches on Text, so feed it
        // the server's FilterText when present (Label may contain punctuation
        // like "If...Else" that would defeat prefix matching).
        Text = filterText ?? text;
        Description = description;
        InsertText = insertText ?? text;
        Kind = kind;
        IsSnippet = isSnippet;
        SortText = sortText;
        Preselect = preselect;
        Priority = GetDefaultPriority();
    }

    /// <summary>The display label shown in the list.</summary>
    public string Label { get; }

    /// <summary>The text AvaloniaEdit uses for filtering (FilterText, falling back to the label).</summary>
    public string Text { get; }

    public string InsertText { get; }
    public CompletionItemKind Kind { get; }

    /// <summary>When true, InsertText contains LSP snippet syntax and is expanded with tab stops on commit.</summary>
    public bool IsSnippet { get; }

    /// <summary>Server-provided rank, carried for ordering the list.</summary>
    public string? SortText { get; }

    /// <summary>When true, the server wants this item initially selected.</summary>
    public bool Preselect { get; }

    public object Content => BuildContent();

    /// <summary>Tooltip shown for the selected item ("Detail\n\nDocumentation").</summary>
    public object? Description { get; }

    /// <summary>
    /// AvaloniaEdit prefers the HIGHEST priority among equal-quality matches.
    /// Defaults to a kind-based value; the LSP pipeline overrides it with the
    /// server rank so the server's best match wins selection.
    /// </summary>
    public double Priority { get; set; }

    public IImage? Image => null;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        if (IsSnippet && InsertText.Contains('$'))
        {
            // Expand LSP snippet syntax ($0, $N, ${N:default}) into a real
            // AvaloniaEdit snippet with tab stops — never insert raw markers.
            // Continuation-line indentation is applied by AvaloniaEdit's own
            // InsertionContext on insert, so the snippet carries none itself.
            var document = textArea.Document;
            var insertOffset = completionSegment.Offset;
            document.Remove(completionSegment);
            textArea.Caret.Offset = insertOffset;

            var snippet = SnippetDefinition.FromInsertText(InsertText).BuildSnippet();
            snippet.Insert(textArea);
            return;
        }

        textArea.Document.Replace(completionSegment, InsertText);
    }

    private StackPanel BuildContent()
    {
        var (glyph, brush) = GetKindGlyph();

        return new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new TextBlock
                {
                    Text = glyph,
                    Foreground = brush,
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 12,
                    Width = 14,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                },
                new TextBlock
                {
                    // No explicit Foreground: inherit the theme's list text
                    // color so items stay readable on light AND dark themes.
                    Text = Label,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                }
            }
        };
    }

    private (string Glyph, IBrush Brush) GetKindGlyph()
    {
        return Kind switch
        {
            CompletionItemKind.Method => ("M", MethodBrush),
            CompletionItemKind.Function => ("F", MethodBrush),
            CompletionItemKind.Constructor => ("C", MethodBrush),
            CompletionItemKind.Field => ("f", VariableBrush),
            CompletionItemKind.Variable => ("V", VariableBrush),
            CompletionItemKind.Class => ("C", ClassBrush),
            CompletionItemKind.Struct => ("S", ClassBrush),
            CompletionItemKind.Interface => ("I", InterfaceBrush),
            CompletionItemKind.Module => ("M", ClassBrush),
            CompletionItemKind.Property => ("P", PropertyBrush),
            CompletionItemKind.Event => ("E", PropertyBrush),
            CompletionItemKind.Enum => ("E", EnumBrush),
            CompletionItemKind.EnumMember => ("e", EnumBrush),
            CompletionItemKind.Constant => ("K", VariableBrush),
            CompletionItemKind.Keyword => ("k", KeywordBrush),
            CompletionItemKind.Snippet => ("⮞", SnippetBrush),
            CompletionItemKind.TypeParameter => ("T", ClassBrush),
            _ => ("•", DefaultBrush)
        };
    }

    private double GetDefaultPriority()
    {
        return Kind switch
        {
            CompletionItemKind.Keyword => 1.0,
            CompletionItemKind.Snippet => 1.1,
            CompletionItemKind.Function or CompletionItemKind.Method => 2.0,
            CompletionItemKind.Variable or CompletionItemKind.Field => 3.0,
            CompletionItemKind.Class or CompletionItemKind.Interface => 4.0,
            CompletionItemKind.Module => 5.0,
            _ => 10.0
        };
    }
}

public enum CompletionItemKind
{
    Text = 1,
    Method = 2,
    Function = 3,
    Constructor = 4,
    Field = 5,
    Variable = 6,
    Class = 7,
    Interface = 8,
    Module = 9,
    Property = 10,
    Unit = 11,
    Value = 12,
    Enum = 13,
    Keyword = 14,
    Snippet = 15,
    Color = 16,
    File = 17,
    Reference = 18,
    Folder = 19,
    EnumMember = 20,
    Constant = 21,
    Struct = 22,
    Event = 23,
    Operator = 24,
    TypeParameter = 25
}
