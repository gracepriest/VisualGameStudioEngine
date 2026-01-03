using System.Text.RegularExpressions;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Provides static code analysis for BasicLang source files.
/// </summary>
public class CodeAnalysisService : ICodeAnalysisService
{
    // Patterns for detecting code issues
    private static readonly Regex FunctionPattern = new(@"^\s*(Function|Sub)\s+(\w+)\s*\(([^)]*)\)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private static readonly Regex EndFunctionPattern = new(@"^\s*End\s+(Function|Sub)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private static readonly Regex VariablePattern = new(@"^\s*(Dim|Let|Const|Var)\s+(\w+)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private static readonly Regex ImportPattern = new(@"^\s*Import\s+(\w+(?:\.\w+)*)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private static readonly Regex MagicNumberPattern = new(@"(?<!\w)(\d{2,})(?!\w)", RegexOptions.Compiled);
    private static readonly Regex PasswordPattern = new(@"(password|passwd|pwd|secret|apikey|api_key|token)\s*[=:]\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
    private static readonly Regex EmptyCatchPattern = new(@"Catch\s*\n\s*(End\s+Try|Catch)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
    private static readonly Regex DeepNestingPattern = new(@"^(\s+)(If|For|While|Do|Select)", RegexOptions.Multiline | RegexOptions.IgnoreCase);

    /// <inheritdoc/>
    public AnalysisOptions Options { get; set; } = new();

    /// <inheritdoc/>
    public event EventHandler<AnalysisEventArgs>? AnalysisStarted;

    /// <inheritdoc/>
    public event EventHandler<AnalysisEventArgs>? AnalysisCompleted;

    /// <inheritdoc/>
    public event EventHandler<AnalysisProgressEventArgs>? AnalysisProgress;

    /// <inheritdoc/>
    public AnalysisResult AnalyzeDocument(string content, string? filePath = null)
    {
        var startTime = DateTime.Now;
        var result = new AnalysisResult();
        var issues = new List<CodeIssue>();

        AnalysisStarted?.Invoke(this, new AnalysisEventArgs(filePath));

        if (Options.CheckCodeSmells)
        {
            result.CodeSmells = GetCodeSmells(content);
            issues.AddRange(result.CodeSmells);
        }

        if (Options.CheckSecurity)
        {
            result.SecurityIssues = GetSecurityIssues(content);
            issues.AddRange(result.SecurityIssues);
        }

        if (Options.CheckUnusedCode)
        {
            result.UnusedCode = GetUnusedCode(content);
            issues.AddRange(result.UnusedCode);
        }

        if (Options.CalculateComplexity)
        {
            result.Complexity = GetComplexity(content);
            // Add issues for overly complex functions
            foreach (var complexity in result.Complexity.Where(c => c.IsComplex))
            {
                issues.Add(new CodeIssue
                {
                    Id = "CA001",
                    Message = $"Function '{complexity.FunctionName}' has high cyclomatic complexity ({complexity.CyclomaticComplexity})",
                    Severity = IssueSeverity.Warning,
                    Category = "Complexity",
                    Line = complexity.StartLine
                });
            }
        }

        if (Options.DetectDuplicates)
        {
            result.Duplicates = GetDuplicates(content);
        }

        result.RefactoringSuggestions = GetRefactoringSuggestions(content);
        result.Issues = issues.Where(i => i.Severity >= Options.MinSeverity).ToList();
        result.Duration = DateTime.Now - startTime;

        AnalysisCompleted?.Invoke(this, new AnalysisEventArgs(filePath, result));

        return result;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FileAnalysisResult>> AnalyzeFilesAsync(
        IEnumerable<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        var results = new List<FileAnalysisResult>();
        var pathList = filePaths.ToList();
        var totalFiles = pathList.Count;
        var filesProcessed = 0;
        var totalIssues = 0;

        foreach (var filePath in pathList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileResult = new FileAnalysisResult
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath)
            };

            try
            {
                if (File.Exists(filePath))
                {
                    var content = await File.ReadAllTextAsync(filePath, cancellationToken);
                    fileResult.Result = AnalyzeDocument(content, filePath);
                    totalIssues += fileResult.Result.TotalIssues;
                }
                else
                {
                    fileResult.Success = false;
                    fileResult.Error = "File not found";
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                fileResult.Success = false;
                fileResult.Error = ex.Message;
            }

            results.Add(fileResult);
            filesProcessed++;

            AnalysisProgress?.Invoke(this, new AnalysisProgressEventArgs(
                filePath, filesProcessed, totalFiles, totalIssues));
        }

        return results;
    }

    /// <inheritdoc/>
    public IReadOnlyList<CodeSmell> GetCodeSmells(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return Array.Empty<CodeSmell>();
        }

        var smells = new List<CodeSmell>();
        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        // Check for long functions
        smells.AddRange(DetectLongFunctions(content, lines));

        // Check for too many parameters
        smells.AddRange(DetectTooManyParameters(content, lines));

        // Check for deep nesting
        smells.AddRange(DetectDeepNesting(lines));

        // Check for magic numbers
        smells.AddRange(DetectMagicNumbers(content, lines));

        // Check for empty catch blocks
        smells.AddRange(DetectEmptyCatch(content, lines));

        return smells;
    }

    /// <inheritdoc/>
    public IReadOnlyList<SecurityIssue> GetSecurityIssues(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return Array.Empty<SecurityIssue>();
        }

        var issues = new List<SecurityIssue>();
        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        // Check for hardcoded credentials
        issues.AddRange(DetectHardcodedCredentials(content, lines));

        // Check for potential command injection
        issues.AddRange(DetectCommandInjection(content, lines));

        // Check for path traversal
        issues.AddRange(DetectPathTraversal(content, lines));

        return issues;
    }

    /// <inheritdoc/>
    public IReadOnlyList<UnusedCode> GetUnusedCode(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return Array.Empty<UnusedCode>();
        }

        var unused = new List<UnusedCode>();
        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        // Find declared variables
        var declaredVars = new Dictionary<string, (int line, int column)>();
        var usedIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var varMatch = VariablePattern.Match(line);
            if (varMatch.Success)
            {
                var varName = varMatch.Groups[2].Value;
                if (!declaredVars.ContainsKey(varName))
                {
                    declaredVars[varName] = (i + 1, varMatch.Groups[2].Index + 1);
                }
            }

            // Track identifier usage (simplified)
            var identifierMatches = Regex.Matches(line, @"\b([a-zA-Z_]\w*)\b");
            foreach (Match m in identifierMatches)
            {
                usedIdentifiers.Add(m.Groups[1].Value);
            }
        }

        // Find unused variables (simplified - may have false positives)
        foreach (var kvp in declaredVars)
        {
            // Count usages - if only one (the declaration), it's unused
            var usageCount = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (Regex.IsMatch(lines[i], $@"\b{Regex.Escape(kvp.Key)}\b"))
                {
                    usageCount++;
                }
            }

            if (usageCount <= 1)
            {
                unused.Add(new UnusedCode
                {
                    Id = "UC001",
                    Message = $"Variable '{kvp.Key}' is declared but never used",
                    Severity = IssueSeverity.Warning,
                    Category = "Unused Code",
                    Line = kvp.Value.line,
                    Column = kvp.Value.column,
                    CodeType = UnusedCodeType.Variable,
                    Identifier = kvp.Key
                });
            }
        }

