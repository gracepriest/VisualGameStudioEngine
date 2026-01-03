namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Provides static code analysis functionality.
/// </summary>
public interface ICodeAnalysisService
{
    /// <summary>
    /// Gets or sets the analysis options.
    /// </summary>
    AnalysisOptions Options { get; set; }

    /// <summary>
    /// Analyzes a document for code issues.
    /// </summary>
    /// <param name="content">The document content.</param>
    /// <param name="filePath">Optional file path for context.</param>
    /// <returns>The analysis results.</returns>
    AnalysisResult AnalyzeDocument(string content, string? filePath = null);

    /// <summary>
    /// Analyzes multiple files asynchronously.
    /// </summary>
    /// <param name="filePaths">The file paths to analyze.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Analysis results for all files.</returns>
    Task<IReadOnlyList<FileAnalysisResult>> AnalyzeFilesAsync(
        IEnumerable<string> filePaths,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets code smells in the document.
    /// </summary>
    /// <param name="content">The document content.</param>
    /// <returns>List of detected code smells.</returns>
    IReadOnlyList<CodeSmell> GetCodeSmells(string content);

    /// <summary>
    /// Gets potential security issues in the code.
    /// </summary>
    /// <param name="content">The document content.</param>
    /// <returns>List of security issues.</returns>
    IReadOnlyList<SecurityIssue> GetSecurityIssues(string content);

    /// <summary>
    /// Gets unused variables and imports.
    /// </summary>
    /// <param name="content">The document content.</param>
    /// <returns>List of unused code elements.</returns>
    IReadOnlyList<UnusedCode> GetUnusedCode(string content);

    /// <summary>
    /// Calculates cyclomatic complexity for functions.
    /// </summary>
    /// <param name="content">The document content.</param>
    /// <returns>Complexity information for each function.</returns>
    IReadOnlyList<ComplexityInfo> GetComplexity(string content);

    /// <summary>
    /// Gets code duplication information.
    /// </summary>
    /// <param name="content">The document content.</param>
    /// <returns>List of duplicate code blocks.</returns>
    IReadOnlyList<DuplicateCode> GetDuplicates(string content);

    /// <summary>
    /// Suggests refactoring opportunities.
    /// </summary>
    /// <param name="content">The document content.</param>
    /// <returns>List of refactoring suggestions.</returns>
    IReadOnlyList<RefactoringSuggestion> GetRefactoringSuggestions(string content);

    /// <summary>
    /// Raised when analysis starts.
    /// </summary>
    event EventHandler<AnalysisEventArgs>? AnalysisStarted;

    /// <summary>
    /// Raised when analysis completes.
    /// </summary>
    event EventHandler<AnalysisEventArgs>? AnalysisCompleted;

    /// <summary>
    /// Raised to report progress during analysis.
    /// </summary>
    event EventHandler<AnalysisProgressEventArgs>? AnalysisProgress;
}

/// <summary>
/// Options for code analysis.
/// </summary>
public class AnalysisOptions
{
    /// <summary>
    /// Gets or sets whether to check for code smells.
    /// </summary>
    public bool CheckCodeSmells { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to check for security issues.
    /// </summary>
    public bool CheckSecurity { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to check for unused code.
    /// </summary>
    public bool CheckUnusedCode { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to calculate complexity.
    /// </summary>
    public bool CalculateComplexity { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to detect duplicates.
    /// </summary>
    public bool DetectDuplicates { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum function length before warning.
    /// </summary>
    public int MaxFunctionLength { get; set; } = 50;

    /// <summary>
    /// Gets or sets the maximum nesting depth before warning.
    /// </summary>
    public int MaxNestingDepth { get; set; } = 4;

    /// <summary>
    /// Gets or sets the complexity threshold for warnings.
    /// </summary>
    public int ComplexityThreshold { get; set; } = 10;

    /// <summary>
    /// Gets or sets the minimum duplicate lines to report.
    /// </summary>
    public int MinDuplicateLines { get; set; } = 5;

    /// <summary>
    /// Gets or sets severity levels to include.
    /// </summary>
    public IssueSeverity MinSeverity { get; set; } = IssueSeverity.Info;
}

/// <summary>
/// Result of analyzing a document.
/// </summary>
public class AnalysisResult
{
    /// <summary>
    /// Gets or sets all issues found.
    /// </summary>
    public IReadOnlyList<CodeIssue> Issues { get; set; } = Array.Empty<CodeIssue>();

    /// <summary>
    /// Gets or sets code smells found.
    /// </summary>
    public IReadOnlyList<CodeSmell> CodeSmells { get; set; } = Array.Empty<CodeSmell>();

    /// <summary>
    /// Gets or sets security issues found.
    /// </summary>
    public IReadOnlyList<SecurityIssue> SecurityIssues { get; set; } = Array.Empty<SecurityIssue>();

    /// <summary>
    /// Gets or sets unused code found.
    /// </summary>
    public IReadOnlyList<UnusedCode> UnusedCode { get; set; } = Array.Empty<UnusedCode>();

    /// <summary>
    /// Gets or sets complexity information.
    /// </summary>
    public IReadOnlyList<ComplexityInfo> Complexity { get; set; } = Array.Empty<ComplexityInfo>();

    /// <summary>
    /// Gets or sets duplicate code found.
    /// </summary>
    public IReadOnlyList<DuplicateCode> Duplicates { get; set; } = Array.Empty<DuplicateCode>();

    /// <summary>
    /// Gets or sets refactoring suggestions.
    /// </summary>
    public IReadOnlyList<RefactoringSuggestion> RefactoringSuggestions { get; set; } = Array.Empty<RefactoringSuggestion>();

    /// <summary>
    /// Gets the total number of issues.
    /// </summary>
    public int TotalIssues => Issues.Count + CodeSmells.Count + SecurityIssues.Count + UnusedCode.Count;

    /// <summary>
    /// Gets or sets the analysis duration.
    /// </summary>
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Analysis result for a file.
/// </summary>
public class FileAnalysisResult
{
    /// <summary>
    /// Gets or sets the file path.
    /// </summary>
    public string FilePath { get; set; } = "";

    /// <summary>
    /// Gets or sets the file name.
    /// </summary>
    public string FileName { get; set; } = "";

    /// <summary>
    /// Gets or sets the analysis result.
    /// </summary>
    public AnalysisResult Result { get; set; } = new();

    /// <summary>
    /// Gets or sets whether the analysis succeeded.
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// Gets or sets any error message.
    /// </summary>
    public string? Error { get; set; }
}

/// <summary>
/// Base class for code issues.
/// </summary>
public class CodeIssue
{
    /// <summary>
    /// Gets or sets the issue ID/code.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Gets or sets the issue message.
    /// </summary>
    public string Message { get; set; } = "";

    /// <summary>
    /// Gets or sets the severity.
    /// </summary>
    public IssueSeverity Severity { get; set; }

    /// <summary>
    /// Gets or sets the category.
    /// </summary>
    public string Category { get; set; } = "";

    /// <summary>
    /// Gets or sets the line number.
    /// </summary>
    public int Line { get; set; }

    /// <summary>
    /// Gets or sets the column.
    /// </summary>
    public int Column { get; set; }

    /// <summary>
    /// Gets or sets the length of the issue span.
    /// </summary>
    public int Length { get; set; }

    /// <summary>
    /// Gets or sets the source code snippet.
    /// </summary>
    public string? CodeSnippet { get; set; }
}

/// <summary>
/// Severity levels for issues.
/// </summary>
public enum IssueSeverity
{
    /// <summary>Informational.</summary>
    Info = 0,
    /// <summary>Warning.</summary>
    Warning = 1,
    /// <summary>Error.</summary>
    Error = 2,
    /// <summary>Critical issue.</summary>
    Critical = 3
}

/// <summary>
/// Represents a code smell.
/// </summary>
public class CodeSmell : CodeIssue
{
    /// <summary>
    /// Gets or sets the smell type.
    /// </summary>
    public CodeSmellType SmellType { get; set; }

    /// <summary>
    /// Gets or sets suggested fix.
    /// </summary>
    public string? SuggestedFix { get; set; }
}

/// <summary>
/// Types of code smells.
/// </summary>
public enum CodeSmellType
{
    /// <summary>Function is too long.</summary>
    LongFunction,
    /// <summary>Too many parameters.</summary>
    TooManyParameters,
    /// <summary>Deep nesting.</summary>
    DeepNesting,
    /// <summary>Magic number.</summary>
    MagicNumber,
    /// <summary>Magic string.</summary>
    MagicString,
    /// <summary>Empty catch block.</summary>
    EmptyCatch,
    /// <summary>Dead code.</summary>
    DeadCode,
    /// <summary>Complex condition.</summary>
    ComplexCondition,
    /// <summary>Feature envy - accessing other object's data too much.</summary>
    FeatureEnvy,
    /// <summary>Large class/module.</summary>
    LargeClass,
    /// <summary>God class - does too much.</summary>
    GodClass,
    /// <summary>Duplicate code.</summary>
    DuplicateCode
}

/// <summary>
/// Represents a security issue.
/// </summary>
public class SecurityIssue : CodeIssue
{
    /// <summary>
    /// Gets or sets the security issue type.
    /// </summary>
    public SecurityIssueType IssueType { get; set; }

    /// <summary>
    /// Gets or sets the CWE ID if applicable.
    /// </summary>
    public string? CweId { get; set; }

    /// <summary>
    /// Gets or sets remediation advice.
    /// </summary>
    public string? Remediation { get; set; }
}

/// <summary>
/// Types of security issues.
/// </summary>
public enum SecurityIssueType
{
    /// <summary>Hardcoded credential.</summary>
    HardcodedCredential,
    /// <summary>Potential SQL injection.</summary>
    SqlInjection,
    /// <summary>Command injection.</summary>
    CommandInjection,
    /// <summary>Path traversal.</summary>
    PathTraversal,
    /// <summary>Insecure random.</summary>
    InsecureRandom,
    /// <summary>Weak cryptography.</summary>
    WeakCrypto,
    /// <summary>Information exposure.</summary>
    InformationExposure,
    /// <summary>Insecure deserialization.</summary>
    InsecureDeserialization,
    /// <summary>Missing input validation.</summary>
    MissingValidation
}

/// <summary>
/// Represents unused code.
/// </summary>
public class UnusedCode : CodeIssue
{
    /// <summary>
    /// Gets or sets the type of unused code.
    /// </summary>
    public UnusedCodeType CodeType { get; set; }

    /// <summary>
    /// Gets or sets the identifier name.
    /// </summary>
    public string Identifier { get; set; } = "";
}

/// <summary>
/// Types of unused code.
/// </summary>
public enum UnusedCodeType
{
    /// <summary>Unused variable.</summary>
    Variable,
    /// <summary>Unused function.</summary>
    Function,
    /// <summary>Unused import.</summary>
    Import,
    /// <summary>Unused parameter.</summary>
    Parameter,
    /// <summary>Unused type.</summary>
    Type,
    /// <summary>Unused field.</summary>
    Field
}

/// <summary>
/// Complexity information for a function.
/// </summary>
public class ComplexityInfo
{
    /// <summary>
    /// Gets or sets the function name.
    /// </summary>
    public string FunctionName { get; set; } = "";

    /// <summary>
    /// Gets or sets the cyclomatic complexity.
    /// </summary>
    public int CyclomaticComplexity { get; set; }

    /// <summary>
    /// Gets or sets the cognitive complexity.
    /// </summary>
    public int CognitiveComplexity { get; set; }

    /// <summary>
    /// Gets or sets the line count.
    /// </summary>
    public int LineCount { get; set; }

    /// <summary>
    /// Gets or sets the parameter count.
    /// </summary>
    public int ParameterCount { get; set; }

    /// <summary>
    /// Gets or sets the maximum nesting depth.
    /// </summary>
    public int MaxNestingDepth { get; set; }

    /// <summary>
    /// Gets or sets the start line.
    /// </summary>
    public int StartLine { get; set; }

    /// <summary>
    /// Gets or sets the end line.
    /// </summary>
    public int EndLine { get; set; }

    /// <summary>
    /// Gets whether the complexity exceeds threshold.
    /// </summary>
    public bool IsComplex => CyclomaticComplexity > 10;
}

/// <summary>
/// Represents duplicate code.
/// </summary>
public class DuplicateCode
{
    /// <summary>
    /// Gets or sets the first occurrence location.
    /// </summary>
    public CodeLocation FirstLocation { get; set; } = new();

    /// <summary>
    /// Gets or sets the second occurrence location.
    /// </summary>
    public CodeLocation SecondLocation { get; set; } = new();

    /// <summary>
    /// Gets or sets the number of duplicate lines.
    /// </summary>
    public int LineCount { get; set; }

    /// <summary>
    /// Gets or sets the duplicate code snippet.
    /// </summary>
    public string CodeSnippet { get; set; } = "";

    /// <summary>
    /// Gets or sets the similarity percentage (0-100).
    /// </summary>
    public double SimilarityPercent { get; set; }
}

/// <summary>
/// A location in code.
/// </summary>
public class CodeLocation
{
    /// <summary>
    /// Gets or sets the start line.
    /// </summary>
    public int StartLine { get; set; }

    /// <summary>
    /// Gets or sets the end line.
    /// </summary>
    public int EndLine { get; set; }

    /// <summary>
    /// Gets or sets the file path.
    /// </summary>
    public string? FilePath { get; set; }
}

/// <summary>
/// A refactoring suggestion.
/// </summary>
public class RefactoringSuggestion
{
    /// <summary>
    /// Gets or sets the refactoring type.
    /// </summary>
    public RefactoringType Type { get; set; }

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Gets or sets the target location.
    /// </summary>
    public CodeLocation Location { get; set; } = new();

    /// <summary>
    /// Gets or sets the priority (1-5, 1 being highest).
    /// </summary>
    public int Priority { get; set; } = 3;

    /// <summary>
    /// Gets or sets the estimated effort.
    /// </summary>
    public RefactoringEffort Effort { get; set; }
}

/// <summary>
/// Types of refactoring.
/// </summary>
public enum RefactoringType
{
    /// <summary>Extract method.</summary>
    ExtractMethod,
    /// <summary>Extract variable.</summary>
    ExtractVariable,
    /// <summary>Inline variable.</summary>
    InlineVariable,
    /// <summary>Rename.</summary>
    Rename,
    /// <summary>Move to new file.</summary>
    MoveToFile,
    /// <summary>Extract class.</summary>
    ExtractClass,
    /// <summary>Simplify condition.</summary>
    SimplifyCondition,
    /// <summary>Replace magic number with constant.</summary>
    ReplaceMagicNumber,
    /// <summary>Introduce parameter object.</summary>
    IntroduceParameterObject,
    /// <summary>Remove dead code.</summary>
    RemoveDeadCode
}

/// <summary>
/// Effort level for refactoring.
/// </summary>
public enum RefactoringEffort
{
    /// <summary>Trivial change.</summary>
    Trivial,
    /// <summary>Small change.</summary>
    Small,
    /// <summary>Medium change.</summary>
    Medium,
    /// <summary>Large change.</summary>
    Large
}

/// <summary>
/// Event args for analysis events.
/// </summary>
public class AnalysisEventArgs : EventArgs
{
    /// <summary>
    /// Gets the file path being analyzed.
    /// </summary>
    public string? FilePath { get; }

    /// <summary>
    /// Gets the analysis result (if completed).
    /// </summary>
    public AnalysisResult? Result { get; }

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public AnalysisEventArgs(string? filePath = null, AnalysisResult? result = null)
    {
        FilePath = filePath;
        Result = result;
    }
}

/// <summary>
/// Event args for analysis progress.
/// </summary>
public class AnalysisProgressEventArgs : EventArgs
{
    /// <summary>
    /// Gets the current file being analyzed.
    /// </summary>
    public string CurrentFile { get; }

    /// <summary>
    /// Gets the number of files processed.
    /// </summary>
    public int FilesProcessed { get; }

    /// <summary>
    /// Gets the total number of files.
    /// </summary>
    public int TotalFiles { get; }

    /// <summary>
    /// Gets the number of issues found so far.
    /// </summary>
    public int IssuesFound { get; }

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public AnalysisProgressEventArgs(string currentFile, int filesProcessed, int totalFiles, int issuesFound)
    {
        CurrentFile = currentFile;
        FilesProcessed = filesProcessed;
        TotalFiles = totalFiles;
        IssuesFound = issuesFound;
    }
}
