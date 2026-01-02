using System.Text.RegularExpressions;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Provides code metrics analysis for BasicLang source files.
/// </summary>
public class CodeMetricsService : ICodeMetricsService
{
    // Patterns for BasicLang constructs
    private static readonly Regex ClassPattern = new(@"^\s*(Public\s+|Private\s+)?Class\s+\w+", RegexOptions.IgnoreCase | RegexOptions.Multiline);
    private static readonly Regex ModulePattern = new(@"^\s*Module\s+\w+", RegexOptions.IgnoreCase | RegexOptions.Multiline);
    private static readonly Regex MethodPattern = new(@"^\s*(Public\s+|Private\s+|Protected\s+)?(Sub|Function)\s+\w+", RegexOptions.IgnoreCase | RegexOptions.Multiline);
    private static readonly Regex CommentPattern = new(@"^\s*('|REM\s)", RegexOptions.IgnoreCase);
    private static readonly Regex BranchingPattern = new(@"\b(If|ElseIf|Select\s+Case|Case|For|For\s+Each|While|Do|Catch|AndAlso|OrElse)\b", RegexOptions.IgnoreCase);
    private static readonly Regex VariablePattern = new(@"\bDim\s+\w+", RegexOptions.IgnoreCase);

    /// <inheritdoc/>
    public CodeMetrics CalculateMetrics(string sourceCode)
    {
        if (string.IsNullOrEmpty(sourceCode))
        {
            return new CodeMetrics();
        }

        var lines = sourceCode.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var metrics = new CodeMetrics
        {
            TotalLines = lines.Length,
            BlankLines = lines.Count(string.IsNullOrWhiteSpace),
            CommentLines = lines.Count(line => CommentPattern.IsMatch(line)),
            ClassCount = ClassPattern.Matches(sourceCode).Count + ModulePattern.Matches(sourceCode).Count,
            MethodCount = MethodPattern.Matches(sourceCode).Count
        };

        metrics.CodeLines = metrics.TotalLines - metrics.BlankLines - metrics.CommentLines;

        // Calculate complexity
        var branchCount = BranchingPattern.Matches(sourceCode).Count;
        if (metrics.MethodCount > 0)
        {
            metrics.AverageCyclomaticComplexity = Math.Round((double)(branchCount + metrics.MethodCount) / metrics.MethodCount, 2);
        }

        // Calculate max nesting depth
        metrics.MaxNestingDepth = CalculateMaxNesting(lines);

        // Calculate maintainability index (simplified formula)
        // Based on Halstead Volume, Cyclomatic Complexity, and Lines of Code
        var halsteadVolume = Math.Log(metrics.CodeLines + 1) * metrics.CodeLines;
        var cc = metrics.AverageCyclomaticComplexity;
        var loc = metrics.CodeLines;

        if (loc > 0)
        {
            // Simplified maintainability index formula
            var mi = 171 - 5.2 * Math.Log(halsteadVolume + 1) - 0.23 * cc - 16.2 * Math.Log(loc + 1);
            metrics.MaintainabilityIndex = Math.Max(0, Math.Min(100, (int)Math.Round(mi * 100 / 171)));
        }
        else
        {
            metrics.MaintainabilityIndex = 100;
        }

        return metrics;
    }

    /// <inheritdoc/>
    public MethodMetrics CalculateMethodMetrics(string methodCode)
    {
        if (string.IsNullOrEmpty(methodCode))
        {
            return new MethodMetrics();
        }

        var lines = methodCode.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var metrics = new MethodMetrics
        {
            LineCount = lines.Length - lines.Count(string.IsNullOrWhiteSpace)
        };

        // Extract method name
        var methodMatch = MethodPattern.Match(methodCode);
        if (methodMatch.Success)
        {
            var parts = methodMatch.Value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            metrics.Name = parts.Length > 0 ? parts[^1] : "";
        }

        // Calculate cyclomatic complexity (1 + number of decision points)
        metrics.CyclomaticComplexity = 1 + BranchingPattern.Matches(methodCode).Count;

        // Count local variables
        metrics.LocalVariableCount = VariablePattern.Matches(methodCode).Count;

        // Calculate nesting depth
        metrics.NestingDepth = CalculateMaxNesting(lines);

        // Count parameters (simplified - looks for content between first set of parentheses)
        var paramMatch = Regex.Match(methodCode, @"\(([^)]*)\)", RegexOptions.Singleline);
        if (paramMatch.Success && !string.IsNullOrWhiteSpace(paramMatch.Groups[1].Value))
        {
            var paramText = paramMatch.Groups[1].Value;
            metrics.ParameterCount = paramText.Split(',').Length;
        }

        return metrics;
    }

    private static int CalculateMaxNesting(string[] lines)
    {
        var maxDepth = 0;
        var currentDepth = 0;

        var openingKeywords = new[] { "If", "For", "While", "Do", "Select", "Try", "With" };
        var closingKeywords = new[] { "End If", "Next", "Wend", "Loop", "End Select", "End Try", "End With" };

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();

            // Check for opening keywords
            foreach (var keyword in openingKeywords)
            {
                if (Regex.IsMatch(trimmed, $@"^{keyword}\b", RegexOptions.IgnoreCase) &&
                    !Regex.IsMatch(trimmed, @"\bThen\b.*\bEnd If\b", RegexOptions.IgnoreCase)) // Single-line If
                {
                    currentDepth++;
                    maxDepth = Math.Max(maxDepth, currentDepth);
                    break;
                }
            }

            // Check for closing keywords
            foreach (var keyword in closingKeywords)
            {
                if (Regex.IsMatch(trimmed, $@"^{Regex.Escape(keyword)}\b", RegexOptions.IgnoreCase))
                {
                    currentDepth = Math.Max(0, currentDepth - 1);
                    break;
                }
            }
        }

        return maxDepth;
    }
}
