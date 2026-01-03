using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

[TestFixture]
public class CodeAnalysisServiceTests
{
    private CodeAnalysisService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new CodeAnalysisService();
    }

    #region AnalyzeDocument Tests

    [Test]
    public void AnalyzeDocument_EmptyContent_ReturnsEmptyResult()
    {
        var result = _service.AnalyzeDocument("");

        Assert.That(result.TotalIssues, Is.EqualTo(0));
        Assert.That(result.Issues, Is.Empty);
    }

    [Test]
    public void AnalyzeDocument_SimpleCode_ReturnsResult()
    {
        var code = @"
Function Hello()
    Print ""Hello World""
End Function";

        var result = _service.AnalyzeDocument(code);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Complexity, Is.Not.Empty);
    }

    [Test]
    public void AnalyzeDocument_RecordsDuration()
    {
        var code = "Dim x = 1";
        var result = _service.AnalyzeDocument(code);

        Assert.That(result.Duration, Is.GreaterThan(TimeSpan.Zero));
    }

    [Test]
    public void AnalyzeDocument_RaisesEvents()
    {
        bool started = false;
        bool completed = false;
        _service.AnalysisStarted += (s, e) => started = true;
        _service.AnalysisCompleted += (s, e) => completed = true;

        _service.AnalyzeDocument("Dim x = 1");

        Assert.That(started, Is.True);
        Assert.That(completed, Is.True);
    }

    [Test]
    public void AnalyzeDocument_CompletedEventHasResult()
    {
        AnalysisResult? receivedResult = null;
        _service.AnalysisCompleted += (s, e) => receivedResult = e.Result;

        _service.AnalyzeDocument("Dim x = 1");

        Assert.That(receivedResult, Is.Not.Null);
    }

    #endregion

    #region GetCodeSmells Tests

    [Test]
    public void GetCodeSmells_EmptyContent_ReturnsEmpty()
    {
        var smells = _service.GetCodeSmells("");
        Assert.That(smells, Is.Empty);
    }

    [Test]
    public void GetCodeSmells_LongFunction_DetectsSmell()
    {
        _service.Options.MaxFunctionLength = 5;
        var code = @"
Function LongFunc()
    Dim a = 1
    Dim b = 2
    Dim c = 3
    Dim d = 4
    Dim e = 5
    Dim f = 6
    Dim g = 7
End Function";

        var smells = _service.GetCodeSmells(code);

        Assert.That(smells, Has.Some.Matches<CodeSmell>(s => s.SmellType == CodeSmellType.LongFunction));
    }

    [Test]
    public void GetCodeSmells_TooManyParameters_DetectsSmell()
    {
        var code = @"
Function TooManyParams(a, b, c, d, e, f)
    Return a + b + c + d + e + f
End Function";

        var smells = _service.GetCodeSmells(code);

        Assert.That(smells, Has.Some.Matches<CodeSmell>(s => s.SmellType == CodeSmellType.TooManyParameters));
    }

    [Test]
    public void GetCodeSmells_DeepNesting_DetectsSmell()
    {
        _service.Options.MaxNestingDepth = 2;
        var code = @"
Function DeepNest()
    If a Then
        If b Then
            If c Then
                Print ""Too deep""
            End If
        End If
    End If
End Function";

        var smells = _service.GetCodeSmells(code);

        Assert.That(smells, Has.Some.Matches<CodeSmell>(s => s.SmellType == CodeSmellType.DeepNesting));
    }

    [Test]
    public void GetCodeSmells_MagicNumber_DetectsSmell()
    {
        var code = @"
Function Calculate()
    Dim result = value * 3.14159
    Return result + 12345
End Function";

        var smells = _service.GetCodeSmells(code);

        Assert.That(smells, Has.Some.Matches<CodeSmell>(s => s.SmellType == CodeSmellType.MagicNumber));
    }

    [Test]
    public void GetCodeSmells_EmptyCatch_DetectsSmell()
    {
        var code = @"
Function Handler()
    Try
        DoSomething()
    Catch
    End Try
End Function";

        var smells = _service.GetCodeSmells(code);

        Assert.That(smells, Has.Some.Matches<CodeSmell>(s => s.SmellType == CodeSmellType.EmptyCatch));
    }

    [Test]
    public void GetCodeSmells_AllowedMagicNumbers_NotReported()
    {
        var code = @"
Function Simple()
    Dim x = 0
    Dim y = 1
    Dim z = -1
End Function";

        var smells = _service.GetCodeSmells(code);

        Assert.That(smells.Where(s => s.SmellType == CodeSmellType.MagicNumber), Is.Empty);
    }

    #endregion

    #region GetSecurityIssues Tests

    [Test]
    public void GetSecurityIssues_EmptyContent_ReturnsEmpty()
    {
        var issues = _service.GetSecurityIssues("");
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void GetSecurityIssues_HardcodedPassword_DetectsIssue()
    {
        var code = @"
Dim password = ""secret123""
Dim apiKey = ""abc123xyz""";

        var issues = _service.GetSecurityIssues(code);

        Assert.That(issues, Has.Some.Matches<SecurityIssue>(i => i.IssueType == SecurityIssueType.HardcodedCredential));
    }

    [Test]
    public void GetSecurityIssues_CommandInjection_DetectsIssue()
    {
        var code = @"
Function RunCommand(userInput)
    Shell(cmd + userInput)
End Function";

        var issues = _service.GetSecurityIssues(code);

        Assert.That(issues, Has.Some.Matches<SecurityIssue>(i => i.IssueType == SecurityIssueType.CommandInjection));
    }

    [Test]
    public void GetSecurityIssues_PathTraversal_DetectsIssue()
    {
        var code = @"
Function ReadUserFile(fileName)
    Return ReadFile(basePath + fileName)
End Function";

        var issues = _service.GetSecurityIssues(code);

        Assert.That(issues, Has.Some.Matches<SecurityIssue>(i => i.IssueType == SecurityIssueType.PathTraversal));
    }

    [Test]
    public void GetSecurityIssues_HasCweId()
    {
        var code = @"Dim password = ""secret123""";
        var issues = _service.GetSecurityIssues(code);

        Assert.That(issues, Has.Some.Matches<SecurityIssue>(i => !string.IsNullOrEmpty(i.CweId)));
    }

    [Test]
    public void GetSecurityIssues_HasRemediation()
    {
        var code = @"Dim password = ""secret123""";
        var issues = _service.GetSecurityIssues(code);

        Assert.That(issues, Has.Some.Matches<SecurityIssue>(i => !string.IsNullOrEmpty(i.Remediation)));
    }

    #endregion

    #region GetUnusedCode Tests

    [Test]
    public void GetUnusedCode_EmptyContent_ReturnsEmpty()
    {
        var unused = _service.GetUnusedCode("");
        Assert.That(unused, Is.Empty);
    }

    [Test]
    public void GetUnusedCode_UnusedVariable_DetectsIssue()
    {
        var code = @"
Function Test()
    Dim unusedVar = 42
    Print ""Hello""
End Function";

        var unused = _service.GetUnusedCode(code);

        Assert.That(unused, Has.Some.Matches<UnusedCode>(u => u.Identifier == "unusedVar"));
    }

    [Test]
    public void GetUnusedCode_UsedVariable_NotReported()
    {
        var code = @"
Function Test()
    Dim usedVar = 42
    Print usedVar
End Function";

        var unused = _service.GetUnusedCode(code);

        Assert.That(unused.Where(u => u.Identifier == "usedVar"), Is.Empty);
    }

    [Test]
    public void GetUnusedCode_HasCorrectType()
    {
        var code = @"Dim unusedVar = 42";
        var unused = _service.GetUnusedCode(code);

        Assert.That(unused, Has.Some.Matches<UnusedCode>(u => u.CodeType == UnusedCodeType.Variable));
    }

    #endregion

    #region GetComplexity Tests

    [Test]
    public void GetComplexity_EmptyContent_ReturnsEmpty()
    {
        var complexity = _service.GetComplexity("");
        Assert.That(complexity, Is.Empty);
    }

    [Test]
    public void GetComplexity_SimpleFunction_LowComplexity()
    {
        var code = @"
Function Simple()
    Return 1
End Function";

        var complexity = _service.GetComplexity(code);

        Assert.That(complexity, Has.Count.EqualTo(1));
        Assert.That(complexity[0].FunctionName, Is.EqualTo("Simple"));
        Assert.That(complexity[0].CyclomaticComplexity, Is.LessThan(5));
    }

    [Test]
    public void GetComplexity_ComplexFunction_HighComplexity()
    {
        var code = @"
Function Complex(a, b, c)
    If a > 0 Then
        If b > 0 Then
            Return 1
        ElseIf c > 0 Then
            Return 2
        End If
    ElseIf b < 0 Then
        For i = 1 To 10
            If i > 5 And a > b Then
                Return i
            End If
        Next
    End If
    While a > 0
        a = a - 1
    Loop
    Return 0
End Function";

        var complexity = _service.GetComplexity(code);

        Assert.That(complexity, Has.Count.EqualTo(1));
        Assert.That(complexity[0].CyclomaticComplexity, Is.GreaterThan(5));
    }

    [Test]
    public void GetComplexity_CountsParameters()
    {
        var code = @"
Function WithParams(a, b, c, d)
    Return a + b + c + d
End Function";

        var complexity = _service.GetComplexity(code);

        Assert.That(complexity[0].ParameterCount, Is.EqualTo(4));
    }

    [Test]
    public void GetComplexity_TracksLineCount()
    {
        var code = @"
Function MultiLine()
    Dim a = 1
    Dim b = 2
    Dim c = 3
    Return a + b + c
End Function";

        var complexity = _service.GetComplexity(code);

        Assert.That(complexity[0].LineCount, Is.GreaterThan(1));
    }

    [Test]
    public void GetComplexity_MultipleFunctions_ReturnsAll()
    {
        var code = @"
Function First()
    Return 1
End Function

Function Second()
    Return 2
End Function";

        var complexity = _service.GetComplexity(code);

        Assert.That(complexity, Has.Count.EqualTo(2));
    }

    [Test]
    public void GetComplexity_IsComplex_SetCorrectly()
    {
        var code = @"
Function VeryComplex(a)
    If a > 0 Then
        If a > 1 Then
            If a > 2 Then
                If a > 3 Then
                    If a > 4 Then
                        If a > 5 Then
                            If a > 6 Then
                                If a > 7 Then
                                    If a > 8 Then
                                        If a > 9 Then
                                            Return 10
                                        End If
                                    End If
                                End If
                            End If
                        End If
                    End If
                End If
            End If
        End If
    End If
    Return 0
End Function";

        var complexity = _service.GetComplexity(code);

        Assert.That(complexity[0].IsComplex, Is.True);
    }

    #endregion

    #region GetDuplicates Tests

    [Test]
    public void GetDuplicates_EmptyContent_ReturnsEmpty()
    {
        var duplicates = _service.GetDuplicates("");
        Assert.That(duplicates, Is.Empty);
    }

    [Test]
    public void GetDuplicates_NoDuplicates_ReturnsEmpty()
    {
        var code = @"
Dim a = 1
Dim b = 2
Dim c = 3";

        var duplicates = _service.GetDuplicates(code);
        Assert.That(duplicates, Is.Empty);
    }

    [Test]
    public void GetDuplicates_WithDuplicates_DetectsDuplication()
    {
        _service.Options.MinDuplicateLines = 3;
        var code = @"
Dim a = 1
Dim b = 2
Dim c = 3
Dim d = 4
Dim e = 5

' Some other code

Dim a = 1
Dim b = 2
Dim c = 3
Dim d = 4
Dim e = 5";

        var duplicates = _service.GetDuplicates(code);

        Assert.That(duplicates, Is.Not.Empty);
    }

    [Test]
    public void GetDuplicates_ReportsLineCount()
    {
        _service.Options.MinDuplicateLines = 2;
        var code = @"
Print ""Hello""
Print ""World""
' gap
Print ""Hello""
Print ""World""";

        var duplicates = _service.GetDuplicates(code);

        if (duplicates.Any())
        {
            Assert.That(duplicates[0].LineCount, Is.GreaterThanOrEqualTo(2));
        }
    }

    #endregion

    #region GetRefactoringSuggestions Tests

    [Test]
    public void GetRefactoringSuggestions_EmptyContent_ReturnsEmpty()
    {
        var suggestions = _service.GetRefactoringSuggestions("");
        Assert.That(suggestions, Is.Empty);
    }

    [Test]
    public void GetRefactoringSuggestions_ComplexFunction_SuggestsExtraction()
    {
        _service.Options.ComplexityThreshold = 3;
        var code = @"
Function Complex(a, b)
    If a > 0 Then
        If b > 0 Then
            If a > b Then
                Return 1
            ElseIf a < b Then
                Return -1
            Else
                Return 0
            End If
        End If
    End If
    Return 0
End Function";

        var suggestions = _service.GetRefactoringSuggestions(code);

        Assert.That(suggestions, Has.Some.Matches<RefactoringSuggestion>(s => s.Type == RefactoringType.ExtractMethod));
    }

    [Test]
    public void GetRefactoringSuggestions_ManyParameters_SuggestsParameterObject()
    {
        var code = @"
Function TooManyParams(a, b, c, d, e, f)
    Return a + b + c + d + e + f
End Function";

        var suggestions = _service.GetRefactoringSuggestions(code);

        Assert.That(suggestions, Has.Some.Matches<RefactoringSuggestion>(s =>
            s.Type == RefactoringType.IntroduceParameterObject));
    }

    [Test]
    public void GetRefactoringSuggestions_MagicNumbers_SuggestsReplacement()
    {
        var code = @"
Function Calculate()
    Return value * 98765
End Function";

        var suggestions = _service.GetRefactoringSuggestions(code);

        Assert.That(suggestions, Has.Some.Matches<RefactoringSuggestion>(s =>
            s.Type == RefactoringType.ReplaceMagicNumber));
    }

    #endregion

    #region AnalyzeFilesAsync Tests

    [Test]
    public async Task AnalyzeFilesAsync_EmptyList_ReturnsEmpty()
    {
        var results = await _service.AnalyzeFilesAsync(Array.Empty<string>());
        Assert.That(results, Is.Empty);
    }

    [Test]
    public async Task AnalyzeFilesAsync_NonExistentFile_ReportsError()
    {
        var results = await _service.AnalyzeFilesAsync(new[] { "nonexistent.bl" });

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Success, Is.False);
        Assert.That(results[0].Error, Is.Not.Null);
    }

    [Test]
    public async Task AnalyzeFilesAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _service.AnalyzeFilesAsync(new[] { "test.bl" }, cts.Token));
    }

    [Test]
    public async Task AnalyzeFilesAsync_RaisesProgressEvent()
    {
        var progressCalled = false;
        _service.AnalysisProgress += (s, e) => progressCalled = true;

        await _service.AnalyzeFilesAsync(new[] { "nonexistent.bl" });

        Assert.That(progressCalled, Is.True);
    }

    #endregion

    #region Options Tests

    [Test]
    public void Options_Defaults_AreCorrect()
    {
        var options = new AnalysisOptions();

        Assert.That(options.CheckCodeSmells, Is.True);
        Assert.That(options.CheckSecurity, Is.True);
        Assert.That(options.CheckUnusedCode, Is.True);
        Assert.That(options.CalculateComplexity, Is.True);
        Assert.That(options.DetectDuplicates, Is.True);
        Assert.That(options.MaxFunctionLength, Is.EqualTo(50));
        Assert.That(options.MaxNestingDepth, Is.EqualTo(4));
        Assert.That(options.ComplexityThreshold, Is.EqualTo(10));
        Assert.That(options.MinDuplicateLines, Is.EqualTo(5));
        Assert.That(options.MinSeverity, Is.EqualTo(IssueSeverity.Info));
    }

    [Test]
    public void Options_DisableChecks_SkipsAnalysis()
    {
        _service.Options = new AnalysisOptions
        {
            CheckCodeSmells = false,
            CheckSecurity = false,
            CheckUnusedCode = false,
            CalculateComplexity = false,
            DetectDuplicates = false
        };

        var code = @"
Function Long()
    Dim password = ""secret""
    Dim unused = 1
End Function";

        var result = _service.AnalyzeDocument(code);

        Assert.That(result.CodeSmells, Is.Empty);
        Assert.That(result.SecurityIssues, Is.Empty);
        Assert.That(result.UnusedCode, Is.Empty);
        Assert.That(result.Complexity, Is.Empty);
        Assert.That(result.Duplicates, Is.Empty);
    }

    [Test]
    public void Options_MinSeverity_FiltersIssues()
    {
        _service.Options.MinSeverity = IssueSeverity.Warning;

        var code = @"
Function Test()
    Dim result = value * 12345
End Function";

        var result = _service.AnalyzeDocument(code);

        // Magic numbers are Info level, should be filtered
        Assert.That(result.Issues.Where(i => i.Severity < IssueSeverity.Warning), Is.Empty);
    }

    #endregion

    #region Model Tests

    [Test]
    public void AnalysisResult_TotalIssues_SumsAllTypes()
    {
        var result = new AnalysisResult
        {
            Issues = new List<CodeIssue> { new CodeIssue(), new CodeIssue() },
            CodeSmells = new List<CodeSmell> { new CodeSmell() },
            SecurityIssues = new List<SecurityIssue> { new SecurityIssue(), new SecurityIssue() },
            UnusedCode = new List<UnusedCode> { new UnusedCode() }
        };

        Assert.That(result.TotalIssues, Is.EqualTo(6));
    }

    [Test]
    public void FileAnalysisResult_Defaults_AreCorrect()
    {
        var result = new FileAnalysisResult();

        Assert.That(result.FilePath, Is.Empty);
        Assert.That(result.FileName, Is.Empty);
        Assert.That(result.Success, Is.True);
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public void CodeIssue_Defaults_AreCorrect()
    {
        var issue = new CodeIssue();

        Assert.That(issue.Id, Is.Empty);
        Assert.That(issue.Message, Is.Empty);
        Assert.That(issue.Category, Is.Empty);
        Assert.That(issue.Line, Is.EqualTo(0));
        Assert.That(issue.Column, Is.EqualTo(0));
        Assert.That(issue.Severity, Is.EqualTo(IssueSeverity.Info));
    }

    [Test]
    public void ComplexityInfo_IsComplex_Threshold()
    {
        var low = new ComplexityInfo { CyclomaticComplexity = 5 };
        var high = new ComplexityInfo { CyclomaticComplexity = 15 };

        Assert.That(low.IsComplex, Is.False);
        Assert.That(high.IsComplex, Is.True);
    }

    [Test]
    public void IssueSeverity_HasCorrectValues()
    {
        Assert.That((int)IssueSeverity.Info, Is.EqualTo(0));
        Assert.That((int)IssueSeverity.Warning, Is.EqualTo(1));
        Assert.That((int)IssueSeverity.Error, Is.EqualTo(2));
        Assert.That((int)IssueSeverity.Critical, Is.EqualTo(3));
    }

    [Test]
    public void CodeSmellType_HasExpectedValues()
    {
        var values = Enum.GetValues<CodeSmellType>();
        Assert.That(values, Does.Contain(CodeSmellType.LongFunction));
        Assert.That(values, Does.Contain(CodeSmellType.TooManyParameters));
        Assert.That(values, Does.Contain(CodeSmellType.DeepNesting));
        Assert.That(values, Does.Contain(CodeSmellType.MagicNumber));
        Assert.That(values, Does.Contain(CodeSmellType.EmptyCatch));
    }

    [Test]
    public void SecurityIssueType_HasExpectedValues()
    {
        var values = Enum.GetValues<SecurityIssueType>();
        Assert.That(values, Does.Contain(SecurityIssueType.HardcodedCredential));
        Assert.That(values, Does.Contain(SecurityIssueType.CommandInjection));
        Assert.That(values, Does.Contain(SecurityIssueType.PathTraversal));
    }

    [Test]
    public void RefactoringType_HasExpectedValues()
    {
        var values = Enum.GetValues<RefactoringType>();
        Assert.That(values, Does.Contain(RefactoringType.ExtractMethod));
        Assert.That(values, Does.Contain(RefactoringType.ExtractVariable));
        Assert.That(values, Does.Contain(RefactoringType.Rename));
    }

    #endregion

    #region Event Args Tests

    [Test]
    public void AnalysisEventArgs_StoresValues()
    {
        var result = new AnalysisResult();
        var args = new AnalysisEventArgs("test.bl", result);

        Assert.That(args.FilePath, Is.EqualTo("test.bl"));
        Assert.That(args.Result, Is.SameAs(result));
    }

    [Test]
    public void AnalysisProgressEventArgs_StoresValues()
    {
        var args = new AnalysisProgressEventArgs("file.bl", 5, 10, 15);

        Assert.That(args.CurrentFile, Is.EqualTo("file.bl"));
        Assert.That(args.FilesProcessed, Is.EqualTo(5));
        Assert.That(args.TotalFiles, Is.EqualTo(10));
        Assert.That(args.IssuesFound, Is.EqualTo(15));
    }

    #endregion
}
