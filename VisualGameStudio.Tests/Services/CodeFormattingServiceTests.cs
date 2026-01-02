using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

[TestFixture]
public class CodeFormattingServiceTests
{
    private CodeFormattingService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new CodeFormattingService();
    }

    #region FormatDocument Tests

    [Test]
    public void FormatDocument_EmptySource_ReturnsEmpty()
    {
        var result = _service.FormatDocument("");
        Assert.That(result, Is.EqualTo(""));
    }

    [Test]
    public void FormatDocument_NullSource_ReturnsNull()
    {
        var result = _service.FormatDocument(null!);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void FormatDocument_SimpleCode_FormatsCorrectly()
    {
        var source = @"Sub Test()
Print ""Hello""
End Sub";

        var result = _service.FormatDocument(source);

        Assert.That(result, Does.Contain("Sub Test()"));
        Assert.That(result, Does.Contain("    Print")); // Should be indented
        Assert.That(result, Does.Contain("End Sub"));
    }

    [Test]
    public void FormatDocument_IndentsNestedBlocks()
    {
        var source = @"Sub Test()
If x > 0 Then
Print ""positive""
End If
End Sub";

        var result = _service.FormatDocument(source);
        var lines = result.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        // Sub Test() - no indent
        Assert.That(lines[0].TrimStart(), Is.EqualTo("Sub Test()"));
        // If should be indented once
        Assert.That(lines[1], Does.StartWith("    If"));
        // Print should be indented twice
        Assert.That(lines[2], Does.StartWith("        Print"));
        // End If should be indented once
        Assert.That(lines[3], Does.StartWith("    End If"));
        // End Sub - no indent
        Assert.That(lines[4].TrimStart(), Is.EqualTo("End Sub"));
    }

    [Test]
    public void FormatDocument_HandlesForLoop()
    {
        var source = @"For i = 1 To 10
Print i
Next";

        var result = _service.FormatDocument(source);

        Assert.That(result, Does.Contain("For i = 1 To 10"));
        Assert.That(result, Does.Contain("    Print i")); // Should be indented
        Assert.That(result, Does.Contain("Next"));
    }

    [Test]
    public void FormatDocument_HandlesWhileLoop()
    {
        var source = @"While x > 0
x = x - 1
Wend";

        var result = _service.FormatDocument(source);
        var lines = result.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        Assert.That(lines[0].TrimStart(), Is.EqualTo("While x > 0"));
        Assert.That(lines[1], Does.StartWith("    x = x - 1"));
        Assert.That(lines[2].TrimStart(), Is.EqualTo("Wend"));
    }

    [Test]
    public void FormatDocument_HandlesClass()
    {
        var source = @"Class MyClass
Public Name As String
Sub New()
Name = """"
End Sub
End Class";

        var result = _service.FormatDocument(source);
        var lines = result.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        Assert.That(lines[0].TrimStart(), Is.EqualTo("Class MyClass"));
        Assert.That(lines[1], Does.StartWith("    Public Name"));
        Assert.That(lines[2], Does.StartWith("    Sub New()"));
        Assert.That(lines[3], Does.StartWith("        Name"));
        Assert.That(lines[4], Does.StartWith("    End Sub"));
        Assert.That(lines[5].TrimStart(), Is.EqualTo("End Class"));
    }

    [Test]
    public void FormatDocument_PreservesBlankLines()
    {
        var source = @"Dim x As Integer

x = 10";

        var result = _service.FormatDocument(source);
        var lines = result.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        Assert.That(lines.Length, Is.GreaterThanOrEqualTo(3));
        Assert.That(lines[1].Trim(), Is.EqualTo("")); // Blank line preserved
    }

    [Test]
    public void FormatDocument_HandlesSingleLineIf()
    {
        var source = @"If x > 0 Then Print ""positive""";

        var result = _service.FormatDocument(source);

        // Single line If should not increase indent for following lines
        Assert.That(result.Trim(), Is.EqualTo("If x > 0 Then Print \"positive\""));
    }

    [Test]
    public void FormatDocument_HandlesElseElseIf()
    {
        var source = @"If x > 0 Then
Print ""positive""
ElseIf x < 0 Then
Print ""negative""
Else
Print ""zero""
End If";

        var result = _service.FormatDocument(source);
        var lines = result.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        Assert.That(lines[0].TrimStart(), Is.EqualTo("If x > 0 Then"));
        Assert.That(lines[1], Does.StartWith("    Print \"positive\""));
        Assert.That(lines[2].TrimStart(), Is.EqualTo("ElseIf x < 0 Then"));
        Assert.That(lines[3], Does.StartWith("    Print \"negative\""));
        Assert.That(lines[4].TrimStart(), Is.EqualTo("Else"));
        Assert.That(lines[5], Does.StartWith("    Print \"zero\""));
    }

    [Test]
    public void FormatDocument_HandlesTryCatch()
    {
        var source = @"Try
DoSomething()
Catch
HandleError()
Finally
Cleanup()
End Try";

        var result = _service.FormatDocument(source);
        var lines = result.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        Assert.That(lines[0].TrimStart(), Is.EqualTo("Try"));
        Assert.That(lines[1], Does.StartWith("    DoSomething"));
        Assert.That(lines[2].TrimStart(), Is.EqualTo("Catch"));
        Assert.That(lines[3], Does.StartWith("    HandleError"));
        Assert.That(lines[4].TrimStart(), Is.EqualTo("Finally"));
        Assert.That(lines[5], Does.StartWith("    Cleanup"));
        Assert.That(lines[6].TrimStart(), Is.EqualTo("End Try"));
    }

    #endregion

    #region FormatSelection Tests

    [Test]
    public void FormatSelection_FormatsOnlySelectedLines()
    {
        var source = @"Dim x As Integer
Sub Test()
Print x
End Sub
Dim y As Integer";

        var result = _service.FormatSelection(source, 2, 4);
        var lines = result.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        // Line 1 should be unchanged
        Assert.That(lines[0], Is.EqualTo("Dim x As Integer"));
        // Lines 2-4 should be formatted
        Assert.That(lines[1].TrimStart(), Is.EqualTo("Sub Test()"));
    }

    #endregion

    #region FormatLine Tests

    [Test]
    public void FormatLine_AddsIndentation()
    {
        var result = _service.FormatLine("Print x", 2);

        Assert.That(result, Is.EqualTo("        Print x")); // 8 spaces (2 * 4)
    }

    [Test]
    public void FormatLine_EmptyLine_ReturnsEmpty()
    {
        var result = _service.FormatLine("", 2);

        Assert.That(result, Is.EqualTo(""));
    }

    [Test]
    public void FormatLine_WhitespaceOnly_ReturnsEmpty()
    {
        var result = _service.FormatLine("   ", 2);

        Assert.That(result, Is.EqualTo(""));
    }

    #endregion

    #region CalculateIndentLevel Tests

    [Test]
    public void CalculateIndentLevel_FirstLine_ReturnsZero()
    {
        var source = @"Sub Test()
Print x
End Sub";

        var result = _service.CalculateIndentLevel(source, 1);

        Assert.That(result, Is.EqualTo(0));
    }

    [Test]
    public void CalculateIndentLevel_InsideSub_ReturnsOne()
    {
        var source = @"Sub Test()
Print x
End Sub";

        var result = _service.CalculateIndentLevel(source, 2);

        Assert.That(result, Is.EqualTo(1));
    }

    [Test]
    public void CalculateIndentLevel_NestedBlock_ReturnsTwo()
    {
        var source = @"Sub Test()
If x > 0 Then
Print x
End If
End Sub";

        var result = _service.CalculateIndentLevel(source, 3);

        Assert.That(result, Is.EqualTo(2));
    }

    [Test]
    public void CalculateIndentLevel_AfterEndIf_ReturnsOne()
    {
        var source = @"Sub Test()
If x > 0 Then
Print x
End If
Print y
End Sub";

        var result = _service.CalculateIndentLevel(source, 5);

        Assert.That(result, Is.EqualTo(1));
    }

    #endregion

    #region RemoveTrailingWhitespace Tests

    [Test]
    public void RemoveTrailingWhitespace_RemovesSpaces()
    {
        var source = "Line 1   \nLine 2  \nLine 3";

        var result = _service.RemoveTrailingWhitespace(source);

        Assert.That(result, Does.Not.Contain("   "));
        Assert.That(result, Does.Contain("Line 1\n") | Does.Contain("Line 1\r\n"));
    }

    [Test]
    public void RemoveTrailingWhitespace_PreservesContent()
    {
        var source = "Line 1\nLine 2";

        var result = _service.RemoveTrailingWhitespace(source);

        Assert.That(result, Does.Contain("Line 1"));
        Assert.That(result, Does.Contain("Line 2"));
    }

    #endregion

    #region NormalizeLineEndings Tests

    [Test]
    public void NormalizeLineEndings_ToLF()
    {
        var source = "Line 1\r\nLine 2\rLine 3";

        var result = _service.NormalizeLineEndings(source, LineEndingStyle.LF);

        Assert.That(result, Is.EqualTo("Line 1\nLine 2\nLine 3"));
    }

    [Test]
    public void NormalizeLineEndings_ToCRLF()
    {
        var source = "Line 1\nLine 2\rLine 3";

        var result = _service.NormalizeLineEndings(source, LineEndingStyle.CRLF);

        Assert.That(result, Is.EqualTo("Line 1\r\nLine 2\r\nLine 3"));
    }

    #endregion

    #region ValidateFormatting Tests

    [Test]
    public void ValidateFormatting_NoIssues_ReturnsEmpty()
    {
        _service.Options.TrimTrailingWhitespace = false;
        _service.Options.InsertFinalNewline = false;
        _service.Options.MaxLineLength = 0;

        var source = @"Sub Test()
    Print ""Hello""
End Sub";

        var issues = _service.ValidateFormatting(source);

        // May have indentation issues, but no trailing whitespace
        var trailingIssues = issues.Where(i => i.Type == FormattingIssueType.TrailingWhitespace).ToList();
        Assert.That(trailingIssues, Is.Empty);
    }

    [Test]
    public void ValidateFormatting_TrailingWhitespace_ReportsIssue()
    {
        _service.Options.TrimTrailingWhitespace = true;

        var source = "Line 1   \nLine 2";

        var issues = _service.ValidateFormatting(source);

        Assert.That(issues.Any(i => i.Type == FormattingIssueType.TrailingWhitespace), Is.True);
        Assert.That(issues.First(i => i.Type == FormattingIssueType.TrailingWhitespace).Line, Is.EqualTo(1));
    }

    [Test]
    public void ValidateFormatting_LineTooLong_ReportsIssue()
    {
        _service.Options.MaxLineLength = 20;

        var source = "This line is way too long to be valid";

        var issues = _service.ValidateFormatting(source);

        Assert.That(issues.Any(i => i.Type == FormattingIssueType.LineTooLong), Is.True);
    }

    [Test]
    public void ValidateFormatting_MissingFinalNewline_ReportsIssue()
    {
        _service.Options.InsertFinalNewline = true;

        var source = "Line 1\nLine 2";

        var issues = _service.ValidateFormatting(source);

        Assert.That(issues.Any(i => i.Type == FormattingIssueType.MissingFinalNewline), Is.True);
    }

    [Test]
    public void ValidateFormatting_IncorrectIndentation_ReportsIssue()
    {
        var source = @"Sub Test()
Print ""Hello""
End Sub";

        var issues = _service.ValidateFormatting(source);

        Assert.That(issues.Any(i => i.Type == FormattingIssueType.Indentation), Is.True);
    }

    #endregion

    #region FormattingOptions Tests

    [Test]
    public void Options_UseTabs_FormatsWithTabs()
    {
        _service.Options.UseTabs = true;

        var source = @"Sub Test()
Print x
End Sub";

        var result = _service.FormatDocument(source);

        Assert.That(result, Does.Contain("\tPrint"));
    }

    [Test]
    public void Options_CustomIndentSize_FormatsWithCustomSize()
    {
        _service.Options.UseTabs = false;
        _service.Options.IndentSize = 2;

        var source = @"Sub Test()
Print x
End Sub";

        var result = _service.FormatDocument(source);

        Assert.That(result, Does.Contain("  Print")); // 2 spaces
        Assert.That(result, Does.Not.Contain("    Print")); // Not 4 spaces
    }

    [Test]
    public void Options_InsertFinalNewline_AddsNewline()
    {
        _service.Options.InsertFinalNewline = true;
        _service.Options.LineEnding = LineEndingStyle.LF;

        var source = "Line 1";

        var result = _service.FormatDocument(source);

        Assert.That(result, Does.EndWith("\n"));
    }

    [Test]
    public void Options_NoFinalNewline_RemovesTrailingNewline()
    {
        _service.Options.InsertFinalNewline = false;

        var source = "Line 1\n";

        var result = _service.FormatDocument(source);

        Assert.That(result, Does.Not.EndWith("\n"));
        Assert.That(result, Does.Not.EndWith("\r"));
    }

    #endregion
}

