using System.Text.RegularExpressions;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;

namespace VisualGameStudio.Editor.Folding;

public class BasicLangFoldingStrategy
{
    private static readonly Regex StartPattern = new(
        @"^\s*(?:(?:Public|Private|Protected|Friend)\s+)?(?:(?:Shared|Overridable|Overrides|MustOverride|NotOverridable)\s+)?" +
        @"(?:Sub|Function|Class|Module|Namespace|Structure|Interface|Enum|Property|Type|Template|If|For|While|Do|Select|Try|With)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EndPattern = new(
        @"^\s*(?:End\s+(?:Sub|Function|Class|Module|Namespace|Structure|Interface|Enum|Property|Type|Template|If|Select|Try|With)|" +
        @"EndSub|EndFunction|EndClass|EndModule|EndNamespace|EndStructure|EndInterface|EndEnum|EndProperty|EndType|EndTemplate|EndIf|EndSelect|EndTry|EndWith|" +
        @"Next|Wend|Loop)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SingleLineIfPattern = new(
        @"^\s*If\b.*\bThen\b.*[^\s].*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public void UpdateFoldings(FoldingManager manager, TextDocument document)
    {
        var foldings = CreateFoldings(document);
        manager.UpdateFoldings(foldings, -1);
    }

    public IEnumerable<NewFolding> CreateFoldings(TextDocument document)
    {
        var foldings = new List<NewFolding>();
        var stack = new Stack<FoldingInfo>();

        for (int lineNumber = 1; lineNumber <= document.LineCount; lineNumber++)
        {
            var line = document.GetLineByNumber(lineNumber);
            var lineText = document.GetText(line.Offset, line.Length);

            // Skip single-line If statements
            if (SingleLineIfPattern.IsMatch(lineText))
                continue;

            if (StartPattern.IsMatch(lineText))
            {
                var foldingName = GetFoldingName(lineText);
                stack.Push(new FoldingInfo(line.Offset, foldingName));
            }
            else if (EndPattern.IsMatch(lineText) && stack.Count > 0)
            {
                var start = stack.Pop();
                var endOffset = line.EndOffset;

                if (endOffset > start.StartOffset)
                {
                    foldings.Add(new NewFolding(start.StartOffset, endOffset)
                    {
                        Name = start.Name,
                        DefaultClosed = false
                    });
                }
            }
        }

        // Handle any unclosed foldings (syntax errors in code)
        while (stack.Count > 0)
        {
            var start = stack.Pop();
            var endLine = document.GetLineByNumber(document.LineCount);
            foldings.Add(new NewFolding(start.StartOffset, endLine.EndOffset)
            {
                Name = start.Name + " (unclosed)",
                DefaultClosed = false
            });
        }

        foldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return foldings;
    }

    private static string GetFoldingName(string lineText)
    {
        var trimmed = lineText.Trim();

        // Extract meaningful part for display
        var keywords = new[] { "Sub", "Function", "Class", "Module", "Namespace", "Structure",
                               "Interface", "Enum", "Property", "Type", "Template", "If", "For",
                               "While", "Do", "Select", "Try", "With" };

        foreach (var keyword in keywords)
        {
            var index = trimmed.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                // Return the line starting from the keyword
                var result = trimmed.Substring(index);
                // Truncate if too long
                if (result.Length > 50)
                    result = result.Substring(0, 50) + "...";
                return result;
            }
        }

        return trimmed.Length > 50 ? trimmed.Substring(0, 50) + "..." : trimmed;
    }

    private record FoldingInfo(int StartOffset, string Name);
}