        return unused;
    }

    /// <inheritdoc/>
    public IReadOnlyList<ComplexityInfo> GetComplexity(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return Array.Empty<ComplexityInfo>();
        }

        var complexityList = new List<ComplexityInfo>();
        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        // Find functions
        var functionMatches = FunctionPattern.Matches(content);
        foreach (Match match in functionMatches)
        {
            var functionName = match.Groups[2].Value;
            var parameters = match.Groups[3].Value;
            var paramCount = string.IsNullOrWhiteSpace(parameters) ? 0 :
                parameters.Split(',').Length;

            // Find function boundaries
            var startIndex = match.Index;
            var startLine = content.Substring(0, startIndex).Count(c => c == '\n') + 1;

            // Find end of function
            var endMatch = EndFunctionPattern.Match(content, startIndex);
            var endLine = startLine;
            var lineCount = 1;

            if (endMatch.Success)
            {
                var endIndex = endMatch.Index;
                endLine = content.Substring(0, endIndex).Count(c => c == '\n') + 1;
                lineCount = endLine - startLine + 1;
            }

            // Calculate cyclomatic complexity
            var functionBody = content.Substring(startIndex,
                endMatch.Success ? endMatch.Index - startIndex + endMatch.Length : content.Length - startIndex);
            var cyclomaticComplexity = CalculateCyclomaticComplexity(functionBody);
            var cognitiveComplexity = CalculateCognitiveComplexity(functionBody);
            var maxNesting = CalculateMaxNestingDepth(functionBody);

            complexityList.Add(new ComplexityInfo
            {
                FunctionName = functionName,
                CyclomaticComplexity = cyclomaticComplexity,
                CognitiveComplexity = cognitiveComplexity,
                LineCount = lineCount,
                ParameterCount = paramCount,
                MaxNestingDepth = maxNesting,
                StartLine = startLine,
                EndLine = endLine
            });
        }

        return complexityList;
    }

    /// <inheritdoc/>
    public IReadOnlyList<DuplicateCode> GetDuplicates(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return Array.Empty<DuplicateCode>();
        }

        var duplicates = new List<DuplicateCode>();
        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        // Simple duplicate detection - look for identical consecutive line sequences
        var minLines = Options.MinDuplicateLines;

        for (int i = 0; i < lines.Length - minLines; i++)
        {
            for (int j = i + minLines; j < lines.Length - minLines; j++)
            {
                var matchingLines = 0;
                while (i + matchingLines < j &&
                       j + matchingLines < lines.Length &&
                       NormalizeForComparison(lines[i + matchingLines]) ==
                       NormalizeForComparison(lines[j + matchingLines]))
                {
                    matchingLines++;
                }

                if (matchingLines >= minLines)
                {
                    var snippet = string.Join(Environment.NewLine,
                        lines.Skip(i).Take(Math.Min(matchingLines, 5)));

                    duplicates.Add(new DuplicateCode
                    {
                        FirstLocation = new CodeLocation { StartLine = i + 1, EndLine = i + matchingLines },
                        SecondLocation = new CodeLocation { StartLine = j + 1, EndLine = j + matchingLines },
                        LineCount = matchingLines,
                        CodeSnippet = snippet,
                        SimilarityPercent = 100
                    });

                    // Skip past this duplicate
                    j += matchingLines - 1;
                }
            }
        }

        return duplicates;
    }

    /// <inheritdoc/>
    public IReadOnlyList<RefactoringSuggestion> GetRefactoringSuggestions(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return Array.Empty<RefactoringSuggestion>();
        }

        var suggestions = new List<RefactoringSuggestion>();

        // Get complexity info
        var complexity = GetComplexity(content);

        // Suggest extracting complex functions
        foreach (var c in complexity.Where(x => x.CyclomaticComplexity > Options.ComplexityThreshold))
        {
            suggestions.Add(new RefactoringSuggestion
            {
                Type = RefactoringType.ExtractMethod,
                Description = $"Consider breaking down function '{c.FunctionName}' (complexity: {c.CyclomaticComplexity})",
                Location = new CodeLocation { StartLine = c.StartLine, EndLine = c.EndLine },
                Priority = c.CyclomaticComplexity > 20 ? 1 : 2,
                Effort = c.CyclomaticComplexity > 20 ? RefactoringEffort.Large : RefactoringEffort.Medium
            });
        }

        // Suggest for long functions
        foreach (var c in complexity.Where(x => x.LineCount > Options.MaxFunctionLength))
        {
            suggestions.Add(new RefactoringSuggestion
            {
                Type = RefactoringType.ExtractMethod,
                Description = $"Function '{c.FunctionName}' is {c.LineCount} lines - consider splitting",
                Location = new CodeLocation { StartLine = c.StartLine, EndLine = c.EndLine },
                Priority = 2,
                Effort = RefactoringEffort.Medium
            });
        }

        // Suggest for too many parameters
        foreach (var c in complexity.Where(x => x.ParameterCount > 4))
        {
            suggestions.Add(new RefactoringSuggestion
            {
                Type = RefactoringType.IntroduceParameterObject,
                Description = $"Function '{c.FunctionName}' has {c.ParameterCount} parameters - consider a parameter object",
                Location = new CodeLocation { StartLine = c.StartLine, EndLine = c.StartLine },
                Priority = 3,
                Effort = RefactoringEffort.Small
            });
        }

        // Check for magic numbers and suggest constants
        var magicNumbers = DetectMagicNumbers(content, content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None));
        foreach (var smell in magicNumbers.Take(5)) // Limit suggestions
        {
            suggestions.Add(new RefactoringSuggestion
            {
                Type = RefactoringType.ReplaceMagicNumber,
                Description = smell.Message,
                Location = new CodeLocation { StartLine = smell.Line, EndLine = smell.Line },
                Priority = 4,
                Effort = RefactoringEffort.Trivial
            });
        }

        return suggestions;
    }

    #region Private Helper Methods

    private List<CodeSmell> DetectLongFunctions(string content, string[] lines)
    {
        var smells = new List<CodeSmell>();
        var complexity = GetComplexity(content);

        foreach (var c in complexity.Where(x => x.LineCount > Options.MaxFunctionLength))
        {
            smells.Add(new CodeSmell
            {
                Id = "CS001",
                Message = $"Function '{c.FunctionName}' is too long ({c.LineCount} lines, max {Options.MaxFunctionLength})",
                Severity = IssueSeverity.Warning,
                Category = "Code Smell",
                Line = c.StartLine,
                SmellType = CodeSmellType.LongFunction,
                SuggestedFix = "Consider breaking this function into smaller functions"
            });
        }

        return smells;
    }

    private List<CodeSmell> DetectTooManyParameters(string content, string[] lines)
    {
        var smells = new List<CodeSmell>();
        var functionMatches = FunctionPattern.Matches(content);

        foreach (Match match in functionMatches)
        {
            var parameters = match.Groups[3].Value;
            if (!string.IsNullOrWhiteSpace(parameters))
            {
                var paramCount = parameters.Split(',').Length;
                if (paramCount > 4)
                {
                    var line = content.Substring(0, match.Index).Count(c => c == '\n') + 1;
                    smells.Add(new CodeSmell
                    {
                        Id = "CS002",
                        Message = $"Function '{match.Groups[2].Value}' has too many parameters ({paramCount})",
                        Severity = IssueSeverity.Warning,
                        Category = "Code Smell",
                        Line = line,
                        SmellType = CodeSmellType.TooManyParameters,
                        SuggestedFix = "Consider using a parameter object or breaking down the function"
                    });
                }
            }
        }

        return smells;
    }

    private List<CodeSmell> DetectDeepNesting(string[] lines)
    {
        var smells = new List<CodeSmell>();
        var nestingStack = new Stack<string>();
        var maxNesting = Options.MaxNestingDepth;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // Check for nesting keywords
            if (Regex.IsMatch(line, @"^\s*(If|For|While|Do|Select|Try)\b", RegexOptions.IgnoreCase))
            {
                nestingStack.Push(line.TrimStart()[..Math.Min(3, line.TrimStart().Length)]);

                if (nestingStack.Count > maxNesting)
                {
                    smells.Add(new CodeSmell
                    {
                        Id = "CS003",
                        Message = $"Code has deep nesting ({nestingStack.Count} levels)",
                        Severity = IssueSeverity.Warning,
                        Category = "Code Smell",
                        Line = i + 1,
                        SmellType = CodeSmellType.DeepNesting,
                        SuggestedFix = "Consider extracting nested code into separate functions or using early returns"
                    });
                }
            }

            // Check for closing keywords
            if (Regex.IsMatch(line, @"^\s*(End\s+If|Next|Loop|End\s+Select|End\s+Try)\b", RegexOptions.IgnoreCase))
            {
                if (nestingStack.Count > 0)
                    nestingStack.Pop();
            }
        }

        return smells;
    }

    private List<CodeSmell> DetectMagicNumbers(string content, string[] lines)
    {
        var smells = new List<CodeSmell>();
        var allowedNumbers = new HashSet<string> { "0", "1", "2", "-1", "10", "100" };

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // Skip comments and string literals (simplified)
            if (line.TrimStart().StartsWith("'") || line.TrimStart().StartsWith("//"))
                continue;

            var matches = MagicNumberPattern.Matches(line);
            foreach (Match match in matches)
            {
                if (!allowedNumbers.Contains(match.Groups[1].Value))
                {
                    smells.Add(new CodeSmell
                    {
                        Id = "CS004",
                        Message = $"Magic number '{match.Groups[1].Value}' - consider using a named constant",
                        Severity = IssueSeverity.Info,
                        Category = "Code Smell",
                        Line = i + 1,
                        Column = match.Index + 1,
                        SmellType = CodeSmellType.MagicNumber,
                        SuggestedFix = $"Replace with a named constant like 'Const MAX_VALUE = {match.Groups[1].Value}'"
                    });
                }
            }
        }

        return smells;
    }

    private List<CodeSmell> DetectEmptyCatch(string content, string[] lines)
    {
        var smells = new List<CodeSmell>();
        var matches = EmptyCatchPattern.Matches(content);

        foreach (Match match in matches)
        {
            var line = content.Substring(0, match.Index).Count(c => c == '\n') + 1;
            smells.Add(new CodeSmell
            {
                Id = "CS005",
                Message = "Empty catch block - exceptions are silently ignored",
                Severity = IssueSeverity.Warning,
                Category = "Code Smell",
                Line = line,
                SmellType = CodeSmellType.EmptyCatch,
                SuggestedFix = "Log the exception or handle it appropriately"
            });
        }

        return smells;
    }

    private List<SecurityIssue> DetectHardcodedCredentials(string content, string[] lines)
    {
        var issues = new List<SecurityIssue>();
        var matches = PasswordPattern.Matches(content);

        foreach (Match match in matches)
        {
            var line = content.Substring(0, match.Index).Count(c => c == '\n') + 1;
            issues.Add(new SecurityIssue
            {
                Id = "SEC001",
                Message = $"Potential hardcoded credential found: {match.Groups[1].Value}",
                Severity = IssueSeverity.Critical,
                Category = "Security",
                Line = line,
                IssueType = SecurityIssueType.HardcodedCredential,
                CweId = "CWE-798",
                Remediation = "Store credentials in environment variables or a secure configuration system"
            });
        }

        return issues;
    }

    private List<SecurityIssue> DetectCommandInjection(string content, string[] lines)
    {
        var issues = new List<SecurityIssue>();
        var shellPattern = new Regex(@"(Shell|Execute|Run|System)\s*\([^""']*\+", RegexOptions.IgnoreCase);

        for (int i = 0; i < lines.Length; i++)
        {
            if (shellPattern.IsMatch(lines[i]))
            {
                issues.Add(new SecurityIssue
                {
                    Id = "SEC002",
                    Message = "Potential command injection - user input may be concatenated into shell command",
                    Severity = IssueSeverity.Critical,
                    Category = "Security",
                    Line = i + 1,
                    IssueType = SecurityIssueType.CommandInjection,
                    CweId = "CWE-78",
                    Remediation = "Validate and sanitize all input before using in shell commands"
                });
            }
        }

        return issues;
    }

    private List<SecurityIssue> DetectPathTraversal(string content, string[] lines)
    {
        var issues = new List<SecurityIssue>();
        var pathPattern = new Regex(@"(OpenFile|ReadFile|WriteFile|File\.Open)\s*\([^""']*\+", RegexOptions.IgnoreCase);

        for (int i = 0; i < lines.Length; i++)
        {
            if (pathPattern.IsMatch(lines[i]))
            {
                issues.Add(new SecurityIssue
                {
                    Id = "SEC003",
                    Message = "Potential path traversal - user input may be concatenated into file path",
                    Severity = IssueSeverity.Error,
                    Category = "Security",
                    Line = i + 1,
                    IssueType = SecurityIssueType.PathTraversal,
                    CweId = "CWE-22",
                    Remediation = "Validate file paths and use a whitelist of allowed directories"
                });
            }
        }

        return issues;
    }

    private static int CalculateCyclomaticComplexity(string functionBody)
    {
        // Start with 1 for the function itself
        var complexity = 1;

        // Count decision points
        var decisionPatterns = new[]
        {
            @"\bIf\b",
            @"\bElseIf\b",
            @"\bCase\b",
            @"\bFor\b",
            @"\bWhile\b",
            @"\bDo\b",
            @"\bAnd\b",
            @"\bOr\b",
            @"\bCatch\b",
            @"\?\?"  // Null coalescing
        };

        foreach (var pattern in decisionPatterns)
        {
            complexity += Regex.Matches(functionBody, pattern, RegexOptions.IgnoreCase).Count;
        }

        return complexity;
    }

    private static int CalculateCognitiveComplexity(string functionBody)
    {
        var complexity = 0;
        var nestingLevel = 0;
        var lines = functionBody.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Nesting increments
            if (Regex.IsMatch(trimmed, @"^(If|For|While|Do|Select|Try)\b", RegexOptions.IgnoreCase))
            {
                complexity += 1 + nestingLevel;
                nestingLevel++;
            }
            else if (Regex.IsMatch(trimmed, @"^(ElseIf|Else|Catch)\b", RegexOptions.IgnoreCase))
            {
                complexity += 1;
            }
            else if (Regex.IsMatch(trimmed, @"^(End\s+If|Next|Loop|End\s+Select|End\s+Try)\b", RegexOptions.IgnoreCase))
            {
                if (nestingLevel > 0) nestingLevel--;
            }

            // Boolean operators add complexity
            complexity += Regex.Matches(trimmed, @"\b(And|Or)\b", RegexOptions.IgnoreCase).Count;
        }

        return complexity;
    }

    private static int CalculateMaxNestingDepth(string functionBody)
    {
        var maxDepth = 0;
        var currentDepth = 0;
        var lines = functionBody.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (Regex.IsMatch(trimmed, @"^(If|For|While|Do|Select|Try)\b", RegexOptions.IgnoreCase))
            {
                currentDepth++;
                maxDepth = Math.Max(maxDepth, currentDepth);
            }
            else if (Regex.IsMatch(trimmed, @"^(End\s+If|Next|Loop|End\s+Select|End\s+Try)\b", RegexOptions.IgnoreCase))
            {
                if (currentDepth > 0) currentDepth--;
            }
        }

        return maxDepth;
    }

    private static string NormalizeForComparison(string line)
    {
        // Remove leading/trailing whitespace and normalize internal whitespace
        return Regex.Replace(line.Trim(), @"\s+", " ");
    }

    #endregion
}