#region Model Tests

[TestFixture]
public class FormattingOptionsTests
{
    [Test]
    public void DefaultOptions_HasExpectedDefaults()
    {
        var options = new FormattingOptions();

        Assert.That(options.UseTabs, Is.False);
        Assert.That(options.IndentSize, Is.EqualTo(4));
        Assert.That(options.TabWidth, Is.EqualTo(4));
        Assert.That(options.TrimTrailingWhitespace, Is.True);
        Assert.That(options.InsertFinalNewline, Is.True);
        Assert.That(options.MaxLineLength, Is.EqualTo(120));
        Assert.That(options.LineEnding, Is.EqualTo(LineEndingStyle.SystemDefault));
        Assert.That(options.SpaceAfterKeywords, Is.True);
        Assert.That(options.SpaceAroundOperators, Is.True);
        Assert.That(options.SpaceAfterCommas, Is.True);
        Assert.That(options.AlignConsecutiveAssignments, Is.False);
        Assert.That(options.OpeningKeywordOnNewLine, Is.False);
    }

    [Test]
    public void Options_CanSetAllProperties()
    {
        var options = new FormattingOptions
        {
            UseTabs = true,
            IndentSize = 2,
            TabWidth = 8,
            TrimTrailingWhitespace = false,
            InsertFinalNewline = false,
            MaxLineLength = 80,
            LineEnding = LineEndingStyle.LF,
            SpaceAfterKeywords = false,
            SpaceAroundOperators = false,
            SpaceAfterCommas = false,
            AlignConsecutiveAssignments = true,
            OpeningKeywordOnNewLine = true
        };

        Assert.That(options.UseTabs, Is.True);
        Assert.That(options.IndentSize, Is.EqualTo(2));
        Assert.That(options.TabWidth, Is.EqualTo(8));
        Assert.That(options.TrimTrailingWhitespace, Is.False);
        Assert.That(options.InsertFinalNewline, Is.False);
        Assert.That(options.MaxLineLength, Is.EqualTo(80));
        Assert.That(options.LineEnding, Is.EqualTo(LineEndingStyle.LF));
        Assert.That(options.SpaceAfterKeywords, Is.False);
        Assert.That(options.SpaceAroundOperators, Is.False);
        Assert.That(options.SpaceAfterCommas, Is.False);
        Assert.That(options.AlignConsecutiveAssignments, Is.True);
        Assert.That(options.OpeningKeywordOnNewLine, Is.True);
    }
}

