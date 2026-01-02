using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;

namespace VisualGameStudio.Editor.Completion;

/// <summary>
/// Represents a completion item for the code completion popup
/// </summary>
public class CompletionData : ICompletionData
{
    public CompletionData(string text, string? description = null, CompletionItemKind kind = CompletionItemKind.Text, string? insertText = null)
    {
        Text = text;
        Description = description;
        InsertText = insertText ?? text;
        Kind = kind;
    }

    public string Text { get; }
    public string InsertText { get; }
    public CompletionItemKind Kind { get; }

    public object Content => Text;
    public object? Description { get; }
    public double Priority => GetPriority();

    public IImage? Image => null;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        textArea.Document.Replace(completionSegment, InsertText);
    }

    private double GetPriority()
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
