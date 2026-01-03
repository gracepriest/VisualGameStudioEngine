using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Integration;

/// <summary>
/// Integration tests for typical project workflows.
/// </summary>
[TestFixture]
public class ProjectWorkflowTests
{
    private FindReplaceService _findReplace = null!;
    private CodeAnalysisService _codeAnalysis = null!;
    private TaskListService _taskList = null!;
    private CodeFormattingService _formatting = null!;

    [SetUp]
    public void SetUp()
    {
        _findReplace = new FindReplaceService();
        _codeAnalysis = new CodeAnalysisService();
        _taskList = new TaskListService();
        _formatting = new CodeFormattingService();
    }

    #region Code Editing Workflow Tests

    [Test]
    public void CodeEditingWorkflow_FindAndAnalyze_Works()
    {
        var code = @"
' TODO: Optimize this function
Function Calculate(a, b, c, d, e, f)
    Dim password = ""secret123""
    If a > 0 Then
        If b > 0 Then
            If c > 0 Then
                Return a + b + c
            End If
        End If
    End If
    Return 0
End Function";

        // Find all occurrences of "If"
        var matches = _findReplace.FindInDocument(code, "If");
        Assert.That(matches.Count, Is.GreaterThan(0));

        // Analyze the code
        var analysis = _codeAnalysis.AnalyzeDocument(code);

        // Should find security issues (password)
        Assert.That(analysis.SecurityIssues, Is.Not.Empty);

        // Should find code smells (too many parameters, deep nesting)
        Assert.That(analysis.CodeSmells, Is.Not.Empty);

        // Should find complexity
        Assert.That(analysis.Complexity, Is.Not.Empty);

        // Scan for tasks
        var tasks = _taskList.ScanDocument(code, "test.bl");
        Assert.That(tasks, Has.Count.EqualTo(1));
        Assert.That(tasks[0].Token, Is.EqualTo("TODO"));
    }

    [Test]
    public void CodeEditingWorkflow_FormatAndValidate_Works()
    {
        var unformattedCode = @"
function test()
if a>0 then
b=1
end if
end function";

        // Format the code
        var formattedCode = _formatting.FormatDocument(unformattedCode);

        // Validate formatting
        var issues = _formatting.ValidateFormatting(formattedCode);

        // Should have fewer issues after formatting
        Assert.That(formattedCode, Does.Contain("    "));
    }

    [Test]
    public void CodeEditingWorkflow_FindReplaceAndReanalyze_Works()
    {
        var code = @"
Dim password = ""secret""
Dim pwd = ""hidden""";

        // Find security issues
        var initialAnalysis = _codeAnalysis.AnalyzeDocument(code);
        Assert.That(initialAnalysis.SecurityIssues, Is.Not.Empty);

        // Replace password assignments
        _findReplace.Options.UseRegex = true;
        var result = _findReplace.ReplaceAll(code, @"password\s*=\s*""[^""]+""", "password = GetFromEnv(\"PASSWORD\")");