[TestFixture]
public class FormattingIssueTests
{
    [Test]
    public void DefaultIssue_HasExpectedDefaults()
    {
        var issue = new FormattingIssue();

        Assert.That(issue.Line, Is.EqualTo(0));
        Assert.That(issue.Column, Is.EqualTo(0));
        Assert.That(issue.Type, Is.EqualTo(FormattingIssueType.Indentation));
        Assert.That(issue.Message, Is.EqualTo(""));
        Assert.That(issue.SuggestedFix, Is.Null);
    }

    [Test]
    public void Issue_CanSetAllProperties()
    {
        var issue = new FormattingIssue
        {
            Line = 10,
            Column = 5,
            Type = FormattingIssueType.TrailingWhitespace,
            Message = "Test message",
            SuggestedFix = "Fixed line"
        };

        Assert.That(issue.Line, Is.EqualTo(10));
        Assert.That(issue.Column, Is.EqualTo(5));
        Assert.That(issue.Type, Is.EqualTo(FormattingIssueType.TrailingWhitespace));
        Assert.That(issue.Message, Is.EqualTo("Test message"));
        Assert.That(issue.SuggestedFix, Is.EqualTo("Fixed line"));
    }
}

[TestFixture]
public class LineEndingStyleTests
{
    [Test]
    public void AllStyleValues_AreDefined()
    {
        Assert.That(Enum.IsDefined(typeof(LineEndingStyle), LineEndingStyle.SystemDefault), Is.True);
        Assert.That(Enum.IsDefined(typeof(LineEndingStyle), LineEndingStyle.LF), Is.True);
        Assert.That(Enum.IsDefined(typeof(LineEndingStyle), LineEndingStyle.CRLF), Is.True);
        Assert.That(Enum.IsDefined(typeof(LineEndingStyle), LineEndingStyle.CR), Is.True);
    }
}

[TestFixture]
public class FormattingIssueTypeTests
{
    [Test]
    public void AllTypeValues_AreDefined()
    {
        Assert.That(Enum.IsDefined(typeof(FormattingIssueType), FormattingIssueType.Indentation), Is.True);
        Assert.That(Enum.IsDefined(typeof(FormattingIssueType), FormattingIssueType.TrailingWhitespace), Is.True);
        Assert.That(Enum.IsDefined(typeof(FormattingIssueType), FormattingIssueType.LineTooLong), Is.True);
        Assert.That(Enum.IsDefined(typeof(FormattingIssueType), FormattingIssueType.Spacing), Is.True);
        Assert.That(Enum.IsDefined(typeof(FormattingIssueType), FormattingIssueType.BlankLines), Is.True);
        Assert.That(Enum.IsDefined(typeof(FormattingIssueType), FormattingIssueType.LineEnding), Is.True);
        Assert.That(Enum.IsDefined(typeof(FormattingIssueType), FormattingIssueType.MissingFinalNewline), Is.True);
    }
}

#endregion
