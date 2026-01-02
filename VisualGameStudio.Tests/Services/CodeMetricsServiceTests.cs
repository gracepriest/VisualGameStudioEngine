using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

[TestFixture]
public class CodeMetricsServiceTests
{
    private CodeMetricsService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new CodeMetricsService();
    }

    #region CalculateMetrics Tests

    [Test]
    public void CalculateMetrics_EmptySource_ReturnsZeroMetrics()
    {
        var metrics = _service.CalculateMetrics("");

        Assert.That(metrics.TotalLines, Is.EqualTo(0));
        Assert.That(metrics.CodeLines, Is.EqualTo(0));
        Assert.That(metrics.CommentLines, Is.EqualTo(0));
        Assert.That(metrics.BlankLines, Is.EqualTo(0));
    }

    [Test]
    public void CalculateMetrics_NullSource_ReturnsZeroMetrics()
    {
        var metrics = _service.CalculateMetrics(null!);

        Assert.That(metrics.TotalLines, Is.EqualTo(0));
    }

    [Test]
    public void CalculateMetrics_CountsTotalLines()
    {
        var source = @"Line 1
Line 2
Line 3";

        var metrics = _service.CalculateMetrics(source);

        Assert.That(metrics.TotalLines, Is.EqualTo(3));
    }

    [Test]
    public void CalculateMetrics_CountsBlankLines()
    {
        var source = @"Line 1

Line 3

Line 5";

        var metrics = _service.CalculateMetrics(source);

        Assert.That(metrics.BlankLines, Is.EqualTo(2));
    }

    [Test]
    public void CalculateMetrics_CountsCommentLines()
    {
        var source = @"' This is a comment
Dim x As Integer
' Another comment
REM Old style comment";

        var metrics = _service.CalculateMetrics(source);

        Assert.That(metrics.CommentLines, Is.EqualTo(3));
    }

    [Test]
    public void CalculateMetrics_CountsClasses()
    {
        var source = @"Class MyClass
End Class

Public Class AnotherClass
End Class";

        var metrics = _service.CalculateMetrics(source);

        Assert.That(metrics.ClassCount, Is.EqualTo(2));
    }

    [Test]
    public void CalculateMetrics_CountsModules()
    {
        var source = @"Module MyModule
End Module

Module AnotherModule
End Module";

        var metrics = _service.CalculateMetrics(source);

        Assert.That(metrics.ClassCount, Is.EqualTo(2)); // Modules counted as classes
    }

    [Test]
    public void CalculateMetrics_CountsMethods()
    {
        var source = @"Sub MySub()
End Sub

Function MyFunction() As Integer
End Function

Public Sub PublicSub()
End Sub

Private Function PrivateFunction() As String
End Function";

        var metrics = _service.CalculateMetrics(source);

        Assert.That(metrics.MethodCount, Is.EqualTo(4));
    }

    [Test]
    public void CalculateMetrics_CalculatesCodeLines()
    {
        var source = @"' Comment
Dim x As Integer

x = 10
' Another comment
Print x";

        var metrics = _service.CalculateMetrics(source);

        Assert.That(metrics.CodeLines, Is.EqualTo(3)); // 6 total - 2 comments - 1 blank = 3
    }

    [Test]
    public void CalculateMetrics_CalculatesAverageComplexity()
    {
        var source = @"Sub Simple()
    Dim x = 1
End Sub

Sub Complex()
    If x > 0 Then
        If y > 0 Then
            Print ""nested""
        End If
    End If
End Sub";

        var metrics = _service.CalculateMetrics(source);

        Assert.That(metrics.AverageCyclomaticComplexity, Is.GreaterThan(1));
    }

    [Test]
    public void CalculateMetrics_CalculatesMaxNestingDepth()
    {
        var source = @"Sub Nested()
    If x > 0 Then
        If y > 0 Then
            For i = 1 To 10
                Print i
            Next
        End If
    End If
End Sub";

        var metrics = _service.CalculateMetrics(source);

        Assert.That(metrics.MaxNestingDepth, Is.EqualTo(3)); // If > If > For
    }

    [Test]
    public void CalculateMetrics_MaintainabilityIndex_InRange()
    {
        var source = @"Sub Test()
    Dim x As Integer
    x = 10
    Print x
End Sub";

        var metrics = _service.CalculateMetrics(source);

        Assert.That(metrics.MaintainabilityIndex, Is.InRange(0, 100));
    }

    [Test]
    public void CalculateMetrics_SimpleCode_HighMaintainability()
    {
        var source = @"Sub Simple()
    Print ""Hello""
End Sub";

        var metrics = _service.CalculateMetrics(source);

        Assert.That(metrics.MaintainabilityIndex, Is.GreaterThanOrEqualTo(50));
    }

    #endregion

    #region CalculateMethodMetrics Tests

    [Test]
    public void CalculateMethodMetrics_EmptyMethod_ReturnsEmptyMetrics()
    {
        var metrics = _service.CalculateMethodMetrics("");

        Assert.That(metrics.Name, Is.EqualTo(""));
        Assert.That(metrics.LineCount, Is.EqualTo(0));
    }

    [Test]
    public void CalculateMethodMetrics_ExtractsMethodName()
    {
        var method = @"Sub MyMethod()
    Print ""Hello""
End Sub";

        var metrics = _service.CalculateMethodMetrics(method);

        Assert.That(metrics.Name, Does.StartWith("MyMethod"));
    }

    [Test]
    public void CalculateMethodMetrics_CountsLines()
    {
        var method = @"Sub Test()
    Dim x As Integer
    x = 10
    Print x
End Sub";

        var metrics = _service.CalculateMethodMetrics(method);

        Assert.That(metrics.LineCount, Is.EqualTo(5));
    }

    [Test]
    public void CalculateMethodMetrics_CountsLocalVariables()
    {
        var method = @"Sub Test()
    Dim x As Integer
    Dim y As String
    Dim z As Double
End Sub";

        var metrics = _service.CalculateMethodMetrics(method);

        Assert.That(metrics.LocalVariableCount, Is.EqualTo(3));
    }

    [Test]
    public void CalculateMethodMetrics_CalculatesCyclomaticComplexity()
    {
        var method = @"Sub Complex()
    If x > 0 Then
        Print ""positive""
    ElseIf x < 0 Then
        Print ""negative""
    Else
        Print ""zero""
    End If
End Sub";

        var metrics = _service.CalculateMethodMetrics(method);

        // 1 (base) + 1 (If) + 1 (ElseIf) = 3
        Assert.That(metrics.CyclomaticComplexity, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public void CalculateMethodMetrics_CountsParameters()
    {
        var method = @"Sub TestWithParams(x As Integer, y As String, z As Double)
    Print x & y & z
End Sub";

        var metrics = _service.CalculateMethodMetrics(method);

        Assert.That(metrics.ParameterCount, Is.EqualTo(3));
    }

    [Test]
    public void CalculateMethodMetrics_ZeroParameters()
    {
        var method = @"Sub NoParams()
    Print ""No params""
End Sub";

        var metrics = _service.CalculateMethodMetrics(method);

        Assert.That(metrics.ParameterCount, Is.EqualTo(0));
    }

    [Test]
    public void CalculateMethodMetrics_CalculatesNestingDepth()
    {
        var method = @"Sub Nested()
    If x > 0 Then
        For i = 1 To 10
            While y > 0
                Print i
            Wend
        Next
    End If
End Sub";

        var metrics = _service.CalculateMethodMetrics(method);

        Assert.That(metrics.NestingDepth, Is.EqualTo(3));
    }

    #endregion

    #region ComplexityRating Tests

    [Test]
    public void ComplexityRating_Simple()
    {
        var method = @"Sub Simple()
    Dim x = 1
End Sub";

        var metrics = _service.CalculateMethodMetrics(method);

        Assert.That(metrics.Rating, Is.EqualTo(ComplexityRating.Simple));
    }

    [Test]
    public void ComplexityRating_Moderate()
    {
        var method = @"Sub Moderate()
    If a Then Print 1
    If b Then Print 2
    If c Then Print 3
    If d Then Print 4
    If e Then Print 5
    If f Then Print 6
End Sub";

        var metrics = _service.CalculateMethodMetrics(method);

        Assert.That(metrics.Rating, Is.EqualTo(ComplexityRating.Moderate));
    }

    #endregion
}

#region Model Tests

[TestFixture]
public class CodeMetricsModelTests
{
    [Test]
    public void DefaultCodeMetrics_HasZeroValues()
    {
        var metrics = new CodeMetrics();

        Assert.That(metrics.TotalLines, Is.EqualTo(0));
        Assert.That(metrics.CodeLines, Is.EqualTo(0));
        Assert.That(metrics.CommentLines, Is.EqualTo(0));
        Assert.That(metrics.BlankLines, Is.EqualTo(0));
        Assert.That(metrics.ClassCount, Is.EqualTo(0));
        Assert.That(metrics.MethodCount, Is.EqualTo(0));
        Assert.That(metrics.AverageCyclomaticComplexity, Is.EqualTo(0));
        Assert.That(metrics.MaxNestingDepth, Is.EqualTo(0));
        Assert.That(metrics.MaintainabilityIndex, Is.EqualTo(0));
    }

    [Test]
    public void CodeMetrics_CanSetAllProperties()
    {
        var metrics = new CodeMetrics
        {
            TotalLines = 100,
            CodeLines = 80,
            CommentLines = 10,
            BlankLines = 10,
            ClassCount = 2,
            MethodCount = 10,
            AverageCyclomaticComplexity = 5.5,
            MaxNestingDepth = 4,
            MaintainabilityIndex = 75
        };

        Assert.That(metrics.TotalLines, Is.EqualTo(100));
        Assert.That(metrics.CodeLines, Is.EqualTo(80));
        Assert.That(metrics.CommentLines, Is.EqualTo(10));
        Assert.That(metrics.BlankLines, Is.EqualTo(10));
        Assert.That(metrics.ClassCount, Is.EqualTo(2));
        Assert.That(metrics.MethodCount, Is.EqualTo(10));
        Assert.That(metrics.AverageCyclomaticComplexity, Is.EqualTo(5.5));
        Assert.That(metrics.MaxNestingDepth, Is.EqualTo(4));
        Assert.That(metrics.MaintainabilityIndex, Is.EqualTo(75));
    }
}

[TestFixture]
public class MethodMetricsModelTests
{
    [Test]
    public void DefaultMethodMetrics_HasDefaultValues()
    {
        var metrics = new MethodMetrics();

        Assert.That(metrics.Name, Is.EqualTo(""));
        Assert.That(metrics.StartLine, Is.EqualTo(0));
        Assert.That(metrics.LineCount, Is.EqualTo(0));
        Assert.That(metrics.ParameterCount, Is.EqualTo(0));
        Assert.That(metrics.CyclomaticComplexity, Is.EqualTo(0));
        Assert.That(metrics.NestingDepth, Is.EqualTo(0));
        Assert.That(metrics.LocalVariableCount, Is.EqualTo(0));
    }

    [Test]
    public void MethodMetrics_CanSetAllProperties()
    {
        var metrics = new MethodMetrics
        {
            Name = "TestMethod",
            StartLine = 10,
            LineCount = 50,
            ParameterCount = 3,
            CyclomaticComplexity = 8,
            NestingDepth = 3,
            LocalVariableCount = 5
        };

        Assert.That(metrics.Name, Is.EqualTo("TestMethod"));
        Assert.That(metrics.StartLine, Is.EqualTo(10));
        Assert.That(metrics.LineCount, Is.EqualTo(50));
        Assert.That(metrics.ParameterCount, Is.EqualTo(3));
        Assert.That(metrics.CyclomaticComplexity, Is.EqualTo(8));
        Assert.That(metrics.NestingDepth, Is.EqualTo(3));
        Assert.That(metrics.LocalVariableCount, Is.EqualTo(5));
    }

    [Test]
    public void Rating_ReturnsSimple_WhenComplexityIs5OrLess()
    {
        var metrics = new MethodMetrics { CyclomaticComplexity = 5 };
        Assert.That(metrics.Rating, Is.EqualTo(ComplexityRating.Simple));

        metrics.CyclomaticComplexity = 1;
        Assert.That(metrics.Rating, Is.EqualTo(ComplexityRating.Simple));
    }

    [Test]
    public void Rating_ReturnsModerate_WhenComplexityIs6To10()
    {
        var metrics = new MethodMetrics { CyclomaticComplexity = 6 };
        Assert.That(metrics.Rating, Is.EqualTo(ComplexityRating.Moderate));

        metrics.CyclomaticComplexity = 10;
        Assert.That(metrics.Rating, Is.EqualTo(ComplexityRating.Moderate));
    }

    [Test]
    public void Rating_ReturnsComplex_WhenComplexityIs11To20()
    {
        var metrics = new MethodMetrics { CyclomaticComplexity = 11 };
        Assert.That(metrics.Rating, Is.EqualTo(ComplexityRating.Complex));

        metrics.CyclomaticComplexity = 20;
        Assert.That(metrics.Rating, Is.EqualTo(ComplexityRating.Complex));
    }

    [Test]
    public void Rating_ReturnsVeryComplex_WhenComplexityIsOver20()
    {
        var metrics = new MethodMetrics { CyclomaticComplexity = 21 };
        Assert.That(metrics.Rating, Is.EqualTo(ComplexityRating.VeryComplex));

        metrics.CyclomaticComplexity = 100;
        Assert.That(metrics.Rating, Is.EqualTo(ComplexityRating.VeryComplex));
    }
}

[TestFixture]
public class ComplexityRatingEnumTests
{
    [Test]
    public void AllRatingValues_AreDefined()
    {
        Assert.That(Enum.IsDefined(typeof(ComplexityRating), ComplexityRating.Simple), Is.True);
        Assert.That(Enum.IsDefined(typeof(ComplexityRating), ComplexityRating.Moderate), Is.True);
        Assert.That(Enum.IsDefined(typeof(ComplexityRating), ComplexityRating.Complex), Is.True);
        Assert.That(Enum.IsDefined(typeof(ComplexityRating), ComplexityRating.VeryComplex), Is.True);
    }
}

#endregion