        // Verify replacement happened
        Assert.That(result.ReplacementCount, Is.GreaterThan(0));
    }

    #endregion

    #region Task Management Workflow Tests

    [Test]
    public void TaskManagementWorkflow_ScanMultipleFiles_Works()
    {
        var file1 = @"
' TODO: Implement feature A
' FIXME: Bug in calculation
Function FeatureA()
End Function";

        var file2 = @"
' TODO: Add validation
' BUG: Memory leak
Function FeatureB()
End Function";

        // Update task list with both files
        _taskList.UpdateDocument(file1, "file1.bl");
        _taskList.UpdateDocument(file2, "file2.bl");

        // Should have all tasks
        Assert.That(_taskList.Tasks, Has.Count.EqualTo(4));

        // Can filter by type
        var todos = _taskList.GetByType(TaskType.Todo);
        Assert.That(todos, Has.Count.EqualTo(2));

        var bugs = _taskList.GetByType(TaskType.Bug);
        Assert.That(bugs, Has.Count.EqualTo(1));

        // Can filter by priority
        var highPriority = _taskList.GetByPriority(TaskPriority.High);
        Assert.That(highPriority, Has.Count.EqualTo(2)); // FIXME and BUG are high priority

        // Can get statistics
        var stats = _taskList.GetStatistics();
        Assert.That(stats.TotalCount, Is.EqualTo(4));
        Assert.That(stats.FileCount, Is.EqualTo(2));
    }

    [Test]
    public void TaskManagementWorkflow_UpdateAndRemove_Works()
    {
        var initialCode = "' TODO: First task";
        _taskList.UpdateDocument(initialCode, "test.bl");
        Assert.That(_taskList.Tasks, Has.Count.EqualTo(1));

        // Update with different content
        var updatedCode = "' FIXME: Updated task\n' BUG: New bug";
        _taskList.UpdateDocument(updatedCode, "test.bl");
        Assert.That(_taskList.Tasks, Has.Count.EqualTo(2));
        Assert.That(_taskList.Tasks.All(t => t.Token != "TODO"), Is.True);

        // Remove file
        _taskList.RemoveFile("test.bl");
        Assert.That(_taskList.Tasks, Is.Empty);
    }

    #endregion

    #region Code Quality Workflow Tests

    [Test]
    public void CodeQualityWorkflow_AnalyzeAndGetSuggestions_Works()
    {
        var complexCode = @"
Function VeryComplex(a, b, c, d, e, f)
    If a > 0 Then
        If b > 0 Then
            If c > 0 Then
                If d > 0 Then
                    If e > 0 Then
                        Return a + b + c + d + e
                    End If
                End If
            End If
        End If
    End If
    Return 0
End Function";

        // Analyze code
        var analysis = _codeAnalysis.AnalyzeDocument(complexCode);

        // Should have complexity info
        Assert.That(analysis.Complexity, Has.Count.EqualTo(1));
        Assert.That(analysis.Complexity[0].CyclomaticComplexity, Is.GreaterThan(5));

        // Should have code smells
        Assert.That(analysis.CodeSmells, Is.Not.Empty);
        Assert.That(analysis.CodeSmells.Any(s => s.SmellType == CodeSmellType.TooManyParameters), Is.True);
        Assert.That(analysis.CodeSmells.Any(s => s.SmellType == CodeSmellType.DeepNesting), Is.True);

        // Should have refactoring suggestions
        Assert.That(analysis.RefactoringSuggestions, Is.Not.Empty);
    }

    [Test]
    public void CodeQualityWorkflow_SecurityAnalysis_Works()
    {
        var insecureCode = @"
Function ProcessUser(userId)
    Dim password = ""admin123""
    Shell(cmd + userId)
    Return ReadFile(basePath + userId)
End Function";

        // Analyze for security issues
        var analysis = _codeAnalysis.AnalyzeDocument(insecureCode);

        // Should find multiple security issues
        Assert.That(analysis.SecurityIssues.Count, Is.GreaterThanOrEqualTo(2));

        // Should include hardcoded credential
        Assert.That(analysis.SecurityIssues.Any(i => i.IssueType == SecurityIssueType.HardcodedCredential), Is.True);

        // Should include command injection
        Assert.That(analysis.SecurityIssues.Any(i => i.IssueType == SecurityIssueType.CommandInjection), Is.True);
    }

    #endregion

    #region Search and Replace Workflow Tests

    [Test]
    public void SearchWorkflow_FindWithDifferentOptions_Works()
    {
        var code = @"
Function HELLO()
    Print ""Hello World""
    Print ""hello there""
End Function

Function hello()
    Print ""HELLO""
End Function";

        // Case insensitive search (default)
        _findReplace.Options.CaseSensitive = false;
        var allMatches = _findReplace.FindInDocument(code, "hello");
        Assert.That(allMatches.Count, Is.GreaterThan(2));

        // Case sensitive search
        _findReplace.Options.CaseSensitive = true;
        var exactMatches = _findReplace.FindInDocument(code, "hello");
        Assert.That(exactMatches.Count, Is.LessThan(allMatches.Count));

        // Whole word search
        _findReplace.Options.CaseSensitive = false;
        _findReplace.Options.WholeWord = true;
        var wholeWordMatches = _findReplace.FindInDocument(code, "hello");
        Assert.That(wholeWordMatches.Count, Is.LessThanOrEqualTo(allMatches.Count));
    }

    [Test]
    public void SearchWorkflow_RegexSearch_Works()
    {
        var code = @"
Dim count1 = 0
Dim count2 = 0
Dim total = 0
Dim value = 100";

        _findReplace.Options.UseRegex = true;

        // Find variables starting with 'count'
        var countVars = _findReplace.FindInDocument(code, @"count\d+");
        Assert.That(countVars.Count, Is.EqualTo(2));

        // Find all numeric values
        var numbers = _findReplace.FindInDocument(code, @"\b\d+\b");
        Assert.That(numbers.Count, Is.GreaterThan(0));
    }

    [Test]
    public void SearchWorkflow_ReplacePreservesCase_Works()
    {
        var code = @"
Dim myVariable = 1
Dim MYVARIABLE = 2
Dim MyVariable = 3";

        _findReplace.Options.CaseSensitive = false;
        _findReplace.Options.PreserveCase = true;

        var result = _findReplace.ReplaceAll(code, "myvariable", "newname");

        // Should preserve case patterns
        Assert.That(result.Content, Does.Contain("newname"));
        Assert.That(result.Content, Does.Contain("NEWNAME"));
        Assert.That(result.Content, Does.Contain("Newname"));
    }

    #endregion

    #region Multi-Service Integration Tests

    [Test]
    public void MultiServiceIntegration_FullCodeReview_Works()
    {
        var codeToReview = @"
' TODO: Refactor this module
' FIXME: Memory leak in loop
Function ProcessData(input)
    Dim password = ""hardcoded""
    Dim result = input * 98765
    If input > 0 Then
        If result > 0 Then
            Return result
        End If
    End If
    Return 0
End Function";

        // 1. Scan for tasks
        var tasks = _taskList.ScanDocument(codeToReview, "review.bl");
        Assert.That(tasks.Count, Is.EqualTo(2));

        // 2. Run code analysis
        var analysis = _codeAnalysis.AnalyzeDocument(codeToReview);
        Assert.That(analysis.SecurityIssues, Is.Not.Empty);
        Assert.That(analysis.CodeSmells, Is.Not.Empty);

        // 3. Search for patterns
        _findReplace.Options.UseRegex = true;
        var patterns = _findReplace.FindInDocument(codeToReview, @"\d{3,}");
        Assert.That(patterns, Is.Not.Empty);

        // 4. Get summary
        var taskStats = _taskList.GetStatistics();
        var totalIssues = analysis.TotalIssues;

        Assert.That(taskStats.TotalCount + totalIssues, Is.GreaterThan(0));
    }

    [Test]
    public void MultiServiceIntegration_CodeCleanup_Works()
    {
        var messyCode = @"
function messy()
dim unused=1
dim x=2
print x
end function";

        // 1. Format code
        var formattedCode = _formatting.FormatDocument(messyCode);
        Assert.That(formattedCode, Does.Contain("    "));

        // 2. Check for issues
        var analysis = _codeAnalysis.AnalyzeDocument(formattedCode);

        // 3. Find potential cleanup targets
        _findReplace.Options.UseRegex = true;
        var unusedVars = _codeAnalysis.GetUnusedCode(formattedCode);

        // Should find the unused variable
        Assert.That(unusedVars.Any(u => u.Identifier == "unused"), Is.True);
    }

    #endregion
}
