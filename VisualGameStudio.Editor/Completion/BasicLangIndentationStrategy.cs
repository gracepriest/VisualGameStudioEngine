using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Indentation;

namespace VisualGameStudio.Editor.Completion;

/// <summary>
/// Smart indentation strategy for BasicLang that mirrors the VS Code extension's
/// indentationRules and onEnterRules from language-configuration.json.
///
/// When Enter is pressed:
/// - After Sub, Function, Class, If Then, For, While, etc. -> increases indent
/// - After End Sub, Next, Loop, etc. -> keeps same indent (those lines already outdented)
/// - Else, ElseIf, Case, Catch, Finally -> same indent as block opener
/// </summary>
public class BasicLangIndentationStrategy : IIndentationStrategy
{
    /// <summary>
    /// Number of spaces per indent level.
    /// </summary>
    public int IndentSize { get; set; } = 4;

    /// <summary>
    /// Whether to use tabs instead of spaces.
    /// </summary>
    public bool UseTabs { get; set; }

    public void IndentLine(TextDocument document, DocumentLine line)
    {
        if (document == null || line == null) return;

        // Get the previous line
        var prevLineNumber = line.LineNumber - 1;
        if (prevLineNumber < 1)
        {
            // First line - no indentation
            return;
        }

        var prevDocLine = document.GetLineByNumber(prevLineNumber);
        var prevLineText = document.GetText(prevDocLine.Offset, prevDocLine.Length);

        // Calculate new indent based on previous line
        var newIndent = SmartIndentHandler.GetNewLineIndent(prevLineText, IndentSize, UseTabs);

        // Apply the indentation to the current line
        var currentLineText = document.GetText(line.Offset, line.Length);
        var currentTrimmed = currentLineText.TrimStart();

        // If the current line (just created by Enter) has content that should be outdented,
        // handle that too (e.g., if the user has auto-typed "End If" somehow)
        if (!string.IsNullOrEmpty(currentTrimmed))
        {
            var corrected = SmartIndentHandler.GetCorrectedIndent(currentLineText, prevLineText, IndentSize, UseTabs);
            if (corrected != null)
            {
                document.Replace(line.Offset, line.Length, corrected);
                return;
            }
        }

        // Apply new indent to empty or whitespace-only line
        if (string.IsNullOrWhiteSpace(currentLineText))
        {
            if (currentLineText != newIndent)
            {
                document.Replace(line.Offset, line.Length, newIndent);
            }
        }
        else
        {
            // Preserve existing content, just fix the indent
            var newLine = newIndent + currentTrimmed;
            if (currentLineText != newLine)
            {
                document.Replace(line.Offset, line.Length, newLine);
            }
        }
    }

    public void IndentLines(TextDocument document, int beginLine, int endLine)
    {
        // Indent each line individually
        for (int i = beginLine; i <= endLine; i++)
        {
            if (i >= 1 && i <= document.LineCount)
            {
                IndentLine(document, document.GetLineByNumber(i));
            }
        }
    }
}
